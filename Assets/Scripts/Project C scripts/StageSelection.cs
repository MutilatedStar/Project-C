using UnityEngine;

/// <summary>
/// What the player picked in the menu, carried across the scene load (menu -> Puzzle scene).
/// StageLauncher reads it when the stage scene opens. Static on purpose: it must survive the scene change.
/// </summary>
public static class StageSelection
{
    /// <summary>Id of the picked stage (StageDefinition.Id). Empty = nothing picked.</summary>
    public static string StageId = "";

    public static StageMode Mode = StageMode.Puzzle;

    /// <summary>
    /// Set by the in-game "Level Select" button (LightsOutScreen) and the PVP end screen. The menu reads it once when it opens and jumps
    /// straight to the stage list of the mode you were just playing, instead of the main page.
    /// </summary>
    public static bool ReturnToStageSelect;

    public static bool HasSelection { get { return !string.IsNullOrEmpty(StageId); } }

    /// <summary>Remember a pick. Also starts a fresh PVP match (scores back to 0-0).</summary>
    public static void Select(string id, StageMode mode)
    {
        StageId = id ?? "";
        Mode = mode;
        PvpMode.ResetMatch();
    }

    /// <summary>Forget the pick (used by the normal Easy / Normal / Hard game, which has no stage).</summary>
    public static void Clear()
    {
        StageId = "";
        Mode = StageMode.Puzzle;
        PvpMode.ResetMatch();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        StageId = "";
        Mode = StageMode.Puzzle;
        ReturnToStageSelect = false;
    }
}
