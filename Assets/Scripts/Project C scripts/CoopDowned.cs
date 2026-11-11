using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Ilumisoft.HealthSystem;
using Cainos.PixelArtTopDown_Basic;

/// <summary>
/// What happens to a player at 0 health: instead of vanishing, they are KNOCKED DOWN, and the team gets a chance to save them.
///
///   KNOCKED DOWN   the player falls over, turns gray, stops animating and loses their glow. Every input is switched off (the
///                  Attack button is the one exception, see below), and nothing can hurt them or collide with them.
///   REVIVED        another player (alive, in the game) stands over them and HOLDS Attack: a bar fills over Revive Seconds
///                  (each extra tap of Attack adds a bonus, so mashing is faster than holding). The player gets up where they fell, with
///                  Revive Health Percent of their health, their colours and glow back, and a short safe moment (Grace Seconds).
///   BLOWN UP       a downed player can ALWAYS give up: they press Attack (after Blow Up Delay) and burst into little pieces of their
///                  own picture, even if the other player is alive. That is the way out when they fell somewhere nobody can reach. The
///                  press is ignored only while the other player is actively holding Attack to revive them. Their score is reset
///                  (Reset Score On Blow Up).
///   RESPAWN        Respawn Delay seconds later they come back at full health (Respawn Health Percent):
///                    room 0            at their point of origin (where they stood when the game started)
///                    any other room    at a CoopSpawnPoint for that room if you placed one, otherwise where they first arrived in it
///                  "Room" is the floor number FloorController reports. Anything else that moves players between rooms can call
///                  CoopDowned.EnterRoom(number).
///   EVERYONE DOWN  when every player that counts is at 0 health, nobody is left to revive anyone: whoever is still lying there blows up
///                  at once (Blow Up When Team Wiped), nobody respawns, and CoopHealth.TeamWiped is raised so LightsOutScreen (the game-over
///                  screen with Retry / Level Select / Menu) comes up. LightsOutScreen's Retry calls CoopDowned.ResetAllForRetry().
///   CAMERA         SplitScreenCamera lets go of a downed or blown-up player (their half follows the other player, or holds still if
///                  both are down) and takes them back by itself when their health is back: after a revive, or after a respawn (which also
///                  snaps the cameras onto them, see Snap Camera On Respawn).
///   SOUND          Fall Clip plays when a player is knocked down, Blow Up Clip when they burst, Revive Clip when they get up.
///
/// SET UP: put this on every player prefab (Player 1, Player 2, or the one shared prefab), next to CoopHealth. Nothing else to wire up:
/// the sprite, Animator, Rigidbody2D, PlayerGlow and TopDownJump are found on the player by themselves. Needs the updated CoopHealth,
/// PlayerInputSource and SplitScreenCamera (small additions: Grace / RaiseTeamWiped, AttackPressedEvenIfLocked,
/// SnapToPlayers) and PlayerHealthAccess.Heal (already used by SwordController).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(CoopHealth))]
public class CoopDowned : MonoBehaviour
{
    public enum DownState { Up, Down, BlownUp }

    [Header("Knocked down")]
    [Tooltip("The picture that is tinted and tipped over. Leave EMPTY to use the player's own SpriteRenderer (the same one PlayerGlow uses).")]
    [SerializeField] private SpriteRenderer sprite;
    [Tooltip("How far the player tips over, in degrees. 90 = lying on their side. 0 = no tipping, they just turn gray. If tipping looks wrong on your prefab (the sprite is on the root object and a shadow turns with it), set 0.")]
    [SerializeField, Range(0f, 90f)] private float fallAngle = 90f;
    [Tooltip("Seconds the fall takes.")]
    [SerializeField, Min(0.01f)] private float fallTime = 0.25f;
    [Tooltip("The colour a downed player is washed with.")]
    [SerializeField] private Color downedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    [Tooltip("How much of that gray covers the picture. 1 = fully gray.")]
    [SerializeField, Range(0f, 1f)] private float grayStrength = 0.9f;

    [Header("Reviving")]
    [Tooltip("How close (world units) the other player must stand to the downed player to revive them.")]
    [SerializeField, Min(0.1f)] private float reviveRadius = 1.0f;
    [Tooltip("Seconds of holding Attack next to them to bring them back.")]
    [SerializeField, Min(0.1f)] private float reviveSeconds = 2.5f;
    [Tooltip("Each fresh tap of Attack from the reviver adds this many seconds of progress, so mashing is faster than holding.")]
    [SerializeField, Min(0f)] private float tapBonusSeconds = 0.3f;
    [Tooltip("Seconds of progress lost per second when nobody is reviving.")]
    [SerializeField, Min(0f)] private float progressDrain = 0.5f;
    [Tooltip("Health a revived player gets back, as a fraction of their maximum.")]
    [SerializeField, Range(0.05f, 1f)] private float reviveHealthPercent = 0.5f;
    [Tooltip("Seconds nothing can hurt the player after a revive or a respawn.")]
    [SerializeField, Min(0f)] private float graceSeconds = 1.5f;

