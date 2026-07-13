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

            if (Object.FindFirstObjectByType<World.ProgressBoard>() == null)
            {
                var board = new GameObject("CityProgressBoard");
                board.transform.position = new Vector3(-18f, 2.3f, 18f);
                board.transform.rotation = Quaternion.Euler(0f, 135f, 0f);
                board.AddComponent<World.ProgressBoard>();
                changed = true;
                Debug.Log("ScenePatcher: added CityProgressBoard near the tavern/plaza.");
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
    }
}
#endif
