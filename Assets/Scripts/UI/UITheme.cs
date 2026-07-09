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

        public static Color HealthColor(float percent) =>
            Color.Lerp(HpRed, HpGreen, Mathf.Clamp01((percent - 0.15f) / 0.6f));
    }
}
