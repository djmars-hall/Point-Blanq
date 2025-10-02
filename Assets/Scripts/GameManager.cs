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
            for(int i = 0; i < privateCubes.Count; i++)
            {
                if(i < NetworkManager.Singleton.ConnectedClientsList.Count)
                {
                    ulong clientId = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
                    privateCubes[i].GetComponent<NetworkObject>().ChangeOwnership(clientId);
                    privateCubes[i].ParentCameraRpc();
                }
                else
                {
                    privateCubes[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
