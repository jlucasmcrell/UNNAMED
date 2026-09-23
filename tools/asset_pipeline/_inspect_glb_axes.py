"""Report the axis conventions and true bounds of a GLB as an engine will read it.

Wave 0 needs the coordinate contract stated from evidence, not assumption. This reads a
GLB's own JSON and its position accessor min/max, so the up axis and the real-world extent
are both measured rather than inferred from the authoring tool.
"""
import json
import os
import struct
import sys


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    _, _, length = struct.unpack_from("<4sII", data, 0)
    offset, gltf, binary = 12, None, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        chunk = data[offset:offset + chunk_length]
        if chunk_type == 0x4E4F534A:
            gltf = json.loads(chunk.decode("utf-8"))
        elif chunk_type == 0x004E4942:
            binary = chunk
        offset += chunk_length
    return gltf, binary


def main():
    path = sys.argv[1]
    gltf, _ = read_glb(path)
    print(f"file        : {os.path.basename(path)}  ({os.path.getsize(path)/1e6:.2f} MB)")
    print(f"generator   : {gltf.get('asset', {}).get('generator', '?')}")
    print(f"version     : {gltf.get('asset', {}).get('version', '?')}")

    for index, mesh in enumerate(gltf.get("meshes", [])):
        for prim in mesh.get("primitives", []):
            pos = gltf["accessors"][prim["attributes"]["POSITION"]]
            lo, hi = pos.get("min"), pos.get("max")
            if lo and hi:
                size = [round(h - l, 4) for l, h in zip(lo, hi)]
                print(f"mesh[{index}] {mesh.get('name','?')}")
                print(f"    min      {[round(v,4) for v in lo]}")
                print(f"    max      {[round(v,4) for v in hi]}")
                print(f"    extent   {size}")
                tallest = size.index(max(size))
                print(f"    longest axis = {'XYZ'[tallest]} ({max(size):.4f})")

    print(f"nodes       : {len(gltf.get('nodes', []))}")
    print(f"materials   : {[m.get('name','?') for m in gltf.get('materials', [])]}")
    print(f"skins       : {len(gltf.get('skins', []))}")
    attrs = set()
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            attrs |= set(prim["attributes"].keys())
    print(f"attributes  : {sorted(attrs)}")


if __name__ == "__main__":
    main()
