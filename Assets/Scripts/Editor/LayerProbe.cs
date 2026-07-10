#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AldenFall.Editor
{
    /// <summary>Diagnostic: what do layer lookups return in this editor session?</summary>
    public static class LayerProbe
    {
        /// <summary>
        /// Writes the project's custom layers through Unity's serialization API.
        /// Fixes the in-memory TagManager AND persists canonical bytes, bypassing
        /// whatever made the on-disk YAML unreadable to this editor.
        /// </summary>
        public static void FixLayers()
        {
            // Diagnostic: what bytes does THIS process see on disk?
            string raw = System.IO.File.ReadAllText("ProjectSettings/TagManager.asset");
            Debug.Log($"LAYERPROBE raw file starts: {raw.Substring(0, Mathf.Min(240, raw.Length)).Replace("\n", "\\n")}");

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var so = new SerializedObject(asset);
            var layers = so.FindProperty("layers");

            var wanted = new System.Collections.Generic.Dictionary<int, string>
            {
                { 8, "Ground" }, { 9, "Player" }, { 10, "Enemy" },
                { 11, "Projectile" }, { 12, "Interactable" },
            };
            foreach (var kv in wanted)
            {
                var slot = layers.GetArrayElementAtIndex(kv.Key);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = kv.Value;
                    Debug.Log($"LAYERPROBE set layer {kv.Key} = {kv.Value}");
                }
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.Log($"LAYERPROBE after fix: Ground={LayerMask.NameToLayer("Ground")} " +
                      $"Player={LayerMask.NameToLayer("Player")} Enemy={LayerMask.NameToLayer("Enemy")} " +
                      $"Projectile={LayerMask.NameToLayer("Projectile")}");
        }

        public static void Run()
        {
            Debug.Log($"LAYERPROBE NameToLayer: Ground={LayerMask.NameToLayer("Ground")} " +
                      $"Player={LayerMask.NameToLayer("Player")} Enemy={LayerMask.NameToLayer("Enemy")} " +
                      $"Projectile={LayerMask.NameToLayer("Projectile")} Water={LayerMask.NameToLayer("Water")}");

            for (int i = 0; i < 16; i++)
                Debug.Log($"LAYERPROBE LayerToName({i})='{LayerMask.LayerToName(i)}'");

            Debug.Log("LAYERPROBE InternalEditorUtility.layers: " +
                      string.Join(", ", UnityEditorInternal.InternalEditorUtility.layers));

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            Debug.Log($"LAYERPROBE TagManager assets loaded: {assets.Length}");
            foreach (var a in assets)
            {
                var so = new SerializedObject(a);
                var layers = so.FindProperty("layers");
                if (layers != null)
                {
                    for (int i = 0; i < layers.arraySize && i < 16; i++)
                        Debug.Log($"LAYERPROBE serialized layers[{i}]='{layers.GetArrayElementAtIndex(i).stringValue}'");
                }
            }
        }
    }
}
#endif
