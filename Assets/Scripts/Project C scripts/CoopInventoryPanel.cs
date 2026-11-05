using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One player's inventory bar: a row of sword slots (the held one is highlighted) with the player's running power-ups
/// as small labelled timers above it. It is built by this script when the player appears, so there are no slots to draw
/// by hand, and it shows only what THAT player owns (with Shared Weapons ON, what anybody owns).
///
/// SET UP, once for each player:
///   1. Make an empty object in a Screen Space - Overlay Canvas (Create Empty under the Canvas gives it a Rect Transform)
///      and put this script on it. Leave it at a normal size: the script sizes it to fit its slots.
///   2. Set Player Number (1 or 2).
///   3. Add ONE CoopInventoryLayout to the scene to place the bars (Free / Mirror / Auto Snap), like the health bars.
/// The object stays switched on; the script shows and hides the bar itself as the player comes and goes.
///
/// It needs the player's SwordController (the slots) and, for the timers, PlayerEffects (added automatically when the
/// player picks up a power-up). The slots show each sword's Sprite, or a coloured bar until it has one.
/// This is separate from InventoryItemSlot on purpose: that one is the detective game's mouse inventory (covers, popups,
/// dragging items to suspects). This one is the per-player weapon bar and never touches it.
/// </summary>
[DisallowMultipleComponent]
public class CoopInventoryPanel : CoopHudPanel
{
    [Header("Slots")]
    [Tooltip("Width and height of one sword slot, in canvas units.")]
    [Min(8f)] public float slotSize = 52f;
    [Tooltip("Gap between slots and between rows.")]
    [Min(0f)] public float spacing = 6f;
    [Tooltip("ON: swords the player doesn't own yet show as dark empty slots, so they can see what is still to find. OFF: only owned swords show.")]
    public bool showLockedSlots = true;
    public Color slotColor = new Color(0.1f, 0.1f, 0.14f, 0.85f);
    public Color selectedColor = new Color(0.95f, 0.8f, 0.25f, 0.95f);
    public Color lockedColor = new Color(0.05f, 0.05f, 0.07f, 0.55f);

    [Header("Power-up timers")]
    [Tooltip("Width and height of one timer square.")]
    [Min(8f)] public float chipSize = 30f;
    [Tooltip("Optional. Leave empty to use TextMeshPro's default font.")]
    public TMP_FontAsset font;
    [Min(6f)] public float chipFontSize = 11f;

    public override HudGroup Group { get { return HudGroup.Inventory; } }

    /// <summary>True while the bar is showing (that player is in the game).</summary>
    public override bool IsShown { get { return content != null && content.activeSelf; } }

    /// <summary>Why the bar is hidden: it only shows for a player that has a SwordController and counts.</summary>
    public override string HiddenReason
    {
        get
        {
            if (SwordController.Get(playerNumber) == null)
            {
                return "no SwordController for Player " + playerNumber + " is in the game (the player prefab needs one, with the matching Player Number).";
            }

            if (!CoopRules.Counts(playerNumber))
            {
                return "CoopRules says Player " + playerNumber + " does not count right now (Single Player Mode is on for the other player, " +
                       "or Player 2 is switched off in LocalCoopManager).";
            }

            return "shown normally";
        }
    }

    private class Slot
    {
        public GameObject root;
        public RectTransform rect;
        public Image background;
        public Image icon;
        public bool visible;
    }

    private class Chip
    {
        public GameObject root;
        public RectTransform rect;
        public Image background;
        public TextMeshProUGUI label;
        public Image bar;
    }

    private RectTransform panelRect;
    private GameObject content;
    private RectTransform contentRect;
    private SwordController controller;
    private PlayerEffects effects;
    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<Chip> chips = new List<Chip>();
    private Vector2 lastSize = new Vector2(-1f, -1f);

