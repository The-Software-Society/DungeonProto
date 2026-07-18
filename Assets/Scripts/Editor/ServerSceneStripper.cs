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

            int renderers = 0, filters = 0, lods = 0, meshCols = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                // MESH COLLIDERS -> BOX COLLIDERS *first* (before meshes are stripped): convex mesh
                // colliders on hundreds of rocks force the server to keep every rock MESH + a cooked
                // convex hull in RAM, and they make the runtime NavMesh bake far heavier. On Render's
                // 512MB starter plan that crash-loops the server (OOM / health-check-blocking nav bake).
                // The server only needs approximate blocking + nav obstacles, so a cheap AABB box does it
                // -- and dropping the mesh reference lets Unity strip the rock meshes entirely. The CLIENT
                // keeps its precise convex colliders (it isn't stripped).
                foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                {
                    var go = mc.gameObject;
                    if (mc.sharedMesh != null && go.GetComponent<BoxCollider>() == null)
                    {
                        var b = mc.sharedMesh.bounds;
                        var box = go.AddComponent<BoxCollider>();
                        box.center = b.center; box.size = b.size; box.isTrigger = mc.isTrigger;
                    }
                    Object.DestroyImmediate(mc); meshCols++;
                }
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
            Debug.Log($"[ServerStrip] '{scene.name}': removed {renderers} renderers, {filters} filters, {lods} LODGroups, {meshCols} meshColliders->box (headless server -- lean RAM)");
        }
    }
}
