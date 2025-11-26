using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure RVO (Reciprocal Velocity Obstacles) collision avoidance system.
/// Implements the ORCA (Optimal Reciprocal Collision Avoidance) algorithm.
/// </summary>
public static class RVOSystem
{
    /// <summary>
    /// Data structure representing an RVO agent.
    /// </summary>
    public struct AgentData
    {
        public Vector3 position;
        public Vector3 velocity;
        public float radius;

        public AgentData(Vector3 position, Vector3 velocity, float radius)
        {
            this.position = position;
            this.velocity = velocity;
            this.radius = radius;
        }
    }

    /// <summary>
    /// Result of evaluating a single velocity candidate.
    /// </summary>
    public struct VelocityEvaluation
    {
        public float cost;
        public List<CollisionInfo> collisionDetails;
        public bool isSafe;

        public VelocityEvaluation(float cost, List<CollisionInfo> collisionDetails, bool isSafe)
        {
            this.cost = cost;
            this.collisionDetails = collisionDetails;
            this.isSafe = isSafe;
        }
    }

    /// <summary>
    /// Debug data structure containing RVO evaluation details for visualization.
    /// </summary>
    public struct RVODebugData
    {
        public List<Vector2> sampledVelocities;
        public List<VelocityEvaluation> velocityEvaluations;
        public Vector2 chosenVelocity;
        public int chosenVelocityIndex;

        public RVODebugData(int capacity)
        {
            sampledVelocities = new List<Vector2>(capacity);
            velocityEvaluations = new List<VelocityEvaluation>(capacity);
            chosenVelocity = Vector2.zero;
            chosenVelocityIndex = -1;
        }
    }

    /// <summary>
    /// Contains detailed collision information for a specific neighbor and velocity.
    /// </summary>
    public struct CollisionInfo
    {
        public AgentData neighbor;
        public float timeToClosest;
        public float closestDistance;
        public bool willCollide;
        public Vector2 myPositionAtClosest;
        public Vector2 neighborPositionAtClosest;

        public CollisionInfo(AgentData neighbor, float timeToClosest, float closestDistance, bool willCollide,
            Vector2 myPositionAtClosest, Vector2 neighborPositionAtClosest)
        {
            this.neighbor = neighbor;
            this.timeToClosest = timeToClosest;
            this.closestDistance = closestDistance;
            this.willCollide = willCollide;
            this.myPositionAtClosest = myPositionAtClosest;
            this.neighborPositionAtClosest = neighborPositionAtClosest;
        }
    }

    /// <summary>
    /// Computes a collision-free velocity for an agent using RVO.
    /// </summary>
    public static Vector2 ComputeAvoidanceVelocity(
        Vector2 currentPosition,
        Vector2 currentVelocity,
        Vector2 preferredVelocity,
        List<AgentData> neighbors,
        float radius,
        float timeHorizon,
        float responsibility)
    {
        RVODebugData dummyDebugData = default(RVODebugData);
        return ComputeAvoidanceVelocityInternal(currentPosition, currentVelocity, preferredVelocity,
            neighbors, radius, timeHorizon, responsibility, ref dummyDebugData, false);
    }

    /// <summary>
    /// Computes a collision-free velocity with debug data for visualization.
    /// </summary>
    public static Vector2 ComputeAvoidanceVelocityWithDebug(
        Vector2 currentPosition,
        Vector2 currentVelocity,
        Vector2 preferredVelocity,
        List<AgentData> neighbors,
        float radius,
        float timeHorizon,
        float responsibility,
        out RVODebugData debugData)
    {
        debugData = new RVODebugData(21); // 20 samples + 1 preferred
        return ComputeAvoidanceVelocityInternal(currentPosition, currentVelocity, preferredVelocity,
            neighbors, radius, timeHorizon, responsibility, ref debugData, true);
    }

    /// <summary>
    /// Internal implementation that handles both debug and non-debug cases.
    /// </summary>
    private static Vector2 ComputeAvoidanceVelocityInternal(
        Vector2 currentPosition,
        Vector2 currentVelocity,
        Vector2 preferredVelocity,
        List<AgentData> neighbors,
        float radius,
        float timeHorizon,
        float responsibility,
        ref RVODebugData debugData,
        bool isDebugging)
    {
        // Early exit for no neighbors
        if (neighbors.Count == 0)
        {
            if (isDebugging)
            {
                debugData.chosenVelocity = preferredVelocity;
            }
            return preferredVelocity;
        }

        // Generate velocity samples
        int numSamples = 20;
        float maxSpeed = preferredVelocity.magnitude;
        List<Vector2> samples = GenerateVelocitySamples(preferredVelocity, maxSpeed, numSamples);

        // Find best velocity (with or without debug tracking)
        Vector2 bestVelocity = FindBestVelocity(samples, currentPosition, currentVelocity, preferredVelocity,
            neighbors, radius, timeHorizon, responsibility, debugData);

        // Store chosen velocity if debugging
        if (isDebugging)
        {
            debugData.chosenVelocity = bestVelocity;
        }

        return bestVelocity;
    }

