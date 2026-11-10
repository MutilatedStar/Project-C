using System;
using System.Reflection;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// Split-screen camera for local co-op. It replaces the old CameraFollow: put it on an empty object (or on the main
/// camera), and it drives Camera 1 for Player 1 and a second camera for Player 2. If Camera 2 is left empty it makes
/// one by itself, copying Camera 1.
///
/// Four modes (pick one in the Inspector, or change it while playing, or tap the optional on-screen button):
///   Horizontal  the dividing line runs left-right: Player 1 on the TOP half, Player 2 on the BOTTOM half
///   Vertical    the dividing line runs up-down: Player 1 on the LEFT half, Player 2 on the RIGHT half
///               (Horizontal and Vertical keep each player on their own half by default. Turn off "Keep Sides Fixed" and
///               they go by position instead, like Adaptive: the player further left / higher up gets the left / top half.)
///   Adaptive    one screen while the players are close. When they get far apart the screen splits, along whichever
///               direction they are further apart in, and the player who is further left (or higher up) gets the
///               left (or top) screen. The dividing line slides in and out instead of popping.
///               Tick "Dynamic Adaptive" and the split becomes a slanted, LEGO-style line that turns as the players
///               move round each other. That one is drawn by DynamicScreenCamera.cs (added automatically).
///   Retro       one screen that follows Player 1; Player 2 is not allowed to leave it (they stop at the edge)
///
/// ONE PLAYER: whenever a player is unassigned, switched off, destroyed, dead, or doesn't count (single player mode),
/// the screen slides back to a single view that follows whoever is left. When Player 2 comes back it splits again.
/// If nobody is left the camera just stays where it is.
///
/// Players are found by number through CoopHealth (so a Player 2 spawned from a prefab is picked up by itself).
/// The Player 1 / Player 2 slots are optional: drag a Transform in only to override that.
///
/// Needs CoopHealth.cs and CoopRules.cs (and DynamicScreenCamera.cs, which is only used by Dynamic Adaptive). Turn off or remove the old CameraFollow (this switches it off itself if it
/// is on the same camera). Draw the UI with a Screen Space - Overlay canvas so it covers both views.
/// </summary>
/// <summary>How the screen is divided right now, in screen pixels. CoopHealthbarLayout uses it to place the health bars.</summary>
public struct SplitViews
{
    /// <summary>Two views are showing (a dividing line is on screen).</summary>
    public bool two;
    /// <summary>The dividing line is slanted (Dynamic Adaptive): use lineCentre / lineNormal instead of the rectangles.</summary>
    public bool slanted;
    /// <summary>The area Player 1's view covers. The whole screen while only one view is showing.</summary>
    public Rect view1;
    /// <summary>The area Player 2's view covers. The whole screen while only one view is showing.</summary>
    public Rect view2;
    /// <summary>A point on the slanted line.</summary>
    public Vector2 lineCentre;
    /// <summary>Unit vector from Player 1's side of the slanted line to Player 2's side.</summary>
    public Vector2 lineNormal;
}

[DisallowMultipleComponent]
public class SplitScreenCamera : MonoBehaviour
{
    public enum SplitMode { Horizontal, Vertical, Adaptive, Retro }

    [Header("Mode")]
    public SplitMode mode = SplitMode.Adaptive;
    [Tooltip("Horizontal and Vertical only (Adaptive always goes by position, so this has no effect on it). ON: each player keeps their own half, Player 1 on the left / top and Player 2 on the right / bottom, however they move, so the views never swap when the players pass each other. OFF: the player who is further left (or higher up) gets the left (or top) half, and the halves swap when they cross.")]
    public bool staticSides = true;
    [Tooltip("Optional on-screen button that cycles through the modes (tapped by code, leave OnClick empty).")]
    public DirectButton modeButton = new DirectButton();
    [Tooltip("Adaptive mode only. ON: the split is a slanted line that turns with the players (drawn by DynamicScreenCamera, which is added automatically; orthographic cameras only). OFF: the straight horizontal / vertical split.")]
    public bool dynamicAdaptive = false;
    // No longer shown in the Inspector: it never needed linking. The DynamicScreenCamera on this object is found, or added
    // when it is first needed (see GetDynamicCamera). Switching that component off falls back to the straight split.
    [HideInInspector] public DynamicScreenCamera dynamicCamera;
    [Tooltip("Optional on-screen button that switches Dynamic Adaptive on and off (tapped by code, leave OnClick empty).")]
    public DirectButton dynamicButton = new DirectButton();

    [Header("Cameras")]
    [Tooltip("Player 1's camera (and the single-screen camera). Empty = the Camera on this object, or the Main Camera. It keeps the AudioListener.")]
    public Camera camera1;
    [Tooltip("Player 2's camera. Empty = one is created while it is needed, as a copy of Camera 1 (if you use URP post-processing, make this one yourself).")]
    public Camera camera2;
    [Tooltip("How much of the world one full screen shows (Orthographic Size). 0 = keep Camera 1's size as it is in the scene.")]
    public float orthoSize = 0f;
    [Tooltip("ON: the world looks the same size in a split view as on a single screen (a half-height view just shows less above and below). OFF: Unity's default, which shows a wider area at a smaller scale.")]
    public bool keepSameScale = true;

    [Header("Players (optional, empty = found automatically by player number)")]
    public Transform player1;
    public Transform player2;

