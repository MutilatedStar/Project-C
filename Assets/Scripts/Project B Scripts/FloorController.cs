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
/// same idea as MenuController). NOT on the player (the player is a prefab and can't hold scene objects) and NOT on
/// a canvas that gets hidden.
///
/// It watches the tile under the player. A tile is matched on the NAME OF THE TILE ASSET (the file name in the
/// Project window) OR on the NAME OF THE SPRITE it shows. Capitals, spaces and underscores don't matter, and with
/// Name Match = Contains, "Stairs_0" or "Dungeon Stairs" both count as "Stairs". Not sure what your tiles are called?
/// Leave Log Tile Under Player on, walk onto the tile, and read the exact names in the Console:
///
///   "Healing Fountain"  heals the player ONE time per fountain tile. Afterwards the tile's picture changes to the
///                       Used Fountain Sprite (if you set one). ChangeTileSprite() is public if you want to
///                       swap any other tile's picture from your own scripts.
///
///   "Stairs"            opens the stairs menu. While it is up, foes, the player, sword swings and the notebook keys
///                       are all held still. Two buttons, hidden the rest of the time:
///                         Go Down a Floor  clears every foe (no score), fades in the black "Floor Level N" screen,
///                                          moves the player to the next floor's spawn point, fades back out.
///                         Shop             clears every foe (no score) and opens the Shop scene.
///                       Esc or P, or clicking anywhere that isn't one of the two buttons, cancels the menu and
///                       un-pauses everything (the next Esc / P then opens the pause menu, see PauseController). It opens again by stepping off and back onto the stairs, or by
///                       pressing the Reopen Key while standing on them.
///
/// ONLY the player uses the stairs and the fountain. The player is found by its TopDownCharacterController (then by the
/// "Player" tag), and anything that has a FoeController on it, above it or below it is never accepted, so a foe standing
/// on a stairs tile does nothing. Optional: drag a scene-placed player into Player Override.
///
/// Other scripts can read:  FloorController.CurrentFloor,  FloorController.BlocksInput,
///                          FloorController.FloorChanged (event, fires with the new floor number).
///
/// Foes and swords are paused through FoeController.Frozen, which SwordController already respects.
/// </summary>
[DefaultExecutionOrder(-100)]   // runs before the player and the sword each frame, so Space/Esc are claimed first
public class FloorController : MonoBehaviour
{
    // ------------------------------------------------------------ inspector

    private enum NameMatch { Contains, Exact }

    [Header("Player")]
    [Tooltip("Optional. Only needed if the player is placed in the scene: drag it here. Leave empty to find it by its TopDownCharacterController, then by the 'Player' tag. An object with a FoeController is never accepted as the player.")]
    [SerializeField] private Transform playerOverride;

    [Header("Tiles")]
    [Tooltip("Tilemaps to check. Leave EMPTY to check every Tilemap in the scene (re-checked once a second, so generated ones count too).")]
    [SerializeField] private Tilemap[] tilemaps;
    [Tooltip("Name of the fountain's Tile asset.")]
    [SerializeField] private string fountainTileName = "Healing Fountain";
    [Tooltip("Name of the stairs' Tile asset.")]
    [SerializeField] private string stairsTileName = "Stairs";
    [Tooltip("Where on the player the tile is read, as an offset from the middle of the player's collider (world units). Select this object in Play mode: a small cyan circle shows the exact spot. Nudge this until it sits at the player's feet.")]
    [SerializeField] private Vector2 checkOffset = Vector2.zero;
    [Tooltip("Contains: the tile's name only has to CONTAIN the name above (\"Stairs_0\" counts as \"Stairs\"). Exact: the whole name must match, apart from a number on the end.")]
    [SerializeField] private NameMatch nameMatch = NameMatch.Contains;
    [Tooltip("ON: a tile also counts when the SPRITE it shows has the matching name, not just the tile asset. Tiles made by dragging a sprite onto a Tile Palette are usually named after the sprite, often with a number on the end.")]
    [SerializeField] private bool matchSpriteName = true;
    [Tooltip("ON: prints the tile asset and sprite names under the player to the Console each time they change. Use it to find the exact names, then switch it off.")]
    [SerializeField] private bool logTileUnderPlayer = true;

