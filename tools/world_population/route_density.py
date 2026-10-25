"""How often a body meets a fight along a region's travelled path: the gaps, in seconds at the base run, between hostile spawns.

The path is the region's roads plus any branch given with --branch (x,z;x,z;...). A hostile site is met where the path passes within
--reach metres of its point or any point of its route (a creature's sight is 25 m; most notice from 20-30 m). Ground within --safe
metres of a settlement location is the safe core: no gap is counted there and no site may stand on the road in
it. Peaceful roles (wildlife, skittish, yard_fowl, herd, herd_shy) are not fights.

  python tools/world_population/route_density.py --region region.starting_frontier --branch "840,659;845,700;850,740"
"""
import argparse
import glob
import math

import yaml

PEACEFUL = {"wildlife", "skittish", "yard_fowl", "herd", "herd_shy"}


def load(pattern):
    return [yaml.safe_load(open(p, encoding="utf-8")) for p in sorted(glob.glob(pattern, recursive=True))]


def region_paths(region):
    """The roads only: the region file is too large to parse whole, so its `roads:` block is read on its own."""
    lines = open(f"content/regions/{region.split('.', 1)[1]}.yaml", encoding="utf-8").read().splitlines()
    start = lines.index("roads:")
    end = next(i for i in range(start + 1, len(lines)) if lines[i] and not lines[i].startswith((" ", "-", "#")))
    return [r["points_m"] for r in yaml.safe_load("\n".join(lines[start:end]))["roads"]]


def sample(points, step):
    out = []
    for (x0, z0), (x1, z1) in zip(points, points[1:]):
        n = max(1, int(math.hypot(x1 - x0, z1 - z0) / step))
        out += [(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n) for i in range(n)]
    return out + [tuple(points[-1])]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--region", default="region.starting_frontier")
    ap.add_argument("--branch", action="append", default=[])
    ap.add_argument("--reach", type=float, default=30)
    ap.add_argument("--safe", type=float, default=60)
    ap.add_argument("--speed", type=float, default=3.2)
    args = ap.parse_args()

    sites = []
    for s in load("content/spawns/**/*.yaml"):
        if s.get("region_ref") != args.region or all(c.get("role") in PEACEFUL for c in s["creatures"]):
            continue
        points = [tuple(s["at"]["position_m"])] + [tuple(p) for p in s.get("route_m", [])]
        sites.append((s["id"].split(".", 1)[1], points))
    cores = [tuple(loc["anchor"]["world"][0::2]) for loc in load("content/locations/**/*.yaml")
             if loc.get("region_ref") == args.region and loc.get("kind_of_place") in ("settlement", "waystation")]

    paths = region_paths(args.region) + [[tuple(map(float, p.split(","))) for p in b.split(";")] for b in args.branch]
    for path in paths:
        print(f"path {path[0]} -> {path[-1]}")
        gap, worst, met, last = 0.0, 0.0, [], None
        for x, z in sample(path, 2.0):
            safe = any(math.hypot(x - cx, z - cz) < args.safe for cx, cz in cores)
            near = [sid for sid, pts in sites if min(math.hypot(x - px, z - pz) for px, pz in pts) < args.reach]
            if safe or near:
                if gap:
                    worst = max(worst, gap)
                    print(f"  gap {gap / args.speed:5.0f} s before ({x:.0f},{z:.0f})")
                gap = 0.0
                for sid in near:
                    if sid not in met:
                        met.append(sid)
            elif last:
                gap += math.hypot(x - last[0], z - last[1])
            last = (x, z)
        if gap:
            worst = max(worst, gap)
            print(f"  gap {gap / args.speed:5.0f} s to the end")
        print(f"  met {len(met)}: {', '.join(met)}\n  longest gap {worst / args.speed:.0f} s")


main()
