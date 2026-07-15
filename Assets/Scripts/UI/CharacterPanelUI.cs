using System.Collections.Generic;
using UnityEngine;
using TMPro;
using AldenFall.Items;
using AldenFall.Multiplayer;
using AldenFall.Stats;

namespace AldenFall.UI
{
    /// <summary>
    /// Character panel: 5-slot paper doll (click to unequip), bag grid
    /// (click to equip), and an OSRS-style equipment bonuses readout that
    /// reads straight from the StatBlock, one source of truth.
    /// </summary>
    public class CharacterPanelUI : MonoBehaviour
    {
        [System.Serializable]
        public class EquipSlotEntry
        {
            public EquipSlot slot;
            public ItemSlotUI ui;
        }

        public List<EquipSlotEntry> equipSlots = new List<EquipSlotEntry>();
        public List<ItemSlotUI> bagSlots = new List<ItemSlotUI>();
        public TMP_Text countText;
        public TMP_Text bonusText;
        public TMP_Text goldText;

        // Small "<amount>g" chip under the bag grid, created at runtime so it
        // shows up even in scenes built before it existed (no rebuild needed).
        private TMP_Text bagGoldText;

        private Inventory inventory;
        private Equipment equipment;
        private StatBlock stats;
        private Wallet wallet;
        private GameObject boundPlayer;

        private SlidePanel slidePanel;
        private float bindTime;
        // Ignore auto-open during the spawn/save-restore settle window: StarterLoadout
        // and PlayerSave.Apply both fire OnEquipmentChanged as they rebuild gear, and
        // we don't want the panel flying open on login.
        private const float AutoOpenSettle = 2.5f;

        private void Start()
        {
            slidePanel = GetComponent<SlidePanel>();
            EnsureBagGold();
            EnsureHandLabels();
            TryBindPlayer();
        }

        /// <summary>
        /// Renames the baked paper-doll labels for the two-hand rework (WEAPON ->
        /// MAIN HAND, FOCUS -> OFF HAND) at runtime, so scenes built before the
        /// rework read correctly without a rebuild.
        /// </summary>
        private void EnsureHandLabels()
        {
            RelabelDollSlot("DollLabel_Weapon", "MAIN HAND");
            RelabelDollSlot("DollLabel_Focus", "OFF HAND");
        }

        private void RelabelDollSlot(string objectName, string label)
        {
            var child = transform.Find(objectName);
            var text = child != null ? child.GetComponent<TMP_Text>() : null;
            if (text == null) return;
            text.text = label;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            // Widen the baked 70px rect so the longer names fit on one line.
            text.rectTransform.sizeDelta = new Vector2(130f, text.rectTransform.sizeDelta.y);
        }

        /// <summary>Builds the gold readout in the bag corner if the scene doesn't have one.</summary>
        private void EnsureBagGold()
        {
            if (bagGoldText != null) return;

            var obj = new GameObject("BagGold");
            var rect = obj.AddComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-22f, 16f);
            rect.sizeDelta = new Vector2(180f, 22f);

            bagGoldText = obj.AddComponent<TextMeshProUGUI>();
            bagGoldText.fontSize = 14;
            bagGoldText.fontStyle = FontStyles.Bold;
            bagGoldText.color = UITheme.TextGold;
            bagGoldText.alignment = TextAlignmentOptions.MidlineRight;
            bagGoldText.raycastTarget = false;
            bagGoldText.text = "0g";
        }

        private void Update()
        {
            if (inventory == null || boundPlayer == null)
                TryBindPlayer();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void TryBindPlayer()
        {
            var player = LocalPlayerLocator.Find();
            if (player == null || player == boundPlayer) return;

            Unbind();
            boundPlayer = player;

            inventory = player.GetComponent<Inventory>();
            equipment = player.GetComponent<Equipment>();
            stats = player.GetComponent<StatBlock>();
            wallet = player.GetComponent<Wallet>();
            bindTime = Time.unscaledTime;

            if (inventory != null) inventory.OnChanged.AddListener(Refresh);
            if (equipment != null) equipment.OnEquipmentChanged.AddListener(OnEquipmentChanged);
            if (wallet != null) wallet.OnChanged.AddListener(OnGoldChanged);
            Refresh();
        }

        /// <summary>
        /// Gear changed: refresh the readout and pop the character panel open so the
        /// new item is visible (Jon: "auto-open equipment when armor/weapon changes").
        /// Suppressed during the login settle window so spawn/save-restore re-equips
        /// don't fling it open.
        /// </summary>
        private void OnEquipmentChanged()
        {
            Refresh();
            if (slidePanel != null && Time.unscaledTime - bindTime > AutoOpenSettle)
                slidePanel.Set(true);
        }

        private void Unbind()
        {
            if (inventory != null) inventory.OnChanged.RemoveListener(Refresh);
            if (equipment != null) equipment.OnEquipmentChanged.RemoveListener(OnEquipmentChanged);
            if (wallet != null) wallet.OnChanged.RemoveListener(OnGoldChanged);
            inventory = null;
            equipment = null;
            stats = null;
            wallet = null;
            boundPlayer = null;
        }

        private void OnGoldChanged(int _) => RefreshGold();

        private void RefreshGold()
        {
            int gold = wallet != null ? wallet.Gold : 0;
            if (goldText != null)
                goldText.text = $"<color=#F2CC8C><b>Gold:</b> {gold}</color>";
            if (bagGoldText != null)
                bagGoldText.text = $"{gold}g";
        }

        public void Refresh()
        {
            if (inventory == null) return;

            foreach (var entry in equipSlots)
            {
                string id = equipment != null ? equipment.GetEquipped(entry.slot) : null;
                var slot = entry.slot;
                entry.ui.Bind(id, true, _ => equipment?.Unequip(slot));
            }

            var slots = inventory.Slots;
            for (int i = 0; i < bagSlots.Count; i++)
            {
                var entry = i < slots.Count ? slots[i] : null;
                bagSlots[i].Bind(entry?.ItemId, false, TryEquip, entry?.Count ?? 1);
            }

            if (countText != null)
                countText.text = $"{slots.Count} / {Inventory.Capacity}";

            RefreshGold();
            RefreshBonuses();
        }

        private void RefreshBonuses()
        {
            if (bonusText == null || stats == null) return;

            // <pos> pins every value to one column so the block reads as a table.
            string Line(string label, StatType type, string suffix = "")
            {
                float bonus = stats.GetModifierTotal(type);
                string color = bonus > 0 ? "#7FD98A" : "#8A8494";
                string value = bonus > 0 ? $"+{bonus:0.#}" : "-";
                return $"<color=#A79FB0>{label}</color><pos=74%><color={color}>{value}{suffix}</color>";
            }

            bonusText.text =
                Line("Weapon Power", StatType.WeaponPower) + "\n" +
                Line("Spell Power", StatType.SpellPower) + "\n" +
                Line("Armor", StatType.Armor) + "\n" +
                Line("Strength Bonus", StatType.Strength) + "\n" +
                Line("Attack Speed", StatType.AttackSpeed) + "\n" +
                Line("Move Speed", StatType.MoveSpeed);
        }

        private void TryEquip(string itemId)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null || def.Slot == EquipSlot.None) return;
            equipment?.Equip(itemId);
        }
    }
}
