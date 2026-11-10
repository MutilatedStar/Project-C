using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// Level of detail for ONE foe. SpriteDistanceCuller adds it to every FoeController by itself (Auto Add Foe Lod), or put it on
/// the foe prefab yourself.
///
///   NEAR a player, or in view of a camera:  nothing changes: the full FoeController AI runs.
///   FAR from every player (and out of view): the foe's picture is already hidden by the culler. On top of that this switches
///   the full AI (FoeController) and the animators OFF, and the foe only wanders: a short walk in a random direction, a rest,
///   another walk, never further than Roam Radius from where it was when it went far. It needs almost no CPU. The moment it
///   is near again, the full AI and the animators come back and the foe carries on from where it stands.
///
/// A foe with no Rigidbody 2D just stands still while far (it can't be moved safely without physics, since a wall could end
/// up on top of it). Foes also stand still while the game is paused (FoeController.Frozen / IsPaused), and a dead foe is left alone.
///
/// Reads where the "near" areas are from SpriteDistanceCuller; with no culler in the scene it does nothing.
/// </summary>
[DisallowMultipleComponent]
public class FoeCullLod : MonoBehaviour
{
    [Header("Checking")]
    [Tooltip("Seconds between checks of 'am I near anyone?'. Each foe is offset a little, so they don't all check on the same frame.")]
    [Min(0.05f)] public float checkInterval = 0.25f;

    [Header("Simple roaming while far away")]
    [Tooltip("OFF: a far foe just stands still.")]
    public bool roam = true;
    [Tooltip("Walking speed while roaming (world units per second).")]
    [Min(0f)] public float roamSpeed = 1.2f;
    [Tooltip("A far foe never wanders further than this from the spot where it went far (world units).")]
    [Min(0.5f)] public float roamRadius = 3f;
    [Tooltip("How long one walk (or one rest) lasts: a random number of seconds between these two.")]
    public Vector2 legSeconds = new Vector2(1.5f, 3.5f);
    [Tooltip("Chance that a leg is a rest instead of a walk.")]
    [Range(0f, 1f)] public float restChance = 0.3f;

    [Header("What is switched off while far away")]
    [Tooltip("Switches the FoeController (the full AI) off while far. ON is the whole point: untick it only if switching it off breaks something.")]
    public bool pauseFoeController = true;
    [Tooltip("Switches the foe's Animators off while far (they keep their state and carry on when it is near again).")]
    public bool pauseAnimators = true;
    [Tooltip("Optional. Any other scripts on the foe that should also sleep while it is far (an attack script, a pathfinder...).")]
    public Behaviour[] alsoPause;

    private FoeController foe;
    private Rigidbody2D body;
    private readonly List<Behaviour> pausedBehaviours = new List<Behaviour>();
    private readonly List<Animator> pausedAnimators = new List<Animator>();

    private bool far;
    private Vector2 anchor;
    private Vector2 direction;
    private float legEnd;
    private float nextCheck;

    /// <summary>True while this foe is far from everyone and using the simple AI.</summary>
    public bool IsFar { get { return far; } }

    private void Awake()
    {
        foe = GetComponent<FoeController>();
        if (foe == null) foe = GetComponentInChildren<FoeController>();
        if (foe == null) foe = GetComponentInParent<FoeController>();

        body = GetComponent<Rigidbody2D>();
        if (body == null) body = GetComponentInParent<Rigidbody2D>();
        if (body == null) body = GetComponentInChildren<Rigidbody2D>();

        nextCheck = Time.time + Random.Range(0f, checkInterval);
    }

    private void OnDisable()
    {
        if (far) Wake();   // never leave a foe half switched off
    }

    private void Update()
    {
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + checkInterval * Random.Range(0.8f, 1.2f);
            Recheck();
        }

        if (far) Roam();
    }

    // ------------------------------------------------------------ near or far

    private Vector2 Position()
    {
        return body != null ? body.position : (Vector2)transform.position;
    }

    private void Recheck()
    {
        SpriteDistanceCuller culler = SpriteDistanceCuller.Current;
        if (culler == null || (foe != null && foe.IsDead))
        {
            if (far) Wake();
            return;
        }

        Vector2 p = Position();
        bool inside = culler.IsInActiveZone(p, 0f, !far);

        if (inside && far) Wake();
        else if (!inside && !far) Sleep(p);
    }

    // Goes far: remember where, switch the heavy parts off.
    private void Sleep(Vector2 here)
    {
        far = true;
        anchor = here;
        direction = Vector2.zero;
        legEnd = 0f;

        if (pauseFoeController && foe != null) Pause(foe);

        if (alsoPause != null)
        {
            for (int i = 0; i < alsoPause.Length; i++) Pause(alsoPause[i]);
        }

        if (pauseAnimators)
        {
            Animator[] animators = GetComponentsInChildren<Animator>();
            for (int i = 0; i < animators.Length; i++)
            {
                Animator a = animators[i];
                if (a == null || !a.enabled) continue;

                a.keepAnimatorStateOnDisable = true;
                a.enabled = false;
                pausedAnimators.Add(a);
            }
        }
    }

    // Comes near: switch everything we switched off back on, and stop the roam.
    private void Wake()
    {
        far = false;
        if (body != null) body.linearVelocity = Vector2.zero;

        for (int i = 0; i < pausedBehaviours.Count; i++)
        {
            if (pausedBehaviours[i] != null) pausedBehaviours[i].enabled = true;
        }
        pausedBehaviours.Clear();

        for (int i = 0; i < pausedAnimators.Count; i++)
        {
            if (pausedAnimators[i] != null) pausedAnimators[i].enabled = true;
        }
        pausedAnimators.Clear();
    }

    private void Pause(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        pausedBehaviours.Add(b);
    }

    // ------------------------------------------------------------ the simple AI

    // A walk in a random direction, a rest, another walk... always inside Roam Radius of the anchor.
    private void Roam()
    {
        if (!roam || body == null) return;

        if (FoeController.Frozen || FoeController.IsPaused)
        {
            body.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 p = body.position;
        bool outside = (p - anchor).sqrMagnitude > roamRadius * roamRadius;

        if (Time.time >= legEnd || outside)
        {
            legEnd = Time.time + Random.Range(Mathf.Min(legSeconds.x, legSeconds.y), Mathf.Max(legSeconds.x, legSeconds.y));

            if (outside) direction = (anchor - p).normalized;                 // too far from home: walk back
            else if (Random.value < restChance) direction = Vector2.zero;     // rest
            else direction = Random.insideUnitCircle.normalized;              // a new random direction
        }

        body.linearVelocity = direction * roamSpeed;
    }
}
