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
    [SerializeField] private bool showAvoidanceDistanceBoundary = true;
    [SerializeField] private bool showDetectedCharacterLines = true;
    [SerializeField] private Color detectionZoneColor = Color.cyan;
    [SerializeField] private Color detectionLineColor = Color.red;
    [SerializeField] private Color npcDirectionColor = Color.blue;
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
        if (npcController == null) return;
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;
        DrawPathGizmos();
        DrawDetectionGizmos();
        DrawEdgeAvoidanceGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (npcController == null) return;
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;
        DrawPathGizmos();
        DrawDetectionGizmos();
        DrawEdgeAvoidanceGizmos();
    }

    private void DrawPathGizmos()
    {
        if (!showPaths) return;
        
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
    /// Red lines indicate high avoidance intensity, orange for moderate avoidance, yellow for no avoidance.
    /// </summary>
    private void DrawDetectionGizmos()
    {
        if (!showDetectionZone && !showAvoidanceDistanceBoundary && !showDetectedCharacterLines) return;

        Vector3 position = transform.position;
        Vector3 forward = transform.forward;
        float radius = npcController.DetectionRadius;
        float angle = npcController.DetectionAngle;

        // Draw the filled semicircle detection zone
        if (showDetectionZone)
        {
            DrawSemicircle(position, forward, radius, angle, filled: true);
        }
        
        // Draw the avoidance distance boundary (outline only)
        if (showAvoidanceDistanceBoundary)
        {
            float avoidanceRadius = npcController.AvoidanceDistance;
            DrawSemicircle(position, forward, avoidanceRadius, angle, filled: false);
        }

        // Get detected characters with their intensities
        if (showDetectedCharacterLines)
        {
            Dictionary<BaseCharController, float> detectedCharacterIntensities = npcController.DetectedCharacterIntensities;
            
            if (detectedCharacterIntensities != null && detectedCharacterIntensities.Count > 0)
            {
                foreach (var kvp in detectedCharacterIntensities)
                {
                    BaseCharController detectedCharacter = kvp.Key;
                    float intensity = kvp.Value;
                    
                    if (detectedCharacter == null) continue;

                    // Determine line color based on avoidance intensity
                    Color lineColor;
                    if (intensity <= 0.0f)
                    {
                        // Yellow for no avoidance (detected but not avoided)
                        lineColor = Color.yellow;
                    }
                    else if (intensity >= 1.5f)
                    {
                        // Red for high avoidance (head-on collisions, urgent scenarios)
                        lineColor = Color.red;
                    }
                    else if (intensity >= 0.5f)
                    {
                        // Orange for moderate avoidance
                        lineColor = new Color(1f, 0.5f, 0f); // Orange
                    }
                    else
                    {
                        // Yellow for low avoidance
                        lineColor = Color.yellow;
                    }

                    // Draw line from this NPC to detected character
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(position + offset, detectedCharacter.transform.position + offset);

                    // Draw arrow showing the detected character's facing direction
                    Vector3 characterForward = detectedCharacter.transform.forward;
                    Vector3 arrowStart = detectedCharacter.transform.position + offset;
                    Vector3 arrowEnd = arrowStart + characterForward * 1.5f;
                    
                    Gizmos.color = npcDirectionColor;
                    Gizmos.DrawLine(arrowStart, arrowEnd);
                    DrawArrowHead(arrowEnd, characterForward, arrowSize);
                }
            }
        }
    }

    /// <summary>
    /// Draws a semicircle in the forward direction of the NPC.
    /// </summary>
    /// <param name="center">Center position of the semicircle</param>
    /// <param name="forward">Forward direction of the NPC</param>
    /// <param name="radius">Radius of the semicircle</param>
    /// <param name="angle">Total angle of the semicircle</param>
    /// <param name="filled">If true, draws filled triangles; if false, draws outline only</param>
    private void DrawSemicircle(Vector3 center, Vector3 forward, float radius, float angle, bool filled)
    {
        // Flatten forward to XZ plane
        forward.y = 0;
        forward.Normalize();

        // Calculate the half angle in radians
        float halfAngleRad = (angle * 0.5f) * Mathf.Deg2Rad;

        // Create points for the semicircle
        Vector3[] points = new Vector3[semicircleSegments + 1];

        for (int i = 0; i <= semicircleSegments; i++)
        {
            float t = (float)i / semicircleSegments;
            float currentAngle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
            
            // Rotate the forward vector by the current angle around Y axis
            Vector3 direction = Quaternion.Euler(0, currentAngle * Mathf.Rad2Deg, 0) * forward;
            points[i] = center + direction * radius;
        }

        Gizmos.color = detectionZoneColor;

        if (filled)
        {
            // Draw filled triangles from center to each pair of arc points
            for (int i = 0; i < semicircleSegments; i++)
            {
                DrawTriangle(center + offset, points[i] + offset, points[i + 1] + offset);
            }
        }
        else
        {
            // Draw outline only - arc segments
            for (int i = 0; i < semicircleSegments; i++)
            {
                Gizmos.DrawLine(points[i] + offset, points[i + 1] + offset);
            }
        }

        // Draw lines from center to the arc edges (for both filled and outline)
        Gizmos.DrawLine(center + offset, points[0] + offset);
        Gizmos.DrawLine(center + offset, points[semicircleSegments] + offset);
    }

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

        bool forwardIsSafe;
        Vector3 forwardEdgeHitPos;
        (forwardIsSafe, forwardEdgeHitPos) = IsDirectionSafeGizmoWithEdge(forward, checkDistance, detectionDistance);
        Gizmos.color = forwardIsSafe ? Color.green : Color.red;
        Gizmos.DrawLine(position, forwardCheckPos);
        DrawArrowHead(forwardCheckPos, forward, forwardIsSafe ? 0.75f : 0.5f);
        if (!forwardIsSafe && forwardEdgeHitPos != Vector3.zero)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(forwardCheckPos, forwardEdgeHitPos + offset);
        }

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
                
                bool isSafe;
                Vector3 edgeHitPos;
                (isSafe, edgeHitPos) = IsDirectionSafeGizmoWithEdge(checkDir, checkDistance, detectionDistance);
                Gizmos.color = isSafe ? Color.green : Color.red;
                Gizmos.DrawLine(position, checkPos);
                DrawArrowHead(checkPos, checkDir, isSafe ? 0.75f : 0.5f);
                if (!isSafe && edgeHitPos != Vector3.zero)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(checkPos, edgeHitPos + offset);
                }
                // If this direction is safe, STOP checking
                if (isSafe)
                {
                    break; // Stop checking after finding first safe direction
                }
            }
        }
    }

    // Helper for gizmo edge check (returns if safe and edge hit position)
    private (bool, Vector3) IsDirectionSafeGizmoWithEdge(Vector3 direction, float checkDistance, float detectionDistance)
    {
        direction.y = 0;
        direction.Normalize();
        Vector3 checkPosition = transform.position + direction * checkDistance;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(checkPosition, out hit, checkDistance * 1.5f, NavMesh.AllAreas))
        {
            return (false, Vector3.zero);
        }
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(hit.position, out edgeHit, NavMesh.AllAreas))
        {
            bool safe = edgeHit.distance >= detectionDistance;
            return (safe, edgeHit.position);
        }
        return (true, Vector3.zero);
    }
}
