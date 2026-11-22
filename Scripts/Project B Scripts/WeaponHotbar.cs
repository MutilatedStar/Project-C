using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The weapon bar at the bottom of the screen: one square per sword, side by side, built by this script.
///
///   Equipped sword     its square is in COLOUR.
///   Owned, not held    its square and sprite are in GREYSCALE.
///   Not found yet      an EMPTY dark box (with a faint number).
///   Switching          the new sword's name appears above the bar for a couple of seconds, then fades out.
///   Clicking a square  equips that sword (a locked one does nothing).
///
/// This script does NOT read the keyboard. SwordController already switches with Q / E (skipping swords the player
/// doesn't own) and with 1-9 (a locked sword's number does nothing), so doing it here too would switch twice.
/// The bar just watches SwordController and shows what it does, so it also reacts to chests, the shop, etc.
///
/// Setup:
///   1. On your HUD canvas (NOT inside the notebook canvas), make an empty UI object and add this script.
///      It is placed at the bottom centre when added; move it if you like.
///   2. Drag one sprite per sword into Icons, in the SAME ORDER as the Swords list on SwordController:
///      Icons[0] = first sword = key 1, Icons[1] = second sword = key 2, and so on.
///   3. The scene needs an EventSystem (GameObject > UI > Event System) for the clicks.
/// The sword names come from SwordController, so there is nothing to type here.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class WeaponHotbar : MonoBehaviour
{
    [Header("Weapons")]
    [Tooltip("The player's SwordController. Found automatically if left empty.")]
    [SerializeField] private SwordController swordController;
    [Tooltip("How many squares to draw. Match the number of swords on SwordController (keys 1-9 only reach the first nine).")]
    [SerializeField, Min(1)] private int slotCount = 6;
    [Tooltip("One sprite per sword, in the SAME ORDER as SwordController's Swords list. Used in colour for the equipped sword and as greyscale for the others (the greyscale copy is made for you).")]
    [SerializeField] private Sprite[] icons = new Sprite[6];

    [Header("Layout")]
    [SerializeField] private float slotSize = 72f;
    [SerializeField] private float spacing = 10f;
    [Tooltip("Space between the edge of a square and its sprite.")]
    [SerializeField] private float iconPadding = 8f;

    [Header("Colours")]
    [Tooltip("A sword the player hasn't found yet.")]
    [SerializeField] private Color emptyBoxColor = new Color(0.08f, 0.08f, 0.1f, 0.75f);
    [Tooltip("A sword the player owns but isn't holding.")]
    [SerializeField] private Color greyBoxColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
    [Tooltip("The sword the player is holding.")]
    [SerializeField] private Color selectedBoxColor = new Color(0.95f, 0.8f, 0.3f, 1f);
    [SerializeField] private Color numberColor = new Color(1f, 1f, 1f, 0.9f);
    [Tooltip("Show the key number on boxes that are still empty.")]
    [SerializeField] private bool showNumbersOnEmptySlots = true;

    [Header("Name label")]
    [Tooltip("Optional. Your own TextMeshPro text to show the sword's name in. Leave empty and one is made above the bar.")]
    [SerializeField] private TMP_Text nameLabel;
    [Tooltip("Seconds the name stays fully visible after switching.")]
    [SerializeField, Min(0f)] private float nameShowTime = 2f;
    [Tooltip("Seconds the name takes to fade away afterwards.")]
    [SerializeField, Min(0f)] private float nameFadeTime = 0.75f;
    [SerializeField] private float nameFontSize = 28f;
    [Tooltip("Space between the bar and the name.")]
    [SerializeField] private float nameGap = 14f;
    [Tooltip("Optional. Leave empty to use TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Animation (uses real time, so it still plays while the notebook has the game paused)")]
    [Tooltip("Seconds a square takes to change between grey and colour.")]
    [SerializeField, Min(0f)] private float colourTime = 0.15f;
    [Tooltip("A square pops once when its sword is found. Seconds the pop lasts.")]
    [SerializeField, Min(0.05f)] private float popTime = 0.4f;
    [Tooltip("How much bigger a square gets at the peak of the pop. 0.25 = 25%.")]
    [SerializeField, Min(0f)] private float popSize = 0.25f;

    private class Slot
    {
        public RectTransform rect;
        public Image background;
        public Image greyIcon;
        public Image colourIcon;
        public TMP_Text number;
        public bool hasIcon;
        public bool owned;
        public float amount;       // 0 = grey, 1 = colour
        public float popProgress = -1f;   // -1 = not popping
    }

    private readonly List<Slot> slots = new List<Slot>();
    private readonly List<Object> madeAssets = new List<Object>();   // greyscale textures and sprites to clean up

    private int shownIndex = -1;
    private bool synced;           // false until the first look at the controller, so nothing pops or flashes on load
    private float nameTimer;
    private float findTimer;

    // ---------------------------------------------------------------- setup

    // Runs in the editor when the script is added: park the bar at the bottom centre of the screen.
    private void Reset()
    {
        RectTransform rt = transform as RectTransform;
        if (rt == null) return;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 24f);
        rt.sizeDelta = new Vector2(100f, 10f);
    }

    private void Awake()
    {
        if (GetComponentInParent<Canvas>() == null)
            Debug.LogError("WeaponHotbar '" + name + "': not inside a Canvas. Put it on your HUD canvas.", this);

        BuildSlots();
        BuildNameLabel();
    }

    private void OnDestroy()
    {
        foreach (Object made in madeAssets)
        {
            if (made != null) Destroy(made);
        }
    }

    private void BuildSlots()
    {
        float step = slotSize + spacing;

        for (int i = 0; i < slotCount; i++)
        {
            Slot slot = new Slot();

            GameObject root = new GameObject("Weapon Slot " + (i + 1), typeof(RectTransform), typeof(Image), typeof(Button));
            slot.rect = (RectTransform)root.transform;
            slot.rect.SetParent(transform, false);
            slot.rect.anchorMin = slot.rect.anchorMax = new Vector2(0.5f, 0f);
            slot.rect.pivot = new Vector2(0.5f, 0.5f);
            slot.rect.sizeDelta = new Vector2(slotSize, slotSize);
            slot.rect.anchoredPosition = new Vector2((i - (slotCount - 1) * 0.5f) * step, slotSize * 0.5f);

            slot.background = root.GetComponent<Image>();
            slot.background.color = emptyBoxColor;
            slot.background.raycastTarget = true;

            // The square is a real button, but its colours are driven below, not by the Button's own transition.
            Button button = root.GetComponent<Button>();
            button.targetGraphic = slot.background;
            button.transition = Selectable.Transition.None;
            Navigation noNavigation = new Navigation();
            noNavigation.mode = Navigation.Mode.None;   // so arrow keys / Space never move or press it
            button.navigation = noNavigation;
            int index = i;
            button.onClick.AddListener(() => OnSlotClicked(index));

            // Two copies of the sprite on top of each other: grey underneath, colour fading in on top.
            Sprite colourSprite = (icons != null && i < icons.Length) ? icons[i] : null;
            slot.hasIcon = colourSprite != null;
            slot.greyIcon = CreateIcon(slot.rect, "Icon (grey)");
            slot.colourIcon = CreateIcon(slot.rect, "Icon (colour)");
            if (slot.hasIcon)
            {
                slot.colourIcon.sprite = colourSprite;
                slot.greyIcon.sprite = MakeGreyscale(colourSprite);
            }

            slot.number = CreateText(slot.rect, "Number", 18f, FontStyles.Bold, numberColor, TextAlignmentOptions.TopLeft);
            RectTransform numberRect = slot.number.rectTransform;
            numberRect.anchorMin = Vector2.zero;
            numberRect.anchorMax = Vector2.one;
            numberRect.offsetMin = new Vector2(6f, 4f);
            numberRect.offsetMax = new Vector2(-6f, -4f);
            slot.number.text = i < 9 ? (i + 1).ToString() : "";

            slots.Add(slot);
        }
    }

    private Image CreateIcon(RectTransform parent, string objectName)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(iconPadding, iconPadding);
        rt.offsetMax = new Vector2(-iconPadding, -iconPadding);

        Image image = go.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;   // only the square itself catches clicks
        image.enabled = false;
        return image;
    }

    private void BuildNameLabel()
    {
        if (nameLabel == null)
        {
            TextMeshProUGUI made = CreateText((RectTransform)transform, "Weapon Name", nameFontSize, FontStyles.Bold,
                                              Color.white, TextAlignmentOptions.Center);
            RectTransform rt = made.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(600f, nameFontSize * 1.6f);
            rt.anchoredPosition = new Vector2(0f, slotSize + nameGap);
            nameLabel = made;
        }

        nameLabel.raycastTarget = false;
        nameLabel.text = "";
        SetNameAlpha(0f);
    }

    private TextMeshProUGUI CreateText(RectTransform parent, string objectName, float size, FontStyles style,
                                       Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    // ---------------------------------------------------------------- greyscale copy of a sprite

    // Works even when the sprite's texture isn't set to Read/Write: the texture is copied through a RenderTexture first.
    private Sprite MakeGreyscale(Sprite source)
    {
        Texture2D sourceTexture = source.texture;
        Rect area = source.textureRect;
        int width = Mathf.RoundToInt(area.width);
        int height = Mathf.RoundToInt(area.height);

        RenderTexture previous = RenderTexture.active;
        RenderTexture temp = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0,
                                                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        Graphics.Blit(sourceTexture, temp);
        RenderTexture.active = temp;

        Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(area.x, area.y, width, height), 0, 0);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temp);

        Color32[] pixels = copy.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            byte grey = (byte)Mathf.Clamp(Mathf.RoundToInt(0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b), 0, 255);
            pixels[i] = new Color32(grey, grey, grey, pixels[i].a);
        }
        copy.SetPixels32(pixels);
        copy.Apply();
        copy.filterMode = sourceTexture.filterMode;   // keeps pixel art crisp
        copy.wrapMode = TextureWrapMode.Clamp;

        Vector2 pivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
        Sprite greySprite = Sprite.Create(copy, new Rect(0f, 0f, width, height), pivot, source.pixelsPerUnit);

        madeAssets.Add(copy);
        madeAssets.Add(greySprite);
        return greySprite;
    }

    // ---------------------------------------------------------------- every frame

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (swordController == null)
        {
            // The player may appear after this bar does (scene load, respawn), so keep looking.
            findTimer -= dt;
            if (findTimer <= 0f)
            {
                findTimer = 0.5f;
#if UNITY_2022_2_OR_NEWER
                swordController = FindFirstObjectByType<SwordController>();
#else
                swordController = FindObjectOfType<SwordController>();
#endif
                if (swordController != null)
                {
                    synced = false;
                    shownIndex = -1;
                }
            }
        }

        if (swordController != null && swordController.SwordCount > 0) ReadController();

        AnimateSlots(dt);
        AnimateName(dt);
    }

    // Looks at what the player owns and holds. Polling every frame is cheap (a handful of bools) and means the bar
    // is always right, whoever unlocked the sword: a chest, the shop, the debug box, anything.
    private void ReadController()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            bool owned = i < swordController.SwordCount && swordController.IsUnlocked(i);
            if (owned == slots[i].owned) continue;

            slots[i].owned = owned;
            if (owned && synced) slots[i].popProgress = 0f;   // just found: pop
        }

        int current = swordController.CurrentIndex;
        if (current != shownIndex)
        {
            shownIndex = current;
            if (synced) ShowName(swordController.CurrentSword.swordName);
        }

        synced = true;
    }

    private void AnimateSlots(float dt)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            Slot slot = slots[i];

            float target = (slot.owned && i == shownIndex) ? 1f : 0f;
            slot.amount = colourTime <= 0f ? target : Mathf.MoveTowards(slot.amount, target, dt / colourTime);

            slot.background.color = slot.owned ? Color.Lerp(greyBoxColor, selectedBoxColor, slot.amount) : emptyBoxColor;

            bool showIcon = slot.owned && slot.hasIcon;
            slot.greyIcon.enabled = showIcon;
            slot.colourIcon.enabled = showIcon && slot.amount > 0.001f;
            Color iconColour = slot.colourIcon.color;
            iconColour.a = slot.amount;
            slot.colourIcon.color = iconColour;

            slot.number.enabled = slot.owned || showNumbersOnEmptySlots;
            Color digit = numberColor;
            if (!slot.owned) digit.a *= 0.35f;
            slot.number.color = digit;

            if (slot.popProgress >= 0f)
            {
                slot.popProgress += dt / popTime;
                if (slot.popProgress >= 1f)
                {
                    slot.popProgress = -1f;
                    slot.rect.localScale = Vector3.one;
                }
                else
                {
                    slot.rect.localScale = Vector3.one * (1f + popSize * Mathf.Sin(slot.popProgress * Mathf.PI));
                }
            }
        }
    }

    // ---------------------------------------------------------------- name label

    private void ShowName(string swordName)
    {
        nameLabel.text = swordName;
        nameTimer = nameShowTime + nameFadeTime;
        SetNameAlpha(1f);
    }

    private void AnimateName(float dt)
    {
        if (nameTimer <= 0f) return;

        nameTimer = Mathf.Max(0f, nameTimer - dt);
        SetNameAlpha(nameFadeTime <= 0f ? (nameTimer > 0f ? 1f : 0f) : Mathf.Clamp01(nameTimer / nameFadeTime));
    }

    private void SetNameAlpha(float alpha)
    {
        Color c = nameLabel.color;
        c.a = alpha;
        nameLabel.color = c;
    }

    // ---------------------------------------------------------------- clicking

    private void OnSlotClicked(int index)
    {
        // Let go of the button, otherwise Space (the attack key) would "press" it again.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        if (swordController == null || FoeController.IsPaused) return;               // notebook open: no switching
        if (index >= swordController.SwordCount || !swordController.IsUnlocked(index)) return;   // empty box: nothing happens

        swordController.EquipSword(index);
    }
}
