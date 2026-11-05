using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A pressure altar. Put this on the altar object (a sprite with a Collider 2D, Is Trigger ticked, covering the spot to
/// stand on) and make the altar a CHILD of its room's PillarRoom object. When the altar is fully pressed, the pillars of
/// THAT room are removed (see PillarRoom).
///
/// WHAT COUNTS AS "ON THE ALTAR": anything whose collider overlaps the altar's trigger, on a layer in Occupant Mask: a
/// player, a ball, a foe, anything. With Only If Sprite Above ticked, a thing only counts if it is drawn at or above the altar
/// (its Sorting Layer is higher, or the same with an equal or higher Order in Layer), so floor decorations and shadows
/// underneath don't press it. One thing with several colliders (a body and a hitbox) is counted once. Pillars and other
/// altars never count. The altar is pressed while at least Items Needed things are on it.
///
/// HOLDING IT:
///   - While it is pressed the altar slowly fades from its own colour to Held Colour, over Hold Seconds, and the Hold Sound
///     loops. After that long it is FULLY triggered: the altar changes to Triggered Colour (gray) at once, the Triggered
///     Sound plays once, and the room is told. It stays triggered.
///   - If the thing steps off before that, the Hold Sound stops and the altar slowly fades back to its own colour over
///     Release Seconds. Anything stepping back on carries on from wherever the fade has got to.
/// </summary>
[DisallowMultipleComponent]
public class RoomAltar : MonoBehaviour
{
    [Header("What counts as 'on the altar'")]
    [Tooltip("Only things on these physics layers count. Players, balls, foes... Leave on Everything and untick layers you want to ignore (for example a sword's hit area).")]
    [SerializeField] private LayerMask occupantMask = ~0;
    [Tooltip("How many things must be on the altar at the same time for it to count as pressed. 1 = one player or one ball is enough; 2 = it needs two.")]
    [SerializeField, Min(1)] private int itemsNeeded = 1;
    [Tooltip("ON: a thing only counts if its picture is drawn at or above the altar's (higher Sorting Layer, or the same layer with an equal or higher Order in Layer). OFF: anything touching it counts.")]
    [SerializeField] private bool onlyIfSpriteAbove = true;

    [Header("Holding")]
    [Tooltip("Seconds it must be held to be fully triggered.")]
    [SerializeField, Min(0.05f)] private float holdSeconds = 2f;
    [Tooltip("Seconds the colour takes to fade back if it is let go before it is fully triggered.")]
    [SerializeField, Min(0.05f)] private float releaseSeconds = 2f;

    [Header("Colours")]
    [Tooltip("The pictures to colour. Leave EMPTY to colour every SpriteRenderer on this altar and under it. The altar's own colour is whatever they are set to now.")]
    [SerializeField] private SpriteRenderer[] sprites;
    [Tooltip("The colour the altar fades towards while it is held.")]
    [SerializeField] private Color heldColor = new Color(1f, 0.85f, 0.25f, 1f);
    [Tooltip("The colour the altar jumps to the instant it is fully triggered.")]
    [SerializeField] private Color triggeredColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("Sound")]
    [Tooltip("Optional. Loops while the altar is being held.")]
    [SerializeField] private AudioClip holdSound;
    [Tooltip("Optional. Plays once when the altar is fully triggered.")]
    [SerializeField] private AudioClip triggeredSound;
    [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

    [Header("Debug")]
    [Tooltip("Prints one Console line when something is standing on the altar but isn't counted because its picture is below the altar's.")]
    [SerializeField] private bool logWhyNotCounted = true;

    /// <summary>True once the altar has been held long enough. It stays true.</summary>
    public bool IsTriggered { get; private set; }

    /// <summary>0 = at rest, 1 = fully triggered.</summary>
    public float Progress { get { return progress; } }

    /// <summary>Raised once, the moment the altar is fully triggered.</summary>
    public event Action<RoomAltar> Triggered;

    private PillarRoom room;
    private Collider2D zone;
    private AudioSource source;
    private SpriteRenderer[] targets;
    private Color[] originals;
    private float progress;
    private bool held;

    private readonly Collider2D[] buffer = new Collider2D[32];
    private readonly HashSet<GameObject> counted = new HashSet<GameObject>();
    private readonly HashSet<GameObject> reported = new HashSet<GameObject>();
    private readonly List<SpriteRenderer> spriteBuffer = new List<SpriteRenderer>();

    // Runs when the component is first added in the editor: an altar is walked onto, so its collider must be a trigger.
    private void Reset()
    {
        Collider2D c = GetComponent<Collider2D>();
        if (c != null) c.isTrigger = true;
    }

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        zone = GetComponent<Collider2D>();
        if (zone == null)
        {
            Debug.LogError("RoomAltar on '" + name + "': it needs a Collider 2D (Is Trigger ticked) to know where to stand. " +
                           "Add one. This altar is switched off until you do.", this);
            enabled = false;
            return;
        }
        if (!zone.isTrigger)
        {
            Debug.LogWarning("RoomAltar on '" + name + "': its Collider 2D is not a trigger, so nothing can stand in it. Tick Is Trigger.", this);
        }

        targets = (sprites != null && sprites.Length > 0) ? sprites : GetComponentsInChildren<SpriteRenderer>();
        originals = new Color[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            originals[i] = targets[i] != null ? targets[i].color : Color.white;
        }

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D: both split-screen players hear it
    }

    private void OnEnable()
    {
        room = GetComponentInParent<PillarRoom>();
        if (room == null)
        {
            Debug.LogWarning("RoomAltar '" + name + "' is not inside a PillarRoom object, so pressing it removes nothing. " +
                             "Make it a child of the room's PillarRoom.", this);
            return;
        }
        room.Register(this);
    }

    private void OnDisable()
    {
        if (room != null) room.Unregister(this);
        StopHoldSound();
        held = false;
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        if (IsTriggered || zone == null) return;

        bool nowHeld = CountOccupants() >= itemsNeeded;
        if (nowHeld != held)
        {
            held = nowHeld;
            if (held) StartHoldSound();
            else StopHoldSound();
        }

        float before = progress;
        if (held) progress += Time.deltaTime / holdSeconds;
        else progress -= Time.deltaTime / releaseSeconds;
        progress = Mathf.Clamp01(progress);

        if (progress >= 1f)
        {
            Trigger();
            return;
        }
        if (progress != before) ApplyProgressColour();
    }

    private void Trigger()
    {
        IsTriggered = true;
        progress = 1f;
        held = false;

        StopHoldSound();
        ApplyTriggeredColour();   // instant: no fade
        if (triggeredSound != null) source.PlayOneShot(triggeredSound, volume);

        if (Triggered != null) Triggered(this);
        if (room != null) room.AltarTriggered(this);
    }

    /// <summary>Puts the altar back to its own colour, ready to be pressed again. Called by PillarRoom.ResetRoom().</summary>
    public void ResetAltar()
    {
        IsTriggered = false;
        held = false;
        progress = 0f;
        StopHoldSound();
        ApplyProgressColour();
    }

    // ------------------------------------------------------------ who is on the altar

    private int CountOccupants()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(occupantMask);
        filter.useTriggers = true;   // a ball or hitbox that is only a trigger still counts

        int hits = zone.Overlap(filter, buffer);
        counted.Clear();

        for (int i = 0; i < hits; i++)
        {
            Collider2D c = buffer[i];
            if (c == null) continue;
            if (c.transform.IsChildOf(transform)) continue;                 // the altar's own colliders
            if (c.GetComponentInParent<RoomPillar>() != null) continue;     // pillars never count
            if (c.GetComponentInParent<RoomAltar>() != null) continue;      // neither do other altars

            // One thing with several colliders counts once: they share the same Rigidbody 2D (or, without one, the same object).
            GameObject who = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
            if (counted.Contains(who)) continue;

            if (onlyIfSpriteAbove && !IsDrawnAbove(who))
            {
                ReportNotCounted(who);
                continue;
            }
            counted.Add(who);
        }
        return counted.Count;
    }

    // True if any picture on this thing is drawn at or above the altar's picture. (No picture on the altar = nothing to compare with.)
    private bool IsDrawnAbove(GameObject who)
    {
        SpriteRenderer reference = ReferenceRenderer();
        if (reference == null) return true;

        int altarLayer = SortingLayer.GetLayerValueFromID(reference.sortingLayerID);
        int altarOrder = reference.sortingOrder;

        who.GetComponentsInChildren<SpriteRenderer>(false, spriteBuffer);
        for (int i = 0; i < spriteBuffer.Count; i++)
        {
            SpriteRenderer r = spriteBuffer[i];
            if (r == null || !r.enabled) continue;

            int layer = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
            if (layer > altarLayer || (layer == altarLayer && r.sortingOrder >= altarOrder)) return true;
        }
        return false;
    }

    private SpriteRenderer ReferenceRenderer()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null) return targets[i];
        }
        return null;
    }

    private void ReportNotCounted(GameObject who)
    {
        if (!logWhyNotCounted || !reported.Add(who)) return;
        Debug.Log("RoomAltar '" + name + "': '" + who.name + "' is on the altar but is NOT counted, because its picture is drawn BELOW the altar's " +
                  "(Sorting Layer / Order in Layer). Raise its Order in Layer, or untick Only If Sprite Above on the altar.", this);
    }

    // ------------------------------------------------------------ colour

    private void ApplyProgressColour()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color original = originals[i];
            Color mix = Color.Lerp(original, heldColor, progress);
            mix.a = original.a;   // the picture's own transparency never changes
            targets[i].color = mix;
        }
    }

    private void ApplyTriggeredColour()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color c = triggeredColor;
            c.a = originals[i].a;
            targets[i].color = c;
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
