"""The clip gate for MetaHuman outfits (CHARACTER_ANIMATION_STRATEGY.md 4.3 rule 8, Phase 4.3): the deformation the GAME draws, posed
through clips, checked for skin or inner layers showing through. A port of asset_work/characters/_tools/qa_gate.py to the new stack.

blender -b --factory-startup --python mh_gate.py -- --body <rigged body glb> --garments <garment glb,...> --clips <clip glb,...>
    --out <report.json> [--phases 12] [--tol-mm 3]

The deformation is computed here from the glTF itself, as Godot computes it - not through Blender's armature, whose bone frames
differ: each clip's joint transforms (linear keys), the body's corrective helper joints from <body>_correctives.json with the same
arithmetic as Art/CorrectiveJoints.cs, linear blend skinning with the inverse bind matrices. Region masks as the shaders apply them:
the body hides every region a garment covers, each garment the regions of the garments on higher layers (layer and hides_mask from
each <garment>_garment.json); a triangle with any hidden corner is not drawn. Blender is used only for its closest-point trees.

Fails, per memo 4.3 rule 8, when in any sampled pose
  * skin: a drawn body or head vertex stands outside a garment - a ray inward along its normal meets the garment more than tol below it,
    further than 15 mm from the garment's border (the visible margin) - more than 0 is a failure;
  * gaps: a body vertex the masks hide that no garment encloses (a ray outward meets none within 30 cm) - the hole shows; more than 0;
  * layers: a drawn vertex of a lower garment standing outside a higher one the same way - more than 10 per layer pair in locomotion
    clips is a failure (25 in get-ups);
  * hair: a hair vertex stands outside headwear - a ray from it towards the head joint meets the garment before the skull; more than 0.
Spring chains and twist modifiers are not simulated (garments rest on their chains); helpers are.

WP17 split of the skin count: a skin vertex through a garment is "own" when the garment is worn over its region (the garment's
rest coverage of that region >= 50%, from <garment>_garment.json) - true clipping of the cloth - and "contact" otherwise: a hand or
forearm the clip pushes into the trousers or the belly, which is the clip's or the build's pose, not the cloth (clip QA). Both are
reported per pose (skin_own, skin_contact); "skin" stays their sum.
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--body", required=True)
ap.add_argument("--garments", default="")
ap.add_argument("--clips", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--phases", type=int, default=12)
ap.add_argument("--tol-mm", type=float, default=3.0)
ap.add_argument("--arm-splay-deg", type=float, default=0.0, help="the body's build pose offset (body content arm_splay_deg; Art/BuildPose.cs)")
ap.add_argument("--fix", action="store_true", help="push each garment out, at rest, by the most any sampled pose let the cloth it covers "
                                                   "or a lower layer through, and rewrite the garment glbs (clearance from motion)")
a = ap.parse_args(argv)
TOL = a.tol_mm / 1000
COVER = 0.03   # how far inward a covering layer is looked for
MARGIN = 0.015  # memo 4.3: 15 mm visible margin at a garment border
REACH = 0.30   # how far outward a hidden body point looks for its garment (loose cloth stands well off)
DEPTH = 0.01   # ...starting this far under the skin
GETUP = ("getup", "get_up")
REGION_NAMES = {i: n for n, i in json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "regions.json")))["regions"].items()}

COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Gltf:
    """The parts of a .glb the gate needs: accessors, nodes (name, local TRS, parent), skins, meshes, animations."""

    def __init__(self, path):
        raw = open(path, "rb").read()
        length = struct.unpack_from("<I", raw, 12)[0]
        self.doc = json.loads(raw[20:20 + length])
        at = 20 + length
        self.bin = raw[at + 8:at + 8 + struct.unpack_from("<I", raw, at)[0]] if at < len(raw) else b""
        self.parent = {c: i for i, n in enumerate(self.doc["nodes"]) for c in n.get("children", [])}

    def accessor(self, index):
        acc = self.doc["accessors"][index]
        view = self.doc["bufferViews"][acc["bufferView"]]
        dtype = COMPONENT[acc["componentType"]]
        width = WIDTH[acc["type"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride", 0) or np.dtype(dtype).itemsize * width
        rows = np.ndarray((acc["count"], width), dtype=dtype, buffer=self.bin, offset=start, strides=(stride, np.dtype(dtype).itemsize))
        out = rows.astype(np.float64)
        if acc.get("normalized"):
            out /= np.iinfo(dtype).max
        return out

    def local(self, i):
        n = self.doc["nodes"][i]
        if "matrix" in n:
            return np.array(n["matrix"], dtype=np.float64).reshape(4, 4).T
        return trs(n.get("translation", [0, 0, 0]), n.get("rotation", [0, 0, 0, 1]), n.get("scale", [1, 1, 1]))


def quat_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs(t, q, s):
    m = np.eye(4)
    m[:3, :3] = quat_matrix(q) * np.array(s)
    m[:3, 3] = t
    return m


def qmul(p, q):
    px, py, pz, pw = p
    qx, qy, qz, qw = q
    return np.array([pw * qx + px * qw + py * qz - pz * qy, pw * qy - px * qz + py * qw + pz * qx,
                     pw * qz + px * qy - py * qx + pz * qw, pw * qw - px * qx - py * qy - pz * qz])


def slerp(p, q, f):
    d = float(np.dot(p, q))
    if d < 0:
        q, d = -q, -d
    if d > 0.9995:
        r = p + (q - p) * f
        return r / np.linalg.norm(r)
    th = math.acos(d)
    return (math.sin((1 - f) * th) * p + math.sin(f * th) * q) / math.sin(th)


class Clip:
    """A clip GLB's animation by joint name: per joint, sampled translation / rotation / scale at a time."""

    def __init__(self, path):
        g = Gltf(path)
        anim = g.doc["animations"][0]
        self.name = os.path.basename(path)
        self.tracks = {}
        self.length = 0.0
        for ch in anim["channels"]:
            s = anim["samplers"][ch["sampler"]]
            times = g.accessor(s["input"])[:, 0]
            values = g.accessor(s["output"])
            node = g.doc["nodes"][ch["target"]["node"]].get("name", "")
            self.tracks.setdefault(node, {})[ch["target"]["path"]] = (times, values, s.get("interpolation", "LINEAR"))
            self.length = max(self.length, float(times[-1]))

    def sample(self, joint, path, t):
        track = self.tracks.get(joint, {}).get(path)
        if track is None:
            return None
        times, values, interp = track
        if t <= times[0]:
            return values[0]
        if t >= times[-1]:
            return values[-1]
        i = int(np.searchsorted(times, t)) - 1
        if interp == "STEP":
            return values[i]
        f = (t - times[i]) / (times[i + 1] - times[i])
        return slerp(values[i], values[i + 1], f) if path == "rotation" else values[i] + (values[i + 1] - values[i]) * f


