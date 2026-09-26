"""The facial bakeoff's standardized test sequence for one candidate (FACIAL_PIPELINE_BAKEOFF.md): matched views and motion, the same
framing and light for every candidate.

    static: front neutral, three-quarter, profile, rear and neck, the eyes close, the mouth close
    motion: blink (one eye), both eyes closed, jaw open, mouth closed, smile, concern, brows raised, brows lowered, eyes left, eyes
            right, head left, head up, head down
    visemes: the fifteen Meta visemes, where the face has them (MPFB's visemes02)
    video:  8 s at 24 fps - blinks, eye saccades, a speech-shape sequence, smile, concern, brows, a head turn

    blender -b --python face_bakeoff.py -- --blend candidate.blend --out <dir> [--static-only] [--rest-mouth-close 0.3] [--size 512]

A .glb with no face channels (the rejected baseline) gets the static views only. Writes <dir>/static.png, motion.png, video.mp4.
"""
import math
import os
import shutil
import subprocess
import sys
import tempfile

import bpy
import bmesh
import addon_utils
from mathutils import Euler, Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
out = os.path.abspath(arg("--out"))
size = int(arg("--size", 512))
os.makedirs(out, exist_ok=True)
src = os.path.abspath(arg("--blend"))
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
if src.endswith(".glb"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    for o in list(bpy.data.objects):
        if o.type == "MESH" and ("LOD" in o.name or not o.data.materials):
            bpy.data.objects.remove(o)
else:
    bpy.ops.wm.open_mainfile(filepath=src)
addon_utils.enable("bl_ext.user_default.mpfb", default_set=True)
from bl_ext.user_default.mpfb.services.faceservice import ARKIT_FACEUNITS, META_VISEMES, MICROSOFT_VISEMES  # noqa: E402

CHANNELS = set(ARKIT_FACEUNITS) | set(META_VISEMES) | set(MICROSOFT_VISEMES)

rig = next(o for o in bpy.data.objects if o.type == "ARMATURE")
bones = rig.data.bones
HEAD = next(b for b in ("head", "Head") if b in bones)
NECK = next(b for b in ("neck_01", "neck", "Neck") if b in bones)
def face_keyed():
    return [o for o in bpy.data.objects if o.type == "MESH" and o.data.shape_keys
            and any(k.name in CHANNELS for k in o.data.shape_keys.key_blocks)]


static_only = "--static-only" in argv or not face_keyed()
REST = {"mouthClose": float(arg("--rest-mouth-close", 0.3))}

# The eyes turn on eye bones (the production skeleton has them; an MPFB game-engine rig does not, so they are added here the way
# charstd/standardize_rig.py adds them): each eyeball's vertices bound to its bone through the eye mesh's own armature modifier.
eye_bones = []
eye_mesh = next((o for o in bpy.data.objects if o.type == "MESH" and ("high-poly" in o.name or o.name.lower().startswith("eye"))
                 and any(m.type == "ARMATURE" for m in o.modifiers)), None)
if not static_only and eye_mesh is not None:
    dg = bpy.context.evaluated_depsgraph_get()
    me = eye_mesh.evaluated_get(dg).to_mesh()
    pts = [eye_mesh.matrix_world @ v.co for v in me.vertices]
    eye_mesh.evaluated_get(dg).to_mesh_clear()
    sides = {"L": [i for i, p in enumerate(pts) if p.x >= 0], "R": [i for i, p in enumerate(pts) if p.x < 0]}
    names = {s: (f"eye.{s}" if f"eye.{s}" in bones else f"eye_test_{s}") for s in sides}
    if any(n not in bones for n in names.values()):
        winv = rig.matrix_world.inverted()
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode="EDIT")
        for s, idx in sides.items():
            if names[s] in rig.data.edit_bones:
                continue
            c = sum((pts[i] for i in idx), Vector()) / len(idx)
            b = rig.data.edit_bones.new(names[s])
            b.head = winv @ c
            b.tail = winv @ (c + Vector((0, 0, 0.02)))
            b.parent = rig.data.edit_bones[HEAD]
        bpy.ops.object.mode_set(mode="OBJECT")
        for g in list(eye_mesh.vertex_groups):
            eye_mesh.vertex_groups.remove(g)
        for s, idx in sides.items():
            eye_mesh.vertex_groups.new(name=names[s]).add(idx, 1.0, "REPLACE")
    eye_bones = list(names.values())
