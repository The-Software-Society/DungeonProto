using UnityEngine;
using AldenFall.AI;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// The kidnapped daughter cowering in Grukmar's warcamp. She stays a captive
    /// until the warlord (a mob whose <see cref="MobIdentity.MobId"/> matches
    /// <see cref="warlordMobId"/>) is dead, then plays a simple "freed" beat:
    /// status text + her marker light warms from fearful red to safe gold.
    ///
    /// Non-networked and self-contained: Grukmar is a replicated enemy, so every
    /// machine sees his death and frees her locally. The q_rescue quest tracks
    /// the actual KillMob credit separately (MobIdentity → QuestLog).
    /// </summary>
    public class RescueCaptive : MonoBehaviour
    {
        [SerializeField] private string warlordMobId = "grukmar";
        [SerializeField] private Light markerLight;

        private bool freed;
        private bool hasSeenWarlord;
        private float nextCheck;

        public void Configure(string mobId, Light light)
        {
            warlordMobId = mobId;
            markerLight = light;
        }

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

            // Only free her after the warlord has actually existed — avoids a
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

            if (markerLight != null)
            {
                markerLight.color = UI.UITheme.TextGold;
                markerLight.intensity = Mathf.Max(markerLight.intensity, 1.6f);
            }

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f,
                "You saved me! Please — take word back to the village.", UI.UITheme.TextGold);
        }
    }
}
