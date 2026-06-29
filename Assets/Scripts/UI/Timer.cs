using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Timer : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("Total seconds to survive to win.")]
    [Range(30f, 600f)]
    [SerializeField] private float targetSurvivalTime = 60f;

    [Header("Session UI")]
    [SerializeField] private Slider sessionSlider;
    [SerializeField] private TMP_Text sessionText;

    [SerializeField] private AudioSource tickingAudioSource;

    private GameManager _gameManager;
    private float _remainingTime;
    private bool _tickingPlaying;

    public float TargetSurvivalTime => targetSurvivalTime;
    public float RemainingTime => _remainingTime;

    private void Awake()
    {
        _gameManager = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
    }

    private void Start()
    {
        ResetTimer();
    }

    private void Update()
    {
        if (_gameManager == null)
            _gameManager = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();

        if (_gameManager == null) return;

        bool isPlaying = _gameManager.state == GameManager.GameState.Playing;

        // Start / stop ticking sound when game state changes.
        if (isPlaying && !_tickingPlaying)
        {
            StartTicking();
        }
        else if (!isPlaying && _tickingPlaying)
        {
            StopTicking();
        }

        if (!isPlaying) return;

        _remainingTime -= Time.deltaTime;
        if (_remainingTime <= 0f)
        {
            _remainingTime = 0f;
            RefreshUI();
            StopTicking();
            _gameManager.TriggerWin();
            return;
        }

        RefreshUI();
    }

    private void OnDisable()
    {
        StopTicking();
    }

    private void StartTicking()
    {
        if (tickingAudioSource == null) return;
        tickingAudioSource.Play();
        _tickingPlaying = true;
    }

    private void StopTicking()
    {
        if (!_tickingPlaying) return;
        if (tickingAudioSource != null)
            tickingAudioSource.Stop();
        _tickingPlaying = false;
    }

    public void ResetTimer()
    {
        _remainingTime = targetSurvivalTime;

        if (sessionSlider != null)
            sessionSlider.maxValue = targetSurvivalTime;

        RefreshUI();
    }

    private void RefreshUI()
    {
        if (sessionSlider != null)
            sessionSlider.value = targetSurvivalTime - _remainingTime;

        if (sessionText != null)
            sessionText.text = $"{Mathf.CeilToInt(_remainingTime)}s";
    }
}
