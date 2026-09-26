"""One character candidate, rendered the same way as every other (the player pipeline benchmark): scaled to its height with the feet
on the ground, textured and untextured (clay) views - front, back, both profiles, the head and neck from four sides, the hands -
and a 360-degree turntable (frames for a video), with the mesh's facts (triangles, parts, materials, texture sizes, UV layers).

    blender -b --python bench_render.py -- --glb cand.glb --out <dir> [--height 1.8] [--turntable 72] [--engine CYCLES|BLENDER_EEVEE] [--yaw 180]
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
glb = argv[argv.index("--glb") + 1]
out = os.path.abspath(argv[argv.index("--out") + 1])
height = float(argv[argv.index("--height") + 1]) if "--height" in argv else 1.8
turn = int(argv[argv.index("--turntable") + 1]) if "--turntable" in argv else 72
engine = argv[argv.index("--engine") + 1] if "--engine" in argv else "BLENDER_EEVEE"
yaw_fix = float(argv[argv.index("--yaw") + 1]) if "--yaw" in argv else 0.0   # turn the candidate so its face looks at the front camera
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
if glb.lower().endswith(".blend"):
    with bpy.data.libraries.load(glb) as (src, dst):
        dst.objects = src.objects
    for o in dst.objects:
        if o is not None:
            bpy.context.scene.collection.objects.link(o)
else:
    bpy.ops.import_scene.gltf(filepath=glb)
meshes = [o for o in bpy.data.objects if o.type == "MESH" and o.data.materials and "_LOD" not in o.name and "lod" not in o.name.lower()[-5:]]
for o in bpy.data.objects:
    if o.type == "MESH" and o not in meshes:
        o.hide_render = True
bpy.context.view_layer.update()


def bounds(objs):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


roots = {o for o in bpy.data.objects if o.parent is None}
for r in roots:
    r.rotation_euler.z += math.radians(yaw_fix)
bpy.context.view_layer.update()
lo, hi = bounds(meshes)
s = height / max(hi.z - lo.z, 1e-6)
for r in roots:
    r.scale = r.scale * s
bpy.context.view_layer.update()
lo, hi = bounds(meshes)
for r in roots:
    r.location += Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
bpy.context.view_layer.update()
lo, hi = bounds(meshes)

facts = {"glb": glb, "parts": len(meshes), "triangles": sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes),
         "materials": sorted({m.name for o in meshes for m in o.data.materials if m}),
         "uv_layers": sorted({uv.name for o in meshes for uv in o.data.uv_layers}),
         "textures": sorted({f"{i.name} {i.size[0]}x{i.size[1]}" for i in bpy.data.images if i.size[0] > 0}),
         "armature_bones": sum(len(o.data.bones) for o in bpy.data.objects if o.type == "ARMATURE"),
         "size_m": [round(hi.x - lo.x, 3), round(hi.y - lo.y, 3), round(hi.z - lo.z, 3)]}
json.dump(facts, open(os.path.join(out, "facts.json"), "w"), indent=1)

scene = bpy.context.scene
scene.render.engine = engine
if engine == "CYCLES":
    scene.cycles.samples = 48
    scene.cycles.device = "GPU"
scene.render.resolution_x, scene.render.resolution_y = 1024, 1024
scene.render.film_transparent = False
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.62, 0.64, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
scene.world = world
key = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
key.data.energy = 3.0
key.rotation_euler = (math.radians(55), 0, math.radians(-35))
scene.collection.objects.link(key)
fill = bpy.data.objects.new("fill", bpy.data.lights.new("fill", "SUN"))
fill.data.energy = 1.0
fill.rotation_euler = (math.radians(70), 0, math.radians(140))
scene.collection.objects.link(fill)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
H = hi.z


def shot(name, target, distance, yaw_deg, lens, pitch_deg=0):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    t = Vector(target)
    cam.location = t + Vector((math.sin(yaw) * math.cos(pitch) * distance, -math.cos(yaw) * math.cos(pitch) * distance,
                               math.sin(pitch) * distance))
    cam.rotation_euler = (math.radians(90) - pitch, 0, yaw)
    cam.data.lens = lens
    scene.render.filepath = os.path.join(out, name + ".png")
    bpy.ops.render.render(write_still=True)


views = [("front", (0, 0, H / 2), 4.4, 0, 50, 0), ("back", (0, 0, H / 2), 4.4, 180, 50, 0), ("left", (0, 0, H / 2), 4.4, -90, 50, 0),
         ("right", (0, 0, H / 2), 4.4, 90, 50, 0), ("head_front", (0, 0, H - 0.16), 1.0, 0, 85, 0),
         ("head_34", (0, 0, H - 0.16), 1.0, 35, 85, 0), ("head_side", (0, 0, H - 0.16), 1.0, 90, 85, 0),
         ("head_back", (0, 0, H - 0.16), 1.0, 180, 85, 0), ("neck_low", (0, 0, H - 0.25), 0.9, 20, 70, -18),
         ("hands", (0.0, 0, H * 0.48), 2.0, 0, 70, 0)]
for v in views:
    shot("tex_" + v[0], *v[1:])
# Clay: every material replaced by one grey, so the geometry alone is judged.
clay = bpy.data.materials.new("clay")
clay.use_nodes = True
clay.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.55, 0.55, 1)
clay.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
for o in meshes:
    for i in range(len(o.data.materials)):
        o.data.materials[i] = clay
    if not o.data.materials:
        o.data.materials.append(clay)
for v in views:
    shot("clay_" + v[0], *v[1:])
# The turntable, textured? no - clay first would hide texture; reload is costly: the turntable is clay; a textured one follows.
tt = os.path.join(out, "turntable_clay")
os.makedirs(tt, exist_ok=True)
for i in range(turn):
    yaw = 360 * i / turn
    t = Vector((0, 0, H / 2))
    cam.location = t + Vector((math.sin(math.radians(yaw)) * 4.4, -math.cos(math.radians(yaw)) * 4.4, 0))
    cam.rotation_euler = (math.radians(90), 0, math.radians(yaw))
    cam.data.lens = 50
    scene.render.resolution_x, scene.render.resolution_y = 720, 720
    scene.render.filepath = os.path.join(tt, f"{i:03d}.png")
    bpy.ops.render.render(write_still=True)
print("BENCH_RENDER", json.dumps(facts))
