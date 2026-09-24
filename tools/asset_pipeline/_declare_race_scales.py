"""Declare the canonical height of each race so the scale audit can see them.

The build pipeline scales by longest axis to a per-category default, and the registry's
`family_defaults` set `race`, `racebody`, `race2`, `raceclass` and `npc` all to **1.8 m**. That is
the Veth height and only the Veth height. The consequence is that every race in the library was
normalised to 1.80 m and the audit agrees with it, because the audit resolves against those same
defaults - so a Kal who should be 1.30 m measures exactly 1.800 and passes.

`CANONICAL_BODY_AND_SKELETON.md` states the four families:

    standard_humanoid  1.80 m  0.42 m shoulder  Veth, Siann, Orenth, ordinary NPCs
    compact_broad      1.30 m  0.58 m shoulder  Kal - broad and short
    tall_narrow        2.35 m  0.34 m shoulder  Vaskaal - tall, extremely lean
    irregular_heavy    2.60 m  0.82 m shoulder  Ondrek and heavy forms

This adds a subject rule per non-standard race, which both corrects the audit's expectation and
records the shoulder width, because a race is defined by its proportions and not only its height.
The Veth baseline needs no rule: 1.8 m is already the default, and it is the one that is right.

Idempotent. Usage:
    python _declare_race_scales.py --audit
    python _declare_race_scales.py --apply
"""
import argparse
import io
import json

REGISTRY = r"W:\UNNAMED\assets\manifests\semantic_dimensions.json"

# keyword -> (height_m, shoulder_m, family, note)
RACES = {
    "kal": (1.30, 0.58, "compact_broad",
            "Kal - broad and short, with dorsal wing channels. The registry defaulted this race to "
            "1.8 m, the Veth height, so every Kal asset in the library measures 0.5 m too tall."),
    "vaskaal": (2.35, 0.34, "tall_narrow", "Vaskaal - tall and extremely lean."),
    "ondrek": (2.60, 0.82, "irregular_heavy", "Ondrek and heavy forms."),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(REGISTRY, encoding="utf-8") as handle:
        registry = json.load(handle)

    # The family defaults are the root cause and are corrected alongside the rules, so a future
    # asset of a race with no rule does not silently inherit the Veth height again.
    for family, note in (("race", "Mixed; per-race rules below resolve first."),
                         ("racebody", "Mixed; per-race rules below resolve first."),
                         ("race2", "Mixed; per-race rules below resolve first."),
                         ("raceclass", "Mixed; per-race rules below resolve first.")):
        entry = registry["family_defaults"].get(family)
        if entry and entry.get("note", "").startswith("Mixed"):
            continue
        registry["family_defaults"][family] = {
            "longest_m": 1.8, "confidence": "low",
            "note": ("Mixed. 1.8 m is the VETH height and only correct for Veth, Siann and Orenth; "
                     "Kal 1.30 m, Vaskaal 2.35 m, Ondrek 2.60 m. A race asset with no subject rule "
                     "will be wrong here - see _declare_race_scales.py."),
        }

    added = []
    for keyword, (height, shoulder, family, note) in sorted(RACES.items()):
        # One rule per id PREFIX, because `_audit_semantic_scale.py` only applies a subject rule when
        # `rule["family"] == asset_id.split("_")[0]`. A race is not one prefix: the Kal appear as
        # `npc_kal_smith`, `race_kal_representative` and `racebody_kal_pose`, so a single rule filed
        # under the fit family name (`compact_broad`) matches none of them and the audit keeps using
        # the 1.8 m default.
        #
        # Armour is deliberately absent. These are BODY heights, and `armour_kal_back_channel_a` is a
        # back-mounted piece that happens to carry `kal` in its id - applying a 1.30 m body height to
        # it flagged a correctly-sized 0.79 m armour piece as a scale failure. Armour is sized by its
        # own rules against the body it fits.
        for prefix in ("npc", "race", "racebody", "race2", "raceclass"):
            rule = {"family": prefix, "match": [keyword], "longest_m": height,
                    "shoulder_width_m": shoulder, "confidence": "high",
                    "note": f"[{family}] {note}"}
            existing = next((r for r in registry["subject_rules"]
                             if r["match"] == [keyword] and r["family"] == prefix), None)
            if existing:
                existing.update(rule)
                action = "updated"
            else:
                registry["subject_rules"].append(rule)
                action = "added"
            added.append((prefix, keyword, height, action))

    for prefix, keyword, height, action in added:
        print(f"  {action:<8} {prefix}_{keyword:<10} {height:.2f} m  ({RACES[keyword][2]})")
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
