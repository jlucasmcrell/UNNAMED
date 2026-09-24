"""Verify a character GLB's skeleton, skin and attachment sockets against its contract.

Written because the extended skeleton adds three things that are easy to get quietly wrong:
fingers that must deform, IK helpers that must NOT deform, and equipment sockets parented to
bones whose world position depends on Blender's bone-parenting offset. A socket that exports at
the origin looks fine in a node list and is useless in the engine.

Checks, in order:
  1. every core bone from the contract is present, at the contracted position
  2. finger bones are present and are skin joints
  3. IK helper bones are present and are NOT skin joints
  4. attachment sockets are present and resolve to the contracted world position
  5. the base sits on the ground, the mesh is centred, and height matches the contract

Usage:
    python _verify_character_skeleton.py --glb <path.glb> --contract <skeleton.json>
"""
import argparse
import json
import struct
import sys

JSON_CHUNK = 0x4E4F534A


def load_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, _version, length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        raise ValueError("not a GLB")
    offset, gltf, binary = 12, None, None
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + chunk_length]
        if chunk_type == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        else:
            binary = payload
        offset += chunk_length
    return gltf, binary


def mat_identity():
    return [1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0]


def mat_mul(a, b):
    """Column-major 4x4 multiply, matching glTF's matrix layout."""
    out = [0.0] * 16
    for col in range(4):
        for row in range(4):
            out[col * 4 + row] = sum(a[k * 4 + row] * b[col * 4 + k] for k in range(4))
    return out


def trs_matrix(node):
    if "matrix" in node:
        return list(node["matrix"])
    tx, ty, tz = node.get("translation", [0.0, 0.0, 0.0])
    qx, qy, qz, qw = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    sx, sy, sz = node.get("scale", [1.0, 1.0, 1.0])
    # Rotation as a column-major 3x3.
    r = [
        1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy + qz * qw), 2 * (qx * qz - qy * qw),
        2 * (qx * qy - qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz + qx * qw),
        2 * (qx * qz + qy * qw), 2 * (qy * qz - qx * qw), 1 - 2 * (qx * qx + qy * qy),
    ]
    m = mat_identity()
    for col in range(3):
        for row in range(3):
            m[col * 4 + row] = r[col * 3 + row] * (sx, sy, sz)[col]
    m[12], m[13], m[14] = tx, ty, tz
    return m


def node_world_positions(gltf):
    """World position of every node.

    Bone hierarchies carry rotation, and a child bone's translation is expressed in the rotated
    parent frame, so translations cannot simply be summed - doing that reports drift on exactly
    the rotated chains (arms) while unrotated ones (spine, legs) look correct. Compose the full
    TRS matrices instead.
    """
    nodes = gltf.get("nodes", [])
    children = {}
    for index, node in enumerate(nodes):
        for child in node.get("children", []):
            children[child] = index

    world_matrix = {}

    def resolve(index, seen):
        if index in world_matrix:
            return world_matrix[index]
        if index in seen:
            return mat_identity()
        seen = seen | {index}
        local = trs_matrix(nodes[index])
        parent = children.get(index)
        world_matrix[index] = local if parent is None else mat_mul(resolve(parent, seen), local)
        return world_matrix[index]

    positions = {}
    for index in range(len(nodes)):
        m = resolve(index, frozenset())
        positions[index] = (m[12], m[13], m[14])
    return positions


