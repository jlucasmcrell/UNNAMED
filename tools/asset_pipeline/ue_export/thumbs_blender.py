"""Render one textured thumbnail per exported mesh of a pack (for inventory.py's contact sheets).

  blender --background --factory-startup --python thumbs_blender.py -- <UE_Exports\\Pack> [--match regex] [--size 320]

Writes <pack>/_thumbs/<key>.png. Base colour only (Workbench), 3/4 view, orthographic. Skips thumbnails that exist.
"""
import glob
import json
import math
import os
import re
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
root = os.path.abspath(argv[0])
match = argv[argv.index("--match") + 1] if "--match" in argv else ""
size = int(argv[argv.index("--size") + 1]) if "--size" in argv else 320
out = os.path.join(root, "_thumbs")
os.makedirs(out, exist_ok=True)


def thumb(man, path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(root, man["fbx"]))
    mats = {m["slot_material"].split(".")[-1]: m for m in man["materials"] if m}
    for mat in bpy.data.materials:
        name = re.sub(r"\.\d{3}$", "", mat.name)
        rec = mats.get(name) or mats.get(re.sub(r"_\d+$", "", name))
        base = rec and rec["channels"].get("basecolor")
        if not base or not base.get("file"):
            continue
        mat.use_nodes = True
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(root, base["file"]), check_existing=True)
        node.image.alpha_mode = "NONE"  # Japanese_Village _B maps carry a zero alpha (not opacity)
        mat.node_tree.nodes.active = node
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        return False
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre, extent = (lo + hi) / 2, max((hi - lo).length, 1e-3)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = scene.render.resolution_y = size
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("w")
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = extent * 1.0
    cam.data.clip_end = extent * 10
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = centre + Vector((math.cos(math.radians(-60)), math.sin(math.radians(-60)), 0.55)).normalized() * extent * 3
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return True


manifests = sorted(glob.glob(os.path.join(root, "Manifests", "*.json")))
done = 0
for f in manifests:
    man = json.load(open(f, encoding="utf-8"))
    path = os.path.join(out, man["key"] + ".png")
    if os.path.exists(path) or not re.search(match, man["key"], re.I):
        continue
    try:
        done += thumb(man, path)
    except RuntimeError as e:  # a broken FBX must not stop the sheet
        print(f"THUMB_FAIL {man['key']}: {e}")
print(f"THUMBS_DONE {done} of {len(manifests)} in {root}")
sys.stdout.flush()
os._exit(0)
