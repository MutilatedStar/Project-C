using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ilumisoft.HealthSystem;
using Ilumisoft.HealthSystem.UI;

/// <summary>
/// Checks the open scene's co-op set-up and, on request, links up what is missing or wrong. Put this file in a folder called
/// Editor (Assets/Editor/CoopSceneSetup.cs). Then:
///
///   Tools > Co-op > Check Scene (changes nothing)   lists every problem it finds in the Console.
///   Tools > Co-op > Link Up Scene                   fixes them. Everything it does is one Undo step (Ctrl+Z), and it does not save
///                                                   the scene: look at the result, then save (Ctrl+S) if you are happy.
///
/// WHAT IT LINKS UP
///   - Removes the "Missing (Mono Script)" from the player prefab (the old controller that no longer exists).
///   - Gives Player 1 and Player 2 a CoopHealth (the health bars only look for CoopHealth with the right Player Number).
///   - Removes a CoopHealth that sits on something that is not a player (it is what made the console say "added as Player 3").
///   - Adds a PlayerInputHub if the scene has none, with Player 1 and Player 2 in its first two slots.
///   - Health bars: one binder and one bar per player (each bar is a child of its binder, and a bar is never shared).
///   - Inventory bars: one panel per player, on a UI object (not on the Canvas itself).
///   - Exactly one CoopHealthbarLayout and one CoopInventoryLayout.
/// It never deletes your player objects, cameras or Project B objects. It only reports things it does not understand.
/// Run "Check Scene" first if you want to see what it will do.
/// </summary>
public static class CoopSceneSetup
{
    private const string HealthbarPrefabPath = "Assets/Prefabs/Healthbar.prefab";

    private static StringBuilder log;
    private static bool apply;
    private static int found;

    [MenuItem("Tools/Co-op/Check Scene (changes nothing)")]
    private static void Check()
    {
        Run(false);
    }

    [MenuItem("Tools/Co-op/Link Up Scene")]
    private static void LinkUp()
    {
        Run(true);
    }

    // ------------------------------------------------------------ plumbing

