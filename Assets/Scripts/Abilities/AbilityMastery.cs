using System;
using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.Abilities
{
    /// <summary>
    /// Conquer-style per-ability mastery: the more you use a skill, the more
    /// powerful it gets. Rank 1-5 at use-count thresholds; each rank past the
    /// first adds +8% damage. Persisted in PlayerPrefs so it survives sessions.
    /// </summary>
    public class AbilityMastery : MonoBehaviour
    {
        private const string PrefsKey = "mastery_v1";
        public const int MaxRank = 5;
        private static readonly int[] RankThresholds = { 0, 5, 15, 35, 70 };

        private readonly Dictionary<string, int> uses = new Dictionary<string, int>();

        /// <summary>ability id, new rank</summary>
        public event Action<string, int> OnRankUp;

        private void Awake() => Load();

        public int UsesOf(string abilityId) =>
            uses.TryGetValue(abilityId, out var n) ? n : 0;

        public int RankFor(string abilityId)
        {
            int n = UsesOf(abilityId);
            int rank = 1;
            for (int i = 1; i < RankThresholds.Length; i++)
                if (n >= RankThresholds[i]) rank = i + 1;
            return rank;
        }

        public float DamageMultiplier(string abilityId) =>
            1f + 0.08f * (RankFor(abilityId) - 1);

        /// <summary>Progress toward the next rank, 0..1 (1 at max rank).</summary>
        public float MasteryProgress01(string abilityId)
        {
            int rank = RankFor(abilityId);
            if (rank >= MaxRank) return 1f;
            int current = RankThresholds[rank - 1];
            int next = RankThresholds[rank];
            return Mathf.InverseLerp(current, next, UsesOf(abilityId));
        }

        public void AddUse(string abilityId)
        {
            int before = RankFor(abilityId);
            uses[abilityId] = UsesOf(abilityId) + 1;
            int after = RankFor(abilityId);
            Save();
            if (after > before)
                OnRankUp?.Invoke(abilityId, after);
        }

        public void ResetAll()
        {
            uses.Clear();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        // ---------- persistence (JsonUtility needs parallel arrays) ----------

        [Serializable]
        private class SaveData
        {
            public List<string> ids = new List<string>();
            public List<int> counts = new List<int>();
        }

        private void Save()
        {
            var data = new SaveData();
            foreach (var kv in uses)
            {
                data.ids.Add(kv.Key);
                data.counts.Add(kv.Value);
            }
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
        }

        private void Load()
        {
            uses.Clear();
            string json = PlayerPrefs.GetString(PrefsKey, null);
            if (string.IsNullOrEmpty(json)) return;
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null) return;
            for (int i = 0; i < data.ids.Count && i < data.counts.Count; i++)
                uses[data.ids[i]] = data.counts[i];
        }
    }
}
