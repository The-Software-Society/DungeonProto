using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Combat;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// Testing cheatsheet: F9 toggles a panel that edits every skill level
    /// directly (−1 / +1 / +10 / 99), plus full heal and god mode. Built
    /// entirely at runtime on its own canvas so it never touches the real HUD
    /// build. Lives on the player.
    /// </summary>
    public class DebugCheatPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F9;

        private Skills skills;
        private Health health;
        private GameObject panelRoot;
        private bool godMode;
        private readonly System.Collections.Generic.Dictionary<SkillType, TMP_Text> levelLabels
            = new System.Collections.Generic.Dictionary<SkillType, TMP_Text>();
        private TMP_Text godLabel;

        private void Awake()
        {
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(toggleKey))
                Toggle();

            if (panelRoot != null && panelRoot.activeSelf)
                RefreshLabels();
        }

        private void Toggle()
        {
            if (panelRoot == null)
                BuildPanel();
            else
                panelRoot.SetActive(!panelRoot.activeSelf);
        }

        private void RefreshLabels()
        {
            if (skills == null) return;
            foreach (var (skill, label) in levelLabels)
                if (label != null)
                    label.text = $"{Skills.DisplayName(skill)}  <b>{skills.Level(skill)}</b>";
        }

        // ==================== RUNTIME UI ====================

        private void BuildPanel()
        {
            var canvasObj = new GameObject("DebugCheatCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300; // above everything
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();

            panelRoot = canvasObj;

            int skillCount = System.Enum.GetValues(typeof(SkillType)).Length;
            float panelH = 96f + skillCount * 44f + 60f;
            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(430, panelH));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.05f, 0.09f, 0.96f);

            MakeText(panel, "Title", "CHEATS  <size=12><color=#8A8494>(F9 to close — testing only)</color></size>",
                new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(400, 30), 19, UITheme.TextGold, TextAlignmentOptions.Center);

            float y = -70f;
            foreach (SkillType skill in System.Enum.GetValues(typeof(SkillType)))
            {
                var captured = skill; // avoid foreach-variable capture surprises
                var label = MakeText(panel, $"Lv_{skill}", "", new Vector2(0f, 1f),
                    new Vector2(110, y), new Vector2(180, 30), 15, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                levelLabels[skill] = label;

                MakeButton(panel, $"{skill}_minus", "−1", new Vector2(212, y), 44,
                    () => skills?.SetLevel(captured, skills.Level(captured) - 1));
                MakeButton(panel, $"{skill}_plus", "+1", new Vector2(262, y), 44,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 1));
                MakeButton(panel, $"{skill}_plus10", "+10", new Vector2(312, y), 44,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 10));
                MakeButton(panel, $"{skill}_max", "99", new Vector2(362, y), 44,
                    () => skills?.SetLevel(captured, Skills.MaxLevel));

                y -= 44f;
            }

            y -= 10f;
            MakeButton(panel, "AllMax", "ALL 99", new Vector2(85, y), 90, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, Skills.MaxLevel);
            });
            MakeButton(panel, "AllReset", "ALL 1", new Vector2(180, y), 90, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, 1);
            });
            MakeButton(panel, "Heal", "HEAL", new Vector2(275, y), 90, () =>
            {
                health?.ResetHealth();
            });
            godLabel = MakeButton(panel, "God", "GOD: OFF", new Vector2(370, y), 90, () =>
            {
                godMode = !godMode;
                health?.SetInvulnerable(godMode);
                if (godLabel != null)
                    godLabel.text = godMode ? "GOD: ON" : "GOD: OFF";
            });

            RefreshLabels();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string content,
            Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, Color color,
            TextAlignmentOptions alignment)
        {
            var rect = Rect(parent, name, anchor, pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private TMP_Text MakeButton(RectTransform parent, string name, string label,
            Vector2 pos, float width, UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(width, 32));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.22f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            return MakeText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, 32), 13, UITheme.TextMain, TextAlignmentOptions.Center);
        }
    }
}
