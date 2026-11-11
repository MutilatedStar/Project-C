using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using TMPro;
using Ilumisoft.HealthSystem;

/// <summary>
/// Put this on an object that is ALWAYS active in the dungeon scene (an empty GameObject named "FloorController",
/// same idea as MenuController). NOT on a player (players are prefabs) and NOT on a canvas that gets hidden.
///
/// It owns the floor NUMBER and the floor change, and it is the ONE place everything else is linked to. The stairs are
/// gone: floors are changed by DOORS (FloorDoor). Each floor lists ITS OWN doors (see "Floors" below) and this script checks
/// the doors of the floor the team is on, every frame. When every available player is standing on a door AND the players
/// fill the doors, one player per door (NOT everybody on the same door), the team goes down. Then:
///
///   1. the black "Floor Level N" screen fades in, and every foe is cleared (no score),
///   2. EACH player is moved to THEIR OWN spawn point for the new floor (see "Floors" below),
///   3. FloorChanged fires (FloorManager starts the new floor's foes), and the screen fades out.
///
///   Floors        THE LINKER. One element per floor (element 0 = floor 1). Each element holds everything that belongs to
///                 that floor:
///                   - an empty object for Player 1, Player 2 and, if you ever need them, Players 3 and 4: where they land
///                     (a player with no object of their own lands on Player 1's),
///                   - the DOORS of that floor (FloorDoor objects from the scene),
///                   - the pillar ROOMS of that floor (PillarRoom objects from the scene).
///                 Past the last element, the last one is reused. Only the doors and rooms of the CURRENT floor are live:
///                 when the team arrives on a floor, that floor's doors start being checked and the others go quiet.
///   Healing Fountain   a tile whose name (tile asset OR sprite) contains the fountain name. It heals the player who steps
///                 on it ONE time per fountain tile, then its picture changes to the Used Fountain Sprite (if you set one).
///                 ChangeTileSprite() is public if you want to swap any other tile's picture from your own scripts.
///                 Not sure what your tiles are called? Leave Log Tile Under Player on, walk onto the tile, and read the
///                 exact names in the Console.
///   Shop          OpenShop() clears the foes and opens the Shop scene (use a FloorDoor with Action = Shop, or any button).
///                 When the dungeon scene loads again, the players are moved to the next floor's spawn points.
///
/// Players are found by number through CoopHealth, the same way SplitScreenCamera does it.
///
/// Other scripts can read:  FloorController.CurrentFloor,  FloorController.BlocksInput,
///                          FloorController.FloorChanged (event, fires with the new floor number).
///
/// Foes and swords are paused through FoeController.Frozen, which SwordController already respects.
/// </summary>
[DefaultExecutionOrder(-100)]   // runs before everything else, so CurrentFloor is set before any other script reads it
public class FloorController : MonoBehaviour
{
    // ------------------------------------------------------------ inspector

    private enum NameMatch { Contains, Exact }

    private const int MaxPlayers = 4;
    private const float PlayerWaitSeconds = 2f;   // after the Shop, how long to wait for the players to appear

    /// <summary>Everything that belongs to ONE floor: where each player lands, its doors, its pillar rooms.</summary>
    [System.Serializable]
    public class FloorLink
    {
        [Tooltip("Just a name for this row in the Inspector (for example Floor 1). The game doesn't read it.")]
        public string label;
        [Tooltip("Empty object where PLAYER 1 (blue) lands on this floor.")]
        public Transform player1;
        [Tooltip("Empty object where PLAYER 2 (orange) lands on this floor.")]
        public Transform player2;
        [Tooltip("Optional. Player 3. Leave empty if you only have two players: they land on Player 1's object instead.")]
        public Transform player3;
        [Tooltip("Optional. Player 4. Leave empty if you only have two players: they land on Player 1's object instead.")]
        public Transform player4;

        [Tooltip("The doors of THIS floor (drag the FloorDoor objects from the scene). The team goes down when every available player is on a door and the players fill the doors, one per door. " +
                 "Doors set to Shop form a second group that opens the Shop instead.")]
        public FloorDoor[] doors;
        [Tooltip("The pillar rooms of THIS floor (drag the PillarRoom objects from the scene). Only these rooms react while the team is on this floor.")]
        public PillarRoom[] rooms;

