using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkAddUser : MonoBehaviour
{
    bool isHostOrClient = false;


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

}
