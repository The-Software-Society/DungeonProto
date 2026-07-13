# World 1 Source of Truth: Emberfall

Updated: 2026-07-12

This is the canonical design source for the first playable world/floor. `DIRECTION.md` remains the
game-wide vision. `EMBERFALL_WORLD_V2_PLAN.md` remains the implementation/history log for the V2
world rebuild. This document defines what Emberfall is supposed to feel like, contain, reward, and
teach.

## Game Foundation

### One-Sentence Promise

An online RPG where players climb dangerous worlds together, building their character through
OSRS-style use-based skills, action combat, meaningful quests, and optional long-form grinding that
always produces value.

### Ethos

The game should respect both kinds of players:

- The player who wants a guided adventure should be able to quest through the floor, learn the
  systems, get useful rewards, and reach the boss without being forced to kill hundreds of mobs.
- The player who wants to grind should be rewarded for staying in a camp, fishing for hours, farming
  rare drops, selling loot, leveling skills, and helping the server-wide floor effort.

The core rule: quests shorten and contextualize progression; grinding deepens and extends it. Neither
path should invalidate the other.

### Design Pillars

1. **Use-Based Progression**
   Skills level by doing the thing: melee, magic, defence, fishing, cooking, and future gathering or
   crafting skills. The player should feel their build emerging from repeated behavior, not from
   class selection.

2. **Quests With Mechanical Value**
   Quests are not only story delivery. They should unlock routes, introduce mechanics, grant useful
   gear, trigger world-state changes, reveal bosses, and reduce mandatory grind.

3. **Optional Grind With Real Output**
   Every repeat loop should produce one or more outputs: XP, gold, food, materials, rare gear, server
   progress, cosmetics, reputation, or market stock.

4. **A Shared Floor To Clear**
   Each world is a floor with a local crisis and a shared objective. Personal quest progress gets a
   player ready; server progress makes the world feel alive and collective.

5. **Readable Action Combat**
   Combat should feel physical and legible: movement matters, enemies telegraph, abilities have
   identity, and melee/magic are both full builds.

6. **Worlds Before Menus**
   Progress should be grounded in places: the lake is where fishing starts, the forge is where gear
   grows, the warcamp is where the rescue happens, and the volcano is the floor's truth.

## World 1 Identity

### Name

World/Floor name: **Emberfall**

Emberfall is a zone/floor name, not necessarily the final game title.

### Core Fantasy

Emberfall is a forest valley built around a city that survives beneath a dormant volcanic mountain.
The city believes the mountain is only a landmark. Aldric knows it is a sealed wound in the world.
As the player helps the valley, the truth surfaces: the volcano is waking, and the floor must be
cleared before the eruption reaches the city.

### Emotional Arc

1. **Wonder and Shelter:** Wake in Aldric's forest glade. Learn that the world is dangerous, but not
   hopeless.
2. **Daily Life:** Fish, cook, visit the city, meet vendors, see ordinary people worth protecting.
3. **Pressure:** Camps around the forest show disorder: goblins, bandits, orcs, missing people.
4. **Revelation:** The Ember Approach proves the volcano is not background scenery. It is the floor's
   threat.
5. **Emergency:** Entering the volcano triggers the eruption state. The city becomes endangered.
6. **Clear:** Defeating the floor boss calms the eruption, saves the valley, and reveals the sealed
   path upward.

### Visual Language

- City: warm torchlight, stone, timber, stalls, working NPCs, signs of ordinary life.
- Aldric's glade: quiet, mossy, human, slightly hidden from the main road.
- Lake: open air, swimmable water, visible fish, social/goofy goblin pocket, low danger.
- Forest ring: lush outside the city, increasingly hostile by pocket.
- Deep woods: dense canopy, lower light, older trees, dire mobs and secrets.
- Ember Approach: green fades to ash, ember particles rise, rocks char, lava appears gradually.
- Volcano: oppressive heat, glowing cracks, lava hazards, sealed doors, floor-boss architecture.

