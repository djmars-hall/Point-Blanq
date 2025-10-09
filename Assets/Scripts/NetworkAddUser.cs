using Mono.Cecil.Cil;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkAddUser : MonoBehaviour
{
    [SerializeField] TMP_InputField ip_field;
    [SerializeField] TMP_Text statusText;
    [SerializeField] GameObject connectionControls;
    [SerializeField] GameObject hostControls;
    bool isHostOrClient = false;

    private void Start()
    {
        hostControls.SetActive(false);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("0.0.0.0", (ushort)7772);
    }

    public void ConnectToIP()
    {
        if (isHostOrClient) return;
        Debug.Log("Applied : " + ip_field.text);
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip_field.text, (ushort)7772);
        isHostOrClient = true;
        NetworkManager.Singleton.StartClient();
        Debug.Log("CLIENT");
        statusText.text = "Connecting as Client.";
        connectionControls.SetActive(false);
    }
    public void HostGame()
    {
        if (isHostOrClient) return;
        isHostOrClient = true;
        NetworkManager.Singleton.StartHost();
        Debug.Log("HOSTING");
        statusText.text = "Hosting.";
        connectionControls.SetActive(false);
        hostControls.SetActive(true);
    }
    public void StartGame()
    {
        if (!isHostOrClient) return;
        NetworkState.inst.RegisterAllClients();
        NetworkManager.Singleton.SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
    }
}
