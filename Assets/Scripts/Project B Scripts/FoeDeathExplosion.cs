using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random.

/// <summary>
/// Blows whatever it is on up into little pieces made from ITS OWN colours. Stand-alone: it knows nothing about foes,
/// health or any other script of this project, so the file can be dropped into another project and used on anything with a
/// SpriteRenderer (foes, crates, a boss, the player...).
///
/// Every explosion, each piece:
///   1. picks a RANDOM SPOT on the sprite (any SpriteRenderer on this object or its children, so the whole prefab counts)
///      and takes the colour of the pixel there, so the debris looks like bits of the real sprite;
///   2. starts at that spot and flies to a random point inside a SMALL RADIUS around the middle. A random number picks how
///      far into that radius it goes;
///   3. fades out over its own random time (and a random size), then disappears.
/// One random sound effect (random volume and pitch) plays from the list of clips.
///
/// Use it from code:       float seconds = GetComponent<FoeDeathExplosion>().Explode();
///   Explode() returns how many seconds until every piece is gone, so the caller can remove its object after that long.
/// Use it on a foe:        tick "Use Death Explosion" on the FoeController. FoeController then calls Explode() when the foe
///                         dies, switches the foe's Animator off and ignores its Death Delay, removing the foe when this
///                         script says it is finished. Nothing else to wire up.
/// Try it without dying:   in Play mode, right-click this component's header > "Test Explosion".
///
/// Sprite textures do NOT need Read/Write enabled: if a texture can't be read directly, the colours are read back from the
/// GPU instead. If neither works, the pieces use "Fallback Colour".
/// </summary>
[DisallowMultipleComponent]
public class FoeDeathExplosion : MonoBehaviour
{
    // ------------------------------------------------------------ inspector

    [Header("Where the colours come from")]
    [Tooltip("Leave EMPTY to use every SpriteRenderer on this object and its children (the whole prefab). Drag renderers in " +
             "here to limit it, e.g. only the body and not a shadow sprite.")]
    [SerializeField] private SpriteRenderer[] sourceRenderers;
    [Tooltip("ON: every piece takes the colour of its OWN random spot on the sprite. OFF: all pieces share the colour of the " +
             "first spot picked (a one-colour burst).")]
    [SerializeField] private bool colourPerPiece = true;
    [Tooltip("Spots on the sprite that are more see-through than this are skipped, so pieces never take the colour of empty space.")]
    [SerializeField, Range(0f, 1f)] private float minPixelAlpha = 0.2f;
    [Tooltip("Only used if the sprite's colours can't be read at all.")]
    [SerializeField] private Color fallbackColour = Color.white;

    [Header("Burst")]
    [Tooltip("How many pieces. A random whole number between these two (both included).")]
    [SerializeField] private Vector2Int pieceCount = new Vector2Int(10, 16);
    [Tooltip("The radius of the burst around the middle of the sprite, in PIXELS. Small = a tight pop. No piece ends up further out than this.")]
    [SerializeField, Min(0f)] private float radiusPixels = 26f;
    [Tooltip("How far INTO the radius each piece goes, as a fraction (0 = stays near the middle, 1 = reaches the edge). " +
             "Every piece rolls its own random number between these two.")]
    [SerializeField] private Vector2 travelFraction = new Vector2(0.35f, 1f);
    [Tooltip("Size of a piece in sprite pixels. Every piece rolls its own random size between these two.")]
    [SerializeField] private Vector2 pieceSizePixels = new Vector2(2f, 4f);
    [Tooltip("Pixels per Unity unit of your sprites (Pixel Art Top Down uses 32).")]
    [SerializeField, Min(1f)] private float pixelsPerUnit = 32f;
    [Tooltip("Pieces are drawn on the same sorting layer as the sprite they came from, this many sorting orders above it.")]
    [SerializeField] private int sortingOrderOffset = 1;

    [Header("Fade")]
    [Tooltip("How long each piece lasts before it has fully faded, in SECONDS. Every piece rolls its own random time between these two.")]
    [SerializeField] private Vector2 fadeTime = new Vector2(0.3f, 0.7f);
    [Tooltip("When the fade begins, as a fraction of a piece's life. 0 = fading the whole time, 0.5 = solid for the first half and " +
             "then fading, 1 = no fade (the piece just vanishes at the end).")]
    [SerializeField, Range(0f, 1f)] private float fadeStartsAt = 0.35f;

