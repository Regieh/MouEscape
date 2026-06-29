using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Tilemaps;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Generates a TinyKeep-inspired dungeon: random rooms connected
/// by A* corridors driven by a Minimum Spanning Tree (with loops).
/// </summary>
public class DungeonMaster : MonoBehaviour
{
    public static DungeonMaster Instance { get; private set; }

    [Header("Map Bounds (Auto-Calculated)")]
    [Tooltip("These are calculated automatically based on Num Rooms and Room Sizes.")]
    public int mapWidth;
    public int mapHeight;

    [Header("TinyKeep Layout Info")]
    public int numRooms = 15;
    public Vector2Int roomSizeMin = new Vector2Int(8, 8);
    public Vector2Int roomSizeMax = new Vector2Int(16, 16);
    [Tooltip("0 = 1 wide, 1 = 3 wide, 2 = 5 wide")]
    public int hallwayRadius = 1; 
    [Range(0f, 1f)] public float loopProbability = 0.15f;

    [Header("References")]
    public Tilemap   myTilemap;
    public Transform player;
    public GameObject doorPrefab;
    [Tooltip("Drag your SMALL enemy prefab here.")]
    public GameObject enemyPrefab;
    [Tooltip("Drag your BIG enemy prefab here (used for hunter enemies). If empty, falls back to small enemy prefab.")]
    public GameObject bigEnemyPrefab;
    [Tooltip("Drag your Rice prefab here.")]
    public GameObject ricePrefab;

    [Header("Decorations")]
    public Tilemap   decorationTilemap;
    public TileBase  riceDecorationTile;
    [Range(0, 100)] public int ricePatchCount = 20;
    [Range(1, 10)]  public int ricePatchRadius = 3;
    
    [Header("Survival Mechanics")]
    [Range(0f, 1f)] public float initialRiceDensity = 0.5f;
    public int initialEnemyCount = 5;
    [Tooltip("Minimum world-space distance from player that enemies must spawn at. Prevents spawning on top of player.")]
    public float initialSpawnMinDistanceFromPlayer = 10f;

    [Header("Hunter Enemies")]
    [Tooltip("Number of larger hunter enemies that orbit and strike the player.")]
    public int bigHunterCount = 5;
    [Tooltip("Optional extra scale multiplier applied to big hunter instances.")]
    public float bigHunterScaleMultiplier = 1f;
    [Tooltip("Base movement speed for big hunters. Keep lower than normal enemies for dramatic circling.")]
    public float bigHunterBaseSpeed = 1.7f;

    [Header("Extra Enemy Spawn Placement")]
    [Tooltip("If enabled, spawned extras prefer rooms closer to the player, with safety bounds.")]
    public bool spawnExtrasCloserToPlayer = true;
    [Tooltip("Extras will not spawn closer than this world-space distance to the player.")]
    public float extraSpawnMinDistanceFromPlayer = 8f;
    [Tooltip("Preferred upper bound for close spawns. Falls back gracefully if no room matches.")]
    public float extraSpawnMaxDistanceFromPlayer = 26f;
    [Tooltip("0 = uniform candidate pick, 1 = strongly favors nearest valid rooms.")]
    [Range(0f, 1f)] public float extraSpawnNearBias = 0.75f;

    [Header("Tiles")]
    public UnityEngine.Tilemaps.TileBase floorTileAsset;
    public UnityEngine.Tilemaps.TileBase wallTileAsset;

    // ── internal state ────────────────────────────────────────────────
    private HashSet<Vector3Int> floorPositions = new HashSet<Vector3Int>();
    private List<RectInt>       rooms          = new List<RectInt>();
    private List<Vector3Int>    doorCells      = new List<Vector3Int>();
    private static readonly Vector3Int[] CardinalDirs =
    {
        Vector3Int.right,
        Vector3Int.up,
        Vector3Int.left,
        Vector3Int.down
    };

    // Public accessors used by Enemy.cs
    public HashSet<Vector3Int> GetFloorPositions() => floorPositions;
    public Tilemap             GetTilemap()         => myTilemap;
    public IReadOnlyList<RectInt> GetRooms()        => rooms;
    public bool                IsReady              { get; private set; }

