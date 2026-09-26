"""Phase B demo: every set-dressing spot (src/Presentation/Art/dressing.json) checked against every route the playthrough walks
(src/Presentation/Playthrough.cs), the worn paths (scatter_rules.json), the layout's blockers and the interactables - scenery with no
collider must never stand where someone walks or stands.

    python check_dressing.py [--margin 2.0]
"""
import argparse
import json
import math
import re

REPO = "G:/UNNAMED_PHASEB"


def seg(p, a, b):
    ax, az = a
    bx, bz = b
    dx, dz = bx - ax, bz - az
    t = 0 if dx == dz == 0 else max(0, min(1, ((p[0] - ax) * dx + (p[1] - az) * dz) / (dx * dx + dz * dz)))
    return math.dist(p, (ax + t * dx, az + t * dz))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--margin", type=float, default=2.0)
    a = ap.parse_args()
    src = open(f"{REPO}/src/Presentation/Playthrough.cs", encoding="utf-8").read()
    routes = {}
    for m in re.finditer(r"(\w+) =\s*\{([^;]*?)\};", src):
        pts = [(float(x), float(z)) for x, z in re.findall(r"\(([\d.]+), ([\d.]+)\)", m.group(2))]
        if pts:
            routes[m.group(1)] = pts
    rules = json.load(open(f"{REPO}/src/Presentation/Art/scatter_rules.json", encoding="utf-8"))
    for i, path in enumerate(rules["paths"]["region.ashen_hollow"]):
        routes[f"worn_path_{i}"] = [tuple(p) for p in path["points"]]
    layout = open(f"{REPO}/content/regions/ashen_hollow.yaml", encoding="utf-8").read()
    spots = [(k, float(x), float(z)) for k, x, z in re.findall(r"key: ([\w.]+),[^\n]*position_m: \[([\d.]+), ([\d.]+)\]", layout)]
    places = json.load(open(f"{REPO}/src/Presentation/Art/dressing.json", encoding="utf-8"))["places"]
    bad = 0
    for p in places:
        at = tuple(p["at"])
        if p.get("y", 0) >= 1.8:
            continue   # overhead: nobody walks into it
        for name, pts in routes.items():
            segs = list(zip(pts, pts[1:])) or [(pts[0], pts[0])]
            d = min(seg(at, s, e) for s, e in segs)
            if d < a.margin:
                print(f"TOO NEAR {p['model']} at {at}: {d:.2f} m from route {name}")
                bad += 1
        for key, x, z in spots:
            if math.dist(at, (x, z)) < a.margin:
                print(f"TOO NEAR {p['model']} at {at}: {math.dist(at, (x, z)):.2f} m from {key}")
                bad += 1
    print(f"{len(places)} places, {bad} problems")


if __name__ == "__main__":
    main()
