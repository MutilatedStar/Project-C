using System;
using System.Collections.Generic;
using UnityEngine;

// What the pickup sends and what the inventory shows for one item.
[Serializable]
public class InventoryItemData
{
    public string name;         // matches the pickup's "Object Name" and the slot's "Item Name"
    public string description;  // shown in the hover box
    public Sprite icon;         // optional, used if the slot has no picture of its own
}

// Put this on a suspect (add ", IItemReceiver" to its class line and write ReceiveItem) to be told
// when the player drops an inventory item on it.
public interface IItemReceiver
{
    void ReceiveItem(InventoryItemData item);
}

// The shared go-between. CollectibleItem sends items in here; inventory slots read them out.
// It is static, so it survives scene changes and works even when the inventory UI isn't loaded
// at the moment of pickup. (It lives in memory only: it resets when the game is closed.)
public static class InventoryState
{
    private static readonly Dictionary<string, InventoryItemData> unlocked =
        new Dictionary<string, InventoryItemData>(StringComparer.OrdinalIgnoreCase);

    // Raised the first time an item is unlocked.
    public static event Action<InventoryItemData> ItemUnlocked;

    // Raised when the player drops an item on a suspect (or anything that can receive items).
    public static event Action<InventoryItemData, GameObject> ItemGiven;

    public static bool IsUnlocked(string itemName)
    {
        return !string.IsNullOrEmpty(itemName) && unlocked.ContainsKey(itemName);
    }

    public static bool TryGet(string itemName, out InventoryItemData item)
    {
        item = null;
        return !string.IsNullOrEmpty(itemName) && unlocked.TryGetValue(itemName, out item);
    }

    public static IEnumerable<InventoryItemData> All => unlocked.Values;

    public static void Unlock(InventoryItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.name)) return;

        bool isNew = !unlocked.ContainsKey(item.name);
        unlocked[item.name] = item;
        if (isNew) ItemUnlocked?.Invoke(item);
    }

    public static void Give(InventoryItemData item, GameObject target)
    {
        ItemGiven?.Invoke(item, target);
    }

    // Call this when starting a new game.
    public static void Clear()
    {
        unlocked.Clear();
    }

    // Static data would otherwise survive between Play sessions when domain reload is turned off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        unlocked.Clear();
        ItemUnlocked = null;
        ItemGiven = null;
    }
}
