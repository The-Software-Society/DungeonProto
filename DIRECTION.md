# Game Direction

*The canonical vision doc. The README covers how to run the project; this covers
what we're building and why. Updated 2026-07-03.*

## One-liner

An MMO where an entire server climbs a world together — OSRS-style skill grinding,
action combat, and a shared mountain nobody summits alone.

## The Five Pillars

### 1. Progression — OSRS DNA
- Skills level **by use**: swing a sword → Attack/Strength; get hit → Defence;
  cast → Magic. (✅ implemented: `Skills`, XP curves, level-by-use)
- Numbers are the dopamine: XP drops, level-up banners, skill milestones.
  (✅ implemented: XP feed, banners, skill strip)
- Quests as flavor + unlocks, not the main loop. Occasional quest chains that
  gate areas/gear, OSRS-style "one long weird story" energy. (🔲 not started)
- Long-horizon goals: 99s exist. Someone will grind them. Let them.

### 2. Combat — Black Desert body, WoW hands
- Action-forward: movement matters, dodge with i-frames, positioning, weight,
  lunges, knockback. (✅ foundation: dodge roll, lunge melee, knockback)
- Plus WoW-style **click/tab targeting for spells**: target → cast rotation
  (fireball, nova → later: full spellbook w/ cooldown tiers).
  (✅ foundation: TargetingSystem + targeted fireball/AoE nova)
- The blend: melee feels like BDO (aim your body), casting feels like WoW
  (aim your target). Both train different skills.

### 3. World Structure — SAO floors, server unity
**The signature system. This is what makes it *ours*.**
- The world is a tower/ascent of **Floors** (worlds/levels). Floor N+1 is sealed
  until **the server collectively** clears Floor N's requirements
  (world boss down, collective objectives — e.g. "1M goblin kills server-wide").
- Dual progression:
  - **Server progress** unlocks the floor for everyone + server-wide rewards.
  - **Personal progress** on each floor (its quests/boss/challenges) gates YOUR
    rewards and your right to the next floor's content.
- Front-runners race; casual players contribute to collective counters. Both matter.
- (🔲 not started — current zones [courtyard → field → cave] are effectively
  "Floor 1" structure practice. Boss-gates-progress already prototyped: Grukk.)

### 4. Travel — Flyff wings
- Flight as aspirational mid-game unlock: brooms/boards/wings, skill-based,
  fun to just *move*. Air travel makes big floors feel big and small trips joyful.
- (🔲 not started. Placeholder hook: `PlayerController` movement is
  camera-relative + stat-scaled (`MoveSpeed`); a flight mode slots in as a third
  locomotion state next to walk/swim.)

### 5. Commerce — Conquer/Flyff market culture
- Player **shop stalls**: plop a stall in the market district, list your drops,
  walk away. Browsing a bazaar of player stalls IS content.
  (Conquer's market chaos + Flyff's AFK vendor charm.)
- (🔲 not started. **PLACEHOLDER RESERVED**: Emberfall courtyard's market stall
  prop marks the future bazaar zone. Architecture note: `ItemDatabase` ids are
  string-keyed + `Inventory` is list-based specifically so trade/persistence can
  serialize them; add `TradeOffer`/`ShopStall` on top without reshaping items.)

## Current Systems Map (what exists → what it becomes)

| Today (prototype) | Tomorrow (MMO) |
|---|---|
| `StatBlock` on every entity | Same — already the single source of truth |
| `Skills` level-by-use (4 skills) | +Gathering/Crafting/Agility(flight?) skills |
| 5-slot equipment w/ visible gear | +rings/amulet/offhand; cosmetic layer |
| Zones: courtyard→field→cave | Floor 1 template: town→wilds→dungeon→floor boss |
| Grukk boss gates Emberfang | Floor bosses gate the NEXT FLOOR (server-wide) |
| `MobCamp` spawners | Named camps w/ collective kill counters |
| Single-player Unity scene | Server-authoritative multiplayer (the big lift) |

## Non-goals (for now)
- PvP (later, maybe floor-gated arenas)
- Housing (stalls first)
- Classes — skills ARE the class (OSRS rule: your levels are your build)

## Naming

Working title in code: **AldenFall** (repo). TikTok temp name: **Project Spire**.
Demo zone name: **Emberfall** (courtyard scene) — stays as a ZONE name only
("Emberfall" as a game title is taken: Steam game + book series).

Search-verified shortlist (2026-07-03 — zero relevant collisions at check time):
1. **Spirevale** ← front-runner: encodes the floors/climb mechanic, fully clean
2. **Vantheria** — classic MMO gravitas, fully clean
3. **Vaelspire** — moodier variant, fully clean
4. **Aldenfall** — keeps the "-fall" feel, clean
5. (runner-up w/ caveat) **Skyrend** — near-clean, slightly generic

Before locking: TikTok handle, .com/.gg domains, USPTO/EUIPO trademark search.
Rejected after verification: Emberfall, Emberspire, Veyrune, Solmara, Kindlespire,
Cindervale, Everclimb, Floorbound, Talveron.

## Content cadence (TikTok devlog)
- Each systems milestone = a clip: combat feel, loot explosion, boss kill,
  fullscreen recording via `AldenFall > Fullscreen Game View` (F8).
- Keep placeholder-to-real-asset glow-ups on camera — the Meshy pipeline makes
  "capsule → knight" transformations great content.
