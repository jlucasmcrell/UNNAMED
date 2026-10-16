"""Sort a pack's exported meshes into building kit categories and build labelled contact sheets.

  python inventory.py C:\\Otherreach_External_Assets\\UE_Exports\\FantasyCastle [more packs...] --sheets <dir>

Needs <pack>/_thumbs/<key>.png from thumbs_blender.py (missing thumbnails get a grey tile). Writes
<sheets>/<Pack>_<category>[_n].png and appends the per-pack listing to <sheets>/INVENTORY.txt.
Categories come from the asset name only - check the sheets, names lie.
"""
import argparse
import glob
import json
import os
import re

from PIL import Image, ImageDraw

CATEGORIES = [
    ("house", r"house|accom|build|home|hut|cottage|tower|keep|castle(?!_?wall)|temple|pyramid|palace|shrine|church|"
              r"chapel|tavern|barn|mill|hall|gatehouse|stable"),
    ("roof", r"roof|shingle|thatch|gable|chimney|dome|spire"),
    ("door_gate", r"door|gate|portcullis"),
    ("window", r"window|shutter|balkon|balcon"),
    ("wall", r"wall|fence|rampart|battlement|parapet|merlon|railing|rail"),
    ("structure", r"floor|ceil|stair|step|pillar|column|colmn|arch|beam|trim|bridge|plinth|base|platform|deck|"
                  r"corner|edge|cornice|frame|module|dan"),
    ("rock_terrain", r"rock|cliff|boulder|stone(?!.*wall)|ground|terrain|landscape|mound|rubble|debris"),
    ("foliage", r"foliage|tree|plant|grass|bush|ivy|vine|leaf|leaves|fern|flower|moss|root|branch|log|trunk|palm"),
]
THUMB, LABEL_H, COLUMNS, PER_SHEET = 200, 30, 8, 48


def category(key):
    name = key.split("__")[-1].lower()
    path = key.lower()
    for cat, rx in CATEGORIES:
        if re.search(rx, name):
            return cat
    return "foliage" if "foliage" in path else "prop"


def sheet(entries, root, path, title):
    rows = (len(entries) + COLUMNS - 1) // COLUMNS
    img = Image.new("RGB", (COLUMNS * THUMB, rows * (THUMB + LABEL_H) + 28), (24, 24, 28))
    draw = ImageDraw.Draw(img)
    draw.text((8, 8), title, fill=(230, 230, 230))
    for i, m in enumerate(entries):
        x, y = (i % COLUMNS) * THUMB, 28 + (i // COLUMNS) * (THUMB + LABEL_H)
        tp = os.path.join(root, "_thumbs", m["key"] + ".png")
        tile = Image.open(tp).convert("RGB").resize((THUMB, THUMB)) if os.path.exists(tp) else Image.new("RGB", (THUMB, THUMB), (70, 70, 70))
        img.paste(tile, (x, y))
        s = m["bounds_cm"]["size"]
        draw.text((x + 3, y + THUMB + 1), m["key"].split("__")[-1][:30], fill=(235, 235, 235))
        draw.text((x + 3, y + THUMB + 14), f"{s[0] / 100:.1f}x{s[1] / 100:.1f}x{s[2] / 100:.1f}m {m.get('triangles_lod0') or 0:,}t",
                  fill=(160, 200, 160))
    img.save(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("packs", nargs="+")
    ap.add_argument("--sheets", required=True)
    a = ap.parse_args()
    os.makedirs(a.sheets, exist_ok=True)
    report = []
    for root in a.packs:
        pack = os.path.basename(os.path.normpath(root))
        mans = [json.load(open(f, encoding="utf-8")) for f in sorted(glob.glob(os.path.join(root, "Manifests", "*.json")))]
        groups = {}
        for m in mans:
            groups.setdefault(category(m["key"]), []).append(m)
        licence = mans[0]["licence"] if mans else ""
        report.append(f"== {pack}: {len(mans)} meshes ({licence.split(' - ')[0]})")
        for cat in [c for c, _ in CATEGORIES] + ["prop"]:
            entries = sorted(groups.get(cat, []), key=lambda m: m["key"])
            if not entries:
                continue
            pages = [entries[i:i + PER_SHEET] for i in range(0, len(entries), PER_SHEET)]
            for n, page in enumerate(pages):
                name = f"{pack}_{cat}" + (f"_{n + 1}" if len(pages) > 1 else "") + ".png"
                sheet(page, root, os.path.join(a.sheets, name), f"{pack} / {cat} ({len(entries)}) page {n + 1}/{len(pages)}")
            names = ", ".join(f"{m['key'].split('__')[-1]} ({'x'.join(f'{v / 100:.1f}' for v in m['bounds_cm']['size'])} m)" for m in entries)
            report.append(f"  {cat} [{len(entries)}]: {names}")
    with open(os.path.join(a.sheets, "INVENTORY.txt"), "a", encoding="utf-8") as fh:
        fh.write("\n".join(report) + "\n")
    print("\n".join(line[:300] for line in report))


if __name__ == "__main__":
    main()
