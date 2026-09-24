"""Assemble the Phase-1 environment presentation manifest.

The brief asks for a clear reference for Claude once M6 playtesting is done: what the Ashen Hollow
environment actually consists of, asset by asset, with the sizes and states that are true on disk
rather than the sizes the bible hoped for.

Three sources are joined here, and each keeps its authority:

  * `ashen_hollow_landmarks.json` owns **placement** - world coordinates, role, cell.
  * `kit_assemblies.json` owns the **buildings**, which exist as assemblies of modular pieces.
  * each asset's own `ready/<id>/<id>_meta.json` owns **measured size, collision, LODs and validation**.

Nothing is copied out of the third source as a fact that could drift; sizes and validation are read
at generation time. This is the same single-source rule the validation reconciliation applied.

The bible's gameplay coordinates are not moved. This reads them and reports them.

Usage:
    python _make_environment_manifest.py
"""
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
MANIFESTS = os.path.join(ASSETS, "manifests")
READY = os.path.join(ASSETS, "ready")
CLIPS = os.path.join(ASSETS, "animation", "ready")
OUT = os.path.join(MANIFESTS, "phase1_environment.json")

# The woodlands and rocks the brief names as "selected" rather than exhaustive.
WOODLAND = [
    "flora_oak_tree", "flora_pine_tree", "flora_birch_tree", "flora_dead_tree",
    "flora_fern_clump", "flora_bramble_bush", "flora_boneleaf_bush", "flora_heather_patch",
]
ROCKS = ["rock_boulder", "rock_field_cluster", "rock_outcrop_shelf"]

MATERIAL_HINT = (
    "World materials are not yet promoted. The brief targets roughly ten Phase-1 materials - packed "
    "dirt, gravel, wet mud, forest/grass ground, timber, plaster/wattle-and-daub, rubble stone, "
    "dressed stone, roof shingle, iron - each validated for tileability, texel/world scale, "
    "normal/roughness, Godot import and absence of baked lighting. That work is not done in this "
    "pass; what exists is the generated concept library under assets/materials/."
)


def measured(asset_id):
    """Size, collision and validation, read from the asset's own metadata."""
    path = os.path.join(READY, asset_id, f"{asset_id}_meta.json")
    if not os.path.exists(path):
        return None
    with io.open(path, encoding="utf-8") as handle:
        meta = json.load(handle)
    dims = (meta.get("transform") or {}).get("dimensions") or []
    return {
        "longest_m": round(max(dims), 3) if dims else None,
        "triangles": meta.get("triangles"),
        "collision": meta.get("collision_status"),
        "lods": meta.get("lod_status"),
        "godot_validated": bool(meta.get("godot_validated")),
        "known_limitations": meta.get("known_limitations"),
    }


def clips_for(asset_id):
    """Animation clips belonging to a creature, matched on the `anim.creature.<name>.` prefix."""
    directory = os.path.join(CLIPS, "creatures")
    if not os.path.isdir(directory) or not asset_id.startswith("creature_"):
        return []
    prefix = f"anim.creature.{asset_id[len('creature_'):]}."
    return sorted(f[:-4] for f in os.listdir(directory)
                  if f.endswith(".glb") and f.startswith(prefix))


def main():
    with io.open(os.path.join(MANIFESTS, "ashen_hollow_landmarks.json"), encoding="utf-8") as handle:
        placements = json.load(handle)

    entries = []
    unresolved = []
    for placement in placements["placements"]:
        asset_id = placement.get("asset_id")
        record = {
            "role": placement.get("role"),
            "cell": placement.get("cell"),
            "x": placement.get("x"),
            "z": placement.get("z"),
            "y_band": placement.get("y"),
            "asset_id": asset_id,
            "state": placement.get("state"),
            "note": placement.get("note"),
        }
        if asset_id:
            metrics = measured(asset_id)
            if metrics:
                record.update(metrics)
            else:
                unresolved.append(asset_id)
        if asset_id and asset_id.startswith("creature_"):
            found = clips_for(asset_id)
            if found:
                record["animation_clips"] = found
        entries.append(record)

    def side(group_id, title, ids, note):
        rows = []
        for asset_id in ids:
            metrics = measured(asset_id)
            if metrics:
                rows.append({"asset_id": asset_id, **metrics})
        return {"id": group_id, "title": title, "note": note, "assets": rows}

    # Buildings come from the assembly manifest, which is their real form.
    with io.open(os.path.join(MANIFESTS, "kit_assemblies.json"), encoding="utf-8") as handle:
        assemblies = json.load(handle)
    buildings = []
    unbuilt = []
    for name, record in sorted((assemblies.get("assemblies") or {}).items()):
        metrics = measured(name)
        built = metrics is not None
        if not built:
            unbuilt.append(name)
        buildings.append({
            "assembly_id": name,
            "built": built,
            "pieces": len(record.get("pieces") or []),
            "footprint_m": record.get("footprint_m"),
            "wall_height_m": record.get("wall_height_m"),
            "note": record.get("note"),
            **(metrics or {}),
        })

    document = {
        "version": 1,
        "comment": [
            "Phase-1 environment presentation manifest for the Ashen Hollow prototype.",
            "",
            "Placement coordinates come from ashen_hollow_landmarks.json and are the bible's; they are",
            "reported here, not moved. Sizes, collision, LODs and validation are read from each asset's",
            "own metadata at generation time, so this file cannot go stale the way a copied fact does.",
            "",
            "Intended for whoever builds the level after the M6 playtest.",
        ],
        "authority": "PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md",
        "placements": entries,
        "buildings": buildings,
        "side_sets": [
            side("woodland", "Charwood woodland", WOODLAND,
                 "Selected, not exhaustive. The brief asks for selected woodland assets; these are the "
                 "ones the waystation and Charwood verge actually use."),
            side("rocks", "Blackvein and Foldscar rocks", ROCKS,
                 "Selected rock assets for the quarry and the Foldscar ruin."),
        ],
        "world_materials": {
            "status": "not_promoted",
            "note": MATERIAL_HINT,
            "concept_library": "assets/materials/",
        },
        "unresolved": sorted(set(unresolved)),
        "coverage": {
            "placements": len(entries),
            "with_geometry": sum(1 for e in entries if e.get("longest_m")),
            "validated": sum(1 for e in entries if e.get("godot_validated")),
            "buildings_declared": len(buildings),
            "buildings_built": sum(1 for b in buildings if b["built"]),
            "buildings_unbuilt": sorted(unbuilt),
        },
        "note_on_unbuilt": (
            "kit_assemblies.json declares eight assemblies; six have no geometry because only the two "
            "the Phase-1 waystation needs - forge_shed and longhouse - were instantiated. The other six "
            "are plans, not assets. The waystation's well is covered by the separate built asset "
            "building_well, not by the unbuilt `world_well` assembly, so nothing Phase-1 depends on is "
            "missing. Recorded here so the declaration is not mistaken for a built count."
        ),
    }

    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")

    print(f"  placements   : {len(entries)}")
    print(f"  with geometry: {document['coverage']['with_geometry']}")
    print(f"  validated    : {document['coverage']['validated']}")
    print(f"  buildings    : {len(buildings)}")
    print(f"  unresolved   : {len(document['unresolved'])} {document['unresolved'][:4]}")
    print(f"  wrote {OUT}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