keyed = face_keyed()


def set_face(values):
    if not any(k.startswith("jaw") for k in values):
        values = {**REST, **values}
    for o in keyed:
        for kb in o.data.shape_keys.key_blocks:
            if kb.name in CHANNELS:
                kb.value = values.get(kb.name, 0.0)


def set_pose(neck=(0, 0, 0), head=(0, 0, 0), eyes=0.0):
    for b, r in ((NECK, neck), (HEAD, head)):
        pb = rig.pose.bones[b]
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = Euler([math.radians(a) for a in r])
    for b in eye_bones:
        # A turn about the world's vertical through the eye's centre, whatever way the bone points.
        pb = rig.pose.bones[b]
        pb.rotation_mode = "QUATERNION"
        pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        h = pb.head.copy()
        pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(math.radians(eyes), 4, "Z") @ Matrix.Translation(-h) @ pb.matrix
    bpy.context.view_layer.update()


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
headpos = rig.matrix_world @ bones[HEAD].head_local
# The eyes' centre from the geometry that carries an eye material (every candidate names it so), for the close-ups.
eye_pts = []
for o in bpy.data.objects:
    if o.type == "MESH" and o.visible_get():
        idx = {i for i, m in enumerate(o.data.materials) if m and m.name.lower().startswith("eye")}
        if idx:
            vs = {v for p in o.data.polygons if p.material_index in idx for v in p.vertices}
            eye_pts += [o.matrix_world @ o.data.vertices[v].co for v in vs]
eye_c = sum(eye_pts, Vector()) / len(eye_pts) if eye_pts else headpos + Vector((0, -0.06, 0.06))
face = Vector((0, eye_c.y + 0.06, eye_c.z - 0.03))


