using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;

namespace AldenFall.UI
{
    /// <summary>Reusable item tooltip that follows the cursor.</summary>
    public class TooltipUI : MonoBehaviour
    {
        public static TooltipUI Instance { get; private set; }

        public RectTransform panel;
        public TMP_Text nameText;
        public TMP_Text statText;
        public TMP_Text descText;
        public Image border;

        private CanvasGroup group;
        private RectTransform canvasRect;

        private void Awake()
        {
            Instance = this;
            group = panel.GetComponent<CanvasGroup>();
            if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            canvasRect = transform as RectTransform;
        }

        /// <summary>Ability tooltip for the skill-tree tiles and action bar.</summary>
        public void Show(Abilities.AbilityDef ability, int rank, bool onActionBar = false)
        {
            if (ability == null) return;

            nameText.text = $"{ability.Name}  <size=11><color=#8A8494>Rank {rank}/{Abilities.AbilityMastery.MaxRank}</color></size>";
            nameText.color = UITheme.TextGold;

            var parts = new System.Collections.Generic.List<string>();
            if (ability.Cooldown > 0f) parts.Add($"{ability.Cooldown:0.#}s cooldown");
            if (ability.RequiredWeapon != Abilities.WeaponClass.Any)
                parts.Add($"requires {ability.RequiredWeapon.ToString().ToLowerInvariant()}");
            float bonus = (rank - 1) * 8f;
            if (bonus > 0f) parts.Add($"+{bonus:0}% mastery damage");
            statText.text = string.Join("   ", parts);

            string hint = onActionBar ? "Drag off the bar to unbind" : "Drag to the action bar";
            descText.text = $"{ability.Description}\n<color=#9E96A8><i>{hint}</i></color>";

            if (border != null)
            {
                var c = UITheme.Gold;
                c.a = 0.5f;
                border.color = c;
            }

            group.alpha = 1f;
            Reposition();
        }

        public void Show(ItemDef item, bool equipped, int plus = 0)
        {
            if (item == null) return;

            nameText.text = $"{ItemPlus.DisplayName(item, plus)}  <size=11><color=#8A8494>{ItemDatabase.SlotName(item.Slot)}</color></size>";
            nameText.color = ItemPlus.PlusColor(item, plus);

            var parts = new System.Collections.Generic.List<string>();
            foreach (var (type, value) in ItemPlus.ScaledBonuses(item, plus))
            {
                string label = type switch
                {
                    Stats.StatType.WeaponPower => "power",
                    Stats.StatType.Armor => "armor",
                    Stats.StatType.Strength => "strength",
                    Stats.StatType.AttackSpeed => "atk speed",
                    Stats.StatType.MoveSpeed => "move speed",
                    Stats.StatType.SpellPower => "spell power",
                    _ => type.ToString().ToLowerInvariant(),
                };
                parts.Add($"+{value:0.##} {label}");
            }
            if (item.IsConsumable)
                parts.Add($"heals {item.HealAmount:0} hp");
            string stats = string.Join("   ", parts);
            statText.text = equipped ? $"{stats}   <color=#E8B872>[equipped]</color>" : stats;

            string action = equipped ? "Click to unequip" : "Click to equip";
            descText.text = item.Slot != EquipSlot.None
                ? $"{item.Description}\n<color=#9E96A8><i>{action}</i></color>"
                : item.Description;

            if (border != null)
            {
                var c = ItemPlus.PlusColor(item, plus);
                c.a = 0.5f;
                border.color = c;
            }

            group.alpha = 1f;
            Reposition();
        }

        public void Hide()
        {
            group.alpha = 0f;
        }

        private void Update()
        {
            if (group.alpha > 0f) Reposition();
        }

        private void Reposition()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, Input.mousePosition, null, out Vector2 local);
            // Offset so the cursor never covers the tooltip; clamp on screen
            Vector2 half = canvasRect.sizeDelta * 0.5f;
            Vector2 size = panel.sizeDelta;
            Vector2 pos = local + new Vector2(18f, -14f);
            pos.x = Mathf.Min(pos.x, half.x - size.x - 8f);
            pos.y = Mathf.Max(pos.y, -half.y + size.y + 8f);
            panel.anchoredPosition = pos;
        }
    }
}
