using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;
using AldenFall.Data;

namespace AldenFall.UI
{
    /// <summary>
    /// Self-contained buy/sell vendor UI, opened by interacting with a
    /// <see cref="World.VendorNPC"/> (press E at the market). Built at runtime on
    /// its own canvas like <see cref="ForgePanel"/>. Lives on the player; reads
    /// the sibling Inventory + Wallet. Buy prices are a markup over an item's
    /// SellValue; selling pays SellValue. Closes on Escape or walking away.
    /// Chrome + rows come from <see cref="PanelChrome"/> so the market matches
    /// every other panel: zebra rows, right-aligned prices, disabled buttons
    /// when an action can't run.
    ///
    /// Economy scope: buy/sell mutate the LOCAL player's sibling Wallet + Inventory
    /// directly. In host mode (the beta's primary path) those ARE the authoritative
    /// components, so the trade is real. On a dedicated server a pure client would
    /// mutate only its client-local copies (server never sees the trade), the same
    /// accepted limitation as loot/quest credit (see MobIdentity.GrantKillRewards);
    /// routing trades through a per-player [Command] relay is the Phase 13
    /// dedicated-server economy-authority follow-up.
    /// </summary>
    public class VendorPanel : MonoBehaviour
    {
        // Buy markup over SellValue now lives in config/economy.json (vendorBuyMultiplier).
        private static float BuyMarkup => GameConfig.Economy.vendorBuyMultiplier;
        private const float WalkAwayDistance = 5f;

        private const float PanelWidth = 760f;
        private const float ColumnWidth = 340f;
        private const float ListTop = 116f;

        private Inventory inventory;
        private Wallet wallet;
        private GameObject panelRoot;
        private RectTransform panel;
        private RectTransform buyList;
        private RectTransform sellList;
        private TMP_Text goldLabel;
        private Transform vendor;
        private string[] stock = System.Array.Empty<string>();

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            wallet = GetComponent<Wallet>();
        }

        public static int BuyPrice(ItemDef def) => Mathf.Max(1, Mathf.RoundToInt(def.SellValue * BuyMarkup));

        public void Open(Transform vendorTransform, string[] stockIds)
        {
            vendor = vendorTransform;
            stock = stockIds ?? System.Array.Empty<string>();
            if (panelRoot == null) BuildPanel();
            panelRoot.SetActive(true);
            Rebuild();
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (vendor != null && Vector3.Distance(transform.position, vendor.position) > WalkAwayDistance)
                Close();
        }

        // ==================== UI ====================

