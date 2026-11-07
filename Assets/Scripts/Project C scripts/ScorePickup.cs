using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Ilumisoft.HealthSystem;

/// <summary>
/// A gem (or coin, or anything worth points). A player who touches it adds its points to THEIR score (CoopScore), and the gem is hidden
/// straight away. The colours are FloorDoor's and PlayerGlow's, so a Player 1 gem is blue like Player 1.
///
/// WHO CAN TAKE IT (set per gem, so every gem can be different):
///   Everyone   any player can take it. It GLOWS, starting yellow and melting into a rainbow (see GLOW below), and the points go to
///              whoever took it.
///   Player 1-4 only that player can take it. It is tinted in that player's colour, so everybody can see whose it is. (No glow.)
///   If the gem's player is not in the game (a single player game and a Player 2 gem), anyone may take it instead, so it can't get
///   stuck there. Untick Anyone If Player Missing to keep it for that player only.
///
/// GLOW (gems anyone can take): pick a Glow Style on the gem. Every style starts yellow (Everyone Color) and moves into rainbow colours.
///   Breathing     the glow swells and fades like a slow breath. Each breath peaks in a different rainbow colour.
///   Rainbow       the whole gem and its halo cycle round the colour wheel.
///   Ripple        rings of light spread out from the gem, turning from yellow to rainbow as they travel (Wave / Ripple).
///   Gradient      layered halo: yellow in the middle, rainbow towards the edge, flowing outwards.
///   Starlight     little four-point stars twinkle around and over the gem.
///   Rainbow Wave  a ring of glowing beads around the gem; a wave of size and rainbow colour runs round them.
///   Random        each gem picks one of the styles above for itself.
///   None          no animation: the gem just shows its Everyone Color like before.
///   The glow is made from small soft pictures the script draws itself, so no extra art is needed. Glow Material is optional
///   (an additive sprite material gives a brighter shine).
///
/// POINTS: Points, 100 by default. Give a big gem 500, a tiny one 10, whatever you like.
///
/// SET UP:
///   1. Put this script on the gem prefab, with a Collider 2D (Is Trigger ticked) sized to where a player can grab it.
///   2. Pick Who Can Collect It and Points on the gem. (For a trigger to fire, the player or the gem needs a Rigidbody 2D. Players normally
///      have one for movement; if a gem is never taken, check that first.)
///   3. (Optional) drag the gem's sprite(s) into Sprites. Left empty, every SpriteRenderer under the gem is tinted.
///   4. (Optional) On Collected: hook up a sound or a sparkle. It gets the number of the player who took the gem.
///
/// THE SCORE: the points go to CoopScore.Add(points, playerNumber), the per-player score kept in CoopRules.cs, so the player who took the
/// gem is the one who earns them. The co-op rules then decide what everyone SEES (Shared Score on = the team total, off = their own), the
/// same as for points from kills. Gem points are fixed: they are not scaled by difficulty the way kill points are.
/// (ScoreController, the older single team score, is only touched if you tick Also Add To Score Controller.)
/// Dead players, and players that don't count in the current co-op rules (CoopRules.Counts), can't pick anything up.
/// </summary>
[DisallowMultipleComponent]
public class ScorePickup : MonoBehaviour
{
    /// <summary>Who may take the gem. The numbers match the player numbers (0 = everyone).</summary>
    public enum Collector { Everyone = 0, Player1 = 1, Player2 = 2, Player3 = 3, Player4 = 4 }

    /// <summary>The glow of a gem anyone can take. Every style starts yellow and moves into rainbow colours.</summary>
    public enum GlowStyle
    {
        None = 0,
        Breathing = 1,
        Rainbow = 2,
        Ripple = 3,
        Gradient = 4,
        Starlight = 5,
        RainbowWave = 6,
        Random = 7
    }

    private const int MaxPlayers = 4;
    private const float GradientStep = 0.18f;   // hue gap between the layers of the Gradient style
    private const float WaveSpeed = 0.7f;       // how fast the Rainbow Wave runs round its ring (laps per second)

