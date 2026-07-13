using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AldenFall.Items
{
    [System.Serializable]
    public class RecipeIngredient
    {
        public string itemId;
        public int count = 1;
    }

    /// <summary>
    /// One crafting recipe: consume inputs at a station to produce outputs, gated by
    /// a skill level, granting XP. Loaded from StreamingAssets/recipes.json.
    /// </summary>
    [System.Serializable]
    public class RecipeDef
    {
        public string id;
        public string skill = "Smithing";   // SkillType name
        public string station = "anvil";     // matches a CraftingStation's station id
        public int requiredLevel = 1;
        public List<RecipeIngredient> inputs = new List<RecipeIngredient>();
        public List<RecipeIngredient> outputs = new List<RecipeIngredient>();
        public float xp = 15f;
        public float seconds = 2f;

        public Progression.SkillType Skill =>
            System.Enum.TryParse<Progression.SkillType>(skill, out var s) ? s : Progression.SkillType.Smithing;
    }

    [System.Serializable]
    public class RecipeFile
    {
        public RecipeDef[] recipes = new RecipeDef[0];
    }

    /// <summary>Recipe registry, loaded from StreamingAssets/recipes.json (mirrors MonsterDatabase).</summary>
    public static class RecipeDatabase
    {
        public const string FileName = "recipes.json";
        private static List<RecipeDef> recipes;

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static IReadOnlyList<RecipeDef> All { get { EnsureLoaded(); return recipes; } }

        /// <summary>Recipes for a given station, in file order.</summary>
        public static List<RecipeDef> ForStation(string station)
        {
            EnsureLoaded();
            var list = new List<RecipeDef>();
            foreach (var r in recipes) if (r.station == station) list.Add(r);
            return list;
        }

        public static void Reload() { recipes = null; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (recipes != null) return;
            recipes = new List<RecipeDef>();
            try
            {
                if (!File.Exists(FilePath)) { Debug.LogError($"RecipeDatabase: {FilePath} not found"); return; }
                var file = JsonUtility.FromJson<RecipeFile>(File.ReadAllText(FilePath));
                if (file?.recipes != null)
                    foreach (var r in file.recipes)
                        if (r != null && !string.IsNullOrEmpty(r.id)) recipes.Add(r);
                Debug.Log($"RecipeDatabase: loaded {recipes.Count} recipes");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"RecipeDatabase: failed to load {FileName}: {e.Message}");
            }
        }
    }
}
