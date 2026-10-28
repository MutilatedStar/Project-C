using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>The six things a blade can be upgraded in. The order is the order of the label list in the Inspector.</summary>
public enum SwordUpgrade
{
    Damage = 0,
    CritChance = 1,
    ChainDamage = 2,
    FireTrailDamage = 3,
    FreezeChance = 4,
    LifeSteal = 5
}

/// <summary>
/// The shop for the BLADES. Every sword has its own upgrade levels. Pairs with PlayerUpgrades.cs (the player's side).
///
/// What each upgrade does to that one sword:
///   Damage            +X% damage on every hit, spin hit, chain jump and fire-trail tick
///   Crit Chance       +X percentage points of crit chance
///   Chain Damage      +X% damage on the lightning jumps only
///   Fire Trail Damage +X% damage of the burning ground only
///   Freeze Chance     +X percentage points of chance to freeze a foe solid
///   Life Steal        the sword heals the player for X% of the damage it deals (only offered on the Dagger by default)
///
/// WHERE IT GOES: on any object. If the Shop is a menu in the gameplay scene, one copy there is enough. If the Shop is
/// its own scene, put a copy in BOTH scenes (copy the component, then Paste Component Values) so the numbers match:
/// the shop scene's copy does the buying, the gameplay scene's copy re-applies the saved levels (and the debug levels).
/// Upgrade levels and score are static, so they survive the scene change. Call SwordShop.ResetAll() on a new game.
///
/// BUILDING THE MENU (all optional, the buying works without any of it):
///   - Button "Next Sword" / "Previous Sword" -> NextSword() / PreviousSword()
///   - One button per upgrade -> BuyDamage(), BuyCritChance(), BuyChainDamage(), BuyFireTrailDamage(), BuyFreezeChance(), BuyLifeSteal()
///   - Drag a Text into each Upgrade Labels slot (same order as the enum above) and they fill themselves in, e.g.
///     "Damage   Lv 2/5   +30%   120 score" or "...MAX". Drag the buttons into Upgrade Rows to hide the ones the
///     selected sword doesn't sell.
///
/// TESTING WITHOUT BUYING: tick Debug Override Levels. Then every sword uses the Debug Levels numbers on its own entry
/// (or Debug All Levels for every upgrade of every sword at once). It also works live in Play mode.
/// </summary>
[DisallowMultipleComponent]
public class SwordShop : MonoBehaviour
{
    // ------------------------------------------------------------ data

    [System.Serializable]
    public class LevelSet
    {
        [Min(0)] public int damage;
        [Min(0)] public int critChance;
        [Min(0)] public int chainDamage;
        [Min(0)] public int fireTrailDamage;
        [Min(0)] public int freezeChance;
        [Min(0)] public int lifeSteal;

        public int Get(SwordUpgrade u)
        {
            switch (u)
            {
                case SwordUpgrade.Damage: return damage;
                case SwordUpgrade.CritChance: return critChance;
                case SwordUpgrade.ChainDamage: return chainDamage;
                case SwordUpgrade.FireTrailDamage: return fireTrailDamage;
                case SwordUpgrade.FreezeChance: return freezeChance;
                default: return lifeSteal;
            }
        }
    }

    [System.Serializable]
    public class SwordEntry
    {
        [Tooltip("Must match the sword's Sword Name in the SwordController list exactly (that is how the two find each other).")]
        public string swordName = "Short Sword";
        [Tooltip("Makes this sword's upgrades cheaper (below 1) or pricier (above 1).")]
        [Min(0.1f)] public float costMultiplier = 1f;

        [Header("Upgrades this sword can buy")]
        public bool damage = true;
        public bool critChance = true;
        [Tooltip("Only useful on a sword with Chain Targets above 0.")]
        public bool chainDamage = false;
        [Tooltip("Only useful on a sword with a Fire Trail.")]
        public bool fireTrailDamage = false;
        [Tooltip("Only useful on a sword with Ice.")]
        public bool freezeChance = false;
        public bool lifeSteal = false;

        [Header("Debug levels (used while Debug Override Levels is ON)")]
        public LevelSet debugLevels = new LevelSet();

        public bool Offers(SwordUpgrade u)
        {
            switch (u)
            {
                case SwordUpgrade.Damage: return damage;
                case SwordUpgrade.CritChance: return critChance;
                case SwordUpgrade.ChainDamage: return chainDamage;
                case SwordUpgrade.FireTrailDamage: return fireTrailDamage;
                case SwordUpgrade.FreezeChance: return freezeChance;
                default: return lifeSteal;
            }
        }
    }

