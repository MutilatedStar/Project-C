using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Tilemaps;
using Ilumisoft.HealthSystem;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random.

/// <summary>
/// The look and sound of lava and water tiles. Put it on ONE empty object in the scene (FloorManager finds the same tiles
/// it hurts with, so what glows is exactly what burns). It builds everything else by itself: no prefabs, nothing to wire up
/// except the sound clips.
///
///   GLOW        a soft, slowly breathing glow over every lava / water tile. It is baked from the tile shapes, so it fades out
///               toward the edge of the pool and can NEVER spill onto a neighbouring floor or wall tile. It is drawn in front of
///               the characters, so a player standing in lava is lit by it too.
///   PARTICLES   small square pieces that take the colour of a random pixel of the tile's own sprite and hop out of the tile:
///               slowly, at random times, with no help from anybody (AMBIENT), and more of them while a player walks on it
///               (WALKING), plus a small splash when a player steps in. Every kind has its own level: Off / Low / Medium / High.
///               Pieces are drawn in front of the characters, but they never land on or pass over a wall.
///   SOUND       a step sound every so often while a player moves over lava or water, and a splash sound when they step in.
///               Drop clips into "Step Clips" / "Splash Clips" on Lava and Water; one is picked at random each time.
///
/// Every part has its own switch. All of them can be changed while the game runs, or from code on this component
/// (for example from a settings menu): EffectsEnabled, GlowEnabled, ParticlesEnabled, SoundEnabled.
///
/// PAUSE: like the foes, everything holds still while the game is held: PauseManager's offline pause (PauseManager.IsFrozen),
/// the stairs menu / notebook (FoeController.IsPaused, MenuController.IsOpen) or Time.timeScale at 0. Pieces freeze in mid-hop,
/// the glow stops breathing, no new sound starts and sounds already playing are paused, then all of it carries on.
/// ONLINE multiplayer keeps running behind the pause menu (PauseManager.IsFrozen is false there), the same as the foes do.
/// You can also drag this component into PauseManager's "World Scripts": it holds still when switched off too.
///
/// WALLS AND LAYERS: the effects are drawn on the players' sorting layer, one order above the highest player, so pieces and glow
/// can go in front of the characters. Walls are found by themselves (Tilemaps with a solid Tilemap Collider 2D); a piece is never
/// started in a wall or sent through one, and the glow has no tiles under walls. If a wall Tilemap is drawn in front of the players,
/// the effects are kept one order below it. Not found right? Fill in "Wall Tilemaps" and / or "Wall Mask".
///
/// Sprite textures do NOT need Read/Write enabled: if a texture can't be read directly, the colours are read back from the GPU.
/// Test: in Play mode, right-click this component's header > "Rebuild Now" after you change tiles by hand.
/// </summary>
[DisallowMultipleComponent]
public class HazardTileFx : MonoBehaviour
{
    // ------------------------------------------------------------ types

    /// <summary>How busy an effect is. Off = nothing, Low = half as often, Medium = as set below, High = twice as often.</summary>
    public enum FxLevel { Off, Low, Medium, High }

    /// <summary>Everything that can be set for ONE kind of tile (shown once for Lava and once for Water).</summary>
    [System.Serializable]
    public class Look
    {
        [Tooltip("Switches everything for this kind of tile off: glow, particles and sound.")]
        public bool enabled = true;

        [Header("Glow")]
        [Tooltip("The soft glow over the tiles.")]
        public bool glow = true;
        [Tooltip("The colour of the glow (its alpha is ignored: use Glow Strength).")]
        public Color glowColour = Color.white;
        [Tooltip("How strong the glow is at its brightest. Keep it low for a soft look.")]
        [Range(0f, 1f)] public float glowStrength = 0.2f;
        [Tooltip("How much the glow breathes. 0 = steady, 1 = fades almost out and back.")]
        [Range(0f, 1f)] public float glowPulse = 0.35f;
        [Tooltip("Seconds for one slow breath of the glow.")]
        [Min(0.2f)] public float glowPulseSeconds = 3.5f;

        [Header("Particles")]
        [Tooltip("How often a piece hops out of the tiles on its own (slow, at random times). Only tiles on screen are used.")]
        public FxLevel ambientLevel = FxLevel.Medium;
        [Tooltip("How many pieces hop while a player WALKS on this tile, and how big the splash is when they step in.")]
        public FxLevel walkingLevel = FxLevel.Medium;
        [Tooltip("The colour of a piece if the tile's sprite pixels can't be read at all.")]
        public Color fallbackColour = Color.white;

        [Header("Sound")]
        [Tooltip("Step sound and splash sound on or off for this tile.")]
        public bool sound = true;
        [Tooltip("Played now and then while a player walks over this tile. One is picked at random. Empty = silent.")]
        public AudioClip[] stepClips;
        [Tooltip("Played when a player steps onto this tile. Empty = a step clip is used instead.")]
        public AudioClip[] splashClips;
        [Tooltip("Random volume between these two.")]
        public Vector2 volume = new Vector2(0.6f, 0.9f);
        [Tooltip("Random pitch between these two (1 = normal).")]
        public Vector2 pitch = new Vector2(0.92f, 1.08f);
    }

    // ------------------------------------------------------------ inspector

    [Header("Switches (can be changed while playing)")]
    [Tooltip("Master switch. Off = no glow, no particles, no sound.")]
    [SerializeField] private bool effectsEnabled = true;
    [Tooltip("The soft glow over the tiles.")]
    [SerializeField] private bool glowEnabled = true;
    [Tooltip("The pieces that hop out of the tiles (ambient and walking).")]
    [SerializeField] private bool particlesEnabled = true;
    [Tooltip("Step and splash sounds.")]
    [SerializeField] private bool soundEnabled = true;
    [Tooltip("ON: the glow is drawn in front of the characters (still only over the tiles, never outside them), so a player standing in lava is lit by it. " +
             "OFF: the glow sits on the tiles, behind the characters.")]
    [SerializeField] private bool glowOverCharacters = true;

    [Header("Lava")]
    [SerializeField] private Look lava = new Look
    {
        glowColour = new Color(1f, 0.42f, 0.08f),
        glowStrength = 0.22f,
        fallbackColour = new Color(1f, 0.5f, 0.1f)
    };

    [Header("Water")]
    [SerializeField] private Look water = new Look
    {
        glowColour = new Color(0.25f, 0.65f, 1f),
        glowStrength = 0.18f,
        fallbackColour = new Color(0.5f, 0.8f, 1f)
    };

