using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An altar with SEVERAL trigger plates. It is fully triggered when ALL its plates are held down at the same time, by
/// balls, players or any mix (for example a player on one plate and a ball on another). Then the room (PillarRoom) that
/// owns this altar is told, and removes ITS pillars.
///
/// SET UP:
///   1. Put this script on the altar object. Give each plate a Collider 2D with Is Trigger ticked, on the altar object or
///      under it, sized to the spot to stand on or push a ball onto.
///   2. Plates: leave the list EMPTY and every Collider 2D on or under the altar becomes a plate. Or list them yourself, to
///      choose which pictures each plate colours.
///   3. List this altar in the Altars list of its PillarRoom. (A one-collider altar is simply an altar with one plate.)
///
/// WHAT COUNTS AS "ON A PLATE": anything whose collider overlaps the plate, on a layer in Occupant Mask: a player, a ball, a
/// foe, anything. With Only If Sprite Above ticked, a thing only counts if it is drawn at or above the plate (higher
/// Sorting Layer, or the same with an equal or higher Order in Layer), so decorations and shadows underneath don't press it.
/// One thing with several colliders counts once. Pillars and altars never count. With One Thing One Plate ticked, a thing
/// touching two plates only holds the nearer one, so one player can't hold two plates by straddling them.
///
/// HOLDING IT, plate by plate:
///   - A held plate slowly fades from its own colour to Held Colour over Hold Seconds, and the Hold Sound loops while any
///     plate is held. A plate that is let go slowly fades back over Release Seconds.
///   - The moment EVERY plate is fully faded in at once, the altar is triggered: all its plates change to Triggered
///     Colour (gray) at once, the Triggered Sound plays once, and it stays triggered.
/// </summary>
[DisallowMultipleComponent]
public class RoomAltar : MonoBehaviour
{
    /// <summary>One trigger plate of the altar.</summary>
    [Serializable]
    public class Plate
    {
        [Tooltip("The trigger area to stand on or roll a ball onto: a Collider 2D with Is Trigger ticked.")]
        public Collider2D trigger;
        [Tooltip("The pictures to colour for this plate. Leave EMPTY to colour every SpriteRenderer on the trigger's object and under it.")]
        public SpriteRenderer[] sprites;

        [NonSerialized] public SpriteRenderer[] targets;
        [NonSerialized] public Color[] originals;
        [NonSerialized] public float progress;   // 0 = at rest, 1 = fully held
        [NonSerialized] public int count;        // how many things are on it this frame
    }

    [Header("The altar's trigger plates")]
    [Tooltip("The plates that must ALL be held at once. Leave EMPTY to use every Collider 2D on or under this altar.")]
    [SerializeField] private Plate[] plates;

    [Header("What counts as 'on a plate'")]
    [Tooltip("Only things on these physics layers count. Players, balls, foes... Leave on Everything and untick layers you want to ignore (for example a sword's hit area).")]
    [SerializeField] private LayerMask occupantMask = ~0;
    [Tooltip("How many things must be on a plate at the same time for it to count as held. 1 = one player or one ball is enough.")]
    [SerializeField, Min(1)] private int itemsNeededPerPlate = 1;
    [Tooltip("ON: a thing only counts if its picture is drawn at or above the plate's (higher Sorting Layer, or the same layer with an equal or higher Order in Layer). OFF: anything touching it counts.")]
    [SerializeField] private bool onlyIfSpriteAbove = true;
    [Tooltip("ON: a thing touching two plates only holds the nearer one. OFF: it holds every plate it touches.")]
    [SerializeField] private bool oneThingOnePlate = true;

    [Header("Holding")]
    [Tooltip("Seconds a plate must be held to be fully faded in.")]
    [SerializeField, Min(0.05f)] private float holdSeconds = 2f;
    [Tooltip("Seconds a plate's colour takes to fade back if it is let go before the altar is triggered.")]
    [SerializeField, Min(0.05f)] private float releaseSeconds = 2f;

    [Header("Colours")]
    [Tooltip("The colour a plate fades towards while it is held.")]
    [SerializeField] private Color heldColor = new Color(1f, 0.85f, 0.25f, 1f);
    [Tooltip("The colour every plate jumps to the instant the altar is triggered.")]
    [SerializeField] private Color triggeredColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Sound")]
    [Tooltip("Optional. Loops while any plate is being held.")]
    [SerializeField] private AudioClip holdSound;
    [Tooltip("Optional. Plays once when the altar is triggered.")]
    [SerializeField] private AudioClip triggeredSound;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    [Header("Debug")]
    [Tooltip("Prints one Console line when something is on a plate but isn't counted because its picture is below the plate's.")]
    [SerializeField] private bool logWhyNotCounted = true;

    /// <summary>The room this altar belongs to (set by that room). Null until a room claims it.</summary>
    public PillarRoom Room { get; set; }

