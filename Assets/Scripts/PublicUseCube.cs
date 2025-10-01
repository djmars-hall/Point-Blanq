using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PublicUseCube : NetworkBehaviour
{
    //[SerializeField] private Transform tranform;
    [SerializeField] float speed = 1.0f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");


        transform.position += new Vector3(h*Time.deltaTime * speed, 0, v*Time.deltaTime * speed);


    }

    //[Rpc(SendTo.ClientsAndHost)]
    void NetworkRPCPositionLol(Vector3 pos, ulong netId) => transform.position = pos;

}
