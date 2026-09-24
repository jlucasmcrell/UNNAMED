"""Write the Wave 0 asset inventory report: pipeline status plus every asset, by family.

`_catalog_assets.py` groups assets by the 3D *sizing* category it infers from keywords, which is
right for choosing a target size but wrong as an inventory taxonomy: `travel_ice_axe` and
`magic_scrying_bowl` both land under "weapon" because their names contain "axe" and "bowl"-adjacent
keywords. The asset id prefix is the authoritative family, so this report groups by that instead.

Reads catalog.json for the measured per-asset values and the sockets/rigged/PROOFSET trees for
build state, so nothing is re-derived from the GLBs here.

Usage:
    python _write_asset_report.py --out W:\\UNNAMED\\docs\\WAVE_0_ASSET_INVENTORY_REPORT.md
"""
import argparse
import collections
import datetime
import json
import os

ASSETS = r"W:\UNNAMED\assets"
CATALOG = os.path.join(ASSETS, "catalog.json")
READY = os.path.join(ASSETS, "ready")
RIGGED = os.path.join(ASSETS, "rigged")
PROOFSET = os.path.join(ASSETS, "PROOFSET")
CONCEPTS = os.path.join(ASSETS, "concepts")

# Families that are 2D deliverables: their concept art is the finished artefact and no 3D build
# is expected, so they are excluded from the "still to build" count.
TWO_D = {"icon", "material"}

# Concept art that exists only to exercise or choose between pipeline options. It is not a
# deliverable, so it must not appear as outstanding work: the test_/methodA_/methodB_ armour and
# stub experiments, the pommel_var_ candidates rendered to pick one pommel, and the parity probe.
SCRATCH_PREFIXES = ("test_", "methodA", "methodB", "pommel_var_", "paritytst", "_")

FAMILY_LABEL = {
    "weapon": "Weapons",
    "weaponcomp": "Weapon components (modular)",
    "armour": "Armour",
    "magiccomp": "Magic components (modular)",
    "magic": "Magic and ritual props",
    "prop": "Props",
    "creature": "Creatures",
    "animal": "Animals",
    "mount": "Mounts",
    "npc": "NPCs",
    "race": "Races",
    "race2": "Races (wave 2 representatives)",
    "racebody": "Race bodies (canonical)",
    "raceclass": "Race classes",
    "item": "Items",
    "container": "Containers",
    "travel": "Travel and traversal",
    "vehicle": "Vehicles",
    "tool": "Tools",
    "herb": "Herbs",
    "flora": "Flora",
    "reagent": "Reagents",
    "resource": "Resources",
    "icon": "UI icons (2D)",
    "material": "World materials (2D)",
}


