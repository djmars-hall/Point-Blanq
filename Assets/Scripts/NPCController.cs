using System.Diagnostics;
using UnityEngine;
using Unity.Netcode;

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

    private void Start()
    {
        NewWaypoint();
        waypoint_time = 5.0f;
    }

    private void NewWaypoint()
    {
        current_waypoint = transform.position + 
            new Vector3(Random.Range(-5,5),0,Random.Range(-5, 5));
        waypoint_time = Random.Range(1.0f, 3.0f);
        microState = NPCStatesMicro.Turning;
    }

    void Update()
    {
        if (!IsOwner) { return; }
        switch (microState)
        {
            case NPCStatesMicro.Standing:
                break;
            case NPCStatesMicro.Walking:
                ProcessMovement(1,0);
                if (Vector3.Distance(transform.position,current_waypoint) < 0.2f)
                {
                    microState = NPCStatesMicro.Standing;
                }
                break;
            case NPCStatesMicro.Turning:
                Vector3 directionToTarget = (current_waypoint - transform.position).normalized;
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                float midpoint_to_target_angle = Mathf.LerpAngle(transform.rotation.eulerAngles.y, targetRotation.eulerAngles.y, 0.5f);
                float midpoint_to_target_angle_diff = midpoint_to_target_angle-transform.rotation.eulerAngles.y;
                float rot_dir = Mathf.Sign(midpoint_to_target_angle_diff);
                ProcessMovement(0, rot_dir);
                if (Mathf.Abs(midpoint_to_target_angle_diff) < Time.deltaTime * speed * rotationSpeed)
                {
                    transform.rotation = targetRotation;
                    microState = NPCStatesMicro.Walking;
                }
                break;
        }
        waypoint_time -= Time.deltaTime;
        if (waypoint_time <= 0.0f)
        {
            NewWaypoint();
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
}
