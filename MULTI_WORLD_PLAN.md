# Multi-World "Reusable Bones" Architecture Plan

Goal: World 1 (Emberfall) becomes the reusable template so future worlds (floors) reuse
the same networking, scene/build framework, and world-object mapper, while a player's
**global** progression follows them across every world. Improving how something *looks*
(dirt, a shader, a prop) must land in **all** worlds automatically **without** changing any
world's size, placement, or layout.

This plan is written to be executed **after World 1 content is complete** (per Jon), but the
P0 decision below is worth locking early because every extra week of baked-in placement is
more to unwind later.

---

## The two separation axes

1. **Per-world vs Global (data lifecycle)**
   - *Per-world* (each world owns its own): the scene / build recipe, the world-object mapper
     (placement of trees/mobs/buildings/props), world size & layout, per-world networked
     world-state (eruption phase etc.), and world-specific content authoring (this world's
     quests/mobs/NPCs).
   - *Global* (follows the account across all worlds): skills, XP, inventory, equipment, gold,
     quest progress, ability mastery, and the set of cleared worlds.

2. **Look vs Placement (what improves globally)**
   - *Look* = code + shared assets: texture generators, shaders, prop/model library, material
     definitions. Improve once → every world benefits, no coordinate touched.
   - *Placement* = per-world data: where props/mobs/buildings go, terrain size, zones.
     A look change must never require editing placement, and vice-versa.

---

## Current state (verified by audit, 2026-07-15)

**Already correct — keep as-is (the global spine):**
- `Persistence/PlayerPersistence` keys saves by `accountId`; `PlayerSave.Capture/Apply`
  persists gold, all skills+XP, inventory, equipment, quest statuses — genuinely global and
  world-independent.
- Runtime content registries load JSON at runtime and are world-agnostic:
  `monsters/items/quests/abilities/skills/recipes/npcs.json`, `config/*.json`.
- `GroundTextureGenerator` writes procedural textures into **named materials**
  (`EmberGrass0-3`, `LakeSand`, …). Improving the `Dirt()`/`Grass()` code + regenerating
  updates every object using those materials with **zero** coordinate changes — the
  "improve look globally" goal already works for material-based looks.

**Gaps to close:**
- **No world/floor abstraction.** `F0.5 Floor abstraction` was never started — no
  `FloorDef`/`WorldDef`/`floorId` exists. "Floor" is a hardcoded literal `1`
  (`PlayerProgress.floor1Cleared` bool, `ServerProgressState.floorBossMobId = "cave_troll_boss"`).
- **Placement is baked C#, not runtime data.** `world_placements.json` / `mob_placements.json`
  are *exports written after* an editor-time build (for validation + the F3 overlay). Nothing
  loads them at runtime to build the world. The real placement is ~430 KB of Emberfall-specific
  builder code (`ArenaBuilder` + `ArenaBuilderEmberfall`, `RenderCatalog` at
  `ArenaBuilderEmberfall.cs:342`). **A "World 2" today = clone + rewrite the builder.**
- **Networking has no scene routing.** `AldenNetworkManager` never sets `onlineScene`/
  `ServerChangeScene`; single `playerPrefab`, `defaultSpawn` hardcoded to Emberfall's glade.
- **Some look/placement entanglement in the builder.** `BuildEmberfallTerrain` welds terrain
  *size* (900×1040) to splat *painting*; `RenderCatalog` computes prop tint/collision inline at
  placement time; shared look assets live under `Assets/Materials/Emberfall/` (a world-named
  folder that should be shared).

Readiness verdict: **single-world hardcoded, with a clean global-player-state layer.**

---

## Target architecture

```
WorldDef  (data, one per world — StreamingAssets/worlds/<id>/world.json)
  id                "emberfall"
  displayName       "Emberfall — Floor 1"
  sceneName / buildRecipe        how to load/build this world
  contentPaths      { placements, mobs, quests, npcs } → worlds/<id>/*.json
  bounds / size, spawnPoints, zones
  bossMobId, firstClearBuff

Global (account-scoped, PlayerSave — unchanged)
  skills, xp, inventory, equipment, gold, quest-progress (keyed by GLOBAL quest id),
  clearedWorlds : HashSet<string>          // was: bool floor1Cleared
  currentWorldId

Shared look (code + asset library, global — Assets/Materials/Shared, Assets/Shaders)
  GroundTextureGenerator, shaders, Meshy model library resolved BY TYPE ID.
  Improving any of these lands in every world; no world data changes.

Networking (per-world instance)
  AldenNetworkManager maps worldId → scene; ServerChangeScene / portal to switch worlds.
  Players in the same world instance share combat/mobs/world-state.
  World-state (eruption, first-clear) is a per-world NetworkBehaviour keyed by worldId.
```

The keystone: world data references shared look-assets **by type id**; it never embeds the
look. `{type:"PineTree", x, z}` + a shared `PineTree` model/material = improving PineTree
improves every world.

---

## Phased path (execute after World 1 ships)

**P0 — Make placement data-driven at runtime (the keystone; everything depends on it).**
Split the builder into (a) a generic, world-agnostic *renderer* that instantiates props/mobs/
NPCs by reading `worlds/<id>/placements.json` + `mobs.json` at load, and (b) per-world
authoring data. Flip `RenderCatalog` (`ArenaBuilderEmberfall.cs:342`) from reading the
in-memory `_catalog` to reading the exported JSON. Emberfall's own build then round-trips
through the same path (authoring code → JSON → renderer), proving the pipe. Only then is
"World 2 = new JSON folder" true.

**P1 — Introduce `WorldDef` / `FloorDef` (completes F0.5).**
A `WorldDef` loaded like the other registries. Parameterize `ServerProgressState`
(`floorBossMobId`, first-clear buff) from it. Generalize `PlayerProgress.floor1Cleared` →
`HashSet<string> clearedWorlds` and `PlayerSaveData.floor1Cleared` likewise (migrate the old
bool on load). Low-risk, unblocks server-wide progress per world.

**P2 — Extract a generic builder base.**
Pull world-agnostic primitives (terrain-from-spec, city-from-spec, water-from-spec) out of the
`BuildEmberfall*` methods and the Emberfall constants (`ArenaBuilderEmberfall.cs:33-55`). A
world becomes `{ terrain size, zone rules, feature list }` data, not code. Decouple terrain
*size* from splat *painting* in `BuildEmberfallTerrain`.

**P3 — Networking scene routing.**
Add worldId→scene mapping and `ServerChangeScene`/portal support to `AldenNetworkManager`;
`defaultSpawn` comes from the active `WorldDef`, not a hardcoded Emberfall coord. Decide the
co-op model: one server hosts one world at a time (simplest) vs. per-world sub-instances.

**P4 — Relocate shared look assets.**
Move cross-world materials/textures out of `Assets/Materials/Emberfall/` into
`Assets/Materials/Shared`; move `CatalogTint`/`CatalogCollision` into a world-agnostic
prop-style table so a look improvement is one edit that lands in every world.

---

## Guardrails (do NOT break)

- Keep `PlayerSave` account-keyed and world-independent — it is already right; just migrate
  `floor1Cleared` → `clearedWorlds` with a back-compat load.
- Keep content DBs and `config/*.json` global. Per-world content = per-world FILES referencing
  global type ids, not per-world FORKS of the shared systems.
- Global quest ids must stay globally unique so quest *progress* (global) and quest
  *definitions* (per-world data) don't collide.
- A "look" change is code/shared-asset only and must compile+regenerate without editing any
  `placements.json`. A "layout" change is data only and must not require a code change.
