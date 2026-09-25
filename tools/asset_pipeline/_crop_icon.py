"""Crop cells out of the UI icon atlas so individual icons can actually be judged.

A 6x5 atlas at 128 px reaches the vision budget downscaled to roughly 125 px per cell, which is
enough to see that an icon is unreadable but not enough to see what it shows. Reading the atlas
ordering out of the manifest and cropping the named cell gives a full-size look at the one icon
that matters, instead of guessing at thumbnails.

Usage:
    python _crop_icon.py ui.status.wounded out.png
"""
import argparse
import io
import json
import os

from PIL import Image

ASSETS = os.environ.get("UNNAMED_ASSETS", r"W:\UNNAMED\assets")
MANIFEST = os.path.join(ASSETS, "manifests", "ui_icons.json")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("slot")
    parser.add_argument("out")
    args = parser.parse_args()

    with io.open(MANIFEST, encoding="utf-8") as handle:
        doc = json.load(handle)
    atlas = doc["atlas"]
    order = atlas["order"]
    if args.slot not in order:
        raise SystemExit(f"'{args.slot}' is not in the atlas; slots: {order}")

    index = order.index(args.slot)
    cell = atlas["cell_px"]
    columns = atlas["columns"]
    column, row = index % columns, index // columns

    source = os.path.join(ASSETS, atlas["path"])
    with Image.open(source) as image:
        box = (column * cell, row * cell, (column + 1) * cell, (row + 1) * cell)
        crop = image.crop(box)
        # Upscale so the harness does not shrink it below what the eye needs.
        crop = crop.resize((crop.width * 3, crop.height * 3), Image.NEAREST)
        crop.save(args.out)
    print(f"  {args.slot}  cell {index} (col {column}, row {row})  ->  {args.out}")


if __name__ == "__main__":
    raise SystemExit(main())
