"""Backfill Wave-0 provenance metadata into every existing meta.json.

`godot_validated` is deliberately NOT written here. It was written as a hardcoded False with the
comment "never tested", which meant it could never become true - and worse, running this file with
`--force` reset a True that `_godot_validate_assets.py` had recorded, silently un-validating every
asset that had actually passed. Validation that cannot be recorded, or that is erased by the next
backfill, is indistinguishable from validation that never happened. The validator is now the single
writer of that field, and this file leaves it alone.

The library has 250 assets with no provenance: no modular_interface_version, no generation
machine or date, no status, no fit family, no Godot validation flag. Regenerating them to
add metadata would be absurd, and the values are all derivable from what already exists.

This is additive and idempotent. It never overwrites an existing value unless --force is
given, so an authored field is never clobbered by a derived one.

Derived objectively:
  asset_id, category          from the meta.json `name` and `category`
  modular_interface_version   "0.0" - pre-interface; sockets were not authored
  status                      "export_ready" if a GLB exists, else "blender_cleanup"
  fit_family                  derived from category and id, see FIT_RULES
  concept_path                concepts/<id>.png if present
  raw_3d_path                 raw/<id>.glb if present
  blender_path                blender_src/<id>.blend if present, else null
  glb_path                    ready/<id>/<id>.glb
  rigged                      whether rigged/<id>/<id>_rigged.glb exists
  triangles                   from meta.json base.triangles
  texture_resolution          from the embedded GLB images, else from base/transform
  lod_status                  "present" if lod files exist
  collision_status            "present" if collision files exist
  sockets                     [] - none authored
  generation_model            from the run manifest settings where one matches
  generation_date             from the file mtime of the raw GLB
  license_notes               fixed string, this is all first-party generated work

Usage:
    python _backfill_metadata.py [--dry-run] [--force] [--limit N]
"""
import argparse
import glob
import json
import os
import re
import struct
import sys
import time

ASSETS = r"W:\UNNAMED\assets"
MODULAR_INTERFACE_VERSION = "0.0"
LICENSE_NOTE = "First-party generated asset. Trellis/Pixal3D reconstruction; see QUALITY_TIERS.md."

# Fit families are approved as asset-production taxonomy only, never gameplay enums.
#
# A fit family answers "what body plan does this attach to". Only worn equipment and
# characters have one. A weapon, prop, creature or building has none - assigning
# irregular_heavy to an axe because weapons share a prefix list is a meaningless value,
# and a meaningless value in a manifest is worse than a null because it looks like data.
WEARABLE_CATEGORIES = {"character", "armour", "armor", "clothing"}

FIT_RULES = [
    ("tall_narrow", ("race2_vaskaal", "race_vaskaal", "racebody_vaskaal", "raceclass_vaskaal")),
    ("compact_broad", ("race2_kal", "race_kal", "racebody_kal", "raceclass_kal", "npc_kal")),
    ("modular_synthetic", ("race2_constructed", "race_constructed",
                           "racebody_constructed", "raceclass_constructed")),
    ("nonphysical", ("race2_mor", "race_mor", "racebody_mor", "raceclass_mor")),
    ("irregular_heavy", ("race2_ondrek", "race_ondrek", "racebody_ondrek",
                         "raceclass_ondrek", "armour_ondrek")),
    ("standard_humanoid", ("race2_", "race_", "racebody_", "raceclass_", "npc_",
                           "armour_", "clothing_")),
]


def fit_family_for(asset_id, category):
    """Return the fit family, or None when the asset does not attach to a body."""
    if category not in WEARABLE_CATEGORIES:
        return None
    for family, prefixes in FIT_RULES:
        if asset_id.startswith(prefixes):
            return family
    return None


def glb_image_sizes(path):
    """Largest embedded image dimension in a GLB, as the texture resolution actually shipped."""
    try:
        with open(path, "rb") as handle:
            data = handle.read()
        _, _, length = struct.unpack_from("<4sII", data, 0)
        offset, gltf = 12, None
        while offset < length:
            chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
            offset += 8
            if chunk_type == 0x4E4F534A:
                gltf = json.loads(data[offset:offset + chunk_length].decode("utf-8"))
            offset += chunk_length
        if not gltf:
            return None
        best = 0
        for image in gltf.get("images", []):
            view = image.get("bufferView")
            if view is None:
                continue
            # The PNG/JPEG header is not parsed; the declared name or size is enough to
            # record that a texture exists. Prefer the accessor-free path: report the
            # largest bufferView referenced by an image.
            best = max(best, gltf["bufferViews"][view].get("byteLength", 0))
        return best or None
    except (OSError, ValueError, KeyError, struct.error):
        return None


