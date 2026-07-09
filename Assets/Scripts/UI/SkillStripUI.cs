using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// Always-visible compact skill bar (left edge): one mini-row per skill with
    /// glyph, level, and a thin XP progress bar. Rows flash on level-up.
    /// </summary>
    public class SkillStripUI : MonoBehaviour
    {
        [System.Serializable]
        public class Row
        {
            public SkillType skill;
            public TMP_Text levelText;
            public Image xpFill;
            public Image flash;
        }

        public Row[] rows;

        private Skills skills;
        private readonly float[] displayed = new float[Skills.SkillCount];

        private void Start()
        {
            ConfigureFillImages();
            TryBindSkills();
        }

        private void OnDestroy()
        {
            if (skills != null)
                skills.OnLevelUp.RemoveListener(OnLevelUp);
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
            var found = player != null ? player.GetComponent<Skills>() : null;
            if (found == null || found == skills) return;

            if (skills != null)
                skills.OnLevelUp.RemoveListener(OnLevelUp);
            skills = found;
            skills.OnLevelUp.AddListener(OnLevelUp);
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
                displayed[i] = Mathf.Lerp(displayed[i], target, 8f * Time.deltaTime);
                if (target < displayed[i] - 0.4f) displayed[i] = target; // level boundary snap

                if (row.xpFill != null) row.xpFill.fillAmount = displayed[i];
                if (row.levelText != null) row.levelText.text = skills.Level(row.skill).ToString();
            }
        }

        private void OnLevelUp(SkillType skill, int level)
        {
            foreach (var row in rows)
                if (row.skill == skill && row.flash != null)
                    StartCoroutine(Flash(row.flash));
        }

        private System.Collections.IEnumerator Flash(Image flash)
        {
            float elapsed = 0f;
            const float duration = 0.7f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (flash == null) yield break;
                var c = flash.color;
                c.a = Mathf.Lerp(0.65f, 0f, UIFx.EaseOut(elapsed / duration));
                flash.color = c;
                yield return null;
            }
        }
    }
}
