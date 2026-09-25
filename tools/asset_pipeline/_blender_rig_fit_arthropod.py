"""Fit an arthropod skeleton to a many-legged creature's own mesh, bind it, and stage the result.

The cave spider was rigged on the quadruped plan: four legs at bounding-box fractions, distance
weights. Its mesh has ten long appendages radiating from the body (a front pair of pedipalps and
four pairs of walking legs) and two short chelicerae hanging under the front, and it was
reconstructed from a three-quarter concept, so the whole body is pitched about 30 degrees nose
down. This tool measures the mesh instead of assuming a plan:

1. Voxelise the LOD0, fill it, and trace a centre-line skeleton (TEASAR: Dijkstra through the
   solid from its deepest voxel, with a cost that keeps paths on the medial axis). Each long
   branch is an appendage; its far end is its tip.
2. Level the mesh with ONE rigid transform, recorded: the ground is the supporting plane of the
   appendage tips (the tips rest on it), yaw comes from the tips' left/right mirror symmetry, and
   the front (where the legs attach) faces glTF +Z. The footprint is centred on the origin.
3. Split the thick body core (a morphological opening) at its waist into cephalothorax and
   abdomen, and place each appendage's joints on its centre-line at its bends: coxa, knee, ankle,
   tip.
4. Bind with Blender's automatic (bone heat) weights computed on a welded copy of the mesh
   (the generated mesh is split at every UV seam), drop the heat weights the measured anatomy
   rules out (femur weight on carapace vertices far from any coxa, a segment's weight well past
   its own stretch of the limb) and give that weight back along the limb, then copy the weights
   to the original vertices by nearest vertex. --weights heat skips the anatomy step.

Bone plan "arthropod_v1" (glTF: +Y up, +Z forward, +X = the creature's left = suffix .L):
  root                      ground under the footprint centre, not deforming (game root)
  body                      pivot at the waist (pedicel), not deforming; parent of the two below
  cephalothorax, abdomen    waist to the front of the carapace / to the rear of the abdomen
  palp_{femur,tibia,tarsus}.{L,R}        the front-most long pair (pedipalps), when there are 5
  leg{1..4}_{femur,tibia,tarsus}.{L,R}   walking legs, numbered front to back
  chelicera.{L,R}, fang.{L,R}  the shorter front pair hanging below the head, base and fang
Chains hang off the cephalothorax: femur = coxa to knee, tibia = knee to ankle, tarsus = ankle
to tip.

Modes (all outputs go under --outdir, which defaults to assets/_staging/rigs/<asset>/):
  fit     measure, level, build, bind, export rigged/<id>_rigged.glb + skeleton/rig/fit reports
  motion  write the six procedural motion sources (idle, walk, run, attack, hit, death) for the
          staged rig, in _make_creature_motion.py's format, for _blender_anim_creature.py
  verify  measure the staged rig and clips as files: joint fit, weights, bind vs LOD0, tracks,
          stretch under every clip
  render  render bind vs LOD0, weights, and clip frames, evaluating the GLBs the way the game
          plays them (tracks matched to joints by name, all tracks played)

Run inside Blender:
  blender --background --factory-startup --python _blender_rig_fit_arthropod.py -- --mode fit
The full sequence is _blender_rig_fit_arthropod_build.py.
"""
import argparse
import heapq
import json
import math
import os
import struct
import sys

import numpy as np

try:
    import bpy
    from mathutils import Matrix, Vector
    from mathutils.bvhtree import BVHTree
    from mathutils.kdtree import KDTree
except ImportError:          # the numpy half is importable outside Blender
    bpy = None

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.path.join(REPO, "assets")
PLAN = "arthropod_v1"
KINDS = ("idle", "walk", "run", "attack", "hit", "death")
# Seconds per clip: the same lengths as the clips this rig replaces (_build_creature_anims.py).
DURATIONS = {"idle": 4.0, "walk": 1.2, "run": 0.8, "attack": 0.9, "hit": 0.55, "death": 1.9}
FPS = 30

# glTF (x, y, z) -> Blender (x, -z, y)
GL_TO_BL = np.array([[1.0, 0, 0], [0, 0, -1.0], [0, 1.0, 0]])


# ------------------------------------------------------------------------------------------------
# glTF as data (numpy only), used by verify and render so they read exactly what a game loads

def read_glb(path):
    with open(path, "rb") as handle:
        magic, _version, _length = struct.unpack("<4sII", handle.read(12))
        if magic != b"glTF":
            raise ValueError(f"not a GLB: {path}")
        size, _kind = struct.unpack("<I4s", handle.read(8))
        gltf = json.loads(handle.read(size))
        blob = b""
        head = handle.read(8)
        if len(head) == 8:
            size, _kind = struct.unpack("<I4s", head)
            blob = handle.read(size)
    return gltf, blob


def accessor(gltf, blob, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
    dtype = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16,
             5125: np.uint32, 5126: np.float32}[acc["componentType"]]
    count = acc["count"]
    offset = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    item = np.dtype(dtype).itemsize * comps
    stride = view.get("byteStride", 0)
    if stride and stride != item:
        raw = np.frombuffer(blob, np.uint8, stride * (count - 1) + item, offset)
        raw = np.lib.stride_tricks.as_strided(raw, (count, item), (stride, 1))
        data = np.frombuffer(raw.copy().tobytes(), dtype).reshape(count, comps)
    else:
        data = np.frombuffer(blob, dtype, count * comps, offset).reshape(count, comps)
    if acc.get("normalized") and dtype != np.float32:
        data = data.astype(np.float64) / np.iinfo(dtype).max
    return data


def quat_to_mat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs_matrix(t, r, s):
    m = np.eye(4)
    m[:3, :3] = quat_to_mat(r) * np.asarray(s)
    m[:3, 3] = t
    return m


def node_trs(node):
    if "matrix" in node:
        m = np.array(node["matrix"]).reshape(4, 4).T
        t = m[:3, 3].copy()
        s = np.linalg.norm(m[:3, :3], axis=0)
        rot = m[:3, :3] / s
        w = math.sqrt(max(0.0, 1 + rot[0, 0] + rot[1, 1] + rot[2, 2])) / 2
        w = max(w, 1e-9)
        q = [(rot[2, 1] - rot[1, 2]) / (4 * w), (rot[0, 2] - rot[2, 0]) / (4 * w),
             (rot[1, 0] - rot[0, 1]) / (4 * w), w]
        return t, np.array(q), s
    return (np.array(node.get("translation", [0.0, 0, 0]), float),
            np.array(node.get("rotation", [0.0, 0, 0, 1]), float),
            np.array(node.get("scale", [1.0, 1, 1]), float))


def world_matrices(gltf, local):
    """local: list of 4x4 per node. Returns world 4x4 per node."""
    parent = {}
    for index, node in enumerate(gltf["nodes"]):
        for child in node.get("children", []):
            parent[child] = index
    cache = {}

    def world(i):
        if i not in cache:
            cache[i] = world(parent[i]) @ local[i] if i in parent else local[i]
        return cache[i]
    return [world(i) for i in range(len(gltf["nodes"]))]


def slerp(a, b, u):
    dot = float(np.dot(a, b))
    if dot < 0:
        b, dot = -b, -dot
    if dot > 0.9995:
        q = a + (b - a) * u
    else:
        theta = math.acos(dot)
        q = (math.sin((1 - u) * theta) * a + math.sin(u * theta) * b) / math.sin(theta)
    return q / np.linalg.norm(q)


class Clip:
    """One glTF animation, sampled per node NAME (the game's Retarget matches by bone name)."""

    def __init__(self, path):
        self.path = path
        gltf, blob = read_glb(path)
        animation = gltf["animations"][0]
        self.tracks = {}
        self.duration = 0.0
        for channel in animation["channels"]:
            sampler = animation["samplers"][channel["sampler"]]
            times = accessor(gltf, blob, sampler["input"]).astype(np.float64).ravel()
            values = accessor(gltf, blob, sampler["output"]).astype(np.float64)
            interp = sampler.get("interpolation", "LINEAR")
            if interp == "CUBICSPLINE":
                values = values[1::3]
            name = gltf["nodes"][channel["target"]["node"]].get("name")
            self.tracks.setdefault(name, {})[channel["target"]["path"]] = (times, values, interp)
            self.duration = max(self.duration, float(times[-1]))

    def sample(self, name, path, t, fallback):
        track = self.tracks.get(name, {}).get(path)
        if track is None:
            return fallback
        times, values, interp = track
        if t <= times[0]:
            return values[0]
        if t >= times[-1]:
            return values[-1]
        k = int(np.searchsorted(times, t, side="right")) - 1
        if interp == "STEP":
            return values[k]
        u = (t - times[k]) / max(times[k + 1] - times[k], 1e-12)
        if path == "rotation":
            return slerp(values[k], values[k + 1], u)
        return values[k] + (values[k + 1] - values[k]) * u


