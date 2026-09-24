"""Measure a GLB's true world-space bounds by composing its node transforms.

`_glb_bounds.py` reports the union of mesh *accessor* min/max in **local** space, with no reference to
node translation, rotation, scale or matrix. For a single-mesh asset that is correct, which is why it
has been reliable for props, weapons and creatures. For a multi-node asset it is not: an assembly of
28 pieces that are identical in shape and differ only by node translation reports roughly **one
piece's** extent, because every piece's accessor bounds overlap at the origin.

That is exactly the trap here. `forge_shed.glb` reports "3.000 x 2.600 x 3.000 m" from that tool,
while its own metadata says 6.0 x 6.318 x 4.974, and 3.0 x 2.6 is the size of a single wall piece.
The two numbers disagree because one of them is measuring the wrong thing.

This walks the node hierarchy from the scene roots, multiplies each node's local transform into its
parent's, transforms all eight corners of every mesh primitive's accessor bounds, and unions the
result. It reports both the composed world bounds and the naive local union, so the difference is
visible rather than assumed.

Usage:
    python _glb_world_bounds.py <file.glb> [more.glb ...]
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
        if kind == 0x4E4F534A:            # 'JSON'
            return json.loads(data[offset + 8:offset + 8 + length].decode("utf-8"))
        offset += 8 + length + ((4 - length % 4) % 4)
    raise ValueError(f"no JSON chunk in {path}")


def mat_identity():
    return [1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0]


def mat_mul(a, b):
    """Column-major 4x4 multiply, the order glTF stores matrices in."""
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

    rotation = [
        1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0.0,
        2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0.0,
        2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0.0,
        0.0, 0.0, 0.0, 1.0,
    ]
    for column, scale in enumerate((sx, sy, sz)):
        for row in range(3):
            rotation[column * 4 + row] *= scale
    rotation[12], rotation[13], rotation[14] = tx, ty, tz
    return rotation


def apply(matrix, point):
    x, y, z = point
    return (
        matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
        matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
        matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14],
    )


def bounds_of(path):
    gltf = read_gltf(path)
    nodes = gltf.get("nodes", [])
    meshes = gltf.get("meshes", [])
    accessors = gltf.get("accessors", [])

    world_low = [float("inf")] * 3
    world_high = [float("-inf")] * 3
    local_low = [float("inf")] * 3
    local_high = [float("-inf")] * 3
    placed = 0

    def visit(index, parent):
        nonlocal placed
        node = nodes[index]
        world = mat_mul(parent, mat_from_trs(node))
        if "mesh" in node:
            placed += 1
            for primitive in meshes[node["mesh"]].get("primitives", []):
                accessor = accessors[primitive["attributes"]["POSITION"]]
                low, high = accessor.get("min"), accessor.get("max")
                if not low or not high:
                    continue
                for axis in range(3):
                    local_low[axis] = min(local_low[axis], low[axis])
                    local_high[axis] = max(local_high[axis], high[axis])
                for corner in range(8):
                    point = (
                        high[0] if corner & 1 else low[0],
                        high[1] if corner & 2 else low[1],
                        high[2] if corner & 4 else low[2],
                    )
                    moved = apply(world, point)
                    for axis in range(3):
                        world_low[axis] = min(world_low[axis], moved[axis])
                        world_high[axis] = max(world_high[axis], moved[axis])
        for child in node.get("children", []):
            visit(child, world)

    for root in gltf["scenes"][gltf.get("scene", 0)]["nodes"]:
        visit(root, mat_identity())

    return {
        "nodes": len(nodes),
        "meshes_placed": placed,
        "world": [round(world_high[i] - world_low[i], 3) for i in range(3)],
        "world_longest": round(max(world_high[i] - world_low[i] for i in range(3)), 3),
        "local": [round(local_high[i] - local_low[i], 3) for i in range(3)],
    }


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    for path in sys.argv[1:]:
        result = bounds_of(path)
        name = path.replace("\\", "/").rsplit("/", 1)[-1]
        print(f"  {name}")
        print(f"     nodes placed      : {result['meshes_placed']} of {result['nodes']}")
        print(f"     world bounds XYZ  : {result['world']}   longest {result['world_longest']} m")
        print(f"     local union XYZ   : {result['local']}   <- what _glb_bounds.py reports")
    return 0


if __name__ == "__main__":
    sys.exit(main())
