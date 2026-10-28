using System.Collections.Generic;   // List / Dictionary / HashSet for the path search. Safe: it does NOT contain Random.
using UnityEngine;
using Ilumisoft.HealthSystem;
using Cainos.PixelArtTopDown_Basic;

// NOTE: no "using System;" on purpose. It makes "Random" ambiguous with UnityEngine.Random.

/// <summary>
/// One script for every foe type (Walker, Runner, Brute...). Make one prefab per foe and change the numbers.
///
/// Behaviour:
///   0. Notebook open (any tab)         -> frozen: no moving, no dealing damage, no taking damage.
///   1. Always Chase on (default), or player within chaseRangePixels -> chase the player,
///      whether the player is moving or standing still.
///      - Straight line to the player is clear for the foe's whole body -> walk straight at the player.
///      - Something is in the way (a corner, a wall, a crate)           -> follow a tile-by-tile route AROUND it
///        (A* search, see "pathfinding" below), so a foe can't get pinned on a corner any more.
///      - Pinned anyway (no movement for a moment)                      -> re-centre on its tile and re-plan.
///      - No route exists (player walled in)                            -> walk to the closest tile it can reach, wait
///        there and keep re-checking, so it comes straight through the moment a crate is broken.
///   2. Otherwise                       -> wander in the four cardinal directions.
///      - In a 1-tile-wide corridor     -> keep walking straight down it (random turning is switched off).
///      - Wall ahead + one side open    -> turn that way automatically (a corner).
///      - Wall ahead + both sides open  -> pick left or right at random (a junction).
///      - Wall ahead + both sides shut  -> turn around (a dead end).
///
/// Suggested starting values (tune by feel):
///                 speedMultiplier   contactDamage   scoreValue   HP (on the HealthComponent)
///      Walker          0.5               10            100            50
///      Runner          1.1                8            150            30
///      Brute           0.75              25            300           150
///
/// HP is NOT set here. Set it on each prefab's HealthComponent (Max Health / Initial Health). Those prefab numbers (and this
/// script's Speed Multiplier and Contact Damage) are only the DEFAULTS: each entry in FoeSpawner can override max HP, damage
/// and speed for the foes it spawns (see SetBaseStats), so one prefab can be used for a big foe and a small foe.
/// Do not also put DemoDummy on a foe: this script already handles death and Destroy().
///
/// Death effect: add FoeDeathExplosion to the foe (on this object or a child) and tick "Use Death Explosion". When the foe
/// dies it then blows up into pieces of its own colours INSTEAD of the old behaviour: the Animator is switched off, "Death
/// Delay" is ignored, and the foe is removed the moment the explosion has finished. FoeDeathExplosion is a separate stand-alone
/// script (it knows nothing about foes), so it can be reused on anything. Leave the toggle off for the old behaviour.
///
/// Idle despawn (set up by FoeSpawner.SetIdleDespawn): a foe that is OFF the camera's view for the whole timer (2 minutes) without
/// ever taking damage is removed and the spawner makes a replacement near the player. The timer resets while the foe is on screen
/// (so a foe never vanishes in view) and is switched off for good the moment it takes any damage. Hand-placed foes never expire.
///
/// Sword hits (SwordController calls these; fire trails call TakeDamage):
///   TakeDamage   health damage through the Health component. Ignored while paused or dead.
///   Knockback    a short shove away from the hit. Knockback Resistance reduces it (Brute 0.5).
///   ApplySlow    moves at a fraction of its speed for a few seconds. The sprite turns light blue.
///   Freeze       frozen solid: can't move, can't attack, animation stops. The sprite turns blue.
///   FlashWhite   a quick white flash (used for critical hits).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Ilumisoft.HealthSystem.Health))]   // the concrete component, so Unity adds it for you on NEW foes
public class FoeController : MonoBehaviour
{
    // ------------------------------------------------------------ difficulty

    public enum Difficulty { Easy = 0, Normal = 1, Hard = 2 }

    /// <summary>One multiplier for each difficulty. Shows up in the Inspector as three boxes.</summary>
    [System.Serializable]
    public struct DifficultyScale
    {
        public float easy;
        public float normal;
        public float hard;

        public DifficultyScale(float easy, float normal, float hard)
        {
            this.easy = easy;
            this.normal = normal;
            this.hard = hard;
        }

