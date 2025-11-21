using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using static UnityEngine.UI.GridLayoutGroup;

public class NPCController : BaseCharController, IObjectPoolable, INetworkPrefabInstanceHandler
{

    public enum NPCStatesMicro
    {
        Standing,
        Walking,
        Turning,
    }
    enum NPCStatesMacro
    {
        WaypointWandering,
        FreeWandering
    }

    NPCStatesMacro macroState = NPCStatesMacro.WaypointWandering;
    NPCStatesMicro microState = NPCStatesMicro.Walking;
    
    // Public getter to expose the micro state
    public NPCStatesMicro MicroState => microState;
    
    float waypoint_time;

    // Pathway system
    private NPCPathway pathway;
    public NPCPathway Pathway => pathway;

    // Spatial grid tracking
    private Vector2Int currentCell;

    [Header("Generic NPC Behavior Settings")]
    [SerializeField] private float npcWalkingSpeed = 1f; // Walking speed of the NPC
    [SerializeField] private bool followPathCorners = false; // Toggle between following path corners or moving forward


    [Header("Path Corner Settings")]
    [SerializeField] private Vector2 cornerZoneSize = new Vector2(3f, 0.5f); // Width (perpendicular) and Depth (along path) of corner visitation zone
    public Vector2 CornerZoneSize => cornerZoneSize;

    [Header("NPC Detection Settings")]
    [SerializeField] private float detectionRadius = 4f; // Radius of the semicircle detection zone
    [SerializeField] private float detectionAngle = 270f; // Angle of detection
    [SerializeField] private bool enableNPCDetection = true; // Toggle detection on/off
    [SerializeField] private float avoidanceStrength = 1.5f; // How strongly NPCs avoid each other
    [SerializeField] private float avoidanceDistance = 1.5f; // Distance at which avoidance is at maximum
    
    [Header("NavMesh Edge Detection Settings")]
    [SerializeField] private bool enableEdgeAvoidance = true; // Toggle edge avoidance on/off
    [SerializeField] private float edgeDetectionDistance = 1.5f; // Distance to start edge avoidance
    [SerializeField] private float edgeCheckAheadDistance = 2f; // How far ahead to check for edges
    [SerializeField] private int maxRaycastAttempts = 24; // Maximum number of raycast attempts (alternating left/right)
    [SerializeField] private float raycastAngleIncrement = 5f; // Angle increment for each raycast attempt

    // Tracks detected characters and their avoidance intensity (0.0 to 1.0+)
    // Key: detected character, Value: avoidance intensity
    // If a character is in this dictionary, they are detected. If intensity > 0, they are being avoided.
    private Dictionary<BaseCharController, float> detectedCharacterIntensities = new Dictionary<BaseCharController, float>();

    // Tracks if we are currently tailgating someone (set by CalculateCharacterAvoidance)
    private bool isTailgating = false;
    
    // Tracks the closest distance to a character we're tailgating (for proportional slowdown)
    private float closestTailgatingDistance = float.MaxValue;

    // Tracks if we are currently in a head-on collision scenario (set by CalculateCharacterAvoidance)
    private bool isInHeadOnCollision = false;
    
    // Speed multiplier for head-on collisions (1.0 = full speed, 0.3 = 30% speed for less assertive)
    private float headOnSpeedReduction = 1.0f;

    // <==========================================================>
    // Public getters for edge avoidance settings
    // <==========================================================>
    public bool EnableEdgeAvoidance => enableEdgeAvoidance;
    public float EdgeDetectionDistance => edgeDetectionDistance;
    public float EdgeCheckAheadDistance => edgeCheckAheadDistance;
    public int MaxRaycastAttempts => maxRaycastAttempts;
    public float RaycastAngleIncrement => raycastAngleIncrement;
    public Dictionary<BaseCharController, float> DetectedCharacterIntensities => detectedCharacterIntensities;
    public float DetectionRadius => detectionRadius;
    public float DetectionAngle => detectionAngle;
    public float AvoidanceStrength => avoidanceStrength;
    public float AvoidanceDistance => avoidanceDistance;
    // <==========================================================>
    // Public getters for edge avoidance settings
    // <==========================================================>

    // Object Pooling
    public static ObjectPool<NPCController> objectPool = new ObjectPool<NPCController>(128);

