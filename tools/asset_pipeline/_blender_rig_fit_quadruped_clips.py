"""Rebuild a fitted quadruped's creature clips into staging with the existing clip builder.

`_build_creature_anims.py` is the creature clip builder, but it writes to hard-coded paths under
W:\\UNNAMED\\assets (rigged/, animation/source, animation/clips, animation/ready). This thin wrapper
imports it, points those four paths at assets/_staging/rigs/<id>/ for one creature at a time, and
calls its own `build_one` for every clip the creature has today. The motion generator and the Blender
bake are untouched, so the staged clips are exactly what the builder would make for the fitted rig.

Usage:
  python _blender_rig_fit_quadruped_clips.py --id creature_ash_ember_hound creature_bristleback_boar
"""
import argparse
import glob
import json
import os
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOL_DIR)
import _build_creature_anims as builder  # noqa: E402

ASSETS = os.path.normpath(os.path.join(TOOL_DIR, "..", "..", "assets"))
STAGING = os.path.join(ASSETS, "_staging", "rigs")
ORDER = ("idle", "walk", "run", "attack", "hit", "death")


def current_kinds(short):
    """The clip kinds this creature ships today, read from its registered clip records."""
    found = []
    for path in glob.glob(os.path.join(ASSETS, "animation", "clips", f"anim.creature.{short}.*.json")):
        found.append(os.path.basename(path)[len(f"anim.creature.{short}."):-len(".json")])
    return [k for k in ORDER if k in found] + sorted(k for k in found if k not in ORDER)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--id", nargs="+", required=True)
    args = parser.parse_args()

    failed = 0
    for asset_id in args.id:
        plan, family, short = builder.ENEMIES[asset_id]
        if plan != "quadruped":
            raise SystemExit(f"{asset_id} is a {plan} rig; this wrapper is for the quadruped plan")
        stage = os.path.join(STAGING, asset_id)
        rigged = os.path.join(stage, f"{asset_id}_rigged.glb")
        if not os.path.exists(rigged):
            raise SystemExit(f"no fitted rig at {rigged}; run _blender_rig_fit_quadruped.py first")
        builder.RIGGED = STAGING                     # RIGGED/<id>/<id>_rigged.glb -> the staged rig
        builder.SOURCE = os.path.join(stage, "animation", "source", "creatures")
        builder.CLIPS = os.path.join(stage, "animation", "clips")
        builder.READY = os.path.join(stage, "animation", "ready", "creatures")

        results = {}
        for kind in current_kinds(short):
            report, detail = builder.build_one(asset_id, plan, family, short, kind, True)
            good = report is not None and "bones in motion" not in detail and "against declared" not in detail
            failed += 0 if good else 1
            record = os.path.join(builder.CLIPS, f"anim.creature.{short}.{kind}.json")
            shipped = os.path.join(ASSETS, "animation", "clips", f"anim.creature.{short}.{kind}.json")
            same_record = None
            if os.path.exists(record) and os.path.exists(shipped):
                with open(record, encoding="utf-8") as a, open(shipped, encoding="utf-8") as b:
                    same_record = json.load(a) == json.load(b)
            results[kind] = {"ok": good, "detail": detail, "bones_keyed": (report or {}).get("bones_keyed"),
                             "clip": os.path.join(builder.READY, f"anim.creature.{short}.{kind}.glb"),
                             "record_matches_shipped": same_record}
            print(f"  {'OK  ' if good else 'FAIL'} {asset_id} {kind:<7} {detail}"
                  f"{'' if same_record in (None, True) else '  (record differs from shipped)'}")
        with open(os.path.join(stage, f"{asset_id}_clips.json"), "w", encoding="utf-8") as handle:
            json.dump({"asset": asset_id, "rig": rigged, "builder": "_build_creature_anims.build_one",
                       "clips": results}, handle, indent=2)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
