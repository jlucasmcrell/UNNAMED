"""Record the known per-asset limitations that must survive future pipeline work.

Three facts about specific Phase-1 assets are easy to lose and expensive to rediscover:

  * The Cave Hunting Spider is rigged on the quadruped plan, so its eight legs cannot be articulated
    independently. The rig works and the creature plays, but anyone expanding the creature pipeline
    needs to know this is a compromise and not a design choice.
  * `item_raw_iron_ore` is the canonical Phase-1 mining and crafting ore.
  * `resource_iron_ore` is a stylised hexagonal crystal that is the wrong object for ore. It is not
    referenced by the crafting chain or the playable manifest, so nothing consumes it today - which
    is exactly why it is a trap: a future search for "iron ore" finds it first alphabetically and
    wires a mana crystal into a mining node.

The brief is explicit that the spider limitation must be preserved under its exact tag and that the
second ore must not be deleted, only reclassified later. Both are honoured here: this adds
`known_limitations` to the asset metadata and touches no geometry.

Usage:
    python _tag_asset_limitations.py --audit
    python _tag_asset_limitations.py --apply
"""
import argparse
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
DOC = r"W:\UNNAMED\docs\ASSET_KNOWN_LIMITATIONS.md"

ARTHROPOD_TAG = "KNOWN_LIMITATION_ARTHROPOD_NEEDS_DEDICATED_RIG"

# asset_id -> (tags, note)
LIMITATIONS = {
    "creature_cave_hunting_spider": (
        [ARTHROPOD_TAG],
        "Rigged on the quadruped plan: 18 bones, so the eight legs cannot be articulated "
        "independently and gait, attack and death read as a four-limbed creature with extra "
        "decoration. The creature is playable and its clips bind correctly. Fixing it needs a "
        "dedicated arthropod rig with per-leg chains, which is out of scope for Phase 1 and must not "
        "be improvised onto the shared quadruped skeleton.",
    ),
    "item_raw_iron_ore": (
        ["CANONICAL_PHASE1_IRON_ORE"],
        "The canonical Phase-1 mining and crafting ore. The crafting chain resolves "
        "`item.material.iron_ore` to this asset as \"exact\", and it is the id in "
        "playable_prototype_assets.json. Measured 0.500 m. A rough rock with bright metallic "
        "inclusions, which is correct for raw ore.",
    ),
    "resource_iron_ore": (
        ["VISUALLY_INCORRECT_FOR_ORE_DO_NOT_WIRE_INTO_MINING"],
        "Not ore. Rendered side by side against the canonical asset it is a stylised hexagonal "
        "crystal with orange veins inside a white frame - a fantasy mana crystal. Nothing consumes "
        "it: it is absent from the crafting chain and from playable_prototype_assets.json. It is "
        "recorded rather than deleted, per the maintenance brief, because the intent is to "
        "reclassify and recontextualise it later. Until then it must not be wired into Ashen Hollow "
        "mining, and it must not be reused as iron ore.",
    ),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    written = []
    for asset_id, (tags, note) in sorted(LIMITATIONS.items()):
        meta_path = os.path.join(READY, asset_id, f"{asset_id}_meta.json")
        if not os.path.exists(meta_path):
            print(f"  MISSING meta: {asset_id}")
            continue
        with io.open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        if args.apply:
            meta["known_limitations"] = note
            meta["limitation_tags"] = tags
            with io.open(meta_path, "w", encoding="utf-8") as handle:
                json.dump(meta, handle, indent=2)
                handle.write("\n")
            written.append(asset_id)
        print(f"  {asset_id}")
        for tag in tags:
            print(f"     {tag}")

    # The two ores are the pair that gets confused, so confirm neither is missing and that the
    # canonical one is the one the playable manifest actually points at.
    print()
    manifest_path = os.path.join(ASSETS, "manifests", "playable_prototype_assets.json")
    with io.open(manifest_path, encoding="utf-8") as handle:
        playable = json.load(handle)
    referenced = [e["asset_id"] for e in playable["entries"] if "iron_ore" in e["asset_id"]]
    print(f"  playable manifest references: {referenced}")
    canonical = "item_raw_iron_ore" in referenced
    stray = "resource_iron_ore" in referenced
    print(f"  canonical ore referenced    : {canonical}")
    print(f"  incorrect ore referenced    : {stray} (must be False)")

    if args.apply:
        print(f"\n  tagged {len(written)} asset metadata record(s)")

    write_doc(args.apply)
    return 0 if canonical and not stray else 1


def write_doc(applied):
    lines = [
        "# Asset Known Limitations",
        "",
        "**Date:** 2026-09-24",
        "",
        "Per-asset facts that are easy to lose and would be expensive to rediscover. They are stored in",
        "each asset's own metadata under `known_limitations` and `limitation_tags`, so anything reading",
        "the asset sees them without having to find this document.",
        "",
    ]
    for asset_id, (tags, note) in sorted(LIMITATIONS.items()):
        lines += [
            f"## `{asset_id}`",
            "",
        ]
        for tag in tags:
            lines.append(f"**`{tag}`**")
        lines += ["", note, ""]

    lines += [
        "## The two ores, side by side",
        "",
        "| | `item_raw_iron_ore` | `resource_iron_ore` |",
        "|---|---|---|",
        "| what it is | a rough rock with metallic ore inclusions | a stylised hexagonal crystal in a "
        "white frame |",
        "| correct for ore | **yes** | no |",
        "| referenced by the crafting chain | yes, as `\"exact\"` | no |",
        "| referenced by the playable manifest | yes | no |",
        "| measured size | 0.500 m | 0.200 m |",
        "| action | keep, canonical | keep, do not wire into mining |",
        "",
        "Both were rendered and looked at before this was written. The distinction is not inferred from",
        "the names; the two assets do not look like the same class of object.",
        "",
        f"*Metadata tagged: {'yes' if applied else 'no (audit only)'}.*",
        "",
    ]
    os.makedirs(os.path.dirname(DOC), exist_ok=True)
    with io.open(DOC, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))
    print(f"  wrote {DOC}")


if __name__ == "__main__":
    raise SystemExit(main())
