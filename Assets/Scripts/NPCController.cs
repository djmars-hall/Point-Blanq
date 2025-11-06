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

    //Heuristics
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

        NewWaypoint();
        waypoint_time = 5.0f;

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

        //Corner Heuristic (Pathway Corners)
        cornerHeuristic = GetCornerHeuristic();
            
        //Character Heuristic (NPCs and Players):
        characterHeuristic = GetCharacterHeuristic();

        //Edge Heuristic (NavMesh Edges):
        edgeHeuristic = GetEdgeHeuristic();

        //If Edge Heuristic is too strong, nullify any heuristic towards edge
        if (edgeHeuristic.magnitude > edgeBARRIER)
        {
            DenyOtherHeuristics(edgeHeuristic.normalized);
        }

        //If the character heuristic is too strong, compare assertiveness and yield
        if (characterHeuristic.magnitude > yieldBARRIER)
        {
            //UnityEngine.Debug.Log("Strong Character Heuristic detected, comparing assertiveness.");
            if (CompareAssertivenessAndYield())
            {
                UnityEngine.Debug.Log("NPC Yielding to more assertive character.");
                return; // Exit early if this NPC is yielding
            }
        }

        // Calculate desired movement by blending corner heuristic with character heuristic and edge heuristic
        // Do NOT normalize - preserve the magnitude to reflect the combined influence strength
        desiredMovement = cornerHeuristic + characterHeuristic + edgeHeuristic;

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
        // Expected range: 0 to ~3 (corner≈1 + character avoidance up to ~1 + edge avoidance up to ~1)
        float rawMagnitude = desiredMovement.magnitude;
        
        // Normalize to 0-1 range for speed multiplier
        // Lower divisor = higher max speed. Adjust this value to tune overall speed
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
    /// If any heuristic is too strong this function nullifies any (except edge--its too high priority) heuristic that contradicts it
    /// </summary>
    /// <param name="normalizedTargetHeuristic"></param>
    private void DenyOtherHeuristics(Vector3 normalizedTargetHeuristic)
    {
        if (Vector3.Dot(cornerHeuristic.normalized, normalizedTargetHeuristic) < 0)
        {
            cornerHeuristic = ReorientHeuristics(normalizedTargetHeuristic, cornerHeuristic);
        }
        if (characterHeuristic.magnitude > 0.01f && Vector3.Dot(characterHeuristic.normalized, normalizedTargetHeuristic) < 0)
        {
            characterHeuristic = ReorientHeuristics(normalizedTargetHeuristic, characterHeuristic);
        }
    }

    /// <summary>
    /// Reorients a heuristic to be perpendicular to a target heuristic
    /// </summary>
    /// <param name="normalizedTargetHeuristic"></param>
    /// <param name="heuristicToModify"></param>
    /// <returns>The Vector3 of the reoriented Heuristic</returns>
    private Vector3 ReorientHeuristics(Vector3 normalizedTargetHeuristic, Vector3 heuristicToModify)
    {
        // Calculate both perpendiculars to the target heuristic
        Vector3 perp1 = Vector3.Cross(normalizedTargetHeuristic, Vector3.up).normalized;
        Vector3 perp2 = -perp1;

        // Choose the perpendicular closest to the original heuristic direction
        float dot1 = Vector3.Dot(heuristicToModify.normalized, perp1);
        float dot2 = Vector3.Dot(heuristicToModify.normalized, perp2);

        Vector3 closestPerp = (dot1 > dot2) ? perp1 : perp2;
        return closestPerp * heuristicToModify.magnitude;
    }

    /// <summary>
    /// Calculates the corner heuristic vector pointing toward the next path corner.
    /// </summary>
    /// <returns>The corner Heuristic</returns>
    private Vector3 GetCornerHeuristic()
    {
        // Calculate distance to next corner
        float distanceToCorner = Vector3.Distance(pathCorners[0], transform.position);

        // Next corner direction with distance-based strength reduction
        Vector3 baseCornerDirection = (pathCorners[0] - transform.position);
        baseCornerDirection.y = 0; // Keep movement on horizontal plane
        baseCornerDirection = baseCornerDirection.normalized;

        // Reduce corner strength when close to destination (within 3 units)
        float cornerStrengthMultiplier = 1f;
        float arrivalSlowdownDistance = 3f;
        if (distanceToCorner < arrivalSlowdownDistance)
        {
            // Smoothly reduce from 1.0 to 0.7 as we get closer (much less aggressive)
            cornerStrengthMultiplier = Mathf.Lerp(0.7f, 1f, distanceToCorner / arrivalSlowdownDistance);
        }

        return baseCornerDirection * cornerStrengthMultiplier;
    }

    /// <summary>
    /// Calculates an avoidance vector from the closest nearby character (both NPCs and Players).
    /// Uses the spatial grid for efficient neighbor queries and returns only the single most influential character avoidance vector.
    /// Only considers characters that are in front of or beside the NPC (not behind).
    /// Also tracks the closest character for assertiveness comparison.
    /// Maximum influence occurs at the dynamic charCurrentPersonalSpaceRadius (adjusted by density), falling off to zero at charAvoidanceRadius.
    /// </summary>
    /// <returns>The avoidance vector for the closest character, or Vector3.zero if no characters nearby</returns>
    private Vector3 GetCharacterHeuristic()
    {
        closestCharacter = null; // Reset closest character
        float closestInfluence = 0f;
        Vector3 closestAvoidanceVector = Vector3.zero;
        
        if (SpatialGrid.Instance != null)
        {
            List<BaseCharController> nearbyCharacters = SpatialGrid.Instance.GetNearbyCharacters(currentCell);

            foreach (var otherCharacter in nearbyCharacters)
            {
                if (otherCharacter == null || otherCharacter == this) continue;

                Vector3 toOther = otherCharacter.transform.position - transform.position;
                float distance = toOther.magnitude;

                // Only avoid characters within the avoidance radius
                if (distance < charAvoidanceRadius && distance > 0.1f)
                {
                    // Calculate the dot product to determine if the character is in front/beside or behind
                    // Flatten to XZ plane for 2D forward check
                    Vector3 forwardFlat = transform.forward;
                    forwardFlat.y = 0;
                    forwardFlat.Normalize();
                    
                    Vector3 toOtherFlat = toOther;
                    toOtherFlat.y = 0;
                    toOtherFlat.Normalize();
                    
                    float dotProduct = Vector3.Dot(forwardFlat, toOtherFlat);
                    
                    // Filter out characters that are behind us (dot product < -0.3 means roughly behind)
                    // This allows characters directly to the side (dot ~= 0) and in front (dot > 0)
                    if (dotProduct < -0.3f)
                    {
                        continue; // Skip characters that are behind us
                    }

                    // Calculate direction away from the other character
                    Vector3 awayFromCharacter = -toOther.normalized;
                    awayFromCharacter.y = 0; // Keep avoidance on horizontal plane

                    float influence = 0f;

                    // Use dynamic personal space radius based on local density
                    if (distance <= charCurrentPersonalSpaceRadius)
                    {
                        // Max influence inside personal space
                        influence = 1f;
                    }
                    else
                    {
                        // Influence falls off from personal space radius to avoidance radius
                        float normalizedDistance = (distance - charCurrentPersonalSpaceRadius) / (charAvoidanceRadius - charCurrentPersonalSpaceRadius);
                        influence = charAvoidanceInfluenceCurve.Evaluate(normalizedDistance);
                    }

                    // Track only the closest (highest influence) character
                    if (influence > closestInfluence)
                    {
                        closestInfluence = influence;
                        closestCharacter = otherCharacter;
                        closestAvoidanceVector = awayFromCharacter * influence;
                    }
                }
            }

            return closestAvoidanceVector;
        }
        
        UnityEngine.Debug.LogError("NO SPATIAL GRID INSTANCE");
        return Vector3.zero;
    }

    /// <summary>
    /// Calculates an avoidance vector away from the closest NavMesh edge.
    /// Returns a vector pointing away from the edge with magnitude based on distance.
    /// Checks for opposing edges to prevent getting stuck between two edges.
    /// </summary>
    /// <returns>Edge avoidance vector, or Vector3.zero if no edge is nearby or caught between edges</returns>
    private Vector3 GetEdgeHeuristic()
    {
        NavMeshHit edgeHit;
        
        // Find the closest edge within the avoidance radius
        if (NavMesh.FindClosestEdge(transform.position, out edgeHit, NavMesh.AllAreas))
        {
            float distanceToEdge = edgeHit.distance;
            
            // Only apply avoidance if within the edge avoidance radius
            if (distanceToEdge < edgeAvoidanceRadius && distanceToEdge > 0.1f)
            {
                // Direction away from the edge (using the edge normal)
                Vector3 awayFromEdge = edgeHit.normal;
                awayFromEdge.y = 0; // Keep avoidance on horizontal plane
                awayFromEdge = awayFromEdge.normalized;
                
                // Calculate influence using the custom curve
                // Normalize distance to 0-1 range (0 = at edge, 1 = at edgeAvoidanceRadius)
                float normalizedDistance = distanceToEdge / edgeAvoidanceRadius;
                
                // Evaluate the curve (curve should go from 1 at x=0 to 0 at x=1)
                float influence = edgeAvoidanceInfluenceCurve.Evaluate(normalizedDistance);
                
                Vector3 edgeAvoidanceVector = awayFromEdge * influence;
                
                // Check if there's an opposing edge that would contradict this heuristic
                // Only nullify if the heuristic is weak AND we're truly trapped
                if (edgeAvoidanceVector.magnitude < 0.2f && HasOpposingEdge(awayFromEdge, distanceToEdge))
                {
                    // Caught between two edges with weak influence, nullify to avoid oscillation
                    return Vector3.zero;
                }
                
                return edgeAvoidanceVector;
            }
        }
        
        return Vector3.zero;
    }

    /// <summary>
    /// Checks if there is an opposing edge in the direction we want to move away from the closest edge.
    /// This prevents the NPC from getting stuck oscillating between two close edges.
    /// Uses stricter thresholds to only detect true opposing edge situations.
    /// Also updates the isInCorridor flag.
    /// </summary>
    /// <param name="avoidanceDirection">The direction we want to move away from the closest edge</param>
    /// <param name="closestEdgeDistance">Distance to the closest edge</param>
    /// <returns>True if there's an opposing edge that would contradict the avoidance direction</returns>
    private bool HasOpposingEdge(Vector3 avoidanceDirection, float closestEdgeDistance)
    {
        // Sample a point in the avoidance direction to check for an opposing edge
        // Use a tighter check radius (half of edgeBuffer)
        Vector3 checkPosition = transform.position + avoidanceDirection * (edgeBuffer * 0.5f);
        
        NavMeshHit opposingEdgeHit;
        if (NavMesh.FindClosestEdge(checkPosition, out opposingEdgeHit, NavMesh.AllAreas))
        {
            float opposingDistance = opposingEdgeHit.distance;

            // Only consider it an opposing edge if its within a little more than edgeBuffer BIG ISSUE WITH THIS LINE
            if (opposingDistance < edgeBuffer * 1.5f)
            {
                // Check if this edge's normal points back toward us (opposing the avoidance direction)
                Vector3 opposingNormal = opposingEdgeHit.normal;
                opposingNormal.y = 0;
                opposingNormal = opposingNormal.normalized;
                
                // If the dot product is negative, the normals point in opposite directions
                // Use stricter threshold (-0.7) to ensure they're truly opposing
                float alignment = Vector3.Dot(avoidanceDirection, opposingNormal);
                
                if (alignment < -0.7f) // Stricter threshold to detect truly opposing edges
                {
                    isInCorridor = true;
                    return true;
                }
            }
        }
        
        isInCorridor = false;
        return false;
    }

    /// <summary>
    /// Calculates local density by sampling the current cell and surrounding 8 cells (3x3 grid),
    /// filtering out cells whose centers are behind the NPC.
    /// Also checks if NPC is in a corridor (set by HasOpposingEdge): if so, sets density to maximum.
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

        // If in a corridor (detected by HasOpposingEdge), set density to maximum
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


                //Conditions to stop Walking:
                //if (!navMeshAgent.pathPending && navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                //{
                //    microState = NPCStatesMicro.Standing;
                //}
                break;
            case NPCStatesMicro.Turning:
                break;
            case NPCStatesMicro.Yielding:
                HandleYielding();
                break;



            //case NPCStatesMicro.Standing:
            //    break;
            //case NPCStatesMicro.Walking:
            //    ProcessMovement(1, 0);
            //    if (Vector3.Distance(transform.position, current_waypoint) < 0.2f)
            //    {
            //        microState = NPCStatesMicro.Standing;
            //    }

            //    break;
            //case NPCStatesMicro.Turning:
            //    Vector3 directionToTarget = (current_waypoint - transform.position).normalized;
            //    Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            //    float midpoint_to_target_angle = Mathf.Lerpangle(transform.rotation.eulerAngles.y, targetRotation.eulerAngles.y, 0.5f);
            //    float midpoint_to_target_angle_diff = midpoint_to_target_angle - transform.rotation.eulerAngles.y;
            //    float rot_dir = Mathf.Sign(midpoint_to_target_angle_diff);
            //    ProcessMovement(0, rot_dir);
            //    if (Mathf.Abs(midpoint_to_target_angle_diff) < Time.deltaTime * speed * rotationSpeed)
            //    {
            //        transform.rotation = targetRotation;
            //        microState = NPCStatesMicro.Walking;
            //    }
            //    break;
        }
        
        //waypoint_time -= Time.deltaTime;
        //if (waypoint_time <= 0.0f)
        //{
        //    NewWaypoint();
        //}
        
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