def shoot(path, yaw, aim, dist, lens=85, pitch=0.0):
    a, p = math.radians(yaw), math.radians(pitch)
    cam.data.lens = lens
    cam.location = aim + Vector((math.sin(a) * math.cos(p) * dist, -math.cos(a) * math.cos(p) * dist, math.sin(p) * dist + 0.02))
    cam.rotation_euler = (aim - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def sheet(items, path, cols):
    script = f"""
from PIL import Image, ImageDraw
items = {items!r}
S = {size}
rows = (len(items) + {cols} - 1) // {cols}
sh = Image.new("RGB", ({cols} * S, rows * S), (40, 40, 40))
for i, (name, p) in enumerate(items):
    im = Image.open(p).convert("RGB")
    ImageDraw.Draw(im).rectangle((0, 0, 8 * len(name) + 14, 22), fill=(0, 0, 0))
    ImageDraw.Draw(im).text((6, 5), name, fill=(255, 255, 255))
    sh.paste(im, ((i % {cols}) * S, (i // {cols}) * S))
sh.save({path!r})
"""
    subprocess.run(["python", "-c", script], check=True)


tmp = tempfile.mkdtemp()
set_face({})
set_pose()
static = []
for name, yaw, aim, dist, lens, pitch in (("front neutral", 0, face, 0.75, 85, 0), ("three-quarter", 35, face, 0.8, 85, 0),
                                          ("profile", 90, face, 0.8, 85, 0), ("rear and neck", 160, headpos - Vector((0, 0, 0.03)), 1.0, 70, 5),
                                          ("eyes close", 0, Vector((0, eye_c.y, eye_c.z)), 0.35, 100, 0),
                                          ("mouth close", 0, Vector((0, eye_c.y - 0.01, eye_c.z - 0.08)), 0.35, 100, 0)):
    p = os.path.join(tmp, f"s{len(static)}.png")
    shoot(p, yaw, aim, dist, lens, pitch)
    static.append((name, p))
sheet(static, os.path.join(out, "static.png"), 3)
if static_only:
    print("FACE_BAKEOFF static only", out)
    sys.exit(0)

POSES = [("blink one eye", {"eyeBlinkLeft": 1}, {}), ("both eyes closed", {"eyeBlinkLeft": 1, "eyeBlinkRight": 1}, {}),
         ("jaw open", {"jawOpen": 0.75}, {}), ("mouth closed", {}, {}),
         ("smile", {"mouthSmileLeft": 0.9, "mouthSmileRight": 0.9, "cheekSquintLeft": 0.4, "cheekSquintRight": 0.4}, {}),
         ("concern", {"browInnerUp": 0.8, "mouthFrownLeft": 0.5, "mouthFrownRight": 0.5, "mouthPressLeft": 0.3, "mouthPressRight": 0.3}, {}),
         ("brows raised", {"browInnerUp": 1, "browOuterUpLeft": 1, "browOuterUpRight": 1}, {}),
         ("brows lowered", {"browDownLeft": 1, "browDownRight": 1}, {}),
         ("eyes left", {"eyeLookOutLeft": 0.5, "eyeLookInRight": 0.5}, {"eyes": 22}),
         ("eyes right", {"eyeLookInLeft": 0.5, "eyeLookOutRight": 0.5}, {"eyes": -22}),
         ("head left", {}, {"neck": (0, 18, 0), "head": (0, 25, 0)}),
         ("head up", {}, {"neck": (-10, 0, 0), "head": (-15, 0, 0)}), ("head down", {}, {"neck": (12, 0, 0), "head": (18, 0, 0)})]
motion = []
for name, fv, pose in POSES:
    set_face(fv)
    set_pose(**pose)
    p = os.path.join(tmp, f"m{len(motion)}.png")
    shoot(p, 0 if not name.startswith("head") else 25, face, 0.8)
    motion.append((name, p))
sheet(motion, os.path.join(out, "motion.png"), 4)
has_visemes = any(kb.name in META_VISEMES for o in keyed for kb in o.data.shape_keys.key_blocks)
if has_visemes:
    vis = []
    set_pose()
    for v in META_VISEMES:
        set_face({v: 1.0})
        p = os.path.join(tmp, f"v{len(vis)}.png")
        shoot(p, 0, face + Vector((0, 0, -0.04)), 0.55)
        vis.append((v, p))
    sheet(vis, os.path.join(out, "visemes.png"), 5)

# The video: keyframed face channels, eyes and head over 8 s.
fps, frames = 24, 192
scene.render.fps = fps
scene.frame_start, scene.frame_end = 1, frames
VIS = [{"jawOpen": 0.3}, {"jawOpen": 0.2, "mouthFunnel": 0.7}, {"jawOpen": 0.1, "mouthStretchLeft": 0.5, "mouthStretchRight": 0.5},
       {"mouthClose": 0.5, "mouthPressLeft": 0.5, "mouthPressRight": 0.5}, {"jawOpen": 0.35}, {"mouthPucker": 0.8, "jawOpen": 0.1},
       {"mouthRollLower": 0.5, "jawOpen": 0.1}, {"jawOpen": 0.25, "mouthSmileLeft": 0.2, "mouthSmileRight": 0.2}]
timeline = {}   # frame -> (face dict, pose dict)


def at(t, face=None, pose=None):
    timeline[int(round(t * fps)) + 1] = (face or {}, pose or {})


at(0.0)
at(0.45)
at(0.5, {"eyeBlinkLeft": 1, "eyeBlinkRight": 1})
at(0.62)
at(0.9, {}, {"eyes": 20})
at(1.25, {}, {"eyes": -20})
at(1.6, {}, {"eyes": 0})
t = 1.7
if has_visemes:
    # "We can't stay here - the stones are moving." as Meta visemes, about eight a second.
    SPEECH = ["viseme_U", "viseme_I", "viseme_kk", "viseme_aa", "viseme_nn", "viseme_DD", "viseme_SS", "viseme_DD", "viseme_E",
              "viseme_I", "viseme_RR", "viseme_sil", "viseme_TH", "viseme_SS", "viseme_DD", "viseme_O", "viseme_nn", "viseme_SS",
              "viseme_aa", "viseme_RR", "viseme_PP", "viseme_U", "viseme_I", "viseme_nn", "viseme_sil"]
    for v in SPEECH:
        at(t, {v: 1.0})
        t += 0.1
else:
    for i in range(16):
        at(t, VIS[i % len(VIS)])
        t += 0.14
at(t)
at(3.0, VIS[1] | {"eyeBlinkLeft": 1, "eyeBlinkRight": 1})
at(4.2, {"mouthSmileLeft": 0.9, "mouthSmileRight": 0.9, "cheekSquintLeft": 0.4, "cheekSquintRight": 0.4})
at(4.9, {"mouthSmileLeft": 0.9, "mouthSmileRight": 0.9, "cheekSquintLeft": 0.4, "cheekSquintRight": 0.4})
at(5.4, {"browInnerUp": 0.8, "mouthFrownLeft": 0.5, "mouthFrownRight": 0.5})
at(6.0, {"browInnerUp": 1, "browOuterUpLeft": 1, "browOuterUpRight": 1})
at(6.5, {}, {"neck": (0, 0, 0), "head": (0, 0, 0)})
at(7.1, {}, {"neck": (0, 14, 0), "head": (0, 20, 0), "eyes": 12})
at(7.4, {"eyeBlinkLeft": 1, "eyeBlinkRight": 1}, {"neck": (0, 14, 0), "head": (0, 20, 0), "eyes": 12})
at(7.55, {}, {"neck": (0, 14, 0), "head": (0, 20, 0), "eyes": 12})
at(8.0, {}, {"neck": (0, 0, 0), "head": (0, 0, 0)})
channels = sorted({k for fv, _ in timeline.values() for k in fv} | set(REST))
for f, (fv, pose) in sorted(timeline.items()):
    scene.frame_set(f)
    set_face(fv)
    for o in keyed:
        for kb in o.data.shape_keys.key_blocks:
            if kb.name in channels:
                kb.keyframe_insert("value", frame=f)
    set_pose(**{"neck": pose.get("neck", (0, 0, 0)), "head": pose.get("head", (0, 0, 0)), "eyes": pose.get("eyes", 0.0)})
    for b in (NECK, HEAD):
        rig.pose.bones[b].keyframe_insert("rotation_euler", frame=f)
    for b in eye_bones:
        rig.pose.bones[b].keyframe_insert("rotation_quaternion", frame=f)
a = math.radians(12)
cam.data.lens = 85
cam.location = face + Vector((math.sin(a) * 0.9, -math.cos(a) * 0.9, 0.02))
cam.rotation_euler = (face - cam.location).to_track_quat("-Z", "Y").to_euler()
fdir = os.path.join(tmp, "frames")
os.makedirs(fdir)
scene.render.filepath = os.path.join(fdir, "f_")
scene.render.image_settings.file_format = "PNG"
bpy.ops.render.render(animation=True)
subprocess.run(["ffmpeg", "-y", "-v", "error", "-framerate", str(fps), "-i", os.path.join(fdir, "f_%04d.png"), "-c:v", "libx264",
                "-pix_fmt", "yuv420p", "-crf", "18", os.path.join(out, "video.mp4")], check=True)
shutil.rmtree(tmp, ignore_errors=True)
print("FACE_BAKEOFF", out)
