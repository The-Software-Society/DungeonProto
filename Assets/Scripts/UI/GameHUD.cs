using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Abilities;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Multiplayer;
using AldenFall.Player;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// HUD orchestrator: health bar (with damage-lag ghost), target frame,
    /// ability slots, panel toggles (I inventory, K skills), pickup toasts.
    /// Built by HUDBuilder in the editor; this component drives it at runtime.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("Player Health")]
        public Image healthFill;
        public Image healthGhost;      // trails behind on damage for readable hits
        public TMP_Text healthText;

        [Header("Action Bar")]
        public AbilitySlotUI meleeSlot;   // docked "AUTO" chip, not a bar slot
        public AbilitySlotUI dodgeSlot;   // docked "TAPx2" chip
        public List<ActionBarSlotUI> barSlots = new List<ActionBarSlotUI>();

        [Header("Ability Icons (name → sprite, filled by HUDBuilder)")]
        public List<string> iconNames = new List<string>();
        public List<Sprite> iconSprites = new List<Sprite>();

        [Header("Target Frame")]
        public CanvasGroup targetGroup;
        public TMP_Text targetName;
        public TMP_Text targetLevel;
        public Image targetHealthFill;
        public Image targetHealthGhost;

        [Header("Panels")]
        public SlidePanel inventoryPanel;
        public SlidePanel skillsPanel;

        [Header("Toasts")]
        public RectTransform toastRoot;

        [Header("Interaction")]
        public GameObject promptChip;      // "E — Fish" pill above the action bar
        public TMP_Text promptText;
        public GameObject castBarRoot;     // thin fishing progress bar
        public Image castBarFill;

        private Health playerHealth;
        private PlayerController playerController;
        private PlayerAbilities playerAbilities;
        private PlayerMelee playerMelee;
        private ActionBar actionBar;
        private PlayerConsumables consumables;
        private PlayerInteractor interactor;
        private FishingController fishing;
        private TargetingSystem targeting;
        private Inventory inventory;
        private GameObject boundPlayer;

        private Health targetHealth;
        private float displayedHp = 1f, ghostHp = 1f;
        private float displayedTargetHp = 1f, ghostTargetHp = 1f;

        private void Start()
        {
            TryBindPlayer();
            if (targetGroup != null) targetGroup.alpha = 0f;
        }

        private void OnDestroy()
        {
            if (actionBar != null)
                actionBar.OnChanged -= RebindBar;
            if (targeting != null)
                targeting.OnTargetChanged.RemoveListener(OnTargetChanged);
            if (inventory != null)
                inventory.OnItemAdded.RemoveListener(OnItemPickedUp);
        }

        private void WireAbilities()
        {
            if (meleeSlot != null && playerMelee != null)
            {
                meleeSlot.getCooldownPercent = () => playerMelee.CooldownPercent;
                meleeSlot.getCooldownRemaining = () => 0f; // sweep only, no number
            }
            if (dodgeSlot != null && playerController != null)
            {
                dodgeSlot.getCooldownPercent = playerController.GetDodgeCooldownPercent;
                dodgeSlot.getCooldownRemaining = playerController.GetDodgeCooldownRemaining;
            }
        }

        public Sprite IconFor(SlotBinding binding)
        {
            if (binding.IsEmpty) return null;
            if (binding.Kind == BindingKind.Item)
                return ItemVisualLibrary.Instance != null
                    ? ItemVisualLibrary.Instance.GetIcon(binding.Id)
                    : null;

            var def = AbilityDatabase.Get(binding.Id);
            if (def == null) return null;
            int idx = iconNames.IndexOf(def.IconSprite);
            return idx >= 0 && idx < iconSprites.Count ? iconSprites[idx] : null;
        }

        private void RebindBar()
        {
            for (int i = 0; i < barSlots.Count; i++)
            {
                var slot = barSlots[i];
                var binding = actionBar.GetSlot(i);
                bool combatSlot = i < ActionBar.CombatSlots;

                var icon = IconFor(binding);
                if (slot.visuals != null && slot.visuals.iconImage != null)
                {
                    slot.visuals.iconImage.enabled = !binding.IsEmpty;
                    slot.visuals.iconImage.sprite = icon;
                }

                if (binding.IsEmpty)
                {
                    if (slot.visuals != null)
                    {
                        slot.visuals.getCooldownPercent = null;
                        slot.visuals.getCooldownRemaining = null;
                        if (slot.visuals.cooldownOverlay != null) slot.visuals.cooldownOverlay.fillAmount = 0f;
                        if (slot.visuals.cooldownText != null) slot.visuals.cooldownText.text = "";
                    }
                    if (slot.countText != null) slot.countText.gameObject.SetActive(false);
                    continue;
                }

                if (binding.Kind == BindingKind.Ability)
                {
                    string id = binding.Id;
                    // Eating locks combat: the lockout sweep rides the combat slots
                    slot.visuals.getCooldownPercent = combatSlot
                        ? () => Mathf.Max(
                            playerAbilities != null ? playerAbilities.GetCooldown01(id) : 0f,
                            consumables != null ? consumables.Lockout01 : 0f)
                        : (System.Func<float>)(() =>
                            playerAbilities != null ? playerAbilities.GetCooldown01(id) : 0f);
                    slot.visuals.getCooldownRemaining = () =>
                        playerAbilities != null ? playerAbilities.GetCooldownRemaining(id) : 0f;
                    if (slot.countText != null) slot.countText.gameObject.SetActive(false);
                }
                else // Item
                {
                    slot.visuals.getCooldownPercent = () =>
                        consumables != null ? consumables.Lockout01 : 0f;
                    slot.visuals.getCooldownRemaining = () =>
                        consumables != null ? consumables.LockoutRemaining : 0f;
                }
            }
        }

        private void Update()
        {
            if (playerHealth == null || boundPlayer == null)
                TryBindPlayer();

            // Panel toggling lives in PanelHotkeys (isolated from HUD exceptions)
            UpdateHealthBar();
            UpdateTargetFrame();
            UpdateBarCounts();
            UpdateInteraction();
        }

        private void TryBindPlayer()
        {
            var player = LocalPlayerLocator.Find();
            if (player == null || player == boundPlayer) return;

            if (actionBar != null)
                actionBar.OnChanged -= RebindBar;
            if (targeting != null)
                targeting.OnTargetChanged.RemoveListener(OnTargetChanged);
            if (inventory != null)
                inventory.OnItemAdded.RemoveListener(OnItemPickedUp);

            boundPlayer = player;

            playerHealth = player.GetComponent<Health>();
            playerController = player.GetComponent<PlayerController>();
            playerAbilities = player.GetComponent<PlayerAbilities>();
            playerMelee = player.GetComponent<PlayerMelee>();
            actionBar = player.GetComponent<ActionBar>();
            consumables = player.GetComponent<PlayerConsumables>();
            interactor = player.GetComponent<PlayerInteractor>();
            fishing = player.GetComponent<FishingController>();
            targeting = player.GetComponent<TargetingSystem>();
            inventory = player.GetComponent<Inventory>();

            if (targeting != null)
                targeting.OnTargetChanged.AddListener(OnTargetChanged);
            if (inventory != null)
                inventory.OnItemAdded.AddListener(OnItemPickedUp);

            WireAbilities();
            if (actionBar != null)
            {
                foreach (var slot in barSlots)
                    slot.Bind(actionBar, this);
                actionBar.OnChanged += RebindBar;
                RebindBar();
            }
        }

        private void UpdateInteraction()
        {
            if (promptChip != null)
            {
                string prompt = interactor != null ? interactor.CurrentPrompt : null;
                bool fishingNow = fishing != null && fishing.IsFishing;
                bool show = prompt != null && !fishingNow;
                if (promptChip.activeSelf != show) promptChip.SetActive(show);
                if (show && promptText != null) promptText.text = prompt;
            }

            if (castBarRoot != null)
            {
                bool show = fishing != null && fishing.IsFishing;
                if (castBarRoot.activeSelf != show) castBarRoot.SetActive(show);
                if (show && castBarFill != null)
                    castBarFill.fillAmount = fishing.Tick01;
            }
        }

        private void UpdateBarCounts()
        {
            if (actionBar == null || inventory == null) return;
            for (int i = 0; i < barSlots.Count; i++)
            {
                var slot = barSlots[i];
                if (slot.countText == null) continue;
                var binding = actionBar.GetSlot(i);
                if (binding.Kind != BindingKind.Item) continue;

                int count = inventory.CountOf(binding.Id);
                slot.countText.gameObject.SetActive(true);
                slot.countText.text = count.ToString();
                slot.countText.color = count > 0 ? UITheme.TextMain : UITheme.HpRed;
            }
        }

        private void UpdateHealthBar()
        {
            if (playerHealth == null || healthFill == null) return;

            float target = playerHealth.HealthPercent;
            displayedHp = Mathf.Lerp(displayedHp, target, 14f * Time.deltaTime);
            // Ghost trails slowly downward, snaps up on heal
            ghostHp = target > ghostHp ? target : Mathf.Lerp(ghostHp, target, 3.2f * Time.deltaTime);

            healthFill.fillAmount = displayedHp;
            healthFill.color = UITheme.HealthColor(displayedHp);
            if (healthGhost != null) healthGhost.fillAmount = ghostHp;

            if (healthText != null)
                healthText.text = $"{Mathf.CeilToInt(playerHealth.CurrentHealth)} / {Mathf.CeilToInt(playerHealth.MaxHealth)}";
        }

        private void OnTargetChanged(Transform newTarget)
        {
            targetHealth = newTarget != null ? newTarget.GetComponent<Health>() : null;

            if (targetHealth != null)
            {
                displayedTargetHp = ghostTargetHp = targetHealth.HealthPercent;

                if (targetName != null)
                {
                    var identity = newTarget.GetComponent<AI.MobIdentity>();
                    targetName.text = identity != null ? identity.DisplayName : newTarget.name;
                    if (targetLevel != null)
                        targetLevel.text = identity != null ? $"Lv {identity.CombatLevel}" : "";
                }

                if (targetGroup != null)
                {
                    StopAllCoroutines();
                    StartCoroutine(UIFx.FadeTo(targetGroup, 1f, 0.15f));
                    StartCoroutine(UIFx.ScaleTo(targetGroup.transform, Vector3.one, 0.2f, back: true));
                    targetGroup.transform.localScale = Vector3.one * 0.92f;
                }
            }
            else if (targetGroup != null)
            {
                StartCoroutine(UIFx.FadeTo(targetGroup, 0f, 0.15f));
            }
        }

        private void UpdateTargetFrame()
        {
            if (targetHealth == null || targetHealthFill == null) return;

            float target = targetHealth.HealthPercent;
            displayedTargetHp = Mathf.Lerp(displayedTargetHp, target, 14f * Time.deltaTime);
            ghostTargetHp = target > ghostTargetHp ? target : Mathf.Lerp(ghostTargetHp, target, 3.2f * Time.deltaTime);

            targetHealthFill.fillAmount = displayedTargetHp;
            targetHealthFill.color = UITheme.HealthColor(displayedTargetHp);
            if (targetHealthGhost != null) targetHealthGhost.fillAmount = ghostTargetHp;
        }

        private void OnItemPickedUp(string itemId)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null || toastRoot == null) return;
            StartCoroutine(ShowToast(def));
        }

        private IEnumerator ShowToast(ItemDef def)
        {
            var obj = new GameObject("PickupToast");
            obj.transform.SetParent(toastRoot, false);
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.text = $"<color=#{ColorUtility.ToHtmlStringRGB(def.RarityColor)}>{def.Name}</color> acquired";
            text.fontSize = 18;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color(0, 0, 0, 0.85f);
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(320, 26);

            // Scale-in only — position belongs to the toastRoot layout group,
            // so same-frame toasts (starter sword + fishing pole) stack cleanly.
            text.transform.localScale = Vector3.one * 0.7f;
            StartCoroutine(UIFx.ScaleTo(text.transform, Vector3.one, 0.25f, back: true));

            yield return new WaitForSeconds(2.4f);

            float elapsed = 0f;
            while (elapsed < 0.4f)
            {
                elapsed += Time.deltaTime;
                if (text == null) yield break;
                text.alpha = 1f - elapsed / 0.4f;
                yield return null;
            }
            if (text != null) Destroy(text.gameObject);
        }
    }
}
