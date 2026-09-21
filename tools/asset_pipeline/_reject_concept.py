"""Reject a concept (and any built asset) so the pipeline stops using it.

Records why, moves any built 3D asset aside, and removes the concept image from the
working set so a later render or a resumed queue cannot pick it up again.

Usage:
    python _reject_concept.py weapon_hand_crossbow_pistol --reason "bow mounted across the stock"
    python _reject_concept.py --list
"""
import argparse
import json
import os
import shutil
import sys

ASSETS = r"W:\UNNAMED\assets"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("concept_id", nargs="?", help="Concept id without extension")
    parser.add_argument("--reason", default="", help="Why it was rejected")
    parser.add_argument("--assets", default=ASSETS)
    parser.add_argument("--list", action="store_true", help="List current rejections")
    args = parser.parse_args()

    rejected_dir = os.path.join(args.assets, "rejected")
    index_path = os.path.join(rejected_dir, "rejected.json")

    if args.list:
        if not os.path.exists(index_path):
            print("no rejections recorded")
            return 0
        with open(index_path, encoding="utf-8") as handle:
            entries = json.load(handle)
        print(f"{len(entries)} rejected concept(s):")
        for entry in entries:
            print(f"  {entry['id']}")
            print(f"      {entry.get('reason', '(no reason given)')}")
        return 0

    if not args.concept_id:
        parser.error("give a concept id, or --list")

    concept_id = args.concept_id
    moved = []

    concept_path = os.path.join(args.assets, "concepts", f"{concept_id}.png")
    if os.path.exists(concept_path):
        os.makedirs(rejected_dir, exist_ok=True)
        shutil.move(concept_path, os.path.join(rejected_dir, f"{concept_id}.png"))
        moved.append("concept image")

    for folder in ("ready", "raw"):
        source = os.path.join(args.assets, folder, concept_id)
        if os.path.isdir(source):
            os.makedirs(rejected_dir, exist_ok=True)
            target = os.path.join(rejected_dir, concept_id)
            if os.path.exists(target):
                shutil.rmtree(target)
            shutil.move(source, target)
            moved.append(f"{folder} asset")
        elif os.path.isfile(source):
            shutil.move(source, os.path.join(rejected_dir, os.path.basename(source)))
            moved.append(f"{folder} file")

    os.makedirs(rejected_dir, exist_ok=True)
    entries = []
    if os.path.exists(index_path):
        with open(index_path, encoding="utf-8") as handle:
            entries = json.load(handle)
    entries = [e for e in entries if e.get("id") != concept_id]
    entries.append({"id": concept_id, "reason": args.reason})
    with open(index_path, "w", encoding="utf-8") as handle:
        json.dump(sorted(entries, key=lambda e: e["id"]), handle, indent=2)

    print(f"rejected {concept_id}")
    print(f"  reason : {args.reason or '(none given)'}")
    print(f"  moved  : {', '.join(moved) if moved else 'nothing on disk (concept only)'}")
    print(f"  index  : {index_path}")
    print()
    print("It will not be rebuilt: the queue skips concepts that are not in concepts\\.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
