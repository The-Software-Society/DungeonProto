#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>
    /// Activates URP for the project. The URP package was installed but no
    /// pipeline asset was ever assigned — the game was silently rendering with
    /// the built-in pipeline (no post-processing, broken URP shader features).
    ///
    /// Creates Assets/Settings/EmberfallURP.asset (+ renderer) and assigns it to
    /// Graphics settings and every quality level.
    /// </summary>
    public static class RenderPipelineSetup
    {
        private const string SettingsDir = "Assets/Settings";
        private const string RendererPath = SettingsDir + "/EmberfallURP_Renderer.asset";
        private const string PipelinePath = SettingsDir + "/EmberfallURP.asset";

        [MenuItem("AldenFall/Setup URP Pipeline", priority = 20)]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDir))
                AssetDatabase.CreateFolder("Assets", "Settings");

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // Quality-of-look defaults for the demo
            var so = new SerializedObject(pipeline);
            SetIfPresent(so, "m_SupportsHDR", true);
            SetIfPresent(so, "m_MainLightShadowsSupported", true);
            SetIfPresent(so, "m_SoftShadowsSupported", true);
            so.ApplyModifiedProperties();

            // Quality levels without an explicit override fall back to this default.
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline; // active level, for good measure

            AssetDatabase.SaveAssets();
            Debug.Log($"URP ACTIVATED: {PipelinePath} assigned to graphics + all quality levels. " +
                      $"Active pipeline now: {(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "NULL")}");
        }

        private static void SetIfPresent(SerializedObject so, string prop, bool value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
        }
    }
}
#endif
