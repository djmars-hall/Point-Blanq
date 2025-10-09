using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static Unity.Netcode.NetworkManager;

public class NetworkState : NetworkBehaviour
{

    public Dictionary<ulong,bool> client_ready_states;
    public bool refusing_connections;

    public static NetworkState inst;
    public void Start()
    {
        DontDestroyOnLoad(gameObject);
        inst = this;
        client_ready_states = new Dictionary<ulong, bool>();
        NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
    }

    public void RegisterAllClients()
    {
        if (!IsHost) return;
        refusing_connections = true;
        client_ready_states = new Dictionary<ulong, bool>();
        for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            client_ready_states.Add(NetworkManager.Singleton.ConnectedClientsList[i].ClientId,false);
        }
    }

    [Rpc(SendTo.Server)]
    public void NotifyHostReadyRpc(ulong client_id)
    {
        if (!IsHost) return;
        client_ready_states[client_id] = true;
    }
    public void ResetReadyStates()
    {
        for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            client_ready_states[NetworkManager.Singleton.ConnectedClientsList[i].ClientId] = false;
        }
    }
    public bool AllClientsReady()
    {
        bool result = true;
        for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            if (!client_ready_states[NetworkManager.Singleton.ConnectedClientsList[i].ClientId])
            {
                result = false;
                break;
            }
        }
        return result;
    }

    private void ApproveConnection(ConnectionApprovalRequest request, ConnectionApprovalResponse response)
    {
        response.Approved = refusing_connections;
    }
}
