using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Purely visual falling projectile (Meteor Strike): a glowing orb streaks
    /// down from above onto a ground point, then bursts into a HitEffect + ground
    /// ring. Self-animating in Update, no coroutine, so the same call works on
    /// the local caster AND on a remote observer replaying the cast (where the
    /// PlayerAbilities component is disabled and cannot run coroutines).
    /// Never touches Health: damage is the caster's responsibility.
    /// </summary>
    public class FallingOrbEffect : MonoBehaviour
    {
        private Vector3 start;
        private Vector3 end;
        private float fallTime;
        private float radius;
        private Color color;
        private float age;
        private bool burst;
        private Light orbLight;

        public static FallingOrbEffect Create(Vector3 groundPoint, float radius, float fallTime, Color color)
        {
            var obj = new GameObject("FallingOrb");
            var effect = obj.AddComponent<FallingOrbEffect>();
            effect.end = groundPoint + Vector3.up * 0.4f;
            effect.start = groundPoint + Vector3.up * 20f;
            effect.fallTime = Mathf.Max(0.1f, fallTime);
            effect.radius = Mathf.Max(0.5f, radius);
            effect.color = color;
            obj.transform.position = effect.start;
            return effect;
        }

        private void Start()
        {
            // Telegraph the landing zone up front so the AoE reads before it lands.
            GroundRingEffect.Create(end - Vector3.up * 0.4f, radius, color);

            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "OrbCore";
            orb.transform.SetParent(transform, false);
            orb.transform.localPosition = Vector3.zero;
            orb.transform.localScale = Vector3.one * 0.9f;
            Destroy(orb.GetComponent<Collider>());

            var renderer = orb.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = color;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2f);
                renderer.material = mat;
            }

            orbLight = orb.AddComponent<Light>();
            orbLight.type = LightType.Point;
            orbLight.color = color;
            orbLight.range = 7f;
            orbLight.intensity = 3.5f;

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.28f;
            trail.startWidth = 0.5f;
            trail.endWidth = 0.02f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            var tail = color; tail.a = 0f;
            trail.startColor = color;
            trail.endColor = tail;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / fallTime);

            if (t >= 1f)
            {
                if (!burst)
                {
                    burst = true;
                    HitEffect.Create(end, color);
                    GroundRingEffect.Create(end - Vector3.up * 0.4f, radius, color);
                }
                Destroy(gameObject);
                return;
            }

            // Accelerating fall (ease-in) sells the plummet.
            transform.position = Vector3.Lerp(start, end, t * t);
        }
    }
}
