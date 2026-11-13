using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// One place that puts a run back to its starting state: score to 0 and every player's health to full.
///
/// Used by:
///   - PauseController's "Quit to Main Menu" button
///   - RetryButton on the Failure screen
///   - LightsOutScreen (Level Select / Menu, and Retry for the score part)
///   - CoopScore, when a scene is loaded fresh (the Shop trip excepted)
///
/// Other scripts can call it too (a victory screen that returns to the Main Menu, say):
///   GameReset.ResetRun();      // score and health
///   GameReset.ResetScores();   // score only
///
/// Nothing to add to a scene: this is a plain static class.
/// </summary>
public static class GameReset
{
    /// <summary>Score back to 0 and the player's health back to full.</summary>
    public static void ResetRun()
    {
        ResetScores();
        RestorePlayerHealth();
    }

    /// <summary>
    /// Every score back to 0: each player's co-op score (CoopScore) AND the single team score the Shop spends (ScoreController),
    /// so the score on screen and the Shop money never disagree.
    /// </summary>
    public static void ResetScores()
    {
        CoopScore.ResetAll();
        ScoreController.ResetScore();
    }

    /// <summary>
    /// Gives EVERY player full health again, even a dead one.
    /// (SetHealth is used on purpose: AddHealth and ApplyDamage do nothing on a dead player.)
    /// Co-op players register themselves in CoopHealth.Players, so nothing is searched for. Only a project whose player
    /// still has the old health component falls back to one search for the single player.
    /// A Player 2 that is switched off isn't registered: it is created fresh (full health) when it is switched on again.
    /// </summary>
    public static void RestorePlayerHealth()
    {
        var players = CoopHealth.Players;
        if (players.Count > 0)
        {
            for (int i = 0; i < players.Count; i++) players[i].SetHealth(players[i].MaxHealth);
            return;
        }

        HealthComponent health = FindLegacyPlayerHealth();
        if (health == null) return;   // no player in the loaded scenes: a new player starts at full health by itself

        health.SetHealth(health.MaxHealth);
    }

    // Legacy single-player path, kept so a player without CoopHealth still resets. Inactive objects are included, and so is
    // a player that is kept alive between scenes (DontDestroyOnLoad).
    private static HealthComponent FindLegacyPlayerHealth()
    {
        var controller = Object.FindAnyObjectByType<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>(FindObjectsInactive.Include);
        if (controller == null) return null;

        HealthComponent health = controller.GetComponentInParent<HealthComponent>(true);
        if (health == null) health = controller.GetComponentInChildren<HealthComponent>(true);
        return health;
    }
}
