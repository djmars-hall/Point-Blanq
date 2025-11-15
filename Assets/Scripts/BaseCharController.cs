using UnityEngine;
using Unity.Netcode;
using System;

public class BaseCharController : NetworkBehaviour
{
    [SerializeField] internal float speed = 3.5f;
    [SerializeField] internal float rotationSpeed = 60f;

    public Animator characterAnimator;

    public MeshRenderer[] meshes;

    protected Rigidbody rb;

    /// <summary>
    /// Public getter for the Rigidbody component
    /// </summary>
    public Rigidbody Rb => rb;

    /// <summary>
    /// Assertiveness level determines right-of-way in collision scenarios.
    /// Higher values mean the character is more assertive and will not yield.
    /// Range: 1-9999
    /// </summary>
    protected int assertivenessLevel;
    public int AssertivenessLevel => assertivenessLevel;

    // Track actual velocity for movement detection
    private Vector3 previousPosition;
    private Vector3 actualVelocity;

    /// <summary>
    /// The actual velocity of the character based on position changes between frames.
    /// Used for determining if a character is moving and their heading.
    /// </summary>
    public Vector3 ActualVelocity => actualVelocity;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Initialize assertiveness level with a random value
        assertivenessLevel = UnityEngine.Random.Range(1, 10000);
        previousPosition = transform.position;
    }

    void Start()
    {
    }
    void Update()
    {
    }

    protected virtual void FixedUpdate()
    {
        // Calculate actual velocity based on position change
        if (Time.fixedDeltaTime > 0)
        {
            actualVelocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
            previousPosition = transform.position;
        }
    }


    protected void ProcessMovement(float forward_movement, float rotation_dir)
    {
        float rot = rotation_dir * Time.fixedDeltaTime * speed * rotationSpeed;
        Vector3 move = new Vector3(0, 0, forward_movement * Time.fixedDeltaTime * speed);
        if (move != Vector3.zero || rot != 0)
        {
            Vector3 newPos = transform.position + move.z * transform.forward;
            Quaternion newRot = transform.rotation * Quaternion.Euler(0, rot, 0);
            rb.MovePosition(newPos);
            rb.MoveRotation(newRot);
        }
        if (!GameManager.Instance.ignoreNetwork) UpdatePositionClientRpc(rb.position, rb.rotation);
    }


    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    public void UpdatePositionClientRpc(Vector3 pos, Quaternion rot)
    {
        // Use Rigidbody for smooth interpolation on clients
        if (rb != null)
        {
            rb.position = pos;
            rb.rotation = rot;
        }
        else
        {
            Debug.LogWarning("Why no rb?");
            transform.position = pos;
            transform.rotation = rot;
        }
    }

    [Rpc(SendTo.Everyone)]
    public void ReplaceMaterialRpc(int materialIndex = 0)
    {
        foreach (MeshRenderer renderer in meshes)
        {
            renderer.material = BountyManager.Instance.playerMaterials[materialIndex];
        }
    }

    [Rpc(SendTo.Everyone)]
    public void DisableMeRpc()
    {
        gameObject.SetActive(false);
    }
}
