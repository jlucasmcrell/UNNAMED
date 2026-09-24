"""Promote unified-pipeline proof assets into the production `ready/` tree.

The modular-validated builds are the shippable ones: they carry asset-id naming, sockets,
a full LOD chain and collision proxies. The copies currently in `ready/` were produced by the
older cleanup chain, which emits `Mesh_0` / `Material_0` and no sockets, so they cannot take
part in modular assembly.

Promotion is deliberately a separate, explicit step rather than something the builder does
inline: a failed build must never be able to overwrite a working production copy.

The previous production files are moved to `archive/<asset>/` rather than deleted, because
"replaced" and "gone" are different things and only one of them is recoverable.

Usage:
    python _promote_proofset.py --dry-run
    python _promote_proofset.py
"""
import argparse
import json
import os
import shutil
import sys

ASSETS = r"W:\UNNAMED\assets"
SOURCE = os.path.join(ASSETS, "review", "wave0_modular")
READY = os.path.join(ASSETS, "ready")
ARCHIVE = os.path.join(ASSETS, "archive")

# The unified set. A promoted asset must have all of these or it is not complete.
REQUIRED = ["{a}.glb", "{a}_lod1.glb", "{a}_lod2.glb", "{a}_lod3.glb",
            "{a}_collision_hull.glb", "{a}_collision_box.glb", "{a}_sockets.json"]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    with open(os.path.join(ASSETS, "proofset_status.json"), encoding="utf-8") as h:
        status = {r["asset_id"]: r for r in json.load(h)}

    promoted, skipped, archived = [], [], []
    for asset_id, record in sorted(status.items()):
        if record.get("state") != "PASS":
            skipped.append((asset_id, f"state is {record.get('state')}"))
            continue

        source = os.path.join(SOURCE, asset_id)
        missing = [n.format(a=asset_id) for n in REQUIRED
                   if not os.path.exists(os.path.join(source, n.format(a=asset_id)))]
        if missing:
            skipped.append((asset_id, f"incomplete: {len(missing)} file(s) missing"))
            continue

        if args.dry_run:
            promoted.append(asset_id)
            continue

        # Archive whatever production copy exists, then lay down the unified set.
        target = os.path.join(READY, asset_id)
        if os.path.isdir(target):
            archive_dir = os.path.join(ARCHIVE, asset_id)
            os.makedirs(archive_dir, exist_ok=True)
            for name in os.listdir(target):
                shutil.move(os.path.join(target, name), os.path.join(archive_dir, name))
            archived.append(asset_id)

        os.makedirs(target, exist_ok=True)
        for name in os.listdir(source):
            # The intermediate stub-cut GLB is working state, not a deliverable.
            if name.endswith("_stubcut.glb"):
                continue
            shutil.copy2(os.path.join(source, name), os.path.join(target, name))
        promoted.append(asset_id)

    print(f"  promoted : {len(promoted)}")
    for a in promoted:
        mark = " (archived previous)" if a in archived else ""
        print(f"    {a}{mark}")
    if skipped:
        print(f"  skipped  : {len(skipped)}")
        for a, why in skipped:
            print(f"    {a}: {why}")
    if args.dry_run:
        print("  (dry run - nothing written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
