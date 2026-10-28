using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Keeps the player's score and shows it in a UI text as:  Score: 1250
///
/// How it gets points: FoeController fires OnFoeKilled(points) once per dead foe, with the final score already
/// worked out (base score x difficulty x the foe's random roll). This script just adds that up.
///
/// Setup:
///   1. Put this script on an object that is ALWAYS ACTIVE (an empty "GameManager", or the HUD canvas root).
///      A disabled object stops listening, so don't put it on something the notebook hides.
///   2. Drag your score text into ONE of the two slots below (TextMeshPro or the old Legacy Text).
///      Or attach this script straight onto the text object and leave both slots empty.
///   3. Keep the text on the HUD canvas, NOT inside the notebook canvas, or it disappears when the notebook closes.
///
/// Other scripts can use it too:
///   ScoreController.Score                 read the current score
///   ScoreController.TrySpend(cost)        returns true and subtracts if the player can afford it (for the shop)
///   ScoreController.AddScore(points)      give points that didn't come from a kill (bonuses, pickups)
///   ScoreController.ResetScore()          back to 0 (new run)
/// </summary>
public class ScoreController : MonoBehaviour
{
    [Header("UI (fill in ONE)")]
    [Tooltip("A TextMeshPro text (UI > Text - TextMeshPro).")]
    [SerializeField] private TMP_Text tmpText;
    [Tooltip("An old-style Legacy text (UI > Legacy > Text). Only used if you didn't fill in the TextMeshPro one.")]
    [SerializeField] private Text legacyText;

    [Header("Format")]
    [Tooltip("Shown in front of the number.")]
    [SerializeField] private string prefix = "Score: ";

    /// <summary>The player's current score.</summary>
    public static int Score { get; private set; }

    /// <summary>Fires whenever the score changes, with the new total.</summary>
    public static event System.Action<int> OnScoreChanged;

    // ------------------------------------------------------------ lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        // Reset once per play session, not whenever a scene containing this component is loaded.
        // This lets the score remain available as currency while moving between gameplay and shop scenes.
        Score = 0;
        OnScoreChanged = null;
    }

    private void Awake()
    {
        if (tmpText == null && legacyText == null)
        {
            tmpText = GetComponent<TMP_Text>();
            if (tmpText == null) legacyText = GetComponent<Text>();
        }

        if (tmpText == null && legacyText == null)
            Debug.LogWarning("ScoreController: no text assigned. Drag your score text into the Inspector.", this);
    }

    private void OnEnable()
    {
        // Always unsubscribe in OnDisable: these are static events and would otherwise keep calling a destroyed object.
        FoeController.OnFoeKilled += AddScore;
        OnScoreChanged += UpdateText;
        UpdateText(Score);
    }

    private void OnDisable()
    {
        FoeController.OnFoeKilled -= AddScore;
        OnScoreChanged -= UpdateText;
    }

    // ------------------------------------------------------------ score

    /// <summary>Adds points to the score. FoeController calls this for you through OnFoeKilled.</summary>
    public static void AddScore(int points)
    {
        if (points == 0) return;
        Score += points;
        OnScoreChanged?.Invoke(Score);
    }

    /// <summary>Spends points if the player has enough. Returns false (and changes nothing) if they can't afford it.</summary>
    public static bool TrySpend(int cost)
    {
        if (cost < 0 || cost > Score) return false;
        Score -= cost;
        OnScoreChanged?.Invoke(Score);
        return true;
    }

    /// <summary>Sets the score back to 0.</summary>
    public static void ResetScore()
    {
        Score = 0;
        OnScoreChanged?.Invoke(Score);
    }

    // ------------------------------------------------------------ display

    private void UpdateText(int value)
    {
        string label = prefix + value;

        if (tmpText != null) tmpText.text = label;
        else if (legacyText != null) legacyText.text = label;
    }
}
