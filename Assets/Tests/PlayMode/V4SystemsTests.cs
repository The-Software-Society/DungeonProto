using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AldenFall.Abilities;
using AldenFall.Items;
using AldenFall.Player;
using AldenFall.Stats;

namespace AldenFall.Tests
{
    /// <summary>
    /// v4 foundation tests: focus items gate spell damage, stackable items
    /// share one bag cell.
    /// </summary>
    public class V4SystemsTests
    {
        private const string SceneName = "EmberfallDemo";

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // Bar layout + mastery persist in PlayerPrefs — never let one test
            // (or a real play session) bleed into another.
            PlayerPrefs.DeleteKey("actionbar_v1");
            PlayerPrefs.DeleteKey("mastery_v1");

            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return new WaitForSeconds(0.5f);
        }

        private static GameObject FindPlayer()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No PlayerController in scene");
            return player.gameObject;
        }

        [UnityTest]
        public IEnumerator FocusEmpowersSpellDamage()
        {
            var player = FindPlayer();
            var stats = player.GetComponent<StatBlock>();
            var inventory = player.GetComponent<Inventory>();
            var equipment = player.GetComponent<Equipment>();
            yield return null; // StarterLoadout coroutine

            float withoutFocus = CombatMath.SpellDamage(stats, 30f);

            Assert.IsTrue(inventory.Add("ember_focus"), "could not add ember_focus");
            Assert.IsTrue(equipment.Equip("ember_focus"), "could not equip ember_focus");
            float withFocus = CombatMath.SpellDamage(stats, 30f);

            // No focus = 60% output; SpellPower 10 = 100%. SpellDamage has no
            // random roll, so the ratio is exact.
            Assert.AreEqual(1f / 0.6f, withFocus / withoutFocus, 0.01f,
                $"focus ratio wrong (without: {withoutFocus:F1}, with: {withFocus:F1})");
            Assert.AreEqual("ember_focus", equipment.GetEquipped(EquipSlot.Focus));
        }

        [UnityTest]
        public IEnumerator FishStacksInOneSlot()
        {
            var player = FindPlayer();
            var inventory = player.GetComponent<Inventory>();
            yield return null;

            int cellsBefore = inventory.Slots.Count;
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(inventory.Add("raw_fish"), $"add #{i + 1} failed");

            Assert.AreEqual(cellsBefore + 1, inventory.Slots.Count,
                "5 raw fish should occupy exactly one new bag cell");
            Assert.AreEqual(5, inventory.CountOf("raw_fish"));

            // Removing eats the stack down, not the cell — until it's empty.
            Assert.IsTrue(inventory.Remove("raw_fish"));
            Assert.AreEqual(4, inventory.CountOf("raw_fish"));
            Assert.AreEqual(cellsBefore + 1, inventory.Slots.Count);
        }

        [UnityTest]
        public IEnumerator EatingHealsAndLocksCombatAbilities()
        {
            var player = FindPlayer();
            var health = player.GetComponent<AldenFall.Combat.Health>();
            var inventory = player.GetComponent<Inventory>();
            var consumables = player.GetComponent<PlayerConsumables>();
            var abilities = player.GetComponent<PlayerAbilities>();
            Assert.IsNotNull(consumables, "player has no PlayerConsumables");
            yield return null;

            health.TakeDamage(50f);
            float hurtHp = health.CurrentHealth;

            Assert.IsTrue(inventory.Add("cooked_fish"));
            Assert.IsTrue(consumables.TryEat("cooked_fish"), "TryEat failed");

            Assert.Greater(health.CurrentHealth, hurtHp, "eating did not heal");
            Assert.AreEqual(0, inventory.CountOf("cooked_fish"), "fish was not consumed");
            Assert.IsFalse(abilities.CanUse("fireball"),
                "combat abilities usable while chewing — lockout broken");
            Assert.IsFalse(consumables.TryEat("cooked_fish"),
                "double-eating allowed during lockout");

            // Lockout is 1.8s — poll until combat unlocks
            float deadline = Time.time + 3f;
            while (Time.time < deadline && !abilities.CanUse("fireball"))
                yield return null;
            Assert.IsTrue(abilities.CanUse("fireball"), "lockout never expired");
        }

        [UnityTest]
        public IEnumerator CombatSlotsRejectItemsFreeSlotsAcceptThem()
        {
            var player = FindPlayer();
            var bar = player.GetComponent<ActionBar>();
            Assert.IsNotNull(bar, "player has no ActionBar");
            yield return null;

            // 1-5 are combat-reserved: items must bounce
            Assert.IsFalse(bar.SetSlot(2, SlotBinding.Item("cooked_fish")),
                "combat slot accepted an item");
            // 6-0 take consumables
            Assert.IsTrue(bar.SetSlot(6, SlotBinding.Item("cooked_fish")),
                "free slot rejected a consumable");
            // …but not junk without a heal
            Assert.IsFalse(bar.SetSlot(7, SlotBinding.Item("burnt_fish")),
                "free slot accepted a non-consumable");
            // abilities are welcome anywhere
            Assert.IsTrue(bar.SetSlot(8, SlotBinding.Ability("fireball")),
                "free slot rejected an ability");

            // defaults put the kit on 1-5
            Assert.AreEqual(BindingKind.Ability, bar.GetSlot(0).Kind);
            Assert.AreEqual("fireball", bar.GetSlot(0).Id);
        }

        // ==================== FISHING & COOKING ====================

        private static void Teleport(Component obj, Vector3 position)
        {
            var cc = obj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            obj.transform.position = position;
            if (cc != null) cc.enabled = true;
        }

        [UnityTest]
        public IEnumerator FishingLoopGrantsFishAndXp()
        {
            var player = FindPlayer();
            var fishing = player.GetComponent<FishingController>();
            var inventory = player.GetComponent<Inventory>();
            var skills = player.GetComponent<AldenFall.Progression.Skills>();
            Assert.IsNotNull(fishing, "player has no FishingController");

            var spot = Object.FindFirstObjectByType<AldenFall.World.FishingSpot>();
            Assert.IsNotNull(spot, "no FishingSpot in the scene");

            Teleport(player.GetComponent<PlayerController>(), spot.transform.position + Vector3.right * 1f);
            yield return null; // StarterLoadout grants the pole on frame 1
            yield return null;

            Assert.IsTrue(inventory.Contains("fishing_pole"), "starter fishing pole missing");

            fishing.ConfigureLoop(seconds: 0.3f, chance: 1f); // deterministic + fast
            Assert.IsTrue(fishing.StartFishing(spot), "StartFishing refused");
            Assert.IsTrue(fishing.IsFishing);

            float deadline = Time.time + 4f;
            while (Time.time < deadline && inventory.CountOf("raw_fish") < 3)
                yield return null;

            Assert.GreaterOrEqual(inventory.CountOf("raw_fish"), 3, "catch ticks not landing");
            Assert.Greater(skills.Xp(AldenFall.Progression.SkillType.Fishing), 0f,
                "no Fishing XP granted");

            fishing.StopFishing();
            Assert.IsFalse(fishing.IsFishing, "StopFishing did not stop the loop");
        }

        [UnityTest]
        public IEnumerator CookingNeverBurnsAtHighLevel()
        {
            var player = FindPlayer();
            var cooking = player.GetComponent<CookingController>();
            var inventory = player.GetComponent<Inventory>();
            var skills = player.GetComponent<AldenFall.Progression.Skills>();
            Assert.IsNotNull(cooking, "player has no CookingController");
            yield return null;

            // Burn chance is a pure curve: verify the promise directly…
            Assert.AreEqual(0.5f, CookingController.BurnChance(1), 0.001f);
            Assert.Greater(CookingController.BurnChance(10), 0f);
            Assert.AreEqual(0f, CookingController.BurnChance(11), "level 11 must never burn");

            // …then end-to-end at level 11+
            skills.AddXp(AldenFall.Progression.SkillType.Cooking,
                AldenFall.Progression.Skills.XpForLevel(11));
            Assert.GreaterOrEqual(skills.Level(AldenFall.Progression.SkillType.Cooking), 11);

            var campfire = Object.FindFirstObjectByType<AldenFall.World.Campfire>();
            Assert.IsNotNull(campfire, "no Campfire in the scene");
            Teleport(player.GetComponent<PlayerController>(), campfire.transform.position + Vector3.left * 1.5f);
            yield return null;

            for (int i = 0; i < 5; i++) inventory.Add("raw_fish");

            var cookingSO = new System.Random(1); // irrelevant at 0% burn, but deterministic anyway
            cooking.SetRandom(cookingSO);
            Assert.IsTrue(cooking.StartCooking(campfire), "StartCooking refused");

            float deadline = Time.time + 15f;
            while (Time.time < deadline && inventory.CountOf("raw_fish") > 0)
                yield return null;

            Assert.AreEqual(5, inventory.CountOf("cooked_fish"), "not everything cooked");
            Assert.AreEqual(0, inventory.CountOf("burnt_fish"), "fish burnt at level 11+");
        }

        [UnityTest]
        public IEnumerator CookingBurnsSometimesAtLevelOne()
        {
            var player = FindPlayer();
            var cooking = player.GetComponent<CookingController>();
            var inventory = player.GetComponent<Inventory>();
            yield return null;

            var campfire = Object.FindFirstObjectByType<AldenFall.World.Campfire>();
            Teleport(player.GetComponent<PlayerController>(), campfire.transform.position + Vector3.left * 1.5f);
            yield return null;

            for (int i = 0; i < 10; i++) inventory.Add("raw_fish");

            // Seeded roll at 50% burn: over 10 fish both outcomes are certain
            // for any sane seed — pin one so the test can never flake.
            cooking.SetRandom(new System.Random(42));
            Assert.IsTrue(cooking.StartCooking(campfire), "StartCooking refused");

            float deadline = Time.time + 25f;
            while (Time.time < deadline && inventory.CountOf("raw_fish") > 0)
                yield return null;

            int cooked = inventory.CountOf("cooked_fish");
            int burnt = inventory.CountOf("burnt_fish");
            Assert.AreEqual(10, cooked + burnt, "fish went missing");
            Assert.Greater(burnt, 0, "level-1 cooking never burnt anything (seed 42)");
            Assert.Greater(cooked, 0, "level-1 cooking burnt everything (seed 42)");
        }

        // ==================== MONSTER JSON ====================

        [UnityTest]
        public IEnumerator MonsterJsonProducesRoster()
        {
            var defs = AldenFall.Data.MonsterDatabase.LoadAll();
            Assert.AreEqual(4, defs.Count, "monsters.json should define exactly 4 monster types");
            yield return new WaitForSeconds(0.5f); // spawners populate

            var identities = Object.FindObjectsByType<AldenFall.AI.MobIdentity>(FindObjectsSortMode.None);
            Assert.Greater(identities.Length, 0, "no mobs spawned");

            foreach (var def in defs)
            {
                AldenFall.AI.MobIdentity match = null;
                foreach (var identity in identities)
                    if (identity.DisplayName == def.displayName) { match = identity; break; }
                Assert.IsNotNull(match, $"no spawned mob named '{def.displayName}'");

                var stats = match.GetComponent<StatBlock>();
                Assert.IsNotNull(stats, $"{def.id} has no StatBlock");
                Assert.AreEqual(def.stats.maxHealth, stats.Get(StatType.MaxHealth), 0.01f,
                    $"{def.id} MaxHealth doesn't match JSON");
                Assert.AreEqual(def.stats.weaponPower, stats.Get(StatType.WeaponPower), 0.01f,
                    $"{def.id} WeaponPower doesn't match JSON");
            }
        }
    }
}
