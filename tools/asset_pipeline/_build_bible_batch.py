"""Build the Phase-1 bible landmarks at their declared real-world size, idempotently.

Every asset in the batch goes to `_make_assets.py` with an explicit `--target-size`, because the
category default is what produced a 0.5 m Ashen Waystone: the pipeline scales by longest axis to
`prop` 0.5 m when nothing else tells it otherwise, and a landmark that is the same size as a barrel
is worse than no landmark at all.

Re-runnable. An asset whose `ready/` meta already records the declared target size is skipped; one
whose size disagrees is archived to `_superseded/` and rebuilt rather than overwritten, because the
previous build is still the only evidence of what the generator produced.

Waits are avoided by checking for the concept: an id whose concept has not rendered yet is reported
and left alone, so this can be run again as the concept batch progresses.

Usage:
    python _build_bible_batch.py --audit
    python _build_bible_batch.py --apply
    python _build_bible_batch.py --apply --only landmark_ashen_waystone
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
READY = os.path.join(ASSETS, "ready")
SUPERSEDED = os.path.join(ASSETS, "_superseded", "bible_batch_prescale")
REQUESTS = os.path.join(ASSETS, "requests", "phase1_bible_landmarks.json")
REGISTRY = os.path.join(ASSETS, "manifests", "semantic_dimensions.json")
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
MAKE_ASSETS = os.path.join(TOOL_DIR, "_make_assets.py")


def declared_sizes():
    """Read the expected longest axis for each requested id out of the dimension registry."""
    with io.open(REGISTRY, encoding="utf-8") as handle:
        registry = json.load(handle)
    sizes = {}
    for rule in registry["subject_rules"]:
        for keyword in rule["match"]:
            sizes[keyword.replace(" ", "_")] = rule["longest_m"]
    return sizes


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true", help="Report only (the default)")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--only", nargs="*", default=None)
    args = parser.parse_args()

    with io.open(REQUESTS, encoding="utf-8") as handle:
        entries = json.load(handle)
    # An entry with build:false is art direction rather than a reconstruction target - the two
    # buildings, which must be assembled from the kit instead. Its concept still gets rendered and
    # is still referenced; it just has no 3D build here.
    art_only = [e["id"] for e in entries if e.get("build") is False]
    wanted = [entry["id"] for entry in entries if entry.get("build") is not False]
    if args.only:
        wanted = [w for w in wanted if w in args.only]
    sizes = declared_sizes()

    pending, missing_concept, wrong_size, done = [], [], [], []
    for asset_id in wanted:
        concept = os.path.join(CONCEPTS, f"{asset_id}.png")
        meta_path = os.path.join(READY, asset_id, f"{asset_id}_meta.json")
        expected = sizes.get(asset_id)
        if expected is None:
            raise SystemExit(f"{asset_id}: no declared size in {REGISTRY}")
        if not os.path.exists(concept):
            missing_concept.append(asset_id)
            continue
        if not os.path.exists(meta_path):
            pending.append(asset_id)
            continue
        with io.open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        actual = meta.get("target_size_m")
        if actual is None or abs(actual - expected) > 0.01:
            wrong_size.append((asset_id, actual, expected))
        else:
            done.append(asset_id)

    print(f"  {'asset id':<36} {'target':>8}  state")
    print("  " + "-" * 70)
    for asset_id in wanted:
        expected = sizes[asset_id]
        if asset_id in done:
            state = "ok"
        elif any(a == asset_id for a, _, _ in wrong_size):
            actual = next(a for i, a, _ in wrong_size if i == asset_id)
            state = f"WRONG SIZE ({actual} m) -> rebuild"
        elif asset_id in pending:
            state = "to build"
        else:
            state = "no concept yet"
        print(f"  {asset_id:<36} {expected:>7.2f}m  {state}")

    print()
    print(f"  {len(done)} correct, {len(pending)} to build, "
          f"{len(wrong_size)} at the wrong size, {len(missing_concept)} with no concept yet")
    if art_only:
        print(f"  art direction only, no 3D build: {', '.join(art_only)}")

    if not args.apply:
        print("  (audit only; pass --apply to build)")
        return 0

    # Rebuilds go first. An asset that is already on disk at the wrong size is worse than one that
    # is missing entirely, because it looks finished: the 0.5 m waystone sat in ready/ looking like
    # a delivered landmark. Doing the corrections before the new work means the tree is never in a
    # state where a wrong asset is the only asset.
    to_build = [a for a, _, _ in wrong_size] + pending
    if not to_build:
        print("  nothing to build")
        return 0

    failures = 0
    for asset_id in to_build:
        existing = os.path.join(READY, asset_id)
        if os.path.isdir(existing):
            os.makedirs(SUPERSEDED, exist_ok=True)
            destination = os.path.join(SUPERSEDED, asset_id)
            if os.path.isdir(destination):
                shutil.rmtree(destination)
            shutil.copytree(existing, destination)
            print(f"  archived {asset_id} -> {destination}")
        print(f"  building {asset_id} at {sizes[asset_id]} m ...")
        result = subprocess.run(
            [sys.executable, MAKE_ASSETS, "--only", asset_id, "--skip-existing",
             "--target-size", str(sizes[asset_id]), "--force"],
            capture_output=True, text=True)
        tail = (result.stdout or "").strip().splitlines()[-3:]
        for line in tail:
            print(f"      {line}")
        if result.returncode != 0:
            failures += 1
            print(f"      FAILED rc={result.returncode}")
            for line in (result.stderr or "").strip().splitlines()[-4:]:
                print(f"      {line}")
    print(f"\n  {len(to_build) - failures} built, {failures} failed")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
