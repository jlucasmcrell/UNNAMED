"""Pack a PBR ground material from the external depot into the terrain renderer's two textures (Phase B, B0.1).

Terrain3D takes a texture layer as two same-size images: albedo RGB + height A, and normal RGB (OpenGL, Y+) + roughness A.
Poly Haven ships diffuse, nor_gl (OpenGL normals - Godot's convention), arm (AO, roughness, metal) and, for some, displacement;
where there is no displacement the height channel is the albedo's luminance, rescaled, which is what the blend by height needs.

    python _pack_terrain_texture.py <depot material folder> <asset id> [--size 2048] [--assets G:\\UNNAMED_PHASEB\\assets]

Writes <assets>/materials/<id>/<id>_albedo_height.png, <id>_normal_rough.png and <id>_material.json. The record carries the
depot provenance (pack, author, source URL, license, attribution) and the SHA-256 of every source file, so the imported asset
traces back to exactly what was downloaded.
"""
import argparse
import glob
import hashlib
import json
import os

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(8 << 20), b""):
            h.update(block)
    return h.hexdigest()


def find(folder, *keys):
    for key in keys:
        hits = [p for p in glob.glob(os.path.join(folder, "*")) if key in os.path.basename(p).lower()]
        if hits:
            return sorted(hits)[0]
    return None


def load(path, size, mode):
    return Image.open(path).convert(mode).resize((size, size), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("asset_id")
    ap.add_argument("--size", type=int, default=2048)
    ap.add_argument("--assets", default=r"G:\UNNAMED_PHASEB\assets")
    a = ap.parse_args()

    diffuse = find(a.source, "_diffuse", "_diff", "color", "albedo")
    normal = find(a.source, "nor_gl", "normalgl", "_normal")
    arm = find(a.source, "_arm")
    rough = find(a.source, "roughness", "_rough")
    disp = find(a.source, "displacement", "_disp", "height")
    if not diffuse or not normal or not (arm or rough):
        raise SystemExit(f"{a.source}: needs a diffuse, an OpenGL normal and a roughness (arm or roughness) map")
    if "normaldx" in os.path.basename(normal).lower():
        raise SystemExit(f"{normal}: DirectX normals; the terrain wants OpenGL (Y+)")

    albedo = np.asarray(load(diffuse, a.size, "RGB"), dtype=np.float32)
    if disp:
        height = np.asarray(load(disp, a.size, "L"), dtype=np.float32)
        height_src = os.path.basename(disp)
    else:
        lum = albedo @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        lo, hi = np.percentile(lum, 1), np.percentile(lum, 99)
        height = np.clip((lum - lo) / max(hi - lo, 1e-3), 0, 1) * 255.0
        height_src = "albedo luminance (no displacement map shipped)"
    nrm = np.asarray(load(normal, a.size, "RGB"), dtype=np.float32)
    if arm:
        roughness = np.asarray(load(arm, a.size, "RGB"), dtype=np.float32)[..., 1]
    else:
        roughness = np.asarray(load(rough, a.size, "L"), dtype=np.float32)

    out = os.path.join(a.assets, "materials", a.asset_id)
    os.makedirs(out, exist_ok=True)
    ah = os.path.join(out, f"{a.asset_id}_albedo_height.png")
    nr = os.path.join(out, f"{a.asset_id}_normal_rough.png")
    Image.fromarray(np.dstack([albedo, height]).clip(0, 255).astype(np.uint8), "RGBA").save(ah)
    Image.fromarray(np.dstack([nrm, roughness]).clip(0, 255).astype(np.uint8), "RGBA").save(nr)

    provenance = {}
    prov_path = os.path.join(a.source, "provenance.json")
    if os.path.exists(prov_path):
        provenance = json.load(open(prov_path, encoding="utf-8"))
    sources = [p for p in (diffuse, normal, arm, rough, disp) if p]
    record = {
        "asset_id": a.asset_id,
        "kind": "terrain_texture_layer",
        "maps": {"albedo_height": os.path.basename(ah), "normal_rough": os.path.basename(nr)},
        "size_px": a.size,
        "height_from": height_src,
        "normal_convention": "OpenGL (Y+)",
        "external": {
            "depot_folder": a.source,
            "pack": provenance.get("pack_name"),
            "author": provenance.get("author_provider"),
            "source_url": provenance.get("url"),
            "license": provenance.get("license"),
            "license_url": provenance.get("license_url"),
            "attribution_required": provenance.get("attribution_required"),
            "source_files": {os.path.basename(p): sha256(p) for p in sources},
        },
        "outputs": {os.path.basename(ah): sha256(ah), os.path.basename(nr): sha256(nr)},
    }
    with open(os.path.join(out, f"{a.asset_id}_material.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=1)
    print(f"PACKED {a.asset_id}: {a.size}px, height from {height_src}, license {record['external']['license']}")


if __name__ == "__main__":
    main()
