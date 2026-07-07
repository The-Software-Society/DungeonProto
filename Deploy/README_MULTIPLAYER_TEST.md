# AldenFall Multiplayer Test

Architecture for the first public test:

- Vercel hosts the Unity WebGL build in `Builds/WebGL`.
- Render Free runs the Unity dedicated server build from `Deploy/Render/server`.
- Mirror handles live players. The WebGL client connects over Mirror's WebSocket transport when available.
- The in-game password is only a test gate, not production auth.

Unity menu flow:

1. `AldenFall > Import Meshy + Rebuild Emberfall Demo`
2. `AldenFall > Multiplayer > Build WebGL Test`
3. `AldenFall > Multiplayer > Build Linux Dedicated Server`
4. From the repo root: `python3 tools/prepare_render_server.py`

Vercel:

- Deploy the contents of `Builds/WebGL`.
- The build tool copies `Deploy/Vercel/vercel.json` into that folder.
- Use Vercel Deployment Protection for the real page password.

Render server:

- `tools/prepare_render_server.py` copies `Builds/LinuxServer` into `Deploy/Render/server`.
- `render.yaml` defines a free Docker web service named `aldenfall-test-server`.
- The Unity server reads Render's `PORT` environment variable before starting.
- Render gives the service an HTTPS/WSS domain such as `aldenfall-test-server.onrender.com`.
- In the WebGL login screen, use only the Render hostname, without `https://` or `wss://`.

Render Free caveats:

- It can spin down after idle time.
- First connection after spin-down can take about a minute.
- This is suitable for a 2-4 person smoke test, not a persistent MMO server.

Later VPS/Caddy upgrade:

- Run `Builds/LinuxServer/AldenFallServer.x86_64 -batchmode -nographics`.
- Reverse proxy `game.yourdomain.com` to `127.0.0.1:27777` with Caddy or Nginx.
- Point the WebGL login screen at `game.yourdomain.com`.

Current test scope:

- no accounts
- no saving
- 2-4 people
- character selection at connect time
- live movement/rotation/animation replication
