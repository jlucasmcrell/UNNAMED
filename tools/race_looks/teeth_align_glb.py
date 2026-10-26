"""Restore the closed bite on a HumGen GLB whose lower teeth were left in the throat (or above the upper row).

HumGen's facial rig moves the lower row on export (siann_export.py's align_teeth fixes it before export); a GLB made without
that fix gets the same correction here: every TeethLower primitive, at every LOD, is moved vertically so its top sits `gap`
below the top of the upper row of the same LOD, as on the aligned bodies (measured 0.016 m). Positions are bind-space, so the
skin still follows; nothing else in the file changes.

usage: python teeth_align_glb.py <in.glb> <out.glb> [gap=0.016]
"""
import json
import struct
import sys

import numpy as np

src, dst = sys.argv[1], sys.argv[2]
gap = float(sys.argv[3]) if len(sys.argv) > 3 else .016

with open(src, "rb") as f:
    magic, version, _ = struct.unpack("<III", f.read(12))
    length, _ = struct.unpack("<II", f.read(8))
    gltf = json.loads(f.read(length))
    blength, _ = struct.unpack("<II", f.read(8))
    binary = bytearray(f.read(blength))


def positions(primitive):
    accessor = gltf["accessors"][primitive["attributes"]["POSITION"]]
    view = gltf["bufferViews"][accessor["bufferView"]]
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride", 12)
    count = accessor["count"]
    data = np.ndarray((count, 3), np.float32, binary, start, (stride, 4))
    return accessor, data


meshes = {mesh["name"]: mesh for mesh in gltf["meshes"]}
moved = []
for name, mesh in meshes.items():
    if "TeethLower" not in name:
        continue
    upper = meshes.get(name.replace("TeethLower", "TeethUpper"))
    if upper is None:
        continue
    top = max(float(positions(p)[1][:, 1].max()) for p in upper["primitives"])
    lower_top = max(float(positions(p)[1][:, 1].max()) for p in mesh["primitives"])
    dy = (top - gap) - lower_top
    for primitive in mesh["primitives"]:
        accessor, data = positions(primitive)
        data[:, 1] += dy
        accessor["min"] = [float(v) for v in data.min(0)]
        accessor["max"] = [float(v) for v in data.max(0)]
    moved.append(f"{name} {dy:+.3f}")

text = json.dumps(gltf, separators=(",", ":")).encode()
text += b" " * (-len(text) % 4)
with open(dst, "wb") as f:
    f.write(struct.pack("<III", magic, version, 12 + 8 + len(text) + 8 + len(binary)))
    f.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
    f.write(struct.pack("<II", len(binary), 0x004E4942) + bytes(binary))
print(f"{src} -> {dst}: " + ", ".join(moved))
