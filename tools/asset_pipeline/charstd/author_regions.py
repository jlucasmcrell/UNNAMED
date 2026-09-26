"""The archetype's body-region map, authored ONCE (charstd): every body vertex of the archetype's topology assigned to one region, written
as frozen vertex-index lists (every instance of the archetype shares the topology, so the map holds for all of them). Garments name
the regions they hide; the exported body is split along these regions so the game can hide them. After this first pass the JSON is
the authority - correct it by hand (or re-run with different planes) and commit it; nothing recomputes it per garment or per build.

    blender -b --python author_regions.py -- --blend archetype.blend --out archetypes/<id>.regions.json [--render <png>]

Names: the combat doc's sixteen (WAVE_0_MODULAR_ASSET_STANDARD.md section 5: head, face, neck, shoulder, chest, back, upperarm,
elbow, forearm, hand, abdomen, groin, thigh, knee, shin, foot; _L/_R for paired), with chest and back each split at a plane 7.5 cm
below the neck's base into chest_upper/chest and back_upper/back (a scoop or low neckline and a cuirass's collar need different
edges there). Assignment: the vertex's dominant bone; head versus face by MPFB's own scalp and ear groups and the plane of the ears;
front versus back by the spine's plane; elbow and knee as the skin within 4.5 cm / 6 cm of the joint.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
body = next(o for o in bpy.data.objects if o.type == "MESH" and "body" in o.vertex_groups and o.data.shape_keys)
for m in body.modifiers:
    if m.type == "MASK":
        m.show_viewport = False
e = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
me = e.to_mesh()
co = np.array([tuple(body.matrix_world @ v.co) for v in me.vertices])
e.to_mesh_clear()
joint = lambda b: np.array(rig.matrix_world @ rig.data.bones[b].head_local)  # noqa: E731
groups = {g.index: g.name for g in body.vertex_groups}
bones = set(rig.data.bones.keys())
neck_base = joint("neck")[2]
spine_y = joint("chest")[1]
head_y = joint("head")[1]
regions = {}
unassigned = 0
for v in body.data.vertices:
    names = {groups.get(g.group, ""): g.weight for g in v.groups}
    if "body" not in names:
        continue   # MPFB's helper geometry: never exported
    weights = {n: w for n, w in names.items() if n in bones}
    if not weights:
        unassigned += 1
        continue
    b = max(weights, key=weights.get)
    p = co[v.index]
    side = b.rsplit(".", 1)[1] if "." in b else ""
    sfx = f"_{side}" if side else ""
    front = p[1] < spine_y
    if b in ("head", "eye.L", "eye.R"):
        r = "head" if ("scalp" in names or "ears" in names or p[1] > head_y + 0.01) else "face"
    elif b == "neck":
        r = "neck"
    elif b.startswith("shoulder"):
        r = "shoulder" + sfx
    elif b == "chest":
        upper = p[2] > neck_base - 0.075
        r = ("chest_upper" if upper else "chest") if front else ("back_upper" if upper else "back")
    elif b in ("spine_mid", "spine"):
        r = "abdomen" if front else "back"
    elif b in ("hips", "root"):
        r = "groin"
    elif b.startswith(("upper_arm", "forearm")):
        near = np.linalg.norm(p - joint(f"forearm.{side}")) < 0.045
        r = ("elbow" if near else ("upperarm" if b.startswith("upper_arm") else "forearm")) + sfx
    elif b.startswith(("hand", "thumb", "index", "middle", "ring", "pinky")):
        r = "hand" + sfx
    elif b.startswith(("thigh", "shin")):
        near = np.linalg.norm(p - joint(f"shin.{side}")) < 0.06
        r = ("knee" if near else ("thigh" if b.startswith("thigh") else "shin")) + sfx
    elif b.startswith(("foot", "toe")):
        r = "foot" + sfx
    else:
        r = "unassigned"
        unassigned += 1
    regions.setdefault(r, []).append(v.index)

out = {"archetype": os.path.splitext(os.path.basename(arg("--out")))[0].replace(".regions", ""), "vertex_count": len(body.data.vertices),
       "authored_by": "author_regions.py (first pass); hand edits allowed - this file is the authority",
       "planes": {"chest_upper_below_neck_base_m": 0.075, "elbow_radius_m": 0.045, "knee_radius_m": 0.06},
       "regions": {k: sorted(v) for k, v in sorted(regions.items())}}
json.dump(out, open(os.path.abspath(arg("--out")), "w"), indent=0)
print("REGIONS", {k: len(v) for k, v in sorted(regions.items())}, "unassigned", unassigned)

if arg("--render"):
    # A check: every region its own colour, front and back.
    rng = np.random.default_rng(3)
    colour = {r: tuple(rng.uniform(0.15, 1.0, 3)) for r in regions}
    attr = body.data.color_attributes.new("regions", "FLOAT_COLOR", "POINT")
    for r, idx in regions.items():
        for i in idx:
            attr.data[i].color = (*colour[r], 1)
    mat = bpy.data.materials.new("regions")
    mat.use_nodes = True
    nt = mat.node_tree
    vc = nt.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "regions"
    nt.links.new(vc.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
    body.data.materials.clear()
    body.data.materials.append(mat)
    for m in body.modifiers:
        if m.type == "MASK":
            m.show_viewport = m.name == "Hide helpers"
            m.show_render = m.name == "Hide helpers"
    for o in bpy.data.objects:
        if o.type == "MESH" and o is not body:
            o.hide_render = True
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 800, 1000
    w = bpy.data.worlds.new("w")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.3, 0.32, 1)
    scene.world = w
    sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(50), 0, math.radians(20))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.0
    base = os.path.splitext(os.path.abspath(arg("--render")))[0]
    for name, yaw in (("front", 0), ("back", 180), ("side", 90)):
        a = math.radians(yaw)
        cam.location = (math.sin(a) * 4, -math.cos(a) * 4, 0.95)
        cam.rotation_euler = (math.radians(90), 0, a)
        scene.render.filepath = f"{base}_{name}.png"
        bpy.ops.render.render(write_still=True)
    print("REGIONS_RENDER", base)
