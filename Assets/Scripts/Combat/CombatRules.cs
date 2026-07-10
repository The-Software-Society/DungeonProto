using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Global combat rules / toggles that don't belong to any single entity.
    ///
    /// PvP is OFF right now — players can't damage each other. This is a deliberate
    /// runtime switch, NOT a hard removal: flip <see cref="PvpEnabled"/> (or later make
    /// it per-zone / per-duel) when the PvP system lands. Nothing here deletes the
    /// ability to hurt another player; it just gates it.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>When false, player-vs-player damage is suppressed everywhere.</summary>
        public static bool PvpEnabled = false;

        /// <summary>
        /// True if the GameObject is a player-controlled avatar (local OR remote).
        /// Every player carries a NetworkPlayerAvatar; remote players are tagged
        /// "Untagged", so a tag check would miss them — component presence is reliable.
        /// </summary>
        public static bool IsPlayer(GameObject go)
        {
            return go != null && go.GetComponent<Multiplayer.NetworkPlayerAvatar>() != null;
        }
    }
}
