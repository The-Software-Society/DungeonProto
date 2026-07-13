using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AldenFall.Data
{
    // ---- JsonUtility DTOs: arrays only, colors as hex strings ----

    [Serializable]
    public class MonsterStats
    {
        public float maxHealth = 100f;
        public float attack = 1f;
        public float strength = 1f;
        public float defence = 1f;
        public float magic = 1f;
        public float weaponPower = 0f;
        public float armor = 0f;
    }

    [Serializable]
    public class MonsterMovement
    {
        public float walkSpeed = 3.5f;
        public float runSpeed = 6f;
    }

    [Serializable]
    public class MonsterSpecial
    {
        public bool enabled = true;
        public float cooldown = 5f;
        public float knockback = 0f;
        public float chance = 0.3f;    // charge only
    }

    [Serializable]
    public class MonsterCombat
    {
        public float attackRange = 2f;
        public float detectionRange = 25f;
        public float attackCooldown = 1.5f;
        public float basicKnockback = 0f;
        public MonsterSpecial jumpAttack = new MonsterSpecial();
        public MonsterSpecial chargeAttack = new MonsterSpecial();
    }

    [Serializable]
    public class MonsterLoot
    {
        public string itemId;
        public float chance;
    }

    [Serializable]
    public class MonsterSpawn
    {
        public string kind = "spawner";   // "spawner" | "camp"
        public float x, y, z;
        public float respawn = 12f;
        public int count = 1;             // camp only
        public float radius = 0f;         // camp only
    }

    [Serializable]
    public class MonsterDef
    {
        public string id;
        public string displayName;
        public int level = 1;
        public string character;          // Meshy character folder / visual name
        public float heightMeters = 1.8f;
        public string fallbackColor = "#888888";
        public MonsterStats stats = new MonsterStats();
        public MonsterMovement movement = new MonsterMovement();
        public MonsterCombat combat = new MonsterCombat();
        public string[] statuses = new string[0];   // reserved: innate statuses
        public string[] abilities = new string[0];  // reserved: mob abilities
        public MonsterLoot[] loot = new MonsterLoot[0];
        public MonsterSpawn[] spawns = new MonsterSpawn[0];

        public Color FallbackColor =>
            ColorUtility.TryParseHtmlString(fallbackColor, out var c) ? c : Color.gray;
    }

    [Serializable]
    public class MonsterFile
    {
        public MonsterDef[] monsters = new MonsterDef[0];
    }

    /// <summary>
    /// The master monster file: every mob's stats, movement, combat behavior,
    /// loot, and spawns live in StreamingAssets/monsters.json, tuning is a
    /// JSON edit + scene rebuild, no code. Read identically by the editor
    /// build pipeline (ArenaBuilder), playmode tests, and shipped builds.
    /// </summary>
    public static class MonsterDatabase
    {
        public const string FileName = "monsters.json";

        public static string FilePath =>
            Path.Combine(Application.streamingAssetsPath, FileName);

        public static List<MonsterDef> LoadAll()
        {
            if (!File.Exists(FilePath))
            {
                Debug.LogError($"MonsterDatabase: {FilePath} not found");
                return new List<MonsterDef>();
            }

            var file = JsonUtility.FromJson<MonsterFile>(File.ReadAllText(FilePath));
            if (file?.monsters == null)
            {
                Debug.LogError("MonsterDatabase: monsters.json parsed to nothing");
                return new List<MonsterDef>();
            }
            return new List<MonsterDef>(file.monsters);
        }
    }
}
