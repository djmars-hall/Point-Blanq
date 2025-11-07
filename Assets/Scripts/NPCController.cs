using NUnit.Framework;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Splines;
using static UnityEngine.UI.GridLayoutGroup;

public class NPCController : BaseCharController
{

    public enum NPCStatesMicro
    {
        Standing,
        Walking,
        Turning,
        Yielding,
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

    [Header("Path Corner Settings")]
    [SerializeField] private Vector2 cornerZoneSize = new Vector2(3f, 0.5f); // Width (perpendicular) and Depth (along path) of corner visitation zone
    public Vector2 CornerZoneSize => cornerZoneSize;

    [Header("Character Avoidance Settings")]
    [SerializeField] private float charAvoidanceRadius = 6f;
    [SerializeField] private float charMinPersonalSpaceRadius = 0.4f;
    [SerializeField] private float charMaxPersonalSpaceRadius = 3f;
    [SerializeField] private float minMoveSpeed = 0.3f;
    [SerializeField] private float avoidanceSlowdownFactor = 0.5f; // Speed multiplier when avoiding (0 = stop, 1 = full speed)
    [SerializeField] private AnimationCurve charAvoidanceInfluenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Assertiveness Yield Settings")]
    [SerializeField] private float yieldDuration = 4f; // How long to yield when less assertive
    [SerializeField] private float yieldBARRIER = 0.7f; // Strength threshold to trigger assertiveness comparison
    private float yieldTimer = 0f;
    private BaseCharController closestCharacter = null; // Track the closest character from heuristics
    public BaseCharController ClosestCharacter => closestCharacter; // Public getter for debugging

    [Header("Edge Avoidance Settings")]
    [SerializeField] private float edgeBARRIER = 0.9f; // Strength of edge heuristic when any contridicting edges are zeroed out
    [SerializeField] private float edgeBuffer = 1.2f; // Distance to maintain from NavMesh edges
    [SerializeField] private float edgeAvoidanceRadius = 3f; // How far to check for edges
    [SerializeField] private AnimationCurve edgeAvoidanceInfluenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Spatial Density Settings")]
    [SerializeField] private int maxDensity = 10;
    [SerializeField] private int minDensity = 1;
    [SerializeField] private float densityUpdateInterval = 0.3f; // How often to recalculate density (in seconds)
    [SerializeField] private int forwardCheckDistance = 1; // How many cells forward to check (in addition to current cell)

    // Cached density values
    private float localDensity = 0f; // Current density (0-1, where 0 = sparse, 1 = crowded)
    private float densityUpdateTimer = 0f;
    
    // Dynamic personal space radius based on density
    private float charCurrentPersonalSpaceRadius = 0.4f; // Current personal space radius (adjusted by density)
    public float CharCurrentPersonalSpaceRadius => charCurrentPersonalSpaceRadius; // Public getter for debugging
    
    // Corridor detection
    private bool isInCorridor = false; // True when NPC is between two close NavMesh edges (in a corridor)

    // PHASE 0: Velocity tracking for boid behaviors
    private Vector3 currentVelocity = Vector3.zero; // Current movement velocity
    private Vector3 previousPosition = Vector3.zero; // Previous frame position for velocity calculation
    public Vector3 CurrentVelocity => currentVelocity; // Public getter for behavior system

    // PHASE 2: Boid Behavior System
    private BoidBehaviorManager behaviorManager = new BoidBehaviorManager();
    private NPCGoalSeekingBehavior goalSeekingBehavior = new NPCGoalSeekingBehavior();
    private CharacterAvoidanceBehavior characterAvoidanceBehavior = new CharacterAvoidanceBehavior();
    private EdgeAvoidanceBehavior edgeAvoidanceBehavior = new EdgeAvoidanceBehavior();
    
    // PHASE 3: Advanced Boid Behaviors
    private PathAdherenceBehavior pathAdherenceBehavior = new PathAdherenceBehavior();

