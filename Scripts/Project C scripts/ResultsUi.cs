using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The two screens of the rank system, built in code (like the PVP result screen) so there is nothing to add to a scene:
///   ROOM PANEL   the drop-down that slides in from the top when every player is on the door: time, gems and score count up, the rank
///                letter stamps in, then the previous best and the buttons appear.
///   WIN SCREEN   after the last room: Player 1's total score, Player 2's total score, the overall rank and each room's rank.
/// LevelResults decides WHEN they show and what the numbers are; this file only draws them.
/// </summary>
public static class ResultsUi
{
    // ------------------------------------------------------------ what the screens are given

    public sealed class Choice
    {
        public string text;
        public Color accent;
        public Action onPick;
    }

    public sealed class RoomView
    {
        public string title = "";
        public string subtitle = "";

        public float seconds;
        public int timeTier;
        public float fastSeconds;

        public int collected;
        public int total;

        public int team;
        public int p1;
        public int p2;
        public bool showP1;
        public bool showP2;
        public int scoreTier;
        public int highScore;

        public int rank;

        public bool hadRecord;
        public int previousBestRank = RankCalc.NoRank;
        public int previousBestScore;
        public float previousBestTime = -1f;
        public bool newBestRank;
        public bool newBestScore;
        public bool newBestTime;

        /// <summary>The ranks of the tries BEFORE this one, newest first.</summary>
        public List<int> priorRanks = new List<int>();

        public List<Choice> choices = new List<Choice>();
        public int defaultChoice;
    }

    public sealed class WinView
    {
        public string title = "VICTORY";
        public string subtitle = "";

        public int p1;
        public int p2;
        public bool showP1;
        public bool showP2;
        public int team;
        public float seconds;
        public int collected;
        public int total;

        public int rank;
        public int[] roomRanks = new int[0];
        public int missing;
        public string missingText = "";
        public bool capped;
        public string capText = "";

        public bool hadRecord;
        public int previousBestRank = RankCalc.NoRank;
        public int previousBestScore;
        public bool newBestRank;
        public bool newBestScore;

        public List<Choice> choices = new List<Choice>();
        public int defaultChoice;
    }

    // ------------------------------------------------------------ what a built screen is

    internal sealed class Row
    {
        public CanvasGroup group;
        public TextMeshProUGUI value;
        public TextMeshProUGUI note;
        public float target;
        public Func<float, string> format;   // null = the value is plain text, nothing counts up
    }

    public sealed class Panel
    {
        public Canvas canvas;
        public CanvasGroup dim;
        public RectTransform card;
        public CanvasGroup cardGroup;
        public Vector2 hiddenOffset;   // where the card waits, relative to its final position
        public RectTransform stamp;
        public CanvasGroup stampGroup;
        public CanvasGroup extras;     // previous best, recent tries, hint
        public CanvasGroup buttonsGroup;
        public readonly List<Button> buttons = new List<Button>();
        public int defaultButton;
        internal readonly List<Row> rows = new List<Row>();
    }

    private sealed class Skip
    {
        public bool Requested;
    }

    private static readonly Color Gold = new Color(1.00f, 0.84f, 0.28f, 1f);

    // ------------------------------------------------------------ the room panel (drop-down)

