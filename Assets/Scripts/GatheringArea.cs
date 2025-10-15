using UnityEngine;

[ExecuteAlways]
public class GatheringArea : MonoBehaviour
{
    public Vector3 size = new Vector3(5, 1, 5);
    public Color gizmoColor = new Color(1f, 0f, 0f, 0.15f); // very transparent red

    public Vector3 GetRandomPointInArea()
    {
        Vector3 halfSize = size * 0.5f;
        float x = Random.Range(-halfSize.x, halfSize.x);
        float y = 0f;
        float z = Random.Range(-halfSize.z, halfSize.z);
        return transform.position + new Vector3(x, y, z);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(transform.position, size);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, size);
    }
}