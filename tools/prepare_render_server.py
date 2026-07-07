#!/usr/bin/env python3
import os
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Builds" / "LinuxServer"
TARGET = ROOT / "Deploy" / "Render" / "server"


def main() -> int:
    if not SOURCE.exists():
        print("Missing Builds/LinuxServer.")
        print("Run Unity menu: AldenFall > Multiplayer > Build Linux Dedicated Server")
        return 1

    binaries = list(SOURCE.glob("*.x86_64"))
    if not binaries:
        print("No *.x86_64 server binary found in Builds/LinuxServer.")
        print("Run Unity menu: AldenFall > Multiplayer > Build Linux Dedicated Server")
        return 1

    if TARGET.exists():
        shutil.rmtree(TARGET)
    shutil.copytree(SOURCE, TARGET)

    for binary in TARGET.glob("*.x86_64"):
        mode = binary.stat().st_mode
        binary.chmod(mode | 0o111)

    rel = TARGET.relative_to(ROOT)
    print(f"Prepared Render server bundle at {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
