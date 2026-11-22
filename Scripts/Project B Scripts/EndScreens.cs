using UnityEngine;
using UnityEngine.SceneManagement;
using Ilumisoft.HealthSystem;

/// <summary>
/// The two ways a run ends. Each one sends the player to its own scene:
///
///   LOSE  - the player's Health hits zero (HealthComponent.OnHealthEmpty)  ->  the "Failure Ending" scene.
///           This script finds the object tagged "Player", listens to its health, and loads the scene by itself.
///           Nothing else needs to call it.
///   WIN   - FloorManager calls ShowVictory() when the stairs are taken on the last floor  ->  the "Ending" scene.
///
/// Whichever ending happens FIRST wins, so dying on the same frame the final stairs are taken can never load both scenes.
///
/// Setup:
///   1. Put this script on an object in the crawler scene that stays active (an empty object is fine).
///   2. Type the exact names of the two ending scenes into the fields below.
///   3. Add the crawler scene AND both ending scenes to the Build Settings (File > Build Profiles / Build Settings).
///      A scene that is missing from the list, or spelled differently, makes Unity log a "couldn't be loaded" error.
/// </summary>
public class EndScreens : MonoBehaviour
{
    [Header("Ending scenes (exact names, must be in the Build Settings)")]
    [Tooltip("Loaded when the player finishes the last floor.")]
    [SerializeField] private string endingScene = "Ending";
    [Tooltip("Loaded when the player's health reaches zero.")]
    [SerializeField] private string failureEndingScene = "Failure Ending";

    /// <summary>True once an ending has been triggered.</summary>
    public bool HasEnded { get; private set; }

    private HealthComponent playerHealth;
    private bool warnedNoPlayer;
    private float searchStart;

    // ------------------------------------------------------------ setup

    private void Awake()
    {
        // Time.timeScale and FoeController.Frozen are global, so they survive a scene change. Reset them here so the crawler
        // never starts paused or with every foe frozen, however the player got back to it.
        Time.timeScale = 1f;
        FoeController.Frozen = false;
    }

    private void Start()
    {
        searchStart = Time.unscaledTime;
        TryHookPlayer();
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnHealthEmpty -= ShowDefeat;
    }

    // The player may be created a moment after the scene starts, so keep looking until its health is found.
    private void Update()
    {
        if (HasEnded || playerHealth != null) return;
        TryHookPlayer();
    }

    // Foes are variants of the player prefab, so they can carry the "Player" tag too, and FindWithTag returns whichever it
    // meets first. Prefer the tagged object that is NOT a foe; fall back to the first tagged object if there is none.
    private GameObject FindPlayer()
    {
        GameObject[] tagged = GameObject.FindGameObjectsWithTag("Player");
        for (int i = 0; i < tagged.Length; i++)
        {
            if (tagged[i].GetComponentInParent<FoeController>() == null) return tagged[i];
        }
        return tagged.Length > 0 ? tagged[0] : null;
    }

    private void TryHookPlayer()
    {
        GameObject player = FindPlayer();
        if (player != null)
        {
            HealthComponent hp = player.GetComponentInParent<HealthComponent>();
            if (hp == null) hp = player.GetComponentInChildren<HealthComponent>();
            if (hp != null)
            {
                playerHealth = hp;
                playerHealth.OnHealthEmpty += ShowDefeat;
                Debug.Log($"EndScreens: now watching '{player.name}' for the Failure Ending.", player);
                return;
            }
        }

        if (!warnedNoPlayer && Time.unscaledTime - searchStart > 3f)
        {
            warnedNoPlayer = true;
            Debug.LogWarning("EndScreens: no Health component found on the object tagged 'Player' after 3 seconds, so the Failure " +
                             "Ending can never load. Tag the player 'Player' and make sure it has a Health component.", this);
        }
    }

    // ------------------------------------------------------------ the two endings

    /// <summary>Loads the Ending scene. FloorManager calls this when the last floor is finished.</summary>
    public void ShowVictory() => Finish(endingScene);

    /// <summary>Loads the Failure Ending scene. Runs by itself when the player's health reaches zero.</summary>
    public void ShowDefeat() => Finish(failureEndingScene);

    private void Finish(string sceneName)
    {
        if (HasEnded) return;   // the first ending wins
        HasEnded = true;

        // Both are global and would carry into the next scene, leaving it paused with foes frozen.
        Time.timeScale = 1f;
        FoeController.Frozen = false;

        SceneManager.LoadScene(sceneName);
    }
}
