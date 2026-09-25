"""Fit the 20-bone humanoid skeleton to a character mesh from measured landmarks, bind it, export.

`_blender_rig.py` builds this skeleton from a fixed 1.8 m table and weights it by distance, so a
1.3 m smith or a 2.0 m suit of armour carries joints metres away from the limbs they should bend.
This tool keeps that skeleton's twenty bone NAMES, hierarchy, order and roll convention (so every
clip and binding written against `npc_veth_magistrate`'s rig still addresses the same bones) but
places every joint from the mesh itself:

  height      the mesh's top above the floor (the asset pipeline stands meshes on Z = 0)
  crotch      the lowest height at which the two leg columns found near the floor merge into one
              cross-section piece of the slice; the hip joints sit HIP_ABOVE_CROTCH above it
  neck        the narrowest central slice in the top quarter, bracketed below by the height at
              which the head column meets the shoulders
  shoulders   the torso's outer edge a little below the neck base, inset by a joint radius, on a
              shoulder line yawed like the measured hip line
  arms        cross-section pieces clear of the torso, followed up from the lowest: that one is
              the hand, the wrist sits a hand length above its tip, the elbow along the column
              (or, where the arm has merged into the body, between the column and the shoulder,
              never outside the body's edge)
  legs        the two leg columns tracked from the floor to the crotch: knee half way down,
              ankle a foot's height above each leg's own lowest point, the foot bone pointing at
              the measured toe

Where a garment hides a landmark (a floor-length robe hides the crotch; sleeves joined to the
body hide the arm columns) the report says so and the joint falls back to a stated proportion of
the measured height. Nothing is offset per bone after the fact: the skeleton is the measurement.

Weights are Blender's automatic (bone heat) weights. The generative meshes are ~1,700 loose,
UV-split shards spanned by huge triangles, which heat cannot diffuse over, so the solve runs on a
welded copy made watertight by a voxel remesh (voxel_proxy) and the weights are read back onto
the untouched vertices (vertices the proxy does not cover keep the heat solved on the welded
surface itself). The arm chains are masked out of the torso and the torso-side weights solved
without them (REGION_BONES): otherwise an arm hanging beside a broad chest, coat or cuirass
claims it, and a hand hanging beside an apron takes the apron. The seam is smoothed. A vertex may
not take weight from a limb chain it is not connected to on the surface (reject_cross_limb): heat
can hand an armour elbow thigh weight across a gap, and that patch then tears toward the thigh. The
mesh geometry, UVs, normals and material are exported untouched.

Modes (run inside Blender):
  blender --background --factory-startup --python _blender_rig_fit_humanoid20.py -- ^
      --mode fit --input <lod0.glb> --outdir <staging dir> --name <asset id>
  blender --background --factory-startup --python _blender_rig_fit_humanoid20.py -- ^
      --mode render --rigged <rigged.glb> --reference <lod0.glb> --clips <clip.glb> [...] ^
      --outdir <renders dir>

The landmark fit, the GLB reader and the fit check are plain numpy and import without Blender, so
`_blender_rig_fit_humanoid20_run.py` can verify the exported files outside it.
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np

try:
    import bpy
    import bmesh
    from mathutils import Vector
    from mathutils.kdtree import KDTree
except ImportError:  # outside Blender: the numpy half of the module still works
    bpy = None

# The 20 bones of the NPC / humanoid-creature rig, in the order `_blender_rig.HUMANOID` creates
# them. Order matters beyond names: it fixes the glTF joint order, so it is kept identical.
BONES = [
    ("root", None), ("hips", "root"), ("spine", "hips"), ("chest", "spine"),
    ("neck", "chest"), ("head", "neck"),
    ("shoulder.L", "chest"), ("upper_arm.L", "shoulder.L"), ("forearm.L", "upper_arm.L"),
    ("hand.L", "forearm.L"),
    ("shoulder.R", "chest"), ("upper_arm.R", "shoulder.R"), ("forearm.R", "upper_arm.R"),
    ("hand.R", "forearm.R"),
    ("thigh.L", "hips"), ("shin.L", "thigh.L"), ("foot.L", "shin.L"),
    ("thigh.R", "hips"), ("shin.R", "thigh.R"), ("foot.R", "shin.R"),
]
LIMB_BONES = [n for n, _p in BONES if n not in ("root", "hips", "spine", "chest")]

# Measurement resolution and anatomical constants, all as fractions of the measured height H.
SLICES = 100                 # horizontal slices from floor to top
BIN = 0.005                  # x histogram bin inside a slice
GAP = 0.015                  # an empty run this wide separates two slice clusters
SIGNIFICANT = 0.02           # a cluster must hold this share of its slice to count as a column
HIP_ABOVE_CROTCH = 0.06      # femoral head above the crotch (template: 0.95 m hips, ~0.84 m crotch)
HEM_BELOW = 0.25             # a leg split lower than this is a garment hem, not the crotch
CROTCH_FALLBACK = 0.47       # crotch height used when a garment hides it
ANKLE = 0.055                # ankle joint above the floor (template 0.10 m of 1.8 m)
NECK_SEARCH_FROM = 0.75      # the neck is looked for in the top quarter
HEAD_MIN = 0.06              # the head keeps at least this much of the top
NECK_MIN = 0.03              # shortest neck bone
SHOULDER_BELOW_NECK = 0.05   # glenohumeral joint below the neck base (template 1.44 vs 1.50)
SHOULDER_INSET = 0.045       # joint centre inside the torso's outer edge (template 0.18 vs 0.26)
CLAVICLE_OFFSET = 0.028      # clavicle root beside the neck (template 0.05 m)
WRIST_ABOVE_TIP = 0.09       # hand length: wrist above the lowest point of the arm column
HAND_TIP_FALLBACK = 0.433    # fingertip height when the arms cannot be separated from the body
ELBOW_FRACTION = 0.53        # elbow's share of the shoulder-to-wrist drop (template 0.52)
SPINE_FRACTION = 0.309       # spine joint's share of hips-to-neck (template 1.12 in 0.95..1.50)
CHEST_FRACTION = 0.673       # chest joint's share of hips-to-neck (template 1.32)
ARM_RADIUS = (0.015, 0.045, 0.03)   # min, max, default arm radius when the column is hidden
FIT_TOLERANCE = 0.06         # definition of done: every weighted joint within 0.06 x H of the mesh


# --------------------------------------------------------------------------------------------
# GLB reading (numpy only)

def read_glb(path):
    with open(path, "rb") as handle:
        handle.read(12)
        length, _kind = struct.unpack("<I4s", handle.read(8))
        doc = json.loads(handle.read(length))
        length, _kind = struct.unpack("<I4s", handle.read(8))
        blob = handle.read(length)
    return doc, blob


def accessor(doc, blob, index):
    acc = doc["accessors"][index]
    view = doc["bufferViews"][acc["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
    dtype = {5126: np.float32, 5123: np.uint16, 5121: np.uint8, 5125: np.uint32}[acc["componentType"]]
    offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride", 0)
    item = np.dtype(dtype).itemsize * comps
    count = acc["count"]
    if stride and stride != item:
        raw = np.frombuffer(blob, np.uint8, stride * count, offset).reshape(count, stride)[:, :item]
        return np.frombuffer(raw.tobytes(), dtype).reshape(count, comps)
    return np.frombuffer(blob, dtype, count * comps, offset).reshape(count, comps)


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    rot = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                    [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                    [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    out = np.eye(4)
    out[:3, :3] = rot * np.array(node.get("scale", [1, 1, 1]))
    out[:3, 3] = node.get("translation", [0, 0, 0])
    return out


def world_matrices(doc):
    nodes = doc["nodes"]
    parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
    cache = {}

    def world(i):
        if i not in cache:
            local = node_matrix(nodes[i])
            cache[i] = world(parent[i]) @ local if i in parent else local
        return cache[i]
    return [world(i) for i in range(len(nodes))], parent


def to_blender(points):
    """glTF (x, y up, z forward) to Blender (x, y back, z up), the frame the fit works in."""
    points = np.asarray(points, dtype=np.float64)
    return np.c_[points[:, 0], -points[:, 2], points[:, 1]]


def glb_mesh(path, skinned=None):
    """Triangles of every mesh in a GLB, in Blender axes. Skinned meshes are taken as authored
    (glTF ignores their node transform); static ones through their node's world matrix."""
    doc, blob = read_glb(path)
    worlds, _parent = world_matrices(doc)
    verts, tris, joints, weights = [], [], [], []
    offset = 0
    for index, node in enumerate(doc["nodes"]):
        if "mesh" not in node:
            continue
        if skinned is not None and ("skin" in node) != skinned:
            continue
        for prim in doc["meshes"][node["mesh"]]["primitives"]:
            pos = accessor(doc, blob, prim["attributes"]["POSITION"]).astype(np.float64)
            if "skin" not in node:
                pos = (worlds[index] @ np.c_[pos, np.ones(len(pos))].T).T[:, :3]
            ind = accessor(doc, blob, prim["indices"]).astype(np.int64).reshape(-1, 3)
            verts.append(pos)
            tris.append(ind + offset)
            offset += len(pos)
            if "JOINTS_0" in prim["attributes"]:
                joints.append(accessor(doc, blob, prim["attributes"]["JOINTS_0"]).astype(np.int64))
                raw = accessor(doc, blob, prim["attributes"]["WEIGHTS_0"])
                weights.append(raw.astype(np.float64) if raw.dtype == np.float32
                               else raw.astype(np.float64) / np.iinfo(raw.dtype).max)
    return {
        "verts_gltf": np.concatenate(verts),
        "verts": to_blender(np.concatenate(verts)),
        "tris": np.concatenate(tris),
        "joints": np.concatenate(joints) if joints else None,
        "weights": np.concatenate(weights) if weights else None,
        "doc": doc, "blob": blob,
    }


