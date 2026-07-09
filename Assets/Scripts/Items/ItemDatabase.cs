using System.Collections.Generic;
using UnityEngine;
using AldenFall.Stats;

namespace AldenFall.Items
{
    public enum EquipSlot
    {
        None,
        Weapon,
        Helm,
        Chest,
        Legs,
        Cape,
        Focus,   // off-hand magic implement (append-only enum!)
    }

    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
    }

    /// <summary>Static definition of an item type. Bonuses are StatBlock modifiers.</summary>
    public class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        public EquipSlot Slot;
        public ItemRarity Rarity;
        public List<(StatType type, float value)> Bonuses = new List<(StatType, float)>();
        public string ModelProp;    // Assets/Models/Props/<name> for visuals + icon
        public Color Tint = Color.white;
        public int MaxStack = 1;    // >1 = stacks in one bag slot (fish, materials)
        public float HealAmount;    // >0 = edible
        public float EatLockout;    // seconds of combat lockout after eating

        public bool IsConsumable => HealAmount > 0f;

        public float Bonus(StatType type)
        {
            float total = 0f;
            foreach (var (t, v) in Bonuses)
                if (t == type) total += v;
            return total;
        }

        public Color RarityColor => Rarity switch
        {
            ItemRarity.Uncommon => new Color(0.35f, 0.85f, 0.45f),
            ItemRarity.Rare => new Color(0.35f, 0.6f, 1f),
            ItemRarity.Epic => new Color(0.8f, 0.45f, 1f),
            _ => new Color(0.85f, 0.85f, 0.85f),
        };
    }

    /// <summary>
    /// Code-defined item registry (code-as-data keeps the headless pipeline simple).
    /// Progression: rusty starter → goblin leather → bandit/orc iron tier → boss epic.
    /// </summary>
    public static class ItemDatabase
    {
        private static Dictionary<string, ItemDef> items;

        public static ItemDef Get(string id)
        {
            EnsureInitialized();
            if (id == null) return null;
            if (items.TryGetValue(id, out var def)) return def;
            // "+N" upgrade instances resolve to their base definition
            return items.TryGetValue(ItemPlus.BaseId(id), out def) ? def : null;
        }

        public static IEnumerable<ItemDef> All
        {
            get { EnsureInitialized(); return items.Values; }
        }

        private static void EnsureInitialized()
        {
            if (items != null) return;
            items = new Dictionary<string, ItemDef>();

            // ---- Starter (deliberately terrible) ----
            Register(new ItemDef
            {
                Id = "rusty_sword",
                Name = "Rusty Sword",
                Description = "Chipped, corroded, and barely a weapon. It's a start.",
                Slot = EquipSlot.Weapon,
                Rarity = ItemRarity.Common,
                Bonuses = { (StatType.WeaponPower, 2f) },
                ModelProp = "RustySword",
            });

            // ---- Goblin tier (leather) ----
            Register(new ItemDef
            {
                Id = "leather_cap",
                Name = "Leather Cap",
                Description = "Hardened goblin leather. Smells like goblin.",
                Slot = EquipSlot.Helm,
                Rarity = ItemRarity.Common,
                Bonuses = { (StatType.Armor, 1f) },
                ModelProp = "LeatherCap",
                Tint = new Color(0.55f, 0.4f, 0.25f),
            });
            Register(new ItemDef
            {
                Id = "leather_legs",
                Name = "Leather Leggings",
                Description = "Scuffed leather leg guards.",
                Slot = EquipSlot.Legs,
                Rarity = ItemRarity.Common,
                Bonuses = { (StatType.Armor, 1f) },
                Tint = new Color(0.5f, 0.36f, 0.22f),
            });

            // ---- Bandit tier ----
            Register(new ItemDef
            {
                Id = "raider_saber",
                Name = "Raider Saber",
                Description = "A cruel curved blade taken from a field bandit.",
                Slot = EquipSlot.Weapon,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.WeaponPower, 8f), (StatType.AttackSpeed, 0.1f) },
                ModelProp = "RaiderSaber",
            });
            Register(new ItemDef
            {
                Id = "bandit_cape",
                Name = "Bandit Cape",
                Description = "A tattered hood-cape. Makes you feel sneaky.",
                Slot = EquipSlot.Cape,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.Armor, 1f), (StatType.MoveSpeed, 0.05f) },
                ModelProp = "BanditCape",
                Tint = new Color(0.25f, 0.22f, 0.28f),
            });

            // ---- Orc tier (iron) ----
            Register(new ItemDef
            {
                Id = "iron_helm",
                Name = "Iron Helm",
                Description = "A dented great helm. Rings like a bell when struck.",
                Slot = EquipSlot.Helm,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.Armor, 3f) },
                ModelProp = "IronHelm",
            });
            Register(new ItemDef
            {
                Id = "iron_chestplate",
                Name = "Iron Chestplate",
                Description = "Riveted orcish iron. Heavy, dented, dependable.",
                Slot = EquipSlot.Chest,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.Armor, 5f) },
                ModelProp = "IronChestplate",
            });
            Register(new ItemDef
            {
                Id = "iron_legs",
                Name = "Iron Greaves",
                Description = "Plated shin and thigh guards, orc-sized but adjustable.",
                Slot = EquipSlot.Legs,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.Armor, 3f) },
                ModelProp = "IronLegs",
            });

            // ---- Orc tier sword (KnightSword prop finally earns its keep) ----
            Register(new ItemDef
            {
                Id = "knight_sword",
                Name = "Knight's Sword",
                Description = "A proper blade with a blue-silver crossguard. How did an orc get this?",
                Slot = EquipSlot.Weapon,
                Rarity = ItemRarity.Rare,
                Bonuses = { (StatType.WeaponPower, 12f), (StatType.AttackSpeed, 0.05f) },
                ModelProp = "KnightSword",
            });

            // ---- Boss tier ----
            Register(new ItemDef
            {
                Id = "emberfang",
                Name = "Emberfang",
                Description = "An obsidian blade veined with living fire. The troll guarded it well.",
                Slot = EquipSlot.Weapon,
                Rarity = ItemRarity.Epic,
                Bonuses = { (StatType.WeaponPower, 18f), (StatType.Strength, 3f) },
                ModelProp = "Emberfang",
                Tint = new Color(1f, 0.6f, 0.3f),
            });

            // ---- Focus items: spells run at 60% without one, 100% with the boss focus ----
            Register(new ItemDef
            {
                Id = "apprentice_focus",
                Name = "Apprentice Focus",
                Description = "A chipped crystal on a stick. It hums, faintly.",
                Slot = EquipSlot.Focus,
                Rarity = ItemRarity.Uncommon,
                Bonuses = { (StatType.SpellPower, 4f) },
                ModelProp = "MagicFocus",
                Tint = new Color(0.5f, 0.7f, 1f),
            });
            Register(new ItemDef
            {
                Id = "adept_focus",
                Name = "Adept Focus",
                Description = "The crystal floats now. That feels important.",
                Slot = EquipSlot.Focus,
                Rarity = ItemRarity.Rare,
                Bonuses = { (StatType.SpellPower, 7f) },
                ModelProp = "MagicFocus",
                Tint = new Color(0.72f, 0.5f, 1f),
            });
            Register(new ItemDef
            {
                Id = "ember_focus",
                Name = "Ember Focus",
                Description = "A shard of the troll's fire, tamed into an orb. Spells hit like they mean it.",
                Slot = EquipSlot.Focus,
                Rarity = ItemRarity.Epic,
                Bonuses = { (StatType.SpellPower, 10f) },
                ModelProp = "MagicFocus",
                Tint = new Color(1f, 0.55f, 0.25f),
            });

            // ---- Fishing & cooking ----
            Register(new ItemDef
            {
                Id = "fishing_pole",
                Name = "Fishing Pole",
                Description = "A bendy stick, some line, and a cork. The pond awaits.",
                Slot = EquipSlot.None,
                Rarity = ItemRarity.Common,
                ModelProp = "FishingPole",
            });
            Register(new ItemDef
            {
                Id = "raw_fish",
                Name = "Raw Fish",
                Description = "Fresh from the courtyard pond. Needs a campfire.",
                Slot = EquipSlot.None,
                Rarity = ItemRarity.Common,
                MaxStack = 50,
                ModelProp = "Fish",
                Tint = new Color(0.72f, 0.8f, 0.9f),
            });
            Register(new ItemDef
            {
                Id = "cooked_fish",
                Name = "Cooked Fish",
                Description = "Golden and flaky. Restores health — chewing takes a moment.",
                Slot = EquipSlot.None,
                Rarity = ItemRarity.Common,
                MaxStack = 50,
                HealAmount = 25f,
                EatLockout = 1.8f,
                ModelProp = "Fish",
                Tint = new Color(0.95f, 0.72f, 0.35f),
            });
            Register(new ItemDef
            {
                Id = "burnt_fish",
                Name = "Burnt Fish",
                Description = "It's charcoal shaped like regret. Cooking gets better with practice.",
                Slot = EquipSlot.None,
                Rarity = ItemRarity.Common,
                MaxStack = 50,
                ModelProp = "Fish",
                Tint = new Color(0.22f, 0.2f, 0.18f),
            });
        }

        private static void Register(ItemDef def) => items[def.Id] = def;

        public static string SlotName(EquipSlot slot) => slot switch
        {
            EquipSlot.Weapon => "Weapon",
            EquipSlot.Helm => "Helm",
            EquipSlot.Chest => "Chest",
            EquipSlot.Legs => "Legs",
            EquipSlot.Cape => "Cape",
            EquipSlot.Focus => "Focus",
            _ => "—",
        };
    }
}
