using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on anything SpriteDistanceCuller should keep sprites alive around: a 3rd or 4th player, a boss, a camera
/// target, anything. It registers itself while it is enabled and leaves the list when it is switched off or destroyed,
/// so players spawned from a prefab at runtime are picked up by themselves.
///
/// Players 1 and 2 do not need it (the culler finds them through CoopHealth, the same way SplitScreenCamera does).
/// </summary>
[DisallowMultipleComponent]
public class CullFocus : MonoBehaviour
{
    /// <summary>Everything that is currently a culling centre.</summary>
    public static readonly List<CullFocus> All = new List<CullFocus>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }
}