COMPONENT_FORMAT = {
    5120: ("b", 1),   # BYTE
    5121: ("B", 1),   # UNSIGNED_BYTE
    5122: ("h", 2),   # SHORT
    5123: ("H", 2),   # UNSIGNED_SHORT
    5125: ("I", 4),   # UNSIGNED_INT
    5126: ("f", 4),   # FLOAT
}
TYPE_COMPONENTS = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def accessor_values(gltf, binary, index):
    """Read an accessor out of the binary chunk, honouring its component type.

    JOINTS_0 is an integer accessor (usually UNSIGNED_BYTE or UNSIGNED_SHORT), not a float one.
    Reading it as float silently yields garbage and makes a correctly skinned mesh look like it
    has almost no weighted joints.
    """
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    fmt, size = COMPONENT_FORMAT[acc["componentType"]]
    components = TYPE_COMPONENTS[acc["type"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or components * size
    out = []
    for i in range(acc["count"]):
        base = start + i * stride
        out.append(struct.unpack_from("<" + fmt * components, binary, base))
    return out


def weighted_joint_names(gltf, binary):
    """Names of every joint that actually carries a non-zero skin weight.

    JOINTS_0 holds indices into the skin's `joints` array, which is itself a list of NODE
    indices. Using a JOINTS_0 value directly as a node index mis-aligns the mapping and reports
    weight on whichever bone happens to sit at that node position.
    """
    names = [n.get("name", "") for n in gltf.get("nodes", [])]
    used = set()
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            attrs = prim.get("attributes", {})
            if "JOINTS_0" not in attrs or "WEIGHTS_0" not in attrs:
                continue
            joint_rows = accessor_values(gltf, binary, attrs["JOINTS_0"])
            weight_rows = accessor_values(gltf, binary, attrs["WEIGHTS_0"])
            wacc = gltf["accessors"][attrs["WEIGHTS_0"]]
            scale = 1.0
            if wacc["componentType"] != 5126 and wacc.get("normalized"):
                scale = 1.0 / {5121: 255.0, 5123: 65535.0}.get(wacc["componentType"], 1.0)

            skins = gltf.get("skins", [])
            skin = skins[prim["skin"]] if "skin" in prim else (skins[0] if skins else None)
            if skin is None:
                continue
            joints = skin.get("joints", [])

            for joint_row, weight_row in zip(joint_rows, weight_rows):
                for slot, weight in zip(joint_row, weight_row):
                    if weight * scale <= 1e-5:
                        continue
                    slot = int(slot)
                    if slot >= len(joints):
                        continue
                    node_index = joints[slot]
                    if node_index < len(names):
                        used.add(names[node_index])
    return used


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--glb", required=True)
    parser.add_argument("--contract", default=None)
    parser.add_argument("--tolerance", type=float, default=0.012,
                        help="Metres. Socket and joint agreement tolerance.")
    parser.add_argument("--kind", choices=("reference", "character"), default="reference",
                        help="A 'reference' body is the block fit-figure: it has no finger or "
                             "foot geometry, so finger weights and a Y=0 base are not expected "
                             "and are reported as notes. A 'character' mesh must satisfy both.")
    args = parser.parse_args()

    gltf, binary = load_glb(args.glb)
    nodes = gltf.get("nodes", [])
    skins = gltf.get("skins", [])
    names = [n.get("name", "") for n in nodes]
    world = node_world_positions(gltf)

    joints = set()
    for skin in skins:
        joints.update(skin.get("joints", []))
    joint_names = {names[i] for i in joints if i < len(names)}
    weighted_names = weighted_joint_names(gltf, binary) if binary else set()

    index_of = {}
    for i, n in enumerate(names):
        index_of.setdefault(n, i)

    failures = []
    notes = []
    print(f"  glb      : {args.glb}")
    print(f"  nodes    : {len(nodes)}   skins: {len(skins)}   skin joints: {len(joints)}")

    if len(skins) != 1:
        failures.append(f"expected exactly 1 skin, found {len(skins)}")

    contract = None
    if args.contract:
        with open(args.contract, encoding="utf-8") as handle:
            contract = json.load(handle)

        core = [b for b in contract["bones"] if b.get("role") == "core"]
        fingers = [b for b in contract["bones"] if b.get("role") == "finger"]
        iks = [b for b in contract["bones"] if b.get("role") == "ik"]

        missing = [b["name"] for b in contract["bones"] if b["name"] not in index_of]
        if missing:
            failures.append(f"{len(missing)} contracted bones absent from the GLB: {missing[:6]}")

        # The GLB is Y-up while the contract records the export frame, so compare directly:
        # export_frame() already wrote Y-up coordinates.
        drift = []
        for bone in core:
            got = world.get(index_of.get(bone["name"], -1))
            if got is None:
                continue
            want = bone["head"]
            d = max(abs(got[i] - want[i]) for i in range(3))
            if d > args.tolerance:
                drift.append((bone["name"], [round(v, 4) for v in got], want, round(d, 4)))
        if drift:
            failures.append(f"{len(drift)} core bones drifted from the contract")
            for name, got, want, d in drift[:5]:
                print(f"    DRIFT {name}: got {got} want {want} (max {d})")

        # A bone in skins.joints can still be harmless: glTF lists every bone of the armature
        # as a joint, so membership proves nothing. What matters is whether the helper carries
        # any skin weight, because a joint with no non-zero weight deforms nothing.
        deforming = [b["name"] for b in iks if b["name"] in weighted_names]
        if deforming:
            failures.append(f"IK helper bones carry skin weight and will deform: {deforming}")

        deform_missing = [b["name"] for b in fingers if b["name"] not in weighted_names]
        if deform_missing and args.kind == "character":
            failures.append(f"finger bones carry no skin weight: {deform_missing[:6]}")

        # Sockets: does the exported world position match the contract?
        socket_fail = []
        for socket in contract.get("attachments", []):
            name = socket["name"]
            if name not in index_of:
                socket_fail.append((name, "absent", None, None))
                continue
            got = world[index_of[name]]
            want = socket["position"]
            d = max(abs(got[i] - want[i]) for i in range(3))
            if d > args.tolerance:
                socket_fail.append((name, "off", [round(v, 4) for v in got], round(d, 4)))
        if socket_fail:
            failures.append(f"{len(socket_fail)} attachment sockets wrong or absent")
            for name, why, got, d in socket_fail:
                print(f"    SOCKET {name}: {why} got {got} max_delta {d}")
        else:
            print(f"  sockets  : {len(contract.get('attachments', []))} present at contracted positions")

        print(f"  core     : {len(core)} present, {len(core) - len(drift)} within {args.tolerance} m")
        print(f"  fingers  : {len(fingers)} deform bones, "
              f"{len(fingers) - len(deform_missing)} carry skin weight")
        print(f"  ik       : {len(iks)} helper bones, "
              f"{len(iks) - len(deforming)} correctly weightless")
        print(f"  weighted : {len(weighted_names)} joints actually influence the mesh")

    # Geometry sanity: base on the ground, centred, plausible height.
    if "meshes" in gltf and gltf["meshes"]:
        accessors = gltf.get("accessors", [])
        lows = [1e9] * 3
        highs = [-1e9] * 3
        for mesh in gltf["meshes"]:
            for prim in mesh.get("primitives", []):
                pos_index = prim.get("attributes", {}).get("POSITION")
                if pos_index is None:
                    continue
                acc = accessors[pos_index]
                if "min" in acc and "max" in acc:
                    for i in range(3):
                        lows[i] = min(lows[i], acc["min"][i])
                        highs[i] = max(highs[i], acc["max"][i])
        if lows[0] < 1e8:
            height = highs[1] - lows[1]
            centre_x = (lows[0] + highs[0]) / 2.0
            centre_z = (lows[2] + highs[2]) / 2.0
            print(f"  geometry : height {height:.4f} m, base Y {lows[1]:.4f}, "
                  f"centre X {centre_x:.4f} Z {centre_z:.4f}")
            if abs(lows[1]) > 0.01:
                if args.kind == "character":
                    failures.append(f"base is not on the ground plane: Y={lows[1]:.4f}")
                else:
                    notes.append(
                        f"reference body's lowest vertex is at Y={lows[1]:.4f}, not 0. The block "
                        "fit-figure has no foot geometry below the ankle; a character mesh must "
                        "reach the ground.")
            if max(abs(centre_x), abs(centre_z)) > 0.02:
                failures.append(f"footprint not centred: X={centre_x:.4f} Z={centre_z:.4f}")
            if contract and abs(height - contract["height_m"]) > 0.12:
                failures.append(
                    f"height {height:.3f} m is not the contracted {contract['height_m']} m")

    for note in notes:
        print(f"  NOTE {note}")
    print()
    if failures:
        for f in failures:
            print(f"  FAIL {f}")
        print("  RESULT: NOT VERIFIED")
        return 1
    print("  RESULT: VERIFIED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