def glb_skeleton(path):
    """Joint names, parents, rest world positions (Blender axes) and the bind-vs-rest error."""
    doc, blob = read_glb(path)
    worlds, parent = world_matrices(doc)
    skin = doc["skins"][0]
    joints = skin["joints"]
    names = [doc["nodes"][j].get("name") for j in joints]
    parents = [doc["nodes"][parent[j]].get("name") if j in parent and parent[j] in joints else None
               for j in joints]
    rest = np.array([worlds[j] for j in joints])
    ibm = accessor(doc, blob, skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    bind = np.array([np.linalg.inv(m) for m in ibm])
    return {"names": names, "parents": parents, "positions": to_blender(rest[:, :3, 3]),
            "bind_vs_rest": float(np.abs(bind - rest).max()),
            "node_order": [doc["nodes"][j].get("name") for j in range(len(doc["nodes"]))]}


# --------------------------------------------------------------------------------------------
# Landmark fit (numpy only)

def surface_samples(verts, tris, count=300000, seed=7):
    """Area-weighted points on the surface. The generative meshes have huge triangles, so the
    vertices alone leave whole limbs empty in a slice."""
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    rng = np.random.default_rng(seed)
    pick = rng.choice(len(tris), count, p=area / area.sum())
    u, v = rng.random(count), rng.random(count)
    flip = u + v > 1
    u[flip], v[flip] = 1 - u[flip], 1 - v[flip]
    samples = a[pick] + (b[pick] - a[pick]) * u[:, None] + (c[pick] - a[pick]) * v[:, None]
    return np.concatenate([samples, verts])


class Cluster:
    """A group of slice points: an x-run of the slice (`Body.clusters`) or a connected 2D blob
    of its cross-section (`Body.components`)."""

    def __init__(self, pts, lo=None, hi=None):
        self.pts = pts
        self.n = len(pts)
        self.lo = float(pts[:, 0].min()) if lo is None else float(lo)
        self.hi = float(pts[:, 0].max()) if hi is None else float(hi)
        self.ylo = float(pts[:, 1].min()) if len(pts) else 0.0
        self.yhi = float(pts[:, 1].max()) if len(pts) else 0.0
        self.cx = 0.5 * (self.lo + self.hi)
        self.cy = float(np.median(pts[:, 1])) if len(pts) else 0.0

    def overlaps(self, other, tol):
        return (self.lo <= other.hi + tol and self.hi >= other.lo - tol
                and self.ylo <= other.yhi + tol and self.yhi >= other.ylo - tol)

    def distance(self, other):
        return math.hypot(self.cx - other.cx, self.cy - other.cy)


def dilate2(grid):
    out = grid.copy()
    out[1:] |= grid[:-1]
    out[:-1] |= grid[1:]
    out[:, 1:] |= grid[:, :-1]
    out[:, :-1] |= grid[:, 1:]
    out[1:, 1:] |= grid[:-1, :-1]
    out[:-1, :-1] |= grid[1:, 1:]
    out[1:, :-1] |= grid[:-1, 1:]
    out[:-1, 1:] |= grid[1:, :-1]
    return out


def label2d(mask):
    """Connected components of a boolean grid (4-neighbour), by min-label propagation with
    pointer jumping; numpy only, because Blender ships without scipy."""
    size = mask.size
    lab = np.where(mask, np.arange(size).reshape(mask.shape), size)
    while True:
        m = lab.copy()
        m[1:] = np.minimum(m[1:], lab[:-1])
        m[:-1] = np.minimum(m[:-1], lab[1:])
        m[:, 1:] = np.minimum(m[:, 1:], lab[:, :-1])
        m[:, :-1] = np.minimum(m[:, :-1], lab[:, 1:])
        m = np.where(mask, m, size)
        for _ in range(4):
            flat = np.append(m.ravel(), size)
            m = np.where(mask, flat[m], size)
        if np.array_equal(m, lab):
            return lab
        lab = m


class Body:
    def __init__(self, samples):
        self.floor = float(samples[:, 2].min())
        self.S = samples - np.array([0.0, 0.0, self.floor])
        self.H = float(self.S[:, 2].max())
        self.dz = self.H / SLICES
        self._cache = {}

    def slab(self, z, half=None):
        half = self.dz * 0.5 if half is None else half
        return self.S[np.abs(self.S[:, 2] - z) <= half]

    def clusters(self, z):
        """Runs of the slice along x separated by at least GAP of empty space."""
        key = ("x", round(z / self.dz, 3))
        if key in self._cache:
            return self._cache[key]
        pts = self.slab(z)
        out = []
        if len(pts) >= 8:
            x = pts[:, 0]
            width = BIN * self.H
            lo = x.min()
            count = int((x.max() - lo) / width) + 1
            index = np.minimum(((x - lo) / width).astype(int), count - 1)
            hist = np.bincount(index, minlength=count)
            occupied = hist >= max(2, 0.03 * np.median(hist[hist > 0]))
            gap_bins = max(1, int(round(GAP / BIN)))
            runs, start, last, empty = [], None, None, 0
            for i in range(count):
                if occupied[i]:
                    if start is None:
                        start = i
                    last, empty = i, 0
                elif start is not None:
                    empty += 1
                    if empty >= gap_bins:
                        runs.append((start, last))
                        start = None
            if start is not None:
                runs.append((start, last))
            for s, e in runs:
                inside = (index >= s) & (index <= e)
                out.append(Cluster(pts[inside], lo + s * width, lo + (e + 1) * width))
        self._cache[key] = out
        return out

    def components(self, z, bridge=None):
        """Connected pieces of the slice's cross-section in (x, y), gaps under GAP bridged. Two
        legs standing one ahead of the other stay apart here even where their x-ranges touch.
        `bridge` overrides the number of one-cell dilations (0 = cells must touch)."""
        steps = max(1, int(round(GAP / BIN / 2))) if bridge is None else int(bridge)
        key = ("c", round(z / self.dz, 3), steps)
        if key in self._cache:
            return self._cache[key]
        pts = self.slab(z)
        out = []
        if len(pts) >= 8:
            cell = BIN * self.H
            lo = pts[:, :2].min(0)
            ij = ((pts[:, :2] - lo) / cell).astype(int)
            occ = np.zeros(ij.max(0) + 1, dtype=bool)
            occ[ij[:, 0], ij[:, 1]] = True
            for _ in range(steps):
                occ = dilate2(occ)
            lab = label2d(occ)[ij[:, 0], ij[:, 1]]
            for k in np.unique(lab):
                out.append(Cluster(pts[lab == k]))
        self._cache[key] = out
        return out

    def zs(self, lo, hi):
        return [z for z in np.arange(lo, hi + 1e-9, self.dz)]


def medial_y(pts, fallback):
    return float(np.median(pts[:, 1])) if len(pts) >= 5 else fallback


def significant(blobs, share=SIGNIFICANT):
    total = sum(b.n for b in blobs) or 1
    return [b for b in blobs if b.n >= share * total]


def track_legs(body):
    """Seed two leg columns near the floor and follow them up until one cross-section piece
    holds both (the crotch)."""
    H, dz = body.H, body.dz
    seed = None
    for z in body.zs(0.06 * H, 0.24 * H):
        blobs = sorted(significant(body.components(z)), key=lambda c: -c.n)[:2]
        if len(blobs) == 2 and abs(blobs[0].cx - blobs[1].cx) >= 0.04 * H:
            seed = (z, sorted(blobs, key=lambda c: c.cx))
            break
    notes = []
    if seed is None:
        return None, notes + ["no two leg columns anywhere between 0.06 and 0.24 H"]
    z0, (right, left) = seed
    columns = {"L": [(z0, left)], "R": [(z0, right)]}
    tol = 0.01 * H
    crotch = None
    merged_run = 0
    prev = {"L": left, "R": right}
    for z in body.zs(z0 + dz, 0.75 * H):
        blobs = body.components(z)
        best = {}
        for s in ("L", "R"):
            hits = [b for b in blobs if b.overlaps(prev[s], tol)]
            best[s] = max(hits, key=lambda b: b.n) if hits else None
        if best["L"] is not None and best["L"] is best["R"]:
            merged_run += 1
            if merged_run >= 2:
                crotch = z - dz
                break
            continue
        merged_run = 0
        for s in ("L", "R"):
            if best[s] is not None:
                columns[s].append((z, best[s]))
                prev[s] = best[s]
    # downward from the seed, so the ankle and foot use the same columns
    prev = {"L": left, "R": right}
    for z in body.zs(dz * 0.5, z0 - dz)[::-1]:
        blobs = body.components(z)
        for s in ("L", "R"):
            hits = [b for b in blobs if b.overlaps(prev[s], tol)]
            if hits:
                b = max(hits, key=lambda k: k.n)
                columns[s].insert(0, (z, b))
                prev[s] = b
    if crotch is None:
        notes.append("leg columns never merged below 0.75 H")
        crotch = max(z for z, _c in columns["L"] + columns["R"])
    return {"crotch": crotch, "columns": columns, "seed_z": z0}, notes


def column_point(column, z, dz):
    """Medial (x, y) of a tracked column near height z, interpolated between tracked slices."""
    near = [c for zz, c in column if abs(zz - z) <= 1.5 * dz]
    if near:
        pts = np.concatenate([c.pts for c in near])
        return float(np.mean([c.cx for c in near])), medial_y(pts, near[0].cy)
    zs = np.array([c[0] for c in column])
    order = np.argsort(zs)
    xs = np.array([column[i][1].cx for i in order])
    ys = np.array([column[i][1].cy for i in order])
    return float(np.interp(z, zs[order], xs)), float(np.interp(z, zs[order], ys))


def track_arm(body, side_sign, z_lo, z_hi, spine_xy):
    """The arm on one side: cross-section pieces clear of the torso piece, followed upward from
    the lowest one while each slice's piece continues the one below. The longest such column
    is the arm (its lowest piece is the hand)."""
    H, dz = body.H, body.dz
    per_slice = []
    for z in body.zs(z_lo, z_hi):
        cx, cy = spine_xy(z)
        blobs = body.components(z)
        if not blobs:
            continue
        holding = [b for b in blobs if b.lo <= cx <= b.hi and b.ylo <= cy <= b.yhi]
        torso = max(holding, key=lambda b: b.n) if holding else min(
            blobs, key=lambda b: math.hypot(b.cx - cx, b.cy - cy))
        total = sum(b.n for b in blobs) or 1
        cands = [b for b in blobs if b is not torso and b.n >= 0.01 * total
                 and b.hi - b.lo >= 0.008 * H and side_sign * (b.cx - cx) > 0.06 * H
                 and side_sign * (b.cx - torso.cx) > 0]
        per_slice.append((z, cands))
    columns = []
    used = set()
    for start_index, (z0, cands0) in enumerate(per_slice):
        for b0 in cands0:
            if id(b0) in used:
                continue
            column = [(z0, b0)]
            used.add(id(b0))
            for z, cands in per_slice[start_index + 1:]:
                last_z, last = column[-1]
                near = [b for b in cands if b.distance(last) <= 0.06 * H and id(b) not in used]
                if near:
                    pick = min(near, key=lambda b: b.distance(last))
                    column.append((z, pick))
                    used.add(id(pick))
                elif z - last_z > 3 * dz:
                    break
            columns.append(column)
    columns = [c for c in columns if len(c) >= 3]
    return max(columns, key=len) if columns else []


# A hand resting against a tool on the belt, the apron or the thigh joins the body's piece of the
# slice at the default GAP bridging, and the arm column above stops at the wrist: the hand is then
# left to the torso (and the thigh) by the weighting. Below the column's lowest piece the same
# slices are re-cut with less bridging, and the piece that continues the one above is followed
# down while it stays clear of the spine and does not widen into another object.
HAND_BRIDGES = (1, 0)        # finer re-cuts tried, coarsest first
HAND_WIDEN = 1.5             # a continuing piece may be at most this much wider or deeper
HAND_DOWN_LIMIT = 0.2        # x H: never follow below this


def extend_arm_down(body, column, side_sign, spine_xy):
    """The arm column continued downward through slices where the hand touches the body."""
    if not column:
        return column, 0
    H, dz = body.H, body.dz
    tol = 0.01 * H
    added = []
    last = column[0][1]
    z = column[0][0] - dz
    while z >= HAND_DOWN_LIMIT * H:
        cx, cy = spine_xy(z)
        pick = None
        for bridge in HAND_BRIDGES:
            near = [b for b in body.components(z, bridge)
                    if b.overlaps(last, tol)
                    and not (b.lo <= cx <= b.hi and b.ylo <= cy <= b.yhi)
                    and side_sign * (b.cx - cx) > 0.06 * H
                    and (b.hi - b.lo) <= HAND_WIDEN * (last.hi - last.lo) + tol
                    and (b.yhi - b.ylo) <= HAND_WIDEN * (last.yhi - last.ylo) + tol]
            if near:
                pick = max(near, key=lambda b: b.n)
                break
        if pick is None:
            break
        added.append((z, pick))
        last = pick
        z -= dz
    return added[::-1] + column, len(added)


def central_cluster(body, z, x_centre):
    cl = body.clusters(z)
    if not cl:
        return None
    inside = [c for c in cl if c.lo - 1e-9 <= x_centre <= c.hi + 1e-9]
    if inside:
        return inside[0]
    return min(cl, key=lambda c: min(abs(c.lo - x_centre), abs(c.hi - x_centre)))


def fit_skeleton(verts, tris, contact_aware=False):
    """Measure the landmarks and return {bone: (head, tail)} in Blender axes plus the measurements.
    `contact_aware`: follow a hand that touches the body down at finer bridging (extend_arm_down)
    and let the weighting keep it apart from what it touches (see CONTACT below)."""
    body = Body(surface_samples(verts, tris))
    H, dz = body.H, body.dz
    floor = body.floor
    notes, measured = [], {"height_m": round(H, 4), "floor_z": round(floor, 5)}
    x_mid = float(np.median(body.S[:, 0]))

    # ---- legs and crotch
    legs, leg_notes = track_legs(body)
    notes += leg_notes
    garment_hides_crotch = legs is None or legs["crotch"] < HEM_BELOW * H
    if legs is not None:
        measured["leg_split_top_m"] = round(legs["crotch"], 4)
    if garment_hides_crotch:
        crotch = CROTCH_FALLBACK * H
        notes.append(f"the leg columns merge at {legs['crotch'] if legs else 0:.3f} m "
                     f"(below {HEM_BELOW} H): a garment hem, not the crotch; crotch taken at "
                     f"{CROTCH_FALLBACK} H = {crotch:.3f} m")
    else:
        crotch = legs["crotch"]
    measured["crotch_m"] = round(crotch, 4)
    hip_z = crotch + HIP_ABOVE_CROTCH * H
    regions = {"crotch": crotch, "dz": dz, "legs": {}, "arms": {}}

    joints = {}
    for side in ("L", "R"):
        column = legs["columns"][side] if legs else []
        if not column:
            sx = 0.055 * H * (1 if side == "L" else -1)
            column = [(0.0, Cluster(np.array([[sx, 0.0, 0.0]])))]
        regions["legs"][side] = [(float(z), c.lo, c.hi, c.ylo, c.yhi) for z, c in column if c.n > 1]
        # A leg that stops short of the floor (a missing or cut-off foot) keeps its ankle a
        # foot's height above its own lowest point, not above the floor.
        bottom = float(np.percentile(column[0][1].pts[:, 2], 1))
        ankle_z = bottom + ANKLE * H
        top = [c for z, c in column if z >= 0.15 * H] or [c for _z, c in column]
        hip_x = float(np.median([c.cx for c in top]))
        hip_y = float(np.median([c.cy for c in top]))
        ank = [(z, c) for z, c in column if ankle_z <= z <= ankle_z + 0.07 * H] or column[:3]
        ankle_x = float(np.median([c.cx for _z, c in ank]))
        ankle_y = medial_y(np.concatenate([c.pts for _z, c in ank]),
                           float(np.median([c.cy for _z, c in ank])))
        knee_z = 0.5 * (hip_z + ankle_z)
        knee_x, knee_y = column_point(column, min(knee_z, crotch - dz), dz)
        joints[f"hip.{side}"] = np.array([hip_x, hip_y, hip_z])
        joints[f"knee.{side}"] = np.array([knee_x, knee_y, knee_z])
        joints[f"ankle.{side}"] = np.array([ankle_x, ankle_y, ankle_z])
        # toe: the most forward (lowest y) surface below the ankle, under this leg
        low = [c for z, c in column if z <= ankle_z + 0.02 * H]
        if low:
            xlo = min(c.lo for c in low) - 0.02 * H
            xhi = max(c.hi for c in low) + 0.02 * H
        else:
            xlo, xhi = ankle_x - 0.04 * H, ankle_x + 0.04 * H
        foot = body.S[(body.S[:, 2] <= ankle_z) & (body.S[:, 0] >= xlo) & (body.S[:, 0] <= xhi)]
        toe = None
        if len(foot) > 20:
            y_toe = float(np.percentile(foot[:, 1], 3))
            front = foot[foot[:, 1] <= y_toe + 0.03 * H]
            toe = np.array([float(np.median(front[:, 0])), y_toe,
                            max(bottom + 0.012 * H, float(np.median(front[:, 2])))])
        if toe is None or toe[1] > ankle_y - 0.03 * H:
            # no foot reaching forward: the foot bone runs down to the leg's lowest piece
            lowest = column[0][1]
            toe = np.array([lowest.cx, lowest.cy, bottom + 0.012 * H])
            if np.linalg.norm(toe - joints[f"ankle.{side}"]) < 0.03 * H:
                toe = joints[f"ankle.{side}"] + np.array([0.0, -0.06 * H, -0.5 * ANKLE * H])
            notes.append(f"foot.{side}: no toe reaches ahead of the ankle (the leg ends "
                         f"{bottom:.3f} m above the floor); the foot bone points at the leg's "
                         f"lowest piece")
        joints[f"toe.{side}"] = toe
        measured[f"leg_bottom_z_m.{side}"] = round(bottom, 4)

    pelvis = 0.5 * (joints["hip.L"] + joints["hip.R"])
    measured["hip_joint_z_m"] = round(hip_z, 4)

    # ---- neck and head: central-cluster width through the top quarter
    widths = []
    for z in body.zs(NECK_SEARCH_FROM * H, H - dz * 0.5):
        c = central_cluster(body, z, x_mid)
        if c is not None:
            widths.append((z, c.hi - c.lo, c))
    top_down = sorted(widths, key=lambda w: -w[0])
    head_width = 0.0
    head_bottom = None
    for z, w, _c in top_down:
        if head_width and w > 1.35 * head_width and z < H - HEAD_MIN * H:
            head_bottom = z + dz
            break
        head_width = max(head_width, w)
    search = [(z, w, c) for z, w, c in widths if z <= H - HEAD_MIN * H]
    narrow_z = min(search, key=lambda t: t[1])[0] if search else H * 0.9
    if head_bottom is None:
        head_bottom = narrow_z
        notes.append("no step between head and shoulder widths; neck base taken at the narrowest slice")
    neck_z = min(head_bottom, narrow_z - 0.02 * H)
    head_z = max(narrow_z + 0.01 * H, neck_z + NECK_MIN * H)
    head_z = min(head_z, H - HEAD_MIN * H)
    neck_z = min(neck_z, head_z - NECK_MIN * H)
    measured.update({"neck_narrowest_z_m": round(narrow_z, 4), "head_column_bottom_m": round(head_bottom, 4),
                     "neck_base_z_m": round(neck_z, 4), "skull_base_z_m": round(head_z, 4)})

    def central_point(z):
        c = central_cluster(body, z, x_mid)
        if c is None:
            return np.array([x_mid, 0.0, z])
        return np.array([c.cx, c.cy, z])

    neck = central_point(neck_z)
    head = central_point(head_z)
    top_pts = body.S[body.S[:, 2] >= H - 0.04 * H]
    crown = np.array([float(np.median(top_pts[:, 0])), float(np.median(top_pts[:, 1])), H])

    # ---- spine line: pelvis to neck base; depth from the torso slice
    def torso_joint(fraction):
        z = pelvis[2] + fraction * (neck[2] - pelvis[2])
        x = pelvis[0] + fraction * (neck[0] - pelvis[0])
        c = central_cluster(body, z, x)
        y = c.cy if c is not None else pelvis[1] + fraction * (neck[1] - pelvis[1])
        return np.array([x, y, z])

    spine = torso_joint(SPINE_FRACTION)
    chest = torso_joint(CHEST_FRACTION)

    # ---- arms
    shoulder_z = neck[2] - SHOULDER_BELOW_NECK * H

    def spine_xy(z):
        t = min(max((z - pelvis[2]) / max(neck[2] - pelvis[2], 1e-6), 0.0), 1.0)
        p = pelvis + t * (neck - pelvis)
        return float(p[0]), float(p[1])

    def edge_x(z, sign):
        pts = body.slab(z, dz)
        if len(pts) < 5:
            return None, pts
        return float(np.percentile(pts[:, 0], 99.5 if sign > 0 else 0.5)), pts

    # The shoulder line takes the body's measured yaw (from the two hip joints): a local depth
    # probe at the shoulder is pulled around by pauldrons, hoods and collars.
    hip_dx = joints["hip.L"][0] - joints["hip.R"][0]
    yaw_slope = (joints["hip.L"][1] - joints["hip.R"][1]) / hip_dx if abs(hip_dx) > 1e-6 else 0.0
    yaw_slope = float(np.clip(yaw_slope, -math.tan(math.radians(25)), math.tan(math.radians(25))))
    measured["body_yaw_deg_from_hips"] = round(math.degrees(math.atan(yaw_slope)), 1)

    for side, sign in (("L", 1.0), ("R", -1.0)):
        column = track_arm(body, sign, crotch, shoulder_z, spine_xy)   # bottom-up
        clear_from = column[0][0] if column else None
        column, extended = extend_arm_down(body, column, sign, spine_xy) if contact_aware else (column, 0)
        if extended:
            notes.append(f"arm.{side}: the hand joins the body's slice below {clear_from:.3f} m (it "
                         f"rests against a tool, the apron or the thigh); followed {extended} slices "
                         f"further down at finer bridging to {column[0][0]:.3f} m")
        # shoulder joint: inside the torso's outer edge, below the neck base
        ex, pts = edge_x(shoulder_z, sign)
        sx = ex - sign * SHOULDER_INSET * H
        if sign * (sx - neck[0]) < 0.06 * H:
            sx = neck[0] + sign * 0.06 * H
        spine_x, spine_y = spine_xy(shoulder_z)
        shoulder = np.array([sx, spine_y + (sx - spine_x) * yaw_slope, shoulder_z])
        if column:
            radius = float(np.clip(0.5 * np.median([c.hi - c.lo for _z, c in column]),
                                   ARM_RADIUS[0] * H, ARM_RADIUS[1] * H))
            lowest = column[0][1]
            tip_z = float(np.percentile(lowest.pts[:, 2], 2))
            tip = np.array([lowest.cx, lowest.cy, tip_z])
            top_z = column[-1][0]
            measured[f"arm_column.{side}"] = {"slices": len(column), "from_z": round(column[0][0], 3),
                                              "to_z": round(top_z, 3), "radius_m": round(radius, 3),
                                              "slices_followed_at_finer_bridging": extended}
        else:
            radius = ARM_RADIUS[2] * H
            tip_z = HAND_TIP_FALLBACK * H
            ex_tip, _pts = edge_x(tip_z, sign)
            tip = np.array([(ex_tip if ex_tip is not None else sign * 0.13 * H) - sign * radius,
                            0.0, tip_z])
            notes.append(f"arm.{side}: no cross-section piece separates the arm from the body "
                         f"(sleeve joined to the torso); fingertip taken at {HAND_TIP_FALLBACK} H "
                         f"and the column along the body's outer edge")
            measured[f"arm_column.{side}"] = {"slices": 0, "hidden": True}

        def arm_point(z, column=column, sign=sign, radius=radius, shoulder=shoulder):
            if column and z <= column[-1][0] + 1.5 * dz:
                x, y = column_point(column, max(z, column[0][0]), dz)
                return np.array([x, y, z])
            if column:
                # above the tracked column the arm runs inside the sleeve or the body, from the
                # column's top to the shoulder joint - and never outside the body's outer edge
                top_z = column[-1][0]
                tx, ty = column_point(column, top_z, dz)
                t = (z - top_z) / max(shoulder[2] - top_z, 1e-6)
                x, y = tx + t * (shoulder[0] - tx), ty + t * (shoulder[1] - ty)
                ex_z, pts_z = edge_x(z, sign)
                if ex_z is not None and sign * (x - (ex_z - sign * radius)) > 0:
                    x = ex_z - sign * radius
                    strip = pts_z[np.abs(pts_z[:, 0] - x) <= radius * 1.5]
                    y = medial_y(strip, y)
                return np.array([x, y, z])
            ex_z, pts_z = edge_x(z, sign)
            if ex_z is None:
                return None
            x = ex_z - sign * radius
            strip = pts_z[np.abs(pts_z[:, 0] - x) <= radius * 1.5]
            return np.array([x, medial_y(strip, float(np.median(pts_z[:, 1]))), z])

        wrist_z = min(tip_z + WRIST_ABOVE_TIP * H, shoulder_z - 0.2 * H)
        wrist = arm_point(wrist_z)
        elbow_z = shoulder_z - ELBOW_FRACTION * (shoulder_z - wrist_z)
        elbow = arm_point(elbow_z)
        if elbow is None:
            elbow = shoulder + ELBOW_FRACTION * (wrist - shoulder)
        clavicle = np.array([neck[0] + sign * CLAVICLE_OFFSET * H, 0.5 * (neck[1] + shoulder[1]),
                             shoulder_z + 0.011 * H])
        regions["arms"][side] = {"radius": radius, "chain": [shoulder, elbow, wrist, tip],
                                 "column": [(float(z), c.lo, c.hi, c.ylo, c.yhi) for z, c in column],
                                 "column_z": ((float(column[0][0]), float(column[-1][0])) if column else None)}
        joints[f"clavicle.{side}"] = clavicle
        joints[f"shoulder.{side}"] = shoulder
        joints[f"elbow.{side}"] = elbow
        joints[f"wrist.{side}"] = wrist
        joints[f"fingertip.{side}"] = tip

    j = joints
    root_head = np.array([pelvis[0], pelvis[1], 0.0])
    bones = {
        "root": (root_head, root_head + np.array([0, 0, 0.055 * H])),
        "hips": (pelvis, spine),
        "spine": (spine, chest),
        "chest": (chest, neck),
        "neck": (neck, head),
        "head": (head, crown),
    }
    for side in ("L", "R"):
        bones[f"shoulder.{side}"] = (j[f"clavicle.{side}"], j[f"shoulder.{side}"])
        bones[f"upper_arm.{side}"] = (j[f"shoulder.{side}"], j[f"elbow.{side}"])
        bones[f"forearm.{side}"] = (j[f"elbow.{side}"], j[f"wrist.{side}"])
        bones[f"hand.{side}"] = (j[f"wrist.{side}"], j[f"fingertip.{side}"])
        bones[f"thigh.{side}"] = (j[f"hip.{side}"], j[f"knee.{side}"])
        bones[f"shin.{side}"] = (j[f"knee.{side}"], j[f"ankle.{side}"])
        bones[f"foot.{side}"] = (j[f"ankle.{side}"], j[f"toe.{side}"])
    # back into the mesh's own floor frame
    lift = np.array([0.0, 0.0, floor])
    bones = {k: (h + lift, t + lift) for k, (h, t) in bones.items()}
    measured["landmarks"] = {k: [round(float(v), 4) for v in (p + lift)] for k, p in joints.items()}
    head_cluster = central_cluster(body, head_z, x_mid)
    regions.update({
        "contact_aware": bool(contact_aware),
        "floor": floor, "H": H, "neck_z": neck_z, "shoulder_z": shoulder_z,
        "neck_x": float(neck[0]),
        "head_half_width": 0.5 * (head_cluster.hi - head_cluster.lo) if head_cluster else 0.06 * H,
        "spine": (pelvis.copy(), neck.copy()),
    })
    body.regions = regions
    return bones, measured, notes, body


# --------------------------------------------------------------------------------------------
# Fit check (numpy only)

def point_triangle_distance(p, a, b, c):
    """Distance from point p to each triangle (a, b, c are (n, 3) arrays)."""
    ab, ac, ap = b - a, c - a, p - a
    d1, d2 = (ab * ap).sum(1), (ac * ap).sum(1)
    bp = p - b
    d3, d4 = (ab * bp).sum(1), (ac * bp).sum(1)
    cp = p - c
    d5, d6 = (ab * cp).sum(1), (ac * cp).sum(1)
    va = d3 * d6 - d5 * d4
    vb = d5 * d2 - d1 * d6
    vc = d1 * d4 - d3 * d2
    denom = va + vb + vc
    denom[np.abs(denom) < 1e-20] = 1e-20
    v = vb / denom
    w = vc / denom
    closest = a + ab * v[:, None] + ac * w[:, None]
    # regions outside the face: fall back to the closest point on each edge
    def seg(p0, p1):
        d = p1 - p0
        t = np.clip(((p - p0) * d).sum(1) / np.maximum((d * d).sum(1), 1e-20), 0, 1)
        return p0 + d * t[:, None]
    outside = (va < 0) | (vb < 0) | (vc < 0)
    if outside.any():
        cands = np.stack([seg(a, b), seg(b, c), seg(c, a)])
        dist = np.linalg.norm(cands - p, axis=2)
        best = cands[dist.argmin(0), np.arange(len(a))]
        closest[outside] = best[outside]
    return np.linalg.norm(closest - p, axis=1)


def surface_distance(points, verts, tris):
    a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
    return np.array([float(point_triangle_distance(np.asarray(p, dtype=np.float64), a, b, c).min())
                     for p in points])


def solid_voxels(samples, H, cells=110, close=2):
    """Voxel fill of a possibly open shell: surface voxels dilated by `close` cells (which plugs
    holes up to about twice that), then everything the outside cannot reach by flood fill."""
    lo = samples.min(0) - 0.05 * H
    size = H / cells
    shape = np.ceil((samples.max(0) + 0.05 * H - lo) / size).astype(int) + 1
    grid = np.zeros(shape, dtype=bool)
    idx = ((samples - lo) / size).astype(int)
    grid[idx[:, 0], idx[:, 1], idx[:, 2]] = True
    for _ in range(close):
        grid = dilate(grid)
    outside = np.zeros_like(grid)
    outside[0, :, :] = outside[-1, :, :] = True
    outside[:, 0, :] = outside[:, -1, :] = True
    outside[:, :, 0] = outside[:, :, -1] = True
    outside &= ~grid
    while True:
        grown = dilate(outside) & ~grid
        if grown.sum() == outside.sum():
            break
        outside = grown
    return ~outside, lo, size


def dilate(grid):
    out = grid.copy()
    out[1:] |= grid[:-1]
    out[:-1] |= grid[1:]
    out[:, 1:] |= grid[:, :-1]
    out[:, :-1] |= grid[:, 1:]
    out[:, :, 1:] |= grid[:, :, :-1]
    out[:, :, :-1] |= grid[:, :, 1:]
    return out


def fit_check(joint_positions, names, verts, tris, samples=None):
    """Per joint: distance to the surface, inside-the-volume flag, and the effective distance
    (0 when inside) against the 0.06 x height tolerance."""
    if samples is None:
        samples = surface_samples(verts, tris)
    H = float(verts[:, 2].max() - verts[:, 2].min())
    solid, lo, size = solid_voxels(samples, H)
    dist = surface_distance(joint_positions, verts, tris)
    vert_dist = np.array([float(np.linalg.norm(verts - p, axis=1).min()) for p in joint_positions])
    out = {}
    for k, name in enumerate(names):
        cell = ((joint_positions[k] - lo) / size).astype(int)
        inside = bool(np.all(cell >= 0) and np.all(cell < solid.shape) and solid[tuple(cell)])
        effective = 0.0 if inside else float(dist[k])
        out[name] = {"pos": [round(float(v), 4) for v in joint_positions[k]],
                     "surface_m": round(float(dist[k]), 4),
                     "nearest_vertex_m": round(float(vert_dist[k]), 4),
                     "inside_volume": inside,
                     "effective_m": round(effective, 4),
                     "within_tolerance": effective <= FIT_TOLERANCE * H}
    return out, H


# --------------------------------------------------------------------------------------------
# Blender: fit and bind

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=["fit", "render"], required=True)
    parser.add_argument("--input")
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--name")
    parser.add_argument("--max-influences", type=int, default=4)
    parser.add_argument("--min-weight", type=float, default=0.02)
    parser.add_argument("--weld", type=float, default=1e-4, help="merge distance, x height")
    parser.add_argument("--contact-aware", action="store_true",
                        help="follow a hand that touches the body, a tool or the apron down at finer "
                             "bridging, and keep it apart from what it touches in the weights")
    parser.add_argument("--arm-reach", type=float, default=None,
                        help=f"x arm radius: surface this close to the arm chain is arm (default "
                             f"{ARM_REACH}). An A-pose mesh whose arms clear the torso to the armpit "
                             f"wants less, or the lats beside the shoulder follow the arm")
    parser.add_argument("--cross-limb", choices=["reject", "keep"], default="reject",
                        help="reject (default): a vertex may not take weight from a limb chain it is not "
                             "connected to on the surface (reject_cross_limb); keep: bone heat as solved "
                             "(rigs fitted before 2026-09-25)")
    parser.add_argument("--proxy", choices=["voxel", "weld"], default="voxel",
                        help="solve bone heat on a voxel remesh of the welded copy, or on the weld")
    parser.add_argument("--rigged")
    parser.add_argument("--reference")
    parser.add_argument("--baseline", help="another rigged GLB whose bind pose is rendered for comparison")
    parser.add_argument("--clips", nargs="*", default=[])
    parser.add_argument("--frames", type=int, default=4)
    parser.add_argument("--size", type=int, default=1024)
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)