    //Heuristics (legacy - kept for visualization and comparison)
    private Vector3 characterHeuristic; // Single heuristic for the closest character
    private Vector3 cornerHeuristic;
    private Vector3 edgeHeuristic;
    private Vector3 desiredMovement;

    //Public getters for heuristics for gizmo drawing
    public Vector3 CharacterHeuristic => characterHeuristic; // Changed to single heuristic
    public Vector3 CornerHeuristic => cornerHeuristic;
    public Vector3 EdgeHeuristic => edgeHeuristic;
    public Vector3 DesiredMovement => desiredMovement;
    
    // Public getter for density visualization
    public float LocalDensity => localDensity;

    private void Start()
    {

        if (!IsOwner) return;

        // Register with spatial grid
        if (SpatialGrid.Instance != null)
        {
            currentCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            SpatialGrid.Instance.RegisterCharacter(this, currentCell);
        }

        // Initialize local density
        UpdateLocalDensity();

        // PHASE 0: Initialize velocity tracking
        previousPosition = transform.position;

        // PHASE 2: Initialize behavior system
        InitializeBehaviorSystem();

        NewWaypoint();
        waypoint_time = 5.0f;

    }

    /// <summary>
    /// PHASE 3A: Initialize the boid behavior system with all core and advanced behaviors.
    /// - Goal Seeking (Phase 2A): Steer toward path corners
    /// - Character Avoidance (Phase 2B): Avoid nearby NPCs and players
    /// - Edge Avoidance (Phase 2C): Stay away from walls and obstacles
    /// - Path Adherence (Phase 3A): Maintain alignment with navigation path corridor
    /// </summary>
    private void InitializeBehaviorSystem()
    {
        // Register goal seeking behavior
        goalSeekingBehavior.SetBaseWeight(1.0f);
        goalSeekingBehavior.SetArrivalSlowdownDistance(3f);
        behaviorManager.RegisterBehavior(goalSeekingBehavior);

        // Register character avoidance behavior
        characterAvoidanceBehavior.SetBaseWeight(1.0f);
        characterAvoidanceBehavior.SetAvoidanceRadius(charAvoidanceRadius);
        characterAvoidanceBehavior.SetInfluenceCurve(charAvoidanceInfluenceCurve);
        behaviorManager.RegisterBehavior(characterAvoidanceBehavior);

        // Register edge avoidance behavior
        edgeAvoidanceBehavior.SetBaseWeight(1.0f);
        edgeAvoidanceBehavior.SetAvoidanceRadius(edgeAvoidanceRadius);
        edgeAvoidanceBehavior.SetEdgeBuffer(edgeBuffer);
        edgeAvoidanceBehavior.SetInfluenceCurve(edgeAvoidanceInfluenceCurve);
        behaviorManager.RegisterBehavior(edgeAvoidanceBehavior);

        // PHASE 3A: Register path adherence behavior
        pathAdherenceBehavior.SetBaseWeight(0.3f);
        pathAdherenceBehavior.SetCorridorWidth(2.5f);
        behaviorManager.RegisterBehavior(pathAdherenceBehavior);

        UnityEngine.Debug.Log($"NPCController: Behavior system initialized with {behaviorManager.BehaviorCount} behaviors");
    }

