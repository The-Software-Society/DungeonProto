using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.UI
{
    /// <summary>
    /// Shared chrome for runtime-built panels (vendor, forge, quest log, dev
    /// tester, weapon cards): layered framing (drop shadow, gold keyline, dark
    /// body, header rule), one title style, one button style with hover /
    /// pressed / disabled states, zebra list rows. All positions are top-left
    /// based (pivot 0,1) so layout code reads like coordinates, not offsets.
    /// Rounded sprites are pulled from Resources/UISprites when the generated
    /// set exists and gracefully fall back to flat rects when it does not.
    /// </summary>
    public static class PanelChrome
    {
        private static Sprite panelSprite, panelBorderSprite, slotSprite, barSprite;
        private static bool spritesLoaded;

        private static void EnsureSprites()
        {
            if (spritesLoaded) return;
            spritesLoaded = true;
            panelSprite = Resources.Load<Sprite>("UISprites/panel_r16");
            panelBorderSprite = Resources.Load<Sprite>("UISprites/panel_border_r16");
            slotSprite = Resources.Load<Sprite>("UISprites/slot_r10");
            barSprite = Resources.Load<Sprite>("UISprites/bar_r6");
        }

        // ==================== STRUCTURE ====================

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>Child rect anchored + pivoted to the parent's top-left; pos.y grows downward.</summary>
        public static RectTransform Item(RectTransform parent, string name, float x, float y,
            float w, float h) =>
            Rect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -y), new Vector2(w, h));

        public static Image Fill(RectTransform parent, string name, Color color,
            Sprite sprite = null, float pad = 0f)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(pad, pad);
            rect.offsetMax = new Vector2(-pad, -pad);
            var image = obj.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border.sqrMagnitude > 0)
                image.type = Image.Type.Sliced;
            return image;
        }

        /// <summary>
        /// Framed modal panel: offset drop shadow, gold keyline, near-opaque
        /// dark body. Children lay out inside the returned rect.
        /// </summary>
        public static RectTransform CreatePanel(Transform canvasRoot, string name, Vector2 size)
        {
            EnsureSprites();
            var panel = Rect(canvasRoot, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, size);

            // Drop shadow: dark copy offset down-right behind everything.
            var shadow = Fill(panel, "DropShadow", UITheme.DropShadow, panelSprite);
            shadow.rectTransform.offsetMin = new Vector2(-2f, -10f);
            shadow.rectTransform.offsetMax = new Vector2(10f, 2f);

            if (panelBorderSprite != null)
            {
                Fill(panel, "Body", UITheme.PanelBgDeep, panelSprite);
                Fill(panel, "Keyline", UITheme.Keyline, panelBorderSprite, pad: -1f);
            }
            else
            {
                // Flat fallback: gold rect 1px larger under the body reads as a keyline.
                Fill(panel, "Keyline", UITheme.Keyline, null, pad: -1f);
                Fill(panel, "Body", UITheme.PanelBgDeep);
            }

            // Faint inner top sheen for a gradient feel on flat bodies.
            var sheen = Fill(panel, "Sheen", new Color(1f, 1f, 1f, 0.025f), barSprite);
            sheen.rectTransform.anchorMin = new Vector2(0f, 1f);
            sheen.rectTransform.offsetMin = new Vector2(2f, -Mathf.Min(72f, size.y * 0.2f));
            sheen.rectTransform.offsetMax = new Vector2(-2f, -2f);
            return panel;
        }

        /// <summary>
        /// Standard header: tracked gold title top-left, optional dim caption
        /// top-right, thin gold rule underneath. Content starts below HeaderHeight.
        /// </summary>
        public const float HeaderHeight = 64f;

        public static TMP_Text Header(RectTransform panel, string title, string caption = null)
        {
            float w = panel.sizeDelta.x;
            var titleText = Text(panel, "Title", title.ToUpperInvariant(),
                UITheme.Pad, 16f, w * 0.62f, 28f,
                UITheme.TitleSize, UITheme.TextGold, TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold);
            titleText.characterSpacing = UITheme.TitleTracking;

            if (!string.IsNullOrEmpty(caption))
                Text(panel, "TitleCaption", caption,
                    w * 0.38f, 22f, w * 0.62f - UITheme.Pad, 18f,
                    UITheme.CaptionSize, UITheme.TextDim, TextAlignmentOptions.MidlineRight);

            var rule = Item(panel, "HeaderRule", UITheme.Pad, HeaderHeight - 10f,
                w - UITheme.Pad * 2f, 1f);
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = UITheme.HeaderRule;
            ruleImage.raycastTarget = false;
            return titleText;
        }

        /// <summary>Thin section rule + tracked caption label (for sub-headers inside a panel).</summary>
        public static TMP_Text Section(RectTransform parent, string name, string label,
            float x, float y, float w)
        {
            var text = Text(parent, name, label.ToUpperInvariant(), x, y, w, 18f,
                UITheme.CaptionSize, UITheme.TextGold, TextAlignmentOptions.MidlineLeft,
                FontStyles.Bold);
            text.characterSpacing = 3f;
            return text;
        }

        // ==================== TEXT ====================

        public static TMP_Text Text(RectTransform parent, string name, string content,
            float x, float y, float w, float h, float size, Color color,
            TextAlignmentOptions alignment, FontStyles style = FontStyles.Normal)
        {
            var rect = Item(parent, name, x, y, w, h);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        // ==================== BUTTONS ====================

        /// <summary>
        /// The one button style: 34 high, hover/pressed tints, tracked 15pt
        /// label, gold keyline on primary actions, proper greyed disabled state.
        /// </summary>
        public static Button MakeButton(RectTransform parent, string name, string label,
            float x, float y, float width, UnityEngine.Events.UnityAction onClick,
            bool primary = false)
        {
            EnsureSprites();
            var rect = Item(parent, name, x, y, width, UITheme.ButtonHeight);

            if (primary)
                Fill(rect, "Keyline", UITheme.Keyline,
                    panelBorderSprite != null ? slotSprite : null, pad: -1f);

            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = slotSprite;
            if (slotSprite != null && slotSprite.border.sqrMagnitude > 0)
                bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = UITheme.ButtonBg;
            colors.highlightedColor = UITheme.ButtonHover;
            colors.pressedColor = UITheme.ButtonPressed;
            colors.selectedColor = UITheme.ButtonBg;
            colors.disabledColor = UITheme.ButtonDisabledBg;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);

            var text = Text(rect, "Label", label, 0f, 0f, width, UITheme.ButtonHeight,
                UITheme.BodySize, primary ? UITheme.TextGold : UITheme.TextMain,
                TextAlignmentOptions.Center, FontStyles.Bold);
            text.characterSpacing = 1f;
            return button;
        }

        /// <summary>Disabled buttons grey out and stop reacting; label dims to match.</summary>
        public static void SetEnabled(Button button, bool enabled)
        {
            if (button == null) return;
            button.interactable = enabled;
            var label = button.transform.Find("Label");
            var text = label != null ? label.GetComponent<TMP_Text>() : null;
            if (text != null)
                text.color = enabled
                    ? (button.transform.Find("Keyline") != null ? UITheme.TextGold : UITheme.TextMain)
                    : UITheme.TextDisabled;
        }

        // ==================== LIST ROWS ====================

        /// <summary>
        /// Zebra list row: alternating faint stripe + hover highlight (a no-op
        /// selectable drives the tint; real buttons inside the row still win
        /// the raycast). Children lay out top-left inside the returned rect.
        /// </summary>
        public static RectTransform Row(RectTransform parent, string name, int index,
            float y, float width, float height = 0f)
        {
            EnsureSprites();
            if (height <= 0f) height = UITheme.RowHeight;
            var row = Item(parent, name, 0f, y, width, height);

            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = barSprite;
            if (barSprite != null && barSprite.border.sqrMagnitude > 0)
                bg.type = Image.Type.Sliced;
            bg.color = Color.white;
            bg.raycastTarget = true;

            var hover = row.gameObject.AddComponent<Button>();
            hover.targetGraphic = bg;
            hover.transition = Selectable.Transition.ColorTint;
            var colors = hover.colors;
            colors.normalColor = index % 2 == 0 ? Color.clear : UITheme.RowStripe;
            colors.highlightedColor = UITheme.RowHover;
            colors.pressedColor = UITheme.RowHover;
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = colors.normalColor;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            hover.colors = colors;
            hover.navigation = new Navigation { mode = Navigation.Mode.None };
            return row;
        }
    }
}
