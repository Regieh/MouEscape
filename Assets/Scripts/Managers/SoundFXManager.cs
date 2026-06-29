using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class SoundFXManager : MonoBehaviour
{
    public static SoundFXManager Instance { get; private set; }

    [System.Serializable]
    public struct SoundData
    {
        public string id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume;
    }

    [Header("Sounds Registry")]
    public List<SoundData> soundRegistry = new List<SoundData>();

    private AudioSource audioSource;
    private Dictionary<string, SoundData> soundLookup = new Dictionary<string, SoundData>();
    private Dictionary<string, AudioSource> activeLoops = new Dictionary<string, AudioSource>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        
        // Initialize lookup dictionary for fast access
        foreach (var data in soundRegistry)
        {
            if (!string.IsNullOrEmpty(data.id) && !soundLookup.ContainsKey(data.id.ToLower()))
            {
                soundLookup.Add(data.id.ToLower(), data);
            }
        }
    }

    /// <summary>
    /// Plays a sound effect by its string ID.
    /// Example: SoundFXManager.Instance.PlaySound("munch");
    /// </summary>
    public void PlaySound(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        string searchId = id.ToLower();
        if (soundLookup.ContainsKey(searchId))
        {
            var data = soundLookup[searchId];
            if (data.clip != null)
            {
                // PlayOneShot allows multiple sounds to overlap without cutting each other off
                audioSource.PlayOneShot(data.clip, data.volume > 0 ? data.volume : 1f);
            }
        }
        else
        {
            Debug.LogWarning($"SoundFXManager: Sound ID '{id}' not found in registry.");
        }
    }

    /// <summary>
    /// Starts a looping sound. It creates a child AudioSource to handle the loop independently.
    /// </summary>
    public void PlayLoopingSound(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        string searchId = id.ToLower();

        if (!soundLookup.ContainsKey(searchId))
        {
            Debug.LogWarning($"SoundFXManager: Cannot loop '{id}' - not found in registry.");
            return;
        }

        if (activeLoops.ContainsKey(searchId)) return; // Already playing

        var data = soundLookup[searchId];
        if (data.clip == null) return;

        // Create a dedicated source for this loop
        GameObject looperObj = new GameObject($"Loop_{id}");
        looperObj.transform.SetParent(transform);
        
        AudioSource looper = looperObj.AddComponent<AudioSource>();
        looper.clip = data.clip;
        looper.loop = true;
        looper.volume = data.volume > 0 ? data.volume : 1f;
        looper.Play();

        activeLoops.Add(searchId, looper);
    }

    /// <summary>
    /// Stops a specific looping sound and destroys its AudioSource.
    /// </summary>
    public void StopLoopingSound(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        string searchId = id.ToLower();

        if (activeLoops.TryGetValue(searchId, out AudioSource looper))
        {
            looper.Stop();
            Destroy(looper.gameObject);
            activeLoops.Remove(searchId);
        }
    }
}
