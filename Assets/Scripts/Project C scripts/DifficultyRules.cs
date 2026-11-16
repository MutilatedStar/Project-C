using UnityEngine;

/// <summary>
/// THE DIFFICULTY RULES. One static place every script can read, same idea as CoopRules and PvpMode.
///
///                  trap damage (lava / water / spikes)    revive    one dies
///     Easy         x 0.5                                  yes       the other keeps playing
///     Medium       x 1  (the damage as it is set up)      yes       the other keeps playing   ("Medium" is FoeController.Difficulty.Normal)
///     Hard         x 2                                    no        BOTH die
///
/// The difficulty itself is still FoeController.CurrentDifficulty (foes already read it), so there is only ONE number. This script adds:
///
///   Active          the difficulty that is really in force right now: PVP always counts as Easy, whatever is set, so players fight each
///                   other instead of dying to puzzles. Read this (not FoeController.CurrentDifficulty) for anything the rules above touch.
///   Remembered      the last difficulty picked under PLAY on the main menu (Medium if nothing was picked yet). Level Select runs on it.
///                   PVP never changes it: go into PVP and back out and Level Select still has your last difficulty.
///
///   TrapDamageScale 0.5 / 1 / 2 for Active. FloorManager multiplies lava, water and spike damage by it.
///   LinkedDeath     true on Hard: when one player dies the rest die too. CoopHealth does it.
///   CanRevive       false on Hard and in PVP. CoopDowned asks before it lets anybody save a downed player.
///
/// Who sets it:
///   MainMenuController   PLAY -> Easy / Normal / Hard calls Choose.  LEVEL SELECT calls UseForLevelSelect.  PVP calls UseForPvp.
///   PvpMode.Changed      keeps FoeController.CurrentDifficulty in step by itself (Easy while PVP is on, Remembered when it goes off), so a
///                        stage that flips PvpMode from somewhere else (StageLauncher) can't leave the wrong difficulty behind.
///
/// The memory lives for the play session (it survives scene loads and is reset when the game starts), like PvpMode.
/// </summary>
public static class DifficultyRules
{
    public const float EasyTrapScale = 0.5f;
    public const float NormalTrapScale = 1f;
    public const float HardTrapScale = 2f;

    private static bool _hasChosen;
    private static FoeController.Difficulty _chosen = FoeController.Difficulty.Normal;

    /// <summary>True once a difficulty was picked under PLAY this session.</summary>
    public static bool HasChosen { get { return _hasChosen; } }

    /// <summary>The last difficulty picked under PLAY, or Medium (Normal) if none was picked yet. PVP never changes it.</summary>
    public static FoeController.Difficulty Remembered
    {
        get { return _hasChosen ? _chosen : FoeController.Difficulty.Normal; }
    }

    /// <summary>The difficulty in force right now. PVP counts as Easy whatever FoeController.CurrentDifficulty says.</summary>
    public static FoeController.Difficulty Active
    {
        get { return PvpMode.Enabled ? FoeController.Difficulty.Easy : FoeController.CurrentDifficulty; }
    }

    /// <summary>What trap damage (lava, water, spikes) is multiplied by: 0.5 Easy, 1 Medium, 2 Hard.</summary>
    public static float TrapDamageScale
    {
        get
        {
            switch (Active)
            {
                case FoeController.Difficulty.Easy: return EasyTrapScale;
                case FoeController.Difficulty.Hard: return HardTrapScale;
                default: return NormalTrapScale;
            }
        }
    }

    /// <summary>Hard: when one player dies, every other player that counts dies with them. Never in PVP.</summary>
    public static bool LinkedDeath
    {
        get { return Active == FoeController.Difficulty.Hard; }
    }

    /// <summary>Easy and Medium: a downed player can be revived. Not on Hard (they both die) and not in PVP (nobody is revived there).</summary>
    public static bool CanRevive
    {
        get { return !PvpMode.Enabled && Active != FoeController.Difficulty.Hard; }
    }

    // ------------------------------------------------------------ set by the main menu

    /// <summary>PLAY on the main menu: this difficulty runs now, and Level Select will use it later.</summary>
    public static void Choose(FoeController.Difficulty difficulty)
    {
        _chosen = difficulty;
        _hasChosen = true;
        FoeController.CurrentDifficulty = difficulty;
    }

    /// <summary>LEVEL SELECT: the last difficulty picked under PLAY, or Medium if there is none. Does not count as a pick.</summary>
    public static void UseForLevelSelect()
    {
        FoeController.CurrentDifficulty = Remembered;
    }

    /// <summary>PVP: Easy for the match. The remembered difficulty is left alone.</summary>
    public static void UseForPvp()
    {
        FoeController.CurrentDifficulty = FoeController.Difficulty.Easy;
    }

    // ------------------------------------------------------------ keeping FoeController in step with PVP

    private static void OnPvpChanged(bool on)
    {
        FoeController.CurrentDifficulty = on ? FoeController.Difficulty.Easy : Remembered;
    }

    // BeforeSceneLoad runs after every SubsystemRegistration reset, so PvpMode.ResetStatics (which clears its Changed event) can't wipe this.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStatics()
    {
        _hasChosen = false;
        _chosen = FoeController.Difficulty.Normal;

        PvpMode.Changed -= OnPvpChanged;
        PvpMode.Changed += OnPvpChanged;
    }
}
