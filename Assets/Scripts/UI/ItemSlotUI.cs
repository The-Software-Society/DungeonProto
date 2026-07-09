using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;
using AldenFall.Multiplayer;

namespace AldenFall.UI
{
    /// <summary>
    /// Inventory grid slot: icon (or rarity-tinted glyph), hover tooltip,
    /// click-to-equip, equipped badge, stack count. Also a drag SOURCE: drag a
    /// consumable onto action-bar slots 6-0 to bind it (the bar validates).
    /// </summary>
    public class ItemSlotUI : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Image background;
        public Image border;
        public Image icon;
        public GameObject equippedBadge;
        public TMP_Text countText;   // stack count, hidden at <= 1

        private string itemId;
        private System.Action<string> onClick;
        private bool equipped;

        public string ItemId => itemId;

        public void Bind(string id, bool isEquipped, System.Action<string> clickHandler, int count = 1)
        {
            itemId = id;
            equipped = isEquipped;
            onClick = clickHandler;

            if (countText != null)
            {
                bool showCount = id != null && count > 1;
                countText.gameObject.SetActive(showCount);
                if (showCount) countText.text = count.ToString();
            }

            var def = id != null ? ItemDatabase.Get(id) : null;
            bool has = def != null;

            if (icon != null)
            {
                var library = ItemVisualLibrary.Instance;
                Sprite sprite = has && library != null ? library.GetIcon(def.Id) : null;
                icon.enabled = has;
                if (has)
                {
                    icon.sprite = sprite;
                    icon.color = sprite != null ? Color.white : def.RarityColor;
                }
            }

            if (border != null)
            {
                // Border escalates with the upgrade tier (+4 blue, +7 purple, +9 orange)
                Color c = has ? ItemPlus.PlusColor(def, ItemPlus.PlusOf(id)) : UITheme.SlotBorder;
                c.a = has ? 0.55f : UITheme.SlotBorder.a;
                border.color = c;
            }

            if (equippedBadge != null)
                equippedBadge.SetActive(equipped);
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (itemId == null) return;
            TooltipUI.Instance?.Show(ItemDatabase.Get(itemId), equipped, ItemPlus.PlusOf(itemId));
        }

        public void OnPointerExit(PointerEventData e)
        {
            TooltipUI.Instance?.Hide();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (itemId == null) return;
            onClick?.Invoke(itemId);
            TooltipUI.Instance?.Hide();
        }

        // ---------- drag source (bind consumables to the action bar) ----------

        public void OnBeginDrag(PointerEventData e)
        {
            if (itemId == null || DragController.Instance == null) return;

            var library = ItemVisualLibrary.Instance;
            var def = ItemDatabase.Get(itemId);
            Sprite sprite = library != null ? library.GetIcon(itemId) : null;

            DragController.Instance.Begin(new DragController.DragPayload
            {
                Kind = Abilities.BindingKind.Item,
                Id = itemId,
                FromBarIndex = -1,
            }, sprite, sprite != null ? Color.white : (def?.RarityColor ?? Color.white));
            TooltipUI.Instance?.Hide();
        }

        public void OnDrag(PointerEventData e)
        {
            DragController.Instance?.UpdateGhost(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            // A real drop target (action-bar slot) consumes the payload in its
            // OnDrop, which runs before OnEndDrag — nothing left to do then.
            if (DragController.Instance == null || !DragController.Instance.IsDragging)
                return;

            DragController.Instance.End();

            // Released over the game world (no UI under the pointer): the item
            // leaves the bag and lands on the ground in front of the player.
            bool overWorld = e.pointerCurrentRaycast.gameObject == null;
            if (overWorld && itemId != null)
                DropToGround(itemId);
        }

        private static void DropToGround(string id)
        {
            var playerObj = LocalPlayerLocator.Find();
            if (playerObj == null) return;

            var inventory = playerObj.GetComponent<Inventory>();
            if (inventory == null || !inventory.Remove(id)) return;

            // Toss it a couple of meters ahead — outside auto-pickup range —
            // with a short grace period so it isn't instantly re-collected.
            Vector3 pos = playerObj.transform.position + playerObj.transform.forward * 2.4f;
            LootDrop.Spawn(id, pos, pickupDelay: 1.25f);
        }
    }
}
