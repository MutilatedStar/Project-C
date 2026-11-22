using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Modular tab system. Replaces TabController + TabOverlay. Depends ONLY on DirectButton.cs
/// (no MenuController, no GameSettings, no other script).
///
/// Each tab = a DirectButton (clickable and/or hotkey) + a Panel (an object holding that tab's content).
/// Opening a tab shows its Panel and hides every other tab's Panel. That's all it does, so there is
/// nothing to fall out of step.
///
/// Where to put it: on the menu's root object (or any always-visible object inside the menu), NOT on
/// any tab's Panel. It only listens while it is active, so a hidden menu costs nothing, and every time
/// the menu is shown again it jumps back to the Start Tab.
///
/// Built to be loud when something is missing (it prints a clear Console message instead of silently
/// doing nothing), including the classic trap: a tab with a hotkey but no Button, which can only be
/// opened with the key.
///
/// Sounds: add each tab's Button to SfxManager's "Button Sounds" list for click sounds.
/// From other scripts:  tabGroup.Open(2);  tabGroup.Open("Options");  tabGroup.TabChanged += ...
/// </summary>
[DisallowMultipleComponent]
public class TabGroup : MonoBehaviour
{
    [System.Serializable]
    public class Tab
    {
        public string name = "Tab";

        [Tooltip("The tab's clickable button and/or hotkey. Taps are detected by code, so leave the Button's OnClick empty.")]
        public DirectButton button = new DirectButton();

        [Tooltip("An empty object that holds everything this tab shows. It must NOT be the object this script is on.")]
        public GameObject panel;

        [Tooltip("Optional. Graphics recolored with the tab (selected / dimmed). Empty = the Button's own graphic.")]
        public Graphic[] tint;
    }

    [Header("Tabs")]
    public List<Tab> tabs = new List<Tab>();
    [Tooltip("Which tab opens first, and again every time this object is switched on (0 = the first in the list).")]
    [Min(0)] public int startTab = 0;
    public bool resetToStartTabOnEnable = true;

    [Header("Look")]
    public bool tintTabs = true;
    public Color selectedColor = Color.white;
    public Color dimmedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("Debug")]
    [Tooltip("Prints every tab change, and every tap/miss on every tab button. Turn off once it works.")]
    public bool logEvents = false;

    /// <summary>Raised right after a tab opens. The number is the new tab.</summary>
    public event System.Action<int> TabChanged;

    /// <summary>The tab that is open right now (-1 before any has opened).</summary>
    public int Current { get { return _current; } }

    private int _current = -1;
    private bool _validated;
    private bool _valid;

    // ------------------------------------------------------------------ lifecycle

    private void OnEnable()
    {
        if (!Validate())
        {
            enabled = false;
            return;
        }

        if (logEvents)
        {
            for (int i = 0; i < tabs.Count; i++) tabs[i].button.debugLog = true;
        }

        if (resetToStartTabOnEnable || _current < 0) Open(startTab, true);
    }

    private void Update()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].button.WasTapped())
            {
                Open(i);
                return;
            }
        }
    }

    // ------------------------------------------------------------------ public API

    /// <summary>Checks the setup and prints what is wrong. Safe to call more than once.</summary>
    public bool Validate()
    {
        if (_validated) return _valid;
        _validated = true;
        _valid = true;

        if (tabs == null || tabs.Count == 0)
        {
            Debug.LogError("TabGroup: the Tabs list is empty.", this);
            return _valid = false;
        }

        for (int i = 0; i < tabs.Count; i++)
        {
            Tab t = tabs[i];
            string id = "TabGroup tab " + i + " (\"" + t.name + "\")";

            if (t.panel == null)
            {
                Debug.LogError(id + " has no Panel, so opening it would show nothing.", this);
            }
            else if (t.panel == gameObject || transform.IsChildOf(t.panel.transform))
            {
                Debug.LogError(id + ": the TabGroup script is on (or inside) this tab's Panel, so hiding the panel would " +
                               "switch the tabs off. Put the script on the menu's root object instead.", this);
                _valid = false;
            }

            if (!t.button.HasTarget)
            {
                if (t.button.hotkey == KeyCode.None)
                    Debug.LogError(id + " has no Button, no Area and no hotkey, so nothing can open it.", this);
                else
                    Debug.LogWarning(id + " has a hotkey but NO Button/Area, so it can only be opened with the key and " +
                                     "clicking it will do nothing. Drag its Button into the tab's Button slot.", this);
            }
        }
        return _valid;
    }

    public void Open(int index) { Open(index, false); }

    public void Open(string tabName)
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            if (string.Equals(tabs[i].name, tabName, System.StringComparison.OrdinalIgnoreCase))
            {
                Open(i, false);
                return;
            }
        }
        Debug.LogWarning("TabGroup: there is no tab named \"" + tabName + "\".", this);
    }

    // ------------------------------------------------------------------ internals

    private void Open(int index, bool force)
    {
        if (tabs == null || index < 0 || index >= tabs.Count)
        {
            Debug.LogWarning("TabGroup: there is no tab " + index + " (the list has " + (tabs == null ? 0 : tabs.Count) + ").", this);
            return;
        }
        if (index == _current && !force) return;

        _current = index;
        GameObject shown = tabs[index].panel;
        EventSystem es = EventSystem.current;

        // Hide everything else first, then show the chosen one (so a panel shared by two tabs still ends up visible).
        for (int i = 0; i < tabs.Count; i++)
        {
            GameObject p = tabs[i].panel;
            if (p == null || p == shown) continue;

            // Let go of anything selected inside a panel that is about to disappear.
            if (es != null && es.currentSelectedGameObject != null &&
                es.currentSelectedGameObject.transform.IsChildOf(p.transform))
            {
                es.SetSelectedGameObject(null);
            }
            p.SetActive(false);
        }
        if (shown != null) shown.SetActive(true);

        if (tintTabs)
        {
            for (int i = 0; i < tabs.Count; i++) Tint(tabs[i], i == index);
        }

        if (logEvents) Debug.Log("TabGroup: opened tab " + index + " (\"" + tabs[index].name + "\").", this);
        if (TabChanged != null) TabChanged(index);
    }

    private void Tint(Tab tab, bool selected)
    {
        Color color = selected ? selectedColor : dimmedColor;

        if (tab.tint != null && tab.tint.Length > 0)
        {
            for (int i = 0; i < tab.tint.Length; i++)
                if (tab.tint[i] != null) tab.tint[i].color = color;
        }
        else if (tab.button.button != null && tab.button.button.targetGraphic != null)
        {
            tab.button.button.targetGraphic.color = color;
        }
    }
}