        public Transform For(int playerNumber)
        {
            switch (playerNumber)
            {
                case 1: return player1;
                case 2: return player2;
                case 3: return player3;
                case 4: return player4;
                default: return null;
            }
        }
    }

    [Header("Tiles")]
    [Tooltip("Tilemaps to check for the fountain. Leave EMPTY to check every Tilemap in the scene (re-checked once a second, so generated ones count too).")]
    [SerializeField] private Tilemap[] tilemaps;
    [Tooltip("Name of the fountain's Tile asset.")]
    [SerializeField] private string fountainTileName = "Healing Fountain";
    [Tooltip("Where on each player the tile is read, as an offset from the middle of that player's collider (world units). Select this object in Play mode: a small cyan circle shows the exact spot for each player. Nudge this until it sits at their feet.")]
    [SerializeField] private Vector2 checkOffset = Vector2.zero;
    [Tooltip("Contains: the tile's name only has to CONTAIN the name above. Exact: the whole name must match, apart from a number on the end.")]
    [SerializeField] private NameMatch nameMatch = NameMatch.Contains;
    [Tooltip("ON: a tile also counts when the SPRITE it shows has the matching name, not just the tile asset. Tiles made by dragging a sprite onto a Tile Palette are usually named after the sprite, often with a number on the end.")]
    [SerializeField] private bool matchSpriteName = true;
    [Tooltip("ON: prints the tile asset and sprite names under each player to the Console each time they change, and a line for every floor change. Use it to find the exact names, then switch it off.")]
    [SerializeField] private bool logTileUnderPlayer = true;

    [Header("Healing Fountain")]
    [Tooltip("ON: the fountain heals the player to full. OFF: it heals Heal Amount instead.")]
    [SerializeField] private bool healToFull = true;
    [Tooltip("Health restored when Heal To Full is OFF (never goes above Max Health).")]
    [SerializeField, Min(0f)] private float healAmount = 50f;
    [Tooltip("Optional. The picture the fountain's tile changes to once it has been used (an empty fountain). Leave empty to keep the picture. The fountain only heals once either way.")]
    [SerializeField] private Sprite usedFountainSprite;

    [Header("Floors")]
    [Tooltip("The floor number the run starts on.")]
    [SerializeField, Min(1)] private int startingFloor = 1;
    [Tooltip("One element per floor: element 0 = floor 1, element 1 = floor 2, and so on. Each element links that floor's player spawn points, its doors and its pillar rooms. " +
             "If the players go deeper than the list is long, the last element is reused.")]
    [UnityEngine.Serialization.FormerlySerializedAs("floorSpawns")]
    [SerializeField] private FloorLink[] floors;

    [Header("Floor screen (the black screen)")]
    [Tooltip("Your black object. It is hidden until a floor change, then fades in over everything on its canvas. A CanvasGroup is added to it automatically.")]
    [SerializeField] private GameObject floorScreen;
    [Tooltip("The text on it. {0} is replaced by the floor number.")]
    [SerializeField] private TMP_Text floorLabel;
    [SerializeField] private string floorTextFormat = "Floor Level {0}";
    [Tooltip("Seconds the black screen takes to fade in (slowly, on purpose).")]
    [SerializeField, Min(0f)] private float fadeInTime = 1.5f;
    [Tooltip("Seconds the screen stays fully black after the players have been moved, so a smoothed camera can catch up.")]
    [SerializeField, Min(0f)] private float holdTime = 0.75f;
    [Tooltip("Seconds the black screen takes to fade out.")]
    [SerializeField, Min(0f)] private float fadeOutTime = 1f;

    [Header("Shop")]
    [Tooltip("Exact name of the Shop scene. It must be in File > Build Profiles (Build Settings) > Scenes In Build.")]
    [SerializeField] private string shopSceneName = "Shop";

    // ------------------------------------------------------------ public / shared

