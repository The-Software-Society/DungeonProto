using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>
    /// ColorLab (task #95): renders a GRID of scene-palette variants side-by-side in ONE PNG
    /// (seconds), so the world look is picked by comparison instead of 20-minute blind builds.
    ///
    /// v3 — the "AAA ground" iteration (Jon: "ground is never flat green; dirt with grassy
    /// patches; tree looks dying; rock should be gray"). Every cell:
    ///   - ground = warm SOIL with organic grass patches (shared patch noise)
    ///   - grass tufts CLUMPED inside those patches (bare dirt in between) + ferns/flowers
    ///   - the same namaqualand boulder desaturated to GRAY granite
    ///   - a real island_tree_01
    /// Rows sweep the grass green (warm -> true -> rich). Columns sweep TREE FOLIAGE DENSITY:
    ///   today's baked LOD0  |  baked + thicker alpha (cutoff 0.30)  |  raw full-canopy FBX.
    /// Output: WorldData/ColorLab/colorlab_grid.png + legend.txt.
    ///
    /// Key findings encoded here:
    ///   - URP Lit IGNORES vertex colours -> blade hue must be baked into the texture (v2).
    ///   - BakeTreeKitPrefabs QEM-simplifies foliage at 0.3 BEFORE tiering -> the "dying" canopy;
    ///     the raw FBX column shows the ceiling if that pass is skipped for leaves.
    /// </summary>
    public static class ColorLab
    {
        private const string OutDir = "WorldData/ColorLab";
        private const int CellW = 480, CellH = 360;
        private static readonly Dictionary<string, GameObject> OptimizedKitTemplates = new();
        private static readonly Dictionary<string, Material> LabImpostorMaterials = new();

        private struct GrassSet
        {
            public string name;
            public Color bladeDark, bladeLight;          // tuft blade gradient (baked into texture)
            public Color gDark, gLight;                  // grass-patch ground colour ramp
        }

        private static readonly GrassSet[] GrassSets =
        {
            new GrassSet { name = "1 warm natural",
                bladeDark = new Color(0.16f, 0.24f, 0.10f), bladeLight = new Color(0.40f, 0.52f, 0.22f),
                gDark = new Color(0.13f, 0.20f, 0.09f), gLight = new Color(0.28f, 0.38f, 0.16f) },
            new GrassSet { name = "2 true green",
                bladeDark = new Color(0.13f, 0.24f, 0.11f), bladeLight = new Color(0.32f, 0.52f, 0.24f),
                gDark = new Color(0.11f, 0.20f, 0.10f), gLight = new Color(0.24f, 0.38f, 0.18f) },
            new GrassSet { name = "3 rich green",
                bladeDark = new Color(0.11f, 0.23f, 0.12f), bladeLight = new Color(0.26f, 0.50f, 0.26f),
                gDark = new Color(0.10f, 0.19f, 0.10f), gLight = new Color(0.20f, 0.37f, 0.19f) },
        };

        private enum TreeMode { Baked, BakedThickAlpha, RawFullCanopy }
        private static readonly (string name, TreeMode mode)[] TreeVariants =
        {
            ("T1 baked LOD0 (today)", TreeMode.Baked),
            ("T2 baked + thick alpha", TreeMode.BakedThickAlpha),
            ("T3 RAW full canopy",     TreeMode.RawFullCanopy),
        };
        // One leaf tint for the whole grid — this iteration isolates foliage DENSITY, not hue.
        // Keep scanned leaf values inside a physically plausible range. The former >1.8 green
        // multiplier clipped highlights and erased the albedo variation that makes foliage read.
        private static readonly Color LeafTint = new Color(0.86f, 1.03f, 0.82f);

        // Warm soil ramp shared by every cell ("ground is dirt with grass, not flat green").
        private static readonly Color DirtDark = new Color(0.22f, 0.18f, 0.13f);
        private static readonly Color DirtMid = new Color(0.32f, 0.26f, 0.18f);
        private static readonly Color DirtLight = new Color(0.41f, 0.34f, 0.24f);

        // =====================================================================================
        //  v4 HERO: one large frame chasing the AAA ceiling (Jon: "10x beauty/clarity/realism").
        //  Locked from the grid: row-2 true-green + full-canopy tree. Adds: REAL Poly Haven
        //  photoscan grass clumps (grass_medium_01/02, bermuda), photoreal normal-mapped ground
        //  (forest_ground_04 in patches, brown_mud_leaves_01 bare), undulating soil mesh, litter
        //  (bark debris, dry branches, moss), gray granite boulders, soft shadows, procedural
        //  skybox, ACES tonemap + bloom + vignette, SMAA, eye-level camera.
        // =====================================================================================
        [MenuItem("AldenFall/Color Lab HERO")]
        public static void RenderHero()
        {
            ClearOptimizedKitTemplates();
            ClearLabImpostorMaterials();
            Directory.CreateDirectory(OutDir);
            var set = GrassSets[1];   // "2 true green" — Jon's pick

            // --- lighting: warm key sun with SOFT SHADOWS + gradient ambient + procedural sky ---
            // Atmospheric fog: the AAA depth cue — fades the ground skirt + tree line into the sky.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.043f;
            RenderSettings.fogColor = new Color(0.23f, 0.34f, 0.35f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.50f, 0.54f);
            RenderSettings.ambientEquatorColor = new Color(0.29f, 0.38f, 0.37f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.27f, 0.22f);
            var lightGO = new GameObject("HeroSun");
            var sun = lightGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.00f, 0.87f, 0.66f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            lightGO.transform.rotation = Quaternion.Euler(28f, 218f, 0f);
            // Keep the photographic forest HDRI for reflection/ambient-probe baking, but do not
            // display it behind an eye-level camera: its low-elevation forest floor and nearby
            // trunks become enormous blurred shapes. A fog-matched physical sky produces a clean
            // atmospheric horizon while the actual 3D forest supplies every visible silhouette.
            var skyMat = new Material(Shader.Find("Skybox/Procedural"));
            skyMat.SetFloat("_SunSize", 0.018f);
            skyMat.SetFloat("_SunSizeConvergence", 4f);
            skyMat.SetFloat("_AtmosphereThickness", 1.15f);
            skyMat.SetFloat("_Exposure", 0.48f);
            skyMat.SetColor("_SkyTint", new Color(0.28f, 0.39f, 0.42f));
            skyMat.SetColor("_GroundColor", RenderSettings.fogColor * 0.72f);
            RenderSettings.skybox = skyMat;
            RenderSettings.sun = sun;

            // --- post: ACES + subtle bloom/vignette/grade (the Phase-D color-grade, previewed) ---
            var volGO = new GameObject("HeroVolume");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true); tone.mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true); bloom.intensity.value = 0.18f; bloom.threshold.value = 1.05f;
            var grade = profile.Add<ColorAdjustments>(true);
            grade.contrast.value = 12f; grade.saturation.value = 7f; grade.postExposure.value = -0.05f;
            var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.16f;
            vol.sharedProfile = profile;

            // --- camera: player eye level, post + SMAA on ---
            var camGO = new GameObject("HeroCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f;
            camGO.transform.position = new Vector3(-4.6f, 1.65f, -7.8f);
            camGO.transform.LookAt(new Vector3(0.4f, 1.15f, 3.0f));
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var root = BuildHeroScene(set);
            AddForestLightShafts(root.transform);
            MarkStaticRecursive(root);
            ReportHeroStats(root);

            const int W = 1600, H = 1000;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); rt.Create();
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
            File.WriteAllBytes($"{OutDir}/colorlab_hero.png", tex.EncodeToPNG());
            Debug.Log($"COLORLAB HERO -> {OutDir}/colorlab_hero.png");

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGO); Object.DestroyImmediate(lightGO); Object.DestroyImmediate(volGO);
            ClearOptimizedKitTemplates();
            ClearLabImpostorMaterials();
        }

        // undulation for the hero ground (gentle rises, ±0.35m)
        private static float GroundH(float x, float z) =>
            (Fbm((x + 6f) / 12f, (z + 6f) / 12f, 2.5f) - 0.5f) * 0.7f;

        private static GameObject BuildHeroScene(GrassSet set)
        {
            var root = new GameObject("HeroScene");
            var rng = new System.Random(1234);
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // ---- undulating ground mesh, 12x12m, 96x96 quads ----
            const int N = 96; const float Sz = 12f;
            var gm = new Mesh { name = "HeroGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var gv = new List<Vector3>(); var guv = new List<Vector2>(); var gt = new List<int>();
            for (int j = 0; j <= N; j++)
                for (int i = 0; i <= N; i++)
                {
                    float x = i / (float)N * Sz - Sz / 2f, z = j / (float)N * Sz - Sz / 2f;
                    gv.Add(new Vector3(x, GroundH(x, z), z));
                    guv.Add(new Vector2(i / (float)N, j / (float)N));
                }
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int a = j * (N + 1) + i, b = a + 1, c = a + N + 1, d = c + 1;
                    gt.AddRange(new[] { a, c, b, b, c, d });
                }
            gm.SetVertices(gv); gm.SetUVs(0, guv); gm.SetTriangles(gt, 0);
            gm.RecalculateNormals(); gm.RecalculateBounds();
            var groundGO = new GameObject("Ground");
            groundGO.transform.SetParent(root.transform);
            groundGO.AddComponent<MeshFilter>().sharedMesh = gm;
            var gr = groundGO.AddComponent<MeshRenderer>();
            gr.sharedMaterial = HeroGroundMaterial(set);

            // Rear terrain rises into the tree wall, eliminating the flat skirt/horizon and giving
            // background vegetation a physical bank to emerge from. It shares the photoreal ground
            // material so the clearing and enclosure remain one continuous ecosystem.
            BuildRearBerm(root.transform, gr.sharedMaterial);

            // ---- GRASS: dense card carpet BAKED from the photoscan clumps (impostor technique).
            //      A card = a side-render of the real scanned grass -> photographic blades on cheap
            //      geometry, layered until the patch ground disappears under grass (the AAA look;
            //      sparse 3D clumps alone read as thin dry weeds — Jon's crab-grass complaint). ----
            var gm1 = KitClump("grass_medium_01");
            var gm2 = KitClump("grass_medium_02");
            var cardMats = new List<Material>();
            void AddBladeMaterial(Color tint)
            {
                const string path = "Assets/Models/Trees/grass_medium_02/grass_medium_02_leafRGBA.png";
                if (!File.Exists(path)) return;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                t.LoadImage(File.ReadAllBytes(path));
                t.filterMode = FilterMode.Trilinear;
                t.anisoLevel = 4;
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetTexture("_BaseMap", t);
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_AlphaClip", 1f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cutoff", 0.46f);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.SetFloat("_Smoothness", 0.03f);
                cardMats.Add(m);
            }
            // Direct isolated blade cutouts from the scan: no rebaked root clod or atlas fragments.
            AddBladeMaterial(new Color(0.68f, 1.00f, 0.68f));
            AddBladeMaterial(new Color(0.76f, 0.97f, 0.62f));
            AddBladeMaterial(new Color(0.65f, 1.00f, 0.73f));
            var starMesh = BuildPhotographicBladeTuft();
            var grassMatrices = new List<Matrix4x4>[cardMats.Count];
            for (int i = 0; i < grassMatrices.Length; i++) grassMatrices[i] = new List<Matrix4x4>();
            void QueueGrass(float wx, float y, float wz, float w, float h, Quaternion rotation)
            {
                int mi = Mathf.Abs((int)(wx * 131 + wz * 17)) % cardMats.Count;
                grassMatrices[mi].Add(Matrix4x4.TRS(new Vector3(wx, y, wz), rotation, new Vector3(w, h, w)));
            }
            void GrassCard(float wx, float wz, float w, float h, float yaw)
            {
                // A shared prevailing lean reads as wind; random micro-lean prevents cloned stars.
                float gust = Mathf.PerlinNoise(wx * 0.23f + 4.7f, wz * 0.23f + 1.9f);
                QueueGrass(wx, GroundH(wx, wz) - 0.02f, wz, w, h * 0.78f,
                    Quaternion.Euler(Rand(-2f, 3f) + gust * 5f, yaw, Rand(-3f, 3f) - gust * 7f));
            }
            if (cardMats.Count > 0)
            {
                int cards = 0;
                for (int i = 0; i < 20000 && cards < 1500; i++)
                {
                    float wx = Rand(-6f, 6f), wz = Rand(-6f, 5.5f);
                    float patch = PatchAt((wx + 6f) / 12f, (wz + 6f) / 12f);
                    if (patch < 0.45f) continue;
                    cards++;
                    float vigor = Mathf.InverseLerp(0.45f, 0.75f, patch);
                    GrassCard(wx, wz, Rand(0.42f, 0.76f), Rand(0.23f, 0.36f) + 0.20f * vigor, Rand(0f, 360f));
                }
                // extra-dense foreground carpet (the first thing the eye judges)
                for (int i = 0, done = 0; i < 6000 && done < 550; i++)
                {
                    float wx = Rand(-6f, 2.5f), wz = Rand(-6f, -1.5f);
                    if (PatchAt((wx + 6f) / 12f, (wz + 6f) / 12f) < 0.38f) continue;
                    done++;
                    GrassCard(wx, wz, Rand(0.40f, 0.72f), Rand(0.24f, 0.48f), Rand(0f, 360f));
                }
                // feather ring past the hero plane so the carpet doesn't stop at a hard seam
                for (int i = 0, done = 0; i < 3000 && done < 260; i++)
                {
                    float wx = Rand(-14f, 14f), wz = Rand(5.6f, 11f);
                    if (Mathf.PerlinNoise(wx * 0.3f + 3.1f, wz * 0.3f + 8.7f) < 0.42f) continue;
                    done++;
                    float w = Rand(0.55f, 0.9f);
                    QueueGrass(wx, RearBermH(wx, wz) - 0.02f, wz, w, Rand(0.28f, 0.45f) * 0.78f,
                        Quaternion.Euler(0f, Rand(0f, 360f), 0f));
                }
                // Bridge the 12m hero mesh to the fog field. This is deliberately denser near the
                // mesh edge and thins with distance, hiding the old olive transition band.
                for (int i = 0, done = 0; i < 24000 && done < 1400; i++)
                {
                    float wz = Rand(4.7f, 17f), wx = Rand(-15f, 15f);
                    float distanceFade = Mathf.InverseLerp(17f, 5f, wz);
                    float field = Fbm((wx + 15f) / 30f, (wz - 4.7f) / 12.3f, 5f);
                    if (field < Mathf.Lerp(0.58f, 0.40f, distanceFade)) continue;
                    done++;
                    float y = RearBermH(wx, wz) - 0.02f;
                    float w = Rand(0.58f, 0.92f);
                    QueueGrass(wx, y, wz, w,
                        Rand(0.34f, 0.56f) * 0.78f * Mathf.Lerp(0.82f, 1f, distanceFade),
                        Quaternion.Euler(Rand(0f, 5f), Rand(0f, 360f), Rand(-7f, 1f)));
                }
                BuildGrassBatches(root.transform, starMesh, cardMats, grassMatrices);
            }
            // ---- real BUSHES (photoscan shrubs, raw FBX — the baked prefabs carry the QEM leaf damage) ----
            var shrub2 = KitClump("shrub_02");
            var shrub3 = KitClump("shrub_03");
            BrightenShrub(shrub2); BrightenShrub(shrub3);
            var bushSpots = new (float x, float z, float h)[]
                { (-2.7f, 1.0f, 1.55f), (4.7f, 0.3f, 1.75f), (1.7f, 4.2f, 1.40f),
                  (-4.8f, -3.2f, 1.25f), (3.0f, -3.4f, 1.15f), (-4.1f, 3.2f, 1.35f) };
            for (int i = 0; i < bushSpots.Length; i++)
            {
                var tpl = (i % 2 == 0) ? shrub2 : shrub3;
                if (tpl == null) continue;
                PlaceClump(root.transform, tpl, new Vector3(bushSpots[i].x, 0f, bushSpots[i].z),
                    bushSpots[i].h, Rand(0f, 360f));
            }
            // ---- biodiversity: ferns, flowers in/around patches ----
            // ferns demoted to a couple of shade accents by the tree — a field of them reads as WEEDS
            var flora = new (string kind, float h, int n, float lo, float hi)[]
            {
                ("fern_02", 0.55f, 2, 0.60f, 1f), ("dandelion_01", 0.34f, 7, 0.48f, 1f),
                ("celandine_01", 0.30f, 5, 0.48f, 1f), ("flower_gazania", 0.26f, 3, 0.42f, 0.55f),
            };
            foreach (var f in flora)
                for (int i = 0, done = 0; i < 400 && done < f.n; i++)
                {
                    float wx = Rand(-5.6f, 5.6f), wz = Rand(-5.6f, 4.6f);
                    float patch = PatchAt((wx + 6f) / 12f, (wz + 6f) / 12f);
                    if (patch < f.lo || patch > f.hi) continue;
                    done++;
                    PlaceKit(root.transform, f.kind, new Vector3(wx, 0f, wz),
                        f.h * (0.8f + (float)rng.NextDouble() * 0.5f), null, 0f);
                }

            // ---- ground litter on the bare dirt: bark debris, dry branches, moss ----
            var bark = KitClump("bark_debris_01");
            var branches = KitClump("dry_branches_medium_01");
            var moss = KitClump("moss_01");
            for (int i = 0, done = 0; i < 500 && done < 8 && bark != null; i++)
            {
                float wx = Rand(-5.5f, 5.5f), wz = Rand(-5.5f, 4.5f);
                if (PatchAt((wx + 6f) / 12f, (wz + 6f) / 12f) > 0.46f) continue;
                done++;
                PlaceClump(root.transform, bark, new Vector3(wx, 0f, wz), Rand(0.06f, 0.12f), Rand(0f, 360f));
            }
            for (int i = 0, done = 0; i < 500 && done < 5 && branches != null; i++)
            {
                float wx = Rand(-5.5f, 5.5f), wz = Rand(-5.5f, 4.5f);
                if (PatchAt((wx + 6f) / 12f, (wz + 6f) / 12f) > 0.46f) continue;
                done++;
                PlaceClump(root.transform, branches, new Vector3(wx, 0f, wz), Rand(0.10f, 0.18f), Rand(0f, 360f));
            }
            _ = moss;   // dropped: the scan reads as a beige mound at this scale, not moss

            // ---- BACKDROP: far ground skirt + tree line fading into the fog (no more void horizon) ----
            var skirtMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var skirtTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            skirtTex.LoadImage(File.ReadAllBytes("Assets/Textures/Ground/PolyHaven/forest_ground_04_diff_2k.jpg"));
            skirtMat.SetTexture("_BaseMap", skirtTex);
            skirtMat.SetTextureScale("_BaseMap", new Vector2(60f, 60f));
            // tint the tan distance-mush toward the CARD-CARPET green so near and far read as one field
            skirtMat.SetColor("_BaseColor", new Color(0.57f, 0.70f, 0.50f));
            skirtMat.SetFloat("_Smoothness", 0.02f);
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10m -> scale 36 = 360m
            skirt.name = "FarSkirt";
            skirt.transform.SetParent(root.transform);
            skirt.transform.position = new Vector3(0f, -0.65f, 0f);
            skirt.transform.localScale = new Vector3(36f, 1f, 36f);
            Object.DestroyImmediate(skirt.GetComponent<Collider>());
            skirt.GetComponent<Renderer>().sharedMaterial = skirtMat;

            // ---- gray granite boulder cluster + a stump ----
            var grayRock = GrayRockMaterial("namaqualand_boulder_02");
            var rock1 = PlaceKit(root.transform, "namaqualand_boulder_02", new Vector3(3.6f, -0.06f, 2.0f), 1.8f, null, 0f);
            var rock2 = PlaceKit(root.transform, "boulder_01", new Vector3(2.5f, -0.05f, 1.0f), 0.9f, null, 0f);
            var grayRock2 = GrayRockMaterial("boulder_01");
            if (rock1 != null && grayRock != null)
                foreach (var r in rock1.GetComponentsInChildren<Renderer>()) r.sharedMaterial = grayRock;
            if (rock2 != null && grayRock2 != null)
                foreach (var r in rock2.GetComponentsInChildren<Renderer>()) r.sharedMaterial = grayRock2;

            // ---- AUTHORED FOREST VIGNETTE: enclosing banks, roots and a fallen-trunk arch ----
            // Real scanned cliff pieces sit mostly outside the playable clearing and act as the
            // large framing masses missing from the earlier open asset-test composition.
            var bankRocks = new (string kind, float x, float z, float h)[]
            {
                ("namaqualand_boulder_02", -6.8f, 5.1f, 2.4f), ("boulder_01", -5.6f, 4.0f, 1.25f),
                ("namaqualand_boulder_03", -7.7f, 7.0f, 2.8f), ("namaqualand_boulder_04", 6.9f, 5.4f, 2.3f),
                ("boulder_01", 5.8f, 4.2f, 1.2f), ("namaqualand_boulder_02", 8.0f, 7.4f, 3.0f),
            };
            foreach (var br in bankRocks)
            {
                var rock = PlaceKit(root.transform, br.kind, new Vector3(br.x, -0.20f, br.z), br.h, null, 0f);
                var rockMat = GrayRockMaterial(br.kind);
                if (rock != null && rockMat != null)
                    foreach (var rr in rock.GetComponentsInChildren<Renderer>()) rr.sharedMaterial = rockMat;
            }
            PlaceKitPose(root.transform, "root_cluster_01", new Vector3(-3.8f, -0.02f, 3.2f), 1.4f,
                new Vector3(8f, 28f, -7f));
            PlaceKitPose(root.transform, "pine_roots", new Vector3(3.8f, -0.03f, 4.5f), 1.1f,
                new Vector3(3f, 214f, 5f));
            PlaceKitPose(root.transform, "dead_tree_trunk", new Vector3(0.5f, 0.20f, 8.2f), 0.72f,
                new Vector3(74f, 102f, 12f));

            // A real campfire mesh marks the path bend; warm local light separates it from the
            // cool forest and supplies the gameplay-scale focal point seen in the references.
            PlaceProp(root.transform, "Campfire", new Vector3(0.7f, 0.02f, 2.9f), 0.42f, 22f,
                Color.white);
            var fireLightGO = new GameObject("CampfireGlow");
            fireLightGO.transform.SetParent(root.transform);
            fireLightGO.transform.position = new Vector3(0.7f, GroundH(0.7f, 2.9f) + 0.48f, 2.9f);
            var fireLight = fireLightGO.AddComponent<Light>();
            fireLight.type = LightType.Point; fireLight.range = 5.5f; fireLight.intensity = 3.2f;
            fireLight.color = new Color(1.0f, 0.36f, 0.08f); fireLight.shadows = LightShadows.Soft;

            // Layered ecology around the clearing: shade ferns at rock/tree bases, broadleaf plants
            // in moist pockets, flowers only in two sunlit colonies, then shrubs/saplings behind.
            void Colony(string kind, Vector2 center, float radius, int count, float loH, float hiH)
            {
                for (int i = 0; i < count; i++)
                {
                    float a = Rand(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rand(0f, 1f)) * radius;
                    float x = center.x + Mathf.Cos(a) * d, z = center.y + Mathf.Sin(a) * d;
                    PlaceKit(root.transform, kind, new Vector3(x, GroundH(x, z), z), Rand(loH, hiH), null, 0f);
                }
            }
            Colony("fern_02", new Vector2(-4.3f, 3.2f), 2.0f, 15, 0.38f, 0.72f);
            Colony("fern_02", new Vector2(4.4f, 4.5f), 2.2f, 18, 0.40f, 0.78f);
            Colony("calathea_orbifolia_01", new Vector2(-3.8f, 0.7f), 1.25f, 4, 0.28f, 0.52f);
            Colony("anthurium_botany_01", new Vector2(4.1f, 1.2f), 1.35f, 4, 0.30f, 0.55f);
            Colony("celandine_01", new Vector2(-1.6f, 4.2f), 1.3f, 14, 0.22f, 0.38f);
            Colony("flower_gazania", new Vector2(3.0f, 3.6f), 1.1f, 11, 0.20f, 0.34f);
            Colony("rock_moss_set_01", new Vector2(-5.0f, 4.5f), 1.4f, 6, 0.22f, 0.48f);
            Colony("fern_02", new Vector2(-3.6f, -0.1f), 1.15f, 12, 0.34f, 0.62f);
            Colony("calathea_orbifolia_01", new Vector2(3.6f, 0.0f), 1.15f, 5, 0.30f, 0.52f);
            Colony("celandine_01", new Vector2(-2.2f, 1.0f), 0.9f, 12, 0.20f, 0.34f);
            Colony("flower_gazania", new Vector2(2.8f, 1.2f), 0.85f, 10, 0.20f, 0.32f);

            var backShrubs = new (string kind, float x, float z, float h)[]
            {
                ("shrub_04", -5.8f, 5.7f, 2.0f), ("shrub_02", -3.6f, 7.1f, 1.7f),
                ("shrub_03", 3.3f, 7.0f, 1.8f), ("shrub_04", 6.0f, 5.8f, 2.2f),
                ("fir_sapling_medium", -7.2f, 8.5f, 4.2f), ("tree_small_02", 7.0f, 8.3f, 4.8f),
            };
            foreach (var s in backShrubs)
                PlaceKit(root.transform, s.kind, new Vector3(s.x, RearBermH(s.x, s.z), s.z),
                    s.h, null, 0f);

            // Mid-distance ecology is the visual bridge between hero scans and impostors. Keep
            // it as shared, batched species instances: enough parallax to conceal the LOD handoff
            // without paying for hero-tree geometry across the skyline.
            for (int i = 0; i < 24; i++)
            {
                float x = Rand(-15.5f, 15.5f), z = Rand(7.4f, 15.8f);
                string species = i % 9 == 0 ? "fir_sapling_medium"
                    : i % 3 == 0 ? "shrub_04" : i % 2 == 0 ? "shrub_02" : "shrub_03";
                float h = species == "fir_sapling_medium" ? Rand(2.6f, 4.1f) : Rand(1.35f, 2.35f);
                PlaceKit(root.transform, species, new Vector3(x, RearBermH(x, z), z), h, null, 0f);
            }

            // ---- the tree: RAW full canopy (col-3 winner), tinted ----
            var heroTree = BuildTree(root.transform, TreeMode.RawFullCanopy);
            if (heroTree != null) OptimizeHierarchyByMaterial(heroTree);
            if (heroTree != null) heroTree.transform.position += new Vector3(-2.7f, 0f, 0.5f);

            // Right-hand foreground trunk completes the enclosure and gives a strong near/mid/far
            // depth read without exposing the flat world edge.
            if (heroTree != null)
            {
                var frameTree = Object.Instantiate(heroTree, root.transform);
                frameTree.transform.localScale *= 1.18f;
                frameTree.transform.rotation = Quaternion.Euler(0f, 205f, -3f) * frameTree.transform.rotation;
                var fb = Measure(frameTree);
                frameTree.transform.position += new Vector3(7.4f, -0.20f, 3.8f)
                    - new Vector3(fb.center.x, fb.min.y, fb.center.z);
            }

            // Tree line = CLONES of the raw full-canopy tree, varied yaw/scale, fading into the fog.
            // (The baked prefabs are the damaged sparse ones — a line of them reads as dead driftwood.)
            var treeTemplates = new List<GameObject>();
            if (heroTree != null) treeTemplates.Add(heroTree);
            var treeLine = new (float x, float z, float h, float yaw)[]
            {
                (-18f, 32f, 10f, 40f), (-8f, 38f, 11f, 130f), (2f, 35f, 9f, 250f),
                (12f, 39f, 11f, 80f), (21f, 33f, 10f, 310f), (-29f, 42f, 12f, 200f),
                (-40f, 50f, 11f, 20f), (34f, 49f, 12f, 160f), (-14f, 55f, 13f, 100f),
                (10f, 58f, 13f, 280f), (30f, 63f, 14f, 220f), (-34f, 65f, 14f, 350f),
            };
            if (treeTemplates.Count > 0)
                for (int ti = 0; ti < treeLine.Length; ti++)
                {
                    var t = treeLine[ti];
                    string species = new[] { "OakTree", "BirchTree", "PineTree" }[ti % 3];
                    PlaceExistingTreeImpostor(root.transform, species, t.x, t.z, t.h * 0.88f, t.yaw);
                }

            return root;
        }

        /// <summary>Side-render a photoscan grass clump into a transparent RGBA card texture — real
        /// photographic blades for the card carpet. Neutral flat lighting so the card re-lights in
        /// the scene via its quad normals.</summary>
        private static Texture2D BakeGrassCard(GameObject template, float bakeYaw)
        {
            var prevMode = RenderSettings.ambientMode;
            var prevAmb = RenderSettings.ambientLight;
            var prevFog = RenderSettings.fog;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.9f, 0.9f, 0.9f);
            RenderSettings.fog = false;

            var go = (GameObject)Object.Instantiate(template);
            go.SetActive(true);
            go.transform.position = new Vector3(0f, 500f, 0f);   // far from the scene under construction
            go.transform.rotation = Quaternion.Euler(0f, bakeYaw, 0f) * go.transform.rotation;
            var b = Measure(go);

            var camGO = new GameObject("CardBakeCam");
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x * 0.75f) * 1.05f;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = b.size.magnitude * 4f;
            camGO.transform.position = b.center + new Vector3(0f, 0f, -(b.size.z + 1f));
            camGO.transform.LookAt(b.center);

            const int S = 512;
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32); rt.Create();
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            tex.ReadPixels(new Rect(0, 0, S, S), 0, 0); tex.Apply(true);
            RenderTexture.active = prev; cam.targetTexture = null; rt.Release();

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(go);
            RenderSettings.ambientMode = prevMode;
            RenderSettings.ambientLight = prevAmb;
            RenderSettings.fog = prevFog;
            return tex;
        }

        private static void BuildRearBerm(Transform parent, Material material)
        {
            const int nx = 48, nz = 28;
            const float width = 34f, nearZ = 5.4f, depth = 13f;
            var mesh = new Mesh { name = "HeroRearBerm", indexFormat = IndexFormat.UInt32 };
            var verts = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx, v = z / (float)nz;
                    float wx = (u - 0.5f) * width, wz = nearZ + v * depth;
                    float y = RearBermH(wx, wz);
                    verts.Add(new Vector3(wx, y, wz)); uv.Add(new Vector2(u * 2.8f, v * 1.5f));
                }
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            mesh.SetVertices(verts); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject("RearForestBerm"); go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var bermMat = new Material(material) { name = "HeroRearBermMaterial" };
            bermMat.SetColor("_BaseColor", new Color(0.68f, 0.82f, 0.65f));
            go.AddComponent<MeshRenderer>().sharedMaterial = bermMat;
        }

        private static void AddForestLightShafts(Transform parent)
        {
            var shader = Shader.Find("AldenFall/Environment/ForestLightShaft");
            if (shader == null) return;
            var mesh = new Mesh { name = "CanopyLightVolume" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
                new Vector3(-1.9f, 1f, 0f), new Vector3(-0.9f, 1f, 0f),
            };
            mesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 1, 2, 0, 3, 2, 1 };
            mesh.RecalculateBounds();
            var mat = new Material(shader) { name = "WarmCanopyShaft", enableInstancing = true };
            mat.SetColor("_Color", new Color(1.0f, 0.82f, 0.48f, 0.075f));
            var shafts = new (float x, float z, float w, float h, float yaw)[]
            {
                (-0.8f, 2.0f, 2.8f, 8.5f, 5f), (1.6f, 3.8f, 2.0f, 7.2f, -8f),
                (-3.0f, 4.8f, 1.7f, 6.2f, 12f),
            };
            foreach (var s in shafts)
            {
                var go = new GameObject("CanopyLightShaft"); go.transform.SetParent(parent);
                go.transform.position = new Vector3(s.x, GroundH(Mathf.Clamp(s.x, -6f, 6f), Mathf.Clamp(s.z, -6f, 6f)) + 0.05f, s.z);
                go.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                go.transform.localScale = new Vector3(s.w, s.h, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        private static float RearBermH(float wx, float wz)
        {
            const float nearZ = 5.4f, depth = 13f;
            float v = Mathf.Clamp01((wz - nearZ) / depth);
            float u = Mathf.Clamp01(wx / 34f + 0.5f);
            float rise = Mathf.SmoothStep(0f, 1f, v) * 4.6f;
            float roll = (Fbm(u, v, 3.1f) - 0.5f) * Mathf.Lerp(0.35f, 1.15f, v);
            return Mathf.Lerp(GroundH(Mathf.Clamp(wx, -6f, 6f), nearZ), -0.05f,
                Mathf.Clamp01(Mathf.Abs(wx) / 17f)) + rise + roll;
        }

        /// <summary>Color-grade the photographed grass itself instead of relying on a material
        /// multiplier. This preserves the scan's blade detail while lifting blocked shadows and
        /// steering dry yellow/olive pixels toward a humid meadow green.</summary>
        private static void ReviveGrassCard(Texture2D tex)
        {
            var px = tex.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                if (c.a < 0.01f) continue;
                float lum = c.r * 0.30f + c.g * 0.59f + c.b * 0.11f;
                float shadowLift = Mathf.Lerp(1.42f, 1.04f, Mathf.SmoothStep(0f, 1f, lum * 2.2f));
                c.r *= shadowLift * 0.88f;
                c.g *= shadowLift * 1.16f;
                c.b *= shadowLift * 0.93f;
                float deep = Mathf.Clamp01(1f - lum * 4.5f);
                c.r += 0.030f * deep;
                c.g += 0.065f * deep;
                c.b += 0.028f * deep;
                // Add a little blue-green only in the olive midtones; highlights stay photographic.
                float olive = Mathf.Clamp01((c.r - c.b) * 2.2f) * Mathf.Clamp01(1f - lum);
                c.g += 0.075f * olive;
                c.b += 0.035f * olive;
                c.r = Mathf.Clamp01(c.r); c.g = Mathf.Clamp01(c.g); c.b = Mathf.Clamp01(c.b);
                px[i] = c;
            }
            tex.SetPixels(px); tex.Apply(true);
        }

        /// <summary>One shared brightened material set per shrub template, inherited by all its
        /// clones. Asset materials remain untouched.</summary>
        private static void BrightenShrub(GameObject template)
        {
            if (template == null) return;
            var cache = new Dictionary<Material, Material>();
            foreach (var r in template.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var source = mats[i];
                    if (source == null) continue;
                    if (!cache.TryGetValue(source, out var bright))
                    {
                        bright = new Material(source) { name = source.name + "_HeroShrub" };
                        Color baseColor = bright.HasProperty("_BaseColor") ? bright.GetColor("_BaseColor") : Color.white;
                        bright.SetColor("_BaseColor", new Color(baseColor.r * 1.01f, baseColor.g * 1.12f,
                            baseColor.b * 1.04f, baseColor.a));
                        bright.SetFloat("_Smoothness", 0.02f);
                        cache[source] = bright;
                    }
                    mats[i] = bright;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Three crossed quads (0/60/120°), unit size, UP normals — the grass-card holder.</summary>
        private static Mesh BuildStarMesh()
        {
            var mesh = new Mesh { name = "GrassStar" };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            void Quad(Quaternion rot)
            {
                int b = verts.Count;
                verts.Add(rot * new Vector3(-0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.5f, 1f, 0f));
                verts.Add(rot * new Vector3(-0.5f, 1f, 0f));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2,
                                      b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Quad(Quaternion.identity);
            Quad(Quaternion.Euler(0f, 60f, 0f));
            Quad(Quaternion.Euler(0f, 120f, 0f));
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            var upN = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++) upN.Add(Vector3.up);
            mesh.SetNormals(upN);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A compact tuft made from isolated blade regions in grass_medium_02's real
        /// photoscan atlas. Each plane carries one blade (transparent surroundings), so overlap
        /// produces blade density without repeating the scan's chunky root/clod silhouette.</summary>
        private static Mesh BuildPhotographicBladeTuft()
        {
            var mesh = new Mesh { name = "PhotoscanBladeTuft" };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            var normals = new List<Vector3>(); var tris = new List<int>();
            // UV rectangles around the clean isolated blades in grass_medium_02_leafRGBA.
            var bladeUV = new Rect[]
            {
                new Rect(0.00f, 0.00f, 0.13f, 1.00f), new Rect(0.14f, 0.16f, 0.15f, 0.82f),
                new Rect(0.34f, 0.10f, 0.12f, 0.88f), new Rect(0.46f, 0.12f, 0.12f, 0.84f),
                new Rect(0.58f, 0.08f, 0.16f, 0.90f), new Rect(0.78f, 0.04f, 0.11f, 0.72f),
                new Rect(0.88f, 0.02f, 0.12f, 0.97f),
            };
            var rng = new System.Random(7721);
            for (int i = 0; i < 15; i++)
            {
                float angle = i * (137.5f * Mathf.Deg2Rad) + (float)rng.NextDouble() * 0.35f;
                float radius = Mathf.Sqrt((float)rng.NextDouble()) * 0.34f;
                float height = 0.72f + (float)rng.NextDouble() * 0.34f;
                float width = 0.075f + (float)rng.NextDouble() * 0.045f;
                var center = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);
                // Small lateral offset at the top gives each photographic blade a natural lean.
                var lean = new Vector3(Mathf.Cos(angle + 0.8f), 0f, Mathf.Sin(angle + 0.8f))
                    * (0.035f + 0.05f * (float)rng.NextDouble());
                int b = verts.Count;
                verts.Add(center - right); verts.Add(center + right);
                verts.Add(center + right + Vector3.up * height + lean);
                verts.Add(center - right + Vector3.up * height + lean);
                Rect uv = bladeUV[i % bladeUV.Length];
                uvs.Add(new Vector2(uv.xMin, uv.yMin)); uvs.Add(new Vector2(uv.xMax, uv.yMin));
                uvs.Add(new Vector2(uv.xMax, uv.yMax)); uvs.Add(new Vector2(uv.xMin, uv.yMax));
                for (int n = 0; n < 4; n++) normals.Add(Vector3.up);
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2,
                                      b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Collapse grass into spatial cells. Each cell/material pair is one renderer,
        /// preserving culling granularity while eliminating thousands of per-tuft renderers.</summary>
        private static void BuildGrassBatches(Transform parent, Mesh source, List<Material> materials,
            List<Matrix4x4>[] matrices)
        {
            const float cellSize = 8f;
            var groups = new Dictionary<(int mat, int x, int z), List<Matrix4x4>>();
            for (int mi = 0; mi < matrices.Length; mi++)
                foreach (var m in matrices[mi])
                {
                    Vector3 p = m.GetColumn(3);
                    var key = (mi, Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<Matrix4x4>();
                    list.Add(m);
                }

            var sv = source.vertices; var su = source.uv; var sn = source.normals; var si = source.triangles;
            foreach (var pair in groups)
            {
                var transforms = pair.Value;
                var v = new List<Vector3>(sv.Length * transforms.Count);
                var u = new List<Vector2>(su.Length * transforms.Count);
                var n = new List<Vector3>(sn.Length * transforms.Count);
                var idx = new List<int>(si.Length * transforms.Count);
                foreach (var m in transforms)
                {
                    int offset = v.Count;
                    for (int i = 0; i < sv.Length; i++)
                    {
                        v.Add(m.MultiplyPoint3x4(sv[i])); u.Add(su[i]);
                        n.Add(m.MultiplyVector(sn[i]).normalized);
                    }
                    for (int i = 0; i < si.Length; i++) idx.Add(offset + si[i]);
                }
                var mesh = new Mesh
                {
                    name = $"GrassCell_{pair.Key.x}_{pair.Key.z}_{pair.Key.mat}",
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.SetVertices(v); mesh.SetUVs(0, u); mesh.SetNormals(n); mesh.SetTriangles(idx, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(mesh.name); go.transform.SetParent(parent);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[pair.Key.mat];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Load a kit FBX + its saved/derived materials once; returns a template GameObject
        /// (deactivated) to clone via PlaceClump. Materials come from TreeKitImporter.BuildMaterials —
        /// safe for these assets: none are in the baked-prefab set, so no references can break.</summary>
        private static GameObject KitClump(string name)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Trees/{name}/{name}.fbx");
            if (fbx == null) { Debug.LogWarning($"COLORLAB no fbx {name}"); return null; }
            var mats = TreeKitImporter.BuildMaterials(name);
            if (mats == null) return null;
            var go = (GameObject)Object.Instantiate(fbx);
            go.name = $"__tpl_{name}";
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var shared = r.sharedMaterials;
                for (int s = 0; s < shared.Length; s++)
                {
                    string n = (shared[s] != null ? shared[s].name : "").ToLower();
                    bool isLeaf = n.Contains("leaf") || n.Contains("leaves") || n.Contains("twig") || n.Contains("foliage")
                        || n.Contains("grass") || n.Contains("moss");
                    shared[s] = isLeaf ? mats.Value.leaf : mats.Value.bark;
                }
                r.sharedMaterials = shared;
            }
            go.SetActive(false);
            return go;
        }

        /// <summary>Clone a kit template at pos: normalize HEIGHT, ground the base on the hero terrain.</summary>
        private static void PlaceClump(Transform parent, GameObject template, Vector3 pos, float height, float yaw)
        {
            var go = (GameObject)Object.Instantiate(template, parent);
            go.SetActive(true);
            var b = Measure(go);
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float scale = maxDim > 0.001f ? height / Mathf.Max(0.05f, b.size.y) : 1f;
            // grass clumps are wide & short — scale off height but cap total so a flat scan can't blow up
            scale = Mathf.Min(scale, height * 6f / Mathf.Max(0.05f, maxDim));
            go.transform.localScale *= scale;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            b = Measure(go);
            pos.y = GroundH(pos.x, pos.z) + pos.y;
            go.transform.position += pos - new Vector3(b.center.x, b.min.y - 0.01f, b.center.z);
        }

        /// <summary>Photoreal ground: forest_ground_04 (grassy) blended into brown_mud_leaves_01 (bare)
        /// by the shared patch mask — albedo AND normal, baked 2048px across the 12m plane.</summary>
        private static Material HeroGroundMaterial(GrassSet set)
        {
            const string dir = "Assets/Textures/Ground/PolyHaven";
            var layered = LayeredForestGroundMaterial(dir);
            if (layered != null) return layered;
            Texture2D Load(string f, bool linear)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
                t.LoadImage(File.ReadAllBytes($"{dir}/{f}"));
                return t;
            }
            var gA = Load("forest_ground_04_diff_2k.jpg", false);
            var gN = Load("forest_ground_04_nor_gl_2k.jpg", true);
            var mA = Load("brown_mud_leaves_01_diff_2k.jpg", false);
            var mN = Load("brown_mud_leaves_01_nor_gl_2k.jpg", true);
            var lA = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            lA.LoadImage(File.ReadAllBytes($"{dir}/forest_leaves_02/forest_leaves_02_diffuse_2k.jpg"));
            var lN = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            lN.LoadImage(File.ReadAllBytes($"{dir}/forest_leaves_02/forest_leaves_02_nor_gl_2k.jpg"));
            var gAp = gA.GetPixels(); var gNp = gN.GetPixels();
            var mAp = mA.GetPixels(); var mNp = mN.GetPixels();
            var lAp = lA.GetPixels(); var lNp = lN.GetPixels();
            int sw = gA.width;   // 2048, all four match

            const int S = 2048;
            var alb = new Color[S * S]; var nor = new Color[S * S];
            const float tiles = 6f;   // source scans are ~2m physical over the 12m plane
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)(S - 1), v = y / (float)(S - 1);
                    int sx = (int)(u * tiles * sw) % sw, sy = (int)(v * tiles * sw) % sw;
                    int si = sy * sw + sx, di = y * S + x;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                        (PatchAt(u, v) + (Fbm(u, v, 25f) - 0.5f) * 0.18f - 0.46f) / 0.14f));
                    Color a = Color.Lerp(mAp[si], gAp[si], t);
                    // Broad shaded colonies receive real moss/leaf/twig litter. Keep it strongest
                    // away from dense grass cores so the ecology reads in layers rather than soup.
                    float leafMacro = Mathf.SmoothStep(0.42f, 0.68f, Fbm(u, v, 2.15f));
                    float leafMask = leafMacro * Mathf.Lerp(0.82f, 0.18f, t);
                    a = Color.Lerp(a, lAp[si], leafMask * 0.72f);
                    // pull the grassy scan toward the picked green so ground + blades stay one family
                    // (gently — the earlier 0.35 pull crushed the floor into dark mud)
                    float lum = a.r * 0.30f + a.g * 0.59f + a.b * 0.11f;
                    Color target = Color.Lerp(set.gDark, set.gLight, Mathf.Clamp01(lum * 2f));
                    a = Color.Lerp(a, target * (lum * 2.4f), t * 0.20f);
                    a *= 1.12f;   // lift the whole floor out of the mud
                    a.r = Mathf.Clamp01(a.r); a.g = Mathf.Clamp01(a.g); a.b = Mathf.Clamp01(a.b);
                    a.a = 0f;
                    alb[di] = a;
                    Color baseN = Color.Lerp(mNp[si], gNp[si], t);
                    nor[di] = Color.Lerp(baseN, lNp[si], leafMask * 0.72f);
                }
            var albT = new Texture2D(S, S, TextureFormat.RGBA32, true);
            albT.SetPixels(alb); albT.Apply(true);
            var norT = new Texture2D(S, S, TextureFormat.RGBA32, true, true);
            norT.SetPixels(nor); norT.Apply(true);
            Object.DestroyImmediate(gA); Object.DestroyImmediate(gN);
            Object.DestroyImmediate(mA); Object.DestroyImmediate(mN);
            Object.DestroyImmediate(lA); Object.DestroyImmediate(lN);

            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", albT);
            m.SetTexture("_BumpMap", norT);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Smoothness", 0.02f);
            return m;
        }

        private static Material LayeredForestGroundMaterial(string dir)
        {
            var shader = Shader.Find("AldenFall/Environment/ForestTerrain");
            if (shader == null) return null;
            Texture2D Load(string path, bool linear)
            {
                if (!File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                t.LoadImage(File.ReadAllBytes(path));
                t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 8;
                return t;
            }
            var dirt = Load($"{dir}/brown_mud_leaves_01_diff_2k.jpg", false);
            var dirtN = Load($"{dir}/brown_mud_leaves_01_nor_gl_2k.jpg", true);
            var grass = Load($"{dir}/forest_ground_04_diff_2k.jpg", false);
            var grassN = Load($"{dir}/forest_ground_04_nor_gl_2k.jpg", true);
            var leaf = Load($"{dir}/forest_leaves_02/forest_leaves_02_diffuse_2k.jpg", false);
            var leafN = Load($"{dir}/forest_leaves_02/forest_leaves_02_nor_gl_2k.jpg", true);
            if (dirt == null || dirtN == null || grass == null || grassN == null || leaf == null || leafN == null)
                return null;

            const int S = 1024;
            var control = new Texture2D(S, S, TextureFormat.RGB24, true, true)
            { name = "ForestEcologyControl", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)(S - 1), v = y / (float)(S - 1);
                    float patch = PatchAt(u, v);
                    float grassW = Mathf.SmoothStep(0.43f, 0.66f, patch);
                    float pathCenter = 0.50f + Mathf.Sin(v * 5.2f + 0.4f) * 0.075f;
                    float path = 1f - Mathf.SmoothStep(0.045f, 0.14f, Mathf.Abs(u - pathCenter));
                    float leafW = Mathf.SmoothStep(0.43f, 0.68f, Fbm(u, v, 2.15f)) * (1f - path) * (1f - grassW * 0.72f);
                    grassW *= 1f - path * 0.96f;
                    float dirtW = Mathf.Max(0.04f, 1f - grassW - leafW) + path * 1.6f;
                    float sum = grassW + leafW + dirtW;
                    px[y * S + x] = new Color(grassW / sum, leafW / sum, dirtW / sum, 1f);
                }
            control.SetPixels(px); control.Apply(true);
            var m = new Material(shader) { name = "HeroLayeredForestGround", enableInstancing = true };
            m.SetTexture("_DirtMap", dirt); m.SetTexture("_DirtNormal", dirtN);
            m.SetTexture("_GrassMap", grass); m.SetTexture("_GrassNormal", grassN);
            m.SetTexture("_LeafMap", leaf); m.SetTexture("_LeafNormal", leafN);
            m.SetTexture("_ControlMap", control); m.SetFloat("_Tiling", 6.5f);
            m.SetFloat("_NormalStrength", 1.05f); m.SetColor("_MacroTint", new Color(1.24f, 1.16f, 1.02f));
            return m;
        }

        [MenuItem("AldenFall/Color Lab Grid")]
        public static void RenderGrid()
        {
            Directory.CreateDirectory(OutDir);
            int rows = GrassSets.Length, cols = TreeVariants.Length;

            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.6f);
            var lightGO = new GameObject("LabSun");
            var sun = lightGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.00f, 0.97f, 0.90f);
            sun.intensity = 1.5f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGO = new GameObject("LabCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.72f, 0.82f);
            cam.fieldOfView = 52f;
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
            camGO.transform.position = new Vector3(0f, 2.6f, -8.2f);
            camGO.transform.LookAt(new Vector3(0f, 2.0f, 2.5f));

            var grid = new Texture2D(cols * CellW, rows * CellH, TextureFormat.RGB24, false);
            var rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32); rt.Create();
            cam.targetTexture = rt;

            var legend = new System.Text.StringBuilder();
            legend.Append("ColorLab v3 'AAA ground'. Rows top->bottom = grass greens, cols = tree foliage density.\n");
            legend.Append("All cells: soil+grass-patch ground, clumped tufts, ferns/flowers, GRAY granite boulder.\n");
            foreach (var g in GrassSets)
                legend.Append($"Row {g.name}: blades {C(g.bladeDark)}->{C(g.bladeLight)} patchGround {C(g.gDark)}->{C(g.gLight)}\n");
            foreach (var t in TreeVariants) legend.Append($"Col {t.name}\n");
            legend.Append($"Leaf tint (all): {C(LeafTint)}  Soil: {C(DirtDark)}/{C(DirtMid)}/{C(DirtLight)}\n");

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var cell = BuildCell(GrassSets[r], TreeVariants[c].mode);
                    var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                    if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    grid.ReadPixels(new Rect(0, 0, CellW, CellH), c * CellW, (rows - 1 - r) * CellH);
                    RenderTexture.active = prev;
                    Object.DestroyImmediate(cell);
                }
            grid.Apply();

            cam.targetTexture = null; rt.Release();
            File.WriteAllBytes($"{OutDir}/colorlab_grid.png", grid.EncodeToPNG());
            File.WriteAllText($"{OutDir}/legend.txt", legend.ToString());
            Debug.Log($"COLORLAB: {rows}x{cols} grid -> {OutDir}/colorlab_grid.png");

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(grid);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(lightGO);
        }

        private static string C(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00})";

        // ---- patch noise: ONE field drives both the ground texture and where tufts/plants clump,
        //      so grass blades stand exactly on the grassy parts of the soil. Plane-local uv 0..1. ----
        private static float Fbm(float u, float v, float f) =>
            Mathf.PerlinNoise(u * f + 7.31f, v * f + 2.17f) * 0.6f
            + Mathf.PerlinNoise(u * f * 2f + 13.7f, v * f * 2f + 5.9f) * 0.3f
            + Mathf.PerlinNoise(u * f * 4f + 29.1f, v * f * 4f + 17.3f) * 0.1f;
        private static float PatchAt(float u, float v) => Fbm(u, v, 3.5f);

        /// <summary>One diorama: soil+patch ground, clumped tufts, ferns/flowers, gray boulder, tree.</summary>
        private static GameObject BuildCell(GrassSet set, TreeMode treeMode)
        {
            var root = new GameObject("ColorLabCell");

            // ---- ground: warm soil with organic grass patches (UNTILED across the 10m plane) ----
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)(S - 1), v = y / (float)(S - 1);
                    float soil = Fbm(u, v, 9f);
                    float grain = Fbm(u, v, 45f);
                    Color dirt = Color.Lerp(Color.Lerp(DirtDark, DirtMid, Mathf.Clamp01(soil * 1.5f)),
                                            DirtLight, Mathf.Clamp01((grain - 0.55f) * 1.6f));
                    float streak = Fbm(u, v, 30f);
                    Color grass = Color.Lerp(set.gDark, set.gLight, Mathf.Clamp01((streak - 0.30f) * 1.8f));
                    // patch blend with a noisy edge so patches feather into the dirt organically
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                        (PatchAt(u, v) + (Fbm(u, v, 25f) - 0.5f) * 0.18f - 0.46f) / 0.14f));
                    Color c = Color.Lerp(dirt, grass, t);
                    c.a = 0f;   // matte (albedo alpha drives smoothness on terrain — keep the habit)
                    px[y * S + x] = c;
                }
            var groundTex = new Texture2D(S, S, TextureFormat.RGBA32, true);
            groundTex.SetPixels(px); groundTex.Apply(true);
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetTexture("_BaseMap", groundTex);
            groundMat.SetFloat("_Smoothness", 0f);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);   // 10x10m
            plane.name = "Ground";
            plane.transform.SetParent(root.transform);
            Object.DestroyImmediate(plane.GetComponent<Collider>());
            plane.GetComponent<Renderer>().sharedMaterial = groundMat;

            // ---- grass tufts CLUMPED inside the patches (bare soil between) ----
            var tuftMesh = BuildTuftMesh();
            var tuftMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var tintedBlades = TintedBladeTexture(set.bladeDark, set.bladeLight);
            if (tintedBlades != null)
            {
                tuftMat.SetTexture("_BaseMap", tintedBlades);
                tuftMat.SetFloat("_AlphaClip", 1f); tuftMat.EnableKeyword("_ALPHATEST_ON");
                tuftMat.SetFloat("_Cutoff", 0.3f);
                if (tuftMat.HasProperty("_Cull")) tuftMat.SetFloat("_Cull", 0f);
            }
            tuftMat.SetFloat("_Smoothness", 0.05f);
            var rng = new System.Random(42);   // fixed seed: identical layout in every cell
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int placed = 0;
            for (int i = 0; i < 900 && placed < 320; i++)
            {
                float wx = Rand(-5f, 5f), wz = Rand(-4.8f, 4f);
                float patch = PatchAt((wx + 5f) / 10f, (wz + 5f) / 10f);
                if (patch < 0.52f) continue;   // tufts only on the grassy parts of the soil
                placed++;
                var t = new GameObject("Tuft");
                t.transform.SetParent(root.transform);
                t.AddComponent<MeshFilter>().sharedMesh = tuftMesh;
                var mr = t.AddComponent<MeshRenderer>();
                mr.sharedMaterial = tuftMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                t.transform.position = new Vector3(wx, 0f, wz);
                t.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                // denser patch core -> slightly taller grass; edges shorter
                float vigor = Mathf.InverseLerp(0.52f, 0.75f, patch);
                t.transform.localScale = new Vector3(Rand(0.7f, 1.4f),
                    Rand(0.35f, 0.6f) + 0.3f * vigor, Rand(0.7f, 1.4f));
            }

            // ---- biodiversity sprinkle: ferns + flowers inside the grass patches ----
            var flora = new (string kind, float h, int n)[]
                { ("fern_02", 0.55f, 6), ("dandelion_01", 0.35f, 5), ("celandine_01", 0.30f, 4) };
            foreach (var f in flora)
            {
                int done = 0;
                for (int i = 0; i < 200 && done < f.n; i++)
                {
                    float wx = Rand(-4.6f, 4.6f), wz = Rand(-4.4f, 3.6f);
                    if (PatchAt((wx + 5f) / 10f, (wz + 5f) / 10f) < 0.55f) continue;
                    done++;
                    PlaceKit(root.transform, f.kind, new Vector3(wx, 0f, wz), f.h * (0.8f + (float)rng.NextDouble() * 0.5f), null, 0f);
                }
            }

            // ---- the SAME boulder, desaturated to gray granite (Jon: orange reads desert) ----
            var grayRock = GrayRockMaterial("namaqualand_boulder_02");
            var rock = PlaceKit(root.transform, "namaqualand_boulder_02", new Vector3(3.3f, 0f, 1.2f), 1.7f, null, 0f);
            if (rock != null && grayRock != null)
                foreach (var r in rock.GetComponentsInChildren<Renderer>()) r.sharedMaterial = grayRock;

            // ---- the tree, per foliage-density variant ----
            BuildTree(root.transform, treeMode);

            return root;
        }

        private static GameObject BuildTree(Transform parent, TreeMode mode)
        {
            const string name = "jacaranda_tree";
            var pos = new Vector3(0f, 0f, 3.4f);
            GameObject go;
            if (mode == TreeMode.RawFullCanopy)
            {
                // The un-decimated photoscan with the ALREADY-SAVED kit materials (do NOT call
                // BuildMaterials here — it deletes+recreates the .mat assets and would orphan the
                // baked prefabs' references). This column is the "not dying" ceiling.
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Trees/{name}/{name}.fbx");
                var leaf = PhotorealTreeMaterial(name, "leaf");
                var bark = PhotorealTreeMaterial(name, "trunk");
                var branches = PhotorealTreeMaterial(name, "branches");
                if (fbx == null || leaf == null || bark == null)
                { Debug.LogWarning($"COLORLAB raw tree bits missing for {name}"); return null; }
                go = (GameObject)Object.Instantiate(fbx, parent);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = (mats[i] != null ? mats[i].name : "").ToLower();
                        bool isLeaf = n.Contains("leaf") || n.Contains("leaves") || n.Contains("twig") || n.Contains("foliage");
                        mats[i] = isLeaf ? leaf : n.Contains("branch") && branches != null ? branches : bark;
                    }
                    r.sharedMaterials = mats;
                }
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeKitImporter.PrefabDir}/{name}.prefab");
                if (prefab == null) { Debug.LogWarning($"COLORLAB missing kit prefab {name}"); return null; }
                go = (GameObject)Object.Instantiate(prefab, parent);
                var lod = go.GetComponentInChildren<LODGroup>();
                if (lod != null) lod.ForceLOD(0);
            }

            // scale to world tree height, ground the base
            var b = Measure(go);
            float scale = b.size.y > 0.01f ? 10.5f / b.size.y : 1f;
            go.transform.localScale *= scale;
            b = Measure(go);
            go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);

            // tint leaves (copies — never mutate shared assets); thick-alpha variant lowers cutoff
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name.ToLower();
                    if (!n.Contains("leaf") && !n.Contains("leaves") && !n.Contains("twig") && !n.Contains("foliage")) continue;
                    var copy = new Material(mats[i]);
                    copy.SetColor("_BaseColor", LeafTint);
                    if (mode == TreeMode.BakedThickAlpha) copy.SetFloat("_Cutoff", 0.30f);
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>Build a complete URP material from the source scan maps. Several generated
        /// kit materials retained only the leaf color texture and silently dropped trunk/branch
        /// albedo and every normal map, which made a high-end scan render like flat game art.</summary>
        private static Material PhotorealTreeMaterial(string asset, string part)
        {
            string dir = $"Assets/Models/Trees/{asset}";
            string prefix = $"{asset}_{part}";
            string diff = part == "leaf" ? $"{dir}/{asset}_leafRGBA.png" : $"{dir}/{prefix}_diff_1k.jpg";
            string normal = part == "leaf" ? $"{dir}/{asset}_leaves_nor_gl_1k.jpg" : $"{dir}/{prefix}_nor_gl_1k.jpg";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(diff);
            if (albedo == null) return null;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = prefix + "_Photoreal", enableInstancing = true };
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", part == "leaf" ? LeafTint : Color.white);
            mat.SetFloat("_Smoothness", part == "leaf" ? 0.16f : 0.11f);
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            if (n != null)
            {
                mat.SetTexture("_BumpMap", n); mat.SetFloat("_BumpScale", part == "leaf" ? 0.75f : 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (part == "leaf")
            {
                mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.38f);
                mat.EnableKeyword("_ALPHATEST_ON"); mat.renderQueue = (int)RenderQueue.AlphaTest;
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                mat.doubleSidedGI = true;
            }
            return mat;
        }

        private static GameObject BuildRawTreeVariant(Transform parent, string name, Vector3 pos, float height)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Trees/{name}/{name}.fbx");
            var leaf = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Models/Trees/{name}/{name}_leaf.mat");
            var bark = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Models/Trees/{name}/{name}_bark.mat");
            if (fbx == null || leaf == null || bark == null) return null;
            var leafTinted = new Material(leaf) { name = name + "_leaf_Hero" };
            leafTinted.SetColor("_BaseColor", LeafTint);
            var go = (GameObject)Object.Instantiate(fbx, parent);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = (mats[i] != null ? mats[i].name : "").ToLower();
                    bool isLeaf = mn.Contains("leaf") || mn.Contains("leaves") || mn.Contains("twig") || mn.Contains("foliage");
                    mats[i] = isLeaf ? leafTinted : bark;
                }
                r.sharedMaterials = mats;
            }
            var b = Measure(go);
            go.transform.localScale *= b.size.y > 0.01f ? height / b.size.y : 1f;
            b = Measure(go);
            go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
            return go;
        }

        private static GameObject PlaceExistingTreeImpostor(Transform parent, string species,
            float x, float z, float height, float yaw)
        {
            const string dir = "Assets/Materials/Emberfall/Impostors";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/ImpostorQuad.mesh");
            var sourceMat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{species}_impostor.mat");
            if (mesh == null || sourceMat == null) return null;
            if (!LabImpostorMaterials.TryGetValue(species, out var mat))
            {
                mat = new Material(sourceMat) { name = species + "_LabImpostor" };
                // Preserve the photographed bake. A sub-0.3 multiplier collapsed the distant
                // crowns into military-green silhouettes; atmosphere should create the value
                // separation, not destructive albedo tinting.
                Color tint = species == "PineTree" ? new Color(0.72f, 0.83f, 0.64f, 1f)
                    : species == "BirchTree" ? new Color(0.78f, 0.88f, 0.70f, 1f)
                    : new Color(0.74f, 0.86f, 0.67f, 1f);
                mat.SetColor("_Tint", tint); mat.enableInstancing = true;
                LabImpostorMaterials[species] = mat;
            }
            float aspect = species == "OakTree" ? 0.7234f : species == "BirchTree" ? 0.4114f : 0.5864f;
            var go = new GameObject(species + "_DistantImpostor"); go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.position = new Vector3(x, RearBermH(x, z), z);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(height * aspect, height, 1f);
            return go;
        }

        /// <summary>The boulder's own photoscan albedo pulled ~85% toward its luminance — same rock,
        /// gray granite instead of desert orange.</summary>
        private static Material GrayRockMaterial(string name)
        {
            string dir = $"Assets/Models/Trees/{name}";
            if (!Directory.Exists(dir)) return null;
            string diff = null;
            foreach (var f in Directory.GetFiles(dir))
            {
                string fn = Path.GetFileName(f).ToLower();
                if ((fn.EndsWith(".jpg") || fn.EndsWith(".png")) && (fn.Contains("diff") || fn.Contains("albedo")))
                { diff = f; break; }
            }
            if (diff == null) return null;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(diff));
            var p = src.GetPixels();
            for (int i = 0; i < p.Length; i++)
            {
                float lum = p[i].r * 0.30f + p[i].g * 0.59f + p[i].b * 0.11f;
                p[i] = Color.Lerp(new Color(lum, lum, lum), p[i], 0.15f);
            }
            var outT = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true);
            outT.SetPixels(p); outT.Apply(true);
            Object.DestroyImmediate(src);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", outT);
            m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        private static GameObject PlaceKit(Transform parent, string name, Vector3 pos, float height, Color? leafTint, float _)
        {
            var template = OptimizedKitTemplate(name);
            if (template == null) return null;
            var go = (GameObject)Object.Instantiate(template, parent);
            go.SetActive(true);
            var b = Measure(go);
            float scale = b.size.y > 0.01f ? height / b.size.y : 1f;
            // Flat roots and broad ground-cover scans can be metres wide but only centimetres
            // tall. Height-only normalization turns them into scene-spanning slabs.
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (maxDim > 0.001f) scale = Mathf.Min(scale, height * 3.5f / maxDim);
            go.transform.localScale *= scale;
            b = Measure(go);
            go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
            return go;
        }

        private static GameObject OptimizedKitTemplate(string name)
        {
            if (OptimizedKitTemplates.TryGetValue(name, out var existing) && existing != null) return existing;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeKitImporter.PrefabDir}/{name}.prefab");
            // Some of the best Poly Haven families have not been through the world-prefab baker
            // yet. Falling back to their real FBX + generated scan materials keeps them visible in
            // the lab, then this method still collapses each species to shared material batches.
            var template = prefab != null ? (GameObject)Object.Instantiate(prefab) : KitClump(name);
            if (template == null) { Debug.LogWarning($"COLORLAB missing source asset {name}"); return null; }
            template.SetActive(true);
            template.name = "__optimized_" + name;
            var lod = template.GetComponentInChildren<LODGroup>();
            if (lod != null) { lod.ForceLOD(0); lod.enabled = false; }
            OptimizeHierarchyByMaterial(template);
            template.SetActive(false);
            OptimizedKitTemplates[name] = template;
            return template;
        }

        private static void ClearOptimizedKitTemplates()
        {
            foreach (var pair in OptimizedKitTemplates)
                if (pair.Value != null) Object.DestroyImmediate(pair.Value);
            OptimizedKitTemplates.Clear();
        }

        private static void ClearLabImpostorMaterials()
        {
            foreach (var pair in LabImpostorMaterials)
                if (pair.Value != null) Object.DestroyImmediate(pair.Value);
            LabImpostorMaterials.Clear();
        }

        /// <summary>Place a kit asset with a full authored orientation, then re-ground its rotated
        /// bounds. Used for real fallen trunks and exposed-root scans.</summary>
        private static GameObject PlaceKitPose(Transform parent, string name, Vector3 pos, float height, Vector3 euler)
        {
            var go = PlaceKit(parent, name, Vector3.zero, height, null, 0f);
            if (go == null) return null;
            go.transform.rotation = Quaternion.Euler(euler) * go.transform.rotation;
            var b = Measure(go);
            go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
            return go;
        }

        /// <summary>Collapse a fragmented photoscan hierarchy into one mesh per material family.
        /// Optimizing the template before cloning means all tree-line instances share these meshes.</summary>
        private static void OptimizeHierarchyByMaterial(GameObject root)
        {
            var groups = new Dictionary<string, (Material material, List<CombineInstance> parts)>();
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = renderer.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = renderer.sharedMaterials;
                int count = Mathf.Min(mf.sharedMesh.subMeshCount, mats.Length);
                for (int s = 0; s < count; s++)
                {
                    var mat = mats[s]; if (mat == null) continue;
                    string key = mat.name.Replace(" (Instance)", "").Replace("_HeroShrub", "");
                    if (!groups.TryGetValue(key, out var group))
                        group = (mat, new List<CombineInstance>());
                    group.parts.Add(new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        subMeshIndex = s,
                        transform = toRoot * mf.transform.localToWorldMatrix,
                    });
                    groups[key] = group;
                }
                renderer.enabled = false;
            }
            foreach (var pair in groups)
            {
                var mesh = new Mesh { name = "OptimizedTree_" + pair.Key, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.parts.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                var child = new GameObject(mesh.name); child.transform.SetParent(root.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                child.AddComponent<MeshRenderer>().sharedMaterial = pair.Value.material;
            }
        }

        /// <summary>Place a real textured prop FBX with one shared PBR material for the instance.
        /// This keeps the lab independent of scene prefabs while preserving source geometry/maps.</summary>
        private static GameObject PlaceProp(Transform parent, string name, Vector3 pos, float height, float yaw, Color tint)
        {
            string dir = $"Assets/Models/Props/{name}";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}.fbx");
            if (fbx == null) { Debug.LogWarning($"COLORLAB missing prop {name}"); return null; }
            var go = (GameObject)Object.Instantiate(fbx, parent);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"Hero_{name}" };
            Texture2D LoadMap(string suffix, bool linear)
            {
                string path = $"{dir}_{suffix}.jpg";
                if (!File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                t.LoadImage(File.ReadAllBytes(path)); t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4;
                return t;
            }
            var baseMap = LoadMap("baseColor", false);
            var normal = LoadMap("normal", true);
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Smoothness", name == "Campfire" ? 0.12f : 0.03f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            var b = Measure(go);
            float scale = b.size.y > 0.001f ? height / b.size.y : 1f;
            go.transform.localScale *= scale;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            b = Measure(go);
            pos.y += GroundH(Mathf.Clamp(pos.x, -6f, 6f), Mathf.Clamp(pos.z, -6f, 6f));
            go.transform.position += pos - new Vector3(b.center.x, b.min.y, b.center.z);
            return go;
        }

        /// <summary>The grey desat blade texture multiplied by a vertical dark(base)->light(tip)
        /// gradient — URP Lit ignores vertex colours, so hue must live in the texture (lab v2 finding).</summary>
        private static Texture2D TintedBladeTexture(Color dark, Color light)
        {
            const string path = "Assets/Textures/Ground/grass_tuft_desat.png";
            if (!File.Exists(path)) { Debug.LogWarning("COLORLAB missing " + path); return null; }
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(path));
            var px = src.GetPixels();
            int w = src.width, h = src.height;
            // Normalize by the texture's own mean luminance (opaque pixels) so mid-grey lands ON the
            // target colour — the blind x2 of lab v2 rendered the tufts near-black.
            float sum = 0f; int cnt = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a > 0.5f) { sum += px[i].r * 0.30f + px[i].g * 0.59f + px[i].b * 0.11f; cnt++; }
            float norm = cnt > 0 && sum > 0.001f ? Mathf.Clamp(cnt / sum, 1.2f, 4f) : 2f;
            for (int y = 0; y < h; y++)
            {
                Color g = Color.Lerp(dark, light, y / (float)(h - 1));
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float a = px[i].a;
                    px[i] = new Color(
                        Mathf.Clamp01(px[i].r * norm * g.r),
                        Mathf.Clamp01(px[i].g * norm * g.g),
                        Mathf.Clamp01(px[i].b * norm * g.b), a);
                }
            }
            var outT = new Texture2D(w, h, TextureFormat.RGBA32, true);
            outT.SetPixels(px); outT.Apply(true);
            Object.DestroyImmediate(src);
            return outT;
        }

        /// <summary>Crossed-quad tuft mesh (MakeGrassTuftPrefab geometry, forced UP normals).</summary>
        private static Mesh BuildTuftMesh()
        {
            var mesh = new Mesh { name = "LabTuft" };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            var tris = new List<int>();
            void Quad(Quaternion rot)
            {
                int b = verts.Count;
                verts.Add(rot * new Vector3(-0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.35f, 1f, 0f));
                verts.Add(rot * new Vector3(-0.35f, 1f, 0f));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2,
                                      b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Quad(Quaternion.identity);
            Quad(Quaternion.Euler(0f, 90f, 0f));
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var upN = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++) upN.Add(Vector3.up);
            mesh.SetNormals(upN);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Bounds Measure(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        private static void MarkStaticRecursive(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = true;
        }

        private static void ReportHeroStats(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var meshes = new HashSet<Mesh>(); var materials = new HashSet<Material>();
            var families = new Dictionary<string, int>();
            long sourceTriangles = 0, submittedTriangles = 0; int shadowCasters = 0, activeRenderers = 0;
            foreach (var r in renderers)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                activeRenderers++;
                string family = r.gameObject.name;
                int split = family.IndexOfAny(new[] { '_', ' ' });
                if (split > 0) family = family.Substring(0, split);
                families[family] = families.TryGetValue(family, out int count) ? count + 1 : 1;
                foreach (var m in r.sharedMaterials) if (m != null) materials.Add(m);
                if (r.shadowCastingMode != ShadowCastingMode.Off) shadowCasters++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    submittedTriangles += (long)mf.sharedMesh.GetIndexCount(s) / 3L;
                if (!meshes.Add(mf.sharedMesh)) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    sourceTriangles += (long)mf.sharedMesh.GetIndexCount(s) / 3L;
            }
            Debug.Log($"COLORLAB STATS renderers={activeRenderers} uniqueMeshes={meshes.Count} " +
                $"uniqueMaterials={materials.Count} uniqueSourceTriangles={sourceTriangles} " +
                $"submittedTriangles={submittedTriangles} shadowCasters={shadowCasters} GPUResidentDrawer=eligible");
            var ordered = new List<KeyValuePair<string, int>>(families);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
            var census = new System.Text.StringBuilder("COLORLAB RENDERER CENSUS");
            for (int i = 0; i < Mathf.Min(15, ordered.Count); i++)
                census.Append($" {ordered[i].Key}={ordered[i].Value}");
            Debug.Log(census.ToString());
        }
    }
}
