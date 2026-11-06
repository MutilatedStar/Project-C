using System.Collections;
using UnityEngine;

/// <summary>
/// A pillar that blocks the way until its room is solved. Put this on the pillar object (a sprite with a solid
/// Collider 2D), then list it in the Pillars list of ITS room (PillarRoom). A pillar belongs to ONE room only: solving
/// another room never touches it, and a room never touches a pillar it doesn't list.
///
/// When the room is solved the pillar's colliders are switched off at once (that is what stops the player, so the way is
/// open immediately) and its pictures fade out over Fade Out Seconds. The pillar object itself stays switched on, so it can
/// be brought back by PillarRoom.ResetRoom().
/// </summary>
[DisallowMultipleComponent]
public class RoomPillar : MonoBehaviour
{
    [Tooltip("Seconds the pillar's pictures take to fade away once the room is solved. Its collision is off straight away either way. 0 = vanishes at once.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 0.4f;

    /// <summary>The room this pillar belongs to (set by that room). Null until a room claims it.</summary>
    public PillarRoom Room { get; set; }

    /// <summary>True once the pillar has been taken away.</summary>
    public bool IsRemoved { get; private set; }

    private Collider2D[] colliders;
    private bool[] colliderWasOn;
    private SpriteRenderer[] renderers;
    private Color[] originalColors;
    private Coroutine fading;

    private void Awake()
    {
        Cache();
    }

    private void Start()
    {
        if (Room == null)
        {
            Debug.LogWarning("RoomPillar '" + name + "' doesn't belong to any PillarRoom, so nothing will ever remove it. " +
                             "Drag it into the Pillars list of its room.", this);
        }
    }

    // A room can remove a pillar whose object started switched off, before its Awake has run, so this is also called on demand.
    private void Cache()
    {
        if (colliders != null) return;

        colliders = GetComponentsInChildren<Collider2D>(true);
        colliderWasOn = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) colliderWasOn[i] = colliders[i].enabled;

        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) originalColors[i] = renderers[i].color;
    }

    /// <summary>Takes the pillar away: collision off now, pictures fade out. Called by its PillarRoom.</summary>
    public void Remove(bool instant = false)
    {
        Cache();
        if (IsRemoved) return;
        IsRemoved = true;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = false;
        }

        if (fading != null) StopCoroutine(fading);
        if (instant || fadeOutSeconds <= 0f || !isActiveAndEnabled) SetRenderers(false);
        else fading = StartCoroutine(FadeOut());
    }

    /// <summary>Brings the pillar back exactly as it was. Called by PillarRoom.ResetRoom().</summary>
    public void Restore()
    {
        Cache();
        if (!IsRemoved) return;
        IsRemoved = false;

        if (fading != null)
        {
            StopCoroutine(fading);
            fading = null;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null) colliders[i].enabled = colliderWasOn[i];
        }
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].enabled = true;
            renderers[i].color = originalColors[i];
        }
    }

    private IEnumerator FadeOut()
    {
        for (float t = 0f; t < fadeOutSeconds; t += Time.deltaTime)
        {
            float k = 1f - t / fadeOutSeconds;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Color c = originalColors[i];
                c.a *= k;
                renderers[i].color = c;
            }
            yield return null;
        }
        SetRenderers(false);
        fading = null;
    }

    private void SetRenderers(bool on)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].color = originalColors[i];
            renderers[i].enabled = on;
        }
    }
}