    /// <summary>The floor the players are on. Starts at Starting Floor and goes up by one each time they go through a door.
    /// It is kept across the trip to the Shop scene, and reset to Starting Floor on any other fresh load of the scene.</summary>
    public static int CurrentFloor { get; private set; } = 1;

    /// <summary>Always false now: the stairs menu is gone. Kept only so any other script that still reads it keeps compiling.</summary>
    public static bool IsStairsOpen { get { return false; } }

    /// <summary>True from the moment a door sends the team down until the black screen has faded away again.</summary>
    public static bool IsChangingFloor { get; private set; }

    /// <summary>True when the players and the notebook should ignore the keyboard: a floor change is running, the floor
    /// change ended earlier this very frame (so the same key press can't also open the notebook), or the pause menu asks for it.</summary>
    public static bool BlocksInput => IsChangingFloor || closedFrame == Time.frameCount || PauseController.BlocksInput || LightsOutScreen.BlocksInput;

    /// <summary>Fires after the players have been moved to a new floor, with the new floor number.</summary>
    public static event System.Action<int> FloorChanged;

    private static int closedFrame = -1;
    private static bool pendingShopReturn;   // set when leaving for the Shop, read by the next FloorController that wakes up

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CurrentFloor = 1;
        IsChangingFloor = false;
        closedFrame = -1;
        pendingShopReturn = false;
        FloorChanged = null;
    }

    // ------------------------------------------------------------ state

    private Tilemap[] autoTilemaps;
    private float nextTilemapRefresh;

    private bool returningFromShop;
    private bool weFroze;
    private CanvasGroup floorGroup;
    private readonly HashSet<(Tilemap, Vector3Int)> usedFountains = new HashSet<(Tilemap, Vector3Int)>();
    private readonly Dictionary<int, string> lastLogged = new Dictionary<int, string>();   // per player number
    private readonly HashSet<string> warned = new HashSet<string>();
    private bool doorsArmed;   // false until the doors have been seen NOT ready, so a door can't fire the instant a floor starts

    // Each player's solid collider, found once (it is only used to find the middle of the player).
    private readonly Collider2D[] colliderCache = new Collider2D[MaxPlayers + 1];
    private readonly CoopHealth[] colliderOwner = new CoopHealth[MaxPlayers + 1];

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        returningFromShop = pendingShopReturn;
        pendingShopReturn = false;
        if (!returningFromShop) CurrentFloor = Mathf.Max(1, startingFloor);

        IsChangingFloor = false;

        if (floorScreen != null)
        {
            floorGroup = floorScreen.GetComponent<CanvasGroup>();
            if (floorGroup == null) floorGroup = floorScreen.AddComponent<CanvasGroup>();
            floorGroup.alpha = 0f;
            floorGroup.interactable = false;
            floorScreen.SetActive(false);
        }
        else
        {
            Debug.LogWarning("FloorController: no Floor Screen assigned, so going down a floor happens with no black screen.", this);
        }
    }

    private void Start()
    {
        ApplyActiveFloor(CurrentFloor);
        CheckWiring();
        if (returningFromShop) StartCoroutine(ChangeFloorRoutine(CurrentFloor, true));
    }

    private void OnDestroy()
    {
        // Never leave the foes frozen behind, e.g. when the Shop scene replaces this one.
        if (weFroze) FoeController.Frozen = false;
        IsChangingFloor = false;
    }

    private void WarnOnce(string key, string message)
    {
        if (warned.Add(key)) Debug.LogWarning(message, this);
    }

    // ------------------------------------------------------------ main loop: the doors and the fountain

    private void Update()
    {
        if (IsChangingFloor) return;
        if (PauseController.IsPaused) return;   // nothing under the players counts while a menu is up
        if (MenuController.IsOpen) return;
        if (LightsOutScreen.IsActive) return;   // the team is wiped: nothing under the downed players counts

        CheckDoors();
        if (IsChangingFloor) return;   // a door just fired

        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            if (p == null || !p.IsAlive || !CoopRules.Counts(n)) continue;

            Vector2 point = CheckPoint(n, p);
            if (logTileUnderPlayer) LogTilesUnder(n, point);

            Tilemap fountainMap;
            Vector3Int fountainCell;
            if (FindTile(fountainTileName, point, out fountainMap, out fountainCell))
            {
                UseFountain(fountainMap, fountainCell, p);
            }
        }
    }

    // ------------------------------------------------------------ reading the tile under a player

    private Vector2 CheckPoint(int playerNumber, CoopHealth player)
    {
        if (colliderOwner[playerNumber] != player)
        {
            colliderOwner[playerNumber] = player;
            colliderCache[playerNumber] = PickCollider(player.gameObject);
        }

        Collider2D c = colliderCache[playerNumber];
        Vector2 centre = c != null ? (Vector2)c.bounds.center : (Vector2)player.transform.position;
        return centre + checkOffset;
    }

    // The player's own solid collider if it has one (the object itself comes first, then its children); a trigger only as a last resort.
    private static Collider2D PickCollider(GameObject go)
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

    private Tilemap[] ActiveTilemaps()
    {
        if (tilemaps != null && tilemaps.Length > 0) return tilemaps;

        if (autoTilemaps == null || Time.unscaledTime >= nextTilemapRefresh)
        {
            autoTilemaps = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            nextTilemapRefresh = Time.unscaledTime + 1f;
        }
        return autoTilemaps;
    }

    // True if any tilemap has a tile with this name under the point.
    private bool FindTile(string tileName, Vector2 worldPoint, out Tilemap foundMap, out Vector3Int foundCell)
    {
        foundMap = null;
        foundCell = Vector3Int.zero;
        if (string.IsNullOrEmpty(tileName)) return false;

        Tilemap[] maps = ActiveTilemaps();
        for (int i = 0; i < maps.Length; i++)
        {
            Tilemap map = maps[i];
            if (map == null) continue;

            Vector3Int cell = map.WorldToCell(worldPoint);
            cell.z = 0;   // a tilemap that sits at a different Z would otherwise look one layer off and find nothing

            TileBase tile = map.GetTile(cell);
            if (tile == null) continue;

            bool match = NameMatches(tile.name, tileName);
            if (!match && matchSpriteName)
            {
                Sprite shown = map.GetSprite(cell);   // works for every kind of tile (plain, rule, animated)
                match = shown != null && NameMatches(shown.name, tileName);
            }
            if (match)
            {
                foundMap = map;
                foundCell = cell;
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------ name matching

    // Capitals, spaces, underscores and dashes are ignored, so "Healing_Fountain", "healing fountain" and "HealingFountain" all look the same.
    private bool NameMatches(string candidate, string wanted)
    {
        string a = Normalize(candidate);
        string b = Normalize(wanted);
        if (a.Length == 0 || b.Length == 0) return false;

        if (nameMatch == NameMatch.Contains) return a.Contains(b);
        return a == b || StripTrailingDigits(a) == b;   // Exact still forgives "Fountain_0" (a sliced sprite's number)
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

    private static string StripTrailingDigits(string text)
    {
        int end = text.Length;
        while (end > 0 && char.IsDigit(text[end - 1])) end--;
        return end > 0 ? text.Substring(0, end) : text;
    }

    // Debug helper: writes the tile asset name and sprite name of every tile under a player, once per change.
    private void LogTilesUnder(int playerNumber, Vector2 worldPoint)
    {
        var sb = new System.Text.StringBuilder();
        Tilemap[] maps = ActiveTilemaps();
        for (int i = 0; i < maps.Length; i++)
        {
            Tilemap map = maps[i];
            if (map == null) continue;

            Vector3Int cell = map.WorldToCell(worldPoint);
            cell.z = 0;

            TileBase tile = map.GetTile(cell);
            if (tile == null) continue;

            Sprite shown = map.GetSprite(cell);
            sb.Append("\n   Tilemap '").Append(map.name).Append("':  tile asset '").Append(tile.name)
              .Append("',  sprite '").Append(shown != null ? shown.name : "none").Append("'");
        }

        string now = sb.Length == 0 ? "\n   no tile on any Tilemap" : sb.ToString();
        string before;
        if (lastLogged.TryGetValue(playerNumber, out before) && before == now) return;
        lastLogged[playerNumber] = now;

        bool fountainHit = FindTile(fountainTileName, worldPoint, out _, out _);
        Debug.Log("FloorController: under Player " + playerNumber + " now:" + now +
                  "\n   looking for fountain = '" + fountainTileName + "' -> " + (fountainHit ? "MATCH" : "no match"), this);
    }

    // ------------------------------------------------------------ healing fountain

    private void UseFountain(Tilemap map, Vector3Int cell, HealthComponent health)
    {
        if (health == null) return;
        if (usedFountains.Contains((map, cell))) return;
        usedFountains.Add((map, cell));

        float healed = healToFull
            ? health.MaxHealth
            : Mathf.Min(health.MaxHealth, health.CurrentHealth + healAmount);
        health.SetHealth(healed);

        if (usedFountainSprite != null) ChangeTileSprite(map, cell, usedFountainSprite);
    }

    /// <summary>Changes the picture of ONE tile on a tilemap (it keeps its collider setting, colour and rotation).
    /// A new tile is made for that cell, so the tile asset in your project is never touched. The new tile is named
    /// "[old name] (changed)", so it no longer matches the old name.</summary>
    public void ChangeTileSprite(Tilemap map, Vector3Int cell, Sprite newSprite)
    {
        if (map == null || newSprite == null) return;

        TileBase old = map.GetTile(cell);
        Tile replacement = ScriptableObject.CreateInstance<Tile>();
        replacement.name = (old != null ? old.name : "Tile") + " (changed)";
        replacement.sprite = newSprite;

        if (old is Tile oldTile)
        {
            replacement.colliderType = oldTile.colliderType;
            replacement.color = oldTile.color;
            replacement.transform = oldTile.transform;
        }
        map.SetTile(cell, replacement);
    }

    // ------------------------------------------------------------ pausing

    // Foes and the sword both obey FoeController.Frozen; the players and notebook obey BlocksInput.
    private void SetPaused(bool paused)
    {
        FoeController.Frozen = paused;
        weFroze = paused;
    }

    /// <summary>True if the mouse is over this button's rectangle. Also used by PauseController for its Quit button.</summary>
    public static bool PointerOver(Button button)
    {
        if (button == null || !button.gameObject.activeInHierarchy) return false;

        // Use the ROOT canvas: a nested canvas (e.g. a panel with its own Canvas for sorting) doesn't carry the render mode or camera.
        Canvas canvas = button.GetComponentInParent<Canvas>();
        Camera cam = null;   // null is correct for Screen Space - Overlay (and for Screen Space - Camera with no camera assigned)
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera) cam = canvas.worldCamera;
            else if (canvas.renderMode == RenderMode.WorldSpace) cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }
        return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, Input.mousePosition, cam);
    }

    // ------------------------------------------------------------ doors

    // The floor the team is on, as an index into Floors (the last element is reused past the end), or -1 with no Floors list.
    private int CurrentRowIndex()
    {
        if (floors == null || floors.Length == 0) return -1;
        return Mathf.Clamp(CurrentFloor - 1, 0, floors.Length - 1);
    }

    // The floor checker. Looks at the doors of the floor the team is on and fires when every available player is on a door
    // and the players fill the doors (FloorDoor.CheckGroup has the exact rule). Doors set to Next Floor and doors set to Shop
    // are two separate groups. The doors of other floors are not looked at at all.
    private void CheckDoors()
    {
        int index = CurrentRowIndex();
        if (index < 0) return;

        FloorLink row = floors[index];
        if (row == null || row.doors == null || row.doors.Length == 0)
        {
            WarnOnce("nodoors" + index, "FloorController: Floors element " + index + " (floor " + (index + 1) + ") has no doors listed, so the team can't leave it. " +
                                        "Drag that floor's FloorDoor objects into its Doors list.");
            return;
        }

        bool down = FloorDoor.CheckGroup(row.doors, FloorDoor.DoorAction.NextFloor);
        bool shop = FloorDoor.CheckGroup(row.doors, FloorDoor.DoorAction.Shop);

        if (!down && !shop)
        {
            doorsArmed = true;   // seen not ready: the next time they ARE ready, they fire
            WatchDoors(row, false);
            return;
        }
        if (!doorsArmed)
        {
            WatchDoors(row, true);
            return;
        }

        doorStallSeconds = 0f;
        doorsArmed = false;
        if (down) GoDown();
        else TryOpenShop();
    }

    // ---- "somebody is on a door and nothing happens": say why, once in a while.
    // Only with Log Tile Under Player on (the Console debug switch of this script). It is just a Console line: nothing changes in the game.

    private const float DoorStallSeconds = 6f;          // somebody has stood on a door this long without the floor changing
    private const float DoorStallRepeatSeconds = 20f;   // and then it is not repeated for this long
    private float doorStallSeconds;

    private void WatchDoors(FloorLink row, bool readyButNotArmed)
    {
        if (!logTileUnderPlayer || !FloorDoor.AnyOccupied(row.doors))
        {
            doorStallSeconds = 0f;
            return;
        }

        doorStallSeconds += Time.unscaledDeltaTime;
        if (doorStallSeconds < DoorStallSeconds) return;
        doorStallSeconds = -DoorStallRepeatSeconds;

        string why = readyButNotArmed
            ? "The doors ARE ready, but they were already ready the moment this floor started, so they wait until somebody steps off a door and on again."
            : "The doors are not ready yet:";
        Debug.Log("FloorController: somebody has been standing on a door for " + DoorStallSeconds.ToString("0") + " seconds and the team has not gone down. " +
                  why + FloorDoor.Explain(row.doors), this);
    }

    // Makes the floor the team has just arrived on the live one: its doors start being checked, the other floors' doors forget
    // who was on them, and only its pillar rooms react. A room that no floor lists is left alone (always live).
    private void ApplyActiveFloor(int floor)
    {
        doorsArmed = false;   // the doors must be seen "not ready" once before they can fire again
        if (floors == null || floors.Length == 0) return;

        int active = Mathf.Clamp(floor - 1, 0, floors.Length - 1);
        for (int i = 0; i < floors.Length; i++)
        {
            FloorLink row = floors[i];
            if (row == null) continue;

            FloorDoor.ClearAll(row.doors);

            if (row.rooms == null) continue;
            for (int r = 0; r < row.rooms.Length; r++)
            {
                if (row.rooms[r] != null) row.rooms[r].SetFloorActive(i == active);
            }
        }
    }

    // Once, when the scene starts: warns about doors no floor lists (they would do nothing) and, with logging on, prints what is linked to what.
    private void CheckWiring()
    {
        if (floors == null || floors.Length == 0)
        {
            WarnOnce("nofloorlist", "FloorController: the Floors list is empty. Add one element per floor and link its spawn points, doors and pillar rooms in it.");
            return;
        }

        FloorDoor[] everyDoor = FindObjectsByType<FloorDoor>(FindObjectsSortMode.None);
        for (int i = 0; i < everyDoor.Length; i++)
        {
            if (!IsListed(everyDoor[i]))
            {
                Debug.LogWarning("FloorController: door '" + everyDoor[i].name + "' is not in any floor's Doors list, so it does nothing. " +
                                 "Drag it into the Doors list of the floor it belongs to.", everyDoor[i]);
            }
        }

        if (!logTileUnderPlayer) return;
        for (int i = 0; i < floors.Length; i++)
        {
            FloorLink row = floors[i];
            if (row == null) continue;

            var sb = new System.Text.StringBuilder();
            sb.Append("FloorController: floor ").Append(i + 1).Append(string.IsNullOrEmpty(row.label) ? "" : " (" + row.label + ")").Append(" is linked to ");

            int nextDoors = 0;
            int shopDoors = 0;
            if (row.doors != null)
            {
                for (int d = 0; d < row.doors.Length; d++)
                {
                    if (row.doors[d] == null) continue;
                    if (row.doors[d].Action == FloorDoor.DoorAction.Shop) shopDoors++;
                    else nextDoors++;
                }
            }
            sb.Append(nextDoors).Append(" door(s)");
            if (shopDoors > 0) sb.Append(" + ").Append(shopDoors).Append(" Shop door(s)");

            if (row.rooms == null || row.rooms.Length == 0) sb.Append(", no pillar rooms.");
            else
            {
                sb.Append(", rooms: ");
                for (int r = 0; r < row.rooms.Length; r++)
                {
                    PillarRoom room = row.rooms[r];
                    if (r > 0) sb.Append("; ");
                    if (room == null) sb.Append("(empty slot)");
                    else sb.Append("'").Append(room.name).Append("' (").Append(room.AltarCount).Append(" altar(s), ").Append(room.PillarCount).Append(" pillar(s))");
                }
            }
            Debug.Log(sb.ToString(), this);
        }
    }

    private bool IsListed(FloorDoor door)
    {
        for (int i = 0; i < floors.Length; i++)
        {
            FloorLink row = floors[i];
            if (row == null || row.doors == null) continue;
            if (System.Array.IndexOf(row.doors, door) >= 0) return true;
        }
        return false;
    }

    /// <summary>Takes every player down one floor with the full change (black screen, move, new foes). Doors call this;
    /// any other script can too. Ignored while a floor change is already running.</summary>
    public void GoDown()
    {
        if (IsChangingFloor) return;

        if (logTileUnderPlayer) Debug.Log("FloorController: heading from floor " + CurrentFloor + " to floor " + (CurrentFloor + 1) + ".", this);
        StartCoroutine(ChangeFloorRoutine(CurrentFloor + 1, false));
    }

    /// <summary>Clears the foes, counts the floor as done, and opens the Shop scene.
    /// The floor number is kept; when the dungeon scene loads again this script moves the players to that floor's
    /// spawn points behind the "Floor Level N" screen. Works from any button's OnClick as well as from a Shop door.</summary>
    public void OpenShop()
    {
        TryOpenShop();
    }

    private bool TryOpenShop()
    {
        if (IsChangingFloor) return false;

        if (!Application.CanStreamedLevelBeLoaded(shopSceneName))
        {
            Debug.LogError("FloorController: the scene '" + shopSceneName + "' can't be loaded. Check the Shop Scene Name spelling " +
                           "and that the scene is in the Scenes In Build list.", this);
            return false;
        }

        ClearFoes();
        CurrentFloor++;
        pendingShopReturn = true;
        SceneManager.LoadScene(shopSceneName);   // OnDestroy un-freezes the foes
        return true;
    }

    // ------------------------------------------------------------ changing floors

    // Fade in, then (while the screen is black) clear the foes and move the players, then fade out.
    // arrivingFromShop skips the fade-in and the clearing: the scene was just loaded, so the screen starts black.
    private IEnumerator ChangeFloorRoutine(int floor, bool arrivingFromShop)
    {
        IsChangingFloor = true;
        SetPaused(true);

        ShowFloorScreen(floor);
        if (arrivingFromShop)
        {
            if (floorGroup != null) floorGroup.alpha = 1f;

            // The players are spawned a moment after the scene loads: give them a little time to appear, or they would not be moved.
            float waited = 0f;
            while (!PlayersReady() && waited < PlayerWaitSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        else
        {
            yield return Fade(0f, 1f, fadeInTime);
        }

        if (!arrivingFromShop)
        {
            ClearFoes();
            CurrentFloor = floor;
        }
        MovePlayersToFloor(floor);
        ApplyActiveFloor(CurrentFloor);   // the next floor's doors and rooms go live, the old ones go quiet
        FloorChanged?.Invoke(CurrentFloor);

        yield return new WaitForSecondsRealtime(holdTime);
        yield return Fade(1f, 0f, fadeOutTime);

        if (floorScreen != null) floorScreen.SetActive(false);

        IsChangingFloor = false;
        SetPaused(false);
        closedFrame = Time.frameCount;
    }

    // True once every player that is meant to be in the game exists.
    private static bool PlayersReady()
    {
        if (CoopRules.OnePlayerOnly) return CoopHealth.Get(CoopRules.TrackedPlayer) != null;
        return CoopHealth.Get(1) != null && CoopHealth.Get(2) != null;
    }

    private void ShowFloorScreen(int floor)
    {
        if (floorLabel != null) floorLabel.text = string.Format(floorTextFormat, floor);
        if (floorScreen == null) return;

        floorGroup.alpha = 0f;
        floorScreen.SetActive(true);
        floorScreen.transform.SetAsLastSibling();   // drawn on top of everything else on its canvas
    }

    // Unscaled time, so the fade still works if something sets Time.timeScale to 0.
    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (floorGroup == null) yield break;

        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            floorGroup.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        floorGroup.alpha = to;
    }

    // Foe.Remove() takes a foe out WITHOUT killing it, so no score is added and OnFoeKilled never fires.
    private void ClearFoes()
    {
        FoeController[] foes = FindObjectsByType<FoeController>(FindObjectsSortMode.None);
        for (int i = 0; i < foes.Length; i++)
        {
            if (foes[i] != null) foes[i].Remove();
        }
    }

    /// <summary>
    /// Where this player lands on this floor: their own point in the Floors list (or Player 1's point if they have none), with the last
    /// element reused past the end, exactly like a floor change. False if the floor has no point for them. LightsOutScreen uses it for Retry.
    /// </summary>
    public bool TryGetSpawnPoint(int floor, int playerNumber, out Vector2 position)
    {
        position = Vector2.zero;
        if (floors == null || floors.Length == 0) return false;

        FloorLink row = floors[Mathf.Clamp(floor - 1, 0, floors.Length - 1)];
        if (row == null) return false;

        Transform spawn = row.For(playerNumber);
        if (spawn == null && playerNumber > 1) spawn = row.For(1);
        if (spawn == null) return false;

        position = spawn.position;
        return true;
    }

    // Moves every player that exists to THEIR OWN point for this floor.
    private void MovePlayersToFloor(int floor)
    {
        if (floors == null || floors.Length == 0)
        {
            WarnOnce("nofloors", "FloorController: the Floors list is empty, so the players stay where they are on the new floor.");
            return;
        }

        int index = floor - 1;
        if (index >= floors.Length)
        {
            WarnOnce("deep" + floor, "FloorController: floor " + floor + " has no row of its own in Floors (the list has " +
                                     floors.Length + "), so the last row is reused.");
        }
        int rowIndex = Mathf.Clamp(index, 0, floors.Length - 1);
        FloorLink row = floors[rowIndex];
        if (row == null)
        {
            WarnOnce("nullrow" + rowIndex, "FloorController: Floors element " + rowIndex + " (floor " + floor + ") is empty, so the players were not moved.");
            return;
        }

        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth player = CoopHealth.Get(n);
            if (player == null) continue;   // not in the game right now

            Transform spawn = row.For(n);
            if (spawn == null && n > 1)
            {
                spawn = row.For(1);
                if (spawn != null)
                {
                    WarnOnce("fallback" + rowIndex + "_" + n, "FloorController: Floors element " + rowIndex + " has no point for Player " + n +
                                                              ", so Player " + n + " lands on Player 1's point.");
                }
            }
            if (spawn == null)
            {
                WarnOnce("missing" + rowIndex + "_" + n, "FloorController: Floors element " + rowIndex + " has no point for Player " + n +
                                                         " (and none for Player 1 to fall back on), so that player was not moved. Drag an empty object into that slot.");
                continue;
            }

            if (logTileUnderPlayer) Debug.Log("FloorController: moving Player " + n + " to floor " + floor + " spawn point '" + spawn.name + "' (element " + rowIndex + ").", this);
            MovePlayer(player, spawn.position);
        }

        Physics2D.SyncTransforms();
    }

    private static void MovePlayer(CoopHealth player, Vector2 position)
    {
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body == null) body = player.GetComponentInParent<Rigidbody2D>();

        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
        }
        Transform t = player.transform;
        t.position = new Vector3(position.x, position.y, t.position.z);
    }

    // ------------------------------------------------------------ editor helper

    // Select this object in Play mode to see exactly where the tile is read for each player (cyan circle).
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        for (int n = 1; n <= MaxPlayers; n++)
        {
            CoopHealth p = CoopHealth.Get(n);
            if (p != null) Gizmos.DrawWireSphere(CheckPoint(n, p), 0.1f);
        }
    }
}
