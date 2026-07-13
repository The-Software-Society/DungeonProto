using System;
using UnityEngine;

namespace AldenFall.Progression
{
    /// <summary>
    /// Per-player world progress: which floors this player has personally cleared. Distinct
    /// from ServerProgressState (which tracks the SERVER-WIDE first clear) - this is the
    /// individual's own record, persisted by PlayerSave and used to gate access to later
    /// floors. Plain component added via EnsureGameplayComponents.
    /// </summary>
    public class PlayerProgress : MonoBehaviour
    {
        [SerializeField] private bool floor1Cleared;

        /// <summary>Fired when a floor-clear flag flips on (UI/gates refresh).</summary>
        public event Action OnProgressChanged;

        public bool Floor1Cleared => floor1Cleared;

        /// <summary>Mark Floor 1 as personally cleared (idempotent). Called server-side when
        /// this player is credited with - or partied on - the Floor-Boss kill.</summary>
        public void MarkFloor1Cleared()
        {
            if (floor1Cleared) return;
            floor1Cleared = true;
            OnProgressChanged?.Invoke();
        }

        /// <summary>Restore from a saved profile without re-firing gameplay side effects.</summary>
        public void Restore(bool cleared1)
        {
            floor1Cleared = cleared1;
            OnProgressChanged?.Invoke();
        }
    }
}
