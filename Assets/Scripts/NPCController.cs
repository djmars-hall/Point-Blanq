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

    [Header("Avoidance Settings")]
    [SerializeField] private float avoidanceRadius = 6f;
    [SerializeField] private float characterAvoidanceWeight = 0.8f;
    [SerializeField] private float minMoveSpeed = 0.3f;
    [SerializeField] private float avoidanceSlowdownFactor = 0.5f; // Speed multiplier when avoiding (0 = stop, 1 = full speed)
    [SerializeField] private AnimationCurve avoidanceInfluenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [SerializeField] private int maxTrackedCharacters = 3;

    [Header("Path Edge Avoidance")]
    [SerializeField] private float edgeBuffer = 1.2f; // Distance to maintain from NavMesh edges
    [SerializeField] private float edgeAvoidanceRadius = 3f; // How far to check for edges
    [SerializeField] private float edgeAvoidanceWeight = 0.6f; // Strength of edge avoidance
    [SerializeField] private AnimationCurve edgeAvoidanceInfluenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    //Heuristics
    private List<Vector3> characterHeuristics = new List<Vector3>();
    private Vector3 cornerHeuristic;
    private Vector3 edgeHeuristic;
    private Vector3 desiredDirection;

    //Public getters for heuristics for gizmo drawing
    public List<Vector3> CharacterHeuristics => characterHeuristics;
    public Vector3 CornerHeuristic => cornerHeuristic;
    public Vector3 EdgeHeuristic => edgeHeuristic;
    public Vector3 DesiredDirection => desiredDirection;

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

        //check if close enough to corner to move on to next corner
        if (Vector3.Distance(pathCorners[0], transform.position) < 0.5f)
        {
            pathCorners.RemoveAt(0);
            //check if at end of pathway AGAIN
            if (pathCorners.Count == 0)
            {
                //we made it to the end of the path
                microState = NPCStatesMicro.Standing;
                return;
            }
        }

        //Corner Heuristic:
        cornerHeuristic = GetCornerHeuristic();
            
        //Character Heuristic (NPCs and Players):
        characterHeuristics = GetCharacterHeuristics();

        //Edge Heuristic (NavMesh edges):
        edgeHeuristic = GetEdgeHeuristic();

        // Calculate desired direction by blending corner heuristic with weighted character heuristics and edge heuristic
        desiredDirection = cornerHeuristic;
        foreach (var characterHeuristic in characterHeuristics)
        {
            desiredDirection += characterHeuristic * characterAvoidanceWeight;
        }
        desiredDirection += edgeHeuristic * edgeAvoidanceWeight;
        desiredDirection = desiredDirection.normalized;

        // Calculate how aligned the NPC's forward direction is with the desired direction
        float forwardAlignment = Vector3.Dot(transform.forward, desiredDirection);
        
        // Calculate the rotation needed (using the right vector to determine turn direction)
        float rightAlignment = Vector3.Dot(transform.right, desiredDirection);

        // Rotation: Turn toward the desired direction
        // Scale rotation by how far we need to turn (larger misalignment = faster turn)
        float rotationDir = Mathf.Clamp(rightAlignment, -1f, 1f);

        // Speed: Move faster when aligned with desired direction, slower when turning
        // This creates more natural movement where NPCs slow down to turn
        float speedMultiplier = Mathf.Clamp01(forwardAlignment);
        
        // Apply minimum speed so NPC doesn't stop completely when turning
        speedMultiplier = Mathf.Max(speedMultiplier, minMoveSpeed);

        // Apply slowdown when avoiding characters (based on total character heuristic magnitude)
        float totalCharacterInfluence = 0f;
        foreach (var characterHeuristic in characterHeuristics)
        {
            totalCharacterInfluence += characterHeuristic.magnitude;
        }
        float avoidanceIntensity = Mathf.Clamp01(totalCharacterInfluence);
        speedMultiplier *= Mathf.Lerp(1f, avoidanceSlowdownFactor, avoidanceIntensity);

        // Execute movement with the calculated speed and rotation
        ProcessMovement(speedMultiplier, rotationDir);
    }


    /// <summary>
    /// Calculates the corner heuristic vector pointing toward the next path corner.
    /// /// </summary>
    /// <returns></returns>
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
    /// Calculates individual avoidance vectors from nearby characters (both NPCs and Players).
    /// Uses the spatial grid for efficient neighbor queries and returns the top N most influential character avoidance vectors.
    /// </summary>
    /// <returns>A list of the most influential character avoidance vectors, limited to maxTrackedCharacters</returns>
    private List<Vector3> GetCharacterHeuristics()
    {
        // Use dictionaries to track influences for sorting
        Dictionary<Vector3, float> influenceMap = new Dictionary<Vector3, float>();
        
        if (SpatialGrid.Instance != null)
        {
            List<BaseCharController> nearbyCharacters = SpatialGrid.Instance.GetNearbyCharacters(currentCell);

            foreach (var otherCharacter in nearbyCharacters)
            {
                if (otherCharacter == null || otherCharacter == this) continue;

                float distance = Vector3.Distance(transform.position, otherCharacter.transform.position);

                // Only avoid characters within the avoidance radius
                if (distance < avoidanceRadius && distance > 0.1f)
                {
                    // Calculate direction away from the other character
                    Vector3 awayFromCharacter = (transform.position - otherCharacter.transform.position);
                    awayFromCharacter.y = 0; // Keep avoidance on horizontal plane
                    awayFromCharacter = awayFromCharacter.normalized;

                    // Calculate influence using the custom curve
                    // Normalize distance to 0-1 range (0 = at same position, 1 = at avoidanceRadius)
                    float normalizedDistance = distance / avoidanceRadius;

                    // Evaluate the curve (curve should go from 1 at x=0 to 0 at x=1)
                    float influence = avoidanceInfluenceCurve.Evaluate(normalizedDistance);

                    // Store the avoidance vector with its influence
                    Vector3 avoidanceVector = awayFromCharacter * influence;
                    influenceMap[avoidanceVector] = influence;
                }
            }

            // Sort by influence (highest first), take the top N, and extract just the vectors
            return influenceMap
                .OrderByDescending(pair => pair.Value)
                .Take(maxTrackedCharacters)
                .Select(pair => pair.Key)
                .ToList();
        }
        
        UnityEngine.Debug.LogError("NO SPATIAL GRID INSTANCE");
        return new List<Vector3>();
    }


    /// <summary>
    /// Calculates an avoidance vector away from the closest NavMesh edge.
    /// Returns a vector pointing away from the edge with magnitude based on distance.
    /// </summary>
    /// <returns>Edge avoidance vector, or Vector3.zero if no edge is nearby</returns>
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
                
                return awayFromEdge * influence;
            }
        }
        
        return Vector3.zero;
    }

    void FixedUpdate()
    {

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
    
    /*
    [Rpc(SendTo.NotMe)]
    void newWaypointRpc(Vector3 cw, float wt)
    {
        waypoint_time = wt;
        current_waypoint = cw;
        //navMeshAgent.SetDestination(current_waypoint);
        microState = NPCStatesMicro.Walking;
    }
    */

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