class SkinnedGLB:
    """A rigged GLB evaluated by the glTF skinning rule: v' = sum_j w_j * World(j) * IBM(j) * v."""

    def __init__(self, path):
        self.path = path
        gltf, blob = read_glb(path)
        self.gltf = gltf
        self.names = [n.get("name", str(i)) for i, n in enumerate(gltf["nodes"])]
        self.rest = [node_trs(n) for n in gltf["nodes"]]
        skin = gltf["skins"][0]
        self.joints = skin["joints"]
        self.joint_names = [self.names[j] for j in self.joints]
        self.ibm = accessor(gltf, blob, skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        mesh_node = next(n for n in gltf["nodes"] if "mesh" in n and "skin" in n)
        prim = gltf["meshes"][mesh_node["mesh"]]["primitives"]
        if len(prim) != 1:
            raise ValueError("expected one primitive")
        attrs = prim[0]["attributes"]
        self.positions = accessor(gltf, blob, attrs["POSITION"]).astype(np.float64)
        self.uv = accessor(gltf, blob, attrs["TEXCOORD_0"]).astype(np.float64) if "TEXCOORD_0" in attrs else None
        self.jidx = accessor(gltf, blob, attrs["JOINTS_0"]).astype(np.int64)
        weights = accessor(gltf, blob, attrs["WEIGHTS_0"]).astype(np.float64)
        self.weights = weights / np.maximum(weights.sum(1, keepdims=True), 1e-12)
        self.tris = accessor(gltf, blob, prim[0]["indices"]).astype(np.int64).reshape(-1, 3)

    def local_matrices(self, clip=None, t=0.0):
        local = []
        for i, (tr, rot, sc) in enumerate(self.rest):
            if clip is not None:
                name = self.names[i]
                if i in self.joints:     # the game plays tracks only onto skeleton bones
                    tr = clip.sample(name, "translation", t, tr)
                    rot = clip.sample(name, "rotation", t, rot)
                    sc = clip.sample(name, "scale", t, sc)
            local.append(trs_matrix(tr, rot, sc))
        return local

    def joint_world(self, clip=None, t=0.0):
        world = world_matrices(self.gltf, self.local_matrices(clip, t))
        return np.array([world[j] for j in self.joints])

    def pose(self, clip=None, t=0.0):
        mats = self.joint_world(clip, t) @ self.ibm
        homo = np.c_[self.positions, np.ones(len(self.positions))]
        out = np.zeros_like(self.positions)
        for k in range(self.jidx.shape[1]):
            m = mats[self.jidx[:, k]]
            out += self.weights[:, k:k + 1] * np.einsum("nij,nj->ni", m, homo)[:, :3]
        return out


def static_positions(path):
    gltf, blob = read_glb(path)
    local = [trs_matrix(*node_trs(n)) for n in gltf["nodes"]]
    world = world_matrices(gltf, local)
    points = []
    for index, node in enumerate(gltf["nodes"]):
        if "mesh" not in node:
            continue
        for prim in gltf["meshes"][node["mesh"]]["primitives"]:
            v = accessor(gltf, blob, prim["attributes"]["POSITION"]).astype(np.float64)
            points.append((world[index] @ np.c_[v, np.ones(len(v))].T).T[:, :3])
    return np.concatenate(points)


def fibonacci_directions(n=64):
    k = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * k / n)
    theta = math.pi * (1 + 5 ** 0.5) * k
    return np.c_[np.cos(theta) * np.sin(phi), np.sin(theta) * np.sin(phi), np.cos(phi)]


def enclosed_fraction(bvh, point, directions=None):
    """Share of rays from the point that meet the surface: 1.0 inside a closed or shelled body."""
    directions = fibonacci_directions() if directions is None else directions
    p = Vector(point)
    hits = sum(1 for d in directions if bvh.ray_cast(p, Vector(d))[0] is not None)
    return hits / len(directions)


def lod0_triangles(path):
    gltf, blob = read_glb(path)
    return int(sum(accessor(gltf, blob, p["indices"]).size // 3
                   for m in gltf["meshes"] for p in m["primitives"]))


# ------------------------------------------------------------------------------------------------
# Measuring the body (numpy only)

def dilate(x, times=1):
    for _ in range(times):
        y = x.copy()
        y[1:] |= x[:-1]
        y[:-1] |= x[1:]
        y[:, 1:] |= x[:, :-1]
        y[:, :-1] |= x[:, 1:]
        y[:, :, 1:] |= x[:, :, :-1]
        y[:, :, :-1] |= x[:, :, 1:]
        x = y
    return x


def erode(x, times=1):
    return ~dilate(~x, times)


class Voxels:
    """The mesh as a filled voxel solid, with each voxel's erosion depth (distance to surface)."""

    def __init__(self, tris, h):
        self.h = h
        corners = tris.reshape(-1, 3)
        self.lo = corners.min(0) - 3 * h
        shape = tuple(int(v) for v in np.ceil((corners.max(0) + 3 * h - self.lo) / h).astype(int) + 1)
        a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
        longest = np.maximum.reduce([np.linalg.norm(b - a, axis=1), np.linalg.norm(c - b, axis=1),
                                     np.linalg.norm(a - c, axis=1)])
        steps = np.maximum(1, np.ceil(longest / (0.4 * h)).astype(int))
        surface = np.zeros(shape, bool)
        for k in np.unique(steps):
            sel = steps == k
            u, v = np.meshgrid(np.arange(k + 1), np.arange(k + 1))
            keep = (u + v) <= k
            u, v = u[keep] / k, v[keep] / k
            p = (a[sel][:, None] * (1 - u - v)[None, :, None] + b[sel][:, None] * u[None, :, None]
                 + c[sel][:, None] * v[None, :, None]).reshape(-1, 3)
            ijk = np.floor((p - self.lo) / h).astype(int)
            surface[ijk[:, 0], ijk[:, 1], ijk[:, 2]] = True
        # Close one-voxel holes, flood the outside from the grid border, then undo the closing.
        closed = dilate(surface)
        outside = np.zeros(shape, bool)
        outside[[0, -1], :, :] = True
        outside[:, [0, -1], :] = True
        outside[:, :, [0, -1]] = True
        outside &= ~closed
        while True:
            grown = dilate(outside) & ~closed
            if np.array_equal(grown, outside):
                break
            outside = grown
        self.solid = erode(~outside) | surface
        self.depth = np.zeros(shape, np.int16)
        current, d = self.solid.copy(), 0
        while current.any():
            d += 1
            self.depth[current] = d
            current = erode(current)

    def centre(self, ijk):
        return self.lo + (np.asarray(ijk) + 0.5) * self.h

    def lookup(self, points, grid):
        ijk = np.floor((np.asarray(points) - self.lo) / self.h).astype(int)
        inside = ((ijk >= 0) & (ijk < np.array(grid.shape))).all(-1)
        out = np.zeros(len(ijk), grid.dtype)
        out[inside] = grid[tuple(ijk[inside].T)]
        return out


def dijkstra(neighbours, steps, source, cost=None):
    n = len(neighbours)
    dist = [math.inf] * n
    pred = [-1] * n
    dist[source] = 0.0
    queue = [(0.0, source)]
    while queue:
        d, u = heapq.heappop(queue)
        if d > dist[u]:
            continue
        cu = cost[u] if cost is not None else 1.0
        for v, step in zip(neighbours[u], steps[u]):
            nd = d + step * ((cu + cost[v]) * 0.5 if cost is not None else 1.0)
            if nd < dist[v]:
                dist[v] = nd
                pred[v] = u
                heapq.heappush(queue, (nd, v))
    return np.array(dist), np.array(pred)


def centre_line_branches(vox):
    """TEASAR skeleton: every branch from a far tip back to the deepest voxel, tip first."""
    idx = np.argwhere(vox.solid)
    lin = np.full(vox.solid.shape, -1, np.int64)
    lin[tuple(idx.T)] = np.arange(len(idx))
    offsets = np.array([(i, j, k) for i in (-1, 0, 1) for j in (-1, 0, 1) for k in (-1, 0, 1)
                        if (i, j, k) != (0, 0, 0)])
    lengths = np.linalg.norm(offsets, axis=1)
    table = np.full((len(idx), 26), -1, np.int64)
    for j, off in enumerate(offsets):
        q = idx + off
        ok = ((q >= 0) & (q < np.array(vox.solid.shape))).all(1)
        table[ok, j] = lin[tuple(q[ok].T)]
    neighbours, steps = [], []
    for row in table:
        keep = row >= 0
        neighbours.append(row[keep].tolist())
        steps.append(lengths[keep].tolist())
    depth = vox.depth[tuple(idx.T)].astype(np.float64)
    root = int(np.argmax(depth))
    geodesic, _ = dijkstra(neighbours, steps, root)
    penalty = (5000.0 * (1.0 - depth / depth.max()) ** 16 + 1.0).tolist()
    _, pred = dijkstra(neighbours, steps, root, penalty)
    reachable = np.isfinite(geodesic)
    gmax = geodesic[reachable].max()
    covered = ~reachable
    branches, junctions = [], []
    for _ in range(64):
        g = np.where(covered, -1.0, geodesic)
        tip = int(np.argmax(g))
        if g[tip] < 0.15 * gmax:
            break
        path = [tip]
        while path[-1] != root:
            path.append(int(pred[path[-1]]))
        path = np.array(path)
        branches.append(path)
        # Where this branch meets the skeleton already traced: a second extremity on the same
        # limb (a curled or split leg end) meets it near the tip, a new limb near the body.
        hits = np.flatnonzero(covered[path])
        junctions.append(int(hits[0]) if len(hits) else len(path) - 1)
        radius = depth[path] * 1.3 + 1.5
        for start in range(0, len(path), 16):
            pts = idx[path[start:start + 16]]
            d2 = ((idx[None, :, :] - pts[:, None, :]) ** 2).sum(-1)
            covered |= (d2 <= (radius[start:start + 16, None] ** 2)).any(0)
    return idx, depth, root, branches, junctions


def arclength(points):
    return np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))]


def resample(points, spacing):
    s = arclength(points)
    if s[-1] < spacing:
        return points.copy()
    t = np.linspace(0.0, s[-1], int(round(s[-1] / spacing)) + 1)
    return np.stack([np.interp(t, s, points[:, k]) for k in range(3)], axis=1)


def smooth(points, window):
    out = points.copy()
    half = window // 2
    for i in range(1, len(points) - 1):
        lo, hi = max(0, i - half), min(len(points), i + half + 1)
        out[i] = points[lo:hi].mean(0)
    return out


def rotation_between(a, b):
    a = a / np.linalg.norm(a)
    b = b / np.linalg.norm(b)
    v = np.cross(a, b)
    s, c = np.linalg.norm(v), float(a @ b)
    if s < 1e-12:
        return np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k * ((1 - c) / s ** 2)


def rot_z(angle):
    c, s = math.cos(angle), math.sin(angle)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1.0]])


def support_plane(tips, up):
    """The plane the tips rest on: every tip on or above it, the tips' centroid over the triangle
    of its three contacts, least mean hover. Returns (unit normal, contact indices, hovers)."""
    best = None
    centroid = tips.mean(0)
    n = len(tips)
    for i in range(n):
        for j in range(i + 1, n):
            for k in range(j + 1, n):
                normal = np.cross(tips[j] - tips[i], tips[k] - tips[i])
                length = np.linalg.norm(normal)
                if length < 1e-9:
                    continue
                normal /= length
                if normal @ up < 0:
                    normal = -normal
                if normal @ up < math.cos(math.radians(60)):
                    continue
                hover = (tips - tips[i]) @ normal
                if (hover < -1e-6).any():
                    continue
                # stability: the centroid, dropped onto the plane, inside the contact triangle
                p = centroid - ((centroid - tips[i]) @ normal) * normal
                tri = (tips[i], tips[j], tips[k])
                signs = [np.cross(tri[(m + 1) % 3] - tri[m], p - tri[m]) @ normal for m in range(3)]
                if not (all(s >= 0 for s in signs) or all(s <= 0 for s in signs)):
                    continue
                score = hover.mean()
                if best is None or score < best[0]:
                    best = (score, normal, (i, j, k), hover)
    if best is None:
        raise RuntimeError("no stable supporting plane through the appendage tips")
    return best[1], best[2], best[3]


