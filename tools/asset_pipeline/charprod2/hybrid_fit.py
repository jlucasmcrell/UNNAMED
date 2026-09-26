"""The hybrid player's registration (charprod2): a source character (the current production model, whose surface and identity the
owner has seen in game) laid onto a dressed MPFB body (mpfb_assemble.py, its head wrapped onto the source's by hybrid_wrap.py) so its
textures can be baked across (hybrid_bake.py).

    torso:  the source translated so its torso centroid sits on the MPFB body's
    head:   a similarity fit (scale, rotation, translation) of the source's head skin onto the MPFB head, for the hair shell
    arms:   the MPFB rig's upper arms and forearms swung (no twist) until their skin lies on the source's arms - a pose kept only for
            the bake; the rest pose is untouched
    hair:   the source's hair faces as a new object on the MPFB head (the head fit applied, pushed clear of the scalp), bound rigidly to
            the head bone, with its own fresh UV layout for its baked textures

    blender -b --python hybrid_fit.py -- --blend assembled.blend --source source.glb --out fitted.blend [--hair-material MAT_player_hair]
                                          [--skin-material MAT_player_skin]
"""
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
import addon_utils
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
hair_mat, skin_mat = arg("--hair-material", "MAT_player_hair"), arg("--skin-material", "MAT_player_skin")
out = os.path.abspath(arg("--out"))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for o in bpy.data.objects:
    if o.type == "MESH" and ObjectService.get_object_type(o) == "Hair":
        bpy.data.objects.remove(o)   # the source's hair replaces the proxy

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--source")))
imported = [o for o in bpy.data.objects if o not in before]
src = next(o for o in imported if o.type == "MESH" and o.data.materials and "LOD" not in o.name)
world = src.matrix_world.copy()
src.parent = None
for m in list(src.modifiers):
    src.modifiers.remove(m)
src.data.transform(world)
src.matrix_world = Matrix.Identity(4)
for o in imported:
    if o is not src:
        bpy.data.objects.remove(o)
src.name = "source"
# The body's own vertices, one to one, while fitting: its masks (the skin hidden under garments) are off until the save.
masks = [m for m in body.modifiers if m.type != "ARMATURE" and m.show_viewport]
for m in masks:
    m.show_viewport = False
bpy.context.view_layer.update()


def evaluated(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    e = obj.evaluated_get(dg)
    m = e.to_mesh()
    co = np.empty(len(m.vertices) * 3)
    m.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3) @ np.array(obj.matrix_world)[:3, :3].T + np.array(obj.matrix_world)[:3, 3]
    bvh = BVHTree.FromPolygons([Vector(c) for c in co], [tuple(p.vertices) for p in m.polygons])
    e.to_mesh_clear()
    return co, bvh


def coords(obj):
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def set_coords(obj, co):
    obj.data.vertices.foreach_set("co", co.astype(np.float32).ravel())
    obj.data.update()


def material_verts(obj, name):
    idx = next(i for i, m in enumerate(obj.data.materials) if m and m.name.startswith(name))
    keep = set()
    for p in obj.data.polygons:
        if p.material_index == idx:
            keep.update(p.vertices)
    return np.array(sorted(keep))


def nearest(bvh, pts):
    hit = [bvh.find_nearest(Vector(p)) for p in pts]
    ok = np.array([h[0] is not None for h in hit])
    loc = np.array([tuple(h[0]) if h[0] is not None else (0, 0, 0) for h in hit])
    nrm = np.array([tuple(h[1]) if h[0] is not None else (0, 0, 1) for h in hit])
    dist = np.array([h[3] if h[0] is not None else 1e9 for h in hit])
    return ok, loc, nrm, dist


# Torso: centroids of the band between the waist and the chest, near the midline so neither figure's arms count (the source's
# garments sit symmetrically over the MPFB skin).
bco, bbvh = evaluated(body)
sco = coords(src)
band = lambda c: c[(c[:, 2] > 1.05) & (c[:, 2] < 1.35) & (np.abs(c[:, 0]) < 0.12)]  # noqa: E731
shift = band(bco).mean(0) - band(sco).mean(0)
shift[2] = 0.0
sco = sco + shift
set_coords(src, sco)
report = {"torso_shift_m": shift.round(4).tolist()}

