#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the rank settings easy to find and edit.
///
///   AUTOMATIC   The first time the project loads without Assets/Resources/RankSettings.asset, it is created with the default targets.
///   Tools > Stages > Rank Settings            selects the asset in the Project window and shows it in the Inspector: set your times, scores and rules there.
///   Tools > Stages > Clear Saved Ranks        forgets every saved best rank, score, time and try (what the Level Select cards and the screens show).
/// </summary>
[InitializeOnLoad]
public static class RankSettingsTool
{
    private const string Folder = "Assets/Resources";
    private const string AssetPath = Folder + "/RankSettings.asset";
    private const string MenuRoot = "Tools/Stages/";

    static RankSettingsTool()
    {
        EditorApplication.delayCall += EnsureAsset;
    }

    private static void EnsureAsset()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (AssetDatabase.LoadAssetAtPath<RankSettings>(AssetPath) != null) return;

        // It may already exist somewhere else in a Resources folder: then that one is used and nothing is made.
        if (Resources.Load<RankSettings>(RankSettings.ResourceName) != null) return;

        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
        }

        RankSettings asset = ScriptableObject.CreateInstance<RankSettings>();
        AssetDatabase.CreateAsset(asset, AssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("Rank Settings: created " + AssetPath + " with the default targets. Open Tools > Stages > Rank Settings to set your own times and scores.");
    }

    [MenuItem(MenuRoot + "Rank Settings")]
    private static void SelectAsset()
    {
        EnsureAsset();
        RankSettings asset = AssetDatabase.LoadAssetAtPath<RankSettings>(AssetPath);
        if (asset == null) asset = Resources.Load<RankSettings>(RankSettings.ResourceName);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Rank Settings", "The asset couldn't be created or found. Make it by hand: right-click in the Project window > Create > Stages > Rank Settings, and put it in a folder called Resources.", "OK");
            return;
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    [MenuItem(MenuRoot + "Clear Saved Ranks")]
    private static void ClearSaved()
    {
        if (!EditorUtility.DisplayDialog("Clear Saved Ranks",
                "This forgets every saved best rank, score, time and try on THIS computer (the Level Select cards go back to 'not cleared'). It can't be undone.",
                "Clear", "Cancel"))
        {
            return;
        }

        LevelRecords.ClearAll();
        Debug.Log("Rank Settings: every saved rank was cleared.");
    }
}
#endif
