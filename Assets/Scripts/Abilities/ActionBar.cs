using System;
using System.Collections.Generic;
using UnityEngine;
using AldenFall.Items;
using AldenFall.UI;

namespace AldenFall.Abilities
{
    public enum BindingKind
    {
        None,
        Ability,
        Item,
    }

    [Serializable]
    public struct SlotBinding
    {
        public BindingKind Kind;
        public string Id;

        public bool IsEmpty => Kind == BindingKind.None || string.IsNullOrEmpty(Id);
        public static SlotBinding Empty => new SlotBinding { Kind = BindingKind.None, Id = null };
        public static SlotBinding Ability(string id) => new SlotBinding { Kind = BindingKind.Ability, Id = id };
        public static SlotBinding Item(string id) => new SlotBinding { Kind = BindingKind.Item, Id = id };
    }

    /// <summary>
    /// The 10-slot action bar (keys 1-9, 0). Slots 1-5 are RESERVED for combat
    /// abilities; 6-0 take anything — abilities or consumable items dragged
    /// from the bag. The model is the validator: UI drop targets just call
    /// SetSlot and show what stuck. Layout persists via PlayerPrefs.
    /// </summary>
    public class ActionBar : MonoBehaviour
    {
        public const int SlotCount = 10;
        public const int CombatSlots = 5;   // indices 0-4 = keys 1-5
        private const string PrefsKey = "actionbar_v1";

        private readonly SlotBinding[] slots = new SlotBinding[SlotCount];

        private PlayerAbilities abilities;
        private PlayerConsumables consumables;

        public event Action OnChanged;

        private static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
        };
        private static readonly KeyCode[] KeypadKeys =
        {
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4, KeyCode.Keypad5,
            KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9, KeyCode.Keypad0,
        };

        private void Awake()
        {
            abilities = GetComponent<PlayerAbilities>();
            consumables = GetComponent<PlayerConsumables>();
            if (!Load())
                ResetToDefaults();
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            for (int i = 0; i < SlotCount; i++)
            {
                if (Input.GetKeyDown(Keys[i]) || Input.GetKeyDown(KeypadKeys[i]))
                    Activate(i);
            }
        }

        public SlotBinding GetSlot(int index) =>
            index >= 0 && index < SlotCount ? slots[index] : SlotBinding.Empty;

        /// <summary>Model-side validation: combat slots take abilities only.</summary>
        public bool SetSlot(int index, SlotBinding binding)
        {
            if (index < 0 || index >= SlotCount) return false;

            if (!binding.IsEmpty)
            {
                switch (binding.Kind)
                {
                    case BindingKind.Ability:
                        if (AbilityDatabase.Get(binding.Id) == null) return false;
                        break;
                    case BindingKind.Item:
                        if (index < CombatSlots) return false; // 1-5 = combat only
                        var def = ItemDatabase.Get(binding.Id);
                        if (def == null || !def.IsConsumable) return false;
                        break;
                    default:
                        return false;
                }
            }

            slots[index] = binding;
            Save();
            OnChanged?.Invoke();
            return true;
        }

        public void ClearSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            slots[index] = SlotBinding.Empty;
            Save();
            OnChanged?.Invoke();
        }

        /// <summary>Move a binding between two bar slots (drag slot → slot).</summary>
        public bool MoveSlot(int from, int to)
        {
            if (from == to) return true;
            var moving = GetSlot(from);
            if (moving.IsEmpty) return false;

            var displaced = GetSlot(to);
            if (!SetSlot(to, moving)) return false;
            // Swap back only if the displaced binding is legal in the source slot
            if (!displaced.IsEmpty && SetSlot(from, displaced)) return true;
            ClearSlot(from);
            return true;
        }

        public void Activate(int index)
        {
            var binding = GetSlot(index);
            if (binding.IsEmpty) return;

            switch (binding.Kind)
            {
                case BindingKind.Ability:
                    abilities?.TryActivate(binding.Id);
                    break;
                case BindingKind.Item:
                    consumables?.TryEat(binding.Id);
                    break;
            }
        }

        public void ResetToDefaults()
        {
            for (int i = 0; i < SlotCount; i++)
                slots[i] = SlotBinding.Empty;
            slots[0] = SlotBinding.Ability("fireball");
            slots[1] = SlotBinding.Ability("blade_rush");
            slots[2] = SlotBinding.Ability("ember_nova");
            slots[3] = SlotBinding.Ability("savage_cleave");
            slots[4] = SlotBinding.Ability("bulwark");
            Save();
            OnChanged?.Invoke();
        }

        // ---------- persistence ----------

        [Serializable]
        private class SaveData
        {
            public int[] kinds = new int[SlotCount];
            public string[] ids = new string[SlotCount];
        }

        private void Save()
        {
            var data = new SaveData();
            for (int i = 0; i < SlotCount; i++)
            {
                data.kinds[i] = (int)slots[i].Kind;
                data.ids[i] = slots[i].Id ?? "";
            }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        }

        private bool Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json)) return false;
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null || data.kinds == null || data.kinds.Length != SlotCount) return false;

            for (int i = 0; i < SlotCount; i++)
            {
                var binding = new SlotBinding
                {
                    Kind = (BindingKind)data.kinds[i],
                    Id = string.IsNullOrEmpty(data.ids[i]) ? null : data.ids[i],
                };
                // Drop bindings whose content no longer exists
                bool valid = binding.Kind switch
                {
                    BindingKind.Ability => AbilityDatabase.Get(binding.Id) != null,
                    BindingKind.Item => ItemDatabase.Get(binding.Id) != null,
                    _ => false,
                };
                slots[i] = valid ? binding : SlotBinding.Empty;
            }
            return true;
        }
    }
}
