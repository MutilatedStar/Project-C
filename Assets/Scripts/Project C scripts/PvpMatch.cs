using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using Ilumisoft.HealthSystem;

/// <summary>
/// The PVP referee. Put it in the Puzzle scene (next to StageLauncher). It does nothing at all while PvpMode.Enabled is off,
/// so the same scene works for the puzzle stages and the PVP arenas.
///
/// While PVP is on it:
///   - keeps the co-op bonuses off (shared damage / score / single player mode) and Player 2 switched on,
///   - stops the game-over screen from coming up when one player falls (CoopHealth.TeamWipeBlockers),
///   - watches both players: when one is at 0 health the OTHER wins the round,
///   - shows the score at the top of the screen and a round banner (Round 1 opens with the arena name, then the round number),
///   - starts the next round by reloading the stage (same stage, scores kept), or ends the match at "Rounds To Win"
///     with Rematch / Pick Arena / Main Menu buttons.
///
/// Swords hurt the other player through SwordController (PvpMode.CanHurt) and nobody can revive a fallen player through
/// CoopDowned while PVP is on. Both are small additions to those scripts.
/// The same hits also shove the victim (Knockback Scale below) and bring the sword's specials: ice slows walking, lightning slows
/// swinging, fire trails burn. All of that lives in SwordController, SwordFireTrail and TopDownCharacterController.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Pvp Match")]
public class PvpMatch : MonoBehaviour
{
    [Header("Match")]
    [Tooltip("Damage multiplier for sword hits on a PLAYER (foes are not affected). 1 = the sword's normal damage.")]
    [SerializeField, Min(0.05f)] private float damageScale = 1f;
    [Tooltip("Knockback multiplier for sword hits on a PLAYER (foes are not affected). 1 = the sword's own Knockback number, 0 = no knockback.")]
    [SerializeField, Min(0f)] private float knockbackScale = 1f;
    [Tooltip("Seconds the round result stays on screen before the next round starts.")]
    [SerializeField, Min(0.5f)] private float roundEndDelay = 2.5f;
    [Tooltip("Seconds after the scene opens before a player at 0 health counts as knocked out (lets health set itself up).")]
    [SerializeField, Min(0f)] private float startGrace = 1f;
    [Tooltip("Seconds the arena name holds on screen at the start of Round 1, before it fades and the round number shows.")]
    [SerializeField, Min(0.3f)] private float arenaBannerHold = 1.4f;

    [Header("Where the buttons go")]
    [Tooltip("Scene name of the main menu. 'Mainmenu', 'MainMenu' and 'Main Menu' are tried too (capitals and spaces don't matter).")]
    [SerializeField] private string mainMenuScene = "MainMenu";
    [Tooltip("Scene name of the Level Select scene (PICK ARENA opens it on the PVP arenas). 'LevelSelect' and 'Level Select' are tried too.")]
    [SerializeField] private string levelSelectScene = "LevelSelect";

    private enum State { Fighting, RoundOver, MatchOver }

    /// <summary>Seconds a StageBanner takes to fade in and out on top of its hold time.</summary>
    private const float BannerFadeTime = 0.8f;

    private static PvpMatch current;

