using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
/// The pause menu. Put this on an object that is ALWAYS active in the dungeon scene (the same empty GameObject as FloorController /
/// LightsOutScreen is fine). There is nothing to wire up: the dark overlay, the title and every row are built by this script.
///
///   P or Esc  (or Start on a controller)  pauses. While paused they go BACK one page, and resume from the first page.
///
///   PAUSED                  Resume
///                           Graphics         -> page: Resolution / Width / Height / Display / Apply / Back
///                           Music & Sound    -> page: Music / Shuffle / Track / Sound Effects / Back
///                           Level Select     resets the run (GameReset.ResetRun) and loads the Level Select scene
///                           Main Menu        resets the run and loads the Main Menu scene
///                           Quit Game        closes the game (stops Play mode in the Editor)
///   Level Select, Main Menu and Quit Game ask "Are you sure?" first (Confirm Leaving). "No" is the row that starts highlighted.
///
/// HOW TO USE IT. Choose a row with  W / S  or  Up / Down  (they wrap round), change a setting with  A / D  or  Left / Right,  and press
/// Enter or Space to use a row. Or just click: the mouse highlights whatever it moves over, a click on the "<" or ">" end of a row steps it,
/// a click anywhere else on a row uses it. A CONTROLLER works too: D-pad or left stick to choose and change, South (A / Cross) to use a row,
/// East (B / Circle) or Start to go back. ANY connected controller can drive the menu. All keys are lists in the Inspector.
///
/// GRAPHICS
///   Resolution   steps through the sizes the screen supports (Screen.resolutions, or the Fallback Resolutions list when that is empty).
///                It reads "Custom" when Width / Height are not one of them.
///   Width/Height YOUR OWN size. A / D steps it by Custom Step; Enter or a click lets you TYPE a number (digits and Backspace, Enter to
///                confirm, Esc to cancel). It is kept between Min and Max Width / Height.
///   Display      Fullscreen (Borderless) / Fullscreen (Exclusive) / Windowed.
///   Apply        sets the screen to the chosen size and mode. Nothing changes until Apply is pressed, and leaving the page without it throws
///                the changes away. Builds remember the last size by themselves; the Editor's Game view ignores Screen.SetResolution.
///
/// MUSIC & SOUND (changes happen the moment you make them, there is no Apply)
///   Music        on / off.
///   Shuffle      on = random songs, never the same one twice in a row. Off = the chosen Track repeats.
///   Track        which song (1 = the first). A / D steps through them; Enter or a click lets you TYPE the number. A number past the end picks
///                the LAST song, and 0 means Shuffle. Choosing a track turns Shuffle off.
///   Sound Effects on / off (GameSettings.SoundOn, which LightsOutScreen's sounds and this menu's own sounds follow).
///   This drives MusicManager (fades, crossfades) and MusicPlayer (through GameSettings) - whichever of them is in the scene, or both.
///
/// SOUND OF THE MENU (optional, follows Sound Effects, keeps playing while the game is frozen): Select Clip plays when the highlighted
/// row changes, Choose Clip when a setting is changed or a row is used, Menu Switch Clip when Level Select or Main Menu is picked (the scene
/// waits for it, up to Menu Switch Max Wait seconds). With Pause World Sound on, every other sound is paused while the menu is up; the music
/// keeps playing so you can hear what you are choosing.
///
/// What freezes while paused:
///   - Time.timeScale goes to 0: physics, animations, particles and anything else that runs on game time.
///   - FoeController.Frozen is set: foes, sword swings, fire trails, the sparks on the blade and the death explosions all stop.
///   - every player's input is locked (only the ones that were not already locked, and only those are unlocked again), and the mouse
///     cursor is freed. All of it is put back on resume.
///   - FloorController.BlocksInput turns true (it includes this pause), so the player and the notebook keys hold still.
///
/// It does NOT open (the key does nothing) when:
///   - the stairs menu is up or a floor change is running. P / Esc close the stairs menu first (FloorController does that); pressing again
///     then opens this pause.
///   - the Lights Out screen is up.
///   - the notebook (MenuController) is open, or was closed on this very press.
///   - the player is dead.
///
/// Other scripts can read:  PauseController.IsPaused,  PauseController.BlocksInput.   They can call:  Pause(),  Resume(),  QuitToMainMenu().
///
/// NEEDS (small additions in the files that came with this one):
///   MusicManager.cs   gets SelectTrack, SetShuffle, TrackCount and SelectedTrack.
///   MusicPlayer.cs    one line in Awake (ignoreListenerPause), so its music keeps playing while the game's other sounds are paused.
///   GameSettings.cs   the "settings glue" section near the bottom of this file is the ONLY place the menu writes it: GameSettings.PendingMusicOn,
///                     PendingTrackMode and PendingSoundOn, then GameSettings.Apply(). If your GameSettings names those differently, fix those few lines.
///   PlayerInputSource.cs (PadButton and ControllerSlots) for the controller part, with the Input System package and Project Settings > Player >
///                     Active Input Handling = "Both". Without it the controller code is left out and the keyboard and mouse still work.
/// Keep only ONE pause in the scene: PauseManager.cs can stay in the project (LightsOutScreen compiles against it) but do not add it as well.
/// </summary>
[DefaultExecutionOrder(-200)]   // runs before FloorController (-100), the player and the sword, so P / Esc are claimed first
public class PauseController : MonoBehaviour
{
    private const int MaxPlayers = 4;
    private const int MaxRows = 6;              // the longest page (Main and Graphics) has six rows
    private const int MaxTypedDigits = 9;       // fits an int, so typing can never overflow

    private const float FirstRowY = 260f;       // canvas units (the canvas is 1920 x 1080)
    private const float RowSpacing = 104f;
    private const float RowWidth = 900f;
    private const float RowHeight = 84f;
    private const float ArrowZone = 0.15f;      // the outer 15% at each end of a Choice / Number row is the "<" and ">" click zone

    private enum Page { Main, Graphics, Music, Confirm }
    private enum RowKind { Button, Toggle, Choice, Number }
    private enum LeaveAction { LevelSelect, MainMenu, Quit }

    private enum RowId
    {
        // Main
        Resume, OpenGraphics, OpenMusic, LevelSelect, MainMenu, QuitGame,
        // Graphics
        Resolution, Width, Height, DisplayMode, ApplyGraphics,
        // Music & Sound
        Music, Shuffle, Track, SoundEffects,
        // Confirm
        ConfirmYes,
        // Graphics, Music & Confirm
        Back
    }

