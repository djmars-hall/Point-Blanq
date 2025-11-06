using UnityEngine;
using System.Collections.Generic;

public class NPCGizmoGenerator : MonoBehaviour
{
    [Header("General Settings")]
    [SerializeField] private bool showONLYWhenSelected = true;

    [Header("Path Gizmo Settings")]
    [SerializeField] private bool showPaths = true;
    [SerializeField] private Color gizmoColor = Color.magenta;
    [SerializeField] private float gizmoRadius = 0.4f;
    [SerializeField] private Color cornerZoneColor = new Color(1f, 0f, 1f, 0.3f); // Semi-transparent magenta for zones
    private List<Vector3> pathCorners;

    private Vector3 offset = new Vector3(0, 1.2f, 0);

    [Header("Heuristic Gizmo Settings")]
    [SerializeField] private bool showHeuristics = true;
    [SerializeField] private Color cornerHeuristicColor = Color.green;
    [SerializeField] private Color edgeHeuristicColor = new Color(1f, 0f, 1f); // Magenta
    [SerializeField] private Color desiredDirectionColor = Color.cyan;
    [SerializeField] private float heuristicLineScale = 2f;
    [SerializeField] private float smallArrowSize = 0.15f;
    
    [Header("Character Heuristic Colors")]
    [SerializeField] private Color closestCharacterColor = Color.red;

    [Header("Density Visualization")]
    [SerializeField] private bool showDensityIndicator = true;
    [SerializeField] private Vector3 densityIndicatorOffset = new Vector3(0, 3f, 0); // Above the NPC
    [SerializeField] private float densityBarWidth = 4f; // Maximum berth size (when sparse/green)
    [SerializeField] private float densityBarHeight = 0.5f;
    [SerializeField] private Color sparseDensityColor = Color.green; // Low density = large berth needed
    [SerializeField] private Color crowdedDensityColor = Color.red; // High density = small berth (can be close)

    private NPCController npcController;

    private void Start()
    {
        npcController = GetComponent<NPCController>();
    }

