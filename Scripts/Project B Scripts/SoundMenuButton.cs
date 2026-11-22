using UnityEngine;
using UnityEngine.UI;

// The menu button for Sound On / Off. Takes effect when Apply is pressed.
// Objects only react if they have a SoundSwitch on them.
public class SoundMenuButton : MonoBehaviour
{
    [SerializeField] Button button;
    [SerializeField] TextRef label;
    [SerializeField] string onText = "Sound: On";
    [SerializeField] string offText = "Sound: Off";

    void OnEnable()
    {
        GameSettings.PendingSoundOn = GameSettings.SoundOn;
        if (button != null) button.onClick.AddListener(Toggle);
        Refresh();
    }

    void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(Toggle);
    }

    void Toggle()
    {
        GameSettings.PendingSoundOn = !GameSettings.PendingSoundOn;
        Refresh();
    }

    void Refresh()
    {
        label.Set(GameSettings.PendingSoundOn ? onText : offText);
    }
}