def family_of(asset_id):
    return asset_id.split("_")[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", default=r"W:\UNNAMED\docs\WAVE_0_ASSET_INVENTORY_REPORT.md")
    args = parser.parse_args()

    catalog = json.load(open(CATALOG, encoding="utf-8"))
    entries = {e["id"]: e for e in catalog["assets"]}

    rigged = set(os.listdir(RIGGED)) if os.path.isdir(RIGGED) else set()
    proofset = set(d for d in os.listdir(PROOFSET)
                   if os.path.isdir(os.path.join(PROOFSET, d))) if os.path.isdir(PROOFSET) else set()
    concepts = set(f[:-4] for f in os.listdir(CONCEPTS) if f.endswith(".png")) \
        if os.path.isdir(CONCEPTS) else set()

    by_family = collections.defaultdict(list)
    for asset_id in entries:
        by_family[family_of(asset_id)].append(asset_id)

    # Pending work per family: a rendered concept with no built asset behind it.
    pending = collections.defaultdict(list)
    for concept_id in concepts:
        if concept_id in entries or concept_id.startswith(SCRATCH_PREFIXES):
            continue
        pending[family_of(concept_id)].append(concept_id)

    lines = []
    add = lines.append
    now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")

    add("# Wave 0 asset inventory report")
    add("")
    add(f"Generated {now} from `assets/catalog.json` (catalog built {catalog['generated']}).")
    add("")
    add("Grouped by **asset-id prefix**, which is the authoritative family. The catalog groups by")
    add("inferred 3D sizing category instead, which mis-files things like `travel_ice_axe` under")
    add("weapons; that grouping is for choosing target dimensions, not for inventory.")
    add("")

    # ---- status ----
    add("## 1. Status")
    add("")
    total_tris = catalog["total_triangles"]
    total_gb = catalog["total_bytes"] / (1024 ** 3)
    add(f"- **Assets built: {catalog['asset_count']}** "
        f"({total_tris:,} triangles, {total_gb:.2f} GB of base GLB)")
    scratch = [c for c in concepts if c.startswith(SCRATCH_PREFIXES)]
    add(f"- **Concepts rendered: {len(concepts)}** "
        f"({len(scratch)} of them pipeline experiments, not deliverables — see the note below)")
    add(f"- **Rigged: {len(rigged)}**")
    add(f"- **Proof set: {len(proofset)}/15**")
    pending_3d = sum(len(v) for k, v in pending.items() if k not in TWO_D)
    pending_2d = sum(len(v) for k, v in pending.items() if k in TWO_D)
    add(f"- **Concepts rendered but not yet built: {pending_3d} 3D**"
        f"{f', {pending_2d} 2D (2D needs no build)' if pending_2d else ''}")
    add("")
    if scratch:
        add(f"Excluded from the outstanding-work counts: {', '.join(sorted(scratch))}. These were")
        add("rendered to test the sacrificial-stub convention, compare the armour fit methods, or")
        add("choose between pommel candidates. They select pipeline options rather than ship.")
        add("")

    add("### Build state per family")
    add("")
    add("| Family | Built | Still to build |")
    add("|---|---:|---:|")
    for family in sorted(set(by_family) | set(pending), key=lambda f: -len(by_family.get(f, []))):
        built = len(by_family.get(family, []))
        left = len(pending.get(family, []))
        note = " — 2D, no build" if family in TWO_D else ""
        add(f"| {FAMILY_LABEL.get(family, family)} (`{family}_`) | {built} | {left}{note} |")
    add("")

    # ---- the listing ----
    add("## 2. Every asset, by family")
    add("")
    add("`R` marks a rigged export in `assets/rigged/`; `P` marks membership in the 15-asset proof")
    add("set. Dimensions are the measured bounding box of the base mesh.")
    add("")

    for family in sorted(by_family, key=lambda f: (-len(by_family[f]), f)):
        ids = sorted(by_family[family])
        add(f"### {FAMILY_LABEL.get(family, family)} — {len(ids)}")
        add("")
        add("| Asset | Tris | Dimensions (m) | LOD budgets | Flags |")
        add("|---|---:|---|---|---|")
        for asset_id in ids:
            e = entries[asset_id]
            dims = e.get("dimensions_m") or [0, 0, 0]
            lods = e.get("lods") or {}
            budgets = "/".join(str(v) for v in lods.values()) if lods else "-"
            flags = []
            if asset_id in rigged:
                flags.append("R")
            if asset_id in proofset:
                flags.append("P")
            add(f"| `{asset_id}` | {e.get('triangles', 0):,} | "
                f"{dims[0]:.3f} x {dims[1]:.3f} x {dims[2]:.3f} | {budgets} | "
                f"{' '.join(flags) or '-'} |")
        add("")

    # ---- pending ----
    if pending:
        add("## 3. Rendered but not yet built")
        add("")
        for family in sorted(pending, key=lambda f: -len(pending[f])):
            add(f"- **{FAMILY_LABEL.get(family, family)}** ({len(pending[family])}): "
                + ", ".join(f"`{x}`" for x in sorted(pending[family])))
        add("")

    add("## 4. Where the pipeline stands")
    add("")
    add("See `WAVE_0_NAMING_AND_HOST_ALLOCATION.md` for the host allocation, the naming migration")
    add("and the ASTRAL tunnel failure. In short: BEAST and ASTRAL build 3D on disjoint id")
    add("prefixes so they can share one `ready/` tree, and RAZER is a 2D-only host because")
    add("Trellis2 is core ComfyUI added in 0.34.0 and RAZER runs 0.33.0.")
    add("")

    text = "\n".join(lines)
    with open(args.out, "w", encoding="utf-8") as handle:
        handle.write(text)
    print(f"  wrote {args.out}")
    print(f"  {catalog['asset_count']} assets across {len(by_family)} families")
    print(f"  pending 3D: {pending_3d}, pending 2D: {pending_2d}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
