using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using AldenFall.Multiplayer;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// XP gain feed (right edge) + level-up banner. XP is batched per skill over a
    /// short window so rapid hits read as one satisfying "+34 Attack" instead of spam.
    /// </summary>
    public class XpFeed : MonoBehaviour
    {
        public RectTransform feedRoot;      // entries stack here
        public RectTransform bannerRoot;    // level-up banner appears here

        private Skills skills;
        private readonly float[] pending = new float[Skills.SkillCount];
        private readonly float[] flushAt = new float[Skills.SkillCount];
        private const float BatchWindow = 0.6f;

        private void Start()
        {
            TryBindSkills();
        }

        private void OnDestroy()
        {
            if (skills == null) return;
            skills.OnXpGained.RemoveListener(OnXp);
            skills.OnLevelUp.RemoveListener(OnLevelUp);
        }

        private void TryBindSkills()
        {
            var player = LocalPlayerLocator.Find();
            var found = player != null ? player.GetComponent<Skills>() : null;
            if (found == null || found == skills) return;

            if (skills != null)
            {
                skills.OnXpGained.RemoveListener(OnXp);
                skills.OnLevelUp.RemoveListener(OnLevelUp);
            }

            skills = found;
            skills.OnXpGained.AddListener(OnXp);
            skills.OnLevelUp.AddListener(OnLevelUp);
        }

        private void OnXp(SkillType skill, float amount)
        {
            int i = (int)skill;
            if (pending[i] <= 0f) flushAt[i] = Time.time + BatchWindow;
            pending[i] += amount;
        }

        private void Update()
        {
            if (skills == null)
                TryBindSkills();

            for (int i = 0; i < Skills.SkillCount; i++)
            {
                if (pending[i] > 0f && Time.time >= flushAt[i])
                {
                    SpawnEntry((SkillType)i, pending[i]);
                    pending[i] = 0f;
                }
            }
        }

        private void SpawnEntry(SkillType skill, float amount)
        {
            if (feedRoot == null) return;

            var obj = new GameObject("XpEntry");
            obj.transform.SetParent(feedRoot, false);
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.text = $"+{Mathf.RoundToInt(amount)} {Skills.DisplayName(skill)}";
            text.fontSize = 17;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.MidlineRight;
            text.color = SkillColor(skill);
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color(0, 0, 0, 0.8f);
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(220, 24);

            StartCoroutine(AnimateEntry(text));
        }

        private IEnumerator AnimateEntry(TMP_Text text)
        {
            var rect = text.rectTransform;
            rect.anchoredPosition = new Vector2(30f, 0f);
            float duration = 1.6f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                if (text == null) yield break;
                rect.anchoredPosition = new Vector2(
                    Mathf.Lerp(30f, 0f, UIFx.EaseOut(Mathf.Clamp01(t * 4f))),
                    Mathf.Lerp(0f, 64f, t));
                text.alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                yield return null;
            }
            if (text != null) Destroy(text.gameObject);
        }

        private void OnLevelUp(SkillType skill, int newLevel)
        {
            if (bannerRoot == null) return;
            StartCoroutine(ShowBanner($"{Skills.DisplayName(skill)} Level {newLevel}!", SkillColor(skill)));
        }

        private IEnumerator ShowBanner(string message, Color color)
        {
            var obj = new GameObject("LevelUpBanner");
            obj.transform.SetParent(bannerRoot, false);
            var text = obj.AddComponent<TextMeshProUGUI>();
            text.text = message;
            text.fontSize = 42;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.outlineWidth = 0.25f;
            text.outlineColor = new Color(0, 0, 0, 0.9f);
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(700, 70);

            // Punch in, hold, fade up
            text.rectTransform.localScale = Vector3.one * 0.4f;
            yield return UIFx.ScaleTo(text.rectTransform, Vector3.one, 0.28f, back: true);
            yield return new WaitForSeconds(1.1f);

            float elapsed = 0f;
            while (elapsed < 0.5f)
            {
                elapsed += Time.deltaTime;
                if (text == null) yield break;
                text.alpha = 1f - elapsed / 0.5f;
                text.rectTransform.anchoredPosition += Vector2.up * (60f * Time.deltaTime);
                yield return null;
            }
            if (text != null) Destroy(text.gameObject);
        }

        // Skill accent color now lives in skills.json (Progression.SkillRegistry).
        public static Color SkillColor(SkillType skill) => SkillRegistry.Get(skill).Color;
    }
}
