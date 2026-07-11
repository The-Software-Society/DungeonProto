#!/usr/bin/env bash
#
# Push the desktop builds to itch.io via Butler (delta-patching upload).
# The itch app on your friend's machine then auto-patches to the new version.
#
# One-time setup (see README.md in this folder):
#   1. brew install butler        # or download from https://itch.io/app
#   2. butler login               # opens a browser, authorizes this machine
#   3. Create a private/draft project on itch.io, note its slug: <user>/<game>
#
# Usage:
#   Deploy/itch/push-itch.sh <itch-user>/<game-slug>
#   e.g. Deploy/itch/push-itch.sh thesoftwaresociety/aldenfall
#
# Builds must already exist (AldenFall menu > Build All Desktop, or the CI method).
set -euo pipefail

TARGET="${1:-}"
if [[ -z "$TARGET" ]]; then
  echo "usage: $0 <itch-user>/<game-slug>   (e.g. thesoftwaresociety/aldenfall)" >&2
  exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MAC_DIR="$ROOT/Builds/Mac"
WIN_DIR="$ROOT/Builds/Windows"

# Version stamped on itch from the Unity bundleVersion, so patches are ordered.
VERSION="$(grep -E '^\s*bundleVersion:' "$ROOT/ProjectSettings/ProjectSettings.asset" | head -1 | awk '{print $2}')"
echo "Pushing AldenFall v${VERSION} to itch target: ${TARGET}"

if command -v butler >/dev/null 2>&1; then BUTLER=butler; else
  echo "butler not found on PATH. Install it (brew install butler) or use the itch app's butler." >&2
  exit 1
fi

# Don't ship Unity's Burst debug symbols or macOS finder cruft.
IGNORE=(--ignore "*_BurstDebugInformation_DoNotShip" --ignore ".DS_Store")

if [[ -d "$MAC_DIR" ]]; then
  echo "== pushing macOS =="
  "$BUTLER" push "$MAC_DIR" "${TARGET}:mac" --userversion "$VERSION" "${IGNORE[@]}"
else
  echo "skip macOS: $MAC_DIR not found (run the Mac build first)"
fi

if [[ -d "$WIN_DIR" ]]; then
  echo "== pushing Windows =="
  "$BUTLER" push "$WIN_DIR" "${TARGET}:windows" --userversion "$VERSION" "${IGNORE[@]}"
else
  echo "skip Windows: $WIN_DIR not found (run the Windows build first)"
fi

echo "Done. Check status with: butler status ${TARGET}"
