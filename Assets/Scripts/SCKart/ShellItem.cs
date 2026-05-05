using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shell item — travels forward, spins out any kart it hits.
/// Spawned by ItemManager.SpawnShell().
/// Grey box placeholder.
/// </summary>
public class ShellItem : NetworkBehaviour
{
    [SerializeField] private float speed        = 25f;
    [SerializeField] private float lifetime     = 6f;
    [SerializeField] private float spinSpeed    = 360f;

    private ulong  _ownerKartClientId;
    private float  _lifeTimer;
    private bool   _launched;

    public void Launch(Vector3 direction, ulong ownerClientId)
    {
        _ownerKartClientId = ownerClientId;
        _launched          = true;
        transform.forward  = direction;
    }

    private void Update()
    {
        if (!IsServer || !_launched) return;

        // Move forward
        transform.position += transform.forward * speed * Time.deltaTime;
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f);

        _lifeTimer += Time.deltaTime;
        if (_lifeTimer >= lifetime)
            NetworkObject.Despawn();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var kart = other.GetComponentInParent<KartController>();
        if (kart == null) return;
        if (kart.OwnerClientId == _ownerKartClientId) return; // don't hit own kart

        kart.SpinOutClientRpc();
        NetworkObject.Despawn();
    }
}