def mirror_yaw(points):
    """Yaw (about +Z) that makes the point set most nearly mirror-symmetric in X."""
    centred = points - points.mean(0)
    best = None
    for deg in np.arange(-90.0, 90.0, 0.25):
        p = centred @ rot_z(math.radians(deg)).T
        p = p - np.array([p[:, 0].mean(), 0, 0])
        mirrored = p * np.array([-1.0, 1, 1])
        d = np.sqrt(((mirrored[:, None, :] - p[None, :, :]) ** 2).sum(-1)).min(1).mean()
        if best is None or d < best[0]:
            best = (d, deg)
    return math.radians(best[1]), best[0]


def segment_error(line, a, b):
    """Squared distances of line[a..b] from the straight segment line[a] -> line[b]."""
    p = line[a:b + 1] - line[a]
    d = line[b] - line[a]
    length = float(d @ d)
    if length < 1e-12:
        return float((p ** 2).sum())
    t = np.clip(p @ d / length, 0.0, 1.0)
    return float(((p - np.outer(t, d)) ** 2).sum())


def polyline_fit(line, segments, min_share=0.15):
    """Interior break points of the `segments`-piece polyline (ends fixed at the line's ends) that
    follows the centre-line most closely: each bone lies along the limb it moves. Every piece
    covers at least min_share of the length, so no bone collapses."""
    s = arclength(line)
    n = len(line)
    gap = min_share * s[-1]
    best = None
    if segments == 2:
        for i in range(1, n - 1):
            if s[i] < gap or s[-1] - s[i] < gap:
                continue
            e = segment_error(line, 0, i) + segment_error(line, i, n - 1)
            if best is None or e < best[0]:
                best = (e, (i,))
    else:
        err = {}
        for i in range(1, n - 2):
            for j in range(i + 1, n - 1):
                if s[i] < gap or s[j] - s[i] < gap or s[-1] - s[j] < gap:
                    continue
                if (0, i) not in err:
                    err[(0, i)] = segment_error(line, 0, i)
                e = err[(0, i)] + segment_error(line, i, j) + segment_error(line, j, n - 1)
                if best is None or e < best[0]:
                    best = (e, (i, j))
    return best[1], math.sqrt(best[0] / n)


def measure(tris_bl):
    """Everything the rig needs from the LOD0 triangles (Blender frame: +Z up, -Y forward)."""
    extent = tris_bl.reshape(-1, 3).max(0) - tris_bl.reshape(-1, 3).min(0)
    h = float(extent.max()) / 120.0
    vox = Voxels(tris_bl, h)
    idx, depth, root, branches, junctions = centre_line_branches(vox)
    centres = vox.centre(idx)

    # Leg thickness sets the opening radius that separates the body core from the appendages.
    tip_depths = np.concatenate([depth[b[:max(3, int(0.4 * len(b)))]] for b in branches])
    k = max(2, int(round(2.0 * float(np.median(tip_depths)))))
    core = vox.depth > k
    body = dilate(core, k) & vox.solid

    info = []
    for number, (path, junction) in enumerate(zip(branches, junctions)):
        in_body = body[tuple(idx[path].T)]
        attach = int(np.argmax(in_body)) if in_body.any() else len(path) - 1
        line = centres[path[:attach + 1]][::-1]              # attach -> tip
        own = min(attach, junction)
        info.append({"branch": number, "voxels": path, "attach": attach, "line": line,
                     "outside_length": float(arclength(line)[-1]),
                     "own_length": float(arclength(centres[path[:own + 1]])[-1]),
                     "tip": centres[path[0]], "attach_point": centres[path[attach]]})
    longest = max(b["outside_length"] for b in info)
    # An appendage is a branch that is long on its own; one that joins another limb near its tip
    # (a curled leg end) is a spur of that limb, not a limb.
    long_branches = [b for b in info
                     if b["outside_length"] >= 0.5 * longest and b["own_length"] >= 0.25 * longest]
    short_branches = [b for b in info if b not in long_branches and b["own_length"] >= 0.1 * longest
                      and b["outside_length"] >= 0.04]
    spurs = [b["branch"] for b in info if b not in long_branches and b not in short_branches]

    # Level: rotate the tips' supporting plane to horizontal.
    tips = np.array([b["tip"] for b in long_branches])
    up = np.array([0, 0, 1.0])
    normal, contacts, _ = support_plane(tips, up)
    r_level = rotation_between(normal, up)
    # Yaw: mirror symmetry of the tips and attach points; front is where the legs attach.
    sym = np.r_[tips, np.array([b["attach_point"] for b in long_branches])] @ r_level.T
    yaw, sym_err = mirror_yaw(sym)
    r = rot_z(yaw) @ r_level
    body_pts = vox.centre(np.argwhere(body)) @ r.T
    attach_pts = np.array([b["attach_point"] for b in long_branches]) @ r.T
    if attach_pts[:, 1].mean() > body_pts[:, 1].mean():      # legs must attach toward -Y (front)
        yaw += math.pi
        r = rot_z(math.pi) @ r
    # Translate: footprint centred, the tips' plane at z = 0.
    verts = tris_bl.reshape(-1, 3) @ r.T
    lo, hi = verts.min(0), verts.max(0)
    ground = float((tips @ r.T)[list(contacts), 2].mean())
    offset = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -ground])
    transform = np.eye(4)
    transform[:3, :3] = r
    transform[:3, 3] = offset

    def xf(p):
        return np.asarray(p) @ r.T + offset

    body_pts = xf(vox.centre(np.argwhere(body)))
    for b in info:
        b["line"] = xf(b["line"])
        b["tip"] = xf(b["tip"])
        b["attach_point"] = xf(b["attach_point"])

    def inside(points):
        back = (np.asarray(points) - offset) @ r          # undo the rigid transform
        return vox.lookup(back, vox.solid).astype(bool)

    # Distance (voxel steps) from the body core, for telling limb surface from body surface.
    body_steps = np.full(body.shape, 255, np.uint8)
    grown = dilate(body, 1)
    body_steps[grown] = 0
    for step in range(1, 30):
        nxt = dilate(grown, 1)
        body_steps[nxt & ~grown] = step
        grown = nxt

    def body_distance(points):
        back = (np.asarray(points) - offset) @ r
        return vox.lookup(back, body_steps).astype(float) * h

    # Two body parts: 2-means on the body core (the abdomen may ride higher than the carapace, so
    # the split follows the line between the two centroids, not a horizontal axis).
    centre_a = body_pts[np.argmin(body_pts[:, 1])]
    centre_b = body_pts[np.argmax(body_pts[:, 1])]
    for _ in range(50):
        near_a = (np.linalg.norm(body_pts - centre_a, axis=1) < np.linalg.norm(body_pts - centre_b, axis=1))
        new_a, new_b = body_pts[near_a].mean(0), body_pts[~near_a].mean(0)
        if np.allclose(new_a, centre_a) and np.allclose(new_b, centre_b):
            break
        centre_a, centre_b = new_a, new_b
    ceph_centre, abdomen_centre = (centre_a, centre_b) if centre_a[1] < centre_b[1] else (centre_b, centre_a)
    axis = abdomen_centre - ceph_centre
    span = float(np.linalg.norm(axis))
    axis /= span
    # Waist: the thinnest slab across that line, between the two centroids.
    along = (body_pts - ceph_centre) @ axis
    stations = np.arange(0.2 * span, 0.8 * span + 1e-9, h)
    counts = np.array([(np.abs(along - s) <= h).sum() for s in stations], float)
    counts = np.convolve(counts, np.ones(3) / 3, mode="same")
    waist = float(stations[int(np.argmin(counts[1:-1])) + 1])
    slab = np.abs(along - waist) <= h
    pedicel = body_pts[slab].mean(0)

    def extend_inside(start, through, factor):
        """A point on the ray start->through, `factor` times as far, pulled back into the solid."""
        for f in np.linspace(factor, 1.0, 25):
            p = start + (through - start) * f
            if inside([p])[0]:
                return p
        return through

    ceph_tail = extend_inside(pedicel, ceph_centre, 1.7)
    abdomen_tail = extend_inside(pedicel, abdomen_centre, 1.7)

    # Appendages: the long branches, paired by side, front to back. Each chain's joints are the
    # break points of the 3-piece polyline that best follows its centre-line.
    chains = []
    spacing = h
    for side, sign in (("L", 1.0), ("R", -1.0)):
        mine = [b for b in long_branches if np.sign(b["attach_point"][0]) == sign]
        mine.sort(key=lambda b: b["attach_point"][1])
        names = ["palp"] + [f"leg{i}" for i in range(1, 5)] if len(mine) == 5 else \
            [f"leg{i}" for i in range(1, len(mine) + 1)]
        for order, (name, b) in enumerate(zip(names, mine)):
            line = smooth(resample(b["line"], spacing), 5)
            line[0], line[-1] = b["line"][0], b["line"][-1]
            (knee, ankle), rms = polyline_fit(line, 3)
            s = arclength(line)
            radius = float(np.median(depth[b["voxels"][:b["attach"] + 1]])) * h
            chains.append({
                "chain": f"{name}.{side}", "kind": "palp" if name == "palp" else "leg",
                "name": name, "side": side, "order": order, "branch": b["branch"],
                "coxa": line[0], "knee": line[knee], "ankle": line[ankle], "tip": line[-1],
                "length": float(s[-1]), "knee_at": float(s[knee] / s[-1]),
                "ankle_at": float(s[ankle] / s[-1]), "polyline_rms_m": rms, "radius_m": radius,
                "line": line,
            })
    counts_by_side = {side: sum(1 for c in chains if c["side"] == side) for side in ("L", "R")}

    # Chelicerae: shorter branches rooted on the cephalothorax side of the waist that hang below
    # their root, one a side, two pieces each (base and fang).
    fangs = []
    for side, sign in (("L", 1.0), ("R", -1.0)):
        cand = [b for b in short_branches
                if np.sign(b["attach_point"][0]) == sign
                and (b["attach_point"] - pedicel) @ axis < 0 and b["tip"][2] < b["attach_point"][2]]
        if cand:
            b = max(cand, key=lambda c: c["outside_length"])
            line = smooth(resample(b["line"], spacing), 5)
            line[0], line[-1] = b["line"][0], b["line"][-1]
            (mid,), rms = polyline_fit(line, 2, min_share=0.25)
            fangs.append({"chain": f"chelicera.{side}", "side": side, "branch": b["branch"],
                          "base": line[0], "mid": line[mid], "tip": line[-1],
                          "radius_m": float(np.median(depth[b["voxels"][:b["attach"] + 1]])) * h,
                          "length": b["outside_length"], "polyline_rms_m": rms, "line": line})

    return {
        "voxel_size_m": h, "opening_radius_voxels": k, "transform": transform,
        "support_normal_before": normal, "support_contacts": contacts,
        "mirror_error_m": sym_err, "yaw_deg": math.degrees(yaw),
        "branches": info, "long_count": len(long_branches), "short_count": len(short_branches),
        "spur_branches": spurs, "counts_by_side": counts_by_side,
        "pedicel": pedicel, "ceph_centre": ceph_centre, "abdomen_centre": abdomen_centre,
        "ceph_tail": ceph_tail, "abdomen_tail": abdomen_tail,
        "chains": chains, "fangs": fangs, "inside": inside, "body_distance": body_distance,
        "limb_radius_m": 0.5 * k * h, "body_points": body_pts,
    }