    /// <summary>
    /// True from the moment a round is decided (a player fell) until the scene is left: the round banner, and then the match result screen, are up
    /// or on their way. PauseController reads it so the pause menu can't open over them (two menus at once), and closes itself if it was already open.
    /// </summary>
    public static bool BlocksInput
    {
        get { return current != null && current.state != State.Fighting; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
    }

    private State state = State.Fighting;
    private bool blockerAdded;
    private bool seen1, seen2, introShown;
    private Canvas hud;
    private TextMeshProUGUI scoreLabel;

    private void Awake()
    {
        current = this;
    }

    private void Update()
    {
        if (!PvpMode.Enabled)
        {
            if (blockerAdded) RemoveBlocker();
            return;
        }

        if (!blockerAdded)
        {
            CoopHealth.TeamWipeBlockers++;
            blockerAdded = true;
        }

        PvpMode.DamageScale = damageScale;
        PvpMode.KnockbackScale = knockbackScale;
        PvpMode.ApplyRules();
        if (!LocalCoop.Enabled) PvpMode.RequireTwoPlayers();
        if (hud == null) BuildHud();

        if (state != State.Fighting) return;
        if (Time.timeSinceLevelLoad < startGrace) return;

        CoopHealth a = CoopHealth.Get(1);
        CoopHealth b = CoopHealth.Get(2);
        if (a == null || b == null) return;

        if (a.IsAlive) seen1 = true;
        if (b.IsAlive) seen2 = true;
        if (!seen1 || !seen2) return;

        if (!introShown)
        {
            introShown = true;
            StartCoroutine(IntroRoutine());
        }

        bool aDown = !a.IsAlive;
        bool bDown = !b.IsAlive;
        if (!aDown && !bDown) return;

        int winner = (aDown && bDown) ? 0 : (aDown ? 2 : 1);
        EndRound(winner);
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        if (blockerAdded) RemoveBlocker();
        if (hud != null) Destroy(hud.gameObject);
    }

    private void RemoveBlocker()
    {
        CoopHealth.TeamWipeBlockers = Mathf.Max(0, CoopHealth.TeamWipeBlockers - 1);
        blockerAdded = false;
    }

    // ------------------------------------------------------------ intro banner

    /// <summary>
    /// Round 1 opens with the arena name. Once that banner has faded, the round number banner comes up.
    /// Every later round skips the arena name and goes straight to its round number.
    /// </summary>
    private IEnumerator IntroRoutine()
    {
        if (PvpMode.RoundsPlayed == 0 && !string.IsNullOrEmpty(PvpMode.ArenaName))
        {
            StageBanner.Show(this, PvpMode.ArenaName, "PVP ARENA", StageUi.Violet, arenaBannerHold);

            // Wait out the hold time plus the banner's fade in/out (NextRoundRoutine budgets the same 0.8 s around its banner).
            float t = 0f;
            while (t < arenaBannerHold + BannerFadeTime) { t += Time.unscaledDeltaTime; yield return null; }

            // If a player already fell while the arena name was up, the round result banner owns the screen: no round number.
            if (state != State.Fighting) yield break;
        }

        StageBanner.Show(this, "Round " + (PvpMode.RoundsPlayed + 1), "FIRST TO " + PvpMode.RoundsToWin, StageUi.Orange, 1.1f);
    }

    // ------------------------------------------------------------ rounds

    private void EndRound(int winner)
    {
        LockPlayers();
        PvpMode.RoundsPlayed++;
        if (winner == 1) PvpMode.Player1Wins++;
        else if (winner == 2) PvpMode.Player2Wins++;
        RefreshScore();

        bool matchOver = winner != 0 &&
                         (PvpMode.Player1Wins >= PvpMode.RoundsToWin || PvpMode.Player2Wins >= PvpMode.RoundsToWin);

        if (matchOver)
        {
            state = State.MatchOver;
            StartCoroutine(MatchOverRoutine(winner));
        }
        else
        {
            state = State.RoundOver;
            StartCoroutine(NextRoundRoutine(winner));
        }
    }

    private IEnumerator NextRoundRoutine(int winner)
    {
        string title = winner == 0 ? "DRAW" : "PLAYER " + winner + " SCORES";
        Color c = winner == 2 ? StageUi.Orange : (winner == 1 ? StageUi.Blue : StageUi.Text);
        StageBanner.Show(this, title, PvpMode.Player1Wins + "  -  " + PvpMode.Player2Wins, c, roundEndDelay - 0.8f);

        float t = 0f;
        while (t < roundEndDelay) { t += Time.unscaledDeltaTime; yield return null; }

        StageUi.FadeAndLoad(this, SceneManager.GetActiveScene().name);
    }

    private IEnumerator MatchOverRoutine(int winner)
    {
        float t = 0f;
        while (t < 1.0f) { t += Time.unscaledDeltaTime; yield return null; }
        BuildEndScreen(winner);
    }

    private static void LockPlayers()
    {
        for (int n = 1; n <= 4; n++)
        {
            PlayerInputSource source = PlayerInputSource.Get(n);
            if (source != null) source.Locked = true;

            // Ice slow, lightning shock and any shove end with the round, so none of it carries into the next one.
            SwordController sword = SwordController.Get(n);
            if (sword != null) sword.ClearPvpEffects();
        }
    }

    // ------------------------------------------------------------ screens

    private void BuildHud()
    {
        hud = StageUi.CreateCanvas("PVP Score", 800);

        // Soft black plate behind "P1 score  FIRST TO N  P2 score". Created first so it sits behind the text; it is centred on the
        // label below (80 px tall at 18 px from the top), 12 px taller than the text on each side.
        Image plate = StageUi.Img("Score Backdrop", hud.transform, StageUi.SoftBlack, StageUi.Rounded, true);
        StageUi.Top(plate.rectTransform, new Vector2(620f, 92f), 12f);

        scoreLabel = StageUi.Label("Score", hud.transform, "", 54f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Top(scoreLabel.rectTransform, new Vector2(900f, 80f), 18f);
        RefreshScore();
    }

    private void RefreshScore()
    {
        if (scoreLabel == null) return;
        string blue = ColorUtility.ToHtmlStringRGB(StageUi.Blue);
        string orange = ColorUtility.ToHtmlStringRGB(StageUi.Orange);
        scoreLabel.text = "<color=#" + blue + ">" + PvpMode.Player1Wins + "</color>   <size=60%>FIRST TO " + PvpMode.RoundsToWin +
                          "</size>   <color=#" + orange + ">" + PvpMode.Player2Wins + "</color>";
    }

    private void BuildEndScreen(int winner)
    {
        Canvas canvas = StageUi.CreateCanvas("PVP Result", 1000);
        StageUi.EnsureEventSystem();

        Image dim = StageUi.Img("Dim", canvas.transform, new Color(0f, 0f, 0f, 0.7f));
        dim.raycastTarget = true;
        StageUi.Stretch(dim.rectTransform);

        Image card = StageUi.Img("Card", canvas.transform, StageUi.Panel, StageUi.Rounded, true);
        StageUi.Center(card.rectTransform, new Vector2(900f, 640f), Vector2.zero);

        Color accent = winner == 2 ? StageUi.Orange : StageUi.Blue;
        TextMeshProUGUI head = StageUi.Label("Winner", card.transform, "PLAYER " + winner + " WINS", 84f, accent,
                                             TextAlignmentOptions.Center, FontStyles.Bold);
        head.characterSpacing = 6f;
        StageUi.Top(head.rectTransform, new Vector2(860f, 110f), 50f);

        TextMeshProUGUI score = StageUi.Label("Final", card.transform, PvpMode.Player1Wins + "  -  " + PvpMode.Player2Wins,
                                              56f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Top(score.rectTransform, new Vector2(860f, 80f), 170f);

        Button rematch = MakeChoice(card.transform, "REMATCH", StageUi.Orange, 280f, () =>
        {
            PvpMode.ResetMatch();
            StageUi.FadeAndLoad(this, SceneManager.GetActiveScene().name);
        });
        MakeChoice(card.transform, "PICK ARENA", StageUi.Blue, 390f, () => LeaveToMenu(true));
        MakeChoice(card.transform, "MAIN MENU", StageUi.Violet, 500f, () => LeaveToMenu(false));

        StartCoroutine(SelectNextFrame(rematch));
    }

    private Button MakeChoice(Transform parent, string text, Color accent, float y, UnityEngine.Events.UnityAction onClick)
    {
        Image bg;
        Button b = StageUi.MakeButton(text, parent, StageUi.Card, StageUi.CardHot, out bg);
        StageUi.Top(bg.rectTransform, new Vector2(560f, 88f), y);

        Image bar = StageUi.Img("Accent", bg.transform, accent, StageUi.Rounded, true);
        bar.rectTransform.anchorMin = new Vector2(0f, 0f);
        bar.rectTransform.anchorMax = new Vector2(0f, 1f);
        bar.rectTransform.pivot = new Vector2(0f, 0.5f);
        bar.rectTransform.sizeDelta = new Vector2(12f, -24f);
        bar.rectTransform.anchoredPosition = new Vector2(14f, 0f);

        TextMeshProUGUI label = StageUi.Label("Label", bg.transform, text, 40f, StageUi.Text, TextAlignmentOptions.Center, FontStyles.Bold);
        StageUi.Stretch(label.rectTransform);
        label.characterSpacing = 4f;

        b.onClick.AddListener(onClick);
        return b;
    }

    private IEnumerator SelectNextFrame(Button button)
    {
        yield return null;
        if (UnityEngine.EventSystems.EventSystem.current != null && button != null)
        {
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
    }

    private void LeaveToMenu(bool toArenaList)
    {
        string scene = null;

        // PICK ARENA: straight to the Level Select scene, on its PVP arenas page.
        if (toArenaList)
        {
            scene = StageUi.ResolveScene(levelSelectScene, "LevelSelect", "Level Select");
            if (scene != null) StageSelection.RequestBrowse(StageMode.Pvp);
            else Debug.LogWarning("PvpMatch: no Level Select scene can be loaded (add it to Build Settings, and check 'Level Select Scene'), so the main menu opens instead.", this);
        }

        if (scene == null) scene = StageUi.ResolveScene(mainMenuScene, "MainMenu", "Mainmenu", "Main Menu");
        if (scene == null)
        {
            Debug.LogWarning("PvpMatch: no main menu scene can be loaded (add it to Build Settings, and check 'Main Menu Scene').", this);
            return;
        }

        StageUi.FadeAndLoad(this, scene);
    }
}
