using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the Lobby Scene UI.
/// Attach to a root Canvas GameObject in the LobbyScene.
///
/// Required UI Hierarchy (set via Inspector):
/// ── PanelInit          (shown while signing in)
/// ── PanelMenu          (host / join buttons)
///    ── InputJoinCode   (TMP_InputField)
///    ── BtnHost
///    ── BtnJoin
/// ── PanelLobby         (waiting room)
///    ── TxtJoinCode     (shown to host)
///    ── TxtStatus
///    ── PlayerListParent (parent for player row prefabs)
///    ── BtnReady
///    ── BtnStart        (host only)
///    ── BtnLeave
/// ── PanelError
///    ── TxtError
///    ── BtnErrorClose
/// </summary>
public class LobbyUI : MonoBehaviour
{
    // ── Panel References ─────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject panelInit;
    [SerializeField] private GameObject panelMenu;
    [SerializeField] private GameObject panelLobby;
    [SerializeField] private GameObject panelError;

    // ── Menu Panel ───────────────────────────────────────────────────────────
    [Header("Menu Panel")]
    [SerializeField] private TMP_InputField inputJoinCode;
    [SerializeField] private Button btnHost;
    [SerializeField] private Button btnJoin;
    [SerializeField] private TMP_Text txtMenuStatus;

    // ── Lobby Panel ──────────────────────────────────────────────────────────
    [Header("Lobby Panel")]
    [SerializeField] private TMP_Text txtJoinCode;
    [SerializeField] private TMP_Text txtLobbyStatus;
    [SerializeField] private Transform playerListParent;
    [SerializeField] private GameObject playerRowPrefab;   // see PlayerRow.cs below
    [SerializeField] private Button btnReady;
    [SerializeField] private Button btnStart;
    [SerializeField] private Button btnLeave;
    [SerializeField] private Button btnCopyCode;

