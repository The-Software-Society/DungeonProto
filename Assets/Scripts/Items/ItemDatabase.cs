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
        Gloves,
        Boots,
        Necklace,
        Ring1,
        Ring2,
    }

    /// <summary>
    /// What a weapon-ish item actually IS, independent of which hand holds it.
    /// Ability gating reads this (PlayerAbilities.HasRequiredWeapon): melee
    /// abilities want a Sword-class main hand; magic abilities accept a Focus
    /// off hand OR a Wand-class main hand.
    /// </summary>
    public enum WeaponKind
    {
        None,
        Sword,
        Wand,    // main-hand magic weapon
        Focus,   // off-hand magic implement
        Bow,     // main-hand ranged weapon (append-only enum!)
    }

    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
    }

    /// <summary>One equip stat bonus, JSON-friendly (JsonUtility cannot serialize
    /// tuples). Implicit-from-tuple keeps inline initializers compiling; Deconstruct
    /// keeps every `foreach (var (t,v) in Bonuses)` reader working unchanged.</summary>
    [System.Serializable]
    public class StatBonus
    {
        public string type;    // StatType name, human-editable (e.g. "WeaponPower")
        public float value;
        public StatBonus() { }
        public StatBonus(StatType t, float v) { type = t.ToString(); value = v; }
        public static implicit operator StatBonus((StatType type, float value) t) => new StatBonus(t.type, t.value);
        public void Deconstruct(out StatType t, out float v)
        {
            t = System.Enum.TryParse<StatType>(type, out var k) ? k : default;
            v = value;
        }

        /// <summary>Convert to the (StatType,float) tuple list that StatBlock.ApplyModifiers expects.</summary>
        public static List<(StatType type, float value)> ToTuples(List<StatBonus> list)
        {
            var r = new List<(StatType, float)>(list?.Count ?? 0);
            if (list != null) foreach (var (t, v) in list) r.Add((t, v));
            return r;
        }
    }

    /// <summary>Static definition of an item type, loaded from items.json. Bonuses are StatBlock modifiers.</summary>
    [System.Serializable]
    public class ItemDef
    {
        public string Id;
        public string Name;
        public string Description;
        // Enums are serialized as editable strings; readers use the parsed properties below.
        public string slot = "None";        // EquipSlot: None/Weapon/Helm/Chest/Legs/Cape/Focus
        public string weaponKind = "None";  // WeaponKind: None/Sword/Wand/Focus (equip-gated abilities)
        public string rarity = "Common";    // ItemRarity: Common/Uncommon/Rare/Epic
        public EquipSlot Slot => System.Enum.TryParse<EquipSlot>(slot, out var k) ? k : EquipSlot.None;
        public WeaponKind WeaponKind => System.Enum.TryParse<WeaponKind>(weaponKind, out var k) ? k : WeaponKind.None;
        public ItemRarity Rarity => System.Enum.TryParse<ItemRarity>(rarity, out var k) ? k : ItemRarity.Common;
        public List<StatBonus> Bonuses = new List<StatBonus>();
        public string ModelProp;    // Assets/Models/Props/<name> for visuals + icon
        public Color Tint = Color.white;
        public int MaxStack = 1;    // >1 = stacks in one bag slot (fish, materials)
        public float HealAmount;    // >0 = edible
        public float EatLockout;    // seconds of combat lockout after eating
        public string CookedResult; // raw food → this id when cooked (fishing/cooking chain)
        public int Value;           // base vendor/gold value; 0 = derive from Rarity

        public bool IsConsumable => HealAmount > 0f;

        /// <summary>Gold value for vendor pricing. Falls back to a rarity default when Value is unset.</summary>
        public int SellValue => Value > 0 ? Value : Rarity switch
        {
            ItemRarity.Uncommon => 40,
            ItemRarity.Rare => 120,
            ItemRarity.Epic => 400,
            _ => 15,
        };

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

    /// <summary>File wrapper for JsonUtility (mirrors Data.MonsterFile).</summary>
    [System.Serializable]
    public class ItemFile
    {
        public ItemDef[] items = new ItemDef[0];
    }

    /// <summary>
    /// Item registry, loaded from StreamingAssets/items.json (mirrors
    /// Data.MonsterDatabase). Progression: rusty starter → goblin leather →
    /// bandit/orc iron tier → boss epic.
    /// </summary>
    public static class ItemDatabase
    {
        public const string FileName = "items.json";
        private static Dictionary<string, ItemDef> items;

        public static string FilePath =>
            System.IO.Path.Combine(Application.streamingAssetsPath, FileName);

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

            try
            {
                if (!System.IO.File.Exists(FilePath))
                {
                    Debug.LogError($"ItemDatabase: {FilePath} not found");
                    return;
                }
                var file = JsonUtility.FromJson<ItemFile>(System.IO.File.ReadAllText(FilePath));
                if (file?.items == null)
                {
                    Debug.LogError("ItemDatabase: items.json parsed to nothing");
                    return;
                }
                foreach (var d in file.items)
                {
                    if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                    items[d.Id] = d;
                }
                Debug.Log($"ItemDatabase: loaded {items.Count} items");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ItemDatabase: failed to load {FileName}: {e.Message}");
            }
        }

        /// <summary>Drop the cache so the next access reloads items.json (hot-reload).</summary>
        public static void Reload() { items = null; EnsureInitialized(); }

        public static string SlotName(EquipSlot slot) => slot switch
        {
            EquipSlot.Weapon => "Main Hand",
            EquipSlot.Helm => "Helm",
            EquipSlot.Chest => "Chest",
            EquipSlot.Legs => "Legs",
            EquipSlot.Cape => "Cape",
            EquipSlot.Focus => "Off Hand (Focus)",
            EquipSlot.Gloves => "Gloves",
            EquipSlot.Boots => "Boots",
            EquipSlot.Necklace => "Necklace",
            EquipSlot.Ring1 => "Ring 1",
            EquipSlot.Ring2 => "Ring 2",
            _ => "-",
        };
    }
}