## Current Implemented Baseline

This section reflects the code state as of this document.

### Implemented Skills

- Attack
- Strength
- Defence
- Magic
- Fishing
- Cooking

### Implemented Quest Objective Types

- Kill mob
- Collect item
- Reach location
- Talk to NPC
- Use ability
- Catch fish
- Eat food
- Cook food

### Implemented Main Quest Spine

1. `q_intro` — A Word from Aldric
2. `q_learn_skill` — Find Your Footing
3. `q_learn_fish` — A Fisher's Patience
4. `q_learn_eat` — Cook, Then Eat
5. `q_rescue` — A Mother's Plea
6. `q_clear_roads` — Clear the Forest Roads
7. `q_ember_scout` — Smoke on the Horizon
8. `q_floor_boss` — The Heart of the Mountain

### Implemented Enemy Families

- Goblin Scrapper
- Field Bandit
- Orc Brute
- Ember Acolyte
- Stone Sentinel
- Dire variants
- Grukmar
- Cave Troll boss

### Implemented Gear Progression

- Starter: rusty sword, apprentice wand
- Goblin leather tier
- Bandit tier
- Orc iron tier
- Knight sword / adept focus tier
- Boss tier: Emberfang / Ember Focus

### Known Design Gaps

- Quest chain exists but is too thin for a full first world.
- Server-wide floor progress is conceptually core but not fully designed for Emberfall.
- Optional grind loops need clearer rewards and targets.
- City NPCs need stronger identities and reasons to exist.
- Volcano dungeon needs a full gameplay structure beyond "reach boss."
- Economy/vendor/forge loops need server-authoritative persistence later.
- Near-city eruption consequences are not fully realized.

## Emberfall Map

### Layout Rule

The city is the center. The forest surrounds it. The volcano is the southern/southeastern endgame.
Players should be able to understand the world by direction:

- North/Northwest: lake, fishing, social space, lower pressure.
- West: goblins and early combat.
- East: bandits and mid-tier humanoid drops.
- Southwest/South: orcs, rescue, warcamp pressure.
- North/Deep Woods: dire mobs, secrets, optional danger.
- South/Southeast: Ember Approach and volcano dungeon.

### Required Areas

#### Aldric's Glade

Purpose: spawn, tone-setter, tutorial, weapon choice.

Content:

- Aldric as hermit mentor, not a city elder.
- Starter weapon choice: sword path or wand/magic path.
- First ability use.
- Campfire and small safe area.
- Road signs toward city and lake.

#### Emberfall City

Purpose: safe hub, market, forge, social anchor, reason to save the floor.

Content:

- Fountain plaza.
- Market row with Marla the Trader and future player stall space.
- Forge with upgrade system.
- Residential district with named NPCs.
- Tavern/quest board placeholder for repeatables and party finding.
- Gates signed by direction.

Important NPC roles:

- Marla: vendor, buying/selling, hints toward market culture.
- Worried Mother: rescue quest emotional anchor.
- Forge NPC: upgrade tutorial and material sink.
- Town guards: communicate rising danger during eruption.
- Tavern keeper/board: future repeatables, bounties, party prompts.

#### Emberfall Lake

Purpose: fishing/cooking identity, non-combat loop, social contrast to the volcano.

Content:

- Fish anywhere from shore or while swimming.
- Visible fish schools.
- Lakeside campfire.
- Dancing/goofy goblins as a low-pressure signature moment.
- Low-level gathering/fishing repeatables later.
- Secret chest or rare fishing reward.

#### Goblin Woods

Purpose: first repeatable combat grind and early armor.

Content:

- Goblin camps with readable boundaries.
- Fast respawns.
- Leather drops.
- Optional rare goblin trinket/cosmetic later.
- Quest use: road clearing and early combat confidence.

#### Bandit Road

Purpose: player-versus-humanoid danger, gold/weapon drops, eastern road pressure.

Content:

