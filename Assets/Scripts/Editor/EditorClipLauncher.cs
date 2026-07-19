#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace AldenFall.Editor
{
    /// <summary>GPU editor-play capture fallback for validating scenes that exceed a Unity player
    /// serializer/package limitation. Uses the exact saved Emberfall scene and runtime scripts.</summary>
    public static class EditorClipLauncher
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
