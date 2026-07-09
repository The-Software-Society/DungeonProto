using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using AldenFall.Abilities;
using AldenFall.Items;

namespace AldenFall.UI
{
    /// <summary>
    /// One action-bar slot: wraps the AbilitySlotUI visuals with an index into
    /// the ActionBar model, a stack-count label, hover tooltips, and full
    /// drag-and-drop — drag an ability/item ONTO it to bind, drag OFF it to
    /// clear or move. Validation lives in ActionBar.SetSlot; this just reports
    /// the attempt.
    /// </summary>
    public class ActionBarSlotUI : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
        IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public int index;
        public AbilitySlotUI visuals;
        public TMP_Text countText;

        private ActionBar bar;
        private GameHUD hud;
        private AbilityMastery mastery;

        public void Bind(ActionBar actionBar, GameHUD gameHud)
        {
            bar = actionBar;
            hud = gameHud;
            if (actionBar != null)
                mastery = actionBar.GetComponent<AbilityMastery>();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                bar?.Activate(index);
        }

        // ---------- hover tooltip (same detail as the skill-tree tiles) ----------

        public void OnPointerEnter(PointerEventData e)
        {
            var binding = bar != null ? bar.GetSlot(index) : SlotBinding.Empty;
            if (binding.IsEmpty) return;

            if (binding.Kind == BindingKind.Ability)
            {
                var def = AbilityDatabase.Get(binding.Id);
                int rank = mastery != null ? mastery.RankFor(binding.Id) : 1;
                TooltipUI.Instance?.Show(def, rank, onActionBar: true);
            }
            else
            {
                TooltipUI.Instance?.Show(ItemDatabase.Get(binding.Id), false, ItemPlus.PlusOf(binding.Id));
            }
        }

        public void OnPointerExit(PointerEventData e)
        {
            TooltipUI.Instance?.Hide();
        }

        // ---------- drag source (move / clear) ----------

        public void OnBeginDrag(PointerEventData e)
        {
            if (bar == null || DragController.Instance == null) return;
            var binding = bar.GetSlot(index);
            if (binding.IsEmpty) return;

            TooltipUI.Instance?.Hide();
            DragController.Instance.Begin(new DragController.DragPayload
            {
                Kind = binding.Kind,
                Id = binding.Id,
                FromBarIndex = index,
            }, hud != null ? hud.IconFor(binding) : null, Color.white);
        }

        public void OnDrag(PointerEventData e)
        {
            DragController.Instance?.UpdateGhost(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (DragController.Instance == null || !DragController.Instance.IsDragging) return;
            // Nothing accepted the drop → dragging off the bar clears the slot
            DragController.Instance.End();
            bar?.ClearSlot(index);
        }

        // ---------- drop target ----------

        public void OnDrop(PointerEventData e)
        {
            if (bar == null || DragController.Instance == null || !DragController.Instance.IsDragging)
                return;

            var payload = DragController.Instance.End();

            if (payload.FromBarIndex >= 0)
            {
                bar.MoveSlot(payload.FromBarIndex, index);
                return;
            }

            var binding = payload.Kind == BindingKind.Ability
                ? SlotBinding.Ability(payload.Id)
                : SlotBinding.Item(payload.Id);
            bar.SetSlot(index, binding); // model validates (combat slots reject items)
        }
    }
}
