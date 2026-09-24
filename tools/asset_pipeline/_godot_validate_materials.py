"""Stage the generated PBR materials into the Godot validation project and validate them.

Materials only exist to be used, and a material that has never been through the engine is not
validated. This copies each material's maps and its StandardMaterial3D resource into the project,
refreshes the import cache, and runs the headless material validator against every one.

The import refresh is not optional: Godot resolves res:// paths against its import cache, and
validating before the textures have been imported reports every map as unresolvable.

Usage:
    python _godot_validate_materials.py
"""
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
MATERIALS = os.path.join(ASSETS, "materials")
GODOT = os.environ.get("UNNAMED_GODOT",
                       r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64_console.exe")
GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT", r"W:\UNNAMED\tools\godot_validate")
STAGING = os.path.join(GODOT_PROJECT, "assets", "materials")


def main():
    if not os.path.isdir(MATERIALS):
        print("  no materials built")
        return 1
    if not os.path.exists(GODOT):
        print(f"  godot not found at {GODOT}")
        return 1

    names = sorted(d for d in os.listdir(MATERIALS) if os.path.isdir(os.path.join(MATERIALS, d)))
    staged = []
    for asset_id in names:
        source = os.path.join(MATERIALS, asset_id)
        target = os.path.join(STAGING, asset_id)
        os.makedirs(target, exist_ok=True)
        for entry in os.listdir(source):
            if entry.endswith((".png", ".tres")):
                shutil.copy2(os.path.join(source, entry), os.path.join(target, entry))
        meta_path = os.path.join(source, f"{asset_id}_material.json")
        meta = json.load(open(meta_path, encoding="utf-8")) if os.path.exists(meta_path) else {}
        expected = os.path.join(target, "expected.json")
        with open(expected, "w", encoding="utf-8") as handle:
            json.dump({"tile_size_m": meta.get("tile_size_m"),
                       "material_class": meta.get("material_class")}, handle, indent=2)
        staged.append((asset_id, os.path.join(target, f"MAT_{asset_id}.tres"), expected))

    subprocess.run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"],
                   capture_output=True, text=True, timeout=600)

    passed = failed = 0
    for asset_id, tres, expected in staged:
        result = subprocess.run(
            [GODOT, "--headless", "--path", GODOT_PROJECT, "--script", "validate_material.gd",
             "--", tres.replace("\\", "/"), expected.replace("\\", "/")],
            capture_output=True, text=True, timeout=300)

        report = None
        for line in result.stdout.splitlines():
            if line.startswith("MATERIAL_RESULT "):
                report = json.loads(line[len("MATERIAL_RESULT "):])
                break

        if report is None:
            print(f"  FAIL {asset_id:<36} no result")
            print(f"       {(result.stdout or '')[-200:]!r}")
            failed += 1
            continue

        checks = report.get("checks", {})
        if report.get("ok"):
            sizes = checks.get("map_sizes", {})
            print(f"  OK   {asset_id:<36} maps {sizes.get('albedo')} tile "
                  f"{checks.get('tile_size_m')} m normal_scale {checks.get('normal_scale')}")
            passed += 1
        else:
            print(f"  FAIL {asset_id:<36}")
            for problem in report.get("problems", []):
                print(f"       {problem}")
            failed += 1

    print(f"\n  {passed} passed, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
