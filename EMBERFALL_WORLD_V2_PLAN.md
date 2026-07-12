# Emberfall — Floor 1, V2 (production, beta-ready)

**Mandate (Jon, 2026-07-11):** Do NOT build off the existing arena. The prototype was "can this
exist." V2 is a **fresh, deliberately-designed, polished Floor 1 = Emberfall**, ready for beta.
Keep all SYSTEMS (combat, quests, fishing, forge, party, cutscenes, Mirror networking, Meshy props);
throw out the spatial world and design a real one. Architect so a future Floor 2 can stack (SAO).

## The vision (Jon's words, structured)
- **City center.** Emberfall city sits at the CENTER of the region.
- **Forest surrounds the city** (radial — woods on all sides, not a linear corridor).
- **Distinct forest pockets** within the surrounding woods:
  - **Emberfall Lake + connecting rivers** — BOTH swimmable; **fish visibly swimming** in them.
  - **Deep dark wooded** part (dense, low-light, tougher).
  - **Goblin camp**, **orc camp**, **bandit camp**, etc. (distinct, placed around the ring).
- **The dungeon is a VOLCANO** (the literal "ember-fall"). The forest **leading up to it
  progressively changes**: embers + soot start falling from the sky, then increasing **lava**.
  **Lava pools DAMAGE** anything inside/touching them (DoT).
- **Main quest line → clear the dungeon.** Its climax **triggers a world ember+soot+lava state**:
  the eruption spreads toward the city, embers taking over — you **must clear the dungeon (floor
  boss) to prevent the city from falling**. Dynamic world-state transformation, reversed on clear.

## Spatial layout (radial; origin = city center)
- **Emberfall (City)** — walled town at (0,0), ~120m across. Central **Ember Fountain** plaza;
  districts radiating out: Market Row (vendors), The Forge (gear +1→+5), Tavern + quest/party board,
  Aldric's home by a gate. 4 signed gates into the forest ring. Warm, textured, lived-in, NPCs.
- **Forest ring** (~200–300m out on all sides) with pockets by direction (tune during build):
  - **NW — Emberfall Lake** + feeder rivers (swim, swimming fish, fishing spot, dancing goblins).
  - **N — Deep Dark Woods** (dense canopy, dim, elevated danger).
  - **W — Goblin Camp**, **E — Bandit Camp**, **NE — deeper Orc Warcamp** (Grukmar, rescue quest).
  - **S/SE — the Ember Approach**: forest that chars and darkens toward the volcano; ember/soot
    intensity ramps, first lava pools appear.
- **Emberfall Volcano (Dungeon)** — far S/SE, a caldera; multi-room crawl, lava hazards, no infinite
  respawns, elite guardians, **Floor Boss** (clear = floor objective + saves the city). The sealed
  **Stairway to Floor 2** sits beyond/atop it (locked; SAO seed).

## New systems to build (self-contained, then wired by world-gen)
1. **World/LavaHazard.cs** — trigger-volume DoT. Attach to a GO with a trigger BoxCollider; server
   applies `damagePerSecond` on `tickInterval` to any `Health` inside (players + enemies). Public
   `Configure(float dps, float tick)`. World-gen: AddComponent + Configure, like TorchFlicker/FishingSpot.
2. **World/FishSchool.cs** — decorative swimming fish. `Configure(Vector3 center, Vector3 extents,
   int count, float surfaceY)` spawns simple fish that patrol/bob just under the surface within the
   volume. Purely visual (ambiance for lake + rivers).
3. **World/EmberAtmosphere.cs** — singleton controller. `SetGlobalIntensity(float 0..1)` drives
   falling ember/soot particles + fog color/density + light tint (0 = lush calm, 1 = apocalypse).
   `World/EmberZone.cs` — trigger volumes placed along the Ember Approach that raise LOCAL target
   intensity when the local player is inside (positional gradient toward the volcano), blended.
4. **World/EmberfallWorldState.cs** — networked (SyncVar `Phase { Calm, Erupting, Cleared }`).
   Server API `BeginEruption()` / `MarkDungeonCleared()`. On phase change: drives
   EmberAtmosphere global intensity, toggles near-city lava hazards, optional spawns. Hooks: main
   quest climax (enter dungeon / accept final quest) → BeginEruption; Floor Boss death → MarkDungeonCleared.

