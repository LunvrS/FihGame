using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Arcade kart controller with drift + boost mechanic.
/// Attach to each kart prefab alongside a Rigidbody.
///
/// Controls:
///   W/S or Up/Down  — accelerate / brake
///   A/D or Left/Right — steer
///   Space           — drift (hold while turning)
///   Q or L-Shift    — use item
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class KartController : NetworkBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Speed")]
    [SerializeField] private float maxSpeed         = 20f;
    [SerializeField] private float acceleration     = 15f;
    [SerializeField] private float brakeForce       = 20f;
    [SerializeField] private float reverseSpeed     = 8f;

    [Header("Steering")]
    [SerializeField] private float steerSpeed       = 120f;   // degrees per second
    [SerializeField] private float steerAtSpeedMult = 0.5f;   // reduces steer at high speed

    [Header("Drift")]
    [SerializeField] private float driftSteerMult   = 1.4f;   // extra steer while drifting
    [SerializeField] private float driftGrip        = 0.3f;   // lateral grip during drift
    [SerializeField] private float normalGrip       = 0.9f;   // lateral grip normally
    [SerializeField] private float minDriftSpeed    = 8f;     // must be going this fast to drift

    [Header("Drift Boost")]
    [SerializeField] private float boostSpeed       = 30f;
    [SerializeField] private float boostDuration    = 0.8f;
    [SerializeField] private float minDriftTime     = 0.5f;   // must drift this long for boost
    [SerializeField] private ParticleSystem boostParticles;
    [SerializeField] private ParticleSystem driftParticles;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDist  = 0.3f;
    [SerializeField] private LayerMask groundLayer;

    // ── Network State ─────────────────────────────────────────────────────────
    public NetworkVariable<int> LapCount = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> RacePosition = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsFinished = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Private ───────────────────────────────────────────────────────────────
    private Rigidbody _rb;
    private bool      _isGrounded;
    private float     _currentSpeed;
    private float     _steerInput;
    private float     _accelInput;
    private bool      _isDrifting;
    private float     _driftTimer;
    private bool      _isBoosting;
    private float     _boostTimer;
    private int       _driftDirection;   // -1 left, 1 right
    private bool      _isSpunOut;        // hit by banana/shell
    private float     _spinTimer;

    // Exposed for camera and UI
    public float CurrentSpeed    => _currentSpeed;
    public bool  IsDrifting      => _isDrifting;
    public bool  IsBoosting      => _isBoosting;
    public bool  IsSpunOut       => _isSpunOut;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity / NGO Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.centerOfMass   = new Vector3(0, -0.3f, 0); // low center of mass
        _rb.linearDamping  = 0.5f;
        _rb.angularDamping = 5f;
        _rb.interpolation  = RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Non-owners don't need the rigidbody simulating locally
            _rb.isKinematic = false; // NGO handles sync
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        GatherInput();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        CheckGround();
        if (!_isGrounded) return;

        HandleSteering();
        HandleAcceleration();
        HandleDrift();
                ApplyGrip();
        ClampSpeed();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Input

    private void GatherInput()
    {
        _accelInput = Input.GetAxis("Vertical");
        _steerInput = Input.GetAxis("Horizontal");

        // Start drift
        if (Input.GetKeyDown(KeyCode.Space) && _currentSpeed > minDriftSpeed && !_isDrifting)
            StartDrift();

        // Release drift
        if (Input.GetKeyUp(KeyCode.Space) && _isDrifting)
            EndDrift();

        // Use item
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftShift))
            GetComponent<ItemManager>()?.UseItem();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Ground Check

    private void CheckGround()
    {
        _isGrounded = Physics.Raycast(
            transform.position + Vector3.up * 0.1f,
            Vector3.down,
            groundCheckDist + 0.1f,
            groundLayer);

        // Align kart to ground normal
        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down,
            out RaycastHit hit, 1f, groundLayer))
        {
            Quaternion targetRot = Quaternion.FromToRotation(transform.up, hit.normal)
                                 * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 10f * Time.fixedDeltaTime);
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Movement

    private void HandleAcceleration()
    {
        if (_isSpunOut) return;

        float targetSpeed = 0f;

        if (_accelInput > 0)
            targetSpeed = _isBoosting ? boostSpeed : maxSpeed;
        else if (_accelInput < 0)
            targetSpeed = -reverseSpeed;

        float force = _accelInput > 0
            ? acceleration
            : (_accelInput < 0 ? brakeForce : brakeForce * 0.5f);

        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed * _accelInput, force * Time.fixedDeltaTime);

        // Apply forward force
        _rb.AddForce(transform.forward * _currentSpeed, ForceMode.VelocityChange);
    }

    private void HandleSteering()
    {
        if (_isSpunOut || Mathf.Abs(_currentSpeed) < 0.5f) return;

        float speedFactor  = Mathf.Lerp(1f, steerAtSpeedMult, _currentSpeed / maxSpeed);
        float driftFactor  = _isDrifting ? driftSteerMult : 1f;
        float steerAmount  = _steerInput * steerSpeed * speedFactor * driftFactor * Time.fixedDeltaTime;

        // Flip steering direction in reverse
        if (_currentSpeed < 0) steerAmount = -steerAmount;

        transform.Rotate(0f, steerAmount, 0f);
    }

    private void ApplyGrip()
    {
        // Cancel out sideways velocity (simulates tire grip)
        float grip = _isDrifting ? driftGrip : normalGrip;

        Vector3 localVel   = transform.InverseTransformDirection(_rb.linearVelocity);
        localVel.x        *= (1f - grip);
        _rb.linearVelocity = transform.TransformDirection(localVel);
    }

    private void ClampSpeed()
    {
        float cap = _isBoosting ? boostSpeed : maxSpeed;
        if (_rb.linearVelocity.magnitude > cap)
            _rb.linearVelocity = _rb.linearVelocity.normalized * cap;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Drift

    private void StartDrift()
    {
        _isDrifting    = true;
        _driftTimer    = 0f;
        _driftDirection = _steerInput >= 0 ? 1 : -1;

        if (driftParticles != null) driftParticles.Play();
    }

    private void HandleDrift()
    {
        if (!_isDrifting) return;
        _driftTimer += Time.fixedDeltaTime;
    }

    private void EndDrift()
    {
        _isDrifting = false;
        if (driftParticles != null) driftParticles.Stop();

        // Grant boost if drifted long enough
        if (_driftTimer >= minDriftTime)
            StartCoroutine(BoostCoroutine());
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Boost

    private IEnumerator BoostCoroutine()
    {
        _isBoosting = true;
        _boostTimer = boostDuration;
        if (boostParticles != null) boostParticles.Play();

        while (_boostTimer > 0f)
        {
            _boostTimer -= Time.deltaTime;
            yield return null;
        }

        _isBoosting = false;
        if (boostParticles != null) boostParticles.Stop();
    }

    /// <summary>Called by BoostItem to apply an item boost.</summary>
    public void ApplyBoost(float duration, float speed)
    {
        StartCoroutine(ExternalBoostCoroutine(duration, speed));
    }

    private IEnumerator ExternalBoostCoroutine(float duration, float speed)
    {
        float prev = boostSpeed;
        boostSpeed  = speed;
        _isBoosting = true;
        if (boostParticles != null) boostParticles.Play();

        yield return new WaitForSeconds(duration);

        _isBoosting = false;
        boostSpeed  = prev;
        if (boostParticles != null) boostParticles.Stop();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Spin Out (hit by item)

    /// <summary>Server calls this when kart is hit by a shell or banana.</summary>
    public void SpinOut()
    {
        if (!IsOwner) return;
        StartCoroutine(SpinOutCoroutine());
    }

    [ClientRpc]
    public void SpinOutClientRpc()
    {
        if (!IsOwner) return;
        StartCoroutine(SpinOutCoroutine());
    }

    private IEnumerator SpinOutCoroutine()
    {
        _isSpunOut = true;
        _currentSpeed = 0f;
        _rb.linearVelocity = Vector3.zero;

        float t = 0f;
        while (t < 1.5f)
        {
            transform.Rotate(0f, 400f * Time.deltaTime, 0f);
            t += Time.deltaTime;
            yield return null;
        }

        _isSpunOut = false;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Lap / Checkpoint (called by CheckpointTrigger)

    public void CompleteLap()
    {
        if (!IsServer) return;
        LapCount.Value++;
        Debug.Log($"[Kart] Client {OwnerClientId} completed lap {LapCount.Value}");

        if (LapCount.Value >= RaceManager.Instance?.TotalLaps)
        {
            IsFinished.Value = true;
            RaceManager.Instance?.OnKartFinished(this);
        }
    }

    #endregion
}
