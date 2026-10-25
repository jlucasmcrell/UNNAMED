"""Generate the hollow under the wolf den - Halda's ledge and the cave behind it - as region content (spec 28).

Geometry is DERIVED from the plan below, not hand-authored: the cave's walls are well over a hundred boxes round an
irregular floor, and the ledge's floor has to match the gorge the exterior draws (Greybox/DenGorge.cs) to the
centimetre. The plan is the source of truth: a ledge under the lip, then chambers and passages as ellipses and
capsules, each with its floor height. Every grid point outside the plan within two metres of it is a wall box;
the presentation draws the rock from the same layout (Greybox/DenGorgeCave.cs).

The interior stands in its own space at the world's own metres, so the ledge is where the exterior shows it: under
the lip behind the den, x 528-561, nine metres down.

    python tools/gen_den_gorge_cave.py      (writes content/regions/den_gorge_cave.yaml)
"""
import math
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[1]
X0, Z0, COLUMNS, ROWS = 496, 742, 72, 70           # the grid: x 496-567, z 742-811, at 1 m
EDGE = 800.0                                       # the frontier's north edge: the lip
SHELF_DROP = 9.2                                   # DenGorge.ShelfDrop
ROCK_M = 2                                         # how far out from the floor the wall boxes reach
WALL_HEIGHT_M = 8.0


