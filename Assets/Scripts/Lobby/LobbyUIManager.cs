using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

public class LobbyUIManager : MonoBehaviour
{
    private UIDocument uiDocument;
    private VisualElement panelMenu, panelLobby, panelLoading, panelError;
    private Label txtCode, txtPlayers, txtLobbyStatus, txtMenuStatus, txtError;
    private TextField inputCode;
    private Button btnHost, btnJoin, btnCopy, btnStart, btnLeave, btnErrorBack;
    private VisualElement loadingBar;

    private void Start()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null || uiDocument.visualTreeAsset == null)
        {
            Debug.LogError("UIDocument or VisualTreeAsset is missing on UI object.");
            return;
        }

        var root = uiDocument.rootVisualElement;

        panelMenu = root.Q<VisualElement>("PanelMenu");
        panelLobby = root.Q<VisualElement>("PanelLobby");
        panelLoading = root.Q<VisualElement>("PanelLoading");
        panelError = root.Q<VisualElement>("PanelError");

        txtCode = root.Q<Label>("TxtCode");
        txtPlayers = root.Q<Label>("TxtPlayers");
        txtLobbyStatus = root.Q<Label>("TxtLobbyStatus");
        txtMenuStatus = root.Q<Label>("TxtMenuStatus");
        txtError = root.Q<Label>("TxtError");

        inputCode = root.Q<TextField>("InputCode");

        btnHost = root.Q<Button>("BtnHost");
        btnJoin = root.Q<Button>("BtnJoin");
        btnCopy = root.Q<Button>("BtnCopy");
        btnStart = root.Q<Button>("BtnStart");
        btnLeave = root.Q<Button>("BtnLeave");
        btnErrorBack = root.Q<Button>("BtnErrorBack");
        
        loadingBar = root.Q<VisualElement>("LoadingBar");

        if (btnHost != null) btnHost.clicked += OnHostClicked;
        if (btnJoin != null) btnJoin.clicked += OnJoinClicked;
        if (btnCopy != null) btnCopy.clicked += OnCopyClicked;
        if (btnStart != null) btnStart.clicked += OnStartClicked;
        if (btnLeave != null) btnLeave.clicked += OnLeaveClicked;
        if (btnErrorBack != null) btnErrorBack.clicked += () => ShowPanel(panelMenu);

        ShowPanel(panelMenu);
    }



    private async void OnHostClicked()
    {
        txtMenuStatus.text = "Initializing Services...";
        btnHost.SetEnabled(false);
        
        bool initialized = await LobbyManager.Instance.InitServices();
        if (!initialized)
        {
            ShowError("Failed to initialize Unity Services.");
            btnHost.SetEnabled(true);
            return;
        }

        txtMenuStatus.text = "Creating Relay...";
        string code = await LobbyManager.Instance.HostGame();
        
        if (!string.IsNullOrEmpty(code))
        {
            txtCode.text = $"JOIN CODE: {code}";
            btnStart.style.display = DisplayStyle.Flex;
            ShowPanel(panelLobby);
            
            // Register callbacks now that NetworkManager is started
            NetworkManager.Singleton.OnClientConnectedCallback += (id) => UpdatePlayerCount();
            NetworkManager.Singleton.OnClientDisconnectCallback += (id) => UpdatePlayerCount();
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
            
            UpdatePlayerCount();
        }
        else
        {
            ShowError("Failed to create host allocation.");
            btnHost.SetEnabled(true);
        }
    }


    private async void OnJoinClicked()
    {
        string code = inputCode.value?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            txtMenuStatus.text = "Enter a code first!";
            return;
        }

        txtMenuStatus.text = "Joining...";
        btnJoin.SetEnabled(false);

        await LobbyManager.Instance.InitServices();
        bool success = await LobbyManager.Instance.JoinGame(code);

        if (success)
        {
            txtCode.text = $"JOIN CODE: {code}";
            btnStart.style.display = DisplayStyle.None;
            ShowPanel(panelLobby);
            
            // Register callbacks now that NetworkManager is started
            NetworkManager.Singleton.OnClientConnectedCallback += (id) => UpdatePlayerCount();
            NetworkManager.Singleton.OnClientDisconnectCallback += (id) => UpdatePlayerCount();
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
            
            UpdatePlayerCount();
        }
        else
        {
            ShowError("Failed to join game. Check code.");
            btnJoin.SetEnabled(true);
        }
    }


    private void OnCopyClicked()
    {
        string code = txtCode.text.Replace("JOIN CODE: ", "");
        GUIUtility.systemCopyBuffer = code;
        txtLobbyStatus.text = "Code copied to clipboard!";
    }

    private void OnStartClicked()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            StartCoroutine(FakeLoadingAndStart());
        }
    }

    private IEnumerator FakeLoadingAndStart()
    {
        // Simple way to trigger loading screen on server and potentially clients via RPC or SceneEvent
        // For simplicity in this script, we'll just wait here then trigger scene load.
        // OnSceneEvent handles showing the panel for everyone when the load starts.
        
        yield return new WaitForSeconds(0.5f); // Minor delay before scene load starts
        LobbyManager.Instance.StartGame();
    }

    private void OnLeaveClicked()
    {
        LobbyManager.Instance.LeaveGame();
        ShowPanel(panelMenu);
        btnHost.SetEnabled(true);
        btnJoin.SetEnabled(true);
        txtMenuStatus.text = "";
    }

    private void UpdatePlayerCount()
    {
        int count = NetworkManager.Singleton.ConnectedClientsIds.Count;
        txtPlayers.text = $"Players: {count} / 4";
        
        if (count >= 4)
            txtLobbyStatus.text = "Lobby is full! Ready to start.";
        else
            txtLobbyStatus.text = "Waiting for more players...";
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType == SceneEventType.Load)
        {
            ShowPanel(panelLoading);
            StartCoroutine(AnimateLoadingBar());
        }
    }

    private IEnumerator AnimateLoadingBar()
    {
        float t = 0;
        while (t < 1)
        {
            t += Time.deltaTime * 0.5f; // 2 seconds to "load"
            loadingBar.style.width = Length.Percent(t * 100);
            yield return null;
        }
    }

    private void ShowPanel(VisualElement panelToShow)
    {
        panelMenu.style.display = DisplayStyle.None;
        panelLobby.style.display = DisplayStyle.None;
        panelLoading.style.display = DisplayStyle.None;
        panelError.style.display = DisplayStyle.None;

        panelToShow.style.display = DisplayStyle.Flex;
    }

    private void ShowError(string message)
    {
        txtError.text = message;
        ShowPanel(panelError);
    }
}
