using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Ilumisoft.HealthSystem;

/// <summary>
/// The foe side of the floor system. FloorController owns the floor NUMBER (doors, Shop trip, black screen,
/// teleport); this script FOLLOWS it:
///
///   FloorController.FloorChanged fires (players already moved, screen still black)
///        ->  FoeSpawner.StartLevel(floor - 1)  ->  foes of the new floor start spawning once the screen fades out.
///
/// So a door AND the Shop both end up here, and the spawner always matches the floor. After a trip to
/// the Shop the scene reloads; Start() then begins the spawner on the floor FloorController remembers (not on floor 1).
///
///   GoDownFloor()  ->  asks FloorController to run the full "go down" (black screen, teleport, new foes), so any script
///                      can send the players down without a door.
///
/// It also decides where foes are allowed to be, using ONE rule (IsValidFoePosition) in two places:
///
///   1. BEFORE a foe is created: the spawner asks this rule about every candidate spawn point, so a foe is never
///      created somewhere it would be removed from later. No pop-in then pop-out.
///   2. While foes are alive: a foe that stays outside the rule for "graceSeconds" is removed (safety net).
///
/// A point is VALID (a foe is never removed there) if ANY of these is true:
///   - it overlaps a collider on the "Protected Mask" layer(s)   <- never removed, under any conditions
///   - it is on a tile of one of the "Floor Tilemaps" (and over the "Floor Mask" layer, if you set one)
/// If nothing at all is set up (no tilemaps, no masks) the rule has no opinion and nothing is ever removed.
///
/// Read FloorManager.CurrentFloor (or FloorController.CurrentFloor, same number) from anywhere: difficulty, spawn rates,
/// shop prices, score bonuses... CurrentFloor is 1-based. The spawner's level index is CurrentFloor - 1, so floor 1 = the
/// first entry in FoeSpawner's "levels" list. Past the last entry the spawner reuses the last list.
/// The floor a run STARTS on is FloorController's "Starting Floor" field.
///
/// Going down also HEALS every player that counts (Heal On Descent, 10% of max health by default) and the run has an END: when the floor
/// number goes past "Last Floor", no new floor is built and EndScreens loads the Ending scene instead.
///
/// HAZARD TILES: it also reads the tile under each player, by player NUMBER, and hurts them:
///   lava    hurts the players listed in "Lava Hurts" (Player 1, blue, by default); any tile with "lava" in its name
///   water   hurts the players listed in "Water Hurts" (Player 2, orange, by default); any tile with "water" in its name
///   spikes  hurt EVERY player, massively, but only while the spike tile shows a frame that is not in "Safe Spike Frames"
///           (frames 0 and 1 by default, so only spike-trap-16px_strip8_2 to _7 hurt). Players are hurt through
///           CoopHealth.ApplyDamage, so Invincible and the CoopRules (Shared Damage, Single Player Mode) still apply.
///
/// HazardTileFx (a separate script on its own object) gives those same lava and water tiles their glow, hopping pieces and walking
/// sounds. It reads the tile names, the Tilemaps and the foot point from here (see "hooks for HazardTileFx" below), so what glows is
/// exactly what burns. This script does not need it, and it does not need this script's "Hazards Enabled" box ticked.
///
/// Put this on an empty object and drag the FoeSpawner into "spawner". The spawner's "Auto Start" box is now ignored
/// (this script starts the right level itself), so it no longer matters whether it is ticked.
/// </summary>
public class FloorManager : MonoBehaviour
{
    // ------------------------------------------------------------ shared

    /// <summary>The floor the player is on. This is FloorController's number: there is only ONE counter.</summary>
    public static int CurrentFloor => FloorController.CurrentFloor;

    /// <summary>Fires when the player arrives on a new floor (behind the black screen), with the NEW floor number. Also fires once at the start of the scene.</summary>
    public static event System.Action<int> OnFloorChanged;

