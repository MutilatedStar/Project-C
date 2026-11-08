using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;   // where CoopHealth lives (SplitScreenCamera uses it too)

/// <summary>
/// Distance culling for sprites. Every SpriteRenderer further than Cull Distance from ALL players has its rendering
/// switched off, and switched back on when a player comes near again. Put it on any object in the scene (the same
/// object as SplitScreenCamera is a good spot). Nothing has to be linked.
///
/// PLAYERS: any number. Players 1..Max Player Number are found through CoopHealth with the same rules SplitScreenCamera
/// uses (dead players, switched-off players and single player mode are left out). For a 3rd / 4th player, or anything
/// else that should keep its surroundings alive, add the CullFocus component to it (or drag it into Extra Targets).
/// With one player it just measures from that one player; with four, a sprite stays on while ANY of them is near.
///
/// CAMERAS: any number. If a SplitScreenCamera is in the scene its cameras are used (Camera 2 only while the screen
/// is split). Otherwise every active game camera is used. Extra Cameras adds more. With Keep On Screen ticked a sprite
/// is never culled while any of those cameras can see it, so nothing ever vanishes in front of a player, even when the
/// camera sits between two far apart players.
///
/// Distances are measured in X / Y only (top-down 2D) from the edge of each sprite, so big sprites are kept while any
/// part of them is close. Culling only turns off DRAWING (Renderer.forceRenderingOff): the object, its scripts,
/// animations and colliders keep running, and it never touches the sprite's own "enabled" tick.
///
/// Tilemaps are not touched (a tilemap is one big renderer, and Unity already skips the parts off screen).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // after SplitScreenCamera has moved the cameras this frame
public class SpriteDistanceCuller : MonoBehaviour
{
    [Header("Distance")]
    [Tooltip("Sprites further than this (world units) from every player are culled. Measured from the nearest edge of the sprite. Make it bigger than half a screen if you turn Keep On Screen off, or sprites will vanish in view.")]
    [Min(0f)] public float cullDistance = 25f;
    [Tooltip("A culled sprite only comes back once it is this much INSIDE Cull Distance, so sprites sitting right on the edge don't flicker on and off.")]
    [Min(0f)] public float hysteresis = 2f;

    [Header("Cameras")]
    [Tooltip("ON: a sprite is never culled while any camera can see it, whatever its distance. Strongly recommended with split screen and Adaptive mode, where a camera can sit far from both players.")]
    public bool keepOnScreen = true;
    [Tooltip("World units added around each camera's view, so sprites are switched back on just BEFORE they scroll into view.")]
    [Min(0f)] public float screenMargin = 2f;
    [Tooltip("Optional. Extra cameras to protect (a 3rd or 4th split-screen camera, a minimap camera). The Split Screen Camera's cameras, or all active game cameras if there is none, are found automatically.")]
    public Camera[] extraCameras;

    [Header("Players")]
    [Tooltip("ON: players are found by number through CoopHealth, exactly like SplitScreenCamera does.")]
    public bool useCoopPlayers = true;
    [Tooltip("The highest player number to look for. Numbers CoopHealth doesn't know about are simply skipped, so it is safe to leave this at 4.")]
    [Range(1, 8)] public int maxPlayerNumber = 4;
    [Tooltip("Optional. Any other objects to measure from. Objects with a CullFocus component are found by themselves.")]
    public Transform[] extraTargets;

    [Header("Sprites")]
    [Tooltip("Only sprites on these layers are ever culled. Take a layer out for anything that must always be drawn.")]
    public LayerMask cullLayers = ~0;
    [Tooltip("Seconds for one full pass over all sprites. The work is spread over the frames in between, so there are no spikes. Smaller = reacts faster, costs more.")]
    [Min(0.02f)] public float checkInterval = 0.1f;
    [Tooltip("Seconds between searches for NEW sprites (spawned foes, pickups...). 0 = only look once, at the start.")]
    [Min(0f)] public float rescanInterval = 1f;

    [Header("Debug (read only: filled in while you play in the Editor)")]
    [SerializeField, TextArea(2, 4)] private string status = "";

