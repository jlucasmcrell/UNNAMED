"""Compose up to four asset renders into one labelled sheet, so a small batch can be judged in one read.

This is not `_contact_sheet.py`. That one triages the concept library: eight 200 px columns to a page,
which is the right shape for spotting a concept that depicts the wrong object across hundreds of
files, and the wrong shape for deciding whether a single built asset is correct. At 200 px across
eight columns the eye gets composition and nothing else.

This caps the grid at 2x2 and gives each cell a full 560 px, because the question it answers is
"is this prop the thing it is supposed to be" - which needs edges, material and silhouette detail.
It refuses more than four items rather than silently shrinking them, and prints each cell's position
so a verdict can be written against a specific asset.

Usage:
    python _render_sheet.py out.jpg label=render.jpg [label=render.jpg ...]
"""
import argparse
import os
import sys

from PIL import Image, ImageDraw

MAX_CELLS = 4


def load(path):
    """Open an image, normalising to RGB and preserving aspect inside a square cell."""
    with Image.open(path) as image:
        return image.convert("RGB")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("out")
    parser.add_argument("items", nargs="+", help="label=path pairs")
    parser.add_argument("--cell", type=int, default=560)
    args = parser.parse_args()

    if len(args.items) > MAX_CELLS:
        print(f"  {len(args.items)} items exceed the {MAX_CELLS}-cell limit; split into more sheets")
        return 1

    tiles, labels = [], []
    for item in args.items:
        label, _, path = item.partition("=")
        if not os.path.exists(path):
            print(f"  missing {path}")
            return 1
        image = load(path)
        image.thumbnail((args.cell, args.cell), Image.LANCZOS)
        cell = Image.new("RGB", (args.cell, args.cell), (26, 28, 32))
        cell.paste(image, ((args.cell - image.width) // 2, (args.cell - image.height) // 2))
        tiles.append(cell)
        labels.append(label)

    columns = 2 if len(tiles) > 1 else 1
    rows = (len(tiles) + columns - 1) // columns
    sheet = Image.new("RGB", (args.cell * columns, args.cell * rows), (26, 28, 32))
    for index, tile in enumerate(tiles):
        sheet.paste(tile, ((index % columns) * args.cell, (index // columns) * args.cell))

    draw = ImageDraw.Draw(sheet)
    for index, label in enumerate(labels):
        x = (index % columns) * args.cell + 10
        y = (index // columns) * args.cell + 10
        draw.rectangle([x - 4, y - 4, x + 7 * len(label) + 8, y + 18], fill=(0, 0, 0))
        draw.text((x, y + 2), label, fill=(255, 255, 255))

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    sheet.save(args.out, quality=92)
    print(f"  sheet {len(tiles)} cell(s) -> {args.out}  {sheet.size[0]}x{sheet.size[1]}")
    for index, label in enumerate(labels):
        print(f"    row {index // columns}, col {index % columns}  {label}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
