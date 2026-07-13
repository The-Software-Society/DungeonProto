using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// The volcano's smoke-and-ember column, a landmark readable from the city so
    /// the threat is always on the horizon. Runtime-built particle systems (same
    /// pattern as <see cref="EmberAtmosphere"/>): a wide dark smoke column rising
    /// from the crater plus sparse bright embers spitting above it. Purely visual;
    /// intensity scales with the world phase (calm smolder → erupting fountain).
    /// </summary>
    public class VolcanoPlume : MonoBehaviour
    {
        [SerializeField] private float smokeRateCalm = 40f;
        [SerializeField] private float smokeRateErupting = 140f;
        [SerializeField] private float emberRateCalm = 4f;
        [SerializeField] private float emberRateErupting = 40f;

        private ParticleSystem smokePS;
        private ParticleSystem emberPS;

        private void Start()
        {
            // Small, faint, numerous: Sprites/Default renders untextured QUADS, so big
            // opaque smoke particles read as floating grey boxes (QA regression). Many
            // small translucent quads blend into a soft column instead.
            smokePS = BuildColumn("PlumeSmoke",
                color: new Color(0.14f, 0.11f, 0.10f, 0.38f),
                sizeMin: 2.2f, sizeMax: 4.5f,
                riseMin: 3.5f, riseMax: 6f,
                lifetime: 22f,
                radius: 6f);

            emberPS = BuildColumn("PlumeEmbers",
                color: new Color(1f, 0.5f, 0.12f),
                sizeMin: 0.3f, sizeMax: 0.9f,
                riseMin: 9f, riseMax: 16f,
                lifetime: 5f,
                radius: 4f);

            if (EmberfallWorldState.Instance != null)
                EmberfallWorldState.Instance.OnPhaseChanged += ApplyPhase;
            ApplyPhase(EmberfallWorldState.Instance != null
                ? EmberfallWorldState.Instance.CurrentPhase
                : EmberfallWorldState.Phase.Calm);
        }

        private void OnDestroy()
        {
            if (EmberfallWorldState.Instance != null)
                EmberfallWorldState.Instance.OnPhaseChanged -= ApplyPhase;
        }

        private void ApplyPhase(EmberfallWorldState.Phase phase)
        {
            bool erupting = phase == EmberfallWorldState.Phase.Erupting;
            SetRate(smokePS, erupting ? smokeRateErupting : smokeRateCalm);
            SetRate(emberPS, erupting ? emberRateErupting : emberRateCalm);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }

        private ParticleSystem BuildColumn(string psName, Color color,
            float sizeMin, float sizeMax, float riseMin, float riseMax,
            float lifetime, float radius)
        {
            var go = new GameObject(psName);
            go.transform.SetParent(transform, false);
            // Rotate so startSpeed pushes particles UP out of the crater.
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(riseMin, riseMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = color;
            main.maxParticles = 900;
            main.playOnAwake = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            // Wind shear: the column drifts and widens as it climbs.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.6f;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.2f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);   // prevailing wind to the east
            vel.space = ParticleSystemSimulationSpace.World;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f),
                        new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { name = psName + "Mat" };
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.06f;   // distant column, never blocky foreground quads

            ps.Play();
            return ps;
        }
    }
}
