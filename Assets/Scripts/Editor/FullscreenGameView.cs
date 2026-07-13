#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace AldenFall.Editor
{
    /// <summary>
    /// Fullscreen Game view for testing/recording.
    ///
    ///   F8 (or AldenFall > Fullscreen Game View) — toggle borderless fullscreen
    ///   Small ✕ button floats top-right as a guaranteed exit
    ///   Stopping play mode (Cmd+P) also closes it
    ///   AldenFall > Fullscreen On Play — auto-fullscreen every Play
    ///
    /// Note: F11/F12 belong to macOS (Show Desktop / etc), hence F8.
    /// </summary>
    [InitializeOnLoad]
    public static class FullscreenGameView
    {
        private const string AutoPrefKey = "AldenFall.FullscreenOnPlay";
        private const string WindowTitle = "FullscreenGameView";

        private static EditorWindow fullscreenWindow;
        private static FullscreenExitButton exitButton;

        static FullscreenGameView()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("AldenFall/Fullscreen Game View _F8", priority = 30)]
        public static void Toggle()
        {
            RecoverOrphans();

            if (fullscreenWindow != null)
            {
                Close();
                return;
            }
            Open();
        }

        [MenuItem("AldenFall/Fullscreen On Play", priority = 31)]
        public static void ToggleAutoFullscreen()
        {
            bool enabled = !EditorPrefs.GetBool(AutoPrefKey, false);
            EditorPrefs.SetBool(AutoPrefKey, enabled);
            Menu.SetChecked("AldenFall/Fullscreen On Play", enabled);
            Debug.Log($"Fullscreen on play: {(enabled ? "ON" : "OFF")}");
        }

        [MenuItem("AldenFall/Fullscreen On Play", true)]
        private static bool ValidateAutoFullscreen()
        {
            Menu.SetChecked("AldenFall/Fullscreen On Play", EditorPrefs.GetBool(AutoPrefKey, false));
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                && EditorPrefs.GetBool(AutoPrefKey, false))
            {
                EditorApplication.delayCall += () => { RecoverOrphans(); if (fullscreenWindow == null) Open(); };
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                RecoverOrphans();
                Close();
            }
        }

        private static void RecoverOrphans()
        {
            // Domain reloads wipe statics; find windows we created earlier
            if (fullscreenWindow == null)
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                    if (window.titleContent.text == WindowTitle)
                        fullscreenWindow = window;
            }
            if (exitButton == null)
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<FullscreenExitButton>())
                    exitButton = window;
            }
        }

        private static void Open()
        {
            var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null)
            {
                Debug.LogError("FullscreenGameView: could not resolve UnityEditor.GameView");
                return;
            }

            fullscreenWindow = (EditorWindow)ScriptableObject.CreateInstance(gameViewType);
            fullscreenWindow.titleContent = new GUIContent(WindowTitle);

            Vector2 size = GetMainDisplaySize();
            fullscreenWindow.ShowPopup();
            fullscreenWindow.position = new Rect(0, 0, size.x, size.y);

            TrySetToolbarVisible(fullscreenWindow, false);

            // Guaranteed exit: small floating ✕ pinned top-right, always on top
            exitButton = ScriptableObject.CreateInstance<FullscreenExitButton>();
            exitButton.ShowPopup();
            exitButton.position = new Rect(size.x - 54, 8, 46, 30);

            // Focus the game view so keyboard input flows to the game
            fullscreenWindow.Focus();

            Debug.Log("Fullscreen Game view ON, F8 or the ✕ button (top-right) exits. Stopping play also exits.");
        }

        public static void Close()
        {
            if (fullscreenWindow != null)
            {
                fullscreenWindow.Close();
                fullscreenWindow = null;
            }
            if (exitButton != null)
            {
                exitButton.Close();
                exitButton = null;
            }
        }

        private static Vector2 GetMainDisplaySize()
        {
            var res = Screen.currentResolution;
            float width = res.width;
            float height = res.height;

            float scale = EditorGUIUtility.pixelsPerPoint;
            if (scale > 1f)
            {
                width /= scale;
                height /= scale;
            }
            return new Vector2(width, height);
        }

        private static void TrySetToolbarVisible(EditorWindow gameView, bool visible)
        {
            try
            {
                var prop = gameView.GetType().GetProperty("showToolbar",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                prop?.SetValue(gameView, visible);
            }
            catch { /* toolbar stays visible on this Unity version — harmless */ }
        }
    }

    /// <summary>Tiny always-available escape hatch for the fullscreen game view.</summary>
    public class FullscreenExitButton : EditorWindow
    {
        private void OnGUI()
        {
            // Escape key works when this window has focus; the button always works
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.F8))
            {
                FullscreenGameView.Close();
                return;
            }

            GUI.backgroundColor = new Color(0.9f, 0.25f, 0.25f);
            if (GUI.Button(new Rect(0, 0, position.width, position.height), "✕"))
            {
                FullscreenGameView.Close();
            }
        }
    }
}
#endif