def bone_plan(fit):
    """(name, head, tail, parent, deform, role) in the Blender frame, from a measure() result."""
    ped = fit["pedicel"]
    bones = [
        ("root", (0.0, 0.0, 0.0), (0.0, 0.0, 0.08), None, False, "root"),
        ("body", tuple(ped), tuple(ped + np.array([0.0, -0.06, 0.0])), "root", False, "body"),
        ("cephalothorax", tuple(ped), tuple(fit["ceph_tail"]), "body", True, "body"),
        ("abdomen", tuple(ped), tuple(fit["abdomen_tail"]), "body", True, "body"),
    ]
    for c in sorted(fit["chains"], key=lambda c: (c["order"], c["side"])):
        stem, side = c["name"], c["side"]
        bones += [
            (f"{stem}_femur.{side}", tuple(c["coxa"]), tuple(c["knee"]), "cephalothorax", True, c["kind"]),
            (f"{stem}_tibia.{side}", tuple(c["knee"]), tuple(c["ankle"]), f"{stem}_femur.{side}", True, c["kind"]),
            (f"{stem}_tarsus.{side}", tuple(c["ankle"]), tuple(c["tip"]), f"{stem}_tibia.{side}", True, c["kind"]),
        ]
    for f in fit["fangs"]:
        side = f["side"]
        bones += [(f"chelicera.{side}", tuple(f["base"]), tuple(f["mid"]), "cephalothorax", True, "chelicera"),
                  (f"fang.{side}", tuple(f["mid"]), tuple(f["tip"]), f"chelicera.{side}", True, "chelicera")]
    return bones


# ------------------------------------------------------------------------------------------------
# Blender: build, bind, export

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.objects):
        for item in list(block):
            block.remove(item)


def import_glb(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def mesh_arrays(obj):
    me = obj.data
    me.calc_loop_triangles()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    tri = np.empty(len(me.loop_triangles) * 3, np.int32)
    me.loop_triangles.foreach_get("vertices", tri)
    return co.reshape(-1, 3), tri.reshape(-1, 3).astype(np.int64)


def build_armature(bones, name):
    armature = bpy.data.armatures.new(f"{name}_armature")
    rig = bpy.data.objects.new(f"{name}_rig", armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    made = {}
    for bone_name, head, tail, _parent, deform, _role in bones:
        eb = armature.edit_bones.new(bone_name)
        eb.head, eb.tail = head, tail
        eb.use_deform = deform
        axis = (Vector(tail) - Vector(head)).normalized()
        # Roll: local Z toward world up, so local X is the horizontal hinge of a limb segment.
        eb.align_roll(Vector((0, -1, 0)) if abs(axis.z) > 0.9 else Vector((0, 0, 1)))
        made[bone_name] = eb
    for bone_name, _h, _t, parent, _d, _r in bones:
        if parent:
            made[bone_name].parent = made[parent]
            made[bone_name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def select_only(objects, active):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active


def limbs_of(fit):
    """Each appendage as (bone names, joint points) in the Blender frame."""
    limbs = []
    for c in fit["chains"]:
        limbs.append(([f"{c['name']}_{s}.{c['side']}" for s in ("femur", "tibia", "tarsus")],
                      [c["coxa"], c["knee"], c["ankle"], c["tip"]], c["radius_m"]))
    for f in fit["fangs"]:
        limbs.append(([f"chelicera.{f['side']}", f"fang.{f['side']}"], [f["base"], f["mid"], f["tip"]],
                      f["radius_m"]))
    return limbs


def constrain_to_anatomy(co, table, deform, fit):
    """Keep the heat weights except the ones the measured anatomy rules out.

    Bone heat gives a vertex weight from any bone it can 'see'. On this mesh that left carapace
    and belly vertices 10-20 cm from any coxa holding a fifth of their weight on femurs of legs
    on both sides, and, where a curled leg end hides its own tibia, put femur weight 13 cm past
    the knee. Raising a leg then drags the carapace, and bending a knee splits the leg. Two rules,
    each measured against a limb bone's own segment, so no seam is drawn between body and limb:
      reach:   a limb bone's weight is kept in full within 1.5 limb radii of its segment and
               fades to nothing at 3 radii;
      order:   on a limb (the vertex's nearest centre-line, within 2 radii), a segment's weight
               is kept in full within one radius of that segment's stretch of the centre-line
               and fades to nothing at two.
    Body bones are left to heat. The weight a vertex loses is given back where the anatomy puts
    it: on a limb, to that limb's segments by the vertex's place along the centre-line (blended
    over one radius either side of each joint); off the limbs, to its surviving heat weights.
    A vertex left with nothing at all takes the nearest bone at 1.0 (counted)."""
    col = {n: i for i, n in enumerate(deform)}
    mult = np.ones_like(table)
    limbs = limbs_of(fit)

    def ramp(x, full, zero):
        return np.clip((zero - x) / max(zero - full, 1e-9), 0.0, 1.0)

    def to_segment(a, b):
        d = b - a
        t = np.clip((co - a) @ d / (d @ d), 0.0, 1.0)
        return np.linalg.norm(co - (a + np.outer(t, d)), axis=1), t

    seg_dist = np.full(table.shape, np.inf)
    centre_dist, along = [], []
    for bones, pts, radius in limbs:
        line = np.full(len(co), np.inf)
        s_on = np.zeros(len(co))
        start = 0.0
        for k, bone in enumerate(bones):
            dd, t = to_segment(pts[k], pts[k + 1])
            seg_dist[:, col[bone]] = dd
            mult[:, col[bone]] = ramp(dd, 1.5 * radius, 3.0 * radius)
            better = dd < line
            line[better] = dd[better]
            s_on[better] = start + t[better] * np.linalg.norm(pts[k + 1] - pts[k])
            start += np.linalg.norm(pts[k + 1] - pts[k])
        centre_dist.append(line)
        along.append(s_on)
    centre_dist = np.array(centre_dist)
    nearest = np.argmin(centre_dist, axis=0)
    target = np.zeros_like(table)
    on_any = np.zeros(len(co), bool)
    for li, (bones, pts, radius) in enumerate(limbs):
        on = (nearest == li) & (centre_dist[li] <= 2.0 * radius)
        on_any |= on
        ends = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(np.array(pts), axis=0), axis=1))]
        s_on = along[li][on]
        share = np.zeros((int(on.sum()), len(bones)))
        for k, bone in enumerate(bones):
            outside = np.maximum(np.maximum(ends[k] - along[li], along[li] - ends[k + 1]), 0.0)
            mult[on, col[bone]] *= ramp(outside[on], radius, 2.0 * radius)
            lo = np.inf if k == 0 else (s_on - ends[k]) / (2 * radius) + 0.5
            hi = np.inf if k == len(bones) - 1 else (ends[k + 1] - s_on) / (2 * radius) + 0.5
            share[:, k] = np.clip(np.minimum(lo, hi), 0.0, 1.0)
        share /= np.maximum(share.sum(1, keepdims=True), 1e-12)
        for k, bone in enumerate(bones):
            target[on, col[bone]] = share[:, k]
    ped = fit["pedicel"]
    for name, tail in (("cephalothorax", fit["ceph_tail"]), ("abdomen", fit["abdomen_tail"])):
        seg_dist[:, col[name]] = to_segment(ped, tail)[0]

    table = table / np.maximum(table.sum(1, keepdims=True), 1e-12)
    kept = table * mult
    lost = 1.0 - kept.sum(1)
    removed = np.where(table.sum(1) > 0, lost, 0.0)
    removed_share = float(removed.sum() / max(float((table.sum(1) > 0).sum()), 1.0))
    kept[on_any] += lost[on_any, None] * target[on_any]
    empty = kept.sum(1) <= 1e-6
    kept[empty] = 0.0
    kept[np.flatnonzero(empty), np.argmin(seg_dist[empty], axis=1)] = 1.0
    kept /= kept.sum(1, keepdims=True)
    return kept, {
        "rule": "heat weights kept except where the measured anatomy rules them out (constrain_to_anatomy)",
        "vertices_with_any_weight_removed": int((removed > 1e-3).sum()),
        "vertices_with_over_25pct_removed": int((removed > 0.25).sum()),
        "weight_removed_share_of_all": round(removed_share, 4),
        "vertices_on_a_limb": int(on_any.sum()),
        "vertices_given_nearest_bone_at_1": int(empty.sum()),
    }


