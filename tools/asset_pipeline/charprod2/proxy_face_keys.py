"""The face's shape keys given to the pieces that sit on it (charprod2): brows, lashes, teeth, tongue and eyes follow the skin under each
of their vertices (its nearest body triangle, barycentrically) for every face key the body has, so a blink closes the lashes with
the lids and jawOpen takes the lower teeth with the jaw. MPFB puts its ARKit face units on the body only.

    blender -b --python proxy_face_keys.py -- --blend in.blend --out out.blend [--prefix-skip $md]

Keys skipped: the body's macro targets (names starting "$md"; the proxies were fitted to the macro shape already) and Basis. Teeth
and tongue take only the jaw and mouth keys (the rest move the face's surface, not the mouth's inside); the eyeballs take none
(they turn on their own bones).
"""
import os
import sys

import bpy
import numpy as np
import addon_utils
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
skip = arg("--prefix-skip", "$md")
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
blocks = body.data.shape_keys.key_blocks
keys = [k for k in blocks if k.name != "Basis" and not k.name.startswith(skip)]
MOUTH = ("jaw", "mouth", "tongue")


def world(co):
    M = np.array(body.matrix_world)
    return co @ M[:3, :3].T + M[:3, 3]


def key_co(kb):
    co = np.empty(len(kb.data) * 3, np.float32)
    kb.data.foreach_get("co", co)
    return world(co.reshape(-1, 3).astype(np.float64))


# The body at rest as the proxies were fitted to it: every key at its current value (the macro mix), the face keys at zero.
saved = {k.name: k.value for k in keys}
for k in keys:
    k.value = 0.0
masks = [m for m in body.modifiers if m.type == "MASK" and m.show_viewport]
for m in masks:
    m.show_viewport = False
e = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
me = e.to_mesh()
rest = world(np.array([tuple(v.co) for v in me.vertices]))
e.to_mesh_clear()
for m in masks:
    m.show_viewport = True
tri_list = []
for p in body.data.polygons:
    vs = list(p.vertices)
    for i in range(1, len(vs) - 1):
        tri_list.append((vs[0], vs[i], vs[i + 1]))
tri_list = np.array(tri_list)
bvh = BVHTree.FromPolygons([Vector(c) for c in rest], [tuple(t) for t in tri_list])
basis = key_co(blocks["Basis"])
deltas = {k.name: key_co(k) - basis for k in keys}   # each face key's own motion (relative to Basis, as Blender applies it)


def bary(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18:
        return np.array([1.0, 0.0, 0.0])
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return np.clip(np.array([1 - v - w, v, w]), 0, 1)


report = {}
for o in bpy.data.objects:
    if o.type != "MESH" or o is body:
        continue
    k = ObjectService.get_object_type(o)
    if k not in ("Eyebrows", "Eyelashes", "Teeth", "Tongue"):
        continue
    oe = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
    om = oe.to_mesh()
    Mo = np.array(o.matrix_world)
    pts = np.array([tuple(v.co) for v in om.vertices]) @ Mo[:3, :3].T + Mo[:3, 3]
    oe.to_mesh_clear()
    idx, wts = [], []
    for p in pts:
        loc, _, fi, _ = bvh.find_nearest(Vector(p))
        t = tri_list[fi]
        idx.append(t)
        wts.append(bary(np.array(loc), rest[t[0]], rest[t[1]], rest[t[2]]))
    idx, wts = np.array(idx), np.array(wts)
    inv = np.linalg.inv(Mo[:3, :3])
    if o.data.shape_keys is None:
        o.shape_key_add(name="Basis", from_mix=False)
    ob_basis = np.empty(len(o.data.vertices) * 3, np.float32)
    o.data.shape_keys.key_blocks["Basis"].data.foreach_get("co", ob_basis)
    ob_basis = ob_basis.reshape(-1, 3)
    # Teeth and tongue are rigid: each connected piece (the upper row, the lower row, the tongue) moves by the mean motion of the
    # skin under it, or its vertices follow different surfaces (the still palate, the moving jaw) and stretch into bars.
    pieces = None
    if k in ("Teeth", "Tongue"):
        parent = list(range(len(pts)))

        def find(i):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i
        for ed in o.data.edges:
            a_, b_ = find(ed.vertices[0]), find(ed.vertices[1])
            if a_ != b_:
                parent[a_] = b_
        roots = np.array([find(i) for i in range(len(pts))])
        pieces = [np.where(roots == r)[0] for r in np.unique(roots)]
    made = 0
    for name, d in deltas.items():
        if k in ("Teeth", "Tongue") and not name.lower().startswith(MOUTH):
            continue
        dv = (d[idx] * wts[..., None]).sum(1)
        if pieces is not None:
            for piece in pieces:
                dv[piece] = dv[piece].mean(0)
        dv = dv @ inv.T
        if np.abs(dv).max() < 1e-6:
            continue
        kb = o.data.shape_keys.key_blocks.get(name) or o.shape_key_add(name=name, from_mix=False)
        kb.data.foreach_set("co", (ob_basis + dv).astype(np.float32).ravel())
        made += 1
    report[o.name] = made
for k in keys:
    k.value = saved[k.name]
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(arg("--out")))
print("PROXY_FACE_KEYS", report)
