#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>Diagnostic: lists scene objects carrying missing-script components.</summary>
    public static class MissingScriptProbe
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");

            var counts = new Dictionary<string, int>();
            int total = 0;
            foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(obj);
                if (missing <= 0) continue;
                total += missing;
                string key = $"{Path(obj)} x{missing}";
                counts[key] = missing;
            }

            Debug.Log($"MISSINGPROBE total={total}");
            int shown = 0;
            foreach (var kv in counts)
            {
                Debug.Log($"MISSINGPROBE {kv.Key}");
                if (++shown >= 40) { Debug.Log("MISSINGPROBE ...truncated"); break; }
            }
        }

        private static string Path(GameObject obj)
        {
            string path = obj.name;
            var t = obj.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }
    }
}
#endif
