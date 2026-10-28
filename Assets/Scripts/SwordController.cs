using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;
using Cainos.PixelArtTopDown_Basic;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random (same reason as FoeController).

/// <summary>
/// Put this on the PLAYER (the same object as TopDownCharacterController). Needs the updated FoeController and
/// SwordFireTrail.cs in the project too.
///
/// Controls (all changeable in the Inspector):
///   Space (tap)        swing the current sword the way the player is facing. The blade always chops DOWNWARD on screen.
///   Space (keep held)  the swing happens straight away, then a charge starts. When the player GLOWS the spin is ready:
///                      let go of Space for a full 360-degree spin that hits every foe around you.
///   Q / E              previous / next sword.   1-9 pick a sword directly.
///
/// Every number lives on the sword, so any sword can have any mix of the special effects below. A special is OFF
/// when its number is at the "off" value (chain targets 0, slow factor 1, freeze chance 0, trail duration 0):
///   Knockback     pushes the foe back this many units (the foe's Knockback Resistance reduces it).
///   Crit          a lucky hit: extra damage, the foe FLASHES WHITE and the crit sound plays.
///   Chain         lightning jumps from the foe you hit to the nearest other foes, a little weaker each jump.
///   Slow / Freeze foes are slowed, with a chance to freeze solid (they turn blue and can't move or attack).
///   Fire trail    leaves a burning line on the ground that hurts foes for several seconds
///                 (a spin leaves 8 of them, one every 45 degrees).
///
/// All hits go through FoeController (TakeDamage, Knockback, ApplySlow, Freeze, FlashWhite), which handles
/// the pause, the death and the score by itself.
///
/// Notebook open (or FoeController.Frozen) = no swinging, no switching, no damage, same as the foes.
///
/// Collecting: a sword with "Unlocked At Start" OFF can't be equipped (Q / E / 1-9 skip it) until UnlockSword,
/// UnlockAllSwords or UnlockByName is called (a pickup, the shop...). Tick Debug Unlock All Swords to own every sword.
///
/// Upgrades: SwordShop.cs (per sword) and PlayerUpgrades.cs (the player) hand their bonuses in through
/// SetUpgradeBonus / SetPlayerBonus. The numbers the sword really uses are the Inspector numbers PLUS those bonuses,
/// which is what CurrentSword returns. The Inspector numbers themselves are never changed.
///
/// Particles: fire, ice and lightning swords sparkle along the blade while it is out (SwordElementFx.cs, built by this script).
///
/// Until a sword has a Sprite, a plain coloured bar is drawn so you can test right away.
/// Once it has one, Sprite Scale / Sprite Width / Sprite Height resize it, and with Fit Hitbox To Sprite ON the
/// sword's hit range follows the size you see (the yellow circle on the player shows it in the Scene view).
/// </summary>
[DisallowMultipleComponent]
public class SwordController : MonoBehaviour
{
    // ------------------------------------------------------------ one sword's numbers

    /// <summary>All the numbers for one sword. Shows up in the Inspector as one foldout per sword.</summary>
    [System.Serializable]
    public class SwordStats
    {
        [Tooltip("Must be unique. The shop and the unlock list find a sword by this name.")]
        public string swordName = "Sword";
        [Tooltip("ON: the player owns this sword from the start. OFF: it stays locked until it is unlocked (a pickup, the shop, or the Debug Unlock All Swords box). The starting sword is always owned.")]
        public bool unlockedAtStart = true;

        [Header("Damage")]
        [Tooltip("Damage per hit, taken off the foe's Health.")]
        [Min(0f)] public float damage = 15f;
        [Tooltip("Chance (0 to 1) that a hit is a critical hit. 0 = never. 0.1 = 10%. A crit makes the foe flash white and plays the crit sound.")]
        [Range(0f, 1f)] public float critChance = 0f;
        [Tooltip("A critical hit deals damage x this.")]
        [Min(1f)] public float critMultiplier = 2f;
        [Tooltip("Most foes ONE normal swing can hit with the blade. 0 = no limit. (The spin always hits everything around you.)")]
        [Min(0)] public int maxTargets = 2;
        [Tooltip("Fraction of the damage a hit deals that heals the player. 0 = off, 0.05 = 5%. The shop can raise it.")]
        [Range(0f, 1f)] public float lifeSteal = 0f;

        [Header("Swing")]
        [Tooltip("Seconds the swing takes from raised to lowered. Smaller = faster.")]
        [Min(0.05f)] public float swingTime = 0.25f;
        [Tooltip("Seconds you must wait after a swing finishes before the next one.")]
        [Min(0f)] public float cooldown = 0.15f;
        [Tooltip("How far the sword reaches, in Unity units (1 unit = 1 tile with 32 px per unit). Ignored while a Sprite is assigned and Fit Hitbox To Sprite is ON: then the sprite's size decides.")]
        [Min(0.1f)] public float reach = 1.2f;
        [Tooltip("Width of the sweep in degrees. Wider hits more foes around you.")]
        [Range(30f, 180f)] public float arc = 110f;

        [Header("Knockback")]
        [Tooltip("How far a hit pushes a foe back, in Unity units. Dagger tiny, broadsword big.")]
        [Min(0f)] public float knockback = 0.5f;

        [Header("Spin attack (hold the attack key)")]
        [Tooltip("Seconds the attack key must be held before the spin is ready (the player glows).")]
        [Min(0.1f)] public float spinChargeTime = 1.5f;
        [Tooltip("Seconds the full 360 degree spin takes.")]
        [Min(0.1f)] public float spinTime = 0.5f;
        [Tooltip("Spin damage = damage x this.")]
        [Min(0f)] public float spinDamageMultiplier = 1.5f;
        [Tooltip("Colour of the glow around the player while the spin charges.")]
        public Color glowColor = new Color(1f, 0.95f, 0.6f);

        [Header("Lightning chain")]
        [Tooltip("How many EXTRA foes the lightning jumps to after the one the blade hit. 0 = off.")]
        [Min(0)] public int chainTargets = 0;
        [Tooltip("A jump can only reach a foe this close (Unity units) to the foe it jumps from.")]
        [Min(0.1f)] public float chainRange = 3f;
        [Tooltip("Each jump deals this fraction of the previous one. 0.7 = 70%.")]
        [Range(0.1f, 1f)] public float chainFalloff = 0.7f;
        [Tooltip("Colour of the lightning bolts.")]
        public Color boltColor = new Color(1f, 1f, 0.5f);
        [Tooltip("Multiplies the damage of every lightning jump. 1 = normal. The shop raises it.")]
        [Min(0f)] public float chainDamageMultiplier = 1f;

