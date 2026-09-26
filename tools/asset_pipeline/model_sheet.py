"""A quick look at static models (Phase B demo triage): each GLB rendered three-quarter on a neutral ground under sun and sky, framed
to its bounds, one PNG per model.

    blender -b --python model_sheet.py -- --out <dir> a.glb [b.glb ...]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
out = os.path.abspath(argv[argv.index("--out") + 1])
os.makedirs(out, exist_ok=True)
for path in [a for a in argv if a.endswith(".glb")]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, size = (lo + hi) / 2, max((hi - lo).length, 0.1)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 640, 640
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(45), 0, math.radians(35))
    scene.collection.objects.link(sun)
    bpy.ops.mesh.primitive_plane_add(size=size * 6, location=(centre.x, centre.y, lo.z))
    plane = bpy.context.active_object
    m = bpy.data.materials.new("ground")
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.25, 0.22, 0.18, 1)
    plane.data.materials.append(m)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.lens = 50
    d = size * 1.35
    cam.location = centre + Vector((d * 0.6, -d * 0.75, d * 0.35))
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, os.path.splitext(os.path.basename(path))[0] + ".png")
    bpy.ops.render.render(write_still=True)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
    print("MODEL_SHEET", os.path.basename(path), "tris", tris, "size_m", [round(v, 2) for v in (hi - lo)])
