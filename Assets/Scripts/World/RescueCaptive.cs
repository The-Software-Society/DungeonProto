using System.Collections.Generic;
using UnityEngine;
using AldenFall.AI;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// The kidnapped daughter locked in Grukmar's warcamp cage. The cage stays
    /// physically LOCKED (bar colliders + renderers on) until the warlord dies,
    /// then the bars drop away, her marker light warms from fearful red to safe
    /// gold, and she thanks the player.
    ///
    /// Freedom is driven by MobIdentity.OnAnyMobKilled on the server/host plus a
    /// replicated-state poll fallback for pure clients (Grukmar is a replicated
    /// enemy, so every machine observes his death). The q_rescue quest tracks the
    /// actual KillMob credit separately (MobIdentity to QuestLog).
    /// </summary>
    public class RescueCaptive : MonoBehaviour
    {
        [SerializeField] private string warlordMobId = "grukmar";
        [SerializeField] private Light markerLight;
        [SerializeField] private List<GameObject> cageBars = new List<GameObject>();

        private bool freed;
        private bool hasSeenWarlord;
        private float nextCheck;

        public void Configure(string mobId, Light light)
        {
            warlordMobId = mobId;
            markerLight = light;
        }

        /// <summary>World-gen hands over the cage bar objects so freeing can open them.</summary>
        public void ConfigureCage(IEnumerable<GameObject> bars)
        {
            cageBars.Clear();
            if (bars != null) cageBars.AddRange(bars);
        }

        private void OnEnable() => MobIdentity.OnAnyMobKilled += OnMobKilled;
        private void OnDisable() => MobIdentity.OnAnyMobKilled -= OnMobKilled;

        // Server/host: instant, no polling race, works even if the warlord died
        // before this component ever saw him alive.
        private void OnMobKilled(string mobId, GameObject mob)
        {
            if (!freed && mobId == warlordMobId) Free();
        }

        // Pure clients never receive the server-side kill event; they watch the
        // replicated mob state instead.
        private void Update()
        {
            if (freed) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;

            if (WarlordAlive())
            {
                hasSeenWarlord = true;
                return;
            }

            // Only free her after the warlord has actually existed, avoids a
            // false "freed" on the frame before he spawns.
            if (hasSeenWarlord) Free();
        }

        private bool WarlordAlive()
        {
            var mobs = Object.FindObjectsByType<MobIdentity>(FindObjectsSortMode.None);
            foreach (var mob in mobs)
            {
                if (mob == null || mob.MobId != warlordMobId) continue;
                var health = mob.GetComponent<Health>();
                if (health == null || health.IsAlive) return true;
            }
            return false;
        }

        private void Free()
        {
            freed = true;

            // The bars actually open: colliders and renderers off, so she is
            // visibly and physically free.
            foreach (var bar in cageBars)
            {
                if (bar == null) continue;
                foreach (var col in bar.GetComponentsInChildren<Collider>())
                    col.enabled = false;
                foreach (var rend in bar.GetComponentsInChildren<Renderer>())
                    rend.enabled = false;
            }

            if (markerLight != null)
            {
                markerLight.color = UI.UITheme.TextGold;
                markerLight.intensity = Mathf.Max(markerLight.intensity, 1.6f);
            }

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f,
                "You saved me! Please, take word back to the village.", UI.UITheme.TextGold);
        }
    }
}
