#!/bin/bash
set -e
NAME="${1:?clip name}"
SCRIPT="${2:?clip script json}"
FPS="${3:-30}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY_APP="/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app"
SCRIPT="$(cd "$(dirname "$SCRIPT")" && pwd)/$(basename "$SCRIPT")"
dow=$(date +%u)
MON=$(date -v-$((dow-1))d +%Y-%m-%d)
SUN=$(date -v+$((7-dow))d +%Y-%m-%d)
DEST_DIR="$ROOT/DevClips/${MON}_to_${SUN}/environment"
mkdir -p "$DEST_DIR"
DEST="$DEST_DIR/$NAME.mp4"
FRAMES="$(mktemp -d)/frames"; mkdir -p "$FRAMES"
LOG="$(mktemp).editorclip.log"
open -W -n "$UNITY_APP" --args -ApplePersistenceIgnoreState YES -projectPath "$ROOT" \
  -executeMethod AldenFall.Editor.EditorClipLauncher.Run -devclip -clip "$SCRIPT" \
  -clipout "$FRAMES" -clipfps "$FPS" -logFile "$LOG" &
OPEN_PID=$!
# AppKit may show its crash-restoration alert before Unity receives command-line flags. The default
# Return action dismisses it; harmless when no alert is present because Unity has no focused text UI.
sleep 2
osascript -e 'tell application "System Events" to key code 36' >/dev/null 2>&1 || true
wait "$OPEN_PID" || true
COUNT=$(find "$FRAMES" -name 'frame_*.png' | wc -l | tr -d ' ')
if [ "$COUNT" -lt 5 ]; then echo "FAILED: only $COUNT frames"; tail -50 "$LOG"; exit 1; fi
"$ROOT/tools/make_montage.sh" "$FRAMES" "$DEST" "$FPS" >/dev/null
rm -rf "$(dirname "$FRAMES")"
echo "CLIP: $DEST  ($COUNT frames, $(du -h "$DEST" | cut -f1))"
