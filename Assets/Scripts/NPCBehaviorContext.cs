using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Context data structure passed to all boid behaviors.
/// Contains all information needed for behaviors to make movement decisions.
/// </summary>
public struct NPCBehaviorContext
{
    // === Position & Movement ===
    /// <summary>Current world position of the NPC</summary>
    public Vector3 position;
    
    /// <summary>Current forward direction of the NPC (normalized)</summary>
    public Vector3 forward;
    
    /// <summary>Current velocity vector (units per second)</summary>
    public Vector3 currentVelocity;

    // === Goals & Path ===
    /// <summary>Next path corner the NPC is navigating toward</summary>
    public Vector3 targetCorner;
    
    /// <summary>Previous path corner (used for path adherence calculations)</summary>
    public Vector3 previousCorner;
    
    /// <summary>List of remaining path corners (includes targetCorner as first element)</summary>
    public List<Vector3> remainingPath;

    // === Environment ===
    /// <summary>Local crowd density (0 = sparse, 1 = very crowded)</summary>
    public float localDensity;
    
    /// <summary>True if NPC is between two close NavMesh edges (in a narrow corridor)</summary>
    public bool isInCorridor;
    
    /// <summary>List of nearby characters from spatial grid (includes NPCs and Players)</summary>
    public List<BaseCharController> nearbyCharacters;

    // === Character Properties ===
    /// <summary>Assertiveness value of this NPC (1-9999, used for social interactions)</summary>
    public int assertiveness;
    
    /// <summary>Current personal space radius (dynamically adjusted by density)</summary>
    public float currentPersonalSpaceRadius;

    // === Timing ===
    /// <summary>Time step for this frame (typically Time.fixedDeltaTime)</summary>
    public float deltaTime;
}
