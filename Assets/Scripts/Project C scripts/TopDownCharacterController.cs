using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cainos.PixelArtTopDown_Basic
{
    /// <summary>
    /// Top-down character. Reads ALL of its input from the PlayerInputSource on the same object, so the same
    /// script drives Player 1 (keyboard left half + Controller 1) and Player 2 (arrow keys + Controller 2): just
    /// set Player Number on that component. Needs PlayerInputSource.cs, DirectButton.cs and LocalCoopManager.cs.
    ///
    /// Keys and buttons are Inspector fields on that PlayerInputSource, one set per player, so changing Player 2's keys
    /// never touches Player 1's. PlatformerCharacterController reads the same component the same way.
    ///
    /// It no longer listens for Tab / Esc / P itself. Pausing belongs to PauseManager (list this script in its
    /// "Player Input Scripts" so the player holds still), and a menu or inventory that should freeze just this
    /// player can set  GetComponent&lt;PlayerInputSource&gt;().Locked = true.
    ///
    /// PREFABS: this script finds its Rigidbody2D, Animator and PlayerInputSource on its own object, a parent or a child, so it
    /// works when the Animator lives on a sprite child of the character prefab. Anything it can't find can be dragged into the
    /// "References" fields. To give a spawned character to a player, call AssignPlayer(2) on it (or PlayerInputSource.Bind).
    /// </summary>
    [RequireComponent(typeof(PlayerInputSource))]
    public class TopDownCharacterController : MonoBehaviour
    {
        public float speed = 3f;
        public float sprintMultiplier = 1.5f;
        [Tooltip("ON: tap the run key to switch running on/off (as before). OFF: run only while the key / button is held.")]
        public bool sprintToggle = true;
        [Tooltip("ON: a gentle controller-stick tilt walks slowly. OFF: any tilt walks at full speed. Keyboard is unaffected.")]
        public bool analogMovement = true;

        [Header("Footsteps")]
        [Tooltip("The AudioSource that plays the steps. Put it on its OWN empty child object (not on the player) with a SoundSwitch on it, so the Sound On/Off setting controls it.")]
        [SerializeField] private AudioSource footstepSource;
        [Tooltip("One or more step sounds. With several, each step picks one at random and never the same one twice in a row.")]
        [SerializeField] private AudioClip[] stepClips;
        [Tooltip("Seconds between steps while walking. Tune it by ear until it matches the walk animation.")]
        [SerializeField] private float walkStepInterval = 0.45f;
        [Tooltip("Seconds between steps while sprinting.")]
        [SerializeField] private float sprintStepInterval = 0.3f;
        [Tooltip("Random pitch change per step while walking, as a fraction (0.04 = up to 4% higher or lower). Keep it small.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float walkPitchVariation = 0.04f;
        [Tooltip("Same while sprinting: a little wider than walking, but still subtle.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float sprintPitchVariation = 0.07f;
        [Tooltip("Tiny random change to the time between steps, as a fraction of the interval (0.05 = up to 5% faster or slower), so the rhythm isn't perfectly mechanical.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float stepTimingVariation = 0.05f;

        [Header("References (optional: found automatically when left empty)")]
        [Tooltip("Empty = the Animator on this object, or the first one on a child (typical when the sprite is a child of the character prefab).")]
        [SerializeField] private Animator animator;
        [Tooltip("Empty = the Rigidbody2D on this object or a parent.")]
        [SerializeField] private Rigidbody2D rb;
        [Tooltip("Empty = the PlayerInputSource on this object, a parent or a child.")]
        [SerializeField] private PlayerInputSource input;

        private bool referencesResolved;
        private bool isSprinting;

        // Footstep state
        private float basePitch = 1f;
        private float stepTimer;
        private bool wasStepping;
        private int lastClipIndex = -1;

        // Awake, not Start: every reference is ready before PauseManager or another script can switch this one off.
        private void Awake()
        {
            ResolveReferences();
            if (rb == null)
            {
                // Without a body there is nothing to move: say so once, instead of an error on every frame.
                Debug.LogError("TopDownCharacterController on '" + name + "': no Rigidbody2D on this object or a parent. " +
                               "Add one to the character prefab (or drag it into References), so this script has something to move.", this);
                enabled = false;
                return;
            }

            if (footstepSource != null)
            {
                footstepSource.playOnAwake = false;
                basePitch = footstepSource.pitch;   // random changes are applied around whatever pitch it has in the Inspector
            }
        }

        // Fills every reference that was not dragged in, looking on this object, its parents and its children. Runs from Awake,
        // and from AssignPlayer for a prefab that was instantiated inactive (its Awake has not run yet).
        private void ResolveReferences()
        {
            if (referencesResolved) return;
            referencesResolved = true;

            if (animator == null) animator = GetComponentInChildren<Animator>(true);   // includes this object first
            if (rb == null) rb = GetComponentInParent<Rigidbody2D>();                  // includes this object first

            if (input == null) input = GetComponentInParent<PlayerInputSource>();
            if (input == null) input = GetComponentInChildren<PlayerInputSource>(true);
            if (input == null)
            {
                // RequireComponent only adds it to NEW components: a prefab made before this script needed one still lacks it.
                input = gameObject.AddComponent<PlayerInputSource>();
                Debug.LogWarning("TopDownCharacterController on '" + name + "': there was no PlayerInputSource, so one was added " +
                                 "as Player 1. For Player 2, call AssignPlayer(2) or set Player Number to 2 on it.", this);
            }

            if (animator == null)
            {
                Debug.LogWarning("TopDownCharacterController on '" + name + "': no Animator found on this object or its children. " +
                                 "The character will still move, but it will not play walk animations.", this);
            }
        }

        /// <summary>
        /// For a spawner or a character-select screen: makes this character Player 1, 2, 3 or 4.
        /// Instantiate(prefab).GetComponent&lt;TopDownCharacterController&gt;().AssignPlayer(2);
        /// </summary>
        public void AssignPlayer(int playerNumber)
        {
            ResolveReferences();
            if (input != null) input.SetPlayer(playerNumber);
        }

        /// <summary>Same, and also picks the controller (Auto = Player N uses Controller N).</summary>
        public void AssignPlayer(int playerNumber, ControllerSlot slot)
        {
            ResolveReferences();
            if (input != null) input.SetPlayer(playerNumber, slot);
        }

        // The Animator is optional, so every animation call goes through these.
        private void SetMoving(bool moving)
        {
            if (animator != null && animator.isActiveAndEnabled) animator.SetBool("IsMoving", moving);
        }

        private void SetDirection(int direction)
        {
            if (animator != null && animator.isActiveAndEnabled) animator.SetInteger("Direction", direction);
        }

        // Switched off by PauseManager / a menu while walking: stand still instead of sliding on.
        private void OnDisable()
        {
            if (rb != null && gameObject.activeInHierarchy) rb.linearVelocity = Vector2.zero;
            SetMoving(false);
            wasStepping = false;
        }

        private void Update()
        {
            // Stairs menu, floor change, or a menu that locked this player: hold still.
            if (FloorController.BlocksInput || input.Locked)
            {
                rb.linearVelocity = Vector2.zero;
                SetMoving(false);
                wasStepping = false;
                return;
            }

            // Run: tap to switch on/off, or hold. The rule lives in PlayerInputSource so both movement scripts share it.
            isSprinting = input.ResolveRun(sprintToggle, isSprinting);

            Vector2 move = input.Move;
            bool moving = move.sqrMagnitude > 0.0001f;
            Vector2 dir = analogMovement ? move : move.normalized;

            // Facing only changes while moving, so the player keeps looking the way they last walked.
            if (moving) SetDirection(DirectionOf(move));
            SetMoving(moving);

            float currentSpeed = isSprinting ? speed * sprintMultiplier : speed;

            // What the physics really did last step. Read BEFORE the velocity is set below: pushing against a wall
            // (velocity near zero) must stay silent, and a stuck player is reported.
            bool reallyMoving = moving && rb.linearVelocity.magnitude > currentSpeed * 0.1f;
            WarnIfStuck(moving, rb.linearVelocity);

            // The velocity is applied FIRST, so a problem in the (purely cosmetic) footsteps can never stop the player.
            rb.linearVelocity = currentSpeed * dir;
            HandleFootsteps(reallyMoving);
        }

        // ------------------------------------------------------------ "I press a key and nothing moves"

        private int stuckFrames;
        private bool stuckReported;

        // Input is arriving but the body has not moved for about a second. A wall or the other player can do that, so the
        // message lists what can be checked, naming the settings that ARE wrong. Reported once; costs two compares per frame.
        private void WarnIfStuck(bool wantsToMove, Vector2 actualVelocity)
        {
            if (stuckReported) return;

            if (wantsToMove && actualVelocity.sqrMagnitude < 0.0001f) stuckFrames++;
            else stuckFrames = 0;
            if (stuckFrames < 60) return;

            stuckReported = true;
            System.Text.StringBuilder why = new System.Text.StringBuilder();
            if (Time.timeScale == 0f) why.Append(" Time.timeScale is 0 (something paused the game).");
            if (!rb.simulated) why.Append(" The Rigidbody2D has Simulated switched off.");
            if (rb.bodyType == RigidbodyType2D.Static) why.Append(" The Rigidbody2D Body Type is Static.");
            if ((rb.constraints & RigidbodyConstraints2D.FreezePositionX) != 0 && (rb.constraints & RigidbodyConstraints2D.FreezePositionY) != 0)
                why.Append(" The Rigidbody2D freezes position on X and Y.");
            if (why.Length == 0)
                why.Append(" The body is pressed against a collider (a wall, or the other player standing on the same spot), or another script resets its position.");

            Debug.LogWarning("TopDownCharacterController on '" + name + "' (Player " + input.playerNumber + "): the input asks for movement " +
                             "but the Rigidbody2D has not moved for about a second." + why, this);
        }

        // 0 = down, 1 = up, 2 = right, 3 = left. Up/down wins a diagonal, exactly like before.
        private static int DirectionOf(Vector2 d)
        {
            if (Mathf.Abs(d.y) >= Mathf.Abs(d.x)) return d.y > 0f ? 1 : 0;
            return d.x > 0f ? 2 : 3;
        }

        // ------------------------------------------------------------ footsteps

        // Counts down while the player is really moving and plays one step each time it reaches zero.
        private void HandleFootsteps(bool moving)
        {
            if (!moving)
            {
                wasStepping = false;
                return;
            }

            float interval = isSprinting ? sprintStepInterval : walkStepInterval;

            // Just started moving: the first step lands half a stride in, not instantly and not a full stride late.
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

            // Respects the Sound On/Off menu: the SoundSwitch on the AudioSource's object handles the source itself,
            // and the SoundOn check here keeps the steps silent even if that source is still switched on.
            if (!GameSettings.SoundOn) return;
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

            // Small random pitch change; slightly wider while sprinting.
            float variation = isSprinting ? sprintPitchVariation : walkPitchVariation;
            footstepSource.pitch = basePitch * (1f + Random.Range(-variation, variation));

            footstepSource.PlayOneShot(clip);
        }
    }
}
