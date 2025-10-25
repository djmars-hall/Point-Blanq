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
    private List<Vector3> pathCorners;

    private Vector3 offset = new Vector3(0, 1f, 0);

    [Header("Heuristic Gizmo Settings")]
    [SerializeField] private bool showHeuristics = true;
    [SerializeField] private Color cornerHeuristicColor = Color.green;
    [SerializeField] private Color edgeHeuristicColor = new Color(1f, 0f, 1f); // Magenta
    [SerializeField] private Color desiredDirectionColor = Color.cyan;
    [SerializeField] private float heuristicLineScale = 2f;
    [SerializeField] private float desiredDirectionScale = 3f;
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

        // Draw spheres at each corner and lines between them
        for (int i = 0; i < pathCorners.Count; i++)
        {
            Gizmos.DrawSphere(pathCorners[i] + offset, gizmoRadius);
            if (i < pathCorners.Count - 1)
            {
                Gizmos.DrawLine(pathCorners[i] + offset, pathCorners[i + 1] + offset);
            }
        }

        // Draw first line and sphere from NPC to first corner
        Gizmos.DrawLine(transform.position + offset, pathCorners[0] + offset);
        Gizmos.DrawSphere(transform.position + offset, gizmoRadius);
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
            Gizmos.DrawLine(npcPosition, endPoint);
            DrawArrowHead(endPoint, cornerHeuristic, smallArrowSize);
        }

        // Draw Edge Heuristic (NavMesh edge avoidance)
        Vector3 edgeHeuristic = npcController.EdgeHeuristic;
        if (edgeHeuristic.magnitude > 0.01f)
        {
            Gizmos.color = edgeHeuristicColor;
            Vector3 endPoint = npcPosition + edgeHeuristic * heuristicLineScale;
            Gizmos.DrawLine(npcPosition, endPoint);
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
                    Gizmos.DrawLine(npcPosition, endPoint);
                    DrawArrowHead(endPoint, characterHeuristic, smallArrowSize);
                }
            }
        }

        // Draw Desired Direction (final combined heuristic) - Most prominent
        Vector3 desiredDirection = npcController.DesiredDirection;
        if (desiredDirection.magnitude > 0.01f)
        {
            Gizmos.color = desiredDirectionColor;
            Vector3 endPoint = npcPosition + desiredDirection * desiredDirectionScale;

            Gizmos.DrawLine(npcPosition, endPoint);
            DrawArrowHead(endPoint, desiredDirection, 0.3f); // Large arrowhead for desired direction
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
        Gizmos.DrawLine(tip, basePoint + perpendicular);
        Gizmos.DrawLine(tip, basePoint - perpendicular);
        Gizmos.DrawLine(basePoint + perpendicular, basePoint - perpendicular);
    }
}
