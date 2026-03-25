using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Fixed side-view camera for one fish player.
/// Attach to the Camera GameObject that is a child of (or separate from) the fish.
///
/// Setup options:
///   A) Make this Camera a child of the Fish prefab — it will follow automatically.
///   B) Keep it separate and assign the fish target via FindOwnerFish() at spawn.
///
/// The camera locks Z position and only tracks X/Y, giving a pure side-scroller view.
/// </summary>
public class FishCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;          // assigned at runtime if null

    [Header("Camera Position")]
    [SerializeField] private float zOffset     = -15f;  // how far back the camera sits
    [SerializeField] private float smoothSpeed = 8f;    // follow smoothing

    [Header("Bounds (optional)")]
    [SerializeField] private bool  useBounds   = false;
    [SerializeField] private float minX        = -20f;
    [SerializeField] private float maxX        =  20f;
    [SerializeField] private float minY        = -10f;
    [SerializeField] private float maxY        =  10f;

    private Camera _cam;
    private bool   _foundTarget;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null)
            _cam = gameObject.AddComponent<Camera>();
    }

    private void Start()
    {
        // If target was pre-assigned in Inspector, use it
        if (target != null)
        {
            _foundTarget = true;
            SnapToTarget();
            return;
        }

        // Otherwise wait for the local fish to spawn (called from FishController)
        // FishController.OnNetworkSpawn will call AssignTarget()
    }

    /// <summary>Called by FishController.OnNetworkSpawn on the owning client.</summary>
    public void AssignTarget(Transform fishTransform)
    {
        target       = fishTransform;
        _foundTarget = true;
        SnapToTarget();
    }

    private void LateUpdate()
    {
        if (!_foundTarget || target == null) return;
        FollowTarget();
    }

    private void FollowTarget()
    {
        Vector3 desired = new Vector3(target.position.x, target.position.y, zOffset);

        if (useBounds)
        {
            desired.x = Mathf.Clamp(desired.x, minX, maxX);
            desired.y = Mathf.Clamp(desired.y, minY, maxY);
        }

        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }

    private void SnapToTarget()
    {
        if (target == null) return;
        transform.position = new Vector3(target.position.x, target.position.y, zOffset);
    }
}
