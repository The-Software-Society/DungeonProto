using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AldenFall.Combat;
using AldenFall.Items;

namespace AldenFall.Quests
{
    public enum QuestStatus { NotStarted, Active, Complete, TurnedIn }

    /// <summary>Runtime tracking for one quest on one player.</summary>
    public class QuestState
    {
        public QuestStatus Status = QuestStatus.NotStarted;
        public int[] Progress;   // per-objective counter, sized to the def
    }

    /// <summary>
    /// Per-player quest tracker. Lives on the player alongside Inventory/Wallet
    /// and the skilling components, whose events it subscribes to so gameplay
    /// (using an ability, catching a fish, eating) advances active objectives
    /// automatically. Kill credit is pushed in from MobIdentity (server-side).
    /// </summary>
    public class QuestLog : MonoBehaviour
    {
        public UnityEvent OnQuestChanged = new UnityEvent();

        private readonly Dictionary<string, QuestState> states = new Dictionary<string, QuestState>();

        private Inventory inventory;
        private Wallet wallet;

        public IReadOnlyDictionary<string, QuestState> States => states;

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            wallet = GetComponent<Wallet>();
        }

        private void Start()
        {
            // Objective auto-hooks. QuestLog is on the player, so these are siblings.
            var abilities = GetComponent<Abilities.PlayerAbilities>();
            if (abilities != null) abilities.OnAbilityUsed += id => Advance(ObjectiveKind.UseAbility, id);

            var consumables = GetComponent<PlayerConsumables>();
            if (consumables != null) consumables.OnAte += id => Advance(ObjectiveKind.EatFood, id);

            var fishing = GetComponent<Player.FishingController>();
            if (fishing != null) fishing.OnFishCaught += id => Advance(ObjectiveKind.CatchFish, id);

            var cooking = GetComponent<Player.CookingController>();
            if (cooking != null) cooking.OnCooked += id => Advance(ObjectiveKind.CookFood, id);
        }

        // ReachLocation objectives are position-driven: poll the player's spot a few
        // times a second (declared in the enum since v1, but nothing drove it until
        // the main-quest spine needed "walk the Ember Approach").
        private float nextLocationCheck;

        private void Update()
        {
            if (Time.time < nextLocationCheck) return;
            nextLocationCheck = Time.time + 0.75f;

            bool changed = false;
            foreach (var kv in states)
            {
                if (kv.Value.Status != QuestStatus.Active) continue;
                var def = QuestDatabase.Get(kv.Key);
                if (def == null) continue;

                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var obj = def.Objectives[i];
                    if (obj.Kind != ObjectiveKind.ReachLocation) continue;
                    if (kv.Value.Progress[i] >= obj.Count) continue;
                    if (Vector3.Distance(transform.position, obj.Location) > Mathf.Max(1f, obj.Radius)) continue;

                    kv.Value.Progress[i] = obj.Count;
                    changed = true;
                }
            }

