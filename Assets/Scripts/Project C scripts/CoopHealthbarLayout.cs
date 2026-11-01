using UnityEngine;

/// <summary>
/// Places the two health bars: Free, Mirror or Auto Snap. Add ONE of these anywhere in the scene (the HUD object is a
/// good home). Everything it does, and all its settings, are described on CoopHudLayout, which it is built on.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]   // after SplitScreenCamera has moved the views this frame
public class CoopHealthbarLayout : CoopHudLayout
{
    protected override HudGroup Group { get { return HudGroup.Health; } }
    protected override string ButtonPrefix { get { return "Health Bars"; } }
}
