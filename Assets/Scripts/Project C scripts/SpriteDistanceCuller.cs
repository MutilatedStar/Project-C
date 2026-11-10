using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;   // where CoopHealth lives (SplitScreenCamera uses it too)

/// <summary>
/// Distance culling for the whole world. Put it on any object in the scene (the same object as SplitScreenCamera is a good
/// spot). Nothing has to be linked. Each frame it works out where the "alive" areas are, then switches everything outside
/// them off, and back on when an area reaches it again.
///
/// THE ALIVE AREAS
///   - every player (any number: 1, 2, 3, 4...), within Cull Distance. Players 1..Max Player Number are found through
///     CoopHealth with the same rules SplitScreenCamera uses; a 3rd / 4th player can also carry a CullFocus component;
///   - everything any camera can see (Keep On Screen), single camera or split screen, so nothing ever vanishes in view;
///   - every RoomAltar that is NOT triggered yet, plus Altar Protect Radius around its plates. A triggered altar is let go;
///   - CullFocus objects (a ball that can press a plate, for example) while they are live;
///   - the landing area of the NEXT floor, within Preload Distance (see PRELOAD below).
///
/// WHAT GETS SWITCHED OFF  (nothing is destroyed, deactivated or moved, and no tile is ever removed)
///   - everything that is DRAWN: sprites, particles, trails, lines, tilemaps. Only the drawing stops (forceRenderingOff): the
///     object, its scripts, physics and animation keep running, and the sprite's own "enabled" tick is never touched. A
///     renderer is only culled if some camera actually draws its layer, so things drawn by another camera (the split screen's
///     composite, a HUD camera) are left alone;
///   - positional sounds (AudioSources with Spatial Blend above 0) are muted when far, and un-muted when you get close. 2D
///     sounds, and anything under SfxManager, can't have a place, so they are never touched; to make a 2D sound obey distance,
///     check SpriteDistanceCuller.IsPointActive(position) before playing it (see SfxManager.PlayAt in the notes);
///   - foes: their pictures are culled like everything else, and FoeCullLod (added to every FoeController automatically) swaps
///     their AI for a simple roam while they are far, and back to the full AI when they are near.
///
/// PRELOAD (floors)
///   While the team is on floor N, the landing spots of floor N+1 (from FloorController's spawn points) are kept alive within
///   Preload Distance, so that area is already switched on, and, with Warm Up Next Floor, drawn once into a tiny hidden picture
///   so its textures are already on the graphics card. When the team changes floor (behind the black screen) every sprite is
///   re-checked at once: floor N is switched off, the new landing area is on, and the next floor's landing area is warmed up.
///   Floors are in ONE scene and the team only goes down, so everything of the old floor is simply far away and culled.
///
/// TILEMAPS: a tilemap is drawn as one object, so it is culled whole, only when all of it is out of range. Unity already skips
/// the parts of a tilemap that no camera sees, and removing tiles would make HazardTileFx rebuild its glow (and confuse
/// FoeSpawner, FloorManager and the fountain), so tiles are never removed.
///
/// Distances are measured in X / Y only (top-down 2D), from the nearest edge of each thing.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // after SplitScreenCamera has moved the cameras this frame
public class SpriteDistanceCuller : MonoBehaviour
{
    [Header("Distance")]
    [Tooltip("Things further than this (world units) from every player are culled. Measured from the nearest edge. Make it bigger than half a screen if you turn Keep On Screen off, or things will vanish in view.")]
    [Min(0f)] public float cullDistance = 25f;
    [Tooltip("A culled thing only comes back once it is this much INSIDE the distance, so things on the edge don't flicker on and off.")]
    [Min(0f)] public float hysteresis = 2f;

    [Header("Cameras")]
    [Tooltip("ON: nothing is culled while any camera can see it, whatever its distance. Strongly recommended with split screen and Adaptive mode.")]
    public bool keepOnScreen = true;
    [Tooltip("World units added around each camera's view, so things are switched back on just BEFORE they scroll into view.")]
    [Min(0f)] public float screenMargin = 2f;
    [Tooltip("Optional. Extra cameras to protect (a 3rd or 4th split-screen camera, a minimap camera). The Split Screen Camera's cameras, or all active game cameras if there is none, are found automatically.")]
    public Camera[] extraCameras;

