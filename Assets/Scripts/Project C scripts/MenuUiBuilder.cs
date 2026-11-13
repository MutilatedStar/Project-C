using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds the menu screens out of ordinary UI objects (Canvas, Image, Button, TextMeshPro). This is the code that used to run every time the
/// menu opened; it now has two jobs:
///
///   BAKING (editor)     MenuUiBakeTool calls it once, in the editor, so the screens become REAL objects in the saved scene that you can
///                       see in the Scene view, select in the Hierarchy and restyle. The buttons' OnClick lists are saved in the scene too.
///   FALLBACK (runtime)  If a scene has no baked UI (the objects were deleted, or the bake never ran), the controller calls it when the
///                       scene starts, so the menu still works exactly like it used to.
///
/// You normally never call this yourself. Use the "Build / Rebuild Menu UI" button on Main Menu Controller / Level Select Controller.
/// </summary>
public static class MenuUiBuilder
{
    public const string HintText = "Arrow keys / D-pad: move      Enter / A: select      Esc / B: back";

    // ------------------------------------------------------------ options and results

    public sealed class Options
    {
        /// <summary>True when building into the saved scene (editor). OnClick calls are then saved in the scene instead of added in code.</summary>
        public bool bake;

        /// <summary>Sprite assets for the side glows (white, fading in alpha, tinted by the Image colour). Null = generate them at runtime.</summary>
        public Sprite glowLeft;
        public Sprite glowRight;

        public bool showTitle = true;
        public string title = "HALFLIGHT";
        public string tagline = "LOCAL CO-OP";
        public bool sideGlow = true;

        /// <summary>Text for the small line under LEVEL SELECT and PVP on the main page, and the stage counts shown on the stage pages.</summary>
        public string levelSelectSubText = "No stages found yet";
        public string pvpSubText = "No arenas found yet";
        public int puzzleCount;
        public int pvpCount;
    }

    public sealed class MainMenuRefs
    {
        public Canvas canvas;
        public GameObject mainPage;
        public GameObject difficultyPage;
        public Selectable mainFirst;
        public Selectable difficultyFirst;
        public TMP_Text levelSelectSub;
        public TMP_Text pvpSub;
        public TMP_Text toast;
    }

    public sealed class StagePageRefs
    {
        public GameObject page;
        public RectTransform card;
        public TMP_Text count;
        public GameObject empty;
        public RectTransform content;
        public StageCardView template;
        public Button back;
        public Button rounds;
        public TMP_Text roundsLabel;
    }

    public sealed class LevelSelectRefs
    {
        public Canvas canvas;
        public StagePageRefs levelSelect;
        public StagePageRefs pvp;
        public TMP_Text toast;
    }

    // ------------------------------------------------------------ main menu

