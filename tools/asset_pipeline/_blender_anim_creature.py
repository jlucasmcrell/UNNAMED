"""Apply procedural motion to a creature's own rig and export an animation-only GLB.

`_blender_retarget.py` drives the canonical humanoid skeleton. Enemies are not on that skeleton:
each is rigged to its own armature (18 bones for a quadruped, 20 for a humanoid creature, 5 for a
worm), so their clips are applied directly to those bones by name instead.

The mesh is deleted before export, so the result is animation-only and can be loaded alongside the
rigged body without duplicating its geometry - the same contract the player clips follow.

Usage:
    blender --background --factory-startup --python _blender_anim_creature.py -- \
        --rigged <creature_rigged.glb> --motion <motion.json> --out <clip.glb> \
        --fps 30 --loop
"""
import argparse
import json
import math
import os
import sys

import bpy

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from _blender_cleanup import reset_scene  # noqa: E402


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--rigged", required=True)
    parser.add_argument("--motion", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--fps", type=float, default=30.0)
    parser.add_argument("--loop", action="store_true")
    return parser.parse_args(argv)


def import_rigged(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=path)
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError(f"no armature in {path}")
    return armatures[0]


def apply_motion(rig, frames, fps):
    """Key every named bone per frame, in degrees converted to radians."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    missing = set()
    keyed = set()
    for index, frame in enumerate(frames):
        bpy.context.scene.frame_set(index)
        for bone_name, transform in frame.items():
            bone = rig.pose.bones.get(bone_name)
            if bone is None:
                missing.add(bone_name)
                continue
            bone.rotation_mode = "XYZ"
            rotation = transform.get("rotation")
            if rotation:
                bone.rotation_euler = [math.radians(value) for value in rotation]
            location = transform.get("location")
            if location:
                # Bone-local translation; only the root and hips use it, for a vertical bob.
                bone.location = location
            bone.keyframe_insert(data_path="rotation_euler", frame=index)
            if location:
                bone.keyframe_insert(data_path="location", frame=index)
            keyed.add(bone_name)
    bpy.ops.object.mode_set(mode="OBJECT")
    return keyed, missing


def main():
    args = parse_args()
    with open(args.motion, encoding="utf-8") as handle:
        motion = json.load(handle)
    frames = motion["frames"]

    reset_scene()
    rig = import_rigged(args.rigged)

    # Remove the mesh so the export is animation-only and does not duplicate the creature's
    # geometry in every clip.
    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    scene = bpy.context.scene
    # The glTF exporter converts frame numbers to seconds using the SCENE rate. Leaving it at
    # Blender's 24 fps default exported the player clips 25% too long; the same trap applies here.
    scene.render.fps = int(round(args.fps))
    scene.render.fps_base = 1.0
    scene.frame_start = 0
    scene.frame_end = max(len(frames) - 1, 0)

    keyed, missing = apply_motion(rig, frames, args.fps)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj is rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=False,
        export_materials="NONE", export_texcoords=False,
        export_skins=True, export_extras=False)

    print("CREATURE_ANIM_RESULT " + json.dumps({
        "rigged": os.path.basename(args.rigged),
        "kind": motion["kind"],
        "plan": motion["plan"],
        "out": args.out,
        "frames": len(frames),
        "bones_keyed": sorted(keyed),
        "bones_missing": sorted(missing),
        "duration_s": round((len(frames) - 1) / args.fps, 4),
        "fps": args.fps,
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
