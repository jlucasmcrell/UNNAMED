"""Blender: AnimSequence FBX files (export_pack.py --animations) -> one GLB per clip, the clip on its UE skeleton, which is what
_retarget_clip.py reads as a source (exact glTF sampling, the skeleton's reference pose as the node rest).

    blender --background --factory-startup --python anim_fbx_to_glb.py -- <pack>_anim [--match regex] [--out <dir>]

Writes <out>/<key>.glb (default <pack>_anim/Glb) with the action named after the FBX key. The FBX node defaults are the
skeleton's reference pose (no preview mesh is exported), so the GLB rest is the UE reference pose; UE centimetres stay as a
0.01 scale on the armature node, which the retarget's forward kinematics carries.
"""
import os
import re
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
pack = argv[0]
match = argv[argv.index("--match") + 1] if "--match" in argv else ""
out = argv[argv.index("--out") + 1] if "--out" in argv else os.path.join(pack, "Glb")
os.makedirs(out, exist_ok=True)
done = 0
for name in sorted(os.listdir(os.path.join(pack, "Animations"))):
    key, ext = os.path.splitext(name)
    if ext.lower() != ".fbx" or not re.search(match, key, re.I):
        continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(pack, "Animations", name), use_anim=True, ignore_leaf_bones=False,
                             automatic_bone_orientation=False, use_custom_props=False)
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if len(arms) != 1 or not (arms[0].animation_data and arms[0].animation_data.action):
        print(f"ANIM_GLB skip {key}: {len(arms)} armatures / no action")
        continue
    rig = arms[0]
    for obj in list(bpy.context.scene.objects):
        if obj is not rig:
            bpy.data.objects.remove(obj, do_unlink=True)
    action = rig.animation_data.action
    action.name = key
    f0, f1 = (int(round(v)) for v in action.frame_range)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = f0, f1
    for obj in scene.objects:
        obj.select_set(obj is rig)
    bpy.ops.export_scene.gltf(filepath=os.path.join(out, key + ".glb"), export_format="GLB", use_selection=True,
                              export_apply=False, export_yup=True, export_materials="NONE", export_skins=True,
                              export_animations=True, export_animation_mode="ACTIVE_ACTIONS", export_force_sampling=True,
                              export_frame_range=False, export_def_bones=False)
    done += 1
    print(f"ANIM_GLB {key} frames {f0}-{f1} fps {scene.render.fps}/{scene.render.fps_base:.4f}")
print(f"ANIM_GLB done {done}")
