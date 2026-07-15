#!/bin/bash
# Stitch montage frames (frame_####.png) into an H.264 MP4 for easy click-play.
# Usage: make_montage.sh <frames_dir> <out.mp4> [fps]
set -e
DIR="${1:?frames dir}"; OUT="${2:?output mp4}"; FPS="${3:-30}"
ffmpeg -y -framerate "$FPS" -i "$DIR/frame_%04d.png" \
  -c:v libx264 -pix_fmt yuv420p -crf 19 -vf "scale=1280:720" -movflags +faststart "$OUT"
echo "wrote $OUT ($(du -h "$OUT" | cut -f1))"
