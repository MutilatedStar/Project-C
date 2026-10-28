using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Ilumisoft.HealthSystem;

/// <summary>
/// Spawns FoeController foes around the player, one level at a time.
///
///   - Each level has its OWN list of foe types, each with its own amount (the total spawned that level).
///   - Foes appear a set number of tiles from the player. Harder difficulty = shorter distance.
///   - At most "maxAlive" foes exist at once. Harder difficulty = higher cap and more foes per level.
///   - Foes arrive one at a time with a COOLDOWN between spawns. Harder difficulty = shorter cooldown (foes arrive faster),
///     easier = longer cooldown.
///   - Each foe entry can set its OWN max HP, contact damage and speed, so a big foe and a small foe can share a prefab
///     (or each have one) and still differ. Leave a value at 0 to use the prefab's number instead.
///   - Every spawned foe then gets a small random change to its speed, damage and HP (and score follows them), applied
///     on top of that entry's numbers. The change is very small on Easy, softer-but-noticeable on Normal, and wider on Hard.
///   - Idle despawn: a foe that stays OFF the camera's view for 2 minutes without taking any damage is removed and given back
///     to the level, so a replacement spawns near the player. This keeps foes trapped behind destructible objects from
///     filling the cap. Floors listed in "Idle Despawn Exempt Floors" (1, 4 and 5 by default) never do this.
///
/// Difficulty comes from FoeController.CurrentDifficulty. Counts and the cap are read when a level STARTS;
/// spawn distance, spawn cooldown and stat variation are read live on every spawn.
///
/// Usage: put this on an empty object, fill in "levels", then call StartLevel(n) (or tick autoStart).
/// Listen to OnLevelCleared to know when the player has killed everything.
/// </summary>
public class FoeSpawner : MonoBehaviour
{
    // ------------------------------------------------------------ level data

    [System.Serializable]
    public class FoeEntry
    {
        [Tooltip("A foe PREFAB from the Project window. It needs a FoeController on it (on the root object is best).")]
        public GameObject prefab;
        [Tooltip("How many of this foe this level spawns in total, on Easy. Harder difficulties multiply it.")]
        [Min(0)] public int amount = 5;

        // Base stats for THIS entry. Each one left at 0 uses the number on the prefab, so entries you don't touch behave
        // exactly as before. The random variation (Stat Variation below) is applied on top of whatever base is used here.
        [Tooltip("Max (and starting) HP for foes from this entry. 0 = use the prefab's own Health value. Random variation is applied on top.")]
        [Min(0f)] public float maxHealth = 0f;
        [Tooltip("Damage per hit to the player, BEFORE the difficulty multiplier and random variation. 0 = use the prefab's Contact Damage.")]
        [Min(0f)] public float contactDamage = 0f;
        [Tooltip("Chase speed as a fraction of the player's walking speed (0.5 = half as fast, 1.1 = a bit faster). 0 = use the prefab's Speed Multiplier.")]
        [Min(0f)] public float speedMultiplier = 0f;
    }

    [System.Serializable]
    public class LevelFoeSet
    {
        public string name = "Level";
        [Tooltip("Most foes alive at once, on Easy. Harder difficulties multiply it.")]
        [Min(1)] public int maxAlive = 4;
        public FoeEntry[] foes;
    }

    // ------------------------------------------------------------ inspector

    [Header("Target")]
    [Tooltip("Leave empty to auto-find the object tagged 'Player'.")]
    [SerializeField] private Transform player;

    [Header("Levels")]
    [Tooltip("One entry per level. Past the last entry, the last one is reused.")]
    [SerializeField] private LevelFoeSet[] levels;
    [SerializeField] private bool autoStart = true;
    [Tooltip("Level to begin on when autoStart is on. 0 = the first level.")]
    [SerializeField] private int startLevel = 0;

