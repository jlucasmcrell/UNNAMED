"""Print every piece of an assembly with its true world-space box, to see whether it is a building.

`_blender_preview.py` computes framing from `obj.matrix_world @ obj.bound_box`. After a glTF import
that bakes node transforms into geometry the objects arrive at identity, so that union is the union of
*local* boxes - the same measurement that makes `_glb_bounds.py` report one wall's extent for a whole
assembly. If the camera centre and radius are derived from that, the framing of any multi-object asset
is computed from the wrong number.

This does not guess. It walks the node hierarchy, composes the transforms, and prints each piece's
world min/max, then summarises the assembly: does the floor sit at the base, do the walls form a
perimeter at a consistent height, does the roof sit above them.

Usage:
    python _assembly_layout.py <file.glb>
"""
import json
import struct
import sys


def read_gltf(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset = 12
    while offset + 8 <= len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        if kind == 0x4E4F534A:
            return json.loads(data[offset + 8:offset + 8 + length].decode("utf-8"))
        offset += 8 + length + ((4 - length % 4) % 4)
    raise ValueError("no JSON chunk")


def mat_identity():
    return [1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0]


def mat_mul(a, b):
    out = [0.0] * 16
    for column in range(4):
        for row in range(4):
            out[column * 4 + row] = sum(a[k * 4 + row] * b[column * 4 + k] for k in range(4))
    return out


def mat_from_trs(node):
    if "matrix" in node:
        return list(node["matrix"])
    tx, ty, tz = node.get("translation", [0.0, 0.0, 0.0])
    qx, qy, qz, qw = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    sx, sy, sz = node.get("scale", [1.0, 1.0, 1.0])
    xx, yy, zz = qx * qx, qy * qy, qz * qz
    xy, xz, yz = qx * qy, qx * qz, qy * qz
    wx, wy, wz = qw * qx, qw * qy, qw * qz
    m = [
        1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0.0,
        2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0.0,
        2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0.0,
        0.0, 0.0, 0.0, 1.0,
    ]
    for column, scale in enumerate((sx, sy, sz)):
        for row in range(3):
            m[column * 4 + row] *= scale
    m[12], m[13], m[14] = tx, ty, tz
    return m


def apply(m, p):
    x, y, z = p
    return (m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14])


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    gltf = read_gltf(sys.argv[1])
    nodes, meshes, accessors = gltf["nodes"], gltf["meshes"], gltf["accessors"]

    pieces = []

    def visit(index, parent):
        node = nodes[index]
        world = mat_mul(parent, mat_from_trs(node))
        if "mesh" in node:
            low = [float("inf")] * 3
            high = [float("-inf")] * 3
            for primitive in meshes[node["mesh"]].get("primitives", []):
                accessor = accessors[primitive["attributes"]["POSITION"]]
                alow, ahigh = accessor.get("min"), accessor.get("max")
                if not alow:
                    continue
                for corner in range(8):
                    point = (ahigh[0] if corner & 1 else alow[0],
                             ahigh[1] if corner & 2 else alow[1],
                             ahigh[2] if corner & 4 else alow[2])
                    moved = apply(world, point)
                    for axis in range(3):
                        low[axis] = min(low[axis], moved[axis])
                        high[axis] = max(high[axis], moved[axis])
            pieces.append((node.get("name", "?"),
                           [round(v, 3) for v in low],
                           [round(v, 3) for v in high],
                           [round(high[i] - low[i], 3) for i in range(3)]))
        for child in node.get("children", []):
            visit(child, world)

    for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
        visit(root, mat_identity())

    print(f"  {len(pieces)} pieces")
    print(f"  {'name':<30} {'centre (x,y,z)':<26} {'size (x,y,z)':<22}")
    for name, low, high, size in sorted(pieces, key=lambda p: (p[1][1], p[0])):
        centre = [round((low[i] + high[i]) / 2, 2) for i in range(3)]
        print(f"  {name:<30} {str(centre):<26} {str(size):<22}")

    lows = [min(p[1][i] for p in pieces) for i in range(3)]
    highs = [max(p[2][i] for p in pieces) for i in range(3)]
    print()
    print(f"  overall world min  {[round(v,3) for v in lows]}")
    print(f"  overall world max  {[round(v,3) for v in highs]}")
    print(f"  ground contact Y   {round(lows[1], 3)}  (should be ~0)")
    print(f"  height             {round(highs[1] - lows[1], 3)} m")
    print(f"  footprint          {round(highs[0] - lows[0], 3)} x {round(highs[2] - lows[2], 3)} m")
    return 0


if __name__ == "__main__":
    sys.exit(main())
