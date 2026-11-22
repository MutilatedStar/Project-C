using System;
using System.Collections.Generic;
using Ilumisoft.HealthSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cainos.PixelArtTopDown_Basic
{
    /// <summary>
    /// Which colliders a rule applies to. A collider matches when it is on a ticked PHYSICS layer, or when the sprite / tilemap on
    /// its object is on one of the listed SORTING layers (handy when walls and props share a physics layer but not a sorting layer).
    /// </summary>
    [Serializable]
    public class JumpColliderFilter
    {
        [Tooltip("Physics layers. A collider on any ticked layer matches.")]
        public LayerMask physicsLayers;

        [Tooltip("Sorting layer NAMES (Project Settings > Tags and Layers). A collider matches when the sprite or tilemap on its object is on one of them.")]
        public string[] sortingLayers = new string[0];

        private int[] sortingIds;   // resolved once in Prepare, so Matches never compares strings

        public bool IsEmpty
        {
            get { return physicsLayers.value == 0 && (sortingLayers == null || sortingLayers.Length == 0); }
        }

        public void Prepare(string owner, UnityEngine.Object context)
        {
            int count = sortingLayers != null ? sortingLayers.Length : 0;
            sortingIds = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (TopDownJump.TryGetSortingLayerId(sortingLayers[i], out sortingIds[i])) continue;

                sortingIds[i] = int.MinValue;   // never equals a real id
                Debug.LogWarning("TopDownJump on '" + owner + "': there is no sorting layer called \"" + sortingLayers[i] + "\" " +
                                 "(Project Settings > Tags and Layers). That entry is ignored.", context);
            }
        }

        public bool Matches(Collider2D collider, Renderer renderer)
        {
            if ((physicsLayers.value & (1 << collider.gameObject.layer)) != 0) return true;
            if (renderer == null || sortingIds == null) return false;

            int id = renderer.sortingLayerID;
            for (int i = 0; i < sortingIds.Length; i++)
            {
                if (sortingIds[i] == id) return true;
            }
            return false;
        }
    }

    /// <summary>One step up: the physics layer and sorting layer the player is moved to.</summary>
    [Serializable]
    public struct JumpLevel
    {
        [Tooltip("Physics layer NAME the player moves to (the layer that collides with that level's walls). Empty = keep the one it has.")]
        public string physicsLayer;

        [Tooltip("Sorting layer NAME the player's sprites move to. Empty = keep the one they have.")]
        public string sortingLayer;
    }

    /// <summary>
    /// A "fake" jump for the top-down character. The body never leaves the ground (the Rigidbody2D keeps its position, so walls
    /// keep working like they always did); only the SPRITE rises along an arc while a shadow stays on the ground.
    ///
    ///   In the air   the player's hurt box is gone: CoopHealth.Airborne makes every hit do nothing (foes, lava, water and spikes
    ///                included), and any trigger colliders listed in "Hurtboxes" are switched off as well.
    ///   Jump OVER    while in the air, colliders that pass the "Jump Over" rule stop blocking the body, so the player can hop over
    ///                foes, crates and so on. "Never Jump Over" always wins: put the walls and the stage boundary there, or the
    ///                player could jump out of the stage. Anything that matches neither stays solid.
    ///   Jump ON      landing on a collider that passes the "Jump On" rule climbs one level: the player's physics layer and sorting
    ///                layer change to the next entry of "Levels". Walking off it (or jumping to open ground) drops back down.
    ///                If the landing spot is inside something solid, or there is no higher level, the player keeps gliding at ground
    ///                height until the spot is free, and goes back to where the jump started after "Blocked Landing Seconds".
    ///
    /// TopDownCharacterController asks for the jump (TryJump) and reads IsAirborne / SpeedMultiplier. This component does the rest.
    /// Needs CoopHealth.cs (the Airborne flag) and the player's solid (non-trigger) collider on this object or a child.
    ///
    /// SETUP: add it to the player prefab next to TopDownCharacterController. Nothing else is required: with the default settings the
    /// player hops, gets a shadow, cannot be hurt in the air, and cannot pass through anything. Then choose what it may jump over / onto.
    /// The Jump key must not also be a Move key (see the warning this script prints), or every step that way becomes a jump.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public class TopDownJump : MonoBehaviour
    {
        // ------------------------------------------------------------ inspector

        [Header("The hop")]
        [Tooltip("How high the sprite rises at the top of the jump, in world units.")]
        [SerializeField, Min(0.05f)] private float jumpHeight = 0.7f;
        [Tooltip("Seconds from take-off to landing.")]
        [SerializeField, Min(0.1f)] private float jumpDuration = 0.55f;
        [Tooltip("Seconds after landing before the next jump is allowed.")]
        [SerializeField, Min(0f)] private float cooldown = 0.1f;
        [Tooltip("Walking speed in the air as a multiple of the ground speed (1 = the same). A little over 1 makes it feel like a leap.")]
        [SerializeField, Min(0.1f)] private float airSpeedMultiplier = 1.15f;

        [Header("Hurt box (gone while in the air)")]
        [Tooltip("OPTIONAL extra. TRIGGER colliders to switch off in the air (the hurt box a foe's hit box touches). The solid body must NOT be listed, " +
                 "or the player would fall through walls. CoopHealth.Airborne already stops all damage, so this can stay empty.")]
        [SerializeField] private Collider2D[] hurtboxes;

        [Header("Sprite and shadow")]
        [Tooltip("The SpriteRenderer the Animator drives. Empty = the one on this object.")]
        [SerializeField] private SpriteRenderer bodyRenderer;
        [Tooltip("OPTIONAL: the player's existing shadow object. It shrinks while the player is high. Empty = a simple pixel shadow is made and shown only while jumping.")]
        [SerializeField] private Transform shadow;
        [Tooltip("Made shadow only: where it sits at the player's feet, relative to the player.")]
        [SerializeField] private Vector2 shadowOffset = Vector2.zero;
        [Tooltip("Made shadow only: its size in world units.")]
        [SerializeField] private Vector2 shadowSize = new Vector2(0.8f, 0.4f);
        [Tooltip("Made shadow only: how dark it is.")]
        [SerializeField, Range(0f, 1f)] private float shadowOpacity = 0.35f;
        [Tooltip("How small the shadow gets at the top of the jump (1 = it does not change).")]
        [SerializeField, Range(0.2f, 1f)] private float shadowShrink = 0.6f;
        [Tooltip("OPTIONAL: other objects on the player that should rise with the sprite (a glow overlay, a held item). Only objects that sit still on the player: " +
                 "anything that moves itself every frame (a sword swing) would fight this.")]
        [SerializeField] private Transform[] liftedParts;

        [Header("Jump OVER: what the player can pass while in the air")]
        [Tooltip("Colliders the player flies over. Default is empty (nothing), so nothing can be jumped over until you choose. " +
                 "Everything works too, as long as Never Jump Over lists the walls.")]
        [SerializeField] private JumpColliderFilter jumpOver = new JumpColliderFilter();
        [Tooltip("EXCEPTIONS: always solid, even in the air. They beat Jump Over and Jump On. Put the walls and the stage boundary here.")]
        [SerializeField] private JumpColliderFilter neverJumpOver = new JumpColliderFilter();

        [Header("Jump ON: things to land on and go up a layer")]
        [Tooltip("Colliders the player can land on. Landing on one moves the player up one level. They count as jump-over things too, so they need not be listed above.")]
        [SerializeField] private JumpColliderFilter jumpOn = new JumpColliderFilter();
        [Tooltip("Element 0 = where the player lands after the FIRST step up, Element 1 = after the second... The level the player starts on is read from the player itself. " +
                 "Names must exist in Project Settings > Tags and Layers (physics layer and sorting layer are separate lists).")]
        [SerializeField] private JumpLevel[] levels =
        {
            new JumpLevel { physicsLayer = "Layer 2", sortingLayer = "Layer 2" },
            new JumpLevel { physicsLayer = "Layer 3", sortingLayer = "Layer 3" }
        };
        [Tooltip("How far the sprite is drawn higher for each level climbed (world units). 0 = the layer changes but the sprite stays level with the ground.")]
        [SerializeField, Min(0f)] private float heightPerLevel = 0f;

        [Header("Fine tuning")]
        [Tooltip("How far around the player colliders are checked for 'can I pass this?' while in the air.")]
        [SerializeField, Min(0.5f)] private float scanRadius = 1.25f;
        [Tooltip("A landing that is blocked (inside something solid) keeps the player gliding at ground height this long, then sends them back to where the jump started.")]
        [SerializeField, Min(0.1f)] private float blockedLandingSeconds = 1f;

        [Header("Debug")]
        [Tooltip("Prints, once at the start, the Jump keys this player really answers to and where they come from. Switch it off once the keys are right. " +
                 "Also in Play mode: the three dots of this component > Log Jump Keys.")]
        [SerializeField] private bool logKeysAtStart = true;

        // ------------------------------------------------------------ what other scripts can use

        /// <summary>True from take-off until the landing is complete (also while a blocked landing is being waited out).</summary>
        public bool IsAirborne { get { return airborne; } }

        /// <summary>Multiply the walking speed by this: the air speed while jumping, 1 on the ground.</summary>
        public float SpeedMultiplier { get { return airborne ? airSpeedMultiplier : 1f; } }

        /// <summary>0 on the ground, 1 after one step up, 2 after two...</summary>
        public int Level { get { return supports.Count; } }

        /// <summary>How high the sprite is drawn above the ground right now (arc plus the height of the level).</summary>
        public float Height { get { return visualBase + arc; } }

        /// <summary>Raised at take-off.</summary>
        public event Action Jumped;

        /// <summary>Raised when the player is down again (a blocked landing that sent them back to the take-off spot raises it too).</summary>
        public event Action Landed;

        /// <summary>Raised when the level changes (up or down), with the new level.</summary>
        public event Action<int> LevelChanged;

        /// <summary>Starts a jump if one can start now. Returns true when it did.</summary>
        public bool TryJump()
        {
            if (airborne || cooldownLeft > 0f || !isActiveAndEnabled) return false;
            if (health != null && !health.IsAlive) return false;

            BeginJump();
            return true;
        }

        // ------------------------------------------------------------ state

        private enum Rule { Solid, Over, Climb }

        private const float LiftEpsilon = 0.001f;   // below this the sprite counts as "on the ground"
        private const float LevelLiftSpeed = 6f;    // world units per second the sprite rises or sinks when the level changes
        private const int RuleCacheLimit = 256;     // the rule cache is emptied when it grows past this

        private Rigidbody2D rb;
        private CoopHealth health;
        private Collider2D[] solids;                                       // the player's non-trigger colliders (the body)
        private readonly List<Collider2D> triggerHurtboxes = new List<Collider2D>();

        private bool airborne;
        private float elapsed;
        private float arc;                  // height from the jump itself
        private float visualBase;           // height from the level (heightPerLevel), eased
        private float cooldownLeft;
        private float blockedFor;
        private Vector2 takeoffPosition;

        // Colliders that stopped blocking us for this jump, and colliders we are standing on (one per level climbed).
        private readonly HashSet<Collider2D> ignored = new HashSet<Collider2D>();
        private readonly List<Collider2D> supports = new List<Collider2D>();
        private readonly Dictionary<Collider2D, Rule> rules = new Dictionary<Collider2D, Rule>(32);
        private readonly List<Collider2D> scanResults = new List<Collider2D>(32);
        private readonly Collider2D[] pointResults = new Collider2D[16];
        private ContactFilter2D scanFilter;     // solid colliders only: a trigger never blocks the body
        private ContactFilter2D pointFilter;    // triggers too: a trigger can be the thing you land on

        // Levels: index 0 = the layers the player starts on.
        private int[] physicsByLevel;
        private int[] sortingByLevel;
        private int currentPhysics;
        private int currentSorting;
        private int appliedLevel;

        // The sprite copy that is drawn raised (the Animator-driven sprite itself cannot move without moving the body).
        private SpriteRenderer body;
        private SpriteRenderer proxy;
        private SortingGroup proxyGroup;
        private bool proxyShown;

        private SpriteRenderer shadowRenderer;
        private bool shadowIsMade;
        private Vector3 shadowBaseScale;
        private Vector3 shadowBaseLocal;

        private Vector3[] partBase;
        private bool partsLifted;

        private static Sprite madeShadowSprite;
        private const int ShadowTextureWidth = 16;
        private const int ShadowTextureHeight = 8;
        private const float ShadowPixelsPerUnit = 16f;

        // ------------------------------------------------------------ setup

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            health = GetComponent<CoopHealth>();
            if (health == null)
            {
                Debug.LogWarning("TopDownJump on '" + name + "': there is no CoopHealth on this object, so the player is NOT protected while in the air " +
                                 "(only the colliders in Hurtboxes are switched off).", this);
            }

            CacheColliders();
            BuildSpriteProxy();
            BuildShadow();
            BuildLevels();

            jumpOver.Prepare(name, this);
            neverJumpOver.Prepare(name, this);
            jumpOn.Prepare(name, this);

            scanFilter = new ContactFilter2D();
            scanFilter.useTriggers = false;
            pointFilter = new ContactFilter2D();
            pointFilter.useTriggers = true;

            if (jumpOver.physicsLayers.value == -1 && neverJumpOver.IsEmpty)
            {
                Debug.LogWarning("TopDownJump on '" + name + "': Jump Over is set to Everything and Never Jump Over is empty, so the player can jump over " +
                                 "walls and leave the stage. Put the walls' layer (or sorting layer) in Never Jump Over.", this);
            }
        }

        // This script never reads the Jump key itself (the controller asks PlayerInputSource.JumpPressed), so whatever keys
        // PlayerInputSource holds for this player are the keys that jump. Start only reports them, so a wrong setup is visible.
        private void Start()
        {
            PlayerInputSource input = PlayerInputSource.For(gameObject);
            if (input == null) return;

            if (logKeysAtStart) LogJumpKeys(input);
            WarnIfJumpKeyIsShared(input);
        }

        [ContextMenu("Log Jump Keys")]
        private void LogJumpKeysNow()
        {
            if (!Application.isPlaying) return;   // PlayerInputSource.For would attach components in edit mode

            PlayerInputSource input = PlayerInputSource.For(gameObject);
            if (input != null) LogJumpKeys(input);
        }

        private void OnEnable()
        {
            FloorController.FloorChanged += HandleFloorChanged;
        }

        private void OnDisable()
        {
            FloorController.FloorChanged -= HandleFloorChanged;
            ResetToGround();
        }

        // A door or the Shop moved the player: whatever they were jumping over or standing on is somewhere else now.
        private void HandleFloorChanged(int floor)
        {
            ResetToGround();
        }

        private void CacheColliders()
        {
            Collider2D[] all = GetComponentsInChildren<Collider2D>();
            List<Collider2D> solid = new List<Collider2D>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].isTrigger) solid.Add(all[i]);
            }
            solids = solid.ToArray();

            if (hurtboxes == null) return;
            for (int i = 0; i < hurtboxes.Length; i++)
            {
                Collider2D c = hurtboxes[i];
                if (c == null) continue;

                if (!c.isTrigger)
                {
                    Debug.LogWarning("TopDownJump on '" + name + "': '" + c.name + "' in Hurtboxes is not a trigger, so it is the solid body and " +
                                     "switching it off would drop the player through walls. It is ignored.", c);
                    continue;
                }
                triggerHurtboxes.Add(c);
            }
        }

        // The Animator draws the player's sprite on the player's own object, which cannot move without taking the body with it.
        // So while the player is high, that renderer is hidden and a copy is drawn raised instead. The copy sits in its own
        // Sorting Group, which is sorted by the ground position, so a jumping player does not slip behind things in front of them.
        private void BuildSpriteProxy()
        {
            body = bodyRenderer != null ? bodyRenderer : GetComponent<SpriteRenderer>();
            if (body == null)
            {
                Debug.LogWarning("TopDownJump on '" + name + "': no SpriteRenderer found (set Body Renderer). The sprite will not rise; " +
                                 "the shadow, the hurt box and the layers still work.", this);
                return;
            }

            GameObject group = new GameObject("JumpVisual");
            group.layer = gameObject.layer;
            group.transform.SetParent(body.transform, false);
            proxyGroup = group.AddComponent<SortingGroup>();

            GameObject sprite = new GameObject("JumpSprite");
            sprite.layer = gameObject.layer;
            sprite.transform.SetParent(group.transform, false);
            proxy = sprite.AddComponent<SpriteRenderer>();

            group.SetActive(false);
        }

        private void BuildShadow()
        {
            if (shadow != null)
            {
                shadowRenderer = shadow.GetComponent<SpriteRenderer>();
                shadowBaseScale = shadow.localScale;
                shadowBaseLocal = shadow.localPosition;
                return;
            }

            if (madeShadowSprite == null) madeShadowSprite = CreateShadowSprite();

            GameObject go = new GameObject("JumpShadow");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            shadow = go.transform;
            shadowIsMade = true;

            shadowRenderer = go.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = madeShadowSprite;
            shadowRenderer.color = new Color(0f, 0f, 0f, shadowOpacity);
            shadowRenderer.sortingLayerID = body != null ? body.sortingLayerID : 0;
            shadowRenderer.sortingOrder = body != null ? body.sortingOrder - 1 : -1;
            shadowRenderer.enabled = false;

            float naturalWidth = ShadowTextureWidth / ShadowPixelsPerUnit;
            float naturalHeight = ShadowTextureHeight / ShadowPixelsPerUnit;
            shadowBaseScale = new Vector3(shadowSize.x / naturalWidth, shadowSize.y / naturalHeight, 1f);
            shadowBaseLocal = new Vector3(shadowOffset.x, shadowOffset.y, 0f);
            shadow.localScale = shadowBaseScale;
            shadow.localPosition = shadowBaseLocal;
        }

        // A small hard-edged ellipse, so it matches pixel art. Made once and shared by every player.
        private static Sprite CreateShadowSprite()
        {
            Texture2D texture = new Texture2D(ShadowTextureWidth, ShadowTextureHeight, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;

            Color32[] pixels = new Color32[ShadowTextureWidth * ShadowTextureHeight];
            for (int y = 0; y < ShadowTextureHeight; y++)
            {
                for (int x = 0; x < ShadowTextureWidth; x++)
                {
                    float nx = (x + 0.5f - ShadowTextureWidth * 0.5f) / (ShadowTextureWidth * 0.5f);
                    float ny = (y + 0.5f - ShadowTextureHeight * 0.5f) / (ShadowTextureHeight * 0.5f);
                    bool inside = nx * nx + ny * ny <= 1f;
                    pixels[y * ShadowTextureWidth + x] = new Color32(0, 0, 0, (byte)(inside ? 255 : 0));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, ShadowTextureWidth, ShadowTextureHeight), new Vector2(0.5f, 0.5f), ShadowPixelsPerUnit);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // Level 0 = the layers the player starts on. Each entry of Levels adds one step up; a blank or unknown name keeps the one below.
        private void BuildLevels()
        {
            int count = levels != null ? levels.Length : 0;
            physicsByLevel = new int[count + 1];
            sortingByLevel = new int[count + 1];
            physicsByLevel[0] = gameObject.layer;
            sortingByLevel[0] = body != null ? body.sortingLayerID : 0;

            for (int i = 0; i < count; i++)
            {
                physicsByLevel[i + 1] = ResolvePhysicsLayer(levels[i].physicsLayer, physicsByLevel[i]);
                sortingByLevel[i + 1] = ResolveSortingLayer(levels[i].sortingLayer, sortingByLevel[i]);
            }

            currentPhysics = physicsByLevel[0];
            currentSorting = sortingByLevel[0];
            appliedLevel = 0;
        }

        private int ResolvePhysicsLayer(string layerName, int fallback)
        {
            if (string.IsNullOrEmpty(layerName)) return fallback;

            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) return layer;

            if (!jumpOn.IsEmpty)
            {
                Debug.LogWarning("TopDownJump on '" + name + "': Levels names a physics layer \"" + layerName + "\" that does not exist " +
                                 "(Project Settings > Tags and Layers), so that step keeps the layer below it.", this);
            }
            return fallback;
        }

        private int ResolveSortingLayer(string layerName, int fallback)
        {
            if (string.IsNullOrEmpty(layerName)) return fallback;

            int id;
            if (TryGetSortingLayerId(layerName, out id)) return id;

            if (!jumpOn.IsEmpty)
            {
                Debug.LogWarning("TopDownJump on '" + name + "': Levels names a sorting layer \"" + layerName + "\" that does not exist " +
                                 "(Project Settings > Tags and Layers), so that step keeps the sorting layer below it.", this);
            }
            return fallback;
        }

        /// <summary>Finds a sorting layer by name. SortingLayer.NameToID answers 0 (Default) for a typo, so the list is searched instead.</summary>
        public static bool TryGetSortingLayerId(string layerName, out int id)
        {
            SortingLayer[] all = SortingLayer.layers;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == layerName)
                {
                    id = all[i].id;
                    return true;
                }
            }
            id = 0;
            return false;
        }

        // What this player's PlayerInputSource really holds for Jump, and who owns it. With a PlayerInputHub in the scene the hub owns every
        // player's keys and copies them over the character's own PlayerInputSource when the character wakes up, so keys typed on the character
        // are lost. Keys changed in Play mode are not kept when Play mode stops, so edit them with Play mode off.
        // The line splits the answer into this player's own list and the Solo Extra Keys, because a key that is left in EITHER one still jumps:
        // a Jump list is an array (Player 1's default is Space AND W), and changing one element leaves the others in place.
        private void LogJumpKeys(PlayerInputSource input)
        {
            string owner = input.Hub != null
                ? "the PlayerInputHub in the scene (change them in that player's slot there; keys typed on the character's own PlayerInputSource are overwritten)"
                : "this character's own PlayerInputSource (there is no PlayerInputHub in the scene)";

            KeyCode[] own = null;
            if (input.keysByPlayer != null)
            {
                int index = Mathf.Clamp(input.playerNumber, 1, PlayerInputSource.MaxPlayers) - 1;
                if (index < input.keysByPlayer.Length && input.keysByPlayer[index] != null) own = input.keysByPlayer[index].Get(PlayerAction.Jump);
            }
            KeyCode[] solo = input.soloExtraKeys != null ? input.soloExtraKeys.Get(PlayerAction.Jump) : null;

            Debug.Log("TopDownJump on '" + name + "' (Player " + input.playerNumber + "): Jump keys in use = [" + JoinKeys(input.KeysFor(PlayerAction.Jump)) + "]. " +
                      "This player's own Jump list = [" + JoinKeys(own) + "]. " +
                      "Solo Extra Keys Jump (Player 1 only, while Player 2 is out of the game) = [" + JoinKeys(solo) + "]. " +
                      "Controller button = " + input.PadButtonFor(PlayerAction.Jump) + ", keyboard on = " + input.KeyboardEnabled + ", " +
                      "two players = " + PlayerInputSource.TwoPlayers + ". Keys come from " + owner + ".", this);
        }

        private static string JoinKeys(KeyCode[] keys)
        {
            return keys == null || keys.Length == 0 ? "none" : string.Join(", ", keys);
        }

        // Jump has its own key in a top-down game, but the defaults share it with Move (Player 1: W, Player 2: Up Arrow, made for the Platformer
        // controller) and a key can be listed for two actions at once (Player 2's Swap is "/" by default). PlayerInputSource only warns about
        // keys shared between two PLAYERS, so the clash inside one player is checked here.
        private void WarnIfJumpKeyIsShared(PlayerInputSource input)
        {
            KeyCode[] jumpKeys = input.KeysFor(PlayerAction.Jump);
            for (int a = 0; a < PlayerInputSource.ActionCount; a++)
            {
                PlayerAction action = (PlayerAction)a;
                if (action == PlayerAction.Jump) continue;

                KeyCode[] otherKeys = input.KeysFor(action);
                for (int j = 0; j < jumpKeys.Length; j++)
                {
                    if (Array.IndexOf(otherKeys, jumpKeys[j]) < 0) continue;

                    Debug.LogWarning("TopDownJump on '" + name + "' (Player " + input.playerNumber + "): the Jump key " + jumpKeys[j] + " is also a " +
                                     action + " key, so one press does both. Give Jump a key of its own in the PlayerInputHub " +
                                     "(and check the \"Solo Extra Keys\" there too).", this);
                    return;   // one warning is enough
                }
            }
        }

        // ------------------------------------------------------------ the jump

        private void Update()
        {
            float dt = Time.deltaTime;
            if (cooldownLeft > 0f) cooldownLeft -= dt;

            if (airborne) FlyStep(dt);
            else if (supports.Count > 0) StepDownIfUnsupported();

            visualBase = Mathf.MoveTowards(visualBase, supports.Count * heightPerLevel, LevelLiftSpeed * dt);
        }

        private void BeginJump()
        {
            airborne = true;
            elapsed = 0f;
            blockedFor = 0f;
            arc = 0f;
            takeoffPosition = rb.position;

            SetHittable(false);
            IgnoreNearbyPassables();   // before the first physics step, so nothing next to the player can stop the take-off

            if (Jumped != null) Jumped();
        }

        private void FlyStep(float dt)
        {
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / jumpDuration);
            arc = jumpHeight * 4f * t * (1f - t);   // a parabola: 0 at take-off, jumpHeight halfway, 0 at the landing

            IgnoreNearbyPassables();
            if (t < 1f) return;

            // The arc is over. Land if the spot is free; otherwise glide at ground height until it is.
            arc = 0f;
            if (TryLand())
            {
                EndJump();
                return;
            }

            blockedFor += dt;
            if (blockedFor >= blockedLandingSeconds) ReturnToTakeoff();
        }

        private void EndJump()
        {
            airborne = false;
            arc = 0f;
            elapsed = 0f;
            blockedFor = 0f;
            cooldownLeft = cooldown;

            RestoreCollisions();
            SetHittable(true);

            if (Landed != null) Landed();
        }

        // A landing that stayed blocked: back to where the jump started, which was a legal spot.
        private void ReturnToTakeoff()
        {
            rb.position = takeoffPosition;
            rb.linearVelocity = Vector2.zero;
            EndJump();
        }

        // Cancels everything: used when the component is switched off and when the floor changes.
        private void ResetToGround()
        {
            airborne = false;
            arc = 0f;
            elapsed = 0f;
            blockedFor = 0f;
            visualBase = 0f;

            RestoreCollisions();
            ReleaseSupportsTo(0, true);
            SetHittable(true);

            if (proxyShown) SetLifted(false);
            LiftParts(0f);
            if (shadowIsMade && shadowRenderer != null) shadowRenderer.enabled = false;
        }

        private void SetHittable(bool hittable)
        {
            if (health != null) health.Airborne = !hittable;

            for (int i = 0; i < triggerHurtboxes.Count; i++)
            {
                if (triggerHurtboxes[i] != null) triggerHurtboxes[i].enabled = hittable;
            }
        }

        // ------------------------------------------------------------ jump over

        // Where the body is: the middle of the solid collider, the same spot FloorManager reads hazard tiles at.
        private Vector2 FeetPoint
        {
            get
            {
                if (solids.Length > 0 && solids[0] != null) return solids[0].bounds.center;
                return rb.position;
            }
        }

        // Every frame in the air: colliders near the player that may be passed stop blocking the body. Colliders that walk into
        // range later (a foe) are caught on the next frame. Solid ones stay solid.
        private void IgnoreNearbyPassables()
        {
            Physics2D.OverlapCircle(FeetPoint, scanRadius, scanFilter, scanResults);

            for (int i = 0; i < scanResults.Count; i++)
            {
                Collider2D c = scanResults[i];
                if (c == null || ignored.Contains(c) || c.transform.IsChildOf(transform)) continue;
                if (IndexInSupports(c, supports.Count) >= 0) continue;   // already passed: we stand on it
                if (RuleFor(c) == Rule.Solid) continue;

                SetPairIgnored(c, true);
                ignored.Add(c);
            }
        }

        // The rule for one collider, worked out once and remembered. Never Jump Over wins, then Jump On, then Jump Over.
        private Rule RuleFor(Collider2D c)
        {
            Rule rule;
            if (rules.TryGetValue(c, out rule)) return rule;

            Renderer r = c.GetComponent<Renderer>();
            if (r == null) r = c.GetComponentInParent<Renderer>();

            if (neverJumpOver.Matches(c, r)) rule = Rule.Solid;
            else if (jumpOn.Matches(c, r)) rule = Rule.Climb;
            else if (jumpOver.Matches(c, r)) rule = Rule.Over;
            else rule = Rule.Solid;

            if (rules.Count > RuleCacheLimit) rules.Clear();
            rules[c] = rule;
            return rule;
        }

        private void SetPairIgnored(Collider2D other, bool ignore)
        {
            for (int s = 0; s < solids.Length; s++)
            {
                if (solids[s] != null) Physics2D.IgnoreCollision(solids[s], other, ignore);
            }
        }

        // Everything that was passed through this jump blocks again. Supports (what we stand on) are not in the set, so they stay passable.
        private void RestoreCollisions()
        {
            foreach (Collider2D c in ignored)
            {
                if (c != null) SetPairIgnored(c, false);
            }
            ignored.Clear();
        }

        // ------------------------------------------------------------ landing and levels

        // True when the player can come down here (and does: supports and layers are updated). False = blocked, try again next frame.
        private bool TryLand()
        {
            Vector2 feet = FeetPoint;

            // Supports still under the feet, from the top down (a support we jumped away from is let go).
            int keep = supports.Count;
            while (keep > 0 && !IsUnder(supports[keep - 1], feet)) keep--;

            // A jump-on collider under the feet that we were not standing on yet: climb onto it.
            Collider2D climbOnto = FindNewSupport(feet, keep);
            int destination = keep + (climbOnto != null ? 1 : 0);
            if (destination >= physicsByLevel.Length) return false;   // nothing higher to stand on

            // Anything else we flew over and still overlap would shove the player out when it becomes solid again.
            int destinationLayer = physicsByLevel[destination];
            foreach (Collider2D c in ignored)
            {
                if (c == null || c == climbOnto) continue;
                if (Physics2D.GetIgnoreLayerCollision(destinationLayer, c.gameObject.layer)) continue;   // would not collide anyway
                if (OverlapsBody(c)) return false;
            }

            ReleaseSupportsTo(keep, false);
            if (climbOnto != null)
            {
                ignored.Remove(climbOnto);   // from now on it is a support: it stays passable until we leave it
                supports.Add(climbOnto);
            }
            ApplyLevel(supports.Count);
            return true;
        }

        private Collider2D FindNewSupport(Vector2 feet, int keep)
        {
            int count = Physics2D.OverlapPoint(feet, pointFilter, pointResults);
            for (int i = 0; i < count; i++)
            {
                Collider2D c = pointResults[i];
                if (c == null || c.transform.IsChildOf(transform)) continue;
                if (RuleFor(c) != Rule.Climb || IndexInSupports(c, keep) >= 0) continue;
                return c;
            }
            return null;
        }

        private bool OverlapsBody(Collider2D other)
        {
            for (int i = 0; i < solids.Length; i++)
            {
                if (solids[i] == null) continue;

                ColliderDistance2D distance = solids[i].Distance(other);
                if (distance.isValid && distance.isOverlapped) return true;
            }
            return false;
        }

        private static bool IsUnder(Collider2D support, Vector2 feet)
        {
            return support != null && support.OverlapPoint(feet);
        }

        private int IndexInSupports(Collider2D c, int limit)
        {
            for (int i = 0; i < limit && i < supports.Count; i++)
            {
                if (supports[i] == c) return i;
            }
            return -1;
        }

        // On the ground at a raised level: walking off the edge of what we stand on drops us a level.
        private void StepDownIfUnsupported()
        {
            Vector2 feet = FeetPoint;
            int keep = supports.Count;
            while (keep > 0 && !IsUnder(supports[keep - 1], feet)) keep--;

            if (keep != supports.Count) ReleaseSupportsTo(keep, true);
        }

        // Lets go of every support above "keep": their collision comes back on.
        private void ReleaseSupportsTo(int keep, bool applyLevel)
        {
            for (int i = supports.Count - 1; i >= keep; i--)
            {
                if (supports[i] != null) SetPairIgnored(supports[i], false);
                supports.RemoveAt(i);
            }
            if (applyLevel) ApplyLevel(supports.Count);
        }

        // Moves the player to the layers of this level. Only things that are on the layer the player is on follow it: a child on
        // its own layer (a weapon hit box, a health bar on a UI sorting layer) is left alone.
        private void ApplyLevel(int newLevel)
        {
            if (newLevel == appliedLevel) return;
            appliedLevel = newLevel;

            int newPhysics = physicsByLevel[newLevel];
            if (newPhysics != currentPhysics)
            {
                FollowLayer(transform, currentPhysics, newPhysics);
                currentPhysics = newPhysics;
            }

            int newSorting = sortingByLevel[newLevel];
            if (newSorting != currentSorting)
            {
                Renderer[] all = GetComponentsInChildren<Renderer>(true);   // not cached: other scripts add renderers (the glow) at runtime
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].sortingLayerID == currentSorting) all[i].sortingLayerID = newSorting;
                }
                currentSorting = newSorting;
            }

            if (LevelChanged != null) LevelChanged(newLevel);
        }

        private static void FollowLayer(Transform t, int from, int to)
        {
            if (t.gameObject.layer == from) t.gameObject.layer = to;
            for (int i = 0; i < t.childCount; i++) FollowLayer(t.GetChild(i), from, to);
        }

        // ------------------------------------------------------------ drawing

        // After the Animator has chosen this frame's sprite.
        private void LateUpdate()
        {
            float lift = visualBase + arc;
            bool lifted = body != null && lift > LiftEpsilon;

            if (lifted != proxyShown) SetLifted(lifted);
            if (lifted) MirrorBody(lift);

            LiftParts(lift);
            UpdateShadow();
        }

        // Hide the Animator's renderer without touching its "enabled" (other scripts use that to blink the player) and show the copy.
        private void SetLifted(bool lifted)
        {
            proxyShown = lifted;
            body.forceRenderingOff = lifted;
            proxyGroup.gameObject.SetActive(lifted);
        }

        private void MirrorBody(float lift)
        {
            if (proxy.sprite != body.sprite) proxy.sprite = body.sprite;
            proxy.color = body.color;
            proxy.flipX = body.flipX;
            proxy.flipY = body.flipY;
            proxy.sharedMaterial = body.sharedMaterial;
            proxy.maskInteraction = body.maskInteraction;
            proxy.enabled = body.enabled;

            proxyGroup.sortingLayerID = body.sortingLayerID;
            proxyGroup.sortingOrder = body.sortingOrder;
            proxy.transform.localPosition = LocalLift(proxyGroup.transform, lift);
        }

        // The shadow stays on the ground (or on top of what the player stands on) and shrinks the higher the sprite is.
        private void UpdateShadow()
        {
            if (shadow == null) return;

            float high = jumpHeight > 0f ? Mathf.Clamp01(arc / jumpHeight) : 0f;
            shadow.localScale = shadowBaseScale * Mathf.Lerp(1f, shadowShrink, high);
            shadow.localPosition = shadowBaseLocal + LocalLift(shadow.parent, visualBase);

            if (shadowIsMade && shadowRenderer != null) shadowRenderer.enabled = airborne;
        }

        // Other objects on the player rise by the same amount, from where they were when the lift began.
        private void LiftParts(float lift)
        {
            if (liftedParts == null || liftedParts.Length == 0) return;

            bool on = lift > LiftEpsilon;
            if (on && !partsLifted)
            {
                if (partBase == null || partBase.Length != liftedParts.Length) partBase = new Vector3[liftedParts.Length];
                for (int i = 0; i < liftedParts.Length; i++)
                {
                    if (liftedParts[i] != null) partBase[i] = liftedParts[i].localPosition;
                }
            }

            if (on || partsLifted)
            {
                for (int i = 0; i < liftedParts.Length; i++)
                {
                    Transform part = liftedParts[i];
                    if (part == null) continue;
                    part.localPosition = on ? partBase[i] + LocalLift(part.parent, lift) : partBase[i];
                }
            }
            partsLifted = on;
        }

        // "Up by this much in the world", expressed in the local space of a parent (which may be scaled).
        private static Vector3 LocalLift(Transform space, float height)
        {
            Vector3 up = Vector3.up * height;
            return space != null ? space.InverseTransformVector(up) : up;
        }
    }
}
