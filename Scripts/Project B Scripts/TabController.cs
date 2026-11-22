using UnityEngine;
using UnityEngine.UI;

// Runs the notebook's tabs. Attach it ONCE (not to each tab button).
// Each entry in "Tabs" is one tab and holds everything that tab needs, so nothing can fall out of step:
//   0 = Cover (not a real tab: leave its tab slots empty, no hotkey)
//   1 = Inventory (I)   2 = Notes (N)   3 = Options (O)   4 = Quit (Q)
//
// Tabs whose content is an overlay (NotesMenu for Notes, TabOverlay for Inventory / Options / Quit) should
// leave their "Ui" slot EMPTY: the overlay script shows and hides that content itself when it hears TabChanged.
//
// A tab opens two ways, and both work at the same time:
//   - press its hotkey
//   - click any part of the tab. This script checks the click itself, so the tab does not need a Button or an
//     OnClick event. (Old OnClick entries that call ActivateTab with the right number are harmless.)
//
// What a tab looks like on screen is described by these slots (all optional, use whatever fits your setup):
//   Tab Image : the tab's icon/image
//   Tab Box   : the whole tab as one box (the object that holds its icon and text)
//   Also Tint : other separate graphics that belong to the tab, e.g. a text label
// Everything listed for a tab is recolored with it (selectedColor when chosen, dimmedColor when not)
// and selects the tab when clicked. Anything that isn't listed keeps its own color.
//
// Sound: every real tab change plays "Tab Switch Clip" through "Audio Source", but only while the
// Sound On/Off setting (GameSettings.SoundOn) is on.
public class TabController : MonoBehaviour
{
    [System.Serializable]
    public class TabEntry
    {
        public string label;           // just a name so the list is readable, e.g. "Inventory"
        public Image tabImage;         // the tab's icon/image you see and click on screen (leave empty for the cover)

        [Tooltip("Optional. The whole tab as one box: the object that holds its icon and text. Clicking anywhere inside it selects the tab, and every graphic inside it (box, icon, text) is recolored with the tab. Use one box per tab, never a box that also holds other tabs or other UI.")]
        public RectTransform tabBox;

        [Tooltip("Optional. Other separate graphics that belong to this tab, like a text label (TextMeshPro or legacy Text both work). They recolor with the tab and select it when clicked.")]
        public Graphic[] alsoTint;

        public GameObject background;  // this tab's "Background" group
        public GameObject ui;          // this tab's "UI" group (leave empty for tabs that use an overlay script)
        public KeyCode hotkey;         // I / N / O / Q (leave as None for the cover)
    }

    public TabEntry[] tabs;

