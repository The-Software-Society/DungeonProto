using System.Collections.Generic;

namespace AldenFall.Persistence
{
    /// <summary>
    /// A player's persisted profile, keyed by account id. Serialized to JSON by
    /// PlayerPersistence (a Render disk on the dedicated server, persistentDataPath
    /// locally). Parallel lists keep it JsonUtility-friendly. Extended as more
    /// state is captured; unknown/absent fields default cleanly.
    /// </summary>
    [System.Serializable]
    public class PlayerSaveData
    {
        public string accountId = "";
        public int gold;

        // Skills, indexed by SkillType value.
        public int[] skillLevels = new int[0];
        public float[] skillXp = new float[0];

        // Inventory: parallel item-id / count lists.
        public List<string> invItems = new List<string>();
        public List<int> invCounts = new List<int>();

        // Equipment: parallel slot-name / item-id lists.
        public List<string> equipSlots = new List<string>();
        public List<string> equipItems = new List<string>();

        // Quests: parallel id / status lists.
        public List<string> questIds = new List<string>();
        public List<int> questStatus = new List<int>();
    }
}
