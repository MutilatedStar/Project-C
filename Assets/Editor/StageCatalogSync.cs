using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps Assets/Resources/StageCatalog.asset (what the main menu reads) in step with the Stage Definition objects in the stage scene.
///
///   AUTOMATIC   every time you SAVE a scene that contains Stage Definition objects, the catalog is rebuilt from them.
///   BY HAND     Tools > Stages > Rebuild Stage Catalog From Open Scene.
///
/// So the whole "add / remove a stage" workflow is: add or delete a Stage object in the Puzzle scene, then save.
/// </summary>
[InitializeOnLoad]
public static class StageCatalogSync
{
    private const string Folder = "Assets/Resources";
    private const string AssetPath = "Assets/Resources/StageCatalog.asset";

    static StageCatalogSync()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Rebuild(scene, false);
    }

    [MenuItem("Tools/Stages/Rebuild Stage Catalog From Open Scene")]
    public static void RebuildFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!Rebuild(scene, true))
        {
            EditorUtility.DisplayDialog("Stage Catalog",
                "The active scene ('" + scene.name + "') has no Stage Definition objects.\nOpen the Puzzle scene and try again.", "OK");
        }
    }

    private static bool Rebuild(Scene scene, bool log)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;

        List<StageDefinition> found = new List<StageDefinition>();
        StageDefinition[] all = Object.FindObjectsByType<StageDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.scene == scene) found.Add(all[i]);
        }
        if (found.Count == 0) return false;

        // menu order: Puzzle first, then Pvp; inside a mode by Order, then name
        found.Sort((a, b) =>
        {
            if (a.mode != b.mode) return a.mode.CompareTo(b.mode);
            if (a.order != b.order) return a.order.CompareTo(b.order);
            return string.CompareOrdinal(a.Title, b.Title);
        });

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Resources");

        StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(AssetPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<StageCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
        }

        catalog.stageScene = scene.name;
        catalog.stages.Clear();

        HashSet<string> usedIds = new HashSet<string>();
        for (int i = 0; i < found.Count; i++)
        {
            StageDefinition d = found[i];
            string id = d.Id;
            if (!usedIds.Add(id))
            {
                Debug.LogWarning("Stage Catalog: two stages share the id '" + id + "' (" + d.name + "). Give one of them a different Stage Id. The duplicate was skipped.", d);
                continue;
            }

            StageCatalog.Entry e = new StageCatalog.Entry();
            e.id = id;
            e.displayName = d.Title;
            e.mode = d.mode;
            e.order = d.order;
            e.thumbnail = d.thumbnail;
            e.description = d.description;
            catalog.stages.Add(e);
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        if (log) Debug.Log("Stage Catalog: " + catalog.stages.Count + " stages written to " + AssetPath + " (scene '" + scene.name + "').");
        return true;
    }
}
