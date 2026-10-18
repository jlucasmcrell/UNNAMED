"""Check the actual scene inventory written by --frontier-perf, without Godot/GPU.

Usage: python tools/check_scatter_shadow_lods.py <capture>/geometry.json
The old renderer fails: point-light shadows can see every range-hidden near LOD.
Checks static models as well as scatter; GI visibility must remain independent.
"""
import json
import sys


def check(rows):
    checked = 0
    failures = []
    for band in rows:
        if band.get("authored_shadows", band["shadows"]) == "Off" or not (
                band["range_begin"] or band["range_end"]):
            continue
        checked += 1
        distance = band["distance_to_camera"]
        expected = distance >= band["range_begin"] and (
            not band["range_end"] or distance < band["range_end"])
        actual = band["shadows"] != "Off"
        # Floating-point squaring/transform rounding at the native boundary is insignificant.
        boundary = abs(distance - band["range_begin"]) < 0.0001 or (
            band["range_end"] and abs(distance - band["range_end"]) < 0.0001)
        if actual != expected and not boundary:
            failures.append(f'{band["path"]}, d={distance:.3f}, '
                            f'band={band["range_begin"]}..{band["range_end"]}, '
                            f'casts={actual}, expected={expected}')
    if not checked:
        failures.append("No casting distance bands found: wrong or empty capture")
    return checked, failures


if __name__ == "__main__":
    checked, failures = check(json.load(open(sys.argv[1], encoding="utf-8")))
    print(f"{checked} authored casting bands; {len(failures)} wrong shadow states")
    print("\n".join(failures[:5]))
    sys.exit(bool(failures))
