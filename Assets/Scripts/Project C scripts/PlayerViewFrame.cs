using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A rectangle that always covers exactly ONE player's view of the split screen. Put a HUD element INSIDE it (as a child)
/// and anchor it to a corner with Unity's ordinary anchors, and it stays in that corner of that player's view however the
/// screen divides: one screen, side by side, one above the other, sliding open or shut, or swapping sides. There is no
/// layout maths to go wrong, because the camera tells this rectangle where the view is and Unity's anchors do the rest.
///
/// SET UP, once for each player:
///   1. In your HUD canvas (it must be Screen Space - Overlay) make an empty object: right-click the Canvas, Create Empty.
///      Name it "Player 1 View" (or "Player 2 View") and add this script to it. Set Player Number.
///   2. Tick Show Tint. Press Play. The frame is tinted, so you can SEE which part of the screen it covers. It should sit exactly on
///      that player's half (or the whole screen while there is only one view). Untick Show Tint when it looks right.
///   3. Drag that player's HUD pieces under it: the object with CoopHealthbarBinder and the object with CoopInventoryPanel.
///   4. Click each piece and, in its Rect Transform, open the Anchor Presets square and hold Shift + Alt while you click the corner
///      you want (top-left, bottom-right, ...). That sets its anchors, pivot and position together. Then nudge Pos X / Pos Y to
///      leave a gap from the edge. (Pos Y is negative to move down from a top corner.)
///
/// Anything inside a Player View Frame is left alone by CoopHealthbarLayout and CoopInventoryLayout, so you can use both: frames for
/// the pieces you place this way, the layouts for the rest. You can leave the layouts out of the scene altogether.
///
/// While the split line is slanted (Dynamic Adaptive) a rectangle can't describe a view, so the frame covers the straight half
/// nearest to the line. Needs SplitScreenCamera in the scene (without one, both frames cover the whole screen).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[DefaultExecutionOrder(200)]   // after SplitScreenCamera and the layouts have moved things this frame
public class PlayerViewFrame : MonoBehaviour
{
    [Tooltip("Whose view this rectangle covers.")]
    [Range(1, 2)] public int playerNumber = 1;
    [Tooltip("Tints the frame (blue for Player 1, orange for Player 2) so you can see which part of the screen it covers. For setting up: untick it when it looks right.")]
    public bool showTint = false;

    [Header("Debug (read only: filled in while you play in the Editor)")]
    [Tooltip("What this frame can see: the split-screen camera, the views, and the rectangle it was put on.")]
    [SerializeField, TextArea(3, 8)] private string status = "";

    private static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

    private Image tint;
    private bool addedTint;

#if UNITY_EDITOR
    private float nextStatus;
#endif

    private void LateUpdate()
    {
        RectTransform rt = transform as RectTransform;
        if (rt == null) return;

        SplitScreenCamera rig = SplitScreenCamera.Current;
        SplitViews views = rig != null ? rig.CurrentViews() : WholeScreen();
        Rect view = ViewFor(views);

        Place(rt, view);
        UpdateTint();
        ReportStatus(rt, rig, views, view);
    }

    // ------------------------------------------------------------ the view

    private static SplitViews WholeScreen()
    {
        SplitViews v = new SplitViews();
        v.view1 = new Rect(0f, 0f, Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        v.view2 = v.view1;
        return v;
    }

    private Rect ViewFor(SplitViews v)
    {
        if (!(v.two && v.slanted)) return playerNumber == 1 ? v.view1 : v.view2;

        // Slanted split: use the straight line nearest to it. Player 2 is on the side the line normal points to.
        float w = Mathf.Max(1, Screen.width);
        float h = Mathf.Max(1, Screen.height);
        float x = Mathf.Clamp(v.lineCentre.x, 0f, w);
        float y = Mathf.Clamp(v.lineCentre.y, 0f, h);

        if (Mathf.Abs(v.lineNormal.x) >= Mathf.Abs(v.lineNormal.y))
        {
            bool onRight = (v.lineNormal.x >= 0f) == (playerNumber == 2);
            return onRight ? new Rect(x, 0f, w - x, h) : new Rect(0f, 0f, x, h);
        }

        bool onTop = (v.lineNormal.y >= 0f) == (playerNumber == 2);
        return onTop ? new Rect(0f, y, w, h - y) : new Rect(0f, 0f, w, y);
    }

    // On an Overlay canvas, world positions are screen pixels. Anchors in the middle of the parent and a pivot at the bottom-left mean
    // the size is simply Size Delta and the position is simply where the bottom-left corner sits, whatever the canvas scale is.
    private static void Place(RectTransform rt, Rect view)
    {
        if (rt.anchorMin != Middle) rt.anchorMin = Middle;
        if (rt.anchorMax != Middle) rt.anchorMax = Middle;
        if (rt.pivot != Vector2.zero) rt.pivot = Vector2.zero;

        Vector3 scale = rt.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));

        rt.sizeDelta = new Vector2(view.width / sx, view.height / sy);
        rt.position = new Vector3(view.xMin, view.yMin, rt.position.z);
    }

    // ------------------------------------------------------------ set-up aids

    private void UpdateTint()
    {
        if (showTint)
        {
            if (tint == null)
            {
                tint = GetComponent<Image>();
                if (tint == null)
                {
                    tint = gameObject.AddComponent<Image>();
                    addedTint = true;
                }
            }

            tint.raycastTarget = false;
            tint.color = playerNumber == 1 ? new Color(0.2f, 0.6f, 1f, 0.25f) : new Color(1f, 0.55f, 0.2f, 0.25f);
            tint.enabled = true;
        }
        else if (tint != null && addedTint)
        {
            tint.enabled = false;
        }
    }

    private void ReportStatus(RectTransform rt, SplitScreenCamera rig, SplitViews views, Rect view)
    {
#if UNITY_EDITOR
        if (Time.unscaledTime < nextStatus) return;
        nextStatus = Time.unscaledTime + 0.25f;

        Canvas canvas = GetComponentInParent<Canvas>();
        Canvas root = canvas != null ? canvas.rootCanvas : null;
        string canvasText = root == null ? "NOT INSIDE A CANVAS"
                          : root.renderMode == RenderMode.ScreenSpaceOverlay ? "canvas '" + root.name + "': Screen Space - Overlay (good)"
                          : "canvas '" + root.name + "' is " + root.renderMode + " - it must be Screen Space - Overlay";

        string viewText = !views.two ? "one view" : (views.slanted ? "two views, slanted line (using the nearest straight half)" : "two views, straight line");

        status = "Player " + playerNumber + "   split-screen camera: " + (rig != null ? "found" : "NOT FOUND (whole screen)") +
                 "\nScreen: " + Screen.width + " x " + Screen.height + "   Now: " + viewText +
                 "\nThis frame covers: " + view +
                 "\nFrame is at: " + new Rect(rt.position.x, rt.position.y, rt.rect.width * rt.lossyScale.x, rt.rect.height * rt.lossyScale.y) +
                 "\n" + canvasText;
#endif
    }
}
