#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>Generates the tiling textures the v2 water shader needs (BDO-ref quality pass):
    /// - WaterNormal.png: FBM ripple normal field, xy slopes in RG (decoded manually in-shader, so
    ///   no NormalMap-importer/DXT5nm channel-swizzle surprises in batch builds).
    /// - FoamMask.png: R = marbled sheet foam (holey), G = directional streak foam, B = fine flecks.
    /// All noise is made tileable with the 4-corner blend, so world-space tiling never seams.</summary>
    public static class WaterTextureGenerator
    {
        const string Dir = "Assets/Textures/Water";
        const int Size = 512;

        [MenuItem("AldenFall/Render/Generate water textures")]
        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets/Textures", "Water");

            WriteNormal($"{Dir}/WaterNormal.png");
            WriteFoam($"{Dir}/FoamMask.png");
            AssetDatabase.Refresh();

            foreach (var p in new[] { $"{Dir}/WaterNormal.png", $"{Dir}/FoamMask.png" })
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                imp.sRGBTexture = false;             // data textures, not color
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            Debug.Log("[WaterTex] WaterNormal.png + FoamMask.png generated.");
        }

        // Tileable FBM: 4-corner blend of Unity Perlin so the field wraps at [0,1)x[0,1).
        static float TileFbm(float u, float v, float baseFreq, int octaves, float seed)
        {
            float sum = 0f, amp = 0.5f, freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                float x = u * freq, y = v * freq, w = freq;
                float n00 = Mathf.PerlinNoise(x + seed, y + seed);
                float n10 = Mathf.PerlinNoise(x - w + seed, y + seed);
                float n01 = Mathf.PerlinNoise(x + seed, y - w + seed);
                float n11 = Mathf.PerlinNoise(x - w + seed, y - w + seed);
                float n = Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
                sum += (n - 0.5f) * amp;
                amp *= 0.5f; freq *= 2f;
            }
            return sum;   // roughly -0.5..0.5
        }

        static void WriteNormal(string path)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            float step = 1f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x * step, v = y * step;
                // Two ripple families: broad chop + fine wind ripples.
                float H(float uu, float vv) =>
                    TileFbm(uu, vv, 6f, 4, 17.13f) * 1.0f + TileFbm(uu, vv, 18f, 3, 91.7f) * 0.45f;
                float hx = H(u + step, v) - H(u - step, v);
                float hy = H(u, v + step) - H(u, v - step);
                // Slopes scaled into 0..1 around 0.5 (decoded in-shader as rg*2-1).
                float sx = Mathf.Clamp01(0.5f + hx * 7f);
                float sy = Mathf.Clamp01(0.5f + hy * 7f);
                tex.SetPixel(x, y, new Color(sx, sy, 1f, 1f));
            }
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static void WriteFoam(string path)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            float step = 1f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x * step, v = y * step;

                // R: marbled sheet foam -- ridged FBM with holes eaten out by a second field.
                float r1 = 1f - Mathf.Abs(TileFbm(u, v, 7f, 4, 3.3f)) * 4f;      // ridged web
                float holes = TileFbm(u, v, 13f, 3, 55.5f) * 2f;                  // -1..1
                float marble = Mathf.Clamp01(r1 * 0.75f + 0.55f - Mathf.Max(0f, holes) * 0.9f);

                // G: directional streaks -- FBM squashed 6:1 along v.
                float streak = Mathf.Clamp01(0.5f + TileFbm(u, v * 6f % 1f, 9f, 3, 27.9f) * 2.2f);
                streak = Mathf.Pow(streak, 2.2f);

                // B: sparse fine flecks.
                float fl = TileFbm(u, v, 34f, 2, 77.2f);
                float flecks = Mathf.Clamp01((fl - 0.16f) * 6f);

                tex.SetPixel(x, y, new Color(marble, streak, flecks, 1f));
            }
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
#endif