    [Header("Healing Fountain")]
    [Tooltip("ON: the fountain heals the player to full. OFF: it heals Heal Amount instead.")]
    [SerializeField] private bool healToFull = true;
    [Tooltip("Health restored when Heal To Full is OFF (never goes above Max Health).")]
    [SerializeField, Min(0f)] private float healAmount = 50f;
    [Tooltip("Optional. The picture the fountain's tile changes to once it has been used (an empty fountain). Leave empty to keep the picture. The fountain only heals once either way.")]
    [SerializeField] private Sprite usedFountainSprite;

    [Header("Stairs menu")]
    [Tooltip("The panel that holds the two buttons. It is hidden until the stairs are used. Keep it on a canvas that is NOT the notebook canvas.")]
    [SerializeField] private GameObject stairsPanel;
    [Tooltip("The Go Down a Floor button. Don't also add an OnClick in the Inspector: this script already hooks it up.")]
    [SerializeField] private Button goDownButton;
    [Tooltip("The Shop button. Same note: no OnClick needed.")]
    [SerializeField] private Button shopButton;
    [Tooltip("Standing on the stairs with the menu closed, this key opens it again. Space is also the sword key, so with Space the player can't swing while standing on the stairs.")]
    [SerializeField] private KeyCode reopenKey = KeyCode.Space;

    [Header("Floors")]
    [Tooltip("The floor number the run starts on.")]
    [SerializeField, Min(1)] private int startingFloor = 1;
    [Tooltip("Where the player lands on each floor: item 0 = floor 1, item 1 = floor 2, and so on. If the player goes deeper than the list is long, the last point is reused.")]
    [SerializeField] private Transform[] floorSpawnPoints;

    [Header("Floor screen (the black screen)")]
    [Tooltip("Your black object. It is hidden until a floor change, then fades in over everything on its canvas. A CanvasGroup is added to it automatically.")]
    [SerializeField] private GameObject floorScreen;
    [Tooltip("The text on it. {0} is replaced by the floor number.")]
    [SerializeField] private TMP_Text floorLabel;
    [SerializeField] private string floorTextFormat = "Floor Level {0}";
    [Tooltip("Seconds the black screen takes to fade in (slowly, on purpose).")]
    [SerializeField, Min(0f)] private float fadeInTime = 1.5f;
    [Tooltip("Seconds the screen stays fully black after the player has been moved, so a smoothed camera can catch up.")]
    [SerializeField, Min(0f)] private float holdTime = 0.75f;
    [Tooltip("Seconds the black screen takes to fade out.")]
    [SerializeField, Min(0f)] private float fadeOutTime = 1f;

    [Header("Shop")]
    [Tooltip("Exact name of the Shop scene. It must be in File > Build Profiles (Build Settings) > Scenes In Build.")]
    [SerializeField] private string shopSceneName = "Shop";

    // ------------------------------------------------------------ public / shared

    /// <summary>The floor the player is on. Starts at Starting Floor and goes up by one each time the player takes the stairs.
    /// It is kept across the trip to the Shop scene, and reset to Starting Floor on any other fresh load of the scene.</summary>
    public static int CurrentFloor { get; private set; } = 1;

    /// <summary>True while the stairs menu is showing.</summary>
    public static bool IsStairsOpen { get; private set; }

    /// <summary>True from the moment "Go Down a Floor" is pressed until the black screen has faded away again.</summary>
    public static bool IsChangingFloor { get; private set; }

    /// <summary>True when the player and the notebook should ignore the keyboard: the stairs menu is up, a floor change is
    /// running, or the stairs menu was closed earlier this very frame (so the same Esc press can't also open the notebook).</summary>
    public static bool BlocksInput => IsStairsOpen || IsChangingFloor || closedFrame == Time.frameCount || PauseController.BlocksInput;

    /// <summary>Fires after the player has been moved to a new floor, with the new floor number.</summary>
    public static event System.Action<int> FloorChanged;