    [Header("Following")]
    [Tooltip("How quickly the cameras catch up, same meaning as Lerp Speed on the old CameraFollow. Copy the value you used there.")]
    public float followSpeed = 5f;
    [Tooltip("If a view has to move further than this in one go (a floor change, a teleport) it jumps instead of sliding. 0 = always slide.")]
    public float snapDistance = 20f;
    [Tooltip("How fast the screens slide when the split opens, closes or changes direction. Higher = quicker.")]
    public float transitionSpeed = 8f;

    [Header("Adaptive")]
    [Tooltip("Split when the players are further apart than this, as a fraction of one full screen (0.7 = 70% of the screen's width or height).")]
    [Range(0.1f, 2f)] public float splitDistance = 0.7f;
    [Tooltip("Join back into one screen when they are closer than this. Keep it below Split Distance, so the screen doesn't flicker at the edge.")]
    [Range(0.05f, 2f)] public float mergeDistance = 0.5f;
    [Tooltip("How much further apart they must be in the other direction before the split turns 90 degrees. Stops it flipping when they are diagonal.")]
    [Range(1f, 3f)] public float flipBias = 1.25f;

    [Header("Retro")]
    [Tooltip("How far inside the screen edge (world units) Player 2 is held, so their sprite isn't cut in half.")]
    public float retroEdgePadding = 0.5f;

    [Header("Debug (read only: filled in while you play in the Editor)")]
    [Tooltip("Which camera code is running right now, and what the split is doing. Handy for working out which script a problem is in.")]
    [SerializeField, TextArea(2, 5)] private string activePath = "";

    /// <summary>True while two views are showing right now.</summary>
    public bool IsSplit
    {
        get
        {
            if (dynamicRunning && dynamicCamera != null) return dynamicCamera.IsSplit;
            return camera2 != null && camera2.enabled;
        }
    }

    /// <summary>
    /// True when each player keeps their own half no matter where they are: Horizontal or Vertical with Keep Sides Fixed
    /// on (the default). Adaptive is never fixed, because it goes by position. DynamicScreenCamera should read this too.
    /// </summary>
    public bool SidesFixed
    {
        get { return staticSides && (mode == SplitMode.Horizontal || mode == SplitMode.Vertical); }
    }

    /// <summary>The split screen in the scene (found by CoopHealthbarLayout), or null.</summary>
    public static SplitScreenCamera Current { get; private set; }

    /// <summary>
    /// Put both cameras straight onto their players on the next frame, with no glide. CoopDowned calls it when a player respawns at
    /// a spawn point that may be far from where they fell. Does nothing else: the split / merge layout still slides as usual.
    /// </summary>
    public void SnapToPlayers()
    {
        snapNextFrame = true;
    }

    /// <summary>How the screen is divided at this moment, with the slide animation included.</summary>
    public SplitViews CurrentViews()
    {
        float w = Mathf.Max(1, Screen.width);
        float h = Mathf.Max(1, Screen.height);

        SplitViews v = new SplitViews();
        v.view1 = new Rect(0f, 0f, w, h);
        v.view2 = v.view1;

        if (dynamicRunning && dynamicCamera != null)
        {
            if (dynamicCamera.IsSplit)
            {
                v.two = true;
                v.lineCentre = dynamicCamera.LineCentre;
                v.lineNormal = dynamicCamera.LineNormal;

                if (dynamicCamera.IsPixelPerfectComposite)
                {
                    // Report the visible rectangular portions rather than the cameras' full-size
                    // render textures. HUD layout code uses these rectangles in screen pixels.
                    float x = Mathf.Clamp(v.lineCentre.x, 0f, w);
                    float y = Mathf.Clamp(v.lineCentre.y, 0f, h);
                    if (Mathf.Abs(v.lineNormal.x) >= Mathf.Abs(v.lineNormal.y))
                    {
                        v.slanted = false;
                        if (v.lineNormal.x >= 0f)
                        {
                            v.view1 = new Rect(0f, 0f, x, h);
                            v.view2 = new Rect(x, 0f, w - x, h);
                        }
                        else
                        {
                            v.view1 = new Rect(x, 0f, w - x, h);
                            v.view2 = new Rect(0f, 0f, x, h);
                        }
                    }
                    else
                    {
                        v.slanted = false;
                        if (v.lineNormal.y >= 0f)
                        {
                            v.view1 = new Rect(0f, 0f, w, y);
                            v.view2 = new Rect(0f, y, w, h - y);
                        }
                        else
                        {
                            v.view1 = new Rect(0f, y, w, h - y);
                            v.view2 = new Rect(0f, 0f, w, y);
                        }
                    }
                }
                else
                {
                    v.slanted = true;
                }
            }
            return v;
        }

        if (camera2 != null && camera2.enabled)
        {
            v.two = true;
            v.view1 = ToPixels(camera1.rect, w, h);
            v.view2 = ToPixels(camera2.rect, w, h);
        }
        return v;
    }