    [Header("Sound")]
    [Tooltip("Drop your sound effect file(s) here. One is picked at random for every explosion. Leave empty for a silent explosion.")]
    [SerializeField] private AudioClip[] clips;
    [Tooltip("Random volume between these two.")]
    [SerializeField] private Vector2 volume = new Vector2(0.8f, 1f);
    [Tooltip("Random pitch between these two (1 = normal).")]
    [SerializeField] private Vector2 pitch = new Vector2(0.9f, 1.1f);
    [Tooltip("0 = plays at full volume everywhere (right for a 2D game), 1 = fades with distance from the AudioListener.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;
    [Tooltip("Optional. Send the sound through this Audio Mixer group (e.g. your SFX group) so the volume sliders affect it.")]
    [SerializeField] private AudioMixerGroup mixerGroup;

    [Header("Behaviour")]
    [Tooltip("ON: the original sprites are hidden the moment it explodes (the pieces take their place).")]
    [SerializeField] private bool hideBodyOnExplode = true;
    [Tooltip("Leave OFF on foes: FoeController removes the foe itself when the explosion has finished. Tick it only when you use " +
             "this script on something else that should delete itself once the pieces are gone.")]
    [SerializeField] private bool destroyObjectWhenDone = false;

    // ------------------------------------------------------------ public

    /// <summary>True while pieces are still flying / fading.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Blows the object up. Returns the number of seconds until every piece is gone (0 if nothing was spawned), so the
    /// caller knows how long to keep the object around. Safe to call again: it first clears the last explosion.
    /// </summary>
    public float Explode()
    {
        Restore();

        CollectSources();
        Vector3 centre = SourcesCentre();
        float bodyScale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));

        PlaySound(centre);

        List<Piece> pieces = BuildPieces(centre, bodyScale, out float longest);

        if (hideBodyOnExplode)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                SpriteRenderer r = sources[i];
                if (r == null) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }

        if (pieces.Count == 0) return 0f;

