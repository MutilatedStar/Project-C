using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

// Bare-bones pause menu.
//
// Esc, P or Tab opens it and freezes the game; press again (or click Resume) to carry on.
// Quit goes back to the main menu scene.
//
// Attach this to an object that is ALWAYS active (for example an empty GameObject named "MenuController"),
// NOT to the menu canvas, because hiding the canvas switches off every script on it.
// There must be exactly ONE MenuController in the scene.
public class MenuController : MonoBehaviour
{
    [Header("Hook-up")]
    [Tooltip("The panel that holds the three buttons. Shown while paused, hidden otherwise. " +
             "It must be a DIFFERENT object from the one this script is on.")]
    public GameObject menuCanvas;
    [Tooltip("Closes the pause menu and carries on playing.")]
    public Button resumeButton;
    [Tooltip("The top button. Goes back to the main menu scene.")]
    public Button quitButton;

    [Header("Settings")]
    [Tooltip("Exact name of the main menu scene. It must be in the Build Settings / Build Profiles scene list.")]
    [SerializeField] private string mainMenuScene = "Mainmenu";
    [Tooltip("Prints what the pause menu is doing to the Console, including why a key press was ignored. Turn off once it works.")]
    [SerializeField] private bool logEvents = true;

    // True while the pause menu is showing. Other scripts (the player, foes, swords...) read this.
    public static bool IsOpen { get; private set; }

    // Raised at the end of SetOpen: true when the menu just opened, false when it just closed.
    public static event System.Action<bool> OpenChanged;

    // True while the player is typing in a text field. The P hotkey stays quiet then.
    public static bool IsTyping
    {
        get
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;

            GameObject selected = es.currentSelectedGameObject;

            TMP_InputField tmpField = selected.GetComponent<TMP_InputField>();
            if (tmpField != null && tmpField.isFocused) return true;

            InputField legacyField = selected.GetComponent<InputField>();
            return legacyField != null && legacyField.isFocused;
        }
    }

    private static MenuController instance;

    // Static values survive between Play sessions when Unity's "Reload Domain" is switched off, so start clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
        OpenChanged = null;
        instance = null;
    }

    private void Log(string message)
    {
        if (logEvents) Debug.Log("[MenuController] " + message, this);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("MenuController: there are TWO MenuControllers ('" + instance.name + "' and '" + name + "'). " +
                           "They share one IsOpen flag and undo each other's key presses. Delete one. " +
                           "This copy is switching itself off.", this);
            enabled = false;
            return;
        }
        instance = this;

        if (menuCanvas == null)
        {
            Debug.LogError("MenuController: drag the pause panel into the Menu Canvas field.", this);
            enabled = false;
            return;
        }

        if (menuCanvas == gameObject || transform.IsChildOf(menuCanvas.transform))
        {
            Debug.LogError("MenuController is on the pause panel (or inside it), so hiding the panel also switches this " +
                           "script off and nothing can close the menu again. Move it to an object that stays active.", this);
        }

        if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
        else Debug.LogWarning("MenuController: no Resume Button assigned.", this);

        if (quitButton != null) quitButton.onClick.AddListener(QuitToMainMenu);
        else Debug.LogWarning("MenuController: no Quit Button assigned.", this);

        SetOpen(false);
    }

    private void Update()
    {
        bool toggleKey = Input.GetKeyDown(KeyCode.Escape) ||
                         Input.GetKeyDown(KeyCode.Tab) ||
                         (Input.GetKeyDown(KeyCode.P) && !IsTyping);
        if (!toggleKey) return;

        // The stairs menu (FloorController) owns the keyboard while it is up. It may stop this menu from OPENING,
        // but never from CLOSING, so an open pause menu can't trap the player.
        if (FloorController.BlocksInput && !IsOpen)
        {
            Log("Key ignored: FloorController.BlocksInput is true, so the pause menu won't open.");
            return;
        }

        SetOpen(!IsOpen);
    }

    // Public so other scripts can open or close the menu too.
    public void SetOpen(bool open)
    {
        IsOpen = open;
        menuCanvas.SetActive(open);

        // Stops everything that runs on game time. Scripts that check MenuController.IsOpen (foes, swords, trails)
        // stop as well. Time.timeScale carries over into the next scene, which is why closing always sets it back to 1.
        Time.timeScale = open ? 0f : 1f;

        // Don't leave a button selected, or Space / Enter could "click" it later.
        if (!open && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        Log(open ? "Paused." : "Resumed.");
        OpenChanged?.Invoke(open);
    }

    // For the Resume button (hooked up automatically).
    public void Resume()
    {
        SetOpen(false);
    }

    // For the Quit button (hooked up automatically).
    public void QuitToMainMenu()
    {
        if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            Debug.LogError("MenuController: the scene \"" + mainMenuScene + "\" can't be loaded. Check the spelling " +
                           "(it is case sensitive) and that it is in the Build Settings / Build Profiles scene list.", this);
            return;
        }

        SetOpen(false);   // puts the game speed back to normal before the new scene loads
        SceneManager.LoadScene(mainMenuScene);
    }

    private void OnDestroy()
    {
        if (resumeButton != null) resumeButton.onClick.RemoveListener(Resume);
        if (quitButton != null) quitButton.onClick.RemoveListener(QuitToMainMenu);

        if (instance != this) return;   // a switched-off duplicate must not reset the real one
        instance = null;

        if (IsOpen) Time.timeScale = 1f;   // never leave the game frozen behind us
        IsOpen = false;
    }
}
