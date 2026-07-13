# World 1 (Emberfall) Completion Plan

Created: 2026-07-12

This is a full implementation plan to build World 1 (Emberfall) to a complete, shippable beta. It is
not a scoping exercise and it does not cut content. Every system in `WORLD1_SOURCE_OF_TRUTH.md` and
`EMBERFALL_WORLD1_BETA_SPEC.md` is in scope and gets built. Where the specs left an open question, this
plan resolves it toward the fuller build and records the decision in section 3.

The plan is grounded in a full read of the current codebase, so each task names the real files and
types to touch. It is written to be executed in autonomous overnight loops.

Style rule: no em dashes anywhere, in this doc or in any game content.

Companion docs: `EMBERFALL_WORLD1_BETA_SPEC.md` holds the canonical content (all quest text, NPC
identities, drop tables, ability-mastery tables). This plan holds the build sequence and the
engineering. Do not duplicate quest prose here; author it from the spec.

---

## 1. Current State (honest baseline)

Live and deployed on itch and the Render dedicated server:

- 6 use-based skills (Attack, Strength, Defence, Magic, Fishing, Cooking), server-authoritative combat
  and XP (XP minted server-side in `Health.TakeDamage`, only on damage dealt).
- A 9-quest spine (`QuestDatabase.cs`), mostly auto-accepting, with one true NPC-given quest (rescue).
- Combat, forge (+1..+10 combine), gold, item UI, party/duel/loot MVP, dungeon that does not
  infinite-respawn.
- Radial world V2 baked into `Assets/Scenes/EmberfallDemo.unity`: city, lake and rivers, forest ring,
  Ember Approach gradient, an eight-room volcano dungeon with sealed doors and a boss chamber, a
  Floor-2 sealed gate.
- Eruption world-state (`EmberfallWorldState`, SyncVar Calm/Erupting/Cleared) with cutscenes.
- Vendors (Marla and others), a forge NPC, talkable townsfolk, the enterable Ember Rest tavern.
- An ability mastery system that already exists at rank 1 to 5 (damage only, client-local).

The gap to a complete beta is content depth, progression breadth, the shared-floor signature, and
polish. The engine foundations exist and are good; most work is building on them, plus a handful of
enabling refactors (section 4).

---

## 2. Definition of Done

World 1 beta is complete when all of the following are true. This is the acceptance checklist.

**Content**
- 16 authored mainline quests (Q1 to Q16) with NPC givers, dialog, reward choices, and cutscenes.
- 8 side quests including the repeatable Tavern bounty board.
- Full named NPC roster present with a job, a worry, and barks: Aldric, Marla Venn, Bram Ironbell,
  Elowen, Mira, Captain Rusk, Sister Maela, Toma Reed, Pip, Sella Hart, Garron Vale.
