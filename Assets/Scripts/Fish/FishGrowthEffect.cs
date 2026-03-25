using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a bubble burst effect when the fish grows to the next stage.
/// Attach to the Fish prefab alongside FishController.
///
/// Uses a ParticleSystem. If you have a bubble PNG:
///   1. Import it as a Sprite (Texture Type: Sprite)
///   2. In the ParticleSystem → Renderer → Material, use a Sprites/Default material
///   3. Assign your bubble PNG as the sprite on that material
///
/// The effect plays once on grow and stops automatically.
/// </summary>
public class FishGrowthEffect : MonoBehaviour
{
    [Header("Particle System")]
    [SerializeField] private ParticleSystem bubbleParticles;

    [Header("Effect Settings")]
    [SerializeField] private float effectDuration    = 1.0f;
    [SerializeField] private int   burstCount        = 20;
    [SerializeField] private float minSize           = 0.1f;
    [SerializeField] private float maxSize           = 0.4f;
    [SerializeField] private float speed             = 2.5f;
    [SerializeField] private Color bubbleColor       = new Color(0.5f, 0.85f, 1f, 0.8f);

    private void Awake()
    {
        // Auto-create a ParticleSystem if none assigned
        if (bubbleParticles == null)
            bubbleParticles = GetComponent<ParticleSystem>();

        if (bubbleParticles == null)
            bubbleParticles = CreateDefaultBubbleSystem();

        // Don't play on awake
        var main = bubbleParticles.main;
        main.playOnAwake = false;
    }

    /// <summary>Call this when the fish grows to the next stage.</summary>
    public void PlayGrowEffect()
    {
        if (bubbleParticles == null) return;
        StartCoroutine(BurstEffect());
    }

    private IEnumerator BurstEffect()
    {
        // Emit a burst of bubbles
        bubbleParticles.Emit(burstCount);

        yield return new WaitForSeconds(effectDuration);

        // Particles fade out on their own via lifetime settings
    }

    /// <summary>
    /// Builds a default bubble ParticleSystem in code.
    /// Replace with your own configured ParticleSystem in the Inspector for better results.
    /// </summary>
    private ParticleSystem CreateDefaultBubbleSystem()
    {
        var go = new GameObject("BubbleParticles");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        var ps = go.AddComponent<ParticleSystem>();

        // Main module
        var main          = ps.main;
        main.loop         = false;
        main.playOnAwake  = false;
        main.duration     = effectDuration;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
        main.startSpeed    = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize     = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor    = bubbleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Emission — burst only
        var emission = ps.emission;
        emission.enabled    = false;   // we call Emit() manually

        // Shape — emit in a circle around the fish
        var shape       = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = 0.3f;

        // Velocity over lifetime — bubbles float upward
        var vel             = ps.velocityOverLifetime;
        vel.enabled         = true;
        vel.y               = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);

        // Size over lifetime — shrink as they rise
        var sizeOverLife    = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve curve = new AnimationCurve();
        curve.AddKey(0f, 1f);
        curve.AddKey(1f, 0f);
        sizeOverLife.size   = new ParticleSystem.MinMaxCurve(1f, curve);

        // Renderer — use default sprite material
        var renderer        = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material   = new Material(Shader.Find("Sprites/Default"));

        return ps;
    }
}
