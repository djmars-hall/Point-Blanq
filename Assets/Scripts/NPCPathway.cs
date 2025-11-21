using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Handles pathfinding and waypoint management for NPCs.
/// Manages path corners, edge avoidance, and navigation to gathering zones.
/// </summary>
public class NPCPathway
{
    // References
    private NPCController npcController;
    private Transform transform;

    // Path data
    private Vector3 currentWaypoint;
    private List<Vector3> pathCorners = new List<Vector3>();
    private Vector3 previousCornerPosition;
    
    // Configuration
    private float pathEdgeBuffer = 0.6f; // Distance to keep from edges when adjusting corners

    // Public getters
    public Vector3 CurrentWaypoint => currentWaypoint;
    public List<Vector3> PathCorners => pathCorners;
    public Vector3 PreviousCornerPosition => previousCornerPosition;

    /// <summary>
    /// Initializes the pathway system with a reference to the NPC controller.
    /// </summary>
    /// <param name="controller">The NPC controller that owns this pathway</param>
    public NPCPathway(NPCController controller)
    {
        npcController = controller;
        transform = controller.transform;
        previousCornerPosition = transform.position;
    }

    /// <summary>
    /// Generates a new waypoint in a random gathering zone and calculates the path to it.
    /// </summary>
    /// <param name="waypointTime">Output parameter for how long to wait at the waypoint</param>
    /// <returns>True if a valid waypoint was generated, false otherwise</returns>
    public bool GenerateNewWaypoint(out float waypointTime)
    {
        waypointTime = 0f;
        
        var zones = NPCManager.Instance.gatheringZones;
        if (zones == null || zones.Length == 0)
        {
            return false;
        }

        // Pick a random gathering zone
        MapZone zone = zones[Random.Range(0, zones.Length)];

        // Use a random point within the zone as the waypoint
        Vector3 targetPoint = zone.GetRandomPointInArea();

        NavMeshHit hit;
        if (NavMesh.SamplePosition(targetPoint, out hit, 5f, NavMesh.AllAreas))
        {
            currentWaypoint = hit.position;
            CalculatePathToWaypoint(currentWaypoint);
            waypointTime = Random.Range(3.0f, 12.0f);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates a NavMesh path to the specified waypoint and adjusts corners away from edges.
    /// </summary>
    /// <param name="waypoint">The destination waypoint</param>
    private void CalculatePathToWaypoint(Vector3 waypoint)
    {
        NavMeshPath pathReturned = new NavMeshPath();
        NavMesh.CalculatePath(transform.position, waypoint, NavMesh.AllAreas, pathReturned);
        pathCorners = new List<Vector3>(pathReturned.corners);
        pathCorners = AdjustCornersAwayFromEdges(pathCorners);
        previousCornerPosition = transform.position;
    }

    /// <summary>
    /// Recalculates the path to the current waypoint without changing the destination.
    /// Used when the NPC skips a corner to prevent potential wall-walking.
    /// </summary>
    /// <returns>True if path was successfully recalculated, false otherwise</returns>
    public bool RecalculatePathToCurrentWaypoint()
    {
        if (currentWaypoint == Vector3.zero)
        {
            Debug.LogWarning($"[{npcController.name}] Cannot recalculate path - no current waypoint set");
            return false;
        }

        NavMeshPath pathReturned = new NavMeshPath();
        if (NavMesh.CalculatePath(transform.position, currentWaypoint, NavMesh.AllAreas, pathReturned))
        {
            pathCorners = new List<Vector3>(pathReturned.corners);
            pathCorners = AdjustCornersAwayFromEdges(pathCorners);
            previousCornerPosition = transform.position;
            Debug.Log($"[{npcController.name}] Path recalculated successfully with {pathCorners.Count} corners");
            return true;
        }
        else
        {
            Debug.LogWarning($"[{npcController.name}] Failed to recalculate path to current waypoint");
            return false;
        }
    }

    /// <summary>
    /// Adjusts path corners to maintain a minimum distance from NavMesh edges.
    /// Samples in multiple directions around each corner to find positions further from edges.
    /// </summary>
    /// <param name="corners">Original path corners from NavMesh</param>
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

                npcController.SpawnDebugMarker(edgeHit.position, i, " Edge Hit. Too Close?");

                // Check if the corner is too close to the edge.
                if (distToEdge < pathEdgeBuffer)
                {
                    npcController.SpawnDebugMarker(corner, i, " TOO CLOSE!!!");

                    // Calculate direction away from edge
                    Vector3 pushDirection = edgeHit.normal;
                    
                    // Calculate how much farther we need to push (plus a little to offset)
                    float deficit = (pathEdgeBuffer - distToEdge) + 0.2f;
                    
                    // Calculate the new position
                    Vector3 newPosition = corner + pushDirection * deficit;

                    npcController.SpawnDebugMarker(newPosition, i, " New Position.");

                    // Re-sample the new position on the NavMesh to ensure it is valid.
                    NavMeshHit newHit;
                    if (NavMesh.SamplePosition(newPosition, out newHit, pathEdgeBuffer * 2, NavMesh.AllAreas))
                    {
                        adjustedCorner = AdjustForOverCorrection(newHit.position, i);
                        npcController.SpawnDebugMarker(adjustedCorner, i, " Corrected Pos");
                    }
                }
            }
            
            adjustedCorners.Add(adjustedCorner);
        }
        
