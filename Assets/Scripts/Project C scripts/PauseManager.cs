using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Universal pause. Needs DirectButton.cs in the project (TabGroup.cs is optional).
///
/// P or Esc (change them in "Pause Keys") opens/closes the pause menu.
///
///   OFFLINE (default): the game is FROZEN
///       - Time.timeScale = 0  (physics, animation, particles, NavMesh, WaitForSeconds... all stop)
///       - audio pauses (anything flagged "ignore listener pause", like MusicManager, keeps playing)
///       - every script in "World Scripts" is switched off
///       - every script in "Player Input Scripts" is switched off
///       - the mouse cursor is freed, then put back exactly as it was
///
///   ONLINE MULTIPLAYER: tick "Online Multiplayer" (or call SetOnlineMultiplayer(true), or set
///       PauseManager.IsOnlineCheck): the menu still opens, but time, audio, enemies and physics keep
///       running behind it. Only "Player Input Scripts" are switched off, so you don't run around
///       while in the menu (untick "Block Input When Online" to keep even that running).
///
/// Everything in the menu is detected by code (DirectButton), so leave the buttons' OnClick lists EMPTY.
/// Put this script on an always-active object that is NOT inside the Pause Menu Root.
///
/// From other scripts:
///     PauseManager.IsPaused   -> the menu is open (online or offline)
///     PauseManager.IsFrozen   -> the game is actually stopped (offline pause only)
///     PauseManager.PausedChanged += paused => { ... };
///     PauseManager.Instance.Pause() / Resume() / Toggle() / SetOnlineMultiplayer(bool)
///
/// Time.timeScale can't stop code that ignores time (movement per frame without deltaTime, mouse look,
/// shooting on key press...). Either list those scripts in Player Input Scripts / World Scripts, or add
/// "if (PauseManager.IsPaused) return;" at the top of their Update.
/// </summary>
[DisallowMultipleComponent]
public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }
    public static bool IsPaused { get; private set; }
    public static bool IsFrozen { get; private set; }

    /// <summary>Raised when the menu opens (true) or closes (false).</summary>
    public static event System.Action<bool> PausedChanged;

    /// <summary>
    /// Optional hook for your netcode, so the pause knows by itself whether you're online. Example:
    ///     PauseManager.IsOnlineCheck = () => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    /// </summary>
    public static System.Func<bool> IsOnlineCheck;

    [Header("Pause keys")]
    public KeyCode[] pauseKeys = { KeyCode.P, KeyCode.Escape };
    [Tooltip("Off = the keys and the Pause Button do nothing (cutscenes, game over screens...). Pause() / Resume() from code still work.")]
    public bool allowPausing = true;
    [Tooltip("Opens the pause menu when the game window loses focus (alt-tab, phone call). Leave off while testing in the Editor.")]
    public bool pauseOnFocusLoss = false;

    [Header("Online multiplayer")]
    [Tooltip("ON: pausing only opens the menu. Time, audio, enemies and physics keep running behind it.")]
    public bool onlineMultiplayer = false;
    [Tooltip("While online and the menu is open, still switch off 'Player Input Scripts' so you don't move around behind the menu.")]
    public bool blockInputWhenOnline = true;

    [Header("What a pause does (offline)")]
    public bool freezeTime = true;
    public bool pauseAudio = true;
    [Tooltip("Frees and shows the mouse cursor while the menu is open, then restores it as it was.")]
    public bool manageCursor = true;

    [Header("Scripts to switch off (drag the script components in)")]
    [Tooltip("Scripts that read the player's input: movement, camera look, shooting, interacting. Switched off whenever the menu is open, and switched back on only if they were on before.")]
    public Behaviour[] playerInputScripts;
    [Tooltip("Anything else that must stop but ignores Time.timeScale (enemy AI on unscaled time, spawners...). Only switched off during a real offline freeze.")]
    public Behaviour[] worldScripts;

    [Header("Pause menu (all optional)")]
    [Tooltip("The object holding the whole pause menu. It is hidden at start and shown while paused. This script must NOT be on it or inside it.")]
    public GameObject pauseMenuRoot;
    [Tooltip("A HUD pause icon. Only works while the game is NOT paused.")]
    public DirectButton pauseButton = new DirectButton();
    public DirectButton resumeButton = new DirectButton();
    public DirectButton restartButton = new DirectButton();
    public DirectButton mainMenuButton = new DirectButton();
    public DirectButton quitButton = new DirectButton();
    [Tooltip("Scene the Main Menu button loads (by name, must be in Build Settings). Empty = the scene at Build Index 0.")]
    public string mainMenuScene = "";

    [Header("Events")]
    public UnityEvent onPaused;
    public UnityEvent onResumed;

    private GameObject _menu;   // pauseMenuRoot after it passed the safety check
    private readonly List<Behaviour> _offInput = new List<Behaviour>();
    private readonly List<Behaviour> _offWorld = new List<Behaviour>();
    private bool _inputBlocked;
    private float _savedTimeScale = 1f;
    private bool _savedAudioPause;
    private bool _cursorSaved;
    private CursorLockMode _savedLock;
    private bool _savedCursorVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        IsPaused = false;
        IsFrozen = false;
        IsOnlineCheck = null;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PauseManager: there is already one in the scene, so this extra copy was removed.", this);
            enabled = false;
            Destroy(this);
            return;
        }
        Instance = this;
        IsPaused = false;
        IsFrozen = false;

        if (pauseMenuRoot != null)
        {
            if (pauseMenuRoot == gameObject || transform.IsChildOf(pauseMenuRoot.transform))
            {
                Debug.LogError("PauseManager: this script is on (or inside) the Pause Menu Root, so hiding the menu would " +
                               "switch the pause off. Put it on an always-active object outside the menu. The menu is ignored until then.", this);
            }
            else
            {
                _menu = pauseMenuRoot;
                _menu.SetActive(false);
            }
        }
    }

    private void Start()
    {
        // Report mistakes now (while the menu is still hidden) instead of the first time someone pauses.
        if (_menu != null)
        {
            TabGroup[] groups = _menu.GetComponentsInChildren<TabGroup>(true);
            for (int i = 0; i < groups.Length; i++) groups[i].Validate();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        // Never leave the next scene frozen.
        IsPaused = false;
        SetFrozen(false);
        SetInputBlocked(false);
        RestoreCursor();
        Instance = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && pauseOnFocusLoss && allowPausing) Pause();
    }

    private void Update()
    {
        if (allowPausing && PausePressed())
        {
            Toggle();
            return;
        }

        // One branch per frame, so a tap on a Resume button that sits on top of the HUD pause icon can't instantly re-open the menu.
        if (IsPaused)
        {
            if (resumeButton.WasTapped()) Resume();
            else if (restartButton.WasTapped()) RestartScene();
            else if (mainMenuButton.WasTapped()) GoToMainMenu();
            else if (quitButton.WasTapped()) QuitGame();
        }
        else if (allowPausing && pauseButton.WasTapped())
        {
            Pause();
        }
    }

    // ------------------------------------------------------------------ public API

    public void Toggle()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;

        SaveAndFreeCursor();
        if (_menu != null) _menu.SetActive(true);
        Apply();

        if (onPaused != null) onPaused.Invoke();
        if (PausedChanged != null) PausedChanged(true);
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;

        Apply();
        if (_menu != null) _menu.SetActive(false);
        RestoreCursor();

        if (onResumed != null) onResumed.Invoke();
        if (PausedChanged != null) PausedChanged(false);
    }

    /// <summary>Switch online mode at runtime (e.g. when a match starts or ends). Takes effect immediately, even mid-pause.</summary>
    public void SetOnlineMultiplayer(bool online)
    {
        onlineMultiplayer = online;
        if (IsPaused) Apply();
    }

    public void RestartScene()
    {
        Resume();   // unfreeze first, or the reloaded scene would start frozen
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Resume();
        if (string.IsNullOrEmpty(mainMenuScene)) SceneManager.LoadScene(0);
        else SceneManager.LoadScene(mainMenuScene);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ pause logic

    private bool PausePressed()
    {
        if (pauseKeys == null) return false;

        for (int i = 0; i < pauseKeys.Length; i++)
        {
            KeyCode key = pauseKeys[i];
            if (!KeyInput.WasPressed(key)) continue;

            // Letter keys must not pause while the player is typing in a chat / name field. Esc always works.
            if (key != KeyCode.Escape && KeyInput.IsTyping()) continue;
            return true;
        }
        return false;
    }

    private bool IsOnline()
    {
        if (onlineMultiplayer) return true;
        if (IsOnlineCheck == null) return false;

        try
        {
            return IsOnlineCheck();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("PauseManager: IsOnlineCheck threw an error, so the game is treated as offline. " + e.Message, this);
            return false;
        }
    }

    // Works out what should be stopped right now and makes it so. Safe to call any time.
    private void Apply()
    {
        bool online = IsOnline();
        SetFrozen(IsPaused && !online);
        SetInputBlocked(IsPaused && (!online || blockInputWhenOnline));
    }

    private void SetFrozen(bool frozen)
    {
        if (frozen == IsFrozen) return;
        IsFrozen = frozen;

        if (frozen)
        {
            if (freezeTime)
            {
                _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            if (pauseAudio)
            {
                _savedAudioPause = AudioListener.pause;
                AudioListener.pause = true;
            }
            DisableListed(worldScripts, _offWorld);
        }
        else
        {
            if (freezeTime) Time.timeScale = _savedTimeScale;
            if (pauseAudio) AudioListener.pause = _savedAudioPause;
            RestoreListed(_offWorld);
        }
    }

    private void SetInputBlocked(bool blocked)
    {
        if (blocked == _inputBlocked) return;
        _inputBlocked = blocked;

        if (blocked) DisableListed(playerInputScripts, _offInput);
        else RestoreListed(_offInput);
    }

    // Switches scripts off and remembers which ones WE switched off, so only those come back on.
    private void DisableListed(Behaviour[] list, List<Behaviour> record)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            Behaviour b = list[i];
            if (b == null || b == this || !b.enabled) continue;
            b.enabled = false;
            record.Add(b);
        }
    }

    private static void RestoreListed(List<Behaviour> record)
    {
        for (int i = 0; i < record.Count; i++)
        {
            if (record[i] != null) record[i].enabled = true;
        }
        record.Clear();
    }

    // ------------------------------------------------------------------ cursor

    private void SaveAndFreeCursor()
    {
        if (!manageCursor) return;

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
}
