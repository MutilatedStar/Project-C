using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The diagonal "dynamic" split for SplitScreenCamera's Adaptive mode (the LEGO-games look). You don't set this one up
/// by hand: tick "Dynamic Adaptive" on SplitScreenCamera and it adds and drives this component itself.
///
/// How it works, and why it is its own script: ordinary split screens just shrink each camera's Viewport Rect, which
/// can only make straight horizontal or vertical splits. A slanted line needs another approach, so here
///   - both cameras draw into full-screen RenderTextures instead of the screen
///   - a canvas this script makes shows camera 1's picture over the whole screen, and camera 2's picture on top of it,
///     cut by a rotated mask (a plain UI Mask, so no custom shader and it works in any render pipeline)
///   - the cut is a straight line through the middle of the screen, at right angles to the line from Player 1 to
///     Player 2, so each player sits in the middle of their own half and the picture turns as they move round each other
/// While the players are close it shows one screen; the line slides in from the edge when they move apart, and slides
/// out again when they come back together or when one player is gone. It uses SplitScreenCamera's Split Distance,
/// Merge Distance and Transition Speed, and its follow speed.
///
/// Notes: orthographic cameras only. Draw your HUD with Screen Space - Overlay canvases (a Screen Space - Camera
/// canvas pointed at the game camera would be drawn inside the camera's texture, and cut by the split).
/// </summary>
[DisallowMultipleComponent]
public class DynamicScreenCamera : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Colour of the line between the two views.")]
    public Color dividerColor = Color.black;
    [Tooltip("Width of that line in screen pixels. 0 = no line.")]
    [Min(0f)] public float dividerThickness = 6f;
    [Tooltip("Where each player sits in their own view: how far from the middle of the screen towards its edge (0.5 = the middle of their half).")]
    [Range(0.2f, 0.8f)] public float playerPlacement = 0.5f;
    [Tooltip("How quickly the line turns when the players move round each other. Higher = quicker.")]
    public float turnSpeed = 6f;
    [Tooltip("Draw order of the canvas this script makes. Keep it BELOW your HUD canvases so the HUD stays on top.")]
    public int canvasSortingOrder = -1000;

    /// <summary>True while two views are showing right now (the line is on screen).</summary>
    public bool IsSplit { get { return running && blend > ShowThreshold; } }

    /// <summary>A point on the dividing line, in screen pixels (for placing HUD).</summary>
    public Vector2 LineCentre { get; private set; }

    /// <summary>Unit vector pointing from Player 1's side of the line towards Player 2's side.</summary>
    public Vector2 LineNormal { get; private set; }

    private const float ShowThreshold = 0.002f;
    private static readonly Rect FullRect = new Rect(0f, 0f, 1f, 1f);

    private SplitScreenCamera host;
    private bool running;
    private bool splitOpen;      // the split/merge decision (with its own hysteresis)
    private float blend;         // 0 = one screen, 1 = fully split; the line slides while it moves between the two
    private float angle;         // degrees: direction from Player 1 to Player 2

    private RenderTexture rt1;
    private RenderTexture rt2;
    private int texW;
    private int texH;

    private GameObject canvasObject;
    private RawImage viewA;
    private RawImage viewB;
    private RectTransform maskRect;
    private RectTransform dividerRect;
    private Image dividerImage;

    // ------------------------------------------------------------ called by SplitScreenCamera every frame

    public void Tick(SplitScreenCamera owner, bool both, bool has1, bool has2, Vector3 p1, Vector3 p2, bool snap)
    {
        host = owner;
        Camera cam1 = host.Camera1;

        if (!running)
        {
            running = true;
            snap = true;        // open already laid out, with no slide
            splitOpen = false;
            blend = 0f;
        }

        int w = Mathf.Max(1, Screen.width);
        int h = Mathf.Max(1, Screen.height);
        BuildUi();
        EnsureTextures(w, h);

        float halfH = host.BaseSize;
        float halfW = halfH * w / h;

        // ---- 1. One screen or two? (same thresholds as the normal Adaptive mode)
        Vector2 d = new Vector2(p2.x - p1.x, p2.y - p1.y);
        bool wasOpen = splitOpen;

        if (!both)
        {
            splitOpen = false;
        }
        else
        {
            float sep = Mathf.Max(Mathf.Abs(d.x) / (halfW * 2f), Mathf.Abs(d.y) / (halfH * 2f));
            if (!splitOpen) { if (sep > host.splitDistance) splitOpen = true; }
            else if (sep < Mathf.Min(host.mergeDistance, host.splitDistance)) splitOpen = false;
        }

        // ---- 2. Which way does the line run? It turns smoothly, and just snaps while it's out of sight.
        float targetAngle = (both && d.sqrMagnitude > 0.000001f) ? Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg : angle;
        float dt = Time.unscaledDeltaTime;

        if (snap || blend < 0.01f || (splitOpen && !wasOpen)) angle = targetAngle;
        else angle = Mathf.LerpAngle(angle, targetAngle, 1f - Mathf.Exp(-turnSpeed * dt));

        // ---- 3. Slide the line in or out.
        float k = snap ? 1f : 1f - Mathf.Exp(-host.transitionSpeed * dt);
        float goal = splitOpen ? 1f : 0f;
        blend = Mathf.Lerp(blend, goal, k);
        if (Mathf.Abs(blend - goal) < ShowThreshold) blend = goal;

        // ---- 4. Where does each camera look?
        float rad = angle * Mathf.Deg2Rad;
        Vector2 n = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));          // points from Player 1 towards Player 2
        float edgeWorld = DistanceToEdge(n, halfW, halfH);                // middle of the screen to its edge, along n
        float shift = edgeWorld * playerPlacement;
        Vector3 n3 = new Vector3(n.x, n.y, 0f);

        Vector3 own1 = has1 ? p1 : p2;                                    // Camera 1: Player 1, or Player 2 if Player 1 is gone
        Vector3 own2 = has2 ? p2 : p1;
        Vector3 merged = both ? (p1 + p2) * 0.5f : (has1 ? p1 : p2);

        // Split: each player sits `shift` back from the middle of the screen, on their own side of the line.
        Vector3 focus1 = Vector3.Lerp(merged, own1 + n3 * shift, blend);
        Vector3 focus2 = Vector3.Lerp(merged, own2 - n3 * shift, blend);

        // ---- 5. Cameras
        cam1.targetTexture = rt1;
        cam1.rect = FullRect;
        if (cam1.orthographic) cam1.orthographicSize = host.BaseSize;
        host.MoveCamera(cam1, focus1, snap);

        bool show2 = blend > ShowThreshold;
        Camera cam2 = show2 ? host.GetCamera2() : host.Camera2;
        if (cam2 != null)
        {
            cam2.targetTexture = rt2;
            cam2.rect = FullRect;
            if (cam2.orthographic) cam2.orthographicSize = host.BaseSize;

            bool wasOn = cam2.enabled;
            cam2.enabled = show2;
            host.MoveCamera(cam2, focus2, snap || !wasOn);   // a camera that was off jumps straight to its spot
        }

        // ---- 6. Canvas: camera 1's picture everywhere, camera 2's picture beyond the line
        float pixelsPerUnit = h / (2f * halfH);
        float edgePixels = edgeWorld * pixelsPerUnit;
        Vector2 centre = new Vector2(w * 0.5f, h * 0.5f);
        Vector2 line = centre + n * ((1f - blend) * edgePixels);          // at the screen edge when closed, middle when split
        LineCentre = line;
        LineNormal = n;

        float phi = Mathf.Atan2(n.y, n.x) * Mathf.Rad2Deg - 90f;          // turns the cut so its "up" points along n
        Quaternion rot = Quaternion.Euler(0f, 0f, phi);
        float big = (w + h) * 1.5f;                                       // plenty to reach every corner

        PlaceCentred(viewA.rectTransform, centre, Quaternion.identity, new Vector2(w, h));

        maskRect.gameObject.SetActive(show2);
        if (show2)
        {
            maskRect.SetPositionAndRotation(new Vector3(line.x, line.y, 0f), rot);
            maskRect.sizeDelta = new Vector2(big, big);                    // pivot is the bottom middle: it fills the +n side
            // Camera 2's picture is the child of the rotated mask, so stand it back upright over the whole screen.
            PlaceCentred(viewB.rectTransform, centre, Quaternion.identity, new Vector2(w, h));
        }

        bool showLine = show2 && dividerThickness > 0.01f;
        dividerRect.gameObject.SetActive(showLine);
        if (showLine)
        {
            dividerImage.color = dividerColor;
            PlaceCentred(dividerRect, line, rot, new Vector2(big, dividerThickness));
        }
    }

    // Puts everything back: cameras draw straight to the screen again and the canvas goes away.
    public void Release()
    {
        running = false;
        splitOpen = false;
        blend = 0f;

        if (host != null)
        {
            Camera cam1 = host.Camera1;
            Camera cam2 = host.Camera2;
            if (cam1 != null) { cam1.targetTexture = null; cam1.rect = FullRect; }
            if (cam2 != null) { cam2.targetTexture = null; cam2.enabled = false; }
        }

        DestroyTextures();
        if (canvasObject != null) canvasObject.SetActive(false);
    }

    private void OnDisable()
    {
        Release();
    }

    private void OnDestroy()
    {
        Release();
        if (canvasObject != null) Destroy(canvasObject);
    }

    // ------------------------------------------------------------ geometry

    // Distance from the middle of the screen to its border, going in direction n.
    private static float DistanceToEdge(Vector2 n, float halfW, float halfH)
    {
        float ax = Mathf.Abs(n.x);
        float ay = Mathf.Abs(n.y);
        float dx = ax > 0.0001f ? halfW / ax : float.MaxValue;
        float dy = ay > 0.0001f ? halfH / ay : float.MaxValue;
        return Mathf.Min(dx, dy);
    }

    private static void PlaceCentred(RectTransform rt, Vector2 position, Quaternion rotation, Vector2 size)
    {
        // World position/rotation: on an Overlay canvas with no scaler, one world unit is one screen pixel.
        rt.SetPositionAndRotation(new Vector3(position.x, position.y, 0f), rotation);
        rt.sizeDelta = size;
    }

    // ------------------------------------------------------------ textures + canvas

    private void EnsureTextures(int w, int h)
    {
        if (rt1 != null && rt2 != null && texW == w && texH == h) return;

        DestroyTextures();
        texW = w;
        texH = h;
        rt1 = NewTexture("Dynamic Split 1", w, h);
        rt2 = NewTexture("Dynamic Split 2", w, h);

        viewA.texture = rt1;
        viewB.texture = rt2;
    }

    private static RenderTexture NewTexture(string name, int w, int h)
    {
        RenderTexture rt = new RenderTexture(w, h, 24);   // 24-bit depth also carries the stencil that 2D masks use
        rt.name = name;
        rt.filterMode = FilterMode.Point;                 // 1 texture pixel = 1 screen pixel, so keep pixel art crisp
        return rt;
    }

    private void DestroyTextures()
    {
        // Cameras must let go before their texture is released.
        if (host != null)
        {
            if (host.Camera1 != null) host.Camera1.targetTexture = null;
            if (host.Camera2 != null) host.Camera2.targetTexture = null;
        }

        if (rt1 != null) { rt1.Release(); Destroy(rt1); rt1 = null; }
        if (rt2 != null) { rt2.Release(); Destroy(rt2); rt2 = null; }
        texW = 0;
        texH = 0;
    }

    private void BuildUi()
    {
        if (canvasObject != null)
        {
            if (!canvasObject.activeSelf) canvasObject.SetActive(true);
            return;
        }

        // No CanvasScaler and no GraphicRaycaster on purpose: one canvas unit is one screen pixel, and clicks pass
        // straight through to your HUD.
        canvasObject = new GameObject("Dynamic Split Screen", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        // Camera 1's picture, behind everything else.
        viewA = NewRawImage("View 1", canvasObject.transform);

        // The cut: a huge rectangle that only fills one side of the line. It is a UI Mask, so whatever is its child
        // (camera 2's picture) only shows inside it. showMaskGraphic is off: the rectangle itself is never drawn.
        GameObject maskObject = new GameObject("Cut", typeof(RectTransform), typeof(Image), typeof(Mask));
        maskObject.transform.SetParent(canvasObject.transform, false);
        maskRect = (RectTransform)maskObject.transform;
        maskRect.anchorMin = new Vector2(0.5f, 0.5f);
        maskRect.anchorMax = new Vector2(0.5f, 0.5f);
        maskRect.pivot = new Vector2(0.5f, 0f);
        Image maskImage = maskObject.GetComponent<Image>();
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;
        maskObject.GetComponent<Mask>().showMaskGraphic = false;

        // Camera 2's picture, inside the cut.
        viewB = NewRawImage("View 2", maskRect);

        // The line itself, on top.
        GameObject lineObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        lineObject.transform.SetParent(canvasObject.transform, false);
        dividerRect = (RectTransform)lineObject.transform;
        dividerRect.anchorMin = new Vector2(0.5f, 0.5f);
        dividerRect.anchorMax = new Vector2(0.5f, 0.5f);
        dividerRect.pivot = new Vector2(0.5f, 0.5f);
        dividerImage = lineObject.GetComponent<Image>();
        dividerImage.raycastTarget = false;
    }

    private static RawImage NewRawImage(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);

        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        RawImage image = go.GetComponent<RawImage>();
        image.raycastTarget = false;
        return image;
    }
}
