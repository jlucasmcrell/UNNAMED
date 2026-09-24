"""Report which concept images have no built asset, so a --skip-existing build is predictable.

`_make_assets.py --skip-existing` walks the whole concepts folder, which is the right way to avoid
restarting the model per asset - but only if the number of unbuilt concepts is known first. A folder
pass that silently picks up two hundred stragglers is not the same job as one that builds fifteen.
"""
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
CONCEPTS = os.path.join(ASSETS, "concepts")
READY = os.path.join(ASSETS, "ready")


def main():
    with io.open(os.path.join(ASSETS, "requests", "phase1_bible_landmarks.json"),
                 encoding="utf-8") as handle:
        mine = {entry["id"] for entry in json.load(handle)}

    concepts = sorted(f[:-4] for f in os.listdir(CONCEPTS) if f.endswith(".png"))
    built, missing, mine_missing = [], [], []
    for asset_id in concepts:
        if os.path.exists(os.path.join(READY, asset_id, f"{asset_id}.glb")):
            built.append(asset_id)
        else:
            missing.append(asset_id)
            if asset_id in mine:
                mine_missing.append(asset_id)

    print(f"  concept images      : {len(concepts)}")
    print(f"  with a ready GLB    : {len(built)}")
    print(f"  WITHOUT a ready GLB : {len(missing)}")
    print(f"    of which this batch: {len(mine_missing)}")
    others = [a for a in missing if a not in mine]
    if others:
        print(f"    other unbuilt      : {len(others)}")
        for asset_id in others[:25]:
            print(f"        {asset_id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
