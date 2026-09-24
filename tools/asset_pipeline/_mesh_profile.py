"""Measure how much of a mesh actually occupies its bounding box.

A bounding box is not a shape. A single stray vertex, or a mesh reconstructed from an angled
concept, inflates the box while the bulk of the geometry is still correctly proportioned. That
distinction decides whether an asset needs rebuilding or just a smarter measurement, so this
reports percentiles rather than trusting `min`/`max`.

Usage:
    python _mesh_profile.py --asset prop_iron_banded_oak_door [--asset ...]
"""
import argparse
import json
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
JSON_CHUNK = 0x4E4F534A
COMPONENT = {5126: ("f", 4), 5123: ("H", 2), 5125: ("I", 4), 5121: ("B", 1)}
TYPE_N = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def load(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, gltf, binary = 12, None, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + length]
        if kind == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        else:
            binary = payload
        offset += length
    return gltf, binary


def positions(gltf, binary):
    out = []
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            index = prim.get("attributes", {}).get("POSITION")
            if index is None:
                continue
            acc = gltf["accessors"][index]
            fmt, size = COMPONENT[acc["componentType"]]
            n = TYPE_N[acc["type"]]
            view = gltf["bufferViews"][acc["bufferView"]]
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            stride = view.get("byteStride") or n * size
            for i in range(acc["count"]):
                out.append(struct.unpack_from("<" + fmt * n, binary, start + i * stride))
    return out


def percentile(values, q):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(int(q * (len(ordered) - 1)), len(ordered) - 1)]


def profile(asset_id, sample_limit=40000):
    path = os.path.join(READY, asset_id, f"{asset_id}.glb")
    if not os.path.exists(path):
        return None
    gltf, binary = load(path)
    verts = positions(gltf, binary)
    if not verts:
        return None
    if len(verts) > sample_limit:
        step = len(verts) // sample_limit + 1
        verts = verts[::step]
    axes = list(zip(*verts))
    report = {}
    for name, axis in zip("XYZ", axes):
        lo, hi = min(axis), max(axis)
        report[name] = {
            "full": round(hi - lo, 4),
            "p2_98": round(percentile(axis, 0.98) - percentile(axis, 0.02), 4),
            "p10_90": round(percentile(axis, 0.90) - percentile(axis, 0.10), 4),
        }
    return report


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", action="append", required=True)
    args = parser.parse_args()

    print(f"  {'asset':<34} {'axis':<5} {'full':>8} {'p2-98':>8} {'p10-90':>8}  note")
    print("  " + "-" * 78)
    for asset_id in args.asset:
        report = profile(asset_id)
        if not report:
            print(f"  {asset_id:<34} not built")
            continue
        for axis in "XYZ":
            r = report[axis]
            shrink = r["full"] / r["p2_98"] if r["p2_98"] else 0
            note = ""
            if shrink > 1.15:
                note = f"bbox inflated {shrink:.2f}x by outliers"
            print(f"  {asset_id:<34} {axis:<5} {r['full']:>8.4f} {r['p2_98']:>8.4f} "
                  f"{r['p10_90']:>8.4f}  {note}")
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main())
