using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The main menu. It REPLACES the old MainMenu component on the Main Menu scene (the old script file is not touched; the scene just
/// stops using it).
///
/// THE SCREENS ARE REAL OBJECTS IN THE SCENE. Under "Menu Canvas" you'll find "Main Page" and "Difficulty Page"; select anything in the
/// Hierarchy and move / resize / recolour it in the Scene view like any other UI. Only one page is switched on at a time while you edit
/// (tick the other page's checkbox in the Inspector, or use the Show buttons on this component, to edit it). This script only decides which
/// page is showing and what the buttons do.
///
///   PLAY            Easy / Normal / Hard (the Difficulty Page)  ->  the normal game (Crawler scene). PVP is OFF. Same as before.
///   LEVEL SELECT    loads the LevelSelect scene on its Level Select page (every Puzzle stage in the catalog).
///   PVP             loads the LevelSelect scene on its PVP Arenas page.
///
/// What the buttons do is set in each button's OnClick list (they call the public methods below), so you can add or rewire buttons in the
/// Inspector. To add a button, duplicate an existing one (Ctrl+D) so it keeps its highlight script, then change its OnClick.
///
/// The first time the scene is opened in the editor the UI is built for you (MenuUiBakeTool); "Build / Rebuild Menu UI" on this component
/// puts the original layout back. If the UI objects are missing when the game starts, the menu is built in code like it used to be.
///
/// The stage cards live in the LevelSelect scene (see LevelSelectController). The stage list comes from StageCatalog
/// (Assets/Resources/StageCatalog.asset), which is rebuilt from the Stage Definition objects in the Puzzle scene whenever you save it.
///
/// Works with mouse, keyboard (arrows / Enter / Esc) and controller (D-pad or stick / A / B) through the normal EventSystem.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Main Menu Controller")]
public class MainMenuController : MonoBehaviour
{
    [Header("Scenes (must be in Build Settings)")]
    [Tooltip("The normal game that Easy / Normal / Hard start. Same name the old menu used.")]
    [SerializeField] private string crawlerScene = "Crawler";
    [Tooltip("The scene LEVEL SELECT and PVP open. 'Level Select' and 'LevelSelect' are both found, whatever the capitals.")]
    [SerializeField] private string levelSelectScene = "LevelSelect";

    [Header("The UI (objects in this scene)")]
    [Tooltip("The Menu Canvas. Filled in for you by Build / Rebuild Menu UI.")]
    [SerializeField] private Canvas canvas;
    [Tooltip("The first page: title and the PLAY / LEVEL SELECT / PVP buttons.")]
    [SerializeField] private GameObject mainPage;
    [Tooltip("The button that is highlighted when the Main Page opens (keyboard / controller start here).")]
    [SerializeField] private Selectable mainFirstSelected;
    [Tooltip("The Easy / Normal / Hard page.")]
    [SerializeField] private GameObject difficultyPage;
    [Tooltip("The button that is highlighted when the Difficulty Page opens.")]
    [SerializeField] private Selectable difficultyFirstSelected;
    [Tooltip("A red line of text for problems such as a scene missing from Build Settings. Hidden until it is needed.")]
    [SerializeField] private TMP_Text toastLabel;

    [Header("Stage counts under the LEVEL SELECT and PVP buttons")]
    [Tooltip("ON: the small line under LEVEL SELECT and PVP is rewritten from the stage catalog when the menu opens. OFF: it keeps whatever text you typed in the Scene view.")]
    [SerializeField] private bool updateStageCounts = true;
    [SerializeField] private TMP_Text levelSelectCountLabel;
    [SerializeField] private TMP_Text pvpCountLabel;
    [Tooltip("{0} becomes the number of stages.")]
    [SerializeField] private string levelSelectCountFormat = "{0} puzzle stages";
    [Tooltip("{0} becomes the number of arenas.")]
    [SerializeField] private string pvpCountFormat = "2 players  -  {0} arenas";
    [SerializeField] private string noLevelsText = "No stages found yet";
    [SerializeField] private string noArenasText = "No arenas found yet";

    [Header("Used only when the UI is built or rebuilt")]
    [Tooltip("Draws the game name on the main page. Untick it if the Main_Menu prefab already shows your title. (Once the UI is built, just edit or delete the Title object instead.)")]
    [SerializeField] private bool showTitle = true;
    [SerializeField] private string gameTitle = "HALFLIGHT";
    [SerializeField] private string tagline = "LOCAL CO-OP";
    [Tooltip("A soft blue (Player 1) and orange (Player 2) glow down the two sides of the screen.")]
    [SerializeField] private bool sideGlow = true;
    [SerializeField, HideInInspector] private bool uiBuilt;

    private enum MenuPage { Main, Difficulty }

    private sealed class PageState
    {
        public GameObject root;
        public RectTransform rect;
        public CanvasGroup group;
        public Selectable first;
        public Vector2 home;
        public GameObject last;
    }

    private StageCatalog catalog;
    private MenuPage current = MenuPage.Main;
    private bool loading;
    private Coroutine showRoutine;
    private float toastUntil;

    private readonly Dictionary<MenuPage, PageState> pages = new Dictionary<MenuPage, PageState>();

    // ------------------------------------------------------------ for the editor tool

    /// <summary>True once the screens exist in the scene (or were built at runtime).</summary>
    public bool HasUi { get { return mainPage != null && difficultyPage != null; } }

    /// <summary>True once the editor has built the UI into this scene at least once, so it is never rebuilt behind your back.</summary>
    public bool UiBuilt { get { return uiBuilt; } }

    public Canvas MenuCanvas { get { return canvas; } }
    public GameObject MainPageObject { get { return mainPage; } }
    public GameObject DifficultyPageObject { get { return difficultyPage; } }

    /// <summary>What the builder needs to know to lay the screens out (the title text, the stage counts, ...).</summary>
    public MenuUiBuilder.Options BuildOptions(bool bake)
    {
        StageCatalog cat = StageCatalog.Load();
        int puzzle = cat != null ? cat.Get(StageMode.Puzzle).Count : 0;
        int pvp = cat != null ? cat.Get(StageMode.Pvp).Count : 0;

        MenuUiBuilder.Options o = new MenuUiBuilder.Options();
        o.bake = bake;
        o.showTitle = showTitle;
        o.title = gameTitle;
        o.tagline = tagline;
        o.sideGlow = sideGlow;
        o.puzzleCount = puzzle;
        o.pvpCount = pvp;
        o.levelSelectSubText = CountText(levelSelectCountFormat, noLevelsText, puzzle);
        o.pvpSubText = CountText(pvpCountFormat, noArenasText, pvp);
        return o;
    }

    /// <summary>Takes over the objects MenuUiBuilder just made. Called by the builder's callers; you don't need to.</summary>
    public void ApplyBuiltUi(MenuUiBuilder.MainMenuRefs refs)
    {
        canvas = refs.canvas;
        mainPage = refs.mainPage;
        mainFirstSelected = refs.mainFirst;
        difficultyPage = refs.difficultyPage;
        difficultyFirstSelected = refs.difficultyFirst;
        toastLabel = refs.toast;
        levelSelectCountLabel = refs.levelSelectSub;
        pvpCountLabel = refs.pvpSub;
        uiBuilt = true;
    }

    // ------------------------------------------------------------ start

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Back on the menu: nothing PVP-ish may leak into the normal game.
        PvpMode.Enabled = false;
        PvpMode.ResetMatch();

        // Something still asking for "the stage list" through the menu: forward it to the Level Select scene.
        if (StageSelection.ReturnToStageSelect)
        {
            StageSelection.ReturnToStageSelect = false;
            if (GoToLevelSelect(StageSelection.Mode == StageMode.Pvp ? StageMode.Pvp : StageMode.Puzzle, true)) return;
        }

        catalog = StageCatalog.Load();
        StageUi.EnsureEventSystem();

        if (!HasUi) BuildUiAtRuntime();

        RegisterPages();
        RefreshStageCounts();
        HookCancel();
        if (toastLabel != null) toastLabel.gameObject.SetActive(false);

        Show(MenuPage.Main, true);
    }

    private void BuildUiAtRuntime()
    {
        Debug.LogWarning("MainMenuController: the menu UI isn't in this scene, so it was built in code just now. Select 'Main Menu Controller' and press " +
                         "'Build / Rebuild Menu UI' in the Inspector to put it in the scene, where you can see and edit it.", this);
        ApplyBuiltUi(MenuUiBuilder.BuildMainMenu(this, BuildOptions(false)));
    }

    private void RegisterPages()
    {
        pages.Clear();
        Register(MenuPage.Main, mainPage, mainFirstSelected);
        Register(MenuPage.Difficulty, difficultyPage, difficultyFirstSelected);
    }

    private void Register(MenuPage id, GameObject root, Selectable first)
    {
        if (root == null) return;

        PageState state = new PageState();
        state.root = root;
        state.rect = root.transform as RectTransform;
        state.group = root.GetComponent<CanvasGroup>();
        if (state.group == null) state.group = root.AddComponent<CanvasGroup>();
        state.first = first;
        state.home = state.rect != null ? state.rect.anchoredPosition : Vector2.zero;
        pages[id] = state;
    }

    // Esc / B on any button goes back (only the Difficulty Page has a back to go to).
    private void HookCancel()
    {
        StageButtonFx[] all = FindObjectsByType<StageButtonFx>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++) all[i].onCancel = Back;
    }

    private void RefreshStageCounts()
    {
        if (!updateStageCounts) return;

        int puzzle = catalog != null ? catalog.Get(StageMode.Puzzle).Count : 0;
        int pvp = catalog != null ? catalog.Get(StageMode.Pvp).Count : 0;
        if (levelSelectCountLabel != null) levelSelectCountLabel.text = CountText(levelSelectCountFormat, noLevelsText, puzzle);
        if (pvpCountLabel != null) pvpCountLabel.text = CountText(pvpCountFormat, noArenasText, pvp);
    }

    private static string CountText(string format, string none, int count)
    {
        return count > 0 ? format.Replace("{0}", count.ToString()) : none;
    }

    private void Update()
    {
        if (toastLabel != null && toastLabel.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastLabel.gameObject.SetActive(false);

#if ENABLE_LEGACY_INPUT_MANAGER
        // A click on empty space drops the selection; the next arrow key picks the page's button again.
        EventSystem es = EventSystem.current;
        PageState page;
        if (es != null && !loading && es.currentSelectedGameObject == null && pages.TryGetValue(current, out page) &&
            (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
             Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Return)))
        {
            SelectDefault(page);
        }
#endif
    }

    // ------------------------------------------------------------ actions (the buttons' OnClick lists call these)

    /// <summary>PLAY: shows the Easy / Normal / Hard page.</summary>
    public void OpenDifficulty()
    {
        if (loading) return;
        Show(MenuPage.Difficulty, false);
    }

    /// <summary>LEVEL SELECT: loads the Level Select scene on its puzzle-stage page.</summary>
    public void OpenLevelSelect()
    {
        GoToLevelSelect(StageMode.Puzzle, false);
    }

    /// <summary>PVP: loads the Level Select scene on its PVP-arena page.</summary>
    public void OpenPvp()
    {
        GoToLevelSelect(StageMode.Pvp, false);
    }

    public void StartEasy() { StartGame(FoeController.Difficulty.Easy); }
    public void StartNormal() { StartGame(FoeController.Difficulty.Normal); }
    public void StartHard() { StartGame(FoeController.Difficulty.Hard); }

    /// <summary>BACK on the Difficulty Page (and Esc / B): back to the Main Page.</summary>
    public void Back()
    {
        if (loading || current == MenuPage.Main) return;
        Show(MenuPage.Main, false);
    }

    private void StartGame(FoeController.Difficulty difficulty)
    {
        if (loading) return;

        string scene = StageUi.ResolveScene(crawlerScene, "Crawler");
        if (scene == null)
        {
            Toast("The scene '" + crawlerScene + "' is not in Build Settings (File > Build Profiles > Scene List).");
            return;
        }

        loading = true;
        FoeController.CurrentDifficulty = difficulty;
        StageSelection.Clear();
        PvpMode.Enabled = false;
        StageUi.FadeAndLoad(this, scene);
    }

    // Returns true if the scene is being loaded.
    private bool GoToLevelSelect(StageMode mode, bool immediately)
    {
        if (loading) return true;

        string scene = StageUi.ResolveScene(levelSelectScene, "LevelSelect", "Level Select");
        if (scene == null)
        {
            Toast("The scene '" + levelSelectScene + "' is not in Build Settings (File > Build Profiles > Scene List).");
            return false;
        }

        loading = true;
        StageSelection.RequestBrowse(mode);
        if (immediately) UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        else StageUi.FadeAndLoad(this, scene);
        return true;
    }

    private void Toast(string message)
    {
        Debug.LogWarning("MainMenuController: " + message, this);
        if (toastLabel == null) return;
        toastLabel.text = message;
        toastLabel.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 5f;
    }

    // ------------------------------------------------------------ pages: show / hide

    private void Show(MenuPage page, bool instant)
    {
        PageState target;
        if (!pages.TryGetValue(page, out target)) return;

        EventSystem es = EventSystem.current;
        PageState old;
        if (es != null && es.currentSelectedGameObject != null && pages.TryGetValue(current, out old)) old.last = es.currentSelectedGameObject;

        foreach (KeyValuePair<MenuPage, PageState> kv in pages) kv.Value.root.SetActive(false);

        current = page;
        target.root.SetActive(true);

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowRoutine(target, instant));
    }

    private IEnumerator ShowRoutine(PageState page, bool instant)
    {
        if (!instant)
        {
            page.group.alpha = 0f;
            float t = 0f;
            while (t < 0.22f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.22f);
                k = 1f - (1f - k) * (1f - k);
                page.group.alpha = k;
                if (page.rect != null) page.rect.anchoredPosition = page.home + new Vector2(0f, -30f * (1f - k));
                yield return null;
            }
        }
        page.group.alpha = 1f;
        if (page.rect != null) page.rect.anchoredPosition = page.home;

        yield return null;   // let the layout settle, then put the cursor on a button
        SelectDefault(page);
    }

    private void SelectDefault(PageState page)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;

        GameObject target = null;
        if (page.last != null && page.last.activeInHierarchy) target = page.last;
        else if (page.first != null) target = page.first.gameObject;

        es.SetSelectedGameObject(null);
        if (target != null) es.SetSelectedGameObject(target);
    }
}
