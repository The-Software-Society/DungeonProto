#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AldenFall.Editor
{
    /// <summary>
    /// Dense DETAIL GRASS + wildflowers, the way real open-world games do it: crossed alpha-cut
    /// quads textured with a grass-tuft cutout, placed thickly across the walkable ground so the
    /// world reads as a living grass field instead of textured cube tiles. Tunable consts up top;
    /// re-run from the AldenFall/World menu (or -executeMethod) and screenshot to iterate.
    /// </summary>
    public static class GrassFieldBuilder
    {
        // ---- tuning ----
        private const float AreaMinX = -140f, AreaMaxX = 140f;
        private const float AreaMinZ = -40f, AreaMaxZ = 215f;
        private const float Spacing = 0.9f;      // grid step (smaller = denser, heavier)
        private const float Jitter = 0.7f;       // random offset within a cell
        private const float BladeMinH = 0.4f, BladeMaxH = 1.05f;
        private const float BladeW = 0.55f;      // tuft width
        private const float FlowerChance = 0.035f;
        private const string TexDir = "Assets/Textures/Ground";

        [MenuItem("AldenFall/World/Rebuild Detail Grass", priority = 215)]
        public static void RebuildDetailGrass()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity", OpenSceneMode.Single);

            // Remove the old solid-triangle grass fields + any prior detail grass.
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var n = mf.gameObject.name;
                if (n.Contains("GrassBlade") || n == "GladeGrass" || n == "EmberGrassBlades"
                    || n.StartsWith("DetailGrass") || n.StartsWith("Wildflowers"))
                    Object.DestroyImmediate(mf.gameObject);
            }

            var grassTex = MakeTex("grass_tuft_alpha", GrassTuftAlpha(128, 128));
            var grassMat = CutoutMat("M_DetailGrass", grassTex, 0.3f);
            var flowerMats = new[]
            {
                CutoutMat("M_FlowerPink",   MakeTex("flower_pink",   FlowerAlpha(96, new Color(0.85f, 0.45f, 0.72f))), 0.4f),
                CutoutMat("M_FlowerYellow", MakeTex("flower_yellow", FlowerAlpha(96, new Color(0.95f, 0.82f, 0.30f))), 0.4f),
                CutoutMat("M_FlowerRed",    MakeTex("flower_red",    FlowerAlpha(96, new Color(0.85f, 0.28f, 0.24f))), 0.4f),
                CutoutMat("M_FlowerWhite",  MakeTex("flower_white",  FlowerAlpha(96, new Color(0.92f, 0.92f, 0.86f))), 0.4f),
            };

            int groundLayer = LayerMask.NameToLayer("Ground");
            Physics.SyncTransforms();

            var gv = new List<Vector3>(); var gt = new List<int>(); var gu = new List<Vector2>(); var gn = new List<Vector3>();
            var flowerVerts = new Dictionary<int, (List<Vector3> v, List<int> t, List<Vector2> u, List<Vector3> n)>();
            for (int f = 0; f < flowerMats.Length; f++) flowerVerts[f] = (new List<Vector3>(), new List<int>(), new List<Vector2>(), new List<Vector3>());

            var rng = new System.Random(12345);
            int grassCount = 0, flowerCount = 0;

            for (float x = AreaMinX; x < AreaMaxX; x += Spacing)
                for (float z = AreaMinZ; z < AreaMaxZ; z += Spacing)
                {
                    float px = x + (float)(rng.NextDouble() - 0.5) * Jitter;
                    float pz = z + (float)(rng.NextDouble() - 0.5) * Jitter;
                    if (!GrassAllowed(px, pz)) continue;

                    // Sample ground height by raycast so grass sits on the surface.
                    if (!Physics.Raycast(new Vector3(px, 20f, pz), Vector3.down, out var hit, 60f,
                            groundLayer >= 0 ? (1 << groundLayer) : ~0))
                        continue;
                    float gyTop = hit.point.y;

                    if (rng.NextDouble() < FlowerChance)
                    {
                        int fm = rng.Next(flowerMats.Length);
                        var b = flowerVerts[fm];
                        float fh = 0.5f + (float)rng.NextDouble() * 0.35f;
                        AddCrossQuad(b.v, b.t, b.u, b.n, new Vector3(px, gyTop, pz), fh, 0.34f, (float)rng.NextDouble() * 6.28f);
                        flowerCount++;
                    }
                    else
                    {
                        float h = BladeMinH + (float)rng.NextDouble() * (BladeMaxH - BladeMinH);
                        AddCrossQuad(gv, gt, gu, gn, new Vector3(px, gyTop, pz), h, BladeW, (float)rng.NextDouble() * 6.28f);
                        grassCount++;
                    }
                }

            BuildMeshObject("DetailGrass", gv, gt, gu, gn, grassMat, groundLayer);
            for (int f = 0; f < flowerMats.Length; f++)
            {
                var b = flowerVerts[f];
                if (b.v.Count > 0) BuildMeshObject($"Wildflowers_{f}", b.v, b.t, b.u, b.n, flowerMats[f], groundLayer);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"GrassFieldBuilder: placed {grassCount} grass tufts + {flowerCount} wildflowers.");
        }

        // Skip city footprint, the lake, and the deep-soot approach (kept sparse/dead there).
        private static bool GrassAllowed(float x, float z)
        {
            if (Mathf.Abs(x) < 50f && Mathf.Abs(z) < 50f) return false;            // city cobbles
            if (Mathf.Abs(x + 138f) < 60f && Mathf.Abs(z - 138f) < 60f) return false; // lake basin
            if (Mathf.Sqrt(x * x + z * z) > 250f) return false;                    // outside the ring
            if (z < -20f) return false;                                            // soot/ember approach stays barren
            return true;
        }

        // Two vertical quads crossed at 90 degrees (double-sided via Cull Off), normals up so they
        // light bright like ground cover. Anchored at the ground point, splayed slightly.
        private static void AddCrossQuad(List<Vector3> v, List<int> t, List<Vector2> u, List<Vector3> n,
            Vector3 baseP, float height, float width, float yaw)
        {
            for (int q = 0; q < 2; q++)
            {
                float a = yaw + q * Mathf.PI * 0.5f;
                Vector3 right = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (width * 0.5f);
                Vector3 top = Vector3.up * height;
                int s = v.Count;
                v.Add(baseP - right); v.Add(baseP + right);
                v.Add(baseP + right + top); v.Add(baseP - right + top);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0)); u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) n.Add(Vector3.up);
                t.Add(s + 0); t.Add(s + 2); t.Add(s + 1);
                t.Add(s + 0); t.Add(s + 3); t.Add(s + 2);
            }
        }

        private static void BuildMeshObject(string name, List<Vector3> v, List<int> t, List<Vector2> u, List<Vector3> n,
            Material mat, int layer)
        {
            var obj = new GameObject(name); obj.isStatic = true;
            if (layer >= 0) obj.layer = layer;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.SetUVs(0, u); mesh.SetNormals(n);
            mesh.RecalculateBounds();
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ---- procedural cutout textures ----

        private static Material CutoutMat(string name, Texture2D tex, float cutoff)
        {
            string path = $"Assets/Materials/Emberfall/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cutoff", cutoff);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);   // double-sided
            mat.renderQueue = 2450;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D MakeTex(string name, Color32[] px)
        {
            int side = (int)Mathf.Sqrt(px.Length);
            Directory.CreateDirectory(TexDir);
            var t = new Texture2D(side, side, TextureFormat.RGBA32, true);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes($"{TexDir}/{name}.png", t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset($"{TexDir}/{name}.png");
            var imp = (TextureImporter)AssetImporter.GetAtPath($"{TexDir}/{name}.png");
            if (imp != null) { imp.alphaIsTransparency = true; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");
        }

        private static Color32[] GrassTuftAlpha(int w, int h)
        {
            var px = new Color32[w * h];
            var rng = new System.Random(7);
            int blades = 9;
            for (int i = 0; i < blades; i++)
            {
                float baseX = (i + 0.5f) / blades * w + (float)(rng.NextDouble() - 0.5) * (w / blades);
                float bladeH = h * (0.55f + (float)rng.NextDouble() * 0.45f);
                float curve = (float)(rng.NextDouble() - 0.5) * w * 0.35f;
                float thick = w * 0.018f + (float)rng.NextDouble() * w * 0.012f;
                for (int y = 0; y < bladeH; y++)
                {
                    float t = y / bladeH;
                    float cx = baseX + curve * t * t;
                    float tw = thick * (1f - t * 0.85f);       // taper to the tip
                    // green gradient: dark at root, bright at tip
                    Color col = Color.Lerp(new Color(0.10f, 0.24f, 0.07f), new Color(0.42f, 0.62f, 0.24f), t);
                    for (int dx = -(int)tw; dx <= (int)tw; dx++)
                    {
                        int xx = Mathf.RoundToInt(cx) + dx;
                        if (xx < 0 || xx >= w || y >= h) continue;
                        px[y * w + xx] = col;
                    }
                }
            }
            return px;
        }

        private static Color32[] FlowerAlpha(int s, Color bloom)
        {
            var px = new Color32[s * s];
            var stem = new Color(0.16f, 0.30f, 0.12f);
            int cx = s / 2;
            // stem
            for (int y = 0; y < s * 0.62f; y++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = cx + dx; if (xx < 0 || xx >= s) continue;
                    px[y * s + xx] = stem;
                }
            // bloom: 5 petals (small discs) around a center dot, so it reads as a flower not an egg
            Vector2 c = new Vector2(cx, s * 0.74f);
            float petalR = s * 0.11f, ringR = s * 0.11f;
            var centres = new List<Vector2> { c };
            for (int p = 0; p < 5; p++)
            {
                float a = p / 5f * Mathf.PI * 2f + 0.3f;
                centres.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ringR);
            }
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var p = new Vector2(x, y);
                    for (int i = 1; i < centres.Count; i++)      // petals
                        if (Vector2.Distance(p, centres[i]) < petalR) { px[y * s + x] = bloom; break; }
                    if (Vector2.Distance(p, c) < petalR * 0.55f)  // bright center
                        px[y * s + x] = Color.Lerp(new Color(0.98f, 0.86f, 0.35f), Color.white, 0.2f);
                }
            return px;
        }
    }
}
#endif
