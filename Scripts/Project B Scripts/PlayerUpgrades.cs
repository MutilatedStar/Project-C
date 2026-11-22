using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using Ilumisoft.HealthSystem;
using Cainos.PixelArtTopDown_Basic;

/// <summary>The six things the PLAYER can be upgraded in. The order is the order of the label list in the Inspector.</summary>
public enum PlayerUpgrade
{
    Armor = 0,
    MoveSpeed = 1,
    AttackSpeed = 2,
    Regeneration = 3,
    Damage = 4,
    MaxHealth = 5
}

/// <summary>
/// The shop for the PLAYER. Pairs with SwordShop.cs (the blades' side).
///
///   Armor          takes X% off every hit the player receives. Each armor tier can also change how the player looks.
///   Move Speed     +X% walking and sprinting speed
///   Attack Speed   +X% faster swings, spins and cooldowns, with every sword
///   Regeneration   heals X HP per second
///   Damage         +X% damage with every sword (blade hits, spins, chain jumps and fire trails)
///   Max HP         +X maximum HP (and that much is healed on the spot)
///
/// WHERE IT GOES: on the PLAYER in the gameplay scene (next to TopDownCharacterController and the HealthComponent).
/// If the Shop is its own scene, also put a copy in that scene (copy the component, then Paste Component Values): the shop
/// scene's copy does the buying, the gameplay copy applies the saved levels to the player. Levels are static, so they
/// survive the scene change. Call PlayerUpgrades.ResetAll() on a new game.
///
/// ARMOR NEEDS ONE LINE IN THE FOE SCRIPT for exact results: wherever a foe hurts the player, pass the damage through
///     damage = PlayerUpgrades.ModifyIncomingDamage(damage);
/// Until that line exists, armor works anyway by handing back its share of every drop in the player's health a frame
/// later (it can't save a hit that would have killed outright). It switches itself off once the line is used.
///
/// ARMOR LOOK (optional): under Armor Looks, add one entry per tier. "From Level" is the first armor level that uses it.
///   Animator Override: a different set of player animations (make it with Create > Animator Override Controller, point it
///     at the player's controller and swap in the armored clips). This is the real "different sprite" option.
///   Tint: a colour multiplied onto the player's sprite. Needs no art at all.
///
/// BUILDING THE MENU (all optional): buttons call BuyArmor(), BuyMoveSpeed(), BuyAttackSpeed(), BuyRegeneration(),
/// BuyDamage(), BuyMaxHealth(). Drag a Text into each Upgrade Labels slot (same order as the enum) to have them fill themselves.
///
/// TESTING WITHOUT BUYING: tick Debug Override Levels and type the levels in. Works live in Play mode.
/// </summary>
[DisallowMultipleComponent]
public class PlayerUpgrades : MonoBehaviour
{
    // ------------------------------------------------------------ data

    [System.Serializable]
    public class ArmorLook
    {
        [Tooltip("First armor level that uses this look. The highest entry at or below the current armor level wins.")]
        [Min(1)] public int fromLevel = 1;
        [Tooltip("Optional. A different set of player animations (an Animator Override Controller). Empty = keep the normal animations.")]
        public RuntimeAnimatorController animatorOverride;
        [Tooltip("Optional. Multiplied onto the player's sprite colour. White = no change.")]
        public Color tint = Color.white;
    }

    [System.Serializable]
    public class LevelSet
    {
        [Min(0)] public int armor;
        [Min(0)] public int moveSpeed;
        [Min(0)] public int attackSpeed;
        [Min(0)] public int regeneration;
        [Min(0)] public int damage;
        [Min(0)] public int maxHealth;

        public int Get(PlayerUpgrade u)
        {
            switch (u)
            {
                case PlayerUpgrade.Armor: return armor;
                case PlayerUpgrade.MoveSpeed: return moveSpeed;
                case PlayerUpgrade.AttackSpeed: return attackSpeed;
                case PlayerUpgrade.Regeneration: return regeneration;
                case PlayerUpgrade.Damage: return damage;
                default: return maxHealth;
            }
        }
    }

    /// <summary>The player's totals after all upgrades. Static, so any script can read PlayerUpgrades.Current.</summary>
    public struct Stats
    {
        public int armorLevel;
        public float armorReduction;      // 0.24 = 24% less damage taken
        public float moveSpeedPercent;    // 0.16 = 16% faster
        public float attackSpeedPercent;
        public float regenPerSecond;
        public float damagePercent;
        public float maxHealthAdd;
    }

    // ------------------------------------------------------------ inspector

    [Header("Player (found automatically if left empty)")]
    [SerializeField] private TopDownCharacterController player;
    [SerializeField] private HealthComponent health;

