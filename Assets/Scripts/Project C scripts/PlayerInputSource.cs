using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>Controller buttons by POSITION, so one setting works on every pad:
/// South = A (Xbox) / Cross (PlayStation) / B (Switch),  East = B / Circle / A,
/// West = X / Square / Y,  North = Y / Triangle / X.</summary>
public enum PadButton
{
    None, South, East, West, North,
    LeftShoulder, RightShoulder, LeftTrigger, RightTrigger,
    LeftStick, RightStick, Start, Select
}

/// <summary>Which controller a player uses. Auto = Player 1 gets Controller 1, Player 2 gets Controller 2...</summary>
public enum ControllerSlot { Auto, None, Controller1, Controller2, Controller3, Controller4 }

/// <summary>Picks the default keyboard layout: TopDown or Platformer. The only difference is the Jump key.</summary>
public enum ControlScheme { TopDown, Platformer }

/// <summary>Everything a player can do. Move is the four directions; the rest are single buttons.</summary>
public enum PlayerAction { Left, Right, Up, Down, Sprint, Jump, Interact, Attack, Inventory, WeaponPrev, WeaponNext }

/// <summary>
/// One player's keyboard keys. Every action can list several keys (any of them works). A PlayerInputSource keeps one of
/// these for EACH player number and uses the one that matches its Player Number, so changing Player 2's keys can't touch Player 1's.
/// </summary>
[Serializable]
public class KeyBindings
{
    [Tooltip("Move left / right / up / down.")]
    public KeyCode[] left = new KeyCode[0];
    public KeyCode[] right = new KeyCode[0];
    public KeyCode[] up = new KeyCode[0];
    public KeyCode[] down = new KeyCode[0];
    [Tooltip("Run (tap to switch on / off, or hold: set Sprint Toggle on the movement script).")]
    public KeyCode[] sprint = new KeyCode[0];
    [Tooltip("Jump. Only the Platformer controller reads this.")]
    public KeyCode[] jump = new KeyCode[0];
    [Tooltip("Swap. Hold it next to a downed partner to revive them.")]
    public KeyCode[] interact = new KeyCode[0];
    [Tooltip("Swing the sword (hold for the spin).")]
    public KeyCode[] attack = new KeyCode[0];
    [Tooltip("Open the inventory.")]
    public KeyCode[] inventory = new KeyCode[0];
    [Tooltip("Previous / next sword.")]
    public KeyCode[] weaponPrev = new KeyCode[0];
    public KeyCode[] weaponNext = new KeyCode[0];

    public KeyCode[] Get(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.Left: return left;
            case PlayerAction.Right: return right;
            case PlayerAction.Up: return up;
            case PlayerAction.Down: return down;
            case PlayerAction.Sprint: return sprint;
            case PlayerAction.Jump: return jump;
            case PlayerAction.Interact: return interact;
            case PlayerAction.Attack: return attack;
            case PlayerAction.Inventory: return inventory;
            case PlayerAction.WeaponPrev: return weaponPrev;
            default: return weaponNext;
        }
    }

    public void Set(PlayerAction action, params KeyCode[] keys)
    {
        switch (action)
        {
            case PlayerAction.Left: left = keys; break;
            case PlayerAction.Right: right = keys; break;
            case PlayerAction.Up: up = keys; break;
            case PlayerAction.Down: down = keys; break;
            case PlayerAction.Sprint: sprint = keys; break;
            case PlayerAction.Jump: jump = keys; break;
            case PlayerAction.Interact: interact = keys; break;
            case PlayerAction.Attack: attack = keys; break;
            case PlayerAction.Inventory: inventory = keys; break;
            case PlayerAction.WeaponPrev: weaponPrev = keys; break;
            default: weaponNext = keys; break;
        }
    }

    public bool SameAs(KeyBindings other)
    {
        if (other == null) return false;
        for (int i = 0; i < PlayerInputSource.ActionCount; i++)
        {
            KeyCode[] a = Get((PlayerAction)i);
            KeyCode[] b = other.Get((PlayerAction)i);
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int k = 0; k < a.Length; k++) if (a[k] != b[k]) return false;
        }
        return true;
    }

    /// <summary>
    /// THE DEFAULT KEYS for one player number. Player 1 = left half of the keyboard, Player 2 = right half. Players 3 and 4 start with no
    /// keys at all (controller only); add keys to them in the Inspector if you want a keyboard player there.
    /// </summary>
    public static KeyBindings CreateDefault(int player)
    {
        KeyBindings b = new KeyBindings();

        if (player == 1)
        {
            b.left = new[] { KeyCode.A };
            b.right = new[] { KeyCode.D };
            b.up = new[] { KeyCode.W };
            b.down = new[] { KeyCode.S };
            b.sprint = new[] { KeyCode.LeftShift };
            b.attack = new[] { KeyCode.F };
            b.interact = new[] { KeyCode.E };
            b.weaponPrev = new[] { KeyCode.Q };
            b.weaponNext = new[] { KeyCode.R };
            b.inventory = new[] { KeyCode.Tab };
            b.jump = new[] { KeyCode.Space, KeyCode.W };      // only the Platformer controller reads Jump
        }
        else if (player == 2)
        {
            b.left = new[] { KeyCode.LeftArrow };
            b.right = new[] { KeyCode.RightArrow };
            b.up = new[] { KeyCode.UpArrow };
            b.down = new[] { KeyCode.DownArrow };
            b.sprint = new[] { KeyCode.RightShift };
            b.attack = new[] { KeyCode.RightControl };
            b.interact = new[] { KeyCode.Slash };
            b.weaponPrev = new[] { KeyCode.Comma };
            b.weaponNext = new[] { KeyCode.Period };
            b.inventory = new[] { KeyCode.Return };
            b.jump = new[] { KeyCode.UpArrow };
        }
        return b;
    }

    /// <summary>What Player 1 ALSO answers to while Player 2 is switched off: Player 2's half, plus I for the inventory.</summary>
    public static KeyBindings CreateSoloExtras()
    {
        KeyBindings b = CreateDefault(2);
        b.inventory = new[] { KeyCode.Return, KeyCode.I };
        return b;
    }
}

