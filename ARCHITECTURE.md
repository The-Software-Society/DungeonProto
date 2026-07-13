# AldenFall Architecture: Data-Driven, Scalable by Design

Created: 2026-07-12

This is the engineering companion to `WORLD1_COMPLETION_PLAN.md`. That plan says what to build and in
what order. This document says how to build it so that balancing is a JSON edit and new content
(skills, worlds, quests, abilities, recipes, economy) is added by data plus a small class, never by
editing a giant hardcoded method.

Two goals drive every decision here:

1. **Tuning lives in JSON, with named variables at the top.** Combat, damage, XP, drop rates, economy,
   and pacing are all edited in flat config files that a human or an AI can read and adjust without
   touching C#. No balance number is a `const` in a `.cs` file.
2. **Content scales by composition.** A new skill, a new world, a new quest objective, a new craftable,
   or a new ability shape plugs into a stable base pattern. Adding content does not mean growing a
   switch statement or a 200-line `EnsureInitialized()`.

Style rule: no em dashes anywhere.

---

## 1. The pattern we already have, and want everywhere

`Assets/StreamingAssets/monsters.json` plus `Assets/Scripts/Data/MonsterDatabase.cs` is the model. Every
mob's stats, movement, combat, loot, and spawns live in JSON. The C# side is three small pieces:

- `MonsterDef` and friends: `[Serializable]` plain-data classes.
- `MonsterFile { MonsterDef[] monsters }`: the file wrapper.
- `MonsterDatabase.LoadAll()`: reads the file with `JsonUtility.FromJson`, validates, returns the list.
  It is read identically by the editor build pipeline, playmode tests, and shipped builds.

Cutscenes follow the same idea (`StreamingAssets/cutscenes/*.json`, hot-reloaded, no rebuild).

Everything else that should be data is currently code: `ItemDatabase`, `AbilityDatabase`, and
`QuestDatabase` are static `EnsureInitialized()` methods with inline `Register(new XDef{...})` blocks;
`CombatMath` is all `const`; the XP curve is a formula in `Skills.XpForLevel`; forge cost and vendor
markup are literals. The plan below moves all of it onto the monsters.json pattern.

---

## 2. Two data layers and the directory layout

Separate tuning (balance knobs) from content (the things being defined). Both are JSON, both hot-reload,
but they answer different questions.

```
Assets/StreamingAssets/
  config/                 # LAYER 1: tuning. Balance knobs, variables at the top.
    combat.json           # damage formulas, mitigation, miss, swing speed
    progression.json      # xp curve, level caps, per-skill xp rates
    economy.json          # vendor markup, forge cost curve, gold rewards, global drop scale
    world.json            # eruption timings, safe-zone rules, aggro, respawn defaults
  content/                # LAYER 2: definitions. The actual game content.
    items.json            # every ItemDef
    abilities.json        # every AbilityDef + per-rank mastery mods
    skills.json           # every SkillDef (id, category, ui, xp rate ref)
    recipes.json          # every RecipeDef (smithing, fletching, cooking)
    gather.json           # every GatherProfileDef (fishing, mining, woodcutting)
    loot.json             # named, reusable loot tables
    npcs.json             # named NPC roster (job, barks, quest ids, placement)
  quests/                 # one file per quest, or grouped; data-driven quest defs
    q_intro.json ...
  floors/                 # LAYER 2: worlds. Each world/floor is one def.
    emberfall.json        # regions, spawn table ref, boss id, phases, progress goals, rewards
  monsters.json           # already here; move under content/ over time or leave and alias
  cutscenes/*.json        # already here
```

Rule: nothing in `config/` names a specific piece of content, and nothing in `content/` hardcodes a
balance number. A sword's base damage bonus is content (items.json); how weapon power turns into damage
is tuning (combat.json).

---

## 3. Tuning files: variables at the top

Each config file is a flat, well-named block of knobs. JSON has no comments, so we adopt one convention:
a leading `"_doc"` string array that the loader ignores (JsonUtility drops JSON keys with no matching C#
field), documenting each knob. Group knobs logically and keep names self-describing.

### `config/combat.json`
This replaces every `const` in `CombatMath.cs`.

