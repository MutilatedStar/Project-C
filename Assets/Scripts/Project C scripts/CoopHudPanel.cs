using System.Collections.Generic;
using UnityEngine;

/// <summary>Which family of HUD a panel belongs to. Each family is placed by its own layout component.</summary>
public enum HudGroup { Health, Inventory }

/// <summary>
/// The base of every per-player HUD piece (the health bars and the inventory bars). A panel belongs to one player,
/// lists itself here when it is switched on, and tells the layout script whether it is showing and how big it is.
/// CoopHealthbarLayout and CoopInventoryLayout (both built on CoopHudLayout) then place the panels of their family.
/// You never add this one itself: add CoopHealthbarBinder or CoopInventoryPanel.
/// </summary>
public abstract class CoopHudPanel : MonoBehaviour
{
    [Tooltip("Which player this panel shows.")]
    [Range(1, 4)] public int playerNumber = 1;
    [Tooltip("FOOTPRINT. Optional. The rectangle that counts as this panel's SIZE when CoopHealthbarLayout / CoopInventoryLayout place it " +
             "(Auto Snap uses it to keep the panel inside its view and clear of the dividing line; Mirror uses it to find the panel's centre). " +
             "Drag in the panel's background image if the bar's own rectangle isn't its real size. " +
             "Empty = the bar's own Rect Transform (health) / this object's Rect Transform (inventory). It is NOT needed for player assignment.")]
    public RectTransform footprint;

    /// <summary>Every panel that is switched on right now.</summary>
    public static readonly List<CoopHudPanel> All = new List<CoopHudPanel>();

    /// <summary>
    /// False when the panel sits on a Screen Space - Camera or World Space canvas. The layout places panels in SCREEN PIXELS, which
    /// only means something on a Screen Space - Overlay canvas, so it leaves such a panel where you put it instead of flinging it away.
    /// </summary>
    public bool OnOverlayCanvas { get; private set; }

    /// <summary>Which layout family places this panel.</summary>
    public abstract HudGroup Group { get; }

    /// <summary>True while the panel is on screen (its player is in the game). Hidden panels are left where they are.</summary>
    public abstract bool IsShown { get; }

    /// <summary>The rectangle to use when no Footprint is set.</summary>
    protected virtual RectTransform DefaultFootprint { get { return transform as RectTransform; } }

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

    protected virtual void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        CheckCanvas();
    }

    // Once per enable. The split-screen compositor draws both cameras into render textures and shows them on its own
    // Overlay canvas, so only an Overlay HUD canvas that sorts ABOVE it is guaranteed to be on screen and uncut.
    private void CheckCanvas()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        OnOverlayCanvas = root == null || root.renderMode == RenderMode.ScreenSpaceOverlay;

        if (root == null) return;

        if (!OnOverlayCanvas)
        {
            Debug.LogWarning(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' is " + root.renderMode +
                             ", not Screen Space - Overlay. With the split-screen cameras the world is drawn into render textures, so this HUD " +
                             "can be drawn inside them and cut by the other view, or vanish while the camera it points at is off. " +
                             "Set the canvas Render Mode to Screen Space - Overlay. Until then it is NOT moved by the layout.", this);
            return;
        }

        SplitScreenCamera rig = SplitScreenCamera.Current;
        int compositeOrder = rig != null ? rig.CompositeCanvasSortingOrder : DynamicScreenCamera.DefaultCanvasSortingOrder;
        if (root.sortingOrder <= compositeOrder)
        {
            Debug.LogWarning(name + " (Player " + playerNumber + "): its canvas '" + root.name + "' has Sort Order " + root.sortingOrder +
                             ", which is at or below the split-screen picture (" + compositeOrder + "), so the picture is drawn over it. " +
                             "Give the HUD canvas a higher Sort Order.", this);
        }
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
