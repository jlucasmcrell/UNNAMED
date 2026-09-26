"""The hybrid player's head wrap (charprod2): the dressed MPFB body's head and neck laid onto the source character's own head and neck
(the current production model's sculpt: brow, cheekbones, jaw, nose, ears), keeping MPFB's topology, rig and face shape keys.

    render: both faces straight on through one orthographic camera (<dir>/mpfb_front.png, source_front.png, cam.json), for
            solve_face_fit.py landmarks (--render mpfb_front.png --reference source_front.png --out <dir>)
    apply:  1. the features: the 468 MediaPipe landmarks lifted onto each mesh; the whole head moved by a similarity between them
               (mis-lifted pairs dropped) and the features by a regularised 3D thin-plate spline fading out 3 cm from the
               landmarks, so MPFB's eyes, nose, mouth and jaw line land on the source's (the eyes, teeth and tongue carried with it);
            2. the detail, coarse to fine: each outward-facing skin vertex pulled toward the nearest source skin that faces the same
               way, the pulls smoothed across the mesh (heavily at first, lightly at the end); the eyes' and mouth's own skin does
               not pull (the source's eyes are a closed painted surface, its mouth a closed line) but is filled smoothly from the
               skin round it, and the inside of the mouth, nose and ears never pulls.
            Both weighted by the head and neck bones' own weights, faded out down the neck (the collar and everything below stay
            as they were); only the body's skin (MPFB's helper geometry is left alone). The same offset is added to every shape
            key, so the face units still move the face the way they did.

    blender -b --python hybrid_wrap.py -- --mode render|apply --blend assembled.blend --source source.glb --dir <dir>
                                          [--out wrapped.blend] [--skin-material MAT_player_skin]
"""
import json
import math
import os
import sys

import bpy
import numpy as np
import addon_utils
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
mode, wdir = arg("--mode"), os.path.abspath(arg("--dir"))
skin_mat = arg("--skin-material", "MAT_player_skin")
os.makedirs(wdir, exist_ok=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
kind = {o: ObjectService.get_object_type(o) for o in bpy.data.objects if o.type == "MESH"}


def shaped(obj, normals=False):
    """World positions (and normals) of an object's vertices at rest with its shape keys, its masks off (its own indices)."""
    masks = [m for m in obj.modifiers if m.type == "MASK" and m.show_viewport]
    for m in masks:
        m.show_viewport = False
    e = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = e.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    nr = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", nr)
    e.to_mesh_clear()
    for m in masks:
        m.show_viewport = True
    M = np.array(obj.matrix_world)
    co = co.reshape(-1, 3) @ M[:3, :3].T + M[:3, 3]
    nr = nr.reshape(-1, 3) @ M[:3, :3].T
    nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-9)
    return (co, nr) if normals else co


def offset(obj, d_world):
    """Adds a world-space offset per vertex to the object's rest shape (every shape key, so the keys' deltas are kept)."""
    d = d_world @ np.linalg.inv(np.array(obj.matrix_world)[:3, :3]).T
    blocks = obj.data.shape_keys.key_blocks if obj.data.shape_keys else None
    for t in (list(blocks) if blocks else [obj.data]):
        pts = t.data if blocks else t.vertices
        co = np.empty(len(pts) * 3, np.float32)
        pts.foreach_get("co", co)
        pts.foreach_set("co", (co.reshape(-1, 3) + d).astype(np.float32).ravel())
    obj.data.update()


# The source, registered to the body's torso as hybrid_fit.py does.
S, N = shaped(body, normals=True)
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--source")))
imported = [o for o in bpy.data.objects if o not in before]
src = next(o for o in imported if o.type == "MESH" and o.data.materials and "LOD" not in o.name)
world = src.matrix_world.copy()
for m in list(src.modifiers):
    src.modifiers.remove(m)
src.parent = None
src.data.transform(world)
src.matrix_world = Matrix.Identity(4)
for o in imported:
    if o is not src:
        bpy.data.objects.remove(o)
sco = np.array([tuple(v.co) for v in src.data.vertices])
band = lambda c: c[(c[:, 2] > 1.05) & (c[:, 2] < 1.35) & (np.abs(c[:, 0]) < 0.12)]  # noqa: E731
shift = band(S).mean(0) - band(sco).mean(0)
shift[2] = 0.0
src.data.transform(Matrix.Translation(Vector(shift)))
sco = sco + shift
eyes_obj = next(o for o, k in kind.items() if k == "Eyes")
E = shaped(eyes_obj)
eye_centres = [E[E[:, 0] < 0].mean(0), E[E[:, 0] >= 0].mean(0)]

