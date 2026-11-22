using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random (same reason as SwordController).

/// <summary>
/// A treasure chest. Put it on a chest prefab that ChestSpawner places around the level.
///
/// When the player gets close, the chest switches from its closed sprite to its open sprite and STAYS in the scene.
/// At that moment it:
///   1. Heals the player by Heal Percent of their MAX health (25% by default). Every chest does this, always.
///   2. Hands the player ONE sword they don't own yet, picked at random, so the same sword can never come out twice.
///      Once the player owns every sword, each chest pays Score When All Owned points instead (300).
///
/// Built from CollectibleItem: same trigger setup and the same player-tag check, but 2D physics only (this project
/// is top-down 2D). The player needs the tag below ("Player" by default) and a Rigidbody2D so Unity reports the touch.
///
/// Prefab setup:
///   1. A GameObject with a SpriteRenderer showing the closed chest, plus this script.
///   2. Drag the closed and open sprites into the slots below. (If the chest has no SpriteRenderer, or one can't be
///      added because the object already carries another kind of renderer, one is made on a child object for you
///      and draws Closed Sprite / Open Sprite, Created Sprite Scale times bigger (5x by default). Raise Sorting Order
///      if it hides behind the floor or walls.)
///   3. Leave the collider alone: a round trigger of Open Radius is added for you. (If the prefab already has a
///      trigger collider, that one is used instead and Open Radius is ignored.)
///
/// The sword list lives on the player's SwordController. For the chests to have something to give, untick
/// "Unlocked At Start" on every sword you want to be found in a chest. (The starting sword is always owned.)
///
/// The heal uses the Health System's HealthComponent on the player (the same one the Player Healthbar reads).
/// </summary>
[DisallowMultipleComponent]
public class WeaponChest : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("The chest's SpriteRenderer. Found on this object or a child if left empty.")]
    [SerializeField] private SpriteRenderer chestRenderer;
    [Tooltip("Shown until the player gets close. Optional: if empty, the sprite the renderer already has is used.")]
    [SerializeField] private Sprite closedSprite;
    [Tooltip("Shown once the chest is open. The chest stays in the scene.")]
    [SerializeField] private Sprite openSprite;
    [Tooltip("Only used when the chest has no SpriteRenderer and one is created for it. Sorting layer to draw on (match your player/foes). Empty = Default.")]
    [SerializeField] private string sortingLayer = "";
    [Tooltip("Only used when the chest has no SpriteRenderer and one is created for it. Raise this if the chest hides behind the floor or walls.")]
    [SerializeField] private int sortingOrder = 5;
    [Tooltip("Only used when the chest has no SpriteRenderer and one is created for it. Size multiplier for that sprite: 5 = five times bigger. " +
             "Only the picture grows; how close the player must get is still Open Radius.")]
    [SerializeField, Min(0.01f)] private float createdSpriteScale = 5f;

    [Header("Opening")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("How close the player must get, in Unity units. Only used when the prefab has no trigger collider of its own: then a round one this size is added.")]
    [SerializeField, Min(0.1f)] private float openRadius = 1.2f;

    [Header("Reward")]
    [Tooltip("Share of the player's MAX health that every opened chest restores. 0.25 = 25%. Set 0 to turn the heal off.")]
    [SerializeField, Range(0f, 1f)] private float healPercent = 0.25f;
    [Tooltip("Points given by a chest opened when the player already owns every sword.")]
    [SerializeField, Min(0)] private int scoreWhenAllOwned = 300;

    [Header("Debug")]
    [Tooltip("Prints one Console line for every chest opened: what it gave.")]
    [SerializeField] private bool debugLog = true;
    [Tooltip("When something other than the player touches the chest, print what it was. Noisy when foes walk past.")]
    [SerializeField] private bool logIgnoredTouches = false;

    /// <summary>True once the player has opened this chest.</summary>
    public bool IsOpen => opened;

    private bool opened;
    private Transform createdVisual;   // the child object holding the sprite this script made, if it had to make one

    // Reset runs in the editor the moment the script is added; Awake covers chests that are spawned at runtime.
    private void Reset()
    {
        FindRenderer();
        EnsureTrigger();
    }

    private void Awake()
    {
        FindRenderer();
        EnsureTrigger();

        // No SpriteRenderer anywhere on the chest: make one on a child object so the chest can still be seen.
        if (chestRenderer == null) chestRenderer = CreateRenderer();

        if (chestRenderer != null && closedSprite != null) chestRenderer.sprite = closedSprite;

        if (openSprite == null)
            Debug.LogWarning("WeaponChest '" + name + "': no Open Sprite assigned, so the chest will look the same after opening.", this);
    }

    private void FindRenderer()
    {
        if (chestRenderer == null) chestRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    // Used when the chest has no SpriteRenderer. It goes on a CHILD object, so it works even when this object can't take
    // a SpriteRenderer itself (for example it already has a different kind of renderer).
    private SpriteRenderer CreateRenderer()
    {
        if (closedSprite == null)
        {
            Debug.LogWarning("WeaponChest '" + name + "': this chest has no SpriteRenderer and no Closed Sprite to draw, so it will be invisible. " +
                             "Drag the closed-chest sprite into this component's Closed Sprite slot (and the open one into Open Sprite).", this);
            return null;
        }

        GameObject visual = new GameObject("ChestSprite");
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = Vector3.one * createdSpriteScale;
        createdVisual = visual.transform;

        SpriteRenderer made = visual.AddComponent<SpriteRenderer>();
        made.sortingOrder = sortingOrder;
        if (!string.IsNullOrEmpty(sortingLayer)) made.sortingLayerName = sortingLayer;
        return made;
    }

    /// <summary>
    /// Resizes the sprite this script created for the chest (1 = the sprite's original size, 5 = five times bigger).
    /// Does nothing if the chest uses its own SpriteRenderer: size that one with the Transform's Scale instead.
    /// Only the picture changes; the open distance stays Open Radius.
    /// </summary>
    public void SetSpriteScale(float scale)
    {
        createdSpriteScale = Mathf.Max(0.01f, scale);
        if (createdVisual != null) createdVisual.localScale = Vector3.one * createdSpriteScale;
    }

    // The pickup is detected with a trigger event, so the chest needs a trigger collider (2D physics).
    private void EnsureTrigger()
    {
        foreach (Collider2D existing in GetComponents<Collider2D>())
        {
            if (existing.isTrigger) return;   // already has one, nothing to add
        }

        CircleCollider2D zone = gameObject.AddComponent<CircleCollider2D>();
        zone.isTrigger = true;

        // Collider sizes are multiplied by the object's scale, so divide it out to get a true radius in Unity units.
        Vector3 scale = transform.lossyScale;
        float biggest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), 0.0001f);
        zone.radius = openRadius / biggest;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (opened) return;

        // The tag may sit on the collider's own object, on the object holding its Rigidbody, or on the player's root.
        GameObject body = other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : null;
        bool isPlayer = other.CompareTag(playerTag)
                        || (body != null && body.CompareTag(playerTag))
                        || other.transform.root.CompareTag(playerTag);

        if (isPlayer)
        {
            TryOpen(other);
        }
        else if (logIgnoredTouches)
        {
            Debug.Log("WeaponChest '" + name + "': touched by '" + other.name + "' (tag: " + other.tag + "), which isn't tagged '" +
                      playerTag + "', so it was ignored.", this);
        }
    }

    private void TryOpen(Component toucher)
    {
        SwordController swords = FindSwords(toucher);
        if (swords == null)
        {
            // Stay closed rather than waste the chest.
            Debug.LogWarning("WeaponChest '" + name + "': the player has no SwordController, so there is nothing to give. The chest stays closed.", this);
            return;
        }

        opened = true;
        if (chestRenderer != null && openSprite != null) chestRenderer.sprite = openSprite;

        // Every chest heals, whatever else it hands out (this happens before the all-swords-owned early return below).
        string healNote = HealPlayer(toucher);

        // Every sword the player doesn't own yet. Picking only from these is what prevents repeats.
        List<int> missing = new List<int>();
        for (int i = 0; i < swords.SwordCount; i++)
        {
            if (!swords.IsUnlocked(i)) missing.Add(i);
        }

        if (missing.Count == 0)
        {
            ScoreController.AddScore(scoreWhenAllOwned);
            if (debugLog) Debug.Log("WeaponChest '" + name + "': player owns every sword, gave +" + scoreWhenAllOwned + " points." + healNote, this);
            return;
        }

        int pick = missing[Random.Range(0, missing.Count)];

        // UnlockSword reports the sword through its SwordUnlocked event; listen just long enough to read its name.
        string givenName = "sword #" + (pick + 1);
        System.Action<SwordController.SwordStats> readName = stats => givenName = stats.swordName;
        swords.SwordUnlocked += readName;
        swords.UnlockSword(pick);
        swords.SwordUnlocked -= readName;

        if (debugLog) Debug.Log("WeaponChest '" + name + "': gave '" + givenName + "' (" + (missing.Count - 1) + " still to find)." + healNote, this);
    }

    /// <summary>Restores Heal Percent of the player's max health. Returns a short note for the debug line.</summary>
    private string HealPlayer(Component toucher)
    {
        if (healPercent <= 0f) return "";

        HealthComponent health = FindHealth(toucher);
        if (health == null)
        {
            // The chest still opens; only the heal is skipped.
            Debug.LogWarning("WeaponChest '" + name + "': no HealthComponent found on the player, so no heal was given.", this);
            return " No heal (player has no HealthComponent).";
        }

        float before = health.CurrentHealth;
        health.AddHealth(health.MaxHealth * healPercent);   // clamps to max health by itself
        float gained = health.CurrentHealth - before;

        return " Healed " + gained.ToString("0.#") + " HP.";
    }

    // Looks only at the object that touched the chest and the Player-tagged object. It does NOT search the whole scene,
    // because foes and dummies carry a HealthComponent too and the wrong one could be healed.
    private HealthComponent FindHealth(Component toucher)
    {
        HealthComponent found = toucher.GetComponentInParent<HealthComponent>();
        if (found == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                found = player.GetComponentInParent<HealthComponent>();
                if (found == null) found = player.GetComponentInChildren<HealthComponent>();
            }
        }
        return found;
    }

    private SwordController FindSwords(Component toucher)
    {
        SwordController found = toucher.GetComponentInParent<SwordController>();
        if (found == null) found = toucher.transform.root.GetComponentInChildren<SwordController>();
        if (found == null)
        {
#if UNITY_2022_2_OR_NEWER
            found = FindFirstObjectByType<SwordController>();
#else
            found = FindObjectOfType<SwordController>();
#endif
        }
        return found;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, openRadius);
    }
}
