"""Index everything in assets\\ready into a catalog the game side can consume.

Reads each asset's _meta.json plus the run manifests, and writes:
  assets\\catalog.json   machine-readable index, keyed by asset id
  assets\\CATALOG.md     human-readable summary grouped by category

Usage:
    python _catalog_assets.py
    python _catalog_assets.py --assets W:\\UNNAMED\\assets
"""
import argparse
import glob
import json
import os
import time

CATEGORY_ORDER = ["weapon", "shield", "tool", "prop", "creature", "character",
                  "building", "icon", "material", "unknown"]


def load_manifests(assets_root):
    """Map asset stem -> the run record that produced it."""
    records = {}
    pattern = os.path.join(assets_root, "manifests", "run_*.json")
    for path in sorted(glob.glob(pattern)):
        try:
            with open(path, encoding="utf-8") as handle:
                manifest = json.load(handle)
        except (OSError, ValueError):
            continue
        for asset in manifest.get("assets", []):
            stem = asset.get("stem")
            if not stem:
                continue
            entry = dict(asset)
            entry["_manifest"] = os.path.basename(path)
            entry["_settings"] = manifest.get("settings", {})
            records[stem] = entry
    return records


def build_entry(asset_dir, records):
    stem = os.path.basename(asset_dir)
    meta_path = os.path.join(asset_dir, f"{stem}_meta.json")
    if not os.path.exists(meta_path):
        return None
    with open(meta_path, encoding="utf-8") as handle:
        meta = json.load(handle)

    record = records.get(stem, {})
    cleanup = record.get("cleanup", {})
    settings = record.get("_settings", {})

    base_glb = os.path.join(asset_dir, f"{stem}.glb")
    entry = {
        "id": stem,
        "category": meta.get("category", "unknown"),
        "status": "engine_ready",
        "base_glb": os.path.relpath(base_glb, os.path.dirname(asset_dir)),
        "base_bytes": os.path.getsize(base_glb) if os.path.exists(base_glb) else None,
        "triangles": meta.get("base", {}).get("triangles"),
        "vertices": meta.get("base", {}).get("vertices"),
        "dimensions_m": meta.get("transform", {}).get("dimensions"),
        "target_size_m": meta.get("target_size_m"),
        "lods": {name: details.get("faces")
                 for name, details in (meta.get("lods") or {}).items()},
        "collision": {
            "hull_faces": meta.get("collision", {}).get("convex_hull_faces"),
            "box_dimensions": meta.get("collision", {}).get("box_dimensions"),
        },
        "source": {
            "concept": record.get("concept"),
            "model": settings.get("model"),
            "faces_budget": settings.get("faces"),
            "texture_size": settings.get("texture_size"),
            "manifest": record.get("_manifest"),
        },
        "files": sorted(os.listdir(asset_dir)),
    }
    return entry


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--assets", default=r"W:\UNNAMED\assets")
    args = parser.parse_args()

    ready_dir = os.path.join(args.assets, "ready")
    if not os.path.isdir(ready_dir):
        print(f"no ready folder at {ready_dir}")
        return 1

    records = load_manifests(args.assets)
    entries = []
    for name in sorted(os.listdir(ready_dir)):
        asset_dir = os.path.join(ready_dir, name)
        if not os.path.isdir(asset_dir):
            continue
        entry = build_entry(asset_dir, records)
        if entry:
            entries.append(entry)

    by_category = {}
    for entry in entries:
        by_category.setdefault(entry["category"], []).append(entry)

    catalog = {
        "generated": time.strftime("%Y-%m-%d %H:%M:%S"),
        "asset_count": len(entries),
        "total_triangles": sum(e.get("triangles") or 0 for e in entries),
        "total_bytes": sum(e.get("base_bytes") or 0 for e in entries),
        "categories": {name: len(items) for name, items in sorted(by_category.items())},
        "assets": entries,
    }

    catalog_path = os.path.join(args.assets, "catalog.json")
    with open(catalog_path, "w", encoding="utf-8") as handle:
        json.dump(catalog, handle, indent=2)

    lines = [
        "# Asset Catalog",
        "",
        f"Generated {catalog['generated']}",
        "",
        f"- assets: **{catalog['asset_count']}**",
        f"- base triangles: **{catalog['total_triangles']:,}**",
        f"- base GLB size: **{catalog['total_bytes'] / 1e6:.1f} MB**",
        "",
    ]
    for category in CATEGORY_ORDER:
        items = by_category.get(category)
        if not items:
            continue
        lines.append(f"## {category} ({len(items)})")
        lines.append("")
        lines.append("| Asset | Tris | Dimensions (m) | LODs | Hull |")
        lines.append("|---|---|---|---|---|")
        for entry in items:
            dims = entry.get("dimensions_m") or []
            dim_text = " x ".join(f"{value:g}" for value in dims) if dims else "-"
            lods = "/".join(str(v) for v in (entry.get("lods") or {}).values()) or "-"
            hull = entry.get("collision", {}).get("hull_faces") or "-"
            lines.append(f"| `{entry['id']}` | {entry.get('triangles') or 0:,} | "
                         f"{dim_text} | {lods} | {hull} |")
        lines.append("")

    md_path = os.path.join(args.assets, "CATALOG.md")
    with open(md_path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))

    print(f"{len(entries)} asset(s)")
    for category, count in sorted(catalog["categories"].items()):
        print(f"  {category:<12} {count}")
    print(f"catalog -> {catalog_path}")
    print(f"summary -> {md_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
