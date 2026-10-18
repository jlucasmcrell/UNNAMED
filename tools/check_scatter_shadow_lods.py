"""Check the actual scene inventory written by --frontier-perf, without Godot/GPU.

Usage: python tools/check_scatter_shadow_lods.py <capture>/geometry.json
The old renderer fails: point-light shadows can see every range-hidden near LOD.
"""
import json
import re
import sys
from collections import defaultdict


def check(rows):
    groups = defaultdict(list)
    for row in rows:
        match = re.search(r"/Scatter/(.+)_lod(\d+)$", row["path"])
        if match:
            groups[match[1]].append(row)
    checked = 0
    failures = []
    for name, bands in groups.items():
        if len(bands) < 2 or not any(b["shadows"] != "Off" for b in bands):
            continue
        checked += 1
        for band in bands:
            distance = band["distance_to_camera"]
            # The native end is BitDecrement(next begin); tolerate its one-float gap.
            expected = distance >= band["range_begin"] and (
                not band["range_end"] or distance <= band["range_end"] + 0.0001)
            if band["visible_in_tree"] != expected:
                failures.append(f'{name}: {band["path"]}, d={distance:.3f}, '
                                f'band={band["range_begin"]}..{band["range_end"]}, '
                                f'visible={band["visible_in_tree"]}, expected={expected}')
    if not checked:
        failures.append("No casting scatter LOD groups found: wrong or empty capture")
    return checked, failures


if __name__ == "__main__":
    checked, failures = check(json.load(open(sys.argv[1], encoding="utf-8")))
    print(f"{checked} casting scatter groups; {len(failures)} wrong visibility states")
    print("\n".join(failures[:5]))
    sys.exit(bool(failures))
