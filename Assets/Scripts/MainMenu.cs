using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The first menu of the game: one Play button. Clicking Play reveals three difficulty buttons (Easy / Normal / Hard).
/// Clicking a difficulty sets FoeController.CurrentDifficulty and loads the crawler scene.
///
/// FoeController.CurrentDifficulty is the ONE number everything reads:
///   - FoeController: foe speed, contact damage and score multipliers.
///   - FoeSpawner: spawn distance, alive cap, foe counts, spawn cooldown and random stat variation.
/// It is a static, so it survives the scene change and the crawler starts already on the chosen difficulty.
/// (If you press Play in the crawler scene directly, it stays on Normal, the default.)
///
/// Setup:
///   1. In the main menu scene make a Canvas with a Play button and the Easy, Normal and Hard buttons. They can sit inside a
///      parent object (the optional "Difficulty Panel" slot) or directly on the Canvas, it works either way.
///   2. Put this script on any object that stays active (the Canvas is fine), NOT on the Play button or the panel.
///   3. Drag the four buttons into the slots (and the panel if you have one). The buttons are wired by this script, so
///      you do not need to touch their On Click lists.
///   4. Both this scene and the crawler scene must be in the Build Settings.
///
/// What the player sees: at the start ONLY Play is visible. Easy, Normal, Hard and the panel are hidden. Clicking Play
/// hides Play and shows the panel and all three difficulty buttons.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [Tooltip("OPTIONAL. A parent object (background, title...) that holds the three difficulty buttons. Hidden until Play is clicked. " +
             "Leave empty if the buttons are not inside a parent. The three buttons below are hidden and shown on their own either way.")]
    [SerializeField] private GameObject difficultyPanel;
    [SerializeField] private Button easyButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button hardButton;

    [Header("Behaviour")]
    [Tooltip("Exact name of the crawler scene (must be in the Build Settings).")]
    [SerializeField] private string crawlerScene = "Crawler";

    private void Awake()
    {
        // Coming back from a win or a loss: make sure nothing is left paused or frozen from the crawler.
        Time.timeScale = 1f;
        FoeController.Frozen = false;

        // At the start only Play is visible.
        SetDifficultyVisible(false);
        if (playButton != null) playButton.gameObject.SetActive(true);

        if (playButton != null) playButton.onClick.AddListener(ShowDifficulties);
        if (easyButton != null) easyButton.onClick.AddListener(() => StartGame(FoeController.Difficulty.Easy));
        if (normalButton != null) normalButton.onClick.AddListener(() => StartGame(FoeController.Difficulty.Normal));
        if (hardButton != null) hardButton.onClick.AddListener(() => StartGame(FoeController.Difficulty.Hard));
    }

    /// <summary>Play was clicked: show Easy / Normal / Hard.</summary>
    public void ShowDifficulties()
    {
        // The panel goes first: a button inside an inactive parent stays invisible even when the button itself is active.
        SetDifficultyVisible(true);
        if (playButton != null) playButton.gameObject.SetActive(false);
    }

    // The panel and each of the three difficulty buttons are switched on their own, so it works whether or not the
    // buttons are children of the panel.
    private void SetDifficultyVisible(bool visible)
    {
        if (difficultyPanel != null) difficultyPanel.SetActive(visible);
        if (easyButton != null) easyButton.gameObject.SetActive(visible);
        if (normalButton != null) normalButton.gameObject.SetActive(visible);
        if (hardButton != null) hardButton.gameObject.SetActive(visible);
    }

    /// <summary>Sets the difficulty for the whole run, then loads the crawler.</summary>
    public void StartGame(FoeController.Difficulty difficulty)
    {
        FoeController.CurrentDifficulty = difficulty;
        SceneManager.LoadScene(crawlerScene);
    }
}
