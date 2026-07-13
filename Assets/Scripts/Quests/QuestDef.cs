using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.Quests
{
    /// <summary>What a single quest step tracks. TargetId meaning depends on Kind
    /// (mob id / item id / npc id / ability id); an empty TargetId matches ANY
    /// (e.g. "use any ability", "eat any food").</summary>
    public enum ObjectiveKind
    {
        KillMob,
        CollectItem,
        ReachLocation,
        TalkToNpc,
        UseAbility,
        CatchFish,
        EatFood,
        CookFood,
        InspectMarker,   // press E on a world marker with a matching id (seal stones, dead-traveler markers)
    }

    /// <summary>One granted item, JSON-friendly (JsonUtility cannot serialize tuples).</summary>
    [System.Serializable]
    public class RewardEntry
    {
        public string itemId;
        public int count = 1;
    }

    /// <summary>
    /// One pick in a quest's reward-choice (melee vs magic vs archery, etc.).
    /// Presented by RewardChoicePanel on turn-in; the chosen option's items are
    /// granted. style drives the card accent ("melee"/"magic"/"archery").
    /// </summary>
    [System.Serializable]
    public class QuestRewardOption
    {
        public string label = "";
        public string description = "";
        public string style = "melee";     // melee | magic | archery
        public List<RewardEntry> items = new List<RewardEntry>();
        public bool equip = false;          // auto-equip the first equippable item
    }

    [System.Serializable]
    public class ObjectiveDef
    {
        // Serialized as a human-editable string in quests.json; parsed to the enum
        // for code. Valid values are the ObjectiveKind names above.
        public string kind = "KillMob";
        public string TargetId = "";   // "" = any of that kind
        public int Count = 1;
        public Vector3 Location;       // ReachLocation only (JsonUtility serializes Vector3 natively)
        public float Radius = 3f;      // ReachLocation only
        public string Description;
        public string LocationHint = "";  // optional "where to go" direction shown in the quest log

        public ObjectiveKind Kind =>
            System.Enum.TryParse<ObjectiveKind>(kind, out var k) ? k : ObjectiveKind.KillMob;
    }

    /// <summary>
    /// Static definition of a quest, loaded from StreamingAssets/quests.json
    /// (see QuestDatabase). A quest chains to the next via <see cref="PrereqQuestId"/>.
    /// </summary>
    [System.Serializable]
    public class QuestDef
    {
        public string Id;
        public string Name;
        public string Description;
        public List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public List<RewardEntry> Rewards = new List<RewardEntry>();
        public List<QuestRewardOption> RewardChoices = new List<QuestRewardOption>();  // optional pick-one
        public int GoldReward;
        public string GiverNpcId;        // "" = auto-turn-in chain quest (no NPC hand-in)
        public string StartCutscene;     // optional cutscene id played on Accept
        public string CompleteCutscene;  // optional cutscene id played when objectives finish
        public string PrereqQuestId;     // must be TurnedIn before this can be accepted
    }

    /// <summary>File wrapper for JsonUtility (mirrors Data.MonsterFile).</summary>
    [System.Serializable]
    public class QuestFile
    {
        public QuestDef[] quests = new QuestDef[0];
    }
}
