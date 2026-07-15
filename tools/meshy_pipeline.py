#!/usr/bin/env python3
"""
Meshy asset pipeline for AldenFall.

Drives the Meshy API end-to-end:
  characters: text-to-3D (T-pose) -> refine (PBR) -> auto-rig -> animation clips
  props:      text-to-3D -> refine (PBR)

Downloads FBX (Unity-native) + GLB into Assets/Models/.

Resumable: every completed Meshy task id is recorded in tools/meshy_state.json,
so re-running never re-bills a finished step.

Usage:
  python3 tools/meshy_pipeline.py --dry-run          # show plan, no credits spent
  python3 tools/meshy_pipeline.py --characters       # generate Hero + Goblin
  python3 tools/meshy_pipeline.py --props            # generate props
  python3 tools/meshy_pipeline.py --all

API key: ~/.meshy_key file or MESHY_API_KEY env var.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.request
import urllib.error
from pathlib import Path

API_BASE = "https://api.meshy.ai"
REPO_ROOT = Path(__file__).resolve().parent.parent
STATE_PATH = REPO_ROOT / "tools" / "meshy_state.json"
MODELS_DIR = REPO_ROOT / "Assets" / "Models"

POLL_INTERVAL_S = 10
POLL_TIMEOUT_S = 45 * 60

# ---------------------------------------------------------------------------
# Asset specs
# ---------------------------------------------------------------------------

# action_id values from https://docs.meshy.ai/en/api/animation-library
CHARACTERS = {
    "Aldric": {
        "prompt": (
            "stylized elderly wise hermit man, long flowing grey beard and weathered "
            "wrinkled face, tattered brown hooded robe with a rope belt, holding a wooden "
            "walking staff, hunched but dignified, kind serious eyes, game-ready character, "
            "standing in T-pose, clearly defined arms and legs"
        ),
        "height_meters": 1.7,
        "animations": {"Idle": 0, "Walk": 30, "Cast": 125, "Hit": 174, "Death": 8},
    },
    "Daughter": {
        "prompt": (
            "stylized young human peasant girl child, simple linen dress, long brown hair, "
            "frightened innocent expression, small child proportions, game-ready character, "
            "standing in T-pose, clearly defined arms and legs"
        ),
        "height_meters": 1.3,
        "animations": {"Idle": 0, "Walk": 30},
    },
    "Hero": {
        "prompt": (
            "stylized fantasy adventurer knight, blue and silver light armor with "
            "cloth hood and leather straps, heroic proportions, clean silhouette, "
            "game-ready character, standing in T-pose, clearly defined arms and legs"
        ),
        "height_meters": 1.8,
        "animations": {
            "Idle": 0,
            "Walk": 30,       # Casual_Walk
            "Run": 14,        # Run_02 (straighter in-game than RunFast)
            "Attack": 4,      # Attack
            "Cast": 125,      # Charged_Spell_Cast
            "Hit": 174,       # Hit reaction family
            "Death": 8,       # Dead
            "Jump": 13,       # Jump_Run
            "Swim": 569,      # Swim_Forward
            "SwimIdle": 568,  # Swim_Idle
            "Dodge": 160,     # Roll/Stand dodge family
            "Eat": 342,       # Stand_and_Drink (food heal; no fishing anim in library — Cast is the fishing fallback)
            "DanceHipHop": 591,   # Hip_Hop_Dance (player /dance emote)
            "DanceGangnam": 74,   # Gangnam_Groove
            "DanceShake": 82,     # Shake_It_Off_Dance
        },
    },
    "Goblin": {
        "prompt": (
            "stylized fantasy goblin warrior, mottled green skin, crude leather "
            "armor, big ears, hunched but bipedal humanoid with clearly defined "
            "arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 1.4,
        "animations": {
            "Idle": 0,
            # v7: real dance mocap for the pond troupe (library has no robot/
            # twerk — breakdance + shake-it-off are the stand-ins)
            "DanceHipHop": 591,   # Hip_Hop_Dance
            "DanceBreak": 395,    # Breakdance_1990
            "DanceShake": 82,     # Shake_It_Off_Dance
            "DanceGangnam": 74,   # Gangnam_Groove (bench: swap in any time)
            "DanceFunny": 22,     # FunnyDancing_01 (bench)
            "Walk": 112,      # Monster_Walk
            "Run": 14,        # Run_02
            "Attack": 97,     # Left_Slash
            "Hit": 174,
            "Death": 8,
            "Jump": 13,
        },
    },
    # v7.1: dedicated dancing goblin — the old Goblin's Meshy tasks expired
    # (can't add clips to a dead rig) and hand-authored euler curves fought
    # the rig's axis conventions. New character, full mocap dance kit, and a
    # party look that separates the troupe from the aggro scrappers.
    "GoblinDancer": {
        "prompt": (
            "stylized fantasy goblin party dancer, mottled green skin, big ears, "
            "colorful festive orange and teal vest with beaded necklaces, bare "
            "feet, cheerful grin, bipedal humanoid with clearly defined arms "
            "and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 1.4,
        "animations": {
            "Idle": 0,
            "DanceHipHop": 591,   # Hip_Hop_Dance — "modern moves"
            "DanceBreak": 395,    # Breakdance_1990 — robot-slot stand-in
            "DanceShake": 82,     # Shake_It_Off_Dance — twerk-slot stand-in
            "DanceGangnam": 74,   # Gangnam_Groove (bench)
            "DanceFunny": 22,     # FunnyDancing_01 (bench)
            "Walk": 112,          # Monster_Walk
            "Run": 14,            # Run_02
            "Attack": 97,         # Left_Slash (they retaliate!)
            "Hit": 174,
            "Death": 8,
        },
    },
    "Bandit": {
        "prompt": (
            "stylized fantasy bandit raider, dark hooded cloak, worn leather "
            "armor with straps and daggers on belt, menacing humanoid with "
            "clearly defined arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 1.75,
        "animations": {
            "Idle": 0,
            "Walk": 30,       # Casual_Walk
            "Run": 14,        # Run_02
            "Attack": 97,     # Left_Slash
            "Hit": 174,
            "Death": 8,
            "Jump": 13,
        },
    },
    "OrcBrute": {
        "prompt": (
            "stylized fantasy orc brute warrior, heavy green-gray muscled body, "
            "iron shoulder plates and hide loincloth, tusks, big bipedal humanoid "
            "with clearly defined arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 2.1,
        "animations": {
            "Idle": 0,
            "Walk": 112,      # Monster_Walk
            "Run": 14,        # Run_02 (straighter in-game than RunFast)
            "Attack": 4,      # Attack
            "Hit": 174,
            "Death": 8,
            "Jump": 13,
        },
    },
    # v6: naked base bodies — the character-customization foundation. Armor,
    # hair, and weapons attach onto these; the old "Hero" (baked-in armor)
    # becomes a fallback only.
    "HeroMale": {
        "prompt": (
            "stylized fantasy male base character body, athletic build, wearing "
            "only simple plain beige undershorts and chest wrap, bare arms and "
            "legs, neutral friendly face, very short neutral hair, clean "
            "silhouette, game-ready base mesh for character customization, "
            "standing in T-pose, clearly defined arms and legs"
        ),
        "height_meters": 1.8,
        "animations": {
            "Idle": 0,
            "Walk": 30,       # Casual_Walk
            "Run": 14,        # Run_02 (straighter in-game than RunFast)
            "Attack": 4,      # Attack
            "Cast": 125,      # Charged_Spell_Cast
            "Hit": 174,       # Hit reaction family
            "Death": 8,       # Dead
            "Jump": 13,       # Jump_Run
            "Swim": 569,      # Swim_Forward
            "SwimIdle": 568,  # Swim_Idle
            "Dodge": 160,     # Roll/Stand dodge family
            "Eat": 342,       # Stand_and_Drink
            "DanceHipHop": 591,   # Hip_Hop_Dance (player /dance emote)
            "DanceGangnam": 74,   # Gangnam_Groove
            "DanceShake": 82,     # Shake_It_Off_Dance
        },
    },
    "HeroFemale": {
        "prompt": (
            "stylized fantasy female base character body, athletic build, wearing "
            "only simple plain beige undershorts and chest wrap, bare arms and "
            "legs, neutral friendly face, very short neutral hair, clean "
            "silhouette, game-ready base mesh for character customization, "
            "standing in T-pose, clearly defined arms and legs"
        ),
        "height_meters": 1.72,
        "animations": {
            "Idle": 0,
            "Walk": 30,       # Casual_Walk
            "Run": 16,        # RunFast
            "Attack": 4,      # Attack
            "Cast": 125,      # Charged_Spell_Cast
            "Hit": 174,       # Hit reaction family
            "Death": 8,       # Dead
            "Jump": 13,       # Jump_Run
            "Swim": 569,      # Swim_Forward
            "SwimIdle": 568,  # Swim_Idle
            "Dodge": 160,     # Roll/Stand dodge family
            "Eat": 342,       # Stand_and_Drink
            "DanceHipHop": 591,   # Hip_Hop_Dance (player /dance emote)
            "DanceGangnam": 74,   # Gangnam_Groove
            "DanceShake": 82,     # Shake_It_Off_Dance
        },
    },
    "CaveTroll": {
        "prompt": (
            "stylized fantasy cave troll boss, massive rocky mossy skin, glowing "
            "ember cracks across chest and arms, huge bipedal humanoid with "
            "clearly defined thick arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 3.2,
        "animations": {
            "Idle": 0,
            "Walk": 112,      # Monster_Walk
            "Run": 14,        # Run_02
            "Attack": 4,      # Attack
            "Attack2": 127,   # Charged_Ground_Slam
            "Hit": 174,
            "Death": 8,
            "Jump": 13,
        },
    },
    "EmberAcolyte": {
        "prompt": (
            "stylized fantasy ember cult acolyte dungeon enemy, dark red hooded "
            "robes over leather armor, glowing ember mask, slim humanoid, clearly "
            "defined arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 1.8,
        "animations": {
            "Idle": 0,
            "Walk": 30,
            "Run": 14,
            "Attack": 97,
            "Cast": 125,
            "Hit": 174,
            "Death": 8,
            "Jump": 13,
        },
    },
    "StoneSentinel": {
        "prompt": (
            "stylized fantasy stone sentinel dungeon guardian, animated carved "
            "granite armor body with glowing orange cracks, bulky bipedal humanoid "
            "with clearly defined arms and legs, game-ready character, standing in T-pose"
        ),
        "height_meters": 2.35,
        "animations": {
            "Idle": 0,
            "Walk": 112,
            "Run": 14,
            "Attack": 4,
            "Hit": 174,
            "Death": 8,
        },
    },
}

PROPS = {
    # === Emberfall update: forest trees + town buildings (high visual impact) ===
    "OakTree": "large stylized oak tree with broad leafy green canopy and thick gnarled brown trunk, full lush foliage, low-poly game environment tree",
    "BirchTree": "tall slender stylized birch tree with white papery bark and light green leaves, low-poly game environment tree",
    "DeadTree": "bare dead leafless stylized tree with twisted gnarled dark branches and no leaves, low-poly game environment tree",
    "ForestBush": "small round leafy green bush shrub, low-poly game environment plant",
    "MossBoulder": "large grey rock boulder covered in green moss patches, low-poly game environment prop",
    "TreeStump": "old cut tree stump with visible growth rings and gnarled roots, low-poly game environment prop",
    "Cottage": "simple medieval cottage, thatched roof, timber walls, low-poly game building",
    "TownHouse": "two-story medieval fantasy timber-framed townhouse with red tiled roof, wooden door and glass windows, stylized low-poly game building",
    "BlacksmithShop": "simple medieval blacksmith shop, stone chimney, wooden walls, low-poly game building",
    "Storehouse": "simple medieval wooden barn, double doors, sloped roof, low-poly game building",
    "TownHall": "grand two-story medieval fantasy town hall with clock gable, timber-framed upper floor over a stone ground floor, wide entrance steps and double doors, stylized low-poly game building",
    "TownWell": "round medieval stone town well with a small wooden roof and a bucket on a rope, stylized low-poly game prop",
    "MarketBarrel": "wooden medieval barrel with iron bands, stylized game prop",
    "WoodCrate": "wooden storage crate with plank sides, stylized game prop",
    "Signpost": "wooden medieval directional signpost with a blank hanging wooden sign board, stylized game prop",
    "TreasureChest": "medieval wooden treasure chest with iron bands and a heavy clasp, closed, stylized game prop",
    "MarketStall": "small medieval market stall with striped canvas awning and wooden counter, stylized game prop",
    "Statue": "weathered stone statue of a hooded warrior holding a sword point-down, moss details, stylized game prop",
    "WellFountain": "round medieval stone well fountain with small pillars, stylized game prop",
    # Weapons & armor (equipment + drops)
    "KnightSword": "stylized fantasy knight longsword, polished steel blade with blue-silver crossguard and leather-wrapped grip, elegant heroic design, game weapon prop, blade pointing up",
    "RaiderSaber": "stylized curved bandit saber, notched dark steel blade, crude wrapped handle, menacing look, game weapon prop, blade pointing up",
    "IronChestplate": "stylized iron chestplate armor piece, riveted plates with leather straps, worn battle-ready look, game armor prop",
    "Emberfang": "stylized legendary flaming sword, dark obsidian blade with glowing orange ember cracks and fiery runes, dramatic fantasy boss weapon, game weapon prop, blade pointing up",
    "RustySword": "rusty battered old shortsword, chipped corroded blade, cracked wooden grip, pitiful starter weapon, game weapon prop, blade pointing up",
    "IronHelm": "stylized iron great helm with visor slit and rivets, worn battle helmet, game armor prop",
    "LeatherCap": "simple hardened leather skullcap helmet with stitching, game armor prop",
    "IronLegs": "stylized iron leg greaves armor pair, plated shin and thigh guards with straps, game armor prop",
    "BanditCape": "tattered dark hooded cape cloak hanging straight down, worn fabric with frayed edges, game armor prop",
    # v4: fishing, cooking, magic focus
    "FishingPole": "simple wooden fishing rod with thin line and small cork float attached, long slender pole, stylized game prop, rod pointing up",
    "Fish": "stylized plump freshwater fish, simple rounded fins and tail, smooth scales, game item prop lying on its side",
    "Campfire": "small campfire with ring of stones and crossed logs, glowing orange embers in the center, stylized game prop",
    "MagicFocus": "stylized arcane magic focus, ornate short metal handle holding a floating glowing crystal orb, mystical game weapon prop, orb pointing up",
    # v6: hair styles — attach to the base body's head bone, tinted at runtime
    "HairShort": "stylized short cropped hair wig hairpiece only, no head no face, simple neat game character hair mesh, neutral light grey color, hollow underside, game prop",
    "HairLong": "stylized long straight hair wig hairpiece flowing to shoulders, hairpiece only, no head no face, neutral light grey game character hair mesh, hollow underside, game prop",
    "HairPonytail": "stylized hair wig hairpiece with high ponytail, hairpiece only, no head no face, neutral light grey game character hair mesh, hollow underside, game prop",
    # v8: environment pass
    "PineTree": "simple stylized low poly pine tree prop, brown trunk, green cone branches, single game environment asset",
    "GrassSquare": "simple stylized low poly square grass patch prop, flat clump of green grass blades, game environment asset",
    "DungeonPillar": "stylized ancient stone dungeon pillar with cracks, moss, carved bands, game environment prop",
    "DungeonChest": "stylized ornate dungeon treasure chest, dark wood, iron bands, ember gem lock, closed game prop",

    # === Environment art pass: real rocks / foliage / ground detail so the world stops
    # reading as cubes (skinned OVER structural cubes where one is needed). ===
    "RockCliff": "large craggy natural rock cliff face with jagged stone ledges, cracks and outcrops, weathered grey granite, stylized low-poly game environment rock formation",
    "BoulderCluster": "cluster of natural grey granite boulders of varying sizes piled together, weathered stone, stylized low-poly game environment prop",
    "MossyRock": "single large mossy grey rock boulder with green moss on top, rounded natural stone, stylized low-poly game environment prop",
    "Fern": "lush green forest fern plant with spreading feathery fronds, stylized low-poly game foliage",
    "Reeds": "dense cluster of tall green water reeds and brown cattails, stylized low-poly game plant for pond and river edges",
    "Wildflowers": "cluster of small colorful wildflowers, red yellow and white blooms among green grass, stylized low-poly game foliage patch",
    "PebblePatch": "flat scattering of small round pebbles and gravel stones on bare dirt ground, stylized low-poly game ground prop",
    "ThatchRoofSection": "section of golden thatched straw roof, sloped rectangular panel, stylized low-poly game building roof part",
    "WoodShingleRoof": "section of weathered wooden shingle roof, sloped rectangular panel, stylized low-poly game building roof part",

    # === Real mountain range + 2-phase volcano (replace the 'landfill hill' rising terrain
    # with actual mesh assets forming the world border; ref: rocky ranges + Iceland volcanoes). ===
    "MountainPeak": "single large rugged rocky mountain peak, tall and steep with jagged grey and brown stone cliffs, sheer rock faces, a few white snow patches near the summit, stylized low-poly game environment mountain, single standalone asset",
    "MountainRidge": "wide rocky mountain ridge with several jagged stone peaks in a row, grey and brown weathered rock, sparse snow, steep faces, stylized low-poly game environment mountain range backdrop section",
    "ForestFoothill": "green forested foothill, a gentle hill covered in pine trees and grass with grey rocky outcrops near the top, transition from valley to mountains, stylized low-poly game environment, single asset",
    "VolcanoDormant": "large dormant stratovolcano mountain, tall steep symmetrical cone of dark grey and brown volcanic rock with a wide crater at the summit, patches of green vegetation and pine on the lower slopes, bare rocky upper slopes, stylized low-poly game environment, single standalone asset",
    "VolcanoErupting": "large erupting stratovolcano mountain, tall steep cone of black volcanic rock with bright glowing orange lava flows streaming down the sides, a fiery glowing crater with rising smoke, charred slopes, stylized low-poly game environment, single standalone asset",
    "LavaRockGround": "chunk of cracked black volcanic basalt ground with bright glowing orange molten lava seams running through it, rugged rocky surface, stylized low-poly game environment ground piece",
    "Fence": "section of a rustic wooden fence, weathered grey-brown vertical planks and two horizontal rails held between sturdy posts, medieval village boundary fence, stylized low-poly game environment prop, standalone",
    "Cobblestone": "flat square section of cobblestone street paving, rounded weathered grey stone cobbles packed tightly with dark earth in the gaps, worn medieval road, stylized low-poly game environment ground tile, flat top surface",
    "DirtShrub": "small dry scrubby roadside bush, a low sparse clump of thin brown twiggy branches with a few dry olive-green leaves, hardy weed, stylized low-poly game environment plant, standalone",
    # ---- Interior furniture for the enterable city ----
    "Bed": "simple medieval wooden bed frame with a straw mattress and a folded wool blanket, rustic, stylized low-poly game furniture prop, standalone",
    "Table": "sturdy rustic wooden tavern table, thick plank top on four square legs, medieval, stylized low-poly game furniture prop, standalone",
    "Chair": "simple rustic wooden chair with a straight slatted back, medieval, stylized low-poly game furniture prop, standalone",
    "Bench": "long rustic wooden bench seat, medieval tavern, stylized low-poly game furniture prop, standalone",
    "Bookshelf": "tall wooden bookshelf filled with worn leather books and rolled scrolls, medieval, stylized low-poly game furniture prop, standalone",
    "Anvil": "blacksmith black iron anvil mounted on a thick wooden stump, medieval, stylized low-poly game prop, standalone",
    "Fireplace": "stone fireplace hearth with a heavy wooden mantel and glowing orange embers in the grate, medieval, stylized low-poly game prop, standalone",
    "Cauldron": "black cast-iron cooking cauldron pot hanging on an iron tripod over embers, medieval, stylized low-poly game prop, standalone",
    "Rug": "rectangular woven wool rug with a red and gold medieval geometric pattern, flat, stylized low-poly game prop",
    "WallLantern": "wrought-iron wall-mounted lantern with a warm glowing candle behind glass panes, medieval, stylized low-poly game prop, standalone",
}

# ---------------------------------------------------------------------------
# Plumbing
# ---------------------------------------------------------------------------


def load_api_key() -> str:
    key = os.environ.get("MESHY_API_KEY", "").strip()
    if not key:
        env_local = REPO_ROOT / ".env.local"
        if env_local.exists():
            for line in env_local.read_text().splitlines():
                if line.strip().startswith("MESHY_API_KEY"):
                    key = line.split("=", 1)[1].strip().strip("\"'")
                    break
    if not key:
        key_file = Path.home() / ".meshy_key"
        if key_file.exists():
            key = key_file.read_text().strip()
    if not key:
        sys.exit(
            "No Meshy API key found.\n"
            "Put it in .env.local (MESHY_API_KEY=...), ~/.meshy_key, or export MESHY_API_KEY.\n"
            "Get one at https://www.meshy.ai (Settings > API Keys, Pro tier required)."
        )
    return key


class Meshy:
    def __init__(self, api_key: str):
        self.api_key = api_key

    def _request(self, method: str, path: str, body: dict | None = None) -> dict:
        url = f"{API_BASE}{path}"
        data = json.dumps(body).encode() if body is not None else None

        last_err = None
        for attempt in range(4):
            if attempt:
                wait = 5 * (2 ** attempt)  # 10s, 20s, 40s
                print(f"    (network retry {attempt}/3 in {wait}s: {last_err})")
                time.sleep(wait)
            req = urllib.request.Request(url, data=data, method=method)
            req.add_header("Authorization", f"Bearer {self.api_key}")
            req.add_header("Content-Type", "application/json")
            try:
                with urllib.request.urlopen(req, timeout=60) as resp:
                    return json.loads(resp.read().decode())
            except urllib.error.HTTPError as e:
                if e.code >= 500:  # server hiccup — retryable
                    last_err = f"HTTP {e.code}"
                    continue
                detail = e.read().decode(errors="replace")
                raise RuntimeError(f"{method} {path} -> HTTP {e.code}: {detail}") from e
            except (urllib.error.URLError, TimeoutError, OSError) as e:
                last_err = e  # transient network failure — retryable
                continue
        raise RuntimeError(f"{method} {path} failed after retries: {last_err}")

    def create_task(self, path: str, body: dict) -> str:
        resp = self._request("POST", path, body)
        task_id = resp.get("result")
        if not task_id:
            raise RuntimeError(f"No task id in response: {resp}")
        return task_id

    def poll(self, path: str, task_id: str, label: str) -> dict:
        started = time.time()
        last_progress = -1
        while True:
            task = self._request("GET", f"{path}/{task_id}")
            status = task.get("status")
            progress = task.get("progress", 0)
            if progress != last_progress:
                print(f"    [{label}] {status} {progress}%")
                last_progress = progress
            if status == "SUCCEEDED":
                return task
            if status in ("FAILED", "CANCELED"):
                raise RuntimeError(f"{label} {status}: {json.dumps(task.get('task_error', task))[:400]}")
            if time.time() - started > POLL_TIMEOUT_S:
                raise RuntimeError(f"{label} timed out after {POLL_TIMEOUT_S}s (task {task_id})")
            time.sleep(POLL_INTERVAL_S)


class State:
    """step-key -> {task_id, status} so completed steps are never re-billed."""

    def __init__(self, path: Path):
        self.path = path
        self.data = json.loads(path.read_text()) if path.exists() else {}

    def save(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(self.data, indent=2))

    def get(self, key: str) -> dict | None:
        return self.data.get(key)

    def get_done(self, key: str) -> dict | None:
        entry = self.data.get(key)
        return entry if entry and entry.get("status") == "SUCCEEDED" else None

    def mark(self, key: str, task_id: str, status: str, extra: dict | None = None):
        self.data[key] = {"task_id": task_id, "status": status, **(extra or {})}
        self.save()


def download(url: str, dest: Path):
    dest.parent.mkdir(parents=True, exist_ok=True)
    if dest.exists() and dest.stat().st_size > 0:
        print(f"    exists, skipping: {dest.relative_to(REPO_ROOT)}")
        return
    print(f"    downloading {dest.relative_to(REPO_ROOT)}")
    req = urllib.request.Request(url, headers={"User-Agent": "AldenFall-pipeline"})
    with urllib.request.urlopen(req, timeout=300) as resp, open(dest, "wb") as f:
        while chunk := resp.read(1 << 16):
            f.write(chunk)


def run_step(meshy: Meshy, state: State, key: str, create_path: str,
             poll_path: str, body: dict, label: str) -> tuple[str, dict]:
    """Create-or-resume one Meshy task; returns (task_id, final task json)."""
    existing = state.get(key)
    if existing and existing.get("task_id"):
        task_id = existing["task_id"]
        if existing.get("status") == "SUCCEEDED":
            print(f"  [{label}] already done (task {task_id})")
            task = meshy._request("GET", f"{poll_path}/{task_id}")
            return task_id, task

        print(f"  [{label}] resuming task {task_id} ({existing.get('status')})")
        task = meshy.poll(poll_path, task_id, label)
        state.mark(key, task_id, "SUCCEEDED")
        return task_id, task

    print(f"  [{label}] creating task...")
    task_id = meshy.create_task(create_path, body)
    state.mark(key, task_id, "PENDING")
    task = meshy.poll(poll_path, task_id, label)
    state.mark(key, task_id, "SUCCEEDED")
    return task_id, task


def download_model_urls(task: dict, dest_dir: Path, base_name: str):
    """Download fbx + glb from a task's model_urls (+ thumbnail)."""
    urls = task.get("model_urls") or {}
    for fmt in ("fbx", "glb"):
        if urls.get(fmt):
            download(urls[fmt], dest_dir / f"{base_name}.{fmt}")
    if task.get("thumbnail_url"):
        download(task["thumbnail_url"], dest_dir / f"{base_name}_thumb.png")


