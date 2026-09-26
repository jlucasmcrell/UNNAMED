"""A character's clean-topology base from MPFB2 (Blender extension, GPL-3 tool; its system assets are CC0): the MakeHuman base mesh
at the spec's macro settings and height, the game-engine rig, high-poly eyes, eyebrows, eyelashes, teeth, tongue, a hair proxy,
a starting skin, and the ARKit-52 face units as shape keys - saved as a .blend, with preview renders (front, side, face, hands).

    blender -b --python mpfb_base.py -- --spec specs/player.json --out <dir>

The spec's "mpfb" block: macro (gender, age, muscle, weight, height, proportions, race), height_m (the character's authored height,
top of the head), eyebrows, eyelashes, hair, clothes (a list), skin, eyes, rig.
"""
import json
import math
import os
import sys

import bpy
import addon_utils
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
spec_path = argv[argv.index("--spec") + 1]
out = argv[argv.index("--out") + 1]
os.makedirs(out, exist_ok=True)
spec = json.load(open(spec_path, encoding="utf-8"))["mpfb"]

addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import HumanService, TargetService, LocationService, AssetService  # noqa: E402
from bl_ext.user_default.mpfb.services.faceservice import FaceService  # noqa: E402

bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
AssetService.update_all_asset_lists()

macro = TargetService.get_default_macro_info_dict()
for k, v in spec["macro"].items():
    if k == "race":
        macro["race"].update(v)
    else:
        macro[k] = v
basemesh = HumanService.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True, feet_on_ground=True,
                                     scale=0.1, macro_detail_dict=macro)
data = LocationService.get_user_data()
# The rig first: MPFB rigs a proxy or garment only when a rig is already there (added after, they would not deform).
rig = HumanService.add_builtin_rig(basemesh, spec.get("rig", "game_engine"), import_weights=True)


def asset(kind, name, ext=".mhclo"):
    folder = os.path.join(data, kind, name)
    for f in os.listdir(folder):
        if f.endswith(ext):
            return os.path.join(folder, f)
    raise SystemExit(f"no {ext} in {folder}")


def mhclo(kind, name, asset_type):
    return HumanService.add_mhclo_asset(asset(kind, name), basemesh, asset_type=asset_type, subdiv_levels=0, material_type="MAKESKIN")


eyes = HumanService.add_mhclo_asset(asset("eyes", spec.get("eyes", "high-poly")), basemesh, asset_type="Eyes", subdiv_levels=0,
                                    material_type="MAKESKIN")
parts = {"eyes": eyes}
for kind, typ in (("eyebrows", "Eyebrows"), ("eyelashes", "Eyelashes"), ("teeth", "Teeth"), ("tongue", "Tongue")):
    if spec.get(kind):
        parts[kind] = mhclo(kind, spec[kind], typ)
if spec.get("hair"):
    parts["hair"] = mhclo("hair", spec["hair"], "Hair")
# Garments from the CC0 wardrobe, fitted to the body by their mhclo (they hide the skin they cover by their own delete groups).
for name in spec.get("clothes", []):
    parts[name] = mhclo("clothes", name, "Clothes")
if spec.get("skin"):
    mhmat = asset("skins", spec["skin"], ".mhmat")
    HumanService.set_character_skin(mhmat, basemesh, skin_type="ENHANCED")

# The face units (ARKit 52) and the visemes as shape keys on the base mesh: blink, jaw, speech.
try:
    # With the visemes too (MPFB's CC0 visemes01 / visemes02 packs, installed 2026-09-26): the speech channels of the face contract.
    vis = spec.get("visemes", ["microsoft", "meta"])
    FaceService.load_targets(basemesh, load_microsoft_visemes="microsoft" in vis, load_meta_visemes="meta" in vis,
                             load_arkit_faceunits=True)
except Exception as e:  # recorded, never silently skipped
    print("FACEUNITS_FAILED", e)

# The authored height: scale the whole character (rig included) so the top of the head is height_m. Measured on the shaped body as
# it renders (the base mesh's own coordinates are the neutral figure, and MPFB's helper geometry - a hair cap above the scalp among
# it - is masked out of the render).
bpy.context.view_layer.update()


def evaluated_top(obj):
    e = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    m = e.to_mesh()
    z = max((obj.matrix_world @ v.co).z for v in m.vertices)
    e.to_mesh_clear()
    return z


top = evaluated_top(basemesh)
if parts.get("hair"):
    top = max(top, evaluated_top(parts["hair"]))
s = spec["height_m"] / top
root = rig if rig is not None else basemesh
root.scale = (s, s, s)
bpy.context.view_layer.update()

counts = {name: len(o.data.polygons) for name, o in parts.items() if o is not None}
counts["body"] = len(basemesh.data.polygons)
keys = [k.name for k in basemesh.data.shape_keys.key_blocks] if basemesh.data.shape_keys else []
report = {"height_scale": round(s, 4), "top_before_m": round(top, 4), "polygons": counts, "shape_keys": len(keys),
          "arkit_like": [k for k in keys if any(t in k.lower() for t in ("blink", "jaw", "mouth", "eye"))][:12],
          "bones": len(rig.data.bones) if rig else 0, "bone_names": [b.name for b in rig.data.bones][:80] if rig else []}
json.dump(report, open(os.path.join(out, "mpfb_base.json"), "w"), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "mpfb_base.blend"))

# Preview renders: front and side full body, the face close, the hands.
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = 1024, 1024
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.75, 0.76, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
scene.world = world
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
sun.data.energy = 3
sun.rotation_euler = (math.radians(50), 0, math.radians(30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
H = spec["height_m"]


def shot(name, target, distance, yaw_deg, lens):
    yaw = math.radians(yaw_deg)
    t = Vector(target)
    cam.location = t + Vector((math.sin(yaw) * distance, -math.cos(yaw) * distance, 0))
    cam.rotation_euler = (math.radians(90), 0, yaw)
    cam.data.lens = lens
    scene.render.filepath = os.path.join(out, f"preview_{name}.png")
    bpy.ops.render.render(write_still=True)


shot("front", (0, 0, H / 2), 4.2, 0, 50)
shot("side", (0, 0, H / 2), 4.2, 90, 50)
shot("back", (0, 0, H / 2), 4.2, 180, 50)
shot("face", (0, 0, H - 0.13), 0.9, 0, 85)
shot("face_side", (0, 0, H - 0.13), 0.9, 90, 85)
print("MPFB_BASE", json.dumps({k: report[k] for k in ("height_scale", "polygons", "shape_keys", "bones")}))
