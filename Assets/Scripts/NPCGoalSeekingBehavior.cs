using UnityEngine;

/// <summary>
/// Behavior that steers the NPC toward the next path corner (goal).
/// This is typically the highest priority behavior - NPCs want to reach their destination.
/// </summary>
public class NPCGoalSeekingBehavior : IBoidBehavior
{
    public string BehaviorName => "Goal Seeking";

    private float baseWeight = 1.0f;
    private float arrivalSlowdownDistance = 3f; // Distance at which to start slowing down near destination

    /// <summary>
    /// Calculates the steering force toward the target corner.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Direction vector pointing toward the goal, with magnitude indicating urgency</returns>
    public Vector3 Calculate(NPCBehaviorContext context)
    {
        // Calculate distance to target corner
        float distanceToCorner = Vector3.Distance(context.targetCorner, context.position);

        // Direction toward the target corner
        Vector3 directionToCorner = context.targetCorner - context.position;
        directionToCorner.y = 0; // Keep movement on horizontal plane
        directionToCorner = directionToCorner.normalized;

        // Reduce strength when close to destination (arrival slowdown)
        // This creates smoother arrival behavior and prevents overshooting
        float strengthMultiplier = 1f;
        if (distanceToCorner < arrivalSlowdownDistance)
        {
            // Smoothly reduce from 1.0 to 0.7 as we get closer
            strengthMultiplier = Mathf.Lerp(0.7f, 1f, distanceToCorner / arrivalSlowdownDistance);
        }

        return directionToCorner * strengthMultiplier;
    }

    /// <summary>
    /// Returns the weight for this behavior.
    /// Goal seeking always has consistent high priority.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Weight multiplier for this behavior</returns>
    public float GetWeight(NPCBehaviorContext context)
    {
        // Goal seeking has constant high priority
        return baseWeight;
    }

    /// <summary>
    /// Sets the base weight for goal seeking.
    /// </summary>
    public void SetBaseWeight(float weight)
    {
        baseWeight = Mathf.Max(0.1f, weight);
    }

    /// <summary>
    /// Sets the distance at which arrival slowdown begins.
    /// </summary>
    public void SetArrivalSlowdownDistance(float distance)
    {
        arrivalSlowdownDistance = Mathf.Max(0.5f, distance);
    }
}
