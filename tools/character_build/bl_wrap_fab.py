"""WP17 outfit v8: wrap a static garment mesh (a Fab garment shipped as FBX on its own A-posed mannequin, no skeleton, no body) onto
a MetaHuman fit body, as a bl_garment.py source.

blender -b --factory-startup --python bl_wrap_fab.py -- --fit-blend <fit>_drape.blend --assembled <work/assembled> --fit mh_veth_m
    --source <garment .fbx> --maps <basecolor>,<normal>,<roughness> --spec <spec .json> --out <dir> [--scale 1.0] [--frames 60]

The spec is bl_garment.py's ({"id", "layer", "hides", "licence", "provenance"}); "fit" is set here.
1. scale (--scale: the fit body's arm length against the body the garment was made for) and place: the collar's centroid on the
   fit body's neck_01 joint, in the drape scene's frame (the body turned to face -Y squarely, bl_garment.py --drape-body);
2. sleeves onto the arms: each sleeve (the cloth within SLEEVE_R_M of the line from the shoulder joint to its cuff, past the
   shoulder) turns about the shoulder joint onto the upper arm and again about the elbow onto the forearm (the A-poses differ by a
   few degrees and the MetaHuman forearm reaches forward), blended in over 6 cm from the shoulder and 10 cm round the elbow;
3. clear the colliders (body, head, hair and the layers beneath: pushed out as a smooth field, folds kept), optionally settle
   with Blender's cloth solver (--frames), then back into the fit body's frame and out as <out>/<id>.glb (unskinned, its maps on one material) and <id>.json.
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--fit-blend", required=True)
ap.add_argument("--assembled", required=True)
ap.add_argument("--fit", required=True)
ap.add_argument("--source", required=True)
ap.add_argument("--maps", required=True)
ap.add_argument("--spec", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--scale", type=float, default=1.0)
ap.add_argument("--frames", type=int, default=0)
ap.add_argument("--clear-mm", type=float, default=6.0)
ap.add_argument("--clear-regions", default="", help="region:mm,... - more room over these body regions (the armpits, where the arm swings the cloth into the trunk)")
ap.add_argument("--stiffness", type=float, default=15.0)
ap.add_argument("--bending", type=float, default=0.5)
ap.add_argument("--tint", help="sRGB hex: re-dye the albedo (its shading kept: each texel's lightness against the cloth's median)")
ap.add_argument("--size", type=int, default=2048, help="the maps are written at this size")
a = ap.parse_args(argv)
ARMHOLE_BLEND_M, ELBOW_BLEND_M = 0.06, 0.10
SLEEVE_R_M = 0.10  # within this of the arm's axis (fading out over ARMHOLE_BLEND_M) a vertex is sleeve, not torso


def smoothstep(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


bpy.ops.wm.open_mainfile(filepath=a.fit_blend)
scene = bpy.context.scene
back_to_fit = Matrix([scene["drape_frame"][i:i + 4] for i in range(0, 16, 4)])
turn = back_to_fit.inverted()

before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=os.path.join(a.assembled, a.fit, f"{a.fit}_rigged.glb"))
new = [o for o in bpy.data.objects if o not in before]
arm = next(o for o in new if o.type == "ARMATURE")
J = {b.name: turn @ arm.matrix_world @ b.head_local for b in arm.data.bones}
for o in new:
    bpy.data.objects.remove(o, do_unlink=True)

before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=a.source)
meshes = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
for o in [o for o in bpy.data.objects if o not in before and o.type != "MESH"]:
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
g = bpy.context.view_layer.objects.active
g.parent = None
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.mesh.customdata_custom_splitnormals_clear()  # the FBX's normals belong to its own drape, not the wrapped one
for name in ("sharp_edge", "sharp_face"):  # its smoothing groups facet the cloth once it is reshaped
    if name in g.data.attributes:
        g.data.attributes.remove(g.data.attributes[name])
spec = json.load(open(a.spec))
g.name = spec["id"]

# open loops: the collar is the highest large one, the cuffs the two furthest out
bm = bmesh.new()
bm.from_mesh(g.data)
seen, loops = set(), []
for e in bm.edges:
    if not e.is_boundary or e.index in seen:
        continue
    stack, vs = [e], set()
    while stack:
        x = stack.pop()
        if x.index in seen:
            continue
        seen.add(x.index)
        vs.update(v.index for v in x.verts)
        stack += [y for v in x.verts for y in v.link_edges if y.is_boundary and y.index not in seen]
    loops.append(sorted(vs))
co = [v.co.copy() for v in bm.verts]
centre = lambda vs: sum((co[i] for i in vs), Vector()) / len(vs)  # noqa: E731
big = sorted(loops, key=len, reverse=True)[:4]
collar = max(big, key=lambda vs: centre(vs).z)
cuffs = {side: max(big, key=lambda vs: centre(vs).x * side) for side in (1, -1)}

bm.free()

# 1. scale and place
s = a.scale
co = [c * s for c in co]
shift = J["neck_01"] - centre(collar)
co = [c + shift for c in co]

# 2. sleeves onto the arms
moved = 0
for side, sfx in ((1, "l"), (-1, "r")):
    Bs, Be, Bh = J[f"upperarm_{sfx}"], J[f"lowerarm_{sfx}"], J[f"hand_{sfx}"]
    L1, L2 = (Be - Bs).length, (Bh - Be).length
    Gc = centre(cuffs[side])
    axis = (Gc - Bs).normalized()
    Ge = Bs + (Gc - Bs) * (L1 / (L1 + L2))
    R1 = (Ge - Bs).rotation_difference(Be - Bs).to_matrix()
    Ge1 = Bs + R1 @ (Ge - Bs)
    R2 = (R1 @ (Gc - Ge)).rotation_difference(Bh - Be).to_matrix()
    te = (Ge - Bs).length
    members = 0
    for i in range(len(co)):
        d = co[i] - Bs
        t = d.dot(axis)
        w = smoothstep(t / ARMHOLE_BLEND_M) * (1 - smoothstep(((d - axis * t).length - SLEEVE_R_M) / ARMHOLE_BLEND_M))
        if (co[i].x - J["neck_01"].x) * side <= 0 or w == 0:
            continue
        w2 = smoothstep((t - te + ELBOW_BLEND_M / 2) / ELBOW_BLEND_M)
        p1 = Bs + R1 @ (co[i] - Bs)
        p2 = Ge1 + R2 @ (p1 - Ge1)
        co[i] = co[i].lerp(p1.lerp(p2, w2), w)
        members += 1
    moved += members
    print("SLEEVE", sfx, members, "vertices", "upper arm", round(math.degrees((Ge - Bs).angle(Be - Bs)), 1), "deg, forearm",
          round(math.degrees((R1 @ (Gc - Ge)).angle(Bh - Be)), 1), "deg")
for v, c in zip(g.data.vertices, co):
    v.co = c
g.data.update()

# 3. clear the body: a body fuller than the garment's mannequin stands through it. Every vertex nearer than --clear-mm to the
# colliders (or under them) is pushed out along their normal, and the push spread to its neighbours as a smooth field (so the cloth
# moves out as a whole and keeps its folds, instead of denting); repeated until nothing is left under.
from mathutils.bvhtree import BVHTree  # noqa: E402
cols = [o for o in scene.objects if o.type == "MESH" and o != g and any(m.type == "COLLISION" for m in o.modifiers)]
REGIONS = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "regions.json")))["regions"]
room = {REGIONS[r]: float(mm) / 1000 for r, mm in (x.split(":") for x in a.clear_regions.split(",") if x)}
verts, tris, extra = [], [], []
for o in cols:
    base = len(verts)
    verts += [o.matrix_world @ v.co for v in o.data.vertices]
    tris += [tuple(base + i for i in p.vertices) for p in o.data.polygons]
    uv = o.data.uv_layers[1].data if o.name.endswith("_body") and len(o.data.uv_layers) > 1 else None
    extra += [room.get(int(round(uv[p.loop_start].uv.x)), 0.0) if uv else 0.0 for p in o.data.polygons]
bvh = BVHTree.FromPolygons(verts, tris)
P0 = np.array([tuple(v.co) for v in g.data.vertices])
E = np.array([tuple(e.vertices) for e in g.data.edges])
deg = np.bincount(E.ravel(), minlength=len(P0)).astype(float)
D = np.zeros_like(P0)
clear = a.clear_mm / 1000
for it in range(60):
    P = P0 + D
    push = np.zeros_like(P0)
    under = 0
    for i, p in enumerate(P):
        hit, n, face, _ = bvh.find_nearest(Vector(p))
        if hit is None:
            continue
        sgn = (Vector(p) - hit).dot(n)
        want = clear + extra[face]
        if sgn < want:
            push[i] = np.array(n) * (want - sgn)
            under += 1
    if not under:
        break
    D += push * 1.2
    for _ in range(4):
        acc = np.zeros_like(D)
        np.add.at(acc, E[:, 0], D[E[:, 1]])
        np.add.at(acc, E[:, 1], D[E[:, 0]])
        avg = acc / np.maximum(deg, 1)[:, None]
        D = np.where((np.linalg.norm(avg, axis=1) > np.linalg.norm(D, axis=1))[:, None], avg, 0.5 * (D + avg))
print("CLEARED", it, "rounds,", under, "under at the last, max push", round(float(np.linalg.norm(D, axis=1).max()) * 1000, 1), "mm")
for v, c in zip(g.data.vertices, P0 + D):
    v.co = c
g.data.update()

# 4. optional settle on the colliders (--frames)
settled = [v.co.copy() for v in g.data.vertices]
if a.frames:
    mod = g.modifiers.new("Cloth", "CLOTH")
    cs = mod.settings
    cs.quality, cs.mass, cs.air_damping = 10, 0.15, 1.0
    cs.tension_stiffness = cs.compression_stiffness = a.stiffness
    cs.shear_stiffness = a.stiffness / 3
    cs.bending_stiffness = a.bending
    cc = mod.collision_settings
    cc.use_collision, cc.distance_min, cc.collision_quality = True, 0.003, 3
    cc.use_self_collision, cc.self_distance_min = True, 0.002
    mod.point_cache.frame_start, mod.point_cache.frame_end = 1, a.frames
    scene.frame_start, scene.frame_end = 1, a.frames
    for f in range(1, a.frames + 1):
        scene.frame_set(f)
    settled = [v.co.copy() for v in g.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.vertices]
    g.modifiers.remove(mod)
for v, c in zip(g.data.vertices, settled):
    v.co = back_to_fit @ c
g.data.update()

# material: the garment's own maps, at --size, the albedo re-dyed
os.makedirs(a.out, exist_ok=True)
mat = bpy.data.materials.new(f"{spec['id']}_cloth")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
for path, kind, colour in zip(a.maps.split(","), ("albedo", "normal", "roughness"), ("sRGB", "Non-Color", "Non-Color")):
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = colour
    img.scale(a.size, a.size)
    if kind == "albedo" and a.tint:
        px = np.empty(len(img.pixels), np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(-1, 4)
        lum = px[:, :3].mean(1)
        dye = np.array([int(a.tint[i:i + 2], 16) / 255 for i in (0, 2, 4)])  # a byte image's pixels stay sRGB
        px[:, :3] = np.clip(dye[None] * (lum / np.median(lum[lum > 0.01]))[:, None], 0, 1)
        img.pixels.foreach_set(px.astype(np.float32).ravel())
    img.filepath_raw = os.path.join(a.out, f"{spec['id']}_{kind}.png")
    img.file_format = "PNG"
    img.save()
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img
    if kind == "albedo":
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
        albedo_node = t
    elif kind == "roughness":
        nt.links.new(t.outputs["Color"], bsdf.inputs["Roughness"])
    else:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(t.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
bsdf.inputs["Metallic"].default_value = 0.0
nt.nodes.active = albedo_node
g.data.materials.clear()
g.data.materials.append(mat)
for p in g.data.polygons:
    p.use_smooth = True

for o in list(scene.objects):
    o.select_set(o == g)
bpy.context.view_layer.objects.active = g
bpy.ops.export_scene.gltf(filepath=os.path.join(a.out, f"{spec['id']}.glb"), use_selection=True, export_format="GLB", export_animations=False)
spec["fit"] = a.fit
spec["provenance"] += (f" Wrapped onto {a.fit} by tools/character_build/bl_wrap_fab.py (scale {s}, sleeves onto the arms,"
                       f" settled {a.frames} frames on {os.path.basename(a.fit_blend)}"
                       + (f", albedo re-dyed #{a.tint}" if a.tint else "") + f", maps at {a.size}px).")
json.dump(spec, open(os.path.join(a.out, f"{spec['id']}.json"), "w"), indent=1)
print("WRAPPED", spec["id"], len(g.data.vertices), "vertices", sum(len(p.vertices) - 2 for p in g.data.polygons), "tris,", moved,
      "sleeve vertices turned")
