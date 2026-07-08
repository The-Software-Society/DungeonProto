using UnityEngine;
using AldenFall.Progression;
using AldenFall.UI;

namespace AldenFall.Combat
{
    /// <summary>Which combat skill melee autos train (OSRS attack styles).</summary>
    public enum CombatStyle
    {
        Attack = 0,
        Strength = 1,
        Defence = 2,
    }

    /// <summary>
    /// The player's selected combat style: you level only what you choose.
    /// Melee autos train the selected skill; passive Defence XP from taking
    /// hits only flows while the Defence style is active. Magic still levels
    /// purely by casting. Persists via PlayerPrefs; V cycles, UI buttons set.
    /// </summary>
    public class CombatStyleController : MonoBehaviour
    {
        private const string PrefsKey = "combat_style_v1";

        [SerializeField] private KeyCode cycleKey = KeyCode.V;

        private CombatStyle style = CombatStyle.Attack;

        public event System.Action<CombatStyle> OnChanged;

        public CombatStyle Style => style;

        /// <summary>The skill a melee auto trains under the current style.</summary>
        public SkillType TrainedSkill => style switch
        {
            CombatStyle.Strength => SkillType.Strength,
            CombatStyle.Defence => SkillType.Defence,
            _ => SkillType.Attack,
        };

        private void Awake()
        {
            style = (CombatStyle)PlayerPrefs.GetInt(PrefsKey, (int)CombatStyle.Attack);
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(cycleKey))
                Set((CombatStyle)(((int)style + 1) % 3));
        }

        public void Set(CombatStyle newStyle)
        {
            if (newStyle == style) return;
            style = newStyle;
            PlayerPrefs.SetInt(PrefsKey, (int)style);
            OnChanged?.Invoke(style);
        }
    }
}
