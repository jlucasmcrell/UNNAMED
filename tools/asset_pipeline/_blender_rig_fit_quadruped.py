"""Fit the 18-bone quadruped skeleton to a creature's own mesh, in the mesh's measured body frame.

`_blender_rig.py` builds the quadruped from bounding-box fractions and assumes the body already lies
along glTF +Z. Creature meshes reconstructed from three-quarter concepts do not: the hound and the
boar face 45 and 54 degrees off that axis, so their legs swung sideways and the head, neck and one
foreleg chain carried no weight at all. This tool replaces that stage for the quadruped plan.

1. Yaw. The body axis is the principal axis of the mesh's footprint (area-weighted PCA of the face
   centroids projected on the ground). The head is the end whose outer slice carries its mass
   higher (a head is held up; a tail end has hind legs and a tail hanging low). The mesh data is
   rotated rigidly about the vertical through the origin so the head faces glTF +Z, the forward the
   game assumes. The rotation is recorded in <id>_orientation.json, and the rotated LOD0 is written
   beside the rig so the static and rigged copies face the same way. Only POSITION, NORMAL and
   TANGENT are rotated, in place in the GLB buffer; materials, UVs and textures are untouched.
2. Skeleton, fitted in that frame (Blender: +X = the creature's left, -Y = forward, +Z = up):
   paws from the bottom cluster of each leg; upper and lower leg joints along each leg column,
   traced slice by slice from the four separate leg blobs up to where they join the torso;
   hips/spine/chest on the body midline at the torso's centre height; neck and head along the
   front end's centreline to the nose, split where a slim skull meets a taller neck section; tail
   from the rump to its tip. Bone names, parents and local axes (roll) are read from the current
   rig, so the creature clip builder's per-bone rotations mean what they meant before.
3. Weights. Blender's automatic (bone heat) weights, solved on a welded copy (merge by distance)
   and transferred to the original vertices, so the shipped geometry is untouched. The generated
   LOD0s are double-walled (an outer skin plus an inward-facing inner skin a few millimetres in),
   which hides the bones from the outer skin; when the welded copy is detected as double-walled,
   bone heat is solved again on a watertight voxel envelope of the body and that result is used.
   The root stays at the ground as the rig origin and is not a deform bone.

Run inside Blender:
  blender --background --factory-startup --python _blender_rig_fit_quadruped.py -- --id <asset>
  blender --background --factory-startup --python _blender_rig_fit_quadruped.py -- --id <asset> --mode render

`fit` writes <id>.glb (yaw-normalised LOD0), <id>_orientation.json, <id>_rigged.glb and <id>_rig.json.
`render` (after `_blender_rig_fit_quadruped_clips.py` has rebuilt the clips) skins the mesh through
every staged clip exactly as the game does - tracks matched by bone name, rest-relative retarget,
linear blend skinning with the file's inverse binds - and renders bind-pose and clip frames at 1024 px
over a ground plane, with <id>_verify.json. Everything is written under assets/_staging/rigs/<id>/.

A replacement or regenerated mesh for an id: --source <lod0.glb> fits that mesh instead of
ready/<id>/<id>.glb, --reference-rig <rigged.glb> names the rig whose bone names, parents and rolls
are kept (default rigged/<id>/<id>_rigged.glb), --out-dir moves the outputs (fit and render alike),
and --concept records the concept the mesh was made from. --level-paws also turns the plane through
the four paw soles horizontal and re-grounds the mesh (--recentre puts its X/Z centre on the origin);
--also writes other static files (LODs, collision) with the same transform. --atlas-rule throat
splits head and neck where the throat falls away, for heads whose upright ears make a height jump
at the muzzle.

Either rule finds the jaw/throat junction, which is at eye level. By default (--occiput waist) the head
joint then moves back to the thinnest neck section behind that junction, the back of the skull, so the
cranium and ears weight to the head bone and turn with it; --occiput off keeps the junction.
Bone heat still blends head and neck across the skull, so after the weight solve the neck weight in
front of the occiput plane moves to the head (--rigid-skull-band, 0 to skip).
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(TOOL_DIR, "..", "..", "assets"))
STAGING = os.path.join(ASSETS, "_staging", "rigs")

QUADRUPED_BONES = [
    "root", "hips", "spine", "neck", "head", "tail",
    "front_upper.L", "front_lower.L", "front_paw.L", "front_upper.R", "front_lower.R", "front_paw.R",
    "rear_upper.L", "rear_lower.L", "rear_paw.L", "rear_upper.R", "rear_lower.R", "rear_paw.R",
]
LEGS = ("front.L", "front.R", "rear.L", "rear.R")

# Fitting constants, as fractions of mesh height H unless noted.
SAMPLES = 400_000          # area-weighted surface samples used for every slice measurement
CELL = 0.01                # slice raster cell for separating leg blobs
LEVEL_STEP = 0.01          # spacing of the horizontal slices
LEVEL_HALF = 0.006         # half-thickness of a horizontal slice
LEG_SEARCH_TOP = 0.60      # legs are looked for below this height
ANKLE = 0.06               # paw joint height above the leg's lowest point (the old rig's 0.06 H)
UPPER_RISE = 0.5           # shoulder/hip joint: this share of the way from the leg's torso entry to the torso centre
HEAD_JUMP = 1.8            # a slab this much taller than the one in front of it starts the neck
HEAD_MIN_EXTENT = 0.08     # ...once the head slabs are at least this tall (skips the nose tip)
NECK_SHARE = 0.30          # otherwise the atlas sits this share of the chest-to-nose path from the chest
THROAT_DROP = 0.03         # --atlas-rule throat: the neck starts where the underside falls this far below the jaw
JOINT_TOLERANCE = 0.06     # a deform joint must be inside the mesh or this close to its surface
OCCIPUT_SEARCH = 0.5       # the back of the skull is looked for from this share of the chest-to-junction line
OCCIPUT_STEP = 0.025       # ...in sections this share of the line apart
OCCIPUT_HALF = 0.006       # half-thickness of a neck section
OCCIPUT_GAP = 0.03         # a gap this wide splits a section into separate runs of surface
OCCIPUT_WAIST = 0.95       # a waist must be at least 5% thinner than the section at the junction
SKULL_ONLY = 0.95          # rigid skull: only vertices whose head + neck weight is at least this share


# --------------------------------------------------------------------------------------------------
# GLB access (numpy only, usable outside Blender)
# --------------------------------------------------------------------------------------------------
COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32,
             5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, _version, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:
        raise ValueError(f"not a GLB: {path}")
    offset, js, binary = 12, None, bytearray()
    while offset < length:
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        chunk = data[offset:offset + chunk_length]
        offset += chunk_length
        if chunk_type == 0x4E4F534A:
            js = json.loads(chunk.decode("utf-8"))
        elif chunk_type == 0x004E4942:
            binary = bytearray(chunk)
    return js, binary


def write_glb(path, js, binary):
    text = json.dumps(js, separators=(",", ":")).encode("utf-8")
    text += b" " * ((4 - len(text) % 4) % 4)
    binary = bytes(binary) + b"\0" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(text) + 8 + len(binary)
    with open(path, "wb") as handle:
        handle.write(struct.pack("<III", 0x46546C67, 2, total))
        handle.write(struct.pack("<II", len(text), 0x4E4F534A) + text)
        handle.write(struct.pack("<II", len(binary), 0x004E4942) + binary)


def _accessor_layout(js, index):
    accessor = js["accessors"][index]
    view = js["bufferViews"][accessor["bufferView"]]
    dtype = np.dtype(COMPONENT[accessor["componentType"]])
    width = WIDTH[accessor["type"]]
    stride = view.get("byteStride") or dtype.itemsize * width
    start = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    return accessor, dtype, width, stride, start


def read_accessor(js, binary, index):
    accessor, dtype, width, stride, start = _accessor_layout(js, index)
    count = accessor["count"]
    raw = np.frombuffer(bytes(binary), dtype=np.uint8, count=stride * (count - 1) + dtype.itemsize * width,
                        offset=start)
    rows = np.lib.stride_tricks.as_strided(raw, shape=(count, dtype.itemsize * width), strides=(stride, 1))
    values = np.ascontiguousarray(rows).view(dtype).reshape(count, width)
    if accessor.get("normalized") and dtype.kind in "ui":
        return values.astype(np.float64) / np.iinfo(dtype).max
    return values.astype(np.float64) if dtype.kind == "f" else values.astype(np.int64)


def write_accessor(js, binary, index, values):
    accessor, dtype, width, stride, start = _accessor_layout(js, index)
    values = np.asarray(values, dtype=dtype).reshape(accessor["count"], width)
    row_bytes = dtype.itemsize * width
    for row in range(accessor["count"]):
        at = start + row * stride
        binary[at:at + row_bytes] = values[row].tobytes()


def node_local(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    return trs_matrix(node.get("translation", [0, 0, 0]), node.get("rotation", [0, 0, 0, 1]),
                      node.get("scale", [1, 1, 1]))


def quat_matrix(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs_matrix(t, r, s):
    m = np.eye(4)
    m[:3, :3] = quat_matrix(np.asarray(r, dtype=np.float64)) * np.asarray(s, dtype=np.float64)[None, :]
    m[:3, 3] = t
    return m


def node_parents(js):
    parent = {}
    for index, node in enumerate(js["nodes"]):
        for child in node.get("children", []):
            parent[child] = index
    return parent


def world_matrices(js, locals_=None):
    parent = node_parents(js)
    locals_ = locals_ or [node_local(n) for n in js["nodes"]]
    cache = {}

    def world(index):
        if index not in cache:
            cache[index] = locals_[index] if index not in parent else world(parent[index]) @ locals_[index]
        return cache[index]
    return [world(i) for i in range(len(js["nodes"]))]


def mesh_arrays(js, binary, skinned=False):
    """Positions (world, glTF frame), triangles, and joints/weights when the mesh is skinned."""
    worlds = world_matrices(js)
    positions, triangles, joints, weights, base = [], [], [], [], 0
    for index, node in enumerate(js["nodes"]):
        if "mesh" not in node or (skinned and "skin" not in node):
            continue
        for primitive in js["meshes"][node["mesh"]]["primitives"]:
            p = read_accessor(js, binary, primitive["attributes"]["POSITION"])
            if "skin" not in node:
                p = p @ worlds[index][:3, :3].T + worlds[index][:3, 3]
            positions.append(p)
            if "indices" in primitive:
                triangles.append(read_accessor(js, binary, primitive["indices"]).reshape(-1, 3) + base)
            else:
                triangles.append(np.arange(len(p)).reshape(-1, 3) + base)
            if skinned:
                joints.append(read_accessor(js, binary, primitive["attributes"]["JOINTS_0"]))
                weights.append(read_accessor(js, binary, primitive["attributes"]["WEIGHTS_0"]))
            base += len(p)
    out = {"P": np.concatenate(positions), "F": np.concatenate(triangles)}
    if skinned:
        out["J"] = np.concatenate(joints)
        out["W"] = np.concatenate(weights)
    return out


def to_body(p):
    """glTF (X right, Y up, Z forward) to Blender (X, Y = -Z, Z = up)."""
    p = np.asarray(p, dtype=np.float64)
    return np.stack([p[..., 0], -p[..., 2], p[..., 1]], axis=-1)


def to_gltf(b):
    b = np.asarray(b, dtype=np.float64)
    return np.stack([b[..., 0], b[..., 2], -b[..., 1]], axis=-1)


GLTF_TO_BODY = np.array([[1.0, 0.0, 0.0], [0.0, 0.0, -1.0], [0.0, 1.0, 0.0]])   # to_body() as a matrix


def yaw_matrix_gltf(yaw):
    """Rotation about glTF +Y by `yaw` radians; the same turn as Blender's rotation about +Z."""
    c, s = math.cos(yaw), math.sin(yaw)
    return np.array([[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]])