## Main quest line (spine)
Aldric intro (city) → learn skills/fishing/eating → forest tasks (goblins→bandits, fishing, rescue
Grukmar from the orc warcamp) → gear up + forge in city → the Ember Approach darkens → discover the
volcano is erupting (climax cutscene: **BeginEruption**, city under ember threat) → clear the
Emberfall Volcano dungeon → kill the Floor Boss (**MarkDungeonCleared**, city saved, calm returns) →
the Floor-2 stairway is revealed (sealed; sequel hook). Text cutscenes at each beat.

## Build phases
- **Phase 1 (systems):** LavaHazard, FishSchool, EmberAtmosphere+EmberZone, EmberfallWorldState.
  Self-contained, compile-verified. [agents A+B]
- **Phase 2 (world-gen):** fresh radial world-gen — city center + forest ring + lake/rivers/camps +
  Ember Approach gradient + volcano dungeon — wiring the Phase-1 systems. Replaces the old arena.
- **Phase 3 (quest + polish):** main-quest spine + world-state transformation + cutscenes; boss;
  named-area title cards; lighting/audio polish; Meshy textured buildings/props for the city.
- Deploy per phase (itch + Render) so Jon can walk it and steer.

## Guardrails
- Everything textured (PropFallbackColor exists for texture-less Meshy props — no white silhouettes).
- No invisible walls; swimmable water needs a Water-layer trigger volume (PlayerController swim
  enters at depth ≥1.3m). Server-authoritative damage via Health. ClientRpc has no interest mgmt.
- Keep Aldric intro + tutorial + rescue + fishing + forge systems working against the new map.

---
# Phase 2.5 — Jon's walk-through feedback (2026-07-11) + fix plan

Jon walked the deployed V2 world. Verdict: rough first cut — flat tiled box in empty sky, invisible
walls everywhere, core combat bugs. Full feedback (grouped):

## Core combat / systems bugs (make it PLAYABLE)
- **XP must be integral to damage, not hardcoded.** Auto-attacking a mob you didn't select gives no
  XP; if a mob hits you first and you retaliate you get no XP; and a mob that takes NO damage still
  grants XP on attack (image 25 gate goblin). Fix: XP granted server-side when your attack actually
  DEALS damage to an enemy Health, attributed to the attacker. No XP-on-swing / XP-on-target-select.
- **Enemies don't aggro / can't be damaged.** Non-dancing goblins/mobs should all be aggressive.
  Some mobs never aggro and never take damage (image 25). Fix aggro + damageability for all combat
  mobs; ensure decorative townsfolk are NOT targetable damage-dummies.
- **Soot/ember must NOT damage; lava MUST.** Currently dying just standing in the soot area; walked
  through visible lava with 0 damage (lava hazard mis-placed / not on the visible lava).

## World realism (make it a REAL place, not a floating box)
- Natural boundaries: **mountains/hills/cliffs enclosing the map**, NOT invisible walls or gray
  floor→sky emptiness (img 20). No holes / falling out of the world (img 16). Immersive depth.
- **Gradual biome transitions** — no stark straight line between grass and soot (img 17); lush→
  soot→ember→lava should blend.
- **Trees: trunk-only collision.** Invisible tree boxes block walking AND magic projectiles (img 15,
  22); deep forest unnavigable. Only be blocked if you actually touch the trunk; magic passes through.
- **Grounded spawns** — mobs floating on invisible floor / in sky (img 23); ground everything.
- **Realistic lava** — currently a flat yellow square that flickers an orange border on movement.
  Make it molten/glowing/animated.
- **Natural water** — river looks man-made and dead-ends (img 19); lean into rivers leading off-map
  to WATERFALLS at the edges. Lake has man-made stairs (img 21) — use mud/slopes; be able to FISH
  ANYWHERE in the lake, even while swimming. (Rivers currently not even visible per Jon.)

## Content / design
- **City too small + empty** — a flat square of randomly-placed things, no structure/depth. Bigger,
  denser, real districts.
