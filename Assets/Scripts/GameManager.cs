using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using NUnit.Framework;
using System.Threading.Tasks;

public class GameManager : NetworkBehaviour
{
    [SerializeField] private List<PlayerController> players;
    bool allClientsLoaded;

    private void Start()
    {
        NetworkState.inst.NotifyHostReadyRpc(NetworkManager.Singleton.LocalClientId);
        if (IsHost) SetupGameForAllPlayers();
    }
    private async void SetupGameForAllPlayers()
    {
        if (!IsHost) return;
        while (!NetworkState.inst.AllClientsReady())
        {
            await Task.Delay(25);
        }
        for (int i = 0; i < players.Count; i++)
        {
            if (i < NetworkManager.Singleton.ConnectedClientsList.Count)
            {
                ulong clientId = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
                players[i].GetComponent<NetworkObject>().ChangeOwnership(clientId);
                players[i].ParentCameraRpc();

                //Update Bounty Manager
                BountyManager.Instance.NewEntryRpc(clientId);
            }
            else
            {
                players[i].gameObject.SetActive(false);
            }
        }

        //Initialize Spawner
        NPCManager.Instance.Initialize();
    }
}
