using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AldenFall.Editor
{
    /// <summary>Idempotent Unity-6 URP production renderer setup. Kept as code so renderer changes
    /// are reproducible rather than opaque hand-edits to serialized pipeline assets.</summary>
    public static class ProductionRenderSetup
    {
        private const string RendererPath = "Assets/Settings/EmberfallURP_Renderer.asset";

        [MenuItem("AldenFall/Rendering/Configure Production URP")]
        public static void Configure()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null) throw new System.IO.FileNotFoundException(RendererPath);

            var ssao = data.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "Production SSAO";
                AssetDatabase.AddObjectToAsset(ssao, data);
                data.rendererFeatures.Add(ssao);
            }

            // Medium blue-noise AO is the quality/performance starting point. Depth normals avoids
            // reconstructing unstable foliage normals; direct-light strength stays restrained.
            var so = new SerializedObject(ssao);
            var settings = so.FindProperty("m_Settings");
            settings.FindPropertyRelative("AOMethod").enumValueIndex = 0;       // BlueNoise
            settings.FindPropertyRelative("Downsample").boolValue = true;
            settings.FindPropertyRelative("AfterOpaque").boolValue = false;
            settings.FindPropertyRelative("Source").enumValueIndex = 1;         // DepthNormals
            settings.FindPropertyRelative("NormalSamples").enumValueIndex = 1;  // Medium
            settings.FindPropertyRelative("Intensity").floatValue = 1.35f;
            settings.FindPropertyRelative("DirectLightingStrength").floatValue = 0.12f;
            settings.FindPropertyRelative("Radius").floatValue = 0.06f;
            settings.FindPropertyRelative("Samples").enumValueIndex = 1;        // Medium
            settings.FindPropertyRelative("BlurQuality").enumValueIndex = 1;    // Medium
            settings.FindPropertyRelative("Falloff").floatValue = 70f;
            so.ApplyModifiedPropertiesWithoutUndo();
            ssao.Create();

            EditorUtility.SetDirty(ssao);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("PRODUCTION URP: SSAO configured; GPU Resident Drawer/occlusion are enabled in EmberfallURP.");
        }
    }
}
