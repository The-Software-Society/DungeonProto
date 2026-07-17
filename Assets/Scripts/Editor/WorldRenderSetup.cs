using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>
    /// Unity 6 GPU-driven rendering setup: Mesh LOD (engine-generated decimated geometry, GPU-selected
    /// per instance) + GPU Resident Drawer (instancing + GPU occlusion culling). This is the "many
    /// assets, real geometry, high fps" path big games use — real decimated meshes that thin with
    /// distance (NOT a flat card), drawn as instanced batches with hidden instances culled on the GPU.
    /// </summary>
    public static class WorldRenderSetup
    {
        private static readonly string[] LodProps =
        {
            "OakTree", "BirchTree", "DeadTree", "PineTree", "MossBoulder", "BoulderCluster",
        };

        // Proper decimated LOD chain: the raw Meshy tree meshes are ~600k verts (a stylized tree should
        // be a few thousand) -- the real root cause of the geometry-bound perf. Decimate each to three
        // real-geometry tiers with UnityMeshSimplifier (QEM), saved as .mesh assets. ArenaBuilder builds a
        // 3-LOD LODGroup from these (see AddPropLOD). This is the permanent pipeline baseline: real
        // geometry that thins with distance, works with the standard renderer (no flaky GPU-driven path).
        public const string TreeLodDir = "Assets/Materials/Emberfall/TreeLODs";
        private static readonly int[] LodVertTargets = { 12000, 4000, 1200 };

        [MenuItem("AldenFall/Render/Bake Decimated Tree LODs")]
        public static void BakeDecimatedLODs()
        {
            if (!AssetDatabase.IsValidFolder(TreeLodDir))
                AssetDatabase.CreateFolder("Assets/Materials/Emberfall", "TreeLODs");

            foreach (var name in LodProps)
            {
                var fbx = $"Assets/Models/Props/{name}.fbx";
                var meshes = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().ToList();
                if (meshes.Count == 0) { Debug.LogWarning($"[Decimate] no mesh {name}"); continue; }
                var src = meshes.OrderByDescending(m => m.vertexCount).First();
                int srcV = src.vertexCount;

                // Chain each tier from the PREVIOUS decimation, not the 615k source: far faster (one slow
                // pass, then cheap refinements) and yields a clean pyramid. PreserveBorderEdges OFF so it
                // can actually reach the target -- a tree is thousands of separate leaf cards (all border
                // edges), which otherwise floors decimation at ~23k and makes LOD1==LOD2.
                Mesh prev = src;
                for (int t = 0; t < LodVertTargets.Length; t++)
                {
                    float quality = Mathf.Clamp((float)LodVertTargets[t] / prev.vertexCount, 0.02f, 1f);
                    var ms = new UnityMeshSimplifier.MeshSimplifier();
                    ms.PreserveBorderEdges = false;
                    ms.PreserveSurfaceCurvature = true;
                    ms.Initialize(prev);
                    ms.SimplifyMesh(quality);
                    var dm = ms.ToMesh();
                    dm.name = $"{name}_LOD{t}";
                    dm.RecalculateBounds();
                    dm.RecalculateNormals();
                    var path = $"{TreeLodDir}/{name}_LOD{t}.mesh";
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(dm, path);
                    Debug.Log($"[Decimate] {name} LOD{t}: {prev.vertexCount} -> {dm.vertexCount} verts (q={quality:0.####})");
                    prev = dm;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Decimate] done -> " + TreeLodDir);
        }

        // Render a prop to a beauty PNG (3/4 view, lit) + log its vert count / texture so a regenerated
        // Meshy asset can be eyeballed and validated outside the editor. Names come from -propnames arg.
        [MenuItem("AldenFall/Render/Beauty-render props")]
        public static void RenderPropBeauty()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-propnames");
            string[] names = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : new[] { "OakTree" };
            string outDir = "/private/tmp/claude-501/-Users-jon-Developer-AldenFall/ef2dc15e-0a6b-45dd-bbf1-c591085fc9e0/scratchpad/prop_beauty";
            System.IO.Directory.CreateDirectory(outDir);

            foreach (var name in names)
            {
                // Props live at Models/Props/{name}.fbx; curated trees at Models/Trees/{name}/{name}.fbx.
                string propPath = $"Assets/Models/Props/{name}.fbx";
                string treePath = $"Assets/Models/Trees/{name}/{name}.fbx";
                bool isTree = AssetDatabase.LoadAssetAtPath<GameObject>(propPath) == null;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(isTree ? treePath : propPath);
                if (asset == null) { Debug.LogWarning($"[Beauty] no fbx {name}"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                bool textured;
                if (isTree)
                {
                    // Keep the FBX's own imported materials (curated trees ship their own PBR + alpha).
                    textured = go.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial != null && r.sharedMaterial.mainTexture != null);
                }
                else
                {
                    var mat = MeshyCharacterImporter.BuildTexturedMaterial(name, $"Assets/Models/Props/{name}_baseColor");
                    textured = mat != null && mat.mainTexture != null;
                    if (mat != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                }

                var mf = go.GetComponentsInChildren<MeshFilter>().OrderByDescending(f => f.sharedMesh ? f.sharedMesh.vertexCount : 0).FirstOrDefault();
                int verts = mf != null && mf.sharedMesh ? mf.sharedMesh.vertexCount : 0;

                // Normalise upright, height ~4, base at origin.
                go.transform.position = Vector3.zero;
                Bounds b = MeasureWorld(go);
                float h = Mathf.Max(0.01f, b.size.y);
                go.transform.localScale *= 4f / h;
                b = MeasureWorld(go);
                go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
                b = MeasureWorld(go);

                var camGO = new GameObject("BeautyCam");
                var cam = camGO.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.62f, 0.72f);
                cam.fieldOfView = 35f;
                float dist = b.size.magnitude * 1.5f;
                cam.transform.position = b.center + new Vector3(dist * 0.75f, b.size.y * 0.35f, -dist * 0.9f);
                cam.transform.LookAt(b.center + Vector3.up * b.size.y * 0.1f);
                cam.nearClipPlane = 0.05f; cam.farClipPlane = dist * 5f;

                var lightGO = new GameObject("BeautyLight");
                var light = lightGO.AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 1.25f; lightGO.transform.rotation = Quaternion.Euler(42f, 200f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.6f);

                int W = 640, H = 800;
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); rt.Create();
                cam.targetTexture = rt;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
                System.IO.File.WriteAllBytes($"{outDir}/{name}.png", tex.EncodeToPNG());

                Debug.Log($"[Beauty] {name}: verts={verts} textured={textured} -> {outDir}/{name}.png");
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                Object.DestroyImmediate(camGO); Object.DestroyImmediate(lightGO); Object.DestroyImmediate(go);
            }
        }

        [MenuItem("AldenFall/Render/Probe Mesh LOD API")]
        public static void ProbeMeshLodApi()
        {
            var path = "Assets/Models/Props/OakTree.fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { Debug.LogError("[Render] no ModelImporter for OakTree"); return; }

            // Log every serialized importer property whose name mentions LOD, so we set the right one.
            var so = new SerializedObject(importer);
            var it = so.GetIterator();
            while (it.NextVisible(true))
                if (it.name.ToLower().Contains("lod"))
                    Debug.Log($"[Render] importer prop: {it.propertyPath} ({it.propertyType}) = {PropVal(it)}");

            // Current mesh LOD count on the imported mesh.
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
            if (mesh != null)
            {
                var lodCountProp = typeof(Mesh).GetProperty("lodCount");
                Debug.Log($"[Render] OakTree mesh '{mesh.name}' verts={mesh.vertexCount} " +
                          $"lodCount={(lodCountProp != null ? lodCountProp.GetValue(mesh) : "N/A(no lodCount API)")}");
            }
            else Debug.Log("[Render] no Mesh found on OakTree.fbx");
        }

        [MenuItem("AldenFall/Render/Enable Mesh LODs on props")]
        public static void EnableMeshLods()
        {
            foreach (var name in LodProps)
            {
                var path = $"Assets/Models/Props/{name}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { Debug.LogWarning($"[Render] no importer {name}"); continue; }
                var so = new SerializedObject(importer);
                so.FindProperty("generateMeshLods").boolValue = true;
                so.FindProperty("maximumMeshLod").intValue = -1;   // generate the full pyramid
                so.ApplyModifiedProperties();
                importer.SaveAndReimport();

                var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
                var lodCountProp = typeof(Mesh).GetProperty("lodCount");
                object lc = mesh != null && lodCountProp != null ? lodCountProp.GetValue(mesh) : "?";
                Debug.Log($"[Render] {name}: verts={mesh?.vertexCount} lodCount={lc}");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AldenFall/Render/Disable Mesh LODs on props")]
        public static void DisableMeshLods()
        {
            foreach (var name in LodProps)
            {
                var path = $"Assets/Models/Props/{name}.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                var so = new SerializedObject(importer);
                so.FindProperty("generateMeshLods").boolValue = false;
                so.FindProperty("maximumMeshLod").intValue = -1;
                so.ApplyModifiedProperties();
                importer.SaveAndReimport();
                Debug.Log($"[Render] {name}: mesh LODs OFF");
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("AldenFall/Render/Configure GPU Resident Drawer")]
        public static void ConfigureGRD()
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            // GRD lives on the URP asset; set via SerializedObject (property names are internal).
            var so = new SerializedObject(urp);
            SetInt(so, "m_GPUResidentDrawerMode", 1);                          // InstancedDrawing
            SetInt(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", 1);
            SetFloat(so, "m_SmallMeshScreenPercentage", 0f);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);

            // GRD and static batching are mutually exclusive -- static-batched renderers are combined
            // and excluded from GRD instancing. Turn static batching OFF so GRD manages the props.
            SetPlayerStaticBatching(false);
            AssetDatabase.SaveAssets();
            Debug.Log("[Render] GRD enabled (InstancedDrawing + occlusion), static batching OFF");
        }

        private static void SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = v; else Debug.LogWarning($"[Render] missing {prop}");
        }
        private static void SetFloat(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.floatValue = v; else Debug.LogWarning($"[Render] missing {prop}");
        }
        private static void SetPlayerStaticBatching(bool on)
        {
            // PlayerSettings static batching via SerializedObject on the ProjectSettings asset.
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings == null || settings.Length == 0) { Debug.LogWarning("[Render] no ProjectSettings"); return; }
            var so = new SerializedObject(settings[0]);
            var sb = so.FindProperty("m_StaticBatching");
            if (sb != null) { sb.intValue = on ? 1 : 0; so.ApplyModifiedProperties(); }
            else Debug.LogWarning("[Render] m_StaticBatching not found");
        }

        private static Bounds MeasureWorld(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        private static string PropVal(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: return p.boolValue.ToString();
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
                default: return "?";
            }
        }
    }
}