    public static Panel BuildRoom(RoomView v)
    {
        Color rankColor = RankCalc.ColorOf(v.rank);

        Panel p = new Panel();
        p.canvas = StageUi.CreateCanvas("Level Results", 1500);
        StageUi.EnsureEventSystem();
        MakeDim(p, 0.45f);

        const float width = 1180f;
        const float height = 800f;
        Image card = StageUi.Img("Card", p.canvas.transform, StageUi.Panel, StageUi.Rounded, true);
        card.raycastTarget = true;
        p.card = card.rectTransform;
        StageUi.Top(p.card, new Vector2(width, height), 24f);
        p.cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        p.cardGroup.alpha = 0f;
        p.hiddenOffset = new Vector2(0f, height + 80f);   // fully above the screen: it DROPS DOWN into place
        p.card.anchoredPosition += p.hiddenOffset;

        Image bar = StageUi.Img("Accent", card.transform, rankColor);
        StageUi.Top(bar.rectTransform, new Vector2(width - 90f, 8f), 0f);

        TextMeshProUGUI title = StageUi.Label("Title", card.transform, v.title, 54f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        title.characterSpacing = 6f;
        StageUi.Top(title.rectTransform, new Vector2(width - 80f, 74f), 24f);

        if (!string.IsNullOrEmpty(v.subtitle))
        {
            TextMeshProUGUI sub = StageUi.Label("Subtitle", card.transform, v.subtitle, 26f, StageUi.TextDim, TextAlignmentOptions.Center);
            StageUi.Top(sub.rectTransform, new Vector2(width - 80f, 40f), 98f);
        }

        // ---- the three rows
        const float rowX = -215f;
        const float rowW = 700f;
        const float rowH = 112f;

        // TIME
        Row time = MakeRow(p, card.transform, "TIME", rowX, 170f, rowW, rowH, StageUi.Blue);
        time.target = v.seconds;
        time.format = RankCalc.FormatTime;
        time.value.text = RankCalc.FormatTime(0f);
        SetNote(time, RankCalc.TimeTierName(v.timeTier), TierColor(v.timeTier), "FAST = UNDER " + RankCalc.FormatTime(v.fastSeconds));

        // GEMS
        Row gems = MakeRow(p, card.transform, "COLLECTABLES", rowX, 300f, rowW, rowH, StageUi.Violet);
        gems.target = v.collected;
        int totalGems = v.total;
        gems.format = x => Mathf.RoundToInt(x) + " / " + totalGems;
        gems.value.text = "0 / " + totalGems;
        bool allGems = v.total <= 0 || v.collected >= v.total;
        if (v.total <= 0) SetNote(gems, "NO GEMS HERE", StageUi.TextDim, "");
        else if (allGems) SetNote(gems, "ALL OF THEM", StageUi.Green, "NEEDED FOR P");
        else SetNote(gems, "MISSED " + (v.total - v.collected), StageUi.Orange, "NEEDED FOR P");

        // SCORE
        string who = "";
        if (v.showP1) who += "   <color=#" + ColorUtility.ToHtmlStringRGB(StageUi.Blue) + ">P1 " + RankCalc.FormatScore(v.p1) + "</color>";
        if (v.showP2) who += "   <color=#" + ColorUtility.ToHtmlStringRGB(StageUi.Orange) + ">P2 " + RankCalc.FormatScore(v.p2) + "</color>";
        Row score = MakeRow(p, card.transform, "SCORE" + who, rowX, 430f, rowW, rowH, StageUi.Orange);
        score.target = v.team;
        score.format = x => RankCalc.FormatScore(Mathf.RoundToInt(x));
        score.value.text = RankCalc.FormatScore(0);
        SetNote(score, RankCalc.ScoreTierName(v.scoreTier), TierColor(v.scoreTier), "HIGH = " + RankCalc.FormatScore(v.highScore));

        // ---- the rank stamp
        Image stampCard = StageUi.Img("Rank Card", card.transform, StageUi.Card, StageUi.Rounded, true);
        StageUi.Top(stampCard.rectTransform, new Vector2(380f, 372f), 170f);
        stampCard.rectTransform.anchoredPosition = new Vector2(365f, -170f);

        TextMeshProUGUI caption = StageUi.Label("Caption", stampCard.transform, "RANK", 24f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        caption.characterSpacing = 8f;
        StageUi.Top(caption.rectTransform, new Vector2(340f, 36f), 16f);

        BuildStamp(p, stampCard.transform, v.rank, 230f, 340f, 52f, 250f);

        // ---- previous best + prior tries
        p.extras = MakeGroup(card.transform, "Extras");
        StageUi.Stretch((RectTransform)p.extras.transform);

        TextMeshProUGUI best = StageUi.Label("Best", p.extras.transform, BestLine(v), 28f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Top(best.rectTransform, new Vector2(width - 80f, 46f), 566f);

        if (v.priorRanks.Count > 0)
        {
            string tries = "PRIOR TRIES";
            int shown = Mathf.Min(v.priorRanks.Count, 7);
            for (int i = 0; i < shown; i++) tries += "   " + RankCalc.Rich(v.priorRanks[i]);
            TextMeshProUGUI recent = StageUi.Label("Prior Tries", p.extras.transform, tries, 26f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
            StageUi.Top(recent.rectTransform, new Vector2(width - 80f, 40f), 616f);
        }

        TextMeshProUGUI hint = StageUi.Label("Hint", p.extras.transform, "ENTER / A  to pick", 20f, StageUi.TextDim, TextAlignmentOptions.Center);
        StageUi.Top(hint.rectTransform, new Vector2(width - 80f, 30f), 764f);

        BuildButtons(p, card.transform, v.choices, v.defaultChoice, 676f, 84f, width);
        return p;
    }

    private static string BestLine(RoomView v)
    {
        if (!v.hadRecord) return "<color=#" + ColorUtility.ToHtmlStringRGB(Gold) + ">FIRST CLEAR</color>  -  THIS IS NOW YOUR BEST";

        string line = "PREVIOUS BEST   " + RankCalc.Rich(v.previousBestRank) + "   -   " + RankCalc.FormatScore(v.previousBestScore) +
                      "   -   " + RankCalc.FormatTime(v.previousBestTime);
        string gold = ColorUtility.ToHtmlStringRGB(Gold);
        if (v.newBestRank) line += "   <color=#" + gold + ">NEW BEST RANK!</color>";
        if (v.newBestScore) line += "   <color=#" + gold + ">NEW BEST SCORE!</color>";
        if (v.newBestTime) line += "   <color=#" + gold + ">NEW BEST TIME!</color>";
        return line;
    }

    // ------------------------------------------------------------ the win screen

    public static Panel BuildWin(WinView v)
    {
        Panel p = new Panel();
        p.canvas = StageUi.CreateCanvas("Win Screen", 1600);
        StageUi.EnsureEventSystem();
        MakeDim(p, 0.78f);

        const float width = 1500f;
        const float height = 930f;
        Image card = StageUi.Img("Card", p.canvas.transform, StageUi.Panel, StageUi.Rounded, true);
        card.raycastTarget = true;
        p.card = card.rectTransform;
        StageUi.Center(p.card, new Vector2(width, height), Vector2.zero);
        p.cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        p.cardGroup.alpha = 0f;
        p.hiddenOffset = new Vector2(0f, -90f);
        p.card.anchoredPosition += p.hiddenOffset;

        Color rankColor = RankCalc.ColorOf(v.rank);
        Image bar = StageUi.Img("Accent", card.transform, rankColor);
        StageUi.Top(bar.rectTransform, new Vector2(width - 100f, 8f), 0f);

        TextMeshProUGUI title = StageUi.Label("Title", card.transform, v.title, 96f, Gold, TextAlignmentOptions.Center, FontStyles.Bold);
        title.characterSpacing = 10f;
        StageUi.Top(title.rectTransform, new Vector2(width - 100f, 120f), 28f);

        TextMeshProUGUI sub = StageUi.Label("Subtitle", card.transform, v.subtitle, 30f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        sub.characterSpacing = 4f;
        StageUi.Top(sub.rectTransform, new Vector2(width - 100f, 44f), 146f);

        // ---- Player 1 | overall rank | Player 2
        MakePlayerCard(p, card.transform, "PLAYER 1", StageUi.Blue, -480f, 214f, v.p1, v.showP1);
        MakePlayerCard(p, card.transform, "PLAYER 2", StageUi.Orange, 480f, 214f, v.p2, v.showP2);

        Image rankCard = StageUi.Img("Rank Card", card.transform, StageUi.Card, StageUi.Rounded, true);
        StageUi.Top(rankCard.rectTransform, new Vector2(420f, 310f), 214f);
        TextMeshProUGUI rankCaption = StageUi.Label("Caption", rankCard.transform, "OVERALL RANK", 24f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        rankCaption.characterSpacing = 6f;
        StageUi.Top(rankCaption.rectTransform, new Vector2(380f, 36f), 16f);
        BuildStamp(p, rankCard.transform, v.rank, 190f, 380f, 56f, 190f);

        // ---- the team line, then one tile per room
        p.extras = MakeGroup(card.transform, "Extras");
        StageUi.Stretch((RectTransform)p.extras.transform);

        string gemText = v.total > 0 ? v.collected + " / " + v.total : "-";
        string team = "TEAM  <color=#" + ColorUtility.ToHtmlStringRGB(StageUi.Text) + ">" + RankCalc.FormatScore(v.team) + "</color>      TIME  " +
                      RankCalc.FormatTime(v.seconds) + "      COLLECTABLES  " + gemText;
        TextMeshProUGUI teamLine = StageUi.Label("Team", p.extras.transform, team, 30f, StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Top(teamLine.rectTransform, new Vector2(width - 100f, 46f), 556f);

        BuildRoomTiles(p.extras.transform, v.roomRanks, 620f, width);

        float noteY = 750f;
        if (v.capped || v.missing > 0)
        {
            TextMeshProUGUI miss = StageUi.Label("Missed", p.extras.transform, v.missingText + (v.capped ? "  -  " + v.capText : ""), 26f,
                                                 StageUi.Red, TextAlignmentOptions.Center, FontStyles.Bold);
            StageUi.Top(miss.rectTransform, new Vector2(width - 100f, 40f), noteY);
            noteY += 40f;
        }

        TextMeshProUGUI best = StageUi.Label("Best", p.extras.transform, WinBestLine(v), 26f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Top(best.rectTransform, new Vector2(width - 100f, 40f), noteY);

        BuildButtons(p, card.transform, v.choices, v.defaultChoice, 826f, 84f, width);
        return p;
    }

    private static string WinBestLine(WinView v)
    {
        string gold = ColorUtility.ToHtmlStringRGB(Gold);
        if (!v.hadRecord) return "<color=#" + gold + ">FIRST FINISH</color>  -  THIS IS NOW YOUR BEST";

        string line = "PREVIOUS BEST   " + RankCalc.Rich(v.previousBestRank) + "   -   " + RankCalc.FormatScore(v.previousBestScore);
        if (v.newBestRank) line += "   <color=#" + gold + ">NEW BEST RANK!</color>";
        if (v.newBestScore) line += "   <color=#" + gold + ">NEW BEST SCORE!</color>";
        return line;
    }

    private static void MakePlayerCard(Panel p, Transform parent, string name, Color accent, float x, float y, int score, bool shown)
    {
        Image card = StageUi.Img(name + " Card", parent, StageUi.Card, StageUi.Rounded, true);
        StageUi.Top(card.rectTransform, new Vector2(440f, 310f), y);
        card.rectTransform.anchoredPosition = new Vector2(x, -y);

        CanvasGroup group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        Image stripe = StageUi.Img("Accent", card.transform, accent, StageUi.Rounded, true);
        StageUi.Top(stripe.rectTransform, new Vector2(380f, 8f), 14f);

        TextMeshProUGUI head = StageUi.Label("Name", card.transform, name, 36f, accent, TextAlignmentOptions.Center, FontStyles.Bold);
        head.characterSpacing = 6f;
        StageUi.Top(head.rectTransform, new Vector2(400f, 50f), 34f);

        TextMeshProUGUI value = StageUi.Label("Score", card.transform, shown ? RankCalc.FormatScore(0) : "-", 92f,
                                              shown ? StageUi.Text : StageUi.TextDim, TextAlignmentOptions.Center, FontStyles.Bold);
        value.overflowMode = TextOverflowModes.Overflow;
        StageUi.Top(value.rectTransform, new Vector2(420f, 130f), 100f);

        TextMeshProUGUI caption = StageUi.Label("Caption", card.transform, shown ? "OVERALL SCORE" : "NOT PLAYING", 22f, StageUi.TextDim,
                                                TextAlignmentOptions.Center, FontStyles.Bold);
        caption.characterSpacing = 4f;
        StageUi.Top(caption.rectTransform, new Vector2(400f, 34f), 244f);

        Row row = new Row();
        row.group = group;
        row.value = value;
        row.target = score;
        row.format = shown ? (Func<float, string>)(x => RankCalc.FormatScore(Mathf.RoundToInt(x))) : null;
        p.rows.Add(row);
    }

    private static void BuildRoomTiles(Transform parent, int[] ranks, float y, float width)
    {
        int n = ranks == null ? 0 : ranks.Length;
        if (n <= 0) return;

        const float gap = 14f;
        float tileW = Mathf.Min(150f, (width - 160f - gap * (n - 1)) / n);
        const float tileH = 112f;
        float total = tileW * n + gap * (n - 1);
        float left = -total * 0.5f + tileW * 0.5f;

        for (int i = 0; i < n; i++)
        {
            bool missing = ranks[i] < 0;
            Color bg = missing ? new Color(0.30f, 0.10f, 0.12f, 1f) : StageUi.Card;

            Image tile = StageUi.Img("Room " + (i + 1), parent, bg, StageUi.Rounded, true);
            StageUi.Top(tile.rectTransform, new Vector2(tileW, tileH), y);
            tile.rectTransform.anchoredPosition = new Vector2(left + i * (tileW + gap), -y);

            TextMeshProUGUI label = StageUi.Label("Label", tile.transform, "ROOM " + (i + 1), 20f, missing ? StageUi.Red : StageUi.TextDim,
                                                  TextAlignmentOptions.Center, FontStyles.Bold);
            StageUi.Top(label.rectTransform, new Vector2(tileW, 30f), 8f);

            TextMeshProUGUI letter = StageUi.Label("Rank", tile.transform, missing ? "MISSED" : RankCalc.Letter(ranks[i]), missing ? 26f : 60f,
                                                   missing ? StageUi.Red : RankCalc.ColorOf(ranks[i]), TextAlignmentOptions.Center, FontStyles.Bold);
            letter.overflowMode = TextOverflowModes.Overflow;
            StageUi.Top(letter.rectTransform, new Vector2(tileW, 72f), 36f);
        }
    }

    // ------------------------------------------------------------ shared pieces

    private static void MakeDim(Panel p, float alpha)
    {
        Image dim = StageUi.Img("Dim", p.canvas.transform, new Color(0f, 0f, 0f, alpha));
        dim.raycastTarget = true;   // nothing behind the screen can be clicked
        StageUi.Stretch(dim.rectTransform);
        p.dim = dim.gameObject.AddComponent<CanvasGroup>();
        p.dim.alpha = 0f;
    }

    private static CanvasGroup MakeGroup(Transform parent, string name)
    {
        RectTransform rt = StageUi.Rect(name, parent);
        CanvasGroup g = rt.gameObject.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        g.interactable = false;
        g.blocksRaycasts = false;
        return g;
    }

    // The big rank letter. boxW / boxH are its text box, y is the distance from the top of its parent.
    private static void BuildStamp(Panel p, Transform parent, int rank, float letterSize, float boxW, float y, float boxH)
    {
        Color color = RankCalc.ColorOf(rank);

        RectTransform holder = StageUi.Rect("Stamp", parent);
        StageUi.Top(holder, new Vector2(boxW, boxH + 56f), y);
        CanvasGroup group = holder.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        TextMeshProUGUI letter = StageUi.Label("Letter", holder, RankCalc.Letter(rank), letterSize, color, TextAlignmentOptions.Center, FontStyles.Bold);
        letter.overflowMode = TextOverflowModes.Overflow;
        StageUi.Top(letter.rectTransform, new Vector2(boxW, boxH), 0f);

        TextMeshProUGUI word = StageUi.Label("Word", holder, RankCalc.Word(rank), 34f, color, TextAlignmentOptions.Center, FontStyles.Bold);
        word.characterSpacing = 6f;
        StageUi.Top(word.rectTransform, new Vector2(boxW, 48f), boxH + 4f);

        p.stamp = holder;
        p.stampGroup = group;
    }

    private static Row MakeRow(Panel p, Transform parent, string label, float x, float y, float width, float height, Color accent)
    {
        Image bg = StageUi.Img(label + " Row", parent, StageUi.Card, StageUi.Rounded, true);
        RectTransform rt = bg.rectTransform;
        StageUi.Top(rt, new Vector2(width, height), y);
        rt.anchoredPosition = new Vector2(x, -y);

        CanvasGroup group = bg.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        Image stripe = StageUi.Img("Accent", bg.transform, accent, StageUi.Rounded, true);
        stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
        stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
        stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
        stripe.rectTransform.sizeDelta = new Vector2(10f, -28f);
        stripe.rectTransform.anchoredPosition = new Vector2(14f, 0f);

        TextMeshProUGUI lab = StageUi.Label("Label", bg.transform, label, 22f, StageUi.TextDim, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        lab.characterSpacing = 4f;
        RectTransform lr = lab.rectTransform;
        lr.anchorMin = Vector2.zero;
        lr.anchorMax = Vector2.one;
        lr.offsetMin = new Vector2(40f, height - 42f);   // the top strip of the row
        lr.offsetMax = new Vector2(-20f, -8f);

        TextMeshProUGUI value = StageUi.Label("Value", bg.transform, "", 60f, StageUi.Text, TextAlignmentOptions.BottomLeft, FontStyles.Bold);
        value.overflowMode = TextOverflowModes.Overflow;
        RectTransform vr = value.rectTransform;
        vr.anchorMin = Vector2.zero;
        vr.anchorMax = Vector2.one;
        vr.offsetMin = new Vector2(40f, 4f);
        vr.offsetMax = new Vector2(-270f, -34f);

        TextMeshProUGUI note = StageUi.Label("Note", bg.transform, "", 30f, StageUi.Text, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        note.overflowMode = TextOverflowModes.Overflow;
        RectTransform nr = note.rectTransform;
        nr.anchorMin = new Vector2(1f, 0f);
        nr.anchorMax = new Vector2(1f, 1f);
        nr.pivot = new Vector2(1f, 0.5f);
        nr.sizeDelta = new Vector2(300f, -10f);
        nr.anchoredPosition = new Vector2(-24f, 0f);

        Row row = new Row();
        row.group = group;
        row.value = value;
        row.format = null;
        row.target = 0f;
        row.note = note;
        p.rows.Add(row);
        return row;
    }

    private static void SetNote(Row row, string text, Color color, string small)
    {
        TextMeshProUGUI note = row.note;
        if (note == null) return;

        string t = "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
        if (!string.IsNullOrEmpty(small)) t += "\n<size=62%><color=#" + ColorUtility.ToHtmlStringRGB(StageUi.TextDim) + ">" + small + "</color></size>";
        note.text = t;
    }

    private static void BuildButtons(Panel p, Transform parent, List<Choice> choices, int defaultChoice, float y, float height, float cardWidth)
    {
        RectTransform holder = StageUi.Rect("Buttons", parent);
        StageUi.Stretch(holder);
        p.buttonsGroup = holder.gameObject.AddComponent<CanvasGroup>();
        p.buttonsGroup.alpha = 0f;
        p.buttonsGroup.interactable = false;

        int n = choices == null ? 0 : choices.Count;
        if (n == 0) return;

        float buttonW = n == 1 ? 560f : Mathf.Min(380f, (cardWidth - 120f - 24f * (n - 1)) / n);
        float step = buttonW + 24f;
        float first = -step * (n - 1) * 0.5f;

        for (int i = 0; i < n; i++)
        {
            Choice choice = choices[i];

            Image bg;
            Button b = StageUi.MakeButton(choice.text, holder, StageUi.Card, StageUi.CardHot, out bg);   // normal, then the colour when selected

            StageUi.Top(bg.rectTransform, new Vector2(buttonW, height), y);
            bg.rectTransform.anchoredPosition = new Vector2(first + i * step, -y);

            Image accent = StageUi.Img("Accent", bg.transform, choice.accent, StageUi.Rounded, true);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.sizeDelta = new Vector2(12f, -24f);
            accent.rectTransform.anchoredPosition = new Vector2(14f, 0f);

            TextMeshProUGUI label = StageUi.Label("Label", bg.transform, choice.text, 34f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            StageUi.Stretch(label.rectTransform);
            label.characterSpacing = 4f;

            Action pick = choice.onPick;
            b.onClick.AddListener(() => { if (pick != null) pick(); });
            p.buttons.Add(b);
        }

        p.defaultButton = Mathf.Clamp(defaultChoice, 0, n - 1);
    }

    private static Color TierColor(int tier)
    {
        switch (tier)
        {
            case 3: return StageUi.Green;
            case 2: return StageUi.Blue;
            case 1: return StageUi.Orange;
            default: return StageUi.Red;
        }
    }

    // ------------------------------------------------------------ animation (all on unscaled time: the game may be paused or frozen)

    /// <summary>Plays the screen in: the card arrives, the numbers count up, the rank stamps, the buttons appear. Enter skips ahead.</summary>
    public static IEnumerator Play(Panel p, float buttonGrace)
    {
        Skip skip = new Skip();

        yield return Tween(0.25f, skip, k => { p.dim.alpha = k; });

        Vector2 final = p.card.anchoredPosition - p.hiddenOffset;
        Vector2 start = p.card.anchoredPosition;
        yield return Tween(0.6f, skip, k =>
        {
            p.cardGroup.alpha = Mathf.Clamp01(k * 4f);
            p.card.anchoredPosition = Vector2.LerpUnclamped(start, final, EaseOutBack(k));
        });
        yield return Hold(0.15f, skip);

        for (int i = 0; i < p.rows.Count; i++)
        {
            Row row = p.rows[i];
            yield return Tween(0.2f, skip, k => { row.group.alpha = k; });
            if (row.format != null)
            {
                yield return Tween(0.6f, skip, k => { row.value.text = row.format(Mathf.Lerp(0f, row.target, EaseOutCubic(k))); });
            }
            yield return Hold(0.12f, skip);
        }

        if (p.stamp != null)
        {
            yield return Hold(0.1f, skip);
            yield return Tween(0.42f, skip, k =>
            {
                p.stampGroup.alpha = Mathf.Clamp01(k * 2.5f);
                float s = Mathf.LerpUnclamped(3f, 1f, EaseOutBack(k));
                p.stamp.localScale = new Vector3(s, s, 1f);
            });
            yield return Hold(0.25f, skip);
        }

        yield return Tween(0.3f, skip, k => { p.extras.alpha = k; });
        yield return Tween(0.25f, skip, k => { p.buttonsGroup.alpha = k; });

        // A player still hammering a key at the door can't press a button by accident.
        float wait = 0f;
        while (wait < buttonGrace)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        p.buttonsGroup.interactable = true;
        p.buttonsGroup.blocksRaycasts = true;

        yield return null;
        if (p.buttons.Count > 0 && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(p.buttons[p.defaultButton].gameObject);
        }
    }

    /// <summary>Slides the drop-down back up and removes it.</summary>
    public static IEnumerator Retract(Panel p)
    {
        if (p == null || p.canvas == null) yield break;

        p.buttonsGroup.interactable = false;
        Vector2 start = p.card.anchoredPosition;
        Vector2 end = start + p.hiddenOffset;
        Skip skip = new Skip();
        yield return Tween(0.35f, skip, k =>
        {
            if (p.card == null) return;
            p.card.anchoredPosition = Vector2.Lerp(start, end, k * k);
            p.dim.alpha = 1f - k;
        }, false);

        if (p.canvas != null) UnityEngine.Object.Destroy(p.canvas.gameObject);
    }

    private static IEnumerator Tween(float duration, Skip skip, Action<float> apply, bool skippable = true)
    {
        float t = 0f;
        while (t < duration && !(skippable && skip.Requested))
        {
            if (skippable && EnterPressed()) skip.Requested = true;
            t += Time.unscaledDeltaTime;
            apply(Mathf.Clamp01(t / duration));
            yield return null;
        }
        apply(1f);
    }

    private static IEnumerator Hold(float seconds, Skip skip)
    {
        float t = 0f;
        while (t < seconds && !skip.Requested)
        {
            if (EnterPressed()) skip.Requested = true;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static float EaseOutCubic(float k)
    {
        float inv = 1f - k;
        return 1f - inv * inv * inv;
    }

    private static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    // Enter only: it skips the count-up. (Space / the pad's A press the selected button through the EventSystem, and Space is the jump key,
    // so it must not also skip things.)
    private static bool EnterPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) return true;
#endif
        return false;
    }
}