        [Header("Ice")]
        [Tooltip("Foes hit move at this fraction of their speed. 1 = off. 0.5 = half speed.")]
        [Range(0.1f, 1f)] public float slowFactor = 1f;
        [Tooltip("Seconds the slow lasts (a new hit refreshes it).")]
        [Min(0f)] public float slowTime = 3f;
        [Tooltip("Chance (0 to 1) that a hit freezes the foe solid. 0 = off.")]
        [Range(0f, 1f)] public float freezeChance = 0f;
        [Tooltip("Seconds a frozen foe stays frozen.")]
        [Min(0f)] public float freezeTime = 2.5f;

        [Header("Fire trail")]
        [Tooltip("Seconds the flames last. 0 = no trail.")]
        [Min(0f)] public float trailDuration = 0f;
        [Tooltip("Length of each burning line, in Unity units.")]
        [Min(0.2f)] public float trailLength = 2f;
        [Tooltip("Width of each burning line, in Unity units.")]
        [Min(0.1f)] public float trailWidth = 0.6f;
        [Tooltip("Damage a foe standing in the flames takes each tick.")]
        [Min(0f)] public float trailDamage = 3f;
        [Tooltip("Seconds between burn ticks.")]
        [Min(0.1f)] public float trailTickTime = 0.5f;

        [Header("Look")]
        [Tooltip("Optional. Leave empty to draw a plain coloured bar the size of the sword's reach.")]
        public Sprite sprite;
        [Tooltip("Overall size of the Sprite (width AND height together). 1 = the sprite's normal size.")]
        [Min(0.01f)] public float spriteScale = 1f;
        [Tooltip("Stretches the Sprite across, as it is drawn in the picture (a sword drawn pointing UP gets thicker). Multiplies with Sprite Scale. 1 = normal.")]
        [Min(0.01f)] public float spriteWidth = 1f;
        [Tooltip("Stretches the Sprite along, as it is drawn in the picture (a sword drawn pointing UP gets longer). Multiplies with Sprite Scale. 1 = normal.")]
        [Min(0.01f)] public float spriteHeight = 1f;
        [Tooltip("ON: the hit range comes from the Sprite's real size after the scale, width and height above. The sprite's back end sits at the player's hand and its tip is the end of the hit range, and the blade's thickness makes the swing a little wider. OFF: the Reach number is used and the sprite is centred halfway along it.")]
        public bool fitHitboxToSprite = true;
        [Tooltip("Extra hit range past the sprite's tip. Can be negative to shorten it. Only used with Fit Hitbox To Sprite ON.")]
        public float hitReachPadding = 0f;
        [Tooltip("Degrees to turn the Sprite so it points along the swing. A sword drawn pointing UP needs -90, one pointing RIGHT needs 0, one drawn diagonally up-right needs -45.")]
        public float spriteRotationOffset = -90f;
        [Tooltip("Colour of the placeholder bar (only used while no Sprite is assigned).")]
        public Color placeholderColor = new Color(0.85f, 0.85f, 0.9f);
        [Tooltip("Thickness of the placeholder bar in Unity units.")]
        public float placeholderThickness = 0.2f;
        [Tooltip("Fire, ice and lightning sparks along the blade (picked from the sword's own specials). OFF = none.")]
        public bool showBladeParticles = true;
        [Tooltip("How many sparks. 1 = normal, 0.5 = half as many, 2 = twice as many.")]
        [Min(0f)] public float particleAmount = 1f;

        /// <summary>A copy of this sword's numbers. SwordController keeps the Inspector ones untouched and works on copies.</summary>
        public SwordStats Clone() { return (SwordStats)MemberwiseClone(); }
    }

    /// <summary>
    /// Extra strength added on top of a sword's own numbers. Everything is "added": 0 = no change.
    /// One of these comes from the sword's shop upgrades, one from the player's upgrades; both are summed.
    /// </summary>
    [System.Serializable]
    public struct SwordBonus
    {
        [Tooltip("0.25 = +25% damage (blade hits, spins, chain jumps and fire trail).")]
        public float damagePercent;
        [Tooltip("0.05 = +5 percentage points of crit chance.")]
        public float critChanceAdd;
        [Tooltip("0.25 = +25% damage on the lightning jumps only.")]
        public float chainDamagePercent;
        [Tooltip("0.30 = +30% damage on the burning ground only.")]
        public float trailDamagePercent;
        [Tooltip("0.04 = +4 percentage points of freeze chance.")]
        public float freezeChanceAdd;
        [Tooltip("0.04 = heal 4% of the damage dealt.")]
        public float lifeStealAdd;
        [Tooltip("0.20 = 20% faster swings, spins and cooldowns.")]
        public float attackSpeedPercent;

        public static SwordBonus operator +(SwordBonus a, SwordBonus b)
        {
            return new SwordBonus
            {
                damagePercent = a.damagePercent + b.damagePercent,
                critChanceAdd = a.critChanceAdd + b.critChanceAdd,
                chainDamagePercent = a.chainDamagePercent + b.chainDamagePercent,
                trailDamagePercent = a.trailDamagePercent + b.trailDamagePercent,
                freezeChanceAdd = a.freezeChanceAdd + b.freezeChanceAdd,
                lifeStealAdd = a.lifeStealAdd + b.lifeStealAdd,
                attackSpeedPercent = a.attackSpeedPercent + b.attackSpeedPercent
            };
        }
    }

    // ------------------------------------------------------------ inspector

