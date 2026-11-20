using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The rank system's brain. NOTHING TO ADD TO A SCENE: FloorController starts it by itself.
///
///   1. While the players play a room it counts TIME (paused with the game, stopped during floor changes) and the gems (ScorePickup) taken.
///   2. When EVERY player is on the door (FloorController's door check fires) the team doesn't go on yet: the RESULTS DROP-DOWN slides in with the
///      time, the gems and the score, and the rank they earn (RankCalc / RankSettings). It is saved (LevelRecords) next to the previous best.
///   3. The button on the drop-down moves the team on. The score is set back to 0 for the next room (the Shop's money, ScoreController, is NOT touched).
///   4. After the LAST room (Rank Settings > Room Count; room 0 counts as room one) the WIN SCREEN shows Player 1's and Player 2's overall
///      scores and the overall rank. A room that was never cleared caps that rank (P, S and A become impossible).
///
/// TWO WAYS TO PLAY IT
///   Level Select   a stage picked in Level Select is one room: the drop-down offers RETRY / LEVEL SELECT / NEXT LEVEL. The rank goes on that stage's
///                  card. The win screen shows after the stage numbered Room Count (the sixth stage, by default) using your BEST result on each stage.
///   Dungeon        Easy / Normal / Hard has no stage: every floor is a room (room 1 = the first floor) and the drop-down's button goes down to the next
///                  one (or to the Shop). The win screen shows after floor Room Count, using THIS run's results.
///
/// SAVING   A room is saved when its results screen opens. A dungeon floor is saved under its own id AND under the Level Select card it is the
///          same room as (floor N = card N, Rank Settings > Dungeon Counts For Level Select), so playing through the dungeon fills in the cards.
///          Anything that moves the team on WITHOUT the door (FloorController.GoDown / OpenShop) saves the room too, through
///          RecordRoomIfUnsaved. Level Select also folds dungeon rooms cleared before into their cards when it opens (SyncCardsFromDungeon).
///
/// Other scripts can read:  LevelResults.IsActive,  LevelResults.BlocksInput (FloorController.BlocksInput already includes it).
/// A door of your own that should end the room the same way:  LevelResults.Complete(() => { ...move on... });
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Level Results")]
public class LevelResults : MonoBehaviour
{
    private const int MaxPlayers = 4;

    [Header("Backdrop")]
    [Tooltip("The simple black backdrop behind the room-complete panel and the win screen (0 = none, 1 = fully black). Not fully black on purpose: " +
             "the level shows through. The pause menu uses the same look.")]
    [SerializeField, Range(0f, 1f)] private float backdropOpacity = ScreenDim.DefaultOpacity;
    [Tooltip("Seconds the backdrop takes to fade in and out.")]
    [SerializeField, Min(0f)] private float backdropFadeSeconds = 0.25f;
    [Tooltip("Only used if the panel does not create a canvas of its own (the backdrop then gets one, drawn at this order). Normally the backdrop is " +
             "placed automatically, right behind the panel. If it ever covers the panel, lower this below the panel's canvas.")]
    [SerializeField] private int backdropFallbackSortingOrder = 450;

    // ------------------------------------------------------------ static API

    /// <summary>True while the results drop-down or the win screen is up.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>True while the screen is up and for the rest of the frame it closed in (so the key that picked a button can't also move a player).</summary>
    public static bool BlocksInput { get { return IsActive || closedFrame == Time.frameCount; } }

    /// <summary>Seconds the team has spent on the current room so far.</summary>
    public static float RoomSeconds { get { return instance != null ? instance.seconds : 0f; } }

    private static LevelResults instance;
    private static int closedFrame = -1;

    // The dungeon run so far: room number -> what the team did there. Kept across the Shop trip, cleared on every fresh start.
    private sealed class RoomResult
    {
        public int rank;
        public int p1;
        public int p2;
        public float seconds;
        public int collected;
        public int total;
    }

