#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>
    /// Headless bisection helper for the "level0 corrupted" standalone-load crash: opens the baked
    /// scene, strips ONE subsystem chosen by the ALDEN_STRIP env var, saves. Build + run the player
    /// headless after each strip to find which baked content breaks build serialization.
    /// Temporary diagnostic tool; not part of the normal bake.
    /// </summary>
    public static class SceneBisect
    {
        public static void Run()
        {
            string strip = Environment.GetEnvironmentVariable("ALDEN_STRIP") ?? "";
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            int removed = 0;

            switch (strip)
            {
                case "trees":
                    foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                    {
                        var td = t.terrainData;
                        removed += td.treeInstances.Length;
                        td.treeInstances = Array.Empty<TreeInstance>();
                        td.treePrototypes = Array.Empty<TreePrototype>();
                        EditorUtility.SetDirty(td);
                    }
                    break;
                case "details":
                    foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                    {
                        var td = t.terrainData;
                        removed += td.detailPrototypes.Length;
                        td.detailPrototypes = Array.Empty<DetailPrototype>();
                        EditorUtility.SetDirty(td);
                    }
                    break;
                case "chunks":
                    foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                        if (go != null && go.name.StartsWith("VisualChunk_"))
                        {
                            removed++;
                            UnityEngine.Object.DestroyImmediate(go);
                        }
                    break;
                case "chunks_even":
                case "chunks_odd":
                    // Halve by chunk-x parity to split "one bad object" from "too many objects".
                    int wantParity = strip == "chunks_even" ? 0 : 1;
                    foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        if (go == null || !go.name.StartsWith("VisualChunk_")) continue;
                        var parts = go.name.Split('_');
                        if (parts.Length < 3 || !int.TryParse(parts[1], out int cx)) continue;
                        if (((cx % 2) + 2) % 2 == wantParity)
                        {
                            removed++;
                            UnityEngine.Object.DestroyImmediate(go);
                        }
                    }
                    break;
                case "terrain":
                    foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                    {
                        removed++;
                        UnityEngine.Object.DestroyImmediate(t.gameObject);
                    }
                    break;
                case "unoverride":
                    // Strip ONLY the per-instance m_Materials overrides the bake writes onto prefab
                    // instances (renderer.sharedMaterial = tintMat), keeping objects + all other mods.
                    foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        if (go == null || !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) continue;
                        var mods = PrefabUtility.GetPropertyModifications(go);
                        if (mods == null) continue;
                        var kept = Array.FindAll(mods, m => m.propertyPath == null || !m.propertyPath.StartsWith("m_Materials"));
                        if (kept.Length != mods.Length)
                        {
                            removed += mods.Length - kept.Length;
                            PrefabUtility.SetPropertyModifications(go, kept);
                        }
                    }
                    break;
                case "markers":
                    foreach (var m in UnityEngine.Object.FindObjectsByType<AldenFall.World.WorldChunkMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        removed++;
                        UnityEngine.Object.DestroyImmediate(m);
                    }
                    foreach (var s in UnityEngine.Object.FindObjectsByType<AldenFall.World.WorldChunkStreamer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        removed++;
                        UnityEngine.Object.DestroyImmediate(s);
                    }
                    break;
                default:
                    if (strip.StartsWith("name:") || strip.StartsWith("keep:"))
                    {
                        // name:<substr> deletes chunk children whose name contains substr;
                        // keep:<substr> deletes chunk children whose name does NOT contain it.
                        bool invert = strip.StartsWith("keep:");
                        string needle = strip.Substring(5);
                        foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                        {
                            if (go == null || !go.transform.parent || !go.transform.parent.name.StartsWith("VisualChunk_")) continue;
                            bool hit = go.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (hit != invert)
                            {
                                removed++;
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        }
                        break;
                    }
                    Debug.LogError($"SceneBisect: unknown ALDEN_STRIP '{strip}'");
                    EditorApplication.Exit(2);
                    return;
            }

            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"SceneBisect: stripped '{strip}' ({removed} items), scene saved.");
        }
    }
}
#endif