    [Header("Swords")]
    [Tooltip("Every sword the player can carry. Add, remove or edit entries freely. (These starting swords are only filled in when the component is first added; use the component's Reset to get them back.)")]
    [SerializeField] private SwordStats[] swords =
    {
        new SwordStats
        {
            swordName = "Short Sword", damage = 15f, maxTargets = 2,
            swingTime = 0.25f, cooldown = 0.15f, reach = 1.2f, arc = 110f, knockback = 0.5f,
            spinChargeTime = 1.5f, spinTime = 0.5f, glowColor = new Color(1f, 0.95f, 0.6f),
            placeholderColor = new Color(0.85f, 0.85f, 0.9f)
        },
        new SwordStats
        {
            swordName = "Broadsword", damage = 35f, maxTargets = 5,
            swingTime = 0.5f, cooldown = 0.35f, reach = 1.6f, arc = 150f, knockback = 1.4f,
            spinChargeTime = 2f, spinTime = 0.7f, glowColor = new Color(1f, 0.8f, 0.5f),
            placeholderColor = new Color(0.55f, 0.7f, 1f), placeholderThickness = 0.3f
        },
        new SwordStats
        {
            swordName = "Dagger", damage = 8f, critChance = 0.1f, critMultiplier = 2.5f, maxTargets = 1,
            swingTime = 0.15f, cooldown = 0.05f, reach = 0.9f, arc = 80f, knockback = 0.2f,
            spinChargeTime = 1f, spinTime = 0.35f, glowColor = new Color(0.95f, 0.95f, 0.7f),
            placeholderColor = new Color(0.95f, 0.95f, 0.7f), placeholderThickness = 0.15f
        },
        new SwordStats
        {
            swordName = "Lightning Sword", damage = 12f, maxTargets = 1,
            swingTime = 0.22f, cooldown = 0.2f, reach = 1.3f, arc = 100f, knockback = 0.4f,
            spinChargeTime = 1.5f, spinTime = 0.5f, glowColor = new Color(1f, 0.95f, 0.3f),
            chainTargets = 4, chainRange = 3f, chainFalloff = 0.7f, boltColor = new Color(1f, 1f, 0.5f),
            placeholderColor = new Color(1f, 0.95f, 0.35f)
        },
        new SwordStats
        {
            swordName = "Fire Sword", damage = 14f, maxTargets = 2,
            swingTime = 0.3f, cooldown = 0.2f, reach = 1.3f, arc = 110f, knockback = 0.5f,
            spinChargeTime = 1.5f, spinTime = 0.5f, glowColor = new Color(1f, 0.5f, 0.1f),
            trailDuration = 10f, trailLength = 2f, trailWidth = 0.6f, trailDamage = 3f, trailTickTime = 0.5f,
            placeholderColor = new Color(1f, 0.45f, 0.1f)
        },
        new SwordStats
        {
            swordName = "Ice Sword", damage = 12f, maxTargets = 2,
            swingTime = 0.3f, cooldown = 0.2f, reach = 1.3f, arc = 110f, knockback = 0.4f,
            spinChargeTime = 1.5f, spinTime = 0.5f, glowColor = new Color(0.4f, 0.9f, 1f),
            slowFactor = 0.5f, slowTime = 3f, freezeChance = 0.12f, freezeTime = 2.5f,
            placeholderColor = new Color(0.5f, 0.9f, 1f)
        }
    };
    [Tooltip("Index of the sword the player starts with (0 = the first one in the list).")]
    [SerializeField] private int startingSword = 0;

    [Header("Controls")]
    [Tooltip("Tap = swing. Hold = charge the spin attack.")]
    [SerializeField] private KeyCode attackKey = KeyCode.Space;
    [Tooltip("Optional second attack key. KeyCode.Mouse0 = left click. None = off.")]
    [SerializeField] private KeyCode altAttackKey = KeyCode.None;
    [SerializeField] private KeyCode previousSwordKey = KeyCode.Q;
    [SerializeField] private KeyCode nextSwordKey = KeyCode.E;
    [Tooltip("Keys 1-9 pick the matching sword in the list. Turn off if those keys already do something else.")]
    [SerializeField] private bool numberKeysPickSword = true;

