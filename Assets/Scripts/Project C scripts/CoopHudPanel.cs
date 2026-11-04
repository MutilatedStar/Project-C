using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Which family of HUD a panel belongs to. Each family is placed by its own layout component.</summary>
public enum HudGroup { Health, Inventory }

/// <summary>
/// The base of every per-player HUD piece (the health bars and the inventory bars). A panel belongs to one player,
/// lists itself here when it is switched on, and tells the layout script whether it is showing and how big it is.
/// CoopHealthbarLayout and CoopInventoryLayout (both built on CoopHudLayout) then place the panels of their family.
/// You never add this one itself: add CoopHealthbarBinder or CoopInventoryPanel.
///
/// Works with the current split screen (SplitScreenCamera + DynamicScreenCamera, including the Pixel Perfect Camera
/// version). That draws both views into render textures on its own Screen Space - Overlay canvas, so a HUD canvas has to
/// be an Overlay canvas that sorts ABOVE that picture. This script checks that when the panel switches on and, unless you
/// turn Fix Canvas Automatically off, repairs the two usual mistakes itself and says so in the Console.
/// </summary>
public abstract class CoopHudPanel : MonoBehaviour
{
    [Tooltip("Which player this panel shows.")]
    [Range(1, 4)] public int playerNumber = 1;
    [Tooltip("FOOTPRINT. LEAVE THIS EMPTY. It is only the panel's SIZE, which the layout uses to keep the panel inside its view and " +
             "clear of the dividing line. When empty, the size is measured automatically: the bar's own rectangle, or, if that " +
             "rectangle is huge (a bar stretched across the whole canvas, say), the pictures and text drawn inside it. " +
             "Only drag something in if a bar still ends up in the wrong place: use the bar's background image, the picture that is " +
             "exactly as big as the bar looks. It is NOT needed for player assignment.")]
    public RectTransform footprint;
    [Tooltip("ON (recommended): if this panel's canvas would be hidden or cut up by the split screen, it is fixed while the game runs " +
             "and the Console says what changed. That covers a Screen Space - Camera canvas that one of the split cameras draws, and an " +
             "Overlay canvas whose Sort Order is at or below the split-screen picture. OFF: only a warning is printed. " +
             "Changes made while playing are not saved, so set the canvas up this way in the scene when you can.")]
    public bool fixCanvasAutomatically = true;

    /// <summary>Every panel that is switched on right now.</summary>
    public static readonly List<CoopHudPanel> All = new List<CoopHudPanel>();

    /// <summary>
    /// False when the panel sits on a Screen Space - Camera (with a camera) or World Space canvas. The layout places panels in SCREEN
    /// PIXELS, which only means something on a Screen Space - Overlay canvas, so it leaves such a panel where you put it instead of
    /// flinging it away.
    /// </summary>
    public bool OnOverlayCanvas { get; private set; }

    /// <summary>
    /// True when this panel sits inside a PlayerViewFrame. The frame covers exactly one player's view, so the panel is kept in its corner by
    /// ordinary Unity anchors, and the layout script leaves it alone.
    /// </summary>
    public bool InViewFrame { get; private set; }

    /// <summary>Which layout family places this panel.</summary>
    public abstract HudGroup Group { get; }

    /// <summary>True while the panel is on screen (its player is in the game). Hidden panels are left where they are.</summary>
    public abstract bool IsShown { get; }

    /// <summary>The rectangle to use when no Footprint is set.</summary>
    protected virtual RectTransform DefaultFootprint { get { return transform as RectTransform; } }

    /// <summary>Where to look for the pictures and text that make up the panel, if its rectangle turns out to be huge.</summary>
    protected virtual Transform MeasureRoot { get { return transform; } }

    /// <summary>The rectangle that counts as the panel's size.</summary>
    public RectTransform Footprint { get { return footprint != null ? footprint : DefaultFootprint; } }

