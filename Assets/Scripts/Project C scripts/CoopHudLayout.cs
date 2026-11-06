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
///              split, a vertical split and the slanted Dynamic Adaptive split, with or without Pixel Perfect Camera, and
///              the panels glide along while the split opens, closes, swaps sides or turns.
///
/// These are three exclusive choices, so it is one three-way selector rather than three tick boxes that could
/// contradict each other. The optional button cycles through them.
///
/// What counts as a panel's size is measured by each panel itself (a health bar's own Rect Transform, an inventory
/// bar's own Rect Transform, or, for a bar whose rectangle is stretched across the screen, the pictures drawn in it).
/// You do not have to set a Footprint. Positions are in screen pixels, so use Screen Space - Overlay canvases for the
/// HUD (a panel on any other kind of canvas is repaired, or at least reported, by CoopHudPanel). A panel that isn't
/// showing (that player isn't in the game) is left alone until it appears. A panel placed INSIDE a PlayerViewFrame is
/// left alone too: that frame covers exactly its player's view, and the panel's own anchors keep it in its corner.
///
/// Safety: a panel is only ever sent to a spot that is a real number and near the screen, and it is moved to an ABSOLUTE spot
/// (target + the offset measured when it appeared), so a mismatched Footprint or a second script moving the same bar can no longer
/// make it run away to Infinity. If the measured panel does not follow the object being moved, one warning in the Console names both.
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

    [Header("Split screen")]
    [Tooltip("The two views can swap sides (the players walk past each other, or Keep Sides Fixed is off on the camera). ON: each panel then " +
             "mirrors with its view, so it stays on the same edge of the screen instead of ending up next to the dividing line. " +
             "OFF: a panel always keeps the same place inside its own view. Not used for the slanted split.")]
    public bool followSwappedViews = true;

    [Header("Debug (read only: filled in while you play in the Editor)")]
    [Tooltip("What this layout can see right now: the split-screen camera, the views, and for each player's panel whether it was found, whether it is showing, and where it is being sent.")]
    [SerializeField, TextArea(6, 12)] private string status = "";

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

        // How far the moved object's pivot sits from the panel's measured centre (screen pixels). It is measured when the panel
        // appears or changes size, and NOT every frame: re-measuring it every frame is what let a mismatched Footprint feed back
        // on itself and fling the bar to infinity.
        public Vector2 pivotOffset;
        public Vector2 lastHalf;
        public bool hasOffset;

        // For the "is the panel really following its slot?" check (reported once, as a warning).
        public Vector2 prevCentre;
        public Vector2 prevMove;
        public bool hasPrev;
        public int notFollowing;
        public bool warned;

        // For the Inspector read-out only.
        public string note = "not looked at yet";
        public Vector2 centre;         // where the panel is now (screen pixels)
        public Vector2 size;           // how big it measured
        public Vector2 target;         // where it is being sent
        public bool hasTarget;
        public bool hiddenNow;
    }

    private readonly BarState[] states = { new BarState(), new BarState() };
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
        float k = Mathf.Clamp01(1f - Mathf.Exp(-moveSpeed * Time.unscaledDeltaTime));   // 0..1 only: a negative speed must not push panels AWAY

        for (int i = 0; i < 2; i++) Layout(i, views, k);

        RefreshStatus(rig, views);
    }

    // ------------------------------------------------------------ Inspector read-out (Editor only)

#if UNITY_EDITOR
    private float nextStatus;