    private static void Run(bool doIt)
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("Co-op set-up: stop Play mode first, then run this again.");
            return;
        }

        apply = doIt;
        found = 0;
        log = new StringBuilder();
        Scene scene = SceneManager.GetActiveScene();
        log.AppendLine("Co-op set-up (" + (doIt ? "LINK UP" : "CHECK ONLY") + ") for scene '" + scene.name + "'");

        if (doIt)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Link up co-op scene");
        }

        GameObject player1 = null;
        GameObject player2 = null;
        FindPlayers(out player1, out player2);

        FixPlayerPrefabs(player1, player2);
        FixPlayerHealth(player1, player2);
        FixInputHub(player1, player2);
        FixHud();
        Report();

        if (found == 0)
        {
            log.AppendLine("Nothing to fix here.");
        }
        else if (doIt)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            log.AppendLine("Done: " + found + " thing(s) linked up. Look at the Hierarchy, then save the scene (Ctrl+S). Ctrl+Z undoes all of it.");
        }
        else
        {
            log.AppendLine(found + " problem(s) found. Tools > Co-op > Link Up Scene fixes them.");
        }

        Debug.Log(log.ToString());
    }

    // Records a problem. Returns true when it should be fixed now.
    private static bool Act(string what)
    {
        found++;
        log.AppendLine((apply ? "  FIXED: " : "  PROBLEM: ") + what);
        return apply;
    }

    private static void Note(string line)
    {
        log.AppendLine("  note: " + line);
    }

    // ------------------------------------------------------------ the players

    private static void FindPlayers(out GameObject player1, out GameObject player2)
    {
        player1 = null;
        player2 = null;

        SplitScreenCamera rig = Object.FindFirstObjectByType<SplitScreenCamera>(FindObjectsInactive.Include);
        if (rig != null)
        {
            if (rig.player1 != null) player1 = rig.player1.gameObject;
            if (rig.player2 != null) player2 = rig.player2.gameObject;
        }

        if (player1 == null) player1 = GameObject.Find("PF Player 1");
        if (player2 == null) player2 = GameObject.Find("PF Player 2");

        log.AppendLine("Player 1: " + (player1 != null ? "'" + player1.name + "'" : "NOT FOUND (fill in the Player 1 slot on SplitScreenCamera)"));
        log.AppendLine("Player 2: " + (player2 != null ? "'" + player2.name + "'" : "NOT FOUND (fill in the Player 2 slot on SplitScreenCamera)"));
    }

    // A script that no longer exists, left on the player prefab. Fixed in the prefab itself, so every copy and variant is clean.
    private static void FixPlayerPrefabs(GameObject player1, GameObject player2)
    {
        string[] paths = new string[2];
        paths[0] = SourcePrefabPath(player1);
        paths[1] = SourcePrefabPath(player2);

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (string.IsNullOrEmpty(path) || (i == 1 && path == paths[0])) continue;

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;

            int missing = CountMissing(asset);
            if (missing == 0) continue;

            if (Act("the prefab '" + path + "' has " + missing + " missing script(s): removing them from the prefab"))
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform[] all = contents.GetComponentsInChildren<Transform>(true);
                    for (int t = 0; t < all.Length; t++) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(all[t].gameObject);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }
    }

    private static string SourcePrefabPath(GameObject instance)
    {
        if (instance == null) return null;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance);
        return source != null ? AssetDatabase.GetAssetPath(source) : null;
    }

    private static int CountMissing(GameObject root)
    {
        int count = 0;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(all[i].gameObject);
        return count;
    }

    private static void FixPlayerHealth(GameObject player1, GameObject player2)
    {
        GameObject[] players = { player1, player2 };
        for (int i = 0; i < players.Length; i++)
        {
            GameObject player = players[i];
            if (player == null) continue;

            if (player.GetComponent<CoopHealth>() == null)
            {
                if (Act("Player " + (i + 1) + " ('" + player.name + "') has no CoopHealth: adding one"))
                {
                    Undo.AddComponent<CoopHealth>(player);
                }
            }

            // A plain Health next to it would fight with it.
            Health[] all = player.GetComponents<Health>();
            for (int h = 0; h < all.Length; h++)
            {
                if (all[h].GetType() == typeof(Health))
                {
                    Note("'" + player.name + "' also has a plain Health component. Remove it by hand: two health components fight each other.");
                }
            }
        }

        // A CoopHealth on something that is not a player makes the game invent "Player 3".
        CoopHealth[] every = Object.FindObjectsByType<CoopHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int c = 0; c < every.Length; c++)
        {
            CoopHealth health = every[c];
            if (health == null) continue;
            if (health.gameObject == player1 || health.gameObject == player2) continue;

            // Only plain scene objects: a prefab-based object (a foe) is not ours to touch.
            if (PrefabUtility.IsPartOfPrefabInstance(health.gameObject)) continue;

            GameObject owner = health.gameObject;
            if (Act("'" + owner.name + "' has a CoopHealth but is not a player: removing it (this is what caused \"added as Player 3\")"))
            {
                Undo.DestroyObjectImmediate(health);
                DestroyIfEmpty(owner);
            }
        }
    }

    // ------------------------------------------------------------ input hub

    private static void FixInputHub(GameObject player1, GameObject player2)
    {
        PlayerInputHub hub = Object.FindFirstObjectByType<PlayerInputHub>(FindObjectsInactive.Include);

        if (hub == null)
        {
            if (Act("the scene has no PlayerInputHub: adding one (it gives each character its Player Number)"))
            {
                GameObject holder = new GameObject("PlayerInputHub");
                Undo.RegisterCreatedObjectUndo(holder, "Add PlayerInputHub");
                hub = Undo.AddComponent<PlayerInputHub>(holder);

                // A freshly added component runs Reset itself; make sure the four slots exist either way.
                if (hub.players == null || hub.players.Length != PlayerInputSource.MaxPlayers || hub.players[0] == null)
                {
                    MethodInfo fill = typeof(PlayerInputHub).GetMethod("FillDefaults", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fill != null) fill.Invoke(hub, null);
                }
            }
        }

        if (hub == null || hub.players == null) return;

        GameObject[] players = { player1, player2 };
        for (int i = 0; i < players.Length && i < hub.players.Length; i++)
        {
            if (players[i] == null || hub.players[i] == null) continue;

            GameObject current = hub.players[i].character;
            if (current == players[i]) continue;

            if (current != null)
            {
                Note("PlayerInputHub: Player " + (i + 1) + " is set to '" + current.name + "', not '" + players[i].name + "'. Left as it is.");
                continue;
            }

            if (Act("PlayerInputHub: Player " + (i + 1) + "'s Character slot is empty: setting it to '" + players[i].name + "'"))
            {
                Undo.RecordObject(hub, "Set hub character");
                hub.players[i].character = players[i];
                EditorUtility.SetDirty(hub);
            }
        }
    }

    // ------------------------------------------------------------ HUD

    private static void FixHud()
    {
        Canvas canvas = FindHudCanvas();
        if (canvas == null)
        {
            log.AppendLine("HUD: no Screen Space - Overlay Canvas found, so the health and inventory bars were not looked at.");
            return;
        }
        log.AppendLine("HUD canvas: '" + canvas.name + "'");

        // Remember the look of the existing bars before anything is removed.
        CoopInventoryPanel inventoryLook = null;
        CoopInventoryPanel[] panels = Object.FindObjectsByType<CoopInventoryPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < panels.Length; i++)
        {
            if (inventoryLook == null || panels[i].gameObject != canvas.gameObject) inventoryLook = panels[i];
        }

        Vector3 barScale = new Vector3(300f, 150f, 1f);   // a 1 x 0.2 bar becomes 300 x 30 pixels
        bool scaleFound = false;
        CoopHealthbarBinder[] oldBinders = Object.FindObjectsByType<CoopHealthbarBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < oldBinders.Length && !scaleFound; i++)
        {
            if (oldBinders[i].healthbar != null)
            {
                barScale = oldBinders[i].healthbar.transform.localScale;
                scaleFound = true;
            }
        }

        for (int player = 1; player <= 2; player++)
        {
            FixHealthBar(canvas, player, barScale);
            FixInventoryBar(canvas, player, inventoryLook);
        }

        EnsureSingleLayout<CoopHealthbarLayout>(canvas, "CoopHealthbarLayout");
        EnsureSingleLayout<CoopInventoryLayout>(canvas, "CoopInventoryLayout");
    }

    private static Canvas FindHudCanvas()
    {
        Component[] hints = Object.FindObjectsByType<CoopHudPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < hints.Length; i++)
        {
            Canvas c = hints[i].GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }

        CoopHudLayout[] layouts = Object.FindObjectsByType<CoopHudLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < layouts.Length; i++)
        {
            Canvas c = layouts[i].GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }

        Canvas[] all = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].isRootCanvas && all[i].renderMode == RenderMode.ScreenSpaceOverlay) return all[i];
        }
        return null;
    }

    // ------------------------------------------------------------ health bars

    private static void FixHealthBar(Canvas canvas, int player, Vector3 barScale)
    {
        CoopHealthbarBinder[] all = Object.FindObjectsByType<CoopHealthbarBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // The binder that belongs to this player and lives on its own object (not on the Canvas itself).
        CoopHealthbarBinder binder = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].playerNumber == player && all[i].gameObject != canvas.gameObject) { binder = all[i]; break; }
        }

        if (binder == null)
        {
            if (!Act("Player " + player + " has no health bar binder on its own object: making one")) return;

            GameObject holder = NewUiObject("Health Bar P" + player, canvas.transform);
            binder = Undo.AddComponent<CoopHealthbarBinder>(holder);
            binder.playerNumber = player;
        }

        // Stray binders for this player (for example one sitting on the Canvas itself) would fight with it.
        for (int i = 0; i < all.Length; i++)
        {
            CoopHealthbarBinder other = all[i];
            if (other == binder || other.playerNumber != player) continue;

            if (Act("an extra health bar binder for Player " + player + " on '" + other.gameObject.name + "': removing it"))
            {
                GameObject owner = other.gameObject;
                Undo.DestroyObjectImmediate(other);
                if (owner != canvas.gameObject) DestroyIfEmpty(owner);
            }
        }

        // The bar: its own, and a child of the binder.
        Healthbar bar = binder.healthbar;
        bool shared = false;
        if (bar != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != binder && all[i].healthbar == bar && all[i].playerNumber != player) shared = true;
            }
        }

        // Player 1 keeps the bar that is already in the scene. Player 2 gets a new one if it was sharing.
        bool needNew = bar == null || (shared && player > 1);
        if (needNew)
        {
            if (Act("Player " + player + " has " + (bar == null ? "no health bar" : "the same health bar as another player") + ": adding its own"))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HealthbarPrefabPath);
                if (prefab == null)
                {
                    Note("could not find '" + HealthbarPrefabPath + "'. Put a Healthbar prefab under the binder by hand.");
                    return;
                }

                GameObject made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, binder.transform);
                Undo.RegisterCreatedObjectUndo(made, "Add health bar");
                made.transform.localPosition = Vector3.zero;
                made.transform.localRotation = Quaternion.identity;
                made.transform.localScale = barScale;
                bar = made.GetComponent<Healthbar>();
                if (bar == null) bar = made.GetComponentInChildren<Healthbar>(true);
                Undo.RecordObject(binder, "Link health bar");
                binder.healthbar = bar;
            }
            else
            {
                return;
            }
        }
        else if (bar != null && (!bar.transform.IsChildOf(binder.transform) || bar.transform == binder.transform))
        {
            if (Act("Player " + player + "'s health bar is not inside its binder: moving it in"))
            {
                Undo.SetTransformParent(bar.transform, binder.transform, "Move health bar");
                bar.transform.localPosition = Vector3.zero;
            }
        }

        if (bar == null) return;

        // A HUD bar must not turn itself to face the camera (that is for bars floating above characters).
        SerializedObject so = new SerializedObject(bar);
        SerializedProperty align = so.FindProperty("alignWithCamera");
        if (align != null && align.boolValue)
        {
            if (Act("Player " + player + "'s health bar has Align With Camera on: switching it off (it is a screen HUD bar)"))
            {
                align.boolValue = false;
                so.ApplyModifiedProperties();
            }
        }

        // The footprint: the rectangle inside the bar that is as big as the bar looks.
        Canvas inner = bar.GetComponentInChildren<Canvas>(true);
        RectTransform innerRect = inner != null ? inner.transform as RectTransform : null;
        if (innerRect != null && binder.footprint != innerRect)
        {
            if (Act("Player " + player + "'s health bar Footprint: pointing it at the bar's own rectangle ('" + innerRect.name + "')"))
            {
                Undo.RecordObject(binder, "Set footprint");
                binder.footprint = innerRect;
                EditorUtility.SetDirty(binder);
            }
        }
    }

    // ------------------------------------------------------------ inventory bars

    private static void FixInventoryBar(Canvas canvas, int player, CoopInventoryPanel look)
    {
        CoopInventoryPanel[] all = Object.FindObjectsByType<CoopInventoryPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // A panel for this player on a real UI object (it needs a Rect Transform), and not on the Canvas itself.
        CoopInventoryPanel panel = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].playerNumber == player && all[i].gameObject != canvas.gameObject && all[i].transform is RectTransform) { panel = all[i]; break; }
        }

        if (panel == null)
        {
            if (!Act("Player " + player + " has no inventory bar on a UI object: making one")) return;

            GameObject holder = NewUiObject("Inventory Bar P" + player, canvas.transform);
            panel = Undo.AddComponent<CoopInventoryPanel>(holder);
            panel.playerNumber = player;

            if (look != null)
            {
                panel.slotSize = look.slotSize;
                panel.spacing = look.spacing;
                panel.showLockedSlots = look.showLockedSlots;
                panel.slotColor = look.slotColor;
                panel.selectedColor = look.selectedColor;
                panel.lockedColor = look.lockedColor;
                panel.chipSize = look.chipSize;
                panel.font = look.font;
                panel.chipFontSize = look.chipFontSize;
            }
        }

        if (panel.footprint != null)
        {
            if (Act("Player " + player + "'s inventory bar had a Footprint set (it measures itself): clearing it"))
            {
                Undo.RecordObject(panel, "Clear footprint");
                panel.footprint = null;
                EditorUtility.SetDirty(panel);
            }
        }

        // Extra panels for this player: on the Canvas itself, or on a non-UI object (the script switches itself off there).
        for (int i = 0; i < all.Length; i++)
        {
            CoopInventoryPanel other = all[i];
            if (other == panel || other.playerNumber != player) continue;

            if (Act("an extra inventory bar for Player " + player + " on '" + other.gameObject.name + "': removing it"))
            {
                GameObject owner = other.gameObject;
                Undo.DestroyObjectImmediate(other);
                if (owner != canvas.gameObject) DestroyIfEmpty(owner);
            }
        }
    }

    // ------------------------------------------------------------ layouts

    private static void EnsureSingleLayout<T>(Canvas canvas, string name) where T : CoopHudLayout
    {
        T[] all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (all.Length == 0)
        {
            if (Act("the scene has no " + name + ": adding one"))
            {
                GameObject holder = NewUiObject(name, canvas.transform);
                Undo.AddComponent<T>(holder);
            }
            return;
        }

        // Keep one: the one whose Player 2 corner is a right-hand corner (a sensible pair), otherwise the first.
        T keep = all[0];
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].player2Corner == CoopHudLayout.Corner.BottomRight || all[i].player2Corner == CoopHudLayout.Corner.TopRight)
            {
                keep = all[i];
                break;
            }
        }

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == keep) continue;

            if (Act("a second " + name + " on '" + all[i].gameObject.name + "': removing it (only one is allowed)"))
            {
                GameObject owner = all[i].gameObject;
                Undo.DestroyObjectImmediate(all[i]);
                if (owner != canvas.gameObject) DestroyIfEmpty(owner);
            }
        }
    }

    // ------------------------------------------------------------ report

    // Lists what is really in the scene (after any fixes), so a problem can be seen without hunting through the Hierarchy.
    private static void Report()
    {
        log.AppendLine("--- What is in the scene right now ---");

        PlayerInputHub hub = Object.FindFirstObjectByType<PlayerInputHub>(FindObjectsInactive.Include);
        log.AppendLine("PlayerInputHub: " + (hub != null ? "'" + PathOf(hub.transform) + "'" : "NONE"));

        CoopHealth[] healths = Object.FindObjectsByType<CoopHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        log.AppendLine("CoopHealth components: " + healths.Length);
        for (int i = 0; i < healths.Length; i++)
        {
            PlayerInputSource input = healths[i].GetComponent<PlayerInputSource>();
            log.AppendLine("   on '" + PathOf(healths[i].transform) + "'   Player Number: " + (input != null ? input.playerNumber.ToString() : "no PlayerInputSource"));
        }

        LocalCoopManager[] managers = Object.FindObjectsByType<LocalCoopManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            LocalCoopManager m = managers[i];
            string inside = (m.player2 != null && m.transform.IsChildOf(m.player2.transform)) ? "   <-- PROBLEM: the manager is on Player 2 or inside it" : "";
            log.AppendLine("LocalCoopManager on '" + PathOf(m.transform) + "'   Player 2 slot: " +
                           (m.player2 != null ? "'" + PathOf(m.player2.transform) + "'" : "EMPTY") + inside);
        }

        Healthbar[] bars = Object.FindObjectsByType<Healthbar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        log.AppendLine("Healthbar components: " + bars.Length);
        for (int i = 0; i < bars.Length; i++)
        {
            log.AppendLine("   '" + PathOf(bars[i].transform) + "'   " + (bars[i].gameObject.activeInHierarchy ? "active" : "hidden") +
                           "   Health: " + (bars[i].Health != null ? "'" + bars[i].Health.name + "'" : "none"));
        }

        CoopHealthbarBinder[] binders = Object.FindObjectsByType<CoopHealthbarBinder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        log.AppendLine("Health bar binders: " + binders.Length);
        for (int i = 0; i < binders.Length; i++)
        {
            log.AppendLine("   '" + PathOf(binders[i].transform) + "'   Player " + binders[i].playerNumber + "   bar: " +
                           (binders[i].healthbar != null ? "'" + PathOf(binders[i].healthbar.transform) + "'" : "EMPTY"));
        }

        CoopInventoryPanel[] panels = Object.FindObjectsByType<CoopInventoryPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        log.AppendLine("Inventory bars: " + panels.Length);
        for (int i = 0; i < panels.Length; i++)
        {
            log.AppendLine("   '" + PathOf(panels[i].transform) + "'   Player " + panels[i].playerNumber + "   " +
                           (panels[i].transform is RectTransform ? "UI object" : "NOT a UI object (needs to be inside the Canvas)"));
        }

        log.AppendLine("Layouts: " + Object.FindObjectsByType<CoopHealthbarLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length +
                       " health, " + Object.FindObjectsByType<CoopInventoryLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length + " inventory");
    }

    private static string PathOf(Transform t)
    {
        if (t == null) return "(none)";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ------------------------------------------------------------ helpers

    private static GameObject NewUiObject(string name, Transform parent)
    {
        GameObject made = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(made, "Create " + name);
        made.transform.SetParent(parent, false);
        return made;
    }

    // Deletes an object that has nothing left on it but its Transform and no children. Never touches a prefab instance.
    private static void DestroyIfEmpty(GameObject go)
    {
        if (go == null || PrefabUtility.IsPartOfPrefabInstance(go)) return;
        if (go.transform.childCount > 0) return;
        if (go.GetComponents<Component>().Length > 1) return;
        Undo.DestroyObjectImmediate(go);
    }
}
