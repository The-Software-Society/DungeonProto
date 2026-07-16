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
    ("01_morning",
     "First-person POV, no visible face or body. A quiet grey morning in an ordinary modern apartment. "
     "The camera is the person's own eyes: looking down at a hand resting on a warm coffee mug on a small table, "
     "then drifting up to a rain-streaked window overlooking a dull grey city skyline at dawn. "
     "Muted desaturated colours, soft melancholy, gentle rain sounds, a slow emotional solo piano melody. "
     "Cinematic, shallow depth of field, calm and a little lonely."),
    ("02_crossing",
     "First-person POV, no visible face. Stepping off a curb to cross a wet city street at dusk, reflections of "
     "traffic lights on the pavement. Suddenly bright white headlights flare and rush toward the camera, the sound "
     "swells and then cuts to complete silence and a soft white bloom that fills the whole frame. Tasteful, no gore, "
     "no impact shown, only light and sudden quiet. Emotional, cinematic, a single held piano note."),
    ("03_descent",
     "First-person POV, no visible face or body, a weightless feeling of falling gently backward through a vast dark "
     "starry void filled with thousands of drifting glowing golden embers and soft motes of light. Far below, the "
     "clouds part to reveal a dark green pine forest valley and a distant mountain glowing with orange lava at its peak. "
     "The camera falls slowly toward the forest. Ethereal, dreamlike, hopeful, a swelling emotional orchestral and "
     "ethereal female vocal-pad score, cinematic, beautiful."),
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
