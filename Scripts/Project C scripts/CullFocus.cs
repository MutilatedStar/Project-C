using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A "keep this alive" point for SpriteDistanceCuller: everything within Radius of this object is never culled while the
/// point is live. It registers itself while it is enabled, so objects spawned from a prefab at runtime are picked up by
/// themselves. Two uses:
///
///   AN EXTRA PLAYER (Player 3, Player 4...)   Leave Radius at 0 (= the culler's Cull Distance) and Active When on Always.
///                                             Players 1 and 2 do not need this: the culler finds them through CoopHealth.
///
///   A BALL (or any prop that can press a plate)   Radius 2 or 3, Active When = While An Altar Is Waiting. The ball is then
///                                             never culled while any RoomAltar on the live floor is still untriggered, and
///                                             can be culled normally once every altar there is done.
///
/// (A RoomAltar itself needs nothing: the culler already protects every altar that is not triggered yet.)
/// </summary>
[DisallowMultipleComponent]
public class CullFocus : MonoBehaviour
{
    public enum ActiveWhen
    {
        Always,
        WhileAnAltarIsWaiting
    }

    /// <summary>Everything that is currently a keep-alive point.</summary>
    public static readonly List<CullFocus> All = new List<CullFocus>();

    [Tooltip("How far around this object things are kept alive (world units). 0 = the culler's Cull Distance, which is right for a player. " +
             "Use a small number such as 2 or 3 for a ball, so only the ball itself and what is right around it stays alive.")]
    [Min(0f)] public float radius = 0f;

    [Tooltip("Always: keep it alive for as long as this object exists. While An Altar Is Waiting: only while a RoomAltar on the live floor has not been triggered yet.")]
    public ActiveWhen activeWhen = ActiveWhen.Always;

    /// <summary>True while this point should keep things alive.</summary>
    public bool IsLive
    {
        get { return activeWhen == ActiveWhen.Always || SpriteDistanceCuller.AltarsWaiting; }
    }

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