- Bandit camps with tents, crates, road ambush points.
- Raider saber and cape drops.
- Better gold than goblins.
- Optional bandit bounty chain later.

#### Orc Warcamp

Purpose: first major story conflict and gear step.

Content:

- Grukmar as named warlord.
- Captive/daughter rescue.
- Orc brutes and war banners.
- Iron gear, knight sword, adept focus.
- Clear identity as "harder than roads, easier than volcano."

#### Deep Dark Woods

Purpose: optional danger and grind escalation.

Content:

- Dire goblin, dire bandit, dire orc.
- Secret chests.
- Rare drops with long respawns.
- Environmental storytelling: old stones, broken seals, hints that the volcano has awakened before.

#### Ember Approach

Purpose: transition from adventure to crisis.

Content:

- Gradual biome shift.
- Ember/soot atmosphere without passive damage.
- Lava pools that visibly match damage volumes.
- Ember acolytes and dire acolytes.
- Scout quest location.
- Eruption trigger at volcano entry.

#### Emberfall Volcano

Purpose: first floor dungeon, culmination, group-capable challenge.

Content:

- Entrance vestibule.
- Multi-room combat path.
- Lava hazard rooms.
- Acolyte packs.
- Stone sentinel room.
- One or two simple door/lever/key beats.
- Boss chamber.
- Floor-2 sealed gate revealed after clear.

## Main Story Plan

### Act 1: The Hermit and the Valley

Goal: teach the player that skills grow by use and the world is a place, not just a combat arena.

Quest beats:

1. Wake in Aldric's glade.
2. Speak with Aldric.
3. Choose starter weapon.
4. Use an ability.
5. Visit lake, catch fish.
6. Cook and eat fish.
7. Enter the city and see its normal life.

Needed expansion:

- Add city arrival beat after cooking.
- Add "visit the forge" microquest.
- Add "sell or buy something from Marla" microquest.

### Act 2: The Forest Is Fraying

Goal: introduce threats by faction and show that quests are useful shortcuts into progression.

Quest beats:

1. Worried Mother asks for help.
2. Player reaches orc warcamp.
3. Kill Grukmar.
4. Rescue or confirm the daughter is safe.
5. Return to city.
6. Clear goblins and bandits from roads.

Needed expansion:

- Rescue should include interacting with the captive, not only killing Grukmar and returning.
- Goblin and bandit objectives should teach optional camp farming, loot, and vendor value.
- Add quest reward choice: melee gear or magic gear, so both builds feel supported.

### Act 3: Smoke on the Horizon

Goal: shift from local problems to floor-level crisis.

Quest beats:

1. City NPCs mention ash, heat, strange lights.
2. Aldric admits he knows more about the mountain.
3. Player scouts the Ember Approach.
4. Player discovers acolytes or signs of a broken seal.
5. Returning to Aldric/city starts the final preparation step.

Needed expansion:

- Add a "prepare for the caldera" quest that asks the player to upgrade gear, cook food, or reach a
  recommended combat skill threshold. It should accept alternate completions so it does not force a
  specific grind.

### Act 4: The Mountain Wakes

Goal: make the dungeon entry feel like a world event.

Quest beats:

1. Player enters volcano threshold.
2. Eruption begins.
3. City atmosphere changes.
4. Dungeon path opens.
5. Kill floor boss.
6. Eruption clears.
7. Floor-2 gate is revealed but sealed.

Needed expansion:

- Add near-city eruption consequences: NPC dialogue, ember sky, limited lava fissures outside the
  walls, emergency guards, optional rescue/defense events.
- Add a clear post-boss handoff: Aldric explains that Emberfall was only the first floor.

## Recommended Full Quest List

### Critical Path

1. **A Word from Aldric**
   Talk to Aldric. Reward: gold, starter context.

2. **Choose Your Edge**
   Choose rusty sword or apprentice wand. Reward: selected weapon and matching first loadout.

3. **Find Your Footing**
   Use any ability. Reward: small gold, ability tutorial complete.

