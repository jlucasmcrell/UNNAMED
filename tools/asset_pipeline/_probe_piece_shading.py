"""Render a GLB twice - once with its own materials, once with all image textures stripped - to
tell a texture problem apart from a geometry problem.

A flat wall panel that renders as a field of triangular shards can be either: chaotic shading across
a flat surface is the classic symptom of a normal map applied with broken UVs or the wrong colour
space, and it is also what genuinely degenerate geometry looks like. The two need different fixes, so
this removes every image node from the materials and renders again. If the panel becomes clean, the
geometry is fine and the material is at fault; if it stays shattered, the mesh is.

Run inside Blender:
    blender --background --factory-startup --python _probe_piece_shading.py -- <glb> <outdir>
"""
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
source, outdir = argv[0], argv[1]
os.makedirs(outdir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=source)

# Every image texture out of every material, leaving the base colour factor.
removed = 0
for material in bpy.data.materials:
    if not material.use_nodes:
        continue
    tree = material.node_tree
    for node in list(tree.nodes):
        if node.type in {"TEX_IMAGE", "NORMAL_MAP", "BUMP"}:
            tree.nodes.remove(node)
            removed += 1
    principled = next((n for n in tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if principled:
        principled.inputs["Base Color"].default_value = (0.62, 0.58, 0.54, 1.0)
        principled.inputs["Roughness"].default_value = 0.75

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = 520
scene.render.image_settings.file_format = "JPEG"
scene.render.film_transparent = False

# Frame the subject the same way the normal preview does.
corners = []
for obj in bpy.data.objects:
    if obj.type == "MESH":
        corners += [obj.matrix_world @ Vector(c) for c in obj.bound_box]
low = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
high = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
center = (low + high) / 2.0
radius = max((high - low).length / 2.0, 0.2)

camera_data = bpy.data.cameras.new("cam")
camera = bpy.data.objects.new("cam", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
direction = Vector((0.6, -1.0, 0.45)).normalized()
camera.location = center + direction * radius * 3.2

track = camera.constraints.new(type="TRACK_TO")
target = bpy.data.objects.new("target", None)
scene.collection.objects.link(target)
target.location = center
track.target = target

world = bpy.data.worlds.new("W")
scene.world = world
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.51, 0.53, 1.0)
world.node_tree.nodes["Background"].inputs[1].default_value = 1.4

for offset in (Vector((2.0, -2.5, 2.2)), Vector((-2.5, -1.5, 1.2)), Vector((0.5, 2.5, 1.6))):
    light_data = bpy.data.lights.new(name="L", type="AREA")
    light_data.energy = 900.0
    light_data.size = radius * 1.6
    light = bpy.data.objects.new("L", light_data)
    scene.collection.objects.link(light)
    light.location = center + offset * radius
    look = light.constraints.new(type="TRACK_TO")
    look.target = target

out = os.path.join(outdir, "untextured.jpg")
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
triangles = 0
for obj in bpy.data.objects:
    if obj.type == "MESH":
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
print(f"PIECE_SHADING_RESULT textures_removed={removed} triangles={triangles} out={out}")
