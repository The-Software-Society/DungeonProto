#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.AI.Navigation;
using System.Collections.Generic;

namespace AldenFall.Editor
{
    /// <summary>
    /// Emberfall Courtyard — the playable demo scene.
    ///
    /// A compact walled arena at dusk: torch-lit brazier plaza, a raised terrace,
    /// a swimmable pond, scattered props, and three goblins. Built to showcase the
    /// full gameplay loop (target → fireball/nova → dodge → swim) in one screen.
    ///
    /// Menu:     AldenFall > Build Emberfall Demo
    /// Headless: Unity -batchmode -executeMethod AldenFall.Editor.ArenaBuilder.BuildDemoHeadless
    /// </summary>
    public static partial class ArenaBuilder
    {
        private const string ScenePath = "Assets/Scenes/EmberfallDemo.unity";
        private const string MaterialDir = "Assets/Materials/Emberfall";

        // Arena dimensions
        private const float ArenaHalf = 42f;     // playfield is 84x84
        private const float TileSize = 4f;
        private const float WallHeight = 7f;
        private const float WallThickness = 1.5f;

        // Pond (southwest corner) — aligned to the 4u tile grid so the floor
        // cutout is exact. Kept far from the courtyard goblin spawner at (0,8)
        // (detectionRange 25) so fishing never pulls aggro; expanded to 24x16.
        private static readonly Rect PondRect = new Rect(-38f, -34f, 24f, 16f); // x,z,w,d
        private const float PondDepth = 3f;
        // Slightly BELOW the surrounding ground so shorelines read as banks dropping to
        // water — at +0.1 the surface plane floated above the shore with a hard cube
        // edge (QA shot 05). Swim depth stays 2.25m (> the 1.3m swim-enter threshold).
        private const float WaterSurfaceY = -0.35f;

        // Terrace (north side)
        private static readonly Rect TerraceRect = new Rect(-14f, 30f, 28f, 12f);
        private const float TerraceHeight = 2.4f;

        private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private static Shader litShader;
        private static System.Random rng;

        // ==================== ENTRY POINTS ====================

        [MenuItem("AldenFall/Build Emberfall Demo", priority = 0)]
        public static void BuildDemoFromMenu()
        {
            if (!Application.isBatchMode)
            {
                if (!EditorUtility.DisplayDialog("Build Emberfall Demo",
                    "This creates a NEW scene (Emberfall Courtyard) with the full demo:\n\n" +
                    "- Torch-lit dusk arena with post-processing\n" +
                    "- Swimmable pond, terrace, props\n" +
                    "- Player, 3 goblins, spawner, HUD\n" +
                    "- Baked NavMesh, saved to Assets/Scenes\n\nContinue?",
                    "Build", "Cancel"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            BuildDemo();
        }

        [MenuItem("AldenFall/Refresh Meshy Assets + Rebuild Demo", priority = 1)]
        public static void RefreshMeshyAssetsAndRebuildDemoFromMenu()
        {
            ImportMeshyAndBuildDemoFromMenu();
        }

        [MenuItem("AldenFall/Import Meshy + Rebuild Emberfall Demo", priority = 2)]
        public static void ImportMeshyAndBuildDemoFromMenu()
        {
            if (!Application.isBatchMode)
            {
                if (!EditorUtility.DisplayDialog("Import Meshy + Rebuild Emberfall Demo",
                    "This refreshes the AssetDatabase, imports Meshy character controllers/prefabs, " +
                    "then rebuilds EmberfallDemo so the latest generated mobs, props, dungeon, and spawns are used.\n\n" +
                    "Continue?",
                    "Import + Rebuild", "Cancel"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
            }

            ImportMeshyAndBuildDemoHeadless();
        }

        /// <summary>
        /// Batch-mode entry after tools/meshy_pipeline.py:
        /// import new FBX/GLB assets, rebuild generated visual prefabs/controllers,
        /// then rebuild and save the playable Emberfall scene.
        /// </summary>
        public static void ImportMeshyAndBuildDemoHeadless()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MeshyCharacterImporter.ImportAll();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildDemo();
        }

        /// <summary>Batch-mode entry: builds, bakes, saves, and quits cleanly.</summary>
        public static void BuildDemoHeadless()
        {
            BuildDemo();
        }

        private static void BuildDemo()
        {
            float startTime = Time.realtimeSinceStartup;
            rng = new System.Random(1387); // deterministic layout

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolder("Assets/Scenes");
            EnsureFolder(MaterialDir);
            materials.Clear();
            litShader = null;

            Debug.Log("Emberfall 1/7: environment lighting...");
            SetupLightingAndAtmosphere();

            Debug.Log("Emberfall 2/7: arena geometry...");
            var arenaRoot = BuildArena();

            Debug.Log("Emberfall 3/7: baking NavMesh...");
            BakeNavMesh(arenaRoot);

            Debug.Log("Emberfall 4/8: network player prefab + camera...");
            var playerPrefab = CreateNetworkPlayerPrefab();
            CreateCamera(null);

            Debug.Log("Emberfall 5/7: goblins + spawner...");
            CreateEnemies();

            Debug.Log("Emberfall 6/8: item icons + visual library...");
            BuildItemVisuals();

            Debug.Log("Emberfall 7/8: HUD...");
            HUDBuilder.Build();

            Debug.Log("Emberfall 8/8: network manager + post-processing volume...");
            CreateNetworking(playerPrefab);
            CreatePostProcessing();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log($"=== EMBERFALL DEMO BUILT in {Time.realtimeSinceStartup - startTime:F1}s → {ScenePath} ===");
        }

        // ==================== LIGHTING & ATMOSPHERE ====================

        private static void SetupLightingAndAtmosphere()
        {
            // Late-afternoon sun: still warm and directional, but HIGH and BRIGHT enough
            // that camps, forest floors, and the ember approach stay readable — the old
            // 24°-dusk setup left everything outside the city near-black in playtests.
            // Bright clear daylight (v9 environment milestone) - the references are sunlit, and
            // the dusk setup left the expanded world dark + unreadable.
            // ONE sun only (real-world lighting; the old second "sky fill" directional read as a
            // second sun with its own highlight on the water). Shade fill comes from ambient.
            var sunObj = new GameObject("Sun");
            var sun = sunObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sunObj.transform.rotation = Quaternion.Euler(48f, -128f, 0f);   // high mid-morning sun
            RenderSettings.sun = sun;

            // Clear procedural sky.
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                sky.SetFloat("_SunSize", 0.04f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness", 1.0f);
                sky.SetColor("_SkyTint", new Color(0.52f, 0.63f, 0.82f));
                sky.SetColor("_GroundColor", new Color(0.42f, 0.42f, 0.39f));
                sky.SetFloat("_Exposure", 1.3f);
                AssetDatabase.CreateAsset(sky, $"{MaterialDir}/EmberfallSkybox.mat");
                RenderSettings.skybox = sky;
            }

            // Warm, neutral daylight ambient (the old strong blue ambient tinted dark ground a
            // cold wet blue - part of the "everything looks flooded" read).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.68f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.53f, 0.47f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.24f);

            // Very light haze, pushed far out and warmed toward neutral so only the distant
            // mountains soften - no blue wash across the whole valley (the "flood" look).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.79f, 0.76f);
            RenderSettings.fogStartDistance = 700f;
            RenderSettings.fogEndDistance = 2400f;
        }

        private static void CreatePostProcessing()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, $"{MaterialDir}/EmberfallPost.asset");

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.9f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.6f);
            AssetDatabase.AddObjectToAsset(bloom, profile);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.27f);
            vignette.smoothness.Override(0.45f);
            AssetDatabase.AddObjectToAsset(vignette, profile);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.1f);
            color.saturation.Override(10f);
            color.contrast.Override(12f);
            AssetDatabase.AddObjectToAsset(color, profile);

            var tonemap = profile.Add<Tonemapping>(true);
            tonemap.mode.Override(TonemappingMode.ACES);
            AssetDatabase.AddObjectToAsset(tonemap, profile);

            var volumeObj = new GameObject("Global Post Volume");
            var volume = volumeObj.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.profile = profile;

            // Post-processing must be enabled on the camera for URP
            var cam = Camera.main;
            if (cam != null)
            {
                var camData = cam.GetUniversalAdditionalCameraData();
                if (camData != null) camData.renderPostProcessing = true;
            }
        }

        // ==================== ARENA GEOMETRY ====================

        private static GameObject BuildArena()
        {
            var root = new GameObject("Emberfall_Arena");
            int groundLayer = LayerMask.NameToLayer("Ground");

            // V2: the fresh radial Emberfall world (city centre + forest ring + lake/camps +
            // ember approach + volcano dungeon). Implemented in ArenaBuilderEmberfall.cs; it
            // reuses every helper below and wires the Phase-1 world systems.
            BuildEmberfallWorld(root.transform, groundLayer);

            return root;
        }

        /// <summary>
        /// World-bounds safety: a catch floor under everything + perimeter rails enclosing
        /// roughly x[-260,260], z[-450,270] (city + forest ring + ember approach + volcano).
        /// Nothing can fall into the void; there are NO invisible walls inside the play space.
        /// </summary>
        private static void BuildGlobalSafety(Transform root, int groundLayer)
        {
            var safety = Child(root, "GlobalSafetyColliders");

            // (1) CONTINUOUS solid underfill just below the tile tops so there is NEVER a
            //     void seam to fall through (Jon fell through the city↔ring gap, img 16).
            //     It is carved only around the NW lake+feeder basins, which sink below it.
            var underMat = Mat("WorldUnderfill", new Color(0.17f, 0.14f, 0.11f), smoothness: 0.03f);
            void Fill(string n, float x0, float x1, float z0, float z1)
            {
                var c = Cube(n, new Vector3((x0 + x1) / 2f, -0.55f, (z0 + z1) / 2f),
                    new Vector3(x1 - x0, 0.7f, z1 - z0), underMat, safety);
                c.isStatic = true;
                if (groundLayer >= 0) c.layer = groundLayer;
            }
            // Legacy flat-tile safety: underfill boxes + cube mountain wall. In terrain mode
            // the terrain is a solid collider with real mountain slopes at the edges, so this
            // cube geography is retired (it would otherwise wall the middle of the expanded world).
            if (!UseUnityTerrain)
            {
                // NW water-exclusion box (lake + feeder): x[-206,-100], z[100,262].
                Fill("Underfill_S", -262f, 262f, -460f, 100f);   // everything south of the water band
                Fill("Underfill_NE", -100f, 262f, 100f, 268f);    // north band, east of the water
                Fill("Underfill_NW", -262f, -206f, 100f, 268f);   // north band, west of the water
                Fill("Underfill_NN", -206f, -100f, 262f, 268f);   // thin strip north of the water
            }

            // (2) Last-resort catch floor far below — a backstop for anything that clips through.
            ColliderBox("WorldCatchFloor", new Vector3(0f, -22f, 0f), new Vector3(1000f, 1f, 1200f), safety, groundLayer);

            // (3) Natural enclosing geography.
            if (!UseUnityTerrain)
                BuildMountainRing(root, groundLayer);
        }