# --------------------------------------------------------------------------------------------------
# Step 1: yaw normalisation
# --------------------------------------------------------------------------------------------------
def measure_orientation(P, F):
    """Body axis from the footprint, head end from the height of each end's mass."""
    B = to_body(P)
    tri = B[F]
    centroid = tri.mean(axis=1)
    area = 0.5 * np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
    weight = area / area.sum()
    mean = (centroid[:, :2] * weight[:, None]).sum(axis=0)
    offset = centroid[:, :2] - mean
    covariance = (offset * weight[:, None]).T @ offset
    eigenvalues, eigenvectors = np.linalg.eigh(covariance)
    axis = eigenvectors[:, 1]
    height = B[:, 2].max()

    ends = {}
    for sign in (1, -1):
        direction = axis * sign
        along = B[:, :2] @ direction
        span = along.max() - along.min()
        outer = along > along.max() - 0.15 * span
        across = B[outer, :2] @ np.array([-direction[1], direction[0]])
        ends[sign] = {
            "direction_body_xy": [round(float(v), 4) for v in direction],
            "direction_gltf_xz": [round(float(direction[0]), 4), round(float(-direction[1]), 4)],
            "vertices": int(outer.sum()),
            "mean_height_m": round(float(B[outer, 2].mean()), 4),
            "share_above_half_height": round(float((B[outer, 2] > 0.5 * height).mean()), 4),
            "width_m": round(float(np.ptp(across)), 4),
        }
    head_sign = 1 if ends[1]["mean_height_m"] >= ends[-1]["mean_height_m"] else -1
    return {
        "axis_body_xy": axis, "head_sign": head_sign, "ends": ends,
        "eigenvalues": eigenvalues, "height": float(height),
    }


def yaw_to_forward(head_direction_body_xy):
    """Turn about +Z (Blender) that brings the head direction onto -Y, the glTF +Z forward."""
    return -math.pi / 2 - math.atan2(head_direction_body_xy[1], head_direction_body_xy[0])


def rotate_glb(source, target, yaw, rotation=None, translation=None):
    """Copy a static GLB with its vertex data turned `yaw` radians about glTF +Y.

    POSITION, NORMAL and TANGENT are rewritten in place in the buffer; everything else - indices,
    UVs, materials, images, names - is byte-identical, so the textured asset is unchanged apart from
    the turn. Node transforms are required to be identity so the data turn is the whole-model turn.
    A full glTF `rotation` matrix (yaw and levelling together) replaces the yaw when given, and a
    `translation` (re-grounding) is added to POSITION after the rotation.
    """
    js, binary = read_glb(source)
    for node in js["nodes"]:
        local = node_local(node)
        if not np.allclose(local, np.eye(4), atol=1e-9):
            raise RuntimeError(f"{source}: node {node.get('name')} has a transform; rotate the node instead")
    rotation = yaw_matrix_gltf(yaw) if rotation is None else np.asarray(rotation, dtype=np.float64)
    shift = np.zeros(3) if translation is None else np.asarray(translation, dtype=np.float64)
    done = set()
    for mesh in js["meshes"]:
        for primitive in mesh["primitives"]:
            if primitive.get("targets"):
                raise RuntimeError(f"{source}: morph targets are not handled")
            for key in ("POSITION", "NORMAL", "TANGENT"):
                index = primitive["attributes"].get(key)
                if index is None or index in done:
                    continue
                done.add(index)
                values = read_accessor(js, binary, index)
                values[:, :3] = values[:, :3] @ rotation.T + (shift if key == "POSITION" else 0.0)
                write_accessor(js, binary, index, values)
                if key == "POSITION":
                    js["accessors"][index]["min"] = [float(v) for v in values.min(axis=0)]
                    js["accessors"][index]["max"] = [float(v) for v in values.max(axis=0)]
    write_glb(target, js, binary)
    return sorted(done)


# --------------------------------------------------------------------------------------------------
# Step 2: skeleton fit (numpy on the body-frame mesh)
# --------------------------------------------------------------------------------------------------
def surface_samples(V, F, count, seed=7):
    tri = V[F]
    area = 0.5 * np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
    rng = np.random.default_rng(seed)
    pick = rng.choice(len(F), size=count, p=area / area.sum())
    r1 = np.sqrt(rng.random(count))
    r2 = rng.random(count)
    return ((1 - r1)[:, None] * tri[pick, 0] + (r1 * (1 - r2))[:, None] * tri[pick, 1]
            + (r1 * r2)[:, None] * tri[pick, 2])


def blobs(xy, cell):
    """Connected blobs of a slice: raster the points, close one-cell gaps, flood-fill 8-connected."""
    if len(xy) == 0:
        return []
    low = xy.min(axis=0) - cell
    ij = np.floor((xy - low) / cell).astype(np.int64) + 2
    shape = tuple(ij.max(axis=0) + 3)
    occupied = np.zeros(shape, dtype=bool)
    occupied[ij[:, 0], ij[:, 1]] = True
    grown = occupied.copy()
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            grown |= np.roll(np.roll(occupied, dx, axis=0), dy, axis=1)
    label = np.zeros(shape, dtype=np.int64)
    count = 0
    for start in map(tuple, np.argwhere(grown)):
        if label[start]:
            continue
        count += 1
        label[start] = count
        stack = [start]
        while stack:
            i, j = stack.pop()
            for di in (-1, 0, 1):
                for dj in (-1, 0, 1):
                    ni, nj = i + di, j + dj
                    if grown[ni, nj] and not label[ni, nj]:
                        label[ni, nj] = count
                        stack.append((ni, nj))
    point_label = label[ij[:, 0], ij[:, 1]]
    out = []
    for value in range(1, count + 1):
        member = point_label == value
        if not member.any():
            continue
        pts = xy[member]
        out.append({"centroid": pts.mean(axis=0), "count": int(member.sum()),
                    "low": pts.min(axis=0), "high": pts.max(axis=0)})
    return out


def trace_legs(S, H):
    """Find the four leg columns and follow each from its paw up to where it joins the torso."""
    step, half, cell = LEVEL_STEP * H, LEVEL_HALF * H, CELL * H
    levels = np.arange(0.01 * H, LEG_SEARCH_TOP * H, step)
    slices = []
    for z in levels:
        member = np.abs(S[:, 2] - z) < half
        found = [b for b in blobs(S[member, :2], cell) if b["count"] >= 12]
        slices.append(found)

    def significant(found):
        if not found:
            return []
        biggest = max(b["count"] for b in found)
        return [b for b in found if b["count"] >= 0.08 * biggest]

    def plausible(four):
        order = sorted(four, key=lambda b: b["centroid"][1])
        front, rear = order[:2], order[2:]
        gap = np.mean([b["centroid"][1] for b in rear]) - np.mean([b["centroid"][1] for b in front])
        return gap > 0.15 * H

    good = [len(significant(s)) == 4 and plausible(significant(s)) for s in slices]
    runs, start = [], None
    for k, flag in enumerate(good + [False]):
        if flag and start is None:
            start = k
        if not flag and start is not None:
            runs.append((start, k - 1))
            start = None
    if not runs:
        raise RuntimeError("no height has exactly four separate leg blobs")
    first, last = max(runs, key=lambda r: r[1] - r[0])
    ref = (first + last) // 2
    four = sorted(significant(slices[ref]), key=lambda b: b["centroid"][1])
    named = {}
    for pair, prefix in ((four[:2], "front"), (four[2:], "rear")):
        left, right = sorted(pair, key=lambda b: -b["centroid"][0])
        named[f"{prefix}.L"], named[f"{prefix}.R"] = left, right

    reach = 4 * step
    paths = {leg: {ref: (named[leg]["centroid"], named[leg]["count"])} for leg in LEGS}
    exits = {}

    # Up: a leg ends where its blob joins another leg's or swells into the torso.
    active = set(LEGS)
    for k in range(ref + 1, len(levels)):
        claims = {}
        for leg in list(active):
            previous, _count = paths[leg][k - 1]
            candidates = [b for b in slices[k] if np.linalg.norm(b["centroid"] - previous) < reach]
            if not candidates:
                exits[leg] = k - 1
                active.discard(leg)
                continue
            best = min(candidates, key=lambda b: np.linalg.norm(b["centroid"] - previous))
            typical = np.median([c for _c, c in paths[leg].values()])
            if best["count"] > 2.5 * typical:
                exits[leg] = k - 1
                active.discard(leg)
                continue
            claims.setdefault(id(best), []).append((leg, best))
        for claimed in claims.values():
            if len(claimed) > 1:
                for leg, _b in claimed:
                    exits[leg] = k - 1
                    active.discard(leg)
            else:
                leg, best = claimed[0]
                paths[leg][k] = (best["centroid"], best["count"])
        if not active:
            break
    for leg in active:
        exits[leg] = max(paths[leg])

    # Down: follow each blob to the lowest slice it reaches.
    for leg in LEGS:
        for k in range(ref - 1, -1, -1):
            previous, _count = paths[leg][k + 1]
            candidates = [b for b in slices[k] if np.linalg.norm(b["centroid"] - previous) < reach]
            if not candidates:
                break
            best = min(candidates, key=lambda b: np.linalg.norm(b["centroid"] - previous))
            paths[leg][k] = (best["centroid"], best["count"])

    columns = {}
    for leg in LEGS:
        ks = sorted(paths[leg])
        columns[leg] = {
            "z": np.array([levels[k] for k in ks]),
            "xy": np.array([paths[leg][k][0] for k in ks]),
            "exit_z": float(levels[exits[leg]]),
            "exit_xy": paths[leg][exits[leg]][0],
            "lowest_xy": paths[leg][ks[0]][0],
            "lowest_level_z": float(levels[ks[0]]),
        }
    return columns, {"reference_height_m": float(levels[ref]),
                     "four_blob_band_m": [float(levels[first]), float(levels[last])]}


def column_at(column, z):
    """Leg centre at height z, interpolated along the traced column (clamped at its ends)."""
    return np.array([np.interp(z, column["z"], column["xy"][:, 0]),
                     np.interp(z, column["z"], column["xy"][:, 1])])


def torso_centre_height(S, y, x_mid, H):
    """Centre of the body's vertical extent in a thin slab on the midline at y."""
    member = (np.abs(S[:, 1] - y) < 0.015 * H) & (np.abs(S[:, 0] - x_mid) < 0.025 * H)
    z = S[member, 2]
    if len(z) < 20:
        raise RuntimeError(f"too few midline samples at y={y:.3f}")
    bottom, top = np.percentile(z, 2), np.percentile(z, 98)
    return float((bottom + top) / 2), float(bottom), float(top)


def torso_midline_x(S, y_front, y_rear, x_guess, H):
    """Body midline: median centre of the torso's width, measured across its middle band (clear of
    the belly and of any crest on the back) in slabs from the shoulders to the hips."""
    centres = []
    for y in np.linspace(y_front, y_rear, 9):
        centre, bottom, top = torso_centre_height(S, y, x_guess, H)
        member = (np.abs(S[:, 1] - y) < 0.015 * H) & (np.abs(S[:, 2] - centre) < 0.25 * (top - bottom))
        if member.sum() < 20:
            continue
        x = S[member, 0]
        centres.append((np.percentile(x, 3) + np.percentile(x, 97)) / 2)
    return float(np.median(centres)) if centres else x_guess


