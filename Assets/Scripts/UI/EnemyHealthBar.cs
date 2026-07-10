using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.UI
{
    using Combat;

    public class EnemyHealthBar : MonoBehaviour
    {
        [SerializeField] private Health targetHealth;
        [SerializeField] private Vector3 offset = new Vector3(0, 2.5f, 0);
        [SerializeField] private float visibilityRange = 15f;
        [SerializeField] private bool alwaysVisible = false;

        private Canvas canvas;
        private Image healthBarFill;
        private Image healthBarBackground;
        private Transform cameraTransform;
        private RectTransform canvasRect;
        private RectTransform fillRect;
        private int colorRefreshFrames = 3; // Force color refresh for first few frames

        private StatusSystem statuses;
        private TMP_Text stunStar;

        private void Start()
        {
            if (targetHealth == null)
                targetHealth = GetComponentInParent<Health>();
            statuses = GetComponentInParent<StatusSystem>();

            cameraTransform = Camera.main?.transform;

            CreateHealthBarUI();

            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.AddListener(OnHealthChanged);
                targetHealth.OnDeath.AddListener(OnTargetDeath);
            }

            if (statuses != null)
                statuses.OnApplied += OnStatusApplied;
        }

        private void OnStatusApplied(StatusEffect effect)
        {
            if ((effect.Flags & StatusFlags.Stunned) != 0)
                CombatEvents.RaiseStatus(transform.position + offset, "Stunned!", UITheme.Gold);
        }

        private void OnHealthChanged(float current, float max)
        {
            UpdateHealthBar();
        }

        private void CreateHealthBarUI()
        {
            // Create world-space canvas
            var canvasObj = new GameObject("EnemyHealthBarCanvas");
            canvasObj.transform.SetParent(transform);
            canvasObj.transform.localPosition = offset;

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1f, 0.15f);
            canvasRect.localScale = Vector3.one * 0.01f;

            // Background
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            healthBarBackground = bgObj.AddComponent<Image>();
            healthBarBackground.raycastTarget = false; // Disable raycast for world space UI
            healthBarBackground.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            var bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Fill
            var fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(canvasObj.transform, false);
            healthBarFill = fillObj.AddComponent<Image>();
            healthBarFill.raycastTarget = false; // Disable raycast for world space UI
            fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.pivot = new Vector2(0, 0.5f);
            fillRect.offsetMin = new Vector2(2, 2);
            fillRect.offsetMax = new Vector2(-2, -2);

            // Force initial color (must happen after RectTransform setup)
            healthBarFill.color = Color.green;

            // Stun star: spins above the bar while the mob is stunned
            var starObj = new GameObject("StunStar");
            starObj.transform.SetParent(canvas.transform, false);
            stunStar = starObj.AddComponent<TextMeshProUGUI>();
            stunStar.text = "✦";
            stunStar.fontSize = 36;
            stunStar.color = UITheme.Gold;
            stunStar.alignment = TextAlignmentOptions.Center;
            stunStar.raycastTarget = false;
            var starRect = stunStar.rectTransform;
            starRect.sizeDelta = new Vector2(40, 40);
            starRect.anchoredPosition = new Vector2(0, 34);
            starObj.SetActive(false);

            // Note: Border removed - was causing white overlay at runtime
            // The dark background provides sufficient contrast

            UpdateHealthBar();
        }

        private void LateUpdate()
        {
            if (canvas == null || cameraTransform == null) return;

            // Always update color every frame to ensure it's applied
            UpdateHealthBar();

            // Face camera (billboard)
            canvas.transform.rotation = Quaternion.LookRotation(
                canvas.transform.position - cameraTransform.position
            );

            // Visibility based on distance
            if (!alwaysVisible)
            {
                float distance = Vector3.Distance(transform.position, cameraTransform.position);
                canvas.enabled = distance <= visibilityRange;
            }

            if (stunStar != null && statuses != null)
            {
                bool stunned = statuses.IsStunned;
                if (stunStar.gameObject.activeSelf != stunned)
                    stunStar.gameObject.SetActive(stunned);
                if (stunned)
                    stunStar.rectTransform.Rotate(0f, 0f, 220f * Time.deltaTime);
            }
        }

        private void UpdateHealthBar()
        {
            if (healthBarFill == null || targetHealth == null || fillRect == null) return;

            float healthPercent = Mathf.Clamp01(targetHealth.CurrentHealth / targetHealth.MaxHealth);
            fillRect.anchorMax = new Vector2(healthPercent, 1);

            // Color gradient: red (0%) -> yellow (50%) -> green (100%)
            // Using fully saturated colors for visibility
            Color targetColor;
            if (healthPercent > 0.5f)
            {
                // Yellow to Green (50% to 100%)
                float t = (healthPercent - 0.5f) * 2f;
                targetColor = new Color(1f - t * 0.8f, 1f, 0.2f, 1f); // Yellow to Green
            }
            else
            {
                // Red to Yellow (0% to 50%)
                float t = healthPercent * 2f;
                targetColor = new Color(1f, t, 0.2f, 1f); // Red to Yellow
            }

            // Force color assignment
            healthBarFill.color = targetColor;
        }

        private void OnTargetDeath()
        {
            if (canvas != null)
                canvas.enabled = false;
        }

        private void OnDestroy()
        {
            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.RemoveListener(OnHealthChanged);
                targetHealth.OnDeath.RemoveListener(OnTargetDeath);
            }
            if (statuses != null)
                statuses.OnApplied -= OnStatusApplied;
        }
    }
}
