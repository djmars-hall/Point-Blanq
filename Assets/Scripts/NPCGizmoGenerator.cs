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
    [SerializeField] private bool showDetectedCharacterLines = true;
    [SerializeField] private Color detectionZoneColor = Color.cyan;
    [SerializeField] private Color detectionLineColor = Color.red;
    [SerializeField] private Color npcDirectionColor = Color.blue;
    [SerializeField] private float arrowSize = 0.5f;
    [SerializeField] private int semicircleSegments = 30; // Number of segments to draw the semicircle

    [Header("RVO Debug Visualization")]
    [Tooltip("Show collision trajectories and predictions for each neighbor")]
    [SerializeField] private bool showCollisionPredictions = true;
    [Tooltip("Show sampled candidate velocities in Scene View")]
    [SerializeField] private bool showCandidateVelocities = false;

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

        //Draw velocity arrows
        DrawVelocityArrows();

        // Draw RVO collision predictions if enabled
        DrawRVOCollisionPredictions();

        // Draw candidate velocities if enabled
        DrawCandidateVelocities();
    }

    private void DrawPathGizmos()
    {
        //Return Conditionals
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
        //Return Conditionals
        if (!showDetectionZone && !showDetectedCharacterLines) return;

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
    /// Generates arc points for a semicircle.
    /// </summary>
    /// <param name="center">Center position of the semicircle</param>
    /// <param name="forward">Forward direction of the NPC</param>
    /// <param name="radius">Radius of the semicircle</param>
    /// <param name="angle">Total angle of the semicircle</param>
    /// <returns>Array of points along the arc</returns>
    private Vector3[] GenerateArcPoints(Vector3 center, Vector3 forward, float radius, float angle)
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

        return points;
    }

    /// <summary>
    /// Draws arc segments connecting an array of points.
    /// </summary>
    /// <param name="points">Array of points to connect</param>
    /// <param name="applyOffset">Whether to apply the vertical offset</param>
    private void DrawArcSegments(Vector3[] points, bool applyOffset = true)
    {
        Vector3 offsetToApply = applyOffset ? offset : Vector3.zero;
        
        for (int i = 0; i < points.Length - 1; i++)
        {
            Gizmos.DrawLine(points[i] + offsetToApply, points[i + 1] + offsetToApply);
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
        // Generate points for both semicircles
        Vector3[] innerPoints = GenerateArcPoints(center, forward, innerRadius, angle);
        Vector3[] outerPoints = GenerateArcPoints(center, forward, outerRadius, angle);

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
        Vector3[] points = GenerateArcPoints(center, forward, radius, angle);

        Gizmos.color = detectionZoneColor;

        // Draw arc segments
        DrawArcSegments(points);

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
        Vector3[] points = GenerateArcPoints(center, forward, radius, angle);

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
            DrawArcSegments(points);
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
        //Return Conditionals
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

    /// <summary>
    /// Draws the actual RVO collision predictions: trajectories, time-to-collision, and closest approach points.
    /// This uses the collision details from the RVODebugData structure.
    /// </summary>
    private void DrawRVOCollisionPredictions()
    {
        if (!showCollisionPredictions) return;
        if (!npcController.HasRVODebugData) return;

        RVOSystem.RVODebugData debugData = npcController.LastRVODebugData;
        if (debugData.velocityEvaluations == null || debugData.velocityEvaluations.Count == 0) return;

        Vector3 basePos = transform.position + Vector3.up * 0.5f;
        float effectiveRadius = npcController.RvoAgentRadius * npcController.PersonalSpaceMultiplier;

        // Get the chosen velocity evaluation (contains collision predictions for the actual path)
        int chosenIndex = debugData.chosenVelocityIndex;
        if (chosenIndex < 0 || chosenIndex >= debugData.velocityEvaluations.Count) return;

        RVOSystem.VelocityEvaluation chosenEvaluation = debugData.velocityEvaluations[chosenIndex];
        if (chosenEvaluation.collisionDetails == null || chosenEvaluation.collisionDetails.Count == 0) return;

        // Draw collision predictions for each neighbor
        foreach (var collision in chosenEvaluation.collisionDetails)
        {
            Vector3 neighborPos3D = new Vector3(collision.neighbor.position.x, basePos.y, collision.neighbor.position.z);

            // Draw line to neighbor
            Gizmos.color = new Color(1f, 1f, 0f, 0.5f); // Yellow
            Gizmos.DrawLine(basePos, neighborPos3D);

            // Draw predicted trajectories
            Vector3 myPos3D = new Vector3(collision.myPositionAtClosest.x, basePos.y, collision.myPositionAtClosest.y);
            Vector3 neighborPos3D_closest = new Vector3(collision.neighborPositionAtClosest.x, basePos.y, collision.neighborPositionAtClosest.y);

            // Color based on collision status
            Gizmos.color = collision.willCollide ? new Color(1f, 0f, 0f, 0.7f) : new Color(0f, 1f, 0f, 0.7f); // Red if collision, green if safe
            
            // Draw my predicted trajectory
            Vector2 myVel = debugData.chosenVelocity;
            Gizmos.DrawLine(basePos, myPos3D);
            DrawArrowHead(myPos3D, new Vector3(myVel.x, 0f, myVel.y).normalized, Gizmos.color, 0.3f);
            
            // Draw neighbor's predicted trajectory
            Vector2 neighborVel = new Vector2(collision.neighbor.velocity.x, collision.neighbor.velocity.z);
            Gizmos.DrawLine(neighborPos3D, neighborPos3D_closest);
            DrawArrowHead(neighborPos3D_closest, new Vector3(neighborVel.x, 0f, neighborVel.y).normalized, Gizmos.color, 0.3f);

            // Draw spheres at closest approach points
            Gizmos.DrawWireSphere(myPos3D, effectiveRadius);
            Gizmos.DrawWireSphere(neighborPos3D_closest, effectiveRadius);

            // Draw line between closest approach points
            Gizmos.color = collision.willCollide ? Color.red : Color.green;
            Gizmos.DrawLine(myPos3D, neighborPos3D_closest);

#if UNITY_EDITOR
            // Label with time and distance info
            UnityEditor.Handles.color = Gizmos.color;
            Vector3 labelPos = (myPos3D + neighborPos3D_closest) * 0.5f + Vector3.up * 0.3f;
            string label = $"t={collision.timeToClosest:F2}s\nd={collision.closestDistance:F2}m";
            if (collision.willCollide)
            {
                label += "\nCOLLISION!";
            }
            UnityEditor.Handles.Label(labelPos, label);
#endif
        }
    }

    private void DrawVelocityArrows()
    {
        Vector3 basePos = transform.position + Vector3.up * 0.5f;
        float arrowScale = 0.5f;

        // Blue arrow: Preferred velocity (where we want to go)
        if (npcController.PreferredRVOVelocity.magnitude > 0.01f)
        {
            Vector3 preferredDir = new Vector3(npcController.PreferredRVOVelocity.x, 0f, npcController.PreferredRVOVelocity.y);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(basePos, basePos + preferredDir * arrowScale);
            DrawArrowHead(basePos + preferredDir * arrowScale, preferredDir.normalized, Color.blue, 0.2f);
        }

        // Cyan arrow: Flow-adjusted velocity (shows lane formation influence)
        if (npcController.RvoFlowBias > 0.01f && npcController.PreferredRVOVelocity.magnitude > 0.01f)
        {
            Vector2 flowAdjusted = npcController.CalculateFlowFieldPublic(npcController.PreferredRVOVelocity);
            if ((flowAdjusted - npcController.PreferredRVOVelocity).magnitude > 0.05f) // Only show if different
            {
                Vector3 flowDir = new Vector3(flowAdjusted.x, 0f, flowAdjusted.y);
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(basePos, basePos + flowDir * arrowScale);
                DrawArrowHead(basePos + flowDir * arrowScale, flowDir.normalized, Color.cyan, 0.15f);
            }
        }

        // Yellow arrow: RVO computed velocity (collision-free velocity)
        if (npcController.CurrentRVOVelocity.magnitude > 0.01f)
        {
            Vector3 rvoDir = new Vector3(npcController.CurrentRVOVelocity.x, 0f, npcController.CurrentRVOVelocity.y);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(basePos, basePos + rvoDir * arrowScale);
            DrawArrowHead(basePos + rvoDir * arrowScale, rvoDir.normalized, Color.yellow, 0.2f);
        }

        // Green arrow: Actual velocity (from Rigidbody)
        if (npcController.ActualVelocity.magnitude > 0.01f)
        {
            Vector3 actualDir = new Vector3(npcController.ActualVelocity.x, 0f, npcController.ActualVelocity.z);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(basePos, basePos + actualDir * arrowScale);
            DrawArrowHead(basePos + actualDir * arrowScale, actualDir.normalized, Color.green, 0.2f);
        }
    }

    private void DrawArrowHead(Vector3 tip, Vector3 direction, Color color, float size)
    {
        Gizmos.color = color;
        Vector3 right = Quaternion.Euler(0, 30, 0) * -direction * size;
        Vector3 left = Quaternion.Euler(0, -30, 0) * -direction * size;
        Gizmos.DrawLine(tip, tip + right);
        Gizmos.DrawLine(tip, tip + left);
    }

    /// <summary>
    /// Draws the candidate velocities tested during RVO computation.
    /// Green arrows for safe velocities, Red arrows for collisions.
    /// Uses the RVODebugData structure from RVOSystem.
    /// </summary>
    private void DrawCandidateVelocities()
    {
        if (!showCandidateVelocities) return;
        if (!npcController.HasRVODebugData) return;

        RVOSystem.RVODebugData debugData = npcController.LastRVODebugData;
        if (debugData.sampledVelocities == null || debugData.sampledVelocities.Count == 0) return;

        Vector3 basePos = transform.position + Vector3.up * 1f;
        float arrowScale = 0.3f;

        // Draw each sampled velocity
        for (int i = 0; i < debugData.sampledVelocities.Count; i++)
        {
            Vector2 sampledVel = debugData.sampledVelocities[i];
            RVOSystem.VelocityEvaluation evaluation = debugData.velocityEvaluations[i];

            Vector3 dir3D = new Vector3(sampledVel.x, 0f, sampledVel.y);
            
            // Choose color based on safety
            Gizmos.color = evaluation.isSafe ? Color.green : Color.red;
            
            Gizmos.DrawLine(basePos, basePos + dir3D * arrowScale);
            DrawArrowHead(basePos + dir3D * arrowScale, dir3D.normalized, Gizmos.color, 0.2f);
        }

        // Draw the chosen velocity (thicker arrow in white)
        Vector2 chosenVel = debugData.chosenVelocity;
        if (chosenVel.magnitude > 0.01f)
        {
            Vector3 chosenDir3D = new Vector3(chosenVel.x, 0f, chosenVel.y);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(basePos, basePos + chosenDir3D * arrowScale * 1.5f);
            DrawArrowHead(basePos + chosenDir3D * arrowScale * 1.5f, chosenDir3D.normalized, Color.white, 0.25f);
        }
    }
}