    [Header("Players")]
    [Tooltip("ON: players are found by number through CoopHealth, exactly like SplitScreenCamera does.")]
    public bool useCoopPlayers = true;
    [Tooltip("The highest player number to look for.")]
    [Range(1, 8)] public int maxPlayerNumber = 4;
    [Tooltip("Optional. Any other objects to measure from. Objects with a CullFocus component are found by themselves.")]
    public Transform[] extraTargets;

    [Header("What gets culled")]
    [Tooltip("Only things on these layers are ever culled. Take a layer out for anything that must always be drawn.")]
    public LayerMask cullLayers = ~0;
    [Tooltip("Sprites, tilemaps, particles, trails, lines: everything that is drawn.")]
    public bool cullRenderers = true;
    [Tooltip("Positional sounds (Spatial Blend above 0) are muted when far away and come back when you get close.")]
    public bool cullSounds = true;
    [Tooltip("Adds FoeCullLod to every FoeController, so far-away foes use a simple roam instead of the full AI.")]
    public bool autoAddFoeLod = true;

    [Header("Altars (puzzle plates)")]
    [Tooltip("ON: an altar that is not triggered yet is never culled, and neither is the area around its plates. A triggered altar can be culled.")]
    public bool protectAltars = true;
    [Tooltip("How far around an untriggered altar's plates everything is kept alive (world units): enough for a ball being pushed onto a plate.")]
    [Min(0f)] public float altarProtectRadius = 10f;

    [Header("Floors (preload)")]
    [Tooltip("ON: the landing area of the NEXT floor is kept switched on while the team is on this one.")]
    public bool preloadNextFloor = true;
    [Tooltip("How big that 'small chunk' around each landing spot of the next floor is (world units).")]
    [Min(0f)] public float preloadDistance = 12f;
    [Tooltip("ON: that area is also drawn once into a tiny hidden picture (when a floor starts, behind the black screen) so its textures and shaders are ready before the team arrives. Untick it if you ever see a warning or a flash.")]
    public bool warmUpNextFloor = true;

    [Header("Speed")]
    [Tooltip("Seconds for one full pass over everything. The work is spread over the frames in between, so there are no spikes.")]
    [Min(0.02f)] public float checkInterval = 0.1f;
    [Tooltip("Seconds between searches for NEW things (spawned foes, pickups, effects...). 0 = only look once, at the start.")]
    [Min(0f)] public float rescanInterval = 1f;

    [Header("Debug (read only: filled in while you play in the Editor)")]
    [SerializeField, TextArea(3, 6)] private string status = "";

    // ------------------------------------------------------------ public API

    /// <summary>The culler in the scene (null if there is none).</summary>
    public static SpriteDistanceCuller Current { get; private set; }

    /// <summary>True while a RoomAltar on the live floor has not been triggered yet.</summary>
    public static bool AltarsWaiting { get { return Current != null && Current.altarsWaiting; } }

    /// <summary>How many things are being managed (drawn things + positional sounds).</summary>
    public int TrackedCount { get { return entries.Count; } }
    /// <summary>How many of them are switched off right now.</summary>
    public int CulledCount { get { return culledCount; } }
    /// <summary>How many players / focus points the distance is measured from.</summary>
    public int PlayersTracked { get { return playersTracked; } }

    /// <summary>
    /// Is this point inside an alive area? (Close to a player, a protected altar or the next floor's landing area, or in view of a
    /// camera.) Use it before playing a 2D sound that belongs to a place, and FoeCullLod uses it for the foe AI. With no culler in the
    /// scene it is always true.
    /// </summary>
    public static bool IsPointActive(Vector2 point)
    {
        return Current == null || Current.IsInActiveZone(point, 0f, true);
    }

