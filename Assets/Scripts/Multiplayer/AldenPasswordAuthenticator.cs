using Mirror;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    public struct AldenAuthRequest : NetworkMessage
    {
        public string password;
        public string playerName;
        public int gender;
        public int skinTone;
        public int hairStyle;
        public int hairColor;
    }

    public struct AldenAuthResponse : NetworkMessage
    {
        public bool accepted;
        public string message;
    }

    public class AldenPasswordAuthenticator : NetworkAuthenticator
    {
        public static event System.Action<string> ClientAuthResult;

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<AldenAuthRequest>(OnAuthRequest, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<AldenAuthRequest>();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            // The client authenticates by sending AldenAuthRequest.
        }

        public override void OnStartClient()
        {
            NetworkClient.RegisterHandler<AldenAuthResponse>(OnAuthResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<AldenAuthResponse>();
        }

        public override void OnClientAuthenticate()
        {
            var manager = NetworkManager.singleton as AldenNetworkManager;
            var profile = manager != null ? manager.LocalProfile : default;
            NetworkClient.Send(new AldenAuthRequest
            {
                password = manager != null ? manager.LocalPassword : "",
                playerName = profile.playerName,
                gender = profile.gender,
                skinTone = profile.skinTone,
                hairStyle = profile.hairStyle,
                hairColor = profile.hairColor
            });
        }

        private void OnAuthRequest(NetworkConnectionToClient conn, AldenAuthRequest msg)
        {
            var manager = NetworkManager.singleton as AldenNetworkManager;
            if (manager == null || !manager.PasswordAccepted(msg.password))
            {
                conn.Send(new AldenAuthResponse { accepted = false, message = "Bad password" });
                ServerReject(conn);
                return;
            }

            manager.SetPendingProfile(conn, new AldenPlayerProfile
            {
                playerName = string.IsNullOrWhiteSpace(msg.playerName) ? $"Player {conn.connectionId + 1}" : msg.playerName.Trim(),
                gender = Mathf.Clamp(msg.gender, 0, 1),
                skinTone = Mathf.Clamp(msg.skinTone, 0, Player.CharacterAppearance.SkinTones.Length - 1),
                hairStyle = Mathf.Clamp(msg.hairStyle, 0, Player.CharacterAppearance.HairStyleNames.Length - 1),
                hairColor = Mathf.Clamp(msg.hairColor, 0, Player.CharacterAppearance.HairColors.Length - 1)
            });
            conn.Send(new AldenAuthResponse { accepted = true, message = "Accepted" });
            ServerAccept(conn);
        }

        private void OnAuthResponse(AldenAuthResponse msg)
        {
            if (msg.accepted)
            {
                ClientAuthResult?.Invoke("Connected. Loading player...");
                ClientAccept();
            }
            else
            {
                ClientAuthResult?.Invoke(string.IsNullOrWhiteSpace(msg.message) ? "Connection rejected." : msg.message);
                ClientReject();
            }
        }
    }
}
