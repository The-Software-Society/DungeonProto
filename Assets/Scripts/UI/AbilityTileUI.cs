using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using AldenFall.Abilities;
using AldenFall.Items;
using AldenFall.Multiplayer;

namespace AldenFall.UI
{
    /// <summary>
    /// One ability tile in the skill-tree panel: icon, name, cooldown, five
    /// mastery rank pips + progress fill, greyed out when the required weapon
    /// isn't equipped. Drag it onto the action bar to bind (slots 1-5).
    /// </summary>
    public class AbilityTileUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public string abilityId;
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text cooldownText;
        public TMP_Text requiresText;
        public Image[] rankPips = new Image[0];
        public Image masteryFill;
        public CanvasGroup group;

        private AbilityMastery mastery;
        private PlayerAbilities abilities;
        private GameHUD hud;

        private void Start()
        {
            TryBindPlayer();
            hud = FindFirstObjectByType<GameHUD>();
        }

        private void Update()
        {
            if (mastery == null || abilities == null)
                TryBindPlayer();

            var def = AbilityDatabase.Get(abilityId);
            if (def == null) return;

            bool usable = abilities == null || abilities.HasRequiredWeapon(def.RequiredWeapon);
            if (group != null)
                group.alpha = usable ? 1f : 0.45f;
            if (requiresText != null)
                requiresText.gameObject.SetActive(!usable);

            if (mastery != null)
            {
                int rank = mastery.RankFor(abilityId);
                for (int i = 0; i < rankPips.Length; i++)
                {
                    if (rankPips[i] == null) continue;
                    rankPips[i].color = i < rank
                        ? UITheme.Gold
                        : new Color(1f, 1f, 1f, 0.14f);
                }
                if (masteryFill != null)
                    masteryFill.fillAmount = mastery.MasteryProgress01(abilityId);
            }
        }

        private void TryBindPlayer()
        {
            var player = LocalPlayerLocator.Find();
            if (player == null) return;

            mastery = player.GetComponent<AbilityMastery>();
            abilities = player.GetComponent<PlayerAbilities>();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            var def = AbilityDatabase.Get(abilityId);
            if (def == null) return;
            TooltipUI.Instance?.Show(def, mastery != null ? mastery.RankFor(abilityId) : 1);
        }

        public void OnPointerExit(PointerEventData e)
        {
            TooltipUI.Instance?.Hide();
        }

        // ---------- drag source (bind onto the action bar) ----------

        public void OnBeginDrag(PointerEventData e)
        {
            if (DragController.Instance == null) return;

            DragController.Instance.Begin(new DragController.DragPayload
            {
                Kind = BindingKind.Ability,
                Id = abilityId,
                FromBarIndex = -1,
            }, hud != null ? hud.IconFor(SlotBinding.Ability(abilityId)) : null, Color.white);
            TooltipUI.Instance?.Hide();
        }

        public void OnDrag(PointerEventData e)
        {
            DragController.Instance?.UpdateGhost(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (DragController.Instance != null && DragController.Instance.IsDragging)
                DragController.Instance.End();
        }
    }
}
