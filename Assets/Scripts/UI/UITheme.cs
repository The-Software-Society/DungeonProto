using UnityEngine;

namespace AldenFall.UI
{
    /// <summary>Single source of truth for the HUD's visual language.</summary>
    public static class UITheme
    {
        // Surfaces
        public static readonly Color PanelBg = new Color(0.07f, 0.063f, 0.10f, 0.92f);
        public static readonly Color PanelBgLight = new Color(0.115f, 0.10f, 0.155f, 0.92f);
        public static readonly Color PanelBorder = new Color(0.91f, 0.72f, 0.45f, 0.32f);
        public static readonly Color SlotBg = new Color(0.10f, 0.088f, 0.14f, 0.95f);
        public static readonly Color SlotBorder = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.45f);

        // Premium panel chrome (opaque modal surfaces + keylines)
        public static readonly Color PanelBgDeep = new Color(0.055f, 0.048f, 0.082f, 0.985f);
        public static readonly Color PanelBgDeepLight = new Color(0.095f, 0.083f, 0.13f, 0.985f);
        public static readonly Color Keyline = new Color(0.91f, 0.72f, 0.45f, 0.38f);
        public static readonly Color KeylineDim = new Color(0.91f, 0.72f, 0.45f, 0.18f);
        public static readonly Color HeaderRule = new Color(0.91f, 0.72f, 0.45f, 0.25f);
        public static readonly Color RowStripe = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color RowHover = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color DropShadow = new Color(0f, 0f, 0f, 0.55f);

        // Buttons
        public static readonly Color ButtonBg = new Color(0.145f, 0.125f, 0.20f, 1f);
        public static readonly Color ButtonHover = new Color(0.20f, 0.175f, 0.27f, 1f);
        public static readonly Color ButtonPressed = new Color(0.105f, 0.09f, 0.15f, 1f);
        public static readonly Color ButtonDisabledBg = new Color(0.115f, 0.11f, 0.14f, 1f);
        public static readonly Color TextDisabled = new Color(0.45f, 0.43f, 0.49f, 1f);

        // Panel typography scale (exactly three sizes inside panels)
        public const float TitleSize = 20f;
        public const float BodySize = 15f;
        public const float CaptionSize = 12f;
        public const float TitleTracking = 6f;   // small-caps style letter spacing

        // Padding grid, multiples of 8
        public const float Pad = 24f;
        public const float RowHeight = 40f;
        public const float ButtonHeight = 34f;

        // Accents
        public static readonly Color Gold = new Color(0.91f, 0.72f, 0.45f);
        public static readonly Color Ember = new Color(1f, 0.48f, 0.18f);
        public static readonly Color MagicBlue = new Color(0.36f, 0.62f, 1f);
        public static readonly Color HpGreen = new Color(0.35f, 0.84f, 0.55f);
        public static readonly Color HpRed = new Color(0.91f, 0.30f, 0.30f);
        public static readonly Color HpGhost = new Color(1f, 0.9f, 0.75f, 0.85f);

        // Text
        public static readonly Color TextMain = new Color(0.95f, 0.93f, 0.89f);
        public static readonly Color TextDim = new Color(0.65f, 0.61f, 0.69f);
        public static readonly Color TextGold = new Color(0.95f, 0.8f, 0.55f);

        // Feedback
        public static readonly Color DamageDealt = new Color(1f, 0.92f, 0.75f);
        public static readonly Color DamageTaken = new Color(1f, 0.36f, 0.32f);
        public static readonly Color XpText = new Color(0.72f, 0.85f, 1f);

        // Rich-text hex snippets kept consistent across every panel
        public const string GoldHex = "#F2CC8C";
        public const string DimHex = "#8A8494";
        public const string DangerHex = "#B06A6A";

        /// <summary>Consistent gold price string: "120g", dimmed red when unaffordable.</summary>
        public static string Price(int amount, bool affordable = true) =>
            $"<color={(affordable ? GoldHex : DangerHex)}>{amount}<size=80%>g</size></color>";

        public static Color HealthColor(float percent) =>
            Color.Lerp(HpRed, HpGreen, Mathf.Clamp01((percent - 0.15f) / 0.6f));
    }
}
