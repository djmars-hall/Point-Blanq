using UnityEngine;

/// <summary>
/// A configurable rectangular zone that can be used for spawning NPCs, gathering points, or other purposes.
/// </summary>
[ExecuteAlways]
public class MapZone : MonoBehaviour
{
    public enum ZoneType
    {
        Spawning,
        Gathering,
        Both
    }

    // Predefined color constants for each zone type
    private readonly Color spawningGizmoColor = new Color(1f, 0.5f, 0f, 0.5f); // More visible orange
    private readonly Color spawningWireColor = new Color(1f, 0f, 1f, 0.8f); // More visible magenta
    
    private readonly Color gatheringGizmoColor = new Color(1f, 0f, 0f, 0.3f); // Transparent red
    private readonly Color gatheringWireColor = new Color(1f, 1f, 0f, 0.5f); // Transparent yellow
    
    private readonly Color bothGizmoColor = new Color(0f, 1f, 0f, 0.3f); // Transparent green
    private readonly Color bothWireColor = new Color(0f, 1f, 1f, 0.5f); // Transparent cyan

    [Header("Zone Settings")]
    [Tooltip("The type of zone - determines its purpose and visual appearance")]
    public ZoneType zoneType = ZoneType.Gathering;
    
    [Tooltip("The size of the rectangular zone")]
    public Vector3 size = new Vector3(5, 1, 5);

    [Header("Visual Settings (Auto-updated based on Zone Type)")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0f, 0f, 0.3f);
    [SerializeField] private Color wireColor = new Color(1f, 1f, 0f, 0.5f);

    private ZoneType lastZoneType;

    private void OnEnable()
    {
        // Force color update when enabled
        lastZoneType = (ZoneType)(-1); // Set to invalid value to force update
        UpdateColors();
    }

    /// <summary>
    /// Gets a random point within this zone's bounds.
    /// </summary>
    /// <returns>A random world position within the zone</returns>
    public Vector3 GetRandomPointInArea()
    {
        Vector3 halfSize = size * 0.5f;
        float x = Random.Range(-halfSize.x, halfSize.x);
        float y = 0f;
        float z = Random.Range(-halfSize.z, halfSize.z);
        return transform.position + new Vector3(x, y, z);
    }

    /// <summary>
    /// Returns true if this zone is configured for spawning.
    /// </summary>
    public bool IsSpawningZone()
    {
        return zoneType == ZoneType.Spawning || zoneType == ZoneType.Both;
    }

    /// <summary>
    /// Returns true if this zone is configured for gathering.
    /// </summary>
    public bool IsGatheringZone()
    {
        return zoneType == ZoneType.Gathering || zoneType == ZoneType.Both;
    }

    private void OnDrawGizmos()
    {
        // Update colors if zone type changed
        if (lastZoneType != zoneType)
        {
            UpdateColors();
        }

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(transform.position, size);
        Gizmos.color = wireColor;
        Gizmos.DrawWireCube(transform.position, size);
    }

    private void OnValidate()
    {
        // Update colors when values change in inspector
        UpdateColors();
    }

    /// <summary>
    /// Updates the gizmo and wire colors based on the current zone type.
    /// </summary>
    private void UpdateColors()
    {
        switch (zoneType)
        {
            case ZoneType.Spawning:
                gizmoColor = spawningGizmoColor;
                wireColor = spawningWireColor;
                break;
            case ZoneType.Gathering:
                gizmoColor = gatheringGizmoColor;
                wireColor = gatheringWireColor;
                break;
            case ZoneType.Both:
                gizmoColor = bothGizmoColor;
                wireColor = bothWireColor;
                break;
        }
        
        lastZoneType = zoneType;
    }
}
