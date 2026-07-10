#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace AldenFall.Editor
{
    /// <summary>
    /// Hand-authored animation clips written directly onto a Meshy rig's bones
    /// — full creative freedom, no animation-library dependency (library tasks
    /// expire; it also simply doesn't HAVE a robot or a twerk).
    ///
    /// Clips are real keyframed .anim assets (Assets/Animations/Authored/),
    /// merged into the character's clip set by MeshyCharacterImporter, so they
    /// become looping Dance* animator states like any mocap clip.
    ///
    ///   DanceRobot — stepped-tangent pop-lock: snap poses with dead holds
    ///   DanceTwerk — crouched, leaned forward, high-frequency hip oscillation
    ///   DanceBounce — simple two-beat groove (modern-slot fallback)
    ///
    /// Axis conventions assume Meshy's mixamo-style auto-rig (x = pitch,
    /// y = yaw for upright bones). If a bone bends the wrong way on some rig,
    /// tune the offset constants and re-import — iteration is seconds.
    /// </summary>
    public static class DanceClipAuthor
    {
        private const string OutDir = "Assets/Animations/Authored";

        private class Rig
        {
            public Transform Root;
            public Transform Hips, Spine, Chest, Head;
            public Transform ArmL, ForeArmL, ArmR, ForeArmR;
            public Transform ThighL, CalfL, ThighR, CalfR;
        }

        /// <summary>Author the dance set for one character; empty when unrigged.</summary>
        public static Dictionary<string, AnimationClip> AuthorFor(string charName, GameObject modelRoot)
        {
            var results = new Dictionary<string, AnimationClip>();
            var rig = Discover(modelRoot.transform);
            if (rig.Hips == null)
            {
                Debug.Log($"{charName}: no hips bone found — skipping authored dances.");
                return results;
            }

            EnsureFolder(OutDir);
            results["DanceRobot"] = BuildRobot(charName, rig);
            results["DanceTwerk"] = BuildTwerk(charName, rig);
            results["DanceBounce"] = BuildBounce(charName, rig);
            Debug.Log($"{charName}: authored dances -> DanceRobot, DanceTwerk, DanceBounce");
            return results;
        }

        // ==================== CHOREOGRAPHY ====================

        /// <summary>Stepped pop-lock: pose → dead hold → SNAP. 8 beats @ 0.4s.</summary>
        private static AnimationClip BuildRobot(string charName, Rig rig)
        {
            var clip = NewClip();
            const float B = 0.4f; // beat
            float[] t8 = { 0, B, 2 * B, 3 * B, 4 * B, 5 * B, 6 * B, 7 * B, 8 * B };

            // Head: snap looks left/right with tilts
            KeyEuler(clip, rig, rig.Head, t8, stepped: true, offsets: new[]
            {
                V(0, 0, 0), V(0, -35, 0), V(0, -35, 14), V(0, 0, 0),
                V(0, 35, 0), V(0, 35, -14), V(12, 0, 0), V(0, 0, 0), V(0, 0, 0),
            });
            // Chest: quarter-turn torso snaps counter to the head
            KeyEuler(clip, rig, rig.Chest ?? rig.Spine, t8, stepped: true, offsets: new[]
            {
                V(0, 0, 0), V(0, 20, 0), V(0, 20, 0), V(0, -20, 0),
                V(0, -20, 0), V(0, 20, 0), V(0, 0, 6), V(0, 0, 0), V(0, 0, 0),
            });
            // Forearms: alternating 90° raises (the classic)
            KeyEuler(clip, rig, rig.ForeArmR, t8, stepped: true, offsets: new[]
            {
                V(0, 0, 0), V(-85, 0, 0), V(-85, 0, 0), V(0, 0, 0),
                V(-45, 0, 0), V(-45, 0, 0), V(-85, 0, 0), V(0, 0, 0), V(0, 0, 0),
            });
            KeyEuler(clip, rig, rig.ForeArmL, t8, stepped: true, offsets: new[]
            {
                V(-45, 0, 0), V(0, 0, 0), V(0, 0, 0), V(-85, 0, 0),
                V(-85, 0, 0), V(0, 0, 0), V(-85, 0, 0), V(-45, 0, 0), V(-45, 0, 0),
            });
            // Upper arms: hold them down out of T-pose, with occasional raises
            KeyEuler(clip, rig, rig.ArmR, t8, stepped: true, offsets: new[]
            {
                V(0, 0, -55), V(0, 0, -55), V(0, 0, -20), V(0, 0, -55),
                V(0, 0, -55), V(0, 0, -20), V(0, 0, -55), V(0, 0, -55), V(0, 0, -55),
            });
            KeyEuler(clip, rig, rig.ArmL, t8, stepped: true, offsets: new[]
            {
                V(0, 0, 55), V(0, 0, 55), V(0, 0, 55), V(0, 0, 20),
                V(0, 0, 55), V(0, 0, 55), V(0, 0, 20), V(0, 0, 55), V(0, 0, 55),
            });
            // Hip pops (position — axis-safe on any rig)
            KeyPosY(clip, rig, rig.Hips, t8, stepped: true,
                offsets: new[] { 0f, -0.04f, 0f, -0.04f, 0f, -0.04f, 0f, -0.04f, 0f });

            HoldRemainingBones(clip, rig, t8[^1]);
            return Save(clip, charName, "DanceRobot");
        }

        /// <summary>Crouched, leaned way forward, fast hip bounce. You asked.</summary>
        private static AnimationClip BuildTwerk(string charName, Rig rig)
        {
            var clip = NewClip();
            const float cycle = 1.28f;

            // Static stance held for the whole loop: deep crouch + forward lean
            float[] hold = { 0f, cycle };
            KeyEuler(clip, rig, rig.Spine, hold, stepped: false,
                offsets: new[] { V(42, 0, 0), V(42, 0, 0) });
            KeyEuler(clip, rig, rig.Head, hold, stepped: false,
                offsets: new[] { V(-30, 0, 0), V(-30, 0, 0) });     // eyes up
            KeyEuler(clip, rig, rig.ThighL, hold, stepped: false,
                offsets: new[] { V(-38, 0, 8), V(-38, 0, 8) });
            KeyEuler(clip, rig, rig.ThighR, hold, stepped: false,
                offsets: new[] { V(-38, 0, -8), V(-38, 0, -8) });
            KeyEuler(clip, rig, rig.CalfL, hold, stepped: false,
                offsets: new[] { V(46, 0, 0), V(46, 0, 0) });
            KeyEuler(clip, rig, rig.CalfR, hold, stepped: false,
                offsets: new[] { V(46, 0, 0), V(46, 0, 0) });
            // Hands toward the knees
            KeyEuler(clip, rig, rig.ArmL, hold, stepped: false,
                offsets: new[] { V(25, 0, 60), V(25, 0, 60) });
            KeyEuler(clip, rig, rig.ArmR, hold, stepped: false,
                offsets: new[] { V(25, 0, -60), V(25, 0, -60) });

            // THE hips: crouched height + fast pitch oscillation (8 bounces/loop)
            const int bounces = 8;
            var times = new float[bounces * 2 + 1];
            var pitch = new Vector3[bounces * 2 + 1];
            var height = new float[bounces * 2 + 1];
            for (int i = 0; i <= bounces * 2; i++)
            {
                times[i] = cycle * i / (bounces * 2f);
                bool up = i % 2 == 0;
                pitch[i] = V(up ? 22f : -6f, 0, 0);   // pelvis pitch swing
                height[i] = up ? -0.14f : -0.20f;      // bounce within the crouch
            }
            KeyEuler(clip, rig, rig.Hips, times, stepped: false, offsets: pitch);
            KeyPosY(clip, rig, rig.Hips, times, stepped: false, offsets: height);

            HoldRemainingBones(clip, rig, cycle);
            return Save(clip, charName, "DanceTwerk");
        }

        /// <summary>Simple two-beat bounce groove — the modern-slot fallback.</summary>
        private static AnimationClip BuildBounce(string charName, Rig rig)
        {
            var clip = NewClip();
            const float B = 0.36f;
            float[] t = { 0, B, 2 * B, 3 * B, 4 * B };

            KeyPosY(clip, rig, rig.Hips, t, stepped: false,
                offsets: new[] { 0f, -0.09f, 0f, -0.09f, 0f });
            KeyEuler(clip, rig, rig.Hips, t, stepped: false, offsets: new[]
                { V(0, -12, 0), V(0, 0, 0), V(0, 12, 0), V(0, 0, 0), V(0, -12, 0) });
            KeyEuler(clip, rig, rig.Chest ?? rig.Spine, t, stepped: false, offsets: new[]
                { V(6, 10, -4), V(2, 0, 0), V(6, -10, 4), V(2, 0, 0), V(6, 10, -4) });
            KeyEuler(clip, rig, rig.Head, t, stepped: false, offsets: new[]
                { V(-6, 8, 0), V(4, 0, 0), V(-6, -8, 0), V(4, 0, 0), V(-6, 8, 0) });
            // Arms pumping loosely at the sides
            KeyEuler(clip, rig, rig.ArmL, t, stepped: false, offsets: new[]
                { V(15, 0, 62), V(-10, 0, 55), V(15, 0, 62), V(-10, 0, 55), V(15, 0, 62) });
            KeyEuler(clip, rig, rig.ArmR, t, stepped: false, offsets: new[]
                { V(-10, 0, -55), V(15, 0, -62), V(-10, 0, -55), V(15, 0, -62), V(-10, 0, -55) });
            KeyEuler(clip, rig, rig.ForeArmL, t, stepped: false, offsets: new[]
                { V(-50, 0, 0), V(-25, 0, 0), V(-50, 0, 0), V(-25, 0, 0), V(-50, 0, 0) });
            KeyEuler(clip, rig, rig.ForeArmR, t, stepped: false, offsets: new[]
                { V(-25, 0, 0), V(-50, 0, 0), V(-25, 0, 0), V(-50, 0, 0), V(-25, 0, 0) });

            HoldRemainingBones(clip, rig, t[^1]);
            return Save(clip, charName, "DanceBounce");
        }

        // ==================== CURVE PLUMBING ====================

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private static AnimationClip NewClip() => new AnimationClip { frameRate = 30 };

        private static void KeyEuler(AnimationClip clip, Rig rig, Transform bone,
            float[] times, bool stepped, Vector3[] offsets)
        {
            if (bone == null) return;
            string path = AnimationUtility.CalculateTransformPath(bone, rig.Root);
            Vector3 baseEuler = bone.localEulerAngles;

            for (int axis = 0; axis < 3; axis++)
            {
                var curve = new AnimationCurve();
                for (int i = 0; i < times.Length; i++)
                    curve.AddKey(new Keyframe(times[i], baseEuler[axis] + offsets[i][axis]));
                ApplyTangents(curve, stepped);
                AnimationUtility.SetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve(path, typeof(Transform),
                        "localEulerAnglesRaw." + "xyz"[axis]), curve);
            }
        }

        private static void KeyPosY(AnimationClip clip, Rig rig, Transform bone,
            float[] times, bool stepped, float[] offsets)
        {
            if (bone == null) return;
            string path = AnimationUtility.CalculateTransformPath(bone, rig.Root);
            float baseY = bone.localPosition.y;

            var curve = new AnimationCurve();
            for (int i = 0; i < times.Length; i++)
                curve.AddKey(new Keyframe(times[i], baseY + offsets[i]));
            ApplyTangents(curve, stepped);
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.y"), curve);
        }

        private static void ApplyTangents(AnimationCurve curve, bool stepped)
        {
            for (int i = 0; i < curve.length; i++)
            {
                if (stepped)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                }
                else
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
                }
            }
        }

        /// <summary>
        /// Every un-choreographed bone gets a single hold key at its current
        /// pose — without this, animator write-defaults snaps them to T-pose.
        /// </summary>
        private static void HoldRemainingBones(AnimationClip clip, Rig rig, float duration)
        {
            var animated = new HashSet<string>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                animated.Add(binding.path);

            foreach (var bone in rig.Root.GetComponentsInChildren<Transform>())
            {
                if (bone == rig.Root) continue;
                if (bone.GetComponent<SkinnedMeshRenderer>() != null) continue;
                string path = AnimationUtility.CalculateTransformPath(bone, rig.Root);
                if (animated.Contains(path)) continue;

                Vector3 baseEuler = bone.localEulerAngles;
                for (int axis = 0; axis < 3; axis++)
                {
                    var curve = new AnimationCurve();
                    curve.AddKey(new Keyframe(0f, baseEuler[axis]));
                    curve.AddKey(new Keyframe(duration, baseEuler[axis]));
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(Transform),
                            "localEulerAnglesRaw." + "xyz"[axis]), curve);
                }
            }
        }

        private static AnimationClip Save(AnimationClip clip, string charName, string clipName)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string path = $"{OutDir}/{charName}_{clipName}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        // ==================== BONE DISCOVERY ====================

        private static Rig Discover(Transform root)
        {
            var rig = new Rig { Root = root };
            foreach (var t in root.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant().Replace("_", "").Replace(".", "").Replace(" ", "");
                bool left = n.Contains("left") || n.EndsWith("l") && !n.EndsWith("skull");
                bool right = n.Contains("right") || (!left && n.EndsWith("r"));

                if (rig.Hips == null && (n.Contains("hips") || n.Contains("pelvis"))) rig.Hips = t;
                else if (rig.Head == null && n.Contains("head")) rig.Head = t;
                else if (n.Contains("spine"))
                {
                    if (rig.Spine == null) rig.Spine = t;      // first spine up the chain
                    rig.Chest = t;                              // last spine seen = chest
                }
                else if (n.Contains("forearm") || n.Contains("lowerarm"))
                {
                    if (left && rig.ForeArmL == null) rig.ForeArmL = t;
                    else if (right && rig.ForeArmR == null) rig.ForeArmR = t;
                }
                else if (n.Contains("arm") && !n.Contains("hand"))
                {
                    if (left && rig.ArmL == null) rig.ArmL = t;
                    else if (right && rig.ArmR == null) rig.ArmR = t;
                }
                else if (n.Contains("upleg") || n.Contains("thigh") || n.Contains("upperleg"))
                {
                    if (left && rig.ThighL == null) rig.ThighL = t;
                    else if (right && rig.ThighR == null) rig.ThighR = t;
                }
                else if (n.Contains("leg") || n.Contains("calf") || n.Contains("shin"))
                {
                    if (left && rig.CalfL == null) rig.CalfL = t;
                    else if (right && rig.CalfR == null) rig.CalfR = t;
                }
            }
            return rig;
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
