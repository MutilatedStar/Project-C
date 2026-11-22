using System.Reflection;
using UnityEngine;
using Ilumisoft.HealthSystem;

// Small pieces both shop scripts (SwordShop.cs and PlayerUpgrades.cs) and SwordController.cs share.

/// <summary>
/// Lets both shops use ScoreController's score as their currency.
/// Score is not stored separately, so kills, pickups, and purchases all use the same total.
/// </summary>
public static class ShopScore
{
    public static int Score => ScoreController.Score;

    /// <summary>Fires whenever the score changes, so the shop labels can refresh.</summary>
    public static event System.Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConnectToScoreController()
    {
        ScoreController.OnScoreChanged -= HandleScoreChanged;
        ScoreController.OnScoreChanged += HandleScoreChanged;
    }

    private static void HandleScoreChanged(int score)
    {
        Changed?.Invoke();
    }

    public static void AddScore(int amount)
    {
        if (amount == 0) return;
        // Preserve the old shop helper's behaviour: its balance could never go below zero.
        int adjustedAmount = Mathf.Max(-ScoreController.Score, amount);
        ScoreController.AddScore(adjustedAmount);
    }

    public static bool TrySpend(int amount)
    {
        return ScoreController.TrySpend(amount);
    }

    public static void Reset(int startingScore = 0)
    {
        ScoreController.ResetScore();
        if (startingScore > 0) ScoreController.AddScore(startingScore);
    }
}

/// <summary>
/// Compatibility bridge for any older scripts that still call ShopWallet.
/// These calls now modify the player's score; no separate coin balance exists.
/// New code should use ShopScore.
/// </summary>
public static class ShopWallet
{
    public static int Coins => ShopScore.Score;

    public static event System.Action Changed
    {
        add { ShopScore.Changed += value; }
        remove { ShopScore.Changed -= value; }
    }

    public static void Add(int amount) { ShopScore.AddScore(amount); }
    public static bool TrySpend(int amount) { return ShopScore.TrySpend(amount); }
    public static void Reset(int startingCoins = 0) { ShopScore.Reset(startingCoins); }
}

/// <summary>
/// One row of upgrades (for example "damage"). Used by both shop scripts.
/// Cost of the NEXT level = Base Cost x Cost Growth ^ (current level), so level 0 costs exactly Base Cost.
/// </summary>
[System.Serializable]
public class UpgradeTrack
{
    [Tooltip("How many times this can be bought.")]
    [Min(1)] public int maxLevel = 5;
    [Tooltip("Score cost for the first level.")]
    [Min(0)] public int baseCost = 50;
    [Tooltip("Each level costs this much more than the one before. 1.5 = 50% more each time.")]
    [Min(1f)] public float costGrowth = 1.5f;
    [Tooltip("What ONE level gives. Percent upgrades use fractions: 0.1 = +10% per level. Regeneration is HP per second, Max HP is HP.")]
    public float perLevel = 0.1f;

    public int CostOfNext(int currentLevel, float multiplier = 1f)
    {
        return Mathf.Max(0, Mathf.RoundToInt(baseCost * multiplier * Mathf.Pow(costGrowth, currentLevel)));
    }
}

/// <summary>
/// The player's HealthComponent, reached through one small bridge.
/// The Ilumisoft HealthComponent source wasn't available when this was written, so Heal and SetMaxHealth look up
/// "AddHealth(float)" and the "MaxHealth" setter by name the first time they are used. If a name is different in your
/// copy, a ONE-TIME warning in the Console says which one, and only the two small methods below need changing.
/// </summary>
public static class PlayerHealthAccess
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool searched;
    private static MethodInfo addHealth;
    private static PropertyInfo maxProperty;
    private static FieldInfo maxField;
    private static bool warnedHeal;
    private static bool warnedMax;

    private static void Search()
    {
        if (searched) return;
        searched = true;

        System.Type type = typeof(HealthComponent);
        addHealth = type.GetMethod("AddHealth", Any, null, new[] { typeof(float) }, null);

        PropertyInfo property = type.GetProperty("MaxHealth", Any);
        if (property != null && property.GetSetMethod(true) != null)
        {
            maxProperty = property;
        }
        else
        {
            maxField = type.GetField("maxHealth", Any) ?? type.GetField("<MaxHealth>k__BackingField", Any);
        }
    }

    /// <summary>Adds health (the HealthComponent keeps it under the maximum itself).</summary>
    public static void Heal(HealthComponent health, float amount)
    {
        if (health == null || amount <= 0f) return;
        Search();

        if (addHealth == null)
        {
            if (!warnedHeal)
            {
                warnedHeal = true;
                Debug.LogWarning("PlayerHealthAccess: no AddHealth(float) found on HealthComponent, so regeneration, life steal " +
                                 "and armor refunds do nothing. Change Heal() in ShopShared.cs to your heal method's name.");
            }
            return;
        }
        addHealth.Invoke(health, new object[] { amount });
    }

    /// <summary>Sets the maximum health.</summary>
    public static void SetMaxHealth(HealthComponent health, float value)
    {
        if (health == null) return;
        Search();

        if (maxProperty != null) maxProperty.SetValue(health, value);
        else if (maxField != null) maxField.SetValue(health, value);
        else if (!warnedMax)
        {
            warnedMax = true;
            Debug.LogWarning("PlayerHealthAccess: could not find a settable MaxHealth on HealthComponent, so the Max HP upgrade " +
                             "does nothing. Change SetMaxHealth() in ShopShared.cs to match it.");
        }
    }
}
