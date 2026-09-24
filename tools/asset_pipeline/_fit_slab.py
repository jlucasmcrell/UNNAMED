"""Reorient a flat asset and fit it to exact real-world dimensions.

Two things the generator gets wrong for slab-shaped objects, and neither is a scale problem:

  1. **Orientation.** A door presented as a straight-on elevation came back with its long axis on
     the depth axis - the panel reconstructed lying down. A single-image reconstructor has no way
     to know which axis is world up, so this has to be corrected deterministically afterwards.
  2. **Thickness.** These models reconstruct a chunky volume, not a thin panel: a door came out
     0.66 m thick. Uniform scaling cannot fix that without ruining the width and height.

So the fit is: rotate the longest axis to up, scale uniformly so HEIGHT matches, then squash only
the depth axis to the target thickness. The width-by-height face - the one anyone actually looks
at - is never rescaled non-uniformly, so the planking and ironwork keep their proportions and only
the slab gets thin.

Optionally moves the origin to the hinge edge, so a hinged object rotates about its own pivot
rather than about its centre.

Refuses skinned meshes for the same reason the rescale tool does.

Usage:
    python _fit_slab.py --asset prop_iron_banded_oak_door_front \
        --height 2.05 --thickness 0.06 --origin hinge_left --apply
"""
import argparse
import json
import os
import shutil
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
ARCHIVE = os.path.join(ASSETS, "archive", "slab_fit")
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
FLOAT = 5126


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a GLB")
    offset, gltf, binary = 12, None, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + chunk_length]
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        elif chunk_type == BIN_CHUNK:
            binary = bytearray(payload)
        offset += chunk_length
    return version, gltf, binary


def write_glb(path, version, gltf, binary):
    text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    text += b" " * ((4 - len(text) % 4) % 4)
    chunks = [struct.pack("<II", len(text), JSON_CHUNK), text]
    padded = bytes(binary) + b"\x00" * ((4 - len(binary) % 4) % 4)
    chunks += [struct.pack("<II", len(padded), BIN_CHUNK), padded]
    body = b"".join(chunks)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", version, 12 + len(body)) + body)


def position_accessors(gltf):
    out = set()
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            index = prim.get("attributes", {}).get("POSITION")
            if index is not None:
                out.add(index)
    return out


def bounds(gltf):
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for index in position_accessors(gltf):
        acc = gltf["accessors"][index]
        for axis in range(3):
            lo[axis] = min(lo[axis], acc["min"][axis])
            hi[axis] = max(hi[axis], acc["max"][axis])
    return lo, hi


