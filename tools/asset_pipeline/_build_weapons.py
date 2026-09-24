"""Author weapon sockets into the three prototype weapons, verify, then promote.

Same staging discipline as the interaction props: build into a staging directory, confirm the
socket nodes actually arrived in the exported GLB at the positions the spec declares, and only
then copy over the live asset. A weapon whose grip does not verify is left untouched.

The socket verification is not ceremony. `_blender_sockets.py` once produced `SOCK_hinge.001` on a
re-run, which is a valid-looking node name that no caller will ever find.

Usage:
    python _build_weapons.py --staged-only
    python _build_weapons.py --apply
"""
import argparse
import json
import os
import shutil
import struct
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
SOCKETS = os.path.join(ASSETS, "sockets")
STAGING = os.path.join(ASSETS, "review", "_weapon_staging")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_blender_sockets.py")
SPEC = os.path.join(SOCKETS, "weapon_sockets.json")

GLB_VARIANTS = ("", "_lod1", "_lod2", "_lod3", "_collision_hull", "_collision_box")
SIDECARS = ("_meta.json", "_sockets.json")
JSON_CHUNK = 0x4E4F534A


def socket_positions(path):
    """World positions of SOCK_ nodes, composing the local hierarchy."""
    with open(path, "rb") as handle:
        data = handle.read()
    offset, gltf = 12, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == JSON_CHUNK:
            gltf = json.loads(data[offset:offset + length].decode("utf-8"))
        offset += length

    nodes = gltf.get("nodes", [])
    children = {}
    for index, node in enumerate(nodes):
        for child in node.get("children", []):
            children[child] = index

    def local(node):
        if "matrix" in node:
            m = node["matrix"]
            return (m[12], m[13], m[14])
        return tuple(node.get("translation", [0.0, 0.0, 0.0]))

    world = {}

    def resolve(index, seen):
        if index in world:
            return world[index]
        if index in seen:
            return (0.0, 0.0, 0.0)
        lx, ly, lz = local(nodes[index])
        parent = children.get(index)
        if parent is None:
            world[index] = (lx, ly, lz)
        else:
            px, py, pz = resolve(parent, seen | {index})
            world[index] = (px + lx, py + ly, pz + lz)
        return world[index]

    out = {}
    for index, node in enumerate(nodes):
        name = node.get("name", "")
        if name.startswith("SOCK_"):
            out[name] = resolve(index, frozenset())
    return out


def build_one(asset_id, declaration, promote):
    source = os.path.join(READY, asset_id, f"{asset_id}.glb")
    socket_file = os.path.join(SOCKETS, f"{asset_id}.json")
    if not os.path.exists(source) or not os.path.exists(socket_file):
        return False, "missing base or socket spec"

    staging = os.path.join(STAGING, asset_id)
    shutil.rmtree(staging, ignore_errors=True)
    os.makedirs(staging, exist_ok=True)

    result = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", TOOL, "--",
         "--input", source, "--sockets", socket_file,
         "--out", os.path.join(staging, f"{asset_id}.glb"),
         "--lod-faces", "8000,2500,600", "--collision-faces", "400"],
        capture_output=True, text=True, timeout=900)

    produced = os.path.join(staging, f"{asset_id}.glb")
    if not os.path.exists(produced):
        return False, f"blender produced nothing: {(result.stdout or '')[-160:]}"

    got = socket_positions(produced)
    want = declaration["sockets"]
    problems = []
    for name, spec in want.items():
        if name not in got:
            problems.append(f"{name} absent")
            continue
        drift = max(abs(got[name][i] - spec["position"][i]) for i in range(3))
        if drift > 0.01:
            problems.append(f"{name} off by {drift * 1000:.0f} mm")
    if problems:
        return False, "; ".join(problems)

    if not promote:
        return True, f"{len(want)} sockets verified (staged)"

    moved = 0
    for variant in GLB_VARIANTS:
        staged = os.path.join(staging, f"{asset_id}{variant}.glb")
        if not os.path.exists(staged):
            continue
        live = os.path.join(READY, asset_id, f"{asset_id}{variant}.glb")
        shutil.copy2(staged, live)
        if os.path.getsize(live) != os.path.getsize(staged):
            return False, f"{variant or 'base'} copied short"
        moved += 1
    for sidecar in SIDECARS:
        staged = os.path.join(staging, f"{asset_id}{sidecar}")
        if os.path.exists(staged):
            shutil.copy2(staged, os.path.join(READY, asset_id, f"{asset_id}{sidecar}"))
            moved += 1

    live_sockets = socket_positions(os.path.join(READY, asset_id, f"{asset_id}.glb"))
    missing = [n for n in want if n not in live_sockets]
    if missing:
        return False, f"promoted {moved} files but {missing} absent from the live asset"
    return True, f"{len(want)} sockets verified, {moved} files promoted"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--staged-only", action="store_true")
    args = parser.parse_args()

    spec = json.load(open(SPEC, encoding="utf-8"))["weapons"]
    ok = failed = 0
    for asset_id in sorted(spec):
        built, detail = build_one(asset_id, spec[asset_id], args.apply)
        print(f"  {'OK  ' if built else 'FAIL'} {asset_id:<32} "
              f"{len(spec[asset_id]['sockets'])} sockets  {detail}")
        if built:
            ok += 1
        else:
            failed += 1
    print(f"\n  {ok} weapons, {failed} failed")
    if not args.apply:
        print("  (staged only; pass --apply to promote)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
