"""Look for degenerate or missing triangles in a shipped garment part (F1 Gate C fix 2).

The Kal chest showed triangular holes with stone behind them. If a hole survives the opaque two-sided material fix, the cause is in the
mesh itself: a triangle with a repeated index, or an index past the vertex count, draws nothing. This reports both per garment mesh.
"""
import json
import struct
import sys
from pathlib import Path

folder = Path(sys.argv[1])
glb = next(folder.glob("*_rigged.glb"))
data = glb.read_bytes()
length, _ = struct.unpack_from("<II", data, 12)
doc = json.loads(data[20:20 + length].decode("utf-8"))
binary = 20 + length + 8

FORMATS = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4)}


def indices(accessor_index):
    accessor = doc["accessors"][accessor_index]
    view = doc["bufferViews"][accessor["bufferView"]]
    start = binary + view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    fmt, size = FORMATS[accessor["componentType"]]
    return struct.unpack_from("<" + fmt * accessor["count"], data, start)


worst = 0
for node in doc.get("nodes", []):
    if "mesh" not in node:
        continue
    name = node.get("name", "?")
    if not name.lower().endswith(("sweater", "shirt", "tunic", "jacket", "coat", "kimono", "smock", "suit", "top")):
        continue
    for primitive in doc["meshes"][node["mesh"]]["primitives"]:
        if "indices" not in primitive:
            continue
        positions = doc["accessors"][primitive["attributes"]["POSITION"]]["count"]
        index = indices(primitive["indices"])
        triangles = len(index) // 3
        degenerate = sum(1 for t in range(triangles)
                         if len({index[3 * t], index[3 * t + 1], index[3 * t + 2]}) < 3)
        out_of_range = sum(1 for i in index if i >= positions)
        if degenerate or out_of_range:
            print(f"{name}: {triangles} triangles, {degenerate} degenerate, {out_of_range} out-of-range indices, {positions} vertices")
            worst = max(worst, degenerate + out_of_range)
print("clean" if worst == 0 else f"worst mesh has {worst} bad triangles")