        IsPlaying = true;
        routine = StartCoroutine(Animate(pieces, longest));
        return longest + 0.05f;   // a tiny margin so the caller never removes the object a frame before the last piece is gone
    }

    /// <summary>Stops the explosion, deletes the pieces and shows the original sprites again. (For re-using an object.)</summary>
    public void Restore()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        IsPlaying = false;

        if (container != null) Destroy(container);
        container = null;

        for (int i = 0; i < hidden.Count; i++)
        {
            if (hidden[i] != null) hidden[i].enabled = true;
        }
        hidden.Clear();
    }

    [ContextMenu("Test Explosion")]
    private void TestExplosion()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("FoeDeathExplosion: enter Play mode first, then use Test Explosion.", this);
            return;
        }
        Explode();
    }

    // ------------------------------------------------------------ state

    private class Piece
    {
        public Transform t;
        public SpriteRenderer sr;
        public Vector3 from, to;
        public float life;
        public Color colour;
        public bool done;
    }

    // One sprite's pixels (cached, so a foe that dies a hundred times only reads its sprite once).
    private class RawSprite
    {
        public Color[] pixels;
        public int w, h;
        public Vector2 origin;   // where pixel (0,0) of the block sits relative to the sprite's pivot, in pixels
    }

    // A SpriteRenderer plus the list of its pixels that are solid enough to take a colour from.
    private class Source
    {
        public SpriteRenderer r;
        public RawSprite raw;
        public int[] solid;
    }

    /// <summary>Optional. Give it a function that returns true while the game is paused and every explosion in flight holds
    /// still until it returns false. Left empty, explosions only stop when Time.timeScale is 0. (FoeController sets it
    /// for you; this script itself still knows nothing about foes.)</summary>
    public static System.Func<bool> IsPausedCheck;

    private static readonly Dictionary<Sprite, RawSprite> cache = new Dictionary<Sprite, RawSprite>();
    private static Sprite square;

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        square = null;
    }

    private readonly List<SpriteRenderer> sources = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> hidden = new List<SpriteRenderer>();
    private GameObject container;
    private Coroutine routine;

    private void OnDestroy()
    {
        // The pieces live in their own object (not under this one, so the foe's scale can't distort them): clean it up here.
        if (container != null) Destroy(container);
    }

    // ------------------------------------------------------------ building the pieces

    private void CollectSources()
    {
        sources.Clear();

        if (sourceRenderers != null && sourceRenderers.Length > 0)
        {
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                SpriteRenderer r = sourceRenderers[i];
                if (r != null && r.sprite != null) sources.Add(r);
            }
            return;
        }

        foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>(false))
        {
            if (r.sprite == null || !r.enabled) continue;
            if (r.color.a <= 0.01f) continue;
            if (r.maskInteraction == SpriteMaskInteraction.VisibleInsideMask) continue;   // an effect drawn through a mask (e.g. a hit flash), not the body
            sources.Add(r);
        }
    }

    private Vector3 SourcesCentre()
    {
        if (sources.Count == 0) return transform.position;

        Bounds b = sources[0].bounds;
        for (int i = 1; i < sources.Count; i++) b.Encapsulate(sources[i].bounds);
        return b.center;
    }

    private List<Piece> BuildPieces(Vector3 centre, float bodyScale, out float longest)
    {
        longest = 0f;
        var pieces = new List<Piece>();

        // Read the pixels of every source once.
        var usable = new List<Source>();
        int totalSolid = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            Source s = MakeSource(sources[i]);
            if (s == null) continue;
            usable.Add(s);
            totalSolid += s.solid.Length;
        }

        int count = Mathf.Max(0, RollInt(pieceCount));
        if (count == 0) return pieces;

        container = new GameObject("FoeExplosion (pieces)");
        float radiusWorld = radiusPixels / pixelsPerUnit * bodyScale;
        Color sharedColour = Color.clear;
        bool haveShared = false;

        for (int i = 0; i < count; i++)
        {
            // 1. A random spot on the prefab, and the colour of the pixel there.
            Color colour = fallbackColour;
            Vector3 start = centre;
            SpriteRenderer origin = sources.Count > 0 ? sources[0] : null;

            if (usable.Count > 0)
            {
                PickSpot(usable, totalSolid, out SpriteRenderer r, out Color c, out Vector3 world);
                origin = r;
                start = world;
                colour = c;
            }

            if (!colourPerPiece)
            {
                if (!haveShared) { sharedColour = colour; haveShared = true; }
                colour = sharedColour;
            }

            // 2. A random point inside the small radius to fly to. The random fraction decides how far into the radius it goes.
            float angle = Random.value * Mathf.PI * 2f;
            float reach = radiusWorld * Mathf.Clamp01(RollFloat(travelFraction));
            Vector3 end = centre + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * reach;
            end.z = start.z;

            // 3. Its own size and fade time.
            float scale = origin != null ? Mathf.Abs(origin.transform.lossyScale.x) : bodyScale;
            float size = Mathf.Max(0.5f, RollFloat(pieceSizePixels)) / pixelsPerUnit * Mathf.Max(0.0001f, scale);
            float life = Mathf.Max(0.05f, RollFloat(fadeTime));
            longest = Mathf.Max(longest, life);

            var go = new GameObject("piece");
            go.transform.SetParent(container.transform, false);
            go.transform.position = start;
            go.transform.localScale = new Vector3(size, size, 1f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Square();
            sr.color = colour;
            if (origin != null)
            {
                sr.sortingLayerID = origin.sortingLayerID;
                sr.sortingOrder = origin.sortingOrder + sortingOrderOffset;
            }

            pieces.Add(new Piece { t = go.transform, sr = sr, from = start, to = end, life = life, colour = colour });
        }

        return pieces;
    }

    private Source MakeSource(SpriteRenderer r)
    {
        if (r == null || r.sprite == null) return null;

        RawSprite raw = ReadSprite(r.sprite);
        if (raw == null || raw.pixels == null) return null;

        var solid = new List<int>(raw.pixels.Length);
        for (int i = 0; i < raw.pixels.Length; i++)
        {
            if (raw.pixels[i].a >= minPixelAlpha) solid.Add(i);
        }
        if (solid.Count == 0) return null;

        return new Source { r = r, raw = raw, solid = solid.ToArray() };
    }

    // A uniformly random solid pixel across ALL the sources, so a bigger sprite gets proportionally more of the pieces.
    private static void PickSpot(List<Source> usable, int totalSolid, out SpriteRenderer renderer, out Color colour, out Vector3 world)
    {
        int roll = Random.Range(0, totalSolid);
        Source chosen = usable[usable.Count - 1];
        for (int i = 0; i < usable.Count; i++)
        {
            if (roll < usable[i].solid.Length) { chosen = usable[i]; break; }
            roll -= usable[i].solid.Length;
        }

        int index = chosen.solid[Mathf.Clamp(roll, 0, chosen.solid.Length - 1)];
        RawSprite raw = chosen.raw;
        SpriteRenderer r = chosen.r;

        int px = index % raw.w;
        int py = index / raw.w;

        // Pixel -> position relative to the sprite's pivot, in world units, then through the renderer's transform.
        Vector2 localPixels = raw.origin + new Vector2(px + 0.5f, py + 0.5f);
        Vector3 local = new Vector3(localPixels.x, localPixels.y, 0f) / r.sprite.pixelsPerUnit;
        if (r.flipX) local.x = -local.x;
        if (r.flipY) local.y = -local.y;

        renderer = r;
        world = r.transform.TransformPoint(local);
        colour = raw.pixels[index] * r.color;   // the sprite's tint counts too: a frozen (blue) foe bursts into blue pieces
        colour.a = r.color.a;
    }

    // ------------------------------------------------------------ reading a sprite's pixels

    private static RawSprite ReadSprite(Sprite sprite)
    {
        if (cache.TryGetValue(sprite, out RawSprite cached)) return cached;

        RawSprite result = null;
        try
        {
            Texture2D tex = sprite.texture;
            Rect rect = sprite.textureRect;
            int x = Mathf.RoundToInt(rect.x), y = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);

            Color[] pixels = null;
            if (tex.isReadable)
            {
                try { pixels = tex.GetPixels(x, y, w, h); }
                catch { pixels = null; }   // e.g. a compressed format: fall through to the GPU read
            }
            if (pixels == null) pixels = ReadViaGpu(tex, x, y, w, h);

            if (pixels != null)
            {
                result = new RawSprite
                {
                    pixels = pixels,
                    w = w,
                    h = h,
                    origin = sprite.textureRectOffset - sprite.pivot
                };
            }
        }
        catch (System.Exception)
        {
            result = null;
        }

        if (cache.Count > 256) cache.Clear();   // a safety cap; a foe only ever uses a handful of frames
        cache[sprite] = result;
        return result;
    }

    // Works on a texture that does NOT have Read/Write enabled: copy it to a temporary render texture and read that.
    private static Color[] ReadViaGpu(Texture2D tex, int x, int y, int w, int h)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(x, y, w, h), 0, 0);
            return copy.GetPixels();
        }
        catch (System.Exception)
        {
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null) Destroy(copy);
        }
    }

    // A 1x1 white sprite one world unit wide, shared by every piece (each piece scales and tints it).
    private static Sprite Square()
    {
        if (square != null) return square;

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        square = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return square;
    }

    // ------------------------------------------------------------ animation

    private IEnumerator Animate(List<Piece> pieces, float longest)
    {
        float elapsed = 0f;
        while (elapsed < longest)
        {
            // Paused by something that leaves timeScale alone (the stairs menu): hold every piece where it is.
            if (IsPausedCheck != null && IsPausedCheck())
            {
                yield return null;
                continue;
            }

            elapsed += Time.deltaTime;   // scaled time: the pieces also freeze whenever timeScale is 0 (pause menu, notebook)

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                if (p.done) continue;

                float t = Mathf.Clamp01(elapsed / p.life);
                float eased = 1f - (1f - t) * (1f - t);   // fast burst out, then slowing down
                p.t.position = Vector3.LerpUnclamped(p.from, p.to, eased);

                float fade = fadeStartsAt >= 1f ? 1f : 1f - Mathf.Clamp01((t - fadeStartsAt) / (1f - fadeStartsAt));
                Color c = p.colour;
                c.a *= fade;
                p.sr.color = c;

                if (t >= 1f)
                {
                    p.sr.enabled = false;
                    p.done = true;
                }
            }
            yield return null;
        }

        Finish();
    }

    private void Finish()
    {
        IsPlaying = false;
        routine = null;

        if (container != null) Destroy(container);
        container = null;

        if (destroyObjectWhenDone) Destroy(gameObject);
    }

    // ------------------------------------------------------------ sound

    private void PlaySound(Vector3 where)
    {
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        float pitchRoll = Mathf.Max(0.05f, RollFloat(pitch));

        // Its own temporary object, so the sound keeps playing even though the foe is removed a moment later.
        var go = new GameObject("FoeExplosion (sound)");
        go.transform.position = where;

        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = Mathf.Clamp01(RollFloat(volume));
        src.pitch = pitchRoll;
        src.spatialBlend = spatialBlend;
        src.outputAudioMixerGroup = mixerGroup;
        src.playOnAwake = false;
        src.Play();

        Destroy(go, clip.length / pitchRoll + 0.1f);
    }

    // ------------------------------------------------------------ small helpers

    // Random number between two values, whichever order they were typed in.
    private static float RollFloat(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    // Random whole number between two values, BOTH included.
    private static int RollInt(Vector2Int range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1);
    }

    // ------------------------------------------------------------ editor helper

    // Select the object to see the burst radius (orange circle).
    private void OnDrawGizmosSelected()
    {
        float scale = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        Gizmos.color = new Color(1f, 0.55f, 0f);
        Gizmos.DrawWireSphere(transform.position, radiusPixels / pixelsPerUnit * scale);
    }
}