    /// <summary>Fires when the player has killed every foe on the floor (passes the floor number).</summary>
    public event System.Action<int> OnFloorCleared;

    // ------------------------------------------------------------ inspector

    [Tooltip("Leave empty to find the FoeSpawner in the scene.")]
    [SerializeField] private FoeSpawner spawner;

    [Header("Where foes may be")]
    [Tooltip("PROTECTED LAYER. A foe whose position overlaps a collider on these layers is NEVER removed, and foes may spawn there. " +
             "Make an empty object with a Box Collider 2D (Is Trigger ticked) covering the whole playable stage, put it on its own " +
             "layer (e.g. 'FoeSafe') and pick that layer here. Nothing = off.")]
    [SerializeField] private LayerMask protectedMask = 0;
    [Tooltip("The floor Tilemaps: a foe standing on a cell with a tile in one of these is fine. Add EVERY Tilemap foes can legally walk on.")]
    [SerializeField] private Tilemap[] floorTilemaps;
    [Tooltip("Optional, for floors built from prefabs: floor pieces with a Collider2D (trigger is fine) on this layer. Nothing = off.")]
    [SerializeField] private LayerMask floorMask = 0;

    [Header("Going down: healing and the last floor")]
    [Tooltip("The final floor of the run. Going down from it (a door) ends the run (Ending scene) instead of building another floor.")]
    [SerializeField, Min(1)] private int lastFloor = 4;
    [Tooltip("Fraction of each player's MAX health restored each time the floor number goes up. 0.1 = 10%. 0 = no healing.")]
    [SerializeField, Range(0f, 1f)] private float healOnDescent = 0.1f;
    [Tooltip("Leave empty to find the EndScreens in the scene. It loads the Ending scene on a win (and the Failure Ending scene when health hits zero).")]
    [SerializeField] private EndScreens endScreens;

    [Header("Hazard tiles (lava, water, spikes)")]
    [Tooltip("Master switch for lava, water and spikes.")]
    [SerializeField] private bool hazardsEnabled = true;
    [Tooltip("Tilemaps to check for hazard tiles. Leave EMPTY to check every Tilemap in the scene (re-checked once a second).")]
    [SerializeField] private Tilemap[] hazardTilemaps;
    [Tooltip("Where on each player the tile is read, as an offset from the middle of that player's collider (world units). Nudge this until it sits at their feet.")]
    [SerializeField] private Vector2 hazardCheckOffset = Vector2.zero;
    [Tooltip("Seconds between damage ticks while a player stands in lava or water they are hurt by. The first tick is the moment they step in.")]
    [SerializeField, Min(0.05f)] private float hazardTickSeconds = 0.5f;
    [Tooltip("Prints a Console line every time a hazard hurts a player, with the tile and sprite names, so a wrong hit can be traced.")]
    [SerializeField] private bool logHazards = true;

    [Header("Lava")]
    [Tooltip("A tile counts as lava when its name (tile asset OR sprite) contains this. Capitals, spaces, dashes and underscores don't matter, so \"lava\" covers lava-16px_strip12_0 to _11.")]
    [SerializeField] private string lavaName = "lava";
    [Tooltip("Which players lava hurts. Player 1 (blue) by default. Add 3 or 4 if you want them hurt too.")]
    [SerializeField] private int[] lavaHurts = { 1 };
    [Tooltip("Damage per tick.")]
    [SerializeField, Min(0f)] private float lavaDamage = 5f;

    [Header("Water")]
    [Tooltip("A tile counts as water when its name (tile asset OR sprite) contains this, so \"water\" covers water-16px_strip8_0 to _7.")]
    [SerializeField] private string waterName = "water";
    [Tooltip("Which players water hurts. Player 2 (orange) by default. Add 3 or 4 if you want them hurt too.")]
    [SerializeField] private int[] waterHurts = { 2 };
    [Tooltip("Damage per tick.")]
    [SerializeField, Min(0f)] private float waterDamage = 5f;

