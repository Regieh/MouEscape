using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

/// <summary>
/// Snake enemy: patrols the dungeon via Prim's-MST waypoints;
/// chases the player (BFS/A*) when it can "see" them.
/// Uses a Kinematic Rigidbody2D so Physics doesn't fight the movement.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(LineRenderer))]
public class Enemy : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("References")]
    public Transform player;
    public int segmentCount = 20;

    [Header("Movement")]
    public float moveSpeed          = 2f;
    public float segmentSpacing     = 0.45f;
    public float pathRefreshRate    = 1.5f;

    [Header("Slither Animation")]
    public float slitherAmplitude     = 0.2f;
    public float slitherFrequency     = 1.2f;
    public float slitherSpeed         = 4f;
    
    [Header("Visuals")]
    public float headOffset       = 0.2f; // Extra gap between head and start of line
    [Tooltip("Optional anchor for line point 0. If null, uses this transform.")]
    public Transform lineStartAnchor;
    [Tooltip("Used only when no anchor is assigned. Local-space offset for the auto-created anchor.")]
    public Vector3 autoLineStartLocalOffset = Vector3.zero;
    [Tooltip("Keeps line point 0 locked to the head/anchor each frame.")]
    public bool pinFirstPointToHead = true;
    public Color bodyColor        = Color.white;
    [Tooltip("Optional override material for the body line.")]
    public Material bodyMaterial;
    [Tooltip("If true uses URP 2D Sprite-Lit shader, else uses Sprites/Default.")]
    public bool useLitShader = true;
    public AnimationCurve bodyWidthCurve;

    [Tooltip("If sprite faces Right, set to 0. If it faces Up, set to -90.")]
    public float spriteRotationOffset = 0f;

    [Header("Patrol")]
    public int patrolWaypointCount  = 10;
    [Tooltip("If enabled, this enemy only chases when player is in the same generated room zone.")]
    public bool chaseOnlyInSameRoom = true;

    public enum BehaviorProfile
    {
        RoomPatrol,
        HunterToying
    }

    [Header("Behavior")]
    public BehaviorProfile behaviorProfile = BehaviorProfile.RoomPatrol;

    [Header("Hunter Toying")]
    [Tooltip("Orbit radius range around the player before a strike.")]
    public Vector2 hunterOrbitRadiusRange = new Vector2(2.5f, 4.5f);
    [Tooltip("Base angular speed while circling around the player.")]
    public float hunterOrbitAngularSpeed = 1.1f;
    [Tooltip("How much faster the hunter moves during strike bursts.")]
    public float hunterStrikeSpeedMultiplier = 2f;
    [Tooltip("Random time gap between strike bursts (seconds).")]
    public Vector2 hunterStrikeIntervalRange = new Vector2(2f, 6f);
    [Tooltip("Random duration of each strike burst (seconds).")]
    public Vector2 hunterStrikeDurationRange = new Vector2(0.35f, 1f);

    [Header("Debug")]
    public bool showDebugGUI  = false;   // toggle on-screen HUD
    public bool showDebugPath = false;   // toggle path gizmos

    // ── Static Enemy Registry ─────────────────────────────────────────────────
    // Every live Enemy registers itself here automatically so HorrorAtmosphere
    // (or any other system) can control them all without Inspector wiring.
    public static readonly System.Collections.Generic.List<Enemy> AllEnemies
        = new System.Collections.Generic.List<Enemy>();

    // ── Private ──────────────────────────────────────────────────────────────
    private Rigidbody2D         rb;
    private DungeonMaster       dm;
    private PlayerController    pc;
    private Tilemap             map;
    private HashSet<Vector3Int> floor;          // direct ref to DungeonMaster's set

    // Body
    public LineRenderer             line;
    private readonly List<Vector3>   posHistory = new List<Vector3>();

    // Path
    private List<Vector3Int> path      = new List<Vector3Int>();
    private int              pathIndex = 0;

    // Patrol
    private List<Vector3Int> waypoints = new List<Vector3Int>();
    private int              wpIndex   = 0;
    private int              homeRoomIndex = -1;

    // Hunter runtime state
    private float hunterOrbitRadius;
    private float hunterOrbitAngle;
    private float hunterOrbitDirection = 1f;
    private float hunterOrbitSpeedRuntime;
    private float hunterStrikeSpeedRuntime;
    private float hunterNextStrikeTime;
    private float hunterStrikeUntilTime;
    private float baseMoveSpeed;

    private enum State { Patrol, Chase }
    private State state = State.Patrol;

    // Debug HUD text
    private string debugText = "Initialising...";

    private const string AutoLineAnchorName = "LineStartAnchor_Auto";

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    void Awake()
    {
        rb               = GetComponent<Rigidbody2D>();
        rb.gravityScale  = 0f;
        rb.bodyType      = RigidbodyType2D.Kinematic;
        
        rb.freezeRotation = false;
        
        EnsureLineStartAnchor();
    }

    private void EnsureLineStartAnchor()
    {
        if (lineStartAnchor != null) return;

        Transform existing = transform.Find(AutoLineAnchorName);
        if (existing != null)
        {
            lineStartAnchor = existing;
            lineStartAnchor.localPosition = autoLineStartLocalOffset;
            return;
        }

        GameObject anchor = new GameObject(AutoLineAnchorName);
        anchor.transform.SetParent(transform, false);
        anchor.transform.localPosition = autoLineStartLocalOffset;
        anchor.transform.localRotation = Quaternion.identity;
        anchor.transform.localScale = Vector3.one;
        lineStartAnchor = anchor.transform;
    }

    void Start()
    {
        if (PlayerController.Instance != null)
            player = PlayerController.Instance.transform;
        else
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }
        if (player != null) pc = player.GetComponent<PlayerController>();

        if (player == null) { debugText = "ERROR: Player not found!"; Debug.LogError("Enemy Start: player is null"); return; }

        // ── DungeonMaster ────────────────────────────────────────────────────
        dm = FindFirstObjectByType<DungeonMaster>();
        if (dm == null) { debugText = "ERROR: DungeonMaster missing!"; Debug.LogError("Enemy Start: DungeonMaster is null"); return; }

        map   = dm.GetTilemap();
        floor = dm.GetFloorPositions();

        if (map == null)   { debugText = "ERROR: Tilemap is null!";          Debug.LogError("Enemy Start: Tilemap null"); return; }
        if (floor == null || floor.Count == 0) { debugText = "ERROR: No floor tiles!"; Debug.LogError($"Enemy Start: floor empty ({floor?.Count})"); return; }

        Debug.Log($"Enemy Start OK | floor tiles={floor.Count}");

        baseMoveSpeed = moveSpeed;
        if (behaviorProfile == BehaviorProfile.HunterToying)
            RandomizeHunterHabit();

        // ── Snap to floor ────────────────────────────────────────────────────
        Vector3Int spawnCell = NearestFloor(map.WorldToCell(transform.position));
        transform.position   = CellCenter(spawnCell);
        homeRoomIndex = dm.GetRoomIndexAtCell(spawnCell);

        // ── Head sprite ──────────────────────────────────────────────────────
        // Visual setup is prefab-driven now (sprite/material/sorting come from prefab).
        var headSr = GetComponent<SpriteRenderer>();
        if (headSr == null)
        {
            Debug.LogError("Enemy: Missing SpriteRenderer on prefab.");
            enabled = false;
            return;
        }

        // ── Body ────────────────────────────────────────────────────────────
        // Prefill history so it doesn't snap at spawn
        for (int i = 0; i < segmentCount * 10; i++)
        {
            posHistory.Add(transform.position);
        }

        // ── Patrol route ─────────────────────────────────────────────────────
        BuildWaypoints();

        // ── Register in global registry ──────────────────────────────────────
        if (!AllEnemies.Contains(this)) AllEnemies.Add(this);

        // ── Start AI loop ────────────────────────────────────────────────────
        StartCoroutine(AILoop());

        debugText = $"Ready | floor={floor.Count} | wps={waypoints.Count}";
    }

    void FixedUpdate()
    {
        if (map == null) return;

        // Move head along path
        if (path.Count > 0 && pathIndex < path.Count)
        {
            Vector2 target = CellCenter(path[pathIndex]);
            Vector2 next   = Vector2.MoveTowards(rb.position, target, moveSpeed * Time.fixedDeltaTime);
            
            // Rotate the head instantly towards the target
            Vector2 dir = target - rb.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                rb.MoveRotation(angle + spriteRotationOffset);
            }

            rb.MovePosition(next);

            if (Vector2.Distance(rb.position, target) < 0.08f)
                pathIndex++;
        }
    }

    void LateUpdate()
    {
        UpdateSpine();
        UpdateLineRenderer();

        // Draw path in Scene view
        if (showDebugPath && path.Count > 0)
        {
            for (int i = pathIndex; i < path.Count - 1; i++)
                Debug.DrawLine(CellCenter(path[i]), CellCenter(path[i + 1]), Color.cyan);

            if (pathIndex < path.Count)
                Debug.DrawLine(transform.position, CellCenter(path[pathIndex]), Color.yellow);
        }
    }

    private void UpdateSpine()
    {
        // Record head history finely (every 0.05f units) to perfectly trace structural corners
        if (Vector3.Distance(transform.position, posHistory[0]) >= 0.05f)
        {
            posHistory.Insert(0, transform.position);

            // Prevent memory leaks by capping the structural list size
            if (posHistory.Count > segmentCount * 10 + 20)
                posHistory.RemoveAt(posHistory.Count - 1);
        }
    }

    private void UpdateLineRenderer()
    {
        if (line == null) return;

        int count = Mathf.Max(1, segmentCount);
        line.positionCount = count;
        
        // Tiling frequency: adjusts how many scales appear along the body
        line.textureScale = new Vector2(segmentCount * 0.2f, 1);

        Vector3 headPos = transform.position;
        Vector3 startPos = lineStartAnchor != null ? lineStartAnchor.position : headPos;
        Vector3 forward = transform.right; // Changed from transform.up to match the sprite's forward direction

        for (int i = 0; i < count; i++)
        {
            if (pinFirstPointToHead && i == 0)
            {
                line.SetPosition(0, startPos);
                continue;
            }

            int visualIndex = pinFirstPointToHead ? i - 1 : i;
            float targetDist = Mathf.Max(0f, headOffset + (visualIndex * segmentSpacing));
            
            // 1. Get History Position (where the head was)
            Vector3 historyPos = GetPointAtDistance(targetDist, out Vector3 historyDir);
            
            // 2. Get Rigid Position (exactly behind the current head angle)
            Vector3 rigidPos = startPos - (forward * targetDist);
            
            // 3. Dynamic Blend: Points 0-5 blend between Rigid and History
            float blendWeight = 0f;
            int blendRange = 6; 
            if (visualIndex < blendRange)
            {
                blendWeight = 1.0f - ((float)visualIndex / blendRange);
                blendWeight = Mathf.SmoothStep(0, 1, blendWeight);
            }
            
            Vector3 finalPos = Vector3.Lerp(historyPos, rigidPos, blendWeight);
            Vector3 finalDir = Vector3.Slerp(historyDir, forward, blendWeight);
            
            // Tapered slither with "Tail Whiplash"
            float tWiggle = (float)i / Mathf.Max(1, count - 1);
            Vector3 perp = new Vector3(-finalDir.y, finalDir.x, 0); 
            
            // Whiplash: higher intensity/frequency at the tail
            float whiplash = Mathf.Lerp(1f, 1.4f, tWiggle);
            float wave = Mathf.Sin(Time.time * slitherSpeed + (i * slitherFrequency * whiplash)) * (slitherAmplitude * tWiggle);
            
            line.SetPosition(i, finalPos + (perp * wave));
        }
    }

    /// <summary>
    /// Helper to find a position at a specific distance along the recorded path history.
    /// </summary>
    private Vector3 GetPointAtDistance(float dist, out Vector3 direction)
    {
        if (posHistory.Count == 0) { direction = transform.right; return transform.position; }
        if (dist <= 0) { direction = transform.right; return posHistory[0]; }

        float accumulated = 0f;
        Vector3 last = transform.position;

        for (int i = 0; i < posHistory.Count; i++)
        {
            float d = Vector3.Distance(last, posHistory[i]);
            if (accumulated + d >= dist)
            {
                float t = (dist - accumulated) / (d > 0 ? d : 0.001f);
                direction = (posHistory[i] - last).normalized;
                if (direction.sqrMagnitude < 0.01f) direction = transform.right;
                return Vector3.Lerp(last, posHistory[i], t);
            }
            accumulated += d;
            last = posHistory[i];
        }

        direction = transform.right;
        return posHistory[posHistory.Count - 1];
    }


    // =========================================================================
    // AI COROUTINE  –  recalculates path at a fixed rate
    // =========================================================================

    private IEnumerator AILoop()
    {
        yield return new WaitForSeconds(0.5f);   // let dungeon fully settle

        State lastState = state;

        while (true)
        {
            Vector3Int fromCell = NearestFloor(map.WorldToCell(transform.position));
            Vector3Int playerCell = BestPlayerCell();

            int enemyRoom = dm.GetRoomIndexAtCell(fromCell);
            int playerRoom = dm.GetRoomIndexAtCell(playerCell);

            bool shouldChase;
            if (behaviorProfile == BehaviorProfile.HunterToying)
            {
                shouldChase = true;
            }
            else
            {
                shouldChase = !chaseOnlyInSameRoom ||
                              (enemyRoom >= 0 && playerRoom >= 0 && enemyRoom == playerRoom);
            }

            state = shouldChase ? State.Chase : State.Patrol;

            if (state == State.Patrol && lastState == State.Chase)
            {
                // Player just hid! Immediately forget the current pursuit path
                path.Clear();
                pathIndex = 0;
            }

            Vector3Int toCell;

            if (state == State.Chase)
            {
                if (behaviorProfile == BehaviorProfile.HunterToying)
                {
                    bool striking = UpdateHunterStrikeState();
                    moveSpeed = striking ? hunterStrikeSpeedRuntime : baseMoveSpeed;

                    if (striking)
                    {
                        toCell = playerCell;
                        debugText = $"HUNTER STRIKE x{moveSpeed / Mathf.Max(0.01f, baseMoveSpeed):F2} → {toCell}";
                    }
                    else
                    {
                        toCell = GetHunterOrbitCell();
                        debugText = $"HUNTER ORBIT r={hunterOrbitRadius:F1} → {toCell}";
                    }
                }
                else
                {
                    moveSpeed = baseMoveSpeed;
                    toCell = playerCell;
                    debugText = $"CHASE room={enemyRoom} → {toCell}";
                }
            }
            else
            {
                moveSpeed = baseMoveSpeed;

                // Advance patrol waypoint when path is exhausted or we need a new destination
                if (pathIndex >= path.Count && waypoints.Count > 0)
                {
                    int bestWp = wpIndex;
                    float maxDistToPlayer = -1f;

                    // Sample random waypoints and pick one generally far away from the player
                    for (int i = 0; i < 15; i++)
                    {
                        int rWp = Random.Range(0, waypoints.Count);
                        float pDist = Vector3.Distance(player.position, CellCenter(waypoints[rWp]));
                        
                        // Add some noise so it's not perfectly homing to the exact furthest corner
                        pDist += Random.Range(-5f, 15f);

                        if (pDist > maxDistToPlayer)
                        {
                            maxDistToPlayer = pDist;
                            bestWp = rWp;
                        }
                    }
                    wpIndex = bestWp;
                }

                toCell = waypoints.Count > 0 ? waypoints[wpIndex] : fromCell;
                debugText = $"PATROL room={homeRoomIndex} wp={wpIndex}/{waypoints.Count} → {toCell}";
            }

            lastState = state;

            if (fromCell != toCell)
            {
                List<Vector3Int> newPath = AStar(fromCell, toCell);
                if (newPath.Count > 0)
                {
                    path      = newPath;
                    pathIndex = 0;
                    debugText += $" | path={path.Count} steps ✓";
                }
                else
                {
                    debugText += " | NO PATH FOUND";
                    Debug.LogWarning($"Enemy: No path from {fromCell} to {toCell}");
                }
            }

            yield return new WaitForSeconds(pathRefreshRate);
        }
    }

    // =========================================================================
    // Player-cell helper – robust against coordinate-system drift
    // =========================================================================

    /// <summary>
    /// Returns the floor cell the player is on (or nearest to).
    /// Logs the player world-pos for diagnostics every call.
    /// </summary>
    private Vector3Int BestPlayerCell()
    {
        Vector3 wp = player.position;
        Vector3Int direct = map.WorldToCell(wp);

        if (floor.Contains(direct)) return direct;

        // Fallback: nearest floor cell by actual world-space distance.
        // Handles coordinate-system mismatches (e.g. rb.position not yet synced).
        Vector3Int best = default;
        float bestSqr = float.MaxValue;
        foreach (var c in floor)
        {
            float sqr = ((Vector2)map.CellToWorld(c) + new Vector2(0.5f, 0.5f) - (Vector2)wp).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = c; }
        }
        Debug.LogWarning($"[Enemy] Player not on floor (cell {direct}), snapped to nearest: {best} (dist {Mathf.Sqrt(bestSqr):F2})");
        return best;
    }

    // =========================================================================
    // A*  –  Manhattan heuristic keeps node count low
    // =========================================================================

    // Simple open-list entry
    private struct OpenEntry { public float f; public int seq; public Vector3Int node; }

    private List<Vector3Int> AStar(Vector3Int start, Vector3Int goal)
    {
        // Use a plain List as priority queue – linear scan to find min is fast
        // enough for tile grids and avoids SortedList's duplicate-key restriction.
        var open     = new List<OpenEntry>(64);
        var cameFrom = new Dictionary<Vector3Int, Vector3Int>(256);
        var gScore   = new Dictionary<Vector3Int, float>(256);
        var closed   = new HashSet<Vector3Int>(256);
        int seq      = 0;

        gScore[start] = 0f;
        open.Add(new OpenEntry { f = H(start, goal), seq = seq++, node = start });

        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

        while (open.Count > 0)
        {
            // Pop the entry with the lowest f (linear scan – no duplicate-key issues)
            int bestIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].f < open[bestIdx].f) bestIdx = i;

            Vector3Int curr = open[bestIdx].node;
            open[bestIdx] = open[open.Count - 1];   // swap-remove (O(1))
            open.RemoveAt(open.Count - 1);

            if (curr == goal) return Reconstruct(cameFrom, curr, start);
            if (closed.Contains(curr)) continue;
            closed.Add(curr);

            float g = gScore.TryGetValue(curr, out float gv) ? gv : float.MaxValue;

            foreach (var d in dirs)
            {
                Vector3Int nb = curr + d;
                if (!floor.Contains(nb) || closed.Contains(nb)) continue;

                float newG = g + 1f;
                if (gScore.TryGetValue(nb, out float oldG) && newG >= oldG) continue;

                gScore[nb]   = newG;
                cameFrom[nb] = curr;

                float f = newG + H(nb, goal);
                open.Add(new OpenEntry { f = f, seq = seq++, node = nb });
            }
        }

        return new List<Vector3Int>(); // no path
    }

    private static float H(Vector3Int a, Vector3Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private static List<Vector3Int> Reconstruct(
        Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int curr, Vector3Int start)
    {
        var path = new List<Vector3Int>();
        while (curr != start && cameFrom.ContainsKey(curr))
        {
            path.Add(curr);
            curr = cameFrom[curr];
        }
        path.Reverse();
        return path;
    }

    // =========================================================================
    // PATROL WAYPOINTS  –  Prim's MST over random floor samples
    // =========================================================================

    private void BuildWaypoints()
    {
        waypoints.Clear();
        if (floor == null || floor.Count == 0) return;

        var allFloor = new List<Vector3Int>();

        if (dm != null && homeRoomIndex >= 0)
        {
            var rooms = dm.GetRooms();
            if (rooms != null && homeRoomIndex < rooms.Count)
            {
                RectInt homeRoom = rooms[homeRoomIndex];
                foreach (var cell in floor)
                {
                    if (cell.x >= homeRoom.x && cell.x < homeRoom.xMax &&
                        cell.y >= homeRoom.y && cell.y < homeRoom.yMax)
                    {
                        allFloor.Add(cell);
                    }
                }
            }
        }

        if (allFloor.Count == 0)
            allFloor.AddRange(floor);

        int nodeCount = Mathf.Min(patrolWaypointCount * 5, allFloor.Count);

        // Sample random floor nodes
        var nodes  = new List<Vector3Int>();
        var picked = new HashSet<int>();
        nodes.Add(NearestFloor(map.WorldToCell(transform.position)));

        for (int i = 0; i < nodeCount * 6 && nodes.Count < nodeCount; i++)
        {
            int idx = Random.Range(0, allFloor.Count);
            if (picked.Add(idx)) nodes.Add(allFloor[idx]);
        }

        if (nodes.Count < 2) { waypoints.AddRange(nodes); return; }

        // Prim's MST
        var inTree = new HashSet<int>() { 0 };
        var parent = new Dictionary<int, int>();
        var dist   = new float[nodes.Count];
        for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
        dist[0] = 0f;

        while (inTree.Count < nodes.Count)
        {
            foreach (int u in inTree)
                for (int v = 0; v < nodes.Count; v++)
                {
                    if (inTree.Contains(v)) continue;
                    float d = H(nodes[u], nodes[v]);
                    if (d < dist[v]) { dist[v] = d; parent[v] = u; }
                }

            int best = -1; float bd = float.MaxValue;
            for (int v = 0; v < nodes.Count; v++)
                if (!inTree.Contains(v) && dist[v] < bd) { bd = dist[v]; best = v; }
            if (best < 0) break;
            inTree.Add(best);
        }

        // Build adjacency, DFS order
        var adj     = new Dictionary<int, List<int>>();
        for (int i = 0; i < nodes.Count; i++) adj[i] = new List<int>();
        foreach (var kv in parent) { adj[kv.Value].Add(kv.Key); adj[kv.Key].Add(kv.Value); }

        var order   = new List<int>();
        var visited = new HashSet<int>();
        var stack   = new Stack<int>(); stack.Push(0);
        while (stack.Count > 0)
        {
            int c = stack.Pop();
            if (!visited.Add(c)) continue;
            order.Add(c);
            foreach (int nb in adj[c]) if (!visited.Contains(nb)) stack.Push(nb);
        }

        int stride = Mathf.Max(1, order.Count / patrolWaypointCount);
        for (int i = 0; i < order.Count; i += stride)
            waypoints.Add(nodes[order[i]]);

        Debug.Log($"Enemy: {waypoints.Count} patrol waypoints built (room={homeRoomIndex}).");
    }

    public void ConfigureAsHunter()
    {
        behaviorProfile = BehaviorProfile.HunterToying;
        baseMoveSpeed = moveSpeed;
        RandomizeHunterHabit();
    }

    private void RandomizeHunterHabit()
    {
        float minRadius = Mathf.Min(hunterOrbitRadiusRange.x, hunterOrbitRadiusRange.y);
        float maxRadius = Mathf.Max(hunterOrbitRadiusRange.x, hunterOrbitRadiusRange.y);
        hunterOrbitRadius = Random.Range(minRadius, maxRadius);

        hunterOrbitAngle = Random.Range(0f, Mathf.PI * 2f);
        hunterOrbitDirection = Random.value < 0.5f ? -1f : 1f;
        hunterOrbitSpeedRuntime = hunterOrbitAngularSpeed * Random.Range(0.75f, 1.35f);
        hunterStrikeSpeedRuntime = baseMoveSpeed * hunterStrikeSpeedMultiplier * Random.Range(0.9f, 1.25f);

        hunterStrikeUntilTime = 0f;
        ScheduleNextHunterStrike(Time.time);
    }

    private bool UpdateHunterStrikeState()
    {
        float now = Time.time;

        if (now <= hunterStrikeUntilTime)
            return true;

        if (now >= hunterNextStrikeTime)
        {
            float minDur = Mathf.Min(hunterStrikeDurationRange.x, hunterStrikeDurationRange.y);
            float maxDur = Mathf.Max(hunterStrikeDurationRange.x, hunterStrikeDurationRange.y);
            hunterStrikeUntilTime = now + Random.Range(minDur, maxDur);
            ScheduleNextHunterStrike(hunterStrikeUntilTime);
            return true;
        }

        return false;
    }

    private void ScheduleNextHunterStrike(float fromTime)
    {
        float minGap = Mathf.Min(hunterStrikeIntervalRange.x, hunterStrikeIntervalRange.y);
        float maxGap = Mathf.Max(hunterStrikeIntervalRange.x, hunterStrikeIntervalRange.y);
        hunterNextStrikeTime = fromTime + Random.Range(minGap, maxGap);
    }

    private Vector3Int GetHunterOrbitCell()
    {
        if (player == null) return NearestFloor(map.WorldToCell(transform.position));

        Vector2 offset = new Vector2(Mathf.Cos(hunterOrbitAngle), Mathf.Sin(hunterOrbitAngle)) * hunterOrbitRadius;
        hunterOrbitAngle += hunterOrbitSpeedRuntime * hunterOrbitDirection * pathRefreshRate;

        Vector3 worldTarget = player.position + new Vector3(offset.x, offset.y, 0f);
        return NearestFloor(map.WorldToCell(worldTarget));
    }

    // =========================================================================
    // SEGMENTS
    // =========================================================================

    // DELETED: SpawnSegment

    // =========================================================================
    // HELPERS
    // =========================================================================

    private Vector2 CellCenter(Vector3Int cell)
        => (Vector2)map.CellToWorld(cell) + new Vector2(0.5f, 0.5f);

    private Vector3Int NearestFloor(Vector3Int cell)
    {
        if (floor == null) return cell;
        if (floor.Contains(cell)) return cell;

        var q = new Queue<Vector3Int>();
        var v = new HashSet<Vector3Int>();
        q.Enqueue(cell); v.Add(cell);
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        int max = 500;
        while (q.Count > 0 && max-- > 0)
        {
            var c = q.Dequeue();
            foreach (var d in dirs)
            {
                var nb = c + d;
                if (!v.Add(nb)) continue;
                if (floor.Contains(nb)) return nb;
                q.Enqueue(nb);
            }
        }
        return cell;
    }

    // =========================================================================
    // DEBUG  –  on-screen HUD
    // =========================================================================

    // void OnGUI()
    // {
    //     if (!showDebugGUI) return;
    //     GUI.color = Color.white;
    //     GUI.Label(new Rect(10, 10, 600, 25),
    //         $"[Enemy] state={state} | path={path.Count} idx={pathIndex} | {debugText}");
    // }

    void OnDestroy()
    {
        AllEnemies.Remove(this);
    }

    private void OnDrawGizmos()
    {
        if (line == null || line.positionCount == 0) return;
        
        Gizmos.color = Color.yellow;
        for (int i = 0; i < line.positionCount; i++)
        {
            Gizmos.DrawWireSphere(line.GetPosition(i), 0.1f);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log("Enemy caught the player!");
            if (GameManager.Instance != null)
                GameManager.Instance.TriggerGameOver("The Snake Caught You!");
        }
    }
}
