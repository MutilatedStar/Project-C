using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2D side-on (platformer) version of TopDownCharacterController, with jumping.
/// (C# class names can't start with a number, so this is the "2D character controller".)
///
/// Put it on the player with a Rigidbody2D and a Collider2D. It reads ALL input from the PlayerInputSource on the
/// same object (Scheme = Platformer), so the same script drives Player 1 and Player 2: just set Player Number on that
/// component. Its keys are Inspector fields there (defaults: Player 1 A/D + Space or W, Player 2 arrows + Up).
/// Needs PlayerInputSource.cs, DirectButton.cs and LocalCoopManager.cs.
///
/// Jump feel: you can still jump a moment after walking off a ledge (Coyote Time), a jump pressed just before
/// landing still happens (Jump Buffer), letting go early gives a short hop, and falling is a bit heavier than rising.
///
/// Optional Animator parameters, only set if they exist on your Animator (no error if they don't):
///   IsMoving (bool)   IsGrounded (bool)   VelocityY (float)   Jump (trigger)
///
/// Pausing: list this script in PauseManager's "Player Input Scripts". A menu or inventory that should freeze just
/// this player can set  GetComponent&lt;PlayerInputSource&gt;().Locked = true.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(PlayerInputSource))]
public class PlatformerCharacterController : MonoBehaviour
{
    [Header("Run")]
    public float speed = 6f;
    public float sprintMultiplier = 1.5f;
    [Tooltip("ON: tap the run key to switch running on/off (like the top-down version). OFF: run only while held.")]
    public bool sprintToggle = true;
    [Tooltip("ON: a gentle controller-stick tilt walks slowly. OFF: any tilt walks at full speed. Keyboard is unaffected.")]
    public bool analogMovement = true;

    [Header("Jump")]
    [Tooltip("How fast the jump launches you upward (Unity units per second). Higher = higher jump.")]
    public float jumpSpeed = 12f;
    [Tooltip("Extra jumps in mid-air. 0 = a normal single jump, 1 = double jump.")]
    [Min(0)] public int airJumps = 0;
    [Tooltip("Seconds after walking off a ledge during which a jump still works.")]
    [Min(0f)] public float coyoteTime = 0.1f;
    [Tooltip("Seconds before landing during which a jump press is remembered and fires on landing.")]
    [Min(0f)] public float jumpBufferTime = 0.1f;
    [Tooltip("Letting go of jump while rising multiplies the upward speed by this. 1 = no short hops, 0.5 = default.")]
    [Range(0.1f, 1f)] public float jumpCutMultiplier = 0.5f;
    [Tooltip("Gravity is this many times stronger while falling. 1 = same as rising.")]
    [Min(1f)] public float fallGravityMultiplier = 1.6f;
    [Tooltip("The fastest the character can fall.")]
    public float maxFallSpeed = 20f;

    [Header("Ground check")]
    [Tooltip("Layers that count as ground. The player's own collider is always ignored. Triggers are ignored.")]
    public LayerMask groundLayers = ~0;
    [Tooltip("How far under the feet the ground is looked for.")]
    public float groundCheckDistance = 0.08f;

