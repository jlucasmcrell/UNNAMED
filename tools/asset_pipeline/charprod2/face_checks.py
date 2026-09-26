"""A character's face and neck under motion, as a labelled contact sheet (charprod2 verification): the face shape keys (blink, jaw,
speech shapes, smile, brows, frown) and the eyes' look straight on, and the neck's own range (turn both ways, look down, look up, tilt) three-quarter with the collar
in frame - the deformation the head wrap and the proxies' face keys must survive.

    blender -b --python face_checks.py -- --blend hybrid.blend --out sheet.png [--size 512]
"""
import math
import os
import sys
import tempfile

import bpy
import addon_utils
from mathutils import Euler, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
size = int(arg("--size", 512))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402
from bl_ext.user_default.mpfb.services.faceservice import ARKIT_FACEUNITS  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
keyed = [o for o in bpy.data.objects if o.type == "MESH" and o.data.shape_keys]
EXPRESSIONS = {
    "neutral": {},
    "blink": {"eyeBlinkLeft": 1, "eyeBlinkRight": 1},
    "jaw open": {"jawOpen": 0.8},
    "speech O": {"jawOpen": 0.3, "mouthFunnel": 0.8, "mouthPucker": 0.3},
    "smile": {"mouthSmileLeft": 1, "mouthSmileRight": 1, "cheekSquintLeft": 0.5, "cheekSquintRight": 0.5},
    "brows up": {"browInnerUp": 1, "browOuterUpLeft": 1, "browOuterUpRight": 1, "eyeWideLeft": 0.4, "eyeWideRight": 0.4},
    "frown": {"browDownLeft": 1, "browDownRight": 1, "mouthFrownLeft": 0.8, "mouthFrownRight": 0.8, "noseSneerLeft": 0.4,
              "noseSneerRight": 0.4},
}
LOOKS = {"eyes look left": 25, "eyes look right": -25}   # degrees about the vertical, the character's left positive
NECK = {"turn left 50": (0, 20, 0, 0, 30, 0), "turn right 50": (0, -20, 0, 0, -30, 0), "look down 35": (15, 0, 0, 20, 0, 0), "look up 30": (-12, 0, 0, -18, 0, 0),
        "tilt 20": (0, 0, 8, 0, 0, 12)}   # neck (x, y, z), head (x, y, z) in degrees about each bone's own axes


# The rest face: MPFB's neutral mouth is slightly parted; mouthClose 0.3 closes it (the facial contract's rest offset), dropped
# whenever the jaw opens.
REST = {"mouthClose": float(arg("--rest-mouth-close", 0.3))}


def set_expression(values):
    if not any(k.startswith("jaw") for k in values):
        values = {**REST, **values}
    for o in keyed:
        for kb in o.data.shape_keys.key_blocks:
            if kb.name in ARKIT_FACEUNITS:   # only the face channels: identity targets and macros keep their values
                kb.value = values.get(kb.name, 0.0)


def set_neck(angles):
    for bone, (x, y, z) in (("neck_01", angles[:3]), ("head", angles[3:])):
        pb = rig.pose.bones[bone]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = Euler((math.radians(x), math.radians(y), math.radians(z)))


scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = size
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.57, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
scene.world = world
for rot, energy in (((55, 0, -35), 3.0), ((70, 0, 140), 1.0)):
    s = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    s.data.energy = energy
    s.rotation_euler = [math.radians(a) for a in rot]
    scene.collection.objects.link(s)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 85
head = rig.matrix_world @ rig.data.bones["head"].head_local
target = head + Vector((0, 0, 0.06))


def shoot(yaw_deg, dist, aim, path):
    a = math.radians(yaw_deg)
    cam.location = aim + Vector((math.sin(a) * dist, -math.cos(a) * dist, 0.02))
    cam.rotation_euler = (aim - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


tmp = tempfile.mkdtemp()
shots = []
for name, values in EXPRESSIONS.items():
    set_expression(values)
    set_neck((0,) * 6)
    shoot(0, 0.75, target, os.path.join(tmp, f"e{len(shots)}.png"))
    shots.append((name, os.path.join(tmp, f"e{len(shots)}.png")))
# Eye motion: the eye bones where the rig has them, else the eyeballs turned about their own centres (with the ARKit lid keys).
eyes = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.get_object_type(o) == "Eyes")
base_co = [v.co.copy() for v in eyes.data.vertices]
for name, deg in LOOKS.items():
    lids = {"eyeLookOutLeft": 0.6, "eyeLookInRight": 0.6} if deg > 0 else {"eyeLookInLeft": 0.6, "eyeLookOutRight": 0.6}
    set_expression(lids)
    set_neck((0,) * 6)
    if "eye.L" in rig.pose.bones:
        for side in ("L", "R"):
            pb = rig.pose.bones[f"eye.{side}"]
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = Euler((0, 0, math.radians(deg)))
    else:
        from mathutils import Matrix
        for sel in (lambda c: c.x >= 0, lambda c: c.x < 0):
            idx = [i for i, c in enumerate(base_co) if sel(c)]
            centre = sum((base_co[i] for i in idx), Vector()) / len(idx)
            R = Matrix.Rotation(math.radians(deg), 3, "Z")
            for i in idx:
                eyes.data.vertices[i].co = centre + R @ (base_co[i] - centre)
        eyes.data.update()
    shoot(0, 0.75, target, os.path.join(tmp, f"l{len(shots)}.png"))
    shots.append((name, os.path.join(tmp, f"l{len(shots)}.png")))
for i, c in enumerate(base_co):
    eyes.data.vertices[i].co = c
eyes.data.update()
if "eye.L" in rig.pose.bones:
    for side in ("L", "R"):
        rig.pose.bones[f"eye.{side}"].rotation_euler = Euler((0, 0, 0))
set_expression({})
for name, angles in NECK.items():
    set_neck(angles)
    shoot(35, 1.1, head + Vector((0, 0, -0.02)), os.path.join(tmp, f"n{len(shots)}.png"))
    shots.append((name, os.path.join(tmp, f"n{len(shots)}.png")))

import subprocess  # noqa: E402
cols = 4
rows = (len(shots) + cols - 1) // cols
script = f"""
from PIL import Image, ImageDraw
shots = {shots!r}
S = {size}
sheet = Image.new("RGB", ({cols} * S, {rows} * S), (40, 40, 40))
for i, (name, path) in enumerate(shots):
    im = Image.open(path).convert("RGB")
    ImageDraw.Draw(im).rectangle((0, 0, 9 * len(name) + 16, 26), fill=(0, 0, 0))
    ImageDraw.Draw(im).text((8, 6), name, fill=(255, 255, 255))
    sheet.paste(im, ((i % {cols}) * S, (i // {cols}) * S))
sheet.save({os.path.abspath(arg("--out"))!r})
"""
subprocess.run(["python", "-c", script], check=True)
print("FACE_CHECKS", os.path.abspath(arg("--out")))