    // ── Error Panel ──────────────────────────────────────────────────────────
    [Header("Error Panel")]
    [SerializeField] private TMP_Text txtError;
    [SerializeField] private Button btnErrorClose;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isReady = false;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private async void Start()
    {
        // Show init panel while UGS connects
        ShowPanel(panelInit);
        SetButtonsInteractable(false);

        // Subscribe to events
        var lm = LobbyManager.Instance;
        lm.OnStatusMessage += OnStatusMessage;
        lm.OnJoinCodeReady += OnJoinCodeReady;
        lm.OnPlayerListUpdated += OnPlayerListUpdated;
        lm.OnError += OnLobbyError;

        // Wire buttons
        btnHost.onClick.AddListener(OnHostClicked);
        btnJoin.onClick.AddListener(OnJoinClicked);
        btnReady.onClick.AddListener(OnReadyClicked);
        btnStart.onClick.AddListener(OnStartClicked);
        btnLeave.onClick.AddListener(OnLeaveClicked);
        btnCopyCode.onClick.AddListener(OnCopyCodeClicked);
        btnErrorClose.onClick.AddListener(() => ShowPanel(panelMenu));

        // Initialize UGS
        bool ok = await lm.InitializeAsync();
        if (ok)
        {
            ShowPanel(panelMenu);
            SetButtonsInteractable(true);
        }
        else
        {
            ShowError("Failed to connect to Unity Services. Check your internet connection.");
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe safely — LobbyManager may already be destroyed
        var lm = LobbyManager.Instance;
        if (lm == null) return;
        lm.OnStatusMessage -= OnStatusMessage;
        lm.OnJoinCodeReady -= OnJoinCodeReady;
        lm.OnPlayerListUpdated -= OnPlayerListUpdated;
        lm.OnError -= OnLobbyError;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Button Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async void OnHostClicked()
    {
        SetButtonsInteractable(false);
        txtMenuStatus.text = "Creating game...";
        await LobbyManager.Instance.HostGameAsync();
        ShowPanel(panelLobby);
        btnStart.gameObject.SetActive(true);   // only host sees Start button
        btnStart.interactable = false;          // enabled once enough players join
        SetButtonsInteractable(true);
    }

    private async void OnJoinClicked()
    {
        SetButtonsInteractable(false);
        txtMenuStatus.text = "Joining game...";
        await LobbyManager.Instance.JoinGameAsync(inputJoinCode.text);
        ShowPanel(panelLobby);
        btnStart.gameObject.SetActive(false);  // clients don't see Start
        SetButtonsInteractable(true);
    }

    private async void OnReadyClicked()
    {
        _isReady = !_isReady;
        UpdateReadyButtonVisual();
        await LobbyManager.Instance.SetReadyAsync(_isReady);
    }

    private async void OnStartClicked()
    {
        btnStart.interactable = false;
        await LobbyManager.Instance.StartGameAsync();
    }

    private void OnLeaveClicked()
    {
        LobbyManager.Instance.LeaveLobby();
        ShowPanel(panelMenu);
        txtMenuStatus.text = "";
    }

    private void OnCopyCodeClicked()
    {
        GUIUtility.systemCopyBuffer = txtJoinCode.text.Replace("Code: ", "").Trim();
        btnCopyCode.GetComponentInChildren<TMP_Text>().text = "Copied!";
        Invoke(nameof(ResetCopyButton), 2f);
    }

    private void ResetCopyButton() =>
        btnCopyCode.GetComponentInChildren<TMP_Text>().text = "Copy";

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Event Handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void OnStatusMessage(string msg)
    {
        txtMenuStatus.text = msg;
        txtLobbyStatus.text = msg;
    }

    private void OnJoinCodeReady(string code)
    {
        txtJoinCode.text = $"Code: {code}";
        txtJoinCode.gameObject.SetActive(true);
    }

    private void OnPlayerListUpdated(List<LobbyPlayerData> players)
    {
        // Guard: this UI may have been destroyed if scene changed
        if (this == null || playerListParent == null || playerRowPrefab == null) return;

        // Clear old rows
        foreach (Transform child in playerListParent)
            Destroy(child.gameObject);

        int readyCount = 0;

        foreach (var p in players)
        {
            if (p.IsReady) readyCount++;

            Debug.Log($"[LobbyUI] Player: {p.PlayerId} | Role: {p.Role} | Ready: {p.IsReady} | IsLocal: {p.IsLocal} | IsHost: {p.IsHost}");

            var row = Instantiate(playerRowPrefab, playerListParent);
            var pr = row.GetComponent<PlayerRow>();
            if (pr != null)
                pr.Setup(p.Role, p.IsReady, p.IsHost, p.IsLocal);
        }

        // Host can start if 2+ players and everyone is ready
        if (LobbyManager.Instance != null && LobbyManager.Instance.IsHost && btnStart != null)
        {
            bool canStart = players.Count >= 2 && readyCount == players.Count;
            btnStart.interactable = canStart;
        }

        if (txtLobbyStatus != null)
            txtLobbyStatus.text = $"Players: {players.Count}/{LobbyManager.Instance?.MaxPlayers}";
    }

    private void OnLobbyError(string msg) => ShowError(msg);

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject panel)
    {
        panelInit.SetActive(panel == panelInit);
        panelMenu.SetActive(panel == panelMenu);
        panelLobby.SetActive(panel == panelLobby);
        panelError.SetActive(panel == panelError);
    }

    private void ShowError(string msg)
    {
        txtError.text = msg;
        ShowPanel(panelError);
        SetButtonsInteractable(true);
    }

    private void SetButtonsInteractable(bool state)
    {
        btnHost.interactable = state;
        btnJoin.interactable = state;
    }

    private void UpdateReadyButtonVisual()
    {
        var txt = btnReady.GetComponentInChildren<TMP_Text>();
        if (txt != null)
            txt.text = _isReady ? "✓ Ready!" : "Ready Up";

        // Tint green when ready
        var colors = btnReady.colors;
        colors.normalColor = _isReady ? new Color(0.2f, 0.8f, 0.3f) : Color.white;
        btnReady.colors = colors;
    }

    #endregion
}