using UnityEngine;

/// <summary>
/// Where one family of per-player HUD panels sits on screen (the health bars, or the inventory bars). You never add
/// this one itself: add CoopHealthbarLayout for the health bars and CoopInventoryLayout for the inventory bars, one of
/// each if you use both. They share the same three modes, so the two HUDs can be set up differently. Each finds its
/// panels by itself (every CoopHudPanel lists itself) and the split screen by itself (SplitScreenCamera), so there is
/// nothing to drag in. Choose how the panels are placed:
///
///   Free       The script leaves the panels alone. Put each one wherever you like in the scene, a different spot for
///              each player. Switch back to Free later and the panels glide back to the spot you gave them.
///   Mirror     Place Player 1's panel once (in Free, or in the scene); Player 2's panel is its mirror image. Each panel
///              keeps its place relative to its OWN view, so when the screen splits the pair splits with it.
///   Auto Snap  No placing at all: each panel snaps to a corner of its own view (health bars: Player 1 top-left, Player 2
///              top-right by default; inventory bars start at the bottom corners). It works for one screen, a horizontal
///              split, a vertical split and the slanted Dynamic Adaptive split, and the panels glide along while the
///              split opens, closes or turns.
///
/// These are three exclusive choices, so it is one three-way selector rather than three tick boxes that could
/// contradict each other. The optional button cycles through them.
///
/// What counts as a panel's size is its Footprint (a health bar's own Rect Transform, an inventory bar's own Rect
/// Transform, or the Footprint you set on it). Positions are in screen pixels, so use Screen Space - Overlay canvases
/// for the HUD. A panel that isn't showing (that player isn't in the game) is left alone until it appears.
/// </summary>
public abstract class CoopHudLayout : MonoBehaviour
{
    public enum LayoutMode { Free, Mirror, AutoSnap }
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }
    public enum MirrorAcross { LeftRight, TopBottom, Both }

    /// <summary>Which family of panels this layout places.</summary>
    protected abstract HudGroup Group { get; }

    /// <summary>The start of the button's label ("Health Bars", "Inventory").</summary>
    protected abstract string ButtonPrefix { get; }

    [Header("Mode")]
    [Tooltip("Free: the panels stay where you put them. Mirror: Player 2's panel is the mirror image of Player 1's. Auto Snap: each panel snaps to a corner of its own view.")]
    public LayoutMode mode = LayoutMode.AutoSnap;
    [Tooltip("Optional on-screen button that cycles through the three modes (tapped by code, leave OnClick empty).")]
    public DirectButton modeButton = new DirectButton();

    [Header("Auto Snap")]
    [Tooltip("The corner of its own view that Player 1's panel snaps to. If the dividing line cuts that corner off (slanted split), the nearest corner on Player 1's side is used instead.")]
    public Corner player1Corner = Corner.TopLeft;
    [Tooltip("The corner of its own view that Player 2's panel snaps to.")]
    public Corner player2Corner = Corner.TopRight;

    [Header("Mirror")]
    [Tooltip("LeftRight: Player 1's panel on the left becomes Player 2's on the right. TopBottom: top becomes bottom. Both: opposite corner.")]
    public MirrorAcross mirrorAcross = MirrorAcross.LeftRight;

    [Header("Auto Snap and Mirror")]
    [Tooltip("Gap in screen pixels between a panel and the edge of its view (Auto Snap).")]
    public Vector2 margin = new Vector2(24f, 24f);
    [Tooltip("How quickly the panels glide to their new spot when the screen changes. Higher = quicker.")]
    public float moveSpeed = 12f;

    private class BarState
    {
        public CoopHudPanel bar;
        public bool captured;
        public bool wasShown;
        public bool settledFree;
        public Vector3 authored;       // where you placed it: anchored position (or local position)
        public Vector2 authoredNorm;   // where its centre sat on the screen, as 0..1
    }

    private readonly BarState[] states = { new BarState(), new BarState() };
    private static readonly Vector3[] worldCorners = new Vector3[4];
    private LayoutMode shownMode = (LayoutMode)(-1);

    // ------------------------------------------------------------ mode (callable from UI buttons / dropdowns)

    public void CycleMode()
    {
        mode = (LayoutMode)(((int)mode + 1) % 3);
    }

    /// <summary>0 = Free, 1 = Mirror, 2 = Auto Snap.</summary>
    public void SetMode(int index)
    {
        mode = (LayoutMode)Mathf.Clamp(index, 0, 2);
    }

    private void Update()
    {
        if (modeButton.WasTapped()) CycleMode();

        if (shownMode != mode)
        {
            shownMode = mode;
            string label = mode == LayoutMode.AutoSnap ? "Auto Snap" : mode.ToString();
            modeButton.SetLabel(ButtonPrefix + ": " + label);
        }
    }

    // ------------------------------------------------------------ main loop

    private void LateUpdate()
    {
        SplitScreenCamera rig = SplitScreenCamera.Current;
        SplitViews views = rig != null ? rig.CurrentViews() : FullScreenViews();
        float k = 1f - Mathf.Exp(-moveSpeed * Time.unscaledDeltaTime);

        for (int i = 0; i < 2; i++) Layout(i, views, k);
    }

    private static SplitViews FullScreenViews()
    {
        SplitViews v = new SplitViews();
        v.view1 = new Rect(0f, 0f, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        v.view2 = v.view1;
        return v;
    }

    private void Layout(int index, SplitViews views, float k)
    {
        int player = index + 1;
        BarState st = states[index];

        CoopHudPanel bar = CoopHudPanel.Get(Group, player);
        if (bar != st.bar)
        {
            st.bar = bar;
            st.captured = false;
            st.wasShown = false;
            st.settledFree = false;
        }
        if (bar == null) return;

        Transform slot = bar.transform;
        Vector2 centre, half;
        GetFootprint(bar, out centre, out half);

        // First sight of this bar: remember where it was placed, before this script moves anything.
        if (!st.captured)
        {
            Capture(st, slot, centre);
            st.captured = true;
            st.settledFree = true;
        }

        bool shown = bar.IsShown;
        bool snap = shown && !st.wasShown;   // just appeared: go straight to its spot
        st.wasShown = shown;
        if (!shown) return;

        if (mode == LayoutMode.Free)
        {
            LayoutFree(st, slot, centre, k, snap);
            return;
        }

        st.settledFree = false;
        Vector2 target = mode == LayoutMode.Mirror ? MirrorTarget(player, half, views) : AutoTarget(player, half, views);

        Vector3 delta = new Vector3(target.x - centre.x, target.y - centre.y, 0f);
        slot.position += snap ? delta : delta * k;
    }

    // ------------------------------------------------------------ Free

    private void LayoutFree(BarState st, Transform slot, Vector2 centre, float k, bool snap)
    {
        // Resting in Free: keep up with wherever you drag it, so Mirror and the way back always use your latest spot.
        if (st.settledFree)
        {
            Capture(st, slot, centre);
            return;
        }

        // Coming back from Mirror / Auto Snap: glide home.
        Vector3 home = WorldOf(slot, st.authored);
        Vector3 delta = home - slot.position;
        if (snap || delta.sqrMagnitude < 0.25f)
        {
            slot.position = home;
            st.settledFree = true;
        }
        else
        {
            slot.position += delta * k;
        }
    }

    private static void Capture(BarState st, Transform slot, Vector2 centre)
    {
        RectTransform rect = slot as RectTransform;
        st.authored = rect != null ? (Vector3)rect.anchoredPosition : slot.localPosition;
        st.authoredNorm = new Vector2(centre.x / Mathf.Max(1, Screen.width), centre.y / Mathf.Max(1, Screen.height));
    }

    // The world (screen pixel) position a bar would have at an authored position.
    private static Vector3 WorldOf(Transform slot, Vector3 authored)
    {
        RectTransform rect = slot as RectTransform;
        if (rect != null)
        {
            Vector2 now = rect.anchoredPosition;
            rect.anchoredPosition = authored;
            Vector3 world = rect.position;
            rect.anchoredPosition = now;
            return world;
        }

        Vector3 nowLocal = slot.localPosition;
        slot.localPosition = authored;
        Vector3 worldFromLocal = slot.position;
        slot.localPosition = nowLocal;
        return worldFromLocal;
    }

    // ------------------------------------------------------------ Auto Snap

    private Vector2 AutoTarget(int player, Vector2 half, SplitViews v)
    {
        Corner wanted = player == 1 ? player1Corner : player2Corner;

        if (!v.slanted) return CornerCentre(ViewOf(player, v), wanted, half);

        // Slanted split: every view is a corner-to-corner half of the screen. Take the wanted corner if the dividing
        // line leaves it on this player's side, otherwise the corner that sits deepest on their side.
        Rect screen = FullScreen();
        Vector2 side = SideOf(player, v);

        Vector2 first = CornerCentre(screen, wanted, half);
        if (SideDistance(first, half, v.lineCentre, side) >= 0f) return first;

        Vector2 best = first;
        float bestScore = float.NegativeInfinity;
        for (int c = 0; c < 4; c++)
        {
            Corner corner = (Corner)c;
            Vector2 centre = CornerCentre(screen, corner, half);
            float score = SideDistance(centre, half, v.lineCentre, side);
            if (IsRight(corner) == IsRight(wanted)) score += 1f;   // a tie goes to the same left / right side
            if (score > bestScore)
            {
                bestScore = score;
                best = centre;
            }
        }
        return best;
    }

    private Vector2 CornerCentre(Rect view, Corner corner, Vector2 half)
    {
        float x = IsRight(corner) ? view.xMax - margin.x - half.x : view.xMin + margin.x + half.x;
        float y = IsTop(corner) ? view.yMax - margin.y - half.y : view.yMin + margin.y + half.y;
        return ClampInside(new Vector2(x, y), view, half);
    }

    private static bool IsRight(Corner c) { return c == Corner.TopRight || c == Corner.BottomRight; }
    private static bool IsTop(Corner c) { return c == Corner.TopLeft || c == Corner.TopRight; }

    // ------------------------------------------------------------ Mirror

    private Vector2 MirrorTarget(int player, Vector2 half, SplitViews v)
    {
        // Player 1's spot, as a fraction of the screen (a sensible default if Player 1's bar doesn't exist).
        BarState first = states[0];
        Vector2 n = first.captured ? first.authoredNorm : new Vector2(0.12f, 0.92f);

        if (player == 2)
        {
            if (mirrorAcross != MirrorAcross.TopBottom) n.x = 1f - n.x;
            if (mirrorAcross != MirrorAcross.LeftRight) n.y = 1f - n.y;
        }

        // The same fraction of the view this bar lives in.
        Rect view = v.slanted ? FullScreen() : ViewOf(player, v);
        Vector2 centre = new Vector2(view.xMin + n.x * view.width, view.yMin + n.y * view.height);
        centre = ClampInside(centre, view, half);

        if (v.slanted)
        {
            // Slanted split: if the line cuts the bar off, slide it back to its own side.
            Vector2 side = SideOf(player, v);
            float inside = SideDistance(centre, half, v.lineCentre, side);
            if (inside < 0f) centre = ClampInside(centre + side * (-inside + 1f), view, half);
        }
        return centre;
    }

    // ------------------------------------------------------------ geometry

    private static Rect ViewOf(int player, SplitViews v)
    {
        return player == 1 ? v.view1 : v.view2;
    }

    private static Rect FullScreen()
    {
        return new Rect(0f, 0f, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
    }

    // Unit vector pointing INTO this player's side of the slanted line.
    private static Vector2 SideOf(int player, SplitViews v)
    {
        return player == 1 ? new Vector2(-v.lineNormal.x, -v.lineNormal.y) : v.lineNormal;
    }

    // How far the bar's nearest corner is on this player's side of the line (negative = the line cuts through the bar).
    private static float SideDistance(Vector2 centre, Vector2 half, Vector2 lineCentre, Vector2 side)
    {
        float worst = float.MaxValue;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float px = centre.x + sx * half.x - lineCentre.x;
                float py = centre.y + sy * half.y - lineCentre.y;
                float d = px * side.x + py * side.y;
                if (d < worst) worst = d;
            }
        }
        return worst;
    }

    // Keeps a bar fully inside a view. If the view is smaller than the bar, it is centred instead.
    private static Vector2 ClampInside(Vector2 centre, Rect view, Vector2 half)
    {
        return new Vector2(ClampAxis(centre.x, view.xMin + half.x, view.xMax - half.x, view.center.x),
                           ClampAxis(centre.y, view.yMin + half.y, view.yMax - half.y, view.center.y));
    }

    private static float ClampAxis(float value, float min, float max, float middle)
    {
        if (min > max) return middle;
        return Mathf.Clamp(value, min, max);
    }

    // The bar's footprint in screen pixels: its centre and half size.
    private static void GetFootprint(CoopHudPanel bar, out Vector2 centre, out Vector2 half)
    {
        RectTransform rect = bar.Footprint;
        if (rect == null)
        {
            Vector3 p = bar.transform.position;
            centre = new Vector2(p.x, p.y);
            half = Vector2.zero;
            return;
        }

        rect.GetWorldCorners(worldCorners);
        float minX = worldCorners[0].x, maxX = minX, minY = worldCorners[0].y, maxY = minY;
        for (int i = 1; i < 4; i++)
        {
            minX = Mathf.Min(minX, worldCorners[i].x);
            maxX = Mathf.Max(maxX, worldCorners[i].x);
            minY = Mathf.Min(minY, worldCorners[i].y);
            maxY = Mathf.Max(maxY, worldCorners[i].y);
        }

        centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        half = new Vector2((maxX - minX) * 0.5f, (maxY - minY) * 0.5f);
    }
}
