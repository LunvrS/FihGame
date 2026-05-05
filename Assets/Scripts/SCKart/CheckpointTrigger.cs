using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Place one finish line trigger on your track.
/// Tag it "FinishLine" and make its collider a trigger.
///
/// For proper lap validation (preventing shortcutting) add
/// intermediate CheckpointTriggers tagged "Checkpoint" around the track.
/// </summary>
public class CheckpointTrigger : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool isFinishLine = false;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var kart = other.GetComponentInParent<KartController>();
        if (kart == null) return;
        if (!kart.IsServer) return;

        if (isFinishLine)
        {
            // Tell RaceManager this kart crossed the line
            RaceManager.Instance?.OnKartCrossedFinishLine(kart);
        }
        else
        {
            // Register checkpoint for shortcut prevention
            RaceManager.Instance?.OnKartCrossedCheckpoint(kart, gameObject.name);
        }
    }
}
