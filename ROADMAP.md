# AldenFall Roadmap

Next work for World 1 (Emberfall), taken from the project's own plans:
`WORLD1_COMPLETION_PLAN.md`, `MMO_NETCODE_PLAN.md`, `MULTI_WORLD_PLAN.md`,
`EMBERFALL_WORLD1_BETA_SPEC.md` and `ALDENFALL_GAME_PLAN.md`. Estimates are focused engineering
hours and add up to well over 100 hours; the first two milestones alone are roughly 30.

## Milestone 1: Netcode hardening (about 14 h)

- [ ] **Server-side damage clamp (2 h).** In `PlayerCombat.CmdDealDamage`, clamp client-reported
  damage to a maximum derived from server-known skill levels plus a gear/crit margin. Stops gross
  cheating without changing legit combat. (MMO_NETCODE_PLAN #63, option A)
- [ ] **Full server-authoritative damage (8 h).** Push equipment and stance modifier totals and
  ability mastery ranks to the server through `PlayerStatBridge`; replace
  `DealDamage(precomputed)` with `DealMelee / DealAbility / DealRanged` that run `CombatMath` on
  the server; update `PlayerMelee`, `PlayerAbilities` and `Projectile`. Verify with a live
  2-client damage test. (#63, option B)
- [ ] **Per-player world-state phasing (4 h).** Add a per-player `PlayerWorldPhase` SyncVar, make
  `EmberfallWorldState` mirror the local player's phase, move eruption triggers to the entering
  player and party, and gate `LavaHazard` damage server-side by phase. (#64)

## Milestone 2: Quest engine and story content (about 18 h)

- [ ] **Data-driven quest engine (6 h).** Move quest definitions out of `QuestDatabase.cs` into
  JSON with NPC givers, dialog, step gating and reward choice. (Completion plan F1, Phase 1)
- [ ] **New objective kinds and reward-choice UI (4 h).** Gather, craft, talk, escort and reach
  objectives; every weapon reward offers melee, magic and archery options. (F2)
- [ ] **Story content A, Q1 to Q7 (8 h).** Author from the beta spec, with glade, lake and city
  NPC placements and barks. (Phase 2)

## Milestone 3: Persistence and economy (about 14 h)

- [ ] **Player persistence and identity (8 h).** Save skills, inventory, equipment, quest state and
  gold on the server across restarts (Render disk mount or another store). (F3, Phase 10)
- [ ] **Economy server authority (6 h).** Vendor buy/sell, forge and loot pickup validated on the
  server. (F4)

## Milestone 4: Progression breadth (about 24 h)

- [ ] Archery combat style end to end: bows, arrows, Ranged XP, projectiles, animations. (Phase 6)
- [ ] Gathering and crafting ladder: Mining, Smithing, Woodcutting, Fletching with crude, iron and
  steel tiers; Health skill training rules. (Phase 7)
- [ ] Ability mastery ranks 1 to 6 that change mechanics and VFX, synced to other players. (Phase 8)
- [ ] Equipment expansion to 11 slots including gloves, boots, necklace and two rings. (Phase 9)

## Milestone 5: Story content B and C, dungeon depth (about 24 h)

- [ ] Q8 to Q13 including the Garron Vale "death-broke" proof beat during the orc gate attack.
  (Phase 3)
- [ ] Dungeon depth: room variety, elite packs, boss mechanics, sealed-door puzzles. (Phase 4)
- [ ] Q14 to Q16, the dragon-breach ending cinematic and 8 side quests including the repeatable
  tavern bounty board. (Phase 5)

## Milestone 6: Shared floor and first-clear prestige (about 8 h)

- [ ] Persistent server-wide floor-progress counters surfaced in the city and tavern.
- [ ] First-clear prestige: `First Flame` title, `Emberfall First Clear` badge, engraved names,
  server announcement; server clear stabilizes the eruption and reveals the breach teaser.
  (Phase 11)

## Milestone 7: World look and performance (about 16 h)

- [ ] ColorLab-driven color pass for grass, tree foliage and terrain splat, then varied grass
  patches. (SESSION_HANDOFF task #95, #92)
- [ ] Water shader pass and lighting/performance pass with streaming and density tiers. (#93, #94)
- [ ] Keep the dedicated server lean (box colliders, stripped meshes) and re-check memory on the
  smallest server plan.

## Milestone 8: Open-source quality of life (about 8 h)

- [ ] Publish a downloadable asset bundle (or Git LFS) for the generated models so a fresh clone
  renders fully, or add primitive-free placeholder prefabs for missing models.
- [ ] Edit-mode and play-mode tests for `CombatMath`, `Skills` XP curve, item and quest JSON
  loading, run in CI with Unity batch mode.
- [ ] Contributor guide: editor menu tour, data file reference, how to add a monster/item/quest.

## Later: multi-world bones

- [ ] Split reusable systems from World 1 content so World 2 can reuse combat, skills, quests and
  networking unchanged. (MULTI_WORLD_PLAN.md phased path)
