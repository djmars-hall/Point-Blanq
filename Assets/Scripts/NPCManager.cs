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

    [Header("Zone Lists:")]
    [SerializeField] private Transform zoneParent;
    [SerializeField] internal MapZone[] spawningZones;
    [SerializeField] internal MapZone[] gatheringZones;

    [Header("NPC Master List:")]
    public NPCController[] npcList;

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
        Debug.Log(NetworkManager.Singleton.PrefabHandler.ToString());

        // Populate zone arrays from children if zoneParent is assigned
        if (zoneParent != null)
        {
            MapZone[] allZones = new MapZone[zoneParent.childCount];
            List<MapZone> spawningList = new List<MapZone>();
            List<MapZone> gatheringList = new List<MapZone>();

            for (int i = 0; i < zoneParent.childCount; i++)
            {
                MapZone zone = zoneParent.GetChild(i).GetComponent<MapZone>();
                if (zone != null)
                {
                    allZones[i] = zone;
                    if (zone.IsSpawningZone())
                        spawningList.Add(zone);
                    if (zone.IsGatheringZone())
                        gatheringList.Add(zone);
        }
            }

            spawningZones = spawningList.ToArray();
            gatheringZones = gatheringList.ToArray();
        }

        npcList = new NPCController[maxNPCs];
        Debug.Log("NPCManager Start call!");
        for (int i = 0; i < BountyManager.Instance.players.Count; i++)
        {
            //Material material = BountyManager.Instance.playerMaterials[i];
            for (int n = 0; n < maxDuplicatesPerPlayer; n++)
            {
                Debug.Log("spawn : " + n);
                NPCController newNPC = SpawnNPC(i);
                //newNPC.GetComponent<NetworkObject>().Spawn();
            }
        }
    }

    /// <summary>
    /// Spawns a new NPC at a random position within a randomly selected spawning zone.
    /// </summary>
    /// <param name="materialIndex">Index for player material assignment</param>
    /// <returns>The spawned NPCController</returns>
    public NPCController SpawnNPC(int materialIndex = 0)
    {
        if (!IsHost || spawningZones == null || spawningZones.Length == 0) return null;
        NPCController newNPC = null;

        // Randomly select a spawning zone
        MapZone zone = spawningZones[Random.Range(0, spawningZones.Length)];
        Vector3 spawnPos = zone.GetRandomPointInArea() + new Vector3(0f, -0.5f, 0f); //add slight y offset to avoid spawning in air

        newNPC = NPCController.objectPool.Spawn(spawnPos, Vector3.zero);
        newNPC.NetworkObject.Spawn();

        //Instantiate(npcPrefab, randomPos, Quaternion.identity).GetComponent<NPCController>();

        //playerMaterial = BountyManager.Instance.playerMaterials[materialIndex];

        //Assign Info
        newNPC.ReplaceMaterialRpc(materialIndex);
        newNPC.AssignSlotRpc(materialIndex);

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