    private static readonly Dictionary<int, RoomResult> run = new Dictionary<int, RoomResult>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsActive = false;
        closedFrame = -1;
        instance = null;
        run.Clear();
    }

    /// <summary>FloorController calls this when its scene wakes up. A fresh start clears the run; coming back from the Shop keeps it.</summary>
    public static void OnSceneStart(bool returningFromShop)
    {
        IsActive = false;
        LockPlayers(false);   // a screen that was up when the last scene closed must not leave anybody frozen
        if (!returningFromShop) run.Clear();

        if (!RankSettings.Current.enabled) return;
        LevelResults r = EnsureInstance();
        if (r != null) r.BeginRoom();
    }

    /// <summary>
    /// FloorController calls this when every player is on the door. Returns true when the results screen took over (then 'proceed' is called by its
    /// button, so the caller must NOT move the team), false when the ranks are off or this is a PVP arena (then the door works as before).
    /// </summary>
    public static bool TryComplete(int floor, bool shopDoor, Action proceed)
    {
        RankSettings settings = RankSettings.Current;
        if (!settings.enabled || PvpMode.Enabled) return false;
        if (IsActive) return true;

        LevelResults r = EnsureInstance();
        if (r == null) return false;

        r.Begin(floor, shopDoor, proceed);
        return true;
    }

    /// <summary>For a door or exit of your own: shows the results, and calls proceed when the player picks Continue. False if the ranks are off.</summary>
    public static bool Complete(Action proceed)
    {
        return TryComplete(Mathf.Max(1, FloorController.CurrentFloor), false, proceed);
    }

    /// <summary>The menus (Level Select, Main Menu) call this: nothing from a finished or abandoned room may stay locked, frozen or half-counted.</summary>
    public static void ClearStaleState()
    {
        IsActive = false;
        closedFrame = -1;
        run.Clear();
        LockPlayers(false);
        FoeController.Frozen = false;
    }

    /// <summary>GameReset calls this when the scores go back to 0 (a Retry, a fresh start): the room's timer and gem count start over too.</summary>
    public static void NotifyScoresReset()
    {
        if (instance != null) instance.RestartAttempt();
    }

    /// <summary>
    /// For anything that moves the team on WITHOUT the door's results screen (FloorController.GoDown / OpenShop called from a script or a button):
    /// saves the room being played as cleared, with the time, gems and score so far. No screen is shown. Does nothing if the
    /// room was already saved (the door's screen saves it before it calls GoDown), if the ranks are off, or in a PVP arena.
    /// </summary>
    public static void RecordRoomIfUnsaved(int floor)
    {
        RankSettings settings = RankSettings.Current;
        if (!settings.enabled || PvpMode.Enabled || IsActive) return;
        if (instance == null || instance.savedThisRoom) return;

        LevelInfo level = instance.Resolve(Mathf.Max(1, floor), settings);
        RankTargets targets = settings.TargetsFor(level.targetStageId, level.number);

        LevelRank rank;
        int timeTier;
        int scoreTier;
        RunAttempt attempt = instance.BuildAttempt(settings, targets, out rank, out timeTier, out scoreTier);
        instance.SaveRoom(level, attempt);
    }

    /// <summary>The Level Select card dungeon floor N is the same room as: the Nth Puzzle stage in Level Select order. Null if there is none.</summary>
    public static StageDefinition StageForFloor(int floor)
    {
        List<StageDefinition> list = PuzzleStages();
        return floor >= 1 && floor <= list.Count ? list[floor - 1] : null;
    }

    /// <summary>
    /// Level Select calls this when it opens: a dungeon room cleared earlier (or before this fix) raises the bests of its card. Only bests move
    /// (plays and history are left alone), so it is safe to call every time.
    /// </summary>
    public static void SyncCardsFromDungeon()
    {
        if (!RankSettings.Current.dungeonCountsForLevelSelect) return;

        List<StageDefinition> list = PuzzleStages();
        for (int i = 0; i < list.Count; i++)
        {
            LevelRecords.RaiseBests(list[i].Id, LevelRecords.DungeonRoomId(i + 1));
        }
    }

    private static LevelResults EnsureInstance()
    {
        if (instance != null) return instance;

        GameObject go = new GameObject("Level Results");
        instance = go.AddComponent<LevelResults>();
        return instance;
    }

    // ------------------------------------------------------------ the room being played

    private float seconds;
    private int collectedNow;
    private int roomGems = -1;      // the automatic gem count for this room (-1 = not counted yet)
    private bool needsCount;
    private bool timing;
    private bool savedThisRoom;     // this room already has its result saved (by the results screen, or silently), so it is never saved twice

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    private void OnEnable()
    {
        ScorePickup.Collected += OnGemTaken;
        FloorController.FloorChanged += OnFloorChanged;
    }

    private void OnDisable()
    {
        ScorePickup.Collected -= OnGemTaken;
        FloorController.FloorChanged -= OnFloorChanged;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (IsActive)
        {
            // The scene is going away with a screen up: nothing may stay locked or frozen.
            IsActive = false;
            closedFrame = Time.frameCount;
            LockPlayers(false);   // RETRY / LEVEL SELECT / NEXT LEVEL leave through here and never unlocked the players
            FoeController.Frozen = false;
        }
    }

    private void BeginRoom()
    {
        savedThisRoom = false;
        seconds = 0f;
        collectedNow = 0;
        roomGems = -1;
        needsCount = true;   // counted in the first Update, when every scene object has woken up and StageLauncher has picked its stage
        timing = true;
    }

    private void RestartAttempt()
    {
        savedThisRoom = false;
        seconds = 0f;
        collectedNow = 0;
    }

    private void OnFloorChanged(int floor)
    {
        if (!IsActive) BeginRoom();
    }

    private void OnGemTaken(ScorePickup gem, int playerNumber)
    {
        if (!IsActive) collectedNow++;
    }

    private void Update()
    {
        if (needsCount)
        {
            needsCount = false;
            roomGems = CountRoomGems();
        }

        if (!timing || IsActive) return;
        if (FloorController.IsChangingFloor || PauseController.IsPaused || MenuController.IsOpen || LightsOutScreen.IsActive) return;

        seconds += Time.deltaTime;
    }

    // ------------------------------------------------------------ gems

    // How many gems belong to this room. Rooms share one scene, so each uncollected gem is given to the room whose start point is closest to it.
    // (RankTargets.totalCollectables overrides this with a number you type, for a room where the guess is wrong.)
    private int CountRoomGems()
    {
        ScorePickup[] all = FindObjectsByType<ScorePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all == null || all.Length == 0) return 0;

        List<List<Vector2>> anchors = new List<List<Vector2>>();   // per room: the points the players start from
        int current = -1;

        StageDefinition stage = StageLauncher.Current;
        if (stage != null && stage.mode == StageMode.Puzzle)
        {
            List<StageDefinition> stages = PuzzleStages();
            for (int i = 0; i < stages.Count; i++)
            {
                List<Vector2> points = new List<Vector2>();
                points.Add(stages[i].SpawnFor(1));
                points.Add(stages[i].SpawnFor(2));
                anchors.Add(points);
                if (stages[i] == stage) current = i;
            }
        }
        else
        {
            FloorController floors = FindFirstObjectByType<FloorController>();
            int count = floors != null ? floors.FloorCount : 0;
            for (int f = 1; f <= count; f++)
            {
                List<Vector2> points = new List<Vector2>();
                Vector2 spot;
                for (int n = 1; n <= 2; n++)
                {
                    if (floors.TryGetSpawnPoint(f, n, out spot)) points.Add(spot);
                }
                anchors.Add(points);
            }
            if (count > 0) current = Mathf.Clamp(FloorController.CurrentFloor - 1, 0, count - 1);
        }

        int mine = 0;
        for (int g = 0; g < all.Length; g++)
        {
            ScorePickup gem = all[g];
            if (gem == null || gem.IsCollected) continue;

            if (current < 0 || anchors.Count < 2)
            {
                mine++;   // one room, or no start points to compare: every gem is this room's
                continue;
            }

            Vector2 at = gem.transform.position;
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                for (int a = 0; a < anchors[i].Count; a++)
                {
                    float d = (anchors[i][a] - at).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
            }
            if (nearest == current) mine++;
        }
        return mine;
    }

    // ------------------------------------------------------------ which room is this

    private struct LevelInfo
    {
        public bool stageMode;
        public string id;             // what its record is saved under
        public string targetStageId;  // for RankSettings.TargetsFor ("" in the dungeon)
        public int number;            // 1-based room / level number
        public int count;             // rooms in the run
        public string title;
        public string subtitle;
        public bool isLast;
        public StageDefinition next;  // the stage after this one in Level Select (null if none)
    }

    // The Puzzle stages in Level Select order (same order as StageCatalog).
    public static List<StageDefinition> PuzzleStages()
    {
        List<StageDefinition> list = new List<StageDefinition>();
        for (int i = 0; i < StageDefinition.All.Count; i++)
        {
            StageDefinition s = StageDefinition.All[i];
            if (s != null && s.mode == StageMode.Puzzle) list.Add(s);
        }
        list.Sort((a, b) =>
        {
            if (a.order != b.order) return a.order.CompareTo(b.order);
            return string.CompareOrdinal(a.Title, b.Title);
        });
        return list;
    }

    private LevelInfo Resolve(int floor, RankSettings settings)
    {
        LevelInfo info = new LevelInfo();

        StageDefinition stage = FloorController.PickedPuzzleStage();   // StageLauncher.Current, or the stage picked in Level Select
        if (stage != null)
        {
            List<StageDefinition> list = PuzzleStages();
            int index = list.IndexOf(stage);

            info.stageMode = true;
            info.id = stage.Id;
            info.targetStageId = stage.Id;
            info.number = index >= 0 ? index + 1 : 1;
            info.count = Mathf.Clamp(settings.roomCount, 1, Mathf.Max(1, list.Count));
            info.title = stage.Title.ToUpperInvariant() + " COMPLETE";
            info.subtitle = "LEVEL " + info.number.ToString("00");
            info.isLast = info.number == info.count;
            info.next = index >= 0 && index + 1 < list.Count ? list[index + 1] : null;
            return info;
        }

        info.stageMode = false;
        info.number = Mathf.Max(1, floor);
        info.id = LevelRecords.DungeonRoomId(info.number);
        info.targetStageId = "";
        info.count = Mathf.Max(1, settings.roomCount);
        info.title = "ROOM " + info.number + " COMPLETE";
        info.subtitle = "ROOM " + info.number + " OF " + info.count;

        info.isLast = info.number >= info.count;
        return info;
    }

    // ------------------------------------------------------------ everybody is on the door

    private void Begin(int floor, bool shopDoor, Action proceed)
    {
        RankSettings settings = RankSettings.Current;
        LevelInfo level = Resolve(floor, settings);
        RankTargets targets = settings.TargetsFor(level.targetStageId, level.number);

        // Everybody holds still and the foes freeze while the screen is up.
        IsActive = true;
        timing = false;
        FoeController.Frozen = true;
        LockPlayers(true);

        int timeTier;
        int scoreTier;
        LevelRank rank;
        RunAttempt attempt = BuildAttempt(settings, targets, out rank, out timeTier, out scoreTier);
        int p1 = attempt.p1;
        int p2 = attempt.p2;
        int team = attempt.score;
        int total = attempt.total;

        // The tries BEFORE this one, newest first (read before the new one is saved).
        LevelRecord before = LevelRecords.Get(level.id);
        List<int> prior = new List<int>();
        if (before != null)
        {
            for (int i = before.history.Count - 1; i >= 0; i--) prior.Add(before.history[i].rank);
        }

        LevelRecords.Outcome outcome = SaveRoom(level, attempt);

        ResultsUi.RoomView view = new ResultsUi.RoomView();
        view.title = level.title;
        view.subtitle = level.subtitle;
        view.seconds = seconds;
        view.timeTier = timeTier;
        view.fastSeconds = targets.fastSeconds;
        view.collected = collectedNow;
        view.total = total;
        view.team = team;
        view.p1 = p1;
        view.p2 = p2;
        view.showP1 = CoopRules.Counts(1);
        view.showP2 = CoopRules.Counts(2);
        view.scoreTier = scoreTier;
        view.highScore = targets.highScore;
        view.rank = (int)rank;
        view.hadRecord = outcome.hadRecord;
        view.previousBestRank = outcome.previousBestRank;
        view.previousBestScore = outcome.previousBestScore;
        view.previousBestTime = outcome.previousBestTime;
        view.newBestRank = outcome.newBestRank;
        view.newBestScore = outcome.newBestScore;
        view.newBestTime = outcome.newBestTime;
        view.priorRanks = prior;

        ResultsUi.Panel panel = null;   // the choices below close it, and it is built after them

        if (level.stageMode)
        {
            BuildStageChoices(view, level, settings, () => panel);
        }
        else if (level.isLast)
        {
            view.choices.Add(Choice("FINISH", StageUi.Green, () => ShowWin(level, settings, panel)));
        }
        else
        {
            string text = shopDoor ? "TO THE SHOP" : "NEXT ROOM";
            view.choices.Add(Choice(text, StageUi.Green, () => ContinueDungeon(panel, proceed)));
        }
        view.defaultChoice = view.choices.Count - 1;

        Canvas[] canvasesBefore = ScreenDim.SnapshotCanvases();
        panel = ResultsUi.BuildRoom(view);
        ScreenDim.BackBehindNewCanvases(canvasesBefore, backdropOpacity, backdropFadeSeconds, backdropFallbackSortingOrder);   // translucent black behind the panel
        StartCoroutine(ResultsUi.Play(panel, settings.buttonGraceSeconds));
    }

    // The room being played right now as a finished try: the time, gems and score so far, and the rank they earn.
    private RunAttempt BuildAttempt(RankSettings settings, RankTargets targets, out LevelRank rank, out int timeTier, out int scoreTier)
    {
        int p1 = CoopRules.Counts(1) ? CoopScore.Earned(1) : 0;
        int p2 = CoopRules.Counts(2) ? CoopScore.Earned(2) : 0;
        int team = CoopScore.Total;

        int total = targets.totalCollectables >= 0 ? targets.totalCollectables : Mathf.Max(roomGems, 0);
        total = Mathf.Max(total, collectedNow);   // never "5 of 4"

        rank = RankCalc.Compute(settings, targets, seconds, team, collectedNow, total, out timeTier, out scoreTier);

        RunAttempt attempt = new RunAttempt();
        attempt.rank = (int)rank;
        attempt.score = team;
        attempt.p1 = p1;
        attempt.p2 = p2;
        attempt.seconds = seconds;
        attempt.collected = collectedNow;
        attempt.total = total;
        return attempt;
    }

    // Saves one finished room. It goes under its own id (a stage id, or dungeon/room-N), and a dungeon floor ALSO goes under the Level Select card
    // that floor is the same room as, so a run through the dungeon fills in the cards instead of leaving every one "not cleared".
    // The Outcome returned is the one for the room's own id (what the results screen compares against).
    private LevelRecords.Outcome SaveRoom(LevelInfo level, RunAttempt attempt)
    {
        savedThisRoom = true;

        LevelRecords.Outcome outcome = LevelRecords.Submit(level.id, attempt);
        string also = "";

        if (!level.stageMode)
        {
            RoomResult result = new RoomResult();
            result.rank = attempt.rank;
            result.p1 = attempt.p1;
            result.p2 = attempt.p2;
            result.seconds = attempt.seconds;
            result.collected = attempt.collected;
            result.total = attempt.total;
            run[level.number] = result;

            StageDefinition card = RankSettings.Current.dungeonCountsForLevelSelect ? StageForFloor(level.number) : null;
            if (card != null)
            {
                LevelRecords.Submit(card.Id, attempt);
                also = " and the Level Select card '" + card.Id + "'";
            }
        }

        Debug.Log("LevelResults: saved '" + level.id + "'" + also + " as rank " + RankCalc.Letter(attempt.rank) + (outcome.newBestRank ? " (new best rank)" : "") + ".", this);
        return outcome;
    }

    private void BuildStageChoices(ResultsUi.RoomView view, LevelInfo level, RankSettings settings, Func<ResultsUi.Panel> panelOf)
    {
        view.choices.Add(Choice("RETRY", StageUi.Orange, () => ReloadStage(level.id)));
        view.choices.Add(Choice("LEVEL SELECT", StageUi.Blue, () => LeaveToLevelSelect(settings)));

        if (level.isLast)
        {
            view.choices.Add(Choice("FINISH", StageUi.Green, () => ShowWin(level, settings, panelOf())));
        }
        else if (level.next != null)
        {
            string nextId = level.next.Id;
            view.choices.Add(Choice("NEXT LEVEL", StageUi.Green, () => ReloadStage(nextId)));
        }
    }

    private static ResultsUi.Choice Choice(string text, Color accent, Action onPick)
    {
        ResultsUi.Choice c = new ResultsUi.Choice();
        c.text = text;
        c.accent = accent;
        c.onPick = onPick;
        return c;
    }

    // ------------------------------------------------------------ moving on

    private void ContinueDungeon(ResultsUi.Panel panel, Action proceed)
    {
        // The next room starts from 0. Only the players' co-op score is cleared: the Shop's money (ScoreController) is a separate number and stays.
        CoopScore.ResetAll();
        seconds = 0f;
        collectedNow = 0;
        timing = true;   // (the floor change itself pauses it, and arriving on the new floor starts the next room's count from 0)

        CloseRoomPanel(panel);
        if (proceed != null) proceed();
    }

    private void CloseRoomPanel(ResultsUi.Panel panel)
    {
        ScreenDim.FadeOutAll(backdropFadeSeconds);   // the backdrop leaves with the panel
        IsActive = false;
        closedFrame = Time.frameCount;
        LockPlayers(false);
        FoeController.Frozen = false;
        if (panel != null) StartCoroutine(ResultsUi.Retract(panel));
    }

    private void ReloadStage(string stageId)
    {
        StageSelection.Select(stageId, StageMode.Puzzle);
        LeaveTo(SceneManager.GetActiveScene().name);
    }

    private void LeaveToLevelSelect(RankSettings settings)
    {
        string scene = StageUi.ResolveScene(settings.levelSelectScene, "LevelSelect", "Level Select");
        if (scene != null)
        {
            StageSelection.RequestBrowse(StageMode.Puzzle);
            LeaveTo(scene);
            return;
        }

        Debug.LogWarning("LevelResults: no Level Select scene can be loaded (add it to Build Settings and check Rank Settings > Level Select Scene), so the main menu opens.", this);
        LeaveToMainMenu(settings);
    }

    private void LeaveToMainMenu(RankSettings settings)
    {
        string scene = StageUi.ResolveScene(settings.mainMenuScene, "Mainmenu", "MainMenu", "Main Menu");
        if (scene == null)
        {
            Debug.LogWarning("LevelResults: no main menu scene can be loaded (add it to Build Settings and check Rank Settings > Main Menu Scene).", this);
            return;
        }
        LeaveTo(scene);
    }

    // IsActive stays true on purpose: nobody can move while the screen fades to black.
    private void LeaveTo(string scene)
    {
        GameReset.ResetRun();
        FoeController.Frozen = false;
        StageUi.FadeAndLoad(this, scene);
    }

    private static void LockPlayers(bool locked)
    {
        for (int n = 1; n <= MaxPlayers; n++)
        {
            PlayerInputSource source = PlayerInputSource.Get(n);
            if (source != null) source.Locked = locked;
        }
    }

    // ------------------------------------------------------------ the win screen

    private void ShowWin(LevelInfo level, RankSettings settings, ResultsUi.Panel roomPanel)
    {
        // The room panel goes away; the screen stays "active" so the players stay locked and the foes frozen.
        if (roomPanel != null) StartCoroutine(ResultsUi.Retract(roomPanel));
        ScreenDim.FadeOutAll(backdropFadeSeconds);   // the room panel's backdrop fades out while the win screen's fades in

        int n = Mathf.Max(1, level.count);
        int[] ranks = new int[n];
        List<int> missingRooms = new List<int>();
        int p1 = 0;
        int p2 = 0;
        int collected = 0;
        int total = 0;
        float time = 0f;

        List<StageDefinition> stages = level.stageMode ? PuzzleStages() : null;

        for (int i = 0; i < n; i++)
        {
            RunAttempt a = null;

            if (level.stageMode)
            {
                // Your BEST try on each stage counts.
                LevelRecord record = i < stages.Count ? LevelRecords.Get(stages[i].Id) : null;
                if (record != null && record.Cleared) a = record.bestRun;
            }
            else
            {
                RoomResult r;
                if (run.TryGetValue(i + 1, out r))
                {
                    a = new RunAttempt();
                    a.rank = r.rank;
                    a.p1 = r.p1;
                    a.p2 = r.p2;
                    a.seconds = r.seconds;
                    a.collected = r.collected;
                    a.total = r.total;
                }
            }

            if (a == null)
            {
                ranks[i] = RankCalc.NoRank;
                missingRooms.Add(i + 1);
                continue;
            }

            ranks[i] = a.rank;
            p1 += a.p1;
            p2 += a.p2;
            collected += a.collected;
            total += a.total;
            time += a.seconds;
        }

        int missing;
        bool capped;
        LevelRank overall = RankCalc.Overall(settings, ranks, out missing, out capped);

        string what = level.stageMode ? "LEVEL" : "ROOM";
        ResultsUi.WinView view = new ResultsUi.WinView();
        view.subtitle = missing == 0
            ? "ALL " + n + (level.stageMode ? " LEVELS" : " ROOMS") + " CLEARED"
            : (n - missing) + " OF " + n + (level.stageMode ? " LEVELS" : " ROOMS") + " CLEARED";
        view.p1 = p1;
        view.p2 = p2;
        view.showP1 = CoopRules.Counts(1);
        view.showP2 = CoopRules.Counts(2);
        view.team = p1 + p2;
        view.seconds = time;
        view.collected = collected;
        view.total = total;
        view.rank = (int)overall;
        view.roomRanks = ranks;
        view.missing = missing;
        view.capped = capped;
        view.missingText = MissedText(what, missingRooms);
        view.capText = "P, S AND A ARE OUT OF REACH (BEST POSSIBLE: " + RankCalc.Letter((int)settings.capRankWhenMissing) + ")";

        RunAttempt overallAttempt = new RunAttempt();
        overallAttempt.rank = (int)overall;
        overallAttempt.score = p1 + p2;
        overallAttempt.p1 = p1;
        overallAttempt.p2 = p2;
        overallAttempt.seconds = time;
        overallAttempt.collected = collected;
        overallAttempt.total = total;

        LevelRecords.Outcome outcome = LevelRecords.Submit(level.stageMode ? LevelRecords.StagesOverallId : LevelRecords.DungeonId, overallAttempt);
        view.hadRecord = outcome.hadRecord;
        view.previousBestRank = outcome.previousBestRank;
        view.previousBestScore = outcome.previousBestScore;
        view.newBestRank = outcome.newBestRank;
        view.newBestScore = outcome.newBestScore;

        if (!level.stageMode)
        {
            view.choices.Add(Choice("PLAY AGAIN", StageUi.Green, () => LeaveTo(SceneManager.GetActiveScene().name)));
        }
        view.choices.Add(Choice("LEVEL SELECT", StageUi.Blue, () => LeaveToLevelSelect(settings)));
        view.choices.Add(Choice("MAIN MENU", StageUi.Violet, () => LeaveToMainMenu(settings)));
        view.defaultChoice = 0;

        Canvas[] canvasesBefore = ScreenDim.SnapshotCanvases();
        ResultsUi.Panel win = ResultsUi.BuildWin(view);
        ScreenDim.BackBehindNewCanvases(canvasesBefore, backdropOpacity, backdropFadeSeconds, backdropFallbackSortingOrder);
        StartCoroutine(ResultsUi.Play(win, settings.buttonGraceSeconds));
    }

    private static string MissedText(string what, List<int> rooms)
    {
        if (rooms.Count == 0) return "";

        string list = "";
        for (int i = 0; i < rooms.Count; i++)
        {
            if (i > 0) list += ", ";
            list += rooms[i];
        }
        return rooms.Count == 1 ? "MISSED " + what + " " + list : "MISSED " + what + "S " + list;
    }
}
