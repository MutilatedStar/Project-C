using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// A colour glow on ONE player, so you can tell at a glance who is Player 1 and who is Player 2. It is FloorDoor's tint (colour,
/// strength and fade speed) applied to the character instead of a door, with the same colours (blue = Player 1, orange = Player 2,
/// Players 3 and 4 spare), so a player matches the door they have to stand on.
///
/// Two layers, each with its own level:
///   Tint   washes the character's own picture with the player's colour (Tint Strength).
///   Halo   a coloured outline that glows behind the character and breathes a little (Halo Strength).
///
/// SET UP: put it on the player prefab(s): Player 1 and Player 2, or the one shared prefab. Nothing else to wire up. The player number is
/// read from CoopHealth every frame, so it follows the hub's slot even when one prefab is shared or a player respawns.
/// Leave Sprite empty to use the same picture PlayerEffects uses (the player's own SpriteRenderer, else the first one under it).
///
/// With a single player in the game the glow stays off (tick Show In Single Player to change that). It fades in and out like the doors do.
///
/// OTHER SCRIPTS THAT RECOLOUR THE SPRITE (PlayerEffects' gold invincibility pulse, a hit flash...): this runs last, in LateUpdate. If
/// something else has changed the sprite's colour since this wrote it, that new colour becomes the base and the tint goes on top of it.
/// PlayerEffects asks BaseColor for the sprite's clean colour, so its pulse never picks up the tint (see the small change in PlayerEffects).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class PlayerGlow : MonoBehaviour
{
    private const int MaxPlayers = 4;

    [Header("When")]
    [Tooltip("Off: the glow only shows while two or more players are in the game, so a lone player stays plain. On: always show it.")]
    [SerializeField] private bool showInSinglePlayer = false;

    [Header("Colours (the same as the FloorDoor colours, so a player matches the door they should stand on)")]
    [SerializeField] private Color player1Color = new Color(0.25f, 0.55f, 1f, 1f);     // blue
    [SerializeField] private Color player2Color = new Color(1f, 0.6f, 0.15f, 1f);      // orange
    [SerializeField] private Color player3Color = new Color(0.35f, 0.9f, 0.4f, 1f);    // green (spare)
    [SerializeField] private Color player4Color = new Color(0.75f, 0.4f, 0.95f, 1f);   // purple (spare)

    [Header("Tint (colours the character's own picture)")]
    [Tooltip("The picture to tint. Leave EMPTY to use the same one PlayerEffects uses.")]
    [SerializeField] private SpriteRenderer sprite;
    [Tooltip("How strongly the colour covers the character's picture. 1 = fully the player's colour, 0 = no tint (halo only).")]
    [SerializeField, Range(0f, 1f)] private float tintStrength = 0.35f;
    [Tooltip("How fast the colour fades in and out. Higher = quicker.")]
    [SerializeField, Min(0.1f)] private float colourSpeed = 10f;

    [Header("Halo (a coloured outline glowing behind the character)")]
    [SerializeField] private bool halo = true;
    [Tooltip("How bright the halo is at its brightest. 0 = invisible, 1 = solid colour.")]
    [SerializeField, Range(0f, 1f)] private float haloStrength = 0.6f;
    [Tooltip("How much bigger than the character the halo is. 1.15 = a thin outline, 1.3 = a fat one.")]
    [SerializeField, Range(1f, 1.5f)] private float haloSize = 1.15f;
    [Tooltip("How much the halo breathes. 0 = steady, 1 = fades right down and back up.")]
    [SerializeField, Range(0f, 1f)] private float haloPulse = 0.3f;
    [Tooltip("How fast the halo breathes. Higher = quicker.")]
    [SerializeField, Min(0.1f)] private float haloPulseSpeed = 3f;

    private CoopHealth health;
    private PlayerInputSource input;
    private SpriteRenderer haloSprite;
    private Transform haloTransform;

    private Color baseColor = Color.white;                // the picture's own colour, without our tint (valid while hasWritten)
    private Color lastWritten = Color.white;              // what we last put on the sprite, to notice anyone else changing it
    private bool hasWritten;
    private Color overlay = new Color(1f, 1f, 1f, 0f);    // rgb = the hue shown, a = how much of it covers the picture
    private float haloAlpha;

    /// <summary>
    /// The sprite's colour as everybody else left it: WITHOUT this glow's tint. Safe to read at any time (PlayerEffects uses it
    /// as the colour to pulse from and to go back to).
    /// </summary>
    public Color BaseColor
    {
        get
        {
            if (sprite == null) return Color.white;
            return (hasWritten && Same(sprite.color, lastWritten)) ? baseColor : sprite.color;
        }
    }

    /// <summary>The glow colour of a player (1 to 4). Handy for a HUD that should match.</summary>
    public Color ColourOf(int playerNumber)
    {
        switch (playerNumber)
        {
            case 1: return player1Color;
            case 2: return player2Color;
            case 3: return player3Color;
            default: return player4Color;
        }
    }

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        health = GetComponent<CoopHealth>();
        if (health == null) health = GetComponentInParent<CoopHealth>();

        if (sprite == null) sprite = GetComponent<SpriteRenderer>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite == null)
        {
            Debug.LogWarning("PlayerGlow on '" + name + "': it found no SpriteRenderer to tint. Drag the player's sprite into Sprite. " +
                             "This glow is switched off until you do.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        // Switched off (Player 2 removed, or the whole player turned off): give the picture its own colour back and leave nothing behind.
        if (sprite != null && hasWritten && Same(sprite.color, lastWritten)) sprite.color = baseColor;
        hasWritten = false;
        overlay = new Color(1f, 1f, 1f, 0f);
        haloAlpha = 0f;
        if (haloSprite != null) haloSprite.enabled = false;
    }

    private void OnDestroy()
    {
        if (haloSprite != null) Destroy(haloSprite.gameObject);
    }

    // ------------------------------------------------------------ running

    private void LateUpdate()
    {
        Color own = BaseColor;   // the picture's colour as everybody else left it, without our tint

        int number = CurrentPlayerNumber();
        bool on = number >= 1 && number <= MaxPlayers && (showInSinglePlayer || PlayerInputSource.TwoPlayers);

        // Not glowing: fade the tint out, keeping the last hue while it goes (the same as FloorDoor).
        Color desired = overlay;
        desired.a = 0f;
        if (on)
        {
            Color c = ColourOf(number);
            desired = new Color(c.r, c.g, c.b, tintStrength);
        }

        float k = 1f - Mathf.Exp(-colourSpeed * Time.deltaTime);
        overlay = Color.Lerp(overlay, desired, k);

        if (overlay.a > 0.002f)
        {
            Color mix = Color.Lerp(own, overlay, overlay.a);
            mix.a = own.a;   // never change the picture's own transparency
            sprite.color = mix;

            baseColor = own;
            lastWritten = mix;
            hasWritten = true;
        }
        else if (hasWritten)
        {
            // Faded all the way out: hand the picture back untouched, and stop keeping watch over its colour.
            sprite.color = own;
            hasWritten = false;
        }

        UpdateHalo(on, k, own.a);
    }

    private int CurrentPlayerNumber()
    {
        if (health != null) return health.PlayerNumber;

        if (input == null) input = GetComponent<PlayerInputSource>();
        return input != null ? input.playerNumber : 0;
    }

    // ------------------------------------------------------------ halo

    // A copy of the character's picture, a little bigger, tinted and drawn just behind it: the part that sticks out is the glow.
    private void UpdateHalo(bool on, float k, float spriteAlpha)
    {
        float target = (halo && on) ? haloStrength : 0f;
        haloAlpha = Mathf.Lerp(haloAlpha, target, k);

        Sprite shape = sprite.sprite;
        if (haloAlpha <= 0.005f || shape == null || !sprite.enabled)
        {
            if (haloSprite != null) haloSprite.enabled = false;
            return;
        }

        if (haloSprite == null) CreateHalo();

        // Follow the character's current frame, direction and drawing order.
        if (haloSprite.sprite != shape) haloSprite.sprite = shape;
        haloSprite.flipX = sprite.flipX;
        haloSprite.flipY = sprite.flipY;
        haloSprite.sortingLayerID = sprite.sortingLayerID;
        haloSprite.sortingOrder = sprite.sortingOrder - 1;

        // Grow around the picture's centre, not its pivot (a pivot at the feet would make the halo grow upward only).
        Vector3 centre = shape.bounds.center;
        if (sprite.flipX) centre.x = -centre.x;
        if (sprite.flipY) centre.y = -centre.y;
        centre.z = 0f;
        haloTransform.localPosition = centre * (1f - haloSize);
        haloTransform.localScale = new Vector3(haloSize, haloSize, 1f);

        // Breathes between full strength and (1 - Halo Pulse) of it; follows the picture's own transparency (fades, blinks).
        float breathe = 1f - haloPulse * 0.5f * (1f + Mathf.Sin(Time.time * haloPulseSpeed));
        Color hue = overlay;
        hue.a = Mathf.Clamp01(haloAlpha * breathe * spriteAlpha);
        haloSprite.color = hue;
        haloSprite.enabled = true;
    }

    private void CreateHalo()
    {
        GameObject go = new GameObject("PlayerGlow Halo");
        go.layer = sprite.gameObject.layer;   // same camera layer, so split-screen cameras draw it too
        haloTransform = go.transform;
        haloTransform.SetParent(sprite.transform, false);
        haloSprite = go.AddComponent<SpriteRenderer>();
    }

    // ------------------------------------------------------------ helpers

    // Colours that differ by less than one shade of 255 count as the same.
    private static bool Same(Color a, Color b)
    {
        const float e = 0.004f;
        return Mathf.Abs(a.r - b.r) < e && Mathf.Abs(a.g - b.g) < e && Mathf.Abs(a.b - b.b) < e && Mathf.Abs(a.a - b.a) < e;
    }
}
