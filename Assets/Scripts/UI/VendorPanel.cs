using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;

namespace AldenFall.UI
{
    /// <summary>
    /// Self-contained buy/sell vendor UI, opened by interacting with a
    /// <see cref="World.VendorNPC"/> (press E at the market). Built at runtime on
    /// its own canvas like <see cref="ForgePanel"/>. Lives on the player; reads
    /// the sibling Inventory + Wallet. Buy prices are a markup over an item's
    /// SellValue; selling pays SellValue. Closes on Escape or walking away.
    /// </summary>
    public class VendorPanel : MonoBehaviour
    {
        private const float BuyMarkup = 4f;
        private const float WalkAwayDistance = 5f;

        private Inventory inventory;
        private Wallet wallet;
        private GameObject panelRoot;
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

            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760, 560));
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.06f, 0.10f, 0.97f);

            MakeText(panel, "Title", "MARKET  <size=12><color=#8A8494>(Esc or walk away to leave)</color></size>",
                new Vector2(0.5f, 1f), new Vector2(0, -26), new Vector2(700, 30), 19, UITheme.TextGold,
                TextAlignmentOptions.Center);

            goldLabel = MakeText(panel, "Gold", "", new Vector2(0.5f, 1f), new Vector2(0, -54),
                new Vector2(700, 24), 15, UITheme.TextMain, TextAlignmentOptions.Center);

            MakeText(panel, "BuyHeader", "BUY", new Vector2(0f, 1f), new Vector2(96, -86),
                new Vector2(180, 24), 16, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);
            MakeText(panel, "SellHeader", "SELL", new Vector2(0f, 1f), new Vector2(476, -86),
                new Vector2(180, 24), 16, UITheme.TextGold, TextAlignmentOptions.MidlineLeft);

            buyList = Rect(panel, "BuyList", new Vector2(0f, 1f), new Vector2(190, -320),
                new Vector2(360, 430));
            sellList = Rect(panel, "SellList", new Vector2(0f, 1f), new Vector2(570, -320),
                new Vector2(360, 430));
        }

        private void Rebuild()
        {
            if (buyList == null) return;
            Clear(buyList);
            Clear(sellList);

            if (goldLabel != null)
                goldLabel.text = $"<color=#F2CC8C>Gold: {(wallet != null ? wallet.Gold : 0)}</color>";

            // ---- Buy column: the vendor's stock ----
            float y = 0f;
            foreach (var id in stock)
            {
                var def = ItemDatabase.Get(id);
                if (def == null) continue;
                int price = BuyPrice(def);
                bool canAfford = wallet != null && wallet.Gold >= price;

                MakeText(buyList, $"buy_lbl_{id}",
                    $"{def.Name}   <color={(canAfford ? "#F2CC8C" : "#B06A6A")}>{price}g</color>",
                    new Vector2(0f, 1f), new Vector2(10, y - 18), new Vector2(240, 30), 14,
                    UITheme.TextMain, TextAlignmentOptions.MidlineLeft);

                string captured = id;
                MakeButton(buyList, $"buy_btn_{id}", "BUY", new Vector2(268, y - 18), 80, () =>
                {
                    var d = ItemDatabase.Get(captured);
                    if (d == null) return;
                    int cost = BuyPrice(d);
                    if (wallet == null || !wallet.TrySpend(cost))
                        Say("Not enough gold.");
                    else if (inventory == null || !inventory.Add(captured))
                    {
                        wallet.Add(cost); // refund — bag was full
                        Say("Your bag is full.");
                    }
                    else
                        Say($"Bought {d.Name}.");
                    Rebuild();
                });

                y -= 38f;
            }

            // ---- Sell column: everything in the bag with a price ----
            var counts = new Dictionary<string, int>();
            if (inventory != null)
                foreach (var entry in inventory.Slots)
                {
                    if (string.IsNullOrEmpty(entry.ItemId)) continue;
                    counts.TryGetValue(entry.ItemId, out int c);
                    counts[entry.ItemId] = c + entry.Count;
                }

            y = 0f;
            if (counts.Count == 0)
                MakeText(sellList, "Empty", "<color=#8A8494>Nothing to sell.</color>",
                    new Vector2(0f, 1f), new Vector2(10, -18), new Vector2(300, 30), 14,
                    UITheme.TextDim, TextAlignmentOptions.MidlineLeft);

            foreach (var kv in counts)
            {
                var def = ItemDatabase.Get(kv.Key);
                if (def == null || def.SellValue <= 0) continue;
                int plus = ItemPlus.PlusOf(kv.Key);
                string name = def.Slot != EquipSlot.None ? ItemPlus.DisplayName(def, plus) : def.Name;

                MakeText(sellList, $"sell_lbl_{kv.Key}",
                    $"{name} <color=#8A8494>x{kv.Value}</color>   <color=#F2CC8C>{def.SellValue}g</color>",
                    new Vector2(0f, 1f), new Vector2(10, y - 18), new Vector2(240, 30), 14,
                    UITheme.TextMain, TextAlignmentOptions.MidlineLeft);

                string captured = kv.Key;
                MakeButton(sellList, $"sell_btn_{kv.Key}", "SELL", new Vector2(268, y - 18), 80, () =>
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

                y -= 38f;
            }
        }

        private static void Clear(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Destroy(parent.GetChild(i).gameObject);
        }

        private void Say(string msg) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f, msg, UITheme.TextGold);

        // ---- primitives (mirrors ForgePanel) ----

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
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(width, 30));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.16f, 0.14f, 0.22f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(onClick);
            MakeText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(width, 30), 13, UITheme.TextMain, TextAlignmentOptions.Center);
        }
    }
}
