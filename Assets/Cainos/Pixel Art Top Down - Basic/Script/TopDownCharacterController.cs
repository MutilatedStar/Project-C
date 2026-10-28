using System.Collections;
using System.Collections.Generic;
using UnityEngine;
 
namespace Cainos.PixelArtTopDown_Basic
{
    public class TopDownCharacterController : MonoBehaviour
    {
        public float speed;
        public float sprintMultiplier = 1.5f;
 
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
 
        private bool isSprinting;
        private bool isMenuOpen;
 
        // Footstep state
        private float basePitch = 1f;
        private float stepTimer;
        private bool wasStepping;
        private int lastClipIndex = -1;
 
        private void Start()
        {
            animator = GetComponent<Animator>();
            rb = GetComponent<Rigidbody2D>();
 
            if (footstepSource != null)
            {
                footstepSource.playOnAwake = false;
                basePitch = footstepSource.pitch;   // random changes are applied around whatever pitch it has in the Inspector
            }
        }
 
 
        private void Update()
        {
            // Stairs menu or floor change: hold still. This sits BEFORE the menu keys below so the Esc that closes
            // the stairs menu (or Tab/P pressed meanwhile) can't flip isMenuOpen.
            if (FloorController.BlocksInput)
            {
                rb.linearVelocity = Vector2.zero;
                animator.SetBool("IsMoving", false);
                wasStepping = false;
                return;
            }

            // Menu keys flip the "menu is up" state
            if (Input.GetKeyDown(KeyCode.Tab) ||
                Input.GetKeyDown(KeyCode.Escape) ||
                Input.GetKeyDown(KeyCode.P))
            {
                isMenuOpen = !isMenuOpen;
            }
 
            // Menu is up: hold the player still and ignore all movement/sprint input.
            // Velocity has to be zeroed here, otherwise the Rigidbody keeps sliding
            // at whatever it was doing on the last frame.
            if (isMenuOpen)
            {
                rb.linearVelocity = Vector2.zero;
                animator.SetBool("IsMoving", false);
                wasStepping = false;
                return;
            }
 
            // Tap Shift to toggle sprint on/off
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                isSprinting = !isSprinting;
            }
 
            Vector2 dir = Vector2.zero;
            if (Input.GetKey(KeyCode.A))
            {
                dir.x = -1;
                animator.SetInteger("Direction", 3);
            }
            else if (Input.GetKey(KeyCode.D))
            {
                dir.x = 1;
                animator.SetInteger("Direction", 2);
            }
 
            if (Input.GetKey(KeyCode.W))
            {
                dir.y = 1;
                animator.SetInteger("Direction", 1);
            }
            else if (Input.GetKey(KeyCode.S))
            {
                dir.y = -1;
                animator.SetInteger("Direction", 0);
            }
 
            dir.Normalize();
            animator.SetBool("IsMoving", dir.magnitude > 0);
 
            float currentSpeed = isSprinting ? speed * sprintMultiplier : speed;
 
            // Footsteps run BEFORE the velocity is set below, so rb.linearVelocity still holds what the physics
            // actually did last step. Pushing against a wall (velocity near zero) therefore stays silent.
            HandleFootsteps(dir.sqrMagnitude > 0f && rb.linearVelocity.magnitude > currentSpeed * 0.1f);
 
            rb.linearVelocity = currentSpeed * dir;
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