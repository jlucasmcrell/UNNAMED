"""WP17 step 1: the archived MetaHuman preset bodies carried onto one Veth body's skeleton, so their difference is flesh, not bones.

blender -b --factory-startup --python bl_preset_shapes.py -- --exports <asset_work/metahuman/exports> --target veth_m
    --presets Victor,Trey,Jorge,... --out <shapes.npz> [--part head]

Every Creator body shares one topology and UV layout (DECISIONS 2026-10-15); exports number ~700 vertices differently, so a
preset's vertices are matched to the target's by UV, then carried vertex by vertex.
Each preset vertex is moved by its target vertex's skin weights from the preset's joints to the target's: per joint the translation
between the two joint positions and the swing that turns the preset's bone onto the target's (bone = joint to its main child, the
one with the most descendants; leaves and twist/helper joints take their parent's swing). Bone roll is left out on purpose: the
Creator estimates joint frames per body and their rolls are not comparable. What is left is the preset's soft tissue - fat,
muscle, bust, belly - on the Veth's own bone lengths and proportions, which is what a build changes.

Out (npz, metres, Blender world, Z up): target (N,3), tris (T,3), names (P,), carried (P,N,3), raw_height (P,), and the
target's joints as joint_names / joint_heads.
"""
import argparse
import os
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--exports", required=True)
ap.add_argument("--target", required=True)
ap.add_argument("--presets", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--part", default="body", choices=["body", "head"], help="head: the head mesh (LOD0), for the neck")
a = ap.parse_args(argv)
N_VERTS = {"body": 32334, "head": 34657}[a.part]


def load_body(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    mesh = next(o for o in new if o.type == "MESH" and N_VERTS <= len(o.data.vertices) <= N_VERTS + 4)
    return new, arm, mesh


def uv_keys(mesh, verts):
    """Per vertex (u, v, height/500): the Creator's shared UV layout, with height/500 to keep any overlapping islands apart."""
    uv = np.zeros((len(mesh.data.vertices), 2))
    layer = mesh.data.uv_layers[0].data
    for loop in mesh.data.loops:
        uv[loop.vertex_index] = layer[loop.index].uv
    return np.hstack([uv, verts[:, 2:3] / 500.0])


def in_target_order(mesh, verts, ref_keys):
    """The mesh's vertex indices in the target's order. Exports of different characters number about 700 vertices differently
    (one preset, Sunita, also carries a stray extra vertex), so vertices are matched by UV, not by index."""
    keys = uv_keys(mesh, verts)
    tree = KDTree(len(keys))
    for i, k in enumerate(keys):
        tree.insert(k, i)
    tree.balance()
    order, worst = np.zeros(N_VERTS, dtype=np.int64), 0.0
    for i, k in enumerate(ref_keys):
        _, j, d = tree.find(k)
        order[i] = j
        worst = max(worst, d)
    if worst > 1e-3:
        raise SystemExit(f"{mesh.name}: a vertex has no UV match (off by {worst:.4f})")
    return order


def joints(arm):
    mw = arm.matrix_world
    head = {b.name: np.array(mw @ b.head_local) for b in arm.data.bones}
    size = {}

    def count(b):
        size[b.name] = 1 + sum(count(c) for c in b.children)
        return size[b.name]

    for b in arm.data.bones:
        if b.parent is None:
            count(b)
    main = {b.name: max(b.children, key=lambda c: size[c.name]).name for b in arm.data.bones if b.children}
    parent = {b.name: b.parent.name if b.parent else None for b in arm.data.bones}
    return head, main, parent


def world_verts(mesh):
    mw = np.array(mesh.matrix_world)
    v = np.array([tuple(x.co) for x in mesh.data.vertices])
    return v @ mw[:3, :3].T + mw[:3, 3]


def weights(mesh):
    """Per bone: (vertex indices, normalised weights)."""
    names = [g.name for g in mesh.vertex_groups]
    per = {}
    for v in mesh.data.vertices:
        ws = [(names[e.group], e.weight) for e in v.groups if e.weight > 1e-5]
        total = sum(w for _, w in ws)
        for n, w in ws:
            per.setdefault(n, ([], []))
            per[n][0].append(v.index)
            per[n][1].append(w / total)
    return {n: (np.array(i), np.array(w)) for n, (i, w) in per.items()}


def swings(src, dst):
    """Per joint: the rotation turning the source bone's direction onto the target's (3x3), parents' swing for leaves."""
    s_head, s_main, s_parent = src
    t_head, t_main, _ = dst
    out = {}

    def swing(name):
        if name in out:
            return out[name]
        child = t_main.get(name)
        if child is not None and child in s_head and name in s_head:
            ds = Vector(s_head[child] - s_head[name])
            dt = Vector(t_head[child] - t_head[name])
            out[name] = np.array(ds.rotation_difference(dt).to_matrix()) if ds.length > 1e-6 and dt.length > 1e-6 else np.eye(3)
        else:
            p = s_parent.get(name)
            out[name] = swing(p) if p else np.eye(3)
        return out[name]

    for n in t_head:
        swing(n)
    return out


def carry(v, per_bone, src, dst):
    s_head, t_head = src[0], dst[0]
    rot = swings(src, dst)
    out = np.zeros_like(v)
    for name, (idx, w) in per_bone.items():
        out[idx] += w[:, None] * ((v[idx] - s_head[name]) @ rot[name].T + t_head[name])
    return out


bpy.ops.wm.read_factory_settings(use_empty=True)
objs, t_arm, t_mesh = load_body(os.path.join(a.exports, a.target, f"{a.target}_{a.part}.fbx"))
target = world_verts(t_mesh)
dst = joints(t_arm)
w_rows = weights(t_mesh)
ref_keys = uv_keys(t_mesh, target)
tris = np.array([tuple(p.vertices) for p in t_mesh.data.polygons if len(p.vertices) == 3], dtype=np.int32)
quads = [tuple(p.vertices) for p in t_mesh.data.polygons if len(p.vertices) == 4]
if quads:
    q = np.array(quads, dtype=np.int32)
    tris = np.vstack([tris.reshape(-1, 3), q[:, [0, 1, 2]], q[:, [0, 2, 3]]])
for o in objs:
    bpy.data.objects.remove(o, do_unlink=True)

names, carried, heights = [], [], []
for preset in a.presets.split(","):
    folder = os.path.join(a.exports, "_stock", preset) if os.path.isdir(os.path.join(a.exports, "_stock", preset)) else os.path.join(a.exports, preset)
    objs, arm, mesh = load_body(os.path.join(folder, f"{preset}_{a.part}.fbx"))
    v = world_verts(mesh)
    v = v[in_target_order(mesh, v, ref_keys)]
    src = joints(arm)
    c = carry(v, w_rows, src, dst)
    names.append(preset)
    carried.append(c.astype(np.float32))
    heights.append(float(v[:, 2].max() - v[:, 2].min()))
    print("CARRIED", preset, "height", round(heights[-1], 3), "mean move from target", round(float(np.linalg.norm(c - target, axis=1).mean()) * 1000, 1), "mm")
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)

jn = sorted(dst[0])
np.savez_compressed(a.out, target=target.astype(np.float32), tris=tris, names=np.array(names), carried=np.stack(carried),
                    raw_height=np.array(heights), joint_names=np.array(jn), joint_heads=np.array([dst[0][n] for n in jn], dtype=np.float32))
print("SHAPES", a.out, len(names), "presets")
