"""A gait clip's swinging feet eased toward a neutral ankle (charstd). A capture's runner points the lifted foot hard down and turns it in
behind the standing leg; on a booted body that reads, from the game's camera, as a foot broken at the instep. While a foot is off the
ground its rotation (and its toe's) is slerped toward the rest - the foot square to the shin - by --strength, fully from --lift above
its lowest height and not at all on the ground: the stance, the heel strike and the push-off stay as captured.

    blender -b --python clip_swing_ankle.py -- --clip <anim.glb> --out <anim.glb> [--strength 0.7] [--lift 0.10]
"""
import os
import sys

import bpy
from mathutils import Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
strength = float(arg("--strength", "0.7"))
lift = float(arg("--lift", "0.10"))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 60     # the clips' bake rate: imported keys land on whole frames, none are resampled away
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--clip")))
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = arm.animation_data.action
scene = bpy.context.scene
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
sides = [s for s in "LR" if f"foot.{s}" in arm.pose.bones]
height = {s: [] for s in sides}
poses = []
for f in range(f0, f1 + 1):
    scene.frame_set(f)
    frame = {}
    for s in sides:
        height[s].append((arm.matrix_world @ arm.pose.bones[f"foot.{s}"].head).z)
        for b in (f"foot.{s}", f"toe.{s}"):
            if b in arm.pose.bones:
                frame[b] = arm.pose.bones[b].rotation_quaternion.copy()
    poses.append(frame)
eased = 0
for i, f in enumerate(range(f0, f1 + 1)):
    for s in sides:
        above = height[s][i] - min(height[s])
        w = strength * min(1.0, max(0.0, (above - 0.02) / max(lift - 0.02, 1e-3)))
        for b in (f"foot.{s}", f"toe.{s}"):
            if b not in poses[i]:
                continue
            pb = arm.pose.bones[b]
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = poses[i][b].slerp(Quaternion(), w)
            pb.keyframe_insert("rotation_quaternion", frame=f)
            eased += w > 0
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
for o in arm.children_recursive:
    o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.abspath(arg("--out")), export_format="GLB", use_selection=True, export_animations=True,
                          export_frame_range=False, export_force_sampling=True, export_optimize_animation_size=False, export_yup=True)
print("CLIP_SWING_ANKLE", arg("--out"), f1 - f0 + 1, "frames", eased, "bone-frames eased")