    public Color selectedColor = Color.white;
    public Color dimmedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Tooltip("Seconds the cover shows before the book flips to Inventory on its own.")]
    public float coverDelay = 3f;

    [Header("Sound")]
    [Tooltip("The AudioSource that plays the tab sound. Put it on its OWN empty object (not on this one) and add a SoundSwitch to that object, so the Sound On/Off setting controls it like every other sound.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Sound played whenever the tab changes. Leave empty for no sound.")]
    [SerializeField] private AudioClip tabSwitchClip;
    [Range(0f, 1f)]
    [SerializeField] private float tabSwitchVolume = 1f;
    [Tooltip("Also play the sound when the notebook opens onto the Cover. Off by default: the Cover flipping to Inventory already plays it.")]
    [SerializeField] private bool playOnCover = false;

    [Tooltip("Debug: print every object this script recolors. Click a line in the Console to highlight that object in the Hierarchy.")]
    public bool logTinting = false;

    // Raised at the very end of ActivateTab, right after the tab has been swapped. The number is the new tab.
    // (NotesMenu and TabOverlay listen to this to show/hide their overlays.)
    public event System.Action<int> TabChanged;

    // The tab that is open right now (-1 before any tab has been activated).
    public int CurrentTab => currentTab;

    private int currentTab = -1;
    private float coverTimer;

    private void Awake()
    {
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            // The notebook may pause the game while it is open; the tab sound still has to play.
            audioSource.ignoreListenerPause = true;
        }
    }

    private void Update()
    {
        // Hotkeys, clicks and the cover timer only run while the notebook is open.
        if (!MenuController.IsOpen) return;

        // A tab is chosen by its hotkey, or by a click on any part of it.
        int chosenTab = GetPressedHotkeyTab();
        if (chosenTab < 0)
        {
            int clickedTab = GetClickedTab();
            if (clickedTab != currentTab) chosenTab = clickedTab;   // clicking the tab that's already open does nothing
        }

        if (chosenTab >= 0)
        {
            ActivateTab(chosenTab);
            return;
        }

        // Cover: flips to Inventory after coverDelay seconds, or when the player clicks.
        // (Mouse-up, so a click on a tab still ends on the tab that was clicked: it already switched on mouse-down.)
        if (currentTab == 0 && tabs.Length > 1)
        {
            coverTimer += Time.unscaledDeltaTime;
            if (coverTimer >= coverDelay || Input.GetMouseButtonUp(0))
            {
                ActivateTab(1);
            }
        }
    }

    // Called by MenuController (which passes 0 every time the book opens), and by any tab button that still uses OnClick.
    public void ActivateTab(int tabNo)
    {
        if (tabNo < 0 || tabNo >= tabs.Length)
        {
            Debug.LogWarning("TabController: there is no tab " + tabNo + " (the list has " + tabs.Length + " entries).", this);
            return;
        }

        bool changed = tabNo != currentTab;
        currentTab = tabNo;
        coverTimer = 0f;

        // Hide everything first...
        foreach (TabEntry tab in tabs) SetGroups(tab, false);

        // ...then show only the chosen tab. (Two steps, so a group shared by several tabs still ends up visible.)
        SetGroups(tabs[tabNo], true);

        // Colors: the chosen tab is highlighted, every other tab in the list is dimmed.
        // Only graphics listed in the entries are touched, so nothing else in the scene changes color.
        for (int i = 0; i < tabs.Length; i++)
        {
            TintEntry(tabs[i], i == tabNo ? selectedColor : dimmedColor);
        }

        // Sound: only on a real change (pressing the hotkey of the tab that's already open stays silent).
        if (changed && (tabNo != 0 || playOnCover)) PlayTabSound();

        // Tell anyone listening (like the notes overlay) that the swap is done.
        TabChanged?.Invoke(tabNo);
    }

    // Respects the Sound On/Off menu: the SoundSwitch on the AudioSource's object handles the source itself,
    // and the SoundOn check here keeps the tab silent even if that source is still switched on.
    private void PlayTabSound()
    {
        if (audioSource == null || tabSwitchClip == null) return;
        if (!GameSettings.SoundOn) return;
        if (!audioSource.isActiveAndEnabled) return;   // SoundSwitch turned it off

        audioSource.PlayOneShot(tabSwitchClip, tabSwitchVolume);
    }

    // The tab whose hotkey went down this frame, or -1. Ignored while the player is typing in a text field.
    public int GetPressedHotkeyTab()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            KeyCode key = tabs[i].hotkey;
            if (key != KeyCode.None && Input.GetKeyDown(key))
            {
                return MenuController.IsTyping ? -1 : i;
            }
        }
        return -1;
    }

    // The tab that was clicked this frame, or -1. Works even if nothing has a Button on it.
    // Clicks still count while typing, since clicking a tab is a normal way to leave a text field.
    private int GetClickedTab()
    {
        if (!Input.GetMouseButtonDown(0)) return -1;

        // Starts at 1: entry 0 is the cover, which is never clicked as a tab.
        for (int i = 1; i < tabs.Length; i++)
        {
            if (TabWasClicked(tabs[i])) return i;
        }
        return -1;
    }

    // True if the mouse is over any visible part of this tab.
    private static bool TabWasClicked(TabEntry tab)
    {
        if (tab.tabImage != null && ClickHits(tab.tabImage.rectTransform)) return true;
        if (ClickHits(tab.tabBox)) return true;

        if (tab.alsoTint != null)
        {
            foreach (Graphic graphic in tab.alsoTint)
            {
                if (graphic != null && ClickHits(graphic.rectTransform)) return true;
            }
        }
        return false;
    }

    private static bool ClickHits(RectTransform rect)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, CanvasCamera(rect));
    }

    // Overlay canvases need no camera for the hit test; camera and world-space canvases do.
    private static Camera CanvasCamera(RectTransform rect)
    {
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;

        switch (canvas.renderMode)
        {
            case RenderMode.ScreenSpaceCamera:
                return canvas.worldCamera;   // none assigned = Unity draws it like an overlay, so no camera here either
            case RenderMode.WorldSpace:
                return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            default:
                return null;                 // Screen Space - Overlay
        }
    }

    private static void SetGroups(TabEntry tab, bool active)
    {
        if (tab.background != null) tab.background.SetActive(active);
        if (tab.ui != null) tab.ui.SetActive(active);
    }

    // Recolors one entry: its Tab Image, everything inside its Tab Box, and anything listed in Also Tint. Nothing else.
    private void TintEntry(TabEntry tab, Color color)
    {
        Tint(tab.tabImage, color);

        if (tab.tabBox != null)
        {
            // "true" also finds graphics that are currently hidden, so they're already the right color when shown.
            foreach (Graphic graphic in tab.tabBox.GetComponentsInChildren<Graphic>(true))
            {
                Tint(graphic, color);
            }
        }

        if (tab.alsoTint != null)
        {
            foreach (Graphic graphic in tab.alsoTint)
            {
                Tint(graphic, color);
            }
        }
    }

    private void Tint(Graphic graphic, Color color)
    {
        if (graphic == null) return;

        graphic.color = color;
        if (logTinting) Debug.Log("TabController recolored \"" + graphic.name + "\"", graphic);
    }

    // Editor sanity check (runs when you change values or press Play): anything in Element 0 (the Cover)
    // gets dimmed whenever another tab is chosen, which is an easy way for unrelated UI to end up gray.
    private void OnValidate()
    {
        if (tabs == null || tabs.Length == 0 || tabs[0] == null) return;

        TabEntry cover = tabs[0];
        bool hasGraphics = cover.tabImage != null || cover.tabBox != null
                           || (cover.alsoTint != null && cover.alsoTint.Length > 0);
        if (hasGraphics)
        {
            Debug.LogWarning("TabController: Element 0 is the Cover, but it has a Tab Image / Tab Box / Also Tint assigned. " +
                             "Those graphics get dimmed whenever another tab is chosen. Clear them unless that's what you want.", this);
        }
    }
}