using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Validates and constrains NPC movement to stay within the NavMesh boundaries.
/// This is a HARD CONSTRAINT that runs after all behavior calculations to prevent wall-walking.
/// </summary>
public class NavMeshConstraintValidator
{
    [SerializeField] private float samplingDistance = 0.5f; // How far to check for valid NavMesh positions
    [SerializeField] private int maxRotationAttempts = 7; // How many rotation angles to try when finding valid movement

    /// <summary>
    /// Validates that the desired movement will keep the NPC on the NavMesh.
    /// If the movement would go off the NavMesh, finds an alternative valid direction.
    /// This is a HARD CONSTRAINT - always runs and always returns a valid movement vector.
    /// </summary>
    /// <param name="currentPosition">Current position of the NPC</param>
    /// <param name="desiredMovement">Desired movement direction and magnitude from behaviors</param>
    /// <param name="deltaTime">Time step for this frame</param>
    /// <returns>Validated movement vector that guarantees staying on NavMesh</returns>
    public Vector3 ValidateMovement(Vector3 currentPosition, Vector3 desiredMovement, float deltaTime)
    {
        // Early exit if no movement desired
        if (desiredMovement.magnitude < 0.001f)
        {
            return Vector3.zero;
        }

        // Calculate the desired next position
        Vector3 desiredNextPosition = currentPosition + desiredMovement * deltaTime;
        desiredNextPosition.y = currentPosition.y; // Keep on same Y level

        // Try to find the closest valid point on NavMesh
        NavMeshHit hit;
        if (NavMesh.SamplePosition(desiredNextPosition, out hit, samplingDistance, NavMesh.AllAreas))
        {
            // Desired position is on or near NavMesh
            // Calculate movement to the valid NavMesh position
            Vector3 validMovement = (hit.position - currentPosition) / deltaTime;
            validMovement.y = 0; // Keep movement horizontal
            
            // Verify we're not moving too far from desired direction
            float alignmentWithDesired = Vector3.Dot(validMovement.normalized, desiredMovement.normalized);
            
            // If the NavMesh position is roughly in the desired direction, use it
            if (alignmentWithDesired > 0.3f || desiredMovement.magnitude < 0.1f)
            {
                return validMovement;
            }
        }

        // Desired position is off NavMesh or too far from desired direction
        // Try to find an alternative direction that stays on NavMesh
        return FindAlternativeMovement(currentPosition, desiredMovement, deltaTime);
    }

    /// <summary>
    /// Attempts to find a valid movement direction when the desired movement would go off the NavMesh.
    /// Tries rotating the desired direction in increments until a valid direction is found.
    /// </summary>
    /// <param name="currentPosition">Current position of the NPC</param>
    /// <param name="desiredMovement">Original desired movement from behaviors</param>
    /// <param name="deltaTime">Time step for this frame</param>
    /// <returns>Valid movement vector, or Vector3.zero if no valid direction found</returns>
    private Vector3 FindAlternativeMovement(Vector3 currentPosition, Vector3 desiredMovement, float deltaTime)
    {
        // Try rotating the desired movement in small increments to find a valid direction
        // This creates a "sliding" effect along walls rather than stopping completely
        float[] rotationAngles = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f };

        float originalMagnitude = desiredMovement.magnitude;

        for (int i = 0; i < rotationAngles.Length && i < maxRotationAttempts; i++)
        {
            // Rotate the desired movement around the Y axis
            Quaternion rotation = Quaternion.Euler(0, rotationAngles[i], 0);
            Vector3 rotatedMovement = rotation * desiredMovement;

            // Calculate test position
            Vector3 testPosition = currentPosition + rotatedMovement * deltaTime;
            testPosition.y = currentPosition.y;

            // Check if this direction is valid
            NavMeshHit hit;
            if (NavMesh.SamplePosition(testPosition, out hit, samplingDistance, NavMesh.AllAreas))
            {
                // Additionally check if we can raycast to this position without hitting an edge
                NavMeshHit rayHit;
                bool wouldHitEdge = NavMesh.Raycast(currentPosition, hit.position, out rayHit, NavMesh.AllAreas);

                if (!wouldHitEdge)
                {
                    // Found a valid direction!
                    Vector3 validMovement = (hit.position - currentPosition) / deltaTime;
                    validMovement.y = 0;
                    
                    // Preserve some of the original movement magnitude
                    if (validMovement.magnitude > 0.01f)
                    {
                        validMovement = validMovement.normalized * Mathf.Min(validMovement.magnitude, originalMagnitude);
                    }
                    
                    return validMovement;
                }
            }
        }

        // No valid direction found - stop movement
        // This should be rare if NavMesh is properly set up
        Debug.LogWarning($"NavMeshConstraintValidator: Could not find valid movement from {currentPosition}. Stopping movement.");
        return Vector3.zero;
    }

    /// <summary>
    /// Additional safety check - detects if movement toward a direction would hit a NavMesh edge.
    /// Used for early detection of potential wall collisions.
    /// </summary>
    /// <param name="currentPosition">Current position of the NPC</param>
    /// <param name="movementDirection">Direction of intended movement (normalized)</param>
    /// <param name="checkDistance">How far ahead to check</param>
    /// <param name="edgeNormal">Output: Normal vector of the detected edge</param>
    /// <returns>True if movement would hit a NavMesh edge</returns>
    public bool WouldHitNavMeshEdge(Vector3 currentPosition, Vector3 movementDirection, float checkDistance, out Vector3 edgeNormal)
    {
        if (movementDirection.magnitude < 0.01f)
        {
            edgeNormal = Vector3.zero;
            return false;
        }

        Vector3 targetPosition = currentPosition + movementDirection.normalized * checkDistance;
        
        NavMeshHit hit;
        if (NavMesh.Raycast(currentPosition, targetPosition, out hit, NavMesh.AllAreas))
        {
            edgeNormal = hit.normal;
            edgeNormal.y = 0;
            edgeNormal.Normalize();
            return true;
        }
        
        edgeNormal = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Checks if the current position is on a valid NavMesh.
    /// Useful for debugging or safety checks.
    /// </summary>
    /// <param name="position">Position to check</param>
    /// <returns>True if position is on NavMesh</returns>
    public bool IsOnNavMesh(Vector3 position)
    {
        NavMeshHit hit;
        return NavMesh.SamplePosition(position, out hit, 0.1f, NavMesh.AllAreas);
    }

    /// <summary>
    /// Sets the sampling distance for NavMesh validation.
    /// Larger values are more forgiving but less accurate.
    /// </summary>
    public void SetSamplingDistance(float distance)
    {
        samplingDistance = Mathf.Max(0.1f, distance);
    }

    /// <summary>
    /// Sets the maximum number of rotation attempts when finding alternative movement.
    /// More attempts = more chances to find valid movement, but slightly more expensive.
    /// </summary>
    public void SetMaxRotationAttempts(int attempts)
    {
        maxRotationAttempts = Mathf.Clamp(attempts, 3, 15);
    }
}
