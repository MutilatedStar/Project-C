using UnityEngine;
using Ilumisoft.HealthSystem;

/// <summary>
/// One place that puts a run back to its starting state: score to 0, the player's health to full, and the player
/// back to the starting sword (every other sword removed).
///
/// Used by:
///   - PauseController's "Quit to Main Menu" button
///   - RetryButton on the Failure screen
///
/// Other scripts can call it too (a victory screen that returns to the Main Menu, say):
///   GameReset.ResetRun();
///
/// Nothing to add to a scene: this is a plain static class.
/// </summary>
public static class GameReset
{
    /// <summary>Score back to 0, player health back to full, and only the starting sword owned and equipped.</summary>
    public static void ResetRun()
    {
        ScoreController.ResetScore();
        RestorePlayerHealth();
        SwordController.ResetToStartingSword();
    }

    /// <summary>
    /// Gives the player full health again, even if the player is dead.
    /// (SetHealth is used on purpose: AddHealth and ApplyDamage do nothing on a dead player.)
    /// Does nothing if there is no player in the loaded scenes: a new player starts at full health by itself.
    /// </summary>
    public static void RestorePlayerHealth()
    {
        HealthComponent health = FindPlayerHealth();
        if (health == null) return;

        health.SetHealth(health.MaxHealth);
    }

    // The player is found through its movement script, the same way PauseController and FloorController do it.
    // Inactive objects are included, and so is a player that is kept alive between scenes (DontDestroyOnLoad).
    private static HealthComponent FindPlayerHealth()
    {
        var controller = Object.FindAnyObjectByType<Cainos.PixelArtTopDown_Basic.TopDownCharacterController>(FindObjectsInactive.Include);
        if (controller == null) return null;

        HealthComponent health = controller.GetComponentInParent<HealthComponent>(true);
        if (health == null) health = controller.GetComponentInChildren<HealthComponent>(true);
        return health;
    }
}
