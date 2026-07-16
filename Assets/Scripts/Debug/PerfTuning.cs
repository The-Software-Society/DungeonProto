using UnityEngine;
using UnityEngine.Rendering;

namespace AldenFall.DebugTools
{
    /// <summary>
    /// Runtime perf knobs so the big GPU levers can be swept WITHOUT a rebuild each time (each Mac
    /// player build is ~2 min). Reads command-line flags on boot and applies them to the live URP
    /// asset via reflection (the runtime assembly does not reference the URP package). Used to
    /// diagnose pixel-bound vs CPU-bound and to dial in the shipping defaults.
    ///
    ///   -renderscale 0.8     URP render scale (Retina Macs render 2x native; &lt;1 is a big fill win)
    ///   -noopaque            drop the _CameraOpaqueTexture copy (nothing in this project samples it)
    ///   -nodepth             drop the depth prepass/copy (nothing samples it; soft particles are off)
    ///   -nohdr               LDR framebuffer (half the bandwidth; bloom dims a touch)
    ///
    /// These are DIAGNOSTIC/override flags; the shipping values are baked into EmberfallURP.asset.
    /// </summary>
    public static class PerfTuning
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            var args = System.Environment.GetCommandLineArgs();
            var urp = GraphicsSettings.currentRenderPipeline;
            if (urp == null) return;

            float rs = ArgFloat(args, "-renderscale", float.NaN);
            if (!float.IsNaN(rs)) SetProp(urp, "renderScale", Mathf.Clamp(rs, 0.4f, 1.5f));
            if (Has(args, "-noopaque")) SetProp(urp, "supportsCameraOpaqueTexture", false);
            if (Has(args, "-nodepth")) SetProp(urp, "supportsCameraDepthTexture", false);
            if (Has(args, "-nohdr")) SetProp(urp, "supportsHDR", false);

            int q = (int)ArgFloat(args, "-quality", -1);
            if (q >= 0) QualitySettings.SetQualityLevel(Mathf.Clamp(q, 0, QualitySettings.names.Length - 1), true);
            if (Has(args, "-noshadow")) QualitySettings.shadowDistance = 0f;
            Debug.Log($"[PerfTuning] quality={QualitySettings.GetQualityLevel()} shadowDist={QualitySettings.shadowDistance} lodBias={QualitySettings.lodBias}");

            Debug.Log($"[PerfTuning] renderScale={GetProp(urp, "renderScale")} " +
                      $"opaque={GetProp(urp, "supportsCameraOpaqueTexture")} " +
                      $"depth={GetProp(urp, "supportsCameraDepthTexture")} hdr={GetProp(urp, "supportsHDR")}");
        }

        private static void SetProp(object target, string name, object value)
        {
            var p = target.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(target, value);
            else Debug.LogWarning($"[PerfTuning] cannot set {name}");
        }

        private static object GetProp(object target, string name)
        {
            var p = target.GetType().GetProperty(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return p != null ? p.GetValue(target) : "?";
        }

        private static bool Has(string[] a, string flag) => System.Array.IndexOf(a, flag) >= 0;

        private static float ArgFloat(string[] a, string flag, float fallback)
        {
            int i = System.Array.IndexOf(a, flag);
            if (i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            return fallback;
        }
    }
}
