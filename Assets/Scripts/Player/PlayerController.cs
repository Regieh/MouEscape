using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance;

    [Header("Movement")]
    public float moveSpeed  = 5f;
    [Tooltip("How fast the sprite visually rotates to face movement direction. Pure cosmetic — never affects velocity.")]
    public float turnSpeed  = 720f;
    [Tooltip("Set this to match the Z rotation you set in the Inspector for your sprite.\n0 = sprite faces UP by default.\n90 = sprite faces RIGHT by default (most common).")]
    public float spriteRotationOffset = 90f;

    [Tooltip("Rotate the joystick input itself. Use this if 'Up' on the stick moves the player in the wrong world direction.\nCommon values are 90, -90, or 180.")]
    public float inputRotationOffset = 0f;

    private bool _canMove = true;

    [Header("Hide")]
    public bool isHiding;
    public GameObject globalHideButton;

    [Header("Light")]
    public Transform spotLight2D;
    [Tooltip("Fine-tune the spotlight direction.\nIf the light cone is 90° clockwise from movement: set to +90\nIf it's 90° counter-clockwise: set to -90")]
    public float lightRotationOffset = 0f;
    public float lightRotationspeed = 360f;

    private Rigidbody2D _rb;
    private Animator    _anim;
    private Vector2     _moveInput;
    public  GameObject  _activeDoor;
    private bool        _isWalkingSoundPlaying = false;

    private void Awake()
    {
        Instance = this;
        _rb = GetComponent<Rigidbody2D>();
        
        // Try current object first, then children (common if Animator is on 'Square')
        _anim = GetComponent<Animator>();
        if (_anim == null) _anim = GetComponentInChildren<Animator>();

        if (_anim == null)
            Debug.LogWarning("PlayerController: No Animator found on this object or its children!");

        _rb.gravityScale  = 0f;
        _rb.freezeRotation = true;  // We handle rotation manually below
        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void Start()
    {
        if (globalHideButton != null)
            globalHideButton.SetActive(false);

        // Ensure player is always visible (unlit) even in dark corridors
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            Material unlit = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            if (unlit != null && unlit.shader != null) sr.material = unlit;
        }
    }

    private void FixedUpdate()
    {
        if (!_canMove || isHiding)
        {
            _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, Vector2.zero, 50f * Time.fixedDeltaTime);
            return;
        }

        // ── MOVEMENT: Apply input rotation offset to synchronize with joystick/screen orientation ──────
        Vector2 rotatedInput = _moveInput;
        if (inputRotationOffset != 0)
        {
            float rad = inputRotationOffset * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            rotatedInput = new Vector2(
                _moveInput.x * cos - _moveInput.y * sin,
                _moveInput.x * sin + _moveInput.y * cos
            );
        }

        _rb.linearVelocity = rotatedInput * moveSpeed;
    }

    private void Update()
    {
        // ── ROTATION: Visuals now use the rotated input to stay synced with movement ───────────
        Vector2 rotatedInput = _moveInput;
        if (inputRotationOffset != 0)
        {
            float rad = inputRotationOffset * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            rotatedInput = new Vector2(
                _moveInput.x * cos - _moveInput.y * sin,
                _moveInput.x * sin + _moveInput.y * cos
            );
        }

        if (rotatedInput != Vector2.zero)
        {
            float rawAngle = Mathf.Atan2(rotatedInput.y, rotatedInput.x) * Mathf.Rad2Deg;

            // SPRITE angle: The mouse faces RIGHT at Z=0.
            float spriteTarget = rawAngle + spriteRotationOffset - 90f;
            float spriteAngle  = Mathf.MoveTowardsAngle(transform.eulerAngles.z, spriteTarget, turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, spriteAngle);

            // SPOTLIGHT angle: The light also faces RIGHT at Z=0.
            float lightTarget = rawAngle + lightRotationOffset;
            if (spotLight2D != null && spotLight2D.gameObject.activeInHierarchy)
                spotLight2D.rotation = Quaternion.Euler(0f, 0f, lightTarget);
        }

        // ── ANIMATION & SOUND: Using physical velocity is more robust than input ──────
        bool walking = _canMove && !isHiding && _rb.linearVelocity.sqrMagnitude > 0.1f;
        
        if (_anim != null)
        {
            _anim.SetBool("isWalking", walking);
        }

        // Handle Walking Sound Loop
        if (walking && !_isWalkingSoundPlaying)
        {
            _isWalkingSoundPlaying = true;
            if (SoundFXManager.Instance != null) SoundFXManager.Instance.PlayLoopingSound("walking");
        }
        else if (!walking && _isWalkingSoundPlaying)
        {
            _isWalkingSoundPlaying = false;
            if (SoundFXManager.Instance != null) SoundFXManager.Instance.StopLoopingSound("walking");
        }
    }

    // --- TRIGGER DETECTION ---
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.GetComponent<Door>() != null)
        {
            _activeDoor = collision.gameObject;
            if (globalHideButton != null) globalHideButton.SetActive(true);
            Debug.Log($"Entered range of: {_activeDoor.name}");
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (_canMove && _activeDoor == collision.gameObject)
        {
            Debug.Log($"Left range of: {_activeDoor.name}");
            _activeDoor = null;
            if (globalHideButton != null) globalHideButton.SetActive(false);
        }
    }

    // --- INPUT SYSTEM CALLBACKS ---
    public void OnMove(InputAction.CallbackContext ctx) => SetMoveInput(ctx.ReadValue<Vector2>());

    public void SetMoveInput(Vector2 input)
    {
        _moveInput = Vector2.ClampMagnitude(input, 1f);
    }

    public void OnInteract(InputAction.CallbackContext ctx)
    {
        if (ctx.performed && _activeDoor != null)
        {
            _activeDoor.SendMessage("Interact", SendMessageOptions.DontRequireReceiver);
        }
    }

    public void OnHide(InputAction.CallbackContext ctx)
    {
        if (ctx.performed && _activeDoor != null)
        {
            _activeDoor.SendMessage("Interact", SendMessageOptions.DontRequireReceiver);
        }
    }

    public bool IsHiding() => isHiding;

    public void ToggleHide()
    {
        if (_activeDoor != null)
        {
            _activeDoor.SendMessage("Interact", SendMessageOptions.DontRequireReceiver);
        }
    }

    public void SetHiding(bool state)
    {
        isHiding = state;
        
        if (spotLight2D != null)
        {
            spotLight2D.gameObject.SetActive(!isHiding);
        }
    }

    public void SetMovementEnabled(bool state)
    {
        _canMove = state;
        if (!state) _rb.linearVelocity = Vector2.zero;
    }
}