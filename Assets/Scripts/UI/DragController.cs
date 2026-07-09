using UnityEngine;
using UnityEngine.UI;
using AldenFall.Abilities;

namespace AldenFall.UI
{
    /// <summary>
    /// Canvas-wide drag state: one payload at a time, with a ghost icon that
    /// follows the pointer. Sources call Begin/UpdateGhost/End from their UGUI
    /// drag handlers; drop targets read Payload in OnDrop.
    /// </summary>
    public class DragController : MonoBehaviour
    {
        public struct DragPayload
        {
            public BindingKind Kind;
            public string Id;
            public int FromBarIndex;   // -1 unless dragged off an action-bar slot
        }

        public static DragController Instance { get; private set; }

        private DragPayload payload;
        private bool dragging;
        private Image ghost;

        public bool IsDragging => dragging;
        public DragPayload Payload => payload;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Begin(DragPayload dragPayload, Sprite icon, Color tint)
        {
            payload = dragPayload;
            dragging = true;

            if (ghost == null)
            {
                var obj = new GameObject("DragGhost");
                obj.transform.SetParent(transform, false);
                ghost = obj.AddComponent<Image>();
                ghost.raycastTarget = false;   // never block the drop target
                ghost.preserveAspect = true;
                ghost.rectTransform.sizeDelta = new Vector2(52, 52);
            }

            ghost.sprite = icon;
            ghost.color = icon != null ? tint : new Color(tint.r, tint.g, tint.b, 0.6f);
            ghost.gameObject.SetActive(true);
            ghost.transform.SetAsLastSibling();
            UpdateGhost(Input.mousePosition);
        }

        public void UpdateGhost(Vector2 screenPosition)
        {
            if (ghost == null || !dragging) return;
            ghost.transform.position = screenPosition + new Vector2(6f, -6f);
        }

        public DragPayload End()
        {
            dragging = false;
            if (ghost != null)
                ghost.gameObject.SetActive(false);
            return payload;
        }
    }
}
