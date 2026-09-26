"""A garment's textures from a sourced tiling PBR set (charstd, CHARACTER_ASSET_STANDARD.md section 14): the set repeated over the
garment's own UV square, tinted, and modulated by the garment's own diffuse where it has one (its baked folds, seams and stitching kept
as light and dark), so a sourced garment takes a production material without re-authoring its UVs.

    python garment_texture.py --set <dir or prefix of a *_Color/_NormalGL/_Roughness set> --out <dir> [--src-diffuse <png>]
        [--tiles 6] [--tint 0.12,0.11,0.1] [--detail 0.6] [--rough-scale 1.0] [--rough-min 0.0] [--size 2048]

Writes <out>/base_color.png, normal.png, roughness.png.
"""
import argparse
import glob
import os

import numpy as np
from PIL import Image


def find(prefix, key):
    hits = sorted(glob.glob(prefix + f"*{key}*.jpg") + glob.glob(prefix + f"*{key}*.png"))
    return hits[0] if hits else None


def tiled(path, size, tiles, mode):
    img = Image.open(path).convert(mode)
    cell = max(8, size // tiles)
    img = img.resize((cell, cell), Image.LANCZOS)
    out = Image.new(mode, (cell * tiles, cell * tiles))
    for y in range(tiles):
        for x in range(tiles):
            out.paste(img, (x * cell, y * cell))
    return out.resize((size, size), Image.LANCZOS)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--set", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--src-diffuse")
    p.add_argument("--tiles", type=int, default=6)
    p.add_argument("--tint", default="")
    p.add_argument("--detail", type=float, default=0.6)
    p.add_argument("--rough-scale", type=float, default=1.0)
    p.add_argument("--rough-min", type=float, default=0.0)
    p.add_argument("--size", type=int, default=2048)
    a = p.parse_args()
    prefix = os.path.join(a.set, "") if os.path.isdir(a.set) else a.set
    os.makedirs(a.out, exist_ok=True)
    colour = np.asarray(tiled(find(prefix, "_Color"), a.size, a.tiles, "RGB"), np.float32) / 255.0
    if a.tint:
        tint = np.array([float(x) for x in a.tint.split(",")], np.float32)
        lum = colour @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        colour = tint[None, None] * (lum / max(float(lum.mean()), 1e-3))[..., None]
    if a.src_diffuse and os.path.exists(a.src_diffuse):
        src = np.asarray(Image.open(a.src_diffuse).convert("RGB").resize((a.size, a.size), Image.LANCZOS), np.float32) / 255.0
        slum = src @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        slum = slum / max(float(np.median(slum)), 1e-3)
        colour = colour * (1.0 + a.detail * (np.clip(slum, 0.2, 1.8) - 1.0))[..., None]
    Image.fromarray((np.clip(colour, 0, 1) * 255).astype(np.uint8)).save(os.path.join(a.out, "base_color.png"))
    tiled(find(prefix, "_NormalGL"), a.size, a.tiles, "RGB").save(os.path.join(a.out, "normal.png"))
    rough = np.asarray(tiled(find(prefix, "_Roughness"), a.size, a.tiles, "L"), np.float32) / 255.0
    rough = np.maximum(rough * a.rough_scale, a.rough_min)
    Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8)).save(os.path.join(a.out, "roughness.png"))
    print("GARMENT_TEXTURE", a.out)


if __name__ == "__main__":
    main()
