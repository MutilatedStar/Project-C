using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    /// <summary>
    /// Finds a scene in the Build list by name, ignoring capitals, spaces and underscores.
    /// Returns its build index, or -1 (and logs every scene the game can see) if not found.
    /// </summary>
    public static int Find(string sceneName)
    {
        string wanted = Normalize(sceneName);
        int count = SceneManager.sceneCountInBuildSettings;

        StringBuilder list = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);

            if (Normalize(name) == wanted)
                return i;

            list.Append("[" + i + "] '" + name + "'  ");
        }

        Debug.LogError("[SceneLoader] Can't find a scene called '" + sceneName + "'. " +
            (count == 0
                ? "The Build list is EMPTY (nothing added, or everything is unchecked)."
                : "Scenes the game can see: " + list +
                  "If yours isn't in that list, it isn't added, it's unchecked, or it's in a different Build Profile (Unity 6). " +
                  "If it is there, fix the spelling in the Inspector."));
        return -1;
    }

    /// <summary>Loads the scene. Returns false (and logs why) if it can't be found.</summary>
    public static bool Load(string sceneName)
    {
        int index = Find(sceneName);
        if (index < 0) return false;

        Time.timeScale = 1f; // in case a menu paused the game
        SceneManager.LoadScene(index);
        return true;
    }

    static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace(" ", "").Replace("_", "").ToLowerInvariant();
    }
}