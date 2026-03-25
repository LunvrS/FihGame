using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked fish player controller.
/// - Top-down 3D world, fixed side-view camera (X/Y movement, Z locked)
/// - Water drag physics
/// - 3 lives, 2 growth stages (small → large)
/// - Eats 3 bait total to reach max stage (1 bait = stage 1, 2+3 bait = stage 2)
/// - On caught: flash red, respawn at spawn point
/// - Only the owning client reads input; state synced via NetworkVariables
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FishController : NetworkBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Movement")]
    [SerializeField] private float moveForce      = 12f;   // force applied per frame
    [SerializeField] private float maxSpeed       = 6f;    // terminal velocity
    [SerializeField] private float waterDrag      = 3.5f;  // linear drag (water feel)
    [SerializeField] private float waterAngularDrag = 10f;

    [Header("Lives & Growth")]
    [SerializeField] private int   maxLives       = 3;
    [SerializeField] private int   baitToGrow     = 1;     // bait eaten to reach stage 2
    [SerializeField] private int   baitToWin      = 3;     // total bait to win

    [Header("Growth Visuals")]
    [SerializeField] private Vector3 smallScale   = new Vector3(0.5f, 0.5f, 0.5f);
    [SerializeField] private Vector3 largeScale   = new Vector3(1.0f, 1.0f, 1.0f);
    [SerializeField] private Sprite[] stageSprites;         // [0]=small, [1]=large (optional)
    [SerializeField] private FishGrowthEffect growthEffect; // particle effect component

    [Header("Respawn")]
    [SerializeField] private Transform spawnPoint;          // assign in Inspector or auto-find
    [SerializeField] private float     flashDuration  = 1.2f;
    [SerializeField] private float     flashInterval  = 0.1f;
    [SerializeField] private Color     flashColor     = Color.red;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    // ── Network State ─────────────────────────────────────────────────────────
    public NetworkVariable<int>  Lives       = new NetworkVariable<int>(3,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int>  BaitEaten   = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int>  GrowthStage = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsCaught    = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> IsAlive     = new NetworkVariable<bool>(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Private ───────────────────────────────────────────────────────────────
    private Rigidbody  _rb;
    private Vector2    _inputDir;
    private bool       _isRespawning;
    private Color      _originalColor;

    // ─────────────────────────────────────────────────────────────────────────
    #region Unity / NGO Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // Lock Z axis — fish only moves on X/Y (side-scroller in 3D space)
        _rb.constraints = RigidbodyConstraints.FreezePositionZ
                        | RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationY
                        | RigidbodyConstraints.FreezeRotationZ;

        _rb.linearDamping        = waterDrag;
        _rb.angularDamping = waterAngularDrag;
        _rb.useGravity    = false;   // underwater — no gravity

        if (spriteRenderer != null)
            _originalColor = spriteRenderer.color;
    }

    public override void OnNetworkSpawn()
    {
        // Initialize lives on server
        if (IsServer)
        {
            Lives.Value       = maxLives;
            BaitEaten.Value   = 0;
            GrowthStage.Value = 0;
            IsCaught.Value    = false;
            IsAlive.Value     = true;
        }

        // Subscribe to network variable changes for visuals (all clients)
        GrowthStage.OnValueChanged += OnGrowthStageChanged;
        Lives.OnValueChanged       += OnLivesChanged;
        IsCaught.OnValueChanged    += OnCaughtChanged;

        // Apply initial visual scale
        ApplyGrowthVisuals(GrowthStage.Value);

        // Auto-find spawn point if not assigned
        if (spawnPoint == null)
        {
            var sp = GameObject.FindGameObjectWithTag("FishSpawn");
            if (sp != null) spawnPoint = sp.transform;
        }

        // Move to spawn on start
        if (spawnPoint != null)
            transform.position = spawnPoint.position;

        // Assign this fish to its own camera (owner client only)
        if (IsOwner)
        {
            var fishCam = FindFirstObjectByType<FishCamera>();
            if (fishCam != null)
                fishCam.AssignTarget(transform);
            else
                Debug.LogWarning("[FishController] No FishCamera found in scene. Add one per fish player.");
        }
    }

    public override void OnNetworkDespawn()
    {
        GrowthStage.OnValueChanged -= OnGrowthStageChanged;
        Lives.OnValueChanged       -= OnLivesChanged;
        IsCaught.OnValueChanged    -= OnCaughtChanged;
    }

    private void Update()
    {
        // Only the owning client reads input
        if (!IsOwner || _isRespawning) return;

        float h = Input.GetAxisRaw("Horizontal");   // A/D or ←/→
        float v = Input.GetAxisRaw("Vertical");     // W/S or ↑/↓
        _inputDir = new Vector2(h, v).normalized;

        // Flip sprite to face movement direction
        if (spriteRenderer != null && h != 0)
            spriteRenderer.flipX = h < 0;
    }

    private void FixedUpdate()
    {
        if (!IsOwner || _isRespawning) return;
        ApplyWaterMovement();
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Movement

    private void ApplyWaterMovement()
    {
        // Clamp speed before adding force (water drag feel)
        Vector3 force = new Vector3(_inputDir.x, _inputDir.y, 0f) * moveForce;
        _rb.AddForce(force, ForceMode.Force);

        // Cap velocity
        Vector3 vel = _rb.linearVelocity;
        if (vel.magnitude > maxSpeed)
            _rb.linearVelocity = vel.normalized * maxSpeed;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Bait Eating (called by BaitObject when fish eats bait)

    /// <summary>Called by BaitObject on the server when this fish fully eats a bait.</summary>
    public void OnBaitEaten()
    {
        if (!IsServer) return;
        if (!IsAlive.Value || IsCaught.Value) return;

        BaitEaten.Value++;

        // Check growth
        if (BaitEaten.Value == baitToGrow && GrowthStage.Value == 0)
            GrowthStage.Value = 1;

        // Check win condition
        if (BaitEaten.Value >= baitToWin)
        {
            NetworkGameManager.Instance?.NotifyFishWonServerRpc();
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Caught by Fisher (called by BaitObject on server)

    /// <summary>Server: fisher pulled the line while this fish was on the bait.</summary>
    public void OnCaughtByFisher()
    {
        if (!IsServer) return;
        if (!IsAlive.Value || IsCaught.Value) return;

        IsCaught.Value = true;
        Lives.Value--;

        if (Lives.Value <= 0)
        {
            IsAlive.Value = false;
            NetworkGameManager.Instance?.NotifyFisherWonServerRpc();
        }
        else
        {
            // Tell owning client to do respawn sequence
            RespawnClientRpc();
        }
    }

    [ClientRpc]
    private void RespawnClientRpc()
    {
        if (!IsOwner) return;
        StartCoroutine(RespawnSequence());
    }

    private IEnumerator RespawnSequence()
    {
        _isRespawning = true;
        _rb.linearVelocity = Vector3.zero;

        // Teleport to spawn
        if (spawnPoint != null)
            transform.position = spawnPoint.position;

        // Flash red
        yield return StartCoroutine(FlashRed());

        _isRespawning = false;

        // Tell server we're done respawning
        FinishRespawnServerRpc();
    }

    [ServerRpc]
    private void FinishRespawnServerRpc()
    {
        IsCaught.Value = false;
    }

    private IEnumerator FlashRed()
    {
        float elapsed = 0f;
        bool  toggle  = false;

        while (elapsed < flashDuration)
        {
            if (spriteRenderer != null)
                spriteRenderer.color = toggle ? flashColor : _originalColor;

            toggle   = !toggle;
            elapsed += flashInterval;
            yield return new WaitForSeconds(flashInterval);
        }

        if (spriteRenderer != null)
            spriteRenderer.color = _originalColor;
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Network Variable Callbacks (visuals — run on all clients)

    private void OnGrowthStageChanged(int prev, int next)
    {
        ApplyGrowthVisuals(next);

        // Play bubble effect on growth
        if (next > prev && growthEffect != null)
            growthEffect.PlayGrowEffect();
    }

    private void OnLivesChanged(int prev, int next)
    {
        // HUD updates handled by GameUI listening to this fish's Lives variable
        Debug.Log($"[Fish] Lives: {next}");
    }

    private void OnCaughtChanged(bool prev, bool next)
    {
        // Could add visual feedback here (e.g., stun particles)
    }

    private void ApplyGrowthVisuals(int stage)
    {
        // Scale
        transform.localScale = stage == 0 ? smallScale : largeScale;

        // Sprite swap (optional)
        if (spriteRenderer != null && stageSprites != null && stage < stageSprites.Length)
            spriteRenderer.sprite = stageSprites[stage];
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Public Getters

    public bool IsMaxGrowth => GrowthStage.Value >= 1;
    public bool HasWon      => BaitEaten.Value >= baitToWin;

    #endregion
}