    /// <summary>
    /// Is a circle at this point inside an alive area? wasInside = was it inside the last time you asked (it gives a little hysteresis,
    /// so a foe on the edge doesn't switch back and forth).
    /// </summary>
    public bool IsInActiveZone(Vector2 point, float radius, bool wasInside)
    {
        if (focus.Count == 0 && viewCount == 0) return true;   // nothing measured yet: don't switch anything off
        return InZone(new Vector3(point.x, point.y, 0f), new Vector2(radius, radius), wasInside, true);
    }

    /// <summary>Look for new things right now (a spawner can call this instead of waiting for Rescan Interval).</summary>
    public void RescanNow()
    {
        Scan();
    }

    // ------------------------------------------------------------ data

    private sealed class Entry
    {
        public UnityEngine.Object owner;   // the renderer or the sound (so a destroyed one can still be dropped from the set)
        public Renderer renderer;
        public AudioSource sound;
        public int layer;
        public bool culled;
        public bool restoreMute;
    }

    // One alive area: a centre and how far it reaches (0 = Cull Distance).
    private struct Focus
    {
        public Vector2 pos;
        public float radius;
    }

    private struct AltarInfo
    {
        public RoomAltar altar;
        public Vector2 centre;
        public float radius;
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
    private readonly HashSet<UnityEngine.Object> known = new HashSet<UnityEngine.Object>();
    private readonly List<Focus> focus = new List<Focus>();
    private readonly List<Transform> focusSources = new List<Transform>();
    private readonly List<AltarInfo> altars = new List<AltarInfo>();
    private readonly List<Collider2D> colliderBuffer = new List<Collider2D>();
    private readonly List<Vector2> preloadPoints = new List<Vector2>();
    private View[] views = new View[8];
    private Camera[] cameraBuffer = new Camera[8];

    private FloorController floorController;
    private int viewCount;
    private int viewMask;            // the layers at least one camera draws
    private int playersTracked;
    private int culledCount;
    private int cursor;              // where the slice checked this frame starts
    private float sliceCarry;        // fractions of an entry carried over from frame to frame
    private float nextRescan;
    private bool scanned;
    private bool altarsWaiting;
    private bool floorChanged;       // the team changed floor: check everything at once
    private bool warmPending;
    private bool warmUpFailed;

#if UNITY_EDITOR
    private float nextReport;
#endif

    // ------------------------------------------------------------ setup

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
    }

    private void OnEnable()
    {
        Current = this;
        scanned = false;   // search the scene again, and check everything straight away
        cursor = 0;
        sliceCarry = 0f;
        warmPending = true;
        FloorController.FloorChanged += OnFloorChanged;
    }

    // Switching this off (or leaving the scene) gives everything back.
    private void OnDisable()
    {
        FloorController.FloorChanged -= OnFloorChanged;
        if (Current == this) Current = null;

        ShowAll();
        entries.Clear();
        known.Clear();
        altars.Clear();
    }

    // Fires when the team has been moved to a new floor, behind the black screen.
    private void OnFloorChanged(int floor)
    {
        floorChanged = true;
        warmPending = true;
    }

    private static T[] FindAll<T>() where T : UnityEngine.Object
    {
#if UNITY_2022_2_OR_NEWER
        return FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<T>();
#endif
    }

