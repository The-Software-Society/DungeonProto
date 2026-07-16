# AldenFall — working guide

Online action RPG (Unity 6000.3.2f1, URP, Mirror). Current playable world: **Emberfall (World 1)**.
Design canon lives in `ALDENFALL_GAME_PLAN.md` (game-wide) and `WORLD1_SOURCE_OF_TRUTH.md` (Emberfall).
Ships to **itch** (desktop) + a **Render** dedicated server. Each work cluster ends in build + deploy.

## GOLDEN RULE — draw one thing many times

When you place **many of the same thing** (shrubs, rocks, stones, grass, trees, cobbles, fences,
identical mobs, tiles…) the GPU must draw ONE mesh N times, not N unique objects. Concretely:
- **Share one material per type** (not a new material per instance) — `Mat()` caches by name and sets
  `enableInstancing`; reuse the same name. Never generate a unique material/mesh per placement.
- **Reuse one source mesh/prefab** per type; vary only transform (position/rotation/scale) + tint.
- **Mark static** (`isStatic = true`) so static batching / the GPU Resident Drawer can combine them.
- The engine then instances them: Unity 6 **GPU Resident Drawer** (URP asset `GPUResidentDrawerMode`,
  needs Forward+) draws each mesh N times + does GPU occlusion culling automatically.
- Stream/cull by locale (interest management for networked entities; camera layer-cull for scatter) —
  see `perf-streaming-architecture` memory. A per-type unique material or a per-instance Update() is a
  smell; if you catch yourself writing one, batch it instead.

## The loop for every visual task

1. **Build** what changed.
2. **Validate** with the fast `-qashots` teleport pass (screenshots, unrecorded) — this is the AI's
   proof the work functions. It never stops and is not the deliverable.
3. **Record a natural DevClip** of it (see below) — player-like, for the dev-log / social series.
4. **Link the clip in chat** for Jon to review; take feedback; iterate.
5. **Deploy** (itch + Render) when the cluster is done.

## Record DevClips (after validation)

A DevClip is a **natural, player-like** capture — the opposite of the teleport validation pass:
- Quest → play the cutscene and run the quest like a player would.
- Combat → spawn 2 players and actually fight (autos, skills, movement).
- Environment → smooth camera a human can follow through the space.

Mechanism: `NaturalClipDriver` (`Assets/Scripts/Debug/NaturalClipDriver.cs`) boots a live host and
plays a JSON **clip script** (`DevClips/scripts/*.json`: `spawn`/`moveTo`/`turn`/`wait`/`cutscene`;
combat steps added as we build them) with the normal follow-camera, capturing frames.

```
tools/record_clip.sh <category> <name> DevClips/scripts/<clip>.json [fps]
```

Archives to `DevClips/<YYYY-MM-DD>_to_<YYYY-MM-DD>/<category>/<name>.mp4` (week = Mon–Sun, auto-named).
Categories: quests, environment, combat, mobs, cutscenes, ui, … See `DevClips/README.md`. On a
**socials** session, browse that week's folders and assemble posts with `Social/` (Remotion) — the
"Day N: AI building an MMO" series.

## Build / deploy commands

Unity: `/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app/Contents/MacOS/Unity`.
**The editor must be CLOSED** for `-batchmode` builds (else the project lock silently exits).

```
UNITY -batchmode -quit -nographics -projectPath . -executeMethod <method> -logFile <log>
```
- `AldenFall.Editor.ArenaBuilder.BuildDemoHeadless` — re-bake the scene (~20s). REQUIRED whenever
  editor/builder code changes (ArenaBuilder*, SceneSetup, HUDBuilder, spawn points, world geometry).
- `AldenFall.Editor.MultiplayerBuildTools.BuildAllDesktopCI` — Mac + Windows (also `BuildMacDesktop`).
- `AldenFall.Editor.MultiplayerBuildTools.BuildLinuxDedicatedServer` — the Render server.
- Runtime-only script/JSON/StreamingAssets changes need a build but **no re-bake**.

Deploy: `./Deploy/itch/push-itch.sh thesoftwaresociety/aldenfall` (Mac+Windows) ·
`python3 tools/prepare_render_server.py && git push origin main` (Render redeploys on push).

Validate: `Builds/Mac/AldenFall.app/Contents/MacOS/AldenFall -qashots -qadir <dir>` (auto-hosts,
teleport screenshots, functional self-test). Player.log: `~/Library/Logs/TheSoftwareSociety/AldenFall/`.

Offline compile check while the editor is open (batch is locked): drive Unity's bundled Roslyn — see
memory `offline-unity-compile`. Google/Meshy API scripts need `SSL_CERT_FILE=$(python3 -m certifi)`.

## Story canon guardrails (World 1)

Do **not** let dialogue/UI leak the meta structure — players discover it through story:
- **Never** name "floors"/"worlds" or the boss/quest meta in the open world or intro.
- **Death/respawn is the central mystery** ("death broke") — hint, never explain.
- The volcano crisis is a **deeper-questline** reveal — not on spawn.
- Aldric: homeless old hermit, sharp/practical, surprised by the arrival, avoids his past, shuts down
  old-role references. Not a "warden," not a hero giving a speech. (See `ALDENFALL_GAME_PLAN.md`.)

## Gotchas

- **Intro plays every login** (no account persistence yet). `IntroCutsceneTrigger` has no seen-gate;
  it skips only under `-devclip`/`-qashots`/batchmode. Gate per-account once Supabase lands.
- Jon is the **visual QA** — headless QA sees screenshots, not motion/audio/feel. Surface anything
  that needs his eyes/ears (cutscenes, voice, video playback).
- The world is **baked C#** (ArenaBuilder*), not the placement JSON (that export is dead at runtime).
- Mirror weaver + `NetworkTransformReliable` for anything server-authoritative (see MP audit memory).
