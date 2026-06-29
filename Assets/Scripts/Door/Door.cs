using UnityEngine;

public class Door : MonoBehaviour
{
    [Header("UI Feedback")]
    [Tooltip("Drag the UI Button here. It will show/hide based on player proximity.")]
    public GameObject interactionButton;

    [Header("Wormhole")]
    [Tooltip("Destination door for this wormhole. DungeonMaster links these automatically.")]
    public Door linkedDoor;
    [Tooltip("Optional exact exit point. If not set, uses this door position + Exit Nudge Distance.")]
    public Transform exitPoint;
    [Tooltip("Short lockout after teleport to prevent immediate bounce-back.")]
    public float teleportCooldown = 0.25f;
    [Tooltip("If Exit Point is empty, player exits in this door's up direction by this distance.")]
    public float exitNudgeDistance = 0.75f;

    private GameObject _playerRef;
    private float _nextTeleportAllowedTime;

    private void Start()
    {
        if (interactionButton != null) interactionButton.SetActive(false);
    }

    // The door detects when a player is close to show the button
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            _playerRef = other.gameObject;
            if (interactionButton != null) interactionButton.SetActive(true);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            if (interactionButton != null) interactionButton.SetActive(false);
            if (_playerRef == other.gameObject) _playerRef = null;
        }
    }

    // This is called by the PlayerController via SendMessage
    public void Interact()
    {
        if (_playerRef == null) return;
        if (Time.time < _nextTeleportAllowedTime) return;

        if (linkedDoor == null)
        {
            Debug.LogWarning($"{gameObject.name}: No linked door assigned for wormhole travel.");
            return;
        }

        Vector3 destination = linkedDoor.GetExitWorldPosition();

        PlayerController pc = _playerRef.GetComponent<PlayerController>();
        if (pc != null) pc.SetHiding(false);

        Rigidbody2D rb = _playerRef.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.position = destination;
        }
        else
        {
            _playerRef.transform.position = destination;
        }

        Physics2D.SyncTransforms();

        _nextTeleportAllowedTime = Time.time + teleportCooldown;
        linkedDoor._nextTeleportAllowedTime = Time.time + linkedDoor.teleportCooldown;

        Debug.Log($"{gameObject.name}: Wormhole jump -> {linkedDoor.gameObject.name}");
    }

    public void SetLinkedDoor(Door other)
    {
        linkedDoor = other;
    }

    private Vector3 GetExitWorldPosition()
    {
        if (exitPoint != null) return exitPoint.position;

        Vector3 dir = transform.up;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.up;
        return transform.position + dir.normalized * exitNudgeDistance;
    }
}