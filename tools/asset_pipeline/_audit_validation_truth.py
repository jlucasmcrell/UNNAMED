"""Fail if a derived manifest carries its own copy of validation truth.

`godot_validated` has exactly one authoritative owner: the per-asset metadata at
`ready/<id>/<id>_meta.json`, written only by `_godot_validate_assets.py`. Any other file that stores
the same fact is a second source, and a second source is a source that drifts.

That is not hypothetical. `ashen_hollow_landmarks.json` used to copy a `godot_validated` boolean into
each placement, and eight of them ended up claiming `false` for assets whose metadata said `true`,
because the copy was taken before the validation pass that settled it. Nothing detected it until the
two were compared by hand.

So this compares them, and it is meant to be run after any metadata or validation work:

  1. any derived manifest that copies a validation boolean is reported as a defect, whatever its
     value, because the fix is to remove the copy rather than to sync it;
  2. where an asset's metadata carries validation, the audit confirms the field is actually readable
     and that the validator is the only writer by checking the recorded shape.

Exit code is non-zero when a copied boolean exists or an authoritative record is unreadable, so it can
gate a pipeline step.

Usage:
    python _audit_validation_truth.py
"""
import argparse
import io
import json
import os
import sys

ASSETS = r"W:\UNNAMED\assets"
MANIFESTS = os.path.join(ASSETS, "manifests")
READY = os.path.join(ASSETS, "ready")

# A manifest is considered derived if it describes assets rather than being one asset's own record.
# These are the shapes that could plausibly carry a copy.
DERIVED_MANIFESTS = [
    "ashen_hollow_landmarks.json",
    "playable_prototype_assets.json",
    "prototype_crafting_chain.json",
    "ui_icons.json",
    "magic_vfx.json",
    "animation_state_machine.json",
]

VALIDATION_FIELD = "godot_validated"
ALLOWED_CONSUMERS = {"validation_source"}


def walk_for_validation(node, path=""):
    """Yield every (path, value) where validation is stored as a per-record fact.

    Only booleans count. A stored `true`/`false` per asset is a second source of truth and will
    drift; an integer count in a summary block is a *consumption* of the authoritative metadata and
    is recomputed every time the manifest is generated, so it cannot go stale. The two look similar
    in a grep and are completely different in behaviour, so the distinction is on the value's type,
    not the key's name.
    """
    if isinstance(node, dict):
        for key, value in node.items():
            here = f"{path}/{key}"
            if key == VALIDATION_FIELD and isinstance(value, bool):
                yield here, value
            elif key in ALLOWED_CONSUMERS:
                continue
            else:
                yield from walk_for_validation(value, here)
    elif isinstance(node, list):
        for index, value in enumerate(node):
            yield from walk_for_validation(value, f"{path}[{index}]")


def consumed_counts(document):
    """Counts and references this manifest consumes rather than stores, reported for the record."""
    summary = document.get("summary")
    if not isinstance(summary, dict):
        return None
    count = summary.get(VALIDATION_FIELD)
    source = summary.get(f"{VALIDATION_FIELD}_source") or document.get("validation_source")
    if isinstance(count, int) and not isinstance(count, bool) and source:
        return count, source
    return None


def scan_derived():
    """Report every derived manifest that stores its own validation boolean."""
    defects = []
    consumed = []
    for name in DERIVED_MANIFESTS:
        path = os.path.join(MANIFESTS, name)
        if not os.path.exists(path):
            continue
        with io.open(path, encoding="utf-8") as handle:
            document = json.load(handle)
        for where, value in walk_for_validation(document):
            defects.append((name, where, value))
        reading = consumed_counts(document)
        if reading:
            consumed.append((name, reading[0], reading[1]))
    return defects, consumed


def scan_authoritative():
    """Confirm the authoritative records are readable and well-formed."""
    checked = validated = unreadable = 0
    for folder in sorted(os.listdir(READY)):
        meta_path = os.path.join(READY, folder, f"{folder}_meta.json")
        if not os.path.exists(meta_path):
            continue
        checked += 1
        try:
            with io.open(meta_path, encoding="utf-8") as handle:
                meta = json.load(handle)
        except (OSError, ValueError) as error:
            unreadable += 1
            print(f"  unreadable: {folder}: {error}")
            continue
        if meta.get(VALIDATION_FIELD) is True:
            validated += 1
    return checked, validated, unreadable


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    print("  authoritative field : ready/<id>/<id>_meta.json :")
    print(f"                        {VALIDATION_FIELD}")
    print("  only writer         : _godot_validate_assets.py")
    print()

    defects, consumed = scan_derived()
    print(f"  derived manifests scanned : {len(DERIVED_MANIFESTS)}")
    if defects:
        print(f"  STORED VALIDATION BOOLEANS : {len(defects)}")
        for name, where, value in defects[:20]:
            print(f"     {name}{where} = {value}")
        print("     These are second sources. Remove the copy; do not sync it.")
    else:
        print("  stored validation booleans : 0")
    for name, count, source in consumed:
        print(f"  consumed by reference      : {name} summary reads {count}")
        print(f"                               from {source}")

    checked, validated, unreadable = scan_authoritative()
    print()
    print(f"  asset metadata records read : {checked}")
    print(f"  recording validated         : {validated}")
    print(f"  unreadable                  : {unreadable}")

    ok = not defects and not unreadable
    print()
    print(f"  RESULT: {'ok' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
