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

/// <summary>Picks the default keyboard layout: TopDown (Space attacks) or Platformer (Space jumps).</summary>
public enum ControlScheme { TopDown, Platformer }

/// <summary>
/// THE CONTROLLER CONVERTER. Put one on every player (Player 1 prefab: Player Number 1, Player 2 prefab: Player Number 2).
/// It turns "this player's keyboard half + this player's controller" into plain actions that any script can ask for:
///
///     PlayerInputSource input = GetComponent&lt;PlayerInputSource&gt;();
///     Vector2 move = input.Move;            if (input.JumpPressed) ...      if (input.InteractPressed) ...
///
/// Both TopDownCharacterController and PlatformerCharacterController read ONLY this, so the keys and the
/// controller buttons live in one place. Needs DirectButton.cs (KeyInput) and LocalCoopManager.cs (LocalCoop).
///
/// KEYBOARD (edit the table in MakeTable below to change it)
///                      Player 1 (left side)             Player 2 (right side)
///   Move               W A S D                          Arrow keys
///   Run                Left Shift                       Right Shift
///   Attack / weapon    TopDown: Space   Platformer: F   Right Ctrl
///   Jump (platformer)  Space or W                       Up Arrow
///   Interact           TopDown: F       Platformer: E   /  (Slash)
///   Inventory          Tab                              Enter
///   Previous weapon    Q                                ,  (Comma)
///   Next weapon        TopDown: E       Platformer: R   .  (Period)
///   Pick weapon 1-9    number keys (Player 1 only)      -
///   The keys right next to the arrow keys (Right Ctrl, Right Shift) go to what Player 2 does most; the rarer
///   actions sit further along the bottom row.
///
///   Player 2 OFF (LocalCoop.Enabled = false): Player 1 gets BOTH halves of the keyboard (WASD or arrows, either
///   Shift, either attack key...) plus I for the inventory. Player 2 ON: each player only gets their own half.
///
/// CONTROLLER (same for every pad; change the Pad Button fields below per prefab)
///   Left stick / D-pad move,  L3 (click the stick) run,  South jump,  West attack,  East interact,
///   North inventory,  left / right shoulder previous / next weapon.  Start pauses (PauseManager).
///
/// Controllers need the Input System package with Project Settings > Player > Active Input Handling = "Both"
/// (so scripts that still use Input.GetKey keep working). Xbox, PS4, PS5 and Switch Pro pads are recognised as
/// gamepads. A cheap generic pad works if Windows presents it as an Xbox pad; one Unity can't read as a gamepad won't.
/// Controllers are handed out in the order they are plugged in, and a slot stays reserved if a pad is unplugged.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputSource : MonoBehaviour
{
    [Header("Who is this player?")]
    [Tooltip("1 = Player 1 (left side of the keyboard, Controller 1). 2 = Player 2 (right side, Controller 2). 3 and 4 are controller-only.")]
    [Range(1, 4)] public int playerNumber = 1;
    [Tooltip("TopDown: Space attacks. Platformer: Space jumps. Only changes the keyboard layout.")]
    public ControlScheme scheme = ControlScheme.TopDown;

    [Header("Devices")]
    public bool useKeyboard = true;
    public bool useController = true;
    [Tooltip("Auto: Player 1 = Controller 1, Player 2 = Controller 2. Pick one to force it, or None for keyboard only.")]
    public ControllerSlot controller = ControllerSlot.Auto;
    [Tooltip("Stick tilt below this is ignored (stops drifting).")]
    [Range(0f, 0.9f)] public float stickDeadzone = 0.2f;

    [Header("Controller buttons (by position)")]
    public PadButton sprintButton = PadButton.LeftStick;
    public PadButton jumpButton = PadButton.South;
    public PadButton attackButton = PadButton.West;
    public PadButton interactButton = PadButton.East;
    public PadButton inventoryButton = PadButton.North;
    public PadButton weaponPrevButton = PadButton.LeftShoulder;
    public PadButton weaponNextButton = PadButton.RightShoulder;

    /// <summary>Set true while a menu / inventory / cutscene owns the input: every action reads as "nothing pressed".</summary>
    public bool Locked { get; set; }

    /// <summary>True for Player 1 while Player 2 is switched off: Player 1 owns the whole keyboard.</summary>
    public bool SoloKeyboard { get { return playerNumber == 1 && !LocalCoop.Enabled; } }

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
                if (KeysHeld(t[(int)Act.Left])) v.x -= 1f;
                if (KeysHeld(t[(int)Act.Right])) v.x += 1f;
                if (KeysHeld(t[(int)Act.Down])) v.y -= 1f;
                if (KeysHeld(t[(int)Act.Up])) v.y += 1f;
            }
