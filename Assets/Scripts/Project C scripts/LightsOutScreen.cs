using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Ilumisoft.HealthSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

/// <summary>
/// The team-wipe screen. Put this on an object that is ALWAYS active in the dungeon scene (the same empty GameObject as
/// FloorController / PauseController is fine). There is nothing to wire up: the black screen, the "LIGHTS OUT" banner and the
/// three buttons are built by this script.
///
///   1. CoopHealth.TeamWiped fires (the last player that counts has gone down).
///   2. After a short pause (so the fall can be seen) the screen fades to black, the game freezes, and a big LIGHTS OUT banner
///      appears with three buttons under it:  Retry / Level Select / Menu.
///   3. Choose with  W / S  or  Up / Down  (they wrap round), then press Enter or Space. Or just click a button
///      (the mouse also highlights whatever it moves over). All keys are lists in the Inspector.
///      A CONTROLLER works too: D-pad or left stick up / down to choose, South (A on Xbox, Cross on PlayStation) to pick. ANY connected
///      controller can drive the menu, not only the one a player was using, because every player's input is locked while this screen is up.
///
///   Retry          behind the black screen every player that counts is back on full health (anybody knocked down or blown up
///                  by CoopDowned stands up again), put back on THEIR spot for the floor they are on (FloorController's Floors
///                  list, element 0 = floor 1), and that floor's foes start over. Then the screen fades out. On the FIRST floor
///                  (room 0) they go back to where they spawned when the scene started.
///   Level Select   resets the run (GameReset.ResetRun, the same as the pause menu's Quit) and loads the Level Select scene.
///   Menu           resets the run and loads the Main Menu scene.
///
/// SOUND: drag a clip into Lights Out Clip (Inspector). It plays once, the moment the last player goes down (Lights Out Sound Delay starts it
/// later, for example when the screen begins to fade). It follows the Sound On/Off setting and keeps playing while the game is frozen.
///
/// While it is up: players are locked, foes are frozen, Time.timeScale is 0, hazards and doors do nothing, pausing is switched off, and the
/// mouse cursor is freed. All of that is put back when the screen is gone.
///
/// Other scripts can read:  LightsOutScreen.IsActive,  LightsOutScreen.BlocksInput (FloorController.BlocksInput already includes it).
///
/// NEEDS (small additions in the files that came with this one):
///   FloorController.TryGetSpawnPoint(...) and the two LightsOutScreen lines in its Update / BlocksInput
///   FloorManager.RestartFloor()
///   PlayerInputSource.cs (PadButton and ControllerSlots) for the controller part. That part needs the Input System package with
///   Project Settings > Player > Active Input Handling = "Both"; without it the controller code is left out and the keyboard still works.
/// And EndScreens must NOT load the Failure Ending scene on a team wipe any more, or it will leave the scene before this screen shows.
/// </summary>
[DisallowMultipleComponent]
public class LightsOutScreen : MonoBehaviour
{
    private const int MaxPlayers = 4;
    private const int ButtonCount = 3;

    // ------------------------------------------------------------ inspector

    [Header("Scenes")]
    [Tooltip("Exact name of the Level Select scene (must be in Scenes In Build). \"LevelSelect\" and \"Level Select\" are tried if this can't be loaded.")]
    [SerializeField] private string levelSelectSceneName = "LevelSelect";
    [Tooltip("Exact name of the Main Menu scene (must be in Scenes In Build). \"MainMenu\" and \"Main Menu\" are tried if this can't be loaded.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Sound (optional, follows the Sound On/Off setting)")]
    [Tooltip("Plays once when the lights go out: the moment the last player goes down. Drag your clip in here; leave it empty for silence.")]
    [SerializeField] private AudioClip lightsOutClip;
    [SerializeField, Range(0f, 1f)] private float lightsOutVolume = 1f;
    [Tooltip("Seconds (real time) after the last player goes down before the clip starts. 0 = the instant they are all down. " +
             "Set it to the same number as Delay Before Fade (below) to start the clip just as the screen begins to go dark.")]
    [SerializeField, Min(0f)] private float lightsOutSoundDelay = 0f;

