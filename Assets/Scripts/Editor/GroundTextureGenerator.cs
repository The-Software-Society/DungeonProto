#if UNITY_EDITOR
using System.IO;
using UnityEngine;
using UnityEditor;

namespace AldenFall.Editor
{
    /// <summary>
    /// Procedurally generates tileable ground albedo textures (grass, dirt) and assigns them
    /// to the existing ground material assets, so the world stops reading as flat-colour tiles.
    /// No scene rebuild needed - the tiles reference the shared .mat assets, so editing the
    /// materials updates every tile. Run via the AldenFall/World menu or -executeMethod.
    /// </summary>
    public static class GroundTextureGenerator
    {
        private const string TexDir = "Assets/Textures/Ground";
        private const string MatDir = "Assets/Materials/Emberfall";
        private const int Size = 256;

        [MenuItem("AldenFall/World/Regenerate Ground Textures", priority = 213)]
        public static void GenerateAndApply()
        {
            Directory.CreateDirectory(TexDir);
            var grass = Save("grass_albedo", Grass(Size));
            var dirt = Save("dirt_albedo", Dirt(Size));
            AssetDatabase.Refresh();
            grass = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/grass_albedo.png");
            dirt = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/dirt_albedo.png");

            // Grass tiles (EmberGrass0..3) get the grass texture; lake sand + the browner
            // ember-gradient buckets get dirt. Tiling repeats the texture across each 12m tile.
            for (int i = 0; i < 4; i++) ApplyTex($"EmberGrass{i}", grass, 3f, new Color(0.85f, 0.9f, 0.82f));
            ApplyTex("LakeSand", dirt, 3f, new Color(0.95f, 0.9f, 0.82f));
            ApplyTex("GrassTuft", grass, 2f, new Color(0.8f, 0.88f, 0.78f));
            AssetDatabase.SaveAssets();
            Debug.Log("GroundTextureGenerator: grass + dirt textures generated and applied to ground materials.");
        }

        private static void ApplyTex(string matName, Texture2D tex, float tiling, Color tint)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{matName}.mat");
            if (mat == null || tex == null) return;
            if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling)); }
            if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", new Vector2(tiling, tiling)); }
            // Tint near-white so the texture's own colour reads (BaseColor multiplies BaseMap).
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            mat.color = tint;
            EditorUtility.SetDirty(mat);
        }

        // ---- procedural tileable textures ----

        /// <summary>Tileable value noise in [0,1] via wrap-blend of four Perlin samples.</summary>
        private static float TileNoise(float u, float v, float freq)
        {
            float x = u * freq, y = v * freq;
            float a = Mathf.PerlinNoise(x, y);
            float b = Mathf.PerlinNoise(x - freq, y);
            float c = Mathf.PerlinNoise(x, y - freq);
            float d = Mathf.PerlinNoise(x - freq, y - freq);
            return a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
        }

        private static float Fbm(float u, float v, float baseFreq)
        {
            return TileNoise(u, v, baseFreq) * 0.6f
                 + TileNoise(u, v, baseFreq * 2f) * 0.3f
                 + TileNoise(u, v, baseFreq * 4f) * 0.1f;
        }

        public static Color32[] Grass(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.14f, 0.28f, 0.10f);
            Color mid = new Color(0.22f, 0.40f, 0.15f);
            Color light = new Color(0.34f, 0.52f, 0.22f);
            Color dry = new Color(0.44f, 0.46f, 0.22f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 6f);
                    float blades = Fbm(u, v, 22f);      // fine blade streaks
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.5f));
                    c = Color.Lerp(c, light, Mathf.Clamp01((blades - 0.45f) * 2.4f));
                    if (blades > 0.72f) c = Color.Lerp(c, dry, (blades - 0.72f) * 2.2f);  // dry tufts
                    px[y * s + x] = c;
                }
            return px;
        }

        public static Color32[] Sand(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.42f, 0.37f, 0.27f);
            Color mid = new Color(0.55f, 0.49f, 0.36f);
            Color light = new Color(0.66f, 0.60f, 0.46f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 6f);
                    float grit = Fbm(u, v, 34f);
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.4f));
                    c = Color.Lerp(c, light, Mathf.Clamp01((grit - 0.5f) * 1.8f));
                    px[y * s + x] = c;
                }
            return px;
        }

        public static Color32[] Soot(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.08f, 0.065f, 0.06f);
            Color mid = new Color(0.14f, 0.11f, 0.10f);
            Color ember = new Color(0.35f, 0.12f, 0.05f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 8f);
                    float cracks = Fbm(u, v, 24f);
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.5f));
                    if (cracks > 0.74f) c = Color.Lerp(c, ember, (cracks - 0.74f) * 2.5f);   // glowing embers in cracks
                    px[y * s + x] = c;
                }
            return px;
        }

        public static Color32[] Dirt(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.24f, 0.17f, 0.11f);
            Color mid = new Color(0.36f, 0.27f, 0.18f);
            Color light = new Color(0.48f, 0.38f, 0.27f);
            Color stone = new Color(0.55f, 0.52f, 0.47f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 7f);
                    float grit = Fbm(u, v, 30f);
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.4f));
                    c = Color.Lerp(c, light, Mathf.Clamp01((grit - 0.5f) * 2f));
                    if (grit > 0.78f) c = Color.Lerp(c, stone, (grit - 0.78f) * 3f);  // pebbles
                    px[y * s + x] = c;
                }
            return px;
        }

        private static Texture2D Save(string name, Color32[] px)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes($"{TexDir}/{name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return null;
        }
    }
}
#endif
