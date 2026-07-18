# AldenFall — Session Handoff (visual overhaul in progress)

Last updated end of the 2026-07-17→18 session. Read this + the memories, then start with the
**Immediate next action** below.

---

## 0. IMMEDIATE NEXT ACTION (say this to kick off)

> "Build the ColorLab and let's tune the grass + trees together."

This is **task #95**. We must stop tuning color one-value-at-a-time with 20-minute full builds —
it plateaued and then regressed (grass went dead-grey, trees are flat "spinach wrap," the ground
splat still has a neon-green band). The fix is a **ColorLab** editor tool that renders a **grid of
grass × tree × ground color variants side-by-side in one PNG** (seconds, not builds), so we compare
and pick the balance, then apply the winner in ONE coordinated change. Build it FRESH with full
context, render the grid, iterate live with Jon, then ship.

---

## 1. CURRENT STATE (what's shipped / what works / what's broken)

- **Last shipped build:** commit `d9cc1ac` on `main`. itch: `thesoftwaresociety/aldenfall`
  (mac + windows channels updated). Render server: `aldenfall-test-server.onrender.com`.
- **MP server: STABLE and playable.** (A Phase-A regression crash-looped it; fixed — see gotcha #2.)
  Health check: `curl -w "%{http_code}" https://aldenfall-test-server.onrender.com/` → 200 = up.
- **WORKS / solid:** placement + junk (Phase A, data-verified clean, 0 conflicts); mountains (no more
  white peaks); rocks/dirt palette; server MP.
- **BROKEN / the active problem:** whole-scene COLOR. Grass blades read dead-grey; terrain splat still
  shows a neon-green band (esp. near water); tree foliage = flat dark "spinach wrap." Rocks + dirt are
  correct. This is a **scene-wide saturation/tone** problem, not a single hue — hence ColorLab + a
  possible URP color-grade, and giving trees life via a leaf-material tint.

---

## 2. THE COLOR PROBLEM — files, knobs, and approach (task #95)

All in `Assets/Scripts/Editor/ArenaBuilderEmberfall.cs` unless noted:
- **Grass blade color** = the vertex colours in `MakeGrassTuftPrefab` (~line 1209): `dark`/`light`.
  These ARE the rendered grass (terrain mesh-details ignore the DetailPrototype healthy/dry tint).
  Current: `dark (0.20,0.27,0.14)`, `light (0.33,0.45,0.24)` → too grey/dead.
- **Grass texture:** `Assets/Textures/Ground/grass_tuft_desat.png` — a GREY luminance mask I generated
  from the original (which was itself saturated green `55,96,33`, unfixable by tint since multiply only
  darkens). Regenerate with the python snippet in the session log if hue bias needs changing.
- **Ground splat (undergrass):** `TL_Grass` in `BuildEmberfallTerrain` (~line 928). Current
  `(0.22,0.27,0.16)` (staged de-neon) — still renders neon where blades are sparse; needs desaturating
  AND matching the blades.
- **Tree foliage ("spinach wrap"):** leaf material in `TreeKitImporter.BuildMaterials` — the leaf
  `_BaseColor` (~line 238). Tinting it needs a re-run of `BakeTreeKitPrefabs` (re-bakes the LOD
  prefabs). Also the flat/blobby look is partly LOD decimation of alpha leaf-cards.
- **Mountains (already fixed):** `BuildMountainRange` sets `MountainHeightBlend` `_RockTop`=185 etc.
- **ColorLab plan:** new editor file; reuse the render helper pattern at `TreeKitImporter.cs` ~360-368
  (`UniversalRenderPipeline.SingleCameraRequest` → RenderTexture → `EncodeToPNG`). Render a grid PNG:
  each cell = ground quad (splat colour) + a few `GrassTuft` prefabs (vertex dark/light) + one
  `island_tree_01` LOD prefab (leaf tint), under the real sun/ambient. Sweep grass-green × tree-tint ×
  splat combos. Pick, apply, build once, verify, ship. Then add **varied grass patches** (2-3 detail
  tints, not one uniform tuft) to kill the "mowed lawn" look.

---

## 3. PHASE PLAN (the agreed overhaul — see memory `visual-overhaul-phases`)

Dependency-ordered; Jon wants ALL of it, hard-but-proper, no shortcuts.
- **Phase A — Placement truth: ✅ DONE.** Every object flows through the `PlaceMeshyProp` gate
  (rejects wrong-ground/overlap), rocks slope-conform + convex colliders, all primitive-scenery + cube-
  tree fallbacks removed, mobs out of water. Tool: `DumpWorldAudit` → `WorldData/world_audit.txt` scans
  every mesh each bake (762 primScenery/412 float/208 fatCol → ~0). Data authority: `WorldManifest`
  → `WorldData/manifest.md` + `ground_map.txt` + per-category json + `conflicts.json` (0 conflicts).
- **Phase B — Terrain/grass/color: IN PROGRESS (this is where we are).**
  - Done: mountains de-whited; palette warmed.
  - ACTIVE: the grass/tree/ground COLOR (task #95, ColorLab) ← do this next.
  - Still TODO (own focused pass, risky): **macro terrain reshape** (big hills / open fields / real
    slopes vs the current ~2-3m lumpy noise at `ArenaBuilderEmberfall.cs` ~866-869; ONLY in the wild
    ring, ramp amplitude down near lake/rivers/city/road/glade so nav+water stay intact; was rolled
    back twice — tasks #12/#18). Plus multi-species varied grass.
- **Phase C — Real water shader (#93):** water is still a flat lit sheet in every shot; biggest single
  visual lift. Depth-fade clarity, normal-map waves/flow, shoreline foam, clean edges (BDO refs). Fix
  flat-white-cutout waterfalls. Also fold in the last 2 shore-edge in-water ferns + water-boundary.
- **Phase D — Lighting + perf (#94):** frame spikes (avg ok, 1%-lows bad). `-perf` NaturalClipDriver
  sweep; the world is CPU/geometry-bound (see `perf-cpu-bound-breakthrough` memory). A scene color-grade
  (URP Volume ColorAdjustments: saturation −, slight warm) may also be the cleanest fix for #2's color.

---

## 4. BUILD / DEPLOY / QA COMMANDS

Editor MUST be closed for batchmode. `UNITY=/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app/Contents/MacOS/Unity`
- **Re-bake scene** (needed after any ArenaBuilder*/scene-geometry change):
  `$UNITY -batchmode -quit -nographics -projectPath . -executeMethod AldenFall.Editor.ArenaBuilder.BuildDemoHeadless -logFile <log>`
  → check `WORLD CONFLICTS:` (must be 0) + `WORLD AUDIT:` lines.
- **Desktop builds:** `MultiplayerBuildTools.BuildMacDesktop` / `.BuildWindowsDesktop` /
  `.BuildAllDesktopCI`. **Server:** `.BuildLinuxDedicatedServer` (runs `ServerSceneStripper`).
  NOTE: full desktop builds take ~8-10 min and can hit the 10-min bash timeout — build Mac and Windows
  as SEPARATE commands, and confirm success via the exe mtime if a command times out.
- **QA (visual, teleport screenshots, 4-way N/E/S/W):**
  `Builds/Mac/AldenFall.app/Contents/MacOS/AldenFall -qashots -qadir <dir>` (auto-hosts; needs a real
  display, not `-nographics`; run in background, it self-quits). **ALWAYS look at a QA shot before
  claiming a visual is fixed.**
- **Deploy:** itch `./Deploy/itch/push-itch.sh thesoftwaresociety/aldenfall` (background, ~5 min upload);
  Render `SSL_CERT_FILE=$(python3 -m certifi) python3 tools/prepare_render_server.py && git push origin main`
  (Render redeploys on push; a push always triggers a ~2-min redeploy blip even if the server bundle is
  unchanged).

---

## 5. HARD-WON GOTCHAS (do not relearn these)

1. **VERIFY VISUALS IN QA BEFORE CLAIMING THEM.** Most pain this session came from saying "fixed" off
   the data/one-shot without looking. Jon is the visual QA; render a shot or ask.
2. **Keep the dedicated server LEAN.** Convex mesh colliders on ~500 rocks crash-looped the 512MB Render
   starter plan (HTTP flaps 200/502 = crash loop, NOT a deploy blip). `ServerSceneStripper` now converts
   MeshCollider→BoxCollider before stripping meshes. Any new mesh colliders → re-check server stays up.
   No Render CLI/logs locally; diagnose via the health-check curl.
3. **HARD RULE:** never a cube/sphere/primitive as the VISUAL of a real object. A gate-rejected real prop
   is SKIPPED, never faked (cube-tree fallbacks are gone — keep them gone).
4. **Data authority:** the world is baked C# (`ArenaBuilder*`), placement JSON is a dead export. Every
   prop must flow through `PlaceMeshyProp`. Read `WorldData/` to reason about the world.
5. **NO EM DASHES.** Commit messages end with the Co-Authored-By line. Branch off main only if asked;
   this repo commits to main (Render deploys on push).

---

## 6. MEMORIES TO READ (auto-load, but key ones)

`visual-overhaul-phases` (the phase plan + color state) · `world-manifest-data-authority` (the data
system + audit tools) · `perf-cpu-bound-breakthrough` + `perf-streaming-architecture` (Phase D) ·
`poly-haven-forest-kit` (tree/rock assets) · `wss-nettransform-navmesh-gotcha` (server/nav + the
mesh-collider crash). Plus repo `CLAUDE.md` (HARD/GOLDEN/PERF rules) and `WORLD1_SOURCE_OF_TRUTH.md`.

---

## 7. OPEN TASKS

#95 ColorLab (grass+tree color) ← NEXT · #92 Phase B terrain macro-reshape + varied grass ·
#93 Phase C water shader · #94 Phase D lighting+perf. (#82/#83 perf streaming/density also open.)