    [Header("Blowing up and respawning")]
    [Tooltip("Seconds after the fall before the downed player's Attack press counts, so the swing that was going on when they were hit can't blow them up by accident. " +
             "A downed player can always blow up with Attack after this, even when the other player is alive (they just can't while that player is reviving them).")]
    [SerializeField, Min(0f)] private float blowUpDelay = 0.75f;
    [Tooltip("ON (recommended): when every player that counts is at 0 health, whoever is still lying down blows up straight away and the game-over screen " +
             "(LightsOutScreen) comes up. OFF: they stay lying there until the screen takes over.")]
    [SerializeField] private bool blowUpWhenTeamWiped = true;
    [Tooltip("Seconds between blowing up and coming back.")]
    [SerializeField, Min(0.1f)] private float respawnDelay = 1.5f;
    [Tooltip("Health a respawned player has, as a fraction of their maximum.")]
    [SerializeField, Range(0.05f, 1f)] private float respawnHealthPercent = 1f;
    [Tooltip("ON: blowing up sets this player's score back to 0 (with Shared Score on, that is the team total).")]
    [SerializeField] private bool resetScoreOnBlowUp = true;
    [Tooltip("ON: both cameras jump straight onto the players when someone respawns, instead of gliding across the level.")]
    [SerializeField] private bool snapCameraOnRespawn = true;
    [Tooltip("Seconds to wait after the players arrive in a new room before remembering where they stand as that room's respawn spot. Raise it if your doors move the players late.")]
    [SerializeField, Min(0f)] private float roomEntryDelay = 0.25f;

    [Header("The burst")]
    [Tooltip("The player's picture is cut into Columns x Rows pieces that fly apart.")]
    [SerializeField, Range(1, 8)] private int pieceColumns = 4;
    [SerializeField, Range(1, 8)] private int pieceRows = 4;
    [Tooltip("How fast the pieces fly out, in world units per second.")]
    [SerializeField, Min(0f)] private float burstSpeed = 4.5f;
    [Tooltip("How fast the pieces spin, in degrees per second (each one a random amount up to this).")]
    [SerializeField, Min(0f)] private float burstSpin = 540f;
    [Tooltip("How quickly the pieces slow down. Higher = they stop sooner.")]
    [SerializeField, Min(0f)] private float burstDrag = 3f;
    [Tooltip("Seconds until the pieces have faded away.")]
    [SerializeField, Min(0.1f)] private float pieceLife = 1.1f;
    [Tooltip("Optional. A particle puff (or anything) created where the player blows up. Destroyed after Effect Life seconds.")]
    [SerializeField] private GameObject blowUpEffect;
    [SerializeField, Min(0.1f)] private float effectLife = 3f;

    [Header("Sound (optional, follows the Sound On/Off setting)")]
    [Tooltip("Plays the moment a player is knocked down (the fall).")]
    [SerializeField] private AudioClip fallClip;
    [SerializeField] private AudioClip blowUpClip;
    [SerializeField] private AudioClip reviveClip;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.8f;

    [Header("Revive bar (above the downed player)")]
    [SerializeField] private bool showReviveBar = true;
    [SerializeField] private Vector2 barOffset = new Vector2(0f, 1.1f);
    [SerializeField, Min(0.1f)] private float barWidth = 1.2f;
    [SerializeField, Min(0.02f)] private float barHeight = 0.16f;
    [SerializeField] private Color barBackColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
    [SerializeField] private Color barFillColor = new Color(0.35f, 0.9f, 0.4f, 1f);

    // ------------------------------------------------------------ public

    /// <summary>Every CoopDowned that is switched on right now.</summary>
    public static readonly List<CoopDowned> All = new List<CoopDowned>();

    public DownState State { get { return state; } }

    /// <summary>True while knocked down and waiting to be revived (or to blow up).</summary>
    public bool IsDown { get { return state == DownState.Down; } }

    /// <summary>Up, Down (knocked down, can be revived) or BlownUp (gave up, waiting to respawn). Read by CoopDebugOverlay.</summary>
    public DownState CurrentState { get { return state; } }

    /// <summary>True from the blow-up until the respawn.</summary>
    public bool IsBlownUp { get { return state == DownState.BlownUp; } }

    /// <summary>True while knocked down or blown up: this player is out of the action.</summary>
    public bool IsOut { get { return state != DownState.Up; } }

    /// <summary>How far the revive is, 0 to 1.</summary>
    public float ReviveProgress { get { return Mathf.Clamp01(progress / reviveSeconds); } }

    /// <summary>The room (floor) this player is in. 0 until FloorController, or EnterRoom, says otherwise.</summary>
    public int Room { get { return room; } }