    /// <summary>The panel of this family for this player, or null.</summary>
    public static CoopHudPanel Get(HudGroup group, int playerNumber)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i].Group == group && All[i].playerNumber == playerNumber) return All[i];
        }
        return null;
    }

    // ------------------------------------------------------------ size

    // A "bar" bigger than this share of the screen is not a bar: its rectangle is stretched, so measure what is drawn instead.
    private const float HugeFraction = 0.6f;

    private static readonly Vector3[] corners = new Vector3[4];
    private static readonly List<Graphic> graphics = new List<Graphic>();
    private bool warnedHuge;

    /// <summary>The panel's size in screen pixels: its centre and half size. This is what the layout uses.</summary>
    public void GetBounds(out Vector2 centre, out Vector2 half)
    {
        RectTransform rect = Footprint;
        if (rect == null)
        {
            Vector3 p = transform.position;
            centre = new Vector2(p.x, p.y);
            half = Vector2.zero;
            return;
        }

        Vector2 min, max;
        RectBounds(rect, out min, out max);

        // Nothing assigned and the rectangle is clearly not a bar: measure what is actually drawn inside it.
        if (footprint == null && IsHuge(max - min))
        {
            Vector2 drawnMin, drawnMax;
            if (MeasureDrawn(out drawnMin, out drawnMax))
            {
                min = drawnMin;
                max = drawnMax;
            }

            if (IsHuge(max - min) && !warnedHuge)
            {
                warnedHuge = true;
                Debug.LogWarning(name + " (Player " + playerNumber + "): this panel's rectangle is " + Mathf.RoundToInt(max.x - min.x) + " x " +
                                 Mathf.RoundToInt(max.y - min.y) + " pixels, which is most of the screen, so it can't be kept in its corner. " +
                                 "Shrink the bar's Rect Transform, or drag the bar's background image into Footprint.", this);
            }
        }

        centre = (min + max) * 0.5f;
        half = (max - min) * 0.5f;
    }

    private static bool IsHuge(Vector2 size)
    {
        return size.x > Screen.width * HugeFraction || size.y > Screen.height * HugeFraction;
    }

    private static void RectBounds(RectTransform rect, out Vector2 min, out Vector2 max)
    {
        rect.GetWorldCorners(corners);
        min = new Vector2(corners[0].x, corners[0].y);
        max = min;
        for (int i = 1; i < 4; i++)
        {
            min = Vector2.Min(min, new Vector2(corners[i].x, corners[i].y));
            max = Vector2.Max(max, new Vector2(corners[i].x, corners[i].y));
        }
    }

    // The area covered by the visible pictures and text under MeasureRoot. A picture that is itself huge is a backdrop, so it is skipped.
    private bool MeasureDrawn(out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);

        Transform root = MeasureRoot;
        if (root == null) return false;

        bool any = false;
        root.GetComponentsInChildren<Graphic>(false, graphics);
        for (int i = 0; i < graphics.Count; i++)
        {
            Graphic g = graphics[i];
            if (!g.enabled || g.color.a < 0.01f) continue;

            Vector2 gMin, gMax;
            RectBounds(g.rectTransform, out gMin, out gMax);
            if (IsHuge(gMax - gMin)) continue;

            min = Vector2.Min(min, gMin);
            max = Vector2.Max(max, gMax);
            any = true;
        }
        graphics.Clear();
        return any;
    }

    // ------------------------------------------------------------ canvas

    private bool checkedWithRig;

    protected virtual void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        InViewFrame = GetComponentInParent<PlayerViewFrame>() != null;
        CheckCanvas();
    }

    // Dragged into (or out of) a Player View Frame while the game is running.
    private void OnTransformParentChanged()
    {
        InViewFrame = GetComponentInParent<PlayerViewFrame>() != null;
    }

    // The split screen may switch on after this panel does (script order). Look again once everything has started.
    protected virtual void Start()
    {
        if (!checkedWithRig) CheckCanvas();
    }

    // Once per enable. The split-screen compositor draws both cameras into render textures and shows them on its own
    // Overlay canvas, so only an Overlay HUD canvas that sorts ABOVE it is guaranteed to be on screen and uncut.
    private void CheckCanvas()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        SplitScreenCamera rig = SplitScreenCamera.Current;
        checkedWithRig = rig != null;

        // A Screen Space - Camera canvas that one of the split cameras draws is drawn INTO that camera's picture, so the other
        // view cuts it off, and it vanishes while that camera is off. As an Overlay canvas it just works.
        if (root != null && root.renderMode == RenderMode.ScreenSpaceCamera && DrawnBySplitCamera(root, rig) && fixCanvasAutomatically)
        {
            root.renderMode = RenderMode.ScreenSpaceOverlay;
            Debug.Log(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' was Screen Space - Camera on a split-screen camera, " +
                      "which the split screen draws over and cuts up. It was switched to Screen Space - Overlay while the game runs. " +
                      "Set the canvas Render Mode to Screen Space - Overlay in the scene to keep this.", this);
        }

        // A Screen Space - Camera canvas with no camera assigned is drawn as an Overlay one by Unity.
        OnOverlayCanvas = root == null ||
                          root.renderMode == RenderMode.ScreenSpaceOverlay ||
                          (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera == null);

        if (root == null) return;

        if (!OnOverlayCanvas)
        {
            Debug.LogWarning(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' is " + root.renderMode +
                             ", not Screen Space - Overlay. With the split-screen cameras the world is drawn into render textures, so this HUD " +
                             "can be drawn inside them and cut by the other view, or vanish while the camera it points at is off. " +
                             "Set the canvas Render Mode to Screen Space - Overlay. Until then it is NOT moved by the layout.", this);
            return;
        }

        int compositeOrder = rig != null ? rig.CompositeCanvasSortingOrder : DynamicScreenCamera.DefaultCanvasSortingOrder;
        if (root.sortingOrder <= compositeOrder)
        {
            if (fixCanvasAutomatically)
            {
                int old = root.sortingOrder;
                root.sortingOrder = compositeOrder + 1;
                Debug.Log(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' had Sort Order " + old +
                          ", at or below the split-screen picture (" + compositeOrder + "), so the picture covered it. It was raised to " +
                          root.sortingOrder + " while the game runs. Give the canvas a higher Sort Order in the scene to keep this.", this);
            }
            else
            {
                Debug.LogWarning(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' has Sort Order " + root.sortingOrder +
                                 ", which is at or below the split-screen picture (" + compositeOrder + "), so the picture is drawn over it. " +
                                 "Give the HUD canvas a higher Sort Order.", this);
            }
        }
    }

    // True when this canvas is drawn by Camera 1 or Camera 2 of the split screen (or, before the split screen exists, by the main camera).
    private static bool DrawnBySplitCamera(Canvas root, SplitScreenCamera rig)
    {
        Camera cam = root.worldCamera;
        if (cam == null) return false;
        if (rig != null) return cam == rig.Camera1 || cam == rig.Camera2;
        return cam == Camera.main;
    }

    protected virtual void OnDisable()
    {
        All.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }
}
