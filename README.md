# DungeonProto

An OSRS-inspired MMO prototype built in Unity 6, combining sandbox freedom with action combat.

## Project Status

**Phase 1: Foundation** - Rebuilding core systems

## Quick Start

1. Open project in Unity 6000.3.2f1
2. Unity will import packages (URP, Input System, TextMeshPro, etc.)
3. Create a new scene or open existing
4. Go to **DungeonProto > Setup New Scene** to auto-create player, camera, enemy, and ground
5. Press Play!

## Controls

| Input | Action |
|-------|--------|
| WASD | Move |
| Space | Jump |
| Left Shift | Run |
| Left Click | Target enemy |
| Tab | Cycle targets |
| Right Click + Mouse | Rotate camera |
| Scroll Wheel | Zoom camera |
| 1 | Cast Fireball |

## Project Structure

```
Assets/
├── Scripts/
│   ├── AI/           # Enemy AI
│   ├── Combat/       # Health, targeting, projectiles
│   ├── Player/       # Character controller, camera
│   ├── UI/           # HUD, health bars
│   ├── World/        # Tile system for city layout
│   └── Editor/       # Editor tools
├── Prefabs/
├── Scenes/
├── Materials/
└── Animations/
```

## Editor Tools

- **DungeonProto > Setup New Scene** - Quick scene setup
- **DungeonProto > City Layout Editor** - Visual city mockup tool
- **DungeonProto > Create Player/Enemy Prefab** - Generate prefabs

## City Mockup System

The city layout uses color-coded tiles for rapid prototyping:

| Color | Tile Type |
|-------|-----------|
| Gray | Cobblestone (streets) |
| Green | Grass |
| Brown/Tan | Buildings |
| Dark Gray | Walls |
| Blue | Water |
| Orange | Market stalls |
| Light Brown | Dirt paths |

## Implemented Features

- [x] WASD movement with camera-relative direction
- [x] Third-person camera (BG3 style - right click to rotate)
- [x] Jump with ground detection
- [x] Walk/Run toggle
- [x] Click and Tab targeting
- [x] Health system with events
- [x] Fireball with homing to target
- [x] Enemy AI (patrol, chase, melee attack)
- [x] Death and respawn
- [x] Basic HUD structure

## Roadmap (from GDD)

See the full Game Design Document for detailed plans.

**Phase 2: Core Systems**
- Skill system framework
- Inventory (weight-based)
- Equipment system
- Starting City (Millbrook)

**Phase 3: World Building**
- Training Grounds zone
- Dungeon 1
- Monster spawning
- Roaming Elite (Hollow Knight)

## Dependencies

- Unity 6000.3.2f1
- Universal Render Pipeline 17.0.3
- Input System 1.11.2
- TextMeshPro 3.2.0
- Cinemachine 3.1.2
- AI Navigation 2.0.4