    private void OnDrawGizmos()
    {
        if (showONLYWhenSelected) return;
        DrawPathGizmos();
        DrawHeuristicGizmos();
        DrawAvoidanceRadiusGizmo();
        DrawDensityIndicator();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showONLYWhenSelected) return;
        DrawPathGizmos();
        DrawHeuristicGizmos();
        DrawAvoidanceRadiusGizmo();
        DrawDensityIndicator();
    }

    private void DrawPathGizmos()
    {
        if (!showPaths) return;
        
        if (npcController == null) return;
        
        // Don't draw gizmos if NPC is in standing mode
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;

        pathCorners = npcController.PathCorners;

        if (pathCorners == null || pathCorners.Count == 0) return;
        Gizmos.color = gizmoColor;

        // Get the previous corner position from the controller
        Vector3 previousCornerPosition = npcController.PreviousCornerPosition;

        // Draw spheres at each corner and lines between them
        for (int i = 0; i < pathCorners.Count; i++)
        {
            Vector3 cornerPosition = pathCorners[i] + offset;
            
            // Calculate the direction from the previous corner position to this corner
            Vector3 prevCorner = (i == 0) ? previousCornerPosition : pathCorners[i - 1];
            Vector3 directionToPrevious = (cornerPosition - (prevCorner + offset)).normalized;
            
            // Draw corner visitation zone
            DrawCornerZone(i, cornerPosition, directionToPrevious);
            
            // Draw line to next corner
            if (i < pathCorners.Count - 1)
            {
                Gizmos.DrawLine(pathCorners[i] + offset, pathCorners[i + 1] + offset);
            }
        }

        // Draw first line and sphere from NPC to first corner
        Gizmos.DrawLine(transform.position + offset, pathCorners[0] + offset);
        Gizmos.DrawSphere(transform.position + offset, gizmoRadius);
    }

    /// <summary>
    /// Draws the corner visitation zone as a semi-transparent box using the zone size from NPCController
    /// </summary>
    /// <param name="cornerIndex">Index of the corner</param>
    /// <param name="position">Center position of the zone</param>
    /// <param name="direction">Direction perpendicular to the zone</param>
    private void DrawCornerZone(int cornerIndex, Vector3 position, Vector3 direction)
    {
        // Get the zone size from the NPC controller to match the actual collision zone
        Vector2 zoneSize = npcController.CornerZoneSize;
        
        // Flatten direction to XZ plane
        direction.y = 0;
        direction.Normalize();
        
        // Calculate the rotation to make the zone perpendicular to the direction
        // Check if direction is valid before creating rotation
        Quaternion rotation = (direction.sqrMagnitude > 0.001f) ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        
        // Create the zone dimensions
        // zoneSize.x = width (perpendicular to path), zoneSize.y = depth (along path)
        Vector3 boxSize = new Vector3(zoneSize.x, 0.5f, zoneSize.y);
        
        // Draw the semi-transparent zone
        Color previousColor = Gizmos.color;
        Gizmos.color = cornerZoneColor;
        
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, boxSize);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
        Gizmos.matrix = oldMatrix;
        
        Gizmos.color = previousColor;
    }

    private void DrawHeuristicGizmos()
    {
        if (!showHeuristics) return;

        if (npcController == null) return;
        
        // Don't draw gizmos if NPC is in standing mode
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;

        Vector3 npcPosition = transform.position + offset;

        // Draw Corner Heuristic (direction to waypoint)
        Vector3 cornerHeuristic = npcController.CornerHeuristic;
        if (cornerHeuristic.magnitude > 0.01f)
        {
            Gizmos.color = cornerHeuristicColor;
            Vector3 endPoint = npcPosition + cornerHeuristic * heuristicLineScale;
            Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
            DrawArrowHead(endPoint, cornerHeuristic, smallArrowSize);
        }

        // Draw Edge Heuristic (NavMesh edge avoidance)
        Vector3 edgeHeuristic = npcController.EdgeHeuristic;
        if (edgeHeuristic.magnitude > 0.01f)
        {
            Gizmos.color = edgeHeuristicColor;
            Vector3 endPoint = npcPosition + edgeHeuristic * heuristicLineScale;
            Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
            DrawArrowHead(endPoint, edgeHeuristic, smallArrowSize);
        }

        // Draw Character Heuristic (only the closest character)
        Vector3 characterHeuristic = npcController.CharacterHeuristic;
        if (characterHeuristic.magnitude > 0.01f)
        {
            Gizmos.color = closestCharacterColor;
            Vector3 endPoint = npcPosition + characterHeuristic * heuristicLineScale;
            Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
            DrawArrowHead(endPoint, characterHeuristic, smallArrowSize);
        }

        // Draw Desired Movement (final combined heuristic) - Most prominent
        Vector3 desiredMovement = npcController.DesiredMovement;
        if (desiredMovement.magnitude > 0.01f)
        {
            Gizmos.color = desiredDirectionColor;
            Vector3 endPoint = npcPosition + desiredMovement * heuristicLineScale;

            Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
            DrawArrowHead(endPoint, desiredMovement, 0.5f);
        }
    }

    private void DrawAvoidanceRadiusGizmo()
    {
        // Implementation for drawing avoidance radius gizmo can be added here
    }

    /// <summary>
    /// Draws a border around the bar
    /// </summary>
    private void DrawBarBorder(Vector3 center, float width, float height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        Vector3 topLeft = center + new Vector3(-halfWidth, halfHeight, 0);
        Vector3 topRight = center + new Vector3(halfWidth, halfHeight, 0);
        Vector3 bottomLeft = center + new Vector3(-halfWidth, -halfHeight, 0);
        Vector3 bottomRight = center + new Vector3(halfWidth, -halfHeight, 0);

        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
        Gizmos.DrawLine(bottomLeft, topLeft);
    }

    /// <summary>
    /// Draws a bar indicator showing the local density (crowdedness) of the area
    /// </summary>
    private void DrawDensityIndicator()
    {
        if (!showDensityIndicator) return;
        if (npcController == null) return;
        
        // Don't draw if NPC is in standing mode
        if (npcController.MicroState == NPCController.NPCStatesMicro.Standing) return;

        float density = npcController.LocalDensity;
        Vector3 barCenter = transform.position + densityIndicatorOffset;

        // INVERTED: Size represents avoidance berth (personal space needed)
        // High density (1.0) = small berth needed (can be close) = small bar
        // Low density (0.0) = large berth needed (need space) = large bar
        float invertedDensity = 1f - density;
        
        float minBarSize = 0.5f; // Minimum bar size when crowded (high density)
        float actualBarWidth = Mathf.Lerp(minBarSize, densityBarWidth, invertedDensity);

        // Color based on density (inverted from size)
        // High density (crowded) = red (small bar, can be close)
        // Low density (sparse) = green (large bar, need space)
        Color fillColor = Color.Lerp(crowdedDensityColor, sparseDensityColor, invertedDensity);
        Gizmos.color = fillColor;
        
        // Draw the bar completely filled at the scaled width
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(barCenter, Quaternion.identity, new Vector3(actualBarWidth, densityBarHeight, 0.1f));
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = oldMatrix;

        // Draw border around the actual bar size
        Gizmos.color = Color.white;
        DrawBarBorder(barCenter, actualBarWidth, densityBarHeight);
    }

    private void DrawArrowHead(Vector3 tip, Vector3 direction, float size)
    {
        Vector3 normalizedDir = direction.normalized;
        
        // Calculate arrow head base point
        Vector3 basePoint = tip - normalizedDir * size;
        
        // Calculate perpendicular vectors for arrow wings
        Vector3 perpendicular = Vector3.Cross(normalizedDir, Vector3.up).normalized * (size * 0.5f);
        
        // Draw arrow wings
        Gizmos.DrawLine(tip + offset, basePoint + perpendicular + offset);
        Gizmos.DrawLine(tip + offset, basePoint - perpendicular + offset);
        Gizmos.DrawLine(basePoint + perpendicular + offset, basePoint - perpendicular + offset);
    }
}