    [Header("Swing")]
    [Tooltip("Where the swing is centred, relative to the player's position. If the player's pivot is at its feet, raise this to about chest height so the swing doesn't start from the ground.")]
    [SerializeField] private Vector2 pivotOffset = new Vector2(0f, 0.3f);
    [Tooltip("Fraction of the swing spent winding up before the blade can hit anything. 0 = hits straight away.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float windup = 0.15f;
    [Tooltip("Layers the swing looks at. Everything is fine: only objects with a FoeController are ever hurt.")]
    [SerializeField] private LayerMask foeMask = ~0;

    [Header("Sound")]
    [Tooltip("The AudioSource that plays the sword sounds. Put it on its OWN empty child object (not on the player) with a SoundSwitch on it, so the Sound On/Off setting controls it, same as the footsteps.")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Played when a hit is a critical hit.")]
    [SerializeField] private AudioClip critClip;

    [Header("Debug")]
    [Tooltip("Prints one Console line per hit (sword, damage, crit) so you can tune the numbers.")]
    [SerializeField] private bool debugLog = false;
    [Tooltip("ON: the player owns EVERY sword at once (also works live in Play mode). Untick it before a final build. Unticking does not lock them again.")]
    [SerializeField] private bool debugUnlockAllSwords = false;

    // ------------------------------------------------------------ public

    /// <summary>The equipped sword's numbers as they really are right now: Inspector numbers plus upgrades.</summary>
    public SwordStats CurrentSword => effective[currentIndex];
    public int CurrentIndex => currentIndex;
    public bool IsSwinging => swinging || spinning;
    public int SwordCount => swords != null ? swords.Length : 0;

    /// <summary>Fires when a sword becomes owned. Hook a "new sword!" message to it.</summary>
    public event System.Action<SwordStats> SwordUnlocked;

    /// <summary>Fires whenever the equipped sword changes. Hook a HUD label to it.</summary>
    public event System.Action<SwordStats> SwordChanged;

    /// <summary>A 1x1-unit white square, made once. Used (tinted and stretched) for the stand-in blade, the bolts,
    /// the flames and the white hit flash.</summary>
    public static Sprite WhiteSquare
    {
        get
        {
            if (whiteSquare == null)
            {
                Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                whiteSquare = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            }
            return whiteSquare;
        }
    }

    // ------------------------------------------------------------ state

    private Animator animator;
    private SpriteRenderer playerSprite;
    private HealthComponent playerHealth;

    private SwordStats[] effective;      // what the swords really use: the Inspector numbers (swords) plus upgrades
    private SwordElementFx fx;           // fire / ice / lightning sparks on the blade
    private bool fxFrozen;               // true while the sparks are held still by a pause
    private bool debugUnlockApplied;

    private Transform pivot;             // the "hand": rotates, carrying the blade with it
    private Transform visualTransform;   // the blade, sitting out along the pivot's +X
    private SpriteRenderer visual;
    private SpriteRenderer glowRenderer; // halo behind the player while the spin charges
    private float activeReach;           // hit range of the equipped sword (its Reach, or measured from its sprite)
    private float activeHalfWidth;       // half the blade's thickness, widens the swing a little (0 = off)

    private ContactFilter2D foeFilter;
    private readonly Collider2D[] overlapBuffer = new Collider2D[32];
    private readonly Collider2D[] chainBuffer = new Collider2D[32];
    private readonly HashSet<FoeController> hitFoes = new HashSet<FoeController>();

    // The arc being swept right now, so a breakable can ask "is this point under the blade?" (see PointInSweep).
    private Vector2 sweepOrigin;
    private float sweepFrom;
    private float sweepSpan;
    private System.Func<Vector2, bool> sweepTest;

    private int currentIndex;
    private bool swinging;
    private bool spinning;
    private bool charging;        // the attack key is being held after a swing started
    private float chargeTimer;
    private float swingTimer;
    private float cooldownLeft;
    private int bladeHitCount;    // foes the BLADE hit this swing (chain jumps don't count toward maxTargets)

    // Locked in at the moment the swing / spin starts.
    private int swingFacing;
    private float facingAngle;     // degrees, 0 = right, 90 = up
    private float sweepSign;       // flips for left-facing swings so the blade always travels downward on screen
    private float previousAngle;   // where the blade was at the last hit check (absolute degrees)

    // Hit checks give a foe's centre this many extra degrees of leeway, since foes have width.
    private const float AngleSlack = 10f;
    // A long frame (a hitch) is treated as at most this long, so the blade can't skip over foes.
    private const float MaxStep = 0.05f;
    private const float BoltTime = 0.15f;

    private static Sprite whiteSquare;
    private static Sprite glowSprite;

    // Shared by every SwordController and kept between scenes (the Shop scene and back). Cleared by ResetProgress.
    private static readonly HashSet<string> unlockedNames = new HashSet<string>();
    private static bool unlocksReady;   // true once the "Unlocked At Start" swords have been filled in
    private static string startingSwordName;   // remembered so ResetToStartingSword works from any scene, even with no player in it
    private static readonly Dictionary<string, SwordBonus> upgradeBonuses = new Dictionary<string, SwordBonus>();
    private static SwordBonus playerBonus;
    private static event System.Action UpgradesChanged;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        animator = GetComponent<Animator>();
        playerSprite = GetComponent<SpriteRenderer>();
        if (playerSprite == null) playerSprite = GetComponentInChildren<SpriteRenderer>();   // looked up BEFORE the blade exists, so it can't find itself
        playerHealth = GetComponentInParent<HealthComponent>();
        if (playerHealth == null) playerHealth = GetComponentInChildren<HealthComponent>();

        foeFilter = new ContactFilter2D();
        foeFilter.SetLayerMask(foeMask);
        foeFilter.useTriggers = false;

        if (sfxSource != null) sfxSource.playOnAwake = false;

        if (swords == null || swords.Length == 0)
        {
            Debug.LogError("SwordController on '" + name + "': the Swords list is empty. Add at least one sword. " +
                           "This script is switched off.", this);
            enabled = false;
            return;
        }

        // Pivot (the hand) with the blade as its child. Both are created here, so there is nothing to set up by hand.
        pivot = new GameObject("SwordPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = pivotOffset;

        visualTransform = new GameObject("SwordVisual").transform;
        visualTransform.SetParent(pivot, false);
        visual = visualTransform.gameObject.AddComponent<SpriteRenderer>();
        visual.enabled = false;   // only visible while swinging

        // Fire / ice / lightning sparks, riding on the pivot so they follow the blade.
        fx = new SwordElementFx(pivot, visual.sharedMaterial);

        // The glow halo, behind the player.
        glowRenderer = new GameObject("SpinGlow").AddComponent<SpriteRenderer>();
        glowRenderer.transform.SetParent(transform, false);
        glowRenderer.transform.localPosition = pivotOffset;
        glowRenderer.sprite = GlowSprite();
        glowRenderer.enabled = false;
        StyleRenderer(glowRenderer, -1);

        currentIndex = Mathf.Clamp(startingSword, 0, swords.Length - 1);
        InitUnlocks();
        RebuildEffective();   // works out the numbers actually used (sword + upgrades) and puts the blade on screen
    }

    private void OnEnable()
    {
        UpgradesChanged += RebuildEffective;
    }

    private void OnDisable()
    {
        UpgradesChanged -= RebuildEffective;
    }

    // Copies the player's sorting layer and puts this renderer a few orders above (or below) the player.
    private void StyleRenderer(SpriteRenderer sr, int orderOffset)
    {
        if (playerSprite == null) return;
        sr.sortingLayerID = playerSprite.sortingLayerID;
        sr.sortingOrder = playerSprite.sortingOrder + orderOffset;
    }

    // ------------------------------------------------------------ switching swords

    /// <summary>Equip the sword at this position in the list. Ignored in the middle of a swing or spin.</summary>
    public void EquipSword(int index)
    {
        if (swords == null || swords.Length == 0 || swinging || spinning) return;
        index = Mathf.Clamp(index, 0, swords.Length - 1);
        if (index == currentIndex) return;
        if (!IsUnlocked(index)) return;   // locked swords can't be equipped

        charging = false;   // a half-charged spin doesn't carry over to the new sword
        currentIndex = index;
        ApplyVisual(effective[currentIndex]);
        SwordChanged?.Invoke(effective[currentIndex]);
    }

    /// <summary>+1 = next sword, -1 = previous. Wraps around the list and skips swords that are still locked.</summary>
    public void EquipNext(int step)
    {
        if (swords == null || swords.Length == 0 || step == 0) return;
        int direction = step > 0 ? 1 : -1;
        for (int i = 1; i <= swords.Length; i++)
        {
            int next = ((currentIndex + direction * i) % swords.Length + swords.Length) % swords.Length;
            if (IsUnlocked(next))
            {
                EquipSword(next);
                return;
            }
        }
    }

    private void HandleSwitchInput()
    {
        if (Input.GetKeyDown(nextSwordKey)) EquipNext(1);
        else if (Input.GetKeyDown(previousSwordKey)) EquipNext(-1);
        else if (numberKeysPickSword)
        {
            for (int i = 0; i < swords.Length && i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    EquipSword(i);
                    break;
                }
            }
        }
    }

    // Puts the sword's look onto the blade object, and works out the hit range to match.
    // Placeholder bar, or a Sprite with Fit Hitbox To Sprite OFF: the blade sits halfway out along the Reach number.
    // Sprite with Fit Hitbox To Sprite ON: the sprite's back end sits on the hand and its tip sets the hit range.
    private void ApplyVisual(SwordStats s)
    {
        activeReach = s.reach;
        activeHalfWidth = 0f;

        if (s.sprite != null)
        {
            visual.sprite = s.sprite;
            visual.color = Color.white;
            visualTransform.localScale = new Vector3(SpriteScaleX(s), SpriteScaleY(s), 1f);
            visualTransform.localRotation = Quaternion.Euler(0f, 0f, s.spriteRotationOffset);

            if (s.fitHitboxToSprite)
            {
                Blade blade = MeasureBlade(s);
                visualTransform.localPosition = new Vector3(-blade.start, 0f, 0f);
                activeReach = blade.reach;
                activeHalfWidth = blade.halfThickness;
            }
            else
            {
                visualTransform.localPosition = new Vector3(s.reach * 0.5f, 0f, 0f);
            }
        }
        else
        {
            visual.sprite = WhiteSquare;
            visual.color = s.placeholderColor;
            visualTransform.localScale = new Vector3(s.reach, s.placeholderThickness, 1f);   // spans the hit range exactly
            visualTransform.localRotation = Quaternion.identity;
            visualTransform.localPosition = new Vector3(s.reach * 0.5f, 0f, 0f);
        }

        if (fx != null) fx.Configure(s, activeReach);   // the sparks that fit this sword, along the blade just placed
    }

    // Width and height are in the sprite's OWN directions (as drawn), so they stay correct whatever the rotation offset is.
    private static float SpriteScaleX(SwordStats s) { return (s.spriteScale > 0f ? s.spriteScale : 1f) * (s.spriteWidth > 0f ? s.spriteWidth : 1f); }
    private static float SpriteScaleY(SwordStats s) { return (s.spriteScale > 0f ? s.spriteScale : 1f) * (s.spriteHeight > 0f ? s.spriteHeight : 1f); }

    // Where a sprite blade starts and ends along the swing direction (the pivot's +X), measured from the sprite's own size
    // after scale, width, height and rotation. Sprite.bounds already accounts for Pixels Per Unit.
    private struct Blade
    {
        public float start;           // the sprite's back end, relative to where the sprite sits
        public float reach;           // length from the hand to the tip, plus the padding
        public float halfThickness;   // half the blade's thickness (0 for diagonal sprites, see below)
    }

    private static Blade MeasureBlade(SwordStats s)
    {
        Bounds b = s.sprite.bounds;
        float sx = SpriteScaleX(s);
        float sy = SpriteScaleY(s);
        Quaternion turn = Quaternion.Euler(0f, 0f, s.spriteRotationOffset);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, 0f);
            corner = turn * new Vector3(corner.x * sx, corner.y * sy, 0f);
            minX = Mathf.Min(minX, corner.x);
            maxX = Mathf.Max(maxX, corner.x);
            minY = Mathf.Min(minY, corner.y);
            maxY = Mathf.Max(maxY, corner.y);
        }

        // The thickness is only trustworthy when the sprite is turned by a multiple of 90 degrees. A sprite drawn
        // diagonally has a box much wider than the blade itself, so it adds no extra width (reach is still measured).
        float quarter = Mathf.Abs(Mathf.DeltaAngle(0f, s.spriteRotationOffset)) % 90f;
        bool straight = quarter < 1f || quarter > 89f;

        return new Blade
        {
            start = minX,
            reach = Mathf.Max(0.1f, (maxX - minX) + s.hitReachPadding),
            halfThickness = straight ? (maxY - minY) * 0.5f : 0f
        };
    }

