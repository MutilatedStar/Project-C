using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

// Three controls. Nothing changes until the Apply button is pressed.
//  1) Current label:  shows the resolution the game is running at right now
//  2) Preset button:  each click moves to the next preset (wraps around at the end)
//  3) Custom box:     type your own size, like 1280x720. Sizes are kept between 240 and 16384.
public class ResolutionMenu : MonoBehaviour
{
    [Header("1) Current resolution (display only)")]
    [SerializeField] TextRef currentLabel;
    [Tooltip("{0} = width, {1} = height")]
    [SerializeField] string currentFormat = "Current: {0} x {1}";

    [Header("2) Preset button (click = next preset)")]
    [SerializeField] Button presetButton;
    [SerializeField] TextRef presetLabel;
    [SerializeField] string presetFormat = "Preset: {0} x {1}";
    [Tooltip("Shown when the pending size isn't one of the presets.")]
    [SerializeField] string customPresetText = "Preset: Custom";
    [SerializeField] Vector2Int[] presets = new Vector2Int[]
    {
        new Vector2Int(800, 600),
        new Vector2Int(1024, 768),
        new Vector2Int(1280, 720),
        new Vector2Int(1366, 768),
        new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080),
        new Vector2Int(2560, 1440),
        new Vector2Int(3840, 2160),
    };

    [Header("3) Custom size box (type something like 1280x720)")]
    [SerializeField] InputRef customInput;

    // Needs a separator between the two numbers: x, X, a multiplication sign, *, a comma, or spaces.
    static readonly Regex SizePattern =
        new Regex(@"^([0-9]+)(?:\s*[xX\u00D7*,]\s*|\s+)([0-9]+)$");

    int presetIndex = -1; // -1 = pending size isn't a preset
    int shownW = -1;
    int shownH = -1;

    void OnEnable()
    {
        // Start from what is actually applied, so closing the menu without Apply discards changes
        GameSettings.PendingResWidth = GameSettings.ResWidth;
        GameSettings.PendingResHeight = GameSettings.ResHeight;
        presetIndex = FindPreset(GameSettings.PendingResWidth, GameSettings.PendingResHeight);

        if (presetButton != null) presetButton.onClick.AddListener(OnPresetButton);
        customInput.AddEndEditListener(OnCustomTyped);
        GameSettings.BeforeApply += CommitTyped;

        RefreshPending();
        RefreshCurrent();
    }

    void OnDisable()
    {
        if (presetButton != null) presetButton.onClick.RemoveListener(OnPresetButton);
        customInput.RemoveEndEditListener(OnCustomTyped);
        GameSettings.BeforeApply -= CommitTyped;
    }

    void Update()
    {
        // Screen size updates a frame or two after Apply, and also when the window is dragged
        if (Screen.width != shownW || Screen.height != shownH) RefreshCurrent();
    }

    void OnPresetButton()
    {
        if (presets == null || presets.Length == 0) return;

        presetIndex = (presetIndex + 1) % presets.Length; // -1 becomes 0, last wraps to first
        GameSettings.PendingResWidth = GameSettings.ClampRes(presets[presetIndex].x);
        GameSettings.PendingResHeight = GameSettings.ClampRes(presets[presetIndex].y);
        RefreshPending();
    }

    void OnCustomTyped(string raw)
    {
        int w, h;
        if (TryParseSize(raw, out w, out h))
        {
            GameSettings.PendingResWidth = w;
            GameSettings.PendingResHeight = h;
            presetIndex = FindPreset(w, h);
        }
        // Invalid text is ignored: the box goes back to showing the pending size
        RefreshPending();
    }

    // Runs right before Apply, in case the player is still typing in the box
    void CommitTyped()
    {
        OnCustomTyped(customInput.Value);
    }

    bool TryParseSize(string raw, out int w, out int h)
    {
        w = 0;
        h = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        Match m = SizePattern.Match(raw.Trim());
        if (!m.Success) return false;

        w = ParseDimension(m.Groups[1].Value);
        h = ParseDimension(m.Groups[2].Value);
        return true;
    }

    // Too small is raised to the minimum, too big is lowered to the maximum
    static int ParseDimension(string digits)
    {
        digits = digits.TrimStart('0');
        if (digits.Length == 0) return GameSettings.MinRes;
        if (digits.Length > 7) return GameSettings.MaxRes; // far too big to even fit in a number
        return GameSettings.ClampRes(int.Parse(digits, CultureInfo.InvariantCulture));
    }

    int FindPreset(int w, int h)
    {
        if (presets == null) return -1;
        for (int i = 0; i < presets.Length; i++)
            if (presets[i].x == w && presets[i].y == h) return i;
        return -1;
    }

    void RefreshPending()
    {
        int w = GameSettings.PendingResWidth;
        int h = GameSettings.PendingResHeight;

        presetLabel.Set(presetIndex >= 0 ? string.Format(presetFormat, w, h) : customPresetText);
        customInput.Value = w + "x" + h;
    }

    void RefreshCurrent()
    {
        shownW = Screen.width;
        shownH = Screen.height;
        currentLabel.Set(string.Format(currentFormat, shownW, shownH));
    }
}
