using UnityEngine;
using System.Collections.Generic;

public class PathGizmoGenerator : MonoBehaviour
{
    [SerializeField] private Color gizmoColor = Color.magenta;
    [SerializeField] private float gizmoRadius = 0.4f;
    [SerializeField] private bool showOnlyWhenSelected = true;
    private List<Vector3> pathCorners;

    private Vector3 offset = new Vector3(0, 1f, 0);

    private void OnDrawGizmos()
    {
        if (showOnlyWhenSelected) return;
        DrawPathGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (!showOnlyWhenSelected) return;
        DrawPathGizmos();
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
}