/// <summary>
/// THE CONTROLLER CONVERTER. Put one on every player (Player 1 prefab: Player Number 1, Player 2 prefab: Player Number 2).
/// It turns "this player's keyboard keys + this player's controller" into plain actions that any script can ask for:
///
///     PlayerInputSource input = GetComponent&lt;PlayerInputSource&gt;();
///     Vector2 move = input.Move;            if (input.JumpPressed) ...      if (input.InteractPressed) ...
///
/// TopDownCharacterController, PlatformerCharacterController and SwordController read ONLY this, so keys and
/// buttons live in one place and each player owns their own copy (nothing is shared between players).
///
/// KEYBOARD. The keys are Inspector fields on this component (Keys By Player), not a hidden table. The component holds
/// one set of keys for each Player Number (Element 0 = Player 1 ... Element 3 = Player 4) and uses the one that matches ITS
/// Player Number. So it makes no difference whether the players are separate prefabs or two instances of one prefab: Player 2's
/// keys can never be Player 1's. The defaults below are filled in for you. Context menu: "Reset ... Keys To Defaults".
///                           Player 1             Player 2
///   Move                    W A S D              Arrow keys
///   Run (tap to toggle)     Left Shift           Right Shift
///   Swing sword             F                    Right Ctrl
///   Swap / revive (hold)    E                    / (Slash)
///   Previous / next sword   Q / R                , / .
///   Inventory               Tab                  Enter
///   Jump (Platformer only)  Space or W           Up Arrow
///   Pick sword 1-9          number keys (Player 1 only)
///   Pause is global, not per player: PauseManager (Esc / P, and Start on any controller).
///   Players 3 and 4 have no keys until you add some; they are made for controllers.
///   Player 2 OFF (LocalCoop.Enabled = false): Player 1 also answers to "Solo Extra Keys" (by default Player 2's half
///   of the keyboard and I for the inventory), so a single player can use the whole keyboard.
///
/// CONTROLLER. Left stick and D-pad always move. Every other action has a Pad Button field below. Each player picks
/// their own pad with Controller (Auto = Player N uses Controller N). Controllers are handed out in plug-in order.
/// Needs the Input System package with Project Settings > Player > Active Input Handling = "Both".
/// Xbox, PS4, PS5 and Switch Pro pads are recognised as gamepads.
///
/// PREFABS. Put this on the character prefab (any object of it) and leave Player Number at 1 on the prefab. Then either:
///   - drop two instances in the scene and set Player Number on each INSTANCE (2 on the second), or
///   - spawn from code and bind the instance to a player:
///         GameObject c = Instantiate(characterPrefab);
///         PlayerInputSource.Bind(c, 2);                 // or Bind(c, 2, ControllerSlot.Controller2)
///     Bind finds (or adds) the PlayerInputSource on that character and calls SetPlayer, which also refreshes the UI and
///     re-checks for two players sharing a number. Changing Player Number by hand at runtime works too, but SetPlayer is tidier.
///   Edit keys on the PREFAB, not on every instance: in the editor an instance no longer copies the default keys into itself
///   (that used to turn every key into a per-instance override that ignored later prefab edits).
///   Player 2 and up only answer while LocalCoop.Enabled is on, so switch it on when you spawn a second player.
///
/// UI: ControlsListPanel shows these exact keys and buttons, and hides / shows them with the Keyboard / Controller
/// toggle (SetKeyboardEnabled / SetControllerEnabled). Needs DirectButton.cs (KeyInput) and LocalCoopManager.cs.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputSource : MonoBehaviour
{
    public const int ActionCount = 11;
    public const int MaxPlayers = 4;

    // Bumped when the layout of the saved key data changes, so older prefabs get defaults filled in once.
    private const int BindingsVersion = 2;

    [Header("Who is this player?")]
    [Tooltip("1 = Player 1 (left side of the keyboard, Controller 1). 2 = Player 2 (right side, Controller 2). 3 and 4 are controller-only until you give them keys.")]
    [Range(1, 4)] public int playerNumber = 1;
    [Tooltip("TopDown or Platformer. Only changes what the controls list shows (Jump is listed for Platformer); the keys below are what counts.")]
    public ControlScheme scheme = ControlScheme.TopDown;

    [Header("Devices")]
    [Tooltip("The Keyboard toggle. Off = this player ignores the keyboard completely.")]
    public bool useKeyboard = true;
    [Tooltip("The Controller toggle. Off = this player ignores controllers completely.")]
    public bool useController = true;
    [Tooltip("Auto: Player 1 = Controller 1, Player 2 = Controller 2. Pick one to force it, or None for keyboard only.")]
    public ControllerSlot controller = ControllerSlot.Auto;
    [Tooltip("Stick tilt below this is ignored (stops drifting).")]
    [Range(0f, 0.9f)] public float stickDeadzone = 0.2f;

    [Header("Keyboard keys: one set PER PLAYER NUMBER")]
    [Tooltip("Element 0 = Player 1's keys, Element 1 = Player 2's, Element 2 = Player 3's, Element 3 = Player 4's. This player uses the element " +
             "that matches Player Number, so even if two players share ONE prefab, editing Player 2's element can never change Player 1's.")]
    public KeyBindings[] keysByPlayer = new KeyBindings[MaxPlayers];
    [Tooltip("Extra keys Player 1 ALSO answers to while Player 2 is switched off (by default Player 2's half of the keyboard, and I for the inventory). Only Player 1 uses these.")]
    public KeyBindings soloExtraKeys = new KeyBindings();

    [Header("Controller buttons (by position)")]
    public PadButton sprintButton = PadButton.LeftStick;
    public PadButton jumpButton = PadButton.South;
    [Tooltip("Swing the sword. West = X on Xbox.")]
    public PadButton attackButton = PadButton.West;
    [Tooltip("Swap / revive. East = B on Xbox.")]
    public PadButton interactButton = PadButton.East;
    public PadButton inventoryButton = PadButton.North;
    public PadButton weaponPrevButton = PadButton.LeftShoulder;
    public PadButton weaponNextButton = PadButton.RightShoulder;

    [SerializeField, HideInInspector] private int bindingsVersion;

    /// <summary>Set true while a menu / inventory / cutscene owns the input: every action reads as "nothing pressed".</summary>
    public bool Locked { get; set; }

    /// <summary>True for Player 1 while Player 2 is switched off: Player 1 owns the whole keyboard.</summary>
    public bool SoloKeyboard { get { return playerNumber == 1 && !LocalCoop.Enabled; } }

    /// <summary>Raised when this player's devices or keys change (the UI listens to it).</summary>
    public event Action Changed;

    /// <summary>Raised when a PlayerInputSource appears or disappears (a player joined, left, respawned).</summary>
    public static event Action RosterChanged;

    /// <summary>The player with this number that is in the game right now, or null.</summary>
    public static PlayerInputSource Get(int playerNumber)
    {
        for (int i = 0; i < Instances.Count; i++)
        {
            if (Instances[i].playerNumber == playerNumber) return Instances[i];
        }
        return null;
    }

    /// <summary>
    /// For prefabs and spawners: gives this character (an instantiated prefab or a scene object) to a player. Finds the
    /// PlayerInputSource on it or on a child, adds one if there is none, then calls SetPlayer. Keeps the controller choice.
    /// </summary>
    public static PlayerInputSource Bind(GameObject character, int playerNumber)
    {
        PlayerInputSource source = FindOrAdd(character);
        if (source != null) source.SetPlayer(playerNumber);
        return source;
    }

    /// <summary>Same as Bind, and also picks the controller (Auto = Player N uses Controller N).</summary>
    public static PlayerInputSource Bind(GameObject character, int playerNumber, ControllerSlot slot)
    {
        PlayerInputSource source = FindOrAdd(character);
        if (source != null) source.SetPlayer(playerNumber, slot);
        return source;
    }

    private static PlayerInputSource FindOrAdd(GameObject character)
    {
        if (character == null) return null;
        PlayerInputSource source = character.GetComponentInChildren<PlayerInputSource>(true);
        if (source == null) source = character.AddComponent<PlayerInputSource>();
        return source;
    }

    /// <summary>
    /// Change which player this is while the game runs (a spawner, a character-select screen). Rebuilds the key tables,
    /// forgets the controller picked this frame, tells the UI, and re-checks for two players with the same number.
    /// Safe to call before the object has started (right after Instantiate).
    /// </summary>
    public void SetPlayer(int number)
    {
        playerNumber = Mathf.Clamp(number, 1, MaxPlayers);
        EnsureBindings();
        _tablesDirty = true;
#if ENABLE_INPUT_SYSTEM
        _padFrame = -1;   // the pad resolved earlier this frame belonged to the OLD number
#endif
        if (isActiveAndEnabled)
        {
            if (_started) CheckForClashes();
            if (RosterChanged != null) RosterChanged();
        }
        RaiseChanged();
    }

    /// <summary>SetPlayer, and also pick the controller.</summary>
    public void SetPlayer(int number, ControllerSlot slot)
    {
        controller = slot;
        SetPlayer(number);
    }

    // ------------------------------------------------------------------ device switches (wire the toggles to these)

    public bool KeyboardEnabled { get { return useKeyboard; } }
    public bool ControllerEnabled { get { return useController; } }

    public void SetKeyboardEnabled(bool on)
    {
        if (useKeyboard == on) return;
        useKeyboard = on;
        RaiseChanged();
    }

    public void SetControllerEnabled(bool on)
    {
        if (useController == on) return;
        useController = on;
        RaiseChanged();
    }

    /// <summary>Call after changing Keys / Solo Extra Keys from code, so the tables and the UI catch up.</summary>
    public void NotifyBindingsChanged()
    {
        _tablesDirty = true;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        if (Changed != null) Changed();
    }

    // ------------------------------------------------------------------ actions (ask these from any script)

    /// <summary>Movement from keyboard, stick and D-pad together. Length 0..1 (a gentle stick tilt is shorter).</summary>
    public Vector2 Move
    {
        get
        {
            if (!Active) return Vector2.zero;

            Vector2 v = Vector2.zero;
            if (KeyboardOn)
            {
                KeyCode[][] t = Table;
                if (KeysHeld(t[(int)PlayerAction.Left])) v.x -= 1f;
                if (KeysHeld(t[(int)PlayerAction.Right])) v.x += 1f;
                if (KeysHeld(t[(int)PlayerAction.Down])) v.y -= 1f;
                if (KeysHeld(t[(int)PlayerAction.Up])) v.y += 1f;
            }
#if ENABLE_INPUT_SYSTEM
            Gamepad pad = Pad;
            if (pad != null) v += Deadzone(pad.leftStick.ReadValue()) + pad.dpad.ReadValue();
#endif
            if (v.sqrMagnitude > 1f) v.Normalize();
            return v;
        }
    }

    public bool SprintPressed { get { return Pressed(PlayerAction.Sprint); } }
    public bool SprintHeld { get { return Held(PlayerAction.Sprint); } }

    public bool JumpPressed { get { return Pressed(PlayerAction.Jump); } }
    public bool JumpHeld { get { return Held(PlayerAction.Jump); } }
    public bool JumpReleased { get { return Released(PlayerAction.Jump); } }

    public bool AttackPressed { get { return Pressed(PlayerAction.Attack); } }
    public bool AttackHeld { get { return Held(PlayerAction.Attack); } }

    public bool InteractPressed { get { return Pressed(PlayerAction.Interact); } }
    /// <summary>True while Swap is held (hold next to a downed partner to revive).</summary>
    public bool InteractHeld { get { return Held(PlayerAction.Interact); } }
    public bool InventoryPressed { get { return Pressed(PlayerAction.Inventory); } }
    public bool WeaponPrevPressed { get { return Pressed(PlayerAction.WeaponPrev); } }
    public bool WeaponNextPressed { get { return Pressed(PlayerAction.WeaponNext); } }

    /// <summary>
    /// The run rule both movement scripts share: toggle mode flips on every tap, hold mode follows the key.
    /// Call it once per frame and keep the answer.
    /// </summary>
    public bool ResolveRun(bool toggleMode, bool currentlyRunning)
    {
        if (toggleMode) return SprintPressed ? !currentlyRunning : currentlyRunning;
        return SprintHeld;
    }

    /// <summary>The 0-based weapon slot picked with the number keys 1-9 this frame, or -1. Player 1's keyboard only.</summary>
    public int NumberKeyPressed()
    {
        if (playerNumber != 1 || !KeyboardOn) return -1;
        for (int i = 0; i < 9; i++)
        {
            if (KeyInput.WasPressed(KeyCode.Alpha1 + i)) return i;
        }
        return -1;
    }

    /// <summary>True while a controller is assigned to this player (handy for showing button hints).</summary>
    public bool HasController
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Pad != null;
#else
            return false;
#endif
        }
    }

    // ------------------------------------------------------------------ what the UI shows (the same data the input reads)

    /// <summary>The keys this player's action answers to right now (solo extras included while Player 2 is off). Don't modify the array.</summary>
    public KeyCode[] KeysFor(PlayerAction action)
    {
        return Table[(int)action];
    }

    /// <summary>
    /// The keys a given PLAYER NUMBER uses for this action, read from this component's per-player sets. For showing a player's
    /// controls from a prefab that holds the keys of every player. Allocates when it merges the solo keys: UI use only, not per frame.
    /// </summary>
    public KeyCode[] KeysFor(int player, PlayerAction action)
    {
        EnsureBindings();
        KeyCode[] own = keysByPlayer[Mathf.Clamp(player, 1, MaxPlayers) - 1].Get(action) ?? new KeyCode[0];
        if (player != 1 || LocalCoop.Enabled) return own;

        KeyCode[] extra = soloExtraKeys.Get(action);
        if (extra == null || extra.Length == 0) return own;
        return Merge(own, extra);
    }

    /// <summary>The controller button for this action, or PadButton.None (movement uses the stick and D-pad).</summary>
    public PadButton PadButtonFor(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.Sprint: return sprintButton;
            case PlayerAction.Jump: return jumpButton;
            case PlayerAction.Attack: return attackButton;
            case PlayerAction.Interact: return interactButton;
            case PlayerAction.Inventory: return inventoryButton;
            case PlayerAction.WeaponPrev: return weaponPrevButton;
            case PlayerAction.WeaponNext: return weaponNextButton;
            default: return PadButton.None;
        }
    }

    /// <summary>True when Jump has no controller button either (used for a setup warning on Platformer players).</summary>
    public bool JumpButtonUnset() { return jumpButton == PadButton.None; }

    // ------------------------------------------------------------------ state

    private static readonly List<PlayerInputSource> Instances = new List<PlayerInputSource>(4);

    private KeyCode[][] _tableCoop;
    private KeyCode[][] _tableSolo;
    private bool _tablesDirty = true;
    private int _tablesFor;   // the Player Number the tables were built for, so changing it (even at runtime) rebuilds them
    private bool _started;    // Start has run, so a later SetPlayer must re-check for clashes itself

    private void Awake()
    {
        EnsureBindings();
#if !ENABLE_INPUT_SYSTEM
        if (useController)
        {
            Debug.LogWarning("PlayerInputSource: controller support needs the Input System package (Window > Package Manager) " +
                             "and Project Settings > Player > Active Input Handling = Both. The keyboard works without it.", this);
        }
#endif
    }

    private void OnEnable()
    {
        if (!Instances.Contains(this)) Instances.Add(this);
        LocalCoop.Changed += OnCoopChanged;
        if (RosterChanged != null) RosterChanged();
    }

    private void OnDisable()
    {
        Instances.Remove(this);
        LocalCoop.Changed -= OnCoopChanged;
        if (RosterChanged != null) RosterChanged();
    }

    // Player 2 switching on or off changes which keys Player 1 owns (solo extras), so the UI has to redraw.
    private void OnCoopChanged(bool on)
    {
        RaiseChanged();
    }

    private void Start()
    {
        _started = true;
        CheckForClashes();
        if (playerNumber > 1) StartCoroutine(WarnIfCoopOff());
    }

    // Player 2 and up are ignored while two-player mode is off, and Player 1 then answers to the WHOLE keyboard. From the outside
    // that looks exactly like "both keyboards move one character", so say so once. It waits a second because the co-op manager
    // may switch two-player mode on in its own Start.
    private System.Collections.IEnumerator WarnIfCoopOff()
    {
        yield return new WaitForSecondsRealtime(1f);
        if (!isActiveAndEnabled || playerNumber == 1 || LocalCoop.Enabled) yield break;

        Debug.LogWarning("PlayerInputSource: \"" + name + "\" is Player " + playerNumber + ", but two-player mode is OFF " +
                         "(LocalCoop.Enabled is false). While it is off, this player ignores all input and Player 1 answers to " +
                         "BOTH halves of the keyboard, so one character gets every key. Switch two-player mode on " +
                         "(LocalCoopManager), or deactivate this player.", this);
    }

    // The classic co-op mistake: two players with the same number, or two players sharing a key.
    // Runs once in Start, and again whenever SetPlayer changes the number later (a spawned prefab).
    private void CheckForClashes()
    {
        for (int i = 0; i < Instances.Count; i++)
        {
            PlayerInputSource other = Instances[i];
            if (other == this) continue;

            if (other.playerNumber == playerNumber)
            {
                Debug.LogWarning("PlayerInputSource: \"" + name + "\" and \"" + other.name + "\" are both Player " +
                                 playerNumber + ", so they will answer to the same keys and controller. " +
                                 "Set a different Player Number on one of them.", this);
                continue;
            }
            WarnAboutSharedKeys(other);
        }
    }

    private void WarnAboutSharedKeys(PlayerInputSource other)
    {
        if (!useKeyboard || !other.useKeyboard) return;
        KeyCode[][] mine = CoopTable;
        KeyCode[][] theirs = other.CoopTable;

        for (int a = 0; a < ActionCount; a++)
        {
            for (int x = 0; x < mine[a].Length; x++)
            {
                for (int b = 0; b < ActionCount; b++)
                {
                    if (Array.IndexOf(theirs[b], mine[a][x]) < 0) continue;
                    Debug.LogWarning("PlayerInputSource: Player " + playerNumber + " (" + (PlayerAction)a + ") and Player " +
                                     other.playerNumber + " (" + (PlayerAction)b + ") both use the key " + mine[a][x] +
                                     ", so it will act for both. Change one of them in Keys.", this);
                    return;   // one warning per pair is enough
                }
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instances.Clear();
        RosterChanged = null;
    }

    // ------------------------------------------------------------------ editor: keep the keys in step with Player Number

    private void Reset()
    {
        FillAllDefaults();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        // On a prefab INSTANCE in edit mode, do not fill in default keys: the write would become a per-instance override,
        // and the instance would stop following the prefab. The prefab asset fills itself, and Awake fills any gap at runtime
        // (a runtime write is never saved, so it can't create an override).
        bool editingInstance = !Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this);
        if (!editingInstance) EnsureBindings();
#else
        EnsureBindings();
#endif
        // gameObject.scene is only valid for a real scene object: a prefab ASSET must not raise runtime events.
        if (Application.isPlaying && gameObject.scene.IsValid())
        {
            _tablesDirty = true;   // an Inspector edit during play must show up in the input AND the UI
            RaiseChanged();
        }
    }

    [ContextMenu("Reset THIS Player's Keys To Defaults")]
    private void ResetThisPlayersKeys()
    {
        EnsureBindings();
        keysByPlayer[Mathf.Clamp(playerNumber, 1, MaxPlayers) - 1] = KeyBindings.CreateDefault(playerNumber);
        _tablesDirty = true;
        RaiseChanged();
    }

    [ContextMenu("Reset ALL Players' Keys To Defaults")]
    private void ResetAllKeys()
    {
        FillAllDefaults();
        RaiseChanged();
    }

    private void FillAllDefaults()
    {
        keysByPlayer = new KeyBindings[MaxPlayers];
        for (int i = 0; i < MaxPlayers; i++) keysByPlayer[i] = KeyBindings.CreateDefault(i + 1);
        soloExtraKeys = KeyBindings.CreateSoloExtras();
        bindingsVersion = BindingsVersion;
        _tablesDirty = true;
    }

    // Every prefab and instance carries the keys of ALL four player numbers and uses the one that matches Player Number.
    // A prefab saved before this existed (or a brand new one) is filled with the defaults once.
    private void EnsureBindings()
    {
        if (bindingsVersion < BindingsVersion || keysByPlayer == null || keysByPlayer.Length != MaxPlayers)
        {
            FillAllDefaults();
            return;
        }

        for (int i = 0; i < MaxPlayers; i++) if (keysByPlayer[i] == null) keysByPlayer[i] = KeyBindings.CreateDefault(i + 1);
        if (soloExtraKeys == null) soloExtraKeys = KeyBindings.CreateSoloExtras();
    }

    // ------------------------------------------------------------------ gating

    // Player 2 (and up) only exist while two-player mode is on.
    private bool Active { get { return !Locked && (playerNumber == 1 || LocalCoop.Enabled); } }

    // KeyInput caches this shared EventSystem check once per frame for all players and UI controls.
    private bool Typing { get { return KeyInput.IsTyping(); } }

    // No "player <= 2" rule: a player with no keys simply has nothing to press.
    private bool KeyboardOn { get { return useKeyboard && Active && !Typing; } }

    // ------------------------------------------------------------------ key tables (built from the Inspector fields)

    private KeyCode[][] CoopTable
    {
        get
        {
            if (_tablesDirty || _tableCoop == null || _tablesFor != playerNumber) RebuildTables();
            return _tableCoop;
        }
    }

    private KeyCode[][] Table
    {
        get
        {
            if (_tablesDirty || _tableCoop == null || _tablesFor != playerNumber) RebuildTables();
            return LocalCoop.Enabled ? _tableCoop : _tableSolo;
        }
    }

    // Rebuilt only when the keys change, never per frame.
    private void RebuildTables()
    {
        EnsureBindings();
        _tablesFor = playerNumber;
        _tableCoop = ToTable(keysByPlayer[Mathf.Clamp(playerNumber, 1, MaxPlayers) - 1]);
        _tableSolo = playerNumber == 1 ? Merge(_tableCoop, ToTable(soloExtraKeys)) : _tableCoop;
        _tablesDirty = false;
    }

    private static KeyCode[][] ToTable(KeyBindings b)
    {
        KeyCode[][] t = new KeyCode[ActionCount][];
        for (int i = 0; i < ActionCount; i++)
        {
            KeyCode[] src = b.Get((PlayerAction)i);
            t[i] = src != null ? (KeyCode[])src.Clone() : new KeyCode[0];
        }
        return t;
    }

    private static KeyCode[] Merge(KeyCode[] a, KeyCode[] b)
    {
        List<KeyCode> list = new List<KeyCode>(a);
        for (int j = 0; j < b.Length; j++) if (!list.Contains(b[j])) list.Add(b[j]);
        return list.ToArray();
    }

    private static KeyCode[][] Merge(KeyCode[][] a, KeyCode[][] b)
    {
        KeyCode[][] t = new KeyCode[ActionCount][];
        for (int i = 0; i < ActionCount; i++)
        {
            List<KeyCode> list = new List<KeyCode>(a[i]);
            for (int j = 0; j < b[i].Length; j++)
            {
                if (!list.Contains(b[i][j])) list.Add(b[i][j]);
            }
            t[i] = list.ToArray();
        }
        return t;
    }

    // ------------------------------------------------------------------ reading keys and buttons

    private static bool KeysPressed(KeyCode[] keys)
    {
        for (int i = 0; i < keys.Length; i++) if (KeyInput.WasPressed(keys[i])) return true;
        return false;
    }

    private static bool KeysHeld(KeyCode[] keys)
    {
        for (int i = 0; i < keys.Length; i++) if (KeyInput.IsHeld(keys[i])) return true;
        return false;
    }

    private static bool KeysReleased(KeyCode[] keys)
    {
        for (int i = 0; i < keys.Length; i++) if (KeyInput.WasReleased(keys[i])) return true;
        return false;
    }

    private bool Pressed(PlayerAction action)
    {
        if (!Active) return false;
        if (KeyboardOn && KeysPressed(Table[(int)action])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, PadButtonFor(action));
        if (control != null && control.wasPressedThisFrame) return true;
#endif
        return false;
    }

    private bool Held(PlayerAction action)
    {
        if (!Active) return false;
        if (KeyboardOn && KeysHeld(Table[(int)action])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, PadButtonFor(action));
        if (control != null && control.isPressed) return true;
#endif
        return false;
    }

    private bool Released(PlayerAction action)
    {
        // Let-go still counts if the player got locked in the same frame, so a jump can't get stuck "held".
        if (playerNumber != 1 && !LocalCoop.Enabled) return false;
        if (useKeyboard && KeysReleased(Table[(int)action])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, PadButtonFor(action));
        if (control != null && control.wasReleasedThisFrame) return true;
#endif
        return false;
    }

    private Vector2 Deadzone(Vector2 v)
    {
        float magnitude = v.magnitude;
        if (magnitude <= stickDeadzone) return Vector2.zero;
        return v.normalized * Mathf.Clamp01((magnitude - stickDeadzone) / (1f - stickDeadzone));
    }

#if ENABLE_INPUT_SYSTEM
    // The pad is resolved once per frame and reused by every read (Move, Pressed, Held... each ask for it).
    private int _padFrame = -1;
    private Gamepad _pad;

    private Gamepad Pad
    {
        get
        {
            if (_padFrame != Time.frameCount)
            {
                _padFrame = Time.frameCount;
                _pad = ResolvePad();
            }
            return _pad;
        }
    }

    private Gamepad ResolvePad()
    {
        if (!useController || playerNumber > 4) return null;
        if (playerNumber > 1 && !LocalCoop.Enabled) return null;

        int slot;
        switch (controller)
        {
            case ControllerSlot.None: return null;
            case ControllerSlot.Auto: slot = playerNumber - 1; break;
            default: slot = (int)controller - (int)ControllerSlot.Controller1; break;
        }

        Gamepad pad = ControllerSlots.Get(slot);

        // One-player mode: Player 1 may use ANY connected controller, even if it isn't "Controller 1".
        if (pad == null && controller == ControllerSlot.Auto && playerNumber == 1 && !LocalCoop.Enabled)
            pad = ControllerSlots.FirstConnected();

        return pad;
    }
#endif
}

