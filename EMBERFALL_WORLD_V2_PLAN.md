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