    public int GetRoomIndexAtCell(Vector3Int cell)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            RectInt r = rooms[i];
            if (cell.x >= r.x && cell.x < r.xMax && cell.y >= r.y && cell.y < r.yMax)
                return i;
        }

        return -1;
    }

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        GenerateDungeon();

        SpawnPlayer();
        SpawnEnemy();
        SpawnBigHunters();
        SpawnDoors();
        
        SpawnInitialRice();
        SpawnRiceDecorations();

        var riceField = FindFirstObjectByType<RiceFieldSpawner>();
        if (riceField != null) riceField.Spawn();

        IsReady = true;

        if (GetComponent<GameManager>() == null)
            gameObject.AddComponent<GameManager>();

        RefreshSystems();

        Debug.Log($"DungeonMaster: TinyKeep style | {rooms.Count} rooms | {floorPositions.Count} floor tiles.");
    }

    // ─────────────────────────────────────────────────────────────────
    // TinyKeep Generation Algorithm
    // ─────────────────────────────────────────────────────────────────
    void GenerateDungeon()
    {
        myTilemap.ClearAllTiles();
        floorPositions.Clear();
        rooms.Clear();
        doorCells.Clear();

        // Dynamically calculate the map bounds to support the requested rooms optimally
        float avgRoomArea = ((roomSizeMin.x + roomSizeMax.x) / 2f) * ((roomSizeMin.y + roomSizeMax.y) / 2f);
        float totalTargetArea = numRooms * avgRoomArea;
        
        // Target a 20% room coverage density for the map to allow nice spacing
        int calculatedSide = Mathf.CeilToInt(Mathf.Sqrt(totalTargetArea * 5f));
        
        mapWidth = Mathf.Max(20, calculatedSide);
        mapHeight = Mathf.Max(20, calculatedSide);

        // 1. Generate Non-Overlapping Rooms
        int tries = 0;
        while (rooms.Count < numRooms && tries < 500)
        {
            int rw = Random.Range(roomSizeMin.x, roomSizeMax.x + 1);
            int rh = Random.Range(roomSizeMin.y, roomSizeMax.y + 1);

            // padding of 4 from edges (to allow full 3-layer wall on outer boundary)
            int rx = Random.Range(4, mapWidth - rw - 4);
            int ry = Random.Range(4, mapHeight - rh - 4);

            RectInt candidate = new RectInt(rx, ry, rw, rh);
            bool overlap = false;

            foreach (var r in rooms)
            {
                // Enforce 4-tile gap between rooms so each side gets a full 3-layer wall
                if (candidate.x < r.xMax + 4 && candidate.xMax + 4 > r.x &&
                    candidate.y < r.yMax + 4 && candidate.yMax + 4 > r.y)
                {
                    overlap = true;
                    break;
                }
            }

            if (!overlap) rooms.Add(candidate);
            tries++;
        }

        // Carve room floors
        foreach (var r in rooms)
        {
            for (int x = r.x; x < r.xMax; x++)
            {
                for (int y = r.y; y < r.yMax; y++)
                {
                    floorPositions.Add(new Vector3Int(x, y, 0));
                }
            }
        }

        if (rooms.Count < 2) return;

        // 2. Minimum Spanning Tree (Prim's Algorithm on complete graph of room centers)
        List<Vector2Int> edges = new List<Vector2Int>();
        HashSet<int> connected = new HashSet<int> { 0 };

        while (connected.Count < rooms.Count)
        {
            int bestA = -1, bestB = -1;
            float bestDist = float.MaxValue;

            foreach (int a in connected)
            {
                for (int b = 0; b < rooms.Count; b++)
                {
                    if (connected.Contains(b)) continue;

                    float dist = Vector2.Distance(RoomCenter(rooms[a]), RoomCenter(rooms[b]));
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestA >= 0)
            {
                connected.Add(bestB);
                edges.Add(new Vector2Int(bestA, bestB));
            }
        }

        // 3. Add random back-edges to create loops (so the dungeon isn't just a tree)
        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                if (Random.value < loopProbability)
                {
                    Vector2Int e1 = new Vector2Int(i, j);
                    Vector2Int e2 = new Vector2Int(j, i);
                    if (!edges.Contains(e1) && !edges.Contains(e2))
                        edges.Add(e1);
                }
            }
        }

        // 4. Carve corridors using A*
        // A* will heavily prioritize tiles already in floorPositions.
        foreach (var edge in edges)
        {
            Vector2Int start = RoomCenter(rooms[edge.x]);
            Vector2Int end   = RoomCenter(rooms[edge.y]);
            CarveAStarCorridor(start, end);
        }

        // RuleTile helper: guarantee no isolated single floor cells.
        EnsureNoSingletonCells(floorPositions);

        // 5. Build tilemap using ultra-fast batching to eliminate lag
        Vector3Int[] fPosArray = new Vector3Int[floorPositions.Count];
        TileBase[] fTileArray = new TileBase[floorPositions.Count];
        int fIdx = 0;
        foreach (var f in floorPositions)
        {
            fPosArray[fIdx] = f;
            fTileArray[fIdx] = floorTileAsset;
            fIdx++;
        }
        myTilemap.SetTiles(fPosArray, fTileArray);

        // Strip colliders from the floor so nothing snags.
        // Keep/disable Rigidbody2D instead of removing it immediately, because
        // CompositeCollider2D can depend on it at destruction time.
        var floorComp = myTilemap.GetComponent<CompositeCollider2D>();
        if (floorComp != null) Destroy(floorComp);

        var floorTc = myTilemap.GetComponent<TilemapCollider2D>();
        if (floorTc != null)
        {
#if UNITY_6000_0_OR_NEWER
            floorTc.compositeOperation = Collider2D.CompositeOperation.None;
#else
            floorTc.usedByComposite = false;
#endif
            Destroy(floorTc);
        }

        var floorRb = myTilemap.GetComponent<Rigidbody2D>();
        if (floorRb != null)
        {
            floorRb.bodyType = RigidbodyType2D.Static;
            floorRb.simulated = false;
        }

        // CREATE A SEPARATE WALL TILEMAP dynamically to guarantee rendering ON TOP!
        Transform gridParent = myTilemap.transform.parent;
        Transform existingWallMap = gridParent.Find("Dynamic_Wall_Tilemap");
        GameObject wallGo = existingWallMap != null ? existingWallMap.gameObject : new GameObject("Dynamic_Wall_Tilemap");
        wallGo.transform.SetParent(gridParent, false);
        
        Tilemap wallTilemap = wallGo.GetComponent<Tilemap>();
        if (wallTilemap == null) wallTilemap = wallGo.AddComponent<Tilemap>();
        
        TilemapRenderer wallRenderer = wallGo.GetComponent<TilemapRenderer>();
        if (wallRenderer == null) wallRenderer = wallGo.AddComponent<TilemapRenderer>();
        
        // Force rendering above the floor!
        TilemapRenderer floorRenderer = myTilemap.GetComponent<TilemapRenderer>();
        wallRenderer.sortingLayerID = floorRenderer.sortingLayerID;
        wallRenderer.sortingOrder = floorRenderer.sortingOrder + 1;
        wallRenderer.material = floorRenderer.material; // Match lighting

        wallTilemap.ClearAllTiles();

        // Add walls 3-tiles thick around the floors (gives RuleTile more context)
        HashSet<Vector3Int> wallPositions = new HashSet<Vector3Int>();
        foreach (var f in floorPositions)
        {
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    Vector3Int p = new Vector3Int(f.x + dx, f.y + dy, 0);
                    if (!floorPositions.Contains(p))
                    {
                        wallPositions.Add(p);
                    }
                }
            }
        }

        // RuleTile helper: guarantee wall cells participate in at least one 2x2 block.
        EnsureCellsHaveTwoByTwoSupport(wallPositions);

        Vector3Int[] wPosArray = new Vector3Int[wallPositions.Count];
        TileBase[] wTileArray = new TileBase[wallPositions.Count];
        int wIdx = 0;
        foreach (var w in wallPositions)
        {
            wPosArray[wIdx] = w;
            wTileArray[wIdx] = wallTileAsset;
            wIdx++;
        }
        wallTilemap.SetTiles(wPosArray, wTileArray);

        // Add Collider physics explicitly to the Wall layer only!
        TilemapCollider2D wtc = wallTilemap.GetComponent<TilemapCollider2D>();
        if (wtc == null) wtc = wallGo.AddComponent<TilemapCollider2D>();

    #if UNITY_6000_0_OR_NEWER
        wtc.compositeOperation = Collider2D.CompositeOperation.Merge;
    #else
        wtc.usedByComposite = true;
    #endif
        
        CompositeCollider2D wcomp = wallTilemap.GetComponent<CompositeCollider2D>();
        if (wcomp == null) wcomp = wallGo.AddComponent<CompositeCollider2D>();
        
        // Add Shadow Caster to block light
        ShadowCaster2D sc = wallGo.GetComponent<ShadowCaster2D>();
        if (sc == null) sc = wallGo.AddComponent<ShadowCaster2D>();
        sc.selfShadows = false; // Prevents "bleeding" inside the wall itself
        
        Rigidbody2D wrb = wallTilemap.GetComponent<Rigidbody2D>();
        if (wrb == null) wrb = wallGo.AddComponent<Rigidbody2D>();
        wrb.bodyType = RigidbodyType2D.Static; // STOP GRAVITY!
        wrb.simulated = true;

        // Add CompositeShadowCaster2D for perfect, lag-free tilemap shadows
        CompositeShadowCaster2D wsc = wallGo.GetComponent<CompositeShadowCaster2D>();
        if (wsc == null) wsc = wallGo.AddComponent<CompositeShadowCaster2D>();
        
        wallTilemap.CompressBounds();
        myTilemap.CompressBounds();
        // Door placements are now handled directly inside CarveAStarCorridor 
        // to support huge 3-wide generic hallways perfectly!
    }

    void SpawnRiceDecorations()
    {
        if (decorationTilemap == null || riceDecorationTile == null) return;
        
        decorationTilemap.ClearAllTiles();
        List<Vector3Int> floors = new List<Vector3Int>(floorPositions);
        if (floors.Count == 0) return;

        // Ensure decorations render above floor but below walls/player
        TilemapRenderer decRenderer = decorationTilemap.GetComponent<TilemapRenderer>();
        if (decRenderer != null)
        {
            decRenderer.sortingOrder = myTilemap.GetComponent<TilemapRenderer>().sortingOrder + 1;
        }

        HashSet<Vector3Int> usedTiles = new HashSet<Vector3Int>();

        for (int i = 0; i < ricePatchCount; i++)
        {
            // Pick a random seed floor tile
            Vector3Int center = floors[Random.Range(0, floors.Count)];
            
            // Randomize patch size for variety
            int radius = Random.Range(1, ricePatchRadius + 1);
            
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    // Check circular radius for organic clustering
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Vector3Int p = new Vector3Int(center.x + dx, center.y + dy, 0);
                        
                        // Only place on floor and don't double-place
                        if (floorPositions.Contains(p) && !usedTiles.Contains(p))
                        {
                            // 80% chance to place to keep it slightly scattered/natural
                            if (Random.value < 0.8f)
                            {
                                decorationTilemap.SetTile(p, riceDecorationTile);
                                usedTiles.Add(p);
                            }
                        }
                    }
                }
            }
        }

        // RuleTile helper: guarantee no isolated decoration cells.
        EnsureNoSingletonTiles(decorationTilemap, riceDecorationTile);
    }

    private void EnsureNoSingletonCells(HashSet<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return;

        var toAdd = new List<Vector3Int>();
        foreach (var cell in cells)
        {
            bool hasNeighbor = false;
            for (int i = 0; i < CardinalDirs.Length; i++)
            {
                if (cells.Contains(cell + CardinalDirs[i]))
                {
                    hasNeighbor = true;
                    break;
                }
            }

            if (hasNeighbor) continue;

            for (int i = 0; i < CardinalDirs.Length; i++)
            {
                Vector3Int candidate = cell + CardinalDirs[i];
                if (!cells.Contains(candidate))
                {
                    toAdd.Add(candidate);
                    break;
                }
            }
        }

        for (int i = 0; i < toAdd.Count; i++)
            cells.Add(toAdd[i]);
    }

    private void EnsureNoSingletonTiles(Tilemap tilemap, TileBase fallbackTile)
    {
        if (tilemap == null) return;

        tilemap.CompressBounds();
        BoundsInt bounds = tilemap.cellBounds;
        var occupied = new HashSet<Vector3Int>();

        foreach (var pos in bounds.allPositionsWithin)
        {
            if (tilemap.HasTile(pos)) occupied.Add(pos);
        }

        if (occupied.Count == 0) return;

        var toAdd = new List<Vector3Int>();
        var tilesToAdd = new List<TileBase>();

        foreach (var cell in occupied)
        {
            bool hasNeighbor = false;
            for (int i = 0; i < CardinalDirs.Length; i++)
            {
                if (occupied.Contains(cell + CardinalDirs[i]))
                {
                    hasNeighbor = true;
                    break;
                }
            }

            if (hasNeighbor) continue;

            TileBase sourceTile = tilemap.GetTile(cell);
            TileBase tileToUse = sourceTile != null ? sourceTile : fallbackTile;
            if (tileToUse == null) continue;

            for (int i = 0; i < CardinalDirs.Length; i++)
            {
                Vector3Int candidate = cell + CardinalDirs[i];
                if (occupied.Contains(candidate)) continue;

                toAdd.Add(candidate);
                tilesToAdd.Add(tileToUse);
                occupied.Add(candidate);
                break;
            }
        }

        for (int i = 0; i < toAdd.Count; i++)
            tilemap.SetTile(toAdd[i], tilesToAdd[i]);
    }

    private void EnsureCellsHaveTwoByTwoSupport(HashSet<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return;

        // Iterate a few times so newly added cells are also validated.
        for (int pass = 0; pass < 3; pass++)
        {
            var additions = new List<Vector3Int>();
            var pending = new HashSet<Vector3Int>();

            foreach (var cell in cells)
            {
                if (IsInAnyTwoByTwo(cells, cell)) continue;

                // Four possible 2x2 anchors that can contain this cell.
                Vector3Int[] anchors =
                {
                    cell,
                    cell + Vector3Int.left,
                    cell + Vector3Int.down,
                    cell + Vector3Int.left + Vector3Int.down
                };

                int bestMissingCount = int.MaxValue;
                Vector3Int[] bestMissing = null;

                for (int i = 0; i < anchors.Length; i++)
                {
                    Vector3Int a = anchors[i];
                    Vector3Int[] block =
                    {
                        a,
                        a + Vector3Int.right,
                        a + Vector3Int.up,
                        a + Vector3Int.right + Vector3Int.up
                    };

                    var missing = new List<Vector3Int>();
                    for (int b = 0; b < block.Length; b++)
                    {
                        if (!cells.Contains(block[b]) && !pending.Contains(block[b]))
                            missing.Add(block[b]);
                    }

                    if (missing.Count < bestMissingCount)
                    {
                        bestMissingCount = missing.Count;
                        bestMissing = missing.ToArray();
                        if (bestMissingCount == 0) break;
                    }
                }

                if (bestMissing == null) continue;
                for (int i = 0; i < bestMissing.Length; i++)
                {
                    additions.Add(bestMissing[i]);
                    pending.Add(bestMissing[i]);
                }
            }

            if (additions.Count == 0) break;
            for (int i = 0; i < additions.Count; i++)
                cells.Add(additions[i]);
        }
    }

    private bool IsInAnyTwoByTwo(HashSet<Vector3Int> cells, Vector3Int cell)
    {
        Vector3Int[] anchors =
        {
            cell,
            cell + Vector3Int.left,
            cell + Vector3Int.down,
            cell + Vector3Int.left + Vector3Int.down
        };

        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3Int a = anchors[i];
            if (cells.Contains(a) &&
                cells.Contains(a + Vector3Int.right) &&
                cells.Contains(a + Vector3Int.up) &&
                cells.Contains(a + Vector3Int.right + Vector3Int.up))
                return true;
        }

        return false;
    }

    Vector2Int RoomCenter(RectInt r)
    {
        return new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);
    }

    bool IsInsideRoom(Vector2Int p)
    {
        foreach (var r in rooms)
        {
            if (p.x >= r.x && p.x < r.xMax && p.y >= r.y && p.y < r.yMax)
                return true;
        }
        return false;
    }

    struct AStarNode { public float f, g; public Vector2Int pos; public Vector2Int parent; }

    void CarveAStarCorridor(Vector2Int start, Vector2Int end)
    {
        var open = new List<AStarNode>();
        var closed = new HashSet<Vector2Int>();
        var nodes = new Dictionary<Vector2Int, AStarNode>();

        open.Add(new AStarNode { pos = start, f = 0, g = 0, parent = start });
        nodes[start] = open[0];

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (open.Count > 0)
        {
            // pop lowest f
            int bestIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bestIdx].f) bestIdx = i;

            var curr = open[bestIdx];
            open.RemoveAt(bestIdx);

            if (curr.pos == end)
            {
                // Reconstruct and carve
                Vector2Int c = curr.pos;
                while (c != start)
                {
                    Vector2Int p = nodes[c].parent;

                    // Place a door immediately when crossing from outside a room to inside a room
                    if (IsInsideRoom(c) && !IsInsideRoom(p))
                        doorCells.Add(new Vector3Int(c.x, c.y, 0));
                    else if (!IsInsideRoom(c) && IsInsideRoom(p))
                        doorCells.Add(new Vector3Int(p.x, p.y, 0));

                    // Carve thickened corridors (hallwayRadius)
                    for (int dx = -hallwayRadius; dx <= hallwayRadius; dx++)
                    {
                        for (int dy = -hallwayRadius; dy <= hallwayRadius; dy++)
                        {
                            floorPositions.Add(new Vector3Int(c.x + dx, c.y + dy, 0));
                        }
                    }

                    c = nodes[c].parent;
                }
                return;
            }

            if (closed.Contains(curr.pos)) continue;
            closed.Add(curr.pos);

            foreach (var d in dirs)
            {
                Vector2Int nb = curr.pos + d;
                
                // Keep within grid
                if (nb.x < 1 || nb.y < 1 || nb.x >= mapWidth - 1 || nb.y >= mapHeight - 1) continue;
                if (closed.Contains(nb)) continue;

                // Priority heuristic: reuse floors cheaply! 
                // Cost = 1 if floor exists, cost = 6 if punching through solid wall.
                float stepCost = floorPositions.Contains(new Vector3Int(nb.x, nb.y, 0)) ? 1f : 6f;
                
                // Add penalty for changing direction (makes corridors straighter!)
                if (curr.pos != start && (curr.pos - curr.parent) != d) stepCost += 2f;

                float newG = curr.g + stepCost;
                float newH = Mathf.Abs(end.x - nb.x) + Mathf.Abs(end.y - nb.y); // Manhattan
                float newF = newG + newH;

                if (!nodes.ContainsKey(nb) || newG < nodes[nb].g)
                {
                    var nbNode = new AStarNode { pos = nb, g = newG, f = newF, parent = curr.pos };
                    nodes[nb] = nbNode;
                    open.Add(nbNode);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Spawning
    // ─────────────────────────────────────────────────────────────────
    void SpawnPlayer()
    {
        if (player == null || rooms.Count == 0) return;

        // Player starts in the first room
        Vector2Int center = RoomCenter(rooms[0]);
        Vector3 worldPos = new Vector3(center.x + 0.5f, center.y + 0.5f, 0f);

        // Immediate transport and sync
        player.position = worldPos;

        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.Sleep();
            rb.position       = worldPos;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.WakeUp();
            Physics2D.SyncTransforms();
        }

        Debug.Log($"DungeonMaster: Player spawned at world {worldPos} (Room 0)");
    }

    void SpawnEnemy()
    {
        if (rooms.Count < 2 || player == null || enemyPrefab == null) return;

        // Create a list of rooms sorted by distance from the player (descending)
        List<RectInt> sortedRooms = new List<RectInt>(rooms);
        sortedRooms.Sort((a, b) =>
        {
            float distA = Vector2.Distance(RoomCenter(a), player.position);
            float distB = Vector2.Distance(RoomCenter(b), player.position);
            return distB.CompareTo(distA);
        });

        // Spawn enemies in the top N furthest rooms, respecting minimum distance
        int spawned = 0;
        for (int i = 0; i < sortedRooms.Count && spawned < initialEnemyCount; i++)
        {
            Vector2Int center = RoomCenter(sortedRooms[i]);
            Vector3 worldPos = new Vector3(center.x + 0.5f, center.y + 0.5f, 0f);
            
            float distToPlayer = Vector3.Distance(worldPos, player.position);
            if (distToPlayer < initialSpawnMinDistanceFromPlayer)
                continue; // Skip this room, too close to player
            
            Instantiate(enemyPrefab, worldPos, Quaternion.identity);
            spawned++;

            Debug.Log($"DungeonMaster: Initial enemy {spawned} spawned at {worldPos} (distance={distToPlayer:F1})");
        }
    }

    void SpawnBigHunters()
    {
        GameObject hunterPrefab = bigEnemyPrefab != null ? bigEnemyPrefab : enemyPrefab;
        if (hunterPrefab == null || rooms.Count < 2 || player == null || bigHunterCount <= 0) return;

        List<RectInt> sortedRooms = new List<RectInt>(rooms);
        sortedRooms.Sort((a, b) =>
        {
            float distA = Vector2.Distance(RoomCenter(a), player.position);
            float distB = Vector2.Distance(RoomCenter(b), player.position);
            return distB.CompareTo(distA);
        });

        int spawned = 0;
        for (int i = 0; i < sortedRooms.Count && spawned < bigHunterCount; i++)
        {
            Vector2Int center = RoomCenter(sortedRooms[i]);
            Vector3 worldPos = new Vector3(center.x + 0.5f, center.y + 0.5f, 0f);
            
            float distToPlayer = Vector3.Distance(worldPos, player.position);
            if (distToPlayer < initialSpawnMinDistanceFromPlayer)
                continue; // Skip this room, too close to player

            GameObject go = Instantiate(hunterPrefab, worldPos, Quaternion.identity);
            if (bigHunterScaleMultiplier > 0f && !Mathf.Approximately(bigHunterScaleMultiplier, 1f))
                go.transform.localScale *= bigHunterScaleMultiplier;

            Enemy e = go.GetComponent<Enemy>();
            if (e != null)
            {
                if (bigHunterBaseSpeed > 0f) e.moveSpeed = bigHunterBaseSpeed;
                e.ConfigureAsHunter();
            }
            else
            {
                Debug.LogWarning("DungeonMaster: Big hunter prefab has no Enemy component.");
            }

            Debug.Log($"DungeonMaster: Big hunter {spawned + 1} spawned at {worldPos} (distance={distToPlayer:F1})");
            spawned++;
        }
    }

    /// <summary>
    /// Called by HorrorAtmosphere to procedurally add more enemies during play.
    /// Picks the room furthest from the player so the spawn is fair.
    /// </summary>
    public void SpawnExtraEnemy(float moveSpeed = -1f)
    {
        if (enemyPrefab == null || rooms.Count < 2) return;

        // Find the room whose centre is furthest from the player
        Transform player = PlayerController.Instance != null
            ? PlayerController.Instance.transform
            : null;

        int targetRoom = rooms.Count - 1; // fallback
        if (player != null)
        {
            var roomDistances = new List<(int index, float dist)>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Vector2Int c    = RoomCenter(rooms[i]);
                Vector3    wPos = new Vector3(c.x + 0.5f, c.y + 0.5f, 0f);
                float      d    = Vector3.Distance(wPos, player.position);
                roomDistances.Add((i, d));
            }

            roomDistances.Sort((a, b) => a.dist.CompareTo(b.dist));

            if (spawnExtrasCloserToPlayer)
            {
                var candidates = new List<(int index, float dist)>();
                for (int i = 0; i < roomDistances.Count; i++)
                {
                    float d = roomDistances[i].dist;
                    if (d >= extraSpawnMinDistanceFromPlayer && d <= extraSpawnMaxDistanceFromPlayer)
                        candidates.Add(roomDistances[i]);
                }

                if (candidates.Count > 0)
                {
                    int nearestPool = Mathf.Max(1, Mathf.CeilToInt(candidates.Count * Mathf.Lerp(1f, 0.35f, extraSpawnNearBias)));
                    int pick = Random.Range(0, nearestPool);
                    targetRoom = candidates[pick].index;
                }
                else
                {
                    // No room in range: pick the nearest room still outside the minimum distance.
                    for (int i = 0; i < roomDistances.Count; i++)
                    {
                        if (roomDistances[i].dist >= extraSpawnMinDistanceFromPlayer)
                        {
                            targetRoom = roomDistances[i].index;
                            break;
                        }
                    }
                }
            }
            else
            {
                float bestDist = -1f;
                for (int i = 0; i < roomDistances.Count; i++)
                {
                    if (roomDistances[i].dist > bestDist)
                    {
                        bestDist = roomDistances[i].dist;
                        targetRoom = roomDistances[i].index;
                    }
                }
            }
        }

        Vector2Int center  = RoomCenter(rooms[targetRoom]);
        Vector3    worldPos = new Vector3(center.x + 0.5f, center.y + 0.5f, 0f);

        GameObject go = Instantiate(enemyPrefab, worldPos, Quaternion.identity);

        // If a speed is provided, override prefab speed for this spawn.
        Enemy e = go.GetComponent<Enemy>();
        if (e != null && moveSpeed > 0f) e.moveSpeed = moveSpeed;

        float spawnedSpeed = e != null ? e.moveSpeed : moveSpeed;
        Debug.Log($"DungeonMaster: Extra enemy spawned at {worldPos} | speed={spawnedSpeed}");
    }

    void SpawnDoors()
    {
        if (doorPrefab == null || doorCells.Count == 0) return;

        HashSet<Vector3Int> used = new HashSet<Vector3Int>();
        List<Door> spawnedDoors = new List<Door>();
        foreach (var dc in doorCells)
        {
            // 25% chance to actually place a physical door in a candidate spot
            if (Random.value > 0.25f) continue;

            // Space doors out slightly to avoid stacking in long 1-wide tunnels
            bool tooClose = false;
            foreach (var u in used)
            {
                if (Mathf.Abs(dc.x - u.x) + Mathf.Abs(dc.y - u.y) < 3)
                {
                    tooClose = true; break;
                }
            }
            if (tooClose) continue;

            GameObject doorGO = Instantiate(doorPrefab, new Vector3(dc.x + 0.5f, dc.y + 0.5f, 0f), Quaternion.identity);
            Door doorComp = doorGO.GetComponent<Door>();
            if (doorComp != null) spawnedDoors.Add(doorComp);
            used.Add(dc);
        }

        // Shuffle and pair doors into wormhole links.
        for (int i = 0; i < spawnedDoors.Count; i++)
        {
            int swapIndex = Random.Range(i, spawnedDoors.Count);
            (spawnedDoors[i], spawnedDoors[swapIndex]) = (spawnedDoors[swapIndex], spawnedDoors[i]);
        }

        int pairs = 0;
        for (int i = 0; i + 1 < spawnedDoors.Count; i += 2)
        {
            Door a = spawnedDoors[i];
            Door b = spawnedDoors[i + 1];
            a.SetLinkedDoor(b);
            b.SetLinkedDoor(a);
            pairs++;
        }

        int unlinked = spawnedDoors.Count % 2;
        Debug.Log($"DungeonMaster: {used.Count} doors placed | {pairs} wormhole pairs linked" + (unlinked == 1 ? " | 1 unpaired door" : ""));
    }

    public void SpawnInitialRice()
    {
        foreach (var cell in floorPositions)
        {
            if (Random.value <= initialRiceDensity)
            {
                SpawnRiceAt(cell);
            }
        }
    }

    public void SpawnSingleRice()
    {
        if (floorPositions.Count == 0) return;

        // Pick a random floor tile
        Vector3Int[] floors = new Vector3Int[floorPositions.Count];
        floorPositions.CopyTo(floors);
        Vector3Int cell = floors[Random.Range(0, floors.Length)];
        SpawnRiceAt(cell);
    }

    private void SpawnRiceAt(Vector3Int cell)
    {
        Vector3 pos = myTilemap.GetCellCenterWorld(cell);

        if (ricePrefab == null)
        {
            Debug.LogWarning("DungeonMaster: ricePrefab is not assigned.");
            return;
        }

        Quaternion rot = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        Instantiate(ricePrefab, pos, rot);
    }

    void RefreshSystems()
    {
        // Physics is now completely handled dynamically on the separate Wall Tilemap during generation!
        // We only need to refresh shadow casters if they exist on the floor.
        ShadowCaster2D sc = myTilemap.GetComponent<ShadowCaster2D>();
        if (sc != null) { sc.enabled = false; sc.enabled = true; }
    }
}