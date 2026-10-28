using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One burning line on the ground, left by a sword with a Fire Trail (see SwordController). SwordController creates
/// it by itself, so you never add this by hand and there is no prefab. It flickers, fades out over its last seconds,
/// hurts every foe standing in it every tick, then destroys itself.
///
/// Like everything else in the game it stops while the notebook is open (FoeController.IsPaused), so a trail
/// doesn't burn down while you are reading.
/// </summary>
public class SwordFireTrail : MonoBehaviour
{
    private const float FadeTime = 1.5f;   // the flames fade out over the last 1.5 seconds

    private static readonly Color Orange = new Color(1f, 0.4f, 0.05f);
    private static readonly Color Yellow = new Color(1f, 0.85f, 0.2f);

    private Vector2 center;
    private Vector2 boxSize;
    private float angle;
    private float damage;
    private float tickTime;
    private float tickTimer;
    private float timeLeft;
    private ContactFilter2D filter;

    private SpriteRenderer[] flames;
    private float[] phases;

    private readonly Collider2D[] buffer = new Collider2D[16];
    private readonly HashSet<FoeController> hurtThisTick = new HashSet<FoeController>();

    /// <summary>Lays the flames along a line from start, going in direction, and starts the countdown.</summary>
    public void Begin(Vector2 start, Vector2 direction, float length, float width, float damagePerTick,
                      float secondsBetweenTicks, float lifetime, ContactFilter2D foeFilter,
                      int sortingLayerId, int sortingOrder)
    {
        filter = foeFilter;
        damage = damagePerTick;
        tickTime = Mathf.Max(0.1f, secondsBetweenTicks);
        tickTimer = tickTime * 0.5f;   // first burn half a tick after the flames appear
        timeLeft = lifetime;

        // One box covers the whole line, so a single overlap check per tick finds every foe standing in it.
        angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        center = start + direction * (length * 0.5f);
        boxSize = new Vector2(length, width);

        Vector2 sideways = new Vector2(-direction.y, direction.x);
        int count = Mathf.Max(2, Mathf.RoundToInt(length / 0.35f));
        flames = new SpriteRenderer[count];
        phases = new float[count];

        for (int i = 0; i < count; i++)
        {
            Vector2 position = start + direction * (length * (i + 0.5f) / count)
                               + sideways * Random.Range(-width * 0.25f, width * 0.25f);

            SpriteRenderer flame = new GameObject("Flame").AddComponent<SpriteRenderer>();
            flame.transform.SetParent(transform, false);
            flame.transform.position = position;
            flame.transform.rotation = Quaternion.Euler(0f, 0f, 45f);   // a diamond reads as a flame
            flame.sprite = SwordController.WhiteSquare;
            flame.sortingLayerID = sortingLayerId;
            flame.sortingOrder = sortingOrder;

            flames[i] = flame;
            phases[i] = Random.Range(0f, 6.28f);
        }
    }

    private void Update()
    {
        if (FoeController.IsPaused) return;

        timeLeft -= Time.deltaTime;
        if (timeLeft <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // Flicker, and shrink / fade in the last seconds.
        float fade = Mathf.Clamp01(timeLeft / FadeTime);
        for (int i = 0; i < flames.Length; i++)
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f + phases[i]);
            float size = Mathf.Lerp(0.2f, 0.38f, wave) * Mathf.Lerp(0.5f, 1f, fade);
            flames[i].transform.localScale = new Vector3(size, size, 1f);

            Color c = Color.Lerp(Orange, Yellow, wave);
            c.a = fade * 0.9f;
            flames[i].color = c;
        }

        tickTimer -= Time.deltaTime;
        if (tickTimer <= 0f)
        {
            tickTimer = tickTime;
            BurnFoes();
        }
    }

    // Each foe standing in the flames takes one tick of damage (even if it has several colliders).
    private void BurnFoes()
    {
        int count = Physics2D.OverlapBox(center, boxSize, angle, filter, buffer);
        hurtThisTick.Clear();

        for (int i = 0; i < count; i++)
        {
            FoeController foe = buffer[i].GetComponentInParent<FoeController>();
            if (foe == null || foe.IsDead || !hurtThisTick.Add(foe)) continue;
            foe.TakeDamage(damage);
        }
    }
}
