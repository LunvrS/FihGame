using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Third-person behind-kart camera.
/// Attach to the Camera prefab or auto-created per kart owner.
/// Smoothly follows and looks at the kart with configurable offset.
/// </summary>
public class KartCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private float height        = 2.5f;
    [SerializeField] private float distance      = 5f;
    [SerializeField] private float followSpeed   = 8f;
    [SerializeField] private float rotateSpeed   = 6f;

    [Header("Look")]
    [SerializeField] private Vector3 lookOffset  = new Vector3(0f, 0.5f, 0f);

    private Vector3    _currentVelocity;
    private Quaternion _currentRotation;

    public void SetTarget(Transform t)
    {
        target           = t;
        // Snap immediately on first assignment
        if (target != null) SnapToTarget();
    }

    private void LateUpdate()
    {
        if (target == null) return;
        FollowKart();
    }

    private void FollowKart()
    {
        // Desired position: behind and above the kart
        Vector3 desiredPos = target.position
                           - target.forward * distance
                           + Vector3.up * height;

        // Smooth follow
        transform.position = Vector3.SmoothDamp(
            transform.position, desiredPos,
            ref _currentVelocity, 1f / followSpeed);

        // Smooth look at kart
        Vector3 lookTarget    = target.position + lookOffset;
        Quaternion desiredRot = Quaternion.LookRotation(lookTarget - transform.position);
        transform.rotation    = Quaternion.Slerp(transform.rotation, desiredRot,
                                    rotateSpeed * Time.deltaTime);
    }

    private void SnapToTarget()
    {
        transform.position = target.position
                           - target.forward * distance
                           + Vector3.up * height;
        transform.LookAt(target.position + lookOffset);
    }
}
