using System.Collections.Generic;
using UnityEngine;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random (same reason as SwordController).

/// <summary>
/// Decides WHERE the weapon chests appear. Put it on an always-active object (an empty "GameManager" works).
///
/// Setup (pick ONE way to mark the spots):
///   A. Make an empty GameObject for every place a chest could appear and make them CHILDREN of this object.
///      Nothing else to drag: every child becomes a spot with Child Spot Chance.
///   B. Or drag the empties into the Spots list and give each its own Chance (0 to 1). If the Spots list has
///      anything in it, it is used and the children are ignored.
///   (Spots show as coloured circles in the Scene view: green = likely, red = unlikely.)
///
/// Then drag your chest prefab (the same one that works when you place it by hand) into Chest Prefab.
///
/// How it picks: every spot rolls its own chance. If fewer than Minimum Chests spots win, random spots that
/// missed their roll are added until the minimum is reached, so the player ALWAYS gets at least that many chests,
/// whatever the chances are. If more spots win, they all get one (set Maximum Chests to cap it).
///
/// Each chest heals the player 25% of their max health when opened, and also gives a sword the player doesn't own
/// yet (or +300 points once they own them all), so extra chests are never wasted. That logic lives in WeaponChest.
///
/// SpawnChests() can be called again at any time to clear the chests and shuffle them to new spots.
///
/// If nothing appears, open the Console in Play mode and look for the lines starting with "ChestSpawner":
///   - no line at all  -> this script isn't running (not in the scene, component unticked, or its object is inactive)
///   - an error/warning -> it says exactly what is missing (no prefab, no spots)
///   - "placed N chests" -> they exist; click the per-chest line to highlight it in the Hierarchy and see where it is
/// </summary>
public class ChestSpawner : MonoBehaviour
{
    /// <summary>One place a chest may appear.</summary>
    [System.Serializable]
    public class ChestSpot
    {
        [Tooltip("An empty GameObject placed where a chest may appear.")]
        public Transform point;
        [Tooltip("Chance (0 to 1) that this spot gets a chest. 1 = always. 0 = only used if needed to reach the minimum.")]
        [Range(0f, 1f)] public float chance = 0.5f;
    }

    [Header("Chest")]
    [Tooltip("The chest to copy (it needs the WeaponChest script). Either the prefab from the Project window, or a chest that already " +
             "works in the Hierarchy: every spawned chest is an exact copy of whatever is dropped here.")]
    [SerializeField] private WeaponChest chestPrefab;
    [Tooltip("Place chests at the prefab's own Z instead of the spot's Z. In a 2D game a spot sitting at the wrong depth can put the chest " +
             "outside the camera's view: it spawns, but you never see it. Leave this ticked unless you layer things by Z on purpose.")]
    [SerializeField] private bool usePrefabDepth = true;

    [Header("Locations")]
    [Tooltip("Optional. If this list is empty, every child of this GameObject is used as a spot instead.")]
    [SerializeField] private List<ChestSpot> spots = new List<ChestSpot>();
    [Tooltip("Chance given to each child spot when the Spots list is empty.")]
    [SerializeField, Range(0f, 1f)] private float childSpotChance = 0.5f;

    [Header("How many")]
    [Tooltip("Always spawn at least this many chests, however the chances roll. 6 = one per sword in your list.")]
    [SerializeField, Min(0)] private int minimumChests = 6;
    [Tooltip("Never spawn more than this many. 0 = no limit.")]
    [SerializeField, Min(0)] private int maximumChests = 0;

    [Header("When")]
    [Tooltip("Place the chests as soon as the scene starts. Untick it to call SpawnChests() yourself.")]
    [SerializeField] private bool spawnOnStart = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    private readonly List<WeaponChest> spawned = new List<WeaponChest>();

    private void Start()
    {
        if (spawnOnStart) SpawnChests();
    }

