using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Persists across scenes. Tracks which role each NetworkClient has.
/// Place on a NetworkManager GameObject or alongside it.
///
/// After scene load this is the source of truth for:
///   - Which client is the Fisher
///   - Which clients are Fish
///   - Game-wide state (e.g. has the game ended)
/// </summary>
public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    // Synced game state
    public NetworkVariable<bool> GameStarted = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> GameOver = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> FisherWon = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        GameOver.OnValueChanged  += OnGameOverChanged;
    }

    public override void OnNetworkDespawn()
    {
        GameOver.OnValueChanged  -= OnGameOverChanged;
    }

    // ── Server-side game control ─────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void NotifyFisherWonServerRpc()
    {
        if (GameOver.Value) return;
        FisherWon.Value = true;
        GameOver.Value  = true;
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotifyFishWonServerRpc()
    {
        if (GameOver.Value) return;
        FisherWon.Value = false;
        GameOver.Value  = true;
    }

    // ── Callbacks ────────────────────────────────────────────────────────────

    private void OnGameOverChanged(bool prev, bool current)
    {
        if (!current) return;
        // Each scene can subscribe to this or check NetworkGameManager.Instance.GameOver.Value
        Debug.Log(FisherWon.Value ? "Fisher wins!" : "Fish win!");
    }
}