    [Header("Glow shape")]
    [Tooltip("How far the glow fades in from the edge of a pool, in tiles. 0 = a hard edge. Small pools glow less when this is big.")]
    [SerializeField, Range(0f, 1.5f)] private float glowEdgeSoftness = 0.6f;
    [Tooltip("Resolution of the baked glow, in pixels per tile. Higher is smoother but uses more memory.")]
    [SerializeField, Range(2, 16)] private int glowTexelsPerTile = 8;
    [Tooltip("Optional. Give the glow its own material, for example an additive sprite material for a brighter, hotter look. Empty = the normal sprite material.")]
    [SerializeField] private Material glowMaterial;

    [Header("Particles: the hop")]
    [Tooltip("Pixels per Unity unit of your sprites (Pixel Art Top Down uses 32). Sizes, distances and heights below are in pixels.")]
    [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;
    [Tooltip("The most pieces alive at once. A new hop is simply skipped when they are all busy.")]
    [SerializeField, Range(4, 256)] private int maxPieces = 64;
    [Tooltip("Size of a piece in pixels. Every piece rolls its own random size between these two.")]
    [SerializeField] private Vector2 pieceSizePixels = new Vector2(2f, 3.5f);
    [Tooltip("How far a piece travels sideways, in pixels (random between the two). Cut short by walls.")]
    [SerializeField] private Vector2 hopDistancePixels = new Vector2(6f, 18f);
    [Tooltip("How high a piece jumps at the top of its hop, in pixels (random between the two). Lowered under walls.")]
    [SerializeField] private Vector2 hopHeightPixels = new Vector2(5f, 12f);
    [Tooltip("When a piece starts to fade, as a fraction of its hop. 0.5 = solid for the first half, then fading out.")]
    [SerializeField, Range(0f, 1f)] private float fadeStartsAt = 0.5f;

    [Header("Particles: ambient (by themselves, slow)")]
    [Tooltip("Seconds between two hops (random between the two) for each kind of tile at Medium. Low waits twice as long, High half as long.")]
    [SerializeField] private Vector2 ambientSecondsBetween = new Vector2(1.2f, 3.5f);
    [Tooltip("How long an ambient hop lasts, in seconds. Long = slow and floaty.")]
    [SerializeField] private Vector2 ambientHopSeconds = new Vector2(1f, 1.8f);

    [Header("Particles: walking")]
    [Tooltip("Seconds between two hops while a player is walking on the tile, at Medium.")]
    [SerializeField] private Vector2 walkingSecondsBetween = new Vector2(0.15f, 0.35f);
    [Tooltip("How long a walking hop lasts, in seconds.")]
    [SerializeField] private Vector2 walkingHopSeconds = new Vector2(0.5f, 0.9f);
    [Tooltip("How many pieces the splash has when a player steps onto the tile, at Medium (random between the two).")]
    [SerializeField] private Vector2Int splashPieces = new Vector2Int(5, 8);

    [Header("Walking (sound and particles)")]
    [Tooltip("A player counts as walking when they move faster than this (units per second).")]
    [SerializeField, Min(0f)] private float minWalkSpeed = 0.4f;
    [Tooltip("A step sound plays every time a player has moved this far over the tile (world units), so running faster means quicker steps.")]
    [SerializeField, Min(0.05f)] private float stepDistance = 0.6f;
    [Tooltip("Step sounds never come closer together than this (seconds).")]
    [SerializeField, Min(0f)] private float minStepSeconds = 0.15f;
    [Tooltip("0 = plays at full volume everywhere (right for a 2D game), 1 = fades with distance from the AudioListener.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;
    [Tooltip("Optional. Send the sounds through this Audio Mixer group (e.g. your SFX group) so the volume sliders affect them.")]
    [SerializeField] private AudioMixerGroup mixerGroup;

    [Header("Walls and layers")]
    [Tooltip("Tilemaps that are walls. Leave EMPTY to find them by themselves: every Tilemap with a solid (not trigger) Tilemap Collider 2D or Composite Collider 2D that has no lava or water.")]
    [SerializeField] private Tilemap[] wallTilemaps;
    [Tooltip("Optional: walls built from prefabs or plain colliders. Colliders on these layers also block the pieces. Nothing = off.")]
    [SerializeField] private LayerMask wallMask = 0;
    [Tooltip("Pieces keep this far from a wall (world units), so even a piece with some size never touches one.")]
    [SerializeField, Min(0f)] private float wallPadding = 0.1f;
    [Tooltip("How many sorting orders above the highest player the effects are drawn. They are kept below any wall that is drawn in front of the players.")]
    [SerializeField, Min(1)] private int sortingOrdersAbovePlayers = 2;

    [Header("Housekeeping")]
    [Tooltip("Only used when there is no FloorManager in the scene: a tile is lava when its name contains this.")]
    [SerializeField] private string fallbackLavaName = "lava";
    [Tooltip("Only used when there is no FloorManager in the scene: a tile is water when its name contains this.")]
    [SerializeField] private string fallbackWaterName = "water";
    [Tooltip("How often the scene's Tilemaps are checked for a change (a new floor, a changed tile), in seconds.")]
    [SerializeField, Min(0.2f)] private float rescanSeconds = 1f;

    // ------------------------------------------------------------ public

    public bool EffectsEnabled { get { return effectsEnabled; } set { effectsEnabled = value; } }
    public bool GlowEnabled { get { return glowEnabled; } set { glowEnabled = value; } }
    public bool ParticlesEnabled { get { return particlesEnabled; } set { particlesEnabled = value; } }
    public bool SoundEnabled { get { return soundEnabled; } set { soundEnabled = value; } }

    /// <summary>Re-reads every Tilemap on the next frame. Only needed if tiles are changed in a way Unity doesn't report.</summary>
    [ContextMenu("Rebuild Now")]
    public void RebuildNow()
    {
        dirty = true;
    }

    // ------------------------------------------------------------ state

    private const int Lava = 0;
    private const int Water = 1;
    private const int None = -1;
    private const int KindCount = 2;
    private const int MaxPlayers = 4;
    private const float TeleportDistance = 1.5f;   // moving further than this in one frame is a teleport, not a step
    private const float GlowEdgeGain = 2.5f;       // how quickly the baked glow reaches full strength inside a pool

    // All the hazard tiles of ONE Tilemap that are one kind (lava or water), and the glow made for them.
    private class Pool
    {
        public Tilemap map;
        public int kind;
        public Vector2 cellSize;
        public float phase;
        public readonly List<Vector3Int> cells = new List<Vector3Int>();
        public readonly HashSet<Vector3Int> set = new HashSet<Vector3Int>();
        public readonly List<Vector3Int> visible = new List<Vector3Int>();
        public GameObject glowObject;
        public SpriteRenderer glowRenderer;
        public Texture2D glowTexture;
        public Sprite glowSprite;
    }

    // One piece hopping out of a tile.
    private class Hop
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector2 from, to;
        public float height, life, age, size;
        public Color colour;
        public bool active;
    }

    // What one player was doing last frame.
    private class Walker
    {
        public bool has;
        public Vector2 last;
        public int kind = None;
        public float distance;
        public float lastStepTime = -10f;
        public float hopTimer;
    }

    // One sprite's pixels (cached, so a tile that is picked a hundred times is only read once).
    private class RawSprite
    {
        public Color[] pixels;
        public int w, h;
        public int[] solid;   // indexes of the pixels that are solid enough to take a colour from
    }

    private static readonly Dictionary<Sprite, RawSprite> pixelCache = new Dictionary<Sprite, RawSprite>();
    private static Sprite square;

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        pixelCache.Clear();
        square = null;
    }

    private readonly List<Pool> pools = new List<Pool>();
    private readonly List<Tilemap> wallMaps = new List<Tilemap>();
    private readonly Dictionary<TileBase, int> tileKinds = new Dictionary<TileBase, int>();
    private readonly Dictionary<Sprite, int> spriteKinds = new Dictionary<Sprite, int>();
    private readonly Collider2D[] wallBuffer = new Collider2D[1];
    private readonly float[] ambientTimer = new float[KindCount];
    private readonly Walker[] walkers = new Walker[MaxPlayers + 1];

    private ContactFilter2D wallFilter;
    private FloorManager floorManager;
    private GameObject root;
    private Hop[] hops;
    private int activeHops;
    private AudioSource[] sources;
    private int nextSource;

    private string lavaName = "lava";
    private string waterName = "water";
    private bool dirty = true;
    private bool built;
    private long signature;
    private float nextRescan;
    private float lastBuild = -10f;
    private float nextVisible;
    private float clock;        // the glow's own clock: it stops while the game is held
    private bool wasHeld;
    private bool warnedSorting;

    private int glowLayerId, glowOrder, pieceLayerId, pieceOrder;

    // ------------------------------------------------------------ lifecycle

    private void Awake()
    {
        for (int i = 0; i < walkers.Length; i++) walkers[i] = new Walker();
        for (int k = 0; k < KindCount; k++) ambientTimer[k] = Random.Range(0.3f, 1.5f);
    }

    private void OnEnable()
    {
        FloorController.FloorChanged += OnFloorChanged;
        Tilemap.tilemapTileChanged += OnTilesChanged;
    }

    private void OnDisable()
    {
        FloorController.FloorChanged -= OnFloorChanged;
        Tilemap.tilemapTileChanged -= OnTilesChanged;

        // Switched off (for example by PauseManager's World Scripts): hold still. The next Update lets everything carry on.
        SetHeld(true);
    }

    private void OnDestroy()
    {
        DestroyPools();
        if (root != null) Destroy(root);
    }

    private void OnFloorChanged(int floor)
    {
        // The players are teleported and a different set of tiles may be on show.
        dirty = true;
        ClearHops();
        for (int i = 0; i < walkers.Length; i++) walkers[i].has = false;
    }

    private void OnTilesChanged(Tilemap map, Tilemap.SyncTile[] tiles)
    {
        dirty = true;
    }

    // ------------------------------------------------------------ the frame

    private void Update()
    {
        if (!effectsEnabled)
        {
            if (root != null && root.activeSelf)
            {
                ClearHops();
                root.SetActive(false);
            }
            return;
        }

        EnsureRoot();
        if (!root.activeSelf) root.SetActive(true);

        // Paused by PauseManager, the stairs menu or the notebook: hold everything exactly where it is.
        bool held = WorldHeld();
        SetHeld(held);
        if (held) return;

        float dt = Time.deltaTime;
        clock += dt;

        if (!built || Time.time >= nextRescan || (dirty && Time.time - lastBuild >= 0.25f)) Rescan();
        if (Time.time >= nextVisible)
        {
            nextVisible = Time.time + 0.4f;
            RefreshVisible();
        }

        UpdateGlow();

        if (!particlesEnabled && activeHops > 0) ClearHops();

        TrackPlayers(dt);
        if (particlesEnabled) TickAmbient(dt);
        UpdateHops(dt);
    }

    // True while the world should hold still. PauseManager.IsFrozen is only true for an OFFLINE pause: online, the world runs on
    // behind the menu, so the lava does too.
    private static bool WorldHeld()
    {
        return PauseManager.IsFrozen || FoeController.IsPaused || MenuController.IsOpen || Time.timeScale <= 0.0001f;
    }

    private void SetHeld(bool held)
    {
        if (held == wasHeld) return;
        wasHeld = held;
        if (sources == null) return;

        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource s = sources[i];
            if (s == null || !s.isActiveAndEnabled) continue;
            if (held) { if (s.isPlaying) s.Pause(); }
            else s.UnPause();
        }
    }