    [Header("Difficulty scaling (Easy / Normal / Hard)")]
    [Tooltip("How far from the player foes appear, in TILES. Smaller = harder.")]
    [SerializeField] private FoeController.DifficultyScale spawnDistanceTiles = new FoeController.DifficultyScale(10f, 8f, 6f);
    [Tooltip("Multiplies each level's 'maxAlive'. Easy is the baseline (1).")]
    [SerializeField] private FoeController.DifficultyScale aliveCapScale = new FoeController.DifficultyScale(1f, 1.25f, 1.5f);
    [Tooltip("Multiplies every foe 'amount' in a level. Hard gets a little extra.")]
    [SerializeField] private FoeController.DifficultyScale foeCountScale = new FoeController.DifficultyScale(1f, 1.25f, 1.6f);
    [Tooltip("Random stat change per foe: 0.05 = each stat is up to 5% higher or lower.")]
    [SerializeField] private FoeController.DifficultyScale statVariation = new FoeController.DifficultyScale(0.05f, 0.15f, 0.25f);

    [Header("Placement")]
    [SerializeField] private float tilePixels = 32f;
    [SerializeField] private float pixelsPerUnit = 32f;
    [Tooltip("Spawn distance is randomised by up to this many tiles either way.")]
    [SerializeField] private float distanceJitterTiles = 1f;
    [Tooltip("Free space needed around a spawn point, in tiles.")]
    [SerializeField] private float clearRadiusTiles = 0.4f;
    [Tooltip("Layers a foe may not spawn on top of or behind: walls, props, other foes. Do NOT include the Player layer.")]
    [SerializeField] private LayerMask blockedMask = ~0;
    [Tooltip("STRONGLY RECOMMENDED. Foes only spawn on cells that have a tile in at least one of these floor Tilemaps. This is what keeps them inside the stage.")]
    [SerializeField] private Tilemap[] floorTilemaps;
    [Tooltip("Alternative for floors built from prefabs instead of Tilemaps: give floor pieces a Collider2D (trigger is fine) on one layer and pick that layer. Nothing = off.")]
    [SerializeField] private LayerMask floorMask = 0;
    [Tooltip("Foes never spawn on a cell that has a tile in any of these Tilemaps (your wall Tilemaps).")]
    [SerializeField] private Tilemap[] wallTilemaps;
    [Tooltip("Reject spawn points with a wall or other solid thing between them and the player. Stops spawns behind walls and outside the stage.")]
    [SerializeField] private bool requireClearLine = true;
    [Tooltip("If the full spawn distance has no valid spot (small room), the search moves closer, down to this many tiles.")]
    [SerializeField] private float minSpawnDistanceTiles = 3f;
    [Tooltip("Random points tried per spawn before giving up until the next frame.")]
    [SerializeField] private int maxPlacementAttempts = 30;

    [Header("Timing")]
    [Tooltip("Cooldown between spawns, in SECONDS, while below the cap (Easy / Normal / Hard). A SHORTER cooldown means foes arrive faster, " +
             "so Hard has the smallest number and Easy the biggest. Read live on every spawn, so changing the difficulty mid-run takes effect straight away.")]
    [SerializeField] private FoeController.DifficultyScale spawnCooldown = new FoeController.DifficultyScale(2.5f, 1.5f, 0.9f);
    [Tooltip("Optional parent object to keep spawned foes tidy in the Hierarchy.")]
    [SerializeField] private Transform foeParent;

    [Header("Idle despawn (foes nobody has seen or hurt)")]
    [Tooltip("Seconds a foe may stay OFF the camera's view without taking any damage before it is removed and a replacement spawns near the player. " +
             "The timer resets while the foe is on screen (so nothing vanishes in view) and is removed for good the moment the foe takes any damage. " +
             "This stops foes trapped behind destructible objects from filling the cap while the player sees nothing. 0 = never.")]
    [SerializeField] private float idleDespawnSeconds = 120f;
    [Tooltip("Floor numbers (1 = the first floor) where foes NEVER idle-despawn: 1 = the single-foe floor, 4 = the monster floor, 5 = the boss floor. " +
             "A floor's number is its level number + 1 (level 0 is floor 1).")]
    [SerializeField] private int[] idleDespawnExemptFloors = { 1, 4, 5 };

    // ------------------------------------------------------------ state

    /// <summary>The current level, 0-based.</summary>
    public int CurrentLevel { get; private set; }