```json
{
  "_doc": [
    "All combat math knobs. Damage = (base + weaponPower) * levelGrowth^(level-1), then mitigation.",
    "levelGrowth is THE master curve: mob HP in monsters.json is tuned to 40 * levelGrowth^(level-1).",
    "If you raise levelGrowth, high levels scale up fast. Keep mob HP on the same curve."
  ],
  "baseMeleeDamage": 5.0,
  "baseSwingInterval": 1.9,
  "levelGrowth": 1.085,

  "spellFocusGateBase": 0.60,
  "spellFocusGatePerPower": 0.04,
  "spellFocusPowerCap": 10,

  "mitigationPerDefence": 0.01,
  "mitigationPerArmor": 0.03,
  "mitigationCap": 0.75,

  "missBase": 0.10,
  "missPerDefenceLead": 0.035,
  "missFloor": 0.05,
  "missCeil": 0.95,

  "swingSpeedPerAttack": 0.008,
  "cooldownReductionPerLevel": 0.0046,
  "cooldownFloor": 0.55,

  "damageVariance": 0.15
}
```

`CombatMath` methods stay exactly where they are (the one place formulas live), but read from
`GameConfig.Combat.levelGrowth` instead of the local `const`. The formula shape stays in code; every
number becomes an edit in this file.

### `config/progression.json`
Replaces `Skills.XpForLevel`, `MaxLevel`, and per-skill XP rates.

```json
{
  "_doc": [
    "xpCoefficient and xpExponent define the curve: xpForLevel = sum l=1..L-1 of floor(coef * l^exp).",
    "practicalCap is the public wall (99). hardCap is the real secret ceiling (500), never shown in UI.",
    "xpRates lets each skill pace independently without touching code."
  ],
  "xpCoefficient": 50.0,
  "xpExponent": 1.35,
  "practicalCap": 99,
  "hardCap": 500,

  "xpRates": {
    "attack": 1.0, "strength": 1.0, "defence": 1.0, "magic": 1.0,
    "archery": 1.0, "health": 0.5,
    "fishing": 1.0, "cooking": 1.0,
    "mining": 1.0, "smithing": 1.0, "woodcutting": 1.0, "fletching": 1.0
  }
}
```

### `config/economy.json`
```json
{
  "_doc": [
    "vendorBuyMultiplier: player pays sellValue * this. forgeCost = forgeCostCoef * (plus+1)^forgeCostExp.",
    "globalDropScale multiplies every loot chance (event knob, keep at 1.0 normally)."
  ],
  "vendorBuyMultiplier": 4.0,
  "vendorSellFraction": 1.0,
  "forgeCostCoef": 50,
  "forgeCostExp": 2,
  "forgeMaxPlus": 10,
  "globalDropScale": 1.0,
  "questGoldScale": 1.0
}
```

Every number a designer or AI would touch to rebalance now lives in three files.

---

## 4. `GameConfig`: one loader, hot-reload, fail loud

A single static config service, modeled on `MonsterDatabase`, loads the `config/` files once and exposes
typed structs. Systems read from it instead of local constants.

```csharp
namespace AldenFall.Data
{
    [Serializable] public class CombatConfig    { public float baseMeleeDamage = 5f; public float levelGrowth = 1.085f; /* ...all knobs... */ }
    [Serializable] public class ProgressionConfig { public float xpCoefficient = 50f; public float xpExponent = 1.35f; public int practicalCap = 99; public int hardCap = 500; /* xpRates map */ }
    [Serializable] public class EconomyConfig   { public float vendorBuyMultiplier = 4f; public int forgeCostCoef = 50; /* ... */ }

    public static class GameConfig
    {
        public static CombatConfig Combat { get; private set; }
        public static ProgressionConfig Progression { get; private set; }
        public static EconomyConfig Economy { get; private set; }

        public static void LoadAll() { Combat = Load<CombatConfig>("config/combat.json"); /* ... */ }

        static T Load<T>(string rel) where T : new()
        {
            var path = Path.Combine(Application.streamingAssetsPath, rel);
            if (!File.Exists(path)) { Debug.LogError($"GameConfig: {path} missing, using defaults"); return new T(); }
            return JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? new T();
        }
    }
}
```

