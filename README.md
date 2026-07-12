# AldenFall

An OSRS-inspired MMO prototype built in Unity 6 — sandbox freedom with action combat.

## Quick Start

1. Open the project in Unity 6000.3.2f1
2. Open `Assets/Scenes/EmberfallDemo.unity` (or rebuild it: **AldenFall > Build Emberfall Demo**)
3. Press Play

## Controls

| Input | Action |
|-------|--------|
| WASD | Move |
| Double-tap W/A/S/D | Dodge roll (i-frames) |
| Space | Jump / swim up / vault out of water |
| Left Shift | Run |
| Left Click | Target enemy (click anywhere else to clear) |
| Tab | Cycle targets |
| 1 | Fireball (homing at target) |
| 2 | Ember Nova (point-blank AoE burst) |
| Right Click + Mouse | Rotate camera |
| Scroll Wheel | Zoom camera |

Swimming is automatic in deep water (chest-deep and beyond). Goblins won't follow you in.

## The Demo — Emberfall Courtyard

A torch-lit walled arena at dusk: central brazier plaza, raised terrace, swimmable
pond, crates and rubble, three goblins with respawns. Built entirely by editor
tooling — no hand-placed scene state.

## Editor Menu

- **AldenFall > Build Emberfall Demo** — build + bake + save the demo scene
- **AldenFall > Build Complete Scene** — legacy Millbrook castle city build
- **AldenFall > Import Meshy Characters** — wire generated models into prefabs
- **AldenFall > Fix Combat System** — repair layers/refs on an existing scene
- **AldenFall > Advanced > ...** — city-only / navmesh-only / manual tools

Headless builds (CI or CLI):

```sh
Unity -batchmode -quit -projectPath . \
  -executeMethod AldenFall.Editor.ArenaBuilder.BuildDemoHeadless
```

## Meshy Asset Pipeline

Characters (rigged + animated) and props are generated through the
[Meshy API](https://docs.meshy.ai):

```sh
# key in .env.local (MESHY_API_KEY=...) or ~/.meshy_key
python3 tools/meshy_pipeline.py --dry-run --all   # show plan + credit estimate
python3 tools/meshy_pipeline.py --all             # generate everything
```

Output lands in `Assets/Models/`. The run is resumable — completed Meshy tasks are
recorded in `tools/meshy_state.json` and never re-billed. After generation, open
Unity and run **AldenFall > Import Meshy Characters**, then rebuild the demo
scene: player/goblin visuals upgrade from primitives to the generated models
automatically.

## Architecture Notes

- `GameLayers` — single source of truth for layer masks; every system resolves
  layers through it (falls back to Default when a layer is missing)
- `Projectile` — swept SphereCast hit detection; no trigger-physics dependence
- `HitEffect` — strictly visual; damage/knockback belong to the damage dealer
- `TargetingSystem` — resolves child colliders to entities via `GetComponentInParent<Health>`
- Water — full-capsule submersion depth with enter/exit hysteresis (no surface
  flicker), momentum cleared on exit
- Enemy AI — state machine: patrol, chase, melee, jump attack, charge attack
  (windup → charge → wall-stun), off-mesh-link traversal

## Project Structure

```
Assets/
├── Scripts/
│   ├── AI/           # EnemyAI state machine, spawner
│   ├── Combat/       # Health, targeting, projectiles, abilities, FX
│   ├── Player/       # Controller (move/dodge/swim), camera, death
│   ├── UI/           # HUD, enemy health bars
│   ├── World/        # GameLayers, torch flicker, tile mockup tools
│   └── Editor/       # ArenaBuilder, SceneSetup, MeshyCharacterImporter
├── Models/           # Meshy-generated characters + props (gitignored source of truth: tools/meshy_pipeline.py)
├── Prefabs/          # Fireball, Goblin, Generated/ visuals
├── Scenes/           # EmberfallDemo.unity
└── Materials/        # Emberfall/ scene materials
tools/
└── meshy_pipeline.py # Meshy API generation pipeline
```

## Dependencies

- Unity 6000.3.2f1 · URP 17.0.3 · Input System · TextMeshPro · AI Navigation
- Meshy API (Pro tier) for asset generation
