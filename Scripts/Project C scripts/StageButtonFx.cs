using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover / controller highlight for the code-built buttons: the button grows a little and brightens while it is the selected one.
/// The mouse selects what it hovers over, so mouse, keyboard and controller always agree on which button is lit.
/// Also scrolls a ScrollRect so the selected card is always on screen, and forwards "Cancel" (Esc / B button) to onCancel.
/// </summary>
public class StageButtonFx : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler, ICancelHandler
{
    public Image background;
    public Color normal = Color.gray;
    public Color hot = Color.white;
    public float hotScale = 1.04f;
    public System.Action onCancel;

    private bool selected;
    private float blend;

    public void OnPointerEnter(PointerEventData eventData)
    {
        Selectable s = GetComponent<Selectable>();
        if (s != null && s.interactable && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
    }

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        ScrollIntoView();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
    }

    public void OnCancel(BaseEventData eventData)
    {
        if (onCancel != null) onCancel();
    }

    private void Update()
    {
        float target = selected ? 1f : 0f;
        if (Mathf.Approximately(blend, target) && (target == 0f || transform.localScale.x >= hotScale - 0.0001f)) return;

        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime * 9f);
        float k = blend * blend * (3f - 2f * blend);
        if (background != null) background.color = Color.Lerp(normal, hot, k);
        transform.localScale = Vector3.one * Mathf.Lerp(1f, hotScale, k);
    }

    private void OnDisable()
    {
        selected = false;
        blend = 0f;
        transform.localScale = Vector3.one;
        if (background != null) background.color = normal;
    }

    private void ScrollIntoView()
    {
        ScrollRect scroll = GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;

        RectTransform item = (RectTransform)transform;
        Canvas.ForceUpdateCanvases();

        Vector3[] itemCorners = new Vector3[4];
        Vector3[] viewCorners = new Vector3[4];
        item.GetWorldCorners(itemCorners);
        scroll.viewport.GetWorldCorners(viewCorners);

        float itemTop = itemCorners[1].y;
        float itemBottom = itemCorners[0].y;
        float viewTop = viewCorners[1].y;
        float viewBottom = viewCorners[0].y;

        float scale = scroll.content.lossyScale.y;
        float scrollable = (scroll.content.rect.height - scroll.viewport.rect.height) * scale;
        if (scrollable <= 0.001f) return;

        float delta = 0f;
        if (itemTop > viewTop) delta = itemTop - viewTop;
        else if (itemBottom < viewBottom) delta = itemBottom - viewBottom;
        if (delta == 0f) return;

        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + delta / scrollable);
    }
}
