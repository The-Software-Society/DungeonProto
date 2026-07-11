#!/usr/bin/env python3
"""
Extract base-color textures from Meshy GLB files.

Meshy's FBX exports don't carry textures, but the GLB twins embed them and
share UVs with the FBX. This walks Assets/Models, parses each GLB container
(JSON chunk + BIN chunk), follows material -> baseColorTexture -> image, and
writes `<Model>_baseColor.png` next to the model for Unity to pick up.

Usage: python3 tools/extract_glb_textures.py
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
MODELS_DIR = REPO_ROOT / "Assets" / "Models"


def parse_glb(path: Path):
    """Return (gltf_json, bin_bytes) from a .glb container."""
    data = path.read_bytes()
    magic, version, _length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:  # 'glTF'
        raise ValueError(f"{path.name}: not a GLB file")

    offset = 12
    gltf = None
    binary = b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8: offset + 8 + chunk_len]
        if chunk_type == 0x4E4F534A:  # 'JSON'
            gltf = json.loads(chunk)
        elif chunk_type == 0x004E4942:  # 'BIN'
            binary = chunk
        offset += 8 + chunk_len
    return gltf, binary


def image_bytes(gltf: dict, binary: bytes, image_index: int) -> tuple[bytes, str] | None:
    images = gltf.get("images", [])
    if image_index >= len(images):
        return None
    img = images[image_index]
    mime = img.get("mimeType", "image/png")
    ext = "jpg" if "jpeg" in mime else "png"
    if "bufferView" not in img:
        return None
    view = gltf["bufferViews"][img["bufferView"]]
    start = view.get("byteOffset", 0)
    return binary[start: start + view["byteLength"]], ext


def base_color_image_index(gltf: dict) -> int | None:
    """material -> pbrMetallicRoughness.baseColorTexture -> texture -> image"""
    for mat in gltf.get("materials", []):
        pbr = mat.get("pbrMetallicRoughness", {})
        tex_info = pbr.get("baseColorTexture")
        if tex_info is None:
            continue
        texture = gltf.get("textures", [])[tex_info["index"]]
        source = texture.get("source")
        if source is not None:
            return source
    # Fallback: first image in the file
    return 0 if gltf.get("images") else None


def extract(glb_path: Path, out_png: Path) -> bool:
    try:
        gltf, binary = parse_glb(glb_path)
        idx = base_color_image_index(gltf)
        if idx is None:
            print(f"  {glb_path.name}: no images")
            return False
        result = image_bytes(gltf, binary, idx)
        if result is None:
            print(f"  {glb_path.name}: image {idx} not embedded")
            return False
        payload, ext = result
        out = out_png.with_suffix(f".{ext}")
        out.write_bytes(payload)
        print(f"  {glb_path.name} -> {out.relative_to(REPO_ROOT)} ({len(payload) // 1024} KB)")
        return True
    except Exception as e:
        print(f"  {glb_path.name}: FAILED {e}")
        return False


def main():
    if not MODELS_DIR.exists():
        sys.exit("No Assets/Models directory")

    # Characters: texture from the rigged-character GLB (matches rigged FBX UVs)
    for char_dir in sorted((MODELS_DIR / "Characters").glob("*")):
        if not char_dir.is_dir():
            continue
        name = char_dir.name
        candidates = (
            sorted(char_dir.glob(f"{name}_rigged_rigged_character*.glb"))
            or sorted(char_dir.glob(f"{name}_game.glb"))
            or sorted(char_dir.glob(f"{name}_static.glb"))
        )
        if candidates:
            print(f"{name}:")
            extract(candidates[0], char_dir / f"{name}_baseColor.png")

    # Props
    props_dir = MODELS_DIR / "Props"
    if props_dir.exists():
        print("Props:")
        for glb in sorted(props_dir.glob("*.glb")):
            extract(glb, props_dir / f"{glb.stem}_baseColor.png")


if __name__ == "__main__":
    main()