4. **A Fisher's Patience**
   Catch a fish at Emberfall Lake. Reward: fishing XP/gold.

5. **Cook, Then Eat**
   Cook and eat a fish. Reward: cooked fish bundle.

6. **First Steps in Emberfall**
   Visit the city fountain, Marla, and the forge. Reward: gold and forge hint.

7. **A Mother's Plea**
   Accept rescue in town. Kill Grukmar, free the captive, return. Reward: build-appropriate gear.

8. **Clear the Forest Roads**
   Kill goblins and bandits. Reward: iron helm or equivalent focus reward.

9. **Old Ash, New Fire**
   Speak with Aldric after road clearing. He points south. Reward: lore and direction.

10. **Smoke on the Horizon**
    Scout Ember Approach. Reward: gold and final warning.

11. **Ready for the Caldera**
    Complete any two preparation objectives:
    - Upgrade an equipped item at the forge.
    - Cook three healing foods.
    - Reach level 5 in any combat skill.
    - Defeat one dire enemy.
    - Buy or sell with Marla.
    Reward: dungeon supply bundle.

12. **The Heart of the Mountain**
    Enter volcano, trigger eruption, clear dungeon, kill floor boss. Reward: Emberfang or Ember
    Focus choice, gold, floor-clear flag.

13. **The Sealed Stair**
    Visit the revealed Floor-2 gate and speak with Aldric. Reward: title/cosmetic later, next-floor
    teaser.

### Optional Side Quests

1. **Lake Oddities**
   Fish up or find a strange ember-warmed stone near the lake. Leads to Aldric lore.

2. **Goblin Games**
   Help or observe the goofy lake goblins. Rewards a cosmetic, emote, or joke item. Keeps the world
   from being one-note grim.

3. **Bandit Ledger**
   Recover a ledger from bandit camp. Unlocks better vendor prices or a repeatable bounty board.

4. **Iron for the Forge**
   Bring orc scraps/iron drops to the forge. Teaches upgrade economy.

5. **The Old Seal Stones**
   Find three old stones in the Deep Woods. Foreshadows the volcano seal.

6. **Dire Hunt**
   Kill one dire variant. High-risk optional reward and long-respawn tutorial.

## Grind Loops

### Combat Grinding

Players who want to stay and kill hundreds or thousands of enemies should gain:

- Combat XP through actual damage.
- Gold and vendor trash.
- Gear drops by faction.
- Rare drops from dire variants.
- Party XP/quest credit if grouped.
- Server-wide floor progress when implemented.

Faction rewards:

- Goblins: early armor, fast XP, low gold.
- Bandits: gold, saber, cape, starter magic focus.
- Orcs: iron armor, knight sword, adept focus.
- Acolytes: magic gear, higher XP, dungeon readiness.
- Sentinels: tank gear and boss-entry confidence.
- Dire mobs: rare drops, prestige, long-respawn targets.

### Fishing and Cooking

Players who avoid combat should still feel productive:

- Fishing XP.
- Cooking XP.
- Food for self-use, parties, or future player stalls.
- Rare fish as vendor value.
- Lake social identity.

Future additions:

- Fishing collection log.
- Rare lake cosmetic.
- Cooking recipes beyond basic cooked fish.
- Food buffs that support boss prep.

### Forge and Economy

The forge should be the first real item sink.

Short-term:

- Upgrade gear from +1 to +5.
- Gold sink.
- Optional quest objective.

Later:

- Require materials from combat/gathering.
- Let crafters specialize.
- Feed player stalls.

### Exploration

Exploration should reward players who wander:

- Secret chests.
- Dire mob discoveries.
- Lore stones.
- Fishing spots.
- Shortcut routes.
- Visual reveals.

## Server-Wide Floor Progress

This is not fully implemented yet, but Emberfall should be designed around it.

### Personal Clear

A player personally clears Emberfall by:

- Completing the main quest through the floor boss.
- Receiving the floor-clear flag.
- Unlocking eligibility for Floor 2 when the server unlocks it.

