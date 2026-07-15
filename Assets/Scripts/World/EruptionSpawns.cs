using System.Collections;
using Mirror;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Near-city eruption consequences: a pre-built, disabled "Content" child (lava pools with
    /// LavaHazard) switched on only during the Erupting phase. With per-player phasing the phase is
    /// per-player, so:
    ///   - on a CLIENT the content is visible while the LOCAL player's world is Erupting;
    ///   - on the SERVER (incl. a headless dedicated server with no local player) the content/collider
    ///     is kept active whenever ANY player is Erupting, so LavaHazard exists to burn them (it
    ///     phase-checks each occupant so only erupting players actually take damage).
    /// </summary>
    public class EruptionSpawns : MonoBehaviour
    {
        private Transform content;
        private EmberfallWorldState world;
        private float nextServerCheck;

        private IEnumerator Start()
        {
            content = transform.Find("Content");
            if (content != null) content.gameObject.SetActive(false);

            while (EmberfallWorldState.Instance == null)
                yield return null;

            world = EmberfallWorldState.Instance;
            world.OnPhaseChanged += Apply;
            Apply(world.CurrentPhase);
        }

        private void OnDestroy()
        {
            if (world != null) world.OnPhaseChanged -= Apply;
        }

        // CLIENT presentation, driven by the LOCAL player's phase. On the server the collider is
        // driven by Update() (any player erupting) instead, so don't let this stomp it.
        private void Apply(EmberfallWorldState.Phase phase)
        {
            if (NetworkServer.active) return;
            if (content != null)
                content.gameObject.SetActive(phase == EmberfallWorldState.Phase.Erupting);
        }

        private void Update()
        {
            if (!NetworkServer.active) return;
            if (Time.time < nextServerCheck) return;
            nextServerCheck = Time.time + 0.5f;

            bool anyErupting = AnyPlayerErupting();
            if (content != null && content.gameObject.activeSelf != anyErupting)
                content.gameObject.SetActive(anyErupting);
        }

        private static bool AnyPlayerErupting()
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                var player = conn?.identity;
                var pwp = player != null ? player.GetComponent<PlayerWorldPhase>() : null;
                if (pwp != null && pwp.Phase == EmberfallWorldState.Phase.Erupting) return true;
            }
            return false;
        }
    }
}