    /// <summary>Fires once per level when every foe has been spawned and killed. Passes the level index.</summary>
    public event System.Action<int> OnLevelCleared;

    /// <summary>
    /// Optional extra rule for spawn points. FloorManager sets this to "is this point somewhere I would NOT remove a
    /// foe from". A point it rejects is never used, so a foe is never created somewhere it would be removed from
    /// a moment later (no pop-in then pop-out).
    /// </summary>
    public System.Func<Vector2, bool> SpawnPointFilter;

    /// <summary>
    /// FloorManager sets this to true. While it is true, "Auto Start" is ignored, because FloorManager starts the level that
    /// matches the current floor itself (this matters after a trip to the Shop, when the run resumes on floor 3, 4, ...
    /// and Auto Start would otherwise start level 0 over it).
    /// </summary>
    public bool ControlledExternally { get; set; }

    private float failedSpawnTime;
    private bool warnedNoSpawnPoint;

    private readonly List<FoeController> alive = new List<FoeController>();
    private int[] remaining;            // foes still to spawn, one number per entry in the current level
    private int maxAliveNow;
    private float spawnTimer;
    private bool levelActive;
    private bool levelClearedFired;
    private HealthComponent playerHealth;
    private bool warnedAboutAsset;
    private bool warnedNoPlayer;
    private readonly RaycastHit2D[] lineHits = new RaycastHit2D[8];

    // Which entry in the level's foe list each spawned foe came from, so RemoveStray can give it back.
    private readonly Dictionary<FoeController, int> entryOf = new Dictionary<FoeController, int>();

    // A foe entry may point at an object in the SCENE (for example the original foe) instead of a prefab asset.
    // Spawning straight from a scene object copies its state AT THAT MOMENT, so once the original has died, been
    // hidden or been destroyed, every foe made from it would be invisible, dead or impossible to make. So each
    // scene object is copied ONCE, while the spawner starts, into an inactive clean copy, and spawning uses that
    // copy instead. Prefab assets are never copied: they are always clean.
    private readonly Dictionary<FoeEntry, GameObject> cleanCopies = new Dictionary<FoeEntry, GameObject>();
    private readonly HashSet<GameObject> warnedNotAFoe = new HashSet<GameObject>();

    private float TileSize => tilePixels / pixelsPerUnit;

    // The cooldown for the CURRENT difficulty. The floor stops a 0 in the Inspector from spawning a foe every single frame.
    private float SpawnCooldown => Mathf.Max(0.05f, spawnCooldown.Get(FoeController.CurrentDifficulty));

    // ------------------------------------------------------------ setup

    private void Start()
    {
        MakeCleanCopies();   // before anything can spawn, die or be hidden
        EnsurePlayer();

        if (autoStart && !ControlledExternally) StartLevel(startLevel);
    }

    // The player must be the LIVE object in the scene. An empty field, a destroyed player, or a player PREFAB dragged
    // in from the Project window (an asset, not the player that is actually running around) all make this search for
    // the object tagged "Player" instead. It keeps retrying, so a player that is spawned later is picked up too.
    private bool EnsurePlayer()
    {
        if (player != null && player.gameObject.scene.IsValid()) return true;

        if (player != null && !warnedAboutAsset)
        {
            warnedAboutAsset = true;
            Debug.LogWarning("FoeSpawner: the Player field points at a prefab asset, not the player in the scene. " +
                             "Using the object tagged 'Player' instead. Clear the field to silence this.");
        }

        GameObject found = GameObject.FindWithTag("Player");
        if (found == null)
        {
            if (!warnedNoPlayer && Time.timeSinceLevelLoad > 3f)
            {
                warnedNoPlayer = true;
                Debug.LogWarning("FoeSpawner: no object tagged 'Player' found in the scene, so nothing can spawn. " +
                                 "Select your player prefab and set its Tag to Player.");
            }
            return false;
        }

        player = found.transform;
        playerHealth = player.GetComponentInParent<HealthComponent>();
        return true;
    }

    // ------------------------------------------------------------ level control

