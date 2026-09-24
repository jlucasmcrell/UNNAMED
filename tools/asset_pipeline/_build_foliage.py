"""Apply the foliage collision policy across the flora set and record it in each asset's metadata.

Runs the collision rebuild per asset, replaces the old proxies, and writes `collision_policy` into
the meta so the decision is inspectable and so `_verify_pack.py` can honour "none" instead of
demanding files that are deliberately absent.

Staging first, promoting only on success: the collision files are what the player physically feels,
so a half-applied change is worse than none.

Usage:
    python _build_foliage.py --audit
    python _build_foliage.py --apply
    python _build_foliage.py --apply --asset flora_oak_tree
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
STAGING = os.path.join(ASSETS, "review", "_foliage_staging")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                    "_blender_foliage_collision.py")

# mode, height fraction. Trees collide at the trunk; bushes at their dense lower mass; ground
# cover not at all. Fractions are of total height.
#
# Tree fractions are small on purpose. A 14 m oak carries low branches, so 22% of its height
# already spans 7 x 8 m and is no more use as a collider than the full canopy was. At 6% the
# proxy is the trunk plus root flare, which is what the player actually walks into.
POLICY = {
    "flora_oak_tree": ("trunk", 0.06),
    "flora_pine_tree": ("trunk", 0.08),
    "flora_birch_tree": ("trunk", 0.06),
    "flora_dead_tree": ("trunk", 0.14),
    "flora_slowtree": ("trunk", 0.06),
    "flora_willow_tree": ("trunk", 0.06),
    "flora_bramble_bush": ("lower", 0.55),
    "flora_boneleaf_bush": ("lower", 0.55),
    "flora_cattail_clump": ("lower", 0.45),
    "flora_fern_clump": ("none", 0.0),
    "flora_heather_patch": ("none", 0.0),
    "flora_mirrorfern": ("none", 0.0),
    "flora_bracket_fungus": ("none", 0.0),
    "flora_glowcap_cluster": ("none", 0.0),
}


def run_blender(asset_id, mode, fraction):
    base = os.path.join(READY, asset_id, f"{asset_id}.glb")
    staging = os.path.join(STAGING, asset_id)
    shutil.rmtree(staging, ignore_errors=True)
    os.makedirs(staging, exist_ok=True)
    result = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", TOOL, "--",
         "--input", base, "--outdir", staging, "--asset-id", asset_id,
         "--mode", mode, "--height-fraction", str(fraction)],
        capture_output=True, text=True, timeout=900)
    for line in (result.stdout or "").splitlines():
        if line.startswith("FOLIAGE_COLLISION "):
            return json.loads(line[len("FOLIAGE_COLLISION "):])
    return None


def apply_policy(asset_id, mode, fraction, apply_changes):
    directory = os.path.join(READY, asset_id)
    if not os.path.isdir(directory):
        return False, "not built"
    report = run_blender(asset_id, mode, fraction)
    if report is None:
        return False, "blender produced no report"

    if not apply_changes:
        if mode == "none":
            return True, "would remove collision entirely"
        return True, (f"would rebuild {mode} collision from {report['collision_vertices']} of "
                      f"{report['source_vertices']} verts, box "
                      f"{[round(v, 2) for v in report['box_dimensions_m']]} m")

    hull = os.path.join(directory, f"{asset_id}_collision_hull.glb")
    box = os.path.join(directory, f"{asset_id}_collision_box.glb")
    if mode == "none":
        for path in (hull, box):
            if os.path.exists(path):
                os.remove(path)
    else:
        staging = os.path.join(STAGING, asset_id)
        for name in (f"{asset_id}_collision_hull.glb", f"{asset_id}_collision_box.glb"):
            staged = os.path.join(staging, name)
            if not os.path.exists(staged):
                return False, f"{name} was not produced"
            shutil.copy2(staged, os.path.join(directory, name))

    meta_path = os.path.join(directory, f"{asset_id}_meta.json")
    if os.path.exists(meta_path):
        meta = json.load(io.open(meta_path, encoding="utf-8"))
        meta["collision_policy"] = mode
        if mode == "none":
            meta.pop("collision", None)
            meta["collision_note"] = ("ground cover is walked through; no collision is emitted by "
                                      "policy")
        else:
            meta["collision"] = {
                "mode": mode,
                "height_fraction": fraction,
                "convex_hull_faces": report["hull_faces"],
                "box_dimensions": report["box_dimensions_m"],
                "note": ("built from the lower part of the plant; the canopy is not solid, so the "
                         "player is not blocked metres from the trunk"),
            }
        with io.open(meta_path, "w", encoding="utf-8") as handle:
            json.dump(meta, handle, indent=2)

    if mode == "none":
        return True, "collision removed (ground cover)"
    return True, (f"{mode} collision from {report['collision_vertices']} verts, "
                  f"box {[round(v, 2) for v in report['box_dimensions_m']]} m")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--asset", nargs="*", default=None)
    args = parser.parse_args()

    names = sorted(POLICY)
    if args.asset:
        names = [n for n in names if n in args.asset]

    print(f"  {'asset':<28} {'mode':<7} {'height':>7}  result")
    print("  " + "-" * 92)
    ok = failed = 0
    for asset_id in names:
        mode, fraction = POLICY[asset_id]
        built, detail = apply_policy(asset_id, mode, fraction, args.apply)
        print(f"  {'OK  ' if built else 'FAIL'} {asset_id:<28} {mode:<7} {fraction:>7.2f}  {detail}")
        if built:
            ok += 1
        else:
            failed += 1

    print(f"\n  {ok} foliage assets, {failed} failed")
    if not args.apply:
        print("  (audit only; pass --apply to rebuild collision)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
