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
    }

    public class ObjectiveDef
    {
        public ObjectiveKind Kind;
        public string TargetId = "";   // "" = any of that kind
        public int Count = 1;
        public Vector3 Location;       // ReachLocation only
        public float Radius = 3f;      // ReachLocation only
        public string Description;
        public string LocationHint = "";  // optional "where to go" direction shown in the quest log
    }

    /// <summary>
    /// Static definition of a quest (code-as-data, mirroring ItemDatabase). A
    /// quest chains to the next via <see cref="PrereqQuestId"/>.
    /// </summary>
    public class QuestDef
    {
        public string Id;
        public string Name;
        public string Description;
        public List<ObjectiveDef> Objectives = new List<ObjectiveDef>();
        public List<(string itemId, int count)> Rewards = new List<(string itemId, int count)>();
        public int GoldReward;
        public string GiverNpcId;        // "" = auto-turn-in chain quest (no NPC hand-in)
        public string StartCutscene;     // optional cutscene id played on Accept
        public string CompleteCutscene;  // optional cutscene id played when objectives finish
        public string PrereqQuestId;     // must be TurnedIn before this can be accepted
    }
}