- **Defaults are the current shipped values**, so a missing or malformed file degrades to today's balance
  rather than breaking. Validation logs loudly (the MonsterDatabase habit).
- **Hot-reload:** in the editor and dev builds, a small `ConfigWatcher` re-runs `LoadAll()` on file change
  or on a key press, so balancing is live. Shipped builds load once at boot.
- **Server-authoritative:** the dedicated server loads the same `config/` files, so the server's combat
  math and the client's agree. Never read tuning from a client-editable place for authoritative math.

`CombatMath.MeleeDamage` becomes: `float raw = (GameConfig.Combat.baseMeleeDamage + weapon) *
LevelScale(strength);` and `LevelScale` reads `GameConfig.Combat.levelGrowth`. No call site changes.

---

## 5. The universal content pattern: Def, Registry, System

Every content domain uses the same triad. This is the single most important convention in the codebase.

- **Def** is `[Serializable]` plain data, loaded from JSON. No behavior. Example: `ItemDef`, `AbilityDef`,
  `SkillDef`, `RecipeDef`, `FloorDef`.
- **Registry** is a static loader plus lookup by string id. One per domain, all identical in shape to
  `MonsterDatabase`. Example: `ItemDatabase.Get(id)`, `AbilityDatabase.Get(id)`, `SkillRegistry.Get(id)`.
- **System / Controller** is the runtime behavior (MonoBehaviour or NetworkBehaviour) that reads Defs and
  acts. Example: `PlayerAbilities`, `GatherController`, `CraftingController`.

Migration: `ItemDatabase`, `AbilityDatabase`, and `QuestDatabase` move from inline C# `Register(...)`
blocks to JSON files loaded exactly like `monsters.json`. The public API (`Get`, `All`) stays the same,
so call sites do not change. Do them one domain at a time behind the unchanged API.

Id convention: `snake_case`, stable forever, and the id is the cross-reference key everywhere (a quest
reward names an item id, a mob loot entry names an item id, a floor names a boss id). Ids are the glue.

---

## 6. Skills as data (add a skill without editing a switch)

Today `SkillType` is a hardcoded append-only enum and skills are wired through several hand-edited
switches (`Skills.DisplayName`, two `HUDBuilder` glyph dictionaries, an `XpFeed` color switch). Adding a
skill means editing five places. Replace that with a `SkillDef` registry.

```json
// content/skills.json
{
  "_doc": ["category: combat | gather | craft | support. index is the stable serialization slot, append only."],
  "skills": [
    { "id": "attack",    "index": 0, "category": "combat",  "display": "Attack",    "glyph": "atk", "color": "#C8482D", "xpRate": 1.0 },
    { "id": "archery",   "index": 6, "category": "combat",  "display": "Archery",   "glyph": "bow", "color": "#5FA845", "xpRate": 1.0 },
    { "id": "mining",    "index": 8, "category": "gather",  "display": "Mining",    "glyph": "pick","color": "#9A8478", "xpRate": 1.0 },
    { "id": "smithing",  "index": 9, "category": "craft",   "display": "Smithing",  "glyph": "anvil","color":"#B0763C", "xpRate": 1.0 }
  ]
}
```

- `SkillRegistry.LoadAll()` reads this; `Skills` (per-player) sizes its `levels[]`/`xp[]` arrays by the
  registry count. The `index` field is the stable serialization slot (append only, never reorder), which
  preserves the current network/save integer contract while moving the list into data.
- UI enumerates the registry instead of hardcoded lists: `HUDBuilder` builds skill rows by iterating
  `SkillRegistry.All`, reading `glyph` and `color` from the def. The two duplicated glyph dictionaries and
  the color switch go away.
- **Category drives behavior wiring.** A `combat` skill feeds `StatBlock` via `PlayerStatBridge`. A
  `gather` or `craft` skill needs no stat bridge (matches how Fishing and Cooking already work). So adding
  Woodcutting is: one `skills.json` entry, one `GatherProfileDef` (section 7), and world node placement.
  No new networking, no new UI code.

`SkillType` the enum can remain as generated constants for compile-time call sites, or be replaced by
string ids; either way the registry is the source of truth.

---

## 7. Gathering and crafting as one data-driven system

