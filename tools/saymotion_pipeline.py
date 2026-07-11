#!/usr/bin/env python3
"""
SayMotion (DeepMotion) text-to-motion pipeline for AldenFall.

FREEDOM CREATIVE ANIMATION BY TEXT: describe a motion in MOTIONS below and it
becomes a game-ready clip on the character's OWN rig:

  1. uploads the character's rigged FBX once (modelId cached in state)
  2. submits a text2motion job with the prompt (on that model)
  3. polls, downloads the FBX (arrives zipped), unzips
  4. drops it at Assets/Models/Characters/<Char>/Animations/<Char>@<Clip>.fbx
     -> AldenFall > Import Meshy Characters picks it up automatically
     (Dance* clips auto-loop and become animator island states)

Resumable: completed job ids live in tools/saymotion_state.json.

Credentials (.env.local or env vars) — from the SayMotion API portal:
  SAYMOTION_CLIENT_ID=...
  SAYMOTION_CLIENT_SECRET=...
  SAYMOTION_API_URL=...        (the "Production URL")

Usage:
  python3 tools/saymotion_pipeline.py --dry-run     # show plan
  python3 tools/saymotion_pipeline.py --balance     # credit check
  python3 tools/saymotion_pipeline.py --all         # generate everything new
  python3 tools/saymotion_pipeline.py --only GoblinDancer:DanceTwerk
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
import time
import zipfile
from pathlib import Path

try:
    import requests
except ImportError:
    sys.exit("pip3 install requests   (needed for SayMotion API)")

REPO_ROOT = Path(__file__).resolve().parent.parent
STATE_PATH = REPO_ROOT / "tools" / "saymotion_state.json"
CHARACTERS_DIR = REPO_ROOT / "Assets" / "Models" / "Characters"

POLL_INTERVAL_S = 10
POLL_TIMEOUT_S = 20 * 60

# ---------------------------------------------------------------------------
# Motion specs — describe it, it exists. seconds: 0 lets the AI decide (max 10).
# ---------------------------------------------------------------------------

MOTIONS = {
    "GoblinDancer": {
        "DanceRobot": {
            "prompt": (
                "dancing the robot: stiff mechanical dance with sharp popping "
                "and locking, rigid right-angle arm poses held then snapped, "
                "robotic head isolations, staying in place, looping dance"
            ),
            "seconds": 8,
        },
        "DanceTwerk": {
            "prompt": (
                "twerking: deep squat with hands braced on knees, torso leaned "
                "far forward, rapid rhythmic hip and pelvis shaking, staying "
                "in place, looping dance"
            ),
            "seconds": 6,
        },
    },
}

# Rigged T-pose FBX per character (uploaded once, cached)
def character_fbx(name: str) -> Path:
    d = CHARACTERS_DIR / name
    for pattern in (f"{name}_rigged_rigged_character_fbx_url.fbx", f"{name}_game.fbx"):
        p = d / pattern
        if p.exists():
            return p
    raise FileNotFoundError(f"No rigged FBX for {name} under {d}")

# ---------------------------------------------------------------------------
# Plumbing
# ---------------------------------------------------------------------------

def load_credentials() -> tuple[str, str, str]:
    creds = {
        "SAYMOTION_CLIENT_ID": os.environ.get("SAYMOTION_CLIENT_ID", "").strip(),
        "SAYMOTION_CLIENT_SECRET": os.environ.get("SAYMOTION_CLIENT_SECRET", "").strip(),
        "SAYMOTION_API_URL": os.environ.get("SAYMOTION_API_URL", "").strip(),
    }
    env_local = REPO_ROOT / ".env.local"
    if env_local.exists():
        for line in env_local.read_text().splitlines():
            line = line.strip()
            if "=" not in line or line.startswith("#"):
                continue
            key, _, value = line.partition("=")
            key = key.strip()
            if key in creds and not creds[key]:
                creds[key] = value.strip()
    missing = [k for k, v in creds.items() if not v]
    if missing:
        sys.exit(
            "Missing SayMotion credentials: " + ", ".join(missing) + "\n"
            "Add them to .env.local once your API trial is approved:\n"
            "  SAYMOTION_CLIENT_ID=...\n  SAYMOTION_CLIENT_SECRET=...\n"
            "  SAYMOTION_API_URL=...  (the Production URL)"
        )
    return creds["SAYMOTION_CLIENT_ID"], creds["SAYMOTION_CLIENT_SECRET"], creds["SAYMOTION_API_URL"].rstrip("/")


class State:
    def __init__(self):
        self.data = json.loads(STATE_PATH.read_text()) if STATE_PATH.exists() else {}

    def save(self):
        STATE_PATH.write_text(json.dumps(self.data, indent=2))


class SayMotion:
    def __init__(self):
        client_id, client_secret, url = load_credentials()
        self.url = url
        self.session = requests.Session()
        self.session.auth = (client_id, client_secret)
        resp = self.session.get(f"{self.url}/account/v1/auth")
        resp.raise_for_status()
        print("Authenticated with SayMotion.")

    def get(self, path: str) -> dict:
        resp = self.session.get(self.url + path)
        resp.raise_for_status()
        return resp.json()

    def post(self, path: str, payload: dict) -> dict:
        resp = self.session.post(self.url + path, json=payload)
        resp.raise_for_status()
        return resp.json()

    # ---- characters ----

    def ensure_model(self, state: State, name: str) -> str:
        key = f"model:{name}"
        if key in state.data:
            return state.data[key]["modelId"]

        fbx = character_fbx(name)
        print(f"  uploading character {name} ({fbx.name}, {fbx.stat().st_size // 1024} KB)...")
        upload_info = self.get(
            f"/character/v1/getModelUploadUrl?name={name}&modelExt=fbx&resumable=0")
        gcs_url = upload_info["modelUrl"]

        data = fbx.read_bytes()
        put = self.session.put(gcs_url, data=data, headers={
            "Content-Length": str(len(data)),
            "Content-Type": "application/octet-stream",
        })
        put.raise_for_status()

        stored = self.post("/character/v1/storeModel", {
            "modelId": None, "modelUrl": gcs_url, "thumbUrl": None, "modelName": name,
        })
        model_id = stored["modelId"]
        state.data[key] = {"modelId": model_id}
        state.save()
        print(f"  stored model {name} -> {model_id}")
        return model_id

    # ---- jobs ----

    def text2motion(self, prompt: str, seconds: float, model_id: str) -> str:
        params = [f'prompt="{prompt}"', f"model={model_id}"]
        if seconds > 0:
            params.insert(1, f"requestedAnimationDuration={min(seconds, 10)}")
        resp = self.post("/job/v1/process/text2motion", {"params": params})
        return resp["rid"]

    def wait(self, rid: str, label: str):
        deadline = time.time() + POLL_TIMEOUT_S
        while time.time() < deadline:
            status = self.get(f"/job/v1/status/{rid}")
            if status.get("count", 0) > 0:
                info = status["status"][0]
                if info["status"] == "SUCCESS":
                    print(f"  [{label}] SUCCEEDED")
                    return
                if info["status"] == "FAILURE":
                    raise RuntimeError(f"{label} FAILED: {info.get('details')}")
                if info["status"] == "PROGRESS":
                    details = info.get("details", {})
                    total = float(details.get("total", 1) or 1)
                    step = float(details.get("step", 0))
                    print(f"  [{label}] {min(100, round(step * 100 / total))}%", end="\r")
            time.sleep(POLL_INTERVAL_S)
        raise TimeoutError(f"{label} timed out")

    def download_fbx(self, rid: str, dest: Path):
        listing = self.get(f"/job/v1/download/{rid}")
        if listing.get("count", 0) == 0:
            raise RuntimeError(f"no downloads for {rid}")
        for url_entry in listing["links"][0]["urls"]:
            for file_entry in url_entry["files"]:
                if "fbx" not in file_entry:
                    continue
                blob = self.session.get(file_entry["fbx"]).content
                # FBX ships zipped; extract the first .fbx inside
                try:
                    with zipfile.ZipFile(io.BytesIO(blob)) as z:
                        inner = next(n for n in z.namelist() if n.lower().endswith(".fbx"))
                        dest.write_bytes(z.read(inner))
                except zipfile.BadZipFile:
                    dest.write_bytes(blob)  # some responses are raw fbx
                print(f"  saved {dest.relative_to(REPO_ROOT)}")
                return
        raise RuntimeError(f"no FBX in download listing for {rid}")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def wanted_motions(only: str | None):
    for char, clips in MOTIONS.items():
        for clip, spec in clips.items():
            tag = f"{char}:{clip}"
            if only and only.lower() not in (tag.lower(), char.lower()):
                continue
            yield char, clip, spec


def main():
    parser = argparse.ArgumentParser(description="SayMotion text-to-motion pipeline")
    parser.add_argument("--all", action="store_true", help="generate every motion not yet done")
    parser.add_argument("--only", help="restrict to Char or Char:Clip (e.g. GoblinDancer:DanceTwerk)")
    parser.add_argument("--balance", action="store_true", help="print credit balance")
    parser.add_argument("--dry-run", action="store_true", help="print the plan, no credits spent")
    args = parser.parse_args()

    if args.dry_run:
        print("PLAN (no credits spent):")
        for char, clip, spec in wanted_motions(args.only):
            print(f"  {char}:{clip}  ({spec['seconds']}s)  \"{spec['prompt'][:70]}...\"")
        return

    api = SayMotion()

    if args.balance:
        print("Credits:", api.get("/account/v1/creditBalance").get("credits"))
        return

    if not (args.all or args.only):
        print("Nothing to do — pass --all, --only, --balance, or --dry-run.")
        return

    state = State()
    failures = []
    for char, clip, spec in wanted_motions(args.only):
        key = f"motion:{char}:{clip}"
        dest = CHARACTERS_DIR / char / "Animations" / f"{char}@{clip}.fbx"
        if state.data.get(key, {}).get("done") and dest.exists():
            print(f"{char}:{clip} already done, skipping.")
            continue
        print(f"\n=== {char}:{clip} ===")
        try:
            model_id = api.ensure_model(state, char)
            rid = state.data.get(key, {}).get("rid")
            if not rid:
                rid = api.text2motion(spec["prompt"], spec["seconds"], model_id)
                state.data[key] = {"rid": rid}
                state.save()
                print(f"  job {rid} submitted")
            api.wait(rid, f"{char}:{clip}")
            dest.parent.mkdir(parents=True, exist_ok=True)
            api.download_fbx(rid, dest)
            state.data[key]["done"] = True
            state.save()
        except Exception as e:  # keep going; state resumes the rest
            print(f"!! {char}:{clip} FAILED: {e}")
            failures.append(f"{char}:{clip}: {e}")

    print("\nDone." if not failures else "\nFAILURES:\n  " + "\n  ".join(failures))
    if not failures:
        print("Next: AldenFall > Import Meshy Characters, then Build Emberfall Demo.")


if __name__ == "__main__":
    main()
