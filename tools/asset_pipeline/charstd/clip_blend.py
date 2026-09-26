"""One clip eased toward a pose (charstd): the base clip's motion, with the named bones pulled part of the way toward another clip's first
frame on the same rig. Used for the sword set: the UAL attacks' deep lunges (the pack's stylised stance) brought up toward an upright
guard while the arms keep their cut.

    blender -b --python clip_blend.py -- --base <anim.glb> --toward <anim.glb> --bones hips,spine,chest,thigh,shin,foot --weight 0.5
        --out <anim.glb>

A bone matches when its name starts with one of the --bones prefixes; its rotation is slerped and its location lerped by --weight
(0 keeps the base, 1 takes the pose). The output keeps the base file's armature, rest and length.
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
prefixes = tuple(arg("--bones").split(","))
weight = float(arg("--weight", "0.5"))


def load(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(path))
    arm = next(o for o in bpy.data.objects if o not in before and o.type == "ARMATURE")
    return arm, arm.animation_data.action


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 60     # the clips' bake rate: imported keys land on whole frames, none are resampled away
base_arm, base_act = load(arg("--base"))
pose_arm, pose_act = load(arg("--toward"))
scene = bpy.context.scene
pose_arm.animation_data.action = pose_act
scene.frame_set(int(pose_act.frame_range[0]))
pose = {b.name: (b.location.copy(), b.rotation_quaternion.copy()) for b in pose_arm.pose.bones if b.name.startswith(prefixes)}
bpy.data.objects.remove(pose_arm, do_unlink=True)
bpy.data.actions.remove(pose_act)     # the file holds the one clip (the game plays a file's first animation)
b0, b1 = int(base_act.frame_range[0]), int(base_act.frame_range[1])
keys = {}
for f in range(b0, b1 + 1):
    scene.frame_set(f)
    frame = {}
    for pb in base_arm.pose.bones:
        if pb.name not in pose:
            continue
        loc, rot = pose[pb.name]
        q = pb.rotation_quaternion.copy()
        if q.dot(rot) < 0:
            rot = -rot
        frame[pb.name] = (pb.location.lerp(loc, weight), q.slerp(rot, weight))
    keys[f] = frame
for f, frame in keys.items():
    for name, (loc, rot) in frame.items():
        pb = base_arm.pose.bones[name]
        pb.rotation_mode = "QUATERNION"
        pb.location = loc
        pb.rotation_quaternion = rot
        pb.keyframe_insert("location", frame=f)
        pb.keyframe_insert("rotation_quaternion", frame=f)
bpy.ops.object.select_all(action="DESELECT")
base_arm.select_set(True)
for o in base_arm.children_recursive:
    o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.abspath(arg("--out")), export_format="GLB", use_selection=True, export_animations=True,
                          export_frame_range=False, export_force_sampling=True, export_optimize_animation_size=False, export_yup=True)
print("CLIP_BLEND", arg("--out"), len(keys), "frames", len(pose), "bones")
