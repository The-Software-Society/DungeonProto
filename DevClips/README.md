# DevClips — the dev-log / social clip archive

Every time we ship something **visual** (a quest, a cutscene, a combat change, a mob, an
environment pass, a UI change…) we capture a **natural, player-like video** of it and file it here.
These become the raw material for the TikTok "Day N: AI building an MMO" series (see `../Social/`).

This is **not** the fast `-qashots` teleport validation pass — that's how the AI *proves the work
functions* and it keeps happening every task, unrecorded. A DevClip is made **after** validation:
the camera moves like a human is playing (walk the quest, spar like a player, glide through the vista).

## Layout

```
DevClips/
  <YYYY-MM-DD>_to_<YYYY-MM-DD>/     one folder per week (Monday–Sunday), auto-named by record_clip.sh
    quests/                          category folders — pick what's easy for a human/AI to browse
    environment/
    combat/
    mobs/
    cutscenes/
    ui/
    <name>.mp4                       one clip per accomplishment, descriptive names
  scripts/                           reusable JSON "clip scripts" the recorder plays (see below)
```

Weeks are Monday–Sunday. Example ranges: `2026-07-13_to_2026-07-19`, then `2026-07-20_to_2026-07-26`.

## How a clip is recorded

1. Do the work and **validate** it with the normal `-qashots` teleport pass (unrecorded).
2. Author (or reuse) a **clip script** in `scripts/` — a JSON path the `NaturalClipDriver` plays:
   `spawn` (start mark) · `moveTo` (walk, camera follows) · `turn` · `wait` · `cutscene`
   (combat steps `attack`/`cast`/`spawnEnemy` get added as we build combat clips).
3. `tools/record_clip.sh <category> <name> scripts/<clip>.json [fps]` — runs the current Mac build
   windowed with `-devclip`, captures frames, and stitches them here via `tools/make_montage.sh`.
4. The AI links the resulting `.mp4` in chat; Jon reviews and gives feedback; we iterate.

## Social use

When a session is about socials, browse the relevant week's folders here, pick the strongest
moments, and assemble posts using `../Social/` (Remotion project + episode notes) and the social
memories (editorial rules, EP hooks). Document the process as we go for the "building an MMO with
AI" series.
