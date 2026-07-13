#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AldenFall.Editor
{
    /// <summary>
    /// Turns Meshy pipeline output (Assets/Models/Characters/<Name>/) into
    /// game-ready pieces:
    ///   1. Configures FBX importers (Generic rig, shared avatar, looped locomotion)
    ///   2. Builds an AnimatorController whose parameters match the hashes already
    ///      used by PlayerController / PlayerCombat / PlayerDeath / EnemyAI
    ///   3. Saves a visual prefab (model + Animator) that SceneSetup swaps in
    ///      over the primitive placeholders
    ///
    /// Menu: AldenFall > Import Meshy Characters
    /// Headless: -executeMethod AldenFall.Editor.MeshyCharacterImporter.ImportAll
    /// </summary>
    public static class MeshyCharacterImporter
    {
        private const string CharactersRoot = "Assets/Models/Characters";
        private const string ControllerDir = "Assets/Animations/Generated";
        private const string PrefabDir = "Assets/Prefabs/Generated";

        // Clips that should loop (any Dance* clip loops too — see IsLooped)
        private static readonly HashSet<string> LoopedClips = new HashSet<string>
        {
            "Idle", "Walk", "Run", "Swim", "SwimIdle",
        };

        private static bool IsLooped(string clipName) =>
            LoopedClips.Contains(clipName) || clipName.StartsWith("Dance");

        // Characters whose clips must play VERBATIM (Blender-authored poses):
        // Humanoid muscle space reinterprets extreme authored poses (sideways
        // knees, drifting arms — v7.4 twerk lesson). Generic = pose fidelity;
        // these characters receive same-skeleton clips only.
        private static readonly HashSet<string> GenericOnly = new HashSet<string>
        {
            "GoblinDancer",
        };

        [MenuItem("AldenFall/Import Meshy Characters", priority = 3)]
        public static void ImportAll()
        {
            if (!AssetDatabase.IsValidFolder(CharactersRoot))
            {
                Debug.LogWarning($"No {CharactersRoot} folder. Run tools/meshy_pipeline.py first.");
                return;
            }

            EnsureFolder(ControllerDir);
            EnsureFolder(PrefabDir);

            foreach (string dir in Directory.GetDirectories(CharactersRoot))
            {
                string name = Path.GetFileName(dir);
                try
                {
                    ImportCharacter(name, dir.Replace('\\', '/'));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Import failed for {name}: {e}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Meshy character import complete.");
        }

        private static void ImportCharacter(string name, string dir)
        {
            // ---- locate the base (rigged) model ----
            // Prefer the canonical rigged character (T-pose) over the
            // basic-animation variants Meshy also exports.
            var riggedCandidates = Directory.GetFiles(dir, $"{name}_rigged_*.fbx");
            string baseFbx = riggedCandidates.FirstOrDefault(f => f.Contains("rigged_character"))
                          ?? riggedCandidates.FirstOrDefault()
                          ?? Directory.GetFiles(dir, $"{name}_game.fbx").FirstOrDefault()
                          ?? Directory.GetFiles(dir, $"{name}_static.fbx").FirstOrDefault();
            if (baseFbx == null)
            {
                Debug.LogWarning($"{name}: no FBX found in {dir}, skipping.");
                return;
            }
            baseFbx = baseFbx.Replace('\\', '/');

            // Humanoid first: Unity retargets ANY humanoid clip from ANY
            // source (SayMotion text-to-motion, BVH converts, Mixamo) onto a
            // Humanoid rig at runtime — the universal adapter for "describe
            // an animation and it exists". Generic remains the fallback when
            // the auto-rig doesn't map cleanly.
            bool humanoid = !GenericOnly.Contains(name) && TryConfigureHumanoid(baseFbx, loop: false);
            if (!humanoid)
                ConfigureModelImporter(baseFbx, null, isBase: true);
            var baseAvatar = AssetDatabase.LoadAllAssetsAtPath(baseFbx).OfType<Avatar>().FirstOrDefault();
            Debug.Log($"{name}: rig mode = {(humanoid ? "HUMANOID (retarget-capable)" : "Generic")}");

            // ---- animation FBXs (<Name>@<Clip>.fbx) ----
            // In humanoid mode, foreign-skeleton clips (text-to-motion output
            // dropped into the folder) work too — each FBX carries its own
            // avatar and Unity retargets through the muscle space.
            var clips = new Dictionary<string, AnimationClip>();
            string animDir = $"{dir}/Animations";
            if (Directory.Exists(animDir))
            {
                foreach (string animPath in Directory.GetFiles(animDir, $"{name}@*.fbx"))
                {
                    string path = animPath.Replace('\\', '/');
                    string clipName = Path.GetFileNameWithoutExtension(path).Split('@')[1];

                    bool clipHumanoid = humanoid && TryConfigureHumanoid(path, loop: IsLooped(clipName));
                    if (!clipHumanoid)
                        ConfigureModelImporter(path, humanoid ? null : baseAvatar,
                            isBase: false, loop: IsLooped(clipName));

                    var clip = AssetDatabase.LoadAllAssetsAtPath(path)
                        .OfType<AnimationClip>()
                        .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                    if (clip != null) clips[clipName] = clip;
                }
            }

            // ---- authored clips: DISABLED by default. Hand-written euler
            // curves fought the auto-rig's axis conventions and read as broken
            // in-game (v7.1 pond troupe). Full-body motion comes from mocap
            // (library clips or a regenerated character); flip this only for
            // axis-calibration experiments. ----
            const bool AuthorDanceClips = false;
            if (AuthorDanceClips)
            {
                var authorModel = AssetDatabase.LoadAssetAtPath<GameObject>(baseFbx);
                if (authorModel != null)
                {
                    var temp = (GameObject)PrefabUtility.InstantiatePrefab(authorModel);
                    try
                    {
                        foreach (var kv in DanceClipAuthor.AuthorFor(name, temp))
                            clips[kv.Key] = kv.Value;
                    }
                    finally
                    {
                        Object.DestroyImmediate(temp);
                    }
                }
            }

            Debug.Log($"{name}: {clips.Count} clips [{string.Join(", ", clips.Keys)}]");

            // ---- animator controller + visual prefab ----
            var controller = BuildController(name, clips);
            BuildVisualPrefab(name, baseFbx, controller);
        }

        /// <summary>
        /// Attempt Humanoid import; true when the avatar maps validly. On
        /// failure the caller reverts to the Generic path — nothing breaks,
        /// that character just can't receive foreign clips.
        /// </summary>
        private static bool TryConfigureHumanoid(string path, bool loop)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return false;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

            if (loop)
            {
                var clipAnims = importer.defaultClipAnimations;
                foreach (var c in clipAnims)
                {
                    c.loopTime = true;
                    c.lockRootRotation = true;      // dances shouldn't drift the body
                    c.lockRootPositionXZ = true;
                    c.keepOriginalPositionY = true;
                    c.keepOriginalOrientation = true;
                }
                if (clipAnims.Length > 0) importer.clipAnimations = clipAnims;
            }

            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            if (avatar != null && avatar.isValid && avatar.isHuman) return true;

            Debug.LogWarning($"{Path.GetFileName(path)}: humanoid avatar mapping failed, falling back to Generic.");
            return false;
        }

        private static void ConfigureModelImporter(string path, Avatar sourceAvatar, bool isBase, bool loop = false)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importAnimation = !isBase || importer.defaultClipAnimations.Length > 0;
            importer.materialImportMode = isBase
                ? ModelImporterMaterialImportMode.ImportStandard
                : ModelImporterMaterialImportMode.None;

            if (!isBase && sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else if (isBase)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }

            if (loop)
            {
                var clipAnims = importer.defaultClipAnimations;
                foreach (var c in clipAnims)
                {
                    c.loopTime = true;
                    c.lockRootRotation = true;
                    c.lockRootPositionXZ = true;
                    c.keepOriginalPositionY = true;
                    c.keepOriginalOrientation = true;
                }
                if (clipAnims.Length > 0) importer.clipAnimations = clipAnims;
            }

            importer.SaveAndReimport();
        }

        // ==================== ANIMATOR CONTROLLER ====================

        private static AnimatorController BuildController(string name, Dictionary<string, AnimationClip> clips)
        {
            string path = $"{ControllerDir}/{name}Controller.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            // Parameters — must match the Animator.StringToHash names in gameplay code
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsSwimming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("IsJumping", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Charging", AnimatorControllerParameterType.Bool);
            foreach (var trigger in new[] { "Jump", "Dodge", "Attack", "SpecialAttack",
                                            "Death", "Respawn", "JumpAttack", "ChargeWindup", "ChargeHit",
                                            "Eat" })
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // --- Locomotion blend tree (default state) ---
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.blendType = BlendTreeType.Simple1D;
            tree.useAutomaticThresholds = false;
            if (clips.TryGetValue("Idle", out var idle)) tree.AddChild(idle, 0f);
            if (clips.TryGetValue("Walk", out var walk)) tree.AddChild(walk, 0.5f);
            if (clips.TryGetValue("Run", out var run)) tree.AddChild(run, 1f);
            sm.defaultState = locomotion;

            // --- Swim (optional) ---
            if (clips.ContainsKey("Swim") || clips.ContainsKey("SwimIdle"))
            {
                var swim = controller.CreateBlendTreeInController("Swim", out BlendTree swimTree, 0);
                swimTree.blendParameter = "Speed";
                swimTree.blendType = BlendTreeType.Simple1D;
                swimTree.useAutomaticThresholds = false;
                if (clips.TryGetValue("SwimIdle", out var swimIdle)) swimTree.AddChild(swimIdle, 0f);
                if (clips.TryGetValue("Swim", out var swimFwd)) swimTree.AddChild(swimFwd, 0.5f);

                var toSwim = locomotion.AddTransition(swim);
                toSwim.AddCondition(AnimatorConditionMode.If, 0, "IsSwimming");
                toSwim.hasExitTime = false;
                toSwim.duration = 0.2f;

                var fromSwim = swim.AddTransition(locomotion);
                fromSwim.AddCondition(AnimatorConditionMode.IfNot, 0, "IsSwimming");
                fromSwim.hasExitTime = false;
                fromSwim.duration = 0.2f;
            }

            // --- One-shot action states (AnyState -> clip -> Locomotion) ---
            void OneShot(string clipKey, string trigger, float exitDuration = 0.15f)
            {
                if (!clips.TryGetValue(clipKey, out var clip)) return;
                var state = sm.AddState(clipKey);
                state.motion = clip;

                var enter = sm.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
                enter.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
                enter.hasExitTime = false;
                enter.duration = 0.08f;
                enter.canTransitionToSelf = false;

                var exit = state.AddTransition(locomotion);
                exit.hasExitTime = true;
                exit.exitTime = 0.9f;
                exit.duration = exitDuration;
            }

            OneShot("Attack", "Attack");
            OneShot("Cast", "SpecialAttack");
            OneShot("Dodge", "Dodge");
            OneShot("Jump", "Jump");
            OneShot("Jump", "JumpAttack");   // goblin leap reuses the jump clip
            OneShot("Hit", "ChargeHit");     // also the stun/stagger react
            OneShot("Eat", "Eat");           // food heal (Hero only has the clip)

            // Goblins without a Cast clip still need SpecialAttack safety: skipped when absent.

            // --- Dance states (looping islands — entered by name via
            // Animator.CrossFade from NeutralNPC / future emote system; any
            // AnyState transition (Attack, Hit, Death) yanks straight out) ---
            foreach (var (clipName, clip) in clips)
            {
                if (!clipName.StartsWith("Dance")) continue;
                var danceState = sm.AddState(clipName);
                danceState.motion = clip;
            }

            // --- Death (bool-driven, no exit until respawn) ---
            if (clips.TryGetValue("Death", out var deathClip))
            {
                var death = sm.AddState("Death");
                death.motion = deathClip;

                var toDeath = sm.AddAnyStateTransition(death);
                toDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
                toDeath.hasExitTime = false;
                toDeath.duration = 0.1f;
                toDeath.canTransitionToSelf = false;

                var revive = death.AddTransition(locomotion);
                revive.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
                revive.hasExitTime = false;
                revive.duration = 0.1f;
            }

            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ==================== VISUAL PREFAB ====================

        private static void BuildVisualPrefab(string name, string baseFbx, AnimatorController controller)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(baseFbx);
            if (model == null)
            {
                Debug.LogWarning($"{name}: could not load model at {baseFbx}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = $"{name}Visual";

            var animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // gameplay code moves the root

            // Meshy FBX carries no textures — apply the base color extracted
            // from the GLB twin (tools/extract_glb_textures.py).
            string dir = Path.GetDirectoryName(baseFbx).Replace('\\', '/');
            var material = BuildTexturedMaterial(name, $"{dir}/{name}_baseColor");
            if (material != null)
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = material;
            }

            string prefabPath = $"{PrefabDir}/{name}Visual.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            Debug.Log($"{name}: visual prefab saved -> {prefabPath} (textured={material != null})");
        }

        /// <summary>URP Lit material using an extracted base-color texture.</summary>
        internal static Material BuildTexturedMaterial(string name, string texturePathNoExt)
            => BuildTexturedMaterial(name, texturePathNoExt, Color.white);

        /// <summary>
        /// Tinted variant: one shared model can ship as several items (raw /
        /// cooked / burnt fish, focus tiers) — the tint multiplies the base map.
        /// </summary>
        internal static Material BuildTexturedMaterial(string name, string texturePathNoExt, Color tint)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{texturePathNoExt}.png")
                         ?? AssetDatabase.LoadAssetAtPath<Texture2D>($"{texturePathNoExt}.jpg");
            if (tex == null) return null;

            const string matDir = "Assets/Materials/Generated";
            EnsureFolder(matDir);
            string matPath = $"{matDir}/{name}.mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader lit = GetDefaultLitShader();
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != lit)
            {
                mat.shader = lit; // upgrade if the render pipeline changed
            }

            mat.color = tint;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Shader cachedLitShader;

        private static Shader GetDefaultLitShader()
        {
            if (cachedLitShader != null) return cachedLitShader;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cachedLitShader = temp.GetComponent<MeshRenderer>().sharedMaterial.shader;
            Object.DestroyImmediate(temp);
            return cachedLitShader;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