    [Header("Keys")]
    [SerializeField] private KeyCode[] upKeys = { KeyCode.W, KeyCode.UpArrow };
    [SerializeField] private KeyCode[] downKeys = { KeyCode.S, KeyCode.DownArrow };
    [Tooltip("Picks the highlighted button. Clicking a button picks it too.")]
    [SerializeField] private KeyCode[] confirmKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space };

    [Header("Controller")]
    [Tooltip("Lets any connected controller drive the menu: D-pad or left stick up / down to choose, a confirm button to pick. Needs the Input System package (Active Input Handling = Both).")]
    [SerializeField] private bool useControllers = true;
    [Tooltip("Picks the highlighted button. By POSITION, so it fits every pad: South = A (Xbox) / Cross (PlayStation) / B (Switch).")]
    [SerializeField] private PadButton[] confirmButtons = { PadButton.South };
    [Tooltip("How far the left stick must be pushed up or down to move the highlight one button. It has to come back to the middle before it moves again.")]
    [SerializeField, Range(0.2f, 0.95f)] private float stickThreshold = 0.6f;

    [Header("Timing (seconds, real time)")]
    [Tooltip("Pause between the last player going down and the screen starting to fade, so the fall can be seen.")]
    [SerializeField, Min(0f)] private float delayBeforeFade = 1f;
    [SerializeField, Min(0f)] private float fadeInTime = 1.5f;
    [Tooltip("How long the LIGHTS OUT banner takes to appear once the screen is black. The buttons follow.")]
    [SerializeField, Min(0f)] private float titleFadeTime = 0.5f;
    [SerializeField, Min(0f)] private float menuFadeTime = 0.4f;
    [Tooltip("After Retry: seconds the screen stays black once everyone is back in place, so a smoothed camera can catch up.")]
    [SerializeField, Min(0f)] private float holdTime = 0.75f;
    [SerializeField, Min(0f)] private float fadeOutTime = 1f;
    [Tooltip("Buttons ignore Enter / Space / clicks for this long after they appear, so a key that was being mashed can't pick one.")]
    [SerializeField, Min(0f)] private float acceptDelay = 0.3f;

    [Header("What it freezes")]
    [Tooltip("Sets Time.timeScale to 0 while the menu is up (put back afterwards). Foes and players are held still either way.")]
    [SerializeField] private bool freezeTime = true;

    [Header("Text")]
    [SerializeField] private string titleText = "LIGHTS OUT";
    [SerializeField] private string retryText = "Retry";
    [SerializeField] private string levelSelectText = "Level Select";
    [SerializeField] private string menuText = "Menu";
    [SerializeField] private string hintText = "W / S or Up / Down to choose,  Enter, Space or click to select";
    [Tooltip("Shown on a second line under the hint while a controller is plugged in.")]
    [SerializeField] private string controllerHintText = "D-pad or Left Stick to choose,  A / Cross to select";

    [Header("Look")]
    [Tooltip("Draw order of the screen's canvas. Higher is drawn on top of other canvases (the pause menu is 500).")]
    [SerializeField] private int canvasSortingOrder = 600;
    [Tooltip("How far the black panel reaches past EVERY screen edge, in canvas units (the canvas is 1920 x 1080). It only has to hide the screen, so it is made far bigger than any screen or camera view.")]
    [SerializeField, Min(0f)] private float blackOverscan = 5000f;
    [SerializeField] private Color titlePlateColor = new Color(0.35f, 0.06f, 0.06f, 0.95f);
    [SerializeField] private Color titleTextColor = new Color(1f, 0.93f, 0.8f, 1f);
    [SerializeField] private Color buttonColor = new Color(0.16f, 0.16f, 0.18f, 1f);
    [SerializeField] private Color buttonTextColor = new Color(0.75f, 0.75f, 0.78f, 1f);
    [SerializeField] private Color selectedButtonColor = new Color(0.95f, 0.75f, 0.25f, 1f);
    [SerializeField] private Color selectedTextColor = new Color(0.1f, 0.07f, 0.02f, 1f);

    // ------------------------------------------------------------ public / shared

    /// <summary>True from the moment the team is wiped until the screen is gone again (or the next scene is loading).</summary>
    public static bool IsActive { get; private set; }

    /// <summary>True while the screen is up, and for the rest of the frame in which it closed (so the key that picked Retry can't also move a player).</summary>
    public static bool BlocksInput => IsActive || closedFrame == Time.frameCount;

    /// <summary>True while a LightsOutScreen is switched on in the scene. CoopDowned reads it: with no game-over screen to take over, a team that is
    /// all down simply respawns instead of waiting for a screen that will never come.</summary>
    public static bool Available { get; private set; }

    private static int closedFrame = -1;

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsActive = false;
        Available = false;
        closedFrame = -1;
    }

    // ------------------------------------------------------------ state

    private GameObject _root;
    private CanvasGroup _blackGroup;
    private CanvasGroup _contentGroup;
    private CanvasGroup _titleGroup;
    private CanvasGroup _menuGroup;
    private RectTransform _titleRect;
    private readonly RectTransform[] _buttonRects = new RectTransform[ButtonCount];
    private readonly Image[] _buttonImages = new Image[ButtonCount];
    private readonly TMP_Text[] _buttonLabels = new TMP_Text[ButtonCount];

    private int _selected;
    private bool _menuReady;
    private float _acceptAfter;
    private Vector2 _lastMouse;
    private int _stickDir;            // left stick last frame: -1 up, +1 down, 0 neutral (so a held stick is one step, not one per frame)
    private TMP_Text _hintLabel;

    // What we changed, so it can be put back.
    private float _savedTimeScale = 1f;
    private bool _timeFrozen;
    private bool _foesFrozen;
    private bool _cursorSaved;
    private CursorLockMode _savedLock;
    private bool _savedCursorVisible;
    private bool _pauseChanged;
    private bool _pauseWasAllowed;

    // Where each player first stood in this scene: "room 0" sends them back here.
    private readonly Vector2[] _startPositions = new Vector2[MaxPlayers + 1];
    private readonly bool[] _hasStart = new bool[MaxPlayers + 1];

    private FloorController _floorController;
    private bool _warnedNoSpot;
    private AudioSource _audio;       // plays the Lights Out clip (2D, keeps playing while time is frozen)
    private float _wipeSeenSince = -1f;   // when every player was first seen at 0 health without the screen starting (PollTeamWipe)

    private const float PollWaitSeconds = 0.4f;       // how long "everybody is at 0 health" must last before the safety net starts the screen
    private const float MaxFloorChangeWait = 3f;      // the longest the screen waits for a running floor change

    // ------------------------------------------------------------ lifecycle

    private void Awake()
    {
        IsActive = false;
        BuildUi();
        BuildAudio();
        _root.SetActive(false);
    }

    private void OnEnable()
    {
        Available = true;
        CoopHealth.TeamWiped += HandleTeamWiped;
    }

    private void OnDisable()
    {
        Available = false;
        CoopHealth.TeamWiped -= HandleTeamWiped;
    }

    private void OnDestroy()
    {
        // Never leave the next scene frozen or locked.
        if (IsActive) ReleaseWorld();
    }

    private void HandleTeamWiped()
    {
        if (IsActive) return;

        if (_root == null)
        {
            Debug.LogError("LightsOutScreen: the team is wiped but the screen's UI was never built (check the Console for an error from Awake, " +
                           "usually TextMeshPro's essential resources not being imported).", this);
            return;
        }

        IsActive = true;       // from this moment the players hold still and the doors and hazards do nothing
        _wipeSeenSince = -1f;
        Debug.Log("LightsOutScreen: every player is down, so the game-over screen is starting.", this);

        LockPlayers(true);
        if (lightsOutClip != null) StartCoroutine(PlayLightsOutSound());
        StartCoroutine(ShowRoutine());
    }

    // Safety net that does not depend on any event or on any player's script: if every player that counts has been at 0 health for a
    // moment and the screen has not started, start it. (The normal way in is CoopHealth.TeamWiped, which fires the instant the last
    // player's health reaches 0.) The short wait stops a one-frame glitch, such as a scene loading, from showing the screen.
    private void PollTeamWipe()
    {
        if (!CoopHealth.AllCountedDead || closedFrame == Time.frameCount)
        {
            _wipeSeenSince = -1f;
            return;
        }

        if (_wipeSeenSince < 0f) _wipeSeenSince = Time.unscaledTime;
        if (Time.unscaledTime - _wipeSeenSince < PollWaitSeconds) return;

        Debug.LogWarning("LightsOutScreen: every player is at 0 health but TeamWiped was never raised, so the screen was started from here instead.", this);
        HandleTeamWiped();
    }

    // ------------------------------------------------------------ sound

    // A 2D source on its own child object, so both split-screen players hear it, it keeps playing while Time.timeScale is 0, and no
    // other AudioSource on this object is touched.
    private void BuildAudio()
    {
        var go = new GameObject("LightsOutSound");
        go.transform.SetParent(transform, false);

        _audio = go.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.spatialBlend = 0f;
        _audio.ignoreListenerPause = true;
    }

    private IEnumerator PlayLightsOutSound()
    {
        if (lightsOutSoundDelay > 0f) yield return new WaitForSecondsRealtime(lightsOutSoundDelay);
        if (_audio == null || lightsOutClip == null || !GameSettings.SoundOn) yield break;

        _audio.PlayOneShot(lightsOutClip, lightsOutVolume);
    }

    // ------------------------------------------------------------ main loop

    private void Update()
    {
        if (!IsActive)
        {
            RecordStartPositions();
            PollTeamWipe();
            return;
        }
        if (!_menuReady) return;

        // Up / down: one step per press, wrapping round.
        int move = 0;
        if (AnyDown(upKeys)) move--;
        if (AnyDown(downKeys)) move++;
        move += PadMove();
        move = Mathf.Clamp(move, -1, 1);
        if (move != 0) SetSelected((_selected + move + ButtonCount) % ButtonCount);

        // The mouse only takes over the highlight when it actually moves, so a resting cursor doesn't fight the keys.
        Vector2 mouse = Input.mousePosition;
        bool mouseMoved = (mouse - _lastMouse).sqrMagnitude > 1f;
        _lastMouse = mouse;
        int hover = ButtonUnderPointer(mouse);
        if (hover >= 0 && mouseMoved) SetSelected(hover);

        if (Time.unscaledTime < _acceptAfter) return;

        if (hover >= 0 && Input.GetMouseButtonDown(0))
        {
            SetSelected(hover);
            Activate(hover);
        }
        else if (AnyDown(confirmKeys) || PadConfirmPressed())
        {
            Activate(_selected);
        }
    }

    private static bool AnyDown(KeyCode[] keys)
    {
        if (keys == null) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (Input.GetKeyDown(keys[i])) return true;
        }
        return false;
    }

    // ------------------------------------------------------------ controller
    //
    // Every player's PlayerInputSource is Locked while this screen is up (so nobody can walk around behind the menu), and a locked
    // source reports "nothing pressed". So the pads are read here directly, through ControllerSlots (the same pad numbering the
    // players use). Any connected controller can drive the menu, not only the one a player happened to be using.

    // -1 = up, +1 = down, 0 = nothing. One step per D-pad press, and one step per push of the left stick.
    private int PadMove()
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers) return 0;

        int move = 0;
        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            if (pad.dpad.up.wasPressedThisFrame) move--;
            if (pad.dpad.down.wasPressedThisFrame) move++;
        }

        int stick = ReadStickDirection(_stickDir);
        if (stick != 0 && stick != _stickDir) move += stick;   // only on the frame the stick GOES up or down
        _stickDir = stick;

        return move;
