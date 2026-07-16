using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.Networking;
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
        public string clip;      // video: StreamingAssets/video/<clip>; (audio uses 'voice')
        public string voice;     // subtitle: StreamingAssets/voice/<voice>.wav played with the caption
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
    /// and performs it, scripted camera, letterbox + subtitles, fades, actor
    /// mocap states and movement. The authoring loop is "describe the scene →
    /// the script gets written → replay in-game" (edit JSON, no rebuild).
    /// SPACE skips. Player control is locked during playback and restored after.
    /// </summary>
    public class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }

        /// <summary>True while a scene plays, EnemyAI and combat systems pause on this.</summary>
        public static bool CombatPaused { get; private set; }

        public bool IsPlaying { get; private set; }

        private Camera cam;
        private Behaviour cameraController;
        private readonly List<Behaviour> lockedComponents = new List<Behaviour>();
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        // Overlay UI
        private GameObject overlayRoot;
        private Image fadeImage;
        private TMP_Text subtitleText;
        private RectTransform barTop, barBottom;
        private Coroutine playRoutine;
        private AudioSource voiceSource;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();

        private IEnumerator Start()
        {
            // Every player object (local AND remote proxies) carries a director, so the
            // static Instance must be claimed only by the LOCAL player's director — else on
            // a host or a client with proxies it points at the wrong player and boss/quest
            // cutscenes drive the wrong camera. Offline/editor has no networking: claim it.
            var ni = GetComponent<NetworkIdentity>();
            if (ni == null || (!NetworkClient.active && !NetworkServer.active))
            {
                Instance = this;
                yield break;
            }
            // Networked: wait for Mirror to resolve ownership, then only the owner claims.
            for (int i = 0; i < 300 && !ni.isLocalPlayer; i++) yield return null;
            if (ni.isLocalPlayer) Instance = this;
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
            // The weapon-choice panel is a mandatory beat, no skipping past it.
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

            // try/finally (legal around yields) so a step that throws can never
            // strand the player with input locked, combat paused, and a black fade.
            try
            {
                foreach (var step in data.steps)
                {
                    var routine = Execute(step);
                    if (routine == null) continue;
                    if (step.blocking) yield return StartCoroutine(routine);
                    else StartCoroutine(routine);
                }
            }
            finally
            {
                Finish();
            }
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
                case "video": return VideoStep(step);
                case "hide": HideStep(step); return null;
                case "materialize": return MaterializeStep(step);
                default:
                    Debug.LogWarning($"CutsceneDirector: unknown step '{step.type}'");
                    return null;
            }
        }

        private IEnumerator CameraStep(CutsceneStep step)
        {
            if (cam == null) yield break; // no main camera, skip the shot rather than NRE mid-scene

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
            float shown = Mathf.Max(0.5f, step.seconds);

            // Voice-acted line: load + play the clip, and hold the caption for the whole clip so the
            // subtitle stays up while the actor speaks. Load happens before the caption shows so
            // words and voice appear together.
            if (!string.IsNullOrEmpty(step.voice))
            {
                string vpath = Path.Combine(Application.streamingAssetsPath, "voice", step.voice);
                using (var uwr = UnityWebRequestMultimedia.GetAudioClip(FileUrl(vpath), AudioType.WAV))
                {
                    yield return uwr.SendWebRequest();
                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        var clip = DownloadHandlerAudioClip.GetContent(uwr);
                        if (clip != null)
                        {
                            EnsureVoiceSource();
                            voiceSource.Stop();
                            voiceSource.clip = clip;
                            voiceSource.Play();
                            shown = Mathf.Max(shown, clip.length + 0.25f);
                        }
                    }
                    else Debug.LogWarning($"CutsceneDirector: voice '{step.voice}' failed: {uwr.error}");
                }
            }

            subtitleText.text = step.text ?? "";
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
            else
                Debug.LogWarning($"CutsceneDirector: anim step '{step.state}' skipped, " +
                                 (actor == null ? $"actor '{step.actor}' not found in scene." : "actor has no Animator."));
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

        /// <summary>
        /// Fullscreen video (the Veo entry cinematic). Plays StreamingAssets/video/&lt;clip&gt; on a
        /// RawImage above the letterbox with its own audio (Direct). Holds the scene until the clip
        /// ends. SPACE still skips the whole intro (Update -> Finish, which destroys the overlay).
        /// </summary>
        private IEnumerator VideoStep(CutsceneStep step)
        {
            if (string.IsNullOrEmpty(step.clip) || overlayRoot == null) yield break;
            string path = Path.Combine(Application.streamingAssetsPath, "video", step.clip);

            var go = new GameObject("CutsceneVideo");
            go.transform.SetParent(overlayRoot.transform, false);
            var raw = go.AddComponent<RawImage>();
            raw.color = Color.white;   // RawImage tints texture*color; black would render the video BLACK
            raw.raycastTarget = false;
            Stretch(raw.rectTransform);
            go.transform.SetAsLastSibling();   // cover the whole frame, above fade + letterbox

            var rt = new RenderTexture(1920, 1080, 0);
            var vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.source = VideoSource.Url;
            vp.url = FileUrl(path);
            vp.audioOutputMode = VideoAudioOutputMode.Direct;
            raw.texture = rt;

            bool done = false;
            vp.loopPointReached += _ => done = true;
            vp.errorReceived += (_, msg) => { Debug.LogWarning($"CutsceneDirector: video '{step.clip}' error: {msg}"); done = true; };

            bool prepared = false;
            vp.prepareCompleted += _ => prepared = true;
            vp.Prepare();
            float pw = 0f;
            while (!prepared && pw < 12f) { pw += Time.unscaledDeltaTime; yield return null; }

            vp.Play();
            // Fallback end-detection: loopPointReached can be missed if the clip is very short. The
            // wall-clock cap guarantees we never spin forever if Prepare failed (frameCount stays 0)
            // or the platform can't decode the file, the scene just continues to the reveal.
            float watchdog = 0f;
            float maxPlay = (vp.frameCount > 0 && vp.frameRate > 0.01f)
                ? (float)vp.frameCount / (float)vp.frameRate + 2f : 40f;
            while (!done)
            {
                if (vp.frameCount > 0 && vp.frame >= (long)vp.frameCount - 1) break;
                watchdog += Time.unscaledDeltaTime;
                if (watchdog > maxPlay) break;
                yield return null;
            }

            vp.Stop();
            raw.texture = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
            Destroy(go);
        }

        /// <summary>Hide an actor's whole avatar (all renderers) so it can materialise in later.</summary>
        private void HideStep(CutsceneStep step)
        {
            var actor = ResolveActor(step.actor);
            if (actor == null) return;
            foreach (var r in actor.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                hiddenRenderers.Add(r);
            }
        }

        /// <summary>Re-show the hidden actor renderers (used by materialise + as a Finish safety net).</summary>
        private void RevealHidden()
        {
            foreach (var r in hiddenRenderers)
                if (r != null) r.enabled = true;
            hiddenRenderers.Clear();
        }

        /// <summary>
        /// The isekai arrival: a column of rising golden embers + a flare of light at the actor,
        /// and partway through the burst the (hidden) avatar snaps back into being. "Particalizes
        /// in front of Aldric."
        /// </summary>
        private IEnumerator MaterializeStep(CutsceneStep step)
        {
            var actor = ResolveActor(step.actor);
            Vector3 pos = actor != null ? actor.transform.position : V3(step.pos, Vector3.zero);
            float dur = Mathf.Max(0.8f, step.seconds);

            var fx = new GameObject("MaterializeFx");
            fx.transform.position = pos;

            var ps = fx.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = dur; main.loop = false;
            main.startLifetime = 1.6f; main.startSpeed = 2.6f;
            main.startSize = 0.16f; main.startColor = new Color(1f, 0.62f, 0.24f);
            main.maxParticles = 600; main.gravityModifier = -0.05f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 260), new ParticleSystem.Burst(0.35f, 160) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f; shape.radius = 0.85f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.material = new Material(Shader.Find("Sprites/Default"));
            psr.material.color = new Color(1f, 0.7f, 0.3f);
            psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lo = new GameObject("MatFlash");
            lo.transform.SetParent(fx.transform, false);
            lo.transform.localPosition = Vector3.up * 1.0f;
            var light = lo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.72f, 0.36f);
            light.range = 12f; light.shadows = LightShadows.None;

            float t = 0f; bool revealed = false;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = t / dur;
                light.intensity = Mathf.Lerp(7f, 0f, k) * (1f - Mathf.Abs(k - 0.15f));
                if (!revealed && k >= 0.4f) { RevealHidden(); revealed = true; }
                yield return null;
            }
            if (!revealed) RevealHidden();
            Destroy(fx, 2f);
        }

        private void EnsureVoiceSource()
        {
            if (voiceSource != null) return;
            var go = new GameObject("CutsceneVoice");
            go.transform.SetParent(transform, false);
            voiceSource = go.AddComponent<AudioSource>();
            voiceSource.spatialBlend = 0f;   // 2D narration, always full volume
            voiceSource.playOnAwake = false;
        }

        // StreamingAssets file -> URL for UnityWebRequest / VideoPlayer (desktop paths are absolute).
        private static string FileUrl(string absolutePath)
        {
            if (absolutePath.Contains("://")) return absolutePath;   // already a URL (Android jar:)
            return "file://" + absolutePath;
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

            HideGameplayHud();
            BuildOverlay(letterbox);
        }

        /// <summary>
        /// The camera is telling a story, the HUD gets out of the way. Disables
        /// every root gameplay canvas (health, action bar, quest tracker, panels,
        /// nameplates…) and remembers exactly which ones were on so Finish can
        /// restore them. The director's own overlay sits at sortingOrder 400 and
        /// is built after this, so it's never caught.
        /// </summary>
        private void HideGameplayHud()
        {
            hiddenCanvases.Clear();
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.enabled || !canvas.isRootCanvas) continue;
                if (canvas.sortingOrder >= 400) continue; // cutscene-layer overlays keep drawing
                if (overlayRoot != null && canvas.gameObject == overlayRoot) continue;

                canvas.enabled = false;
                hiddenCanvases.Add(canvas);
            }
        }

        private void Finish()
        {
            // Never strand the player invisible or with voice still talking if a scene is skipped mid-way.
            RevealHidden();
            if (voiceSource != null) { Destroy(voiceSource.gameObject); voiceSource = null; }

            if (overlayRoot != null) Destroy(overlayRoot);
            overlayRoot = null;

            foreach (var behaviour in lockedComponents)
                if (behaviour != null) behaviour.enabled = true;
            lockedComponents.Clear();

            // Bring back exactly the canvases we hid (skip any destroyed mid-scene)
            foreach (var canvas in hiddenCanvases)
                if (canvas != null) canvas.enabled = true;
            hiddenCanvases.Clear();

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

                // 170 (not symmetric 130): tall enough to fully cover the HP bar +
                // action bar, so the HUD can't poke through under the subtitle.
                var bottom = NewImage("BarBottom", overlayRoot.transform, Color.black);
                barBottom = bottom.rectTransform;
                barBottom.anchorMin = Vector2.zero; barBottom.anchorMax = new Vector2(1, 0);
                barBottom.offsetMin = Vector2.zero; barBottom.offsetMax = new Vector2(0, 170);
            }

            var subtitleObj = new GameObject("Subtitle");
            subtitleObj.transform.SetParent(overlayRoot.transform, false);
            subtitleText = subtitleObj.AddComponent<TextMeshProUGUI>();
            var subRect = subtitleText.rectTransform;
            subRect.anchorMin = new Vector2(0.5f, 0f); subRect.anchorMax = new Vector2(0.5f, 0f);
            subRect.anchoredPosition = new Vector2(0, 212);
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
            skip.text = "SPACE, skip";
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
