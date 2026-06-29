using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Spawns dense visual rice stalk clusters on floor tiles to create
/// a rice field atmosphere. These are purely decorative — no colliders,
/// no nutrition — and are batched under a single parent for performance.
/// </summary>
public class RiceFieldSpawner : MonoBehaviour
{
    [Header("Prefab / Sprite")]
    [Tooltip("Assign a rice stalk sprite prefab. Must have a SpriteRenderer. No collider needed.")]
    public GameObject stalkPrefab;

    [Header("Field Coverage")]
    [Tooltip("Fraction of floor tiles that become rice field centers (0 = none, 1 = everywhere).")]
    [Range(0f, 1f)] public float fieldDensity = 0.35f;

    [Header("Cluster Settings")]
    [Tooltip("Min stalks generated per cluster center.")]
    [Range(1, 40)] public int stalksPerClusterMin = 6;
    [Tooltip("Max stalks generated per cluster center.")]
    [Range(1, 40)] public int stalksPerClusterMax = 14;
    [Tooltip("Radius in world units around each cluster center that stalks are scattered.")]
    public float clusterRadius = 0.55f;

    [Header("Stalk Variation")]
    public float scaleMin = 0.5f;
    public float scaleMax = 1.1f;
    [Tooltip("Random Z rotation range in degrees applied to each stalk.")]
    public float rotationRange = 360f;
    [Tooltip("Random alpha applied per stalk for natural depth variation.")]
    [Range(0f, 1f)] public float alphaMin = 0.55f;
    [Range(0f, 1f)] public float alphaMax = 1f;

    [Header("Sorting")]
    public string sortingLayerName = "Default";
    public int sortingOrder = -1;

    void Start()
    {
        // Spawn() is called by DungeonMaster after generation.
        // If DungeonMaster.Instance already exists (e.g. in Editor test), spawn now.
        if (DungeonMaster.Instance != null && DungeonMaster.Instance.IsReady)
            Spawn();
    }

    public void Spawn()
    {
        DungeonMaster dm = DungeonMaster.Instance;
        if (dm == null)
        {
            Debug.LogWarning("RiceFieldSpawner: DungeonMaster not found.");
            return;
        }

        if (stalkPrefab == null)
        {
            Debug.LogWarning("RiceFieldSpawner: stalkPrefab not assigned.");
            return;
        }

        HashSet<Vector3Int> floor = dm.GetFloorPositions();
        Tilemap tilemap = dm.GetTilemap();
        if (floor == null || tilemap == null || floor.Count == 0) return;

        GameObject fieldRoot = new GameObject("RiceField");
        fieldRoot.transform.SetParent(transform, false);

        int spawned = 0;

        foreach (Vector3Int cell in floor)
        {
            if (Random.value > fieldDensity) continue;

            Vector3 worldCenter = tilemap.GetCellCenterWorld(cell);
            int count = Random.Range(stalksPerClusterMin, stalksPerClusterMax + 1);

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * clusterRadius;
                Vector3 pos = worldCenter + new Vector3(offset.x, offset.y, 0f);

                GameObject stalk = Instantiate(stalkPrefab, pos, Quaternion.identity, fieldRoot.transform);
                stalk.transform.localRotation = Quaternion.identity;

                float scale = Random.Range(scaleMin, scaleMax);
                stalk.transform.localScale = new Vector3(scale, scale, 1f);

                SpriteRenderer sr = stalk.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sortingLayerName = sortingLayerName;
                    sr.sortingOrder = sortingOrder;
                    Color c = sr.color;
                    c.a = Random.Range(alphaMin, alphaMax);
                    sr.color = c;
                }

                spawned++;
            }
        }

        Debug.Log($"RiceFieldSpawner: Spawned {spawned} stalks across {floor.Count} floor tiles.");
    }
}
