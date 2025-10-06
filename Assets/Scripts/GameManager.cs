using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using NUnit.Framework;

public class GameManager : NetworkBehaviour
{
    [SerializeField] private List<PlayerController> players;

    // Update is called once per frame
    void Update()
    {
        if (!IsServer) return;

        if (Input.GetKeyDown(KeyCode.M))
        {
            for(int i = 0; i < players.Count; i++)
            {
                if(i < NetworkManager.Singleton.ConnectedClientsList.Count)
                {
                    ulong clientId = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
                    players[i].GetComponent<NetworkObject>().ChangeOwnership(clientId);
                    players[i].ParentCameraRpc();

                    //Update Bounty Manager
                    BountyManager.PlayerEntry newEntry = new BountyManager.PlayerEntry();
                    newEntry.PlayerClient = clientId;
                    newEntry.currentObject = players[i].gameObject;

                    BountyManager.Instance.players.Add(newEntry);
                }
                else
                {
                    players[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