    /// <summary>The CoopDowned of this player number, or null.</summary>
    public static CoopDowned Get(int playerNumber)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].health != null && All[i].health.PlayerNumber == playerNumber) return All[i];
        }
        return null;
    }

    /// <summary>
    /// Tell every player which room they are in now. FloorController's FloorChanged already does this by itself; call it yourself only
    /// for a room change that FloorController doesn't know about.
    /// </summary>
    public static void EnterRoom(int roomNumber)
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null) All[i].HandleRoomChanged(roomNumber);
        }
    }

    /// <summary>Change the point of origin: where this player comes back in room 0.</summary>
    public void SetOrigin(Vector3 position)
    {
        origin = position;
        originSet = true;
    }

    /// <summary>Bring a downed player back now, as if they had been revived.</summary>
    public void ReviveNow()
    {
        if (state == DownState.Down) Revive();
    }

    /// <summary>Blow a downed player up now, as if they had pressed Attack.</summary>
    public void BlowUpNow()
    {
        if (state == DownState.Down) BlowUp();
    }

    // ------------------------------------------------------------ state

    private CoopHealth health;
    private HealthComponent hc;
    private PlayerInputSource input;
    private Animator animator;
    private Rigidbody2D rb;
    private PlayerGlow glow;
    private TopDownJump jump;
    private Transform visual;

    private DownState state = DownState.Up;
    private float downTime;
    private float progress;
    private bool reviverNear;
    private float respawnLeft;
    private float graceLeft;
    private float knownMax = 1f;
    private bool everAlive;

    // what to put back
    private Color baseColor = Color.white;
    private Quaternion startRotation = Quaternion.identity;
    private float animatorSpeed = 1f;
    private bool animatorFrozen;
    private bool glowWasOn;
    private bool bodyWasSimulated = true;
    private float fallT;
    private float fallSign = -1f;
    private readonly List<Renderer> hidden = new List<Renderer>();

    // rooms
    private int room;
    private Vector3 origin;
    private bool originSet;
    private Vector3 roomEntry;
    private bool roomEntrySet;

    private bool healWarned;
    private static bool scoreWarned;
    private static bool wipeRaised;   // TeamWiped was already raised for the team being all down (cleared when anybody gets up)
    private static bool noScreenWarned;
    private static float strayStart = -1f;   // when everyone with a CoopDowned was first seen down while the game was not over
    private static bool strayWarned;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        // A CoopDowned on a CHILD of a player is part of that player, not a player of its own. Switch it off before it asks the PlayerInputHub
        // for a slot (which would hand it the next free one, "Player 3", and make the doors and the game-over screen wait for it).
        if (CoopHealth.IsNestedInPlayer(this))
        {
            Debug.LogWarning("CoopDowned on '" + name + "' sits inside another player, so it is switched off and does nothing. " +
                             "You can remove it from '" + name + "'.", this);
            enabled = false;
            return;
        }

        health = GetComponent<CoopHealth>();
        hc = GetComponent<HealthComponent>();
        if (hc == null) hc = GetComponentInParent<HealthComponent>();

        input = PlayerInputSource.For(gameObject);
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        jump = GetComponent<TopDownJump>();

        glow = GetComponent<PlayerGlow>();
        if (glow == null) glow = GetComponentInChildren<PlayerGlow>();

        if (sprite == null) sprite = GetComponent<SpriteRenderer>();
        if (sprite == null) sprite = GetComponentInChildren<SpriteRenderer>();
        visual = sprite != null ? sprite.transform : transform;

        if (sprite == null)
        {
            Debug.LogWarning("CoopDowned on '" + name + "': it found no SpriteRenderer, so the player can't turn gray, tip over or burst into pieces " +
                             "(everything else still works). Drag the player's sprite into Sprite.", this);
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        FloorController.FloorChanged += HandleFloorChanged;
    }

    private void OnDisable()
    {
        All.Remove(this);
        FloorController.FloorChanged -= HandleFloorChanged;

        // Switched off while knocked down (Player 2 removed, a menu...): leave nothing gray, tipped over, hidden or locked behind.
        if (state != DownState.Up) PutBack(false, Vector3.zero);
        if (health != null) health.Grace = false;

        DestroyBar();
        DestroyPieces();
    }

    private void OnDestroy()
    {
        DestroyBar();
        DestroyPieces();
    }

    // The point of origin is where the player stands after the first frame, once every other script has placed them.
    private IEnumerator Start()
    {
        yield return null;
        if (!originSet && state == DownState.Up)
        {
            origin = transform.position;
            originSet = true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
        scoreWarned = false;
        wipeRaised = false;
        noScreenWarned = false;
        strayStart = -1f;
        strayWarned = false;
    }

    // ------------------------------------------------------------ every frame

    private void Update()
    {
        float dt = Time.deltaTime;

        if (state == DownState.Up)
        {
            if (health.IsAlive)
            {
                everAlive = true;
                if (hc != null) knownMax = Mathf.Max(knownMax, hc.CurrentHealth);

                if (health.Grace)
                {
                    graceLeft -= dt;
                    if (graceLeft <= 0f) health.Grace = false;
                }
                return;
            }

            // At 0 health. Only a player who has been alive can be knocked down (not one whose health hasn't set itself up yet).
            // A player still in the air lands first, so they never fall over in mid-jump.
            if (!everAlive) return;
            if (jump != null && jump.IsAirborne) return;
            BeginDown();
            return;
        }

        // Paused: everything holds still, and a click that closes a menu must not count as a press.
        if (Time.timeScale <= 0f) return;

        CheckTeamWipe();

        if (state == DownState.Down) UpdateDown(dt);
        else UpdateBlownUp(dt);
    }

    // Safety net: everybody that counts is at 0 health, so the game must end. CoopHealth raises TeamWiped by itself when the last player
    // dies; this makes sure it is raised (once) even if that moment was missed, so the game-over screen can never fail to come up.
    private void CheckTeamWipe()
    {
        if (!CoopHealth.AllCountedDead)
        {
            wipeRaised = false;
            WatchForStrayPlayer();
            return;
        }

        strayStart = -1f;
        if (wipeRaised) return;
        wipeRaised = true;

        if (!LightsOutScreen.Available && !noScreenWarned)
        {
            noScreenWarned = true;
            Debug.LogWarning("CoopDowned: every player is down, but there is no LightsOutScreen switched on in this scene, so there is no game-over screen " +
                             "to show (the team respawns instead). Put the LightsOutScreen component on an ACTIVE object in this scene.", this);
        }
        CoopHealth.RaiseTeamWiped();
    }

    // Everybody who can be knocked down is down, yet the team does not count as wiped: somebody that counts is still alive (or something raised
    // TeamWipeBlockers). After a few seconds, say who, so a stray extra player object or a blocker is easy to spot in the Console.
    private static void WatchForStrayPlayer()
    {
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].state == DownState.Up)
            {
                strayStart = -1f;
                strayWarned = false;
                return;
            }
        }

        if (strayStart < 0f) strayStart = Time.unscaledTime;
        if (strayWarned || Time.unscaledTime - strayStart < 3f) return;
        strayWarned = true;

        var sb = new System.Text.StringBuilder();
        sb.Append("CoopDowned: every player with a CoopDowned is down, but the game is not over, so no game-over screen comes up. ");
        if (CoopHealth.TeamWipeBlockers > 0) sb.Append("CoopHealth.TeamWipeBlockers is " + CoopHealth.TeamWipeBlockers + " (something raised it and never lowered it). ");
        sb.Append("Every CoopHealth in the scene right now: ");

        for (int i = 0; i < CoopHealth.Players.Count; i++)
        {
            CoopHealth p = CoopHealth.Players[i];
            if (p == null) continue;
            sb.Append("[" + p.name + ", Player " + p.PlayerNumber + ", health " + p.CurrentHealth +
                      (CoopRules.Counts(p.PlayerNumber) ? ", counts" : ", does not count") + "] ");
        }
        sb.Append("Any player that counts and still has health keeps the game going.");
        Debug.LogWarning(sb.ToString());
    }

    private void UpdateDown(float dt)
    {
        downTime += dt;
        input.Locked = true;   // a menu that unlocked this player must not give them control back

        // ---- everybody is down: nobody is left to revive anyone, so they all burst. This comes BEFORE the BlocksInput check below,
        // because the game-over screen blocks input from the very moment the team is wiped.
        if (blowUpWhenTeamWiped && CoopHealth.AllCountedDead)
        {
            BlowUp();
            return;
        }

        if (FloorController.BlocksInput) return;

        // ---- someone standing over them, holding Attack
        CoopHealth reviver = FindReviver();
        reviverNear = reviver != null;

        PlayerInputSource reviverInput = reviver != null ? PlayerInputSource.Get(reviver.PlayerNumber) : null;
        bool holding = reviverInput != null && reviverInput.AttackHeld;

        if (holding)
        {
            progress += dt;
            if (reviverInput.AttackPressed) progress += tapBonusSeconds;
        }
        else if (progress > 0f)
        {
            progress = Mathf.Max(0f, progress - progressDrain * dt);
        }

        if (progress >= reviveSeconds)
        {
            Revive();
            return;
        }

        // ---- Attack gives up: the downed player bursts and comes back at their spawn spot. Always allowed, even with the other player
        // alive (they may have fallen somewhere nobody can reach). The one exception: while the other player is actively holding Attack
        // to revive them, so a stray press can't undo a rescue that is in progress.
        bool mayBlowUp = downTime >= blowUpDelay && !holding;
        if (mayBlowUp && input.AttackPressedEvenIfLocked) BlowUp();
    }

    private void UpdateBlownUp(float dt)
    {
        input.Locked = true;

        // Everybody is out: the game-over screen decides what happens next, so nobody comes back by themselves behind it.
        // (A scene with no LightsOutScreen has nothing to take over, so there the team respawns as before.)
        if (LightsOutScreen.Available && CoopHealth.AllCountedDead) return;

        respawnLeft -= dt;
        if (respawnLeft <= 0f) Respawn();
    }

    // Runs after every other script, so nothing (an animation, PlayerEffects' pulse) can paint over the downed look.
    private void LateUpdate()
    {
        if (state == DownState.Up) return;

        if (state == DownState.BlownUp)
        {
            KeepBodyHidden();
            return;
        }

        fallT = Mathf.Min(1f, fallT + Time.deltaTime / fallTime);
        float eased = 1f - (1f - fallT) * (1f - fallT);

        if (fallAngle > 0f && visual != null)
            visual.localRotation = Quaternion.Euler(0f, 0f, fallSign * fallAngle * eased) * startRotation;

        if (sprite != null)
        {
            Color gray = Color.Lerp(baseColor, downedColor, grayStrength);
            gray.a = baseColor.a;
            sprite.color = gray;
        }

        // The animation is stopped once the player has settled, so it can finish going to its idle picture while they fall.
        if (!animatorFrozen && fallT >= 1f && animator != null)
        {
            animator.speed = 0f;
            animatorFrozen = true;
        }

        UpdateBar();
    }

    // ------------------------------------------------------------ knocked down

    private void BeginDown()
    {
        state = DownState.Down;
        downTime = 0f;
        progress = 0f;
        fallT = 0f;
        reviverNear = false;
        animatorFrozen = false;

        health.Grace = false;
        input.Locked = true;

        // The camera is NOT kept on them: SplitScreenCamera lets go of a player at 0 health (their half follows the other player), and
        // picks them up again by itself the moment their health is back (see Revive and Respawn).
        PlaySound(fallClip, transform.position);

        // Nothing can touch a downed player, and they can't block the one who comes to revive them.
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            bodyWasSimulated = rb.simulated;
            rb.simulated = false;
        }

        // The picture's own colour, without PlayerGlow's tint (read BEFORE the glow is switched off).
        baseColor = glow != null ? glow.BaseColor : (sprite != null ? sprite.color : Color.white);
        glowWasOn = glow != null && glow.enabled;
        if (glow != null) glow.enabled = false;   // its OnDisable puts the plain colour back and hides the halo

        startRotation = visual != null ? visual.localRotation : Quaternion.identity;

        // Fall towards the side they were facing (Direction 3 = left), otherwise to the right.
        fallSign = -1f;
        if (animator != null)
        {
            animatorSpeed = animator.speed;
            if (HasParameter(animator, "Direction") && animator.GetInteger("Direction") == 3) fallSign = 1f;
        }
    }

    private void Revive()
    {
        if (!Restore(MaxHealth() * reviveHealthPercent))
        {
            progress = 0f;
            return;
        }

        PlaySound(reviveClip, transform.position);
        PutBack(false, Vector3.zero);
        StartGrace();
    }

    // ------------------------------------------------------------ blown up

    private void BlowUp()
    {
        state = DownState.BlownUp;
        respawnLeft = respawnDelay;
        progress = 0f;
        reviverNear = false;
        HideBar();

        Vector3 centre = sprite != null ? sprite.bounds.center : transform.position;

        SpawnPieces();
        if (blowUpEffect != null)
        {
            GameObject fx = Instantiate(blowUpEffect, centre, Quaternion.identity);
            Destroy(fx, effectLife);
        }
        PlaySound(blowUpClip, centre);

        HideBody();
        if (resetScoreOnBlowUp) ResetScore(health.PlayerNumber);
    }

    private void Respawn()
    {
        if (!Restore(MaxHealth() * respawnHealthPercent))
        {
            respawnLeft = 2f;   // try again shortly; the problem has been reported once
            return;
        }

        Vector3 spot = RespawnPosition();
        PutBack(true, spot);
        StartGrace();

        // The camera has been following the body this whole time; now that the body has jumped to the spawn spot, it jumps too.
        if (snapCameraOnRespawn && SplitScreenCamera.Current != null) SplitScreenCamera.Current.SnapToPlayers();
    }

    // Room 0 -> the point of origin. Any other room -> a CoopSpawnPoint placed there, else where they first arrived, else the origin.
    private Vector3 RespawnPosition()
    {
        // No origin recorded yet (knocked down in the very first frame): the only honest place left is where they are.
        if (!originSet)
        {
            origin = transform.position;
            originSet = true;
        }

        Vector3 spot;
        if (room != 0 && CoopSpawnPoint.TryFind(room, health.PlayerNumber, out spot)) { }
        else if (room != 0 && roomEntrySet) spot = roomEntry;
        else spot = origin;

        spot.z = transform.position.z;
        return spot;
    }

    // ------------------------------------------------------------ getting back up (revive, respawn, or being switched off)

    // Undoes everything BeginDown / BlowUp did. teleport = move to spot first, while the body is still switched off.
    private void PutBack(bool teleport, Vector3 spot)
    {
        if (visual != null) visual.localRotation = startRotation;
        if (sprite != null) sprite.color = baseColor;
        if (animator != null) animator.speed = animatorSpeed;
        animatorFrozen = false;

        ShowBody();

        if (teleport) transform.position = spot;
        if (rb != null)
        {
            rb.simulated = bodyWasSimulated;
            if (teleport) rb.position = spot;
            rb.linearVelocity = Vector2.zero;
        }

        if (glow != null && glowWasOn) glow.enabled = true;   // fades back in, like at the start of the game

        if (input != null) input.Locked = false;

        HideBar();
        progress = 0f;
        reviverNear = false;
        wipeRaised = false;   // somebody is up again, so the next time everyone is down counts as a new wipe
        state = DownState.Up;
    }

    /// <summary>
    /// Everybody who is knocked down or blown up gets back on their feet right where they are (no respawn spot, no safe moment).
    /// LightsOutScreen's Retry calls this behind its black screen, after it has refilled every player's health and before it moves
    /// them to the floor's start. Players who are already up are left alone.
    /// </summary>
    public static void ResetAllForRetry()
    {
        wipeRaised = false;
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null) All[i].ForceUp();
        }
    }

    private void ForceUp()
    {
        if (state == DownState.Up) return;

        DestroyPieces();   // a burst that was still flying is gone
        PutBack(false, Vector3.zero);
        health.Grace = false;
    }

    private void StartGrace()
    {
        if (graceSeconds <= 0f) return;
        health.Grace = true;
        graceLeft = graceSeconds;
    }

    // ------------------------------------------------------------ who can save them

    // The closest player that is alive, in the game, and standing within Revive Radius.
    private CoopHealth FindReviver()
    {
        float best = float.MaxValue;
        float limit = reviveRadius * reviveRadius;
        CoopHealth found = null;

        for (int i = 0; i < CoopHealth.Players.Count; i++)
        {
            CoopHealth p = CoopHealth.Players[i];
            if (p == null || p == health || !p.IsAlive || !CoopRules.Counts(p.PlayerNumber)) continue;

            Vector2 offset = p.transform.position - transform.position;
            float d = offset.sqrMagnitude;
            if (d > limit || d >= best) continue;

            best = d;
            found = p;
        }
        return found;
    }

    // ------------------------------------------------------------ rooms

    private void HandleFloorChanged(int floor)
    {
        HandleRoomChanged(floor);
    }

    private void HandleRoomChanged(int roomNumber)
    {
        room = roomNumber;
        roomEntrySet = false;
        if (isActiveAndEnabled) StartCoroutine(RememberEntry(roomNumber));
    }

    // Where this player stands a moment after arriving is the spot they come back to in that room.
    private IEnumerator RememberEntry(int roomNumber)
    {
        yield return new WaitForSecondsRealtime(roomEntryDelay);
        if (room == roomNumber && state == DownState.Up && roomNumber != 0)
        {
            roomEntry = transform.position;
            roomEntrySet = true;
        }
    }

    // ------------------------------------------------------------ health (the parts of the health scripts this relies on)

    private float MaxHealth()
    {
        System.Type type = health.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        object value = null;
        PropertyInfo property = type.GetProperty("MaxHealth", flags);
        if (property != null) value = property.GetValue(health, null);
        else
        {
            FieldInfo field = type.GetField("MaxHealth", flags);
            if (field != null) value = field.GetValue(health);
        }

        if (value is float) return Mathf.Max(1f, (float)value);
        if (value is int) return Mathf.Max(1f, (int)value);
        return Mathf.Max(1f, knownMax);   // no MaxHealth found: the most health this player has had
    }

    // Gives a player at 0 health this much health again. True if they are alive afterwards.
    private bool Restore(float amount)
    {
        if (hc != null) PlayerHealthAccess.Heal(hc, amount);
        if (health.IsAlive) return true;

        // PlayerHealthAccess.Heal would not add health to a player at 0: ask the health component directly.
        System.Type type = health.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        string[] names = { "SetHealth", "AddHealth", "SetCurrentHealth" };

        for (int i = 0; i < names.Length && !health.IsAlive; i++)
        {
            MethodInfo method = type.GetMethod(names[i], flags, null, new[] { typeof(float) }, null);
            if (method != null) method.Invoke(health, new object[] { amount });
        }

        if (health.IsAlive) return true;

        if (!healWarned)
        {
            healWarned = true;
            Debug.LogError("CoopDowned on '" + name + "': it could not give this player their health back (still " + (hc != null ? hc.CurrentHealth.ToString("0.#") : "?") +
                           " after PlayerHealthAccess.Heal and the health component's SetHealth / AddHealth). The player stays down. " +
                           "Fix: make PlayerHealthAccess.Heal able to add health to a player at 0, or change Restore() in CoopDowned to call your health script's own method.", this);
        }
        return false;
    }

    // ------------------------------------------------------------ score

    // CoopScore (CoopRules.cs) was not available when this was written, so the reset looks for a method that clears one player's score
    // by its usual names. If none fits, it says so once. If you know the method, replace the body with a direct call.
    private static void ResetScore(int playerNumber)
    {
        System.Type type = typeof(CoopScore);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        string[] clearNames = { "Reset", "ResetPlayer", "ResetScore", "Clear", "ClearPlayer", "Zero" };
        for (int i = 0; i < clearNames.Length; i++)
        {
            MethodInfo m = type.GetMethod(clearNames[i], flags, null, new[] { typeof(int) }, null);
            if (m != null) { m.Invoke(null, new object[] { playerNumber }); return; }
        }

        string[] setNames = { "Set", "SetScore" };
        for (int i = 0; i < setNames.Length; i++)
        {
            MethodInfo m = type.GetMethod(setNames[i], flags, null, new[] { typeof(int), typeof(int) }, null);
            if (m != null) { m.Invoke(null, new object[] { playerNumber, 0 }); return; }
        }

        string[] addNames = { "Add", "AddScore", "AddPoints" };
        for (int i = 0; i < addNames.Length; i++)
        {
            MethodInfo m = type.GetMethod(addNames[i], flags, null, new[] { typeof(int), typeof(int) }, null);
            if (m != null) { m.Invoke(null, new object[] { playerNumber, -CoopScore.Of(playerNumber) }); return; }
        }

        if (!scoreWarned)
        {
            scoreWarned = true;
            Debug.LogWarning("CoopDowned: it could not find a way to reset the score in CoopScore (it looked for Reset / Set / Add with a player number). " +
                             "The score is NOT reset. Fix: call your own reset in ResetScore() in CoopDowned.cs.");
        }
    }

    // ------------------------------------------------------------ hiding the body while blown up

    // Every renderer on the player (the picture, the jump shadow...) is switched off with forceRenderingOff, which other scripts
    // don't touch the way they touch "enabled". The colliders are left alone. Renderers that were already forced off stay as they were.
    private void HideBody()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            if (!hidden.Contains(r)) hidden.Add(r);
        }
    }

    // Some scripts switch renderers back on by themselves (the jump's shadow), so the ones hidden above are held off every frame.
    private void KeepBodyHidden()
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            if (hidden[i] != null) hidden[i].forceRenderingOff = true;
        }
    }

    private void ShowBody()
    {
        for (int i = 0; i < hidden.Count; i++)
        {
            if (hidden[i] != null) hidden[i].forceRenderingOff = false;
        }
        hidden.Clear();
    }

    // ------------------------------------------------------------ the burst

    private sealed class Piece
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public Vector2 velocity;
        public float spin;
        public Vector3 scale;
    }

    private readonly List<Piece> pieces = new List<Piece>();
    private Coroutine pieceRoutine;

    // The player's current picture, cut into a grid. Each piece starts exactly where it was, then flies outward.
    private void SpawnPieces()
    {
        if (sprite == null || sprite.sprite == null) return;

        Sprite source = sprite.sprite;
        Texture2D texture = source.texture;
        Rect area = source.textureRect;
        float ppu = source.pixelsPerUnit;

        float w = area.width / pieceColumns;
        float h = area.height / pieceRows;

        Vector3 scale = sprite.transform.lossyScale;
        Color tint = sprite.color;

        // Pieces made by an earlier blow-up are gone before new ones appear.
        DestroyPieces();

        for (int row = 0; row < pieceRows; row++)
        {
            for (int col = 0; col < pieceColumns; col++)
            {
                Rect part = new Rect(area.x + col * w, area.y + row * h, w, h);
                Sprite slice = Sprite.Create(texture, part, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);

                // Where this piece's centre sits relative to the sprite's pivot, in the sprite's own units.
                Vector2 local = (new Vector2(col * w + w * 0.5f, row * h + h * 0.5f) - source.pivot) / ppu;
                if (sprite.flipX) local.x = -local.x;
                if (sprite.flipY) local.y = -local.y;

                GameObject go = new GameObject("Piece");
                go.layer = sprite.gameObject.layer;   // the same camera layer, so split-screen cameras draw it too
                go.transform.position = sprite.transform.TransformPoint(local);
                go.transform.rotation = sprite.transform.rotation;
                go.transform.localScale = scale;

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = slice;
                sr.flipX = sprite.flipX;
                sr.flipY = sprite.flipY;
                sr.color = tint;
                sr.sharedMaterial = sprite.sharedMaterial;
                sr.sortingLayerID = sprite.sortingLayerID;
                sr.sortingOrder = sprite.sortingOrder + 2;

                Vector2 outward = local.sqrMagnitude > 0.0001f ? local.normalized : Random.insideUnitCircle.normalized;
                Vector2 direction = (outward + Random.insideUnitCircle * 0.6f).normalized;

                Piece piece = new Piece();
                piece.transform = go.transform;
                piece.renderer = sr;
                piece.velocity = direction * burstSpeed * Random.Range(0.6f, 1.2f);
                piece.spin = Random.Range(-burstSpin, burstSpin);
                piece.scale = scale;
                pieces.Add(piece);
            }
        }

        pieceRoutine = StartCoroutine(FlyPieces());
    }

    private IEnumerator FlyPieces()
    {
        float age = 0f;

        while (age < pieceLife)
        {
            float dt = Time.deltaTime;
            age += dt;

            float k = Mathf.Clamp01(age / pieceLife);
            float alpha = 1f - Mathf.Clamp01((k - 0.45f) / 0.55f);
            float drag = Mathf.Exp(-burstDrag * dt);

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                if (p.transform == null) continue;

                p.velocity *= drag;
                p.transform.position += (Vector3)(p.velocity * dt);
                p.transform.Rotate(0f, 0f, p.spin * dt);
                p.transform.localScale = p.scale * Mathf.Lerp(1f, 0.6f, k);

                Color c = p.renderer.color;
                c.a = alpha;
                p.renderer.color = c;
            }
            yield return null;
        }

        DestroyPieces();
    }

    private void DestroyPieces()
    {
        if (pieceRoutine != null)
        {
            StopCoroutine(pieceRoutine);
            pieceRoutine = null;
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].transform == null) continue;
            if (pieces[i].renderer != null && pieces[i].renderer.sprite != null) Destroy(pieces[i].renderer.sprite);   // the cut-out sprite made for it
            Destroy(pieces[i].transform.gameObject);
        }
        pieces.Clear();
    }

    // ------------------------------------------------------------ the revive bar

    private Transform barRoot;
    private SpriteRenderer barBack;
    private SpriteRenderer barFill;
    private static Sprite whiteCentred;
    private static Sprite whiteLeft;

    private void UpdateBar()
    {
        bool show = showReviveBar && state == DownState.Down && (reviverNear || progress > 0.001f);
        if (!show)
        {
            HideBar();
            return;
        }

        if (barRoot == null) BuildBar();

        barRoot.gameObject.SetActive(true);
        barRoot.position = transform.position + (Vector3)barOffset;   // upright and in world space, whatever the player's rotation

        if (sprite != null)
        {
            barBack.sortingLayerID = barFill.sortingLayerID = sprite.sortingLayerID;
            barBack.sortingOrder = sprite.sortingOrder + 10;
            barFill.sortingOrder = sprite.sortingOrder + 11;
        }

        barFill.transform.localScale = new Vector3(Mathf.Max(0.0001f, barWidth * ReviveProgress), barHeight, 1f);
    }

    private void BuildBar()
    {
        if (whiteCentred == null) whiteCentred = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect);
        if (whiteLeft == null) whiteLeft = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f, 0, SpriteMeshType.FullRect);

        GameObject root = new GameObject("Revive Bar");
        root.layer = sprite != null ? sprite.gameObject.layer : gameObject.layer;
        barRoot = root.transform;

        GameObject back = new GameObject("Back");
        back.layer = root.layer;
        back.transform.SetParent(barRoot, false);
        back.transform.localScale = new Vector3(barWidth + 0.08f, barHeight + 0.08f, 1f);
        barBack = back.AddComponent<SpriteRenderer>();
        barBack.sprite = whiteCentred;
        barBack.color = barBackColor;

        GameObject fill = new GameObject("Fill");
        fill.layer = root.layer;
        fill.transform.SetParent(barRoot, false);
        fill.transform.localPosition = new Vector3(-barWidth * 0.5f, 0f, 0f);
        barFill = fill.AddComponent<SpriteRenderer>();
        barFill.sprite = whiteLeft;
        barFill.color = barFillColor;
    }

    private void HideBar()
    {
        if (barRoot != null) barRoot.gameObject.SetActive(false);
    }

    private void DestroyBar()
    {
        if (barRoot != null) Destroy(barRoot.gameObject);
        barRoot = null;
    }

    // ------------------------------------------------------------ helpers

    private static bool HasParameter(Animator a, string parameter)
    {
        if (a.runtimeAnimatorController == null) return false;

        AnimatorControllerParameter[] all = a.parameters;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].name == parameter) return true;
        }
        return false;
    }

    // A 2D sound that plays wherever the cameras are, and follows the Sound On/Off setting.
    private void PlaySound(AudioClip clip, Vector3 at)
    {
        if (clip == null || !GameSettings.SoundOn) return;

        GameObject go = new GameObject("CoopDowned Sound");
        go.transform.position = at;
        AudioSource source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.volume = soundVolume;
        source.PlayOneShot(clip);
        Destroy(go, clip.length + 0.2f);
    }
}
