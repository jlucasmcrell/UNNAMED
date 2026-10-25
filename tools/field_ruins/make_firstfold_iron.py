"""The Spill's iron as a world material (docs/FIELD_RUINS.md): material_firstfold_iron, worn over the ruin pieces' own relief.

Firstfold iron (WORLD_MATERIALS.md): dark, cool, its grain running the wrong way to the light, and it never rusts. The ruin pieces
are Red Echo and Monolith modules at megastructure scale, so their own panelling is metres across; this lays man-sized plates,
seams and rivets, the grain, pitting and bright scuffs, and the tally marks people have cut into it, at the scale a walker sees.
The colour map's alpha marks where an edge is chipped back to bright iron (the worn shader's edge_wear reads it). Every field is
periodic over the tile, so the triplanar repeat has no seam.

  python tools/field_ruins/make_firstfold_iron.py --out D:/Otherreach_Depot/field_ruins_4k/materials
"""
import argparse
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw

ID = "material_firstfold_iron"
SIZE = 4096
TILE_M = 8.0
rng = np.random.default_rng(23)


def waves(count, lo, hi, direction=None):
    """Summed cosines on integer wave vectors (so the field tiles); with a direction they crowd round it and the field streaks."""
    axis = np.arange(SIZE, dtype=np.float32) / SIZE
    out = np.zeros((SIZE, SIZE), np.float32)
    for _ in range(count):
        m = int(rng.integers(lo, hi))
        if direction:
            a, b = direction[0] * m + int(rng.integers(-1, 2)), direction[1] * m + int(rng.integers(-1, 2))
        else:
            a, b = (int(v) for v in rng.integers(-hi, hi + 1, 2))
        phase = rng.uniform(0, 2 * np.pi)
        # cos(u + v) = cos u cos v - sin u sin v: two outer products instead of a full-size argument.
        u, v = 2 * np.pi * a * axis + phase, 2 * np.pi * b * axis
        out += (np.outer(np.cos(v), np.cos(u)) - np.outer(np.sin(v), np.sin(u))) / math.sqrt(max(m, 1))
    return (out - out.min()) / (out.max() - out.min())


def px(metres):
    return int(round(metres / TILE_M * SIZE))


