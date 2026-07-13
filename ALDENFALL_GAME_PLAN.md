# Aldenfall Game Plan

Updated: 2026-07-12

This is the overarching direction for Aldenfall. World-specific implementation details belong in
dedicated specs, starting with `EMBERFALL_WORLD1_BETA_SPEC.md`.

## Core Identity

**Aldenfall** is an online action RPG about a layered fantasy world where death has broken. Players
begin in Emberfall with no knowledge of other worlds, no knowledge of the larger structure, and no
clear answer for why people and monsters have begun returning after death.

The game should feel like a serious fantasy MMO with mystery, danger, long-term progression, and
occasional weird or comedic side moments. The main tone leans toward SAO/Re:Zero-style mystery and
stakes, with Baldur's Gate-style character scenes and OSRS-style odd side content.

## Core Promise

Players can progress through Aldenfall by following story and quests with minimal forced grind, or by
grinding skills, mobs, drops, gold, and resources for an advantage. Questing unlocks the world.
Grinding deepens your power inside it. Neither path replaces the other.

Design constraints:

- Questing should provide a clear path through each world without requiring excessive mob farming.
- Grinding should give real advantage: XP, gold, rare drops, food, upgrades, cosmetics, prestige.
- Grinding one early world should not let a player trivialize twenty future worlds.
- Access to major worlds, dungeons, resources, and story beats should require world clears or key
  quest progress.
- A player can delay quests and grind heavily, but they eventually need clears to reach new worlds.

## Death Broke

Aldenfall was a world where death behaved normally. Then something changed.

Current known rules for early story:

- Some people die and return.
- Some remember death.
- Some return with missing memories.
- Some return somewhere strange.
- Some deaths still appear permanent.
- Monsters return too quickly.
- Nobody understands the pattern.

This turns MMO respawning into the central story mystery rather than a silent game convention.

World 1 should prove that death is broken. Worlds 2-4 should deepen the pattern. Around Worlds 5-10,
the story can begin showing why some people do not return and who may be manipulating the system.

## World Structure

The game is Aldenfall. Each world has a main city and its surrounding region, named from that city.

World 1:

- Emberfall City
- Emberfall Forest
- Emberfall Lake
- Emberfall Deepwood
- Emberfall Warcamp
- Emberfall Volcano
- Emberfall Dungeon

Players do not initially know these are worlds or floors. They discover the larger structure through
story. Each world should end like a strong TV episode: the local arc resolves, but the ending opens a
larger hook.

## Player Arrival

The player should not simply spawn into the world. Aldenfall starts with an authored, mysterious
arrival sequence: Kingdom Hearts-like in tone, with a short glimpse of the player's normal life
before Aldenfall, then their death, then their materializing/particlizing into Emberfall as if pulled
through somewhere else. The player appears directly in front of Aldric in his forest glade.

Aldric should be surprised. He did not summon the player in any simple tutorial-NPC way. His reaction
should imply that this arrival is unusual even to him, while still letting him quickly become
practical: the player is here, they know nothing, and he needs to teach them how to survive.

This opening should establish three things without explaining them:

- The player came from somewhere or through something.
- The player died before arriving, making death and respawn part of the opening mystery.
- Aldric recognizes that the arrival matters.
- Emberfall is a real place with rules the player must learn, not a menu lobby.

## Skills

Skills secretly cap at **500**. The UI should not expose the cap. Players should simply see their
current level, such as `Magic 103`, and discover that progression continues past familiar milestones.

Progression feel:

- Early levels are fast and satisfying.
- Level 50 feels like commitment.
- Level 99 feels like a huge milestone.
- The first player to pass 99 creates a community discovery moment.
- High levels slow hard and become long-term identity.

Builds should stay OSRS-flexible. There are no hard classes. Players can become pures by focusing
one style, or mains by leveling everything. Gear should be broadly wearable by anyone, with practical
requirements and scaling coming from skill level, weapon type, mastery, and item tier rather than a
class lock.

