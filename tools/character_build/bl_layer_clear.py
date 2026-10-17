"""WP17: clear one garment of the surfaces under it at rest, in a built character's .glb (production MPFB players: a top over its base
layer and the skin). build_char.py's clearance smooths its pushes, so a small feature under a close-fitting top (the bust under the
linen top) stays through it; here every outer vertex closer than the clearance to an inner surface along its own inward normal - or
behind it, by no more than --max-push - is pushed out to the clearance, the push grown one ring and eased (never below what was asked). LOD0 exactly, the LODs
by their nearest LOD0 points. The .glb is rewritten in place (the build's .blend keeps the original).

blender -b --factory-startup --python bl_layer_clear.py -- --glb <id>_rigged.glb --outer <mesh name> --inner <mesh,mesh>
    [--clear 0.005] [--reach 0.03] [--max-push 0.008]
"""
import argparse
import os
import sys

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from glb import Glb  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--glb", required=True)
ap.add_argument("--outer", required=True)
ap.add_argument("--inner", required=True)
ap.add_argument("--clear", type=float, default=0.005)
ap.add_argument("--reach", type=float, default=0.03)
ap.add_argument("--max-push", type=float, default=0.008, help="a deeper crossing is another part (the neck in a collar), not a poke")
a = ap.parse_args(argv)


def mesh(g, name):
    """Positions and triangles of a node's mesh (all primitives), and each primitive's (accessor, start, count)."""
    node = g.node(name)
    pos, tri, parts, base = [], [], [], 0
    for prim in g.doc["meshes"][node["mesh"]]["primitives"]:
        p = g.read(prim["attributes"]["POSITION"])
        pos.append(p)
        tri.append(g.read(prim["indices"]).astype(np.int64).reshape(-1, 3) + base)
        parts.append((prim["attributes"]["POSITION"], base, len(p)))
        base += len(p)
    return np.vstack(pos), np.vstack(tri), parts


def normals(pos, tris):
    _, weld = np.unique(np.round(pos / 1e-5).astype(np.int64), axis=0, return_inverse=True)
    weld = weld.ravel()
    face = np.cross(pos[tris[:, 1]] - pos[tris[:, 0]], pos[tris[:, 2]] - pos[tris[:, 0]])
    acc = np.zeros((weld.max() + 1, 3))
    for k in range(3):
        np.add.at(acc, weld[tris[:, k]], face)
    n = acc[weld]
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12), weld


g = Glb(a.glb)
pos, tris, parts = mesh(g, a.outer)
nrm, weld = normals(pos, tris)
inner_v, inner_t = [], []
for name in a.inner.split(","):
    p, t, _ = mesh(g, name)
    inner_t.append(t + sum(len(v) for v in inner_v))
    inner_v.append(p)
bvh = BVHTree.FromPolygons([Vector(v) for v in np.vstack(inner_v)], [tuple(int(x) for x in t) for t in np.vstack(inner_t)])
push = np.zeros(len(pos))
for i, (p, n) in enumerate(zip(pos, nrm)):
    o = Vector(p) + Vector(n) * a.reach
    hit = bvh.ray_cast(o, Vector(-n), a.reach * 2)
    if hit[0] is not None:
        gap = a.reach - hit[3]          # inner surface's height below this vertex (negative: it stands above it)
        if gap < a.clear and a.clear - gap <= a.max_push:
            push[i] = a.clear - gap
w = np.zeros(weld.max() + 1)
np.maximum.at(w, weld, push)
edges = np.vstack([weld[tris][:, [0, 1]], weld[tris][:, [1, 2]], weld[tris][:, [2, 0]]])
edges = np.vstack([edges, edges[:, ::-1]])
grown = w.copy()
np.maximum.at(grown, edges[:, 0], w[edges[:, 1]])
for _ in range(2):
    avg = np.zeros_like(grown)
    np.add.at(avg, edges[:, 0], grown[edges[:, 1]])
    grown = np.maximum(w, avg / np.maximum(np.bincount(edges[:, 0], minlength=len(grown)), 1))
move = nrm * grown[weld][:, None]
for acc, start, count in parts:
    g.write(acc, pos[start:start + count] + move[start:start + count])
tree = KDTree(len(pos))
for i, p in enumerate(pos):
    tree.insert(p, i)
tree.balance()
lods = [n["name"] for n in g.doc["nodes"] if n.get("name", "").startswith(a.outer + "_LOD")]
for name in lods:
    for acc, start, count in mesh(g, name)[2]:
        p = g.read(acc)
        g.write(acc, p + np.array([move[tree.find(q)[1]] for q in p]))
g.save(a.glb)
print("CLEAR", a.outer, "vertices pushed", int((push > 0).sum()), "max mm", round(float(grown.max()) * 1000, 1), "LODs", lods)