    private static int closedFrame = -1;
    private static bool pendingShopReturn;   // set when leaving for the Shop, read by the next FloorController that wakes up

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CurrentFloor = 1;
        IsStairsOpen = false;
        IsChangingFloor = false;
        closedFrame = -1;
        pendingShopReturn = false;
        FloorChanged = null;
    }

    // ------------------------------------------------------------ state

    private Transform player;
    private Rigidbody2D playerBody;
    private Collider2D playerCollider;
    private HealthComponent playerHealth;
    private float nextPlayerSearch;

    private Tilemap[] autoTilemaps;
    private float nextTilemapRefresh;

    private bool wasOnStairs;
    private bool returningFromShop;
    private bool weFroze;
    private bool warnedSpawn;
    private string lastLoggedTiles = "";
    private bool warnedPanel;
    private string lastBlock;
    private CanvasGroup floorGroup;
    private readonly HashSet<(Tilemap, Vector3Int)> usedFountains = new HashSet<(Tilemap, Vector3Int)>();

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        returningFromShop = pendingShopReturn;
        pendingShopReturn = false;
        if (!returningFromShop) CurrentFloor = Mathf.Max(1, startingFloor);

        IsStairsOpen = false;
        IsChangingFloor = false;

        if (stairsPanel != null) CheckStairsWiring();
        SetStairsUi(false);
        if (stairsPanel == null) Debug.LogWarning("FloorController: drag the panel that holds your two buttons into the Stairs Panel field, or the stairs menu can't show.", this);

        if (goDownButton != null) goDownButton.onClick.AddListener(GoDown);
        else Debug.LogError("FloorController: the Go Down Button field is EMPTY, so the Next Floor button is not hooked up to anything. " +
                            "Drag the button from the Hierarchy into the Go Down Button field on this object.", this);
        if (shopButton != null) shopButton.onClick.AddListener(OpenShop);

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
        if (EnsurePlayer()) wasOnStairs = FindTile(stairsTileName, CheckPoint(), out _, out _);   // starting ON the stairs shouldn't pop the menu up
        if (returningFromShop) StartCoroutine(ChangeFloorRoutine(CurrentFloor, true));
    }

    private void OnDestroy()
    {
        if (goDownButton != null) goDownButton.onClick.RemoveListener(GoDown);
        if (shopButton != null) shopButton.onClick.RemoveListener(OpenShop);

        // Never leave the foes frozen behind, e.g. when the Shop scene replaces this one.
        if (weFroze) FoeController.Frozen = false;
        IsStairsOpen = false;
        IsChangingFloor = false;
    }

    // Finds THE player: the object with the player's movement script (TopDownCharacterController), or the Player Override,
    // or an object tagged "Player". Foes are skipped in every case, so a foe can never become the thing the stairs watch.
    private bool EnsurePlayer()
    {
        if (player != null) return true;
        if (Time.unscaledTime < nextPlayerSearch) return false;
        nextPlayerSearch = Time.unscaledTime + 0.5f;

        GameObject found = FindPlayerObject(out string how);
        if (found == null) return false;

        player = found.transform;
        playerBody = found.GetComponent<Rigidbody2D>();
        playerCollider = PickCollider(found);

        playerHealth = found.GetComponentInParent<HealthComponent>();
        if (playerHealth == null) playerHealth = found.GetComponentInChildren<HealthComponent>();

        if (logTileUnderPlayer)
        {
            Debug.Log("FloorController: using '" + found.name + "' as the player (found " + how + "). Only this object can use the stairs and fountains.", found);
        }
        return true;
    }

    private GameObject FindPlayerObject(out string how)
    {
        how = "";

        if (playerOverride != null && !IsFoe(playerOverride.gameObject))
        {
            how = "through the Player Override field";
            return playerOverride.gameObject;
        }

        // Written out in full (no 'using') so none of that namespace's other names can clash with Tile, Button, etc. in this file.
        var controller = FindAnyObjectByType<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>();
        if (controller != null && !IsFoe(controller.gameObject))
        {
            how = "by its TopDownCharacterController";
            return controller.gameObject;
        }

        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < tagged.Length; i++)
        {
            if (IsFoe(tagged[i])) continue;
            how = "by the 'Player' tag";
            return tagged[i];
        }
        return null;
    }

    // A foe has a FoeController on itself, on a parent or on a child.
    private static bool IsFoe(GameObject go)
    {
        return go.GetComponentInParent<FoeController>() != null || go.GetComponentInChildren<FoeController>() != null;
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

    // ------------------------------------------------------------ main loop

    private void Update()
    {
        if (!EnsurePlayer()) { NoteBlock("no player was found yet (needs a TopDownCharacterController or the 'Player' tag)"); return; }
        if (IsChangingFloor) { NoteBlock("a floor change is running"); return; }

        if (IsStairsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))   // P / Esc close the stairs menu; pressing again then opens the pause menu
            {
                CancelStairs();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                // The buttons are also handled right here, not only through their onClick. That way a click still works when
                // the scene has no EventSystem / GraphicRaycaster, or an invisible UI object sits on top of the button.
                // GoDown and OpenShop ignore a second call, so if onClick fires as well nothing happens twice.
                if (PointerOver(goDownButton))
                {
                    if (goDownButton.interactable) GoDown();
                }
                else if (PointerOver(shopButton))
                {
                    if (shopButton.interactable) OpenShop();
                }
                else
                {
                    if (logTileUnderPlayer) Debug.Log("FloorController: click was not on the Go Down or Shop button, so the stairs menu was cancelled.", this);
                    CancelStairs();   // a misclick
                }
            }
            return;
        }

        if (PauseController.IsPaused) { NoteBlock("the pause menu is open"); return; }   // Space must not reopen the stairs menu behind the pause
        if (MenuController.IsOpen) { NoteBlock("the notebook (MenuController.IsOpen) is open"); return; }   // nothing under the player counts
        if (playerHealth != null && playerHealth.CurrentHealth <= 0f) { NoteBlock("the player's health is 0"); return; }
        NoteBlock(null);

        Vector2 point = CheckPoint();
        if (logTileUnderPlayer) LogTilesUnder(point);

        if (FindTile(fountainTileName, point, out Tilemap fountainMap, out Vector3Int fountainCell))
        {
            UseFountain(fountainMap, fountainCell);
        }

        bool onStairs = FindTile(stairsTileName, point, out _, out _);
        bool justArrived = onStairs && !wasOnStairs;
        wasOnStairs = onStairs;

        if (onStairs && (justArrived || Input.GetKeyDown(reopenKey))) OpenStairs();
    }

    // ------------------------------------------------------------ reading the tile under the player

    private Vector2 CheckPoint()
    {
        Vector2 centre = playerCollider != null ? (Vector2)playerCollider.bounds.center : (Vector2)player.position;
        return centre + checkOffset;
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

    // Capitals, spaces, underscores and dashes are ignored, so "Stairs_Down", "stairs down" and "StairsDown" all look the same.
    private bool NameMatches(string candidate, string wanted)
    {
        string a = Normalize(candidate);
        string b = Normalize(wanted);
        if (a.Length == 0 || b.Length == 0) return false;

        if (nameMatch == NameMatch.Contains) return a.Contains(b);
        return a == b || StripTrailingDigits(a) == b;   // Exact still forgives "Stairs_0" (a sliced sprite's number)
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

    // Debug helper: says ONCE why the stairs check is not running (null = it is running again). If the stairs ever stop
    // reacting, the Console tells you which thing is holding them back.
    private void NoteBlock(string reason)
    {
        if (reason == lastBlock) return;
        lastBlock = reason;
        if (reason != null && logTileUnderPlayer) Debug.Log("FloorController: the stairs check is OFF right now because " + reason + ".", this);
        else if (reason == null && logTileUnderPlayer) Debug.Log("FloorController: the stairs check is running again.", this);
    }

    // Debug helper: writes the tile asset name and sprite name of every tile under the player, once per change.
    private void LogTilesUnder(Vector2 worldPoint)
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
        if (now == lastLoggedTiles) return;
        lastLoggedTiles = now;

        bool stairsHit = FindTile(stairsTileName, worldPoint, out _, out _);
        bool fountainHit = FindTile(fountainTileName, worldPoint, out _, out _);
        Debug.Log("FloorController: under '" + player.name + "' now:" + now +
                  "\n   looking for stairs = '" + stairsTileName + "' -> " + (stairsHit ? "MATCH" : "no match") +
                  ",  fountain = '" + fountainTileName + "' -> " + (fountainHit ? "MATCH" : "no match"), this);
    }

    // ------------------------------------------------------------ healing fountain

    private void UseFountain(Tilemap map, Vector3Int cell)
    {
        if (playerHealth == null) return;
        if (usedFountains.Contains((map, cell))) return;
        usedFountains.Add((map, cell));

        float healed = healToFull
            ? playerHealth.MaxHealth
            : Mathf.Min(playerHealth.MaxHealth, playerHealth.CurrentHealth + healAmount);
        playerHealth.SetHealth(healed);

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

    // ------------------------------------------------------------ stairs menu

    private void OpenStairs()
    {
        if (stairsPanel == null && goDownButton == null && shopButton == null)
        {
            if (!warnedPanel)
            {
                warnedPanel = true;
                Debug.LogError("FloorController: the player is on the stairs, but the Stairs Panel, Go Down Button and Shop Button " +
                               "fields are all EMPTY, so there is no menu to show. Select this object and fill them in.", this);
            }
            return;
        }

        IsStairsOpen = true;
        SetPaused(true);
        SetStairsUi(true);
        if (logTileUnderPlayer) Debug.Log("FloorController: stairs menu opened by '" + player.name + "', game paused.", this);
    }

    private void CancelStairs()
    {
        HideStairsPanel();
        SetPaused(false);
        closedFrame = Time.frameCount;   // the player and MenuController see this and ignore this frame's Esc
    }

    private void HideStairsPanel()
    {
        IsStairsOpen = false;
        SetStairsUi(false);
    }

    // Shows or hides the menu: the panel, plus any button that is NOT inside the panel (so a button that was left outside
    // the panel in the hierarchy still hides until the stairs are used, instead of sitting on screen all the time).
    private void SetStairsUi(bool show)
    {
        if (stairsPanel != null) stairsPanel.SetActive(show);
        SetButtonOutsidePanel(goDownButton, show);
        SetButtonOutsidePanel(shopButton, show);
    }

    private void SetButtonOutsidePanel(Button button, bool show)
    {
        if (button == null) return;
        if (stairsPanel != null && button.transform.IsChildOf(stairsPanel.transform)) return;   // the panel already controls it
        if (button.transform.IsChildOf(transform)) return;                                      // never switch off our own object
        button.gameObject.SetActive(show);
    }

    // Warns once at start-up about hierarchy mistakes that break the menu.
    private void CheckStairsWiring()
    {
        if (transform.IsChildOf(stairsPanel.transform))
        {
            Debug.LogError("FloorController: this script sits INSIDE the Stairs Panel, so hiding the panel switches the script off too. " +
                           "Move FloorController to its own always-active empty object, outside the panel.", this);
        }
        if (goDownButton != null && !goDownButton.transform.IsChildOf(stairsPanel.transform))
        {
            Debug.LogWarning("FloorController: the Go Down Button is not inside the Stairs Panel in the Hierarchy. " +
                             "It is hidden separately now, but putting it inside the panel is tidier.", this);
        }
        if (shopButton != null && !shopButton.transform.IsChildOf(stairsPanel.transform))
        {
            Debug.LogWarning("FloorController: the Shop Button is not inside the Stairs Panel in the Hierarchy. " +
                             "It is hidden separately now, but putting it inside the panel is tidier.", this);
        }
    }

    // Foes and the sword both obey FoeController.Frozen; the player and notebook obey BlocksInput.
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

    // ------------------------------------------------------------ the two buttons

    /// <summary>"Go Down a Floor" button.</summary>
    public void GoDown()
    {
        if (!IsStairsOpen || IsChangingFloor) return;

        if (logTileUnderPlayer) Debug.Log("FloorController: Go Down pressed, heading from floor " + CurrentFloor + " to floor " + (CurrentFloor + 1) + ".", this);
        HideStairsPanel();
        StartCoroutine(ChangeFloorRoutine(CurrentFloor + 1, false));
    }

    /// <summary>"Shop" button. Clears the foes, counts the floor as done, and opens the Shop scene.
    /// The floor number is kept; when the dungeon scene loads again this script moves the player to that floor's
    /// spawn point behind the "Floor Level N" screen.</summary>
    public void OpenShop()
    {
        if (!IsStairsOpen || IsChangingFloor) return;

        if (!Application.CanStreamedLevelBeLoaded(shopSceneName))
        {
            Debug.LogError("FloorController: the scene '" + shopSceneName + "' can't be loaded. Check the Shop Scene Name spelling " +
                           "and that the scene is in the Scenes In Build list.", this);
            return;
        }

        HideStairsPanel();
        ClearFoes();
        CurrentFloor++;
        pendingShopReturn = true;
        SceneManager.LoadScene(shopSceneName);   // OnDestroy un-freezes the foes
    }

    // ------------------------------------------------------------ changing floors

    // Fade in, then (while the screen is black) clear the foes and move the player, then fade out.
    // arrivingFromShop skips the fade-in and the clearing: the scene was just loaded, so the screen starts black.
    private IEnumerator ChangeFloorRoutine(int floor, bool arrivingFromShop)
    {
        IsChangingFloor = true;
        SetPaused(true);

        ShowFloorScreen(floor);
        if (arrivingFromShop)
        {
            if (floorGroup != null) floorGroup.alpha = 1f;
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
        MovePlayerToFloor(floor);
        FloorChanged?.Invoke(CurrentFloor);

        yield return new WaitForSecondsRealtime(holdTime);
        yield return Fade(1f, 0f, fadeOutTime);

        if (floorScreen != null) floorScreen.SetActive(false);

        IsChangingFloor = false;
        SetPaused(false);
        closedFrame = Time.frameCount;
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

    private void MovePlayerToFloor(int floor)
    {
        if (!EnsurePlayer()) return;

        if (floorSpawnPoints == null || floorSpawnPoints.Length == 0)
        {
            if (!warnedSpawn)
            {
                warnedSpawn = true;
                Debug.LogWarning("FloorController: Floor Spawn Points is empty, so the player stays where it is on the new floor.", this);
            }
            return;
        }

        int index = floor - 1;
        if (index >= floorSpawnPoints.Length && !warnedSpawn)
        {
            warnedSpawn = true;
            Debug.LogWarning("FloorController: floor " + floor + " has no spawn point of its own (the list has " +
                             floorSpawnPoints.Length + "), so the last one is reused.", this);
        }
        int spawnIndex = Mathf.Clamp(index, 0, floorSpawnPoints.Length - 1);
        Transform spawn = floorSpawnPoints[spawnIndex];
        if (spawn == null)
        {
            Debug.LogWarning("FloorController: Floor Spawn Points item " + spawnIndex + " (floor " + floor + ") is empty (None), so the player was not moved. " +
                             "Drag the spawn point object into that slot.", this);
            return;
        }
        if (logTileUnderPlayer) Debug.Log("FloorController: moving player to floor " + floor + " spawn point '" + spawn.name + "' (item " + spawnIndex + ").", this);

        Vector2 position = spawn.position;
        if (playerBody != null)
        {
            playerBody.position = position;
            playerBody.linearVelocity = Vector2.zero;
        }
        player.position = new Vector3(position.x, position.y, player.position.z);
        Physics2D.SyncTransforms();

        // Landing on a stairs tile must not pop the menu straight back up.
        wasOnStairs = FindTile(stairsTileName, CheckPoint(), out _, out _);
    }

    // ------------------------------------------------------------ editor helper

    // Select this object in Play mode to see exactly where the tile is read (cyan circle).
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || player == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(CheckPoint(), 0.1f);
    }
}
