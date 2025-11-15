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
    
    Vector3 current_waypoint;
    float waypoint_time;
    private List<Vector3> pathCorners = new List<Vector3>();
    public List<Vector3> PathCorners => pathCorners;

    // Spatial grid tracking
    private Vector2Int currentCell;
    
    // Previous corner position for zone orientation
    private Vector3 previousCornerPosition;
    public Vector3 PreviousCornerPosition => previousCornerPosition;

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
    [SerializeField] private float edgeTurnSpeed = 2f; // Speed multiplier when turning away from edges...should I have this?

    // Tracks detected characters and their avoidance intensity (0.0 to 1.0+)
    // Key: detected character, Value: avoidance intensity
    // If a character is in this dictionary, they are detected. If intensity > 0, they are being avoided.
    private Dictionary<BaseCharController, float> detectedCharacterIntensities = new Dictionary<BaseCharController, float>();

    // Tracks if we are currently tailgating someone (set by CalculateCharacterAvoidance)
    private bool isTailgating = false;

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

    private float pathEdgeBuffer = 0.6f; // Distance to keep from edges when adjusting corners

    // Object Pooling
    public static ObjectPool<NPCController> objectPool = new ObjectPool<NPCController>(128);

    [Header("Object Pooling? Fintan?")]
    [SerializeField] bool _isPoolable = false;
    public bool IsPoolable { get{return _isPoolable;} set{_isPoolable=true;} }
    public bool IsPoolSpawned { get; set; } = false;
    protected override void Awake() 
    { 
        base.Awake();
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

            // Calculate relative heading: are we on a collision course?
            // If both are moving toward each other, this will be high
            // If one is moving away or perpendicular, this will be low
            // If other is stationary, this will be based only on our approach
            float collisionThreat = myApproachDot * Mathf.Max(0f, otherApproachDot);

            // Determine if this is a head-on collision (both moving toward each other)
            bool isHeadOn = !otherIsStationary && otherApproachDot > 0.3f;
            
            // Determine if we're following/tailgating (both moving in similar direction but too close)
            bool isTailgatingThisCharacter = isTooClose && myApproachDot > 0.1f && otherApproachDot < 0.3f;

            // For stationary characters, if we're too close we should avoid them
            bool approachingStationary = otherIsStationary && (isTooClose || myApproachDot > 0.1f);

            // If there's no significant collision threat and we're not tailgating and not approaching a stationary character, skip
            if (collisionThreat < 0.05f && !isTailgatingThisCharacter && !approachingStationary)
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
            }
            // For tailgating, apply moderate boost
            else if (isTailgatingThisCharacter)
            {
                avoidanceFactor *= 1.3f;
                isTailgating = true; // Set the field if we are tailgating ANYONE right now
            }

            // Consider assertiveness: binary decision - either yield or don't yield
            if (otherCharacter.AssertivenessLevel > assertivenessLevel)
            {
                // We are less assertive, so we yield (apply full avoidance)
                // avoidanceFactor remains unchanged
            }
            else if (otherCharacter.AssertivenessLevel < assertivenessLevel)
            {
                // We are more assertive, so we don't yield (skip this character)
                // Exception: If tailgating or approaching stationary, still avoid (move to the side)
                if (!isTailgatingThisCharacter && !approachingStationary)
                {
                    // Set intensity to 0 (detected but not avoided due to assertiveness)
                    detectedCharacterIntensities[otherCharacter] = 0.0f;
                    continue;
                }
            }
            // If assertiveness is equal, both will avoid each other (default behavior)

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
                Vector3 perpendicular = Vector3.Cross(Vector3.up, myForward);
                
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
                    // Equal assertiveness: use position as tiebreaker
                    // Check which side the other character is on relative to our forward direction
                    float sideChoice = Vector3.Dot(perpendicular, toOtherNormalized);
                    moveRight = sideChoice > 0;
                }
                
                avoidanceDirection = moveRight ? perpendicular : -perpendicular;
            }
            // For tailgating scenarios, move to the side to pass
            else if (isTailgatingThisCharacter)
            {
                Vector3 perpendicular = Vector3.Cross(Vector3.up, myForward);
                
                // Use assertiveness to choose passing side
                // More assertive passes on the left, less assertive on the right
                bool moveRight = assertivenessLevel <= otherCharacter.AssertivenessLevel;
                
                avoidanceDirection = moveRight ? perpendicular : -perpendicular;
            }
            // For stationary characters, move to the side
            else if (approachingStationary)
            {
                Vector3 perpendicular = Vector3.Cross(Vector3.up, myForward);
                
                // Choose the side based on which side they're on relative to our path
                float sideChoice = Vector3.Dot(perpendicular, toOtherNormalized);
                bool moveRight = sideChoice > 0;
                
                avoidanceDirection = moveRight ? -perpendicular : perpendicular;
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
        var zones = NPCManager.Instance.gatheringZones;
        if (zones == null || zones.Length == 0) return;

        // Pick a random gathering zone
        MapZone zone = zones[Random.Range(0, zones.Length)];

        // Use a random point within the zone as the waypoint
        Vector3 targetPoint = zone.GetRandomPointInArea();

        NavMeshHit hit;
        if (NavMesh.SamplePosition(targetPoint, out hit, 5f, NavMesh.AllAreas))
        {
            current_waypoint = hit.position;
            NavMeshPath pathReturned = new NavMeshPath();
            NavMesh.CalculatePath(transform.position, current_waypoint, NavMesh.AllAreas, pathReturned);
            pathCorners = new List<Vector3>(pathReturned.corners);
            pathCorners = AdjustCornersAwayFromEdges(pathCorners);
            previousCornerPosition = transform.position;
            waypoint_time = Random.Range(3.0f, 12.0f);
            microState = NPCStatesMicro.Walking;
        }
    }

    /// <summary>
    /// Adjusts path corners to maintain a minimum distance from NavMesh edges.
    /// Samples in multiple directions around each corner to find positions further from edges.
    /// </summary>
    /// <param name="corners">Original path corners from NavMesh</param>
    /// <param name="minDistanceFromEdge">Minimum desired distance from edges</param>
    /// <returns>List of adjusted corner positions</returns>
    private List<Vector3> AdjustCornersAwayFromEdges(List<Vector3> corners)
    {
        List<Vector3> adjustedCorners = new List<Vector3>();

        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 corner = corners[i];
            Vector3 adjustedCorner = corner;
            
            // Check if this corner is too close to an edge
            NavMeshHit edgeHit;
            if (NavMesh.FindClosestEdge(corner, out edgeHit, NavMesh.AllAreas))
            {
                float distToEdge = edgeHit.distance;

                SpawnMarker(edgeHit.position, i, " Edge Hit. Too Close?");

                // Check if the corner is too close to the edge.
                if (distToEdge < pathEdgeBuffer)
                {
                    SpawnMarker(corner, i, " TOO CLOSE!!!");

                    // Calculate direction away from edge
                    Vector3 pushDirection = edgeHit.normal;
                    
                    // Calculate how much farther we need to push (plus a little to offset)
                    float deficit = (pathEdgeBuffer - distToEdge) + 0.2f;
                    
                    // Calculate the new position
                    Vector3 newPosition = corner + pushDirection * deficit;

                    SpawnMarker(newPosition, i, " New Position.");

                    // Re-sample the new position on the NavMesh to ensure it is valid.
                    NavMeshHit newHit;
                    if (NavMesh.SamplePosition(newPosition, out newHit, pathEdgeBuffer * 2, NavMesh.AllAreas))
                    {
                        adjustedCorner = AdjustForOverCorrection(newHit.position, i);
                        SpawnMarker(adjustedCorner, i, " Corrected Pos");
                    }
                }
            }
            
            adjustedCorners.Add(adjustedCorner);
        }
        
        return adjustedCorners;
    }

    private Vector3 AdjustForOverCorrection(Vector3 generatedPoint, int order)
    {
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(generatedPoint, out edgeHit, NavMesh.AllAreas))
        {
            float distToEdge = edgeHit.distance;

            SpawnMarker(edgeHit.position, order, " Edge Hit. Overcorrection?");

            if (distToEdge < pathEdgeBuffer)
            {
                //Generate a new point, then take the average of the two
                SpawnMarker(edgeHit.position, order, " TOO CLOSE!!! OverCorrection!");

                // Calculate direction away from edge
                Vector3 pushDirection = edgeHit.normal;

                // Calculate how much farther we need to push
                float deficit = pathEdgeBuffer - distToEdge;

                // Calculate the new position (in between corrected position and newly generated position)
                Vector3 newPosition = ((generatedPoint + pushDirection * deficit) + generatedPoint) / 2;

                SpawnMarker(newPosition, order, " New Position. Corrected!");

                // Re-sample the new position on the NavMesh to ensure it is valid.
                NavMeshHit newHit;
                if (NavMesh.SamplePosition(newPosition, out newHit, pathEdgeBuffer * 2, NavMesh.AllAreas))
                {
                    return newHit.position;
                }
            }
            else
            {
                return generatedPoint;
            }

        }
        throw new System.Exception("AdjustForOverCorrection failed to find edge!");
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
            // Calculate the closest point in the corner zone to aim for
            Vector3 cornerPosition = pathCorners[0];
            Vector3 npcPosition = transform.position;
            
            // Flatten to XZ plane
            cornerPosition.y = 0;
            npcPosition.y = 0;
            
            // Calculate path direction
            Vector3 prevCorner = previousCornerPosition;
            prevCorner.y = 0;
            Vector3 pathDirection = (cornerPosition - prevCorner).normalized;
            
            // Calculate perpendicular direction (left/right of path)
            Vector3 perpendicular = Vector3.Cross(pathDirection, Vector3.up).normalized;
            
            // Get half width of the corner zone
            float halfWidth = cornerZoneSize.x * 0.5f;
            
            // Three candidate points: center, left edge, right edge of corner zone
            Vector3 centerPoint = cornerPosition;
            Vector3 leftPoint = cornerPosition + perpendicular * halfWidth;
            Vector3 rightPoint = cornerPosition - perpendicular * halfWidth;
            
            // Find which point is closest
            float distToCenter = Vector3.Distance(npcPosition, centerPoint);
            float distToLeft = Vector3.Distance(npcPosition, leftPoint);
            float distToRight = Vector3.Distance(npcPosition, rightPoint);
            
            Vector3 targetPoint = centerPoint;
            if (distToLeft < distToCenter && distToLeft < distToRight)
            {
                targetPoint = leftPoint;
            }
            else if (distToRight < distToCenter && distToRight < distToLeft)
            {
                targetPoint = rightPoint;
            }
            
            Vector3 toCorner = targetPoint - npcPosition;
            toCorner.y = 0;
            return toCorner.normalized;
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
    /// <returns>True if movement should continue, false if state changed to Turning</returns>
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
                // No safe direction found after all attempts: enter Turning state
                microState = NPCStatesMicro.Turning;
                Debug.Log($"[{name}] No safe direction found due to edges, entering Turning state");
                return false; // Don't continue with movement
            }
        }
        
        return true; // Continue with normal movement
    }

    private void DecideMovement()
    {
        //check if at end of pathway
        if (pathCorners.Count == 0)
        {
            //we made it to the end of the path
            microState = NPCStatesMicro.Standing;
            return;
        }

        // Check if NPC is inside the corner visitation zone
        if (IsInsideCornerZone(0))
        {
            // Update previous corner position before removing the corner
            previousCornerPosition = pathCorners[0];
            pathCorners.RemoveAt(0);
            //check if at end of pathway AGAIN
            if (pathCorners.Count == 0)
            {
                //we made it to the end of the path
                microState = NPCStatesMicro.Standing;
                return;
            }
        }

        // Check if we're closer to the next corner than the current one (corner-cutting optimization)
        if (pathCorners.Count > 1)
        {
            float distToCurrent = Vector3.Distance(pathCorners[0], transform.position);
            float distToNext = Vector3.Distance(pathCorners[1], transform.position);
            float distFromCurrentToNext = Vector3.Distance(pathCorners[0], pathCorners[1]);

            if (distToNext < distToCurrent)
            {
                // Skip the current corner since we're already closer to the next one
                previousCornerPosition = pathCorners[0];
                pathCorners.RemoveAt(0);
            }
            else if(distToNext < distFromCurrentToNext)
            {
                // Skip the current corner since we're on our way to the next one
                // This could cause NPCs to walk through walls - recalculate path to prevent this
                UnityEngine.Debug.Log($"[{name}] Corner skipped, recalculating path to current waypoint to avoid walls");
                RecalculatePathToCurrentWaypoint();
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
            movementSpeed *= 0.5f; // Slow down to 50% when tailgating
        }
        else if (avoidanceForce.magnitude > 0.1f)
        {
            movementSpeed *= 0.7f; // Slow down to 70% when avoiding
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
    /// Recalculates the path to the current waypoint without changing the destination.
    /// Used when the NPC skips a corner to prevent potential wall-walking.
    /// </summary>
    private void RecalculatePathToCurrentWaypoint()
    {
        if (current_waypoint == Vector3.zero)
        {
            Debug.LogWarning($"[{name}] Cannot recalculate path - no current waypoint set");
            return;
        }

        NavMeshPath pathReturned = new NavMeshPath();
        if (NavMesh.CalculatePath(transform.position, current_waypoint, NavMesh.AllAreas, pathReturned))
        {
            pathCorners = new List<Vector3>(pathReturned.corners);
            pathCorners = AdjustCornersAwayFromEdges(pathCorners);
            previousCornerPosition = transform.position;
            Debug.Log($"[{name}] Path recalculated successfully with {pathCorners.Count} corners");
        }
        else
        {
            Debug.LogWarning($"[{name}] Failed to recalculate path to current waypoint");
        }
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
                Debug.Log($"[{name}] Found safe direction at {checkAngle}° ({(checkAngle > 0 ? "right" : "left")}) after {i} attempts");
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
    /// Continuously turns the NPC right until a safe direction for movement is found.
    /// Once a safe direction is found, transitions back to Walking state.
    /// </summary>
    private void TurnUntilSafeDirection()
    {
        // Check if current forward direction is safe
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        if (IsDirectionSafe(forward))
        {
            // Found a safe direction, return to walking state
            microState = NPCStatesMicro.Walking;
            Debug.Log($"[{name}] Found safe direction, returning to Walking state");
            return;
        }

        // Continue turning right (no forward movement)
        ProcessMovement(0f, 1f);
    }

    private bool IsInsideCornerZone(int cornerIndex)
    {
        if (cornerIndex >= pathCorners.Count)
            return false;

        Vector3 cornerPosition = pathCorners[cornerIndex];
        Vector3 npcPosition = transform.position;

        // Flatten positions to XZ plane
        cornerPosition.y = 0;
        npcPosition.y = 0;

        // Calculate the direction from previous corner position to this corner
        Vector3 prevCorner = previousCornerPosition;
        prevCorner.y = 0;
        Vector3 pathDirection = (cornerPosition - prevCorner).normalized;

        // Calculate local position of NPC relative to corner
        Vector3 toNPC = npcPosition - cornerPosition;

        // Calculate perpendicular direction (left/right of path)
        Vector3 perpendicular = Vector3.Cross(pathDirection, Vector3.up).normalized;

        // Project NPC position onto path direction and perpendicular
        float alongPath = Vector3.Dot(toNPC, pathDirection);
        float acrossPath = Vector3.Dot(toNPC, perpendicular);

        // Check if within zone bounds
        // alongPath: distance along the path direction (depth of zone)
        // acrossPath: distance perpendicular to path (width of zone)
        float halfDepth = cornerZoneSize.y * 0.5f;
        float halfWidth = cornerZoneSize.x * 0.5f;

        bool withinDepth = Mathf.Abs(alongPath) <= halfDepth;
        bool withinWidth = Mathf.Abs(acrossPath) <= halfWidth;

        return withinDepth && withinWidth;
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
    private void SpawnMarker(Vector3 spawnPos, int order, string label)
    {
        //GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //cube.transform.localScale = Vector3.one * 0.5f;
        //cube.transform.position = spawnPos;
        //cube.name = order + label;
    }

}