# ---------------------------------------------------------------------------
# Pipelines
# ---------------------------------------------------------------------------


def generate_character(meshy: Meshy, state: State, name: str, spec: dict):
    print(f"\n=== CHARACTER: {name} ===")
    char_dir = MODELS_DIR / "Characters" / name

    # 1) preview mesh (T-pose for rigging)
    _, preview = run_step(
        meshy, state, f"char:{name}:preview",
        "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
        {
            "mode": "preview",
            "prompt": spec["prompt"],
            "ai_model": "latest",
            "topology": "triangle",
            "target_polycount": 20000,
            "pose_mode": "t-pose",
            "target_formats": ["glb", "fbx"],
        },
        f"{name} preview",
    )
    preview_id = state.data[f"char:{name}:preview"]["task_id"]

    # 2) refine (textures)
    _, refine = run_step(
        meshy, state, f"char:{name}:refine",
        "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
        {"mode": "refine", "preview_task_id": preview_id, "enable_pbr": True},
        f"{name} refine",
    )
    refine_id = state.data[f"char:{name}:refine"]["task_id"]
    download_model_urls(refine, char_dir, f"{name}_static")

    # 3) remesh to a game-ready polycount (rigging caps at 300k faces; refine
    #    output can far exceed that)
    remesh_path = "/openapi/v2/remesh"
    try:
        _, remesh = run_step(
            meshy, state, f"char:{name}:remesh",
            remesh_path, remesh_path,
            {
                "input_task_id": refine_id,
                "target_polycount": 30000,
                "topology": "triangle",
                "target_formats": ["glb", "fbx"],
            },
            f"{name} remesh",
        )
    except RuntimeError as e:
        if "404" not in str(e):
            raise
        # older deployments expose remesh under v1
        remesh_path = "/openapi/v1/remesh"
        _, remesh = run_step(
            meshy, state, f"char:{name}:remesh",
            remesh_path, remesh_path,
            {
                "input_task_id": refine_id,
                "target_polycount": 30000,
                "topology": "triangle",
                "target_formats": ["glb", "fbx"],
            },
            f"{name} remesh",
        )
    remesh_id = state.data[f"char:{name}:remesh"]["task_id"]
    download_model_urls(remesh, char_dir, f"{name}_game")

    # 4) auto-rig (from the remeshed model)
    _, rig = run_step(
        meshy, state, f"char:{name}:rig",
        "/openapi/v1/rigging", "/openapi/v1/rigging",
        {"input_task_id": remesh_id, "height_meters": spec["height_meters"]},
        f"{name} rig",
    )
    rig_id = state.data[f"char:{name}:rig"]["task_id"]

    # Rigged output urls vary by API version — grab everything that looks like a model
    rig_result = rig.get("result", rig)
    for k, v in _walk_urls(rig_result):
        ext = ".fbx" if ".fbx" in v.lower() else ".glb" if ".glb" in v.lower() else None
        if ext:
            download(v, char_dir / f"{name}_rigged_{k}{ext}")

    # 5) animation clips
    for anim_name, action_id in spec["animations"].items():
        try:
            _, anim = run_step(
                meshy, state, f"char:{name}:anim:{anim_name}",
                "/openapi/v1/animations", "/openapi/v1/animations",
                {"rig_task_id": rig_id, "action_id": action_id},
                f"{name} anim {anim_name}",
            )
            anim_result = anim.get("result", anim)
            fbx = _first_url(anim_result, "fbx")
            glb = _first_url(anim_result, "glb")
            if fbx:
                download(fbx, char_dir / "Animations" / f"{name}@{anim_name}.fbx")
            if glb:
                download(glb, char_dir / "Animations" / f"{name}@{anim_name}.glb")
        except RuntimeError as e:
            # A bad action_id shouldn't sink the whole character.
            print(f"    !! {anim_name} failed: {e}")