# Head: a trimmed similarity ICP of the source's head skin (face, ears, upper neck - the scalp is under its hair) onto the MPFB body.
skin = sco[material_verts(src, skin_mat)]
chin = bco[:, 2].max() - 0.26
P0 = skin[skin[:, 2] > chin]
T = np.eye(4)
for it in range(40):
    P = P0 @ T[:3, :3].T + T[:3, 3]
    ok, Q, _, d = nearest(bbvh, P)
    keep = ok & (d < max(np.median(d) * 2.5, 0.004))
    A, B = P[keep], Q[keep]
    ma, mb = A.mean(0), B.mean(0)
    X, Y = A - ma, B - mb
    U, S, Vt = np.linalg.svd(Y.T @ X / len(A))
    D = np.eye(3)
    D[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ D @ Vt
    # Nearest-point ICP with a free scale drifts small; the heads of two same-height figures differ by a few percent at most.
    s_now = float(np.cbrt(np.linalg.det(T[:3, :3])))
    s = float(np.clip(np.trace(np.diag(S) @ D) / X.var(0).sum(), 0.94 / s_now, 1.06 / s_now))
    step = np.eye(4)
    step[:3, :3] = s * R
    step[:3, 3] = mb - s * R @ ma
    T = step @ T
report["head_fit"] = {"scale": round(float(np.cbrt(np.linalg.det(T[:3, :3]))), 4), "median_residual_mm": round(float(np.median(d)) * 1000, 2),
                      "points": int(len(P0))}
report["head_T"] = T.tolist()

# Hair: the source's hair faces, head-fitted, pushed 3 mm clear of the scalp, bound to the head bone.
hair = src.copy()
hair.data = src.data.copy()
bpy.context.scene.collection.objects.link(hair)
hair.name = "Human.hair_source"
bm = bmesh.new()
bm.from_mesh(hair.data)
hidx = next(i for i, m in enumerate(hair.data.materials) if m and m.name.startswith(hair_mat))
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != hidx], context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(hair.data)
bm.free()
# The source's fringe pieces that hang below the brow line in front of the ears came from its own forehead; on another face they
# lie on the skin (the push-out below flattens them onto the brows and nose), so they are not part of the hair asset.
eyes = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.get_object_type(o) == "Eyes")
eco = evaluated(eyes)[0]
brow_z = float(eco[:, 2].mean()) + 0.035
head_front_y = float(eco[:, 1].mean()) + 0.04
hco0 = coords(hair) @ T[:3, :3].T + T[:3, 3]
bm = bmesh.new()
bm.from_mesh(hair.data)
bm.faces.ensure_lookup_table()
seen, doomed = set(), []
for f in bm.faces:
    if f.index in seen:
        continue
    stack, part = [f], []
    seen.add(f.index)
    while stack:
        g = stack.pop()
        part.append(g)
        for e in g.edges:
            for n in e.link_faces:
                if n.index not in seen:
                    seen.add(n.index)
                    stack.append(n)
    idx = [v.index for g in part for v in g.verts]
    c = hco0[idx].mean(0)
    if c[2] < brow_z and c[1] < head_front_y:
        doomed += part
bmesh.ops.delete(bm, geom=doomed, context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(hair.data)
bm.free()
hco = coords(hair) @ T[:3, :3].T + T[:3, 3]
ok, loc, nrm, _ = nearest(bbvh, hco)
depth = ((hco - loc) * nrm).sum(1)
inside = ok & (depth < 0.003)
hco[inside] = loc[inside] + nrm[inside] * 0.003
set_coords(hair, hco)
report["hair"] = {"verts": int(len(hco)), "pushed_out": int(inside.sum()), "fringe_faces_dropped": len(doomed)}
for g in list(hair.vertex_groups):
    hair.vertex_groups.remove(g)
vg = hair.vertex_groups.new(name="head")
vg.add(list(range(len(hair.data.vertices))), 1.0, "REPLACE")
hair.parent = rig
hair.matrix_parent_inverse = rig.matrix_world.inverted()
mod = hair.modifiers.new("Armature", "ARMATURE")
mod.object = rig
# Its UVs: the source's own charts (a fresh unwrap of a spiky shell shatters into thousands of islands), repacked to fill the square.
hair.data.uv_layers.new(name="hairUV", do_init=True)
bpy.ops.object.select_all(action="DESELECT")
bpy.context.view_layer.objects.active = hair
hair.select_set(True)
hair.data.uv_layers.active = hair.data.uv_layers["hairUV"]
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.select_all(action="SELECT")
bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
bpy.ops.object.mode_set(mode="OBJECT")

# Arms: each segment swung about its head until the centroid of the skin it carries meets the centroid of the nearest source surface.
_, sbvh = evaluated(src)
groups = {g.index: g.name for g in body.vertex_groups}
dominant = []
for v in body.data.vertices:
    gs = [(g.weight, groups.get(g.group, "")) for g in v.groups if not groups.get(g.group, "").startswith("Delete.")]
    dominant.append(max(gs)[1] if gs else "")
dominant = np.array(dominant)
W = np.array(rig.matrix_world)
Winv = np.linalg.inv(W)
swings = {}
for it in range(8):
    for bone in ("upperarm_l", "lowerarm_l", "upperarm_r", "lowerarm_r"):
        bpy.context.view_layer.update()
        co, _ = evaluated(body)
        P = co[dominant == bone]
        ok, Q, _, d = nearest(sbvh, P)
        keep = ok & (d < 0.1)
        pb = rig.pose.bones[bone]
        pivot = W[:3, :3] @ np.array(pb.head) + W[:3, 3]
        a, b = P[keep].mean(0) - pivot, Q[keep].mean(0) - pivot
        rot = Vector(a).rotation_difference(Vector(b))
        if rot.angle > math.radians(10):
            rot = rot.slerp(rot.__class__(), 1 - math.radians(10) / rot.angle)
        R = np.eye(4)
        R[:3, :3] = np.array(rot.to_matrix())
        Tp, Tn = np.eye(4), np.eye(4)
        Tp[:3, 3], Tn[:3, 3] = pivot, -pivot
        # The world-space swing about the pivot, expressed in armature space (the rig object may carry a transform).
        pb.matrix = Matrix((Winv @ Tp @ R @ Tn @ W).tolist()) @ pb.matrix
        swings[bone] = swings.get(bone, 0) + math.degrees(rot.angle)
bpy.context.view_layer.update()
co, _ = evaluated(body)
arms = np.isin(dominant, ["upperarm_l", "lowerarm_l", "upperarm_r", "lowerarm_r"])
_, _, _, d = nearest(sbvh, co[arms])
report["arm_fit"] = {"swing_deg_total": {k: round(v, 1) for k, v in swings.items()}, "median_gap_mm": round(float(np.median(d)) * 1000, 1)}

src.hide_render = True
for m in masks:
    m.show_viewport = True
bpy.ops.wm.save_as_mainfile(filepath=out)
json.dump(report, open(os.path.splitext(out)[0] + "_fit.json", "w"), indent=1)
print("HYBRID_FIT", json.dumps(report))
