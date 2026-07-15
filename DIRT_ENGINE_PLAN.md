# Dirt / Terrain Realism — Engine & Asset Catalog

Goal: ground that reads like the references (stylized-ARPG dirt trails, New-World-style) — **smooth
mottled earth with natural damp/dry variation, occasional real rocks, grass feathering the edges,
and cobblestone emerging from the dirt as you approach the city** — NOT flat Minecraft texture and
NOT a carpet of white "hat patch" gravel clumps.

## The engine (what makes dirt look real)

Real dirt is a **layered composition**, not one thing:

1. **Rich tiling albedo** — `GroundTextureGenerator.Dirt()` bakes the base look:
   - fbm base mottle (dark↔mid↔dry-light brown)
   - **low-freq damp/erosion patches** (`wetN`) that darken hollows — the single biggest "real dirt" cue
   - small **earth-toned pebble specks** (warm, NOT the old light-grey "stone" that read as white gravel)
   - matte (diffuse alpha zeroed — terrain smoothness comes from that alpha; opaque = wet mirror).
2. **Terrain splat blending** — 6 layers (`TL_Grass/Sand/Soot/Dirt/Rock/Cobble`), weight-painted per cell.
   Roads/city = dirt; gate approaches ramp to **cobble**; shores = sand; volcano corridor = soot.
3. **Mud/damp decals** (`ScatterRoadDetail`) — a few small, subtle, ground-hugging dark-earth patches
   for wet spots. Sparse + small (over-doing this = black blotches).
4. **Real rocks, earth-toned** — `EarthTint()` warm-darks placed boulders so roadside/field stones sit
   IN the dirt instead of popping as bright white granite.
5. **Grass at the edges** — GPU detail grass runs up to the road, feathering the dirt→grass seam.
6. **NO detail-mesh "speck carpet" on the road** — tried it (DirtTuft); it reads as blotchy angular
   quads. The albedo mottle + rocks + decals carry the depth instead.

### Anti-patterns (removed)
- `PebblePatch` white gravel clumps scattered on roads → the "hat patches" Jon flagged. **Gone.**
- Light-grey pebble specks baked into the dirt texture → read as white flecks. **Now earth-toned.**
- Dense flat dirt-tuft detail carpet → blotchy quads. **Removed; texture carries it.**

## Cobblestone city transition

- `GroundTextureGenerator.Cobble()` — jittered-grid domed stones + dark earth mortar gaps.
- `TL_Cobble` terrain layer, painted on the road **just outside each gate** (|z| ∈ [55, 71]),
  full at the wall, fading to dirt over ~16 m → manmade street eases into the natural trail.
- FUTURE: scatter a few loose `Cobblestone` mesh tiles at the seam so cobbles physically emerge from
  the dirt (half-buried), and stone-stair transitions where the ground steps up (ref images 26/27).

## Asset catalog (Meshy) — for dirt/roadside realism

| Asset | Status | Use |
|-------|--------|-----|
| `Cobblestone` | generating | loose cobble tiles half-buried at the dirt→cobble seam; courtyard patches |
| `Fence` | ✅ generated | wooden rail fence lining roads / penning yards near the city + camps |
| `DirtShrub` | queued | dry roadside weed clumps tucked at the road edge (gated behind HasMesh) |
| `MossBoulder`/`BoulderCluster` | ✅ (earth-tinted) | roadside + field stones |
| `LavaRockGround` | ✅ | volcanic-approach dark basalt |

FUTURE candidates (not yet generated): wooden signpost/mile-marker, cart-rut plank, hay/straw patch
(city entrances, ref 25), broken barrel/crate debris, tree-root gnarls crossing the path.

## Where it lives (code)
- `Assets/Scripts/Editor/GroundTextureGenerator.cs` — `Dirt()`, `Cobble()`, `Rock()`, `Grass()`, `Sand()`, `Soot()`
- `ArenaBuilderEmberfall.cs` — terrain splat weights (incl. cobble band), `ScatterRoadDetail` (mud+rocks+shrub),
  `EarthTint()`, detail-grass density.
- Prompts: `tools/meshy_pipeline.py` (`Fence`, `Cobblestone`, `DirtShrub`).
