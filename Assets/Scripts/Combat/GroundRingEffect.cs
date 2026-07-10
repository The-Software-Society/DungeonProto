using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// AoE telegraph: a glowing ring on the ground showing an ability's true
    /// radius, expanding slightly and fading out. Purely visual.
    /// </summary>
    public class GroundRingEffect : MonoBehaviour
    {
        private const int Segments = 48;
        private const float Lifetime = 0.5f;

        private LineRenderer line;
        private float radius;
        private Color color;
        private float age;

        public static GroundRingEffect Create(Vector3 center, float radius, Color color)
        {
            var obj = new GameObject("GroundRing");
            obj.transform.position = new Vector3(center.x, center.y + 0.07f, center.z);
            var ring = obj.AddComponent<GroundRingEffect>();
            ring.radius = Mathf.Max(0.5f, radius);
            ring.color = color;
            return ring;
        }

        private void Start()
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.positionCount = Segments;
            line.startWidth = 0.16f;
            line.endWidth = 0.16f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.startColor = color;
            line.endColor = color;
            SetRadius(radius * 0.88f);
            Destroy(gameObject, Lifetime + 0.05f);
        }

        private void SetRadius(float r)
        {
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / Lifetime);

            // Snap out to the true radius, then fade
            SetRadius(radius * Mathf.Lerp(0.88f, 1f, Mathf.Min(1f, t * 3f)));
            var c = color;
            c.a = 1f - t * t;
            line.startColor = c;
            line.endColor = c;
        }
    }
}
