"""Sink the Stopped Gate's ring court into the Spill's plateau (docs/FIELD_RUINS.md).

Edits terrain.heights_m in content/regions/starting_frontier.yaml in place, value by value, so the file's layout (one height per
line, row r column c at a fixed line) and every other line stay exactly as they were. Run it once from the repository root; it
refuses to run twice, because a second pass would sink the court again.

  python tools/field_ruins/sculpt_spill.py
"""
import math
import re
import sys

REGION = "content/regions/starting_frontier.yaml"
CENTRE = (850.0, 758.0)   # the court's centre, world metres
FLOOR_R = 17.0            # flat floor out to here
RIM_R = 24.0              # the slope meets the untouched plateau here
FLOOR_M = 9.0             # floor height, about 3.5 m under the plateau round it

lines = open(REGION, encoding="utf-8").read().split("\n")
start = lines.index("  heights_m:") + 1
origin = re.findall(r"\d+(?:\.\d+)?", " ".join(lines[start - 6:start - 4]))
assert lines[start - 7].strip() == "origin_m:" and lines[start - 4].strip() == "spacing_m: 1", "terrain header moved"
ox, oz = float(origin[0]), float(origin[1])
columns = int(lines[start - 3].split(":")[1])
assert columns == 701 and lines[start].startswith("  - - ")


def at(row, col):
    return start + row * columns + col


x0, z0 = CENTRE
centre_line = lines[at(int(z0 - oz), int(x0 - ox))]
if float(centre_line.split("- ")[-1]) < FLOOR_M + 0.5:
    sys.exit("the court is already sunk; not sinking it twice")
changed = 0
for row in range(int(z0 - RIM_R - oz), int(z0 + RIM_R - oz) + 2):
    for col in range(int(x0 - RIM_R - ox), int(x0 + RIM_R - ox) + 2):
        r = math.hypot(ox + col - x0, oz + row - z0)
        if r >= RIM_R:
            continue
        i = at(row, col)
        prefix, value = lines[i].rsplit("- ", 1)
        h = float(value)
        t = 0.0 if r <= FLOOR_R else (r - FLOOR_R) / (RIM_R - FLOOR_R)
        t = t * t * (3 - 2 * t)   # smoothstep: the slope eases onto the floor and onto the plateau
        new = min(h, FLOOR_M + (h - FLOOR_M) * t)
        if abs(new - h) > 1e-4:
            lines[i] = f"{prefix}- {round(new, 3):g}"
            changed += 1
open(REGION, "w", encoding="utf-8", newline="\r\n").write("\n".join(lines))   # the region file is CRLF on disk
print(f"sank {changed} height samples")