    /// <summary>Begins a level: removes foes left from the last one and loads this level's foe list.</summary>
    public void StartLevel(int level)
    {
        ClearFoes();
        CurrentLevel = Mathf.Max(0, level);

        LevelFoeSet set = GetSet(CurrentLevel);
        if (set == null || set.foes == null)
        {
            Debug.LogWarning("FoeSpawner: no foe list set up for level " + CurrentLevel);
            levelActive = false;
            return;
        }

        FoeController.Difficulty d = FoeController.CurrentDifficulty;
        float countScale = foeCountScale.Get(d);

        remaining = new int[set.foes.Length];
        for (int i = 0; i < set.foes.Length; i++)
        {
            FoeEntry entry = set.foes[i];
            GameObject source = SourceFor(entry);
            bool usable = source != null && entry.amount > 0;
            if (usable && source.GetComponentInChildren<FoeController>(true) == null)
            {
                Debug.LogWarning("FoeSpawner: prefab '" + source.name + "' has no FoeController on it or its children, so it was skipped.");
                usable = false;
            }
            remaining[i] = usable ? Mathf.Max(1, Mathf.RoundToInt(entry.amount * countScale)) : 0;
        }

        maxAliveNow = Mathf.Max(1, Mathf.RoundToInt(set.maxAlive * aliveCapScale.Get(d)));
        spawnTimer = SpawnCooldown;
        levelClearedFired = false;
        levelActive = true;
    }

    /// <summary>Starts the next level (the foe list of the last level is reused once you run past the list).</summary>
    public void NextLevel()
    {
        StartLevel(CurrentLevel + 1);
    }

    /// <summary>Destroys every foe this spawner still has alive.</summary>
    public void ClearFoes()
    {
        for (int i = 0; i < alive.Count; i++)
        {
            if (alive[i] != null) alive[i].Remove();   // removes just that foe: the spawner (or other foes) under its object survive
        }
        alive.Clear();
        entryOf.Clear();
    }

    /// <summary>
    /// Removes a foe that ended up somewhere invalid (for example off the floor) and gives it back to the level,
    /// so a replacement spawns in a valid spot. Foes this spawner did not make are just destroyed.
    /// FloorManager calls this from its stray foe check.
    /// </summary>
    public void RemoveStray(FoeController foe)
    {
        if (foe == null) return;

        if (entryOf.TryGetValue(foe, out int index) && remaining != null && index < remaining.Length)
            remaining[index]++;

        entryOf.Remove(foe);
        alive.Remove(foe);
        foe.Remove();
    }

    // Idle despawn seconds for foes spawned on the CURRENT floor. 0 = off (the setting is 0, or this floor is exempt).
    // Floor number = level index + 1, so level 0 is floor 1.
    private float IdleSecondsThisFloor()
    {
        if (idleDespawnSeconds <= 0f) return 0f;

        int floor = CurrentLevel + 1;
        if (idleDespawnExemptFloors != null)
        {
            for (int i = 0; i < idleDespawnExemptFloors.Length; i++)
            {
                if (idleDespawnExemptFloors[i] == floor) return 0f;
            }
        }
        return idleDespawnSeconds;
    }

