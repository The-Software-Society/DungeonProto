using System.Collections.Generic;
using UnityEngine;

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
                        Description = "Speak with Aldric",
                        LocationHint = "Aldric waits in the forest glade where you woke, north of the city" },
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
                        Description = "Use any ability (keys 1-5)",
                        LocationHint = "Right here in the glade — press keys 1-5" },
                },
                GoldReward = 15,
                PrereqQuestId = "q_intro",
            });

            Register(new QuestDef
            {
                Id = "q_learn_fish",
                Name = "A Fisher's Patience",
                Description = "Take the pole to Emberfall Lake and land your first catch.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.CatchFish, TargetId = "", Count = 1,
                        Description = "Catch a fish at Emberfall Lake",
                        LocationHint = "The lake lies northwest of the city — fish from its east bank, or swim right in" },
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
                        Description = "Eat any cooked food",
                        LocationHint = "Cook your catch at the lakeside campfire, then eat it" },
                },
                GoldReward = 20,
                Rewards = { ("cooked_fish", 3) },
                PrereqQuestId = "q_learn_fish",
            });

            // ---- Rescue quest: given by a town NPC (the "Worried Mother"), whose
            // daughter Grukmar the warlord has dragged off to his forest warcamp.
            Register(new QuestDef
            {
                Id = "q_rescue",
                Name = "A Mother's Plea",
                Description = "Grukmar the Warlord has stolen a girl away to his forest warcamp. Cut him down, free her, and bring word back to her mother in town.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.KillMob, TargetId = "grukmar", Count = 1,
                        Description = "Slay Grukmar the Warlord",
                        LocationHint = "Grukmar's warcamp lies in the southwest woods, beyond the city" },
                    new ObjectiveDef { Kind = ObjectiveKind.TalkToNpc, TargetId = "townsperson", Count = 1,
                        Description = "Return to the girl's mother in town",
                        LocationHint = "Her mother waits near the fountain in the city square" },
                },
                GoldReward = 150,
                Rewards = { ("knight_sword", 1) },
                GiverNpcId = "townsperson",
                PrereqQuestId = "q_learn_eat",
                StartCutscene = "q_rescue_start",
                CompleteCutscene = "q_rescue_complete",
            });

            // ---- MAIN-QUEST SPINE to the Floor Boss (giver-less: each auto-accepts
            // when its prereq is turned in, so the chain keeps its own momentum) ----

            Register(new QuestDef
            {
                Id = "q_clear_roads",
                Name = "Clear the Forest Roads",
                Description = "With the girl home safe, the town asks more: goblins prowl the western woods and bandits the east. Thin them out so the roads breathe again.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.KillMob, TargetId = "goblin_scrapper", Count = 4,
                        Description = "Cull goblin scrappers",
                        LocationHint = "The goblin camp squats in the western woods" },
                    new ObjectiveDef { Kind = ObjectiveKind.KillMob, TargetId = "field_bandit", Count = 4,
                        Description = "Drive off bandits",
                        LocationHint = "The bandit camp hides in the eastern woods" },
                },
                GoldReward = 120,
                Rewards = { ("iron_helm", 1) },
                PrereqQuestId = "q_rescue",
            });

            Register(new QuestDef
            {
                Id = "q_ember_scout",
                Name = "Smoke on the Horizon",
                Description = "Ash drifts thicker every day, and the southern sky glows after dark. Walk the Ember Approach and see with your own eyes what the mountain is becoming.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.ReachLocation, TargetId = "", Count = 1,
                        Description = "Scout the Ember Approach",
                        LocationHint = "Follow the charred road south of the city until the ember-rain thickens",
                        Location = new Vector3(0f, 0f, -230f), Radius = 30f },
                },
                GoldReward = 60,
                PrereqQuestId = "q_clear_roads",
            });

            Register(new QuestDef
            {
                Id = "q_floor_boss",
                Name = "The Heart of the Mountain",
                Description = "The volcano is waking, and Emberfall stands in the path of its fire. Descend into the caldera, fight through its halls, and slay the beast at its heart before the city burns.",
                Objectives =
                {
                    new ObjectiveDef { Kind = ObjectiveKind.KillMob, TargetId = "cave_troll_boss", Count = 1,
                        Description = "Slay the beast of the caldera",
                        LocationHint = "The volcano's mouth opens at the far south end of the Ember Approach" },
                },
                GoldReward = 400,
                Rewards = { ("ember_focus", 1) },
                PrereqQuestId = "q_ember_scout",
            });
        }

        private static void Register(QuestDef def) => quests[def.Id] = def;
    }
}
