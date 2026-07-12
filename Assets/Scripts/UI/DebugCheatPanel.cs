using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using AldenFall.Combat;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// F9 dev tester: a tabbed panel covering everything a playtest needs to poke —
    /// SKILLS (level editing), WORLD (eruption phase, atmosphere, teleports),
    /// SCENES (play any StreamingAssets cutscene, reset the intro flag),
    /// ANIMS (CrossFade any animator state on the local player), and
    /// ITEMS (gold + test gear). Built entirely at runtime on its own canvas so it
    /// never touches the real HUD build. Lives on the player. HEAL/GOD live in a
    /// footer visible on every tab.
    ///
    /// World phase is server-authoritative: on a host we call the world-state
    /// directly, on a pure client it relays through
    /// NetworkPlayerAvatar.CmdDebugSetWorldPhase.
    /// </summary>
    public class DebugCheatPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F9;

        private Skills skills;
        private Health health;
        private Items.Inventory inventory;
        private Items.Wallet wallet;

        private GameObject panelRoot;
        private RectTransform contentParent;
        private bool godMode;
        private string activeTab = "SKILLS";
        private readonly Dictionary<string, TMP_Text> tabLabels = new Dictionary<string, TMP_Text>();
        private readonly Dictionary<SkillType, TMP_Text> levelLabels = new Dictionary<SkillType, TMP_Text>();
        private TMP_Text godLabel;

        private static readonly string[] Tabs = { "SKILLS", "WORLD", "SCENES", "ANIMS", "ITEMS" };

        // Named teleports matching the V2 radial world (see ArenaBuilderEmberfall).
        private static readonly (string label, Vector3 pos)[] Teleports =
        {
            ("Glade (spawn)",  new Vector3(0f, 1.5f, 84f)),
            ("City Plaza",     new Vector3(0f, 1.5f, 8f)),
            ("Lake Bank",      new Vector3(-103f, 1.5f, 154f)),
            ("Dark Woods",     new Vector3(0f, 1.5f, 195f)),
            ("Goblin Camp",    new Vector3(-190f, 1.5f, 0f)),
            ("Bandit Camp",    new Vector3(190f, 1.5f, 0f)),
            ("Orc Warcamp",    new Vector3(-105f, 1.5f, -114f)),
            ("Ember Approach", new Vector3(0f, 1.5f, -170f)),
            ("Volcano Mouth",  new Vector3(0f, 1.5f, -308f)),
            ("Boss Chamber",   new Vector3(0f, 1.5f, -430f)),
        };

        // The shared mocap vocabulary (MeshyCharacterImporter states). Missing states
        // just log "State could not be found" — harmless in a dev tool.
        private static readonly string[] AnimStates =
        {
            "Locomotion", "Attack", "Cast", "Dodge", "Jump", "Hit", "Eat", "Death",
            "Dance", "DanceHipHop", "DanceBounce", "DanceShake",
            "DanceRobot", "DanceGangnam", "DanceBreak", "DanceTwerk", "DanceFunny",
        };

        private static readonly (string label, string itemId)[] TestItems =
        {
            ("Knight Sword +3", "knight_sword+3"),
            ("Emberfang",       "emberfang"),
            ("Ember Focus",     "ember_focus"),
            ("Iron Helm",       "iron_helm"),
            ("Iron Chest",      "iron_chestplate"),
            ("Iron Legs",       "iron_legs"),
            ("Fishing Pole",    "fishing_pole"),
            ("Cooked Fish",     "cooked_fish"),
        };

        private void Awake()
        {
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
            inventory = GetComponent<Items.Inventory>();
            wallet = GetComponent<Items.Wallet>();
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(toggleKey))
                Toggle();

            if (panelRoot != null && panelRoot.activeSelf && activeTab == "SKILLS")
                RefreshLabels();
        }

        private void Toggle()
        {
            if (panelRoot == null)
                BuildPanel();
            else
                panelRoot.SetActive(!panelRoot.activeSelf);
        }

        private void RefreshLabels()
        {
            if (skills == null) return;
            foreach (var (skill, label) in levelLabels)
                if (label != null)
                    label.text = $"{Skills.DisplayName(skill)}  <b>{skills.Level(skill)}</b>";
        }

        // ==================== ACTIONS ====================

        private void SetWorldPhase(World.EmberfallWorldState.Phase phase)
        {
            var world = World.EmberfallWorldState.Instance;
            if (world == null) { Say("No world-state in scene."); return; }

            if (NetworkServer.active)
                world.DebugForcePhase(phase);
            else
                GetComponent<Multiplayer.NetworkPlayerAvatar>()?.CmdDebugSetWorldPhase((int)phase);
            Say($"World phase → {phase}");
        }

        private void Teleport(Vector3 pos)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                transform.position = pos;
                cc.enabled = true;
            }
            else
                transform.position = pos;
        }

        private void PlayCutscene(string id)
        {
            var director = GetComponent<Cinematics.CutsceneDirector>();
            if (director == null) { Say("No CutsceneDirector on player."); return; }
            panelRoot.SetActive(false);   // the scene takes the screen; F9 to reopen after
            if (!director.Play(id))
                Say($"Couldn't play '{id}'.");
        }

        private void PlayAnim(string state)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) { Say("No Animator on player."); return; }
            animator.CrossFadeInFixedTime(state, 0.1f);
        }

        private static IEnumerable<string> CutsceneIds()
        {
            string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "cutscenes");
            if (!System.IO.Directory.Exists(dir)) yield break;
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.json"))
                yield return System.IO.Path.GetFileNameWithoutExtension(file);
        }

        private void Say(string msg) =>
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f, msg, UITheme.TextGold);

        // ==================== RUNTIME UI ====================

        private void BuildPanel()
        {
            var canvasObj = new GameObject("DebugCheatCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300; // above everything
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();

            panelRoot = canvasObj;

            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(580, 640));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.05f, 0.09f, 0.96f);

            MakeText(panel, "Title", "DEV TESTER  <size=12><color=#8A8494>(F9 to close — testing only)</color></size>",
                new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(540, 30), 19, UITheme.TextGold, TextAlignmentOptions.Center);

            // Tab strip
            float tx = 60f;
            foreach (var tab in Tabs)
            {
                string captured = tab;
                var label = MakeButton(panel, $"tab_{tab}", tab, new Vector2(tx, -56f), 92, () =>
                {
                    activeTab = captured;
                    RebuildContent();
                });
                tabLabels[tab] = label;
                tx += 98f;
            }

            contentParent = Rect(panel, "Content", new Vector2(0.5f, 1f), new Vector2(0, -330),
                new Vector2(560, 480));

            // Footer: always-available survival cheats
            MakeButton(panel, "Heal", "HEAL", new Vector2(160, -600f), 120, () => health?.ResetHealth());
            godLabel = MakeButton(panel, "God", "GOD: OFF", new Vector2(300, -600f), 120, () =>
            {
                godMode = !godMode;
                health?.SetInvulnerable(godMode);
                if (godLabel != null)
                    godLabel.text = godMode ? "GOD: ON" : "GOD: OFF";
            });

            RebuildContent();
        }

        private void RebuildContent()
        {
            foreach (var (tab, label) in tabLabels)
                if (label != null)
                    label.color = tab == activeTab ? UITheme.TextGold : UITheme.TextMain;

            levelLabels.Clear();
            for (int i = contentParent.childCount - 1; i >= 0; i--)
                Destroy(contentParent.GetChild(i).gameObject);

            switch (activeTab)
            {
                case "SKILLS": BuildSkillsTab(); break;
                case "WORLD":  BuildWorldTab();  break;
                case "SCENES": BuildScenesTab(); break;
                case "ANIMS":  BuildAnimsTab();  break;
                case "ITEMS":  BuildItemsTab();  break;
            }
        }

        private void BuildSkillsTab()
        {
            float y = -14f;
            foreach (SkillType skill in System.Enum.GetValues(typeof(SkillType)))
            {
                var captured = skill; // avoid foreach-variable capture surprises
                var label = MakeText(contentParent, $"Lv_{skill}", "", new Vector2(0f, 1f),
                    new Vector2(120, y), new Vector2(180, 30), 15, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                levelLabels[skill] = label;

                MakeButton(contentParent, $"{skill}_minus", "−1", new Vector2(268, y), 48,
                    () => skills?.SetLevel(captured, skills.Level(captured) - 1));
                MakeButton(contentParent, $"{skill}_plus", "+1", new Vector2(322, y), 48,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 1));
                MakeButton(contentParent, $"{skill}_plus10", "+10", new Vector2(376, y), 48,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 10));
                MakeButton(contentParent, $"{skill}_max", "99", new Vector2(430, y), 48,
                    () => skills?.SetLevel(captured, Skills.MaxLevel));

                y -= 44f;
            }

            y -= 8f;
            MakeButton(contentParent, "AllMax", "ALL 99", new Vector2(200, y), 110, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, Skills.MaxLevel);
            });
            MakeButton(contentParent, "AllReset", "ALL 1", new Vector2(330, y), 110, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, 1);
            });

            RefreshLabels();
        }

        private void BuildWorldTab()
        {
            float y = -14f;
            var world = World.EmberfallWorldState.Instance;
            MakeText(contentParent, "PhaseHdr",
                $"World phase: <b>{(world != null ? world.CurrentPhase.ToString() : "—")}</b>  <size=11><color=#8A8494>(server-wide, replicated to everyone)</color></size>",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(520, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 34f;
            MakeButton(contentParent, "PhaseCalm", "CALM", new Vector2(90, y), 140,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Calm); RebuildContent(); });
            MakeButton(contentParent, "PhaseErupt", "ERUPTING", new Vector2(250, y), 140,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Erupting); RebuildContent(); });
            MakeButton(contentParent, "PhaseClear", "CLEARED", new Vector2(410, y), 140,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Cleared); RebuildContent(); });

            y -= 46f;
            MakeText(contentParent, "AtmoHdr",
                "Atmosphere override  <size=11><color=#8A8494>(this screen only — the phase above resets it)</color></size>",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(520, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 34f;
            foreach (var (label, v) in new[] { ("CLEAR 0%", 0f), ("HAZE 35%", 0.35f), ("HEAVY 70%", 0.7f), ("MAX 100%", 1f) })
            {
                float captured = v;
                float bx = 90f + System.Array.IndexOf(new[] { 0f, 0.35f, 0.7f, 1f }, v) * 122f;
                MakeButton(contentParent, $"Atmo_{v}", label, new Vector2(bx, y), 112,
                    () => World.EmberAtmosphere.Instance?.SetGlobalIntensity(captured));
            }

            y -= 46f;
            MakeText(contentParent, "TpHdr", "Teleport",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(520, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 34f;
            for (int i = 0; i < Teleports.Length; i++)
            {
                var (label, pos) = Teleports[i];
                Vector3 captured = pos;
                float bx = 100f + (i % 3) * 175f;
                MakeButton(contentParent, $"Tp_{i}", label, new Vector2(bx, y), 165, () => Teleport(captured));
                if (i % 3 == 2) y -= 38f;
            }
        }

        private void BuildScenesTab()
        {
            float y = -14f;
            MakeText(contentParent, "SceneHdr",
                "Cutscenes  <size=11><color=#8A8494>(plays on this client; SPACE/ESC skips; F9 reopens after)</color></size>",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(520, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 36f;

            int shown = 0;
            foreach (var id in CutsceneIds())
            {
                string captured = id;
                MakeText(contentParent, $"cs_lbl_{id}", captured, new Vector2(0f, 1f),
                    new Vector2(30, y), new Vector2(300, 28), 14, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                MakeButton(contentParent, $"cs_btn_{id}", "PLAY", new Vector2(420, y), 100,
                    () => PlayCutscene(captured));
                y -= 36f;
                shown++;
            }
            if (shown == 0)
                MakeText(contentParent, "NoScenes", "<color=#8A8494>No cutscene JSONs found.</color>",
                    new Vector2(0f, 1f), new Vector2(30, y), new Vector2(400, 28), 14, UITheme.TextDim, TextAlignmentOptions.MidlineLeft);

            y -= 14f;
            MakeButton(contentParent, "ResetIntro", "RESET INTRO FLAG (replays next login)", new Vector2(230, y), 420, () =>
            {
                PlayerPrefs.DeleteKey("cutscene_intro_seen_v2");
                PlayerPrefs.DeleteKey("starter_path_v1");
                PlayerPrefs.Save();
                Say("Intro will replay on next login.");
            });
        }

        private void BuildAnimsTab()
        {
            float y = -14f;
            MakeText(contentParent, "AnimHdr",
                "Animator states  <size=11><color=#8A8494>(CrossFade on YOUR avatar; Locomotion recovers; missing states just log)</color></size>",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(540, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 36f;

            for (int i = 0; i < AnimStates.Length; i++)
            {
                string captured = AnimStates[i];
                float bx = 100f + (i % 3) * 175f;
                MakeButton(contentParent, $"anim_{captured}", captured, new Vector2(bx, y), 165,
                    () => PlayAnim(captured));
                if (i % 3 == 2) y -= 38f;
            }
        }

        private void BuildItemsTab()
        {
            float y = -14f;
            MakeText(contentParent, "GoldHdr",
                $"Gold: <b>{(wallet != null ? wallet.Gold : 0)}</b>",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(300, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            MakeButton(contentParent, "Gold100", "+100g", new Vector2(300, y), 90,
                () => { wallet?.Add(100); RebuildContent(); });
            MakeButton(contentParent, "Gold1000", "+1000g", new Vector2(400, y), 90,
                () => { wallet?.Add(1000); RebuildContent(); });

            y -= 44f;
            MakeText(contentParent, "GiveHdr", "Give items",
                new Vector2(0f, 1f), new Vector2(20, y), new Vector2(300, 26), 15, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            y -= 34f;
            for (int i = 0; i < TestItems.Length; i++)
            {
                var (label, id) = TestItems[i];
                string captured = id;
                float bx = 100f + (i % 3) * 175f;
                MakeButton(contentParent, $"give_{id}", label, new Vector2(bx, y), 165, () =>
                {
                    if (inventory != null && inventory.Add(captured)) Say($"+ {captured}");
                    else Say("Bag full.");
                });
                if (i % 3 == 2) y -= 38f;
            }
        }

        // ---- primitives ----

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string content,
            Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, Color color,
            TextAlignmentOptions alignment)
        {
            var rect = Rect(parent, name, anchor, pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private TMP_Text MakeButton(RectTransform parent, string name, string label,
            Vector2 pos, float width, UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(width, 32));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.22f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);

            return MakeText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, 32), 13, UITheme.TextMain, TextAlignmentOptions.Center);
        }
    }
}