- Bespoke opening cinematic (normal life, death, particlize into Aldric's glade).
- The death-broke proof beat: Garron Vale dies and returns during the Q10 orc gate attack.
- Dragon-breach ending cinematic (Q15) and Aldric volunteering for World 2 (Q16).

**Progression systems**
- All 12 beta skills exist and level by use: Health, Attack, Strength, Defence, Magic, Archery,
  Fishing, Cooking, Mining, Smithing, Woodcutting, Fletching.
- Three-tier resource and crafting ladder (crude, iron, steel equivalents): ore, bars, logs, arrows,
  crafted gear capping at steel equivalent; the boss drops above steel.
- Ability mastery ranks 1 to 6 for every ability, each rank changing mechanics, animation, and VFX,
  and visible to other players.
- All 11 equipment slots: Weapon, Focus/offhand, Helm, Chest, Legs, Cape, Gloves, Boots, Necklace,
  Ring 1, Ring 2. First necklace roll from the boss clear.
- A reward-choice UI covering melee, magic, and archery at every reward beat.

**Shared floor**
- Server-wide floor-progress counters exist, persist across restarts, and are surfaced in the city and
  tavern.
- First-clear prestige: title `First Flame`, badge `Emberfall First Clear`, engraved names in the
  city, server-wide announcement, cosmetic reward.
- Server clear stabilizes the eruption, eases the dungeon, grants `Hope of Emberfall` to all World-1
  players, and reveals the dragon-breach teaser.
- Personal clear grants the World-1 flag, the Emberfang/Ember Focus/bow reward choice, the first
  necklace roll, 400 gold, and gates Floor-2 eligibility per player.

**Fairness and rules (hard criteria)**
- No reward beat gives only a sword. Every weapon reward offers a magic and an archery equivalent, or
  the boss drops all paths.
- Lava damages on visible lava only; soot and ember never damage.
- Geometry is secure everywhere: no fall-through or infinite-fall.
- Combat is server-authoritative including the weapon/ability gate and loadout; XP only on damage
  dealt (utility casts keep on-cast XP).

**Quality**
- No white or untextured silhouettes.
- All character animation uses Meshy anim-library clips, never transform-puppeting.
- Two-player validation of party, duel (including melee targeting), and loot passes on the dedicated
  server.
- A boss-ready character reaches main combat skill ~15 to 30 and Defence ~10 to 25 through normal
  play, matching the spec's time targets (5 to 10 hours mainline).

---

## 3. Resolved Design Decisions

Every previously open question is resolved toward the fuller build.

1. **Beta length: full 16-quest arc.** No vertical slice. Side quests may land alongside or just after
   their related mainline beat, but all 8 ship.
2. **Skills: all 12.** Health, Attack, Strength, Defence, Magic, Archery, Fishing, Cooking, Mining,
   Smithing, Woodcutting, Fletching. Full three-tier resource ladder.
3. **Floor boss: a named Volcano Guardian.** Reframe `cave_troll_boss` as the Cinderbound Guardian
   (keep the id for continuity, change display name and model dressing). It drops Emberfang, Ember
   Focus, and an Emberstring Bow so melee, magic, and archery are all covered.
4. **Reward choice: a real reward-choice UI, and dual paths where natural.** Build a generalized
   `RewardChoicePanel`. The boss additionally guarantees all three weapon paths through separate
   drops, so a player is never blocked by a wrong choice.
5. **Server progress: build it fully, and persistent.** Not a fast follow. Server-wide counters and
   per-player clear flags persist on a Render disk mount.
6. **Terrain rewrite: do it properly.** Ship World 1 on the Unity Terrain plus GPU-grass path with the
   detail-grass and splat-quality issues solved, not on the legacy cube ground.
7. **Level cap: implement the secret 500 correctly.** Keep 99 as the practical wall through curve
   tuning, never surface 500 in UI, but make the curve and combat math well-defined to 500.
8. **The NPC who dies and returns in Q10: Garron Vale**, introduced before the attack so the moment
   lands.
9. **Archery uses arrows (real ammo), fed by Fletching.** Ammo-light is not the beta target; the full
   loop ships.

---

## 4. Foundational Refactors (build these first)

These are the enabling systems that many features depend on. Doing them first removes rework. Each is
small and well-located.

### F0. Data-driven, scalable foundation (do this underneath everything)
See `ARCHITECTURE.md` for the full engineering design. In short: move all balance tuning into JSON
config files with named variables at the top (`config/combat.json`, `config/progression.json`,
`config/economy.json`) loaded by a `GameConfig` service, so combat, damage, XP, and economy are edited
without touching C#. Move all content onto the `monsters.json` pattern (Def plus Registry plus System),
migrating `ItemDatabase`, `AbilityDatabase`, and `QuestDatabase` from inline C# to JSON. Standardize the
scalable base patterns: a `SkillDef` registry, generalized gather and craft controllers driven by data,
an objective strategy for quests, and a `FloorDef` abstraction so worlds plug in as data. Order:
F0.1 GameConfig and config files, F0.2 migrate content databases, F0.3 registry-ize skills, F0.4
strategy patterns, F0.5 floor abstraction. F1 to F5 below build on this.

### F1. Data-driven quest engine
Today quests are hardcoded in `QuestDatabase.EnsureInitialized()` and NPC givers are baked one-per-quest
in the editor builder. 24 quests need a data-driven path.
- Add `StreamingAssets/quests/*.json` loading in `QuestDatabase`, parsed with `JsonUtility`, using a
  serializable `RewardEntry {string itemId; int count}` and float-array positions (JsonUtility cannot
  do tuples or Vector3). Mirror the cutscene JSON approach so quests hot-reload without a rebuild.
- Add the missing objective driver for `CollectItem`: hook `Inventory.OnItemAdded` in
  `QuestLog.Start()` next to the existing Fishing/Cooking/Ability/Eat hooks.
- Generalize `QuestGiverNPC` (`World/QuestGiverNPC.cs`) from a single `questId` to resolving the
  current offer-able or turn-in-able quest for its `npcId` from the player `QuestLog`. This lets a few
  named NPCs give many quests.
- Add a runtime giver spawner that reads giver definitions (npcId, world pos, model, quest set) from
  the quest data, so new NPC-given quests do not require an editor rebuild.
- Add quest-progress persistence (see F3): `QuestState` is in-memory only today.

### F2. New objective kinds and reward choice
- Add `InspectMarker` to `ObjectiveKind` and an `InspectableMarker : Interactable` whose `Interact()`
  calls `QuestLog.Advance(InspectMarker, markerId)`. Drives Q4 and Q11 seal stones and side quests.
- Add `DefendEvent`: a `DefendZone` MonoBehaviour that spawns timed orc waves and calls
  `Advance(DefendEvent, eventId)` on success. Drives Q10.
- Add objective groups for "complete any N of M": add `List<ObjectiveGroup>` with a `requiredCount` to
  `QuestDef`, change `QuestLog.CheckCompletion` (currently an AND over all objectives) to satisfy a
  group when its threshold is met, and update both `QuestTrackerHUD` render paths. Drives Q13.
- Generalize `WeaponChoicePanel` into `RewardChoicePanel.Present(player, options, onChosen)` (the
  card-building code is already generic; only the hardcoded starter kits need parameterizing). Make
  `QuestLog.TurnIn` async-aware so it presents the choice, grants the picked option, then chains the
  next quest.

### F3. Persistence and player identity
There is zero server-side persistence today and the Render container is ephemeral.
- Add a persistent player identity: extend `AldenPasswordAuthenticator` and login (`MultiplayerConnectUI`)
  to carry an account id, so per-player clear flags and progress can key to a stable id.
- Add a Render persistent disk: a `disk:` block in `render.yaml` mounted at `/data`.
- Add a server-side save/load service that reads and writes JSON at the mount (debounced), loaded in
  `OnStartServer`. Used by server progress and per-player flags.
- Add quest and skill persistence per player (server-authoritative once the economy moves server-side;
  see F4).

### F4. Economy server-authority
`Wallet`, `Inventory`, `Forge`, and vendor buy/sell are plain client-local code today, which breaks on
the dedicated server and cannot count toward server progress.
- Make gold server-authoritative: a SyncVar on a networked component or TargetRpc grants from
  `MobIdentity.GrantRewardsTo`.
- Route vendor buy/sell and forge combine through `[Command]` methods on a networked relay component
  (mirror the `PlayerCombat` relay pattern), validated server-side.
- Persist gold and inventory per player via F3.

### F5. Skill and stat expansion foundation
- Append the new skills to `SkillType` (append-only): Health, Archery, Mining, Smithing, Woodcutting,
  Fletching. Bump `SkillCount`. Add `DisplayName` cases.
- Append `Ranged` to `StatType` (append-only) and bridge it in `PlayerStatBridge`.
- Retune the XP curve in `Skills.XpForLevel` for a 500 cap and precompute the cumulative table instead
  of the runtime loop. Keep 99 as the practical wall.
- Retune `CombatMath.LevelGrowth` and the `monsters.json` mob-HP curve so the boss-ready targets in the
  Definition of Done hold and high levels do not overflow.
- Add the new skills to every UI enumeration point: `HUDBuilder.cs` glyph dictionaries (two copies at
  the skills panel and skill strip), `XpFeed` color switch, and the skill-tree tabs if the new combat
  skill (Archery) gets a tree.

---

## 5. Phased Implementation Plan

Phases are ordered by dependency. Story content (Phases 2, 3, 5) can run in parallel with skills and
progression (Phases 6 to 9) once the foundations (Phase 1) are done. Each phase ends shippable.

### Phase 0: Stabilize and Secure
Playtest bugs and geometry, so content is built on solid ground.
- Secure all geometry. Derive `DungeonResetController.EnsureDungeonEnvelope` coords from shared
  constants instead of the hardcoded duplicates of the volcano shell (they desync on any resize and
  reopen the void). Verify forest-corner coverage where the mountain massifs meet the ground disc.
  Confirm `BuildGlobalSafety` underfill and catch floor cover every basin.
- Add a `SafeZone` trigger-volume system (model on `EmberZone`): a static registry, and a check in
  `EnemyAI.CheckForPlayer` that skips acquisition when the mob or the target is inside a safe zone.
  Place a safe zone over the pond and city in the builder. Keep the neutral dancing goblins.
- Incremental mob clustering at forest entries: 1 to 3 easy mobs near entrances escalating inward,
  authored in `monsters.json` (the dungeon acolyte/sentinel/boss gradient is the template).
- Dodge-roll camera smoothness and straight-run fix (`ThirdPersonCamera`, `PlayerController`).
- Confirm the intro cutscene runs reliably on play.
- Full QA screenshot pass and redeploy to itch and Render.

### Phase 1: Quest Engine Foundation
Build F1 and F2 in full. Deliverable: the existing 9 quests run through the new data-driven loader
with no behavior change, plus the three new objective kinds and the reward-choice panel are proven on a
test quest. This unblocks all story content.

### Phase 2: Story Content A (Q1 to Q7) and glade/lake/city NPCs
- Author Q1 A Word from Aldric (opening cinematic prologue), Q2 Choose Your Edge (now via
  `RewardChoicePanel`), Q3 Find Your Footing (tutorial beats including a first ability-mastery rank),
  Q4 First Blood First Return (InspectMarker), Q5 A Fisher's Patience, Q6 Cook Then Eat, Q7 First
  Steps in Emberfall.
- Build the opening cinematic (normal-life scene, death, particlize) as a new cutscene JSON plus a
  short prologue set. Extend `IntroCutsceneTrigger`.
- Add named NPCs via a `BuildNamedTownsfolk` helper modeled on `PlaceTownsfolk`: Toma Reed and Pip at
  the lake, Marla and Bram already exist as vendor/forge (wire their quests), and the city arrival NPCs.
- Author the city-reveal cutscene for Q7.

### Phase 3: Story Content B (Q8 to Q13), the death-broke event, remaining NPCs
- Q8 A Mother's Plea (Elowen giver, Grukmar kill, Mira rescue, reward choice Knight's Sword / Adept
  Focus / bow equivalent), Q9 Smoke on the Roads (Captain Rusk, kill counts, unlocks bounty board),
  Q10 Retaliation at Emberfall Gate (DefendZone waves plus the Garron Vale death-and-return cutscene),
  Q11 Old Ash New Fire (InspectMarker seal stones), Q12 Smoke on the Horizon (ReachLocation approach),
  Q13 Ready for the Caldera (objective-group any-two-of prep).
