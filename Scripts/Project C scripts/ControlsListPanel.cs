using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The controls list that sits DIRECTLY UNDER a "Keyboard" or "Controller" toggle for one player.
///
///   toggle OFF  ->  the list is hidden, and that player ignores that device
///   toggle ON   ->  the list is shown, filled in from the player's REAL bindings
///
/// Nothing in the list is typed in by hand. With a PlayerInputHub in the scene, every line is read from that player's slot in the
/// hub: the same keys and buttons the game reads its input from, so it also works for a Player 2 who has not been spawned yet.
/// It redraws when a slot, the roster of players or the one-player / two-player situation changes. Pause is read from PauseManager.
///
/// SET UP, once per toggle (so four lists for two players: P1 Keyboard, P1 Controller, P2 Keyboard, P2 Controller):
///   1. Under the toggle, make an empty object (the List Root) and give it a TextMeshPro text (Text). Put the toggle OUTSIDE
///      the List Root, otherwise hiding the list would hide the toggle.
///   2. Add this script anywhere (the List Root's parent is a good home). Set Player Number and Device.
///   3. Drag the toggle into Toggle, the text into Text, and the empty object into List Root.
///   (Without a PlayerInputHub in the scene, drag the Player 2 PREFAB's PlayerInputSource into Prefab Source instead.)
///
/// The toggle drives the player's device switch, so you don't wire the toggle's OnValueChanged yourself. The switch is kept in
/// the hub's slot, so it survives the character being respawned.
///
/// The columns use TextMeshPro's position tag. Players 3 and 4 work the same way; the camera and the HUD layouts
/// only place two views.
/// </summary>
[DisallowMultipleComponent]
public class ControlsListPanel : MonoBehaviour
{
    public enum Device { Keyboard, Controller }

    [Header("Whose controls")]
    [Range(1, 4)] public int playerNumber = 1;
    public Device device = Device.Keyboard;
    [Tooltip("Only needed when the scene has NO PlayerInputHub: the PlayerInputSource on the player's PREFAB, shown until the live player exists.")]
    public PlayerInputSource prefabSource;

    [Header("The toggle and the list")]
    [Tooltip("The Keyboard / Controller toggle this list belongs to. Optional: without one, the list follows the player's device switch.")]
    public Toggle toggle;
    [Tooltip("The text the list is written into.")]
    public TMP_Text text;
    [Tooltip("What gets hidden when the toggle is off. It must NOT contain the toggle. Empty = the text object itself.")]
    public GameObject listRoot;
    [Tooltip("Optional first line. Empty = \"Player 1 Keyboard\" (made from the two settings above).")]
    public string heading = "";
    [Tooltip("Where the second column (the keys) starts, as a percentage of the text width.")]
    [Range(20f, 80f)] public float keyColumnPercent = 45f;

    private PlayerInputHub hub;          // the scene's hub, if it has one (the normal case)
    private PlayerInputSource source;    // only used without a hub: the live player
    private bool userTouchedToggle;
    private readonly StringBuilder sb = new StringBuilder(256);

    // ------------------------------------------------------------------ lifecycle

    private void OnEnable()
    {
        if (text == null)
        {
            Debug.LogError("ControlsListPanel on '" + name + "': the Text slot is empty, so there is nowhere to write the list.", this);
            enabled = false;
            return;
        }

        GameObject root = ListObject();
        if (toggle != null && root != null && toggle.transform.IsChildOf(root.transform))
        {
            Debug.LogError("ControlsListPanel on '" + name + "': the toggle is inside the List Root, so switching the list off would " +
                           "hide the toggle too and it could never be switched back on. Move the toggle out of it.", this);
            enabled = false;
            return;
        }

        hub = PlayerInputHub.Instance;
        if (hub != null) hub.Changed += Redraw;
        else if (prefabSource == null) Debug.LogWarning("ControlsListPanel on '" + name + "': there is no PlayerInputHub in the scene and no Prefab Source, " +
                                                        "so the list can only show a player who is already in the game.", this);

        PlayerInputSource.RosterChanged += OnRosterChanged;
        LocalCoop.Changed += OnCoopChanged;
        if (toggle != null) toggle.onValueChanged.AddListener(OnToggleChanged);

        if (hub != null) SyncToggle();
        else Bind(PlayerInputSource.Get(playerNumber));
        Redraw();
    }

