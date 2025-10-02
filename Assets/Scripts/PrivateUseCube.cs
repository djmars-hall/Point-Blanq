using UnityEngine;
using Unity.Netcode;

public class PrivateUseCube : NetworkBehaviour
{
    [SerializeField] float speed = 3.5f;
    

    void Update()
    {
        //if (Input.GetKeyDown(KeyCode.M))
        //{
        //    ReassignOwnershipRpc(NetworkManager.Singleton.LocalClientId);
        //}

        if (!IsOwner) { return; }
        

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move = new Vector3(h * Time.deltaTime * speed, 0, v * Time.deltaTime * speed);
        if (move != Vector3.zero)
        {
            Vector3 newPos = transform.position + move;
            transform.position = newPos;
            UpdatePositionClientRpc(newPos);
        }
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void UpdatePositionClientRpc(Vector3 pos)
    {
        transform.position = pos;
        Debug.Log("NOT_ME");
    }


    //[Rpc(SendTo.Server)]
    //void ReassignOwnershipRpc(ulong newOwnerId)
    //{
    //    NetworkObject.ChangeOwnership(newOwnerId);
    //}

}