    private static readonly RowId[] MainRows = { RowId.Resume, RowId.OpenGraphics, RowId.OpenMusic, RowId.LevelSelect, RowId.MainMenu, RowId.QuitGame };
    private static readonly RowId[] GraphicsRows = { RowId.Resolution, RowId.Width, RowId.Height, RowId.DisplayMode, RowId.ApplyGraphics, RowId.Back };
    private static readonly RowId[] MusicRows = { RowId.Music, RowId.Shuffle, RowId.Track, RowId.SoundEffects, RowId.Back };
    private static readonly RowId[] ConfirmRows = { RowId.Back, RowId.ConfirmYes };   // "No" first, so a double tap of Enter can't leave

    private static readonly FullScreenMode[] DisplayModes =
    {
        FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed
    };

    // ------------------------------------------------------------ inspector

    [Header("Keys")]
    [Tooltip("Pauses and resumes. While the menu is up it also goes BACK one page (and resumes from the first page).")]
    [SerializeField] private KeyCode pauseKey = KeyCode.P;
    [Tooltip("Second key that pauses, resumes and goes back. None = off.")]
    [SerializeField] private KeyCode altPauseKey = KeyCode.Escape;
    [SerializeField] private KeyCode[] upKeys = { KeyCode.W, KeyCode.UpArrow };
    [SerializeField] private KeyCode[] downKeys = { KeyCode.S, KeyCode.DownArrow };
    [Tooltip("Change the highlighted setting (step it down).")]
    [SerializeField] private KeyCode[] leftKeys = { KeyCode.A, KeyCode.LeftArrow };
    [Tooltip("Change the highlighted setting (step it up).")]
    [SerializeField] private KeyCode[] rightKeys = { KeyCode.D, KeyCode.RightArrow };
    [Tooltip("Uses the highlighted row. Clicking a row uses it too.")]
    [SerializeField] private KeyCode[] confirmKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space };

    [Header("Controller")]
    [Tooltip("Lets any connected controller drive the menu: D-pad or left stick to choose and change, a confirm button to use a row, East or Start to go back, " +
             "and Start pauses. Needs the Input System package (Active Input Handling = Both).")]
    [SerializeField] private bool useControllers = true;
    [Tooltip("Uses the highlighted row. By POSITION, so it fits every pad: South = A (Xbox) / Cross (PlayStation) / B (Switch).")]
    [SerializeField] private PadButton[] confirmButtons = { PadButton.South };
    [Tooltip("How far the left stick must be pushed to move the highlight or change a setting one step. It has to come back to the middle before it steps again.")]
    [SerializeField, Range(0.2f, 0.95f)] private float stickThreshold = 0.6f;

    [Header("Scenes")]
    [Tooltip("Name of the Level Select scene (must be in Scenes In Build). Capitals and spaces don't matter, and \"LevelSelect\" / \"Level Select\" are tried if this can't be found.")]
    [SerializeField] private string levelSelectSceneName = "LevelSelect";
    [Tooltip("Name of the Main Menu scene (must be in Scenes In Build). Capitals and spaces don't matter, so \"Mainmenu\", \"MainMenu\" and \"Main Menu\" all find the same scene.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [Tooltip("ON: Level Select, Main Menu and Quit Game ask \"Are you sure?\" first, because they end the run. OFF: they act at once.")]
    [SerializeField] private bool confirmLeaving = true;

    [Header("Graphics")]
    [Tooltip("ON: the Resolution row lists the sizes this screen supports (Screen.resolutions). OFF, or if that list is empty (some platforms, the Editor), the Fallback list is used.")]
    [SerializeField] private bool useSystemResolutions = true;
    [SerializeField] private Vector2Int[] fallbackResolutions =
    {
        new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900),
        new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160)
    };
    [Tooltip("Pixels one A / D press (or one click on an arrow) moves Width and Height. To jump further, type the number.")]
    [SerializeField, Min(1)] private int customStep = 10;
    [SerializeField, Min(160)] private int minWidth = 640;
    [SerializeField, Min(90)] private int minHeight = 360;
    [SerializeField, Min(160)] private int maxWidth = 7680;
    [SerializeField, Min(90)] private int maxHeight = 4320;

    [Header("Music")]
    [Tooltip("Only used when neither a MusicManager nor a MusicPlayer exists in the scene, so the Track row still has a number to count up to.")]
    [SerializeField, Min(1)] private int fallbackTrackCount = 7;

    [Header("Sound (optional, follows the Sound Effects setting)")]
    [Tooltip("ON: every other sound is paused while the menu is up and carries on after (AudioListener.pause). The music is NOT paused " +
             "(MusicManager and MusicPlayer ignore it), so the music settings can be heard. OFF: only time is frozen, sounds keep playing.")]
    [SerializeField] private bool pauseWorldSound = true;
    [Tooltip("Plays every time the highlighted row changes (keys, controller or mouse). The toggle sound.")]
    [SerializeField] private AudioClip selectClip;
    [SerializeField, Range(0f, 1f)] private float selectVolume = 1f;
    [Tooltip("Plays when a setting is changed or a row is used.")]
    [SerializeField] private AudioClip chooseClip;
    [SerializeField, Range(0f, 1f)] private float chooseVolume = 1f;
    [Tooltip("Plays when Level Select or Main Menu is picked, as the screen goes from this menu to another one.")]
    [SerializeField] private AudioClip menuSwitchClip;
    [SerializeField, Range(0f, 1f)] private float menuSwitchVolume = 1f;
    [Tooltip("The next scene waits for the between-menus sound to finish, but never longer than this many seconds (real time), so it is not cut off by the loading.")]
    [SerializeField, Min(0f)] private float menuSwitchMaxWait = 1.5f;

    [Header("Timing (seconds, real time)")]
    [Tooltip("Rows ignore Enter / Space / clicks for this long after the menu opens, so a key that was being mashed can't use one.")]
    [SerializeField, Min(0f)] private float acceptDelay = 0.15f;

    [Header("Text")]
    [SerializeField] private string pausedTitle = "PAUSED";
    [SerializeField] private string graphicsTitle = "GRAPHICS";
    [SerializeField] private string musicTitle = "MUSIC & SOUND";
    [SerializeField] private string confirmTitle = "ARE YOU SURE?";
    [SerializeField] private string resumeText = "Resume";
    [SerializeField] private string graphicsText = "Graphics";
    [SerializeField] private string musicText = "Music & Sound";
    [SerializeField] private string levelSelectText = "Level Select";
    [SerializeField] private string mainMenuText = "Main Menu";
    [SerializeField] private string quitText = "Quit Game";
    [SerializeField] private string backText = "Back";
    [SerializeField] private string confirmNoText = "No, go back";
    [SerializeField] private string applyText = "Apply";
    [SerializeField] private string appliedText = "Applied!";
    [SerializeField] private string customText = "Custom";
    [SerializeField] private string onText = "On";
    [SerializeField] private string offText = "Off";
    [SerializeField] private string hintText = "W / S to choose,  A / D to change,  Enter or click to select,  Esc / P to go back";
    [Tooltip("Shown on a second line under the hint while a controller is plugged in.")]
    [SerializeField] private string controllerHintText = "D-pad or Left Stick to choose and change,  A / Cross to select,  B / Circle or Start to go back";
    [SerializeField] private string typingHintText = "Type a number,  Enter to confirm,  Esc to cancel";
    [SerializeField] private string confirmHintText = "Leaving ends this run: your score and progress in the level are lost.";

    [Header("Look")]
    [Tooltip("Draw order of the menu's canvas. Higher is drawn on top of other canvases (the Lights Out screen is 600).")]
    [SerializeField] private int canvasSortingOrder = 500;
    [Tooltip("How dark the screen goes behind the menu (0 = not at all, 1 = black).")]
    [SerializeField, Range(0f, 1f)] private float dimAmount = 0.65f;
    [SerializeField] private Color titlePlateColor = new Color(0.1f, 0.12f, 0.2f, 0.95f);
    [SerializeField] private Color titleTextColor = new Color(1f, 0.93f, 0.8f, 1f);
    [SerializeField] private Color buttonColor = new Color(0.16f, 0.16f, 0.18f, 1f);
    [SerializeField] private Color buttonTextColor = new Color(0.75f, 0.75f, 0.78f, 1f);
    [SerializeField] private Color selectedButtonColor = new Color(0.95f, 0.75f, 0.25f, 1f);
    [SerializeField] private Color selectedTextColor = new Color(0.1f, 0.07f, 0.02f, 1f);

    // ------------------------------------------------------------ public / shared

    /// <summary>True while the pause menu is showing.</summary>
    public static bool IsPaused { get; private set; }

    /// <summary>True while the pause is up, and for the rest of the frame in which it closed (so the same Esc or click
    /// can't also be read by the player, the sword or the notebook). FloorController.BlocksInput includes this.</summary>
    public static bool BlocksInput => IsPaused || closedFrame == Time.frameCount;

    private static int closedFrame = -1;

    // Static values survive between Play sessions when "Reload Domain" is switched off, so they are reset by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsPaused = false;
        closedFrame = -1;
    }

    // ------------------------------------------------------------ state

    private GameObject _root;
    private TMP_Text _titleLabel;
    private TMP_Text _hintLabel;
    private readonly RectTransform[] _rowRects = new RectTransform[MaxRows];
    private readonly Image[] _rowImages = new Image[MaxRows];
    private readonly TMP_Text[] _rowLabels = new TMP_Text[MaxRows];
    private readonly TMP_Text[] _rowLeft = new TMP_Text[MaxRows];
    private readonly TMP_Text[] _rowRight = new TMP_Text[MaxRows];

    private Page _page;
    private RowId[] _rows = new RowId[0];
    private int _rowCount;
    private int _selected = -1;
    private int _mainSelected;          // the row on the first page that opened the current page, so Back returns to it
    private bool _menuReady;
    private bool _leaving;              // Level Select / Main Menu / Quit was chosen: one choice only
    private float _acceptAfter;
    private Vector2 _lastMouse;
    private readonly int[] _stickDir = new int[2];   // left stick last frame, per axis: -1 up / left, +1 down / right, 0 neutral (a held stick is one step, not one per frame)
    private LeaveAction _pendingLeave;

    // Typing a number into Width, Height or Track.
    private bool _typing;
    private RowId _typingRow;
    private string _typeBuffer = "";

    // Graphics: what the rows show. Nothing reaches the screen until Apply.
    private Vector2Int[] _presets = new Vector2Int[0];
    private int _pendingWidth;
    private int _pendingHeight;
    private FullScreenMode _pendingMode;
    private float _appliedFlashUntil;   // the Apply row says "Applied!" until this time

    // Music & Sound: what the rows show (read when the page opens, written the moment a row changes).
    private bool _musicOn = true;
    private bool _shuffle = true;
    private bool _soundOn = true;
    private int _track = 1;             // 1-based. Remembered while shuffling, so turning Shuffle off returns to it

    // What we changed, so it can be put back.
    private float _previousTimeScale = 1f;
    private bool _weFroze;
    private bool _audioPaused;
    private bool _savedAudioPause;
    private bool _cursorSaved;
    private CursorLockMode _savedLock;
    private bool _savedCursorVisible;
    private readonly bool[] _lockedByUs = new bool[MaxPlayers + 1];

    private AudioSource _uiAudio;       // the menu sounds

    private bool _notebookWasOpen;
    private HealthComponent _playerHealth;
    private float _nextHealthSearch;

    // ------------------------------------------------------------ lifecycle

    private void Awake()
    {
        IsPaused = false;
        BuildUi();
        BuildAudio();
        _root.SetActive(false);
    }

    private void OnDestroy()
    {
        // Never leave the game frozen or locked behind, e.g. when another scene replaces this one.
        ReleaseWorld();
    }

    private void Update()
    {
        if (IsPaused) UpdateOpen();
        else UpdateClosed();
    }

    // ------------------------------------------------------------ main loop: menu closed

    private void UpdateClosed()
    {
        // The notebook counts as "busy" for one extra frame, so the Esc / P that CLOSES it can't also open the pause
        // (this works whichever of the two scripts runs first).
        bool notebookOpen = MenuController.IsOpen;
        bool notebookBusy = notebookOpen || _notebookWasOpen;
        _notebookWasOpen = notebookOpen;

        if (!PausePressed()) return;
        if (notebookBusy) return;
        if (FloorController.BlocksInput) return;     // stairs menu up / floor change running / one just closed this frame
        if (LightsOutScreen.BlocksInput) return;     // the game-over screen owns the keys
        if (PlayerIsDead()) return;

        Pause();
    }

    private bool PausePressed()
    {
        return Input.GetKeyDown(pauseKey) || Input.GetKeyDown(altPauseKey) || PadPressed(false);
    }

    private bool BackPressed()
    {
        return Input.GetKeyDown(pauseKey) || Input.GetKeyDown(altPauseKey) || PadPressed(true);
    }

    // The player is found through its movement script, like FloorController does. Only looked up when a pause is attempted.
    private bool PlayerIsDead()
    {
        // Co-op players register themselves, so no search is needed: the pause is blocked once every player that counts is dead.
        if (CoopHealth.Players.Count > 0) return CoopHealth.AllCountedDead;

        if (_playerHealth == null && Time.unscaledTime >= _nextHealthSearch)
        {
            _nextHealthSearch = Time.unscaledTime + 1f;

            var controller = FindAnyObjectByType<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>();
            if (controller != null)
            {
                _playerHealth = controller.GetComponentInParent<HealthComponent>();
                if (_playerHealth == null) _playerHealth = controller.GetComponentInChildren<HealthComponent>();
            }
        }
        return _playerHealth != null && _playerHealth.CurrentHealth <= 0f;
    }

    // ------------------------------------------------------------ main loop: menu open

    private void UpdateOpen()
    {
        // Something else (the notebook closing, say) may have put the time back: the pause keeps it at 0.
        if (Time.timeScale != 0f) Time.timeScale = 0f;

        if (_appliedFlashUntil > 0f && Time.unscaledTime >= _appliedFlashUntil)
        {
            _appliedFlashUntil = 0f;
            RefreshRows();
        }

        if (!_menuReady) return;

        if (_typing)
        {
            UpdateTyping();
            return;
        }

        if (BackPressed())
        {
            GoBack();
            return;
        }

        // Up / down: one step per press, wrapping round.
        int vertical = 0;
        if (AnyDown(upKeys)) vertical--;
        if (AnyDown(downKeys)) vertical++;
        vertical += PadStep(0);
        vertical = Mathf.Clamp(vertical, -1, 1);
        if (vertical != 0) SetSelected((_selected + vertical + _rowCount) % _rowCount);

        // Left / right: changes the highlighted setting. (PadStep runs every frame so the stick's state stays right.)
        int horizontal = 0;
        if (AnyDown(leftKeys)) horizontal--;
        if (AnyDown(rightKeys)) horizontal++;
        horizontal += PadStep(1);
        horizontal = Mathf.Clamp(horizontal, -1, 1);

        // The mouse only takes over the highlight when it actually moves, so a resting cursor doesn't fight the keys.
        Vector2 mouse = Input.mousePosition;
        bool mouseMoved = (mouse - _lastMouse).sqrMagnitude > 1f;
        _lastMouse = mouse;
        int hover = RowUnderPointer(mouse);
        if (hover >= 0 && mouseMoved) SetSelected(hover);

        if (Time.unscaledTime < _acceptAfter) return;

        if (horizontal != 0)
        {
            StepRow(_rows[_selected], horizontal);
        }
        else if (hover >= 0 && Input.GetMouseButtonDown(0))
        {
            SetSelected(hover);
            ClickRow(hover, mouse);
        }
        else if (AnyDown(confirmKeys) || PadConfirmPressed())
        {
            ActivateRow(_rows[_selected]);
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
    // Every player's PlayerInputSource is Locked while the menu is up (so nobody can walk around behind it), and a locked source reports
    // "nothing pressed". So the pads are read here directly, through ControllerSlots (the same pad numbering the players use). Any connected
    // controller can drive the menu, not only the one a player happened to be using.

    // One step on an axis: axis 0 = vertical (-1 up, +1 down), axis 1 = horizontal (-1 left, +1 right). One step per D-pad press, and one
    // step per push of the left stick.
    private int PadStep(int axis)
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers) return 0;

        int move = 0;
        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            if (axis == 0)
            {
                if (pad.dpad.up.wasPressedThisFrame) move--;
                if (pad.dpad.down.wasPressedThisFrame) move++;
            }
            else
            {
                if (pad.dpad.left.wasPressedThisFrame) move--;
                if (pad.dpad.right.wasPressedThisFrame) move++;
            }
        }

        int stick = ReadStickDirection(axis, _stickDir[axis]);
        if (stick != 0 && stick != _stickDir[axis]) move += stick;   // only on the frame the stick GOES that way
        _stickDir[axis] = stick;

        return move;
