"""A captured facial performance (capture_face_performance.py) played on any character that carries the face channel contract, and
rendered (charstd): each ARKit channel keyed onto every mesh with a shape key of that name, the eyes turned from the eyeLook channels
on the eye bones, the head rotation split between neck and head. The same file plays on every compatible face; nothing here knows
the mesh.

    blender -b --python play_face_performance.py -- --blend character.blend --performance perf.json --out clip.mp4
                                                     [--rest-mouth-close 0.3] [--size 540] [--head-gain 1.0]
"""
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile

import bpy
from mathutils import Euler, Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
perf = json.load(open(arg("--performance")))
size = int(arg("--size", 540))
rest_close = float(arg("--rest-mouth-close", 0.3))
gain = float(arg("--head-gain", 1.0))
bpy.ops.wm.open_mainfile(filepath=os.path.abspath(arg("--blend")))
rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
bones = rig.data.bones
HEAD = next(b for b in ("head", "Head") if b in bones)
NECK = next(b for b in ("neck", "neck_01", "Neck") if b in bones)
EYES = [b for b in ("eye.L", "eye.R") if b in bones]
channels = perf["channels"]
keyed = [(o, kb) for o in bpy.data.objects if o.type == "MESH" and o.data.shape_keys for kb in o.data.shape_keys.key_blocks
         if kb.name in channels or kb.name == "mouthClose"]
fps = int(round(perf["fps"]))
n = perf["frames"]
scene = bpy.context.scene
scene.render.fps = fps
scene.frame_start, scene.frame_end = 1, n


def eye_angles(f, side):
    c = lambda k: channels.get(k, [0.0] * n)[f]  # noqa: E731
    s = "Left" if side == "L" else "Right"
    # A character's left eye looks out to its left, in to its right; about 30 degrees at full weight.
    yaw = (c(f"eyeLookOut{s}") - c(f"eyeLookIn{s}")) * 30 * (1 if side == "L" else -1)
    pitch = (c(f"eyeLookUp{s}") - c(f"eyeLookDown{s}")) * 20
    return yaw, pitch


for f in range(n):
    frame = f + 1
    jaw = channels.get("jawOpen", [0.0] * n)[f]
    for o, kb in keyed:
        v = channels.get(kb.name, [0.0] * n)[f]
        if kb.name == "mouthClose":
            v = max(v, rest_close * (1 - min(1.0, jaw * 4)))   # the rest face's closed lips, released as the jaw opens
        kb.value = v
        kb.keyframe_insert("value", frame=frame)
    hx, hy, hz = (a * gain for a in perf["head_euler_deg"][f])
    for b, share in ((NECK, 0.4), (HEAD, 0.6)):
        pb = rig.pose.bones[b]
        pb.rotation_mode = "XYZ"
        # MediaPipe's camera frame: x pitch, y yaw, z roll; the bones point up (+Y along the bone), so yaw is about the bone's Y.
        pb.rotation_euler = Euler((math.radians(-hx * share), math.radians(hy * share), math.radians(-hz * share)))
        pb.keyframe_insert("rotation_euler", frame=frame)
    for b in EYES:
        pb = rig.pose.bones[b]
        yaw, pitch = eye_angles(f, b[-1])
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = Euler((math.radians(pitch), 0, math.radians(yaw)))
        pb.keyframe_insert("rotation_euler", frame=frame)

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
h = rig.matrix_world @ bones[HEAD].head_local
aim = h + Vector((0, -0.05, 0.05))
a = math.radians(12)
cam.location = aim + Vector((math.sin(a) * 0.95, -math.cos(a) * 0.95, 0.03))
cam.rotation_euler = (aim - cam.location).to_track_quat("-Z", "Y").to_euler()
tmp = tempfile.mkdtemp()
scene.render.filepath = os.path.join(tmp, "f_")
scene.render.image_settings.file_format = "PNG"
bpy.ops.render.render(animation=True)
out = os.path.abspath(arg("--out"))
subprocess.run(["ffmpeg", "-y", "-v", "error", "-framerate", str(fps), "-i", os.path.join(tmp, "f_%04d.png"), "-c:v", "libx264",
                "-pix_fmt", "yuv420p", "-crf", "18", out], check=True)
shutil.rmtree(tmp, ignore_errors=True)
print("PLAY_FACE_PERFORMANCE", out, {"channels_driven": len({kb.name for _, kb in keyed}), "meshes": len({o.name for o, _ in keyed}),
                                     "eye_bones": EYES, "frames": n})
