using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Organic flame flicker for torch/brazier point lights using layered Perlin noise.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class TorchFlicker : MonoBehaviour
    {
        [SerializeField] private float baseIntensity = 2.2f;
        [SerializeField] private float flickerAmount = 0.6f;
        [SerializeField] private float flickerSpeed = 7f;

        private Light torchLight;
        private float noiseSeed;

        private void Awake()
        {
            torchLight = GetComponent<Light>();
            noiseSeed = Random.value * 100f;
        }

        public void Configure(float intensity, float amount, float speed)
        {
            baseIntensity = intensity;
            flickerAmount = amount;
            flickerSpeed = speed;
        }

        private void Update()
        {
            float t = Time.time * flickerSpeed + noiseSeed;
            // Two noise octaves: slow breathing + fast crackle
            float noise = Mathf.PerlinNoise(t * 0.35f, noiseSeed) * 0.7f
                        + Mathf.PerlinNoise(t, noiseSeed + 17f) * 0.3f;
            torchLight.intensity = baseIntensity + (noise - 0.5f) * 2f * flickerAmount;
        }
    }
}
