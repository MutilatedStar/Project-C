using UnityEngine;

/// <summary>
/// Places the two inventory bars (CoopInventoryPanel): Free, Mirror or Auto Snap, exactly like the health bars and with
/// the same options, but set separately, so the two HUDs can sit in different places. Add ONE of these anywhere in the
/// scene. Everything it does, and all its settings, are described on CoopHudLayout, which it is built on.
/// New ones start with Auto Snap on the BOTTOM corners (the health bars take the top ones).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // after SplitScreenCamera has moved the views this frame
public class CoopInventoryLayout : CoopHudLayout
{
    protected override HudGroup Group { get { return HudGroup.Inventory; } }
    protected override string ButtonPrefix { get { return "Inventory"; } }

    // Runs when the component is first added in the editor (and on its Reset menu entry).
    private void Reset()
    {
        player1Corner = Corner.BottomLeft;
        player2Corner = Corner.BottomRight;
    }
}
