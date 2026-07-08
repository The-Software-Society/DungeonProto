using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AldenFall.Multiplayer
{
    public class MultiplayerConnectUI : MonoBehaviour
    {
        private const string RenderServerHost = "aldenfall-test-server.onrender.com";

        private GameObject panelRoot;
        private GameObject loginRoot;
        private GameObject characterRoot;
        private TMP_InputField serverField;
        private TMP_InputField passwordField;
        private TMP_InputField nameField;
        private TMP_Text statusText;
        private TMP_Text characterStatusText;
        private GameObject previewRoot;
        private Player.CharacterAppearance previewAppearance;
        private bool connecting;
        private int gender;
        private int skinTone;
        private int hairStyle = 1;
        private int hairColor;

        private void Start()
        {
            EnsureEventSystem();
            AldenPasswordAuthenticator.ClientAuthResult += OnAuthResult;
            Build();
        }

        private void OnDestroy()
        {
            AldenPasswordAuthenticator.ClientAuthResult -= OnAuthResult;
            if (previewRoot != null)
                Destroy(previewRoot);
        }

        private void Update()
        {
            bool visible = !NetworkClient.isConnected && !NetworkServer.active;
            if (panelRoot != null)
                panelRoot.SetActive(visible);
            if (previewRoot != null)
                previewRoot.SetActive(visible && characterRoot != null && characterRoot.activeSelf);

            if (connecting && !NetworkClient.active && !NetworkClient.isConnected)
            {
                connecting = false;
                SetStatus("Connection failed. Check the server and password.");
            }
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

            BuildLogin(canvasObj.transform);
            BuildCharacterSelect(canvasObj.transform);
            ShowLogin();
        }

        private void BuildLogin(Transform parent)
        {
            var panel = Rect(parent, "LoginPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 330));
            panel.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.04f, 0.065f, 0.97f);
            loginRoot = panel.gameObject;

            Label(panel, "Title", "ALDENFALL LOGIN", new Vector2(0, -34), new Vector2(500, 34), 24, new Color(0.95f, 0.78f, 0.46f));
            serverField = Input(panel, "Server", "Server", new Vector2(0, -104), DefaultServer(), 420);
            passwordField = Input(panel, "Password", "Password", new Vector2(0, -164), "", 420);
            passwordField.contentType = TMP_InputField.ContentType.Password;

            Button(panel, "Next", new Vector2(160, -230), 120, ShowCharacterSelect);
#if !UNITY_WEBGL
            Button(panel, "Host", new Vector2(300, -230), 100, Host);
#endif
            statusText = Label(panel, "Status", "Enter the test password, then choose your character.", new Vector2(0, -286), new Vector2(500, 24), 12, new Color(0.72f, 0.68f, 0.78f));
        }

        private void BuildCharacterSelect(Transform parent)
        {
            var panel = Rect(parent, "CharacterPanel", new Vector2(0.5f, 0.5f), new Vector2(-260, 0), new Vector2(560, 500));
            panel.gameObject.AddComponent<Image>().color = new Color(0.045f, 0.04f, 0.065f, 0.97f);
            characterRoot = panel.gameObject;

            Label(panel, "Title", "CREATE CHARACTER", new Vector2(0, -34), new Vector2(500, 34), 24, new Color(0.95f, 0.78f, 0.46f));
            nameField = Input(panel, "Name", "Name", new Vector2(0, -96), "Tester", 420);

            Label(panel, "BodyLabel", "Body", new Vector2(-185, -160), new Vector2(90, 28), 15, Color.white);
            Button(panel, "Male", new Vector2(230, -160), 80, () => SetGender(0));
            Button(panel, "Female", new Vector2(324, -160), 90, () => SetGender(1));

            Label(panel, "SkinLabel", "Skin", new Vector2(-185, -216), new Vector2(90, 28), 15, Color.white);
            for (int i = 0; i < Player.CharacterAppearance.SkinTones.Length; i++)
            {
                int captured = i;
                Swatch(panel, $"Skin{i}", Player.CharacterAppearance.SkinTones[i], new Vector2(230 + i * 44, -216), () => SetSkin(captured));
            }

            Label(panel, "HairLabel", "Hair", new Vector2(-185, -272), new Vector2(90, 28), 15, Color.white);
            for (int i = 0; i < Player.CharacterAppearance.HairStyleNames.Length; i++)
            {
                int captured = i;
                Button(panel, Player.CharacterAppearance.HairStyleNames[i], new Vector2(180 + i * 72, -272), 68, () => SetHair(captured));
            }

            Label(panel, "HairColorLabel", "Color", new Vector2(-185, -328), new Vector2(90, 28), 15, Color.white);
            for (int i = 0; i < Player.CharacterAppearance.HairColors.Length; i++)
            {
                int captured = i;
                Swatch(panel, $"HairColor{i}", Player.CharacterAppearance.HairColors[i], new Vector2(230 + i * 44, -328), () => SetHairColor(captured));
            }

            Button(panel, "Back", new Vector2(160, -406), 100, ShowLogin);
            Button(panel, "Connect", new Vector2(280, -406), 140, Connect);
            characterStatusText = Label(panel, "PreviewNote", "Preview appears to the right. Final selection applies when you connect.", new Vector2(0, -462), new Vector2(500, 24), 12, new Color(0.72f, 0.68f, 0.78f));
        }

        private void ShowLogin()
        {
            if (loginRoot != null)
                loginRoot.SetActive(true);
            if (characterRoot != null)
                characterRoot.SetActive(false);
            if (previewRoot != null)
                previewRoot.SetActive(false);
            SetStatus("Enter the test password, then choose your character.");
        }

        private void ShowCharacterSelect()
        {
            if (string.IsNullOrWhiteSpace(passwordField.text))
            {
                SetStatus("Enter the test password first.");
                return;
            }

            loginRoot.SetActive(false);
            characterRoot.SetActive(true);
            SetStatus("Choose your character, then connect.");
            EnsurePreview();
            ApplyPreview();
        }

        private void Connect()
        {
            var manager = NetworkManager.singleton as AldenNetworkManager;
            if (manager == null)
            {
                SetStatus("Network manager is missing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(passwordField.text))
            {
                SetStatus("Enter the test password before connecting.");
                ShowLogin();
                return;
            }

            manager.SetLocalLogin(serverField.text, passwordField.text, Profile());
            manager.StartClient();
            connecting = true;
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
            connecting = false;
            SetStatus("Hosting local test session...");
        }

        private void OnAuthResult(string message)
        {
            connecting = false;
            SetStatus(message);
        }

        private void EnsurePreview()
        {
            if (previewRoot != null)
            {
                previewRoot.SetActive(true);
                return;
            }

            var manager = NetworkManager.singleton;
            if (manager == null || manager.playerPrefab == null || Camera.main == null)
                return;

            previewRoot = Instantiate(manager.playerPrefab);
            previewRoot.name = "CharacterPreview";
            foreach (var behaviour in previewRoot.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour is Player.CharacterAppearance || behaviour is Animator)
                    continue;
                behaviour.enabled = false;
            }

            foreach (var collider in previewRoot.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            previewAppearance = previewRoot.GetComponent<Player.CharacterAppearance>();

            var cam = Camera.main.transform;
            previewRoot.transform.position = Camera.main.ViewportToWorldPoint(new Vector3(0.74f, 0.43f, 4.5f));
            previewRoot.transform.rotation = Quaternion.LookRotation(-cam.forward, Vector3.up);
            previewRoot.transform.localScale = Vector3.one * 1.15f;

            var lightObj = new GameObject("CharacterPreviewLight");
            lightObj.transform.SetParent(previewRoot.transform, false);
            lightObj.transform.localPosition = new Vector3(0f, 2.5f, -2f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            lightObj.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
        }

        private void SetGender(int value)
        {
            gender = value;
            ApplyPreview();
        }

        private void SetSkin(int value)
        {
            skinTone = value;
            ApplyPreview();
        }

        private void SetHair(int value)
        {
            hairStyle = value;
            ApplyPreview();
        }

        private void SetHairColor(int value)
        {
            hairColor = value;
            ApplyPreview();
        }

        private void ApplyPreview()
        {
            if (previewAppearance != null)
                previewAppearance.ApplyNetworkSelection(gender, skinTone, hairStyle, hairColor);
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
            if (characterStatusText != null)
                characterStatusText.text = message;
        }

        private static string DefaultServer()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return RenderServerHost;
#else
            return "localhost";
#endif
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
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

        private static TMP_InputField Input(RectTransform parent, string name, string placeholder, Vector2 pos, string value, float width)
        {
            var rect = Rect(parent, name, new Vector2(0.5f, 1f), pos, new Vector2(width, 38));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.105f, 0.16f, 1f);
            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.caretColor = new Color(0.95f, 0.78f, 0.46f);
            input.selectionColor = new Color(0.95f, 0.78f, 0.46f, 0.35f);

            var text = Label(rect, "Text", value, Vector2.zero, new Vector2(width - 30f, 32), 15, Color.white);
            text.alignment = TextAlignmentOptions.Left;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = new Vector2(8, 0);
            var hint = Label(rect, "Placeholder", placeholder, Vector2.zero, new Vector2(width - 30f, 32), 15, new Color(0.55f, 0.52f, 0.62f));
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
