using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AldenFall.UI
{
    /// <summary>Slide + fade panel toggle (inventory, skills, combat trees).</summary>
    public class SlidePanel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Vector2 shownPos;
        public Vector2 hiddenPos;
        public float duration = 0.22f;
        public bool draggable = true;

        private RectTransform rect;
        private CanvasGroup group;
        private bool shown;
        private Coroutine moving;
        private Vector2 dragStartPointer;
        private Vector2 dragStartPos;

        public bool IsShown => shown;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            rect.anchoredPosition = hiddenPos;
        }

        public void Toggle() => Set(!shown);

        public void Set(bool show)
        {
            if (shown == show) return;
            shown = show;

            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(Animate(show));
        }

        private IEnumerator Animate(bool show)
        {
            group.interactable = show;
            group.blocksRaycasts = show;
            StartCoroutine(UIFx.FadeTo(group, show ? 1f : 0f, duration));
            yield return UIFx.MoveAnchored(rect, show ? shownPos : hiddenPos, duration, show);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!draggable || !shown) return;
            if (DragController.Instance != null && DragController.Instance.IsDragging) return;

            dragStartPointer = eventData.position;
            dragStartPos = rect.anchoredPosition;
            if (moving != null)
            {
                StopCoroutine(moving);
                moving = null;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!draggable || !shown) return;
            if (DragController.Instance != null && DragController.Instance.IsDragging) return;

            Vector2 delta = eventData.position - dragStartPointer;
            rect.anchoredPosition = ClampToViewport(dragStartPos + delta);
            shownPos = rect.anchoredPosition;
        }

        private Vector2 ClampToViewport(Vector2 pos)
        {
            var canvas = GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.GetComponent<RectTransform>() : null;
            if (canvasRect == null) return pos;

            Vector2 halfCanvas = canvasRect.rect.size * 0.5f;
            Vector2 halfPanel = rect.rect.size * 0.5f;
            Vector2 anchor = (rect.anchorMin + rect.anchorMax) * 0.5f;
            Vector2 anchorOffset = new Vector2(
                Mathf.Lerp(-halfCanvas.x, halfCanvas.x, anchor.x),
                Mathf.Lerp(-halfCanvas.y, halfCanvas.y, anchor.y));
            Vector2 local = anchorOffset + pos;
            local = new Vector2(
                Mathf.Clamp(local.x, -halfCanvas.x + halfPanel.x, halfCanvas.x - halfPanel.x),
                Mathf.Clamp(local.y, -halfCanvas.y + halfPanel.y, halfCanvas.y - halfPanel.y));
            return local - anchorOffset;
        }
    }
}
