"""Stage the arthropod re-rig of a creature: rig, clips, clip records, verification and renders.

Runs, in order, every step of _blender_rig_fit_arthropod.py plus the pipeline's own unchanged
_blender_anim_creature.py (the exporter every creature clip goes through), with every path pointed
at assets/_staging/rigs/<asset>/ instead of the live rigged/ and animation/ folders, which
_build_creature_anims.py hard-codes. Nothing outside the staging folder is written.

Staging layout (mirrors assets/ so promotion is a copy):
  rigged/<asset>_rigged.glb, _rigged_skeleton.json, _rig.json
  animation/source/creatures/<short>_<kind>.json      motion sources
  animation/ready/creatures/anim.creature.<short>.<kind>.glb
  animation/clips/anim.creature.<short>.<kind>.json    clip records (same ids, lengths, loop flags
                                                       and events as the records they replace)
  reports/fit_report.json, verify_report.json, build_report.json
  renders/<bind|weights|clip>/*.png, renders/sheets/*.jpg

Usage:
  python _blender_rig_fit_arthropod_build.py [--asset creature_cave_hunting_spider] [--skip-fit]
      [--weights heat+anatomy|heat] [--outdir <scratch folder, for trials>]
"""
import argparse
import importlib.util
import json
import os
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(os.path.dirname(TOOL_DIR)), "assets")
BLENDER = os.environ.get("UNNAMED_BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
FIT_TOOL = os.path.join(TOOL_DIR, "_blender_rig_fit_arthropod.py")
ANIM_TOOL = os.path.join(TOOL_DIR, "_blender_anim_creature.py")
KINDS = ("idle", "walk", "run", "attack", "hit", "death")
FAMILY = "creature_arthropod"


def blender(script, *args, marker):
    run = subprocess.run([BLENDER, "--background", "--factory-startup", "--python", script, "--", *args],
                         capture_output=True, text=True, timeout=3600)
    for line in (run.stdout or "").splitlines():
        if line.startswith(marker + " "):
            return json.loads(line[len(marker) + 1:])
    tail = "\n".join(((run.stdout or "") + (run.stderr or "")).splitlines()[-40:])
    raise RuntimeError(f"{os.path.basename(script)} {' '.join(args)} failed:\n{tail}")


def load_registry():
    spec = importlib.util.spec_from_file_location("registry", os.path.join(TOOL_DIR, "_animation_registry.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def contact_sheets(renders, sheets):
    from PIL import Image
    os.makedirs(sheets, exist_ok=True)
    made = []
    for group in sorted(os.listdir(renders)):
        folder = os.path.join(renders, group)
        if group == "sheets" or not os.path.isdir(folder):
            continue
        files = sorted(f for f in os.listdir(folder) if f.endswith(".png"))
        views = sorted({f.rsplit("_", 1)[-1] for f in files})
        rows = sorted({f.rsplit("_", 1)[0] for f in files})
        cell = 512
        sheet = Image.new("RGB", (cell * len(views), cell * len(rows)), (255, 255, 255))
        for r, row in enumerate(rows):
            for c, view in enumerate(views):
                path = os.path.join(folder, f"{row}_{view}")
                if os.path.exists(path):
                    sheet.paste(Image.open(path).convert("RGB").resize((cell, cell)), (c * cell, r * cell))
        out = os.path.join(sheets, f"{group}.jpg")
        sheet.save(out, quality=88)
        made.append(out)
    return made


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", default="creature_cave_hunting_spider")
    parser.add_argument("--skip-fit", action="store_true", help="reuse the staged rig")
    parser.add_argument("--outdir", default=None, help="default assets/_staging/rigs/<asset>")
    parser.add_argument("--weights", default="heat+anatomy", choices=["heat", "heat+anatomy"])
    args = parser.parse_args()
    asset, short = args.asset, args.asset.replace("creature_", "", 1)
    outdir = args.outdir or os.path.join(ASSETS, "_staging", "rigs", asset)
    common = ["--asset", asset, "--outdir", outdir]
    report = {"asset": asset, "staging": outdir}

    if not args.skip_fit:
        report["fit"] = blender(FIT_TOOL, "--mode", "fit", *common, "--weights", args.weights,
                                marker="ARTHROPOD_FIT")
        print(f"  fit: {report['fit']['bones']} bones, chains {report['fit']['chains']}")
    report["motion"] = blender(FIT_TOOL, "--mode", "motion", *common, marker="ARTHROPOD_MOTION")

    rigged = os.path.join(outdir, "rigged", f"{asset}_rigged.glb")
    ready = os.path.join(outdir, "animation", "ready", "creatures")
    clips_dir = os.path.join(outdir, "animation", "clips")
    os.makedirs(clips_dir, exist_ok=True)
    registry = load_registry()
    report["clips"] = []
    for kind in KINDS:
        clip_id = f"anim.creature.{short}.{kind}"
        motion = os.path.join(outdir, "animation", "source", "creatures", f"{short}_{kind}.json")
        out = os.path.join(ready, f"{clip_id}.glb")
        result = blender(ANIM_TOOL, "--rigged", rigged, "--motion", motion, "--out", out, "--fps", "30",
                         marker="CREATURE_ANIM_RESULT")
        # The record this clip replaces sets the id, length, loop flag and events; only the
        # skeleton family and the plan tag change, because the bones did.
        with open(os.path.join(ASSETS, "animation", "clips", f"{clip_id}.json"), encoding="utf-8") as h:
            record = json.load(h)
        record["skeleton_family"] = FAMILY
        record["tags"] = ["arthropod" if t == "quadruped" else t for t in record.get("tags", [])]
        path = os.path.join(clips_dir, f"{clip_id}.json")
        with open(path, "w", encoding="utf-8") as h:
            json.dump(record, h, indent=2)
        problems = registry.validate_clip(record, path)
        entry = {"kind": kind, "glb": out, "record": path, "frames": result["frames"],
                 "duration_s": result["duration_s"], "declared_s": record["duration_s"],
                 "loop": record["loop"], "bones_keyed": len(result["bones_keyed"]),
                 "bones_missing": result["bones_missing"], "registry_problems": problems}
        report["clips"].append(entry)
        print(f"  {kind:<7} {result['frames']:>3} frames {result['duration_s']} s "
              f"(declared {record['duration_s']}), {len(result['bones_keyed'])} bones, "
              f"missing {result['bones_missing']}, registry: {problems or 'ok'}")

    report["verify"] = blender(FIT_TOOL, "--mode", "verify", *common, marker="ARTHROPOD_VERIFY")
    print(f"  verify: joints pass {report['verify']['all_joints_pass']}, worst deform joint "
          f"{report['verify']['worst_deform_joint_distance_m']} m (tolerance {report['verify']['tolerance_m']} m)")
    report["render"] = blender(FIT_TOOL, "--mode", "render", *common, marker="ARTHROPOD_RENDER")
    report["sheets"] = contact_sheets(os.path.join(outdir, "renders"), os.path.join(outdir, "renders", "sheets"))
    with open(os.path.join(outdir, "reports", "build_report.json"), "w", encoding="utf-8") as h:
        json.dump(report, h, indent=2)
    print(f"  {report['render']['count']} renders, sheets: {len(report['sheets'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
