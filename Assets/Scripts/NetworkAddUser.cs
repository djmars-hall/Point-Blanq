using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

public class NetworkAddUser : MonoBehaviour
{
    enum MenuState
    {
        None,
        Lobby,
        Results,
    }

    public static bool startOnResultScene = false;
    [SerializeField] TMP_InputField ip_field;
    [SerializeField] TMP_Text statusText;
    [SerializeField] GameObject connectionControls;
    [SerializeField] GameObject hostControls;
    [SerializeField] GameObject results;
    public static bool isHostOrClient = false;
    MenuState menuState = MenuState.Lobby;

    private void Start()
    {
        if (startOnResultScene)
        {
            ScorePanel.inst.Initialize();
            SwitchMenuState(MenuState.Results);
        }
        else SwitchMenuState(MenuState.Lobby);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", (ushort)7772);
    }

    private void SwitchMenuState(MenuState newstate)
    {
        hostControls.SetActive(false);
        connectionControls.SetActive(false);
        results.SetActive(false);
        menuState = newstate;
        switch (menuState)
        {
            case MenuState.None:
                statusText.text = "...";
                break;
            case MenuState.Lobby:
                if (!isHostOrClient)
                {
                    connectionControls.SetActive(true);
                    statusText.text = "Disconnected...";
                }
                else if (NetworkManager.Singleton.IsHost)
                {
                    hostControls.SetActive(true);
                    statusText.text = "Hosting.";
                }
                else
                {
                    statusText.text = "Connecting as Client.";
                }
                break;
            case MenuState.Results: 
                results.SetActive(true);
                statusText.text = "Results.";
                break;
        }
    }

    public void ConnectToIP()
    {
        if (isHostOrClient) return;
        Debug.Log("Applied : " + ip_field.text);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip_field.text, (ushort)7772);
        isHostOrClient = true;
        NetworkManager.Singleton.StartClient();
        Debug.Log("CLIENT");
        SwitchMenuState(MenuState.Lobby);
    }
    public void HostGame()
    {
        if (isHostOrClient) return;
        isHostOrClient = true;
        NetworkManager.Singleton.StartHost();
        Debug.Log("HOSTING");
        SwitchMenuState(MenuState.Lobby);
    }
    public void StartGame()
    {
        if (!isHostOrClient) return;
        NetworkState.inst.RegisterAllClients();
        NetworkManager.Singleton.SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
    }
    public void CloseResults()
    {
        SwitchMenuState(MenuState.Lobby);
    }
}
