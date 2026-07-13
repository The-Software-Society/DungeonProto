using System.Collections;
using UnityEngine;
using AldenFall.Items;
using AldenFall.Progression;

namespace AldenFall.Player
{
    /// <summary>
    /// AFK crafting at a station (anvil, fletch bench), mirroring CookingController:
    /// repeatedly find the first recipe for this station the player has the level and
    /// materials for, wait its craft time, consume inputs, produce outputs, and grant
    /// XP, until nothing more can be made or the bag is full. Client-local.
    /// </summary>
    public class CraftingController : MonoBehaviour
    {
        private Items.Inventory inventory;
        private Skills skills;
        private Coroutine loop;
        private string activeStation;

        /// <summary>(primary output itemId, skill) each successful craft.</summary>
        public event System.Action<string, SkillType> OnCrafted;

        private void Awake()
        {
            inventory = GetComponent<Items.Inventory>();
            skills = GetComponent<Skills>();
        }

        public void StartCraft(string station)
        {
            if (loop != null || string.IsNullOrEmpty(station)) return;
            activeStation = station;
            loop = StartCoroutine(CraftLoop());
        }

        private IEnumerator CraftLoop()
        {
            while (true)
            {
                var recipe = FindCraftable(activeStation);
                if (recipe == null) { Say("Nothing more you can make here."); break; }

                yield return new WaitForSeconds(recipe.seconds);
                if (!CanCraft(recipe)) break;   // materials/level may have changed

                foreach (var inp in recipe.inputs)
                    for (int i = 0; i < inp.count; i++) inventory.Remove(inp.itemId);

                bool full = false;
                foreach (var outp in recipe.outputs)
                    for (int i = 0; i < outp.count; i++)
                        if (!inventory.Add(outp.itemId)) full = true;

                skills?.AddXp(recipe.Skill, recipe.xp);
                OnCrafted?.Invoke(recipe.outputs.Count > 0 ? recipe.outputs[0].itemId : "", recipe.Skill);

                if (full) { Say("Your bag is full."); break; }
            }
            loop = null;
        }

        private RecipeDef FindCraftable(string station)
        {
            foreach (var r in RecipeDatabase.ForStation(station))
                if ((skills == null || skills.Level(r.Skill) >= r.requiredLevel) && CanCraft(r))
                    return r;
            return null;
        }

        private bool CanCraft(RecipeDef r)
        {
            if (inventory == null) return false;
            foreach (var inp in r.inputs)
                if (inventory.CountOf(inp.itemId) < inp.count) return false;
            return true;
        }

        private void Say(string message) =>
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f, message, UI.UITheme.TextDim);
    }
}
