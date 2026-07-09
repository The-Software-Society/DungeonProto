using Mirror;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    public static class LocalPlayerLocator
    {
        public static GameObject Find()
        {
            if (NetworkClient.localPlayer != null)
                return NetworkClient.localPlayer.gameObject;

            var tagged = GameObject.FindGameObjectsWithTag("Player");
            foreach (var obj in tagged)
            {
                var identity = obj.GetComponent<NetworkIdentity>();
                if (identity == null || identity.isLocalPlayer)
                    return obj;
            }

            return tagged.Length > 0 ? tagged[0] : null;
        }
    }
}
