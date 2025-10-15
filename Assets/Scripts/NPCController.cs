using System.Diagnostics;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using NUnit.Framework;
using System.Collections.Generic;

public class NPCController : CharacterController
{

    enum NPCStatesMicro
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
    NPCStatesMicro microState = NPCStatesMicro.Turning;
    Vector3 current_waypoint;
    float waypoint_time;
    private List<Vector3> pathCorners = new List<Vector3>();
    public List<Vector3> PathCorners => pathCorners;

    // Spatial grid tracking
    private Vector2Int currentCell;

    [Header("Avoidance Settings")]
    [SerializeField] private float avoidanceRadius = 4f;
    [SerializeField] private float avoidanceWeight = 0.8f;
    [SerializeField] private float minMoveSpeed = 0.3f;
    [SerializeField] private float turnAmplification = 2f;

    private void Start()
    {
        // Register with spatial grid
        if (SpatialGrid.Instance != null)
        {
            currentCell = SpatialGrid.Instance.GetCellCoords(transform.position);
            SpatialGrid.Instance.RegisterNPC(this, currentCell);
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
            //navMeshAgent.SetDestination(current_waypoint);
            NavMeshPath pathReturned = new NavMeshPath();

            NavMesh.CalculatePath(transform.position, current_waypoint, NavMesh.AllAreas, pathReturned);

            pathCorners = new List<Vector3>(pathReturned.corners);

            waypoint_time = Random.Range(3.0f, 12.0f);
            microState = NPCStatesMicro.Walking;

            //not networking yet...kinda being done on everyone's compuuuter :0
            //newWaypointRpc(current_waypoint, waypoint_time);
        }
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

        

        //logic for hueristic movement decisions to go towards the next corner, and avoid:
        //NPCs
        //Navmesh edges
        //Players

        // Heuristic: pursue the next waypoint (corner)
        Vector3 toCorner = (pathCorners[0] - transform.position).normalized;

        // Get nearby NPCs from spatial grid and calculate avoidance
        Vector3 avoidanceVector = Vector3.zero;
        if (SpatialGrid.Instance != null)
        {
            List<NPCController> nearbyNPCs = SpatialGrid.Instance.GetNearbyNPCs(currentCell);

            foreach (var otherNPC in nearbyNPCs)
            {
                if (otherNPC == null || otherNPC == this) continue;

                float distance = Vector3.Distance(transform.position, otherNPC.transform.position);
                
                // Only avoid NPCs within the avoidance radius
                if (distance < avoidanceRadius && distance > 0.1f)
                {
                    // Calculate direction away from the other NPC
                    Vector3 awayFromNPC = (transform.position - otherNPC.transform.position).normalized;
                    
                    // Calculate influence (stronger when closer)
                    // At distance 0: influence = 1.0
                    // At avoidanceRadius: influence = 0.0
                    float influence = 1f - (distance / avoidanceRadius);
                    
                    // Add weighted avoidance vector
                    avoidanceVector += awayFromNPC * influence;
                }
            }
        }

        // Blend waypoint pursuit with avoidance
        // Waypoint has higher priority, avoidance modifies the direction
        Vector3 desiredDirection = (toCorner + avoidanceVector * avoidanceWeight).normalized;

        // Calculate how to turn toward the desired direction
        float forward = Vector3.Dot(transform.forward, desiredDirection);
        float right = Vector3.Dot(transform.right, desiredDirection);
        
        // Always try to move forward, but slow down if not facing the right direction
        float moveAmount = Mathf.Clamp01(forward * (1f - minMoveSpeed) + minMoveSpeed);
        
        // Turn toward the desired direction
        float rotationDir = Mathf.Clamp(right * turnAmplification, -1f, 1f);

        ProcessMovement(moveAmount, rotationDir);
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
                SpatialGrid.Instance.UpdateNPC(this, currentCell, newCell);
                currentCell = newCell;
            }
        }

        //this shouldn't have to happen eventually. or maybe only once in a while?
        UpdatePositionClientRpc(transform.position, transform.rotation);

        switch (microState)
        {
            case NPCStatesMicro.Standing:

                

                waypoint_time -= Time.deltaTime;
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
            //    float midpoint_to_target_angle = Mathf.LerpAngle(transform.rotation.eulerAngles.y, targetRotation.eulerAngles.y, 0.5f);
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

    [Rpc(SendTo.NotMe)]
    void newWaypointRpc(Vector3 cw, float wt)
    {
        waypoint_time = wt;
        current_waypoint = cw;
        //navMeshAgent.SetDestination(current_waypoint);
        microState = NPCStatesMicro.Walking;
    }

    private void OnDestroy()
    {
        // Unregister from spatial grid
        if (SpatialGrid.Instance != null)
        {
            SpatialGrid.Instance.UnregisterNPC(this, currentCell);
        }
    }

}