def generate_prop(meshy: Meshy, state: State, name: str, prompt: str):
    print(f"\n=== PROP: {name} ===")
    prop_dir = MODELS_DIR / "Props"

    _, preview = run_step(
        meshy, state, f"prop:{name}:preview",
        "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
        {
            "mode": "preview",
            "prompt": prompt,
            "ai_model": "latest",
            "topology": "triangle",
            "target_polycount": 6000,
            "target_formats": ["glb", "fbx"],
        },
        f"{name} preview",
    )
    preview_id = state.data[f"prop:{name}:preview"]["task_id"]

    # Preview-only mode: Meshy's PBR refine step is currently unreliable (server-side
    # timeouts that stall the whole queue). Preview meshes generate reliably, but the RAW
    # preview is millions of verts (unusable + fails to instantiate), so REMESH it down to a
    # game-ready polycount and download that. Falls back to the raw preview if remesh 404s.
    if os.environ.get("MESHY_PREVIEW_ONLY"):
        target = int(os.environ.get("MESHY_REMESH_POLY", "4000"))
        remesh = None
        for rp in ("/openapi/v1/remesh", "/openapi/v2/remesh"):
            try:
                _, remesh = run_step(
                    meshy, state, f"prop:{name}:remesh", rp, rp,
                    {"input_task_id": preview_id, "target_polycount": target,
                     "topology": "triangle", "target_formats": ["glb", "fbx"]},
                    f"{name} remesh",
                )
                break
            except Exception as e:
                state.data.pop(f"prop:{name}:remesh", None)   # clear so the next endpoint retries fresh
                if "404" in str(e):
                    continue
                print(f"    {name}: remesh error ({e})")
                break
        if remesh:
            # Overwrite the huge raw preview with the decimated mesh.
            for ext in ("fbx", "glb"):
                f = prop_dir / f"{name}.{ext}"
                if f.exists():
                    f.unlink()
            download_model_urls(remesh, prop_dir, name)
        else:
            print(f"    {name}: remesh unavailable; keeping raw preview")
            download_model_urls(preview, prop_dir, name)
        return

    # Heavy architectural previews can push the PBR refine over Meshy's server-side
    # task timeout; when that happens, fall back to a plain (non-PBR) refine, which
    # is markedly faster and still delivers a textured building.
    try:
        _, refine = run_step(
            meshy, state, f"prop:{name}:refine",
            "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
            {"mode": "refine", "preview_task_id": preview_id, "enable_pbr": True},
            f"{name} refine",
        )
    except RuntimeError as e:
        if "timeout" not in str(e).lower():
            raise
        print(f"    {name}: PBR refine timed out — retrying without PBR")
        _, refine = run_step(
            meshy, state, f"prop:{name}:refine_nopbr",
            "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
            {"mode": "refine", "preview_task_id": preview_id, "enable_pbr": False},
            f"{name} refine (no PBR)",
        )
    download_model_urls(refine, prop_dir, name)


