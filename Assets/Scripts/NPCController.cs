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

    [Header("RVO Settings")]
    [Tooltip("Time horizon for collision prediction (2-4 seconds typical).")]
    [SerializeField] private float rvoTimeHorizon = 2.5f;
    [Tooltip("Agent radius for collision calculation.")]
    [SerializeField] private float rvoAgentRadius = 0.75f;
    [Tooltip("Responsibility factor: 0.5 = symmetric (both agents adjust equally)")]
    [SerializeField] [UnityEngine.Range(0.1f, 0.9f)] private float rvoResponsibility = 0.5f;
    [Tooltip("Flow field bias: Higher values encourage lane formation by biasing toward neighbor flow directions")]
    [SerializeField] [UnityEngine.Range(0f, 1f)] private float rvoFlowBias = 0.3f;
    
    [Header("RVO Advanced Settings")]
    [Tooltip("Enable adaptive time horizon that adjusts based on crowd density")]
    [SerializeField] private bool useAdaptiveTimeHorizon = true;
    [Tooltip("Minimum time horizon in sparse areas (seconds)")]
    [SerializeField] private float minTimeHorizon = 1.5f;
    [Tooltip("Maximum time horizon in dense areas (seconds)")]
    [SerializeField] private float maxTimeHorizon = 3.5f;
    [Tooltip("Personal space multiplier: increases effective radius to maintain comfortable distance")]
    [SerializeField] [UnityEngine.Range(1.0f, 2.0f)] private float personalSpaceMultiplier = 1.2f;
    [Tooltip("Velocity smoothing factor: higher = smoother but less responsive (0 = no smoothing)")]
    [SerializeField] [UnityEngine.Range(0f, 0.8f)] private float velocitySmoothingFactor = 0.3f;

    private Vector2 currentRVOVelocity;
    private Vector2 preferredRVOVelocity;
    private List<RVOSystem.AgentData> rvoNeighbors;
    private Vector2 previousRVOVelocity; // For smoothing
    private float currentCrowdDensity; // For adaptive time horizon

    // Debug visualization for RVO - stores the last evaluation results
    private RVOSystem.RVODebugData lastRVODebugData;
    private bool hasRVODebugData = false;

    public Vector2 CurrentRVOVelocity => currentRVOVelocity;
    public Vector2 PreferredRVOVelocity => preferredRVOVelocity;
    public float NpcWalkingSpeed => npcWalkingSpeed;
    public float RvoFlowBias => rvoFlowBias;
    public RVOSystem.RVODebugData LastRVODebugData => lastRVODebugData;
    public bool HasRVODebugData => hasRVODebugData;
    public float RvoAgentRadius => rvoAgentRadius;
    public float PersonalSpaceMultiplier => personalSpaceMultiplier;
    public float RvoResponsibility => rvoResponsibility;
    public bool UseAdaptiveTimeHorizon => useAdaptiveTimeHorizon;
    public float CurrentCrowdDensity => currentCrowdDensity;
    
    [Header("NPC Detection Settings")]
    [SerializeField] private float detectionRadius = 4f; // Radius of the semicircle detection zone
    [SerializeField] private float detectionAngle = 270f; // Angle of detection
    [SerializeField] private bool enableNPCDetection = true; // Toggle detection on/off
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
        rvoNeighbors = new List<RVOSystem.AgentData>();
        previousRVOVelocity = Vector2.zero;
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
    /// Prepares the list of RVO neighbors from detected characters.
    /// Converts BaseCharController data to RVOSystem.AgentData format.
    /// </summary>
    /// <returns>List of RVO agent data for neighbors</returns>
    private List<RVOSystem.AgentData> PrepareRVONeighbors()
    {
        rvoNeighbors.Clear();
        foreach (var kvp in detectedCharacterIntensities)
        {
            BaseCharController character = kvp.Key;
            if (character != null && character.gameObject.activeInHierarchy)
            {
                rvoNeighbors.Add(new RVOSystem.AgentData(
                    character.transform.position,
                    character.ActualVelocity,
                    rvoAgentRadius
                ));
            }
        }
        return rvoNeighbors;
    }

    /// <summary>
    /// Calculates the average flow direction of nearby agents moving in similar directions.
    /// This encourages lane formation by biasing toward maintaining parallel movement.
    /// </summary>
    /// <param name="myDirection">Current desired direction of movement</param>
    /// <returns>Flow-adjusted direction vector (or original if no flow detected)</returns>
    private Vector2 CalculateFlowField(Vector2 myDirection)
    {
        if (rvoFlowBias <= 0.01f || detectedCharacterIntensities.Count == 0)
        {
            return myDirection; // No flow bias, return original direction
        }

        Vector2 flowSum = Vector2.zero;
        int flowCount = 0;
        float mySpeed = myDirection.magnitude;

        foreach (var kvp in detectedCharacterIntensities)
        {
            BaseCharController character = kvp.Key;
            if (character == null || !character.gameObject.activeInHierarchy)
                continue;

            Vector3 otherVel3D = character.ActualVelocity;
            Vector2 otherVel = new Vector2(otherVel3D.x, otherVel3D.z);

            // Only consider agents that are moving
            if (otherVel.magnitude < 0.1f)
                continue;

            Vector2 otherDir = otherVel.normalized;
            float alignment = Vector2.Dot(myDirection.normalized, otherDir);

            // Only consider agents moving in a similar direction (alignment > 0.5 means within ~60 degrees)
            if (alignment > 0.5f)
            {
                // Weight by alignment - more aligned neighbors have more influence
                flowSum += otherDir * alignment;
                flowCount++;
            }
        }

        if (flowCount == 0)
        {
            return myDirection; // No flow detected
        }

        // Calculate average flow direction
        Vector2 flowDirection = (flowSum / flowCount).normalized;

        // Blend between desired direction and flow direction based on flow bias
        Vector2 blendedDirection = Vector2.Lerp(myDirection.normalized, flowDirection, rvoFlowBias);
        
        // Restore original speed
        return blendedDirection * mySpeed;
    }

    /// <summary>
    /// Calculates local crowd density based on nearby agents.
    /// Returns a normalized value from 0 (sparse) to 1 (dense).
    /// </summary>
    private float CalculateCrowdDensity()
    {
        if (detectedCharacterIntensities.Count == 0)
            return 0f;

        // Density based on number of neighbors and their proximity
        float densityScore = 0f;
        float totalWeight = 0f;

        foreach (var kvp in detectedCharacterIntensities)
        {
            BaseCharController character = kvp.Key;
            if (character == null || !character.gameObject.activeInHierarchy)
                continue;

            float distance = Vector3.Distance(transform.position, character.transform.position);
            
            // Weight by inverse distance (closer = higher contribution to density)
            float weight = 1f - Mathf.Clamp01(distance / detectionRadius);
            densityScore += weight;
            totalWeight += 1f;
        }

        if (totalWeight > 0f)
        {
            // Normalize by maximum possible neighbors in detection radius
            float maxNeighbors = 8f; // Assume max 8 neighbors for normalization
            return Mathf.Clamp01(densityScore / maxNeighbors);
        }

        return 0f;
    }

    /// <summary>
    /// Calculates adaptive time horizon based on crowd density.
    /// Dense crowds use longer time horizons for smoother avoidance.
    /// </summary>
    private float GetAdaptiveTimeHorizon()
    {
        if (!useAdaptiveTimeHorizon)
            return rvoTimeHorizon;

        currentCrowdDensity = CalculateCrowdDensity();

        // Lerp between min and max based on density
        return Mathf.Lerp(minTimeHorizon, maxTimeHorizon, currentCrowdDensity);
    }

    // Make this public so NPCGizmoGenerator can call it
    public float GetAdaptiveTimeHorizonPublic()
    {
        return GetAdaptiveTimeHorizon();
    }

    // Make this public so NPCGizmoGenerator can call it
    public Vector2 CalculateFlowFieldPublic(Vector2 myDirection)
    {
        return CalculateFlowField(myDirection);
    }

    /// <summary>
    /// Applies velocity smoothing to reduce jitter and oscillation.
    /// </summary>
    private Vector2 ApplyVelocitySmoothing(Vector2 newVelocity)
    {
        if (velocitySmoothingFactor <= 0.01f)
            return newVelocity;

        // Exponential smoothing
        Vector2 smoothedVelocity = Vector2.Lerp(newVelocity, previousRVOVelocity, velocitySmoothingFactor);
        previousRVOVelocity = smoothedVelocity;
        
        return smoothedVelocity;
    }

    /// <summary>
    /// Calculates an edge-aware preferred velocity by adding repulsion forces from nearby edges.
    /// This guides RVO toward safer directions before velocity computation.
    /// </summary>
    /// <param name="desiredVelocity">The original desired velocity (toward goal)</param>
    /// <returns>Edge-influenced velocity that biases away from edges</returns>
    private Vector2 CalculateEdgeAwarePreferredVelocity(Vector2 desiredVelocity)
    {
        if (!enableEdgeAvoidance || desiredVelocity.magnitude < 0.01f)
            return desiredVelocity;

        Vector3 currentPos = transform.position;
        Vector3 desiredDir3D = new Vector3(desiredVelocity.x, 0, desiredVelocity.y).normalized;
        
        // Check multiple directions around the desired direction to detect nearby edges
        Vector3 edgeRepulsionForce = Vector3.zero;
        int edgeDetectionCount = 0;
        
        // Sample directions in a cone around the desired direction
        float[] sampleAngles = { 0f, -30f, 30f, -60f, 60f };
        
        foreach (float angle in sampleAngles)
        {
            Vector3 sampleDir = Quaternion.Euler(0, angle, 0) * desiredDir3D;
            Vector3 samplePos = currentPos + sampleDir * edgeCheckAheadDistance;
            
            // Check if this direction leads toward an edge
            NavMeshHit edgeHit;
            if (NavMesh.FindClosestEdge(samplePos, out edgeHit, NavMesh.AllAreas))
            {
                // If edge is close, create a repulsion force
                if (edgeHit.distance < edgeDetectionDistance)
                {
                    // Calculate repulsion direction (away from edge)
                    Vector3 toEdge = edgeHit.position - currentPos;
                    toEdge.y = 0;
                    
                    if (toEdge.magnitude > 0.01f)
                    {
                        // Repulsion strength inversely proportional to distance
                        float repulsionStrength = 1.0f - (edgeHit.distance / edgeDetectionDistance);
                        repulsionStrength = Mathf.Pow(repulsionStrength, 2); // Quadratic falloff
                        
                        Vector3 repulsionDir = -toEdge.normalized;
                        edgeRepulsionForce += repulsionDir * repulsionStrength;
                        edgeDetectionCount++;
                    }
                }
            }
            
            // Also check with raycast for obstacles
            NavMeshHit raycastHit;
            if (NavMesh.Raycast(currentPos, samplePos, out raycastHit, NavMesh.AllAreas))
            {
                // Hit an obstacle - create repulsion away from it
                Vector3 toObstacle = raycastHit.position - currentPos;
                toObstacle.y = 0;
                
                if (toObstacle.magnitude > 0.01f)
                {
                    float distToObstacle = toObstacle.magnitude;
                    float repulsionStrength = 1.0f - Mathf.Clamp01(distToObstacle / edgeCheckAheadDistance);
                    repulsionStrength = Mathf.Pow(repulsionStrength, 2);
                    
                    Vector3 repulsionDir = -toObstacle.normalized;
                    edgeRepulsionForce += repulsionDir * repulsionStrength;
                    edgeDetectionCount++;
                }
            }
        }
        
        // If edges detected, blend the repulsion with the desired velocity
        if (edgeDetectionCount > 0)
        {
            edgeRepulsionForce /= edgeDetectionCount; // Average the forces
            edgeRepulsionForce.y = 0;
            
            // Blend desired direction with repulsion (stronger repulsion = more influence)
            float repulsionMagnitude = edgeRepulsionForce.magnitude;
            float blendFactor = Mathf.Clamp01(repulsionMagnitude * 0.5f); // 0 to 0.5 influence
            
            Vector3 adjustedDir3D = Vector3.Lerp(desiredDir3D, (desiredDir3D + edgeRepulsionForce).normalized, blendFactor);
            adjustedDir3D.y = 0;
            adjustedDir3D.Normalize();
            
            // Maintain original speed
            return new Vector2(adjustedDir3D.x, adjustedDir3D.z) * desiredVelocity.magnitude;
        }
        
        return desiredVelocity;
    }

    /// <summary>
    /// Applies edge constraints to RVO velocity with smooth adjustments rather than hard overrides.
    /// If the RVO direction is unsafe, it rotates the velocity toward the nearest safe direction
    /// while preserving as much of the original velocity magnitude as possible.
    /// </summary>
    /// <param name="rvoVelocity">The RVO-computed velocity</param>
    /// <returns>Edge-constrained velocity with smooth adjustments</returns>
    private Vector2 ApplyEdgeConstraints(Vector2 rvoVelocity)
    {
        if (!enableEdgeAvoidance)
            return rvoVelocity;

        Vector3 velocityDir3D = new Vector3(rvoVelocity.x, 0, rvoVelocity.y);
        float speed = rvoVelocity.magnitude;

        // If velocity is negligible, return as-is
        if (speed < 0.01f)
            return rvoVelocity;

        Vector3 velocityDirNormalized = velocityDir3D.normalized;

        // Check if the RVO direction is safe
        if (!IsDirectionSafe(velocityDirNormalized))
        {
            // RVO direction would lead off NavMesh - find nearest safe direction
            float safeAngle = FindSafeAngleFromEdge();
            
            if (safeAngle != 0f)
            {
                // Smoothly rotate toward safe direction instead of hard override
                // Use a partial rotation to maintain some of the RVO's intent
                float rotationStrength = Mathf.Clamp01(Mathf.Abs(safeAngle) / 45f); // 0-1 based on how far we need to turn
                float adjustedAngle = safeAngle * Mathf.Lerp(0.3f, 1.0f, rotationStrength); // At least 30% rotation
                
                Vector3 adjustedDir = Quaternion.Euler(0, adjustedAngle, 0) * velocityDir3D;
                adjustedDir.y = 0;
                
                // Verify the adjusted direction is actually safer
                if (IsDirectionSafe(adjustedDir.normalized))
                {
                    // Reduce speed slightly when correcting direction to improve stability
                    float speedMultiplier = Mathf.Lerp(0.7f, 1.0f, 1.0f - rotationStrength);
                    return new Vector2(adjustedDir.x, adjustedDir.z).normalized * (speed * speedMultiplier);
                }
                else
                {
                    // Adjusted direction still unsafe - try full rotation to safe angle
                    Vector3 fullyAdjustedDir = Quaternion.Euler(0, safeAngle, 0) * velocityDir3D;
                    fullyAdjustedDir.y = 0;
                    
                    if (IsDirectionSafe(fullyAdjustedDir.normalized))
                    {
                        // Use full safe direction at reduced speed
                        return new Vector2(fullyAdjustedDir.x, fullyAdjustedDir.z).normalized * (speed * 0.5f);
                    }
                }
            }
            
            // No safe direction found - gradually reduce velocity instead of stopping abruptly
            // This allows RVO to potentially find a better solution in the next frame
            return rvoVelocity * 0.2f; // Reduce to 20% speed instead of full stop
        }

        // Direction is safe, return original velocity
        return rvoVelocity;
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

        // Reset rotation direction and movement speed
        float rotationDir = 0f;
        float movementSpeed = npcWalkingSpeed;

        // Calculate desired direction to next corner or forward
        Vector3 toCorner = CalculateTargetDirection();

        Vector3 desiredDirection;

        // Calculate preferred velocity (where we want to go at full speed)
        Vector3 preferredVelocity3D = toCorner * npcWalkingSpeed;
        Vector2 basePreferredVelocity = new Vector2(preferredVelocity3D.x, preferredVelocity3D.z);

        // STAGE 1: Apply edge awareness to guide preferred velocity away from edges
        Vector2 edgeAwareVelocity = CalculateEdgeAwarePreferredVelocity(basePreferredVelocity);
        
        // Apply flow field bias to encourage lane formation
        Vector2 flowAdjustedVelocity = CalculateFlowField(edgeAwareVelocity);
        
        // Store for visualization
        preferredRVOVelocity = flowAdjustedVelocity;

        // Prepare neighbor data for RVO
        List<RVOSystem.AgentData> neighbors = PrepareRVONeighbors();

        // Get current position and velocity in 2D
        Vector2 currentPosition = new Vector2(transform.position.x, transform.position.z);
        Vector2 currentVelocity = new Vector2(ActualVelocity.x, ActualVelocity.z);

        // Calculate adaptive parameters
        float adaptiveTimeHorizon = GetAdaptiveTimeHorizon();
        float effectiveRadius = rvoAgentRadius * personalSpaceMultiplier;

        // STAGE 2: Compute RVO avoidance velocity
        // Use debug version in editor to populate visualization data
        #if UNITY_EDITOR
        currentRVOVelocity = RVOSystem.ComputeAvoidanceVelocityWithDebug(
            currentPosition,
            currentVelocity,
            flowAdjustedVelocity,
            neighbors,
            effectiveRadius,
            adaptiveTimeHorizon,
            rvoResponsibility,
            out lastRVODebugData
        );
        hasRVODebugData = true;
        #else
        currentRVOVelocity = RVOSystem.ComputeAvoidanceVelocity(
            currentPosition,
            currentVelocity,
            flowAdjustedVelocity,
            neighbors,
            effectiveRadius,
            adaptiveTimeHorizon,
            rvoResponsibility
        );
        #endif

        // Apply velocity smoothing
        currentRVOVelocity = ApplyVelocitySmoothing(currentRVOVelocity);
        
        // STAGE 3: Apply final edge constraints (smooth adjustment, not hard override)
        currentRVOVelocity = ApplyEdgeConstraints(currentRVOVelocity);
        
        // Convert RVO velocity back to 3D direction
        desiredDirection = new Vector3(currentRVOVelocity.x, 0f, currentRVOVelocity.y);
        
        // If the RVO velocity is very small, just use the preferred direction
        if (desiredDirection.magnitude < 0.01f)
        {
            desiredDirection = toCorner;
        }
        else
        {
            desiredDirection.Normalize();
        }

        // RVO already handles speed modulation, so we use the magnitude
        float rvoSpeed = currentRVOVelocity.magnitude;
        movementSpeed = Mathf.Min(rvoSpeed, npcWalkingSpeed); // Cap at max speed

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

        // Check for edge avoidance and adjust rotation/speed if needed
        if (!HandleEdgeAvoidance(ref rotationDir, ref movementSpeed))
        {
            return;
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
