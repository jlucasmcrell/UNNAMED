"""Fill the undecided sounds with a random B or C, and record honestly how they were chosen.

The owner listened to 15 sounds, found the candidates within each class too similar to be worth
discriminating between, and asked for a random B or C across the rest so the prototype can move. That
is a sound call: auditioning 228 near-identical options is a worse use of the time than shipping
something playable and choosing a better model later.

What this deliberately does NOT do is mark those picks as auditioned. An owner pick and an automatic
choice are different facts, and a manifest that records both as `human_auditioned: true` would claim a
listening pass that never happened - which is exactly the kind of quiet falsehood that makes a
provenance record worthless. Automatic choices are marked `selection_basis: "auto_random_bc"` and keep
`human_auditioned: false`.

The owner's own 15 decisions are left exactly as they are, including the two they picked as A. Their
instruction was for the sounds they had not reached; overwriting a decision they actually made would
be worse than leaving it.

Usage:
    python _autoselect_bc.py --audit
    python _autoselect_bc.py --apply
"""
import argparse
import io
import json
import os
import random

ASSETS = r"W:\UNNAMED\assets"
MANIFEST = os.path.join(ASSETS, "manifests", "playable_prototype_audio_v2.json")
DELIVERED = os.path.join(ASSETS, "audio", "v2_delivered")

# Fixed so the choice is reproducible. A random selection that cannot be re-derived makes the manifest
# impossible to audit later, and "which ones were random" is a question that will be asked.
SEED = 20260924


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)

    rng = random.Random(SEED)
    changed = 0
    kept = 0
    already = 0
    missing = []
    tally = {"V2-B": 0, "V2-C": 0}

    for entry in manifest["sounds"]:
        audio_id = entry["audio_id"]
        if entry.get("human_auditioned"):
            kept += 1
            continue
        if entry.get("selection_basis") == "auto_random_bc":
            already += 1
            continue
        # Only offer a label with a delivered file behind it. A random choice that points at nothing is
        # worse than a deterministic default, because it fails at load rather than at selection.
        available = [label for label in ("b", "c")
                     if os.path.exists(os.path.join(DELIVERED, audio_id, f"candidate_{label}.wav"))]
        if not available:
            missing.append(audio_id)
            continue
        label = rng.choice(available)
        key = f"V2-{label.upper()}"
        tally[key] += 1
        entry["owner_selection"] = key
        entry["selected_candidate"] = label
        entry["delivered"] = os.path.join(
            "audio", "v2_delivered", audio_id, f"candidate_{label}.wav").replace(os.sep, "/")
        entry["generation_version"] = "v2"
        # Kept false on purpose. Nobody listened to this one.
        entry["human_auditioned"] = False
        entry["selection_basis"] = "auto_random_bc"
        changed += 1

    print(f"  owner-auditioned, left alone : {kept}")
    print(f"  auto-selected now            : {changed}")
    print(f"  already auto-selected        : {already}")
    print(f"  no B or C available          : {len(missing)}  {missing[:4]}")
    print(f"  split                        : {tally}")
    print(f"  reproducible with seed       : {SEED}")

    if not args.apply:
        print("\n  (audit only; pass --apply to write)")
        return 0

    counts = {"V2-A": 0, "V2-B": 0, "V2-C": 0, "V1": 0, "NONE": 0}
    basis = {}
    for entry in manifest["sounds"]:
        key = entry.get("owner_selection")
        if key in counts:
            counts[key] += 1
        reason = entry.get("selection_basis") or (
            "owner_audition" if entry.get("human_auditioned") else "provisional")
        basis[reason] = basis.get(reason, 0) + 1

    manifest["audition"] = {
        "owner_report": manifest.get("audition", {}).get("report"),
        "owner_decided": kept,
        "auto_selected": changed + already,
        "auto_selection_seed": SEED,
        "owner_note": (
            "The owner listened to the first 15, found the candidates within each class too similar "
            "to discriminate between, and asked for a random B or C across the remainder so the "
            "prototype could move. Automatic picks are marked selection_basis auto_random_bc and keep "
            "human_auditioned false: they are not auditions."),
        "counts_by_selection": counts,
        "selection_basis": basis,
    }
    manifest["active_selection"] = "owner"

    with io.open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print(f"\n  selections   : {counts}")
    print(f"  basis        : {basis}")
    print(f"  manifest     {MANIFEST}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
