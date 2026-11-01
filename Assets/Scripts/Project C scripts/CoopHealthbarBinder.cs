using UnityEngine;
using Ilumisoft.HealthSystem;
using Ilumisoft.HealthSystem.UI;

/// <summary>
/// One health bar per player. Player 2 is usually spawned from a prefab, and a bar in the scene can't point at a
/// prefab that doesn't exist yet, so this finds the player by number at runtime instead:
///   - the bar appears when that player is in the game (and counts), and hides when they aren't
///   - it re-connects by itself if the player is destroyed and spawned again
///
/// SET UP, once for each player:
///   1. Make an empty parent object in the Canvas (this script goes here, and it must stay switched ON).
///   2. Put the bar (the object with Healthbar or PlayerHealthbar on it) as a CHILD of it. Leave the bar's Health
///      slot (and PlayerHealthbar's Player slot) empty.
///   3. Drag that child into Healthbar below and set Player Number (1 or 2).
/// Works for Player 1 as well, so both bars behave the same.
///
/// WHERE the bars sit on screen is handled by CoopHealthbarLayout (free / mirror / auto-snap). This script moves
/// nothing itself: it only shows and hides the bar and lets the layout script find it (through CoopHudPanel).
/// </summary>
public class CoopHealthbarBinder : CoopHudPanel
{
    [Tooltip("The Healthbar to feed. It must be a CHILD object, not this one: this object stays on while the bar is hidden.")]
    public Healthbar healthbar;

    public override HudGroup Group { get { return HudGroup.Health; } }

    /// <summary>True while the bar is showing (that player is in the game).</summary>
    public override bool IsShown { get { return healthbar != null && healthbar.gameObject.activeSelf; } }

    // The bar's own rectangle is its size, unless a Footprint is set.
    protected override RectTransform DefaultFootprint { get { return healthbar != null ? healthbar.transform as RectTransform : null; } }

    /// <summary>The binder for this player number, or null.</summary>
    public static CoopHealthbarBinder Get(int playerNumber)
    {
        return Get(HudGroup.Health, playerNumber) as CoopHealthbarBinder;
    }

    private void Awake()
    {
        // Hidden until the player exists, so the bar never runs without a health to read.
        if (healthbar != null) healthbar.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (healthbar == null) return;

        CoopHealth player = CoopHealth.Get(playerNumber);
        bool show = player != null && CoopRules.Counts(playerNumber);

        // Connect BEFORE switching the bar on: its Start reads the health straight away.
        if (show && healthbar.Health != player) healthbar.Health = player;

        if (healthbar.gameObject.activeSelf != show) healthbar.gameObject.SetActive(show);
    }
}
