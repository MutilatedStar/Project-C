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
/// IF THE BAR NEVER APPEARS, in this order (a message in the Console names the one that applies after 3 seconds):
///   1. The Healthbar slot is empty, or the bar is this object / a parent of it (must be a CHILD).
///   2. No CoopHealth with this Player Number is in the game: the player prefab needs CoopHealth (not plain Health)
///      and PlayerInputSource.Player Number must match. Player 2 also needs LocalCoop switched on.
///   3. The canvas is not Screen Space - Overlay, or its Sort Order is at or below the split-screen picture
///      (CoopHudPanel warns about both when the bar is enabled).
///
/// WHERE the bars sit on screen is handled by CoopHealthbarLayout (free / mirror / auto-snap). This script moves
/// nothing itself: it only shows and hides the bar and lets the layout script find it (through CoopHudPanel).
/// The bar's size is measured automatically, so leave the Footprint slot empty.
/// </summary>
public class CoopHealthbarBinder : CoopHudPanel
{
    [Tooltip("The Healthbar to feed. It must be a CHILD object, not this one: this object stays on while the bar is hidden.")]
    public Healthbar healthbar;

    public override HudGroup Group { get { return HudGroup.Health; } }

    /// <summary>True while the bar is showing (that player is in the game).</summary>
    public override bool IsShown { get { return healthbar != null && healthbar.gameObject.activeSelf; } }

    /// <summary>Why the bar is hidden: it only shows for a player that has a CoopHealth and counts.</summary>
    public override string HiddenReason
    {
        get
        {
            CoopHealth player = CoopHealth.Get(playerNumber);
            if (player == null)
            {
                string found = "";
                for (int i = 0; i < CoopHealth.Players.Count; i++)
                {
                    found += (i > 0 ? ", " : "") + "Player " + CoopHealth.Players[i].PlayerNumber;
                }

                return "no CoopHealth with Player Number " + playerNumber + " is in the game. CoopHealth components found: " +
                       (CoopHealth.Players.Count == 0 ? "none" : found) +
                       ". The player prefab needs CoopHealth (not the plain Health component), and its PlayerInputSource Player Number must match.";
            }

            if (!CoopRules.Counts(playerNumber))
            {
                return "CoopRules says Player " + playerNumber + " does not count right now (Single Player Mode is on for the other player, " +
                       "or Player 2 is switched off in LocalCoopManager).";
            }

            return "shown normally";
        }
    }

    // The bar's own rectangle is its size, unless a Footprint is set. (Leave Footprint empty: if the bar's rectangle turns out to be
    // stretched across the screen, the pictures and text drawn inside the bar are measured instead.)
    protected override RectTransform DefaultFootprint { get { return healthbar != null ? healthbar.transform as RectTransform : null; } }
    protected override Transform MeasureRoot { get { return healthbar != null ? healthbar.transform : transform; } }

    // The layout moves the bar itself when it is not inside this object, so it can't be left behind.
    public override Transform SlotTransform
    {
        get { return (healthbar != null && !healthbar.transform.IsChildOf(transform)) ? healthbar.transform : transform; }
    }

    /// <summary>The binder for this player number, or null.</summary>
    public static CoopHealthbarBinder Get(int playerNumber)
    {
        return Get(HudGroup.Health, playerNumber) as CoopHealthbarBinder;
    }

    private float warnAt;
    private bool everShown;
    private bool warned;

    private void Awake()
    {
        warnAt = Time.unscaledTime + 3f;

        // The bar is meant to be a child of this object: find it by itself instead of needing it dragged in.
        if (healthbar == null) healthbar = GetComponentInChildren<Healthbar>(true);

        if (healthbar == null)
        {
            Debug.LogError("CoopHealthbarBinder on '" + name + "': the Healthbar slot is empty, so nothing can be shown. " +
                           "Drag the bar (a CHILD of this object) into it.", this);
            enabled = false;
            return;
        }

        // Hiding the bar would hide this script too if the bar is this object or one of its parents, and the bar would
        // then vanish for good. Refuse to hide it and say so, instead of silently disappearing.
        if (healthbar.gameObject == gameObject || transform.IsChildOf(healthbar.transform))
        {
            Debug.LogError("CoopHealthbarBinder on '" + name + "': the Healthbar is this object (or a parent of it). This object has to " +
                           "stay switched on while the bar is hidden, so the bar must be a CHILD. Make an empty parent, put this script " +
                           "on the parent and the bar under it. The bar is left as it is.", this);
            enabled = false;
            return;
        }

        // Hidden until the player exists, so the bar never runs without a health to read.
        healthbar.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (healthbar == null) return;   // destroyed at runtime

        CoopHealth player = CoopHealth.Get(playerNumber);
        bool show = player != null && CoopRules.Counts(playerNumber);

        if (show && healthbar.Health != player)
        {
            // A respawned player is a NEW object. Switch the bar off first so it re-reads the new health when it comes back on,
            // and connect BEFORE it is switched on: its Start reads the health straight away.
            if (healthbar.gameObject.activeSelf) healthbar.gameObject.SetActive(false);
            healthbar.Health = player;
        }

        if (healthbar.gameObject.activeSelf != show) healthbar.gameObject.SetActive(show);

        if (show) everShown = true;
        else if (!everShown && !warned && Time.unscaledTime >= warnAt) ExplainMissingBar(player);
    }

    // One message, after three seconds, naming the real reason the bar has not appeared (instead of guessing).
    private void ExplainMissingBar(CoopHealth player)
    {
        warned = true;

        // Player 2 hidden because two-player mode is off, or a single-player rule is on: that is the design, not a fault.
        if (player != null && !CoopRules.Counts(playerNumber)) return;
        if (player == null && playerNumber > 1 && !LocalCoop.Enabled) return;

        if (player == null)
        {
            Debug.LogWarning("CoopHealthbarBinder (Player " + playerNumber + "): no CoopHealth with Player Number " + playerNumber +
                             " is in the game. Check that the player prefab has CoopHealth (not the plain Health component), that the PlayerInputHub " +
                             "gave a character Player " + playerNumber + "'s slot (the console says which character got which slot)" +
                             (playerNumber > 1 ? ", and that Player 2 is switched on (LocalCoopManager)." : "."), this);
        }
    }
}
