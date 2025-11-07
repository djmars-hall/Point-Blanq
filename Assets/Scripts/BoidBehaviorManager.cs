using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages and coordinates all boid behaviors for an NPC.
/// Calculates the final movement vector by combining all active behaviors with their weights.
/// </summary>
public class BoidBehaviorManager
{
    private List<IBoidBehavior> behaviors = new List<IBoidBehavior>();

    /// <summary>
    /// Registers a behavior with the manager.
    /// </summary>
    /// <param name="behavior">The behavior to register</param>
    public void RegisterBehavior(IBoidBehavior behavior)
    {
        if (behavior == null)
        {
            Debug.LogError("BoidBehaviorManager: Attempted to register null behavior");
            return;
        }

        if (behaviors.Contains(behavior))
        {
            Debug.LogWarning($"BoidBehaviorManager: Behavior '{behavior.BehaviorName}' already registered");
            return;
        }

        behaviors.Add(behavior);
        Debug.Log($"BoidBehaviorManager: Registered behavior '{behavior.BehaviorName}'");
    }

    /// <summary>
    /// Removes a behavior from the manager.
    /// </summary>
    /// <param name="behavior">The behavior to remove</param>
    public void UnregisterBehavior(IBoidBehavior behavior)
    {
        if (behaviors.Remove(behavior))
        {
            Debug.Log($"BoidBehaviorManager: Unregistered behavior '{behavior.BehaviorName}'");
        }
    }

    /// <summary>
    /// Calculates the final movement vector by combining all registered behaviors.
    /// Uses weighted averaging to blend behavior influences.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Final desired movement vector (not normalized, magnitude represents urgency)</returns>
    public Vector3 CalculateFinalMovement(NPCBehaviorContext context)
    {
        if (behaviors.Count == 0)
        {
            Debug.LogWarning("BoidBehaviorManager: No behaviors registered, returning zero movement");
            return Vector3.zero;
        }

        Vector3 weightedSum = Vector3.zero;
        float totalWeight = 0f;

        foreach (var behavior in behaviors)
        {
            if (behavior == null) continue;

            // Calculate behavior output and weight
            Vector3 behaviorVector = behavior.Calculate(context);
            float weight = behavior.GetWeight(context);

            // Skip behaviors with zero or negative weight
            if (weight <= 0.001f) continue;

            // Accumulate weighted sum
            weightedSum += behaviorVector * weight;
            totalWeight += weight;

            // Debug visualization (optional - can be enabled via a flag later)
            // Debug.DrawRay(context.position, behaviorVector, GetDebugColor(behavior.BehaviorName), 0.1f);
        }

        // Normalize by total weight to prevent speed inflation
        if (totalWeight > 0.001f)
        {
            return weightedSum / totalWeight;
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Gets the number of registered behaviors.
    /// </summary>
    public int BehaviorCount => behaviors.Count;

    /// <summary>
    /// Clears all registered behaviors.
    /// </summary>
    public void ClearBehaviors()
    {
        behaviors.Clear();
        Debug.Log("BoidBehaviorManager: Cleared all behaviors");
    }
}