    /// <summary>True once every plate has been held at once. It stays true.</summary>
    public bool IsTriggered { get; private set; }

    /// <summary>How many plates the altar has.</summary>
    public int PlateCount { get { return live != null ? live.Length : 0; } }

    /// <summary>Raised once, the moment the altar is triggered.</summary>
    public event Action<RoomAltar> Triggered;

    private Plate[] live;
    private AudioSource source;
    private bool soundOn;

    private struct Hit
    {
        public GameObject who;
        public int plate;
        public float sq;   // squared distance from the thing to the middle of the plate
    }

    private readonly Collider2D[] buffer = new Collider2D[32];
    private readonly List<Hit> found = new List<Hit>();
    private readonly HashSet<(GameObject, int)> pairs = new HashSet<(GameObject, int)>();
    private readonly Dictionary<GameObject, int> nearestHit = new Dictionary<GameObject, int>();
    private readonly HashSet<GameObject> reported = new HashSet<GameObject>();
    private readonly List<SpriteRenderer> spriteBuffer = new List<SpriteRenderer>();

    // Runs when the component is first added in the editor: plates are walked onto, so their colliders must be triggers.
    private void Reset()
    {
        Collider2D[] all = GetComponentsInChildren<Collider2D>();
        for (int i = 0; i < all.Length; i++) all[i].isTrigger = true;
    }

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        var list = new List<Plate>();
        if (plates != null)
        {
            for (int i = 0; i < plates.Length; i++)
            {
                if (plates[i] != null && plates[i].trigger != null) list.Add(plates[i]);
                else Debug.LogWarning("RoomAltar '" + name + "': Plates element " + i + " has no Trigger, so it is skipped.", this);
            }
        }
        if (list.Count == 0)
        {
            Collider2D[] all = GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < all.Length; i++)
            {
                Plate p = new Plate();
                p.trigger = all[i];
                list.Add(p);
            }
        }

        if (list.Count == 0)
        {
            Debug.LogError("RoomAltar on '" + name + "': it has no plates. Give it a Collider 2D (Is Trigger ticked) on it or under it, or fill in the Plates list. " +
                           "This altar is switched off until you do.", this);
            enabled = false;
            return;
        }

        live = list.ToArray();
        for (int i = 0; i < live.Length; i++)
        {
            Plate p = live[i];
            if (!p.trigger.isTrigger)
            {
                Debug.LogWarning("RoomAltar '" + name + "': the Collider 2D on '" + p.trigger.name + "' is not a trigger, so nothing can stand in it. Tick Is Trigger.", this);
            }

            p.targets = (p.sprites != null && p.sprites.Length > 0) ? p.sprites : p.trigger.GetComponentsInChildren<SpriteRenderer>();
            p.originals = new Color[p.targets.Length];
            for (int s = 0; s < p.targets.Length; s++)
            {
                p.originals[s] = p.targets[s] != null ? p.targets[s].color : Color.white;
            }
        }

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D: both split-screen players hear it
    }

    private void Start()
    {
        if (Room == null)
        {
            Debug.LogWarning("RoomAltar '" + name + "' doesn't belong to any PillarRoom, so triggering it removes nothing. " +
                             "Drag it into the Altars list of its room.", this);
        }
    }

    private void OnDisable()
    {
        StopHoldSound();
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        if (IsTriggered) return;
        if (Room != null && !Room.IsFloorActive) return;   // the team is on another floor: this room is asleep

        CountOccupants();

        bool anyHeld = false;
        bool allFull = true;
        float dt = Time.deltaTime;

        for (int i = 0; i < live.Length; i++)
        {
            Plate p = live[i];
            bool held = p.count >= itemsNeededPerPlate;
            float before = p.progress;

            p.progress = Mathf.Clamp01(p.progress + (held ? dt / holdSeconds : -dt / releaseSeconds));

            if (held) anyHeld = true;
            if (p.progress < 1f) allFull = false;
            if (p.progress != before) ApplyProgressColour(p);
        }

        if (anyHeld != soundOn)
        {
            soundOn = anyHeld;
            if (soundOn) StartHoldSound();
            else StopHoldSound();
        }

        if (allFull) Trigger();
    }

    private void Trigger()
    {
        IsTriggered = true;
        soundOn = false;

        for (int i = 0; i < live.Length; i++)
        {
            live[i].progress = 1f;
            ApplyTriggeredColour(live[i]);   // instant: no fade
        }

        StopHoldSound();
        if (triggeredSound != null) source.PlayOneShot(triggeredSound, volume);

        if (Triggered != null) Triggered(this);
    }

    /// <summary>Puts every plate back to its own colour, ready to be held again. Called by PillarRoom.ResetRoom().</summary>
    public void ResetAltar()
    {
        IsTriggered = false;
        soundOn = false;
        StopHoldSound();

        if (live == null) return;
        for (int i = 0; i < live.Length; i++)
        {
            live[i].progress = 0f;
            live[i].count = 0;
            ApplyProgressColour(live[i]);
        }
    }

    // ------------------------------------------------------------ what is on the plates

    // Fills in Plate.count for every plate.
    private void CountOccupants()
    {
        for (int i = 0; i < live.Length; i++) live[i].count = 0;

        found.Clear();
        pairs.Clear();

        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(occupantMask);
        filter.useTriggers = true;   // a ball or hitbox that is only a trigger still counts

        for (int pi = 0; pi < live.Length; pi++)
        {
            Plate plate = live[pi];
            Collider2D zone = plate.trigger;
            if (zone == null || !zone.enabled || !zone.gameObject.activeInHierarchy) continue;

            int hits = zone.Overlap(filter, buffer);
            Vector2 centre = zone.bounds.center;

            for (int i = 0; i < hits; i++)
            {
                Collider2D c = buffer[i];
                if (c == null) continue;
                if (c.transform.IsChildOf(transform)) continue;                 // the altar's own colliders
                if (c.GetComponentInParent<RoomPillar>() != null) continue;     // pillars never count
                if (c.GetComponentInParent<RoomAltar>() != null) continue;      // neither do other altars

                // One thing with several colliders counts once: they share the same Rigidbody 2D (or, without one, the same object).
                GameObject who = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
                if (pairs.Contains((who, pi))) continue;

                if (onlyIfSpriteAbove && !IsDrawnAbove(who, plate))
                {
                    ReportNotCounted(who);
                    continue;
                }

                pairs.Add((who, pi));

                Hit hit;
                hit.who = who;
                hit.plate = pi;
                hit.sq = ((Vector2)who.transform.position - centre).sqrMagnitude;
                found.Add(hit);
            }
        }

        if (oneThingOnePlate)
        {
            // Each thing is given to the nearest plate it touches.
            nearestHit.Clear();
            for (int h = 0; h < found.Count; h++)
            {
                int existing;
                if (!nearestHit.TryGetValue(found[h].who, out existing) || found[h].sq < found[existing].sq)
                {
                    nearestHit[found[h].who] = h;
                }
            }
            foreach (KeyValuePair<GameObject, int> pair in nearestHit)
            {
                live[found[pair.Value].plate].count++;
            }
        }
        else
        {
            for (int h = 0; h < found.Count; h++) live[found[h].plate].count++;
        }
    }

    // True if any picture on this thing is drawn at or above the plate's picture. (No picture on the plate = nothing to compare with.)
    private bool IsDrawnAbove(GameObject who, Plate plate)
    {
        SpriteRenderer reference = null;
        for (int i = 0; i < plate.targets.Length; i++)
        {
            if (plate.targets[i] != null)
            {
                reference = plate.targets[i];
                break;
            }
        }
        if (reference == null) return true;

        int plateLayer = SortingLayer.GetLayerValueFromID(reference.sortingLayerID);
        int plateOrder = reference.sortingOrder;

        who.GetComponentsInChildren<SpriteRenderer>(false, spriteBuffer);
        for (int i = 0; i < spriteBuffer.Count; i++)
        {
            SpriteRenderer r = spriteBuffer[i];
            if (r == null || !r.enabled) continue;

            int layer = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
            if (layer > plateLayer || (layer == plateLayer && r.sortingOrder >= plateOrder)) return true;
        }
        return false;
    }

    private void ReportNotCounted(GameObject who)
    {
        if (!logWhyNotCounted || !reported.Add(who)) return;
        Debug.Log("RoomAltar '" + name + "': '" + who.name + "' is on a plate but is NOT counted, because its picture is drawn BELOW the plate's " +
                  "(Sorting Layer / Order in Layer). Raise its Order in Layer, or untick Only If Sprite Above on the altar.", this);
    }

    // ------------------------------------------------------------ colour

    private void ApplyProgressColour(Plate plate)
    {
        for (int i = 0; i < plate.targets.Length; i++)
        {
            if (plate.targets[i] == null) continue;

            Color original = plate.originals[i];
            Color mix = Color.Lerp(original, heldColor, plate.progress);
            mix.a = original.a;   // the picture's own transparency never changes
            plate.targets[i].color = mix;
        }
    }

    private void ApplyTriggeredColour(Plate plate)
    {
        for (int i = 0; i < plate.targets.Length; i++)
        {
            if (plate.targets[i] == null) continue;

            Color c = triggeredColor;
            c.a = plate.originals[i].a;
            plate.targets[i].color = c;
        }
    }

    // ------------------------------------------------------------ sound

    private void StartHoldSound()
    {
        if (holdSound == null || source == null) return;
        source.clip = holdSound;
        source.loop = true;
        source.volume = volume;
        source.Play();
    }

    private void StopHoldSound()
    {
        if (source == null || holdSound == null) return;
        if (source.isPlaying && source.clip == holdSound) source.Stop();
    }
}
