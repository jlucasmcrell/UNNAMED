"""Phase B demo: more trees for Ashen Hollow's woods, as layout blockers (content/regions/ashen_hollow.yaml tree_NN circles), placed so
nothing anyone walks is blocked - clustered woodland with clearings, clear of every route the harnesses and the content bible use.

    python place_trees.py [--write] [--preview out.png]

Deterministic (seeded). Without --write it only prints and previews. Keep-clear sets (metres, 4.5 m either side of a route):
the playthrough's routes (src/Presentation/Playthrough.cs), the content bible's Charwood trail and the stream (section 6), the landmarks
and creature homes with their clearings, the Foldscar sight line from (175, 120), and a margin inside the region edge.
"""
import argparse
import math
import random
import re

import numpy as np

LAYOUT = "G:/UNNAMED_PHASEB/content/regions/ashen_hollow.yaml"
ROUTES = [
    [(30, 158), (55, 145), (78, 125), (105, 108)],                      # the bible's spine
    [(102, 142), (128, 150), (157, 162), (180, 138)],                   # the Charwood trail
    [(108, 148), (128, 150)], [(145, 156), (157, 158.8)], [(165, 150), (175, 142), (179, 138.3)],
    [(179, 138.3), (170, 118), (150, 106), (125, 102), (100, 110), (80, 106), (63, 98)],
    [(90, 112), (105, 108), (120, 93), (140, 80)],
    [(150, 45), (135, 70), (100, 100), (80, 118), (62, 130), (48, 137)],
    [(55, 136), (66, 140), (66, 150), (90, 162), (104, 168), (112, 172)],
    [(160, 65), (187, 65), (194, 45), (181, 31)],                       # the safer route round the spider
    [(88, 145), (102, 142)], [(128, 150), (151, 128)],                  # to the woundmoss
]
STREAM = [(190, 195), (170, 165), (150, 132), (132, 108), (126, 96)]
CLEARINGS = [((128, 150), 11), ((157, 162), 8), ((180, 138), 6), ((151, 128), 6), ((112, 180), 10), ((188, 180), 7),
             ((118, 128), 10), ((172, 48), 8), ((153, 48), 16), ((150, 78), 5), ((122, 38), 5), ((181, 31), 5)]
SIGHT = ((175, 120), (153, 48), 5.0)   # keep the first view of the Foldscar open for its first 30 m


def seg_dist(p, a, b):
    p, a, b = np.array(p, float), np.array(a, float), np.array(b, float)
    ab = b - a
    t = np.clip(np.dot(p - a, ab) / max(np.dot(ab, ab), 1e-9), 0, 1)
    return float(np.linalg.norm(a + t * ab - p))


def line_dist(p, line):
    return min(seg_dist(p, a, b) for a, b in zip(line, line[1:]))


def existing():
    out = []
    for m in re.finditer(r"id: (tree_\d+), circle_m: \[([\d.]+), ([\d.]+), [\d.]+\]", open(LAYOUT, encoding="utf-8").read()):
        out.append((m.group(1), float(m.group(2)), float(m.group(3))))
    return out


def blocked_by_layout():
    """Every non-tree blocker (circles and boxes) with a 3 m margin."""
    text = open(LAYOUT, encoding="utf-8").read()
    shapes = []
    for m in re.finditer(r"id: (\w+), circle_m: \[([\d.]+), ([\d.]+), ([\d.]+)\]", text):
        if not m.group(1).startswith("tree_"):
            shapes.append(("c", float(m.group(2)), float(m.group(3)), float(m.group(4))))
    for m in re.finditer(r"id: (\w+), box_m: \[([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\]", text):
        shapes.append(("b", float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5))))
    return shapes