def heat_weights_on_welded_copy(mesh, rig, weld_distance, fit, constrain=True):
    """Bone heat on a welded duplicate (generated meshes are split at every UV seam, which heat
    diffusion cannot cross), constrained to the anatomy, copied back to the original vertices by
    nearest vertex."""
    import bmesh
    welded = mesh.copy()
    welded.data = mesh.data.copy()
    welded.name = mesh.name + "_welded"
    bpy.context.collection.objects.link(welded)
    bm = bmesh.new()
    bm.from_mesh(welded.data)
    before = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld_distance)
    after = len(bm.verts)
    bm.to_mesh(welded.data)
    bm.free()
    welded.vertex_groups.clear()

    select_only([welded, rig], rig)
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    deform = [b.name for b in rig.data.bones if b.use_deform]
    names = {g.index: g.name for g in welded.vertex_groups}
    wv = welded.data.vertices
    table = np.zeros((len(wv), len(deform)))
    col = {n: i for i, n in enumerate(deform)}
    for v in wv:
        for g in v.groups:
            name = names.get(g.group)
            if name in col and g.weight > 0:
                table[v.index, col[name]] = g.weight
    weighted = table.sum(1) > 0
    unweighted_welded = int((~weighted).sum())
    co = np.array([v.co[:] for v in wv])
    if constrain:
        table, constraint = constrain_to_anatomy(co, table, deform, fit)
    else:
        constraint = "none"
        if unweighted_welded:
            tree = KDTree(int(weighted.sum()))
            for i in np.flatnonzero(weighted):
                tree.insert(co[i], int(i))
            tree.balance()
            for i in np.flatnonzero(~weighted):
                table[i] = table[tree.find(co[i])[1]]

    tree = KDTree(len(co))
    for i, p in enumerate(co):
        tree.insert(p, i)
    tree.balance()
    mesh.vertex_groups.clear()
    groups = {n: mesh.vertex_groups.new(name=n) for n in deform}
    worst = 0.0
    for v in mesh.data.vertices:
        _p, j, d = tree.find(v.co)
        worst = max(worst, d)
        row = table[j]
        for c in np.flatnonzero(row > 0):
            groups[deform[c]].add([v.index], float(row[c]), "REPLACE")
    bpy.data.objects.remove(welded, do_unlink=True)
    return {"method": "bone-heat on welded copy, constrained to the measured anatomy, nearest-vertex transfer",
            "weld_distance_m": weld_distance, "vertices_before_weld": before,
            "vertices_after_weld": after, "heat_unweighted_welded_vertices": unweighted_welded,
            "anatomy_constraint": constraint,
            "transfer_max_distance_m": round(worst, 9)}


def clean_weights(mesh, max_influences=4, min_weight=0.02):
    select_only([mesh], mesh)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=min_weight)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=max_influences)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def export_rigged(rig, mesh, path):
    select_only([rig, mesh], rig)
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=False,
        export_yup=True, export_normals=True, export_materials="EXPORT",
        export_texcoords=True, export_skins=True)


def to_gl(p):
    return (GL_TO_BL.T @ np.asarray(p, float)).tolist()


def rnd(v, n=5):
    return [round(float(x), n) for x in v]


def run_fit(args, paths):
    reset_scene()
    imported = import_glb(args.input)
    meshes = [o for o in imported if o.type == "MESH"]
    select_only(meshes, meshes[0])
    if len(meshes) > 1:
        bpy.ops.object.join()
    mesh = bpy.context.view_layer.objects.active
    for obj in [o for o in bpy.context.scene.objects if o is not mesh]:
        bpy.data.objects.remove(obj, do_unlink=True)
    stem = f"{args.asset}_rigged"
    mesh.name, mesh.data.name = stem, f"{stem}_mesh"
    for material in mesh.data.materials:
        if material is not None:
            material.name = f"MAT_{stem}"
    mesh.data.transform(mesh.matrix_world)
    mesh.matrix_world = Matrix.Identity(4)

    co, tri = mesh_arrays(mesh)
    fit = measure(co[tri])
    transform = fit["transform"]
    mesh.data.transform(Matrix(transform.tolist()))
    mesh.data.update()

    bones = bone_plan(fit)
    rig = build_armature(bones, args.asset)
    bind = heat_weights_on_welded_copy(mesh, rig, weld_distance=1e-4, fit=fit,
                                       constrain=args.weights == "heat+anatomy")
    select_only([mesh, rig], rig)
    bpy.ops.object.parent_set(type="ARMATURE")          # modifier only; the groups exist
    clean_weights(mesh)

    os.makedirs(paths["rigged"], exist_ok=True)
    out = os.path.join(paths["rigged"], f"{args.asset}_rigged.glb")
    export_rigged(rig, mesh, out)

    # Reports. The transform is stated in both frames; glTF is what the file and the game use.
    t_gl = np.eye(4)
    t_gl[:3, :3] = GL_TO_BL.T @ transform[:3, :3] @ GL_TO_BL
    t_gl[:3, 3] = GL_TO_BL.T @ transform[:3, 3]
    r = t_gl[:3, :3]
    pitch = math.degrees(math.atan2(r[2, 1], r[2, 2]))
    heading = math.degrees(math.atan2(-r[2, 0], math.hypot(r[2, 1], r[2, 2])))
    bank = math.degrees(math.atan2(r[1, 0], r[0, 0]))
    angle = math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(r) - 1) / 2))))
    chains_by_name = {c["chain"]: c for c in fit["chains"]}
    skeleton = {
        "skeleton_version": PLAN,
        "asset_id": args.asset,
        "fit_family": "arthropod",
        "units": "metres",
        "export_frame": "Y-up, tips' supporting plane on the ground plane, footprint centred",
        "forward_axis": "+Z",
        "up_axis": "+Y",
        "left_axis": "+X (suffix .L)",
        "reference_pose": "the LOD0 as modelled, levelled by one rigid transform (see fit report)",
        "naming": {
            "root": "ground under the footprint centre; not deforming; carries no motion",
            "body": "pivot at the waist (pedicel); not deforming; carries the body's bob, sway and lunge",
            "cephalothorax / abdomen": "waist to the front of the carapace / to the rear of the abdomen",
            "palp_<segment>.<side>": "the front-most long pair (pedipalps)",
            "leg<n>_<segment>.<side>": "walking legs, n = 1 (front) to 4 (rear)",
            "<segment>": "femur = coxa to knee, tibia = knee to ankle, tarsus = ankle to tip",
            "chelicera.<side>, fang.<side>": "the shorter pair hanging below the head: base, then fang",
            "<side>": "L = the creature's left = glTF +X; R = glTF -X",
        },
        "bone_count": len(bones),
        "deform_bone_count": sum(1 for b in bones if b[4]),
        "bones": [{"name": n, "parent": p, "head": rnd(to_gl(h)), "tail": rnd(to_gl(t)),
                   "deform": d, "role": role} for n, h, t, p, d, role in bones],
        "chains": [{"chain": c["chain"], "kind": c["kind"], "side": c["side"], "order": c["order"],
                    "bones": [f"{c['name']}_{s}.{c['side']}" for s in ("femur", "tibia", "tarsus")]}
                   for c in sorted(fit["chains"], key=lambda c: (c["order"], c["side"]))]
                  + [{"chain": f["chain"], "kind": "chelicera", "side": f["side"], "order": -1,
                      "bones": [f"chelicera.{f['side']}", f"fang.{f['side']}"]} for f in fit["fangs"]],
    }
    with open(os.path.join(paths["rigged"], f"{args.asset}_rigged_skeleton.json"), "w", encoding="utf-8") as h:
        json.dump(skeleton, h, indent=2)

    stats = weight_stats(mesh, rig)
    rig_report = {"asset": args.asset, "plan": "arthropod", "skeleton_version": PLAN,
                  "method": "automatic", "weighting": bind, "out": out, **stats}
    with open(os.path.join(paths["rigged"], f"{args.asset}_rig.json"), "w", encoding="utf-8") as h:
        json.dump(rig_report, h, indent=2)

    fit_report = {
        "asset": args.asset, "input": args.input, "plan": PLAN,
        "orientation_change": {
            "why": "The LOD0 rests on its front appendage tips with the body pitched nose-down, "
                   "because it was reconstructed from a three-quarter overhead concept. The "
                   "arthropod plan needs the tips on the ground and the body axis along +Z, so the "
                   "whole mesh is moved by this one rigid transform; no vertex moves relative to another.",
            "gltf_matrix_row_major": [rnd(row, 7) for row in t_gl],
            "rotation_angle_deg": round(angle, 3),
            "pitch_about_x_deg": round(pitch, 3), "yaw_about_y_deg": round(heading, 3),
            "roll_about_z_deg": round(bank, 3),
            "translation_m": rnd(t_gl[:3, 3]),
            "yaw_from_mirror_symmetry_deg": round(fit["yaw_deg"], 3),
            "mirror_symmetry_error_m": round(fit["mirror_error_m"], 4),
            "support_contacts": [int(i) for i in fit["support_contacts"]],
        },
        "voxel_size_m": fit["voxel_size_m"], "opening_radius_voxels": fit["opening_radius_voxels"],
        "appendages": {"long": fit["long_count"], "short": fit["short_count"],
                       "long_by_side": fit["counts_by_side"]},
        "chains": [{k: (rnd(to_gl(v), 4) if k in ("coxa", "knee", "ankle", "tip") else
                        (round(v, 4) if isinstance(v, float) else v))
                    for k, v in c.items() if k != "line"}
                   for c in sorted(fit["chains"], key=lambda c: (c["order"], c["side"]))],
        "tip_height_above_ground_m": {c["chain"]: round(float(c["tip"][2]), 4) for c in fit["chains"]},
        "fangs": [{"chain": f["chain"], "base": rnd(to_gl(f["base"]), 4), "mid": rnd(to_gl(f["mid"]), 4),
                   "tip": rnd(to_gl(f["tip"]), 4), "length": round(f["length"], 4),
                   "polyline_rms_m": round(f["polyline_rms_m"], 4)} for f in fit["fangs"]],
        "body": {"pedicel": rnd(to_gl(fit["pedicel"]), 4), "ceph_tail": rnd(to_gl(fit["ceph_tail"]), 4),
                 "abdomen_tail": rnd(to_gl(fit["abdomen_tail"]), 4)},
        "mesh_after": {"min_gltf": rnd(np.min(mesh_arrays(mesh)[0] @ GL_TO_BL, 0)),
                       "max_gltf": rnd(np.max(mesh_arrays(mesh)[0] @ GL_TO_BL, 0))},
        "branch_lines_gltf": {c["chain"]: [rnd(to_gl(p), 4) for p in c["line"]] for c in fit["chains"]},
        "weighting": bind,
    }
    os.makedirs(paths["reports"], exist_ok=True)
    with open(os.path.join(paths["reports"], "fit_report.json"), "w", encoding="utf-8") as h:
        json.dump(fit_report, h, indent=2)
    print("ARTHROPOD_FIT " + json.dumps({"out": out, "bones": len(bones), "chains": sorted(chains_by_name),
                                          "fangs": [f["chain"] for f in fit["fangs"]],
                                          "orientation": fit_report["orientation_change"],
                                          "weighting": bind, **stats}))


