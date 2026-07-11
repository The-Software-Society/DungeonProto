using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.Cinematics
{
    [System.Serializable]
    public class CutsceneStep
    {
        public string type;      // camera | fade | subtitle | anim | move | face | wait
        public float[] pos;      // camera/move world position [x,y,z]
        public float[] lookAt;   // camera aim point / face target [x,y,z]
        public string text;      // subtitle text
        public string actor;     // "player" or a scene object name
        public string state;     // animator state to CrossFade into
        public float seconds;    // duration (0 = instant cut)
        public float target;     // fade target alpha 0..1
        public bool blocking = true;
    }

    [System.Serializable]
    public class CutsceneData
    {
        public string id;
        public bool letterbox = true;
        public CutsceneStep[] steps = new CutsceneStep[0];
    }

    /// <summary>
    /// Data-driven cutscene playback: loads StreamingAssets/cutscenes/&lt;id&gt;.json
    /// and performs it — scripted camera, letterbox + subtitles, fades, actor
    /// mocap states and movement. The authoring loop is "describe the scene →
    /// the script gets written → replay in-game" (edit JSON, no rebuild).
    /// SPACE skips. Player control is locked during playback and restored after.
    /// </summary>
    public class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }

        /// <summary>True while a scene plays — EnemyAI and combat systems pause on this.</summary>
        public static bool CombatPaused { get; private set; }

        public bool IsPlaying { get; private set; }

        private Camera cam;
        private Behaviour cameraController;
        private readonly List<Behaviour> lockedComponents = new List<Behaviour>();

        // Overlay UI
        private GameObject overlayRoot;
        private Image fadeImage;
        private TMP_Text subtitleText;
        private RectTransform barTop, barBottom;
        private Coroutine playRoutine;

        private void Awake()
        {
            Instance = this;
        }

        public bool Play(string cutsceneId)
        {
            if (IsPlaying) return false;

            string path = Path.Combine(Application.streamingAssetsPath, "cutscenes", cutsceneId + ".json");
            if (!File.Exists(path))
            {
                Debug.LogWarning($"CutsceneDirector: no script at {path}");
                return false;
            }

            var data = JsonUtility.FromJson<CutsceneData>(File.ReadAllText(path));
            return Play(data);
        }

        /// <summary>Play a code-built scene (boss rage, quest beats with dynamic positions).</summary>
        public bool Play(CutsceneData data)
        {
            if (IsPlaying) return false;
            if (data == null || data.steps == null || data.steps.Length == 0) return false;

            playRoutine = StartCoroutine(Run(data));
            return true;
        }

        private void Update()
        {
            // The weapon-choice panel is a mandatory beat — no skipping past it.
            if (UI.WeaponChoicePanel.IsOpen) return;

            if (IsPlaying && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)))
            {
                if (playRoutine != null) StopCoroutine(playRoutine);
                StopAllCoroutines();
                Finish();
            }
        }

        // ==================== PLAYBACK ====================

        private IEnumerator Run(CutsceneData data)
        {
            IsPlaying = true;
            TakeControl(data.letterbox);

            foreach (var step in data.steps)
            {
                var routine = Execute(step);
                if (routine == null) continue;
                if (step.blocking) yield return StartCoroutine(routine);
                else StartCoroutine(routine);
            }

            Finish();
        }

        private IEnumerator Execute(CutsceneStep step)
        {
            switch (step.type)
            {
                case "camera": return CameraStep(step);
                case "fade": return FadeStep(step);
                case "subtitle": return SubtitleStep(step);
                case "anim": AnimStep(step); return null;
                case "move": return MoveStep(step);
                case "face": FaceStep(step); return null;
                case "wait": return WaitStep(step.seconds);
                case "choice": return ChoiceStep();
                default:
                    Debug.LogWarning($"CutsceneDirector: unknown step '{step.type}'");
                    return null;
            }
        }

        private IEnumerator CameraStep(CutsceneStep step)
        {
            Vector3 targetPos = V3(step.pos, cam.transform.position);
            Quaternion targetRot = step.lookAt != null
                ? Quaternion.LookRotation(V3(step.lookAt, targetPos + cam.transform.forward) - targetPos)
                : cam.transform.rotation;

            if (step.seconds <= 0f)
            {
                cam.transform.SetPositionAndRotation(targetPos, targetRot);
                yield break;
            }

            Vector3 fromPos = cam.transform.position;
            Quaternion fromRot = cam.transform.rotation;
            float elapsed = 0f;
            while (elapsed < step.seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / step.seconds));
                cam.transform.SetPositionAndRotation(
                    Vector3.Lerp(fromPos, targetPos, t),
                    Quaternion.Slerp(fromRot, targetRot, t));
                yield return null;
            }
        }

        private IEnumerator FadeStep(CutsceneStep step)
        {
            float from = fadeImage.color.a;
            float elapsed = 0f;
            if (step.seconds <= 0f)
            {
                SetFade(step.target);
                yield break;
            }
            while (elapsed < step.seconds)
            {
                elapsed += Time.deltaTime;
                SetFade(Mathf.Lerp(from, step.target, elapsed / step.seconds));
                yield return null;
            }
            SetFade(step.target);
        }

        private IEnumerator SubtitleStep(CutsceneStep step)
        {
            subtitleText.text = step.text ?? "";
            float shown = Mathf.Max(0.5f, step.seconds);
            yield return new WaitForSeconds(shown);
            if (subtitleText != null && subtitleText.text == step.text)
                subtitleText.text = "";
        }

        private void AnimStep(CutsceneStep step)
        {
            var actor = ResolveActor(step.actor);
            var animator = actor != null ? actor.GetComponentInChildren<Animator>() : null;
            if (animator != null && !string.IsNullOrEmpty(step.state))
                animator.CrossFadeInFixedTime(step.state, 0.2f);
        }

        private IEnumerator MoveStep(CutsceneStep step)
        {
            var actor = ResolveActor(step.actor);
            if (actor == null) yield break;

            Vector3 from = actor.transform.position;
            Vector3 to = V3(step.pos, from);
            Vector3 dir = to - from; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                actor.transform.rotation = Quaternion.LookRotation(dir.normalized);

            // Walk cadence on the animator while moving
            var animator = actor.GetComponentInChildren<Animator>();
            animator?.SetFloat("Speed", 0.5f);

            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, step.seconds);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                actor.transform.position = Vector3.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            animator?.SetFloat("Speed", 0f);
        }

        private void FaceStep(CutsceneStep step)
        {
            var actor = ResolveActor(step.actor);
            if (actor == null || step.lookAt == null) return;
            Vector3 dir = V3(step.lookAt, actor.transform.position) - actor.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                actor.transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        private IEnumerator WaitStep(float seconds)
        {
            yield return new WaitForSeconds(seconds);
        }

        /// <summary>
        /// Aldric's gift: raise the starter-weapon panel over the player (the
        /// director lives on the player object) and hold the scene here until a
        /// path is picked. The panel grants + equips and persists the choice.
        /// </summary>
        private IEnumerator ChoiceStep()
        {
            bool chosen = false;
            UI.WeaponChoicePanel.Present(gameObject, () => chosen = true);
            while (!chosen)
                yield return null;
        }

        // ==================== CONTROL / RESTORE ====================

        private void TakeControl(bool letterbox)
        {
            cam = Camera.main;

            // Freeze the third-person rig; the director owns the camera now
            cameraController = cam != null ? cam.GetComponent<Player.ThirdPersonCamera>() : null;
            if (cameraController != null) cameraController.enabled = false;

            // Lock player input (components restored exactly as found)
            lockedComponents.Clear();
            foreach (var type in new[]
            {
                typeof(Player.PlayerController), typeof(Combat.PlayerMelee),
                typeof(Combat.TargetingSystem), typeof(Abilities.ActionBar),
                typeof(Abilities.PlayerAbilities), typeof(Player.PlayerInteractor),
            })
            {
                if (GetComponent(type) is Behaviour behaviour && behaviour.enabled)
                {
                    behaviour.enabled = false;
                    lockedComponents.Add(behaviour);
                }
            }

            // The whole battlefield holds its breath: mobs freeze (EnemyAI
            // checks CombatPaused), the player can't be killed mid-scene.
            CombatPaused = true;
            GetComponent<Combat.Health>()?.SetInvulnerable(true);

            BuildOverlay(letterbox);
        }

        private void Finish()
        {
            if (overlayRoot != null) Destroy(overlayRoot);
            overlayRoot = null;

            foreach (var behaviour in lockedComponents)
                if (behaviour != null) behaviour.enabled = true;
            lockedComponents.Clear();

            if (cameraController != null) cameraController.enabled = true; // rig snaps back

            CombatPaused = false;
            GetComponent<Combat.Health>()?.SetInvulnerable(false);

            IsPlaying = false;
            playRoutine = null;
        }

        // ==================== OVERLAY UI ====================

        private void BuildOverlay(bool letterbox)
        {
            overlayRoot = new GameObject("CutsceneOverlay");
            var canvas = overlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400; // above every HUD layer
            var scaler = overlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Fade plate
            fadeImage = NewImage("Fade", overlayRoot.transform, Color.black);
            Stretch(fadeImage.rectTransform);
            SetFade(0f);

            if (letterbox)
            {
                var top = NewImage("BarTop", overlayRoot.transform, Color.black);
                barTop = top.rectTransform;
                barTop.anchorMin = new Vector2(0, 1); barTop.anchorMax = Vector2.one;
                barTop.offsetMin = new Vector2(0, -130); barTop.offsetMax = Vector2.zero;

                var bottom = NewImage("BarBottom", overlayRoot.transform, Color.black);
                barBottom = bottom.rectTransform;
                barBottom.anchorMin = Vector2.zero; barBottom.anchorMax = new Vector2(1, 0);
                barBottom.offsetMin = Vector2.zero; barBottom.offsetMax = new Vector2(0, 130);
            }

            var subtitleObj = new GameObject("Subtitle");
            subtitleObj.transform.SetParent(overlayRoot.transform, false);
            subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
            var subRect = subtitleText.rectTransform;
            subRect.anchorMin = new Vector2(0.5f, 0f); subRect.anchorMax = new Vector2(0.5f, 0f);
            subRect.anchoredPosition = new Vector2(0, 168);
            subRect.sizeDelta = new Vector2(1200, 80);
            subtitleText.fontSize = 30;
            subtitleText.alignment = TextAlignmentOptions.Center;
            subtitleText.color = new Color(0.95f, 0.92f, 0.85f);
            subtitleText.text = "";

            var skipObj = new GameObject("SkipHint");
            skipObj.transform.SetParent(overlayRoot.transform, false);
            var skip = skipObj.AddComponent<TextMeshProUGUI>();
            var skipRect = skip.rectTransform;
            skipRect.anchorMin = skipRect.anchorMax = new Vector2(1f, 1f);
            skipRect.anchoredPosition = new Vector2(-110, -60);
            skipRect.sizeDelta = new Vector2(200, 30);
            skip.fontSize = 15;
            skip.alignment = TextAlignmentOptions.MidlineRight;
            skip.color = new Color(1f, 1f, 1f, 0.45f);
            skip.text = "SPACE — skip";
        }

        private void SetFade(float alpha)
        {
            var c = fadeImage.color;
            c.a = Mathf.Clamp01(alpha);
            fadeImage.color = c;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var image = obj.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ==================== HELPERS ====================

        private GameObject ResolveActor(string actorId)
        {
            if (string.IsNullOrEmpty(actorId) || actorId == "player")
                return gameObject; // director lives on the player
            return GameObject.Find(actorId);
        }

        private static Vector3 V3(float[] a, Vector3 fallback) =>
            a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;
    }
}
