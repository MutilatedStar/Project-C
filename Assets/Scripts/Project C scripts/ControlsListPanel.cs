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
/// Nothing in the list is typed in by hand. Every line is read from the same PlayerInputSource the game reads its input
/// from (the keys it really answers to right now, the pad buttons it really uses), and it redraws when the keys, the
/// devices, the roster or the one-player / two-player switch change. Pause is read from PauseManager.
///
/// SET UP, once per toggle (so four lists for two players: P1 Keyboard, P1 Controller, P2 Keyboard, P2 Controller):
///   1. Under the toggle, make an empty object (the List Root) and give it a TextMeshPro text (Text). Put the toggle OUTSIDE
///      the List Root, otherwise hiding the list would hide the toggle.
///   2. Add this script anywhere (the List Root's parent is a good home). Set Player Number and Device.
///   3. Drag the toggle into Toggle, the text into Text, and the empty object into List Root.
///   4. Player 2 is usually spawned from a prefab, so the scene can't point at it yet: drag the Player 2 PREFAB's
///      PlayerInputSource into Prefab Source. The list then shows the keys the prefab will have, and switches to the live
///      player once it exists. If both players are the SAME prefab, point both panels at it: each panel reads the keys of
///      ITS OWN Player Number from that one component.
///
/// The toggle drives the player's device switch (PlayerInputSource.SetKeyboardEnabled / SetControllerEnabled), so
/// you don't wire the toggle's OnValueChanged yourself. Until the person touches the toggle it follows the player's
/// own setting instead, so a prefab that starts with the controller off shows an unticked box.
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
    [Tooltip("Optional, for a player who isn't in the scene yet (Player 2 is spawned from a prefab): the PlayerInputSource on that PREFAB. Shown until the live player exists.")]
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

    private PlayerInputSource source;
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

        PlayerInputSource.RosterChanged += OnRosterChanged;
        LocalCoop.Changed += OnCoopChanged;
        if (toggle != null) toggle.onValueChanged.AddListener(OnToggleChanged);

        Bind(PlayerInputSource.Get(playerNumber));
    }

    private void OnDisable()
    {
        PlayerInputSource.RosterChanged -= OnRosterChanged;
        LocalCoop.Changed -= OnCoopChanged;
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggleChanged);
        if (source != null) source.Changed -= Redraw;
        source = null;
    }

    // ------------------------------------------------------------------ events

    // A player joined, left or respawned: follow the live one for this player number.
    private void OnRosterChanged()
    {
        PlayerInputSource now = PlayerInputSource.Get(playerNumber);
        if (now != source) Bind(now);
    }

    private void OnCoopChanged(bool on)
    {
        Redraw();   // Player 1's keys change while Player 2 is off, and Player 2's list may need to say "not in the game"
    }

    private void OnToggleChanged(bool on)
    {
        userTouchedToggle = true;
        PushToggleToPlayer();
        Redraw();
    }

    private void Bind(PlayerInputSource next)
    {
        if (source != null) source.Changed -= Redraw;
        source = next;

        if (source != null)
        {
            source.Changed += Redraw;
            if (toggle != null)
            {
                if (userTouchedToggle) PushToggleToPlayer();                 // a respawned player takes the person's choice
                else toggle.SetIsOnWithoutNotify(DeviceOn(source));           // otherwise the toggle shows the player's own setting
            }
        }
        Redraw();
    }

    private void PushToggleToPlayer()
    {
        if (source == null || toggle == null) return;
        if (device == Device.Keyboard) source.SetKeyboardEnabled(toggle.isOn);
        else source.SetControllerEnabled(toggle.isOn);
    }

    private bool DeviceOn(PlayerInputSource s)
    {
        return device == Device.Keyboard ? s.KeyboardEnabled : s.ControllerEnabled;
    }

    // ------------------------------------------------------------------ drawing (only when something changed, never per frame)

    private GameObject ListObject()
    {
        if (listRoot != null) return listRoot;
        return text != null ? text.gameObject : null;
    }

    private void Redraw()
    {
        PlayerInputSource shown = source != null ? source : prefabSource;

        bool on;
        if (toggle != null) on = toggle.isOn;
        else on = shown != null && DeviceOn(shown);

        GameObject root = ListObject();
        if (root != null && root.activeSelf != on) root.SetActive(on);
        if (!on) return;

        text.text = Build(shown);
    }

    private string Build(PlayerInputSource s)
    {
        sb.Length = 0;
        sb.Append("<b>");
        if (!string.IsNullOrEmpty(heading)) sb.Append(heading);
        else sb.Append("Player ").Append(playerNumber).Append(device == Device.Keyboard ? " Keyboard" : " Controller");
        sb.Append("</b>");

        if (s == null)
        {
            sb.Append("\nPlayer ").Append(playerNumber).Append(" is not in the game.");
            return sb.ToString();
        }

        if (device == Device.Keyboard) BuildKeyboard(s);
        else BuildController(s);
        return sb.ToString();
    }

    private void BuildKeyboard(PlayerInputSource s)
    {
        Row("Move", MoveKeys(s));
        Row("Run", KeyList(s.KeysFor(playerNumber, PlayerAction.Sprint)));
        if (s.scheme == ControlScheme.Platformer) Row("Jump", KeyList(s.KeysFor(playerNumber, PlayerAction.Jump)));
        Row("Swing Sword", KeyList(s.KeysFor(playerNumber, PlayerAction.Attack)));
        Row("Swap", KeyList(s.KeysFor(playerNumber, PlayerAction.Interact)));
        Row("Previous / Next Sword", KeyList(s.KeysFor(playerNumber, PlayerAction.WeaponPrev)) + " / " + KeyList(s.KeysFor(playerNumber, PlayerAction.WeaponNext)));

        // Pause belongs to PauseManager and works for any player; show what it really uses.
        PauseManager pause = PauseManager.Instance;
        if (pause != null && pause.pauseKeys != null && pause.pauseKeys.Length > 0) Row("Pause", KeyList(pause.pauseKeys));
    }

    private void BuildController(PlayerInputSource s)
    {
        Row("Controller", ControllerName(s));
        Row("Move", "Left Stick / D-Pad");
        Row("Run", PadName(s.PadButtonFor(PlayerAction.Sprint)));
        if (s.scheme == ControlScheme.Platformer) Row("Jump", PadName(s.PadButtonFor(PlayerAction.Jump)));
        Row("Swing Sword", PadName(s.PadButtonFor(PlayerAction.Attack)));
        Row("Swap", PadName(s.PadButtonFor(PlayerAction.Interact)));
        Row("Previous / Next Sword", PadName(s.PadButtonFor(PlayerAction.WeaponPrev)) + " / " + PadName(s.PadButtonFor(PlayerAction.WeaponNext)));

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
    private string MoveKeys(PlayerInputSource s)
    {
        KeyCode[] up = s.KeysFor(playerNumber, PlayerAction.Up);
        KeyCode[] left = s.KeysFor(playerNumber, PlayerAction.Left);
        KeyCode[] down = s.KeysFor(playerNumber, PlayerAction.Down);
        KeyCode[] right = s.KeysFor(playerNumber, PlayerAction.Right);

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

    private string ControllerName(PlayerInputSource s)
    {
        switch (s.controller)
        {
            case ControllerSlot.None: return "None";
            case ControllerSlot.Auto: return "Controller " + playerNumber;
            default: return "Controller " + ((int)s.controller - (int)ControllerSlot.Controller1 + 1);
        }
    }
}