#if ENABLE_INPUT_SYSTEM
            Gamepad pad = Pad;
            if (pad != null) v += Deadzone(pad.leftStick.ReadValue()) + pad.dpad.ReadValue();
#endif
            if (v.sqrMagnitude > 1f) v.Normalize();
            return v;
        }
    }

    public bool SprintPressed { get { return Pressed(Act.Sprint, sprintButton); } }
    public bool SprintHeld { get { return Held(Act.Sprint, sprintButton); } }

    public bool JumpPressed { get { return Pressed(Act.Jump, jumpButton); } }
    public bool JumpHeld { get { return Held(Act.Jump, jumpButton); } }
    public bool JumpReleased { get { return Released(Act.Jump, jumpButton); } }

    public bool AttackPressed { get { return Pressed(Act.Attack, attackButton); } }
    public bool AttackHeld { get { return Held(Act.Attack, attackButton); } }

    public bool InteractPressed { get { return Pressed(Act.Interact, interactButton); } }
    public bool InventoryPressed { get { return Pressed(Act.Inventory, inventoryButton); } }
    public bool WeaponPrevPressed { get { return Pressed(Act.WeaponPrev, weaponPrevButton); } }
    public bool WeaponNextPressed { get { return Pressed(Act.WeaponNext, weaponNextButton); } }

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

    // ------------------------------------------------------------------ state

    private enum Act { Left, Right, Up, Down, Sprint, Jump, Interact, Attack, Inventory, WeaponPrev, WeaponNext }
    private const int ActCount = 11;

    private static readonly List<PlayerInputSource> Instances = new List<PlayerInputSource>();

    private KeyCode[][] _tableCoop;
    private KeyCode[][] _tableSolo;
    private ControlScheme _builtScheme;
    private int _builtPlayer = -1;

    private int _typingFrame = -1;
    private bool _typing;

    private void Awake()
    {
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
    }

    private void OnDisable()
    {
        Instances.Remove(this);
    }

    private void Start()
    {
        // The classic co-op mistake: Player 2's prefab still says Player Number 1, so both fight over the same keys.
        for (int i = 0; i < Instances.Count; i++)
        {
            if (Instances[i] != this && Instances[i].playerNumber == playerNumber)
            {
                Debug.LogWarning("PlayerInputSource: \"" + name + "\" and \"" + Instances[i].name + "\" are both Player " +
                                 playerNumber + ", so they will answer to the same keys and controller. " +
                                 "Set Player Number 2 on the Player 2 prefab.", this);
                break;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Instances.Clear(); }

    // ------------------------------------------------------------------ gating

    // Player 2 only exists while two-player mode is on.
    private bool Active { get { return !Locked && (playerNumber == 1 || LocalCoop.Enabled); } }

    // Typing in a chat / name field must not move the character.
    private bool Typing
    {
        get
        {
            if (_typingFrame != Time.frameCount)
            {
                _typingFrame = Time.frameCount;
                _typing = KeyInput.IsTyping();
            }
            return _typing;
        }
    }

    private bool KeyboardOn { get { return useKeyboard && playerNumber <= 2 && Active && !Typing; } }

    // ------------------------------------------------------------------ keyboard tables

    private KeyCode[][] Table
    {
        get
        {
            if (_tableCoop == null || _builtPlayer != playerNumber || _builtScheme != scheme) BuildTables();
            return LocalCoop.Enabled ? _tableCoop : _tableSolo;
        }
    }

    private void BuildTables()
    {
        _builtPlayer = playerNumber;
        _builtScheme = scheme;
        _tableCoop = MakeTable(scheme, playerNumber);

        if (playerNumber == 1)
        {
            // Player 2 off: Player 1 gets both halves of the keyboard, plus I for the inventory.
            _tableSolo = Merge(_tableCoop, MakeTable(scheme, 2));
            KeyCode[] inventory = _tableSolo[(int)Act.Inventory];
            System.Array.Resize(ref inventory, inventory.Length + 1);
            inventory[inventory.Length - 1] = KeyCode.I;
            _tableSolo[(int)Act.Inventory] = inventory;
        }
        else
        {
            _tableSolo = _tableCoop;
        }
    }

    private static KeyCode[] K(params KeyCode[] keys) { return keys; }

    // THE KEY TABLE. Change keys here.
    private static KeyCode[][] MakeTable(ControlScheme scheme, int player)
    {
        KeyCode[][] t = new KeyCode[ActCount][];
        for (int i = 0; i < ActCount; i++) t[i] = new KeyCode[0];
        bool topDown = scheme == ControlScheme.TopDown;

        if (player == 1)   // LEFT side of the keyboard
        {
            t[(int)Act.Left] = K(KeyCode.A);
            t[(int)Act.Right] = K(KeyCode.D);
            t[(int)Act.Up] = K(KeyCode.W);
            t[(int)Act.Down] = K(KeyCode.S);
            t[(int)Act.Sprint] = K(KeyCode.LeftShift);
            t[(int)Act.Inventory] = K(KeyCode.Tab);
            t[(int)Act.WeaponPrev] = K(KeyCode.Q);

            if (topDown)
            {
                t[(int)Act.Attack] = K(KeyCode.Space);
                t[(int)Act.Interact] = K(KeyCode.F);
                t[(int)Act.WeaponNext] = K(KeyCode.E);
            }
            else
            {
                t[(int)Act.Jump] = K(KeyCode.Space, KeyCode.W);
                t[(int)Act.Attack] = K(KeyCode.F);
                t[(int)Act.Interact] = K(KeyCode.E);
                t[(int)Act.WeaponNext] = K(KeyCode.R);
            }
        }
        else if (player == 2)   // RIGHT side of the keyboard
        {
            t[(int)Act.Left] = K(KeyCode.LeftArrow);
            t[(int)Act.Right] = K(KeyCode.RightArrow);
            t[(int)Act.Up] = K(KeyCode.UpArrow);
            t[(int)Act.Down] = K(KeyCode.DownArrow);
            t[(int)Act.Sprint] = K(KeyCode.RightShift);
            t[(int)Act.Attack] = K(KeyCode.RightControl);
            t[(int)Act.Interact] = K(KeyCode.Slash);
            t[(int)Act.Inventory] = K(KeyCode.Return);
            t[(int)Act.WeaponPrev] = K(KeyCode.Comma);
            t[(int)Act.WeaponNext] = K(KeyCode.Period);
            if (!topDown) t[(int)Act.Jump] = K(KeyCode.UpArrow);
        }
        return t;   // players 3 and 4 have no keyboard keys
    }

    private static KeyCode[][] Merge(KeyCode[][] a, KeyCode[][] b)
    {
        KeyCode[][] t = new KeyCode[ActCount][];
        for (int i = 0; i < ActCount; i++)
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

    private bool Pressed(Act act, PadButton button)
    {
        if (!Active) return false;
        if (KeyboardOn && KeysPressed(Table[(int)act])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, button);
        if (control != null && control.wasPressedThisFrame) return true;
#endif
        return false;
    }

    private bool Held(Act act, PadButton button)
    {
        if (!Active) return false;
        if (KeyboardOn && KeysHeld(Table[(int)act])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, button);
        if (control != null && control.isPressed) return true;
#endif
        return false;
    }

    private bool Released(Act act, PadButton button)
    {
        // Let-go still counts if the player got locked in the same frame, so a jump can't get stuck "held".
        if (playerNumber != 1 && !LocalCoop.Enabled) return false;
        if (useKeyboard && playerNumber <= 2 && KeysReleased(Table[(int)act])) return true;
#if ENABLE_INPUT_SYSTEM
        ButtonControl control = ControllerSlots.Control(Pad, button);
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
    // The controller this player uses right now, or null.
    private Gamepad Pad
    {
        get
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
    }
#endif
}

/// <summary>
/// Hands out controllers in the order they are plugged in: the first pad is Controller 1, the second Controller 2...
/// A slot stays reserved if its pad is unplugged, so Controller 2 doesn't suddenly become Controller 1.
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
    public static int ConnectedCount { get { return 0; } }
#endif
}
