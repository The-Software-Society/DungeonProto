# AldenFall Multiplayer Test — Architecture

Desktop-only (WebGL/Vercel removed 2026-07). Two pieces:

- **Clients**: Mac + Windows standalone builds, distributed privately via itch.io.
  See `Deploy/itch/README.md` for the build + `butler push` flow.
- **Server**: a Unity Linux dedicated server on Render (`Deploy/Render/`), reached by
  clients over Mirror's SimpleWebTransport (WebSocket/WSS). The in-game password is only
  a test gate, not real auth.

## Server deploy (when the game code changes)
1. Unity: `AldenFall > Multiplayer > Build Linux Dedicated Server`
2. `python3 tools/prepare_render_server.py`  (copies `Builds/LinuxServer` → `Deploy/Render/server`)
3. Commit + push to `main` → Render redeploys from `Deploy/Render/` (Docker). `render.yaml`
   defines the free web service `aldenfall-test-server`; the Unity server reads Render's `PORT` env.

## Clients connect
- Shipped clients default their server address to `aldenfall-test-server.onrender.com`
  (no typing). Password: `emberfall`.
- Render Free spins down when idle; first connect after that takes ~1 minute.

## Current test scope
- no accounts / no saving, 2-4 people, character selection at connect time
- server-authoritative enemies + combat + loot (shared world)

## Notes
- The WebSocket transport (SimpleWebTransport) is required even though clients are desktop —
  Render only exposes HTTPS/WSS. It is NOT WebGL-specific, so it stays.
- The WebGL Build Support module can be uninstalled from Unity Hub to reclaim disk (~5 GB).
