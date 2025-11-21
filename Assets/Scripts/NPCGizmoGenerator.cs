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
        
        pathCorners = npcController.Pathway.PathCorners;
        if (pathCorners == null || pathCorners.Count == 0) return;
        Gizmos.color = gizmoColor;

        Vector3 previousCornerPosition = npcController.Pathway.PreviousCornerPosition;

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

        // Draw line from NPC to the closest point in the first corner zone
        if (pathCorners.Count > 0)
        {
            Vector3 closestPoint = npcController.Pathway.GetClosestPointInCornerZone(transform.position, 0);
            Gizmos.DrawLine(transform.position + offset, closestPoint + offset);
        }
        
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
    /// Draws rectangles between the avoidance distance boundary and detection radius.
    /// Red lines indicate high avoidance intensity, orange for moderate avoidance, yellow for no avoidance.
    /// Blue arrows show the detected character's velocity direction (only drawn if character is moving).
    /// </summary>
    private void DrawDetectionGizmos()
    {
        if (!showDetectionZone && !showAvoidanceDistanceBoundary && !showDetectedCharacterLines) return;

        Vector3 position = transform.position;
        Vector3 forward = transform.forward;
        float radius = npcController.DetectionRadius;
        float angle = npcController.DetectionAngle;
        float avoidanceRadius = npcController.AvoidanceDistance;

        // Draw the rectangles between avoidance distance and detection radius
        if (showDetectionZone)
        {
            DrawSemicircleRectangles(position, forward, avoidanceRadius, radius, angle);
        }
        
        // Draw the avoidance distance boundary (outline only)
        if (showAvoidanceDistanceBoundary)
        {
            DrawSemicircleOutline(position, forward, avoidanceRadius, angle);
        }

        // Draw the detection radius boundary (outline only)
        if (showDetectionZone)
        {
            DrawSemicircleOutline(position, forward, radius, angle);
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
                    else if (intensity >= 1f)
                    {
                        // Red for high avoidance (head-on collisions, urgent scenarios)
                        lineColor = Color.red;
                    }
                    else if (intensity >= 0.3f)
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

                    // Use ActualVelocity to determine if character is moving and their direction
                    Vector3 velocity = detectedCharacter.ActualVelocity;
                    velocity.y = 0; // Flatten to XZ plane
                    
                    // Only draw arrow if velocity is significant (character is moving)
                    if (velocity.magnitude > 0.01f)
                    {
                        Vector3 velocityDirection = velocity.normalized;
                        Vector3 arrowStart = detectedCharacter.transform.position + offset;
                        Vector3 arrowEnd = arrowStart + velocityDirection * 1.5f;
                        
                        Gizmos.color = npcDirectionColor;
                        Gizmos.DrawLine(arrowStart, arrowEnd);
                        DrawArrowHead(arrowEnd, velocityDirection, arrowSize);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws rectangles between the inner and outer semicircles to create a detection zone band.
    /// </summary>
    /// <param name="center">Center position of the semicircles</param>
    /// <param name="forward">Forward direction of the NPC</param>
    /// <param name="innerRadius">Inner radius (avoidance distance)</param>
    /// <param name="outerRadius">Outer radius (detection radius)</param>
    /// <param name="angle">Total angle of the semicircle</param>
    private void DrawSemicircleRectangles(Vector3 center, Vector3 forward, float innerRadius, float outerRadius, float angle)
    {
        // Flatten forward to XZ plane
        forward.y = 0;
        forward.Normalize();

        // Calculate the half angle in radians
        float halfAngleRad = (angle * 0.5f) * Mathf.Deg2Rad;

        // Create points for both semicircles
        Vector3[] innerPoints = new Vector3[semicircleSegments + 1];
        Vector3[] outerPoints = new Vector3[semicircleSegments + 1];

        for (int i = 0; i <= semicircleSegments; i++)
        {
            float t = (float)i / semicircleSegments;
            float currentAngle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
            
            // Rotate the forward vector by the current angle around Y axis
            Vector3 direction = Quaternion.Euler(0, currentAngle * Mathf.Rad2Deg, 0) * forward;
            innerPoints[i] = center + direction * innerRadius;
            outerPoints[i] = center + direction * outerRadius;
        }

        Gizmos.color = detectionZoneColor;

        // Draw rectangles between inner and outer arc segments
        for (int i = 0; i < semicircleSegments; i++)
        {
            // Draw a quad (rectangle) between segment i and i+1
            DrawQuad(
                innerPoints[i] + offset,
                innerPoints[i + 1] + offset,
                outerPoints[i + 1] + offset,
                outerPoints[i] + offset
            );
        }
    }

    /// <summary>
    /// Draws just the outline of a semicircle (arc and radial lines).
    /// </summary>
    /// <param name="center">Center position of the semicircle</param>
    /// <param name="forward">Forward direction of the NPC</param>
    /// <param name="radius">Radius of the semicircle</param>
    /// <param name="angle">Total angle of the semicircle</param>
    private void DrawSemicircleOutline(Vector3 center, Vector3 forward, float radius, float angle)
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

        // Draw arc segments
        for (int i = 0; i < semicircleSegments; i++)
        {
            Gizmos.DrawLine(points[i] + offset, points[i + 1] + offset);
        }

        // Draw lines from center to the arc edges
        Gizmos.DrawLine(center + offset, points[0] + offset);
        Gizmos.DrawLine(center + offset, points[semicircleSegments] + offset);
    }

    /// <summary>
    /// Draws a quad (4-sided polygon) by drawing lines between the four corners.
    /// </summary>
    private void DrawQuad(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
    {
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
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
    // Uses NavMesh.Raycast to ensure no walls or obstacles block the path.
    private (bool, Vector3) IsDirectionSafeGizmoWithEdge(Vector3 direction, float checkDistance, float detectionDistance)
    {
        direction.y = 0;
        direction.Normalize();
        Vector3 checkPosition = transform.position + direction * checkDistance;
        
        // First check: Use NavMesh.Raycast to check if there's a clear path (no obstacles/walls)
        NavMeshHit raycastHit;
        if (NavMesh.Raycast(transform.position, checkPosition, out raycastHit, NavMesh.AllAreas))
        {
            // Raycast hit something - there's an obstacle or edge in this direction
            return (false, raycastHit.position);
        }
        
        // Second check: Ensure the end position is still on NavMesh with tight tolerance
        NavMeshHit sampleHit;
        if (!NavMesh.SamplePosition(checkPosition, out sampleHit, 0.5f, NavMesh.AllAreas))
        {
            // Position is off NavMesh - not safe
            return (false, Vector3.zero);
        }
        
        // Third check: Verify the sampled position isn't too close to an edge
        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(sampleHit.position, out edgeHit, NavMesh.AllAreas))
        {
            bool safe = edgeHit.distance >= detectionDistance;
            return (safe, edgeHit.position);
        }
        
        return (true, Vector3.zero);
    }
}
