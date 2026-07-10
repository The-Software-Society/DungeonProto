using System.Collections.Generic;
using UnityEngine;
using TMPro;
using AldenFall.Combat;

namespace AldenFall.UI
{
    /// <summary>
    /// Pooled floating damage numbers: white-gold for damage you deal, red for
    /// damage you take. World position projected to the HUD canvas each frame.
    /// </summary>
    public class FloatingCombatText : MonoBehaviour
    {
        private class Entry
        {
            public TMP_Text text;
            public Vector3 worldPos;
            public float age;
            public float lifetime;
            public Vector2 drift;
        }

        private readonly List<Entry> active = new List<Entry>();
        private readonly Queue<TMP_Text> pool = new Queue<TMP_Text>();
        private Camera cam;
        private RectTransform canvasRect;

        private void Awake()
        {
            canvasRect = GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            CombatEvents.DamageNumber += OnDamage;
            CombatEvents.StatusText += OnStatus;
        }

        private void OnDisable()
        {
            CombatEvents.DamageNumber -= OnDamage;
            CombatEvents.StatusText -= OnStatus;
        }

        private void OnDamage(Vector3 worldPos, float amount, bool fromPlayer)
        {
            Spawn(worldPos,
                Mathf.Max(1, Mathf.RoundToInt(amount)).ToString(),
                fromPlayer ? UITheme.DamageDealt : UITheme.DamageTaken,
                fromPlayer ? 26 : 30,
                lifetime: 0.9f);
        }

        private void OnStatus(Vector3 worldPos, string message, Color color)
        {
            Spawn(worldPos, message, color, 28, lifetime: 1.1f);
        }

        private void Spawn(Vector3 worldPos, string message, Color color, float fontSize, float lifetime)
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            var text = pool.Count > 0 ? pool.Dequeue() : CreateText();
            text.gameObject.SetActive(true);
            text.text = message;
            text.color = color;
            text.fontSize = fontSize;

            active.Add(new Entry
            {
                text = text,
                worldPos = worldPos + Random.insideUnitSphere * 0.25f,
                lifetime = lifetime,
                drift = new Vector2(Random.Range(-24f, 24f), Random.Range(70f, 95f)),
            });
        }

        private TMP_Text CreateText()
        {
            var obj = new GameObject("DamageNumber");
            obj.transform.SetParent(transform, false);
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color(0, 0, 0, 0.85f);
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(160, 44);
            return text;
        }

        private void Update()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                e.age += Time.deltaTime;
                float t = e.age / e.lifetime;

                if (t >= 1f)
                {
                    e.text.gameObject.SetActive(false);
                    pool.Enqueue(e.text);
                    active.RemoveAt(i);
                    continue;
                }

                Vector3 screen = cam.WorldToScreenPoint(e.worldPos);
                if (screen.z < 0)
                {
                    e.text.alpha = 0f;
                    continue;
                }

                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screen, null, out Vector2 local);
                float ease = UIFx.EaseOut(t);
                e.text.rectTransform.anchoredPosition = local + e.drift * ease;
                e.text.alpha = 1f - Mathf.Pow(t, 2.2f);
                e.text.rectTransform.localScale = Vector3.one * (1f + 0.25f * (1f - ease));
            }
        }
    }
}
