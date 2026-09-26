"""The garment QA sheet (charstd): an outfit on a body - the archetype's canonical body or a built instance - skinned by the standard
weight transfer, the regions its descriptors name hidden exactly as the game hides them, rendered at rest (front, back, side) and in
stress poses (arms raised, arms forward, deep stride, crouch, torso twist) so clipping, holes and bad weights show before export.

    blender -b --python garment_check.py -- --body archetype.blend --outfit id1,id2,... --garments <dir> --out sheet.png
                                             [--regions archetypes/<archetype>.regions.json] [--size 480]
"""
import math
import os
import subprocess
import sys
import tempfile

import bpy
import addon_utils
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
size = int(arg("--size", 480))
outfit = [x for x in arg("--outfit").split(",") if x]
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--body")))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services import ObjectService  # noqa: E402

body = next(o for o in bpy.data.objects if o.type == "MESH" and ObjectService.object_is_basemesh(o))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
descs = [common.descriptor(g) for g in outfit]
regions = common.load(arg("--regions", os.path.join(common.HERE, "archetypes", descs[0]["archetype"] + ".regions.json")))
for m in body.modifiers:
    if m.type == "MASK" and m.name != "Hide helpers":
        body.modifiers.remove(m)
for o in bpy.data.objects:
    if o.type == "MESH" and ObjectService.get_object_type(o) == "Clothes":
        bpy.data.objects.remove(o)
# Skinned from the whole body first; the hidden regions go after (a garment weighted from what is left would take the nearest
# visible skin - the hands beside the thighs - instead of the skin it covers).
for gid in outfit:
    g = common.append(os.path.join(os.path.abspath(arg("--garments")), gid, gid + ".blend"), gid)
    common.skin(g, body, rig)
hidden = sorted({r for d in descs for r in d["hides"]})
removed = common.hide_regions(body, regions, set(hidden))

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = size
scene.view_settings.view_transform = "Standard"
w = bpy.data.worlds.new("w")
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (0.5, 0.5, 0.52, 1)
w.node_tree.nodes["Background"].inputs[1].default_value = 0.9
scene.world = w
for rot, e in (((55, 0, -35), 3.0), ((70, 0, 150), 1.2)):
    s = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    s.data.energy = e
    s.rotation_euler = [math.radians(a) for a in rot]
    scene.collection.objects.link(s)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.lens = 50


def turn(bone, axis, deg):
    """A pose bone swung about a world axis through its own head."""
    pb = rig.pose.bones[bone]
    bpy.context.view_layer.update()
    head = pb.head.copy()
    R = Matrix.Rotation(math.radians(deg), 4, axis)
    pb.matrix = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ pb.matrix
    bpy.context.view_layer.update()


def reset():
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


POSES = {
    "arms raised": [("upper_arm.L", "Y", -70), ("upper_arm.R", "Y", 70)],
    "arms forward": [("upper_arm.L", "X", 60), ("upper_arm.R", "X", 60), ("upper_arm.L", "Z", 35), ("upper_arm.R", "Z", -35)],
    "stride": [("thigh.L", "X", 40), ("shin.L", "X", -35), ("thigh.R", "X", -25), ("shin.R", "X", -20)],
    "crouch": [("thigh.L", "X", 85), ("thigh.R", "X", 85), ("shin.L", "X", -110), ("shin.R", "X", -110), ("spine", "X", 20)],
    "twist": [("spine", "Z", 20), ("chest", "Z", 20), ("spine_mid", "X", 12), ("upper_arm.R", "X", 45)],
}
H = max(c[2] for c in common.shaped(body))


def shoot(name, yaw, height, dist, path):
    a = math.radians(yaw)
    aim = Vector((0, 0, height))
    cam.location = aim + Vector((math.sin(a) * dist, -math.cos(a) * dist, 0.1))
    cam.rotation_euler = (aim - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return (name, path)


tmp = tempfile.mkdtemp()
shots = []
reset()
for name, yaw in (("rest front", 0), ("rest back", 180), ("rest side", 90)):
    shots.append(shoot(name, yaw, H / 2, 4.3, os.path.join(tmp, f"{len(shots)}.png")))
shots.append(shoot("torso 3/4", 35, H * 0.72, 1.7, os.path.join(tmp, f"{len(shots)}.png")))
for name, moves in POSES.items():
    reset()
    for bone, axis, deg in moves:
        turn(bone, axis, deg)
    shots.append(shoot(name, 30, H * (0.4 if name == "crouch" else 0.55), 3.4, os.path.join(tmp, f"{len(shots)}.png")))
reset()
cols = 3
script = f"""
from PIL import Image, ImageDraw
shots = {shots!r}
S = {size}
rows = (len(shots) + {cols} - 1) // {cols}
sheet = Image.new("RGB", ({cols} * S, rows * S + 28), (30, 30, 30))
ImageDraw.Draw(sheet).text((8, 8), {("outfit: " + ", ".join(outfit) + "   hidden: " + ", ".join(hidden))!r}, fill=(255, 255, 255))
for i, (name, path) in enumerate(shots):
    im = Image.open(path).convert("RGB")
    ImageDraw.Draw(im).rectangle((0, 0, 8 * len(name) + 14, 22), fill=(0, 0, 0))
    ImageDraw.Draw(im).text((6, 5), name, fill=(255, 255, 255))
    sheet.paste(im, ((i % {cols}) * S, 28 + (i // {cols}) * S))
sheet.save({os.path.abspath(arg("--out"))!r})
"""
subprocess.run(["python", "-c", script], check=True)
print("GARMENT_CHECK", os.path.abspath(arg("--out")), "hidden faces", removed)
