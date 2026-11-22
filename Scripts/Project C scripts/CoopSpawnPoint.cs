using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OPTIONAL. Marks the exact spot where a player comes back after they blow up (CoopDowned) in one room. Without any of these,
/// CoopDowned puts the player back where they first arrived in that room, and in room 0 back at their point of origin, so you only
/// need these when you want to choose the spot yourself.
///
/// SET UP: make an empty object where you want players to reappear and put this script on it.
///   Room       the room (floor) number this spot belongs to: the same number FloorController reports when the players arrive there.
///              Room 0 is left out on purpose: in room 0 players always return to their point of origin.
///   Player     0 = used by every player (Player 2 and up are placed a little further along Spread so nobody lands on the same spot).
///              1, 2, 3 or 4 = only that player uses this spot (it wins over a shared one).
/// </summary>
[DisallowMultipleComponent]
public class CoopSpawnPoint : MonoBehaviour
{
    [Tooltip("The room (floor) number this spot belongs to. Room 0 always uses the player's point of origin, so it isn't offered here.")]
    [Min(1)] public int room = 1;

    [Tooltip("0 = every player uses this spot. 1 to 4 = only that player (and it beats a shared spot in the same room).")]
    [Range(0, 4)] public int playerNumber = 0;

    [Tooltip("Only for a shared spot (Player = 0): Player 2 is placed this far from the spot, Player 3 twice as far, and so on.")]
    public Vector2 spread = new Vector2(0.8f, 0f);

    private static readonly List<CoopSpawnPoint> All = new List<CoopSpawnPoint>();

    private void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    /// <summary>The spot for this player in this room, or false if no spawn point was placed there.</summary>
    public static bool TryFind(int room, int playerNumber, out Vector3 position)
    {
        CoopSpawnPoint shared = null;

        for (int i = 0; i < All.Count; i++)
        {
            CoopSpawnPoint p = All[i];
            if (p == null || p.room != room) continue;

            if (p.playerNumber == playerNumber)
            {
                position = p.transform.position;
                return true;
            }

            if (p.playerNumber == 0 && shared == null) shared = p;
        }

        if (shared != null)
        {
            position = shared.transform.position + (Vector3)(shared.spread * Mathf.Max(0, playerNumber - 1));
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private void OnDrawGizmos()
    {
        Color c;
        switch (playerNumber)
        {
            case 1: c = new Color(0.25f, 0.55f, 1f, 1f); break;
            case 2: c = new Color(1f, 0.6f, 0.15f, 1f); break;
            case 3: c = new Color(0.35f, 0.9f, 0.4f, 1f); break;
            case 4: c = new Color(0.75f, 0.4f, 0.95f, 1f); break;
            default: c = new Color(1f, 0.85f, 0.25f, 1f); break;
        }

        Gizmos.color = c;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position + Vector3.left * 0.25f, transform.position + Vector3.right * 0.25f);
        Gizmos.DrawLine(transform.position + Vector3.down * 0.25f, transform.position + Vector3.up * 0.25f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.6f,
            "Room " + room + " spawn" + (playerNumber == 0 ? " (all players)" : " (Player " + playerNumber + ")"));
#endif
    }
}
