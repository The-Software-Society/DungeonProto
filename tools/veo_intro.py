#!/usr/bin/env python3
"""Generate the POV isekai entry-cutscene clips with Veo 3.1, then download them.
Submits all clips, polls the long-running operations, saves mp4s to scratchpad.
Usage: python3 tools/veo_intro.py <out_dir>
"""
import os, sys, json, time, base64, urllib.request, urllib.error

KEY = os.environ["GOOGLE_AI_API"]
MODEL = "veo-3.1-generate-preview"
BASE = "https://generativelanguage.googleapis.com/v1beta"
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(OUT, exist_ok=True)

# POV, no visible protagonist face (so it never has to match character creation).
# Kingdom-Hearts / "Simple and Clean" register: melancholy, ethereal, emotional, with music.
CLIPS = [
    ("02_crossing",
     "First-person POV camera, no visible face or body, no hands. The viewer is standing on a wet city street at "
     "dusk. The head turns to look down the road, and a single car is accelerating straight toward the camera, very "
     "fast. Its two headlights grow larger and brighter extremely quickly, filling the whole frame, engine roaring, "
     "tyres screeching on wet asphalt, a blaring car horn. At the final instant everything snaps to pure blinding "
     "white with a single loud crash-and-glass impact sound, then abrupt total silence. NO people, NO body, NO blood, "
     "NO injury shown, only the speeding car, the blinding headlights, the crash sound and the white-out. Cinematic, "
     "dramatic, tense, first-person."),
]
NEG = "gore, blood, injury, corpse, text, watermark, subtitles, logo, cartoon, low quality"

def post(url, body):
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.load(r)

def get(url):
    with urllib.request.urlopen(url, timeout=120) as r:
        return json.load(r)

def submit(prompt):
    url = f"{BASE}/models/{MODEL}:predictLongRunning?key={KEY}"
    body = {"instances": [{"prompt": prompt}],
            "parameters": {"aspectRatio": "16:9", "negativePrompt": NEG, "resolution": "1080p"}}
    return post(url, body)["name"]

def download_video(resp, path):
    # Gemini API returns either an inline base64 video or a fileUri to fetch with the key.
    gv = resp.get("generateVideoResponse") or resp.get("response", {}).get("generateVideoResponse")
    if not gv:
        # some responses nest under 'response'
        gv = resp.get("response", {})
    samples = gv.get("generatedSamples") or gv.get("generated_samples") or gv.get("videos") or []
    if not samples:
        raise RuntimeError("no samples in response: " + json.dumps(resp)[:800])
    vid = samples[0].get("video", samples[0])
    if "uri" in vid or "fileUri" in vid:
        uri = vid.get("uri") or vid.get("fileUri")
        sep = "&" if "?" in uri else "?"
        req = urllib.request.Request(uri + f"{sep}key={KEY}")
        with urllib.request.urlopen(req, timeout=300) as r, open(path, "wb") as f:
            f.write(r.read())
    elif "encodedVideo" in vid or "bytesBase64Encoded" in vid:
        b = vid.get("encodedVideo") or vid.get("bytesBase64Encoded")
        open(path, "wb").write(base64.b64decode(b))
    else:
        raise RuntimeError("unknown video payload keys: " + str(list(vid.keys())))

def main():
    ops = {}
    for name, prompt in CLIPS:
        try:
            op = submit(prompt)
            ops[name] = op
            print(f"submitted {name}: {op}", flush=True)
        except urllib.error.HTTPError as e:
            print(f"SUBMIT FAIL {name}: {e.code} {e.read().decode()[:500]}", flush=True)
    json.dump(ops, open(os.path.join(OUT, "veo_ops.json"), "w"), indent=2)

    # Poll
    pending = dict(ops)
    deadline = time.time() + 1800  # 30 min
    while pending and time.time() < deadline:
        time.sleep(15)
        for name in list(pending):
            op = pending[name]
            try:
                st = get(f"{BASE}/{op}?key={KEY}")
            except urllib.error.HTTPError as e:
                print(f"poll {name} http {e.code}", flush=True); continue
            if st.get("done"):
                if "error" in st:
                    print(f"DONE-ERROR {name}: {json.dumps(st['error'])[:500]}", flush=True)
                else:
                    path = os.path.join(OUT, f"{name}.mp4")
                    try:
                        download_video(st.get("response", st), path)
                        print(f"downloaded {name} -> {path} ({os.path.getsize(path)} bytes)", flush=True)
                    except Exception as ex:
                        print(f"DOWNLOAD FAIL {name}: {ex}", flush=True)
                        json.dump(st, open(os.path.join(OUT, f"{name}_raw.json"), "w"))
                del pending[name]
            else:
                print(f"...{name} still running", flush=True)
    if pending:
        print("TIMEOUT still pending:", list(pending), flush=True)
    print("VEO DONE", flush=True)

if __name__ == "__main__":
    main()
