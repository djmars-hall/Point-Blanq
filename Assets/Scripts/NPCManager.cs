using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public class NPCManager : NetworkBehaviour
{
    public static NPCManager Instance;

    [Header("NPC Settings:")]
    public NetworkObject npcPrefab;

    [Header("Spawning Settings:")]
    public int maxNPCs = 200;
    public int maxDuplicatesPerPlayer = 20;

    public float spawnRadius = 10f;

    [Header("NPC Master List:")]
    public NPCController[] npcList;

    [Header("Gathering Area")]
    [SerializeField] private Transform gatheringAreaParent;
    [SerializeField] internal GatheringArea[] gatheringAreas;

    private void Awake()
    {
        gatheringAreas = new GatheringArea[gatheringAreaParent.childCount];
        for (int i = 0; i < gatheringAreaParent.childCount; i++)
        {
            gatheringAreas[i] = gatheringAreaParent.GetChild(i).GetComponent<GatheringArea>();
        }
        Instance = this;
        npcList = new NPCController[maxNPCs];
    }

    public void Initialize()
    {
        for(int i = 0; i < BountyManager.Instance.players.Count; i++)
        {
            //Material material = BountyManager.Instance.playerMaterials[i];
            for (int n = 0; n < maxDuplicatesPerPlayer; n++)
            {
                NPCController newNPC = SpawnNPC(i);
                //newNPC.GetComponent<NetworkObject>().Spawn();
            }
        }
        
    }

    public NPCController SpawnNPC(int materialIndex = 0)
    {
        NPCController newNPC = null;
        Material playerMaterial;

        //Randomly Determine a Spawnpoint
        float randomX = Random.Range(-spawnRadius, spawnRadius);
        float randomZ = Random.Range(-spawnRadius, spawnRadius);
        Vector3 randomPos = new Vector3(randomX, -0.46f, randomZ);

        //Spawn NPC Object & Replace Material to match player
        newNPC = NetworkManager.Singleton.SpawnManager.InstantiateAndSpawn(npcPrefab, 0).GetComponent<NPCController>();
        newNPC.transform.position = randomPos;

        //Instantiate(npcPrefab, randomPos, Quaternion.identity).GetComponent<NPCController>();

        //playerMaterial = BountyManager.Instance.playerMaterials[materialIndex];

        //Assign Info
        newNPC.ReplaceMaterialRpc(materialIndex);
        newNPC.AssignSlotRpc(materialIndex);

        return newNPC;
    }

    //RPC Spawn NPC Function^^^^

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;

        //Show Radius
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