    private static Rect ToPixels(Rect r, float w, float h)
    {
        return new Rect(r.x * w, r.y * h, r.width * w, r.height * h);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    // Used by DynamicScreenCamera, which shares these cameras.
    public Camera Camera1 { get { return camera1; } }
    public Camera Camera2 { get { return camera2; } }
    // Pixel Perfect Camera owns orthographicSize when present. Read its live result instead of the
    // cached scene value, because the package recalculates the projection for the current resolution.
    public float BaseSize
    {
        get
        {
            if (camera1 != null && camera1.orthographic && HasActivePixelPerfectCamera(camera1))
                return Mathf.Max(0.01f, camera1.orthographicSize);
            return baseSize;
        }
    }

    /// <summary>The sorting order of the canvas that shows the two views. HUD canvases must sort ABOVE it (CoopHudPanel checks this).</summary>
    public int CompositeCanvasSortingOrder
    {
        get { return dynamicCamera != null ? dynamicCamera.canvasSortingOrder : DynamicScreenCamera.DefaultCanvasSortingOrder; }
    }

    /// <summary>True when the source camera has an enabled Unity Pixel Perfect Camera component.</summary>
    public bool UsesPixelPerfectCamera
    {
        get { return camera1 != null && HasActivePixelPerfectCamera(camera1); }
    }

    /// <summary>Camera 2, made on the spot (a copy of Camera 1) if there isn't one yet.</summary>
    public Camera GetCamera2()
    {
        if (camera2 == null) CreateCamera2();
        return camera2;
    }

    private const float MinSize = 0.01f;   // a view thinner than this (of the screen) is switched off

    // The slide never goes slower than Transition Speed x this, in screens per second (8 x 0.2 = 1.6 screens/s by default).
    private const float MinSlideFraction = 0.2f;

    // Keep Sides Fixed OFF: how far past the other player (as a fraction of the screen's width or height) someone must go
    // before the two halves swap.
    private const float SwapMargin = 0.1f;

    private static readonly Rect FullRect = new Rect(0f, 0f, 1f, 1f);

    private Rect rect1 = new Rect(0f, 0f, 1f, 1f);
    private Rect rect2 = new Rect(1f, 0f, 0f, 1f);   // Camera 2 starts parked, thin, at the right edge
    private float mergeBlend = 1f;                   // 1 = both cameras look at the same spot, 0 = each at its own player

    private bool adaptiveMerged = true;              // Adaptive: what the screen is heading for (true = one screen)
    private bool wantSplit;                          // Adaptive: the players are far enough apart for two views
    private bool layoutChanging;                     // closing the old split so it can reopen the other way round (Adaptive, and Horizontal / Vertical when sides swap)
    private bool vertical = true;                    // true: side by side. false: one above the other
    private bool cam1First = true;                   // Camera 1 is on the left (side by side) or on top (stacked)

    private bool offsetKnown;
    private Vector3 offset;
    private float baseSize = 5f;
    private bool createdCamera2;
    private bool dynamicRunning;
    // True when DynamicScreenCamera is being used as a rectangular Pixel Perfect compositor rather
    // than the optional diagonal Adaptive renderer. This prevents transitions from reusing the wrong mode.
    private bool pixelPerfectCompositeRunning;
    private bool snapNextFrame;
    private bool shownDynamic;
    private SplitMode shownMode = (SplitMode)(-1);

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        if (camera1 == null) camera1 = GetComponent<Camera>();
        if (camera1 == null) camera1 = Camera.main;
        if (camera1 == null)
        {
            Debug.LogError("SplitScreenCamera: no Camera 1. Put this on the camera, or drag one into Camera 1. This script is switched off.", this);
            enabled = false;
            return;
        }

        baseSize = orthoSize > 0f ? orthoSize : (camera1.orthographic ? camera1.orthographicSize : 5f);

        SwitchOffOldFollow(camera1);
        camera1.rect = FullRect;

        if (camera2 != null && camera2 != camera1)
        {
            PrepareSecondaryCamera(camera2); // also copies Pixel Perfect Camera settings
        }
    }

    private void OnEnable()
    {
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
        if (dynamicRunning && dynamicCamera != null) dynamicCamera.Release();
        dynamicRunning = false;
        pixelPerfectCompositeRunning = false;

        // Leave the screen as it was: one full view.
        if (camera1 != null) camera1.rect = FullRect;
        if (camera2 != null) camera2.enabled = false;
    }

    private void OnDestroy()
    {
        if (createdCamera2 && camera2 != null) Destroy(camera2.gameObject);
    }

    private void OnValidate()
    {
        mergeDistance = Mathf.Min(mergeDistance, splitDistance);
    }

    // The old CameraFollow would fight this script for the camera, so it is switched off (found by name, so
    // this still compiles if you delete that script).
    private static void SwitchOffOldFollow(Camera cam)
    {
        foreach (MonoBehaviour mb in cam.GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.enabled && mb.GetType().Name == "CameraFollow")
            {
                mb.enabled = false;
                Debug.Log("SplitScreenCamera: switched off the old CameraFollow on '" + cam.name + "'.", cam);
            }
        }
    }

    // ------------------------------------------------------------ mode (callable from UI buttons / dropdowns)

    public void CycleMode()
    {
        mode = (SplitMode)(((int)mode + 1) % 4);
    }

    /// <summary>0 = Horizontal, 1 = Vertical, 2 = Adaptive, 3 = Retro.</summary>
    public void SetMode(int index)
    {
        mode = (SplitMode)Mathf.Clamp(index, 0, 3);
    }

    private void Update()
    {
        if (modeButton.WasTapped()) CycleMode();
        if (dynamicButton.WasTapped()) dynamicAdaptive = !dynamicAdaptive;

        if (shownMode != mode || shownDynamic != dynamicAdaptive)
        {
            shownMode = mode;
            shownDynamic = dynamicAdaptive;
            modeButton.SetLabel("Camera: " + mode + ((mode == SplitMode.Adaptive && dynamicAdaptive) ? " (diagonal)" : ""));
            dynamicButton.SetLabel("Diagonal Split: " + (dynamicAdaptive ? "On" : "Off"));
        }
    }

    // ------------------------------------------------------------ debug read-out