    /// <summary>How many sprites are being managed.</summary>
    public int TrackedSprites { get { return entries.Count; } }
    /// <summary>How many of them are switched off right now.</summary>
    public int CulledSprites { get { return culledCount; } }
    /// <summary>How many players / focus points the distance is measured from.</summary>
    public int PlayersTracked { get { return playersTracked; } }

    // ------------------------------------------------------------ data

    private sealed class Entry
    {
        public SpriteRenderer renderer;
        public Transform transform;
        public bool culled;
    }

    // What one camera can see this frame, in the camera's own space so a rotated camera works too.
    private struct View
    {
        public Camera camera;
        public Matrix4x4 worldToLocal;
        public bool ortho;
        public float halfW, halfH;      // orthographic: half the width / height of the view
        public float tanHalf, aspect;   // perspective: the view widens with distance
        public Vector2 centre;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly HashSet<SpriteRenderer> known = new HashSet<SpriteRenderer>();
    private readonly List<Transform> focusSources = new List<Transform>();
    private readonly List<Vector2> focusPos = new List<Vector2>();
    private View[] views = new View[8];
    private Camera[] cameraBuffer = new Camera[8];

    private int viewCount;
    private int playersTracked;
    private int culledCount;
    private int cursor;              // where the slice of sprites checked this frame starts
    private float sliceCarry;        // fractions of a sprite carried over from frame to frame
    private float nextRescan;
    private bool scanned;

#if UNITY_EDITOR
    private float nextReport;
#endif

    // ------------------------------------------------------------ setup

    private void OnEnable()
    {
        scanned = false;   // search the scene again, and check every sprite straight away
        cursor = 0;
        sliceCarry = 0f;
    }

    // Switching this off (or leaving the scene) gives every culled sprite back.
    private void OnDisable()
    {
        ShowAll();
        entries.Clear();
        known.Clear();
    }

    /// <summary>Look for new sprites right now (a spawner can call this instead of waiting for Rescan Interval).</summary>
    public void RescanNow()
    {
        ScanForSprites();
    }

    private void ScanForSprites()
    {
#if UNITY_2022_2_OR_NEWER
        SpriteRenderer[] all = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
#else
        SpriteRenderer[] all = FindObjectsOfType<SpriteRenderer>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            SpriteRenderer sr = all[i];
            if (known.Contains(sr)) continue;
            if ((cullLayers.value & (1 << sr.gameObject.layer)) == 0) continue;

            known.Add(sr);
            Entry e = new Entry();
            e.renderer = sr;
            e.transform = sr.transform;
            entries.Add(e);
        }
    }

    // ------------------------------------------------------------ main loop

    private void LateUpdate()
    {
        if (!scanned || (rescanInterval > 0f && Time.unscaledTime >= nextRescan))
        {
            bool first = !scanned;
            scanned = true;
            nextRescan = Time.unscaledTime + rescanInterval;
            ScanForSprites();
            if (first) sliceCarry = entries.Count;   // the very first pass checks everything at once
        }

        CollectViews();
        CollectFocus();
        playersTracked = focusPos.Count;

        // Nobody left to measure from (everyone is down): measure from the cameras instead, so the picture stays put.
        if (focusPos.Count == 0)
        {
            for (int i = 0; i < viewCount; i++) focusPos.Add(views[i].centre);
        }

        // No players and no cameras at all: show everything rather than hide the world.
        if (focusPos.Count == 0)
        {
            if (culledCount > 0) ShowAll();
            Report();
            return;
        }

        int count = entries.Count;
        if (count == 0)
        {
            Report();
            return;
        }

        // Check a slice of the sprites each frame so that one full pass takes about Check Interval seconds.
        sliceCarry = Mathf.Min(sliceCarry + count * Time.unscaledDeltaTime / Mathf.Max(0.02f, checkInterval), count);
        int slice = (int)sliceCarry;
        sliceCarry -= slice;

        for (int done = 0; done < slice && entries.Count > 0; done++)
        {
            if (cursor >= entries.Count) cursor = 0;

            Entry e = entries[cursor];
            if (e.renderer == null)
            {
                // The sprite was destroyed: drop it (the last one takes its place, so the cursor stays where it is).
                if (e.culled) culledCount--;
                known.Remove(e.renderer);
                int last = entries.Count - 1;
                entries[cursor] = entries[last];
                entries.RemoveAt(last);
                continue;
            }

            Evaluate(e);
            cursor++;
        }

        Report();
    }