    private void OnDisable()
    {
        if (hub != null) hub.Changed -= Redraw;
        hub = null;
        PlayerInputSource.RosterChanged -= OnRosterChanged;
        LocalCoop.Changed -= OnCoopChanged;
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggleChanged);
        if (source != null) source.Changed -= Redraw;
        source = null;
    }

    // ------------------------------------------------------------------ events

    // A player joined, left or respawned. With a hub the list only needs a redraw (the slot did not change); without one, follow the live player.
    private void OnRosterChanged()
    {
        if (hub != null) { Redraw(); return; }

        PlayerInputSource now = PlayerInputSource.Get(playerNumber);
        if (now != source) Bind(now);
    }

    private void OnCoopChanged(bool on)
    {
        Redraw();   // Player 1's keys change while nobody else plays (the solo keys)
    }

    private void OnToggleChanged(bool on)
    {
        userTouchedToggle = true;
        SetDevice(on);
        Redraw();
    }

    // Without a hub: follow the live player, and let the person's toggle choice carry over to a respawned one.
    private void Bind(PlayerInputSource next)
    {
        if (source != null) source.Changed -= Redraw;
        source = next;

        if (source != null)
        {
            source.Changed += Redraw;
            if (toggle != null)
            {
                if (userTouchedToggle) SetDevice(toggle.isOn);
                else toggle.SetIsOnWithoutNotify(DeviceOn());
            }
        }
        Redraw();
    }

    // With a hub the slot is the truth, so the toggle always shows what the slot says.
    private void SyncToggle()
    {
        if (toggle != null && toggle.isOn != DeviceOn()) toggle.SetIsOnWithoutNotify(DeviceOn());
    }

    // ------------------------------------------------------------------ the player's real settings (hub slot, or the component without a hub)

    private PlayerInputSource Legacy { get { return source != null ? source : prefabSource; } }

    private bool HasConfig { get { return hub != null || Legacy != null; } }

    private bool DeviceOn()
    {
        if (hub != null)
        {
            PlayerInputHub.PlayerSlot slot = hub.Slot(playerNumber);
            return device == Device.Keyboard ? slot.useKeyboard : slot.useController;
        }
        PlayerInputSource s = Legacy;
        return s != null && (device == Device.Keyboard ? s.KeyboardEnabled : s.ControllerEnabled);
    }

    private void SetDevice(bool on)
    {
        if (hub != null)
        {
            if (device == Device.Keyboard) hub.SetKeyboardEnabled(playerNumber, on);
            else hub.SetControllerEnabled(playerNumber, on);
        }
        else if (source != null)
        {
            if (device == Device.Keyboard) source.SetKeyboardEnabled(on);
            else source.SetControllerEnabled(on);
        }
    }

    private KeyCode[] Keys(PlayerAction action)
    {
        return hub != null ? hub.KeysFor(playerNumber, action) : Legacy.KeysFor(playerNumber, action);
    }

    private PadButton Pad(PlayerAction action)
    {
        return hub != null ? hub.PadButtonFor(playerNumber, action) : Legacy.PadButtonFor(action);
    }

    private bool IsPlatformer
    {
        get { return (hub != null ? hub.scheme : Legacy.scheme) == ControlScheme.Platformer; }
    }

    private string ControllerName()
    {
        ControllerSlot slot = hub != null ? hub.Slot(playerNumber).controller : Legacy.controller;
        switch (slot)
        {
            case ControllerSlot.None: return "None";
            case ControllerSlot.Auto: return "Controller " + playerNumber;
            default: return "Controller " + ((int)slot - (int)ControllerSlot.Controller1 + 1);
        }
    }

    // ------------------------------------------------------------------ drawing (only when something changed, never per frame)

    private GameObject ListObject()
    {
        if (listRoot != null) return listRoot;
        return text != null ? text.gameObject : null;
    }

    private void Redraw()
    {
        if (hub != null) SyncToggle();

        bool on;
        if (hub != null) on = DeviceOn();
        else if (toggle != null) on = toggle.isOn;
        else on = DeviceOn();

        GameObject root = ListObject();
        if (root != null && root.activeSelf != on) root.SetActive(on);
        if (!on) return;

        text.text = Build();
    }

    private string Build()
    {
        sb.Length = 0;
        sb.Append("<b>");
        if (!string.IsNullOrEmpty(heading)) sb.Append(heading);
        else sb.Append("Player ").Append(playerNumber).Append(device == Device.Keyboard ? " Keyboard" : " Controller");
        sb.Append("</b>");

        if (!HasConfig)
        {
            sb.Append("\nPlayer ").Append(playerNumber).Append(" is not in the game.");
            return sb.ToString();
        }

        if (hub != null && !hub.IsInGame(playerNumber)) sb.Append("  <size=75%>(not in the game right now)</size>");

        if (device == Device.Keyboard) BuildKeyboard();
        else BuildController();
        return sb.ToString();
    }

    private void BuildKeyboard()
    {
        Row("Move", MoveKeys());
        Row("Run", KeyList(Keys(PlayerAction.Sprint)));
        if (IsPlatformer) Row("Jump", KeyList(Keys(PlayerAction.Jump)));
        Row("Swing Sword", KeyList(Keys(PlayerAction.Attack)));
        Row("Swap", KeyList(Keys(PlayerAction.Interact)));
        Row("Previous / Next Sword", KeyList(Keys(PlayerAction.WeaponPrev)) + " / " + KeyList(Keys(PlayerAction.WeaponNext)));

        // Pause belongs to PauseManager and works for any player; show what it really uses.
        PauseManager pause = PauseManager.Instance;
        if (pause != null && pause.pauseKeys != null && pause.pauseKeys.Length > 0) Row("Pause", KeyList(pause.pauseKeys));
    }

    private void BuildController()
    {
        Row("Controller", ControllerName());
        Row("Move", "Left Stick / D-Pad");
        Row("Run", PadName(Pad(PlayerAction.Sprint)));
        if (IsPlatformer) Row("Jump", PadName(Pad(PlayerAction.Jump)));
        Row("Swing Sword", PadName(Pad(PlayerAction.Attack)));
        Row("Swap", PadName(Pad(PlayerAction.Interact)));
        Row("Previous / Next Sword", PadName(Pad(PlayerAction.WeaponPrev)) + " / " + PadName(Pad(PlayerAction.WeaponNext)));

        PauseManager pause = PauseManager.Instance;
        if (pause != null && pause.controllerStartPauses) Row("Pause", "Start");
    }

    private void Row(string action, string controls)
    {
        sb.Append('\n').Append(action).Append("<pos=").Append(keyColumnPercent.ToString("0")).Append("%>").Append(controls);
    }

    // ------------------------------------------------------------------ names

    // "W A S D" or "Arrow Keys": the first key of each direction. If a direction has several keys (Player 1 alone
    // also answers to the arrows), each set is shown: "W A S D / Arrow Keys".
    private string MoveKeys()
    {
        KeyCode[] up = Keys(PlayerAction.Up);
        KeyCode[] left = Keys(PlayerAction.Left);
        KeyCode[] down = Keys(PlayerAction.Down);
        KeyCode[] right = Keys(PlayerAction.Right);

        int sets = Mathf.Max(Mathf.Max(up.Length, left.Length), Mathf.Max(down.Length, right.Length));
        if (sets == 0) return "-";

        StringBuilder b = new StringBuilder(32);
        for (int i = 0; i < sets; i++)
        {
            if (i >= up.Length || i >= left.Length || i >= down.Length || i >= right.Length) continue;   // an incomplete set can't steer

            if (b.Length > 0) b.Append(" / ");
            if (up[i] == KeyCode.UpArrow && left[i] == KeyCode.LeftArrow && down[i] == KeyCode.DownArrow && right[i] == KeyCode.RightArrow)
            {
                b.Append("Arrow Keys");
            }
            else
            {
                b.Append(KeyName(up[i])).Append(' ').Append(KeyName(left[i])).Append(' ')
                 .Append(KeyName(down[i])).Append(' ').Append(KeyName(right[i]));
            }
        }
        return b.Length > 0 ? b.ToString() : "-";
    }

    private static string KeyList(KeyCode[] keys)
    {
        if (keys == null || keys.Length == 0) return "-";

        StringBuilder b = new StringBuilder(24);
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) b.Append(" / ");
            b.Append(KeyName(keys[i]));
        }
        return b.ToString();
    }

    private static string KeyName(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift: return "Left Shift";
            case KeyCode.RightShift: return "Right Shift";
            case KeyCode.LeftControl: return "Left Ctrl";
            case KeyCode.RightControl: return "Right Ctrl";
            case KeyCode.LeftAlt: return "Left Alt";
            case KeyCode.RightAlt: return "Right Alt";
            case KeyCode.Slash: return "/";
            case KeyCode.Backslash: return "\\";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            case KeyCode.Escape: return "Esc";
            case KeyCode.Return: return "Enter";
            case KeyCode.UpArrow: return "Up Arrow";
            case KeyCode.DownArrow: return "Down Arrow";
            case KeyCode.LeftArrow: return "Left Arrow";
            case KeyCode.RightArrow: return "Right Arrow";
            case KeyCode.None: return "-";
        }
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
        return key.ToString();
    }

    private static string PadName(PadButton button)
    {
        switch (button)
        {
            case PadButton.South: return "South Button (A on Xbox)";
            case PadButton.East: return "East Button (B on Xbox)";
            case PadButton.West: return "West Button (X on Xbox)";
            case PadButton.North: return "North Button (Y on Xbox)";
            case PadButton.LeftShoulder: return "Left Shoulder";
            case PadButton.RightShoulder: return "Right Shoulder";
            case PadButton.LeftTrigger: return "Left Trigger";
            case PadButton.RightTrigger: return "Right Trigger";
            case PadButton.LeftStick: return "Left Stick Click";
            case PadButton.RightStick: return "Right Stick Click";
            case PadButton.Start: return "Start";
            case PadButton.Select: return "Select";
            default: return "-";
        }
    }
}
