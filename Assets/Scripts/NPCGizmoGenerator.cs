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
    [SerializeField] private Color secondCharacterColor = new Color(1f, 0.5f, 0f); // Orange
    [SerializeField] private Color thirdCharacterColor = Color.yellow;


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
    }

    private void OnDrawGizmosSelected()
    {
        if (!showONLYWhenSelected) return;
        DrawPathGizmos();
        DrawHeuristicGizmos();
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
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        
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

        // Draw Individual Character Heuristics
        List<Vector3> characterHeuristics = npcController.CharacterHeuristics;
        if (characterHeuristics != null && characterHeuristics.Count > 0)
        {
            for (int i = 0; i < characterHeuristics.Count; i++)
            {
                Vector3 characterHeuristic = characterHeuristics[i];
                
                // Assign color based on priority (closest = red, second = orange, third = yellow)
                Color heuristicColor = i switch
                {
                    0 => closestCharacterColor,
                    1 => secondCharacterColor,
                    2 => thirdCharacterColor,
                    _ => Color.white
                };

                Gizmos.color = heuristicColor;

                // Draw the avoidance vector
                if (characterHeuristic.magnitude > 0.01f)
                {
                    Vector3 endPoint = npcPosition + characterHeuristic * heuristicLineScale;
                    Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
                    DrawArrowHead(endPoint, characterHeuristic, smallArrowSize);
                }
            }
        }

        // Draw Desired Direction (final combined heuristic) - Most prominent
        Vector3 desiredDirection = npcController.DesiredDirection;
        if (desiredDirection.magnitude > 0.01f)
        {
            Gizmos.color = desiredDirectionColor;
            Vector3 endPoint = npcPosition + desiredDirection * heuristicLineScale;

            Gizmos.DrawLine(npcPosition + offset, endPoint + offset);
            DrawArrowHead(endPoint, desiredDirection, 0.5f);
        }
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
