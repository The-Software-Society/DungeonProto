using System.Collections.Generic;
using UnityEngine;
using AldenFall.Stats;

namespace AldenFall.Items
{
    /// <summary>
    /// The +1..+10 gear upgrade layer. A plussed item is the base item id with
    /// a "+N" suffix ("raider_saber+7") — the id IS the instance, so it flows
    /// through inventory, equipment, loot, and the action bar untouched;
    /// ItemDatabase.Get strips the suffix, and stat application scales here.
    ///
    /// Balance anchor (Jon's spec): each + adds FLAT power per stat type —
    /// +2 WeaponPower per plus means a Rusty Sword (2 power) at +8 equals
    /// Emberfang (18), and +10 (22) is godlike for its level.
    /// </summary>
    public static class ItemPlus
    {
        public const int MaxPlus = 10;

        /// <summary>"raider_saber+7" → ("raider_saber", 7). Plain ids pass through.</summary>
        public static (string baseId, int plus) Parse(string id)
        {
            if (string.IsNullOrEmpty(id)) return (id, 0);
            int at = id.LastIndexOf('+');
            if (at <= 0 || at == id.Length - 1) return (id, 0);
            if (!int.TryParse(id.Substring(at + 1), out int plus) || plus <= 0) return (id, 0);
            return (id.Substring(0, at), Mathf.Min(plus, MaxPlus));
        }

        public static string BaseId(string id) => Parse(id).baseId;
        public static int PlusOf(string id) => Parse(id).plus;

        public static string Compose(string baseId, int plus) =>
            plus > 0 ? $"{baseId}+{Mathf.Min(plus, MaxPlus)}" : baseId;

        /// <summary>"Raider Saber +7" (plain name at +0).</summary>
        public static string DisplayName(ItemDef def, int plus) =>
            plus > 0 ? $"{def.Name} +{plus}" : def.Name;

        /// <summary>Flat bonus added per plus, by stat type.</summary>
        public static float PerPlus(StatType type) => type switch
        {
            StatType.WeaponPower => 2f,
            StatType.Armor => 1f,
            StatType.SpellPower => 0.75f,
            StatType.AttackSpeed => 0.015f,
            StatType.MoveSpeed => 0.01f,
            _ => 0.5f,   // flat skill bonuses (Strength on Emberfang, etc.)
        };

        /// <summary>The item's bonuses with the plus applied.</summary>
        public static List<(StatType type, float value)> ScaledBonuses(ItemDef def, int plus)
        {
            if (def == null) return new List<(StatType, float)>();
            if (plus <= 0) return def.Bonuses;

            var scaled = new List<(StatType, float)>(def.Bonuses.Count);
            foreach (var (type, value) in def.Bonuses)
                scaled.Add((type, value + PerPlus(type) * plus));
            return scaled;
        }

        /// <summary>
        /// Drop roll: +1-3 common (~82%), +4-6 uncommon, +7-8 rare (~2.5%,
        /// boss-tier on a common base), +9-10 super rare (+10 = 1 in 1000).
        /// </summary>
        public static int RollPlus()
        {
            float r = Random.value;
            if (r < 0.38f) return 1;
            if (r < 0.66f) return 2;
            if (r < 0.82f) return 3;
            if (r < 0.90f) return 4;
            if (r < 0.945f) return 5;
            if (r < 0.97f) return 6;
            if (r < 0.985f) return 7;
            if (r < 0.995f) return 8;
            if (r < 0.999f) return 9;
            return 10;
        }

        /// <summary>Border/name color escalates with plus (visual excitement).</summary>
        public static Color PlusColor(ItemDef def, int plus)
        {
            if (plus >= 9) return new Color(1f, 0.55f, 0.15f);   // legendary orange
            if (plus >= 7) return new Color(0.8f, 0.45f, 1f);    // epic purple
            if (plus >= 4) return new Color(0.35f, 0.6f, 1f);    // rare blue
            return def.RarityColor;
        }
    }
}
