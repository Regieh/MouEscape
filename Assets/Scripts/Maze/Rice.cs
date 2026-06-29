using UnityEngine;

public class Rice : MonoBehaviour
{
    public float nutritionValue = 10f;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            // Heal player
            if (GameManager.Instance != null)
            {
                GameManager.Instance.EatRice(nutritionValue);
            }

            // Spawn replacement so the player never fully runs out of food
            DungeonMaster dm = FindObjectOfType<DungeonMaster>();
            if (dm != null)
            {
                dm.SpawnSingleRice();
            }

            // Play eating sound
            if (SoundFXManager.Instance != null)
                SoundFXManager.Instance.PlaySound("Eat");

            // Destroy this grain
            Destroy(gameObject);
        }
    }
}
