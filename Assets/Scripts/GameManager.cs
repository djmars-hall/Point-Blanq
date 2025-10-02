using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using NUnit.Framework;

public class GameManager : NetworkBehaviour
{
    [SerializeField] private List<PrivateUseCube> privateCubes;

    // Update is called once per frame
    void Update()
    {
        if (!IsServer) return;

        if (Input.GetKeyDown(KeyCode.M))
        {
            for(int i = 0; i < Mathf.Min(privateCubes.Count, NetworkManager.Singleton.ConnectedClientsList.Count); i++)
            {
                ulong clientId = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
                privateCubes[i].GetComponent<NetworkObject>().ChangeOwnership(clientId);
            }
        }
    }
}
