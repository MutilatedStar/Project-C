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
    /// Which list the LEVEL SELECT SCENE opens on (Puzzle = Level Select page, Pvp = PVP Arenas page). Only meaningful when BrowseRequested is
    /// true; whoever loads the Level Select scene calls RequestBrowse. LevelSelectController reads it once and clears BrowseRequested, so
    /// pressing Play inside the Level Select scene by itself uses that controller's own Default Mode instead.
    /// </summary>
    public static StageMode BrowseMode = StageMode.Puzzle;
    public static bool BrowseRequested;

    /// <summary>Call this right before loading the Level Select scene (main menu buttons, the Lights Out screen, the PVP result screen).</summary>
    public static void RequestBrowse(StageMode mode)
    {
        BrowseMode = mode;
        BrowseRequested = true;
    }

    /// <summary>
    /// Old way in: "when the MAIN MENU opens, jump to the stage list". Nothing in the project sets it any more (the Level Select scene is loaded
    /// directly), but MainMenuController still honours it by forwarding to the Level Select scene, so an old caller can't get stuck.
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
        BrowseMode = StageMode.Puzzle;
        BrowseRequested = false;
    }
}
