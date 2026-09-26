"""Re-dyed garment and hair textures for an MPFB character (the reference outfit on the CC0 wardrobe's own geometry and shading):
a colour laid over each source texture's luminance (its folds, weave, seams and wear stay; its print and dye go), with grime.

    python garment_textures.py --spec specs/player.json --mpfb-data <MPFB user data dir> --out <dir>

The spec's "garments" block: {name: {source: "<clothes or hair folder>/<file>", colour: [r, g, b] (sRGB 0-1), use: "luma"|"ao",
knit: true|false, grime: 0-1}}.
"""
import argparse
import json
import os

import numpy as np
from PIL import Image


def noise(n, scale, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((max(2, n // scale), max(2, n // scale)))
    return np.asarray(Image.fromarray((small * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC), np.float32) / 255


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", required=True)
    ap.add_argument("--mpfb-data", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    garments = json.load(open(a.spec))["mpfb"]["garments"]
    for name, g in garments.items():
        src = Image.open(os.path.join(a.mpfb_data, g["source"]))
        alpha = np.asarray(src.convert("RGBA"), np.float32)[..., 3] / 255 if "A" in src.getbands() else None
        rgb = np.asarray(src.convert("RGB"), np.float32) / 255
        n = rgb.shape[0]
        lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
        # Shading only: the luminance normalised about its median (a print or dye is flattened by the clamp).
        shade = np.clip(lum / max(np.median(lum), 1e-3), 0.35, 1.35)
        if g.get("use") == "ao":
            shade = np.clip(lum, 0.3, 1.0) / max(np.percentile(lum, 90), 1e-3)
        col = np.array(g["colour"], np.float32) ** 2.2          # sRGB -> linear, shaded, back
        lin = col[None, None, :] * shade[..., None] ** 1.1
        if g.get("knit"):
            y = np.arange(n, dtype=np.float32)[:, None]
            x = np.arange(n, dtype=np.float32)[None, :]
            ribs = 0.92 + 0.08 * np.sin(x * (2 * np.pi / 6.0)) * (0.7 + 0.3 * np.sin(y * (2 * np.pi / 9.0)))
            lin = lin * ribs[..., None]
        if g.get("grime", 0) > 0:
            dirt = noise(n, 64, 1) * 0.6 + noise(n, 16, 2) * 0.4
            lin = lin * (1 - g["grime"] * 0.45 * np.clip(dirt - 0.35, 0, 1)[..., None] * 1.6)
        out = np.clip(lin, 0, 1) ** (1 / 2.2)
        img = (out * 255).astype(np.uint8)
        if alpha is not None:
            img = np.dstack([img, (alpha * 255).astype(np.uint8)])
        Image.fromarray(img).save(os.path.join(a.out, name + ".png"))
        print("GARMENT", name, img.shape)


if __name__ == "__main__":
    main()