    private void NewWaypoint()
    {

        var areas = NPCManager.Instance.gatheringAreas;
        if (areas == null || areas.Length == 0) return;

        // Pick a random GatheringArea
        GatheringArea area = areas[Random.Range(0, areas.Length)];

        // Use a random point within the area as the waypoint
        Vector3 targetPoint = area.GetRandomPointInArea();


        NavMeshHit hit;
        if (NavMesh.SamplePosition(targetPoint, out hit, 5f, NavMesh.AllAreas))
        {
            current_waypoint = hit.position;

            NavMeshPath pathReturned = new NavMeshPath();

            NavMesh.CalculatePath(transform.position, current_waypoint, NavMesh.AllAreas, pathReturned);

            pathCorners = new List<Vector3>(pathReturned.corners);

            // Adjust corners to maintain distance from NavMesh edges
            pathCorners = AdjustCornersAwayFromEdges(pathCorners);

            // Initialize previous corner position to NPC's current position
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
                if (distToEdge < edgeBuffer)
                {
                    SpawnMarker(corner, i, " TOO CLOSE!!!");

                    // Calculate direction away from edge
                    Vector3 pushDirection = edgeHit.normal;
                    
                    // Calculate how much farther we need to push (plus a little to offset)
                    float deficit = (edgeBuffer - distToEdge) + 0.2f;
                    
                    // Calculate the new position
                    Vector3 newPosition = corner + pushDirection * deficit;

                    SpawnMarker(newPosition, i, " New Position.");

                    // Re-sample the new position on the NavMesh to ensure it is valid.
                    NavMeshHit newHit;
                    if (NavMesh.SamplePosition(newPosition, out newHit, edgeBuffer * 2, NavMesh.AllAreas))
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

            if (distToEdge < edgeBuffer)
            {
                //Generate a new point, then take the average of the two
                SpawnMarker(edgeHit.position, order, " TOO CLOSE!!! OverCorrection!");

                // Calculate direction away from edge
                Vector3 pushDirection = edgeHit.normal;

                // Calculate how much farther we need to push
                float deficit = edgeBuffer - distToEdge;

                // Calculate the new position (in between corrected position and newly generated position)
                Vector3 newPosition = ((generatedPoint + pushDirection * deficit) + generatedPoint) / 2;

                SpawnMarker(newPosition, order, " New Position. Corrected!");

                // Re-sample the new position on the NavMesh to ensure it is valid.
                NavMeshHit newHit;
                if (NavMesh.SamplePosition(newPosition, out newHit, edgeBuffer * 2, NavMesh.AllAreas))
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
                UnityEngine.Debug.Log("This could cause NPCs to walk through walls if not careful!");
                previousCornerPosition = pathCorners[0];
                pathCorners.RemoveAt(0);
            }
        }

        // PHASE 3A: Create behavior context for all behaviors
        NPCBehaviorContext context = new NPCBehaviorContext
        {
            position = transform.position,
            forward = transform.forward,
            currentVelocity = currentVelocity,
            targetCorner = pathCorners[0],
            previousCorner = previousCornerPosition,
            remainingPath = pathCorners,
            localDensity = localDensity,
            isInCorridor = isInCorridor,
            nearbyCharacters = SpatialGrid.Instance != null ? SpatialGrid.Instance.GetNearbyCharacters(currentCell) : new List<BaseCharController>(),
            assertiveness = Assertiveness,
            currentPersonalSpaceRadius = charCurrentPersonalSpaceRadius,
            deltaTime = Time.fixedDeltaTime
        };

        // PHASE 3A: Calculate movement from complete behavior system
        // (goal seeking + character avoidance + edge avoidance + path adherence)
        desiredMovement = behaviorManager.CalculateFinalMovement(context);

        // Store individual behavior outputs for visualization (compatibility with gizmo system)
        cornerHeuristic = goalSeekingBehavior.Calculate(context);
        characterHeuristic = characterAvoidanceBehavior.Calculate(context);
        edgeHeuristic = edgeAvoidanceBehavior.Calculate(context);
        
        // Track closest character from character avoidance behavior
        closestCharacter = characterAvoidanceBehavior.ClosestCharacter;

        // If desiredMovement is too small, don't move
        if (desiredMovement.magnitude < 0.01f)
        {
            ProcessMovement(0f, 0f);
            return;
        }

        // Get the direction (normalized) for rotation calculations
        Vector3 movementDirection = desiredMovement.normalized;

        // Calculate how aligned the NPC's forward direction is with the desired movement direction
        float forwardAlignment = Vector3.Dot(transform.forward, movementDirection);
        
        // Calculate the rotation needed (using the right vector to determine turn direction)
        float rightAlignment = Vector3.Dot(transform.right, movementDirection);

        // Check if we need to turn around (desired direction is opposite to current facing)
        bool needsToTurnAround = forwardAlignment < -0.5f;

        // Rotation: Turn toward the desired direction
        float rotationDir = Mathf.Clamp(rightAlignment, -1f, 1f);

        // Speed calculation: Use the magnitude of desiredMovement directly as the PRIMARY driver
        // The magnitude represents the combined strength/urgency of all heuristics
        float rawMagnitude = desiredMovement.magnitude;
        
        // Normalize to 0-1 range for speed multiplier
        float speedMultiplier = Mathf.Clamp01(rawMagnitude);
        
        // Apply forward alignment as a MODIFIER, not a multiplier
        // This reduces speed slightly when turning, but doesn't eliminate the magnitude effect
        // Range: 0.7 (worst alignment) to 1.0 (perfect alignment)
        float alignmentModifier = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(forwardAlignment));
        speedMultiplier *= alignmentModifier;
        
        // If we need to turn around, stop moving and just rotate
        if (needsToTurnAround)
        {
            speedMultiplier = 0f;
        }
        else if (rawMagnitude < 0.3f)
        {
            // Only apply minimum speed when magnitude is very low (safety net)
            // This prevents stopping during very weak heuristics
            speedMultiplier = Mathf.Max(speedMultiplier, minMoveSpeed);
        }

        // Execute movement with the calculated speed and rotation
        ProcessMovement(speedMultiplier, rotationDir);
    }

