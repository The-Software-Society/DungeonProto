using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AldenFall.Multiplayer
{
    public class MultiplayerConnectUI : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [SerializeField] private string defaultServer = "aldenfall-test-server.onrender.com";
#else
        [SerializeField] private string defaultServer = "localhost";
#endif

        private GameObject panelRoot;
        private TMP_InputField serverField;
        private TMP_InputField passwordField;
        private TMP_InputField nameField;
        private TMP_Text statusText;
        private int gender;
        private int skinTone;
        private int hairStyle = 1;
        private int hairColor;

        private void Start()
        {
            Build();
        }

        private void Update()
        {
            if (panelRoot != null)
                panelRoot.SetActive(!NetworkClient.isConnected && !NetworkServer.active);
        }

        private void Build()
        {
            var canvasObj = new GameObject("MultiplayerConnectCanvas");
            canvasObj.transform.SetParent(transform, false);
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            panelRoot = canvasObj;

            var panel = Rect(canvasObj.transform, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 455));
            panel.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.04f, 0.065f, 0.97f);

            Label(panel, "Title", "ALDENFALL TEST LOGIN", new Vector2(0, -34), new Vector2(460, 34), 24, new Color(0.95f, 0.78f, 0.46f));
            nameField = Input(panel, "Name", "Name", new Vector2(0, -92), "Tester");
            serverField = Input(panel, "Server", "Server", new Vector2(0, -148), defaultServer);
            passwordField = Input(panel, "Password", "Password", new Vector2(0, -204), "");
            passwordField.contentType = TMP_InputField.ContentType.Password;

            Label(panel, "BodyLabel", "Body", new Vector2(-180, -258), new Vector2(90, 28), 15, Color.white);
            Button(panel, "Male", new Vector2(210, -258), 80, () => gender = 0);
            Button(panel, "Female", new Vector2(304, -258), 90, () => gender = 1);

            Label(panel, "SkinLabel", "Skin", new Vector2(-180, -302), new Vector2(90, 28), 15, Color.white);
            for (int i = 0; i < Player.CharacterAppearance.SkinTones.Length; i++)
            {
                int captured = i;
                Swatch(panel, $"Skin{i}", Player.CharacterAppearance.SkinTones[i], new Vector2(210 + i * 44, -302), () => skinTone = captured);
            }

            Label(panel, "HairLabel", "Hair", new Vector2(-180, -346), new Vector2(90, 28), 15, Color.white);
            for (int i = 0; i < Player.CharacterAppearance.HairStyleNames.Length; i++)
            {
                int captured = i;
                Button(panel, Player.CharacterAppearance.HairStyleNames[i], new Vector2(160 + i * 72, -346), 68, () => hairStyle = captured);
            }

            Button(panel, "Connect", new Vector2(150, -404), 130, Connect);
#if !UNITY_WEBGL
            Button(panel, "Host", new Vector2(300, -404), 100, Host);
#endif
            statusText = Label(panel, "Status", "WebGL connects to the test server. Desktop/editor can host locally.", new Vector2(0, -438), new Vector2(470, 24), 12, new Color(0.72f, 0.68f, 0.78f));
        }

        private void Connect()
        {
            var manager = NetworkManager.singleton as AldenNetworkManager;
            if (manager == null)
            {
                SetStatus("Network manager is missing.");
                return;
            }

            manager.SetLocalLogin(serverField.text, passwordField.text, Profile());
            manager.StartClient();
            SetStatus($"Connecting to {manager.networkAddress}...");
        }

        private void Host()
        {
            var manager = NetworkManager.singleton as AldenNetworkManager;
            if (manager == null)
            {
                SetStatus("Network manager is missing.");
                return;
            }

            manager.SetLocalLogin("localhost", passwordField.text, Profile());
            manager.StartHost();
            SetStatus("Hosting local test session...");
        }

        private AldenPlayerProfile Profile() => new AldenPlayerProfile
        {
            playerName = string.IsNullOrWhiteSpace(nameField.text) ? "Tester" : nameField.text.Trim(),
            gender = gender,
            skinTone = skinTone,
            hairStyle = hairStyle,
            hairColor = hairColor
        };

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

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

        private static TMP_Text Label(RectTransform parent, string name, string content, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 1f), pos, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_InputField Input(RectTransform parent, string name, string placeholder, Vector2 pos, string value)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 1f), pos, new Vector2(380, 38));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.105f, 0.16f, 1f);
            var input = rect.gameObject.AddComponent<TMP_InputField>();

            var text = Label(rect, "Text", value, Vector2.zero, new Vector2(350, 32), 15, Color.white);
            text.alignment = TextAlignmentOptions.Left;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = new Vector2(8, 0);
            var hint = Label(rect, "Placeholder", placeholder, Vector2.zero, new Vector2(350, 32), 15, new Color(0.55f, 0.52f, 0.62f));
            hint.alignment = TextAlignmentOptions.Left;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hint.rectTransform.anchoredPosition = new Vector2(8, 0);

            input.textComponent = text;
            input.placeholder = hint;
            input.text = value;
            return input;
        }

        private static void Button(RectTransform parent, string label, Vector2 pos, float width, UnityEngine.Events.UnityAction click)
        {
            var rect = Rect(parent, $"Button_{label}", new Vector2(0f, 1f), pos, new Vector2(width, 34));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.18f, 0.145f, 0.24f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(click);
            var text = Label(rect, "Label", label, Vector2.zero, new Vector2(width, 30), 13, Color.white);
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = Vector2.zero;
        }

        private static void Swatch(RectTransform parent, string name, Color color, Vector2 pos, UnityEngine.Events.UnityAction click)
        {
            var rect = Rect(parent, name, new Vector2(0f, 1f), pos, new Vector2(34, 28));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(click);
        }
    }
}
