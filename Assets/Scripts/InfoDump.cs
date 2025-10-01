using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class InfoDump : MonoBehaviour
{

    int counter = 0;

    void OnGUI()
    {
        //counter++;
        //GUI.Label(new Rect(10, 120, 200, 20), $"{counter}");

        if (!NetworkManager.Singleton) return;
        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer) return;

        for(int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            GUI.Label(new Rect(10, 40*i, 200, 20), $"{ NetworkManager.Singleton.ConnectedClientsList[i].ClientId}");
        }
    }
}