class Skinned:
    """One skinned mesh of a GLB (LOD0 only): positions, triangles, region per vertex, joints and weights, its skin's joint names and
    inverse bind matrices."""

    def __init__(self, g, node_index):
        node = g.doc["nodes"][node_index]
        mesh = g.doc["meshes"][node["mesh"]]
        skin = g.doc["skins"][node["skin"]]
        self.name = node.get("name", "")
        self.joint_names = [g.doc["nodes"][j].get("name", "") for j in skin["joints"]]
        ibm = g.accessor(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        pos, tri, reg, jnt, wgt = [], [], [], [], []
        base = 0
        for prim in mesh["primitives"]:
            at = prim["attributes"]
            p = g.accessor(at["POSITION"])
            pos.append(p)
            tri.append(g.accessor(prim["indices"]).astype(np.int64).reshape(-1, 3) + base)
            reg.append(np.rint(g.accessor(at["TEXCOORD_1"])[:, 0]).astype(int) if "TEXCOORD_1" in at else np.full(len(p), -1))
            jnt.append(g.accessor(at["JOINTS_0"]).astype(int))
            wgt.append(g.accessor(at["WEIGHTS_0"]))
            base += len(p)
        self.positions = np.vstack(pos)
        self.triangles = np.vstack(tri)
        self.regions = np.concatenate(reg)
        self.joints = np.vstack(jnt)
        self.weights = np.vstack(wgt)
        self.weights /= np.maximum(self.weights.sum(1, keepdims=True), 1e-9)
        self.ibm = ibm

    def deform(self, globals_by_name):
        mats = np.stack([globals_by_name[n] for n in self.joint_names]) @ self.ibm
        homo = np.hstack([self.positions, np.ones((len(self.positions), 1))])
        out = np.zeros((len(self.positions), 3))
        for k in range(4):
            m = mats[self.joints[:, k]]
            out += self.weights[:, k:k + 1] * np.einsum("nij,nj->ni", m, homo)[:, :3]
        return out


class Skin:
    """Several skinned meshes as one surface (the body and the head: skin, regions and the flesh rays stop at)."""

    def __init__(self, meshes):
        self.meshes = meshes
        offsets = np.cumsum([0] + [len(m.positions) for m in meshes])
        self.positions = np.vstack([m.positions for m in meshes])
        self.triangles = np.vstack([m.triangles + o for m, o in zip(meshes, offsets)])
        self.regions = np.concatenate([m.regions for m in meshes])

    def deform(self, globals_by_name):
        return np.vstack([m.deform(globals_by_name) for m in self.meshes])


def lod0_skinned(g):
    return [Skinned(g, i) for i, n in enumerate(g.doc["nodes"]) if "mesh" in n and "skin" in n and "_LOD" not in n.get("name", "")
            and not n.get("name", "").endswith("_shadow")]


def skeleton(g):
    """Joint name -> (node index, parent joint name or None, rest local matrix), and the matrix of the joints' root above them."""
    joints = {g.doc["nodes"][j].get("name", ""): j for s in g.doc["skins"] for j in s["joints"]}
    by_index = {i: n for n, i in joints.items()}
    out = {}
    for name, i in joints.items():
        p = g.parent.get(i)
        out[name] = (i, by_index.get(p), g.local(i))
    roots = {i for n, (i, p, _) in out.items() if p is None}
    above = {}
    for i in roots:
        m = np.eye(4)
        p = g.parent.get(i)
        while p is not None:
            m = g.local(p) @ m
            p = g.parent.get(p)
        above[i] = m
    return out, above


def correctives(path, rest):
    if not os.path.exists(path):
        return None
    d = json.load(open(path))
    return d["angles_deg"], d["helpers"]


def apply_correctives(table, rest_locals, locals_):
    """Art/CorrectiveJoints.cs: each helper's local pose from its driver's rotation away from rest (rotation vector, degrees)."""
    angles, helpers = table
    first, step = angles[0], angles[1] - angles[0]
    for name, h in helpers.items():
        driver = h["driver"]
        rq = mat_quat(rest_locals[driver][:3, :3])
        pq = mat_quat(locals_[driver][:3, :3] / np.linalg.norm(locals_[driver][:3, 0]))
        away = qmul(np.array([-rq[0], -rq[1], -rq[2], rq[3]]), pq)
        if away[3] < 0:
            away = -away
        half = math.acos(min(1.0, max(-1.0, away[3])))
        s = math.sin(half)
        deg = np.zeros(3) if s < 1e-6 else away[:3] / s * math.degrees(2 * half)
        move, turn = np.zeros(3), np.zeros(3)
        for axis, tab in h["axes"].items():
            at = min(max((deg["xyz".index(axis)] - first) / step, 0), len(tab["t"]) - 1)
            i = min(int(at), len(tab["t"]) - 2)
            f = at - i
            move += np.array(tab["t"][i]) * (1 - f) + np.array(tab["t"][i + 1]) * f
            turn += np.array(tab["r"][i]) * (1 - f) + np.array(tab["r"][i + 1]) * f
        rest = rest_locals[name]
        rot = rest[:3, :3]
        angle = float(np.linalg.norm(turn))
        if angle > 1e-9:
            ax = turn / angle
            rot = quat_matrix([*(ax * math.sin(angle / 2)), math.cos(angle / 2)]) @ rot
        m = np.eye(4)
        m[:3, :3] = rot
        m[:3, 3] = rest[:3, 3] + move
        locals_[name] = m


def mat_quat(m):
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1) * 2
        return np.array([(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, s / 4])
    i = int(np.argmax([m[0, 0], m[1, 1], m[2, 2]]))
    j, k = (i + 1) % 3, (i + 2) % 3
    s = math.sqrt(1 + m[i, i] - m[j, j] - m[k, k]) * 2
    q = np.zeros(4)
    q[i] = s / 4
    q[j] = (m[j, i] + m[i, j]) / s
    q[k] = (m[k, i] + m[i, k]) / s
    q[3] = (m[k, j] - m[j, k]) / s
    return q


def rotation(axis, angle):
    axis = axis / np.linalg.norm(axis)
    x, y, z = axis * math.sin(angle / 2)
    return quat_matrix([x, y, z, math.cos(angle / 2)])


def splay(skel, above, locals_, rest_globals, degrees):
    """Art/BuildPose.cs: each upper arm turned out from the trunk by the build's arm splay, about the body's forward axis at the
    shoulder, after the clip - a heavier build's arms swing clear of its own sides with every clip unchanged."""
    g = resolve_all(skel, above, locals_)
    pelvis = g["pelvis"][:3, :3] @ rest_globals["pelvis"][:3, :3].T
    across = pelvis @ (rest_globals["thigh_l"][:3, 3] - rest_globals["thigh_r"][:3, 3])
    across /= np.linalg.norm(across)
    forward = np.cross(pelvis @ np.array([0.0, 1.0, 0.0]), across)
    for side, out in (("l", across), ("r", -across)):
        upper, parent = f"upperarm_{side}", skel[f"upperarm_{side}"][1]
        d = g[f"lowerarm_{side}"][:3, 3] - g[upper][:3, 3]
        sign = 1.0 if np.dot(np.cross(forward, d), out) > 0 else -1.0
        turn = np.eye(4)
        turn[:3, :3] = rotation(forward, sign * math.radians(degrees))
        about = np.eye(4)
        about[:3, 3] = g[upper][:3, 3]
        world = about @ turn @ np.linalg.inv(about)
        locals_[upper] = np.linalg.inv(g[parent]) @ world @ g[parent] @ locals_[upper]


def resolve_all(skel, above, locals_):
    globals_ = {}

    def resolve(n):
        if n in globals_:
            return globals_[n]
        i, p, _ = skel[n]
        globals_[n] = (resolve(p) if p else above[i]) @ locals_[n]
        return globals_[n]

    for n in skel:
        resolve(n)
    return globals_


def pose(skel, above, clip, t, table, rest_globals=None):
    rest_locals = {n: r for n, (_, _, r) in skel.items()}
    locals_ = {}
    for n, (_, _, rest) in skel.items():
        tr = clip.sample(n, "translation", t)
        ro = clip.sample(n, "rotation", t)
        sc = clip.sample(n, "scale", t)
        if tr is None and ro is None and sc is None:
            locals_[n] = rest
            continue
        rq = mat_quat(rest[:3, :3])
        locals_[n] = trs(rest[:3, 3] if tr is None else tr, rq if ro is None else ro, [1, 1, 1] if sc is None else sc)
    if a.arm_splay_deg:
        splay(skel, above, locals_, rest_globals, a.arm_splay_deg)
    if table:
        apply_correctives(table, rest_locals, locals_)
    return resolve_all(skel, above, locals_)


def drawn(mesh, hidden_mask):
    """Per vertex: drawn (no triangle it belongs to is dropped by the mask). A vertex of a hidden region is never drawn."""
    hidden = np.array([(hidden_mask >> int(r)) & 1 if r >= 0 else 0 for r in mesh.regions], dtype=bool)
    tri_hidden = hidden[mesh.triangles].any(1)
    shown = np.zeros(len(mesh.positions), dtype=bool)
    shown[mesh.triangles[~tri_hidden].ravel()] = True
    return shown, mesh.triangles[~tri_hidden]


def normals(verts, tris):
    n = np.zeros_like(verts)
    face = np.cross(verts[tris[:, 1]] - verts[tris[:, 0]], verts[tris[:, 2]] - verts[tris[:, 0]])
    for k in range(3):
        np.add.at(n, tris[:, k], face)
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


def border(verts, tris):
    """The vertices on a surface's open border (edges with one drawn triangle). Welded by position first: glTF splits vertices at
    every UV seam and hard edge, and counted by index those seams were 'borders' that excused show-through near them."""
    _, weld = np.unique(np.round(verts / 1e-5).astype(np.int64), axis=0, return_inverse=True)
    weld = weld.ravel()
    w = weld[tris]
    edges = np.sort(np.vstack([w[:, [0, 1]], w[:, [1, 2]], w[:, [2, 0]]]), axis=1)
    uniq, count = np.unique(edges, axis=0, return_counts=True)
    return np.flatnonzero(np.isin(weld, uniq[count == 1].ravel()))


class Cover:
    """A drawn surface in its current pose, for ray tests, with its border for the 15 mm margin."""

    def __init__(self, verts, tris):
        self.tris = tris
        self.bvh = BVHTree.FromPolygons([Vector(v) for v in verts], [tuple(int(x) for x in t) for t in tris])
        rim = verts[border(verts, tris)] if len(tris) else np.zeros((0, 3))
        self.rim = BVHTree.FromPolygons([Vector(v) for v in rim], [(i, i, i) for i in range(len(rim))]) if len(rim) else None

    def near_rim(self, point):
        return self.rim is not None and self.rim.find_nearest(point, MARGIN)[0] is not None


def first(bvh, origin, direction, reach):
    hit = bvh.ray_cast(origin, direction, reach)
    return hit[3] if hit[0] is not None else None


def show_through(points, normals_, shown, cover, flesh, regions=None, tally=None, push=None, own=None):
    """Shown points standing outside the covering surface: a ray inward meets it more than TOL below, away from its border, before it
    meets the body's own surface (a ray through a hand that reaches the trousers beside it is not a hand under the trousers).
    With push (per covering vertex, metres): each point of a region the cover is worn over (own; all when own is None) asks the
    corners of the triangle it is under to stand out by its depth plus TOL."""
    count = 0
    for i, (p, n, s) in enumerate(zip(points, normals_, shown)):
        if not s:
            continue
        o, d = Vector(p) - Vector(n) * 0.0005, Vector(-n)
        hit, _, _, dist = cover.bvh.ray_cast(o, d, COVER)
        if hit is None or dist <= TOL or cover.near_rim(hit):
            continue
        body = first(flesh, o, d, COVER)
        if body is None or body > dist:
            count += 1
            if tally is not None:
                tally[int(regions[i])] = tally.get(int(regions[i]), 0) + 1
            if push is not None and (own is None or int(regions[i]) in own):
                corners = cover.tris[cover.bvh.ray_cast(o, d, COVER)[2]]
                push[corners] = np.maximum(push[corners], dist + TOL)
    return count


def gaps(points, normals_, hidden, regions, covers, flesh):
    """Hidden body points no garment encloses: a ray outward reaches open space - meeting neither a garment nor the body itself within
    REACH - so the hole shows. By region."""
    by_region = {}
    for p, n, h, r in zip(points, normals_, hidden, regions):
        if not h:
            continue
        o, d = Vector(p) + Vector(n) * 0.0005, Vector(n)
        if first(flesh, o, d, REACH) is not None:
            continue  # the body itself hides it (between toes, the armpit)
        # from 1 cm under the skin: a garment lying a little inside the (hidden) skin still covers the view
        inside = Vector(p) - Vector(n) * DEPTH
        if not any(first(c.bvh, inside, d, DEPTH + REACH) is not None for c in covers):
            by_region[int(r)] = by_region.get(int(r), 0) + 1
    return by_region


def fix(g):
    """Push the garment out at rest along its normals by what the poses asked of it (grown one ring and eased so the cloth moves as a
    whole, never less than asked), in its glb: LOD0 exactly, the LODs by their nearest LOD0 points. Returns the largest push, mm."""
    mesh, push = g["mesh"], g["push"]
    if not push.any():
        return 0.0
    _, weld = np.unique(np.round(mesh.positions / 1e-5).astype(np.int64), axis=0, return_inverse=True)
    weld = weld.ravel()
    w = np.zeros(weld.max() + 1)
    np.maximum.at(w, weld, push)
    tri = weld[mesh.triangles]
    edges = np.vstack([tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]]])
    edges = np.vstack([edges, edges[:, ::-1]])
    grown = w.copy()
    np.maximum.at(grown, edges[:, 0], w[edges[:, 1]])
    for _ in range(2):
        avg = np.zeros_like(grown)
        np.add.at(avg, edges[:, 0], grown[edges[:, 1]])
        grown = np.maximum(w, avg / np.maximum(np.bincount(edges[:, 0], minlength=len(grown)), 1))
    move = normals(mesh.positions, mesh.triangles) * grown[weld][:, None]
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from glb import Glb
    from mathutils.kdtree import KDTree
    tree = KDTree(len(mesh.positions))
    for i, p in enumerate(mesh.positions):
        tree.insert(p, i)
    tree.balance()
    doc = Glb(g["path"])
    for node in doc.doc["nodes"]:
        if "mesh" not in node or not node.get("name", "").startswith(g["id"]):
            continue
        base = 0
        for prim in doc.doc["meshes"][node["mesh"]]["primitives"]:
            at = prim["attributes"]["POSITION"]
            pos = doc.read(at)
            if node["name"] == g["id"]:
                d = move[base:base + len(pos)]
                base += len(pos)
            else:
                d = np.array([move[tree.find(p)[1]] for p in pos])
            doc.write(at, pos + d)
    doc.save(g["path"])
    return round(float(grown.max()) * 1000, 1)


