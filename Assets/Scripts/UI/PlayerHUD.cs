using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DungeonProto.UI
{
    using Combat;
    using Player;

    public class PlayerHUD : MonoBehaviour
    {
        [Header("Player Health")]
        [SerializeField] private Image playerHealthFill;
        [SerializeField] private TextMeshProUGUI playerHealthText;
        [SerializeField] private Gradient playerHealthGradient;

        [Header("Target Info")]
        [SerializeField] private GameObject targetPanel;
        [SerializeField] private Image targetHealthFill;
        [SerializeField] private TextMeshProUGUI targetNameText;
        [SerializeField] private TextMeshProUGUI targetHealthText;

        [Header("Ability Cooldowns")]
        [SerializeField] private Image fireballCooldownFill;
        [SerializeField] private TextMeshProUGUI fireballKeyText;

        private Health playerHealth;
        private TargetingSystem targetingSystem;
        private Health currentTargetHealth;

        private float playerDisplayedHealth = 1f;
        private float targetDisplayedHealth = 1f;
        private const float SmoothSpeed = 8f;

        private void Start()
        {
            // Find player components
            var player = FindFirstObjectByType<PlayerController>();
            if (player != null)
            {
                playerHealth = player.GetComponent<Health>();
                targetingSystem = player.GetComponent<TargetingSystem>();
            }

            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged.AddListener(OnPlayerHealthChanged);
                UpdatePlayerHealth(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            }

            if (targetingSystem != null)
            {
                targetingSystem.OnTargetChanged.AddListener(OnTargetChanged);
            }

            // Hide target panel initially
            if (targetPanel != null)
                targetPanel.SetActive(false);

            // Set up default gradient
            if (playerHealthGradient == null || playerHealthGradient.colorKeys.Length == 0)
            {
                playerHealthGradient = CreateDefaultGradient();
            }
        }

        private void Update()
        {
            SmoothHealthBars();
        }

        private void SmoothHealthBars()
        {
            // Player health smoothing
            if (playerHealth != null && playerHealthFill != null)
            {
                float targetPercent = playerHealth.HealthPercent;
                playerDisplayedHealth = Mathf.Lerp(playerDisplayedHealth, targetPercent, SmoothSpeed * Time.deltaTime);
                playerHealthFill.fillAmount = playerDisplayedHealth;
                playerHealthFill.color = playerHealthGradient.Evaluate(playerDisplayedHealth);
            }

            // Target health smoothing
            if (currentTargetHealth != null && targetHealthFill != null)
            {
                float targetPercent = currentTargetHealth.HealthPercent;
                targetDisplayedHealth = Mathf.Lerp(targetDisplayedHealth, targetPercent, SmoothSpeed * Time.deltaTime);
                targetHealthFill.fillAmount = targetDisplayedHealth;
            }
        }

        private void OnPlayerHealthChanged(float current, float max)
        {
            UpdatePlayerHealth(current, max);
        }

        private void UpdatePlayerHealth(float current, float max)
        {
            if (playerHealthText != null)
            {
                playerHealthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
            }
        }

        private void OnTargetChanged(Transform newTarget)
        {
            if (newTarget == null)
            {
                ClearTarget();
                return;
            }

            // Unsubscribe from old target
            if (currentTargetHealth != null)
            {
                currentTargetHealth.OnHealthChanged.RemoveListener(OnTargetHealthChanged);
            }

            // Set up new target
            currentTargetHealth = newTarget.GetComponent<Health>();

            if (currentTargetHealth != null)
            {
                currentTargetHealth.OnHealthChanged.AddListener(OnTargetHealthChanged);

                if (targetPanel != null)
                    targetPanel.SetActive(true);

                if (targetNameText != null)
                    targetNameText.text = newTarget.name;

                targetDisplayedHealth = currentTargetHealth.HealthPercent;
                UpdateTargetHealth(currentTargetHealth.CurrentHealth, currentTargetHealth.MaxHealth);
            }
            else
            {
                ClearTarget();
            }
        }

        private void OnTargetHealthChanged(float current, float max)
        {
            UpdateTargetHealth(current, max);

            // Check if target died
            if (current <= 0)
            {
                ClearTarget();
            }
        }

        private void UpdateTargetHealth(float current, float max)
        {
            if (targetHealthText != null)
            {
                targetHealthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
            }
        }

        private void ClearTarget()
        {
            if (currentTargetHealth != null)
            {
                currentTargetHealth.OnHealthChanged.RemoveListener(OnTargetHealthChanged);
                currentTargetHealth = null;
            }

            if (targetPanel != null)
                targetPanel.SetActive(false);
        }

        private Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.8f, 0.2f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.8f, 0.6f, 0.2f), 0.5f),
                    new GradientColorKey(new Color(0.2f, 0.8f, 0.2f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                }
            );
            return gradient;
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged.RemoveListener(OnPlayerHealthChanged);
            }

            if (currentTargetHealth != null)
            {
                currentTargetHealth.OnHealthChanged.RemoveListener(OnTargetHealthChanged);
            }

            if (targetingSystem != null)
            {
                targetingSystem.OnTargetChanged.RemoveListener(OnTargetChanged);
            }
        }
    }
}
