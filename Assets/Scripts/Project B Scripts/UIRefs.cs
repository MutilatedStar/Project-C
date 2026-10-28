using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Small helpers so every menu script works with EITHER legacy UI (Text / InputField)
// OR TextMeshPro (TMP_Text / TMP_InputField). In the Inspector, fill in only the one you use.

[System.Serializable]
public class TextRef
{
    public Text legacy;
    public TMP_Text tmp;

    public void Set(string value)
    {
        if (legacy != null) legacy.text = value;
        if (tmp != null) tmp.text = value;
    }
}

[System.Serializable]
public class InputRef
{
    public InputField legacy;
    public TMP_InputField tmp;

    public string Value
    {
        get
        {
            if (tmp != null) return tmp.text;
            if (legacy != null) return legacy.text;
            return "";
        }
        set
        {
            if (tmp != null) tmp.text = value;
            if (legacy != null) legacy.text = value;
        }
    }

    public void AddEndEditListener(UnityAction<string> action)
    {
        if (tmp != null) tmp.onEndEdit.AddListener(action);
        if (legacy != null) legacy.onEndEdit.AddListener(action);
    }

    public void RemoveEndEditListener(UnityAction<string> action)
    {
        if (tmp != null) tmp.onEndEdit.RemoveListener(action);
        if (legacy != null) legacy.onEndEdit.RemoveListener(action);
    }
}
