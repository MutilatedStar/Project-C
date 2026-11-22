using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The numbers a room is judged by: how fast, how many points, how many gems. One of these is the default for every room;
/// RankSettings can hold different ones for particular rooms or stages.
///
///   TIME     seconds from the moment the players take control of the room until they are all on the door.
///            at or under Fast       -> FAST      (the top time tier)
///            at or under Medium     -> GOOD
///            at or under Slow       -> SLOW
///            over Slow              -> TOO SLOW
///   SCORE    the combined score of every player that counts, earned in THIS room (it starts from 0 in every room).
///            at or over High        -> HIGH      (the top score tier)
///            at or over Medium      -> MEDIUM
///            at or over Low         -> LOW
///            under Low              -> NONE
///   GEMS     how many ScorePickup gems were taken, out of how many the room has.
/// </summary>
[Serializable]
public class RankTargets
{
    [Header("Time (seconds)")]
    [Tooltip("At or under this the time is FAST. P, S and A all need a FAST time.")]
    [Min(1f)] public float fastSeconds = 60f;
    [Tooltip("At or under this the time is GOOD.")]
    [Min(1f)] public float mediumSeconds = 90f;
    [Tooltip("At or under this the time is SLOW. Over it the time is TOO SLOW.")]
    [Min(1f)] public float slowSeconds = 150f;

    [Header("Score (all players added together, this room only)")]
    [Tooltip("At or over this the score is HIGH. P and S need a HIGH score.")]
    [Min(0)] public int highScore = 1500;
    [Tooltip("At or over this the score is MEDIUM. A needs at least a MEDIUM score.")]
    [Min(0)] public int mediumScore = 800;
    [Tooltip("At or over this the score is LOW. Under it the score is NONE.")]
    [Min(0)] public int lowScore = 300;

    [Header("Gems")]
    [Tooltip("How many gems the room has. -1 = count them automatically when the room starts (every uncollected ScorePickup that is closer to this room than to any other). " +
             "Type a number if the automatic count is wrong for this room. 0 = the room has no gems, so P only needs a fast time and a high score.")]
    [Min(-1)] public int totalCollectables = -1;

    /// <summary>Keeps Fast &lt;= Medium &lt;= Slow and High &gt;= Medium &gt;= Low, so a typo can't make a tier impossible to reach.</summary>
    public void Sanitize()
    {
        if (mediumSeconds < fastSeconds) mediumSeconds = fastSeconds;
        if (slowSeconds < mediumSeconds) slowSeconds = mediumSeconds;
        if (mediumScore > highScore) mediumScore = highScore;
        if (lowScore > mediumScore) lowScore = mediumScore;
    }
}

/// <summary>Different targets for one room (Room) of one stage (Stage Id), or for every stage's room N, or for one stage's every room.</summary>
[Serializable]
public class RankTargetOverride
{
    [Tooltip("Just a name for this row in the Inspector. The game doesn't read it.")]
    public string label = "";
    [Tooltip("The stage this applies to (its Id: the object name in lower case with dashes, e.g. stage-3). Empty = any stage / the normal dungeon.")]
    public string stageId = "";
    [Tooltip("The room or level number this applies to, counting from 1 (the first room is 1, the sixth is 6; it is the number on the Level Select card). 0 = every room.")]
    [Min(0)] public int room = 0;
    public RankTargets targets = new RankTargets();
}

/// <summary>
/// ALL the numbers behind the ranks, in one asset you edit in the Inspector:  Assets/Resources/RankSettings.asset
/// (created for you; menu Tools > Stages > Rank Settings selects it).
///
/// THE RANKS (P is the best):
///   P   PERFECT      every gem + a FAST time + a HIGH score
///   S   WONDERFUL    a FAST time + a HIGH score, but some gems missed
///   A   GREAT        a FAST time + at least a MEDIUM score
///   B / C / D / F    everything else, by points:  time tier (0-3) + score tier (0-3)   ->   B at B Points or more, C at C Points, D at D Points, else F
///   So a slow clear can never be P, S or A, however many points it earned: time is part of every rank.
///
/// THE WIN SCREEN (after the last room) averages the room ranks into ONE overall rank for the team. P needs every room to be P. If ANY room
/// was never cleared, the overall rank is capped at Cap Rank When Missing (B by default), so P, S and A become impossible.
/// </summary>
[CreateAssetMenu(fileName = "RankSettings", menuName = "Stages/Rank Settings")]
public class RankSettings : ScriptableObject
{
    public const string ResourceName = "RankSettings";

    [Header("On / off")]
    [Tooltip("OFF: no results panel, no win screen, doors work as before.")]
    public bool enabled = true;

    [Header("The run")]
    [Tooltip("How many rooms the run has, COUNTING THE FIRST ROOM (room 0) AS ROOM ONE. 6 = rooms 0 to 5 = six rooms. Beating the last one shows the win screen. " +
             "For the dungeon this is the floor number of the last floor; for Level Select it is the stage number of the last stage.")]
    [Min(1)] public int roomCount = 6;

