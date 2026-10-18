"""WP17 step 6: install the body builds and their baked garments into a playtest asset root (a package's `primary`), additively.

python install_bodies.py <asset root> [--dry-run]

Installs, under <root>/rigged/:
  * production (MPFB, asset_work/characters/ready): pc_veth_f (the female body the package lacked, now with its opaque base layer) and
    every pc_veth_m_body_c_<build> / pc_veth_f_<build> - the .glb and its _meta.json;
  * MetaHuman (asset_work/metahuman/work/assembled): mh_veth_m / mh_veth_f and each mh_veth_<sex>_<build> - glb, textures, eye shader,
    correctives - and every baked garment player_<garment>_<sex>__<body> (work/drape/rigged);
and the MetaHuman player clips (anim.player.mh.*) under animation/. Nothing already in the root is replaced except what an earlier run
of this script installed (manifests/wp17_bodies.json lists it); before the first change the manifests folder is copied to
manifests.before_wp17_<date>. Running it again re-copies the same items.
"""
import argparse
import datetime
import glob
import json
import os
import shutil

MH = r"C:\Otherreach_Recovery\asset_work\metahuman"
CHARS = r"C:\Otherreach_Recovery\asset_work\characters\ready"
HERE = os.path.dirname(os.path.abspath(__file__))
BUILDS = [b["id"] for b in json.load(open(os.path.join(HERE, "builds.json")))["builds"]]

ap = argparse.ArgumentParser()
ap.add_argument("root")
ap.add_argument("--dry-run", action="store_true")
ap.add_argument("--bodies-only", action="store_true", help="Install approved bodies only; omit stale garment bakes and old clips")
a = ap.parse_args()
manifest_path = os.path.join(a.root, "manifests", "wp17_bodies.json")
ours = set(json.load(open(manifest_path))["installed"]) if os.path.exists(manifest_path) else set()


def suffix(build):
    return "" if build == "average" else f"_{build}"


items = []   # (source path, path under the root)
for base in (() if a.bodies_only else ("pc_veth_m_body_c", "pc_veth_f")):
    for build in BUILDS:
        asset = base + suffix(build)
        if asset == "pc_veth_m_body_c":
            continue   # the package's own male player, unchanged
        for f in (f"{asset}_rigged.glb", f"{asset}_meta.json"):
            items.append((os.path.join(CHARS, asset, f), os.path.join("rigged", asset, f)))
for sex in ("m", "f"):
    for build in BUILDS:
        asset = f"mh_veth_{sex}" + suffix(build)
        src = os.path.join(MH, "work", "assembled", asset)
        for path in glob.glob(os.path.join(src, "**", "*"), recursive=True):
            if os.path.isfile(path) and not path.endswith((".blend", ".blend1")):
                items.append((path, os.path.join("rigged", asset, os.path.relpath(path, src))))
        for garment in ([] if a.bodies_only else glob.glob(os.path.join(MH, "work", "drape", "rigged", f"player_*_{sex}__{asset}"))):
            for path in glob.glob(os.path.join(garment, "*")):
                items.append((path, os.path.join("rigged", os.path.basename(garment), os.path.basename(path))))
clips = os.path.join(MH, "work", "clips_veth", "animation")
for path in ([] if a.bodies_only else glob.glob(os.path.join(clips, "ready", "player", "anim.player.mh.*.glb")) + glob.glob(os.path.join(clips, "clips", "anim.player.mh.*.json"))):
    items.append((path, os.path.join("animation", os.path.relpath(path, clips))))

missing = [s for s, _ in items if not os.path.exists(s)]
if missing:
    raise SystemExit(f"{len(missing)} sources missing, e.g. {missing[:3]}: build and bake first")
clash = [d for _, d in items if os.path.exists(os.path.join(a.root, d)) and d.replace("\\", "/") not in ours]
if clash:
    raise SystemExit(f"{len(clash)} files already in the root that this script did not install, e.g. {clash[:3]}: not replaced")
total = sum(os.path.getsize(s) for s, _ in items)
print(f"INSTALL {len(items)} files, {total / 2**30:.2f} GB into {a.root}{' (dry run)' if a.dry_run else ''}")
if a.dry_run:
    raise SystemExit(0)
backup = os.path.join(a.root, f"manifests.before_wp17_{datetime.date.today():%Y%m%d}")
if not os.path.exists(backup):
    shutil.copytree(os.path.join(a.root, "manifests"), backup)
for src, dst in items:
    target = os.path.join(a.root, dst)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if os.path.exists(target):
        os.remove(target)   # a root mirrored by hard links: replace the file, never write through to the package it links to
    shutil.copy2(src, target)
record = {"package": "WP17 bodies and garments that follow the body (tools/character_build/install_bodies.py)",
          "installed_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds"),
          "licences": {"pc_veth_*": "MPFB 2 / MakeHuman CC0 base and assets (see each _meta.json components); Poly Haven CC0 fabrics",
                       "mh_veth_*": "MetaHuman (UE 5.8 in-editor Creator; UE EULA): never fed to generative AI, training or AI judges",
                       "player_*__mh_veth_*": "own garments (GarmentCode MIT patterns, CC0 fabrics) and CC0 MakeHuman boots; see each _garment.json",
                       "anim.player.mh.*": "Quaternius UAL1 (CC0) baked on mh_veth_m"},
          "bodies_only": a.bodies_only,
          "installed": sorted(ours | {d.replace("\\", "/") for _, d in items})}
json.dump(record, open(manifest_path, "w"), indent=1)
print("INSTALLED", len(items), "files; record", manifest_path)
