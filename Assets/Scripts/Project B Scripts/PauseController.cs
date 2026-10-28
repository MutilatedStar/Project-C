using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Ilumisoft.HealthSystem;

/// <summary>
/// The pause menu. Put this on an object that is ALWAYS active in the dungeon scene (the same empty GameObject as
/// FloorController is fine). There is nothing to wire up: the dark overlay, the "Paused" text and the Quit button are
/// built by this script. (Want your own look? Drag your own Quit button and/or panel into "Your own overlay" below. Your
/// button is hidden until the game is paused, wherever it sits in the scene, and shown while it is.)
///
///   P or Esc            pauses. While paused, P or Esc resumes.
///   Click anywhere      resumes, as long as the click is not on the Quit button.
///   Quit button         resets the score to 0 and the player's health to full (GameReset), then goes to the Main Menu scene.
///
/// What freezes while paused:
///   - Time.timeScale goes to 0: physics, animations, particles and anything else that runs on game time.
///   - FoeController.Frozen is set, the same switch the stairs menu uses: foes, sword swings, fire trails, the sparks
///     on the blade and the death explosions all stop.
///   - FloorController.BlocksInput turns true (it includes this pause), so the player and the notebook keys hold still.
///
/// It does NOT open (the key does nothing) when:
///   - the stairs menu is up or a floor change is running. P / Esc close the stairs menu first (FloorController does
///     that); pressing again then opens this pause.
///   - the notebook (MenuController) is open, or was closed on this very press.
///   - the player is dead.
///
/// Other scripts can read:  PauseController.IsPaused,  PauseController.BlocksInput.
/// </summary>
[DefaultExecutionOrder(-200)]   // runs before FloorController (-100), the player and the sword, so P / Esc are claimed first
public class PauseController : MonoBehaviour
{
    // ------------------------------------------------------------ inspector

    [Header("Keys")]
    [SerializeField] private KeyCode pauseKey = KeyCode.P;
    [Tooltip("Second key that pauses and resumes. None = off.")]
    [SerializeField] private KeyCode altPauseKey = KeyCode.Escape;

    [Header("Quit button")]
    [Tooltip("Exact name of the Main Menu scene. It must be in File > Build Profiles (Build Settings) > Scenes In Build. " +
             "\"MainMenu\" and \"Main Menu\" are both tried if this name can't be loaded.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Default overlay (built for you)")]
    [SerializeField] private string titleText = "Paused";
    [SerializeField] private string hintText = "Click anywhere, or press P / Esc, to resume";
    [SerializeField] private string quitButtonText = "Quit to Main Menu";
    [Tooltip("How dark the screen goes behind the menu (0 = not at all, 1 = black).")]
    [SerializeField, Range(0f, 1f)] private float dimAmount = 0.65f;
    [Tooltip("Draw order of the overlay's canvas. Higher is drawn on top of other canvases.")]
    [SerializeField] private int canvasSortingOrder = 500;

    [Header("Your own overlay (optional)")]
    [Tooltip("Leave EMPTY to use the dark overlay this script builds. Or drag your own panel here. It is hidden until the game is paused.")]
    [SerializeField] private GameObject pausePanel;
    [Tooltip("Leave EMPTY to use the Quit button this script builds. Or drag your own button here, inside your Pause Panel or anywhere else " +
             "in the scene: it is hidden until the game is paused and shown while it is. Don't also add an OnClick: this script hooks it up.")]
    [SerializeField] private Button quitButton;
    [Tooltip("Optional. Anything else that should only be visible while paused, most often the TEXT on your Quit button when that text is not " +
             "a child of the button in the Hierarchy (a child hides with the button by itself). Each one is hidden until the game is paused and " +
             "shown while it is. Anything inside your Pause Panel is already handled by the panel.")]
    [SerializeField] private GameObject[] extraObjects;

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