def weight_stats(mesh, rig):
    deform = {b.name for b in rig.data.bones if b.use_deform}
    names = {g.index: g.name for g in mesh.vertex_groups}
    unweighted = 0
    most = 0
    for v in mesh.data.vertices:
        inf = [g for g in v.groups if names.get(g.group) in deform and g.weight > 0]
        unweighted += not inf
        most = max(most, len(inf))
    return {"bones": len(rig.data.bones), "deform_bones": len(deform),
            "vertices": len(mesh.data.vertices), "unweighted_vertices": unweighted,
            "max_influences": most}


# ------------------------------------------------------------------------------------------------
# Motion: the six procedural clips, written as _make_creature_motion.py-format sources
#
# Schema 2 (see _make_creature_motion.py): what the spider does, in its own axes and in leg
# lengths, resolved by _blender_anim_creature.py against this rig and its skin. Every leg and palp
# tip is placed by IK on the ground (the tarsus carried on the tibia, the knee bending the way its
# rest shape bends: up); the stance lifts the body until its underside clears the floor and draws
# in the tips a leg cannot reach from there (the rear pair, 0.1 m up in the mesh); the chelicerae
# and fangs fold up just enough to stay above the floor; the death lays the curled body on it.

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _make_creature_motion import (COMMIT, GAIT_SAMPLES, GAITS, RECOVER, STRIKE_END, arc,  # noqa: E402
                                   attack_beats, gait_block, smooth, step)


def arthropod_rig(skeleton):
    limbs, keep_clear = {}, []
    for c in skeleton["chains"]:
        if c["kind"] == "chelicera":
            keep_clear.append(list(c["bones"]))
            continue
        limbs[c["chain"]] = {"bones": list(c["bones"]), "side": c["side"], "bend": "rest|up",
                             "contact": True, "end": "carry", "reach": "pull_in"}
    return {"body": "body", "ground_bone": "root", "trunk": ["body", "cephalothorax", "abdomen"],
            "limbs": limbs, "keep_clear": keep_clear,
            "stance": {"extension": 0.95, "clearance": 0.015}}


def arthropod_intent(kind, t, duration, phase, chains):
    """chains: the skeleton's leg and palp chains (chain, kind, side, order)."""
    body, bones, limbs, ground = {}, {}, {}, "feet"
    fangs = ("chelicera.L", "chelicera.R")
    if kind == "idle":
        w1 = 2 * math.pi * phase                  # 4.0 s
        w2 = 2 * w1                               # 2.0 s
        body = {"up": 0.012 * math.sin(w1), "pitch": 0.8 * math.sin(w1), "side": 0.01 * math.sin(w1 + 1.0)}
        bones = {"abdomen": {"pitch": -2.2 * math.sin(w1 + 0.6), "yaw": 1.2 * math.sin(w2)}}
        bones.update({f: {"pitch": 4.0 * math.sin(w2 + 0.4)} for f in fangs})
        for c in chains:
            if c["kind"] == "palp":               # the palps tap in turn
                k = 0.5 - 0.5 * math.cos(w2 + (0.0 if c["side"] == "L" else math.pi))
                limbs[c["chain"]] = {"foot": [0.0, 0.07 * k, 0.03 * k], "planted": k < 1e-6}
            else:
                limbs[c["chain"]] = {"foot": [0.0, 0.0, 0.0]}
    elif kind in ("walk", "run"):
        walk = kind == "walk"
        g = GAITS[("arthropod", kind)]
        duty, lift = g["duty"], g["lift"]
        for c in chains:
            # alternating tetrapod: L1 R2 L3 R4 (+ R palp) against R1 L2 R3 L4 (+ L palp); one cycle,
            # feet in stance travels (every planted tip travels at the ground's speed, palps too)
            group = (c["order"] + (0 if c["side"] == "L" else 1)) % 2
            scale = 0.8 if c["kind"] == "palp" else 1.0
            s = step(phase + 0.5 * group, duty, 1.0, lift * scale, 0.0)
            s["toe"] = 0.0
            limbs[c["chain"]] = s
        body = {"up": (0.006 if walk else 0.012) * math.cos(4 * math.pi * phase),
                "yaw": 2.0 * math.sin(2 * math.pi * phase), "roll": 1.5 * math.sin(2 * math.pi * phase),
                "pitch": 1.0 * math.sin(4 * math.pi * phase)}
        bones = {"abdomen": {"yaw": -3.0 * math.sin(2 * math.pi * phase - 0.5),
                             "pitch": 1.5 * math.sin(4 * math.pi * phase)}}
    elif kind == "attack":
        w, s, r, mix = attack_beats(t, duration)
        # rear up with the palps and first legs raised and the fangs spread, lunge forward and down
        # through the hit window as the raised pair comes down a stride ahead, hold, recover
        body = {"fwd": mix(-0.08, 0.30), "up": mix(0.10, 0.02), "pitch": mix(-14.0, 6.0)}
        bones = {"abdomen": {"pitch": mix(6.0, -4.0)}}
        bones.update({f: {"pitch": mix(-30.0, 25.0)} for f in fangs})
        reach = 0.42
        for c in chains:
            if c["kind"] == "palp" or c["order"] == 1:
                if t < COMMIT:
                    foot = {"foot": [0.0, 0.45 * w, 0.10 * w], "planted": w <= 0.0}
                elif t < STRIKE_END:
                    foot = {"foot": [0.0, 0.45 * (1.0 - s), 0.10 + (reach - 0.10) * s], "planted": s >= 1.0}
                elif t < RECOVER:
                    foot = {"foot": [0.0, 0.0, reach]}
                else:
                    back = arc(r, -reach, 0.10)
                    foot = {"foot": [0.0, back["foot"][1], reach + back["foot"][2]], "planted": back["planted"]}
                limbs[c["chain"]] = foot
            else:
                limbs[c["chain"]] = {"foot": [0.0, 0.0, 0.0]}
    elif kind == "hit":
        k = math.sin(math.pi * min(t / duration, 1.0))
        # struck from the front: thrown back and up onto the rear legs, palps flung up
        body = {"fwd": -0.10 * k, "up": 0.03 * k, "pitch": -10.0 * k, "roll": 4.0 * k}
        bones = {"abdomen": {"pitch": 8.0 * k}}
        bones.update({f: {"pitch": -15.0 * k} for f in fangs})
        for c in chains:
            if c["kind"] == "palp":
                limbs[c["chain"]] = {"foot": [0.0, 0.15 * k, 0.0], "planted": k <= 0.0}
            else:
                limbs[c["chain"]] = {"foot": [0.0, 0.0, 0.0]}
    elif kind == "death":
        # The body sinks onto its belly on planted legs. Then each leg in turn lets go: first it
        # lifts about its coxa (with the belly down, the leg's own rest shape already holds the tip
        # off the floor), then it folds at the knee and tarsus, curling up and in over the body.
        # Folding before lifting, or folding the long knee-to-tip lever much past the lift (the
        # tarsus skin runs 0.3 m past its joint), would swing the tip down under the belly and push
        # the corpse up.
        # The ground rule keeps the lowest part, the belly, on y = 0. (The tips are not drawn in
        # first: femur plus carried tibia and tarsus cannot fold shorter than about 0.2 m, and IK
        # would push a tip drawn closer than that through the floor.)
        settle = smooth(t / 0.5)
        ordered = sorted(chains, key=lambda c: (c["order"], c["side"]))
        for n, c in enumerate(ordered):
            lift = smooth((t - 0.45 - 0.03 * n) / 0.4)
            fold = smooth((t - 0.75 - 0.03 * n) / 0.6)
            limbs[c["chain"]] = {"foot": [0.0, 0.0, 0.0], "ik": 1.0 - lift,
                                 "raise": 70.0 * lift, "flex": [55.0 * fold, 25.0 * fold]}
        body = {"up": -0.22 * settle, "pitch": 3.0 * settle, "roll": 4.0 * settle}
        bones = {f: {"pitch": 20.0 * settle} for f in fangs}
        ground = "lie"
    else:
        raise ValueError(kind)
    return {"body": body, "bones": bones, "limbs": limbs, "ground": ground}


def run_motion(args, paths):
    with open(os.path.join(paths["rigged"], f"{args.asset}_rigged_skeleton.json"), encoding="utf-8") as h:
        skeleton = json.load(h)
    rig = arthropod_rig(skeleton)
    chains = [c for c in skeleton["chains"] if c["kind"] != "chelicera"]
    short = args.asset.replace("creature_", "", 1)
    os.makedirs(paths["source"], exist_ok=True)
    written = []
    for kind in KINDS:
        duration = DURATIONS[kind]
        gait = ("arthropod", kind) in GAITS
        # a gait is one cycle sampled by phase (the baker fits the cycles to the rig and the clip);
        # a death ends on the floor: keys four times as dense keep it there between keys as well
        count = GAIT_SAMPLES if gait else int(round(duration * FPS * (4 if kind == "death" else 1))) + 1
        frames = []
        for i in range(count):
            u = i / (count - 1)
            frame = arthropod_intent(kind, u * duration, duration, u, chains)
            frame["phase" if gait else "t"] = round(u if gait else u * duration, 6)
            frames.append(frame)
        bones = sorted({b for f in frames for b in f["bones"]} | {b for l in rig["limbs"].values() for b in l["bones"]}
                       | {b for c in rig["keep_clear"] for b in c} | {"root", "body"})
        data = {"schema_version": "2", "kind": kind, "plan": "arthropod", "fps": FPS, "duration_s": duration,
                "synthetic": True, "skeleton_version": PLAN, "loop": kind in ("idle", "walk", "run"),
                "rig": rig, "bones_animated": bones, "frames": frames}
        if gait:
            data["gait"] = gait_block("arthropod", kind)
        path = os.path.join(paths["source"], f"{short}_{kind}.json")
        with open(path, "w", encoding="utf-8") as h:
            json.dump(data, h)
        written.append({"kind": kind, "frames": len(frames), "path": path})
    print("ARTHROPOD_MOTION " + json.dumps(written))


# ------------------------------------------------------------------------------------------------
# Verify: the files, measured