def plates():
    """Staggered plates, 2 m by 1 m: each its own tone, a dark seam between them and rivets along it. Returns tone, seam, rivet."""
    tone = np.zeros((SIZE, SIZE), np.float32)
    seam = Image.new("L", (SIZE, SIZE), 0)
    rivet = Image.new("L", (SIZE, SIZE), 0)
    ds, dr = ImageDraw.Draw(seam), ImageDraw.Draw(rivet)
    w, h = px(2.0), px(1.0)
    for row in range(SIZE // h):
        shift = (w // 2) * (row % 2)
        y0 = row * h
        ds.rectangle([0, y0 - 3, SIZE, y0 + 3], fill=255)
        for col in range(SIZE // w + 1):
            x0 = (col * w + shift) % SIZE
            value = float(rng.normal(0, 0.035))
            for xa in (x0, x0 - SIZE):
                tone[y0:y0 + h, max(xa, 0):max(min(xa + w, SIZE), 0)] = value
                ds.rectangle([xa - 3, y0, xa + 3, y0 + h], fill=255)
            for k in range(1, 8):
                for cx, cy in (((x0 + k * w // 8) % SIZE, y0 + 14), (x0 + 14, y0 + k * h // 8)):
                    dr.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=255)
    return tone, np.asarray(seam, np.float32) / 255, np.asarray(rivet, np.float32) / 255


def tallies():
    """Count-marks cut into the iron: fives of upright strokes struck through, a few groups to a cluster, a few clusters to a tile."""
    cut = Image.new("L", (SIZE, SIZE), 0)
    d = ImageDraw.Draw(cut)
    tall, step = px(0.3), px(0.06)
    for _ in range(4):
        x, y = int(rng.integers(px(0.6), SIZE - px(1.6))), int(rng.integers(px(0.6), SIZE - px(1.2)))
        count = int(rng.integers(3, 19))
        for i in range(count):
            group, k = divmod(i, 5)
            gx = x + group * step * 6
            if k < 4:
                lean = int(rng.integers(-6, 7))
                d.line([gx + k * step, y, gx + k * step + lean, y + tall], fill=255, width=9)
            else:
                d.line([gx - step // 2, y + tall * 3 // 4, gx + step * 3 + step // 2, y + tall // 4], fill=255, width=9)
    return np.asarray(cut, np.float32) / 255


def blur(a, r):
    out = a.copy()
    for axis in (0, 1):
        acc = np.zeros_like(out)
        for d in range(-r, r + 1):
            acc += np.roll(out, d, axis)
        out = acc / (2 * r + 1)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    folder = os.path.join(ap.parse_args().out, ID)
    os.makedirs(folder, exist_ok=True)

    grain = waves(56, 10, 220, direction=(1, -2))
    mottle = waves(16, 1, 6)
    tone, seam, rivet = plates()
    cut = tallies()
    pits = (rng.random((SIZE, SIZE), dtype=np.float32) > 0.996).astype(np.float32)
    pits = blur(pits, 2) * 5
    scuff = np.clip((waves(24, 4, 40) - 0.72) * 4, 0, 1) * np.clip(grain * 1.6 - 0.3, 0, 1)
    seam_soft = blur(seam, 3)
    # Run-off: dark streaks hanging from each plate's upper seam, fading down the plate.
    below = (np.arange(SIZE, dtype=np.float32) % px(1.0)) / px(1.0)
    runoff = waves(40, 30, 400, direction=(1, 0)) ** 3 * np.exp(-below * 3.5)[:, None]
    cut_soft = blur(cut, 2)

    # Linear albedo about 0.14 (sRGB ~105): dark iron that still shows its plates, tallies and wear in shade at game distance.
    luma = np.clip(0.13 + 0.05 * grain + 0.03 * mottle - 0.07 * runoff + 0.5 * tone + 0.14 * scuff - 0.08 * seam_soft - 0.1 * cut_soft - 0.05 * pits + 0.04 * rivet, 0.01, 1)
    colour = np.clip(luma[..., None] * np.array([0.9, 0.94, 1.04], np.float32), 0, 1)
    # Where an edge may be chipped back to bright iron (alpha 0) and where it holds (alpha 1).
    chip = 1 - np.clip((waves(20, 6, 60) - 0.55) * 5, 0, 1)
    occlusion = np.clip(1 - 0.55 * seam_soft - 0.6 * cut_soft - 0.3 * pits, 0, 1)
    rough = np.clip(0.58 + 0.16 * mottle - 0.1 * grain - 0.25 * scuff + 0.2 * cut_soft, 0, 1)
    metal = np.clip(0.3 + 0.5 * scuff - 0.2 * cut_soft, 0, 1)
    height = 0.5 * grain + 0.3 * mottle - seam_soft - 1.2 * cut_soft - 0.5 * pits + 0.8 * blur(rivet, 2)
    dx, dy = np.roll(height, -1, 1) - np.roll(height, 1, 1), np.roll(height, -1, 0) - np.roll(height, 1, 0)
    n = np.stack([-dx * 4, dy * 4, np.ones_like(dx)], axis=2)   # OpenGL: +Y up the image
    n /= np.linalg.norm(n, axis=2, keepdims=True)

    def save(name, a):
        Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(os.path.join(folder, f"{ID}_{name}.png"))

    save("basecolor", np.concatenate([colour ** (1 / 2.2), chip[..., None]], axis=2))
    save("orm", np.stack([occlusion, rough, metal], axis=2))
    save("normal", n * 0.5 + 0.5)
    record = {
        "asset_id": ID,
        "material_class": "metal",
        "tile_size_m": TILE_M,
        "resolution": SIZE,
        "maps": {"basecolor": f"{ID}_basecolor.png", "normal": f"{ID}_normal.png", "orm": f"{ID}_orm.png"},
        "pbr": {"roughness_base": 0.58, "metallic": 0.3, "normal_strength": 1.0},
        "tileable": True,
        "generator": {"script": "tools/field_ruins/make_firstfold_iron.py", "method": "periodic numpy fields and drawn plates and tallies"},
        "note": "ORM packs occlusion, roughness and metallic as glTF does. The colour map's alpha is the chip mask edge_wear reads.",
    }
    json.dump(record, open(os.path.join(folder, f"{ID}_material.json"), "w"), indent=2)
    print(folder, "luma mean", round(float((colour ** (1 / 2.2)).mean()) * 255, 1))


main()
