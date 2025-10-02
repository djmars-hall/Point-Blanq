using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkAddUser : MonoBehaviour
{
    [SerializeField] TMP_InputField ip_field;
    bool isHostOrClient = false;

    private void Start()
    {
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", (ushort)7772);
    }

    private void Update()
    {
        //NetworkManager.Singleton.

        if(Input.GetKeyDown(KeyCode.H) && !isHostOrClient)
        {
            isHostOrClient = true;
            NetworkManager.Singleton.StartHost();
            Debug.Log("HOSTING");
        }
        if(Input.GetKeyDown(KeyCode.C) && !isHostOrClient)
        {
            isHostOrClient = true;
            NetworkManager.Singleton.StartClient();
            Debug.Log("CLIENT");
        }
    }

    public void ApplyIP()
    {
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetConnectionData(ip_field.text, (ushort)7772);
    }

}
