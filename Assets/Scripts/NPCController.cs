using System.Diagnostics;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

public class NPCController : CharacterController
{
    private NavMeshAgent navMeshAgent;


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

    private void Start()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
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
            navMeshAgent.SetDestination(current_waypoint);
            waypoint_time = Random.Range(3.0f, 8.0f);
            microState = NPCStatesMicro.Walking;

            //not networking yet
            //newWaypointRpc(current_waypoint, waypoint_time);
        }
    }

    void FixedUpdate()
    {
        if (!IsOwner) { return; }

        //should this really have to happen? If we're already updating it whenever they get new waypoints?
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

                //Conditions to stop Walking:
                if (!navMeshAgent.pathPending && navMeshAgent.remainingDistance <= navMeshAgent.stoppingDistance)
                {
                    microState = NPCStatesMicro.Standing;
                }
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
        navMeshAgent.SetDestination(current_waypoint);
        microState = NPCStatesMicro.Walking;
    }

}