    [Header("Who and how much")]
    [Tooltip("Everyone: any player can take it and it glows. Player 1 to 4: only that player can take it, and it is tinted in their colour.")]
    [SerializeField] private Collector whoCanCollectIt = Collector.Everyone;
    [Tooltip("The score the player who takes it gets. 100 by default; make it bigger for a big gem.")]
    [SerializeField, Min(0)] private int points = 100;
    [Tooltip("On: when this gem's player is not in the game (a single player game), anyone can take it, so it can't get stuck there. Off: it stays for that player only.")]
    [SerializeField] private bool anyoneIfPlayerMissing = true;
    [Tooltip("Also add the points to ScoreController, the separate single team score (the one its TrySpend spends). Leave OFF if your score text and shop use CoopScore.")]
    [SerializeField] private bool alsoAddToScoreController = false;

    [Header("Colours")]
    [Tooltip("The pictures to tint. Leave EMPTY to tint every SpriteRenderer under this gem. Fill it in to leave a shine or shadow untinted.")]
    [SerializeField] private SpriteRenderer[] sprites;
    [SerializeField] private Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    [SerializeField] private Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    [SerializeField] private Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    [SerializeField] private Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)
    [Tooltip("How strongly a player's colour covers the gem's own picture. 1 = fully the player's colour, 0 = no tint.")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.8f;
    [Tooltip("The colour a gem anyone can take starts with (yellow). The glow begins here and melts into the rainbow. Every gem has its own.")]
    [SerializeField] private Color everyoneColor = new Color(1f, 0.85f, 0.25f, 1f);    // gold
    [Tooltip("How strongly the glow colour covers the gem's own picture. 0 = leave the gem's own picture alone and only show the glow around it.")]
    [SerializeField, Range(0f, 1f)] private float everyoneTintStrength = 0.8f;

    [Header("Glow (gems anyone can take)")]
    [Tooltip("The glow effect. Breathing, Rainbow, Ripple (wave), Gradient, Starlight or Rainbow Wave. Random lets each gem pick its own. None = no animation.")]
    [SerializeField] private GlowStyle glowStyle = GlowStyle.Breathing;
    [Tooltip("How bright the glow is. 0 = invisible, 1 = strongest.")]
    [SerializeField, Range(0f, 1f)] private float glowStrength = 0.7f;
    [Tooltip("How far the glow reaches, as a multiple of the gem's size. 2 = the glow is twice as wide as the gem.")]
    [SerializeField, Range(1f, 4f)] private float glowSize = 2f;
    [Tooltip("Seconds a new gem takes to melt from yellow into the rainbow. 0 = straight into the rainbow.")]
    [SerializeField, Min(0f)] private float fadeToRainbowSeconds = 2f;
    [Tooltip("How fast the colours travel round the rainbow, in full trips per second. 0.35 = one trip every 3 seconds.")]
    [SerializeField, Min(0f)] private float rainbowSpeed = 0.35f;
    [Tooltip("How rich the rainbow colours are. 1 = full colour, 0 = white.")]
    [SerializeField, Range(0f, 1f)] private float rainbowSaturation = 0.9f;
    [Tooltip("Breathing: seconds for one full breath (glow up and back down).")]
    [SerializeField, Min(0.2f)] private float breathSeconds = 2.5f;
    [Tooltip("Ripple: seconds one ring takes to spread from the gem to the edge of the glow.")]
    [SerializeField, Min(0.2f)] private float rippleSeconds = 1.6f;
    [Tooltip("Ripple: how many rings are spreading at once.")]
    [SerializeField, Range(1, 5)] private int rippleRings = 3;
    [Tooltip("Starlight: how many stars twinkle around the gem.")]
    [SerializeField, Range(0, 24)] private int starCount = 8;
    [Tooltip("Rainbow Wave: how many glowing beads make the ring round the gem.")]
    [SerializeField, Range(3, 16)] private int waveOrbs = 10;
    [Tooltip("Optional. A material for the glow pictures, for example a sprite material with additive blending for a brighter shine. Empty = the normal sprite material.")]
    [SerializeField] private Material glowMaterial;

    [Header("When it is taken")]
    [Tooltip("The gem is hidden the moment it is taken. This is how many seconds later the object is removed (lets a sound finish playing).")]
    [SerializeField, Min(0f)] private float destroyAfter = 1f;
    [Tooltip("Runs when a player takes the gem. The number is the player who took it (1 = Player 1...). Hook up a sound or a sparkle here.")]
    [SerializeField] private UnityEvent<int> onCollected;

    /// <summary>Who may take this gem.</summary>
    public Collector WhoCanCollectIt { get { return whoCanCollectIt; } }

    /// <summary>The score it gives.</summary>
    public int Points { get { return points; } }

    /// <summary>True once a player has taken it.</summary>
    public bool IsCollected { get { return collected; } }

    /// <summary>The glow style chosen for this gem (only used while anyone can take it).</summary>
    public GlowStyle Glow { get { return glowStyle; } }

    // One piece of the glow (the halo, a ring, a star, a bead...).
    private class GlowPart
    {
        public Transform tf;
        public SpriteRenderer sr;
        public float phase;   // its own offset in the animation (0 to 1)
        public float speed;   // its own speed (stars)
        public float size;    // its own base size in world units (stars)
        public float angle;   // where it sits round the gem, in radians
        public float dist;    // how far from the middle of the gem, in world units
    }

    private Collider2D zone;
    private SpriteRenderer[] targets;
    private Color[] originals;
    private bool collected;

    // glow
    private Transform glowRoot;
    private GlowPart halo;
    private readonly List<GlowPart> parts = new List<GlowPart>();
    private GlowStyle activeStyle = GlowStyle.None;
    private int randomPick;          // the style a Random gem chose (0 = not chosen yet)
    private bool glowRunning;
    private float glowStart;
    private float baseHue;           // the hue of Everyone Color (yellow): every rainbow starts here
    private float gemRadius;         // half the gem's size, in world units
    private float haloDiameter;      // the full width of the glow, in world units
    private int orderHalo, orderMid, orderCore, orderAbove;

    // The soft pictures are drawn once and shared by every gem.
    private static Sprite softSprite;
    private static Sprite ringSprite;
    private static Sprite starSprite;

    // ------------------------------------------------------------ setup

    private void Reset()
    {
        Collider2D c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    private void Awake()
    {
        zone = GetComponent<Collider2D>();
        if (zone == null)
        {
            Debug.LogError("ScorePickup on '" + name + "': it needs a Collider 2D (Is Trigger ticked) so players can touch it. " +
                           "Add one to the gem prefab. This gem is switched off until you do.", this);
            enabled = false;
            return;
        }
        if (!zone.isTrigger)
        {
            Debug.LogWarning("ScorePickup on '" + name + "': its Collider 2D is not a trigger, so players will bump into it instead of picking it up. " +
                             "Tick Is Trigger on the collider.", this);
        }

        targets = (sprites != null && sprites.Length > 0) ? sprites : GetComponentsInChildren<SpriteRenderer>();
        originals = new Color[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            originals[i] = targets[i] != null ? targets[i].color : Color.white;
        }

        ApplyTint();
        BuildGlow();
    }

    /// <summary>For a spawner: sets who can take the gem and what it is worth, and re-colours it. Call it right after Instantiate.</summary>
    public void Configure(Collector who, int pointValue)
    {
        whoCanCollectIt = who;
        points = Mathf.Max(0, pointValue);
        if (targets != null)
        {
            ApplyTint();
            BuildGlow();
        }
    }

    /// <summary>For a spawner: picks the glow of a gem anyone can take. Call it right after Instantiate (and after Configure).</summary>
    public void SetGlow(GlowStyle style)
    {
        glowStyle = style;
        randomPick = 0;
        if (targets != null) BuildGlow();
    }

    // ------------------------------------------------------------ colour

    private void ApplyTint()
    {
        bool forEveryone = whoCanCollectIt == Collector.Everyone;
        Color hue = forEveryone ? everyoneColor : ColourOf((int)whoCanCollectIt);
        float strength = forEveryone ? everyoneTintStrength : tintStrength;
        SetGemColour(hue, strength);
    }

    // Mixes a colour over the gem's own pictures (the glow pictures are not part of this).
    private void SetGemColour(Color hue, float strength)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color original = originals[i];
            Color mix = Color.Lerp(original, hue, strength);
            mix.a = original.a;   // never change the picture's own transparency
            targets[i].color = mix;
        }
    }

    private Color ColourOf(int playerNumber)
    {
        switch (playerNumber)
        {
            case 1: return player1Color;
            case 2: return player2Color;
            case 3: return player3Color;
            default: return player4Color;
        }
    }

    // A rainbow colour. Phase 0 is yellow (the hue of Everyone Color); 1 is a full trip round the wheel, back to yellow.
    private Color Rainbow(float phase)
    {
        float hue = Mathf.Repeat(baseHue + phase, 1f);
        return Color.HSVToRGB(hue, rainbowSaturation, 1f);
    }

    // Yellow when the gem appears, melting into the given rainbow colour over Fade To Rainbow Seconds.
    private Color Melt(Color rainbow, float age)
    {
        float amount = fadeToRainbowSeconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, age / fadeToRainbowSeconds);
        return Color.Lerp(everyoneColor, rainbow, amount);
    }

    // ------------------------------------------------------------ glow: building

    private void ClearGlow()
    {
        glowRunning = false;
        if (glowRoot != null)
        {
            glowRoot.gameObject.SetActive(false);   // gone right now; Destroy finishes at the end of the frame
            Destroy(glowRoot.gameObject);
        }
        glowRoot = null;
        halo = null;
        parts.Clear();
    }

    private void BuildGlow()
    {
        ClearGlow();

        if (whoCanCollectIt != Collector.Everyone || glowStyle == GlowStyle.None || targets == null) return;

        activeStyle = glowStyle;
        if (activeStyle == GlowStyle.Random)
        {
            if (randomPick == 0) randomPick = Random.Range(1, 7);   // 1 to 6: every real style
            activeStyle = (GlowStyle)randomPick;
        }

        float saturation, value;
        Color.RGBToHSV(everyoneColor, out baseHue, out saturation, out value);

        // Where the gem is, how big, and what the glow has to sit behind or in front of.
        Bounds bounds = VisualBounds();
        gemRadius = Mathf.Max(0.1f, Mathf.Max(bounds.extents.x, bounds.extents.y));
        haloDiameter = gemRadius * 2f * glowSize;

        int sortLayer = 0;
        int lowest = 0;
        int highest = 0;
        bool found = false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            if (!found)
            {
                sortLayer = targets[i].sortingLayerID;
                lowest = highest = targets[i].sortingOrder;
                found = true;
            }
            else
            {
                lowest = Mathf.Min(lowest, targets[i].sortingOrder);
                highest = Mathf.Max(highest, targets[i].sortingOrder);
            }
        }
        orderHalo = lowest - 3;
        orderMid = lowest - 2;
        orderCore = lowest - 1;
        orderAbove = highest + 1;

        // The glow hangs on the gem, centred on its picture. Its scale is cancelled out so 1 unit in the glow = 1 world unit.
        GameObject rootObject = new GameObject("GemGlow");
        rootObject.layer = gameObject.layer;
        glowRoot = rootObject.transform;
        glowRoot.SetParent(transform, false);
        glowRoot.position = bounds.center;
        Vector3 s = transform.lossyScale;
        glowRoot.localScale = new Vector3(Inverse(s.x), Inverse(s.y), Inverse(s.z));

        Sprite soft = GetSoftSprite();
        halo = MakePart("Halo", soft, sortLayer, orderHalo);

        switch (activeStyle)
        {
            case GlowStyle.Gradient:
                parts.Add(MakePart("GradientMiddle", soft, sortLayer, orderMid));
                parts.Add(MakePart("GradientCore", soft, sortLayer, orderCore));
                break;

            case GlowStyle.Ripple:
                for (int i = 0; i < rippleRings; i++)
                {
                    GlowPart ring = MakePart("Ripple" + i, GetRingSprite(), sortLayer, orderMid);
                    ring.phase = (float)i / rippleRings;
                    parts.Add(ring);
                }
                break;

            case GlowStyle.Starlight:
                for (int i = 0; i < starCount; i++)
                {
                    GlowPart star = MakePart("Star" + i, GetStarSprite(), sortLayer, orderAbove);
                    star.phase = Random.value;
                    star.speed = Random.Range(0.5f, 1.2f);
                    star.size = gemRadius * Random.Range(0.5f, 1f);
                    star.angle = Random.value * Mathf.PI * 2f;
                    star.dist = gemRadius * Random.Range(0.2f, 0.5f + 0.5f * glowSize);
                    star.tf.localPosition = new Vector3(Mathf.Cos(star.angle) * star.dist, Mathf.Sin(star.angle) * star.dist, 0f);
                    parts.Add(star);
                }
                break;

            case GlowStyle.RainbowWave:
                for (int i = 0; i < waveOrbs; i++)
                {
                    GlowPart orb = MakePart("Bead" + i, soft, sortLayer, orderAbove);
                    orb.phase = (float)i / waveOrbs;
                    orb.angle = orb.phase * Mathf.PI * 2f;
                    orb.dist = gemRadius * (0.5f + 0.5f * glowSize);
                    parts.Add(orb);
                }
                break;
        }

        glowStart = Time.time;
        glowRunning = true;
    }

    private GlowPart MakePart(string partName, Sprite sprite, int sortLayer, int order)
    {
        GameObject go = new GameObject(partName);
        go.layer = gameObject.layer;
        go.transform.SetParent(glowRoot, false);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = sortLayer;
        sr.sortingOrder = order;
        if (glowMaterial != null) sr.sharedMaterial = glowMaterial;
        sr.color = new Color(1f, 1f, 1f, 0f);   // invisible until the first frame of the animation

        GlowPart part = new GlowPart();
        part.tf = go.transform;
        part.sr = sr;
        return part;
    }

    private Bounds VisualBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool have = false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null || targets[i].sprite == null) continue;
            if (!have) { b = targets[i].bounds; have = true; }
            else b.Encapsulate(targets[i].bounds);
        }
        if (!have && zone != null) b = zone.bounds;
        return b;
    }

    private static float Inverse(float v)
    {
        return Mathf.Abs(v) > 0.0001f ? 1f / v : 1f;
    }

    // ------------------------------------------------------------ glow: animating

    private void Update()
    {
        if (!glowRunning || collected) return;

        float age = Time.time - glowStart;
        Color gem;
        switch (activeStyle)
        {
            case GlowStyle.Breathing:   gem = AnimateBreathing(age);   break;
            case GlowStyle.Rainbow:     gem = AnimateRainbow(age);     break;
            case GlowStyle.Ripple:      gem = AnimateRipple(age);      break;
            case GlowStyle.Gradient:    gem = AnimateGradient(age);    break;
            case GlowStyle.Starlight:   gem = AnimateStarlight(age);   break;
            case GlowStyle.RainbowWave: gem = AnimateRainbowWave(age); break;
            default: return;
        }

        SetGemColour(gem, everyoneTintStrength);   // the gem's own picture follows the glow colour
    }

    // Breathing: the glow swells and fades. Dim = yellow, full = a rainbow colour, and every breath peaks in the next colour.
    private Color AnimateBreathing(float age)
    {
        float cycle = age / breathSeconds;
        float breath = 0.5f - 0.5f * Mathf.Cos(cycle * Mathf.PI * 2f);   // 0 to 1, starts at 0
        int breathNumber = Mathf.FloorToInt(cycle);

        Color colour = Color.Lerp(everyoneColor, Rainbow((breathNumber + 1) * 0.17f), breath);
        SetHalo(colour, Mathf.Lerp(0.15f, 1f, breath) * glowStrength, Mathf.Lerp(0.75f, 1.1f, breath));
        return colour;
    }

    // Rainbow: the whole gem and its halo travel round the colour wheel, starting from yellow.
    private Color AnimateRainbow(float age)
    {
        Color colour = Melt(Rainbow(age * rainbowSpeed), age);
        SetHalo(colour, glowStrength * 0.8f, 1f + 0.06f * Mathf.Sin(age * 4f));
        return colour;
    }

    // Wave / Ripple: rings leave the gem yellow and turn rainbow as they spread out and fade.
    private Color AnimateRipple(float age)
    {
        Color tone = Color.Lerp(everyoneColor, Melt(Rainbow(age * rainbowSpeed * 0.5f), age), 0.5f);
        SetHalo(tone, glowStrength * 0.4f, 0.8f);

        for (int i = 0; i < parts.Count; i++)
        {
            GlowPart ring = parts[i];
            float p = Mathf.Repeat(age / rippleSeconds + ring.phase, 1f);   // 0 = just born, 1 = gone
            float grow = 1f - (1f - p) * (1f - p);                          // fast at first, slower at the edge
            float alpha = Mathf.Clamp01(p * 8f) * Mathf.Pow(1f - p, 1.5f);  // fades in quickly, fades out slowly

            Color colour = Color.Lerp(everyoneColor, Rainbow(age * rainbowSpeed + p * 0.6f), p);
            ring.tf.localScale = Vector3.one * (haloDiameter * Mathf.Lerp(0.35f, 1.15f, grow));
            Paint(ring, colour, alpha * glowStrength);
        }
        return tone;
    }

    // Gradient: three soft layers. The middle is yellow-first, each layer outwards is further along the rainbow, and it all flows.
    private Color AnimateGradient(float age)
    {
        float t = age * rainbowSpeed;
        Color core = Melt(Rainbow(t), age);
        Color middle = Melt(Rainbow(t + GradientStep), age);
        Color outer = Melt(Rainbow(t + GradientStep * 2f), age);

        SetHalo(outer, glowStrength * 0.5f, 1f + 0.04f * Mathf.Sin(age * 2f));

        if (parts.Count >= 2)
        {
            parts[0].tf.localScale = Vector3.one * (haloDiameter * 0.65f);
            Paint(parts[0], middle, glowStrength * 0.6f);
            parts[1].tf.localScale = Vector3.one * (haloDiameter * 0.35f);
            Paint(parts[1], core, glowStrength * 0.85f);
        }
        return core;
    }

    // Starlight: little stars twinkle on and off around the gem, each in its own rainbow colour.
    private Color AnimateStarlight(float age)
    {
        Color tone = Color.Lerp(everyoneColor, Melt(Rainbow(age * rainbowSpeed * 0.3f), age), 0.5f);
        SetHalo(tone, glowStrength * (0.35f + 0.1f * Mathf.Sin(age * 2f)), 1f);

        for (int i = 0; i < parts.Count; i++)
        {
            GlowPart star = parts[i];
            float twinkle = 0.5f + 0.5f * Mathf.Sin((age * star.speed + star.phase) * Mathf.PI * 2f);
            twinkle = twinkle * twinkle * twinkle;   // mostly dark with short bright flashes

            Color colour = Melt(Rainbow((float)i / parts.Count + age * rainbowSpeed * 0.5f), age);
            colour = Color.Lerp(colour, Color.white, 0.35f);   // stars are a little whiter than the glow

            star.tf.localScale = Vector3.one * (star.size * (0.3f + 0.7f * twinkle));
            star.tf.localRotation = Quaternion.Euler(0f, 0f, age * 15f + star.phase * 90f);
            Paint(star, colour, twinkle * Mathf.Min(1f, glowStrength * 1.3f));
        }
        return tone;
    }

    // Rainbow Wave: a ring of beads. A wave of size and brightness runs round the ring, and the rainbow runs with it.
    private Color AnimateRainbowWave(float age)
    {
        float hue = age * rainbowSpeed;
        Color tone = Melt(Rainbow(hue), age);
        SetHalo(tone, glowStrength * 0.4f, 1f);

        for (int i = 0; i < parts.Count; i++)
        {
            GlowPart orb = parts[i];
            float wave = 0.5f + 0.5f * Mathf.Sin((age * WaveSpeed - orb.phase) * Mathf.PI * 2f);   // 0 to 1
            float angle = orb.angle + age * 0.4f;                                                   // the ring turns slowly
            float dist = orb.dist * (1f + 0.15f * wave);                                            // the crest of the wave pushes out

            orb.tf.localPosition = new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist, 0f);
            orb.tf.localScale = Vector3.one * (gemRadius * Mathf.Lerp(0.45f, 1f, wave));
            Paint(orb, Melt(Rainbow(hue - orb.phase), age), glowStrength * Mathf.Lerp(0.35f, 1f, wave));
        }
        return tone;
    }

    private void SetHalo(Color colour, float alpha, float scale)
    {
        if (halo == null) return;
        halo.tf.localScale = Vector3.one * (haloDiameter * scale);
        Paint(halo, colour, alpha);
    }

    private static void Paint(GlowPart part, Color colour, float alpha)
    {
        if (part == null || part.sr == null) return;
        colour.a = Mathf.Clamp01(alpha);
        part.sr.color = colour;
    }

    // ------------------------------------------------------------ glow: the pictures

    // Each picture is drawn in code, white with a see-through shape, so the glow can be tinted any colour. 1 sprite = 1 world unit wide.
    private static Sprite GetSoftSprite()
    {
        if (softSprite == null) softSprite = MakeSprite("GemGlowSoft", 64, SoftAlpha);
        return softSprite;
    }

    private static Sprite GetRingSprite()
    {
        if (ringSprite == null) ringSprite = MakeSprite("GemGlowRing", 128, RingAlpha);
        return ringSprite;
    }

    private static Sprite GetStarSprite()
    {
        if (starSprite == null) starSprite = MakeSprite("GemGlowStar", 64, StarAlpha);
        return starSprite;
    }

    // x and y run from -1 to 1 across the picture.
    private static float SoftAlpha(float x, float y)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float f = Mathf.Clamp01(1f - d);
        return f * f;   // bright in the middle, fading smoothly to nothing at the edge
    }

    private static float RingAlpha(float x, float y)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float t = (d - 0.8f) / 0.1f;
        return Mathf.Exp(-t * t) * Mathf.Clamp01((1f - d) * 8f);   // a soft thin ring near the edge
    }

    private static float StarAlpha(float x, float y)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        float streakAcross = Mathf.Pow(Mathf.Clamp01(1f - ax), 3f) * Mathf.Exp(-ay * 14f);
        float streakUp = Mathf.Pow(Mathf.Clamp01(1f - ay), 3f) * Mathf.Exp(-ax * 14f);
        float core = Mathf.Exp(-(x * x + y * y) * 18f);
        return Mathf.Max(Mathf.Max(streakAcross, streakUp), core);   // a four-point sparkle
    }

    private static Sprite MakeSprite(string spriteName, int size, System.Func<float, float, float> alphaAt)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = spriteName;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = ((x + 0.5f) / size) * 2f - 1f;
                float ny = ((y + 0.5f) / size) * 2f - 1f;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(nx, ny)) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        sprite.name = spriteName;
        return sprite;
    }

    // ------------------------------------------------------------ taking it

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCollect(other);
    }

    // Also while standing on it: covers a gem that appears under a player, or a player who is revived on top of one.
    private void OnTriggerStay2D(Collider2D other)
    {
        TryCollect(other);
    }

    private void TryCollect(Collider2D other)
    {
        if (collected || !isActiveAndEnabled) return;

        CoopHealth who = other.GetComponentInParent<CoopHealth>();
        if (who == null) return;

        int number = who.PlayerNumber;
        if (number < 1 || number > MaxPlayers) return;
        if (!who.IsAlive || !CoopRules.Counts(number)) return;
        if (!CanTake(number)) return;

        Collect(number);
    }

    private bool CanTake(int number)
    {
        if (whoCanCollectIt == Collector.Everyone) return true;
        if ((int)whoCanCollectIt == number) return true;
        return anyoneIfPlayerMissing && !OwnerIsInGame();
    }

    // The gem's own player is in the game when they exist and count in the current co-op rules.
    private bool OwnerIsInGame()
    {
        int owner = (int)whoCanCollectIt;
        return CoopHealth.Get(owner) != null && CoopRules.Counts(owner);
    }

    private void Collect(int number)
    {
        collected = true;
        glowRunning = false;

        CoopScore.Add(points, number);   // note the order in CoopRules.cs: (amount, playerNumber)
        if (alsoAddToScoreController) ScoreController.AddScore(points);

        // Hidden at once (the glow too, it is made of renderers under the gem), so it can't be taken twice or seen lingering;
        // the object itself goes a moment later.
        zone.enabled = false;
        Renderer[] visible = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < visible.Length; i++) visible[i].enabled = false;

        if (onCollected != null) onCollected.Invoke(number);

        Destroy(gameObject, destroyAfter);
    }
}
