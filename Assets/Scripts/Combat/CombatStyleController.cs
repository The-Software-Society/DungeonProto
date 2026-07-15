using UnityEngine;
using AldenFall.Progression;
using AldenFall.Stats;
using AldenFall.Items;
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

    /// <summary>Pure/Defensive stance for the ranged families (magic + archery).</summary>
    public enum RangedStance
    {
        Pure = 0,       // faster casts/shots, full damage
        Defensive = 1,  // slower, half damage, but gains half the melee Defence guard
    }

    /// <summary>
    /// The player's selected combat stance: you level only what you choose, and
    /// each stance carries a real mechanical trade-off applied as StatBlock
    /// modifiers under the "style" source.
    ///
    /// The stance axis is WEAPON-CATEGORY aware:
    ///   Melee (sword)      -> Attack / Strength / Defence
    ///       Attack   : faster swings (AttackSpeed up), trains Attack
    ///       Strength : harder hits (WeaponPower up),   trains Strength
    ///       Defence  : mitigate + dodge (Armor+Defence up), trains Defence
    ///   Magic / Archery    -> Pure / Defensive
    ///       Pure      : faster attacks, full damage, trains Magic/Ranged
    ///       Defensive : slower + half damage, but HALF the Defence guard
    ///                   (mitigation + dodge) and also trains Defence from hits.
    ///
    /// V cycles within the active category; UI buttons Set directly. Persists via
    /// PlayerPrefs. Modifiers are re-applied on any stance OR equipment change so
    /// swapping weapon swaps you onto that category's stance set cleanly.
    /// </summary>
    public class CombatStyleController : MonoBehaviour
    {
        private const string MeleePrefsKey = "combat_style_v1";
        private const string RangedPrefsKey = "ranged_stance_v1";
        private const string StyleSource = "style";

        [SerializeField] private KeyCode cycleKey = KeyCode.V;

        // Melee stance bonuses
        [SerializeField] private float attackSpeedBonus = 0.20f;   // Attack style: +20% swing rate
        [SerializeField] private float strengthWeaponPower = 3f;   // Strength style: flat WeaponPower (scaled by level)
        [SerializeField] private float defenceArmor = 6f;          // Defence style: flat mitigation
        [SerializeField] private float defenceDodge = 3f;          // Defence style: effective Defence for miss/dodge

        // Ranged/magic stance bonuses
        [SerializeField] private float pureSpeedBonus = 0.20f;     // Pure: +20% attack rate
        [SerializeField] private float defensiveSpeedPenalty = 0.35f; // Defensive: -35% attack rate
        [SerializeField] private float defensiveDamageMultiplier = 0.5f; // Defensive: half offensive output

        private CombatStyle meleeStyle = CombatStyle.Attack;
        private RangedStance rangedStance = RangedStance.Pure;

        private StatBlock stats;
        private Equipment equipment;

        /// <summary>Fires with the melee style int when the melee stance changes (UI compatibility).</summary>
        public event System.Action<CombatStyle> OnChanged;
        /// <summary>Fires whenever any stance or the active category changes.</summary>
        public event System.Action OnStanceChanged;

        public CombatStyle Style => meleeStyle;
        public RangedStance Stance => rangedStance;

        /// <summary>True when the equipped hands make this a ranged family (magic OR archery).</summary>
        public bool IsRangedCategory =>
            equipment != null && (equipment.HasMagicImplement || equipment.HasBow);

        /// <summary>
        /// The skill an AUTO trains under the current stance. Melee routes to the
        /// selected style; magic/archery autos grant their own skill (Magic via the
        /// bolt projectile, Ranged via the arrow) so this is only read by the melee
        /// path, but we answer sensibly for both.
        /// </summary>
        public SkillType TrainedSkill
        {
            get
            {
                if (IsRangedCategory)
                    return equipment != null && equipment.HasBow ? SkillType.Ranged : SkillType.Magic;
                return meleeStyle switch
                {
                    CombatStyle.Strength => SkillType.Strength,
                    CombatStyle.Defence => SkillType.Defence,
                    _ => SkillType.Attack,
                };
            }
        }

        /// <summary>Damage scale applied to autos: 1.0 except ranged/magic Defensive stance.</summary>
        public float OffenseMultiplier =>
            IsRangedCategory && rangedStance == RangedStance.Defensive ? defensiveDamageMultiplier : 1f;

        /// <summary>
        /// True when the active stance grants passive Defence training from taking
        /// hits: melee Defence style, or a ranged/magic Defensive stance.
        /// </summary>
        public bool TrainsDefenceFromHits =>
            (!IsRangedCategory && meleeStyle == CombatStyle.Defence) ||
            (IsRangedCategory && rangedStance == RangedStance.Defensive);

        private void Awake()
        {
            stats = GetComponent<StatBlock>();
            equipment = GetComponent<Equipment>();
            meleeStyle = (CombatStyle)PlayerPrefs.GetInt(MeleePrefsKey, (int)CombatStyle.Attack);
            rangedStance = (RangedStance)PlayerPrefs.GetInt(RangedPrefsKey, (int)RangedStance.Pure);
        }

        private void Start()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.AddListener(ApplyStanceModifiers);
            ApplyStanceModifiers();
        }

        private void OnDestroy()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.RemoveListener(ApplyStanceModifiers);
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(cycleKey))
                CycleActive();
        }

        /// <summary>V cycles within whichever category the equipped weapon selects.</summary>
        private void CycleActive()
        {
            if (IsRangedCategory)
                SetStance((RangedStance)(((int)rangedStance + 1) % 2));
            else
                Set((CombatStyle)(((int)meleeStyle + 1) % 3));
        }

        /// <summary>Set the melee style (Attack/Strength/Defence).</summary>
        public void Set(CombatStyle newStyle)
        {
            if (newStyle == meleeStyle) return;
            meleeStyle = newStyle;
            PlayerPrefs.SetInt(MeleePrefsKey, (int)meleeStyle);
            ApplyStanceModifiers();
            OnChanged?.Invoke(meleeStyle);
            OnStanceChanged?.Invoke();
        }

        /// <summary>Set the magic/archery stance (Pure/Defensive).</summary>
        public void SetStance(RangedStance newStance)
        {
            if (newStance == rangedStance) return;
            rangedStance = newStance;
            PlayerPrefs.SetInt(RangedPrefsKey, (int)rangedStance);
            ApplyStanceModifiers();
            OnStanceChanged?.Invoke();
        }

        /// <summary>
        /// Rewrites the "style" StatBlock modifier set for the active category and
        /// stance. Additive contributions ride the existing modifier machinery, so
        /// they stack with equipment and flow through CombatMath untouched.
        /// </summary>
        private void ApplyStanceModifiers()
        {
            if (stats == null) return;

            var mods = new System.Collections.Generic.List<(StatType, float)>();

            if (IsRangedCategory)
            {
                if (rangedStance == RangedStance.Pure)
                {
                    mods.Add((StatType.AttackSpeed, pureSpeedBonus));
                }
                else // Defensive: slower, half the melee Defence guard (dodge + mitigation)
                {
                    mods.Add((StatType.AttackSpeed, -defensiveSpeedPenalty));
                    mods.Add((StatType.Armor, defenceArmor * 0.5f));
                    mods.Add((StatType.Defence, defenceDodge * 0.5f));
                }
            }
            else
            {
                switch (meleeStyle)
                {
                    case CombatStyle.Attack:
                        mods.Add((StatType.AttackSpeed, attackSpeedBonus));
                        break;
                    case CombatStyle.Strength:
                        mods.Add((StatType.WeaponPower, strengthWeaponPower));
                        break;
                    case CombatStyle.Defence:
                        mods.Add((StatType.Armor, defenceArmor));
                        mods.Add((StatType.Defence, defenceDodge));
                        break;
                }
            }

            stats.ApplyModifiers(StyleSource, mods);
        }
    }
}
