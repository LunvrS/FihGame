using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Place this on a GameObject in the GameScene.
/// On game start, the server spawns:
///   - One Fish prefab per Fish-role client (owned by that client)
///   - One Fisher prefab for the host
///
/// Requires:
///   - Fish prefab registered in NetworkManager prefab list
///   - Fisher prefab registered in NetworkManager prefab list
///   - Spawn points tagged "FishSpawn" in the scene
/// </summary>
public class GameSpawner : NetworkBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject fishPrefab;
    [SerializeField] private GameObject fisherPrefab;

    [Header("Spawn Points")]
    [SerializeField] private Transform fisherSpawnPoint;
    [SerializeField] private Transform[] fishSpawnPoints;   // one per fish player slot

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnPlayersWhenReady());
    }

    /// <summary>
    /// Wait one frame so all clients are fully connected before spawning.
    /// </summary>
    private IEnumerator SpawnPlayersWhenReady()
    {
        yield return new WaitForSeconds(0.5f);
        SpawnAllPlayers();
    }

    private void SpawnAllPlayers()
    {
        int fishSpawnIndex = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;

            // Host (clientId == 0) is always the Fisher
            if (clientId == NetworkManager.Singleton.LocalClientId && IsHost)
            {
                SpawnFisher(clientId);
            }
            else
            {
                // All other clients are Fish
                Transform spawnPt = fishSpawnIndex < fishSpawnPoints.Length
                    ? fishSpawnPoints[fishSpawnIndex]
                    : transform;   // fallback to spawner position

                SpawnFish(clientId, spawnPt.position);
                fishSpawnIndex++;
            }
        }
    }

    private void SpawnFish(ulong ownerClientId, Vector3 position)
    {
        if (fishPrefab == null)
        {
            Debug.LogError("[GameSpawner] fishPrefab is not assigned!");
            return;
        }

        var obj = Instantiate(fishPrefab, position, Quaternion.identity);
        var no = obj.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError("[GameSpawner] Fish prefab is missing a NetworkObject component!");
            Destroy(obj);
            return;
        }

        // Spawn with ownership assigned to the fish client
        no.SpawnWithOwnership(ownerClientId);
        Debug.Log($"[GameSpawner] Spawned Fish for client {ownerClientId} at {position}");
    }

    private void SpawnFisher(ulong ownerClientId)
    {
        if (fisherPrefab == null)
        {
            Debug.LogWarning("[GameSpawner] fisherPrefab not assigned — skipping fisher spawn.");
            return;
        }

        Vector3 pos = fisherSpawnPoint != null ? fisherSpawnPoint.position : Vector3.zero;
        var obj = Instantiate(fisherPrefab, pos, Quaternion.identity);
        var no = obj.GetComponent<NetworkObject>();
        if (no != null)
            no.SpawnWithOwnership(ownerClientId);

        Debug.Log($"[GameSpawner] Spawned Fisher for client {ownerClientId}");
    }
}