    private float previousTimeScale = 1f;
    private bool weFroze;
    private bool notebookWasOpen;
    private HealthComponent playerHealth;
    private float nextHealthSearch;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        IsPaused = false;
        SetupUi();
        SetUi(false);
    }

    private void OnDestroy()
    {
        if (quitButton != null) quitButton.onClick.RemoveListener(QuitToMainMenu);

        // Never leave the game frozen behind, e.g. when another scene replaces this one.
        if (IsPaused)
        {
            IsPaused = false;
            Time.timeScale = previousTimeScale;
        }
        if (weFroze)
        {
            FoeController.Frozen = false;
            weFroze = false;
        }
    }

    // ------------------------------------------------------------ main loop

    private void Update()
    {
        if (IsPaused)
        {
            // Something else (the notebook closing, say) may have put the time back: the pause keeps it at 0.
            if (Time.timeScale != 0f) Time.timeScale = 0f;

            if (PausePressed())
            {
                Resume();
            }
            else if (Input.GetMouseButtonDown(0))
            {
                // Handled right here (not only through the button's onClick), so it works even without an EventSystem.
                // A click on the Quit button quits; a click anywhere else resumes.
                if (FloorController.PointerOver(quitButton))
                {
                    if (quitButton.interactable) QuitToMainMenu();
                }
                else
                {
                    Resume();
                }
            }
            return;
        }

        // The notebook counts as "busy" for one extra frame, so the Esc / P that CLOSES it can't also open the pause
        // (this works whichever of the two scripts runs first).
        bool notebookOpen = MenuController.IsOpen;
        bool notebookBusy = notebookOpen || notebookWasOpen;
        notebookWasOpen = notebookOpen;

        if (!PausePressed()) return;
        if (notebookBusy) return;
        if (FloorController.BlocksInput) return;   // stairs menu up / floor change running / one just closed this frame
        if (PlayerIsDead()) return;

        Pause();
    }

    private bool PausePressed()
    {
        return Input.GetKeyDown(pauseKey) || Input.GetKeyDown(altPauseKey);
    }

    // ------------------------------------------------------------ pausing

    /// <summary>Pauses the game and shows the menu. P / Esc call this for you.</summary>
    public void Pause()
    {
        if (IsPaused) return;

        IsPaused = true;
        previousTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        FoeController.Frozen = true;   // foes, sword, trails, sparks and explosions all obey this
        weFroze = true;

        SetUi(true);
    }

    /// <summary>Resumes the game and hides the menu. P / Esc and a click outside the Quit button call this for you.</summary>
    public void Resume()
    {
        if (!IsPaused) return;
        Release();
    }

    // Puts everything back. closedFrame makes the player, the sword and the notebook ignore this frame's keys and clicks.
    private void Release()
    {
        IsPaused = false;
        closedFrame = Time.frameCount;

        Time.timeScale = previousTimeScale;
        if (weFroze)
        {
            FoeController.Frozen = false;
            weFroze = false;
        }
        SetUi(false);
    }

    /// <summary>The Quit button: back to the Main Menu scene.</summary>
    public void QuitToMainMenu()
    {
        if (!IsPaused) return;

        string scene = FindMenuScene();
        if (scene == null)
        {
            Debug.LogError("PauseController: the scene '" + mainMenuSceneName + "' can't be loaded. Check the Main Menu Scene Name " +
                           "spelling and that the scene is in the Scenes In Build list (File > Build Profiles).", this);
            return;
        }

        Release();   // time back to normal and foes un-frozen, so the Main Menu doesn't start frozen
        GameReset.ResetRun();   // score back to 0 and player health back to full, so the next run starts fresh
        SceneManager.LoadScene(scene);
    }

    private string FindMenuScene()
    {
        string[] candidates = { mainMenuSceneName, "MainMenu", "Main Menu" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.IsNullOrEmpty(candidates[i]) || !Application.CanStreamedLevelBeLoaded(candidates[i])) continue;

            if (i > 0) Debug.LogWarning("PauseController: '" + mainMenuSceneName + "' isn't in the build, so '" + candidates[i] + "' is used. Set Main Menu Scene Name to match.", this);
            return candidates[i];
        }
        return null;
    }

    // The player is found through its movement script, like FloorController does. Only looked up when a pause is attempted.
    private bool PlayerIsDead()
    {
        if (playerHealth == null && Time.unscaledTime >= nextHealthSearch)
        {
            nextHealthSearch = Time.unscaledTime + 1f;

            var controller = FindAnyObjectByType<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>();
            if (controller != null)
            {
                playerHealth = controller.GetComponentInParent<HealthComponent>();
                if (playerHealth == null) playerHealth = controller.GetComponentInChildren<HealthComponent>();
            }
        }
        return playerHealth != null && playerHealth.CurrentHealth <= 0f;
    }

    // ------------------------------------------------------------ the overlay

    private void SetupUi()
    {
        // No panel of your own: the dark screen, title and hint are built. The Quit button is built too, unless you assigned yours.
        if (pausePanel == null)
        {
            BuildDefaultOverlay();
        }
        else if (quitButton == null)
        {
            Debug.LogWarning("PauseController: your Pause Panel has no Quit Button assigned, so there is no way to quit from it.", this);
        }

        if (quitButton != null) quitButton.onClick.AddListener(QuitToMainMenu);
    }

    // Shows or hides the menu: the panel, plus the Quit button and any Extra Objects (such as the text on your button) that are NOT
    // inside the panel. That way they only exist on screen while the game is paused.
    private void SetUi(bool show)
    {
        if (pausePanel != null) pausePanel.SetActive(show);

        if (quitButton != null) SetOutsidePanel(quitButton.gameObject, show);

        if (extraObjects == null) return;
        for (int i = 0; i < extraObjects.Length; i++) SetOutsidePanel(extraObjects[i], show);
    }

    // Shows or hides one object, unless the panel already controls it or it would switch off this script's own object.
    private void SetOutsidePanel(GameObject target, bool show)
    {
        if (target == null) return;
        if (pausePanel != null && target.transform.IsChildOf(pausePanel.transform)) return;   // the panel already controls it
        if (transform.IsChildOf(target.transform)) return;                                    // never switch off our own object
        target.SetActive(show);
    }

    // A full-screen dark panel (a click on it resumes), a title, a hint and the Quit button.
    private void BuildDefaultOverlay()
    {
        var canvasObject = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder();

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = NewRect("PausePanel", canvasObject.transform);
        Stretch(panel);
        Image dim = panel.gameObject.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, dimAmount);

        NewText("Title", panel, titleText, 96f, new Vector2(0f, 150f), new Vector2(1200f, 140f));
        TextMeshProUGUI hint = NewText("Hint", panel, hintText, 36f, new Vector2(0f, 70f), new Vector2(1400f, 60f));
        hint.color = new Color(1f, 1f, 1f, 0.8f);

        pausePanel = panel.gameObject;
        if (quitButton == null) quitButton = BuildQuitButton(panel);   // a button you assigned yourself is kept as it is
    }

    // The default red "Quit to Main Menu" button, inside the dark panel (so it hides and shows with it).
    private Button BuildQuitButton(RectTransform panel)
    {
        RectTransform buttonRect = NewRect("QuitButton", panel);
        Place(buttonRect, new Vector2(0f, -60f), new Vector2(520f, 100f));
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = Color.white;   // the Button tints this

        Button button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.55f, 0.12f, 0.12f, 1f);
        colors.highlightedColor = new Color(0.75f, 0.2f, 0.2f, 1f);
        colors.pressedColor = new Color(0.4f, 0.08f, 0.08f, 1f);
        colors.selectedColor = colors.normalColor;
        button.colors = colors;

        TextMeshProUGUI label = NewText("Label", buttonRect, quitButtonText, 44f, Vector2.zero, Vector2.zero);
        Stretch(label.rectTransform);
        return button;
    }

    // Your own Quit button must stay visible ABOVE the dark screen. When it sits on another Overlay canvas, the dark screen is
    // therefore drawn just underneath that canvas instead of on top of it. With no button of your own it simply uses Canvas Sorting Order.
    private int OverlaySortingOrder()
    {
        if (quitButton == null) return canvasSortingOrder;

        Canvas buttonCanvas = quitButton.GetComponentInParent<Canvas>();
        if (buttonCanvas == null) return canvasSortingOrder;
        buttonCanvas = buttonCanvas.rootCanvas;

        if (buttonCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning("PauseController: your Quit Button is on a canvas that isn't Screen Space - Overlay, so the dark screen may be drawn " +
                             "over it. Put the button on an Overlay canvas, or assign your own Pause Panel as well.", this);
            return canvasSortingOrder;
        }
        return buttonCanvas.sortingOrder - 1;
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
        label.raycastTarget = false;   // clicks go to the panel or the button behind it
        return label;
    }
}