    // The hit range a sword has right now. Used by the Scene view circle, so it also works outside Play mode.
    private static float ReachOf(SwordStats s)
    {
        return (s.sprite != null && s.fitHitboxToSprite) ? MeasureBlade(s).reach : s.reach;
    }

    // Lets you change Sprite Scale / Width / Height / Padding in the Inspector during Play mode and see it at once.
    private void OnValidate()
    {
        if (!Application.isPlaying || visualTransform == null || swords == null || swords.Length == 0) return;
        RebuildEffective();   // re-reads the Inspector numbers (and the upgrades) and redraws the blade
    }

    // A soft white circle (bright in the middle, fading out), 1 unit wide. Tinted with the sword's glow colour.
    private static Sprite GlowSprite()
    {
        if (glowSprite == null)
        {
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            glowSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
        return glowSprite;
    }

    // ------------------------------------------------------------ main loop

    private void Update()
    {
        // Notebook open, stairs menu, pause menu (or foes frozen by hand): everything freezes mid-air and nothing can be
        // hit. Works even if the game sets timeScale to 0.
        bool paused = FoeController.IsPaused;

        // The sparks are separate particle systems that keep running on their own, so they are held still by hand.
        if (fx != null && paused != fxFrozen)
        {
            fxFrozen = paused;
            fx.SetPaused(paused);
        }
        if (paused) return;

        // The frame a menu closes on: the click or key that closed it must not also swing the sword
        // (it would, if "Alt Attack Key" is left click).
        if (FloorController.BlocksInput) return;

        // A dead player can't swing.
        if (playerHealth != null && playerHealth.CurrentHealth <= 0f)
        {
            CancelAttack();
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, MaxStep);

        // Debug box ticked (also live in Play mode): own every sword.
        if (debugUnlockAllSwords)
        {
            if (!debugUnlockApplied)
            {
                debugUnlockApplied = true;
                UnlockAllSwords();
            }
        }
        else
        {
            debugUnlockApplied = false;
        }

        if (spinning)
        {
            UpdateSpin(dt);
            return;
        }

        if (swinging) UpdateSwing(dt);

        UpdateCharge(dt);   // may start the spin

        if (!swinging && !spinning)
        {
            cooldownLeft -= dt;
            if (!charging) HandleSwitchInput();

            if (!charging && cooldownLeft <= 0f && AttackPressed())
            {
                StartSwing();
                charging = true;   // keep holding after this swing and the spin charges up
                chargeTimer = 0f;
            }
        }

        UpdateGlow();
    }

    private bool AttackPressed()
    {
        return Input.GetKeyDown(attackKey) || Input.GetKeyDown(altAttackKey);
    }

    private bool AttackHeld()
    {
        return Input.GetKey(attackKey) || Input.GetKey(altAttackKey);
    }

    private void CancelAttack()
    {
        swinging = false;
        spinning = false;
        charging = false;
        visual.enabled = false;
        fx.Hide();
        glowRenderer.enabled = false;
    }

    // ------------------------------------------------------------ charging the spin

    // While the key stays held the timer runs. Letting go after the sword's Spin Charge Time starts the spin;
    // letting go earlier just cancels the charge (the normal swing already happened).
    private void UpdateCharge(float dt)
    {
        if (!charging) return;

        if (AttackHeld())
        {
            chargeTimer += dt;
            return;
        }

        SwordStats s = effective[currentIndex];
        bool ready = !swinging && chargeTimer >= s.spinChargeTime;
        charging = false;
        if (ready) StartSpin();
    }

    // The glow grows softly while charging, then pulses brightly once the spin is ready.
    private void UpdateGlow()
    {
        if (!charging)
        {
            glowRenderer.enabled = false;
            return;
        }

        SwordStats s = effective[currentIndex];
        float progress = Mathf.Clamp01(chargeTimer / Mathf.Max(0.05f, s.spinChargeTime));
        bool ready = progress >= 1f && !swinging;

        float alpha;
        float size;
        if (ready)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
            alpha = Mathf.Lerp(0.55f, 0.95f, pulse);
            size = Mathf.Lerp(2.4f, 2.9f, pulse);
        }
        else
        {
            alpha = progress * 0.25f;
            size = Mathf.Lerp(1.2f, 2f, progress);
        }

        Color c = s.glowColor;
        c.a = alpha;
        glowRenderer.color = c;
        glowRenderer.transform.localScale = new Vector3(size, size, 1f);
        glowRenderer.enabled = true;
    }

    // ------------------------------------------------------------ normal swing