    /// <summary>
    /// Checks if the NPC is inside the perpendicular zone around a corner.
    /// The zone is oriented perpendicular to the path direction (from previous corner to this corner).
    /// </summary>
    /// <param name="cornerIndex">Index of the corner to check</param>
    /// <returns>True if the NPC is inside the corner's visitation zone</returns>
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

    /// <summary>
    /// Calculates local density by sampling the current cell and surrounding 8 cells (3x3 grid),
    /// filtering out cells whose centers are behind the NPC.
    /// Also updates the isInCorridor flag from EdgeAvoidanceBehavior.
    /// Updates the dynamic personal space radius based on the calculated density.
    /// Returns a value from 0 (sparse/empty) to 1 (crowded/full).
    /// </summary>
    private void UpdateLocalDensity()
    {
        if (SpatialGrid.Instance == null)
        {
            localDensity = 0f;
            charCurrentPersonalSpaceRadius = charMaxPersonalSpaceRadius;
            return;
        }

        // Update corridor state from EdgeAvoidanceBehavior
        isInCorridor = edgeAvoidanceBehavior.IsInCorridor;

        // If in a corridor, set density to maximum
        if (isInCorridor)
        {
            localDensity = 1f;
            charCurrentPersonalSpaceRadius = charMinPersonalSpaceRadius;
            return;
        }

        // Get forward direction (flatten to XZ plane)
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        // --- Normal density calculation ---
        int totalCharacters = 0;
        int cellsChecked = 0;

        Vector2Int currentCellCoords = currentCell;
        
        // Get cell size from SpatialGrid
        float cellSize = SpatialGrid.Instance.CellSize;

        // Sample 3x3 grid around current cell
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                Vector2Int checkCell = currentCellCoords + new Vector2Int(x, z);
                
                // Calculate the world position of the cell center
                Vector3 cellCenter = new Vector3(
                    checkCell.x * cellSize + cellSize * 0.5f,
                    0,
                    checkCell.y * cellSize + cellSize * 0.5f
                );

                // Calculate direction from NPC to cell center
                Vector3 toCellCenter = cellCenter - transform.position;
                toCellCenter.y = 0;
                toCellCenter.Normalize();

                // Check if cell center is in front of or beside the NPC (not behind)
                float dotProduct = Vector3.Dot(forward, toCellCenter);

                // Skip cells that are behind us (dot product < -0.3 means roughly behind)
                // For the current cell (0,0), always include it
                if (x == 0 && z == 0)
                {
                    // Always include current cell
                    int cellPop = SpatialGrid.Instance.GetCellPopulation(checkCell);
                    totalCharacters += cellPop;
                    cellsChecked++;
                }
                else if (dotProduct >= -0.3f)
                {
                    // Include cells that are in front or to the sides
                    int cellPop = SpatialGrid.Instance.GetCellPopulation(checkCell);
                    totalCharacters += cellPop;
                    cellsChecked++;
                }
            }
        }

