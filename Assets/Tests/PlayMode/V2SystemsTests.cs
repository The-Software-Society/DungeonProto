using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AldenFall.AI;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Player;
using AldenFall.Progression;

namespace AldenFall.Tests
{
    /// <summary>
    /// End-to-end tests for the v2 loop: starter gear, use-based skill XP,
    /// loot pickup + equip, mob camps, and the cave boss.
    /// </summary>
    public class V2SystemsTests
    {
        private const string SceneName = "EmberfallDemo";

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return new WaitForSeconds(0.6f); // spawners + starter loadout
        }

        private static GameObject Player()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(player, "no player");
            return player;
        }

        private static void Teleport(GameObject obj, Vector3 pos)
        {
            var cc = obj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            obj.transform.position = pos;
            if (cc != null) cc.enabled = true;
        }

        [UnityTest]
        public IEnumerator StarterGearIsEquipped()
        {
            var player = Player();
            yield return null;

            var equipment = player.GetComponent<Equipment>();
            Assert.IsNotNull(equipment, "player has no Equipment");
            Assert.AreEqual("rusty_sword", equipment.GetEquipped(EquipSlot.Weapon),
                "starter weapon not equipped");
            Assert.Greater(equipment.WeaponPower, 0f, "weapon power not applied via StatBlock");

            // Starts with nothing else — armor comes from the world
            Assert.IsNull(equipment.GetEquipped(EquipSlot.Chest), "should start with no chest armor");
        }

        [UnityTest]
        public IEnumerator MeleeSwingDamagesAndTrainsSkills()
        {
            var player = Player();
            var skills = player.GetComponent<Skills>();
            var targeting = player.GetComponent<TargetingSystem>();
            Assert.IsNotNull(skills);
            Assert.IsNotNull(targeting);

            // Nearest goblin to the plaza
            EnemyAI enemy = null;
            float best = float.MaxValue;
            foreach (var candidate in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(candidate.transform.position, new Vector3(0, 0, 8f));
                if (d < best) { best = d; enemy = candidate; }
            }
            Assert.IsNotNull(enemy, "no goblin found");

            var enemyHealth = enemy.GetComponent<Health>();
            float hpBefore = enemyHealth.CurrentHealth;
            float atkXpBefore = skills.Xp(SkillType.Attack);
            float strXpBefore = skills.Xp(SkillType.Strength);

            // Stand in melee range and target it — auto-attack should swing
            Teleport(player, enemy.transform.position + new Vector3(1.6f, 0.1f, 0));
            targeting.SetTarget(enemy.transform);

            float deadline = Time.time + 5f;
            while (Time.time < deadline && enemyHealth.CurrentHealth >= hpBefore)
                yield return null;

            Assert.Less(enemyHealth.CurrentHealth, hpBefore, "auto-melee never landed");
            Assert.Greater(skills.Xp(SkillType.Attack), atkXpBefore, "no Attack xp gained");
            Assert.Greater(skills.Xp(SkillType.Strength), strXpBefore, "no Strength xp gained");
        }

        [UnityTest]
        public IEnumerator LootDropPickupAndEquip()
        {
            var player = Player();
            var inventory = player.GetComponent<Inventory>();
            var equipment = player.GetComponent<Equipment>();
            yield return null; // starter loadout settles

            // Drop a saber at the player's feet; auto-pickup should collect it
            LootDrop.Spawn("raider_saber", player.transform.position + Vector3.forward);

            float deadline = Time.time + 3f;
            while (Time.time < deadline && !inventory.Contains("raider_saber"))
                yield return null;

            Assert.IsTrue(inventory.Contains("raider_saber"), "loot was not picked up");

            // Equip it — old weapon returns to the bag
            Assert.IsTrue(equipment.Equip("raider_saber"), "equip failed");
            Assert.AreEqual("raider_saber", equipment.GetEquipped(EquipSlot.Weapon));
            Assert.IsTrue(inventory.Contains("rusty_sword"), "displaced weapon lost");
            Assert.AreEqual(8f, equipment.WeaponPower, 0.01f, "saber power not in StatBlock");

            // Armor pieces equip into their own slots and raise Armor
            var stats = player.GetComponent<AldenFall.Stats.StatBlock>();
            float armorBefore = stats.Get(AldenFall.Stats.StatType.Armor);
            inventory.Add("iron_helm");
            Assert.IsTrue(equipment.Equip("iron_helm"), "helm equip failed");
            Assert.AreEqual("iron_helm", equipment.GetEquipped(EquipSlot.Helm));
            Assert.Greater(stats.Get(AldenFall.Stats.StatType.Armor), armorBefore,
                "helm armor bonus not applied");
        }

        [UnityTest]
        public IEnumerator DefenceTrainsWhenHit()
        {
            var player = Player();
            var skills = player.GetComponent<Skills>();
            var health = player.GetComponent<Health>();
            float defXpBefore = skills.Xp(SkillType.Defence);

            health.TakeDamage(10f, null);
            yield return null;

            Assert.Greater(skills.Xp(SkillType.Defence), defXpBefore, "no Defence xp from taking damage");
            // No armor at level 1 → no mitigation expected; mitigation itself is
            // covered by the equip test raising the Armor stat.
        }

        [UnityTest]
        public IEnumerator FieldCampsAndBossSpawned()
        {
            yield return new WaitForSeconds(0.5f);

            int bandits = 0, orcs = 0, bosses = 0;
            foreach (var identity in Object.FindObjectsByType<MobIdentity>(FindObjectsSortMode.None))
            {
                if (identity.DisplayName.Contains("Bandit")) bandits++;
                else if (identity.DisplayName.Contains("Orc")) orcs++;
                else if (identity.DisplayName.Contains("Grukk")) bosses++;
            }

            Assert.GreaterOrEqual(bandits, 3, "bandit camp underpopulated");
            Assert.GreaterOrEqual(orcs, 3, "orc camp underpopulated");
            Assert.AreEqual(1, bosses, "cave boss missing");
        }

        [UnityTest]
        public IEnumerator BossDropsEmberfangOnDeath()
        {
            yield return new WaitForSeconds(0.5f);

            MobIdentity boss = null;
            foreach (var identity in Object.FindObjectsByType<MobIdentity>(FindObjectsSortMode.None))
                if (identity.DisplayName.Contains("Grukk")) boss = identity;
            Assert.IsNotNull(boss, "boss not found");

            var health = boss.GetComponent<Health>();
            health.TakeDamage(healthOverkill(health), Player());
            yield return new WaitForSeconds(0.3f);

            // v4: the boss guarantees BOTH the sword and the magic focus
            var drops = Object.FindObjectsByType<LootDrop>(FindObjectsSortMode.None);
            Assert.Greater(drops.Length, 0, "boss dropped nothing");
            bool sword = false, focus = false;
            foreach (var drop in drops)
            {
                if (drop.ItemId == "emberfang") sword = true;
                if (drop.ItemId == "ember_focus") focus = true;
            }
            Assert.IsTrue(sword, "boss did not drop emberfang");
            Assert.IsTrue(focus, "boss did not drop ember_focus");
        }

        private static float healthOverkill(Health h) => h.CurrentHealth + 100f;
    }
}
