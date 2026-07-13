using UnityEngine;
using AldenFall.Combat;
using AldenFall.Quests;

namespace AldenFall.World
{
    /// <summary>
    /// A quest-giving NPC the player interacts with (press E). Resolves the
    /// CURRENT relevant quest for its npcId from the quest database, so one NPC
    /// can carry many quests across the spine: a ready-to-turn-in quest wins,
    /// then an acceptable one, then an active one. Speaks a runtime dialogue line
    /// via the combat-text bus. The prompt tracks the local player's quest state.
    /// </summary>
    public class QuestGiverNPC : Interactable
    {
        [SerializeField] private string npcId = "aldric";
        [SerializeField] private string questId = "";   // optional fallback; npcId resolution wins
        [SerializeField] private string displayName = "Aldric";

        public string NpcId { get => npcId; set => npcId = value; }
        public string QuestId { get => questId; set => questId = value; }
        public string NpcName { get => displayName; set => displayName = value; }

        private Quests.QuestLog cachedLog;
        private float nextPromptRefresh;

        protected override void OnEnable()
        {
            base.OnEnable();
            Prompt = $"Talk to {displayName}";
        }

        /// <summary>Pick this NPC's most relevant quest: turn-in > accept > active > fallback.</summary>
        private string ResolveQuest(Quests.QuestLog log, out bool turnInable, out bool acceptable)
        {
            string turnIn = null, accept = null, active = null;
            foreach (var q in QuestDatabase.All)
            {
                if (q.GiverNpcId != npcId) continue;
                if (log.IsComplete(q.Id)) { turnIn = q.Id; break; }
                if (accept == null && log.CanAccept(q.Id)) accept = q.Id;
                if (active == null && log.IsActive(q.Id)) active = q.Id;
            }
            // Fallback to the hand-set questId if npcId resolved nothing offerable.
            string chosen = turnIn ?? accept ?? active
                ?? (!string.IsNullOrEmpty(questId) && log.CanAccept(questId) ? questId : null);
            turnInable = chosen != null && log.IsComplete(chosen);
            acceptable = chosen != null && !turnInable && log.CanAccept(chosen);
            return chosen;
        }

        public override void Interact(GameObject player)
        {
            var log = player.GetComponent<Quests.QuestLog>();
            if (log == null) return;

            string qid = ResolveQuest(log, out bool turnInable, out bool acceptable);

            if (turnInable)
            {
                log.TurnIn(qid);
                Say($"{displayName}: Well done, adventurer.");
            }
            else if (acceptable)
            {
                log.Accept(qid);
                log.Advance(ObjectiveKind.TalkToNpc, npcId);   // talking IS the first beat
                Say($"{displayName}: I have a task for you.");
            }
            else
            {
                log.Advance(ObjectiveKind.TalkToNpc, npcId);   // still satisfies any "talk to me" step
                Say(qid != null
                    ? $"{displayName}: You're still busy, off you go."
                    : $"{displayName}: Safe travels.");
            }

            RefreshPrompt(log);
        }

        private void Update()
        {
            if (Time.time < nextPromptRefresh) return;
            nextPromptRefresh = Time.time + 0.5f;

            if (cachedLog == null)
            {
                var p = Multiplayer.LocalPlayerLocator.Find();
                cachedLog = p != null ? p.GetComponent<Quests.QuestLog>() : null;
            }
            if (cachedLog != null) RefreshPrompt(cachedLog);
        }

        private void RefreshPrompt(Quests.QuestLog log)
        {
            ResolveQuest(log, out bool turnInable, out _);
            Prompt = turnInable ? $"Turn in, {displayName}" : $"Talk to {displayName}";
        }

        private void Say(string line) =>
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f, line, UI.UITheme.TextGold);
    }
}
