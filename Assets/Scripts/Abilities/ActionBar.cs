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

    /// <summary>Which equipped weapon family auto-swaps to a preset bar.</summary>
    public enum WeaponCategory
    {
        None = 0,   // no trigger: this bar is manual-only
        Melee = 1,
        Magic = 2,
        Archery = 3,
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
    /// The 10-slot action bar (keys 1-9, 0) with up to five PRESETS. Slots 1-5 are
    /// RESERVED for combat abilities; 6-0 take anything. Each preset can carry a
    /// WeaponCategory trigger: equip a weapon of that family and the bar auto-swaps
    /// to the matching preset with no user action (Aldric hands you all three
    /// weapons and one bar per family). Up/Down cycle presets manually; the trigger
    /// is user-rebindable. Layout + triggers persist via PlayerPrefs.
    /// </summary>
    public class ActionBar : MonoBehaviour
    {
        public const int SlotCount = 10;
        public const int CombatSlots = 5;    // indices 0-4 = keys 1-5
        public const int MaxPresets = 5;
        public const int DefaultPresetCount = 3;
        private const string PrefsKey = "actionbar_v3";

        [Serializable]
        private class Preset
        {
            public SlotBinding[] slots = NewSlots();
            public WeaponCategory trigger = WeaponCategory.None;
            public static SlotBinding[] NewSlots()
            {
                var s = new SlotBinding[SlotCount];
                for (int i = 0; i < SlotCount; i++) s[i] = SlotBinding.Empty;
                return s;
            }
        }

        private readonly List<Preset> presets = new List<Preset>();
        private int active;

        private PlayerAbilities abilities;
        private PlayerConsumables consumables;
        private Items.Equipment equipment;

        /// <summary>Raised when the active bar's contents OR the active preset changes.</summary>
        public event Action OnChanged;

        public int PresetCount => presets.Count;
        public int ActivePreset => active;

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
            equipment = GetComponent<Items.Equipment>();
            if (!Load())
                ResetToDefaults();
        }

        private void Start()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.AddListener(AutoSwapForEquippedWeapon);
        }

        private void OnDestroy()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.RemoveListener(AutoSwapForEquippedWeapon);
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

        private Preset Active => presets[active];

        // ---------- preset selection ----------

        public void SetActivePreset(int index)
        {
            if (index < 0 || index >= presets.Count || index == active) return;
            active = index;
            Save();
            OnChanged?.Invoke();
        }

        public void NextPreset() => SetActivePreset((active + 1) % presets.Count);
        public void PrevPreset() => SetActivePreset((active - 1 + presets.Count) % presets.Count);

        public WeaponCategory TriggerOf(int index) =>
            index >= 0 && index < presets.Count ? presets[index].trigger : WeaponCategory.None;

        /// <summary>Rebind which weapon family auto-swaps to a preset (the [X] menu).</summary>
        public void SetTrigger(int index, WeaponCategory category)
        {
            if (index < 0 || index >= presets.Count) return;
            // A category can trigger only one bar: clear it off any other preset.
            if (category != WeaponCategory.None)
                foreach (var p in presets)
                    if (p.trigger == category) p.trigger = WeaponCategory.None;
            presets[index].trigger = category;
            Save();
            OnChanged?.Invoke();
        }

        /// <summary>Cycle the active preset's trigger None->Melee->Magic->Archery (the [X] button).</summary>
        public void CycleActiveTrigger()
        {
            var next = (WeaponCategory)(((int)presets[active].trigger + 1) % 4);
            SetTrigger(active, next);
        }

        /// <summary>The weapon family currently equipped, for auto-swap + display.</summary>
        public WeaponCategory EquippedCategory()
        {
            if (equipment == null) return WeaponCategory.None;
            if (equipment.HasBow) return WeaponCategory.Archery;
            if (equipment.HasMagicImplement) return WeaponCategory.Magic;
            if (equipment.HasSwordMainHand) return WeaponCategory.Melee;
            return WeaponCategory.None;
        }

        /// <summary>Equipment changed: if a preset is triggered by the new weapon family, swap to it.</summary>
        private void AutoSwapForEquippedWeapon()
        {
            var cat = EquippedCategory();
            if (cat == WeaponCategory.None) return;
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].trigger == cat)
                {
                    SetActivePreset(i);
                    return;
                }
            }
        }

        // ---------- slot access (operates on the ACTIVE preset) ----------

        public SlotBinding GetSlot(int index) =>
            index >= 0 && index < SlotCount ? Active.slots[index] : SlotBinding.Empty;

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

            Active.slots[index] = binding;
            Save();
            OnChanged?.Invoke();
            return true;
        }

        public void ClearSlot(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            Active.slots[index] = SlotBinding.Empty;
            Save();
            OnChanged?.Invoke();
        }

        /// <summary>Move a binding between two bar slots (drag slot → slot) in the active preset.</summary>
        public bool MoveSlot(int from, int to)
        {
            if (from == to) return true;
            var moving = GetSlot(from);
            if (moving.IsEmpty) return false;

            var displaced = GetSlot(to);
            if (!SetSlot(to, moving)) return false;
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

        /// <summary>Fill the ACTIVE preset's combat slots (keys 1-5) with a starter kit, in order.</summary>
        public void ApplyCombatKit(IList<string> abilityIds) => ApplyKitTo(active, abilityIds);

        /// <summary>
        /// Set up a preset for the intro: fill its combat slots and bind it to a
        /// weapon family so equipping that weapon auto-swaps here. Ensures the
        /// preset exists (grows the list up to MaxPresets).
        /// </summary>
        public void ConfigurePreset(int index, WeaponCategory trigger, IList<string> abilityIds)
        {
            if (index < 0 || index >= MaxPresets) return;
            while (presets.Count <= index && presets.Count < MaxPresets)
                presets.Add(new Preset());
            if (index >= presets.Count) return;

            presets[index].trigger = trigger;
            ApplyKitTo(index, abilityIds);
        }

        private void ApplyKitTo(int presetIndex, IList<string> abilityIds)
        {
            if (presetIndex < 0 || presetIndex >= presets.Count) return;
            var slots = presets[presetIndex].slots;
            for (int i = 0; i < CombatSlots; i++)
            {
                string id = abilityIds != null && i < abilityIds.Count ? abilityIds[i] : null;
                slots[i] = id != null && AbilityDatabase.Get(id) != null
                    ? SlotBinding.Ability(id)
                    : SlotBinding.Empty;
            }
            Save();
            OnChanged?.Invoke();
        }

        public void ResetToDefaults()
        {
            presets.Clear();
            for (int i = 0; i < DefaultPresetCount; i++)
                presets.Add(new Preset());
            active = 0;

            // Preset 0 = Melee, 1 = Magic, 2 = Archery, each triggered by its family.
            presets[0].trigger = WeaponCategory.Melee;
            presets[0].slots[0] = SlotBinding.Ability("blade_rush");
            presets[0].slots[1] = SlotBinding.Ability("savage_cleave");
            presets[0].slots[2] = SlotBinding.Ability("whirlwind");
            presets[0].slots[3] = SlotBinding.Ability("bash");
            presets[0].slots[4] = SlotBinding.Ability("bulwark");

            presets[1].trigger = WeaponCategory.Magic;
            presets[1].slots[0] = SlotBinding.Ability("fireball");
            presets[1].slots[1] = SlotBinding.Ability("frost_bolt");
            presets[1].slots[2] = SlotBinding.Ability("ember_nova");
            presets[1].slots[3] = SlotBinding.Ability("meteor_strike");
            presets[1].slots[4] = SlotBinding.Ability("arcane_ward");

            presets[2].trigger = WeaponCategory.Archery;
            presets[2].slots[0] = SlotBinding.Ability("power_shot");
            presets[2].slots[1] = SlotBinding.Ability("multi_shot");
            presets[2].slots[2] = SlotBinding.Ability("pinning_shot");
            presets[2].slots[3] = SlotBinding.Ability("rain_of_arrows");
            presets[2].slots[4] = SlotBinding.Ability("hunters_mark");

            Save();
            OnChanged?.Invoke();
        }

        // ---------- persistence ----------

        [Serializable]
        private class PresetData
        {
            public int[] kinds = new int[SlotCount];
            public string[] ids = new string[SlotCount];
            public int trigger;
        }

        [Serializable]
        private class SaveData
        {
            public int active;
            public List<PresetData> presets = new List<PresetData>();
        }

        private void Save()
        {
            var data = new SaveData { active = active };
            foreach (var p in presets)
            {
                var pd = new PresetData { trigger = (int)p.trigger };
                for (int i = 0; i < SlotCount; i++)
                {
                    pd.kinds[i] = (int)p.slots[i].Kind;
                    pd.ids[i] = p.slots[i].Id ?? "";
                }
                data.presets.Add(pd);
            }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        }

        private bool Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json)) return false;
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null || data.presets == null || data.presets.Count == 0) return false;

            presets.Clear();
            foreach (var pd in data.presets)
            {
                if (presets.Count >= MaxPresets) break;
                var p = new Preset { trigger = (WeaponCategory)Mathf.Clamp(pd.trigger, 0, 3) };
                if (pd.kinds != null && pd.ids != null && pd.kinds.Length == SlotCount)
                {
                    for (int i = 0; i < SlotCount; i++)
                    {
                        var binding = new SlotBinding
                        {
                            Kind = (BindingKind)pd.kinds[i],
                            Id = string.IsNullOrEmpty(pd.ids[i]) ? null : pd.ids[i],
                        };
                        bool valid = binding.Kind switch
                        {
                            BindingKind.Ability => AbilityDatabase.Get(binding.Id) != null,
                            BindingKind.Item => ItemDatabase.Get(binding.Id) != null,
                            _ => false,
                        };
                        p.slots[i] = valid ? binding : SlotBinding.Empty;
                    }
                }
                presets.Add(p);
            }
            if (presets.Count == 0) return false;
            active = Mathf.Clamp(data.active, 0, presets.Count - 1);
            return true;
        }
    }
}