#if UNITY_EDITOR
    private float nextPathReport;
#endif

    // Editor only: shows in the Inspector which camera path is running (0 normal cameras, 1 Pixel Perfect composite, 2 diagonal).
    private void ReportPath(int path)
    {
#if UNITY_EDITOR
        if (Time.unscaledTime < nextPathReport) return;
        nextPathReport = Time.unscaledTime + 0.25f;

        string label = path == 2 ? "Diagonal compositor (Dynamic Adaptive, DynamicScreenCamera.Tick)"
                     : path == 1 ? "Pixel Perfect composite, straight halves (DynamicScreenCamera.TickPixelPerfect)"
                     : "Normal cameras, straight split (SplitScreenCamera, Camera.rect)";

        activePath = label + "\nMode: " + mode + "   Keep Sides Fixed in effect: " + SidesFixed +
                     "   Pixel Perfect Camera found: " + UsesPixelPerfectCamera;
        if (path == 1 && dynamicCamera != null) activePath += "\n" + dynamicCamera.DebugState;
#endif
    }

    // ------------------------------------------------------------ main loop

    private void LateUpdate()
    {
        // A duplicated camera object can briefly run its cloned controller before Awake disables it. Reassert this
        // active controller as the scene's current split-screen camera after that setup has settled.
        if (Current != this) Current = this;

        // Players have moved by now (their Update and the physics step both ran), so the cameras follow the final spot.
        Transform t1 = Resolve(1, player1);
        Transform t2 = Resolve(2, player2);
        bool has1 = t1 != null;
        bool has2 = t2 != null;
        if (!has1 && !has2) return;   // nobody left to show: hold still

        bool both = has1 && has2;
        Vector3 p1 = has1 ? t1.position : Vector3.zero;
        Vector3 p2 = has2 ? t2.position : Vector3.zero;

        // First frame: remember how the camera sat relative to the player (same as the old script's Start) and
        // skip every slide, so the game opens already laid out.
        bool first = !offsetKnown;
        if (first)
        {
            offset = camera1.transform.position - (has1 ? p1 : p2);
            offsetKnown = true;
        }

        // Pixel Perfect Camera expects to own a full viewport. The standard split path shrinks
        // Camera.rect, which makes the package recalculate against the wrong viewport. Render-texture
        // compositing keeps each Pixel Perfect Camera full-screen and masks the result into the halves.
        bool useDynamic = WantsDynamic();
        bool usePixelPerfectComposite = UsesPixelPerfectCamera && !useDynamic;
        ReportPath(useDynamic ? 2 : (usePixelPerfectComposite ? 1 : 0));

        // If changing compositor modes (or returning to ordinary cameras), release the prior render
        // textures and reset camera state before the next path takes ownership.
        if (dynamicRunning && (pixelPerfectCompositeRunning != usePixelPerfectComposite)) LeaveDynamic();
        else if (dynamicRunning && !useDynamic && !usePixelPerfectComposite) LeaveDynamic();

        bool snap = first || snapNextFrame;
        snapNextFrame = false;

        if (useDynamic || usePixelPerfectComposite)
        {
            bool entering = !dynamicRunning;
            dynamicRunning = true;
            pixelPerfectCompositeRunning = usePixelPerfectComposite;
            GetDynamicCamera();

            if (useDynamic)
            {
                dynamicCamera.Tick(this, both, has1, has2, p1, p2, snap || entering);
            }
            else
            {
                dynamicCamera.TickPixelPerfect(this, both, has1, has2, p1, p2, snap || entering);
                // Keep Retro mode's existing rule: Player 2 cannot leave Camera 1's visible area.
                if (mode == SplitMode.Retro && both) ClampToScreen(t2);
            }
            return;
        }

        // ---- 1. Which layout do we want? (merged = one screen)
        bool merged;
        switch (mode)
        {
            case SplitMode.Horizontal:
            case SplitMode.Vertical:
                ResetAdaptive();
                merged = UpdateFixedSplit(mode == SplitMode.Vertical, both, p1, p2);
                break;

            case SplitMode.Retro:
                merged = true;
                ResetAdaptive();
                break;

            default:   // Adaptive
                if (both) UpdateAdaptive(p1, p2);
                else ResetAdaptive();   // when Player 2 joins, start from one screen
                merged = adaptiveMerged;
                break;
        }

        // ---- 2. Slide the screens towards it.
        Rect target1, target2, closed1, parked2;

        // A hidden Camera 2 waits on the edge of the CURRENT direction, so when it opens it grows out of the right
        // edge instead of morphing in from the wrong one.
        LayoutTargets(true, out closed1, out parked2);
        if (rect2.width < MinSize || rect2.height < MinSize) rect2 = parked2;

        LayoutTargets(merged, out target1, out target2);

        // Eases out like before, but never slower than minStep, so the last bit of the slide can't crawl and the
        // dividing line is really gone a moment after the players come together (not after a long fade-out tail).
        float dt = Time.unscaledDeltaTime;
        float k = snap ? 1f : 1f - Mathf.Exp(-transitionSpeed * dt);
        float minStep = snap ? 1f : transitionSpeed * MinSlideFraction * dt;
        rect1 = SlideRect(rect1, target1, k, minStep);
        rect2 = SlideRect(rect2, target2, k, minStep);
        mergeBlend = Slide(mergeBlend, merged ? 1f : 0f, k, minStep);

        // ---- 3. Where does each camera look?
        Vector3 own1 = has1 ? p1 : p2;   // Camera 1 follows Player 1, or Player 2 if Player 1 is gone
        Vector3 own2 = has2 ? p2 : p1;
        Vector3 mergedFocus;
        if (both) mergedFocus = (mode == SplitMode.Retro) ? p1 : (p1 + p2) * 0.5f;
        else mergedFocus = has1 ? p1 : p2;

        Vector3 focus1 = Vector3.Lerp(own1, mergedFocus, mergeBlend);
        Vector3 focus2 = Vector3.Lerp(own2, mergedFocus, mergeBlend);

        ApplyCamera(camera1, rect1, focus1, snap);

        bool show2 = SplitOnScreen();
        if (show2 && camera2 == null) CreateCamera2();
        if (camera2 != null)
        {
            bool wasOn = camera2.enabled;
            camera2.enabled = show2;
            // A camera that was switched off jumps straight to its spot, rather than flying in from wherever it was.
            ApplyCamera(camera2, rect2, focus2, snap || !wasOn);
        }

        // ---- 4. Retro: Player 2 can't leave the screen.
        if (mode == SplitMode.Retro && both) ClampToScreen(t2);
    }

    // ------------------------------------------------------------ dynamic (diagonal) adaptive

    private bool WantsDynamic()
    {
        if (mode != SplitMode.Adaptive || !dynamicAdaptive || !camera1.orthographic) return false;

        return GetDynamicCamera().isActiveAndEnabled;   // switching that component off falls back to the straight split
    }

    // The DynamicScreenCamera on this object, added the first time it is needed. Nothing has to be linked by hand.
    private DynamicScreenCamera GetDynamicCamera()
    {
        if (dynamicCamera == null) dynamicCamera = GetComponent<DynamicScreenCamera>();
        if (dynamicCamera == null) dynamicCamera = gameObject.AddComponent<DynamicScreenCamera>();
        return dynamicCamera;
    }

    // Back to the ordinary viewport-rect split: start from one clean full screen, with no slide.
    private void LeaveDynamic()
    {
        dynamicRunning = false;
        pixelPerfectCompositeRunning = false;
        if (dynamicCamera != null) dynamicCamera.Release();

        camera1.rect = FullRect;
        rect1 = FullRect;
        rect2 = new Rect(1f, 0f, 0f, 1f);
        mergeBlend = 1f;
        ResetAdaptive();
        snapNextFrame = true;
    }

    // ------------------------------------------------------------ players

    // The player's transform if they are in the game and alive, otherwise null.
    private static Transform Resolve(int number, Transform manual)
    {
        Transform t;
        CoopHealth health;
        bool explicitlyAssigned = manual != null;

        if (explicitlyAssigned)
        {
            // An Inspector assignment is an explicit camera target. Do not discard it merely because the saved
            // LocalCoop toggle is off; users can test / use two cameras with two scene objects without a manager.
            if (!manual.gameObject.activeInHierarchy) return null;
            t = manual;
            health = manual.GetComponent<CoopHealth>();
        }
        else
        {
            // Automatically discovered players still obey the co-op rules and must be registered as CoopHealth.
            if (!CoopRules.Counts(number)) return null;
            health = CoopHealth.Get(number);
            if (health == null) return null;
            t = health.transform;
        }

        // Lost all their health: the camera lets go, EXCEPT while CoopDowned is looking after them (knocked down, blown up, waiting to
        // respawn). It keeps following them, so each player's half of the screen never goes blind and the view is already there
        // the moment they are revived or respawn.
        if (health != null && !health.IsAlive && !health.KeepCameraOn) return null;

        // Explicit Single Player Mode remains authoritative, including when its chosen target is not Player 1.
        if (CoopRules.SinglePlayerMode && number != CoopRules.TrackedPlayer) return null;
        return t;
    }

    // A manually duplicated Camera 1 GameObject may also contain copies of this controller, the diagonal splitter,
    // or an AudioListener. Those components conflict with the single controller that owns both views.
    private void PrepareSecondaryCamera(Camera cam)
    {
        if (cam == null || cam == camera1) return;

        if (cam.gameObject != camera1.gameObject)
        {
            SplitScreenCamera[] controllers = cam.GetComponents<SplitScreenCamera>();
            for (int i = 0; i < controllers.Length; i++)
            {
                SplitScreenCamera other = controllers[i];
                if (other != null && other != this && other.enabled)
                {
                    other.enabled = false;
                    Debug.LogWarning("SplitScreenCamera: disabled a duplicate SplitScreenCamera on Camera 2. Only Camera 1 should own this controller.", cam);
                }
            }

            DynamicScreenCamera[] dynamicControllers = cam.GetComponents<DynamicScreenCamera>();
            for (int i = 0; i < dynamicControllers.Length; i++)
            {
                DynamicScreenCamera other = dynamicControllers[i];
                if (other != null && other != dynamicCamera && other.enabled) other.enabled = false;
            }

            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
        }

        SwitchOffOldFollow(cam);
        cam.targetTexture = null;
        cam.depth = camera1.depth + 1f;
        cam.enabled = false;
        SynchronizePixelPerfectCamera(cam);
    }

    // ------------------------------------------------------------ Pixel Perfect Camera compatibility

    // Pixel Perfect Camera exists in two different Unity implementations: the Built-in package uses
    // UnityEngine.U2D.PixelPerfectCamera, modern URP uses UnityEngine.Rendering.Universal.PixelPerfectCamera,
    // and older URP releases used UnityEngine.Experimental.Rendering.Universal.PixelPerfectCamera.
    // Reflection lets these scripts support either implementation without a hard package dependency.
    private static Component SearchPixelPerfectCamera(Camera cam)
    {
        if (cam == null) return null;
        Component[] components = cam.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null) continue;

            Type type = component.GetType();
            if (type.Name != "PixelPerfectCamera") continue;

            string fullName = type.FullName ?? string.Empty;
            if (fullName == "UnityEngine.U2D.PixelPerfectCamera" ||
                fullName == "UnityEngine.Rendering.Universal.PixelPerfectCamera" ||
                fullName == "UnityEngine.Experimental.Rendering.Universal.PixelPerfectCamera")
                return component;
        }
        return null;
    }

    // The lookup above allocates an array and the PPU read uses reflection. MoveCamera and the compositor ask about
    // Pixel Perfect Camera several times EVERY FRAME, so the answer is kept per camera and only searched again if the
    // component was destroyed or just added (InvalidatePixelPerfect).
    private sealed class PixelPerfectCache
    {
        public Camera camera;
        public Component component;
        public PropertyInfo ppuProperty;
        public bool searched;
        public bool hadComponent;
        public bool ppuLooked;
    }

    private readonly PixelPerfectCache[] ppCache = { new PixelPerfectCache(), new PixelPerfectCache() };   // Camera 1, Camera 2

    private PixelPerfectCache CacheFor(Camera cam)
    {
        if (cam == null) return null;

        PixelPerfectCache slot = null;
        for (int i = 0; i < ppCache.Length; i++)
        {
            if (ppCache[i].camera == cam) { slot = ppCache[i]; break; }
        }
        if (slot == null)
        {
            slot = ppCache[0].camera == null ? ppCache[0] : ppCache[1];
            slot.camera = cam;
            slot.searched = false;
        }

        if (slot.searched && slot.hadComponent && slot.component == null) slot.searched = false;   // it was destroyed
        if (!slot.searched)
        {
            slot.component = SearchPixelPerfectCamera(cam);
            slot.hadComponent = slot.component != null;
            slot.ppuProperty = null;
            slot.ppuLooked = false;
            slot.searched = true;
        }
        return slot;
    }

    private void InvalidatePixelPerfect(Camera cam)
    {
        for (int i = 0; i < ppCache.Length; i++)
        {
            if (ppCache[i].camera == cam) ppCache[i].searched = false;
        }
    }

    private Component FindPixelPerfectCamera(Camera cam)
    {
        PixelPerfectCache c = CacheFor(cam);
        return c != null ? c.component : null;
    }

    private bool HasActivePixelPerfectCamera(Camera cam)
    {
        Component component = FindPixelPerfectCamera(cam);
        Behaviour behaviour = component as Behaviour;
        return component != null && (behaviour == null || behaviour.enabled);
    }

    private void SynchronizePixelPerfectCamera(Camera targetCamera)
    {
        if (camera1 == null || targetCamera == null || targetCamera == camera1) return;

        Component source = FindPixelPerfectCamera(camera1);
        if (source == null) return;

        Component target = targetCamera.GetComponent(source.GetType());
        if (target == null)
        {
            try
            {
                target = targetCamera.gameObject.AddComponent(source.GetType());
                InvalidatePixelPerfect(targetCamera);
            }
            catch (Exception e)
            {
                Debug.LogWarning("SplitScreenCamera: could not add the same Pixel Perfect Camera component to Camera 2. " + e.Message, targetCamera);
                return;
            }
        }

        // Copy documented settings through the component API, not private package internals. The whitelist
        // supports the legacy boolean properties and the modern URP enum properties without copying inherited
        // UnityEngine.Component members such as "name".
        string[] settingNames =
        {
            "assetsPPU", "refResolutionX", "refResolutionY",
            "cropFrameX", "cropFrameY", "cropFrame", "stretchFill",
            "upscaleRT", "pixelSnapping", "gridSnapping"
        };
        for (int i = 0; i < settingNames.Length; i++)
        {
            PropertyInfo property = source.GetType().GetProperty(settingNames[i], BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0) continue;
            try
            {
                object value = property.GetValue(source, null);
                property.SetValue(target, value, null);
            }
            catch (Exception)
            {
                // Some package properties are read-only in practice despite a setter being exposed.
            }
        }

        Behaviour sourceBehaviour = source as Behaviour;
        Behaviour targetBehaviour = target as Behaviour;
        if (sourceBehaviour != null && targetBehaviour != null)
            targetBehaviour.enabled = sourceBehaviour.enabled;
    }

    private static int ReadAssetsPPU(PixelPerfectCache cache)
    {
        if (cache == null || cache.component == null) return 0;
        try
        {
            if (!cache.ppuLooked)
            {
                cache.ppuLooked = true;
                cache.ppuProperty = cache.component.GetType().GetProperty("assetsPPU", BindingFlags.Instance | BindingFlags.Public);
            }
            if (cache.ppuProperty != null && cache.ppuProperty.CanRead) return Convert.ToInt32(cache.ppuProperty.GetValue(cache.component, null));
        }
        catch (Exception) { }
        return 0;
    }

    private static Vector3 SnapToPixelGrid(Vector3 position, int ppu)
    {
        if (ppu <= 0) return position;
        float worldPixel = 1f / ppu;
        position.x = Mathf.Round(position.x / worldPixel) * worldPixel;
        position.y = Mathf.Round(position.y / worldPixel) * worldPixel;
        return position;
    }

    // ------------------------------------------------------------ adaptive

    private void UpdateAdaptive(Vector3 p1, Vector3 p2)
    {
        float viewH = FullHalfHeight() * 2f;
        float viewW = viewH * ((float)Screen.width / Mathf.Max(1, Screen.height));

        float sepX = Mathf.Abs(p1.x - p2.x) / viewW;
        float sepY = Mathf.Abs(p1.y - p2.y) / viewH;
        float sep = Mathf.Max(sepX, sepY);

        // 1. Far enough apart for two views? Split at Split Distance, join at Merge Distance (the gap between the two
        //    stops it flickering at the edge).
        float mergeAt = Mathf.Min(mergeDistance, splitDistance);
        if (wantSplit)
        {
            if (sep < mergeAt) wantSplit = false;
        }
        else if (sep > splitDistance)
        {
            wantSplit = true;
        }

        if (!wantSplit)
        {
            adaptiveMerged = true;
            layoutChanging = false;
            return;
        }

        // 2. Which layout do the players want? While no split is on screen it is a free choice (along the bigger gap).
        //    Once one is showing, it only turns 90 degrees when the other gap is clearly bigger (Flip Bias).
        bool onScreen = SplitOnScreen();
        bool wantVertical;
        if (!onScreen) wantVertical = sepX >= sepY;
        else if (vertical) wantVertical = !(sepY > sepX * flipBias);
        else wantVertical = sepX > sepY * flipBias;

        // Side by side: the one further left takes the left. Stacked: the one higher up takes the top.
        bool wantFirst = wantVertical ? p1.x <= p2.x : p1.y >= p2.y;

        // 3. Apply it. A split that is showing is never morphed into a different one: the old views would sweep across
        //    each other (this was the "freak out" when two players pass each other). It closes first, and reopens the
        //    new way round once it is gone. Nothing changes while a split is showing the way the players still want.
        if (!onScreen)
        {
            vertical = wantVertical;
            cam1First = wantFirst;
            layoutChanging = false;
        }
        else
        {
            layoutChanging = (wantVertical != vertical) || (wantFirst != cam1First);
        }

        adaptiveMerged = layoutChanging;
    }

    // One screen, and forget everything the adaptive split was doing.
    private void ResetAdaptive()
    {
        adaptiveMerged = true;
        wantSplit = false;
        layoutChanging = false;
    }

    // True while the second view is visible at all, including while it is still sliding open or closed.
    private bool SplitOnScreen()
    {
        return rect2.width > MinSize && rect2.height > MinSize;
    }

    // Horizontal / Vertical: two views for as long as both players are in. Who gets which half depends on Keep Sides Fixed.
    // Returns true when the screen should be heading for ONE view: a player is missing, or the split is closing so that it
    // can reopen the other way round (same close-and-reopen as Adaptive, so the two views never sweep across each other).
    private bool UpdateFixedSplit(bool wantVertical, bool both, Vector3 p1, Vector3 p2)
    {
        bool onScreen = SplitOnScreen();

        // Which half does Player 1 take? With Keep Sides Fixed on, always the left / top one, wherever the players are.
        bool wantFirst = true;
        if (!staticSides && both)
        {
            float viewH = FullHalfHeight() * 2f;
            float viewW = viewH * ((float)Screen.width / Mathf.Max(1, Screen.height));

            if (!onScreen || wantVertical != vertical)
            {
                // A fresh split, or a new direction: straight to whoever is further left / higher up.
                wantFirst = wantVertical ? p1.x <= p2.x : p1.y >= p2.y;
            }
            else
            {
                // A split is showing: keep the halves as they are until someone has clearly crossed over, so two players
                // walking side by side don't make the halves flutter back and forth.
                wantFirst = cam1First;
                if (wantVertical)
                {
                    float margin = SwapMargin * viewW;
                    if (cam1First && p1.x > p2.x + margin) wantFirst = false;
                    else if (!cam1First && p1.x < p2.x - margin) wantFirst = true;
                }
                else
                {
                    float margin = SwapMargin * viewH;
                    if (cam1First && p1.y < p2.y - margin) wantFirst = false;
                    else if (!cam1First && p1.y > p2.y + margin) wantFirst = true;
                }
            }
        }

        if (!onScreen)
        {
            vertical = wantVertical;
            cam1First = wantFirst;
            layoutChanging = false;
        }
        else
        {
            layoutChanging = (wantVertical != vertical) || (wantFirst != cam1First);
        }

        return !both || layoutChanging;
    }

    // ------------------------------------------------------------ layout

    // Screen rectangles (0..1, bottom-left origin) for each camera. Merged = Camera 1 fills the screen and Camera 2
    // shrinks into the edge on its own side; the dividing line slides over to that edge.
    private void LayoutTargets(bool merged, out Rect r1, out Rect r2)
    {
        if (vertical)   // dividing line runs up-down: side by side
        {
            if (cam1First)
            {
                r1 = merged ? FullRect : new Rect(0f, 0f, 0.5f, 1f);
                r2 = merged ? new Rect(1f, 0f, 0f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f);
            }
            else
            {
                r1 = merged ? FullRect : new Rect(0.5f, 0f, 0.5f, 1f);
                r2 = merged ? new Rect(0f, 0f, 0f, 1f) : new Rect(0f, 0f, 0.5f, 1f);
            }
        }
        else            // dividing line runs left-right: one above the other
        {
            if (cam1First)
            {
                r1 = merged ? FullRect : new Rect(0f, 0.5f, 1f, 0.5f);
                r2 = merged ? new Rect(0f, 0f, 1f, 0f) : new Rect(0f, 0f, 1f, 0.5f);
            }
            else
            {
                r1 = merged ? FullRect : new Rect(0f, 0f, 1f, 0.5f);
                r2 = merged ? new Rect(0f, 1f, 1f, 0f) : new Rect(0f, 0.5f, 1f, 0.5f);
            }
        }
    }

    private static Rect SlideRect(Rect from, Rect to, float k, float minStep)
    {
        return new Rect(
            Slide(from.x, to.x, k, minStep),
            Slide(from.y, to.y, k, minStep),
            Slide(from.width, to.width, k, minStep),
            Slide(from.height, to.height, k, minStep));
    }

    // Moves a value towards its target by the larger of two steps: the usual ease-out step (k of the way) or minStep.
    // The ease-out gives the quick start; minStep stops the end from slowing to a crawl, and MoveTowards lands exactly
    // on the target instead of creeping up on it.
    private static float Slide(float current, float target, float k, float minStep)
    {
        float easeStep = Mathf.Abs(target - current) * k;
        return Mathf.MoveTowards(current, target, Mathf.Max(easeStep, minStep));
    }

    // ------------------------------------------------------------ cameras

    private void ApplyCamera(Camera cam, Rect rect, Vector3 focus, bool snap)
    {
        cam.rect = rect;

        if (cam.orthographic)
        {
            // A view that is half as tall shows half as much height, so the world keeps the same on-screen size.
            cam.orthographicSize = keepSameScale ? baseSize * Mathf.Max(rect.height, 0.02f) : baseSize;
        }

        MoveCamera(cam, focus, snap);
    }

    /// <summary>Moves a camera towards a focus point (same follow feel as the old CameraFollow). Used by DynamicScreenCamera too.</summary>
    public void MoveCamera(Camera cam, Vector3 focus, bool snap)
    {
        Vector3 target = focus + offset;
        Transform ct = cam.transform;

        PixelPerfectCache pp = CacheFor(cam);
        Component pixelPerfectComponent = pp != null ? pp.component : null;
        Behaviour pixelPerfectBehaviour = pixelPerfectComponent as Behaviour;
        bool pixelPerfect = pixelPerfectComponent != null && (pixelPerfectBehaviour == null || pixelPerfectBehaviour.enabled);
        int ppu = pixelPerfect ? ReadAssetsPPU(pp) : 0;
        if (ppu > 0) target = SnapToPixelGrid(target, ppu);

        bool jump = snap || (snapDistance > 0f && (ct.position - target).sqrMagnitude > snapDistance * snapDistance);
        if (jump) ct.position = target;
        else ct.position = Vector3.Lerp(ct.position, target, Mathf.Min(1f, followSpeed * Time.deltaTime));

        // Pixel-perfect artwork is easily disturbed by fractional camera movement. Snap the final camera
        // transform to the same art-pixel grid used by Pixel Perfect Camera's assetsPPU setting.
        if (ppu > 0) ct.position = SnapToPixelGrid(ct.position, ppu);
    }

    private void CreateCamera2()
    {
        GameObject go = new GameObject("Player 2 Camera");   // a root object, so it never inherits movement from Camera 1
        camera2 = go.AddComponent<Camera>();
        camera2.CopyFrom(camera1);
        camera2.targetTexture = null;                        // do not inherit Camera 1's compositor RenderTexture
        camera2.depth = camera1.depth + 1f;                  // drawn on top of Camera 1 wherever they overlap
        camera2.enabled = false;                             // no AudioListener on purpose: Camera 1 keeps it
        createdCamera2 = true;
        SynchronizePixelPerfectCamera(camera2);
    }

    // Half the world height that one FULL screen shows.
    private float FullHalfHeight()
    {
        if (camera1.orthographic) return BaseSize;
        return Mathf.Abs(offset.z) * Mathf.Tan(camera1.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    // ------------------------------------------------------------ retro

    // Holds a player inside Camera 1's view. Uses the full-screen view, so it also works while the screens are sliding.
    private void ClampToScreen(Transform t)
    {
        float halfH = FullHalfHeight();
        float halfW = halfH * ((float)Screen.width / Mathf.Max(1, Screen.height));
        float padX = Mathf.Min(retroEdgePadding, halfW);
        float padY = Mathf.Min(retroEdgePadding, halfH);

        Vector3 c = camera1.transform.position;
        Vector3 p = t.position;
        Vector3 clamped = p;
        clamped.x = Mathf.Clamp(p.x, c.x - halfW + padX, c.x + halfW - padX);
        clamped.y = Mathf.Clamp(p.y, c.y - halfH + padY, c.y + halfH - padY);

        if (clamped.x == p.x && clamped.y == p.y) return;

        t.position = clamped;

        // Rigidbody2D players: move the physics body too, or it would put the player straight back.
        Rigidbody2D rb = t.GetComponent<Rigidbody2D>();
        if (rb != null) rb.position = clamped;
    }
}
