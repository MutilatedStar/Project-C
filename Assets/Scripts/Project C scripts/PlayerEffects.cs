using System.Collections.Generic;
using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// The timed power-ups running on ONE player. Every player has their own, so one player's speed-up or
/// invincibility never touches the other. You don't have to add it by hand: ItemPickup adds it to the player that
/// picks up a power-up. Add it yourself to call it from your own scripts:
///
///     player.GetComponent&lt;PlayerEffects&gt;().Apply(PlayerEffects.EffectKind.Speed, 1.5f, 8f);   // 1.5x speed for 8 seconds
///
///   Speed         amount = speed multiplier (1.5 = 50% faster). Works with the top-down and the platformer controller
///                 without touching their Speed number, so upgrades that change Speed keep working.
///   Invincible    no damage at all (needs CoopHealth on the player). Amount is not used. The player gleams gold.
///   Strength      amount = extra damage as a fraction (0.5 = +50% on every sword hit, spin, chain jump and fire trail).
///   AttackSpeed   amount = faster swings as a fraction (0.3 = 30% faster swings, spins and cooldowns).
///
/// Picking up the same kind again refreshes it: the time goes back to full (or stays longer if it was already longer)
/// and the stronger amount wins, so they never stack up by accident. Different kinds run side by side.
/// The timers stand still while the game is paused (stairs menu, notebook...), like the foes do.
/// </summary>
[DisallowMultipleComponent]
public class PlayerEffects : MonoBehaviour
{
    public enum EffectKind { Speed, Invincible, Strength, AttackSpeed }

    /// <summary>One running power-up. Read-only for HUDs (CoopInventoryPanel shows these).</summary>
    public class ActiveEffect
    {
        public EffectKind kind;
        public float amount;
        public float duration;    // the full time it was given
        public float remaining;   // seconds left
    }

    [Header("Look")]
    [Tooltip("The player's tint while Invincible is running. The player gently pulses between their own colour and this one.")]
    [SerializeField] private Color invincibleTint = new Color(1f, 0.9f, 0.3f, 1f);

    private readonly List<ActiveEffect> active = new List<ActiveEffect>();

    private SwordController sword;
    private CoopHealth health;
    private Rigidbody2D rb;
    private SpriteRenderer sprite;
    private bool platformer;
    private bool tinting;
    private Color baseColor = Color.white;
    private float speedMultiplier = 1f;

    /// <summary>The power-ups running right now.</summary>
    public IList<ActiveEffect> Active { get { return active; } }

    /// <summary>True while a power-up of this kind is running.</summary>
    public bool Has(EffectKind kind)
    {
        return Find(kind) != null;
    }

    /// <summary>The speed multiplier in force right now (1 = normal).</summary>
    public float SpeedMultiplier { get { return speedMultiplier; } }

    // ------------------------------------------------------------ names and colours (also used by the HUD)

    public static string ShortName(EffectKind kind)
    {
        switch (kind)
        {
            case EffectKind.Speed: return "SPD";
            case EffectKind.Invincible: return "INV";
            case EffectKind.Strength: return "STR";
            default: return "ATK";
        }
    }

