using UnityEngine;
using Unity.Netcode;
using Unity.VisualScripting;

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

        float rot = h * Time.deltaTime * speed * 20;

        Vector3 move = new Vector3(0, 0, v * Time.deltaTime * speed);
        if (move != Vector3.zero || rot != 0)
        {
            Vector3 newPos = transform.position + move.z * transform.forward;
            transform.position = newPos;
            transform.Rotate(new Vector3(0 , rot, 0));
            UpdatePositionClientRpc(newPos, transform.rotation);
        }
    }

    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
    void UpdatePositionClientRpc(Vector3 pos, Quaternion rot)
    {
        transform.position = pos;
        transform.rotation = rot;
        Debug.Log("NOT_ME");
    }

    [Rpc(SendTo.Owner)]
    public void ParentCameraRpc()
    {
        if(!IsOwner) { Debug.LogError("BIG BAD!");  return; }
        Debug.Log("should be parenting camera");
        Camera.main.transform.SetParent(transform.GetChild(0));
        Camera.main.transform.localPosition = Vector3.zero;
        Camera.main.transform.localRotation = Quaternion.identity;
    }


    //[Rpc(SendTo.Server)]
    //void ReassignOwnershipRpc(ulong newOwnerId)
    //{
    //    NetworkObject.ChangeOwnership(newOwnerId);
    //}

}
