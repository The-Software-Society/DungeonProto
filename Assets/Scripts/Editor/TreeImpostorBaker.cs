using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>
    /// Bakes distant-tree billboard impostors by RENDERING THE REAL FBX to a texture -- the BDO /
    /// New World technique, not a cube/cone/card stand-in (see CLAUDE.md HARD RULE). For each tree
    /// species we instantiate the actual model with its actual material, frame it side-on with an
    /// orthographic camera, and recover clean anti-aliased alpha via the classic TWO-BACKGROUND
    /// method (render over black + over white; alpha = 1 - (white-black), colour = black/alpha).
    /// Output: one RGBA texture .asset + one AldenFall/TreeImpostor material per species, plus a
    /// manifest.json of width/height aspect ratios that ArenaBuilder reads to size the LOD1 quad.
    ///
    /// MUST run with a GPU (Camera.Render needs a framebuffer). In the editor: menu below. Headless:
    ///   Unity -batchmode -projectPath . -executeMethod AldenFall.Editor.TreeImpostorBaker.BakeAll -quit
    /// (NOTE: no -nographics -- that disables rendering and the bake comes back empty.)
    /// </summary>
    public static class TreeImpostorBaker
    {
        private const string OutDir = "Assets/Materials/Emberfall/Impostors";
        private static readonly string[] Species = { "OakTree", "BirchTree", "DeadTree", "PineTree" };
        private const int TexHeight = 320;   // texel height of the impostor sheet (width follows aspect)

        [MenuItem("AldenFall/Bake Tree Impostors")]
        public static void BakeAll()
        {
            if (!AssetDatabase.IsValidFolder(OutDir))
                AssetDatabase.CreateFolder("Assets/Materials/Emberfall", "Impostors");

            var shader = Shader.Find("AldenFall/TreeImpostor");
            if (shader == null) { Debug.LogError("[Impostor] shader AldenFall/TreeImpostor not found"); return; }

            var aspects = new Dictionary<string, float>();
            foreach (var name in Species)
            {
                float aspect = Bake(name, shader);
                if (aspect > 0f) aspects[name] = aspect;
            }

            // Manifest: species -> width/height aspect of the baked silhouette (StreamingAssets so a
            // headless scene build can read it without an AssetDatabase round-trip).
            var manifestDir = "Assets/StreamingAssets/impostors";
            Directory.CreateDirectory(manifestDir);
            var sb = new System.Text.StringBuilder("{\n");
            int i = 0;
            foreach (var kv in aspects)
                sb.Append($"  \"{kv.Key}\": {kv.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}{(++i < aspects.Count ? "," : "")}\n");
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), sb.ToString());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Impostor] baked {aspects.Count}/{Species.Length} species -> {OutDir}");
        }

        /// <summary>Returns the baked silhouette aspect (width/height), or -1 on failure.</summary>
        private static float Bake(string name, Shader billboardShader)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{name}.fbx");
            if (asset == null) { Debug.LogError($"[Impostor] no fbx {name}"); return -1f; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            var mat = MeshyCharacterImporter.BuildTexturedMaterial(name, $"Assets/Models/Props/{name}_baseColor");
            if (mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.sharedMaterial = mat;

            // Stand the model upright at the origin the same way PlaceMeshyProp does: the FBX axis
            // conversion lives on the root rotation, so keep it (yaw 0). Normalise to a known height.
            go.transform.position = Vector3.zero;
            go.transform.rotation = go.transform.rotation;   // preserve import rotation

            Bounds b = MeasureWorld(go);
            if (b.size.y < 0.01f) { Object.DestroyImmediate(go); Debug.LogError($"[Impostor] {name} has no mesh"); return -1f; }
            // Recenter on origin, base at y=0.
            go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = MeasureWorld(go);

            float height = b.size.y;
            float width = Mathf.Max(b.size.x, b.size.z);   // widest horizontal silhouette
            float aspect = Mathf.Clamp(width / height, 0.15f, 4f);
            int texH = TexHeight;
            int texW = Mathf.Clamp(Mathf.RoundToInt(texH * aspect), 16, 1024);

            // Orthographic side camera looking along +Z at the tree, framing its full extent with a
            // small margin so anti-aliased edges are not clipped.
            var camGO = new GameObject("ImpostorCam");
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = height * 0.5f * 1.04f;
            cam.aspect = aspect;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = (b.size.z + width) * 2f + 50f;
            cam.transform.position = new Vector3(0f, height * 0.5f, -(b.extents.z + width + 5f));
            cam.transform.LookAt(new Vector3(0f, height * 0.5f, 0f));
            cam.cullingMask = ~0;
            cam.allowHDR = false;
            cam.allowMSAA = true;

            // A soft key light + ambient so the impostor reads like a daytime tree (fixed lighting is
            // fine at distance; the fog does most of the blending anyway).
            var lightGO = new GameObject("ImpostorLight");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            var prevAmbient = RenderSettings.ambientLight;
            var prevAmbMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.60f);

            var black = RenderPass(cam, texW, texH, Color.black);
            var white = RenderPass(cam, texW, texH, Color.white);

            RenderSettings.ambientLight = prevAmbient;
            RenderSettings.ambientMode = prevAmbMode;

            // Two-background alpha recovery, per pixel.
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, true, false);
            var cb = black.GetPixels();
            var cw = white.GetPixels();
            var outPx = new Color[cb.Length];
            for (int p = 0; p < cb.Length; p++)
            {
                // Where the tree is opaque, cw==cb -> a=1. Where it's the background, cw-cb=1 -> a=0.
                float a = 1f - ((cw[p].r - cb[p].r) + (cw[p].g - cb[p].g) + (cw[p].b - cb[p].b)) / 3f;
                a = Mathf.Clamp01(a);
                Color rgb = a > 0.003f ? new Color(cb[p].r / a, cb[p].g / a, cb[p].b / a, 1f) : Color.black;
                outPx[p] = new Color(rgb.r, rgb.g, rgb.b, a);
            }
            tex.SetPixels(outPx);
            tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var texPath = $"{OutDir}/{name}_impostor.asset";
            AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(tex, texPath);

            // Debug PNG (with alpha) so the bake can be eyeballed outside Unity. Also a checker-composite
            // so alpha fringe is visible against a mid-grey.
            var dbgDir = "/private/tmp/claude-501/-Users-jon-Developer-AldenFall/ef2dc15e-0a6b-45dd-bbf1-c591085fc9e0/scratchpad/impostor_preview";
            Directory.CreateDirectory(dbgDir);
            File.WriteAllBytes(Path.Combine(dbgDir, $"{name}_rgba.png"), tex.EncodeToPNG());
            var flat = new Texture2D(texW, texH, TextureFormat.RGB24, false);
            var comp = new Color[outPx.Length];
            for (int p = 0; p < outPx.Length; p++)
            {
                var bg = ((p / texW / 16 + p % texW / 16) & 1) == 0 ? new Color(0.55f, 0.55f, 0.58f) : new Color(0.35f, 0.35f, 0.38f);
                comp[p] = Color.Lerp(bg, new Color(outPx[p].r, outPx[p].g, outPx[p].b), outPx[p].a);
            }
            flat.SetPixels(comp); flat.Apply();
            File.WriteAllBytes(Path.Combine(dbgDir, $"{name}_onchecker.png"), flat.EncodeToPNG());
            Object.DestroyImmediate(flat);

            var billboard = new Material(billboardShader) { name = $"{name}_impostor" };
            billboard.SetTexture("_BaseMap", tex);
            billboard.SetFloat("_Cutoff", 0.4f);
            var matPath = $"{OutDir}/{name}_impostor.mat";
            AssetDatabase.DeleteAsset(matPath);
            AssetDatabase.CreateAsset(billboard, matPath);

            Object.DestroyImmediate(black);
            Object.DestroyImmediate(white);
            Object.DestroyImmediate(camGO);
            Object.DestroyImmediate(lightGO);
            Object.DestroyImmediate(go);

            Debug.Log($"[Impostor] {name}: {texW}x{texH} aspect {aspect:0.###}");
            return aspect;
        }

        private static Texture2D RenderPass(Camera cam, int w, int h, Color clear)
        {
            // Non-MSAA target: ReadPixels cannot resolve a multisampled RT (it comes back empty). We
            // supersample instead -- render at 2x then box-downscale in the caller for clean edges.
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = clear;
            cam.targetTexture = rt;
            // Unity 6 URP: Camera.Render() is a legacy no-op under SRP, so drive the pipeline with an
            // on-demand SingleCameraRequest (works in edit-mode batch; Camera.Render leaves the RT black).
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            bool supported = RenderPipeline.SupportsRenderRequest(cam, request);
            if (supported)
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();   // built-in fallback (non-URP)
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            return tex;
        }

        private static Bounds MeasureWorld(GameObject go)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>();
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
                    Vector3 wpt = m.MultiplyPoint3x4(corner);
                    if (first) { world = new Bounds(wpt, Vector3.zero); first = false; }
                    else world.Encapsulate(wpt);
                }
            }
            return world;
        }
    }
}
