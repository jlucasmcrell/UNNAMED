"""A clip on a character, side view at evenly spaced moments (charstd review): the character GLB and a retargeted clip GLB (same
skeleton names), the clip's action put on the character's armature, N frames rendered side by side into one PNG.

    blender -b --python clip_poses.py -- --character <char.glb> --clip <anim.glb> --out <png> [--frames 6] [--yaw 90]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
n = int(arg("--frames", "6"))
yaw = float(arg("--yaw", "90"))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--clip")))
clip_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
action = (clip_arm.animation_data.action if clip_arm.animation_data and clip_arm.animation_data.action else bpy.data.actions[0])
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--character")))
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for o in bpy.data.objects:     # only the base outfit (the export's variant materials name "<variants>::...")
    if o.type == "MESH" and all(s.material and "::" in s.material.name and "base" not in s.material.name.split("::")[0].split(",") for s in o.material_slots):
        o.hide_render = True
arm.animation_data_create()
arm.animation_data.action = action
f0, f1 = [int(v) for v in action.frame_range]
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 420, 620
scene.world = bpy.data.worlds.new("w")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.52, 0.55, 1)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
scene.collection.objects.link(sun)
bpy.ops.mesh.primitive_plane_add(size=20)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 50
d = 4.2
cam.location = Vector((math.sin(math.radians(yaw)) * d, -math.cos(math.radians(yaw)) * d, 1.0))
cam.rotation_euler = (Vector((0, 0, 0.9)) - cam.location).to_track_quat("-Z", "Y").to_euler()
frames = []
out = os.path.abspath(arg("--out"))
tmp = out + ".tmp"
os.makedirs(tmp, exist_ok=True)
for i in range(n):
    f = f0 + (f1 - f0) * i / max(n, 1)
    scene.frame_set(int(f))
    scene.render.filepath = os.path.join(tmp, f"{i:02d}.png")
    bpy.ops.render.render(write_still=True)
    frames.append(scene.render.filepath)
# The frames side by side (Blender's own image API: no Pillow in its Python).
import numpy as np  # noqa: E402
tiles = []
for p in frames:
    im = bpy.data.images.load(p)
    tiles.append(np.array(im.pixels[:], np.float32).reshape(im.size[1], im.size[0], 4))
sheet = np.concatenate(tiles, axis=1)
img = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
img.pixels.foreach_set(sheet.ravel())
img.filepath_raw = out
img.file_format = "PNG"
img.save()
print("CLIP_POSES", out)
