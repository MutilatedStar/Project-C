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
    [Tooltip("Optional. The rectangle that counts as this panel's size when it is placed (a bar's background, say). Empty = the bar's own Rect Transform / the panel's own Rect Transform.")]
    public RectTransform footprint;

    /// <summary>Every panel that is switched on right now.</summary>
    public static readonly List<CoopHudPanel> All = new List<CoopHudPanel>();

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
