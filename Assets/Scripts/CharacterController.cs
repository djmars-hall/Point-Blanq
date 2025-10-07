using UnityEngine;
using Unity.Netcode;

public class CharacterController : NetworkBehaviour
{
    [SerializeField] internal float speed = 3.5f;
    [SerializeField] internal float rotationSpeed = 60f;

    public Animator characterAnimator;

    public MeshRenderer[] meshes;

    void Start()
    {
    }
    void Update()
    {
    }

    protected void ProcessMovement(float forward_movement, float rotation_dir)
    {
        float rot = rotation_dir * Time.deltaTime * speed * rotationSpeed;
        Vector3 move = new Vector3(0, 0, forward_movement * Time.deltaTime * speed);
        if (move != Vector3.zero || rot != 0)
        {
            Vector3 newPos = transform.position + move.z * transform.forward;
            transform.position = newPos;
            transform.Rotate(new Vector3(0, rot, 0));
            UpdatePositionClientRpc(newPos, transform.rotation);
        }
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void UpdatePositionClientRpc(Vector3 pos, Quaternion rot)
    {
        transform.position = pos;
        transform.rotation = rot;
    }

    [Rpc(SendTo.Everyone)]
    public void ReplaceMaterialRpc(int materialIndex = 0)
    {
        foreach (MeshRenderer renderer in meshes)
        {
            renderer.material = BountyManager.Instance.playerMaterials[materialIndex];
        }
    }
}