    private void EnsureRoot()
    {
        if (root != null) return;
        // Its own object at the origin (not under this one), so the scale of whatever this script sits on can't distort anything.
        root = new GameObject("HazardTileFx (effects)");
    }

    // ------------------------------------------------------------ reading the tiles

    private void Rescan()
    {
        nextRescan = Time.time + rescanSeconds;

        Tilemap[] all = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        long sig = Signature(all);
        if (built && !dirty && sig == signature)
        {
            RefreshSorting();   // nothing changed in the tiles, but a player may have spawned since
            ApplySorting();
            return;
        }

        signature = sig;
        dirty = false;
        built = true;
        lastBuild = Time.time;
        Rebuild(all);
    }

    // A number that changes when a Tilemap is shown / hidden, gets or loses tiles, or its size changes. Added up (not chained), so the
    // order FindObjectsByType returns the maps in doesn't matter.
    private static long Signature(Tilemap[] maps)
    {
        unchecked
        {
            long sig = 17;
            for (int i = 0; i < maps.Length; i++)
            {
                Tilemap m = maps[i];
                if (m == null || !m.gameObject.activeInHierarchy) continue;

                BoundsInt b = m.cellBounds;
                // Which Tilemap this is. Not GetInstanceID(): newer Unity versions made it an error (EntityId replaced it), and
                // this identity hash works the same in every version.
                long h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m);
                h = h * 31 + m.GetUsedTilesCount();
                h = h * 31 + b.xMin;
                h = h * 31 + b.yMin;
                h = h * 31 + b.size.x;
                h = h * 31 + b.size.y;
                h = h * 31 + (HasSolidCollider(m) ? 1 : 0);
                sig += h * 2654435761L;
            }
            return sig;
        }
    }

    private void Rebuild(Tilemap[] all)
    {
        EnsureRoot();
        DestroyPools();
        tileKinds.Clear();
        spriteKinds.Clear();

        if (floorManager == null) floorManager = FindAnyObjectByType<FloorManager>();
        lavaName = floorManager != null ? floorManager.LavaName : fallbackLavaName;
        waterName = floorManager != null ? floorManager.WaterName : fallbackWaterName;

        // The same Tilemaps FloorManager reads its hazards from. Without a FloorManager: every Tilemap.
        Tilemap[] candidates = floorManager != null ? floorManager.HazardTilemapList : all;
        if (candidates != null)
        {
            for (int i = 0; i < candidates.Length; i++)
            {
                Tilemap map = candidates[i];
                if (map == null || !map.gameObject.activeInHierarchy) continue;
                ScanMap(map);
            }
        }

        FindWalls(all);

        for (int i = 0; i < pools.Count; i++) BakeGlow(pools[i]);

        RefreshSorting();
        ApplySorting();
        RefreshVisible();
    }

    // Finds every lava and water tile of one Tilemap.
    private void ScanMap(Tilemap map)
    {
        BoundsInt b = map.cellBounds;
        b.zMin = 0;   // like FloorManager: only tiles at Z 0 count
        b.zMax = 1;
        if (b.size.x <= 0 || b.size.y <= 0) return;

        TileBase[] tiles = map.GetTilesBlock(b);
        Pool[] made = new Pool[KindCount];

        int i = 0;
        for (int y = b.yMin; y < b.yMax; y++)
        {
            for (int x = b.xMin; x < b.xMax; x++, i++)
            {
                if (i >= tiles.Length) return;
                TileBase tile = tiles[i];
                if (tile == null) continue;

                Vector3Int cell = new Vector3Int(x, y, 0);
                int kind = KindOfTile(tile, map, cell);
                if (kind == None) continue;

                if (made[kind] == null)
                {
                    made[kind] = new Pool { map = map, kind = kind, phase = Random.value * 6.2832f, cellSize = CellWorldSize(map) };
                    pools.Add(made[kind]);
                }
                made[kind].cells.Add(cell);
                made[kind].set.Add(cell);
            }
        }
    }

    // Lava or water? Same rule as FloorManager: the name of the tile asset OR of its sprite.
    private int KindOfTile(TileBase tile, Tilemap map, Vector3Int cell)
    {
        int kind;
        if (!tileKinds.TryGetValue(tile, out kind))
        {
            kind = KindOfName(tile.name);
            tileKinds[tile] = kind;
        }
        if (kind != None) return kind;

        Sprite sprite = map.GetSprite(cell);
        if (sprite == null) return None;

        if (!spriteKinds.TryGetValue(sprite, out kind))
        {
            kind = KindOfName(sprite.name);
            spriteKinds[sprite] = kind;
        }
        return kind;
    }

    private int KindOfName(string candidate)
    {
        if (FloorManager.TileNameHas(candidate, lavaName)) return Lava;
        if (FloorManager.TileNameHas(candidate, waterName)) return Water;
        return None;
    }

    private static Vector2 CellWorldSize(Tilemap map)
    {
        Vector3 a = map.GetCellCenterWorld(Vector3Int.zero);
        Vector3 bx = map.GetCellCenterWorld(Vector3Int.right);
        Vector3 by = map.GetCellCenterWorld(Vector3Int.up);
        float w = Mathf.Abs(bx.x - a.x);
        float h = Mathf.Abs(by.y - a.y);
        return new Vector2(w > 0.0001f ? w : 1f, h > 0.0001f ? h : 1f);
    }

    private bool IsPoolMap(Tilemap map)
    {
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].map == map) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ walls

    private void FindWalls(Tilemap[] all)
    {
        wallMaps.Clear();

        if (wallTilemaps != null && wallTilemaps.Length > 0)
        {
            for (int i = 0; i < wallTilemaps.Length; i++)
            {
                if (wallTilemaps[i] != null) wallMaps.Add(wallTilemaps[i]);
            }
        }
        else
        {
            for (int i = 0; i < all.Length; i++)
            {
                Tilemap m = all[i];
                if (m == null || !m.gameObject.activeInHierarchy || IsPoolMap(m)) continue;
                if (HasSolidCollider(m)) wallMaps.Add(m);
            }
        }

        wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(wallMask);
        wallFilter.useTriggers = false;
    }

    private static bool HasSolidCollider(Tilemap map)
    {
        TilemapCollider2D tc = map.GetComponent<TilemapCollider2D>();
        if (tc != null && tc.enabled && !tc.isTrigger) return true;

        CompositeCollider2D cc = map.GetComponent<CompositeCollider2D>();
        return cc != null && cc.enabled && !cc.isTrigger;
    }

    private bool HasWalls
    {
        get { return wallMaps.Count > 0 || wallMask.value != 0; }
    }

    // True if this exact point is inside a wall.
    private bool IsWallAt(Vector2 p)
    {
        for (int i = 0; i < wallMaps.Count; i++)
        {
            Tilemap m = wallMaps[i];
            if (m == null) continue;

            Vector3Int c = m.WorldToCell(p);
            c.z = 0;   // a Tilemap at a different Z would otherwise look one layer off and find nothing
            if (m.HasTile(c)) return true;
        }

        if (wallMask.value != 0 && Physics2D.OverlapPoint(p, wallFilter, wallBuffer) > 0) return true;
        return false;
    }

    // Inside a wall, or closer to one than "Wall Padding".
    private bool IsNearWall(Vector2 p)
    {
        if (IsWallAt(p)) return true;
        if (wallPadding <= 0f) return false;

        return IsWallAt(p + new Vector2(wallPadding, 0f)) ||
               IsWallAt(p - new Vector2(wallPadding, 0f)) ||
               IsWallAt(p + new Vector2(0f, wallPadding)) ||
               IsWallAt(p - new Vector2(0f, wallPadding));
    }

    // ------------------------------------------------------------ layers

    // Works out where the effects sit: the players' sorting layer, a little above the highest player, but below any wall
    // that is drawn in front of the players.
    private void RefreshSorting()
    {
        bool foundPlayer = false;
        int layer = 0;
        int top = 0;
        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            if (p == null) continue;

            SpriteRenderer r = p.GetComponentInChildren<SpriteRenderer>();
            if (r == null) continue;

            if (!foundPlayer)
            {
                layer = r.sortingLayerID;
                top = r.sortingOrder;
                foundPlayer = true;
            }
            else if (r.sortingLayerID == layer)
            {
                top = Mathf.Max(top, r.sortingOrder);
            }
        }

        int tileLayer = 0;
        int tileOrder = 0;
        if (pools.Count > 0 && pools[0].map != null)
        {
            TilemapRenderer tr = pools[0].map.GetComponent<TilemapRenderer>();
            if (tr != null)
            {
                tileLayer = tr.sortingLayerID;
                tileOrder = tr.sortingOrder;
            }
        }

        int fxLayer = foundPlayer ? layer : tileLayer;
        int above = foundPlayer ? top : tileOrder;
        int fxOrder = above + sortingOrdersAbovePlayers;

        // A wall drawn in front of the players on the same layer: stay one order below its lowest tile.
        int ceiling = int.MaxValue;
        for (int i = 0; i < wallMaps.Count; i++)
        {
            Tilemap m = wallMaps[i];
            if (m == null) continue;

            TilemapRenderer wr = m.GetComponent<TilemapRenderer>();
            if (wr == null || wr.sortingLayerID != fxLayer || wr.sortingOrder <= above) continue;
            ceiling = Mathf.Min(ceiling, wr.sortingOrder);
        }

        if (ceiling != int.MaxValue && fxOrder >= ceiling)
        {
            fxOrder = ceiling - 1;
            if (fxOrder <= above && !warnedSorting)
            {
                warnedSorting = true;
                Debug.LogWarning("HazardTileFx: a wall is drawn right in front of the players (sorting order " + ceiling + " against " + above +
                                 "), so there is no room for the effects between them. They are drawn behind the players for now. " +
                                 "Leave a gap of 3 or more sorting orders between the players and the walls to get them in front.", this);
            }
        }

        pieceLayerId = fxLayer;
        pieceOrder = fxOrder;

        if (glowOverCharacters)
        {
            glowLayerId = fxLayer;
            glowOrder = (fxOrder - 1 > above) ? fxOrder - 1 : fxOrder;   // one order under the pieces when there is room, still in front of the players
        }
        else
        {
            glowLayerId = tileLayer;
            glowOrder = tileOrder + 1;   // sits on the tiles, behind the characters
        }
    }

    private void ApplySorting()
    {
        for (int i = 0; i < pools.Count; i++)
        {
            SpriteRenderer sr = pools[i].glowRenderer;
            if (sr == null) continue;
            sr.sortingLayerID = glowLayerId;
            sr.sortingOrder = glowOrder;
        }

        if (hops == null) return;
        for (int i = 0; i < hops.Length; i++)
        {
            hops[i].sr.sortingLayerID = pieceLayerId;
            hops[i].sr.sortingOrder = pieceOrder;
        }
    }

    // ------------------------------------------------------------ the glow

    // Bakes ONE soft texture for a whole pool: white, with an alpha that is 0 outside the lava / water tiles (and under walls),
    // fades in from the edge and varies a little from place to place. The sprite renderer only tints it. Because the alpha is 0 off
    // the tiles, the glow cannot spill onto the floor or the walls around the pool.
    private void BakeGlow(Pool pool)
    {
        Tilemap map = pool.map;
        if (map == null || pool.cells.Count == 0) return;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < pool.cells.Count; i++)
        {
            Vector3Int c = pool.cells[i];
            if (c.x < minX) minX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }

        int w = maxX - minX + 1;
        int h = maxY - minY + 1;

        // Which tiles of the pool get glow: every one that has no wall on it.
        bool[] foot = new bool[w * h];
        bool any = false;
        for (int i = 0; i < pool.cells.Count; i++)
        {
            Vector3Int c = pool.cells[i];
            if (IsWallAt(map.GetCellCenterWorld(c))) continue;
            foot[(c.y - minY) * w + (c.x - minX)] = true;
            any = true;
        }
        if (!any) return;

        int k = Mathf.Clamp(glowTexelsPerTile, 2, 16);
        while (k > 2 && (w * k > 2048 || h * k > 2048)) k--;
        int tw = w * k;
        int th = h * k;
        if (tw > 2048 || th > 2048)
        {
            Debug.LogWarning("HazardTileFx: a pool of " + (pool.kind == Lava ? "lava" : "water") + " is " + w + " x " + h +
                             " tiles, too big to bake a glow for. It gets no glow (particles and sound still work).", this);
            return;
        }

        float[] v = new float[tw * th];
        for (int ty = 0; ty < th; ty++)
        {
            int cy = ty / k;
            for (int tx = 0; tx < tw; tx++)
            {
                if (foot[cy * w + tx / k]) v[ty * tw + tx] = 1f;
            }
        }

        bool soft = glowEdgeSoftness > 0.01f;
        if (soft)
        {
            int radius = Mathf.Max(1, Mathf.RoundToInt(glowEdgeSoftness * k));
            float[] temp = new float[v.Length];
            BoxBlur(v, temp, tw, th, radius);
            BoxBlur(v, temp, tw, th, radius);
        }

        float seedX = Random.Range(0f, 100f);
        float seedY = Random.Range(0f, 100f);
        Color32[] pixels = new Color32[tw * th];
        for (int ty = 0; ty < th; ty++)
        {
            float worldY = minY + (ty + 0.5f) / k;
            for (int tx = 0; tx < tw; tx++)
            {
                int index = ty * tw + tx;
                float value = v[index];
                float edge = soft ? Mathf.Clamp01((value - 0.5f) * GlowEdgeGain) : value;
                float worldX = minX + (tx + 0.5f) / k;
                float variation = 0.7f + 0.3f * Mathf.PerlinNoise(worldX * 0.4f + seedX, worldY * 0.4f + seedY);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(edge * variation) * 255f);
                pixels[index] = new Color32(255, 255, 255, alpha);
            }
        }

        Texture2D tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = soft ? FilterMode.Bilinear : FilterMode.Point;
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tw, th), Vector2.zero, k, 0, SpriteMeshType.FullRect);

        // The sprite is w x h units big (k texels per unit); scaling by the tile size lays it exactly over the tiles.
        Vector3 first = map.GetCellCenterWorld(new Vector3Int(minX, minY, 0));
        GameObject go = new GameObject(pool.kind == Lava ? "Glow (lava)" : "Glow (water)");
        go.transform.SetParent(root.transform, false);
        go.transform.position = new Vector3(first.x - pool.cellSize.x * 0.5f, first.y - pool.cellSize.y * 0.5f, 0.01f);   // a hair behind the pieces
        go.transform.localScale = new Vector3(pool.cellSize.x, pool.cellSize.y, 1f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        if (glowMaterial != null) sr.sharedMaterial = glowMaterial;
        sr.color = new Color(1f, 1f, 1f, 0f);

        pool.glowObject = go;
        pool.glowRenderer = sr;
        pool.glowTexture = tex;
        pool.glowSprite = sprite;
    }

    // A blur that is fast on big grids (a sliding window). Anything outside the grid counts as 0.
    private static void BoxBlur(float[] data, float[] temp, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            float sum = 0f;
            for (int x = 0; x <= r && x < w; x++) sum += data[row + x];
            for (int x = 0; x < w; x++)
            {
                temp[row + x] = sum * inv;
                int add = x + r + 1;
                int remove = x - r;
                if (add < w) sum += data[row + add];
                if (remove >= 0) sum -= data[row + remove];
            }
        }

        for (int x = 0; x < w; x++)
        {
            float sum = 0f;
            for (int y = 0; y <= r && y < h; y++) sum += temp[y * w + x];
            for (int y = 0; y < h; y++)
            {
                data[y * w + x] = sum * inv;
                int add = y + r + 1;
                int remove = y - r;
                if (add < h) sum += temp[add * w + x];
                if (remove >= 0) sum -= temp[remove * w + x];
            }
        }
    }

    private void UpdateGlow()
    {
        for (int i = 0; i < pools.Count; i++)
        {
            Pool pool = pools[i];
            if (pool.glowRenderer == null) continue;

            Look look = pool.kind == Lava ? lava : water;
            bool on = glowEnabled && look.enabled && look.glow && look.glowStrength > 0f;
            pool.glowRenderer.enabled = on;
            if (!on) continue;

            float breathe = 0.5f + 0.5f * Mathf.Sin(clock * (6.2832f / look.glowPulseSeconds) + pool.phase);
            float strength = look.glowStrength * (1f - look.glowPulse + look.glowPulse * breathe);

            Color c = look.glowColour;
            c.a = Mathf.Clamp01(strength);
            pool.glowRenderer.color = c;
        }
    }

    private void DestroyPools()
    {
        for (int i = 0; i < pools.Count; i++)
        {
            Pool pool = pools[i];
            if (pool.glowObject != null) Destroy(pool.glowObject);
            if (pool.glowSprite != null) Destroy(pool.glowSprite);
            if (pool.glowTexture != null) Destroy(pool.glowTexture);
        }
        pools.Clear();
    }

    // ------------------------------------------------------------ what is on screen

    private bool ViewRect(out Rect rect)
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            rect = new Rect();
            return false;
        }

        float halfHeight = cam.orthographicSize + 1f;
        float halfWidth = cam.orthographicSize * cam.aspect + 1f;
        Vector3 c = cam.transform.position;
        rect = new Rect(c.x - halfWidth, c.y - halfHeight, halfWidth * 2f, halfHeight * 2f);
        return true;
    }

    // The tiles worth popping pieces out of: the ones near the camera. Refreshed a few times a second.
    private void RefreshVisible()
    {
        Rect view;
        bool haveView = ViewRect(out view);

        for (int i = 0; i < pools.Count; i++)
        {
            Pool pool = pools[i];
            pool.visible.Clear();
            if (pool.map == null) continue;

            if (!haveView)
            {
                pool.visible.AddRange(pool.cells);
                continue;
            }

            Vector3Int a = pool.map.WorldToCell(new Vector3(view.xMin, view.yMin, 0f));
            Vector3Int b = pool.map.WorldToCell(new Vector3(view.xMax, view.yMax, 0f));
            int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
            int y0 = Mathf.Min(a.y, b.y), y1 = Mathf.Max(a.y, b.y);

            for (int c = 0; c < pool.cells.Count; c++)
            {
                Vector3Int cell = pool.cells[c];
                if (cell.x >= x0 && cell.x <= x1 && cell.y >= y0 && cell.y <= y1) pool.visible.Add(cell);
            }
        }
    }

    private bool PickVisibleCell(int kind, out Pool pool, out Vector3Int cell)
    {
        pool = null;
        cell = Vector3Int.zero;

        int total = 0;
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].kind == kind) total += pools[i].visible.Count;
        }
        if (total == 0) return false;

        int roll = Random.Range(0, total);
        for (int i = 0; i < pools.Count; i++)
        {
            Pool p = pools[i];
            if (p.kind != kind) continue;
            if (roll < p.visible.Count)
            {
                pool = p;
                cell = p.visible[roll];
                return true;
            }
            roll -= p.visible.Count;
        }
        return false;
    }

    // Which lava / water tile is under this point? Lava wins where both are on one cell.
    private bool FindHazard(Vector2 point, out Pool pool, out Vector3Int cell)
    {
        pool = null;
        cell = Vector3Int.zero;

        for (int i = 0; i < pools.Count; i++)
        {
            Pool p = pools[i];
            if (p.map == null) continue;

            Vector3Int c = p.map.WorldToCell(point);
            c.z = 0;
            if (!p.set.Contains(c)) continue;

            if (pool == null || p.kind == Lava)
            {
                pool = p;
                cell = c;
            }
        }
        return pool != null;
    }

    // ------------------------------------------------------------ the players

    private Vector2 FeetPoint(CoopHealth player)
    {
        if (floorManager != null) return floorManager.HazardPointOf(player);   // the same spot that decides who gets burned
        return player.transform.position;
    }

    private void TrackPlayers(float dt)
    {
        bool blocked = FloorController.BlocksInput;   // black screen: nobody is walking

        for (int n = 1; n <= MaxPlayers; n++)
        {
            Walker w = walkers[n];
            CoopHealth p = CoopHealth.Get(n);
            if (blocked || p == null || !p.IsAlive || !CoopRules.Counts(n))
            {
                w.has = false;
                w.kind = None;
                continue;
            }

            Vector2 feet = FeetPoint(p);
            bool first = !w.has;
            float moved = 0f;
            bool teleported = false;
            if (w.has)
            {
                moved = (feet - w.last).magnitude;
                if (moved > TeleportDistance)
                {
                    teleported = true;
                    moved = 0f;
                }
            }
            w.last = feet;
            w.has = true;

            Pool pool;
            Vector3Int cell;
            int kind = FindHazard(feet, out pool, out cell) ? pool.kind : None;
            if (kind != None && !(kind == Lava ? lava : water).enabled) kind = None;

            if (kind != w.kind)
            {
                if (kind != None && !first && !teleported) Splash(pool, cell, feet);
                w.kind = kind;
                w.distance = 0f;
                w.hopTimer = 0f;
            }
            if (kind == None) continue;

            bool moving = dt > 0f && moved / dt >= minWalkSpeed;
            if (!moving) continue;

            Look look = kind == Lava ? lava : water;

            w.distance += moved;
            if (w.distance >= stepDistance && Time.time - w.lastStepTime >= minStepSeconds)
            {
                w.distance = 0f;
                w.lastStepTime = Time.time;
                PlaySound(look, look.stepClips, feet);
            }

            float level = Multiplier(look.walkingLevel);
            if (particlesEnabled && level > 0f)
            {
                w.hopTimer -= dt;
                if (w.hopTimer <= 0f)
                {
                    w.hopTimer = RollFloat(walkingSecondsBetween) / level;
                    WalkingHop(pool, cell, feet);
                }
            }
        }
    }

    // A player has just stepped onto lava or water.
    private void Splash(Pool pool, Vector3Int cell, Vector2 feet)
    {
        Look look = pool.kind == Lava ? lava : water;

        AudioClip[] clips = (look.splashClips != null && look.splashClips.Length > 0) ? look.splashClips : look.stepClips;
        PlaySound(look, clips, feet);

        float level = Multiplier(look.walkingLevel);
        if (!particlesEnabled || level <= 0f) return;

        int count = Mathf.RoundToInt(RollInt(splashPieces) * level);
        for (int i = 0; i < count; i++) WalkingHop(pool, cell, feet);
    }

    private void WalkingHop(Pool pool, Vector3Int cell, Vector2 feet)
    {
        Vector2 start = feet + new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(-0.12f, 0.12f));

        Vector2 ignored;
        Color colour;
        SampleTile(pool, cell, out ignored, out colour);   // only the colour: the piece starts at the player's feet
        SpawnHop(start, colour, RollFloat(walkingHopSeconds));
    }

    // ------------------------------------------------------------ ambient hops

    private void TickAmbient(float dt)
    {
        for (int kind = 0; kind < KindCount; kind++)
        {
            ambientTimer[kind] -= dt;
            if (ambientTimer[kind] > 0f) continue;

            Look look = kind == Lava ? lava : water;
            float level = Multiplier(look.ambientLevel);
            if (!look.enabled || level <= 0f)
            {
                ambientTimer[kind] = 0.5f;
                continue;
            }

            ambientTimer[kind] = RollFloat(ambientSecondsBetween) / level;

            Pool pool;
            Vector3Int cell;
            if (!PickVisibleCell(kind, out pool, out cell)) continue;

            Vector2 start;
            Color colour;
            SampleTile(pool, cell, out start, out colour);
            SpawnHop(start, colour, RollFloat(ambientHopSeconds));
        }
    }

    // ------------------------------------------------------------ the pieces

    // Picks a random solid pixel of the tile's sprite and returns its colour and where on the tile it is. This is the "one sprite
    // taken as a colour". Always gives a usable answer: the tile's middle and the fallback colour if the pixels can't be read.
    private bool SampleTile(Pool pool, Vector3Int cell, out Vector2 world, out Color colour)
    {
        Look look = pool.kind == Lava ? lava : water;
        Vector3 centre = pool.map.GetCellCenterWorld(cell);
        world = centre;
        colour = look.fallbackColour;

        Sprite sprite = pool.map.GetSprite(cell);
        if (sprite == null) return false;

        RawSprite raw = ReadSprite(sprite);
        if (raw == null || raw.solid == null || raw.solid.Length == 0) return false;

        int index = raw.solid[Random.Range(0, raw.solid.Length)];
        int px = index % raw.w;
        int py = index / raw.w;

        // Where that pixel is on the tile (the sprite is taken to fill the tile).
        world = new Vector2(centre.x + ((px + 0.5f) / raw.w - 0.5f) * pool.cellSize.x,
                            centre.y + ((py + 0.5f) / raw.h - 0.5f) * pool.cellSize.y);

        Color c = raw.pixels[index] * pool.map.GetColor(cell) * pool.map.color;   // the tile's tint counts too
        c.a = 1f;
        colour = c;
        return true;
    }

    private void EnsureHops()
    {
        if (hops != null) return;
        EnsureRoot();

        Sprite sq = Square();
        hops = new Hop[Mathf.Clamp(maxPieces, 4, 256)];
        for (int i = 0; i < hops.Length; i++)
        {
            GameObject go = new GameObject("piece");
            go.transform.SetParent(root.transform, false);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sq;
            sr.sortingLayerID = pieceLayerId;
            sr.sortingOrder = pieceOrder;
            sr.enabled = false;

            hops[i] = new Hop { t = go.transform, sr = sr };
        }
    }

    private void SpawnHop(Vector2 from, Color colour, float seconds)
    {
        if (IsWallAt(from)) return;   // never start inside a wall

        EnsureHops();

        Hop hop = null;
        for (int i = 0; i < hops.Length; i++)
        {
            if (!hops[i].active)
            {
                hop = hops[i];
                break;
            }
        }
        if (hop == null) return;   // every piece is busy

        float distance = RollFloat(hopDistancePixels) / pixelsPerUnit;
        float height = RollFloat(hopHeightPixels) / pixelsPerUnit;

        // Try a few directions and keep the one that gets furthest before a wall (all of them are fine when there are no walls).
        Vector2 to = from;
        float best = -1f;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float angle = Random.value * 6.2832f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 end = ReachableEnd(from, dir, distance);
            float reach = (end - from).magnitude;
            if (reach > best)
            {
                best = reach;
                to = end;
            }
            if (reach >= distance * 0.99f) break;
        }

        hop.from = from;
        hop.to = to;
        hop.height = FitHeight(from, to, height);
        hop.life = Mathf.Max(0.1f, seconds);
        hop.age = 0f;
        hop.size = Mathf.Max(0.5f, RollFloat(pieceSizePixels)) / pixelsPerUnit;
        hop.colour = colour;
        hop.active = true;
        activeHops++;

        hop.t.position = new Vector3(from.x, from.y, 0f);
        hop.t.localScale = new Vector3(hop.size, hop.size, 1f);
        hop.sr.sortingLayerID = pieceLayerId;
        hop.sr.sortingOrder = pieceOrder;
        Color c = colour;
        c.a = 0f;
        hop.sr.color = c;
        hop.sr.enabled = true;
    }

    // The furthest point along a direction (up to "distance") that is still clear of walls.
    private Vector2 ReachableEnd(Vector2 from, Vector2 dir, float distance)
    {
        if (!HasWalls) return from + dir * distance;

        const float step = 0.1f;
        Vector2 last = from;
        float travelled = 0f;
        while (travelled < distance)
        {
            travelled = Mathf.Min(distance, travelled + step);
            Vector2 p = from + dir * travelled;
            if (IsNearWall(p)) break;
            last = p;
        }
        return last;
    }

    // The jump is drawn higher up the screen than the ground it flies over, so a piece under a wall must jump lower (or not at all).
    private float FitHeight(Vector2 from, Vector2 to, float height)
    {
        if (!HasWalls) return height;

        Vector2 quarter = Vector2.Lerp(from, to, 0.25f);
        Vector2 middle = Vector2.Lerp(from, to, 0.5f);
        Vector2 threeQuarters = Vector2.Lerp(from, to, 0.75f);

        for (int i = 0; i < 4; i++)
        {
            float h = height * (1f - i * 0.33f);
            if (h <= 0.001f) return 0f;

            Vector2 lift = new Vector2(0f, h);
            Vector2 liftSides = new Vector2(0f, h * 0.75f);
            if (!IsNearWall(middle + lift) && !IsNearWall(quarter + liftSides) && !IsNearWall(threeQuarters + liftSides)) return h;
        }
        return 0f;
    }

    private void UpdateHops(float dt)
    {
        if (hops == null || activeHops == 0) return;

        for (int i = 0; i < hops.Length; i++)
        {
            Hop h = hops[i];
            if (!h.active) continue;

            h.age += dt;
            float t = h.age / h.life;
            if (t >= 1f)
            {
                h.active = false;
                h.sr.enabled = false;
                activeHops--;
                continue;
            }

            // Sideways at an even pace, up and down in an arc: a hop.
            Vector2 pos = Vector2.Lerp(h.from, h.to, t);
            pos.y += h.height * 4f * t * (1f - t);
            h.t.position = new Vector3(pos.x, pos.y, 0f);

            float size = h.size * Mathf.Lerp(1f, 0.6f, t);
            h.t.localScale = new Vector3(size, size, 1f);

            float appear = Mathf.Clamp01(t / 0.12f);
            float fade = fadeStartsAt >= 1f ? 1f : 1f - Mathf.Clamp01((t - fadeStartsAt) / (1f - fadeStartsAt));
            Color c = h.colour;
            c.a = appear * fade;
            h.sr.color = c;
        }
    }

    private void ClearHops()
    {
        if (hops == null) return;
        for (int i = 0; i < hops.Length; i++)
        {
            hops[i].active = false;
            hops[i].sr.enabled = false;
        }
        activeHops = 0;
    }

    // ------------------------------------------------------------ sound

    private void EnsureSources()
    {
        if (sources != null) return;
        EnsureRoot();

        sources = new AudioSource[6];   // a few at once, each with its own pitch
        for (int i = 0; i < sources.Length; i++)
        {
            GameObject go = new GameObject("sound");
            go.transform.SetParent(root.transform, false);

            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.outputAudioMixerGroup = mixerGroup;
            sources[i] = s;
        }
    }

    private void PlaySound(Look look, AudioClip[] clips, Vector2 where)
    {
        if (!soundEnabled || !look.sound || clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        EnsureSources();
        AudioSource s = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;

        s.transform.position = new Vector3(where.x, where.y, 0f);
        s.Stop();
        s.clip = clip;
        s.volume = Mathf.Clamp01(RollFloat(look.volume));
        s.pitch = Mathf.Max(0.05f, RollFloat(look.pitch));
        s.spatialBlend = spatialBlend;
        s.Play();
    }

    // ------------------------------------------------------------ reading a sprite's pixels

    private static RawSprite ReadSprite(Sprite sprite)
    {
        RawSprite cached;
        if (pixelCache.TryGetValue(sprite, out cached)) return cached;

        RawSprite result = null;
        try
        {
            Texture2D tex = sprite.texture;
            Rect rect = sprite.textureRect;
            int x = Mathf.RoundToInt(rect.x), y = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);

            Color[] pixels = null;
            if (tex.isReadable)
            {
                try { pixels = tex.GetPixels(x, y, w, h); }
                catch { pixels = null; }   // e.g. a compressed format: fall through to the GPU read
            }
            if (pixels == null) pixels = ReadViaGpu(tex, x, y, w, h);

            if (pixels != null)
            {
                List<int> solid = new List<int>(pixels.Length);
                for (int i = 0; i < pixels.Length; i++)
                {
                    if (pixels[i].a >= 0.2f) solid.Add(i);
                }
                result = new RawSprite { pixels = pixels, w = w, h = h, solid = solid.ToArray() };
            }
        }
        catch (System.Exception)
        {
            result = null;
        }

        if (pixelCache.Count > 256) pixelCache.Clear();   // a safety cap
        pixelCache[sprite] = result;
        return result;
    }

    // Works on a texture that does NOT have Read/Write enabled: copy it to a temporary render texture and read that.
    private static Color[] ReadViaGpu(Texture2D tex, int x, int y, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(x, y, w, h), 0, 0);
            return copy.GetPixels();
        }
        catch (System.Exception)
        {
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null) Destroy(copy);
        }
    }

    // A 1x1 white sprite one world unit wide, shared by every piece (each piece scales and tints it).
    private static Sprite Square()
    {
        if (square != null) return square;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        square = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return square;
    }

    // ------------------------------------------------------------ small helpers

    private static float Multiplier(FxLevel level)
    {
        switch (level)
        {
            case FxLevel.Low: return 0.5f;
            case FxLevel.Medium: return 1f;
            case FxLevel.High: return 2f;
            default: return 0f;
        }
    }

    // Random number between two values, whichever order they were typed in.
    private static float RollFloat(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    // Random whole number between two values, BOTH included.
    private static int RollInt(Vector2Int range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);
    }
}