def run_verify(args, paths):
    rigged_path = os.path.join(paths["rigged"], f"{args.asset}_rigged.glb")
    glb = SkinnedGLB(rigged_path)
    with open(os.path.join(paths["rigged"], f"{args.asset}_rigged_skeleton.json"), encoding="utf-8") as h:
        skeleton = json.load(h)
    with open(os.path.join(paths["reports"], "fit_report.json"), encoding="utf-8") as h:
        fit_report = json.load(h)
    deform = {b["name"]: b["deform"] for b in skeleton["bones"]}
    role = {b["name"]: b["role"] for b in skeleton["bones"]}

    P = glb.positions
    height = float(P[:, 1].max() - P[:, 1].min())
    tolerance = 0.06 * height

    tris = P[glb.tris]
    rest_world = glb.joint_world()
    joints = rest_world[:, :3, 3]

    reset_scene()
    me = bpy.data.meshes.new("verify")
    me.from_pydata(P.tolist(), [], glb.tris.tolist())
    bvh = BVHTree.FromPolygons(P.tolist(), glb.tris.tolist())
    # Inside test: a point is enclosed when rays in (nearly) every direction meet the surface.
    # The generated mesh is a hollow shell, so winding numbers and parity do not apply.
    enclosure = np.array([enclosed_fraction(bvh, p) for p in joints])
    inside = enclosure >= 0.95
    kd = KDTree(len(P))
    for i, p in enumerate(P):
        kd.insert(p, i)
    kd.balance()

    dominant = np.zeros(len(glb.joint_names), int)
    top = glb.jidx[np.arange(len(P)), np.argmax(glb.weights, 1)]
    strong = glb.weights.max(1) > 0.5
    for j in range(len(glb.joint_names)):
        dominant[j] = int(((top == j) & strong).sum())
    any_weight = np.zeros(len(glb.joint_names), int)
    for k in range(glb.jidx.shape[1]):
        np.add.at(any_weight, glb.jidx[glb.weights[:, k] > 0, k], 1)

    per_joint = []
    for j, name in enumerate(glb.joint_names):
        p = Vector(joints[j])
        _v, _i, near_v = kd.find(p)
        hit = bvh.find_nearest(p)
        near_s = hit[3] if hit[0] is not None else float("inf")
        dist = 0.0 if inside[j] else near_s
        needs = deform.get(name, False)
        limb = role.get(name) in ("leg", "palp", "chelicera") or name in ("cephalothorax", "abdomen")
        per_joint.append({
            "joint": name, "deform": needs, "role": role.get(name),
            "position_gltf": rnd(joints[j], 4), "enclosed_fraction": round(float(enclosure[j]), 3),
            "inside_mesh": bool(inside[j]), "nearest_vertex_m": round(near_v, 4),
            "nearest_surface_m": round(near_s, 4), "distance_to_mesh_m": round(dist, 4),
            "within_tolerance": bool(dist <= tolerance),
            "dominated_vertices": int(dominant[j]), "weighted_vertices": int(any_weight[j]),
            "passes": bool((dist <= tolerance or not needs) and (dominant[j] > 0 or not (needs and limb))),
        })
    # Leaf tips are tails, not joints; report them too since they place the leg ends.
    tips = []
    for b in skeleton["bones"]:
        if b["name"].split("_")[-1].startswith("tarsus") or b["name"].startswith("fang."):
            q = Vector(b["tail"])
            hit = bvh.find_nearest(q)
            tips.append({"bone": b["name"], "tail_gltf": b["tail"],
                         "inside_mesh": bool(enclosed_fraction(bvh, b["tail"]) >= 0.95),
                         "nearest_surface_m": round(hit[3], 4)})

    # Bind == rest, and the bind mesh == the LOD0 under the recorded transform.
    bind_world = np.linalg.inv(glb.ibm)
    bind_rest = float(np.abs(bind_world - rest_world).max())
    lod0 = static_positions(args.input)
    t_gl = np.array(fit_report["orientation_change"]["gltf_matrix_row_major"])
    moved = (t_gl @ np.c_[lod0, np.ones(len(lod0))].T).T[:, :3]
    kd0 = KDTree(len(moved))
    for i, p in enumerate(moved):
        kd0.insert(p, i)
    kd0.balance()
    to_lod0 = max(kd0.find(p)[2] for p in P)
    from_lod0 = max(kd.find(p)[2] for p in moved)
    posed_rest = glb.pose()
    bind_pose_err = float(np.abs(posed_rest - P).max())

    # Clips: tracks, rest translations, loop closure, stretch.
    edges = np.unique(np.sort(np.r_[glb.tris[:, [0, 1]], glb.tris[:, [1, 2]], glb.tris[:, [2, 0]]], 1), axis=0)
    rest_len = np.linalg.norm(P[edges[:, 0]] - P[edges[:, 1]], axis=1)
    ok_edge = rest_len > 1e-3          # ratios of sub-millimetre edges are noise
    top = glb.jidx[np.arange(len(P)), np.argmax(glb.weights, 1)]
    # Coincident vertices (UV seams): if they part under a pose, the surface has torn.
    seam_a, seam_b = [], []
    order = np.lexsort(np.round(P, 6).T[::-1])
    Ps = np.round(P[order], 6)
    same = (Ps[1:] == Ps[:-1]).all(1)
    seam_a, seam_b = order[:-1][same], order[1:][same]
    short = args.asset.replace("creature_", "", 1)
    clips = []
    rest_trs = {glb.names[j]: glb.rest[j] for j in glb.joints}
    for kind in KINDS:
        path = os.path.join(paths["ready"], f"anim.creature.{short}.{kind}.glb")
        if not os.path.exists(path):
            clips.append({"kind": kind, "missing": path})
            continue
        clip = Clip(path)
        names = set(clip.tracks)
        rig_names = set(glb.joint_names)
        translation_drift = 0.0
        for name in rig_names & names:
            track = clip.tracks[name].get("translation")
            if track is None or name in ("root", "body"):
                continue
            translation_drift = max(translation_drift, float(np.abs(track[1] - rest_trs[name][0]).max()))
        frames = int(round(DURATIONS[kind] * FPS)) + 1
        times = np.arange(frames) / FPS
        worst_stretch, worst_squash, worst_t, seam_gap, worst_grow = 1.0, 1.0, 0.0, 0.0, 0.0
        over, worst_edge = 0, None
        for t in times:
            posed = glb.pose(clip, t)
            length = np.linalg.norm(posed[edges[:, 0]] - posed[edges[:, 1]], axis=1)
            ratio = np.where(ok_edge, length / np.maximum(rest_len, 1e-12), 1.0)
            if ratio.max() > worst_stretch:
                worst_stretch, worst_t = float(ratio.max()), float(t)
                e = edges[int(np.argmax(ratio))]
                worst_edge = [glb.joint_names[top[e[0]]], glb.joint_names[top[e[1]]]]
            worst_grow = max(worst_grow, float((length - rest_len).max()))
            worst_squash = min(worst_squash, float(ratio[ok_edge].min()))
            over = max(over, int((ratio > 1.25).sum()))
            if len(seam_a):
                seam_gap = max(seam_gap, float(np.linalg.norm(posed[seam_a] - posed[seam_b], axis=1).max()))
        loop_gap = None
        if kind in ("idle", "walk", "run"):
            a = glb.pose(clip, 0.0)
            b = glb.pose(clip, clip.duration)
            loop_gap = round(float(np.abs(a - b).max()), 6)
        clips.append({
            "kind": kind, "file": path, "duration_s": round(clip.duration, 4),
            "declared_s": DURATIONS[kind],
            "tracks_for_rig_bones": len(rig_names & names), "rig_bones": len(rig_names),
            "bones_without_tracks": sorted(rig_names - names),
            "tracks_for_unknown_bones": sorted(names - rig_names - {None}),
            "max_rest_translation_drift_m": round(translation_drift, 7),
            "loop_closure_max_vertex_gap_m": loop_gap,
            "max_edge_stretch_ratio": round(worst_stretch, 3), "at_s": round(worst_t, 3),
            "worst_edge_dominant_bones": worst_edge,
            "max_edge_elongation_m": round(worst_grow, 4),
            "min_edge_ratio": round(worst_squash, 3),
            "max_edges_over_1_25x_in_a_frame": over,
            "max_seam_gap_m": round(seam_gap, 6),
        })

    report = {
        "asset": args.asset, "rigged": rigged_path, "mesh_height_m": round(height, 4),
        "tolerance_m": round(tolerance, 4), "tolerance_rule": "0.06 x mesh height; a joint inside the filled mesh is at distance 0",
        "joints": per_joint,
        "all_joints_pass": all(j["passes"] for j in per_joint),
        "worst_deform_joint_distance_m": max(j["distance_to_mesh_m"] for j in per_joint if j["deform"]),
        "leaf_tails": tips,
        "bind_vs_rest_max_abs": round(bind_rest, 8),
        "bind_pose_skinning_max_error_m": round(bind_pose_err, 8),
        "rigged_vs_transformed_lod0_max_m": {"rigged_to_lod0": round(to_lod0, 7), "lod0_to_rigged": round(from_lod0, 7)},
        "triangles": {"rigged": int(len(glb.tris)), "lod0": lod0_triangles(args.input)},
        "clips": clips,
    }
    with open(os.path.join(paths["reports"], "verify_report.json"), "w", encoding="utf-8") as h:
        json.dump(report, h, indent=2)
    print("ARTHROPOD_VERIFY " + json.dumps({
        "all_joints_pass": report["all_joints_pass"],
        "worst_deform_joint_distance_m": report["worst_deform_joint_distance_m"],
        "tolerance_m": report["tolerance_m"],
        "bind_vs_rest": report["bind_vs_rest_max_abs"],
        "rigged_vs_lod0": report["rigged_vs_transformed_lod0_max_m"],
        "clips": [{k: c.get(k) for k in ("kind", "duration_s", "bones_without_tracks",
                                         "max_edge_stretch_ratio", "max_edge_elongation_m",
                                         "max_edges_over_1_25x_in_a_frame", "max_seam_gap_m",
                                         "loop_closure_max_vertex_gap_m")} for c in clips]}))


# ------------------------------------------------------------------------------------------------
# Render: evaluate the GLBs as a game would and look at them