    [Header("Facing (optional)")]
    [Tooltip("Drag the player's SpriteRenderer here to flip it with flipX. Empty = the whole object is flipped by its X scale.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("ON: the artwork is drawn facing RIGHT. Turn off if it is drawn facing left.")]
    public bool artFacesRight = true;

    [Header("Footsteps")]
    [Tooltip("The AudioSource that plays the steps. Put it on its OWN empty child object (not on the player). To make the SFX on/off button silence it, also drag it into SfxManager's 'Also Mute' list.")]
    [SerializeField] private AudioSource footstepSource;
    [Tooltip("One or more step sounds. With several, each step picks one at random and never the same one twice in a row.")]
    [SerializeField] private AudioClip[] stepClips;
    [Tooltip("Seconds between steps while walking.")]
    [SerializeField] private float walkStepInterval = 0.45f;
    [Tooltip("Seconds between steps while sprinting.")]
    [SerializeField] private float sprintStepInterval = 0.3f;
    [Tooltip("Random pitch change per step while walking, as a fraction (0.04 = up to 4% higher or lower).")]
    [Range(0f, 0.2f)]
    [SerializeField] private float walkPitchVariation = 0.04f;
    [Tooltip("Same while sprinting.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float sprintPitchVariation = 0.07f;
    [Tooltip("Tiny random change to the time between steps, as a fraction of the interval.")]
    [Range(0f, 0.2f)]
    [SerializeField] private float stepTimingVariation = 0.05f;

    // ------------------------------------------------------------ public

    public bool IsGrounded { get { return grounded; } }
    public bool IsSprinting { get { return isSprinting; } }
    public bool FacingRight { get { return facingRight; } }

    // ------------------------------------------------------------ state

    private Rigidbody2D rb;
    private Collider2D col;
    private Animator animator;
    private PlayerInputSource input;
    private HashSet<string> animatorParams;

    private ContactFilter2D groundFilter;
    private readonly List<Collider2D> groundHits = new List<Collider2D>(8);

    private float baseGravity = 1f;
    private bool grounded;
    private bool isSprinting;
    private bool facingRight = true;
    private int airJumpsLeft;
    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;

    // Footstep state
    private float basePitch = 1f;
    private float stepTimer;
    private bool wasStepping;
    private int lastClipIndex = -1;

    // Unity 6 renamed Rigidbody2D.velocity to linearVelocity; this works in both.
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

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();

        // RequireComponent guarantees the PlayerInputSource exists (the "no jump key" check is in Start, after every Awake).
        input = GetComponent<PlayerInputSource>();

        if (col == null)
        {
            Debug.LogError("PlatformerCharacterController on '" + name + "': add a Collider2D, or the ground can't be detected. " +
                           "This script is switched off.", this);
            enabled = false;
            return;
        }

        baseGravity = rb.gravityScale;
        if (baseGravity <= 0f)
            Debug.LogWarning("PlatformerCharacterController on '" + name + "': the Rigidbody2D's Gravity Scale is 0, so the " +
                             "character will float. Set it to about 3.", this);

        rb.constraints |= RigidbodyConstraints2D.FreezeRotation;   // a platformer character shouldn't tumble over

        groundFilter = new ContactFilter2D();
        groundFilter.SetLayerMask(groundLayers);
        groundFilter.useTriggers = false;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animatorParams = new HashSet<string>();
            foreach (AnimatorControllerParameter p in animator.parameters) animatorParams.Add(p.name);
        }

        if (footstepSource != null)
        {
            footstepSource.playOnAwake = false;
            basePitch = footstepSource.pitch;
        }
    }

    // After every Awake, so PlayerInputSource has already filled in its default keys.
    private void Start()
    {
        if (input.KeysFor(PlayerAction.Jump).Length == 0 && input.JumpButtonUnset())
        {
            Debug.LogWarning("PlatformerCharacterController on '" + name + "': this player has no Jump key or button " +
                             "(its keys for Player " + input.playerNumber + " list none). Add a Jump key under Keys By Player.", this);
        }
    }

    // Switched off by PauseManager / a menu while running: stop sideways motion instead of sliding on.
    private void OnDisable()
    {
        if (rb != null && gameObject.activeInHierarchy)
        {
            Vector2 v = Velocity;
            v.x = 0f;
            Velocity = v;
            rb.gravityScale = baseGravity;
        }
        wasStepping = false;
    }

    // ------------------------------------------------------------ main loop

    private void Update()
    {
        Vector2 v = Velocity;

        // Ground: standing on something, and not on the way up.
        grounded = CheckGround(v.y);
        if (grounded)
        {
            lastGroundedTime = Time.time;
            airJumpsLeft = airJumps;
        }

        bool locked = input.Locked;
        float moveX = 0f;

        if (!locked)
        {
            // Run: tap to switch on/off, or hold (same rule as the top-down controller, kept in PlayerInputSource).
            isSprinting = input.ResolveRun(sprintToggle, isSprinting);

            float raw = input.Move.x;
            moveX = analogMovement ? raw : (raw > 0.01f ? 1f : (raw < -0.01f ? -1f : 0f));

            if (input.JumpPressed) lastJumpPressedTime = Time.time;
        }

        // Letting go while still rising cuts the jump short. (Reads the release even if a menu just locked the player.)
        if (input.JumpReleased && v.y > 0f) v.y *= jumpCutMultiplier;

        // Jump: a recent press, and either on the ground / just left it (coyote), or an air jump to spend.
        if (!locked && Time.time - lastJumpPressedTime <= jumpBufferTime)
        {
            bool coyote = Time.time - lastGroundedTime <= coyoteTime;
            if (coyote || airJumpsLeft > 0)
            {
                if (!coyote) airJumpsLeft--;
                v.y = jumpSpeed;
                lastJumpPressedTime = float.NegativeInfinity;   // this press is used up
                lastGroundedTime = float.NegativeInfinity;      // and there is no second coyote jump
                grounded = false;
                SetTrigger("Jump");
            }
        }

        // What the physics actually did last step, for the footsteps (pushing into a wall stays silent).
        float actualX = v.x;
        float currentSpeed = isSprinting ? speed * sprintMultiplier : speed;

        v.x = moveX * currentSpeed;
        if (v.y < -maxFallSpeed) v.y = -maxFallSpeed;

        // Heavier on the way down.
        rb.gravityScale = (!grounded && v.y < -0.01f) ? baseGravity * fallGravityMultiplier : baseGravity;
        Velocity = v;

        // Facing.
        if (moveX > 0.01f) SetFacing(true);
        else if (moveX < -0.01f) SetFacing(false);

        bool moving = Mathf.Abs(moveX) > 0.01f;
        SetBool("IsMoving", moving);
        SetBool("IsGrounded", grounded);
        SetFloat("VelocityY", v.y);

        HandleFootsteps(moving && grounded && Mathf.Abs(actualX) > currentSpeed * 0.1f);
    }

    // ------------------------------------------------------------ ground check

    // A thin box just under the feet. Ignores this player's own collider and anything attached to it.
    private bool CheckGround(float verticalSpeed)
    {
        if (verticalSpeed > 0.05f) return false;   // still rising

        Bounds b = col.bounds;
        Vector2 center = new Vector2(b.center.x, b.min.y - groundCheckDistance * 0.5f);
        Vector2 size = new Vector2(b.size.x * 0.9f, groundCheckDistance);

        int count = Physics2D.OverlapBox(center, size, 0f, groundFilter, groundHits);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = groundHits[i];
            if (hit != null && hit != col && hit.attachedRigidbody != rb) return true;
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Collider2D c = col != null ? col : GetComponent<Collider2D>();
        if (c == null) return;

        Bounds b = c.bounds;
        Gizmos.color = grounded ? Color.green : Color.red;
        Gizmos.DrawWireCube(new Vector3(b.center.x, b.min.y - groundCheckDistance * 0.5f, 0f),
                            new Vector3(b.size.x * 0.9f, groundCheckDistance, 0f));
    }

    // ------------------------------------------------------------ facing

    private void SetFacing(bool right)
    {
        facingRight = right;

        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = artFacesRight ? !right : right;
        }
        else
        {
            Vector3 s = transform.localScale;
            float sign = (artFacesRight ? right : !right) ? 1f : -1f;
            s.x = Mathf.Abs(s.x) * sign;
            transform.localScale = s;
        }
    }

    // ------------------------------------------------------------ animator (only parameters that exist)

    private void SetBool(string parameter, bool value)
    {
        if (animatorParams != null && animatorParams.Contains(parameter)) animator.SetBool(parameter, value);
    }

    private void SetFloat(string parameter, float value)
    {
        if (animatorParams != null && animatorParams.Contains(parameter)) animator.SetFloat(parameter, value);
    }

    private void SetTrigger(string parameter)
    {
        if (animatorParams != null && animatorParams.Contains(parameter)) animator.SetTrigger(parameter);
    }

    // ------------------------------------------------------------ footsteps

    // Counts down while the player is really running on the ground and plays one step each time it reaches zero.
    private void HandleFootsteps(bool moving)
    {
        if (!moving)
        {
            wasStepping = false;
            return;
        }

        float interval = isSprinting ? sprintStepInterval : walkStepInterval;

        // Just started moving: the first step lands half a stride in.
        if (!wasStepping)
        {
            wasStepping = true;
            stepTimer = interval * 0.5f;
        }

        stepTimer -= Time.deltaTime;
        if (stepTimer > 0f) return;

        PlayStep();
        stepTimer = interval * (1f + Random.Range(-stepTimingVariation, stepTimingVariation));
    }

    private void PlayStep()
    {
        if (footstepSource == null || stepClips == null || stepClips.Length == 0) return;
        if (!footstepSource.isActiveAndEnabled) return;

        // Pick a clip, never the same one twice in a row (when there is more than one).
        int index;
        if (stepClips.Length == 1 || lastClipIndex < 0)
            index = Random.Range(0, stepClips.Length);
        else
            index = (lastClipIndex + 1 + Random.Range(0, stepClips.Length - 1)) % stepClips.Length;
        lastClipIndex = index;

        AudioClip clip = stepClips[index];
        if (clip == null) return;

        float variation = isSprinting ? sprintPitchVariation : walkPitchVariation;
        footstepSource.pitch = basePitch * (1f + Random.Range(-variation, variation));
        footstepSource.PlayOneShot(clip);
    }
}