    // Finds everything new: drawn things, positional sounds, altars and foes.
    private void Scan()
    {
        if (cullRenderers)
        {
            Renderer[] all = FindAll<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || known.Contains(r)) continue;

                int layer = r.gameObject.layer;
                if ((cullLayers.value & (1 << layer)) == 0) continue;
                if (r.GetComponentInParent<SplitScreenCamera>() != null) continue;   // the split screen's own pieces

                known.Add(r);
                Entry e = new Entry();
                e.owner = r;
                e.renderer = r;
                e.layer = layer;
                entries.Add(e);
            }
        }

        if (cullSounds)
        {
            AudioSource[] all = FindAll<AudioSource>();
            for (int i = 0; i < all.Length; i++)
            {
                AudioSource s = all[i];
                if (s == null || known.Contains(s)) continue;
                if (s.spatialBlend <= 0.01f) continue;                              // a 2D sound has no place to be far from
                if ((cullLayers.value & (1 << s.gameObject.layer)) == 0) continue;
                if (s.GetComponentInParent<SfxManager>() != null) continue;         // the global sound effects are never culled

                known.Add(s);
                Entry e = new Entry();
                e.owner = s;
                e.sound = s;
                entries.Add(e);
            }
        }

        ScanAltars();

        if (autoAddFoeLod)
        {
            FoeController[] foes = FindAll<FoeController>();
            for (int i = 0; i < foes.Length; i++)
            {
                if (foes[i] != null && foes[i].GetComponent<FoeCullLod>() == null) foes[i].gameObject.AddComponent<FoeCullLod>();
            }
        }
    }

    // The middle and size of every altar, from its plate colliders.
    private void ScanAltars()
    {
        altars.Clear();
        RoomAltar[] found = FindAll<RoomAltar>();
        for (int i = 0; i < found.Length; i++)
        {
            RoomAltar a = found[i];
            if (a == null) continue;

            a.GetComponentsInChildren<Collider2D>(true, colliderBuffer);
            Bounds b = new Bounds(a.transform.position, Vector3.zero);
            for (int c = 0; c < colliderBuffer.Count; c++)
            {
                if (colliderBuffer[c] != null) b.Encapsulate(colliderBuffer[c].bounds);
            }

            AltarInfo info;
            info.altar = a;
            info.centre = new Vector2(b.center.x, b.center.y);
            info.radius = new Vector2(b.extents.x, b.extents.y).magnitude + altarProtectRadius;
            altars.Add(info);
        }
    }

    // The landing spots of the NEXT floor (a small chunk of it is kept alive). Past the last floor the list repeats, so a
    // spot that is the same as this floor's own is skipped.
    private void RefreshPreloadPoints()
    {
        preloadPoints.Clear();
        if (!preloadNextFloor) return;

        if (floorController == null) floorController = FindAnyObjectByType<FloorController>();
        if (floorController == null) return;

        int current = FloorController.CurrentFloor;
        for (int n = 1; n <= 4; n++)
        {
            Vector2 next;
            if (!floorController.TryGetSpawnPoint(current + 1, n, out next)) continue;

            Vector2 now;
            if (floorController.TryGetSpawnPoint(current, n, out now) && (next - now).sqrMagnitude < 1f) continue;

            bool duplicate = false;
            for (int j = 0; j < preloadPoints.Count; j++)
            {
                if ((preloadPoints[j] - next).sqrMagnitude < 1f) duplicate = true;
            }
            if (!duplicate) preloadPoints.Add(next);
        }
    }

    // ------------------------------------------------------------ main loop

    private void LateUpdate()
    {
        bool everything = false;   // check every entry this frame instead of a slice

        if (!scanned || floorChanged || (rescanInterval > 0f && Time.unscaledTime >= nextRescan))
        {
            if (!scanned || floorChanged) everything = true;
            scanned = true;
            nextRescan = Time.unscaledTime + rescanInterval;
            Scan();
            RefreshPreloadPoints();
        }

        CollectViews();
        CollectFocus();

        // Nobody left to measure from (everyone is down): measure from the cameras instead, so the picture stays put.
        if (playersTracked == 0)
        {
            for (int i = 0; i < viewCount; i++) AddFocusPoint(views[i].centre, 0f);
        }

        AddAltarFocus();
        AddPreloadFocus();

        // No players, no cameras, nothing: show everything rather than hide the world.
        if (focus.Count == 0)
        {
            if (culledCount > 0) ShowAll();
            floorChanged = false;
            Report();
            return;
        }

        int count = entries.Count;
        if (count > 0)
        {
            int slice;
            if (everything)
            {
                slice = count;
                sliceCarry = 0f;
            }
            else
            {
                // Check a slice each frame so one full pass takes about Check Interval seconds.
                sliceCarry = Mathf.Min(sliceCarry + count * Time.unscaledDeltaTime / Mathf.Max(0.02f, checkInterval), count);
                slice = (int)sliceCarry;
                sliceCarry -= slice;
            }
            Process(slice);
        }

        if (warmPending)
        {
            warmPending = false;
            WarmUpNextFloor();
        }

        floorChanged = false;
        Report();
    }

    private void Process(int slice)
    {
        for (int done = 0; done < slice && entries.Count > 0; done++)
        {
            if (cursor >= entries.Count) cursor = 0;

            Entry e = entries[cursor];
            if (e.owner == null)
            {
                // Destroyed: drop it (the last one takes its place, so the cursor stays where it is).
                if (e.culled) culledCount--;
                known.Remove(e.owner);
                int last = entries.Count - 1;
                entries[cursor] = entries[last];
                entries.RemoveAt(last);
                continue;
            }

            if (e.renderer != null) EvaluateRenderer(e);
            else EvaluateSound(e);
            cursor++;
        }
    }

    // ------------------------------------------------------------ deciding

    private void EvaluateRenderer(Entry e)
    {
        Renderer r = e.renderer;

        // Not drawn right now anyway (switched off by its owner, inactive, or no camera draws its layer): leave it alone,
        // and hand back anything we were holding, so the owner's own on / off decides.
        bool usable = r.enabled && r.gameObject.activeInHierarchy && (viewMask & (1 << e.layer)) != 0;
        if (!usable)
        {
            if (e.culled) SetCulled(e, false);
            return;
        }

        Bounds b = r.bounds;
        Vector2 ext = new Vector2(b.extents.x, b.extents.y);
        bool keep = InZone(b.center, ext, !e.culled, true);

        if (keep && e.culled) SetCulled(e, false);
        else if (!keep && !e.culled) SetCulled(e, true);
    }

    private void EvaluateSound(Entry e)
    {
        AudioSource s = e.sound;
        if (!s.isActiveAndEnabled)
        {
            if (e.culled) SetCulled(e, false);
            return;
        }

        bool keep = InZone(s.transform.position, Vector2.zero, !e.culled, false);

        if (keep && e.culled) SetCulled(e, false);
        else if (!keep && !e.culled) SetCulled(e, true);
    }

    private void SetCulled(Entry e, bool culled)
    {
        e.culled = culled;
        culledCount += culled ? 1 : -1;

        if (e.renderer != null)
        {
            e.renderer.forceRenderingOff = culled;
        }
        else if (e.sound != null)
        {
            if (culled)
            {
                e.restoreMute = e.sound.mute;   // hand it back exactly as it was
                e.sound.mute = true;
            }
            else
            {
                e.sound.mute = e.restoreMute;
            }
        }
    }

    private void ShowAll()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (!e.culled) continue;

            if (e.renderer != null) e.renderer.forceRenderingOff = false;
            else if (e.sound != null) e.sound.mute = e.restoreMute;
            e.culled = false;
        }
        culledCount = 0;
    }

    // Is a thing (a box around centre, extents ext) inside an alive area? A culled thing has to come back inside by Hysteresis.
    private bool InZone(Vector3 centre, Vector2 ext, bool wasInside, bool useViews)
    {
        for (int i = 0; i < focus.Count; i++)
        {
            float reach = focus[i].radius > 0f ? focus[i].radius : cullDistance;
            float limit = wasInside ? reach : Mathf.Max(0f, reach - hysteresis);

            float dx = Mathf.Max(Mathf.Abs(centre.x - focus[i].pos.x) - ext.x, 0f);
            float dy = Mathf.Max(Mathf.Abs(centre.y - focus[i].pos.y) - ext.y, 0f);
            if (dx * dx + dy * dy <= limit * limit) return true;
        }

        if (useViews && keepOnScreen) return SeenByAnyView(centre, ext.magnitude);
        return false;
    }

    // ------------------------------------------------------------ the alive areas

    private void AddFocusPoint(Vector2 position, float radius)
    {
        Focus f;
        f.pos = position;
        f.radius = radius;
        focus.Add(f);
    }

    private void AddFocus(Transform t, float radius)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return;
        if (focusSources.Contains(t)) return;   // the same player found twice

        focusSources.Add(t);
        Vector3 p = t.position;
        AddFocusPoint(new Vector2(p.x, p.y), radius);
    }

    // Players first, then CullFocus objects, then Extra Targets.
    private void CollectFocus()
    {
        focusSources.Clear();
        focus.Clear();

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
                AddFocus(ResolvePlayer(n, manual), 0f);
            }
        }

        for (int i = 0; i < CullFocus.All.Count; i++)
        {
            CullFocus f = CullFocus.All[i];
            if (f != null && f.IsLive) AddFocus(f.transform, f.radius);
        }

        if (extraTargets != null)
        {
            for (int i = 0; i < extraTargets.Length; i++) AddFocus(extraTargets[i], 0f);
        }

        playersTracked = focus.Count;
    }

    // Every altar that is not triggered yet and whose floor is the live one stays alive, with its surroundings.
    private void AddAltarFocus()
    {
        altarsWaiting = false;
        for (int i = altars.Count - 1; i >= 0; i--)
        {
            AltarInfo a = altars[i];
            if (a.altar == null)
            {
                altars.RemoveAt(i);
                continue;
            }

            if (!a.altar.isActiveAndEnabled || a.altar.IsTriggered) continue;
            if (a.altar.Room != null && !a.altar.Room.IsFloorActive) continue;   // asleep: the team is on another floor

            altarsWaiting = true;
            if (protectAltars) AddFocusPoint(a.centre, a.radius);
        }
    }

    private void AddPreloadFocus()
    {
        for (int i = 0; i < preloadPoints.Count; i++) AddFocusPoint(preloadPoints[i], preloadDistance);
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
        viewMask = 0;

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
        viewMask |= cam.cullingMask;
        viewCount++;
    }

    // True if a circle of this radius around p is inside, or within Screen Margin of, any camera's view.
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

    // ------------------------------------------------------------ warm-up

    // Draws the landing area of the next floor once, into a tiny hidden picture, so its textures and shaders are already loaded.
    private void WarmUpNextFloor()
    {
        if (!warmUpNextFloor || warmUpFailed || preloadPoints.Count == 0) return;

        int count = Mathf.Min(preloadPoints.Count, 2);
        for (int i = 0; i < count; i++) RenderWarmView(preloadPoints[i]);
    }

    private void RenderWarmView(Vector2 centre)
    {
        GameObject go = null;
        Camera cam = null;
        RenderTexture rt = null;
        try
        {
            go = new GameObject("Culler warm-up camera (temporary)");
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(1f, preloadDistance);
            cam.aspect = 1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = ~0;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            float z = viewCount > 0 && views[0].camera != null ? views[0].camera.transform.position.z : -10f;
            cam.transform.position = new Vector3(centre.x, centre.y, z);

            rt = RenderTexture.GetTemporary(64, 64, 0);
            cam.targetTexture = rt;
            cam.Render();
        }
        catch (System.Exception ex)
        {
            warmUpFailed = true;
            Debug.LogWarning("SpriteDistanceCuller: the next-floor warm-up could not run, so it is switched off (" + ex.Message + "). " +
                             "Untick 'Warm Up Next Floor' to hide this message. Everything else keeps working.", this);
        }
        finally
        {
            if (cam != null) cam.targetTexture = null;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (go != null) Destroy(go);
        }
    }

    // ------------------------------------------------------------ debug

    private void Report()
    {
#if UNITY_EDITOR
        if (Time.unscaledTime < nextReport) return;
        nextReport = Time.unscaledTime + 0.25f;

        status = "Measuring from " + playersTracked + " player(s), " + viewCount + " camera view(s), " + preloadPoints.Count + " next-floor landing spot(s).\n" +
                 "Managed: " + entries.Count + " things, " + culledCount + " culled.\n" +
                 (altarsWaiting ? "An altar is waiting: altars and plate props stay alive." : "No altar is waiting.");
#endif
    }

    // Select this object while playing to see every alive area as an orange circle.
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        for (int i = 0; i < focus.Count; i++)
        {
            float reach = focus[i].radius > 0f ? focus[i].radius : cullDistance;
            Gizmos.DrawWireSphere(new Vector3(focus[i].pos.x, focus[i].pos.y, 0f), reach);
        }
    }
}