    // Removes foes whose idle timer ran out (off screen, never hurt). Goes through RemoveStray, so each one is given back
    // to the level and a replacement spawns near the player, where it can be seen. Nothing happens in view.
    private void RemoveIdleFoes()
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            FoeController foe = alive[i];
            if (foe != null && foe.ShouldIdleDespawn()) RemoveStray(foe);
        }
    }

    private LevelFoeSet GetSet(int level)
    {
        if (levels == null || levels.Length == 0) return null;
        return levels[Mathf.Min(level, levels.Length - 1)];
    }

    // ------------------------------------------------------------ clean copies

    private void MakeCleanCopies()
    {
        if (levels == null) return;
        for (int l = 0; l < levels.Length; l++)
        {
            if (levels[l] == null || levels[l].foes == null) continue;
            for (int i = 0; i < levels[l].foes.Length; i++) SourceFor(levels[l].foes[i]);
        }
    }

    // The object to spawn this entry from: its clean copy when it points at a scene object, otherwise the prefab asset.
    private GameObject SourceFor(FoeEntry entry)
    {
        if (entry == null) return null;
        if (cleanCopies.TryGetValue(entry, out GameObject existing) && existing != null) return existing;
        if (entry.prefab == null || !entry.prefab.scene.IsValid()) return entry.prefab;   // an asset (or nothing): already clean

        // A Hierarchy object that is the Player (or carries the FloorController) is not a foe. Copying it would spawn extra
        // players and run a second FloorController, which resets the floor and breaks the stairs. Skip the entry and say so.
        if (entry.prefab.CompareTag("Player") || entry.prefab.GetComponentInChildren<FloorController>(true) != null)
        {
            if (warnedNotAFoe.Add(entry.prefab))
            {
                Debug.LogError("FoeSpawner: a foe entry's Prefab slot points at the scene object '" + entry.prefab.name + "', which is the " +
                               "Player (it has the Player tag or a FloorController). It was skipped. In this spawner's Levels > Foes lists, " +
                               "find the entry showing '" + entry.prefab.name + "' and replace it with a foe PREFAB from the Project window.", this);
            }
            return null;
        }

        GameObject copy = Instantiate(entry.prefab);
        copy.name = entry.prefab.name + " (spawn template)";
        foreach (FoeSpawner s in copy.GetComponentsInChildren<FoeSpawner>(true)) Destroy(s);   // a copy must never spawn foes itself
        copy.SetActive(false);

        cleanCopies[entry] = copy;
        return copy;
    }

    // ------------------------------------------------------------ spawning

    private void Update()
    {
        if (!levelActive || FoeController.Frozen || !EnsurePlayer()) return;
        if (playerHealth != null && playerHealth.CurrentHealth <= 0f) return;   // player is dead: stop

        // Dead foes stop counting toward the cap the moment they die (before they are destroyed).
        alive.RemoveAll(f => f == null || f.IsDead);

        RemoveIdleFoes();   // before the checks below, so a recycled foe is counted as "still to spawn" again

        if (TotalRemaining() == 0)
        {
            if (alive.Count == 0 && !levelClearedFired)
            {
                levelClearedFired = true;
                OnLevelCleared?.Invoke(CurrentLevel);
            }
            return;
        }

        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f || alive.Count >= maxAliveNow) return;

        if (SpawnOne())
        {
            spawnTimer = SpawnCooldown;
            failedSpawnTime = 0f;
        }
        else
        {
            // No valid point this frame: just try again next frame, but say so if it keeps happening.
            failedSpawnTime += Time.deltaTime;
            if (failedSpawnTime > 3f && !warnedNoSpawnPoint)
            {
                warnedNoSpawnPoint = true;
                Debug.LogWarning("FoeSpawner: no valid spawn point found for 3 seconds, so nothing is spawning. Check the Floor " +
                                 "Tilemaps here and on FloorManager, the Blocked Mask (it must NOT include FloorManager's protected " +
                                 "layer), and that the player is standing on the floor.");
            }
        }
    }

    private bool SpawnOne()
    {
        int index = PickEntryIndex();
        if (index < 0) return false;
        if (!TryGetSpawnPoint(out Vector2 point)) return false;

        FoeEntry entry = GetSet(CurrentLevel).foes[index];
        GameObject instance = Instantiate(SourceFor(entry), point, Quaternion.identity, foeParent);
        instance.SetActive(true);   // the source may be an inactive clean copy

        // Normally the prefab holds ONE foe. If it holds several, every one of them is set up and counted.
        FoeController[] foes = instance.GetComponentsInChildren<FoeController>(true);
        for (int i = 0; i < foes.Length; i++)
        {
            FoeController foe = foes[i];

            if (i == 0) foe.SetRoot(instance);   // the first foe owns the whole prefab, even if FoeController is on a child
            foe.SetTarget(player);
            foe.SetBaseStats(entry.maxHealth, entry.contactDamage, entry.speedMultiplier);   // this entry's numbers (0 = keep the prefab's)
            foe.ApplyRandomStatus(statVariation.Get(FoeController.CurrentDifficulty));       // then the random +/- on top of them
            foe.SetIdleDespawn(IdleSecondsThisFloor());

            alive.Add(foe);
            entryOf[foe] = index;
        }

        remaining[index]--;
        return true;
    }

    // Weighted pick: a foe type with more left to spawn is more likely, so the mix matches the amounts.
    private int PickEntryIndex()
    {
        int total = TotalRemaining();
        if (total <= 0) return -1;

        int roll = Random.Range(0, total);
        for (int i = 0; i < remaining.Length; i++)
        {
            roll -= remaining[i];
            if (roll < 0) return i;
        }
        return -1;
    }

    private int TotalRemaining()
    {
        if (remaining == null) return 0;
        int total = 0;
        for (int i = 0; i < remaining.Length; i++) total += remaining[i];
        return total;
    }

    // Tries random points around the player until one is valid. The first tries use the full spawn distance for the
    // current difficulty; if that finds nothing (small room), later tries move closer, down to minSpawnDistanceTiles.
    private bool TryGetSpawnPoint(out Vector2 point)
    {
        float farDistance = spawnDistanceTiles.Get(FoeController.CurrentDifficulty) * TileSize;
        float nearDistance = Mathf.Min(minSpawnDistanceTiles * TileSize, farDistance);
        Vector2 center = player.position;

        for (int i = 0; i < maxPlacementAttempts; i++)
        {
            float progress = maxPlacementAttempts > 1 ? (float)i / (maxPlacementAttempts - 1) : 0f;
            float baseDistance = Mathf.Lerp(farDistance, nearDistance, progress);

            float angle = Random.value * Mathf.PI * 2f;
            float distance = baseDistance + Random.Range(-distanceJitterTiles, distanceJitterTiles) * TileSize;
            distance = Mathf.Max(distance, TileSize);   // never spawn right on top of the player

            Vector2 candidate = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            if (IsValidSpawn(candidate, center))
            {
                point = candidate;
                return true;
            }
        }

        point = center;
        return false;
    }

    private bool IsValidSpawn(Vector2 p, Vector2 playerPos)
    {
        // 0. Optional outside rule (FloorManager). Checked BEFORE the foe is created, so it never appears somewhere
        //    it would be removed from afterwards.
        if (SpawnPointFilter != null && !SpawnPointFilter(p)) return false;

        // 1. Must be on the floor (this is what keeps foes inside the stage).
        if (floorTilemaps != null && floorTilemaps.Length > 0 && !OnAnyTile(floorTilemaps, p)) return false;
        if (floorMask.value != 0 && Physics2D.OverlapPoint(p, floorMask) == null) return false;

        // 2. Must not be on a wall tile.
        if (wallTilemaps != null && wallTilemaps.Length > 0 && OnAnyTile(wallTilemaps, p)) return false;

        // 3. Nothing solid right there (wall colliders, props, other foes).
        if (Physics2D.OverlapCircle(p, clearRadiusTiles * TileSize, blockedMask) != null) return false;

        // 4. Nothing solid between the player and the spawn point (not behind a wall, not outside the stage).
        if (requireClearLine && LineBlocked(playerPos, p)) return false;

        return true;
    }

    private static bool OnAnyTile(Tilemap[] maps, Vector2 worldPos)
    {
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] != null && maps[i].HasTile(maps[i].WorldToCell(worldPos))) return true;
        }
        return false;
    }

    // True if a solid collider sits on the line between the two points (the player's own colliders are ignored).
    private bool LineBlocked(Vector2 from, Vector2 to)
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(blockedMask);
        filter.useTriggers = false;

        int count = Physics2D.Linecast(from, to, filter, lineHits);
        for (int i = 0; i < count; i++)
        {
            Transform hit = lineHits[i].collider.transform;
            if (hit == player || hit.IsChildOf(player)) continue;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------ editor helper

    // Select the spawner while playing to see the spawn ring for the current difficulty (yellow).
    private void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(player.position, spawnDistanceTiles.Get(FoeController.CurrentDifficulty) * TileSize);
    }
}
