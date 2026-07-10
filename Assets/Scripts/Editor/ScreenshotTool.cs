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
        public static void CaptureDemo()
        {
            string outDir = "Screenshots";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshotDir") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");

            var shots = new (string name, Vector3 pos, Vector3 lookAt)[]
            {
                ("overview",  new Vector3(0, 55f, -70f),  new Vector3(0, 0, 0)),
                ("plaza",     new Vector3(8f, 4f, -14f),  new Vector3(0, 1.5f, 0)),
                ("goblin",    new Vector3(3f, 2.5f, 4f),  new Vector3(0, 1f, 8f)),
                ("player",    new Vector3(2.5f, 2.5f, -34f), new Vector3(0, 1f, -30f)),
                ("pond",      new Vector3(-12f, 5f, -10f), new Vector3(-24f, 0f, 0f)),
                ("props",     new Vector3(24f, 3f, -6f),  new Vector3(29f, 1f, 0f)),
            };

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
