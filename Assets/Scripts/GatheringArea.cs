using UnityEngine;

[ExecuteAlways]
public class GatheringArea : MonoBehaviour
{
    public Vector3 size = new Vector3(5, 1, 5);
    public Color gizmoColor = Color.red;

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(transform.position, size);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, size);
    }
}