using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;

namespace AldenFall.UI
{
    /// <summary>
    /// Self-contained gear-upgrade forge UI (F7). Built at runtime on its own
    /// canvas so it never touches the authored HUD. Lists every gear item you
    /// hold two identical copies of and lets you fuse them into the next +tier
    /// for gold. Lives on the player; reads the sibling Inventory + Wallet.
    /// </summary>
    public class ForgePanel : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F7;

        private Inventory inventory;
        private Wallet wallet;
        private GameObject panelRoot;
        private RectTransform listParent;
        private TMP_Text goldLabel;

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            wallet = GetComponent<Wallet>();
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping) return;
            if (Input.GetKeyDown(toggleKey)) Toggle();
        }

        private void Toggle()
        {
            if (panelRoot == null) BuildPanel();
            else panelRoot.SetActive(!panelRoot.activeSelf);
            if (panelRoot != null && panelRoot.activeSelf) Rebuild();
        }

        // ==================== FORGEABLES ====================

        private struct Forgeable { public string id; public int count; }

        private List<Forgeable> Collect()
        {
            var counts = new Dictionary<string, int>();
            if (inventory != null)
                foreach (var entry in inventory.Slots)
                {
                    var def = ItemDatabase.Get(entry.ItemId);
                    if (def == null || def.Slot == EquipSlot.None) continue;
                    if (ItemPlus.PlusOf(entry.ItemId) >= ItemPlus.MaxPlus) continue;
                    counts.TryGetValue(entry.ItemId, out int c);
                    counts[entry.ItemId] = c + entry.Count;
                }

            var list = new List<Forgeable>();
            foreach (var kv in counts)
                if (kv.Value >= 2) list.Add(new Forgeable { id = kv.Key, count = kv.Value });
            return list;
        }

        // ==================== UI ====================

        private void BuildPanel()
        {
            var canvasObj = new GameObject("ForgeCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            panelRoot = canvasObj;

            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560, 520));
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.06f, 0.10f, 0.97f);

            MakeText(panel, "Title", "FORGE  <size=12><color=#8A8494>(F7 — fuse two identical items into +1)</color></size>",
                new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(520, 30), 19, UITheme.TextGold,
                TextAlignmentOptions.Center);

            goldLabel = MakeText(panel, "Gold", "", new Vector2(0.5f, 1f), new Vector2(0, -54),
                new Vector2(520, 24), 15, UITheme.TextMain, TextAlignmentOptions.Center);

            listParent = Rect(panel, "List", new Vector2(0.5f, 1f), new Vector2(0, -300),
                new Vector2(520, 440));
        }

        private void Rebuild()
        {
            if (listParent == null) return;
            for (int i = listParent.childCount - 1; i >= 0; i--)
                Destroy(listParent.GetChild(i).gameObject);

            if (goldLabel != null)
                goldLabel.text = $"<color=#F2CC8C>Gold: {(wallet != null ? wallet.Gold : 0)}</color>";

            var forgeables = Collect();
            float y = 0f;
            if (forgeables.Count == 0)
            {
                MakeText(listParent, "Empty",
                    "<color=#8A8494>Hold two identical items of the same +tier to forge.</color>",
                    new Vector2(0.5f, 1f), new Vector2(0, -10), new Vector2(500, 40), 15,
                    UITheme.TextDim, TextAlignmentOptions.Top);
                return;
            }

            foreach (var f in forgeables)
            {
                var def = ItemDatabase.Get(f.id);
                int plus = ItemPlus.PlusOf(f.id);
                int cost = Forge.CostFor(plus);
                bool canAfford = wallet != null && wallet.Gold >= cost;

                string name = ItemPlus.DisplayName(def, plus);
                MakeText(listParent, $"lbl_{f.id}",
                    $"{name}  <color=#8A8494>x{f.count}</color>   →  +{plus + 1}   <color={(canAfford ? "#F2CC8C" : "#B06A6A")}>{cost}g</color>",
                    new Vector2(0f, 1f), new Vector2(20, y - 20), new Vector2(360, 30), 15,
                    UITheme.TextMain, TextAlignmentOptions.MidlineLeft);

                string captured = f.id;
                MakeButton(listParent, $"btn_{f.id}", "FORGE", new Vector2(400, y - 20), 110, () =>
                {
                    if (Forge.DoCombine(inventory, wallet, captured, captured))
                        CombatEvents_Say("Forge success!");
                    else
                        CombatEvents_Say("Can't forge that.");
                    Rebuild();
                });

                y -= 40f;
            }
        }

        private void CombatEvents_Say(string msg) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f, msg, UITheme.TextGold);

        // ---- primitives (mirrors DebugCheatPanel) ----

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text MakeText(RectTransform parent, string name, string content, Vector2 anchor,
            Vector2 pos, Vector2 size, float fontSize, Color color, TextAlignmentOptions alignment)
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

        private void MakeButton(RectTransform parent, string name, string label, Vector2 pos, float width,
            UnityEngine.Events.UnityAction onClick)
        {
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(width, 32));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.22f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);
            MakeText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, 32), 13, UITheme.TextMain, TextAlignmentOptions.Center);
        }
    }
}
