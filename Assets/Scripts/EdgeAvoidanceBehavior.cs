using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Behavior that steers NPCs away from NavMesh edges (walls, obstacles, boundaries).
/// Uses NavMesh raycasting to detect nearby edges and applies avoidance forces.
/// Includes corridor detection to prevent oscillation between parallel walls.
/// </summary>
public class EdgeAvoidanceBehavior : IBoidBehavior
{
    public string BehaviorName => "Edge Avoidance";

    private float baseWeight = 1.0f;
    private float avoidanceRadius = 3f; // How far to check for edges
    private float edgeBuffer = 1.2f; // Minimum desired distance from edges
    private AnimationCurve influenceCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f); // Falloff curve

    // Track whether the NPC is in a corridor (between two opposing edges)
    private bool isInCorridor = false;
    public bool IsInCorridor => isInCorridor;

    /// <summary>
    /// Calculates an avoidance vector away from the closest NavMesh edge.
    /// Returns zero vector if no edge is nearby or if caught between opposing edges (corridor).
    /// Updates the isInCorridor flag during calculation.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Edge avoidance vector pointing away from nearest edge</returns>
    public Vector3 Calculate(NPCBehaviorContext context)
    {
        NavMeshHit edgeHit;

        // Find the closest edge within the avoidance radius
        if (NavMesh.FindClosestEdge(context.position, out edgeHit, NavMesh.AllAreas))
        {
            float distanceToEdge = edgeHit.distance;

            // Only apply avoidance if within the edge avoidance radius
            if (distanceToEdge < avoidanceRadius && distanceToEdge > 0.1f)
            {
                // Direction away from the edge (using the edge normal)
                Vector3 awayFromEdge = edgeHit.normal;
                awayFromEdge.y = 0; // Keep avoidance on horizontal plane
                awayFromEdge = awayFromEdge.normalized;

                // Calculate influence using the custom curve
                // Normalize distance to 0-1 range (0 = at edge, 1 = at edgeAvoidanceRadius)
                float normalizedDistance = distanceToEdge / avoidanceRadius;

                // Evaluate the curve (curve goes from 1 at x=0 to 0 at x=1)
                float influence = influenceCurve.Evaluate(normalizedDistance);

                Vector3 edgeAvoidanceVector = awayFromEdge * influence;

                // Check if there's an opposing edge that would contradict this heuristic
                // Only nullify if the heuristic is weak AND we're truly trapped (in a corridor)
                if (edgeAvoidanceVector.magnitude < 0.2f && HasOpposingEdge(context.position, awayFromEdge, distanceToEdge))
                {
                    // Caught between two edges with weak influence, nullify to avoid oscillation
                    return Vector3.zero;
                }

                // Update corridor state
                isInCorridor = CheckCorridorState(context.position, awayFromEdge);

                return edgeAvoidanceVector;
            }
        }

        // If no edge is close, we are not in a corridor
        isInCorridor = false;
        return Vector3.zero;
    }

    /// <summary>
    /// Returns the weight for this behavior.
    /// Weight increases significantly in corridors to prioritize safe navigation.
    /// </summary>
    /// <param name="context">Current NPC state and environmental data</param>
    /// <returns>Weight multiplier for this behavior</returns>
    public float GetWeight(NPCBehaviorContext context)
    {
        // In corridors, edge avoidance is critical - boost weight significantly
        // This ensures NPCs don't walk into walls when navigating tight spaces
        if (context.isInCorridor)
        {
            return baseWeight * 2.0f; // Double weight in corridors
        }

        return baseWeight;
    }

    /// <summary>
    /// Checks if there is an opposing edge in the avoidance direction.
    /// This prevents the NPC from getting stuck oscillating between two close edges.
    /// Uses stricter thresholds to only detect true opposing edge situations (corridors).
    /// </summary>
    /// <param name="position">Current position to check from</param>
    /// <param name="avoidanceDirection">The direction we want to move away from the closest edge</param>
    /// <param name="closestEdgeDistance">Distance to the closest edge</param>
    /// <returns>True if there's an opposing edge that would contradict the avoidance direction</returns>
    private bool HasOpposingEdge(Vector3 position, Vector3 avoidanceDirection, float closestEdgeDistance)
    {
        // Sample a point in the avoidance direction to check for an opposing edge
        // Use a tighter check radius (half of edgeBuffer)
        Vector3 checkPosition = position + avoidanceDirection * (edgeBuffer * 0.5f);

        NavMeshHit opposingEdgeHit;
        if (NavMesh.FindClosestEdge(checkPosition, out opposingEdgeHit, NavMesh.AllAreas))
        {
            float opposingDistance = opposingEdgeHit.distance;

            // Only consider it an opposing edge if it's within a little more than edgeBuffer
            if (opposingDistance < edgeBuffer * 1.5f)
            {
                // Check if this edge's normal points back toward us (opposing the avoidance direction)
                Vector3 opposingNormal = opposingEdgeHit.normal;
                opposingNormal.y = 0;
                opposingNormal = opposingNormal.normalized;

                // If the dot product is negative, the normals point in opposite directions
                // Use stricter threshold (-0.7) to ensure they're truly opposing
                float alignment = Vector3.Dot(avoidanceDirection, opposingNormal);

                if (alignment < -0.7f) // Stricter threshold to detect truly opposing edges
                {
                    return true; // In a corridor
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks the current corridor state based on position and edge proximity.
    /// </summary>
    /// <param name="position">Current position of the NPC</param>
    /// <param name="avoidanceDirection">Current avoidance direction</param>
    /// <returns>True if the NPC is in a corridor, false otherwise</returns>
    private bool CheckCorridorState(Vector3 position, Vector3 avoidanceDirection)
    {
        // Sample slightly ahead in the avoidance direction to determine corridor state
        Vector3 samplePosition = position + avoidanceDirection * (edgeBuffer * 0.5f);

        NavMeshHit edgeHit;
        if (NavMesh.FindClosestEdge(samplePosition, out edgeHit, NavMesh.AllAreas))
        {
            // If there's an edge close by, we might be in a corridor
            float distanceToEdge = edgeHit.distance;

            // Check for opposing edge as well
            Vector3 oppositeDirection = -avoidanceDirection;
            Vector3 opposingSamplePosition = position + oppositeDirection * (edgeBuffer * 0.5f);
            NavMeshHit opposingEdgeHit;
            bool hasOpposingEdge = NavMesh.FindClosestEdge(opposingSamplePosition, out opposingEdgeHit, NavMesh.AllAreas);

            // If we have an opposing edge nearby, and we're close to an edge in general, we're likely in a corridor
            if (hasOpposingEdge && distanceToEdge < avoidanceRadius && opposingEdgeHit.distance < avoidanceRadius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sets the base weight for edge avoidance.
    /// </summary>
    public void SetBaseWeight(float weight)
    {
        baseWeight = Mathf.Max(0.1f, weight);
    }

    /// <summary>
    /// Sets the radius at which edge avoidance begins.
    /// </summary>
    public void SetAvoidanceRadius(float radius)
    {
        avoidanceRadius = Mathf.Max(0.5f, radius);
    }

    /// <summary>
    /// Sets the minimum desired distance from edges.
    /// </summary>
    public void SetEdgeBuffer(float buffer)
    {
        edgeBuffer = Mathf.Max(0.3f, buffer);
    }

    /// <summary>
    /// Sets the influence curve for edge avoidance falloff.
    /// </summary>
    public void SetInfluenceCurve(AnimationCurve curve)
    {
        if (curve != null)
        {
            influenceCurve = curve;
        }
    }
}
