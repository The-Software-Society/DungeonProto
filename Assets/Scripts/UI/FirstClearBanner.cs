using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.UI
{
    /// <summary>
    /// Full-screen celebratory banner for the server-wide FIRST clear of a floor. Static
    /// Show() lazily builds its own top-most overlay canvas (independent of the HUD, so it
    /// fires even mid-cutscene) and plays a fade-in / hold / fade-up sequence. Driven on
    /// every client by ServerProgressState.RpcAnnounceFirstClear - the signature
    /// "the whole server shares one floor" moment.
    /// </summary>
    public class FirstClearBanner : MonoBehaviour
    {
        private static FirstClearBanner instance;

        public static void Show(string clearerName)
        {
            if (instance == null)
            {
                var go = new GameObject("FirstClearBanner");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<FirstClearBanner>();
                instance.Build();
            }
            instance.Play(clearerName);
        }

        private TextMeshProUGUI headline;
        private TextMeshProUGUI subline;
        private Coroutine playing;

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;   // above HUD + letterbox
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            headline = MakeText("Headline", 56, FontStyles.Bold, new Vector2(0, 44));
            headline.color = HexColor(UITheme.GoldHex);
            subline = MakeText("Subline", 26, FontStyles.Normal, new Vector2(0, -26));
            subline.color = Color.white;

            SetVisible(0f);
        }

        private TextMeshProUGUI MakeText(string label, float size, FontStyles style, Vector2 pos)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(transform, false);
            var t = obj.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color(0, 0, 0, 0.9f);
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.62f);
            rt.anchorMax = new Vector2(0.5f, 0.62f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200, 90);
            rt.anchoredPosition = pos;
            return t;
        }

        private void Play(string clearerName)
        {
            headline.text = "EMBERFALL HAS FALLEN";
            subline.text = $"{clearerName} is first to clear Floor 1. Hope spreads across the realm.";
            if (playing != null) StopCoroutine(playing);
            playing = StartCoroutine(Sequence());
        }

        private IEnumerator Sequence()
        {
            float t = 0f;
            while (t < 0.5f) { t += Time.deltaTime; SetVisible(Mathf.Clamp01(t / 0.5f)); yield return null; }
            SetVisible(1f);
            yield return new WaitForSeconds(4.5f);
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime;
                SetVisible(1f - t);
                if (headline != null) headline.rectTransform.anchoredPosition += Vector2.up * (30f * Time.deltaTime);
                yield return null;
            }
            SetVisible(0f);
            playing = null;
        }

        private void SetVisible(float a)
        {
            if (headline != null) headline.alpha = a;
            if (subline != null) subline.alpha = a;
        }

        private static Color HexColor(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
    }
}