    private void Awake()
    {
        panelRect = transform as RectTransform;
        if (panelRect == null)
        {
            Debug.LogError("CoopInventoryPanel on '" + name + "': this object is not a UI object, so the bar can't be drawn (it needs a Rect Transform inside a Canvas). " +
                           "Fix: drag this object onto your HUD Canvas in the Hierarchy. Unity turns it into a UI object by itself. This script is switched off until you do.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        SwordController found = SwordController.Get(playerNumber);
        bool show = found != null && CoopRules.Counts(playerNumber);

        if (!show)
        {
            if (content != null && content.activeSelf) content.SetActive(false);
            return;
        }

        if (found != controller)
        {
            controller = found;
            effects = null;
            Build();
        }

        if (content == null) return;
        if (!content.activeSelf) content.SetActive(true);

        if (effects == null) effects = controller.GetComponent<PlayerEffects>();   // appears once the player first picks up a power-up

        Refresh();
    }

    // ------------------------------------------------------------ building

    private void Build()
    {
        // A new player (Player 2 respawned): throw the old bar away and make one for them.
        if (content != null) Destroy(content);
        slots.Clear();
        chips.Clear();
        lastSize = new Vector2(-1f, -1f);

        content = new GameObject("Inventory", typeof(RectTransform));
        contentRect = (RectTransform)content.transform;
        contentRect.SetParent(panelRect, false);
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        for (int i = 0; i < controller.SwordCount; i++) slots.Add(NewSlot(i));
    }

    private Slot NewSlot(int index)
    {
        Slot s = new Slot();
        s.root = new GameObject("Slot " + (index + 1), typeof(RectTransform), typeof(Image));
        s.rect = (RectTransform)s.root.transform;
        s.rect.SetParent(contentRect, false);
        AnchorBottomLeft(s.rect, new Vector2(slotSize, slotSize));

        s.background = s.root.GetComponent<Image>();
        s.background.raycastTarget = false;

        GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        RectTransform iconRect = (RectTransform)iconObject.transform;
        iconRect.SetParent(s.rect, false);
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;

        s.icon = iconObject.GetComponent<Image>();
        s.icon.raycastTarget = false;
        s.icon.preserveAspect = true;

        SwordController.SwordStats stats = controller.StatsAt(index);
        if (stats != null && stats.sprite != null)
        {
            s.icon.sprite = stats.sprite;
            s.icon.color = Color.white;
            iconRect.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.8f);
        }
        else
        {
            // No sprite yet: a coloured bar laid diagonally, like a sword.
            s.icon.sprite = SwordController.WhiteSquare;
            s.icon.color = stats != null ? stats.placeholderColor : Color.white;
            iconRect.sizeDelta = new Vector2(slotSize * 0.85f, slotSize * 0.16f);
            iconRect.localEulerAngles = new Vector3(0f, 0f, 45f);
        }
        return s;
    }

    private Chip NewChip()
    {
        Chip c = new Chip();
        c.root = new GameObject("Timer", typeof(RectTransform), typeof(Image));
        c.rect = (RectTransform)c.root.transform;
        c.rect.SetParent(contentRect, false);
        AnchorBottomLeft(c.rect, new Vector2(chipSize, chipSize));

        c.background = c.root.GetComponent<Image>();
        c.background.raycastTarget = false;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(c.rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(0f, 6f);   // keeps clear of the time bar along the bottom
        labelRect.offsetMax = Vector2.zero;

        c.label = labelObject.GetComponent<TextMeshProUGUI>();
        if (font != null) c.label.font = font;
        c.label.fontSize = chipFontSize;
        c.label.fontStyle = FontStyles.Bold;
        c.label.alignment = TextAlignmentOptions.Center;
        c.label.color = Color.white;
        c.label.raycastTarget = false;

        // The time bar: full when the power-up starts, empty when it runs out.
        GameObject barObject = new GameObject("Time", typeof(RectTransform), typeof(Image));
        RectTransform barRect = (RectTransform)barObject.transform;
        barRect.SetParent(c.rect, false);
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0f, 0f);
        barRect.sizeDelta = new Vector2(0f, 4f);
        barRect.anchoredPosition = Vector2.zero;

        c.bar = barObject.GetComponent<Image>();
        c.bar.raycastTarget = false;
        c.bar.sprite = SwordController.WhiteSquare;   // a filled Image needs a sprite
        c.bar.type = Image.Type.Filled;
        c.bar.fillMethod = Image.FillMethod.Horizontal;
        c.bar.color = Color.white;
        return c;
    }

    private static void AnchorBottomLeft(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.sizeDelta = size;
    }

    // ------------------------------------------------------------ every frame

    private void Refresh()
    {
        // Slots: a row along the bottom.
        int current = controller.CurrentIndex;
        int visible = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            bool owned = controller.IsUnlocked(i);
            bool show = owned || showLockedSlots;

            if (s.visible != show)
            {
                s.visible = show;
                s.root.SetActive(show);
            }
            if (!show) continue;

            s.rect.anchoredPosition = new Vector2(visible * (slotSize + spacing), 0f);
            s.background.color = !owned ? lockedColor : (i == current ? selectedColor : slotColor);
            s.icon.enabled = owned;
            visible++;
        }

        // Timers: a row above the slots, one square per running power-up.
        int running = effects != null ? effects.Active.Count : 0;
        float chipY = slotSize + spacing;
        for (int k = 0; k < running; k++)
        {
            while (chips.Count <= k) chips.Add(NewChip());

            Chip c = chips[k];
            PlayerEffects.ActiveEffect e = effects.Active[k];
            Color tint = PlayerEffects.ColorOf(e.kind);

            if (!c.root.activeSelf) c.root.SetActive(true);
            c.rect.anchoredPosition = new Vector2(k * (chipSize + spacing), chipY);
            c.background.color = new Color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, 0.9f);
            c.bar.color = tint;
            c.bar.fillAmount = e.duration > 0f ? Mathf.Clamp01(e.remaining / e.duration) : 0f;
            c.label.text = PlayerEffects.ShortName(e.kind);
        }
        for (int k = running; k < chips.Count; k++)
        {
            if (chips[k].root.activeSelf) chips[k].root.SetActive(false);
        }

        // The panel is always as big as the room reserved: the slot row plus one timer row. (Reserved even when no
        // timer is running, so the bar doesn't jump around when a power-up starts and the layout can place it once.)
        float width = Mathf.Max(visible * slotSize + Mathf.Max(0, visible - 1) * spacing, chipSize);
        float height = slotSize + spacing + chipSize;
        Vector2 size = new Vector2(width, height);
        if (size != lastSize)
        {
            lastSize = size;
            panelRect.sizeDelta = size;
        }
    }
}
