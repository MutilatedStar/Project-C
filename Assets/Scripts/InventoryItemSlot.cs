using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One item's spot in the inventory. Put it on a UI Image.
/// - The Image acts as a COVER. While the item is locked it is an opaque dark box.
///   When a CollectibleItem with the same Object Name is picked up, the cover turns fully transparent
///   (it still catches the mouse), so whatever sits behind it shows through.
/// - Optional: assign Name Text and it is filled with the item's name when found.
/// - Hovering OR clicking a found item shows a popup ABOVE it with its name and description
///   (click once to pin it open, click again to close it).
/// - A found item can be dragged over the UI (drawn on top of everything) and dropped on a suspect.
/// </summary>
public class InventoryItemSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
                                 IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Item")]
    [Tooltip("Must match the pickup's Object Name exactly (capitals don't matter).")]
    [SerializeField] private string itemName;
    [Tooltip("Optional. Shown in the popup instead of the description sent by the pickup.")]
    [SerializeField, TextArea] private string description;

    [Header("Cover")]
    [Tooltip("The Image that covers the item until it is found. Found automatically on this object if left empty.")]
    [SerializeField] private Image icon;
    [Tooltip("Optional. A TextMeshPro text that gets the item's name when found.")]
    [SerializeField] private TMP_Text nameText;
    [Tooltip("Cover colour while the item is still locked.")]
    [SerializeField] private Color coverColor = new Color(0.1f, 0.1f, 0.1f, 1f);
    [Tooltip("Cover colour once found. Alpha 0 = invisible, so what's behind it shows.")]
    [SerializeField] private Color foundColor = new Color(1f, 1f, 1f, 0f);

    [Header("Info popup (shown above the item)")]
    [SerializeField] private float popupWidth = 260f;
    [SerializeField] private float popupGap = 8f;
    [SerializeField] private Color popupBackground = new Color(0.08f, 0.08f, 0.1f, 0.95f);
    [SerializeField] private Color nameColor = Color.white;
    [SerializeField] private Color descriptionColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private float nameFontSize = 22f;
    [SerializeField] private float descriptionFontSize = 18f;
    [Tooltip("Optional. Leave empty to use TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Giving to suspects")]
    [Tooltip("Name of your suspect script. Anything with it (or with IItemReceiver) can be given items.")]
    [SerializeField] private string suspectScriptName = "Suspect";
    [Tooltip("Camera that looks at the suspects. Main Camera if left empty.")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private bool logDrops = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    private static InventoryItemSlot pinnedSlot;
    private static bool anyDragging;

    private InventoryItemData info;          // null while locked
    private System.Type suspectType;
    private bool hovered;
    private bool dragging;

    private GameObject ghost;
    private RectTransform ghostRect;
    private RectTransform ghostCanvasRect;

    private GameObject popup;
    private RectTransform popupRect;
    private RectTransform popupCanvasRect;
    private TextMeshProUGUI popupNameText;
    private TextMeshProUGUI popupBodyText;

    private void Log(string message)
    {
        if (debugLog) Debug.Log("[InventoryItemSlot '" + itemName + "'] " + message, this);
    }

    private void Awake()
    {
        if (icon == null) icon = GetComponent<Image>();
        if (icon == null) Debug.LogError("InventoryItemSlot '" + name + "': needs an Image on this object.", this);
        else icon.raycastTarget = true;   // a transparent Image still catches the mouse while this is on

        if (!string.IsNullOrEmpty(suspectScriptName))
        {
            System.Type t = System.Type.GetType(suspectScriptName);
            if (t != null && typeof(Component).IsAssignableFrom(t)) suspectType = t;
        }
    }

    private void Start()
    {
        if (EventSystem.current == null)
            Debug.LogError("InventoryItemSlot '" + name + "': no EventSystem in the scene (GameObject > UI > Event System).", this);

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            Debug.LogError("InventoryItemSlot '" + name + "': not inside a Canvas.", this);
        else if (canvas.rootCanvas.GetComponent<GraphicRaycaster>() == null)
            Debug.LogError("InventoryItemSlot '" + name + "': the canvas has no Graphic Raycaster.", this);
    }

    private void OnEnable()
    {
        InventoryState.ItemUnlocked += OnItemUnlocked;

        if (InventoryState.TryGet(itemName, out InventoryItemData found)) ApplyUnlocked(found);
        else ApplyLocked();
    }

    private void OnDisable()
    {
        InventoryState.ItemUnlocked -= OnItemUnlocked;

        hovered = false;
        if (pinnedSlot == this) pinnedSlot = null;
        if (dragging) anyDragging = false;
        dragging = false;

        HidePopup();
        DestroyGhost();
    }

    private void OnDestroy()
    {
        if (popup != null) Destroy(popup);
    }

    private void LateUpdate()
    {
        if (popup != null && popup.activeSelf) PositionPopup();
    }

    // ---------------------------------------------------------------- lock / unlock

    private void OnItemUnlocked(InventoryItemData item)
    {
        if (string.Equals(item.name, itemName, System.StringComparison.OrdinalIgnoreCase)) ApplyUnlocked(item);
    }

    private void ApplyUnlocked(InventoryItemData item)
    {
        info = item;
        if (icon != null) icon.color = foundColor;          // cover disappears, what's behind it shows
        if (nameText != null) nameText.text = item.name;
        Log("found. Description " + (string.IsNullOrEmpty(item.description) ? "is EMPTY on the pickup" : "received"));
    }

    private void ApplyLocked()
    {
        info = null;
        if (icon != null) icon.color = coverColor;
    }

    // ---------------------------------------------------------------- hover + click

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        Log("mouse entered (" + (info != null ? "found" : "still locked") + ")");
        RefreshPopup();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        RefreshPopup();
    }

    // Click pins the popup open; click again to close it.
    public void OnPointerClick(PointerEventData eventData)
    {
        Log("clicked (" + (info != null ? "found" : "still locked") + ")");
        if (info == null) return;

        InventoryItemSlot previous = pinnedSlot;
        pinnedSlot = (previous == this) ? null : this;

        if (previous != null && previous != this) previous.RefreshPopup();
        RefreshPopup();
    }

    private void RefreshPopup()
    {
        bool show = info != null && !anyDragging && (hovered || pinnedSlot == this);
        if (show) ShowPopup();
        else HidePopup();
    }

    private void ShowPopup()
    {
        EnsurePopup();
        if (popup == null) return;

        string body = string.IsNullOrEmpty(description) ? info.description : description;
        popupNameText.text = info.name;
        popupBodyText.text = string.IsNullOrEmpty(body) ? "No description." : body;

        popup.SetActive(true);
        popupRect.SetAsLastSibling();
        LayoutRebuilder.ForceRebuildLayoutImmediate(popupRect);
        PositionPopup();
        Log("popup shown");
    }

    private void HidePopup()
    {
        if (popup != null) popup.SetActive(false);
    }

    // Centered above the item; goes below instead if there's no room above.
    private void PositionPopup()
    {
        RectTransform slotRect = transform as RectTransform;
        if (slotRect == null || popupRect == null || popupCanvasRect == null) return;

        Vector3[] corners = new Vector3[4];
        slotRect.GetWorldCorners(corners);
        Vector3 top = popupCanvasRect.InverseTransformPoint((corners[1] + corners[2]) * 0.5f);
        Vector3 bottom = popupCanvasRect.InverseTransformPoint((corners[0] + corners[3]) * 0.5f);

        Rect bounds = popupCanvasRect.rect;
        Vector2 size = popupRect.rect.size;

        bool above = top.y + popupGap + size.y <= bounds.yMax;
        popupRect.pivot = new Vector2(0.5f, above ? 0f : 1f);

        float x = Mathf.Clamp(top.x, bounds.xMin + size.x * 0.5f, bounds.xMax - size.x * 0.5f);
        float y = above ? top.y + popupGap : bottom.y - popupGap;
        popupRect.localPosition = new Vector3(x, y, 0f);
    }

    private void EnsurePopup()
    {
        if (popup != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("InventoryItemSlot '" + name + "': can't build the popup, no Canvas above this object.", this);
            return;
        }
        canvas = canvas.rootCanvas;
        popupCanvasRect = (RectTransform)canvas.transform;

        popup = new GameObject("ItemInfoPopup",
            typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        popupRect = (RectTransform)popup.transform;
        popupRect.SetParent(canvas.transform, false);
        popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = new Vector2(popupWidth, 0f);

        Canvas popupCanvas = popup.GetComponent<Canvas>();
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = 1000;

        Image background = popup.GetComponent<Image>();
        background.color = popupBackground;
        background.raycastTarget = false;

        VerticalLayoutGroup layout = popup.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = popup.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        popupNameText = CreateText(popupRect, "Name", nameFontSize, FontStyles.Bold, nameColor, TextAlignmentOptions.TopLeft);
        popupBodyText = CreateText(popupRect, "Description", descriptionFontSize, FontStyles.Normal, descriptionColor, TextAlignmentOptions.TopLeft);

        popup.SetActive(false);
    }

    private TextMeshProUGUI CreateText(Transform parent, string objectName, float size, FontStyles style,
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

    // ---------------------------------------------------------------- dragging

    public void OnBeginDrag(PointerEventData eventData)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (info == null || icon == null || canvas == null)
        {
            eventData.pointerDrag = null;   // locked items can't be dragged
            return;
        }

        Log("drag started");
        dragging = true;
        anyDragging = true;

        if (pinnedSlot == this) pinnedSlot = null;
        HidePopup();

        canvas = canvas.rootCanvas;
        ghostCanvasRect = (RectTransform)canvas.transform;

        // A small card with the item's name follows the mouse; the slot stays put.
        // It has its own sorting override, so it is drawn ON TOP of every other UI object.
        ghost = new GameObject("ItemDragGhost", typeof(RectTransform), typeof(Canvas), typeof(Image));
        ghostRect = (RectTransform)ghost.transform;
        ghostRect.SetParent(canvas.transform, false);
        ghostRect.SetAsLastSibling();
        ghostRect.anchorMin = ghostRect.anchorMax = ghostRect.pivot = new Vector2(0.5f, 0.5f);

        Canvas ghostCanvas = ghost.GetComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingOrder = 2000;

        float scale = icon.rectTransform.lossyScale.x / canvas.transform.lossyScale.x;
        Vector2 size = icon.rectTransform.rect.size * scale;
        ghostRect.sizeDelta = new Vector2(Mathf.Max(size.x, 100f), Mathf.Max(size.y, 40f));

        Image ghostImage = ghost.GetComponent<Image>();
        ghostImage.color = new Color(0.22f, 0.22f, 0.28f, 0.9f);
        ghostImage.raycastTarget = false;   // so it never blocks what's under the mouse

        TextMeshProUGUI label = CreateText(ghostRect, "Name", 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 4f);
        labelRect.offsetMax = new Vector2(-4f, -4f);
        label.text = info.name;

        MoveGhost(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        MoveGhost(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        bool wasDragging = ghost != null;
        DestroyGhost();

        dragging = false;
        anyDragging = false;

        if (wasDragging && info != null) TryGive(eventData.position);
        RefreshPopup();
    }

    private void MoveGhost(PointerEventData eventData)
    {
        if (ghostRect == null) return;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                ghostCanvasRect, eventData.position, eventData.pressEventCamera, out Vector3 world))
        {
            ghostRect.position = world;
        }
    }

    private void DestroyGhost()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostRect = null;
    }

    // ---------------------------------------------------------------- giving to a suspect

    private void TryGive(Vector2 screenPosition)
    {
        if (!FindTarget(screenPosition, out GameObject target, out IItemReceiver receiver))
        {
            if (logDrops) Debug.Log("InventoryItemSlot: dropped '" + info.name + "' but no suspect was under the mouse.", this);
            return;
        }

        if (logDrops) Debug.Log("InventoryItemSlot: gave '" + info.name + "' to '" + target.name + "'.", target);

        if (receiver != null) receiver.ReceiveItem(info);
        InventoryState.Give(info, target);
    }

    private bool FindTarget(Vector2 screenPosition, out GameObject target, out IItemReceiver receiver)
    {
        target = null;
        receiver = null;

        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("InventoryItemSlot: no camera found. Tag your camera MainCamera or assign World Camera.", this);
            return false;
        }

        Ray ray = cam.ScreenPointToRay(screenPosition);

        foreach (RaycastHit2D hit in Physics2D.GetRayIntersectionAll(ray))
        {
            if (hit.collider != null && Resolve(hit.collider, out target, out receiver)) return true;
        }

        foreach (RaycastHit hit in Physics.RaycastAll(ray))
        {
            if (Resolve(hit.collider, out target, out receiver)) return true;
        }

        return false;
    }

    private bool Resolve(Component hitComponent, out GameObject target, out IItemReceiver receiver)
    {
        target = null;

        receiver = hitComponent.GetComponentInParent<IItemReceiver>();
        if (receiver != null)
        {
            target = ((Component)receiver).gameObject;
            return true;
        }

        if (suspectType != null)
        {
            Component suspect = hitComponent.GetComponentInParent(suspectType);
            if (suspect != null)
            {
                target = suspect.gameObject;
                return true;
            }
        }

        return false;
    }
}