### Server Clear

The server clears Emberfall when collective goals are met. Recommended initial goals:

- Cave Troll boss defeated at least once.
- 1,000 total hostile mobs killed across the floor.
- 300 total fish cooked or consumed.
- 100 total gear upgrades forged.
- 25 players, or a tuned percentage of active players, have completed `q_ember_scout`.

The exact numbers should be tuned for population. For small test servers, keep these low.

### Why This Matters

Front-runners get race energy from boss clears. Casual players contribute by fishing, cooking,
forging, killing low-level mobs, or helping friends. The floor becomes something the server completes
together instead of a private checklist.

## Reward Philosophy

### Quest Rewards

Quest rewards should be reliable and useful:

- Starter quests give food, gold, and basic gear.
- Rescue gives a meaningful weapon or offhand choice.
- Road clearing gives armor or focus progression.
- Dungeon prep gives consumables.
- Floor boss gives an epic build-defining reward.

### Grind Rewards

Grind rewards should be probabilistic and long-tail:

- Common drops support gold and gear replacement.
- Uncommon drops support build upgrades.
- Rare/dire drops create stories.
- Cosmetics and titles can provide prestige without breaking balance.

### Build Fairness

Major reward moments should support melee and magic:

- Do not give only swords from story beats.
- Any quest that grants a weapon should offer a magic equivalent or offhand.
- Boss should let players pick Emberfang or Ember Focus, or should drop both through separate paths.

## Content Tone

Emberfall should balance warmth and danger:

- Aldric is quiet, regretful, and practical.
- The city should feel worth saving.
- The lake can be playful.
- Camps should feel dangerous but not edgy for its own sake.
- The volcano should feel mythic and urgent.

Avoid making every NPC speak in generic fantasy exposition. Give each NPC a job, worry, or useful
function.

## Implementation Plan

### Phase A: Canon and Quest Expansion

- Add `q_choose_weapon`.
- Add `q_city_arrival`.
- Expand `q_rescue` to include captive interaction.
- Add `q_old_ash_new_fire`.
- Add `q_ready_for_caldera` with alternate objectives, or approximate it with currently supported
  objective types until compound alternate objectives exist.
- Add `q_sealed_stair`.
- Update quest rewards so melee and magic both get supported.

### Phase B: Area Content

- Add stronger area dressing and signs for each directional pocket.
- Add more named NPC placement in city.
- Add Deep Woods lore stones and secret chest pathing.
- Add lake side quest and cosmetic/goofy goblin moment.
- Add volcano room sequence with at least three distinct encounters before boss.

### Phase C: Grind Systems

- Add repeatable bounty board using existing kill objective patterns.
- Add fishing/cooking repeatable or collection log.
- Add faction drop tables with clearer tier identity.
- Add rare/cosmetic drops for dire variants.
- Add forge objective hooks and material sinks.

### Phase D: Server Floor Progress

- Add server counters for kills, boss clears, cooking/fishing, and upgrades.
- Add UI surface in city/tavern showing floor progress.
- Gate Floor 2 by server clear plus personal clear.
- Give server-wide clear reward or title when the floor unlocks.

### Phase E: Eruption State

- Make eruption visibly affect city and forest.
- Add near-city but avoid unfair random deaths.
- Add emergency NPC dialogue.
- Add world-state cleanup after boss clear.
- Keep lava damage tied only to visible lava.

## Immediate Next Decisions

1. Decide whether quest rewards can support reward choice now, or whether to fake it by granting both
   melee and magic items until a reward-choice UI exists.
2. Decide whether server-wide progress should be implemented before or after the first beta walk.
3. Decide the target length for World 1:
   - Fast demo: 30-45 minutes to clear.
   - First beta floor: 2-4 hours casual, longer for optional grind.
   - MMO-feeling floor: 8+ hours with repeatables and collection goals.
4. Decide whether Emberfall's floor boss is always Cave Troll, or whether it should become a more
   volcano-specific named boss with the current troll as placeholder.