`FishingController` and `CookingController` are two hand-written copies of the same shape. Generalize each
family once, then Mining, Woodcutting, Smithing, and Fletching are data.

### Gathering: `GatherController` + `GatherProfileDef` + `ResourceNode`
```json
// content/gather.json
{
  "profiles": [
    { "id": "mining_iron", "skill": "mining", "requiredLevel": 15, "tool": "iron_pickaxe",
      "tickSeconds": 3.0, "xpPerYield": 35,
      "yields": [ { "itemId": "iron_ore", "chance": 1.0, "minLevel": 15 } ] }
  ]
}
```
- One `GatherController` (generalized from `FishingController`) runs the tick loop, tool gate, level
  gate, and tiered-yield roll, all read from a `GatherProfileDef`.
- One `ResourceNode : Interactable` (the missing stateful node) references a profile id and adds charges
  plus a respawn timer, server-authoritative. Fishing spots, ore rocks, and trees are all `ResourceNode`
  with different profiles. Placement is a builder call or, better, node coordinates in the floor def.

### Crafting: `CraftingController` + `RecipeDef` + stations
```json
// content/recipes.json
{
  "recipes": [
    { "id": "smith_iron_bar", "skill": "smithing", "station": "anvil", "requiredLevel": 15,
      "inputs": [ { "itemId": "iron_ore", "count": 2 } ],
      "outputs": [ { "itemId": "iron_bar", "count": 1 } ], "xp": 25, "seconds": 2.0 },
    { "id": "fletch_iron_arrow", "skill": "fletching", "station": "fletch_bench", "requiredLevel": 20,
      "inputs": [ { "itemId": "iron_bar", "count": 1 }, { "itemId": "oak_log", "count": 1 } ],
      "outputs": [ { "itemId": "iron_arrow", "count": 15 } ], "xp": 18, "seconds": 2.0 }
  ]
}
```
- One `CraftingController` (generalized from `CookingController`) consumes inputs, produces outputs, grants
  XP, gated by level and by the station the player is at. Cooking becomes a recipe set with
  `station: "campfire"`. Smithing and Fletching are just more recipes plus a station Interactable.
- The existing `Forge` (+N combine) stays as a separate enhancement sink; crafted gear is a normal
  `ItemDef` and remains Forge-upgradeable automatically.

Result: the entire "OSRS economy loop" (gather to craft to gear) is authored in `gather.json` and
`recipes.json` with two generalized controllers and two small Interactable station types.

---

## 8. Abilities and mastery as data plus a behavior strategy

`AbilityKind` is currently a big switch inside `PlayerAbilities` (`CastProjectile`, `CastNova`, ...). Keep
the switch as a thin dispatcher, but move all tuning and per-rank behavior into data.

```json
// content/abilities.json
{
  "abilities": [
    { "id": "meteor", "kind": "GroundAoe", "weaponClass": "Focus", "baseDamage": 22, "radius": 4,
      "cooldown": 9, "impactDelay": 0.8, "tint": "#E4622A",
      "rankMods": [
        { "rank": 1, "damageMult": 1.0, "bonusRadius": 0, "extraHits": 0, "vfxTier": "a", "anim": "cast_a" },
        { "rank": 4, "damageMult": 1.24, "bonusRadius": 1, "addsStatus": "burn_ground", "vfxTier": "b" },
        { "rank": 6, "damageMult": 1.6, "bonusRadius": 2, "extraHits": 4, "vfxTier": "c", "anim": "cast_flourish" }
      ] }
  ]
}
```

- `AbilityDef` gains `AbilityRankMod[] rankMods`. `AbilityMastery` (which already exists at rank 1 to 5,
  damage only) extends to 6 and returns the resolved `AbilityRankMod` for the current rank, not just a
  damage scalar.
- The `Cast*` methods read radius, hitcount, added status, VFX tier, and anim from the resolved rank-mod.
  New per-rank behavior is a data edit. A genuinely new ability shape (a new `kind`) is one new behavior
  branch, ideally an `IAbilityBehaviour` implementation registered by kind so the switch becomes a map.
- Rank is plumbed through `PlayerCombat.BroadcastCast` so other clients see the rank-6 flourish.

---

## 9. Quests as data plus an objective strategy

