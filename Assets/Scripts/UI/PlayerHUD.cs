using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.UI
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
        [SerializeField] private Image novaCooldownFill;

        [Header("Status Display")]
        [SerializeField] private Image dodgeCooldownFill;
        [SerializeField] private TextMeshProUGUI dodgeCooldownText;
        [SerializeField] private GameObject statusPanel;

        private Health playerHealth;
        private PlayerController playerController;
        private Abilities.PlayerAbilities playerAbilities;
        private TargetingSystem targetingSystem;
        private Health currentTargetHealth;

        private float playerDisplayedHealth = 1f;
        private float targetDisplayedHealth = 1f;
        private const float SmoothSpeed = 8f;

        private void Start()
        {
            // IMPORTANT: Create gradient FIRST before any code uses it
            if (playerHealthGradient == null || playerHealthGradient.colorKeys.Length == 0)
            {
                playerHealthGradient = CreateDefaultGradient();
            }

            // Find player components
            playerController = FindFirstObjectByType<PlayerController>();
            if (playerController != null)
            {
                playerHealth = playerController.GetComponent<Health>();
                targetingSystem = playerController.GetComponent<TargetingSystem>();
                playerAbilities = playerController.GetComponent<Abilities.PlayerAbilities>();
            }

            if (playerHealth != null)
            {
                playerHealth.OnHealthChanged.AddListener(OnPlayerHealthChanged);
                playerDisplayedHealth = playerHealth.HealthPercent;
                UpdatePlayerHealth(playerHealth.CurrentHealth, playerHealth.MaxHealth);

                // Initialize health bar visual immediately (gradient is now available)
                if (playerHealthFill != null)
                {
                    playerHealthFill.fillAmount = playerDisplayedHealth;
                    playerHealthFill.color = playerHealthGradient.Evaluate(playerDisplayedHealth);
                }
            }

            if (targetingSystem != null)
            {
                targetingSystem.OnTargetChanged.AddListener(OnTargetChanged);
            }

            // Hide target panel initially
            if (targetPanel != null)
                targetPanel.SetActive(false);
        }

        private void Update()
        {
            SmoothHealthBars();
            UpdateCooldownDisplays();
        }

        private void UpdateCooldownDisplays()
        {
            // Update dodge cooldown display
            if (playerController != null && dodgeCooldownFill != null)
            {
                float cooldownPercent = playerController.GetDodgeCooldownPercent();
                dodgeCooldownFill.fillAmount = cooldownPercent;

                if (dodgeCooldownText != null)
                {
                    if (cooldownPercent > 0.01f)
                    {
                        float remaining = playerController.GetDodgeCooldownRemaining();
                        dodgeCooldownText.text = remaining.ToString("F1");
                        dodgeCooldownText.gameObject.SetActive(true);
                    }
                    else
                    {
                        dodgeCooldownText.text = "";
                        dodgeCooldownText.gameObject.SetActive(false);
                    }
                }

                // Show/hide status panel based on any active cooldowns
                if (statusPanel != null)
                {
                    statusPanel.SetActive(cooldownPercent > 0.01f);
                }
            }

            // Update fireball cooldown display
            if (playerAbilities != null && fireballCooldownFill != null)
            {
                fireballCooldownFill.fillAmount = playerAbilities.GetCooldown01("fireball");
            }

            // Update nova cooldown display
            if (playerAbilities != null && novaCooldownFill != null)
            {
                novaCooldownFill.fillAmount = playerAbilities.GetCooldown01("ember_nova");
            }
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

            // Unsubscribe from old target (check gameObject to handle destroyed Unity objects)
            if (currentTargetHealth != null && currentTargetHealth.gameObject != null)
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
            // Check gameObject to handle destroyed Unity objects (prevents NullReferenceException)
            if (currentTargetHealth != null && currentTargetHealth.gameObject != null)
            {
                currentTargetHealth.OnHealthChanged.RemoveListener(OnTargetHealthChanged);
            }
            currentTargetHealth = null;

            if (targetPanel != null)
                targetPanel.SetActive(false);
        }

        private Gradient CreateDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(0.9f, 0.15f, 0.15f), 0f),      // Bright red at 0%
                    new GradientColorKey(new Color(0.95f, 0.6f, 0.1f), 0.33f),    // Orange at 33%
                    new GradientColorKey(new Color(0.9f, 0.85f, 0.1f), 0.5f),     // Yellow at 50%
                    new GradientColorKey(new Color(0.5f, 0.9f, 0.2f), 0.66f),     // Yellow-green at 66%
                    new GradientColorKey(new Color(0.2f, 0.85f, 0.2f), 1f)        // Bright green at 100%
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