- Add remaining NPCs: Elowen, Captain Rusk, Sister Maela, Sella Hart (tavern), Garron Vale (introduced
  pre-attack), each with job, worry, and barks.
- Build the Q10 death-broke cutscene (guard dies at the gate, reappears at the fountain, Aldric's
  restrained line).

### Phase 4: Dungeon Depth
- Extend `BuildEmberfallVolcano` with additional gated rooms and connecting pinch walls; add a
  `SealableDoor` per new gated room configured to its AABB; place `CreateLootChest` guarded chests
  behind those doors with meaningful loot. Keep `DungeonResetController.dungeonNamePrefixes` and the
  envelope in sync (or better, drive both from shared constants per Phase 0).
- Author richer incremental encounters in `monsters.json` inside the new rooms (respawn 999999 for
  dungeon mobs), with a readable difficulty ramp and one or two surprises.
- Reframe the boss as the Cinderbound Guardian: display name and model dressing, keep the id. Ensure
  it drops Emberfang, Ember Focus, and the Emberstring Bow, plus the necklace roll.
- Build near-city Erupting-phase content: an `EruptionSpawns` server component subscribing to
  `EmberfallWorldState.OnPhaseChanged` that enables pre-built, disabled lava pools and extra mob camps
  near the city on Erupting and clears them on Cleared. Lava damages, soot and ember do not.

