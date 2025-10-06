using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BountyManager : MonoBehaviour
{
    public static BountyManager Instance;

    [Header("Point Values:")]
    public int kill = 500; //points for killing your target
    public int penalty = -200; //points lost for killing wrong target/npc

    [Header("Distance Thresholds + Multipliers:")]
    public Vector2 pointBlank;
    public Vector2 close;
    public Vector2 medium;
    public Vector2 far;

    [Header("Other Multipliers:")]
    public float backShotMultiplier;

    [System.Serializable]
    public class PlayerEntry
    {
        [Header("Player Information:")]
        public ulong PlayerClient;

        public GameObject currentObject;

        public int points;

        [Header("Bounty Information:")]
        public PlayerEntry currentTarget;
    }

    public List<PlayerEntry> players = new List<PlayerEntry>();

    private void Awake()
    {
        Instance = this;
    }

    [Rpc(SendTo.Everyone)]
    public void NewEntry(ulong clientId, GameObject obj)
    {
        BountyManager.PlayerEntry newEntry = new BountyManager.PlayerEntry();
        newEntry.PlayerClient = clientId;
        newEntry.currentObject = obj;

        BountyManager.Instance.players.Add(newEntry);
    }

    /// <summary>
    /// Call from anywhere when triggering a kill to have the bounty manager assign points.
    /// </summary>
    /// <param name="shooter">The character doing the shooting.</param>
    /// <param name="target">The character being shot.</param>
    /// <param name="distance">Distance between the shooter and the target.</param>
    /// <param name="shotFromBehind">Whether or not the target was hit in the back or not.</param>
    [Rpc(SendTo.Server)]
    public void CheckKill(bool npc_kill, ulong shooter, ulong target, float distance, bool shotFromBehind)
    {
        PlayerEntry shooterPlayer = null;
        PlayerEntry targetPlayer = null;

        //Check Shooter Validity
        foreach(PlayerEntry player in players)
        {
            if (player.PlayerClient == shooter) shooterPlayer = player;
        }

        if (shooterPlayer == null) return;

        //Check Target Validity
        foreach (PlayerEntry player in players)
        {
            if (player.PlayerClient == target) targetPlayer = player;
        }

        //Check for Points
        if (targetPlayer == null) //Hit an Npc
        {
            UpdatePoints(shooterPlayer, penalty);
        }
        else //Hit a Player
        {
            //Was this your target?
            if (shooterPlayer.currentTarget == targetPlayer) //Killed target
            {
                float totalPoints = kill;
                //Multipliers
                if (shotFromBehind) totalPoints *= backShotMultiplier;
                UpdatePoints(shooterPlayer, (int)totalPoints);
            }
            else
            UpdatePoints(shooterPlayer, penalty);
        }

    }

    [Rpc(SendTo.Everyone)]
    public void UpdatePoints(PlayerEntry player, int value)
    {
        foreach(PlayerEntry _player in players)
        {
            if (_player == player)
            {
                _player.points += value;
            }
        }
    }
}
