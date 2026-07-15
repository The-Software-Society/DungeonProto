using UnityEngine;
using AldenFall.Progression;

namespace AldenFall.Combat
{
    /// <summary>
    /// Trains Defence from taking hits, but ONLY while the Defence combat
    /// style is selected (you level what you choose). Actual mitigation happens
    /// universally in Health via the StatBlock (Defence + Armor), this
    /// component only handles the XP side.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerDefence : MonoBehaviour
    {
        private Skills skills;
        private CombatStyleController combatStyle;

        private void Awake()
        {
            skills = GetComponent<Skills>();
            combatStyle = GetComponent<CombatStyleController>();
            GetComponent<Health>().OnRawDamaged.AddListener(OnRawDamage);
        }

        private void OnRawDamage(float rawDamage)
        {
            // Defence trains from taking hits only while a guarding stance is active:
            // the melee Defence style, or a magic/archery Defensive stance.
            if (combatStyle != null && !combatStyle.TrainsDefenceFromHits) return;
            skills?.AddXp(SkillType.Defence, rawDamage * 1.1f);
        }
    }
}
