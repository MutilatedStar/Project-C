using System;
using UnityEngine;

/// <summary>
/// THE ONE PLACE EVERY PLAYER'S INPUT IS SET UP. Add exactly one of these to the scene (an empty "Input" object, or the object
/// that holds LocalCoopManager) and leave it active. The characters need no input component at all: their scripts
/// (TopDownCharacterController, PlatformerCharacterController, SwordController, CoopHealth) ask for their input with
/// PlayerInputSource.For(gameObject), and this hub gives each character the right player slot.
///
/// SET UP
///   1. Add PlayerInputHub to an empty object in the scene.
///   2. Players, Element 0 = Player 1, Element 1 = Player 2 (Elements 2 and 3 = Players 3 and 4): drag the character into Character.
///      The PREFAB asset works for a character that is spawned (Player 2 from LocalCoopManager), and so does an object that is already
///      in the scene. Keys, Controller and Pad buttons for each player are right there in the same element.
///   3. Remove PlayerInputSource from the character prefabs if you put it there before (optional: if it is left, the hub takes it over and
///      overwrites its Player Number, keys and buttons, so the old values on the prefab are ignored).
///
/// HOW A CHARACTER GETS ITS SLOT (the first rule that fits, checked when its first script wakes up)
///   1. It IS the scene object dragged into a slot's Character          -> that slot.
///   2. Its name matches a slot's prefab (the "(Clone)" and " (1)" parts are ignored) and that slot is free -> that slot.
///   3. A slot with no Character assigned is free                        -> the first such slot.
///   The console says once which slot each character got and why. If two players are the SAME prefab, they take the free slots in the
///   order they wake up, so for characters already in the scene drag each one into its own slot (rule 1) and the order stops mattering.
///   Spawning from code with PlayerInputSource.Bind(instance, 2) or PlayerInputHub.Instance.Claim(instance, 2) picks the slot yourself.
///   A slot belongs to its character until that character is destroyed; a respawned character takes the slot back.
///
/// TWO PLAYERS. Player 2 and up answer as soon as a second character is in the game, and Player 1 stops owning the whole keyboard at
/// that moment. LocalCoop.Enabled (LocalCoopManager) still drives the camera split, HUD and rules; it no longer decides whether
/// Player 2's keys work.
///
/// DEVICES. A player's Keyboard / Controller switches, and a controller picked with Controller, are stored in the slot, so they survive
/// a respawn. ControlsListPanel shows and changes them. The defaults match the table in PlayerInputSource.
///
/// ORDER. This runs before other scripts (Default Execution Order -200) so it exists when the characters wake up. If a scene has no hub,
/// PlayerInputSource still works on its own: put one on each character and set Player Number there.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class PlayerInputHub : MonoBehaviour
{
    /// <summary>The controller buttons of one player (by position: South = A on Xbox, West = X, East = B, North = Y).</summary>
    [Serializable]
    public class PadBindings
    {
        public PadButton sprint = PadButton.LeftStick;
        public PadButton jump = PadButton.South;
        [Tooltip("Swing the sword. West = X on Xbox.")]
        public PadButton attack = PadButton.West;
        [Tooltip("Swap / revive. East = B on Xbox.")]
        public PadButton interact = PadButton.East;
        public PadButton inventory = PadButton.North;
        public PadButton weaponPrev = PadButton.LeftShoulder;
        public PadButton weaponNext = PadButton.RightShoulder;

        public PadButton Get(PlayerAction action)
        {
            switch (action)
            {
                case PlayerAction.Sprint: return sprint;
                case PlayerAction.Jump: return jump;
                case PlayerAction.Attack: return attack;
                case PlayerAction.Interact: return interact;
                case PlayerAction.Inventory: return inventory;
                case PlayerAction.WeaponPrev: return weaponPrev;
                case PlayerAction.WeaponNext: return weaponNext;
                default: return PadButton.None;   // movement is the stick and D-pad
            }
        }
    }

    /// <summary>Everything about ONE player: which character, which devices, which keys and buttons.</summary>
    [Serializable]
    public class PlayerSlot
    {
        [Tooltip("This player's character: the PREFAB (for one that is spawned) or an object already in the scene. Leave empty to let the first character without a slot take this one.")]
        public GameObject character;
        [Tooltip("The Keyboard switch. Off = this player ignores the keyboard.")]
        public bool useKeyboard = true;
        [Tooltip("The Controller switch. Off = this player ignores controllers.")]
        public bool useController = true;
        [Tooltip("Auto: Player 1 = Controller 1, Player 2 = Controller 2. Pick one to force it, or None for keyboard only.")]
        public ControllerSlot controller = ControllerSlot.Auto;
        [Tooltip("This player's keyboard keys. Players 3 and 4 start with none (controller only).")]
        public KeyBindings keys = new KeyBindings();
        public PadBindings pad = new PadBindings();

        /// <summary>The character that is playing this slot right now (set at runtime).</summary>
        [NonSerialized] public PlayerInputSource live;
    }

    [Header("Players  (Element 0 = Player 1, Element 1 = Player 2, 2 = Player 3, 3 = Player 4)")]
    public PlayerSlot[] players = new PlayerSlot[PlayerInputSource.MaxPlayers];

    [Header("Shared by every player")]
    [Tooltip("TopDown or Platformer. Only changes what the controls list shows (Jump is listed for Platformer).")]
    public ControlScheme scheme = ControlScheme.TopDown;
    [Tooltip("Stick tilt below this is ignored (stops drifting).")]
    [Range(0f, 0.9f)] public float stickDeadzone = 0.2f;
    [Tooltip("Extra keys Player 1 ALSO answers to while nobody else is playing (by default Player 2's half of the keyboard, and I for the inventory).")]
    public KeyBindings soloExtraKeys = new KeyBindings();

    [Header("Who is playing right now (read only, filled in during play)")]
    [SerializeField] private GameObject[] playingNow = new GameObject[PlayerInputSource.MaxPlayers];

    [SerializeField, HideInInspector] private bool initialised;

    /// <summary>The hub of this scene, or null if there is none.</summary>
    public static PlayerInputHub Instance { get; private set; }

    /// <summary>Raised when a player's devices, keys or character change (the controls list listens to it).</summary>
    public event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PlayerInputHub: there is already a hub ('" + Instance.name + "'), so this one ('" + name + "') is switched off. " +
                             "One hub per scene holds every player.", this);
            enabled = false;
            return;
        }
        EnsureSlots();
        Instance = this;
    }

    // Characters that survived a scene change (DontDestroyOnLoad) were bound to the old hub: keep their player numbers.
    private void OnEnable()
    {
        if (Instance != this) return;
        for (int n = 1; n <= PlayerInputSource.MaxPlayers; n++)
        {
            PlayerInputSource existing = PlayerInputSource.Get(n);
            if (existing != null && existing.Hub != this) BindToSlot(existing.gameObject, existing, n - 1, "kept from the previous scene");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Reset()
    {
        FillDefaults();
    }

    private void OnValidate()
    {
        EnsureSlots();
        // gameObject.scene is only valid for a real scene object (not a prefab asset): an Inspector edit during play must reach the players.
        if (Application.isPlaying && Instance == this && gameObject.scene.IsValid()) NotifyChanged();
    }

    [ContextMenu("Reset ALL Players To Defaults")]
    private void ResetAll()
    {
        FillDefaults();
        NotifyChanged();
    }

    private void FillDefaults()
    {
        GameObject[] keep = new GameObject[PlayerInputSource.MaxPlayers];
        if (players != null)
        {
            for (int i = 0; i < players.Length && i < keep.Length; i++) if (players[i] != null) keep[i] = players[i].character;
        }

        players = new PlayerSlot[PlayerInputSource.MaxPlayers];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new PlayerSlot { keys = KeyBindings.CreateDefault(i + 1), character = keep[i] };
        }
        soloExtraKeys = KeyBindings.CreateSoloExtras();
        playingNow = new GameObject[PlayerInputSource.MaxPlayers];
        initialised = true;
    }

    private void EnsureSlots()
    {
        if (!initialised || players == null || players.Length != PlayerInputSource.MaxPlayers)
        {
            FillDefaults();
            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null) players[i] = new PlayerSlot();
            if (players[i].keys == null) players[i].keys = KeyBindings.CreateDefault(i + 1);
            if (players[i].pad == null) players[i].pad = new PadBindings();
        }
        if (soloExtraKeys == null) soloExtraKeys = KeyBindings.CreateSoloExtras();
        if (playingNow == null || playingNow.Length != PlayerInputSource.MaxPlayers) playingNow = new GameObject[PlayerInputSource.MaxPlayers];
    }

    // ------------------------------------------------------------------ reading a player's settings

    /// <summary>The settings of Player 1 to 4. Never null.</summary>
    public PlayerSlot Slot(int playerNumber)
    {
        return players[Mathf.Clamp(playerNumber, 1, PlayerInputSource.MaxPlayers) - 1];
    }

    /// <summary>True while a character is playing this player's slot and is switched on.</summary>
    public bool IsInGame(int playerNumber)
    {
        PlayerInputSource live = Slot(playerNumber).live;
        return live != null && live.isActiveAndEnabled;
    }

    /// <summary>
    /// The keys this player's action answers to right now (for the controls list). Player 1 also gets the solo keys while nobody else
    /// is playing, exactly like the input itself. Allocates when it merges: UI use only, not per frame.
    /// </summary>
    public KeyCode[] KeysFor(int playerNumber, PlayerAction action)
    {
        KeyCode[] own = Slot(playerNumber).keys.Get(action) ?? new KeyCode[0];
        if (playerNumber != 1 || PlayerInputSource.TwoPlayers) return own;

        KeyCode[] extra = soloExtraKeys.Get(action);
        if (extra == null || extra.Length == 0) return own;
        return PlayerInputSource.Merge(own, extra);
    }

    public PadButton PadButtonFor(int playerNumber, PlayerAction action)
    {
        return Slot(playerNumber).pad.Get(action);
    }

    // ------------------------------------------------------------------ giving a character its slot

    /// <summary>
    /// The input source of this character, bound to its player slot. Adds the component if the character has none and takes over one
    /// that is there. Returns the same component on every later call. See the class comment for how the slot is chosen.
    /// </summary>
    public PlayerInputSource Attach(GameObject character)
    {
        if (character == null) return null;

        PlayerInputSource source = character.GetComponent<PlayerInputSource>();
        if (source != null && source.Hub == this) return source;

        string why;
        int index = ChooseSlot(character, out why);
        if (index < 0)
        {
            // Better silent than stealing Player 1's keys: this character gets no input, and the log says what to fix.
            if (source == null) source = character.AddComponent<PlayerInputSource>();
            source.useKeyboard = false;
            source.useController = false;
            Debug.LogError("PlayerInputHub: no free player slot for '" + character.name + "', so it will not respond to any input. " +
                           "Every slot already has a character, or its Character field holds a different prefab. Assign this character " +
                           "(or its prefab) to a slot, or leave a slot's Character empty.", character);
            return source;
        }
        return BindToSlot(character, source, index, why);
    }

    /// <summary>Gives this character to a specific player (a spawner or a character-select screen). Moves it if it had another slot.</summary>
    public PlayerInputSource Claim(GameObject character, int playerNumber)
    {
        if (character == null) return null;

        int index = Mathf.Clamp(playerNumber, 1, PlayerInputSource.MaxPlayers) - 1;
        PlayerInputSource source = character.GetComponent<PlayerInputSource>();

        if (source != null && source.Hub == this && players[index].live == source)
        {
            CopySettingsTo(source);   // already there: just refresh its settings
            return source;
        }
        return BindToSlot(character, source, index, "assigned by code");
    }

    private PlayerInputSource BindToSlot(GameObject character, PlayerInputSource source, int index, string why)
    {
        if (source == null) source = character.AddComponent<PlayerInputSource>();

        // Leave any slot this component held before (it is being moved).
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].live == source) { players[i].live = null; playingNow[i] = null; }
        }

        PlayerSlot slot = players[index];
        if (slot.live != null && slot.live != source)
        {
            Debug.LogWarning("PlayerInputHub: Player " + (index + 1) + " was '" + slot.live.name + "' and is now '" + character.name +
                             "'. The old character is still bound to Player " + (index + 1) + " until it is destroyed or moved, so both will answer.", character);
        }

        slot.live = source;
        playingNow[index] = character;
        source.BindToHub(this, index + 1);

        if (!string.IsNullOrEmpty(why)) Debug.Log("PlayerInputHub: '" + character.name + "' is Player " + (index + 1) + " (" + why + ").", character);
        if (Changed != null) Changed();
        return source;
    }

    // The first rule that fits wins. Returns -1 when no slot can take the character.
    private int ChooseSlot(GameObject character, out string why)
    {
        // 1. A scene object that was dragged into a slot.
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].character == character && IsFree(i))
            {
                why = "this object is assigned to the slot";
                return i;
            }
        }

        // 2. The same prefab as a slot's Character. A slot pinned to ANOTHER scene object is not up for grabs.
        string baseName = BaseName(character.name);
        for (int i = 0; i < players.Length; i++)
        {
            GameObject assigned = players[i].character;
            if (assigned == null || !IsFree(i)) continue;
            if (assigned.scene.IsValid() && assigned != character) continue;
            if (BaseName(assigned.name) == baseName)
            {
                why = "same prefab as the slot's Character";
                return i;
            }
        }

        // 3. A slot with no Character assigned: first come, first served.
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].character == null && IsFree(i))
            {
                why = "first free slot";
                return i;
            }
        }

        why = null;
        return -1;
    }

    private bool IsFree(int index)
    {
        return players[index].live == null;   // a destroyed character reads as null, so its slot opens up again
    }

    // "Player(Clone)" and "Player (1)" are the same prefab as "Player".
    private static string BaseName(string objectName)
    {
        string n = objectName.Trim();
        const string clone = "(Clone)";
        while (n.EndsWith(clone, StringComparison.Ordinal)) n = n.Substring(0, n.Length - clone.Length).TrimEnd();

        int open = n.LastIndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && n.EndsWith(")", StringComparison.Ordinal))
        {
            int ignored;
            if (int.TryParse(n.Substring(open + 2, n.Length - open - 3), out ignored)) n = n.Substring(0, open);
        }
        return n;
    }

    // ------------------------------------------------------------------ pushing settings to the characters

    /// <summary>Writes this slot's settings into the source (devices, controller, buttons, and the shared keys). The slot is the truth.</summary>
    public void CopySettingsTo(PlayerInputSource source)
    {
        PlayerSlot slot = Slot(source.playerNumber);
        PadBindings pad = slot.pad;

        source.useKeyboard = slot.useKeyboard;
        source.useController = slot.useController;
        source.controller = slot.controller;
        source.scheme = scheme;
        source.stickDeadzone = stickDeadzone;

        source.sprintButton = pad.sprint;
        source.jumpButton = pad.jump;
        source.attackButton = pad.attack;
        source.interactButton = pad.interact;
        source.inventoryButton = pad.inventory;
        source.weaponPrevButton = pad.weaponPrev;
        source.weaponNextButton = pad.weaponNext;

        KeyBindings[] byPlayer = new KeyBindings[PlayerInputSource.MaxPlayers];
        for (int i = 0; i < byPlayer.Length; i++) byPlayer[i] = players[i].keys;   // shared with the hub, so edits here reach the source
        source.AdoptKeys(byPlayer, soloExtraKeys);
    }

    /// <summary>Call after changing a slot from code (keys, buttons, devices) so every playing character and the UI catch up.</summary>
    public void NotifyChanged()
    {
        if (players == null) return;
        for (int i = 0; i < players.Length; i++)
        {
            PlayerInputSource live = players[i].live;
            if (live == null) continue;
            CopySettingsTo(live);
            live.NotifyBindingsChanged();
        }
        if (Changed != null) Changed();
    }

    // ------------------------------------------------------------------ device switches (the toggles)

    public void SetKeyboardEnabled(int playerNumber, bool on)
    {
        PlayerSlot slot = Slot(playerNumber);
        slot.useKeyboard = on;
        if (slot.live != null) slot.live.SetKeyboardEnabled(on);
        if (Changed != null) Changed();
    }

    public void SetControllerEnabled(int playerNumber, bool on)
    {
        PlayerSlot slot = Slot(playerNumber);
        slot.useController = on;
        if (slot.live != null) slot.live.SetControllerEnabled(on);
        if (Changed != null) Changed();
    }

    /// <summary>Called by a source whose own switch was flipped, so the slot keeps the choice for the next respawn. Does not push back.</summary>
    public void RememberDevice(int playerNumber, bool keyboard, bool on)
    {
        PlayerSlot slot = Slot(playerNumber);
        if (keyboard) slot.useKeyboard = on;
        else slot.useController = on;
        if (Changed != null) Changed();
    }

    /// <summary>Called by a source whose controller was changed, for the same reason as RememberDevice.</summary>
    public void RememberController(int playerNumber, ControllerSlot controller)
    {
        Slot(playerNumber).controller = controller;
        if (Changed != null) Changed();
    }
}
