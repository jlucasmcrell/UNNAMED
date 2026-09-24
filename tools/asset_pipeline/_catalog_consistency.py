"""Is assets/catalog.json consistent with the _meta.json files it summarises?

_audit_semantic_scale.py measures every asset's size from catalog.json. If the catalog is stale,
the audit compares an out-of-date number against the expectation table and _rescale_glb.py then
applies a correction to geometry that was already correct - scaling it twice.

This compares the two sources directly: catalog `dimensions_m` against the meta file's
`transform.dimensions` (Blender frame, so axis order differs but the max does not).
"""
import json
import os

ASSETS = r"W:\UNNAMED\assets"
catalog = json.load(open(os.path.join(ASSETS, "catalog.json"), encoding="utf-8"))

mismatch = []
missing = 0
same = 0
for entry in catalog["assets"]:
    asset_id = entry["id"]
    meta_path = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_meta.json")
    if not os.path.exists(meta_path):
        missing += 1
        continue
    meta = json.load(open(meta_path, encoding="utf-8"))
    dims = (meta.get("transform") or {}).get("dimensions")
    if not dims:
        missing += 1
        continue
    cat_max = max(entry["dimensions_m"])
    meta_max = max(dims)
    if abs(cat_max - meta_max) > 0.005:
        mismatch.append((asset_id, cat_max, meta_max, meta_max / cat_max if cat_max else 0))
    else:
        same += 1

print(f"catalog entries        : {len(catalog['assets'])}")
print(f"catalog says same size : {same}")
print(f"catalog is STALE       : {len(mismatch)}")
print(f"no meta to compare     : {missing}")
print()
print(f"  {'asset':<46} {'catalog':>9} {'meta':>9} {'meta/catalog':>13}")
print("  " + "-" * 80)
for asset_id, cat_max, meta_max, ratio in sorted(mismatch, key=lambda r: -abs(r[3] - 1)):
    print(f"  {asset_id:<46} {cat_max:>9.4f} {meta_max:>9.4f} {ratio:>13.3f}")