            if (!changed) return;
            OnQuestChanged?.Invoke();
            CheckAllCompletion();
        }

        // ==================== QUERIES ====================

        public QuestState StateOf(string id) => states.TryGetValue(id, out var s) ? s : null;
        public QuestStatus StatusOf(string id) => StateOf(id)?.Status ?? QuestStatus.NotStarted;

        public bool IsActive(string id) => StatusOf(id) == QuestStatus.Active;
        public bool IsComplete(string id) => StatusOf(id) == QuestStatus.Complete;
        public bool IsTurnedIn(string id) => StatusOf(id) == QuestStatus.TurnedIn;

        public bool CanAccept(string id)
        {
            var def = QuestDatabase.Get(id);
            if (def == null || StatusOf(id) != QuestStatus.NotStarted) return false;
            if (!string.IsNullOrEmpty(def.PrereqQuestId) &&
                StatusOf(def.PrereqQuestId) != QuestStatus.TurnedIn) return false;
            return true;
        }

        /// <summary>Current progress of objective i (for UI), 0 if unknown.</summary>
        public int ProgressOf(string id, int objectiveIndex)
        {
            var st = StateOf(id);
            return st != null && st.Progress != null && objectiveIndex < st.Progress.Length
                ? st.Progress[objectiveIndex] : 0;
        }

        // ==================== MUTATIONS ====================

        public bool Accept(string id)
        {
            if (!CanAccept(id)) return false;
            var def = QuestDatabase.Get(id);
            states[id] = new QuestState
            {
                Status = QuestStatus.Active,
                Progress = new int[def.Objectives.Count],
            };
            PlayCutscene(def.StartCutscene);
            OnQuestChanged?.Invoke();
            CheckCompletion(id);   // handles a quest whose objectives are already satisfied
            return true;
        }

        /// <summary>Increment every active objective matching (kind, targetId). An empty
        /// objective TargetId matches any target of that kind.</summary>
        public void Advance(ObjectiveKind kind, string targetId, int amount = 1)
        {
            if (amount <= 0) return;
            bool changed = false;

            // Only ints on states are touched here (no dict mutation), so a plain
            // enumeration is safe; completion (which may add follow-ups) runs after.
            foreach (var kv in states)
            {
                if (kv.Value.Status != QuestStatus.Active) continue;
                var def = QuestDatabase.Get(kv.Key);
                if (def == null) continue;

                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var obj = def.Objectives[i];
                    if (obj.Kind != kind) continue;
                    if (!string.IsNullOrEmpty(obj.TargetId) && obj.TargetId != targetId) continue;
                    if (kv.Value.Progress[i] >= obj.Count) continue;

                    kv.Value.Progress[i] = Mathf.Min(obj.Count, kv.Value.Progress[i] + amount);
                    changed = true;
                }
            }

            if (!changed) return;
            OnQuestChanged?.Invoke();
            CheckAllCompletion();
        }

        /// <summary>Grants rewards, marks TurnedIn, and auto-accepts a chained follow-up.</summary>
        public bool TurnIn(string id)
        {
            var st = StateOf(id);
            if (st == null || st.Status != QuestStatus.Complete) return false;
            var def = QuestDatabase.Get(id);

            st.Status = QuestStatus.TurnedIn;

            if (def.Rewards != null)
                foreach (var reward in def.Rewards)
                    for (int c = 0; c < Mathf.Max(1, reward.count); c++)
                        LootDrop.GiveOrDrop(inventory, reward.itemId, transform);  // never silently lost

            if (def.GoldReward > 0)
                wallet?.Add(def.GoldReward);

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.4f,
                def.GoldReward > 0 ? $"Quest complete: {def.Name}  +{def.GoldReward}g"
                                   : $"Quest complete: {def.Name}",
                UI.UITheme.TextGold);

            OnQuestChanged?.Invoke();

            // Chain the next quest. NPC-handed quests wait for the player to visit
            // their giver; giver-less chain quests auto-accept here.
            foreach (var next in QuestDatabase.All)
            {
                if (next.PrereqQuestId != id) continue;
                if (!string.IsNullOrEmpty(next.GiverNpcId)) continue;
                if (CanAccept(next.Id)) { Accept(next.Id); break; }
            }
            return true;
        }

        // ==================== COMPLETION ====================

        private void CheckAllCompletion()
        {
            // Snapshot: completion may auto-turn-in and add follow-up quests.
            var ids = new List<string>(states.Keys);
            foreach (var id in ids) CheckCompletion(id);
        }

        private void CheckCompletion(string id)
        {
            var st = StateOf(id);
            if (st == null || st.Status != QuestStatus.Active) return;
            var def = QuestDatabase.Get(id);
            if (def == null) return;

            for (int i = 0; i < def.Objectives.Count; i++)
                if (st.Progress[i] < def.Objectives[i].Count) return;   // still in progress

            st.Status = QuestStatus.Complete;
            PlayCutscene(def.CompleteCutscene);
            OnQuestChanged?.Invoke();

            // Teaching beats (no NPC hand-in) reward + chain immediately.
            if (string.IsNullOrEmpty(def.GiverNpcId))
                TurnIn(id);
        }

        private static void PlayCutscene(string cutsceneId)
        {
            if (string.IsNullOrEmpty(cutsceneId)) return;
            Cinematics.CutsceneDirector.Instance?.Play(cutsceneId);
        }
    }
}