def main():
    body_g = Gltf(a.body)
    skel, above = skeleton(body_g)
    parts = {m.name.rsplit("_", 1)[-1]: m for m in lod0_skinned(body_g)}
    if "body" not in parts or "head" not in parts:
        raise SystemExit(f"{a.body}: no <asset>_body and <asset>_head meshes")
    body = Skin([parts["body"], parts["head"]])
    hair = parts.get("hair")
    table = correctives(a.body.replace("_rigged.glb", "_correctives.json"), skel)
    garments = []
    for path in [p for p in a.garments.split(",") if p]:
        spec = json.load(open(path.replace("_rigged.glb", "_garment.json")))
        mesh = lod0_skinned(Gltf(path))[0]
        names = {n: i for i, n in REGION_NAMES.items()}
        covers = {names[r] for r, share in spec.get("coverage", {}).items() if share >= 0.5 and r in names}
        garments.append({"id": spec["id"], "layer": spec["layer"], "hides": spec["hides_mask"], "mesh": mesh, "covers": covers,
                         "path": path, "push": np.zeros(len(mesh.positions)) if a.fix else None})
    garments.sort(key=lambda g: g["layer"])
    body_hidden = 0
    for g in garments:
        body_hidden |= g["hides"]
    for g in garments:
        g["hidden"] = 0
        for h in garments:
            if h["layer"] > g["layer"]:
                g["hidden"] |= h["hides"]
    rest_globals = resolve_all(skel, above, {n: r for n, (_, _, r) in skel.items()})
    report = {"body": os.path.basename(a.body), "garments": [g["id"] for g in garments], "correctives": bool(table),
              "arm_splay_deg": a.arm_splay_deg,
              "tol_mm": a.tol_mm, "phases": a.phases, "clips": {}, "fail": []}
    body_shown, _ = drawn(body, body_hidden)
    for g in garments:
        g["shown"], g["tris"] = drawn(g["mesh"], g["hidden"])
    for path in a.clips.split(","):
        clip = Clip(path)
        state = clip.name.replace("anim.", "").replace(".glb", "")
        limit = 25 if any(k in state for k in GETUP) else 10
        rows = []
        for k in range(a.phases):
            t = clip.length * k / a.phases
            G = pose(skel, above, clip, t, table, rest_globals)
            bv = body.deform(G)
            bn = normals(bv, body.triangles)
            row = {"t": round(t, 3), "skin": 0, "gaps": 0, "layers": {}}
            for g in garments:
                g["now"] = g["mesh"].deform(G)
                g["normals"] = normals(g["now"], g["mesh"].triangles)
                g["cover"] = Cover(g["now"], g["tris"])
            flesh = BVHTree.FromPolygons([Vector(v) for v in bv], [tuple(int(x) for x in t) for t in body.triangles])
            row["skin_by"] = {}
            row["skin_own"] = row["skin_contact"] = 0
            for g in garments:
                tally = {}
                row["skin"] += show_through(bv, bn, body_shown, g["cover"], flesh, body.regions, tally, g["push"], g["covers"])
                for r, n in tally.items():
                    row["skin_by"][f"{REGION_NAMES.get(r, r)}>{g['id']}"] = n
                    row["skin_own" if r in g["covers"] else "skin_contact"] += n
            holes = gaps(bv, bn, ~body_shown, body.regions, [g["cover"] for g in garments], flesh)
            row["gaps"] = sum(holes.values())
            if hair is not None:
                # hair standing outside headwear: a ray from each hair vertex towards the head joint meets a garment before the skull
                hv = hair.deform(G)
                centre = (G["head"] @ np.array([0.0, 0.0, 0.0, 1.0]))[:3]
                out = (hv - centre) / np.maximum(np.linalg.norm(hv - centre, axis=1, keepdims=True), 1e-9)
                row["hair_by"] = {g["id"]: n for g in garments
                                  if (n := show_through(hv, out, np.ones(len(hv), dtype=bool), g["cover"], flesh))}
                row["hair"] = sum(row["hair_by"].values())
            row["gaps_by_region"] = {REGION_NAMES.get(r, str(r)): n for r, n in sorted(holes.items())}
            for lo in garments:
                for hi in garments:
                    if hi["layer"] > lo["layer"]:
                        n = show_through(lo["now"], lo["normals"], lo["shown"], hi["cover"], flesh, push=hi["push"])
                        if n:
                            row["layers"][f"{lo['id']}<{hi['id']}"] = n
            rows.append(row)
            if row["skin"] > 0:
                report["fail"].append(f"{state} t={row['t']}: {row['skin']} skin vertices show through")
            if row["gaps"] > 0:
                report["fail"].append(f"{state} t={row['t']}: {row['gaps']} hidden body vertices no garment encloses")
            if row.get("hair", 0) > 0:
                report["fail"].append(f"{state} t={row['t']}: {row['hair']} hair vertices stand outside headwear")
            for pair, n in row["layers"].items():
                if n > limit:
                    report["fail"].append(f"{state} t={row['t']}: {pair} {n} > {limit}")
        report["clips"][state] = {"length_s": round(clip.length, 3), "worst_skin": max(r["skin"] for r in rows),
                                  "worst_skin_own": max(r["skin_own"] for r in rows), "worst_skin_contact": max(r["skin_contact"] for r in rows),
                                  "worst_gaps": max(r["gaps"] for r in rows), "worst_hair": max(r.get("hair", 0) for r in rows),
                                  "worst_layer": max([max(r["layers"].values()) for r in rows if r["layers"]] or [0]), "phases": rows}
    report["pass"] = not report["fail"]
    if a.fix:
        report["fixed_mm"] = {g["id"]: fix(g) for g in garments}
    json.dump(report, open(a.out, "w"), indent=1)
    print("MHGATE", "PASS" if report["pass"] else "FAIL", json.dumps({s: {"skin": c["worst_skin"], "gaps": c["worst_gaps"], "hair": c["worst_hair"], "layer": c["worst_layer"]} for s, c in report["clips"].items()}))


main()