def display_mesh(name, positions, tris, uv, material):
    """A render mesh on welded topology (seams joined), so a torn seam would show as a gap."""
    key = np.round(positions, 6)
    _u, rep, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inv = inv.ravel()
    faces = inv[tris]
    me = bpy.data.meshes.new(name)
    me.vertices.add(len(rep))
    me.vertices.foreach_set("co", positions[rep].ravel())
    me.loops.add(faces.size)
    me.loops.foreach_set("vertex_index", faces.ravel().astype(np.int32))
    me.polygons.add(len(faces))
    me.polygons.foreach_set("loop_start", np.arange(0, faces.size, 3, dtype=np.int32))
    me.update()
    if uv is not None:
        layer = me.uv_layers.new(name="UVMap")
        loop_uv = uv[tris.ravel()].copy()
        loop_uv[:, 1] = 1.0 - loop_uv[:, 1]
        layer.data.foreach_set("uv", loop_uv.ravel())
    me.polygons.foreach_set("use_smooth", np.ones(len(faces), bool))
    me.update()
    if material is not None:
        me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(obj)
    return obj, rep


def gl_to_bl_points(p):
    return np.asarray(p) @ GL_TO_BL.T


def stage(scene_width=1024):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene_width
    scene.render.resolution_y = scene_width
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("w")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.50, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.55
    scene.world = world
    bpy.ops.mesh.primitive_plane_add(size=60.0, location=(0.0, 0.0, 0.0))
    ground = bpy.context.active_object
    ground.name = "ground_plane_z0"
    mat = bpy.data.materials.new("ground")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.20, 0.19, 0.18, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    ground.data.materials.append(mat)
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = 2.4
    sun_obj = bpy.data.objects.new("sun", sun)
    sun_obj.rotation_euler = (math.radians(35), math.radians(10), math.radians(35))
    bpy.context.collection.objects.link(sun_obj)
    fill = bpy.data.lights.new("fill", "AREA")
    fill.energy = 120
    fill.size = 4
    fill_obj = bpy.data.objects.new("fill", fill)
    fill_obj.location = (-3, -2, 3)
    fill_obj.rotation_euler = (Vector((0, 0, 0.2)) - Vector(fill_obj.location)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.collection.objects.link(fill_obj)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    return scene, cam


# Blender frame: front of the creature is -Y. Views are named from the creature's side.
VIEWS = {
    "front34": ((1.25, -1.6, 0.85), (0.0, -0.02, 0.1)),
    "side": ((2.0, -0.02, 0.35), (0.0, -0.02, 0.12)),
    "front": ((0.0, -2.35, 0.45), (0.0, 0.0, 0.1)),
    "top": ((0.0, -0.001, 2.75), (0.0, 0.0, 0.0)),
    "rear34": ((-1.45, 1.85, 0.95), (0.0, 0.05, 0.1)),
    "close34": ((0.95, -1.15, 0.62), (0.0, -0.05, 0.12)),
    "closerear": ((-0.95, 1.15, 0.62), (0.0, 0.1, 0.12)),
}


def aim(cam, where, target):
    cam.location = where
    cam.rotation_euler = (Vector(target) - Vector(where)).to_track_quat("-Z", "Y").to_euler()


def render_to(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def paint_dominant(obj, rep, top, count):
    import colorsys
    hues = (np.arange(count) * 0.618034) % 1.0
    palette = np.array([colorsys.hsv_to_rgb(hh, 0.9, 0.95) for hh in hues]) ** 2.2
    attr = obj.data.color_attributes.new("dominant", "FLOAT_COLOR", "POINT")
    attr.data.foreach_set("color", np.c_[palette[top[rep]], np.ones(len(rep))].ravel())
    mat = bpy.data.materials.new("weights")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    node = nodes.new("ShaderNodeAttribute")
    node.attribute_name = "dominant"
    mat.node_tree.links.new(node.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    mat.node_tree.links.new(node.outputs["Color"], nodes["Principled BSDF"].inputs["Emission Color"])
    nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 0.5
    obj.data.materials.append(mat)


def run_render(args, paths):
    rigged_path = os.path.join(paths["rigged"], f"{args.asset}_rigged.glb")
    glb = SkinnedGLB(rigged_path)
    with open(os.path.join(paths["reports"], "fit_report.json"), encoding="utf-8") as h:
        fit_report = json.load(h)
    reset_scene()
    imported = import_glb(rigged_path)
    material = next((m for o in imported if o.type == "MESH" for m in o.data.materials if m), None)
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    scene, cam = stage(args.size)
    out = paths["renders"]
    os.makedirs(out, exist_ok=True)
    written = []
    only = set(args.only or [])

    def want(tag):
        return not only or tag in only

    # 1. Bind pose (the rigged file as skinned at rest) vs the LOD0 moved by the recorded transform.
    if want("bind"):
        lod_gltf, lod_blob = read_glb(args.input)
        lod_node = next(n for n in lod_gltf["nodes"] if "mesh" in n)
        prim = lod_gltf["meshes"][lod_node["mesh"]]["primitives"][0]
        lod_p = accessor(lod_gltf, lod_blob, prim["attributes"]["POSITION"]).astype(np.float64)
        lod_uv = accessor(lod_gltf, lod_blob, prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
        lod_t = accessor(lod_gltf, lod_blob, prim["indices"]).astype(np.int64).reshape(-1, 3)
        world = world_matrices(lod_gltf, [trs_matrix(*node_trs(n)) for n in lod_gltf["nodes"]])
        lod_node_i = lod_gltf["nodes"].index(lod_node)
        lod_p = (world[lod_node_i] @ np.c_[lod_p, np.ones(len(lod_p))].T).T[:, :3]
        t_gl = np.array(fit_report["orientation_change"]["gltf_matrix_row_major"])
        lod_p = (t_gl @ np.c_[lod_p, np.ones(len(lod_p))].T).T[:, :3]
        for label, pos, uv, tris in (("lod0_levelled", lod_p, lod_uv, lod_t),
                                     ("bind_pose", glb.pose(), glb.uv, glb.tris)):
            obj, _rep = display_mesh(label, gl_to_bl_points(pos), tris, uv, material)
            for view in ("front34", "side", "front", "top"):
                aim(cam, *VIEWS[view])
                written.append(render_to(scene, os.path.join(out, "bind", f"{label}_{view}.png")))
            bpy.data.objects.remove(obj, do_unlink=True)

    # 2. Weights: each vertex coloured by its dominant bone.
    top = glb.jidx[np.arange(len(glb.positions)), np.argmax(glb.weights, 1)]
    if want("weights"):
        obj, rep = display_mesh("weights", gl_to_bl_points(glb.positions), glb.tris, None, None)
        paint_dominant(obj, rep, top, len(glb.joint_names))
        for view in ("front34", "top", "side", "rear34"):
            aim(cam, *VIEWS[view])
            written.append(render_to(scene, os.path.join(out, "weights", f"weights_{view}.png")))
        bpy.data.objects.remove(obj, do_unlink=True)

    # 3. Clips, driven through the exported animation GLBs: textured from two views, and close up
    # in weight colours, where a torn or smeared joint shows as a colour boundary that parts.
    short = args.asset.replace("creature_", "", 1)
    picks = {"idle": [0.0, 1.0, 2.0, 3.0], "walk": [0.0, 0.3, 0.6, 0.9],
             "run": [0.0, 0.2, 0.4, 0.6], "attack": [0.0, 0.15, 0.25, 0.35, 0.45, 0.62, 0.8],
             "hit": [0.0, 0.14, 0.275, 0.45], "death": [0.0, 0.5, 1.0, 1.4, 1.9]}
    for kind in KINDS:
        if not want(kind):
            continue
        clip = Clip(os.path.join(paths["ready"], f"anim.creature.{short}.{kind}.glb"))
        obj, rep = display_mesh(kind, gl_to_bl_points(glb.positions), glb.tris, glb.uv, material)
        flat, _ = display_mesh(kind + "_weights", gl_to_bl_points(glb.positions), glb.tris, None, None)
        paint_dominant(flat, rep, top, len(glb.joint_names))
        for t in picks[kind]:
            posed = gl_to_bl_points(glb.pose(clip, t))
            for mesh_obj in (obj, flat):
                mesh_obj.data.vertices.foreach_set("co", posed[rep].ravel())
                mesh_obj.data.update()
            flat.hide_render = True
            obj.hide_render = False
            for view in ("front34", "side", "close34"):
                aim(cam, *VIEWS[view])
                written.append(render_to(scene, os.path.join(out, kind, f"{kind}_{t:05.2f}s_{view}.png")))
            flat.hide_render = False
            obj.hide_render = True
            for view in ("close34", "closerear"):
                aim(cam, *VIEWS[view])
                written.append(render_to(scene, os.path.join(out, kind + "_weights", f"{kind}_{t:05.2f}s_{view}.png")))
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.objects.remove(flat, do_unlink=True)
    print("ARTHROPOD_RENDER " + json.dumps({"count": len(written), "dir": out}))


# ------------------------------------------------------------------------------------------------

def staging_paths(outdir):
    return {"root": outdir,
            "rigged": os.path.join(outdir, "rigged"),
            "source": os.path.join(outdir, "animation", "source", "creatures"),
            "ready": os.path.join(outdir, "animation", "ready", "creatures"),
            "clips": os.path.join(outdir, "animation", "clips"),
            "reports": os.path.join(outdir, "reports"),
            "renders": os.path.join(outdir, "renders")}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", required=True, choices=["fit", "motion", "verify", "render"])
    parser.add_argument("--asset", default="creature_cave_hunting_spider")
    parser.add_argument("--input", default=None, help="LOD0 GLB (default ready/<asset>/<asset>.glb)")
    parser.add_argument("--outdir", default=None, help="default assets/_staging/rigs/<asset>")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--weights", default="heat+anatomy", choices=["heat", "heat+anatomy"])
    args = parser.parse_args(argv)
    args.input = args.input or os.path.join(ASSETS, "ready", args.asset, f"{args.asset}.glb")
    args.outdir = args.outdir or os.path.join(ASSETS, "_staging", "rigs", args.asset)
    staging = os.path.normcase(os.path.abspath(os.path.join(ASSETS, "_staging")))
    target = os.path.normcase(os.path.abspath(args.outdir))
    if target.startswith(os.path.normcase(os.path.abspath(ASSETS))) and not target.startswith(staging):
        raise SystemExit(f"--outdir inside assets/ must be under {staging}")
    return args


def main():
    args = parse_args()
    paths = staging_paths(args.outdir)
    {"fit": run_fit, "motion": run_motion, "verify": run_verify, "render": run_render}[args.mode](args, paths)
    return 0


if __name__ == "__main__" and bpy is not None:
    sys.exit(main())
