"""Record which library assets are the wrong *thing* rather than the wrong size.

`weapon_recurve_hunting_bow` measures a correct 1.70 m and still cannot serve as the bible's
hunting bow, because it is a modern compound target bow: a riser with a sight bracket, a long-rod
and a side-rod stabiliser crossing at the middle, and a cable guard. None of that belongs in a
frontier setting. Size auditing cannot see that; only looking at the render can.

So the registry keeps both expectations: the existing bow's real size, and the size the
bible-named replacement must hit. The notes carry the reason, because a future reader comparing
two 1.7 m bows needs to know why both exist.

Idempotent. Usage:
    python _declare_bow_families.py --audit
    python _declare_bow_families.py --apply
"""
import argparse
import io
import json

REGISTRY = r"W:\UNNAMED\assets\manifests\semantic_dimensions.json"

MODERN_BOW = (
    "A modern compound target bow - riser with a sight bracket, long-rod and side-rod stabilisers "
    "crossing at the middle, and a cable guard. Measures a correct 1.70 m and is still wrong for "
    "the setting. Superseded as the bible's hunting_bow family by weapon_hunting_bow, a plain "
    "self yew bow, which is why two 1.7 m bows exist."
)
SELF_BOW = (
    "Weapon family 2 of the bible three, built as a plain self yew bow. The library's "
    "weapon_recurve_hunting_bow is a modern compound bow and does not satisfy this family."
)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(REGISTRY, encoding="utf-8") as handle:
        registry = json.load(handle)

    changed = []
    for rule in registry["subject_rules"]:
        if rule["match"] == ["weapon_recurve_hunting_bow"] and rule["note"] != MODERN_BOW:
            rule["note"] = MODERN_BOW
            changed.append("annotated weapon_recurve_hunting_bow")
        elif rule["match"] == ["weapon_hunting_bow"] and rule["note"] != SELF_BOW:
            rule["note"] = SELF_BOW
            changed.append("annotated weapon_hunting_bow")

    if not any(r["match"] == ["weapon_hunting_bow"] for r in registry["subject_rules"]):
        registry["subject_rules"].append({
            "family": "weapon", "match": ["weapon_hunting_bow"], "longest_m": 1.7,
            "confidence": "high", "note": SELF_BOW})
        changed.append("added weapon_hunting_bow")

    for note in changed or ["nothing to change"]:
        print(f"  {note}")
    print(f"  {len(registry['subject_rules'])} subject rules")

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0
    with io.open(REGISTRY, "w", encoding="utf-8") as handle:
        json.dump(registry, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {REGISTRY}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
