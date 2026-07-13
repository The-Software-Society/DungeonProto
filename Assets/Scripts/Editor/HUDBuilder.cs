#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using AldenFall.UI;
using AldenFall.Progression;

namespace AldenFall.Editor
{
    /// <summary>
    /// Builds the production HUD: health cluster, ability bar, target frame,
    /// XP feed, inventory + skills panels, tooltip, all from the generated
    /// sprite set (UISpriteGenerator) and the UITheme palette.
    /// </summary>
    public static class HUDBuilder
    {
        private const string SpriteDir = "Assets/UI/Sprites";

        private static Sprite S(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteDir}/{name}.png");

        public static void Build()
        {
            UISpriteGenerator.GenerateAll();

            // Clear old UI
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.gameObject.name == "GameUI")
                    Object.DestroyImmediate(canvas.gameObject);
            var oldEs = Object.FindFirstObjectByType<EventSystem>();
            if (oldEs != null) Object.DestroyImmediate(oldEs.gameObject);

            // Event system (hover/click support)
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<StandaloneInputModule>();

            // Canvas
            var canvasObj = new GameObject("GameUI");
            var rootCanvas = canvasObj.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 100;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            var hud = canvasObj.AddComponent<GameHUD>();
            canvasObj.AddComponent<FloatingCombatText>();
            var xpFeed = canvasObj.AddComponent<XpFeed>();

            // ---- structure ----
            BuildHealthCluster(canvasObj.transform, hud);
            BuildAbilityBar(canvasObj.transform, hud);
            BuildCombatStyleCluster(canvasObj.transform);
            BuildTargetFrame(canvasObj.transform, hud);
            BuildFeedsAndToasts(canvasObj.transform, hud, xpFeed);
            BuildSkillStrip(canvasObj.transform);
            hud.inventoryPanel = BuildCharacterPanel(canvasObj.transform);
            hud.skillsPanel = BuildSkillsPanel(canvasObj.transform);
            var treesPanel = BuildSkillTreePanel(canvasObj.transform);
            BuildTooltip(canvasObj.transform);

            // Hotkeys isolated from the HUD + clickable buttons for mouse users
            var hotkeys = canvasObj.AddComponent<PanelHotkeys>();
            hotkeys.characterPanel = hud.inventoryPanel;
            hotkeys.skillsPanel = hud.skillsPanel;
            hotkeys.treesPanel = treesPanel;
            BuildPanelButtons(canvasObj.transform, hotkeys);

            Debug.Log("Production HUD built.");
        }

        // ==================== HEALTH ====================

        private static void BuildHealthCluster(Transform root, GameHUD hud)
        {
            var cluster = Panel(root, "HealthCluster", new Vector2(0.5f, 0f), new Vector2(0, 148), new Vector2(420, 30));

            Image(cluster, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -10);
            Image(cluster, "Bg", S("bar_r6"), UITheme.PanelBg, stretch: true);
            // Border radius matches the bar body (r12), not the big panel radius.
            Image(cluster, "Border", S("bar_border_r6"), UITheme.PanelBorder, stretch: true, padding: -1);

            hud.healthGhost = FillBar(cluster, "Ghost", UITheme.HpGhost, 4);
            hud.healthFill = FillBar(cluster, "Fill", UITheme.HpGreen, 4);

            hud.healthText = Text(cluster, "HpText", "100 / 100", 15, UITheme.TextMain, FontStyles.Bold);
            Stretch(hud.healthText.rectTransform);
        }

        // ==================== ABILITIES ====================

        private static void BuildAbilityBar(Transform root, GameHUD hud)
        {
            // 10 bindable slots (keys 1-9, 0): 1-5 combat abilities, 6-0 free.
            // Melee "AUTO" docks left of the bar, dodge "TAPx2" docks right.
            const float slotSize = 58f, gap = 8f;
            const int slotCount = Abilities.ActionBar.SlotCount;
            float barWidth = slotCount * slotSize + (slotCount - 1) * gap;

            var bar = Panel(root, "AbilityBar", new Vector2(0.5f, 0f), new Vector2(0, 74), new Vector2(barWidth + 190, 84));

            // Docked chips sit a clean 16px off the bar ends (was a ragged 38px).
            hud.meleeSlot = BuildAbilitySlot(bar, "Melee", "icon_sword", "AUTO",
                UITheme.Gold, new Vector2(-barWidth / 2f - 40, 0), 48, raycast: false);
            hud.dodgeSlot = BuildAbilitySlot(bar, "Dodge", "icon_strength", "TAPx2",
                UITheme.TextDim, new Vector2(barWidth / 2f + 40, 0), 48, raycast: false);

            hud.barSlots.Clear();
            float startX = -barWidth / 2f + slotSize / 2f;
            for (int i = 0; i < slotCount; i++)
            {
                bool combat = i < Abilities.ActionBar.CombatSlots;
                string key = i == 9 ? "0" : (i + 1).ToString();
                var accent = combat ? UITheme.Gold : UITheme.TextDim;

                var visuals = BuildAbilitySlot(bar, $"BarSlot{i}", null, key, accent,
                    new Vector2(startX + i * (slotSize + gap), 0), slotSize, raycast: true);

                var barSlot = visuals.gameObject.AddComponent<ActionBarSlotUI>();
                barSlot.index = i;
                barSlot.visuals = visuals;

                barSlot.countText = Text(visuals.GetComponent<RectTransform>(), "Count", "", 12,
                    UITheme.TextMain, FontStyles.Bold);
                Anchor(barSlot.countText.rectTransform, new Vector2(0f, 0f), new Vector2(15, 11), new Vector2(26, 15));
                barSlot.countText.alignment = TextAlignmentOptions.BottomLeft;
                barSlot.countText.gameObject.SetActive(false);

                hud.barSlots.Add(barSlot);
            }

            // Serialize the ability-icon lookup so GameHUD can resolve sprites at runtime
            hud.iconNames.Clear();
            hud.iconSprites.Clear();
            foreach (var def in Abilities.AbilityDatabase.All)
            {
                if (string.IsNullOrEmpty(def.IconSprite) || hud.iconNames.Contains(def.IconSprite))
                    continue;
                hud.iconNames.Add(def.IconSprite);
                hud.iconSprites.Add(S(def.IconSprite));
            }

            // The drag ghost + shared drag state live on the canvas root
            if (root.GetComponent<DragController>() == null)
                root.gameObject.AddComponent<DragController>();

            // ---- Interaction prompt pill ("[E] Fish") above the bar ----
            var chip = Panel(root, "InteractPrompt", new Vector2(0.5f, 0f), new Vector2(0, 186), new Vector2(180, 34));
            Image(chip, "ChipBg", S("panel_r16"), new Color(0.07f, 0.063f, 0.10f, 0.85f), stretch: true);
            Image(chip, "ChipBorder", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);
            hud.promptText = Text(chip, "PromptText", "[E] Fish", 15, UITheme.TextGold, FontStyles.Bold);
            Stretch(hud.promptText.rectTransform);
            hud.promptChip = chip.gameObject;
            chip.gameObject.SetActive(false);

            // ---- Fishing cast bar: thin progress strip above the action bar ----
            var castBar = Panel(root, "FishingCastBar", new Vector2(0.5f, 0f), new Vector2(0, 132), new Vector2(260, 22));
            Image(castBar, "CastBg", S("bar_r6"), new Color(0, 0, 0, 0.6f), stretch: true);
            hud.castBarFill = FillBar(castBar, "CastFill", new Color(0.45f, 0.85f, 0.9f), 3);
            hud.castBarFill.fillAmount = 0f;
            var castLabel = Text(castBar, "CastLabel", "Fishing…", 11, UITheme.TextMain, FontStyles.Bold);
            Stretch(castLabel.rectTransform);
            hud.castBarRoot = castBar.gameObject;
            castBar.gameObject.SetActive(false);
        }

