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
    /// reads straight from the StatBlock — one source of truth.
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

        private Inventory inventory;
        private Equipment equipment;
        private StatBlock stats;
        private GameObject boundPlayer;

        private void Start()
        {
            TryBindPlayer();
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

            if (inventory != null) inventory.OnChanged.AddListener(Refresh);
            if (equipment != null) equipment.OnEquipmentChanged.AddListener(Refresh);
            Refresh();
        }

        private void Unbind()
        {
            if (inventory != null) inventory.OnChanged.RemoveListener(Refresh);
            if (equipment != null) equipment.OnEquipmentChanged.RemoveListener(Refresh);
            inventory = null;
            equipment = null;
            stats = null;
            boundPlayer = null;
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

            RefreshBonuses();
        }

        private void RefreshBonuses()
        {
            if (bonusText == null || stats == null) return;

            string Line(string label, StatType type, string suffix = "")
            {
                float bonus = stats.GetModifierTotal(type);
                string color = bonus > 0 ? "#7FD98A" : "#8A8494";
                string value = bonus > 0 ? $"+{bonus:0.#}" : "—";
                return $"<color=#A79FB0>{label}</color>  <color={color}>{value}{suffix}</color>";
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