Health/HP is a core skill/stat and should not be forgotten in progression design.

Equipment should eventually support main hand, off hand/focus, helm, chest, legs, cape, gloves,
boots, necklace, and two ring slots. Jewelry should start as a meaningful but small stat layer, not a
mandatory early optimization wall.

## Ability Mastery

Every active ability should improve through use/mastery, not only through raw skill level. Mastery
upgrades should include mechanical improvements, animation improvements, and VFX improvements.

Examples:

- Meteor starts as one impact, later gains larger radius, lower cooldown, burning ground, then
  multiple meteors over several seconds.
- Whirlwind gains radius, duration, hit pulses, stronger pull/knockback, and a bigger animation.
- Blade Rush gains range, stun duration, cooldown reduction, impact effects, and eventually a
  cooldown refund on kill.
- Fireball gains burn strength, explosion radius, visual upgrades, and later splash behavior.
- Blink gains range, trail effects, and possibly charges.

Players should be able to say, "I main Meteor," "I main Whirlwind," or "I am a Blink mage," and
have that identity reflected by mastery visuals.

## Aldric

Aldric begins as a nobody: homeless, old, sharp, practical, and dismissed by most people. He lives
outside Emberfall City and teaches the player how to survive.

He is not called a warden. That title does not fit.

Long-term, Aldric may be an **architect of worlds**, or someone adjacent to the creation, division,
or repair of Aldenfall's worlds. Do not reveal this in World 1.

World 1 Aldric should only hint:

- He recognizes old symbols.
- He understands that death breaking is dangerous.
- He knows the volcano should not be erupting.
- He is surprised by the player's arrival, but not completely ignorant of what it could mean.
- He avoids his past.
- He shuts down people who almost call him by an old role or name.
- He is disturbed by events that shock everyone else.

After the World 1 mainline reveals that other worlds exist, Aldric should volunteer to help lead the
mission into World 2 alongside the player: the outsider/new arrival who cleared Emberfall Dungeon and
saved the city. He should not provide the actual method of world-to-world transportation immediately.
First, he helps the player explore World 2 and reach its main city. Only after several World 2
mainline quests should he admit he can no longer hide something important and provide or reveal the
travel method the player will need going forward.

Long arc possibility:

1. Homeless mentor.
2. Mysterious guide.
3. Revealed to have impossible authority or knowledge.
4. Fights beside the player for a necessary boss.
5. Appears to turn against the player.
6. Players defeat him or a vessel of him.
7. Later reveal: the real Aldric was taken, copied, corrupted, or used.
8. Players rescue him.
9. He returns as a long-term guide with deeper stakes.

## Hidden Organization

The hidden group should not be introduced directly until around World 5.

Worlds 1-4 should only foreshadow:

- symbols
- repeated phrases
- impossible architecture
- ritual marks
- vanished NPCs
- altered corpses
- dungeon mechanisms
- Aldric reacting and refusing to explain

World 1 should prove that someone or something caused the current crisis, but not name the group.

## First Clears

The first clear of each world should be genuinely hard and require a team.

Before first clear:

- Dungeon and boss are tuned for group coordination.
- World crisis is active.
- Mechanics are at their hardest.
- Server tension is high.

After first clear:

- The world stabilizes.
- The boss/dungeon becomes easier.
- Solo or small-group clears become possible later.
- The first-clear team receives prestige.
- The entire server receives a minor, non-game-breaking reward.

This creates server history without permanently blocking casual players.

## Monetization Note

Do not let monetization shape World 1 design yet. World 1 should be the strongest possible free beta
showcase.

Possible future model:

- Initial release/beta: first worlds free.
- Later free access could be based on a rolling formula, such as first `20% + 5` released worlds.
- Paid/subscribed players access all current worlds.

This needs future validation because access splits can harm MMO friend groups.