/// <summary>
/// Hands out controllers in the order they are plugged in: the first pad is Controller 1, the second Controller 2...
/// Unplugging a pad frees only ITS slot: the other pads keep their numbers, so Controller 2 doesn't suddenly become
/// Controller 1. A pad plugged in later takes the first free slot.
/// </summary>
public static class ControllerSlots
{
    public const int MaxSlots = 4;

#if ENABLE_INPUT_SYSTEM
    private static readonly Gamepad[] Slots = new Gamepad[MaxSlots];
    private static int _frame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        for (int i = 0; i < MaxSlots; i++) Slots[i] = null;
        _frame = -1;
    }

    /// <summary>The pad in this slot (0 = Controller 1), or null.</summary>
    public static Gamepad Get(int slot)
    {
        Refresh();
        return (slot >= 0 && slot < MaxSlots) ? Slots[slot] : null;
    }

    /// <summary>Any connected pad (the lowest slot first), or null.</summary>
    public static Gamepad FirstConnected()
    {
        Refresh();
        for (int i = 0; i < MaxSlots; i++)
        {
            if (Slots[i] != null) return Slots[i];
        }
        return null;
    }

    /// <summary>True on the frame ANY assigned controller's Start button went down (used by PauseManager: Start pauses for every player).</summary>
    public static bool AnyStartPressed()
    {
        Refresh();
        for (int i = 0; i < MaxSlots; i++)
        {
            Gamepad pad = Slots[i];
            if (pad != null && pad.startButton.wasPressedThisFrame) return true;
        }
        return false;
    }

    public static int ConnectedCount
    {
        get
        {
            Refresh();
            int count = 0;
            for (int i = 0; i < MaxSlots; i++) if (Slots[i] != null) count++;
            return count;
        }
    }

    /// <summary>The control behind a PadButton on this pad, or null.</summary>
    public static ButtonControl Control(Gamepad pad, PadButton button)
    {
        if (pad == null) return null;
        switch (button)
        {
            case PadButton.South: return pad.buttonSouth;
            case PadButton.East: return pad.buttonEast;
            case PadButton.West: return pad.buttonWest;
            case PadButton.North: return pad.buttonNorth;
            case PadButton.LeftShoulder: return pad.leftShoulder;
            case PadButton.RightShoulder: return pad.rightShoulder;
            case PadButton.LeftTrigger: return pad.leftTrigger;
            case PadButton.RightTrigger: return pad.rightTrigger;
            case PadButton.LeftStick: return pad.leftStickButton;
            case PadButton.RightStick: return pad.rightStickButton;
            case PadButton.Start: return pad.startButton;
            case PadButton.Select: return pad.selectButton;
            default: return null;
        }
    }

    // Once per frame: free the slots of unplugged pads, then give new pads the first free slot.
    private static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        for (int i = 0; i < MaxSlots; i++)
        {
            if (Slots[i] != null && !Slots[i].added) Slots[i] = null;
        }

        var all = Gamepad.all;
        for (int i = 0; i < all.Count; i++)
        {
            Gamepad pad = all[i];
            if (IsAssigned(pad)) continue;

            for (int s = 0; s < MaxSlots; s++)
            {
                if (Slots[s] == null)
                {
                    Slots[s] = pad;
                    break;
                }
            }
        }
    }

    private static bool IsAssigned(Gamepad pad)
    {
        for (int i = 0; i < MaxSlots; i++) if (Slots[i] == pad) return true;
        return false;
    }
#else
    public static bool AnyStartPressed() { return false; }
    public static int ConnectedCount { get { return 0; } }
#endif
}
