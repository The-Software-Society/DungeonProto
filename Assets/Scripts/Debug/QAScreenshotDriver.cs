using System.Collections;
using System.IO;
using Mirror;
using UnityEngine;

namespace AldenFall.DebugTools
{
    /// <summary>
    /// Automated visual-QA pass. ONLY active when the player is launched with the
    /// -qashots command-line flag (never in normal play): boots a local HOST
    /// session (full server+client, every system live), teleports the avatar
    /// through the key areas of the V2 world, flips the eruption world-state, and
    /// writes a screenshot per stop via ScreenCapture. Exits the app when done.
    ///
    /// Output dir: -qadir &lt;absolute path&gt; (default: persistentDataPath/qa).
    /// </summary>
    public static class QAScreenshotBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qashots") < 0)
                return;
            var go = new GameObject("QAScreenshotDriver");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<QAScreenshotDriver>();
        }
    }

    public class QAScreenshotDriver : MonoBehaviour
    {
        private string outDir;

        // (shot name, teleport position, facing yaw — the camera snaps behind the avatar)
        private static readonly (string name, Vector3 pos, float yaw)[] Shots =
        {
            ("01_glade_spawn",    new Vector3(0f, 1.5f, 84f),     0f),    // face Aldric
            ("02_city_north_gate",new Vector3(0f, 1.5f, 62f),     180f),  // approach the city wall
            ("03_city_plaza",     new Vector3(0f, 1.5f, 14f),     180f),  // fountain + streets
            ("04_city_market",    new Vector3(24f, 1.5f, 20f),    20f),   // market row + vendor
            ("05_lake_bank",      new Vector3(-74f, 1.5f, 140f), 315f),  // lake + goblins + fishing
            ("06_dark_woods",     new Vector3(0f, 1.5f, 180f),    0f),
            ("07_goblin_camp",    new Vector3(-180f, 1.5f, 0f),   270f),
            ("08_orc_warcamp",    new Vector3(-98f, 1.5f, -108f), 225f),  // grukmar + cage
            ("09_ember_approach", new Vector3(0f, 1.5f, -150f),   180f),  // soot gradient toward volcano
            ("10_volcano_mouth",  new Vector3(0f, 1.5f, -300f),   180f),
            ("11_dungeon_lavahall", new Vector3(0f, 1.5f, -360f), 180f),  // lava channels
            ("12_boss_chamber",   new Vector3(0f, 1.5f, -426f),   180f),  // boss + sealed door behind
        };

        private bool uiMode;

        private IEnumerator Start()
        {
            // -qaui: capture every UI panel/popup instead of world locations.
            uiMode = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-qaui") >= 0;

            // Never let the intro cutscene hijack the QA run.
            // Intro is skipped under -batchmode by IntroCutsceneTrigger itself now, so QA no
            // longer needs to (and must not) set the seen-flag - that leaked into the shared
            // PlayerPrefs store and blocked the intro in real same-machine builds.
            PlayerPrefs.Save();

            // Start each QA run from a clean save so the live PlayerSave auto-load is
            // deterministic (a prior run's roundtrip test wrote this account's file).
            Persistence.PlayerPersistence.Delete(Persistence.PlayerIdentity.AccountId);

            var args = System.Environment.GetCommandLineArgs();
            int dirArg = System.Array.IndexOf(args, "-qadir");
            outDir = dirArg >= 0 && dirArg + 1 < args.Length
                ? args[dirArg + 1]
                : Path.Combine(Application.persistentDataPath, "qa");
            Directory.CreateDirectory(outDir);
            Debug.Log($"QAScreenshotDriver: writing shots to {outDir}");

            while (NetworkManager.singleton == null) yield return null;
            var manager = NetworkManager.singleton as Multiplayer.AldenNetworkManager;
            if (manager == null) { Fail("no AldenNetworkManager"); yield break; }

            manager.SetLocalLogin("localhost", manager.RequiredPassword,
                new Multiplayer.AldenPlayerProfile { playerName = "QA", gender = 0, skinTone = 0, hairStyle = 1, hairColor = 0, accountId = Persistence.PlayerIdentity.AccountId });
            manager.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 30f)
            {
                wait += Time.deltaTime;
                yield return null;
            }
            if (NetworkClient.localPlayer == null) { Fail("player never spawned"); yield break; }

            var player = NetworkClient.localPlayer.gameObject;
            player.GetComponent<Combat.Health>()?.SetInvulnerable(true); // QA rig doesn't die mid-shoot

            // Let the runtime NavMesh bake, spawners, and atmosphere settle.
            yield return new WaitForSeconds(5f);

            // Functional gameplay self-test: proves the JSON-driven data actually
            // works in a live host (quests complete, items equip and change stats,
            // abilities/config load), not just that it parses. Logs [QATEST] lines.
            RunFunctionalTests(player);

            if (uiMode)
            {
                yield return CaptureUiPanels(player);
                Debug.Log("QAScreenshotDriver: UI pass done.");
                yield return new WaitForSeconds(0.5f);
                Application.Quit(0);
                yield break;
            }

            foreach (var (name, pos, yaw) in Shots)
            {
                Teleport(player, pos, yaw);
                yield return new WaitForSeconds(2.2f);   // camera SmoothDamp + LODs settle
                yield return Capture(name);
            }

            // ---- Eruption sequence: phase flips fire the real climax cutscenes ----
            var world = World.EmberfallWorldState.Instance;
            var director = player.GetComponent<Cinematics.CutsceneDirector>();
            if (world != null)
            {
                Teleport(player, new Vector3(0f, 1.5f, -150f), 180f);
                yield return new WaitForSeconds(1f);
                world.DebugForcePhase(World.EmberfallWorldState.Phase.Erupting);
                yield return new WaitForSeconds(4f);     // inside the volcano establishing shot
                yield return Capture("13_cutscene_eruption");
                yield return WaitForCutsceneEnd(director);

                Teleport(player, new Vector3(0f, 1.5f, 14f), 180f);
                yield return new WaitForSeconds(2.5f);
                yield return Capture("14_city_under_eruption");

                world.DebugForcePhase(World.EmberfallWorldState.Phase.Cleared);
                yield return new WaitForSeconds(4f);
                yield return Capture("15_cutscene_floor_cleared");
                yield return WaitForCutsceneEnd(director);

                Teleport(player, new Vector3(0f, 1.5f, -438f), 180f);  // stairway beyond the boss
                yield return new WaitForSeconds(2.2f);
                yield return Capture("16_floor2_gate_open");
            }

            Debug.Log("QAScreenshotDriver: done.");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// -qaui: seed a believable bag/wallet, then open and capture every player-facing
        /// panel one at a time (the source material for UI quality passes).
        /// </summary>
        private IEnumerator CaptureUiPanels(GameObject player)
        {
            // Believable state so panels aren't empty husks.
            var inventory = player.GetComponent<Items.Inventory>();
            var wallet = player.GetComponent<Items.Wallet>();
            wallet?.Add(347);
            foreach (var id in new[] { "knight_sword+2", "knight_sword+2", "iron_helm", "raw_trout", "cooked_fish", "fishing_pole", "apprentice_focus" })
                inventory?.Add(id);

            yield return Capture("ui_01_hud_baseline");

            var hotkeys = FindFirstObjectByType<UI.PanelHotkeys>();
            if (hotkeys != null)
            {
                hotkeys.ToggleCharacter();
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_02_character_inventory");
                hotkeys.ToggleCharacter();

                hotkeys.ToggleSkills();
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_03_skills");
                hotkeys.ToggleSkills();

                hotkeys.ToggleTrees();
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_04_combat_trees");
                hotkeys.ToggleTrees();
                yield return new WaitForSeconds(0.4f);
            }

            var tracker = player.GetComponent<UI.QuestTrackerHUD>();
            if (tracker != null)
            {
                tracker.SendMessage("ToggleLog", SendMessageOptions.DontRequireReceiver);
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_05_quest_log");
                tracker.SendMessage("ToggleLog", SendMessageOptions.DontRequireReceiver);
            }

            var forge = player.GetComponent<UI.ForgePanel>();
            if (forge != null)
            {
                forge.Open(null);
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_06_forge");
                forge.Close();
            }

            var vendorPanel = player.GetComponent<UI.VendorPanel>();
            if (vendorPanel != null)
            {
                vendorPanel.Open(player.transform,
                    new[] { "cooked_fish", "cooked_trout", "fishing_pole", "leather_cap", "iron_helm", "apprentice_focus" });
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_07_vendor");
                vendorPanel.Close();
            }

            bool chosen = false;
            UI.WeaponChoicePanel.Present(player, () => chosen = true);
            yield return new WaitForSeconds(0.8f);
            yield return Capture("ui_08_weapon_choice");
            // Close it by picking melee via reflection-free SendMessage fallback.
            var choice = FindFirstObjectByType<UI.WeaponChoicePanel>();
            choice?.SendMessage("ChooseMelee", SendMessageOptions.DontRequireReceiver);
            yield return new WaitForSeconds(0.4f);

            var cheats = player.GetComponent<UI.DebugCheatPanel>();
            if (cheats != null)
            {
                cheats.SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_09_dev_tester");
                cheats.SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
            }
        }

        private IEnumerator WaitForCutsceneEnd(Cinematics.CutsceneDirector director)
        {
            float t = 0f;
            while (director != null && director.IsPlaying && t < 60f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(1f);
        }

        private void Teleport(GameObject player, Vector3 pos, float yaw)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null) cc.enabled = true;

            Camera.main?.GetComponent<Player.ThirdPersonCamera>()?.SnapToAngle(yaw, 14f);
        }

        private IEnumerator Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSeconds(0.6f);       // capture lands end-of-frame; give it air
            Debug.Log($"QAScreenshotDriver: captured {name}");
        }

        private void Fail(string why)
        {
            Debug.LogError($"QAScreenshotDriver: FAILED, {why}");
            Application.Quit(2);
        }

        // ==================== FUNCTIONAL SELF-TEST ====================

        private int qaPass, qaFail;

        private void Check(string name, bool ok)
        {
            if (ok) { qaPass++; Debug.Log($"[QATEST] PASS  {name}"); }
            else { qaFail++; Debug.LogError($"[QATEST] FAIL  {name}"); }
        }

        private void RunFunctionalTests(GameObject player)
        {
            Debug.Log("[QATEST] === functional gameplay tests (live host) ===");

            // Config loaded and driving CombatMath.
            Check("config levelGrowth == 1.085", Mathf.Approximately(Stats.CombatMath.LevelGrowth, 1.085f));
            Check("config vendor markup == 4", Mathf.Approximately(Data.GameConfig.Economy.vendorBuyMultiplier, 4f));

            // Skill registry.
            Check("skill Attack display == 'Attack'", Progression.SkillRegistry.Get(Progression.SkillType.Attack).display == "Attack");

            // Item database + string enums + bonuses.
            var ks = Items.ItemDatabase.Get("knight_sword");
            Check("item knight_sword loaded, slot Weapon", ks != null && ks.Slot == Items.EquipSlot.Weapon);
            Check("item knight_sword WeaponPower == 12", ks != null && Mathf.Approximately(ks.Bonus(Stats.StatType.WeaponPower), 12f));
            var ef = Items.ItemDatabase.Get("ember_focus");
            Check("item ember_focus slot Focus, SpellPower 10", ef != null && ef.Slot == Items.EquipSlot.Focus && Mathf.Approximately(ef.Bonus(Stats.StatType.SpellPower), 10f));

            // Phase 6 archery.
            var bow = Items.ItemDatabase.Get("emberstring_bow");
            Check("emberstring_bow loads WeaponKind.Bow", bow != null && bow.WeaponKind == Items.WeaponKind.Bow);
            var arrowDef = Items.ItemDatabase.Get("arrow");
            Check("arrow stacks (MaxStack>1)", arrowDef != null && arrowDef.MaxStack > 1);
            Check("SkillRegistry has Ranged (Archery)", Progression.SkillRegistry.Get(Progression.SkillType.Ranged).display == "Archery");
            var bossQ = Quests.QuestDatabase.Get("q_floor_boss");
            Check("q_floor_boss offers melee/magic/archery (3 choices)", bossQ != null && bossQ.RewardChoices.Count == 3);

            // Phase 7 gather/craft skills.
            Check("SkillRegistry has Mining", Progression.SkillRegistry.Get(Progression.SkillType.Mining).display == "Mining");
            Check("SkillRegistry has Smithing", Progression.SkillRegistry.Get(Progression.SkillType.Smithing).display == "Smithing");
            Check("SkillRegistry has Health", Progression.SkillRegistry.Get(Progression.SkillType.Health).display == "Health");
            Check("recipes load (>=6)", Items.RecipeDatabase.All.Count >= 6);
            Check("anvil has smithing recipes", Items.RecipeDatabase.ForStation("anvil").Count >= 1);
            var oreItem = Items.ItemDatabase.Get("iron_ore");
            Check("iron_ore stacks", oreItem != null && oreItem.MaxStack > 1);
            Check("iron_bar item loads", Items.ItemDatabase.Get("iron_bar") != null);
            Check("resource nodes placed (>=5)", FindObjectsByType<World.ResourceNode>(FindObjectsSortMode.None).Length >= 5);
            Check("craft stations placed (>=2)", FindObjectsByType<World.CraftingStation>(FindObjectsSortMode.None).Length >= 2);

            // Phase 9 equipment slots + jewelry.
            var gl = Items.ItemDatabase.Get("iron_gloves");
            Check("iron_gloves slot Gloves", gl != null && gl.Slot == Items.EquipSlot.Gloves);
            var neck = Items.ItemDatabase.Get("ember_necklace");
            Check("ember_necklace slot Necklace, MaxHealth 20", neck != null && neck.Slot == Items.EquipSlot.Necklace && Mathf.Approximately(neck.Bonus(Stats.StatType.MaxHealth), 20f));
            bool bossNeck = false;
            var fbq = Quests.QuestDatabase.Get("q_floor_boss");
            if (fbq != null) foreach (var r in fbq.Rewards) if (r.itemId == "ember_necklace") bossNeck = true;
            Check("boss grants first-necklace roll", bossNeck);
            var eq9 = player.GetComponent<Items.Equipment>();
            var iv9 = player.GetComponent<Items.Inventory>();
            var st9 = player.GetComponent<Stats.StatBlock>();
            if (eq9 != null && iv9 != null && st9 != null)
            {
                iv9.Add("band_of_embers"); eq9.Equip("band_of_embers");
                Check("first ring fills Ring1", eq9.GetEquipped(Items.EquipSlot.Ring1) == "band_of_embers");
                iv9.Add("copper_ring"); eq9.Equip("copper_ring");
                Check("second ring fills Ring2 (dual-ring)", eq9.GetEquipped(Items.EquipSlot.Ring2) == "copper_ring");
                iv9.Add("iron_gloves");
                float armorBefore = st9.Get(Stats.StatType.Armor);
                eq9.Equip("iron_gloves");
                Check("equipping gloves raises Armor", st9.Get(Stats.StatType.Armor) > armorBefore);
            }
            else Check("player has equipment/inventory/statblock (P9)", false);

            // Ability database + enums/flags.
            var fb = Abilities.AbilityDatabase.Get("fireball");
            Check("ability fireball loaded, kind Projectile", fb != null && fb.Kind == Abilities.AbilityKind.Projectile);
            var bash = Abilities.AbilityDatabase.Get("bash");
            Check("ability bash has Stunned flag", bash != null && (bash.StatusFlags & Combat.StatusFlags.Stunned) != 0);

            // Phase 8 ability mastery ranks 1-6.
            Check("AbilityMastery MaxRank == 6", Abilities.AbilityMastery.MaxRank == 6);
            var meteor = Abilities.AbilityDatabase.Get("meteor_strike");
            Check("meteor_strike loads rankMods", meteor != null && meteor.rankMods.Count >= 3);
            var mast = player.GetComponent<Abilities.AbilityMastery>();
            if (mast != null)
            {
                mast.ResetAll();
                for (int i = 0; i < 20; i++) mast.AddUse("meteor_strike");   // -> rank 3 (>=15 uses)
                Check("mastery rank climbs with use (>=3)", mast.RankFor("meteor_strike") >= 3);
                Check("mastery resolves rankMod (radius bonus at rank 3)", mast.Modifiers("meteor_strike").bonusRadius > 0f);
                Check("mastery DamageMultiplier > 1 at higher rank", mast.DamageMultiplier("meteor_strike") > 1f);
            }
            else Check("player has AbilityMastery", false);

            // Inventory + equip -> live StatBlock change.
            var inv = player.GetComponent<Items.Inventory>();
            var equip = player.GetComponent<Items.Equipment>();
            var stats = player.GetComponent<Stats.StatBlock>();
            if (inv != null && equip != null && stats != null)
            {
                inv.Add("knight_sword");
                Check("inventory holds knight_sword after Add", inv.CountOf("knight_sword") >= 1);
                float before = stats.Get(Stats.StatType.WeaponPower);
                equip.Equip("knight_sword");
                float after = stats.Get(Stats.StatType.WeaponPower);
                Check("equipping knight_sword raises WeaponPower", after > before);
            }
            else Check("player has inventory/equipment/statblock", false);

            // Quest flow end-to-end: accept -> advance -> turn in -> reward + chain.
            var log = player.GetComponent<Quests.QuestLog>();
            var wallet = player.GetComponent<Items.Wallet>();
            if (log != null && wallet != null)
            {
                int goldBefore = wallet.Gold;
                log.Accept("q_intro");
                Check("q_intro accepted (Active)", log.StatusOf("q_intro") == Quests.QuestStatus.Active);
                log.Advance(Quests.ObjectiveKind.TalkToNpc, "aldric");
                var st = log.StatusOf("q_intro");
                Check("q_intro objectives satisfied", st == Quests.QuestStatus.Complete || st == Quests.QuestStatus.TurnedIn);
                bool turned = log.TurnIn("q_intro");
                Check("q_intro turned in", turned && log.StatusOf("q_intro") == Quests.QuestStatus.TurnedIn);
                Check("q_intro granted +10 gold", wallet.Gold >= goldBefore + 10);
                // Q2 is a giver quest at Aldric, so it becomes offerable (not auto-chained).
                Check("Q2 q_choose_edge now offerable", log.CanAccept("q_choose_edge"));
            }
            else Check("player has questlog/wallet", false);

            // Reward-choice grant path (data-driven; the panel click routes through this).
            var inv2 = player.GetComponent<Items.Inventory>();
            if (inv2 != null)
            {
                var opt = new Quests.QuestRewardOption
                {
                    style = "archery",
                    items = new System.Collections.Generic.List<Quests.RewardEntry>
                    { new Quests.RewardEntry { itemId = "adept_focus", count = 1 } }
                };
                int before = inv2.CountOf("adept_focus");
                UI.RewardChoicePanel.Grant(player, opt);
                Check("reward-choice Grant adds the chosen item", inv2.CountOf("adept_focus") == before + 1);
            }

            // Phase 3 chain + set-pieces.
            var gate = Quests.QuestDatabase.Get("q_gate");
            Check("q_gate uses DefendEvent kind", gate != null && gate.Objectives.Count > 0 && gate.Objectives[0].Kind == Quests.ObjectiveKind.DefendEvent);
            var prep = Quests.QuestDatabase.Get("q_caldera_prep");
            Check("q_caldera_prep is any-2-of", prep != null && prep.RequiredObjectiveCount == 2);
            var beyond = Quests.QuestDatabase.Get("q_beyond");
            Check("mainline ends at q_beyond (Q16)", beyond != null && beyond.PrereqQuestId == "q_breach");
            int sealStones = 0, emptyGraves = 0;
            foreach (var m in FindObjectsByType<World.InspectableMarker>(FindObjectsSortMode.None))
            {
                if (m.MarkerId == "seal_stone") sealStones++;
                if (m.MarkerId == "empty_grave") emptyGraves++;
            }
            Check("3 seal stones placed", sealStones == 3);
            Check("empty grave marker placed", emptyGraves == 1);
            int questCount = 0;
            foreach (var _ in Quests.QuestDatabase.All) questCount++;
            Check("24 quests loaded (16 main + 8 side)", questCount == 24);
            Check("side quest sq_dire_hunt loads", Quests.QuestDatabase.Get("sq_dire_hunt") != null);
            bool hasGate = FindObjectsByType<World.DefendZone>(FindObjectsSortMode.None).Length >= 1;
            Check("gate DefendZone present", hasGate);

            // Phase 4 dungeon depth + eruption.
            var mobList = AldenFall.Data.MonsterDatabase.LoadAll();
            var boss = mobList.Find(m => m.id == "cave_troll_boss");
            Check("boss reframed Cinderbound Guardian", boss != null && boss.displayName == "Cinderbound Guardian");
            Check("EruptionSpawns present", FindObjectsByType<World.EruptionSpawns>(FindObjectsSortMode.None).Length >= 1);
            int seals = FindObjectsByType<World.SealableDoor>(FindObjectsSortMode.None).Length;
            Check("dungeon has >=2 sealable doors", seals >= 2);

            // Named NPC roster loaded + placed in the scene.
            Check("npc roster loads 7", World.NpcRoster.LoadAll().Count == 7);
            bool hasElowen = false;
            foreach (var g in FindObjectsByType<World.QuestGiverNPC>(FindObjectsSortMode.None))
                if (g.NpcId == "elowen") hasElowen = true;
            Check("Elowen quest giver placed in scene", hasElowen);

            // Phase 10 persistence: save/load roundtrip + stable identity.
            var save = new Persistence.PlayerSaveData
            {
                accountId = "qatest_account",
                gold = 777,
                skillLevels = new int[] { 5, 3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1 }
            };
            Persistence.PlayerPersistence.Save(save);
            var loaded = Persistence.PlayerPersistence.Load("qatest_account");
            Check("persistence roundtrip: gold", loaded != null && loaded.gold == 777);
            Check("persistence roundtrip: skills", loaded != null && loaded.skillLevels != null && loaded.skillLevels.Length == 12 && loaded.skillLevels[0] == 5);
            Check("player identity is stable + non-empty", !string.IsNullOrEmpty(Persistence.PlayerIdentity.AccountId));

            // Phase 10d: full capture -> save -> load -> apply roundtrip on the LIVE host
            // player via its PlayerSave component (gold + a skill actually restore).
            var pSave = player.GetComponent<Persistence.PlayerSave>();
            Check("live player has PlayerSave component", pSave != null);
            if (pSave != null)
            {
                var pWallet = player.GetComponent<Items.Wallet>();
                var pSkills = player.GetComponent<Progression.Skills>();
                pWallet?.SetGold(4321);
                pSkills?.Restore(Progression.SkillType.Strength, 7, 0f);
                pSave.ForceSave();                                    // writes PlayerIdentity account
                pWallet?.SetGold(0);
                pSkills?.Restore(Progression.SkillType.Strength, 1, 0f);
                pSave.ForceLoad();                                    // pulls it back and applies
                Check("PlayerSave restores gold", pWallet != null && pWallet.Gold == 4321);
                Check("PlayerSave restores skill level", pSkills != null && pSkills.Level(Progression.SkillType.Strength) == 7);

                // Phase 13: accountId plumbs login profile -> avatar -> PlayerSave (so the
                // dedicated server can persist each player under their own id, not just the host).
                var avatar13 = player.GetComponent<Multiplayer.NetworkPlayerAvatar>();
                Check("login profile plumbs accountId to avatar", avatar13 != null && avatar13.AccountId == Persistence.PlayerIdentity.AccountId);
                Check("PlayerSave resolves per-account id", pSave.AccountId == Persistence.PlayerIdentity.AccountId);
            }

            // Phase 11: server-wide floor progress + first-clear prestige.
            var progress = World.ServerProgressState.Instance;
            Check("ServerProgressState present in scene", progress != null);
            Check("city progress board placed in scene", FindObjectsByType<World.ProgressBoard>(FindObjectsSortMode.None).Length >= 1);
            if (progress != null)
            {
                Check("floor not cleared at start", !progress.FloorCleared && progress.BossClears == 0);
                var pStatus = player.GetComponent<Combat.StatusSystem>();
                var expectName = player.GetComponent<Multiplayer.NetworkPlayerAvatar>()?.DisplayName;
                string wanted = string.IsNullOrWhiteSpace(expectName) ? "A hero of Emberfall" : expectName;

                progress.RegisterBossClear(player);
                Check("first boss clear sets FloorCleared + count", progress.FloorCleared && progress.BossClears == 1);
                Check("first clear records clearer name", progress.FirstClearName == wanted);
                Check("first clear grants Hope of Emberfall buff", pStatus != null && pStatus.Has(World.ServerProgressState.HopeBuffId));

                progress.RegisterBossClear(player);
                Check("later clears bump count, keep first-clearer", progress.BossClears == 2 && progress.FirstClearName == wanted);

                // Per-player clear flag (11d): set on clear + persists through save/load.
                var pProg = player.GetComponent<Progression.PlayerProgress>();
                Check("boss clear marks player Floor1Cleared", pProg != null && pProg.Floor1Cleared);
                if (pProg != null && pSave != null)
                {
                    pSave.ForceSave();
                    pProg.Restore(false);
                    pSave.ForceLoad();
                    Check("Floor1Cleared persists through save/load", pProg.Floor1Cleared);
                }
            }

            // Phase 13: KillMob quest credit now routes through PlayerCombat (host inline /
            // pure-client TargetRpc), closing the dedicated-server gap where a client's kill
            // quests never advanced. Host path is behaviour-identical to before (the quest-flow
            // test above covers advancement); the relay must exist and be safe to call.
            var combat13 = player.GetComponent<Combat.PlayerCombat>();
            Check("PlayerCombat present (KillMob quest relay)", combat13 != null);
            if (combat13 != null)
            {
                combat13.ServerAdvanceKillQuest("");         // empty -> no-op
                combat13.ServerAdvanceKillQuest("grukmar");  // no active quest -> safe no-op
                Check("ServerAdvanceKillQuest safe no-ops", true);
            }

            // v9 Cluster 1: forge opens via typed Open()/Close() (no SendMessage broadcast that
            // also toggled the F9 dev panel), and the intro seen-key was bumped to invalidate
            // the contaminated flag so the intro replays.
            var forgeP = player.GetComponent<UI.ForgePanel>();
            Check("player has ForgePanel", forgeP != null);
            if (forgeP != null)
            {
                forgeP.Open(null);
                Check("forge Open() shows panel", forgeP.IsOpen);
                forgeP.Close();
                Check("forge Close() hides panel", !forgeP.IsOpen);
            }
            Check("intro seen-key bumped (v4)", Cinematics.IntroCutsceneTrigger.SeenKey == "cutscene_intro_seen_v4");

            // Safe zones baked into the scene (pond + city peaceful, forest not).
            Check("city center is a safe zone", World.SafeZone.IsInAnySafeZone(Vector3.zero));
            Check("lake center is a safe zone", World.SafeZone.IsInAnySafeZone(new Vector3(-138f, 0f, 138f)));
            Check("deep forest is NOT a safe zone", !World.SafeZone.IsInAnySafeZone(new Vector3(-180f, 0f, 0f)));

            if (qaFail == 0) Debug.Log($"[QATEST] === ALL {qaPass} PASSED ===");
            else Debug.LogError($"[QATEST] === {qaPass} passed, {qaFail} FAILED ===");
        }
    }
}
