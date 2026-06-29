using UnityEngine;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("Settings")]
    [SerializeField] private bool enableDebugLogs = false;

    [Header("References")]
    [SerializeField] private SoundLibrary sfxLibrary;
    [SerializeField] private AudioSource sfx2DSource; // Used for one-shots

    private void Awake()
    {
        Log("🎵 SoundManager Awake() called");

        if (Instance != null)
        {
            LogWarning("⚠️ Duplicate SoundManager destroyed.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Log("✅ SoundManager initialized");

        if (sfxLibrary == null) LogError("❌ SoundLibrary is NULL!");
        else Log($"✅ SoundLibrary OK ({sfxLibrary.soundEffects.Length} groups)");

        if (sfx2DSource == null) LogWarning("⚠️ 2D AudioSource is NULL!");
    }

    // ── ONE-SHOT: fire and forget ──────────────────────────────────────────────
    public void PlaySound2D(string soundName)
    {
        Log($"🔊 PlaySound2D: '{soundName}'");

        if (sfx2DSource == null) { LogError("❌ 2D AudioSource is NULL!"); return; }

        AudioClip clip = sfxLibrary.GetClipFromName(soundName);
        if (clip != null) sfx2DSource.PlayOneShot(clip);
    }

    public void PlaySound2D(AudioClip clip)
    {
        Log($"🔊 PlaySound2D(clip): '{clip?.name}'");

        if (sfx2DSource == null) { LogError("❌ 2D AudioSource is NULL!"); return; }
        if (clip != null) sfx2DSource.PlayOneShot(clip);
    }

    // ── STOPPABLE: returns the AudioSource so caller can Stop() it ────────────
    public AudioSource PlaySound2DStoppable(
        AudioClip clip,
        float volume = 1f,
        bool loop = false)
    {
        if (clip == null) { LogError("❌ AudioClip is NULL!"); return null; }

        GameObject go = new GameObject($"TempAudio_{clip.name}");
        DontDestroyOnLoad(go); // survive scene loads if needed

        AudioSource source = go.AddComponent<AudioSource>();
        source.clip        = clip;
        source.volume      = volume;
        source.spatialBlend = 0f;  // ← 0 = full 2D, 1 = full 3D
        source.loop        = loop;
        source.Play();

        if (!loop) Destroy(go, clip.length);

        return source;
    }

    public AudioSource PlaySound2DStoppable(
        string soundName,
        float volume = 1f,
        bool loop = false)
    {
        AudioClip clip = GetClip(soundName);
        return PlaySound2DStoppable(clip, volume, loop);
    }

    // ── UTILITY ───────────────────────────────────────────────────────────────
    public AudioClip GetClip(string soundName)
    {
        if (sfxLibrary == null) { LogError("❌ SoundLibrary is NULL!"); return null; }
        AudioClip clip = sfxLibrary.GetClipFromName(soundName);
        if (clip == null) LogError($"❌ Sound not found: '{soundName}'");
        return clip;
    }

    private void Log(string msg)        { if (enableDebugLogs) Debug.Log(msg); }
    private void LogWarning(string msg) { if (enableDebugLogs) Debug.LogWarning(msg); }
    private void LogError(string msg)   { if (enableDebugLogs) Debug.LogError(msg); }
}