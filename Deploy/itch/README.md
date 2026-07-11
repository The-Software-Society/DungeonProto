# AldenFall → itch.io (private friend-test distribution)

This is the desktop distribution path: a faithful Mac/Windows standalone build
(what you see in the Play button) handed to friends through itch.io, with
**auto-patching** so updates are a one-click download for them — like Steam,
but free and private. When Steam-ready (a few months out), the same builds go up
via SteamPipe instead; nothing here is wasted.

## Why desktop (not WebGL) for the real test
A desktop standalone runs the same scripting runtime, physics, threading, and
network transport as the Editor — so "works in Play, breaks in the build" mostly
goes away. WebGL stays as an optional instant-link share; re-verify there before
sending a WebGL link.

## One-time setup (you do these once)
1. **Make an itch.io account** and create a new project.
   - Set **Visibility → Draft** (or Restricted) so nothing is public.
   - Kind: **Downloadable**. Note the slug: `https://<user>.itch.io/<game>` → target is `<user>/<game>`.
2. **Install Butler** (itch's upload CLI). NOTE: `brew install butler` is the WRONG tool
   (a Mac Cask app), and itch's old `broth.itch.ovh` host is dead — use `broth.itch.zone`:
   ```bash
   curl -L -o /tmp/butler.zip https://broth.itch.zone/butler/darwin-arm64/LATEST/archive/default
   mkdir -p ~/.butler && unzip -o /tmp/butler.zip -d ~/.butler && chmod +x ~/.butler/butler
   ln -sf ~/.butler/butler "$(brew --prefix)/bin/butler"
   butler version   # verify
   ```
   (Windows/Linux: swap the channel to `windows-amd64` / `linux-amd64`.)
3. **Log in:** `butler login` (opens a browser, authorizes this machine with your itch API key).

## Every update (repeatable loop)
1. In Unity: **AldenFall ▸ Multiplayer ▸ Build All Desktop (Mac + Windows)**
   - Or headless once Unity is closed:
     `"/Applications/Unity/Hub/Editor/6000.3.2f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod AldenFall.Editor.MultiplayerBuildTools.BuildAllDesktopCI`
   - Bump `bundleVersion` in Project Settings before a release so patches order correctly.
2. Push:  `Deploy/itch/push-itch.sh <user>/<game>`
   - e.g. `Deploy/itch/push-itch.sh thesoftwaresociety/aldenfall`
3. Butler uploads only the changed bytes to channels `mac` and `windows`.

## What your friend does
- Install the **itch app** once (https://itch.io/app), open your project link
  (share the private link with them), click **Install**.
- On every update: the itch app shows an update and **patches** in place — no
  re-download of the whole game.
- First launch may still show a Gatekeeper/SmartScreen "unidentified developer"
  warning (unsigned build): right-click → Open on Mac, "More info → Run anyway" on
  Windows. (Signing/notarization removes this later; not needed for a friend test.)

## Multiplayer note
Desktop and WebGL clients both connect to the same Mirror dedicated server
(Render.com) via SimpleWebTransport — the platform doesn't change networking.
Desktop can also **Host** locally for solo/LAN testing.
