"""Compose flipbook cells onto a dark ground so alpha reads, for review.

An RGBA atlas viewed raw is white-on-transparent and says almost nothing about whether the effect
has a readable shape. This samples cells from each atlas, draws them the way the manifest says the
engine will, and lays them out in rows.

Usage:
    python _vfx_review.py --group offensive
    python _vfx_review.py --group support
"""
import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw

ASSETS = r"W:\UNNAMED\assets"
MANIFEST = os.path.join(ASSETS, "manifests", "magic_vfx.json")
OUT = os.path.join(ASSETS, "review", "vfx")

GROUPS = {
    "offensive": ["vfx.magic.cast_charge", "vfx.force.impulse_bolt_travel",
                  "vfx.force.impulse_bolt_impact"],
    "support": ["vfx.warding.brace_ward_shell", "vfx.vital.mending_thread_restore",
                "vfx.resonance.strain_overlay"],
}
SAMPLES = 5
CELL = 190
LABEL = 24
GROUND = (34, 36, 42)


def render_cell(frame, blend_mode):
    """Scale one flipbook cell and draw it the way the manifest says the engine will."""
    scaled = frame.resize((CELL, CELL), Image.LANCZOS)
    base = np.full((CELL, CELL, 3), GROUND, dtype=np.float64)
    rgb = np.asarray(scaled.convert("RGB"), dtype=np.float64)
    alpha = np.asarray(scaled.getchannel("A"), dtype=np.float64)[..., None] / 255.0
    if blend_mode == "add":
        # Additive, as Godot's blend_mode add does: RGB scaled by alpha, then summed and clamped.
        out = base + rgb * alpha
    else:
        out = base * (1.0 - alpha) + rgb * alpha
    return Image.fromarray(np.clip(out, 0, 255).astype("uint8"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--group", choices=sorted(GROUPS), required=True)
    args = parser.parse_args()

    with open(MANIFEST, encoding="utf-8") as handle:
        effects = json.load(handle)["effects"]

    names = GROUPS[args.group]
    sheet = Image.new("RGB", (CELL * SAMPLES, (CELL + LABEL) * len(names)), (26, 28, 34))
    draw = ImageDraw.Draw(sheet)

    for row, name in enumerate(names):
        effect = effects[name]
        atlas = Image.open(os.path.join(ASSETS, effect["atlas"])).convert("RGBA")
        cell = effect["cell_px"]
        columns = effect["grid"]["columns"]
        count = effect["frame_count"]
        picks = sorted({round(i * (count - 1) / (SAMPLES - 1)) for i in range(SAMPLES)})
        top = row * (CELL + LABEL)
        draw.text((8, top + 6),
                  f"{name}   {count} frames @ {effect['fps']} fps   "
                  f"blend={effect['blend']['blend_mode']}   {effect['kind']}",
                  fill=(232, 232, 228))
        for column, index in enumerate(picks):
            x, y = (index % columns) * cell, (index // columns) * cell
            frame = atlas.crop((x, y, x + cell, y + cell))
            sheet.paste(render_cell(frame, effect["blend"]["blend_mode"]),
                        (column * CELL, top + LABEL))
            draw.text((column * CELL + 6, top + LABEL + 4), f"f{index}", fill=(180, 180, 175))

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"vfx_review_{args.group}.png")
    sheet.save(path)
    print(f"{path}  {sheet.width}x{sheet.height}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
