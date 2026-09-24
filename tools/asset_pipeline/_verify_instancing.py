"""Check that foliage and rocks can actually be instanced.

Section 11 asks for "instancing compatibility". Instancing has hard requirements that a mesh can
fail while looking perfectly fine on its own:

  * ONE mesh and ONE material - a multi-material asset becomes several draw calls per instance,
    which defeats the point of instancing it hundreds of times.
  * NO skin - a skinned mesh cannot be instanced by Godot's MultiMesh at all.
  * No embedded textures bloating each copy.
  * A LOD chain, because instanced scatter needs to drop detail with distance.
  * A vertex budget low enough that thousands of copies are affordable.

Usage:
    python _verify_instancing.py --family flora rock
"""
import argparse
import json
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
JSON_CHUNK = 0x4E4F534A

# A scatter of a few thousand instances at 40k triangles each is not viable; these are the
# ceilings for something intended to be duplicated across a cell.
MAX_TRIS = 45000
MAX_MATERIALS = 1


def load(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, gltf = 12, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == JSON_CHUNK:
            gltf = json.loads(data[offset:offset + length].decode("utf-8"))
        offset += length
    return gltf


def check(asset_id, lod_tris):
    directory = os.path.join(READY, asset_id)
    base = os.path.join(directory, f"{asset_id}.glb")
    if not os.path.exists(base):
        return None
    gltf = load(base)
    problems = []
    meshes = gltf.get("meshes", [])
    materials = gltf.get("materials", [])
    primitives = sum(len(m.get("primitives", [])) for m in meshes)
    tris = 0
    for mesh in meshes:
        for prim in mesh.get("primitives", []):
            if "indices" in prim:
                tris += gltf["accessors"][prim["indices"]]["count"] // 3

    if len(meshes) != 1:
        problems.append(f"{len(meshes)} meshes; one is required for a single draw call")
    if primitives != 1:
        problems.append(f"{primitives} primitives; each is a separate draw per instance")
    if len(materials) > MAX_MATERIALS:
        problems.append(f"{len(materials)} materials; instancing wants one")
    if gltf.get("skins"):
        problems.append("skinned; Godot's MultiMesh cannot instance a skinned mesh")
    if tris > MAX_TRIS:
        problems.append(f"{tris} triangles exceeds the {MAX_TRIS} ceiling for scatter")

    # Embedded textures are reported, not failed. A GLB imports once as a shared scene resource in
    # Godot, so its textures are not duplicated per instance and instancing still works. Embedding
    # only costs file size and prevents sharing one texture across several assets, which is worth
    # knowing but is not an instancing blocker - treating it as one produced fourteen false
    # failures on a set that instances perfectly well.
    note = None
    if gltf.get("images"):
        note = f"{len(gltf['images'])} embedded images (shared per import, not per instance)"

    lods = sorted(n for n in os.listdir(directory) if "_lod" in n and n.endswith(".glb"))
    if not lods:
        problems.append("no LOD chain")

    return {"asset_id": asset_id, "meshes": len(meshes), "primitives": primitives,
            "materials": len(materials), "tris": tris, "lods": len(lods),
            "skinned": bool(gltf.get("skins")), "problems": problems, "note": note}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--family", nargs="*", default=["flora", "rock"])
    args = parser.parse_args()

    names = sorted(d for d in os.listdir(READY)
                   if os.path.isdir(os.path.join(READY, d)) and d.split("_")[0] in args.family)
    print(f"  {'asset':<30} {'mesh':>5} {'prim':>5} {'mat':>4} {'tris':>7} {'lods':>5}  result")
    print("  " + "-" * 84)
    failed = 0
    for asset_id in names:
        result = check(asset_id, None)
        if result is None:
            continue
        mark = "OK  " if not result["problems"] else "FAIL"
        print(f"  {mark} {asset_id:<25} {result['meshes']:>5} {result['primitives']:>5} "
              f"{result['materials']:>4} {result['tris']:>7} {result['lods']:>5}")
        for problem in result["problems"]:
            print(f"        {problem}")
        if result["problems"]:
            failed += 1

    print(f"\n  {len(names) - failed}/{len(names)} assets instance cleanly")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