#else
        return 0;
#endif
    }

    // Which way the left stick is pushed: -1 up, +1 down, 0 neutral. It has to pass stickThreshold to count and drop under half of it
    // to let go, so a stick resting near the line can't flicker. 'previous' is last frame's answer.
    private int ReadStickDirection(int previous)
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers) return 0;

        float y = 0f;
        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            float v = pad.leftStick.ReadValue().y;
            if (Mathf.Abs(v) > Mathf.Abs(y)) y = v;
        }

        if (y >= stickThreshold) return -1;    // pushed up = the button above
        if (y <= -stickThreshold) return 1;    // pushed down = the button below
        if (Mathf.Abs(y) < stickThreshold * 0.5f) return 0;
        return previous;
#else
        return 0;
#endif
    }

    // True on the frame a confirm button (South by default) goes down on any connected controller.
    private bool PadConfirmPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers || confirmButtons == null) return false;

        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            for (int b = 0; b < confirmButtons.Length; b++)
            {
                ButtonControl control = ControllerSlots.Control(pad, confirmButtons[b]);
                if (control != null && control.wasPressedThisFrame) return true;
            }
        }
#endif
        return false;
    }

    // The keyboard hint, plus a controller line while a pad is plugged in.
    private void RefreshHint()
    {
        string text = hintText;
#if ENABLE_INPUT_SYSTEM
        if (useControllers && ControllerSlots.ConnectedCount > 0 && !string.IsNullOrEmpty(controllerHintText))
        {
            text += "\n" + controllerHintText;
        }
#endif
        if (_hintLabel != null) _hintLabel.text = text;
    }

    private int ButtonUnderPointer(Vector2 screenPoint)
    {
        for (int i = 0; i < ButtonCount; i++)
        {
            // Screen Space - Overlay canvas, so no camera.
            if (RectTransformUtility.RectangleContainsScreenPoint(_buttonRects[i], screenPoint, null)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Sets where Retry puts this player back. StageLauncher calls it after it has moved the players to the stage that was picked
    /// in the menu, so Retry goes back to THAT stage's start and not to wherever the scene happened to begin.
    /// </summary>
    public void SetStartPosition(int playerNumber, Vector2 position)
    {
        if (playerNumber < 1 || playerNumber > MaxPlayers) return;
        _startPositions[playerNumber] = position;
        _hasStart[playerNumber] = true;
    }

    // Remembers where each player stood the first time we saw them. Not while a floor change is moving them around.
    private void RecordStartPositions()
    {
        if (FloorController.IsChangingFloor) return;

        for (int i = 0; i < CoopHealth.Players.Count; i++)
        {
            CoopHealth p = CoopHealth.Players[i];
            if (p == null) continue;

            int n = p.PlayerNumber;
            if (n < 1 || n > MaxPlayers || _hasStart[n]) continue;

            _startPositions[n] = p.transform.position;
            _hasStart[n] = true;
        }
    }

    // ------------------------------------------------------------ showing the screen

    private IEnumerator ShowRoutine()
    {
        // A floor change that is mid-way finishes first (a hit can land just as the doors close). Never for more than a few seconds,
        // so a floor change that gets stuck can't keep the game-over screen away for ever.
        float waitedForFloor = 0f;
        while (FloorController.IsChangingFloor && waitedForFloor < MaxFloorChangeWait)
        {
            waitedForFloor += Time.unscaledDeltaTime;
            yield return null;
        }
        if (FloorController.IsChangingFloor)
        {
            Debug.LogWarning("LightsOutScreen: a floor change was still running after " + MaxFloorChangeWait + " seconds, so the screen is shown anyway.", this);
        }

        // Let the last player's fall be seen before it goes dark.
        if (delayBeforeFade > 0f) yield return new WaitForSecondsRealtime(delayBeforeFade);

        SaveAndFreeCursor();
        BlockPausing(true);

        _blackGroup.alpha = 0f;
        _contentGroup.alpha = 1f;
        _titleGroup.alpha = 0f;
        _menuGroup.alpha = 0f;
        _titleRect.localScale = Vector3.one;
        SetSelected(0);
        RefreshHint();
        _root.SetActive(true);

        yield return Fade(_blackGroup, 0f, 1f, fadeInTime);

        FreezeWorld();

        yield return PopTitle();
        yield return Fade(_menuGroup, 0f, 1f, menuFadeTime);

        _lastMouse = Input.mousePosition;
        _stickDir = ReadStickDirection(0);   // a stick already held up / down when the menu opens counts as "already there"
        _acceptAfter = Time.unscaledTime + acceptDelay;
        _menuReady = true;
    }

    // The banner fades in while settling from slightly too big to its normal size.
    private IEnumerator PopTitle()
    {
        for (float t = 0f; t < titleFadeTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / titleFadeTime);
            float eased = 1f - (1f - k) * (1f - k);
            _titleGroup.alpha = k;
            _titleRect.localScale = Vector3.one * Mathf.Lerp(1.15f, 1f, eased);
            yield return null;
        }
        _titleGroup.alpha = 1f;
        _titleRect.localScale = Vector3.one;
    }

    // Unscaled time, so the fades still work with Time.timeScale at 0.
    private static IEnumerator Fade(CanvasGroup group, float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        group.alpha = to;
    }

    // ------------------------------------------------------------ the three buttons

    private void SetSelected(int index)
    {
        _selected = index;
        for (int i = 0; i < ButtonCount; i++)
        {
            bool on = i == index;
            _buttonImages[i].color = on ? selectedButtonColor : buttonColor;
            _buttonLabels[i].color = on ? selectedTextColor : buttonTextColor;
            _buttonRects[i].localScale = on ? Vector3.one * 1.06f : Vector3.one;
        }
    }

    private void Activate(int index)
    {
        if (!_menuReady) return;
        _menuReady = false;   // one choice only

        if (index == 0)
        {
            StartCoroutine(RetryRoutine());
        }
        else if (index == 1)
        {
            StageSelection.ReturnToStageSelect = true;   // the main menu opens straight on its stage list
            if (!LeaveTo(levelSelectSceneName, "LevelSelect", "Level Select")) _menuReady = true;
        }
        else
        {
            if (!LeaveTo(mainMenuSceneName, "MainMenu", "Main Menu")) _menuReady = true;
        }
    }

    // ------------------------------------------------------------ Retry

    private IEnumerator RetryRoutine()
    {
        yield return Fade(_contentGroup, 1f, 0f, 0.25f);

        // Behind the black screen: everyone back on their feet, back on their spot, the floor's foes started over.
        ReviveTeam();
        CoopDowned.ResetAllForRetry();   // knocked-down / blown-up players stand up again (gray, tipped over, hidden, locked all undone)
        LockPlayers(true);               // ...but nobody may move until the screen is clear (standing up unlocks them)
        yield return null;   // one frame for whatever listens to the health change (animations, colliders) to catch up
        PlaceTeam();

        FloorManager manager = FindAnyObjectByType<FloorManager>();
        if (manager != null) manager.RestartFloor();
        else Debug.LogWarning("LightsOutScreen: there is no FloorManager in the scene, so the floor's foes were not started over.", this);

        ThawTime();   // physics and the camera settle while it is still black; the foes stay frozen until the screen is clear
        if (holdTime > 0f) yield return new WaitForSecondsRealtime(holdTime);

        yield return Fade(_blackGroup, 1f, 0f, fadeOutTime);

        _root.SetActive(false);
        ReleaseWorld();
    }

    // Every player that counts gets back to full health. SetHealth also tells the health bars.
    private void ReviveTeam()
    {
        CoopHealth[] all = CoopHealth.Players.ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            CoopHealth hp = all[i];
            if (hp == null || !CoopRules.Counts(hp.PlayerNumber)) continue;
            hp.SetHealth(hp.MaxHealth);
        }
    }

    // Floor 1 (room 0): back to where each player spawned. Any other floor: THEIR OWN point from FloorController's Floors list.
    private void PlaceTeam()
    {
        int floor = FloorController.CurrentFloor;
        bool firstFloor = floor <= 1;

        CoopHealth[] all = CoopHealth.Players.ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            CoopHealth hp = all[i];
            if (hp == null) continue;

            int n = hp.PlayerNumber;
            Vector2 spot;
            bool found = firstFloor
                ? TryStartPosition(n, out spot) || TryFloorSpot(floor, n, out spot)
                : TryFloorSpot(floor, n, out spot) || TryStartPosition(n, out spot);

            if (!found)
            {
                if (!_warnedNoSpot)
                {
                    _warnedNoSpot = true;
                    Debug.LogWarning("LightsOutScreen: no spawn spot found for Player " + n + " on floor " + floor +
                                     " (nothing in FloorController's Floors list for them, and no start position was recorded), so they were not moved.", this);
                }
                continue;
            }
            MovePlayer(hp, spot);
        }

        Physics2D.SyncTransforms();
    }

    private bool TryStartPosition(int playerNumber, out Vector2 position)
    {
        if (playerNumber >= 1 && playerNumber <= MaxPlayers && _hasStart[playerNumber])
        {
            position = _startPositions[playerNumber];
            return true;
        }
        position = Vector2.zero;
        return false;
    }

    private bool TryFloorSpot(int floor, int playerNumber, out Vector2 position)
    {
        if (_floorController == null) _floorController = FindAnyObjectByType<FloorController>();
        if (_floorController != null && _floorController.TryGetSpawnPoint(floor, playerNumber, out position)) return true;

        position = Vector2.zero;
        return false;
    }

    // Same move FloorController uses: the Rigidbody2D and the transform, with the speed zeroed.
    private static void MovePlayer(CoopHealth player, Vector2 position)
    {
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body == null) body = player.GetComponentInParent<Rigidbody2D>();

        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
        }
        Transform t = player.transform;
        t.position = new Vector3(position.x, position.y, t.position.z);
    }

    // ------------------------------------------------------------ Level Select and Menu

    // Returns false (and shows the menu again) if no scene of that name can be loaded.
    private bool LeaveTo(string wanted, string alternative1, string alternative2)
    {
        string scene = FindScene(wanted, alternative1, alternative2);
        if (scene == null)
        {
            Debug.LogError("LightsOutScreen: the scene '" + wanted + "' can't be loaded. Check the scene name spelling and that the scene " +
                           "is in the Scenes In Build list (File > Build Profiles).", this);
            return false;
        }
        if (scene != wanted)
        {
            Debug.LogWarning("LightsOutScreen: '" + wanted + "' isn't in the build, so '" + scene + "' is used. Set the scene name to match.", this);
        }

        ReleaseWorld();           // time back to normal and foes un-frozen, so the next scene doesn't start frozen
        GameReset.ResetRun();     // score back to 0 and player health back to full, same as the pause menu's Quit
        SceneManager.LoadScene(scene);
        return true;
    }

    private static string FindScene(string a, string b, string c)
    {
        string[] candidates = { a, b, c };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrEmpty(candidates[i]) && Application.CanStreamedLevelBeLoaded(candidates[i])) return candidates[i];
        }
        return null;
    }

    // ------------------------------------------------------------ freezing and putting everything back

    private void FreezeWorld()
    {
        FoeController.Frozen = true;   // foes, sword, trails and explosions all obey this
        _foesFrozen = true;

        if (freezeTime && !_timeFrozen)
        {
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            _timeFrozen = true;
        }
    }

    private void ThawTime()
    {
        if (!_timeFrozen) return;
        _timeFrozen = false;
        Time.timeScale = _savedTimeScale;
    }

    // Puts EVERYTHING back and ends the screen. Safe to call twice.
    private void ReleaseWorld()
    {
        ThawTime();

        if (_foesFrozen)
        {
            _foesFrozen = false;
            FoeController.Frozen = false;
        }

        LockPlayers(false);
        BlockPausing(false);
        RestoreCursor();

        _menuReady = false;
        IsActive = false;
        closedFrame = Time.frameCount;
    }

    private static void LockPlayers(bool locked)
    {
        for (int n = 1; n <= MaxPlayers; n++)
        {
            PlayerInputSource source = PlayerInputSource.Get(n);
            if (source != null) source.Locked = locked;
        }
    }

    // PauseManager (if the scene has one) must not open its menu over this screen. Pausing is put back as it was.
    private void BlockPausing(bool block)
    {
        PauseManager pause = PauseManager.Instance;

        if (block)
        {
            if (pause == null || _pauseChanged) return;
            _pauseWasAllowed = pause.allowPausing;
            pause.allowPausing = false;
            _pauseChanged = true;
        }
        else if (_pauseChanged)
        {
            _pauseChanged = false;
            if (pause != null) pause.allowPausing = _pauseWasAllowed;
        }
    }

    private void SaveAndFreeCursor()
    {
        if (_cursorSaved) return;

        _savedLock = Cursor.lockState;
        _savedCursorVisible = Cursor.visible;
        _cursorSaved = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreCursor()
    {
        if (!_cursorSaved) return;
        _cursorSaved = false;

        Cursor.lockState = _savedLock;
        Cursor.visible = _savedCursorVisible;
    }

    // ------------------------------------------------------------ building the screen

    //  Canvas
    //   LightsOutRoot
    //     Black          fades to black first
    //     Content        fades out on Retry
    //       Title        the LIGHTS OUT banner
    //       Menu         the three buttons and the hint
    private void BuildUi()
    {
        var canvasObject = new GameObject("LightsOutCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = NewRect("LightsOutRoot", canvasObject.transform);
        Stretch(root);
        _root = root.gameObject;

        // Stretched to the canvas, then pushed out blackOverscan canvas units past EVERY edge. It only has to hide the screen, so it is far
        // bigger than any screen, window size or camera view (split-screen viewports, letterboxing and ultrawide included).
        RectTransform black = NewRect("Black", root);
        Stretch(black);
        float overscan = Mathf.Max(0f, blackOverscan);
        black.offsetMin = new Vector2(-overscan, -overscan);
        black.offsetMax = new Vector2(overscan, overscan);
        black.gameObject.AddComponent<Image>().color = Color.black;   // also swallows clicks meant for the game behind it
        _blackGroup = black.gameObject.AddComponent<CanvasGroup>();

        RectTransform content = NewRect("Content", root);
        Stretch(content);
        _contentGroup = content.gameObject.AddComponent<CanvasGroup>();

        // The banner.
        _titleRect = NewRect("Title", content);
        Place(_titleRect, new Vector2(0f, 270f), new Vector2(1100f, 240f));
        Image plate = _titleRect.gameObject.AddComponent<Image>();
        plate.color = titlePlateColor;
        plate.raycastTarget = false;
        _titleGroup = _titleRect.gameObject.AddComponent<CanvasGroup>();

        TextMeshProUGUI title = NewText("Text", _titleRect, titleText, 150f, Vector2.zero, Vector2.zero);
        Stretch(title.rectTransform);
        title.fontStyle = FontStyles.Bold;
        title.color = titleTextColor;

        // The three buttons.
        RectTransform menu = NewRect("Menu", content);
        Stretch(menu);
        _menuGroup = menu.gameObject.AddComponent<CanvasGroup>();

        string[] labels = { retryText, levelSelectText, menuText };
        for (int i = 0; i < ButtonCount; i++)
        {
            RectTransform rect = NewRect("Button " + labels[i], menu);
            Place(rect, new Vector2(0f, 50f - i * 130f), new Vector2(640f, 100f));

            Image image = rect.gameObject.AddComponent<Image>();
            TextMeshProUGUI label = NewText("Label", rect, labels[i], 52f, Vector2.zero, Vector2.zero);
            Stretch(label.rectTransform);
            label.fontStyle = FontStyles.Bold;

            _buttonRects[i] = rect;
            _buttonImages[i] = image;
            _buttonLabels[i] = label;
        }

        TextMeshProUGUI hint = NewText("Hint", menu, hintText, 30f, new Vector2(0f, -350f), new Vector2(1400f, 110f));   // two lines when a controller is plugged in
        _hintLabel = hint;
        hint.color = new Color(1f, 1f, 1f, 0.55f);

        SetSelected(0);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // Anchored to the middle of its parent, offset by position.
    private static void Place(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float fontSize, Vector2 position, Vector2 size)
    {
        RectTransform rect = NewRect(name, parent);
        Place(rect, position, size);

        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }
}
