using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using Ilumisoft.HealthSystem;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random.

/// <summary>
/// The look and sound of lava and water tiles. Put it on ONE empty object in the scene (FloorManager finds the same tiles
/// it hurts with, so what glows is exactly what burns). It builds everything else by itself: no prefabs, NO MATERIAL TO MAKE,
/// nothing to wire up except the sound clips. (The glow materials are made from HazardGlow.shader while the game runs.
/// Don't put that shader on your Tilemaps: this script uses it on its own sprites.)
///
///   GLOW        a fake light for a top-down sprite game, in two soft layers baked from the tile shapes:
///                 Additive    the light is ADDED to what is underneath: hot, bright, a bit like fire.
///                 Soft Light  the light TINTS and brightens the art underneath (bright pixels more than dark ones) instead of washing
///                             over it, so the tile art and the characters keep all their detail. Looks like a real light.
///                 Both        (default) soft light for the main colour, additive for the shimmering highlight.
///               The two layers breathe out of step, so bright patches seem to drift over the pool. The light BLEEDS out of the pool
///               onto the tiles around it ("Glow Bleed", in tiles) but never onto or past a wall: it is cut off clean at the wall's edge
///               and does not wrap through it. It is drawn in front of the characters, so a player standing in lava is lit by it.
///               "Opacity" on Lava and Water and "Glow Opacity (all)" set how strong it is.
///   PARTICLES   small square pieces that take the colour of a random pixel of the tile's own sprite and hop out of the tile:
///               slowly, at random times, with no help from anybody (AMBIENT); a few on EVERY STEP a player takes on it; and a
///               bigger splash when a player steps in. Every kind has its own level: Off / Low / Medium / High.
///               Pieces are drawn in front of the characters, but they never land on or pass over a wall.
///   SOUND       a step sound on every step over lava or water, and a splash sound when they step in. Put clips into
///               "Step Clips" / "Splash Clips" on Lava and Water; one is picked at random each time. If only one of the two lists
///               has clips, it is used for both.
///
/// A STEP is a fixed distance walked on the tile (see "Step Distance"), so running makes quicker steps, standing still makes none.
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
/// WALLS AND LAYERS: the effects are drawn on the players' sorting layer, a little above the highest player, so pieces and glow
/// can go in front of the characters. Walls are found by themselves (Tilemaps with a solid Tilemap Collider 2D); a piece is never
/// started in a wall or sent through one, and the glow stops at walls. If a wall Tilemap is drawn in front of the players,
/// the effects are kept just below it. If something that is not a wall gets taken for one (a player stands on lava "inside" it),
/// it is dropped from the walls after half a second and a warning tells you which Tilemap it was. Fill in "Wall Tilemaps" and / or
/// "Wall Mask" to set the walls yourself.
///
/// ALIGNMENT: the glow is laid on the tile GRID, then moved by however far the tile art is drawn from its grid cell (a Tilemap draws
/// a sprite's pivot on its "Tile Anchor", so a sprite with a corner pivot sits off its cell). If the glow still sits a little off, use
/// "Glow Offset" (world units) to nudge it.
///
/// If something does not show up, look at the one line this script writes to the Console when it starts: it says how many lava and
/// water tiles it found, which Tilemaps count as walls, where it draws, and whether the glow shader was found.
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

    /// <summary>How the glow is mixed into the picture.</summary>
    public enum GlowMode
    {
        Additive,               // the light is added to what is underneath: hot and bright
        SoftLight,              // the light tints and brightens the art underneath, keeping its detail
        SoftLightAndAdditive    // soft light for the main colour, additive for the shimmering highlight
    }

    /// <summary>Everything that can be set for ONE kind of tile (shown once for Lava and once for Water).</summary>
    [System.Serializable]
    public class Look
    {
        [Tooltip("Switches everything for this kind of tile off: glow, particles and sound.")]
        public bool enabled = true;

        [Header("Glow")]
        [Tooltip("The fake light over the tiles.")]
        public bool glow = true;
        [Tooltip("The main colour of the light.")]
        public Color glowColour = Color.white;
        [Tooltip("The second, brighter colour of the light. It shimmers in and out over the main colour.")]
        public Color glowHighlight = Color.white;
        [Tooltip("Opacity of the glow for this kind of tile: 0 = invisible, 1 = as strong as it gets. 0.3 to 0.8 looks good.")]
        [FormerlySerializedAs("glowStrength")]
        [Range(0f, 1f)] public float glowOpacity = 0.6f;
        [Tooltip("How much the whole glow breathes. 0 = steady, 1 = fades almost out and back.")]
        [Range(0f, 1f)] public float glowPulse = 0.35f;
        [Tooltip("Seconds for one slow breath of the glow.")]
        [Min(0.2f)] public float glowPulseSeconds = 3.5f;

        [Header("Particles")]
        [Tooltip("How often a piece hops out of the tiles on its own (slow, at random times). Only tiles on screen are used.")]
        public FxLevel ambientLevel = FxLevel.Medium;
        [Tooltip("How many pieces pop out on every STEP on this tile, and how big the splash is when a player steps in.")]
        public FxLevel walkingLevel = FxLevel.Medium;
        [Tooltip("The colour of a piece if the tile's sprite pixels can't be read at all.")]
        public Color fallbackColour = Color.white;

        [Header("Sound")]
        [Tooltip("Step sound and splash sound on or off for this tile.")]
        public bool sound = true;
        [Tooltip("Played on every step on this tile. One is picked at random. Empty = the splash clips are used; both empty = silent.")]
        public AudioClip[] stepClips;
        [Tooltip("Played when a player steps onto this tile. Empty = the step clips are used.")]
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
    [Tooltip("The fake light over the tiles.")]
    [SerializeField] private bool glowEnabled = true;
    [Tooltip("The pieces that hop out of the tiles (ambient, steps and splash).")]
    [SerializeField] private bool particlesEnabled = true;
    [Tooltip("Step and splash sounds.")]
    [SerializeField] private bool soundEnabled = true;
    [Tooltip("ON: the glow is drawn in front of the characters, so a player standing in lava is lit by it. " +
             "OFF: the glow sits on the tiles, behind the characters.")]
    [SerializeField] private bool glowOverCharacters = true;

    [Header("Lava")]
    [SerializeField] private Look lava = new Look
    {
        glowColour = new Color(1f, 0.38f, 0.05f),
        glowHighlight = new Color(1f, 0.78f, 0.3f),
        glowOpacity = 0.7f,
        fallbackColour = new Color(1f, 0.5f, 0.1f)
    };

    [Header("Water")]
    [SerializeField] private Look water = new Look
    {
        glowColour = new Color(0.15f, 0.45f, 0.9f),
        glowHighlight = new Color(0.6f, 0.92f, 1f),
        glowOpacity = 0.55f,
        fallbackColour = new Color(0.5f, 0.8f, 1f)
    };

    [Header("Glow: look")]
    [Tooltip("Additive = hot and bright. Soft Light = tints and brightens the art underneath, keeping its detail. Both = soft light plus an additive shimmer.")]
    [SerializeField] private GlowMode glowMode = GlowMode.SoftLightAndAdditive;
    [Tooltip("Opacity of ALL glow, on top of the Opacity of Lava and Water. 1 = as set there, 0 = no glow at all.")]
    [SerializeField, Range(0f, 1f)] private float glowOpacityAll = 1f;
    [Tooltip("0 = smooth light. 3 to 8 = the light is cut into that many hard steps of brightness, which suits pixel art.")]
    [SerializeField, Range(0, 8)] private int glowBands = 0;

    [Header("Glow: bleed and shape")]
    [Tooltip("How far the light reaches OUT of the lava / water onto the tiles around it, in tiles. 0 = it stays exactly on the lava / water tiles. " +
             "It is cut off at walls and never goes through them.")]
    [SerializeField, Range(0f, 3f)] private float glowBleed = 1f;
    [Tooltip("ON: the light only bleeds onto tiles that exist (floor), not onto empty space around the map. " +
             "Turn it OFF if your floor is not made of tiles (a background sprite, for example).")]
    [SerializeField] private bool glowOnlyOverTiles = true;
    [Tooltip("Smooths the glow, in tiles. Fine detail in the shape of the edge is rounded off by this. 0 = no smoothing.")]
    [SerializeField, Range(0f, 1.5f)] private float glowEdgeSoftness = 0.35f;
    [Tooltip("Resolution of the baked glow, in pixels per tile. Higher is smoother but uses more memory.")]
    [SerializeField, Range(2, 16)] private int glowTexelsPerTile = 8;
    [Tooltip("Nudges the whole glow, in world units, if it sits a little off the tile art. It is already moved to match how the tiles are drawn.")]
    [SerializeField] private Vector2 glowOffset = Vector2.zero;
    [Tooltip("The glow shader (HazardGlow.shader). It fills itself in the Editor when the file is in your project. " +
             "Leave it in the field so it ships in builds. Empty and not found = a plain, weaker overlay instead.")]
    [SerializeField] private Shader glowShader;

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

    [Header("Particles: steps and splash")]
    [Tooltip("How many pieces pop out on every step, at Medium (random between the two).")]
    [SerializeField] private Vector2Int stepPieces = new Vector2Int(2, 4);
    [Tooltip("How many pieces the splash has when a player steps onto the tile, at Medium (random between the two).")]
    [SerializeField] private Vector2Int splashPieces = new Vector2Int(5, 8);
    [Tooltip("How long a step or splash hop lasts, in seconds.")]
    [SerializeField] private Vector2 walkingHopSeconds = new Vector2(0.5f, 0.9f);

    [Header("Steps (sound and particles)")]
    [Tooltip("One step is this far walked over the tile (world units). Every step plays a sound and pops pieces. Smaller = quicker steps.")]
    [SerializeField, Min(0.05f)] private float stepDistance = 0.5f;
    [Tooltip("Steps never come closer together than this (seconds).")]
    [SerializeField, Min(0f)] private float minStepSeconds = 0.12f;
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
    [Tooltip("Writes one line to the Console describing what was found (tile counts, walls, layers, glow). Handy when something doesn't show up.")]
    [SerializeField] private bool logSetup = true;
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

    /// <summary>Opacity of all the glow, 0 to 1 (the "Glow Opacity (all)" slider).</summary>
    public float GlowOpacity { get { return glowOpacityAll; } set { glowOpacityAll = Mathf.Clamp01(value); } }

    /// <summary>Re-reads every Tilemap on the next frame. Only needed if tiles are changed in a way Unity doesn't report.</summary>
    [ContextMenu("Rebuild Now")]
    public void RebuildNow()
    {
        dirty = true;
    }

#if UNITY_EDITOR
    // In the Editor the glow shader is picked up by itself (by name), so there is nothing to drag in. The reference stays
    // in the field, which is what gets the shader into a build.
    private void Reset()
    {
        FindGlowShaderInEditor();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) FindGlowShaderInEditor();
    }

    private void FindGlowShaderInEditor()
    {
        if (glowShader != null) return;

        Shader found = Shader.Find(GlowShaderName);
        if (found == null) return;

        glowShader = found;
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // ------------------------------------------------------------ state

    private const string GlowShaderName = "Sprites/Hazard Glow";
    private const int Lava = 0;
    private const int Water = 1;
    private const int None = -1;
    private const int KindCount = 2;
    private const int MaxPlayers = 4;
    private const int GlowLayers = 2;              // the main layer (0) and the highlight layer (1)
    private const int BlockTiles = 32;             // the glow is baked in blocks of 32 x 32 tiles, so empty space costs nothing
    private const float Unreached = 1e9f;          // "the light has not got here" in the distance maps
    private const float TeleportDistance = 1.5f;   // moving further than this in one frame is a teleport, not a step
    private const float MinMove = 0.002f;          // smaller moves are physics jitter, not walking
    private const float WallDoubtSeconds = 0.5f;   // a "wall" a player stands in for this long is not a wall

    // One baked block of light over a pool (a main layer and a highlight layer each get their own).
    private class GlowLayer
    {
        public int layer;
        public bool soft;
        public GameObject go;
        public SpriteRenderer sr;
        public Texture2D tex;
        public Sprite sprite;
    }

    // All the hazard tiles of ONE Tilemap that are one kind (lava or water), and the glow made for them.
    private class Pool
    {
        public Tilemap map;
        public int kind;
        public Vector2 cellSize;
        public Vector2 artOffset;   // how far the tile art is drawn from the middle of its grid cell
        public float phase;
        public readonly float[] seedX = new float[GlowLayers];
        public readonly float[] seedY = new float[GlowLayers];
        public readonly List<Vector3Int> cells = new List<Vector3Int>();
        public readonly HashSet<Vector3Int> set = new HashSet<Vector3Int>();
        public readonly List<Vector3Int> visible = new List<Vector3Int>();
        public readonly List<GlowLayer> glow = new List<GlowLayer>();
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
        public Tilemap doubtMap;   // an automatically found "wall" this player is standing in...
        public float doubt;        // ...and for how long
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
    private readonly HashSet<Tilemap> notWalls = new HashSet<Tilemap>();
    private readonly Dictionary<TileBase, int> tileKinds = new Dictionary<TileBase, int>();
    private readonly Dictionary<Sprite, int> spriteKinds = new Dictionary<Sprite, int>();
    private readonly Collider2D[] wallBuffer = new Collider2D[1];
    private readonly float[] ambientTimer = new float[KindCount];
    private readonly Walker[] walkers = new Walker[MaxPlayers + 1];

    private ContactFilter2D wallFilter;
    private FloorManager floorManager;
    private Tilemap[] allMaps = new Tilemap[0];
    private GameObject root;
    private Hop[] hops;
    private int activeHops;
    private AudioSource[] sources;
    private int nextSource;

    private Material additiveMaterial;
    private Material softMaterial;
    private Material plainMaterial;
    private bool materialsTried;
    private bool shaderOk;
    private bool autoWalls;
    private string lastLog;

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
        if (additiveMaterial != null) Destroy(additiveMaterial);
        if (softMaterial != null) Destroy(softMaterial);
        if (plainMaterial != null) Destroy(plainMaterial);
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
        allMaps = all;

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
        BuildMaterials();   // looks the shader up now so the setup line can say what it found

        for (int i = 0; i < pools.Count; i++) BakeGlow(pools[i]);

        RefreshSorting();
        ApplySorting();
        RefreshVisible();
        LogSetup();
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
                    Vector2 size = CellWorldSize(map);
                    Pool pool = new Pool { map = map, kind = kind, phase = Random.value * 6.2832f, cellSize = size, artOffset = ArtOffset(map, cell, size) };
                    for (int l = 0; l < GlowLayers; l++)
                    {
                        pool.seedX[l] = Random.Range(0f, 200f);
                        pool.seedY[l] = Random.Range(0f, 200f);
                    }
                    made[kind] = pool;
                    pools.Add(pool);
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

    // How far the tile ART is drawn from the middle of its grid cell. A Tilemap draws a sprite with its pivot on the map's Tile Anchor
    // (the middle of the cell by default), so a sprite whose pivot is a corner, or a map whose anchor is not the middle, shows the tile
    // shifted off the grid. (0, 0) for the usual centred sprite. The glow is moved by this so it lands on the art and not on the grid.
    private static Vector2 ArtOffset(Tilemap map, Vector3Int cell, Vector2 cellSize)
    {
        Sprite sprite = map.GetSprite(cell);
        if (sprite == null) return Vector2.zero;

        Vector3 scale = map.transform.lossyScale;
        Vector3 anchor = map.tileAnchor;
        Rect rect = sprite.rect;
        float ppu = Mathf.Max(0.0001f, sprite.pixelsPerUnit);

        float x = (anchor.x - 0.5f) * cellSize.x + ((rect.width * 0.5f - sprite.pivot.x) / ppu) * scale.x;
        float y = (anchor.y - 0.5f) * cellSize.y + ((rect.height * 0.5f - sprite.pivot.y) / ppu) * scale.y;

        Vector2 offset = new Vector2(x, y);
        return offset.magnitude > 0.02f ? offset : Vector2.zero;
    }

    private bool IsPoolMap(Tilemap map)
    {
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].map == map) return true;
        }
        return false;
    }

    // True if any Tilemap has a tile on this point (there is floor, or something, to light).
    private bool HasAnyTile(Vector2 p)
    {
        for (int i = 0; i < allMaps.Length; i++)
        {
            Tilemap m = allMaps[i];
            if (m == null || !m.gameObject.activeInHierarchy) continue;

            Vector3Int c = m.WorldToCell(p);
            c.z = 0;
            if (m.HasTile(c)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ walls

    private void FindWalls(Tilemap[] all)
    {
        wallMaps.Clear();

        if (wallTilemaps != null && wallTilemaps.Length > 0)
        {
            autoWalls = false;
            for (int i = 0; i < wallTilemaps.Length; i++)
            {
                if (wallTilemaps[i] != null) wallMaps.Add(wallTilemaps[i]);
            }
        }
        else
        {
            autoWalls = true;
            for (int i = 0; i < all.Length; i++)
            {
                Tilemap m = all[i];
                if (m == null || !m.gameObject.activeInHierarchy || IsPoolMap(m) || notWalls.Contains(m)) continue;
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

    // The wall Tilemap that has a tile on this exact point, or null.
    private Tilemap WallMapAt(Vector2 p)
    {
        for (int i = 0; i < wallMaps.Count; i++)
        {
            Tilemap m = wallMaps[i];
            if (m == null) continue;

            Vector3Int c = m.WorldToCell(p);
            c.z = 0;   // a Tilemap at a different Z would otherwise look one layer off and find nothing
            if (m.HasTile(c)) return m;
        }
        return null;
    }

    // True if this exact point is inside a wall.
    private bool IsWallAt(Vector2 p)
    {
        if (WallMapAt(p) != null) return true;
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

    // A player is standing on lava or water that an AUTOMATICALLY found wall says is a wall tile. Nobody can stand in a wall, so if it
    // goes on for half a second that Tilemap is not a wall (a ground layer with a collider, say) and is dropped.
    private void CheckWallDoubt(Walker w, Vector2 feet, float dt)
    {
        Tilemap claimed = autoWalls ? WallMapAt(feet) : null;
        if (claimed == null)
        {
            w.doubtMap = null;
            w.doubt = 0f;
            return;
        }

        if (claimed != w.doubtMap)
        {
            w.doubtMap = claimed;
            w.doubt = 0f;
        }

        w.doubt += dt;
        if (w.doubt < WallDoubtSeconds) return;

        notWalls.Add(claimed);
        wallMaps.Remove(claimed);
        w.doubtMap = null;
        w.doubt = 0f;
        dirty = true;   // bake the glow again, now that tile is not a wall
        Debug.LogWarning("HazardTileFx: the Tilemap '" + claimed.name + "' has a solid collider, so it was taken for a wall, but a player is standing on lava / water " +
                         "in it. It is not treated as a wall any more. If it should be, set the walls yourself in 'Wall Tilemaps'.", claimed);
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
            List<GlowLayer> glow = pools[i].glow;
            for (int g = 0; g < glow.Count; g++)
            {
                if (glow[g].sr == null) continue;
                glow[g].sr.sortingLayerID = glowLayerId;
                glow[g].sr.sortingOrder = glowOrder;   // all layers share one order: they are blended together, so which is on top doesn't matter
            }
        }

        if (hops == null) return;
        for (int i = 0; i < hops.Length; i++)
        {
            hops[i].sr.sortingLayerID = pieceLayerId;
            hops[i].sr.sortingOrder = pieceOrder;
        }
    }

    // ------------------------------------------------------------ the glow: materials

    // Makes the glow materials from the shader. Nothing to create or assign in the project: they only exist while the game runs.
    // Two kinds: additive (adds light) and soft light (tints and brightens what is underneath). Without the shader a plain sprite is used,
    // which can only paint over, so it is made weaker.
    private void BuildMaterials()
    {
        if (materialsTried) return;
        materialsTried = true;

        Shader shader = glowShader != null ? glowShader : Shader.Find(GlowShaderName);
        if (shader != null && shader.isSupported)
        {
            additiveMaterial = MakeGlowMaterial(shader, false);
            softMaterial = MakeGlowMaterial(shader, true);
            shaderOk = true;

            if (!additiveMaterial.HasProperty("_SrcBlend"))
            {
                Debug.LogWarning("HazardTileFx: the glow shader is the OLD version of HazardGlow.shader (it has no soft light). " +
                                 "Replace the file with the new one.", this);
            }
            return;
        }

        // No glow shader. In URP's 2D renderer the default sprite material is LIT (a dark scene would swallow the glow), so ask for the unlit one.
        Shader unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (unlit != null && unlit.isSupported) plainMaterial = new Material(unlit);
    }

    private static Material MakeGlowMaterial(Shader shader, bool soft)
    {
        Material m = new Material(shader);
        m.name = soft ? "HazardGlow (soft light)" : "HazardGlow (additive)";
        m.SetFloat("_Mode", soft ? 1f : 0f);

        // Additive: source + destination. Soft light: 2 x source x destination, where a source of 50% grey changes nothing.
        m.SetFloat("_SrcBlend", soft ? (float)UnityEngine.Rendering.BlendMode.DstColor : (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", soft ? (float)UnityEngine.Rendering.BlendMode.SrcColor : (float)UnityEngine.Rendering.BlendMode.One);
        return m;
    }

    private Material MaterialFor(bool soft)
    {
        if (shaderOk) return soft ? softMaterial : additiveMaterial;
        return plainMaterial;
    }

    // Which of the two layers is drawn as soft light in the chosen mode.
    private bool LayerIsSoft(int layer)
    {
        switch (glowMode)
        {
            case GlowMode.SoftLight: return true;
            case GlowMode.SoftLightAndAdditive: return layer == 0;
            default: return false;
        }
    }

    // ------------------------------------------------------------ the glow: baking

    // Bakes the glow of one pool, a block of 32 x 32 tiles at a time. Only blocks that have lava / water in them, or that the light can
    // reach, get a texture, so a map with two lakes far apart doesn't pay for the space between them.
    private void BakeGlow(Pool pool)
    {
        if (pool.map == null || pool.cells.Count == 0) return;

        bool bleeding = glowBleed > 0.05f;
        int reach = bleeding ? Mathf.CeilToInt(glowBleed) : 0;
        int pad = Mathf.CeilToInt(Mathf.Max(glowBleed, glowEdgeSoftness * 2f)) + 1;   // margin of tiles around a block that the light of its neighbours comes from
        int k = Mathf.Clamp(glowTexelsPerTile, 2, 16);

        HashSet<Vector2Int> blocks = new HashSet<Vector2Int>();
        for (int i = 0; i < pool.cells.Count; i++)
        {
            Vector3Int c = pool.cells[i];
            int bx0 = FloorDiv(c.x - reach, BlockTiles), bx1 = FloorDiv(c.x + reach, BlockTiles);
            int by0 = FloorDiv(c.y - reach, BlockTiles), by1 = FloorDiv(c.y + reach, BlockTiles);
            for (int by = by0; by <= by1; by++)
            {
                for (int bx = bx0; bx <= bx1; bx++) blocks.Add(new Vector2Int(bx, by));
            }
        }

        foreach (Vector2Int block in blocks) BakeBlock(pool, block.x, block.y, bleeding, pad, k);
    }

    private static int FloorDiv(int a, int b)
    {
        return Mathf.FloorToInt((float)a / b);
    }

    private void BakeBlock(Pool pool, int blockX, int blockY, bool bleeding, int pad, int k)
    {
        Tilemap map = pool.map;
        int cw = BlockTiles + 2 * pad;     // the working grid, in tiles: the block plus a margin all round
        int gx0 = blockX * BlockTiles - pad;
        int gy0 = blockY * BlockTiles - pad;

        // Tile by tile: is it lava / water (and not under a wall), and can light get onto it (not a wall, and floor when that is asked for)?
        bool[] cellFoot = new bool[cw * cw];
        bool[] cellOpen = new bool[cw * cw];
        bool anyFoot = false;
        for (int cy = 0; cy < cw; cy++)
        {
            for (int cx = 0; cx < cw; cx++)
            {
                Vector3Int cell = new Vector3Int(gx0 + cx, gy0 + cy, 0);
                Vector3 centre3 = map.GetCellCenterWorld(cell);
                Vector2 centre = new Vector2(centre3.x, centre3.y);

                bool inPool = pool.set.Contains(cell);
                bool lightCanReach = !IsWallAt(centre) && (inPool || !glowOnlyOverTiles || HasAnyTile(centre));

                int index = cy * cw + cx;
                cellOpen[index] = lightCanReach;
                cellFoot[index] = inPool && lightCanReach;
                if (cellFoot[index]) anyFoot = true;
            }
        }
        if (!anyFoot) return;   // no lava or water anywhere near this block

        // The same, in texels (k per tile).
        int tw = cw * k;
        int n = tw * tw;
        float[] alpha = new float[n];
        float[] open = new float[n];
        float[] dist = bleeding ? new float[n] : null;

        for (int ty = 0; ty < tw; ty++)
        {
            int rowCell = (ty / k) * cw;
            for (int tx = 0; tx < tw; tx++)
            {
                int ci = rowCell + tx / k;
                int i = ty * tw + tx;
                open[i] = (bleeding ? cellOpen[ci] : cellFoot[ci]) ? 1f : 0f;
                if (bleeding) dist[i] = cellFoot[ci] ? 0f : Unreached;
                else alpha[i] = cellFoot[ci] ? 1f : 0f;
            }
        }

        if (bleeding)
        {
            // How far the light has to walk from the lava / water to each texel, going around walls, never through them.
            SpreadDistance(dist, open, tw);

            float range = glowBleed * k;
            for (int i = 0; i < n; i++)
            {
                if (open[i] < 0.5f || dist[i] >= range) continue;
                float t = 1f - dist[i] / range;
                alpha[i] = t * t * (3f - 2f * t);   // 1 on the lava / water, easing out to 0 at the end of the bleed
            }

            // Smooth the result. Walls count for nothing in the average, so the light stays full strength right up to a wall and stops there.
            int radius = Mathf.RoundToInt(glowEdgeSoftness * k);
            if (radius >= 1)
            {
                float[] weight = new float[n];
                float[] temp = new float[n];
                System.Array.Copy(open, weight, n);
                BoxBlur(alpha, temp, tw, tw, radius);
                BoxBlur(alpha, temp, tw, tw, radius);
                BoxBlur(weight, temp, tw, tw, radius);
                BoxBlur(weight, temp, tw, tw, radius);
                for (int i = 0; i < n; i++) alpha[i] = (open[i] > 0.5f && weight[i] > 0.0001f) ? Mathf.Clamp01(alpha[i] / weight[i]) : 0f;
            }
        }

        // Cut the middle of the working grid (the block itself) out as a texture, once for each layer.
        bool banded = glowBands >= 2;
        int start = pad * k;
        int rw = BlockTiles * k;
        Vector3 first = map.GetCellCenterWorld(new Vector3Int(blockX * BlockTiles, blockY * BlockTiles, 0));

        for (int layer = 0; layer < GlowLayers; layer++)
        {
            Color32[] pixels = new Color32[rw * rw];
            bool anyLight = false;

            for (int ry = 0; ry < rw; ry++)
            {
                int ty = start + ry;
                float worldY = gy0 + (ty + 0.5f) / k;
                for (int rx = 0; rx < rw; rx++)
                {
                    int tx = start + rx;
                    byte value = 0;

                    float a = alpha[ty * tw + tx];
                    if (a > 0.004f)
                    {
                        float worldX = gx0 + (tx + 0.5f) / k;
                        a = Mathf.Clamp01(a * (0.3f + 0.7f * Patches(worldX, worldY, pool.seedX[layer], pool.seedY[layer])));
                        if (banded) a = Mathf.Round(a * glowBands) / glowBands;
                        value = (byte)Mathf.RoundToInt(a * 255f);
                        if (value > 0) anyLight = true;
                    }
                    pixels[ry * rw + rx] = new Color32(255, 255, 255, value);
                }
            }
            if (!anyLight) continue;

            Texture2D tex = new Texture2D(rw, rw, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = (bleeding && !banded) ? FilterMode.Bilinear : FilterMode.Point;
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            // The sprite is BlockTiles units wide (k texels per unit); scaling by the tile size lays it over the tiles.
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, rw, rw), Vector2.zero, k, 0, SpriteMeshType.FullRect);

            GameObject go = new GameObject("Glow " + (layer == 0 ? "A" : "B") + (pool.kind == Lava ? " (lava) " : " (water) ") + blockX + "," + blockY);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(first.x - pool.cellSize.x * 0.5f + pool.artOffset.x + glowOffset.x,
                                                first.y - pool.cellSize.y * 0.5f + pool.artOffset.y + glowOffset.y,
                                                first.z);
            go.transform.localScale = new Vector3(pool.cellSize.x, pool.cellSize.y, 1f);

            bool soft = LayerIsSoft(layer);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(1f, 1f, 1f, 0f);
            Material material = MaterialFor(soft);
            if (material != null) sr.sharedMaterial = material;

            pool.glow.Add(new GlowLayer { layer = layer, soft = soft, go = go, sr = sr, tex = tex, sprite = sprite });
        }
    }

    // Soft bright and dark patches, about three tiles across with a little finer detail on top. 0..1. Made from world position, so
    // neighbouring blocks match up with no seam.
    private static float Patches(float x, float y, float seedX, float seedY)
    {
        float coarse = Mathf.PerlinNoise(x * 0.3f + seedX, y * 0.3f + seedY);
        float fine = Mathf.PerlinNoise(x * 0.9f + seedX + 31f, y * 0.9f + seedY + 17f);
        return Mathf.Clamp01(Mathf.InverseLerp(0.25f, 0.75f, 0.65f * coarse + 0.35f * fine));
    }

    // Walking distance from the lava / water to every open texel, found by sweeping the grid back and forth. Closed texels (walls)
    // are skipped, so the distance has to go around them.
    private static void SpreadDistance(float[] dist, float[] open, int size)
    {
        const float side = 1f;
        const float diagonal = 1.4142f;

        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (open[i] < 0.5f) continue;

                    float d = dist[i];
                    if (x > 0) d = Mathf.Min(d, dist[i - 1] + side);
                    if (y > 0)
                    {
                        d = Mathf.Min(d, dist[i - size] + side);
                        if (x > 0) d = Mathf.Min(d, dist[i - size - 1] + diagonal);
                        if (x < size - 1) d = Mathf.Min(d, dist[i - size + 1] + diagonal);
                    }
                    if (d < dist[i])
                    {
                        dist[i] = d;
                        changed = true;
                    }
                }
            }

            for (int y = size - 1; y >= 0; y--)
            {
                for (int x = size - 1; x >= 0; x--)
                {
                    int i = y * size + x;
                    if (open[i] < 0.5f) continue;

                    float d = dist[i];
                    if (x < size - 1) d = Mathf.Min(d, dist[i + 1] + side);
                    if (y < size - 1)
                    {
                        d = Mathf.Min(d, dist[i + size] + side);
                        if (x < size - 1) d = Mathf.Min(d, dist[i + size + 1] + diagonal);
                        if (x > 0) d = Mathf.Min(d, dist[i + size - 1] + diagonal);
                    }
                    if (d < dist[i])
                    {
                        dist[i] = d;
                        changed = true;
                    }
                }
            }

            if (!changed) break;
        }
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

    // ------------------------------------------------------------ the glow: every frame

    // The two layers breathe out of step with each other: where one fades, the other brightens, so the light seems to drift over the pool.
    private void UpdateGlow()
    {
        float plain = shaderOk ? 1f : 0.55f;   // a plain overlay can only paint over, so it needs to be softer

        for (int i = 0; i < pools.Count; i++)
        {
            Pool pool = pools[i];
            Look look = pool.kind == Lava ? lava : water;
            bool on = glowEnabled && look.enabled && look.glow && look.glowOpacity > 0f && glowOpacityAll > 0f;

            float speed = 6.2832f / look.glowPulseSeconds;
            float envelope = 1f - look.glowPulse + look.glowPulse * (0.5f + 0.5f * Mathf.Sin(clock * speed + pool.phase));
            float strength = look.glowOpacity * glowOpacityAll * envelope * plain;
            float mainWave = 0.5f + 0.5f * Mathf.Sin(clock * speed * 1.3f + pool.phase * 2f);
            float highlightWave = 0.5f + 0.5f * Mathf.Sin(clock * speed + pool.phase * 2f + 2.4f);

            for (int g = 0; g < pool.glow.Count; g++)
            {
                GlowLayer layer = pool.glow[g];
                if (layer.sr == null) continue;

                layer.sr.enabled = on;
                if (!on) continue;

                // The mode can be changed while playing: swap the material when this layer's kind changes.
                bool soft = LayerIsSoft(layer.layer);
                if (soft != layer.soft)
                {
                    layer.soft = soft;
                    Material material = MaterialFor(soft);
                    if (material != null) layer.sr.sharedMaterial = material;
                }

                // An additive layer on top of a soft one is kept gentler, or the two together would be too much.
                float gain = (soft || glowMode == GlowMode.Additive) ? 1f : 0.6f;

                Color c;
                if (layer.layer == 0)
                {
                    c = look.glowColour;
                    c.a = Mathf.Clamp01(strength * (0.5f + 0.5f * mainWave) * gain);
                }
                else
                {
                    c = look.glowHighlight;
                    c.a = Mathf.Clamp01(strength * 0.9f * highlightWave * gain);
                }
                layer.sr.color = c;
            }
        }
    }

    private void DestroyPools()
    {
        for (int i = 0; i < pools.Count; i++)
        {
            List<GlowLayer> glow = pools[i].glow;
            for (int g = 0; g < glow.Count; g++)
            {
                if (glow[g].go != null) Destroy(glow[g].go);
                if (glow[g].sprite != null) Destroy(glow[g].sprite);
                if (glow[g].tex != null) Destroy(glow[g].tex);
            }
        }
        pools.Clear();
    }

    // One line in the Console saying what was found, but only when it differs from the last one.
    private void LogSetup()
    {
        if (!logSetup) return;

        int lavaTiles = 0;
        int waterTiles = 0;
        int glowSprites = 0;
        string shifted = "";
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i].kind == Lava) lavaTiles += pools[i].cells.Count;
            else waterTiles += pools[i].cells.Count;
            glowSprites += pools[i].glow.Count;

            Vector2 o = pools[i].artOffset;
            if (o != Vector2.zero) shifted += " '" + pools[i].map.name + "' by (" + o.x.ToString("0.##") + ", " + o.y.ToString("0.##") + ")";
        }

        List<string> walls = new List<string>();
        for (int i = 0; i < wallMaps.Count; i++)
        {
            if (wallMaps[i] != null) walls.Add(wallMaps[i].name);
        }

        string message = "HazardTileFx: found " + lavaTiles + " lava and " + waterTiles + " water tile(s)" +
                         (lavaTiles + waterTiles == 0 ? " (nothing matched: check the tile names, see FloorManager's Lava Name / Water Name)" : "") +
                         ". Walls: " + (walls.Count == 0 ? "none" : string.Join(", ", walls.ToArray())) + (autoWalls ? " (found by themselves)" : " (set by hand)") +
                         ". Drawn on sorting layer '" + SortingLayer.IDToName(pieceLayerId) + "', glow at order " + glowOrder + ", pieces at order " + pieceOrder +
                         ". Glow: " + glowSprites + " baked layer(s), " +
                         (shaderOk ? glowMode.ToString() + " (shader found)" : "plain overlay (no glow shader found, see 'Glow Shader')") +
                         (shifted.Length > 0 ? ". Tile art is drawn off its grid cells, so the glow was shifted to match:" + shifted : "") + ".";

        if (message == lastLog) return;
        lastLog = message;
        Debug.Log(message, this);
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
            }

            if (kind == None)
            {
                w.doubtMap = null;
                w.doubt = 0f;
                continue;
            }

            CheckWallDoubt(w, feet, dt);

            // Every time the player has walked one "Step Distance" over the tile, that is a step. No speed limit to pass: positions
            // that only change in physics steps (so some frames show no movement at all) still add up to the right distance.
            if (moved > MinMove) w.distance += moved;
            if (w.distance >= stepDistance && Time.time - w.lastStepTime >= minStepSeconds)
            {
                w.distance = (w.distance - stepDistance > stepDistance) ? 0f : w.distance - stepDistance;
                w.lastStepTime = Time.time;
                Step(pool, cell, feet);
            }
        }
    }

    // One step on lava or water: a sound and a few pieces.
    private void Step(Pool pool, Vector3Int cell, Vector2 feet)
    {
        Look look = pool.kind == Lava ? lava : water;
        PlaySound(look, ClipsFor(look, false), feet);
        EmitPieces(pool, cell, feet, RollInt(stepPieces), Multiplier(look.walkingLevel));
    }

    // A player has just stepped onto lava or water.
    private void Splash(Pool pool, Vector3Int cell, Vector2 feet)
    {
        Look look = pool.kind == Lava ? lava : water;
        PlaySound(look, ClipsFor(look, true), feet);
        EmitPieces(pool, cell, feet, RollInt(splashPieces), Multiplier(look.walkingLevel));
    }

    // Splash clips for a splash, step clips for a step; if one list is empty the other one is used.
    private static AudioClip[] ClipsFor(Look look, bool splash)
    {
        bool haveSplash = look.splashClips != null && look.splashClips.Length > 0;
        bool haveStep = look.stepClips != null && look.stepClips.Length > 0;

        if (splash) return haveSplash ? look.splashClips : look.stepClips;
        return haveStep ? look.stepClips : look.splashClips;
    }

    private void EmitPieces(Pool pool, Vector3Int cell, Vector2 feet, int baseCount, float level)
    {
        if (!particlesEnabled || level <= 0f) return;

        int count = Mathf.RoundToInt(baseCount * level);
        for (int i = 0; i < count; i++)
        {
            Vector2 start = feet + new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(-0.12f, 0.12f));

            Vector2 ignored;
            Color colour;
            SampleTile(pool, cell, out ignored, out colour);   // only the colour: the piece starts at the player's feet
            SpawnHop(start, colour, RollFloat(walkingHopSeconds));
        }
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
        Vector2 artCentre = new Vector2(centre.x + pool.artOffset.x, centre.y + pool.artOffset.y);
        world = artCentre;
        colour = look.fallbackColour;

        Sprite sprite = pool.map.GetSprite(cell);
        if (sprite == null) return false;

        RawSprite raw = ReadSprite(sprite);
        if (raw == null || raw.solid == null || raw.solid.Length == 0) return false;

        int index = raw.solid[Random.Range(0, raw.solid.Length)];
        int px = index % raw.w;
        int py = index / raw.w;

        // Where that pixel is, on the sprite as it is drawn.
        Vector3 scale = pool.map.transform.lossyScale;
        float spriteWidth = sprite.rect.width / Mathf.Max(0.0001f, sprite.pixelsPerUnit) * scale.x;
        float spriteHeight = sprite.rect.height / Mathf.Max(0.0001f, sprite.pixelsPerUnit) * scale.y;
        world = new Vector2(artCentre.x + ((px + 0.5f) / raw.w - 0.5f) * spriteWidth,
                            artCentre.y + ((py + 0.5f) / raw.h - 0.5f) * spriteHeight);

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

    private void SpawnHop(Vector2 start, Color colour, float seconds)
    {
        if (IsWallAt(start)) return;   // never start inside a wall

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
        Vector2 end = start;
        float best = -1f;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            float angle = Random.value * 6.2832f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 reached = ReachableEnd(start, dir, distance);
            float reachedDistance = (reached - start).magnitude;
            if (reachedDistance > best)
            {
                best = reachedDistance;
                end = reached;
            }
            if (reachedDistance >= distance * 0.99f) break;
        }

        hop.from = start;
        hop.to = end;
        hop.height = FitHeight(start, end, height);
        hop.life = Mathf.Max(0.1f, seconds);
        hop.age = 0f;
        hop.size = Mathf.Max(0.5f, RollFloat(pieceSizePixels)) / pixelsPerUnit;
        hop.colour = colour;
        hop.active = true;
        activeHops++;

        hop.t.position = new Vector3(start.x, start.y, 0f);
        hop.t.localScale = new Vector3(hop.size, hop.size, 1f);
        hop.sr.sortingLayerID = pieceLayerId;
        hop.sr.sortingOrder = pieceOrder;
        Color c = colour;
        c.a = 0f;
        hop.sr.color = c;
        hop.sr.enabled = true;
    }

    // The furthest point along a direction (up to "distance") that is still clear of walls.
    private Vector2 ReachableEnd(Vector2 start, Vector2 dir, float distance)
    {
        if (!HasWalls) return start + dir * distance;

        const float step = 0.1f;
        Vector2 last = start;
        float travelled = 0f;
        while (travelled < distance)
        {
            travelled = Mathf.Min(distance, travelled + step);
            Vector2 p = start + dir * travelled;
            if (IsNearWall(p)) break;
            last = p;
        }
        return last;
    }

    // The jump is drawn higher up the screen than the ground it flies over, so a piece under a wall must jump lower (or not at all).
    private float FitHeight(Vector2 start, Vector2 end, float height)
    {
        if (!HasWalls) return height;

        Vector2 quarter = Vector2.Lerp(start, end, 0.25f);
        Vector2 middle = Vector2.Lerp(start, end, 0.5f);
        Vector2 threeQuarters = Vector2.Lerp(start, end, 0.75f);

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
