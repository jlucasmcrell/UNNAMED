"""A four-view workbench sheet of a GLB, for checking a built piece by eye before it goes in game (no GPU needed).

  blender --background --factory-startup --python tools/field_ruins/preview_glb.py -- <in.glb> <out.png>
"""
import math
import sys

import bpy
import mathutils

src, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
pts = [o.matrix_world @ mathutils.Vector(c) for o in meshes for c in o.bound_box]
lo = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
centre, size = (lo + hi) / 2, max(hi - lo)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 640, 640
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.55, 0.55, 0.58)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
cam.data.type = "ORTHO"
cam.data.ortho_scale = size * 1.15
scene.camera = cam
frames = []
for i, yaw in enumerate((0, 90, 180, 35)):
    a = math.radians(yaw)
    d = mathutils.Vector((math.sin(a), -math.cos(a), 0.25 if i == 3 else 0.0)).normalized()
    cam.location = centre + d * size * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = out.replace(".png", f"_{i}.png")
    bpy.ops.render.render(write_still=True)
    frames.append(scene.render.filepath)
sheet = bpy.data.images.new("sheet", 640 * 4, 640)
pixels = [0.0] * (640 * 4 * 640 * 4)
for i, f in enumerate(frames):
    im = bpy.data.images.load(f)
    px = list(im.pixels)
    for y in range(640):
        row = px[y * 640 * 4:(y + 1) * 640 * 4]
        start = (y * 640 * 4 + i * 640) * 4
        pixels[start:start + 640 * 4] = row
sheet.pixels = pixels
sheet.filepath_raw = out
sheet.file_format = "PNG"
sheet.save()
print("PREVIEW", out, tuple(round(v, 2) for v in (hi - lo)))