    private void StartSwing()
    {
        SwordStats s = effective[currentIndex];

        swingFacing = ReadFacing();
        facingAngle = FacingToAngle(swingFacing);
        sweepSign = swingFacing == 3 ? -1f : 1f;   // left-facing: sweep the other way round so it still goes down

        swingTimer = 0f;
        previousAngle = facingAngle + s.arc * 0.5f * sweepSign;
        hitFoes.Clear();
        bladeHitCount = 0;
        swinging = true;

        // Facing up the sword is drawn behind the player, any other way in front.
        StyleRenderer(visual, swingFacing == 1 ? -1 : 1);
        visual.enabled = true;
        fx.Show(visual.sortingLayerID, visual.sortingOrder + (swingFacing == 1 ? 0 : 1));   // sparks just above the blade (level with it when it's behind the player)
        pivot.rotation = Quaternion.Euler(0f, 0f, previousAngle);
    }

    private void UpdateSwing(float dt)
    {
        SwordStats s = effective[currentIndex];

        swingTimer += dt;
        float t = Mathf.Clamp01(swingTimer / Mathf.Max(0.01f, s.swingTime));
        float eased = t * t * (3f - 2f * t);   // slow start, fast chop, soft landing

        float startOffset = s.arc * 0.5f * sweepSign;
        float angle = facingAngle + Mathf.Lerp(startOffset, -startOffset, eased);
        pivot.rotation = Quaternion.Euler(0f, 0f, angle);

        // After the wind-up, check everything the blade has passed over since the last check.
        if (t >= windup)
        {
            HitSweep(s, previousAngle, angle, false);
            previousAngle = angle;
        }

        if (t >= 1f)
        {
            SpawnFireTrails(s, false);
            swinging = false;
            visual.enabled = false;
            fx.Hide();
            cooldownLeft = s.cooldown;
        }
    }

    // ------------------------------------------------------------ spin attack

    private void StartSpin()
    {
        swingFacing = ReadFacing();
        facingAngle = FacingToAngle(swingFacing);

        swingTimer = 0f;
        previousAngle = facingAngle;   // starts where the player is facing and turns clockwise
        hitFoes.Clear();
        bladeHitCount = 0;
        spinning = true;

        glowRenderer.enabled = false;
        StyleRenderer(visual, 1);
        visual.enabled = true;
        fx.Show(visual.sortingLayerID, visual.sortingOrder + 1);
        pivot.rotation = Quaternion.Euler(0f, 0f, previousAngle);
    }

    private void UpdateSpin(float dt)
    {
        SwordStats s = effective[currentIndex];

        swingTimer += dt;
        float t = Mathf.Clamp01(swingTimer / Mathf.Max(0.05f, s.spinTime));
        float angle = facingAngle - 360f * t;
        pivot.rotation = Quaternion.Euler(0f, 0f, angle);

        HitSweep(s, previousAngle, angle, true);
        previousAngle = angle;

        if (t >= 1f)
        {
            SpawnFireTrails(s, true);
            spinning = false;
            visual.enabled = false;
            fx.Hide();
            cooldownLeft = s.cooldown;
        }
    }

    // ------------------------------------------------------------ facing

    // The player's Animator holds the facing: 0 = down, 1 = up, 2 = right, 3 = left.
    private int ReadFacing()
    {
        return animator != null ? animator.GetInteger("Direction") : 0;
    }

    private static float FacingToAngle(int facing)
    {
        switch (facing)
        {
            case 1: return 90f;
            case 2: return 0f;
            case 3: return 180f;
            default: return 270f;
        }
    }

    // ------------------------------------------------------------ hitting foes

    // Hits every foe inside the sword's reach whose angle lies between where the blade was and where it is now
    // (plus a little leeway). Each foe is hit at most once per swing / spin.
    private void HitSweep(SwordStats s, float fromAngle, float toAngle, bool spin)
    {
        Vector2 origin = pivot.position;
        int count = Physics2D.OverlapCircle(origin, activeReach, foeFilter, overlapBuffer);
        float span = Mathf.DeltaAngle(fromAngle, toAngle);   // signed: negative = clockwise

        sweepOrigin = origin;
        sweepFrom = fromAngle;
        sweepSpan = span;
        if (sweepTest == null) sweepTest = PointInSweep;

        for (int i = 0; i < count; i++)
        {
            // Breakable objects (crates, rubble...): one touch breaks them, and they don't use up a Max Targets slot.
            DestructibleObject breakable = overlapBuffer[i].GetComponentInParent<DestructibleObject>();
            if (breakable != null)
            {
                // The breakable decides what breaks: a whole object, or (on a Tilemap) only the tiles under the blade.
                breakable.HitByBlade(overlapBuffer[i], origin, activeReach, sweepTest);
                continue;
            }

            if (!spin && s.maxTargets > 0 && bladeHitCount >= s.maxTargets) continue;   // was "return": keep looping so objects still break

            FoeController foe = overlapBuffer[i].GetComponentInParent<FoeController>();
            if (foe == null || foe.IsDead || hitFoes.Contains(foe)) continue;

            Vector2 toFoe = foe.Center - origin;
            if (toFoe.sqrMagnitude > 0.0001f)   // a foe standing exactly on the pivot counts as in the arc
            {
                float angle = Mathf.Atan2(toFoe.y, toFoe.x) * Mathf.Rad2Deg;
                if (!AngleInSweep(angle, fromAngle, span, AngleSlack + WidthSlack(toFoe.magnitude))) continue;
            }

            hitFoes.Add(foe);
            bladeHitCount++;
            BladeHit(foe, s, toFoe, spin);
        }
    }

    // Extra degrees of leeway for a thick blade: a blade with half-thickness w covers atan(w / distance) either side of its middle line.
    // Capped, so a very close foe can't turn a thick blade into a 180-degree hit.
    private float WidthSlack(float distance)
    {
        if (activeHalfWidth <= 0f) return 0f;
        return Mathf.Min(45f, Mathf.Atan2(activeHalfWidth, Mathf.Max(distance, 0.1f)) * Mathf.Rad2Deg);
    }

    // True if this world point lies inside the part of the arc the blade is passing over right now (same leeway as foes get).
    private bool PointInSweep(Vector2 point)
    {
        Vector2 toPoint = point - sweepOrigin;
        if (toPoint.sqrMagnitude < 0.0001f) return true;   // exactly on the hand counts as in the arc

        float angle = Mathf.Atan2(toPoint.y, toPoint.x) * Mathf.Rad2Deg;
        return AngleInSweep(angle, sweepFrom, sweepSpan, AngleSlack + WidthSlack(toPoint.magnitude));
    }

    private static bool AngleInSweep(float angle, float fromAngle, float span, float slack)
    {
        float relative = Mathf.DeltaAngle(fromAngle, angle);   // -180..180 from where the sweep started
        if (span >= 0f) return relative >= -slack && relative <= span + slack;
        return relative <= slack && relative >= span - slack;
    }

