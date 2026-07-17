using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AldenFall.Editor
{
    /// <summary>
    /// When building the DEDICATED SERVER (headless, never renders), strip MeshRenderers + MeshFilters +
    /// LODGroups from the scene during the build. The server only needs colliders + NavMesh + logic
    /// components, so the visual meshes/materials/textures are dead weight -- and they were bloating the
    /// server's sharedassets0.assets to ~409MB (the Poly Haven photoscans), which broke `git push` to Render
    /// (GitHub rejects files >100MB). This runs on the TEMPORARY build scene only -- the source scene is
    /// untouched. Colliders, Terrain (its collider/navmesh), Mirror NetworkIdentities, spawners, quests,
    /// and everything gameplay stays intact.
    /// </summary>
    public class ServerSceneStripper : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Only for a Standalone Server (headless) build.
            bool isServer = report != null
                && report.summary.platformGroup == BuildTargetGroup.Standalone
                && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server;
            if (!isServer) return;

            int renderers = 0, filters = 0, lods = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                // LODGroups first (they reference the renderers we're about to remove).
                foreach (var lg in root.GetComponentsInChildren<LODGroup>(true)) { Object.DestroyImmediate(lg); lods++; }
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) { Object.DestroyImmediate(mr); renderers++; }
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) { Object.DestroyImmediate(mf); filters++; }
                // Skinned meshes (characters) too -- server doesn't animate visuals.
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { Object.DestroyImmediate(smr); renderers++; }
                // Particle systems / trails / lights: pure visual, drop them.
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps);
                foreach (var lt in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(lt);
            }
            Debug.Log($"[ServerStrip] '{scene.name}': removed {renderers} renderers, {filters} filters, {lods} LODGroups (headless server -- no visuals bundled)");
        }
    }
}
