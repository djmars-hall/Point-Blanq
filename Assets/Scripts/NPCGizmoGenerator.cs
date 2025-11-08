using UnityEngine;
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

    private NPCController npcController;

    private void Start()
    {
        npcController = GetComponent<NPCController>();
    }

    private void OnDrawGizmos()
    {
        if(onlyShowWhenSelected) return;
        DrawPathGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        DrawPathGizmos();
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


    // Draw a semicircle area around myself of the zone where I pay attention to other characters

    // Draw a line from all other characters to myself when they are within my attention zone

    // Draw a line with an arrowhead at the end from each of the characters within my attention zone to the forward direction they are facing

    private void DrawArrowHead(Vector3 tip, Vector3 direction, float size)
    {
        Vector3 normalizedDir = direction.normalized;
        Vector3 basePoint = tip - normalizedDir * size;
        Vector3 perpendicular = Vector3.Cross(normalizedDir, Vector3.up).normalized * (size * 0.5f);
        Gizmos.DrawLine(tip + offset, basePoint + perpendicular + offset);
        Gizmos.DrawLine(tip + offset, basePoint - perpendicular + offset);
        Gizmos.DrawLine(basePoint + perpendicular + offset, basePoint - perpendicular + offset);
    }
}
