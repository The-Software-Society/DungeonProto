#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace AldenFall.Editor
{
    /// <summary>
    /// Headless visual QA: opens the demo scene and renders labelled camera
    /// angles to PNGs. Output dir comes from -screenshotDir on the command line
    /// (falls back to ./Screenshots).
    /// </summary>
    public static class ScreenshotTool
    {
        public static void CaptureDemo() => Run(new (string, Vector3, Vector3)[]
        {
            ("overview",  new Vector3(0, 55f, -70f),  new Vector3(0, 0, 0)),
            ("plaza",     new Vector3(8f, 4f, -14f),  new Vector3(0, 1.5f, 0)),
            ("goblin",    new Vector3(3f, 2.5f, 4f),  new Vector3(0, 1f, 8f)),
            ("player",    new Vector3(2.5f, 2.5f, -34f), new Vector3(0, 1f, -30f)),
            ("pond",      new Vector3(-12f, 5f, -10f), new Vector3(-24f, 0f, 0f)),
            ("props",     new Vector3(24f, 3f, -6f),  new Vector3(29f, 1f, 0f)),
        });

        /// <summary>Ground-focused close-ups: grass/dirt patches in several biomes + inside and
        /// outside the tavern. Low camera angled down so the ground texture fills the frame.</summary>
        public static void CaptureGround() => Run(new (string, Vector3, Vector3)[]
        {
            ("grass_glade",     new Vector3(0f, 2.4f, 84f),    new Vector3(0f, 0f, 92f)),
            ("grass_forest",    new Vector3(-60f, 2.4f, 34f),  new Vector3(-60f, 0f, 44f)),
            ("grass_northfield",new Vector3(40f, 2.4f, 120f),  new Vector3(40f, 0f, 132f)),
            ("dirt_lakeshore",  new Vector3(-100f, 2.4f, 118f),new Vector3(-108f, 0f, 128f)),
            ("dirt_approach",   new Vector3(0f, 2.4f, -150f),  new Vector3(0f, 0f, -162f)),
            ("city_lot",        new Vector3(-20f, 2.4f, 14f),  new Vector3(-20f, 0f, 24f)),
            ("tavern_outside",  new Vector3(-32f, 2.6f, 46f),  new Vector3(-32f, 1.2f, 32f)),
            ("tavern_inside",   new Vector3(-32f, 2.0f, 37f),  new Vector3(-32f, 1.0f, 28f)),
        });

        /// <summary>Lake + hydrology angles: the lake is centred at (-138,138), ~72m radius,
        /// cradled by mountains, fed by the north feeder river.</summary>
        public static void CaptureLake() => Run(new (string, Vector3, Vector3)[]
        {
            ("lake_birdseye",  new Vector3(0f, 150f, 120f),    new Vector3(0f, 0f, 300f)),      // whole lake + mtns
            ("lake_from_spawn",new Vector3(0f, 4f, 185f),      new Vector3(0f, 4f, 300f)),      // Aldric's shore looking across
            ("lake_shore_E",   new Vector3(90f, 6f, 300f),     new Vector3(0f, 3f, 300f)),      // east shore across water
            ("lake_river_in",  new Vector3(20f, 12f, 420f),    new Vector3(5f, 0f, 360f)),      // north river inflow
            ("lake_wide",      new Vector3(0f, 70f, 150f),     new Vector3(0f, 0f, 320f)),      // wide basin + mtns
        });

        /// <summary>A world tour: one labelled shot per feature, for the montage video.
        /// Ordered as a walk from spawn -> lake -> mountains -> rivers -> city -> volcano.</summary>
        public static void CaptureTour() => Run(new (string, Vector3, Vector3)[]
        {
            ("01_spawn_shore",   new Vector3(0f, 13f, 150f),   new Vector3(0f, 0f, 240f)),
            ("02_lake_vista",    new Vector3(-70f, 22f, 195f), new Vector3(30f, 0f, 320f)),
            ("03_mountain_range",new Vector3(0f, 30f, 330f),   new Vector3(0f, 60f, 520f)),
            ("04_north_waterfall",new Vector3(40f, 20f, 425f), new Vector3(0f, 6f, 486f)),
            ("05_connecting_river",new Vector3(190f, 24f, 345f),new Vector3(90f, 0f, 360f)),
            ("06_river_to_city", new Vector3(60f, 20f, 60f),   new Vector3(-70f, 0f, 90f)),
            ("07_city_river",    new Vector3(40f, 16f, 22f),   new Vector3(-60f, 0f, 55f)),
            ("08_city",          new Vector3(90f, 34f, -75f),  new Vector3(0f, 0f, 10f)),
            ("09_dirt_road",     new Vector3(24f, 16f, 88f),   new Vector3(0f, 0f, 172f)),
            ("10_forest",        new Vector3(-150f, 20f, 235f),new Vector3(-150f, 0f, 320f)),
            ("11_ember_approach",new Vector3(0f, 16f, -135f),  new Vector3(0f, 2f, -260f)),
            ("12_volcano",       new Vector3(0f, 26f, -110f),  new Vector3(0f, 42f, -345f)),
        });

        /// <summary>Tight close-ups of the exact problem spots for verify-driven fixing.</summary>
        public static void CaptureDiag() => Run(new (string, Vector3, Vector3)[]
        {
            // Natural 3rd-person-ish angles (higher, ~30 deg down) - the old y2-4 grazing angles
            // made smooth bare ground (sand/dirt) mirror the sky and read as fake water.
            ("diag_spawn_lake",     new Vector3(0f, 12f, 168f),     new Vector3(0f, 0f, 250f)),
            ("diag_camp_goblin",    new Vector3(-195f, 14f, 42f),   new Vector3(-195f, 0f, -10f)),
            ("diag_forest",         new Vector3(-120f, 14f, 108f),  new Vector3(-120f, 0f, 175f)),
            ("diag_nwpatch",        new Vector3(-150f, 16f, 250f),  new Vector3(-135f, 0f, 305f)),
            ("diag_waterfall",      new Vector3(20f, 12f, 452f),    new Vector3(0f, 4f, 486f)),
        });

        /// <summary>High aerial of the whole expanded world to judge the macro layout.</summary>
        public static void CaptureMacro() => Run(new (string, Vector3, Vector3)[]
        {
            ("macro_N",  new Vector3(0f, 430f, -200f),  new Vector3(0f, 0f, 180f)),   // looking N over city->lake
            ("macro_top",new Vector3(0f, 620f, 40f),    new Vector3(0f, 0f, 45f)),    // near top-down
            ("macro_E",  new Vector3(-420f, 300f, 120f),new Vector3(60f, 0f, 160f)),  // from the west
        });

        public static void CheckTerrain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            var terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null) { Debug.Log("CHECK: no Terrain in scene"); return; }
            var data = terrain.terrainData;
            Debug.Log($"CHECK layers={data.terrainLayers.Length} splatRes={data.alphamapResolution} " +
                      $"names=[{string.Join(",", System.Array.ConvertAll(data.terrainLayers, l => l == null ? "NULL" : l.name))}]");
            var mat = terrain.materialTemplate;
            Debug.Log($"CHECK terrain material={(mat == null ? "NULL(default)" : mat.name)} shader={(mat == null ? "?" : mat.shader.name)}");
            // world (0,-250) -> alphamap index. origin z=-470 sizeZ=1040, origin x=-450 sizeX=900, res=512
            int res = data.alphamapResolution;
            int ix = Mathf.RoundToInt((0f - (-450f)) / 900f * (res - 1));
            int iz = Mathf.RoundToInt((-250f - (-470f)) / 1040f * (res - 1));
            var a = data.GetAlphamaps(ix, iz, 1, 1);
            Debug.Log($"CHECK volcano alphamap grass={a[0, 0, 0]:F2} sand={a[0, 0, 1]:F2} soot={a[0, 0, 2]:F2} dirt={a[0, 0, 3]:F2} rock={a[0, 0, 4]:F2}");
        }

        public static void CheckPuddles()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            var terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null) { Debug.Log("PUDDLE: no terrain"); return; }
            var data = terrain.terrainData;
            Vector3 tp = terrain.transform.position;
            const float waterY = -0.35f;
            int res = data.heightmapResolution;
            var hm = data.GetHeights(0, 0, res, res);
            // Lake disc (0,300,r95). Count sub-water texels OUTSIDE it (rivers + stray puddles).
            int subWater = 0, outsideLake = 0;
            float minH = 999f;
            var samples = new System.Collections.Generic.List<string>();
            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = tp.x + ix / (float)(res - 1) * data.size.x;
                    float wz = tp.z + iz / (float)(res - 1) * data.size.z;
                    float wy = tp.y + hm[iz, ix] * data.size.y;
                    if (wy < minH) minH = wy;
                    if (wy < waterY)
                    {
                        subWater++;
                        float lakeR = Mathf.Sqrt(wx * wx + (wz - 300f) * (wz - 300f));
                        if (lakeR > 108f)
                        {
                            outsideLake++;
                            // Focus on the exact NW-field box the forest shot frames.
                            if (samples.Count < 40 && wz > 255f && wz < 340f && wx > -200f && wx < -100f)
                                samples.Add($"({wx:F0},{wz:F0},y{wy:F2})");
                        }
                    }
                }
            Debug.Log($"PUDDLE subWater={subWater} outsideLake={outsideLake} minWorldY={minH:F2} res={res}");
            Debug.Log($"PUDDLE outside-lake samples: {string.Join(" ", samples)}");
            // Fine terrain-height dump over the NW-field water the shot frames, via SampleHeight
            // (matches the rendered terrain), plus list any water renderers overlapping it.
            for (float wz = 165f; wz <= 215f; wz += 10f)
            {
                var row = new System.Text.StringBuilder($"GRID z{wz:F0}: ");
                for (float wx = -30f; wx <= 30f; wx += 10f)
                {
                    float th = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
                    row.Append($"x{wx:F0}={th:F2}{(th < -0.35f ? "*W" : "")} ");
                }
                Debug.Log(row.ToString());
            }
            // Every renderer whose bounds overlap the spawn foreground (x-25..25, z170..200).
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var b = r.bounds;
                if (b.min.x < 25f && b.max.x > -25f && b.min.z < 200f && b.max.z > 170f && b.min.y < 0.5f && b.max.y > -1.5f
                    && (r.name.Contains("Water") || r.name.Contains("Lake") || r.name.Contains("Surface") || r.name.Contains("River") || r.name.Contains("Feeder") || r.name.Contains("Outflow")))
                    Debug.Log($"SPAWNREND {r.name} mat={r.sharedMaterial?.name} y[{b.min.y:F2},{b.max.y:F2}] x[{b.min.x:F0},{b.max.x:F0}] z[{b.min.z:F0},{b.max.z:F0}]");
            }
            var ls = GameObject.Find("Lake_Surface");
            if (ls != null) { var lb = ls.GetComponent<Renderer>().bounds; Debug.Log($"LAKESURF x[{lb.min.x:F0},{lb.max.x:F0}] z[{lb.min.z:F0},{lb.max.z:F0}] y[{lb.min.y:F2},{lb.max.y:F2}]"); }
            Debug.Log($"SCENE has {Object.FindObjectsOfType<Terrain>().Length} terrain(s); WorldWater present: {GameObject.Find("WorldWater") != null}; Lake_Surface: {GameObject.Find("Lake_Surface") != null}");
        }

        private static void Run((string name, Vector3 pos, Vector3 lookAt)[] shots)
        {
            string outDir = "Screenshots";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshotDir") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");

            var camObj = new GameObject("QA_Camera");
            var cam = camObj.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1400f;

            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            foreach (var shot in shots)
            {
                camObj.transform.position = shot.pos;
                camObj.transform.LookAt(shot.lookAt);

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                File.WriteAllBytes(Path.Combine(outDir, $"{shot.name}.png"), tex.EncodeToPNG());
                Debug.Log($"SCREENSHOT saved {shot.name}.png");
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camObj);
        }
    }
}
#endif
