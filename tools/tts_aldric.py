#!/usr/bin/env python3
"""Generate Aldric's intro voice lines with Gemini TTS -> 16-bit PCM WAV files.
Usage: python3 tools/tts_aldric.py <out_dir>
"""
import os, sys, json, base64, struct, urllib.request, urllib.error

KEY = os.environ["GOOGLE_AI_API"]
MODEL = "gemini-2.5-flash-preview-tts"
VOICE = "Algenib"   # gravelly -> a weathered old hermit-mentor
BASE = "https://generativelanguage.googleapis.com/v1beta"
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(OUT, exist_ok=True)

STYLE = ("Read aloud as Aldric, a weathered, kindly old hermit and retired hero. "
         "Warm, unhurried, gravelly, a little wry, with quiet wonder and gravity. Natural pauses.")

LINES = [
    ("01", "Easy now. Easy. By the old fire... a soul, falling right out of the dark. I have not seen that in a long, long age."),
    ("02", "Breathe. You are whole. The embers only borrowed your shape to carry you down here."),
    ("03", "You died. Did you not, in the world you came from. I can still see that last grey morning on you."),
    ("04", "This place is Emberfall. A world built in floors, and each floor a trial that must be cleared before the next will open to you."),
    ("05", "You woke on mine. The lowest floor, and just now, the most troubled."),
    ("06", "Your body is new, but your hands are not empty. The skills you carry will wake as you use them. Fight, and you grow. That is the one law here that never breaks."),
    ("07", "South of here, the mountain burns. A sealed wound, coming open, and its embers already reach for the town below."),
    ("08", "Someone must go down into that fire and close it. And you... you have the look of one with little left to lose."),
    ("09", "I have three old arms left in me. A blade. A wand and focus. A bow, and a hundred arrows. Take them all, and draw whichever one calls to you."),
    ("10", "Come. Learn which one you are. Fish the lake, warm yourself, live a little. The mountain will still be waiting."),
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
    with urllib.request.urlopen(req, timeout=120) as r:
        d = json.load(r)
    part = d["candidates"][0]["content"]["parts"][0]["inlineData"]
    return base64.b64decode(part["data"]), part.get("mimeType", "audio/L16;rate=24000")

def wav_bytes(pcm, rate=24000, ch=1, bits=16):
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

def main():
    for lid, text in LINES:
        try:
            pcm, mime = tts(text)
            rate = rate_from_mime(mime)
            path = os.path.join(OUT, f"aldric_intro_{lid}.wav")
            open(path, "wb").write(wav_bytes(pcm, rate=rate))
            dur = len(pcm) / (rate * 2)
            print(f"{lid}: {path}  {len(pcm)}B  ~{dur:.1f}s  ({mime})", flush=True)
        except urllib.error.HTTPError as e:
            print(f"{lid} FAIL {e.code}: {e.read().decode()[:400]}", flush=True)
    print("TTS DONE", flush=True)

if __name__ == "__main__":
    main()
