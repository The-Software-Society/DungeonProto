using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Client-side visual controller for the "ember-fall" atmosphere: falling ember
    /// and soot particles over the camera, ashen/red fog, and a hellish tint on the
    /// scene's sun + ambient light. One instance owns the whole look; both the world
    /// (approaching the volcano) and the world-state (the eruption climax) just push
    /// it an intensity in 0..1.
    ///
    /// Intensity has two independent sources that are blended by MAX so neither can
    /// cancel the other:
    ///   - GLOBAL  , set by <see cref="EmberfallWorldState"/> when the world flips
    ///                into the eruption phase (whole map goes apocalyptic).
    ///   - LOCAL   , the strongest <see cref="EmberZone"/> the local player is
    ///                standing in (a positional gradient darkening toward the volcano).
    /// effective = max(global, strongest active zone). The displayed intensity eases
    /// toward that target every frame so transitions never pop.
    ///
    /// Purely cosmetic and NON-networked: every client runs its own copy driven by the
    /// (networked) world-state phase and its own local player's position. At intensity 0
    /// it restores the calm baseline captured on Awake, so a cleared world looks lush again.
    /// </summary>
    public class EmberAtmosphere : MonoBehaviour
    {
        /// <summary>Scene singleton. Created/owned in the scene by world-gen.</summary>
        public static EmberAtmosphere Instance { get; private set; }

        [Header("Blend")]
        [Tooltip("How fast the displayed intensity eases toward the target (per second).")]
        [SerializeField] private float easeSpeed = 1.5f;

        [Header("Particles")]
        [Tooltip("Emission rate of drifting embers at full intensity.")]
        [SerializeField] private float maxEmberRate = 160f;
        [Tooltip("Emission rate of dark soot flecks at full intensity.")]
        [SerializeField] private float maxSootRate = 110f;
        [Tooltip("Half-width of the square patch of sky the particles rain from, centred on the camera.")]
        [SerializeField] private float fallFieldRadius = 26f;
        [Tooltip("Height above the tracked camera the particles spawn at.")]
        [SerializeField] private float fallFieldHeight = 20f;

        [Header("Fog")]
        [Tooltip("Exponential fog density at full intensity (thick ash haze).")]
        [SerializeField] private float maxFogDensity = 0.045f;

        [Header("Lighting")]
        [Tooltip("Scene sun. Left null it is resolved via FindObjectOfType at Awake (first directional Light).")]
        [SerializeField] private Light sunLight;
        [Tooltip("Fraction of the sun's calm intensity that remains at full apocalypse (dimmed + reddened).")]
        [SerializeField] private float sunDimAtFull = 0.5f;

        // --- apocalypse look targets (intensity == 1) ---
        private static readonly Color AshFogColor = new Color(0.30f, 0.11f, 0.07f);
        private static readonly Color HellAmbient = new Color(0.34f, 0.13f, 0.09f);
        private static readonly Color HellAmbientEquator = new Color(0.26f, 0.10f, 0.07f);
        private static readonly Color HellAmbientGround = new Color(0.16f, 0.06f, 0.05f);
        private static readonly Color HellSun    = new Color(1.00f, 0.42f, 0.18f);
        private static readonly Color HellSkyTint = new Color(0.55f, 0.12f, 0.05f);

        // --- calm baseline, captured on Awake so intensity 0 restores it exactly ---
        private bool baseFog;
        private Color baseFogColor;
        private float baseFogDensity;
        private FogMode baseFogMode;
        private Color baseAmbient;
        private Color baseAmbientEquator;
        private Color baseAmbientGround;
        private Color baseSunColor;
        private float baseSunIntensity;
        private bool baselineCaptured;

        // --- skybox: we tint a runtime COPY of the shared skybox material so the
        // sky itself goes hellish during the eruption (fog alone never touches it) ---
        private Material skyboxRuntime;
        private Color baseSkyTint;
        private float baseSkyExposure;
        private float baseSkyThickness;
        private bool skyboxDriven;

        // --- intensity state ---
        private float globalIntensity;      // set by world-state
        private float smoothedIntensity;    // displayed value, eases toward the target
        private readonly HashSet<EmberZone> activeZones = new HashSet<EmberZone>();

        // --- runtime-built particle systems ---
        private ParticleSystem emberPS;
        private ParticleSystem sootPS;
        private Transform trackedCamera;

        /// <summary>The displayed intensity right now (0 = calm, 1 = full apocalypse).</summary>
        public float CurrentIntensity => smoothedIntensity;

        /// <summary>The intensity we are easing toward: max(global, strongest active zone).</summary>
        public float TargetIntensity => ComputeTarget();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            CaptureBaseline();
            BuildParticles();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                RestoreBaseline();
                Instance = null;
            }
        }

        // ---------------------------------------------------------------- public API

        /// <summary>
        /// Set the GLOBAL intensity (0 = lush calm, 1 = full apocalypse). Called by the
        /// networked world-state on phase changes; blended by MAX with any local zone.
        /// </summary>
        public void SetGlobalIntensity(float t)
        {
            globalIntensity = Mathf.Clamp01(t);
        }

        // ------------------------------------------------------- zone registration

        /// <summary>An EmberZone reports that the local player entered it. While registered
        /// its <c>ZoneIntensity</c> contributes to the local target (strongest wins).</summary>
        internal void RegisterZone(EmberZone zone)
        {
            if (zone != null) activeZones.Add(zone);
        }

        /// <summary>An EmberZone reports that the local player left it (or it was destroyed).</summary>
        internal void UnregisterZone(EmberZone zone)
        {
            if (zone != null) activeZones.Remove(zone);
        }

        private float ComputeTarget()
        {
            float local = 0f;
            // Strongest active zone wins; prune any that vanished from under us.
            activeZones.RemoveWhere(z => z == null);
            foreach (var z in activeZones)
                if (z.ZoneIntensity > local) local = z.ZoneIntensity;

            return Mathf.Max(globalIntensity, local);
        }

        // ------------------------------------------------------------------- update

        private void Update()
        {
            // Ease the displayed intensity toward the blended target (no popping).
            float target = ComputeTarget();
            smoothedIntensity = Mathf.MoveTowards(
                smoothedIntensity, target, easeSpeed * Time.deltaTime);

            ApplyParticles(smoothedIntensity);
            ApplyFog(smoothedIntensity);
            ApplyLighting(smoothedIntensity);
        }

        // --------------------------------------------------------------- particles

        private void BuildParticles()
        {
            // Bright, additive-looking embers that glow orange and fade as they fall.
            emberPS = CreateFallSystem(
                "EmberFall",
                startColor: new Color(1f, 0.55f, 0.15f),
                sizeMin: 0.06f, sizeMax: 0.16f,
                fallMin: 2.5f, fallMax: 5.5f,
                lifetime: 5.5f,
                drift: 0.9f);

            // Dark soot flecks that sink slower and read as falling ash.
            sootPS = CreateFallSystem(
                "SootFall",
                startColor: new Color(0.10f, 0.09f, 0.09f),
                sizeMin: 0.05f, sizeMax: 0.12f,
                fallMin: 1.2f, fallMax: 3.0f,
                lifetime: 7f,
                drift: 1.3f);
        }

        /// <summary>
        /// Build one downward-raining ParticleSystem in WORLD space. It is repositioned
        /// above the tracked camera each frame so the field of falling particles always
        /// surrounds the viewer while individual particles keep drifting in world space.
        /// </summary>
        private ParticleSystem CreateFallSystem(
            string psName, Color startColor,
            float sizeMin, float sizeMax,
            float fallMin, float fallMax,
            float lifetime, float drift)
        {
            var go = new GameObject(psName);
            go.transform.SetParent(transform, worldPositionStays: false);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(fallMin, fallMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = startColor;
            main.gravityModifier = 0f;               // fall speed comes from startSpeed + shape
            main.maxParticles = 1400;
            main.playOnAwake = false;

            // Emit downward from a flat square patch of "sky" above the camera.
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(fallFieldRadius * 2f, 0.1f, fallFieldRadius * 2f);
            shape.rotation = new Vector3(90f, 0f, 0f);   // point emission cone straight down

            // Emission starts at zero; ApplyParticles scales it by intensity each frame.
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            // Gentle sideways wander so the fall never looks like straight rain.
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = drift;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.3f;

            // Fade each particle out over its life so nothing pops when it despawns.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Sprites/Default is the shader the rest of the project relies on for
            // runtime-built line/trail visuals, it survives build shader stripping.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { name = psName + "Mat" };
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = -10f;   // draw in front of most world geometry
            // A particle drifting right past the lens must never blow up into a
            // screen-corner square, clamp billboard size to ~2% of the viewport
            // (Unity's default is 50%).
            renderer.maxParticleSize = 0.02f;

            ps.Play();
            return ps;
        }

        private void ApplyParticles(float t)
        {
            // Keep the fall field centred over the camera so it always surrounds the viewer.
            if (trackedCamera == null && Camera.main != null)
                trackedCamera = Camera.main.transform;
            if (trackedCamera != null)
            {
                Vector3 above = trackedCamera.position + Vector3.up * fallFieldHeight;
                if (emberPS != null) emberPS.transform.position = above;
                if (sootPS != null) sootPS.transform.position = above;
            }

            if (emberPS != null)
            {
                var e = emberPS.emission;
                e.rateOverTime = t * maxEmberRate;
            }
            if (sootPS != null)
            {
                var e = sootPS.emission;
                e.rateOverTime = t * maxSootRate;
            }
        }

        // --------------------------------------------------------------------- fog

        private void ApplyFog(float t)
        {
            bool wantFog = baseFog || t > 0.02f;
            RenderSettings.fog = wantFog;
            RenderSettings.fogMode = t > 0.02f ? FogMode.ExponentialSquared : baseFogMode;
            RenderSettings.fogColor = Color.Lerp(baseFogColor, AshFogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(baseFogDensity, maxFogDensity, t);

            // The scene uses Trilight ambient: drive all three channels or the ground
            // stays calmly lit while the sky burns.
            RenderSettings.ambientSkyColor = Color.Lerp(baseAmbient, HellAmbient, t);
            RenderSettings.ambientEquatorColor = Color.Lerp(baseAmbientEquator, HellAmbientEquator, t);
            RenderSettings.ambientGroundColor = Color.Lerp(baseAmbientGround, HellAmbientGround, t);

            ApplySkybox(t);
        }

        // The procedural skybox ignores fog entirely, without this the horizon stays
        // cheerful blue while the world burns (exactly what playtests showed).
        private void ApplySkybox(float t)
        {
            if (!skyboxDriven) return;
            skyboxRuntime.SetColor("_SkyTint", Color.Lerp(baseSkyTint, HellSkyTint, t));
            skyboxRuntime.SetFloat("_Exposure", Mathf.Lerp(baseSkyExposure, baseSkyExposure * 0.55f, t));
            skyboxRuntime.SetFloat("_AtmosphereThickness", Mathf.Lerp(baseSkyThickness, 2.2f, t));
        }

        // ----------------------------------------------------------------- lighting

        private void ApplyLighting(float t)
        {
            if (sunLight == null) return;
            sunLight.color = Color.Lerp(baseSunColor, HellSun, t);
            sunLight.intensity = Mathf.Lerp(baseSunIntensity, baseSunIntensity * sunDimAtFull, t);
        }

        // --------------------------------------------------------------- baseline

        private void CaptureBaseline()
        {
            if (baselineCaptured) return;

            baseFog = RenderSettings.fog;
            baseFogColor = RenderSettings.fogColor;
            baseFogDensity = RenderSettings.fogDensity;
            baseFogMode = RenderSettings.fogMode;
            baseAmbient = RenderSettings.ambientSkyColor;
            baseAmbientEquator = RenderSettings.ambientEquatorColor;
            baseAmbientGround = RenderSettings.ambientGroundColor;

            // Swap the shared skybox asset for a runtime copy we can tint freely
            // (mutating the asset itself would persist edits in the editor).
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_SkyTint") && sky.HasProperty("_Exposure"))
            {
                skyboxRuntime = new Material(sky) { name = sky.name + " (EmberAtmosphere)" };
                RenderSettings.skybox = skyboxRuntime;
                baseSkyTint = skyboxRuntime.GetColor("_SkyTint");
                baseSkyExposure = skyboxRuntime.GetFloat("_Exposure");
                baseSkyThickness = skyboxRuntime.HasProperty("_AtmosphereThickness")
                    ? skyboxRuntime.GetFloat("_AtmosphereThickness") : 1f;
                skyboxDriven = skyboxRuntime.HasProperty("_AtmosphereThickness");
            }

            // Resolve the sun if it wasn't wired in the inspector: first directional light.
            if (sunLight == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) { sunLight = l; break; }
                }
            }
            if (sunLight != null)
            {
                baseSunColor = sunLight.color;
                baseSunIntensity = sunLight.intensity;
            }

            baselineCaptured = true;
        }

        private void RestoreBaseline()
        {
            if (!baselineCaptured) return;

            RenderSettings.fog = baseFog;
            RenderSettings.fogMode = baseFogMode;
            RenderSettings.fogColor = baseFogColor;
            RenderSettings.fogDensity = baseFogDensity;
            RenderSettings.ambientLight = baseAmbient;

            if (sunLight != null)
            {
                sunLight.color = baseSunColor;
                sunLight.intensity = baseSunIntensity;
            }
        }
    }
}
