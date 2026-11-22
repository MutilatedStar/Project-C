using UnityEngine;

/// <summary>
/// Places the two score panels (CoopScorePanel): Free, Mirror or Auto Snap, exactly like the health bars and the inventory
/// bars and with the same options, but set separately, so the score can sit somewhere different from both. Add ONE of these
/// anywhere in the scene (the HUD object is a good home). Everything it does, and all its settings, are described on
/// CoopHudLayout, which it is built on.
/// The health bars use the top corners and the inventory bars the bottom ones, so choose where the score goes in the
/// Inspector (Player 1 / Player 2 corner) if it lands on top of either.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // after SplitScreenCamera has moved the views this frame
public class CoopScoreLayout : CoopHudLayout
{
    protected override HudGroup Group { get { return HudGroup.Score; } }
    protected override string ButtonPrefix { get { return "Score"; } }
}
