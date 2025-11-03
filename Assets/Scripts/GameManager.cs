using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    private bool gameStarted = false;
    [SerializeField] private float timeLeftInMatch;

    [SerializeField] private List<PlayerController> players;

    public bool ignoreNetwork = false;

    private void Awake()
    {
        Instance = this;

        if (ignoreNetwork) GetComponent<NetworkObject>().enabled = false;
    }

    private void Start()
    {
        if (!ignoreNetwork)
        {
            NetworkState.inst.NotifyHostReadyRpc(NetworkManager.Singleton.LocalClientId);
            if (IsHost) SetupGameForAllPlayers();
            if (ScorePanel.inst != null) ScorePanel.inst.Initialize();
        }
        else StartGameSolo();
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
                //players[i].gameObject.SetActive(false);
                players[i].DisableMeRpc();
            }
        }
        //Initialize Spawner
        NPCManager.Instance.Initialize();
        // Start Game
        StartGameRpc();
    }

    private void StartGameSolo()
    {
        players[0].ParentCamera();
        players[1].gameObject.SetActive(false);
        players[2].gameObject.SetActive(false);
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
            if (roll == i) roll += 1;
            if (roll >= ncount) roll = 0;
            Debug.Log(" i : "+i+" roll : "+roll+" ncount : "+ncount);
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
        if (!IsHost) return;
        NPCManager.Instance.Cleanup();
        NetworkAddUser.startOnResultScene = true;
        NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
    }
}
