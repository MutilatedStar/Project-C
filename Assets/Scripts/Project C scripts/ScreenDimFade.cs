using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fades one ScreenDim backdrop in or out. It runs on UNSCALED time, so it works while Time.timeScale is 0. ScreenDim adds it for you.
/// When a backdrop has faded all the way out, the object it was told to clean up is destroyed with it.
/// </summary>
public sealed class ScreenDimFade : MonoBehaviour
{
    private static readonly List<ScreenDimFade> Live = new List<ScreenDimFade>();

    private Image _image;
    private GameObject _destroyWhenClear;
    private float _from;
    private float _to;
    private float _seconds;
    private float _elapsed;
    private bool _running;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Live.Clear();
    }

    /// <summary>Starts fading image from one opacity to another. destroyWhenClear (may be null) is destroyed once the image is fully clear.</summary>
    public static ScreenDimFade Run(Image image, float from, float to, float seconds, GameObject destroyWhenClear)
    {
        if (image == null) return null;

        ScreenDimFade fade = image.GetComponent<ScreenDimFade>();
        if (fade == null) fade = image.gameObject.AddComponent<ScreenDimFade>();

        fade._image = image;
        fade._destroyWhenClear = destroyWhenClear;
        fade.Begin(from, to, seconds);
        return fade;
    }

    /// <summary>Fades every backdrop that is up to clear, from whatever it shows now.</summary>
    public static void FadeOutAll(float seconds)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            ScreenDimFade fade = Live[i];
            if (fade == null || fade._image == null)
            {
                Live.RemoveAt(i);
                continue;
            }
            fade.Begin(fade._image.color.a, 0f, seconds);
        }
    }

    private void OnEnable()
    {
        if (!Live.Contains(this)) Live.Add(this);
    }

    private void OnDisable()
    {
        Live.Remove(this);
    }

    private void Begin(float from, float to, float seconds)
    {
        _from = from;
        _to = to;
        _seconds = Mathf.Max(0f, seconds);
        _elapsed = 0f;
        _running = true;

        SetAlpha(from);
        if (_seconds <= 0f) Finish();
    }

    private void Update()
    {
        if (!_running || _image == null) return;

        _elapsed += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_elapsed / _seconds);
        SetAlpha(Mathf.Lerp(_from, _to, k));
        if (k >= 1f) Finish();
    }

    private void Finish()
    {
        _running = false;
        SetAlpha(_to);
        if (_to <= 0f && _destroyWhenClear != null) Destroy(_destroyWhenClear);
    }

    private void SetAlpha(float alpha)
    {
        if (_image == null) return;
        Color c = _image.color;
        c.a = alpha;
        _image.color = c;
    }
}
