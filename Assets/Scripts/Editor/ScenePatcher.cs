#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>
    /// Targeted, idempotent scene edits that add a single object without a full ArenaBuilder
    /// world regen. Used to introduce new scene network objects (like ServerProgressState)
    /// into the already-baked EmberfallDemo scene. Run headless via -executeMethod before a
    /// build, or from the menu.
    /// </summary>
    public static class ScenePatcher
    {
        private const string ScenePath = "Assets/Scenes/EmberfallDemo.unity";

        [MenuItem("AldenFall/World/Add ServerProgressState To Scene", priority = 210)]
        public static void AddServerProgressState()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (Object.FindFirstObjectByType<World.ServerProgressState>() != null)
            {
                Debug.Log("ScenePatcher: ServerProgressState already present, nothing to do.");
                return;
            }

            var go = new GameObject("ServerProgressState");
            go.AddComponent<Mirror.NetworkIdentity>();
            go.AddComponent<World.ServerProgressState>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ScenePatcher: added ServerProgressState (NetworkIdentity) to the scene and saved.");
        }
    }
}
#endif
