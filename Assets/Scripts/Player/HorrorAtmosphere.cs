using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// MouEscape Horror Atmosphere System  v2
/// ──────────────────────────────────────
/// Drop this on any persistent GameObject (e.g. the Player or a Manager empty).
/// Wire up the two lights in the Inspector — enemies are found automatically
/// via Enemy.AllEnemies (self-registering registry) and new ones are spawned
/// through DungeonMaster.SpawnExtraEnemy().
///
/// Difficulty curve:
///   • Player spotlight shrinks from large → tiny
///   • Global light dims toward near-black
///   • Random flicker events — brief glimpses of the full maze, then darkness
///   • New enemies spawn at rising intervals, each one SLOWER than the last
///     (pack-swarm horror: quantity is the threat, not speed)
///   • Camera micro-shakes increase in frequency with fear
/// </summary>
public class HorrorAtmosphere : MonoBehaviour
{
    public static HorrorAtmosphere Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Session")]
    [Tooltip("Seconds until maximum horror. Game stays at max after this.")]
    public float sessionDuration = 120f;

    [Header("Player Spotlight")]
    [Tooltip("The Light2D on the player (spot or point type).")]
    public Light2D playerLight;
    public float   lightRadiusStart = 8f;
    public float   lightRadiusEnd   = 2.5f;
    [Range(0f, 1f)] public float innerRatio = 0.4f;

    [Header("Flicker")]
    public float flickerIntervalStart  = 5f;
    public float flickerIntervalAtMax  = 0.5f;
    public float flickerDuration       = 0.07f;
    [Range(1, 8)] public int flickerCount = 3;
    [Tooltip("How dim the light gets during a flicker. 0.3 keeps it from going totally black.")]
    [Range(0f, 1f)] public float flickerIntensityMin = 0.3f;

    [Header("Global Ambient Light")]
    [Tooltip("The scene's Global Light 2D. If left null we auto-find it.")]
    public Light2D globalLight;
    [Range(0f, 1f)] public float globalIntensityStart = 0.6f;
    [Range(0f, 1f)] public float globalIntensityEnd   = 0.05f;

    [Header("Enemy Spawning (automatic — no wiring needed)")]
    [Tooltip("How many seconds between each new enemy spawn at the start.")]
    public float spawnIntervalStart = 30f;
    [Tooltip("How many seconds between each new enemy spawn at maximum fear.")]
    public float spawnIntervalEnd   = 15f;
    [Tooltip("Speed of the FIRST extra enemy spawned.")]
    public float spawnSpeedFirst    = 1.8f;
    [Tooltip("Each subsequent enemy is this much SLOWER (pack is the threat, not speed).")]
    public float spawnSpeedDecrement = 0.15f;
    [Tooltip("Absolute minimum speed so no enemy is ever completely stationary.")]
    public float spawnSpeedMin      = 0.6f;
    [Tooltip("Hard cap on total live enemies.")]
    public int   maxEnemies         = 6;

    // ── Private ───────────────────────────────────────────────────────────────

    private float  _elapsed       = 0f;
    private float  _fearLevel     = 0f;
    private float  _baseIntensity = 1f;
    private int    _extraSpawned  = 0;

    // =========================================================================
    void Awake()
    {
        Instance = this;

        if (globalLight == null)
        {
            foreach (var l in FindObjectsOfType<Light2D>())
            {
                if (l.lightType == Light2D.LightType.Global) { globalLight = l; break; }
            }
        }
    }

    void Start()
    {
        if (playerLight != null)
        {
            _baseIntensity                    = playerLight.intensity;
            playerLight.pointLightOuterRadius = lightRadiusStart;
            playerLight.pointLightInnerRadius = lightRadiusStart * innerRatio;
            
            // Enable hard shadows
            playerLight.shadowsEnabled = true;
            playerLight.shadowIntensity = 1.0f;
        }

        if (globalLight != null)
            globalLight.intensity = globalIntensityStart;

        StartCoroutine(FlickerLoop());
        StartCoroutine(EnemySpawnLoop());
    }

    void Update()
    {
        // _elapsed still tracked for enemy spawn loops if needed, 
        // but core atmosphere (Fear Level) is now driven by STAGE OF HUNGER.
        _elapsed = Mathf.Min(_elapsed + Time.deltaTime, sessionDuration);

        if (GameManager.Instance != null)
        {
            // Fear is the inverse of Hunger: 100% hunger = 0% fear, 0% hunger = 100% fear.
            _fearLevel = 1.0f - GameManager.Instance.HungerRatio;
        }
        else
        {
            // Fallback for testing without GameSystem
            _fearLevel = _elapsed / sessionDuration;
        }

        ApplyLightShrink();
        ApplyGlobalDim();
    }

    // =========================================================================
    // LIGHT UPDATES
    // =========================================================================

    void ApplyLightShrink()
    {
        if (playerLight == null) return;
        float t      = _fearLevel * _fearLevel;   // quadratic: stays open, then collapses fast
        float radius = Mathf.Lerp(lightRadiusStart, lightRadiusEnd, t);
        playerLight.pointLightOuterRadius = radius;
        playerLight.pointLightInnerRadius = radius * innerRatio;
    }