def occiput_from_waist(S, H, chest, junction, floor):
    """Move the head joint from the head/neck junction back to the back of the skull.

    The jump and throat rules find where the jaw meets the throat, which is at eye level: a head joint
    there leaves the cranium and the ears behind it, on the neck bone, and the skull bends between the
    eyes and the ears whenever the head turns. The skull ends where the neck is narrowest just behind
    it. Sections are cut square to the chest-to-junction line; each keeps only the connected run of
    surface through the neck (so a slice that also grazes the back or the jaw does not count), and
    its extent in the body's mid-plane is the neck's depth there. The waist is the thinnest section
    between OCCIPUT_SEARCH x the neck length and the junction. The joint goes to the centre of that
    section, raised to the junction's height when the section centre is lower (the head bone runs
    through the head's centre, not under it). No waist in front of the junction (a head that blends
    into a thick neck) leaves the junction as the joint. Returns (point or None, record)."""
    span = junction - chest
    length = float(np.linalg.norm(span))
    axis = span / length
    side = np.cross(axis, [1.0, 0.0, 0.0])
    side /= np.linalg.norm(side)
    sections = []
    for s in np.arange(OCCIPUT_SEARCH, 1.0 + 1e-9, OCCIPUT_STEP):
        centre = chest + s * length * axis
        member = (np.abs((S - centre) @ axis) < OCCIPUT_HALF * H) & (S[:, 2] > floor)
        if member.sum() < 20:
            continue
        P = S[member] - centre
        u = P @ side
        order = np.argsort(u)
        cuts = np.flatnonzero(np.diff(u[order]) > OCCIPUT_GAP * H)
        runs = np.split(order, cuts + 1)
        run = min(runs, key=lambda r: 0.0 if u[r].min() <= 0.0 <= u[r].max() else min(abs(u[r].min()), abs(u[r].max())))
        lo, hi = np.percentile(u[run], 2), np.percentile(u[run], 98)
        x_lo, x_hi = np.percentile(P[run, 0], 2), np.percentile(P[run, 0], 98)
        sections.append({"s": float(s), "depth": float(hi - lo),
                         "centre": centre + side * (lo + hi) / 2 + np.array([(x_lo + x_hi) / 2, 0.0, 0.0])})
    if len(sections) < 3:
        return None, {"rule": "occiput: too few neck sections", "sections": len(sections)}
    waist = min(sections, key=lambda sec: sec["depth"])
    at_junction = sections[-1]
    record = {"method": "thinnest neck section behind the head/neck junction, square to the chest-to-junction "
                        "line; joint at its centre, raised to the junction height",
              "waist_share_of_neck": round(waist["s"], 3), "waist_depth_m": round(waist["depth"], 4),
              "depth_at_junction_m": round(at_junction["depth"], 4),
              "depth_profile_m": [[round(sec["s"], 3), round(sec["depth"], 4)] for sec in sections]}
    if waist is at_junction or waist["depth"] > OCCIPUT_WAIST * at_junction["depth"]:
        record["rule"] = "occiput: no neck waist behind the junction; joint stays at the junction"
        return None, record
    point = waist["centre"].copy()
    point[2] = max(point[2], junction[2])
    record["rule"] = (f"occiput: neck waist {waist['depth']:.3f} m (junction {at_junction['depth']:.3f} m) at "
                      f"{waist['s']:.2f} of the neck; joint moved {np.linalg.norm(point - junction):.3f} m back")
    record["moved_m"] = round(float(np.linalg.norm(point - junction)), 4)
    return point, record


def fit_skeleton(V, F, H, atlas_mode="jump", occiput_mode="waist"):
    S = surface_samples(V, F, SAMPLES)
    columns, leg_info = trace_legs(S, H)

    exits = {leg: np.array([*columns[leg]["exit_xy"], columns[leg]["exit_z"]]) for leg in LEGS}
    y_front = float(np.mean([exits[leg][1] for leg in ("front.L", "front.R")]))
    y_rear = float(np.mean([exits[leg][1] for leg in ("rear.L", "rear.R")]))
    x_legs = float(np.mean([exits[leg][0] for leg in LEGS]))
    x_mid = torso_midline_x(S, y_front, y_rear, x_legs, H)
    x_mid = torso_midline_x(S, y_front, y_rear, x_mid, H)

    joints, tails, notes = {}, {}, {}
    torso = {}
    for label, y in (("rear", y_rear), ("mid", (y_front + y_rear) / 2), ("front", y_front)):
        torso[label] = torso_centre_height(S, y, x_mid, H)
    hips = np.array([x_mid, y_rear, torso["rear"][0]])
    spine = np.array([x_mid, (y_front + y_rear) / 2, torso["mid"][0]])
    chest = np.array([x_mid, y_front, torso["front"][0]])

    # Legs.
    for leg in LEGS:
        column = columns[leg]
        prefix, side = leg.split(".")
        # Shoulder/hip joint: on the leg column's line where it enters the torso, raised part of the way
        # to the torso centre. At the centre itself a thick body's whole flank (and even its back crest)
        # sits nearer the leg bone than the spine and swings with the leg.
        centre_z = torso["front"][0] if prefix == "front" else torso["rear"][0]
        exit_xy = column["exit_xy"]
        upper = np.array([exit_xy[0], exit_xy[1],
                          column["exit_z"] + UPPER_RISE * max(centre_z - column["exit_z"], 0.0)])

        near = np.linalg.norm(S[:, :2] - column["lowest_xy"], axis=1) < 0.08 * H
        band = near & (S[:, 2] < column["lowest_level_z"] + 0.03 * H)
        paw_pts = S[band]
        z_low = float(paw_pts[:, 2].min())
        ankle_z = z_low + ANKLE * H
        ankle = np.array([*column_at(column, ankle_z), ankle_z])
        sole = paw_pts[paw_pts[:, 2] < z_low + 0.03 * H]
        toe = np.array([float(np.median(sole[:, 0])), float(np.percentile(sole[:, 1], 5)) + 0.01 * H,
                        z_low + 0.012 * H])

        knee_z = min((upper[2] + ankle_z) / 2, column["exit_z"] - 0.5 * LEVEL_STEP * H)
        knee = np.array([*column_at(column, knee_z), knee_z])

        joints[f"{prefix}_upper.{side}"] = upper
        joints[f"{prefix}_lower.{side}"] = knee
        joints[f"{prefix}_paw.{side}"] = ankle
        tails[f"{prefix}_paw.{side}"] = toe
        notes[leg] = {"exit_height_m": round(column["exit_z"], 4), "ground_m": round(z_low, 4),
                      "traced_from_m": round(float(column["z"].min()), 4)}

    # Front end: centreline of y-slabs above the forelegs, from the nose back to the chest. Each slab's
    # centre is the middle of its vertical extent. The head is the run of slabs behind the nose whose
    # extent stays small; where the extent jumps (a slim skull meeting an upright neck) the atlas sits
    # at the back of that run. A head that blends into a thick neck has no jump, and the atlas is put
    # a fixed share of the chest-to-nose path in front of the chest instead.
    y_min = float(S[:, 1].min())
    nose_pts = S[S[:, 1] < y_min + 0.015 * H]
    nose = nose_pts.mean(axis=0)
    floor = float(np.mean([columns[leg]["exit_z"] for leg in ("front.L", "front.R")]))
    slabs, bottoms = [], []
    for y in np.arange(y_min + 0.01 * H, y_front, 0.023 * H):
        member = (np.abs(S[:, 1] - y) < 0.012 * H) & (S[:, 2] > floor)
        if member.sum() < 30:
            continue
        z = S[member, 2]
        low, high = np.percentile(z, 3), np.percentile(z, 97)
        slabs.append((np.array([float(np.median(S[member, 0])), y, float((low + high) / 2)]), float(high - low)))
        bottoms.append(float(low))
    centreline = np.array([nose] + [c for c, _h in slabs] + [chest])
    atlas, atlas_rule = None, None
    if atlas_mode == "throat":
        # A head with upright ears jumps in height where the muzzle meets the skull, well in front of
        # the atlas, so the jump rule would make the head bone the muzzle alone. Here the skull runs
        # back from the nose while the underside (jaw line) stays level; the neck starts where the
        # underside falls away towards the chest.
        jaw = None
        for k in range(1, len(slabs)):
            if slabs[k][1] < HEAD_MIN_EXTENT * H:
                continue
            if jaw is not None and bottoms[k] < jaw - THROAT_DROP * H:
                atlas = slabs[k - 1][0]
                atlas_rule = (f"throat: slab underside falls {jaw - bottoms[k]:.3f} m below the jaw line "
                              f"({jaw:.3f} m) at y={slabs[k][0][1]:.3f}; atlas at the slab in front")
                break
            jaw = bottoms[k] if jaw is None else min(jaw, bottoms[k])
    for k in range(1, len(slabs)) if atlas is None and atlas_mode == "jump" else ():
        previous, current = slabs[k - 1][1], slabs[k][1]
        if previous > HEAD_MIN_EXTENT * H and current > HEAD_JUMP * previous:
            atlas = slabs[k - 1][0]
            atlas_rule = (f"head/neck junction: slab vertical extent jumps {previous:.3f} -> {current:.3f} m "
                          f"at y={slabs[k][0][1]:.3f}")
            break
    if atlas is None:
        path = centreline[::-1]
        lengths = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(path, axis=0), axis=1))]
        target = NECK_SHARE * lengths[-1]
        atlas = np.array([np.interp(target, lengths, path[:, i]) for i in range(3)])
        atlas_rule = (f"no head/neck {'jump in slab extent' if atlas_mode == 'jump' else 'throat drop'}; "
                      f"atlas at {NECK_SHARE:.0%} of the chest-to-nose centreline from the chest")
    junction = atlas
    occiput_record = None
    if occiput_mode == "waist":
        moved, occiput_record = occiput_from_waist(S, H, chest, atlas, floor)
        if moved is not None:
            atlas = moved
            atlas_rule += "; " + occiput_record["rule"]
    head_dir = (nose - atlas) / np.linalg.norm(nose - atlas)
    joints["neck"] = chest
    joints["head"] = atlas
    tails["head"] = nose - head_dir * 0.01 * H

    # Tail: from the rump's rear surface at hip height to the rearmost cluster (the tail tip).
    tube = (np.abs(S[:, 0] - x_mid) < 0.03 * H) & (np.abs(S[:, 2] - hips[2]) < 0.03 * H) & (S[:, 1] > y_rear)
    rump_y = float(np.percentile(S[tube, 1], 98)) if tube.sum() > 10 else float(S[:, 1].max())
    tail_root = np.array([x_mid, rump_y - 0.025 * H, hips[2]])
    y_max = float(S[:, 1].max())
    tail_tip = S[S[:, 1] > y_max - 0.02 * H].mean(axis=0)
    if np.linalg.norm(tail_tip - tail_root) < 0.05 * H:
        tail_tip = tail_root + np.array([0.0, 0.05 * H, 0.0])
    joints["tail"] = tail_root
    tails["tail"] = tail_tip

    joints["hips"] = hips
    joints["spine"] = spine
    tails["spine"] = chest
    joints["root"] = np.array([x_mid, (y_front + y_rear) / 2, 0.0])
    tails["root"] = joints["root"] + np.array([0.0, 0.0, 0.1 * H])

    landmarks = {
        "midline_x_m": round(x_mid, 4), "leg_entry_mean_x_m": round(x_legs, 4), "shoulder_y_m": round(y_front, 4), "hip_y_m": round(y_rear, 4),
        "torso_centre_heights_m": {k: [round(v, 4) for v in t] for k, t in torso.items()},
        "front_end_floor_m": round(floor, 4), "atlas_rule": atlas_rule,
        "head_neck_junction_body": [round(float(v), 4) for v in junction],
        "occiput": occiput_record if occiput_record is not None else f"off (--occiput {occiput_mode})",
        "front_end_centreline_body": [[round(float(v), 4) for v in p] for p in centreline],
        "rump_rear_y_m": round(rump_y, 4), "legs": notes, **leg_info,
    }
    return joints, tails, landmarks


def paw_soles(V, F, H):
    """Body-frame sole point of each leg: the lowest surface under the leg column trace_legs finds."""
    S = surface_samples(V, F, SAMPLES)
    columns, _info = trace_legs(S, H)
    soles = {}
    for leg, column in columns.items():
        near = np.linalg.norm(S[:, :2] - column["lowest_xy"], axis=1) < 0.06 * H
        points = S[near & (S[:, 2] < column["lowest_level_z"] + 0.05 * H)]
        z = float(np.percentile(points[:, 2], 0.5))
        sole = points[points[:, 2] < z + 0.01 * H]
        soles[leg] = np.array([*sole[:, :2].mean(axis=0), z])
    return soles


