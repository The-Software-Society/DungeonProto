using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AldenFall.World
{
    /// <summary>
    /// A world-space notice board that shows the SERVER-WIDE Floor-1 standings, so the whole
    /// town can see the state of the climb: uncleared, or first-cleared by a named hero with
    /// a running clear count. Reads <see cref="ServerProgressState"/> and refreshes on its
    /// OnProgressChanged. Self-builds its canvas; placed in the city/tavern by the scene
    /// builder. Purely presentational (no network state of its own).
    /// </summary>
    public class ProgressBoard : MonoBehaviour
    {
        [SerializeField] private Vector2 boardSize = new Vector2(3.4f, 1.8f);

        private TextMeshProUGUI text;
        private bool bound;

        private void Start()
        {
            BuildCanvas();
            Refresh();
        }

        private void Update()
        {
            if (bound) return;
            var state = ServerProgressState.Instance;
            if (state == null) return;
            state.OnProgressChanged += Refresh;
            bound = true;
            Refresh();
        }

        private void OnDestroy()
        {
            var state = ServerProgressState.Instance;
            if (state != null) state.OnProgressChanged -= Refresh;
        }

        private void BuildCanvas()
        {
            var canvasObj = new GameObject("BoardCanvas");
            canvasObj.transform.SetParent(transform, false);
            canvasObj.transform.localPosition = Vector3.zero;
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(340, 180);
            rt.localScale = Vector3.one * (boardSize.x / 340f);

            var bg = new GameObject("Bg");
            bg.transform.SetParent(canvasObj.transform, false);
            var img = bg.AddComponent<Image>();
            img.color = new Color(0.06f, 0.05f, 0.08f, 0.92f);
            var bgRt = img.rectTransform;
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var textObj = new GameObject("BoardText");
            textObj.transform.SetParent(canvasObj.transform, false);
            text = textObj.AddComponent<TextMeshProUGUI>();
            text.fontSize = 26;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            var tRt = text.rectTransform;
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(14, 12); tRt.offsetMax = new Vector2(-14, -12);
        }

        private void Refresh()
        {
            if (text == null) return;
            var state = ServerProgressState.Instance;
            if (state == null || !state.FloorCleared)
            {
                text.text = "<b>EMBERFALL - FLOOR 1</b>\n<size=70%><color=#B06A6A>Uncleared</color>\nThe Cinderbound Guardian still reigns.</size>";
            }
            else
            {
                text.text = $"<b>EMBERFALL - FLOOR 1</b>\n<size=70%><color=#F2CC8C>Cleared</color>\nFirst: {state.FirstClearName}\n{state.BossClears} total clears</size>";
            }
        }
    }
}