    [Header("Upgrade tracks")]
    [Tooltip("Armor: Per Level 0.08 = 8% less damage taken per level (never more than 80% in total).")]
    [SerializeField] private UpgradeTrack armor = new UpgradeTrack { maxLevel = 5, baseCost = 80, costGrowth = 1.6f, perLevel = 0.08f };
    [Tooltip("Move Speed: Per Level 0.08 = +8% speed per level.")]
    [SerializeField] private UpgradeTrack moveSpeed = new UpgradeTrack { maxLevel = 5, baseCost = 50, costGrowth = 1.5f, perLevel = 0.08f };
    [Tooltip("Attack Speed: Per Level 0.10 = +10% faster swings per level.")]
    [SerializeField] private UpgradeTrack attackSpeed = new UpgradeTrack { maxLevel = 5, baseCost = 70, costGrowth = 1.6f, perLevel = 0.1f };
    [Tooltip("Regeneration: Per Level 0.5 = +0.5 HP per second per level.")]
    [SerializeField] private UpgradeTrack regeneration = new UpgradeTrack { maxLevel = 5, baseCost = 60, costGrowth = 1.6f, perLevel = 0.5f };
    [Tooltip("Damage: Per Level 0.10 = +10% damage with every sword per level.")]
    [SerializeField] private UpgradeTrack damage = new UpgradeTrack { maxLevel = 5, baseCost = 70, costGrowth = 1.6f, perLevel = 0.1f };
    [Tooltip("Max HP: Per Level 20 = +20 maximum HP per level.")]
    [SerializeField] private UpgradeTrack maxHealth = new UpgradeTrack { maxLevel = 5, baseCost = 50, costGrowth = 1.5f, perLevel = 20f };

    [Header("Armor look (optional)")]
    [SerializeField] private ArmorLook[] armorLooks = new ArmorLook[0];

    [Header("Shop menu (all optional)")]
    [FormerlySerializedAs("coinsLabel")]
    [SerializeField] private Text scoreLabel;
    [Tooltip("Six Texts, in this order: Armor, Move Speed, Attack Speed, Regeneration, Damage, Max HP.")]
    [SerializeField] private Text[] upgradeLabels = new Text[6];

    [Header("Debug")]
    [Tooltip("ON: ignore what was bought and use the Debug Levels instead. Buying is switched off while this is ON. Works live in Play mode.")]
    [SerializeField] private bool debugOverrideLevels = false;
    [Tooltip("Above 0: EVERY upgrade is set to this level (capped at its max), overriding the numbers below. 0 = use the numbers below.")]
    [Min(0)]
    [SerializeField] private int debugAllLevels = 0;
    [SerializeField] private LevelSet debugLevels = new LevelSet();
    [Tooltip("ON: buying costs nothing.")]
    [SerializeField] private bool debugFreePurchases = false;

    // ------------------------------------------------------------ static state (survives scene changes)

    private static readonly int[] savedLevels = new int[6];
    private static bool hookUsed;

    /// <summary>The player's totals right now.</summary>
    public static Stats Current { get; private set; }

