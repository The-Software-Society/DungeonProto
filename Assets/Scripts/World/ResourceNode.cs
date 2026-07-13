using System.Collections;
using UnityEngine;
using AldenFall.Progression;

namespace AldenFall.World
{
    /// <summary>
    /// A stateful gathering node (ore rock, tree): press E to have the player's
    /// GatherController work it. Has a required tool, a required skill level, a
    /// per-hit yield, and a charge pool that depletes and respawns after a delay
    /// (the node dims while spent). Client-local like FishingController; per-node
    /// charge sync across clients is a Phase-13 multiplayer item.
    /// </summary>
    public class ResourceNode : Interactable
    {
        [SerializeField] private string skill = "Mining";      // SkillType name
        [SerializeField] private string resourceItem = "copper_ore";
        [SerializeField] private string requiredTool = "pickaxe";
        [SerializeField] private int requiredLevel = 1;
        [SerializeField] private float xpPerYield = 20f;
        [SerializeField] private float tickSeconds = 3f;
        [SerializeField] private int charges = 8;
        [SerializeField] private float respawnSeconds = 20f;

        public SkillType Skill => System.Enum.TryParse<SkillType>(skill, out var s) ? s : SkillType.Mining;
        public string ResourceItem => resourceItem;
        public string RequiredTool => requiredTool;
        public int RequiredLevel => requiredLevel;
        public float XpPerYield => xpPerYield;
        public float TickSeconds => tickSeconds;

        private int remaining;
        private Renderer[] rends;

        public void Configure(string skillName, string item, string tool, int level,
            float xp, float tick, int charge, float respawn)
        {
            skill = skillName; resourceItem = item; requiredTool = tool; requiredLevel = level;
            xpPerYield = xp; tickSeconds = tick; charges = charge; respawnSeconds = respawn;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            remaining = charges;
        }

        public bool Depleted => remaining <= 0;

        public bool TryConsumeCharge()
        {
            if (remaining <= 0) return false;
            remaining--;
            if (remaining <= 0) StartCoroutine(Respawn());
            return true;
        }

        private IEnumerator Respawn()
        {
            SetVisible(false);
            yield return new WaitForSeconds(respawnSeconds);
            remaining = charges;
            SetVisible(true);
        }

        private void SetVisible(bool v)
        {
            if (rends == null) rends = GetComponentsInChildren<Renderer>();
            foreach (var r in rends) if (r != null) r.enabled = v;
        }

        public override void Interact(GameObject player)
        {
            if (remaining <= 0 || player == null) return;
            player.GetComponent<Player.GatherController>()?.StartGather(this);
        }
    }
}
