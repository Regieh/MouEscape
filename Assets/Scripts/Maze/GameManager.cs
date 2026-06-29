using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// ╔══════════════════════════════════════════════════════╗
/// ║           MOUSEESCAPE  —  GAME MANAGER                ║
/// ║       Master Logic, Balancing & Gameplay Hub          ║
/// ╚══════════════════════════════════════════════════════╝
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState { AwaitingStartTap, Countdown, Playing, Won, GameOver }
    [Header("── Game State ───────────────────────────────")]
    public GameState state = GameState.Playing;
    public bool requireTapToStart = true;
    public bool enableStartCountdown = true;
    public float startCountdownDuration = 3f;

    [Header("── Start UI (Predefined) ───────────────")]
    public GameObject tapToStartUIRoot;
    public TMP_Text tapToStartText;
    public string tapToStartMessage = "TAP TO START";

    [Header("── Countdown UI (Predefined) ───────────────")]
    public GameObject countdownUIRoot;
    public TMP_Text countdownText;
    [Header("── Countdown Sounds ───────────────────────────────")]
    public string countdownBeep1SoundID = "Beep1";
    public string countdownBeep2SoundID = "Beep2";
    // =========================================================================
    // ── HUNGER ───────────────────────────────────────────────────────────────
    // =========================================================================

    [Header("── Hunger ───────────────────────────────────")]
    [Range(10f, 200f)] public float maxHunger = 100f;
    public float currentHunger;
    [Range(0f, 20f)] public float hungerDrainPerSecond = 2.2f;

    [Header("── Hunger UI (optional) ──")]
    public Slider hungerSlider;
    public TMP_Text hungerText;
    public Color hungerColorFull  = new Color(0.2f, 0.85f, 0.3f);
    public Color hungerColorEmpty = new Color(0.9f, 0.15f, 0.15f);

    [Header("── Debug ───────────────────────────────────")]
    public bool showFPS = false;
    public bool showDebugHUD = false;

    // =========================================================================
    // ── PRIVATE STATE ────────────────────────────────────────────────────────
    // =========================================================================

    public string lastStatusReason = ""; 
    private PlayerController _player;
    private Image _hungerFill;
    private float _countdownTimer;

    private float _fpsAccumulator = 0;
    private int   _fpsFrames      = 0;
    private float _fpsTimeLeft    = 0.5f;
    private float _lastFps        = 0;

    // =========================================================================
    // ── LIFECYCLE ────────────────────────────────────────────────────────────
    // =========================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _player = FindObjectOfType<PlayerController>();
        
        // Ensure UI logic is attached
        if (GetComponent<GameOverUI>() == null) gameObject.AddComponent<GameOverUI>();
    }

    private void Start()
    {
        currentHunger = maxHunger;

        if (tapToStartText != null)
            tapToStartText.text = tapToStartMessage;

        if (requireTapToStart)
        {
            state = GameState.AwaitingStartTap;
            Time.timeScale = 0f;
            if (_player != null) _player.SetMovementEnabled(false);
            SetTapToStartUIVisible(true);
            SetCountdownUIVisible(false);
        }
        else if (enableStartCountdown)
        {
            state = GameState.Countdown;
            _countdownTimer = startCountdownDuration;
            Time.timeScale = 0f;
            if (_player != null) _player.SetMovementEnabled(false);
            SetTapToStartUIVisible(false);
            SetCountdownUIVisible(true);
            UpdateCountdownUI();
        }
        else
        {
            state = GameState.Playing;
            Time.timeScale = 1f;
            SetTapToStartUIVisible(false);
            SetCountdownUIVisible(false);
        }

        if (hungerSlider != null)
        {
            hungerSlider.maxValue = maxHunger;
            _hungerFill = hungerSlider.fillRect?.GetComponent<Image>();
        }
        RefreshUI();
    }

    private void Update()
    {
        HandleMetaInput();

        if (state == GameState.AwaitingStartTap)
        {
            Time.timeScale = 0f;
            if (_player != null) _player.SetMovementEnabled(false);

            if (IsStartInputPressed())
                BeginGameplayFlowAfterStartTap();
            return;
        }

        if (state == GameState.Countdown)
        {
            UpdateCountdown();
            return;
        }

        if (state != GameState.Playing) return;

        UpdateTick();
    }

    private void UpdateCountdown()
    {
        Time.timeScale = 0f;
        if (_player != null) _player.SetMovementEnabled(false);

        _countdownTimer -= Time.unscaledDeltaTime;
        UpdateCountdownUI();

        if (_countdownTimer <= 0f)
        {
            state = GameState.Playing;
            Time.timeScale = 1f;
            if (_player != null) _player.SetMovementEnabled(true);
            SetCountdownUIVisible(false);
        }
    }

    private void SetCountdownUIVisible(bool visible)
    {
        if (countdownUIRoot != null)
            countdownUIRoot.SetActive(visible);
    }

    private void SetTapToStartUIVisible(bool visible)
    {
        if (tapToStartUIRoot != null)
            tapToStartUIRoot.SetActive(visible);
    }

    public void OnTapToStartPressed()
    {
        if (state != GameState.AwaitingStartTap) return;
        BeginGameplayFlowAfterStartTap();
    }

    private void BeginGameplayFlowAfterStartTap()
    {
        SetTapToStartUIVisible(false);

        if (enableStartCountdown)
        {
            state = GameState.Countdown;
            _countdownTimer = startCountdownDuration;
            _lastCountdownSeconds = -1;
            Time.timeScale = 0f;
            if (_player != null) _player.SetMovementEnabled(false);
            SetCountdownUIVisible(true);
            UpdateCountdownUI();
        }
        else
        {
            state = GameState.Playing;
            Time.timeScale = 1f;
            if (_player != null) _player.SetMovementEnabled(true);
            SetCountdownUIVisible(false);
        }
    }

    private bool IsStartInputPressed()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            if (Keyboard.current.enterKey.wasPressedThisFrame) return true;
        }

        return false;
    }

    private int _lastCountdownSeconds = -1;

    private void UpdateCountdownUI()
    {
        if (countdownText == null) return;

        int seconds = Mathf.CeilToInt(Mathf.Max(0f, _countdownTimer));
        string newText = seconds > 0 ? seconds.ToString() : "GO!";

        if (seconds != _lastCountdownSeconds)
        {
            _lastCountdownSeconds = seconds;
            countdownText.text = newText;
            if (_scalePunchCoroutine != null) StopCoroutine(_scalePunchCoroutine);
            _scalePunchCoroutine = StartCoroutine(ScalePunch(countdownText.transform));

            // Play countdown beeps
            if (seconds > 0)
            {
                string soundToPlay = (seconds == 1) ? countdownBeep2SoundID : countdownBeep1SoundID;
                if (SoundFXManager.Instance != null)
                    SoundFXManager.Instance.PlaySound(soundToPlay);
            }
        }
    }

    private Coroutine _scalePunchCoroutine;

    private System.Collections.IEnumerator ScalePunch(Transform target)
    {
        Vector3 originalScale = Vector3.one;
        float punchScale = 1.6f;
        float duration = 0.25f;
        float halfDuration = duration * 0.5f;
        float elapsed = 0f;

        // Scale up
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.LerpUnclamped(originalScale, originalScale * punchScale, t);
            yield return null;
        }

        elapsed = 0f;

        // Scale back down
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / halfDuration;
            target.localScale = Vector3.LerpUnclamped(originalScale * punchScale, originalScale, t);
            yield return null;
        }

        target.localScale = originalScale;
        _scalePunchCoroutine = null;
    }

    private void UpdateTick()
    {
        // Hunger Tick
        if (currentHunger > 0)
        {
            currentHunger -= hungerDrainPerSecond * Time.deltaTime;
            currentHunger = Mathf.Max(0, currentHunger);
            
            if (currentHunger <= 0)
                TriggerGameOver("Starved To Death!");
        }

        RefreshUI();
    }

    private void HandleMetaInput()
    {
        if (state == GameState.GameOver || state == GameState.Won)
        {
            bool restart = false;
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) restart = true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) restart = true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) restart = true;
            
            if (restart) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // =========================================================================
    // ── UI REFRESH ───────────────────────────────────────────────────────────
    // =========================================================================

    private void RefreshUI()
    {
        // Hunger Bar
        if (hungerSlider != null) hungerSlider.value = currentHunger;
        if (hungerText != null) hungerText.text = $"Hunger: {Mathf.CeilToInt(currentHunger)}";
        if (_hungerFill != null)
        {
            float t = currentHunger / maxHunger;
            _hungerFill.color = Color.Lerp(hungerColorEmpty, hungerColorFull, t);
        }
    }

    // =========================================================================
    // ── PUBLIC API ───────────────────────────────────────────────────────────
    // =========================================================================

    public float HungerRatio => maxHunger > 0f ? currentHunger / maxHunger : 0f;

    public void EatRice(float nutrition)
    {
        if (state != GameState.Playing) return;
        currentHunger = Mathf.Min(currentHunger + nutrition, maxHunger);
        RefreshUI();
    }

    public void TriggerGameOver(string reason = "You got caught!")
    {
        if (state != GameState.Playing) return;
        state = GameState.GameOver;
        lastStatusReason = reason;
        if (_player != null) _player.SetMovementEnabled(false);
    }

    public void TriggerWin()
    {
        if (state != GameState.Playing) return;
        state = GameState.Won;
        lastStatusReason = "YOU SURVIVED!";
        if (_player != null) _player.SetMovementEnabled(false);
    }

    private void OnGUI()
    {
        if (showFPS)
        {
            _fpsTimeLeft -= Time.unscaledDeltaTime;
            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsTimeLeft <= 0)
            {
                _lastFps = _fpsFrames / _fpsAccumulator;
                _fpsTimeLeft = 0.5f; _fpsAccumulator = 0; _fpsFrames = 0;
            }
            GUIStyle style = new GUIStyle(); style.fontSize = 20; style.normal.textColor = Color.white; style.alignment = TextAnchor.UpperRight;
            GUI.Label(new Rect(Screen.width - 110, 10, 100, 30), $"{Mathf.RoundToInt(_lastFps)} FPS", style);
        }

        if (showDebugHUD)
        {
            GUIStyle style = new GUIStyle(); style.fontSize = 18; style.normal.textColor = Color.yellow;
            string info = $"Hunger: {currentHunger:F1} | Fear: {FindObjectOfType<HorrorAtmosphere>()?.FearLevel:P0}";
            GUI.Label(new Rect(10, Screen.height - 30, 400, 30), info, style);
        }
    }
}
