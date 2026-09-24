"""Author interaction anchors on every interactive prop, verify them, then promote.

Runs the standard socket pipeline per prop into a staging directory, checks that the anchors
actually arrived in the exported GLB at the positions the spec declares, and only then copies the
result over `ready/`. A prop whose anchors do not verify is left untouched, so a partial failure
cannot leave an asset with half-authored interfaces.

Staging rather than writing in place matters because `_blender_sockets.py` also regenerates LODs
and collision: if it failed halfway the live asset would be damaged.

Usage:
    python _build_interaction_props.py --asset container_chest_iron_banded
    python _build_interaction_props.py --all
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
STAGING = os.path.join(ASSETS, "review", "_interaction_staging")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_blender_sockets.py")
SPEC = os.path.join(SOCKETS, "interaction_sockets.json")
JSON_CHUNK = 0x4E4F534A

# GLB variants carry a `.glb` extension; the two sidecar files already include theirs. Getting
# this wrong promotes only the sidecars and leaves the live asset without its anchors, which
# looks like success in the report.
GLB_VARIANTS = ("", "_lod1", "_lod2", "_lod3", "_collision_hull", "_collision_box")
SIDECARS = ("_meta.json", "_sockets.json")


def glb_node_positions(path):
    """World positions of named nodes, walking the local TRS hierarchy.

    Bone-free props use unrotated nodes, but composing properly costs nothing and avoids the
    class of bug that already produced a false metre-scale error on the character sockets.
    """
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


def build_one(asset_id, spec, dry_run=False):
    source = os.path.join(READY, asset_id, f"{asset_id}.glb")
    if not os.path.exists(source):
        return False, "not built"
    socket_file = os.path.join(SOCKETS, f"{asset_id}.json")
    if not os.path.exists(socket_file):
        return False, "no socket spec"

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
        tail = (result.stdout or "")[-200:] + (result.stderr or "")[-200:]
        return False, f"blender produced nothing: {tail}"

    got = glb_node_positions(produced)
    want = spec["sockets"]
    problems = []
    for name, declaration in want.items():
        if name not in got:
            problems.append(f"{name} absent from the GLB")
            continue
        expected = declaration["position"]
        actual = got[name]
        drift = max(abs(actual[i] - expected[i]) for i in range(3))
        if drift > 0.01:
            problems.append(f"{name} off by {drift * 1000:.1f} mm")
    if problems:
        return False, "; ".join(problems)

    if dry_run:
        return True, f"{len(want)} anchors verified (staged only)"

    # Promote every produced file over the live asset, then confirm the anchors survived the
    # copy rather than trusting the loop count. Sizes are compared as well: the asset drive is a
    # network share, and a truncated copy parses its JSON chunk fine while being unloadable, so
    # the socket check alone would pass on a corrupt file.
    moved = 0
    for variant in GLB_VARIANTS:
        staged = os.path.join(staging, f"{asset_id}{variant}.glb")
        if not os.path.exists(staged):
            continue
        live = os.path.join(READY, asset_id, f"{asset_id}{variant}.glb")
        shutil.copy2(staged, live)
        if os.path.getsize(live) != os.path.getsize(staged):
            return False, (f"{variant or 'base'} copied short: "
                           f"{os.path.getsize(live)} of {os.path.getsize(staged)} bytes")
        moved += 1
    for sidecar in SIDECARS:
        staged = os.path.join(staging, f"{asset_id}{sidecar}")
        if os.path.exists(staged):
            shutil.copy2(staged, os.path.join(READY, asset_id, f"{asset_id}{sidecar}"))
            moved += 1

    live = os.path.join(READY, asset_id, f"{asset_id}.glb")
    live_sockets = glb_node_positions(live)
    missing = [name for name in want if name not in live_sockets]
    if missing:
        return False, f"promoted {moved} files but {missing} absent from the live asset"
    return True, f"{len(want)} anchors verified, {moved} files promoted"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--asset", nargs="*", default=None)
    parser.add_argument("--staged-only", action="store_true",
                        help="Verify without promoting over the live asset")
    parser.add_argument("--limit", type=int, default=None)
    args = parser.parse_args()

    if not os.path.exists(SPEC):
        print(f"  no spec at {SPEC}; run _author_interaction_sockets.py --apply first")
        return 1
    spec = json.load(open(SPEC, encoding="utf-8"))["props"]

    targets = sorted(spec)
    if args.asset:
        targets = [t for t in targets if t in args.asset]
    elif not args.all:
        print("  give --all or --asset")
        return 1
    if args.limit:
        targets = targets[:args.limit]

    passed = failed = 0
    for asset_id in targets:
        ok, detail = build_one(asset_id, spec[asset_id], args.staged_only)
        mark = "OK  " if ok else "FAIL"
        print(f"  {mark} {asset_id:<34} {detail}")
        if ok:
            passed += 1
        else:
            failed += 1

    print(f"\n  {passed} props built and verified, {failed} failed")
    if not args.staged_only:
        print(f"  staged in {STAGING}, promoted into {READY}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
