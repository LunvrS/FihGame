using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [SerializeField] private string gameSceneName = "GameScene";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<bool> InitServices()
    {
        try
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return true;
            
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Service Init Failed: {e.Message}");
            return false;
        }
    }

    public async Task<string> HostGame()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(3); // 3 guests + 1 host
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, "dtls"));
            
            if (NetworkManager.Singleton.StartHost())
            {
                return joinCode;
            }
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"Host Failed: {e.Message}");
            return null;
        }
    }

    public async Task<bool> JoinGame(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));
            
            return NetworkManager.Singleton.StartClient();
        }
        catch (Exception e)
        {
            Debug.LogError($"Join Failed: {e.Message}");
            return false;
        }
    }

    public void StartGame()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    public void LeaveGame()
    {
        NetworkManager.Singleton.Shutdown();
    }
}
