using UnityEngine;
using AldenFall.Progression;

namespace AldenFall.Combat
{
    /// <summary>
    /// DEPRECATED as of the v9 Hitpoints rework. Hitpoints (SkillType.Health, displayed
    /// "Hitpoints") now trains OSRS-style from DEALING combat damage - a fraction of every
    /// combat XP grant, in PlayerCombat.ApplyCombatXp (config: hitpointsXpFraction). This
    /// component no longer trains on damage TAKEN. Kept as a harmless no-op so the runtime
    /// EnsureGameplayComponents wiring and any serialized references stay valid.
    /// </summary>
    public class HealthSkill : MonoBehaviour
    {
        // Intentionally empty: Hitpoints XP is minted in PlayerCombat.ApplyCombatXp.
    }
}
