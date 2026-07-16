#!/bin/bash
# Record a NATURAL dev clip and archive it for dev-log / social use.
#   tools/record_clip.sh <category> <name> <clip_script.json> [fps]
# Categories are just folders (quests, environment, combat, mobs, ui, cutscenes, ...).
# Output: DevClips/<mon>_to_<sun>/<category>/<name>.mp4   (week = Mon..Sun of today)
#
# This runs the CURRENT Mac build windowed with the NaturalClipDriver (-devclip), captures
# frames, then stitches them with make_montage.sh. Build first (BuildMacDesktop) so the app
# reflects your latest work. This is NOT the fast -qashots validation pass; it's the polished,
# player-like capture you make AFTER you've validated the work.
set -e
CAT="${1:?category (quests|environment|combat|mobs|ui|cutscenes|...)}"
NAME="${2:?clip name}"
SCRIPT="${3:?clip script json}"
FPS="${4:-30}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/Builds/Mac/AldenFall.app/Contents/MacOS/AldenFall"
[ -x "$APP" ] || { echo "No Mac build at $APP — build BuildMacDesktop first."; exit 1; }
[ -f "$SCRIPT" ] || { echo "No clip script at $SCRIPT"; exit 1; }
# The app's CWD is the .app bundle, not the repo — pass an ABSOLUTE clip-script path.
SCRIPT="$(cd "$(dirname "$SCRIPT")" && pwd)/$(basename "$SCRIPT")"

dow=$(date +%u)                                   # 1..7 (Mon..Sun)
MON=$(date -v-$((dow-1))d +%Y-%m-%d)
SUN=$(date -v+$((7-dow))d +%Y-%m-%d)
WEEK="${MON}_to_${SUN}"
DEST_DIR="$ROOT/DevClips/$WEEK/$CAT"
mkdir -p "$DEST_DIR"
DEST="$DEST_DIR/$NAME.mp4"

FRAMES="$(mktemp -d)/frames"; mkdir -p "$FRAMES"
LOG="$(mktemp).devclip.log"
echo "Recording $CAT/$NAME (fps $FPS) ..."
"$APP" -devclip -clip "$SCRIPT" -clipout "$FRAMES" -clipfps "$FPS" -logFile "$LOG" || true
COUNT=$(ls "$FRAMES"/frame_*.png 2>/dev/null | wc -l | tr -d ' ')
if [ "$COUNT" -lt 5 ]; then echo "FAILED: only $COUNT frames. Tail of log:"; tail -20 "$LOG"; exit 1; fi

"$ROOT/tools/make_montage.sh" "$FRAMES" "$DEST" "$FPS" >/dev/null
rm -rf "$(dirname "$FRAMES")"
echo "CLIP: $DEST  ($COUNT frames, $(du -h "$DEST" | cut -f1))"
