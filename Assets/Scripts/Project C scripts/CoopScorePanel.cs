using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ilumisoft.HealthSystem;

/// <summary>
/// One player's score on screen: a small plate with "P1  001250" on it. It is built by this script, so there is nothing to
/// draw by hand, and it reads CoopScore.Of(Player Number), so it follows the co-op rules by itself:
///   Shared Score OFF   each player's panel shows THEIR OWN points (two different scores)
///   Shared Score ON    both panels show the one team total, and the label changes to Shared Label (TEAM)
/// The panel shows only while that player is in the game and counts (the same rule as the health bar and the inventory bar).
///
/// SET UP, once for each player (exactly like CoopInventoryPanel):
///   1. Make an empty object in a Screen Space - Overlay Canvas (Create Empty under the Canvas gives it a Rect Transform)
///      and put this script on it. Leave it at a normal size: the script sizes it to fit the text.
///   2. Set Player Number (1 or 2).
///   3. Add ONE CoopScoreLayout to the scene to place the two panels (Free / Mirror / Auto Snap), like the health bars.
/// The object stays switched on; the script shows and hides the plate itself as the player comes and goes.
///
/// Needs CoopHudPanel and CoopHudLayout (the HUD snapping scripts), CoopRules.cs (CoopScore) and HudGroup.Score.
/// </summary>
[DisallowMultipleComponent]
public class CoopScorePanel : CoopHudPanel
{
    [Header("Text")]
    [Tooltip("Optional. Leave empty to use TextMeshPro's default font.")]
    public TMP_FontAsset font;
    [Min(8f)] public float fontSize = 36f;
    [Tooltip("Written in front of the number. {0} becomes the player number, so 'P{0}' reads P1 or P2. Leave empty for just the number.")]
    public string label = "P{0}";
    [Tooltip("Written instead of the label while Shared Score is ON, because then both players see the one team number.")]
    public string sharedLabel = "TEAM";
    [Tooltip("Room for this many digits is always reserved, so the plate doesn't change size every time the score gains a digit.")]
    [Range(1, 10)] public int reserveDigits = 6;
    [Tooltip("ON: pad with zeros up to Reserve Digits (arcade style: 000250). OFF: the plain number (250).")]
    public bool padWithZeros = false;

    [Header("Colours")]
    public Color numberColor = Color.white;
    [Tooltip("The label's colour for each player. Same colours as the gems and the player glow.")]
    public Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    public Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    public Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    public Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)
    [Tooltip("The label's colour while Shared Score is ON.")]
    public Color sharedColor = new Color(1f, 0.85f, 0.25f, 1f);

    [Header("Plate")]
    [Tooltip("ON: a dark plate behind the text, so it reads on any background.")]
    public bool showPlate = true;
    public Color plateColor = new Color(0.1f, 0.1f, 0.14f, 0.85f);
    [Tooltip("Space between the text and the edge of the plate, in canvas units.")]
    [Min(0f)] public float padding = 10f;

    public override HudGroup Group { get { return HudGroup.Score; } }

    /// <summary>True while the score is showing (that player is in the game).</summary>
    public override bool IsShown { get { return content != null && content.activeSelf; } }

    /// <summary>Why the score is hidden: it only shows for a player that has a CoopHealth and counts.</summary>
    public override string HiddenReason
    {
        get
        {
            if (CoopHealth.Get(playerNumber) == null)
            {
                return "no CoopHealth with Player Number " + playerNumber + " is in the game (the player prefab needs CoopHealth, " +
                       "and its PlayerInputSource Player Number must match).";
            }

            if (!CoopRules.Counts(playerNumber))
            {
                return "CoopRules says Player " + playerNumber + " does not count right now (Single Player Mode is on for the other player, " +
                       "or Player 2 is switched off in LocalCoopManager).";
            }

            return "shown normally";
        }
    }

    /// <summary>The score panel for this player number, or null.</summary>
    public static CoopScorePanel Get(int playerNumber)
    {
        return Get(HudGroup.Score, playerNumber) as CoopScorePanel;
    }

    private RectTransform panelRect;
    private GameObject content;
    private Image plate;
    private TextMeshProUGUI text;

    private int shownScore;
    private bool shownShared;
    private bool dirty = true;   // redraw on the next frame (first frame, or a setting was changed in the Inspector)
    private Vector2 lastSize = new Vector2(-1f, -1f);

    private void Awake()
    {
        panelRect = transform as RectTransform;
        if (panelRect == null)
        {
            Debug.LogError("CoopScorePanel on '" + name + "': this object is not a UI object, so the score can't be drawn (it needs a Rect Transform inside a Canvas). " +
                           "Fix: drag this object onto your HUD Canvas in the Hierarchy. Unity turns it into a UI object by itself. This script is switched off until you do.", this);
            enabled = false;
            return;
        }

        Build();
    }

    // A setting changed in the Inspector (also while playing): redraw on the next frame.
    private void OnValidate()
    {
        dirty = true;
    }

    // ------------------------------------------------------------ building

    private void Build()
    {
        content = new GameObject("Score", typeof(RectTransform), typeof(Image));
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.SetParent(panelRect, false);
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        plate = content.GetComponent<Image>();
        plate.raycastTarget = false;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(contentRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        text = textObject.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.richText = true;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        content.SetActive(false);   // Update switches it on once the player is in the game
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        bool show = CoopHealth.Get(playerNumber) != null && CoopRules.Counts(playerNumber);

        if (!show)
        {
            if (content.activeSelf) content.SetActive(false);
            return;
        }

        if (!content.activeSelf) content.SetActive(true);

        int score = CoopScore.Of(playerNumber);
        bool shared = CoopRules.SharedScore;
        if (!dirty && score == shownScore && shared == shownShared) return;

        dirty = false;
        shownScore = score;
        shownShared = shared;
        Redraw(score, shared);
    }

    private void Redraw(int score, bool shared)
    {
        string prefix = shared ? sharedLabel : (string.IsNullOrEmpty(label) ? "" : label.Replace("{0}", playerNumber.ToString()));
        Color tint = shared ? sharedColor : ColourOf(playerNumber);

        string front = string.IsNullOrEmpty(prefix) ? "" : "<color=#" + ColorUtility.ToHtmlStringRGB(tint) + ">" + prefix + "</color>  ";
        string number = padWithZeros ? score.ToString("D" + reserveDigits) : score.ToString();

        if (font != null) text.font = font;
        text.fontSize = fontSize;
        text.color = numberColor;
        text.text = front + number;

        plate.enabled = showPlate;
        plate.color = plateColor;

        // Size the panel to the text, always leaving room for Reserve Digits ('8' is about the widest digit), so the layout
        // has one steady size to place instead of a width that changes with every few points.
        int digits = Mathf.Max(reserveDigits, number.Length);
        Vector2 wanted = text.GetPreferredValues(front + new string('8', digits));
        Vector2 size = new Vector2(Mathf.Ceil(wanted.x) + 2f + padding * 2f, Mathf.Ceil(wanted.y) + padding * 2f);

        if (size != lastSize)
        {
            lastSize = size;
            panelRect.sizeDelta = size;
        }
    }

    private Color ColourOf(int number)
    {
        switch (number)
        {
            case 1: return player1Color;
            case 2: return player2Color;
            case 3: return player3Color;
            default: return player4Color;
        }
    }
}
