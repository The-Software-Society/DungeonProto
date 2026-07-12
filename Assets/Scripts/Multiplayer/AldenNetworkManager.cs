using System.Collections.Generic;
using System;
using Mirror;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    public struct AldenPlayerProfile
    {
        public string playerName;
        public int gender;
        public int skinTone;
        public int hairStyle;
        public int hairColor;
    }

    public class AldenNetworkManager : NetworkManager
    {
        [Header("Test Server")]
        [SerializeField] private string requiredPassword = "emberfall";
        // Fallback ONLY used if GetStartPosition() returns null (no NetworkStartPositions
        // registered). Must be SAFE: Aldric's forest glade just north of the city, matching
        // the NetworkStart_* points. The old value (0,0.5,-115) dropped players into the soot
        // approach amid mobs, where they died on arrival.
        [SerializeField] private Vector3 defaultSpawn = new Vector3(0f, 0.5f, 84f);

        public AldenPlayerProfile LocalProfile { get; private set; }
        public string LocalPassword { get; private set; } = "";
        public string RequiredPassword => requiredPassword;

        private readonly Dictionary<int, AldenPlayerProfile> pendingProfiles = new Dictionary<int, AldenPlayerProfile>();

        public override void Awake()
        {
            EnsureTransport();
            base.Awake();
            Application.runInBackground = true;
        }

        public override void Start()
        {
            base.Start();
            if (Application.isBatchMode && !NetworkServer.active && !NetworkClient.active)
            {
                ConfigureServerPortFromEnvironment();
                StartServer();
            }
        }

        public void SetLocalLogin(string serverAddress, string password, AldenPlayerProfile profile)
        {
            networkAddress = NormalizeServerAddress(serverAddress);
            LocalPassword = password ?? "";
            LocalProfile = profile;
        }

        public bool PasswordAccepted(string password) =>
            string.IsNullOrEmpty(requiredPassword) || password == requiredPassword;

        private static string NormalizeServerAddress(string serverAddress)
        {
#if UNITY_EDITOR
            const string fallback = "localhost";
#else
            const string fallback = "aldenfall-test-server.onrender.com";
#endif
            if (string.IsNullOrWhiteSpace(serverAddress))
                return fallback;

            string address = serverAddress.Trim();
            if (Uri.TryCreate(address, UriKind.Absolute, out Uri uri) && !string.IsNullOrWhiteSpace(uri.Host))
                address = uri.Host;

            int slash = address.IndexOf('/');
            if (slash >= 0)
                address = address.Substring(0, slash);

            return string.IsNullOrWhiteSpace(address) ? fallback : address;
        }

        public void SetPendingProfile(NetworkConnectionToClient conn, AldenPlayerProfile profile)
        {
            if (conn != null)
                pendingProfiles[conn.connectionId] = profile;
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn != null)
                pendingProfiles.Remove(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            var start = GetStartPosition();
            Vector3 spawnPos = start != null ? start.position : defaultSpawn;
            Quaternion spawnRot = start != null ? start.rotation : Quaternion.identity;

            var player = Instantiate(playerPrefab, spawnPos, spawnRot);
            if (!pendingProfiles.TryGetValue(conn.connectionId, out var profile))
            {
                profile = new AldenPlayerProfile
                {
                    playerName = $"Player {conn.connectionId + 1}",
                    gender = 0,
                    skinTone = 0,
                    hairStyle = 1,
                    hairColor = 0
                };
            }

            var avatar = player.GetComponent<NetworkPlayerAvatar>();
            if (avatar != null)
                avatar.ServerApplyProfile(profile);

            NetworkServer.AddPlayerForConnection(conn, player);
        }

        private void ConfigureServerPortFromEnvironment()
        {
            string portText = Environment.GetEnvironmentVariable("PORT");
            if (string.IsNullOrWhiteSpace(portText) || !ushort.TryParse(portText, out ushort port))
                return;

            EnsureTransport();
            var activeTransport = transport != null ? transport : Transport.active;
            if (activeTransport == null) return;

            var portProperty = activeTransport.GetType().GetProperty("Port");
            if (portProperty != null && portProperty.CanWrite)
            {
                portProperty.SetValue(activeTransport, port);
                Debug.Log($"AldenFall server bound to PORT={port}");
                return;
            }

            var portField = activeTransport.GetType().GetField("port");
            if (portField != null)
            {
                portField.SetValue(activeTransport, port);
                Debug.Log($"AldenFall server bound to PORT={port}");
            }
        }

        private void EnsureTransport()
        {
            if (transport == null)
                transport = GetComponent<Transport>();

            if (transport == null)
                transport = CreateSimpleWebTransport();

            if (transport != null)
            {
                ConfigureSimpleWebTransport(transport, 27777);
                Transport.active = transport;
            }
            else
            {
                Debug.LogError("AldenFall multiplayer requires a Mirror transport, but none could be found or created.");
            }
        }

        private Transport CreateSimpleWebTransport()
        {
            var candidates = new[]
            {
                "Mirror.SimpleWeb.SimpleWebTransport, SimpleWebTransport",
                "Mirror.SimpleWeb.SimpleWebTransport, Mirror.Transports",
                "Mirror.SimpleWeb.SimpleWebTransport, Mirror"
            };

            foreach (string candidate in candidates)
            {
                Type type = Type.GetType(candidate);
                if (type == null || !typeof(Transport).IsAssignableFrom(type))
                    continue;

                var component = gameObject.AddComponent(type) as Transport;
                if (component != null)
                {
                    Debug.Log($"Created Mirror transport: {type.FullName}");
                    return component;
                }
            }

            return null;
        }

        private static void ConfigureSimpleWebTransport(Transport activeTransport, ushort port)
        {
            Type type = activeTransport.GetType();
            if (type.Name != "SimpleWebTransport")
                return;

            var portProperty = type.GetProperty("Port");
            if (portProperty != null && portProperty.CanWrite)
                portProperty.SetValue(activeTransport, port);

            var portField = type.GetField("port");
            if (portField != null)
                portField.SetValue(activeTransport, port);

            var clientUseWss = type.GetField("clientUseWss");
            if (clientUseWss != null)
                clientUseWss.SetValue(activeTransport, true);

            var settingsField = type.GetField("clientWebsocketSettings");
            var settings = settingsField?.GetValue(activeTransport);
            if (settings == null)
                return;

            var optionField = settings.GetType().GetField("ClientPortOption");
            if (optionField == null)
                return;

            object matchWebpageProtocol = Enum.Parse(optionField.FieldType, "MatchWebpageProtocol");
            optionField.SetValue(settings, matchWebpageProtocol);
            settingsField.SetValue(activeTransport, settings);
        }
    }
}
