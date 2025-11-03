using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    [Header("Gathering Area List:")]
    [SerializeField] private Transform gatheringAreaParent;
    [SerializeField] internal GatheringArea[] gatheringAreas;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (!IsHost) return;
    }

    public void Initialize()
    {
        if (!IsHost) return;
        gatheringAreas = new GatheringArea[gatheringAreaParent.childCount];
        for (int i = 0; i < gatheringAreaParent.childCount; i++)
        {
            gatheringAreas[i] = gatheringAreaParent.GetChild(i).GetComponent<GatheringArea>();
        }
        npcList = new NPCController[maxNPCs];
        Debug.Log("NPCManager Start call!");
        for (int i = 0; i < BountyManager.Instance.players.Count; i++)
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
        if (!IsHost) return null;
        NPCController newNPC = null;
        Material playerMaterial;

        //Randomly Determine a Spawnpoint
        float randomX = Random.Range(-spawnRadius, spawnRadius) + transform.position.x;
        float randomZ = Random.Range(-spawnRadius, spawnRadius) + transform.position.z;
        Vector3 randomPos = new Vector3(randomX, -0.46f, randomZ);

        //Spawn NPC Object & Replace Material to match player
        /*
        newNPC = NetworkManager.Singleton.SpawnManager.InstantiateAndSpawn(npcPrefab, 0).GetComponent<NPCController>();
        newNPC.transform.position = randomPos;
        */
        newNPC = NPCController.objectPool.Spawn(randomPos,Vector3.zero);

        //Instantiate(npcPrefab, randomPos, Quaternion.identity).GetComponent<NPCController>();

        //playerMaterial = BountyManager.Instance.playerMaterials[materialIndex];

        //Assign Info
        if (newNPC != null)
        {
            newNPC.ReplaceMaterialRpc(materialIndex);
            newNPC.AssignSlotRpc(materialIndex);
        }
        else
        {
            Debug.Log(newNPC);
        }

        return newNPC;
    }

    // Function for cleaning up npcs and other resources at the end of battle
    public void Cleanup()
    {
        // Delete them all if we REALLY need to
        // (Bandaid solution that we can cleanup later if we figure it out)
        Debug.Log(npcList.Length);
        Debug.Log(npcList);
        for (int i = 0; i < maxNPCs; i++)
        {
            if (npcList[i] == null) continue;
            Debug.Log("Despawning!");
            npcList[i].NetworkObject.Despawn(true);
        }
        npcList = null;
    }

    //RPC Spawn NPC Function^^^^
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;

        //Show Radius
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