def level_on_paws(P, F, yaw_matrix, recentre):
    """Level a mesh whose four paws stand on a tilted plane (Pixal3D reconstructs in the concept
    camera's frame, so a camera looking slightly down leaves the whole animal leaning).

    After the yaw, the plane through the four paw soles is turned horizontal about the ground-plane
    axis perpendicular to its slope, the yaw is measured again on the levelled mesh (the footprint
    moves a little when the lean comes out), and the mesh is re-grounded at min height 0 (with
    --recentre, its X/Z bounding-box centre also goes to the origin, as _blender_cleanup does).
    One rigid transform for the whole mesh; returns (glTF matrix, glTF translation, report).
    """
    def body(matrix):
        V = to_body(P @ matrix.T)
        V[:, 2] -= V[:, 2].min()          # the leg tracer measures heights from the ground
        return V

    V = body(yaw_matrix)
    H = float(np.ptp(V[:, 2]))
    soles = paw_soles(V, F, H)
    A = np.array([[p[0], p[1], 1.0] for p in soles.values()])
    b = np.array([p[2] for p in soles.values()])
    (ax, ay, c0), *_ = np.linalg.lstsq(A, b, rcond=None)
    normal = np.array([-ax, -ay, 1.0]) / np.linalg.norm([-ax, -ay, 1.0])
    axis = np.cross(normal, [0.0, 0.0, 1.0])
    sine, cosine = float(np.linalg.norm(axis)), float(normal[2])
    level_body = np.eye(3)
    if sine > 1e-9:
        k = axis / sine
        K = np.array([[0.0, -k[2], k[1]], [k[2], 0.0, -k[0]], [-k[1], k[0], 0.0]])
        level_body = np.eye(3) + sine * K + (1.0 - cosine) * K @ K
    matrix = GLTF_TO_BODY.T @ level_body @ GLTF_TO_BODY @ yaw_matrix

    again = measure_orientation(P @ matrix.T, F)
    direction = again["axis_body_xy"] if again["axis_body_xy"][1] < 0 else -again["axis_body_xy"]
    correction = yaw_to_forward(direction)
    matrix = yaw_matrix_gltf(correction) @ matrix

    moved = P @ matrix.T
    translation = np.array([0.0, -moved[:, 1].min(), 0.0])
    if recentre:
        translation[0] = -(moved[:, 0].min() + moved[:, 0].max()) / 2
        translation[2] = -(moved[:, 2].min() + moved[:, 2].max()) / 2
    V_after = to_body(moved + translation)
    soles_after = paw_soles(V_after, F, float(np.ptp(V_after[:, 2])))
    report = {
        "method": ("plane through the four paw soles (lowest surface under each traced leg column), turned "
                   "horizontal; yaw re-measured on the levelled mesh; re-grounded at min height 0"
                   + ("; X/Z bounding-box centre moved to the origin" if recentre else "")),
        "sole_plane_before": {
            "slope_forward_up_deg": round(math.degrees(math.atan(-ay)), 3),
            "slope_left_up_deg": round(math.degrees(math.atan(ax)), 3),
            "tilt_deg": round(math.degrees(math.acos(min(1.0, cosine))), 3),
            "residuals_m": {leg: round(float(r), 4) for leg, r in zip(soles, A @ [ax, ay, c0] - b)},
        },
        "yaw_correction_after_levelling_deg": round(math.degrees(correction), 3),
        "sole_heights_before_m": {leg: round(float(p[2]), 4) for leg, p in soles.items()},
        "sole_heights_after_m": {leg: round(float(p[2]), 4) for leg, p in soles_after.items()},
    }
    return matrix, translation, report


def bone_segments(joints, tails):
    """Head and tail of every bone: a chain bone ends at its chain child's head."""
    chain_child = {"hips": "spine", "neck": "head",
                   **{f"{p}_upper.{s}": f"{p}_lower.{s}" for p in ("front", "rear") for s in "LR"},
                   **{f"{p}_lower.{s}": f"{p}_paw.{s}" for p in ("front", "rear") for s in "LR"}}
    segments = {}
    for name in QUADRUPED_BONES:
        head = joints[name]
        tail = joints[chain_child[name]] if name in chain_child else tails[name]
        segments[name] = (np.asarray(head, dtype=np.float64), np.asarray(tail, dtype=np.float64))
    return segments


# --------------------------------------------------------------------------------------------------
# Envelope: the solid the mesh encloses, as voxels
# --------------------------------------------------------------------------------------------------
# The generated LOD0s are hollow: an outward skin and an inward-facing inner skin a few millimetres
# inside it, joined into one piece. A point in the body cavity is therefore "outside" by parity or
# winding number, and bone heat on the shell hands whole regions to the wrong bone because the inner
# skin hides every bone from the outer one. The envelope fills the cavity: surface voxels, sealed by
# a small dilation, flood-filled from outside; everything the flood cannot reach is body.
def _dilate6(grid):
    out = grid.copy()
    for axis in range(3):
        out |= np.roll(grid, 1, axis=axis)
        out |= np.roll(grid, -1, axis=axis)
    return out


def _erode6(grid):
    return ~_dilate6(~grid)


def envelope(V, F, H, probes, seal_steps=(1, 2, 3, 4)):
    """Voxel solid of the mesh envelope. `probes` are points that must come out inside (the torso
    midline); the seal grows until they do, so holes in the shell cannot let the flood into the cavity."""
    cell = CELL * H
    S = np.concatenate([surface_samples(V, F, SAMPLES, seed=11), V])
    pad = max(seal_steps) + 3
    low = S.min(axis=0) - pad * cell
    ijk = np.floor((S - low) / cell).astype(np.int64)
    shape = tuple(ijk.max(axis=0) + pad + 1)
    surface = np.zeros(shape, dtype=bool)
    surface[ijk[:, 0], ijk[:, 1], ijk[:, 2]] = True

    def index(points):
        idx = np.floor((np.atleast_2d(points) - low) / cell).astype(np.int64)
        return np.clip(idx, 0, np.array(shape) - 1)

    for seal in seal_steps:
        sealed = surface
        for _ in range(seal):
            sealed = _dilate6(sealed)
        empty = ~sealed
        outside = np.zeros(shape, dtype=bool)
        outside[0], outside[-1], outside[:, 0], outside[:, -1], outside[:, :, 0], outside[:, :, -1] = (True,) * 6
        outside &= empty
        while True:
            grown = _dilate6(outside) & empty
            if grown.sum() == outside.sum():
                break
            outside = grown
        solid = ~outside
        for _ in range(seal):
            solid = _erode6(solid)
        solid |= surface
        p = index(probes)
        if solid[p[:, 0], p[:, 1], p[:, 2]].all():
            break
    else:
        raise RuntimeError("could not seal the mesh envelope around the torso")

    def inside(points):
        q = index(points)
        return solid[q[:, 0], q[:, 1], q[:, 2]]
    return {"solid": solid, "low": low, "cell": cell, "seal_voxels": seal, "inside": inside,
            "voxels": int(solid.sum())}


def envelope_quads(env):
    """Boundary faces of the solid voxels as a closed quad mesh (vertices on the voxel corners)."""
    solid, low, cell = env["solid"], env["low"], env["cell"]
    padded = np.pad(solid, 1)
    quads = []
    offsets = {0: [(0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1)],
               1: [(0, 0, 0), (0, 0, 1), (1, 0, 1), (1, 0, 0)],
               2: [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)]}
    for axis in range(3):
        a = padded
        b = np.roll(padded, -1, axis=axis)
        for flip, mask in ((False, a & ~b), (True, ~a & b)):
            cells = np.argwhere(mask)
            base = cells.copy()
            base[:, axis] += 1
            face = np.stack([base + np.array(o) for o in offsets[axis]], axis=1)
            if flip:
                face = face[:, ::-1]
            quads.append(face)
    quads = np.concatenate(quads)
    flat = quads.reshape(-1, 3)
    unique, inverse = np.unique(flat, axis=0, return_inverse=True)
    verts = low + (unique - 1) * cell
    return verts, inverse.reshape(-1, 4)


# --------------------------------------------------------------------------------------------------
# Joint-to-mesh measurement (numpy; used for the current rig and the fitted one alike)
# --------------------------------------------------------------------------------------------------


