using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Banana item — sits on track, spins out any kart that drives over it.
/// Spawned by ItemManager.SpawnBanana() behind the kart.
/// Grey box placeholder.
/// </summary>
public class BananaItem : NetworkBehaviour
{
    [SerializeField] private float spinSpeed = 60f;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var kart = other.GetComponentInParent<KartController>();
        if (kart == null) return;

        kart.SpinOutClientRpc();
        NetworkObject.Despawn();
    }
}
