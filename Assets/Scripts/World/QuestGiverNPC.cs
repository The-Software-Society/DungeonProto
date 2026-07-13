using UnityEngine;
using AldenFall.Combat;
using AldenFall.Quests;

namespace AldenFall.World
{
    /// <summary>
    /// A quest-giving NPC the player interacts with (press E). Offers its quest
    /// when available, turns it in when its objectives are done, and speaks a
    /// runtime dialogue line via the combat-text bus. The prompt tracks the
    /// local player's quest state so it reads "Talk to Aldric" / "Turn in".
    /// </summary>
    public class QuestGiverNPC : Interactable
    {
        [SerializeField] private string npcId = "aldric";
        [SerializeField] private string questId = "q_intro";
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

        public override void Interact(GameObject player)
        {
            var log = player.GetComponent<Quests.QuestLog>();
            if (log == null) return;

            if (log.IsComplete(questId))
            {
                log.TurnIn(questId);
                Say($"{displayName}: Well done, adventurer.");
            }
            else if (log.CanAccept(questId))
            {
                log.Accept(questId);
                log.Advance(ObjectiveKind.TalkToNpc, npcId);   // talking IS the first beat
                Say($"{displayName}: I have a task for you.");
            }
            else
            {
                log.Advance(ObjectiveKind.TalkToNpc, npcId);   // still satisfies any "talk to me" step
                Say(log.IsActive(questId)
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
            Prompt = log.IsComplete(questId) ? $"Turn in, {displayName}" : $"Talk to {displayName}";
        }

        private void Say(string line) =>
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f, line, UI.UITheme.TextGold);
    }
}
