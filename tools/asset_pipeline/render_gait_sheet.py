"""Blender (Workbench, background): a body posed by locomotion clips the way the game plays them, front and side, several frames
per clip, for an old-vs-new comparison sheet. compose with gait_sheet.py.

    blender -b --factory-startup --python render_gait_sheet.py -- --body BODY_rigged.glb --out DIR ^
        --clip old_walk=anim.player.cw_player.walk.glb:1.3333 --clip new_walk=...walk.glb:1.2583 [--frames 6] [--px 360]

Each --clip is label=glb:cycle_s; frames are spread evenly over the first cycle_s seconds. A clip authored on another rest is
carried onto the body as ArtLibrary.Retarget does at runtime (rotation: body rest . clip rest^-1 . key; position: body rest +
(key - clip rest), the hips' travel scaled by the hips-height ratio), so a clip made on cw_player and one made on the body itself
are shown on equal terms. Writes DIR/<label>_<view>_<i>.png and DIR/frames.json.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _retarget_clip import Scene, mat_quat, ortho, qmat, trs  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]


def arg(name, default=None, many=False):
    vals = [argv[i + 1] for i, a in enumerate(argv) if a == name]
    return vals if many else (vals[0] if vals else default)


body_path, out = arg("--body"), arg("--out")
frames, px = int(arg("--frames", 6)), int(arg("--px", 360))
os.makedirs(out, exist_ok=True)
body = Scene(body_path)
hips_name = "hips" if "hips" in body.index else "pelvis"
body_rest = {n: trs(*body.rest[body.index[n]]) for n in body.joints}
body_h = body.pos(body.world(body.local()), hips_name)[1]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=body_path, bone_heuristic="BLENDER", disable_bone_shape=True)
rig = next(o for o in bpy.context.scene.objects if o.type == "ARMATURE")
for o in list(bpy.context.scene.objects):
    if o.type == "MESH" and any(k in o.name.lower() for k in ("lod1", "lod2", "lod3", "proxy")):
        bpy.data.objects.remove(o, do_unlink=True)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = px, int(px * 1.35)
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.82, 0.82, 0.8)
bpy.ops.mesh.primitive_plane_add(size=6)
floor = bpy.context.object
mat = bpy.data.materials.new("floor")
mat.diffuse_color = (0.55, 0.55, 0.52, 1)
floor.data.materials.append(mat)
# guides on the floor: two lines 0.20 m apart along the walking line (the reference stance's middle) and a cross line
for x in (-0.10, 0.10):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, 0, 0.001))
    g = bpy.context.object
    g.scale = (0.004, 2.5, 0.001)
    gm = bpy.data.materials.new("guide")
    gm.diffuse_color = (0.15, 0.35, 0.8, 1)
    g.data.materials.append(gm)
cams = {}
# both views look 12 degrees down so the floor and its guide lines read
for view, loc, rot in (("front", (0, -6, 2.2), (math.radians(78), 0, 0)), ("side", (6, 0, 2.2), (math.radians(78), 0, math.radians(90)))):
    cd = bpy.data.cameras.new(view)
    cd.type = "ORTHO"
    cd.ortho_scale = 2.3
    c = bpy.data.objects.new(view, cd)
    c.location, c.rotation_euler = loc, rot
    scene.collection.objects.link(c)
    cams[view] = c

bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="POSE")
record = {}
for spec in arg("--clip", many=True):
    label, rest = spec.split("=", 1)
    path, cycle = rest.rsplit(":", 1)
    clip = Scene(path)
    tracks = clip.tracks(clip.clip_names()[0])
    c_world = clip.world(clip.local())
    c_h = clip.pos(c_world, "hips" if "hips" in clip.index else "pelvis")[1]
    k = body_h / c_h
    record[label] = {"clip": path, "cycle_s": float(cycle), "hips_scale": round(float(k), 4), "times": []}
    for i in range(frames):
        t = float(cycle) * i / frames
        record[label]["times"].append(round(t, 4))
        loc = clip.local(tracks, t)
        for name in body.joints:
            pb = rig.pose.bones.get(name)
            if pb is None:
                continue
            want = body_rest[name].copy()
            if name in clip.index:
                ci = clip.index[name]
                c_rest = trs(*clip.rest[ci])
                key = loc[ci]
                R = ortho(body_rest[name][:3, :3]) @ ortho(c_rest[:3, :3]).T @ ortho(key[:3, :3])
                moved = key[:3, 3] - c_rest[:3, 3]
                travels = name in ("hips", "pelvis") or body.parent.get(body.index[name]) is None
                want[:3, :3] = R
                want[:3, 3] = body_rest[name][:3, 3] + (moved * k if travels else moved)
            basis = np.linalg.inv(body_rest[name]) @ want
            q = mat_quat(ortho(basis[:3, :3]))
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = Quaternion((q[3], q[0], q[1], q[2]))
            pb.location = Vector(basis[:3, 3].tolist())
        bpy.context.view_layer.update()
        for view, cam in cams.items():
            scene.camera = cam
            scene.render.filepath = os.path.join(out, f"{label}_{view}_{i}.png")
            bpy.ops.render.render(write_still=True)
with open(os.path.join(out, "frames.json"), "w", encoding="utf-8") as fh:
    json.dump({"body": body_path, "frames": frames, "clips": record}, fh, indent=1)
print("GAIT_SHEET rendered", len(record), "clips")
