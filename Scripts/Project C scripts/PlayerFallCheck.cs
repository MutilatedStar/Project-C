using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Ilumisoft.HealthSystem;
using Cainos.PixelArtTopDown_Basic;

/// <summary>
/// Pits: a player whose feet have no GROUND under them falls. The picture shrinks away, the player is killed, their score and health are
/// reset, and they respawn at their spawn spot (all of that is CoopDowned.BeginFall, which this script starts).
///
/// WHAT IS GROUND: a tile under the player's feet (the middle of their solid collider). Three ways to say which tiles count, all added together:
///   Ground Sorting Layers   names of sorting layers. A tile on a Tilemap drawn on one of these layers is ground (your background / floor layer).
///                           Add as many as you like.
///   Ground Tilemaps         Tilemaps that are ground whatever layer they are on.
///   Any Grid Tile Counts    the BASIC check, ON by default: a tile on ANY Tilemap in the scene's Grid is ground, so a player only falls where
///                           there is no tile at all. Untick it once the layers above are filled in, so walls, props and other tilemaps on
///                           other layers don't count as floor.
///   Ground Mask             optional, for floors made of prefabs: colliders (a trigger is fine) on these physics layers are ground.
/// Lava, water and spike tiles are tiles, so they are ground too (FloorManager still burns the player there).
/// If NOTHING can count as ground (no Tilemaps, no layers, no mask) the check has no opinion and nobody falls; the Console says so.
///
/// HOW FAST: there is no waiting. The fall starts the same frame the feet are over nothing, and the path walked since the last frame is checked too
/// (every half tile), so sprinting can't skip over a gap, however narrow. A player who was carried past a gap in one step is put back on it.
///
/// WHEN THEY DO NOT FALL: while jumping (jump over the gap!), while standing on something they jumped onto, while knocked down or falling
/// already, behind a menu / pause / black screen, for a moment after a floor change, and for a player who doesn't count (Single Player Mode).
///
/// SET UP: add this to the player prefab(s) next to CoopHealth and CoopDowned. With the defaults it works as is.
/// TUNING: in Play mode, the three dots of this component > "Log What Is Under My Feet" lists every Tilemap under the player, its sorting layer,
/// and whether it counts as ground, so the right layer name is easy to find.
/// Needs CoopHealth.cs (ForceKill) and the updated CoopDowned.cs (BeginFall).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CoopHealth), typeof(CoopDowned))]
public class PlayerFallCheck : MonoBehaviour
{
    [Header("What counts as ground")]
    [Tooltip("Sorting layer NAMES (Project Settings > Tags and Layers). A tile on a Tilemap drawn on one of these layers is ground. Add your background / floor " +
             "layer, and any others a player may stand on. A name that does not exist is reported and skipped.")]
    [SerializeField] private string[] groundSortingLayers = new string[0];
    [Tooltip("Tilemaps that are ground whatever sorting layer they are on. Drag them in one by one.")]
    [SerializeField] private Tilemap[] groundTilemaps;
    [Tooltip("The basic check. ON: a tile on ANY Tilemap in the scene counts as ground, so the player only falls where there is no tile at all. " +
             "OFF: only the layers and Tilemaps listed above count (use this once they are filled in).")]
    [SerializeField] private bool anyGridTileCounts = true;
    [Tooltip("OPTIONAL, for floors built from prefabs: colliders on these physics layers count as ground (a trigger collider is fine). Nothing = off.")]
    [SerializeField] private LayerMask groundMask = 0;

    [Header("When the player falls")]
    [Tooltip("Where the ground is read, as an offset from the middle of the player's solid collider (world units). Nudge it until it sits at their feet.")]
    [SerializeField] private Vector2 feetOffset = Vector2.zero;
    [Tooltip("Seconds after the player appears (and after every floor change) when no fall can start, while the floor's tiles and the player settle.")]
    [SerializeField, Min(0f)] private float settleSeconds = 0.75f;
    [Tooltip("Prints a Console line every time a player falls, with where, so a fall in the wrong place can be traced.")]
    [SerializeField] private bool logFalls = true;