    // Decides whether one sprite should be drawn, and switches it if that changed.
    private void Evaluate(Entry e)
    {
        Transform t = e.transform;
        Vector3 p = t.position;
        float radius = WorldRadius(e.renderer, t);

        // 1. Close to any player? Distance counts from the sprite's nearest edge. A sprite that is already culled has
        //    to come back inside (Cull Distance - Hysteresis), so the edge doesn't flicker.
        float limit = e.culled ? Mathf.Max(0f, cullDistance - hysteresis) : cullDistance;
        float reach = limit + radius;
        float reachSq = reach * reach;

        bool keep = false;
        for (int i = 0; i < focusPos.Count; i++)
        {
            float dx = p.x - focusPos[i].x;
            float dy = p.y - focusPos[i].y;
            if (dx * dx + dy * dy <= reachSq) { keep = true; break; }
        }

        // 2. Or is it in view of any camera?
        if (!keep && keepOnScreen) keep = SeenByAnyView(p, radius);

        if (keep && e.culled)
        {
            e.culled = false;
            e.renderer.forceRenderingOff = false;
            culledCount--;
        }
        else if (!keep && !e.culled)
        {
            e.culled = true;
            e.renderer.forceRenderingOff = true;
            culledCount++;
        }
    }

    private void ShowAll()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e.culled && e.renderer != null) e.renderer.forceRenderingOff = false;
            e.culled = false;
        }
        culledCount = 0;
    }

    // ------------------------------------------------------------ sprite size

    // Radius of a circle around the sprite's pivot that covers the whole sprite, in world units. A circle ignores
    // rotation and flipping, and sprite.bounds works even while the renderer is switched off (renderer.bounds doesn't).
    private static float WorldRadius(SpriteRenderer sr, Transform t)
    {
        Vector3 s = t.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));

        // Tiled / sliced sprites have their own size: the full diagonal covers wherever the pivot is.
        if (sr.drawMode != SpriteDrawMode.Simple) return sr.size.magnitude * scale;

        Sprite sprite = sr.sprite;
        if (sprite == null) return 0f;

        Bounds b = sprite.bounds;
        return (b.center.magnitude + b.extents.magnitude) * scale;
    }

    // ------------------------------------------------------------ players

    // Everything the distance is measured from this frame: players first, then CullFocus objects, then Extra Targets.
    private void CollectFocus()
    {
        focusSources.Clear();
        focusPos.Clear();

        if (useCoopPlayers)
        {
            SplitScreenCamera split = SplitScreenCamera.Current;
            for (int n = 1; n <= maxPlayerNumber; n++)
            {
                // Players dragged into SplitScreenCamera's Player 1 / Player 2 slots count as well.
                Transform manual = null;
                if (split != null)
                {
                    if (n == 1) manual = split.player1;
                    else if (n == 2) manual = split.player2;
                }
                AddFocus(ResolvePlayer(n, manual));
            }
        }

        for (int i = 0; i < CullFocus.All.Count; i++) AddFocus(CullFocus.All[i].transform);

        if (extraTargets != null)
        {
            for (int i = 0; i < extraTargets.Length; i++) AddFocus(extraTargets[i]);
        }
    }

    private void AddFocus(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return;
        if (focusSources.Contains(t)) return;   // the same player found twice

        focusSources.Add(t);
        Vector3 p = t.position;
        focusPos.Add(new Vector2(p.x, p.y));
    }

    // Same rules as SplitScreenCamera.Resolve: the player's transform if they are in the game and alive, else null.
    private static Transform ResolvePlayer(int number, Transform manual)
    {
        Transform t;
        CoopHealth health;

        if (manual != null)
        {
            if (!manual.gameObject.activeInHierarchy) return null;
            t = manual;
            health = manual.GetComponent<CoopHealth>();
        }
        else
        {
            if (!CoopRules.Counts(number)) return null;
            health = CoopHealth.Get(number);
            if (health == null) return null;
            t = health.transform;
        }

        if (health != null && !health.IsAlive) return null;
        if (CoopRules.SinglePlayerMode && number != CoopRules.TrackedPlayer) return null;
        return t;
    }

    // ------------------------------------------------------------ cameras

    // The cameras that are showing the game this frame: the split screen's (Camera 2 only while it is split), or every
    // active game camera when there is no SplitScreenCamera, plus Extra Cameras.
    private void CollectViews()
    {
        viewCount = 0;

        SplitScreenCamera split = SplitScreenCamera.Current;
        if (split != null && split.isActiveAndEnabled)
        {
            AddView(split.Camera1);
            Camera cam2 = split.Camera2;
            if (cam2 != null && (cam2.enabled || split.IsSplit)) AddView(cam2);
        }
        else
        {
            int n = Camera.allCamerasCount;
            if (cameraBuffer.Length < n) cameraBuffer = new Camera[n];
            n = Camera.GetAllCameras(cameraBuffer);
            for (int i = 0; i < n; i++)
            {
                if (cameraBuffer[i] != null && cameraBuffer[i].cameraType == CameraType.Game) AddView(cameraBuffer[i]);
            }
        }

        if (extraCameras != null)
        {
            for (int i = 0; i < extraCameras.Length; i++)
            {
                if (extraCameras[i] != null && extraCameras[i].isActiveAndEnabled) AddView(extraCameras[i]);
            }
        }
    }

    private void AddView(Camera cam)
    {
        if (cam == null) return;
        for (int i = 0; i < viewCount; i++)
        {
            if (views[i].camera == cam) return;   // already have it
        }
        if (viewCount == views.Length) System.Array.Resize(ref views, viewCount * 2);

        Transform ct = cam.transform;

        // A camera's aspect follows its Camera.rect, so a half-height split view is already wider than the screen.
        float aspect = cam.aspect;
        if (!(aspect > 0.05f) || aspect > 50f) aspect = (float)Screen.width / Mathf.Max(1, Screen.height);

        views[viewCount].camera = cam;
        views[viewCount].worldToLocal = ct.worldToLocalMatrix;
        views[viewCount].ortho = cam.orthographic;
        views[viewCount].aspect = aspect;
        if (cam.orthographic)
        {
            views[viewCount].halfH = cam.orthographicSize;
            views[viewCount].halfW = cam.orthographicSize * aspect;
            views[viewCount].tanHalf = 0f;
        }
        else
        {
            views[viewCount].halfH = 0f;
            views[viewCount].halfW = 0f;
            views[viewCount].tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }
        Vector3 c = ct.position;
        views[viewCount].centre = new Vector2(c.x, c.y);
        viewCount++;
    }

    // True if a sprite (a circle of this radius around p) is inside, or within Screen Margin of, any camera's view.
    private bool SeenByAnyView(Vector3 p, float radius)
    {
        float pad = radius + screenMargin;
        for (int i = 0; i < viewCount; i++)
        {
            Vector3 local = views[i].worldToLocal.MultiplyPoint3x4(p);

            float halfW, halfH;
            if (views[i].ortho)
            {
                halfW = views[i].halfW;
                halfH = views[i].halfH;
            }
            else
            {
                if (local.z < -pad) continue;   // behind the camera
                halfH = Mathf.Max(local.z, 0f) * views[i].tanHalf;
                halfW = halfH * views[i].aspect;
            }

            if (Mathf.Abs(local.x) <= halfW + pad && Mathf.Abs(local.y) <= halfH + pad) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ debug

    private void Report()
    {
#if UNITY_EDITOR
        if (Time.unscaledTime < nextReport) return;
        nextReport = Time.unscaledTime + 0.25f;

        status = "Measuring from " + playersTracked + " player(s), protecting " + viewCount + " camera view(s).\n" +
                 "Sprites: " + entries.Count + " managed, " + culledCount + " culled.";
#endif
    }

    // Select this object while playing to see each player's Cull Distance as an orange circle.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        for (int i = 0; i < focusPos.Count; i++)
        {
            Gizmos.DrawWireSphere(new Vector3(focusPos[i].x, focusPos[i].y, 0f), cullDistance);
        }
    }
}
