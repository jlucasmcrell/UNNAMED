"""Build the whole modular building kit into ready/, verifying each piece's dimensions.

The dimensions are the interface. A 3.0 m wall must be 3.0 m or three of them do not span 9.0 m,
and a door frame's opening must clear the 2.05 m door leaf and a 1.80 m Veth. So every piece is
checked against its declared size before it is promoted, and a piece that comes out wrong is not
copied over the live asset.

Usage:
    python _build_kit.py --audit
    python _build_kit.py --apply
    python _build_kit.py --apply --asset building_well
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
STAGING = os.path.join(ASSETS, "review", "_kit_staging")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_blender_build_kit.py")

# The piece list lives here rather than being imported: `_blender_build_kit` imports bmesh, which
# only exists inside Blender, so importing it from a plain Python process fails. EXPECTED_M below
# already enumerates every piece this runner drives.
#
# Declared interface dimensions per piece: the size other pieces are built against. A tolerance of
# 2 cm is the point - these are supposed to be exact to the centimetre.
EXPECTED_M = {
    "building_wall_timber": (3.00, 0.18, 2.60),
    "building_wall_stone": (3.00, 0.35, 2.60),
    "building_roof_panel": (3.00, 2.00, 0.30),
    "building_door_frame": (1.34, 0.20, 2.44),
    "building_window_frame": (1.10, 0.16, 1.10),
    "building_floor_planks": (3.00, 3.00, 0.05),
    "building_step": (1.20, 0.34, 0.21),
    "building_post": (0.15, 0.15, 2.60),
    "building_beam": (3.00, 0.15, 0.15),
    "building_fence_panel": (2.40, 0.15, 1.10),
    "building_ruin_wall": (3.00, 0.35, 1.60),
    "building_well": (1.44, 1.80, 2.82),
    "building_road_segment": (4.00, 4.00, 0.07),
}
TOLERANCE_M = 0.02   # the kit is dimensioned in centimetres; 2 cm is a real error at this scale


def build_one(asset_id, promote):
    staging = os.path.join(STAGING, asset_id)
    shutil.rmtree(staging, ignore_errors=True)
    os.makedirs(staging, exist_ok=True)

    result = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", TOOL, "--",
         "--asset-id", asset_id, "--outdir", staging],
        capture_output=True, text=True, timeout=600)

    report = None
    for line in (result.stdout or "").splitlines():
        if line.startswith("KIT_RESULT "):
            report = json.loads(line[len("KIT_RESULT "):])
            break
    if report is None:
        return None, f"blender produced no report: {(result.stdout or '')[-160:]}"

    problems = []
    base = os.path.join(staging, f"{asset_id}.glb")
    if not os.path.exists(base):
        problems.append("base GLB not written")
    if not report.get("has_uvs"):
        problems.append("no UVs, so the PBR material cannot map onto it")

    expected = EXPECTED_M.get(asset_id)
    if expected:
        got = report["dimensions_m"]
        for axis, (g, w) in enumerate(zip(got, expected)):
            if abs(g - w) > TOLERANCE_M:
                problems.append(f"axis {('XYZ')[axis]} is {g:.3f} m, declared {w:.3f} m")

    box = os.path.join(staging, f"{asset_id}_collision_box.glb")
    if not os.path.exists(box):
        problems.append("no box collision proxy")

    if problems:
        return report, "; ".join(problems)

    if not promote:
        return report, "verified (staged)"

    moved = 0
    for name in os.listdir(staging):
        source = os.path.join(staging, name)
        target = os.path.join(READY, asset_id, name)
        shutil.copy2(source, target)
        if os.path.getsize(target) != os.path.getsize(source):
            return report, f"{name} copied short"
        moved += 1
    return report, f"verified, {moved} files promoted"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--asset", nargs="*", default=None)
    args = parser.parse_args()

    names = sorted(EXPECTED_M)
    if args.asset:
        names = [n for n in names if n in args.asset]

    print(f"  {'piece':<30} {'dimensions (W x D x H) m':<30} {'faces':>6} {'lod':>5}  result")
    print("  " + "-" * 100)
    ok = failed = 0
    for asset_id in names:
        os.makedirs(os.path.join(READY, asset_id), exist_ok=True)
        report, detail = build_one(asset_id, args.apply)
        if report is None:
            print(f"  FAIL {asset_id:<25} {'':<30} {'':>6} {'':>5}  {detail}")
            failed += 1
            continue
        dims = " x ".join(f"{v:.2f}" for v in report["dimensions_m"])
        good = "verified" in detail
        print(f"  {'OK  ' if good else 'FAIL'} {asset_id:<25} {dims:<30} "
              f"{report['faces']:>6} {report['lod_policy']:>5}  {detail}")
        if good:
            ok += 1
        else:
            failed += 1

    print(f"\n  {ok} kit pieces, {failed} failed")
    if not args.apply:
        print("  (audit only; pass --apply to promote)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