        float rawDensity = (float)totalCharacters;
        
        // Normalize based on max expected density
        localDensity = Mathf.Clamp01(rawDensity / maxDensity);
        
        // Calculate dynamic personal space radius based on density
        // High density (1.0) = smaller personal space (charMinPersonalSpaceRadius)
        // Low density (0.0) = larger personal space (charMaxPersonalSpaceRadius)
        charCurrentPersonalSpaceRadius = Mathf.Lerp(charMaxPersonalSpaceRadius, charMinPersonalSpaceRadius, localDensity);
    }

    void FixedUpdate()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (!IsOwner) { return; }

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

        // Periodically update local density
        densityUpdateTimer += Time.fixedDeltaTime;
        if (densityUpdateTimer >= densityUpdateInterval)
        {
            UpdateLocalDensity();
            densityUpdateTimer = 0f;
        }

        //this shouldn't have to happen eventually. or maybe only once in a while?
        UpdatePositionClientRpc(transform.position, transform.rotation);

        switch (microState)
        {
            case NPCStatesMicro.Standing:
                waypoint_time -= Time.fixedDeltaTime;
                if (waypoint_time <= 0.0f)
                {
                    NewWaypoint();
                }
                break;
            case NPCStatesMicro.Walking:
                DecideMovement();
                break;
            case NPCStatesMicro.Turning:
                break;
        
            // PHASE 0: YIELDING DISABLED - Commented out for behavior system migration
            /*
            case NPCStatesMicro.Yielding:
                HandleYielding();
                break;
            */
        }

        // PHASE 0: Update velocity tracking (calculate from position change)
        if (Time.fixedDeltaTime > 0)
        {
            currentVelocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
            previousPosition = transform.position;
        }
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

    /// <summary>
    /// Compares assertiveness with the closest NPC. The NPC with lower assertiveness yields (stops for 4 seconds).
    /// Only NPCs are compared - players are excluded from assertiveness comparison.
    /// </summary>
    /// <returns>True if this NPC should yield, false otherwise</returns>
    private bool CompareAssertivenessAndYield()
    {
        // If no closest character or closest character is not an NPC, don't yield
        if (closestCharacter == null || !(closestCharacter is NPCController))
        {
            return false;
        }

        NPCController otherNPC = closestCharacter as NPCController;
        UnityEngine.Debug.Log("npc: " + closestCharacter.ToString() + " Assertiveness: " + closestCharacter.Assertiveness);


        // Skip if the other NPC is already yielding (don't create a stalemate)
        if (otherNPC.MicroState == NPCStatesMicro.Yielding)
        {
            return false;
        }

        // Compare assertiveness - lower value yields
        if (this.Assertiveness < otherNPC.Assertiveness)
        {
            // This NPC has lower assertiveness, so it yields
            microState = NPCStatesMicro.Yielding;
            yieldTimer = yieldDuration;
            
            UnityEngine.Debug.Log($"{this.name} (Assertiveness: {this.Assertiveness}) yielding to {otherNPC.name} (Assertiveness: {otherNPC.Assertiveness})");
            
            return true;
        }
        UnityEngine.Debug.Log("4");
        return false;
    }

    /// <summary>
    /// Handles the yielding behavior - NPC waits in place for the yield duration.
    /// </summary>
    private void HandleYielding()
    {
        yieldTimer -= Time.fixedDeltaTime;

        // Check if we should stop yielding
        if (yieldTimer <= 0f)
        {
            microState = NPCStatesMicro.Walking;
            return;
        }

        // Don't move while yielding
        ProcessMovement(0f, 0f);
    }
}
