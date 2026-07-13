using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AldenFall.Abilities;
using AldenFall.Items;
using AldenFall.Combat;

namespace AldenFall.UI
{
    /// <summary>
    /// The first-login "starting weapon" panel Aldric raises during the intro
    /// cutscene: the player picks which starter gear to begin with, a worn blade
    /// or an apprentice's focus, and we grant + equip it through the normal
    /// Inventory/Equipment APIs. This is NOT a class choice: the player can swap
    /// weapons and playstyles freely at any time; it's only their opening kit.
    ///
    /// Combat "style" in this game is emergent from the equipped HANDS, not a flag:
    ///   - Blade = a Sword-class main hand      -> PlayerMelee swings; sword abilities.
    ///   - Magic = any magic implement equipped -> PlayerMelee casts Magic Bolt autos
    ///             at range; focus abilities. (Focus off hand OR wand main hand.)
    /// The two hands are independent: sword + focus is a legal hybrid. The melee
    /// sub-style (Attack/Strength/Defence) is left on CombatStyleController.
    ///
    /// Each choice also fills action bar slots 1-5 with a matching starter kit,
    /// so nobody leaves the intro with buttons they can't press.
    ///
    /// Built at runtime (no prefab) to match the codebase's code-as-data UI.
    /// Mouse-driven via a UI Button on each card. Local-player only, the caller
    /// (CutsceneDirector, which lives on the player) passes the player object.
    /// </summary>
    public class WeaponChoicePanel : MonoBehaviour
    {
        public const string StarterMelee = "rusty_sword";           // main hand (sword)
        public const string StarterMagicWand = "apprentice_wand";   // main hand (wand)
        public const string StarterMagicFocus = "apprentice_focus"; // off hand (focus)
        public const string StarterBow = "short_bow";               // main hand (bow)
        public const string StarterArrows = "arrow";                // consumable ammo
        public const int StarterArrowCount = 60;
        public const string PathKey = "starter_path_v1";            // "melee" | "magic" | "archery"

        // Starter kits for action bar keys 1-5 (see ActionBar.ApplyCombatKit).
        public static readonly string[] MeleeKit =
            { "blade_rush", "savage_cleave", "whirlwind", "bash", "bulwark" };
        public static readonly string[] MagicKit =
            { "fireball", "frost_bolt", "ember_nova", "meteor_strike", "arcane_ward" };
        public static readonly string[] ArcheryKit =
            { "power_shot", "multi_shot", "pinning_shot", "rain_of_arrows", "hunters_mark" };

        /// <summary>True while the choice is on screen, the director suppresses SPACE-skip so the pick is mandatory.</summary>
        public static bool IsOpen { get; private set; }

        private GameObject player;
        private Action onChosen;
        private CanvasGroup group;
        private bool resolved;

        /// <summary>Raise the panel over the given player; invokes onChosen once a path is picked.</summary>
        public static WeaponChoicePanel Present(GameObject player, Action onChosen)
        {
            var host = new GameObject("WeaponChoicePanel");
            var panel = host.AddComponent<WeaponChoicePanel>();
            panel.player = player;
            panel.onChosen = onChosen;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            IsOpen = true;
            EnsureEventSystem();

            // Make sure the pointer can click the cards (the camera rig, which
            // owns cursor lock, is disabled during the cutscene).
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // above the cutscene overlay (400)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            gameObject.AddComponent<GraphicRaycaster>();

            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            // Dim scrim behind the cards so the choice reads as a modal moment.
            var scrim = NewImage("Scrim", transform, new Color(0f, 0f, 0f, 0.62f));
            Stretch(scrim.rectTransform);

            var prompt = Label(transform, "Prompt", "CHOOSE YOUR FIRST WEAPON",
                new Vector2(0, 292), new Vector2(1200, 56), 38, UITheme.TextGold);
            prompt.fontStyle = FontStyles.Bold;
            prompt.characterSpacing = 8;
            var sub = Label(transform, "Sub",
                "Aldric offers you a parting gift. You can swap weapons and playstyles freely, anytime: this is only what you set out with.",
                new Vector2(0, 246), new Vector2(1320, 36), 17, UITheme.TextDim);
            sub.characterSpacing = 1;

            BuildCard(
                new Vector2(-580, -32), UITheme.Gold,
                "A WORN BLADE", "Starter sword, main hand",
                "Step in close and cut them down.\n\nSwinging trains Attack / Strength / Defence.\nYour bar fills with five sword skills.\n\nJust your first weapon: trade up whenever you like.",
                ChooseMelee);

            BuildCard(
                new Vector2(0, -32), UITheme.MagicBlue,
                "APPRENTICE'S KIT", "Starter wand and off-hand focus",
                "Strike from range with wand and focus.\n\nCasting levels the Magic skill.\nYour bar fills with five spells.\n\nJust your first weapons: pick up a blade anytime.",
                ChooseMagic);

            BuildCard(
                new Vector2(580, -32), UITheme.TextGold,
                "A HUNTER'S BOW", "Starter short bow and a quiver",
                "Loose arrows from afar and keep your distance.\n\nShooting trains Archery (and Hitpoints).\nYour bar fills with five bow skills.\n\nArrows are ammo: gather them from foes and traders.",
                ChooseArchery);
        }

        private void Update()
        {
            if (group != null && group.alpha < 1f)
                group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime * 3f);
        }

