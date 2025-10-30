using System.Collections.Generic;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ScorePanel : NetworkBehaviour
{

    public class ScoreEntry
    {
        public ulong id;
        public float score;
        public TMP_Text text;
        public RectTransform rt;
    }
    public static ScorePanel inst;
    [SerializeField] GameObject visibleArea;
    [SerializeField] RectTransform contentZone;
    [SerializeField] RectTransform originalScoreEntry;
    public static Dictionary<ulong, ScoreEntry> scoreEntries;
    bool initialized;
    [SerializeField] bool resetScores;
    [SerializeField] bool initializeOnStart;
    [SerializeField] bool shownWithTab;

    private void Awake()
    {
        inst = this;
    }

    private void Start()
    {
        if (!initialized && initializeOnStart) Initialize();
    }

    void MakeScoreEntry(ulong id)
    {
        ScoreEntry entry;
        bool entry_exists = scoreEntries.ContainsKey(id);
        if (entry_exists) {
            entry = scoreEntries[id];
            Debug.Log("EntryExists : " + entry.id + "," + entry.score);
        } else {
            entry = new ScoreEntry();
            entry.id = id;
            entry.score = 0;
        }
        if (id != NetworkManager.Singleton.LocalClientId) {
            entry.rt = Instantiate(originalScoreEntry);
            entry.text = entry.rt.GetComponent<TMP_Text>();
            entry.text.transform.SetParent(contentZone, false);
            entry.text.transform.localPosition = Vector3.zero;
        } else {
            entry.rt = originalScoreEntry;
            entry.text = entry.rt.GetComponent<TMP_Text>();
        }
        if (!entry_exists) scoreEntries.Add(id, entry);
        PopulateEntry(entry);
    }

    void PopulateEntry(ScoreEntry se)
    {
        if (!se.text) return;
        se.text.text = se.id + " : " + se.score;
    }

    void JustifyEntries()
    {
        if (!initialized) return;
        ulong[] keys = new ulong[scoreEntries.Keys.Count];
        scoreEntries.Keys.CopyTo(keys, 0);
        for (int i = 0; i < keys.Length; i++)
        {
            ulong key = keys[i];
            if (!scoreEntries[key].rt) continue;
            scoreEntries[key].rt.anchoredPosition = new Vector3(0,(-i*50)-64);
            Debug.Log("Justified entry id : " + key);
        }
    }

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        if (resetScores)
        {
            scoreEntries = new Dictionary<ulong, ScoreEntry>();
        }
        else 
        {
            Debug.Log(scoreEntries.ToString());
        }
        MakeScoreEntry(NetworkManager.Singleton.LocalClientId);
        for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            ulong id = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
            if (NetworkManager.Singleton.LocalClientId == id) continue;
            MakeScoreEntry(id);
        }
        JustifyEntries();
    }

    public void UpdateScores()
    {
        if (!initialized) return;
        if (BountyManager.Instance == null) return;
        for (int i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            ulong id = NetworkManager.Singleton.ConnectedClientsList[i].ClientId;
            ScoreEntry se = scoreEntries[id];
            BountyManager.PlayerEntry bmpe = null;
            foreach (BountyManager.PlayerEntry p in BountyManager.Instance.players)
            {
                if (p.PlayerClient == id) bmpe = p;
            }
            se.score = bmpe.points;
            PopulateEntry(se);
        }
    }

    private void Update()
    {
        if (!initialized) return;
        if (visibleArea) visibleArea.SetActive(Keyboard.current.tabKey.IsPressed() || !shownWithTab);
    }

}
