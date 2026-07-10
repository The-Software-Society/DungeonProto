using UnityEngine;
using UnityEngine.EventSystems;

namespace AldenFall.UI
{
    /// <summary>
    /// Hover grow + press punch for interactive UI elements.
    /// One MonoBehaviour per file — components added by editor build code must
    /// live in a file matching the class name or the scene loses the script
    /// on reload ("referenced script (Unknown) is missing").
    /// </summary>
    public class HoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        public float hoverScale = 1.07f;
        public float pressScale = 0.95f;
        public float duration = 0.12f;

        private Coroutine current;
        private bool hovered;

        public void OnPointerEnter(PointerEventData e) { hovered = true; Animate(hoverScale, true); }
        public void OnPointerExit(PointerEventData e) { hovered = false; Animate(1f, false); }
        public void OnPointerDown(PointerEventData e) => Animate(pressScale, false);
        public void OnPointerUp(PointerEventData e) => Animate(hovered ? hoverScale : 1f, true);

        private void Animate(float target, bool back)
        {
            if (current != null) StopCoroutine(current);
            current = StartCoroutine(UIFx.ScaleTo(transform, Vector3.one * target, duration, back));
        }

        private void OnDisable()
        {
            transform.localScale = Vector3.one;
        }
    }
}
