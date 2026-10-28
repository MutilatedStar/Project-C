using UnityEngine;
using UnityEngine.UI;

// Put this on the Apply button. Pressing it applies every pending setting (music, sound,
// resolution) and starts the countdown to save them to settings.txt.
[RequireComponent(typeof(Button))]
public class ApplyButton : MonoBehaviour
{
    [Header("OPTIONAL status text (leave empty to skip)")]
    [SerializeField] TextRef statusText;
    [Tooltip("{0} becomes the save delay in seconds.")]
    [SerializeField] string appliedMessage = "Applied! Saving in {0} seconds...";
    [SerializeField] string savedMessage = "Settings saved.";

    Button button;

    void Awake()
    {
        button = GetComponent<Button>();
    }

    void OnEnable()
    {
        button.onClick.AddListener(OnClick);
        GameSettings.OnSaved += OnSaved;
        statusText.Set("");
    }

    void OnDisable()
    {
        button.onClick.RemoveListener(OnClick);
        GameSettings.OnSaved -= OnSaved;
    }

    void OnClick()
    {
        GameSettings.Apply();
        statusText.Set(string.Format(appliedMessage, GameSettings.SaveDelaySeconds));
    }

    void OnSaved()
    {
        statusText.Set(savedMessage);
    }
}
