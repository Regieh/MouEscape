using UnityEngine;

/// <summary>
/// Smooth camera follow — tracks the player's POSITION only.
/// Rotation is always locked to zero so the camera never tilts or spins
/// even when the player sprite rotates.
///
/// Setup:
///   1. Attach this script to your Main Camera.
///   2. Make sure the Camera is NOT a child of the player in the Hierarchy.
///      (Drag it out to the root of the scene if it was a child.)
///   3. Assign the Player transform in the Inspector.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The player's Transform. Auto-found via PlayerController if left empty.")]
    public Transform target;

    [Header("Smoothing")]
    [Tooltip("Higher = snappier follow. Lower = floaty cinematic feel. 8-12 is ideal.")]
    public float smoothSpeed = 10f;

    [Header("Offset")]
    [Tooltip("Z offset to keep the camera above the 2D scene. Usually -10.")]
    public float zDepth = -10f;

    [Tooltip("Optional slight offset in front of the player so you see more of where they're heading.")]
    public Vector2 lookAheadOffset = Vector2.zero;

    private Vector3 _velocity = Vector3.zero;

    private void Awake()
    {
        // LOCK rotation immediately — never let it be anything but identity
        transform.rotation = Quaternion.identity;
    }

    private void Start()
    {
        // Auto-find player if not assigned
        if (target == null && PlayerController.Instance != null)
            target = PlayerController.Instance.transform;

        if (target == null)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) target = go.transform;
        }

        // Snap to player immediately so there's no slide-in on game start
        if (target != null)
            transform.position = new Vector3(target.position.x + lookAheadOffset.x,
                                             target.position.y + lookAheadOffset.y,
                                             zDepth);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos = new Vector3(
            target.position.x + lookAheadOffset.x,
            target.position.y + lookAheadOffset.y,
            zDepth
        );

        // Smooth damp position only
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos,
                                                 ref _velocity, 1f / smoothSpeed);

        // HARD LOCK rotation — no matter what — every single frame.
        // This prevents the player's rotating sprite from ever affecting the camera.
        transform.rotation = Quaternion.identity;
    }
}