Move quests from `QuestDatabase.EnsureInitialized()` to `StreamingAssets/quests/*.json` (hot-reload, like
cutscenes). Replace the AND-only completion switch with an objective strategy so new objective kinds are
new classes, not new switch arms.

```json
// quests/q10_gate.json
{
  "id": "q10_gate", "name": "Retaliation at Emberfall Gate", "giver": "captain_rusk",
  "prereq": "q9_roads", "startCutscene": "q10_start", "completeCutscene": "q10_garron",
  "groups": [
    { "requiredCount": 1, "objectives": [
      { "kind": "DefendEvent", "target": "gate_defense", "count": 1, "desc": "Defend the gate" } ] }
  ],
  "rewards": [ { "itemId": "iron_helm", "count": 1 } ], "gold": 175,
  "rewardChoices": []
}
```

- `QuestDef` gains `List<ObjectiveGroup>` where a group has `requiredCount` (satisfy any N of M). The
  AND-only case is a single group whose `requiredCount` equals its objective count. This unlocks Q13's
  "complete any two of."
- Each objective kind is an `IQuestObjective` strategy (a small class per kind) registered in a map by
  kind name. `KillMob`, `ReachLocation`, `TalkToNpc`, `InspectMarker`, `DefendEvent`, `CollectItem`, and
  the fishing/cooking kinds each become a class. Adding a kind is a class plus a registration, and
  `QuestLog` stops caring how a kind is measured.
- `rewardChoices` feeds the generalized `RewardChoicePanel` (melee, magic, archery), satisfying the
  fairness rule as data.
- Givers are data too: `npcs.json` lists which quest ids each NPC offers, and a runtime spawner places
  givers from that data, so a new NPC-given quest needs no editor rebuild.

---

## 10. Economy as data

- Vendor stock: per-NPC in `npcs.json`; buy and sell math from `config/economy.json`.
- Forge cost: `forgeCostCoef * (plus+1)^forgeCostExp` from economy.json, not the `Forge.CostFor` literal.
- Loot tables: named and reusable in `content/loot.json`, referenced by mobs (a mob names a loot-table id
  instead of inlining entries), scaled globally by `economy.globalDropScale`.
- Quest and mob gold: base value in the quest or mob def, multiplied by `economy.questGoldScale`.
- All of this becomes server-authoritative and persistent per `WORLD1_COMPLETION_PLAN.md` F4 and F3;
  the data layer here is what makes the server and client agree on prices and rates.

---

## 11. Worlds and floors: the multi-world abstraction

This is the scalability payoff. Emberfall should be one `FloorDef`, so World 2 through World 5 are new
floor files plus assets, not new bespoke systems. Today `EmberfallWorldState`, the boss id, the region
builders, and the progress goals are all Emberfall-specific. Generalize them.

```json
// floors/emberfall.json
{
  "id": "emberfall", "index": 1, "display": "Emberfall",
  "bossMobId": "cave_troll_boss",
  "phases": ["Calm", "Erupting", "Cleared"],
  "atmosphere": { "erupting": 0.85, "cleared": 0.15 },
  "regions": [ /* named region rects, spawn-table ref, node placements */ ],
  "spawnTable": "monsters.json#emberfall",
  "progressGoals": {
    "bossDefeated": 1, "mobsKilled": 1000, "fishCookedOrEaten": 300,
    "gearUpgrades": 100, "scoutCompletions": 25
  },
  "clearRewards": {
    "personal": { "gold": 400, "choice": ["emberfang", "ember_focus", "emberstring_bow"], "necklaceRoll": true },
    "firstTeam": { "title": "First Flame", "badge": "emberfall_first_clear", "cosmetic": "first_flame_cape" },
    "serverBuff": { "id": "hope_of_emberfall", "xpBonus": 0.03 }
  },
  "nextFloorGate": "floor2_sealed_gate"
}
```

- `EmberfallWorldState` generalizes to `FloorState : NetworkBehaviour` parameterized by a `FloorDef`
  (phases, boss id, atmosphere). The scene-network-singleton pattern stays.
- `ServerProgressState` reads `progressGoals` and `clearRewards` from the floor def, so the shared-floor
  system is floor-agnostic. World 2's goals are a JSON edit.
