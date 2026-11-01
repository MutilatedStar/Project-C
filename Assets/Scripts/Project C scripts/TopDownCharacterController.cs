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
    /// It no longer listens for Tab / Esc / P itself. Pausing belongs to PauseManager (list this script in its
    /// "Player Input Scripts" so the player holds still), and a menu or inventory that should freeze just this
    /// player can set  GetComponent&lt;PlayerInputSource&gt;().Locked = true.
    /// </summary>
    [RequireComponent(typeof(PlayerInputSource))]
    public class TopDownCharacterController : MonoBehaviour
    {
        public float speed;
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

        private Animator animator;
        private Rigidbody2D rb;
        private PlayerInputSource input;

        private bool isSprinting;

        // Footstep state
        private float basePitch = 1f;
        private float stepTimer;
        private bool wasStepping;
        private int lastClipIndex = -1;

        private void Start()
        {
            animator = GetComponent<Animator>();
            rb = GetComponent<Rigidbody2D>();

            input = GetComponent<PlayerInputSource>();
            if (input == null)
            {
                // Prefabs made before this script needed one: add it so the player still moves.
                input = gameObject.AddComponent<PlayerInputSource>();
                Debug.LogWarning("TopDownCharacterController on '" + name + "': there was no PlayerInputSource, so one was added " +
                                 "as Player 1. For Player 2, add it yourself and set Player Number to 2.", this);
            }

            if (footstepSource != null)
            {
                footstepSource.playOnAwake = false;
                basePitch = footstepSource.pitch;   // random changes are applied around whatever pitch it has in the Inspector
            }
        }

        // Switched off by PauseManager / a menu while walking: stand still instead of sliding on.
        private void OnDisable()
        {
            if (rb != null && gameObject.activeInHierarchy) rb.linearVelocity = Vector2.zero;
            if (animator != null && animator.isActiveAndEnabled) animator.SetBool("IsMoving", false);
            wasStepping = false;
        }

        private void Update()
        {
            // Stairs menu, floor change, or a menu that locked this player: hold still.
            if (FloorController.BlocksInput || input.Locked)
            {
                rb.linearVelocity = Vector2.zero;
                animator.SetBool("IsMoving", false);
                wasStepping = false;
                return;
            }

            // Run: tap to switch on/off, or hold.
            if (sprintToggle)
            {
                if (input.SprintPressed) isSprinting = !isSprinting;
            }
            else
            {
                isSprinting = input.SprintHeld;
            }

            Vector2 move = input.Move;
            bool moving = move.sqrMagnitude > 0.0001f;
            Vector2 dir = analogMovement ? move : move.normalized;

            // Facing only changes while moving, so the player keeps looking the way they last walked.
            if (moving) animator.SetInteger("Direction", DirectionOf(move));
            animator.SetBool("IsMoving", moving);

            float currentSpeed = isSprinting ? speed * sprintMultiplier : speed;

            // Footsteps run BEFORE the velocity is set below, so rb.linearVelocity still holds what the physics
            // actually did last step. Pushing against a wall (velocity near zero) therefore stays silent.
            HandleFootsteps(moving && rb.linearVelocity.magnitude > currentSpeed * 0.1f);

            rb.linearVelocity = currentSpeed * dir;
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
