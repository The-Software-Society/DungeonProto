using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Player;

namespace AldenFall.UI
{
    /// <summary>
    /// F10: character appearance panel — gender, skin tone, hair style, hair
    /// color. Runtime-built on its own canvas (same pattern as the cheat
    /// panel). Skin/hair apply live; gender applies next session.
    /// </summary>
    public class CharacterCustomizationUI : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F10;

        private CharacterAppearance appearance;
        private GameObject panelRoot;
        private TMP_Text genderLabel;
        private TMP_Text hairLabel;

        private void Awake()
        {
            appearance = GetComponent<CharacterAppearance>();
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(toggleKey))
            {
                if (panelRoot == null) BuildPanel();
                else panelRoot.SetActive(!panelRoot.activeSelf);
            }
        }

        private void BuildPanel()
        {
            var canvasObj = new GameObject("AppearanceCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 290;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            panelRoot = canvasObj;

            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 330));
            panel.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.05f, 0.09f, 0.96f);

            Label(panel, "Title", "APPEARANCE  <size=12><color=#8A8494>(F10 to close)</color></size>",
                new Vector2(0, -26), new Vector2(390, 30), 19, UITheme.TextGold);

            // ---- Gender ----
            genderLabel = Label(panel, "GenderLabel", "", new Vector2(-90, -74), new Vector2(180, 26), 15, UITheme.TextMain);
            Btn(panel, "Male", new Vector2(230, -74), 70, () => { appearance?.SetGender(0); RefreshLabels(); });
            Btn(panel, "Female", new Vector2(310, -74), 76, () => { appearance?.SetGender(1); RefreshLabels(); });
            Label(panel, "GenderNote", "<i>body applies next play session</i>", new Vector2(0, -98),
                new Vector2(390, 18), 11, UITheme.TextDim);

            // ---- Skin tone swatches ----
            Label(panel, "SkinLabel", "Skin", new Vector2(-160, -136), new Vector2(80, 26), 15, UITheme.TextMain);
            for (int i = 0; i < CharacterAppearance.SkinTones.Length; i++)
            {
                int captured = i;
                Swatch(panel, $"Skin{i}", CharacterAppearance.SkinTones[i], new Vector2(150 + i * 46, -136),
                    () => appearance?.SetSkinTone(captured));
            }

            // ---- Hair style ----
            hairLabel = Label(panel, "HairLabel", "", new Vector2(-120, -184), new Vector2(160, 26), 15, UITheme.TextMain);
            for (int i = 0; i < CharacterAppearance.HairStyleNames.Length; i++)
            {
                int captured = i;
                Btn(panel, CharacterAppearance.HairStyleNames[i], new Vector2(150 + i * 68, -184), 64,
                    () => { appearance?.SetHairStyle(captured); RefreshLabels(); });
            }

            // ---- Hair color swatches ----
            Label(panel, "HairColorLabel", "Hair color", new Vector2(-140, -232), new Vector2(120, 26), 15, UITheme.TextMain);
            for (int i = 0; i < CharacterAppearance.HairColors.Length; i++)
            {
                int captured = i;
                Swatch(panel, $"HairColor{i}", CharacterAppearance.HairColors[i], new Vector2(150 + i * 46, -232),
                    () => appearance?.SetHairColor(captured));
            }

            Label(panel, "Note", "<i>hair styles appear once base models are generated</i>",
                new Vector2(0, -286), new Vector2(390, 20), 11, UITheme.TextDim);

            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (appearance == null) return;
            if (genderLabel != null)
                genderLabel.text = $"Body   <b>{(appearance.Gender == 1 ? "Female" : "Male")}</b>";
            if (hairLabel != null)
                hairLabel.text = $"Hair   <b>{CharacterAppearance.HairStyleNames[appearance.HairStyle]}</b>";
        }

        // ---------- tiny UI builders (mirrors DebugCheatPanel) ----------

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Label(RectTransform parent, string name, string content,
            Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 1f), pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private void Btn(RectTransform parent, string label, Vector2 pos, float width,
            UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(parent, $"Btn_{label}", new Vector2(0f, 1f), pos, new Vector2(width, 30));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.22f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            var text = Label(rect, "Label", label, Vector2.zero, new Vector2(width, 30), 13, UITheme.TextMain);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = Vector2.zero;
        }

        private void Swatch(RectTransform parent, string name, Color color, Vector2 pos,
            UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(38, 30));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);
        }
    }
}