def smoothstep(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def ledge_weight(x):
    return smoothstep(522, 532, x) * (1 - smoothstep(556, 566, x))


def shelf_end(x):
    return 3.6 + 5 * ledge_weight(x)                # DenGorge.ShelfEnd


frontier = yaml.safe_load((ROOT / 'content/regions/starting_frontier.yaml').read_text(encoding='utf-8'))
grid = frontier['terrain']
lip_row = grid['heights_m'][int(EDGE - grid['origin_m'][1])]


def lip(x):
    i = x - grid['origin_m'][0]
    a = lip_row[int(math.floor(i))]
    b = lip_row[min(len(lip_row) - 1, int(math.floor(i)) + 1)]
    return a + (b - a) * (i - math.floor(i))


def shelf(x, z):
    """The ledge's floor, as the exterior draws it: the shelf under the lip, falling 8 cm a metre outward."""
    d = z - EDGE
    return lip(x) - SHELF_DROP - max(0.0, d - 2.6) * 0.08


# The cave, from the mouth inward. Ellipse: centre, radii, floor. Capsule: from, to, radius, floor at each end.
ELLIPSES = [
    ("antechamber", (540.0, 786.0), (7.0, 5.5), -3.2),
    ("great_chamber", (518.0, 765.0), (14.5, 10.5), -6.2),
    ("alcove", (540.5, 755.5), (3.4, 3.0), -5.6),
]
CAPSULES = [
    ("mouth", (539.0, 802.0), (539.5, 790.5), 2.4, None, -3.2),
    ("squeeze", (537.0, 781.5), (528.0, 772.5), 1.35, -3.2, -5.3),
    ("to_alcove", (531.0, 760.0), (537.5, 756.5), 1.4, -6.0, -5.6),
    ("way_on", (507.0, 758.0), (499.5, 749.0), 1.5, -6.4, -9.0),
]


def on_ledge(x, z):
    return 528 <= x <= 561 and EDGE + 1.5 <= z <= EDGE + shelf_end(x) - 0.6


def floor_at(x, z):
    """The floor at a grid point, or None for rock."""
    best = None
    if on_ledge(x, z):
        best = shelf(x, z)
    for _, (cx, cz), (rx, rz), f in ELLIPSES:
        e = ((x - cx) / rx) ** 2 + ((z - cz) / rz) ** 2
        if e <= 1:
            h = f + 0.7 * e                         # a shallow bowl, rising to the walls
            best = h if best is None else min(best, h)
    for name, (ax, az), (bx, bz), r, fa, fb in CAPSULES:
        vx, vz = bx - ax, bz - az
        t = max(0.0, min(1.0, ((x - ax) * vx + (z - az) * vz) / (vx * vx + vz * vz)))
        px, pz = ax + vx * t, az + vz * t
        if (x - px) ** 2 + (z - pz) ** 2 <= r * r:
            start = shelf(ax, max(az, EDGE + 2.6)) if fa is None else fa
            h = start + (fb - start) * smoothstep(0, 1, t)
            best = h if best is None or name == "mouth" and z > EDGE else min(best, h)
    return best


floors = [[floor_at(X0 + i, Z0 + j) for i in range(COLUMNS)] for j in range(ROWS)]

# Rock under the floor's nearest edge, so a height read beside a wall is the floor's own.
heights = []
for j in range(ROWS):
    row = []
    for i in range(COLUMNS):
        if floors[j][i] is not None:
            row.append(floors[j][i])
            continue
        near = [floors[b][a] for b in range(max(0, j - 3), min(ROWS, j + 4)) for a in range(max(0, i - 3), min(COLUMNS, i + 4))
                if floors[b][a] is not None]
        row.append(min(near) if near else -6.0)
    heights.append(row)


def walled(i, j):
    if floors[j][i] is not None:
        return False
    return any(floors[b][a] is not None
               for b in range(max(0, j - ROCK_M), min(ROWS, j + ROCK_M + 1))
               for a in range(max(0, i - ROCK_M), min(COLUMNS, i + ROCK_M + 1)))


# Wall boxes: each grid point's metre square, merged along rows, then rows of the same span merged down.
runs = {}
for j in range(ROWS):
    i = 0
    while i < COLUMNS:
        if not walled(i, j):
            i += 1
            continue
        start = i
        while i < COLUMNS and walled(i, j):
            i += 1
        runs.setdefault((start, i), []).append(j)
boxes = []
for (a, b), rows in sorted(runs.items()):
    rows.sort()
    top = rows[0]
    for k in range(1, len(rows) + 1):
        if k == len(rows) or rows[k] != rows[k - 1] + 1:
            boxes.append((X0 + a - 0.5, Z0 + top - 0.5, X0 + b - 0.5, Z0 + rows[k - 1] + 0.5))
            if k < len(rows):
                top = rows[k]
boxes.sort(key=lambda b: (b[1], b[0]))

out = [
    "# The hollow under the wolf den: Halda's ledge under the gorge lip, and the cave behind it (spec 28).",
    "#",
    "# GENERATED by tools/gen_den_gorge_cave.py from its plan. Edit the generator, not this file.",
    "",
    "id: region.den_gorge_cave",
    "kind: region",
    "schema: 1",
    "display_key: region.den_gorge_cave.name",
    "tags: [region, interior]",
    "notes: >-",
    "  The ledge nine metres under the lip behind the wolf den and the cave behind it: a mouth, an antechamber, a squeeze,",
    "  the great chamber under the den's notch, Halda's store in an alcove off it, and a way on that goes down. Its own space",
    "  at the world's metres, so the ledge stands where the exterior draws it. Reached by Halda's rope, and after the rope",
    "  has gone, by the cleft.",
    "",
    "space: space.den_gorge_cave",
    "region_key: r_0_0",
    "cells: [space.den_gorge_cave/r_0_0:c_04_07, space.den_gorge_cave/r_0_0:c_04_08, space.den_gorge_cave/r_0_0:c_05_07, space.den_gorge_cave/r_0_0:c_05_08]",
    f"bounds_m: {{ min: [{X0}, {Z0}], max: [{X0 + COLUMNS - 1}, {Z0 + ROWS - 1}] }}",
    "",
    "# Where a body arrives when the region is opened directly: on the ledge under the rope, facing the cave.",
    "spawn: { position_m: [538.5, 805.0], facing_deg: 180 }",
    "generation: { terrain: { base_height_mm: 0, amplitude_mm: 0, samples_per_axis: 2 } }",
    "terrain:",
    f"  origin_m: [{X0}, {Z0}]",
    "  spacing_m: 1",
    f"  columns: {COLUMNS}",
    f"  rows: {ROWS}",
    "  heights_m:",
]
for row in heights:
    out.append("    - [" + ", ".join(f"{h:.2f}" for h in row) + "]")
out += ["", "# The rock round the floor and the ledge's rim over the drop: walls the body and the camera stop at.", "structures:"]
for n, (a, b, c, d) in enumerate(boxes):
    out.append(f"  - {{ id: gorge_wall_{n:03d}, box_m: [{a:.1f}, {b:.1f}, {c:.1f}, {d:.1f}], height_m: {WALL_HEIGHT_M} }}")
# Silk across the way on's throat: it stops a body where the dark goes down (the cave draws it; DenGorgeCave).
out.append("  - { id: gorge_silk, box_m: [500.4, 750.6, 503.6, 753.6], height_m: 3.0 }")
out += ["", "# Halda's dry store at the back of the alcove (PROPOSED, owner question DG-1).", "containers:",
        "  - { key: container.den_gorge.haldas_store, loot_ref: loot.haldas_store, position_m: [541.5, 754.0], stack_slots: 6 }",
        "doors: []", "npcs: []", "nodes: []", "stations: []", "switches: []", "barriers: []"]
(ROOT / 'content/regions/den_gorge_cave.yaml').write_text("\n".join(out) + "\n", encoding='utf-8')
print(f"den_gorge_cave: {sum(f is not None for r in floors for f in r)} floor points, {len(boxes)} wall boxes")
