using AldenFall.Player;
using AldenFall.Abilities;
using AldenFall.Items;
using AldenFall.Progression;
using AldenFall.Stats;
using Mirror;
using TMPro;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    [RequireComponent(typeof(CharacterAppearance))]
    public class NetworkPlayerAvatar : NetworkBehaviour
    {
        [Header("Remote Smoothing")]
        [SerializeField] private float positionLerp = 14f;
        [SerializeField] private float rotationLerp = 16f;

        [SyncVar(hook = nameof(OnNameChanged))] private string syncedName = "Player";
        [SyncVar(hook = nameof(OnIntProfileChanged))] private int syncedGender;
        [SyncVar(hook = nameof(OnIntProfileChanged))] private int syncedSkinTone;
        [SyncVar(hook = nameof(OnIntProfileChanged))] private int syncedHairStyle = 1;
        [SyncVar(hook = nameof(OnIntProfileChanged))] private int syncedHairColor;

        [SyncVar] private Vector3 syncedPosition;
        [SyncVar] private Quaternion syncedRotation = Quaternion.identity;
        [SyncVar] private float syncedAnimSpeed;
        [SyncVar] private bool syncedRunning;
        [SyncVar] private bool syncedSwimming;

        private CharacterAppearance appearance;
        private PlayerController controller;
        private Animator animator;
        private TMP_Text nameplate;
        private float nextSendTime;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
        private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");

        private void Awake()
        {
            EnsureGameplayComponents();
            appearance = GetComponent<CharacterAppearance>();
            controller = GetComponent<PlayerController>();
            animator = GetComponentInChildren<Animator>();
            gameObject.tag = "Untagged";
            gameObject.name = "RemotePlayer";
        }

        public override void OnStartServer()
        {
            syncedPosition = transform.position;
            syncedRotation = transform.rotation;
        }

        public override void OnStartClient()
        {
            ApplyProfile();
            CreateNameplate();
            SetLocalAuthorityVisuals(false);
        }

        public override void OnStartLocalPlayer()
        {
            EnsureGameplayComponents();
            gameObject.tag = "Player";
            gameObject.name = "LocalPlayer";
            SetLocalAuthorityVisuals(true);

            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam != null)
                cam.SetTarget(transform);

            if (nameplate != null)
                nameplate.color = new Color(0.9f, 1f, 0.75f);

            GetComponent<StarterLoadout>()?.EnsureGranted();
        }

        private void EnsureGameplayComponents()
        {
            Ensure<StatBlock>();
            Ensure<Skills>();
            Ensure<Combat.Health>();
            Ensure<Combat.StatusSystem>();
            Ensure<Combat.TargetingSystem>();
            Ensure<Combat.PlayerMelee>();
            Ensure<Combat.PlayerDefence>();
            Ensure<Inventory>();
            Ensure<Equipment>();
            Ensure<StarterLoadout>();
            Ensure<PlayerAbilities>();
            Ensure<AbilityMastery>();
            Ensure<ActionBar>();
            Ensure<Combat.CombatStyleController>();
            Ensure<PlayerConsumables>();
            Ensure<FishingController>();
            Ensure<CookingController>();
            Ensure<PlayerInteractor>();
        }

        private T Ensure<T>() where T : Component
        {
            var component = GetComponent<T>();
            return component != null ? component : gameObject.AddComponent<T>();
        }

        private void Update()
        {
            if (isLocalPlayer)
            {
                SendLocalState();
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, syncedPosition, positionLerp * Time.deltaTime);
                transform.rotation = Quaternion.Slerp(transform.rotation, syncedRotation, rotationLerp * Time.deltaTime);
                if (animator != null)
                {
                    animator.SetFloat(SpeedHash, syncedAnimSpeed, 0.1f, Time.deltaTime);
                    animator.SetBool(IsRunningHash, syncedRunning);
                    animator.SetBool(IsSwimmingHash, syncedSwimming);
                }
            }

            if (nameplate != null && Camera.main != null)
                nameplate.transform.rotation = Camera.main.transform.rotation;
        }

        [Server]
        public void ServerApplyProfile(AldenPlayerProfile profile)
        {
            syncedName = string.IsNullOrWhiteSpace(profile.playerName) ? "Player" : profile.playerName.Trim();
            syncedGender = Mathf.Clamp(profile.gender, 0, 1);
            syncedSkinTone = Mathf.Clamp(profile.skinTone, 0, CharacterAppearance.SkinTones.Length - 1);
            syncedHairStyle = Mathf.Clamp(profile.hairStyle, 0, CharacterAppearance.HairStyleNames.Length - 1);
            syncedHairColor = Mathf.Clamp(profile.hairColor, 0, CharacterAppearance.HairColors.Length - 1);
        }

        private void SendLocalState()
        {
            if (Time.time < nextSendTime) return;
            nextSendTime = Time.time + 0.05f;

            float speed = controller != null && controller.IsMoving
                ? (controller.IsRunning ? 1f : 0.5f)
                : 0f;
            CmdUpdateState(transform.position, transform.rotation, speed,
                controller != null && controller.IsRunning,
                controller != null && controller.IsSwimming);
        }

        [Command]
        private void CmdUpdateState(Vector3 position, Quaternion rotation, float animSpeed, bool running, bool swimming)
        {
            transform.position = position;
            transform.rotation = rotation;
            syncedPosition = position;
            syncedRotation = rotation;
            syncedAnimSpeed = Mathf.Clamp01(animSpeed);
            syncedRunning = running;
            syncedSwimming = swimming;
        }

        private void OnNameChanged(string _, string __)
        {
            ApplyProfile();
        }

        private void OnIntProfileChanged(int _, int __)
        {
            ApplyProfile();
        }

        private void ApplyProfile()
        {
            if (appearance == null)
                appearance = GetComponent<CharacterAppearance>();
            appearance?.ApplyNetworkSelection(syncedGender, syncedSkinTone, syncedHairStyle, syncedHairColor);
            if (nameplate != null)
                nameplate.text = syncedName;
        }

        private void SetLocalAuthorityVisuals(bool local)
        {
            if (local)
            {
                Enable<PlayerController>();
                Enable<Combat.PlayerMelee>();
                Enable<Abilities.PlayerAbilities>();
                Enable<Abilities.ActionBar>();
                Enable<Combat.TargetingSystem>();
                Enable<Items.PlayerConsumables>();
                Enable<FishingController>();
                Enable<CookingController>();
                Enable<PlayerInteractor>();
                Enable<PlayerDeath>();
                var characterController = GetComponent<CharacterController>();
                if (characterController != null)
                    characterController.enabled = true;

                var rendererList = GetComponentsInChildren<Renderer>(true);
                foreach (var r in rendererList)
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return;
            }

            Disable<PlayerController>();
            Disable<Combat.PlayerMelee>();
            Disable<Abilities.PlayerAbilities>();
            Disable<Abilities.ActionBar>();  // remote avatars must NOT read our number keys
            Disable<Combat.TargetingSystem>();
            Disable<Items.PlayerConsumables>();
            Disable<FishingController>();
            Disable<CookingController>();
            Disable<PlayerInteractor>();
            Disable<PlayerDeath>();

            var remoteCharacterController = GetComponent<CharacterController>();
            if (remoteCharacterController != null)
                remoteCharacterController.enabled = false;
        }

        private void Disable<T>() where T : Behaviour
        {
            var component = GetComponent<T>();
            if (component != null)
                component.enabled = false;
        }

        private void Enable<T>() where T : Behaviour
        {
            var component = GetComponent<T>();
            if (component != null)
                component.enabled = true;
        }

        private void CreateNameplate()
        {
            var canvasObj = new GameObject("Nameplate");
            canvasObj.transform.SetParent(transform, false);
            canvasObj.transform.localPosition = Vector3.up * 2.35f;
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.transform.localScale = Vector3.one * 0.012f;

            nameplate = canvasObj.AddComponent<TextMeshProUGUI>();
            nameplate.text = syncedName;
            nameplate.alignment = TextAlignmentOptions.Center;
            nameplate.fontSize = 18f;
            nameplate.color = isLocalPlayer ? new Color(0.9f, 1f, 0.75f) : new Color(0.75f, 0.95f, 1f);
            nameplate.raycastTarget = false;
            nameplate.rectTransform.sizeDelta = new Vector2(220f, 32f);
        }
    }
}
