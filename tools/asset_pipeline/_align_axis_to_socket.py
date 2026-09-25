"""Align a long held asset's geometry with its grip socket's primary axis, rotating about the grip.

A weapon is held by SOCK_grip_primary, whose +X is the primary axis (toward the head). The sockets were
authored axis-aligned, but some reconstructions carry the concept camera's tilt in their vertices
(weapon_arming_sword: blade 12.1 deg off the grip axis), so the held weapon sits crooked in the hand.
This rotates the vertices (POSITION, NORMAL, TANGENT) and the socket positions by the minimal rotation
that takes the mesh's principal axis onto the grip's primary axis, about the grip point; the sockets'
own orientations are kept. Then the asset is re-grounded (lowest point y = 0) and re-centred on X/Z, as
the cleanup pass leaves every asset. Materials, textures, UVs and topology are untouched: only those
three accessors, their min/max, and the socket node translations change.

  python _align_axis_to_socket.py --input ready/<id>/<id>.glb --output _staging/orient/<id>/<id>.glb [--length 1.0]
--length restores the declared length along the grip axis by a uniform scale about the ground centre: the
cleanup pass scaled the TILTED box's longest side to the declared size, so once aligned the asset is long.
Writes <output stem>_axis_alignment.json beside the output.
"""
import argparse
import hashlib
import json
import os
import struct

import numpy as np

GRIP = "SOCK_grip_primary"


def read_glb(path):
    data = open(path, "rb").read()
    offset, gltf, binary = 12, None, b""
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        chunk = data[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            gltf = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = bytearray(chunk)
        offset += 8 + length
    return gltf, binary


def write_glb(path, gltf, binary):
    text = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    text += b" " * (-len(text) % 4)
    binary = bytes(binary) + b"\0" * (-len(binary) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<4sII", b"glTF", 2, total))
        handle.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
        handle.write(struct.pack("<II", len(binary), 0x004E4942) + binary)


def view(gltf, binary, index, width):
    accessor = gltf["accessors"][index]
    buffer_view = gltf["bufferViews"][accessor["bufferView"]]
    if buffer_view.get("byteStride", width * 4) != width * 4 or accessor["componentType"] != 5126:
        raise SystemExit(f"accessor {index} is interleaved or not float32; not handled")
    start = buffer_view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    return np.frombuffer(binary, dtype=np.float32, count=accessor["count"] * width, offset=start).reshape(-1, width), start


def quat_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def rotation_between(a, b):
    a, b = a / np.linalg.norm(a), b / np.linalg.norm(b)
    v, c = np.cross(a, b), float(a @ b)
    if np.linalg.norm(v) < 1e-9:
        return np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k * (1 / (1 + c))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--length", type=float, default=None, help="declared length along the grip axis, metres")
    args = parser.parse_args()

    gltf, binary = read_glb(args.input)
    nodes = gltf["nodes"]
    for node in nodes:
        if "mesh" in node and any(k in node for k in ("rotation", "scale", "matrix")):
            raise SystemExit(f"mesh node {node.get('name')} carries a transform; not handled")
    sockets = {n["name"]: n for n in nodes if n.get("name", "").startswith("SOCK_")}
    if GRIP not in sockets:
        raise SystemExit(f"no {GRIP}")
    grip = sockets[GRIP]
    primary = quat_matrix(grip.get("rotation", [0, 0, 0, 1]))[:, 0]
    pivot = np.array(grip.get("translation", [0, 0, 0]), dtype=float)

    positions = sorted({p["attributes"]["POSITION"] for m in gltf["meshes"] for p in m["primitives"]})
    points = np.vstack([view(gltf, binary, i, 3)[0] for i in positions]).astype(float)
    centred = points - points.mean(0)
    axis = np.linalg.eigh(centred.T @ centred)[1][:, -1]
    if axis @ primary < 0:
        axis = -axis
    before = float(np.degrees(np.arccos(min(1.0, abs(axis @ primary)))))
    rotation = rotation_between(axis, primary)

    done = set()
    for mesh in gltf["meshes"]:
        for primitive in mesh["primitives"]:
            for name, width in (("POSITION", 3), ("NORMAL", 3), ("TANGENT", 4)):
                index = primitive["attributes"].get(name)
                if index is None or index in done:
                    continue
                done.add(index)
                array, start = view(gltf, binary, index, width)
                values = array.astype(float)
                if name == "POSITION":
                    values = (values - pivot) @ rotation.T + pivot
                else:
                    values[:, :3] = values[:, :3] @ rotation.T
                binary[start:start + values.size * 4] = values.astype(np.float32).tobytes()
    for node in sockets.values():
        node["translation"] = list((np.array(node.get("translation", [0, 0, 0])) - pivot) @ rotation.T + pivot)

    moved = np.vstack([view(gltf, binary, i, 3)[0] for i in positions]).astype(float)
    low, high = moved.min(0), moved.max(0)
    shift = np.array([-(low[0] + high[0]) / 2, -low[1], -(low[2] + high[2]) / 2])
    extent = float(np.abs((high - low) @ np.abs(primary)))
    scale = args.length / extent if args.length else 1.0
    for index in positions:
        array, start = view(gltf, binary, index, 3)
        values = (array.astype(float) + shift) * scale
        binary[start:start + values.size * 4] = values.astype(np.float32).tobytes()
        gltf["accessors"][index]["min"] = [float(v) for v in values.min(0)]
        gltf["accessors"][index]["max"] = [float(v) for v in values.max(0)]
    for node in sockets.values():
        node["translation"] = [round(float(v), 6) for v in (np.array(node["translation"]) + shift) * scale]

    final = np.vstack([view(gltf, binary, i, 3)[0] for i in positions]).astype(float)
    check = np.linalg.eigh((final - final.mean(0)).T @ (final - final.mean(0)))[1][:, -1]
    after = float(np.degrees(np.arccos(min(1.0, abs(check @ primary)))))
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    write_glb(args.output, gltf, binary)
    record = {
        "input": args.input, "input_sha256": hashlib.sha256(open(args.input, "rb").read()).hexdigest(),
        "output": args.output, "output_sha256": hashlib.sha256(open(args.output, "rb").read()).hexdigest(),
        "grip_primary_axis": [round(float(v), 4) for v in primary],
        "principal_axis_before": [round(float(v), 4) for v in axis],
        "angle_to_grip_axis_deg": {"before": round(before, 3), "after": round(after, 3)},
        "rotation_matrix": [[round(float(v), 6) for v in row] for row in rotation],
        "pivot": [round(float(v), 5) for v in pivot], "reground_shift": [round(float(v), 5) for v in shift],
        "length_along_grip_axis": {"before_scale": round(extent, 4), "declared": args.length, "scale": round(scale, 6)},
        "size_after_xyz": [round(float(v), 4) for v in final.max(0) - final.min(0)],
        "sockets_after": {k: v["translation"] for k, v in sockets.items()},
    }
    stem = os.path.splitext(args.output)[0]
    with open(stem + "_axis_alignment.json", "w", encoding="utf-8") as handle:
        json.dump(record, handle, indent=2)
    print("ALIGN_RESULT " + json.dumps(record["angle_to_grip_axis_deg"]))


if __name__ == "__main__":
    main()