- **Aldric is NOT in the city.** He's the homeless old man — was someone great, fled to the forest
  for peace, becomes the player's mentor. Put him in a FOREST glade.
- **Intro flow:** first load = cutscene meeting Aldric, talk/learn, then CHOOSE first weapon
  (main magic OR main melee). Player currently spawns in the soot forest with no intro.
- **Magic depth:** magic only has Fireball + Ember Nova. Add more (another single-target ranged, a
  ranged AoE, DoT/utility) so a magic-only build has depth; balance vs melee.
- **More mobs** — forest feels empty.
- **Dancing goblins belong by the LAKE, not the soot area.** Twerker STILL missing (only Modern
  "hand pump" + Robot show). Want MANY more goofy fishing-friendly goblins by the lake.
- **Volcano dungeon unplayable** — one empty room then blocked off; can't explore.
- Odd/incomplete pockets (img 18); clipping/tweak-through (img 24).

## Phase-2.5 execution (staged; iterate per deploy)
- **Round 1 (now):** (A) combat integrity [XP-on-damage + aggro + damageability]; (B) world realism
  [mountain-ring boundaries + no void/holes + gradual biome gradient + trunk-only tree colliders +
  grounded spawns + realistic lava & correct hazard placement + natural lake-mud banks + rivers→
  waterfalls + dancing goblins to lake + twerker + more + player spawn + Aldric forest glade + more
  forest mobs]; (C) magic ability depth; (D) intro cutscene + starter-weapon choice + Aldric hermit.
  Keep the valley floor flat (mountains ring it) so existing placement stays grounded.
- **Round 2:** bigger structured city; playable multi-room volcano dungeon; fish-anywhere; polish.

---
# Round 2 + Phase 3 status (2026-07-12 overnight pass)

**Combat integrity (audit of hotfix c486f00, all fixed):** enemy jump/charge specials now
require `agent.isOnNavMesh` (they teleported via transform writes — THE "damage from range,
contact gone" bug); ServerNavMeshBaker is server-only gated (a slow-connecting client used to
run a wasteful bake), verifies the bake produced triangulation, and re-warps agents that
spawned before the bake; enemy LOS raycast now masks out the Enemy layer + triggers; remote
avatars keep a trigger capsule when their CharacterController is disabled (host-mode aggro).
Intro hardening: trigger waits on `isLocalPlayer` (not just netId), aborts cleanly when no
camera exists, CutsceneDirector null-guards the camera + logs missing actors, and playback
runs in try/finally so a throwing step can never strand input-locked players.

**Phase 3 eruption — WIRED:** EruptionTrigger (volcano entry tunnel AABB, server) →
BeginEruption; MobIdentity.OnAnyMobKilled (server event) → cave_troll_boss death →
MarkDungeonCleared; Floor2_SealedGate opens on Cleared via new WorldStateGate; climax text
cutscenes `eruption.json` / `floor_cleared.json` play on genuine phase transitions.

**Round 2 — DONE:** dungeon fragilities fixed (boss-door seal now 16.5×8 full corridor+ceiling,
was slip/jump-passable 6.6×5; SealableDoor 90s fail-open safety; vestibule acolyte pair at
z-330 so the entrance reads hostile); city densified to 19 buildings (double market row, W/SW
residential lanes) + gate-street lamps + 7 townsfolk; **Marla the Trader** vendor at the market
(new VendorNPC + runtime VendorPanel: buy at 4× SellValue, sell anything, Esc/walk-away);
**fish-anywhere-while-swimming** (F while swimming, bobber on the water surface ahead; bank
spots unchanged); quest LocationHints rewritten with V2 compass directions.

**Jon ruling 2026-07-12:** utility casts (Taunt/SelfBuff/Blink) KEEP on-cast XP.
**Still open:** near-city lava/spawns during Erupting phase (atmosphere-only for now); Meshy
buildings (Cottage/BlacksmithShop/Storehouse/TownHall still fallback blocks); starter-weapon
choice is client-side only (server unaware of Focus swap — flag for combat owner); vendor/forge
economy is client-local.