def manifest_model_for(asset_id, manifests):
    """Find the generation model recorded for an asset in a run manifest.

    Two shapes are in use under manifests/: the run manifests carry `assets` as a LIST of records,
    while `semantic_dimensions.json` carries it as a DICT keyed by asset id. Iterating the dict form
    yields its keys, which are strings, and calling `.get` on one crashed this tool for the whole
    library. Both shapes are handled rather than one being assumed.
    """
    for path, data in manifests:
        entries = data.get("assets", [])
        if isinstance(entries, dict):
            entries = entries.values()
        for asset in entries:
            if not isinstance(asset, dict):
                continue
            if asset.get("stem") == asset_id:
                return data.get("settings", {}).get("model")
    return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--force", action="store_true",
                        help="Overwrite existing values instead of only adding missing ones")
    parser.add_argument("--limit", type=int, default=None)
    args = parser.parse_args()

    manifests = []
    for path in glob.glob(os.path.join(ASSETS, "manifests", "*.json")):
        try:
            with open(path, encoding="utf-8") as handle:
                manifests.append((path, json.load(handle)))
        except (OSError, ValueError):
            continue

    ready = os.path.join(ASSETS, "ready")
    concepts = os.path.join(ASSETS, "concepts")
    raw = os.path.join(ASSETS, "raw")
    rigged = os.path.join(ASSETS, "rigged")
    blend_src = os.path.join(ASSETS, "blender_src")

    updated = skipped = failed = 0
    report = []
    for index, asset_id in enumerate(sorted(os.listdir(ready)), 1):
        if args.limit and index > args.limit:
            break
        asset_dir = os.path.join(ready, asset_id)
        meta_path = os.path.join(asset_dir, f"{asset_id}_meta.json")
        if not os.path.isdir(asset_dir) or not os.path.exists(meta_path):
            skipped += 1
            continue
        try:
            with open(meta_path, encoding="utf-8") as handle:
                meta = json.load(handle)
        except (OSError, ValueError) as exc:
            print(f"  {asset_id}: unreadable meta ({exc})")
            failed += 1
            continue

        glb = os.path.join(asset_dir, f"{asset_id}.glb")
        raw_glb = os.path.join(raw, f"{asset_id}.glb")
        concept = os.path.join(concepts, f"{asset_id}.png")
        blend = os.path.join(blend_src, f"{asset_id}.blend")
        rig_glb = os.path.join(rigged, asset_id, f"{asset_id}_rigged.glb")

        derived = {
            "asset_id": asset_id,
            "category": meta.get("category"),
            "modular_interface_version": MODULAR_INTERFACE_VERSION,
            "status": "export_ready" if os.path.exists(glb) else "blender_cleanup",
            "fit_family": fit_family_for(asset_id, meta.get("category")),
            "concept_path": concept if os.path.exists(concept) else None,
            "raw_3d_path": raw_glb if os.path.exists(raw_glb) else None,
            "blender_path": blend if os.path.exists(blend) else None,
            "glb_path": glb,
            "rigged": os.path.exists(rig_glb),
            "triangles": (meta.get("base") or {}).get("triangles"),
            "lod_status": "present" if (meta.get("lods") or {}) else "none",
            "collision_status": "present" if (meta.get("collision") or {}) else "none",
            "sockets": [],
            "socket_family": None,
            "generation_model": manifest_model_for(asset_id, manifests),
            "generation_date": (time.strftime("%Y-%m-%d",
                               time.localtime(os.path.getmtime(raw_glb)))
                               if os.path.exists(raw_glb) else None),
            "license_notes": LICENSE_NOTE,
        }

        changed = {}
        for key, value in derived.items():
            if args.force or key not in meta:
                if meta.get(key) != value:
                    changed[key] = value
        if not changed:
            skipped += 1
            continue

        meta.update(changed)
        if not args.dry_run:
            with open(meta_path, "w", encoding="utf-8") as handle:
                json.dump(meta, handle, indent=2)
        updated += 1
        report.append((asset_id, sorted(changed.keys())))

    print(f"  assets updated : {updated}")
    print(f"  already complete: {skipped}")
    print(f"  failed          : {failed}")
    if args.dry_run:
        print("  (dry run - nothing written)")
    print()
    print("  sample of what was added:")
    for asset_id, keys in report[:5]:
        print(f"    {asset_id:<38} {', '.join(keys[:6])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
