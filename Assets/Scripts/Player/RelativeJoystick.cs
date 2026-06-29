using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class RelativeJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("References")]
    [SerializeField] private GameObject joystickContainer;
    [SerializeField] private bool verboseLogs;

    private Canvas _canvas;
    private OnScreenStick _onScreenStick;
    private RectTransform _joystickContainerRect;
    private RectTransform _canvasRect;
    private CanvasGroup _joystickGroup;
    private int _activePointerId = int.MinValue;

    private void Awake()
    {
        if (joystickContainer == null)
        {
            Debug.LogError("RelativeJoystick: joystickContainer is not assigned.", this);
            enabled = false;
            return;
        }

        _joystickContainerRect = joystickContainer.GetComponent<RectTransform>();
        if (_joystickContainerRect == null)
        {
            Debug.LogError("RelativeJoystick: joystickContainer has no RectTransform.", this);
            enabled = false;
            return;
        }

        _canvas = GetComponentInParent<Canvas>();
        _canvasRect = _canvas.transform as RectTransform;

        // Normalize anchor to center so anchoredPosition == local offset from canvas center.
        _joystickContainerRect.anchorMin = new Vector2(0.5f, 0.5f);
        _joystickContainerRect.anchorMax = new Vector2(0.5f, 0.5f);
        _joystickContainerRect.pivot     = new Vector2(0.5f, 0.5f);

        // CanvasGroup: hide without SetActive so OnScreenStick stays initialized.
        if (!joystickContainer.TryGetComponent(out _joystickGroup))
            _joystickGroup = joystickContainer.AddComponent<CanvasGroup>();

        _onScreenStick = joystickContainer.GetComponentInChildren<OnScreenStick>(true);

        // This object (Joystick Area) needs an invisible graphic so it receives pointer events.
        EnsurePointerRaycastTarget();

        SetJoystickVisible(false);
    }

    private void EnsurePointerRaycastTarget()
    {
        if (TryGetComponent<Graphic>(out var g))
        {
            g.raycastTarget = true;
            return;
        }
        var img = gameObject.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_activePointerId != int.MinValue) return;

        _activePointerId = eventData.pointerId;

        // Convert screen tap to canvas-local position and place the background there.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            eventData.position,
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out Vector2 localPoint);

        _joystickContainerRect.anchoredPosition = localPoint;
        SetJoystickVisible(true);

        if (_onScreenStick != null)
            ExecuteEvents.Execute<IPointerDownHandler>(
                _onScreenStick.gameObject, eventData, ExecuteEvents.pointerDownHandler);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId) return;

        if (_onScreenStick != null)
            ExecuteEvents.Execute<IDragHandler>(
                _onScreenStick.gameObject, eventData, ExecuteEvents.dragHandler);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _activePointerId) return;

        if (_onScreenStick != null)
            ExecuteEvents.Execute<IPointerUpHandler>(
                _onScreenStick.gameObject, eventData, ExecuteEvents.pointerUpHandler);

        SetJoystickVisible(false);
        _activePointerId = int.MinValue;
    }

    private void OnDisable()
    {
        _activePointerId = int.MinValue;
        if (_joystickGroup != null)
            SetJoystickVisible(false);
    }

    private void SetJoystickVisible(bool visible)
    {
        _joystickGroup.alpha          = visible ? 1f : 0f;
        _joystickGroup.interactable   = visible;
        _joystickGroup.blocksRaycasts = visible;

        if (verboseLogs)
            Debug.Log($"[RelativeJoystick] visible={visible}  alpha={_joystickGroup.alpha}  " +
                      $"anchored={_joystickContainerRect.anchoredPosition}  pointer={_activePointerId}", this);
    }
}