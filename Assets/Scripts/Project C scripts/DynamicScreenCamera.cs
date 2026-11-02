using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Render-texture compositor used by SplitScreenCamera for diagonal Adaptive splits and for Pixel Perfect Camera
/// compatibility. You normally don't set it up by hand: SplitScreenCamera adds and drives it automatically.
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
/// Pixel Perfect compatibility uses straight horizontal/vertical halves while retaining each camera's full viewport.
/// Notes: orthographic cameras only for diagonal Adaptive mode. Draw your HUD with Screen Space - Overlay canvases
/// (a Screen Space - Camera canvas pointed at the game camera would be drawn inside the camera's texture and cut).
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

    /// <summary>True when this component is compositing a straight split to accommodate Pixel Perfect Camera.</summary>
    public bool IsPixelPerfectComposite { get; private set; }

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
    private bool pixelPerfectVertical = true;

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
    private Image maskImage;
    private Mask stencilMask;
    private RectMask2D rectangularMask;

    // RenderTexture cameras do not count as cameras rendering to a display. This quiet output camera
    // keeps the target display active; the Screen Space Overlay compositor is drawn above it.
    private GameObject outputCameraObject;
    private Camera outputCamera;

    private struct CameraRenderState
    {
        public Camera camera;
        public RenderTexture targetTexture;
        public Rect rect;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public bool enabled;
        public bool valid;
    }

    private CameraRenderState camera1State;
    private CameraRenderState camera2State;

    // ------------------------------------------------------------ called by SplitScreenCamera every frame

    public void Tick(SplitScreenCamera owner, bool both, bool has1, bool has2, Vector3 p1, Vector3 p2, bool snap)
    {
        host = owner;
        IsPixelPerfectComposite = false;
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
        EnsureOutputCamera(cam1);
        EnsureTextures(w, h);

        if (stencilMask != null) stencilMask.enabled = true;
        if (rectangularMask != null) rectangularMask.enabled = false;
        if (maskImage != null) maskImage.enabled = true;
        maskRect.pivot = new Vector2(0.5f, 0f);

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
        PrepareCameraForTexture(cam1);
        cam1.targetTexture = rt1;
        cam1.rect = FullRect;
        if (cam1.orthographic && !host.UsesPixelPerfectCamera) cam1.orthographicSize = host.BaseSize;
        host.MoveCamera(cam1, focus1, snap);

        bool show2 = blend > ShowThreshold;
        Camera cam2 = show2 ? host.GetCamera2() : host.Camera2;
        if (cam2 != null)
        {
            PrepareCameraForTexture(cam2);
            cam2.targetTexture = rt2;
            cam2.rect = FullRect;
            if (cam2.orthographic && !host.UsesPixelPerfectCamera) cam2.orthographicSize = host.BaseSize;

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

    /// <summary>
    /// Rectangular split-screen path for Unity Pixel Perfect Camera. Each camera renders its full viewport
    /// to a full-resolution texture, so Pixel Perfect Camera can own its projection and render settings.
    /// A UI mask then shows only the correct half of each image. This avoids shrinking Camera.rect, which
    /// otherwise conflicts with Pixel Perfect Camera's resolution calculations.
    /// </summary>
    public void TickPixelPerfect(SplitScreenCamera owner, bool both, bool has1, bool has2, Vector3 p1, Vector3 p2, bool snap)
    {
        host = owner;
        Camera cam1 = host.Camera1;
        IsPixelPerfectComposite = true;

        if (!running)
        {
            running = true;
            snap = true;
            splitOpen = false;
            blend = 0f;
        }

        int w = Mathf.Max(1, Screen.width);
        int h = Mathf.Max(1, Screen.height);
        BuildUi();
        EnsureOutputCamera(cam1);
        EnsureTextures(w, h);

        // A RectMask2D is used for straight splits. Unlike a rotated stencil mask, its axis-aligned
        // clip rectangle cannot leak one camera's background across the divider.
        if (stencilMask != null) stencilMask.enabled = false;
        if (rectangularMask != null) rectangularMask.enabled = true;
        if (maskImage != null) maskImage.enabled = false;
        maskRect.pivot = new Vector2(0.5f, 0.5f);

        float halfH = cam1 != null && cam1.orthographic ? Mathf.Max(0.01f, cam1.orthographicSize) : host.BaseSize;
        float halfW = halfH * w / h;
        Vector2 d = new Vector2(p2.x - p1.x, p2.y - p1.y);
        bool wasOpen = splitOpen;
        bool vertical = pixelPerfectVertical;

        switch (host.mode)
        {
            case SplitScreenCamera.SplitMode.Horizontal:
                splitOpen = both;
                vertical = false;
                break;

            case SplitScreenCamera.SplitMode.Vertical:
                splitOpen = both;
                vertical = true;
                break;

            case SplitScreenCamera.SplitMode.Retro:
                splitOpen = false;
                break;

            default: // Adaptive, keeping the same split/merge hysteresis as the normal camera path.
                if (!both)
                {
                    splitOpen = false;
                }
                else
                {
                    float sepX = Mathf.Abs(d.x) / Mathf.Max(0.01f, halfW * 2f);
                    float sepY = Mathf.Abs(d.y) / Mathf.Max(0.01f, halfH * 2f);
                    float sep = Mathf.Max(sepX, sepY);
                    if (!splitOpen)
                    {
                        if (sep > host.splitDistance) splitOpen = true;
                    }
                    else if (sep < Mathf.Min(host.mergeDistance, host.splitDistance))
                    {
                        splitOpen = false;
                    }

                    if (splitOpen)
                    {
                        if (!wasOpen) vertical = sepX >= sepY;
                        else if (vertical && sepY > sepX * host.flipBias) vertical = false;
                        else if (!vertical && sepX > sepY * host.flipBias) vertical = true;
                    }
                }
                break;
        }

        pixelPerfectVertical = vertical;

        // Point the mask from Player 1 toward Player 2, but constrain it to a straight line.
        // This preserves player ownership if the characters cross one another.
        if (vertical)
            angle = d.x >= 0f ? 0f : 180f;
        else
            angle = d.y >= 0f ? 90f : -90f;

        float dt = Time.unscaledDeltaTime;
        float k = snap ? 1f : 1f - Mathf.Exp(-host.transitionSpeed * dt);
        blend = Mathf.Lerp(blend, splitOpen ? 1f : 0f, k);
        if (Mathf.Abs(blend - (splitOpen ? 1f : 0f)) < ShowThreshold) blend = splitOpen ? 1f : 0f;

        float rad = angle * Mathf.Deg2Rad;
        Vector2 n = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        float edgeWorld = vertical ? halfW : halfH;
        float shift = edgeWorld * playerPlacement;
        Vector3 n3 = new Vector3(n.x, n.y, 0f);

        Vector3 own1 = has1 ? p1 : p2;
        Vector3 own2 = has2 ? p2 : p1;
        Vector3 merged = both && host.mode != SplitScreenCamera.SplitMode.Retro
            ? (p1 + p2) * 0.5f
            : (has1 ? p1 : p2);
        Vector3 focus1 = Vector3.Lerp(merged, own1 + n3 * shift, blend);
        Vector3 focus2 = Vector3.Lerp(merged, own2 - n3 * shift, blend);

        // Keep each camera at a full viewport. Pixel Perfect Camera can now compute the right pixel grid
        // without camera.rect being changed to a half-size region.
        if (cam1 != null)
        {
            PrepareCameraForTexture(cam1);
            cam1.targetTexture = rt1;
            cam1.rect = FullRect;
            host.MoveCamera(cam1, focus1, snap);
        }

        bool show2 = blend > ShowThreshold;
        Camera cam2 = show2 ? host.GetCamera2() : host.Camera2;
        if (cam2 != null)
        {
            PrepareCameraForTexture(cam2);
            cam2.targetTexture = rt2;
            cam2.rect = FullRect;
            bool wasOn = cam2.enabled;
            cam2.enabled = show2;
            host.MoveCamera(cam2, focus2, snap || !wasOn);
        }

        // The divider follows the edge of the rectangular Player 2 clip.
        float pixelsPerUnit = h / (2f * halfH);
        float edgePixels = edgeWorld * pixelsPerUnit;
        Vector2 centre = new Vector2(w * 0.5f, h * 0.5f);
        Vector2 line = centre + n * ((1f - blend) * edgePixels);
        LineCentre = line;
        LineNormal = n;

        PlaceCentred(viewA.rectTransform, centre, Quaternion.identity, new Vector2(w, h));

        maskRect.gameObject.SetActive(show2);
        if (show2)
        {
            // Camera 2 only occupies the side of the split that belongs to Player 2. Clip its full-size
            // RawImage to that half instead of drawing a giant rotated rectangle over Camera 1's image.
            Rect clip;
            if (vertical)
            {
                clip = n.x >= 0f
                    ? new Rect(line.x, 0f, Mathf.Max(0f, w - line.x), h)
                    : new Rect(0f, 0f, Mathf.Max(0f, line.x), h);
            }
            else
            {
                clip = n.y >= 0f
                    ? new Rect(0f, line.y, w, Mathf.Max(0f, h - line.y))
                    : new Rect(0f, 0f, w, Mathf.Max(0f, line.y));
            }

            maskRect.SetPositionAndRotation(new Vector3(clip.center.x, clip.center.y, 0f), Quaternion.identity);
            maskRect.sizeDelta = clip.size;
            PlaceCentred(viewB.rectTransform, centre, Quaternion.identity, new Vector2(w, h));
        }

        bool showLine = show2 && dividerThickness > 0.01f;
        dividerRect.gameObject.SetActive(showLine);
        if (showLine)
        {
            dividerImage.color = dividerColor;
            PlaceCentred(dividerRect, line, Quaternion.identity,
                new Vector2(vertical ? dividerThickness : w, vertical ? h : dividerThickness));
        }
    }

    // Puts everything back: cameras draw straight to the screen again and the canvas goes away.
    public void Release()
    {
        running = false;
        splitOpen = false;
        blend = 0f;
        IsPixelPerfectComposite = false;

        // Release render targets before restoring each camera's pre-compositor settings.
        DestroyTextures();
        RestoreCameraState(ref camera1State);
        RestoreCameraState(ref camera2State);

        if (outputCamera != null) outputCamera.enabled = false;
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
        if (outputCameraObject != null) Destroy(outputCameraObject);
    }

    private void EnsureOutputCamera(Camera source)
    {
        if (outputCamera == null)
        {
            outputCameraObject = new GameObject("Split Screen Output Camera");
            outputCameraObject.transform.SetParent(transform, false);
            outputCamera = outputCameraObject.AddComponent<Camera>();
            outputCamera.enabled = false;
            outputCamera.cullingMask = 0;
            outputCamera.clearFlags = CameraClearFlags.SolidColor;
            outputCamera.orthographic = true;
            outputCamera.nearClipPlane = 0.1f;
            outputCamera.farClipPlane = 1f;
            outputCamera.allowHDR = false;
            outputCamera.allowMSAA = false;
            outputCamera.useOcclusionCulling = false;
        }

        // The actual player cameras render into textures during compositing, so neither is a display camera.
        // This empty camera keeps the display active while the overlay canvas presents the combined textures.
        outputCamera.targetTexture = null;
        outputCamera.targetDisplay = source != null ? source.targetDisplay : 0;
        Canvas compositeCanvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
        if (compositeCanvas != null) compositeCanvas.targetDisplay = outputCamera.targetDisplay;
        outputCamera.depth = source != null ? source.depth - 10000f : -10000f;
        outputCamera.backgroundColor = source != null ? source.backgroundColor : Color.black;
        Color opaque = outputCamera.backgroundColor;
        opaque.a = 1f;
        outputCamera.backgroundColor = opaque;
        outputCamera.enabled = true;
    }

    private void PrepareCameraForTexture(Camera cam)
    {
        if (cam == null) return;

        if (host != null && cam == host.Camera1 && (!camera1State.valid || camera1State.camera != cam))
            camera1State = CaptureCameraState(cam);
        else if (host != null && cam == host.Camera2 && (!camera2State.valid || camera2State.camera != cam))
            camera2State = CaptureCameraState(cam);

        // A depth-only / don't-clear camera can leave old texture pixels behind, and a transparent solid
        // background lets Camera 1 show through Camera 2. Render each view as an opaque, independently
        // cleared image while it is composited; RestoreCameraState returns the original settings later.
        if (cam.clearFlags == CameraClearFlags.Depth || cam.clearFlags == CameraClearFlags.Nothing)
            cam.clearFlags = CameraClearFlags.SolidColor;

        if (cam.clearFlags == CameraClearFlags.SolidColor && cam.backgroundColor.a < 0.999f)
        {
            Color opaque = cam.backgroundColor;
            opaque.a = 1f;
            cam.backgroundColor = opaque;
        }
    }

    private static CameraRenderState CaptureCameraState(Camera cam)
    {
        CameraRenderState state = new CameraRenderState();
        state.camera = cam;
        state.targetTexture = cam.targetTexture;
        state.rect = cam.rect;
        state.clearFlags = cam.clearFlags;
        state.backgroundColor = cam.backgroundColor;
        state.enabled = cam.enabled;
        state.valid = true;
        return state;
    }

    private static void RestoreCameraState(ref CameraRenderState state)
    {
        if (!state.valid || state.camera == null)
        {
            state.valid = false;
            return;
        }

        state.camera.targetTexture = state.targetTexture;
        state.camera.rect = state.rect;
        state.camera.clearFlags = state.clearFlags;
        state.camera.backgroundColor = state.backgroundColor;
        state.camera.enabled = state.enabled;
        state = new CameraRenderState();
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
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.name = name;
        rt.filterMode = FilterMode.Point;
        rt.wrapMode = TextureWrapMode.Clamp;
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.antiAliasing = 1;
        rt.Create();
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
        GameObject maskObject = new GameObject("Cut", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(RectMask2D));
        maskObject.transform.SetParent(canvasObject.transform, false);
        maskRect = (RectTransform)maskObject.transform;
        maskRect.anchorMin = new Vector2(0.5f, 0.5f);
        maskRect.anchorMax = new Vector2(0.5f, 0.5f);
        maskRect.pivot = new Vector2(0.5f, 0f);
        maskImage = maskObject.GetComponent<Image>();
        maskImage.color = Color.white;
        maskImage.raycastTarget = false;
        stencilMask = maskObject.GetComponent<Mask>();
        stencilMask.showMaskGraphic = false;
        rectangularMask = maskObject.GetComponent<RectMask2D>();
        rectangularMask.enabled = false;

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
