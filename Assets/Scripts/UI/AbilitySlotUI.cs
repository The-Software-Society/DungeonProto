using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.UI
{
    /// <summary>
    /// One ability slot: icon, keybind chip, radial cooldown sweep, cooldown
    /// countdown text, ready flash + idle glow pulse. Data comes from delegates
    /// assigned by the HUD so the slot stays purely visual.
    /// </summary>
    public class AbilitySlotUI : MonoBehaviour
    {
        public Image cooldownOverlay;    // radial 360 fill
        public TMP_Text cooldownText;
        public Image readyGlow;          // pulsing ring/glow when off cooldown
        public Image iconImage;

        public System.Func<float> getCooldownPercent;   // 1 = just used, 0 = ready
        public System.Func<float> getCooldownRemaining;

        private ReadyPulse pulse;
        private bool wasReady = true;

        private void Awake()
        {
            if (readyGlow != null)
            {
                pulse = gameObject.AddComponent<ReadyPulse>();
                pulse.target = readyGlow;
            }
        }

        private void Update()
        {
            if (getCooldownPercent == null) return;

            float percent = Mathf.Clamp01(getCooldownPercent());
            bool ready = percent <= 0.001f;

            if (cooldownOverlay != null)
                cooldownOverlay.fillAmount = percent;

            if (cooldownText != null)
            {
                float remaining = getCooldownRemaining?.Invoke() ?? 0f;
                cooldownText.text = remaining > 0.05f
                    ? (remaining >= 1f ? Mathf.CeilToInt(remaining).ToString() : remaining.ToString("0.0"))
                    : "";
            }

            if (iconImage != null)
                iconImage.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);

            if (pulse != null)
                pulse.active = ready;

            // Flash the moment we come off cooldown
            if (ready && !wasReady)
                StartCoroutine(ReadyFlash());
            wasReady = ready;
        }

        private System.Collections.IEnumerator ReadyFlash()
        {
            if (readyGlow == null) yield break;
            float elapsed = 0f;
            const float duration = 0.35f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (readyGlow == null) yield break;
                var c = readyGlow.color;
                c.a = Mathf.Lerp(0.9f, 0f, UIFx.EaseOut(elapsed / duration));
                readyGlow.color = c;
                yield return null;
            }
        }
    }
}