def ok(p, placed, shapes, zone):
    x, z = p
    if not (2 <= x <= 198 and 2 <= z <= 198):
        return False
    if any(line_dist(p, r) < 4.5 for r in ROUTES):
        return False
    if line_dist(p, STREAM) < 3.0:
        return False
    if any(math.dist(p, c) < r for c, r in CLEARINGS):
        return False
    a, b, w = SIGHT
    if seg_dist(p, a, (a[0] + (b[0] - a[0]) * 0.4, a[1] + (b[1] - a[1]) * 0.4)) < w:
        return False
    for s in shapes:
        if s[0] == "c" and math.dist(p, (s[1], s[2])) < s[3] + 3:
            return False
        if s[0] == "b" and s[1] - 3 <= x <= s[3] + 3 and s[2] - 3 <= z <= s[4] + 3:
            return False
    return all(math.dist(p, q) >= zone for q in placed)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--preview")
    a = ap.parse_args()
    rnd = random.Random(20260926)
    old = existing()
    placed = [(x, z) for _, x, z in old]
    shapes = blocked_by_layout()
    # Cluster centres: woods are groups with clearings between (an open woodland, bible section 6).
    clusters = [(115, 160), (140, 172), (163, 185), (185, 160), (192, 128), (168, 132), (140, 125), (120, 116), (158, 110),
                (145, 190), (106, 190), (196, 100)]
    fringe = [(8, 190), (20, 196), (6, 170), (90, 196), (72, 194), (196, 20), (190, 88), (104, 60), (106, 30), (196, 60)]
    new = []
    for centres, radius, per, spacing in ((clusters, 11, 7, 4.8), (fringe, 7, 3, 5.0)):
        for c in centres:
            n = 0
            for _ in range(400):
                if n >= per:
                    break
                ang, r = rnd.uniform(0, 2 * math.pi), radius * math.sqrt(rnd.random())
                p = (round(c[0] + r * math.cos(ang), 1), round(c[1] + r * math.sin(ang), 1))
                if ok(p, placed, shapes, spacing):
                    placed.append(p)
                    new.append(p)
                    n += 1
    start = max(int(t[0].split("_")[1]) for t in old) + 1
    lines = [f"  - {{ id: tree_{start + i:02d}, circle_m: [{x}, {z}, 0.4], height_m: 7.0 }}" for i, (x, z) in enumerate(new)]
    print(f"{len(old)} existing trees, {len(new)} new (tree_{start}..tree_{start + len(new) - 1})")
    if a.preview:
        from PIL import Image, ImageDraw
        img = Image.new("RGB", (800, 800), (40, 60, 40))
        d = ImageDraw.Draw(img)
        S = 4
        f = lambda x, z: (x * S, (200 - z) * S)  # noqa: E731
        for r in ROUTES:
            d.line([f(*p) for p in r], fill=(230, 200, 120), width=int(9 * S))
        d.line([f(*p) for p in STREAM], fill=(80, 120, 200), width=int(6 * S))
        for c, r in CLEARINGS:
            d.ellipse([f(c[0] - r, c[1] + r), f(c[0] + r, c[1] - r)], outline=(255, 120, 120), width=2)
        for s in shapes:
            if s[0] == "c":
                d.ellipse([f(s[1] - s[3], s[2] + s[3]), f(s[1] + s[3], s[2] - s[3])], fill=(150, 150, 150))
            else:
                d.rectangle([f(s[1], s[4]), f(s[3], s[2])], fill=(170, 150, 120))
        for _, x, z in old:
            d.ellipse([f(x - 1.5, z + 1.5), f(x + 1.5, z - 1.5)], fill=(20, 110, 20))
        for x, z in new:
            d.ellipse([f(x - 1.5, z + 1.5), f(x + 1.5, z - 1.5)], fill=(120, 230, 90))
        img.save(a.preview)
    if a.write:
        text = open(LAYOUT, encoding="utf-8").read()
        anchor = re.findall(r"  - \{ id: tree_\d+, circle_m: \[[^\n]*\n", text)[-1]
        block = anchor + "  # Phase B demo: the woods filled out (tools/asset_pipeline/place_trees.py) - clusters with clearings, clear of every route.\n" + "\n".join(lines) + "\n"
        text = text.replace(anchor, block, 1)
        open(LAYOUT, "w", encoding="utf-8", newline="").write(text)
        print("written")


if __name__ == "__main__":
    main()
