using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to each kart. Holds one item at a time.
/// Receives item from ItemBox, fires with Q or L-Shift.
/// </summary>
public class ItemManager : NetworkBehaviour
{
    public NetworkVariable<ItemType> HeldItem = new NetworkVariable<ItemType>(ItemType.None,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Spawn Points")]
    [SerializeField] private Transform frontSpawnPoint;  // for shell (fired forward)
    [SerializeField] private Transform rearSpawnPoint;   // for banana (dropped behind)

    [Header("Item Prefabs — assign grey box placeholders")]
    [SerializeField] private GameObject shellPrefab;
    [SerializeField] private GameObject bananaPrefab;

    // ── Give Item (called by ItemBox on server) ───────────────────────────────
    public void GiveItem(ItemType type)
    {
        if (!IsServer) return;
        if (HeldItem.Value != ItemType.None) return; // already holding one
        HeldItem.Value = type;
    }

    // ── Use Item (called by KartController input) ─────────────────────────────
    public void UseItem()
    {
        if (HeldItem.Value == ItemType.None) return;
        UseItemServerRpc();
    }

    [ServerRpc]
    private void UseItemServerRpc()
    {
        if (HeldItem.Value == ItemType.None) return;

        switch (HeldItem.Value)
        {
            case ItemType.Boost:
                GetComponent<KartController>()?.ApplyBoost(1.5f, 35f);
                break;

            case ItemType.Shell:
                SpawnShell();
                break;

            case ItemType.Banana:
                SpawnBanana();
                break;
        }

        HeldItem.Value = ItemType.None;
    }

    private void SpawnShell()
    {
        if (shellPrefab == null) { Debug.LogWarning("[ItemManager] Shell prefab not assigned!"); return; }

        Vector3 pos   = frontSpawnPoint != null ? frontSpawnPoint.position : transform.position + transform.forward * 1.5f;
        var obj       = Instantiate(shellPrefab, pos, transform.rotation);
        var no        = obj.GetComponent<NetworkObject>();
        if (no != null) no.Spawn();

        var shell = obj.GetComponent<ShellItem>();
        if (shell != null) shell.Launch(transform.forward, OwnerClientId);
    }

    private void SpawnBanana()
    {
        if (bananaPrefab == null) { Debug.LogWarning("[ItemManager] Banana prefab not assigned!"); return; }

        Vector3 pos = rearSpawnPoint != null ? rearSpawnPoint.position : transform.position - transform.forward * 1.5f;
        var obj     = Instantiate(bananaPrefab, pos, Quaternion.identity);
        var no      = obj.GetComponent<NetworkObject>();
        if (no != null) no.Spawn();
    }
}

public enum ItemType { None, Boost, Shell, Banana }