        public float Get(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Easy: return easy;
                case Difficulty.Hard: return hard;
                default: return normal;
            }
        }
    }

    // ------------------------------------------------------------ shared by every foe

    /// <summary>Set this from your difficulty menu, e.g. FoeController.CurrentDifficulty = FoeController.Difficulty.Hard;</summary>
    public static Difficulty CurrentDifficulty = Difficulty.Normal;

    /// <summary>Set true from any script to freeze every foe by hand. The notebook (MenuController.IsOpen) freezes them
    /// automatically, so you normally never need to touch this.</summary>
    public static bool Frozen;

    /// <summary>True while the game should LOOK paused: the notebook is open (any tab) or Frozen was set by hand.
    /// Foes don't move, deal damage or take damage while this is true. If another script (the player's attack, a
    /// projectile...) damages foes without using colliders, put "if (FoeController.IsPaused) return;" at the top of it.</summary>
    public static bool IsPaused => Frozen || MenuController.IsOpen;

    // FoeDeathExplosion is stand-alone and knows nothing about this script, so it is handed a way to ask "is the game paused?".
    // That makes its flying pieces freeze with the stairs menu, the pause menu and the notebook. Done once, before the first scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void LinkExplosionPause()
    {
        FoeDeathExplosion.IsPausedCheck = () => IsPaused;
    }

    /// <summary>Fires once when a foe dies, with the final score (base score x difficulty multiplier).
    /// Subscribe your score manager to it: FoeController.OnFoeKilled += points => { ... };</summary>
    public static event System.Action<int> OnFoeKilled;

    /// <summary>Adds points through the SAME event foe kills use (OnFoeKilled), so the score manager that already listens
    /// to it picks them up. DestructibleObject uses this for broken crates. Nothing dies here; anything else that listens
    /// to OnFoeKilled hears these points too.</summary>
    public static void AwardPoints(int points)
    {
        if (points > 0) OnFoeKilled?.Invoke(points);
    }

    // ------------------------------------------------------------ inspector

    [Header("Target")]
    [Tooltip("Leave EMPTY. The spawner (or the 'Player' tag) supplies the live player. A prefab can't hold a scene object, so anything dragged here from the Project window is ignored.")]
    [SerializeField] private Transform target;

    [Header("Chasing")]
    [Tooltip("ON (default): foes always hunt the player from any distance, whether the player is moving or standing still. The Chase Range below is ignored. OFF: foes only chase inside the Chase Range and wander otherwise.")]
    [SerializeField] private bool alwaysChase = true;
    [Tooltip("Only used when Always Chase is OFF. Chase the player when they are this many PIXELS away or closer. Foes spawn 6-10 tiles (192-320 px) away, so a value below about 330 means they only notice the player once it walks closer.")]
    [SerializeField] private float chaseRangePixels = 384f;
    [Tooltip("Pixels per Unity unit of your sprites (Pixel Art Top Down uses 32).")]
    [SerializeField] private float pixelsPerUnit = 32f;
    [Tooltip("If on, a wall between the foe and the player cancels the chase (the foe wanders instead). Off = chase through walls, may snag on them.")]
    [SerializeField] private bool requireLineOfSight = false;

    [Header("Speed")]
    [Tooltip("Chase speed as a fraction of the player's walking speed. 0.5 = half as fast, 1.1 = a bit faster.")]
    [SerializeField] private float speedMultiplier = 0.75f;
    [Tooltip("Used only if the player has no TopDownCharacterController to read speed from.")]
    [SerializeField] private float fallbackPlayerSpeed = 3f;
    [Tooltip("Wandering speed as a fraction of this foe's chase speed.")]
    [SerializeField] private float wanderSpeedFactor = 0.5f;
    [Tooltip("Extra speed multiplier per difficulty. 1 / 1 / 1 = difficulty does not change speed.")]
    [SerializeField] private DifficultyScale speedScale = new DifficultyScale(1f, 1f, 1f);

    [Header("Wandering / corridors")]
    [Tooltip("Layers that count as walls. Walls only: do NOT include the Player or Foe layers.")]
    [SerializeField] private LayerMask wallMask = ~0;
    [Tooltip("Size of one tile in pixels. A corridor is 'one tile' wide when walls are on both sides.")]
    [SerializeField] private float tilePixels = 32f;
    [Tooltip("In open ground the foe picks a new random direction every X to Y seconds.")]
    [SerializeField] private Vector2 wanderTurnTime = new Vector2(1.5f, 4f);

    [Header("Damage")]
    [Tooltip("Damage dealt to the player per hit, before the difficulty multiplier.")]
    [SerializeField] private float contactDamage = 10f;
    [Tooltip("Seconds between hits while touching the player.")]
    [SerializeField] private float attackCooldown = 1f;
    [Tooltip("Damage multiplier per difficulty (Easy / Normal / Hard).")]
    [SerializeField] private DifficultyScale damageScale = new DifficultyScale(0.75f, 1f, 1.5f);

    [Header("Score")]
    [Tooltip("Points for killing this foe, before the difficulty multiplier.")]
    [SerializeField] private int scoreValue = 100;
    [Tooltip("Score multiplier per difficulty (Easy / Normal / Hard).")]
    [SerializeField] private DifficultyScale scoreScale = new DifficultyScale(0.5f, 1f, 2f);

    [Header("Death / animation")]
    [Tooltip("Seconds before the dead foe is destroyed (DemoDummy used 1).")]
    [SerializeField] private float deathDelay = 1f;
    [Tooltip("Drive an Animator with the same parameters as the player (Direction int, IsMoving bool). Off by default so a missing Animator does not spam warnings.")]
    [SerializeField] private bool useAnimator = false;
    [Tooltip("ON: when this foe dies, the FoeDeathExplosion on it (or on a child) takes over. It OVERRIDES the Animator (switched off at death) " +
             "and 'Death Delay' above (ignored): the foe is removed the moment the explosion has finished. Needs a FoeDeathExplosion component " +
             "on this object or a child. OFF: the old behaviour, wait 'Death Delay' seconds and then remove the foe.")]
    [SerializeField] private bool useDeathExplosion = false;

    [Header("Sword hits")]
    [Tooltip("0 = a sword pushes this foe back the full distance. 1 = can't be pushed at all. Suggested: Walker 0, Runner 0, Brute 0.5.")]
    [Range(0f, 1f)]
    [SerializeField] private float knockbackResistance = 0f;
    [Tooltip("Seconds a push-back lasts. The sword decides how far the foe is pushed in that time.")]
    [SerializeField] private float knockbackTime = 0.15f;
    [Tooltip("Sprite colour while frozen.")]
    [SerializeField] private Color frozenColor = new Color(0.35f, 0.65f, 1f);
    [Tooltip("Sprite colour while slowed.")]
    [SerializeField] private Color slowedColor = new Color(0.75f, 0.88f, 1f);

    [Header("Pathfinding (corners and obstacles)")]
    [Tooltip("ON: when something blocks the straight line to the player, the foe walks a tile-by-tile route around it instead of pushing into the corner. OFF: the old behaviour (always walk straight at the player).")]
    [SerializeField] private bool usePathfinding = true;
    [Tooltip("How often (seconds) a chasing foe re-checks its route. Each foe uses a slightly different moment so searches don't all land on one frame. Lower = reacts faster to a moving player, costs more.")]
    [SerializeField] private float repathInterval = 0.35f;
    [Tooltip("Most tiles one route search may look at. Bigger finds longer detours but costs more. If the player is further around than this, the foe walks to the closest reachable tile and searches again.")]
    [SerializeField] private int maxSearchTiles = 600;

    [Header("Idle despawn (set by FoeSpawner)")]
    [Tooltip("OFF: this foe type is never removed for being idle, whatever floor it is on. Leave ON for normal foes; turn OFF on a boss prefab. The seconds and the exempt floors are set on FoeSpawner.")]
    [SerializeField] private bool canIdleDespawn = true;
    [Tooltip("Extra border around the camera view, in TILES, that still counts as 'on screen'. Stops a foe from vanishing right at the edge of the screen.")]
    [SerializeField] private float viewPaddingTiles = 1.5f;

    [Header("Debug")]
    [Tooltip("Prints one line per second to the Console describing what this foe is doing. Turn it on for ONE foe while troubleshooting.")]
    [SerializeField] private bool debugLog = false;

    // ------------------------------------------------------------ state

    private Rigidbody2D rb;
    private Collider2D col;
    private HealthComponent health;
    private Animator animator;

    // Sword hit effects (see "sword hits" near the bottom)
    private SpriteRenderer sprite;
    private Color baseColor = Color.white;
    private Vector2 knockVelocity;
    private float knockTimeLeft;
    private float slowFactor = 1f;
    private float slowTimeLeft;
    private float frozenTimeLeft;
    private SpriteMask flashMask;
    private SpriteRenderer flashFill;
    private float flashTimeLeft;

    private HealthComponent targetHealth;
    private TopDownCharacterController playerController;

    private ContactFilter2D wallFilter;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[8];

    private static readonly Vector2[] Cardinals =
        { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    private Vector2 heading = Vector2.right;   // always one of the four cardinal directions
    private float wanderTimer;
    private bool wasChasing;
    private float nextAttackTime;
    private bool dead;

    // Pathfinding state (see "pathfinding" near the bottom of the main loop)
    private enum ChaseMode { Direct, Path, Lost }
    private ChaseMode chaseMode = ChaseMode.Direct;   // Direct = straight at the player, Path = follow 'path', Lost = no route, wander
    private readonly List<Vector2> path = new List<Vector2>();   // world positions of tile centres, in walking order
    private int pathIndex;
    private bool pathReachesPlayer;     // true if the last route search got all the way to the player's tile
    private float repathTimer;
    private float avoidDirectUntil;     // after being pinned, the straight-line shortcut is ignored until this time
    private Vector2 stuckAnchor;
    private float stuckTimer;
    private float recenterTimeLeft;     // > 0: walking back to the middle of the current tile to get free of a corner

    // Idle despawn state (see "idle despawn" at the bottom of the main loop)
    private float idleLimit;            // seconds from the spawner. 0 = this foe never idle-despawns
    private float idleLeft;
    private bool idleExpired;
    private bool hurtOnce;              // true after the foe has taken ANY damage: the timer is gone for good
    private float healthBaseline = -1f; // last health seen, to spot damage that didn't come through TakeDamage

    // Death explosion (only used when "Use Death Explosion" is ticked)
    private FoeDeathExplosion deathFx;
    private bool deathFxActive;      // true once the explosion has taken over: the Animator and the hit flash stay off
    private bool warnedNoDeathFx;

    // Set by the spawner through ApplyRandomStatus(). 1 = unchanged.
    private float speedRoll = 1f;
    private float damageRoll = 1f;
    private float scoreRoll = 1f;
    private float healthRoll = 1f;
    private bool healthRollPending;
    private float healthBaseOverride;   // max HP handed over by the spawner (SetBaseStats). 0 = keep the prefab's Health value

    /// <summary>True once the foe's health hit zero. The spawner uses this so dead foes stop counting toward the cap.</summary>
    public bool IsDead => dead;

    private GameObject rootObject;   // the whole spawned foe prefab; null = just this object

    /// <summary>The object that is this whole foe: the spawned prefab root, or this object if none was set.</summary>
    public GameObject Root => rootObject != null ? rootObject : gameObject;

    /// <summary>Called by FoeSpawner right after Instantiate.</summary>
    public void SetRoot(GameObject root)
    {
        rootObject = root;
    }

    private float TileSize => tilePixels / pixelsPerUnit;

    // Chase speed = player's walking speed x this foe's multiplier x difficulty speed multiplier.
    private float ChaseSpeed
    {
        get
        {
            float playerSpeed = playerController != null ? playerController.speed : fallbackPlayerSpeed;
            return playerSpeed * speedMultiplier * speedScale.Get(CurrentDifficulty) * speedRoll * (slowTimeLeft > 0f ? slowFactor : 1f);
        }
    }

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        if (col == null) col = GetComponentInChildren<Collider2D>();   // a collider on a child object is fine too
        health = GetComponent<HealthComponent>();
        animator = GetComponent<Animator>();
        deathFx = GetComponentInChildren<FoeDeathExplosion>(true);   // this object or a child; only used if "Use Death Explosion" is ticked
        sprite = GetComponent<SpriteRenderer>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        if (sprite != null) baseColor = sprite.color;

        rb.gravityScale = 0f;      // top-down game: nothing should fall
        rb.freezeRotation = true;  // don't spin when bumped
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;   // a sleeping body stops sending OnCollisionStay2D, so a foe pressed against a still player could stop hurting it

        wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(wallMask);
        wallFilter.useTriggers = false;

        // Clear messages for the most common setup mistakes. Unity can NOT add the Collider2D or the Health
        // component for you (they are abstract requirements), so they must be added by hand on the foe prefab.
        if (col == null)
        {
            Debug.LogError("FoeController on '" + name + "': no Collider2D found. Add a non-trigger collider " +
                           "(e.g. Box Collider 2D) to the foe prefab. This foe is switched off.", this);
            enabled = false;
            return;
        }
        if (health == null)
        {
            Debug.LogError("FoeController on '" + name + "': no Health component on this object. Add Component > " +
                           "Health System > Health. Without it the foe can't take damage or die.", this);
        }
        if (GetComponent<TopDownCharacterController>() != null)
        {
            Debug.LogWarning("FoeController on '" + name + "': this object also has the PLAYER's " +
                             "TopDownCharacterController, which overwrites the foe's movement. Remove it from the foe.", this);
        }
        if (rb.bodyType != RigidbodyType2D.Dynamic)
        {
            Debug.LogWarning("FoeController on '" + name + "': the Rigidbody2D Body Type should be Dynamic.", this);
        }

        if (health != null) health.OnHealthEmpty += Die;
    }

    private void Start()
    {
        EnsureTarget();
        CacheTarget();   // also covers a target that was already valid, so EnsureTarget didn't cache it
        PickRandomHeading();
    }

    // ------------------------------------------------------------ spawner interface

    /// <summary>
    /// Called by FoeSpawner right after Instantiate. Seconds this foe may stay OFF-screen and unhurt before it can be removed
    /// (FoeSpawner.RemoveStray then gives it back to the level so a replacement spawns near the player). 0 or less = never
    /// (the spawner passes 0 on exempt floors). A foe that was never given a time here, such as one placed by hand, never expires.
    /// The prefab's "Can Idle Despawn" tick overrides this: OFF means never.
    /// </summary>
    public void SetIdleDespawn(float seconds)
    {
        idleLimit = canIdleDespawn ? Mathf.Max(0f, seconds) : 0f;
        idleLeft = idleLimit;
        idleExpired = false;
    }

    /// <summary>Called by FoeSpawner right after Instantiate so the foe doesn't have to search for the player.</summary>
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        CacheTarget();
    }

    /// <summary>
    /// Called by FoeSpawner right after Instantiate, BEFORE ApplyRandomStatus. Replaces this foe's base max HP, contact damage
    /// and speed multiplier with the numbers set on its spawner entry. A value of 0 or less leaves that stat as it is on the
    /// prefab. Only this spawned copy changes, never the prefab or the other foes made from it. The difficulty multipliers and
    /// the random variation still apply on top of these base numbers.
    /// </summary>
    public void SetBaseStats(float maxHealth, float damage, float speed)
    {
        if (damage > 0f) contactDamage = damage;
        if (speed > 0f) speedMultiplier = speed;

        if (maxHealth > 0f)
        {
            healthBaseOverride = maxHealth;
            healthRollPending = true;   // applied on the first FixedUpdate, together with the random health roll
        }
    }

    /// <summary>
    /// Called by FoeSpawner right after Instantiate. Speed, damage and HP each get a separate random
    /// multiplier between (1 - variation) and (1 + variation). Score follows the average of those three
    /// rolls, so a tougher foe is worth a little more. variation = 0.05 means up to 5% either way.
    /// </summary>
    public void ApplyRandomStatus(float variation)
    {
        speedRoll = Roll(variation);
        damageRoll = Roll(variation);
        healthRoll = Roll(variation);
        scoreRoll = (speedRoll + damageRoll + healthRoll) / 3f;
        healthRollPending = true;   // applied on the first FixedUpdate, after HealthComponent has set itself up
    }

    private static float Roll(float variation)
    {
        return 1f + Random.Range(-variation, variation);
    }

    private static bool warnedNoPlayerHealth;

    private void CacheTarget()
    {
        if (target == null) return;
        targetHealth = target.GetComponentInParent<HealthComponent>();
        if (targetHealth == null) targetHealth = target.GetComponentInChildren<HealthComponent>();   // Health on a child object

        if (targetHealth == null && !warnedNoPlayerHealth)
        {
            warnedNoPlayerHealth = true;
            Debug.LogWarning("FoeController: the player ('" + target.name + "') has no Health component on it, its parents or " +
                             "its children, so foes cannot damage it. Add Component > Health System > Health to the player.");
        }
        playerController = target.GetComponent<TopDownCharacterController>();
    }

    private float nextDebugTime;

    // One Console line per second (only when "Debug Log" is ticked) so you can see WHY a foe isn't doing something.
    private void DebugState()
    {
        if (!debugLog || Time.time < nextDebugTime) return;
        nextDebugTime = Time.time + 1f;

        bool liveTarget = target != null && target.gameObject.scene.IsValid();
        string distance = liveTarget ? Vector2.Distance(rb.position, target.position).ToString("F1") : "-";
        Debug.Log(name + " | target=" + (target != null ? target.name : "NONE") + " (live in scene: " + liveTarget + ")" +
                  " | targetHealth found: " + (targetHealth != null) +
                  " | distance (units)=" + distance + " | chase range (units)=" + (chaseRangePixels / pixelsPerUnit).ToString("F1") +
                  " | velocity=" + rb.linearVelocity + " | heading=" + heading +
                  " | alwaysChase=" + alwaysChase + " | Paused=" + IsPaused + " | dead=" + dead + " | bodyType=" + rb.bodyType +
                  " | chaseMode=" + chaseMode + " | pathTiles=" + path.Count +
                  " | idleTimer=" + (idleLimit <= 0f ? "off" : hurtOnce ? "off (hurt)" : idleLeft.ToString("F0") + "s"));
    }

    private float nextTargetSearch;

    // The target must be the LIVE player in the scene. A prefab asset (the only thing a foe PREFAB can hold in its Target
    // slot), an empty slot, or a destroyed player all make the foe look for the object tagged "Player" instead,
    // retrying twice a second. While no valid target exists the foe simply wanders.
    private bool EnsureTarget()
    {
        if (target != null && target.gameObject.scene.IsValid()) return true;
        if (Time.time < nextTargetSearch) return false;
        nextTargetSearch = Time.time + 0.5f;

        GameObject found = GameObject.FindWithTag("Player");
        if (found == null)
        {
            target = null;
            targetHealth = null;
            playerController = null;
            return false;
        }

        target = found.transform;
        CacheTarget();
        return true;
    }

    // True if a wall sits between the foe and the player (the foe's own and the player's colliders are ignored).
    private bool LineBlocked()
    {
        int count = Physics2D.Linecast(rb.position, target.position, wallFilter, hits);
        for (int i = 0; i < count; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == col || c.attachedRigidbody == rb || c.transform == target || c.transform.IsChildOf(target)) continue;
            return true;
        }
        return false;
    }

    // Uses the Health component's settable MaxHealth and SetHealth().
    private void ApplyHealthRoll()
    {
        healthRollPending = false;
        if (health == null) return;
        if (healthBaseOverride > 0f) health.MaxHealth = healthBaseOverride;   // the spawner entry's HP replaces the prefab's, then the roll applies to it
        health.MaxHealth *= healthRoll;
        health.SetHealth(health.MaxHealth);
        healthBaseline = -1f;   // the roll is not "damage": the idle timer re-reads the health on its next tick
    }

    private void OnDestroy()
    {
        if (health != null) health.OnHealthEmpty -= Die;
    }

    // ------------------------------------------------------------ main loop

    private bool pauseApplied;

    // Update (not FixedUpdate) so the pause still gets applied and lifted even if the notebook sets timeScale to 0.
    private void Update()
    {
        bool paused = IsPaused;
        if (paused == pauseApplied) return;
        pauseApplied = paused;
        ApplyPause(paused);
    }

    // While paused the foe is taken out of the physics world: it can't move, can't be pushed, can't hurt the player,
    // and attacks that use colliders / triggers / overlap checks can't hit it. Animations are frozen too.
    private void ApplyPause(bool paused)
    {
        if (paused)
        {
            rb.linearVelocity = Vector2.zero;
            if (useAnimator && animator != null) animator.speed = 0f;
            rb.simulated = false;
        }
        else
        {
            if (!dead) rb.simulated = true;   // a dead foe stays out of physics (its collider is already off)
            if (useAnimator && animator != null) animator.speed = frozenTimeLeft > 0f ? 0f : 1f;
        }
    }

    private void FixedUpdate()
    {
        if (healthRollPending) ApplyHealthRoll();

        DebugState();

        if (dead)
        {
            Stop();
            return;
        }

        if (IsPaused)
        {
            Stop();
            nextAttackTime += Time.fixedDeltaTime;   // the attack cooldown doesn't tick while paused, so no instant hit the moment the notebook closes
            return;
        }

        TickStatuses(Time.fixedDeltaTime);
        TickIdle(Time.fixedDeltaTime);   // not reached while the notebook is open, so the timer pauses with everything else

        if (frozenTimeLeft > 0f)    // frozen solid: no moving
        {
            Stop();
            return;
        }
        if (knockTimeLeft > 0f)     // being knocked back: the shove replaces normal movement
        {
            rb.linearVelocity = knockVelocity;
            return;
        }

        bool hasTarget = EnsureTarget();
        bool playerAlive = targetHealth == null || targetHealth.CurrentHealth > 0f;
        bool chasing = hasTarget && playerAlive && PlayerInRange();

        if (chasing)
        {
            if (!wasChasing)
            {
                repathTimer = Random.value * 0.15f;   // just started chasing: decide the route almost at once, each foe on a slightly different frame
                stuckTimer = 0f;
                stuckAnchor = Center;
            }
            ChaseStep();
            wasChasing = true;
        }
        else
        {
            if (wasChasing)
            {
                // Lost the player: carry on in whichever cardinal direction we were drifting.
                heading = ToCardinal(rb.linearVelocity, heading);
                ResetWanderTimer();
                wasChasing = false;
            }
            Wander(ChaseSpeed * wanderSpeedFactor);
        }
    }

    private bool PlayerInRange()
    {
        if (!alwaysChase)
        {
            float rangeUnits = chaseRangePixels / pixelsPerUnit;
            if (Vector2.Distance(rb.position, target.position) > rangeUnits) return false;
        }
        if (requireLineOfSight && LineBlocked()) return false;
        return true;
    }

    // ------------------------------------------------------------ pathfinding

    // The old chase walked in a dead-straight line at the player, so any corner or wall in between pinned the foe against
    // it for as long as the player stood still behind it. Now, every 'repathInterval' seconds each chasing foe decides:
    //   Direct - a box as wide as the foe can slide straight to the player: walk straight at them (smooth, cheap).
    //   Path   - something is in the way: search the tile grid (A*) for a route around it and follow it tile by tile.
    //   Lost   - no route to the player (they are walled in): stand at the closest reachable tile and keep re-checking.
    // The route only uses tiles where a body the size of this foe fits, so it never plans through a gap that is too narrow.
    // Anything on the wall mask counts as a wall, crates and other destructibles included, and a broken crate stops
    // counting on the very next search, so foes pour through a gap the moment the player opens one.

    // The straight-line test uses a box slightly WIDER than the foe: a line that only just squeezes past a corner is
    // treated as blocked, so the foe goes around instead of clipping the corner and getting pinned on it.
    private const float DirectLineScale = 1.1f;
    // A tile counts as free when a box slightly SMALLER than the foe fits in it, so a wall touching the tile's edge
    // doesn't count as being inside it. Never bigger than one tile, so a very big foe can still plan something.
    private const float TileProbeScale = 0.9f;
    private const float StuckCheckSeconds = 0.5f;
    // Above 1 the search leans toward the player: far fewer tiles looked at, routes a hair longer than perfect.
    private const float PathWeight = 1.15f;

    private struct OpenNode
    {
        public Vector2Int cell;
        public float f;
    }

    // Scratch space shared by every foe. Safe because a search finishes inside one call, never across frames.
    private static readonly List<OpenNode> openList = new List<OpenNode>();
    private static readonly HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
    private static readonly Dictionary<Vector2Int, int> stepsTo = new Dictionary<Vector2Int, int>();
    private static readonly Dictionary<Vector2Int, bool> blockedCache = new Dictionary<Vector2Int, bool>();
    private static readonly Collider2D[] overlapHits = new Collider2D[16];
    private static readonly RaycastHit2D[] sweepHits = new RaycastHit2D[16];
    private static readonly Vector2Int[] CellSteps =
        { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };

    private Vector2 searchOrigin;   // the foe's centre while a search runs
    private Vector2 probeSize;      // the box used to test one tile while a search runs

    private void ChaseStep()
    {
        Vector2 here = Center;
        Vector2 goal = target.position;

        if (!usePathfinding)
        {
            Move((goal - (Vector2)rb.position).normalized, ChaseSpeed);   // the old behaviour
            return;
        }

        CheckStuck(here, goal);

        // Pinned on something: walk back to the middle of the current tile (that frees a foe caught on a corner), then re-plan.
        if (recenterTimeLeft > 0f)
        {
            recenterTimeLeft -= Time.fixedDeltaTime;
            Vector2 middle = CellCenter(WorldToCell(here));
            if (Vector2.Distance(here, middle) > 0.05f)
            {
                Move((middle - here).normalized, ChaseSpeed);
                return;
            }
            recenterTimeLeft = 0f;
        }

        repathTimer -= Time.fixedDeltaTime;
        if (repathTimer <= 0f) Repath(here, goal);

        switch (chaseMode)
        {
            case ChaseMode.Path:
                FollowPath(here, goal);
                break;
            case ChaseMode.Lost:
                Stop();
                break;
            default:
                Move((goal - (Vector2)rb.position).normalized, ChaseSpeed);
                break;
        }
    }

    // Decides Direct / Path / Lost. Runs every 'repathInterval' seconds, or at once when a foe is pinned.
    private void Repath(Vector2 here, Vector2 goal)
    {
        repathTimer = repathInterval * Random.Range(0.8f, 1.2f);

        bool shortcutAllowed = Time.time >= avoidDirectUntil;
        if (shortcutAllowed && SweepClear(here, goal, DirectLineScale))
        {
            chaseMode = ChaseMode.Direct;
            return;
        }

        if (FindPath(here, goal))
        {
            chaseMode = ChaseMode.Path;
            return;
        }

        // No route. In the same tile as the player there is nothing to plan, so just walk at them; otherwise wait.
        chaseMode = WorldToCell(here) == WorldToCell(goal) ? ChaseMode.Direct : ChaseMode.Lost;
    }

    private void FollowPath(Vector2 here, Vector2 goal)
    {
        float reach = Mathf.Max(0.1f, ChaseSpeed * Time.fixedDeltaTime * 1.5f);
        while (pathIndex < path.Count && Vector2.Distance(here, path[pathIndex]) <= reach) pathIndex++;

        if (pathIndex >= path.Count)
        {
            // End of the route. If it led all the way to the player, walk at them. If not, the player can't be reached
            // right now: stand here and let the next search look again.
            if (pathReachesPlayer)
            {
                chaseMode = ChaseMode.Direct;
                Move((goal - (Vector2)rb.position).normalized, ChaseSpeed);
            }
            else
            {
                chaseMode = ChaseMode.Lost;
                Stop();
            }
            return;
        }

        Move((path[pathIndex] - here).normalized, ChaseSpeed);
    }

    // Spots a foe that is chasing but barely moving (pinned by a corner or a crate edge). It then skips the straight-line
    // shortcut for a moment, walks back to the middle of its tile, and plans a route.
    private void CheckStuck(Vector2 here, Vector2 goal)
    {
        // Pressing against the player is normal, not "stuck".
        if (Vector2.Distance(here, goal) < TileSize * 1.3f)
        {
            stuckTimer = 0f;
            stuckAnchor = here;
            return;
        }

        stuckTimer += Time.fixedDeltaTime;
        if (stuckTimer < StuckCheckSeconds) return;

        float shouldHaveMoved = ChaseSpeed * stuckTimer;
        bool pinned = Vector2.Distance(here, stuckAnchor) < shouldHaveMoved * 0.25f;
        stuckTimer = 0f;
        stuckAnchor = here;

        if (!pinned) return;
        avoidDirectUntil = Time.time + 1.5f;
        recenterTimeLeft = 0.3f;
        repathTimer = 0f;
    }

    // True if a box the size of this foe (times 'scale') can slide from 'from' to 'to' without touching a wall.
    private bool SweepClear(Vector2 from, Vector2 to, float scale)
    {
        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;

        int count = Physics2D.BoxCast(from, col.bounds.size * scale, 0f, delta / distance, wallFilter, sweepHits, distance);
        for (int i = 0; i < count; i++)
        {
            if (IgnoreForPath(sweepHits[i].collider, from)) continue;
            return false;
        }
        return true;
    }

    // Colliders that never count as a wall when planning: this foe itself, the player, and any collider the foe's centre is
    // already INSIDE (a big background collider, not a wall). The same idea Blocked() uses for its "starts inside" rule.
    private bool IgnoreForPath(Collider2D c, Vector2 centre)
    {
        if (c == null || c == col || c.attachedRigidbody == rb) return true;
        if (target != null && (c.transform == target || c.transform.IsChildOf(target))) return true;
        return c.OverlapPoint(centre);
    }

    // A* over the tile grid, from the foe's tile to the player's tile, 4 directions. Fills 'path' with tile centres.
    // Returns false if there is nothing to follow (same tile, or no tile closer to the player can be reached).
    // If the player's tile can't be reached, the route goes to the reachable tile closest to them instead.
    private bool FindPath(Vector2 here, Vector2 goal)
    {
        path.Clear();
        pathIndex = 0;
        pathReachesPlayer = false;

        Vector2Int start = WorldToCell(here);
        Vector2Int goalCell = WorldToCell(goal);
        if (start == goalCell) return false;

        searchOrigin = here;
        Vector2 body = col.bounds.size * TileProbeScale;
        float biggest = TileSize * 0.95f;
        probeSize = new Vector2(Mathf.Min(body.x, biggest), Mathf.Min(body.y, biggest));

        openList.Clear();
        closedSet.Clear();
        cameFrom.Clear();
        stepsTo.Clear();
        blockedCache.Clear();

        stepsTo[start] = 0;
        PushOpen(start, Heuristic(start, goalCell) * PathWeight);

        Vector2Int closest = start;
        int closestDistance = Heuristic(start, goalCell);
        int looked = 0;
        bool reached = false;

        while (openList.Count > 0 && looked < maxSearchTiles)
        {
            Vector2Int cell = PopOpen();
            if (!closedSet.Add(cell)) continue;   // an older, worse copy of a tile that is already finished
            looked++;

            if (cell == goalCell)
            {
                reached = true;
                closest = cell;
                break;
            }

            int remaining = Heuristic(cell, goalCell);
            if (remaining < closestDistance)
            {
                closestDistance = remaining;
                closest = cell;
            }

            int steps = stepsTo[cell] + 1;
            for (int i = 0; i < CellSteps.Length; i++)
            {
                Vector2Int next = cell + CellSteps[i];
                if (closedSet.Contains(next)) continue;
                if (next != goalCell && CellBlocked(next)) continue;
                if (stepsTo.TryGetValue(next, out int known) && known <= steps) continue;

                stepsTo[next] = steps;
                cameFrom[next] = cell;
                PushOpen(next, steps + Heuristic(next, goalCell) * PathWeight);
            }
        }

        if (closest == start) return false;   // nowhere closer to the player than right here

        // Walk back from the end to the start, then flip it into walking order.
        Vector2Int back = closest;
        while (back != start)
        {
            path.Add(CellCenter(back));
            back = cameFrom[back];
        }
        path.Add(CellCenter(start));
        path.Reverse();

        pathReachesPlayer = reached;

        // The foe is usually part-way across its own tile. If it is already nearer the second tile than the first
        // tile's middle is, don't walk back to the first one.
        if (path.Count > 1 && Vector2.Distance(here, path[1]) < Vector2.Distance(path[0], path[1])) pathIndex = 1;
        return true;
    }

    // True if something solid sits in this tile (for a body the size of the foe). Remembered for the rest of the search.
    private bool CellBlocked(Vector2Int cell)
    {
        if (blockedCache.TryGetValue(cell, out bool known)) return known;

        int count = Physics2D.OverlapBox(CellCenter(cell), probeSize, 0f, wallFilter, overlapHits);
        bool blocked = false;
        for (int i = 0; i < count; i++)
        {
            if (IgnoreForPath(overlapHits[i], searchOrigin)) continue;
            blocked = true;
            break;
        }

        blockedCache[cell] = blocked;
        return blocked;
    }

    private static int Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);   // tiles, ignoring walls
    }

    // A tiny priority queue (smallest 'f' first). Duplicates are allowed: the search skips tiles it has already finished.
    private static void PushOpen(Vector2Int cell, float f)
    {
        openList.Add(new OpenNode { cell = cell, f = f });
        int i = openList.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (openList[parent].f <= openList[i].f) break;
            OpenNode swap = openList[parent];
            openList[parent] = openList[i];
            openList[i] = swap;
            i = parent;
        }
    }

    private static Vector2Int PopOpen()
    {
        Vector2Int top = openList[0].cell;
        int last = openList.Count - 1;
        openList[0] = openList[last];
        openList.RemoveAt(last);

        int i = 0;
        while (true)
        {
            int left = i * 2 + 1;
            int right = left + 1;
            int smallest = i;
            if (left < openList.Count && openList[left].f < openList[smallest].f) smallest = left;
            if (right < openList.Count && openList[right].f < openList[smallest].f) smallest = right;
            if (smallest == i) break;

            OpenNode swap = openList[smallest];
            openList[smallest] = openList[i];
            openList[i] = swap;
            i = smallest;
        }
        return top;
    }

    // Tile <-> world. Uses the scene's tile Grid so route tiles line up exactly with the real tiles (whatever its position
    // or cell size). With no Grid in the scene it assumes tiles start at the world origin.
    private static UnityEngine.Grid sharedGrid;
    private static float nextGridSearch;

    private static UnityEngine.Grid TileGrid()
    {
        if (sharedGrid == null)
        {
            if (nextGridSearch > Time.time + 5f) nextGridSearch = 0f;   // left over from an earlier play session
            if (Time.time >= nextGridSearch)
            {
                nextGridSearch = Time.time + 2f;   // don't search every call when there is no Grid at all
                sharedGrid = FindFirstObjectByType<UnityEngine.Grid>();
            }
        }
        return sharedGrid;
    }

    private Vector2Int WorldToCell(Vector2 p)
    {
        UnityEngine.Grid g = TileGrid();
        if (g != null)
        {
            Vector3Int c = g.WorldToCell(p);
            return new Vector2Int(c.x, c.y);
        }
        return new Vector2Int(Mathf.FloorToInt(p.x / TileSize), Mathf.FloorToInt(p.y / TileSize));
    }

    private Vector2 CellCenter(Vector2Int c)
    {
        UnityEngine.Grid g = TileGrid();
        if (g != null) return g.GetCellCenterWorld(new Vector3Int(c.x, c.y, 0));
        return new Vector2((c.x + 0.5f) * TileSize, (c.y + 0.5f) * TileSize);
    }

    // ------------------------------------------------------------ idle despawn

    // Runs every unpaused physics step (so it stands still while the notebook is open). See SetIdleDespawn.
    //   - Took ANY damage: the timer is gone for good.
    //   - On screen (camera view plus a border): the timer resets to full, so a foe never vanishes where it can be seen.
    //   - Off screen: counts down. At zero the spawner removes the foe and makes a replacement near the player.
    private void TickIdle(float dt)
    {
        if (idleLimit <= 0f || hurtOnce) return;

        // Any drop in health counts, even damage that didn't come through TakeDamage (an explosion, a hazard...).
        if (health != null)
        {
            float hp = health.CurrentHealth;
            if (healthBaseline < 0f)
            {
                healthBaseline = hp;
            }
            else if (hp < healthBaseline - 0.001f)
            {
                hurtOnce = true;
                idleExpired = false;
                return;
            }
            else
            {
                healthBaseline = hp;
            }
        }

        if (IsOnScreen())
        {
            idleLeft = idleLimit;
            idleExpired = false;
            return;
        }

        idleLeft -= dt;
        if (idleLeft <= 0f) idleExpired = true;
    }

    /// <summary>True when the idle timer has run out AND the foe is still off screen right now. FoeSpawner checks this
    /// every frame and removes the foe (through RemoveStray, so the level gives it back and a replacement spawns).</summary>
    public bool ShouldIdleDespawn()
    {
        if (IsPaused) return false;   // a paused foe is out of the physics world, so its collider bounds can't be trusted
        return idleExpired && !dead && !hurtOnce && !IsOnScreen();
    }

    // Is any part of this foe inside the main camera's view (plus 'viewPaddingTiles' on every side)?
    private bool IsOnScreen()
    {
        Camera cam = Camera.main;
        if (cam == null || col == null) return true;   // can't tell where the camera is: count as seen, so nothing is removed by mistake

        Bounds b = col.bounds;
        float pad = viewPaddingTiles * TileSize;
        Vector3 min = cam.WorldToViewportPoint(new Vector3(b.min.x - pad, b.min.y - pad, b.center.z));
        Vector3 max = cam.WorldToViewportPoint(new Vector3(b.max.x + pad, b.max.y + pad, b.center.z));

        if (!cam.orthographic && max.z <= 0f) return false;   // behind a perspective camera
        return max.x >= 0f && min.x <= 1f && max.y >= 0f && min.y <= 1f;
    }

    // ------------------------------------------------------------ wandering

    private void Wander(float speed)
    {
        Vector2 left = new Vector2(-heading.y, heading.x);   // 90 degrees counter-clockwise from heading
        Vector2 right = -left;

        bool forwardOpen = !Blocked(heading, TileSize * 0.1f, 0.9f);
        bool leftOpen = !Blocked(left, TileSize, 0.5f);
        bool rightOpen = !Blocked(right, TileSize, 0.5f);
        bool inCorridor = !leftOpen && !rightOpen;           // walls on both sides = one-tile corridor

        if (!forwardOpen)
        {
            if (leftOpen && !rightOpen) heading = left;                       // corner: turn automatically
            else if (rightOpen && !leftOpen) heading = right;                 // corner: turn automatically
            else if (leftOpen && rightOpen) heading = Random.value < 0.5f ? left : right;  // junction
            else heading = -heading;                                          // dead end: turn around
            ResetWanderTimer();
        }
        else if (!inCorridor)
        {
            // Open ground: change direction at random every so often.
            wanderTimer -= Time.fixedDeltaTime;
            if (wanderTimer <= 0f) PickRandomHeading();
        }
        // else: in a corridor with a clear path ahead -> keep going straight, ignore the random timer.

        Move(heading, speed);
    }

    // Picks a random cardinal direction that isn't blocked (starts the search at a random one).
    private void PickRandomHeading()
    {
        int start = Random.Range(0, Cardinals.Length);
        for (int i = 0; i < Cardinals.Length; i++)
        {
            Vector2 dir = Cardinals[(start + i) % Cardinals.Length];
            if (!Blocked(dir, TileSize * 0.1f, 0.9f))
            {
                heading = dir;
                break;
            }
        }
        ResetWanderTimer();
    }

    private void ResetWanderTimer()
    {
        wanderTimer = Random.Range(wanderTurnTime.x, wanderTurnTime.y);
    }

    // Casts a box from the foe's collider in a direction and reports whether a wall is in the way.
    // boxScale shrinks the box (1 = full collider size) so scraping along a wall doesn't count as a hit.
    private bool Blocked(Vector2 dir, float distance, float boxScale)
    {
        Bounds b = col.bounds;
        int count = Physics2D.BoxCast(b.center, b.size * boxScale, 0f, dir, wallFilter, hits, distance);
        for (int i = 0; i < count; i++)
        {
            Collider2D c = hits[i].collider;
            if (c == col || c.attachedRigidbody == rb) continue;   // ourselves (including child colliders)
            if (hits[i].distance <= 0f) continue;                  // probe STARTS inside it (big background collider): not "ahead"
            return true;
        }
        return false;
    }

    private static Vector2 ToCardinal(Vector2 v, Vector2 fallback)
    {
        if (v.sqrMagnitude < 0.0001f) return fallback;
        return Mathf.Abs(v.x) > Mathf.Abs(v.y)
            ? new Vector2(Mathf.Sign(v.x), 0f)
            : new Vector2(0f, Mathf.Sign(v.y));
    }

    // ------------------------------------------------------------ movement / animation

    private void Move(Vector2 dir, float speed)
    {
        rb.linearVelocity = dir * speed;

        if (useAnimator && animator != null)
        {
            animator.SetBool("IsMoving", dir.sqrMagnitude > 0.0001f);

            // Same numbers the player script uses: 0 = down, 1 = up, 2 = right, 3 = left.
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                animator.SetInteger("Direction", dir.x > 0f ? 2 : 3);
            else
                animator.SetInteger("Direction", dir.y > 0f ? 1 : 0);
        }
    }

    private void Stop()
    {
        rb.linearVelocity = Vector2.zero;
        if (useAnimator && animator != null && !deathFxActive) animator.SetBool("IsMoving", false);
    }

    // ------------------------------------------------------------ combat

    // Hurts the player while touching them, at most once per attackCooldown.
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (dead || IsPaused || IsFrozen || targetHealth == null) return;
        if (Time.time < nextAttackTime) return;
        if (targetHealth.CurrentHealth <= 0f) return;
        Transform other = collision.collider.transform;
        if (target == null || (other != target && !other.IsChildOf(target))) return;   // only hurt the player

        DealDamage(targetHealth, contactDamage * damageScale.Get(CurrentDifficulty) * damageRoll);
        nextAttackTime = Time.time + attackCooldown;
    }

    // ApplyDamage (NOT AddHealth with a negative number): it is the only call that fires OnHealthEmpty at zero.
    private void DealDamage(HealthComponent victim, float amount)
    {
        victim.ApplyDamage(amount);
    }

    private void Die()
    {
        if (dead) return;   // guard so the score is only awarded once
        dead = true;

        rb.linearVelocity = Vector2.zero;
        col.enabled = false;   // dead foes shouldn't block or hurt the player

        int points = Mathf.RoundToInt(scoreValue * scoreScale.Get(CurrentDifficulty) * scoreRoll);
        OnFoeKilled?.Invoke(points);

        Remove(StartDeathEffect());
    }

    // Returns how many seconds the dead foe stays before it is removed.
    //   Toggle OFF, or nothing to play: the normal Death Delay.
    //   Toggle ON: the Animator is switched off and the explosion decides the time (Death Delay is ignored).
    private float StartDeathEffect()
    {
        if (!useDeathExplosion) return deathDelay;

        if (deathFx == null)
        {
            if (!warnedNoDeathFx)
            {
                warnedNoDeathFx = true;
                Debug.LogWarning("FoeController on '" + name + "': 'Use Death Explosion' is ticked but there is no FoeDeathExplosion " +
                                 "on this object or its children, so the normal Death Delay is used. Add the component to the foe prefab.", this);
            }
            return deathDelay;
        }

        deathFxActive = true;
        if (animator != null) animator.enabled = false;   // the explosion owns the visuals now
        CancelFlash();                                    // no white silhouette left over the burst
        return deathFx.Explode();
    }

    // Ends a hit flash straight away (the explosion replaces it as the death feedback).
    private void CancelFlash()
    {
        flashTimeLeft = 0f;
        if (flashMask != null) flashMask.enabled = false;
        if (flashFill != null) flashFill.enabled = false;
    }

    // ------------------------------------------------------------ removing a foe

    private static bool warnedSharedRoot;

    /// <summary>
    /// Removes THIS foe from the game (after an optional delay) without taking anyone else with it.
    ///
    /// Destroying an object also destroys everything parented under it. So if other living foes, or the FoeSpawner,
    /// sit somewhere under this foe's object (for example when the spawner's Foe Parent is a foe, or one prefab holds
    /// several foes), they are first moved out to that object's own parent. One death can then never wipe out the
    /// rest. Death calls this, and so do FoeSpawner.ClearFoes and RemoveStray.
    /// </summary>
    public void Remove(float delay = 0f)
    {
        GameObject doomed = Root;

        // If the root is a different, still living foe, only this foe (a child of it) is removed.
        if (doomed != gameObject)
        {
            FoeController rootFoe = doomed.GetComponent<FoeController>();
            if (rootFoe != null && !rootFoe.dead) doomed = gameObject;
        }

        // A spawner that sits ON the same object as the foe can't be moved out of it, and destroying the object
        // would destroy the spawner. So just hide the foe and leave the object alone.
        if (doomed.GetComponent<FoeSpawner>() != null)
        {
            Debug.LogError("FoeController: the FoeSpawner is on the same object as the foe '" + doomed.name + "', so that " +
                           "object can't be destroyed without destroying the spawner. Move the spawner onto its own empty " +
                           "object. For now only this foe's own sprites are hidden.", this);

            // Hide ONLY this foe's own renderers. Renderers belonging to any OTHER foe parented under this object
            // (the ones the spawner made) must stay visible.
            foreach (Renderer r in doomed.GetComponentsInChildren<Renderer>(true))
            {
                FoeController owner = r.GetComponentInParent<FoeController>(true);
                if (owner == null || owner == this) r.enabled = false;
            }
            return;
        }

        Transform newParent = doomed.transform.parent;   // survivors go here (null = the top of the scene)
        int saved = 0;

        // Other living foes under the doomed object move out. A foe that has its own prefab root below the doomed
        // object takes the whole root with it; otherwise just the foe's own object moves.
        foreach (FoeController other in doomed.GetComponentsInChildren<FoeController>(true))
        {
            if (other == this || other.dead || other.gameObject == doomed) continue;

            GameObject unit = other.Root;
            if (unit == doomed || !unit.transform.IsChildOf(doomed.transform) || transform.IsChildOf(unit.transform))
            {
                unit = other.gameObject;
                other.rootObject = unit;   // from now on this foe is its own root
            }
            unit.transform.SetParent(newParent, true);   // true = it stays exactly where it is in the world
            saved++;
        }

        // Same for a spawner hiding somewhere under the doomed object.
        foreach (FoeSpawner spawner in doomed.GetComponentsInChildren<FoeSpawner>(true))
        {
            spawner.transform.SetParent(newParent, true);
            saved++;
        }

        if (saved > 0 && !warnedSharedRoot)
        {
            warnedSharedRoot = true;
            Debug.LogWarning("FoeController: '" + name + "' was removed while its object '" + doomed.name + "' also held " +
                             saved + " other living foe(s) / the spawner. They were moved out so they survive. Usually that " +
                             "means the spawner's Foe Parent, or the spawner itself, is a foe, or one prefab contains several " +
                             "foes. An empty container object for Foe Parent avoids it. (Shown once.)", this);
        }

        Destroy(doomed, delay);
    }

    // ------------------------------------------------------------ sword hits

    /// <summary>True while the foe is frozen solid (can't move or attack).</summary>
    public bool IsFrozen => frozenTimeLeft > 0f;

    /// <summary>The middle of this foe's collider. The sword uses it to aim and to chain lightning. (A dead foe's
    /// collider is switched off, so this falls back to its position.)</summary>
    public Vector2 Center => (col != null && col.enabled) ? (Vector2)col.bounds.center : (Vector2)transform.position;

    /// <summary>Health damage. Goes through ApplyDamage so FoeController.Die() runs at zero. Does nothing while the
    /// notebook is open or after death.</summary>
    public void TakeDamage(float amount)
    {
        if (dead || IsPaused || health == null) return;
        if (amount > 0f) hurtOnce = true;   // any damage at all removes the idle timer for good
        health.ApplyDamage(amount);
    }

    /// <summary>Shoves the foe this many units in a direction (over Knockback Time). Reduced by Knockback Resistance.</summary>
    public void Knockback(Vector2 direction, float distance)
    {
        if (dead || IsPaused || IsFrozen) return;

        distance *= 1f - knockbackResistance;
        if (distance <= 0f || knockbackTime <= 0f || direction.sqrMagnitude < 0.0001f) return;

        knockVelocity = direction.normalized * (distance / knockbackTime);
        knockTimeLeft = knockbackTime;
    }

    /// <summary>Slows the foe to this fraction of its speed (0.5 = half) for this many seconds. A stronger or longer
    /// slow replaces a weaker one.</summary>
    public void ApplySlow(float speedFactor, float duration)
    {
        if (dead || IsPaused || duration <= 0f) return;

        slowFactor = slowTimeLeft > 0f ? Mathf.Min(slowFactor, speedFactor) : speedFactor;
        slowTimeLeft = Mathf.Max(slowTimeLeft, duration);
        UpdateTint();
    }

    /// <summary>Freezes the foe solid for this many seconds: it can't move or attack, its animation stops and it turns blue.</summary>
    public void Freeze(float duration)
    {
        if (dead || IsPaused || duration <= 0f) return;

        frozenTimeLeft = Mathf.Max(frozenTimeLeft, duration);
        knockTimeLeft = 0f;
        if (useAnimator && animator != null) animator.speed = 0f;
        UpdateTint();
    }

    // Counts the slow / freeze / knockback timers down (physics time, so they pause with everything else).
    private void TickStatuses(float dt)
    {
        bool tintChanged = false;

        if (knockTimeLeft > 0f) knockTimeLeft -= dt;

        if (slowTimeLeft > 0f)
        {
            slowTimeLeft -= dt;
            if (slowTimeLeft <= 0f)
            {
                slowFactor = 1f;
                tintChanged = true;
            }
        }

        if (frozenTimeLeft > 0f)
        {
            frozenTimeLeft -= dt;
            if (frozenTimeLeft <= 0f)
            {
                if (useAnimator && animator != null) animator.speed = 1f;
                tintChanged = true;
            }
        }

        if (tintChanged) UpdateTint();
    }

    // Frozen = blue, slowed = light blue, otherwise the colour the sprite started with.
    private void UpdateTint()
    {
        if (sprite == null) return;

        if (frozenTimeLeft > 0f) sprite.color = frozenColor;
        else if (slowTimeLeft > 0f) sprite.color = slowedColor;
        else sprite.color = baseColor;
    }

    /// <summary>A quick pure-white flash in the shape of the sprite (the foe's current animation frame). Works even on
    /// the hit that kills the foe.</summary>
    public void FlashWhite(float duration = 0.12f)
    {
        if (sprite == null || deathFxActive) return;   // once the explosion has taken over, a flash would draw a white ghost of the hidden body
        if (flashFill == null) BuildFlash();

        flashTimeLeft = duration;
        flashMask.enabled = true;
        flashFill.enabled = true;
        SyncFlash();
    }

    // A SpriteMask with the foe's sprite, plus a white square that is only visible INSIDE that mask: together they
    // draw a white silhouette of the sprite, with no special shader or material needed.
    private void BuildFlash()
    {
        Transform parent = sprite.transform;

        flashMask = new GameObject("HitFlashMask").AddComponent<SpriteMask>();
        flashMask.transform.SetParent(parent, false);
        flashMask.alphaCutoff = 0.1f;

        flashFill = new GameObject("HitFlashFill").AddComponent<SpriteRenderer>();
        flashFill.transform.SetParent(parent, false);
        flashFill.sprite = SwordController.WhiteSquare;
        flashFill.color = Color.white;
        flashFill.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        flashFill.sortingLayerID = sprite.sortingLayerID;
        flashFill.sortingOrder = sprite.sortingOrder + 1;

        flashMask.enabled = false;
        flashFill.enabled = false;
    }

    // Keeps the silhouette matching the sprite's current frame (the Animator swaps frames while it flashes).
    private void SyncFlash()
    {
        Sprite current = sprite.sprite;
        flashMask.sprite = current;
        if (current == null) return;

        Bounds b = current.bounds;
        flashFill.transform.localPosition = b.center;
        flashFill.transform.localScale = new Vector3(b.size.x * 1.1f, b.size.y * 1.1f, 1f);
    }

    private void LateUpdate()
    {
        if (flashTimeLeft <= 0f || IsPaused) return;   // the flash holds still while paused and finishes afterwards

        flashTimeLeft -= Time.deltaTime;
        if (flashTimeLeft <= 0f)
        {
            flashMask.enabled = false;
            flashFill.enabled = false;
            return;
        }
        SyncFlash();
    }

    // ------------------------------------------------------------ editor helper

    // Select a foe in the Scene view to see its chase range as a red circle, and (while playing) the route it is
    // following as a cyan line.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, chaseRangePixels / pixelsPerUnit);

        if (chaseMode == ChaseMode.Path && path.Count > 0)
        {
            Gizmos.color = Color.cyan;
            Vector3 previous = Center;
            for (int i = Mathf.Max(0, pathIndex); i < path.Count; i++)
            {
                Gizmos.DrawLine(previous, path[i]);
                previous = path[i];
            }
        }
    }
}