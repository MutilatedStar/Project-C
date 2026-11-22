using UnityEngine;

/// <summary>
/// Fire, ice and lightning sparks that ride along the blade while it is out.
///
/// There is NOTHING to add by hand: SwordController builds this itself and feeds it the equipped sword.
/// Which effect a sword gets comes from its own numbers, so a custom sword with a mix gets a mix:
///   Fire trail on (Trail Duration above 0)               fire embers, rising
///   Ice on (Slow Factor below 1 or Freeze Chance above 0) frost glints, drifting down
///   Lightning chain on (Chain Targets above 0)           short bright sparks, flicking in every direction
/// Per sword you can turn them off (Show Blade Particles) or thin / thicken them (Particle Amount).
///
/// The sparks spawn at random points along the blade and stay in the world where they were born, so a swing leaves a
/// short glittering smear behind it. The square-pixel look suits pixel art, and they stop being made when the blade goes away.
/// </summary>
public class SwordElementFx
{
    private const int Fire = 0;
    private const int Ice = 1;
    private const int Lightning = 2;

    private readonly ParticleSystem[] systems = new ParticleSystem[3];
    private readonly ParticleSystemRenderer[] renderers = new ParticleSystemRenderer[3];
    private readonly bool[] wanted = new bool[3];
    private readonly float[] baseRate = { 45f, 28f, 60f };   // sparks per second on a blade about 1.3 units long

    private bool shown;
    private int sortingLayerId;
    private int sortingOrder;

    /// <param name="parent">The sword's pivot (the hand). +X of it is along the blade.</param>
    /// <param name="material">Any sprite material. The blade's own one is used so it renders like the rest of the game.</param>
    public SwordElementFx(Transform parent, Material material)
    {
        systems[Fire] = Create("FireSparks", parent, material);
        systems[Ice] = Create("IceSparks", parent, material);
        systems[Lightning] = Create("LightningSparks", parent, material);

        for (int i = 0; i < 3; i++) renderers[i] = systems[i].GetComponent<ParticleSystemRenderer>();

        // Fire: orange-yellow embers that rise and shrink away, fading from bright to deep red.
        ParticleSystem.MainModule fire = systems[Fire].main;
        fire.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        fire.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
        fire.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.05f), new Color(1f, 0.85f, 0.25f));
        Drift(systems[Fire], -0.25f, 0.25f, 0.5f, 1.1f);
        Shrink(systems[Fire], 0f);
        FadeColor(systems[Fire], Color.white, new Color(1f, 0.3f, 0.1f), 1f, 0f);

        // Ice: pale blue glints that appear, twinkle and sink slowly.
        ParticleSystem.MainModule ice = systems[Ice].main;
        ice.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
        ice.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
        ice.startColor = new ParticleSystem.MinMaxGradient(new Color(0.65f, 0.92f, 1f), Color.white);
        Drift(systems[Ice], -0.2f, 0.2f, -0.4f, 0.05f);
        Shrink(systems[Ice], 0.3f);
        Twinkle(systems[Ice]);

        // Lightning: tiny hard sparks that live a split second and jump in random directions.
        ParticleSystem.MainModule bolt = systems[Lightning].main;
        bolt.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        bolt.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        bolt.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 0.5f), Color.white);   // real colour set per sword in Configure
        Drift(systems[Lightning], -1.4f, 1.4f, -1.4f, 1.4f);
        FadeColor(systems[Lightning], Color.white, Color.white, 1f, 0f);
    }

    // ------------------------------------------------------------ used by SwordController

    /// <summary>Picks the effects for this sword and fits them to the blade. Safe to call any time, even mid-swing.</summary>
    public void Configure(SwordController.SwordStats s, float reach)
    {
        bool allowed = s.showBladeParticles && s.particleAmount > 0f;
        wanted[Fire] = allowed && s.trailDuration > 0f;
        wanted[Ice] = allowed && (s.slowFactor < 1f || s.freezeChance > 0f);
        wanted[Lightning] = allowed && s.chainTargets > 0;

        // Skip the hilt: sparks come from the last 80% of the blade.
        float start = reach * 0.2f;
        float end = Mathf.Max(reach, 0.3f);
        float half = (end - start) * 0.5f;
        float middle = (start + end) * 0.5f;
        float lengthFactor = Mathf.Max(0.5f, reach / 1.3f);

        if (wanted[Lightning])
        {
            ParticleSystem.MainModule bolt = systems[Lightning].main;
            bolt.startColor = new ParticleSystem.MinMaxGradient(s.boltColor, Color.white);
        }

        for (int i = 0; i < 3; i++)
        {
            ParticleSystem ps = systems[i];
            if (!wanted[i])
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                continue;
            }

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = half;
            shape.position = new Vector3(middle, 0f, 0f);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = baseRate[i] * s.particleAmount * lengthFactor;

            ApplySorting(i);
            if (shown) ps.Play(true);
        }
    }

    /// <summary>Freezes every spark exactly where it is (true) or lets them go on (false). SwordController calls this while the game is
    /// paused, so a blade held mid-swing doesn't keep spraying sparks. It only changes the playback speed, so which effects are
    /// playing or stopped is untouched. Safe to call every frame.</summary>
    public void SetPaused(bool paused)
    {
        float speed = paused ? 0f : 1f;
        for (int i = 0; i < 3; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.simulationSpeed = speed;
        }
    }

    /// <summary>The blade just came out: start sparking. Sorting follows the blade (in front of the player, or behind when facing up).</summary>
    public void Show(int layerId, int order)
    {
        shown = true;
        sortingLayerId = layerId;
        sortingOrder = order;

        for (int i = 0; i < 3; i++)
        {
            if (!wanted[i]) continue;
            ApplySorting(i);
            systems[i].Play(true);
        }
    }

    /// <summary>The blade went away: stop making new sparks. The ones already alive fade out by themselves.</summary>
    public void Hide()
    {
        shown = false;
        for (int i = 0; i < 3; i++) systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // ------------------------------------------------------------ building the systems

    private void ApplySorting(int index)
    {
        renderers[index].sortingLayerID = sortingLayerId;
        renderers[index].sortingOrder = sortingOrder;
    }

    // One empty particle system laid along the blade. Everything shared lives here; each element adds its own look above.
    private static ParticleSystem Create(string name, Transform parent, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // it starts playing by itself; stop it before configuring

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;       // sparks stay where they were born
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;           // sized like the blade, even if the player is scaled
        main.startSpeed = 0f;                                             // movement comes from Drift below
        main.gravityModifier = 0f;
        main.maxParticles = 150;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;                   // a line along the pivot's +X, which is the blade
        shape.radiusMode = ParticleSystemShapeMultiModeValue.Random;      // random spot along the line

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    // Each spark picks its own random velocity (world space) between these limits.
    private static void Drift(ParticleSystem ps, float xMin, float xMax, float yMin, float yMax)
    {
        ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(xMin, xMax);   // all three axes must use the same curve mode
        vel.y = new ParticleSystem.MinMaxCurve(yMin, yMax);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    // Size goes from 100% at birth down to this fraction at death.
    private static void Shrink(ParticleSystem ps, float endFraction)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, endFraction));
    }

    // Tints from one colour to another over the spark's life while the alpha goes from one value to another.
    private static void FadeColor(ParticleSystem ps, Color from, Color to, float alphaFrom, float alphaTo)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(alphaFrom, 0f), new GradientAlphaKey(alphaTo, 1f) });

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    // Fades in, glints bright, then fades out.
    private static void Twinkle(ParticleSystem ps)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }
}