    private CoopHealth health;
    private CoopDowned downed;
    private TopDownJump jump;
    private Rigidbody2D rb;
    private Collider2D body;

    private readonly List<int> layerIds = new List<int>();
    private readonly List<Tilemap> maps = new List<Tilemap>();   // the Tilemaps that count as ground right now
    private readonly Collider2D[] pointBuffer = new Collider2D[8];
    private ContactFilter2D maskFilter;

    // The path walked since the last frame is checked too, so a sprint can't step over a gap between two frames.
    private const float MaxSweepDistance = 3f;   // a bigger step than this is a teleport (respawn, floor change), not a walk
    private const int MaxSweepSteps = 40;
    private const float DefaultStep = 0.25f;
    private Vector2 lastFeet;
    private bool hasLast;
    private float stepSize = DefaultStep;        // world units between checks along that path: half a tile

    private float ignoreUntil;
    private float nextRefresh;
    private float startedAt;
    private float lastFall = -100f;
    private bool hasOpinion;
    private bool warnedNoGround;
    private bool warnedLoop;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        // A copy on a CHILD of a player is part of that player, not a player of its own.
        if (CoopHealth.IsNestedInPlayer(this))
        {
            Debug.LogWarning("PlayerFallCheck on '" + name + "' sits inside another player, so it is switched off and does nothing. You can remove it from '" + name + "'.", this);
            enabled = false;
            return;
        }

        health = GetComponent<CoopHealth>();
        downed = GetComponent<CoopDowned>();
        jump = GetComponent<TopDownJump>();
        rb = GetComponent<Rigidbody2D>();

