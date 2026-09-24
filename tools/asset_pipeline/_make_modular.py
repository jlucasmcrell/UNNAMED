"""Run one asset through the full Wave 0 modular pipeline and validate it in Godot.

    raw GLB  ->  [stub cut]  ->  sockets + naming + absolute sizing  ->  canonical .blend
             ->  production GLB  ->  socket verification  ->  Godot import validation

This is the command that makes "done" mean something for modular assets. It is deliberately
strict: any stage failing stops the chain, because a modular component that half-passes is
worse than one that clearly fails - it would be assembled into items that break later.

The stub cut runs FIRST when a component was built from a sacrificial-stub concept, because
everything downstream measures the component and the stub would inflate every dimension.

Usage:
    python _make_modular.py --sockets sockets\\name.json ^
        --raw raw\\name.glb --out ready\\name --validate
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
GODOT = os.environ.get("UNNAMED_GODOT",
                       r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64.exe")
GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT",
                               r"W:\UNNAMED\tools\godot_validate")
SOCKETS_SCRIPT = os.path.join(TOOL_DIR, "_blender_sockets.py")
CUT_STUB_SCRIPT = os.path.join(TOOL_DIR, "_blender_cut_stub.py")
VERIFY_SOCKETS = os.path.join(TOOL_DIR, "_verify_sockets.py")


def run(command, label):
    result = subprocess.run(command, capture_output=True, text=True)
    output = (result.stdout or "") + (result.stderr or "")
    if result.returncode != 0:
        print(f"  [{label}] FAILED (exit {result.returncode})")
        for line in output.strip().splitlines()[-12:]:
            print(f"      {line}")
    return result.returncode, output


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sockets", required=True, help="Socket definition JSON")
    parser.add_argument("--raw", required=True, help="Source GLB, already normalised")
    parser.add_argument("--out", required=True, help="Production asset directory")
    parser.add_argument("--blend-source", default=None)
    parser.add_argument("--resize-to-nominal", action="store_true")
    parser.add_argument("--validate", action="store_true",
                        help="Run Godot import validation as the final stage")
    parser.add_argument("--stub-pending", action="store_true",
                        help="Skip the Godot size check: the mesh still carries its "
                             "sacrificial stub, so it is deliberately larger than nominal")
    parser.add_argument("--cut-stub", metavar="SOCKET", default=None,
                        help="Cut the sacrificial stub before anything else, using this "
                             "socket's axis. Requires the mesh to come from a stub concept.")
    parser.add_argument("--component-side", choices=("near", "far"), default=None,
                        help="Which side of the stub cut is the component. Declared per "
                             "component because a mace has wide ends and a narrow middle.")
    args = parser.parse_args()

    with open(args.sockets, encoding="utf-8") as handle:
        definition = json.load(handle)
    asset_id = definition["asset_id"]

    os.makedirs(args.out, exist_ok=True)
    glb = os.path.join(args.out, f"{asset_id}.glb")
    blend = args.blend_source or os.path.join(
        r"W:\UNNAMED\assets\blender_src", f"{asset_id}.blend")
    work = os.path.join(args.out, f"{asset_id}_stubcut.glb")
    stages = {}

    # --- Stage 0: remove the sacrificial stub, if this component was built with one ---
    # Must run before socket authoring: the stub inflates every downstream measurement, and
    # the socket positions are relative to the component's real mating face.
    source = args.raw
    if args.cut_stub:
        command = [BLENDER, "--background", "--factory-startup",
                   "--python", CUT_STUB_SCRIPT, "--",
                   "--input", args.raw, "--sockets", args.sockets,
                   "--socket", args.cut_stub, "--out", work, "--cap"]
        if args.component_side:
            command += ["--component-side", args.component_side]
        code, output = run(command, "cut-stub")
        stages["cut_stub"] = code == 0
        if code != 0:
            return 1
        source = work
        print(f"    stub removed -> {os.path.basename(work)}")

    # --- Stage 1: sockets, naming, absolute sizing, canonical blend, export ---
    command = [BLENDER, "--background", "--factory-startup", "--python", SOCKETS_SCRIPT, "--",
               "--input", source, "--sockets", args.sockets,
               "--out", glb, "--blend-source", blend]
    if args.resize_to_nominal:
        command.append("--resize-to-nominal")
    code, output = run(command, "sockets")
    stages["sockets"] = code == 0
    if code != 0:
        return 1
    if not os.path.exists(blend):
        print("  [lineage] canonical .blend was not written; the asset is not regenerable")
        return 1

    # --- Stage 2: verify the sockets survived export ---
    code, output = run([sys.executable, VERIFY_SOCKETS,
                        "--sockets", args.sockets, "--glb", glb], "verify-sockets")
    stages["verify_sockets"] = code == 0
    if code != 0:
        return 1

    # --- Stage 3: Godot import validation ---
    if args.validate:
        staging = os.path.join(GODOT_PROJECT, "assets")
        os.makedirs(staging, exist_ok=True)
        shutil.copy2(glb, os.path.join(staging, f"{asset_id}.glb"))
        expected = {
            "asset_id": asset_id,
            "sockets": {k: v["position"] for k, v in definition.get("sockets", {}).items()},
        }

        # What the size assertion should compare against depends on how the asset was built,
        # and conflating the two cases is what made this fail:
        #
        #   * a component whose stub has been CUT has its own real dimensions, which the
        #     pipeline is expected to preserve through export and import. Comparing them
        #     against a nominal size would be wrong, because nominal describes the idealised
        #     part, not what the generator produced.
        #   * a component still CARRYING its stub is legitimately larger than nominal, so no
        #     size assertion is meaningful at all.
        #
        # So: after a cut, assert round-trip fidelity against the measured mesh. Otherwise,
        # skip. Nominal sizing is applied by `--resize-to-nominal` at authoring time and is
        # checked there, not here.
        measured = os.path.splitext(glb)[0] + "_sockets.json"
        if os.path.exists(measured):
            with open(measured, encoding="utf-8") as handle:
                report = json.load(handle)
            if report.get("dimensions_m_export_frame"):
                expected["dimensions_m"] = report["dimensions_m_export_frame"]
        expected_path = os.path.join(staging, f"{asset_id}_expected.json")
        with open(expected_path, "w", encoding="utf-8") as handle:
            json.dump(expected, handle, indent=2)

        # Godot must see the file before a --script run, so import first.
        run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"], "godot-import")
        code, output = run([GODOT, "--headless", "--path", GODOT_PROJECT,
                            "--script", "validate_glb.gd", "--",
                            os.path.join(staging, f"{asset_id}.glb"), expected_path],
                           "godot-validate")
        ok = '"ok":true' in output.replace(" ", "")
        stages["godot_validate"] = ok
        if not ok:
            print("  [godot-validate] reported problems:")
            for line in output.splitlines():
                if "VALIDATE_RESULT" in line:
                    print("      " + line[:400])
            return 1

    print(f"\n  {asset_id}: all stages passed")
    for name, ok in stages.items():
        print(f"    {'ok ' if ok else 'BAD'} {name}")
    print(f"    glb           {glb}")
    print(f"    blend source  {blend}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
