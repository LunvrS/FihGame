using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manages the race — countdown, lap tracking, positions, win condition.
/// Place on a NetworkObject in the GameScene.
/// </summary>
public class RaceManager : NetworkBehaviour
{
    public static RaceManager Instance { get; private set; }

    [Header("Race Settings")]
    [SerializeField] public int TotalLaps      = 3;
    [SerializeField] private float countdownTime = 3f;

    // ── Network State ─────────────────────────────────────────────────────────
    public NetworkVariable<bool> RaceStarted = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> RaceFinished = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<float> RaceTime = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Events (local UI) ─────────────────────────────────────────────────────
    public UnityEvent<int>             OnCountdown;       // fires 3,2,1,GO
    public UnityEvent                  OnRaceStart;
    public UnityEvent<ulong, int>      OnPositionUpdate;  // clientId, position
    public UnityEvent<ulong>           OnKartFinishedRace; // clientId of finisher

    // ── Private ───────────────────────────────────────────────────────────────
    private List<KartController>        _karts            = new List<KartController>();
    private Dictionary<ulong, int>      _finishOrder      = new Dictionary<ulong, int>();
    private Dictionary<string, bool>    _checkpointsPassed = new Dictionary<string, bool>();
    private int                         _finishCount;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity / NGO Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!IsServer || !RaceStarted.Value || RaceFinished.Value) return;
        RaceTime.Value += Time.deltaTime;
        UpdatePositions();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Race Start

    /// <summary>Called by GameSpawner once all karts are spawned.</summary>
    public void RegisterKartsAndStart(List<KartController> karts)
    {
        if (!IsServer) return;
        _karts = karts;
        StartCoroutine(CountdownCoroutine());
    }

    private IEnumerator CountdownCoroutine()
    {
        // Freeze all karts during countdown
        SetKartsMoveable(false);

        for (int i = (int)countdownTime; i > 0; i--)
        {
            SendCountdownClientRpc(i);
            yield return new WaitForSeconds(1f);
        }

        SendCountdownClientRpc(0); // 0 = GO!
        SetKartsMoveable(true);
        RaceStarted.Value = true;
        RaceStartClientRpc();
    }

    private void SetKartsMoveable(bool canMove)
    {
        foreach (var k in _karts)
        {
            var rb = k.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = !canMove;
        }
    }

    [ClientRpc]
    private void SendCountdownClientRpc(int number)
    {
        OnCountdown?.Invoke(number);
    }

    [ClientRpc]
    private void RaceStartClientRpc()
    {
        OnRaceStart?.Invoke();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Checkpoints & Laps

    public void OnKartCrossedCheckpoint(KartController kart, string checkpointName)
    {
        // Mark checkpoint as passed for this kart (simple implementation)
        string key = $"{kart.OwnerClientId}_{checkpointName}";
        _checkpointsPassed[key] = true;
    }

    public void OnKartCrossedFinishLine(KartController kart)
    {
        if (!RaceStarted.Value) return;
        kart.CompleteLap();
    }

    public void OnKartFinished(KartController kart)
    {
        if (!IsServer) return;
        _finishCount++;
        _finishOrder[kart.OwnerClientId] = _finishCount;

        KartFinishedClientRpc(kart.OwnerClientId, _finishCount);

        // Race ends when all karts finish or first place finishes
        if (_finishCount == 1)
        {
            // Give others 30 seconds to finish
            StartCoroutine(EndRaceAfterDelay(30f));
        }

        if (_finishCount >= _karts.Count)
        {
            StopAllCoroutines();
            RaceFinished.Value = true;
        }
    }

    private IEnumerator EndRaceAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        RaceFinished.Value = true;
    }

    [ClientRpc]
    private void KartFinishedClientRpc(ulong clientId, int place)
    {
        OnKartFinishedRace?.Invoke(clientId);
        Debug.Log($"[RaceManager] Client {clientId} finished in place {place}!");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Position Tracking

    private void UpdatePositions()
    {
        // Sort karts by lap count then distance to next checkpoint
        _karts.Sort((a, b) =>
        {
            if (a.LapCount.Value != b.LapCount.Value)
                return b.LapCount.Value.CompareTo(a.LapCount.Value);

            // Same lap — use distance from start as tiebreaker
            return 0;
        });

        for (int i = 0; i < _karts.Count; i++)
        {
            if (_karts[i].RacePosition.Value != i + 1)
                _karts[i].RacePosition.Value = i + 1;
        }
    }

    #endregion
}
