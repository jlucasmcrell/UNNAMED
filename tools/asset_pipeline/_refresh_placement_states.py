"""Refresh each placement's build state from what is actually on disk.

`ashen_hollow_landmarks.json` carries a `state` per placement, and those were written by hand as the
work progressed, so they drift: an entry that says "in this batch" keeps saying it after the asset is
built. A placement manifest that lies about what exists is worse than none, because it is the file
the level build is meant to read. This derives the state instead - built, concept only, or no asset
yet - so the manifest cannot claim more than the tree supports.

**Validation is deliberately not copied here.** This file used to write a `godot_validated` boolean
per placement, read out of the asset's own metadata. That made a second source of truth for a fact
that already has an authoritative owner, and it went stale exactly as a copied value does: eight
placements claimed `godot_validated: false` for assets whose metadata said `true`, because the copy
was taken before the validation pass that settled it.

Per-asset `ready/<id>/<id>_meta.json` owns validation truth, and `_godot_validate_assets.py` is its
only writer. A derived manifest that consumes it should reference it, not duplicate it. `_audit_
validation_truth.py` enforces that by failing if any derived manifest carries a copied validation
boolean.

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

VALIDATION_SOURCE = ("per-asset ready/<id>/<id>_meta.json, field godot_validated, written only by "
                     "_godot_validate_assets.py")


def state_for(asset_id):
    if not asset_id:
        return "no asset - terrain or trail", False
    ready = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
    concept = os.path.join(ASSETS, "concepts", f"{asset_id}.png")
    if os.path.exists(ready):
        return "built", True
    if os.path.exists(concept):
        return "concept only, 3D pending", False
    return "specified, nothing rendered yet", False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)

    counts = {}
    changes = []
    removed = 0
    for placement in manifest["placements"]:
        state, built = state_for(placement.get("asset_id"))
        counts[state] = counts.get(state, 0) + 1
        if placement.get("state") != state:
            changes.append((placement["asset_id"] or placement["role"], placement.get("state"), state))
            placement["state"] = state
        # Strip any copied validation boolean left by an earlier version of this file, so the
        # duplicate source is removed rather than merely corrected once.
        if "godot_validated" in placement:
            del placement["godot_validated"]
            removed += 1

    manifest["validation_source"] = VALIDATION_SOURCE

    for name, was, now in changes:
        print(f"  {name}\n     {was}  ->  {now}")
    print()
    for state, count in sorted(counts.items()):
        print(f"  {count:>3}  {state}")
    print(f"\n  {len(changes)} placement state(s) corrected")
    print(f"  {removed} copied validation boolean(s) removed")

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
