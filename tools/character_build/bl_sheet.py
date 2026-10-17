"""WP17 comparison sheets: assembled bodies (and optionally their garments) side by side in clay or textured Workbench, from the front,
the side and the back, at their rest pose or posed by one clip at given times. For our own visual QA (viewing MetaHuman renders is
allowed; never feed them to generative AI).

blender -b --factory-startup --python bl_sheet.py -- --assets <glb>[+<garment glb>...],... --labels a,b,...
    --out <png> [--views front,side,back] [--clip <clip glb> --times 0.3,0.6] [--textured] [--size 360x900]
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--assets", required=True, help="comma list; each item is a body glb with optional +garment glbs")
ap.add_argument("--labels", default="")
ap.add_argument("--out", required=True)
ap.add_argument("--views", default="front,side,back")
ap.add_argument("--clip")
ap.add_argument("--times", default="")
ap.add_argument("--textured", action="store_true")
ap.add_argument("--size", default="360x900")
ap.add_argument("--spacing", type=float, default=0.9)
ap.add_argument("--zoom", default="full", choices=["full", "upper", "legs"])
a = ap.parse_args(argv)
W, H = (int(x) for x in a.size.split("x"))
VIEW_YAW = {"front": 0.0, "side": 90.0, "back": 180.0, "three": 35.0}

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
shading = scene.display.shading
shading.light = "STUDIO"
shading.color_type = "TEXTURE" if a.textured else "SINGLE"
shading.single_color = (0.55, 0.52, 0.5)
shading.show_specular_highlight = False
shading.show_cavity = True
shading.cavity_type = "WORLD"
scene.display.render_aa = "8"
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.16, 0.16, 0.17)

clip_action = None
if a.clip:
    bpy.ops.import_scene.gltf(filepath=a.clip)
    clip_objs = [o for o in bpy.data.objects]
    clip_action = next(iter(bpy.data.actions))
    for o in clip_objs:
        bpy.data.objects.remove(o, do_unlink=True)

items = a.assets.split(",")
labels = a.labels.split(",") if a.labels else [os.path.basename(i.split("+")[0]) for i in items]
figures = []
for k, item in enumerate(items):
    before = set(bpy.data.objects)
    for path in item.split("+"):
        bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    for o in [o for o in new if o.type == "MESH" and ("_LOD" in o.name or o.name.endswith("_shadow") or not o.modifiers)]:
        new.remove(o)
        bpy.data.objects.remove(o, do_unlink=True)
    arms = [o for o in new if o.type == "ARMATURE"]
    body_arm = arms[0]
    # garments come with their own copy of the skeleton: drive them from the body's
    for arm in arms[1:]:
        for o in [o for o in new if o.type == "MESH" and o.parent == arm]:
            for m in o.modifiers:
                if m.type == "ARMATURE":
                    m.object = body_arm
            mw = o.matrix_world.copy()
            o.parent = body_arm
            o.matrix_world = mw
    # the garments' own skeletons (and the empties above them) are left with no mesh: drop them
    for o in [o for o in new if o.type != "MESH" and o is not body_arm and not any(c.type == "MESH" for c in o.children_recursive)
              and o not in body_arm.children_recursive and body_arm not in o.children_recursive]:
        new.remove(o)
        bpy.data.objects.remove(o, do_unlink=True)
    roots = [o for o in new if o.parent is None]
    # the rest skeleton is yawed (~10 deg on the Veth bodies): square each figure to the camera as bl_garment.py --drape-body does
    joint = {b.name: body_arm.matrix_world @ b.head_local for b in body_arm.data.bones}
    pairs = [(f"{n}_l", f"{n}_r") for n in ("thigh", "upperarm", "foot")] + [(f"{n}.L", f"{n}.R") for n in ("thigh", "upper_arm", "foot")]
    across = sum((joint[l_] - joint[r_] for l_, r_ in pairs if l_ in joint and r_ in joint), Vector())
    square = Matrix.Rotation(-math.atan2(across.y, across.x), 4, "Z") if across.length > 0 else Matrix()
    for r in roots:
        r.matrix_world = square @ r.matrix_world
    figures.append((roots, body_arm))
    if clip_action is not None:
        body_arm.animation_data_create()
        body_arm.animation_data.action = clip_action

times = [float(t) for t in a.times.split(",")] if a.times else [None]
views = a.views.split(",")
cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
scene.render.resolution_x = W * len(items)
scene.render.resolution_y = H
tiles = []
fps = scene.render.fps / scene.render.fps_base
for t in times:
    if t is not None:
        scene.frame_set(int(round(t * fps)))
    for view in views:
        # each figure turned to the view about its own origin (the feet), then set out along X
        place = [Matrix.Translation((k * a.spacing, 0, 0)) @ Matrix.Rotation(-math.radians(VIEW_YAW[view]), 4, "Z") for k in range(len(figures))]
        for (roots, _), m in zip(figures, place):
            for r in roots:
                r.matrix_world = m @ r.matrix_world
        bpy.context.view_layer.update()
        lo, hi = Vector((1e9, 1e9, 1e9)), Vector((-1e9, -1e9, -1e9))
        for o in scene.objects:
            if o.type == "MESH" and any(m.type == "ARMATURE" for m in o.modifiers):
                for c in o.bound_box:
                    w = o.matrix_world @ Vector(c)
                    lo = Vector(map(min, lo, w))
                    hi = Vector(map(max, hi, w))
        zlo, zhi = lo.z, hi.z
        if a.zoom == "upper":
            zlo += (zhi - zlo) * 0.45
        elif a.zoom == "legs":
            zhi = zlo + (zhi - zlo) * 0.6
        cam_data.ortho_scale = max((zhi - zlo) * 1.06 * W * len(items) / H, hi.x - lo.x + 0.2)
        cam.location = Vector(((lo.x + hi.x) / 2, -10.0, (zlo + zhi) / 2))
        cam.rotation_euler = (math.radians(90), 0, 0)
        path = f"{a.out[:-4]}_{view}{'' if t is None else f'_t{t:.2f}'}.png"
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        tiles.append(path)
        for (roots, _), m in zip(figures, place):
            for r in roots:
                r.matrix_world = m.inverted() @ r.matrix_world
        bpy.context.view_layer.update()
print("SHEET", labels, tiles)
