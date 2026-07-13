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
            PlayerPrefs.SetInt("cutscene_intro_seen_v2", 1);
            PlayerPrefs.Save();

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
                new Multiplayer.AldenPlayerProfile { playerName = "QA", gender = 0, skinTone = 0, hairStyle = 1, hairColor = 0 });
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
                forge.SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
                yield return new WaitForSeconds(0.6f);
                yield return Capture("ui_06_forge");
                forge.SendMessage("Toggle", SendMessageOptions.DontRequireReceiver);
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

            // Ability database + enums/flags.
            var fb = Abilities.AbilityDatabase.Get("fireball");
            Check("ability fireball loaded, kind Projectile", fb != null && fb.Kind == Abilities.AbilityKind.Projectile);
            var bash = Abilities.AbilityDatabase.Get("bash");
            Check("ability bash has Stunned flag", bash != null && (bash.StatusFlags & Combat.StatusFlags.Stunned) != 0);

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
                Check("q_learn_skill auto-chained to Active", log.StatusOf("q_learn_skill") == Quests.QuestStatus.Active);
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

            // Safe zones baked into the scene (pond + city peaceful, forest not).
            Check("city center is a safe zone", World.SafeZone.IsInAnySafeZone(Vector3.zero));
            Check("lake center is a safe zone", World.SafeZone.IsInAnySafeZone(new Vector3(-138f, 0f, 138f)));
            Check("deep forest is NOT a safe zone", !World.SafeZone.IsInAnySafeZone(new Vector3(-180f, 0f, 0f)));

            if (qaFail == 0) Debug.Log($"[QATEST] === ALL {qaPass} PASSED ===");
            else Debug.LogError($"[QATEST] === {qaPass} passed, {qaFail} FAILED ===");
        }
    }
}