        private void BuildPanel()
        {
            var canvasObj = new GameObject("VendorCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 260;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            panelRoot = canvasObj;

            panel = PanelChrome.CreatePanel(canvasObj.transform, "Panel",
                new Vector2(PanelWidth, 360f));
            PanelChrome.Header(panel, "Market", "Esc or walk away to leave");

            goldLabel = PanelChrome.Text(panel, "Gold", "", UITheme.Pad, 62f,
                PanelWidth - UITheme.Pad * 2f, 20f, UITheme.BodySize, UITheme.TextMain,
                TextAlignmentOptions.MidlineRight);

            PanelChrome.Section(panel, "BuyHeader", "Buy", UITheme.Pad, 90f, ColumnWidth);
            PanelChrome.Section(panel, "SellHeader", "Sell",
                PanelWidth - UITheme.Pad - ColumnWidth, 90f, ColumnWidth);

            buyList = PanelChrome.Item(panel, "BuyList", UITheme.Pad, ListTop,
                ColumnWidth, 400f);
            sellList = PanelChrome.Item(panel, "SellList",
                PanelWidth - UITheme.Pad - ColumnWidth, ListTop, ColumnWidth, 400f);
        }

        private void Rebuild()
        {
            if (buyList == null) return;
            Clear(buyList);
            Clear(sellList);

            if (goldLabel != null)
                goldLabel.text = $"<color={UITheme.DimHex}>Gold</color>  {UITheme.Price(wallet != null ? wallet.Gold : 0)}";

            // ---- Buy column: the vendor's stock ----
            int buyRows = 0;
            foreach (var id in stock)
            {
                var def = ItemDatabase.Get(id);
                if (def == null) continue;
                int price = BuyPrice(def);
                bool canAfford = wallet != null && wallet.Gold >= price;

                var row = PanelChrome.Row(buyList, $"buy_{id}", buyRows,
                    buyRows * UITheme.RowHeight, ColumnWidth);
                PanelChrome.Text(row, "Name", def.Name, 12f, 10f, 164f, 20f,
                    UITheme.BodySize, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                PanelChrome.Text(row, "Price", UITheme.Price(price, canAfford),
                    172f, 10f, 88f, 20f, UITheme.BodySize, UITheme.TextMain,
                    TextAlignmentOptions.MidlineRight);

                string captured = id;
                var button = PanelChrome.MakeButton(row, "BuyBtn", "BUY",
                    ColumnWidth - 68f, 3f, 64f, () =>
                {
                    var d = ItemDatabase.Get(captured);
                    if (d == null) return;
                    int cost = BuyPrice(d);
                    if (wallet == null || !wallet.TrySpend(cost))
                        Say("Not enough gold.");
                    else if (inventory == null || !inventory.Add(captured))
                    {
                        wallet.Add(cost); // refund, bag was full
                        Say("Your bag is full.");
                    }
                    else
                        Say($"Bought {d.Name}.");
                    Rebuild();
                });
                PanelChrome.SetEnabled(button, canAfford);
                buyRows++;
            }
            if (buyRows == 0)
                PanelChrome.Text(buyList, "Empty", "Nothing in stock.", 12f, 10f,
                    ColumnWidth - 24f, 20f, UITheme.BodySize, UITheme.TextDim,
                    TextAlignmentOptions.MidlineLeft);

            // ---- Sell column: everything in the bag with a price ----
            var counts = new Dictionary<string, int>();
            if (inventory != null)
                foreach (var entry in inventory.Slots)
                {
                    if (string.IsNullOrEmpty(entry.ItemId)) continue;
                    counts.TryGetValue(entry.ItemId, out int c);
                    counts[entry.ItemId] = c + entry.Count;
                }

            int sellRows = 0;
            foreach (var kv in counts)
            {
                var def = ItemDatabase.Get(kv.Key);
                if (def == null || def.SellValue <= 0) continue;
                int plus = ItemPlus.PlusOf(kv.Key);
                string name = def.Slot != EquipSlot.None ? ItemPlus.DisplayName(def, plus) : def.Name;
                string qty = kv.Value > 1
                    ? $"  <color={UITheme.DimHex}><size=80%>x{kv.Value}</size></color>"
                    : "";

                var row = PanelChrome.Row(sellList, $"sell_{kv.Key}", sellRows,
                    sellRows * UITheme.RowHeight, ColumnWidth);
                PanelChrome.Text(row, "Name", $"{name}{qty}", 12f, 10f, 164f, 20f,
                    UITheme.BodySize, UITheme.TextMain, TextAlignmentOptions.MidlineLeft);
                PanelChrome.Text(row, "Price", UITheme.Price(def.SellValue),
                    172f, 10f, 88f, 20f, UITheme.BodySize, UITheme.TextMain,
                    TextAlignmentOptions.MidlineRight);

                string captured = kv.Key;
                PanelChrome.MakeButton(row, "SellBtn", "SELL",
                    ColumnWidth - 68f, 3f, 64f, () =>
                {
                    var d = ItemDatabase.Get(captured);
                    if (d == null || inventory == null || !inventory.Remove(captured))
                    {
                        Say("Can't sell that.");
                    }
                    else
                    {
                        wallet?.Add(d.SellValue);
                        Say($"Sold {d.Name} for {d.SellValue}g.");
                    }
                    Rebuild();
                });
                sellRows++;
            }
            if (sellRows == 0)
                PanelChrome.Text(sellList, "Empty", "Nothing to sell.", 12f, 10f,
                    ColumnWidth - 24f, 20f, UITheme.BodySize, UITheme.TextDim,
                    TextAlignmentOptions.MidlineLeft);

            // Panel hugs its content instead of leaving a dead lower half.
            int rows = Mathf.Max(buyRows, sellRows, 1);
            float height = ListTop + rows * UITheme.RowHeight + UITheme.Pad;
            panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(height, 280f));
        }

        private static void Clear(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private void Say(string msg) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f, msg, UITheme.TextGold);
    }
}
