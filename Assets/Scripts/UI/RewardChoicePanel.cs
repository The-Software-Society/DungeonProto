using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AldenFall.Items;
using AldenFall.Quests;

namespace AldenFall.UI
{
    /// <summary>
    /// A generic "pick one reward" modal, raised on quest turn-in when a quest has
    /// RewardChoices (melee vs magic vs archery, per the build-fairness rule). Reuses
    /// the WeaponChoicePanel card look but is fully data-driven from QuestRewardOption.
    /// The chosen option's items are granted through the normal Inventory/LootDrop API
    /// (Grant is public + static so the functional QA test can exercise it without a
    /// mouse click). Local-player only. Built at runtime, no prefab.
    /// </summary>
    public class RewardChoicePanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private GameObject player;
        private Action onChosen;
        private CanvasGroup group;
        private bool resolved;

        /// <summary>Grant one reward option's items to the player (equip the first if requested).</summary>
        public static void Grant(GameObject player, QuestRewardOption option)
        {
            if (player == null || option == null) return;
            var inv = player.GetComponent<Inventory>();
            var equip = player.GetComponent<Equipment>();
            if (inv == null) return;

            bool equipped = false;
            foreach (var r in option.items)
            {
                for (int c = 0; c < Mathf.Max(1, r.count); c++)
                    LootDrop.GiveOrDrop(inv, r.itemId, player.transform);

                if (option.equip && !equipped && equip != null)
                {
                    var def = ItemDatabase.Get(r.itemId);
                    if (def != null && def.Slot != EquipSlot.None) { equip.Equip(r.itemId); equipped = true; }
                }
            }
        }

        public static RewardChoicePanel Present(GameObject player, string heading, List<QuestRewardOption> options, Action onChosen = null)
        {
            var host = new GameObject("RewardChoicePanel");
            var panel = host.AddComponent<RewardChoicePanel>();
            panel.player = player;
            panel.onChosen = onChosen;
            panel.Build(heading ?? "CHOOSE YOUR REWARD", options);
            return panel;
        }

        private static Color AccentFor(string style)
        {
            switch ((style ?? "melee").ToLowerInvariant())
            {
                case "magic": return UITheme.MagicBlue;
                case "archery": return new Color(0.45f, 0.8f, 0.4f);
                default: return UITheme.Gold;
            }
        }

        private void Build(string heading, List<QuestRewardOption> options)
        {
            IsOpen = true;
            EnsureEventSystem();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();

            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            var scrim = NewImage("Scrim", transform, new Color(0f, 0f, 0f, 0.62f));
            Stretch(scrim.rectTransform);

            var prompt = Label(transform, "Prompt", heading.ToUpperInvariant(),
                new Vector2(0, 292), new Vector2(1200, 56), 38, UITheme.TextGold);
            prompt.fontStyle = FontStyles.Bold;
            prompt.characterSpacing = 8;

            int n = options != null ? options.Count : 0;
            if (n == 0) { Resolve(); return; }

            float spread = 324f;
            float startX = -(n - 1) * spread * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var opt = options[i];
                var accent = AccentFor(opt.style);
                string sub = BuildItemSummary(opt);
                BuildCard(new Vector2(startX + i * spread, -32), accent,
                    string.IsNullOrEmpty(opt.label) ? opt.style.ToUpperInvariant() : opt.label.ToUpperInvariant(),
                    sub, opt.description, () => Choose(opt));
            }
        }

        private static string BuildItemSummary(QuestRewardOption opt)
        {
            if (opt.items == null || opt.items.Count == 0) return "";
            var parts = new List<string>();
            foreach (var r in opt.items)
            {
                var def = ItemDatabase.Get(r.itemId);
                string name = def != null ? def.Name : r.itemId;
                parts.Add(r.count > 1 ? $"{name} x{r.count}" : name);
            }
            return string.Join(", ", parts);
        }

        private void Choose(QuestRewardOption opt)
        {
            Grant(player, opt);
            Resolve();
        }

        private void Update()
        {
            if (group != null && group.alpha < 1f)
                group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime * 3f);
        }

        private void Resolve()
        {
            if (resolved) return;
            resolved = true;
            IsOpen = false;
            onChosen?.Invoke();
            Destroy(gameObject);
        }

        // ==================== CARDS + HELPERS (mirrors WeaponChoicePanel style) ====================

        private void BuildCard(Vector2 pos, Color accent, string title, string subtitle, string body, Action onClick)
        {
            var card = Rect(transform, "Card_" + title, new Vector2(0.5f, 0.5f), pos, new Vector2(300, 408));

            var keyline = NewImage("Keyline", card, new Color(accent.r, accent.g, accent.b, 0.45f));
            keyline.rectTransform.anchorMin = Vector2.zero; keyline.rectTransform.anchorMax = Vector2.one;
            keyline.rectTransform.offsetMin = new Vector2(-1, -1); keyline.rectTransform.offsetMax = new Vector2(1, 1);
            keyline.raycastTarget = false;

            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UITheme.PanelBgDeep;

            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.55f, 1.5f, 1.4f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            card.gameObject.AddComponent<HoverScale>().hoverScale = 1.02f;

            var bar = NewImage("Accent", card, accent);
            bar.rectTransform.anchorMin = new Vector2(0, 1); bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1); bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(0, 4); bar.raycastTarget = false;

            var titleLabel = Label(card, "Title", title, new Vector2(0, 138), new Vector2(280, 44), 24, accent);
            titleLabel.fontStyle = FontStyles.Bold; titleLabel.characterSpacing = 4;

            var subLabel = Label(card, "Subtitle", subtitle, new Vector2(0, 98), new Vector2(280, 40), 14, UITheme.TextGold);
            subLabel.textWrappingMode = TextWrappingModes.Normal;

            var bodyLabel = Label(card, "Body", body, new Vector2(0, -20), new Vector2(260, 200), 16, UITheme.TextMain);
            bodyLabel.alignment = TextAlignmentOptions.Top; bodyLabel.lineSpacing = 6;
            bodyLabel.textWrappingMode = TextWrappingModes.Normal;

            var hint = Label(card, "Hint", "CLICK TO CHOOSE", new Vector2(0, -178), new Vector2(280, 20), 12,
                new Color(accent.r, accent.g, accent.b, 0.85f));
            hint.fontStyle = FontStyles.Bold; hint.characterSpacing = 4;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var image = obj.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Label(Transform parent, string name, string content, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 0.5f), pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
