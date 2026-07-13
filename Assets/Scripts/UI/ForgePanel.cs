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
    /// Uses the shared <see cref="PanelChrome"/> so it matches the market:
    /// zebra rows, right-aligned costs, FORGE disabled when unaffordable.
    /// </summary>
    public class ForgePanel : MonoBehaviour
    {
        [SerializeField] private KeyCode toggleKey = KeyCode.F7;

        private const float PanelWidth = 620f;
        private const float RowWidth = PanelWidth - UITheme.Pad * 2f;
        private const float ListTop = 96f;

        private Inventory inventory;
        private Wallet wallet;
        private GameObject panelRoot;
        private RectTransform panel;
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

            panel = PanelChrome.CreatePanel(canvasObj.transform, "Panel",
                new Vector2(PanelWidth, 320f));
            PanelChrome.Header(panel, "Forge", "F7 closes, fuse two identical items into +1");

            goldLabel = PanelChrome.Text(panel, "Gold", "", UITheme.Pad, 62f,
                RowWidth, 20f, UITheme.BodySize, UITheme.TextMain,
                TextAlignmentOptions.MidlineRight);

            listParent = PanelChrome.Item(panel, "List", UITheme.Pad, ListTop, RowWidth, 400f);
        }

        private void Rebuild()
        {
            if (listParent == null) return;
            for (int i = listParent.childCount - 1; i >= 0; i--)
                Destroy(listParent.GetChild(i).gameObject);

            if (goldLabel != null)
                goldLabel.text = $"<color={UITheme.DimHex}>Gold</color>  {UITheme.Price(wallet != null ? wallet.Gold : 0)}";

            var forgeables = Collect();
            if (forgeables.Count == 0)
            {
                PanelChrome.Text(listParent, "Empty",
                    "Hold two identical items of the same +tier to forge.",
                    0f, 10f, RowWidth, 22f, UITheme.BodySize, UITheme.TextDim,
                    TextAlignmentOptions.MidlineLeft);
                panel.sizeDelta = new Vector2(PanelWidth, ListTop + 40f + UITheme.Pad);
                return;
            }

            int rowIndex = 0;
            foreach (var f in forgeables)
            {
                var def = ItemDatabase.Get(f.id);
                int plus = ItemPlus.PlusOf(f.id);
                int cost = Forge.CostFor(plus);
                bool canAfford = wallet != null && wallet.Gold >= cost;
                string name = ItemPlus.DisplayName(def, plus);

                var row = PanelChrome.Row(listParent, $"row_{f.id}", rowIndex,
                    rowIndex * UITheme.RowHeight, RowWidth);

                PanelChrome.Text(row, "Name",
                    $"{name}  <color={UITheme.DimHex}><size=80%>x{f.count}</size></color>",
                    12f, 10f, 244f, 20f, UITheme.BodySize, UITheme.TextMain,
                    TextAlignmentOptions.MidlineLeft);
                PanelChrome.Text(row, "Result", $"→ +{plus + 1}", 264f, 10f, 68f, 20f,
                    UITheme.CaptionSize, UITheme.TextDim, TextAlignmentOptions.Midline);
                PanelChrome.Text(row, "Cost", UITheme.Price(cost, canAfford),
                    340f, 10f, 120f, 20f, UITheme.BodySize, UITheme.TextMain,
                    TextAlignmentOptions.MidlineRight);

                string captured = f.id;
                var button = PanelChrome.MakeButton(row, "ForgeBtn", "FORGE",
                    RowWidth - 100f, 3f, 96f, () =>
                {
                    if (Forge.DoCombine(inventory, wallet, captured, captured))
                        CombatEvents_Say("Forge success!");
                    else
                        CombatEvents_Say("Can't forge that.");
                    Rebuild();
                }, primary: true);
                PanelChrome.SetEnabled(button, canAfford);
                rowIndex++;
            }

            float height = ListTop + rowIndex * UITheme.RowHeight + UITheme.Pad;
            panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(height, 240f));
        }

        private void CombatEvents_Say(string msg) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f, msg, UITheme.TextGold);
    }
}