    /// <summary>
    /// Generates a list of velocity samples to test, including the preferred velocity.
    /// </summary>
    private static List<Vector2> GenerateVelocitySamples(Vector2 preferredVelocity, float maxSpeed, int numSamples)
    {
        List<Vector2> samples = new List<Vector2>(numSamples + 1);
        
        // Add preferred velocity first
        samples.Add(preferredVelocity);
        
        // Sample velocities in a circle
        for (int i = 0; i < numSamples; i++)
        {
            float angle = (float)i / numSamples * Mathf.PI * 2f;
            Vector2 sampleVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * maxSpeed;
            samples.Add(sampleVelocity);
        }

        return samples;
    }

    /// <summary>
    /// Finds the best velocity from a list of samples.
    /// </summary>
    /// <param name="samples">List of velocity samples to evaluate</param>
    /// <param name="currentPosition">Current position of the agent</param>
    /// <param name="currentVelocity">Current velocity of the agent</param>
    /// <param name="preferredVelocity">Preferred velocity (for fallback)</param>
    /// <param name="neighbors">List of nearby agents</param>
    /// <param name="radius">Agent radius</param>
    /// <param name="timeHorizon">Time horizon for collision prediction</param>
    /// <param name="responsibility">Responsibility factor</param>
    /// <param name="debugData">Optional debug data to populate</param>
    /// <returns>Best velocity found</returns>
    private static Vector2 FindBestVelocity(
        List<Vector2> samples,
        Vector2 currentPosition,
        Vector2 currentVelocity,
        Vector2 preferredVelocity,
        List<AgentData> neighbors,
        float radius,
        float timeHorizon,
        float responsibility,
        RVODebugData debugData)
    {
        Vector2 bestVelocity = preferredVelocity;
        float bestCost = float.MaxValue;

        for (int i = 0; i < samples.Count; i++)
        {
            Vector2 sample = samples[i];
            VelocityEvaluation evaluation = EvaluateVelocity(sample, currentPosition, currentVelocity,
                neighbors, radius, timeHorizon, responsibility);

            // Track debug data if provided
            if (debugData.sampledVelocities != null)
            {
                debugData.sampledVelocities.Add(sample);
                debugData.velocityEvaluations.Add(evaluation);
            }

            if (evaluation.cost < bestCost)
            {
                bestCost = evaluation.cost;
                bestVelocity = sample;
                
                if (debugData.sampledVelocities != null)
                {
                    debugData.chosenVelocityIndex = i;
                }
            }
        }

        // If no safe velocity found, reduce speed
        if (bestCost >= float.MaxValue)
        {
            bestVelocity = currentVelocity * 0.5f;
        }

        return bestVelocity;
    }

    /// <summary>
    /// Evaluates the cost of a candidate velocity.
    /// Returns float.MaxValue if velocity leads to collision, otherwise returns
    /// a cost based on deviation from preferred velocity.
    /// </summary>
    private static VelocityEvaluation EvaluateVelocity(
        Vector2 candidateVelocity,
        Vector2 currentPosition,
        Vector2 currentVelocity,
        List<AgentData> neighbors,
        float radius,
        float timeHorizon,
        float responsibility)
    {
        List<CollisionInfo> collisionDetails = new List<CollisionInfo>();

        // Check for collisions with each neighbor
        foreach (var neighbor in neighbors)
        {
            Vector2 neighborPos = new Vector2(neighbor.position.x, neighbor.position.z);
            Vector2 neighborVel = new Vector2(neighbor.velocity.x, neighbor.velocity.z);
            
            // Calculate relative position and velocity
            Vector2 relativePosition = neighborPos - currentPosition;
            float dist = relativePosition.magnitude;
            
            if (dist < 0.01f) continue; // Too close to evaluate
            
            // Calculate relative velocity
            Vector2 relativeVelocity = candidateVelocity - neighborVel;
            
            // Check if velocity leads to collision within time horizon
            float combinedRadius = radius + neighbor.radius;
            
            // Time to closest approach
            float timeToClosest = -Vector2.Dot(relativePosition, relativeVelocity) / relativeVelocity.sqrMagnitude;
            
            if (timeToClosest < 0 || timeToClosest > timeHorizon)
            {
                continue; // Not on collision course or too far in future
            }
            
            // Position at closest approach
            Vector2 closestPoint = relativePosition + relativeVelocity * timeToClosest;
            float closestDist = closestPoint.magnitude;
            
            // Store collision info for later inspection
            bool willCollide = closestDist < combinedRadius;
            collisionDetails.Add(new CollisionInfo(neighbor, timeToClosest, closestDist, willCollide,
                currentPosition + candidateVelocity * timeToClosest,
                neighborPos + neighborVel * timeToClosest));
            
            // Check if collision occurs
            if (willCollide)
            {
                return new VelocityEvaluation(float.MaxValue, collisionDetails, false); // Collision detected
            }
        }
        
        // No collision - return cost based on deviation from preferred velocity
        return new VelocityEvaluation((candidateVelocity - currentVelocity).sqrMagnitude, collisionDetails, true);
    }
}
