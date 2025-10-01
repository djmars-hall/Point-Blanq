using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PublicUseCube : NetworkBehaviour
{
    [SerializeField] float speed = 3.5f;

    void Update()
    {
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

}
