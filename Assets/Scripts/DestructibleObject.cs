using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Tilemaps;

/// <summary>
/// Something the sword breaks in ONE hit. Needs the updated SwordController, which calls HitByBlade() when a swing or
/// spin touches it. It works in two ways, and picks the right one by itself:
///
///   A normal object (a crate, barrel or pot that is its own sprite with a normal, non-trigger Collider2D):
///       put this on the object itself. The whole object breaks and gives Default Score.
///
///   A Tilemap (crates drawn as tiles on a Tilemap that has a TilemapCollider2D):
///       put this on the Tilemap's object. ONLY THE TILE THE BLADE TOUCHES breaks, never the whole Tilemap.
///       Which tiles may break:
///         - the ones listed under Breakable Tiles. A tile matches on the name of its Tile asset OR the name of the
///           Sprite it shows (the same way the stairs are matched), and each entry has its own score.
///         - every tile on the Tilemap, if Break Any Tile is ticked (they give Default Score).
///
/// Score: the default is 30 for a whole object, a listed tile and Break Any Tile alike (each can be changed).
/// It is sent through FoeController.AwardPoints, the same score event that foe kills use, so the score manager you
/// already have adds it. DestructibleObject.OnScored also fires with the points, if you would rather listen to that.
///
/// Solid: at the start of the game the object (and its children) is put on the layer named below, so the player can't
/// walk through it. Make sure that layer collides with the player's layer (Edit > Project Settings > Physics 2D >
/// Layer Collision Matrix, where every box is ticked by default) and that SwordController's Foe Mask includes it
/// (its default, Everything, does). NOTE: on a Tilemap this moves the WHOLE Tilemap to that layer. If the Tilemap also
/// holds walls you do not want moved, empty Solid Layer Name and set the layer by hand.
///
/// When something breaks it vanishes and stops blocking straight away, fires On Broken, and (a whole object) is destroyed.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DestructibleObject : MonoBehaviour
{
    /// <summary>One kind of tile that may break, and what it is worth.</summary>
    [System.Serializable]
    public class BreakableTile
    {
        [Tooltip("The tile's name: the name of its Tile asset OR of the Sprite it shows (capitals, spaces and underscores are ignored).")]
        public string tileName = "0x72_16x16DungeonTileset.v5 1_173";
        [Tooltip("Score for breaking one of these tiles.")]
        [Min(0)] public int score = 30;
    }

    // ------------------------------------------------------------ inspector

    [Header("Breaking")]
    [Tooltip("The layer this object is moved to when the game starts, so it blocks the player. Must match the layer's name in the Layers list exactly (it is case-sensitive). Leave EMPTY to keep the layer you set by hand.")]
    [SerializeField] private string solidLayerName = "objects";

    [Tooltip("Seconds before a broken whole object is destroyed. It is hidden and stops blocking immediately; a delay is only useful if a sound or effect that is on this object still needs to finish. (Tiles just disappear.)")]
    [SerializeField, Min(0f)] private float destroyDelay = 0f;

    [Tooltip("Runs when something breaks: once for a whole object, once for EACH tile on a Tilemap. Hook up a sound, a particle effect, a drop...")]
    public UnityEvent onBroken;

    [Header("Score")]
    [Tooltip("Score for breaking a whole object, and for any tile when Break Any Tile is ticked. 0 = no score.")]
    [SerializeField, Min(0)] private int defaultScore = 30;
    [Tooltip("ON: the score is sent through FoeController's score event (the one foe kills use), so the score manager you already have adds it. Note that anything else listening to that event also hears it. OFF: only DestructibleObject.OnScored fires.")]
    [SerializeField] private bool scoreThroughFoeEvent = true;

    [Header("Tilemap mode (only when this object is a Tilemap)")]
    [Tooltip("Tiles that may break, each with its own score. A tile matches on the name of its Tile asset OR the name of the Sprite it shows.")]
    [SerializeField] private BreakableTile[] breakableTiles = { new BreakableTile() };
    [Tooltip("ON: EVERY tile on this Tilemap can break, one at a time, for Default Score (use it for a Tilemap that holds only crates). Tiles named in the list above keep their own score. OFF: only the listed tiles break.")]
    [SerializeField] private bool breakAnyTile = false;
    [Tooltip("ON: the whole name must match (capitals, spaces and underscores ignored). OFF: a tile whose name merely CONTAINS the listed name also matches. Leave ON for numbered names, or '..._17' would also catch '..._173'.")]
    [SerializeField] private bool exactNameMatch = true;

    // ------------------------------------------------------------ public

    /// <summary>True once a whole object has been broken. (Stays false on a Tilemap: its tiles break one by one.)</summary>
    public bool IsBroken { get; private set; }

    /// <summary>Fires with the points every time something here breaks. Subscribe a score display or counter to it.</summary>
    public static event System.Action<int> OnScored;

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnScored = null;
    }

    // ------------------------------------------------------------ state

    // (tile asset, sprite it shows) -> score, or -1 when that tile may not break. Saves re-reading names every frame of a swing.
    private readonly System.Collections.Generic.Dictionary<(TileBase, Sprite), int> scoreCache =
        new System.Collections.Generic.Dictionary<(TileBase, Sprite), int>();

    private void OnValidate()
    {
        scoreCache.Clear();   // the lists were edited: forget what was worked out
    }

    private void Awake()
    {
        if (!string.IsNullOrEmpty(solidLayerName))
        {
            int layer = LayerMask.NameToLayer(solidLayerName);
            if (layer >= 0)
            {
                foreach (Transform t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            }
            else
            {
                Debug.LogWarning("DestructibleObject on '" + name + "': there is no layer called '" + solidLayerName +
                                 "'. Add it under Layers, or fix the spelling in Solid Layer Name. The object stays on its current layer.", this);
            }
        }

        bool anySolid = false;
        int solidObjects = 0;
        foreach (Collider2D c in GetComponentsInChildren<Collider2D>())
        {
            if (c.isTrigger) continue;
            anySolid = true;
            if (c.gameObject != gameObject) solidObjects++;
        }
        if (!anySolid)
        {
            Debug.LogWarning("DestructibleObject on '" + name + "': every collider is a trigger, so the player can walk straight through " +
                             "it and the sword can't see it. Untick Is Trigger.", this);
        }

        // A whole-object breakable that holds several separate solid children breaks ALL of them with one hit.
        if (GetComponent<Tilemap>() == null && solidObjects >= 2)
        {
            Debug.LogWarning("DestructibleObject on '" + name + "': this object holds " + solidObjects + " separate solid child objects, and " +
                             "hitting any one of them breaks ALL of them. If they are separate crates, take this component off the parent and " +
                             "put one DestructibleObject on each crate. If they are tiles, use a Tilemap with a TilemapCollider2D and put it there.", this);
        }
    }

    // ------------------------------------------------------------ hit by the sword

    /// <summary>
    /// Called by SwordController for every collider of this object that is inside the sword's reach.
    /// "insideSweep" says whether a world point lies inside the part of the arc the blade is passing over right now.
    /// Returns how many things broke (a whole object counts as 1).
    /// </summary>
    public int HitByBlade(Collider2D touched, Vector2 origin, float reach, System.Func<Vector2, bool> insideSweep)
    {
        if (IsBroken || touched == null || insideSweep == null) return 0;

        // A Tilemap (its TilemapCollider2D / CompositeCollider2D sits on the same object as the Tilemap): break only the tiles the blade reaches.
        Tilemap map = touched.GetComponent<Tilemap>();
        if (map != null) return BreakTilesInSweep(map, origin, reach, insideSweep);

        // A normal object: it breaks when the blade passes over the closest point of the collider that was touched.
        if (insideSweep(touched.ClosestPoint(origin)))
        {
            Break();
            return 1;
        }
        return 0;
    }

    /// <summary>Older call: breaks the whole object. (On a Tilemap that would be the whole Tilemap, so the sword no longer uses it.)</summary>
    public void Hit()
    {
        Break();
    }

    /// <summary>Breaks the whole object right now (also usable from other scripts or a UnityEvent). On a Tilemap this removes the ENTIRE Tilemap.</summary>
    public void Break()
    {
        if (IsBroken) return;
        IsBroken = true;

        foreach (Collider2D c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        Award(defaultScore);
        onBroken.Invoke();
        Destroy(gameObject, destroyDelay);
    }

    // ------------------------------------------------------------ tiles

    // Breaks every allowed tile whose nearest point to the sword's hand is within reach AND inside the arc being swept.
    private int BreakTilesInSweep(Tilemap map, Vector2 origin, float reach, System.Func<Vector2, bool> insideSweep)
    {
        BoundsInt used = map.cellBounds;
        if (used.size.x <= 0 || used.size.y <= 0) return 0;

        // Only the cells around the sword are looked at.
        Vector3Int a = map.WorldToCell(origin - Vector2.one * reach);
        Vector3Int b = map.WorldToCell(origin + Vector2.one * reach);
        int xMin = Mathf.Max(Mathf.Min(a.x, b.x), used.xMin);
        int xMax = Mathf.Min(Mathf.Max(a.x, b.x), used.xMax - 1);
        int yMin = Mathf.Max(Mathf.Min(a.y, b.y), used.yMin);
        int yMax = Mathf.Min(Mathf.Max(a.y, b.y), used.yMax - 1);

        float reachSq = reach * reach;
        int broken = 0;

        for (int x = xMin; x <= xMax; x++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                Vector3Int cell = new Vector3Int(x, y, 0);
                TileBase tile = map.GetTile(cell);
                if (tile == null) continue;

                if (!TryGetTileScore(tile, map.GetSprite(cell), out int score)) continue;

                // The corner-to-corner box of this cell, and the point of it nearest the sword's hand.
                Vector2 c1 = map.CellToWorld(cell);
                Vector2 c2 = map.CellToWorld(new Vector3Int(x + 1, y + 1, 0));
                Vector2 closest = new Vector2(
                    Mathf.Clamp(origin.x, Mathf.Min(c1.x, c2.x), Mathf.Max(c1.x, c2.x)),
                    Mathf.Clamp(origin.y, Mathf.Min(c1.y, c2.y), Mathf.Max(c1.y, c2.y)));

                float distanceSq = (closest - origin).sqrMagnitude;
                if (distanceSq > reachSq) continue;      // out of reach
                if (distanceSq < 0.0025f) continue;      // the hand is inside this cell: it is under the player, not in front of the blade
                if (!insideSweep(closest)) continue;     // not in the part of the arc the blade is passing over

                map.SetTile(cell, null);                 // only THIS tile goes; the Tilemap's collider updates by itself
                broken++;
                Award(score);
                onBroken.Invoke();
            }
        }
        return broken;
    }

    // Looks the tile up in the list (by Tile asset name or Sprite name); falls back to Break Any Tile. Cached per tile + sprite.
    private bool TryGetTileScore(TileBase tile, Sprite sprite, out int score)
    {
        var key = (tile, sprite);
        if (!scoreCache.TryGetValue(key, out score))
        {
            score = WorkOutTileScore(tile, sprite);
            scoreCache[key] = score;
        }
        return score >= 0;
    }

    private int WorkOutTileScore(TileBase tile, Sprite sprite)
    {
        if (breakableTiles != null)
        {
            for (int i = 0; i < breakableTiles.Length; i++)
            {
                BreakableTile entry = breakableTiles[i];
                if (entry == null) continue;

                if (NameMatches(tile.name, entry.tileName) || (sprite != null && NameMatches(sprite.name, entry.tileName)))
                {
                    return entry.score;
                }
            }
        }
        return breakAnyTile ? defaultScore : -1;
    }

    // Capitals, spaces, underscores and dashes are ignored: "0x72_16x16DungeonTileset.v5 1_173" and "0x72 16x16 dungeontileset.v5 1 173" are the same.
    private bool NameMatches(string candidate, string wanted)
    {
        string a = Normalize(candidate);
        string b = Normalize(wanted);
        if (a.Length == 0 || b.Length == 0) return false;
        return exactNameMatch ? a == b : a.Contains(b);
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i])) sb.Append(char.ToLowerInvariant(text[i]));
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------ score

    private void Award(int points)
    {
        if (points <= 0) return;

        OnScored?.Invoke(points);
        if (scoreThroughFoeEvent) FoeController.AwardPoints(points);
    }
}
