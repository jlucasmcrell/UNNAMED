"""Plot the ring's walk space, its walls, its doors and the flood fill, so B7 is tuned by seeing it.

The fill in the test is `Kinematics.IsClear` at 1 m steps from the spawn with every door shut. This reproduces that
exactly - point plus body radius inside the bounds, and no blocker whose box is nearer than the radius - and draws it,
with the room rectangles, the blockout's walls, the rock mass, the gate doors and every room centre labelled.
"""
import io
import math
import re

from PIL import Image, ImageDraw

ROOT = r"C:\Otherreach_Build\barrowworks_wt"
YAML = ROOT + r"\content\regions\barrowworks.yaml"
GEN = ROOT + r"\tools\gen_barrowworks_blockout.py"
OUT = r"C:\Otherreach_Build\b7_fill.png"

RADIUS_M = 0.35      # the default body radius; the test reads the real one, this is for the picture
STEP_M = 1.0

# ── the region as emitted ────────────────────────────────────────────────────────────────────────────────
text = io.open(YAML, encoding="utf-8").read()

bounds = re.search(r"bounds_m: \{ min: \[([-\d.]+), ([-\d.]+)\], max: \[([-\d.]+), ([-\d.]+)\] \}", text)
min_x, min_z, max_x, max_z = (float(g) for g in bounds.groups())

spawn = re.search(r"spawn: \{ position_m: \[([-\d.]+), ([-\d.]+)\], facing_deg: ([-\d.]+) \}", text)
spawn_x, spawn_z = float(spawn.group(1)), float(spawn.group(2))

structures = []
for m in re.finditer(r"\{ id: ([a-z0-9_]+), box_m: \[([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\], height_m: ([-\d.]+)", text):
    sid, x0, z0, x1, z1, h = m.group(1), *[float(m.group(k)) for k in (2, 3, 4, 5)], float(m.group(6))
    structures.append((sid, x0, z0, x1, z1, h))

doors = []
for m in re.finditer(r"key: (door\.barrowworks\.g\d), flag_ref: [^,]+, box_m: \[([-\d.]+), ([-\d.]+), ([-\d.]+), ([-\d.]+)\]", text):
    doors.append((m.group(1), float(m.group(2)), float(m.group(3)), float(m.group(4)), float(m.group(5))))

# ── the rooms, shifted ──────────────────────────────────────────────────────────────────────────────────
gen = io.open(GEN, encoding="utf-8").read()
OFF_X, OFF_Z = 50.0, 10.0
rooms = []
for m in re.finditer(r'\("(R\d\d)",\s*"[^"]+",\s*\(([-\d.]+),\s*([-\d.]+)\),\s*\(([-\d.]+),\s*([-\d.]+)\)\)', gen):
    rid = m.group(1)
    cx, cz, w, d = (float(m.group(k)) for k in (2, 3, 4, 5))
    x, z = cx + OFF_X, cz + OFF_Z
    rooms.append((rid, x - w / 2, z - d / 2, x + w / 2, z + d / 2))


def blocked(x, z):
    r = RADIUS_M
    if x - r < min_x or x + r > max_x or z - r < min_z or z + r > max_z:
        return True
    for _, x0, z0, x1, z1, h in structures:
        if h <= 0:      # height 0 blocks everything (Kinematics.Blocks)
            pass
        qx, qz = min(max(x, x0), x1), min(max(z, z0), z1)
        if (x - qx) ** 2 + (z - qz) ** 2 < r * r:
            return True
    for _, x0, z0, x1, z1 in doors:      # every door shut
        qx, qz = min(max(x, x0), x1), min(max(z, z0), z1)
        if (x - qx) ** 2 + (z - qz) ** 2 < r * r:
            return True
    return False


# ── the fill, exactly the test's ────────────────────────────────────────────────────────────────────────
seen = {(int(spawn_x), int(spawn_z))}
frontier = [(spawn_x, spawn_z)]
while frontier:
    x, z = frontier.pop()
    for dx, dz in ((STEP_M, 0), (-STEP_M, 0), (0, STEP_M), (0, -STEP_M)):
        nx, nz = x + dx, z + dz
        key = (int(nx), int(nz))
        if key in seen or blocked(nx, nz):
            continue
        seen.add(key)
        frontier.append((nx, nz))

# ── draw ────────────────────────────────────────────────────────────────────────────────────────────────
SCALE = 8
W, H = int((max_x - min_x) * SCALE), int(max_z - min_z) * SCALE
img = Image.new("RGB", (W, H), (14, 14, 16))
draw = ImageDraw.Draw(img)


def px(x, z):
    return ((x - min_x) * SCALE, (z - min_z) * SCALE)


for x, z in seen:
    draw.rectangle([px(x - 0.5, z - 0.5), px(x + 0.5, z + 0.5)], fill=(40, 90, 120))

for sid, x0, z0, x1, z1, h in structures:
    colour = (150, 60, 60) if sid.startswith("rock") else (120, 120, 125)
    draw.rectangle([px(x0, z0), px(x1, z1)], outline=colour)

for _, x0, z0, x1, z1 in doors:
    draw.rectangle([px(x0, z0), px(x1, z1)], outline=(220, 180, 60), width=2)

for rid, x0, z0, x1, z1 in rooms:
    draw.rectangle([px(x0, z0), px(x1, z1)], outline=(90, 200, 120))
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    reached = (int(cx), int(cz)) in seen
    draw.text(px(cx - 2.0, cz), f"{rid}{'' if reached else ' X'}", fill=(255, 255, 255) if reached else (255, 80, 80))

draw.text(px(spawn_x + 0.6, spawn_z), "spawn", fill=(255, 255, 0))
img.save(OUT)

reached_rooms = [rid for rid, x0, z0, x1, z1 in rooms if (int((x0 + x1) / 2), int((z0 + z1) / 2)) in seen]
unreached = [rid for rid, *_ in rooms if rid not in reached_rooms]
print(f"  {len(seen)} tiles reached")
print(f"  reached:   {', '.join(sorted(reached_rooms))}")
print(f"  UNREACHED: {', '.join(sorted(unreached))}")
print(f"  wrote {OUT}")
