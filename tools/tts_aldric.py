#!/usr/bin/env python3
"""Generate Aldric's intro voice lines with Gemini TTS, then pitch them DOWN a little with
ffmpeg for a deeper / older / mysterious tone. -> 16-bit PCM WAV in StreamingAssets/voice.
Usage: python3 tools/tts_aldric.py <out_dir>
"""
import os, sys, json, base64, struct, subprocess, tempfile, urllib.request, urllib.error

KEY = os.environ["GOOGLE_AI_API"]
MODEL = "gemini-2.5-pro-preview-tts"     # higher-quality TTS
VOICE = "Charon"                          # a deep male voice; pitched down further below
PITCH = 0.90                              # 0.90 = ~10% lower -> deeper, older, bigger
BASE = "https://generativelanguage.googleapis.com/v1beta"
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(OUT, exist_ok=True)

# Delivery: ancient, weathered, deep, SLOW, mysterious. Long pauses, grave and low.
STYLE = ("Speak as Aldric, an ancient and weathered hermit at the edge of the world. A very deep, "
         "low, slow, gravelly voice, quiet and mysterious, weary and wise, almost grave. Take long "
         "unhurried pauses. Never rushed. Speak barely above a murmur, with the weight of ages.")

# CANON (ALDENFALL_GAME_PLAN.md): NO mention of floors/worlds, NO volcano (deeper questline), do NOT
# explain death/respawn. Aldric is surprised, unsettled, then practical. Welcome to Emberfall as a
# real place. Give ALL THREE weapons as a gift to try. ~1/3 shorter than before.
LINES = [
    ("01", "Stand easy. Easy now... You came out of the air itself. Set down in my glade like a leaf off the wind."),
    ("02", "And you carry the chill of the newly dead. Yet here you stand, breathing. That is no small thing, stranger. Not here. Not now."),
    ("03", "No matter. You are here, and you know nothing of this place. So let an old man teach you a little, while it is quiet."),
    ("04", "This is Emberfall. Real ground, real cold, real teeth waiting in the dark of it. It is no dream. Walk it carefully, and it may yet let you live."),
    ("05", "Here. Take these, all of them. Three old friends of mine. A blade. A wand and its focus. And a bow, with a full quiver. They are yours now."),
    ("06", "I will not tell you which to carry. Swing them, loose them, learn which one answers your hand. Then come and find me, and we will see what you are made of."),
]

def tts(text):
    url = f"{BASE}/models/{MODEL}:generateContent?key={KEY}"
    body = {
        "contents": [{"parts": [{"text": f"{STYLE}\n\n\"{text}\""}]}],
        "generationConfig": {
            "responseModalities": ["AUDIO"],
            "speechConfig": {"voiceConfig": {"prebuiltVoiceConfig": {"voiceName": VOICE}}},
        },
    }
    req = urllib.request.Request(url, data=json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req, timeout=180) as r:
        d = json.load(r)
    part = d["candidates"][0]["content"]["parts"][0]["inlineData"]
    return base64.b64decode(part["data"]), part.get("mimeType", "audio/L16;rate=24000")

def wav_bytes(pcm, rate, ch=1, bits=16):
    byte_rate = rate * ch * bits // 8
    block = ch * bits // 8
    return (b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVE" +
            b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, rate, byte_rate, block, bits) +
            b"data" + struct.pack("<I", len(pcm)) + pcm)

def rate_from_mime(mime):
    for tok in mime.split(";"):
        tok = tok.strip()
        if tok.startswith("rate="):
            return int(tok.split("=")[1])
    return 24000

def pitch_down(src_wav, dst_wav, rate):
    # asetrate lowers pitch (and slows); atempo restores duration -> deeper voice, same length.
    new_sr = int(rate * PITCH)
    tempo = 1.0 / PITCH
    filt = f"asetrate={new_sr},aresample={rate},atempo={tempo:.5f}"
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", src_wav, "-af", filt,
                    "-ar", str(rate), "-ac", "1", "-c:a", "pcm_s16le", dst_wav], check=True)

def main():
    for lid, text in LINES:
        try:
            pcm, mime = tts(text)
            rate = rate_from_mime(mime)
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tf:
                tf.write(wav_bytes(pcm, rate))
                raw = tf.name
            dst = os.path.join(OUT, f"aldric_intro_{lid}.wav")
            pitch_down(raw, dst, rate)
            os.unlink(raw)
            print(f"{lid}: {dst}  ({mime}, pitched x{PITCH})", flush=True)
        except urllib.error.HTTPError as e:
            print(f"{lid} FAIL {e.code}: {e.read().decode()[:400]}", flush=True)
    print("TTS DONE", flush=True)

if __name__ == "__main__":
    main()
