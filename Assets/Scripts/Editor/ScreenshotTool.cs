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
            ("lake_birdseye",  new Vector3(-138f, 90f, 40f),   new Vector3(-138f, 0f, 138f)),   // whole lake + mtns
            ("lake_vista_SE",  new Vector3(-78f, 7f, 82f),     new Vector3(-150f, 2f, 150f)),   // across water to mtns
            ("lake_shore_S",   new Vector3(-138f, 5f, 74f),    new Vector3(-138f, 1f, 150f)),   // south shore looking N
            ("lake_river_in",  new Vector3(-120f, 8f, 208f),   new Vector3(-135f, 0f, 175f)),   // north river inflow
            ("lake_from_hill", new Vector3(-55f, 26f, 60f),    new Vector3(-138f, 0f, 138f)),   // hilltop over lake
            ("lake_wide",      new Vector3(-138f, 45f, -10f),  new Vector3(-138f, 0f, 150f)),   // wide basin
        });

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
