using UnityEngine;

/// <summary>
/// Behavior that keeps NPCs aligned with their navigation path corridor.
/// Applies corrective steering when the NPC drifts too far from the ideal path line
/// between waypoints, helping maintain smooth path following even with other behaviors active.
/// </summary>
public class PathAdherenceBehavior : IBoidBehavior
{
    public string BehaviorName => "Path Adherence";

    private float baseWeight = 0.3f; // Lower weight - this is a subtle corrective behavior
    private float corridorWidth = 2.5f; // How far from the path line before correction applies
    private AnimationCurve influenceCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); // Increases as we drift further

    /// <summary>
    /// Calculates a steering force to keep the NPC within the path corridor.
    /// Returns zero if the NPC is within acceptable bounds of the path.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Steering vector toward the ideal path line</returns>
    public Vector3 Calculate(NPCBehaviorContext context)
    {
        // Need at least a current target to calculate path adherence
        if (context.remainingPath == null || context.remainingPath.Count == 0)
        {
            return Vector3.zero;
        }

        // Calculate the path line segment (from previous corner to target corner)
        Vector3 pathStart = context.previousCorner;
        Vector3 pathEnd = context.targetCorner;
        
        // Flatten to XZ plane for 2D path analysis
        pathStart.y = 0;
        pathEnd.y = 0;
        Vector3 npcPosition = context.position;
        npcPosition.y = 0;

        // Calculate path direction
        Vector3 pathDirection = (pathEnd - pathStart).normalized;
        
        // If path segment is too short, skip adherence
        if ((pathEnd - pathStart).magnitude < 0.5f)
        {
            return Vector3.zero;
        }

        // Project NPC position onto the path line to find the closest point
        Vector3 pathStartToNpc = npcPosition - pathStart;
        float projectionLength = Vector3.Dot(pathStartToNpc, pathDirection);
        
        // Clamp projection to the path segment bounds
        float pathLength = (pathEnd - pathStart).magnitude;
        projectionLength = Mathf.Clamp(projectionLength, 0, pathLength);
        
        // Calculate the closest point on the path line
        Vector3 closestPointOnPath = pathStart + pathDirection * projectionLength;
        
        // Calculate distance from path line (perpendicular distance)
        float distanceFromPath = Vector3.Distance(npcPosition, closestPointOnPath);
        
        // Only apply correction if outside the corridor width
        if (distanceFromPath < corridorWidth * 0.5f)
        {
            return Vector3.zero; // Within acceptable bounds
        }
        
        // Calculate direction back toward the path line
        Vector3 directionToPath = (closestPointOnPath - npcPosition).normalized;
        directionToPath.y = 0; // Keep on horizontal plane
        
        // Calculate influence based on how far we've drifted
        // Normalize distance: 0 at half corridor width, 1 at full corridor width
        float normalizedDistance = Mathf.Clamp01((distanceFromPath - corridorWidth * 0.5f) / (corridorWidth * 0.5f));
        float influence = influenceCurve.Evaluate(normalizedDistance);
        
        return directionToPath * influence;
    }

    /// <summary>
    /// Returns the weight for this behavior.
    /// Weight increases slightly in corridors where path adherence is more critical.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Weight multiplier for this behavior</returns>
    public float GetWeight(NPCBehaviorContext context)
    {
        // Slightly higher weight in corridors where staying on path is more important
        if (context.isInCorridor)
        {
            return baseWeight * 1.5f;
        }
        
        return baseWeight;
    }

    /// <summary>
    /// Sets the base weight for path adherence.
    /// </summary>
    public void SetBaseWeight(float weight)
    {
        baseWeight = Mathf.Max(0.05f, weight);
    }

    /// <summary>
    /// Sets the corridor width - NPCs will be guided back toward the path
    /// when they drift beyond half this width.
    /// </summary>
    public void SetCorridorWidth(float width)
    {
        corridorWidth = Mathf.Max(1f, width);
    }

    /// <summary>
    /// Sets the influence curve for path adherence strength as NPCs drift further.
    /// </summary>
    public void SetInfluenceCurve(AnimationCurve curve)
    {
        if (curve != null)
        {
            influenceCurve = curve;
        }
    }
}
