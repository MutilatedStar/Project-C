using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Ilumisoft.HealthSystem;

/// <summary>
/// Two menus in one script:
///
///   1. CHOICE MENU - appears when the floor is cleared: [Go Down] or [Shop].
///   2. SHOP MENU   - a separate panel. Buy items, then [Back] returns to the choice menu.
///                    The player can go in and out of the shop as many times as they like.
///
/// While either menu is up the game is paused (Time.timeScale = 0 and FoeController.Frozen = true).
///
/// SETUP
///   - Put this script on an object that is ALWAYS active (e.g. your UI manager), NOT on one of the panels,
///     because the panels get switched off and this script has to keep listening for the floor being cleared.
///   - choicePanel and shopPanel are two child panels. Both start switched off (the script also hides them).
///   - itemButtonPrefab is a prefab: a Button with a legacy UI Text as a child. One is made per shop item.
///     Give itemContainer a Vertical Layout Group so they stack neatly.
///
/// COINS
///   The shop is paid for in coins. Coins are earned from kills: points x coinsPerScorePoint (0 = off).
///   You can also give coins from anywhere with FloorMenu.AddCoins(10).
/// </summary>
public class FloorMenu : MonoBehaviour
{
    // ------------------------------------------------------------ shop item data

    public enum ItemEffect
    {
        None,          // does nothing by itself; use the onPurchased event for custom effects
        Heal,          // restores "amount" health (refused if already at full health)
        MaxHealthUp    // raises max health by "amount" and heals that much
    }

    [System.Serializable]
    public class ShopItem
    {
        public string itemName = "Medkit";
        [TextArea] public string description = "Restores 25 health.";
        [Min(0)] public int cost = 50;
        [Tooltip("How many times this can be bought per run. 0 = unlimited.")]
        [Min(0)] public int maxPurchases = 0;
        public ItemEffect effect = ItemEffect.Heal;
        public float amount = 25f;
        [Tooltip("Optional: runs when bought. Use for effects not listed above (damage upgrade, ammo, etc.).")]
        public UnityEvent onPurchased;
    }

    // ------------------------------------------------------------ shared coin balance

    private static int coins;

    public static int Coins => coins;

