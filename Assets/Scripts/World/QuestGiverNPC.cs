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

            // Talking always satisfies a "talk to me" objective for whatever is active.
            log.Advance(ObjectiveKind.TalkToNpc, npcId);

            // Open the quest WINDOW so the player reads the story, objectives, and rewards and
            // ACCEPTS / TURNS IN there (v9 feedback: acceptance was too minimal). Falls back to
            // a spoken line if there is no quest to show or the panel is missing.
            var detail = player.GetComponent<UI.QuestDetailPanel>();
            if (qid != null && detail != null)
            {
                detail.OpenFor(qid);
                Say(turnInable ? $"{displayName}: Let's see what you've done."
                    : acceptable ? $"{displayName}: I have a task for you."
                    : $"{displayName}: How goes the task?");
            }
            else
            {
                Say($"{displayName}: Safe travels.");
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
