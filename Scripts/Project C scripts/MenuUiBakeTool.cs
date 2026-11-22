using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the menu screens into the saved scene as real objects, so you can see and edit them in the Scene view.
///
///   AUTOMATIC   The first time a scene with a Main Menu Controller or a Level Select Controller is opened (and its UI isn't there yet),
///               the UI is built into the scene. The Console says so; press Ctrl+S to save the scene. It happens once per scene.
///   BY HAND     The "Build / Rebuild Menu UI" buttons on those two components (or Tools > Stages > Menu UI) build it again from scratch.
///               That throws away your changes to the UI, so it asks first. Ctrl+Z undoes it.
///
/// It also makes the three small sprite assets the UI needs (a rounded rectangle and two side glows) in Assets/Menu UI, because a sprite
/// that is drawn in code can't be saved in a scene. You can replace those pictures with your own art later.
///
/// And it adds the LevelSelect scene to File > Build Profiles (Build Settings) if it is missing, since the menu can't open a scene that
/// isn't in the build.
/// </summary>
[InitializeOnLoad]
public static class MenuUiBakeTool
{
    private const string AssetFolder = "Assets/Menu UI";
    private const string RoundedPath = AssetFolder + "/MenuRounded.png";
    private const string GlowLeftPath = AssetFolder + "/MenuGlowLeft.png";
    private const string GlowRightPath = AssetFolder + "/MenuGlowRight.png";
    private const string MenuRoot = "Tools/Stages/Menu UI/";

