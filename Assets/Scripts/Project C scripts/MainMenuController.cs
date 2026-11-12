using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The main menu. It REPLACES the old MainMenu component on the Main Menu scene (the old script file is not touched; the scene just
/// stops using it). The screens are built in code the moment the scene starts, in the same spot the old buttons were, on top of
/// whatever background the Main_Menu prefab draws.
///
///   PLAY            Easy / Normal / Hard  ->  the normal game (Crawler scene). PVP is OFF. Same as before.
///   LEVEL SELECT    every Puzzle stage in the catalog, as picture cards  ->  Puzzle scene on that stage. PVP is OFF.
///   PVP             every Pvp stage in the catalog + "First to N"  ->  Puzzle scene on that arena. PVP is ON.
///
/// The stage cards come from StageCatalog (Assets/Resources/StageCatalog.asset), which is rebuilt from the Stage Definition objects in
/// the Puzzle scene whenever you save it. So adding or removing a stage never touches this script or this scene.
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
    [Tooltip("Only used if there is no StageCatalog asset yet. The catalog normally knows the stage scene by itself.")]
    [SerializeField] private string fallbackStageScene = "Puzzle";

    [Header("Look")]
    [Tooltip("Draws the game name on the main page. Untick it if the Main_Menu prefab already shows your title.")]
    [SerializeField] private bool showTitle = true;
    [SerializeField] private string gameTitle = "HALFLIGHT";
    [SerializeField] private string tagline = "LOCAL CO-OP";
    [Tooltip("A soft blue (Player 1) and orange (Player 2) glow down the two sides of the screen.")]
    [SerializeField] private bool sideGlow = true;

    private enum MenuPage { Main, Difficulty, LevelSelect, Pvp }

    private RectTransform root;
    private StageCatalog catalog;
    private MenuPage current = MenuPage.Main;
    private bool loading;
    private Coroutine showRoutine;
    private TextMeshProUGUI toast;
    private float toastUntil;
    private TextMeshProUGUI roundsLabel;

    private readonly Dictionary<MenuPage, RectTransform> pages = new Dictionary<MenuPage, RectTransform>();
    private readonly Dictionary<MenuPage, GameObject> firstSelected = new Dictionary<MenuPage, GameObject>();
    private readonly Dictionary<MenuPage, GameObject> lastSelected = new Dictionary<MenuPage, GameObject>();

    // ------------------------------------------------------------ start

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        // Back on the menu: nothing PVP-ish may leak into the normal game.
        PvpMode.Enabled = false;
        PvpMode.ResetMatch();

        catalog = StageCatalog.Load();
        StageUi.EnsureEventSystem();
        BuildUi();

        MenuPage start = MenuPage.Main;
        if (StageSelection.ReturnToStageSelect)
        {
            StageSelection.ReturnToStageSelect = false;
            start = StageSelection.Mode == StageMode.Pvp ? MenuPage.Pvp : MenuPage.LevelSelect;
        }
        Show(start, true);
    }

    private void Update()
    {
        if (toast != null && toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);

#if ENABLE_LEGACY_INPUT_MANAGER
        // A click on empty space drops the selection; the next arrow key picks the page's button again.
        EventSystem es = EventSystem.current;
        if (es != null && !loading && es.currentSelectedGameObject == null &&
            (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
             Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Return)))
        {
            SelectDefault(current);
        }
