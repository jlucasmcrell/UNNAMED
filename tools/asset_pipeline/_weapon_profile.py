"""Profile a weapon's cross-section along its length, so grip sockets land on the handle.

A grip socket is only correct if it sits on the part a hand closes around. Declaring one from the
bounding box alone puts it wherever the box midpoint happens to fall, which for a sword is halfway
up the blade. This measures the real geometry: bin the vertices along the weapon's long axis and
report the cross-sectional extent in each bin, so the guard shows up as a wide spike, the blade as
a broad flat band, the grip as a narrow one, and the pommel as a small bump.

The output is what `_author_weapon_sockets.py` uses to choose positions, and it is printed so the
choice is auditable rather than asserted.

Usage:
    python _weapon_profile.py --asset weapon_arming_sword
    python _weapon_profile.py --asset weapon_boar_spear_hunting --bins 24
"""
import argparse
import json
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
JSON_CHUNK = 0x4E4F534A
COMPONENT = {5126: ("f", 4)}
TYPE_N = {"VEC3": 3}


def load_positions(path):
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
    out = []
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            acc = gltf["accessors"][prim["attributes"]["POSITION"]]
            view = gltf["bufferViews"][acc["bufferView"]]
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            stride = view.get("byteStride") or 12
            for i in range(acc["count"]):
                out.append(struct.unpack_from("<fff", binary, start + i * stride))
    return out


def profile(asset_id, bins):
    path = os.path.join(READY, asset_id, f"{asset_id}.glb")
    if not os.path.exists(path):
        return None
    verts = load_positions(path)
    if not verts:
        return None
    # Export frame is Y-up, and a weapon's length runs along Y after export.
    ys = [v[1] for v in verts]
    low, high = min(ys), max(ys)
    span = high - low
    if span <= 0:
        return None
    rows = []
    for index in range(bins):
        lo = low + span * index / bins
        hi = low + span * (index + 1) / bins
        slab = [v for v in verts if lo <= v[1] < hi]
        if not slab:
            rows.append({"bin": index, "fraction": round((index + 0.5) / bins, 3),
                         "count": 0, "width": 0.0, "depth": 0.0})
            continue
        xs = [v[0] for v in slab]
        zs = [v[2] for v in slab]
        rows.append({
            "bin": index,
            "fraction": round((index + 0.5) / bins, 3),
            "count": len(slab),
            "width": round(max(xs) - min(xs), 4),
            "depth": round(max(zs) - min(zs), 4),
            "length_m": round(span, 4),
            "base_y": round(low, 4),
        })
    return {"asset_id": asset_id, "length_m": round(span, 4),
            "base_y": round(low, 4), "top_y": round(high, 4), "rows": rows}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", action="append", required=True)
    parser.add_argument("--bins", type=int, default=20)
    args = parser.parse_args()

    for asset_id in args.asset:
        data = profile(asset_id, args.bins)
        if data is None:
            print(f"  {asset_id}: not built")
            continue
        print(f"\n  {asset_id}  length {data['length_m']} m  "
              f"(Y from {data['base_y']} to {data['top_y']})")
        print(f"    {'frac':>5} {'verts':>6} {'width':>8} {'depth':>8}  shape")
        for row in data["rows"]:
            if row["count"] == 0:
                print(f"    {row['fraction']:>5.2f} {0:>6} {'-':>8} {'-':>8}  empty")
                continue
            w, d = row["width"], row["depth"]
            biggest = max(w, d)
            if biggest < 0.06:
                shape = "narrow (grip or tip)"
            elif w > d * 2.5:
                shape = "flat and wide (blade)"
            elif d > w * 2.5:
                shape = "flat and deep"
            else:
                shape = "bulky"
            print(f"    {row['fraction']:>5.2f} {row['count']:>6} {w:>8.3f} {d:>8.3f}  {shape}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