        // ==================== COMBAT STYLE SELECTOR ====================
        // Three buttons above the melee AUTO slot: which skill melee autos train.

        private static void BuildCombatStyleCluster(Transform root)
        {
            const float slotSize = 58f, gap = 8f;
            const int slotCount = Abilities.ActionBar.SlotCount;
            float barWidth = slotCount * slotSize + (slotCount - 1) * gap;
            float clusterX = -barWidth / 2f - 40f;   // centered over the melee AUTO slot

            var cluster = Panel(root, "CombatStyleCluster", new Vector2(0.5f, 0f),
                new Vector2(clusterX, 136), new Vector2(140, 58));
            var styleUI = cluster.gameObject.AddComponent<CombatStyleUI>();

            // Grouping pill so label + buttons read as one control, not floaters.
            Image(cluster, "ClusterBg", S("panel_r16"), new Color(0.07f, 0.063f, 0.10f, 0.62f), stretch: true, padding: -4);

            var label = Text(cluster, "Label", "XP STYLE · V", 10, UITheme.TextDim, FontStyles.Bold);
            label.characterSpacing = 2;
            Anchor(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -3), new Vector2(140, 14));

            var styles = new (Combat.CombatStyle style, string glyph, UnityEngine.Events.UnityAction onClick)[]
            {
                (Combat.CombatStyle.Attack,   "icon_sword",    styleUI.SelectAttack),
                (Combat.CombatStyle.Strength, "icon_strength", styleUI.SelectStrength),
                (Combat.CombatStyle.Defence,  "icon_shield",   styleUI.SelectDefence),
            };

            float x = -44f;
            foreach (var (style, glyph, onClick) in styles)
            {
                var btn = Panel(cluster, $"Style_{style}", new Vector2(0.5f, 0.5f), new Vector2(x, -8), new Vector2(38, 38));
                btn.gameObject.AddComponent<HoverScale>();

                var bg = Image(btn, "Bg", S("slot_r10"), UITheme.SlotBg, stretch: true);
                bg.raycastTarget = true;
                var border = Image(btn, "Border", S("slot_border_r10"), new Color(1f, 1f, 1f, 0.12f), stretch: true, padding: -1);
                var icon = Image(btn, "Icon", S(glyph), new Color(0.92f, 0.88f, 0.8f, 1f), stretch: true, padding: 8);
                icon.preserveAspect = true;

                var button = btn.gameObject.AddComponent<Button>();
                button.targetGraphic = bg;
                UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, onClick);

