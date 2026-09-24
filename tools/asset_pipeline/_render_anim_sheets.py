"""Render a 2x2 preview sheet for every generated motion, so the whole clip set can be judged.

Frames are picked at even intervals through the clip rather than at fixed frames, so a 22-frame
block and a 61-frame death both show start, two middles and end.

Usage:
    python _render_anim_sheets.py                 # every motion in source/manual
    python _render_anim_sheets.py --kind death pickup
"""
import argparse
import glob
import json
import os
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
SOURCE = os.path.join(ASSETS, "animation", "source", "manual")
REVIEW = os.path.join(ASSETS, "review", "anim_preview")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
RENDER_TOOL = os.path.join(TOOL_DIR, "_render_anim_preview.py")
SHEET_TOOL = os.path.join(TOOL_DIR, "_sheet.py")

# The four locomotion probes are generated too, but they were judged when they were built.
SKIP = {"walk", "run", "sprint", "idle", "walk_forward", "polearm_thrust"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--kind", nargs="*", default=None)
    args = parser.parse_args()

    motions = []
    for path in sorted(glob.glob(os.path.join(SOURCE, "*.json"))):
        kind = os.path.splitext(os.path.basename(path))[0]
        if kind.startswith("_") or kind in SKIP:
            continue
        if args.kind and kind not in args.kind:
            continue
        motions.append((kind, path))

    if not motions:
        print("  no motions selected")
        return 1

    ok = failed = 0
    for kind, path in motions:
        with open(path, encoding="utf-8") as handle:
            count = len(json.load(handle)["frames"])
        last = count - 1
        frames = [0, round(last * 0.33), round(last * 0.62), last]
        frames = sorted(set(frames))

        out_dir = os.path.join(REVIEW, kind)
        render = subprocess.run(
            [BLENDER, "--background", "--factory-startup", "--python", RENDER_TOOL, "--",
             "--motion", path, "--out", out_dir,
             "--frames", *[str(f) for f in frames]],
            capture_output=True, text=True, timeout=900)
        if "PREVIEW_RESULT" not in (render.stdout or ""):
            tail = ((render.stdout or "") + (render.stderr or ""))[-300:]
            print(f"  FAIL {kind:<20} render: {tail}")
            failed += 1
            continue

        sheet = os.path.join(REVIEW, f"sheet_{kind}.png")
        compose = subprocess.run([sys.executable, SHEET_TOOL, out_dir, sheet],
                                 capture_output=True, text=True, timeout=120)
        if compose.returncode != 0:
            print(f"  FAIL {kind:<20} sheet: {(compose.stderr or '')[-160:]}")
            failed += 1
            continue
        print(f"  OK   {kind:<20} frames {frames} -> {sheet}")
        ok += 1

    print(f"\n  {ok} sheets, {failed} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
