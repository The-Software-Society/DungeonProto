#!/usr/bin/env python3
"""Re-download the PBR-textured refine output (mesh + base_color) for props whose
refine task already SUCCEEDED (already billed). Fixes 'flat/untextured' props that
were only ever downloaded as remesh (texture-stripped) output. Usage:
    python3 tools/fetch_refine.py BoulderCluster MossyRock RockCliff
"""
import sys, json
from pathlib import Path
from meshy_pipeline import Meshy, load_api_key, download, MODELS_DIR

STATE = json.load(open(Path(__file__).parent / "meshy_state.json"))
PROP_DIR = MODELS_DIR / "Props"


def fetch(meshy, name):
    key = f"prop:{name}:refine"
    entry = STATE.get(key)
    if not entry or not entry.get("task_id"):
        print(f"!! {name}: no refine task in state")
        return
    tid = entry["task_id"]
    task = meshy._request("GET", f"/openapi/v2/text-to-3d/{tid}")
    urls = task.get("model_urls") or {}
    for fmt in ("fbx", "glb"):
        if urls.get(fmt):
            download(urls[fmt], PROP_DIR / f"{name}.{fmt}")
            print(f"  {name}.{fmt} <- refine {tid}")
    if task.get("thumbnail_url"):
        download(task["thumbnail_url"], PROP_DIR / f"{name}_thumb.png")
    # PBR textures live in texture_urls; grab base_color (+ any metallic/normal/rough)
    texs = task.get("texture_urls") or []
    if texs:
        t0 = texs[0]
        for suffix, urlkey in [("baseColor", "base_color"), ("normal", "normal"),
                               ("metallic", "metallic"), ("roughness", "roughness")]:
            if t0.get(urlkey):
                download(t0[urlkey], PROP_DIR / f"{name}_{suffix}.jpg")
                print(f"  {name}_{suffix}.jpg")
    else:
        print(f"  !! {name}: no texture_urls (texture may be embedded in glb)")


def refine(meshy, name):
    """Create+poll a PBR refine from the existing preview, then download textured output.
    For props generated preview-only (flat/untextured). Updates state so it isn't re-billed."""
    import meshy_pipeline as mp
    state = mp.State(Path(__file__).parent / "meshy_state.json")
    pkey = f"prop:{name}:preview"
    prev = state.get_done(pkey)
    if not prev:
        print(f"!! {name}: no SUCCEEDED preview to refine from")
        return
    preview_id = prev["task_id"]
    _, task = mp.run_step(
        meshy, state, f"prop:{name}:refine",
        "/openapi/v2/text-to-3d", "/openapi/v2/text-to-3d",
        {"mode": "refine", "preview_task_id": preview_id, "enable_pbr": True},
        f"{name} refine",
    )
    state.save()
    fetch(meshy, name)


def main():
    args = sys.argv[1:]
    do_refine = False
    if args and args[0] == "--refine":
        do_refine = True; args = args[1:]
    meshy = Meshy(load_api_key())
    for name in args:
        print(f"=== {name} ===")
        if do_refine:
            refine(meshy, name)
        else:
            fetch(meshy, name)


if __name__ == "__main__":
    main()
