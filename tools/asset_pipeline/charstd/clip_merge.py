"""One clip from two on the same rig (charstd): the base clip's motion everywhere except the named bones, which take the other clip's
motion time-warped onto the base clip's length (both loops start at their first frame). Used for the crouched walk: KayKit's sneak
gives the gait at the simulation's crouched pace, UAL's crouch walk the arms (KayKit's rig has no clavicles and its arms flare).

    blender -b --python clip_merge.py -- --base <anim.glb> --parts <anim.glb> --bones shoulder,upper_arm,forearm,hand,thumb,index,middle,ring,pinky
        --out <anim.glb>

A bone matches when its name starts with one of the --bones prefixes. The output keeps the base file's armature and rest.
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
prefixes = tuple(arg("--bones").split(","))


def load(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.abspath(path))
    arm = next(o for o in bpy.data.objects if o not in before and o.type == "ARMATURE")
    act = arm.animation_data.action if arm.animation_data and arm.animation_data.action else None
    return arm, act


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.render.fps = 60     # the clips' bake rate: imported keys land on whole frames, none are resampled away
base_arm, base_act = load(arg("--base"))
part_arm, part_act = load(arg("--parts"))
b0, b1 = base_act.frame_range
p0, p1 = part_act.frame_range
scene = bpy.context.scene
part_arm.animation_data.action = part_act
# Sample the parts clip at the base clip's frames, time-warped, and key the base armature's matching bones with those local poses.
samples = {}
for f in range(int(b0), int(b1) + 1):
    t = (f - b0) / max(b1 - b0, 1e-6)
    scene.frame_set(int(round(p0 + t * (p1 - p0))))
    samples[f] = {b.name: (b.location.copy(), b.rotation_quaternion.copy()) for b in part_arm.pose.bones if b.name.startswith(prefixes)}
bpy.context.view_layer.objects.active = base_arm
for f, poses in samples.items():
    for name, (loc, rot) in poses.items():
        pb = base_arm.pose.bones.get(name)
        if pb is None:
            continue
        pb.rotation_mode = "QUATERNION"
        pb.location = loc
        pb.rotation_quaternion = rot
        pb.keyframe_insert("location", frame=f)
        pb.keyframe_insert("rotation_quaternion", frame=f)
bpy.data.objects.remove(part_arm, do_unlink=True)
bpy.data.actions.remove(part_act)     # the file holds the one merged clip (the game plays a file's first animation)
bpy.ops.object.select_all(action="DESELECT")
base_arm.select_set(True)
for o in base_arm.children_recursive:
    o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.abspath(arg("--out")), export_format="GLB", use_selection=True, export_animations=True,
                          export_frame_range=False, export_force_sampling=True, export_optimize_animation_size=False, export_yup=True)
print("CLIP_MERGE", arg("--out"), len(samples), "frames")
