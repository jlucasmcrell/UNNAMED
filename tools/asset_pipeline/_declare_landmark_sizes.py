"""Declare the expected real-world size of the Phase-1 bible landmarks.

The build pipeline scales every asset by its LONGEST AXIS to a per-category default (`prop` 0.5 m,
`weapon` 1.2 m, `building` 4.0 m, from UNIT_HINT_MAP in _blender_cleanup.py). That is why the Ashen
Waystone - a landmark meant to be the tallest fixed object in the settlement - first came out of the
3D pass 0.5 m tall, the same size as a barrel.

This adds the missing expectations in two places, because they serve two different consumers:

  * `subject_rules` is what `_audit_semantic_scale.py` resolves against, so the audit can tell
    whether a built asset is the right size.
  * the same numbers are passed to the builder as `--target-size`, which is what actually sets the
    scale at build time.

Idempotent: re-running replaces its own entries rather than appending duplicates. The existing file
is copied to _superseded/ before it is written.

Usage:
    python _declare_landmark_sizes.py --audit
    python _declare_landmark_sizes.py --apply
"""
import argparse
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
REGISTRY = os.path.join(ASSETS, "manifests", "semantic_dimensions.json")
BACKUP = os.path.join(ASSETS, "_superseded", "semantic_dimensions")

# id -> (longest axis in metres, family, why)
#
# The numbers come from the bible and from the concepts the assets are built from, not from taste:
# a waystone is a landmark and has to out-read a person, a billet is a thing you carry in one hand,
# and a spear is the same length as the spear family already in the library.
SIZES = {
    "landmark_ashen_waystone": (2.8, "prop",
                                "Cell A respawn landmark; must out-read a 1.8 m person standing"
                                " beside it (bible section 5)."),
    "building_smithy": (5.5, "building",
                        "Small open-fronted frontier workshop; the whole front, not one wall."),
    "building_lodge": (9.0, "building",
                       "Long single-storey communal hall with a door a person walks through."),
    "prop_cart_damaged_merchant": (2.2, "prop",
                                   "Two-wheeled trade cart, matching the intact ore cart's scale."),
    "resource_ash_haft": (1.9, "resource",
                          "Spear-length stave; it becomes the March Spear's haft (bible section 12)."),
    "prop_iron_vein_outcrop": (1.2, "prop",
                               "Waist-high outcrop the player mines, so it must read as reachable"
                               " and not as a boulder."),
    "prop_blocked_shaft": (2.6, "prop",
                           "Collapsed shaft mouth; tall enough that a person cannot be imagined"
                           " walking into it."),
    "prop_quarry_winch": (1.8, "prop", "Hand-cranked A-frame at a person's working height."),
    "resource_iron_billet": (0.45, "resource",
                             "Forearm-length worked bloom; a one-handed carry, not a bar."),
    "landmark_quiet_stone": (2.2, "prop",
                             "Quest 2 standing stone; a little taller than a person (bible"
                             " section 8)."),
    "landmark_foldscar_core": (6.0, "prop",
                               "Ring of slabs around the central depression; the widest of the"
                               " three Foldscar pieces."),
    "resource_woundmoss": (0.3, "resource", "Hand-gathered moss clump."),
    "weapon_march_spear": (2.2, "weapon",
                           "Matches weapon_boar_spear_hunting so the two read as the same class."),
    "weapon_hunting_bow": (1.7, "weapon",
                           "Self bow taller than a person; the audit already expects 1.7 m."),
    "prop_quarry_rail_track": (3.0, "prop", "A short run of narrow quarry rail."),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true", help="Report only (the default)")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    with io.open(REGISTRY, encoding="utf-8") as handle:
        registry = json.load(handle)

    rules = [r for r in registry["subject_rules"]
             if not any(key.replace(" ", "_") == r_id for r_id in SIZES for key in r["match"])]

    added = []
    for asset_id, (longest, family, note) in sorted(SIZES.items()):
        # `family` here must be the ID PREFIX, not the semantic family. `_audit_semantic_scale.py`
        # resolves a subject rule only when `rule["family"] == asset_id.split("_")[0]`, so a rule
        # filed under "prop" is silently invisible on `landmark_ashen_waystone` and the audit then
        # reports NO expectation at all. The first version of this file used the semantic family, so
        # the three actual landmarks - the waystone, the quiet stone and the Foldscar core - were
        # never validated against anything. The semantic family is kept in the note.
        prefix = asset_id.split("_")[0]
        rules.append({
            "family": prefix,
            "match": [asset_id],
            "longest_m": longest,
            "confidence": "high",
            "note": f"[{family}] {note}",
        })
        added.append((asset_id, longest))

    registry["subject_rules"] = rules

    print(f"  {'asset id':<36} {'longest':>8}  family")
    print("  " + "-" * 62)
    for asset_id, longest in added:
        print(f"  {asset_id:<36} {longest:>7.2f}m  {SIZES[asset_id][1]}")
    print(f"\n  {len(registry['subject_rules'])} subject rules in total")

    if not args.apply:
        print("  (audit only; pass --apply to write)")
        return 0

    os.makedirs(BACKUP, exist_ok=True)
    target = os.path.join(BACKUP, "semantic_dimensions.json")
    if os.path.exists(REGISTRY) and not os.path.exists(target):
        shutil.copy2(REGISTRY, target)
    with io.open(REGISTRY, "w", encoding="utf-8") as handle:
        json.dump(registry, handle, indent=2)
        handle.write("\n")
    print(f"  wrote {REGISTRY}")
    print(f"  backup {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
