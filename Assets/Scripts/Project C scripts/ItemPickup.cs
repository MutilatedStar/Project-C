using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// One item lying in the world. Put it on an object with a sprite and a Collider2D (it switches that collider to
/// Is Trigger by itself). The first player to walk into it gets it: with two players the item goes to WHOEVER touched
/// it, unless you tick Affects Everyone.
///
///   Heal      gives health. A flat number, or a share of the player's max health. A player already at full health
///             doesn't use it up (Keep If Not Needed), so the other player can still come for it.
///   Power-Up  starts a timed effect on the player (PlayerEffects): speed-up, invincibility, stronger attacks or faster
///             attacks, for as many seconds as you set. Picking up the same kind again refreshes it.
///   Weapon    unlocks a sword for the player, using the SwordController sword list: type the sword's name exactly as
///             it is in that list. Each player has their own sword collection, so only the one who touched it gets the
///             sword (everybody does with Shared Weapons ON). A player who already owns it leaves it on the ground
///             for the other one. Equip On Pickup puts it straight in their hand.
///
/// Needs CoopHealth, CoopRules and PlayerEffects (added automatically) in the project.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class ItemPickup : MonoBehaviour
{
    public enum ItemKind { Heal, PowerUp, Weapon }

    [Header("What it is")]
    public ItemKind kind = ItemKind.Heal;
    [Tooltip("ON: every player in the game gets it, whoever touches it. OFF: only the player who touched it.")]
    public bool affectsEveryone = false;

    [Header("Heal")]
    [Tooltip("Health given, in points.")]
    [Min(0f)] public float healAmount = 25f;
    [Tooltip("Extra health given as a share of the player's MAX health (0.25 = a quarter of the bar). Added to Heal Amount. 0 = off.")]
    [Range(0f, 1f)] public float healPercentOfMax = 0f;
    [Tooltip("ON: a player who is already at full health can't pick this up, so it stays for the other player. OFF: it is used up whether it was needed or not.")]
    public bool keepIfNotNeeded = true;

    [Header("Power-Up")]
    public PlayerEffects.EffectKind effect = PlayerEffects.EffectKind.Speed;
    [Tooltip("Speed: speed multiplier (1.5 = 50% faster).   Strength: extra damage (0.5 = +50%).   Attack Speed: faster swings (0.3 = 30% faster).   Invincible: not used.")]
    [Min(0f)] public float amount = 1.5f;
    [Tooltip("How many seconds it lasts.")]
    [Min(0.1f)] public float duration = 8f;

    [Header("Weapon")]
    [Tooltip("The sword's name, exactly as in the SwordController's list (capitals don't matter).")]
    public string swordName = "Broadsword";
    [Tooltip("ON: the player holds the new sword straight away. OFF: it just joins their collection.")]
    public bool equipOnPickup = false;
    [Tooltip("ON: a player who already owns the sword can't pick this up, so it stays for the other player.")]
    public bool keepIfAlreadyOwned = true;

    [Header("Pickup")]
    [Tooltip("ON: the item disappears when it is used. OFF: it stays and can be used again (a refill station).")]
    public bool destroyOnPickup = true;
    [Tooltip("Optional. Played where the item was when it is picked up (silent while the Sound setting is off).")]
    public AudioClip pickupSound;
    [Range(0f, 1f)] public float pickupVolume = 1f;

    private bool taken;

    private void Reset()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;   // a solid collider would block the player instead of being picked up
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryPickup(other);
    }

    // Also checked while a player stands on it, so an item they couldn't use yet (full health, sword already owned) is
    // picked up the moment they can, without stepping out and in again. Only for items that are used up: one that
    // stays (a refill station) would otherwise give its effect again on every physics step.
    private void OnTriggerStay2D(Collider2D other)
    {
        if (!destroyOnPickup) return;
        TryPickup(other);
    }

    private void TryPickup(Collider2D other)
    {
        if (taken) return;

        CoopHealth health = other.GetComponentInParent<CoopHealth>();
        if (health == null || !health.IsAlive) return;
        if (!CoopRules.Counts(health.PlayerNumber)) return;

        bool used = UseOn(health);
        if (!used) return;

        if (pickupSound != null && GameSettings.SoundOn) AudioSource.PlayClipAtPoint(pickupSound, transform.position, pickupVolume);

        if (destroyOnPickup)
        {
            taken = true;
            Destroy(gameObject);
        }
    }

    // ------------------------------------------------------------ the effects

    // Gives the item to the player who touched it (or to everybody). True if anybody actually got something.
    private bool UseOn(CoopHealth toucher)
    {
        bool anyUsed = false;

        if (affectsEveryone)
        {
            // A copy, because using an item can change the list (nothing here does, but it costs nothing to be safe).
            foreach (CoopHealth player in CoopHealth.Players.ToArray())
            {
                if (player == null || !player.IsAlive || !CoopRules.Counts(player.PlayerNumber)) continue;
                if (UseOnOne(player)) anyUsed = true;
            }
        }
        else
        {
            anyUsed = UseOnOne(toucher);
        }
        return anyUsed;
    }

    private bool UseOnOne(CoopHealth player)
    {
        switch (kind)
        {
            case ItemKind.Heal: return Heal(player);
            case ItemKind.PowerUp: return PowerUp(player);
            default: return Weapon(player);
        }
    }

    private bool Heal(CoopHealth player)
    {
        if (keepIfNotNeeded && player.CurrentHealth >= player.MaxHealth) return false;

        float points = healAmount + healPercentOfMax * player.MaxHealth;
        if (points <= 0f) return false;

        player.AddHealth(points);
        return true;
    }

    private bool PowerUp(CoopHealth player)
    {
        PlayerEffects effects = player.GetComponent<PlayerEffects>();
        if (effects == null) effects = player.gameObject.AddComponent<PlayerEffects>();   // no setup needed

        return effects.Apply(effect, amount, duration);
    }

    private bool Weapon(CoopHealth player)
    {
        SwordController sword = player.GetComponent<SwordController>();
        if (sword == null)
        {
            Debug.LogWarning("ItemPickup '" + name + "': player " + player.PlayerNumber + " has no SwordController, so the weapon was not given.", this);
            return false;
        }

        int index = sword.IndexOf(swordName);
        if (index < 0)
        {
            Debug.LogWarning("ItemPickup '" + name + "': there is no sword called '" + swordName + "' in the SwordController's list.", this);
            return false;
        }

        if (sword.IsUnlocked(index))
        {
            if (keepIfAlreadyOwned) return false;
        }
        else
        {
            sword.UnlockSword(index);
        }

        if (equipOnPickup) sword.EquipSword(index);
        return true;
    }
}