    [Header("Spikes (hurt every player)")]
    [Tooltip("A tile counts as a spike trap when its name contains this.")]
    [SerializeField] private string spikeName = "spike-trap";
    [Tooltip("Spike frames that do NOTHING: the number at the end of the sprite name (spike-trap-16px_strip8_0 is frame 0). Every other frame hurts. Default 0 and 1 are the harmless ones.")]
    [SerializeField] private int[] safeSpikeFrames = { 0, 1 };
    [Tooltip("Damage of one spike hit. Make it as big as or bigger than the players' max health for an instant kill.")]
    [SerializeField, Min(0f)] private float spikeDamage = 100f;
    [Tooltip("After a spike hits a player it can't hit that same player again for this long, so one trap going off doesn't hit once per frame of its animation.")]
    [SerializeField, Min(0f)] private float spikeCooldown = 1f;

    [Header("Stray foe check")]
    [Tooltip("Turn the removal of foes that are somewhere invalid on or off. (The spawn-time check stays active either way.)")]
    [SerializeField] private bool removeStrayFoes = true;
    [Tooltip("How often the check runs, in seconds.")]
    [SerializeField, Min(0.05f)] private float checkInterval = 0.25f;
    [Tooltip("A foe must stay invalid this long before it is removed, so a foe scraping a wall edge for a moment is not killed.")]
    [SerializeField, Min(0f)] private float graceSeconds = 1f;
    [Tooltip("Prints one Console line every time a foe is removed, with its name and position, so a wrong removal can be traced.")]
    [SerializeField] private bool logRemovals = true;

    // ------------------------------------------------------------ state

    private readonly Dictionary<FoeController, float> invalidSince = new Dictionary<FoeController, float>();
    private readonly List<FoeController> pruneList = new List<FoeController>();
    private readonly Collider2D[] overlapBuffer = new Collider2D[1];
    private float nextCheckTime;
    private bool warnedNoFloor;
    private FloorController floorController;
    private int handledFloor;   // the floor the spawner was last started for (0 = none yet), so one floor never starts twice

    private const int MaxPlayers = 4;
    private readonly float[] nextTickTime = new float[MaxPlayers + 1];    // per player number: when lava / water may hurt them next
    private readonly float[] nextSpikeTime = new float[MaxPlayers + 1];   // per player number: when a spike may hurt them next
    private readonly Collider2D[] playerColliders = new Collider2D[MaxPlayers + 1];
    private readonly CoopHealth[] playerColliderOwners = new CoopHealth[MaxPlayers + 1];
    private Tilemap[] autoHazardMaps;
    private float nextHazardMapRefresh;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        if (spawner == null) spawner = FindAnyObjectByType<FoeSpawner>();
        if (endScreens == null) endScreens = FindAnyObjectByType<EndScreens>();

