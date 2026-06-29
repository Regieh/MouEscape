using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HideButton : MonoBehaviour
{
    [Header("Sprites")]
    public Sprite hideSprite;
    public Sprite unhideSprite;

    private Image _buttonImage;
    private TextMeshProUGUI _tmpText;

    private void Awake()
    {
        _buttonImage = GetComponent<Image>();
        _tmpText = GetComponentInChildren<TextMeshProUGUI>();
        
        // Set initial state
        UpdateVisuals();
    }

    public void OnClick()
    {
        if (PlayerController.Instance != null)
        {
            PlayerController.Instance.ToggleHide();
            UpdateVisuals();
        }
    }

    private void UpdateVisuals()
    {
        // Door action now acts as wormhole entry.
        if (_tmpText != null)
        {
            _tmpText.text = "ENTER";
        }

        // Update Sprite if assigned
        if (_buttonImage != null)
        {
            Sprite target = hideSprite != null ? hideSprite : unhideSprite;
            if (target != null) _buttonImage.sprite = target;
        }
    }
}