#endif

    private void RefreshStatus(SplitScreenCamera rig, SplitViews views)
    {
#if UNITY_EDITOR
        if (Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + 0.25f;

        string viewText = !views.two ? "one view" : (views.slanted ? "two views, slanted line" : "two views, straight line");
        status = "Mode: " + mode + "   Split-screen camera: " + (rig != null ? "found" : "NOT FOUND (whole screen is used)") +
                 "\nScreen: " + Screen.width + " x " + Screen.height + "   Now: " + viewText +
                 "\nView 1: " + views.view1 + "\nView 2: " + views.view2;

        for (int i = 0; i < 2; i++)
        {
            BarState st = states[i];
            status += "\nPlayer " + (i + 1) + ": " + st.note;
            if (st.hiddenNow && st.bar != null) status += "\n    why: " + st.bar.HiddenReason;
            if (st.hasTarget) status += "\n    panel at " + st.centre + "  size " + st.size + "  sent to " + st.target;
        }
#endif
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
            st.hasOffset = false;
            st.hasPrev = false;
            st.notFollowing = 0;
            st.warned = false;
        }
        st.hasTarget = false;
        st.hiddenNow = false;
        if (bar == null)
        {
            st.note = "NO PANEL found with this Player Number (" + Group + " family)";
            return;
        }

        // Screen pixels only mean something on an Overlay canvas. On any other canvas the panel stays where it was put.
        if (!bar.OnOverlayCanvas)
        {
            st.note = "its canvas is not Screen Space - Overlay, so it is left where you put it";
            return;
        }

        // Inside a Player View Frame, ordinary anchors keep it in its player's view. Nothing for this layout to do.
        if (bar.InViewFrame)
        {
            st.note = "sits inside a Player View Frame: placed by its anchors, not by this layout";
            return;
        }

        Transform slot = bar.SlotTransform;
        float limit = SaneLimit();

        // Safety net: if the panel has already been thrown off to nowhere (or to a position that is not even a number), put it
        // back where it was placed. There is nothing sensible to measure or chase out there.
        if (!IsSane(slot.position, limit))
        {
            Vector3 lost = slot.position;
            Vector3 home = st.captured ? WorldOf(slot, st.authored) : lost;
            if (st.captured && IsSane(home, limit)) slot.position = home;
            st.hasOffset = false;
            st.hasPrev = false;
            st.note = "the panel was far off the screen (" + lost + "): put back where it was placed";
            return;
        }

        Vector2 centre, half;
        GetFootprint(bar, out centre, out half);

        // A measurement that is not a real number (or is absurdly far away) can't be acted on. Skip the frame instead of handing
        // it to Unity: that is what used to fill the Console with "transform.position assign attempt ... is not valid".
        if (!IsSane(centre, limit) || !IsSane(half, limit))
        {
            st.hasPrev = false;
            st.note = "the panel's measured spot or size is not usable (centre " + centre + ", half size " + half + "): skipped";
            return;
        }

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
        if (!shown)
        {
            st.note = "panel found, but it is hidden";
            st.hiddenNow = true;
            return;
        }

        if (mode == LayoutMode.Free)
        {
            st.note = "Free mode: left where you put it";
            st.hasOffset = false;   // measured afresh when it is next placed
            st.hasPrev = false;
            LayoutFree(st, slot, centre, k, snap);
            return;
        }

        st.settledFree = false;
        Vector2 target = mode == LayoutMode.Mirror ? MirrorTarget(player, half, views) : AutoTarget(player, half, views);

        if (!IsSane(target, limit))
        {
            st.hasPrev = false;
            st.note = "the target spot is not usable (" + target + "): are the split-screen views set up yet?";
            return;
        }

        Vector3 here = slot.position;
        Vector2 now = new Vector2(here.x, here.y);

        // Measure how far the moved object's pivot sits from the panel's centre. Only when the panel has just appeared or changed
        // size: the offset between the two never changes while the panel is simply being moved.
        bool remeasure = snap || !st.hasOffset || (half - st.lastHalf).sqrMagnitude > 1f;

        // Is the measured panel really following the object we move? (Warns once if not; changes nothing.)
        if (!remeasure) CheckFollowing(st, bar, slot, centre);

        if (remeasure)
        {
            Vector2 fresh = now - centre;
            st.pivotOffset = IsSane(fresh, limit) ? fresh : Vector2.zero;
            st.lastHalf = half;
            st.hasOffset = true;
        }

        // Where the moved object's pivot has to be for the panel's centre to sit on the target. An absolute spot, not "the gap
        // measured this frame": the move can only ever converge on it, whatever the measurement does.
        Vector2 desired = target + st.pivotOffset;
        Vector2 next = snap ? desired : Vector2.Lerp(now, desired, k);

        Vector3 moved = new Vector3(next.x, next.y, here.z);
        bool apply = IsSane(moved, limit);   // never hand Unity a position it will refuse
        if (apply) slot.position = moved;

        st.prevCentre = centre;
        st.prevMove = apply ? next - now : Vector2.zero;
        st.hasPrev = true;

        st.note = "placing it (" + mode + ")";
        st.centre = centre;
        st.size = half * 2f;
        st.target = target;
        st.hasTarget = true;
    }

    // Diagnostics only. A panel is placed by moving its slot object, so the measured panel has to move by the same amount. When
    // it doesn't, the layout can't line it up, and the bar just ends up somewhere that looks wrong. Say why, once, by name.
    private void CheckFollowing(BarState st, CoopHudPanel bar, Transform slot, Vector2 centre)
    {
        if (!st.hasPrev || st.warned) return;

        float asked = st.prevMove.magnitude;
        if (asked < 8f) return;   // too small a move to tell

        Vector2 followed = centre - st.prevCentre;
        bool follows = (followed - st.prevMove).magnitude <= 0.5f * asked + 3f;
        st.notFollowing = follows ? 0 : st.notFollowing + 1;
        if (st.notFollowing < 5) return;

        st.warned = true;
        RectTransform foot = bar.Footprint;
        Debug.LogWarning(Group + " panel for Player " + (bar.playerNumber) + ": the layout moved '" + PathOf(slot) + "' by " + Mathf.RoundToInt(asked) +
                         " px, but the panel's measured size box (" + (foot != null ? "'" + PathOf(foot) + "'" : "the pictures drawn in the bar") +
                         ") moved " + Mathf.RoundToInt(followed.magnitude) + " px, so it can't be lined up. The Footprint must be INSIDE the object that is moved, and nothing else " +
                         "(a Layout Group, a follow-the-player script, a second Healthbar layout) may be positioning the same bar. " +
                         "This is only a warning: the bar can no longer run away because of it.", bar);
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

        if (!v.slanted)
        {
            // If the views have swapped sides, the corner swaps with them.
            bool flipX, flipY;
            FlipsFor(v, out flipX, out flipY);
            return CornerCentre(ViewOf(player, v), FlipCorner(wanted, flipX, flipY), half);
        }

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

        // If the views have swapped sides, the panel's place swaps with its view.
        bool flipX, flipY;
        FlipsFor(v, out flipX, out flipY);
        if (flipX) n.x = 1f - n.x;
        if (flipY) n.y = 1f - n.y;

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

    // Are the two views the other way round from usual? Usual: Player 1 on the left (side by side) or on top (one above the
    // other). The camera puts Player 1 on the right / bottom when the players have walked past each other, or when its Keep Sides
    // Fixed is off. Worked out from where the views really are, so it also holds while a split slides open, shut or swaps.
    private void FlipsFor(SplitViews v, out bool flipX, out bool flipY)
    {
        flipX = false;
        flipY = false;
        if (!followSwappedViews || !v.two || v.slanted) return;

        float dx = v.view2.center.x - v.view1.center.x;   // positive: Player 2's view is to the right of Player 1's
        float dy = v.view2.center.y - v.view1.center.y;   // positive: Player 2's view is above Player 1's

        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) flipX = dx < 0f;   // side by side, with Player 1 on the right
        else flipY = dy > 0f;                                  // one above the other, with Player 1 on the bottom
    }

    private static Corner FlipCorner(Corner corner, bool flipX, bool flipY)
    {
        bool right = IsRight(corner);
        bool top = IsTop(corner);
        if (flipX) right = !right;
        if (flipY) top = !top;

        if (top) return right ? Corner.TopRight : Corner.TopLeft;
        return right ? Corner.BottomRight : Corner.BottomLeft;
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

    // The bar's footprint in screen pixels: its centre and half size. The panel measures itself (see CoopHudPanel.GetBounds),
    // so nothing has to be assigned.
    private static void GetFootprint(CoopHudPanel bar, out Vector2 centre, out Vector2 half)
    {
        bar.GetBounds(out centre, out half);
    }

    // ------------------------------------------------------------ safety checks

    // Nothing on a screen is ever this far away. Far enough that a panel a person parked off to the side still passes, near
    // enough that a runaway value is caught long before it reaches Infinity.
    private static float SaneLimit()
    {
        return Mathf.Max(Screen.width, Screen.height, 100) * 10f;
    }

    private static bool IsFinite(float v)
    {
        return !float.IsNaN(v) && !float.IsInfinity(v);
    }

    private static bool IsSane(Vector2 v, float limit)
    {
        return IsFinite(v.x) && IsFinite(v.y) && Mathf.Abs(v.x) <= limit && Mathf.Abs(v.y) <= limit;
    }

    private static bool IsSane(Vector3 v, float limit)
    {
        return IsFinite(v.z) && Mathf.Abs(v.z) <= limit && IsSane(new Vector2(v.x, v.y), limit);
    }

    private static string PathOf(Transform t)
    {
        if (t == null) return "(none)";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
