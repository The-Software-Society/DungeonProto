using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// Skills panel (toggle: K): one row per skill with level, animated XP bar,
    /// and xp-to-next readout.
    /// </summary>
    public class SkillsPanelUI : MonoBehaviour
    {
        [System.Serializable]
        public class SkillRow
        {
            public SkillType skill;
            public TMP_Text levelText;
            public TMP_Text xpText;
            public Image xpFill;
        }

        public SkillRow[] rows;

        private Skills skills;
        private readonly float[] displayed = new float[Skills.SkillCount];

        private void Start()
        {
            ConfigureFillImages();
            TryBindSkills();
        }

        private void ConfigureFillImages()
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row.xpFill == null) continue;
                row.xpFill.type = Image.Type.Filled;
                row.xpFill.fillMethod = Image.FillMethod.Horizontal;
                row.xpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                row.xpFill.fillAmount = 0f;
            }
        }

        private void TryBindSkills()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            skills = player != null ? player.GetComponent<Skills>() : null;
        }

        private void Update()
        {
            if (skills == null)
                TryBindSkills();
            if (skills == null || rows == null) return;

            foreach (var row in rows)
            {
                int i = (int)row.skill;
                float target = skills.Progress01(row.skill);
                displayed[i] = Mathf.Lerp(displayed[i], target,
                    10f * Time.unscaledDeltaTime);
                // Snap across level boundaries so the bar doesn't rewind slowly
                if (target < displayed[i] - 0.4f) displayed[i] = target;

                if (row.xpFill != null) row.xpFill.fillAmount = displayed[i];
                if (row.levelText != null) row.levelText.text = skills.Level(row.skill).ToString();
                if (row.xpText != null)
                {
                    float toNext = skills.XpToNextLevel(row.skill);
                    row.xpText.text = toNext > 0
                        ? $"{Mathf.CeilToInt(toNext):N0} xp to next"
                        : "MAX";
                }
            }
        }
    }
}
