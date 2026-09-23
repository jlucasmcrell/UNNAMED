"""Verify socket nodes survived export with the position and basis they were authored with.

Sockets are only useful if they arrive in the engine where they were declared. Blender's
Y-up export converts coordinates, and a wrong conversion produces sockets that look right in
Blender and are wrong in Godot - which is exactly the class of silent interface failure
Wave 0 exists to prevent.

Reads the socket definitions and the exported GLB, and compares each socket's node
translation and basis against the authored values in the export frame.

Usage:
    python _verify_sockets.py --sockets sockets\\name.json --glb out\\name.glb
"""
import argparse
import json
import math
import os
import struct
import sys


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    _, _, length = struct.unpack_from("<4sII", data, 0)
    offset, gltf = 12, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        if chunk_type == 0x4E4F534A:
            gltf = json.loads(data[offset:offset + chunk_length].decode("utf-8"))
        offset += chunk_length
    return gltf


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def normalise(vector):
    length = math.sqrt(sum(v * v for v in vector))
    return [v / length for v in vector] if length > 1e-12 else vector


def basis_from_definition(spec):
    """Mirror the authoring math so the check tests the definition, not the exporter."""
    primary = normalise(spec["primary"])
    secondary = normalise(spec.get("secondary", [0, 0, 1]))
    if abs(sum(p * s for p, s in zip(primary, secondary))) > 0.999:
        secondary = normalise([1, 0, 0])
        if abs(sum(p * s for p, s in zip(primary, secondary))) > 0.9:
            secondary = normalise([0, 1, 0])
    cross = normalise([
        primary[1] * secondary[2] - primary[2] * secondary[1],
        primary[2] * secondary[0] - primary[0] * secondary[2],
        primary[0] * secondary[1] - primary[1] * secondary[0],
    ])
    secondary = normalise([
        cross[1] * primary[2] - cross[2] * primary[1],
        cross[2] * primary[0] - cross[0] * primary[2],
        cross[0] * primary[1] - cross[1] * primary[0],
    ])
    roll = math.radians(float(spec.get("roll", 0.0) or 0.0))
    if roll:
        c, s = math.cos(roll), math.sin(roll)
        # Rodrigues about primary
        k = primary
        kx = [[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]]
        kk = [[k[i] * k[j] for j in range(3)] for i in range(3)]
        rot = [[c * (1 if i == j else 0) + s * kx[i][j] + (1 - c) * kk[i][j]
                for j in range(3)] for i in range(3)]
        secondary = normalise([sum(rot[i][j] * secondary[j] for j in range(3)) for i in range(3)])
        cross = normalise([
            primary[1] * secondary[2] - primary[2] * secondary[1],
            primary[2] * secondary[0] - primary[0] * secondary[2],
            primary[0] * secondary[1] - primary[1] * secondary[0],
        ])
    return primary, secondary, cross


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sockets", required=True)
    parser.add_argument("--glb", required=True)
    parser.add_argument("--position-tolerance", type=float, default=0.004)
    parser.add_argument("--angle-tolerance-deg", type=float, default=1.0)
    args = parser.parse_args()

    with open(args.sockets, encoding="utf-8") as handle:
        definition = json.load(handle)
    gltf = read_glb(args.glb)
    nodes = gltf.get("nodes", [])

    named = {node.get("name"): node for node in nodes}
    print(f"glb nodes    : {len(nodes)}")
    print(f"sockets decl : {sorted(definition.get('sockets', {}).keys())}")

    cos_tol = math.cos(math.radians(args.angle_tolerance_deg))
    failures = []
    for name, spec in definition.get("sockets", {}).items():
        node = named.get(name)
        if node is None:
            failures.append(f"{name}: MISSING from GLB")
            print(f"  {name:<22} MISSING")
            continue
        got_pos = node.get("translation", [0.0, 0.0, 0.0])
        want_pos = spec["position"]
        drift = math.dist(got_pos, want_pos)
        want_p, want_s, want_c = basis_from_definition(spec)
        # glTF stores a quaternion; recover the basis columns for comparison.
        quat = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
        x, y, z, w = quat
        got_basis = [
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
        ]
        got_p = normalise([got_basis[i][0] for i in range(3)])
        got_s = normalise([got_basis[i][1] for i in range(3)])
        axis_dot = sum(a * b for a, b in zip(got_p, want_p))
        roll_dot = sum(a * b for a, b in zip(got_s, want_s))
        pos_ok = drift <= args.position_tolerance
        axis_ok = axis_dot >= cos_tol
        roll_ok = roll_dot >= cos_tol
        status = "ok " if (pos_ok and axis_ok and roll_ok) else "BAD"
        print(f"  {status} {name:<22} pos drift {drift*1000:.1f} mm  "
              f"primary dot {axis_dot:+.4f}  secondary dot {roll_dot:+.4f}")
        if not (pos_ok and axis_ok and roll_ok):
            failures.append(name)

    print()
    if failures:
        print(f"RESULT: {len(failures)} socket(s) failed -> {failures}")
        return 1
    print(f"RESULT: all {len(definition.get('sockets', {}))} sockets verified in the GLB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
