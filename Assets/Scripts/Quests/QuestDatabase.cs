using System.Collections.Generic;

namespace AldenFall.Quests
{
    /// <summary>
    /// Code-defined quest registry (mirrors ItemDatabase). Ships the Aldric
    /// tutorial line — a self-teaching chain (talk → use an ability → fish →
    /// eat) — plus a stub rescue quest for the next content pass.
    /// </summary>
    public static class QuestDatabase
    {
        private static Dictionary<string, QuestDef> quests;

        public static QuestDef Get(string id)
        {
            EnsureInitialized();
            if (id == null) return null;
            return quests.TryGetValue(id, out var q) ? q : null;
        }

        public static IEnumerable<QuestDef> All
        {
            get { EnsureInitialized(); return quests.Values; }
        }

        private static void EnsureInitialized()
        {
            if (quests != null) return;
            quests = new Dictionary<string, QuestDef>();

            // ---- Tutorial line, given by Aldric in the spawn clearing ----
            // q_intro is handed in AT Aldric (GiverNpcId set); the rest are
            // teaching beats that auto-complete and chain themselves (no giver).

            Register(new QuestDef
            {
                Id = "q_intro",
                Name = "A Word from Aldric",
                Description = "Aldric, the old warden of the clearing, wants to speak with you.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.TalkToNpc, TargetId = "aldric", Count = 1,
                        Description = "Speak with Aldric" },
                },
                GoldReward = 10,
                GiverNpcId = "aldric",
            });

            Register(new QuestDef
            {
                Id = "q_learn_skill",
                Name = "Find Your Footing",
                Description = "Aldric bids you test your arm. Use one of your abilities in the field.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.UseAbility, TargetId = "", Count = 1,
                        Description = "Use any ability (keys 1-5)" },
                },
                GoldReward = 15,
                PrereqQuestId = "q_intro",
            });

            Register(new QuestDef
            {
                Id = "q_learn_fish",
                Name = "A Fisher's Patience",
                Description = "Take the pole to the pond and land your first catch.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.CatchFish, TargetId = "", Count = 1,
                        Description = "Catch a fish at the pond" },
                },
                GoldReward = 15,
                PrereqQuestId = "q_learn_skill",
            });

            Register(new QuestDef
            {
                Id = "q_learn_eat",
                Name = "Cook, Then Eat",
                Description = "Cook your catch at the campfire and eat it to mend your wounds.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.EatFood, TargetId = "", Count = 1,
                        Description = "Eat any cooked food" },
                },
                GoldReward = 20,
                Rewards = { ("cooked_fish", 3) },
                PrereqQuestId = "q_learn_fish",
            });

            // ---- Stub for a later content pass (grukmar mob not yet placed) ----
            Register(new QuestDef
            {
                Id = "q_rescue",
                Name = "The Warden's Debt",
                Description = "A townsperson was dragged off by the brute Grukmar. Cut him down and bring word back.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.KillMob, TargetId = "grukmar", Count = 1,
                        Description = "Slay Grukmar" },
                    new ObjectiveDef { Kind = ObjectiveKind.TalkToNpc, TargetId = "townsperson", Count = 1,
                        Description = "Return to the freed townsperson" },
                },
                GoldReward = 50,
                GiverNpcId = "aldric",
                PrereqQuestId = "q_learn_eat",
            });
        }

        private static void Register(QuestDef def) => quests[def.Id] = def;
    }
}
