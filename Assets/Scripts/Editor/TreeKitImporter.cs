using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>
    /// Sets up materials for the CC0 Poly Haven forest kit (Assets/Models/Trees/&lt;asset&gt;/). These are
    /// realistic photoscans with SEPARATE leaf-diffuse + leaf-alpha jpgs and opaque bark/branch diffuse.
    /// We combine leaf diff+alpha into one RGBA cutout texture and build a 2-sided alpha-clip leaf material
    /// (the real game-tree technique) + an opaque bark material, then assign them to the tree's submeshes
    /// by material name. See memory poly-haven-forest-kit.
    /// </summary>
    public static class TreeKitImporter
    {
        private const string Kit = "Assets/Models/Trees";

        // --- Single source of truth for what a curated asset IS. The whole class of regressions (foliage
        //     scaled tree-huge, rocks thinned to crumpled paper, plants flat-tinted) came from code not
        //     knowing the asset type. Classify once; drive size clamps, decimation, and collision from it. ---
        public enum KitType { Tree, DeadTree, Plant, Flower, Rock, Stump, Unknown }

        public static KitType Classify(string name)
        {
            string n = name.ToLower();
            if (n.Contains("dead_tree") || n.Contains("dead_quiver")) return KitType.DeadTree;
            if (n.Contains("tree") || n.Contains("jacaranda") || n.Contains("fir_")) return KitType.Tree;
            if (n.Contains("stump") || n.Contains("root")) return KitType.Stump;
            if (n.Contains("boulder") || n.Contains("rock") || n.Contains("moss_") || n.Contains("cliff")) return KitType.Rock;
            if (n.Contains("flower") || n.Contains("dandelion") || n.Contains("celandine") || n.Contains("gazania")
                || n.Contains("anthurium") || n.Contains("calathea")) return KitType.Flower;
            if (n.Contains("shrub") || n.Contains("fern") || n.Contains("bush") || n.Contains("grass")) return KitType.Plant;
            return KitType.Unknown;
        }

        /// <summary>Sane world-height clamp (metres) per asset type, so a stray scatter call can never make
        /// a giant fern or a shrunk tree. Unknown props are left to the caller.</summary>
        public static (float min, float max) HeightRange(KitType t)
        {
            switch (t)
            {
                case KitType.Tree: return (6f, 22f);
                case KitType.DeadTree: return (3.5f, 16f);
                case KitType.Rock: return (0.4f, 6f);
                case KitType.Stump: return (0.4f, 2.5f);
                case KitType.Plant: return (0.25f, 1.3f);
                case KitType.Flower: return (0.2f, 0.7f);
                default: return (0.05f, 100f);
            }
        }

        public static bool IsRockLike(string name) { var t = Classify(name); return t == KitType.Rock || t == KitType.Stump; }

        [MenuItem("AldenFall/Render/Setup + render curated trees")]
        public static void SetupAndRenderCuratedTrees()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-treenames");
            string[] names = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : new[] { "island_tree_01" };
            string outDir = "/private/tmp/claude-501/-Users-jon-Developer-AldenFall/ef2dc15e-0a6b-45dd-bbf1-c591085fc9e0/scratchpad/prop_beauty";
            Directory.CreateDirectory(outDir);

            foreach (var name in names)
            {
                var mats = BuildMaterials(name);
                if (mats == null) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/{name}/{name}.fbx");
                if (asset == null) { Debug.LogWarning($"[TreeKit] no fbx {name}"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                AssignMaterials(go, mats.Value.leaf, mats.Value.bark);
                RenderBeauty(go, name, outDir);
                Object.DestroyImmediate(go);
            }
        }

        // Canopy/dead trees baked into LOD prefabs. (jacaranda 3.6M + fir 237MB dropped from this pass --
        // too slow to decimate; add later once the base forest ships.)
        private static readonly string[] TreeSpecies =
        {
            "island_tree_01", "island_tree_02", "island_tree_03", "tree_small_02",
            "dead_tree_trunk", "dead_tree_trunk_02",
            // undergrowth: bushes/ferns/flowers (alpha-cut) + rocks/stumps (opaque)
            "shrub_01", "shrub_02", "shrub_03", "shrub_04", "fern_02",
            "flower_gazania", "dandelion_01", "celandine_01",
            "boulder_01", "namaqualand_boulder_02", "namaqualand_boulder_03", "namaqualand_boulder_04",
            "rock_moss_set_01", "tree_stump_01", "tree_stump_02",
        };
        private static readonly float[] LodThresholds = { 0.10f, 0.035f, 0.010f };
        // Fraction of LEAF / BRANCH triangles kept per tier. These photoscans are disconnected geometry
        // (leaf cards AND twig bits), so QEM floors -- we THIN triangles instead to differentiate LODs.
        // LOD0 keeps branches whole; far tiers thin both so a distant tree is genuinely cheap.
        private static readonly float[] LeafKeep = { 0.9f, 0.4f, 0.15f };
        private static readonly float[] BranchKeep = { 1.0f, 0.4f, 0.15f };
        public const string PrefabDir = "Assets/Prefabs/Trees";

        [MenuItem("AldenFall/Render/Bake tree-kit LOD prefabs")]
        public static void BakeTreeKitPrefabs()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Trees");

            foreach (var name in TreeSpecies)
            {
                var mats = BuildMaterials(name);
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/{name}/{name}.fbx");
                if (mats == null || fbx == null) { Debug.LogWarning($"[TreeKit] skip {name}"); continue; }

                var src = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                // Do NOT reset the FBX root transform -- Poly Haven FBX carry a unit-conversion scale on
                // the root; zeroing it reassembles the parts at raw (100x) scale (giant, splayed trees).
                // Leave the instance as imported; copy each part's real world transform below.
                AssignMaterials(src, mats.Value.leaf, mats.Value.bark);
                var filters = src.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).ToArray();

                // Rocks/stumps are SOLID connected meshes -- QEM decimates them cleanly per tier. Foliage
                // (trees/plants) is disconnected leaf cards -- QEM floors, so we thin triangles instead.
                // Deleting triangles from a rock (the old path) makes crumpled-paper geometry -- don't.
                bool isRock = IsRockLike(name);   // solid mesh -> clean QEM; foliage -> thin (see Classify)
                float[] rockQ = { 0.5f, 0.2f, 0.08f };

                // Foliage base: QEM once (branches reduced, leaves floor); thinned per tier below.
                // Rocks: keep the SOURCE mesh and QEM it per tier.
                var baseData = new System.Collections.Generic.List<(Mesh mesh, Material[] mats, Transform t)>();
                foreach (var mf in filters)
                {
                    Mesh baseMesh;
                    if (isRock) baseMesh = mf.sharedMesh;
                    else
                    {
                        var ms = new UnityMeshSimplifier.MeshSimplifier { PreserveBorderEdges = false, PreserveSurfaceCurvature = true };
                        ms.Initialize(mf.sharedMesh);
                        ms.SimplifyMesh(0.3f);
                        baseMesh = ms.ToMesh(); baseMesh.RecalculateBounds();
                    }
                    baseData.Add((baseMesh, mf.GetComponent<MeshRenderer>().sharedMaterials, mf.transform));
                }

                var root = new GameObject(name);
                var lods = new LOD[LodThresholds.Length];
                for (int t = 0; t < LodThresholds.Length; t++)
                {
                    var tierGO = new GameObject($"LOD{t}");
                    tierGO.transform.SetParent(root.transform, false);
                    var rends = new System.Collections.Generic.List<Renderer>();
                    int idx = 0;
                    foreach (var bd in baseData)
                    {
                        Mesh tierMesh;
                        if (isRock)
                        {
                            var ms = new UnityMeshSimplifier.MeshSimplifier { PreserveBorderEdges = true, PreserveSurfaceCurvature = true };
                            ms.Initialize(bd.mesh);
                            ms.SimplifyMesh(rockQ[t]);
                            tierMesh = ms.ToMesh(); tierMesh.name = $"{name}_LOD{t}_{idx}"; tierMesh.RecalculateBounds();
                        }
                        else tierMesh = ThinLeaves(bd.mesh, bd.mats, LeafKeep[t], BranchKeep[t], $"{name}_LOD{t}_{idx}");
                        AssetDatabase.CreateAsset(tierMesh, $"{PrefabDir}/{name}_LOD{t}_{idx}.mesh");
                        var g = new GameObject($"part{idx}");
                        g.transform.SetParent(tierGO.transform, false);
                        g.transform.position = bd.t.position;
                        g.transform.rotation = bd.t.rotation;
                        g.transform.localScale = bd.t.lossyScale;
                        g.AddComponent<MeshFilter>().sharedMesh = tierMesh;
                        g.AddComponent<MeshRenderer>().sharedMaterials = bd.mats;
                        rends.Add(g.GetComponent<MeshRenderer>());
                        idx++;
                    }
                    lods[t] = new LOD(LodThresholds[t], rends.ToArray());
                    int tris = rends.Sum(r => { var m = r.GetComponent<MeshFilter>().sharedMesh; int s = 0; for (int i = 0; i < m.subMeshCount; i++) s += (int)m.GetIndexCount(i); return s / 3; });
                    Debug.Log($"[TreeKit] {name} LOD{t}: {tris} tris");
                }
                var lg = root.AddComponent<LODGroup>();
                lg.fadeMode = LODFadeMode.CrossFade;
                lg.animateCrossFading = true;
                lg.SetLODs(lods);
                lg.RecalculateBounds();

                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{name}.prefab");
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(src);
                Debug.Log($"[TreeKit] baked prefab {name}");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Return a copy of baseMesh with each LEAF submesh's quads thinned to `keep` fraction
        /// (whole 2-triangle cards kept, so leaves stay intact -- just fewer of them at distance).</summary>
        private static Mesh ThinLeaves(Mesh baseMesh, Material[] mats, float leafKeep, float branchKeep, string outName)
        {
            var m = Object.Instantiate(baseMesh);
            m.name = outName;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                bool isLeaf = s < mats.Length && mats[s] != null && mats[s].name.ToLower().Contains("leaf");
                float keep = isLeaf ? leafKeep : branchKeep;
                if (keep >= 0.999f) continue;
                var tris = m.GetTriangles(s);
                var kept = new System.Collections.Generic.List<int>(tris.Length);
                float acc = 0f;
                // Keep whole quads (6 indices) when the submesh is quad-aligned; else fall back per-tri.
                int step = (tris.Length % 6 == 0) ? 6 : 3;
                for (int i = 0; i + step - 1 < tris.Length; i += step)
                {
                    acc += keep;
                    if (acc >= 1f) { acc -= 1f; for (int k = 0; k < step; k++) kept.Add(tris[i + k]); }
                }
                m.SetTriangles(kept, s);
            }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Build (leaf cutout, bark opaque) materials for a tree; combines diff+alpha once, cached.</summary>
        public static (Material leaf, Material bark)? BuildMaterials(string name)
        {
            var dir = $"{Kit}/{name}";
            if (!AssetDatabase.IsValidFolder(dir)) { Debug.LogWarning($"[TreeKit] no folder {name}"); return null; }

            string leafDiff = Tex(dir, "leaves_diff", "twig_diff", "leaf_diff");       // tree leaf albedo
            string leafAlpha = Tex(dir, "leaves_alpha", "twig_alpha", "leaf_alpha", "leaves_mask", "twig_mask", "_alpha_");
            string barkDiff = Tex(dir, "branches_diff", "bark_diff", "trunk_a_diff", "trunk_diff");  // tree bark
            string genDiff = Tex(dir, "_diff_", "diffuse");                            // plant/rock albedo

            var lit = Shader.Find("Universal Render Pipeline/Lit");

            // LEAF / foliage cutout material -- built whenever there's an alpha map (tree leaves OR a whole
            // alpha-cut plant like a shrub/fern/flower). Uses the tree-leaf albedo if present, else the
            // general Diffuse.
            Material leaf = null;
            string diffForLeaf = leafDiff ?? genDiff;
            if (leafAlpha != null && diffForLeaf != null)
            {
                var combined = CombineDiffAlpha(name, diffForLeaf, leafAlpha);
                leaf = new Material(lit) { name = $"{name}_leaf" };
                leaf.SetTexture("_BaseMap", combined);
                leaf.SetFloat("_Smoothness", 0.12f);
                leaf.SetFloat("_AlphaClip", 1f);
                leaf.SetFloat("_Cutoff", 0.45f);
                leaf.EnableKeyword("_ALPHATEST_ON");
                leaf.SetFloat("_Cull", (float)CullMode.Off);   // 2-sided
                leaf.renderQueue = (int)RenderQueue.AlphaTest;
                SaveMat(leaf, $"{dir}/{name}_leaf.mat");
            }

            // OPAQUE material -- tree bark, or a rock/stump's Diffuse.
            Material bark = null;
            string opaqueDiff = barkDiff ?? (leafAlpha == null ? genDiff : null);
            if (opaqueDiff != null)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(opaqueDiff);
                bark = new Material(lit) { name = $"{name}_bark" };
                bark.SetTexture("_BaseMap", tex);
                bark.SetFloat("_Smoothness", 0.05f);
                SaveMat(bark, $"{dir}/{name}_bark.mat");
            }
            // Whole-plant cutout (shrub/fern/flower, no separate bark): every submesh uses the leaf cutout.
            if (bark == null && leaf != null) bark = leaf;
            // Dead trees / props with no diffuse at all: flat brown so they still bake.
            if (bark == null && leaf == null)
            {
                bark = new Material(lit) { name = $"{name}_bark" };
                bark.SetColor("_BaseColor", new Color(0.30f, 0.24f, 0.19f));
                bark.SetFloat("_Smoothness", 0.05f);
                SaveMat(bark, $"{dir}/{name}_bark.mat");
            }
            if (leaf == null) leaf = bark;
            return leaf == null ? null : (leaf, bark);
        }

        private static void AssignMaterials(GameObject go, Material leaf, Material bark)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                var shared = r.sharedMaterials;
                for (int s = 0; s < shared.Length; s++)
                {
                    string n = (shared[s] != null ? shared[s].name : "").ToLower();
                    bool isLeaf = n.Contains("leaf") || n.Contains("leaves") || n.Contains("twig") || n.Contains("foliage");
                    shared[s] = isLeaf ? leaf : bark;
                }
                r.sharedMaterials = shared;
            }
        }

        // --- helpers ---
        private static string Tex(string dir, params string[] keys)
        {
            foreach (var k in keys)
            {
                var hit = Directory.GetFiles(dir, "*.jpg").Concat(Directory.GetFiles(dir, "*.png"))
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLower().Contains(k));
                if (hit != null) return hit.Replace('\\', '/');
            }
            return null;
        }

        private static Texture2D LoadReadable(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        private static Texture2D CombineDiffAlpha(string name, string diffPath, string alphaPath)
        {
            var outPath = $"{Kit}/{name}/{name}_leafRGBA.png";
            var d = LoadReadable(diffPath);
            var px = d.GetPixels();
            if (alphaPath != null)
            {
                var a = LoadReadable(alphaPath);
                var ap = a.width == d.width && a.height == d.height ? a.GetPixels() : null;
                if (ap != null) for (int i = 0; i < px.Length; i++) px[i].a = ap[i].r;
            }
            var outT = new Texture2D(d.width, d.height, TextureFormat.RGBA32, true);
            outT.SetPixels(px); outT.Apply(true);
            File.WriteAllBytes(outPath, outT.EncodeToPNG());
            AssetDatabase.ImportAsset(outPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
            if (imp != null) { imp.alphaIsTransparency = true; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        private static void SaveMat(Material m, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
        }

        private static void RenderBeauty(GameObject go, string name, string outDir)
        {
            go.transform.position = Vector3.zero;
            Bounds b = Measure(go);
            float h = Mathf.Max(0.01f, b.size.y);
            go.transform.localScale *= 5f / h;
            b = Measure(go);
            go.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = Measure(go);

            var camGO = new GameObject("BeautyCam");
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
            cam.fieldOfView = 32f;
            float dist = b.size.magnitude * 1.4f;
            cam.transform.position = b.center + new Vector3(dist * 0.7f, b.size.y * 0.18f, -dist * 0.95f);
            cam.transform.LookAt(b.center);
            cam.nearClipPlane = 0.05f; cam.farClipPlane = dist * 6f;

            var lightGO = new GameObject("BeautyLight");
            var light = lightGO.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.35f; lightGO.transform.rotation = Quaternion.Euler(38f, 205f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.57f, 0.62f);

            int W = 640, H = 820;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); rt.Create();
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; rt.Release();
            File.WriteAllBytes($"{outDir}/{name}.png", tex.EncodeToPNG());
            int verts = go.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh ? f.sharedMesh.vertexCount : 0);
            Debug.Log($"[TreeKit] {name}: verts={verts} -> {outDir}/{name}.png");
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGO); Object.DestroyImmediate(lightGO);
        }

        private static Bounds Measure(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
