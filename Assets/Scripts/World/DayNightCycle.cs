using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace AldenFall.World
{
    /// <summary>
    /// A full day/night cycle: a sun and a moon that orbit the world, sunrise and
    /// sunset warmth, a dark-but-navigable night with a star field and moonlight,
    /// all easing smoothly on a fast cycle so a single session passes through the
    /// day several times.
    ///
    /// This component is the SOLE owner of the time-based lighting (sun rotation +
    /// colour + intensity, moon, stars, ambient, fog, skybox exposure/tint). The
    /// eruption look from <see cref="EmberAtmosphere"/> is layered ON TOP: each frame
    /// we read its CurrentIntensity and lerp the whole scene toward the apocalypse
    /// palette, so "erupting at midnight" still goes red. While a cycle is Active,
    /// EmberAtmosphere stops writing RenderSettings itself (it keeps its particles).
    ///
    /// SERVER-AUTHORITATIVE clock: the server holds an ANCHOR (a time-of-day + speed captured
    /// at one NetworkTime moment) as SyncVars, and every client EXTRAPOLATES from Mirror's
    /// synchronized NetworkTime.time. So all clients agree on the time of day with no per-frame
    /// syncing, and re-anchoring (F9 jump / pause) replicates once. Presentation (the actual
    /// lighting) is still per-client; only the CLOCK is networked. A dedicated (headless) server
    /// runs only the clock, not the visuals.
    ///
    /// timeOfDay is normalised [0,1): 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // Awake before EmberAtmosphere so it can defer the skybox to us
    public class DayNightCycle : NetworkBehaviour
    {
        public static DayNightCycle Instance { get; private set; }
        /// <summary>True when a cycle exists and is driving the scene (EmberAtmosphere defers).</summary>
        public static bool Active => Instance != null && Instance.isActiveAndEnabled;

        [Header("Clock")]
        [Tooltip("Real seconds for one full day->night->day. 600 = 10 min, several cycles per session.")]
        [SerializeField] private float cycleSeconds = 600f;

        // Server-owned anchor: timeOfDay == anchorTod at NetworkTime.time == anchorTime, advancing
        // at todSpeed units/second (0 = paused). Re-anchored on start, jump, and pause.
        [SyncVar] private double anchorTime;
        [SyncVar] private float anchorTod = 0.32f;   // spawn just after sunrise, full morning
        [SyncVar] private float todSpeed;

        // Offline fallback (editor play with no network session): a plain local clock.
        private float offlineTod = 0.32f;
        private bool offlinePaused;

        private bool renders;                        // false on a headless dedicated server
        private float timeOfDay = 0.32f;             // derived each frame, used by the visuals

        [Header("Sun")]
        [SerializeField] private Light sunLight;
        [Tooltip("Compass direction of the sun's arc.")]
        [SerializeField] private float sunYaw = -100f;
        [Tooltip("Peak elevation of the noon sun. Kept low so the whole day keeps a flattering, angled, blue-sky look.")]
        [SerializeField] private float peakElevationDeg = 40f;
        [SerializeField] private float sunPeakIntensity = 1.5f;
        private static readonly Color NoonSun = new Color(1.00f, 0.97f, 0.90f);
        private static readonly Color HorizonSun = new Color(1.00f, 0.52f, 0.26f);  // warm sunrise/sunset

        [Header("Moon")]
        [SerializeField] private float moonPeakIntensity = 0.32f;
        private static readonly Color MoonColor = new Color(0.55f, 0.63f, 0.86f);

        [Header("Night ambient (dark but explorable)")]
        private static readonly Color NightAmbSky = new Color(0.11f, 0.13f, 0.21f);
        private static readonly Color NightAmbEquator = new Color(0.08f, 0.10f, 0.16f);
        private static readonly Color NightAmbGround = new Color(0.05f, 0.06f, 0.10f);
        private static readonly Color NightFog = new Color(0.05f, 0.06f, 0.12f);
        private static readonly Color TwilightFog = new Color(0.50f, 0.30f, 0.22f);

        // --- captured day baseline (from whatever the world build set) ---
        private Color dayAmbSky, dayAmbEquator, dayAmbGround;
        private Color dayFogColor;
        private float dayFogStart, dayFogEnd;
        private float daySkyExposure;
        private Color daySkyTint;

        // --- runtime pieces we own ---
        private Light moonLight;
        private Material skyboxRuntime;
        private Transform starDome;
        private Material starMat;
        private Transform moonDisc;
        private Material moonMat;
        private Transform trackedCamera;

        private bool Networked => NetworkServer.active || NetworkClient.active;

        /// <summary>The current time of day [0,1), from the networked clock (or the offline fallback).</summary>
        public float TimeOfDay =>
            Networked ? Mathf.Repeat(anchorTod + (float)(NetworkTime.time - anchorTime) * todSpeed, 1f)
                      : Mathf.Repeat(offlineTod, 1f);

        public bool Paused => Networked ? todSpeed <= 0f : offlinePaused;

        /// <summary>Human label for the current time (for the F9 readout).</summary>
        public string PhaseLabel
        {
            get
            {
                float t = TimeOfDay;
                if (t < 0.22f || t >= 0.79f) return "Night";
                if (t < 0.30f) return "Sunrise";
                if (t < 0.70f) return "Day";
                return "Sunset";
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            // A headless dedicated server runs the CLOCK only — no camera, so skip the visuals
            // (moon/star meshes, skybox copy, RenderSettings). Clients still get the synced time.
            renders = !(Application.isBatchMode && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null);
            if (!renders) return;

            if (sunLight == null) sunLight = RenderSettings.sun;
            if (sunLight == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sunLight = l; break; }

            CaptureDayBaseline();
            BuildMoon();
            BuildStarDome();
            BuildMoonDisc();
        }

        public override void OnStartServer()
        {
            // Global clock: must reach EVERY client regardless of distance, so opt out of the
            // distance interest management (which would hide this scene object from far players).
            netIdentity.visibility = Visibility.ForceShown;
            // Server owns the clock: anchor it now and let it run at the configured cadence.
            ServerReanchor(anchorTod, 1f / Mathf.Max(1f, cycleSeconds));
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------- public API (F9 / QA)

        /// <summary>Jump the clock. Server-authoritative: re-anchors + replicates to everyone.</summary>
        public void SetTimeOfDay(float t)
        {
            if (!Networked) { offlineTod = Mathf.Repeat(t, 1f); return; }
            if (isServer) ServerReanchor(t, todSpeed);
            else CmdSetTimeOfDay(t);
        }

        public void SetPaused(bool value)
        {
            if (!Networked) { offlinePaused = value; return; }
            if (isServer) ServerReanchor(TimeOfDay, value ? 0f : 1f / Mathf.Max(1f, cycleSeconds));
            else CmdSetPaused(value);
        }

        public void TogglePaused() => SetPaused(!Paused);

        // requiresAuthority=false: DayNightCycle is a scene object nobody owns, so the F9 panel
        // on any client can drive it. (Dev/testing tool.)
        [Command(requiresAuthority = false)]
        private void CmdSetTimeOfDay(float t) => ServerReanchor(t, todSpeed);

        [Command(requiresAuthority = false)]
        private void CmdSetPaused(bool value) => ServerReanchor(TimeOfDay, value ? 0f : 1f / Mathf.Max(1f, cycleSeconds));

        [Server]
        private void ServerReanchor(float tod, float speed)
        {
            anchorTod = Mathf.Repeat(tod, 1f);
            anchorTime = NetworkTime.time;
            todSpeed = speed;
        }

        // ---------------------------------------------------------------- update

        private void Update()
        {
            // Offline fallback advances a local clock; networked mode extrapolates from the anchor.
            if (!Networked && !offlinePaused && cycleSeconds > 1f)
                offlineTod = Mathf.Repeat(offlineTod + Time.deltaTime / cycleSeconds, 1f);

            timeOfDay = TimeOfDay;
            if (renders) Apply();
        }

        private void Apply()
        {
            // Sun on a TILTED arc: rises at the east horizon, peaks at peakElevationDeg
            // in the south (NOT dead overhead, so vertical walls keep catching light),
            // sets at the west horizon, and passes below the world at night. Rotating
            // RenderSettings.sun also swings the procedural skybox's sun disc + horizon
            // glow, so dawn/dusk colour is free.
            float s = (timeOfDay - 0.25f) * 2f * Mathf.PI;   // 0 sunrise, pi/2 noon, pi sunset, -pi/2 midnight
            float peak = peakElevationDeg * Mathf.Deg2Rad;
            Vector3 eastAxis = Quaternion.Euler(0f, sunYaw, 0f) * Vector3.forward;        // rise/set line
            Vector3 southAxis = Quaternion.Euler(0f, sunYaw + 90f, 0f) * Vector3.forward; // noon lean
            Vector3 sunSky = (eastAxis * Mathf.Cos(s)
                           + Vector3.up * (Mathf.Sin(s) * Mathf.Sin(peak))
                           + southAxis * (Mathf.Sin(s) * Mathf.Cos(peak))).normalized;
            if (sunLight != null && sunSky.sqrMagnitude > 0.0001f)
                sunLight.transform.rotation = Quaternion.LookRotation(-sunSky);   // light travels toward the scene
            if (moonLight != null && sunSky.sqrMagnitude > 0.0001f)
                moonLight.transform.rotation = Quaternion.LookRotation(sunSky);   // moon opposite the sun

            float sunHeight = sunSky.y;      // elevation sine (~+0.82 noon, 0 horizon, ~-0.82 midnight)
            float dayAmount = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.24f, sunHeight));
            float nightAmount = 1f - dayAmount;
            // Warmth peaks right at the horizon crossing (sunrise/sunset), zero when high or deep-night.
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(sunHeight) / 0.34f) * Mathf.Clamp01(dayAmount * 2f);

            // Eruption tint blended over whatever the time of day is.
            float ember = EmberAtmosphere.Instance != null ? EmberAtmosphere.Instance.CurrentIntensity : 0f;

            ApplySun(sunHeight, twilight, ember);
            ApplyMoon(sunHeight, ember);
            ApplyAmbient(dayAmount, ember);
            ApplyFog(dayAmount, twilight, ember);
            ApplySkybox(dayAmount, ember);
            ApplyStarsAndMoonDisc(nightAmount, ember);
        }

        private void ApplySun(float sunHeight, float twilight, float ember)
        {
            if (sunLight == null) return;
            float lit = Mathf.Clamp01(sunHeight * 2.2f);   // fades out as it nears/passes the horizon
            Color c = Color.Lerp(NoonSun, HorizonSun, twilight);
            c = Color.Lerp(c, EmberAtmosphere.HellSun, ember);
            sunLight.color = c;
            // Ember keeps a smouldering key light even when the sun is technically down.
            float intensity = sunPeakIntensity * Mathf.Max(lit, ember * 0.5f);
            sunLight.intensity = intensity;
            sunLight.shadowStrength = 0.75f * Mathf.Clamp01(lit + ember * 0.5f);
            sunLight.enabled = intensity > 0.01f;
            RenderSettings.sun = sunLight;
        }

        private void ApplyMoon(float sunHeight, float ember)
        {
            if (moonLight == null) return;
            // The moon fills the night; it recedes under the eruption's red key.
            float moon = Mathf.Clamp01(-sunHeight * 2.2f) * (1f - ember * 0.7f);
            moonLight.intensity = moonPeakIntensity * moon;
            moonLight.enabled = moonLight.intensity > 0.01f;
        }

        private void ApplyAmbient(float dayAmount, float ember)
        {
            Color sky = Color.Lerp(NightAmbSky, dayAmbSky, dayAmount);
            Color eq = Color.Lerp(NightAmbEquator, dayAmbEquator, dayAmount);
            Color gr = Color.Lerp(NightAmbGround, dayAmbGround, dayAmount);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(sky, EmberAtmosphere.HellAmbient, ember);
            RenderSettings.ambientEquatorColor = Color.Lerp(eq, EmberAtmosphere.HellAmbientEquator, ember);
            RenderSettings.ambientGroundColor = Color.Lerp(gr, EmberAtmosphere.HellAmbientGround, ember);
        }

        private void ApplyFog(float dayAmount, float twilight, float ember)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            Color fog = Color.Lerp(NightFog, dayFogColor, dayAmount);
            fog = Color.Lerp(fog, TwilightFog, twilight * 0.6f);
            fog = Color.Lerp(fog, EmberAtmosphere.AshFogColor, ember);
            RenderSettings.fogColor = fog;
            // Night pulls the haze in for mood; ember pulls it in further.
            float startDay = dayFogStart, endDay = dayFogEnd;
            float start = Mathf.Lerp(200f, startDay, dayAmount);
            float end = Mathf.Lerp(1100f, endDay, dayAmount);
            RenderSettings.fogStartDistance = Mathf.Lerp(start, 60f, ember);
            RenderSettings.fogEndDistance = Mathf.Lerp(end, 520f, ember);
        }

        private void ApplySkybox(float dayAmount, float ember)
        {
            if (skyboxRuntime == null) return;
            // Dark night sky (a hint of deep blue, not dead black), bright day; ember reddens + dims.
            float exposure = Mathf.Lerp(0.22f, daySkyExposure, dayAmount);
            exposure = Mathf.Lerp(exposure, daySkyExposure * 0.5f, ember);
            skyboxRuntime.SetFloat("_Exposure", exposure);

            Color tint = Color.Lerp(new Color(0.10f, 0.13f, 0.26f), daySkyTint, dayAmount);
            tint = Color.Lerp(tint, EmberAtmosphere.HellSkyTint, ember);
            skyboxRuntime.SetColor("_SkyTint", tint);
        }

        private void ApplyStarsAndMoonDisc(float nightAmount, float ember)
        {
            Camera cam = trackedCamera != null ? trackedCamera.GetComponent<Camera>() : null;
            if (cam == null && Camera.main != null) { cam = Camera.main; trackedCamera = cam.transform; }
            float show = nightAmount * (1f - ember);   // stars/moon wash out under the eruption glow

            // Size the dome/moon to sit just inside the camera far plane, whatever it is
            // (a dome beyond the far clip is culled entirely — the stars-never-render bug).
            float far = cam != null ? cam.farClipPlane : 900f;
            float domeRadius = far * 0.9f;
            float moonDist = far * 0.85f;

            if (starDome != null)
            {
                if (trackedCamera != null) starDome.position = trackedCamera.position;
                starDome.localScale = Vector3.one * (domeRadius * 2f);
                starDome.Rotate(Vector3.up, Time.deltaTime * 0.4f, Space.World);   // slow drift
                if (starMat != null)
                {
                    var c = starMat.color; c.a = show; starMat.color = c;
                }
                starDome.gameObject.SetActive(show > 0.01f);
            }

            if (moonDisc != null && moonLight != null)
            {
                // Park the disc far along the moonlight's up-sky direction, facing the camera.
                Vector3 moonDir = -moonLight.transform.forward;
                Vector3 camPos = trackedCamera != null ? trackedCamera.position : transform.position;
                moonDisc.position = camPos + moonDir * moonDist;
                moonDisc.localScale = Vector3.one * (moonDist * 0.22f);
                if (trackedCamera != null) moonDisc.rotation = Quaternion.LookRotation(moonDisc.position - camPos);
                bool up = moonDir.y > -0.05f;   // only when the moon is above the horizon
                if (moonMat != null)
                {
                    var c = moonMat.color; c.a = up ? show : 0f; moonMat.color = c;
                }
                moonDisc.gameObject.SetActive(up && show > 0.01f);
            }
        }

        // -------------------------------------------------------------- building

        private void CaptureDayBaseline()
        {
            dayAmbSky = RenderSettings.ambientSkyColor;
            dayAmbEquator = RenderSettings.ambientEquatorColor;
            dayAmbGround = RenderSettings.ambientGroundColor;
            dayFogColor = RenderSettings.fogColor;
            dayFogStart = RenderSettings.fogStartDistance > 1f ? RenderSettings.fogStartDistance : 450f;
            dayFogEnd = RenderSettings.fogEndDistance > 1f ? RenderSettings.fogEndDistance : 1900f;

            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure"))
            {
                skyboxRuntime = new Material(sky) { name = sky.name + " (DayNight)" };
                RenderSettings.skybox = skyboxRuntime;
                daySkyExposure = skyboxRuntime.GetFloat("_Exposure");
                daySkyTint = skyboxRuntime.HasProperty("_SkyTint")
                    ? skyboxRuntime.GetColor("_SkyTint") : Color.white;
            }
            else
            {
                daySkyExposure = 1.3f;
                daySkyTint = new Color(0.52f, 0.63f, 0.82f);
            }
        }

        private void BuildMoon()
        {
            var go = new GameObject("MoonLight");
            go.transform.SetParent(transform, false);
            moonLight = go.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = MoonColor;
            moonLight.intensity = 0f;
            moonLight.shadows = LightShadows.None;   // one shadow-caster (the sun) is plenty
            moonLight.renderMode = LightRenderMode.ForcePixel;
        }

        private void BuildStarDome()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "StarDome";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 1900f;   // just inside the camera far plane
            WhitenVertexColors(go);   // Sprites/Default multiplies by vertex colour; primitives have none

            starMat = new Material(SkyOverlayShader())
            {
                name = "StarDomeMat",
                mainTexture = MakeStarTexture(2048),
                mainTextureScale = new Vector2(4f, 2f),   // tile hard so each texel is tiny -> small crisp stars, not blobs
            };
            starMat.color = new Color(1f, 1f, 1f, 0f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = starMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            starDome = go.transform;
        }

        private void BuildMoonDisc()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "MoonDisc";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 200f;   // clearly readable moon
            WhitenVertexColors(go);

            moonMat = new Material(SkyOverlayShader())
            {
                name = "MoonDiscMat",
                mainTexture = MakeMoonTexture(128),
            };
            moonMat.color = new Color(1f, 1f, 1f, 0f);
            moonMat.renderQueue = 3010;   // draw the moon just after the star dome (Sprites queue 3000)
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = moonMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            moonDisc = go.transform;
        }

        // A real-looking starfield: thousands of mostly single-pixel points on transparent black,
        // brightness weighted heavily toward faint stars (a real sky is mostly dim pinpricks with a
        // handful of bright ones), subtle warm/cool colour scatter, a faint Milky-Way style band, and
        // only the very brightest stars given a 1px halo. High resolution + hard tiling on the dome
        // keeps every star small and crisp instead of a magnified blob.
        private static Texture2D MakeStarTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);

            // Faint diffuse "galaxy band" — a soft, wide diagonal wash of very dim micro-stars so the
            // sky has structure rather than a uniform scatter. Kept dim + wide so it never clumps into
            // a bright blocky patch when it happens to sit overhead.
            float bandCenter = size * 0.5f, bandWidth = size * 0.24f;
            int band = size * size / 170;
            for (int i = 0; i < band; i++)
            {
                int x = Random.Range(0, size);
                // cluster y around a diagonal line through the tile
                float lineY = (bandCenter + (x - size * 0.5f) * 0.55f + Random.Range(-bandWidth, bandWidth));
                int y = Mathf.Clamp(Mathf.RoundToInt(lineY), 0, size - 1);
                byte a = (byte)Random.Range(10, 34);
                Blend(px, size, x, y, 232, 238, 255, a);
            }

            int stars = size * size / 140;   // dense field of pinpricks
            for (int i = 0; i < stars; i++)
            {
                int x = Random.Range(0, size), y = Random.Range(0, size);
                // brightness heavily weighted to faint: b^3 keeps most stars dim, few bright
                float b = Random.value; b = b * b * b;
                float warm = Random.value;   // colour temperature scatter
                byte rr = (byte)Mathf.Clamp(255 * (0.80f + warm * 0.20f), 0, 255);
                byte gg = (byte)Mathf.Clamp(255 * (0.86f + 0.10f), 0, 255);
                byte bb = (byte)Mathf.Clamp(255 * (0.82f + (1f - warm) * 0.18f), 0, 255);
                byte coreA = (byte)Mathf.Clamp(60 + b * 195f, 0, 255);

                Blend(px, size, x, y, rr, gg, bb, coreA);   // the star is a single sharp pixel
                if (b > 0.55f)   // only the brightest get a faint 4-neighbour halo
                {
                    byte haloA = (byte)Mathf.Clamp(coreA * 0.28f, 0, 255);
                    Blend(px, size, x + 1, y, rr, gg, bb, haloA);
                    Blend(px, size, x - 1, y, rr, gg, bb, haloA);
                    Blend(px, size, x, y + 1, rr, gg, bb, haloA);
                    Blend(px, size, x, y - 1, rr, gg, bb, haloA);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // Sprites/Default is proven to render + survive build shader stripping in this
        // project (the ember particles use it). The star dome + moon sit just inside the
        // camera far plane, so nearer world geometry occludes them and they show over sky.
        private static Shader SkyOverlayShader()
        {
            return Shader.Find("Sprites/Default");
        }

        // Sprites/Default outputs texture * vertexColor. CreatePrimitive meshes carry NO
        // vertex colours, which the shader reads as black -> the mesh renders invisible.
        // Give the instance mesh solid-white vertex colours so the texture shows through.
        private static void WhitenVertexColors(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) return;
            var mesh = mf.mesh;   // instantiates a per-object copy (safe to mutate)
            var cols = new Color32[mesh.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = cols;
        }

        // Brightening blend (keep the brightest colour, accumulate alpha) so overlapping
        // star halos read as glow rather than overwriting each other.
        private static void Blend(Color32[] px, int size, int x, int y, byte r, byte g, byte b, byte a)
        {
            x = (x % size + size) % size; y = (y % size + size) % size;
            int idx = y * size + x;
            var cur = px[idx];
            px[idx] = new Color32(
                (byte)Mathf.Max(cur.r, r),
                (byte)Mathf.Max(cur.g, g),
                (byte)Mathf.Max(cur.b, b),
                (byte)Mathf.Min(255, cur.a + a));
        }

        // Soft glowing moon disc: bright core fading to transparent at the rim.
        private static Texture2D MakeMoonTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0 centre, 1 rim
                float disc = Mathf.Clamp01(1f - Mathf.SmoothStep(0.62f, 0.72f, d));  // hard-ish moon body
                float halo = Mathf.Clamp01(1f - d) * 0.35f;                          // soft glow beyond
                float a = Mathf.Clamp01(disc + halo * (1f - disc));
                // faint crater shading so it isn't a flat dot
                float shade = 0.82f + 0.18f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                byte r = (byte)(235 * shade), g = (byte)(238 * shade), b = (byte)(230 * shade);
                px[y * size + x] = new Color32(r, g, b, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