- The region builders can stay editor-side for hand-crafted geometry, but they read region rects, spawn
  tables, and node placements from the floor def so the data (not the code) defines what is where.
- A `WorldRegistry` lists floors and their unlock gating (personal clear of floor N gates floor N+1). SAO
  style floor climb is then data: add `floors/floor2.json`, list it, build its assets.

---

## 12. The universal modifier bus (stat sources)

`StatBlock` already aggregates bonuses from named sources (for example `equip:{slot}`). Formalize this as
the one extension point for every temporary or conditional stat change, so systems never touch base
stats directly.

- Equipment contributes `equip:{slot}` (exists).
- Ability mastery, food buffs, the `Hope of Emberfall` server buff, world-state effects, and potions each
  contribute a named source (`buff:hope_of_emberfall`, `food:cooked_bass`, ...).
- Adding a buff type is: pick a source key, add and remove the modifier. No new plumbing. This keeps the
  economy of new buffs (a huge future surface) at zero marginal system cost.

---

## 13. Player composition manifest

`NetworkPlayerAvatar.EnsureGameplayComponents()` already assembles the player's component stack at
runtime. Make that list the single manifest for player systems, so a new player-side system (a new
gather controller, the archery branch, a loadout sync) is added in exactly one place with a clear
server-versus-owner-versus-remote enable rule (the existing `SetLocalAuthorityVisuals` split). Do not
scatter `AddComponent` calls across builders and prefabs.

---

## 14. Conventions (the rules that keep this scalable)

1. **No balance number in C#.** If a designer or an AI might tune it, it lives in `config/`. Code holds
   formula shape and defaults only.
2. **One domain, one JSON, one registry, one loader shape.** All registries look like `MonsterDatabase`.
3. **Ids are `snake_case` and permanent.** Ids are the only cross-reference. Renaming an id is a content
   migration, not a rename.
4. **Serialization indices are append-only.** `SkillDef.index`, `StatType`, `EquipSlot`, `WeaponKind`,
   and `WeaponClass` are int-serialized and network-cast. Append, never reorder.
5. **Fail loud, default safe.** Missing or malformed data logs an error and falls back to a sane default
   (today's shipped value), never a silent crash.
6. **Hot-reload in editor and dev builds.** Content and config reload without a rebuild. Shipped builds
   load once at boot. The server loads the same authoritative config.
7. **Behavior is a strategy, not a switch arm.** Quest objectives and ability kinds are classes
   registered by name. Adding a kind is a class plus a registration.
8. **Content adds without code.** The target: a new skill, quest, item, recipe, gather node, loot table,
   NPC, or floor is a JSON edit. Only a genuinely new mechanic (a new objective kind, a new ability shape,
   a new controller family) is a small new class.

---

## 15. How this maps onto the completion plan

This architecture is the foundation beneath the plan's F1 to F5. Insert it as **F0**, and do the pieces
in this order so each is low-risk and immediately useful:

- **F0.1 GameConfig + config/ files.** Externalize `CombatMath`, the XP curve, and the economy constants
  first. Pure refactor, no behavior change, immediate balancing win. This alone gives you and the AI live
  tuning of combat, damage, XP, and economy.
- **F0.2 Migrate content databases to JSON.** `ItemDatabase`, `AbilityDatabase`, then `QuestDatabase`,
  one at a time behind their unchanged `Get`/`All` API, matching `MonsterDatabase`.
- **F0.3 Registry-ize skills.** `SkillDef` + `SkillRegistry`, UI enumerates the registry. Unblocks adding
  the six new skills as data (plan Phase 6 and 7).
- **F0.4 Strategy patterns.** `IQuestObjective` and the generalized gather/craft controllers, so plan F1
  (quest engine) and Phase 7 (skills) build on stable bases.
- **F0.5 Floor abstraction.** `FloorDef` + `FloorState` + floor-driven `ServerProgressState`, so plan
  Phase 11 (shared floor) and every future world plug in as data.

Once F0 is in, the 15 phases in `WORLD1_COMPLETION_PLAN.md` are mostly authoring plus small behavior
classes, which is exactly the point: the hard engineering is the foundation, and the content is data.