### Phase 5: Story Content C (Q14 to Q16), all cutscenes, 8 side quests
- Q14 The Heart of Emberfall (dungeon entry triggers eruption, clear encounters, defeat the Guardian,
  phase to Cleared, reward choice plus necklace roll, personal clear flag), Q15 The Breach Beyond
  (dragon-breach cinematic), Q16 Beyond Emberfall (Aldric volunteers, reach World 2 staging marker).
- Build the dragon-breach cinematic (a Meshy dragon flyover, text-only letterboxed).
- Author all 8 side quests: Lake Oddities (Toma), Goblin Games (Pip, with the twerk emote unlock),
  Bandit Ledger (Marla), Iron for the Forge (Bram), The Old Seal Stones (Aldric), Dire Hunt (Rusk),
  The Empty Grave (Sister Maela), Tavern Board repeatable bounties (Sella / board).
- Wire the Tavern bounty board as a repeatable-quest source.

### Phase 6: Archery combat style
- Add `SkillType.Ranged` handling and a `RangedDamage` formula in `CombatMath` (copy `MeleeDamage`,
  scale by the new Ranged stat).
- Add `WeaponKind.Bow` (`ItemDatabase`) and `WeaponClass.Bow` (`AbilityDef`), an `Equipment.HasBow`
  query mirroring `HasMagicImplement`, and extend `CombatStyleController` beyond its hardcoded `% 3`.
