using UnityEngine;

/// <summary>
/// Behavior that steers NPCs away from nearby characters (both NPCs and Players).
/// Uses spatial grid for efficient neighbor queries and dynamic personal space based on crowd density.
/// Only avoids characters that are in front of or beside the NPC (not behind).
/// Tracks the closest character for potential assertiveness comparisons.
/// </summary>
public class CharacterAvoidanceBehavior : IBoidBehavior
{
    public string BehaviorName => "Character Avoidance";

    [SerializeField] private float baseWeight = 1.0f;
    [SerializeField] private float avoidanceRadius = 6f; // How far to detect characters
    [SerializeField] private AnimationCurve influenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f); // Falloff from personal space to avoidance radius

    // Track the closest character (can be used for assertiveness comparison later)
    private BaseCharController closestCharacter = null;
    public BaseCharController ClosestCharacter => closestCharacter;

    /// <summary>
    /// Calculates an avoidance vector from the closest nearby character.
    /// Maximum influence occurs at the dynamic personal space radius (adjusted by density),
    /// falling off to zero at avoidanceRadius.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Avoidance vector away from the closest character</returns>
    public Vector3 Calculate(NPCBehaviorContext context)
    {
        closestCharacter = null;
        float closestInfluence = 0f;
        Vector3 closestAvoidanceVector = Vector3.zero;

        // Iterate through nearby characters from spatial grid
        foreach (var otherCharacter in context.nearbyCharacters)
        {
            if (otherCharacter == null) continue;

            Vector3 toOther = otherCharacter.transform.position - context.position;
            float distance = toOther.magnitude;

            // Only avoid characters within the avoidance radius
            if (distance < avoidanceRadius && distance > 0.1f)
            {
                // Check if character is in front/beside (not behind)
                // Flatten to XZ plane for 2D forward check
                Vector3 forwardFlat = context.forward;
                forwardFlat.y = 0;
                forwardFlat.Normalize();

                Vector3 toOtherFlat = toOther;
                toOtherFlat.y = 0;
                toOtherFlat.Normalize();

                float dotProduct = Vector3.Dot(forwardFlat, toOtherFlat);

                // Filter out characters that are behind us (dot < -0.3 means roughly behind)
                // This allows characters directly to the side (dot ~= 0) and in front (dot > 0)
                if (dotProduct < -0.3f)
                {
                    continue; // Skip characters that are behind us
                }

                // Calculate direction away from the other character
                Vector3 awayFromCharacter = -toOther.normalized;
                awayFromCharacter.y = 0; // Keep avoidance on horizontal plane

                float influence = 0f;

                // Use dynamic personal space radius from context (adjusted by density)
                if (distance <= context.currentPersonalSpaceRadius)
                {
                    // Max influence inside personal space
                    influence = 1f;
                }
                else
                {
                    // Influence falls off from personal space radius to avoidance radius
                    float normalizedDistance = (distance - context.currentPersonalSpaceRadius) / 
                                              (avoidanceRadius - context.currentPersonalSpaceRadius);
                    influence = influenceCurve.Evaluate(normalizedDistance);
                }

                // Track only the closest (highest influence) character
                if (influence > closestInfluence)
                {
                    closestInfluence = influence;
                    closestCharacter = otherCharacter;
                    closestAvoidanceVector = awayFromCharacter * influence;
                }
            }
        }

        return closestAvoidanceVector;
    }

    /// <summary>
    /// Returns the weight for this behavior.
    /// Weight increases in crowded areas to prioritize collision avoidance.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Weight multiplier for this behavior</returns>
    public float GetWeight(NPCBehaviorContext context)
    {
        // Base weight, can be boosted by density
        // In crowded areas (high density), character avoidance becomes more important
        float densityMultiplier = Mathf.Lerp(1f, 1.5f, context.localDensity);
        return baseWeight * densityMultiplier;
    }

    /// <summary>
    /// Sets the base weight for character avoidance.
    /// </summary>
    public void SetBaseWeight(float weight)
    {
        baseWeight = Mathf.Max(0.1f, weight);
    }

    /// <summary>
    /// Sets the radius at which character avoidance begins.
    /// </summary>
    public void SetAvoidanceRadius(float radius)
    {
        avoidanceRadius = Mathf.Max(1f, radius);
    }

    /// <summary>
    /// Sets the influence curve for character avoidance falloff.
    /// </summary>
    public void SetInfluenceCurve(AnimationCurve curve)
    {
        if (curve != null)
        {
            influenceCurve = curve;
        }
    }
}
