using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to anything the player can pick up during the investigation.
/// On contact with the player it sends the item to the inventory (InventoryState)
/// so its slot unlocks, and removes the object from the scene.
///
/// Works with 2D physics (Rigidbody2D / Collider2D, the usual top-down setup) and with 3D physics.
/// The object needs a trigger collider. If it has none, a trigger Box Collider is added for you;
/// any solid collider it already has is left alone.
/// The player needs the tag set below ("Player" by default) and a Rigidbody2D (or a Rigidbody /
/// CharacterController in 3D) so Unity reports the touch.
///
/// To add a new object: give it a name below in pointsByName (or leave the
/// "Object Name" field blank / unmatched to use the fallback amount).
/// To make it appear in the inventory, the Object Name must match an Inventory Item Slot's Item Name.
/// </summary>
public class CollectibleItem : MonoBehaviour
{
    [Header("Object Identity")]
    [Tooltip("Shown in the pickup popup and used to look up its point value below. Also unlocks the inventory slot with the same Item Name.")]
    [SerializeField] private string objectName;

    [Header("Inventory")]
    [Tooltip("Shown in the inventory's hover box for this item.")]
    [SerializeField, TextArea] private string description;
    [Tooltip("Optional. Picture for the inventory slot if the slot has none of its own. Leave empty to use this object's sprite.")]
    [SerializeField] private Sprite inventoryIcon;

    [Header("Pickup Rules")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private bool destroyOnPickup = true;

    [Header("Debug")]
    [Tooltip("When something other than the player touches this object, print what it was and why it was ignored.")]
    [SerializeField] private bool logIgnoredTouches = true;

    // Points awarded when an object's name is blank or not found below.
    private const int FallbackPoints = 100;

    // --- Name -> Points lookup --------------------------------------------
    // Add a line here every time you create a new object type. The name just
    // needs to match whatever you type into "Object Name" in the Inspector.
    private static readonly Dictionary<string, int> pointsByName = new Dictionary<string, int>
    {
        { "Bloody Knife",    150 },
        { "Torn Letter",     100 },
        { "Muddy Footprint", 200 },
        { "Broken Watch",    120 },
        // { "New Object",   100 },
    };
    // ------------------------------------------------------------------------

    private bool collected = false;

    // Reset runs in the editor the moment the script is added; Awake covers objects that got the script
    // another way (copy/paste, spawned at runtime).
    // There is deliberately no [RequireComponent(typeof(Collider))] on this class: "Collider" is the abstract
    // 3D base type, so Unity can't choose a collider to add and refuses to attach the script ("Can't add
    // script"). It also says nothing about 2D colliders, which are a separate physics system.
    private void Reset() { EnsureTriggerCollider(); }
    private void Awake() { EnsureTriggerCollider(); }

    // Pickups are detected with trigger events, so the object needs a trigger collider in the RIGHT physics
    // system: 2D physics never sees 3D colliders, and 3D physics never sees 2D ones.
    private void EnsureTriggerCollider()
    {
        // Sprites / 2D colliders mean 2D; meshes / 3D colliders mean 3D. 2D wins ties and unknowns.
        bool looks2D = GetComponent<Collider2D>() != null || GetComponent<Rigidbody2D>() != null
                       || GetComponentInChildren<SpriteRenderer>() != null;
        bool looks3D = GetComponent<Collider>() != null || GetComponent<Rigidbody>() != null
                       || GetComponentInChildren<MeshRenderer>() != null;

        if (looks2D || !looks3D) Ensure2DTrigger();
        else Ensure3DTrigger();
    }

    private void Ensure2DTrigger()
    {
        foreach (Collider2D existing in GetComponents<Collider2D>())
        {
            if (existing.isTrigger) return;   // already has a trigger, nothing to add
        }

        // Added next to any solid collider the object already has: the solid one keeps blocking,
        // this one does the pickup. It usually sizes itself to the sprite; adjust Size if it looks off.
        BoxCollider2D zone = gameObject.AddComponent<BoxCollider2D>();
        zone.isTrigger = true;
    }

    private void Ensure3DTrigger()
    {
        foreach (Collider existing in GetComponents<Collider>())
        {
            if (existing.isTrigger) return;
        }

        BoxCollider zone = gameObject.AddComponent<BoxCollider>();
        zone.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        Touched(other, other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Touched(other, other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : null);
    }

    // Shared by 2D and 3D physics. "other" is whatever touched us; "bodyObject" is the object that holds its Rigidbody, if any.
    private void Touched(Component other, GameObject bodyObject)
    {
        if (collected) return;

        // The tag may sit on the collider's own object, on the object holding its Rigidbody, or on the player's root.
        bool isPlayer = other.CompareTag(playerTag)
                        || (bodyObject != null && bodyObject.CompareTag(playerTag))
                        || other.transform.root.CompareTag(playerTag);

        if (isPlayer)
        {
            Collect();
        }
        else if (logIgnoredTouches)
        {
            Debug.Log($"CollectibleItem '{name}': touched by '{other.name}' (tag: {other.tag}), which isn't tagged '{playerTag}', so it was ignored. " +
                      "If that was the player, set its Tag to match.", this);
        }
    }

    private void Collect()
    {
        collected = true;

        SendToInventory();

        if (destroyOnPickup) Destroy(gameObject);
        else gameObject.SetActive(false);
    }

    // Hands the item to InventoryState, which keeps it across scene changes and unlocks the matching
    // slot in the inventory (now if it's showing, or the next time the slot appears).
    private void SendToInventory()
    {
        if (string.IsNullOrEmpty(objectName))
        {
            Debug.LogWarning($"CollectibleItem '{name}': Object Name is blank, so no inventory slot can unlock for it. " +
                             "Give it a name that matches a slot's Item Name.", this);
            return;
        }

        Sprite icon = inventoryIcon;
        if (icon == null)
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) icon = sprite.sprite;
        }

        InventoryState.Unlock(new InventoryItemData
        {
            name = objectName,
            description = description,
            icon = icon
        });
    }

    private int GetPointValue()
    {
        if (!string.IsNullOrEmpty(objectName) && pointsByName.TryGetValue(objectName, out int mapped))
            return mapped;
        return FallbackPoints;
    }
}