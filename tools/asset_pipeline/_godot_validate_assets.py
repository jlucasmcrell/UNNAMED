"""Drive the headless Godot asset validator over one or more GLBs.

`validate_glb.gd` does the checking; this stages the asset into the validation project, derives the
expectation file from the asset's own Blender-side contract, and reports the result. Staging is not
optional: Godot resolves `load()` against the project tree, so a GLB outside it cannot be loaded at
all, and the import cache has to be refreshed before validating or Godot validates a stale copy.

The contract records positions in Blender's Z-up frame. Godot is Y-up, so the expectation converts
with (x, y, z)_blender -> (x, z, -y)_export. Getting this wrong looks exactly like a scale failure.

Usage:
    python _godot_validate_assets.py --glb <path.glb> [--contract <skeleton.json>]
    python _godot_validate_assets.py --manifest assets/manifests/playable_prototype_assets.json
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from datetime import date

GODOT = os.environ.get("UNNAMED_GODOT", r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_win64_console.exe")
GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT", r"W:\UNNAMED\tools\godot_validate")
STAGING = os.path.join(GODOT_PROJECT, "assets")
EXPECTED_DIR = os.path.join(GODOT_PROJECT, "_expected")


def to_export_frame(p):
    """Blender Z-up metres -> glTF/Godot Y-up metres, for a POSITION."""
    x, y, z = p
    return [x, z, -y]


def extents_to_export_frame(dims):
    """Same axis reorder for a SIZE.

    A size has no sign, so negating the third component here turns 0.2988 into -0.2988 and the
    validator reports a 0.6 m scale failure on a perfectly correct asset.
    """
    x, y, z = dims
    return [abs(x), abs(z), abs(y)]


def build_expectation(contract):
    """Derive what Godot should see, from the Blender-side contract."""
    dims = contract.get("measured_dimensions_m") or contract.get("dimensions_m")
    expected = {
        "asset_id": contract.get("asset_id") or contract.get("fit_family"),
        "sockets": {},
    }
    if dims:
        expected["dimensions_m"] = [round(v, 4) for v in extents_to_export_frame(dims)]
    for socket in contract.get("attachments", []):
        expected["sockets"][socket["name"]] = [round(v, 4)
                                               for v in to_export_frame(socket["position"])]
    return expected


def stage(glb_path):
    os.makedirs(STAGING, exist_ok=True)
    os.makedirs(EXPECTED_DIR, exist_ok=True)
    name = os.path.basename(glb_path)
    staged = os.path.join(STAGING, name)
    shutil.copy2(glb_path, staged)
    # Drop any stale import cache so Godot re-imports the copy we just made.
    for suffix in (".import",):
        stale = staged + suffix
        if os.path.exists(stale):
            os.remove(stale)
    return staged


def reimport():
    subprocess.run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"],
                   capture_output=True, text=True, timeout=300)


def record_validation(glb_path):
    """Record a passing Godot validation in the asset's own metadata.

    `godot_validated` rides on every asset as a provenance field, and until now it was written as a
    permanent False with the comment "never tested" - so it could never become true, and the
    manifests reported every asset as unvalidated including the ones that pass. Validation that is
    not recorded is indistinguishable from validation that never happened, which is the one thing a
    provenance field exists to prevent.

    Silently does nothing when there is no metadata beside the GLB, because an animation clip GLB
    legitimately has none.
    """
    folder = os.path.dirname(glb_path)
    stem = os.path.basename(glb_path)[:-4]
    candidates = [os.path.join(folder, f"{stem}_meta.json")]
    if stem.endswith("_rigged"):
        candidates.append(os.path.join(folder, f"{stem[:-len('_rigged')]}_meta.json"))
    for meta_path in candidates:
        if not os.path.exists(meta_path):
            continue
        with open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        meta["godot_validated"] = True
        meta["godot_validated_on"] = date.today().isoformat()
        with open(meta_path, "w", encoding="utf-8") as handle:
            json.dump(meta, handle, indent=2)
            handle.write("\n")
        return


def validate(staged, expected, label):
    expected_path = os.path.join(EXPECTED_DIR, os.path.basename(staged) + ".expected.json")
    with open(expected_path, "w", encoding="utf-8") as handle:
        json.dump(expected, handle, indent=2)

    result = subprocess.run(
        [GODOT, "--headless", "--path", GODOT_PROJECT, "--script", "validate_glb.gd",
         "--", staged.replace("\\", "/"), expected_path.replace("\\", "/")],
        capture_output=True, text=True, timeout=300)

    report = None
    for line in result.stdout.splitlines():
        if line.startswith("VALIDATE_RESULT "):
            report = json.loads(line[len("VALIDATE_RESULT "):])
            break

    print(f"  {label}")
    if report is None:
        print(f"    NO RESULT. stdout tail: {result.stdout[-300:]!r}")
        print(f"    stderr tail: {result.stderr[-300:]!r}")
        return False

    checks = report.get("checks", {})
    print(f"    size     : {checks.get('aabb_size_m')} m")
    print(f"    position : {checks.get('aabb_position_m')}")
    print(f"    mesh     : {checks.get('mesh_resource_name')}")
    print(f"    materials: {checks.get('materials')}")
    if report.get("ok"):
        print("    RESULT: OK")
        return True
    for problem in report.get("problems", []):
        print(f"    PROBLEM: {problem}")
    print("    RESULT: FAILED")
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--glb")
    parser.add_argument("--contract")
    parser.add_argument("--manifest")
    parser.add_argument("--clips", action="store_true",
                        help="Validate every registered animation clip")
    parser.add_argument("--weapons", action="store_true",
                        help="Validate the three prototype weapons and their grip sockets")
    parser.add_argument("--family", nargs="*", default=None,
                        help="Scale-validate every built asset in these id families")
    parser.add_argument("--interaction", action="store_true",
                        help="Validate every interactive prop's scale and anchors")
    parser.add_argument("--assets-root", default=r"W:\UNNAMED\assets")
    args = parser.parse_args()

    if not os.path.exists(GODOT):
        print(f"  godot not found at {GODOT}")
        return 1

    targets = []
    if args.weapons:
        spec_path = os.path.join(args.assets_root, "sockets", "weapon_sockets.json")
        spec = json.load(open(spec_path, encoding="utf-8"))["weapons"]
        prepared = []
        skipped = 0
        for asset_id in sorted(spec):
            glb = os.path.join(args.assets_root, "ready", asset_id, f"{asset_id}.glb")
            if not os.path.exists(glb):
                print(f"  MISSING  {asset_id}")
                skipped += 1
                continue
            declaration = spec[asset_id]
            # Socket positions were read straight from the GLB, so they are already in the export
            # frame. Converting again swaps Y and Z and reports false failures.
            expected = {
                "asset_id": asset_id,
                "sockets": {name: [round(v, 4) for v in s["position"]]
                            for name, s in declaration["sockets"].items()},
            }
            prepared.append((stage(glb), expected, asset_id))
        reimport()
        passed = failed = 0
        for staged, expected, label in prepared:
            if validate(staged, expected, label):
                passed += 1
            else:
                failed += 1
        print(f"\n  {passed} passed, {failed} failed, {skipped} missing")
        return 1 if failed else 0

    if args.family:
        # Scale-only validation for assets with no authored contract: the audit's declared
        # expectation supplies the expected extents, so Godot confirms the imported AABB matches
        # what the asset is supposed to measure in metres.
        audit_path = os.path.join(args.assets_root, "manifests", "scale_audit.json")
        audit = json.load(open(audit_path, encoding="utf-8"))["results"]
        prepared = []
        skipped = 0
        for record in sorted(audit, key=lambda r: r["asset_id"]):
            if record["family"] not in args.family:
                continue
            expected_longest = record.get("expected_longest_m")
            if not expected_longest:
                continue
            asset_id = record["asset_id"]
            glb = os.path.join(args.assets_root, "ready", asset_id, f"{asset_id}.glb")
            if not os.path.exists(glb):
                skipped += 1
                continue
            dims = record["measured_dims_m"]
            # The audit records Blender-frame dims; Godot is Y-up.
            expected = {"asset_id": asset_id,
                        "dimensions_m": [round(dims[0], 4), round(dims[2], 4), round(dims[1], 4)]}
            prepared.append((stage(glb), expected, asset_id))
        reimport()
        passed = failed = 0
        for staged, expected, label in prepared:
            if validate(staged, expected, label):
                passed += 1
            else:
                failed += 1
        print(f"\n  {passed} passed, {failed} failed, {skipped} missing")
        return 1 if failed else 0

    if args.interaction:
        spec_path = os.path.join(args.assets_root, "sockets", "interaction_sockets.json")
        spec = json.load(open(spec_path, encoding="utf-8"))["props"]
        prepared = []
        skipped = 0
        for asset_id in sorted(spec):
            glb = os.path.join(args.assets_root, "ready", asset_id, f"{asset_id}.glb")
            if not os.path.exists(glb):
                print(f"  MISSING  {asset_id}")
                skipped += 1
                continue
            declaration = spec[asset_id]
            bounds = declaration["bounds_m"]
            # `bounds_m` and the socket positions were read straight out of the GLB, so they are
            # ALREADY in the export frame. Converting again swaps Y and Z and reports every prop
            # as a scale failure.
            expected = {
                "asset_id": asset_id,
                "dimensions_m": [round(bounds["max"][a] - bounds["min"][a], 4) for a in range(3)],
                "sockets": {name: [round(v, 4) for v in s["position"]]
                            for name, s in declaration["sockets"].items()},
            }
            prepared.append((stage(glb), expected, asset_id))
        reimport()
        passed = failed = 0
        for staged, expected, label in prepared:
            if validate(staged, expected, label):
                passed += 1
            else:
                failed += 1
        print(f"\n  {passed} passed, {failed} failed, {skipped} missing")
        return 1 if failed else 0

    if args.clips:
        clips_dir = os.path.join(args.assets_root, "animation", "clips")
        ready_root = os.path.join(args.assets_root, "animation", "ready")
        for name in sorted(os.listdir(clips_dir)):
            if not name.endswith(".json"):
                continue
            clip = json.load(open(os.path.join(clips_dir, name), encoding="utf-8"))
            # Player clips live under humanoid/, creature clips under creatures/.
            glb = None
            for family_dir in ("humanoid", "creatures", "mechanical"):
                candidate = os.path.join(ready_root, family_dir,
                                         clip["animation_id"] + ".glb")
                if os.path.exists(candidate):
                    glb = candidate
                    break
            if glb is None:
                glb = os.path.join(ready_root, "humanoid", clip["animation_id"] + ".glb")
            targets.append((glb, None, clip))
        prepared = []
        skipped = 0
        for glb, _contract, clip in targets:
            if not os.path.exists(glb):
                print(f"  MISSING  {os.path.basename(glb)}")
                skipped += 1
                continue
            expected = {"animation_id": clip["animation_id"],
                        "duration_s": clip["duration_s"]}
            prepared.append((stage(glb), expected, clip["animation_id"]))
        reimport()
        passed = failed = 0
        for staged, expected, label in prepared:
            if validate(staged, expected, label):
                passed += 1
            else:
                failed += 1
        print(f"\n  {passed} passed, {failed} failed, {skipped} missing")
        return 1 if failed else 0

    if args.glb:
        targets.append((args.glb, args.contract, None))
    elif args.manifest:
        doc = json.load(open(args.manifest, encoding="utf-8"))
        for entry in doc["entries"]:
            if not entry.get("ready_glb"):
                continue
            glb = os.path.join(args.assets_root, "ready", entry["ready_glb"])
            contract = os.path.join(os.path.dirname(glb),
                                    entry["asset_id"] + "_meta.json")
            targets.append((glb, contract if os.path.exists(contract) else None, None))
    else:
        print("  give --glb or --manifest")
        return 1

    # Stage every file FIRST, then import once, then validate. Staging after the import leaves the
    # copy unimported, and Godot then reports "could not load the GLB as a PackedScene" - which
    # looks like a broken asset rather than a broken order of operations.
    prepared = []
    skipped = 0
    for glb, contract, _clip in targets:
        if not os.path.exists(glb):
            print(f"  MISSING  {glb}")
            skipped += 1
            continue
        expected = {}
        label = os.path.basename(glb)
        if contract and os.path.exists(contract):
            try:
                expected = build_expectation(json.load(open(contract, encoding="utf-8")))
                label = f"{os.path.basename(glb)}  (from {os.path.basename(contract)})"
            except (ValueError, KeyError) as exc:
                print(f"  {os.path.basename(glb)}: unreadable contract ({exc}); "
                      "validating structure only")
        prepared.append((stage(glb), expected, label, glb))

    reimport()

    passed = failed = 0
    for staged, expected, label, source in prepared:
        if validate(staged, expected, label):
            passed += 1
            record_validation(source)
        else:
            failed += 1

    print(f"\n  {passed} passed, {failed} failed, {skipped} missing")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
