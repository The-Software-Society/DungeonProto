#!/usr/bin/env bash
set -euo pipefail

SERVER_BIN="$(find /app -maxdepth 1 -type f -name '*.x86_64' | head -n 1)"
if [[ -z "${SERVER_BIN}" ]]; then
  echo "No Unity Linux server binary found in /app" >&2
  exit 1
fi

exec "${SERVER_BIN}" -batchmode -nographics -logFile -
