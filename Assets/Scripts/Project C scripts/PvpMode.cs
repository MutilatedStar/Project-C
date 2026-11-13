using System;
using UnityEngine;

/// <summary>
/// THE PVP TOGGLE. One static switch every script can read, same idea as CoopRules and LocalCoop.
///
///     PvpMode.Enabled = true;    // PVP mode:    swords hurt the OTHER player, nobody can be revived, first to N rounds wins
///     PvpMode.Enabled = false;   // everything else (Easy / Normal / Hard and the puzzle stages): players can never hurt each other
///
/// Who flips it:
///   - StageLauncher (Puzzle scene) sets it from the stage that was picked: a stage whose Mode is Pvp turns it ON, a Puzzle stage turns it OFF.
///   - MainMenuController and LevelSelectController turn it OFF whenever a menu opens; LevelSelectController sets it before it loads a stage.
///   - You can also flip it yourself (a debug key, an Inspector button...): PvpMode.Toggle().
///
/// The value survives scene loads (menu -> stage -> menu) on purpose. It is reset only when the game starts.
/// </summary>
public static class PvpMode
{
    private static bool _enabled;

    /// <summary>Raised when the toggle changes. The value is the new state.</summary>
    public static event Action<bool> Changed;

    public static bool Enabled
    {
        get { return _enabled; }
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled) ApplyRules();
            if (Changed != null) Changed(_enabled);
        }
    }

    /// <summary>Multiplies the sword's damage when it hits a PLAYER (never foes). 1 = same damage as against foes.</summary>
    public static float DamageScale = 1f;

    /// <summary>Rounds a player must win to win the match (set by the PVP menu, 1 to 3).</summary>
    public static int RoundsToWin = 2;

    public static int Player1Wins;
    public static int Player2Wins;
    public static int RoundsPlayed;

    /// <summary>True when attacker may hurt victim: PVP is on and they are two different players.</summary>
    public static bool CanHurt(int attackerNumber, int victimNumber)
    {
        return _enabled && attackerNumber != victimNumber;
    }

    /// <summary>
    /// PVP needs the co-op bonuses OFF: shared damage would hurt the attacker too, shared score would merge the scoreboards,
    /// and single player mode would make one player unhurtable. Cheap to call every frame (the setters ignore unchanged values).
    /// </summary>
    public static void ApplyRules()
    {
        CoopRules.SharedDamage = false;
        CoopRules.SharedScore = false;
        CoopRules.SinglePlayerMode = false;
    }

    /// <summary>
    /// PVP needs Player 2. Switches the second player on for this session WITHOUT overwriting the saved co-op choice in PlayerPrefs.
    /// </summary>
    public static void RequireTwoPlayers()
    {
        if (LocalCoop.Enabled) return;

        bool keep = LocalCoop.RememberChoice;
        LocalCoop.RememberChoice = false;
        LocalCoop.Set(true);
        LocalCoop.RememberChoice = keep;
    }

    public static void ResetMatch()
    {
        Player1Wins = 0;
        Player2Wins = 0;
        RoundsPlayed = 0;
    }

    public static void Toggle()
    {
        Enabled = !Enabled;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _enabled = false;
        Changed = null;
        DamageScale = 1f;
        RoundsToWin = 2;
        ResetMatch();
    }
}