def point_triangle_distance(p, V, F):
    """Exact distance from p to the nearest triangle (vectorised over triangles)."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    ab, ac, ap = b - a, c - a, p - a
    d1, d2 = np.einsum("ij,ij->i", ab, ap), np.einsum("ij,ij->i", ac, ap)
    bp = p - b
    d3, d4 = np.einsum("ij,ij->i", ab, bp), np.einsum("ij,ij->i", ac, bp)
    cp = p - c
    d5, d6 = np.einsum("ij,ij->i", ab, cp), np.einsum("ij,ij->i", ac, cp)
    va = d3 * d6 - d5 * d4
    vb = d5 * d2 - d1 * d6
    vc = d1 * d4 - d3 * d2
    denom = va + vb + vc
    denom = np.where(np.abs(denom) < 1e-20, 1e-20, denom)
    v = vb / denom
    w = vc / denom
    closest = a + ab * v[:, None] + ac * w[:, None]
    # Region tests (Ericson, Real-Time Collision Detection 5.1.5), applied in reverse priority.
    ab_edge = np.clip(d1 / np.where(np.abs(d1 - d3) < 1e-20, 1e-20, d1 - d3), 0, 1)
    ac_edge = np.clip(d2 / np.where(np.abs(d2 - d6) < 1e-20, 1e-20, d2 - d6), 0, 1)
    bc_num = d4 - d3
    bc_edge = np.clip(bc_num / np.where(np.abs(bc_num + d5 - d6) < 1e-20, 1e-20, bc_num + d5 - d6), 0, 1)
    mask = (va <= 0) & (bc_num >= 0) & ((d5 - d6) >= 0)
    closest[mask] = (b + (c - b) * bc_edge[:, None])[mask]
    mask = (vb <= 0) & (d2 >= 0) & (d6 <= 0)
    closest[mask] = (a + ac * ac_edge[:, None])[mask]
    mask = (vc <= 0) & (d1 >= 0) & (d3 <= 0)
    closest[mask] = (a + ab * ab_edge[:, None])[mask]
    mask = (d6 >= 0) & (d5 <= d6)
    closest[mask] = c[mask]
    mask = (d3 >= 0) & (d4 <= d3)
    closest[mask] = b[mask]
    mask = (d1 <= 0) & (d2 <= 0)
    closest[mask] = a[mask]
    return float(np.sqrt(np.min(np.sum((closest - p) ** 2, axis=1))))


def measure_joints(named_points, V, F, H, deform, env):
    """Per joint: inside the mesh envelope or not, distance to the surface and to the nearest vertex."""
    names = list(named_points)
    points = np.array([named_points[n] for n in names])
    enclosed = env["inside"](points)
    report = {}
    for name, point, inside in zip(names, points, enclosed):
        surface = point_triangle_distance(point, V, F)
        vertex = float(np.min(np.linalg.norm(V - point, axis=1)))
        inside = bool(inside)
        outside_by = 0.0 if inside else surface
        report[name] = {
            "position_gltf": [round(float(v), 4) for v in to_gltf(point)],
            "inside_mesh": inside,
            "surface_distance_m": round(surface, 4), "nearest_vertex_m": round(vertex, 4),
            "outside_by_m": round(outside_by, 4),
            "deform": name in deform,
            "within_tolerance": bool(outside_by <= JOINT_TOLERANCE * H),
        }
    return report


def current_rig(asset_id, path=None):
    """Names, parents, joint positions and local X axes of the rig the game ships today."""
    path = path or os.path.join(ASSETS, "rigged", asset_id, f"{asset_id}_rigged.glb")
    js, _binary = read_glb(path)
    skin = js["skins"][0]
    worlds = world_matrices(js)
    parent = node_parents(js)
    names = {i: js["nodes"][i]["name"] for i in skin["joints"]}
    out = {"path": path, "bones": {}}
    for index, name in names.items():
        p = parent.get(index)
        out["bones"][name] = {
            "parent": names.get(p) if p in names else None,
            "head_body": to_body(worlds[index][:3, 3]),
            # The importer keeps a node's frame as the bone frame, so the node's X is the bone's X.
            "x_axis_body": to_body(worlds[index][:3, 0] / np.linalg.norm(worlds[index][:3, 0])),
        }
    return out


# --------------------------------------------------------------------------------------------------
# Blender stages
# --------------------------------------------------------------------------------------------------
def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--id", required=True)
    parser.add_argument("--mode", choices=["fit", "render"], default="fit")
    parser.add_argument("--head", choices=["auto", "+", "-"], default="auto",
                        help="override which end of the footprint axis is the head")
    parser.add_argument("--merge-distance", type=float, default=0.0005,
                        help="weld distance for the heat-weighting copy, in metres")
    parser.add_argument("--weight-proxy", choices=["auto", "weld", "envelope"], default="auto",
                        help="auto: welded copy unless it is double-walled, then the envelope proxy")
    parser.add_argument("--concept-check", default=None,
                        help="the reviewer's note on the concept image, recorded in <id>_orientation.json")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--max-influences", type=int, default=4)
    parser.add_argument("--min-weight", type=float, default=0.02)
    # A replacement or regenerated mesh for an existing id: fit it in place of ready/<id>/<id>.glb,
    # keep the bone names, parents and rolls of the id's shipped rig, and write somewhere other
    # than _staging/rigs/<id>/.
    parser.add_argument("--source", default=None, help="LOD0 to fit (default ready/<id>/<id>.glb)")
    parser.add_argument("--reference-rig", default=None,
                        help="rig whose bone names, parents and rolls are kept (default rigged/<id>/<id>_rigged.glb)")
    parser.add_argument("--out-dir", default=None, help="output folder (default _staging/rigs/<id>)")
    parser.add_argument("--concept", default=None, help="concept image recorded in <id>_orientation.json")
    parser.add_argument("--level-paws", action="store_true",
                        help="also level the mesh on its four paw soles and re-ground it (see level_on_paws)")
    parser.add_argument("--recentre", action="store_true",
                        help="with --level-paws: put the X/Z bounding-box centre on the origin")
    parser.add_argument("--atlas-rule", choices=["jump", "throat"], default="jump",
                        help="head/neck split: 'jump' = where the slab height jumps (default); 'throat' = where the "
                             "underside falls away from the jaw line (heads with upright ears)")
    parser.add_argument("--occiput", choices=["waist", "off"], default="waist",
                        help="waist (default): move the head joint back from the head/neck junction to the "
                             "neck waist behind the skull (see occiput_from_waist); off: keep it at the junction "
                             "(rigs fitted before 2026-09-25)")
    parser.add_argument("--rigid-skull-band", type=float, default=0.03,
                        help="half-width (x mesh height) of the blend band about the occiput plane in which neck "
                             "weight moves to the head (see rigid_skull); 0 keeps the bone-heat weights "
                             "(rigs fitted before 2026-09-25)")
    parser.add_argument("--also", nargs="*", default=[],
                        help="static GLBs (LODs, collision) to write to the output folder with the same transform; "
                             "like the LOD0, each must have identity node transforms (cleanup's collision box does not)")
    return parser.parse_args(argv)


def output_dir(args):
    return os.path.abspath(args.out_dir) if args.out_dir else os.path.join(STAGING, args.id)


def blender_reset():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)


def blender_import(path):
    import bpy
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.context.scene.objects if o not in before]


def mesh_numpy(obj):
    """World-space vertices and triangles of a Blender mesh object."""
    mesh = obj.data
    mesh.calc_loop_triangles()
    V = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", V)
    V = V.reshape(-1, 3)
    M = np.array(obj.matrix_world)
    V = V @ M[:3, :3].T + M[:3, 3]
    F = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", F)
    return V, F.reshape(-1, 3)


def build_armature(asset_id, segments, parents, reference_x):
    import bpy
    from mathutils import Vector
    armature = bpy.data.armatures.new(f"{asset_id}_armature")
    rig = bpy.data.objects.new(f"{asset_id}_rig", armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for name in QUADRUPED_BONES:
        head, tail = segments[name]
        bone = armature.edit_bones.new(name)
        bone.head = Vector(head)
        bone.tail = Vector(tail)
        direction = Vector(tail - head).normalized()
        # Same local X as the current rig's bone, so the clip builder's Euler angles keep their meaning.
        x_axis = Vector(reference_x[name])
        x_axis = (x_axis - direction * x_axis.dot(direction)).normalized()
        bone.align_roll(x_axis.cross(direction))
        created[name] = bone
    for name in QUADRUPED_BONES:
        if parents[name]:
            created[name].parent = created[parents[name]]
            created[name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    axes = {}
    for bone in armature.bones:
        m = bone.matrix_local.to_3x3()
        axes[bone.name] = float(Vector(reference_x[bone.name]).normalized().dot(m.col[0]))
    armature.bones["root"].use_deform = False
    return rig, axes


def select_only(objects, active):
    import bpy
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active


def welded_copy(mesh, merge_distance):
    import bpy
    weld = mesh.copy()
    weld.data = mesh.data.copy()
    weld.name = f"{mesh.name}_weld"
    bpy.context.collection.objects.link(weld)
    for group in list(weld.vertex_groups):
        weld.vertex_groups.remove(group)
    select_only([weld], weld)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=merge_distance)
    bpy.ops.object.mode_set(mode="OBJECT")
    return weld


def ray_parity(obj, points):
    """Surface crossings along the six axis rays from each point. Odd = inside a closed skin; even
    counts from a point in the torso mean the skin is double-walled around a hollow."""
    import bpy
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    tree = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    counts = []
    for point in points:
        row = []
        for direction in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
            origin, d, n = Vector(point), Vector(direction), 0
            while True:
                location, _normal, _index, _distance = tree.ray_cast(origin, d)
                if location is None:
                    break
                n += 1
                origin = location + d * 1e-5
            row.append(n)
        counts.append(row)
    return counts


def envelope_proxy(env, name):
    """Closed, smooth stand-in for the body: the envelope's voxel faces, voxel-remeshed and relaxed."""
    import bpy
    verts, quads = envelope_quads(env)
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts.tolist(), [], quads.tolist())
    data.update()
    proxy = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(proxy)
    select_only([proxy], proxy)
    data.remesh_voxel_size = env["cell"] * 0.75
    bpy.ops.object.voxel_remesh()
    smooth = proxy.modifiers.new("relax", "SMOOTH")
    smooth.factor = 0.5
    smooth.iterations = 6
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    return proxy


def heat_on(proxy, rig):
    """Blender automatic weights (bone heat) on `proxy`; returns how many of its vertices got none."""
    import bpy
    select_only([proxy, rig], rig)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    deform = {b.name for b in rig.data.bones if b.use_deform}
    names = {g.index: g.name for g in proxy.vertex_groups}
    return sum(1 for v in proxy.data.vertices
               if not any(names.get(g.group) in deform and g.weight > 0 for g in v.groups))


def transfer_weights(source, mesh, mapping):
    import bpy
    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)
    for group in source.vertex_groups:
        mesh.vertex_groups.new(name=group.name)
    modifier = mesh.modifiers.new("weights_from_proxy", "DATA_TRANSFER")
    modifier.object = source
    modifier.use_vert_data = True
    modifier.data_types_verts = {"VGROUP_WEIGHTS"}
    modifier.vert_mapping = mapping
    modifier.layers_vgroup_select_src = "ALL"
    modifier.layers_vgroup_select_dst = "NAME"
    select_only([mesh], mesh)
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def weight_matrix(mesh, bones):
    column = {name: k for k, name in enumerate(bones)}
    group_column = {g.index: column[g.name] for g in mesh.vertex_groups if g.name in column}
    W = np.zeros((len(mesh.data.vertices), len(bones)))
    for vertex in mesh.data.vertices:
        for g in vertex.groups:
            if g.group in group_column:
                W[vertex.index, group_column[g.group]] = g.weight
    return W


def segment_distances(P, segments, bones):
    out = []
    for name in bones:
        head, tail = segments[name]
        span = tail - head
        t = np.clip(((P - head) @ span) / max(span @ span, 1e-12), 0.0, 1.0)
        out.append(np.linalg.norm(P - (head + np.outer(t, span)), axis=1))
    return np.stack(out, axis=1)


def locality(mesh, segments, H):
    """Share of weighted vertices whose dominant bone is more than 0.1 H further away than the nearest
    bone: a measure of weights landing on the wrong body part."""
    bones = [n for n in QUADRUPED_BONES if n != "root"]
    V, _F = mesh_numpy(mesh)
    W = weight_matrix(mesh, bones)
    D = segment_distances(V, segments, bones)
    weighted = W.sum(axis=1) > 0
    dominant = W.argmax(axis=1)
    excess = D[np.arange(len(V)), dominant] - D.min(axis=1)
    far = weighted & (excess > 0.1 * H)
    return {"vertices_on_a_far_bone": int(far.sum()),
            "share_on_a_far_bone": round(float(far.sum() / max(weighted.sum(), 1)), 4),
            "dominant_is_nearest_share": round(float((dominant == D.argmin(axis=1))[weighted].mean()), 4)}


def heat_weights(mesh, rig, merge_distance, env, probes, segments, H, proxy_mode):
    """Bone heat on the welded copy first. When that copy is a double-walled shell (even crossings from
    the torso midline) its heat weights cannot see the bones from the outer skin, so bone heat is solved
    again on the watertight envelope proxy and those weights are used. Either way the weights reach the
    original vertices by transfer, so the shipped geometry is untouched."""
    import bpy
    weld = welded_copy(mesh, merge_distance)
    parity = ray_parity(weld, probes)
    even = sum(1 for row in parity for n in row if n % 2 == 0)
    hollow = even > sum(len(row) for row in parity) / 2
    record = {"welded_vertices": len(weld.data.vertices), "original_vertices": len(mesh.data.vertices),
              "merge_distance_m": merge_distance,
              "torso_ray_crossings_on_weld": parity, "weld_is_double_walled": hollow}

    record["weld_heat_unweighted"] = heat_on(weld, rig)
    transfer_weights(weld, mesh, "NEAREST")
    record["weld_heat_locality"] = locality(mesh, segments, H)
    use_envelope = proxy_mode == "envelope" or (proxy_mode == "auto" and hollow)
    if use_envelope:
        proxy = envelope_proxy(env, f"{mesh.name}_envelope")
        record["envelope_proxy_vertices"] = len(proxy.data.vertices)
        record["envelope_seal_voxels"] = env["seal_voxels"]
        record["envelope_voxel_m"] = round(env["cell"], 5)
        record["envelope_heat_unweighted"] = heat_on(proxy, rig)
        transfer_weights(proxy, mesh, "POLYINTERP_NEAREST")
        record["envelope_heat_locality"] = locality(mesh, segments, H)
        bpy.data.objects.remove(proxy, do_unlink=True)
        record["source"] = "envelope proxy"
        record["reason"] = ("welded copy is double-walled (outer skin plus an inward-facing inner skin); "
                            "bone heat on it put {} vertices on a bone more than 0.1 H further than the "
                            "nearest one".format(record["weld_heat_locality"]["vertices_on_a_far_bone"]))
    else:
        record["source"] = "welded copy"
    bpy.data.objects.remove(weld, do_unlink=True)

    mesh.parent = rig
    armature_modifier = mesh.modifiers.new("Armature", "ARMATURE")
    armature_modifier.object = rig
    return record