- Add a bow branch in `PlayerMelee.Update` (copy `CastBolt`), firing an arrow via the existing
  `Projectile` (which already has `SetXpReward(SkillType, rate)` and routes damage through
  `PlayerCombat.DealDamage`), consuming an arrow from inventory per shot.
- Add bow and arrow items, the early bow, the steel-equivalent crafted bow, and the Emberstring Bow
  boss reward. Add bow-usable abilities where appropriate.

### Phase 7: Gathering, Crafting, and the Health skill
- Build a stateful `ResourceNode : Interactable` (the missing piece): resource item, required tool,
  required level, charges, respawn timer, server-authoritative. Use `FishingController` as the
  tick-loop and tiered-drop template.
- Mining: ore nodes (crude, iron, steel-equivalent) placed in the world builder; a `MiningController`
  copied from `FishingController`.
- Woodcutting: tree nodes and a `WoodcuttingController`, producing logs by tier.
- Build a `RecipeDef` registry (none exists; nearest precedent is `ItemDef.CookedResult`) for
  multi-input recipes.
- Smithing: an `Anvil : Interactable` plus a `SmithingController` (copy `CookingController`) consuming
  ore or bars to produce bars and crafted gear (crude to steel equivalent), gated by skill level.
  Crafted gear slots into `Equipment` and remains Forge-upgradeable automatically.
- Fletching: logs plus bars to arrows and bows, a `FletchingController`.
- Health skill: level through damage taken and survival, feeding max HP; wire into `Health`/`StatBlock`.
- Add real Meshy harvest, mining, and smithing animation clips (no transform-puppeting; Fishing and
  Cooking currently reuse attack triggers, which is acceptable only as a stopgap).

### Phase 8: Ability mastery ranks 1 to 6
- Extend `AbilityMastery` from `MaxRank = 5` to 6 with a sixth use-count threshold.
- Add a per-rank upgrade table: an `AbilityRankMod[]` on `AbilityDef` (damage mult, bonus radius,
  extra hits, added status id, VFX tier, anim trigger). Populate per ability from the spec examples
  (Meteor, Whirlwind, Blade Rush, and the rest).
- Generalize `AbilityMastery.DamageMultiplier` into `Modifiers(abilityId)` returning the resolved
  rank-mod. Swap the six `Cast*` methods in `PlayerAbilities` to read rank-mods for radius, hitcount,
  and added status, not just the damage scalar.
- Rank up VFX and animation: add a VFX tier and anim trigger to the visual helpers and gate on rank.
- Network the flourish: plumb rank through `PlayerCombat.BroadcastCast` and `RpcCast` /
  `ReplayRemoteCast` so other clients see the rank-6 version.
- Persist mastery per player via F3 (currently PlayerPrefs only).

### Phase 9: Equipment expansion and jewelry
- Append `Gloves, Boots, Necklace, Ring1, Ring2` to `EquipSlot` (append-only). Add them to
  `Equipment.Slots[]` and `ItemDatabase.SlotName()`.
- Add attach logic in `Equipment.AttachToSlot` and `CreateFallbackShape` (gloves to hands, boots to
  feet, necklace to neck; rings visually negligible).
