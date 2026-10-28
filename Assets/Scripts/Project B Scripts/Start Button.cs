using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
 
// Attach to each menu button. The GameObject's name decides which method runs,
// so name the buttons exactly: OnStartClick, OnOptionsClick, OnExitClick.
public class StartButton : MonoBehaviour
{
    [SerializeField] private GameObject optionsPanel; // only needed on the OnOptionsClick button
 
    private void Awake()
    {
        if (TryGetComponent(out Button button))
        {
            button.onClick.AddListener(HandleClick);
        }
    }
 
    // `name` is the name of the GameObject this script is on (inherited from UnityEngine.Object)
    private void HandleClick()
    {
        switch (name)
        {
            case nameof(OnStartClick):
                OnStartClick();
                break;
            case nameof(OnOptionsClick):
                OnOptionsClick();
                break;
            case nameof(OnExitClick):
                OnExitClick();
                break;
            default:
                Debug.LogWarning($"StartButton: no handler for a GameObject named \"{name}\". " +
                                 "Name it OnStartClick, OnOptionsClick, or OnExitClick.", this);
                break;
        }
    }
 
    public void OnStartClick()
    {
        SceneManager.LoadScene("Main Menu");
    }
 
    public void OnOptionsClick()
    {
        optionsPanel.SetActive(true);
    }
 
    public void OnExitClick()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }
}