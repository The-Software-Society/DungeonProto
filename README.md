# AldenFall

A multiplayer action RPG prototype built in Unity 6 with Mirror networking. Players arrive in
Emberfall, a valley under a waking volcano, and progress through use-based skills, action combat,
quests and a shared dungeon floor that the whole server clears together.

AldenFall is for players who like long-horizon skill progression from classic sandbox MMOs mixed
with modern action combat (dodge rolls, knockback, tab-targeted spells), and for Unity developers
who want a readable, data-driven example of a small server-authoritative online RPG.

## What is in it

- **World 1, Emberfall:** a walled city with enterable buildings and NPCs, a lake and river
  system, a forest ring, a volcanic approach and an eight-room volcano dungeon with a boss.
- **Skills that level by use:** Attack, Strength, Defence, Magic, Ranged, Health, Fishing,
  Cooking, Mining, Smithing, Woodcutting and Fletching.
- **Action combat:** melee, magic and archery styles, dodge with i-frames, abilities with
  mastery ranks, enemy AI with patrol, chase, jump and charge attacks.
- **Quests and cutscenes:** a mainline quest spine plus side quests, data-driven cutscenes and a
  voiced first-login intro.
- **Multiplayer:** Mirror with a dedicated Linux server build, parties, duels, shared loot and a
  server-wide world state (the eruption) driven by the floor boss.
- **Data-driven content:** monsters, items, abilities, skills, recipes, quests, NPCs and tuning
  live in JSON under `Assets/StreamingAssets/` and hot-reload in the editor.

## Built with

- Unity **6000.3.2f1** (Unity 6), Universal Render Pipeline 17.0.3, Input System, TextMeshPro,
  AI Navigation, Cinemachine
- [Mirror](https://github.com/MirrorNetworking/Mirror) 96.10.3 networking (MIT), using
  SimpleWebTransport so a dedicated server can run behind HTTPS/WSS hosts
- Python 3 tooling in `tools/` for the asset pipelines

## How to open and play

1. Install Unity Hub and Unity **6000.3.2f1** (add *Linux Build Support (Mono)* only if you want to
   build the dedicated server).
2. Clone this repo and open the folder in Unity Hub (**Add project from disk**). The first import
   takes a while; Unity downloads the packages in `Packages/manifest.json`.
3. If prompted, import **TMP Essential Resources** (Window > TextMeshPro).
4. Open `Assets/Scenes/EmberfallDemo.unity`, or rebuild it from code with
   **AldenFall > Build Emberfall Demo**.
5. Press **Play**. On the connect screen, enter the test password set on the scene's
   `AldenNetworkManager` (`requiredPassword`) and choose **Host** to play locally. Desktop clients
   and the Linux dedicated server build from **AldenFall > Multiplayer**.

**Note on 3D models:** the AI-generated character and prop models (`Assets/Models/`, several GB)
are not stored in git. The scene and prefabs reference them, so a fresh clone opens and runs but
those objects show as missing meshes until the models are generated. You can regenerate them with
your own Meshy account (see *Asset pipelines*). Code, data, UI, terrain, textures, Poly Haven
foliage prefabs and the Mirror networking stack are all included.

## Controls

| Input | Action |
|---|---|
| WASD | Move |
| Double-tap W/A/S/D | Dodge roll (i-frames) |
| Space | Jump, swim up, vault out of water |
| Left Shift | Run |
| Left Click | Target enemy (click elsewhere to clear) |
| Tab | Cycle targets |
| 1-9 | Action bar abilities |
| V | Cycle combat style (melee, magic, archery) |
| E | Interact, talk, gather |
| F | Fish |
| I or B | Inventory |
| C | Character and equipment |
| K | Skills |
| T | Combat skill trees |
| Q | Quest tracker |
| P | Party invite (Y / N to answer) |
| O | Duel request (Y / N to answer) |
| Right Mouse + drag | Rotate camera |
| Scroll wheel | Zoom |
| Esc | Close panels |

## Project layout

```
Assets/
  Scripts/          game code (AI, Combat, Player, Progression, Items, Quests, Multiplayer, UI, Editor)
  StreamingAssets/  JSON content and tuning (monsters, items, quests, abilities, config/*.json)
  Prefabs/          gameplay prefabs, generated visuals, Poly Haven tree/rock/plant LOD prefabs
  Materials/ Shaders/ Textures/ UI/   art built or processed by editor tooling
  Scenes/           EmberfallDemo.unity (built by AldenFall > Build Emberfall Demo)
  Mirror/           Mirror networking (third party, MIT)
  ThirdParty/       UnityMeshSimplifier (third party, MIT)
Deploy/             dedicated server Docker setup and itch.io push script
tools/              Python asset pipelines (Meshy, SayMotion, Gemini TTS, Veo) and capture scripts
WorldData/          exported world layout data used for audits
*.md                design docs and plans (see ROADMAP.md)
```

## Asset pipelines (optional)

The tools that call paid APIs read keys only from environment variables or a local, gitignored
`.env.local`. Copy `.env.example` to `.env.local` and fill in your own keys. No key is stored in
this repository.

```sh
python3 tools/meshy_pipeline.py --dry-run --all   # plan + credit estimate, nothing generated
python3 tools/meshy_pipeline.py --all             # generate models into Assets/Models/
```

Then in Unity run **AldenFall > Import Meshy Characters** and rebuild the demo scene.

## Asset credits

- **3D characters, props, buildings and some trees/rocks:** generated with
  [Meshy](https://www.meshy.ai) AI from the text prompts in `tools/meshy_pipeline.py`.
  3D models generated with Meshy (meshy.ai).
- **Plant, tree and rock models, ground textures and the forest HDRI:**
  [Poly Haven](https://polyhaven.com), CC0 1.0 (authors include Rob Tuytel and Andreas Mischok).
  See the `ASSET_PROVENANCE.md` files.
- **Intro voice lines:** generated with Google Gemini text-to-speech (`tools/tts_aldric.py`).
- **Intro video:** generated with Google Veo (`tools/veo_intro.py`).
- **UI sprites, icons, ground and water textures:** generated procedurally by the editor tools in
  `Assets/Scripts/Editor/` or rendered from the project's own models.
- **Networking:** Mirror (MIT) and its bundled transports; Mirror's example scenes include Kenney
  and OpenGameArt CC0 assets.
- **Mesh simplification:** UnityMeshSimplifier by Mattias Edlund (MIT).

AldenFall's design is inspired by classic sandbox MMOs (use-based skills, a 99 level cap), but the
names, characters, places, story, code and art here are original or used under the licenses above.
It is not affiliated with or endorsed by any other game or studio.

## License

MIT, Copyright (c) 2026 The Software Society. Third-party components keep their own licenses;
see `THIRD_PARTY_NOTICES.md`.

## Roadmap

See [ROADMAP.md](ROADMAP.md).
