using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Code-side tap detection for a UI control. It never uses Button.onClick or EventSystem raycasts.
///
/// WasTapped() is true on the frame when EITHER
///   - its hotkey (optional) was pressed, OR
///   - a mouse click / finger press landed inside the Button's own rectangle, the optional Area,
///     or any of the optional Extra Areas.
///
/// It is built to be hard to break and loud when it IS broken:
///   - The Button is optional: assign a plain RectTransform as "Area" and it still works.
///   - A rectangle that is 0 x 0 big (a very common reason clicks do nothing) prints a warning
///     the first time the control is tapped while visible.
///   - Tick "Debug Log" to see every tap, and for every miss the screen rectangle that was missed.
///
/// Use it as a field in any MonoBehaviour:
///     public DirectButton myButton = new DirectButton();
///     void Update() { if (myButton.WasTapped()) DoThing(); }
///
/// Works with the old Input Manager, the new Input System, or both.
/// </summary>
[System.Serializable]
public class DirectButton
{
    [Tooltip("Drag the object that has the Button component here (NOT its text child).")]
    public Button button;

    [Tooltip("Optional. A plain RectTransform that counts as the clickable area. Use it when there is no Button component, or in addition to one.")]
    public RectTransform area;

    [Tooltip("Optional. More rectangles where a tap also counts (for example a text label that sticks out of the button). Only used when a Button or Area is assigned.")]
    public RectTransform[] extraAreas;

    [Tooltip("Optional keyboard key that does the same as tapping.\nIgnored while the player is typing in an input field.\nIf a Button/Area is assigned, the key only works while that object is visible (active).")]
    public KeyCode hotkey = KeyCode.None;

    [Tooltip("Optional. A TMP text to update (e.g. 'Music: ON'). Leave empty and the button's text is never touched.")]
    public TMP_Text label;

    [Tooltip("Off (default): ignores taps while the Button is not Interactable or its CanvasGroup blocks it.\nOn: taps always count while the object is active.")]
    public bool ignoreInteractable = false;

    [Tooltip("Debug: prints every tap, and for every miss the screen rectangle that was missed. Turn off once it works.")]
    public bool debugLog = false;

    /// <summary>True if a Button or an Area is assigned (a control with only a hotkey returns false).</summary>
    public bool HasTarget { get { return button != null || area != null; } }

    private bool _warnedZero;

    private static readonly List<Vector2> Presses = new List<Vector2>(4);
    private static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>True on the single frame this control was tapped (or its hotkey was pressed).</summary>
    public bool WasTapped()
    {
        // 1) Keyboard
        if (hotkey != KeyCode.None && KeyInput.WasPressed(hotkey) && HotkeyAllowed())
        {
            if (debugLog) Debug.Log("DirectButton: hotkey " + hotkey + " pressed for \"" + TargetName() + "\".");
            return true;
        }

        // 2) Mouse / touch
        CollectPresses();
        if (Presses.Count == 0 || !HasTarget || !CanInteract()) return false;

        for (int i = 0; i < Presses.Count; i++)
        {
            if (HitsAnyRect(Presses[i]))
            {
                if (debugLog) Debug.Log("DirectButton: tap at " + Presses[i] + " hit \"" + TargetName() + "\".");
                return true;
            }
        }

        if (debugLog)
            Debug.Log("DirectButton: tap at " + Presses[0] + " missed \"" + TargetName() + "\" (" + DescribeMainRect() + ").");
        return false;
    }

    /// <summary>Changes the optional label text. Does nothing if no label is assigned.</summary>
    public void SetLabel(string text)
    {
        if (label != null) label.text = text;
    }

    // ------------------------------------------------------------------ internals

    private bool HotkeyAllowed()
    {
        if (KeyInput.IsTyping()) return false;
        if (!HasTarget) return true;                 // hotkey-only control
        return AnyTargetActive() && CanInteract();   // otherwise only while it is visible
    }

    private bool AnyTargetActive()
    {
        return (button != null && button.gameObject.activeInHierarchy)
            || (area != null && area.gameObject.activeInHierarchy);
    }

