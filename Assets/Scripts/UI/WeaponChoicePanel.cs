using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AldenFall.Items;
using AldenFall.Combat;

namespace AldenFall.UI
{
    /// <summary>
    /// The first-login "path" panel Aldric raises during the intro cutscene:
    /// the player picks Warrior (melee) or Mage (magic), and we grant + equip
    /// the matching starter through the normal Inventory/Equipment APIs.
    ///
    /// Combat "style" in this game is emergent, not a flag:
    ///   - Warrior = a Weapon-slot blade equipped, no Focus  -> PlayerMelee swings.
    ///   - Mage    = a Focus equipped, no Weapon              -> PlayerMelee casts
    ///               Magic Bolt autos instead (see PlayerMelee.hasFocus).
    /// So "setting the style" is exactly which slots we fill. The melee sub-style
    /// (Attack/Strength/Defence) is left on CombatStyleController for the warrior.
    ///
    /// Built at runtime (no prefab) to match the codebase's code-as-data UI.
    /// Mouse-driven via a UI Button on each card. Local-player only — the caller
    /// (CutsceneDirector, which lives on the player) passes the player object.
    /// </summary>
    public class WeaponChoicePanel : MonoBehaviour
    {
        public const string StarterMelee = "rusty_sword";       // Weapon slot
        public const string StarterMagic = "apprentice_focus";  // Focus slot (the mage's "wand")
        public const string PathKey = "starter_path_v1";        // "melee" | "magic"

        /// <summary>True while the choice is on screen — the director suppresses SPACE-skip so the pick is mandatory.</summary>
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
            var scrim = NewImage("Scrim", transform, new Color(0f, 0f, 0f, 0.55f));
            Stretch(scrim.rectTransform);

            Label(transform, "Prompt", "CHOOSE YOUR PATH", new Vector2(0, 300), new Vector2(1200, 60), 40, UITheme.TextGold);
            Label(transform, "Sub", "Aldric offers you a blade, or the flame.", new Vector2(0, 250), new Vector2(1200, 40), 22, UITheme.TextDim);

            BuildCard(
                new Vector2(-320, -10), UITheme.Gold,
                "WARRIOR", "The Blade",
                "Wields the Rusty Sword.\nClose the distance and cut them down.\nAutos train Attack / Strength / Defence.",
                ChooseMelee);

            BuildCard(
                new Vector2(320, -10), UITheme.MagicBlue,
                "MAGE", "The Flame",
                "Wields the Apprentice Focus.\nStrike from range with Magic Bolt.\nCasting levels the Magic skill.",
                ChooseMagic);
        }

        private void Update()
        {
            if (group != null && group.alpha < 1f)
                group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime * 3f);
        }

        // ==================== CARDS ====================

        private void BuildCard(Vector2 pos, Color accent, string title, string subtitle, string body, Action onClick)
        {
            var card = Rect(transform, "Card_" + title, new Vector2(0.5f, 0.5f), pos, new Vector2(560, 460));

            var bg = card.gameObject.AddComponent<Image>();
            bg.color = UITheme.PanelBg;

            var button = card.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            // Accent bar along the top edge.
            var bar = NewImage("Accent", card, accent);
            bar.rectTransform.anchorMin = new Vector2(0, 1);
            bar.rectTransform.anchorMax = new Vector2(1, 1);
            bar.rectTransform.pivot = new Vector2(0.5f, 1);
            bar.rectTransform.anchoredPosition = Vector2.zero;
            bar.rectTransform.sizeDelta = new Vector2(0, 8);
            bar.raycastTarget = false;

            Label(card, "Title", title, new Vector2(0, 150), new Vector2(520, 60), 42, accent);
            Label(card, "Subtitle", subtitle, new Vector2(0, 100), new Vector2(520, 40), 24, UITheme.TextMain);

            var bodyLabel = Label(card, "Body", body, new Vector2(0, -40), new Vector2(500, 220), 22, UITheme.TextDim);
            bodyLabel.alignment = TextAlignmentOptions.Top;
        }

        // ==================== SELECTION ====================

        private void ChooseMelee()
        {
            var inv = player != null ? player.GetComponent<Inventory>() : null;
            var equip = player != null ? player.GetComponent<Equipment>() : null;
            if (inv == null || equip == null) { Resolve(); return; }

            if (!inv.Contains(StarterMelee)) inv.Add(StarterMelee);
            // Warrior fights in melee: a Focus would flip autos to magic, so clear it.
            if (equip.GetEquipped(EquipSlot.Focus) != null) equip.Unequip(EquipSlot.Focus);
            equip.Equip(StarterMelee);

            // Default melee sub-style; harmless if already set.
            player.GetComponent<CombatStyleController>()?.Set(CombatStyle.Attack);

            Commit("melee");
        }

        private void ChooseMagic()
        {
            var inv = player != null ? player.GetComponent<Inventory>() : null;
            var equip = player != null ? player.GetComponent<Equipment>() : null;
            if (inv == null || equip == null) { Resolve(); return; }

            if (!inv.Contains(StarterMagic)) inv.Add(StarterMagic);
            equip.Equip(StarterMagic); // Focus slot -> PlayerMelee casts Magic Bolt autos
            // Remove any melee weapon so the mage always casts, even at close range.
            if (equip.GetEquipped(EquipSlot.Weapon) != null) equip.Unequip(EquipSlot.Weapon);

            Commit("magic");
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