#else
        return 0;
#endif
    }

    // Which way the left stick is pushed on an axis: -1 up / left, +1 down / right, 0 neutral. It has to pass stickThreshold to count and
    // drop under half of it to let go, so a stick resting near the line can't flicker. 'previous' is last frame's answer.
    private int ReadStickDirection(int axis, int previous)
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers) return 0;

        float value = 0f;
        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            Vector2 stick = pad.leftStick.ReadValue();
            float v = axis == 0 ? -stick.y : stick.x;   // stick up is +y, but "up" is -1 here
            if (Mathf.Abs(v) > Mathf.Abs(value)) value = v;
        }

        if (value >= stickThreshold) return 1;
        if (value <= -stickThreshold) return -1;
        if (Mathf.Abs(value) < stickThreshold * 0.5f) return 0;
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

    // Start goes down on any controller (pausing and going back). With includeEast, East (B / Circle) counts too (going back only,
    // so the attack / dodge button can't pause the game).
    private bool PadPressed(bool includeEast)
    {
#if ENABLE_INPUT_SYSTEM
        if (!useControllers) return false;

        for (int slot = 0; slot < ControllerSlots.MaxSlots; slot++)
        {
            Gamepad pad = ControllerSlots.Get(slot);
            if (pad == null) continue;

            if (pad.startButton.wasPressedThisFrame) return true;
            if (includeEast && pad.buttonEast.wasPressedThisFrame) return true;
        }
#endif
        return false;
    }

    // ------------------------------------------------------------ pausing

    /// <summary>Pauses the game and shows the menu. P / Esc / Start call this for you.</summary>
    public void Pause()
    {
        if (IsPaused) return;

        if (_root == null)
        {
            Debug.LogError("PauseController: the pause menu's UI was never built (check the Console for an error from Awake, " +
                           "usually TextMeshPro's essential resources not being imported).", this);
            return;
        }

        IsPaused = true;
        _leaving = false;

        _previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        FoeController.Frozen = true;   // foes, sword, trails, sparks and explosions all obey this
        _weFroze = true;

        if (pauseWorldSound)
        {
            _savedAudioPause = AudioListener.pause;
            AudioListener.pause = true;
            _audioPaused = true;
        }

        LockPlayers(true);
        SaveAndFreeCursor();

        ReadAudioSettings();

        _lastMouse = Input.mousePosition;
        _stickDir[0] = ReadStickDirection(0, 0);   // a stick already held when the menu opens counts as "already there"
        _stickDir[1] = ReadStickDirection(1, 0);
        _acceptAfter = Time.unscaledTime + acceptDelay;

        _root.SetActive(true);
        ShowPage(Page.Main, 0);
        _menuReady = true;
    }

    /// <summary>Resumes the game and hides the menu. P / Esc / Start on the first page, and the Resume row, call this for you.</summary>
    public void Resume()
    {
        if (!IsPaused) return;
        ReleaseWorld();
    }

    /// <summary>Goes straight to the Main Menu scene (no "Are you sure?").</summary>
    public void QuitToMainMenu()
    {
        if (!IsPaused) return;
        DoLeave(LeaveAction.MainMenu);
    }

    // Puts EVERYTHING back and closes the menu. closedFrame makes the player, the sword and the notebook ignore this frame's keys and
    // clicks. Safe to call twice.
    private void ReleaseWorld()
    {
        if (!IsPaused) return;

        IsPaused = false;
        closedFrame = Time.frameCount;
        _menuReady = false;
        _typing = false;

        Time.timeScale = _previousTimeScale;

        if (_weFroze)
        {
            _weFroze = false;
            FoeController.Frozen = false;
        }

        if (_audioPaused)
        {
            _audioPaused = false;
            AudioListener.pause = _savedAudioPause;
        }

        LockPlayers(false);
        RestoreCursor();

        if (_root != null) _root.SetActive(false);
    }

    // Locks every player's input, remembering which ones WE locked (a player already locked by something else, like being knocked down,
    // is left alone), and unlocks only those again.
    private void LockPlayers(bool locked)
    {
        for (int n = 1; n <= MaxPlayers; n++)
        {
            PlayerInputSource source = PlayerInputSource.Get(n);
            if (source == null) continue;

            if (locked)
            {
                if (source.Locked) continue;
                source.Locked = true;
                _lockedByUs[n] = true;
            }
            else if (_lockedByUs[n])
            {
                _lockedByUs[n] = false;
                source.Locked = false;
            }
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

    // ------------------------------------------------------------ pages

    private RowId[] RowsOf(Page page)
    {
        switch (page)
        {
            case Page.Graphics: return GraphicsRows;
            case Page.Music: return MusicRows;
            case Page.Confirm: return ConfirmRows;
            default: return MainRows;
        }
    }

    private string TitleOf(Page page)
    {
        switch (page)
        {
            case Page.Graphics: return graphicsTitle;
            case Page.Music: return musicTitle;
            case Page.Confirm: return confirmTitle;
            default: return pausedTitle;
        }
    }

    private static RowKind KindOf(RowId id)
    {
        switch (id)
        {
            case RowId.Resolution:
            case RowId.DisplayMode:
                return RowKind.Choice;

            case RowId.Width:
            case RowId.Height:
            case RowId.Track:
                return RowKind.Number;

            case RowId.Music:
            case RowId.Shuffle:
            case RowId.SoundEffects:
                return RowKind.Toggle;

            default:
                return RowKind.Button;
        }
    }

    private void ShowPage(Page page, int selected)
    {
        _page = page;
        _rows = RowsOf(page);
        _rowCount = _rows.Length;
        _typing = false;
        _typeBuffer = "";

        if (page == Page.Graphics) BeginGraphics();
        else if (page == Page.Music) ReadAudioSettings();

        for (int i = 0; i < MaxRows; i++) _rowRects[i].gameObject.SetActive(i < _rowCount);

        _titleLabel.text = TitleOf(page);
        _selected = -1;
        SetSelected(Mathf.Clamp(selected, 0, _rowCount - 1), true);
        RefreshHint();
        RefreshRows();
    }

    // Back one page, or resume from the first one.
    private void GoBack()
    {
        if (_page == Page.Main)
        {
            Resume();
            return;
        }

        PlayChooseSound();
        ShowPage(Page.Main, _mainSelected);
    }

    private void SetSelected(int index, bool silent = false)
    {
        bool changed = index != _selected;
        _selected = index;
        if (changed && !silent && _menuReady) PlaySelectSound();   // not while the page is being set up, and not once a choice has been made

        ApplySelectionColors();
    }

    private void ApplySelectionColors()
    {
        for (int i = 0; i < MaxRows; i++)
        {
            bool on = i == _selected;
            Color text = on ? selectedTextColor : buttonTextColor;

            _rowImages[i].color = on ? selectedButtonColor : buttonColor;
            _rowLabels[i].color = text;
            _rowLeft[i].color = text;
            _rowRight[i].color = text;
            _rowRects[i].localScale = on ? Vector3.one * 1.04f : Vector3.one;
        }
    }

    // Texts and arrows of the rows on the current page.
    private void RefreshRows()
    {
        for (int i = 0; i < _rowCount; i++)
        {
            RowId id = _rows[i];
            RowKind kind = KindOf(id);
            bool arrows = kind == RowKind.Choice || kind == RowKind.Number;

            _rowLabels[i].text = RowText(id);
            _rowLeft[i].gameObject.SetActive(arrows);
            _rowRight[i].gameObject.SetActive(arrows);
        }
        ApplySelectionColors();
    }

    private string RowText(RowId id)
    {
        switch (id)
        {
            case RowId.Resume: return resumeText;
            case RowId.OpenGraphics: return graphicsText;
            case RowId.OpenMusic: return musicText;
            case RowId.LevelSelect: return levelSelectText;
            case RowId.MainMenu: return mainMenuText;
            case RowId.QuitGame: return quitText;

            case RowId.Resolution: return "Resolution: " + ResolutionName();
            case RowId.Width: return "Width: " + NumberText(id, _pendingWidth.ToString());
            case RowId.Height: return "Height: " + NumberText(id, _pendingHeight.ToString());
            case RowId.DisplayMode: return "Display: " + ModeName(_pendingMode);
            case RowId.ApplyGraphics: return Time.unscaledTime < _appliedFlashUntil ? appliedText : applyText;

            case RowId.Music: return "Music: " + (_musicOn ? onText : offText);
            case RowId.Shuffle: return "Shuffle: " + (_shuffle ? onText : offText);
            case RowId.Track: return "Track: " + NumberText(id, _shuffle ? "-" : _track + " / " + TrackCount());
            case RowId.SoundEffects: return "Sound Effects: " + (_soundOn ? onText : offText);

            case RowId.ConfirmYes: return "Yes: " + LeaveActionText(_pendingLeave);
            case RowId.Back: return _page == Page.Confirm ? confirmNoText : backText;
        }
        return "";
    }

    private string LeaveActionText(LeaveAction action)
    {
        switch (action)
        {
            case LeaveAction.LevelSelect: return levelSelectText;
            case LeaveAction.MainMenu: return mainMenuText;
            default: return quitText;
        }
    }

    // The row's number, or what is being typed into it (with a "_" cursor) while it is being edited.
    private string NumberText(RowId id, string normal)
    {
        return _typing && _typingRow == id ? _typeBuffer + "_" : normal;
    }

    // The keyboard hint, plus a controller line while a pad is plugged in.
    private void RefreshHint()
    {
        if (_hintLabel == null) return;

        if (_typing)
        {
            _hintLabel.text = typingHintText;
            return;
        }

        if (_page == Page.Confirm)
        {
            _hintLabel.text = confirmHintText;
            return;
        }

        string text = hintText;
#if ENABLE_INPUT_SYSTEM
        if (useControllers && ControllerSlots.ConnectedCount > 0 && !string.IsNullOrEmpty(controllerHintText))
        {
            text += "\n" + controllerHintText;
        }
#endif
        _hintLabel.text = text;
    }

    // ------------------------------------------------------------ using the rows

    private int RowUnderPointer(Vector2 screenPoint)
    {
        for (int i = 0; i < _rowCount; i++)
        {
            // Screen Space - Overlay canvas, so no camera.
            if (RectTransformUtility.RectangleContainsScreenPoint(_rowRects[i], screenPoint, null)) return i;
        }
        return -1;
    }

    // 0 at the row's left edge, 1 at its right edge.
    private float FractionAcross(int row, Vector2 screenPoint)
    {
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rowRects[row], screenPoint, null, out local)) return 0.5f;
        return Mathf.InverseLerp(_rowRects[row].rect.xMin, _rowRects[row].rect.xMax, local.x);
    }

    // A click: on the "<" or ">" end of a Choice / Number row it steps, anywhere else it uses the row.
    private void ClickRow(int row, Vector2 screenPoint)
    {
        RowId id = _rows[row];
        RowKind kind = KindOf(id);

        if (kind == RowKind.Choice || kind == RowKind.Number)
        {
            float f = FractionAcross(row, screenPoint);
            if (f <= ArrowZone) { StepRow(id, -1); return; }
            if (f >= 1f - ArrowZone) { StepRow(id, 1); return; }
        }
        ActivateRow(id);
    }

    // Left / right (or a click on an arrow): changes the row's value one step. A toggle just flips either way.
    private void StepRow(RowId id, int dir)
    {
        switch (id)
        {
            case RowId.Resolution:
                StepResolution(dir);
                break;

            case RowId.Width:
                _pendingWidth = Mathf.Clamp(_pendingWidth + dir * customStep, minWidth, maxWidth);
                break;

            case RowId.Height:
                _pendingHeight = Mathf.Clamp(_pendingHeight + dir * customStep, minHeight, maxHeight);
                break;

            case RowId.DisplayMode:
                StepMode(dir);
                break;

            case RowId.Track:
                StepTrack(dir);
                break;

            case RowId.Music:
            case RowId.Shuffle:
            case RowId.SoundEffects:
                ActivateRow(id);
                return;

            default:
                return;   // a plain button has nothing to step
        }
        Chosen();
    }

    // Enter / Space / South / a click on the middle of a row.
    private void ActivateRow(RowId id)
    {
        switch (id)
        {
            case RowId.Resume:
                Resume();
                break;

            case RowId.OpenGraphics:
                _mainSelected = _selected;
                PlayChooseSound();
                ShowPage(Page.Graphics, 0);
                break;

            case RowId.OpenMusic:
                _mainSelected = _selected;
                PlayChooseSound();
                ShowPage(Page.Music, 0);
                break;

            case RowId.LevelSelect:
                AskToLeave(LeaveAction.LevelSelect);
                break;

            case RowId.MainMenu:
                AskToLeave(LeaveAction.MainMenu);
                break;

            case RowId.QuitGame:
                AskToLeave(LeaveAction.Quit);
                break;

            case RowId.Resolution:
            case RowId.DisplayMode:
                StepRow(id, 1);   // cycles forward, and round to the start
                break;

            case RowId.Width:
            case RowId.Height:
            case RowId.Track:
                BeginTyping(id);
                break;

            case RowId.ApplyGraphics:
                ApplyGraphics();
                break;

            case RowId.Music:
                _musicOn = !_musicOn;
                PushMusic();
                Chosen();
                break;

            case RowId.Shuffle:
                _shuffle = !_shuffle;
                PushMusic();
                Chosen();
                break;

            case RowId.SoundEffects:
                _soundOn = !_soundOn;
                PushGameSettings();
                Chosen();
                break;

            case RowId.ConfirmYes:
                DoLeave(_pendingLeave);
                break;

            case RowId.Back:
                GoBack();
                break;
        }
    }

    private void Chosen()
    {
        PlayChooseSound();
        RefreshRows();
    }

    // ------------------------------------------------------------ typing a number

    private void BeginTyping(RowId id)
    {
        _typing = true;
        _typingRow = id;
        _typeBuffer = "";

        PlayChooseSound();
        RefreshHint();
        RefreshRows();
    }

    // Digits and Backspace come in through Input.inputString. Enter, a click or South confirms; Esc, East or Start cancels.
    // (P is a letter, so it is ignored here, the same as the notebook ignores its hotkeys while a note is being typed.)
    private void UpdateTyping()
    {
        string typed = Input.inputString;
        bool changed = false;

        for (int i = 0; i < typed.Length; i++)
        {
            char c = typed[i];
            if (c == '\b')
            {
                if (_typeBuffer.Length > 0)
                {
                    _typeBuffer = _typeBuffer.Substring(0, _typeBuffer.Length - 1);
                    changed = true;
                }
            }
            else if (c >= '0' && c <= '9' && _typeBuffer.Length < MaxTypedDigits)
            {
                _typeBuffer += c;
                changed = true;
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0) || PadConfirmPressed())
        {
            CommitTyping();
        }
        else if (Input.GetKeyDown(KeyCode.Escape) || PadPressed(true))
        {
            CancelTyping();
        }
        else if (changed)
        {
            RefreshRows();
        }
    }

    // Nothing typed = nothing changes. (Track: 0 means Shuffle, and a number past the end means the last song.)
    private void CommitTyping()
    {
        RowId id = _typingRow;
        string text = _typeBuffer;

        _typing = false;
        _typeBuffer = "";

        if (text.Length > 0)
        {
            int n;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n)) n = int.MaxValue;
            ApplyTyped(id, n);
        }

        PlayChooseSound();
        RefreshHint();
        RefreshRows();
    }

    private void CancelTyping()
    {
        _typing = false;
        _typeBuffer = "";

        RefreshHint();
        RefreshRows();
    }

    private void ApplyTyped(RowId id, int n)
    {
        switch (id)
        {
            case RowId.Width:
                _pendingWidth = Mathf.Clamp(n, minWidth, maxWidth);
                break;

            case RowId.Height:
                _pendingHeight = Mathf.Clamp(n, minHeight, maxHeight);
                break;

            case RowId.Track:
                if (n <= 0)
                {
                    _shuffle = true;
                    PushMusic();
                }
                else
                {
                    SelectTrackNumber(n);
                }
                break;
        }
    }

    // ------------------------------------------------------------ graphics

    // The page opens showing what the screen is now. (Leaving it without Apply throws the changes away.)
    private void BeginGraphics()
    {
        BuildPresets();
        _pendingWidth = Screen.width;
        _pendingHeight = Screen.height;
        _pendingMode = Screen.fullScreenMode;
        _appliedFlashUntil = 0f;
    }

    private void BuildPresets()
    {
        var list = new List<Vector2Int>();

        if (useSystemResolutions)
        {
            Resolution[] all = Screen.resolutions;
            for (int i = 0; i < all.Length; i++) AddPreset(list, all[i].width, all[i].height);
        }

        if (list.Count == 0 && fallbackResolutions != null)
        {
            for (int i = 0; i < fallbackResolutions.Length; i++) AddPreset(list, fallbackResolutions[i].x, fallbackResolutions[i].y);
        }

        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        _presets = list.ToArray();
    }

    // Screen.resolutions repeats every size once per refresh rate, so the same size is only added once.
    private static void AddPreset(List<Vector2Int> list, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].x == width && list[i].y == height) return;
        }
        list.Add(new Vector2Int(width, height));
    }

    // Which preset Width x Height is right now, or -1 when it is a custom size.
    private int PresetIndex()
    {
        for (int i = 0; i < _presets.Length; i++)
        {
            if (_presets[i].x == _pendingWidth && _presets[i].y == _pendingHeight) return i;
        }
        return -1;
    }

    private string ResolutionName()
    {
        int index = PresetIndex();
        return index >= 0 ? _presets[index].x + " x " + _presets[index].y : customText;
    }

    private void StepResolution(int dir)
    {
        if (_presets.Length == 0) return;

        int index = PresetIndex();
        if (index >= 0) index = (index + dir + _presets.Length) % _presets.Length;
        else index = dir > 0 ? 0 : _presets.Length - 1;   // from a custom size: the first or the last preset

        _pendingWidth = _presets[index].x;
        _pendingHeight = _presets[index].y;
    }

    private void StepMode(int dir)
    {
        int index = System.Array.IndexOf(DisplayModes, _pendingMode);
        index = index >= 0 ? (index + dir + DisplayModes.Length) % DisplayModes.Length : 0;
        _pendingMode = DisplayModes[index];
    }

    private static string ModeName(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.FullScreenWindow: return "Fullscreen (Borderless)";
            case FullScreenMode.ExclusiveFullScreen: return "Fullscreen (Exclusive)";
            case FullScreenMode.Windowed: return "Windowed";
            default: return mode.ToString();
        }
    }

    private void ApplyGraphics()
    {
        _pendingWidth = Mathf.Clamp(_pendingWidth, minWidth, maxWidth);
        _pendingHeight = Mathf.Clamp(_pendingHeight, minHeight, maxHeight);

        Screen.SetResolution(_pendingWidth, _pendingHeight, _pendingMode);
        _appliedFlashUntil = Time.unscaledTime + 1.5f;

        Chosen();
    }

    // ------------------------------------------------------------ music and sound

    // How many songs there are to choose from: the MusicManager's playlist, else the MusicPlayer's, else the Inspector's fallback.
    private int TrackCount()
    {
        int count = 0;
        if (MusicManager.Instance != null) count = MusicManager.Instance.TrackCount;
        if (count <= 0 && MusicPlayer.Instance != null) count = MusicPlayer.Instance.TrackCount;
        if (count <= 0) count = fallbackTrackCount;
        return Mathf.Max(1, count);
    }

    // What the rows show: what is really applied right now, so the menu always opens on the truth.
    private void ReadAudioSettings()
    {
        MusicManager manager = MusicManager.Instance;
        if (manager != null)
        {
            _musicOn = manager.IsOn;
            _shuffle = manager.IsShuffling;
            _track = manager.SelectedTrack;
        }
        else
        {
            _musicOn = GameSettings.MusicOn;

            int mode = GameSettings.TrackMode;   // 0 = random
            _shuffle = mode <= 0;
            if (mode > 0) _track = mode;
        }

        _soundOn = GameSettings.SoundOn;
        _track = Mathf.Clamp(_track, 1, TrackCount());
    }

    private void StepTrack(int dir)
    {
        int count = TrackCount();
        SelectTrackNumber((_track - 1 + dir + count) % count + 1);
    }

    // Choosing a song turns Shuffle off and repeats that song. A number past the end is the last song.
    private void SelectTrackNumber(int number)
    {
        _track = Mathf.Clamp(number, 1, TrackCount());
        _shuffle = false;
        PushMusic();
    }

    // Sends Music / Shuffle / Track to whatever plays the music. Safe to call when nothing changed (both players ignore a repeat).
    private void PushMusic()
    {
        MusicManager manager = MusicManager.Instance;
        if (manager != null)
        {
            manager.SetOn(_musicOn);
            if (_shuffle) manager.SetShuffle(true);
            else manager.SelectTrack(_track);
        }

        PushGameSettings();   // MusicPlayer listens to GameSettings
    }

    // ---- settings glue: the ONLY place this menu writes GameSettings. It follows MusicMenu's pattern: set the Pending values, then Apply.
    // (GameSettings.cs was not among the files this was written from, so PendingSoundOn and Apply() are the names to check if this won't compile.)
    private void PushGameSettings()
    {
        GameSettings.PendingMusicOn = _musicOn;
        GameSettings.PendingTrackMode = _shuffle ? 0 : _track;   // 0 = random
        GameSettings.PendingSoundOn = _soundOn;
        GameSettings.Apply();
    }

    // ------------------------------------------------------------ leaving

    private void AskToLeave(LeaveAction action)
    {
        if (!confirmLeaving)
        {
            DoLeave(action);
            return;
        }

        _mainSelected = _selected;
        _pendingLeave = action;
        PlayChooseSound();
        ShowPage(Page.Confirm, 0);   // "No" is the first row, so it starts highlighted
    }

    private void DoLeave(LeaveAction action)
    {
        if (!IsPaused || _leaving) return;
        _leaving = true;
        _menuReady = false;   // one choice only

        bool started;
        if (action == LeaveAction.LevelSelect)
        {
            started = LeaveTo(FindLevelSelectScene(), levelSelectSceneName);

            // The Level Select scene opens on the puzzle stages (the same scene also holds the PVP arenas page).
            if (started) StageSelection.RequestBrowse(StageMode.Puzzle);
        }
        else if (action == LeaveAction.MainMenu)
        {
            started = LeaveTo(FindMainMenuScene(), mainMenuSceneName);
        }
        else
        {
            QuitGame();
            return;
        }

        if (!started)
        {
            _leaving = false;
            _menuReady = true;
            ShowPage(Page.Main, _mainSelected);
        }
    }

    // Exact names first, then the same names ignoring capitals / spaces / dashes (so "MainMenu" finds a scene called "Mainmenu").
    private string FindLevelSelectScene()
    {
        return StageUi.ResolveScene(levelSelectSceneName, "LevelSelect", "Level Select");
    }

    private string FindMainMenuScene()
    {
        return StageUi.ResolveScene(mainMenuSceneName, "Mainmenu", "MainMenu", "Main Menu");
    }

    // Returns false (and the menu stays up) if no scene of that name can be loaded.
    private bool LeaveTo(string scene, string wanted)
    {
        if (scene == null)
        {
            Debug.LogError("PauseController: the scene '" + wanted + "' can't be loaded. Check the scene name spelling and that the scene " +
                           "is in the Scenes In Build list (File > Build Profiles).", this);
            return false;
        }
        if (scene != wanted)
        {
            Debug.LogWarning("PauseController: '" + wanted + "' isn't in the build, so '" + scene + "' is used. Set the scene name to match.", this);
        }

        StartCoroutine(LeaveRoutine(scene));
        return true;
    }

    private IEnumerator LeaveRoutine(string scene)
    {
        float wait = PlayMenuSwitchSound();
        if (wait > 0f) yield return new WaitForSecondsRealtime(wait);

        ReleaseWorld();           // time back to normal, foes un-frozen, players unlocked, so the next scene doesn't start frozen
        GameReset.ResetRun();     // score back to 0 and player health back to full, so the next run starts fresh
        SceneManager.LoadScene(scene);
    }

    private void QuitGame()
    {
        ReleaseWorld();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------ sound

    // One 2D source on a child object, so both split-screen players hear it and it keeps playing while Time.timeScale is 0 and
    // AudioListener.pause is on.
    private void BuildAudio()
    {
        var go = new GameObject("PauseMenuSounds");
        go.transform.SetParent(transform, false);

        _uiAudio = go.AddComponent<AudioSource>();
        _uiAudio.playOnAwake = false;
        _uiAudio.loop = false;
        _uiAudio.spatialBlend = 0f;
        _uiAudio.ignoreListenerPause = true;
    }

    private void PlaySelectSound()
    {
        PlayMenuSound(selectClip, selectVolume);
    }

    private void PlayChooseSound()
    {
        PlayMenuSound(chooseClip, chooseVolume);
    }

    private void PlayMenuSound(AudioClip clip, float volume)
    {
        if (clip == null || _uiAudio == null || !GameSettings.SoundOn) return;
        _uiAudio.PlayOneShot(clip, volume);
    }

    // The between-menus sound. Returns how long the scene should wait before loading, so loading doesn't cut the sound off.
    private float PlayMenuSwitchSound()
    {
        if (menuSwitchClip == null || _uiAudio == null || !GameSettings.SoundOn) return 0f;

        _uiAudio.PlayOneShot(menuSwitchClip, menuSwitchVolume);
        return Mathf.Min(menuSwitchClip.length, menuSwitchMaxWait);
    }

    // ------------------------------------------------------------ building the menu

    //  Canvas
    //   PauseRoot       the dark screen (also swallows clicks meant for the game behind it)
    //     Title         the banner: PAUSED / GRAPHICS / MUSIC & SOUND / ARE YOU SURE?
    //     Row 1..6      one pool of rows, reused by every page (a page just switches the ones it needs on)
    //       Label       the row's text
    //       Left, Right the "<" and ">" of Choice and Number rows
    //     Hint          what the keys do
    private void BuildUi()
    {
        var canvasObject = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = NewRect("PauseRoot", canvasObject.transform);
        Stretch(root);
        _root = root.gameObject;

        Image dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, dimAmount);

        // The banner.
        RectTransform title = NewRect("Title", root);
        Place(title, new Vector2(0f, 410f), new Vector2(1000f, 130f));
        Image plate = title.gameObject.AddComponent<Image>();
        plate.color = titlePlateColor;
        plate.raycastTarget = false;

        TextMeshProUGUI titleText = NewText("Text", title, pausedTitle, 90f, Vector2.zero, Vector2.zero);
        Stretch(titleText.rectTransform);
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = titleTextColor;
        _titleLabel = titleText;

        // The pool of rows.
        for (int i = 0; i < MaxRows; i++)
        {
            RectTransform rect = NewRect("Row " + (i + 1), root);
            Place(rect, new Vector2(0f, FirstRowY - i * RowSpacing), new Vector2(RowWidth, RowHeight));

            Image image = rect.gameObject.AddComponent<Image>();

            // Inset on both sides so the text never runs under the arrows; it shrinks to fit when it is long.
            TextMeshProUGUI label = NewText("Label", rect, "", 40f, Vector2.zero, Vector2.zero);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(90f, 0f);
            label.rectTransform.offsetMax = new Vector2(-90f, 0f);
            label.fontStyle = FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 22f;
            label.fontSizeMax = 40f;

            TextMeshProUGUI left = NewText("Left", rect, "<", 52f, new Vector2(-RowWidth * 0.5f + 45f, 0f), new Vector2(80f, RowHeight));
            left.fontStyle = FontStyles.Bold;
            TextMeshProUGUI right = NewText("Right", rect, ">", 52f, new Vector2(RowWidth * 0.5f - 45f, 0f), new Vector2(80f, RowHeight));
            right.fontStyle = FontStyles.Bold;

            _rowRects[i] = rect;
            _rowImages[i] = image;
            _rowLabels[i] = label;
            _rowLeft[i] = left;
            _rowRight[i] = right;
        }

        // Two lines when a controller is plugged in.
        TextMeshProUGUI hint = NewText("Hint", root, hintText, 30f, new Vector2(0f, -410f), new Vector2(1600f, 130f));
        hint.color = new Color(1f, 1f, 1f, 0.55f);
        _hintLabel = hint;
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