    /// <summary>Fires when any player upgrade is bought, reset or recalculated.</summary>
    public static event System.Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        System.Array.Clear(savedLevels, 0, savedLevels.Length);
        Current = default(Stats);
        hookUsed = false;
        Changed = null;
    }

    /// <summary>New game: forget every bought player upgrade.</summary>
    public static void ResetAll()
    {
        System.Array.Clear(savedLevels, 0, savedLevels.Length);
        Current = default(Stats);
        SwordController.SetPlayerBonus(default(SwordController.SwordBonus));
        Changed?.Invoke();
    }

    /// <summary>Call this on every hit the player takes: it returns the damage after armor.</summary>
    public static float ModifyIncomingDamage(float incoming)
    {
        hookUsed = true;
        return incoming * (1f - Current.armorReduction);
    }

    // ------------------------------------------------------------ player state

    private Animator animator;
    private SpriteRenderer playerSprite;

    private bool baseCaptured;
    private float baseSpeed;
    private float baseMaxHealth;
    private float appliedMaxHealthAdd;
    private RuntimeAnimatorController baseController;
    private Color baseColor = Color.white;

    private bool started;
    private float lastHealth = -1f;
    private float regenTimer;
    private const float RegenTick = 0.5f;   // regeneration heals in half-second steps

    // ------------------------------------------------------------ lifecycle

    private void OnEnable()
    {
        ShopScore.Changed += Refresh;
    }

    private void OnDisable()
    {
        ShopScore.Changed -= Refresh;
    }

    private void Start()
    {
        if (player == null) player = GetComponent<TopDownCharacterController>();
        if (player == null) player = FindFirstObjectByType<TopDownCharacterController>();   // stays empty in the Shop scene, which is fine

        if (health == null) health = GetComponentInParent<HealthComponent>();
        if (health == null) health = GetComponentInChildren<HealthComponent>();
        if (health == null && player != null) health = player.GetComponentInChildren<HealthComponent>();

        if (player != null)
        {
            animator = player.GetComponent<Animator>();
            playerSprite = player.GetComponent<SpriteRenderer>();
            if (playerSprite == null) playerSprite = player.GetComponentInChildren<SpriteRenderer>();
        }

        started = true;
        Recalculate();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || !started) return;   // before Start the player hasn't been found yet
        Recalculate();
    }

    private void Update()
    {
        if (health == null || FoeController.IsPaused) return;

        float current = health.CurrentHealth;
        if (current <= 0f)
        {
            lastHealth = current;
            return;
        }

        // Armor without the foe-script line: hand back the armor's share of any drop since last frame.
        if (!hookUsed && Current.armorReduction > 0f && lastHealth > 0f && current < lastHealth - 0.001f)
        {
            PlayerHealthAccess.Heal(health, (lastHealth - current) * Current.armorReduction);
            current = health.CurrentHealth;
        }

        // Regeneration.
        if (Current.regenPerSecond > 0f && current < health.MaxHealth)
        {
            regenTimer += Time.deltaTime;
            if (regenTimer >= RegenTick)
            {
                PlayerHealthAccess.Heal(health, Current.regenPerSecond * regenTimer);
                regenTimer = 0f;
                current = health.CurrentHealth;
            }
        }
        else
        {
            regenTimer = 0f;
        }

        lastHealth = current;
    }

    // ------------------------------------------------------------ levels and totals

    private UpgradeTrack TrackOf(PlayerUpgrade u)
    {
        switch (u)
        {
            case PlayerUpgrade.Armor: return armor;
            case PlayerUpgrade.MoveSpeed: return moveSpeed;
            case PlayerUpgrade.AttackSpeed: return attackSpeed;
            case PlayerUpgrade.Regeneration: return regeneration;
            case PlayerUpgrade.Damage: return damage;
            default: return maxHealth;
        }
    }

    private static string NameOf(PlayerUpgrade u)
    {
        switch (u)
        {
            case PlayerUpgrade.Armor: return "Armor";
            case PlayerUpgrade.MoveSpeed: return "Move Speed";
            case PlayerUpgrade.AttackSpeed: return "Attack Speed";
            case PlayerUpgrade.Regeneration: return "Regeneration";
            case PlayerUpgrade.Damage: return "Damage";
            default: return "Max HP";
        }
    }

    // The level actually in force: the debug number while the override is ON, otherwise what was bought.
    private int UsedLevel(PlayerUpgrade u)
    {
        int level = debugOverrideLevels
            ? (debugAllLevels > 0 ? debugAllLevels : debugLevels.Get(u))
            : savedLevels[(int)u];
        return Mathf.Clamp(level, 0, TrackOf(u).maxLevel);
    }

    // Works out the totals, tells the swords, and puts the result on the player (if this scene has one).
    private void Recalculate()
    {
        Stats stats = new Stats();
        stats.armorLevel = UsedLevel(PlayerUpgrade.Armor);
        stats.armorReduction = Mathf.Min(0.8f, stats.armorLevel * armor.perLevel);
        stats.moveSpeedPercent = UsedLevel(PlayerUpgrade.MoveSpeed) * moveSpeed.perLevel;
        stats.attackSpeedPercent = UsedLevel(PlayerUpgrade.AttackSpeed) * attackSpeed.perLevel;
        stats.regenPerSecond = UsedLevel(PlayerUpgrade.Regeneration) * regeneration.perLevel;
        stats.damagePercent = UsedLevel(PlayerUpgrade.Damage) * damage.perLevel;
        stats.maxHealthAdd = UsedLevel(PlayerUpgrade.MaxHealth) * maxHealth.perLevel;
        Current = stats;

        SwordController.SwordBonus bonus = new SwordController.SwordBonus();
        bonus.damagePercent = stats.damagePercent;
        bonus.attackSpeedPercent = stats.attackSpeedPercent;
        SwordController.SetPlayerBonus(bonus);

        ApplyToPlayer();
        Changed?.Invoke();
        Refresh();
    }

    private void ApplyToPlayer()
    {
        if (!baseCaptured)
        {
            // The values the scene was built with, noted once, so upgrades are always "base + bonus" and never stack on themselves.
            if (player != null) baseSpeed = player.speed;
            if (health != null) baseMaxHealth = health.MaxHealth;
            if (animator != null) baseController = animator.runtimeAnimatorController;
            if (playerSprite != null) baseColor = playerSprite.color;
            baseCaptured = true;
        }

        if (player != null) player.speed = baseSpeed * (1f + Current.moveSpeedPercent);

        if (health != null)
        {
            float change = Current.maxHealthAdd - appliedMaxHealthAdd;
            if (!Mathf.Approximately(change, 0f))
            {
                PlayerHealthAccess.SetMaxHealth(health, baseMaxHealth + Current.maxHealthAdd);
                if (change > 0f) PlayerHealthAccess.Heal(health, change);   // the new HP arrive filled
                appliedMaxHealthAdd = Current.maxHealthAdd;
            }
        }

        ApplyArmorLook();
    }

    private void ApplyArmorLook()
    {
        if (armorLooks == null || armorLooks.Length == 0) return;

        ArmorLook look = null;
        for (int i = 0; i < armorLooks.Length; i++)
        {
            ArmorLook candidate = armorLooks[i];
            if (candidate == null || candidate.fromLevel > Current.armorLevel) continue;
            if (look == null || candidate.fromLevel >= look.fromLevel) look = candidate;
        }

        if (animator != null)
        {
            RuntimeAnimatorController target = look != null && look.animatorOverride != null ? look.animatorOverride : baseController;
            if (animator.runtimeAnimatorController != target) animator.runtimeAnimatorController = target;
        }
        if (playerSprite != null)
        {
            playerSprite.color = look != null ? baseColor * look.tint : baseColor;
        }
    }

    // ------------------------------------------------------------ buying

    /// <summary>Buys the next level of this upgrade. Returns true if it went through.</summary>
    public bool TryBuy(PlayerUpgrade u)
    {
        if (debugOverrideLevels)
        {
            Debug.Log("PlayerUpgrades: Debug Override Levels is ON, so buying is switched off. Untick it to buy.");
            return false;
        }

        UpgradeTrack track = TrackOf(u);
        int level = savedLevels[(int)u];
        if (level >= track.maxLevel) return false;

        if (!debugFreePurchases && !ShopScore.TrySpend(track.CostOfNext(level))) return false;   // not enough score

        savedLevels[(int)u] = level + 1;
        Recalculate();
        return true;
    }

    // Button-friendly versions (a Button's On Click can only call methods that return nothing).
    public void BuyArmor() { TryBuy(PlayerUpgrade.Armor); }
    public void BuyMoveSpeed() { TryBuy(PlayerUpgrade.MoveSpeed); }
    public void BuyAttackSpeed() { TryBuy(PlayerUpgrade.AttackSpeed); }
    public void BuyRegeneration() { TryBuy(PlayerUpgrade.Regeneration); }
    public void BuyDamage() { TryBuy(PlayerUpgrade.Damage); }
    public void BuyMaxHealth() { TryBuy(PlayerUpgrade.MaxHealth); }

    /// <summary>For one button that picks the upgrade by number (0 = Armor ... 5 = Max HP).</summary>
    public void BuyUpgrade(int id)
    {
        if (id >= 0 && id < 6) TryBuy((PlayerUpgrade)id);
    }

    // ------------------------------------------------------------ labels

    /// <summary>The text for one upgrade, e.g. "Armor   Lv 2/5   -16% damage taken   130 score".</summary>
    public string Describe(PlayerUpgrade u)
    {
        UpgradeTrack track = TrackOf(u);
        int level = UsedLevel(u);

        string effect;
        switch (u)
        {
            case PlayerUpgrade.Armor: effect = "-" + Mathf.RoundToInt(Mathf.Min(0.8f, level * track.perLevel) * 100f) + "% damage taken"; break;
            case PlayerUpgrade.Regeneration: effect = "+" + (level * track.perLevel).ToString("0.#") + " HP/s"; break;
            case PlayerUpgrade.MaxHealth: effect = "+" + Mathf.RoundToInt(level * track.perLevel) + " HP"; break;
            default: effect = "+" + Mathf.RoundToInt(level * track.perLevel * 100f) + "%"; break;
        }

        string price;
        if (level >= track.maxLevel) price = "MAX";
        else if (debugFreePurchases) price = "FREE";
        else price = track.CostOfNext(level) + " score";

        return NameOf(u) + "   Lv " + level + "/" + track.maxLevel + "   " + effect + "   " + price + (debugOverrideLevels ? "   (debug)" : "");
    }

    /// <summary>Redraws every label. Called automatically after a purchase and when the score changes.</summary>
    public void Refresh()
    {
        if (scoreLabel != null) scoreLabel.text = "Score: " + ShopScore.Score;

        if (upgradeLabels == null) return;
        for (int i = 0; i < 6 && i < upgradeLabels.Length; i++)
        {
            if (upgradeLabels[i] != null) upgradeLabels[i].text = Describe((PlayerUpgrade)i);
        }
    }

    // Right-click the component's title bar to run these (in Play mode, they help when testing).
    [ContextMenu("Debug: add 1000 score")]
    private void DebugAddScore() { ShopScore.AddScore(1000); }

    [ContextMenu("Debug: reset all player upgrades")]
    private void DebugResetUpgrades()
    {
        ResetAll();
        Recalculate();
    }
}
