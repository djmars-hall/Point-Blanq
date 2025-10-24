using UnityEngine;
using System.Collections.Generic;

public class NPCGizmoGenerator : MonoBehaviour
{
    [Header("General Settings")]
    [SerializeField] private bool showONLYWhenSelected = true;

    [Header("Path Gizmo Settings")]
    [SerializeField] private Color gizmoColor = Color.magenta;
    [SerializeField] private float gizmoRadius = 0.4f;
    private List<Vector3> pathCorners;

    private Vector3 offset = new Vector3(0, 1f, 0);

    [Header("Heuristic Gizmo Settings")]
    [SerializeField] private bool showHeuristics = true;
    [SerializeField] private Color cornerHeuristicColor = Color.green;
    [SerializeField] private float heuristicLineScale = 2f;
    
    [Header("Character Heuristic Colors")]
    [SerializeField] private Color closestCharacterColor = Color.red;
    [SerializeField] private Color secondCharacterColor = new Color(1f, 0.5f, 0f); // Orange
    [SerializeField] private Color thirdCharacterColor = Color.yellow;

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
        if (GetComponent<NPCController>() == null) return;

        pathCorners = GetComponent<NPCController>().PathCorners;

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

        NPCController npcController = GetComponent<NPCController>();
        if (npcController == null) return;

        Vector3 npcPosition = transform.position + offset;

        // Draw Corner Heuristic (direction to waypoint)
        Vector3 cornerHeuristic = npcController.CornerHeuristic;
        if (cornerHeuristic.magnitude > 0.01f)
        {
            Gizmos.color = cornerHeuristicColor;
            Vector3 endPoint = npcPosition + cornerHeuristic * heuristicLineScale;
            Gizmos.DrawLine(npcPosition, endPoint);
            // Draw a small sphere at the end to indicate direction
            Gizmos.DrawSphere(endPoint, 0.1f);
        }

        // Draw Individual Character Heuristics
        List<NPCController.CharacterInfluence> characterHeuristics = npcController.CharacterHeuristics;
        if (characterHeuristics != null && characterHeuristics.Count > 0)
        {
            for (int i = 0; i < characterHeuristics.Count; i++)
            {
                var influence = characterHeuristics[i];
                
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
                if (influence.avoidanceVector.magnitude > 0.01f)
                {
                    Vector3 endPoint = npcPosition + influence.avoidanceVector * heuristicLineScale;
                    Gizmos.DrawLine(npcPosition, endPoint);
                    
                    // Draw a sphere at the end to indicate direction and priority
                    float sphereSize = 0.15f - (i * 0.03f); // Larger for higher priority
                    Gizmos.DrawSphere(endPoint, sphereSize);

                    // Draw a line to the character being avoided (for debugging)
                    if (influence.character != null)
                    {
                        Gizmos.color = new Color(heuristicColor.r, heuristicColor.g, heuristicColor.b, 0.3f);
                        Gizmos.DrawLine(npcPosition, influence.character.transform.position + offset);
                    }
                }
            }
        }
    }
}
