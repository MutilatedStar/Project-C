using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Small shared toolbox for the screens that are built in code: the main menu pages, the stage banner and the PVP result screen.
/// Nothing here needs art: the rounded corners are drawn into a tiny texture the first time they are used.
/// </summary>
public static class StageUi
{
    // ------------------------------------------------------------ palette (blue = Player 1, orange = Player 2)

    public static readonly Color Panel   = new Color(0.06f, 0.08f, 0.14f, 0.94f);
    public static readonly Color Card    = new Color(0.11f, 0.14f, 0.22f, 1f);
    public static readonly Color CardHot = new Color(0.19f, 0.25f, 0.38f, 1f);
    public static readonly Color Blue    = new Color(0.30f, 0.60f, 1.00f, 1f);
    public static readonly Color Orange  = new Color(1.00f, 0.62f, 0.20f, 1f);
    public static readonly Color Violet  = new Color(0.66f, 0.50f, 1.00f, 1f);
    public static readonly Color Green   = new Color(0.35f, 0.85f, 0.55f, 1f);
    public static readonly Color Red     = new Color(1.00f, 0.36f, 0.36f, 1f);
    public static readonly Color Text    = new Color(0.94f, 0.96f, 1.00f, 1f);
    public static readonly Color TextDim = new Color(0.62f, 0.69f, 0.82f, 1f);

    // ------------------------------------------------------------ sprites

    private static Sprite _rounded;

    /// <summary>Size and corner radius (pixels) of the rounded-rectangle texture. The 9-slice border of the sprite is the radius on every side.</summary>
    public const int RoundedSize = 64;
    public const int RoundedRadius = 20;

    /// <summary>
    /// Set ONLY by the editor tool that bakes the menu UI into a scene (MenuUiBakeTool). While it is set, Rounded returns this real sprite
    /// asset instead of the generated one, so what is saved in the scene is a proper asset reference (a generated sprite can't be saved).
    /// </summary>
    public static Sprite RoundedOverride;

    /// <summary>The pixels of the rounded rectangle: white, with the corners cut away in the alpha channel. The caller owns the texture.</summary>
    public static Texture2D MakeRoundedTexture()
    {
        const int size = RoundedSize;
        const int radius = RoundedRadius;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float cx = Mathf.Clamp(px, radius, size - radius);
                float cy = Mathf.Clamp(py, radius, size - radius);
                float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - d + 0.5f));
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>A white strip whose alpha fades 1 -> 0 (opaqueOnLeft) or 0 -> 1 across its width. Tint it with the Image colour. The caller owns the texture.</summary>
    public static Texture2D MakeFadeTexture(bool opaqueOnLeft)
    {
        const int width = 128;
        Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int x = 0; x < width; x++)
        {
            float k = x / (float)(width - 1);
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, opaqueOnLeft ? 1f - k : k));
        }
        tex.Apply();
        return tex;
    }

    /// <summary>A white rounded rectangle you can tint. Use it with Image Type = Sliced.</summary>
    public static Sprite Rounded
    {
        get
        {
            if (RoundedOverride != null) return RoundedOverride;
            if (_rounded != null) return _rounded;

            const int size = RoundedSize;
            const int radius = RoundedRadius;
            Texture2D tex = MakeRoundedTexture();
            tex.hideFlags = HideFlags.HideAndDontSave;

            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                     SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            _rounded.hideFlags = HideFlags.HideAndDontSave;
            return _rounded;
        }
    }

    /// <summary>A sprite that fades from one colour on the left to another on the right.</summary>
    public static Sprite HorizontalGradient(Color left, Color right)
    {
        const int width = 128;
        Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int x = 0; x < width; x++) tex.SetPixel(x, 0, Color.Lerp(left, right, x / (float)(width - 1)));
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;

        Sprite s = Sprite.Create(tex, new Rect(0, 0, width, 1), new Vector2(0.5f, 0.5f), 100f);
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // ------------------------------------------------------------ building blocks

    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = 5;

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    /// <summary>Makes sure clicks, keyboard and controller can drive UI. Does nothing if the scene already has an EventSystem.</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        GameObject go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.InputSystem.UI.InputSystemUIInputModule module = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        module.AssignDefaultActions();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Centre of the parent, fixed size.</summary>
    public static void Center(RectTransform rt, Vector2 size, Vector2 offset)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
    }

    /// <summary>Hangs from the top-centre of the parent, y is the distance down from the top edge.</summary>
    public static void Top(RectTransform rt, Vector2 size, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(0f, -y);
    }

    /// <summary>Sits at the bottom-centre of the parent, y is the distance up from the bottom edge.</summary>
    public static void Bottom(RectTransform rt, Vector2 size, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(0f, y);
    }

    public static Image Img(string name, Transform parent, Color color, Sprite sprite = null, bool sliced = false)
    {
        RectTransform rt = Rect(name, parent);
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        }
        return image;
    }

    public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color,
                                        TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        RectTransform rt = Rect(name, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    /// <summary>A rounded, clickable button background with hover / controller highlight built in.</summary>
    public static Button MakeButton(string name, Transform parent, Color normal, Color hot, out Image background)
    {
        Image img = Img(name, parent, normal, Rounded, true);
        img.raycastTarget = true;

        Button button = img.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = img;

        StageButtonFx fx = img.gameObject.AddComponent<StageButtonFx>();
        fx.background = img;
        fx.normal = normal;
        fx.hot = hot;

        background = img;
        return button;
    }

    // ------------------------------------------------------------ scene loading

    /// <summary>
    /// The first candidate that can really be loaded (is in Build Settings), or null. The candidates are tried exactly first; if none matches,
    /// they are tried again ignoring upper / lower case, spaces, dashes and underscores against every scene in the build, so "MainMenu",
    /// "Main Menu" and "Mainmenu" all find the same scene. The name returned is the scene's real name.
    /// </summary>
    public static string ResolveScene(params string[] candidates)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!string.IsNullOrEmpty(candidates[i]) && Application.CanStreamedLevelBeLoaded(candidates[i])) return candidates[i];
        }

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.IsNullOrEmpty(candidates[i])) continue;
            string wanted = SceneKey(candidates[i]);

            for (int s = 0; s < count; s++)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(s));
                if (!string.IsNullOrEmpty(sceneName) && SceneKey(sceneName) == wanted) return sceneName;
            }
        }
        return null;
    }

    private static string SceneKey(string name)
    {
        return name.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
    }

    /// <summary>Fades the screen to black, then loads the scene. runner is any MonoBehaviour that stays alive until the load.</summary>
    public static void FadeAndLoad(MonoBehaviour runner, string sceneName)
    {
        runner.StartCoroutine(FadeAndLoadRoutine(sceneName));
    }

    private static IEnumerator FadeAndLoadRoutine(string sceneName)
    {
        Canvas canvas = CreateCanvas("Fade To Black", 32000);
        Image black = Img("Black", canvas.transform, new Color(0f, 0f, 0f, 0f));
        Stretch(black.rectTransform);

        float t = 0f;
        while (t < 0.3f)
        {
            t += Time.unscaledDeltaTime;
            Color c = black.color;
            c.a = Mathf.Clamp01(t / 0.3f);
            black.color = c;
            yield return null;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }
}