    void ApplyGlobalDim()
    {
        if (globalLight == null) return;
        float t = _fearLevel * _fearLevel;
        globalLight.intensity = Mathf.Lerp(globalIntensityStart, globalIntensityEnd, t);
    }

    // =========================================================================
    // FLICKER
    // =========================================================================

    private IEnumerator FlickerLoop()
    {
        while (true)
        {
            // Interval is now strictly tied to hunger/fear level
            float interval = Mathf.Lerp(flickerIntervalStart, flickerIntervalAtMax, _fearLevel);
            // Reduced random jitter so it feels more like a direct reflection of state
            interval += Random.Range(-interval * 0.05f, interval * 0.05f);
            interval  = Mathf.Max(interval, 0.1f);
            yield return new WaitForSeconds(interval);

            int count = Mathf.RoundToInt(Mathf.Lerp(1, flickerCount, _fearLevel));
            for (int i = 0; i < count; i++)
            {
                if (playerLight != null)
                {
                    playerLight.intensity = Random.Range(flickerIntensityMin, _baseIntensity * 0.4f);
                    // Occasionally open the light for a terrifying full-maze glimpse
                    if (Random.value > 0.55f)
                        playerLight.pointLightOuterRadius = lightRadiusStart * 1.3f;
                }
                if (globalLight != null)
                    globalLight.intensity = Random.Range(0f, globalLight.intensity * 0.6f);

                yield return new WaitForSeconds(flickerDuration);

                // Restore to current fear-scaled values
                if (playerLight != null)
                {
                    playerLight.intensity = _baseIntensity;
                    float t      = _fearLevel * _fearLevel;
                    float radius = Mathf.Lerp(lightRadiusStart, lightRadiusEnd, t);
                    playerLight.pointLightOuterRadius = radius;
                    playerLight.pointLightInnerRadius = radius * innerRatio;
                }
                if (globalLight != null)
                    globalLight.intensity = Mathf.Lerp(globalIntensityStart, globalIntensityEnd, _fearLevel * _fearLevel);

                yield return new WaitForSeconds(flickerDuration * 0.4f);
            }
        }
    }

    // =========================================================================
    // ENEMY SPAWNING  –  more enemies, each one SLOWER
    // =========================================================================

    private IEnumerator EnemySpawnLoop()
    {
        // Wait for DungeonMaster to finish generating the dungeon
        yield return new WaitForSeconds(4f);

        while (true)
        {
            float interval = Mathf.Lerp(spawnIntervalStart, spawnIntervalEnd, _fearLevel);
            yield return new WaitForSeconds(interval);

            int currentEnemyCount = Enemy.AllEnemies.Count;
            if (currentEnemyCount >= maxEnemies) continue;

            DungeonMaster dm = FindObjectOfType<DungeonMaster>();
            if (dm == null) continue;

            // Each successive enemy is slightly slower — the swarm grows but stays beatable
            float speed = Mathf.Max(spawnSpeedMin, spawnSpeedFirst - (_extraSpawned * spawnSpeedDecrement));
            dm.SpawnExtraEnemy(speed);
            _extraSpawned++;

            Debug.Log($"[HorrorAtmosphere] Spawned enemy #{currentEnemyCount + 1} | speed={speed:F2} | fear={_fearLevel:P0}");

            // Brief jump-flicker to sell the appear moment
            if (_fearLevel > 0.2f)
                TriggerJumpScare();
        }
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    public float FearLevel => _fearLevel;
    public float Elapsed   => _elapsed;

    [ContextMenu("Apply 2-Min Demo Tension Preset")]
    private void ApplyTwoMinuteDemoPreset()
    {
        sessionDuration = 120f;

        lightRadiusStart = 7.5f;
        lightRadiusEnd = 1.8f;
        innerRatio = 0.35f;

        globalIntensityStart = 0.42f;
        globalIntensityEnd = 0.015f;

        flickerIntervalStart = 2.0f;
        flickerIntervalAtMax = 0.2f;
        flickerDuration = 0.08f;
        flickerCount = 4;
        flickerIntensityMin = 0.06f;

        spawnIntervalStart = 16f;
        spawnIntervalEnd = 6f;
        spawnSpeedFirst = 1.9f;
        spawnSpeedDecrement = 0.08f;
        spawnSpeedMin = 1.1f;
        maxEnemies = 9;

        Debug.Log("[HorrorAtmosphere] Applied 2-minute demo tension preset.");
    }

    /// <summary>Trigger a soft flicker — no blackout, no shake.</summary>
    public void TriggerJumpScare()
    {
        StartCoroutine(SoftFlicker());
    }

    private IEnumerator SoftFlicker()
    {
        for (int i = 0; i < 2; i++)
        {
            if (playerLight != null) playerLight.intensity = flickerIntensityMin;
            yield return new WaitForSeconds(0.05f);
            if (playerLight != null) playerLight.intensity = _baseIntensity;
            yield return new WaitForSeconds(0.04f);
        }
    }
}
