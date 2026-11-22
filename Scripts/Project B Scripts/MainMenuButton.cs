using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class MainMenuButton : MonoBehaviour
{
    [Tooltip("Exact name of the main menu scene (must be in Build Settings).")]
    [SerializeField] string mainMenuScene = "Main Menu";

    [Header("UI cleanup before leaving")]
    [Tooltip("ON: automatically destroys every Canvas that survives scene loads (DontDestroyOnLoad). No dragging needed.")]
    [SerializeField] bool destroyPersistentCanvases = true;
    [Tooltip("Extra things to hide (set inactive) before leaving.")]
    [SerializeField] GameObject[] hideOnLeave;
    [Tooltip("Extra things to destroy before leaving.")]
    [SerializeField] GameObject[] destroyOnLeave;

    [Header("Reset")]
    [Tooltip("ON: forget the culprit and the accused suspect, so the next game starts fresh.")]
    [SerializeField] bool resetGame = false;

    [Header("Behavior")]
    [Tooltip("ON: the script hooks itself to the Button. Turn OFF only if you wire MainMenuButton > Go() in the Button's OnClick list yourself, or it fires twice.")]
    [SerializeField] bool autoWireClick = true;
    [SerializeField] bool debugLogs = true;

    Button button;

    void Awake()
    {
        button = GetComponent<Button>();
    }

    void OnEnable()
    {
        if (autoWireClick)
        {
            button.onClick.RemoveListener(Go); // re-hook every time, in case something cleared it
            button.onClick.AddListener(Go);
        }
    }

    void Log(string message)
    {
        if (debugLogs) Debug.Log("[MainMenuButton] " + message, this);
    }

    public void Go()
    {
        Log("Clicked in scene '" + SceneManager.GetActiveScene().name + "'");

        // Check the scene first so nothing is hidden or destroyed if the load can't happen.
        if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
        {
            Debug.LogError("[MainMenuButton] Scene '" + mainMenuScene + "' isn't in Build Settings (or the name is misspelled).", this);
            return;
        }

        Time.timeScale = 1f; // in case the menu paused the game

        foreach (GameObject go in hideOnLeave)
        {
            if (go == null) continue;
            go.SetActive(false);
            Log("Hid " + go.name);
        }

        HashSet<GameObject> toDestroy = new HashSet<GameObject>();

        foreach (GameObject go in destroyOnLeave)
            if (go != null) toDestroy.Add(go);

        if (destroyPersistentCanvases)
        {
            foreach (Canvas canvas in FindObjectsOfType<Canvas>(true))
            {
                if (canvas.gameObject.scene.name == "DontDestroyOnLoad")
                    toDestroy.Add(canvas.rootCanvas.gameObject);
            }
        }

        if (toDestroy.Count == 0)
            Log("Nothing to destroy: no persistent canvases found and the list is empty.");

        foreach (GameObject go in toDestroy)
        {
            Log("Destroying " + go.name);
            Destroy(go);
        }

        Log("Loading '" + mainMenuScene + "'");
        SceneManager.LoadScene(mainMenuScene);
    }
}