"""Write each turned rock's drawn footprint into the region as hull_m (owner finding 50).

The game writes the hulls from the placed models (OTHERREACH_ROCK_HULLS_OUT on the real-time route); this replaces those structures'
box_m / circle_m with hull_m, leaving every other key and every other structure byte-for-byte as it was.

usage: python apply_rock_hulls.py <rock_hulls.json> <region.yaml>
"""
import json
import re
import sys

TOLERANCE_M = 0.03   # a corner this close to the line of its neighbours adds nothing a body can feel


def simplify(hull):
    """Drop corners within TOLERANCE_M of the chord between their neighbours, until none is (the outline stays within 3 cm)."""
    pts = list(hull)
    changed = True
    while changed and len(pts) > 3:
        changed = False
        for i in range(len(pts)):
            (ax, az), (bx, bz), (cx, cz) = pts[i - 1], pts[i], pts[(i + 1) % len(pts)]
            chord = ((cx - ax) ** 2 + (cz - az) ** 2) ** 0.5
            if chord and abs((cx - ax) * (bz - az) - (cz - az) * (bx - ax)) / chord < TOLERANCE_M:
                del pts[i]
                changed = True
                break
    return pts


ROCKS = ("cw_boulder_", "cw_crag_")   # rock only: turned props (woodpiles) keep their authored footprint and the anchors fitted to it

hulls = {k: simplify(v) for k, v in json.load(open(sys.argv[1], encoding="utf-8")).items() if k.startswith(ROCKS)}
path = sys.argv[2]
text = open(path, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in text else "\n"
lines = text.split(nl)
out, i, done = [], 0, 0
while i < len(lines):
    line = lines[i]
    m = re.match(r"^- id: (\S+)\s*$", line)
    out.append(line)
    i += 1
    if not m or m.group(1) not in hulls:
        continue
    # The structure's own keys run until the next "- " entry or a top-level key.
    while i < len(lines) and lines[i].startswith("  "):
        key = lines[i].strip()
        if key in ("box_m:", "circle_m:"):
            i += 1
            while i < len(lines) and lines[i].startswith("  - "):
                i += 1
            out.append("  hull_m:")
            out.extend(f"  - [{x:.3f}, {z:.3f}]" for x, z in hulls[m.group(1)])
            done += 1
            continue
        out.append(lines[i])
        i += 1
open(path, "w", encoding="utf-8", newline="").write(nl.join(out))
print(f"{done} of {len(hulls)} structures now carry their hull")