    /// <summary>Fires whenever the balance changes, with the new total.</summary>
    public static event System.Action<int> OnCoinsChanged;

    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        coins += amount;
        OnCoinsChanged?.Invoke(coins);
    }

    public static bool TrySpend(int amount)
    {
        if (amount > coins) return false;
        coins -= amount;
        OnCoinsChanged?.Invoke(coins);
        return true;
    }

    // ------------------------------------------------------------ inspector

    [Header("Floor")]
    [Tooltip("Leave empty to find the FloorManager in the scene.")]
    [SerializeField] private FloorManager floorManager;
    [Tooltip("Seconds to wait after the last foe dies before the menu appears.")]
    [SerializeField] private float openDelay = 1f;

    [Header("Choice menu")]
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private Text titleText;
    [SerializeField] private Button goDownButton;
    [SerializeField] private Button shopButton;

    [Header("Shop menu")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Text coinsText;
    [Tooltip("Shows messages like 'Not enough coins'. Optional.")]
    [SerializeField] private Text messageText;
    [SerializeField] private Transform itemContainer;
    [SerializeField] private Button itemButtonPrefab;
    [SerializeField] private Button shopBackButton;

    [Header("Shop items")]
    [SerializeField] private ShopItem[] items;

    [Header("Coins")]
    [SerializeField] private int startingCoins = 0;
    [Tooltip("Coins earned per score point when a foe dies. 1 = coins equal the score. 0 = no coins from kills.")]
    [SerializeField] private float coinsPerScorePoint = 1f;

    // ------------------------------------------------------------ state

    private HealthComponent playerHealth;
    private readonly List<Button> itemButtons = new List<Button>();
    private readonly List<Text> itemLabels = new List<Text>();
    private int[] bought;               // how many of each item were bought this run
    private bool isOpen;
    private float previousTimeScale = 1f;

    public bool IsOpen => isOpen;

    // ------------------------------------------------------------ setup

    private void OnEnable()
    {
        FoeController.OnFoeKilled += HandleFoeKilled;
    }

    private void OnDisable()
    {
        FoeController.OnFoeKilled -= HandleFoeKilled;
    }

    private void Start()
    {
        coins = startingCoins;
        OnCoinsChanged?.Invoke(coins);

        if (floorManager == null) floorManager = FindFirstObjectByType<FloorManager>();
        if (floorManager != null) floorManager.OnFloorCleared += HandleFloorCleared;

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) playerHealth = player.GetComponentInParent<HealthComponent>();

        goDownButton.onClick.AddListener(GoDown);
        shopButton.onClick.AddListener(OpenShop);
        shopBackButton.onClick.AddListener(CloseShop);

        BuildShop();

        choicePanel.SetActive(false);
        shopPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (floorManager != null) floorManager.OnFloorCleared -= HandleFloorCleared;

        // Never leave the game frozen if this object is destroyed while a menu is up (e.g. scene reload).
        if (isOpen) SetPaused(false);
    }

    // ------------------------------------------------------------ opening / closing

    private void HandleFloorCleared(int floor)
    {
        Invoke(nameof(Open), openDelay);
    }

    /// <summary>Shows the choice menu and pauses the game.</summary>
    public void Open()
    {
        if (isOpen) return;
        isOpen = true;

        titleText.text = "Floor " + FloorManager.CurrentFloor + " cleared";
        choicePanel.SetActive(true);
        shopPanel.SetActive(false);
        SetPaused(true);
    }

    /// <summary>Hides both menus and unpauses.</summary>
    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        choicePanel.SetActive(false);
        shopPanel.SetActive(false);
        SetPaused(false);
    }

    private void SetPaused(bool paused)
    {
        if (paused)
        {
            previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = previousTimeScale;
        }
        FoeController.Frozen = paused;
    }

    // ------------------------------------------------------------ choice menu buttons

    private void GoDown()
    {
        Close();   // unpause first, then move down so the new floor starts running right away
        if (floorManager != null) floorManager.GoDownFloor();
    }

    private void OpenShop()
    {
        choicePanel.SetActive(false);
        shopPanel.SetActive(true);
        SetMessage("");
        RefreshShop();
    }

    private void CloseShop()
    {
        shopPanel.SetActive(false);
        choicePanel.SetActive(true);
    }

    // ------------------------------------------------------------ shop

    // Makes one button per item. Runs once.
    private void BuildShop()
    {
        if (items == null) items = new ShopItem[0];
        bought = new int[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            int index = i;   // copy for the click listener (the loop variable changes)
            Button button = Instantiate(itemButtonPrefab, itemContainer);
            button.onClick.AddListener(() => TryBuy(index));
            itemButtons.Add(button);
            itemLabels.Add(button.GetComponentInChildren<Text>());
        }
    }

    // Updates the coin total, item labels, and which buttons can be clicked.
    private void RefreshShop()
    {
        if (coinsText != null) coinsText.text = "Coins: " + coins;

        for (int i = 0; i < items.Length; i++)
        {
            ShopItem item = items[i];
            bool soldOut = item.maxPurchases > 0 && bought[i] >= item.maxPurchases;

            if (itemLabels[i] != null)
            {
                string label = item.itemName + "  -  " + (soldOut ? "SOLD OUT" : item.cost + " coins");
                if (!string.IsNullOrEmpty(item.description)) label += "\n" + item.description;
                itemLabels[i].text = label;
            }

            itemButtons[i].interactable = !soldOut && coins >= item.cost;   // greyed out if sold out or too expensive
        }
    }

    private void TryBuy(int index)
    {
        ShopItem item = items[index];

        if (item.maxPurchases > 0 && bought[index] >= item.maxPurchases) { SetMessage("Sold out."); return; }
        if (coins < item.cost) { SetMessage("Not enough coins."); return; }

        // Don't charge for a heal the player can't use.
        bool needsHealth = item.effect == ItemEffect.Heal || item.effect == ItemEffect.MaxHealthUp;
        if (needsHealth && playerHealth == null) { SetMessage("No player health found."); return; }
        if (item.effect == ItemEffect.Heal && playerHealth.CurrentHealth >= playerHealth.MaxHealth - 0.01f)
        {
            SetMessage("Already at full health.");
            return;
        }

        TrySpend(item.cost);
        ApplyEffect(item);
        bought[index]++;
        item.onPurchased?.Invoke();

        SetMessage("Bought " + item.itemName + ".");
        RefreshShop();
    }

    // ASSUMPTION: HealthComponent has AddHealth(float) and a settable MaxHealth (same assumptions as FoeController).
    // If your names differ, change them here.
    private void ApplyEffect(ShopItem item)
    {
        switch (item.effect)
        {
            case ItemEffect.Heal:
                playerHealth.AddHealth(item.amount);
                break;

            case ItemEffect.MaxHealthUp:
                playerHealth.MaxHealth += item.amount;
                playerHealth.AddHealth(item.amount);
                break;
        }
    }

    private void SetMessage(string text)
    {
        if (messageText != null) messageText.text = text;
    }

    // ------------------------------------------------------------ coins from kills

    private void HandleFoeKilled(int scorePoints)
    {
        AddCoins(Mathf.RoundToInt(scorePoints * coinsPerScorePoint));
    }
}
