"""Lay icon concepts out as a labelled sheet so a set can be judged together.

Icons are read as a set, not one at a time: what matters is whether they share a palette, a weight
and a readable silhouette at small size, and that is only visible side by side.

Usage:
    python _icon_review.py --out assets/review/icons_hud.png health stamina focus resonance
"""
import argparse
import os

from PIL import Image, ImageDraw

CONCEPTS = os.path.join(os.environ.get("UNNAMED_ASSETS", r"W:\UNNAMED\assets"), "concepts")
CELL = 300
LABEL = 22
COLUMNS = 2


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--concept-prefix", default="icon_")
    parser.add_argument("names", nargs="+")
    args = parser.parse_args()

    tiles = []
    for name in args.names:
        for candidate in (name, f"{args.concept_prefix}{name}"):
            path = os.path.join(CONCEPTS, f"{candidate}.png")
            if os.path.exists(path):
                tiles.append((candidate, Image.open(path).convert("RGB")))
                break
        else:
            tiles.append((name, None))

    rows = (len(tiles) + COLUMNS - 1) // COLUMNS
    sheet = Image.new("RGB", (COLUMNS * CELL, rows * (CELL + LABEL)), (24, 24, 26))
    draw = ImageDraw.Draw(sheet)
    for index, (name, image) in enumerate(tiles):
        top = (index // COLUMNS) * (CELL + LABEL)
        left = (index % COLUMNS) * CELL
        draw.text((left + 8, top + 5), name, fill=(232, 232, 228))
        if image is None:
            draw.text((left + 8, top + LABEL + 8), "MISSING", fill=(230, 120, 120))
            continue
        # Shown at 128 px inside the cell as well, because an icon that only reads at 1536 px does
        # not work as an icon.
        tile = image.resize((CELL, CELL), Image.LANCZOS)
        sheet.paste(tile, (left, top + LABEL))
        small = image.resize((64, 64), Image.LANCZOS)
        sheet.paste(small, (left + CELL - 72, top + LABEL + 8))

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    sheet.save(args.out)
    print(f"{args.out}  {sheet.width}x{sheet.height}  {len(tiles)} tile(s)")


if __name__ == "__main__":
    main()