    // One foe touched by the blade: roll the crit, hit it, then start any chain lightning from it.
    private void BladeHit(FoeController foe, SwordStats s, Vector2 toFoe, bool spin)
    {
        float baseDamage = s.damage * (spin ? s.spinDamageMultiplier : 1f);
        bool crit = Random.value < s.critChance;
        float damage = crit ? baseDamage * s.critMultiplier : baseDamage;

        Vector2 foeCenter = foe.Center;   // read BEFORE the hit: a foe that dies has its collider switched off
        Vector2 pushDirection = toFoe.sqrMagnitude > 0.0001f ? toFoe.normalized : AngleToVector(facingAngle);
        ApplyHit(foe, s, damage, pushDirection, crit);

        if (s.chainTargets > 0)
        {
            SpawnBolt(pivot.position, foeCenter, s.boltColor);   // sword to the first foe
            ChainLightning(foeCenter, s, baseDamage);
        }
    }

    // Everything one hit does to a foe. The foe handles the pause, the death and the score itself.
    private void ApplyHit(FoeController foe, SwordStats s, float damage, Vector2 pushDirection, bool crit)
    {
        foe.TakeDamage(damage);

        if (s.lifeSteal > 0f) PlayerHealthAccess.Heal(playerHealth, damage * s.lifeSteal);

        if (crit)
        {
            foe.FlashWhite();
            PlayCritSound();
        }
        if (s.knockback > 0f) foe.Knockback(pushDirection, s.knockback);
        if (s.slowFactor < 1f) foe.ApplySlow(s.slowFactor, s.slowTime);
        if (s.freezeChance > 0f && Random.value < s.freezeChance) foe.Freeze(s.freezeTime);

        if (debugLog)
        {
            Debug.Log(s.swordName + " hit " + foe.name + " for " + damage.ToString("F1") + (crit ? " (CRIT)" : ""));
        }
    }

    private void PlayCritSound()
    {
        if (sfxSource == null || critClip == null) return;
        // Respects the Sound On/Off menu: the SoundSwitch on the AudioSource's object handles the source itself,
        // and the SoundOn check here keeps it silent even if that source is still switched on.
        if (!GameSettings.SoundOn) return;
        if (!sfxSource.isActiveAndEnabled) return;
        sfxSource.PlayOneShot(critClip);
    }

    // ------------------------------------------------------------ lightning

    // Jumps from foe to foe: each jump goes to the nearest foe within range that hasn't been hit this swing,
    // and deals chainFalloff x the damage of the jump before it.
    private void ChainLightning(Vector2 from, SwordStats s, float baseDamage)
    {
        float damage = baseDamage * s.chainDamageMultiplier;

        for (int jump = 0; jump < s.chainTargets; jump++)
        {
            FoeController next = FindChainTarget(from, s.chainRange);
            if (next == null) break;

            Vector2 nextCenter = next.Center;   // read BEFORE the damage: a foe that dies has its collider switched off
            hitFoes.Add(next);
            damage *= s.chainFalloff;
            SpawnBolt(from, nextCenter, s.boltColor);
            next.TakeDamage(damage);

            if (debugLog) Debug.Log(s.swordName + " lightning jumped to " + next.name + " for " + damage.ToString("F1"));
            from = nextCenter;
        }
    }

