using UnityEngine;
using UnityEngine.UI;

namespace DungeonProto.UI
{
    using Combat;

    public class HealthBar : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Health targetHealth;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Gradient healthGradient;

        [Header("Settings")]
        [SerializeField] private bool hideWhenFull = false;
        [SerializeField] private float smoothSpeed = 5f;

        [Header("World Space Settings")]
        [SerializeField] private bool faceCamera = true;
        [SerializeField] private Vector3 offset = new Vector3(0, 2f, 0);

        private float displayedHealth;
        private Canvas canvas;
        private Transform cameraTransform;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();

            if (targetHealth == null)
                targetHealth = GetComponentInParent<Health>();

            // Set up default gradient if not assigned
            if (healthGradient == null || healthGradient.colorKeys.Length == 0)
            {
                healthGradient = new Gradient();
                healthGradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(Color.red, 0f),
                        new GradientColorKey(Color.yellow, 0.5f),
                        new GradientColorKey(Color.green, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );
            }
        }

        private void Start()
        {
            cameraTransform = Camera.main?.transform;

            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.AddListener(OnHealthChanged);
                displayedHealth = targetHealth.HealthPercent;
                UpdateBar(displayedHealth);
            }
        }

        private void OnDestroy()
        {
            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.RemoveListener(OnHealthChanged);
            }
        }

        private void LateUpdate()
        {
            // Smooth health bar movement
            if (targetHealth != null)
            {
                float targetPercent = targetHealth.HealthPercent;
                displayedHealth = Mathf.Lerp(displayedHealth, targetPercent, smoothSpeed * Time.deltaTime);
                UpdateBar(displayedHealth);
            }

            // Face camera if world space
            if (faceCamera && cameraTransform != null && canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cameraTransform.position);
            }

            // Follow target position
            if (targetHealth != null && canvas != null && canvas.renderMode == RenderMode.WorldSpace)
            {
                transform.position = targetHealth.transform.position + offset;
            }
        }

        private void OnHealthChanged(float current, float max)
        {
            // Event-based update (immediate response)
            if (hideWhenFull)
            {
                bool shouldShow = current < max;
                if (fillImage != null) fillImage.gameObject.SetActive(shouldShow);
                if (backgroundImage != null) backgroundImage.gameObject.SetActive(shouldShow);
            }
        }

        private void UpdateBar(float percent)
        {
            if (fillImage != null)
            {
                fillImage.fillAmount = percent;
                fillImage.color = healthGradient.Evaluate(percent);
            }
        }

        public void SetTarget(Health newTarget)
        {
            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.RemoveListener(OnHealthChanged);
            }

            targetHealth = newTarget;

            if (targetHealth != null)
            {
                targetHealth.OnHealthChanged.AddListener(OnHealthChanged);
                displayedHealth = targetHealth.HealthPercent;
                UpdateBar(displayedHealth);
            }
        }
    }
}