        // The body is the first solid collider, the same one TopDownJump and FloorManager read the feet from.
        Collider2D[] all = GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && !all[i].isTrigger)
            {
                body = all[i];
                break;
            }
        }

        // Sorting layer names are turned into ids once, so the check never compares strings.
        int count = groundSortingLayers != null ? groundSortingLayers.Length : 0;
        for (int i = 0; i < count; i++)
        {
            if (string.IsNullOrEmpty(groundSortingLayers[i])) continue;

            int id;
            if (TopDownJump.TryGetSortingLayerId(groundSortingLayers[i], out id))
            {
                layerIds.Add(id);
                continue;
            }

            Debug.LogWarning("PlayerFallCheck on '" + name + "': there is no sorting layer called \"" + groundSortingLayers[i] + "\" " +
                             "(Project Settings > Tags and Layers). That entry is ignored.", this);
        }

        maskFilter = new ContactFilter2D();
        maskFilter.SetLayerMask(groundMask);
        maskFilter.useTriggers = true;
    }

    private void OnEnable()
    {
        FloorController.FloorChanged += HandleFloorChanged;
        ignoreUntil = Time.time + settleSeconds;
        startedAt = Time.unscaledTime;
        nextRefresh = 0f;
        hasLast = false;
    }

    private void OnDisable()
    {
        FloorController.FloorChanged -= HandleFloorChanged;
    }

    // The players were moved and the new floor's tilemaps went live: wait a moment, then look the tilemaps up again.
    private void HandleFloorChanged(int floor)
    {
        ignoreUntil = Time.time + settleSeconds;
        nextRefresh = 0f;
        hasLast = false;
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        if (!CanCheck())
        {
            hasLast = false;   // after a jump, a respawn or a floor change the next check starts fresh, not from the old spot
            return;
        }

        if (Time.unscaledTime >= nextRefresh) RefreshGround();

        if (!hasOpinion)
        {
            WarnNothingCounts();
            hasLast = false;
            return;
        }

        Vector2 feet = FeetPoint();
        Vector2 gap;
        bool fell = FindGap(hasLast ? lastFeet : feet, feet, out gap);

        lastFeet = feet;
        hasLast = true;

        // No waiting: the fall starts the very frame the feet are over nothing.
        if (fell)
        {
            hasLast = false;
            StartFall(feet, gap);
        }
    }

    // Only a player who is up, alive, in the game and standing on the ground can fall.
    private bool CanCheck()
    {
        if (Time.time < ignoreUntil) return false;
        if (Time.timeScale <= 0f || FloorController.BlocksInput) return false;   // pause, menus, black screens

        if (health == null || !health.IsAlive || downed.IsOut) return false;
        if (!CoopRules.Counts(health.PlayerNumber)) return false;
        if (rb != null && !rb.simulated) return false;

        // In the air the player flies over the pit, and on top of something they jumped onto the ground below is not what holds them up.
        if (jump != null && (jump.IsAirborne || jump.Level > 0)) return false;

        return true;
    }

    private Vector2 FeetPoint()
    {
        Vector2 centre = body != null ? (Vector2)body.bounds.center : (Vector2)transform.position;
        return centre + feetOffset;
    }

    // ------------------------------------------------------------ is there ground?

    // Once a second (and after a floor change): which Tilemaps in the scene count as ground. Tilemaps of a floor that is switched off are not found.
    private void RefreshGround()
    {
        nextRefresh = Time.unscaledTime + 1f;
        maps.Clear();

        if (groundTilemaps != null)
        {
            for (int i = 0; i < groundTilemaps.Length; i++)
            {
                Tilemap listed = groundTilemaps[i];
                if (listed != null && !maps.Contains(listed)) maps.Add(listed);
            }
        }

        if (anyGridTileCounts || layerIds.Count > 0)
        {
            Tilemap[] all = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Tilemap map = all[i];
                if (map == null || maps.Contains(map)) continue;

                if (anyGridTileCounts)
                {
                    maps.Add(map);
                    continue;
                }

                TilemapRenderer drawn = map.GetComponent<TilemapRenderer>();
                if (drawn != null && layerIds.Contains(drawn.sortingLayerID)) maps.Add(map);
            }
        }

        hasOpinion = maps.Count > 0 || groundMask.value != 0;

        // Half a tile between checks along the walked path, so no one-tile gap can fall between two of them.
        float cell = float.MaxValue;
        for (int i = 0; i < maps.Count; i++)
        {
            Tilemap map = maps[i];
            if (map == null) continue;

            Vector3 scale = map.transform.lossyScale;
            float w = Mathf.Abs(map.cellSize.x * scale.x);
            float h = Mathf.Abs(map.cellSize.y * scale.y);
            float smallest = Mathf.Min(w, h);
            if (smallest > 0.0001f) cell = Mathf.Min(cell, smallest);
        }
        stepSize = cell < float.MaxValue ? Mathf.Clamp(cell * 0.5f, 0.05f, 0.5f) : DefaultStep;
    }

    // Looks for ground-less spots from where the feet were last frame to where they are now (sprinting can cover most of a tile in one frame).
    // Returns the first spot without ground along that path.
    private bool FindGap(Vector2 from, Vector2 to, out Vector2 gap)
    {
        gap = to;

        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length > MaxSweepDistance)
        {
            // Too far to have been walked: the player was moved (respawn, floor change), so only where they are now counts.
            from = to;
            length = 0f;
        }

        if (!IsGround(to)) return true;   // the end of the path first: the cheapest check, and by far the most common

        int steps = Mathf.Min(MaxSweepSteps, Mathf.CeilToInt(length / stepSize));
        for (int i = 1; i < steps; i++)
        {
            Vector2 point = from + delta * ((float)i / steps);
            if (IsGround(point))
            {
                continue;
            }

            gap = point;
            return true;
        }

        return false;
    }

    private bool IsGround(Vector2 point)
    {
        for (int i = 0; i < maps.Count; i++)
        {
            Tilemap map = maps[i];
            if (map == null) continue;

            Vector3Int cell = map.WorldToCell(point);
            cell.z = 0;   // a tilemap that sits at a different Z would otherwise look one layer off and find nothing
            if (map.HasTile(cell)) return true;
        }

        return groundMask.value != 0 && OnMaskLayer(point);
    }

    private bool OnMaskLayer(Vector2 point)
    {
        int count = Physics2D.OverlapPoint(point, maskFilter, pointBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D c = pointBuffer[i];
            if (c != null && !c.transform.IsChildOf(transform)) return true;   // the player's own colliders are not the floor
        }
        return false;
    }

    // ------------------------------------------------------------ falling

    private void StartFall(Vector2 feet, Vector2 gap)
    {
        if (!downed.BeginFall()) return;

        // A fast step can carry the player past a gap before this frame's check sees it. Put them back ON the gap, so they fall into the hole
        // and not off the far side of it. (The body is already switched off by BeginFall, the same way CoopDowned teleports it.)
        Vector2 shift = gap - feet;
        if (shift.sqrMagnitude > 0.0001f)
        {
            Vector3 moved = transform.position + (Vector3)shift;
            transform.position = moved;
            if (rb != null) rb.position = moved;
        }

        if (logFalls)
        {
            Debug.Log("PlayerFallCheck: Player " + health.PlayerNumber + " has no ground under " + gap + " and falls. " +
                      "(Play mode: the three dots of this component > Log What Is Under My Feet shows what the check sees.)", this);
        }

        // Falling again soon after the last respawn usually means the respawn spot itself has no ground, and the player would fall for ever.
        if (!warnedLoop && Time.time - lastFall < 3f)
        {
            warnedLoop = true;
            Debug.LogWarning("PlayerFallCheck on '" + name + "' (Player " + health.PlayerNumber + "): this is the second fall within 3 seconds, so the spot the player " +
                             "comes back at may have no ground under it. Check the player's starting position and any CoopSpawnPoint, or the ground settings here.", this);
        }
        lastFall = Time.time;
    }

    // Nothing is set up to count as ground, so no player can ever fall. Say so once, a few seconds after the start (floors may still be loading).
    private void WarnNothingCounts()
    {
        if (warnedNoGround || Time.unscaledTime - startedAt < 3f) return;
        warnedNoGround = true;

        Debug.LogWarning("PlayerFallCheck on '" + name + "': nothing counts as ground, so nobody can fall. There is no Tilemap in the scene (or none on a listed " +
                         "sorting layer / in Ground Tilemaps), and Ground Mask is empty. Fix: put the floor on a Tilemap, tick Any Grid Tile Counts, or fill in the " +
                         "ground settings.", this);
    }

    // ------------------------------------------------------------ tuning help

    [ContextMenu("Log What Is Under My Feet")]
    private void LogUnderFeet()
    {
        if (!Application.isPlaying || health == null) return;   // needs the player running

        RefreshGround();
        Vector2 feet = FeetPoint();

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("PlayerFallCheck on '" + name + "' (Player " + health.PlayerNumber + "): feet at " + feet + ". Tilemaps with a tile there: ");

        int found = 0;
        Tilemap[] all = FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Tilemap map = all[i];
            Vector3Int cell = map.WorldToCell(feet);
            cell.z = 0;
            if (!map.HasTile(cell)) continue;

            TilemapRenderer drawn = map.GetComponent<TilemapRenderer>();
            string layer = drawn != null ? SortingLayer.IDToName(drawn.sortingLayerID) : "(no renderer)";
            sb.Append("['" + map.name + "', sorting layer '" + layer + "', " + (maps.Contains(map) ? "counts as ground" : "does NOT count as ground") + "] ");
            found++;
        }

        if (found == 0) sb.Append("none. ");
        sb.Append("Ground right now: " + (IsGround(feet) ? "yes." : "NO, the player would fall."));
        Debug.Log(sb.ToString(), this);
    }
}