        return adjustedCorners;
    }

    /// <summary>
    /// Corrects positions that were over-adjusted away from edges.
    /// Ensures the adjusted position maintains the minimum buffer distance.
    /// </summary>
    /// <param name="generatedPoint">The initially adjusted position</param>
    /// <param name="order">The corner index for debug purposes</param>
    /// <returns>The final corrected position</returns>
    private Vector3 AdjustForOverCorrection(Vector3 generatedPoint, int order)
    {
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(generatedPoint, out edgeHit, NavMesh.AllAreas))
        {
            float distToEdge = edgeHit.distance;

            npcController.SpawnDebugMarker(edgeHit.position, order, " Edge Hit. Overcorrection?");

            if (distToEdge < pathEdgeBuffer)
            {
                //Generate a new point, then take the average of the two
                npcController.SpawnDebugMarker(edgeHit.position, order, " TOO CLOSE!!! OverCorrection!");

                // Calculate direction away from edge
                Vector3 pushDirection = edgeHit.normal;

                // Calculate how much farther we need to push
                float deficit = pathEdgeBuffer - distToEdge;

                // Calculate the new position (in between corrected position and newly generated position)
                Vector3 newPosition = ((generatedPoint + pushDirection * deficit) + generatedPoint) / 2;

                npcController.SpawnDebugMarker(newPosition, order, " New Position. Corrected!");

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
    /// Checks if the NPC is inside the corner visitation zone for a specific corner.
    /// The zone is oriented along the path direction with configurable width and depth.
    /// </summary>
    /// <param name="cornerIndex">The index of the corner to check</param>
    /// <returns>True if inside the corner zone, false otherwise</returns>
    public bool IsInsideCornerZone(int cornerIndex)
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
        float halfDepth = npcController.CornerZoneSize.y * 0.5f;
        float halfWidth = npcController.CornerZoneSize.x * 0.5f;

        bool withinDepth = Mathf.Abs(alongPath) <= halfDepth;
        bool withinWidth = Mathf.Abs(acrossPath) <= halfWidth;

        return withinDepth && withinWidth;
    }

    /// <summary>
    /// Calculates the closest point in the corner zone (center, left edge, or right edge).
    /// This is a reusable function that can be called from multiple places.
    /// </summary>
    /// <param name="fromPosition">The position from which to calculate the closest point (e.g., NPC's current position)</param>
    /// <param name="cornerIndex">The index of the corner to check (default is 0 for the next corner)</param>
    /// <returns>The closest point in the corner zone to the specified position</returns>
    public Vector3 GetClosestPointInCornerZone(Vector3 fromPosition, int cornerIndex = 0)
    {
        if (cornerIndex >= pathCorners.Count)
            return Vector3.zero; // Return zero vector if corner index is invalid

        Vector3 cornerPosition = pathCorners[cornerIndex];
        
        // Flatten to XZ plane
        cornerPosition.y = 0;
        fromPosition.y = 0;
        
        // Calculate path direction
        Vector3 prevCorner = previousCornerPosition;
        prevCorner.y = 0;
        Vector3 pathDirection = (cornerPosition - prevCorner).normalized;
        
        // Calculate perpendicular direction (left/right of path)
        Vector3 perpendicular = Vector3.Cross(pathDirection, Vector3.up).normalized;
        
        // Get half width of the corner zone
        float halfWidth = npcController.CornerZoneSize.x * 0.5f;
        
        // Three candidate points: center, left edge, right edge of corner zone
        Vector3 centerPoint = cornerPosition;
        Vector3 leftPoint = cornerPosition + perpendicular * halfWidth;
        Vector3 rightPoint = cornerPosition - perpendicular * halfWidth;
        
        // Find which point is closest
        float distToCenter = Vector3.Distance(fromPosition, centerPoint);
        float distToLeft = Vector3.Distance(fromPosition, leftPoint);
        float distToRight = Vector3.Distance(fromPosition, rightPoint);
        
        Vector3 targetPoint = centerPoint;
        if (distToLeft < distToCenter && distToLeft < distToRight)
        {
            targetPoint = leftPoint;
        }
        else if (distToRight < distToCenter && distToRight < distToLeft)
        {
            targetPoint = rightPoint;
        }
        
        return targetPoint;
    }

    /// <summary>
    /// Calculates the target direction for movement based on path corners.
    /// Aims for the closest point in the corner zone (center or edges).
    /// </summary>
    /// <returns>Normalized direction vector to move towards</returns>
    public Vector3 CalculateTargetDirection()
    {
        if (pathCorners.Count == 0)
            return transform.forward.normalized;

        // Calculate direction to target point from NPC's current position
        Vector3 npcPosition = transform.position;
        npcPosition.y = 0;
        
        // Use the reusable function to get the closest point
        Vector3 targetPoint = GetClosestPointInCornerZone(npcPosition, 0);
        
        Vector3 toCorner = targetPoint - npcPosition;
        toCorner.y = 0;
        return toCorner.normalized;
    }

    /// <summary>
    /// Removes the corner at the specified index and updates the previous corner position.
    /// </summary>
    /// <param name="cornerIndex">The index of the corner to remove</param>
    public void RemoveCorner(int cornerIndex)
    {
        if (cornerIndex >= 0 && cornerIndex < pathCorners.Count)
        {
            // Update previous corner position before removing the corner
            previousCornerPosition = pathCorners[cornerIndex];
            pathCorners.RemoveAt(cornerIndex);
        }
    }

    /// <summary>
    /// Checks if the NPC is closer to the next corner than the current one (corner-cutting detection).
    /// </summary>
    /// <param name="distToCurrent">Output: distance to current corner</param>
    /// <param name="distToNext">Output: distance to next corner</param>
    /// <param name="distFromCurrentToNext">Output: distance between current and next corner</param>
    /// <returns>True if should skip current corner, false otherwise</returns>
    public bool ShouldSkipCurrentCorner(out float distToCurrent, out float distToNext, out float distFromCurrentToNext)
    {
        distToCurrent = 0f;
        distToNext = 0f;
        distFromCurrentToNext = 0f;

        if (pathCorners.Count <= 1)
            return false;

        Vector3 npcPosition = transform.position;
        npcPosition.y = 0;

        // Calculate distances to the closest points in each corner zone
        distToCurrent = Vector3.Distance(npcPosition, GetClosestPointInCornerZone(npcPosition, 0));
        distToNext = Vector3.Distance(npcPosition, GetClosestPointInCornerZone(npcPosition, 1));
        
        // Calculate distance between the corner positions themselves
        distFromCurrentToNext = Vector3.Distance(pathCorners[0], pathCorners[1]);

        // Skip if closer to next corner than current or on the way to next corner
        if (distToNext < distToCurrent || distToNext < distFromCurrentToNext)
            return true;

        return false;
    }

    /// <summary>
    /// Clears all path corners.
    /// </summary>
    public void ClearPath()
    {
        pathCorners.Clear();
    }

    /// <summary>
    /// Gets the number of corners remaining in the path.
    /// </summary>
    public int GetCornerCount()
    {
        return pathCorners.Count;
    }

    /// <summary>
    /// Processes corner visitation by checking if the NPC is inside the corner zone.
    /// If inside, removes the corner and updates the state.
    /// </summary>
    /// <returns>True if path is complete (no more corners), false otherwise</returns>
    public bool ProcessCornerVisitation()
    {
        // Check if NPC is inside the corner visitation zone
        if (IsInsideCornerZone(0))
        {
            RemoveCorner(0);
            
            // Check if at end of pathway
            if (GetCornerCount() == 0)
            {
                // We made it to the end of the path
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Processes corner-cutting optimization by checking if the NPC is closer to the next corner.
    /// If corner-cutting is detected, either skips the corner or recalculates the path.
    /// </summary>
    /// <returns>True if path needs to be recalculated (early exit required), false to continue normal movement</returns>
    public bool ProcessCornerCuttingOptimization()
    {
        // Check if we're closer to the next corner than the current one (corner-cutting optimization)
        if (GetCornerCount() > 1)
        {
            if (ShouldSkipCurrentCorner(out float distToCurrent, out float distToNext, out float distFromCurrentToNext))
            {
                if (distToNext < distToCurrent)
                {
                    // Skip the current corner since we're already closer to the next one
                    RemoveCorner(0);
                }
                else if (distToNext < distFromCurrentToNext)
                {
                    // Skip the current corner since we're on our way to the next one
                    // This could cause NPCs to walk through walls - recalculate path to prevent this
                    UnityEngine.Debug.Log($"[{npcController.name}] Corner skipped, recalculating path to current waypoint to avoid walls");
                    RecalculatePathToCurrentWaypoint();
                    return true; // Signal that path was recalculated, caller should exit early
                }
            }
        }
        
        return false; // Continue normal movement
    }

    /// <summary>
    /// Checks if the next corner is behind the NPC (angle greater than 90 degrees from forward direction).
    /// </summary>
    /// <param name="angleThreshold">The angle threshold in degrees (default 90). Corners beyond this angle are considered "behind".</param>
    /// <returns>True if the next corner is behind the NPC, false otherwise</returns>
    public bool IsNextCornerBehind(float angleThreshold = 90f)
    {
        if (pathCorners.Count == 0)
            return false;

        // Get direction to next corner
        Vector3 toCorner = pathCorners[0] - transform.position;
        toCorner.y = 0; // Flatten to XZ plane
        toCorner.Normalize();

        // Get NPC's forward direction
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();

        // Calculate angle between forward and direction to corner
        float angle = Vector3.Angle(forward, toCorner);

        // If angle is greater than threshold, corner is behind
        return angle > angleThreshold;
    }
}
