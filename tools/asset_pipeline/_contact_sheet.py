"""Build contact sheets of the concept library for eyeball review.

The structural checks in _qc_concepts.py can tell whether an image is neatly framed;
they cannot tell whether it depicts a plausible object. Only a human or a vision model
can judge "that crossbow's bow is mounted sideways". This makes that review cheap:
one small labelled thumbnail per concept, a page at a time.

Usage:
    python _contact_sheet.py                      # all categories
    python _contact_sheet.py --prefix weapon_     # one category
    python _contact_sheet.py --prefix weapon_ --per-sheet 40
"""
import argparse
import glob
import os
import sys

from PIL import Image, ImageDraw

ASSETS = r"W:\UNNAMED\assets"
THUMB = 200
LABEL_H = 18
COLUMNS = 8


def build_sheet(paths, out_path, title):
    rows = (len(paths) + COLUMNS - 1) // COLUMNS
    width = COLUMNS * THUMB
    height = rows * (THUMB + LABEL_H) + 26
    sheet = Image.new("RGB", (width, height), (24, 24, 28))
    draw = ImageDraw.Draw(sheet)
    draw.text((8, 7), title, fill=(235, 235, 235))

    for index, path in enumerate(paths):
        column = index % COLUMNS
        row = index // COLUMNS
        x = column * THUMB
        y = 26 + row * (THUMB + LABEL_H)
        try:
            thumb = Image.open(path).convert("RGB")
            thumb.thumbnail((THUMB, THUMB), Image.LANCZOS)
        except OSError:
            continue
        sheet.paste(thumb, (x + (THUMB - thumb.width) // 2,
                            y + (THUMB - thumb.height) // 2))
        name = os.path.basename(path)[:-4]
        # Strip the category prefix so more of the distinguishing name fits.
        label = name.split("_", 1)[1] if "_" in name else name
        draw.text((x + 3, y + THUMB + 3), label[:30], fill=(200, 200, 205))

    sheet.save(out_path, quality=88)
    return out_path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default=ASSETS)
    parser.add_argument("--prefix", default=None,
                        help="Only concepts starting with this prefix")
    parser.add_argument("--per-sheet", type=int, default=64)
    args = parser.parse_args()

    concepts_dir = os.path.join(args.assets, "concepts")
    paths = sorted(glob.glob(os.path.join(concepts_dir, "*.png")))
    if args.prefix:
        paths = [p for p in paths if os.path.basename(p).startswith(args.prefix)]
    if not paths:
        print("no concepts matched")
        return 1

    out_dir = os.path.join(args.assets, "review")
    os.makedirs(out_dir, exist_ok=True)

    sheets = []
    for start in range(0, len(paths), args.per_sheet):
        batch = paths[start:start + args.per_sheet]
        label = args.prefix.rstrip("_") if args.prefix else "all"
        index = start // args.per_sheet + 1
        title = f"{label}  {start + 1}-{start + len(batch)} of {len(paths)}"
        name = f"sheet_{label}_{index:02d}.jpg"
        sheets.append(build_sheet(batch, os.path.join(out_dir, name), title))

    print(f"{len(paths)} concept(s) -> {len(sheets)} sheet(s) in {out_dir}")
    for sheet in sheets:
        print(f"  {os.path.basename(sheet)}")
    print()
    print("Open the sheets, note any concept that does not look like a functional")
    print("object of its type, and reject it:")
    print("  python _reject_concept.py <concept_id> --reason \"bow mounted sideways\"")
    return 0


if __name__ == "__main__":
    sys.exit(main())