- Add the dual-ring rule in `Equipment.Equip`: a ring fills Ring1 then Ring2.
- Extend the paper doll in both places: `HUDBuilder.cs` doll slot array and `CharacterPanelUI` runtime
  layout (a second column for the new slots).
- Add new `StatType`s for jewelry (append-only) with `ItemPlus.PerPlus` scaling and `CombatMath`
  handling.
- Add gloves and boots to drop tables (per the spec additions), the first-necklace roll on boss clear,
  and rings as rare side drops.

### Phase 10: Economy server-authority and persistence
Build F4 in full and connect it to F3 persistence: networked gold, vendor and forge Commands, and
per-player persistence of gold, inventory, equipment, quest progress, and skills on the Render disk.

### Phase 11: Shared floor and first-clear prestige
- Add a `ServerProgressState : NetworkBehaviour` scene singleton, authored next to `EmberfallWorldState`
  in `ArenaBuilderEmberfall.cs`, with SyncVar or SyncDictionary counters: boss defeated at least once,
  1,000 hostile mobs killed, 300 fish cooked or eaten, 100 gear upgrades forged, N players completed
  `q_ember_scout`. It subscribes to `MobIdentity.OnAnyMobKilled` (the hook `EmberfallWorldState`
  already uses) and to economy and skill events. Persist via F3.
- Surface progress in the city and tavern: a progress board UI reading the SyncVars.
- First-clear detection: on the first-ever transition to Cleared, record the clearer, fire a
  server-wide announcement `[ClientRpc]` (no announcement system exists today; build a banner UI
  modeled on `PartyHUD`), engrave names near the fountain or dungeon gate, and grant the `First Flame`
  title, `Emberfall First Clear` badge, and a cosmetic cape or aura.
- Server reward: on server clear, stabilize the eruption, ease the dungeon, grant `Hope of Emberfall`
  (+3% XP or gold in World 1) to all World-1 players, update city NPC dialogue, and reveal the
  dragon-breach teaser.
- Per-player clear flag and Floor-2 gating: store a personal clear flag keyed to the persistent player
  id (F3), and gate Floor-2 entry on it with a networked trigger that runs a Command eligibility check
  (the current `WorldStateGate` is global cosmetic, not per-player).

### Phase 12: Terrain rewrite
Complete the rolled-back Unity Terrain plus GPU-grass path (`UseUnityTerrain`): fix the URP detail-grass
rendering (the unlit black shards) and splat-texture quality, replace green ground spots with flat thin
walkable grass patches, and re-verify NavMesh bake and `GroundSpawn` raycasts against the terrain
collider. Ship World 1 on this path.

### Phase 13: Multiplayer validation
- Enable melee duel targeting: let `TargetingSystem` and `PlayerMelee` accept a player target when
  `DuelMember.IsDuelingWith(target.netId)` is true (the damage path already carves out duel pairs in
  `Health.TakeDamage`; only target acquisition, filtered to the `Enemy` tag, is blocking it).
- Make loadout server-aware: SyncVar main-hand and off-hand kind on `PlayerCombat` pushed from
  `Equipment.Equip`, and validate the weapon/ability gate server-side in the damage and cast Commands.
- Fix the dedicated-server reward path: `MobIdentity.GrantRewardsTo` must TargetRpc gold and rewards to
  the killer rather than mutate a server-side-only Wallet.
- Two-player validation on the dedicated server: party invite and share, duel (ranged and melee),
  loot ownership and protection windows.

### Phase 14: Balance, QA, and ship
- Retune combat and XP curves so the boss-ready targets hold and the 5-to-10-hour mainline pacing is
  right; verify grinders can push past 99 as a discovery moment without trivializing later worlds.
- Full QA screenshot pass (world and every UI popup) via the `-qashots` driver.
- Deploy: split the compile, scene rebuild, QA, and deploy steps (each exceeds the background cap).
  Push to itch (butler) and rebuild the Render dedicated server. Watch the `[Dd]ebug/` gitignore trap.
- A live first-clear dry run with two clients to validate the prestige and floor-gating flow.

---

## 6. Content To Author (from the spec)

All content prose lives in `EMBERFALL_WORLD1_BETA_SPEC.md`. This is the build checklist so nothing is
missed. Author each item there, build it via the phase above.