    private bool CanInteract()
    {
        return ignoreInteractable || button == null || button.IsInteractable();
    }

    private bool HitsAnyRect(Vector2 screenPoint)
    {
        if (button != null && Hits(button.transform as RectTransform, screenPoint)) return true;
        if (area != null && Hits(area, screenPoint)) return true;
        if (extraAreas != null)
        {
            for (int i = 0; i < extraAreas.Length; i++)
                if (Hits(extraAreas[i], screenPoint)) return true;
        }
        return false;
    }

    private bool Hits(RectTransform rect, Vector2 screenPoint)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy) return false;

        if (rect.rect.width <= 0f || rect.rect.height <= 0f)
        {
            if (!_warnedZero)
            {
                _warnedZero = true;
                Debug.LogWarning("DirectButton: \"" + rect.name + "\" is 0 x 0 in size, so it can never be tapped. " +
                                 "Make the object itself cover the clickable area, or assign a bigger RectTransform as Area.", rect);
            }
            return false;
        }
        return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, CanvasCamera(rect));
    }

    // Overlay canvases need no camera for the hit test; camera and world-space canvases do.
    private static Camera CanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;

        switch (canvas.renderMode)
        {
            case RenderMode.ScreenSpaceCamera:
                return canvas.worldCamera;   // none assigned = Unity draws it like an overlay, so no camera here either
            case RenderMode.WorldSpace:
                return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            default:
                return null;                 // Screen Space - Overlay
        }
    }

    private string TargetName()
    {
        if (button != null) return button.name;
        if (area != null) return area.name;
        return "(hotkey only)";
    }

    private string DescribeMainRect()
    {
        RectTransform r = button != null ? button.transform as RectTransform : area;
        if (r == null) return "nothing assigned";
        if (!r.gameObject.activeInHierarchy) return "the object is not active";

        r.GetWorldCorners(Corners);
        Camera cam = CanvasCamera(r);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
        return "its screen rectangle is " + a + " to " + b;
    }

    // Gathers every press that began this frame (mouse + all touches) into a shared list.
    private static void CollectPresses()
    {
        Presses.Clear();

#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            Presses.Add(mouse.position.ReadValue());

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].press.wasPressedThisFrame)
                    Presses.Add(touches[i].position.ReadValue());
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
            Presses.Add(Input.mousePosition);

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began)
                Presses.Add(t.position);
        }
#endif
    }
}

/// <summary>
/// Keyboard helper that works the same with the old Input Manager and the new Input System,
/// so scripts can keep using plain KeyCode fields.
/// </summary>
public static class KeyInput
{
    /// <summary>True on the frame the key went down.</summary>
    public static bool WasPressed(KeyCode key)
    {
        if (key == KeyCode.None) return false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        Key mapped;
        if (keyboard != null && TryMap(key, out mapped) && keyboard[mapped].wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(key)) return true;
#endif
        return false;
    }

    /// <summary>True while the player has a text input field selected and focused.</summary>
    public static bool IsTyping()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        GameObject selected = es.currentSelectedGameObject;
        if (selected == null) return false;

        TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
        if (tmp != null && tmp.isFocused) return true;

        InputField legacy = selected.GetComponent<InputField>();
        return legacy != null && legacy.isFocused;
    }

#if ENABLE_INPUT_SYSTEM
    // KeyCode and the new Input System's Key enum mostly share names; these are the ones that differ.
    private static bool TryMap(KeyCode code, out Key key)
    {
        string name = code.ToString();

        if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
        else if (name.StartsWith("Keypad")) name = "Numpad" + name.Substring(6);
        else
        {
            switch (name)
            {
                case "Return": name = "Enter"; break;
                case "LeftControl": name = "LeftCtrl"; break;
                case "RightControl": name = "RightCtrl"; break;
                case "BackQuote": name = "Backquote"; break;
            }
        }
        return System.Enum.TryParse(name, out key);
    }
#endif
}
