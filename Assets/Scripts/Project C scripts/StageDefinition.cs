using System.Collections.Generic;
using UnityEngine;

/// <summary>The two kinds of stage. Puzzle = Level Select (PVP off). Pvp = the PVP menu (PVP on).</summary>
public enum StageMode
{
    Puzzle = 0,
    Pvp = 1
}

/// <summary>
/// ONE STAGE (one room layout) in the Puzzle scene. Put this on an empty object, give it a name, pick a Mode, and place its two
/// spawn points. That is the whole job: the menu list, the level select cards and the player placement are all built from these.
///
///   ADD A STAGE     empty object > Add Component > Stage Definition > right-click the component header >
///                   "Create Spawn Points" > move the two spawn markers into the room > save the scene.
///                   (Or just duplicate an existing Stage object and move it.)
///   REMOVE A STAGE  delete the object, save the scene.
///   RE-ORDER        change Order (lowest first inside its mode).
///   MOVE A START    move the "P1 Spawn" / "P2 Spawn" child objects (blue and orange circles in the Scene view).
///
/// Saving the scene rebuilds Assets/Resources/StageCatalog.asset, which is what the main menu reads.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Stages/Stage Definition")]
public class StageDefinition : MonoBehaviour
{
    public static readonly List<StageDefinition> All = new List<StageDefinition>();

    [Tooltip("Unique id. Leave EMPTY to use the object's name (spaces become dashes).")]
    public string stageId = "";

    [Tooltip("The name shown on the card and the banner. Leave EMPTY to use the object's name.")]
    public string displayName = "";

    [Tooltip("Puzzle = shows in Level Select, PVP off.   Pvp = shows in the PVP menu, PVP on (swords hurt the other player).")]
    public StageMode mode = StageMode.Puzzle;

    [Tooltip("Position in its list (lowest first).")]
    public int order = 0;

    [Tooltip("Picture on the card. Optional: a stage with no picture shows its number.")]
    public Sprite thumbnail;

    [Tooltip("Optional short line under the name.")]
    [TextArea(1, 3)] public string description = "";

    [Header("Where the players start")]
    public Transform player1Spawn;
    public Transform player2Spawn;

    public string Id { get { return string.IsNullOrEmpty(stageId) ? MakeId(name) : stageId; } }
    public string Title { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }

    public static string MakeId(string text)
    {
        if (string.IsNullOrEmpty(text)) return "stage";
        return text.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    public static StageDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Count; i++)
        {
            if (All[i] != null && All[i].Id == id) return All[i];
        }
        return null;
    }

    /// <summary>Where this player starts. Players 3 and 4 line up beside Player 1.</summary>
    public Vector3 SpawnFor(int playerNumber)
    {
        Transform t = playerNumber == 2 ? player2Spawn : player1Spawn;
        if (t != null && playerNumber <= 2) return t.position;

        Vector3 basePoint = player1Spawn != null ? player1Spawn.position : transform.position;
        return basePoint + new Vector3(0.5f * Mathf.Max(0, playerNumber - 1), 0f, 0f);
    }

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

    [ContextMenu("Create Spawn Points")]
    private void CreateSpawnPoints()
    {
        if (player1Spawn == null) player1Spawn = MakeChild("P1 Spawn", new Vector3(-0.5f, 0f, 0f));
        if (player2Spawn == null) player2Spawn = MakeChild("P2 Spawn", new Vector3(0.5f, 0f, 0f));
    }

    private Transform MakeChild(string childName, Vector3 localPosition)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = mode == StageMode.Pvp ? new Color(1f, 0.35f, 0.3f, 1f) : new Color(0.7f, 0.5f, 1f, 1f);
        Gizmos.DrawWireSphere(transform.position, 0.25f);

        if (player1Spawn != null)
        {
            Gizmos.color = new Color(0.30f, 0.60f, 1.00f, 1f);
            Gizmos.DrawWireSphere(player1Spawn.position, 0.3f);
        }
        if (player2Spawn != null)
        {
            Gizmos.color = new Color(1.00f, 0.62f, 0.20f, 1f);
            Gizmos.DrawWireSphere(player2Spawn.position, 0.3f);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, Title + (mode == StageMode.Pvp ? "  (PVP)" : "  (Puzzle)"));
#endif
    }
}