#endif
    }

    // ------------------------------------------------------------ actions

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

    private void Back()
    {
        if (loading || current == MenuPage.Main) return;
        Show(MenuPage.Main, false);
    }

    private void CycleRounds()
    {
        PvpMode.RoundsToWin = PvpMode.RoundsToWin % 3 + 1;
        RefreshRoundsLabel();
    }

    private void RefreshRoundsLabel()
    {
        if (roundsLabel != null) roundsLabel.text = "FIRST TO " + PvpMode.RoundsToWin;
    }

    private void Toast(string message)
    {
        if (toast == null) return;
        toast.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 5f;
    }

    // ------------------------------------------------------------ pages: show / hide

    private void Show(MenuPage page, bool instant)
    {
        EventSystem es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) lastSelected[current] = es.currentSelectedGameObject;

        foreach (KeyValuePair<MenuPage, RectTransform> kv in pages) kv.Value.gameObject.SetActive(false);

        current = page;
        RectTransform rect = pages[page];
        rect.gameObject.SetActive(true);

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowRoutine(rect, instant, page));
    }

    private IEnumerator ShowRoutine(RectTransform rect, bool instant, MenuPage page)
    {
        CanvasGroup group = rect.GetComponent<CanvasGroup>();

        if (!instant)
        {
            group.alpha = 0f;
            float t = 0f;
            while (t < 0.22f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.22f);
                k = 1f - (1f - k) * (1f - k);
                group.alpha = k;
                rect.anchoredPosition = new Vector2(0f, -30f * (1f - k));
                yield return null;
            }
        }
        group.alpha = 1f;
        rect.anchoredPosition = Vector2.zero;

        yield return null;   // let the layout settle, then put the cursor on a button
        SelectDefault(page);
    }

    private void SelectDefault(MenuPage page)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;

        GameObject target;
        if (!lastSelected.TryGetValue(page, out target) || target == null || !target.activeInHierarchy)
        {
            firstSelected.TryGetValue(page, out target);
        }

        es.SetSelectedGameObject(null);
        if (target != null) es.SetSelectedGameObject(target);
    }

    // ------------------------------------------------------------ build

    private void BuildUi()
    {
        Canvas canvas = StageUi.CreateCanvas("Menu Canvas", 50);
        root = (RectTransform)canvas.transform;

        Image dim = StageUi.Img("Backdrop", root, new Color(0.02f, 0.03f, 0.07f, 0.55f));
        StageUi.Stretch(dim.rectTransform);

        if (sideGlow)
        {
            Color b0 = new Color(StageUi.Blue.r, StageUi.Blue.g, StageUi.Blue.b, 0.38f);
            Color b1 = new Color(StageUi.Blue.r, StageUi.Blue.g, StageUi.Blue.b, 0f);
            Color o0 = new Color(StageUi.Orange.r, StageUi.Orange.g, StageUi.Orange.b, 0f);
            Color o1 = new Color(StageUi.Orange.r, StageUi.Orange.g, StageUi.Orange.b, 0.38f);

            Image left = StageUi.Img("Glow Left", root, Color.white, StageUi.HorizontalGradient(b0, b1));
            left.rectTransform.anchorMin = new Vector2(0f, 0f);
            left.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            left.rectTransform.offsetMin = Vector2.zero;
            left.rectTransform.offsetMax = Vector2.zero;

            Image right = StageUi.Img("Glow Right", root, Color.white, StageUi.HorizontalGradient(o0, o1));
            right.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            right.rectTransform.anchorMax = new Vector2(1f, 1f);
            right.rectTransform.offsetMin = Vector2.zero;
            right.rectTransform.offsetMax = Vector2.zero;
        }

        BuildMainPage();
        BuildDifficultyPage();
        BuildStagePage(MenuPage.LevelSelect, StageMode.Puzzle, "LEVEL SELECT", StageUi.Violet, false);
        BuildStagePage(MenuPage.Pvp, StageMode.Pvp, "PVP ARENAS", StageUi.Orange, true);

        TextMeshProUGUI hint = StageUi.Label("Hint", root,
            "Arrow keys / D-pad: move      Enter / A: select      Esc / B: back",
            24f, StageUi.TextDim, TextAlignmentOptions.Center);
        StageUi.Bottom(hint.rectTransform, new Vector2(1400f, 40f), 18f);

        toast = StageUi.Label("Toast", root, "", 28f, StageUi.Red, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Bottom(toast.rectTransform, new Vector2(1600f, 50f), 70f);
        toast.gameObject.SetActive(false);
    }

    private RectTransform NewPage(MenuPage page)
    {
        RectTransform rect = StageUi.Rect(page + " Page", root);
        StageUi.Stretch(rect);
        rect.gameObject.AddComponent<CanvasGroup>();
        rect.gameObject.SetActive(false);
        pages[page] = rect;
        return rect;
    }

    // ---- main page

    private void BuildMainPage()
    {
        RectTransform page = NewPage(MenuPage.Main);

        float firstButtonY = showTitle ? 280f : 70f;
        Image card = StageUi.Img("Card", page, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(720f, firstButtonY + 400f), Vector2.zero);

        if (showTitle)
        {
            TextMeshProUGUI title = StageUi.Label("Title", card.transform, gameTitle, 104f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(StageUi.Blue, StageUi.Orange, StageUi.Blue, StageUi.Orange);
            title.characterSpacing = 8f;
            StageUi.Top(title.rectTransform, new Vector2(680f, 130f), 56f);

            TextMeshProUGUI sub = StageUi.Label("Tagline", card.transform, tagline, 28f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            sub.characterSpacing = 18f;
            StageUi.Top(sub.rectTransform, new Vector2(680f, 40f), 195f);
        }

        int puzzleCount = catalog != null ? catalog.Get(StageMode.Puzzle).Count : 0;
        int pvpCount = catalog != null ? catalog.Get(StageMode.Pvp).Count : 0;

        Button play = MenuButton(card.transform, "PLAY", "Easy  /  Normal  /  Hard", StageUi.Blue, firstButtonY, () => Show(MenuPage.Difficulty, false));
        MenuButton(card.transform, "LEVEL SELECT", puzzleCount > 0 ? puzzleCount + " puzzle stages" : "No stages found yet",
                   StageUi.Violet, firstButtonY + 120f, () => Show(MenuPage.LevelSelect, false));
        MenuButton(card.transform, "PVP", pvpCount > 0 ? "2 players  -  " + pvpCount + " arenas" : "No arenas found yet",
                   StageUi.Orange, firstButtonY + 240f, () => Show(MenuPage.Pvp, false));

        firstSelected[MenuPage.Main] = play.gameObject;
    }

    private Button MenuButton(Transform parent, string text, string sub, Color accent, float y, UnityAction onClick)
    {
        Image bg;
        Button button = StageUi.MakeButton(text, parent, StageUi.Card, StageUi.CardHot, out bg);
        StageUi.Top(bg.rectTransform, new Vector2(600f, 100f), y);

        Image bar = StageUi.Img("Accent", bg.transform, accent, StageUi.Rounded, true);
        bar.rectTransform.anchorMin = new Vector2(0f, 0f);
        bar.rectTransform.anchorMax = new Vector2(0f, 1f);
        bar.rectTransform.pivot = new Vector2(0f, 0.5f);
        bar.rectTransform.sizeDelta = new Vector2(12f, -28f);
        bar.rectTransform.anchoredPosition = new Vector2(16f, 0f);

        TextMeshProUGUI label = StageUi.Label("Label", bg.transform, text, 42f, StageUi.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        label.characterSpacing = 4f;
        Fill(label.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 1f), new Vector2(50f, 0f), new Vector2(-16f, -6f));

        TextMeshProUGUI small = StageUi.Label("Sub", bg.transform, sub, 23f, StageUi.TextDim, TextAlignmentOptions.MidlineLeft);
        Fill(small.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(50f, 8f), new Vector2(-16f, 0f));

        button.onClick.AddListener(onClick);
        bg.GetComponent<StageButtonFx>().onCancel = Back;
        return button;
    }

    // ---- difficulty page

    private void BuildDifficultyPage()
    {
        RectTransform page = NewPage(MenuPage.Difficulty);

        Image card = StageUi.Img("Card", page, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(1360f, 640f), Vector2.zero);

        TextMeshProUGUI header = StageUi.Label("Header", card.transform, "CHOOSE DIFFICULTY", 60f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        header.characterSpacing = 6f;
        StageUi.Top(header.rectTransform, new Vector2(1200f, 80f), 36f);

        Button easy = OptionCard(card.transform, "EASY", "Foes hit softer and arrive slower. A good way to learn the rooms. Lower score.",
                                 StageUi.Green, -410f, () => StartGame(FoeController.Difficulty.Easy));
        OptionCard(card.transform, "NORMAL", "The intended game. Balanced damage, foes and score.",
                   StageUi.Blue, 0f, () => StartGame(FoeController.Difficulty.Normal));
        OptionCard(card.transform, "HARD", "Foes hit harder and swarm faster. Higher score for every kill.",
                   StageUi.Red, 410f, () => StartGame(FoeController.Difficulty.Hard));

        BackButton(card.transform);
        firstSelected[MenuPage.Difficulty] = easy.gameObject;
    }

    private Button OptionCard(Transform parent, string title, string body, Color accent, float x, UnityAction onClick)
    {
        Image bg;
        Button button = StageUi.MakeButton(title, parent, StageUi.Card, StageUi.CardHot, out bg);
        StageUi.Center(bg.rectTransform, new Vector2(380f, 360f), new Vector2(x, -20f));

        Image bar = StageUi.Img("Accent", bg.transform, accent, StageUi.Rounded, true);
        bar.rectTransform.anchorMin = new Vector2(0f, 1f);
        bar.rectTransform.anchorMax = new Vector2(1f, 1f);
        bar.rectTransform.pivot = new Vector2(0.5f, 1f);
        bar.rectTransform.sizeDelta = new Vector2(-48f, 12f);
        bar.rectTransform.anchoredPosition = new Vector2(0f, -22f);

        TextMeshProUGUI t = StageUi.Label("Title", bg.transform, title, 62f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        t.characterSpacing = 4f;
        Fill(t.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -125f), new Vector2(-10f, -48f));

        TextMeshProUGUI d = StageUi.Label("Body", bg.transform, body, 27f, StageUi.TextDim, TextAlignmentOptions.Top);
        d.overflowMode = TextOverflowModes.Overflow;
        Fill(d.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(34f, 24f), new Vector2(-34f, -140f));

        button.onClick.AddListener(onClick);
        bg.GetComponent<StageButtonFx>().onCancel = Back;
        return button;
    }

    private Button BackButton(Transform parent)
    {
        Image bg;
        Button button = StageUi.MakeButton("Back", parent, StageUi.Card, StageUi.CardHot, out bg);
        bg.rectTransform.anchorMin = new Vector2(0f, 0f);
        bg.rectTransform.anchorMax = new Vector2(0f, 0f);
        bg.rectTransform.pivot = new Vector2(0f, 0f);
        bg.rectTransform.sizeDelta = new Vector2(240f, 68f);
        bg.rectTransform.anchoredPosition = new Vector2(40f, 28f);

        TextMeshProUGUI label = StageUi.Label("Label", bg.transform, "BACK", 32f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        label.characterSpacing = 4f;
        StageUi.Stretch(label.rectTransform);

        button.onClick.AddListener(Back);
        bg.GetComponent<StageButtonFx>().onCancel = Back;
        return button;
    }

    // ---- stage pages (Level Select and PVP share this)

    private void BuildStagePage(MenuPage pageId, StageMode mode, string headerText, Color accent, bool withRounds)
    {
        RectTransform page = NewPage(pageId);
        List<StageCatalog.Entry> entries = catalog != null ? catalog.Get(mode) : new List<StageCatalog.Entry>();

        float cardHeight = Mathf.Clamp(300f + Mathf.Ceil(entries.Count / 3f) * 354f, 640f, 960f);

        Image card = StageUi.Img("Card", page, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(1560f, cardHeight), Vector2.zero);

        TextMeshProUGUI header = StageUi.Label("Header", card.transform, headerText, 60f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        header.characterSpacing = 6f;
        StageUi.Top(header.rectTransform, new Vector2(1200f, 80f), 28f);

        string countText = entries.Count == 1 ? "1 stage" : entries.Count + " stages";
        TextMeshProUGUI count = StageUi.Label("Count", card.transform, countText, 26f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        count.characterSpacing = 8f;
        StageUi.Top(count.rectTransform, new Vector2(600f, 36f), 100f);

        Button back = BackButton(card.transform);
        GameObject first = back.gameObject;

        if (withRounds)
        {
            Image rb;
            Button rounds = StageUi.MakeButton("Rounds", card.transform, StageUi.Card, StageUi.CardHot, out rb);
            rb.rectTransform.anchorMin = new Vector2(1f, 0f);
            rb.rectTransform.anchorMax = new Vector2(1f, 0f);
            rb.rectTransform.pivot = new Vector2(1f, 0f);
            rb.rectTransform.sizeDelta = new Vector2(320f, 68f);
            rb.rectTransform.anchoredPosition = new Vector2(-40f, 28f);

            roundsLabel = StageUi.Label("Label", rb.transform, "", 30f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
            roundsLabel.characterSpacing = 4f;
            StageUi.Stretch(roundsLabel.rectTransform);
            RefreshRoundsLabel();

            rounds.onClick.AddListener(CycleRounds);
            rb.GetComponent<StageButtonFx>().onCancel = Back;
        }

        if (entries.Count == 0)
        {
            TextMeshProUGUI empty = StageUi.Label("Empty", card.transform,
                "No stages found.\nOpen the Puzzle scene, add Stage Definition objects, and save it\n(or use Tools > Stages > Rebuild Stage Catalog From Open Scene).",
                28f, StageUi.TextDim, TextAlignmentOptions.Center);
            empty.overflowMode = TextOverflowModes.Overflow;
            StageUi.Center(empty.rectTransform, new Vector2(1200f, 200f), new Vector2(0f, 10f));
            firstSelected[pageId] = first;
            return;
        }

        // scroll area
        RectTransform viewport = StageUi.Rect("Viewport", card.transform);
        Fill(viewport, Vector2.zero, Vector2.one, new Vector2(30f, 124f), new Vector2(-30f, -146f));
        Image hit = viewport.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = StageUi.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(470f, 330f);
        grid.spacing = new Vector2(24f, 24f);
        grid.padding = new RectOffset(14, 14, 14, 14);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        for (int i = 0; i < entries.Count; i++)
        {
            Button cardButton = StageCard(content, entries[i], i + 1, accent);
            if (i == 0) first = cardButton.gameObject;
        }

        firstSelected[pageId] = first;
    }

    private Button StageCard(Transform parent, StageCatalog.Entry entry, int number, Color accent)
    {
        Image bg;
        Button button = StageUi.MakeButton(string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName,
                                           parent, StageUi.Card, StageUi.CardHot, out bg);

        // picture (or a big number when the stage has no picture yet)
        Image thumb = StageUi.Img("Thumbnail", bg.transform, entry.thumbnail != null ? Color.white : new Color(0.06f, 0.08f, 0.13f, 1f));
        Fill(thumb.rectTransform, new Vector2(0f, 0.22f), new Vector2(1f, 1f), new Vector2(12f, 0f), new Vector2(-12f, -12f));
        if (entry.thumbnail != null)
        {
            thumb.sprite = entry.thumbnail;
            thumb.preserveAspect = true;
        }
        else
        {
            TextMeshProUGUI big = StageUi.Label("Number", thumb.transform, number.ToString("00"), 110f, StageUi.TextDim,
                                                TextAlignmentOptions.Center, FontStyles.Bold);
            StageUi.Stretch(big.rectTransform);
        }

        // number pill, top-left over the picture
        Image pill = StageUi.Img("Pill", bg.transform, accent, StageUi.Rounded, true);
        pill.rectTransform.anchorMin = new Vector2(0f, 1f);
        pill.rectTransform.anchorMax = new Vector2(0f, 1f);
        pill.rectTransform.pivot = new Vector2(0f, 1f);
        pill.rectTransform.sizeDelta = new Vector2(64f, 40f);
        pill.rectTransform.anchoredPosition = new Vector2(22f, -22f);
        TextMeshProUGUI pillText = StageUi.Label("Text", pill.transform, number.ToString("00"), 26f, new Color(0.05f, 0.06f, 0.1f, 1f),
                                                 TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Stretch(pillText.rectTransform);

        // name
        string title = string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName;
        TextMeshProUGUI nameText = StageUi.Label("Name", bg.transform, title, 34f, StageUi.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Fill(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.22f), new Vector2(20f, 4f), new Vector2(-20f, 0f));

        StageCatalog.Entry captured = entry;
        button.onClick.AddListener(() => StartStage(captured));
        bg.GetComponent<StageButtonFx>().onCancel = Back;
        return button;
    }

    private static void Fill(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