    // ------------------------------------------------------------ inspector

    [Header("Swords in the shop")]
    [Tooltip("One entry per sword, named exactly like the sword. The six starting swords are pre-filled (only when the component is first added).")]
    [SerializeField] private SwordEntry[] swords =
    {
        MakeEntry("Short Sword"),
        MakeEntry("Broadsword"),
        MakeEntry("Dagger", lifeSteal: true),
        MakeEntry("Lightning Sword", chain: true),
        MakeEntry("Fire Sword", fire: true),
        MakeEntry("Ice Sword", freeze: true)
    };

    [Header("Upgrade tracks (the same for every sword)")]
    [Tooltip("Damage: Per Level 0.15 = +15% damage per level.")]
    [SerializeField] private UpgradeTrack damage = new UpgradeTrack { maxLevel = 5, baseCost = 60, costGrowth = 1.5f, perLevel = 0.15f };
    [Tooltip("Crit Chance: Per Level 0.03 = +3 percentage points per level.")]
    [SerializeField] private UpgradeTrack critChance = new UpgradeTrack { maxLevel = 5, baseCost = 70, costGrowth = 1.5f, perLevel = 0.03f };
    [Tooltip("Chain Damage: Per Level 0.25 = +25% lightning damage per level.")]
    [SerializeField] private UpgradeTrack chainDamage = new UpgradeTrack { maxLevel = 5, baseCost = 60, costGrowth = 1.5f, perLevel = 0.25f };
    [Tooltip("Fire Trail Damage: Per Level 0.30 = +30% burning damage per level.")]
    [SerializeField] private UpgradeTrack fireTrailDamage = new UpgradeTrack { maxLevel = 5, baseCost = 60, costGrowth = 1.5f, perLevel = 0.3f };
    [Tooltip("Freeze Chance: Per Level 0.04 = +4 percentage points per level.")]
    [SerializeField] private UpgradeTrack freezeChance = new UpgradeTrack { maxLevel = 5, baseCost = 70, costGrowth = 1.5f, perLevel = 0.04f };
    [Tooltip("Life Steal: Per Level 0.04 = the sword heals 4% of its damage per level.")]
    [SerializeField] private UpgradeTrack lifeSteal = new UpgradeTrack { maxLevel = 4, baseCost = 80, costGrowth = 1.6f, perLevel = 0.04f };

    [Header("Shop menu (all optional)")]
    [Tooltip("ON: swords the player hasn't unlocked yet don't show up in the shop.")]
    [SerializeField] private bool onlyShowUnlockedSwords = true;
    [SerializeField] private Text swordNameLabel;
    [FormerlySerializedAs("coinsLabel")] [SerializeField] private Text scoreLabel;
    [Tooltip("Six Texts, in this order: Damage, Crit Chance, Chain Damage, Fire Trail Damage, Freeze Chance, Life Steal.")]
    [SerializeField] private Text[] upgradeLabels = new Text[6];
    [Tooltip("Optional, same order: the button (or row) for each upgrade. It is hidden when the selected sword doesn't sell it.")]
    [SerializeField] private GameObject[] upgradeRows = new GameObject[6];

    [Header("Debug")]
    [Tooltip("ON: ignore what was bought and use the Debug Levels instead. Buying is switched off while this is ON. Works live in Play mode.")]
    [SerializeField] private bool debugOverrideLevels = false;
    [Tooltip("Above 0: EVERY upgrade a sword offers is set to this level (capped at its max), overriding the per-sword Debug Levels. 0 = use the per-sword numbers.")]
    [Min(0)]
    [SerializeField] private int debugAllLevels = 0;
    [Tooltip("ON: buying costs nothing.")]
    [SerializeField] private bool debugFreePurchases = false;

    // ------------------------------------------------------------ saved state (static: survives scene changes)

    private static readonly Dictionary<string, int[]> savedLevels = new Dictionary<string, int[]>();

    /// <summary>Fires when any sword upgrade is bought or reset.</summary>
    public static event System.Action Changed;

