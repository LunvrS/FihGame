using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns one kart per connected player and starts the race.
/// All players get the same kart prefab (differentiated by color or model later).
/// Place on a NetworkObject in GameScene.
/// </summary>
public class GameSpawner : NetworkBehaviour
{
    [Header("Kart Prefab — register in NetworkManager prefab list")]
    [SerializeField] private GameObject kartPrefab;

    [Header("Spawn Points — one per player slot")]
    [SerializeField] private Transform[] spawnPoints;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnAfterDelay());
    }

    private IEnumerator SpawnAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);
        SpawnAllKarts();
    }

    private void SpawnAllKarts()
    {
        var karts   = new List<KartController>();
        int index   = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            Vector3 spawnPos = (spawnPoints != null && index < spawnPoints.Length)
                ? spawnPoints[index].position
                : new Vector3(index * 3f, 0.5f, 0f);

            Quaternion spawnRot = (spawnPoints != null && index < spawnPoints.Length)
                ? spawnPoints[index].rotation
                : Quaternion.identity;

            var kartObj = Instantiate(kartPrefab, spawnPos, spawnRot);
            var no      = kartObj.GetComponent<NetworkObject>();
            if (no != null) no.SpawnWithOwnership(client.ClientId);

            var kart = kartObj.GetComponent<KartController>();
            if (kart != null) karts.Add(kart);

            // Assign camera on owning client
            AssignCameraClientRpc(no.NetworkObjectId, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { client.ClientId }
                }
            });

            Debug.Log($"[GameSpawner] Spawned kart for client {client.ClientId} at {spawnPos}");
            index++;
        }

        // Start race once all karts are ready
        StartCoroutine(StartRaceNextFrame(karts));
    }

    [ClientRpc]
    private void AssignCameraClientRpc(ulong kartNetworkObjectId, ClientRpcParams rpcParams = default)
    {
        // Find the kart NetworkObject and assign camera to it
        StartCoroutine(FindAndAssignCamera(kartNetworkObjectId));
    }

    private IEnumerator FindAndAssignCamera(ulong networkObjectId)
    {
        // Wait for kart to be spawned on this client
        NetworkObject kartNetObj = null;
        float timeout = 3f;
        while (kartNetObj == null && timeout > 0f)
        {
            NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(networkObjectId, out kartNetObj);
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (kartNetObj == null) { Debug.LogError("[GameSpawner] Could not find kart NetworkObject!"); yield break; }

        // Find or create camera
        var cam = FindFirstObjectByType<KartCamera>();
        if (cam == null)
        {
            var camGo = new GameObject("KartCamera");
            cam = camGo.AddComponent<Camera>() != null
                ? camGo.AddComponent<KartCamera>()
                : camGo.GetComponent<KartCamera>();
        }

        cam.SetTarget(kartNetObj.transform);
    }

    private IEnumerator StartRaceNextFrame(List<KartController> karts)
    {
        yield return new WaitForSeconds(0.5f);
        RaceManager.Instance?.RegisterKartsAndStart(karts);
    }
}
