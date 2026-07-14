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
            ("01_spawn_shore",   new Vector3(0f, 4f, 182f),    new Vector3(0f, 5f, 300f)),
            ("02_lake_vista",    new Vector3(-70f, 12f, 210f), new Vector3(30f, 3f, 320f)),
            ("03_mountain_range",new Vector3(0f, 30f, 330f),   new Vector3(0f, 60f, 520f)),
            ("04_north_waterfall",new Vector3(40f, 18f, 430f), new Vector3(0f, 12f, 486f)),
            ("05_connecting_river",new Vector3(190f, 16f, 350f),new Vector3(90f, 2f, 360f)),
            ("06_river_to_city", new Vector3(60f, 10f, 70f),   new Vector3(-70f, 2f, 90f)),
            ("07_city_river",    new Vector3(40f, 9f, 30f),    new Vector3(-60f, 1f, 55f)),
            ("08_city",          new Vector3(90f, 22f, -70f),  new Vector3(0f, 2f, 10f)),
            ("09_dirt_road",     new Vector3(22f, 7f, 95f),    new Vector3(0f, 1f, 175f)),
            ("10_forest",        new Vector3(-150f, 10f, 250f),new Vector3(-150f, 3f, 320f)),
            ("11_ember_approach",new Vector3(0f, 14f, -150f),  new Vector3(0f, 0f, -260f)),
            ("12_volcano",       new Vector3(0f, 30f, -230f),  new Vector3(0f, 10f, -330f)),
        });

        /// <summary>Tight close-ups of the exact problem spots for verify-driven fixing.</summary>
        public static void CaptureDiag() => Run(new (string, Vector3, Vector3)[]
        {
            ("diag_canal_eye",      new Vector3(46f, 4.5f, 20f),   new Vector3(-20f, 0.5f, 8f)),   // down the city canal
            ("diag_canal_top",      new Vector3(0f, 80f, 8f),      new Vector3(0f, 0f, 8f)),       // city top-down: canal thread
            ("diag_volcano_vista",  new Vector3(0f, 26f, -120f),   new Vector3(0f, 40f, -345f)),   // volcano silhouette from valley
            ("diag_waterfall",      new Vector3(18f, 6f, 458f),    new Vector3(0f, 6f, 486f)),
            ("diag_volcano_ground", new Vector3(0f, 3.5f, -210f),  new Vector3(20f, 0f, -260f)),
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
            cam.farClipPlane = 500f;

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
