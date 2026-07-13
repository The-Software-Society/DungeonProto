using System.Collections;
using UnityEngine;
using AldenFall.Progression;
using AldenFall.World;

namespace AldenFall.Player
{
    /// <summary>
    /// Runs the AFK gathering loop when the player works a ResourceNode (ore, tree),
    /// mirroring FishingController: gate on the required tool and skill level, then
    /// tick, consume a node charge, add the resource to the bag, and grant XP, until
    /// the node is spent, the bag is full, or the player walks away. Client-local.
    /// </summary>
    public class GatherController : MonoBehaviour
    {
        private Items.Inventory inventory;
        private Skills skills;
        private ResourceNode active;
        private Coroutine loop;

        /// <summary>(itemId, skill) each time a resource is gathered, for quest hooks.</summary>
        public event System.Action<string, SkillType> OnGathered;

        private void Awake()
        {
            inventory = GetComponent<Items.Inventory>();
            skills = GetComponent<Skills>();
        }

        public void StartGather(ResourceNode node)
        {
            if (node == null || loop != null) return;

            if (!string.IsNullOrEmpty(node.RequiredTool) &&
                (inventory == null || !inventory.Contains(node.RequiredTool)))
            {
                Say($"You need a {ItemName(node.RequiredTool)} for that.");
                return;
            }
            if (skills != null && skills.Level(node.Skill) < node.RequiredLevel)
            {
                Say($"You need {Skills.DisplayName(node.Skill)} level {node.RequiredLevel}.");
                return;
            }

            active = node;
            loop = StartCoroutine(GatherLoop());
        }

        private IEnumerator GatherLoop()
        {
            while (active != null)
            {
                yield return new WaitForSeconds(active.TickSeconds);
                if (active == null) break;
                if ((transform.position - active.transform.position).sqrMagnitude > 16f) break; // walked off

                if (!active.TryConsumeCharge()) { Say("The node is spent."); break; }

                if (inventory != null && inventory.Add(active.ResourceItem))
                {
                    skills?.AddXp(active.Skill, active.XpPerYield);
                    OnGathered?.Invoke(active.ResourceItem, active.Skill);
                }
                else { Say("Your bag is full."); break; }
            }
            loop = null;
            active = null;
        }

        private void Say(string message) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f, message, UI.UITheme.TextDim);

        private static string ItemName(string id)
        {
            var d = Items.ItemDatabase.Get(id);
            return d != null ? d.Name : id;
        }
    }
}