    [Header("Targets used by every room (unless a row below says otherwise)")]
    public RankTargets defaults = new RankTargets();

    [Header("Rank rules")]
    [Tooltip("Below a FAST time with a HIGH score you can still get B, C, D or F. A room scores time tier + score tier (each 0 to 3). " +
             "B needs at least this many points (of 6).")]
    [Range(1, 6)] public int bPoints = 4;
    [Tooltip("C needs at least this many points (of 6).")]
    [Range(1, 6)] public int cPoints = 3;
    [Tooltip("D needs at least this many points (of 6). Under it the rank is F.")]
    [Range(1, 6)] public int dPoints = 2;
    [Tooltip("S needs at least this share of the gems (0 = any, so S only needs a fast time and a high score). Under it a fast, high-scoring room is an A.")]
    [Range(0f, 1f)] public float sMinCollectFraction = 0f;

    [Header("Missing a room")]
    [Tooltip("The best overall rank possible when a room of the run was never cleared. B means P, S and A are impossible.")]
    public LevelRank capRankWhenMissing = LevelRank.B;

    [Header("Different targets for particular rooms or stages")]
    [Tooltip("The most specific row wins: a row with a stage AND a room beats a row with only a stage, which beats a row with only a room, which beats the defaults above.")]
    public List<RankTargetOverride> overrides = new List<RankTargetOverride>();

    [Header("Level Select cards")]
    [Tooltip("ON: clearing floor N of the dungeon (or ending the run from it) also counts as clearing the Nth card on Level Select, with the same rank, score and time, " +
             "so playing through the dungeon fills in the cards instead of leaving them all 'not cleared'. Rooms cleared before are folded in when Level Select opens. " +
             "Needs dungeon floor N to be the same room as Level Select stage N; switch it OFF if your floors and stages are not in the same order.")]
    public bool dungeonCountsForLevelSelect = true;

    [Header("Screens")]
    [Tooltip("Exact name of the Level Select scene (the buttons on the screens use it). Capitals and spaces don't matter.")]
    public string levelSelectScene = "LevelSelect";
    [Tooltip("Exact name of the main menu scene.")]
    public string mainMenuScene = "Mainmenu";
    [Tooltip("Seconds the Continue / Retry / Level Select buttons can't be pressed after they appear, so a player still hammering a key at the door can't skip the results.")]
    [Min(0f)] public float buttonGraceSeconds = 0.6f;

    // ------------------------------------------------------------ lookup

    /// <summary>The targets for this stage's room: the most specific matching row, else the defaults. stageId may be empty (the dungeon).</summary>
    public RankTargets TargetsFor(string stageId, int room)
    {
        RankTargets best = defaults;
        int bestSpecificity = 0;

        for (int i = 0; overrides != null && i < overrides.Count; i++)
        {
            RankTargetOverride o = overrides[i];
            if (o == null || o.targets == null) continue;

            bool stageRow = !string.IsNullOrEmpty(o.stageId);
            bool roomRow = o.room > 0;
            if (!stageRow && !roomRow) continue;   // matches everything: that is what 'defaults' is for
            if (stageRow && StageDefinition.MakeId(o.stageId) != StageDefinition.MakeId(stageId)) continue;
            if (roomRow && o.room != room) continue;

            int specificity = (stageRow ? 2 : 0) + (roomRow ? 1 : 0);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                best = o.targets;
            }
        }
        return best ?? defaults ?? new RankTargets();
    }

    private void OnValidate()
    {
        if (defaults == null) defaults = new RankTargets();
        defaults.Sanitize();
        for (int i = 0; overrides != null && i < overrides.Count; i++)
        {
            if (overrides[i] == null) continue;
            if (overrides[i].targets == null) overrides[i].targets = new RankTargets();
            overrides[i].targets.Sanitize();
        }
        if (cPoints > bPoints) cPoints = bPoints;
        if (dPoints > cPoints) dPoints = cPoints;
    }

    // ------------------------------------------------------------ loading

    private static RankSettings current;
    private static bool warned;

    /// <summary>
    /// The settings asset (Assets/Resources/RankSettings.asset). If the project has none yet, a copy of the built-in defaults is used, so the
    /// ranks still work; Tools > Stages > Rank Settings creates the real asset.
    /// </summary>
    public static RankSettings Current
    {
        get
        {
            if (current != null) return current;

            current = Resources.Load<RankSettings>(ResourceName);
            if (current == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("RankSettings: there is no Assets/Resources/RankSettings.asset, so the built-in default targets are used. " +
                                     "Open the menu  Tools > Stages > Rank Settings  to create it and set your own times and scores.");
                }
                current = CreateInstance<RankSettings>();
                current.hideFlags = HideFlags.HideAndDontSave;
            }
            current.OnValidate();
            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        warned = false;
    }
}