        // ==================== CARDS ====================

        private void BuildCard(Vector2 pos, Color accent, string title, string subtitle, string body, Action onClick)
        {
            var card = Rect(transform, "Card_" + title, new Vector2(0.5f, 0.5f), pos, new Vector2(560, 408));

            // Drop shadow behind the card, then accent keyline, then opaque body.
            var shadow = NewImage("DropShadow", card, UITheme.DropShadow);
            shadow.rectTransform.anchorMin = Vector2.zero;
            shadow.rectTransform.anchorMax = Vector2.one;
            shadow.rectTransform.offsetMin = new Vector2(-2, -12);
            shadow.rectTransform.offsetMax = new Vector2(12, 2);
            shadow.raycastTarget = false;

            var keyline = NewImage("Keyline", card, new Color(accent.r, accent.g, accent.b, 0.45f));
            keyline.rectTransform.anchorMin = Vector2.zero;
            keyline.rectTransform.anchorMax = Vector2.one;
            keyline.rectTransform.offsetMin = new Vector2(-1, -1);
            keyline.rectTransform.offsetMax = new Vector2(1, 1);
            keyline.raycastTarget = false;

            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UITheme.PanelBgDeep;

            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.55f, 1.5f, 1.4f, 1f);   // subtle on the near-black body
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            card.gameObject.AddComponent<HoverScale>().hoverScale = 1.02f;

            // Accent bar along the top edge.
            var bar = NewImage("Accent", card, accent);
            bar.rectTransform.anchorMin = new Vector2(0, 1);
            bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(0, 4);
            bar.raycastTarget = false;

            var titleLabel = Label(card, "Title", title, new Vector2(0, 138), new Vector2(512, 44), 30, accent);
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.characterSpacing = 6;

            var subLabel = Label(card, "Subtitle", subtitle.ToUpperInvariant(),
                new Vector2(0, 102), new Vector2(512, 24), 13, UITheme.TextDim);
            subLabel.characterSpacing = 3;

            var rule = NewImage("Rule", card, new Color(accent.r, accent.g, accent.b, 0.25f));
            rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rule.rectTransform.anchoredPosition = new Vector2(0, 84);
            rule.rectTransform.sizeDelta = new Vector2(464, 1);
            rule.raycastTarget = false;

            var bodyLabel = Label(card, "Body", body, new Vector2(0, -32), new Vector2(464, 200), 18, UITheme.TextMain);
            bodyLabel.alignment = TextAlignmentOptions.Top;
            bodyLabel.lineSpacing = 6;

            var hint = Label(card, "Hint", "CLICK TO CHOOSE", new Vector2(0, -178), new Vector2(512, 20), 12,
                new Color(accent.r, accent.g, accent.b, 0.85f));
            hint.fontStyle = FontStyles.Bold;
            hint.characterSpacing = 4;
        }

        // ==================== SELECTION ====================

        private void ChooseMelee()
        {
            var inv = player != null ? player.GetComponent<Inventory>() : null;
            var equip = player != null ? player.GetComponent<Equipment>() : null;
            if (inv == null || equip == null) { Resolve(); return; }

            GrantAndEquip(inv, equip, StarterMelee); // sword into the main hand

            // Default melee sub-style; harmless if already set.
            player.GetComponent<CombatStyleController>()?.Set(CombatStyle.Attack);

            player.GetComponent<ActionBar>()?.ApplyCombatKit(MeleeKit);
            Commit("melee");
        }

        private void ChooseMagic()
        {
            var inv = player != null ? player.GetComponent<Inventory>() : null;
            var equip = player != null ? player.GetComponent<Equipment>() : null;
            if (inv == null || equip == null) { Resolve(); return; }

            // Wand into the MAIN hand (displacing the starter sword into the bag),
            // focus into the OFF hand. Both hands filled = full spell kit usable.
            GrantAndEquip(inv, equip, StarterMagicWand);
            GrantAndEquip(inv, equip, StarterMagicFocus);

            player.GetComponent<ActionBar>()?.ApplyCombatKit(MagicKit);
            Commit("magic");
        }

        private void ChooseArchery()
        {
            var inv = player != null ? player.GetComponent<Inventory>() : null;
            var equip = player != null ? player.GetComponent<Equipment>() : null;
            if (inv == null || equip == null) { Resolve(); return; }

            GrantAndEquip(inv, equip, StarterBow);   // bow into the main hand
            for (int i = 0; i < StarterArrowCount; i++) inv.Add(StarterArrows);

            player.GetComponent<ActionBar>()?.ApplyCombatKit(ArcheryKit);
            Commit("archery");
        }

        private static void GrantAndEquip(Inventory inv, Equipment equip, string itemId)
        {
            if (equip.IsEquipped(itemId)) return;
            if (!inv.Contains(itemId)) inv.Add(itemId);
            equip.Equip(itemId);
        }

        private void Commit(string path)
        {
            PlayerPrefs.SetString(PathKey, path);
            PlayerPrefs.Save();
            Resolve();
        }

        private void Resolve()
        {
            if (resolved) return;
            resolved = true;
            IsOpen = false;
            onChosen?.Invoke();
            Destroy(gameObject);
        }

        // ==================== UI HELPERS ====================

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var image = obj.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Label(Transform parent, string name, string content, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 0.5f), pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }
    }
}