def _walk_urls(obj, prefix=""):
    """Yield (key_path, url) for every http url in a nested task result."""
    if isinstance(obj, dict):
        for k, v in obj.items():
            yield from _walk_urls(v, f"{prefix}{k}" if not prefix else f"{prefix}_{k}")
    elif isinstance(obj, list):
        for i, v in enumerate(obj):
            yield from _walk_urls(v, f"{prefix}{i}")
    elif isinstance(obj, str) and obj.startswith("http"):
        yield prefix, obj


def _first_url(obj, ext: str) -> str | None:
    for _, url in _walk_urls(obj):
        base = url.split("?")[0].lower()
        if base.endswith(f".{ext}") or f"_{ext}" in base:
            return url
    return None


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------


def main():
    parser = argparse.ArgumentParser(description="Meshy asset pipeline for AldenFall")
    parser.add_argument("--characters", action="store_true", help="generate rigged+animated characters")
    parser.add_argument("--props", action="store_true", help="generate props")
    parser.add_argument("--all", action="store_true", help="everything")
    parser.add_argument("--only", help="restrict to asset name(s), comma-separated (e.g. Goblin or FishingPole,Fish)")
    parser.add_argument("--dry-run", action="store_true", help="print the plan without spending credits")
    args = parser.parse_args()

    do_chars = args.characters or args.all
    do_props = args.props or args.all
    if not (do_chars or do_props):
        parser.print_help()
        return

    only = {n.strip() for n in args.only.split(",")} if args.only else None
    chars = {k: v for k, v in CHARACTERS.items() if not only or k in only}
    props = {k: v for k, v in PROPS.items() if not only or k in only}

    if args.dry_run:
        print("PLAN (no credits spent):")
        if do_chars:
            for name, spec in chars.items():
                anims = ", ".join(spec["animations"])
                print(f"  character {name}: preview+refine+rig, anims: {anims}")
        if do_props:
            for name in props:
                print(f"  prop {name}: preview+refine")
        print("\nRough credit cost: ~20-35 per character mesh, ~3 per animation, ~15-30 per prop.")
        return

    meshy = Meshy(load_api_key())
    state = State(STATE_PATH)

    failures = []
    if do_chars:
        for name, spec in chars.items():
            try:
                generate_character(meshy, state, name, spec)
            except Exception as e:  # keep going — other assets shouldn't sink
                failures.append(f"character {name}: {e}")
                print(f"!! character {name} FAILED: {e}")
    if do_props:
        for name, prompt in props.items():
            try:
                generate_prop(meshy, state, name, prompt)
            except Exception as e:
                failures.append(f"prop {name}: {e}")
                print(f"!! prop {name} FAILED: {e}")

    if failures:
        print("\nFAILURES:")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)

    print("\nAll requested assets processed.")
    print(f"Models under: {MODELS_DIR.relative_to(REPO_ROOT)}")
    print("Next: open Unity so the FBX/GLB files import, then run AldenFall > Build Emberfall Demo.")


if __name__ == "__main__":
    main()
