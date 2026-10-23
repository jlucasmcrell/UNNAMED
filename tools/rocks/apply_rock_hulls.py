"""Write each turned rock's drawn footprint into the region as hull_m (owner finding 50).

The game writes the hulls from the placed models (OTHERREACH_ROCK_HULLS_OUT on the real-time route); this replaces those structures'
box_m / circle_m with hull_m, leaving every other key and every other structure byte-for-byte as it was.

usage: python apply_rock_hulls.py <rock_hulls.json> <region.yaml>
"""
import json
import re
import sys

hulls = json.load(open(sys.argv[1], encoding="utf-8"))
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