    /// <summary>Removes any chests this spawner made and places a fresh random set.</summary>
    [ContextMenu("Spawn chests now")]
    public void SpawnChests()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("ChestSpawner '" + name + "': chests can only be placed in Play mode.", this);
            return;
        }

        ClearChests();

        if (chestPrefab == null)
        {
            Debug.LogError("ChestSpawner '" + name + "': no Chest Prefab assigned.", this);
            return;
        }

        // Safety: if the chest carries a ChestSpawner (for example both scripts sit on the same object), every chest made
        // would contain a spawner that makes more chests, each of those making more, and Unity locks up within seconds.
        if (chestPrefab.GetComponentInChildren<ChestSpawner>(true) != null)
        {
            Debug.LogError("ChestSpawner '" + name + "': the Chest Prefab has a ChestSpawner on it, which would spawn chests forever. " +
                           "Keep this script on a separate empty object (like a GameManager) and point Chest Prefab at a chest that only has WeaponChest on it.", this);
            return;
        }

        List<ChestSpot> pool = BuildPool();
        if (pool.Count == 0)
        {
            Debug.LogError("ChestSpawner '" + name + "': there are no spots to place chests on. Make empty GameObjects children of this object, " +
                           "or drag empties into the Spots list.", this);
            return;
        }

        // Shuffle the spots so every later step (promoting, trimming) picks at random.
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            ChestSpot swap = pool[i];
            pool[i] = pool[j];
            pool[j] = swap;
        }

        // Every spot rolls its own chance.
        List<ChestSpot> chosen = new List<ChestSpot>();
        List<ChestSpot> missed = new List<ChestSpot>();
        foreach (ChestSpot spot in pool)
        {
            if (spot.chance >= 1f || Random.value < spot.chance) chosen.Add(spot);
            else missed.Add(spot);
        }

        // Not enough winners: promote random spots that missed (the pool is already shuffled) until the minimum is met.
        if (minimumChests > pool.Count)
        {
            Debug.LogWarning("ChestSpawner '" + name + "': Minimum Chests is " + minimumChests + " but only " + pool.Count +
                             " spots are available. Add more empties.", this);
        }
        int minimum = Mathf.Min(minimumChests, pool.Count);
        while (chosen.Count < minimum && missed.Count > 0)
        {
            chosen.Add(missed[0]);
            missed.RemoveAt(0);
        }

        // Too many winners: drop the extras (the order is random, so which ones go is random too).
        if (maximumChests > 0)
        {
            int cap = Mathf.Max(maximumChests, minimum);
            if (chosen.Count > cap) chosen.RemoveRange(cap, chosen.Count - cap);
        }

        bool warnedNothingToDraw = false;
        foreach (ChestSpot spot in chosen)
        {
            Vector3 position = spot.point.position;
            if (usePrefabDepth) position.z = chestPrefab.transform.position.z;

            // Only the spin around Z is kept, so a tilted empty can never turn the sprite edge-on and make it vanish.
            Quaternion rotation = Quaternion.Euler(0f, 0f, spot.point.eulerAngles.z);

            WeaponChest chest = Instantiate(chestPrefab, position, rotation);
            chest.name = chestPrefab.name;
            chest.gameObject.SetActive(true);   // in case the prefab slot holds a scene object that was switched off as a template
            spawned.Add(chest);

            // A chest with nothing to draw is the usual reason spawned chests are invisible, so say so once, loudly.
            if (!warnedNothingToDraw && !HasSomethingToDraw(chest))
            {
                warnedNothingToDraw = true;
                Debug.LogWarning("ChestSpawner '" + name + "': the spawned chests have nothing visible to draw (see the 'chest at' line for why). " +
                                 "The Chest Prefab is missing a sprite or has its renderer switched off. If a chest you place by hand looks right, " +
                                 "drag THAT chest from the Hierarchy into Chest Prefab instead.", chestPrefab);
            }

            // Click this line in the Console to highlight the chest in the Hierarchy.
            if (debugLog) Debug.Log("ChestSpawner '" + name + "': chest at " + chest.transform.position + " | " + Describe(chest), chest);
        }

        if (debugLog) Debug.Log("ChestSpawner '" + name + "': placed " + spawned.Count + " chests on " + pool.Count + " possible spots.", this);
    }

    /// <summary>Destroys every chest this spawner made.</summary>
    public void ClearChests()
    {
        foreach (WeaponChest chest in spawned)
        {
            if (chest != null) Destroy(chest.gameObject);
        }
        spawned.Clear();
    }

    // True when the chest has a SpriteRenderer that is on, has a sprite, and isn't fully transparent.
    private static bool HasSomethingToDraw(WeaponChest chest)
    {
        SpriteRenderer sr = chest.GetComponentInChildren<SpriteRenderer>(true);
        return sr != null && sr.enabled && sr.sprite != null && sr.color.a > 0.01f;
    }

    // One line describing how a spawned chest looks and whether it can be touched, for the Console.
    private static string Describe(WeaponChest chest)
    {
        SpriteRenderer sr = chest.GetComponentInChildren<SpriteRenderer>(true);
        string look;
        if (sr == null)
        {
            look = "NO SpriteRenderer";
        }
        else
        {
            string sprite = sr.sprite != null ? sr.sprite.name : "NONE";
            Vector3 size = sr.sprite != null ? sr.bounds.size : Vector3.zero;
            look = "sprite " + sprite + ", renderer " + (sr.enabled ? "on" : "OFF") +
                   ", layer '" + sr.sortingLayerName + "' order " + sr.sortingOrder +
                   ", alpha " + sr.color.a.ToString("0.##") +
                   ", size " + size.x.ToString("0.##") + "x" + size.y.ToString("0.##") + " units";
        }

        int triggers = 0;
        foreach (Collider2D col in chest.GetComponents<Collider2D>())
        {
            if (col.isTrigger) triggers++;
        }

        return look + ", scale " + chest.transform.lossyScale.x.ToString("0.##") + ", trigger colliders " + triggers;
    }

    // The usable spots: the Spots list if it has anything valid in it, otherwise this object's children.
    private List<ChestSpot> BuildPool()
    {
        List<ChestSpot> pool = new List<ChestSpot>();

        foreach (ChestSpot spot in spots)
        {
            if (spot != null && spot.point != null) pool.Add(spot);
        }

        if (pool.Count == 0)
        {
            foreach (Transform child in transform)
            {
                ChestSpot spot = new ChestSpot();
                spot.point = child;
                spot.chance = childSpotChance;
                pool.Add(spot);
            }

            if (pool.Count > 0 && debugLog)
                Debug.Log("ChestSpawner '" + name + "': the Spots list is empty, so its " + pool.Count + " child objects are used as spots.", this);
        }

        return pool;
    }

    // Shows each spot in the Scene view, tinted by its chance (red = 0, green = 1).
    private void OnDrawGizmos()
    {
        bool drewAny = false;

        if (spots != null)
        {
            foreach (ChestSpot spot in spots)
            {
                if (spot == null || spot.point == null) continue;
                DrawSpot(spot.point.position, spot.chance);
                drewAny = true;
            }
        }

        if (!drewAny)
        {
            foreach (Transform child in transform) DrawSpot(child.position, childSpotChance);
        }
    }

    private static void DrawSpot(Vector3 position, float chance)
    {
        Gizmos.color = Color.Lerp(Color.red, Color.green, chance);
        Gizmos.DrawWireSphere(position, 0.4f);
    }
}
