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
    /// F9 dev tester: a tabbed panel covering everything a playtest needs to poke -
    /// SKILLS (level editing), WORLD (eruption phase, atmosphere, teleports),
    /// SCENES (play any StreamingAssets cutscene, reset the intro flag),
    /// ANIMS (CrossFade any animator state on the local player), and
    /// ITEMS (gold + test gear). Built entirely at runtime on its own canvas so it
    /// never touches the real HUD build. Lives on the player. HEAL/GOD live in a
    /// footer visible on every tab. Chrome comes from <see cref="PanelChrome"/>.
    ///
    /// World phase is server-authoritative: on a host we call the world-state
    /// directly, on a pure client it relays through
    /// NetworkPlayerAvatar.CmdDebugSetWorldPhase.
    /// </summary>
    public class DebugCheatPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F9;

        private const float PanelWidth = 580f;
        private const float PanelHeight = 648f;
        private const float ContentWidth = PanelWidth - UITheme.Pad * 2f;

        private Skills skills;
        private Health health;
        private Items.Inventory inventory;
        private Items.Wallet wallet;

        private GameObject panelRoot;
        private RectTransform contentParent;
        private bool godMode;
        private string activeTab = "SKILLS";
        private readonly Dictionary<string, TMP_Text> tabLabels = new Dictionary<string, TMP_Text>();
        private readonly Dictionary<string, Button> tabButtons = new Dictionary<string, Button>();
        private readonly Dictionary<SkillType, TMP_Text> levelLabels = new Dictionary<SkillType, TMP_Text>();
        private TMP_Text godLabel;

        private static readonly string[] Tabs = { "SKILLS", "WORLD", "SCENES", "ANIMS", "ITEMS" };

        // Named teleports matching the V2 radial world (see ArenaBuilderEmberfall).
        private static readonly (string label, Vector3 pos)[] Teleports =
        {
            ("Glade (spawn)",  new Vector3(0f, 1.5f, 84f)),
            ("City Plaza",     new Vector3(0f, 1.5f, 8f)),
            ("Lake Bank",      new Vector3(-74f, 1.5f, 140f)),
            ("Dark Woods",     new Vector3(0f, 1.5f, 195f)),
            ("Goblin Camp",    new Vector3(-190f, 1.5f, 0f)),
            ("Bandit Camp",    new Vector3(190f, 1.5f, 0f)),
            ("Orc Warcamp",    new Vector3(-105f, 1.5f, -114f)),
            ("Ember Approach", new Vector3(0f, 1.5f, -170f)),
            ("Volcano Mouth",  new Vector3(0f, 1.5f, -308f)),
            ("Boss Chamber",   new Vector3(0f, 1.5f, -430f)),
        };

        // The shared mocap vocabulary (MeshyCharacterImporter states). Missing states
        // just log "State could not be found", harmless in a dev tool.
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

        private static TMP_Text LabelOf(Button button)
        {
            var child = button.transform.Find("Label");
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

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

            var panel = PanelChrome.CreatePanel(canvasObj.transform, "Panel",
                new Vector2(PanelWidth, PanelHeight));
            PanelChrome.Header(panel, "Dev Tester", "F9 closes, testing only");

            // Tab strip: five tabs on the 8px grid, centered as one block.
            const float tabW = 100f, tabGap = 8f;
            float tabX = (PanelWidth - (Tabs.Length * tabW + (Tabs.Length - 1) * tabGap)) / 2f;
            foreach (var tab in Tabs)
            {
                string captured = tab;
                var button = PanelChrome.MakeButton(panel, $"tab_{tab}", tab, tabX, 64f, tabW, () =>
                {
                    activeTab = captured;
                    RebuildContent();
                });
                var label = LabelOf(button);
                if (label != null) label.fontSize = UITheme.CaptionSize;
                tabLabels[tab] = label;
                tabButtons[tab] = button;
                tabX += tabW + tabGap;
            }

            contentParent = PanelChrome.Item(panel, "Content", UITheme.Pad, 122f,
                ContentWidth, PanelHeight - 122f - 82f);

            // Footer: always-available survival cheats, centered above a thin rule.
            var rule = PanelChrome.Item(panel, "FooterRule", UITheme.Pad,
                PanelHeight - 74f, ContentWidth, 1f);
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = UITheme.HeaderRule;
            ruleImage.raycastTarget = false;

            float footerY = PanelHeight - 58f;
            float footerX = (PanelWidth - (140f * 2f + 16f)) / 2f;
            PanelChrome.MakeButton(panel, "Heal", "HEAL", footerX, footerY, 140f,
                () => health?.ResetHealth(), primary: true);
            var god = PanelChrome.MakeButton(panel, "God", "GOD: OFF",
                footerX + 156f, footerY, 140f, null);
            godLabel = LabelOf(god);
            god.onClick.AddListener(() =>
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
                    label.color = tab == activeTab ? UITheme.TextGold : UITheme.TextDim;
            foreach (var (tab, button) in tabButtons)
            {
                if (button == null) continue;
                var colors = button.colors;
                colors.normalColor = tab == activeTab ? UITheme.ButtonHover : UITheme.ButtonBg;
                colors.selectedColor = colors.normalColor;
                button.colors = colors;
            }

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
            // Right-aligned button block on the 8px grid: 4 x 48 + 3 x 8 = 216.
            float bx = ContentWidth - 216f;
            float y = 0f;
            int rowIndex = 0;
            foreach (SkillType skill in System.Enum.GetValues(typeof(SkillType)))
            {
                var captured = skill; // avoid foreach-variable capture surprises
                var row = PanelChrome.Row(contentParent, $"Row_{skill}", rowIndex, y, ContentWidth);
                var label = PanelChrome.Text(row, $"Lv_{skill}", "", 12f, 10f, 180f, 20f,
                    UITheme.BodySize, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                levelLabels[skill] = label;

                PanelChrome.MakeButton(row, "minus", "−1", bx, 3f, 48f,
                    () => skills?.SetLevel(captured, skills.Level(captured) - 1));
                PanelChrome.MakeButton(row, "plus", "+1", bx + 56f, 3f, 48f,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 1));
                PanelChrome.MakeButton(row, "plus10", "+10", bx + 112f, 3f, 48f,
                    () => skills?.SetLevel(captured, skills.Level(captured) + 10));
                PanelChrome.MakeButton(row, "max", "99", bx + 168f, 3f, 48f,
                    () => skills?.SetLevel(captured, Skills.MaxLevel));

                y += UITheme.RowHeight;
                rowIndex++;
            }

            y += 16f;
            float allX = (ContentWidth - (120f * 2f + 16f)) / 2f;
            PanelChrome.MakeButton(contentParent, "AllMax", "ALL 99", allX, y, 120f, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, Skills.MaxLevel);
            });
            PanelChrome.MakeButton(contentParent, "AllReset", "ALL 1", allX + 136f, y, 120f, () =>
            {
                foreach (SkillType s in System.Enum.GetValues(typeof(SkillType)))
                    skills?.SetLevel(s, 1);
            });

            RefreshLabels();
        }

        private void BuildWorldTab()
        {
            float y = 0f;
            var world = World.EmberfallWorldState.Instance;
            PanelChrome.Section(contentParent, "PhaseHdr",
                $"World phase: {(world != null ? world.CurrentPhase.ToString() : "-")}", 0f, y, ContentWidth);
            PanelChrome.Text(contentParent, "PhaseSub", "server-wide, replicated to everyone",
                0f, y + 18f, ContentWidth, 16f, UITheme.CaptionSize, UITheme.TextDim,
                TextAlignmentOptions.MidlineLeft);
            y += 42f;
            PanelChrome.MakeButton(contentParent, "PhaseCalm", "CALM", 0f, y, 168f,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Calm); RebuildContent(); });
            PanelChrome.MakeButton(contentParent, "PhaseErupt", "ERUPTING", 182f, y, 168f,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Erupting); RebuildContent(); });
            PanelChrome.MakeButton(contentParent, "PhaseClear", "CLEARED", 364f, y, 168f,
                () => { SetWorldPhase(World.EmberfallWorldState.Phase.Cleared); RebuildContent(); });

            y += 58f;
            PanelChrome.Section(contentParent, "AtmoHdr", "Atmosphere override", 0f, y, ContentWidth);
            PanelChrome.Text(contentParent, "AtmoSub", "this screen only, the phase above resets it",
                0f, y + 18f, ContentWidth, 16f, UITheme.CaptionSize, UITheme.TextDim,
                TextAlignmentOptions.MidlineLeft);
            y += 42f;
            var atmos = new[] { ("CLEAR 0%", 0f), ("HAZE 35%", 0.35f), ("HEAVY 70%", 0.7f), ("MAX 100%", 1f) };
            for (int i = 0; i < atmos.Length; i++)
            {
                float captured = atmos[i].Item2;
                PanelChrome.MakeButton(contentParent, $"Atmo_{i}", atmos[i].Item1,
                    i * 136f, y, 128f,
                    () => World.EmberAtmosphere.Instance?.SetGlobalIntensity(captured));
            }

            y += 58f;
            PanelChrome.Section(contentParent, "TpHdr", "Teleport", 0f, y, ContentWidth);
            y += 26f;
            for (int i = 0; i < Teleports.Length; i++)
            {
                var (label, pos) = Teleports[i];
                Vector3 captured = pos;
                PanelChrome.MakeButton(contentParent, $"Tp_{i}", label,
                    (i % 3) * 180f, y, 172f, () => Teleport(captured));
                if (i % 3 == 2) y += 42f;
            }
        }

        private void BuildScenesTab()
        {
            float y = 0f;
            PanelChrome.Section(contentParent, "SceneHdr", "Cutscenes", 0f, y, ContentWidth);
            PanelChrome.Text(contentParent, "SceneSub",
                "plays on this client; SPACE/ESC skips; F9 reopens after",
                0f, y + 18f, ContentWidth, 16f, UITheme.CaptionSize, UITheme.TextDim,
                TextAlignmentOptions.MidlineLeft);
            y += 46f;

            int shown = 0;
            foreach (var id in CutsceneIds())
            {
                string captured = id;
                var row = PanelChrome.Row(contentParent, $"cs_{id}", shown, y, ContentWidth);
                PanelChrome.Text(row, "Name", captured, 12f, 10f, ContentWidth - 130f, 20f,
                    UITheme.BodySize, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                PanelChrome.MakeButton(row, "Play", "PLAY", ContentWidth - 100f, 3f, 96f,
                    () => PlayCutscene(captured), primary: true);
                y += UITheme.RowHeight;
                shown++;
            }
            if (shown == 0)
            {
                PanelChrome.Text(contentParent, "NoScenes", "No cutscene JSONs found.",
                    0f, y, ContentWidth, 20f, UITheme.BodySize, UITheme.TextDim,
                    TextAlignmentOptions.MidlineLeft);
                y += UITheme.RowHeight;
            }

            y += 16f;
            PanelChrome.MakeButton(contentParent, "ResetIntro",
                "RESET INTRO FLAG (replays next login)",
                (ContentWidth - 420f) / 2f, y, 420f, () =>
            {
                // Use the live key constant so this can never drift from the trigger again
                // (it was hardcoded to the stale "_v2" and never cleared the real flag).
                PlayerPrefs.DeleteKey(Cinematics.IntroCutsceneTrigger.SeenKey);
                PlayerPrefs.DeleteKey("starter_path_v1");
                PlayerPrefs.Save();
                Say("Intro will replay on next login.");
            });
        }

        private void BuildAnimsTab()
        {
            float y = 0f;
            PanelChrome.Section(contentParent, "AnimHdr", "Animator states", 0f, y, ContentWidth);
            PanelChrome.Text(contentParent, "AnimSub",
                "CrossFade on YOUR avatar; Locomotion recovers; missing states just log",
                0f, y + 18f, ContentWidth, 16f, UITheme.CaptionSize, UITheme.TextDim,
                TextAlignmentOptions.MidlineLeft);
            y += 46f;

            for (int i = 0; i < AnimStates.Length; i++)
            {
                string captured = AnimStates[i];
                PanelChrome.MakeButton(contentParent, $"anim_{captured}", captured,
                    (i % 3) * 180f, y, 172f, () => PlayAnim(captured));
                if (i % 3 == 2) y += 42f;
            }
        }

        private void BuildItemsTab()
        {
            float y = 0f;
            PanelChrome.Section(contentParent, "GoldHdr",
                $"Gold: {(wallet != null ? wallet.Gold : 0)}", 0f, y + 8f, 240f);
            PanelChrome.MakeButton(contentParent, "Gold100", "+100g", 280f, y, 120f,
                () => { wallet?.Add(100); RebuildContent(); });
            PanelChrome.MakeButton(contentParent, "Gold1000", "+1000g", 412f, y, 120f,
                () => { wallet?.Add(1000); RebuildContent(); });

            y += 50f;
            PanelChrome.Section(contentParent, "GiveHdr", "Give items", 0f, y, 240f);
            y += 26f;
            for (int i = 0; i < TestItems.Length; i++)
            {
                var (label, id) = TestItems[i];
                string captured = id;
                PanelChrome.MakeButton(contentParent, $"give_{id}", label,
                    (i % 3) * 180f, y, 172f, () =>
                {
                    if (inventory != null && inventory.Add(captured)) Say($"+ {captured}");
                    else Say("Bag full.");
                });
                if (i % 3 == 2) y += 42f;
            }
        }
    }
}
