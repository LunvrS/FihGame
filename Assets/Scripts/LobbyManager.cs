using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────
    public event Action<string> OnStatusMessage;
    public event Action<string> OnJoinCodeReady;
    public event Action OnLobbyReady;
    public event Action<List<LobbyPlayerData>> OnPlayerListUpdated;
    public event Action<string> OnError;

    // ── Config ────────────────────────────────────────────────────────────────
    [Header("Lobby Settings")]
    [SerializeField] private string lobbyName = "FishGameLobby";
    [SerializeField] private int maxPlayers = 3;
    [SerializeField] private float heartbeatInterval = 15f;
    [SerializeField] private float lobbyPollInterval = 2f;
    [SerializeField] private string gameSceneName = "GameScene";

    private const string KEY_RELAY_CODE = "RelayCode";
    private const string KEY_GAME_STARTED = "GameStarted";
    private const string KEY_PLAYER_ROLE = "Role";
    private const string KEY_PLAYER_READY = "Ready";

    // ── State ─────────────────────────────────────────────────────────────────
    private Lobby _currentLobby;
    private string _localPlayerId;
    private bool _isHost;
    private bool _gameStarted;

    // ── Unity Lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    private void OnDestroy() => LeaveLobby();
    private void OnApplicationQuit() => LeaveLobby();

    // ── Initialization ────────────────────────────────────────────────────────
    public async Task<bool> InitializeAsync()
    {
        try
        {
            SendStatus("Connecting to Unity Services...");
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _localPlayerId = AuthenticationService.Instance.PlayerId;
            SendStatus("Ready.");
            return true;
        }
        catch (Exception e) { SendError($"Init failed: {e.Message}"); return false; }
    }

    // ── Transport Helper ──────────────────────────────────────────────────────
    /// <summary>
    /// Gets UnityTransport, assigns it to NetworkManager.NetworkConfig, and returns it.
    /// This ensures NGO always knows about the transport before Start/Host/Client is called.
    /// </summary>
    private UnityTransport GetOrAssignTransport()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport == null)
        {
            SendError("UnityTransport component not found on the NetworkManager GameObject.\n" +
                      "→ Select NetworkManager in the scene, click Add Component, search 'Unity Transport', add it.");
            return null;
        }
        // Force-assign in case the Inspector field was left empty
        NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
        return transport;
    }

    // ── Host ──────────────────────────────────────────────────────────────────
    public async Task HostGameAsync()
    {
        try
        {
            // Validate transport FIRST before touching any network calls
            var transport = GetOrAssignTransport();
            if (transport == null) return;

            SendStatus("Creating Relay allocation...");
            Allocation relayAlloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(relayAlloc.AllocationId);

            SendStatus("Creating Lobby...");
            var lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_RELAY_CODE,   new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                    { KEY_GAME_STARTED, new DataObject(DataObject.VisibilityOptions.Member, "false") }
                },
                Player = BuildLocalPlayer("Fisher")
            };

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, lobbyOptions);
            _isHost = true;

            // Configure relay data on transport BEFORE StartHost
            var relayData = new RelayServerData(relayAlloc, "dtls");
            transport.SetRelayServerData(relayData);

            NetworkManager.Singleton.StartHost();

            OnJoinCodeReady?.Invoke(_currentLobby.LobbyCode);
            SendStatus($"Lobby created! Code: {_currentLobby.LobbyCode}");

            StartCoroutine(HeartbeatCoroutine());
            StartCoroutine(PollLobbyCoroutine());
        }
        catch (Exception e) { SendError($"Host failed: {e.Message}"); }
    }

    // ── Join ──────────────────────────────────────────────────────────────────
    public async Task JoinGameAsync(string lobbyCode)
    {
        if (string.IsNullOrWhiteSpace(lobbyCode)) { SendError("Please enter a lobby code."); return; }

        try
        {
            // Validate transport FIRST
            var transport = GetOrAssignTransport();
            if (transport == null) return;

            SendStatus("Joining lobby...");
            _currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(
                lobbyCode.Trim().ToUpper(),
                new JoinLobbyByCodeOptions { Player = BuildLocalPlayer("Fish") });
            _isHost = false;

            string relayCode = _currentLobby.Data[KEY_RELAY_CODE].Value;
            SendStatus("Connecting to Relay...");

            JoinAllocation joinAlloc = await RelayService.Instance.JoinAllocationAsync(relayCode);

            // Configure relay data on transport BEFORE StartClient
            var relayData = new RelayServerData(joinAlloc, "dtls");
            transport.SetRelayServerData(relayData);

            NetworkManager.Singleton.StartClient();

            SendStatus("Connected! Waiting for host to start...");
            StartCoroutine(PollLobbyCoroutine());
        }
        catch (LobbyServiceException e) { SendError($"Lobby error: {e.Message}"); }
        catch (Exception e) { SendError($"Join failed: {e.Message}"); }
    }

    // ── Start Game ────────────────────────────────────────────────────────────
    public async Task StartGameAsync()
    {
        if (!_isHost || _currentLobby == null) return;
        try
        {
            await LobbyService.Instance.UpdateLobbyAsync(_currentLobby.Id, new UpdateLobbyOptions
            {
                IsLocked = true,
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_GAME_STARTED, new DataObject(DataObject.VisibilityOptions.Member, "true") }
                }
            });
            NetworkManager.Singleton.SceneManager.LoadScene(
                gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (Exception e) { SendError($"Start game failed: {e.Message}"); }
    }

    // ── Player Ready ──────────────────────────────────────────────────────────
    public async Task SetReadyAsync(bool isReady)
    {
        if (_currentLobby == null) return;
        try
        {
            await LobbyService.Instance.UpdatePlayerAsync(_currentLobby.Id, _localPlayerId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { KEY_PLAYER_READY, new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Member,
                            isReady ? "true" : "false") }
                    }
                });
        }
        catch (Exception e) { Debug.LogWarning($"SetReady: {e.Message}"); }
    }

    // ── Leave ─────────────────────────────────────────────────────────────────
    public async void LeaveLobby()
    {
        StopAllCoroutines();
        if (_currentLobby == null) return;
        try
        {
            if (_isHost) await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
            else await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _localPlayerId);
        }
        catch (Exception e) { Debug.LogWarning($"LeaveLobby: {e.Message}"); }
        _currentLobby = null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    // ── Coroutines ────────────────────────────────────────────────────────────
    private IEnumerator HeartbeatCoroutine()
    {
        var delay = new WaitForSecondsRealtime(heartbeatInterval);
        while (_currentLobby != null)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
            yield return delay;
        }
    }

    private IEnumerator PollLobbyCoroutine()
    {
        var delay = new WaitForSecondsRealtime(lobbyPollInterval);
        while (_currentLobby != null)
        {
            yield return delay;
            var task = LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);
            yield return new WaitUntil(() => task.IsCompleted);
            if (task.Exception != null) { Debug.LogWarning($"Poll: {task.Exception.InnerException?.Message}"); continue; }
            _currentLobby = task.Result;
            ProcessLobbyUpdate();
        }
    }

    private void ProcessLobbyUpdate()
    {
        var players = new List<LobbyPlayerData>();
        foreach (var p in _currentLobby.Players)
        {
            players.Add(new LobbyPlayerData
            {
                PlayerId = p.Id,
                Role = GetPlayerVal(p, KEY_PLAYER_ROLE, "Fish"),
                IsReady = GetPlayerVal(p, KEY_PLAYER_READY, "false") == "true",
                IsLocal = p.Id == _localPlayerId,
                IsHost = p.Id == _currentLobby.HostId
            });
        }
        OnPlayerListUpdated?.Invoke(players);

        if (!_isHost && !_gameStarted && GetLobbyVal(KEY_GAME_STARTED, "false") == "true")
            _gameStarted = true;
    }

    private string GetPlayerVal(Player p, string key, string fb)
        => (p.Data != null && p.Data.TryGetValue(key, out var o)) ? o.Value : fb;
    private string GetLobbyVal(string key, string fb)
        => (_currentLobby?.Data != null && _currentLobby.Data.TryGetValue(key, out var o)) ? o.Value : fb;

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Player BuildLocalPlayer(string role) => new Player
    {
        Data = new Dictionary<string, PlayerDataObject>
        {
            { KEY_PLAYER_ROLE,  new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, role) },
            { KEY_PLAYER_READY, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "false") }
        }
    };

    public bool IsHost => _isHost;
    public int PlayerCount => _currentLobby?.Players?.Count ?? 0;
    public int MaxPlayers => maxPlayers;
    public bool IsInLobby => _currentLobby != null;

    private void SendStatus(string msg) => OnStatusMessage?.Invoke(msg);
    private void SendError(string msg) { Debug.LogError("[LobbyManager] " + msg); OnError?.Invoke(msg); }
}

[Serializable]
public class LobbyPlayerData
{
    public string PlayerId;
    public string Role;
    public bool IsReady;
    public bool IsLocal;
    public bool IsHost;
}