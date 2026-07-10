#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AldenFall.Editor
{
    public static class MultiplayerBuildTools
    {
        private const string ScenePath = "Assets/Scenes/EmberfallDemo.unity";
        private const string WebBuildDir = "Builds/WebGL";
        private const string ServerBuildDir = "Builds/LinuxServer";
        private const string MacBuildDir = "Builds/Mac";
        private const string WinBuildDir = "Builds/Windows";

        [MenuItem("AldenFall/Multiplayer/Build WebGL Test", priority = 100)]
        public static void BuildWebGlTest()
        {
            EnsureSceneExists();
            Directory.CreateDirectory(WebBuildDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WebBuildDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            LogReport("WebGL", report);
            CopyVercelConfig(WebBuildDir);
            PatchWebGlFullscreen(WebBuildDir);
        }

        [MenuItem("AldenFall/Multiplayer/Build Linux Dedicated Server", priority = 101)]
        public static void BuildLinuxDedicatedServer()
        {
            EnsureSceneExists();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                Debug.LogError(
                    "Linux dedicated server build target is not installed for this Unity editor. " +
                    "Install Linux Build Support / Dedicated Server Build Support for Unity 6000.3.2f1 in Unity Hub, then rerun this menu item.");
                return;
            }

            Directory.CreateDirectory(ServerBuildDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = $"{ServerBuildDir}/AldenFallServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                options = BuildOptions.None
            };

#if UNITY_2021_2_OR_NEWER
            options.subtarget = 1;
#else
            options.options = BuildOptions.EnableHeadlessMode;
#endif

            var report = BuildPipeline.BuildPlayer(options);
            LogReport("Linux dedicated server", report);
        }

        // ---------------------------------------------------------------------
        // Desktop builds — the dev source-of-truth (closest to the Play button)
        // and the payload we hand to itch.io / Steam later. Same scene, same code
        // as WebGL, just a faithful standalone target.
        // ---------------------------------------------------------------------

        [MenuItem("AldenFall/Multiplayer/Build Mac Desktop", priority = 110)]
        public static void BuildMacDesktop()
        {
            EnsureSceneExists();
            EnsureStandaloneIdentity();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            {
                Debug.LogError("Mac Build Support is not installed for Unity 6000.3.2f1. Add it in Unity Hub, then rerun.");
                return;
            }

            Directory.CreateDirectory(MacBuildDir);
            TrySetMacUniversal();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = $"{MacBuildDir}/AldenFall.app",
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            LogReport("Mac desktop", report);
        }

        [MenuItem("AldenFall/Multiplayer/Build Windows Desktop", priority = 111)]
        public static void BuildWindowsDesktop()
        {
            EnsureSceneExists();
            EnsureStandaloneIdentity();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("Windows Build Support is not installed for Unity 6000.3.2f1. Add it in Unity Hub, then rerun.");
                return;
            }

            Directory.CreateDirectory(WinBuildDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = $"{WinBuildDir}/AldenFall.exe",
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            LogReport("Windows desktop", report);
        }

        [MenuItem("AldenFall/Multiplayer/Build All Desktop (Mac + Windows)", priority = 112)]
        public static void BuildAllDesktop()
        {
            BuildMacDesktop();
            BuildWindowsDesktop();
        }

        /// <summary>
        /// Headless entrypoint for CI / terminal builds (run once Unity is closed):
        /// Unity -batchmode -quit -projectPath . -executeMethod AldenFall.Editor.MultiplayerBuildTools.BuildAllDesktopCI
        /// </summary>
        public static void BuildAllDesktopCI()
        {
            BuildMacDesktop();
            BuildWindowsDesktop();
        }

        /// <summary>Standalone macOS builds want a bundle identifier; fill one in if the project left it blank.</summary>
        private static void EnsureStandaloneIdentity()
        {
            if (string.IsNullOrEmpty(PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone)))
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "io.thesoftwaresociety.aldenfall");
        }

        /// <summary>
        /// Best-effort: make the macOS build a Universal binary (Intel + Apple Silicon) so a
        /// friend's Mac runs it natively regardless of chip. Reflection-guarded so it never
        /// breaks the build if the OSXStandalone API shifts between Unity versions.
        /// </summary>
        private static void TrySetMacUniversal()
        {
            try
            {
                System.Type settings = null;
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    settings = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings");
                    if (settings != null) break;
                }
                var archProp = settings?.GetProperty("architecture",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (archProp != null)
                {
                    var universal = System.Enum.Parse(archProp.PropertyType, "x64ARM64");
                    archProp.SetValue(null, universal);
                    Debug.Log("macOS build architecture set to Universal (x64 + ARM64).");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not force macOS Universal architecture (non-fatal, using default): {e.Message}");
            }
        }

        private static void EnsureSceneExists()
        {
            if (!File.Exists(ScenePath))
                ArenaBuilder.ImportMeshyAndBuildDemoHeadless();
        }

        private static void CopyVercelConfig(string buildDir)
        {
            const string source = "Deploy/Vercel/vercel.json";
            string target = Path.Combine(buildDir, "vercel.json");
            if (File.Exists(source))
            {
                File.Copy(source, target, true);
                Debug.Log($"Copied Vercel config to {target}");
            }
        }

        private static void PatchWebGlFullscreen(string buildDir)
        {
            string indexPath = Path.Combine(buildDir, "index.html");
            if (File.Exists(indexPath))
            {
                string html = File.ReadAllText(indexPath);
                html = html.Replace(
                    "        // Desktop style: Render the game canvas in a window that can be maximized to fullscreen:\n" +
                    "        canvas.style.width = \"960px\";\n" +
                    "        canvas.style.height = \"600px\";",
                    "        // Desktop style: fill the browser viewport.\n" +
                    "        canvas.style.width = \"100vw\";\n" +
                    "        canvas.style.height = \"100vh\";");
                File.WriteAllText(indexPath, html);
            }

            string stylePath = Path.Combine(buildDir, "TemplateData", "style.css");
            if (File.Exists(stylePath))
            {
                string css = File.ReadAllText(stylePath);
                css = css.Replace("body { padding: 0; margin: 0 }", "html, body { width: 100%; height: 100%; padding: 0; margin: 0; overflow: hidden; background: #231F20 }");
                css = css.Replace("#unity-container { position: absolute }", "#unity-container { position: fixed; width: 100%; height: 100% }");
                css = css.Replace("#unity-container.unity-desktop { left: 50%; top: 50%; transform: translate(-50%, -50%) }", "#unity-container.unity-desktop { left: 0; top: 0; transform: none }");
                css = css.Replace("#unity-canvas { background: #231F20 }", "#unity-canvas { width: 100%; height: 100%; background: #231F20 }");
                css = css.Replace("#unity-footer { position: relative }", "#unity-footer { display: none }");
                File.WriteAllText(stylePath, css);
            }
        }

        private static void LogReport(string label, BuildReport report)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"{label} build succeeded: {report.summary.totalSize / (1024f * 1024f):F1} MB");
            }
            else
            {
                Debug.LogError($"{label} build failed: {report.summary.result}");
            }
        }
    }
}
#endif
