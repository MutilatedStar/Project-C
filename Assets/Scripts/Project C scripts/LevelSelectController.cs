using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The LEVEL SELECT SCENE. This used to be a page of the main menu; it is its own scene now, so the Lights Out screen (and the PVP result
/// screen) can load it directly, and BACK returns to the Main Menu scene.
///
/// The scene has two pages, each made of real objects under "Level Select Canvas" that you can edit in the Scene view:
///
///   Level Select Page   every Puzzle stage in the catalog, as picture cards.  Picking one loads the Puzzle scene on that stage (PVP off).
///   Pvp Page            every Pvp stage (arena), plus the "FIRST TO n" button.  Picking one loads the Puzzle scene on that arena (PVP on).
///
/// Which page opens is decided by whoever loaded the scene (StageSelection.RequestBrowse): the main menu's LEVEL SELECT / PVP buttons, the
/// Lights Out screen, the PVP result screen. Pressing Play inside this scene by itself uses "Default Mode".
///
/// THE CARDS: the one card under Viewport > Content is a TEMPLATE. When the scene starts it is switched off and one copy per stage is made
/// from it (name, number and picture filled in by Stage Card View), so restyling that one card restyles them all. The list of stages is
/// StageCatalog (Assets/Resources/StageCatalog.asset), rebuilt from the Stage Definition objects in the Puzzle scene whenever you save it.
///
/// The first time the scene is opened in the editor the UI is built for you (MenuUiBakeTool); "Build / Rebuild Level Select UI" on this
/// component puts the original layout back. If the UI objects are missing when the game starts, they are built in code.
///
/// Works with mouse, keyboard (arrows / Enter / Esc) and controller (D-pad or stick / A / B) through the normal EventSystem.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Level Select Controller")]
public class LevelSelectController : MonoBehaviour
{
    /// <summary>The objects of one page. Filled in for you by Build / Rebuild Level Select UI.</summary>
    [System.Serializable]
    public class StagePage
    {
        [Tooltip("The whole page. Switched on for the mode that is opening, off for the other one.")]
        public GameObject page;
        [Tooltip("The text that says how many stages there are.")]
        public TMP_Text countLabel;
        [Tooltip("The panel behind the cards. Its height can shrink to fit a short list (see Fit Card To Stage Count).")]
        public RectTransform card;
        [Tooltip("Shown instead of the cards when there are none of this kind.")]
        public GameObject emptyMessage;
        [Tooltip("The object the cards are placed in (it has the grid on it).")]
        public RectTransform content;
        [Tooltip("The template card. Switched off at start; every stage gets a copy of it.")]
        public StageCardView cardTemplate;
        [Tooltip("The button that returns to the Main Menu scene.")]
        public Button backButton;
        [Tooltip("PVP page only: the button that cycles First To 1 / 2 / 3.")]
        public Button roundsButton;
        [Tooltip("PVP page only: the text on the rounds button.")]
        public TMP_Text roundsLabel;
    }

    [Header("Scenes (must be in Build Settings)")]
    [Tooltip("Where BACK goes. 'Mainmenu', 'MainMenu' and 'Main Menu' are all found, whatever the capitals.")]
    [SerializeField] private string mainMenuScene = "Mainmenu";
    [Tooltip("Only used if there is no StageCatalog asset yet. The catalog normally knows the stage scene by itself.")]
    [SerializeField] private string fallbackStageScene = "Puzzle";

    [Header("Which page opens")]
    [Tooltip("Used when nothing asked for a page: pressing Play in this scene by itself. (The main menu, the Lights Out screen and the PVP result screen always ask.)")]
    [SerializeField] private StageMode defaultMode = StageMode.Puzzle;

    [Header("The UI (objects in this scene)")]
    [Tooltip("The Level Select Canvas. Filled in for you by Build / Rebuild Level Select UI.")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private StagePage levelSelectPage = new StagePage();
    [SerializeField] private StagePage pvpPage = new StagePage();
    [Tooltip("A red line of text for problems such as a scene missing from Build Settings. Hidden until it is needed.")]
    [SerializeField] private TMP_Text toastLabel;

    [Header("Text that is filled in by itself")]
    [Tooltip("{0} becomes the number of stages.")]
    [SerializeField] private string stageCountFormat = "{0} stages";
    [SerializeField] private string oneStageText = "1 stage";
    [Tooltip("{0} becomes the number of rounds (1, 2 or 3).")]
    [SerializeField] private string roundsFormat = "FIRST TO {0}";

    [Header("Card panel size")]
    [Tooltip("ON: the panel behind the cards grows with the number of stages, from Min Card Height up to the height you gave the Card object in the Scene view. " +
             "OFF: the Card keeps exactly the size you gave it. Needs the Card's top and bottom anchors to be the same (the default).")]
    [SerializeField] private bool fitCardToStageCount = true;
    [SerializeField, Min(100f)] private float minCardHeight = 640f;

    [Header("Used only when the UI is built or rebuilt")]
    [Tooltip("A soft blue (Player 1) and orange (Player 2) glow down the two sides of the screen.")]
    [SerializeField] private bool sideGlow = true;
    [SerializeField, HideInInspector] private bool uiBuilt;

    private StageCatalog catalog;
    private bool loading;
    private float toastUntil;
    private GameObject firstSelected;

    // ------------------------------------------------------------ for the editor tool

    /// <summary>True once the screens exist in the scene (or were built at runtime).</summary>
    public bool HasUi { get { return levelSelectPage.page != null && pvpPage.page != null; } }

    /// <summary>True once the editor has built the UI into this scene at least once, so it is never rebuilt behind your back.</summary>
    public bool UiBuilt { get { return uiBuilt; } }

    public Canvas UiCanvas { get { return canvas; } }
    public GameObject LevelSelectPageObject { get { return levelSelectPage.page; } }
    public GameObject PvpPageObject { get { return pvpPage.page; } }

    /// <summary>What the builder needs to know to lay the screens out.</summary>
    public MenuUiBuilder.Options BuildOptions(bool bake)
    {
        StageCatalog cat = StageCatalog.Load();

        MenuUiBuilder.Options o = new MenuUiBuilder.Options();
        o.bake = bake;
        o.sideGlow = sideGlow;
        o.puzzleCount = cat != null ? cat.Get(StageMode.Puzzle).Count : 0;
        o.pvpCount = cat != null ? cat.Get(StageMode.Pvp).Count : 0;
        return o;
    }

    /// <summary>Takes over the objects MenuUiBuilder just made. Called by the builder's callers; you don't need to.</summary>
    public void ApplyBuiltUi(MenuUiBuilder.LevelSelectRefs refs)
    {
        canvas = refs.canvas;
        toastLabel = refs.toast;
        Apply(levelSelectPage, refs.levelSelect);
        Apply(pvpPage, refs.pvp);
        uiBuilt = true;
    }

    private static void Apply(StagePage page, MenuUiBuilder.StagePageRefs r)
    {
        page.page = r.page;
        page.countLabel = r.count;
        page.card = r.card;
        page.emptyMessage = r.empty;
        page.content = r.content;
        page.cardTemplate = r.template;
        page.backButton = r.back;
        page.roundsButton = r.rounds;
        page.roundsLabel = r.roundsLabel;
    }

    // ------------------------------------------------------------ start

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // On the menus: nothing PVP-ish may leak into the normal game.
        PvpMode.Enabled = false;
        PvpMode.ResetMatch();

        catalog = StageCatalog.Load();
        StageUi.EnsureEventSystem();

        if (!HasUi) BuildUiAtRuntime();

        StageMode mode = StageSelection.BrowseRequested ? StageSelection.BrowseMode : defaultMode;
        StageSelection.BrowseRequested = false;

        GameObject puzzleFirst = SetUpPage(levelSelectPage, StageMode.Puzzle, mode == StageMode.Puzzle);
        GameObject pvpFirst = SetUpPage(pvpPage, StageMode.Pvp, mode == StageMode.Pvp);
        firstSelected = mode == StageMode.Pvp ? pvpFirst : puzzleFirst;

        if (toastLabel != null) toastLabel.gameObject.SetActive(false);
        StartCoroutine(SelectFirstNextFrame());
    }

    private void BuildUiAtRuntime()
    {
        Debug.LogWarning("LevelSelectController: the level select UI isn't in this scene, so it was built in code just now. Select 'Level Select Controller' " +
                         "and press 'Build / Rebuild Level Select UI' in the Inspector to put it in the scene, where you can see and edit it.", this);
        ApplyBuiltUi(MenuUiBuilder.BuildLevelSelect(this, BuildOptions(false)));
    }

    // Shows or hides the page and, if shown, fills it with the cards of its mode. Returns what the cursor should start on.
    private GameObject SetUpPage(StagePage p, StageMode mode, bool show)
    {
        if (p.page == null) return null;
        p.page.SetActive(show);
        if (!show) return null;

        List<StageCatalog.Entry> entries = catalog != null ? catalog.Get(mode) : new List<StageCatalog.Entry>();
        GameObject back = p.backButton != null ? p.backButton.gameObject : null;

        // Cancel (Esc / B) goes back from anywhere on the page, including the cards made below.
        HookCancel(p.page);

        if (p.countLabel != null)
        {
            p.countLabel.text = entries.Count == 1 ? oneStageText : stageCountFormat.Replace("{0}", entries.Count.ToString());
        }

        if (mode == StageMode.Pvp)
        {
            if (p.roundsButton != null) p.roundsButton.gameObject.SetActive(true);
            RefreshRoundsLabel();
        }

        FitCard(p, entries.Count);

        if (p.cardTemplate != null) p.cardTemplate.gameObject.SetActive(false);   // the template is only a pattern

        if (entries.Count == 0 || p.cardTemplate == null)
        {
            if (p.emptyMessage != null) p.emptyMessage.SetActive(true);
            return back;
        }
        if (p.emptyMessage != null) p.emptyMessage.SetActive(false);

        Transform parent = p.content != null ? p.content : p.cardTemplate.transform.parent;
        GameObject first = back;
        for (int i = 0; i < entries.Count; i++)
        {
            StageCatalog.Entry entry = entries[i];
            string title = string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName;

            StageCardView card = Instantiate(p.cardTemplate, parent);
            card.gameObject.name = "Stage " + (i + 1).ToString("00") + " - " + title;
            card.gameObject.SetActive(true);
            card.Fill(title, i + 1, entry.thumbnail);

            if (card.button != null)
            {
                StageCatalog.Entry captured = entry;
                card.button.onClick.AddListener(() => StartStage(captured));
                if (i == 0) first = card.button.gameObject;
            }

            StageButtonFx fx = card.GetComponent<StageButtonFx>();
            if (fx != null) fx.onCancel = Back;
        }
        return first;
    }

    private void HookCancel(GameObject root)
    {
        StageButtonFx[] all = root.GetComponentsInChildren<StageButtonFx>(true);
        for (int i = 0; i < all.Length; i++) all[i].onCancel = Back;
    }

    // The panel is sized for the longest list in the Scene view; a short list shrinks it, like the old code-built page did.
    private void FitCard(StagePage p, int count)
    {
        if (!fitCardToStageCount || p.card == null) return;
        if (!Mathf.Approximately(p.card.anchorMin.y, p.card.anchorMax.y)) return;   // a vertically stretched card sizes itself

        int columns = 3;
        float rowHeight = 354f;
        if (p.content != null)
        {
            GridLayoutGroup grid = p.content.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount) columns = Mathf.Max(1, grid.constraintCount);
                rowHeight = grid.cellSize.y + grid.spacing.y;
            }
        }

        float maxHeight = Mathf.Max(minCardHeight, p.card.sizeDelta.y);
        float wanted = 300f + Mathf.Ceil(count / (float)columns) * rowHeight;
        p.card.sizeDelta = new Vector2(p.card.sizeDelta.x, Mathf.Clamp(wanted, minCardHeight, maxHeight));
    }

    private IEnumerator SelectFirstNextFrame()
    {
        yield return null;   // let the layout settle, then put the cursor on a button
        SelectFirst();
    }

    private void SelectFirst()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;

        es.SetSelectedGameObject(null);
        if (firstSelected != null) es.SetSelectedGameObject(firstSelected);
    }

    private void Update()
    {
        if (toastLabel != null && toastLabel.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastLabel.gameObject.SetActive(false);

#if ENABLE_LEGACY_INPUT_MANAGER
        // A click on empty space drops the selection; the next arrow key picks the first card again.
        EventSystem es = EventSystem.current;
        if (es != null && !loading && es.currentSelectedGameObject == null &&
            (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
             Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Return)))
        {
            SelectFirst();
        }
#endif
    }

    // ------------------------------------------------------------ actions

    private void StartStage(StageCatalog.Entry entry)
    {
        if (loading || entry == null) return;

        string wanted = catalog != null ? catalog.stageScene : fallbackStageScene;
        string scene = StageUi.ResolveScene(wanted, fallbackStageScene);
        if (scene == null)
        {
            Toast("The scene '" + wanted + "' is not in Build Settings (File > Build Profiles > Scene List).");
            return;
        }

        loading = true;
        StageSelection.Select(entry.id, entry.mode);
        PvpMode.Enabled = entry.mode == StageMode.Pvp;
        if (PvpMode.Enabled) PvpMode.RequireTwoPlayers();
        StageUi.FadeAndLoad(this, scene);
    }

    /// <summary>BACK (and Esc / B): returns to the Main Menu scene.</summary>
    public void Back()
    {
        if (loading) return;

        string scene = StageUi.ResolveScene(mainMenuScene, "Mainmenu", "MainMenu", "Main Menu");
        if (scene == null)
        {
            Toast("The scene '" + mainMenuScene + "' is not in Build Settings (File > Build Profiles > Scene List).");
            return;
        }

        loading = true;
        StageUi.FadeAndLoad(this, scene);
    }

    /// <summary>The FIRST TO n button on the PVP page: 1 -> 2 -> 3 -> 1.</summary>
    public void CycleRounds()
    {
        PvpMode.RoundsToWin = PvpMode.RoundsToWin % 3 + 1;
        RefreshRoundsLabel();
    }

    private void RefreshRoundsLabel()
    {
        if (pvpPage.roundsLabel != null) pvpPage.roundsLabel.text = roundsFormat.Replace("{0}", PvpMode.RoundsToWin.ToString());
    }

    private void Toast(string message)
    {
        Debug.LogWarning("LevelSelectController: " + message, this);
        if (toastLabel == null) return;
        toastLabel.text = message;
        toastLabel.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 5f;
    }
}