    static MenuUiBakeTool()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.delayCall += AutoBuild;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += AutoBuild;
    }

    // ------------------------------------------------------------ automatic, once per scene

    private static void AutoBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoBuild;   // the editor is busy importing: try again in a moment
            return;
        }

        MainMenuController[] menus = Object.FindObjectsByType<MainMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < menus.Length; i++)
        {
            if (menus[i] == null || !IsEditableScene(menus[i].gameObject.scene)) continue;
            if (!menus[i].UiBuilt && !menus[i].HasUi) BuildMainMenuUi(menus[i], false);
        }

        LevelSelectController[] selects = Object.FindObjectsByType<LevelSelectController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < selects.Length; i++)
        {
            if (selects[i] == null || !IsEditableScene(selects[i].gameObject.scene)) continue;
            if (!selects[i].UiBuilt && !selects[i].HasUi) BuildLevelSelectUi(selects[i], false);
        }
    }

    private static bool IsEditableScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && !(scene.path != null && scene.path.StartsWith("Packages/"));
    }

    // ------------------------------------------------------------ building

    /// <summary>Builds the Main Menu screens into the controller's scene. confirmReplace asks before throwing away an existing UI.</summary>
    public static void BuildMainMenuUi(MainMenuController c, bool confirmReplace)
    {
        if (c == null) return;
        if (confirmReplace && c.HasUi && !AskToReplace("Main Menu")) return;

        Sprite rounded, glowLeft, glowRight;
        if (!EnsureSprites(out rounded, out glowLeft, out glowRight)) return;

        Undo.RecordObject(c, "Build Main Menu UI");
        if (c.MenuCanvas != null) Undo.DestroyObjectImmediate(c.MenuCanvas.gameObject);

        GameObject built = null;
        StageUi.RoundedOverride = rounded;
        try
        {
            MenuUiBuilder.Options options = c.BuildOptions(true);
            options.glowLeft = glowLeft;
            options.glowRight = glowRight;

            MenuUiBuilder.MainMenuRefs refs = MenuUiBuilder.BuildMainMenu(c, options);
            built = refs.canvas.gameObject;
            SceneManager.MoveGameObjectToScene(built, c.gameObject.scene);
            c.ApplyBuiltUi(refs);
        }
        finally
        {
            StageUi.RoundedOverride = null;
        }

        FinishBuild(c, built, "Build Main Menu UI");
        AddLevelSelectSceneToBuildSettings();
        Debug.Log("MenuUiBakeTool: the Main Menu UI is now in the scene '" + c.gameObject.scene.name + "' (under Menu Canvas). " +
                  "Press Ctrl+S to save it. Select Menu Canvas and press F in the Scene view to look at it.", c);
    }

    /// <summary>Builds the Level Select screens into the controller's scene. confirmReplace asks before throwing away an existing UI.</summary>
    public static void BuildLevelSelectUi(LevelSelectController c, bool confirmReplace)
    {
        if (c == null) return;
        if (confirmReplace && c.HasUi && !AskToReplace("Level Select")) return;

        Sprite rounded, glowLeft, glowRight;
        if (!EnsureSprites(out rounded, out glowLeft, out glowRight)) return;

        Undo.RecordObject(c, "Build Level Select UI");
        if (c.UiCanvas != null) Undo.DestroyObjectImmediate(c.UiCanvas.gameObject);

        GameObject built = null;
        StageUi.RoundedOverride = rounded;
        try
        {
            MenuUiBuilder.Options options = c.BuildOptions(true);
            options.glowLeft = glowLeft;
            options.glowRight = glowRight;

            MenuUiBuilder.LevelSelectRefs refs = MenuUiBuilder.BuildLevelSelect(c, options);
            built = refs.canvas.gameObject;
            SceneManager.MoveGameObjectToScene(built, c.gameObject.scene);
            c.ApplyBuiltUi(refs);
        }
        finally
        {
            StageUi.RoundedOverride = null;
        }

        FinishBuild(c, built, "Build Level Select UI");
        AddSceneToBuildSettings(c.gameObject.scene.path);
        Debug.Log("MenuUiBakeTool: the Level Select UI is now in the scene '" + c.gameObject.scene.name + "' (under Level Select Canvas). " +
                  "Press Ctrl+S to save it. Select Level Select Canvas and press F in the Scene view to look at it.", c);
    }

    private static bool AskToReplace(string what)
    {
        return EditorUtility.DisplayDialog("Rebuild " + what + " UI",
            "This deletes the current " + what + " screens (Canvas and everything under it) and builds the original layout again.\n\n" +
            "Any changes you made to them are lost. You can undo it with Ctrl+Z.",
            "Rebuild", "Cancel");
    }

    private static void FinishBuild(Object owner, GameObject built, string undoName)
    {
        if (built != null) Undo.RegisterCreatedObjectUndo(built, undoName);
        EditorUtility.SetDirty(owner);
        MonoBehaviour behaviour = owner as MonoBehaviour;
        if (behaviour != null) EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
    }

    // ------------------------------------------------------------ preview helpers (used by the Inspector buttons)

    /// <summary>Switches one page on and the others off, so only the page you are editing is in the way in the Scene view.</summary>
    public static void ShowOnly(GameObject show, params GameObject[] others)
    {
        for (int i = 0; i < others.Length; i++)
        {
            GameObject other = others[i];
            if (other == null || other == show || !other.activeSelf) continue;
            Undo.RecordObject(other, "Show page");
            other.SetActive(false);
        }

        if (show != null && !show.activeSelf)
        {
            Undo.RecordObject(show, "Show page");
            show.SetActive(true);
        }

        if (show != null) EditorSceneManager.MarkSceneDirty(show.scene);
    }

    public static void SelectAndFrame(Canvas canvas)
    {
        if (canvas == null) return;
        Selection.activeGameObject = canvas.gameObject;

        SceneView view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            view.in2DMode = true;
            view.FrameSelected();
        }
    }

    // ------------------------------------------------------------ sprite assets

    private static bool EnsureSprites(out Sprite rounded, out Sprite glowLeft, out Sprite glowRight)
    {
        float r = StageUi.RoundedRadius;
        rounded = EnsureSprite(RoundedPath, StageUi.MakeRoundedTexture, new Vector4(r, r, r, r));
        glowLeft = EnsureSprite(GlowLeftPath, MakeGlowLeft, Vector4.zero);
        glowRight = EnsureSprite(GlowRightPath, MakeGlowRight, Vector4.zero);

        if (rounded == null || glowLeft == null || glowRight == null)
        {
            Debug.LogError("MenuUiBakeTool: couldn't create the menu sprites in '" + AssetFolder + "'. Check the Console for import errors above this one.");
            return false;
        }
        return true;
    }

    private static Texture2D MakeGlowLeft() { return StageUi.MakeFadeTexture(true); }
    private static Texture2D MakeGlowRight() { return StageUi.MakeFadeTexture(false); }

    // Loads the sprite at assetPath; if there is none yet, writes the texture as a PNG there and imports it as a sprite.
    private static Sprite EnsureSprite(string assetPath, System.Func<Texture2D> makeTexture, Vector4 border)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (existing != null) return existing;

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

        Texture2D texture = makeTexture();
        try
        {
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null) return null;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.spriteBorder = border;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Full Rect, and the border / pixels-per-unit again through the settings object so nothing set above is overwritten by it.
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteBorder = border;
        settings.spritePixelsPerUnit = 100f;
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // ------------------------------------------------------------ Build Settings

    // The main menu can't open the Level Select scene unless it is in the build, so a scene called LevelSelect / Level Select anywhere in the project is added.
    private static void AddLevelSelectSceneToBuildSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:Scene");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.StartsWith("Assets/")) continue;

            string key = Path.GetFileNameWithoutExtension(path).Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            if (key == "levelselect") AddSceneToBuildSettings(path);
        }
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return;

        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].path == scenePath) return;   // already listed: whatever you chose for it stays
        }

        List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(current);
        list.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("MenuUiBakeTool: added '" + scenePath + "' to the scene list in File > Build Profiles (Build Settings), so the menus can open it.");
    }

    // ------------------------------------------------------------ Tools menu

    [MenuItem(MenuRoot + "Build Main Menu UI In Open Scene")]
    private static void MenuBuildMain()
    {
        MainMenuController c = Object.FindFirstObjectByType<MainMenuController>();
        if (c == null)
        {
            EditorUtility.DisplayDialog("Main Menu UI", "There is no Main Menu Controller in the open scene. Open the Main Menu scene first.", "OK");
            return;
        }
        BuildMainMenuUi(c, true);
    }

    [MenuItem(MenuRoot + "Build Level Select UI In Open Scene")]
    private static void MenuBuildLevelSelect()
    {
        LevelSelectController c = Object.FindFirstObjectByType<LevelSelectController>();
        if (c == null)
        {
            EditorUtility.DisplayDialog("Level Select UI", "There is no Level Select Controller in the open scene. Open the LevelSelect scene first.", "OK");
            return;
        }
        BuildLevelSelectUi(c, true);
    }

    [MenuItem(MenuRoot + "Add Level Select Scene To Build Settings")]
    private static void MenuAddToBuild()
    {
        AddLevelSelectSceneToBuildSettings();
    }
}