def import_meshes(path):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.context.scene.objects if o not in before]


def join_meshes(meshes, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    # same naming as _blender_rig.merge, so the staged file reads like the library's
    joined.name = name
    joined.data.name = f"{name}_mesh"
    for material in joined.data.materials:
        if material is not None:
            material.name = f"MAT_{name}"
    return joined


def mesh_arrays(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    verts = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", verts)
    verts = verts.reshape(-1, 3)
    mw = np.array(obj.matrix_world)
    verts = (mw @ np.c_[verts, np.ones(len(verts))].T).T[:, :3]
    tris = np.zeros(len(mesh.loop_triangles) * 3, dtype=np.int64)
    mesh.loop_triangles.foreach_get("vertices", tris)
    return verts, tris.reshape(-1, 3)


def build_armature(bones, name):
    armature = bpy.data.armatures.new(f"{name}_armature")
    rig = bpy.data.objects.new(f"{name}_rig", armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for bone_name, _parent in BONES:
        head, tail = bones[bone_name]
        edit = armature.edit_bones.new(bone_name)
        edit.head = Vector(head)
        edit.tail = Vector(tail)
        edit.roll = 0.0   # the template's convention; the clips' Euler angles assume it
        created[bone_name] = edit
    for bone_name, parent in BONES:
        if parent:
            created[bone_name].parent = created[parent]
            created[bone_name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    # The root is the ground anchor under the pelvis: it carries no skin (the template's root
    # dominated no vertex either) and heat weighting must not hand it the feet.
    armature.bones["root"].use_deform = False
    return rig


def welded_copy(obj, distance):
    copy = obj.copy()
    copy.data = obj.data.copy()
    copy.name = f"{obj.name}_weld"
    bpy.context.collection.objects.link(copy)
    for group in list(copy.vertex_groups):
        copy.vertex_groups.remove(group)
    bm = bmesh.new()
    bm.from_mesh(copy.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=distance)
    after = len(bm.verts)
    bm.to_mesh(copy.data)
    bm.free()
    return copy, before, after


# Which bones may weight each part of the body. Bone heat alone lets an arm bone hanging beside
# a broad chest, coat or cuirass claim that torso (it is the nearest bone the surface can see),
# so raising the arm peels the chest away with it, and a hand hanging beside an apron takes the
# apron. So the arms are masked the way a rigger masks groups: heat is solved once over every
# bone for the arm geometry and once without the arm chains for everything else, and the seam
# between the two is smoothed over the mesh.
ARM_CHAIN = {side: [f"upper_arm.{side}", f"forearm.{side}", f"hand.{side}"] for side in ("L", "R")}
DEFORM = [n for n, _p in BONES if n != "root"]
REGION_BONES = {
    "arm.L": [n for n in DEFORM if n not in ARM_CHAIN["R"]],
    "arm.R": [n for n in DEFORM if n not in ARM_CHAIN["L"]],
    "body": [n for n in DEFORM if n not in ARM_CHAIN["L"] + ARM_CHAIN["R"]],
}
ARM_REACH = 3.0         # x arm radius: surface this close to the arm's bone chain is arm
SEAM_WIDTH = 0.03       # x H: the arm seam is smoothed this far either side
SEAM_ITERATIONS = 10
PROXIMITY_LINK = 0.004  # x H: loose shards closer than this are smoothed as if joined
SHARD_GLUE_ITERATIONS = 3
# Voxel sizes (x H) tried for the heat proxy, finest first. Too fine and a thin plate becomes a
# closed sliver whose every vertex is shadowed from every bone by its own far side; bone heat
# then has a component with no heat source, its system is singular, and Blender drops the whole
# solve ("failed to find solution"). The finest size at which every pass solves is used.
PROXY_VOXELS = (0.010, 0.0125, 0.015, 0.0175, 0.02, 0.025, 0.03)
PROXY_MIN_PIECE = 0.01  # proxy pieces smaller than this share of it are dropped
PASS_COVERAGE = 0.98    # a heat pass must weight this share of its region's vertices
# Limb chains for the cross-limb rule (reject_cross_limb): a vertex may take a limb chain's weight
# only where the surface carrying that chain's weight is connected to the limb itself.
LIMB_CHAINS = {
    "arm.L": ["shoulder.L"] + ARM_CHAIN["L"], "arm.R": ["shoulder.R"] + ARM_CHAIN["R"],
    "leg.L": ["thigh.L", "shin.L", "foot.L"], "leg.R": ["thigh.R", "shin.R", "foot.R"],
}
LIMB_SUPPORT = 0.02     # a chain's surface is where it holds at least this weight (the clean limit)


def segment_distance(points, a, b):
    ab = b - a
    t = np.clip(((points - a) @ ab) / max(float(ab @ ab), 1e-12), 0.0, 1.0)
    return np.linalg.norm(points - (a + t[:, None] * ab), axis=1)


def classify_regions(points, regions):
    """Label each point (Blender world coordinates): 'arm.L', 'arm.R' or 'body'. Arm geometry
    is the tracked arm column pieces plus any surface within ARM_REACH arm radii of the arm's
    bone chain, clear of the torso's core and no lower than the fingertips."""
    H, dz = regions["H"], regions["dz"]
    P = points - np.array([0.0, 0.0, regions["floor"]])
    labels = np.array(["body"] * len(P), dtype=object)
    pelvis, neck = regions["spine"]
    spine_x = np.interp(P[:, 2], [pelvis[2], neck[2]], [pelvis[0], neck[0]])
    tol = 0.01 * H
    for side, sign in (("L", 1.0), ("R", -1.0)):
        arm = regions["arms"][side]
        chain = [np.asarray(c, dtype=np.float64) for c in arm["chain"]]   # already floor-relative
        near = np.full(len(P), np.inf)
        for a, b in zip(chain[:-1], chain[1:]):
            near = np.minimum(near, segment_distance(P, a, b))
        inside = near <= regions.get("arm_reach", ARM_REACH) * arm["radius"]
        pieces = np.zeros(len(P), dtype=bool)
        for z, lo, hi, ylo, yhi in arm.get("column", []):
            pieces |= ((np.abs(P[:, 2] - z) <= dz) & (P[:, 0] >= lo - tol) & (P[:, 0] <= hi + tol)
                       & (P[:, 1] >= ylo - tol) & (P[:, 1] <= yhi + tol))
        if regions.get("contact_aware") and arm.get("column_z"):
            # CONTACT: along the tracked column the arm is exactly its own cross-section pieces;
            # the reach would also take the tool, apron or thigh the hand rests against
            zone = P[:, 2] <= arm["column_z"][1] - dz
            inside = np.where(zone, pieces, inside | pieces)
        else:
            inside |= pieces
        inside &= (P[:, 2] >= chain[-1][2] - 0.01 * H) & (sign * (P[:, 0] - spine_x) > 0.04 * H)
        labels[inside] = f"arm.{side}"
    return labels


def heat_pass(weld, rig, allowed, bone_index):
    """Bone heat on a throwaway copy of the welded mesh with only `allowed` bones deforming."""
    for bone in rig.data.bones:
        bone.use_deform = bone.name in allowed
    copy = weld.copy()
    copy.data = weld.data.copy()
    bpy.context.collection.objects.link(copy)
    bpy.ops.object.select_all(action="DESELECT")
    copy.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    table = np.zeros((len(copy.data.vertices), len(bone_index)))
    groups = {g.index: bone_index[g.name] for g in copy.vertex_groups if g.name in allowed}
    for v in copy.data.vertices:
        for g in v.groups:
            if g.group in groups:
                table[v.index, groups[g.group]] = g.weight
    mesh = copy.data
    bpy.data.objects.remove(copy, do_unlink=True)
    bpy.data.meshes.remove(mesh)
    return table


def voxel_proxy(weld, voxel):
    """A closed, evenly tessellated stand-in for the welded mesh, for bone heat to solve on.

    Welding alone leaves the generative surface as ~1,700 loose shards whose limbs are spanned by
    a handful of huge triangles: bone heat then has almost no vertices to diffuse over (a thigh
    can own a dozen) and touching shards solve independently and part when posed. Here every
    sheet is given a voxel of thickness (the remesher needs closed volumes to find inside from
    outside), the whole is voxel-remeshed into one surface of uniform resolution, and pieces
    under PROXY_MIN_PIECE of it are dropped: a sliver that small can be shadowed from every bone
    by its own far side, which leaves bone heat a component with no heat source and Blender then
    abandons the entire solve. Weights are read back onto the untouched mesh afterwards; points the
    proxy does not cover (hidden inner shards, dropped slivers) take the nearest surface's."""
    copy = weld.copy()
    copy.data = weld.data.copy()
    copy.name = f"{weld.name}_proxy"
    bpy.context.collection.objects.link(copy)
    solid = copy.modifiers.new("proxy_solidify", "SOLIDIFY")
    solid.thickness = voxel
    solid.offset = 0.0
    remesh = copy.modifiers.new("proxy_remesh", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = voxel
    remesh.adaptivity = 0.0
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(copy.evaluated_get(depsgraph))
    old = copy.data
    copy.modifiers.clear()
    copy.data = mesh
    bpy.data.meshes.remove(old)
    bm = bmesh.new()
    bm.from_mesh(copy.data)
    # the remesher can leave a few zero-area faces, which make the heat Laplacian singular too
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.05 * voxel)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=0.05 * voxel)
    bm.verts.ensure_lookup_table()
    piece = [-1] * len(bm.verts)
    count = 0
    for v in bm.verts:
        if piece[v.index] >= 0:
            continue
        stack = [v]
        piece[v.index] = count
        while stack:
            x = stack.pop()
            for e in x.link_edges:
                y = e.other_vert(x)
                if piece[y.index] < 0:
                    piece[y.index] = count
                    stack.append(y)
        count += 1
    sizes = np.bincount(piece)
    small = sizes < PROXY_MIN_PIECE * len(bm.verts)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if small[piece[v.index]]], context="VERTS")
    bm.to_mesh(copy.data)
    bm.free()
    copy["pieces_kept"] = int((~small).sum())
    copy["pieces_dropped"] = int(small.sum())
    return copy


def weld_graph(weld, H, links=True):
    """Mesh edges of the welded copy, and links between loose shards that touch in space."""
    mesh = weld.data
    edges = np.zeros(len(mesh.edges) * 2, dtype=np.int64)
    mesh.edges.foreach_get("vertices", edges)
    edges = edges.reshape(-1, 2)
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    if not links:
        return edges, np.zeros((0, 2), dtype=np.int64)
    tree = KDTree(len(co))
    for i, p in enumerate(co):
        tree.insert(p, i)
    tree.balance()
    links = set()
    for i, p in enumerate(co):
        for _c, j, _d in tree.find_range(p, PROXIMITY_LINK * H):
            if j > i:
                links.add((i, j))
    known = {tuple(sorted(e)) for e in edges.tolist()}
    extra = np.array([l for l in links if l not in known], dtype=np.int64).reshape(-1, 2)
    return edges, extra


def neighbour_mean(table, graph, n):
    acc = np.zeros_like(table)
    cnt = np.zeros(n)
    np.add.at(acc, graph[:, 0], table[graph[:, 1]])
    np.add.at(acc, graph[:, 1], table[graph[:, 0]])
    np.add.at(cnt, graph[:, 0], 1)
    np.add.at(cnt, graph[:, 1], 1)
    return acc, cnt


def fill_unweighted(table, graph, co):
    """Vertices no allowed bone could see take the mean of their weighted neighbours, ring by
    ring; islands with no weighted vertex at all take their nearest weighted vertex's weights."""
    n = len(table)
    empty = table.sum(1) <= 1e-9
    initially = int(empty.sum())
    by_ring = 0
    while empty.any():
        weighted = (~empty).astype(float)[:, None]
        acc, _cnt = neighbour_mean(table * weighted, graph, n)
        donors, _cnt = neighbour_mean(weighted, graph, n)
        grow = empty & (donors[:, 0] > 0)
        if not grow.any():
            break
        table[grow] = acc[grow] / np.maximum(acc[grow].sum(1, keepdims=True), 1e-12)
        by_ring += int(grow.sum())
        empty &= ~grow
    by_nearest = 0
    if empty.any() and not empty.all():
        full = np.where(~empty)[0]
        tree = KDTree(len(full))
        for k, i in enumerate(full):
            tree.insert(co[i], k)
        tree.balance()
        for i in np.where(empty)[0]:
            _c, k, _d = tree.find(co[i])
            table[i] = table[full[k]]
            by_nearest += 1
    return {"heat_unweighted": initially, "filled_from_neighbours": by_ring,
            "filled_from_nearest": by_nearest}


def contact_edges(edges, labels, points, regions):
    """Arm/body edges below the height where the arm column meets the torso: a hand or forearm
    touching the body, a tool or an apron, not the shoulder. The two sides must part there."""
    out = np.zeros(len(edges), dtype=bool)
    if not regions.get("contact_aware"):
        return out
    a, b = edges[:, 0], edges[:, 1]
    mid_z = 0.5 * (points[a, 2] + points[b, 2]) - regions["floor"]
    for side in ("L", "R"):
        span = regions["arms"][side].get("column_z")
        if not span:
            continue
        arm = f"arm.{side}"
        out |= (((labels[a] == arm) != (labels[b] == arm)) & (mid_z <= span[1] - regions["dz"]))
    return out


def smooth_seams(table, labels, edges, graph, rings, apart=None):
    """Blend across region seams: vertices within `rings` mesh rings of a seam relax toward
    their neighbours' weights, so neighbouring regions do not part at the seam. Edges flagged in
    `apart` (contacts, contact_edges) are not seams: those sides should part (the caller also
    leaves them out of `graph`)."""
    n = len(table)
    a, b = edges[:, 0], edges[:, 1]
    seam = np.zeros(n, dtype=bool)
    cross = labels[a] != labels[b]
    if apart is not None:
        cross &= ~apart
    seam[a[cross]] = True
    seam[b[cross]] = True
    band = seam.copy()
    for _ in range(rings):
        grown = band.copy()
        grown[a[band[b]]] = True
        grown[b[band[a]]] = True
        band = grown
    for _ in range(SEAM_ITERATIONS):
        acc, cnt = neighbour_mean(table, graph, n)
        mean = acc / np.maximum(cnt, 1)[:, None]
        table[band] = 0.5 * table[band] + 0.5 * mean[band]
    return int(band.sum())


def glue_shards(table, links):
    """Loose shards that touch in space but share no vertex would part as soon as their
    weights differ; relaxing each across its proximity links keeps them together."""
    if not len(links):
        return
    n = len(table)
    for _ in range(SHARD_GLUE_ITERATIONS):
        acc, cnt = neighbour_mean(table, links, n)
        linked = cnt > 0
        table[linked] = 0.5 * table[linked] + 0.5 * acc[linked] / cnt[linked][:, None]


def contact_zone_side(z, labels_pair, regions):
    """The arm side ('L'/'R') a contact between these labels belongs to, when it lies below that
    arm column's top; else None."""
    for side in ("L", "R"):
        span = regions["arms"][side].get("column_z")
        if span and f"arm.{side}" in labels_pair and z <= span[1] - regions["dz"]:
            return side
    return None


def contact_split(mesh, regions):
    """Part the surface where a hanging hand touches the body, a tool or the apron.

    Generative meshes fuse such contacts into one surface, so however the weights fall, the
    triangles that join the fist to the hammer handle beside it stretch when the fist lifts. Each
    face takes the region most of its corners are in; the edges between arm faces and body faces
    below the arm column's top are split (positions, UVs and the triangle set stay as they are,
    only the vertex sharing changes), and every vertex there then takes the region of its own
    faces. Returns the per-vertex labels to weight by, and what was split."""
    import collections
    # the split must not change shading: keep every corner's normal as imported
    corner_normals = [tuple(c.vector) for c in mesh.data.corner_normals]
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bm.verts.ensure_lookup_table()
    mw = np.array(mesh.matrix_world)
    co = np.array([v.co for v in bm.verts])
    world = (mw @ np.c_[co, np.ones(len(co))].T).T[:, :3]
    vlab = classify_regions(world, regions)
    floor = regions["floor"]
    face_label = {}
    for f in bm.faces:
        counts = collections.Counter(vlab[v.index] for v in f.verts).most_common()
        face_label[f.index] = counts[0][0] if counts[0][1] * 2 > len(f.verts) else "body"
    cross = []
    for e in bm.edges:
        if len(e.link_faces) != 2:
            continue
        la, lb = face_label[e.link_faces[0].index], face_label[e.link_faces[1].index]
        if la == lb:
            continue
        z = 0.5 * (world[e.verts[0].index, 2] + world[e.verts[1].index, 2]) - floor
        if contact_zone_side(z, (la, lb), regions):
            cross.append(e)
    before = len(bm.verts)
    labels_by_face = [face_label[f.index] for f in bm.faces]
    if cross:
        bmesh.ops.split_edges(bm, edges=cross)
    bm.verts.index_update()
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    # faces keep their order through split_edges; read their labels back by index
    labels = np.array(vlab.tolist() + ["body"] * (len(bm.verts) - before), dtype=object)
    co = np.array([v.co for v in bm.verts])
    world = (mw @ np.c_[co, np.ones(len(co))].T).T[:, :3]
    labels[before:] = classify_regions(world[before:], regions)
    mixed = 0
    for v in bm.verts:
        if not v.link_faces:
            continue
        own = collections.Counter(labels_by_face[f.index] for f in v.link_faces).most_common()
        pair = (own[0][0], labels[v.index])
        in_zone = contact_zone_side(world[v.index, 2] - floor, tuple(o[0] for o in own) + (labels[v.index],),
                                    regions)
        if len(own) > 1 and in_zone:
            mixed += 1
        if own[0][0] != labels[v.index] and contact_zone_side(world[v.index, 2] - floor, pair, regions):
            labels[v.index] = own[0][0]
    bm.to_mesh(mesh.data)
    bm.free()
    mesh.data.normals_split_custom_set(corner_normals)   # split_edges keeps faces and corners in order
    mesh.data.update()
    return labels, {"contact_edges_split": len(cross), "vertices_added": len(labels) - before,
                    "contact_vertices_still_shared_by_arm_and_body": mixed}


def free_arm_only_chain(table, labels, points, regions, bone_index):
    """The free-hanging arm (arm geometry below the top of its tracked column, where it is its own
    cross-section piece) takes only its own chain. Bone heat otherwise gives a fist that hangs
    beside the thigh part of the thigh, and the fist then tears toward the leg when it lifts. The
    other bones fade back in over the top three slices of the column, so there is no step."""
    dz = regions["dz"]
    z = points[:, 2] - regions["floor"]
    for side in ("L", "R"):
        span = regions["arms"][side].get("column_z")
        if not span:
            continue
        chain = [bone_index[n] for n in (f"shoulder.{side}", f"upper_arm.{side}",
                                          f"forearm.{side}", f"hand.{side}") if n in bone_index]
        rows = np.nonzero(labels == f"arm.{side}")[0]
        keep = np.clip((z[rows] - (span[1] - 3 * dz)) / (2 * dz), 0.0, 1.0)
        other = np.ones(table.shape[1], dtype=bool)
        other[chain] = False
        faded = table[rows].copy()
        faded[:, other] *= keep[:, None]
        ok = faded[:, chain].sum(1) > 0
        table[rows[ok]] = faded[ok]


def surface_components(mask, graph, n):
    """Connected pieces of the vertices in `mask` over the edges in `graph` (union-find)."""
    parent = np.arange(n)

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    keep = mask[graph[:, 0]] & mask[graph[:, 1]]
    for a, b in graph[keep]:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[max(ra, rb)] = min(ra, rb)
    return np.array([find(i) for i in range(n)])


def reject_cross_limb(table, graph, bone_index):
    """A vertex may not take weight from a limb chain it is not connected to on the surface.

    Bone heat on generative meshes (armour plates, coats, loose shards) sometimes hands a patch
    weight from a limb it only faces across a gap: thigh.R weight on the armour's elbow, a hand's
    weight on a coat skirt. Such a patch tears toward that limb whenever the limb moves. For each
    limb chain (LIMB_CHAINS) the surface holding at least LIMB_SUPPORT of its weight is split into
    connected pieces over the heat mesh's edges (and its shard links); the piece holding most of the
    chain's weight is the limb, and the chain's weight on every other piece is removed. The vertex
    keeps its other bones' weights (renormalised by the caller). Within a chain a vertex may only
    blend neighbouring bones: weight that skips a link moves to the link it skipped. Returns (report, territory), where
    territory[chain] marks the vertices that may carry that chain."""
    n = len(table)
    report, territory = {}, {}
    whole = surface_components(np.ones(n, dtype=bool), graph, n)
    for chain, bones in LIMB_CHAINS.items():
        cols = [bone_index[b] for b in bones if b in bone_index]
        w = table[:, cols].sum(1)
        support = w >= LIMB_SUPPORT
        if not support.any():
            territory[chain] = np.zeros(n, dtype=bool)
            continue
        piece = surface_components(support, graph, n)
        totals = np.bincount(piece[support], weights=w[support], minlength=n)
        limb = int(totals.argmax())
        # Weight under the support limit is the fading edge of some piece and is left to the clean.
        # A piece on a separate part of the surface (a loose pauldron or plate the heat mesh keeps
        # apart) is not reached through anything else, so its weights stand; the rule removes the
        # limb's weight from pieces on the same surface as the limb, reached only across the body.
        stray = support & (piece != limb) & (whole == whole[limb])
        territory[chain] = ~stray
        table[np.ix_(stray, cols)] = 0.0
        # Along the chain a vertex bends between neighbouring bones only: weight on a chain bone two
        # or more links from the vertex's strongest chain bone (an elbow plate holding the clavicle
        # but not the upper arm) moves to the bone next to the strongest one, toward it.
        sub = table[:, cols]
        held = sub.sum(1) > 0
        strongest = sub.argmax(1)
        skipped = 0
        for j in range(len(cols)):
            far = held & (np.abs(j - strongest) > 1) & (sub[:, j] > 0)
            if not far.any():
                continue
            skipped += int(far.sum())
            step = strongest[far] + np.sign(j - strongest[far])
            np.add.at(sub, (np.flatnonzero(far), step), sub[far, j])
            sub[far, j] = 0.0
        table[:, cols] = sub
        report[chain] = {"pieces": int(len(np.unique(piece[support]))),
                         "stray_vertices": int(stray.sum()),
                         "stray_weight_removed": round(float(w[stray].sum()), 3),
                         "stray_max_weight": round(float(w[stray].max()) if stray.any() else 0.0, 3),
                         "vertices_with_a_skipped_link_moved": skipped}
    return report, territory


def nearest_indices(source, points):
    tree = KDTree(len(source.data.vertices))
    for k, v in enumerate(source.data.vertices):
        tree.insert(source.matrix_world @ v.co, k)
    tree.balance()
    return np.array([tree.find(Vector(p))[1] for p in points])


def keep_limb_territory(rows, source, territory, points, bone_names):
    """Weights read from a heat mesh keep a limb chain only where the nearest heat-mesh vertex is in
    that chain's territory (reject_cross_limb): the transfer blend must not bring the stray back."""
    near = nearest_indices(source, points)
    index = {name: i for i, name in enumerate(bone_names)}
    removed = 0
    for chain, allowed in territory.items():
        cols = [index[b] for b in LIMB_CHAINS[chain] if b in index]
        out = ~allowed[near]
        removed += int((rows[np.ix_(out, cols)].sum(1) > 0).sum())
        rows[np.ix_(out, cols)] = 0.0
    return removed


def region_weights(weld, rig, regions, H, proxy_voxel=None, labels_out=None):
    bone_names = [b.name for b in rig.data.bones]
    bone_index = {name: i for i, name in enumerate(bone_names)}
    co = np.zeros(len(weld.data.vertices) * 3)
    weld.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    world = np.array(weld.matrix_world)
    points = (world @ np.c_[co, np.ones(len(co))].T).T[:, :3]
    labels = classify_regions(points, regions)
    table = np.zeros((len(co), len(bone_names)))
    counts, coverage = {}, {}
    for region, allowed in REGION_BONES.items():
        mask = labels == region
        counts[region] = int(mask.sum())
        if mask.any():
            solved = heat_pass(weld, rig, allowed, bone_index)[mask]
            coverage[region] = round(float((solved.sum(1) > 0).mean()), 4)
            table[mask] = solved
    for bone in rig.data.bones:
        bone.use_deform = bone.name != "root"
    if regions.get("contact_aware"):
        free_arm_only_chain(table, labels, points, regions, bone_index)
    edges, links = weld_graph(weld, H, links=proxy_voxel is None)
    apart = contact_edges(edges, labels, points, regions)
    if len(links):
        links = links[~contact_edges(links, labels, points, regions)]
    graph = np.concatenate([edges[~apart], links])
    fill = fill_unweighted(table, graph, points)
    if proxy_voxel:
        spacing = proxy_voxel
    else:
        lengths = np.linalg.norm(points[edges[:, 0]] - points[edges[:, 1]], axis=1)
        spacing = float(np.median(lengths))
    rings = max(2, int(round(SEAM_WIDTH * H / max(spacing, 1e-6))))
    band = smooth_seams(table, labels, edges, graph, rings, apart)
    glue_shards(table, links)
    cross_limb, territory, refill = None, {}, {"heat_unweighted": 0}
    if regions.get("cross_limb", "reject") == "reject":
        cross_limb, territory = reject_cross_limb(table, graph, bone_index)
        refill = fill_unweighted(table, graph, points)
    table /= np.maximum(table.sum(1, keepdims=True), 1e-12)
    info = {"heat_mesh_vertices": int(len(co)), "region_vertices": counts, "pass_coverage": coverage,
            "seam_rings": rings, "seam_band_vertices": band,
            "shard_proximity_links": int(len(links)), **fill,
            "cross_limb": cross_limb, "cross_limb_emptied_and_refilled": refill["heat_unweighted"]}
    if labels_out is not None:
        labels_out["territory"] = territory
    if regions.get("contact_aware"):
        info["contact_edges_kept_apart"] = int(apart.sum())
    if labels_out is not None:
        labels_out["labels"] = labels
    return table, bone_names, info


def sample_rows(source, table, points, neighbours=1, source_labels=None, point_labels=None):
    """Weights at `points` read from a heat mesh: the nearest source vertex, or an inverse-
    distance blend of the nearest few when the source is the (denser) proxy. Also returns the
    distance to the nearest source vertex. With labels, a point reads only source vertices of its
    own region, so a hand does not blend in the tool it touches (and the tool not the hand)."""
    def build(indices):
        tree = KDTree(len(indices))
        for k in indices:
            tree.insert(source.matrix_world @ source.data.vertices[k].co, int(k))
        tree.balance()
        return tree
    everything = build(range(len(source.data.vertices)))
    trees = {}
    if source_labels is not None and point_labels is not None:
        for label in np.unique(point_labels):
            own = np.nonzero(source_labels == label)[0]
            if len(own) >= neighbours:
                trees[label] = build(own)
    rows = np.zeros((len(points), table.shape[1]))
    gaps = np.zeros(len(points))
    for i, p in enumerate(points):
        tree = trees.get(point_labels[i], everything) if trees else everything
        found = tree.find_n(Vector(p), neighbours)
        gaps[i] = found[0][2]
        w = np.array([1.0 / max(d, 1e-6) for _c, _j, d in found])
        rows[i] = (w[:, None] * table[[j for _c, j, _d in found]]).sum(0) / w.sum()
    return rows, gaps


def write_groups(target, rows, bone_names):
    for group in list(target.vertex_groups):
        target.vertex_groups.remove(group)
    groups = [target.vertex_groups.new(name=name) for name in bone_names]
    for i, row in enumerate(rows):
        for k in np.nonzero(row > 0)[0]:
            groups[k].add([i], float(row[k]), "REPLACE")


def clean_weights(mesh, max_influences, min_weight):
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=min_weight)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=max_influences)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def export_glb(objects, path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    # identical settings to _blender_rig.export_glb
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_apply=False, export_yup=True, export_normals=True,
                              export_materials="EXPORT", export_texcoords=True, export_skins=True)


