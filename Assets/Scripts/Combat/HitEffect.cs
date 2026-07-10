using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Purely visual impact burst: flash light + emissive particle spray.
    /// Gameplay effects (damage, knockback) are the responsibility of whoever
    /// dealt the hit — this class must never touch Health.
    /// </summary>
    public class HitEffect : MonoBehaviour
    {
        [Header("Particle Settings")]
        [SerializeField] private int particleCount = 18;
        [SerializeField] private float particleSpeed = 12f;
        [SerializeField] private float particleSize = 0.3f;
        [SerializeField] private float duration = 0.55f;
        [SerializeField] private Color particleColor = new Color(1f, 0.6f, 0.2f, 1f);

        private GameObject[] particles;
        private Vector3[] particleVelocities;
        private float elapsedTime;
        private Light impactLight;
        private static Material sharedParticleBase;

        private void Start()
        {
            CreateImpactLight();
            SpawnParticles();
            Destroy(gameObject, duration + 0.1f);
        }

        private void CreateImpactLight()
        {
            var lightObj = new GameObject("ImpactLight");
            lightObj.transform.SetParent(transform);
            lightObj.transform.localPosition = Vector3.zero;

            impactLight = lightObj.AddComponent<Light>();
            impactLight.type = LightType.Point;
            impactLight.color = particleColor;
            impactLight.intensity = 6f;
            impactLight.range = 9f;
        }

        private void SpawnParticles()
        {
            particles = new GameObject[particleCount];
            particleVelocities = new Vector3[particleCount];

            for (int i = 0; i < particleCount; i++)
            {
                var particle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                particle.name = $"Particle_{i}";
                particle.transform.SetParent(transform);
                particle.transform.position = transform.position;
                particle.transform.localScale = Vector3.one * (particleSize * Random.Range(0.5f, 1.4f));

                Destroy(particle.GetComponent<Collider>());

                var renderer = particle.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var mat = new Material(renderer.sharedMaterial);
                    Color bright = particleColor * 1.5f;
                    bright.a = 1f;
                    mat.color = bright;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", bright);
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", particleColor * 2f);
                    renderer.material = mat;
                }

                Vector3 randomDir = Random.onUnitSphere;
                randomDir.y = Mathf.Abs(randomDir.y) * 0.3f + 0.3f; // upward bias
                particleVelocities[i] = randomDir * particleSpeed * Random.Range(0.6f, 1.4f);
                particles[i] = particle;
            }
        }

        private void Update()
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;

            if (impactLight != null)
                impactLight.intensity = Mathf.Max(0f, 6f * (1f - progress * 2f));

            if (particles == null) return;

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;

                particles[i].transform.position += particleVelocities[i] * Time.deltaTime;
                particleVelocities[i] += Vector3.down * 10f * Time.deltaTime;

                float scale = particleSize * (1f - progress * 0.85f);
                particles[i].transform.localScale = Vector3.one * Mathf.Max(scale, 0.04f);
            }
        }

        public static HitEffect Create(Vector3 position, Color color)
        {
            var effectObj = new GameObject("HitEffect");
            effectObj.transform.position = position;

            var effect = effectObj.AddComponent<HitEffect>();
            effect.particleColor = color;
            return effect;
        }

        public static HitEffect CreateFireballImpact(Vector3 position)
        {
            return Create(position, new Color(1f, 0.55f, 0.12f, 1f));
        }

        public static HitEffect CreateMeleeImpact(Vector3 position)
        {
            return Create(position, new Color(1f, 0.95f, 0.7f, 1f));
        }

        public static HitEffect CreateNovaImpact(Vector3 position)
        {
            return Create(position, new Color(0.45f, 0.7f, 1f, 1f));
        }
    }
}
