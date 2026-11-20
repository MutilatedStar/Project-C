using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The simple dark backdrop behind the pause menu and the rank screens: black at some opacity (NOT fully black), so the level still shows
/// through a little. The LIGHTS OUT screen does not use this. Its black stays fully black and LightsOutScreen builds it itself.
///
///   Pause menu    PauseController calls ScreenDim.Build when it builds its menu. Its "Dim Amount" is the opacity.
///   Rank screens  LevelResults calls SnapshotCanvases() before ResultsUi builds a panel and BackBehindNewCanvases() right after. The
///                 backdrop goes INSIDE the canvas that ResultsUi just created, as its first child, so it is drawn behind every button,
///                 label and rank on that canvas whatever their sorting order is. It fades in, and FadeOutAll() fades it out again.
///
/// The backdrop is stretched far past every edge of the canvas (Overscan), so no window size, aspect ratio or ultrawide screen can show a gap.
/// It also swallows clicks meant for the game behind it.
/// </summary>
public static class ScreenDim
{
    public const float DefaultOpacity = 0.7f;
    public const float DefaultOverscan = 2000f;

    private const string BackdropName = "Backdrop";

    // ------------------------------------------------------------ the backdrop itself

    /// <summary>
    /// A black Image stretched over the whole of parent (and far past it), placed as parent's FIRST child so everything else on parent is drawn
    /// on top of it. opacity 0 = invisible, 1 = fully black.
    /// </summary>
    public static Image Build(Transform parent, float opacity, float overscan = DefaultOverscan)
    {
        var go = new GameObject(BackdropName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.SetAsFirstSibling();

        float push = Mathf.Max(0f, overscan);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-push, -push);
        rect.offsetMax = new Vector2(push, push);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(opacity));
        image.raycastTarget = true;   // swallows clicks meant for the game behind it
        return image;
    }

    // ------------------------------------------------------------ the rank screens

    /// <summary>Every canvas that exists right now. Call this BEFORE ResultsUi builds a panel and hand the result to BackBehindNewCanvases.</summary>
    public static Canvas[] SnapshotCanvases()
    {
        return Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>
    /// Puts the backdrop behind a panel that was built after SnapshotCanvases(). It goes into the new canvas (the lowest-sorted one if the panel
    /// made several), fading in over fadeInSeconds (0 = at once). If the panel did not create any canvas of its own, the backdrop gets a canvas
    /// of its own drawn at fallbackSortingOrder instead. Returns true if it was placed on the panel's own canvas.
    /// </summary>
    public static bool BackBehindNewCanvases(Canvas[] before, float opacity, float fadeInSeconds, int fallbackSortingOrder)
    {
        HashSet<Canvas> known = new HashSet<Canvas>();
        if (before != null)
        {
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] != null) known.Add(before[i]);
            }
        }

        Canvas target = null;
        Canvas[] now = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < now.Length; i++)
        {
            Canvas c = now[i];
            if (c == null || known.Contains(c) || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if (target == null || c.sortingOrder < target.sortingOrder) target = c;
        }

        if (target != null)
        {
            Image onPanelCanvas = Build(target.transform, opacity);
            ScreenDimFade.Run(onPanelCanvas, 0f, opacity, fadeInSeconds, onPanelCanvas.gameObject);
            return true;
        }

        // No canvas of its own: the panel lives on a canvas that was already there. The backdrop gets its own, at the order set on LevelResults.
        var holder = new GameObject("RankBackdropCanvas", typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = holder.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = fallbackSortingOrder;

        CanvasScaler scaler = holder.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Image own = Build(holder.transform, opacity);
        ScreenDimFade.Run(own, 0f, opacity, fadeInSeconds, holder);

        Debug.LogWarning("ScreenDim: the results panel did not create a canvas of its own, so the backdrop got one at sorting order " + fallbackSortingOrder +
                         ". If it covers the panel, lower Level Results > Backdrop Fallback Sorting Order below the panel's canvas.");
        return false;
    }

    /// <summary>Fades out every rank-screen backdrop that is up (and removes it when it is clear).</summary>
    public static void FadeOutAll(float seconds)
    {
        ScreenDimFade.FadeOutAll(seconds);
    }
}