def clean_weights(mesh, max_influences, min_weight):
    import bpy
    select_only([mesh], mesh)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=min_weight)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=max_influences)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def rigid_skull(mesh, segments, H, band):
    """The skull is one bone: in front of the occiput nothing may bend. Bone heat blends the head and
    neck weights over a wide zone around the head joint, which leaves the cranium and the ears partly
    on the neck. The occiput plane passes through the head joint, square to the bisector of the neck
    and head bone directions. Neck (the head's parent) weight on vertices in front of it moves to the
    head; across a band of +-band x H about the plane only a smoothstep share of it moves, so the neck
    still bends behind the skull. Only vertices on the head and neck alone (head + neck at least
    SKULL_ONLY of the weight) change: skin that also carries spine or leg weight is shoulder or mane,
    which bends with the body. Other bones' weights are left alone, and each vertex keeps its sum."""
    import bpy
    atlas = segments["head"][0]
    neck_dir = atlas - segments["neck"][0]
    head_dir = segments["head"][1] - atlas
    normal = neck_dir / np.linalg.norm(neck_dir) + head_dir / np.linalg.norm(head_dir)
    normal /= np.linalg.norm(normal)
    V, _F = mesh_numpy(mesh)
    d = (V - atlas) @ normal
    t = np.clip((d + band * H) / (2 * band * H), 0.0, 1.0)
    share = t * t * (3 - 2 * t)
    head = mesh.vertex_groups.get("head") or mesh.vertex_groups.new(name="head")
    neck = mesh.vertex_groups.get("neck")
    moved, whole, total = 0, 0, 0.0
    if neck is not None:
        for index in np.flatnonzero(share > 0):
            vertex = mesh.data.vertices[int(index)]
            w_neck = next((g.weight for g in vertex.groups if g.group == neck.index), 0.0)
            if w_neck <= 0:
                continue
            w_head = next((g.weight for g in vertex.groups if g.group == head.index), 0.0)
            if w_neck + w_head < SKULL_ONLY * sum(g.weight for g in vertex.groups):
                continue
            shift = w_neck * float(share[index])
            neck.add([int(index)], w_neck - shift, "REPLACE")
            head.add([int(index)], w_head + shift, "REPLACE")
            if w_neck - shift <= 1e-6:
                neck.remove([int(index)])
                whole += 1
            moved += 1
            total += shift
    return {"method": "occiput plane through the head joint, square to the bisector of the neck and head bones; "
                      "neck weight in front of it moves to the head, smoothstep across the band, on vertices "
                      f"weighted at least {SKULL_ONLY:.0%} to head and neck",
            "band_m": round(band * H, 4), "plane_normal_gltf": to_gltf(normal),
            "vertices_changed": moved, "vertices_now_without_neck": whole,
            "weight_moved_total": round(total, 3)}


def weight_stats(mesh, rig):
    deform = [b.name for b in rig.data.bones if b.use_deform]
    index_to_name = {g.index: g.name for g in mesh.vertex_groups}
    dominated = {name: 0 for name in deform}
    unweighted, most = 0, 0
    for vertex in mesh.data.vertices:
        groups = [(index_to_name[g.group], g.weight) for g in vertex.groups
                  if index_to_name.get(g.group) in dominated and g.weight > 0]
        if not groups:
            unweighted += 1
            continue
        most = max(most, len(groups))
        dominated[max(groups, key=lambda item: item[1])[0]] += 1
    return {"bones": len(rig.data.bones), "deform_bones": len(deform),
            "bones_with_weights": sum(1 for g in mesh.vertex_groups if g.name in dominated),
            "vertices": len(mesh.data.vertices), "unweighted_vertices": unweighted,
            "unweighted_percent": round(100.0 * unweighted / max(len(mesh.data.vertices), 1), 2),
            "max_influences": most, "dominated_vertices": dominated}


def export_rigged(rig, mesh, path):
    import bpy
    select_only([rig, mesh], rig)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=False,
        export_yup=True, export_normals=True, export_materials="EXPORT", export_texcoords=True,
        export_skins=True)