                styleUI.entries.Add(new CombatStyleUI.Entry { style = style, border = border, icon = icon });
                x += 44f;
            }
        }

        private static AbilitySlotUI BuildAbilitySlot(RectTransform parent, string name,
            string glyph, string keyLabel, Color accent, Vector2 pos, float size, bool raycast = false)
        {
            var slot = Panel(parent, name, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var ui = slot.gameObject.AddComponent<AbilitySlotUI>();
            slot.gameObject.AddComponent<HoverScale>();

            Image(slot, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -8);

            // Ready glow behind the slot (alpha driven by ReadyPulse)
            var glowColor = accent; glowColor.a = 0f;
            ui.readyGlow = Image(slot, "ReadyGlow", S("glow_soft"), glowColor, stretch: true, padding: -14);

            var bg = Image(slot, "Bg", S("slot_r10"), UITheme.SlotBg, stretch: true);
            bg.raycastTarget = raycast;   // drop targets need a raycastable surface
            var borderColor = accent; borderColor.a = 0.35f;
            Image(slot, "Border", S("slot_border_r10"), borderColor, stretch: true, padding: -1);

            ui.iconImage = Image(slot, "Icon", glyph != null ? S(glyph) : null, Color.white, stretch: true, padding: 10);
            ui.iconImage.preserveAspect = true;
            if (glyph == null) ui.iconImage.enabled = false;

            // Radial cooldown sweep
            var cooldown = Image(slot, "Cooldown", S("slot_r10"), new Color(0f, 0f, 0f, 0.72f), stretch: true, padding: 2);
            cooldown.type = UnityEngine.UI.Image.Type.Filled;
            cooldown.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            cooldown.fillClockwise = false;
            cooldown.fillAmount = 0;
            ui.cooldownOverlay = cooldown;

            ui.cooldownText = Text(slot, "CooldownText", "", 21, UITheme.TextMain, FontStyles.Bold);
            Stretch(ui.cooldownText.rectTransform);

            // Keybind chip
            var chip = Panel(slot, "KeyChip", new Vector2(1f, 0f), new Vector2(-4, 6), new Vector2(keyLabel.Length > 2 ? 42 : 24, 17));
            Image(chip, "ChipBg", S("slot_r10"), new Color(accent.r, accent.g, accent.b, 0.92f), stretch: true);
            var chipText = Text(chip, "ChipText", keyLabel, 10, new Color(0.12f, 0.09f, 0.06f), FontStyles.Bold);
            Stretch(chipText.rectTransform);

            return ui;
        }

        // ==================== TARGET FRAME ====================

        private static void BuildTargetFrame(Transform root, GameHUD hud)
        {
            var frame = Panel(root, "TargetFrame", new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(380, 72));
            hud.targetGroup = frame.gameObject.AddComponent<CanvasGroup>();

            Image(frame, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -10);
            Image(frame, "Bg", S("panel_r16"), UITheme.PanelBg, stretch: true);
            Image(frame, "Border", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);

            hud.targetName = Text(frame, "Name", "Target", 18, UITheme.TextMain, FontStyles.Bold);
            var nameRect = hud.targetName.rectTransform;
            nameRect.anchorMin = new Vector2(0, 0.5f); nameRect.anchorMax = new Vector2(0.72f, 1f);
            nameRect.offsetMin = new Vector2(16, 0); nameRect.offsetMax = new Vector2(0, -6);
            hud.targetName.alignment = TextAlignmentOptions.MidlineLeft;

            hud.targetLevel = Text(frame, "Level", "Lv 2", 14, UITheme.TextGold, FontStyles.Bold);
            var lvRect = hud.targetLevel.rectTransform;
            lvRect.anchorMin = new Vector2(0.72f, 0.5f); lvRect.anchorMax = new Vector2(1f, 1f);
            lvRect.offsetMin = Vector2.zero; lvRect.offsetMax = new Vector2(-16, -6);
            hud.targetLevel.alignment = TextAlignmentOptions.MidlineRight;

            // HP bar lower half
            var barArea = Panel(frame, "BarArea", new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(348, 13));
            Image(barArea, "BarBg", S("bar_r6"), new Color(0f, 0f, 0f, 0.55f), stretch: true);
            hud.targetHealthGhost = FillBar(barArea, "Ghost", UITheme.HpGhost, 2);
            hud.targetHealthFill = FillBar(barArea, "Fill", UITheme.HpRed, 2);
        }

        // ==================== FEEDS ====================

        private static void BuildFeedsAndToasts(Transform root, GameHUD hud, XpFeed xpFeed)
        {
            var feed = Panel(root, "XpFeedRoot", new Vector2(1f, 0.42f), new Vector2(-40, 0), new Vector2(220, 24));
            xpFeed.feedRoot = feed;

            var banner = Panel(root, "BannerRoot", new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(700, 70));
            xpFeed.bannerRoot = banner;

            // Toasts stack upward from the bottom-right, fully on screen
            var toasts = Panel(root, "ToastRoot", new Vector2(1f, 0f), new Vector2(-24, 210), new Vector2(320, 160));
            toasts.pivot = new Vector2(1f, 0f);
            var toastLayout = toasts.gameObject.AddComponent<VerticalLayoutGroup>();
            toastLayout.childAlignment = TextAnchor.LowerRight;
            toastLayout.spacing = 6f;
            toastLayout.childControlWidth = false;
            toastLayout.childControlHeight = false;
            toastLayout.childForceExpandWidth = false;
            toastLayout.childForceExpandHeight = false;
            hud.toastRoot = toasts;
            hud.toastPillSprite = S("panel_r16");   // rounded pill bg behind each toast
        }

        // ==================== CHARACTER PANEL ====================
        // Paper-doll equipment column + OSRS-style bonuses + bag grid.

        private static SlidePanel BuildCharacterPanel(Transform root)
        {
            var panel = Panel(root, "CharacterPanel", new Vector2(1f, 0.5f), new Vector2(300, 0), new Vector2(478, 660));
            var slide = panel.gameObject.AddComponent<SlidePanel>();
            slide.shownPos = new Vector2(-258, 0);
            slide.hiddenPos = new Vector2(300, 0);
            var charUI = panel.gameObject.AddComponent<CharacterPanelUI>();

            Image(panel, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -14);
            Image(panel, "Bg", S("panel_r16"), UITheme.PanelBg, stretch: true);
            Image(panel, "Border", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);

            var header = Text(panel, "Header", "CHARACTER", 20, UITheme.TextGold, FontStyles.Bold);
            header.characterSpacing = 6;
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0, 1); headerRect.anchorMax = new Vector2(0.7f, 1);
            headerRect.offsetMin = new Vector2(24, -46); headerRect.offsetMax = new Vector2(0, -16);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            charUI.countText = Text(panel, "Count", "0 / 20", 12, UITheme.TextDim, FontStyles.Normal);
            var countRect = charUI.countText.rectTransform;
            countRect.anchorMin = new Vector2(0.7f, 1); countRect.anchorMax = new Vector2(1, 1);
            countRect.offsetMin = new Vector2(0, -46); countRect.offsetMax = new Vector2(-24, -16);
            charUI.countText.alignment = TextAlignmentOptions.MidlineRight;

            // Thin gold rule under the header line.
            var headerRule = Image(panel, "HeaderRule", null, UITheme.HeaderRule, stretch: false);
            var ruleRect = headerRule.rectTransform;
            ruleRect.anchorMin = new Vector2(0, 1); ruleRect.anchorMax = new Vector2(1, 1);
            ruleRect.offsetMin = new Vector2(24, -55); ruleRect.offsetMax = new Vector2(-24, -54);

            // Gold readout (moved off the quest tracker), under the header, left column.
            charUI.goldText = Text(panel, "Gold", "<color=#F2CC8C><b>Gold:</b> 0</color>", 15,
                UITheme.TextGold, FontStyles.Bold);
            var goldRect = charUI.goldText.rectTransform;
            goldRect.anchorMin = new Vector2(0, 1); goldRect.anchorMax = new Vector2(0.7f, 1);
            goldRect.offsetMin = new Vector2(24, -86); goldRect.offsetMax = new Vector2(0, -64);
            charUI.goldText.alignment = TextAlignmentOptions.MidlineLeft;
            charUI.goldText.richText = true;

            // ---- Paper doll (left column, top→bottom: helm/cape/chest/legs/weapon/focus)
            // Starts clear of the gold line; labels sit in the gaps between slots. ----
            var dollSlots = new (Items.EquipSlot slot, string label, float y)[]
            {
                (Items.EquipSlot.Helm,   "HELM", -128),
                (Items.EquipSlot.Cape,   "CAPE", -208),
                (Items.EquipSlot.Chest,  "CHEST", -288),
                (Items.EquipSlot.Legs,   "LEGS", -368),
                (Items.EquipSlot.Weapon, "MAIN HAND", -448),
                (Items.EquipSlot.Focus,  "OFF HAND (FOCUS)", -528),
            };
            foreach (var (slot, label, y) in dollSlots)
            {
                var slotUI = BuildItemSlot(panel, $"Doll_{slot}", new Vector2(0, 1), new Vector2(56, y), 62);
                charUI.equipSlots.Add(new CharacterPanelUI.EquipSlotEntry { slot = slot, ui = slotUI });

                var slotLabel = Text(panel, $"DollLabel_{slot}", label, 10, UITheme.TextDim, FontStyles.Bold);
                slotLabel.characterSpacing = 2;
                slotLabel.textWrappingMode = TextWrappingModes.NoWrap;
                Anchor(slotLabel.rectTransform, new Vector2(0, 1), new Vector2(56, y - 40), new Vector2(130, 12));
            }

            // ---- Bonuses block (bottom-right, under the bag grid, table-aligned) ----
            var bonusHeader = Text(panel, "BonusHeader", "BONUSES", 12, UITheme.TextDim, FontStyles.Bold);
            bonusHeader.characterSpacing = 3;
            Anchor(bonusHeader.rectTransform, new Vector2(0, 0), new Vector2(238, 182), new Vector2(180, 16));
            bonusHeader.alignment = TextAlignmentOptions.MidlineLeft;

            charUI.bonusText = Text(panel, "BonusText", "", 12, UITheme.TextMain, FontStyles.Normal);
            var bonusRect = charUI.bonusText.rectTransform;
            bonusRect.anchorMin = new Vector2(0, 0); bonusRect.anchorMax = new Vector2(0, 0);
            bonusRect.pivot = new Vector2(0, 0);
            bonusRect.anchoredPosition = new Vector2(148, 44);
            bonusRect.sizeDelta = new Vector2(300, 124);
            charUI.bonusText.alignment = TextAlignmentOptions.TopLeft;
            charUI.bonusText.lineSpacing = 8;

            // ---- Bag grid (right side, 4 wide x 5 tall) ----
            var gridObj = new GameObject("Grid");
            gridObj.transform.SetParent(panel, false);
            var gridRect = gridObj.AddComponent<RectTransform>();
            gridRect.anchorMin = new Vector2(0, 1); gridRect.anchorMax = new Vector2(1, 1);
            gridRect.offsetMin = new Vector2(148, -440); gridRect.offsetMax = new Vector2(-20, -74);
            var grid = gridObj.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(64, 64);
            grid.spacing = new Vector2(9, 9);
            grid.childAlignment = TextAnchor.UpperLeft;

            for (int i = 0; i < Items.Inventory.Capacity; i++)
            {
                var slot = BuildItemSlot(gridRect, $"Slot{i}", new Vector2(0.5f, 0.5f), Vector2.zero, 64);
                charUI.bagSlots.Add(slot);
            }

            return slide;
        }

        // ==================== SKILL STRIP (always visible) ====================

        private static void BuildSkillStrip(Transform root)
        {
            int skillCount = System.Enum.GetValues(typeof(SkillType)).Length;
            var strip = Panel(root, "SkillStrip", new Vector2(0f, 0.5f), new Vector2(64, 40), new Vector2(104, skillCount * 54 + 18));
            var stripUI = strip.gameObject.AddComponent<SkillStripUI>();

            Image(strip, "Bg", S("panel_r16"), new Color(0.07f, 0.063f, 0.10f, 0.72f), stretch: true);
            Image(strip, "Border", S("panel_border_r16"), new Color(1f, 1f, 1f, 0.07f), stretch: true, padding: -1);

            var glyphs = new Dictionary<SkillType, string>
            {
                { SkillType.Attack, "icon_sword" },
                { SkillType.Strength, "icon_strength" },
                { SkillType.Defence, "icon_shield" },
                { SkillType.Magic, "icon_magic" },
                { SkillType.Fishing, "icon_fish" },
                { SkillType.Cooking, "icon_flame" },
            };

            var rows = new List<SkillStripUI.Row>();
            float y = -34f;
            foreach (SkillType skill in System.Enum.GetValues(typeof(SkillType)))
            {
                var accent = XpFeed.SkillColor(skill);

                var flash = Image(strip, $"Flash_{skill}", S("panel_r16"),
                    new Color(accent.r, accent.g, accent.b, 0f), stretch: false);
                Anchor(flash.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(92, 48));

                var iconColor = accent; iconColor.a = 0.95f;
                var icon = Image(strip, $"Icon_{skill}", S(glyphs[skill]), iconColor, stretch: false);
                Anchor(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(-26, y + 4), new Vector2(26, 26));

                var level = Text(strip, $"Lv_{skill}", "1", 20, UITheme.TextMain, FontStyles.Bold);
                Anchor(level.rectTransform, new Vector2(0.5f, 1f), new Vector2(16, y + 4), new Vector2(52, 26));
                level.alignment = TextAlignmentOptions.Center;

                var barArea = Panel(strip, $"XpArea_{skill}", new Vector2(0.5f, 1f), new Vector2(0, y - 19), new Vector2(84, 5));
                Image(barArea, "BarBg", S("bar_r6"), new Color(0, 0, 0, 0.55f), stretch: true);
                var fill = FillBar(barArea, "Fill", accent, 0);

                rows.Add(new SkillStripUI.Row { skill = skill, levelText = level, xpFill = fill, flash = flash });
                y -= 54f;
            }
            stripUI.rows = rows.ToArray();
        }

        // ==================== COMBAT SKILL TREES ====================

        private static SlidePanel BuildSkillTreePanel(Transform root)
        {
            var panel = Panel(root, "SkillTreePanel", new Vector2(0.5f, 0f), new Vector2(0, -520), new Vector2(680, 460));
            var slide = panel.gameObject.AddComponent<SlidePanel>();
            slide.shownPos = new Vector2(0, 400);   // clears the action bar + health cluster
            slide.hiddenPos = new Vector2(0, -520);
            var treeUI = panel.gameObject.AddComponent<SkillTreePanelUI>();

            Image(panel, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -14);
            Image(panel, "Bg", S("panel_r16"), UITheme.PanelBg, stretch: true);
            Image(panel, "Border", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);

            var header = Text(panel, "Header", "COMBAT TREES (T)", 20, UITheme.TextGold, FontStyles.Bold);
            header.characterSpacing = 6;
            var hRect = header.rectTransform;
            hRect.anchorMin = new Vector2(0, 1); hRect.anchorMax = new Vector2(1, 1);
            hRect.offsetMin = new Vector2(24, -46); hRect.offsetMax = new Vector2(-24, -16);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            var hint = Text(panel, "Hint", "drag an ability onto the action bar", 12, UITheme.TextDim, FontStyles.Normal);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = new Vector2(0, 1); hintRect.anchorMax = new Vector2(1, 1);
            hintRect.offsetMin = new Vector2(24, -46); hintRect.offsetMax = new Vector2(-24, -16);
            hint.alignment = TextAlignmentOptions.MidlineRight;

            var treesRule = Image(panel, "HeaderRule", null, UITheme.HeaderRule, stretch: false);
            var treesRuleRect = treesRule.rectTransform;
            treesRuleRect.anchorMin = new Vector2(0, 1); treesRuleRect.anchorMax = new Vector2(1, 1);
            treesRuleRect.offsetMin = new Vector2(24, -55); treesRuleRect.offsetMax = new Vector2(-24, -54);

            var trees = new[] { SkillType.Attack, SkillType.Strength, SkillType.Defence, SkillType.Magic };
            var tabActions = new UnityEngine.Events.UnityAction[]
            {
                treeUI.SelectAttack, treeUI.SelectStrength, treeUI.SelectDefence, treeUI.SelectMagic,
            };

            float tabW = 150f, tabGap = 8f;
            float tabStartX = -((trees.Length - 1) * (tabW + tabGap)) / 2f;

            for (int t = 0; t < trees.Length; t++)
            {
                var tree = trees[t];
                var accent = XpFeed.SkillColor(tree);

                // Tab button
                var tabRect = Panel(panel, $"Tab_{tree}", new Vector2(0.5f, 1f),
                    new Vector2(tabStartX + t * (tabW + tabGap), -74), new Vector2(tabW, 36));
                var tabBg = Image(tabRect, "TabBg", S("panel_r16"), new Color(0, 0, 0, 0.35f), stretch: true);
                tabBg.raycastTarget = true;
                var tabLabel = Text(tabRect, "TabLabel", Skills.DisplayName(tree).ToUpperInvariant(), 13,
                    UITheme.TextDim, FontStyles.Bold);
                tabLabel.characterSpacing = 2;
                Stretch(tabLabel.rectTransform);

                var tabButton = tabRect.gameObject.AddComponent<UnityEngine.UI.Button>();
                tabButton.targetGraphic = tabBg;
                UnityEditor.Events.UnityEventTools.AddPersistentListener(tabButton.onClick, tabActions[t]);
                tabRect.gameObject.AddComponent<HoverScale>().hoverScale = 1.04f;

                // Accent underline: only the active tab shows it (SkillTreePanelUI toggles).
                var tabUnderline = Image(tabRect, "Underline", S("bar_r6"),
                    new Color(accent.r, accent.g, accent.b, 0.9f), stretch: false);
                Anchor(tabUnderline.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 2), new Vector2(tabW - 20, 3));

                // Content page: sections per weapon class, tiles stacked down
                var page = Panel(panel, $"Page_{tree}", new Vector2(0.5f, 1f), new Vector2(0, -122), new Vector2(632, 320));
                page.pivot = new Vector2(0.5f, 1f);

                float y = 0f;
                foreach (var weaponClass in new[] { Abilities.WeaponClass.Sword, Abilities.WeaponClass.Focus, Abilities.WeaponClass.Any })
                {
                    var defs = new List<Abilities.AbilityDef>();
                    foreach (var def in Abilities.AbilityDatabase.All)
                        if (def.Tree == tree && def.RequiredWeapon == weaponClass)
                            defs.Add(def);
                    if (defs.Count == 0) continue;

                    string section = weaponClass == Abilities.WeaponClass.Any
                        ? "ANY WEAPON" : weaponClass.ToString().ToUpperInvariant();
                    var sectionLabel = Text(page, $"Section_{weaponClass}", section, 12, UITheme.TextDim, FontStyles.Bold);
                    sectionLabel.characterSpacing = 3;
                    Anchor(sectionLabel.rectTransform, new Vector2(0, 1), new Vector2(86, y - 12), new Vector2(140, 16));
                    sectionLabel.alignment = TextAlignmentOptions.MidlineLeft;
                    y -= 30f;

                    foreach (var def in defs)
                    {
                        BuildAbilityTile(page, def, new Vector2(0, y - 45), accent);
                        y -= 96f;
                    }
                    y -= 8f;
                }

                treeUI.tabs.Add(new SkillTreePanelUI.Tab
                {
                    tree = tree,
                    button = tabButton,
                    buttonBg = tabBg,
                    label = tabLabel,
                    underline = tabUnderline,
                    content = page.gameObject,
                });
            }

            return slide;
        }

        private static void BuildAbilityTile(RectTransform page, Abilities.AbilityDef def, Vector2 pos, Color accent)
        {
            var tile = Panel(page, $"Tile_{def.Id}", new Vector2(0.5f, 1f), pos, new Vector2(600, 86));
            var group = tile.gameObject.AddComponent<CanvasGroup>();

            var tileUI = tile.gameObject.AddComponent<AbilityTileUI>();
            tileUI.abilityId = def.Id;
            tileUI.group = group;

            var bg = Image(tile, "TileBg", S("panel_r16"), UITheme.PanelBgLight, stretch: true);
            bg.raycastTarget = true;   // hover + drag surface
            Image(tile, "TileBorder", S("panel_border_r16"), new Color(1, 1, 1, 0.06f), stretch: true, padding: -1);

            // Icon plate
            var plate = Panel(tile, "IconPlate", new Vector2(0, 0.5f), new Vector2(52, 0), new Vector2(60, 60));
            Image(plate, "PlateBg", S("slot_r10"), UITheme.SlotBg, stretch: true);
            var plateBorder = accent; plateBorder.a = 0.4f;
            Image(plate, "PlateBorder", S("slot_border_r10"), plateBorder, stretch: true, padding: -1);
            var iconColor = accent; iconColor.a = 0.95f;
            tileUI.icon = Image(plate, "Icon", S(def.IconSprite), iconColor, stretch: true, padding: 9);
            tileUI.icon.preserveAspect = true;

            tileUI.nameText = Text(tile, "Name", def.Name, 15, UITheme.TextMain, FontStyles.Bold);
            Anchor(tileUI.nameText.rectTransform, new Vector2(0, 1), new Vector2(230, -22), new Vector2(280, 22));
            tileUI.nameText.alignment = TextAlignmentOptions.MidlineLeft;

            tileUI.cooldownText = Text(tile, "Cd", $"{def.Cooldown:0.#}s cd", 12, UITheme.TextDim, FontStyles.Normal);
            Anchor(tileUI.cooldownText.rectTransform, new Vector2(1, 1), new Vector2(-58, -22), new Vector2(90, 18));
            tileUI.cooldownText.alignment = TextAlignmentOptions.MidlineRight;

            tileUI.requiresText = Text(tile, "Requires",
                $"requires {def.RequiredWeapon.ToString().ToLowerInvariant()}", 12,
                UITheme.HpRed, FontStyles.Normal);
            Anchor(tileUI.requiresText.rectTransform, new Vector2(1, 0), new Vector2(-99, 16), new Vector2(170, 16));
            tileUI.requiresText.alignment = TextAlignmentOptions.MidlineRight;
            tileUI.requiresText.gameObject.SetActive(false);

            // Rank pips + mastery progress (bar starts clear of the last pip)
            var pips = new List<UnityEngine.UI.Image>();
            for (int i = 0; i < Abilities.AbilityMastery.MaxRank; i++)
            {
                var pip = Image(tile, $"Pip{i}", S("circle"), new Color(1, 1, 1, 0.14f), stretch: false);
                Anchor(pip.rectTransform, new Vector2(0, 0), new Vector2(104 + i * 20, 22), new Vector2(11, 11));
                pips.Add(pip);
            }
            tileUI.rankPips = pips.ToArray();

            var barArea = Panel(tile, "MasteryArea", new Vector2(0, 0), new Vector2(288, 22), new Vector2(160, 5));
            Image(barArea, "BarBg", S("bar_r6"), new Color(0, 0, 0, 0.55f), stretch: true);
            tileUI.masteryFill = FillBar(barArea, "Fill", UITheme.Gold, 0);
        }

        // ==================== PANEL BUTTONS ====================

        private static void BuildPanelButtons(Transform root, PanelHotkeys hotkeys)
        {
            var cluster = Panel(root, "PanelButtons", new Vector2(1f, 0f), new Vector2(-130, 52), new Vector2(220, 60));

            // Method groups (not closures), persistent listeners must serialize
            BuildPanelButton(cluster, "CharacterBtn", "icon_bag", "C", new Vector2(-72, 0),
                hotkeys.ToggleCharacter);
            BuildPanelButton(cluster, "SkillsBtn", "icon_magic", "K", new Vector2(0, 0),
                hotkeys.ToggleSkills);
            BuildPanelButton(cluster, "TreesBtn", "icon_cleave", "T", new Vector2(72, 0),
                hotkeys.ToggleTrees);
        }

        private static void BuildPanelButton(RectTransform parent, string name, string glyph,
            string key, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var btn = Panel(parent, name, new Vector2(0.5f, 0.5f), pos, new Vector2(56, 56));
            btn.gameObject.AddComponent<HoverScale>();

            Image(btn, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -7);
            var bg = Image(btn, "Bg", S("slot_r10"), UITheme.SlotBg, stretch: true);
            bg.raycastTarget = true;
            Image(btn, "Border", S("slot_border_r10"), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.3f), stretch: true, padding: -1);
            Image(btn, "Icon", S(glyph), new Color(0.92f, 0.88f, 0.8f, 0.95f), stretch: true, padding: 12);

            var chip = Panel(btn, "KeyChip", new Vector2(1f, 0f), new Vector2(-3, 5), new Vector2(20, 16));
            Image(chip, "ChipBg", S("slot_r10"), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.92f), stretch: true);
            var chipText = Text(chip, "ChipText", key, 10, new Color(0.12f, 0.09f, 0.06f), FontStyles.Bold);
            Stretch(chipText.rectTransform);

            var button = btn.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, onClick);
        }

        private static ItemSlotUI BuildItemSlot(RectTransform parent, string name,
            Vector2 anchor, Vector2 pos, float size)
        {
            var slot = Panel(parent, name, anchor, pos, new Vector2(size, size));
            var ui = slot.gameObject.AddComponent<ItemSlotUI>();
            slot.gameObject.AddComponent<HoverScale>().hoverScale = 1.06f;

            ui.background = Image(slot, "Bg", S("slot_r10"), UITheme.SlotBg, stretch: true);
            ui.border = Image(slot, "Border", S("slot_border_r10"), UITheme.SlotBorder, stretch: true, padding: -1);
            ui.icon = Image(slot, "Icon", null, Color.white, stretch: true, padding: 7);
            ui.icon.enabled = false;
            ui.icon.preserveAspect = true;

            var badge = Panel(slot, "EquippedBadge", new Vector2(1, 1), new Vector2(-8, -8), new Vector2(14, 14));
            Image(badge, "BadgeDot", S("circle"), UITheme.Gold, stretch: true);
            ui.equippedBadge = badge.gameObject;
            badge.gameObject.SetActive(false);

            // Stack count, bottom-right, hidden at <= 1
            ui.countText = Text(slot, "Count", "", 13, UITheme.TextMain, FontStyles.Bold);
            Anchor(ui.countText.rectTransform, new Vector2(1, 0), new Vector2(-16, 12), new Vector2(30, 16));
            ui.countText.alignment = TextAlignmentOptions.BottomRight;
            ui.countText.gameObject.SetActive(false);

            // Upgrade tier "+X", top-left, tinted by tier, hidden at +0
            ui.plusText = Text(slot, "Plus", "", 13, UITheme.TextGold, FontStyles.Bold);
            Anchor(ui.plusText.rectTransform, new Vector2(0, 1), new Vector2(15, -12), new Vector2(30, 16));
            ui.plusText.alignment = TextAlignmentOptions.TopLeft;
            ui.plusText.gameObject.SetActive(false);

            // GraphicRaycaster needs a raycast target covering the slot
            ui.background.raycastTarget = true;

            return ui;
        }

        // ==================== SKILLS PANEL ====================

        private static SlidePanel BuildSkillsPanel(Transform root)
        {
            int skillCount = System.Enum.GetValues(typeof(SkillType)).Length;
            float panelH = 92f + skillCount * 82f + 24f;
            var panel = Panel(root, "SkillsPanel", new Vector2(0f, 0.5f), new Vector2(-240, 0), new Vector2(360, panelH));
            var slide = panel.gameObject.AddComponent<SlidePanel>();
            slide.shownPos = new Vector2(308, 0);   // clears the always-on skill strip
            slide.hiddenPos = new Vector2(-240, 0);
            var skillsUI = panel.gameObject.AddComponent<SkillsPanelUI>();

            Image(panel, "Shadow", S("glow_soft"), UITheme.Shadow, stretch: true, padding: -14);
            Image(panel, "Bg", S("panel_r16"), UITheme.PanelBg, stretch: true);
            Image(panel, "Border", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);

            var header = Text(panel, "Header", "SKILLS", 20, UITheme.TextGold, FontStyles.Bold);
            header.characterSpacing = 6;
            var hRect = header.rectTransform;
            hRect.anchorMin = new Vector2(0, 1); hRect.anchorMax = new Vector2(1, 1);
            hRect.offsetMin = new Vector2(24, -46); hRect.offsetMax = new Vector2(-24, -16);
            header.alignment = TextAlignmentOptions.MidlineLeft;

            var skillsRule = Image(panel, "HeaderRule", null, UITheme.HeaderRule, stretch: false);
            var skillsRuleRect = skillsRule.rectTransform;
            skillsRuleRect.anchorMin = new Vector2(0, 1); skillsRuleRect.anchorMax = new Vector2(1, 1);
            skillsRuleRect.offsetMin = new Vector2(24, -55); skillsRuleRect.offsetMax = new Vector2(-24, -54);

            var glyphs = new Dictionary<SkillType, string>
            {
                { SkillType.Attack, "icon_sword" },
                { SkillType.Strength, "icon_strength" },
                { SkillType.Defence, "icon_shield" },
                { SkillType.Magic, "icon_magic" },
                { SkillType.Fishing, "icon_fish" },
                { SkillType.Cooking, "icon_flame" },
            };

            var rows = new List<SkillsPanelUI.SkillRow>();
            float rowY = -92f;
            foreach (SkillType skill in System.Enum.GetValues(typeof(SkillType)))
            {
                rows.Add(BuildSkillRow(panel, skill, glyphs[skill], rowY));
                rowY -= 82f;
            }
            skillsUI.rows = rows.ToArray();

            return slide;
        }

        private static SkillsPanelUI.SkillRow BuildSkillRow(RectTransform panel,
            SkillType skill, string glyph, float y)
        {
            var row = Panel(panel, $"Row_{skill}", new Vector2(0.5f, 1f), new Vector2(0, y), new Vector2(312, 68));
            Image(row, "RowBg", S("panel_r16"), UITheme.PanelBgLight, stretch: true);
            Image(row, "RowBorder", S("panel_border_r16"), new Color(1f, 1f, 1f, 0.05f), stretch: true, padding: -1);

            var accent = XpFeed.SkillColor(skill);
            var iconColor = accent; iconColor.a = 0.95f;
            var icon = Image(row, "Icon", S(glyph), iconColor, stretch: false);
            Anchor(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(30, 6), new Vector2(32, 32));

            var name = Text(row, "Name", Skills.DisplayName(skill), 15, UITheme.TextMain, FontStyles.Bold);
            var nRect = name.rectTransform;
            nRect.anchorMin = new Vector2(0, 0.5f); nRect.anchorMax = new Vector2(0.7f, 1f);
            nRect.offsetMin = new Vector2(56, -4); nRect.offsetMax = new Vector2(0, -6);
            name.alignment = TextAlignmentOptions.MidlineLeft;

            // One level color across all skills; the icon already carries identity.
            var level = Text(row, "Level", "1", 26, UITheme.TextMain, FontStyles.Bold);
            var lRect = level.rectTransform;
            lRect.anchorMin = new Vector2(0.78f, 0); lRect.anchorMax = new Vector2(1, 1);
            lRect.offsetMin = Vector2.zero; lRect.offsetMax = new Vector2(-14, 0);
            level.alignment = TextAlignmentOptions.MidlineRight;

            var xpText = Text(row, "XpText", "", 12, UITheme.TextDim, FontStyles.Normal);
            var xRect = xpText.rectTransform;
            xRect.anchorMin = new Vector2(0, 0); xRect.anchorMax = new Vector2(0.78f, 0.42f);
            xRect.offsetMin = new Vector2(56, 16); xRect.offsetMax = Vector2.zero;
            xpText.alignment = TextAlignmentOptions.MidlineLeft;

            // XP bar along the bottom
            var barArea = Panel(row, "XpBar", new Vector2(0.5f, 0f), new Vector2(8, 9), new Vector2(232, 6));
            Image(barArea, "BarBg", S("bar_r6"), new Color(0, 0, 0, 0.35f), stretch: true);
            var fill = FillBar(barArea, "Fill", accent, 0);

            return new SkillsPanelUI.SkillRow
            {
                skill = skill,
                levelText = level,
                xpText = xpText,
                xpFill = fill,
            };
        }

        // ==================== TOOLTIP & HINTS ====================

        private static void BuildTooltip(Transform root)
        {
            var holder = new GameObject("Tooltip");
            holder.transform.SetParent(root, false);
            var holderRect = holder.AddComponent<RectTransform>();
            Stretch(holderRect);
            var tooltip = holder.AddComponent<TooltipUI>();

            // Two columns: hovered item (left) + currently-equipped counterpart
            // (right, shown only for equippable items). Left column keeps its
            // original ~252px width; the compare column occupies the right half.
            const float leftRight = -294f; // ends the left column near x=266 in a 560 panel

            var panel = Panel(holderRect, "TooltipPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 118));
            panel.pivot = new Vector2(0f, 1f);
            tooltip.panel = panel;

            Image(panel, "Bg", S("panel_r16"), new Color(0.05f, 0.045f, 0.075f, 0.97f), stretch: true);
            tooltip.border = Image(panel, "Border", S("panel_border_r16"), UITheme.PanelBorder, stretch: true, padding: -1);

            tooltip.nameText = Text(panel, "Name", "", 15, UITheme.TextMain, FontStyles.Bold);
            var nRect = tooltip.nameText.rectTransform;
            nRect.anchorMin = new Vector2(0, 1); nRect.anchorMax = new Vector2(1, 1);
            nRect.offsetMin = new Vector2(16, -34); nRect.offsetMax = new Vector2(leftRight, -8);
            tooltip.nameText.alignment = TextAlignmentOptions.MidlineLeft;

            tooltip.statText = Text(panel, "Stats", "", 12, UITheme.TextGold, FontStyles.Normal);
            var sRect = tooltip.statText.rectTransform;
            sRect.anchorMin = new Vector2(0, 1); sRect.anchorMax = new Vector2(1, 1);
            sRect.offsetMin = new Vector2(16, -56); sRect.offsetMax = new Vector2(leftRight, -34);
            tooltip.statText.alignment = TextAlignmentOptions.MidlineLeft;

            tooltip.descText = Text(panel, "Desc", "", 12, UITheme.TextDim, FontStyles.Normal);
            var dRect = tooltip.descText.rectTransform;
            dRect.anchorMin = new Vector2(0, 0); dRect.anchorMax = new Vector2(1, 1);
            dRect.offsetMin = new Vector2(16, 10); dRect.offsetMax = new Vector2(leftRight, -58);
            tooltip.descText.alignment = TextAlignmentOptions.TopLeft;
            tooltip.descText.textWrappingMode = TextWrappingModes.Normal;

            // ---- comparison column (right) ----
            var compare = Panel(panel, "Compare", new Vector2(0, 0), Vector2.zero, Vector2.zero);
            compare.anchorMin = new Vector2(0, 0); compare.anchorMax = new Vector2(1, 1);
            compare.offsetMin = new Vector2(286, 10); compare.offsetMax = new Vector2(-16, -8);
            tooltip.compareRoot = compare.gameObject;

            // Divider between the two columns, parented to the compare column so
            // it disappears with it (the panel collapses to one column then).
            var divider = Image(compare, "Divider", null, new Color(1f, 1f, 1f, 0.10f), stretch: false);
            var dvRect = divider.rectTransform;
            dvRect.anchorMin = new Vector2(0, 0); dvRect.anchorMax = new Vector2(0, 1);
            dvRect.pivot = new Vector2(0.5f, 0.5f);
            dvRect.offsetMin = new Vector2(-8, 2); dvRect.offsetMax = new Vector2(-7, -2);
            dvRect.anchoredPosition = new Vector2(dvRect.anchoredPosition.x, 0);

            tooltip.compareHeader = Text(compare, "CompareHeader", "Equipped", 12, UITheme.TextDim, FontStyles.Bold);
            var chRect = tooltip.compareHeader.rectTransform;
            chRect.anchorMin = new Vector2(0, 1); chRect.anchorMax = new Vector2(1, 1);
            chRect.offsetMin = new Vector2(0, -18); chRect.offsetMax = new Vector2(0, 0);
            tooltip.compareHeader.alignment = TextAlignmentOptions.MidlineLeft;
            tooltip.compareHeader.characterSpacing = 3;

            tooltip.compareName = Text(compare, "CompareName", "", 15, UITheme.TextMain, FontStyles.Bold);
            var cnRect = tooltip.compareName.rectTransform;
            cnRect.anchorMin = new Vector2(0, 1); cnRect.anchorMax = new Vector2(1, 1);
            cnRect.offsetMin = new Vector2(0, -44); cnRect.offsetMax = new Vector2(0, -20);
            tooltip.compareName.alignment = TextAlignmentOptions.MidlineLeft;

            tooltip.compareStat = Text(compare, "CompareStat", "", 12, UITheme.TextGold, FontStyles.Normal);
            var csRect = tooltip.compareStat.rectTransform;
            csRect.anchorMin = new Vector2(0, 1); csRect.anchorMax = new Vector2(1, 1);
            csRect.offsetMin = new Vector2(0, -66); csRect.offsetMax = new Vector2(0, -44);
            tooltip.compareStat.alignment = TextAlignmentOptions.MidlineLeft;

            compare.gameObject.SetActive(false);
        }

        private static void BuildHints(Transform root)
        {
            var hints = Text((RectTransform)root, "Hints",
                "I  Inventory      K  Skills", 13, UITheme.TextDim, FontStyles.Normal);
            hints.characterSpacing = 1;
            var rect = hints.rectTransform;
            rect.anchorMin = new Vector2(1, 0); rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-24, 18);
            rect.sizeDelta = new Vector2(320, 22);
            hints.alignment = TextAlignmentOptions.MidlineRight;
        }

        // ==================== PRIMITIVES ====================

        private static RectTransform Panel(Transform parent, string name, Vector2 anchor,
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

        private static Image Image(RectTransform parent, string name, Sprite sprite,
            Color color, bool stretch, float padding = 0)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(padding, padding);
                rect.offsetMax = new Vector2(-padding, -padding);
            }
            var image = obj.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null && sprite.border.sqrMagnitude > 0)
                image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        /// <summary>Horizontal filled bar inset within its parent.</summary>
        private static Image FillBar(RectTransform parent, string name, Color color, float inset)
        {
            var image = Image(parent, name, S("bar_r6"), color, stretch: true, padding: inset);
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            image.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            return image;
        }

        private static TextMeshProUGUI Text(RectTransform parent, string name, string content,
            float size, Color color, FontStyles style)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }
    }
}
#endif
