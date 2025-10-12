using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using NUnit.Framework;
using System.Threading.Tasks;
using System.Linq;

public class GameManager : NetworkBehaviour
{

    private bool gameStarted = false;
    [SerializeField] private float timeLeftInMatch;

    [SerializeField] private List<PlayerController> players;

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
        // Start Game
        StartGameRpc();
    }

    [Rpc(SendTo.Everyone)]
    private void StartGameRpc()
    {
        gameStarted = true;
        if (!IsHost) return;
        // FOR DEBUG randomly assign everybody a target
        int ncount = NetworkManager.Singleton.ConnectedClientsList.Count;
        for (int i = 0; i < ncount; i++)
        {
            int roll = Random.Range(0, ncount - 1);
            if (roll == i) i += 1;
            if (roll >= ncount) i = 0;
            UpdatePlayerTargetRpc(
                NetworkManager.Singleton.ConnectedClientsList[i].ClientId, 
                NetworkManager.Singleton.ConnectedClientsList[roll].ClientId
                );
        }
    }

    [Rpc(SendTo.Everyone)]
    private void UpdatePlayerTargetRpc(ulong player_id, ulong target_id)
    {
        BountyManager.PlayerEntry found_player =
            BountyManager.Instance.players.FirstOrDefault(pe => pe.PlayerClient == player_id);
        BountyManager.PlayerEntry found_target =
            BountyManager.Instance.players.FirstOrDefault(pe => pe.PlayerClient == target_id);
        found_player.currentTarget = found_target;
    }

    private void Update()
    {
        if (!gameStarted) return;
        if (timeLeftInMatch <= 0.0f) return;
        timeLeftInMatch -= Time.deltaTime;
        if (timeLeftInMatch <= 0.0f)
        {
            if (IsHost) EndGameRpc();
        }
    }

    [Rpc(SendTo.Everyone)]
    private void EndGameRpc()
    {
        Debug.Log("Game has ended!");

    }
}
