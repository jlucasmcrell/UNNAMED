"""Run all 15 Wave 0 proof assets through the unified modular pipeline.

    raw GLB -> [stub cut] -> sockets + LODs + collision -> socket verify -> Godot validate

Each component's cut side comes from `assets/cut_sides.json`, which records what a rendered
inspection actually showed rather than what a heuristic guessed. Components whose verdict is
not `correct` are still processed and reported, because a play test needs to know precisely
which of the 15 are trustworthy.

Output goes to `review/wave0_modular/<asset>/` first. Promotion into `ready/` is a separate,
deliberate step so a failure never overwrites a working production copy.

Usage:
    python _build_proofset.py            # all 15
    python _build_proofset.py --only mace
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

TOOL = os.path.dirname(os.path.abspath(__file__))
ASSETS = r"W:\UNNAMED\assets"
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
GODOT = os.environ.get("UNNAMED_GODOT",
                       r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64.exe")
GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT",
                               r"W:\UNNAMED\tools\godot_validate")

CUT_STUB = os.path.join(TOOL, "_blender_cut_stub.py")
SOCKETS = os.path.join(TOOL, "_blender_sockets.py")
VERIFY = os.path.join(TOOL, "_verify_sockets.py")

# Faces and collision per category. Components are small, so a lighter chain than a hero
# character needs; per the standard, tiny components should not carry an expensive LOD chain,
# but a play test wants them consistent, so every component gets the same three levels.
LOD_FACES = "8000,2500,600"
COLLISION_FACES = 400


def run(command, label, quiet=True):
    result = subprocess.run(command, capture_output=True, text=True)
    out = (result.stdout or "") + (result.stderr or "")
    if result.returncode != 0 and not quiet:
        print(f"    [{label}] exit {result.returncode}")
        for line in out.strip().splitlines()[-6:]:
            print(f"        {line}")
    return result.returncode, out


def write_status(results):
    """Persist results after every asset.

    Writing only at the end lost the whole record when the batch crashed partway, and a
    single-asset test run then overwrote the full run's file. Writing per asset means the
    status always reflects the most recent truth.
    """
    with open(os.path.join(ASSETS, "proofset_status.json"), "w", encoding="utf-8") as h:
        json.dump([{"asset_id": a, "state": s, "note": n} for a, s, n in results],
                  h, indent=2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", default=None, help="Substring filter on asset id")
    parser.add_argument("--out", default=os.path.join(ASSETS, "review", "wave0_modular"))
    args = parser.parse_args()

    with open(os.path.join(ASSETS, "requests", "wave0_proofset.json"), encoding="utf-8") as h:
        proofset = [x["id"] for x in json.load(h)]
    with open(os.path.join(ASSETS, "cut_sides.json"), encoding="utf-8") as h:
        sides = json.load(h)["components"]

    staging = os.path.join(GODOT_PROJECT, "assets")
    os.makedirs(staging, exist_ok=True)

    results = []
    for index, asset_id in enumerate(proofset, 1):
        if args.only and args.only not in asset_id:
            continue
        raw = os.path.join(ASSETS, "raw", f"{asset_id}.glb")
        socket_file = os.path.join(ASSETS, "sockets", f"{asset_id}.json")
        if not os.path.exists(raw) or not os.path.exists(socket_file):
            results.append((asset_id, "SKIP", "no raw GLB or no socket definition"))
            print(f"  [{index}/{len(proofset)}] {asset_id}: SKIP")
            continue

        out_dir = os.path.join(args.out, asset_id)
        os.makedirs(out_dir, exist_ok=True)
        glb = os.path.join(out_dir, f"{asset_id}.glb")
        cut_glb = os.path.join(out_dir, f"{asset_id}_stubcut.glb")

        declaration = sides.get(asset_id, {})
        verdict = declaration.get("verdict", "not_cut")
        socket_name = declaration.get("socket")
        side = declaration.get("side")

        source = raw
        stages = {}

        # --- stub cut, where the component was built from a stub concept ---
        if verdict in ("correct", "concept_failure") and socket_name:
            command = [BLENDER, "--background", "--factory-startup", "--python", CUT_STUB,
                       "--", "--input", raw, "--sockets", socket_file,
                       "--socket", socket_name, "--out", cut_glb, "--cap"]
            if side:
                command += ["--component-side", side]
            code, out = run(command, "cut")
            stages["cut"] = code == 0
            if code == 0:
                source = cut_glb
            else:
                results.append((asset_id, "FAIL", "stub cut failed"))
                print(f"  [{index}/{len(proofset)}] {asset_id}: FAIL cut")
                continue
        elif verdict == "not_applicable":
            stages["cut"] = True   # planar interface, nothing to remove
        elif verdict == "degenerate":
            # No boundary exists in the mesh, so the cut cannot run; build anyway and say so.
            stages["cut"] = None

        # --- author sockets, LODs and collision in one Blender session ---
        code, out = run([BLENDER, "--background", "--factory-startup", "--python", SOCKETS,
                         "--", "--input", source, "--sockets", socket_file, "--out", glb,
                         "--blend-source", os.path.join(ASSETS, "blender_src", f"{asset_id}.blend"),
                         "--lod-faces", LOD_FACES,
                         "--collision-faces", str(COLLISION_FACES)], "author")
        stages["author"] = code == 0
        # Blender can exit 0 having written nothing, so check the artefact rather than the
        # exit code. Without this the batch crashed later on a missing file instead of
        # reporting which asset failed and why.
        if code != 0 or not os.path.exists(glb):
            detail = "authoring produced no GLB"
            for line in out.strip().splitlines()[-3:]:
                if "Error" in line or "error" in line:
                    detail = line.strip()[:160]
            print(f"  [{index}/{len(proofset)}] {asset_id}: FAIL author ({detail})")
            results.append((asset_id, "FAIL", f"author: {detail}"))
            write_status(results)
            continue

        # --- verify the socket basis survived export ---
        code, out = run([sys.executable, VERIFY, "--sockets", socket_file, "--glb", glb],
                        "verify")
        stages["verify"] = code == 0

        # --- Godot import validation ---
        report_path = os.path.splitext(glb)[0] + "_sockets.json"
        expected = {"asset_id": asset_id,
                    "sockets": {k: v["position"]
                                for k, v in json.load(open(socket_file, encoding="utf-8")
                                                     )["sockets"].items()}}
        if os.path.exists(report_path):
            with open(report_path, encoding="utf-8") as h:
                rep = json.load(h)
            if rep.get("dimensions_m_export_frame"):
                expected["dimensions_m"] = rep["dimensions_m_export_frame"]
        if not os.path.exists(glb):
            results.append((asset_id, "FAIL", "no GLB before Godot staging"))
            write_status(results)
            continue
        shutil.copy2(glb, os.path.join(staging, f"{asset_id}.glb"))
        expected_path = os.path.join(staging, f"{asset_id}_expected.json")
        with open(expected_path, "w", encoding="utf-8") as h:
            json.dump(expected, h, indent=2)

        # Godot must re-import AFTER the new GLB lands. Importing first leaves a stale
        # `.import` beside a freshly copied source, and the loader then fails with "Godot
        # could not load the GLB as a PackedScene" because it read the file mid-copy.
        # Observed as a 0 KB staged GLB whose .import file was eleven minutes older.
        staged = os.path.join(staging, f"{asset_id}.glb")
        import_marker = staged + ".import"
        for _ in range(4):
            run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"], "godot-import")
            if os.path.exists(import_marker) and \
                    os.path.getmtime(import_marker) >= os.path.getmtime(staged):
                break
            time.sleep(2)

        code, out = run([GODOT, "--headless", "--path", GODOT_PROJECT, "--script",
                         "validate_glb.gd", "--", staged,
                         expected_path], "godot")
        stages["godot"] = '"ok":true' in out.replace(" ", "")

        passed = all(v for v in stages.values() if v is not None)
        state = "PASS" if passed else "PARTIAL"
        note = f"cut={verdict}"
        results.append((asset_id, state, note))
        marks = " ".join(f"{k}={'ok' if v else ('n/a' if v is None else 'BAD')}"
                         for k, v in stages.items())
        print(f"  [{index}/{len(proofset)}] {asset_id}: {state}  {marks}  ({note})")
        write_status(results)

    print()
    ok = sum(1 for r in results if r[1] == "PASS")
    print(f"  PASS {ok}/{len(results)}")
    with open(os.path.join(ASSETS, "proofset_status.json"), "w", encoding="utf-8") as h:
        json.dump([{"asset_id": a, "state": s, "note": n} for a, s, n in results], h, indent=2)
    print(f"  wrote {os.path.join(ASSETS, 'proofset_status.json')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