if mode == "render":
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 1024
    scene.view_settings.view_transform = "Standard"
    world_ = bpy.data.worlds.new("w")
    world_.use_nodes = True
    world_.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.5, 0.52, 1)
    scene.world = world_
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(70), 0, math.radians(-15))
    scene.collection.objects.link(sun)
    centre = (eye_centres[0] + eye_centres[1]) / 2 - np.array([0, 0, 0.03])
    cam = bpy.data.objects.new("wrapcam", bpy.data.cameras.new("wrapcam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 0.30
    cam.location = (centre[0], centre[1] - 1.0, centre[2])
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam
    shots = {"mpfb_front": [o for o, k in kind.items() if k in ("Basemesh", "Eyes", "Eyebrows", "Eyelashes") or o is body],
             "source_front": [src]}
    for name, visible in shots.items():
        for o in bpy.data.objects:
            if o.type == "MESH":
                o.hide_render = o not in visible
        scene.render.filepath = os.path.join(wdir, name + ".png")
        bpy.ops.render.render(write_still=True)
    json.dump({"centre": [float(c) for c in cam.location], "scale": 0.30, "res": 1024}, open(os.path.join(wdir, "cam.json"), "w"))
    print("WRAP_RENDER", wdir)
    sys.exit(0)

# ---- apply ----
groups = {g.index: g.name for g in body.vertex_groups}
neck_base = (rig.matrix_world @ rig.data.bones["neck_01"].head_local).z
w = np.zeros(len(S))
skin = np.zeros(len(S), bool)
for v in body.data.vertices:
    names = {groups.get(g.group, ""): g.weight for g in v.groups}
    skin[v.index] = "body" in names   # MPFB's helper geometry (tights, skirt, hair and joint helpers) is not in "body"
    head = names.get("head", 0.0)
    neck = sum(wt for n, wt in names.items() if n.startswith("neck"))
    w[v.index] = min(1.0, head + neck * float(np.clip((S[v.index, 2] - neck_base) / 0.04, 0, 1)))
w[~skin] = 0.0
body_polys = [tuple(p.vertices) for p in body.data.polygons if all(skin[i] for i in p.vertices)]
edges = np.array([tuple(e.vertices) for e in body.data.edges])
deg = np.bincount(edges.ravel(), minlength=len(S)).astype(np.float64)
src_bvh = BVHTree.FromPolygons([Vector(c) for c in sco], [tuple(p.vertices) for p in src.data.polygons])
skin_idx = next(i for i, m in enumerate(src.data.materials) if m and m.name.startswith(skin_mat))
skin_bvh = BVHTree.FromPolygons([Vector(c) for c in sco], [tuple(p.vertices) for p in src.data.polygons if p.material_index == skin_idx])

# 1. The features: landmarks lifted onto each mesh (a ray from the camera plane along its view), a thin-plate spline between them.
cam = json.load(open(os.path.join(wdir, "cam.json")))
cx, cy, cz = cam["centre"]


def lift(landmarks, bvh):
    pts = []
    for px, py, _ in landmarks[:468]:   # 468-477 are the irises (the source's are painted)
        x = cx + (px / cam["res"] - 0.5) * cam["scale"]
        z = cz + (0.5 - py / cam["res"]) * cam["scale"]
        hit = bvh.ray_cast(Vector((x, cy, z)), Vector((0, 1, 0)), 3.0)
        pts.append(tuple(hit[0]) if hit[0] is not None else None)
    return pts


mpfb_bvh = BVHTree.FromPolygons([Vector(c) for c in S], body_polys)
lm_m = lift(json.load(open(os.path.join(wdir, "lm_render.json")))["landmarks"], mpfb_bvh)
# The source's landmarks on its skin and eyes only: its fringe hangs in front of the forehead.
face_idx = {i for i, m in enumerate(src.data.materials) if m and (m.name.startswith(skin_mat) or m.name.lower().startswith("eye"))}
face_bvh = BVHTree.FromPolygons([Vector(c) for c in sco], [tuple(p.vertices) for p in src.data.polygons if p.material_index in face_idx])
lm_s = lift(json.load(open(os.path.join(wdir, "lm_reference.json")))["landmarks"], face_bvh)
pairs = [(a, b) for a, b in zip(lm_m, lm_s) if a is not None and b is not None]
P = np.array([a for a, _ in pairs])
Q = np.array([b for _, b in pairs])


def tps(P, Y, lam=1e-6):
    n = len(P)
    K = np.linalg.norm(P[:, None] - P[None], axis=2)
    A = np.zeros((n + 4, n + 4))
    A[:n, :n] = K + lam * np.eye(n)
    A[:n, n:] = np.c_[np.ones(n), P]
    A[n:, :n] = A[:n, n:].T
    sol = np.linalg.solve(A, np.r_[Y, np.zeros((4, 3))])
    return lambda X: np.linalg.norm(X[:, None] - P[None], axis=2) @ sol[:n] + np.c_[np.ones(len(X)), X] @ sol[n:]


def similarity(A, B):
    ma, mb = A.mean(0), B.mean(0)
    X, Y = A - ma, B - mb
    U, Sg, Vt = np.linalg.svd(Y.T @ X / len(A))
    Dg = np.eye(3)
    Dg[2, 2] = np.sign(np.linalg.det(U @ Vt))
    R = U @ Dg @ Vt
    sc = float(np.clip(np.trace(np.diag(Sg) @ Dg) / X.var(0).sum(), 0.94, 1.06))
    return lambda Z: sc * Z @ R.T + (mb - sc * R @ ma), sc


# The head as a whole by a similarity (pairs far off it dropped as mis-lifted), the features by a regularised spline of what is
# left, fading out beyond 3 cm from the nearest landmark (a spline alone extrapolates wildly toward the crown and nape).
sim, sim_scale = similarity(P, Q)
res = np.linalg.norm(Q - sim(P), axis=1)
good = res < 3 * np.median(res)
P, Q = P[good], Q[good]
sim, sim_scale = similarity(P, Q)
local = tps(P, Q - sim(P), lam=1e-3)
lm_tree = KDTree(len(P))
for i, p in enumerate(P):
    lm_tree.insert(p, i)
lm_tree.balance()


def field(X):
    near = np.array([lm_tree.find(x)[2] for x in X])
    return sim(X) - X + np.exp(-(near / 0.03) ** 2)[:, None] * local(X)


D = np.zeros_like(S)
region = np.where(w > 0)[0]
D[region] = w[region, None] * field(S[region])
lm_before = float(np.median(np.linalg.norm(Q - P, axis=1))) * 1000

# 2. The detail. Which skin may pull: outward-facing (a ray out along its normal leaves the head: not the inside of the mouth,
# nostrils or ear canals), and outside the eyes' and mouth's own skin.
teeth = next(o for o, k in kind.items() if k == "Teeth")
T0 = shaped(teeth)
mouth = T0[T0[:, 1] < np.percentile(T0[:, 1], 30)].mean(0)
S1 = S + D
held_zone = np.zeros(len(S), bool)
for centre, radius in [(eye_centres[0] + field(eye_centres[0][None])[0], 0.02), (eye_centres[1] + field(eye_centres[1][None])[0], 0.02),
                       (mouth + field(mouth[None])[0], 0.028)]:
    held_zone |= np.linalg.norm(S1 - centre, axis=1) < radius
bvh1 = BVHTree.FromPolygons([Vector(c) for c in S1], body_polys)
outward = np.zeros(len(S), bool)
for i in region:
    outward[i] = bvh1.ray_cast(Vector(S1[i] + N[i] * 0.002), Vector(N[i]), 0.06)[0] is None
pulls = region[outward[region] & ~held_zone[region]]
held = region[~(outward[region] & ~held_zone[region])]


def harmonic(field_, fixed_mask, n=150):
    f = field_.copy()
    for _ in range(n):
        acc = np.zeros_like(f)
        np.add.at(acc, edges[:, 0], f[edges[:, 1]])
        np.add.at(acc, edges[:, 1], f[edges[:, 0]])
        f[~fixed_mask] = acc[~fixed_mask] / np.maximum(deg[~fixed_mask, None], 1)
    return f


def smooth(field_, conf, n):
    f, c = field_ * conf[:, None], conf.copy()
    for _ in range(n):
        af, ac = np.zeros_like(f), np.zeros_like(c)
        np.add.at(af, edges[:, 0], f[edges[:, 1]])
        np.add.at(af, edges[:, 1], f[edges[:, 0]])
        np.add.at(ac, edges[:, 0], c[edges[:, 1]])
        np.add.at(ac, edges[:, 1], c[edges[:, 0]])
        f = 0.5 * f + 0.5 * af / np.maximum(deg[:, None], 1)
        c = 0.5 * c + 0.5 * ac / np.maximum(deg, 1)
    return f / np.maximum(c, 1e-6)[:, None]


def residual():
    X = S + D
    r, c = np.zeros_like(S), np.zeros(len(S))
    for i in pulls:
        loc, nrm, _, dist = skin_bvh.find_nearest(Vector(X[i]), 0.015)
        if loc is not None and Vector(N[i]).dot(nrm) > 0.5:
            r[i] = np.array(loc) - X[i]
            c[i] = 1.0
    return r, c


r0, c0 = residual()
gap_before = np.linalg.norm(r0[c0 > 0], axis=1)
fixed = np.ones(len(S), bool)
fixed[held] = False
for n in [60, 40, 30, 20, 14, 10, 6, 4, 2, 1]:
    r, c = residual()
    step = harmonic(smooth(r, c, n), fixed)
    D += 0.6 * w[:, None] * step
r, c = residual()
gap_after = np.linalg.norm(r[c > 0], axis=1)

# The eyes, teeth and tongue ride rigidly with the skin round them; the brows and lashes follow the skin under each vertex.
kd = KDTree(len(S))
for i in np.where(skin)[0]:
    kd.insert(S[i], int(i))
kd.balance()
moved = {}
for o, k in kind.items():
    if o is body or k not in ("Eyes", "Teeth", "Tongue", "Eyebrows", "Eyelashes"):
        continue
    X = shaped(o)
    if k in ("Eyebrows", "Eyelashes"):
        d = np.array([D[kd.find(p)[1]] for p in X])
    else:
        d = np.zeros_like(X)
        parts = [X[:, 0] < 0, X[:, 0] >= 0] if k == "Eyes" else [np.ones(len(X), bool)]
        for part in parts:
            near = [i for _, i, _ in kd.find_range(X[part].mean(0), 0.02 if k == "Eyes" else 0.03)]
            d[part] = D[near].mean(0) if near else 0
    offset(o, d)
    moved[o.name] = round(float(np.linalg.norm(d, axis=1).mean()) * 1000, 2)
offset(body, D)
bpy.data.objects.remove(src)

mag = np.linalg.norm(D, axis=1)
top = np.argsort(-mag)[:5]
pct = lambda a: [round(float(np.percentile(a, q)) * 1000, 2) for q in (50, 90, 99)] + [round(float(a.max()) * 1000, 2)]  # noqa: E731
report = {"torso_shift_m": shift.round(4).tolist(), "landmark_pairs": len(pairs), "landmark_pairs_kept": int(good.sum()),
          "head_similarity_scale": round(sim_scale, 4), "landmark_gap_mm_median_before": round(lm_before, 2),
          "region_verts": int(len(region)), "pulling_verts": int(len(pulls)), "held_verts": int(len(held)),
          "gap_mm_p50_p90_p99_max_before": pct(gap_before), "gap_mm_p50_p90_p99_max_after": pct(gap_after),
          "offset_mm_p50_p90_p99_max": pct(mag[region]),
          "largest_offsets": [{"vertex": int(i), "mm": round(float(mag[i]) * 1000, 1), "pulls": bool(i in set(pulls.tolist())),
                               "tps_mm": round(float(np.linalg.norm(field(S[i][None])[0])) * 1000, 1), "below_crown_mm": round(float(S[:, 2][skin].max() - S[i, 2]) * 1000),
                               "at": np.round(S[i], 3).tolist()} for i in top],
          "rigid_and_proxy_offsets_mm": moved}
out = os.path.abspath(arg("--out"))
bpy.ops.wm.save_as_mainfile(filepath=out)
json.dump(report, open(os.path.splitext(out)[0] + "_wrap.json", "w"), indent=1)
print("HYBRID_WRAP", json.dumps(report))