        // Awake runs before every Start, so the spawner's own Auto Start can never start the wrong level over ours.
        if (spawner != null) spawner.ControlledExternally = true;
    }

    private void Start()
    {
        floorController = FindAnyObjectByType<FloorController>();
        if (floorController == null)
        {
            Debug.LogWarning("FloorManager: no FloorController in the scene, so nothing can change the floor and the " +
                             "spawner will stay on floor " + CurrentFloor + ".", this);
        }

        if (spawner != null)
        {
            spawner.OnLevelCleared += HandleLevelCleared;
            spawner.SpawnPointFilter = IsValidFoePosition;   // the spawn-time check: no foe is created in an invalid spot
        }

        // Begin on the floor FloorController says we are on. Fresh run = Starting Floor; back from the Shop = the floor after it.
        ApplyFloor(CurrentFloor);

        // From now on, every floor change (a door, Shop return, GoDownFloor()) comes through here.
        FloorController.FloorChanged += ApplyFloor;
    }

    private void OnDestroy()
    {
        // FloorChanged is static and outlives this scene, so a leftover subscription would call a destroyed object.
        FloorController.FloorChanged -= ApplyFloor;

        if (spawner != null)
        {
            spawner.OnLevelCleared -= HandleLevelCleared;
            spawner.SpawnPointFilter = null;
            spawner.ControlledExternally = false;
        }
    }

    private void HandleLevelCleared(int levelIndex)
    {
        OnFloorCleared?.Invoke(CurrentFloor);
    }

    // ------------------------------------------------------------ the floor changes

    // Runs when the player has just arrived on a floor. FloorController has already removed the old foes and moved the
    // player, and the foes are still frozen behind the black screen, so the new level's first spawn waits for the fade-out.
    private void ApplyFloor(int floor)
    {
        if (floor == handledFloor) return;
        int previousFloor = handledFloor;
        handledFloor = floor;

        invalidSince.Clear();

        // Went down from the last floor: the run is won. No new level starts, and nothing listening to OnFloorChanged
        // gets a floor number that has no data behind it.
        if (floor > lastFloor)
        {
            WinRun();
            return;
        }

        // Healing only on a real step down. The first call of a scene (a fresh start, or back from the Shop) has no
        // previous floor, so it never heals.
        if (previousFloor > 0 && floor > previousFloor) HealPlayers(healOnDescent);

        if (spawner != null) spawner.StartLevel(floor - 1);   // StartLevel also clears any foe still left from the last floor

        OnFloorChanged?.Invoke(floor);
    }

    /// <summary>True while the player is on the final floor (going down from here is the victory).</summary>
    public bool IsOnLastFloor => CurrentFloor >= lastFloor;

    /// <summary>
    /// Ends the run by loading the Ending scene (through EndScreens). Called automatically when the floor number goes past "Last Floor"; any script
    /// can also call it directly (for example from a script on the last floor, to skip the extra black screen).
    /// </summary>
    public void WinRun()
    {
        ClearAllFoes();   // no score for these

        if (endScreens == null) endScreens = FindAnyObjectByType<EndScreens>();
        if (endScreens != null) endScreens.ShowVictory();
        else Debug.LogWarning("FloorManager: the run is won but there is no EndScreens in the scene to load the Ending scene.", this);
    }

    // Restores a fraction of each living player's MAX health (so health upgrades from the Shop count). AddHealth clamps at max.
    private void HealPlayers(float fraction)
    {
        if (fraction <= 0f) return;

        CoopHealth[] all = CoopHealth.Players.ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            CoopHealth hp = all[i];
            if (hp == null || !hp.IsAlive || !CoopRules.Counts(hp.PlayerNumber)) continue;
            hp.AddHealth(hp.MaxHealth * fraction);
        }
    }

    /// <summary>
    /// Sends the players down one floor with the full change: black "Floor Level N" screen, teleport to that floor's spawn
    /// points, old foes removed, new floor's foes started. Works without a door. (If no black screen is set up,
    /// the players still go down, just without the fade.)
    /// </summary>
    public void GoDownFloor()
    {
        if (floorController == null) floorController = FindAnyObjectByType<FloorController>();
        if (floorController == null)
        {
            Debug.LogWarning("FloorManager: GoDownFloor() needs a FloorController in the scene.", this);
            return;
        }

        floorController.GoDown();
    }

    /// <summary>
    /// Destroys every foe in the scene, including ones the spawner didn't make (hand-placed foes).
    /// Destroying skips FoeController.Die(), so no score is awarded for cleared foes.
    /// </summary>
    public void ClearAllFoes()
    {
        FoeController[] foes = FindObjectsByType<FoeController>();
        for (int i = 0; i < foes.Length; i++)
        {
            Destroy(foes[i].Root);
        }
        invalidSince.Clear();
    }

    /// <summary>Call this if a new floor uses different floor Tilemaps than the last one.</summary>
    public void SetFloorTilemaps(Tilemap[] maps)
    {
        floorTilemaps = maps;
        invalidSince.Clear();
    }

    // ------------------------------------------------------------ the one rule

    /// <summary>
    /// True if a foe may stand at this point (it will never be removed there, and the spawner may use it).
    /// Used by the spawner BEFORE it creates a foe, and by the stray check on foes that already exist.
    /// </summary>
    public bool IsValidFoePosition(Vector2 pos)
    {
        bool haveProtected = protectedMask.value != 0;
        bool haveTilemaps = floorTilemaps != null && floorTilemaps.Length > 0;
        bool haveMask = floorMask.value != 0;

        if (!haveProtected && !haveTilemaps && !haveMask) return true;   // nothing set up: no opinion

        if (haveProtected && PointOnLayer(pos, protectedMask)) return true;   // protected: never invalid

        if (!haveTilemaps && !haveMask) return false;   // only the protected layer is set, and the point is outside it
        if (haveTilemaps && !OnAnyTile(pos)) return false;
        if (haveMask && !PointOnLayer(pos, floorMask)) return false;
        return true;
    }

    // Triggers are included on purpose, so a trigger collider works as the protected / floor layer.
    private bool PointOnLayer(Vector2 pos, LayerMask mask)
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(mask);
        filter.useTriggers = true;
        return Physics2D.OverlapPoint(pos, filter, overlapBuffer) > 0;
    }

    private bool OnAnyTile(Vector2 worldPos)
    {
        for (int i = 0; i < floorTilemaps.Length; i++)
        {
            Tilemap map = floorTilemaps[i];
            if (map != null && map.HasTile(map.WorldToCell(worldPos))) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ hazard tiles (lava, water, spikes)

    // Reads the tile under each player and hurts them by player number:
    //   lava / water   only the players in Lava Hurts / Water Hurts, once per Hazard Tick Seconds
    //   spikes         every player, once per Spike Cooldown, only while the spike shows a frame not in Safe Spike Frames
    private void CheckHazards()
    {
        if (FloorController.BlocksInput || MenuController.IsOpen) return;   // nobody is hurt behind a menu or a black screen

        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            if (p == null || !p.IsAlive || !CoopRules.Counts(n)) continue;

            bool onLava;
            bool onWater;
            bool onSpike;
            string detail;
            ReadHazardTiles(HazardPoint(n, p), out onLava, out onWater, out onSpike, out detail);

            if (onSpike && Time.time >= nextSpikeTime[n])
            {
                nextSpikeTime[n] = Time.time + spikeCooldown;
                HurtByHazard(p, spikeDamage, "spikes", detail);
                if (!p.IsAlive) continue;
            }

            string source = null;
            float amount = 0f;
            if (onLava && Listed(lavaHurts, n)) { source = "lava"; amount = lavaDamage; }
            else if (onWater && Listed(waterHurts, n)) { source = "water"; amount = waterDamage; }

            if (source != null && Time.time >= nextTickTime[n])
            {
                nextTickTime[n] = Time.time + hazardTickSeconds;
                HurtByHazard(p, amount, source, detail);
            }
        }
    }

    private void HurtByHazard(CoopHealth player, float amount, string source, string detail)
    {
        if (amount <= 0f) return;

        if (logHazards)
        {
            Debug.Log("FloorManager: Player " + player.PlayerNumber + " hurt by " + source + " for " + amount + " (" + detail + ").", player);
        }
        player.ApplyDamage(amount);   // goes through CoopHealth: Invincible and the CoopRules still apply
    }

    // Looks at every hazard tilemap under one point and says what is there.
    private void ReadHazardTiles(Vector2 point, out bool onLava, out bool onWater, out bool onSpike, out string detail)
    {
        onLava = false;
        onWater = false;
        onSpike = false;
        detail = "";

        Tilemap[] maps = HazardMaps();
        for (int i = 0; i < maps.Length; i++)
        {
            Tilemap map = maps[i];
            if (map == null) continue;

            Vector3Int cell = map.WorldToCell(point);
            cell.z = 0;   // a tilemap that sits at a different Z would otherwise look one layer off and find nothing

            TileBase tile = map.GetTile(cell);
            if (tile == null) continue;

            Sprite shown = map.GetSprite(cell);
            string spriteName = shown != null ? shown.name : "";
            string names = "tile '" + tile.name + "', sprite '" + spriteName + "'";

            if (NameHas(tile.name, lavaName) || NameHas(spriteName, lavaName))
            {
                onLava = true;
                detail = names;
            }
            if (NameHas(tile.name, waterName) || NameHas(spriteName, waterName))
            {
                onWater = true;
                detail = names;
            }
            if (NameHas(tile.name, spikeName) || NameHas(spriteName, spikeName))
            {
                int frame = SpikeFrame(map, cell, spriteName);
                if (!Listed(safeSpikeFrames, frame))
                {
                    onSpike = true;
                    detail = names + ", frame " + frame;
                }
            }
        }
    }

    // Which frame of the spike is showing right now. An Animated Tile always reports ONE fixed picture through GetSprite, so for
    // those the current frame comes from the animation itself (frame 0 = its first picture: keep the pictures in _0, _1, _2...
    // order). A tile that isn't animated uses the number at the end of its sprite name. -1 = no number found (it hurts).
    private static int SpikeFrame(Tilemap map, Vector3Int cell, string spriteName)
    {
        if (map.GetAnimationFrameCount(cell) > 1) return map.GetAnimationFrame(cell);
        return FrameNumber(spriteName);
    }

    // The number at the very end of a name: "spike-trap-16px_strip8_2" -> 2.
    private static int FrameNumber(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return -1;

        int end = spriteName.Length;
        int start = end;
        while (start > 0 && char.IsDigit(spriteName[start - 1])) start--;
        if (start == end) return -1;

        int value;
        return int.TryParse(spriteName.Substring(start), out value) ? value : -1;
    }

    private static bool Listed(int[] list, int value)
    {
        return list != null && System.Array.IndexOf(list, value) >= 0;
    }

    // Capitals, spaces, dashes and underscores are ignored, so "lava" is found in "Lava-16px_strip12_3".
    private static bool NameHas(string candidate, string wanted)
    {
        string a = Normalize(candidate);
        string b = Normalize(wanted);
        return a.Length > 0 && b.Length > 0 && a.Contains(b);
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i])) sb.Append(char.ToLowerInvariant(text[i]));
        }
        return sb.ToString();
    }

    private Tilemap[] HazardMaps()
    {
        if (hazardTilemaps != null && hazardTilemaps.Length > 0) return hazardTilemaps;

        if (autoHazardMaps == null || Time.unscaledTime >= nextHazardMapRefresh)
        {
            autoHazardMaps = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            nextHazardMapRefresh = Time.unscaledTime + 1f;
        }
        return autoHazardMaps;
    }

    // The spot on a player where the tile is read: the middle of their solid collider, plus the offset.
    private Vector2 HazardPoint(int playerNumber, CoopHealth player)
    {
        if (playerColliderOwners[playerNumber] != player)
        {
            playerColliderOwners[playerNumber] = player;
            playerColliders[playerNumber] = PickPlayerCollider(player.gameObject);
        }

        Collider2D c = playerColliders[playerNumber];
        Vector2 centre = c != null ? (Vector2)c.bounds.center : (Vector2)player.transform.position;
        return centre + hazardCheckOffset;
    }

    private static Collider2D PickPlayerCollider(GameObject go)
    {
        Collider2D[] all = go.GetComponentsInChildren<Collider2D>();
        Collider2D fallback = null;
        for (int i = 0; i < all.Length; i++)
        {
            Collider2D c = all[i];
            if (!c.enabled) continue;
            if (!c.isTrigger) return c;
            if (fallback == null) fallback = c;
        }
        return fallback;
    }

    // ------------------------------------------------------------ hooks for HazardTileFx (read-only)

    /// <summary>The name a tile must contain to count as lava (the same rule that hurts).</summary>
    public string LavaName { get { return lavaName; } }

    /// <summary>The name a tile must contain to count as water (the same rule that hurts).</summary>
    public string WaterName { get { return waterName; } }

    /// <summary>The Tilemaps hazards are read from: the list you set, or every Tilemap in the scene (refreshed once a second).</summary>
    public Tilemap[] HazardTilemapList { get { return HazardMaps(); } }

    /// <summary>The spot on this player where the hazard tile is read (middle of their solid collider plus "Hazard Check Offset").</summary>
    public Vector2 HazardPointOf(CoopHealth player)
    {
        if (player == null) return Vector2.zero;

        int n = player.PlayerNumber;
        if (n < 1 || n > MaxPlayers) return player.transform.position;
        return HazardPoint(n, player);
    }

    /// <summary>The name rule used for lava, water and spikes: capitals, spaces, dashes and underscores don't matter.</summary>
    public static bool TileNameHas(string candidate, string wanted)
    {
        return NameHas(candidate, wanted);
    }

    // ------------------------------------------------------------ stray foe check

    private void Update()
    {
        if (hazardsEnabled) CheckHazards();

        if (!removeStrayFoes || Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;
        CheckFoes();
    }

    private void CheckFoes()
    {
        bool configured = protectedMask.value != 0 || floorMask.value != 0 ||
                          (floorTilemaps != null && floorTilemaps.Length > 0);
        if (!configured)
        {
            if (!warnedNoFloor)
            {
                warnedNoFloor = true;
                Debug.LogWarning("FloorManager: the stray foe check is on but no Floor Tilemaps, Floor Mask or Protected Mask " +
                                 "are set, so it is doing nothing.");
            }
            return;
        }

        FoeController[] foes = FindObjectsByType<FoeController>();
        for (int i = 0; i < foes.Length; i++)
        {
            FoeController foe = foes[i];
            if (foe == null || foe.IsDead)
            {
                invalidSince.Remove(foe);
                continue;
            }

            if (FoeIsSafe(foe))
            {
                invalidSince.Remove(foe);   // a valid spot: do nothing
                continue;
            }

            if (!invalidSince.TryGetValue(foe, out float since))
            {
                invalidSince[foe] = Time.time;   // just noticed it somewhere invalid: start the grace timer
                continue;
            }

            if (Time.time - since >= graceSeconds)
            {
                invalidSince.Remove(foe);
                RemoveStray(foe);
            }
        }

        PruneDestroyedFoes();
    }

    // A foe counts as safe if EITHER its pivot OR the centre of its collider is on a valid point. Foes pressed against a
    // wall often have a pivot sitting on the wall's cell, so testing only the pivot would remove foes that are fine.
    private bool FoeIsSafe(FoeController foe)
    {
        if (IsValidFoePosition(foe.transform.position)) return true;

        Collider2D c = foe.GetComponentInChildren<Collider2D>();
        if (c != null && IsValidFoePosition(c.bounds.center)) return true;

        return false;
    }

    private void RemoveStray(FoeController foe)
    {
        if (logRemovals)
        {
            Debug.Log("FloorManager: removed foe '" + foe.name + "' at " + (Vector2)foe.transform.position +
                      " (not on a floor tile or the protected layer for " + graceSeconds + "s).", foe);
        }

        if (spawner != null) spawner.RemoveStray(foe);   // destroys it AND hands it back so a replacement spawns
        else Destroy(foe.Root);
    }

    // Foes that were destroyed elsewhere (killed, cleared) leave dead keys behind; drop them.
    private void PruneDestroyedFoes()
    {
        if (invalidSince.Count == 0) return;

        pruneList.Clear();
        foreach (KeyValuePair<FoeController, float> pair in invalidSince)
        {
            if (pair.Key == null) pruneList.Add(pair.Key);
        }
        for (int i = 0; i < pruneList.Count; i++) invalidSince.Remove(pruneList[i]);
    }
}