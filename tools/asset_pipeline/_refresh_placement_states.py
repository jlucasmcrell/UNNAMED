"""Refresh each placement's build state from what is actually on disk.

`ashen_hollow_landmarks.json` carries a `state` per placement, and those were written by hand as the
work progressed, so they drift: an entry that says "in this batch" keeps saying it after the asset is
built and Godot-validated. A placement manifest that lies about what exists is worse than none,
because it is the file the level build is meant to read.

This derives the state instead: built, concept only, or no asset yet, plus whether Godot has signed
off, so the manifest cannot claim more than the tree supports.

Usage:
    python _refresh_placement_states.py --audit
    python _refresh_placement_states.py --apply
"""
import argparse
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
MANIFEST = os.path.join(ASSETS, "manifests", "ashen_hollow_landmarks.json")
BACKUP = os.path.join(ASSETS, "_superseded", "ashen_hollow_landmarks")


def state_for(asset_id):
    if not asset_id:
        return "no asset - terrain or trail", False, False
    ready = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
    concept = os.path.join(ASSETS, "concepts", f"{asset_id}.png")
    meta_path = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_meta.json")
    if os.path.exists(ready):
        validated = False
        if os.path.exists(meta_path):
            with io.open(meta_path, encoding="utf-8") as handle:
                validated = bool(json.load(handle).get("godot_validated"))
        return "built", True, validated
    if os.path.exists(concept):
        return "concept only, 3D pending", False, False
    return "specified, nothing rendered yet", False, False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)

    counts = {}
    changes = []
    for placement in manifest["placements"]:
        state, built, validated = state_for(placement.get("asset_id"))
        counts[state] = counts.get(state, 0) + 1
        if placement.get("state") != state:
            changes.append((placement["asset_id"] or placement["role"], placement.get("state"), state))
            placement["state"] = state
        if built:
            placement["godot_validated"] = validated

    for name, was, now in changes:
        print(f"  {name}\n     {was}  ->  {now}")
    print()
    for state, count in sorted(counts.items()):
        print(f"  {count:>3}  {state}")
    print(f"\n  {len(changes)} placement state(s) corrected")

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0
    os.makedirs(BACKUP, exist_ok=True)
    shutil.copy2(MANIFEST, os.path.join(BACKUP, "ashen_hollow_landmarks.json"))
    with io.open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {MANIFEST}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
