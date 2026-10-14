"""Load converted ready/<id> folders in headless Godot 4.7.2 the way ArtLibrary does, and report pass/fail per asset.

    python validate_godot.py C:\\Otherreach_Recovery\\asset_work\\test\\rock_cos_bigrock_01 [more folders...]
    python validate_godot.py --all C:\\Otherreach_Recovery\\asset_work\\test

Godot needs a project to run a script in, so a throwaway probe project is kept under --probe (never the repo's own
tools/godot_validate, whose import cache must not fill up with test assets). Exit code 0 only when every asset passes.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GODOT = os.environ.get("UNNAMED_GODOT", r"C:\Otherreach_Worker\tools\godot_4_7_2_net8_0_31\godot\Godot_v4.7.2-stable_mono_win64_console.exe")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folders", nargs="+")
    ap.add_argument("--all", action="store_true", help="each argument is a parent of ready/<id> folders")
    ap.add_argument("--probe", default=r"C:\Otherreach_Recovery\asset_work\_godot_probe")
    ap.add_argument("--json", default="", help="also write the reports here")
    a = ap.parse_args()
    folders = []
    for f in a.folders:
        f = os.path.abspath(f)
        folders += sorted(os.path.join(f, d) for d in os.listdir(f) if os.path.isdir(os.path.join(f, d))) if a.all else [f]
    os.makedirs(a.probe, exist_ok=True)
    with open(os.path.join(a.probe, "project.godot"), "w", encoding="utf-8") as fh:
        fh.write('[application]\nconfig/name="asset_pipeline probe"\nconfig/features=PackedStringArray("4.7")\n')
    shutil.copy2(os.path.join(HERE, "validate_runtime.gd"), os.path.join(a.probe, "validate_runtime.gd"))
    env = dict(os.environ, DOTNET_ROOT=os.environ.get("DOTNET_ROOT", r"C:\Program Files\dotnet"))
    run = subprocess.run([GODOT, "--headless", "--path", a.probe, "--script", "res://validate_runtime.gd", "--",
                          *[f.replace("\\", "/") for f in folders]],
                         capture_output=True, text=True, encoding="utf-8", errors="replace", env=env, timeout=1800)
    reports = [json.loads(line.split(" ", 1)[1]) for line in run.stdout.splitlines() if line.startswith("RUNTIME_RESULT ")]
    if not reports:
        print(run.stdout[-4000:], run.stderr[-4000:])
        return 2
    for r in reports:
        print(f"{'PASS' if r['ok'] else 'FAIL'}  {r['asset_id']}")
        for lv in r["levels"]:
            print(f"    {lv['file']:40s} {lv['triangles']:>8d} tris  aabb {lv.get('aabb_size_m')}  "
                  f"tex {lv.get('textures')}  orm_shared {lv.get('orm_shared')}  load {lv.get('load_ms')} ms")
        for p in r["problems"]:
            print(f"    problem: {p}")
    if a.json:
        with open(a.json, "w", encoding="utf-8") as fh:
            json.dump(reports, fh, indent=1)
    return 0 if all(r["ok"] for r in reports) else 1


if __name__ == "__main__":
    sys.exit(main())
