using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Controls a predefined Game Over / Victory UI wired from the Inspector.
/// </summary>
public class GameOverUI : MonoBehaviour
{
    [Header("Predefined UI References")]
    public GameObject uiRoot;
    public TextMeshProUGUI headerText;
    public TextMeshProUGUI subText;
    public Button restartButton;

    [Header("Text Settings")]
    public string gameOverTitle = "GAME OVER";
    public string victoryTitle = "VICTORY";
    public Color gameOverColor = new Color(0.9f, 0.1f, 0.1f);
    public Color victoryColor = Color.green;

    private bool uiShown = false;

    void Awake()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartLevel);
            restartButton.onClick.AddListener(RestartLevel);
        }

        if (uiRoot != null)
            uiRoot.SetActive(false);
    }

    void Update()
    {
        if (GameManager.Instance == null) return;

        bool shouldShow = (GameManager.Instance.state == GameManager.GameState.GameOver || 
                           GameManager.Instance.state == GameManager.GameState.Won);

        if (shouldShow && !uiShown)
        {
            ShowUI();
        }
        else if (!shouldShow && uiShown)
        {
            HideUI();
        }
    }

    void ShowUI()
    {
        uiShown = true;

        if (uiRoot == null)
        {
            Debug.LogWarning("GameOverUI: uiRoot is not assigned.");
            return;
        }

        bool isWin = GameManager.Instance.state == GameManager.GameState.Won;

        if (headerText != null)
        {
            headerText.text = isWin ? victoryTitle : gameOverTitle;
            headerText.color = isWin ? victoryColor : gameOverColor;
        }

        if (subText != null)
            subText.text = GameManager.Instance.lastStatusReason;

        uiRoot.SetActive(true);
    }

    void HideUI()
    {
        uiShown = false;
        if (uiRoot != null) uiRoot.SetActive(false);
    }

    public void RestartLevel()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
