using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class NPCGizmoGenerator : MonoBehaviour
{
    [Header("General Gizmo Settings")]
    [SerializeField] private bool onlyShowWhenSelected = false;

    [Header("Path Gizmo Settings")]
    [SerializeField] private bool showPaths = true;
    [SerializeField] private Color gizmoColor = Color.magenta;
    [SerializeField] private float gizmoRadius = 0.4f;
    [SerializeField] private Color cornerZoneColor = new Color(1f, 0f, 1f, 0.3f); // Semi-transparent magenta for zones
    private List<Vector3> pathCorners;
    private Vector3 offset = new Vector3(0, 1.2f, 0);

    [Header("NPC Detection Gizmo Settings")]
    [SerializeField] private bool showDetectionZone = true;
    [SerializeField] private Color detectionZoneColor = new Color(0f, 1f, 1f, 0.2f); // Cyan semi-transparent
    [SerializeField] private Color detectionZoneOutlineColor = new Color(0f, 1f, 1f, 0.8f); // Cyan outline
    [SerializeField] private Color detectionLineColor = Color.yellow;
    [SerializeField] private Color npcDirectionColor = Color.red;
    [SerializeField] private float arrowSize = 0.5f;
    [SerializeField] private int semicircleSegments = 30; // Number of segments to draw the semicircle

    private NPCController npcController;

    private void Start()
    {
        npcController = GetComponent<NPCController>();
    }

    private void OnDrawGizmos()
    {
        if(onlyShowWhenSelected) return;
        DrawPathGizmos();
        DrawDetectionGizmos();
        DrawEdgeAvoidanceGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        DrawPathGizmos();
        DrawDetectionGizmos();
        DrawEdgeAvoidanceGizmos();
    }

    private void DrawPathGizmos()
    {
        if (!showPaths) return;
        if (npcController == null) return;
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;

        pathCorners = npcController.PathCorners;
        if (pathCorners == null || pathCorners.Count == 0) return;
        Gizmos.color = gizmoColor;

        Vector3 previousCornerPosition = npcController.PreviousCornerPosition;

        for (int i = 0; i < pathCorners.Count; i++)
        {
            Vector3 cornerPosition = pathCorners[i] + offset;
            Vector3 prevCorner = (i == 0) ? previousCornerPosition : pathCorners[i - 1];
            Vector3 directionToPrevious = (cornerPosition - (prevCorner + offset)).normalized;
            DrawCornerZone(i, cornerPosition, directionToPrevious);
            if (i < pathCorners.Count - 1)
            {
                Gizmos.DrawLine(pathCorners[i] + offset, pathCorners[i + 1] + offset);
            }
        }

        Gizmos.DrawLine(transform.position + offset, pathCorners[0] + offset);
        Gizmos.DrawSphere(transform.position + offset, gizmoRadius);
    }

    private void DrawCornerZone(int cornerIndex, Vector3 position, Vector3 direction)
    {
        Vector2 zoneSize = npcController.CornerZoneSize;
        direction.y = 0;
        direction.Normalize();
        Quaternion rotation = (direction.sqrMagnitude > 0.001f) ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        Vector3 boxSize = new Vector3(zoneSize.x, 0.5f, zoneSize.y);
        Color previousColor = Gizmos.color;
        Gizmos.color = cornerZoneColor;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, boxSize);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
        Gizmos.matrix = oldMatrix;
        Gizmos.color = previousColor;
    }

    /// <summary>
    /// Draws the NPC detection zone and detected NPCs with connection lines and directional arrows.
    /// </summary>
    private void DrawDetectionGizmos()
    {
        if (!showDetectionZone) return;
        if (npcController == null) return;

        Vector3 position = transform.position;
        Vector3 forward = transform.forward;
        float radius = npcController.DetectionRadius;
        float angle = npcController.DetectionAngle;

        // Draw the filled semicircle detection zone
        DrawFilledSemicircle(position, forward, radius, angle);

        // Draw lines to detected NPCs and their directional arrows
        List<NPCController> detectedNPCs = npcController.DetectedNPCs;
        if (detectedNPCs != null && detectedNPCs.Count > 0)
        {
            foreach (var detectedNPC in detectedNPCs)
            {
                if (detectedNPC == null) continue;

                // Draw line from this NPC to detected NPC
                Gizmos.color = detectionLineColor;
                Gizmos.DrawLine(position + offset, detectedNPC.transform.position + offset);

                // Draw arrow showing the detected NPC's facing direction
                Vector3 npcForward = detectedNPC.transform.forward;
                Vector3 arrowStart = detectedNPC.transform.position + offset;
                Vector3 arrowEnd = arrowStart + npcForward * 1.5f;
                
                Gizmos.color = npcDirectionColor;
                Gizmos.DrawLine(arrowStart, arrowEnd);
                DrawArrowHead(arrowEnd, npcForward, arrowSize);
            }
        }
    }

    /// <summary>
    /// Draws a filled semicircle in the forward direction of the NPC.
    /// </summary>
    private void DrawFilledSemicircle(Vector3 center, Vector3 forward, float radius, float angle)
    {
        // Flatten forward to XZ plane
        forward.y = 0;
        forward.Normalize();

        // Calculate the half angle in radians
        float halfAngleRad = (angle * 0.5f) * Mathf.Deg2Rad;

        // Create points for the semicircle
        Vector3[] points = new Vector3[semicircleSegments + 2];
        points[0] = center; // Center point

        for (int i = 0; i <= semicircleSegments; i++)
        {
            float t = (float)i / semicircleSegments;
            float currentAngle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
            
            // Rotate the forward vector by the current angle around Y axis
            Vector3 direction = Quaternion.Euler(0, currentAngle * Mathf.Rad2Deg, 0) * forward;
            points[i + 1] = center + direction * radius;
        }

        // Draw filled triangles
        Gizmos.color = detectionZoneColor;
        for (int i = 1; i <= semicircleSegments; i++)
        {
            DrawTriangle(points[0] + offset, points[i] + offset, points[i + 1] + offset);
        }

        // Draw lines from center to edges
        Gizmos.DrawLine(points[0] + offset, points[1] + offset);
        Gizmos.DrawLine(points[0] + offset, points[semicircleSegments + 1] + offset);
    }

    /// <summary>
    /// Draws a filled triangle by drawing lines between vertices.
    /// </summary>
    private void DrawTriangle(Vector3 p1, Vector3 p2, Vector3 p3)
    {
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p1);
    }

    private void DrawArrowHead(Vector3 tip, Vector3 direction, float size)
    {
        Vector3 normalizedDir = direction.normalized;
        Vector3 basePoint = tip - normalizedDir * size;
        Vector3 perpendicular = Vector3.Cross(normalizedDir, Vector3.up).normalized * (size * 0.5f);
        Gizmos.DrawLine(tip, basePoint + perpendicular);
        Gizmos.DrawLine(tip, basePoint - perpendicular);
        Gizmos.DrawLine(basePoint + perpendicular, basePoint - perpendicular);
    }

    /// <summary>
    /// Draws edge avoidance rays: forward (red/green), and alternating left/right rays (red if edge, green if safe).
    /// Stops after finding the first safe direction, matching the actual NPC behavior.
    /// </summary>
    private void DrawEdgeAvoidanceGizmos()
    {
        if (npcController == null) return;
        if (!npcController.enabled) return;
        if (!npcController.gameObject.activeInHierarchy) return;
        if (!npcController.EnableEdgeAvoidance) return;

        Vector3 position = transform.position + offset;
        float checkDistance = npcController.EdgeCheckAheadDistance;
        float detectionDistance = npcController.EdgeDetectionDistance;
        int maxAttempts = npcController.MaxRaycastAttempts;
        float angleIncrement = npcController.RaycastAngleIncrement;

        // Forward direction
        Vector3 forward = transform.forward;
        forward.y = 0;
        forward.Normalize();
        Vector3 forwardCheckPos = position + forward * checkDistance;

        bool forwardIsSafe = IsDirectionSafeGizmo(forward, checkDistance, detectionDistance);
        Gizmos.color = forwardIsSafe ? Color.green : Color.red;
        Gizmos.DrawLine(position, forwardCheckPos);
        DrawArrowHead(forwardCheckPos, forward, 0.5f);

        // If forward is not safe, check alternating directions and STOP at first safe one
        if (!forwardIsSafe)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                float angle = angleIncrement * i;
                float checkAngle = (i % 2 == 1) ? angle : -angle;
                Vector3 checkDir = Quaternion.Euler(0, checkAngle, 0) * forward;
                checkDir.y = 0;
                checkDir.Normalize();
                Vector3 checkPos = position + checkDir * checkDistance;
                
                bool isSafe = IsDirectionSafeGizmo(checkDir, checkDistance, detectionDistance);
                Gizmos.color = isSafe ? Color.green : Color.red;
                Gizmos.DrawLine(position, checkPos);
                DrawArrowHead(checkPos, checkDir, 0.5f);
                
                // If this direction is safe, mark it with a sphere and STOP checking
                if (isSafe)
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(checkPos, 0.3f);
                    break; // Stop checking after finding first safe direction
                }
            }
        }
    }

    // Helper for gizmo edge check (matches NPCController logic)
    private bool IsDirectionSafeGizmo(Vector3 direction, float checkDistance, float detectionDistance)
    {
        direction.y = 0;
        direction.Normalize();
        Vector3 checkPosition = transform.position + direction * checkDistance;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(checkPosition, out hit, checkDistance * 1.5f, NavMesh.AllAreas))
        {
            return false;
        }
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(hit.position, out edgeHit, NavMesh.AllAreas))
        {
            return edgeHit.distance >= detectionDistance;
        }
        return true;
    }
}