/// <summary>Inspector for Main Menu Controller: the build button and one-click page switching for editing.</summary>
[CustomEditor(typeof(MainMenuController))]
public class MainMenuControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        MainMenuController c = (MainMenuController)target;

        if (!c.HasUi)
        {
            EditorGUILayout.HelpBox("The menu UI isn't in this scene yet. Press 'Build / Rebuild Menu UI' (it is also built for you the first time the scene is opened).", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("The menu is real objects under 'Menu Canvas': select them in the Hierarchy and edit them in the Scene view (2D mode, then F to frame). " +
                                    "Only one page is switched on at a time while you edit; use the buttons below to pick the page you are working on. " +
                                    "Button actions are in each button's OnClick list.", MessageType.Info);
        }

        bool rebuild = false;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show Main Page")) MenuUiBakeTool.ShowOnly(c.MainPageObject, c.DifficultyPageObject);
                if (GUILayout.Button("Show Difficulty Page")) MenuUiBakeTool.ShowOnly(c.DifficultyPageObject, c.MainPageObject);
            }
            if (GUILayout.Button("Select Menu Canvas")) MenuUiBakeTool.SelectAndFrame(c.MenuCanvas);
            if (GUILayout.Button("Build / Rebuild Menu UI")) rebuild = true;
        }

        if (rebuild)
        {
            MenuUiBakeTool.BuildMainMenuUi(c, true);
            GUIUtility.ExitGUI();   // the objects the Inspector was drawing may have just been replaced
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}

/// <summary>Inspector for Level Select Controller: the build button and one-click page switching for editing.</summary>
[CustomEditor(typeof(LevelSelectController))]
public class LevelSelectControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LevelSelectController c = (LevelSelectController)target;

        if (!c.HasUi)
        {
            EditorGUILayout.HelpBox("The level select UI isn't in this scene yet. Press 'Build / Rebuild Level Select UI' (it is also built for you the first time the scene is opened).", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox("The screens are real objects under 'Level Select Canvas': select them in the Hierarchy and edit them in the Scene view (2D mode, then F to frame). " +
                                    "The single card under Viewport > Content is the template every stage card is copied from. " +
                                    "Only one page is switched on at a time while you edit; use the buttons below to pick the page you are working on.", MessageType.Info);
        }

        bool rebuild = false;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show Level Select Page")) MenuUiBakeTool.ShowOnly(c.LevelSelectPageObject, c.PvpPageObject);
                if (GUILayout.Button("Show PVP Page")) MenuUiBakeTool.ShowOnly(c.PvpPageObject, c.LevelSelectPageObject);
            }
            if (GUILayout.Button("Select Level Select Canvas")) MenuUiBakeTool.SelectAndFrame(c.UiCanvas);
            if (GUILayout.Button("Build / Rebuild Level Select UI")) rebuild = true;
        }

        if (rebuild)
        {
            MenuUiBakeTool.BuildLevelSelectUi(c, true);
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