    public static Color ColorOf(EffectKind kind)
    {
        switch (kind)
        {
            case EffectKind.Speed: return new Color(0.25f, 0.6f, 1f);
            case EffectKind.Invincible: return new Color(1f, 0.8f, 0.2f);
            case EffectKind.Strength: return new Color(0.95f, 0.3f, 0.25f);
            default: return new Color(1f, 0.55f, 0.15f);
        }
    }

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        sword = GetComponent<SwordController>();
        health = GetComponent<CoopHealth>();
        rb = GetComponent<Rigidbody2D>();
        sprite = GetComponent<SpriteRenderer>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        platformer = GetComponent<PlatformerCharacterController>() != null;
    }

    private void OnDisable()
    {
        // Switched off (Player 2 removed, or the whole player turned off): leave nothing behind.
        ClearAll();
    }

    // ------------------------------------------------------------ giving and taking

    /// <summary>Starts (or refreshes) a power-up. Returns false if it did nothing (no time, or no amount).</summary>
    public bool Apply(EffectKind kind, float amount, float seconds)
    {
        if (seconds <= 0f) return false;
        if (kind != EffectKind.Invincible && amount <= 0f) return false;

        ActiveEffect e = Find(kind);
        if (e == null)
        {
            e = new ActiveEffect { kind = kind, amount = amount, duration = seconds, remaining = seconds };
            active.Add(e);
        }
        else
        {
            e.amount = Mathf.Max(e.amount, amount);
            e.remaining = Mathf.Max(e.remaining, seconds);
            e.duration = Mathf.Max(e.duration, e.remaining);
        }

        Recalculate();
        return true;
    }

    /// <summary>Ends one kind of power-up straight away.</summary>
    public void Clear(EffectKind kind)
    {
        ActiveEffect e = Find(kind);
        if (e == null) return;
        active.Remove(e);
        Recalculate();
    }

    /// <summary>Ends every power-up straight away (a new run, a death).</summary>
    public void ClearAll()
    {
        if (active.Count == 0 && speedMultiplier == 1f && !tinting) return;
        active.Clear();
        Recalculate();
    }

    private ActiveEffect Find(EffectKind kind)
    {
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].kind == kind) return active[i];
        }
        return null;
    }

    // ------------------------------------------------------------ running

    private void Update()
    {
        if (active.Count == 0) return;

        // Stairs menu, notebook, pause menu: the timers wait, exactly like the foes.
        if (FoeController.IsPaused) return;

        bool expired = false;
        float dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            active[i].remaining -= dt;
            if (active[i].remaining <= 0f)
            {
                active.RemoveAt(i);
                expired = true;
            }
        }

        if (expired) Recalculate();

        UpdateTint();
    }

    // Speed is applied AFTER the controller has set this frame's velocity (LateUpdate), as a multiplier on top, so the
    // controller's own Speed number is never touched. Both controllers set the velocity fresh every frame, so it can't pile up.
    private void LateUpdate()
    {
        if (speedMultiplier <= 1.001f || rb == null) return;

        Vector2 v = Velocity;
        if (platformer) v.x *= speedMultiplier;   // sideways only: jumps and falls keep their own speed
        else v *= speedMultiplier;
        Velocity = v;
    }

    private Vector2 Velocity
    {
#if UNITY_6000_0_OR_NEWER
        get { return rb.linearVelocity; }
        set { rb.linearVelocity = value; }
#else
        get { return rb.velocity; }
        set { rb.velocity = value; }
#endif
    }

    // Works out what everything running adds up to and hands it to the sword, the health and the movement.
    private void Recalculate()
    {
        SwordController.SwordBonus bonus = default(SwordController.SwordBonus);
        float speed = 1f;
        bool invincible = false;

        for (int i = 0; i < active.Count; i++)
        {
            ActiveEffect e = active[i];
            switch (e.kind)
            {
                case EffectKind.Speed: speed = Mathf.Max(speed, e.amount); break;
                case EffectKind.Invincible: invincible = true; break;
                case EffectKind.Strength: bonus.damagePercent += e.amount; break;
                case EffectKind.AttackSpeed: bonus.attackSpeedPercent += e.amount; break;
            }
        }

        speedMultiplier = speed;
        if (sword != null) sword.SetTimedBonus(bonus);

        if (health == null) health = GetComponent<CoopHealth>();
        if (health != null) health.Invincible = invincible;
        else if (invincible) Debug.LogWarning("PlayerEffects on '" + name + "': Invincible needs CoopHealth on the player (it replaces Health). It had no effect.", this);

        if (!invincible && tinting)
        {
            tinting = false;
            if (sprite != null) sprite.color = baseColor;
        }
        else if (invincible && !tinting)
        {
            tinting = true;
            if (sprite != null) baseColor = sprite.color;
        }
    }

    // A slow gold pulse, speeding up in the last two seconds so the player can see it is about to run out.
    private void UpdateTint()
    {
        if (!tinting || sprite == null) return;

        ActiveEffect e = Find(EffectKind.Invincible);
        float rate = (e != null && e.remaining < 2f) ? 16f : 6f;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * rate);
        sprite.color = Color.Lerp(baseColor, invincibleTint, pulse * 0.6f);
    }
}