def json_ready(value):
    if isinstance(value, dict):
        return {k: json_ready(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [json_ready(v) for v in value]
    if isinstance(value, np.ndarray):
        return [json_ready(v) for v in value.tolist()]
    if isinstance(value, (np.floating,)):
        return round(float(value), 5)
    if isinstance(value, (np.integer,)):
        return int(value)
    if isinstance(value, float):
        return round(value, 5)
    return value


def write_json(path, data):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(json_ready(data), handle, indent=2)


def run_fit(args):
    import bpy
    asset_id = args.id
    out_dir = output_dir(args)
    os.makedirs(out_dir, exist_ok=True)
    ready_path = os.path.abspath(args.source) if args.source else os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")

    # 1. Orientation, measured on the shipped LOD0.
    js, binary = read_glb(ready_path)
    lod0 = mesh_arrays(js, binary)
    measured = measure_orientation(lod0["P"], lod0["F"])
    head_sign = measured["head_sign"] if args.head == "auto" else (1 if args.head == "+" else -1)
    head_direction = measured["axis_body_xy"] * head_sign
    yaw = yaw_to_forward(head_direction)
    matrix, translation, levelling = yaw_matrix_gltf(yaw), np.zeros(3), None
    if args.level_paws:
        matrix, translation, levelling = level_on_paws(lod0["P"], lod0["F"], matrix, args.recentre)
        yaw = math.atan2(matrix[0, 2], matrix[0, 0])
    rotated_path = os.path.join(out_dir, f"{asset_id}.glb")
    rotated_accessors = rotate_glb(ready_path, rotated_path, yaw, matrix, translation)
    also = {}
    for extra in args.also:
        target = os.path.join(out_dir, os.path.basename(extra))
        also[os.path.abspath(extra)] = {"output": target, "accessors": rotate_glb(extra, target, yaw, matrix, translation)}

    check_js, check_binary = read_glb(rotated_path)
    rotated = mesh_arrays(check_js, check_binary)
    expected = lod0["P"] @ matrix.T + translation
    rotation_error = float(np.abs(rotated["P"] - expected).max())
    same_topology = bool(np.array_equal(rotated["F"], lod0["F"]))
    concept = os.path.abspath(args.concept) if args.concept else os.path.join(ASSETS, "concepts", f"{asset_id}.png")
    yaw_deg = math.degrees(yaw)
    orientation = {
        "asset": asset_id,
        "source": ready_path,
        "output": rotated_path,
        "method": ("yaw only, about the vertical through the origin. Body axis = principal axis of the "
                   "footprint (area-weighted PCA of face centroids on the ground plane). Head end = the "
                   "end whose outer 15% of the axis has the higher mean vertex height."),
        "head_end_rule": "auto" if args.head == "auto" else f"override {args.head}",
        "footprint_axis_gltf_xz": [round(float(measured["axis_body_xy"][0]), 5),
                                    round(float(-measured["axis_body_xy"][1]), 5)],
        "footprint_eigenvalues": [round(float(v), 6) for v in measured["eigenvalues"]],
        "footprint_aspect": round(float(math.sqrt(measured["eigenvalues"][1] / measured["eigenvalues"][0])), 3),
        "ends": {("+axis" if s == 1 else "-axis"): e for s, e in measured["ends"].items()},
        "head_direction_before_gltf_xz": [round(float(head_direction[0]), 5), round(float(-head_direction[1]), 5)],
        "head_heading_before_deg": round(math.degrees(math.atan2(head_direction[0], -head_direction[1])), 2),
        "rotation": {
            "axis": "glTF +Y (up); equals Blender +Z", "pivot": [0.0, 0.0, 0.0],
            "angle_deg": round(yaw_deg, 4),
            "quaternion_xyzw_gltf": [0.0, round(math.sin(yaw / 2), 8), 0.0, round(math.cos(yaw / 2), 8)],
            "matrix_gltf": [[round(float(v), 8) for v in row] for row in yaw_matrix_gltf(yaw)],
            "applies_to": "vertex data (POSITION, NORMAL, TANGENT); v_new = matrix @ v_old",
        },
        "accessors_rotated": rotated_accessors,
        "check": {"max_position_error_m": rotation_error, "indices_unchanged": same_topology},
        **({"levelling": levelling, "transform_gltf": {
            "matrix": [[round(float(v), 8) for v in row] for row in matrix],
            "translation_m": [round(float(v), 6) for v in translation],
            "applies_to": "vertex data; POSITION_new = matrix @ POSITION_old + translation, NORMAL/TANGENT_new = "
                          "matrix @ old. This (not the yaw-only 'rotation' above) is the whole change."}}
           if levelling else {}),
        **({"also_transformed": also} if also else {}),
        "bbox_before_gltf": [np.round(lod0["P"].min(0), 4).tolist(), np.round(lod0["P"].max(0), 4).tolist()],
        "bbox_after_gltf": [np.round(rotated["P"].min(0), 4).tolist(), np.round(rotated["P"].max(0), 4).tolist()],
        "footprint_centre_after_gltf_xz": [round(float((rotated["P"][:, 0].min() + rotated["P"][:, 0].max()) / 2), 4),
                                           round(float((rotated["P"][:, 2].min() + rotated["P"][:, 2].max()) / 2), 4)],
        "concept": concept if os.path.exists(concept) else None,
        "concept_check": args.concept_check or "not recorded",
    }
    print(f"  yaw {yaw_deg:.2f} deg, head end {measured['ends'][head_sign]}")

    # 2. Import the rotated LOD0 and fit the skeleton in its frame.
    blender_reset()
    meshes = [o for o in blender_import(rotated_path) if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"expected one mesh object, got {len(meshes)}")
    mesh = meshes[0]
    mesh.name = f"{asset_id}_rigged"
    mesh.data.name = f"{asset_id}_rigged_mesh"
    for material in mesh.data.materials:
        if material is not None:
            material.name = f"MAT_{asset_id}_rigged"
    V, F = mesh_numpy(mesh)
    H = float(V[:, 2].max() - V[:, 2].min())
    joints, tails, landmarks = fit_skeleton(V, F, H, args.atlas_rule, args.occiput)

    reference = current_rig(asset_id, args.reference_rig)
    names = set(reference["bones"])
    if names != set(QUADRUPED_BONES):
        raise RuntimeError(f"current rig bones differ from the quadruped plan: {sorted(names ^ set(QUADRUPED_BONES))}")
    parents = {n: reference["bones"][n]["parent"] for n in QUADRUPED_BONES}
    reference_x = {n: reference["bones"][n]["x_axis_body"] for n in QUADRUPED_BONES}
    segments = bone_segments(joints, tails)

    # Cross-checks in the turned frame: the nose-to-tail-tip line should run along -Y, and the left and
    # right legs should pair up across the body (a staggered stance shows up as a heading offset here).
    nose_tail = tails["tail"] - tails["head"]
    orientation["cross_checks"] = {
        "head_forward": bool(tails["head"][1] < tails["tail"][1]),
        "nose_to_tail_tip_offset_deg": round(math.degrees(math.atan2(nose_tail[0], nose_tail[1])), 2),
        "front_pair_dy_m": round(float(joints["front_upper.L"][1] - joints["front_upper.R"][1]), 4),
        "rear_pair_dy_m": round(float(joints["rear_upper.L"][1] - joints["rear_upper.R"][1]), 4),
        "paw_pairs_heading_offset_deg": round(math.degrees(math.atan2(
            (joints["front_paw.L"][1] + joints["rear_paw.L"][1]) - (joints["front_paw.R"][1] + joints["rear_paw.R"][1]),
            (joints["front_paw.L"][0] + joints["rear_paw.L"][0]) - (joints["front_paw.R"][0] + joints["rear_paw.R"][0]))), 2),
    }
    write_json(os.path.join(out_dir, f"{asset_id}_orientation.json"), orientation)

    # 3. Armature, weights, export.
    probes = [segments["hips"][0], segments["spine"][0], segments["neck"][0]]
    env = envelope(V, F, H, np.array(probes))
    rig, roll_agreement = build_armature(asset_id, segments, parents, reference_x)
    weighting = heat_weights(mesh, rig, args.merge_distance, env, probes, segments, H, args.weight_proxy)
    unweighted_on_source = weighting.get("envelope_heat_unweighted", weighting["weld_heat_unweighted"]) \
        if weighting["source"] == "envelope proxy" else weighting["weld_heat_unweighted"]
    method = "heat-envelope" if weighting["source"] == "envelope proxy" else "heat-welded"
    if unweighted_on_source > 0:
        method += " (partial)"
    clean_weights(mesh, args.max_influences, args.min_weight)
    if args.rigid_skull_band > 0:
        weighting["rigid_skull"] = rigid_skull(mesh, segments, H, args.rigid_skull_band)
    stats = weight_stats(mesh, rig)
    weighting["final_locality"] = locality(mesh, segments, H)
    rigged_path = os.path.join(out_dir, f"{asset_id}_rigged.glb")
    export_rigged(rig, mesh, rigged_path)

    # 4. Reports: fitted joints against the mesh, and the current rig measured the same way. The current
    # rig was built on the same geometry before the turn, so its joints are turned into this frame.
    deform = {n for n in QUADRUPED_BONES if n != "root"}
    heads = {n: segments[n][0] for n in QUADRUPED_BONES}
    leaf_tails = {f"{n}:tip": segments[n][1] for n in ("head", "tail") + tuple(
        f"{p}_paw.{s}" for p in ("front", "rear") for s in "LR")}
    fitted = measure_joints({**heads, **leaf_tails}, V, F, H, deform | set(leaf_tails), env)
    turn = GLTF_TO_BODY @ matrix @ GLTF_TO_BODY.T
    shift = GLTF_TO_BODY @ translation
    before = measure_joints({n: turn @ b["head_body"] + shift for n, b in reference["bones"].items()},
                            V, F, H, deform, env)

    def worst(report, key):
        rows = [(n, r[key]) for n, r in report.items() if r["deform"]]
        return max(rows, key=lambda item: item[1])

    limb_bones = [n for n in QUADRUPED_BONES if n not in ("root", "hips", "spine")]
    report = {
        "asset": asset_id,
        "plan": "quadruped",
        "method": method,
        "out": rigged_path,
        **{k: v for k, v in stats.items() if k != "dominated_vertices"},
        "mesh_height_m": round(H, 4),
        "tool": os.path.basename(__file__),
        "frame": "body frame = glTF with the head toward +Z; see <id>_orientation.json",
        "weighting": {
            "method": method,
            "detail": ("Blender automatic (bone heat) weights. Solved first on a copy welded by distance and "
                       "copied to the original vertices by nearest vertex; when that copy is double-walled, "
                       "solved again on a watertight envelope proxy (voxel envelope of the mesh with its "
                       "cavity filled, voxel-remeshed and relaxed) and copied by nearest-face interpolation. "
                       f"Then normalised, cleaned below {args.min_weight}, limited to {args.max_influences} "
                       "influences, normalised. No distance weighting is used."),
            **weighting,
            "root_deform": False,
            "root_note": "root is the ground-level rig origin; it is not a deform bone so bone heat cannot "
                         "hand belly vertices to a bone the clips never move.",
        },
        "dominated_vertices": stats["dominated_vertices"],
        "limb_neck_head_bones_with_zero_dominance": [n for n in limb_bones if stats["dominated_vertices"].get(n, 0) == 0],
        "roll_agreement_with_current_rig": {k: round(v, 4) for k, v in roll_agreement.items()},
        "bones_detail": {n: {"parent": parents[n], "head_gltf": to_gltf(segments[n][0]),
                             "tail_gltf": to_gltf(segments[n][1])} for n in QUADRUPED_BONES},
        "landmarks_body_frame": landmarks,
        "joint_fit": {
            "tolerance_m": round(JOINT_TOLERANCE * H, 4),
            "rule": "a deform joint passes when it is inside the mesh envelope (the voxel solid the skin "
                    f"encloses, cavity filled) or within {JOINT_TOLERANCE} x mesh height of its surface",
            "all_deform_joints_pass": all(r["within_tolerance"] for r in fitted.values() if r["deform"]),
            "max_outside_by_m": worst(fitted, "outside_by_m"),
            "max_nearest_vertex_m": worst(fitted, "nearest_vertex_m"),
            "joints": fitted,
        },
        "current_rig_for_comparison": {
            "path": reference["path"],
            "note": (f"the reference rig was built on another mesh ({ready_path} replaces it), so this "
                     "comparison only shows how badly the old skeleton would fit the new mesh")
            if args.source or args.reference_rig else "same mesh, turned into this frame",
            "max_outside_by_m": worst(before, "outside_by_m"),
            "max_nearest_vertex_m": worst(before, "nearest_vertex_m"),
            "joints_failing": [n for n, r in before.items() if r["deform"] and not r["within_tolerance"]],
            "joints": before,
        },
    }
    write_json(os.path.join(out_dir, f"{asset_id}_rig.json"), report)
    print("RIG_FIT_RESULT " + json.dumps(json_ready({
        "asset": asset_id, "method": method, "yaw_deg": yaw_deg,
        "unweighted": stats["unweighted_vertices"],
        "zero_dominance": report["limb_neck_head_bones_with_zero_dominance"],
        "all_joints_pass": report["joint_fit"]["all_deform_joints_pass"],
        "max_outside_after": report["joint_fit"]["max_outside_by_m"],
        "max_outside_before": report["current_rig_for_comparison"]["max_outside_by_m"],
    })))


# --------------------------------------------------------------------------------------------------
# Render / verify: the game's skinning, frame by frame
# --------------------------------------------------------------------------------------------------
CLIP_TIMES = {
    "idle": [0.0, 1.0, 2.0, 3.0],
    "walk": [0.0, 0.3, 0.6, 0.9],
    "run": [0.0, 0.2, 0.4, 0.6],
    "attack": [0.0, 0.25, 0.35, 0.5, 0.75],
    "hit": [0.0, 0.14, 0.275, 0.55],
    "death": [0.0, 0.6, 1.2, 1.9],
}


def quat_normalize(q):
    return q / np.linalg.norm(q)


def slerp(a, b, t):
    a, b = quat_normalize(a), quat_normalize(b)
    dot = float(np.dot(a, b))
    if dot < 0:
        b, dot = -b, -dot
    if dot > 0.9995:
        return quat_normalize(a + t * (b - a))
    theta = math.acos(dot)
    return (math.sin((1 - t) * theta) * a + math.sin(t * theta) * b) / math.sin(theta)


def quat_multiply(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz])


def quat_inverse(q):
    return np.array([-q[0], -q[1], -q[2], q[3]]) / np.dot(q, q)


def sample_channel(times, values, interpolation, t, path):
    if interpolation == "CUBICSPLINE":
        values = values.reshape(len(times), 3, -1)[:, 1]
    if t <= times[0]:
        return values[0]
    if t >= times[-1]:
        return values[-1]
    k = int(np.searchsorted(times, t) - 1)
    u = (t - times[k]) / (times[k + 1] - times[k])
    if interpolation == "STEP":
        return values[k]
    if path == "rotation":
        return slerp(values[k], values[k + 1], u)
    return values[k] * (1 - u) + values[k + 1] * u


def load_clip(path):
    js, binary = read_glb(path)
    animation = js["animations"][0]
    tracks = {}
    duration = 0.0
    for channel in animation["channels"]:
        sampler = animation["samplers"][channel["sampler"]]
        times = read_accessor(js, binary, sampler["input"]).ravel()
        values = read_accessor(js, binary, sampler["output"])
        node = js["nodes"][channel["target"]["node"]]
        tracks.setdefault(node["name"], {})[channel["target"]["path"]] = (
            times, values, sampler.get("interpolation", "LINEAR"))
        duration = max(duration, float(times[-1]))
    rest = {n["name"]: n for n in js["nodes"]}
    return {"tracks": tracks, "duration": duration, "rest": rest, "name": animation.get("name")}


def game_pose(rig_js, clip, t):
    """Node locals at time t as SkinnedModel/ArtLibrary.Retarget build them: bone tracks by name,
    rotation = target_rest * source_rest^-1 * key, position = target_rest + (key - source_rest)."""
    locals_ = [node_local(n) for n in rig_js["nodes"]]
    for index, node in enumerate(rig_js["nodes"]):
        name = node.get("name")
        if name not in clip["tracks"] or name not in QUADRUPED_BONES:
            continue
        t_rest = np.array(node.get("translation", [0, 0, 0]), dtype=np.float64)
        r_rest = np.array(node.get("rotation", [0, 0, 0, 1]), dtype=np.float64)
        s_rest = np.array(node.get("scale", [1, 1, 1]), dtype=np.float64)
        source = clip["rest"].get(name, {})
        t_src = np.array(source.get("translation", [0, 0, 0]), dtype=np.float64)
        r_src = np.array(source.get("rotation", [0, 0, 0, 1]), dtype=np.float64)
        translation, rotation, scale = t_rest, r_rest, s_rest
        for path, (times, values, interpolation) in clip["tracks"][name].items():
            value = sample_channel(times, values, interpolation, t, path)
            if path == "translation":
                translation = t_rest + (value - t_src)
            elif path == "rotation":
                rotation = quat_normalize(quat_multiply(quat_multiply(r_rest, quat_inverse(r_src)), value))
            elif path == "scale":
                scale = value
        locals_[index] = trs_matrix(translation, rotation, scale)
    return locals_


def skin(rig_js, rig_bin, locals_, mesh):
    skin_def = rig_js["skins"][0]
    worlds = world_matrices(rig_js, locals_)
    ibm = read_accessor(rig_js, rig_bin, skin_def["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    mats = np.array([worlds[j] @ ibm[k] for k, j in enumerate(skin_def["joints"])])
    blend = np.einsum("vk,vkij->vij", mesh["W"], mats[mesh["J"]])
    P = np.einsum("vij,vj->vi", blend[:, :3, :3], mesh["P"]) + blend[:, :3, 3]
    N = np.einsum("vij,vj->vi", blend[:, :3, :3], mesh["N"])
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    joints = {rig_js["nodes"][j]["name"]: worlds[j][:3, 3] for j in skin_def["joints"]}
    return P, N, joints


def setup_render_scene(center, radius, size):
    import bpy
    from mathutils import Vector
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("W")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.44, 0.48, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6

    bpy.ops.mesh.primitive_plane_add(size=40.0, location=(0.0, 0.0, 0.0))
    ground = bpy.context.active_object
    ground.name = "ground_z0"
    material = bpy.data.materials.new("ground")
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.36, 0.34, 0.31, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    ground.data.materials.append(material)

    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", type="SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(4)
    sun.rotation_euler = (math.radians(38), math.radians(8), math.radians(-35))
    bpy.context.collection.objects.link(sun)
    fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", type="AREA"))
    fill.data.energy = 120.0 * radius ** 2
    fill.data.size = radius * 3
    fill.location = Vector(center) + Vector((-radius * 3, -radius * 2, radius * 2.5))
    fill.rotation_euler = (Vector(center) - fill.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.collection.objects.link(fill)

    camera = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    camera.data.lens = 50
    bpy.context.collection.objects.link(camera)
    scene.camera = camera
    return camera


VIEWS = {
    # Direction from the subject to the camera (Blender frame: -Y = the creature's forward, +X = its left).
    "left": (1.0, 0.0, 0.18),
    "front_left": (0.75, -0.75, 0.42),
    "front": (0.0, -1.0, 0.25),
    "top": (0.0, 0.001, 1.0),
}


def render_view(camera, center, radius, view, path):
    import bpy
    from mathutils import Vector
    direction = Vector(VIEWS[view]).normalized()
    camera.location = Vector(center) + direction * radius * 2.8
    camera.rotation_euler = (Vector(center) - camera.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def make_sheet(paths, columns, out_path, scale=2):
    """Tile rendered PNGs into one contact sheet (downscaled) for review."""
    import bpy
    images = [bpy.data.images.load(p) for p in paths]
    width, height = images[0].size
    w, h = width // scale, height // scale
    rows = (len(images) + columns - 1) // columns
    sheet = np.ones((rows * h, columns * w, 4), dtype=np.float32)
    for k, image in enumerate(images):
        pixels = np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)
        pixels = pixels[: h * scale, : w * scale].reshape(h, scale, w, scale, 4).mean(axis=(1, 3))
        r = rows - 1 - k // columns
        c = k % columns
        sheet[r * h:(r + 1) * h, c * w:(c + 1) * w] = pixels
    out = bpy.data.images.new(os.path.basename(out_path), columns * w, rows * h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = out_path
    out.file_format = "PNG"
    out.save()
    for image in images:
        bpy.data.images.remove(image)
    bpy.data.images.remove(out)


def nearest_distances(A, B, chunk=1500):
    """Distance from every point of A to its nearest point of B (exact, chunked brute force)."""
    out = np.empty(len(A))
    B2 = np.einsum("ij,ij->i", B, B)
    for start in range(0, len(A), chunk):
        a = A[start:start + chunk]
        d2 = np.einsum("ij,ij->i", a, a)[:, None] + B2[None, :] - 2.0 * a @ B.T
        out[start:start + chunk] = np.sqrt(np.maximum(d2.min(axis=1), 0.0))
    return out


def run_render(args):
    import bpy
    from mathutils import Vector
    asset_id = args.id
    out_dir = output_dir(args)
    render_dir = os.path.join(out_dir, "renders")
    os.makedirs(render_dir, exist_ok=True)
    rigged_path = os.path.join(out_dir, f"{asset_id}_rigged.glb")
    lod0_path = os.path.join(out_dir, f"{asset_id}.glb")
    with open(os.path.join(out_dir, f"{asset_id}_rig.json"), encoding="utf-8") as handle:
        rig_report = json.load(handle)

    rig_js, rig_bin = read_glb(rigged_path)
    mesh = mesh_arrays(rig_js, rig_bin, skinned=True)
    mesh_node = next(n for n in rig_js["nodes"] if "skin" in n)
    primitive = rig_js["meshes"][mesh_node["mesh"]]["primitives"][0]
    mesh["N"] = read_accessor(rig_js, rig_bin, primitive["attributes"]["NORMAL"])
    lod0_js, lod0_bin = read_glb(lod0_path)
    lod0 = mesh_arrays(lod0_js, lod0_bin)

    verify = {"asset": asset_id, "rigged": rigged_path, "lod0": lod0_path}

    # Bind pose: skinning at rest must reproduce the static LOD0.
    rest_P, _rest_N, _rest_joints = skin(rig_js, rig_bin, [node_local(n) for n in rig_js["nodes"]], mesh)
    matched = None
    if len(rest_P) == len(lod0["P"]):
        matched = float(np.abs(rest_P - lod0["P"]).max())
    lod0_to_rest = nearest_distances(lod0["P"], rest_P)
    rest_to_lod0 = nearest_distances(rest_P, lod0["P"])
    skin_def = rig_js["skins"][0]
    ibm = read_accessor(rig_js, rig_bin, skin_def["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    worlds = world_matrices(rig_js)
    verify["bind"] = {
        "vertex_count_rigged_vs_lod0": [len(rest_P), len(lod0["P"])],
        "max_vertex_offset_same_order_m": matched,
        "hausdorff_lod0_vs_rest_skin_m": round(max(float(lod0_to_rest.max()), float(rest_to_lod0.max())), 7),
        "inverse_bind_times_rest_max_error": float(max(np.abs(ibm[k] @ worlds[j] - np.eye(4)).max()
                                                    for k, j in enumerate(skin_def["joints"]))),
        "skin_joint_count": len(skin_def["joints"]),
    }

    # Weight dominance and joint fit, read back from the exported file.
    names = [rig_js["nodes"][j]["name"] for j in skin_def["joints"]]
    dominant = mesh["J"][np.arange(len(mesh["J"])), np.argmax(mesh["W"], axis=1)]
    verify["dominated_vertices_from_glb"] = {n: int((dominant == k).sum()) for k, n in enumerate(names)}
    verify["max_influences_from_glb"] = int((mesh["W"] > 0).sum(axis=1).max())
    verify["weight_sum_range"] = [float(mesh["W"].sum(axis=1).min()), float(mesh["W"].sum(axis=1).max())]

    # Blender scene: import the rigged model once, drive its vertices with our skinning.
    blender_reset()
    imported = blender_import(rigged_path)
    body = next(o for o in imported if o.type == "MESH")
    for obj in imported:
        if obj.type == "ARMATURE":
            obj.hide_render = True
    for modifier in list(body.modifiers):
        body.modifiers.remove(modifier)
    world_matrix = body.matrix_world.copy()
    body.parent = None
    body.matrix_world = world_matrix
    order = np.unique(mesh["F"])

    def set_pose(P, N):
        B = to_body(P[order])
        inverse = np.linalg.inv(np.array(body.matrix_world))
        local = B @ inverse[:3, :3].T + inverse[:3, 3]
        body.data.attributes["position"].data.foreach_set("vector", local.astype(np.float32).ravel())
        body.data.normals_split_custom_set_from_vertices([tuple(v) for v in to_body(N[order])])
        body.data.update()

    base = np.empty(len(body.data.vertices) * 3)
    body.data.vertices.foreach_get("co", base)
    import_error = float(np.abs(base.reshape(-1, 3) @ np.array(body.matrix_world)[:3, :3].T
                                + np.array(body.matrix_world)[:3, 3] - to_body(mesh["P"][order])).max())
    verify["blender_vertex_order_error_m"] = import_error
    if import_error > 1e-4:
        raise RuntimeError("imported vertex order does not follow the glTF buffer; cannot drive it")

    B_rest = to_body(mesh["P"])
    low, high = B_rest.min(axis=0), B_rest.max(axis=0)
    center = (low + high) / 2
    radius = float(np.linalg.norm(high - low) / 2) * 1.08
    camera = setup_render_scene(center, radius, args.size)

    written = {"bind": [], "fit": [], "clips": {}}

    # Bind pose next to the static LOD0 from identical cameras.
    set_pose(mesh["P"], mesh["N"])
    for view in ("left", "front_left", "front"):
        path = os.path.join(render_dir, f"{asset_id}_bind_rigged_{view}.png")
        render_view(camera, center, radius, view, path)
        written["bind"].append(path)
    body.hide_render = True
    static = [o for o in blender_import(lod0_path) if o.type == "MESH"]
    for view in ("left", "front_left", "front"):
        path = os.path.join(render_dir, f"{asset_id}_bind_lod0_{view}.png")
        render_view(camera, center, radius, view, path)
        written["bind"].append(path)
    pixel_diffs = {}
    for view in ("left", "front_left", "front"):
        a = bpy.data.images.load(os.path.join(render_dir, f"{asset_id}_bind_rigged_{view}.png"))
        b = bpy.data.images.load(os.path.join(render_dir, f"{asset_id}_bind_lod0_{view}.png"))
        pa, pb = np.array(a.pixels[:]), np.array(b.pixels[:])
        pixel_diffs[view] = {"mean_abs": round(float(np.abs(pa - pb).mean()), 6),
                             "max_abs": round(float(np.abs(pa - pb).max()), 6)}
        bpy.data.images.remove(a)
        bpy.data.images.remove(b)
    verify["bind"]["render_difference_rigged_vs_lod0"] = pixel_diffs
    for obj in static:
        bpy.data.objects.remove(obj, do_unlink=True)
    body.hide_render = False

    # Fit overlay: the fitted bones drawn through a see-through body.
    overlay = []
    ghost = bpy.data.materials.new("ghost")
    ghost.use_nodes = True
    ghost_bsdf = ghost.node_tree.nodes["Principled BSDF"]
    ghost_bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.82, 1.0)
    ghost_bsdf.inputs["Alpha"].default_value = 0.22
    ghost.surface_render_method = "BLENDED"
    original_materials = list(body.data.materials)
    body.data.materials.clear()
    body.data.materials.append(ghost)
    colours = {"L": (0.95, 0.15, 0.1, 1), "R": (0.1, 0.35, 0.95, 1), "C": (1.0, 0.8, 0.05, 1)}
    bone_materials = {}
    for key, colour in colours.items():
        material = bpy.data.materials.new(f"bone_{key}")
        material.use_nodes = True
        nodes = material.node_tree.nodes
        nodes.remove(nodes["Principled BSDF"])
        emission = nodes.new("ShaderNodeEmission")
        emission.inputs[0].default_value = colour
        emission.inputs[1].default_value = 3.0
        material.node_tree.links.new(emission.outputs[0], nodes["Material Output"].inputs[0])
        bone_materials[key] = material
    details = rig_report["bones_detail"]
    for name, detail in details.items():
        head = Vector(to_body(detail["head_gltf"]))
        tail = Vector(to_body(detail["tail_gltf"]))
        key = "L" if name.endswith(".L") else "R" if name.endswith(".R") else "C"
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.012 * radius * 2, location=head)
        sphere = bpy.context.active_object
        sphere.data.materials.append(bone_materials[key])
        overlay.append(sphere)
        length = (tail - head).length
        bpy.ops.mesh.primitive_cone_add(radius1=0.008 * radius * 2, radius2=0.002 * radius * 2, depth=length,
                                        location=(head + tail) / 2)
        cone = bpy.context.active_object
        cone.rotation_euler = (tail - head).to_track_quat("Z", "Y").to_euler()
        cone.data.materials.append(bone_materials[key])
        overlay.append(cone)
    for view in ("left", "front", "top", "front_left"):
        path = os.path.join(render_dir, f"{asset_id}_fit_{view}.png")
        render_view(camera, center, radius, view, path)
        written["fit"].append(path)
    for obj in overlay:
        bpy.data.objects.remove(obj, do_unlink=True)
    body.data.materials.clear()
    for material in original_materials:
        body.data.materials.append(material)

    # Clips, skinned the way the game plays them.
    from_clips = os.path.join(out_dir, "animation", "ready", "creatures")
    verify["clips"] = {}
    for clip_file in sorted(os.listdir(from_clips)) if os.path.isdir(from_clips) else []:
        if not clip_file.endswith(".glb"):
            continue
        kind = clip_file.split(".")[-2]
        clip = load_clip(os.path.join(from_clips, clip_file))
        rest_error = 0.0
        for node in rig_js["nodes"]:
            source = clip["rest"].get(node.get("name"))
            if node.get("name") in QUADRUPED_BONES and source is not None:
                rest_error = max(rest_error, float(np.abs(node_local(node) - node_local(source)).max()))
        stretch = []
        times = CLIP_TIMES.get(kind, [0.0, clip["duration"] / 2, clip["duration"]])
        paths = []
        edges = np.unique(np.sort(np.concatenate([mesh["F"][:, [0, 1]], mesh["F"][:, [1, 2]],
                                                  mesh["F"][:, [2, 0]]]), axis=1), axis=0)
        rest_length = np.linalg.norm(mesh["P"][edges[:, 0]] - mesh["P"][edges[:, 1]], axis=1)
        valid = rest_length > 1e-5
        for t in times:
            t = min(t, clip["duration"])
            P, N, _joints = skin(rig_js, rig_bin, game_pose(rig_js, clip, t), mesh)
            length = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
            ratio = length[valid] / rest_length[valid]
            B = to_body(P)
            stretch.append({"t": round(t, 3), "edge_ratio_p99_9": round(float(np.percentile(ratio, 99.9)), 4),
                            "edge_ratio_max": round(float(ratio.max()), 4),
                            "edge_ratio_min": round(float(ratio.min()), 4),
                            "lowest_point_m": round(float(B[:, 2].min()), 4)})
            set_pose(P, N)
            for view in ("left", "front_left"):
                path = os.path.join(render_dir, f"{asset_id}_{kind}_t{t:.2f}_{view}.png")
                render_view(camera, center, radius, view, path)
                paths.append(path)
        written["clips"][kind] = paths
        make_sheet(paths, 2, os.path.join(render_dir, f"{asset_id}_sheet_{kind}.png"))
        verify["clips"][kind] = {"file": clip_file, "duration_s": clip["duration"],
                                 "bones_with_tracks": sorted(clip["tracks"]),
                                 "clip_rest_vs_rig_rest_max_error": rest_error, "frames": stretch}
    set_pose(mesh["P"], mesh["N"])
    make_sheet(written["bind"], 3, os.path.join(render_dir, f"{asset_id}_sheet_bind.png"))
    make_sheet(written["fit"], 2, os.path.join(render_dir, f"{asset_id}_sheet_fit.png"))
    verify["renders"] = written
    write_json(os.path.join(out_dir, f"{asset_id}_verify.json"), verify)
    print("RIG_RENDER_RESULT " + json.dumps(json_ready({"asset": asset_id, "bind": verify["bind"],
                                                        "clips": sorted(verify["clips"])})))


def main():
    args = parse_args()
    if args.mode == "fit":
        run_fit(args)
    else:
        run_render(args)


if __name__ == "__main__":
    main()
