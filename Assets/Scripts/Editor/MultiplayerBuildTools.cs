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
