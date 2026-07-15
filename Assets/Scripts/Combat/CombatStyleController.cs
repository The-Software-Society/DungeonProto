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

    /// <summary>The weapon family the equipped hands select, for stance bookkeeping.</summary>
    public enum CombatCategory
    {
        Melee = 0,
        Magic = 1,
        Archery = 2,
    }

    /// <summary>
    /// The player's selected combat stance, remembered INDEPENDENTLY per weapon
    /// family. Three stances live at once and never bleed into each other:
    ///   Melee   -> Attack / Strength / Defence   (pref combat_style_v1)
    ///   Magic   -> Pure / Defensive              (pref magic_stance_v1,   default Pure)
    ///   Archery -> Pure / Defensive              (pref archery_stance_v1, default Pure)
    /// The ACTIVE stance is whichever family the equipped weapon selects. Swapping
    /// weapon restores that family's last stance (e.g. Strength melee + Defensive
    /// archery + Pure magic all persist at once), so switching never rewrites another
    /// category. V cycles within the active family; UI buttons set directly. Each
    /// stance carries a real trade-off applied as StatBlock modifiers ("style").
    /// </summary>
    public class CombatStyleController : MonoBehaviour
    {
        private const string MeleePrefsKey = "combat_style_v1";
        private const string MagicPrefsKey = "magic_stance_v1";
        private const string ArcheryPrefsKey = "archery_stance_v1";
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
        private RangedStance magicStance = RangedStance.Pure;
        private RangedStance archeryStance = RangedStance.Pure;

        private StatBlock stats;
        private Equipment equipment;

        /// <summary>Fires with the melee style int when the melee stance changes (UI compatibility).</summary>
        public event System.Action<CombatStyle> OnChanged;
        /// <summary>Fires whenever any stance or the active category changes.</summary>
        public event System.Action OnStanceChanged;

        public CombatStyle Style => meleeStyle;

        /// <summary>The active ranged family's stance (magic vs archery tracked separately).</summary>
        public RangedStance Stance =>
            ActiveCategory == CombatCategory.Archery ? archeryStance : magicStance;

        /// <summary>The weapon family the equipped hands currently select (fists count as melee).</summary>
        public CombatCategory ActiveCategory
        {
            get
            {
                if (equipment != null && equipment.HasBow) return CombatCategory.Archery;
                if (equipment != null && equipment.HasMagicImplement) return CombatCategory.Magic;
                return CombatCategory.Melee;
            }
        }

        /// <summary>True when the equipped hands make this a ranged family (magic OR archery).</summary>
        public bool IsRangedCategory => ActiveCategory != CombatCategory.Melee;

        /// <summary>
        /// The skill an AUTO trains under the current stance. Melee routes to the
        /// selected style; magic/archery autos grant their own skill (Magic via the
        /// bolt projectile, Ranged via the arrow) so this is only read by the melee
        /// path, but we answer sensibly for all three.
        /// </summary>
        public SkillType TrainedSkill => ActiveCategory switch
        {
            CombatCategory.Archery => SkillType.Ranged,
            CombatCategory.Magic => SkillType.Magic,
            _ => meleeStyle switch
            {
                CombatStyle.Strength => SkillType.Strength,
                CombatStyle.Defence => SkillType.Defence,
                _ => SkillType.Attack,
            },
        };

        /// <summary>Damage scale applied to autos: 1.0 except a ranged/magic Defensive stance.</summary>
        public float OffenseMultiplier =>
            IsRangedCategory && Stance == RangedStance.Defensive ? defensiveDamageMultiplier : 1f;

        /// <summary>
        /// True when the active stance grants passive Defence training from taking
        /// hits: melee Defence style, or a ranged/magic Defensive stance.
        /// </summary>
        public bool TrainsDefenceFromHits =>
            (ActiveCategory == CombatCategory.Melee && meleeStyle == CombatStyle.Defence) ||
            (IsRangedCategory && Stance == RangedStance.Defensive);

        private void Awake()
        {
            stats = GetComponent<StatBlock>();
            equipment = GetComponent<Equipment>();
            meleeStyle = (CombatStyle)PlayerPrefs.GetInt(MeleePrefsKey, (int)CombatStyle.Attack);
            magicStance = (RangedStance)PlayerPrefs.GetInt(MagicPrefsKey, (int)RangedStance.Pure);
            archeryStance = (RangedStance)PlayerPrefs.GetInt(ArcheryPrefsKey, (int)RangedStance.Pure);
        }

        private void Start()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.AddListener(OnEquipmentChanged);
            ApplyStanceModifiers();
        }

        private void OnDestroy()
        {
            if (equipment != null)
                equipment.OnEquipmentChanged.RemoveListener(OnEquipmentChanged);
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(cycleKey))
                CycleActive();
        }

        // Weapon swap: reapply the newly-active family's stance modifiers and let the
        // UI repaint to that family's remembered stance.
        private void OnEquipmentChanged()
        {
            ApplyStanceModifiers();
            OnStanceChanged?.Invoke();
        }

        /// <summary>V cycles within whichever family the equipped weapon selects.</summary>
        private void CycleActive()
        {
            if (IsRangedCategory)
                SetStance((RangedStance)(((int)Stance + 1) % 2));
            else
                Set((CombatStyle)(((int)meleeStyle + 1) % 3));
        }

        /// <summary>Set the melee style (Attack/Strength/Defence). Only touches the melee family.</summary>
        public void Set(CombatStyle newStyle)
        {
            if (newStyle == meleeStyle) return;
            meleeStyle = newStyle;
            PlayerPrefs.SetInt(MeleePrefsKey, (int)meleeStyle);
            if (ActiveCategory == CombatCategory.Melee) ApplyStanceModifiers();
            OnChanged?.Invoke(meleeStyle);
            OnStanceChanged?.Invoke();
        }

        /// <summary>
        /// Set the active ranged family's stance (Pure/Defensive). Magic and archery
        /// remember separately, so this writes only the currently-equipped family and
        /// never disturbs the other.
        /// </summary>
        public void SetStance(RangedStance newStance)
        {
            var cat = ActiveCategory;
            if (cat == CombatCategory.Archery)
            {
                if (newStance == archeryStance) return;
                archeryStance = newStance;
                PlayerPrefs.SetInt(ArcheryPrefsKey, (int)archeryStance);
            }
            else if (cat == CombatCategory.Magic)
            {
                if (newStance == magicStance) return;
                magicStance = newStance;
                PlayerPrefs.SetInt(MagicPrefsKey, (int)magicStance);
            }
            else
            {
                return; // no ranged weapon equipped: nothing to set
            }
            ApplyStanceModifiers();
            OnStanceChanged?.Invoke();
        }

        /// <summary>
        /// Rewrites the "style" StatBlock modifier set for the active family and its
        /// remembered stance. Additive contributions ride the existing modifier
        /// machinery, so they stack with equipment and flow through CombatMath.
        /// </summary>
        private void ApplyStanceModifiers()
        {
            if (stats == null) return;

            var mods = new System.Collections.Generic.List<(StatType, float)>();

            if (IsRangedCategory)
            {
                if (Stance == RangedStance.Pure)
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