    private FoeController FindChainTarget(Vector2 from, float range)
    {
        int count = Physics2D.OverlapCircle(from, range, foeFilter, chainBuffer);
        FoeController best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            FoeController foe = chainBuffer[i].GetComponentInParent<FoeController>();
            if (foe == null || foe.IsDead || hitFoes.Contains(foe)) continue;

            float distance = (foe.Center - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = foe;
            }
        }
        return best;
    }

    // A zig-zag bolt made of a few thin stretched squares. It lives for a split second, then is destroyed.
    private void SpawnBolt(Vector2 from, Vector2 to, Color color)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.05f) return;

        Vector2 direction = delta / length;
        Vector2 sideways = new Vector2(-direction.y, direction.x);
        int segments = Mathf.Max(3, Mathf.CeilToInt(length / 0.4f));

        Transform bolt = new GameObject("LightningBolt").transform;
        Vector2 previous = from;
        for (int i = 1; i <= segments; i++)
        {
            Vector2 next = i == segments
                ? to
                : from + direction * (length * i / segments) + sideways * Random.Range(-0.18f, 0.18f);
            AddSegment(bolt, previous, next, 0.09f, color);
            previous = next;
        }
        Destroy(bolt.gameObject, BoltTime);
    }

    private void AddSegment(Transform parent, Vector2 a, Vector2 b, float thickness, Color color)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.001f) return;

        SpriteRenderer sr = new GameObject("Segment").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.sprite = WhiteSquare;
        sr.color = color;
        StyleRenderer(sr, 3);
        sr.transform.position = (a + b) * 0.5f;
        sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        sr.transform.localScale = new Vector3(length, thickness, 1f);
    }

    // ------------------------------------------------------------ fire trails

    // A normal swing leaves one burning line the way the player faces. A spin leaves 8, one every 45 degrees.
    private void SpawnFireTrails(SwordStats s, bool spin)
    {
        if (s.trailDuration <= 0f) return;

        if (spin)
        {
            for (int i = 0; i < 8; i++) SpawnTrail(s, i * 45f);
        }
        else
        {
            SpawnTrail(s, facingAngle);
        }
    }

    private void SpawnTrail(SwordStats s, float angle)
    {
        Vector2 direction = AngleToVector(angle);
        Vector2 start = (Vector2)transform.position + direction * 0.4f;   // starts just in front of the player, on the ground

        SwordFireTrail trail = new GameObject("FireTrail").AddComponent<SwordFireTrail>();
        trail.Begin(start, direction, s.trailLength, s.trailWidth, s.trailDamage, s.trailTickTime, s.trailDuration,
                    foeFilter,
                    playerSprite != null ? playerSprite.sortingLayerID : 0,
                    playerSprite != null ? playerSprite.sortingOrder - 1 : 0);
    }

    private static Vector2 AngleToVector(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    // ------------------------------------------------------------ collecting (unlocks)

    // Fills in the starting unlocks the first time any SwordController wakes up, then keeps them between scenes.
    private void InitUnlocks()
    {
        if (!unlocksReady)
        {
            unlocksReady = true;
            for (int i = 0; i < swords.Length; i++)
            {
                if (swords[i].unlockedAtStart) unlockedNames.Add(swords[i].swordName);
            }
        }

        // The starting sword is always owned, so the player is never empty-handed.
        startingSwordName = swords[Mathf.Clamp(startingSword, 0, swords.Length - 1)].swordName;
        unlockedNames.Add(startingSwordName);

        if (debugUnlockAllSwords)
        {
            debugUnlockApplied = true;
            for (int i = 0; i < swords.Length; i++) unlockedNames.Add(swords[i].swordName);
        }
    }

    /// <summary>True if the player owns the sword at this position in the list.</summary>
    public bool IsUnlocked(int index)
    {
        return swords != null && index >= 0 && index < swords.Length && unlockedNames.Contains(swords[index].swordName);
    }

    /// <summary>Gives the player the sword at this position. Returns true if it was new. Call it from a pickup.</summary>
    public bool UnlockSword(int index)
    {
        if (swords == null || index < 0 || index >= swords.Length) return false;
        if (!unlockedNames.Add(swords[index].swordName)) return false;

        if (debugLog) Debug.Log("Unlocked " + swords[index].swordName);
        SwordUnlocked?.Invoke(effective != null ? effective[index] : swords[index]);
        return true;
    }

    /// <summary>Gives the player every sword in the list (this is what the debug box does).</summary>
    [ContextMenu("Debug: unlock all swords")]
    public void UnlockAllSwords()
    {
        if (swords == null) return;
        for (int i = 0; i < swords.Length; i++) UnlockSword(i);
    }

    /// <summary>
    /// True if the sword with this name is owned. Also true while nothing has been set up yet (for example when the Shop
    /// scene is played on its own), so a shop never shows an empty list just because no gameplay scene ran first.
    /// </summary>
    public static bool IsUnlockedByName(string swordName)
    {
        return !unlocksReady || unlockedNames.Contains(swordName);
    }

    /// <summary>Unlocks a sword by its name, from any script or scene (no SwordController needed).</summary>
    public static void UnlockByName(string swordName)
    {
        unlockedNames.Add(swordName);
    }

    /// <summary>
    /// New run (Retry, Quit to Main Menu): the player owns ONLY the starting sword again and is holding it.
    /// Every other sword is removed until a pickup or the shop gives it back. Works from any scene (GameReset calls it).
    /// Upgrade bonuses are NOT touched: see ResetProgress / ClearUpgradeBonuses for those.
    /// </summary>
    public static void ResetToStartingSword()
    {
        if (startingSwordName == null) return;   // no SwordController has woken up yet this session: nothing to reset

        unlockedNames.Clear();
        unlockedNames.Add(startingSwordName);
        unlocksReady = true;

        // A player that is kept between scenes still holds its old sword: put the starting sword back in its hand.
        SwordController[] players = FindObjectsByType<SwordController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++) players[i].EquipStartingSword();
    }

    // Puts the starting sword in hand and drops whatever was going on (a swing frozen by the pause menu, a half-charged spin).
    private void EquipStartingSword()
    {
        if (swords == null || swords.Length == 0 || effective == null || visualTransform == null) return;

        // A pause holds the sparks still by hand. Let them go first, in the same order a normal resume would.
        if (fx != null && fxFrozen)
        {
            fxFrozen = false;
            fx.SetPaused(false);
        }

        CancelAttack();
        cooldownLeft = 0f;

        int previous = currentIndex;
        currentIndex = Mathf.Clamp(startingSword, 0, swords.Length - 1);
        ApplyVisual(effective[currentIndex]);
        if (currentIndex != previous) SwordChanged?.Invoke(effective[currentIndex]);
    }

    // ------------------------------------------------------------ upgrades

    /// <summary>The bonus bought for one sword (from SwordShop). Replaces that sword's earlier bonus.</summary>
    public static void SetUpgradeBonus(string swordName, SwordBonus bonus)
    {
        upgradeBonuses[swordName] = bonus;
        UpgradesChanged?.Invoke();
    }

    public static SwordBonus GetUpgradeBonus(string swordName)
    {
        SwordBonus bonus;
        return upgradeBonuses.TryGetValue(swordName, out bonus) ? bonus : default(SwordBonus);
    }

    /// <summary>Removes every sword's bonus (used when the sword upgrades are reset).</summary>
    public static void ClearUpgradeBonuses()
    {
        upgradeBonuses.Clear();
        UpgradesChanged?.Invoke();
    }

    /// <summary>The bonus that applies to EVERY sword (from PlayerUpgrades: damage and attack speed).</summary>
    public static void SetPlayerBonus(SwordBonus bonus)
    {
        playerBonus = bonus;
        UpgradesChanged?.Invoke();
    }

    /// <summary>New game: forget unlocks (back to each sword's Unlocked At Start) and all upgrade bonuses.</summary>
    public static void ResetProgress()
    {
        unlockedNames.Clear();
        unlocksReady = false;
        upgradeBonuses.Clear();
        playerBonus = default(SwordBonus);
        UpgradesChanged?.Invoke();
    }

    // Statics outlive Play mode when Domain Reload is switched off in the editor, so clear them on every start.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        unlockedNames.Clear();
        unlocksReady = false;
        startingSwordName = null;
        upgradeBonuses.Clear();
        playerBonus = default(SwordBonus);
        UpgradesChanged = null;
    }

    // Builds the numbers each sword really uses: a copy of the Inspector sword, with the shop and player bonuses on top.
    private void RebuildEffective()
    {
        if (swords == null || swords.Length == 0) return;

        if (effective == null || effective.Length != swords.Length) effective = new SwordStats[swords.Length];
        for (int i = 0; i < swords.Length; i++) effective[i] = BuildEffective(swords[i]);

        if (visualTransform != null)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, swords.Length - 1);
            ApplyVisual(effective[currentIndex]);
        }
    }

    private static SwordStats BuildEffective(SwordStats baseStats)
    {
        SwordStats e = baseStats.Clone();
        SwordBonus b = GetUpgradeBonus(baseStats.swordName) + playerBonus;

        float damageFactor = 1f + b.damagePercent;
        e.damage = baseStats.damage * damageFactor;
        e.trailDamage = baseStats.trailDamage * damageFactor * (1f + b.trailDamagePercent);
        e.chainDamageMultiplier = baseStats.chainDamageMultiplier * (1f + b.chainDamagePercent);   // chain jumps already scale with damage
        e.critChance = Mathf.Clamp01(baseStats.critChance + b.critChanceAdd);
        e.freezeChance = Mathf.Clamp01(baseStats.freezeChance + b.freezeChanceAdd);
        e.lifeSteal = Mathf.Clamp01(baseStats.lifeSteal + b.lifeStealAdd);

        // Faster attacking: swing, spin and the wait between swings all shrink. (The spin CHARGE time is left alone.)
        float speed = 1f + Mathf.Max(-0.9f, b.attackSpeedPercent);
        e.swingTime = Mathf.Max(0.05f, baseStats.swingTime / speed);
        e.spinTime = Mathf.Max(0.1f, baseStats.spinTime / speed);
        e.cooldown = baseStats.cooldown / speed;
        return e;
    }

    // ------------------------------------------------------------ editor helper

    // Select the player in the Scene view to see the current sword's reach as a yellow circle.
    private void OnDrawGizmosSelected()
    {
        if (swords == null || swords.Length == 0) return;
        int index = Application.isPlaying ? currentIndex : Mathf.Clamp(startingSword, 0, swords.Length - 1);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + (Vector3)pivotOffset, ReachOf(swords[index]));
    }
}