    private int selected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        savedLevels.Clear();
        Changed = null;
    }

    /// <summary>New game: forget every bought sword upgrade.</summary>
    public static void ResetAll()
    {
        savedLevels.Clear();
        SwordController.ClearUpgradeBonuses();
        Changed?.Invoke();
    }

    private static SwordEntry MakeEntry(string name, bool chain = false, bool fire = false, bool freeze = false, bool lifeSteal = false)
    {
        return new SwordEntry
        {
            swordName = name,
            chainDamage = chain,
            fireTrailDamage = fire,
            freezeChance = freeze,
            lifeSteal = lifeSteal
        };
    }

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
        PushAll();   // hand every sword's bonus to the SwordController (this is what applies the levels in the gameplay scene)
        selected = FirstListed();
        Refresh();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        PushAll();
        Refresh();
    }

    // ------------------------------------------------------------ levels

    private UpgradeTrack TrackOf(SwordUpgrade u)
    {
        switch (u)
        {
            case SwordUpgrade.Damage: return damage;
            case SwordUpgrade.CritChance: return critChance;
            case SwordUpgrade.ChainDamage: return chainDamage;
            case SwordUpgrade.FireTrailDamage: return fireTrailDamage;
            case SwordUpgrade.FreezeChance: return freezeChance;
            default: return lifeSteal;
        }
    }

    private static string NameOf(SwordUpgrade u)
    {
        switch (u)
        {
            case SwordUpgrade.Damage: return "Damage";
            case SwordUpgrade.CritChance: return "Crit Chance";
            case SwordUpgrade.ChainDamage: return "Chain Damage";
            case SwordUpgrade.FireTrailDamage: return "Fire Trail Damage";
            case SwordUpgrade.FreezeChance: return "Freeze Chance";
            default: return "Life Steal";
        }
    }

    private static int SavedLevel(string swordName, SwordUpgrade u)
    {
        int[] levels;
        return savedLevels.TryGetValue(swordName, out levels) ? levels[(int)u] : 0;
    }

    private static void SetSavedLevel(string swordName, SwordUpgrade u, int level)
    {
        int[] levels;
        if (!savedLevels.TryGetValue(swordName, out levels))
        {
            levels = new int[6];
            savedLevels[swordName] = levels;
        }
        levels[(int)u] = level;
    }

    // The level actually in force: the debug number while the override is ON, otherwise what was bought.
    private int UsedLevel(SwordEntry e, SwordUpgrade u)
    {
        if (!e.Offers(u)) return 0;
        if (debugOverrideLevels)
        {
            int level = debugAllLevels > 0 ? debugAllLevels : e.debugLevels.Get(u);
            return Mathf.Clamp(level, 0, TrackOf(u).maxLevel);
        }
        return Mathf.Clamp(SavedLevel(e.swordName, u), 0, TrackOf(u).maxLevel);
    }

    private void PushBonus(SwordEntry e)
    {
        if (e == null || string.IsNullOrEmpty(e.swordName)) return;

        SwordController.SwordBonus bonus = new SwordController.SwordBonus();
        bonus.damagePercent = UsedLevel(e, SwordUpgrade.Damage) * damage.perLevel;
        bonus.critChanceAdd = UsedLevel(e, SwordUpgrade.CritChance) * critChance.perLevel;
        bonus.chainDamagePercent = UsedLevel(e, SwordUpgrade.ChainDamage) * chainDamage.perLevel;
        bonus.trailDamagePercent = UsedLevel(e, SwordUpgrade.FireTrailDamage) * fireTrailDamage.perLevel;
        bonus.freezeChanceAdd = UsedLevel(e, SwordUpgrade.FreezeChance) * freezeChance.perLevel;
        bonus.lifeStealAdd = UsedLevel(e, SwordUpgrade.LifeSteal) * lifeSteal.perLevel;
        SwordController.SetUpgradeBonus(e.swordName, bonus);
    }

    private void PushAll()
    {
        if (swords == null) return;
        for (int i = 0; i < swords.Length; i++) PushBonus(swords[i]);
    }

    // ------------------------------------------------------------ buying

    /// <summary>Buys the next level of this upgrade for the selected sword. Returns true if it went through.</summary>
    public bool TryBuy(SwordUpgrade u)
    {
        SwordEntry e = Selected;
        if (e == null || !e.Offers(u)) return false;

        if (debugOverrideLevels)
        {
            Debug.Log("SwordShop: Debug Override Levels is ON, so buying is switched off. Untick it to buy.");
            return false;
        }

        UpgradeTrack track = TrackOf(u);
        int level = SavedLevel(e.swordName, u);
        if (level >= track.maxLevel) return false;

        if (!debugFreePurchases && !ShopScore.TrySpend(track.CostOfNext(level, e.costMultiplier))) return false;   // not enough score

        SetSavedLevel(e.swordName, u, level + 1);
        PushBonus(e);
        Changed?.Invoke();
        Refresh();
        return true;
    }

    // Button-friendly versions (a Button's On Click can only call methods that return nothing).
    public void BuyDamage() { TryBuy(SwordUpgrade.Damage); }
    public void BuyCritChance() { TryBuy(SwordUpgrade.CritChance); }
    public void BuyChainDamage() { TryBuy(SwordUpgrade.ChainDamage); }
    public void BuyFireTrailDamage() { TryBuy(SwordUpgrade.FireTrailDamage); }
    public void BuyFreezeChance() { TryBuy(SwordUpgrade.FreezeChance); }
    public void BuyLifeSteal() { TryBuy(SwordUpgrade.LifeSteal); }

    /// <summary>For one button that picks the upgrade by number (0 = Damage ... 5 = Life Steal).</summary>
    public void BuyUpgrade(int id)
    {
        if (id >= 0 && id < 6) TryBuy((SwordUpgrade)id);
    }

    // ------------------------------------------------------------ choosing the sword

    public SwordEntry Selected
    {
        get { return swords != null && selected >= 0 && selected < swords.Length ? swords[selected] : null; }
    }

    public void NextSword() { Step(1); }
    public void PreviousSword() { Step(-1); }

    private void Step(int direction)
    {
        if (swords == null || swords.Length == 0) return;
        for (int i = 1; i <= swords.Length; i++)
        {
            int index = ((selected + direction * i) % swords.Length + swords.Length) % swords.Length;
            if (IsListed(swords[index]))
            {
                selected = index;
                break;
            }
        }
        Refresh();
    }

    private bool IsListed(SwordEntry e)
    {
        return !onlyShowUnlockedSwords || SwordController.IsUnlockedByName(e.swordName);
    }

    private int FirstListed()
    {
        if (swords == null) return 0;
        for (int i = 0; i < swords.Length; i++)
        {
            if (IsListed(swords[i])) return i;
        }
        return 0;
    }

    // ------------------------------------------------------------ labels

    /// <summary>The text for one upgrade of the selected sword, e.g. "Damage   Lv 2/5   +30%   120 score".</summary>
    public string Describe(SwordUpgrade u)
    {
        SwordEntry e = Selected;
        if (e == null || !e.Offers(u)) return "";

        UpgradeTrack track = TrackOf(u);
        int level = UsedLevel(e, u);
        string effect = "+" + Mathf.RoundToInt(level * track.perLevel * 100f) + "%";

        string price;
        if (level >= track.maxLevel) price = "MAX";
        else if (debugFreePurchases) price = "FREE";
        else price = track.CostOfNext(level, e.costMultiplier) + " score";

        return NameOf(u) + "   Lv " + level + "/" + track.maxLevel + "   " + effect + "   " + price + (debugOverrideLevels ? "   (debug)" : "");
    }

    /// <summary>Redraws every label. Called automatically after a purchase and when the score changes.</summary>
    public void Refresh()
    {
        SwordEntry e = Selected;
        bool listed = e != null && IsListed(e);

        if (swordNameLabel != null) swordNameLabel.text = listed ? e.swordName : "No swords yet";
        if (scoreLabel != null) scoreLabel.text = "Score: " + ShopScore.Score;

        for (int i = 0; i < 6; i++)
        {
            bool offered = listed && e.Offers((SwordUpgrade)i);

            if (upgradeRows != null && i < upgradeRows.Length && upgradeRows[i] != null) upgradeRows[i].SetActive(offered);
            if (upgradeLabels != null && i < upgradeLabels.Length && upgradeLabels[i] != null)
            {
                upgradeLabels[i].text = offered ? Describe((SwordUpgrade)i) : "";
            }
        }
    }

    // Right-click the component's title bar to run these (in Play mode, they help when testing).
    [ContextMenu("Debug: add 1000 score")]
    private void DebugAddScore() { ShopScore.AddScore(1000); }

    [ContextMenu("Debug: reset all sword upgrades")]
    private void DebugResetUpgrades()
    {
        ResetAll();
        PushAll();
        Refresh();
    }
}
