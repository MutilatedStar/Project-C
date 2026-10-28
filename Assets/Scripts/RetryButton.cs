using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The Retry button for the Failure screen. One press:
///   1. puts the score back to 0 and the player's health back to full (through GameReset),
///   2. makes sure time is running and the foes are un-frozen, so the next scene doesn't start frozen,
///   3. loads the Main Menu scene.
///
/// Setup:
///   1. Put this script ON the Retry button (the object that has the Button component).
///      It hooks the click up itself, so don't also add an OnClick in the Inspector.
///      (If you would rather use OnClick, put the script on any object and point OnClick at RetryButton.Retry.)
///   2. The scene needs an EventSystem for the button to be clickable (UI > Button adds one).
///   3. The Main Menu scene must be in File > Build Profiles (Build Settings) > Scenes In Build.
///
/// Want Retry to restart the dungeon straight away instead of going to the menu?
/// Type the dungeon scene's name into "Scene To Load".
/// </summary>
public class RetryButton : MonoBehaviour
{
    [Tooltip("Exact name of the scene to load after the reset. It must be in Scenes In Build. " +
             "\"MainMenu\" and \"Main Menu\" are both tried if this name can't be loaded.")]
    [SerializeField] private string sceneToLoad = "MainMenu";

    private Button button;
    private bool loading;

    private void Awake()
    {
        button = GetComponent<Button>();

        if (button != null)
            button.onClick.AddListener(Retry);
        else
            Debug.LogWarning("RetryButton: there is no Button on this object. Put the script on your Retry button, " +
                             "or point a button's OnClick at RetryButton.Retry.", this);
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Retry);
    }

    /// <summary>Resets score and health, then loads the Main Menu. The button calls this for you.</summary>
    public void Retry()
    {
        if (loading) return;   // a double click can't run it twice

        string scene = FindScene();
        if (scene == null)
        {
            Debug.LogError("RetryButton: the scene '" + sceneToLoad + "' can't be loaded. Check the Scene To Load spelling " +
                           "and that the scene is in the Scenes In Build list (File > Build Profiles).", this);
            return;
        }

        loading = true;

        GameReset.ResetRun();

        // The Failure screen may have frozen the game; the next scene must not start that way.
        Time.timeScale = 1f;
        FoeController.Frozen = false;

        SceneManager.LoadScene(scene);
    }

    private string FindScene()
    {
        string[] candidates = { sceneToLoad, "MainMenu", "Main Menu" };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.IsNullOrEmpty(candidates[i]) || !Application.CanStreamedLevelBeLoaded(candidates[i])) continue;

            if (i > 0) Debug.LogWarning("RetryButton: '" + sceneToLoad + "' isn't in the build, so '" + candidates[i] + "' is used. Set Scene To Load to match.", this);
            return candidates[i];
        }
        return null;
    }
}
