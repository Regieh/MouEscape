using UnityEngine;

[System.Serializable]
public struct SoundEffect
{
    public string groupID;
    public AudioClip[] clips;
}

public class SoundLibrary : MonoBehaviour
{
    public SoundEffect[] soundEffects;
    
    public AudioClip GetClipFromName(string name)
    {
        Debug.Log($"🔍 SoundLibrary: Looking for sound group '{name}'");
        Debug.Log($"📚 Total sound groups available: {soundEffects.Length}");
        
        foreach (var soundEffect in soundEffects)
        {
            Debug.Log($"  - Checking group: '{soundEffect.groupID}' (has {soundEffect.clips.Length} clips)");
            
            if (soundEffect.groupID == name)
            {
                if (soundEffect.clips.Length == 0)
                {
                    Debug.LogError($"❌ Sound group '{name}' found but has NO clips!");
                    return null;
                }
                
                int randomIndex = Random.Range(0, soundEffect.clips.Length);
                AudioClip selectedClip = soundEffect.clips[randomIndex];
                
                if (selectedClip == null)
                {
                    Debug.LogError($"❌ Clip at index {randomIndex} in group '{name}' is NULL!");
                    return null;
                }
                
                Debug.Log($"✅ Found clip: '{selectedClip.name}' (index {randomIndex}/{soundEffect.clips.Length})");
                return selectedClip;
            }
        }
        
        Debug.LogError($"❌ Sound group '{name}' NOT FOUND in library!");
        return null;
    }
}