- **16 mainline quests:** Q1 A Word from Aldric, Q2 Choose Your Edge, Q3 Find Your Footing, Q4 First
  Blood First Return, Q5 A Fisher's Patience, Q6 Cook Then Eat, Q7 First Steps in Emberfall, Q8 A
  Mother's Plea, Q9 Smoke on the Roads, Q10 Retaliation at Emberfall Gate, Q11 Old Ash New Fire, Q12
  Smoke on the Horizon, Q13 Ready for the Caldera, Q14 The Heart of Emberfall, Q15 The Breach Beyond,
  Q16 Beyond Emberfall.
- **8 side quests:** Lake Oddities, Goblin Games, Bandit Ledger, Iron for the Forge, The Old Seal
  Stones, Dire Hunt, The Empty Grave, Tavern Board bounties.
- **11 NPCs:** Aldric, Marla Venn, Bram Ironbell, Elowen, Mira, Captain Rusk, Sister Maela, Toma Reed,
  Pip, Sella Hart, Garron Vale.
- **Cutscenes:** opening prologue, city reveal (Q7), mother/daughter reunion (Q8), Garron death and
  return (Q10), approach and glow (Q12), dungeon entry and eruption (Q14), boss intro and defeat (Q14),
  dragon breach (Q15), Aldric at the breach (Q16), plus the existing eruption and floor-cleared.
- **12 skills, three resource tiers, ammo:** per section 3 and Phases 6 and 7.
- **Ability mastery 1 to 6:** rank tables for Meteor Strike, Whirlwind, Blade Rush, and every other
  ability, per the spec examples.
- **11 equipment slots and jewelry;** first necklace roll from the boss.
- **Drop tables:** keep the existing tables, layer the spec's proposed additions (gold, materials,
  gloves and boots when slots exist, magic main-hand weapons, dire tokens, cosmetics), and the boss
  three-path reward.
- **First-clear rewards:** first-team, server, and personal reward sets per the spec.

---

## 7. Standing Constraints (do not violate)

- No em dashes in any content.
- Server-authoritative combat, XP, economy, and progress. XP only on damage dealt; utility casts keep
  on-cast XP. Enemies use `NetworkTransformReliable`; the dedicated server bakes NavMesh at runtime.
- No hard classes; skills are the build; all gear wearable by anyone; melee, magic, and archery
  first-class at every reward beat.
- Lava damages on visible lava only; soot and ember never damage.
- Never transform-puppet animation; use Meshy anim clips. No white silhouettes.
- Meshy budget: about 1,900 credits per month; run in 2 to 3 model batches within the background cap;
  reuse models at 0 credits; set `SSL_CERT_FILE=$(python3 -m certifi)`; the pipeline is resumable.
- Emberfall is a zone and floor name only; the game title is Aldenfall.
- The secret level cap of 500 is never surfaced in UI.
- Do not let monetization shape World 1; it must be the strongest possible free beta showcase.
- Aldric never reveals the world-travel method in World 1; the hidden organization is not named until
  a much later world.

---

## 8. Risk Register

- **Persistence and player identity (F3)** is the deepest new system. Everything server-authoritative
  and every per-player flag depends on it. Build and validate it early (Phases 10 and 11 depend on it,
  but do the identity and disk work as soon as Phase 1 lands).
- **Append-only enums** (`SkillType`, `StatType`, `EquipSlot`, `WeaponKind`, `WeaponClass`) are
  serialized and network-cast as ints. Always append; never reorder. A reorder silently corrupts saved
  and networked state.
- **Dungeon envelope duplication:** `DungeonResetController` hardcodes copies of the volcano shell
  coords. Every dungeon resize risks a fall-through. Drive both from shared constants in Phase 0.
- **Curve retune for 500** couples XP, damage, and mob HP. Retune all three together (F5, Phase 14) or
  high-level content breaks.
- **Cutscene actor binding by `GameObject.Find`** and hand-authored world positions couple cutscenes
  to NPC placement. Stabilize scene names or add a registry as the roster grows.
- **Deploy and build cycles exceed the background cap.** Always split compile, scene rebuild, QA, and
  deploy into separate steps.
- **Meshy credit cap** is real. Reuse models aggressively; batch new dragon, boss, and NPC models.