        /// <summary>
        /// A continuous ring of natural mountains/tall hills enclosing the whole play area:
        /// a solid inner containment wall hugging the forest disc (leaving the south corridor
        /// mouth open toward the volcano and river notches for waterfalls), solid flank walls
        /// down the ember-approach sides, then layered mid ridgelines + a hazy distant range
        /// so the horizon is never gray-floor-to-sky emptiness. Bases overlap the ground.
        /// </summary>
        private static void BuildMountainRing(Transform root, int groundLayer)
        {
            var container = Child(root, "MountainRing");
            var rock = Mat("RingRock", new Color(0.30f, 0.28f, 0.29f), smoothness: 0.04f);
            var rockDark = Mat("RingRockDark", new Color(0.19f, 0.18f, 0.21f), smoothness: 0.04f);
            var rockFar = Mat("RingRockFar", new Color(0.34f, 0.32f, 0.41f), smoothness: 0.02f);
            var snow = Mat("RingSnow", new Color(0.85f, 0.88f, 0.95f), smoothness: 0.15f);
            var rr = new System.Random(20260711);
            float R() => (float)rr.NextDouble();

            void Massif(string n, float x, float z, float w, float h, Material m, bool collide, bool cap)
            {
                var c = Cube(n, new Vector3(x, h / 2f - 2.5f, z), new Vector3(w, h, w), m, container);
                c.transform.rotation = Quaternion.Euler(0f, R() * 60f, 0f);
                c.isStatic = true;
                if (groundLayer >= 0) c.layer = groundLayer;
                if (!collide) Object.DestroyImmediate(c.GetComponent<BoxCollider>());
                if (cap)
                {
                    var s = Cube(n + "_Snow", new Vector3(x, h - 4.5f, z),
                        new Vector3(w * 0.52f, w * 0.34f, w * 0.52f), snow, container);
                    s.transform.rotation = c.transform.rotation;
                    s.isStatic = true;
                    Object.DestroyImmediate(s.GetComponent<BoxCollider>());
                }
            }

            Vector2 lake = new Vector2(LakeCenter.x, LakeCenter.z);

            // (1) Solid containment wall hugging the forest-disc edge (~252m). Dense +
            //     overlapping so there is no gap. Open the south corridor mouth (to the
            //     volcano) and any river channel (those notches become waterfalls). The
            //     lake neighbourhood is walled separately by its own tall banks.
            for (int i = 0; i < 150; i++)
            {
                float a = i / 150f * Mathf.PI * 2f;
                float dirX = Mathf.Cos(a), dirZ = Mathf.Sin(a);
                float rad = 250f + R() * 14f;
                float x = dirX * rad, z = dirZ * rad;
                if (z < -28f && Mathf.Abs(x) < 96f) continue;              // south corridor mouth
                if (Vector2.Distance(new Vector2(x, z), lake) < 80f) continue; // lake walled separately
                if (InFeederRiver(x, z)) continue;                        // river notch (waterfall)
                float h = 24f + R() * 32f;
                Massif($"Ring_{i}", x, z, 30f + R() * 16f, h, i % 2 == 0 ? rock : rockDark, true, h > 44f);
            }

            // (1b) Lake enclosure — tall rock banks wrapping the lake's outer (NW) arc, with
            //      a notch at the far corner where the lake overflows into a waterfall.
            for (int i = 0; i < 26; i++)
            {
                float a = Mathf.Lerp(35f, 235f, i / 25f) * Mathf.Deg2Rad; // outward-facing half
                float x = lake.x + Mathf.Cos(a) * (LakeHalf + 15f);
                float z = lake.y + Mathf.Sin(a) * (LakeHalf + 15f);
                // Leave a notch at the NW corner (~135°) for the outflow waterfall.
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 135f)) < 12f) continue;
                float h = 26f + R() * 20f;
                Massif($"LakeWall_{i}", x, z, 24f + R() * 12f, h, i % 2 == 0 ? rock : rockDark, true, h > 40f);
            }

            // (2) Ember-approach flank walls — the corridor sides get a solid mountain edge.
            for (float z = -60f; z >= -334f; z -= 15f)
            {
                float h = 22f + R() * 20f;
                Massif($"RingApW_{z}", -(ApproachHalfW + 22f) - R() * 8f, z, 26f + R() * 12f, h, rockDark, true, false);
                Massif($"RingApE_{z}", (ApproachHalfW + 22f) + R() * 8f, z, 26f + R() * 12f, h, rockDark, true, false);
            }
            // South backdrop behind the volcano so the horizon is walled there too.
            for (float x = -160f; x <= 160f; x += 22f)
                Massif($"RingS_{x}", x, -474f - R() * 22f, 34f + R() * 16f, 34f + R() * 30f, rockDark, false, false);

            // (3) Layered mid ridgeline (visual only) — a taller second range behind the wall.
            for (int i = 0; i < 76; i++)
            {
                float a = i / 76f * Mathf.PI * 2f + 0.04f;
                float rad = 306f + R() * 34f;
                float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad - 92f;
                float h = 46f + R() * 48f;
                Massif($"Ridge_{i}", x, z, 40f + R() * 26f, h, i % 3 == 0 ? rockDark : rock, false, h > 76f);
            }

            // (4) Distant silhouette range (visual only) — hazy peaks read through the fog.
            for (int i = 0; i < 60; i++)
            {
                float a = i / 60f * Mathf.PI * 2f + 0.09f;
                float rad = 392f + R() * 58f;
                float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad - 92f;
                float h = 72f + R() * 66f;
                Massif($"Far_{i}", x, z, 54f + R() * 36f, h, rockFar, false, h > 110f);
            }
        }

        // ==================== MOUNTAIN VISTA (south, beyond the forest) ====================
        // Past the forest's south wall gap the winding river spills toward a
        // dramatic mountain massing on the horizon, so the forest visibly leads
        // somewhere bigger. A rough ramp climbs a foothill to a small summit
        // ledge with a torch-lit reward chest (GuildWars-vista style).
        private static void BuildMountain(Transform root, int groundLayer)
        {
            var container = Child(root, "Mountain");
            var rock = Mat("MountainRock", new Color(0.34f, 0.31f, 0.30f), smoothness: 0.04f);
            var rockD = Mat("MountainRockDark", new Color(0.24f, 0.22f, 0.23f), smoothness: 0.04f);
            var snow = Mat("MountainSnow", new Color(0.9f, 0.92f, 0.97f), smoothness: 0.2f);

            void Rock(string n, Vector3 pos, Vector3 size, float yaw, Material m)
            {
                var c = Cube(n, pos, size, m, container);
                c.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                c.isStatic = true;
                if (groundLayer >= 0) c.layer = groundLayer;
            }

            // Dry apron linking the forest's south gap to the mountain foot.
            Rock("Mtn_Apron", new Vector3(38f, -0.25f, -276f), new Vector3(74f, 0.5f, 34f), 0f, rock);

            // Foothills blend the flat apron up into the peaks.
            Rock("Mtn_Foot0", new Vector3(20f, 7f, -298f), new Vector3(70f, 16f, 26f), 6f, rock);
            Rock("Mtn_Foot1", new Vector3(-6f, 6f, -300f), new Vector3(44f, 14f, 24f), 14f, rockD);
            Rock("Mtn_Foot2", new Vector3(52f, 6f, -299f), new Vector3(40f, 14f, 24f), -12f, rockD);

            // Three peaks of rising height — the horizon backdrop over the 12m wall.
            Rock("Mtn_PeakE", new Vector3(58f, 19f, -320f), new Vector3(40f, 40f, 40f), 32f, rock);
            Rock("Mtn_PeakC", new Vector3(28f, 24f, -326f), new Vector3(50f, 50f, 50f), 18f, rock);
            Rock("Mtn_PeakW", new Vector3(-12f, 31f, -338f), new Vector3(56f, 64f, 56f), -16f, rockD);

            // Snow caps.
            Rock("Mtn_SnowE", new Vector3(58f, 40f, -320f), new Vector3(22f, 8f, 22f), 32f, snow);
            Rock("Mtn_SnowC", new Vector3(28f, 50f, -326f), new Vector3(28f, 9f, 28f), 18f, snow);
            Rock("Mtn_SnowW", new Vector3(-12f, 64f, -338f), new Vector3(32f, 10f, 32f), -16f, snow);

            // ---- Bonus climb: a walkable ramp (well under the slope limit) rises
            // from the apron to a summit ledge with a torch-lit reward chest. ----
            const float rampRise = 4.5f, rampRun = 14f;
            var ramp = Cube("Mtn_Ramp", new Vector3(20f, rampRise / 2f, -292f),
                new Vector3(6.5f, 0.8f, Mathf.Sqrt(rampRise * rampRise + rampRun * rampRun)), rock, container);
            ramp.transform.rotation = Quaternion.LookRotation(new Vector3(0f, rampRise, -rampRun).normalized);
            ramp.isStatic = true;
            if (groundLayer >= 0) ramp.layer = groundLayer;

            Rock("Mtn_Ledge", new Vector3(20f, rampRise, -300f), new Vector3(11f, 1f, 10f), 0f, rock);

            CreateTorch(container, new Vector3(16f, rampRise + 0.5f, -301f), 0.8f);
            CreateLootChest(container, new Vector3(22f, rampRise + 0.5f, -301f), 200f,
                new (string id, float chance)[] { ("knight_sword", 1f), ("iron_chestplate", 0.75f) }, groundLayer);
        }

        // ==================== FOREST ZONE (v7 starting area) ====================
        // South of the courtyard: a deep, dark pine forest. The player wakes up
        // here; goblin camps hide in the clearings; a winding dirt path leads
        // north to the courtyard's south gate.

        /// <summary>The path's x position at a given z — one gentle S-curve.</summary>
        private static float ForestPathX(float z) => Mathf.Sin((z + 43.5f) / 24f) * 12f;

        // ---- Forest river (winding, deep, swimmable). Shared by BuildForest
        // (carves the floor + keeps trees/props out of the channel) and
        // BuildFishingAndCooking (builds the bed, water volume + shore steps).
        // The river winds down the EAST side of the forest and out the south
        // wall gap toward the mountain, so the fishing party's WEST bank stays dry.
        private const float RiverSourceZ = -116f;   // north source, up among the trees
        private const float RiverMouthZ = -260f;    // south mouth, at the wall gap
        private const float RiverHalfW = 7f;        // half the open-water width (~14m)
        private const float RiverBankHalf = 12.5f;  // half-width incl. the sloped shore steps
        private const float RiverBedY = -2.0f;      // bed top: depth = WaterSurfaceY-RiverBedY = 2.1m

        /// <summary>Winding centerline X of the forest river at a given z.</summary>
        private static float RiverCenterX(float z) => 33f + 6f * Mathf.Sin((z + 116f) / 30f);

        /// <summary>True if (x,z) sits inside the river's carved footprint (water + shore).</summary>
        private static bool IsOverRiver(float x, float z)
            => z <= RiverSourceZ + 4f && z >= RiverMouthZ - 4f
               && Mathf.Abs(x - RiverCenterX(z)) < RiverBankHalf;

        private static void BuildForest(Transform root, int groundLayer)
        {
            var container = Child(root, "Forest");

            // Darker, mossier grass than the field
            var grassTints = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                grassTints[i] = Mat($"ForestGrass{i}",
                    new Color(0.16f + i * 0.015f, 0.30f + i * 0.02f, 0.15f + i * 0.012f), smoothness: 0.05f);
            }
            var dirtMat = Mat("ForestDirt", new Color(0.38f, 0.30f, 0.22f), smoothness: 0.05f);

            // Keep-clear zones: goblin camps (match monsters.json!) + spawn clearing
            // + Grukmar's warcamp (-45,-165) so the dense forest doesn't bury the
            // rescue objective — the player must be able to SEE the camp to find him.
            var clearings = new Vector2[]
            {
                new Vector2(-18f, -78f), new Vector2(22f, -112f),
                new Vector2(-26f, -146f), new Vector2(28f, -174f),
                new Vector2(0f, -115f),   // player spawn clearing
                new Vector2(-45f, -165f), // Grukmar's warcamp (rescue quest boss)
            };

            for (float x = -ForestHalfW; x < ForestHalfW; x += TileSize)
            {
                for (float z = ForestEndZ; z < ForestStartZ; z += TileSize)
                {
                    float cx = x + TileSize / 2, cz = z + TileSize / 2;
                    if (IsOverRiver(cx, cz)) continue; // leave the river channel open (carved)
                    bool isPath = Mathf.Abs(cx - ForestPathX(cz)) < 4.5f;
                    var mat = isPath ? dirtMat : grassTints[rng.Next(grassTints.Length)];
                    var tile = Cube($"Forest_{x}_{z}",
                        new Vector3(x + TileSize / 2, -0.25f, z + TileSize / 2),
                        new Vector3(TileSize, 0.5f, TileSize), mat, container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            BuildGrassBladeField(container, "ForestGrassBlades",
                -ForestHalfW, ForestHalfW, ForestEndZ, ForestStartZ, 7,
                new Color(0.22f, 0.42f, 0.18f),
                (x, z) =>
                {
                    if (Mathf.Abs(x - ForestPathX(z)) < 5.3f) return false;
                    if (IsOverRiver(x, z)) return false;
                    foreach (var c in clearings)
                        if (Vector2.Distance(new Vector2(x, z), c) < 13f) return false;
                    return true;
                });

            // Perimeter cliffs seal the forest on W/E/S
            var cliffMat = Mat("Cliff", new Color(0.30f, 0.27f, 0.26f), smoothness: 0.05f);
            void Cliff(string name, Vector3 pos, Vector3 size)
            {
                var cliff = Cube(name, pos, size, cliffMat, container);
                cliff.isStatic = true;
                if (groundLayer >= 0) cliff.layer = groundLayer;
            }
            float forestLen = ForestStartZ - ForestEndZ;
            Cliff("ForestCliff_W", new Vector3(-ForestHalfW - 2f, 6f, ForestEndZ + forestLen / 2), new Vector3(4f, 12f, forestLen + 8f));
            Cliff("ForestCliff_E", new Vector3(ForestHalfW + 2f, 6f, ForestEndZ + forestLen / 2), new Vector3(4f, 12f, forestLen + 8f));
            // South wall split around an 18m river-mouth gap (x30..48) so the
            // winding river can spill out toward the mountain vista beyond.
            Cliff("ForestCliff_SW", new Vector3(-22f, 6f, ForestEndZ - 2f), new Vector3(104f, 12f, 4f));
            Cliff("ForestCliff_SE", new Vector3(61f, 6f, ForestEndZ - 2f), new Vector3(26f, 12f, 4f));
            ColliderBox("ForestSafety_NW", new Vector3(-38f, 3f, ForestStartZ + 1.5f), new Vector3(64f, 6f, 3f), container, groundLayer);
            ColliderBox("ForestSafety_NE", new Vector3(38f, 3f, ForestStartZ + 1.5f), new Vector3(64f, 6f, 3f), container, groundLayer);
            ColliderBox("ForestCatchFloor", new Vector3(0f, -8f, ForestEndZ + forestLen / 2), new Vector3(ForestHalfW * 2f, 1f, forestLen), container, groundLayer);

            // DENSE trees — a real forest, not a lawn with decorations. Rotate
            // through the Meshy tree kit (OakTree/BirchTree/DeadTree/PineTree),
            // each auto-scaled by PlaceMeshyProp, and fall back to a cheap cube
            // pine when an fbx hasn't downloaded yet.
            var trunkMat = Mat("TreeTrunk", new Color(0.30f, 0.21f, 0.13f), smoothness: 0.05f);
            var pineMat = Mat("PineLeaf", new Color(0.12f, 0.28f, 0.14f), smoothness: 0.08f);
            string[] treeKit = { "OakTree", "BirchTree", "DeadTree", "PineTree" };
            int planted = 0;
            for (int attempt = 0; attempt < 950 && planted < 265; attempt++)
            {
                float x = Mathf.Lerp(-ForestHalfW + 5f, ForestHalfW - 5f, (float)rng.NextDouble());
                float z = Mathf.Lerp(ForestEndZ + 6f, ForestStartZ - 6f, (float)rng.NextDouble());
                if (Mathf.Abs(x - ForestPathX(z)) < 6f) continue;    // winding path stays clear
                if (IsOverRiver(x, z)) continue;                     // no trees in the river
                bool blocked = false;
                foreach (var c in clearings)
                    if (Vector2.Distance(new Vector2(x, z), c) < 12f) { blocked = true; break; }
                if (blocked) continue;

                planted++;
                float h = 5.5f + (float)rng.NextDouble() * 5f;
                var meshyTree = PlaceMeshyProp(container, treeKit[planted % treeKit.Length],
                    new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f, h, groundLayer);
                if (meshyTree != null) continue;

                // Cube-pine fallback while the tree fbx is still downloading
                var trunk = Cube($"Pine{planted}_Trunk", new Vector3(x, h * 0.28f, z),
                    new Vector3(0.8f, h * 0.56f, 0.8f), trunkMat, container);
                trunk.isStatic = true;
                if (groundLayer >= 0) trunk.layer = groundLayer;

                // Two stacked canopies → cheap pine silhouette
                var canopyLow = Cube($"Pine{planted}_LeafLow", new Vector3(x, h * 0.62f, z),
                    new Vector3(h * 0.5f, h * 0.38f, h * 0.5f), pineMat, container);
                canopyLow.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 45f, 0);
                canopyLow.isStatic = true;
                var canopyHigh = Cube($"Pine{planted}_LeafHigh", new Vector3(x, h * 0.92f, z),
                    new Vector3(h * 0.32f, h * 0.3f, h * 0.32f), pineMat, container);
                canopyHigh.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 45f, 0);
                canopyHigh.isStatic = true;
            }

            // Forest-floor dressing — bushes, mossy boulders, old stumps make the
            // ground read as a lived-in forest rather than bare tiles.
            string[] floorKit = { "ForestBush", "MossBoulder", "TreeStump" };
            float[] floorHeights = { 1.1f, 1.4f, 0.9f };
            var floorRockMat = Mat("ForestFloorRock", new Color(0.3f, 0.32f, 0.26f), smoothness: 0.08f);
            int scattered = 0;
            for (int attempt = 0; attempt < 500 && scattered < 106; attempt++)
            {
                float x = Mathf.Lerp(-ForestHalfW + 4f, ForestHalfW - 4f, (float)rng.NextDouble());
                float z = Mathf.Lerp(ForestEndZ + 5f, ForestStartZ - 5f, (float)rng.NextDouble());
                if (Mathf.Abs(x - ForestPathX(z)) < 5f) continue;
                if (IsOverRiver(x, z)) continue;                     // keep the channel clear
                bool blocked = false;
                foreach (var c in clearings)
                    if (Vector2.Distance(new Vector2(x, z), c) < 9f) { blocked = true; break; }
                if (blocked) continue;

                scattered++;
                int k = scattered % floorKit.Length;
                var prop = PlaceMeshyProp(container, floorKit[k], new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, floorHeights[k], groundLayer);
                if (prop == null)
                {
                    // graceful fallback: a small mossy rock if the fbx isn't present
                    var rock = Cube($"ForestRock{scattered}", new Vector3(x, 0.25f, z),
                        new Vector3(0.9f, 0.5f, 0.8f), floorRockMat, container);
                    rock.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    rock.isStatic = true;
                    if (groundLayer >= 0) rock.layer = groundLayer;
                }
            }

            // Glowing mushroom clusters — waypoints in the gloom
            var shroomMat = Mat("GlowShroom", new Color(0.45f, 0.8f, 0.9f), smoothness: 0.3f,
                emission: new Color(0.25f, 0.6f, 0.75f) * 2f);
            for (int i = 0; i < 8; i++)
            {
                float z = Mathf.Lerp(ForestEndZ + 15f, ForestStartZ - 15f, i / 7f);
                float x = ForestPathX(z) + (i % 2 == 0 ? 6.5f : -6.5f);
                for (int m = 0; m < 3; m++)
                {
                    var cap = Sphere($"Shroom{i}_{m}",
                        new Vector3(x + m * 0.5f - 0.5f, 0.22f + m * 0.06f, z + (m % 2) * 0.6f),
                        Vector3.one * (0.35f + m * 0.12f), shroomMat, container);
                    Object.DestroyImmediate(cap.GetComponent<SphereCollider>());
                }
            }

            // Torches marking the spawn clearing + the way north to the gate
            CreateTorch(container, new Vector3(ForestPathX(-115f) - 3f, 0, -115f), 0.9f);
            CreateTorch(container, new Vector3(ForestPathX(-115f) + 3f, 0, -115f), 0.9f);
            CreateTorch(container, new Vector3(ForestPathX(-60f) + 3f, 0, -60f), 0.9f);

            // Camp dressing for the two nearest goblin camps
            BuildBanditCampDressing(container, new Vector3(-18f, 0, -78f));
            BuildBanditCampDressing(container, new Vector3(28f, 0, -174f));

            // Aldric — the tutorial quest-giver, standing in the spawn clearing
            BuildQuestGiver(container, new Vector3(4f, 0f, -110f), groundLayer);
        }

        /// <summary>
        /// A plain (non-networked) quest-giving NPC — an Interactable like the
        /// fishing spot, so it needs no NetworkIdentity. A simple robed capsule
        /// stands in until a Meshy character earns the slot.
        /// </summary>
        private static void BuildQuestGiver(Transform parent, Vector3 pos, int groundLayer)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");

            var npcObj = new GameObject("Aldric");
            npcObj.transform.SetParent(parent);
            npcObj.transform.position = pos;
            npcObj.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the spawn point (south)
            if (interactableLayer >= 0) npcObj.layer = interactableLayer;

            var giver = npcObj.AddComponent<World.QuestGiverNPC>();
            var giverSO = new SerializedObject(giver);
            giverSO.FindProperty("npcId").stringValue = "aldric";
            giverSO.FindProperty("questId").stringValue = "q_intro";
            giverSO.FindProperty("displayName").stringValue = "Aldric";
            giverSO.FindProperty("prompt").stringValue = "Talk to Aldric";
            giverSO.FindProperty("radius").floatValue = 3.5f;
            giverSO.ApplyModifiedProperties();

            // Real Meshy model when it's been imported; otherwise the robed
            // capsule + "sage" head placeholder stands in.
            var aldricModel = SceneSetup.InstantiateGeneratedVisual("Aldric", npcObj.transform);
            if (aldricModel != null)
            {
                aldricModel.name = "Body";
                aldricModel.transform.localPosition = Vector3.zero;   // feet at ground
                aldricModel.transform.localRotation = Quaternion.identity;
                if (groundLayer >= 0) SetLayerRecursive(aldricModel, groundLayer);
            }
            else
            {
                var robeMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.32f, 0.26f, 0.42f), "AldricRobeMaterial");
                var skinMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.82f, 0.66f, 0.52f), "AldricSkinMaterial");

                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(npcObj.transform);
                body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                body.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
                body.GetComponent<MeshRenderer>().sharedMaterial = robeMat;
                if (groundLayer >= 0) body.layer = groundLayer;

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(npcObj.transform);
                head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
                head.transform.localScale = Vector3.one * 0.55f;
                Object.DestroyImmediate(head.GetComponent<SphereCollider>());
                head.GetComponent<MeshRenderer>().sharedMaterial = skinMat;
                if (groundLayer >= 0) head.layer = groundLayer;
            }

            // Warm marker light so he's easy to spot in the gloomy clearing.
            var lightObj = new GameObject("QuestLight");
            lightObj.transform.SetParent(npcObj.transform);
            lightObj.transform.localPosition = Vector3.up * 2.6f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.95f, 0.8f, 0.5f);
            light.intensity = 1.6f;
            light.range = 5f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// The rescue-quest giver in the town courtyard — a grieving mother whose
        /// daughter Grukmar has carried off. Interactable like Aldric (no
        /// NetworkIdentity). npcId "townsperson" matches q_rescue's TalkToNpc
        /// return step, so handing the quest in here also frees the reunion.
        /// </summary>
        private static void BuildRescueGiver(Transform parent, int groundLayer)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            Vector3 pos = new Vector3(10f, 0f, -6f);   // town courtyard, clear of the pond

            var npcObj = new GameObject("WorriedMother");
            npcObj.transform.SetParent(parent);
            npcObj.transform.position = pos;
            npcObj.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            if (interactableLayer >= 0) npcObj.layer = interactableLayer;

            var giver = npcObj.AddComponent<World.QuestGiverNPC>();
            var giverSO = new SerializedObject(giver);
            giverSO.FindProperty("npcId").stringValue = "townsperson";
            giverSO.FindProperty("questId").stringValue = "q_rescue";
            giverSO.FindProperty("displayName").stringValue = "Worried Mother";
            giverSO.FindProperty("prompt").stringValue = "Talk to the Worried Mother";
            giverSO.FindProperty("radius").floatValue = 3.5f;
            giverSO.ApplyModifiedProperties();

            var model = SceneSetup.InstantiateGeneratedVisual("HeroFemale", npcObj.transform);
            if (model != null)
            {
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                if (groundLayer >= 0) SetLayerRecursive(model, groundLayer);
            }
            else
            {
                var shawlMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.45f, 0.2f, 0.28f), "MotherShawlMaterial");
                var skinMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.85f, 0.68f, 0.55f), "MotherSkinMaterial");

                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(npcObj.transform);
                body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                body.transform.localScale = new Vector3(0.72f, 0.95f, 0.72f);
                body.GetComponent<MeshRenderer>().sharedMaterial = shawlMat;
                if (groundLayer >= 0) body.layer = groundLayer;

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(npcObj.transform);
                head.transform.localPosition = new Vector3(0f, 1.9f, 0f);
                head.transform.localScale = Vector3.one * 0.5f;
                Object.DestroyImmediate(head.GetComponent<SphereCollider>());
                head.GetComponent<MeshRenderer>().sharedMaterial = skinMat;
                if (groundLayer >= 0) head.layer = groundLayer;
            }

            var lightObj = new GameObject("QuestLight");
            lightObj.transform.SetParent(npcObj.transform);
            lightObj.transform.localPosition = Vector3.up * 2.5f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.7f, 0.75f, 0.95f);
            light.intensity = 1.5f;
            light.range = 5f;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// Grukmar's orc warcamp on the west forest edge (~-45, -165), off the
        /// main path: totems, a war-fire, a spike-ring, and the caged Daughter
        /// who is freed (RescueCaptive) when the warlord falls. Grukmar himself +
        /// his guards are spawned from monsters.json by CreateEnemies.
        /// </summary>
        private static void BuildOrcWarcamp(Transform root, int groundLayer)
        {
            var container = Child(root, "OrcWarcamp");
            Vector3 center = new Vector3(-45f, 0f, -165f);

            // Reuse the field orc dressing (totem + bone pile), then add a war-fire.
            BuildOrcCampDressing(container, center + new Vector3(4f, 0f, 3f));
            BuildOrcCampDressing(container, center + new Vector3(-6f, 0f, -5f));

            var fireStoneMat = Mat("WarcampStone", new Color(0.3f, 0.28f, 0.28f), smoothness: 0.1f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var stone = Cube($"WarFireStone{i}",
                    center + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * 1.3f,
                    new Vector3(0.55f, 0.4f, 0.55f), fireStoneMat, container);
                stone.isStatic = true;
            }
            var fireObj = new GameObject("WarFire");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = center + Vector3.up * 0.9f;
            var fireLight = fireObj.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.5f, 0.18f);
            fireLight.range = 16f;
            fireLight.shadows = LightShadows.None;
            fireObj.AddComponent<World.TorchFlicker>().Configure(3.0f, 0.9f, 7f);

            // Spike ring (sharpened stakes) around the camp perimeter.
            var spikeMat = Mat("WarSpike", new Color(0.28f, 0.2f, 0.13f), smoothness: 0.05f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 sp = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11f;
                var spike = Cylinder($"WarSpike{i}", sp + Vector3.up * 1.1f,
                    new Vector3(0.22f, 1.1f, 0.22f), spikeMat, container);
                spike.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 22f - 11f,
                    (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 22f - 11f);
                spike.isStatic = true;
                if (groundLayer >= 0) spike.layer = groundLayer;
            }

            // Lean-to tents (Meshy prop if present, else cube fallback).
            foreach (var (offset, yaw) in new[] { (new Vector3(6f, 0, 4f), 40f), (new Vector3(-5f, 0, 6f), -30f) })
            {
                Vector3 tentPos = center + offset;
                var tentModel = PlaceMeshyProp(container, "OrcTent", tentPos, yaw, 3f, groundLayer);
                if (tentModel == null)
                {
                    var tentMat = Mat("WarTentCloth", new Color(0.3f, 0.24f, 0.16f), smoothness: 0.05f);
                    var tent = Cube("OrcTent", tentPos + Vector3.up * 1.2f, new Vector3(3.2f, 2.4f, 2.8f), tentMat, container);
                    tent.transform.rotation = Quaternion.Euler(0, yaw, 42f);
                    tent.isStatic = true;
                }
            }

            BuildCagedDaughter(container, center + new Vector3(-3f, 0f, -3f), groundLayer);
        }

        /// <summary>The kidnapped girl in a crude cage; RescueCaptive frees her on Grukmar's death.</summary>
        private static void BuildCagedDaughter(Transform parent, Vector3 pos, int groundLayer)
        {
            var daughterObj = new GameObject("Daughter");
            daughterObj.transform.SetParent(parent);
            daughterObj.transform.position = pos;
            daughterObj.transform.rotation = Quaternion.Euler(0f, 60f, 0f);

            var model = SceneSetup.InstantiateGeneratedVisual("Daughter", daughterObj.transform);
            if (model != null)
            {
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                if (groundLayer >= 0) SetLayerRecursive(model, groundLayer);
            }
            else
            {
                var dressMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.6f, 0.45f, 0.5f), "DaughterDressMaterial");
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(daughterObj.transform);
                body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                body.transform.localScale = new Vector3(0.55f, 0.6f, 0.55f);   // small: a cowering child
                Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
                body.GetComponent<MeshRenderer>().sharedMaterial = dressMat;
                if (groundLayer >= 0) body.layer = groundLayer;
            }

            // Locked cage: a dense ring of bars with NO gap. She is genuinely trapped
            // until Grukmar dies, when RescueCaptive drops the bars (Jon: "she should
            // be locked up until you kill the warlord").
            var cageMat = Mat("Cage", new Color(0.22f, 0.16f, 0.11f), smoothness: 0.05f);
            var bars = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                var post = Cylinder($"CagePost{i}", pos + new Vector3(Mathf.Cos(a), 1.1f, Mathf.Sin(a)) * 1.3f,
                    new Vector3(0.14f, 1.1f, 0.14f), cageMat, parent);
                post.isStatic = true;
                if (groundLayer >= 0) post.layer = groundLayer;
                bars.Add(post);
            }
            // Cross-rails so the cage reads as a cage, not fence posts.
            for (int i = 0; i < 2; i++)
            {
                var rail = Cylinder($"CageRail{i}", pos + Vector3.up * (0.7f + i * 0.9f), new Vector3(1.5f, 0.06f, 1.5f), cageMat, parent);
                rail.transform.rotation = Quaternion.identity;
                rail.isStatic = true;
                Object.DestroyImmediate(rail.GetComponent<CapsuleCollider>());
                if (groundLayer >= 0) rail.layer = groundLayer;
                bars.Add(rail);
            }

            // Fearful red captive glow that RescueCaptive warms to gold on rescue.
            var lightObj = new GameObject("CaptiveLight");
            lightObj.transform.SetParent(daughterObj.transform);
            lightObj.transform.localPosition = Vector3.up * 1.8f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.85f, 0.25f, 0.2f);
            light.intensity = 1.1f;
            light.range = 4.5f;
            light.shadows = LightShadows.None;

            var captive = daughterObj.AddComponent<World.RescueCaptive>();
            captive.Configure("grukmar", light);
            captive.ConfigureCage(bars);
        }

        // ==================== FISHING & COOKING (pond east shore) ====================
        // The pond becomes a skilling spot: fish at the shoreline (needs the
        // starter fishing pole), cook the catch at the campfire a few steps away.

        private static void BuildFishingAndCooking(Transform root, int groundLayer)
        {
            var container = Child(root, "FishingAndCooking");
            int interactableLayer = LayerMask.NameToLayer("Interactable");

            // ---- Forest river: a REAL, swimmable river. A translucent surface
            // (no collider) floats over a deep carved bed; a Water-layer trigger
            // volume is what the swim system actually reads; walk-in shore steps
            // let the player wade DOWN from the bank instead of hitting a wall.
            // It winds down the east side and out the south wall gap toward the
            // mountain (BuildForest carves the matching hole in the floor tiles).
            {
                int waterLayer = LayerMask.NameToLayer("Water");
                var bedMat = Mat("RiverBed", new Color(0.16f, 0.19f, 0.2f), smoothness: 0.25f);
                var shoreMat = Mat("RiverShore", new Color(0.3f, 0.26f, 0.19f), smoothness: 0.05f);
                var riverWaterMat = Mat("RiverWater", new Color(0.16f, 0.35f, 0.45f, 0.62f),
                    smoothness: 0.95f, transparent: true, emission: new Color(0.03f, 0.08f, 0.1f));

                float colTop = WaterSurfaceY;              // 0.1
                float colH = colTop - RiverBedY;           // 2.1 (>= swimEnterDepth 1.3)
                float colCy = (colTop + RiverBedY) / 2f;   // -0.95
                float[] stepTops = { -1.3f, -0.55f };      // inner (deep) -> outer (shallow)
                float[] stepInnerEdge = { RiverHalfW, RiverHalfW + 2.5f };
                const float stepW = 2.5f, stepBottomY = RiverBedY - 0.4f, seg = 6f;

                for (float z = RiverSourceZ; z > RiverMouthZ - 0.1f; z -= seg)
                {
                    float zA = z, zB = Mathf.Max(z - seg, RiverMouthZ);
                    float xA = RiverCenterX(zA), xB = RiverCenterX(zB);
                    Vector3 mid = new Vector3((xA + xB) / 2f, 0f, (zA + zB) / 2f);
                    float dx = xB - xA, dz = zB - zA;
                    float segLen = Mathf.Sqrt(dx * dx + dz * dz) + 1.6f;   // overlap the seams
                    float yawDeg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                    Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
                    float yawRad = yawDeg * Mathf.Deg2Rad;
                    Vector3 rightDir = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

                    // Bed (solid, Ground layer) — supports a swimmer who touches bottom.
                    var bed = Cube("River_Bed", mid + Vector3.up * (RiverBedY - 0.35f),
                        new Vector3(RiverHalfW * 2f + 1f, 0.7f, segLen), bedMat, container);
                    bed.transform.rotation = rot;
                    bed.isStatic = true;
                    if (groundLayer >= 0) bed.layer = groundLayer;

                    // Water trigger volume (Water layer) — top sits exactly at the
                    // surface; the swim system measures depth below bounds.max.y.
                    var vol = new GameObject("River_WaterVolume");
                    vol.transform.SetParent(container);
                    vol.transform.position = mid + Vector3.up * colCy;
                    vol.transform.rotation = rot;
                    var vbox = vol.AddComponent<BoxCollider>();
                    vbox.isTrigger = true;
                    vbox.size = new Vector3(RiverHalfW * 2f, colH, segLen);
                    if (waterLayer >= 0) vol.layer = waterLayer;

                    // Translucent surface visual (no collider — players/props pass in).
                    var surf = Cube("River_Water", mid + Vector3.up * WaterSurfaceY,
                        new Vector3(RiverHalfW * 2f - 0.4f, 0.06f, segLen), riverWaterMat, container);
                    surf.transform.rotation = rot;
                    Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

                    // Walk-in shore steps on each bank (Ground layer): two ledges
                    // (~0.55 then ~0.75 drop) grade the bank down into the water.
                    for (int side = -1; side <= 1; side += 2)
                        for (int s = 0; s < stepTops.Length; s++)
                        {
                            float top = stepTops[s];
                            float h = top - stepBottomY;
                            float off = stepInnerEdge[s] + stepW / 2f;
                            var stp = Cube("River_Shore",
                                mid + rightDir * (off * side) + Vector3.up * (top - h / 2f),
                                new Vector3(stepW, h, segLen), shoreMat, container);
                            stp.transform.rotation = rot;
                            stp.isStatic = true;
                            if (groundLayer >= 0) stp.layer = groundLayer;
                        }
                }
            }

            // ---- Fishing spot on the dry west bank of the forest river ----
            var spotObj = new GameObject("FishingSpot");
            spotObj.transform.SetParent(container);
            spotObj.transform.position = new Vector3(14f, 0f, -148f);
            if (interactableLayer >= 0) spotObj.layer = interactableLayer;

            var spot = spotObj.AddComponent<World.FishingSpot>();
            var spotSO = new SerializedObject(spot);
            spotSO.FindProperty("prompt").stringValue = "Fish";
            spotSO.FindProperty("radius").floatValue = 4f;
            spotSO.FindProperty("bobberAnchor").vector3Value =
                new Vector3(RiverCenterX(-148f), WaterSurfaceY + 0.05f, -148f); // out over open water
            spotSO.ApplyModifiedProperties();

            // Little wooden marker so the spot reads at a glance: post + sign
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "SignPost";
            post.transform.SetParent(spotObj.transform);
            post.transform.localPosition = new Vector3(0.6f, 0.55f, -0.8f);
            post.transform.localScale = new Vector3(0.09f, 0.55f, 0.09f);
            post.GetComponent<MeshRenderer>().sharedMaterial =
                SceneSetup.GetOrCreateMaterialAsset(new Color(0.4f, 0.28f, 0.16f), "FishSignPostMaterial");
            if (groundLayer >= 0) post.layer = groundLayer;

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "SignBoard";
            board.transform.SetParent(spotObj.transform);
            board.transform.localPosition = new Vector3(0.6f, 1.05f, -0.8f);
            board.transform.localRotation = Quaternion.Euler(0f, -70f, 0f);
            board.transform.localScale = new Vector3(0.62f, 0.34f, 0.05f);
            Object.DestroyImmediate(board.GetComponent<BoxCollider>());
            board.GetComponent<MeshRenderer>().sharedMaterial =
                SceneSetup.GetOrCreateMaterialAsset(new Color(0.5f, 0.36f, 0.2f), "FishSignBoardMaterial");

            // ---- Campfire a few steps back from the river bank ----
            var fireObj = new GameObject("Campfire");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = new Vector3(9f, 0f, -145f);
            if (interactableLayer >= 0) fireObj.layer = interactableLayer;

            var fire = fireObj.AddComponent<World.Campfire>();
            var fireSO = new SerializedObject(fire);
            fireSO.FindProperty("prompt").stringValue = "Cook";
            fireSO.FindProperty("radius").floatValue = 3.5f;
            fireSO.ApplyModifiedProperties();

            // Meshy campfire model when it exists, stone-ring fallback otherwise
            var fireModel = PlaceMeshyProp(fireObj.transform, "Campfire",
                fireObj.transform.position, rng.Next(0, 360), 0.9f, groundLayer);
            if (fireModel == null)
            {
                var stoneMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.35f, 0.33f, 0.31f), "CampfireStoneMaterial");
                var logMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.32f, 0.2f, 0.1f), "CampfireLogMaterial");
                for (int i = 0; i < 6; i++)
                {
                    float angle = i * Mathf.PI * 2f / 6f;
                    var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    stone.name = $"Stone{i}";
                    stone.transform.SetParent(fireObj.transform);
                    stone.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.55f, 0.1f, Mathf.Sin(angle) * 0.55f);
                    stone.transform.localScale = Vector3.one * 0.28f;
                    Object.DestroyImmediate(stone.GetComponent<SphereCollider>());
                    stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
                }
                for (int i = 0; i < 3; i++)
                {
                    var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    log.name = $"Log{i}";
                    log.transform.SetParent(fireObj.transform);
                    log.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                    log.transform.localRotation = Quaternion.Euler(70f, i * 60f, 0f);
                    log.transform.localScale = new Vector3(0.1f, 0.35f, 0.1f);
                    Object.DestroyImmediate(log.GetComponent<CapsuleCollider>());
                    log.GetComponent<MeshRenderer>().sharedMaterial = logMat;
                }
            }

            // Warm light + flicker sells "fire" even before a flame VFX exists
            var lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(fireObj.transform);
            lightObj.transform.localPosition = Vector3.up * 0.8f;
            var fireLight = lightObj.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.55f, 0.25f);
            fireLight.intensity = 2.4f;
            fireLight.range = 7f;
            lightObj.AddComponent<World.TorchFlicker>();
        }

        // ==================== FIELD ZONE ====================
        // Beyond the north gate: open grass field with two mob camps flanking a
        // dirt path that leads to the cave mouth in the northern cliffs.

        private const float FieldStartZ = 43.5f;   // just past the courtyard wall
        private const float FieldEndZ = 180f;      // v7: much deeper field
        private const float FieldHalfW = 64f;      // v7: much wider field

        // v7 forest (south of the courtyard) — the new starting zone
        private const float ForestStartZ = -43.5f; // just past the south wall
        private const float ForestEndZ = -260f;     // v8: deeper forest leading to the mountain
        private const float ForestHalfW = 70f;

        private static void BuildField(Transform root, int groundLayer)
        {
            var container = Child(root, "Field");

            // Grass tiles with tint variety; a dirt path runs up the middle
            var grassTints = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                grassTints[i] = Mat($"FieldGrass{i}",
                    new Color(0.24f + i * 0.02f, 0.38f + i * 0.025f, 0.20f + i * 0.015f), smoothness: 0.05f);
            }
            var dirtMat = Mat("FieldDirt", new Color(0.42f, 0.33f, 0.24f), smoothness: 0.05f);

            // Camp keep-clear zones (must match monsters.json spawns)
            var fieldCamps = new Vector2[]
            {
                new Vector2(-26, 75), new Vector2(34, 64), new Vector2(-46, 120), new Vector2(44, 135), // bandits
                new Vector2(26, 100), new Vector2(-42, 150), new Vector2(50, 88),                        // orcs
            };

            for (float x = -FieldHalfW; x < FieldHalfW; x += TileSize)
            {
                for (float z = FieldStartZ; z < FieldEndZ; z += TileSize)
                {
                    bool isPath = Mathf.Abs(x + TileSize / 2) < 5f;
                    var mat = isPath ? dirtMat : grassTints[rng.Next(grassTints.Length)];
                    var tile = Cube($"Field_{x}_{z}",
                        new Vector3(x + TileSize / 2, -0.25f, z + TileSize / 2),
                        new Vector3(TileSize, 0.5f, TileSize), mat, container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            BuildGrassBladeField(container, "FieldGrassBlades",
                -FieldHalfW, FieldHalfW, FieldStartZ, FieldEndZ, 5,
                new Color(0.34f, 0.58f, 0.22f),
                (x, z) =>
                {
                    if (Mathf.Abs(x) < 6f) return false;
                    foreach (var c in fieldCamps)
                        if (Vector2.Distance(new Vector2(x, z), c) < 14f) return false;
                    return true;
                });

            // Perimeter cliffs (leave the cave mouth open at north center)
            var cliffMat = Mat("Cliff", new Color(0.30f, 0.27f, 0.26f), smoothness: 0.05f);
            void Cliff(string name, Vector3 pos, Vector3 size)
            {
                var cliff = Cube(name, pos, size, cliffMat, container);
                cliff.isStatic = true;
                if (groundLayer >= 0) cliff.layer = groundLayer;
            }
            float fieldLen = FieldEndZ - FieldStartZ;
            Cliff("Cliff_W", new Vector3(-FieldHalfW - 2f, 6f, FieldStartZ + fieldLen / 2), new Vector3(4f, 12f, fieldLen + 8f));
            Cliff("Cliff_E", new Vector3(FieldHalfW + 2f, 6f, FieldStartZ + fieldLen / 2), new Vector3(4f, 12f, fieldLen + 8f));
            // North cliff split by the cave mouth (8 wide)
            float northSeg = (FieldHalfW * 2f - 8f) / 2f;
            Cliff("Cliff_N_West", new Vector3(-(8f + northSeg) / 2f, 6f, FieldEndZ + 2f), new Vector3(northSeg, 12f, 4f));
            Cliff("Cliff_N_East", new Vector3((8f + northSeg) / 2f, 6f, FieldEndZ + 2f), new Vector3(northSeg, 12f, 4f));
            // Lintel rock over the cave mouth
            Cliff("CaveMouth_Top", new Vector3(0, 8.5f, FieldEndZ + 2f), new Vector3(10f, 7f, 4f));

            // Scattered trees + rocks
            var trunkMat = Mat("TreeTrunk", new Color(0.30f, 0.21f, 0.13f), smoothness: 0.05f);
            var leafMat = Mat("TreeLeaf", new Color(0.18f, 0.34f, 0.16f), smoothness: 0.08f);
            var rockMat = Mat("FieldRock", new Color(0.38f, 0.36f, 0.35f), smoothness: 0.1f);
            for (int i = 0; i < 30; i++)
            {
                float x = Mathf.Lerp(-FieldHalfW + 6f, FieldHalfW - 6f, (float)rng.NextDouble());
                float z = Mathf.Lerp(FieldStartZ + 6f, FieldEndZ - 8f, (float)rng.NextDouble());
                if (Mathf.Abs(x) < 8f) continue;                      // keep the path clear
                bool nearCamp = false;
                foreach (var c in fieldCamps)
                    if (Vector2.Distance(new Vector2(x, z), c) < 14f) { nearCamp = true; break; }
                if (nearCamp) continue;

                if (rng.Next(2) == 0)
                {
                    float h = 4.5f + (float)rng.NextDouble() * 3f;
                    var trunk = Cube($"Tree{i}_Trunk", new Vector3(x, h * 0.3f, z), new Vector3(0.7f, h * 0.6f, 0.7f), trunkMat, container);
                    trunk.isStatic = true;
                    if (groundLayer >= 0) trunk.layer = groundLayer;
                    var leaf = Cube($"Tree{i}_Leaf", new Vector3(x, h * 0.75f, z), new Vector3(h * 0.55f, h * 0.5f, h * 0.55f), leafMat, container);
                    leaf.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 45f, 0);
                    leaf.isStatic = true;
                }
                else
                {
                    float s = 1.2f + (float)rng.NextDouble() * 2f;
                    var rock = Cube($"Rock{i}", new Vector3(x, s * 0.3f, z), new Vector3(s, s * 0.7f, s * 0.85f), rockMat, container);
                    rock.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 15f, (float)rng.NextDouble() * 360f, 0);
                    rock.isStatic = true;
                    if (groundLayer >= 0) rock.layer = groundLayer;
                }
            }

            // Camp dressing: fire ring + tents (bandits), totem + bone pile (orcs)
            BuildBanditCampDressing(container, new Vector3(-26, 0, 75));
            BuildBanditCampDressing(container, new Vector3(34, 0, 64));
            BuildBanditCampDressing(container, new Vector3(-46, 0, 120));
            BuildBanditCampDressing(container, new Vector3(44, 0, 135));
            BuildOrcCampDressing(container, new Vector3(26, 0, 100));
            BuildOrcCampDressing(container, new Vector3(-42, 0, 150));
            BuildOrcCampDressing(container, new Vector3(50, 0, 88));
        }

        private static void BuildBanditCampDressing(Transform container, Vector3 center)
        {
            var stoneMat = Mat("CampStone", new Color(0.32f, 0.3f, 0.3f), smoothness: 0.1f);
            var tentMat = Mat("TentCloth", new Color(0.4f, 0.31f, 0.21f), smoothness: 0.05f);
            var dirtMat = Mat("CampDirt", new Color(0.30f, 0.24f, 0.17f), smoothness: 0.02f);
            var woodMat = Mat("CampWood", new Color(0.28f, 0.2f, 0.12f), smoothness: 0.04f);

            // Worn TRAMPLED-DIRT ground so the camp reads as lived-in, not tents on a lawn.
            var ground = Cube("CampGround", center + Vector3.up * 0.04f, new Vector3(17f, 0.08f, 15f), dirtMat, container);
            ground.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            ground.isStatic = true;
            Object.DestroyImmediate(ground.GetComponent<BoxCollider>());

            // Fire ring + a real campfire mesh + flickering light.
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                var stone = Cube($"FireStone{i}", center + new Vector3(Mathf.Cos(angle), 0.15f, Mathf.Sin(angle)) * 1.1f,
                    new Vector3(0.5f, 0.35f, 0.5f), stoneMat, container);
                stone.isStatic = true;
            }
            PlaceMeshyProp(container, "Campfire", center, (float)rng.NextDouble() * 360f, 1.2f, -1, PropCollision.None);
            var fire = new GameObject("CampFire");
            fire.transform.SetParent(container);
            fire.transform.position = center + Vector3.up * 0.8f;
            var light = fire.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 12f;
            light.shadows = LightShadows.None;
            fire.AddComponent<World.TorchFlicker>().Configure(2.6f, 0.8f, 7f);

            // Three lean-to tents around the fire.
            foreach (var (offset, yaw) in new[] { (new Vector3(-4.5f, 0, 2f), 40f), (new Vector3(3.5f, 0, 3.5f), -30f), (new Vector3(-1f, 0, -5f), 165f) })
            {
                var tent = Cube("Tent", center + offset + Vector3.up * 1.1f, new Vector3(3f, 2.2f, 2.6f), tentMat, container);
                tent.transform.rotation = Quaternion.Euler(0, yaw, 42f);
                tent.isStatic = true;
            }

            // Crates + barrels + a couple of stacked-log seats scattered around (real meshes).
            var props = new[] { "WoodCrate", "MarketBarrel", "WoodCrate", "MarketBarrel" };
            for (int i = 0; i < props.Length; i++)
            {
                float a = i * 1.7f + 0.6f, rr = 4.5f + (float)rng.NextDouble() * 2.5f;
                Vector3 p = center + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                if (PlaceMeshyProp(container, props[i], p, (float)rng.NextDouble() * 360f, 1.1f + (float)rng.NextDouble() * 0.4f, -1, PropCollision.None) == null)
                {
                    var box = Cube($"CampCrate{i}", p + Vector3.up * 0.6f, Vector3.one * 1.1f, woodMat, container);
                    box.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0); box.isStatic = true;
                }
            }
            // A leaning spear/spit by the fire.
            var spear = Cube("CampSpear", center + new Vector3(1.6f, 1.4f, -1.2f), new Vector3(0.12f, 3.4f, 0.12f), woodMat, container);
            spear.transform.rotation = Quaternion.Euler(24f, 30f, 8f); spear.isStatic = true;
        }

        private static void BuildOrcCampDressing(Transform container, Vector3 center)
        {
            var totemMat = Mat("Totem", new Color(0.32f, 0.22f, 0.14f), smoothness: 0.05f);
            var boneMat = Mat("Bone", new Color(0.8f, 0.78f, 0.7f), smoothness: 0.2f);

            var totem = Cube("OrcTotem", center + Vector3.up * 2.2f, new Vector3(1f, 4.4f, 1f), totemMat, container);
            totem.transform.rotation = Quaternion.Euler(0, 25f, 0);
            totem.isStatic = true;
            var totemTop = Cube("OrcTotemTop", center + Vector3.up * 4.6f, new Vector3(1.8f, 0.9f, 1.4f), totemMat, container);
            totemTop.transform.rotation = Quaternion.Euler(0, 25f, 0);
            totemTop.isStatic = true;

            for (int i = 0; i < 5; i++)
            {
                var bone = Cube($"Bone{i}",
                    center + new Vector3((float)rng.NextDouble() * 6f - 3f, 0.12f, (float)rng.NextDouble() * 6f - 3f),
                    new Vector3(0.9f, 0.18f, 0.22f), boneMat, container);
                bone.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                bone.isStatic = true;
            }
        }

        // ==================== DUNGEON ZONE ====================
        // A room-and-corridor dungeon behind the field's north cliff. The path
        // branches, side rooms reward exploration, and the boss sits past a
        // final antechamber instead of directly inside one cave box.

        private static void BuildCave(Transform root, int groundLayer)
        {
            var container = Child(root, "Dungeon");
            var floorMat = Mat("DungeonFloor", new Color(0.16f, 0.15f, 0.17f), smoothness: 0.15f);
            var wallMat = Mat("DungeonWall", new Color(0.13f, 0.12f, 0.14f), smoothness: 0.08f);
            var trimMat = Mat("DungeonTrim", new Color(0.24f, 0.21f, 0.19f), smoothness: 0.12f);
            var emberMat = Mat("EmberCrystal", new Color(1f, 0.4f, 0.12f), smoothness: 0.4f,
                emission: new Color(1f, 0.35f, 0.1f) * 3f);
            var rubbleMat = Mat("DungeonRubble", new Color(0.22f, 0.21f, 0.22f), smoothness: 0.08f);

            void Slab(string name, Vector3 pos, Vector3 size, Material mat, bool walkable)
            {
                var obj = Cube(name, pos, size, mat, container);
                obj.isStatic = true;
                if (groundLayer >= 0) obj.layer = groundLayer;
                if (!walkable)
                {
                    var mod = obj.AddComponent<NavMeshModifier>();
                    mod.overrideArea = true;
                    mod.area = 1;
                }
            }

            void Room(string name, Vector3 center, Vector2 size)
            {
                Slab($"{name}_Floor", center + Vector3.down * 0.25f, new Vector3(size.x, 0.5f, size.y), floorMat, true);
                Slab($"{name}_Ceiling", center + Vector3.up * 8f, new Vector3(size.x + 2f, 0.8f, size.y + 2f), wallMat, false);
            }

            void Corridor(string name, Vector3 center, Vector2 size)
            {
                Slab($"{name}_Floor", center + Vector3.down * 0.25f, new Vector3(size.x, 0.5f, size.y), floorMat, true);
                Slab($"{name}_Ceiling", center + Vector3.up * 7.5f, new Vector3(size.x + 1.5f, 0.8f, size.y + 1.5f), wallMat, false);
            }

            void Wall(string name, Vector3 center, Vector3 size)
            {
                Slab(name, center, size, wallMat, false);
            }

            void DoorFrame(string name, Vector3 pos, float yaw)
            {
                var lintel = Cube($"{name}_Lintel", pos + Vector3.up * 4.4f, new Vector3(7f, 1f, 0.8f), trimMat, container);
                lintel.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                lintel.isStatic = true;
                var left = Cube($"{name}_Left", pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-3.3f, 2.1f, 0f),
                    new Vector3(0.7f, 4.2f, 0.8f), trimMat, container);
                left.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                left.isStatic = true;
                var right = Cube($"{name}_Right", pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(3.3f, 2.1f, 0f),
                    new Vector3(0.7f, 4.2f, 0.8f), trimMat, container);
                right.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                right.isStatic = true;
            }

            void EmberLight(string name, Vector3 pos, float range = 13f)
            {
                var crystal = Cube($"{name}_Crystal", pos + Vector3.up * 0.8f, new Vector3(0.7f, 1.6f, 0.7f), emberMat, container);
                crystal.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f - 10f);
                crystal.isStatic = true;
                var lightObj = new GameObject($"{name}_Light");
                lightObj.transform.SetParent(container);
                lightObj.transform.position = pos + Vector3.up * 1.7f;
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.42f, 0.15f);
                light.range = range;
                light.shadows = LightShadows.None;
                lightObj.AddComponent<World.TorchFlicker>().Configure(2.2f, 0.5f, 5f);
            }

            // Walkable spaces
            Room("EntryHall", new Vector3(0f, 0f, 194f), new Vector2(18f, 24f));
            Corridor("MainCorridorA", new Vector3(0f, 0f, 218f), new Vector2(8f, 28f));
            Room("ForkRoom", new Vector3(0f, 0f, 240f), new Vector2(24f, 22f));
            Corridor("LeftBranch", new Vector3(-17f, 0f, 253f), new Vector2(26f, 7f));
            Room("Armory", new Vector3(-34f, 0f, 253f), new Vector2(24f, 18f));
            Corridor("RightBranch", new Vector3(17f, 0f, 257f), new Vector2(26f, 7f));
            Room("Reliquary", new Vector3(36f, 0f, 257f), new Vector2(24f, 18f));
            Corridor("MainCorridorB", new Vector3(0f, 0f, 270f), new Vector2(8f, 30f));
            Room("AmbushRoom", new Vector3(0f, 0f, 288f), new Vector2(26f, 20f));
            Corridor("FinalHall", new Vector3(0f, 0f, 306f), new Vector2(9f, 26f));
            Room("BossChamber", new Vector3(0f, 0f, 326f), new Vector2(38f, 30f));

            // Outer/partition walls. Doors are left open at corridor joins.
            Wall("Dungeon_WestOuter", new Vector3(-48f, 4f, 260f), new Vector3(2f, 8f, 150f));
            Wall("Dungeon_EastOuter", new Vector3(48f, 4f, 260f), new Vector3(2f, 8f, 150f));
            Wall("Dungeon_NorthOuter", new Vector3(0f, 4f, 342f), new Vector3(98f, 8f, 2f));
            Wall("Dungeon_SouthWestOuter", new Vector3(-27f, 4f, 181f), new Vector3(42f, 8f, 2f));
            Wall("Dungeon_SouthEastOuter", new Vector3(27f, 4f, 181f), new Vector3(42f, 8f, 2f));
            Wall("Armory_NorthWall", new Vector3(-34f, 4f, 263f), new Vector3(24f, 8f, 2f));
            Wall("Armory_SouthWall", new Vector3(-34f, 4f, 243f), new Vector3(24f, 8f, 2f));
            Wall("Reliquary_NorthWall", new Vector3(36f, 4f, 267f), new Vector3(24f, 8f, 2f));
            Wall("Reliquary_SouthWall", new Vector3(36f, 4f, 247f), new Vector3(24f, 8f, 2f));
            Wall("Boss_LeftPinch", new Vector3(-15f, 4f, 310f), new Vector3(18f, 8f, 2f));
            Wall("Boss_RightPinch", new Vector3(15f, 4f, 310f), new Vector3(18f, 8f, 2f));
            ColliderBox("DungeonSafetyCatchFloor", new Vector3(0f, -6f, 262f), new Vector3(104f, 1f, 170f), container, groundLayer);
            ColliderBox("DungeonSafety_SouthDoorRailL", new Vector3(-10f, 3f, 180f), new Vector3(12f, 6f, 3f), container, groundLayer);
            ColliderBox("DungeonSafety_SouthDoorRailR", new Vector3(10f, 3f, 180f), new Vector3(12f, 6f, 3f), container, groundLayer);

            DoorFrame("EntryDoor", new Vector3(0f, 0f, 181.2f), 0f);
            DoorFrame("ForkDoor", new Vector3(0f, 0f, 229f), 0f);
            DoorFrame("BossDoor", new Vector3(0f, 0f, 310f), 0f);
            DoorFrame("ArmoryDoor", new Vector3(-20.5f, 0f, 253f), 90f);
            DoorFrame("ReliquaryDoor", new Vector3(21.5f, 0f, 257f), 90f);

            // ---- Sealable doors: a solid Ground-layer slab fills each opening,
            // blocking passage + LoS + navmesh until the guarded room is cleared,
            // then SealableDoor hides it. The ENTRY door is left open (it's the
            // way in — nothing to clear behind it). Doors gate on the mobs in the
            // room the player has just reached / must clear before passing.
            var sealMat = Mat("DoorSeal", new Color(0.17f, 0.13f, 0.11f), smoothness: 0.15f,
                emission: new Color(0.14f, 0.04f, 0.02f));
            void SealDoor(string name, Vector3 pos, float yaw, Vector3 guardCenter, Vector3 guardSize)
            {
                var slab = Cube($"{name}_Seal", pos + Vector3.up * 2.5f,
                    new Vector3(6.6f, 5f, 0.6f), sealMat, container);
                slab.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                slab.isStatic = true;
                if (groundLayer >= 0) slab.layer = groundLayer;
                var mod = slab.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = 1;   // Not Walkable — carves the doorway out of the bake
                slab.AddComponent<World.SealableDoor>().Configure(guardCenter, guardSize);
            }

            // ForkDoor: gated by the entry gauntlet (field bandits + orc brutes in the entry hall).
            SealDoor("ForkDoor", new Vector3(0f, 0f, 229f), 0f,
                new Vector3(0f, 4f, 205f), new Vector3(22f, 14f, 52f));
            // Armory + Reliquary side doors: gated by the fork room's acolytes.
            SealDoor("ArmoryDoor", new Vector3(-20.5f, 0f, 253f), 90f,
                new Vector3(0f, 4f, 240f), new Vector3(28f, 14f, 26f));
            SealDoor("ReliquaryDoor", new Vector3(21.5f, 0f, 257f), 90f,
                new Vector3(0f, 4f, 240f), new Vector3(28f, 14f, 26f));
            // BossDoor: gated by the ambush room + final hall — no mage-cheesing the boss.
            SealDoor("BossDoor", new Vector3(0f, 0f, 310f), 0f,
                new Vector3(0f, 4f, 289f), new Vector3(30f, 14f, 44f));

            // ---- Line-of-sight baffles: short tall walls just inside a couple of
            // gates, offset to alternate sides so the player must step in and weave
            // (a chicane) rather than snipe the room straight through the doorway.
            Wall("ForkLoS_A", new Vector3(-5f, 3.5f, 233f), new Vector3(8f, 7f, 1.2f));
            Wall("ForkLoS_B", new Vector3(5f, 3.5f, 235.5f), new Vector3(8f, 7f, 1.2f));
            Wall("AmbushLoS_A", new Vector3(-6f, 3.5f, 280f), new Vector3(9f, 7f, 1.2f));
            Wall("AmbushLoS_B", new Vector3(6f, 3.5f, 283f), new Vector3(9f, 7f, 1.2f));

            foreach (var p in new[]
            {
                (new Vector3(-8f, 0f, 240f), 0f), (new Vector3(8f, 0f, 240f), 180f),
                (new Vector3(-13f, 0f, 288f), 0f), (new Vector3(13f, 0f, 288f), 180f),
                (new Vector3(-16f, 0f, 326f), 0f), (new Vector3(16f, 0f, 326f), 180f),
            })
            {
                PlaceMeshyProp(container, "DungeonPillar", p.Item1, p.Item2, 3.8f, groundLayer);
            }

            // Navigation surprises and sight blockers
            for (int i = 0; i < 18; i++)
            {
                Vector3 pos = new Vector3(
                    Mathf.Lerp(-15f, 15f, (float)rng.NextDouble()),
                    0.25f,
                    Mathf.Lerp(190f, 335f, (float)rng.NextDouble()));
                if (Mathf.Abs(pos.x) < 5f && pos.z < 310f) continue;
                float s = 0.8f + (float)rng.NextDouble() * 1.6f;
                var rubble = Cube($"DungeonRubble{i}", pos, new Vector3(s, 0.5f + s * 0.35f, s * 0.8f), rubbleMat, container);
                rubble.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                rubble.isStatic = true;
                if (groundLayer >= 0) rubble.layer = groundLayer;
            }

            foreach (var pos in new[]
            {
                new Vector3(-6f, 0f, 194f), new Vector3(6f, 0f, 218f), new Vector3(-9f, 0f, 240f),
                new Vector3(-38f, 0f, 253f), new Vector3(40f, 0f, 257f), new Vector3(8f, 0f, 288f),
                new Vector3(-12f, 0f, 326f), new Vector3(12f, 0f, 326f)
            })
            {
                EmberLight($"DungeonEmber_{pos.x}_{pos.z}", pos);
            }

            CreateLootChest(container, new Vector3(-40f, 0f, 257f), 30f,
                new (string id, float chance)[] { ("knight_sword", 1f), ("adept_focus", 1f), ("iron_chestplate", 0.75f) }, groundLayer);
            CreateLootChest(container, new Vector3(42f, 0f, 260f), -25f,
                new (string id, float chance)[] { ("knight_sword", 1f), ("iron_chestplate", 1f), ("iron_legs", 0.8f), ("adept_focus", 0.65f) }, groundLayer);
            CreateLootChest(container, new Vector3(-10f, 0f, 333f), 200f,
                new (string id, float chance)[] { ("ember_focus", 1f), ("emberfang", 0.75f), ("knight_sword", 1f) }, groundLayer);
        }

        private static void CreateLootChest(Transform parent, Vector3 pos, float yaw,
            (string id, float chance)[] rewards, int groundLayer)
        {
            var chestObj = new GameObject("LootChest");
            chestObj.transform.SetParent(parent);
            chestObj.transform.position = pos;
            chestObj.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            chestObj.layer = interactableLayer >= 0 ? interactableLayer : 0;

            var chest = chestObj.AddComponent<World.LootChest>();
            var so = new SerializedObject(chest);
            so.FindProperty("prompt").stringValue = "Open Chest";
            so.FindProperty("radius").floatValue = 3f;
            var rewardProp = so.FindProperty("rewards");
            rewardProp.arraySize = rewards.Length;
            for (int i = 0; i < rewards.Length; i++)
            {
                var element = rewardProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemId").stringValue = rewards[i].id;
                element.FindPropertyRelative("chance").floatValue = rewards[i].chance;
            }
            so.ApplyModifiedProperties();

            var model = PlaceMeshyProp(chestObj.transform, "DungeonChest", pos, yaw, 1.1f, groundLayer)
                     ?? PlaceMeshyProp(chestObj.transform, "TreasureChest", pos, yaw, 1.1f, groundLayer);
            if (model == null)
            {
                var box = Cube("ChestFallback", pos + Vector3.up * 0.45f, new Vector3(1.6f, 0.9f, 1.0f),
                    Mat("ChestWood", new Color(0.32f, 0.2f, 0.1f), smoothness: 0.08f), chestObj.transform);
                box.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                if (groundLayer >= 0) box.layer = groundLayer;
            }
        }

        /// <summary>
        /// Places Meshy-generated props (Assets/Models/Props/*.fbx) when they exist.
        /// Each prop is scaled to a target world size from its rendered bounds and
        /// gets a fitted box collider — Meshy exports arrive at arbitrary scales.
        /// </summary>
        private static void PlaceMeshyProps(Transform root, int groundLayer)
        {
            // The walled courtyard is now the town of Emberfall: buildings ring a
            // central square (the brazier + TownWell), with market dressing in the
            // southeast. Buildings sit against the E/W walls facing inward and stay
            // clear of the N/S gate corridors (x≈0) and the SW pond (x<-14,z<-18).
            // name, position, yaw (front faces inward), target largest-dim in meters.
            var placements = new (string name, Vector3 pos, float yaw, float height)[]
            {
                // --- Buildings around the square ---
                ("Cottage",        new Vector3(-32f, 0, 22f),   90f,  7.0f),  // NW, faces east
                ("BlacksmithShop", new Vector3(-32f, 0, 2f),    90f,  7.5f),  // W,  faces east
                ("TownHouse",      new Vector3(32f, 0, 22f),    270f, 8.0f),  // NE, faces west
                ("Storehouse",     new Vector3(32f, 0, -18f),   270f, 8.0f),  // SE, faces west

                // --- Town centre ---
                ("TownWell",       new Vector3(-8f, 0, 13f),    0f,   3.0f),

                // --- Market dressing (southeast, near the crate cluster) ---
                ("MarketStall",    new Vector3(20f, 0, -14f),   200f, 3.2f),
                ("MarketBarrel",   new Vector3(23f, 0, -11f),   0f,   1.2f),
                ("MarketBarrel",   new Vector3(15.5f, 0, -16f), 40f,  1.2f),
                ("WoodCrate",      new Vector3(18f, 0, -9.5f),  20f,  1.1f),
                ("WoodCrate",      new Vector3(-14f, 0, 18f),   60f,  1.1f),
                ("Signpost",       new Vector3(6f, 0, -26f),    200f, 2.2f),  // toward south gate
                ("Signpost",       new Vector3(-6f, 0, 24f),    10f,  2.2f),  // toward terrace
            };

            Transform container = null;
            foreach (var p in placements)
            {
                if (container == null) container = Child(root, "Town");
                PlaceMeshyProp(container, p.name, p.pos, p.yaw, p.height, groundLayer);
            }

            if (container != null)
                Debug.Log($"Placed {container.childCount} Meshy town props.");
        }

        /// <summary>
        /// Instantiates one Meshy prop FBX with textured material, normalized
        /// size, composed rotation, bottom-center anchoring, and a fitted
        /// collider. Returns null when the model doesn't exist yet.
        /// </summary>
        // Plausible flat color for a Meshy prop whose base-color texture didn't
        // download — keyed by what the name reads as, so a bush is green, a stump
        // is wood-brown, a boulder is stone, a house is timber. Order matters:
        // "dead"/"stump" are checked before the generic "tree" so bare props stay brown.
        private static Color PropFallbackColor(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("foothill"))
                return new Color(0.24f, 0.33f, 0.18f);   // forested green foothill
            if (n.Contains("bush") || n.Contains("grass") || n.Contains("hedge") || n.Contains("shrub") || n.Contains("leaf")
                || n.Contains("fern") || n.Contains("reed") || n.Contains("flower") || n.Contains("frond"))
                return new Color(0.17f, 0.33f, 0.15f);   // foliage green
            if (n.Contains("thatch") || n.Contains("straw"))
                return new Color(0.6f, 0.48f, 0.22f);    // golden thatch
            if (n.Contains("dead") || n.Contains("stump") || n.Contains("log") || n.Contains("trunk") || n.Contains("branch"))
                return new Color(0.31f, 0.22f, 0.14f);   // dry wood brown
            if (n.Contains("tree") || n.Contains("pine") || n.Contains("oak") || n.Contains("birch") || n.Contains("fir"))
                return new Color(0.21f, 0.35f, 0.17f);   // canopy green
            if (n.Contains("mountain") || n.Contains("peak") || n.Contains("ridge") || n.Contains("crag"))
                return new Color(0.37f, 0.34f, 0.30f);   // grey-brown mountain rock
            if (n.Contains("boulder") || n.Contains("rock") || n.Contains("moss") || n.Contains("stone") || n.Contains("well") || n.Contains("cliff"))
                return new Color(0.33f, 0.34f, 0.31f);   // mossy stone
            if (n.Contains("house") || n.Contains("hut") || n.Contains("cottage") || n.Contains("stall") || n.Contains("shop") || n.Contains("home") || n.Contains("hall") || n.Contains("store"))
                return new Color(0.48f, 0.38f, 0.26f);   // timber / plaster
            return new Color(0.4f, 0.38f, 0.34f);        // neutral fallback
        }

        /// <summary>How a Meshy prop's collider is fitted.</summary>
        private enum PropCollision
        {
            FitBox,   // tight box around the whole model (buildings, chests, wells)
            Trunk,    // thin capsule at the base only (trees — walk through canopy, magic passes)
            None,     // no collider at all (small bushes / decor)
        }

        private static GameObject PlaceMeshyProp(Transform parent, string name,
            Vector3 pos, float yaw, float height, int groundLayer,
            PropCollision collision = PropCollision.FitBox)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{name}.fbx");
            if (asset == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            instance.transform.SetParent(parent);

            // Apply the base-color texture extracted from the GLB twin. When the
            // texture never downloaded (fbx present, _baseColor.png absent) the
            // model keeps Unity's default WHITE material and reads as a bald
            // silhouette — so paint a plausible flat color instead. This keeps the
            // world looking right regardless of which Meshy textures actually landed.
            var propMat = MeshyCharacterImporter.BuildTexturedMaterial(
                name, $"Assets/Models/Props/{name}_baseColor");
            if (propMat == null)
                propMat = Mat($"PropFallback_{name}", PropFallbackColor(name), smoothness: 0.06f);
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
                r.sharedMaterial = propMat;

            // Renderer.bounds can be stale immediately after transform edits in
            // batch mode — measure via mesh bounds through the current matrix,
            // which is always up to date.
            Bounds Measure()
            {
                var filters = instance.GetComponentsInChildren<MeshFilter>();
                bool first = true;
                Bounds world = default;
                foreach (var mf in filters)
                {
                    if (mf.sharedMesh == null) continue;
                    var m = mf.transform.localToWorldMatrix;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                            (c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        Vector3 w = m.MultiplyPoint3x4(corner);
                        if (first) { world = new Bounds(w, Vector3.zero); first = false; }
                        else world.Encapsulate(w);
                    }
                }
                return world;
            }

            if (instance.GetComponentsInChildren<MeshFilter>().Length == 0)
            {
                Object.DestroyImmediate(instance);
                return null;
            }

            // Normalize the LARGEST dimension to the target size (props arrive in
            // arbitrary orientations), rotate, then anchor bottom-center at pos.
            // MULTIPLY the existing localScale — model imports carry a unit-
            // conversion scale on the root that must be preserved.
            var bounds = Measure();
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = maxDim > 0.001f ? height / maxDim : 1f;
            instance.transform.localScale = Vector3.Scale(
                instance.transform.localScale, Vector3.one * scale);
            // COMPOSE the yaw with the import rotation (FBX axis conversion
            // lives on the root — replacing it flips props onto their side).
            instance.transform.rotation =
                Quaternion.Euler(0, yaw, 0) * instance.transform.rotation;

            bounds = Measure();
            Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            // Snap ground props (caller passes y~0) onto the terrain surface, so raising the
            // valley into hills never leaves trees/rocks floating or buried.
            var groundT = Terrain.activeTerrain;
            if (groundT != null && Mathf.Abs(pos.y) < 0.01f)
                pos.y = groundT.SampleHeight(new Vector3(pos.x, 0f, pos.z)) + groundT.transform.position.y;
            instance.transform.position += pos - bottomCenter;

            // Collider fit depends on the prop type. Trees get a THIN trunk-only
            // capsule so the player walks through canopy/branches and projectiles
            // pass — you're only blocked touching the trunk. Small bushes get none.
            // Everything else (buildings/chests/wells) keeps the fitted box.
            bounds = Measure();
            Vector3 lossy = instance.transform.lossyScale;
            if (collision == PropCollision.FitBox)
            {
                var box = instance.AddComponent<BoxCollider>();
                box.center = instance.transform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(
                    bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
                    bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
                    bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.0001f));
            }
            else if (collision == PropCollision.Trunk)
            {
                // A slim vertical capsule hugging the base ~45% of the model — the
                // trunk. Canopy above it is walk-through and projectile-transparent.
                var cap = instance.AddComponent<CapsuleCollider>();
                cap.direction = 1; // Y axis
                float worldTrunkH = Mathf.Max(0.6f, bounds.size.y * 0.45f);
                float worldRadius = Mathf.Clamp(
                    Mathf.Min(bounds.size.x, bounds.size.z) * 0.16f, 0.16f, 0.7f);
                Vector3 worldCenter = new Vector3(
                    bounds.center.x, bounds.min.y + worldTrunkH * 0.5f, bounds.center.z);
                cap.center = instance.transform.InverseTransformPoint(worldCenter);
                cap.height = worldTrunkH / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
                cap.radius = worldRadius / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f);
            }
            // PropCollision.None: leave the prop collider-free.

            if (groundLayer >= 0) SetLayerRecursive(instance, groundLayer);
            instance.isStatic = true;

            Debug.Log($"MeshyProp {name}: pos={instance.transform.position} " +
                      $"scale={scale:F3} bounds={Measure().size} renderers=" +
                      instance.GetComponentsInChildren<Renderer>().Length);
            return instance;
        }

        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private static void BuildFloor(Transform root, int groundLayer)
        {
            var container = Child(root, "Floor");

            // Six pre-tinted stone variants → varied floor without hundreds of materials
            var tints = new Material[6];
            for (int i = 0; i < 6; i++)
            {
                float v = 0.36f + i * 0.025f;
                tints[i] = Mat($"FloorStone{i}", new Color(v, v * 0.97f, v * 1.02f), smoothness: 0.12f);
            }

            for (float x = -ArenaHalf; x < ArenaHalf; x += TileSize)
            {
                for (float z = -ArenaHalf; z < ArenaHalf; z += TileSize)
                {
                    // Leave the pond hole open
                    if (PondRect.Contains(new Vector2(x + TileSize / 2, z + TileSize / 2))) continue;

                    var tile = Cube($"Tile_{x}_{z}",
                        new Vector3(x + TileSize / 2, -0.25f, z + TileSize / 2),
                        new Vector3(TileSize, 0.5f, TileSize),
                        tints[rng.Next(tints.Length)], container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            // Bridge the gate thresholds: the arena floor stops at z=±42 and the field/forest
            // floors start at z=±43.5 — a 1.5u gap normally covered by the wall body, but the
            // 10u gate openings cut it out, leaving the hole the player fell through. These
            // walkable tiles overlap both floors to close it at BOTH gates.
            foreach (float gateZ in new[] { 42.75f, -42.75f })
            {
                var bridge = Cube($"GateThreshold_{(gateZ > 0 ? "N" : "S")}",
                    new Vector3(0f, -0.25f, gateZ),
                    new Vector3(12f, 0.5f, 3.5f),
                    tints[0], container);
                bridge.isStatic = true;
                if (groundLayer >= 0) bridge.layer = groundLayer;
            }
        }

        private static void BuildPond(Transform root, int groundLayer)
        {
            var container = Child(root, "Pond");
            int waterLayer = LayerMask.NameToLayer("Water");

            // Keep the basin off the NavMesh: goblins hold the shoreline instead of
            // wading — the pond doubles as a player escape zone.
            var modifier = container.gameObject.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1; // Not Walkable

            Vector3 center = new Vector3(PondRect.center.x, 0, PondRect.center.y);
            float w = PondRect.width, d = PondRect.height;

            var basinMat = Mat("PondBasin", new Color(0.2f, 0.21f, 0.24f), smoothness: 0.3f);
            var rimMat = Mat("PondRim", new Color(0.5f, 0.48f, 0.46f), smoothness: 0.2f);

            // Basin floor + side walls (solid, on Ground layer so swimmers can stand)
            var floor = Cube("Basin_Floor", center + Vector3.down * PondDepth,
                new Vector3(w, 0.5f, d), basinMat, container);
            floor.isStatic = true;
            if (groundLayer >= 0) floor.layer = groundLayer;

            void SideWall(string name, Vector3 pos, Vector3 size)
            {
                var wall = Cube(name, pos, size, basinMat, container);
                wall.isStatic = true;
                if (groundLayer >= 0) wall.layer = groundLayer;
            }
            float wallY = -PondDepth / 2;
            SideWall("Basin_N", center + new Vector3(0, wallY, d / 2 + 0.25f), new Vector3(w + 1f, PondDepth, 0.5f));
            SideWall("Basin_S", center + new Vector3(0, wallY, -d / 2 - 0.25f), new Vector3(w + 1f, PondDepth, 0.5f));
            SideWall("Basin_W", center + new Vector3(-w / 2 - 0.25f, wallY, 0), new Vector3(0.5f, PondDepth, d + 1f));

            // East side: shallow steps out of the water (0.375 risers — walkable)
            int stepCount = 8;
            float stepDepth = 1.1f;
            for (int i = 0; i < stepCount; i++)
            {
                float stepTop = -PondDepth + (i + 1) * (PondDepth / stepCount);
                float height = PondDepth + stepTop; // column from basin floor up to stepTop
                var step = Cube($"Basin_Step{i}",
                    center + new Vector3(w / 2 - (stepCount - i) * stepDepth + stepDepth / 2, stepTop - height / 2, 0),
                    new Vector3(stepDepth, height, d), basinMat, container);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;
            }

            // Stone rim ring at ground level
            SideWall("Rim_N", center + new Vector3(0, 0.15f, d / 2 + 0.6f), new Vector3(w + 2.4f, 0.3f, 1.2f));
            SideWall("Rim_S", center + new Vector3(0, 0.15f, -d / 2 - 0.6f), new Vector3(w + 2.4f, 0.3f, 1.2f));
            SideWall("Rim_W", center + new Vector3(-w / 2 - 0.6f, 0.15f, 0), new Vector3(1.2f, 0.3f, d));
            // Swap rim material
            foreach (Transform t in container)
                if (t.name.StartsWith("Rim_")) t.GetComponent<MeshRenderer>().sharedMaterial = rimMat;

            // Water trigger volume (Water layer) — detection is depth-based
            var waterVolume = new GameObject("WaterVolume");
            waterVolume.transform.SetParent(container);
            waterVolume.transform.position = center + Vector3.up * ((WaterSurfaceY - PondDepth) / 2);
            var box = waterVolume.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(w, PondDepth + WaterSurfaceY, d);
            if (waterLayer >= 0) waterVolume.layer = waterLayer;

            // Translucent surface visual (no collider)
            var waterMat = Mat("PondWater", new Color(0.16f, 0.35f, 0.45f, 0.62f),
                smoothness: 0.95f, transparent: true,
                emission: new Color(0.03f, 0.08f, 0.1f));
            var surface = Cube("WaterSurface", center + Vector3.up * WaterSurfaceY,
                new Vector3(w - 0.2f, 0.06f, d - 0.2f), waterMat, container);
            Object.DestroyImmediate(surface.GetComponent<BoxCollider>());
        }

        private static void BuildTerrace(Transform root, int groundLayer)
        {
            var container = Child(root, "Terrace");
            var stoneMat = Mat("TerraceStone", new Color(0.45f, 0.41f, 0.4f), smoothness: 0.15f);

            Vector3 center = new Vector3(TerraceRect.center.x, 0, TerraceRect.center.y);
            float w = TerraceRect.width, d = TerraceRect.height;

            var slab = Cube("Terrace_Slab", center + Vector3.up * (TerraceHeight / 2),
                new Vector3(w, TerraceHeight, d), stoneMat, container);
            slab.isStatic = true;
            if (groundLayer >= 0) slab.layer = groundLayer;

            // Wide steps on the south face (NavMesh-climbable: 0.3 risers)
            int steps = 8;
            float rise = TerraceHeight / steps;
            float run = 0.9f;
            for (int i = 0; i < steps; i++)
            {
                float h = TerraceHeight - i * rise;
                var step = Cube($"Terrace_Step{i}",
                    center + new Vector3(0, h / 2, -d / 2 - (i + 0.5f) * run),
                    new Vector3(w * 0.6f, h, run), stoneMat, container);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;
            }

            // Low balustrade on the terrace edge
            var railMat = Mat("TerraceRail", new Color(0.38f, 0.35f, 0.34f), smoothness: 0.15f);
            var rail = Cube("Terrace_Rail", center + new Vector3(0, TerraceHeight + 0.4f, d / 2 - 0.25f),
                new Vector3(w, 0.8f, 0.5f), railMat, container);
            rail.isStatic = true;
        }

        private static void BuildWalls(Transform root, int groundLayer)
        {
            var container = Child(root, "Walls");
            var wallMat = Mat("WallStone", new Color(0.34f, 0.31f, 0.32f), smoothness: 0.1f);
            var capMat = Mat("WallCap", new Color(0.28f, 0.26f, 0.27f), smoothness: 0.1f);

            float len = ArenaHalf * 2 + WallThickness * 2;

            void Wall(string name, Vector3 pos, Vector3 size)
            {
                var wall = Cube(name, pos, size, wallMat, container);
                wall.isStatic = true;
                if (groundLayer >= 0) wall.layer = groundLayer;
            }

            // North wall is split by the field gate (10u opening at center)
            const float gateWidth = 10f;
            float segLen = (len - gateWidth) / 2f;
            Wall("Wall_N_West", new Vector3(-(gateWidth + segLen) / 2f, WallHeight / 2, ArenaHalf + WallThickness / 2), new Vector3(segLen, WallHeight, WallThickness));
            Wall("Wall_N_East", new Vector3((gateWidth + segLen) / 2f, WallHeight / 2, ArenaHalf + WallThickness / 2), new Vector3(segLen, WallHeight, WallThickness));

            // Gate pillars + lintel
            var pillarMat = Mat("GatePillar", new Color(0.4f, 0.36f, 0.36f), smoothness: 0.15f);
            foreach (float side in new[] { -1f, 1f })
            {
                var pillar = Cube($"GatePillar_{side}", new Vector3(side * (gateWidth / 2f + 0.8f), 4.5f, ArenaHalf + WallThickness / 2),
                    new Vector3(1.8f, 9f, 2.4f), pillarMat, container);
                pillar.isStatic = true;
                if (groundLayer >= 0) pillar.layer = groundLayer;
            }
            var lintel = Cube("GateLintel", new Vector3(0, 8.4f, ArenaHalf + WallThickness / 2),
                new Vector3(gateWidth + 3.6f, 1.6f, 2.6f), pillarMat, container);
            lintel.isStatic = true;
            if (groundLayer >= 0) lintel.layer = groundLayer;
            CreateTorch(container, new Vector3(-(gateWidth / 2f + 0.8f), 0, ArenaHalf - 1.6f), 1f);
            CreateTorch(container, new Vector3(gateWidth / 2f + 0.8f, 0, ArenaHalf - 1.6f), 1f);

            // South wall split by the forest gate (v7: the player arrives from
            // the forest through here) — mirrors the north gate
            Wall("Wall_S_West", new Vector3(-(gateWidth + segLen) / 2f, WallHeight / 2, -ArenaHalf - WallThickness / 2), new Vector3(segLen, WallHeight, WallThickness));
            Wall("Wall_S_East", new Vector3((gateWidth + segLen) / 2f, WallHeight / 2, -ArenaHalf - WallThickness / 2), new Vector3(segLen, WallHeight, WallThickness));
            foreach (float side in new[] { -1f, 1f })
            {
                var southPillar = Cube($"SouthGatePillar_{side}", new Vector3(side * (gateWidth / 2f + 0.8f), 4.5f, -ArenaHalf - WallThickness / 2),
                    new Vector3(1.8f, 9f, 2.4f), pillarMat, container);
                southPillar.isStatic = true;
                if (groundLayer >= 0) southPillar.layer = groundLayer;
            }
            var southLintel = Cube("SouthGateLintel", new Vector3(0, 8.4f, -ArenaHalf - WallThickness / 2),
                new Vector3(gateWidth + 3.6f, 1.6f, 2.6f), pillarMat, container);
            southLintel.isStatic = true;
            if (groundLayer >= 0) southLintel.layer = groundLayer;
            CreateTorch(container, new Vector3(-(gateWidth / 2f + 0.8f), 0, -ArenaHalf + 1.6f), 1f);
            CreateTorch(container, new Vector3(gateWidth / 2f + 0.8f, 0, -ArenaHalf + 1.6f), 1f);

            Wall("Wall_E", new Vector3(ArenaHalf + WallThickness / 2, WallHeight / 2, 0), new Vector3(WallThickness, WallHeight, len));
            Wall("Wall_W", new Vector3(-ArenaHalf - WallThickness / 2, WallHeight / 2, 0), new Vector3(WallThickness, WallHeight, len));

            // Corner towers with caps + torches
            float t = ArenaHalf + WallThickness / 2;
            Vector3[] corners = { new(t, 0, t), new(-t, 0, t), new(t, 0, -t), new(-t, 0, -t) };
            for (int i = 0; i < corners.Length; i++)
            {
                var tower = Cube($"Tower_{i}", corners[i] + Vector3.up * 5.5f, new Vector3(5f, 11f, 5f), wallMat, container);
                tower.isStatic = true;
                if (groundLayer >= 0) tower.layer = groundLayer;

                var cap = Cube($"TowerCap_{i}", corners[i] + Vector3.up * 11.4f, new Vector3(6f, 0.8f, 6f), capMat, container);
                cap.isStatic = true;

                // Torch glow on the inner corner of each tower
                Vector3 inward = -corners[i].normalized;
                CreateTorch(container, corners[i] + inward * 3.2f + Vector3.up * 0f, 0.9f);
            }
        }

        private static void BuildBrazierPlaza(Transform root)
        {
            var container = Child(root, "Plaza");

            var baseMat = Mat("BrazierBase", new Color(0.3f, 0.28f, 0.29f), smoothness: 0.25f);
            var emberMat = Mat("Ember", new Color(1f, 0.45f, 0.1f), smoothness: 0.4f,
                emission: new Color(1f, 0.38f, 0.08f) * 3.5f);

            // Central brazier: stone drum + glowing fire bowl
            var drum = Cylinder("Brazier_Drum", new Vector3(0, 0.6f, 0), new Vector3(2.6f, 0.6f, 2.6f), baseMat, container);
            drum.isStatic = true;
            var bowl = Cylinder("Brazier_Bowl", new Vector3(0, 1.35f, 0), new Vector3(1.9f, 0.18f, 1.9f), emberMat, container);
            Object.DestroyImmediate(bowl.GetComponent<Collider>());

            var fire = new GameObject("Brazier_Light");
            fire.transform.SetParent(container);
            fire.transform.position = new Vector3(0, 2.2f, 0);
            var fireLight = fire.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.55f, 0.2f);
            fireLight.range = 18f;
            fireLight.shadows = LightShadows.None;
            fire.AddComponent<World.TorchFlicker>().Configure(3.4f, 0.9f, 6f);
        }

        private static void BuildTorchRing(Transform root)
        {
            var container = Child(root, "Torches");

            // Ring of six torches around the plaza
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI * 2f / 6f + Mathf.PI / 6f;
                Vector3 pos = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 13f;
                CreateTorch(container, pos, 1f);
            }

            // Terrace flanks + pond edge
            CreateTorch(container, new Vector3(TerraceRect.xMin + 1.5f, TerraceHeight, TerraceRect.center.y), 1f);
            CreateTorch(container, new Vector3(TerraceRect.xMax - 1.5f, TerraceHeight, TerraceRect.center.y), 1f);
            CreateTorch(container, new Vector3(PondRect.xMax + 1.5f, 0, PondRect.yMin - 1.5f), 0.9f);
            CreateTorch(container, new Vector3(PondRect.xMax + 1.5f, 0, PondRect.yMax + 1.5f), 0.9f);
        }

        private static void CreateTorch(Transform container, Vector3 basePos, float scale)
        {
            var woodMat = Mat("TorchWood", new Color(0.26f, 0.18f, 0.12f), smoothness: 0.05f);
            var flameMat = Mat("TorchFlame", new Color(1f, 0.6f, 0.15f), smoothness: 0.3f,
                emission: new Color(1f, 0.5f, 0.12f) * 4f);

            var post = Cube($"Torch_Post", basePos + Vector3.up * (1.3f * scale),
                new Vector3(0.22f, 2.6f, 0.22f) * scale, woodMat, container);
            post.isStatic = true;

            var flame = Sphere("Torch_Flame", basePos + Vector3.up * (2.75f * scale),
                Vector3.one * 0.4f * scale, flameMat, container);
            Object.DestroyImmediate(flame.GetComponent<Collider>());

            var lightObj = new GameObject("Torch_Light");
            lightObj.transform.SetParent(container);
            lightObj.transform.position = basePos + Vector3.up * (2.9f * scale);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.58f, 0.22f);
            light.range = 11f * scale;
            light.shadows = LightShadows.None;
            lightObj.AddComponent<World.TorchFlicker>().Configure(2.1f * scale, 0.55f, 8f);
        }

        private static void BuildProps(Transform root, int groundLayer)
        {
            var container = Child(root, "Props");
            var crateMat = Mat("Crate", new Color(0.42f, 0.3f, 0.18f), smoothness: 0.08f);
            var barrelMat = Mat("Barrel", new Color(0.35f, 0.24f, 0.15f), smoothness: 0.12f);
            var rubbleMat = Mat("Rubble", new Color(0.4f, 0.38f, 0.37f), smoothness: 0.1f);

            void Crate(Vector3 pos, float size, float yaw)
            {
                var c = Cube("Crate", pos + Vector3.up * size / 2, Vector3.one * size, crateMat, container);
                c.transform.rotation = Quaternion.Euler(0, yaw, 0);
                c.isStatic = true;
                if (groundLayer >= 0) c.layer = groundLayer;
            }

            void Barrel(Vector3 pos)
            {
                var b = Cylinder("Barrel", pos + Vector3.up * 0.65f, new Vector3(0.9f, 0.65f, 0.9f), barrelMat, container);
                b.isStatic = true;
                if (groundLayer >= 0) b.layer = groundLayer;
            }

            // Crate cluster near the east wall
            Crate(new Vector3(30f, 0, 6f), 1.5f, 14f);
            Crate(new Vector3(31.6f, 0, 4.6f), 1.2f, 41f);
            Crate(new Vector3(30.5f, 1.5f, 5.6f), 1.0f, 8f);
            Barrel(new Vector3(28.4f, 0, 7.4f));
            Barrel(new Vector3(33.2f, 0, 7.2f));

            // Market-ish corner near the south wall (east of the pond, which
            // now occupies the southwest corner)
            Crate(new Vector3(-8f, 0, -36f), 1.4f, 66f);
            Barrel(new Vector3(-6f, 0, -37.5f));
            Barrel(new Vector3(-9.6f, 0, -34.4f));

            // Rubble scatter in the southeast
            for (int i = 0; i < 10; i++)
            {
                float rx = 18f + (float)rng.NextDouble() * 14f;
                float rz = -34f + (float)rng.NextDouble() * 10f;
                float s = 0.4f + (float)rng.NextDouble() * 0.9f;
                var rock = Cube("Rubble", new Vector3(rx, s * 0.35f, rz), new Vector3(s, s * 0.7f, s * 0.9f), rubbleMat, container);
                rock.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 14f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 14f);
                rock.isStatic = true;
                if (groundLayer >= 0) rock.layer = groundLayer;
            }
        }

        // ==================== ITEM VISUALS ====================

        /// <summary>
        /// Renders inventory icons for every item with a generated model, then
        /// creates the scene's ItemVisualLibrary mapping ids → prefabs + icons.
        /// </summary>
        private static void BuildItemVisuals()
        {
            EnsureFolder("Assets/UI/Icons");
            UISpriteGenerator.GenerateAll(); // glyph fallbacks must exist before library entries load them

            EnsureFolder("Assets/Prefabs/Generated/Items");

            var entries = new List<Items.ItemVisualLibrary.Entry>();
            foreach (var def in Items.ItemDatabase.All)
            {
                GameObject rawModel = null;
                if (!string.IsNullOrEmpty(def.ModelProp))
                    rawModel = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{def.ModelProp}.fbx");

                // Textured prefab variant (Meshy FBX ships without materials)
                GameObject model = null;
                if (rawModel != null)
                {
                    string itemPrefabPath = $"Assets/Prefabs/Generated/Items/{def.Id}.prefab";
                    AssetDatabase.DeleteAsset(itemPrefabPath);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(rawModel);
                    // Per-ITEM material so tinted twins (fish states, focus tiers)
                    // each get their own look from one shared model.
                    var material = MeshyCharacterImporter.BuildTexturedMaterial(
                        $"item_{def.Id}", $"Assets/Models/Props/{def.ModelProp}_baseColor", def.Tint);
                    if (material != null)
                        foreach (var r in instance.GetComponentsInChildren<Renderer>())
                            r.sharedMaterial = material;
                    model = PrefabUtility.SaveAsPrefabAsset(instance, itemPrefabPath);
                    Object.DestroyImmediate(instance);
                }

                Sprite icon = null;
                if (model != null)
                {
                    string iconPath = $"Assets/UI/Icons/{def.Id}.png";
                    if (AssetDatabase.LoadAssetAtPath<Sprite>(iconPath) == null)
                        RenderIcon(model, iconPath);
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                }
                if (icon == null)
                {
                    // Glyph fallback by slot
                    string glyph = def.Slot == Items.EquipSlot.Weapon ? "icon_sword" : "icon_armor";
                    icon = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/UI/Sprites/{glyph}.png");
                }

                entries.Add(new Items.ItemVisualLibrary.Entry
                {
                    itemId = def.Id,
                    modelPrefab = model,
                    icon = icon,
                });
            }

            // v6: hair meshes for character customization. Not items — but the
            // library is the scene's only id→prefab bridge available at runtime,
            // so CharacterAppearance pulls hair from here too.
            var hairProps = new (string id, string prop)[]
            {
                ("hair_short", "HairShort"),
                ("hair_long", "HairLong"),
                ("hair_ponytail", "HairPonytail"),
            };
            foreach (var (id, prop) in hairProps)
            {
                var rawHair = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{prop}.fbx");
                if (rawHair == null) continue; // not generated yet — styles appear on next rebuild

                string hairPrefabPath = $"Assets/Prefabs/Generated/Items/{id}.prefab";
                AssetDatabase.DeleteAsset(hairPrefabPath);
                var hairInstance = (GameObject)PrefabUtility.InstantiatePrefab(rawHair);
                var hairMat = MeshyCharacterImporter.BuildTexturedMaterial(
                    $"item_{id}", $"Assets/Models/Props/{prop}_baseColor");
                if (hairMat != null)
                    foreach (var r in hairInstance.GetComponentsInChildren<Renderer>())
                        r.sharedMaterial = hairMat;
                var hairModel = PrefabUtility.SaveAsPrefabAsset(hairInstance, hairPrefabPath);
                Object.DestroyImmediate(hairInstance);

                entries.Add(new Items.ItemVisualLibrary.Entry { itemId = id, modelPrefab = hairModel, icon = null });
            }

            var libraryObj = new GameObject("ItemVisualLibrary");
            var library = libraryObj.AddComponent<Items.ItemVisualLibrary>();
            library.EditorSetEntries(entries);
        }

        /// <summary>Renders a model to a transparent 256px icon at a 3/4 angle.</summary>
        private static void RenderIcon(GameObject model, string outputPath)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.position = new Vector3(0, -800f, 0); // far below the arena

            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(instance);
                return;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var camObj = new GameObject("IconCamera");
            var cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.25f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            Vector3 dir = new Vector3(0.75f, 0.5f, -1f).normalized;
            camObj.transform.position = bounds.center + dir * (bounds.size.magnitude + 2f);
            camObj.transform.LookAt(bounds.center);

            // Icons were rendering as near-black silhouettes (no light in the icon scene).
            // Add a key + fill light so each item reads clearly.
            var keyObj = new GameObject("IconKeyLight");
            var key = keyObj.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.color = new Color(1f, 0.98f, 0.92f);
            keyObj.transform.rotation = Quaternion.Euler(35f, -140f, 0f);
            var fillObj = new GameObject("IconFillLight");
            var fill = fillObj.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f;
            fill.color = new Color(0.72f, 0.8f, 0.95f);
            fillObj.transform.rotation = Quaternion.Euler(12f, 40f, 0f);

            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            System.IO.File.WriteAllBytes(outputPath, tex.EncodeToPNG());

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camObj);
            Object.DestroyImmediate(keyObj);
            Object.DestroyImmediate(fillObj);
            Object.DestroyImmediate(instance);

            AssetDatabase.ImportAsset(outputPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outputPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        // ==================== NAVMESH ====================

        private static void BakeNavMesh(GameObject arenaRoot)
        {
            var surface = arenaRoot.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders; // triggers (water) are ignored
            surface.BuildNavMesh();

            // Safety net: re-bake the NavMesh at runtime on the authoritative peer so
            // server-simulated enemies always have a mesh to path on, even if the built
            // (Linux dedicated server) scene shipped without a valid bake. Without a mesh,
            // NavMeshAgent.SetDestination silently no-ops and every enemy freezes.
            arenaRoot.AddComponent<AI.ServerNavMeshBaker>();
        }

        // ==================== CHARACTERS ====================

        private static GameObject CreatePlayer()
        {
            var player = SceneSetup.CreatePlayerObject();
            // V2: spawn in the Emberfall city plaza (SW of the Ember Fountain), facing north.
            player.transform.position = new Vector3(0f, 0.5f, -14f);
            player.transform.rotation = Quaternion.identity;
            return player;
        }

        private static GameObject CreateNetworkPlayerPrefab()
        {
            EnsureFolder("Assets/Prefabs");
            string prefabPath = "Assets/Prefabs/AldenNetworkPlayer.prefab";
            AssetDatabase.DeleteAsset(prefabPath);

            var player = CreatePlayer();
            player.name = "AldenNetworkPlayer";

            var identity = player.GetComponent<Mirror.NetworkIdentity>();
            if (identity == null)
                identity = player.AddComponent<Mirror.NetworkIdentity>();

            if (player.GetComponent<Multiplayer.NetworkPlayerAvatar>() == null)
                player.AddComponent<Multiplayer.NetworkPlayerAvatar>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(player, prefabPath);
            Object.DestroyImmediate(player);
            return prefab;
        }

        private static void CreateCamera(GameObject player)
        {
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();

            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 300f;

            var tpc = camObj.AddComponent<Player.ThirdPersonCamera>();
            var tpcSO = new SerializedObject(tpc);
            var collisionProp = tpcSO.FindProperty("collisionLayers");
            if (collisionProp != null)
            {
                int ground = LayerMask.NameToLayer("Ground");
                collisionProp.intValue = (ground >= 0 ? (1 << ground) : 0) | 1; // Ground + Default
                tpcSO.ApplyModifiedProperties();
            }

            Vector3 focus = player != null ? player.transform.position : new Vector3(0f, 0.5f, -14f);
            // Start behind the expected spawn. NetworkPlayerAvatar retargets this
            // camera as soon as the local player is spawned.
            camObj.transform.position = focus + new Vector3(0, 5f, -8f);
            camObj.transform.LookAt(focus + Vector3.up * 1.5f);
        }

        private static void CreateNetworking(GameObject playerPrefab)
        {
            var networkRoot = new GameObject("Aldenfall_Network");
            var manager = networkRoot.AddComponent<Multiplayer.AldenNetworkManager>();
            var auth = networkRoot.AddComponent<Multiplayer.AldenPasswordAuthenticator>();
            manager.authenticator = auth;
            manager.playerPrefab = playerPrefab;
            manager.autoCreatePlayer = true;
            manager.runInBackground = true;

            var transport = CreatePreferredTransport(networkRoot);
            if (transport != null)
                manager.transport = transport;

            // Spawn in Aldric's lush forest glade just NORTH of the city (see BuildAldricGlade
            // at ~(0,0,90)). The intro cutscene meets Aldric here; the city is a short walk south.
            var starts = new[]
            {
                new Vector3(0f, 0.5f, 84f),
                new Vector3(3f, 0.5f, 82f),
                new Vector3(-3f, 0.5f, 82f),
                new Vector3(0f, 0.5f, 80f)
            };
            for (int i = 0; i < starts.Length; i++)
            {
                var startObj = new GameObject($"NetworkStart_{i + 1}");
                startObj.transform.position = starts[i];
                startObj.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // face north toward Aldric
                startObj.AddComponent<Mirror.NetworkStartPosition>();
            }

            // Clients must be able to spawn everything the server spawns. Registered from the
            // build-time list populated by CreateEnemies (projectiles/loot added the same way).
            manager.spawnPrefabs = new System.Collections.Generic.List<GameObject>(networkSpawnPrefabs);

            networkRoot.AddComponent<Multiplayer.MultiplayerConnectUI>();
        }

        private static Mirror.Transport CreatePreferredTransport(GameObject owner)
        {
            // The Render dedicated server is reached over WebSocket/WSS, so use Mirror's
            // SimpleWebTransport. It lives in its own assembly in current releases, so use
            // reflection to avoid binding this editor script to a transport assembly name that may move.
            var candidates = new[]
            {
                "Mirror.SimpleWeb.SimpleWebTransport, SimpleWebTransport",
                "Mirror.SimpleWeb.SimpleWebTransport, Mirror"
            };

            foreach (string candidate in candidates)
            {
                var type = System.Type.GetType(candidate);
                if (type == null || !typeof(Mirror.Transport).IsAssignableFrom(type)) continue;
                var component = owner.AddComponent(type) as Mirror.Transport;
                if (component != null)
                {
                    ConfigureSimpleWebTransport(component);
                    return component;
                }
            }

            var fallback = System.Type.GetType("Mirror.TelepathyTransport, Telepathy");
            if (fallback != null && typeof(Mirror.Transport).IsAssignableFrom(fallback))
                return owner.AddComponent(fallback) as Mirror.Transport;

            Debug.LogWarning("Mirror transport not found. Install Mirror fully, then rebuild the demo.");
            return null;
        }

        private static void ConfigureSimpleWebTransport(Mirror.Transport transport)
        {
            var type = transport.GetType();
            if (type.Name != "SimpleWebTransport") return;

            var port = type.GetField("port");
            if (port != null) port.SetValue(transport, (ushort)27777);

            var clientUseWss = type.GetField("clientUseWss");
            if (clientUseWss != null) clientUseWss.SetValue(transport, true);

            var settingsField = type.GetField("clientWebsocketSettings");
            var settings = settingsField?.GetValue(transport);
            if (settings == null) return;

            var optionField = settings.GetType().GetField("ClientPortOption");
            if (optionField == null) return;

            var enumType = optionField.FieldType;
            var matchWebpageProtocol = System.Enum.Parse(enumType, "MatchWebpageProtocol");
            optionField.SetValue(settings, matchWebpageProtocol);
            settingsField.SetValue(transport, settings);
        }

        /// <summary>
        /// The entire roster comes from StreamingAssets/monsters.json — stats,
        /// movement, specials, loot, spawns. Tuning a mob is a JSON edit + rebuild.
        /// </summary>
        /// <summary>
        /// Prefabs that must be registered in NetworkManager.spawnPrefabs so a client can
        /// spawn whatever the server spawns (enemies now; projectiles/loot the same way
        /// later). Rebuilt every demo build and kept in sync at BUILD time — never register
        /// a spawnable at runtime on only one side, or server/client assetIds diverge.
        /// </summary>
        private static readonly System.Collections.Generic.List<GameObject> networkSpawnPrefabs =
            new System.Collections.Generic.List<GameObject>();

        private static void CreateEnemies()
        {
            EnsureFolder("Assets/Prefabs");
            networkSpawnPrefabs.Clear();

            // The world geometry is already built (colliders present), so ground queries
            // resolve — but batch mode may have auto-sync off, so push transforms first.
            Physics.SyncTransforms();

            var defs = Data.MonsterDatabase.LoadAll();
            if (defs.Count == 0)
            {
                Debug.LogError("CreateEnemies: monsters.json missing or empty, no mobs built!");
                return;
            }

            foreach (var def in defs)
            {
                var prefab = BuildMobPrefab(def);
                foreach (var spawn in def.spawns)
                {
                    var pos = new Vector3(spawn.x, spawn.y, spawn.z);
                    if (spawn.kind == "camp")
                        CreateCamp($"{def.id}_camp", prefab, pos, spawn.count, spawn.radius, spawn.respawn);
                    else
                        CreateSpawner($"{def.id}_spawner", prefab, pos, spawn.respawn);
                }
            }
            Debug.Log($"CreateEnemies: built {defs.Count} monster types from monsters.json");

            CreateDancingGoblins();
            CreateLootPrefab();
            new GameObject("DungeonResetController").AddComponent<AI.DungeonResetController>();
        }

        /// <summary>
        /// Builds the networked world-drop prefab (NetworkIdentity + LootDrop) into
        /// Resources so LootDrop.ServerSpawn can Instantiate it, and registers it in the
        /// spawn list so clients can spawn what the server spawns. Visual is built per-item
        /// at runtime, so the prefab itself is just the networking shell.
        /// </summary>
        private static void CreateLootPrefab()
        {
            EnsureFolder("Assets/Resources");
            const string path = "Assets/Resources/NetLootDrop.prefab";
            AssetDatabase.DeleteAsset(path);

            var go = new GameObject("NetLootDrop");
            go.AddComponent<Mirror.NetworkIdentity>();
            go.AddComponent<Items.LootDrop>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            networkSpawnPrefabs.Add(prefab);
            Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Neutral entertainment by the pond: dancing goblins on the north
        /// shore. They never attack (NeutralNPC, no EnemyAI) but are killable —
        /// and drop fish, because of course they're full of fish.
        /// </summary>
        private static void CreateDancingGoblins()
        {
            // A lively fishing troupe strung along the SOUTH/EAST dry shore of Emberfall
            // Lake (the side facing the city), beside the FishingSpot/Campfire.
            //
            // TWERKER ROOT CAUSE (finally): the old troupe clustered three goblins within
            // ~6m at x≈-96..-102, and the Twerker at x=-102 landed ON TOP of the lake's
            // wade-in step cubes, which live under the EmberfallLake container — a
            // NavMeshModifier there marks them Not Walkable. Its NavMeshAgent then couldn't
            // place on the NavMesh and the goblin never appeared, while the two on the
            // walkable sand did. Fix: no more basin steps (mud banks now), and every goblin
            // is placed on the flat walkable ground RING well clear of the basin, grounded.
            var styles = new[] { AI.DanceStyle.Modern, AI.DanceStyle.Twerk, AI.DanceStyle.Robot };
            var prefabByStyle = new Dictionary<AI.DanceStyle, GameObject>
            {
                { AI.DanceStyle.Modern, BuildDancingGoblinPrefab(AI.DanceStyle.Modern) },
                { AI.DanceStyle.Robot,  BuildDancingGoblinPrefab(AI.DanceStyle.Robot) },
                { AI.DanceStyle.Twerk,  BuildDancingGoblinPrefab(AI.DanceStyle.Twerk) },
            };

            Vector3 lake = LakeCenter;
            const int count = 9;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float deg = Mathf.Lerp(-120f, 30f, t);           // S → SE → E shore (faces the city)
                float rad = 60f + (i % 2) * 6f;                   // staggered depth, clear of the basin
                float x = lake.x + Mathf.Cos(deg * Mathf.Deg2Rad) * rad;
                float z = lake.z + Mathf.Sin(deg * Mathf.Deg2Rad) * rad;
                var style = styles[i % styles.Length];
                CreateSpawner($"dancing_goblin_{i}_{style}", prefabByStyle[style],
                    new Vector3(x, 0f, z), 30f);                  // CreateSpawner grounds y
            }
        }

        private static GameObject BuildDancingGoblinPrefab(AI.DanceStyle style)
        {
            string prefabPath = $"Assets/Prefabs/dancing_goblin_{style.ToString().ToLowerInvariant()}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);

            const float heightMeters = 1.4f;
            var mob = new GameObject("dancing_goblin");
            mob.tag = "Enemy";   // targetable/killable — behavior stays neutral
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            mob.layer = enemyLayer >= 0 ? enemyLayer : 0;

            float radius = Mathf.Max(0.4f, heightMeters * 0.22f);
            var collider = mob.AddComponent<CapsuleCollider>();
            collider.height = heightMeters;
            collider.radius = radius;
            collider.center = new Vector3(0, heightMeters / 2f, 0);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(mob.transform);
            visual.transform.localPosition = Vector3.zero;
            // Dedicated party-goblin model (full mocap dance kit); old Goblin
            // as interim fallback until GoblinDancer is generated + imported.
            var generated = SceneSetup.InstantiateGeneratedVisual("GoblinDancer", visual.transform)
                         ?? SceneSetup.InstantiateGeneratedVisual("Goblin", visual.transform);
            if (generated == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "FallbackBody";
                capsule.transform.SetParent(visual.transform);
                capsule.transform.localPosition = new Vector3(0, heightMeters / 2f, 0);
                capsule.transform.localScale = new Vector3(radius * 2f, heightMeters / 2f, radius * 2f);
                Object.DestroyImmediate(capsule.GetComponent<CapsuleCollider>());
                capsule.GetComponent<MeshRenderer>().sharedMaterial =
                    SceneSetup.GetOrCreateMaterialAsset(new Color(0.3f, 0.48f, 0.25f), "DancingGoblinMaterial");
            }

            var agent = mob.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height = heightMeters;
            agent.radius = radius;
            agent.speed = 3.5f;

            var statBlock = mob.AddComponent<Stats.StatBlock>();
            ConfigureStats(statBlock, new (Stats.StatType, float)[]
            {
                (Stats.StatType.MaxHealth, 30f),
                (Stats.StatType.Attack, 2f),
                (Stats.StatType.Strength, 2f),
                (Stats.StatType.Defence, 1f),
                (Stats.StatType.Magic, 1f),
                (Stats.StatType.WeaponPower, 1f),
                (Stats.StatType.Armor, 0f),
            });

            var health = mob.AddComponent<Combat.Health>();
            var healthSO = new SerializedObject(health);
            healthSO.FindProperty("maxHealth").floatValue = 30f;
            healthSO.ApplyModifiedProperties();

            mob.AddComponent<Combat.StatusSystem>();

            // EnemyAI with ZERO detection range: never aggros on its own, but
            // NeutralNPC ForceAggros it when hurt — then it chases and swings
            // like any goblin. Also owns death (anim, collider, destroy), which
            // is what lets the EnemySpawner respawn it.
            var ai = mob.AddComponent<AI.EnemyAI>();
            var aiSO = new SerializedObject(ai);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                aiSO.FindProperty("playerLayer").intValue = 1 << playerLayer;
            aiSO.FindProperty("detectionRange").floatValue = 0f;   // peaceful by default
            aiSO.FindProperty("loseTargetRange").floatValue = 60f; // but it holds a grudge
            aiSO.FindProperty("attackRange").floatValue = 2.2f;
            aiSO.FindProperty("attackCooldown").floatValue = 1.6f;
            aiSO.FindProperty("walkSpeed").floatValue = 3.5f;
            aiSO.FindProperty("runSpeed").floatValue = 5.5f;
            aiSO.FindProperty("jumpAttackEnabled").boolValue = false;
            aiSO.FindProperty("chargeAttackEnabled").boolValue = false;
            aiSO.FindProperty("autoGeneratePatrol").boolValue = false; // stays put, dances
            aiSO.ApplyModifiedProperties();

            var npc = mob.AddComponent<AI.NeutralNPC>();   // dances; retaliates when hurt
            var npcSO = new SerializedObject(npc);
            npcSO.FindProperty("danceStyle").enumValueIndex = (int)style;
            npcSO.ApplyModifiedProperties();

            var identity = mob.AddComponent<AI.MobIdentity>();
            ConfigureIdentity(identity, "Dancing Goblin", 1, new (string, float)[]
            {
                ("raw_fish", 1f),      // guaranteed fish
                ("raw_fish", 0.5f),    // sometimes a second
            });

            mob.AddComponent<UI.EnemyHealthBar>();

            AddEnemyNetworking(mob);

            var prefab = PrefabUtility.SaveAsPrefabAsset(mob, prefabPath);
            networkSpawnPrefabs.Add(prefab);
            Object.DestroyImmediate(mob);
            return prefab;
        }

        /// <summary>Writes base stats into a StatBlock's serialized list.</summary>
        private static void ConfigureStats(Stats.StatBlock statBlock, (Stats.StatType type, float value)[] stats)
        {
            var so = new SerializedObject(statBlock);
            var list = so.FindProperty("baseStats");
            list.arraySize = stats.Length;
            for (int i = 0; i < stats.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("type").enumValueIndex = (int)stats[i].type;
                element.FindPropertyRelative("value").floatValue = stats[i].value;
            }
            so.ApplyModifiedProperties();
        }

        /// <summary>Writes identity + loot table into a MobIdentity's serialized fields.</summary>
        private static void ConfigureIdentity(AI.MobIdentity identity, string displayName,
            int level, (string id, float chance)[] loot, string mobIdOverride = null)
        {
            var so = new SerializedObject(identity);
            so.FindProperty("displayName").stringValue = displayName;
            so.FindProperty("combatLevel").intValue = level;
            // Stable slug so KillMob quest objectives can target this mob type.
            // Prefer the JSON id (e.g. "grukmar") so quests target it exactly.
            so.FindProperty("MobId").stringValue =
                !string.IsNullOrEmpty(mobIdOverride) ? mobIdOverride
                : displayName.ToLowerInvariant().Replace(' ', '_');
            var table = so.FindProperty("lootTable");
            table.arraySize = loot.Length;
            for (int i = 0; i < loot.Length; i++)
            {
                var element = table.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("itemId").stringValue = loot[i].id;
                element.FindPropertyRelative("chance").floatValue = loot[i].chance;
            }
            so.ApplyModifiedProperties();
        }

        /// <summary>
        /// Makes a freshly-built enemy GameObject a server-authoritative networked entity:
        /// NetworkIdentity + server→client NetworkTransform + (when animated) NetworkAnimator
        /// + the EnemyNetworkSync authority gate. Call right before SaveAsPrefabAsset. The
        /// resulting prefab MUST also be added to networkSpawnPrefabs (done at the call site).
        /// </summary>
        private static void AddEnemyNetworking(GameObject mob)
        {
            // NetworkIdentity first — Mirror wants exactly one, ahead of the NetworkBehaviours.
            mob.AddComponent<Mirror.NetworkIdentity>();

            // RELIABLE transform sync (was NetworkTransformUnreliable). The desktop build
            // reaches the Render server over SimpleWebTransport (WebSocket/WSS) — a
            // reliable-only, TCP-backed transport with no real unreliable channel. An
            // "Unreliable" NetworkTransform on a reliable-only transport is a mismatch that
            // left clients rendering enemies frozen at their spawn snapshot while the server
            // enemy actually chased and meleed — the "frozen enemy that damages from range"
            // report. Reliable delta-compresses over the reliable channel and replicates
            // server-driven movement dependably. Coordinate space stays Local (the default)
            // so syncScale keeps working for the boss's rage growth (World space forces
            // syncScale off in NetworkTransformBase).
            var nt = mob.AddComponent<Mirror.NetworkTransformReliable>();
            nt.target = mob.transform;
            nt.syncDirection = Mirror.SyncDirection.ServerToClient;   // server drives movement
            nt.syncPosition = true;
            nt.syncRotation = true;
            nt.syncScale = true;   // the boss GROWS during rage — scale must replicate

            // Replicate attack/death/dance animation when the visual has an Animator.
            var anim = mob.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                var na = mob.AddComponent<Mirror.NetworkAnimator>();
                na.animator = anim;
                na.clientAuthority = false;   // server owns the animation state
                // MUST be ServerToClient or NetworkAnimator.SetTrigger early-returns on the
                // server (no client owner) and enemy attack/death triggers never replicate.
                na.syncDirection = Mirror.SyncDirection.ServerToClient;
            }

            // Authority gate: disables EnemyAI / NavMeshAgent / BossRageMode on pure clients.
            mob.AddComponent<AI.EnemyNetworkSync>();
        }

        /// <summary>Builds a configurable mob prefab (visual, StatBlock, identity, loot).</summary>
        private static GameObject BuildMobPrefab(Data.MonsterDef def)
        {
            string prefabPath = $"Assets/Prefabs/{def.id}.prefab";
            AssetDatabase.DeleteAsset(prefabPath);

            float heightMeters = def.heightMeters;
            var mob = new GameObject(def.id);
            mob.tag = "Enemy";
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            mob.layer = enemyLayer >= 0 ? enemyLayer : 0;

            float radius = Mathf.Max(0.4f, heightMeters * 0.22f);
            var collider = mob.AddComponent<CapsuleCollider>();
            collider.height = heightMeters;
            collider.radius = radius;
            collider.center = new Vector3(0, heightMeters / 2f, 0);

            // Visual: generated Meshy model or capsule fallback
            var visual = new GameObject("Visual");
            visual.transform.SetParent(mob.transform);
            visual.transform.localPosition = Vector3.zero;
            var generated = SceneSetup.InstantiateGeneratedVisual(def.character, visual.transform);
            if (generated == null)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "FallbackBody";
                capsule.transform.SetParent(visual.transform);
                capsule.transform.localPosition = new Vector3(0, heightMeters / 2f, 0);
                capsule.transform.localScale = new Vector3(radius * 2f, heightMeters / 2f, radius * 2f);
                Object.DestroyImmediate(capsule.GetComponent<CapsuleCollider>());
                capsule.GetComponent<MeshRenderer>().sharedMaterial =
                    SceneSetup.GetOrCreateMaterialAsset(def.FallbackColor, $"{def.id}Material");
            }

            // ---- Elite treatment: dire mini-bosses + the warlord read as bigger
            // and menacing. Scale the whole visual up (generated models ignore
            // heightMeters, so this is what makes them visibly larger) and add a
            // low, blood-red glow. heightMeters is already bumped in JSON so the
            // collider/agent grow to match.
            float eliteScale = def.id.StartsWith("dire_") ? 1.3f
                             : def.id == "grukmar" ? 1.5f
                             : 1f;
            if (eliteScale > 1f)
            {
                visual.transform.localScale = Vector3.one * eliteScale;

                var glowObj = new GameObject("EliteGlow");
                glowObj.transform.SetParent(mob.transform);
                glowObj.transform.localPosition = new Vector3(0f, heightMeters * 0.55f, 0f);
                var glow = glowObj.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(0.85f, 0.12f, 0.1f);
                glow.intensity = 2.2f;
                glow.range = Mathf.Max(5f, heightMeters * 3.2f);
                glow.shadows = LightShadows.None;
            }

            var agent = mob.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height = heightMeters;
            agent.radius = radius;
            agent.speed = def.movement.walkSpeed;

            // StatBlock is the single source of truth for this mob's combat math —
            // the SAME systems the player runs on (stats, status, CombatMath).
            var statBlock = mob.AddComponent<Stats.StatBlock>();
            ConfigureStats(statBlock, new (Stats.StatType, float)[]
            {
                (Stats.StatType.MaxHealth, def.stats.maxHealth),
                (Stats.StatType.Attack, def.stats.attack),
                (Stats.StatType.Strength, def.stats.strength),
                (Stats.StatType.Defence, def.stats.defence),
                (Stats.StatType.Magic, def.stats.magic),
                (Stats.StatType.WeaponPower, def.stats.weaponPower),
                (Stats.StatType.Armor, def.stats.armor),
            });

            var health = mob.AddComponent<Combat.Health>();
            var healthSO = new SerializedObject(health);
            healthSO.FindProperty("maxHealth").floatValue = def.stats.maxHealth; // StatBlock overrides at Awake
            healthSO.ApplyModifiedProperties();

            mob.AddComponent<Combat.StatusSystem>();

            var ai = mob.AddComponent<AI.EnemyAI>();
            var aiSO = new SerializedObject(ai);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
                aiSO.FindProperty("playerLayer").intValue = 1 << playerLayer;
            aiSO.FindProperty("walkSpeed").floatValue = def.movement.walkSpeed;
            aiSO.FindProperty("runSpeed").floatValue = def.movement.runSpeed;
            aiSO.FindProperty("attackRange").floatValue = def.combat.attackRange;
            aiSO.FindProperty("detectionRange").floatValue = def.combat.detectionRange;
            aiSO.FindProperty("loseTargetRange").floatValue = def.combat.detectionRange + 14f;
            aiSO.FindProperty("attackCooldown").floatValue = def.combat.attackCooldown;
            aiSO.FindProperty("basicKnockback").floatValue = def.combat.basicKnockback;
            aiSO.FindProperty("jumpAttackEnabled").boolValue = def.combat.jumpAttack.enabled;
            aiSO.FindProperty("jumpAttackCooldown").floatValue = def.combat.jumpAttack.cooldown;
            aiSO.FindProperty("jumpAttackKnockback").floatValue = def.combat.jumpAttack.knockback;
            aiSO.FindProperty("chargeAttackEnabled").boolValue = def.combat.chargeAttack.enabled;
            aiSO.FindProperty("chargeAttackCooldown").floatValue = def.combat.chargeAttack.cooldown;
            aiSO.FindProperty("chargeKnockback").floatValue = def.combat.chargeAttack.knockback;
            aiSO.FindProperty("chargeChance").floatValue = def.combat.chargeAttack.chance;
            aiSO.ApplyModifiedProperties();

            var identity = mob.AddComponent<AI.MobIdentity>();
            var lootTuples = new (string, float)[def.loot.Length];
            for (int i = 0; i < def.loot.Length; i++)
                lootTuples[i] = (def.loot[i].itemId, def.loot[i].chance);
            ConfigureIdentity(identity, def.displayName, def.level, lootTuples, def.id);

            mob.AddComponent<UI.EnemyHealthBar>();

            // Bosses get the two-phase rage fight
            if (def.id.Contains("boss"))
                mob.AddComponent<AI.BossRageMode>();

            AddEnemyNetworking(mob);

            var prefab = PrefabUtility.SaveAsPrefabAsset(mob, prefabPath);
            networkSpawnPrefabs.Add(prefab);
            Object.DestroyImmediate(mob);
            return prefab;
        }

        /// <summary>
        /// Snaps a spawn point down onto the actual ground so nothing floats in the sky /
        /// on an invisible floor (img 23). Raycasts against the Ground layer; falls back to
        /// the flat valley (y=0) when nothing is hit. Keeps x/z, only corrects y.
        /// </summary>
        private static Vector3 GroundSpawn(Vector3 pos)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            int mask = groundLayer >= 0 ? (1 << groundLayer) : ~0;
            if (Physics.Raycast(new Vector3(pos.x, 250f, pos.z), Vector3.down, out var hit,
                    500f, mask, QueryTriggerInteraction.Ignore))
                return new Vector3(pos.x, hit.point.y + 0.05f, pos.z);
            return new Vector3(pos.x, 0.05f, pos.z);   // flat valley floor
        }

        /// <summary>
        /// Nudges a spawn point onto the nearest baked NavMesh (small radius so it only
        /// corrects tiny offsets and preserves the intended layout). If nothing is found
        /// within range the original point is kept — EnemyAI.InitializeNavMeshAgent still
        /// self-heals onto the mesh within 10 units at runtime after the server rebake.
        /// </summary>
        private static Vector3 SnapToNavMesh(Vector3 pos)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                return new Vector3(hit.position.x, pos.y, hit.position.z);
            return pos;
        }

        private static void CreateSpawner(string name, GameObject prefab, Vector3 pos, float respawnDelay)
        {
            pos = GroundSpawn(pos);
            pos = SnapToNavMesh(pos);   // ensure the agent can activate on spawn
            var spawnerObj = new GameObject(name);
            spawnerObj.transform.position = pos;
            var spawner = spawnerObj.AddComponent<AI.EnemySpawner>();
            var so = new SerializedObject(spawner);
            so.FindProperty("enemyPrefab").objectReferenceValue = prefab;
            so.FindProperty("spawnPosition").vector3Value = pos;
            so.FindProperty("respawnDelay").floatValue = respawnDelay;
            so.FindProperty("spawnOnStart").boolValue = true;
            var respawnProp = so.FindProperty("respawnAfterDeath");
            // V2: the volcano dungeon lies to the SOUTH (z < -300) and is clearable — its mobs
            // never respawn. Everything north of that (city + forest ring + ember approach)
            // respawns. A huge respawn delay (999999 in JSON) additionally marks a one-and-done
            // quest boss (Grukmar, the Floor Boss) that never returns wherever it stands.
            if (respawnProp != null) respawnProp.boolValue = pos.z > -300f && respawnDelay < 100000f;
            so.ApplyModifiedProperties();
        }

        private static void CreateCamp(string name, GameObject prefab, Vector3 pos, int population, float radius, float respawnDelay)
        {
            pos = GroundSpawn(pos);
            var campObj = new GameObject(name);
            campObj.transform.position = pos;
            var camp = campObj.AddComponent<AI.MobCamp>();
            var so = new SerializedObject(camp);
            so.FindProperty("mobPrefab").objectReferenceValue = prefab;
            so.FindProperty("population").intValue = population;
            so.FindProperty("radius").floatValue = radius;
            so.FindProperty("respawnDelay").floatValue = respawnDelay;
            var respawnProp = so.FindProperty("respawnAfterDeath");
            // V2: the volcano dungeon (z < -300) is clearable — its camps never respawn.
            // Forest-ring camps north of that respawn; a 999999 delay marks one-and-done.
            if (respawnProp != null) respawnProp.boolValue = pos.z > -300f && respawnDelay < 100000f;
            so.ApplyModifiedProperties();
        }

        // ==================== HELPERS ====================

        private static Transform Child(Transform parent, string name)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            return obj.transform;
        }

        private static GameObject Cube(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
            => Primitive(PrimitiveType.Cube, name, pos, scale, mat, parent);

        private static GameObject Cylinder(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
            => Primitive(PrimitiveType.Cylinder, name, pos, scale, mat, parent);

        private static GameObject Sphere(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
            => Primitive(PrimitiveType.Sphere, name, pos, scale, mat, parent);

        private static void BuildGrassBladeField(Transform parent, string name,
            float xMin, float xMax, float zMin, float zMax, int bladesPerTile,
            Color color, System.Func<float, float, bool> allowAt)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            obj.isStatic = true;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            var localRng = new System.Random(name.GetHashCode());

            for (float tx = xMin; tx < xMax; tx += TileSize)
            {
                for (float tz = zMin; tz < zMax; tz += TileSize)
                {
                    for (int i = 0; i < bladesPerTile; i++)
                    {
                        float x = tx + 0.35f + (float)localRng.NextDouble() * (TileSize - 0.7f);
                        float z = tz + 0.35f + (float)localRng.NextDouble() * (TileSize - 0.7f);
                        if (!allowAt(x, z)) continue;

                        // Short, flat ground cover (not waist-high spikes): 0.12-0.28m tall with
                        // a wider splay so it reads as walkable grass patches, per playtest feedback.
                        float height = 0.12f + (float)localRng.NextDouble() * 0.16f;
                        float width = 0.045f + (float)localRng.NextDouble() * 0.05f;
                        float angle = (float)localRng.NextDouble() * Mathf.PI * 2f;
                        Vector3 right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        Vector3 lean = new Vector3(-right.z, 0f, right.x) * (0.06f + (float)localRng.NextDouble() * 0.12f);
                        Vector3 baseCenter = new Vector3(x, 0.03f, z);
                        Vector3 topCenter = baseCenter + Vector3.up * height + lean;

                        int start = vertices.Count;
                        vertices.Add(baseCenter - right * width);
                        vertices.Add(baseCenter + right * width);
                        vertices.Add(topCenter + right * width * 0.35f);
                        vertices.Add(topCenter - right * width * 0.35f);

                        triangles.Add(start + 0); triangles.Add(start + 2); triangles.Add(start + 1);
                        triangles.Add(start + 0); triangles.Add(start + 3); triangles.Add(start + 2);
                        triangles.Add(start + 0); triangles.Add(start + 1); triangles.Add(start + 2);
                        triangles.Add(start + 0); triangles.Add(start + 2); triangles.Add(start + 3);

                        uvs.Add(new Vector2(0f, 0f));
                        uvs.Add(new Vector2(1f, 0f));
                        uvs.Add(new Vector2(1f, 1f));
                        uvs.Add(new Vector2(0f, 1f));
                    }
                }
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            // Point every blade normal UP (not RecalculateNormals, which gives vertical faces
            // sideways normals that catch no overhead light and render as black shards). Lit as
            // ground cover, the blades read as bright grass matching the textured ground.
            var upNormals = new List<Vector3>(vertices.Count);
            for (int n = 0; n < vertices.Count; n++) upNormals.Add(Vector3.up);
            mesh.SetNormals(upNormals);
            mesh.RecalculateBounds();

            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = Mat(name + "Material", color, smoothness: 0.02f);
        }

        private static GameObject ColliderBox(string name, Vector3 pos, Vector3 size, Transform parent, int layer)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent);
            obj.transform.position = pos;
            obj.isStatic = true;
            if (layer >= 0) obj.layer = layer;
            var box = obj.AddComponent<BoxCollider>();
            box.size = size;

            var modifier = obj.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = 1;
            return obj;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent);
            obj.transform.position = pos;
            obj.transform.localScale = scale;
            obj.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return obj;
        }

        private static Shader LitShader()
        {
            if (litShader != null) return litShader;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            litShader = temp.GetComponent<MeshRenderer>().sharedMaterial.shader;
            Object.DestroyImmediate(temp);
            return litShader;
        }

        private static Material Mat(string name, Color color, float smoothness = 0.1f,
            bool transparent = false, Color? emission = null)
        {
            if (materials.TryGetValue(name, out var cached)) return cached;

            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(LitShader());
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != LitShader())
            {
                mat.shader = LitShader(); // upgrade cached materials if the pipeline changed
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
            }

            if (transparent)
            {
                // Full URP Lit transparent setup — the shader needs every one of
                // these to actually alpha-blend (partial setup renders opaque).
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // alpha blend
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_SrcBlendAlpha"))
                    mat.SetInt("_SrcBlendAlpha", (int)UnityEngine.Rendering.BlendMode.One);
                if (mat.HasProperty("_DstBlendAlpha"))
                    mat.SetInt("_DstBlendAlpha", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                if (mat.HasProperty("_ZWriteControl")) mat.SetFloat("_ZWriteControl", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetShaderPassEnabled("DepthOnly", false);
                mat.SetShaderPassEnabled("SHADOWCASTER", false);
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(mat);
            materials[name] = mat;
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