def run_fit(args):
    os.makedirs(args.outdir, exist_ok=True)
    reset_scene()
    meshes = [o for o in import_meshes(args.input) if o.type == "MESH"]
    mesh = join_meshes(meshes, f"{args.name}_rigged")
    verts, tris = mesh_arrays(mesh)
    bones, measured, notes, body = fit_skeleton(verts, tris, contact_aware=args.contact_aware)
    rig = build_armature(bones, args.name)
    if args.arm_reach is not None:
        body.regions["arm_reach"] = args.arm_reach
    body.regions["cross_limb"] = args.cross_limb

    H = measured["height_m"]
    point_labels, split = None, None
    if args.contact_aware:
        # part fused hand contacts; each vertex then reads only heat solved for its own region
        point_labels, split = contact_split(mesh, body.regions)
    weld, before, after = welded_copy(mesh, args.weld * H)
    points = np.array([mesh.matrix_world @ v.co for v in mesh.data.vertices])
    # the same masked heat on the welded surface itself: the fallback for whole-mesh failure,
    # and the source for geometry the proxy does not cover
    weld_labels = {}
    weld_table, bone_names, weld_fill = region_weights(weld, rig, body.regions, H, labels_out=weld_labels)
    weld_rows, _ = sample_rows(weld, weld_table, points, source_labels=weld_labels.get("labels"),
                               point_labels=point_labels)
    stray_rows = {"welded_surface": keep_limb_territory(weld_rows, weld, weld_labels["territory"], points,
                                                        bone_names)} if weld_labels["territory"] else {}
    tried, voxel, fill, own_heat = [], None, weld_fill, 0
    rows = weld_rows
    if args.proxy == "voxel":
        for fraction in PROXY_VOXELS:
            heat_mesh = voxel_proxy(weld, fraction * H)
            proxy_labels = {}
            table, bone_names, proxy_fill = region_weights(heat_mesh, rig, body.regions, H, fraction * H,
                                                           labels_out=proxy_labels)
            ok = min(proxy_fill["pass_coverage"].values()) >= PASS_COVERAGE
            tried.append({"voxel_m": round(fraction * H, 4), "pass_coverage": proxy_fill["pass_coverage"],
                          "pieces_kept": heat_mesh["pieces_kept"],
                          "pieces_dropped": heat_mesh["pieces_dropped"]})
            if ok:
                voxel, fill = fraction * H, proxy_fill
                rows, gaps = sample_rows(heat_mesh, table, points, neighbours=4,
                                         source_labels=proxy_labels.get("labels"), point_labels=point_labels)
                if proxy_labels["territory"]:
                    stray_rows["proxy"] = keep_limb_territory(rows, heat_mesh, proxy_labels["territory"],
                                                              points, bone_names)
                # Geometry the proxy does not represent - slivers it dropped, sheets inside the
                # body it swallowed - keeps the heat weights solved on its own welded surface.
                # ...unless that solve left them with nothing (it can fail outright on a mesh with
                # many inner sheets): then the proxy's nearest heat beats an unweighted vertex.
                uncovered = (gaps > 2 * voxel) & (weld_rows.sum(1) > 0)
                rows[uncovered] = weld_rows[uncovered]
                own_heat = int(uncovered.sum())
                bpy.data.objects.remove(heat_mesh, do_unlink=True)
                break
            bpy.data.objects.remove(heat_mesh, do_unlink=True)
    emptied = (rows.sum(1) <= 1e-9) & (args.cross_limb == "reject")
    if emptied.any():
        # a vertex whose every weight was a stray limb's takes its mesh neighbours' weights
        edges, links = weld_graph(mesh, H)
        fill_unweighted(rows, np.concatenate([edges, links]), points)
    stray_rows["vertices_emptied_then_refilled"] = int(emptied.sum())
    rows /= np.maximum(rows.sum(1, keepdims=True), 1e-12)
    write_groups(mesh, rows, bone_names)
    bpy.data.objects.remove(weld, do_unlink=True)

    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE")          # keeps the transferred groups
    clean_weights(mesh, args.max_influences, args.min_weight)

    out_path = os.path.join(args.outdir, f"{args.name}_rigged.glb")
    export_glb([rig, mesh], out_path)

    weighting = {
        "method": ("automatic-heat-on-voxel-proxy-of-welded-copy" if voxel
                   else "automatic-heat-on-welded-copy"),
        "regions": "bone heat solved per region (arm geometry over every bone, the rest without "
                   "the arm chains; REGION_BONES), the seam between them smoothed",
        "contact_aware": bool(args.contact_aware),
        "arm_reach": body.regions.get("arm_reach", ARM_REACH),
        "contact_split": split,
        "contact": ("along the tracked arm column the arm region is its own cross-section pieces; "
                    "arm/body edges below the column's top are contacts, not seams (no smoothing "
                    "or shard glue across them), and each vertex reads only its own region's heat"
                    if args.contact_aware else None),
        "weld_distance_m": round(args.weld * H, 6),
        "welded_vertices": {"before": before, "after": after},
        "proxy_voxel_m": round(voxel, 5) if voxel else None,
        "proxy_sizes_tried": tried,
        "transfer": ("inverse-distance blend of the 4 nearest proxy vertices; vertices more than "
                     "2 voxels from the proxy keep the heat solved on the welded surface"
                     if voxel else "nearest welded vertex (welding only merges coincident vertices)"),
        "vertices_from_proxy": len(points) - own_heat if voxel else 0,
        "vertices_from_welded_surface_heat": own_heat if voxel else len(points),
        "welded_surface_pass_coverage": weld_fill["pass_coverage"],
        "cross_limb_mode": args.cross_limb,
        "cross_limb_rule": ("a vertex may not take weight from a limb chain it is not connected to on the "
                            "surface: per chain (LIMB_CHAINS) the heat mesh's surface holding at least "
                            f"{LIMB_SUPPORT} of it is split into connected pieces, weight off the main piece is "
                            "removed, and transferred rows keep a chain only where their nearest heat-mesh "
                            "vertex may carry it"),
        "cross_limb_welded_surface": weld_fill.get("cross_limb"),
        "cross_limb_rows_changed": stray_rows,
        **fill,
    }
    if fill["heat_unweighted"]:
        weighting["note"] = (f"bone heat left {fill['heat_unweighted']} of {fill['heat_mesh_vertices']} "
                             f"heat-mesh vertices without a solution (surface no allowed bone can see); "
                             f"{fill['filled_from_neighbours']} took their weighted mesh neighbours' heat "
                             f"weights and {fill['filled_from_nearest']} (pieces with no weighted vertex "
                             f"at all) their nearest weighted vertex's; no vertex uses distance weights")
    result = {
        "asset": args.name,
        "plan": "humanoid",
        "fit": "measured landmarks (_blender_rig_fit_humanoid20.py)",
        "out": out_path,
        "bones": {name: {"head": [round(float(v), 4) for v in bones[name][0]],
                         "tail": [round(float(v), 4) for v in bones[name][1]],
                         "parent": parent,
                         "deform": name != "root"} for name, parent in BONES},
        "measured": measured,
        "notes": notes,
        "weighting": weighting,
    }
    with open(os.path.join(args.outdir, f"{args.name}_fit_blender.json"), "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2)
    print("FIT_RESULT " + json.dumps({"out": out_path, "notes": notes, "weighting": weighting}))


# --------------------------------------------------------------------------------------------
# Blender: renders

VIEWS = {
    "front": (0.0, -1.0, 0.0),
    "three_quarter": (0.70711, -0.70711, 0.0),
    "side": (1.0, 0.0, 0.0),
}


def render_setup(size, height):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("render_world")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.44, 0.47, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    sun = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
    sun.data.energy = 5.0
    sun.rotation_euler = (math.radians(50), math.radians(8), math.radians(-35))
    scene.collection.objects.link(sun)
    bpy.ops.mesh.primitive_plane_add(size=height * 8, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.name = "ground"
    mat = bpy.data.materials.new("ground_mat")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.55, 0.52, 1)
    ground.data.materials.append(mat)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = height * 1.25
    return scene, cam


def aim(cam, direction, height, centre_x=0.0):
    d = Vector(direction)
    target = Vector((centre_x, 0.0, height * 0.5))
    cam.location = target + d * height * 4 + Vector((0, 0, height * 0.15))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()


def render_views(scene, cam, height, prefix, views=VIEWS):
    written = []
    for view, direction in views.items():
        aim(cam, direction, height)
        scene.render.filepath = f"{prefix}_{view}.png"
        bpy.ops.render.render(write_still=True)
        written.append(scene.render.filepath)
    return written


def set_visible(objs, visible):
    for o in objs:
        o.hide_render = not visible
        o.hide_set(not visible)


# Dominant-bone colours for the weight renders: left side warm, right side cool, trunk greys.
WEIGHT_COLOURS = {
    "hips": (0.55, 0.55, 0.55), "spine": (0.75, 0.75, 0.75), "chest": (0.95, 0.95, 0.95),
    "neck": (0.95, 0.85, 0.2), "head": (1.0, 1.0, 0.45),
    "shoulder.L": (0.55, 0.1, 0.1), "upper_arm.L": (0.9, 0.15, 0.1), "forearm.L": (1.0, 0.5, 0.1),
    "hand.L": (1.0, 0.75, 0.5), "shoulder.R": (0.1, 0.15, 0.55), "upper_arm.R": (0.1, 0.3, 0.95),
    "forearm.R": (0.1, 0.7, 1.0), "hand.R": (0.55, 0.9, 1.0),
    "thigh.L": (0.6, 0.1, 0.6), "shin.L": (0.85, 0.35, 0.85), "foot.L": (1.0, 0.7, 1.0),
    "thigh.R": (0.05, 0.45, 0.2), "shin.R": (0.15, 0.75, 0.35), "foot.R": (0.6, 1.0, 0.6),
    "root": (0.0, 0.0, 0.0),
}


def flat_material(name, colour=None, attribute=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = False
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.65
    if attribute:
        node = mat.node_tree.nodes.new("ShaderNodeAttribute")
        node.attribute_name = attribute
        mat.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = colour
    return mat


def swap_materials(obj, materials):
    previous = [slot.material for slot in obj.material_slots]
    for slot, mat in zip(obj.material_slots, materials):
        slot.material = mat
    return previous


def dominant_bone_colours(body):
    names = {g.index: g.name for g in body.vertex_groups}
    attr = body.data.color_attributes.new("dominant_bone", "FLOAT_COLOR", "POINT")
    for v in body.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        colour = WEIGHT_COLOURS.get(names[best.group], (1, 0, 1)) if best else (1, 0, 1)
        attr.data[v.index].color = (*colour, 1.0)


def run_render(args):
    os.makedirs(args.outdir, exist_ok=True)
    reset_scene()
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.render.fps_base = 1.0
    rigged_objs = import_meshes(args.rigged)
    rig = next(o for o in rigged_objs if o.type == "ARMATURE")
    body = next(o for o in rigged_objs if o.type == "MESH")
    height = max((body.matrix_world @ Vector(c)).z for c in body.bound_box)
    scene, cam = render_setup(args.size, height)
    manifest = {"bind": [], "reference": [], "weights": [], "clips": {}}

    rig.animation_data_clear()
    set_visible(rigged_objs, True)
    manifest["bind"] = render_views(scene, cam, height, os.path.join(args.outdir, "bind"))

    if args.reference:
        set_visible(rigged_objs, False)
        ref_objs = import_meshes(args.reference)
        manifest["reference"] = render_views(scene, cam, height, os.path.join(args.outdir, "lod0"))
        for o in ref_objs:
            bpy.data.objects.remove(o, do_unlink=True)
        if args.baseline:
            base_objs = import_meshes(args.baseline)
            manifest["baseline"] = render_views(scene, cam, height, os.path.join(args.outdir, "baseline"))
            for o in base_objs:
                bpy.data.objects.remove(o, do_unlink=True)
        set_visible(rigged_objs, True)

    slots = len(body.material_slots)
    clay = [flat_material("clay", (0.42, 0.40, 0.37, 1.0))] * slots
    dominant_bone_colours(body)
    textured = swap_materials(body, [flat_material("weights", attribute="dominant_bone")] * slots)
    manifest["weights"] = render_views(scene, cam, height, os.path.join(args.outdir, "weights"),
                                       {"front": VIEWS["front"], "side": VIEWS["side"],
                                        "back": (0.0, 1.0, 0.0)})
    swap_materials(body, textured)

    modifier = next(m for m in body.modifiers if m.type == "ARMATURE")
    rest = {b.name: np.array(b.matrix_local) for b in rig.data.bones}
    for clip in args.clips:
        clip_objs = import_meshes(clip)
        clip_rig = next(o for o in clip_objs if o.type == "ARMATURE")
        # The game drives the body's skeleton with the clip's tracks by bone name; here the
        # body is driven by the clip's own armature, which is the same thing only if the two
        # rests agree, so that is checked rather than assumed.
        rest_err = max(float(np.abs(np.array(b.matrix_local) - rest[b.name]).max())
                       for b in clip_rig.data.bones if b.name in rest)
        missing = sorted(set(rest) - {b.name for b in clip_rig.data.bones})
        for o in clip_objs:
            if o is not clip_rig:
                bpy.data.objects.remove(o, do_unlink=True)
        clip_rig.hide_render = True
        modifier.object = clip_rig
        action = clip_rig.animation_data.action if clip_rig.animation_data else None
        start, end = (int(action.frame_range[0]), int(action.frame_range[1])) if action else (0, 0)
        stem = os.path.splitext(os.path.basename(clip))[0]
        frames = sorted({int(round(start + (end - start) * k / max(args.frames - 1, 1)))
                         for k in range(args.frames)})
        written = []
        for f in frames:
            scene.frame_set(f)
            prefix = os.path.join(args.outdir, f"{stem}_f{f:03d}")
            written += render_views(scene, cam, height, prefix,
                                    {"front": VIEWS["front"], "three_quarter": VIEWS["three_quarter"]})
            # untextured, both faces drawn: tears and stretched triangles are plain to see
            swap_materials(body, clay)
            written += render_views(scene, cam, height, prefix + "_clay",
                                    {"front": VIEWS["front"], "three_quarter": VIEWS["three_quarter"],
                                     "side": VIEWS["side"]})
            swap_materials(body, textured)
        manifest["clips"][stem] = {"frames": frames, "frame_range": [start, end],
                                   "rest_vs_body_rest_max": round(rest_err, 6),
                                   "bones_missing_in_clip": missing, "renders": written}
        modifier.object = rig
        bpy.data.objects.remove(clip_rig, do_unlink=True)
    with open(os.path.join(args.outdir, "render_manifest.json"), "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    print("RENDER_RESULT " + json.dumps({k: (len(v) if isinstance(v, (list, dict)) else v)
                                         for k, v in manifest.items()}))


def main():
    args = parse_args()
    if args.mode == "fit":
        run_fit(args)
    else:
        run_render(args)


if __name__ == "__main__" and bpy is not None:
    main()
