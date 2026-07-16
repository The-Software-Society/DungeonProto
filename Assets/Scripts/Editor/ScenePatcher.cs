#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>
    /// Targeted, idempotent scene edits that add specific objects without a full ArenaBuilder
    /// world regen. Used to introduce new scene objects (ServerProgressState, the city
    /// progress board) into the already-baked EmberfallDemo scene. Run headless via
    /// -executeMethod before a build, or from the menu. Every add is guarded so re-running is
    /// a no-op.
    /// </summary>
    public static class ScenePatcher
    {
        private const string ScenePath = "Assets/Scenes/EmberfallDemo.unity";

        [MenuItem("AldenFall/World/Patch Scene (progress objects)", priority = 210)]
        public static void PatchScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            bool changed = false;

            if (Object.FindFirstObjectByType<World.ServerProgressState>() == null)
            {
                var go = new GameObject("ServerProgressState");
                go.AddComponent<Mirror.NetworkIdentity>();
                go.AddComponent<World.ServerProgressState>();
                changed = true;
                Debug.Log("ScenePatcher: added ServerProgressState (NetworkIdentity).");
            }

            // Strip any stale CityProgressBoard: it exposed FLOOR/boss meta in the open world.
            foreach (var stale in Object.FindObjectsByType<World.ProgressBoard>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(stale.gameObject);
                changed = true;
                Debug.Log("ScenePatcher: removed stale CityProgressBoard.");
            }

            if (!changed)
            {
                Debug.Log("ScenePatcher: scene already patched, nothing to do.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ScenePatcher: saved scene.");
        }

        // Back-compat alias for the earlier method name.
        public static void AddServerProgressState() => PatchScene();

        /// <summary>Add archery ammo (arrow + short_bow) to Sela the Provisioner's stock in the
        /// current scene, so the town supports archers without a full world regen (v9).</summary>
        [MenuItem("AldenFall/World/Patch Vendor Stock (archery)", priority = 212)]
        public static void PatchVendorStock()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            bool changed = false;
            foreach (var v in Object.FindObjectsByType<World.VendorNPC>(FindObjectsSortMode.None))
            {
                if (v.gameObject.name != "VendorSela") continue;
                var so = new SerializedObject(v);
                var stock = so.FindProperty("stock");
                var have = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < stock.arraySize; i++) have.Add(stock.GetArrayElementAtIndex(i).stringValue);
                foreach (var id in new[] { "arrow", "short_bow" })
                    if (have.Add(id))
                    {
                        stock.arraySize++;
                        stock.GetArrayElementAtIndex(stock.arraySize - 1).stringValue = id;
                        changed = true;
                    }
                so.ApplyModifiedProperties();
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("ScenePatcher: added arrow + short_bow to Sela's stock + saved.");
            }
            else Debug.Log("ScenePatcher: vendor stock already patched.");
        }

        /// <summary>Fix grass-blade meshes already baked in the scene: point their normals UP so
        /// they light as bright ground cover instead of rendering as black shards (v9 visual).</summary>
        [MenuItem("AldenFall/World/Fix Grass Blade Lighting", priority = 214)]
        public static void FixGrassBlades()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int fixedCount = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var n = mf.gameObject.name;
                if (!n.Contains("Grass")) continue;
                var m = mf.sharedMesh;
                if (m == null || m.vertexCount == 0) continue;
                var ups = new Vector3[m.vertexCount];
                for (int i = 0; i < ups.Length; i++) ups[i] = Vector3.up;
                m.SetNormals(ups);
                m.RecalculateBounds();
                fixedCount++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"ScenePatcher: set UP normals on {fixedCount} grass meshes + saved.");
        }

        /// <summary>Rebuild ONLY the GameUI HUD canvas in the scene (HUDBuilder destroys + rebuilds
        /// it, and regenerates the UI sprite set), leaving the rest of the world untouched. Much
        /// safer than a full BuildDemoHeadless world regen when only the HUD changed.</summary>
        [MenuItem("AldenFall/World/Rebuild HUD", priority = 211)]
        public static void RebuildHud()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            HUDBuilder.Build();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ScenePatcher: rebuilt HUD (GameUI) + saved scene.");
        }
    }
}
#endif