def transform(gltf, binary, mapper):
    for index in position_accessors(gltf):
        acc = gltf["accessors"][index]
        if acc["componentType"] != FLOAT or acc["type"] != "VEC3":
            raise ValueError(f"accessor {index} is not float VEC3")
        view = gltf["bufferViews"][acc["bufferView"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride") or 12
        for i in range(acc["count"]):
            base = start + i * stride
            x, y, z = struct.unpack_from("<fff", binary, base)
            struct.pack_into("<fff", binary, base, *mapper(x, y, z))

        # The min/max must move with the vertices or every later measurement lies.
        corners = []
        for cx in (acc["min"][0], acc["max"][0]):
            for cy in (acc["min"][1], acc["max"][1]):
                for cz in (acc["min"][2], acc["max"][2]):
                    corners.append(mapper(cx, cy, cz))
        acc["min"] = [min(c[a] for c in corners) for a in range(3)]
        acc["max"] = [max(c[a] for c in corners) for a in range(3)]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset", help="Asset id under ready/")
    parser.add_argument("--input", help="Direct GLB path, for fitting a staged build before its "
                                        "LODs and collision are generated from it")
    parser.add_argument("--height", type=float, required=True)
    parser.add_argument("--width", type=float, default=None,
                        help="Optional. Defaults to preserving the measured aspect.")
    parser.add_argument("--thickness", type=float, default=None,
                        help="If given, the shortest axis is squashed to this.")
    parser.add_argument("--origin", choices=("centre", "hinge_left"), default="centre")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    if args.input:
        path = args.input
        label = os.path.splitext(os.path.basename(path))[0]
    elif args.asset:
        path = os.path.join(READY, args.asset, f"{args.asset}.glb")
        label = args.asset
    else:
        print("  give --asset or --input")
        return 1
    if not os.path.exists(path):
        print(f"  not built: {path}")
        return 1

    version, gltf, binary = read_glb(path)
    if gltf.get("skins"):
        print("  skinned mesh; refusing")
        return 1

    lo, hi = bounds(gltf)
    extents = [hi[a] - lo[a] for a in range(3)]
    print(f"  measured extents (export frame XYZ): "
          f"{[round(e, 4) for e in extents]}")

    # Longest axis becomes up (+Y), shortest becomes depth (+Z), the remaining one is width.
    up_axis = max(range(3), key=lambda a: extents[a])
    thin_axis = min(range(3), key=lambda a: extents[a])
    width_axis = ({0, 1, 2} - {up_axis, thin_axis}).pop()
    print(f"  axis roles: up={('XYZ')[up_axis]} thin={('XYZ')[thin_axis]} "
          f"width={('XYZ')[width_axis]}")

    def rotate(x, y, z):
        """Re-emit (x, y, z) with the chosen axes mapped to X=width, Y=up, Z=thin."""
        source = (x, y, z)
        return (source[width_axis], source[up_axis], source[thin_axis])

    # Step 1: rotate, then re-measure so the scale factors come from the rotated geometry.
    transform(gltf, binary, rotate)
    lo, hi = bounds(gltf)
    extents = [hi[a] - lo[a] for a in range(3)]

    height_scale = args.height / extents[1] if extents[1] else 1.0
    depth_scale = 1.0
    if args.thickness:
        depth_scale = args.thickness / extents[2] if extents[2] else 1.0
    width_scale = height_scale
    if args.width:
        width_scale = args.width / extents[0] if extents[0] else 1.0

    def scale_flat(x, y, z):
        return (x * width_scale, y * height_scale, z * depth_scale)

    transform(gltf, binary, scale_flat)

    # Step 2: seat it, and optionally move the origin to the hinge edge so a hinged object
    # rotates about its own pivot instead of about its middle.
    lo, hi = bounds(gltf)
    if args.origin == "hinge_left":
        offset = (-lo[0], -lo[1], -(lo[2] + hi[2]) / 2.0)
    else:
        offset = (-(lo[0] + hi[0]) / 2.0, -lo[1], -(lo[2] + hi[2]) / 2.0)

    def seat(x, y, z):
        return (x + offset[0], y + offset[1], z + offset[2])

    transform(gltf, binary, seat)
    lo, hi = bounds(gltf)

    if not args.apply:
        print(f"  would fit to {round(hi[0] - lo[0], 3)} x {round(hi[1] - lo[1], 3)} x "
              f"{round(hi[2] - lo[2], 3)} m (W x H x D), origin {args.origin}")
        print("  (dry run; pass --apply)")
        return 0

    archive = os.path.join(ARCHIVE, label)
    os.makedirs(archive, exist_ok=True)
    backup = os.path.join(archive, os.path.basename(path))
    if not os.path.exists(backup):
        shutil.copy2(path, backup)
    write_glb(path, version, gltf, binary)

    _v, check, _b = read_glb(path)
    clo, chi = bounds(check)
    dims = [chi[a] - clo[a] for a in range(3)]
    print(f"  fitted: {round(dims[0], 3)} x {round(dims[1], 3)} x {round(dims[2], 3)} m "
          f"(W x H x D), base Y {round(clo[1], 4)}, origin {args.origin}")
    if abs(dims[1] - args.height) > 0.01:
        shutil.copy2(backup, path)
        print(f"  FAILED verification (height {dims[1]:.4f}); original restored")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
