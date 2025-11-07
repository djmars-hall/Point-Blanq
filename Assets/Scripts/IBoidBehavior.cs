using UnityEngine;

/// <summary>
/// Interface for all boid-style steering behaviors.
/// Each behavior calculates a directional vector and weight based on the current context.
/// </summary>
public interface IBoidBehavior
{
    /// <summary>
    /// Calculates the steering force for this behavior.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Directional vector representing the desired movement (not normalized, magnitude indicates strength)</returns>
    Vector3 Calculate(NPCBehaviorContext context);

    /// <summary>
    /// Calculates the dynamic weight for this behavior based on current context.
    /// Higher weights give this behavior more influence in the final movement decision.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Weight multiplier (typically 0.0 to 2.0, but can be higher for critical behaviors)</returns>
    float GetWeight(NPCBehaviorContext context);

    /// <summary>
    /// Readable name for this behavior (used for debugging).
    /// </summary>
    string BehaviorName { get; }
}
