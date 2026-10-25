"""Kal dorsal wings: MakeHuman community 'Bone Wings 1' (CC0) spars, rigged, with a membrane grown between them.

Every loose bone of the source mesh is bound rigidly to one joint of a per-side chain (root, forearm, four fingers of
three segments). The membrane is new geometry between consecutive finger chains and from the innermost finger to the
back, skinned across the two spars it spans so it folds with them. Actions: 'folded' (spars collapsed along the spine
channels), 'deployed' (full span) and 'deploy' (folded -> deployed, 0.9 s).

usage: blender -b --factory-startup --python build_kal_wings.py -- <out dir> [stone colour png] [leather colour png] [leather normal png]
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(args[0])
STONE = args[1] if len(args) > 1 else None
LEATHER = args[2] if len(args) > 2 else None
LEATHER_N = args[3] if len(args) > 3 else None
SOURCE = r"D:\Otherreach_Depot\Otherreach_External_Assets\MakeHuman\community\clothes\1493_Static_Wings4_Bone_Wings1\bonewings1.obj"
SCALE = 0.1  # MakeHuman decimetres
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_homefile(use_empty=True)
bpy.ops.wm.obj_import(filepath=SOURCE)
src = bpy.context.selected_objects[0]
bpy.context.view_layer.objects.active = src
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
src.data.transform(Matrix.Scale(SCALE, 4))
bm = bmesh.new()
bm.from_mesh(src.data)
bm.verts.ensure_lookup_table()
co = np.array([v.co[:] for v in bm.verts])

# loose parts
part_of = np.full(len(co), -1)
parts = []
for v in bm.verts:
    if part_of[v.index] >= 0:
        continue
    stack, comp = [v], []
    while stack:
        x = stack.pop()
        if part_of[x.index] >= 0:
            continue
        part_of[x.index] = len(parts)
        comp.append(x.index)
        stack.extend(e.other_vert(x) for e in x.link_edges)
    parts.append(np.array(comp))

centre_z = co[:, 2].mean()


def axis_ends(idx):
    p = co[idx]
    m = p.mean(0)
    _, _, vt = np.linalg.svd(p - m, full_matrices=False)
    t = (p - m) @ vt[0]
    return m + vt[0] * t.min(), m + vt[0] * t.max()


chains = {}
for side, sign in (("L", 1.0), ("R", -1.0)):
    mine = [i for i, c in enumerate(parts) if np.sign(co[c, 0].mean()) == sign]
    by_size = lambda n: [i for i in mine if len(parts[i]) == n]
    scap = max(mine, key=lambda i: len(parts[i]))
    fore = by_size(554) + by_size(386)
    # finger segments by size, ordered from the body outward by their mean |x|
    seg = [sorted(by_size(n), key=lambda i: abs(co[parts[i], 0].mean())) for n in (218, 170, 146)]
    wrist_parts = [i for i in mine if i not in [scap, *fore, *seg[0], *seg[1], *seg[2]]]  # carpals; claws join after chaining
    fore_pts = np.concatenate([co[parts[i]] for i in fore])
    wrist = co[np.concatenate([parts[i] for i in wrist_parts])].mean(0)
    shoulder = fore_pts[fore_pts[:, 2] < np.percentile(fore_pts[:, 2], 3)].mean(0)
    root = co[parts[scap]].mean(0)
    # link each finger outward from the wrist: the next segment is the unused one whose nearer end meets this one's far end
    fingers, used = [], set()
    chain_parts = []
    for k in range(4):
        joints, ids = [wrist], []
        for s in range(3):
            def gap(i):
                a, b = axis_ends(parts[i])
                return min(np.linalg.norm(a - joints[-1]), np.linalg.norm(b - joints[-1]))
            i = seg[s][k] if s == 0 else min((i for i in seg[s] if i not in used), key=gap)
            used.add(i); ids.append(i)
            a, b = axis_ends(parts[i])
            joints.append(b if np.linalg.norm(b - joints[-1]) > np.linalg.norm(a - joints[-1]) else a)
        fingers.append(joints); chain_parts.append(ids)
    wrist_parts += [i for i in seg[1] + seg[2] if i not in used]
    chains[side] = {"root": root, "shoulder": shoulder, "wrist": wrist, "fingers": fingers,
                    "parts": {"root": [scap], "fore": fore + wrist_parts, **{f"f{k}_{s}": [chain_parts[k][s]] for k in range(4) for s in range(3)}}}

# armature
arm_data = bpy.data.armatures.new("KalWings")
rig = bpy.data.objects.new("KalWings", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
spine = eb.new("wing_root")
mid = (chains["L"]["root"] + chains["R"]["root"]) / 2
spine.head, spine.tail = Vector(mid), Vector(mid) + Vector((0, 0, .1))
for side, c in chains.items():
    r = eb.new(f"scapula.{side}"); r.head, r.tail = Vector(c["root"]), Vector(c["shoulder"]); r.parent = spine
    f = eb.new(f"forearm.{side}"); f.head, f.tail = Vector(c["shoulder"]), Vector(c["wrist"]); f.parent = r; f.use_connect = True
    for k, joints in enumerate(c["fingers"]):
        parent = f
        for s in range(3):
            b = eb.new(f"finger{k}_{s}.{side}")
            b.head, b.tail = Vector(joints[s]), Vector(joints[s + 1])
            b.parent = parent
            b.use_connect = s > 0
            parent = b
bpy.ops.object.mode_set(mode="OBJECT")

# bind the spars rigidly
src.parent = rig
groups = {}
for side, c in chains.items():
    for key, ids in c["parts"].items():
        name = {"root": f"scapula.{side}", "fore": f"forearm.{side}"}.get(key, f"finger{key[1]}_{key[3]}.{side}")
        g = groups.get(name) or src.vertex_groups.new(name=name)
        groups[name] = g
        g.add([int(v) for i in ids for v in parts[i]], 1.0, "REPLACE")
src.modifiers.new("Armature", "ARMATURE").object = rig
src.name = "KalWings_spars"

# membrane between consecutive finger chains and from finger 0 to the back
N = 9
verts, faces, weights = [], [], []


def chain_point(joints, t):
    """Point a fraction t along a finger chain (by length), and the (segment, local) it falls in."""
    lens = [np.linalg.norm(joints[i + 1] - joints[i]) for i in range(3)]
    d = t * sum(lens)
    for s in range(3):
        if d <= lens[s] or s == 2:
            u = min(d / lens[s], 1.0)
            return joints[s] + (joints[s + 1] - joints[s]) * u, s
        d -= lens[s]


for side, c in chains.items():
    edges = [None] + c["fingers"]
    for k in range(4):
        if k == 0:
            # inner panel: from finger 0 to a line down the back, root to sacrum
            back_top, back_bottom = c["root"], c["root"] + np.array([0, 0, -0.62])
            a_pts = [(back_top + (back_bottom - back_top) * (i / (N - 1)), f"scapula.{side}") for i in range(N)]
        else:
            a_pts = [(lambda p: (p[0], f"finger{k - 1}_{p[1]}.{side}"))(chain_point(edges[k], i / (N - 1))) for i in range(N)]
        b_pts = [(lambda p: (p[0], f"finger{k}_{p[1]}.{side}"))(chain_point(edges[k + 1], i / (N - 1))) for i in range(N)]
        rows = 6
        base = len(verts)
        for i in range(N):
            pa, ga = a_pts[i]
            pb, gb = b_pts[i]
            for j in range(rows):
                u = j / (rows - 1)
                p = pa + (pb - pa) * u
                if i == N - 1:  # scalloped trailing edge: bow inward between the tips
                    p = p + ((a_pts[N - 2][0] + b_pts[N - 2][0]) / 2 - (pa + pb) / 2) * (math.sin(u * math.pi) * .55)
                verts.append(p)
                weights.append(((ga, 1 - u), (gb, u)))
        for i in range(N - 1):
            for j in range(rows - 1):
                q = base + i * rows + j
                faces.append((q, q + 1, q + rows + 1, q + rows))

mesh = bpy.data.meshes.new("KalWings_membrane")
mesh.from_pydata([tuple(v) for v in verts], [], faces)
mesh.update()
membrane = bpy.data.objects.new("KalWings_membrane", mesh)
bpy.context.scene.collection.objects.link(membrane)
membrane.parent = rig
for i, ((ga, wa), (gb, wb)) in enumerate(weights):
    for g, w in ((ga, wa), (gb, wb)):
        if w > 0:
            (membrane.vertex_groups.get(g) or membrane.vertex_groups.new(name=g)).add([i], w, "ADD")
membrane.modifiers.new("Armature", "ARMATURE").object = rig
bpy.context.view_layer.objects.active = membrane
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(island_margin=.02)
bpy.ops.object.mode_set(mode="OBJECT")
membrane.data.polygons.foreach_set("use_smooth", [True] * len(membrane.data.polygons))
membrane.modifiers.new("Solidify", "SOLIDIFY").thickness = .003


def material(name, colour_path, normal_path, base, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    bsdf.inputs["Roughness"].default_value = rough
    if colour_path:
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(colour_path)
        m.node_tree.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if normal_path:
        t = m.node_tree.nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images.load(normal_path)
        t.image.colorspace_settings.name = "Non-Color"
        n = m.node_tree.nodes.new("ShaderNodeNormalMap")
        m.node_tree.links.new(t.outputs["Color"], n.inputs["Color"])
        m.node_tree.links.new(n.outputs["Normal"], bsdf.inputs["Normal"])
    return m


src.data.materials.clear()
src.data.materials.append(material("kal_wing_spar", STONE, None, (.55, .53, .5), .85))
membrane.data.materials.append(material("kal_wing_membrane", LEATHER, LEATHER_N, (.07, .055, .045), .75))
bpy.context.view_layer.objects.active = src
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(island_margin=.02)
bpy.ops.object.mode_set(mode="OBJECT")


# poses, by target direction in armature space (Blender: +X the wing's own side, +Y behind the body, +Z up)
def directions(spec_for_side):
    """Pose every bone of both sides so its axis points along spec_for_side(side, bone) (None keeps rest)."""
    rig.animation_data_create()
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    order = sorted(rig.pose.bones, key=lambda b: len(b.parent_recursive))
    for pb in order:
        side = pb.name.rsplit(".", 1)[-1] if "." in pb.name else None
        want = spec_for_side(side, pb.name.split(".")[0]) if side else None
        if want is None:
            continue
        sign = 1 if side == "L" else -1
        want = Vector((want[0] * sign, want[1], want[2])).normalized()
        rest = pb.bone.matrix_local
        q = (rest.to_3x3() @ Vector((0, 1, 0))).rotation_difference(want)
        head = pb.head.copy()
        pb.matrix = Matrix.Translation(head) @ (q.to_matrix() @ rest.to_3x3()).to_4x4()
        bpy.context.view_layer.update()


def folded(side, bone):
    if bone == "forearm":
        return (0.12, 0.05, 1.0)
    if bone.startswith("finger"):
        k, s = int(bone[6]), int(bone[8])
        return (0.05 + 0.035 * k, 0.03 - 0.03 * s, -1.0)
    return None


def deployed(side, bone):
    if bone == "forearm":
        return (1.0, 0.3, 0.62)
    if bone.startswith("finger"):
        k, s = int(bone[6]), int(bone[8])
        angle = math.radians((-78, -48, -20, 8)[k] + 4 * s)
        return (math.cos(angle), 0.22, math.sin(angle))
    return None


def key(frame):
    for pb in rig.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)


for name, spec in (("folded", folded), ("deployed", deployed)):
    rig.animation_data_create()
    rig.animation_data.action = bpy.data.actions.new(name)
    directions(spec); key(1)
    rig.animation_data.action.use_fake_user = True
rig.animation_data.action = bpy.data.actions.new("deploy")
directions(folded); key(1)
directions(deployed); key(27)
rig.animation_data.action.use_fake_user = True
bpy.context.scene.render.fps = 30
for track_action in ("folded", "deployed", "deploy"):
    t = rig.animation_data.nla_tracks.new()
    t.name = track_action
    t.strips.new(track_action, 1, bpy.data.actions[track_action])
rig.animation_data.action = None

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "kal_wings.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "kal_wings.glb"), export_format="GLB", export_animations=True,
                          export_animation_mode="NLA_TRACKS", export_apply=False, export_yup=True)
print("WINGS", {s: {"root": [round(v, 3) for v in c["root"]], "wrist": [round(v, 3) for v in c["wrist"]],
                    "tips": [[round(v, 3) for v in f[-1]] for f in c["fingers"]]} for s, c in chains.items()})
