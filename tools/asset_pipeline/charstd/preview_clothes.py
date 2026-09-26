"""Triage sheet for candidate MPFB/MakeHuman garments (CHARACTER_ASSET_STANDARD.md section 7, step 1): each candidate fitted by its own
MHCLO onto a fresh MPFB body at the given macros, rendered front and three-quarter on a neutral backdrop, one image per candidate.

    blender -b --python preview_clothes.py -- --gender 1.0 --clothes wizard_robe,monks_robe --out <dir> [--macro muscle=0.7,weight=0.6]
"""
import math
import os
import sys

import bpy
import addon_utils
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
out = os.path.abspath(arg("--out"))
os.makedirs(out, exist_ok=True)
names = arg("--clothes").split(",")

addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import HumanService, TargetService, LocationService, AssetService  # noqa: E402

AssetService.update_all_asset_lists()
macro = TargetService.get_default_macro_info_dict()
macro["gender"] = float(arg("--gender", "1.0"))
for kv in filter(None, (arg("--macro", "") or "").split(",")):
    k, v = kv.split("=")
    macro[k] = float(v)
body = HumanService.create_human(mask_helpers=True, detailed_helpers=False, extra_vertex_groups=True, feet_on_ground=True, scale=0.1,
                                 macro_detail_dict=macro)
HumanService.add_builtin_rig(body, "game_engine", import_weights=True)
data = LocationService.get_user_data()

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 640, 900
scene.world = bpy.data.worlds.new("w")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.42, 0.44, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3.5
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
cam.data.lens = 50
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(path, yaw):
    d = 4.2
    cam.location = Vector((math.sin(math.radians(yaw)) * -d, -math.cos(math.radians(yaw)) * d, 1.0))
    direction = Vector((0, 0, 0.92)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


for name in names:
    folder = os.path.join(data, "clothes", name)
    mhclo = next(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".mhclo"))
    try:
        g = HumanService.add_mhclo_asset(mhclo, body, asset_type="Clothes", subdiv_levels=0, material_type="MAKESKIN")
    except Exception as e:  # a candidate that does not load is triaged C, not a reason to stop the sheet
        print("CANDIDATE FAILED", name, e)
        continue
    for yaw in (0, 35, 180):
        shoot(os.path.join(out, f"{name}_{yaw:03d}.png"), yaw)
    bpy.data.objects.remove(g, do_unlink=True)
print("done", len(names))