    /// <summary>The Menu Canvas with the main page (title + PLAY / LEVEL SELECT / PVP) and the difficulty page.</summary>
    public static MainMenuRefs BuildMainMenu(MainMenuController c, Options o)
    {
        MainMenuRefs r = new MainMenuRefs();

        Canvas canvas = StageUi.CreateCanvas("Menu Canvas", 50);
        RectTransform root = (RectTransform)canvas.transform;
        r.canvas = canvas;

        BuildBackdrop(root, o);

        // ---- main page
        RectTransform main = NewPage(root, "Main Page", true);
        r.mainPage = main.gameObject;

        float firstButtonY = o.showTitle ? 280f : 70f;
        Image card = StageUi.Img("Card", main, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(720f, firstButtonY + 400f), Vector2.zero);

        if (o.showTitle)
        {
            TextMeshProUGUI title = StageUi.Label("Title", card.transform, o.title, 104f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            title.enableVertexGradient = true;
            title.colorGradient = new VertexGradient(StageUi.Blue, StageUi.Orange, StageUi.Blue, StageUi.Orange);
            title.characterSpacing = 8f;
            StageUi.Top(title.rectTransform, new Vector2(680f, 130f), 56f);

            TextMeshProUGUI sub = StageUi.Label("Tagline", card.transform, o.tagline, 28f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            sub.characterSpacing = 18f;
            StageUi.Top(sub.rectTransform, new Vector2(680f, 40f), 195f);
        }

        TMP_Text ignored;
        Button play = MenuButton(card.transform, "PLAY", "Easy  /  Normal  /  Hard", StageUi.Blue, firstButtonY, c.OpenDifficulty, o, out ignored);
        TMP_Text levelSub;
        MenuButton(card.transform, "LEVEL SELECT", o.levelSelectSubText, StageUi.Violet, firstButtonY + 120f, c.OpenLevelSelect, o, out levelSub);
        TMP_Text pvpSub;
        MenuButton(card.transform, "PVP", o.pvpSubText, StageUi.Orange, firstButtonY + 240f, c.OpenPvp, o, out pvpSub);

        r.mainFirst = play;
        r.levelSelectSub = levelSub;
        r.pvpSub = pvpSub;

        // ---- difficulty page
        RectTransform diff = NewPage(root, "Difficulty Page", true);
        r.difficultyPage = diff.gameObject;

        Image dcard = StageUi.Img("Card", diff, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(dcard.rectTransform, new Vector2(1360f, 640f), Vector2.zero);

        TextMeshProUGUI header = StageUi.Label("Header", dcard.transform, "CHOOSE DIFFICULTY", 60f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        header.characterSpacing = 6f;
        StageUi.Top(header.rectTransform, new Vector2(1200f, 80f), 36f);

        Button easy = OptionCard(dcard.transform, "EASY", "Foes hit softer and arrive slower. A good way to learn the rooms. Lower score.",
                                 StageUi.Green, -410f, c.StartEasy, o);
        OptionCard(dcard.transform, "NORMAL", "The intended game. Balanced damage, foes and score.",
                   StageUi.Blue, 0f, c.StartNormal, o);
        OptionCard(dcard.transform, "HARD", "Foes hit harder and swarm faster. Higher score for every kill.",
                   StageUi.Red, 410f, c.StartHard, o);
        BackButton(dcard.transform, c.Back, o);
        r.difficultyFirst = easy;

        // The page that is not being edited starts switched off, so the two don't sit on top of each other in the Scene view.
        diff.gameObject.SetActive(false);

        BuildHintAndToast(root, out r.toast);
        return r;
    }

    private static Button MenuButton(Transform parent, string text, string sub, Color accent, float y, UnityAction onClick, Options o, out TMP_Text subLabel)
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

        Wire(button, onClick, o);
        subLabel = small;
        return button;
    }

    private static Button OptionCard(Transform parent, string title, string body, Color accent, float x, UnityAction onClick, Options o)
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

        Wire(button, onClick, o);
        return button;
    }

    // ------------------------------------------------------------ level select

    /// <summary>The Level Select Canvas with the Level Select page and the PVP Arenas page (each with a template stage card).</summary>
    public static LevelSelectRefs BuildLevelSelect(LevelSelectController c, Options o)
    {
        LevelSelectRefs r = new LevelSelectRefs();

        Canvas canvas = StageUi.CreateCanvas("Level Select Canvas", 50);
        RectTransform root = (RectTransform)canvas.transform;
        r.canvas = canvas;

        BuildBackdrop(root, o);

        r.levelSelect = BuildStagePage(root, "Level Select Page", "LEVEL SELECT", StageUi.Violet, false, o.puzzleCount, c, o);
        r.pvp = BuildStagePage(root, "Pvp Page", "PVP ARENAS", StageUi.Orange, true, o.pvpCount, c, o);

        // Only one page is shown at a time. Tick / untick the page objects in the Hierarchy (or use the buttons on Level Select Controller) to edit the other.
        r.pvp.page.SetActive(false);

        BuildHintAndToast(root, out r.toast);
        return r;
    }

    private static StagePageRefs BuildStagePage(Transform root, string pageName, string headerText, Color accent, bool withRounds,
                                                int sampleCount, LevelSelectController c, Options o)
    {
        StagePageRefs r = new StagePageRefs();

        RectTransform page = NewPage(root, pageName, false);
        r.page = page.gameObject;

        Image card = StageUi.Img("Card", page, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(1560f, 960f), Vector2.zero);
        r.card = card.rectTransform;

        TextMeshProUGUI header = StageUi.Label("Header", card.transform, headerText, 60f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        header.characterSpacing = 6f;
        StageUi.Top(header.rectTransform, new Vector2(1200f, 80f), 28f);

        TextMeshProUGUI count = StageUi.Label("Count", card.transform, sampleCount == 1 ? "1 stage" : sampleCount + " stages", 26f, accent,
                                              TextAlignmentOptions.Center, FontStyles.Bold);
        count.characterSpacing = 8f;
        StageUi.Top(count.rectTransform, new Vector2(600f, 36f), 100f);
        r.count = count;

        r.back = BackButton(card.transform, c.Back, o);

        if (withRounds)
        {
            Image rb;
            Button rounds = StageUi.MakeButton("Rounds", card.transform, StageUi.Card, StageUi.CardHot, out rb);
            rb.rectTransform.anchorMin = new Vector2(1f, 0f);
            rb.rectTransform.anchorMax = new Vector2(1f, 0f);
            rb.rectTransform.pivot = new Vector2(1f, 0f);
            rb.rectTransform.sizeDelta = new Vector2(320f, 68f);
            rb.rectTransform.anchoredPosition = new Vector2(-40f, 28f);

            TextMeshProUGUI roundsLabel = StageUi.Label("Label", rb.transform, "FIRST TO " + PvpMode.RoundsToWin, 30f, accent,
                                                        TextAlignmentOptions.Center, FontStyles.Bold);
            roundsLabel.characterSpacing = 4f;
            StageUi.Stretch(roundsLabel.rectTransform);

            Wire(rounds, c.CycleRounds, o);
            r.rounds = rounds;
            r.roundsLabel = roundsLabel;
        }

        // Shown instead of the cards when the catalog has no stage of this kind (switched off here, so it isn't in the way).
        TextMeshProUGUI empty = StageUi.Label("Empty Message", card.transform,
            "No stages found.\nOpen the Puzzle scene, add Stage Definition objects, and save it\n(or use Tools > Stages > Rebuild Stage Catalog From Open Scene).",
            28f, StageUi.TextDim, TextAlignmentOptions.Center);
        empty.overflowMode = TextOverflowModes.Overflow;
        StageUi.Center(empty.rectTransform, new Vector2(1200f, 200f), new Vector2(0f, 10f));
        empty.gameObject.SetActive(false);
        r.empty = empty.gameObject;

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
        r.content = content;

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

        r.template = BuildStageCard(content, accent);
        return r;
    }

    /// <summary>The template card: a rounded button with a picture area (and a big number when there is no picture), a number pill and the name.</summary>
    private static StageCardView BuildStageCard(Transform parent, Color accent)
    {
        Image bg;
        Button button = StageUi.MakeButton("Stage Card (template)", parent, StageUi.Card, StageUi.CardHot, out bg);

        Color noPicture = new Color(0.06f, 0.08f, 0.13f, 1f);
        Image thumb = StageUi.Img("Thumbnail", bg.transform, noPicture);
        Fill(thumb.rectTransform, new Vector2(0f, 0.22f), new Vector2(1f, 1f), new Vector2(12f, 0f), new Vector2(-12f, -12f));

        TextMeshProUGUI big = StageUi.Label("Number", thumb.transform, "01", 110f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Stretch(big.rectTransform);

        Image pill = StageUi.Img("Pill", bg.transform, accent, StageUi.Rounded, true);
        pill.rectTransform.anchorMin = new Vector2(0f, 1f);
        pill.rectTransform.anchorMax = new Vector2(0f, 1f);
        pill.rectTransform.pivot = new Vector2(0f, 1f);
        pill.rectTransform.sizeDelta = new Vector2(64f, 40f);
        pill.rectTransform.anchoredPosition = new Vector2(22f, -22f);
        TextMeshProUGUI pillText = StageUi.Label("Text", pill.transform, "01", 26f, new Color(0.05f, 0.06f, 0.1f, 1f),
                                                 TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Stretch(pillText.rectTransform);

        TextMeshProUGUI nameText = StageUi.Label("Name", bg.transform, "Stage Name", 34f, StageUi.Text, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        Fill(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.22f), new Vector2(20f, 4f), new Vector2(-20f, 0f));

        StageCardView view = bg.gameObject.AddComponent<StageCardView>();
        view.button = button;
        view.thumbnail = thumb;
        view.thumbnailNumber = big;
        view.pillNumber = pillText;
        view.nameLabel = nameText;
        view.noPictureColor = noPicture;
        return view;
    }

    // ------------------------------------------------------------ shared pieces

    private static void BuildBackdrop(RectTransform root, Options o)
    {
        Image dim = StageUi.Img("Backdrop", root, new Color(0.02f, 0.03f, 0.07f, 0.55f));
        StageUi.Stretch(dim.rectTransform);

        if (!o.sideGlow) return;

        Image left;
        Image right;
        if (o.glowLeft != null && o.glowRight != null)
        {
            // Baked: real sprite assets, tinted here (white, fading in alpha).
            left = StageUi.Img("Glow Left", root, new Color(StageUi.Blue.r, StageUi.Blue.g, StageUi.Blue.b, 0.38f), o.glowLeft);
            right = StageUi.Img("Glow Right", root, new Color(StageUi.Orange.r, StageUi.Orange.g, StageUi.Orange.b, 0.38f), o.glowRight);
        }
        else
        {
            Color b0 = new Color(StageUi.Blue.r, StageUi.Blue.g, StageUi.Blue.b, 0.38f);
            Color b1 = new Color(StageUi.Blue.r, StageUi.Blue.g, StageUi.Blue.b, 0f);
            Color o0 = new Color(StageUi.Orange.r, StageUi.Orange.g, StageUi.Orange.b, 0f);
            Color o1 = new Color(StageUi.Orange.r, StageUi.Orange.g, StageUi.Orange.b, 0.38f);
            left = StageUi.Img("Glow Left", root, Color.white, StageUi.HorizontalGradient(b0, b1));
            right = StageUi.Img("Glow Right", root, Color.white, StageUi.HorizontalGradient(o0, o1));
        }

        Fill(left.rectTransform, new Vector2(0f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        Fill(right.rectTransform, new Vector2(0.5f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
    }

    private static void BuildHintAndToast(RectTransform root, out TMP_Text toast)
    {
        TextMeshProUGUI hint = StageUi.Label("Hint", root, HintText, 24f, StageUi.TextDim, TextAlignmentOptions.Center);
        StageUi.Bottom(hint.rectTransform, new Vector2(1400f, 40f), 18f);

        TextMeshProUGUI t = StageUi.Label("Toast", root, "", 28f, StageUi.Red, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Bottom(t.rectTransform, new Vector2(1600f, 50f), 70f);
        t.gameObject.SetActive(false);
        toast = t;
    }

    private static Button BackButton(Transform parent, UnityAction onClick, Options o)
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

        Wire(button, onClick, o);
        return button;
    }

    private static RectTransform NewPage(Transform root, string name, bool withCanvasGroup)
    {
        RectTransform rect = StageUi.Rect(name, root);
        StageUi.Stretch(rect);
        if (withCanvasGroup) rect.gameObject.AddComponent<CanvasGroup>();
        return rect;
    }

    /// <summary>
    /// Baking: the click is saved in the scene (visible and editable in the button's OnClick list). Runtime fallback: added in code.
    /// Either way the button ends up with exactly ONE listener for this action.
    /// </summary>
    private static void Wire(Button button, UnityAction action, Options o)
    {
#if UNITY_EDITOR
        if (o.bake)
        {
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, action);
            return;
        }
#endif
        button.onClick.AddListener(action);
    }

    private static void Fill(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
