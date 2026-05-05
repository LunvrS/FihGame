using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Item box placed on the track.
/// Spins visually, gives a random item when a kart drives through it,
/// then disappears and respawns after a delay.
///
/// Place as a NetworkObject in the scene (not a spawned prefab).
/// Grey box placeholder until you replace with your Blender model.
/// </summary>
public class ItemBox : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float respawnDelay  = 5f;
    [SerializeField] private float spinSpeed     = 90f;   // degrees per second
    [SerializeField] private float bobHeight     = 0.3f;
    [SerializeField] private float bobSpeed      = 2f;

    [Header("Item Weights (higher = more common)")]
    [SerializeField] private int weightBoost    = 40;
    [SerializeField] private int weightShell    = 35;
    [SerializeField] private int weightBanana   = 25;

    // ── Network State ─────────────────────────────────────────────────────────
    public NetworkVariable<bool> IsActive = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Vector3      _startPos;
    private MeshRenderer _renderer;
    private Collider     _collider;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity / NGO Lifecycle

    private void Awake()
    {
        _startPos  = transform.position;
        _renderer  = GetComponent<MeshRenderer>();
        _collider  = GetComponent<Collider>();
        if (_collider != null) _collider.isTrigger = true;
    }

    public override void OnNetworkSpawn()
    {
        IsActive.OnValueChanged += OnActiveChanged;
        UpdateVisuals(IsActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        IsActive.OnValueChanged -= OnActiveChanged;
    }

    private void Update()
    {
        if (!IsActive.Value) return;

        // Spin
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);

        // Bob up and down
        float newY = _startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(_startPos.x, newY, _startPos.z);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Trigger

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer || !IsActive.Value) return;

        var kart = other.GetComponentInParent<KartController>();
        if (kart == null) return;

        var itemManager = kart.GetComponent<ItemManager>();
        if (itemManager == null) return;
        if (itemManager.HeldItem.Value != ItemType.None) return; // already has item

        // Give random item
        ItemType item = RollItem();
        itemManager.GiveItem(item);

        // Deactivate and schedule respawn
        IsActive.Value = false;
        StartCoroutine(RespawnCoroutine());

        Debug.Log($"[ItemBox] Gave {item} to kart {kart.OwnerClientId}");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Item Roll

    private ItemType RollItem()
    {
        int total = weightBoost + weightShell + weightBanana;
        int roll  = Random.Range(0, total);

        if (roll < weightBoost)  return ItemType.Boost;
        if (roll < weightBoost + weightShell) return ItemType.Shell;
        return ItemType.Banana;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Respawn

    private IEnumerator RespawnCoroutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        IsActive.Value = true;
        transform.position = _startPos; // reset bob position
    }

    private void OnActiveChanged(bool prev, bool current)
    {
        UpdateVisuals(current);
    }

    private void UpdateVisuals(bool active)
    {
        if (_renderer != null) _renderer.enabled = active;
        if (_collider  != null) _collider.enabled  = active;
    }

    #endregion
}
