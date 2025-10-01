using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PublicUseCube : NetworkBehaviour
{
    [SerializeField] float speed = 1.0f;

    void Update()
    {
        if (!IsOwner) return; // Only the owner (Host or owning client) can move the cube

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 move = new Vector3(h * Time.deltaTime * speed, 0, v * Time.deltaTime * speed);
        if (move != Vector3.zero)
        {
            Vector3 newPos = transform.position + move;
            SubmitPositionRequestServerRpc(newPos);
        }
    }

    [ServerRpc]
    void SubmitPositionRequestServerRpc(Vector3 pos)
    {
        // The Host receives the position and broadcasts it to all clients
        UpdatePositionClientRpc(pos);
    }

    [ClientRpc]
    void UpdatePositionClientRpc(Vector3 pos)
    {
        transform.position = pos;
    }

}