    [Header("Object Pooling? Fintan?")]
    [SerializeField] bool _isPoolable = false;
    public bool IsPoolable { get{return _isPoolable;} set{_isPoolable=true;} }
    public bool IsPoolSpawned { get; set; } = false;
    protected override void Awake() 
    { 
        base.Awake();
        pathway = new NPCPathway(this);
        Debug.Log(NetworkManager.Singleton.PrefabHandler.AddHandler(gameObject, this));
        if (IsPoolable) objectPool.RegisterSpawnable(this); 
    }
    NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        Debug.Log("INetworkPrefabInstanceHandler.Instantiate has been called!");
        gameObject.SetActive(true);
        transform.position = position;
        transform.rotation = rotation;
        return NetworkObject;
    }

    void INetworkPrefabInstanceHandler.Destroy(NetworkObject networkObject)
    {
        Debug.Log("INetworkPrefabInstanceHandler.Destroy has been called!");
        gameObject.SetActive(false);
    }

    private void Start()
    {
        if (!IsOwner) return;

        // Register with spatial grid
        if (SpatialGrid.Instance != null)
        {
            currentCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            SpatialGrid.Instance.RegisterCharacter(this, currentCell);
        }

        NewWaypoint();
        waypoint_time = 5.0f;

    }

    protected override void FixedUpdate()
    {

        //rb.linearVelocity = Vector3.zero;
        //rb.angularVelocity = Vector3.zero;
        if (!IsOwner) { return; }
        base.FixedUpdate();

        // Update spatial grid cell if changed
        if (SpatialGrid.Instance != null)
        {
            Vector2Int newCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            if (newCell != currentCell)
            {
                SpatialGrid.Instance.UpdateCharacter(this, currentCell, newCell);
                currentCell = newCell;
            }
        }

        switch (microState)
        {
            case NPCStatesMicro.Standing:
                waypoint_time -= Time.fixedDeltaTime;
                if (waypoint_time <= 0.0f)
                {
                    NewWaypoint();
                }
                ProcessMovement(0f, 0f);
                break;
            case NPCStatesMicro.Walking:
                DecideMovement();
                break;
            case NPCStatesMicro.Turning:
                TurnUntilSafeDirection();
                break;
        }
    }

    /// <summary>
    /// Scans for nearby characters within a forward-facing semicircle detection zone.
    /// Uses the spatial grid for efficient neighbor queries.
    /// </summary>
    private void ScanForNearbyCharacters()
    {
        detectedCharacterIntensities.Clear();

        if (SpatialGrid.Instance == null) return;

        // Get all nearby characters from the spatial grid
        List<BaseCharController> nearbyCharacters = SpatialGrid.Instance.GetNearbyCharacters(currentCell);

        foreach (var character in nearbyCharacters)
        {
            // Skip self
            if (character == this) continue;

            // Detect any BaseCharController (including NPCs and Players)
            if (character == null) continue;

            // Check if within detection radius
            Vector3 toOther = character.transform.position - transform.position;
            float distance = toOther.magnitude;

            if (distance > detectionRadius) continue;

            // Check if within forward-facing semicircle (angle check)
            // Flatten to XZ plane for 2D angle calculation
            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 directionToOther = toOther;
            directionToOther.y = 0;
            directionToOther.Normalize();

            float angle = Vector3.Angle(forward, directionToOther);

            // If within the detection angle (half angle on each side)
            if (angle <= detectionAngle * 0.5f)
            {
                // Cast a ray from self to other character and check for NavMesh edge
                bool edgeBetween = false;
                int raySteps = Mathf.CeilToInt(distance / 0.5f); // step every 0.5 units
                Vector3 rayDir = (character.transform.position - transform.position).normalized;
                for (int i = 1; i < raySteps; i++)
                {
                    Vector3 samplePos = transform.position + rayDir * (i * 0.5f);
                    NavMeshHit edgeHit;
                    if (NavMesh.FindClosestEdge(samplePos, out edgeHit, NavMesh.AllAreas))
                    {
                        // If the edge is very close to the sample position, consider it blocking
                        if (edgeHit.distance < 0.2f)
                        {
                            edgeBetween = true;
                            break;
                        }
                    }
                }
                if (!edgeBetween)
                {
                    // Add detected character with initial intensity of 0.0
                    detectedCharacterIntensities[character] = 0.0f;
                }
            }
        }
    }

    /// <summary>
    /// Calculates an avoidance steering vector based on detected characters.
    /// Only avoids characters that are on a potential collision course.
    /// The closer another character is and the sooner a collision would occur, the stronger the avoidance force.
    /// Uses actual velocity from Rigidbody to determine if the other character is moving.
    /// Returns a steering vector (not normalized) representing the avoidance direction and strength.
    /// </summary>
    /// <returns>A steering vector (not normalized) representing the avoidance direction and strength</returns>
    private Vector3 CalculateCharacterAvoidance()
    {
        if (detectedCharacterIntensities.Count == 0)
            return Vector3.zero;

        Vector3 avoidanceVector = Vector3.zero;
        isTailgating = false; // Reset at the start of each calculation
        closestTailgatingDistance = float.MaxValue; // Reset closest distance
        isInHeadOnCollision = false; // Reset head-on collision state
        headOnSpeedReduction = 1.0f; // Reset speed reduction (default: full speed)

        // Create a copy of the keys to iterate over (allows safe modification of dictionary values)
        var detectedCharactersList = new List<BaseCharController>(detectedCharacterIntensities.Keys);

        foreach (BaseCharController otherCharacter in detectedCharactersList)
        {
            if (otherCharacter == null || !otherCharacter.gameObject.activeInHierarchy)
                continue;

            // Calculate direction to the other character
            Vector3 toOther = otherCharacter.transform.position - transform.position;
            toOther.y = 0; // Keep on XZ plane
            
            float distance = toOther.magnitude;
            
            if (distance < 0.01f)
            {
                Debug.LogWarning("Two characters are extremely close! Skipping avoidance calculation to avoid division by zero.");
                continue;
            }

            Vector3 toOtherNormalized = toOther / distance;

            // Get my forward direction (flattened to XZ plane)
            Vector3 myForward = transform.forward;
            myForward.y = 0;
            myForward.Normalize();

            // Get the other character's actual velocity
            Vector3 otherVelocity = otherCharacter.ActualVelocity;
            otherVelocity.y = 0; // Keep on XZ plane
            
            // Determine the other character's heading based on velocity
            // If velocity is near zero, they're stationary
            Vector3 otherHeading;
            bool otherIsStationary = otherVelocity.magnitude < 0.01f;
            
            if (otherIsStationary)
            {
                // Character is stationary - use position for heading calculation
                otherHeading = Vector3.zero;
            }
            else
            {
                // Character is moving - use velocity direction
                otherHeading = otherVelocity.normalized;
            }

            // Calculate dot product to determine if we're heading toward the other character
            // Positive dot product means we're moving toward the other character
            float myApproachDot = Vector3.Dot(myForward, toOtherNormalized);
            
            // Check if we're too close (within avoidance distance) regardless of direction
            bool isTooClose = distance < avoidanceDistance;

            if(isTooClose)
            {
                detectedCharacterIntensities[otherCharacter] = 1.0f;
            }
            
            // If we're not heading toward them AND not too close, skip avoidance
            if (myApproachDot < 0.1f && !isTooClose)
            {
                // Set intensity to 0 (detected but not avoided)
                detectedCharacterIntensities[otherCharacter] = 0.0f;
                continue;
            }

            // Calculate if the other character is heading toward us
            float otherApproachDot = 0f;
            if (!otherIsStationary)
            {
                // Only calculate approach if they're moving
                otherApproachDot = Vector3.Dot(otherHeading, -toOtherNormalized);
            }
            // If stationary, otherApproachDot stays 0, meaning they're not approaching

            // Check if we're both heading in the same direction (parallel movement)
            // Dot product close to 1.0 means same direction
            float sameDirectionDot = 0f;
            bool movingInSameDirection = false;
            if (!otherIsStationary)
            {
                sameDirectionDot = Vector3.Dot(myForward, otherHeading);
                movingInSameDirection = sameDirectionDot > 0.7f; // If heading directions are similar (within ~45 degrees)
            }

            // Check if the other character is in FRONT of us or BESIDE us
            // This is key for distinguishing tailgating (behind them) from parallel walking (beside them)
            bool otherIsInFront = myApproachDot > 0.5f; // They're significantly in front of our forward direction
            
            // Calculate perpendicular distance (how far to the side they are)
            Vector3 perpendicular = Vector3.Cross(Vector3.up, myForward);
            float lateralDistance = Mathf.Abs(Vector3.Dot(perpendicular, toOtherNormalized));
            bool otherIsBeside = lateralDistance > 0.5f; // They're significantly to the side

            // Calculate relative heading: are we on a collision course?
            // If both are moving toward each other, this will be high
            // If one is moving away or perpendicular, this will be low
            // If other is stationary, this will be based only on our approach
            float collisionThreat = myApproachDot * Mathf.Max(0f, otherApproachDot);

            // Determine if this is a head-on collision (both moving toward each other)
            // FIXED: More strict threshold (0.5 instead of 0.3) and also check that we're approaching them
            bool isHeadOn = !otherIsStationary && otherApproachDot > 0.5f && myApproachDot > 0.5f;
            
            // Determine if we're following/tailgating (behind someone moving in same direction)
            // Key change: Only tailgate if they're IN FRONT of us, we're too close, and moving same direction
            bool isTailgatingThisCharacter = isTooClose && otherIsInFront && movingInSameDirection && !otherIsBeside;

            // NEW: Check if we're moving parallel (beside someone moving in same direction)
            // In this case, maintain lateral distance but DON'T slow down
            bool parallelAndTooClose = isTooClose && movingInSameDirection && otherIsBeside;

            // For stationary characters, if we're too close we should avoid them
            bool approachingStationary = otherIsStationary && (isTooClose || myApproachDot > 0.1f);

            // If there's no significant collision threat and we're not tailgating and not approaching a stationary character and not parallel-too-close, skip
            if (collisionThreat < 0.05f && !isTailgatingThisCharacter && !approachingStationary && !parallelAndTooClose)
            {
                // Set intensity to 0 (detected but not avoided)
                detectedCharacterIntensities[otherCharacter] = 0.0f;
                continue;
            }

            // Calculate time to potential collision
            // Lower time = more urgent avoidance needed
            float relativeSpeed = npcWalkingSpeed + (otherIsStationary ? 0f : npcWalkingSpeed); // Consider if other is stationary
            float timeToCollision = distance / Mathf.Max(0.1f, relativeSpeed * Mathf.Max(0.1f, collisionThreat > 0 ? collisionThreat : myApproachDot));

            // Calculate avoidance strength based on distance
            float avoidanceFactor;
            if (distance < avoidanceDistance)
            {
                // At very close distances, use maximum avoidance
                avoidanceFactor = 1.0f;
            }
            else
            {
                // Falloff based on distance relative to detection radius
                avoidanceFactor = 1.0f - ((distance - avoidanceDistance) / (detectionRadius - avoidanceDistance));
                avoidanceFactor = Mathf.Max(0f, avoidanceFactor);
            }

            // Store the base avoidance factor before modifications for intensity tracking
            float baseAvoidanceFactor = avoidanceFactor;

            // Increase avoidance factor based on collision threat and urgency
            if (approachingStationary)
            {
                // For stationary characters, use a moderate avoidance factor
                avoidanceFactor *= Mathf.Max(0.5f, myApproachDot);
            }
            else if (parallelAndTooClose)
            {
                // For parallel movement that's too close, use moderate avoidance
                // This will create lateral separation without triggering slowdown
                avoidanceFactor *= 0.8f;
            }
            else
            {
                avoidanceFactor *= Mathf.Max(0.3f, collisionThreat); // Minimum 30% factor for tailgating
            }
            
            // Add time urgency factor (closer collision time = stronger avoidance)
            float urgencyFactor = Mathf.Clamp01(3.0f / timeToCollision); // Peaks at ~3 seconds
            avoidanceFactor *= (1.0f + urgencyFactor);

            // For head-on collisions, boost avoidance significantly
            if (isHeadOn)
            {
                avoidanceFactor *= 2.0f;
                
                // Track that we're in a head-on collision
                isInHeadOnCollision = true;
                
                // If we're less assertive, reduce our speed to yield more effectively
                if (otherCharacter.AssertivenessLevel > assertivenessLevel)
                {
                    // We are less assertive, slow down to 30% speed
                    headOnSpeedReduction = 0.3f;
                }
                // If we're more assertive, maintain full speed (already at 1.0f)
                // If equal assertiveness, both maintain full speed and move to the side
            }
            // For tailgating, apply moderate boost and track closest distance
            else if (isTailgatingThisCharacter)
            {
                avoidanceFactor *= 1.3f;
                isTailgating = true; // Set the field if we are tailgating ANYONE right now
                
                // Track the closest tailgating distance for proportional slowdown
                if (distance < closestTailgatingDistance)
                {
                    closestTailgatingDistance = distance;
                }
            }
            // For parallel movement, don't boost - just maintain lateral distance
            // (no special handling needed, avoidanceFactor is already set)

            // FIXED: Consider assertiveness but ALWAYS avoid in head-on collisions
            // In head-on situations, assertiveness only determines which side to move to, not whether to avoid
            if (!isHeadOn)
            {
                // Normal assertiveness logic (not head-on)
                if (otherCharacter.AssertivenessLevel > assertivenessLevel)
                {
                    // We are less assertive, so we yield (apply full avoidance)
                    // avoidanceFactor remains unchanged
                }
                else if (otherCharacter.AssertivenessLevel < assertivenessLevel)
                {
                    // We are more assertive, so we don't yield (skip this character)
                    // Exception: If tailgating or approaching stationary or parallel-too-close, still avoid (move to the side)
                    if (!isTailgatingThisCharacter && !approachingStationary && !parallelAndTooClose)
                    {
                        // Set intensity to 0 (detected but not avoided due to assertiveness)
                        detectedCharacterIntensities[otherCharacter] = 0.0f;
                        continue;
                    }
                }
                // If assertiveness is equal, both will avoid each other (default behavior)
            }
            // If it IS head-on, we skip the assertiveness check and ALWAYS avoid

            // Store normalized avoidance intensity (0.0 to 1.0+)
            // Intensity considers: distance, collision threat, urgency, scenario type
            float intensity = avoidanceFactor / avoidanceStrength;
            detectedCharacterIntensities[otherCharacter] = intensity;

            // Calculate avoidance direction
            Vector3 avoidanceDirection;
            
            // For head-on collisions (both moving toward each other), move to the side
            if (isHeadOn)
            {
                // Use assertiveness to deterministically choose which side to move to
                // This ensures both NPCs don't pick the same side
                Vector3 perpendicularVec = Vector3.Cross(Vector3.up, myForward);
                
                // Determine side based on assertiveness comparison
                // Lower assertiveness moves right, higher moves left
                // If equal, use relative position as tiebreaker
                bool moveRight;
                if (otherCharacter.AssertivenessLevel != assertivenessLevel)
                {
                    // Different assertiveness: less assertive moves right
                    moveRight = assertivenessLevel < otherCharacter.AssertivenessLevel;
                }
                else
                {
                    // FIXED: Equal assertiveness - move AWAY from where they are
                    // If they're on our right (sideChoice > 0), we move left (moveRight = false)
                    // If they're on our left (sideChoice < 0), we move right (moveRight = true)
                    float sideChoice = Vector3.Dot(perpendicularVec, toOtherNormalized);
                    moveRight = sideChoice < 0; // INVERTED: move opposite to their position
                }
                
                avoidanceDirection = moveRight ? perpendicularVec : -perpendicularVec;
            }
            // For tailgating scenarios, move to the side to pass
            else if (isTailgatingThisCharacter)
            {
                Vector3 perpendicularVec = Vector3.Cross(Vector3.up, myForward);
                
                // Use assertiveness to choose passing side
                // More assertive passes on the left, less assertive on the right
                bool moveRight = assertivenessLevel <= otherCharacter.AssertivenessLevel;
                
                avoidanceDirection = moveRight ? perpendicularVec : -perpendicularVec;
                
                // Also add a slight backward component to maintain distance while tailgating
                // This helps maintain avoidanceDistance when following directly behind
                if (distance < avoidanceDistance)
                {
                    // Mix lateral avoidance with backward push to maintain distance
                    float backwardComponent = (avoidanceDistance - distance) / avoidanceDistance;
                    avoidanceDirection = avoidanceDirection * 0.7f + (-toOtherNormalized) * backwardComponent * 0.3f;
                    avoidanceDirection.Normalize();
                }
            }
            // For parallel movement (same direction, too close), move to the side ONLY
            else if (parallelAndTooClose)
            {
                Vector3 perpendicularVec = Vector3.Cross(Vector3.up, myForward);
                
                // Choose the side based on which side they're on relative to our path
                float sideChoice = Vector3.Dot(perpendicularVec, toOtherNormalized);
                bool moveRight = sideChoice > 0;
                
                avoidanceDirection = moveRight ? -perpendicularVec : perpendicularVec;
                
                // IMPORTANT: No backward component - we want to maintain speed
                // Just create lateral separation
            }
            // For stationary characters, move to the side
            else if (approachingStationary)
            {
                Vector3 perpendicularVec = Vector3.Cross(Vector3.up, myForward);
                
                // Choose the side based on which side they're on relative to our path
                float sideChoice = Vector3.Dot(perpendicularVec, toOtherNormalized);
                bool moveRight = sideChoice > 0;
                
                avoidanceDirection = moveRight ? -perpendicularVec : perpendicularVec;
            }
            else
            {
                // For other scenarios, move directly away
                avoidanceDirection = -toOtherNormalized;
            }

            // Add weighted avoidance vector
            avoidanceVector += avoidanceDirection * avoidanceFactor;
        }

        // Apply overall avoidance strength
        avoidanceVector *= avoidanceStrength;

        return avoidanceVector;
    }

    private void NewWaypoint()
    {
        if (pathway.GenerateNewWaypoint(out float newWaypointTime))
        {
            waypoint_time = newWaypointTime;
            microState = NPCStatesMicro.Walking;
        }
    }

    /// <summary>
    /// Calculates the target direction for movement based on path corners or forward direction.
    /// When following path corners, aims for the closest point in the corner zone (center or edges).
    /// </summary>
    /// <returns>Normalized direction vector to move towards</returns>
    private Vector3 CalculateTargetDirection()
    {
        if (followPathCorners)
        {
            return pathway.CalculateTargetDirection();
        }
        else
        {
            // Just move forward in current direction
            return transform.forward.normalized;
        }
    }

    /// <summary>
    /// Handles edge avoidance by checking for safe directions and adjusting rotation/speed.
    /// </summary>
    /// <param name="rotationDir">Reference to rotation direction to modify</param>
    /// <param name="movementSpeed">Reference to movement speed to modify</param>
    /// <returns>True if movement should continue, false if no safe direction found</returns>
    private bool HandleEdgeAvoidance(ref float rotationDir, ref float movementSpeed)
    {
        if (!enableEdgeAvoidance)
        {
            return true; // Continue with normal movement
        }

        // Check if there's an edge directly ahead (using current forward direction)
        if (!IsDirectionSafe())
        {
            // Find a safe angle to turn to
            float safeAngle = FindSafeAngleFromEdge();
            
            if (safeAngle != 0f)
            {
                // Convert angle to rotation direction for ProcessMovement
                rotationDir = Mathf.Sign(safeAngle);
                movementSpeed = npcWalkingSpeed * 0.75f;
            }
            else
            {
                // No safe direction found after all attempts: stop movement
                movementSpeed = 0f;
                // Try turning right as a fallback
                rotationDir = 1f;
                Debug.LogWarning($"[{name}] No safe direction found due to edges, stopping movement and turning right THIS NEEDS TO BE OVERRIDEN AT SOME POINT");
            }
        }
        
        return true; // Continue with movement
    }

    private void DecideMovement()
    {
        // Only do pathway-related checks if we're following path corners
        if (followPathCorners)
        {
            //redundant check if at end of pathway
            if (pathway.GetCornerCount() == 0)
            {
                //we made it to the end of the path
                microState = NPCStatesMicro.Standing;
                return;
            }

            // Check if the next corner is behind the NPC - if so, stop and turn around
            if (pathway.IsNextCornerBehind())
            {
                microState = NPCStatesMicro.Turning;
                Debug.Log($"[{name}] Next corner is behind, entering Turning state");
                return;
            }

            // Check if NPC is inside the corner visitation zone
            if (pathway.ProcessCornerVisitation())
            {
                //we made it to the end of the path
                microState = NPCStatesMicro.Standing;
                return;
            }

            // Check if we're closer to the next corner than the current one (corner-cutting optimization)
            if (pathway.ProcessCornerCuttingOptimization())
            {
                return; // Exit early - path has been recalculated, will use new path next frame
            }
        }

        // Scan for nearby NPCs
        if (enableNPCDetection)
        {
            ScanForNearbyCharacters();
        }

        // Calculate character avoidance force
        Vector3 avoidanceForce = CalculateCharacterAvoidance();

        // Calculate rotation direction and movement speed
        float rotationDir = 0f;
        float movementSpeed = npcWalkingSpeed;

        // Calculate desired direction to next corner or forward
        Vector3 toCorner = CalculateTargetDirection();

        // Apply avoidance steering
        Vector3 desiredDirection = toCorner + avoidanceForce;
        desiredDirection.y = 0;
        desiredDirection.Normalize();

        // Calculate rotation needed to face desired direction
        if (desiredDirection.magnitude > 0.01f)
        {
            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            // Calculate signed angle between current forward and desired direction
            float angleToDesired = Vector3.SignedAngle(forward, desiredDirection, Vector3.up);

            // Convert angle to rotation direction (-1 to 1)
            rotationDir = Mathf.Clamp(angleToDesired / 45f, -1f, 1f);
        }

        // Adjust speed based on situation
        if (isTailgating)
        {
            // Calculate proportional slowdown based on distance to the closest character we're tailgating
            // At half avoidanceDistance (e.g., 0.75m): stop completely (0%)
            // At full avoidanceDistance (e.g., 1.5m): slow to 50%
            // Between these: linear interpolation
            
            float minSlowdownDistance = avoidanceDistance * 0.5f; // Distance at which we stop (0% speed)
            float maxSlowdownDistance = avoidanceDistance;         // Distance at which we're at 50% speed
            
            // Clamp the distance to the range
            float clampedDistance = Mathf.Clamp(closestTailgatingDistance, minSlowdownDistance, maxSlowdownDistance);
            
            // Calculate speed multiplier: 0.0 at minSlowdownDistance, 0.5 at maxSlowdownDistance
            float speedMultiplier = Mathf.Lerp(0.0f, 0.5f, (clampedDistance - minSlowdownDistance) / (maxSlowdownDistance - minSlowdownDistance));
            
            movementSpeed *= speedMultiplier;
        }
        else if (isInHeadOnCollision && headOnSpeedReduction > 0f)
        {
            // Apply head-on collision speed reduction (set by CalculateCharacterAvoidance)
            movementSpeed *= headOnSpeedReduction;
        }
        else if (avoidanceForce.magnitude > 0.1f)
        {
            //movementSpeed *= 0.7f; // Slow down to 70% when avoiding
        }

        // Check for edge avoidance
        if (!HandleEdgeAvoidance(ref rotationDir, ref movementSpeed))
        {
            return; // State changed to Turning, don't execute movement
        }

        // Execute movement with the calculated rotation
        ProcessMovement(movementSpeed, rotationDir);
    }

    /// <summary>
    /// Finds a safe angle to turn away from the edge by alternating left and right radially.
    /// Starts at 0°, then checks 5°, -5°, 10°, -10°, 15°, -15°, etc.
    /// Returns immediately upon finding the first safe direction.
    /// </summary>
    /// <returns>The safe angle in degrees (positive = right, negative = left), or 0 if none found</returns>
    private float FindSafeAngleFromEdge()
    {
        Vector3 currentForward = transform.forward;
        currentForward.y = 0;
        currentForward.Normalize();

        // Start checking at increments of raycastAngleIncrement degrees
        for (int i = 1; i <= maxRaycastAttempts; i++)
        {
            float angle = raycastAngleIncrement * i;
            
            // Alternate: try right (positive), then left (negative)
            // i=1: +5°, i=2: -5°, i=3: +10°, i=4: -10°, etc.
            float checkAngle = (i % 2 == 1) ? angle : -angle;
            
            // Get the direction at this angle
            Vector3 checkDirection = Quaternion.Euler(0, checkAngle, 0) * currentForward;
            
            // Check if this direction is safe - return immediately if found
            if (IsDirectionSafe(checkDirection))
            {
                //Debug.Log($"[{name}] Found safe direction at {checkAngle}° ({(checkAngle > 0 ? "right" : "left")}) after {i} attempts");
                return checkAngle;
            }
        }

        // No safe direction found after all attempts
        Debug.LogWarning($"[{name}] No safe direction found after {maxRaycastAttempts} attempts");
        return 0f;
    }

    /// <summary>
    /// Checks if a given direction is safe (no NavMesh edge in that direction).
    /// Uses NavMesh.Raycast to ensure no walls or obstacles block the path.
    /// </summary>
    /// <param name="direction">The direction to check (should be normalized). If null, uses transform.forward.</param>
    /// <returns>True if the direction is safe, false otherwise</returns>
    private bool IsDirectionSafe(Vector3? direction = null)
    {
        // Use forward direction if no direction provided
        Vector3 checkDirection = direction ?? transform.forward;
        checkDirection.y = 0;
        checkDirection.Normalize();

        Vector3 checkPosition = transform.position + checkDirection * edgeCheckAheadDistance;

        // First check: Use NavMesh.Raycast to check if there's a clear path (no obstacles/walls)
        NavMeshHit raycastHit;
        if (NavMesh.Raycast(transform.position, checkPosition, out raycastHit, NavMesh.AllAreas))
        {
            // Raycast hit something - there's an obstacle or edge in this direction
            return false;
        }

        // Second check: Ensure the end position is still on NavMesh with tight tolerance
        NavMeshHit sampleHit;
        if (!NavMesh.SamplePosition(checkPosition, out sampleHit, 0.5f, NavMesh.AllAreas))
        {
            // Position is off NavMesh - not safe
            return false;
        }

        // Third check: Verify the sampled position isn't too close to an edge
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(sampleHit.position, out edgeHit, NavMesh.AllAreas))
        {
            // Safe if the edge is far enough away
            return edgeHit.distance >= edgeDetectionDistance;
        }

        // If we can't find an edge, assume it's safe
        return true;
    }

    /// <summary>
    /// Continuously turns the NPC until the next corner is in front of them.
    /// Turns in the most efficient direction (shortest rotation path) to face the corner.
    /// Once the corner is in front, transitions back to Walking state.
    /// </summary>
    private void TurnUntilSafeDirection()
    {
        // Check if the corner is now in front of us
        bool cornerIsInFront = !pathway.IsNextCornerBehind();

        if (cornerIsInFront)
        {
            // Corner is now in front, return to walking state
            microState = NPCStatesMicro.Walking;
            Debug.Log($"[{name}] Corner is now in front, returning to Walking state");
            return;
        }

        // Calculate which direction to turn (shortest path to face the corner)
        Vector3 toCorner = pathway.PathCorners[0] - transform.position;
        toCorner.y = 0;
        toCorner.Normalize();

        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        // Calculate signed angle to determine turn direction
        // Positive = turn right, Negative = turn left
        float signedAngle = Vector3.SignedAngle(forward, toCorner, Vector3.up);
        
        // Determine rotation direction based on signed angle
        float rotationDir = signedAngle > 0 ? 1f : -1f;

        // Continue turning in the calculated direction (no forward movement)
        ProcessMovement(0f, rotationDir);
    }

    [Rpc(SendTo.Everyone)]
    public void AssignSlotRpc(int materialIndex)
    {
        Material playerMaterial = BountyManager.Instance.playerMaterials[materialIndex];

        //Find next available spot in list
        for (int i = 0; i < NPCManager.Instance.npcList.Length; i++)
        {
            if (NPCManager.Instance.npcList[i] == null)
            {
                NPCManager.Instance.npcList[i] = this;
                this.name = "" + playerMaterial.name + " " + (i + 1);
                break;
            }
        }
    }

    // TODO -> Run this upon npc death or removal
    private void UnregisterFromGrid()
    {
        // Unregister from spatial grid
        if (SpatialGrid.Instance != null)
        {
            SpatialGrid.Instance.UnregisterCharacter(this, currentCell);
        }
    }

    /// <summary>
    /// FOR DEBUG PURPOSES ONLY: Spawns a marker at the given position with order and label info.
    /// SHOULD ONLY BE USED WITH ONE NPC TO AVOID MARKER OVERLOAD.
    /// </summary>
    /// <param name="spawnPos"></param>
    /// <param name="order"></param>
    /// <param name="label"></param>
    public void SpawnDebugMarker(Vector3 spawnPos, int order, string label)
    {
        //GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //cube.transform.localScale = Vector3.one * 0.5f;
        //cube.transform.position = spawnPos;
        //cube.name = order + label;
    }

}
