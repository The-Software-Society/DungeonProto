using System.Collections;
using UnityEngine;

namespace AldenFall.UI
{
    /// <summary>
    /// Lightweight UI micro-animation helpers (no external tween library).
    /// All easing is exponential ease-out for a crisp, expensive feel.
    /// The component classes (HoverScale, ReadyPulse, SlidePanel) live in
    /// their own files — MonoBehaviours in a mismatched file lose their
    /// script reference when a built scene reloads.
    /// </summary>
    public static class UIFx
    {
        public static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        public static IEnumerator ScaleTo(Transform t, Vector3 target, float duration, bool back = false)
        {
            Vector3 start = t.localScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = back ? EaseOutBack(elapsed / duration) : EaseOut(elapsed / duration);
                if (t == null) yield break;
                t.localScale = Vector3.LerpUnclamped(start, target, k);
                yield return null;
            }
            if (t != null) t.localScale = target;
        }

        public static IEnumerator FadeTo(CanvasGroup group, float target, float duration)
        {
            float start = group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (group == null) yield break;
                group.alpha = Mathf.Lerp(start, target, EaseOut(elapsed / duration));
                yield return null;
            }
            if (group != null) group.alpha = target;
        }

        public static IEnumerator MoveAnchored(RectTransform rect, Vector2 target, float duration, bool back = false)
        {
            Vector2 start = rect.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = back ? EaseOutBack(elapsed / duration) : EaseOut(elapsed / duration);
                if (rect == null) yield break;
                rect.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
                yield return null;
            }
            if (rect != null) rect.anchoredPosition = target;
        }
    }
}
