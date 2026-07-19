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

        public static Color32[] Grass(int s) => Grass(s,
            new Color(0.14f, 0.28f, 0.10f), new Color(0.22f, 0.40f, 0.15f),
            new Color(0.34f, 0.52f, 0.22f), new Color(0.44f, 0.46f, 0.22f));

        /// <summary>Parametrized grass albedo ramp (dark base -> mid -> light blades -> dry tufts) so
        /// the ground hue can be tuned/swept (ColorLab) instead of hard-coded here.</summary>
        public static Color32[] Grass(int s, Color dark, Color mid, Color light, Color dry)
        {
            var px = new Color32[s * s];
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

        public static Color32[] Rock(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.20f, 0.19f, 0.21f);
            Color mid = new Color(0.34f, 0.32f, 0.34f);
            Color light = new Color(0.5f, 0.49f, 0.5f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 5f);
                    float facet = Fbm(u, v, 16f);
                    float crack = Fbm(u, v, 40f);
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.4f));
                    c = Color.Lerp(c, light, Mathf.Clamp01((facet - 0.5f) * 1.6f));
                    if (crack < 0.28f) c = Color.Lerp(c, dark, (0.28f - crack) * 2.5f);   // dark fissures
                    px[y * s + x] = c;
                }
            return px;
        }

        public static Color32[] Soot(int s)
        {
            var px = new Color32[s * s];
            // Warm volcanic basalt/ash (not neutral black - dark neutral reads cold blue under
            // the bright sky ambient). Brighter mid + frequent glowing ember cracks so the
            // approach looks HOT and burnt, per Jon's "full blown dirt, lava, magma" note.
            Color dark = new Color(0.13f, 0.09f, 0.075f);
            Color mid = new Color(0.22f, 0.15f, 0.12f);
            Color ash = new Color(0.30f, 0.22f, 0.18f);
            Color ember = new Color(0.75f, 0.24f, 0.06f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 8f);
                    float ashN = Fbm(u, v, 16f);
                    float cracks = Fbm(u, v, 24f);
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.5f));
                    c = Color.Lerp(c, ash, Mathf.Clamp01((ashN - 0.55f) * 2.0f));                 // scattered dry ash
                    if (cracks > 0.68f) c = Color.Lerp(c, ember, (cracks - 0.68f) * 2.6f);        // glowing ember cracks
                    px[y * s + x] = c;
                }
            return px;
        }

        // Real packed-earth road dirt (ref: New World / stylized ARPG trails): rich brown with FINE
        // high-frequency mottle + small EARTH-TONED pebble specks. IMPORTANT: no low-frequency
        // "damp patch" here - a big soft blob baked into a tiling texture repeats identically every
        // tile and reads as "repeat squares with a black splotch" (Jon). Damp/erosion variation is
        // done at WORLD scale instead (road mud decals), never baked into the tile.
        public static Color32[] Dirt(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.27f, 0.20f, 0.125f);
            Color mid = new Color(0.37f, 0.285f, 0.185f);
            Color light = new Color(0.45f, 0.36f, 0.245f);  // dry dusty
            Color pebble = new Color(0.33f, 0.28f, 0.21f);  // earth pebble (warm, not grey/white)
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float n = Fbm(u, v, 9f);          // finer base mottle (less blobby -> tiles cleaner)
                    float grit = Fbm(u, v, 40f);       // fine grit speckle
                    Color c = Color.Lerp(dark, mid, Mathf.Clamp01(n * 1.5f));
                    c = Color.Lerp(c, light, Mathf.Clamp01((grit - 0.55f) * 1.8f));    // dry speckle
                    if (grit > 0.86f) c = Color.Lerp(c, pebble, (grit - 0.86f) * 3f);  // earth pebbles
                    px[y * s + x] = c;
                }
            return px;
        }

        // Cobblestone paving for city-entrance transitions: rounded grey stones packed with dark
        // earth in the gaps (jittered grid -> irregular cobbles, mortar lines darkened).
        public static Color32[] Cobble(int s)
        {
            var px = new Color32[s * s];
            Color mortar = new Color(0.17f, 0.15f, 0.12f);   // dark earth between stones
            Color stoneD = new Color(0.33f, 0.32f, 0.31f);
            Color stoneL = new Color(0.55f, 0.53f, 0.50f);
            const float cell = 8f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    // Jitter each grid cell's centre so cobbles read irregular, then measure the
                    // radial falloff from the cell centre for a domed-stone + mortar-gap look.
                    float cu = u * cell, cv = v * cell;
                    float gu = Mathf.Floor(cu), gv = Mathf.Floor(cv);
                    float jx = Mathf.PerlinNoise(gu * 1.7f, gv * 2.3f) * 0.5f;
                    float jy = Mathf.PerlinNoise(gu * 2.9f + 5f, gv * 1.3f + 3f) * 0.5f;
                    float dx = (cu - gu) - (0.5f + (jx - 0.25f));
                    float dy = (cv - gv) - (0.5f + (jy - 0.25f));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float stoneN = Fbm(u, v, 22f);
                    Color c = Color.Lerp(stoneL, stoneD, Mathf.Clamp01(d * 1.8f));      // domed stones
                    c = Color.Lerp(c, stoneD, Mathf.Clamp01((stoneN - 0.5f) * 1.2f));   // stone mottle
                    if (d > 0.42f) c = Color.Lerp(c, mortar, Mathf.Clamp01((d - 0.42f) * 4f)); // mortar gaps
                    px[y * s + x] = c;
                }
            return px;
        }

        // Brick wall: staggered red-brown bricks with recessed dark mortar lines.
        public static Color32[] Brick(int s)
        {
            var px = new Color32[s * s];
            Color mortar = new Color(0.22f, 0.19f, 0.16f);
            Color[] brick = { new Color(0.52f, 0.28f, 0.20f), new Color(0.46f, 0.24f, 0.17f), new Color(0.58f, 0.33f, 0.24f), new Color(0.42f, 0.22f, 0.16f) };
            const int rows = 8; const float bw = 1f / 4f, bh = 1f / rows, m = 0.05f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    int row = (int)(v / bh);
                    float uu = u + (row % 2 == 0 ? 0f : bw * 0.5f);
                    uu -= Mathf.Floor(uu);
                    float fu = (uu % bw) / bw, fv = (v % bh) / bh;
                    bool inMortar = fu < m / bw || fu > 1 - m / bw || fv < m / bh || fv > 1 - m / bh;
                    int idx = ((int)(uu / bw) * 3 + row * 7) % brick.Length;
                    Color c = inMortar ? mortar : Color.Lerp(brick[idx], brick[(idx + 1) % brick.Length], Fbm(u, v, 24f));
                    px[y * s + x] = c;
                }
            return px;
        }

        // Stone wall: irregular grey ashlar blocks with dark joints.
        public static Color32[] StoneWall(int s)
        {
            var px = new Color32[s * s];
            Color joint = new Color(0.17f, 0.16f, 0.15f);
            Color d = new Color(0.34f, 0.33f, 0.31f), l = new Color(0.56f, 0.55f, 0.52f);
            const float cell = 5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    int row = (int)(v * cell);
                    float uu = u * cell + (row % 2 == 0 ? 0f : 0.5f);
                    float fu = uu - Mathf.Floor(uu), fv = (v * cell) - Mathf.Floor(v * cell);
                    float edge = Mathf.Min(Mathf.Min(fu, 1 - fu), Mathf.Min(fv, 1 - fv));
                    float n = Fbm(u, v, 16f);
                    Color c = Color.Lerp(d, l, n);
                    if (edge < 0.09f) c = Color.Lerp(c, joint, (0.09f - edge) / 0.09f);
                    px[y * s + x] = c;
                }
            return px;
        }

        // Thatch roof: golden straw with vertical fibre streaks + darker horizontal courses.
        public static Color32[] Thatch(int s)
        {
            var px = new Color32[s * s];
            Color dark = new Color(0.42f, 0.31f, 0.13f), mid = new Color(0.60f, 0.46f, 0.20f), light = new Color(0.74f, 0.60f, 0.30f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float fib = Fbm(u * 3f, v, 40f);                 // vertical straw fibres
                    float course = Mathf.Abs(Mathf.Sin(v * 3.14159f * 7f));
                    Color c = Color.Lerp(dark, mid, course);
                    c = Color.Lerp(c, light, Mathf.Clamp01((fib - 0.5f) * 2f));
                    px[y * s + x] = c;
                }
            return px;
        }

        // Terracotta tile roof: rows of overlapping orange-red curved tiles.
        public static Color32[] RoofTile(int s)
        {
            var px = new Color32[s * s];
            Color gap = new Color(0.32f, 0.14f, 0.08f);
            Color d = new Color(0.62f, 0.26f, 0.15f), l = new Color(0.82f, 0.42f, 0.24f);
            const int rows = 7, cols = 6;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float fv = (v * rows) - Mathf.Floor(v * rows);
                    int row = (int)(v * rows);
                    float uu = u * cols + (row % 2 == 0 ? 0f : 0.5f);
                    float fu = uu - Mathf.Floor(uu);
                    float dome = Mathf.Sin(fu * 3.14159f);            // rounded tile shading
                    Color c = Color.Lerp(d, l, dome * (0.6f + 0.4f * Fbm(u, v, 20f)));
                    if (fv > 0.82f) c = Color.Lerp(c, gap, (fv - 0.82f) / 0.18f);   // row overlap shadow
                    px[y * s + x] = c;
                }
            return px;
        }

        // Wood plank floor: horizontal boards with dark seams + grain.
        public static Color32[] Plank(int s)
        {
            var px = new Color32[s * s];
            Color seam = new Color(0.16f, 0.10f, 0.06f);
            Color d = new Color(0.30f, 0.20f, 0.11f), l = new Color(0.46f, 0.33f, 0.19f);
            const int boards = 6;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)s, v = y / (float)s;
                    float fv = (v * boards) - Mathf.Floor(v * boards);
                    float grain = Fbm(u, v * 0.3f, 30f);
                    Color c = Color.Lerp(d, l, grain);
                    if (fv < 0.06f || fv > 0.94f) c = Color.Lerp(c, seam, 0.7f);
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
