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

    private List<Vector3> AdjustCornersAwayFromEdges(List<Vector3> corners)
    {
        List<Vector3> adjustedCorners = new List<Vector3>();
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 corner = corners[i];
            Vector3 adjustedCorner = corner;
            NavMeshHit edgeHit;
            if (NavMesh.FindClosestEdge(corner, out edgeHit, NavMesh.AllAreas))
            {
                float distToEdge = edgeHit.distance;
                SpawnMarker(edgeHit.position, i, " Edge Hit. Too Close?");
                if (distToEdge < 1.2f) // Use a default edgeBuffer value
                {
                    SpawnMarker(corner, i, " TOO CLOSE!!!");
                    Vector3 pushDirection = edgeHit.normal;
                    float deficit = (1.2f - distToEdge) + 0.2f;
                    Vector3 newPosition = corner + pushDirection * deficit;
                    SpawnMarker(newPosition, i, " New Position.");
                    NavMeshHit newHit;
                    if (NavMesh.SamplePosition(newPosition, out newHit, 2.4f, NavMesh.AllAreas))
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
            if (distToEdge < 1.2f)
            {
                SpawnMarker(edgeHit.position, order, " TOO CLOSE!!! OverCorrection!");
                Vector3 pushDirection = edgeHit.normal;
                float deficit = 1.2f - distToEdge;
                Vector3 newPosition = ((generatedPoint + pushDirection * deficit) + generatedPoint) / 2;
                SpawnMarker(newPosition, order, " New Position. Corrected!");
                NavMeshHit newHit;
                if (NavMesh.SamplePosition(newPosition, out newHit, 2.4f, NavMesh.AllAreas))
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
        if (pathCorners.Count == 0)
        {
            microState = NPCStatesMicro.Standing;
            return;
        }
        if (IsInsideCornerZone(0))
        {
            previousCornerPosition = pathCorners[0];
            pathCorners.RemoveAt(0);
            if (pathCorners.Count == 0)
            {
                microState = NPCStatesMicro.Standing;
                return;
            }
        }
        if (pathCorners.Count > 1)
        {
            float distToCurrent = Vector3.Distance(pathCorners[0], transform.position);
            float distToNext = Vector3.Distance(pathCorners[1], transform.position);
            float distFromCurrentToNext = Vector3.Distance(pathCorners[0], pathCorners[1]);
            if (distToNext < distToCurrent)
            {
                previousCornerPosition = pathCorners[0];
                pathCorners.RemoveAt(0);
            }
            else if(distToNext < distFromCurrentToNext)
            {
                UnityEngine.Debug.Log("This could cause NPCs to walk through walls if not careful!");
                previousCornerPosition = pathCorners[0];
                pathCorners.RemoveAt(0);
            }
        }
        //ProcessMovement(,);
    }

    private bool IsInsideCornerZone(int cornerIndex)
    {
        if (cornerIndex >= pathCorners.Count)
            return false;
        Vector3 cornerPosition = pathCorners[cornerIndex];
        Vector3 npcPosition = transform.position;
        cornerPosition.y = 0;
        npcPosition.y = 0;
        Vector3 prevCorner = previousCornerPosition;
        prevCorner.y = 0;
        Vector3 pathDirection = (cornerPosition - prevCorner).normalized;
        Vector3 toNPC = npcPosition - cornerPosition;
        Vector3 perpendicular = Vector3.Cross(pathDirection, Vector3.up).normalized;
        float alongPath = Vector3.Dot(toNPC, pathDirection);
        float acrossPath = Vector3.Dot(toNPC, perpendicular);
        float halfDepth = cornerZoneSize.y * 0.5f;
        float halfWidth = cornerZoneSize.x * 0.5f;
        bool withinDepth = Mathf.Abs(alongPath) <= halfDepth;
        bool withinWidth = Mathf.Abs(acrossPath) <= halfWidth;
        return withinDepth && withinWidth;
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
        }
    }

    [Rpc(SendTo.Everyone)]
    public void AssignSlotRpc(int materialIndex)
    {
        Material playerMaterial = BountyManager.Instance.playerMaterials[materialIndex];
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

    private void UnregisterFromGrid()
    {
        // Unregister from spatial grid
    }

    private void SpawnMarker(Vector3 spawnPos, int order, string label)
    {
        //GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        //cube.transform.localScale = Vector3.one * 0.5f;
        //cube.transform.position = spawnPos;
        //cube.name = order + label;
    }
}
