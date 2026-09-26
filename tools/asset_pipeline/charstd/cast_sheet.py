"""A review sheet of exported characters (charstd): each GLB imported as the game loads it (glTF), rendered full-body front,
three-quarter and back plus a face close-up, under a neutral three-point light - one PNG per character in --out.

    blender -b --python cast_sheet.py -- --out <dir> char1.glb [char2.glb ...] [--engine eevee|cycles] [--variant hide_vest]

A --variant shows that outfit variant (materials named "<variants>::..." hidden otherwise, as the game does).
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
out = os.path.abspath(arg("--out"))
os.makedirs(out, exist_ok=True)
variant = arg("--variant", "base")
engine = arg("--engine", "eevee")
paths = [a for a in argv if a.endswith(".glb")]


def light(name, energy, rot, size=5.0):
    l = bpy.data.objects.new(name, bpy.data.lights.new(name, "AREA"))
    l.data.energy = energy
    l.data.size = size
    l.rotation_euler = [math.radians(x) for x in rot]
    bpy.context.scene.collection.objects.link(l)
    return l


for path in paths:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    for o in bpy.data.objects:
        if o.type != "MESH":
            continue
        for slot in o.material_slots:
            m = slot.material
            if m and "::" in m.name and variant not in m.name.split("::")[0].split(","):
                if all(s.material and "::" in s.material.name and variant not in s.material.name.split("::")[0].split(",") for s in o.material_slots):
                    o.hide_render = True
                else:
                    # a body surface only other variants show: transparent
                    m.use_nodes = True
                    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
                    for l in list(b.inputs["Alpha"].links):
                        m.node_tree.links.remove(l)
                    b.inputs["Alpha"].default_value = 0.0
    scene = bpy.context.scene
    scene.render.engine = "CYCLES" if engine == "cycles" else "BLENDER_EEVEE"
    if engine == "cycles":
        scene.cycles.samples = 64
    scene.render.resolution_x, scene.render.resolution_y = 700, 1000
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.36, 0.37, 0.39, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    for o in list(scene.collection.objects):
        pass
    top = max((o.matrix_world @ Vector(c)).z for o in bpy.data.objects if o.type == "MESH" and not o.hide_render for c in o.bound_box)
    light("key", 900, (55, 0, 35), 4)
    light("fill", 350, (60, 0, -60), 6)
    light("rim", 600, (-120, 0, 180), 3)
    for l in [o for o in bpy.data.objects if o.type == "LIGHT"]:
        d = l.rotation_euler.to_matrix() @ Vector((0, 0, 1))
        l.location = Vector((0, 0, top * 0.6)) + d * 6
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    name = os.path.splitext(os.path.basename(path))[0]
    shots = [("front", 0, top * 0.52, 4.6, 45), ("threeq", 40, top * 0.52, 4.6, 45), ("back", 180, top * 0.52, 4.6, 45),
             ("face", 20, top * 0.93, 0.62, 60)]
    for label, yaw, z, d, lens in shots:
        cam.data.lens = lens
        # glTF +Z forward becomes Blender -Y: the characters face -Y
        cam.location = Vector((math.sin(math.radians(yaw)) * d, -math.cos(math.radians(yaw)) * d, z))
        cam.rotation_euler = (Vector((0, 0, z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"{name}_{variant}_{label}.png")
        bpy.ops.render.render(write_still=True)
print("CAST_SHEET", out)
