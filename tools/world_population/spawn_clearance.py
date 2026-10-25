"""Every spawn point and route point of a region, checked against the region's structures (circles, boxes and hulls, padded).

A creature placed inside a blocker stands in a wall or never reaches its beat; the content lint does not look for it.

  python tools/world_population/spawn_clearance.py [--region region.starting_frontier] [--pad 1.5]
"""
import argparse
import glob
import math

import yaml


def structures(region):
    lines = open(f"content/regions/{region.split('.', 1)[1]}.yaml", encoding="utf-8").read().splitlines()
    return yaml.safe_load("\n".join(lines[lines.index("structures:"):lines.index("doors:")]))["structures"]


def inside(x, z, s, pad):
    if "circle_m" in s:
        cx, cz, r = s["circle_m"]
        return math.hypot(x - cx, z - cz) < r + pad
    if "box_m" in s:
        v = s["box_m"]
        (x0, z0), (x1, z1) = (v[0], v[1]) if isinstance(v[0], list) else ((v[0], v[1]), (v[2], v[3]))
        return min(x0, x1) - pad < x < max(x0, x1) + pad and min(z0, z1) - pad < z < max(z0, z1) + pad
    if "hull_m" in s:
        pts = s["hull_m"]
        turn = None
        for (ax, az), (bx, bz) in zip(pts, pts[1:] + pts[:1]):
            cross = (bx - ax) * (z - az) - (bz - az) * (x - ax)
            if turn is None and abs(cross) > 1e-9:
                turn = 1 if cross > 0 else -1
            if turn and cross * turn / (math.hypot(bx - ax, bz - az) or 1) < -pad:
                return False
        return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--region", default="region.starting_frontier")
    ap.add_argument("--pad", type=float, default=1.5)
    args = ap.parse_args()
    blockers = structures(args.region)
    bad = 0
    for path in sorted(glob.glob("content/spawns/**/*.yaml", recursive=True)):
        spawn = yaml.safe_load(open(path, encoding="utf-8"))
        if spawn.get("region_ref") != args.region:
            continue
        for x, z in [tuple(spawn["at"]["position_m"])] + [tuple(p) for p in spawn.get("route_m", [])]:
            for s in blockers:
                if inside(x, z, s, args.pad):
                    bad += 1
                    print(f"{spawn['id']}: ({x}, {z}) is inside {s['id']}")
    print(f"{bad} spawn points inside structures")


main()
