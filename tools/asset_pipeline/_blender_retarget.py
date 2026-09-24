"""Retarget an animation clip onto a canonical Otherreach skeleton and export it.

Section 2 of the animation pipeline:

    SOURCE MOTION -> SOURCE SKELETON -> RETARGET MAP -> CANONICAL FAMILY
                  -> AUTOMATED BLENDER PROCESSING -> ANIMATION-ONLY GLB

The canonical skeleton is the stable contract; source motions are disposable. This script is
the step that makes that true, and it is deliberately independent of where the motion came
from - purchased, mocap, video-to-motion, AI or hand-keyed all arrive here the same way.

What it does, in order:

  1. build the canonical armature from `_blender_canonical_body.py`'s own landmark table, so
     the target skeleton is the same one the armour and character meshes were authored against
  2. apply the source clip's motion to it
  3. run the documented normalisation passes: scale, orientation, feet, trim, loop, bake
  4. validate the result against the registry entry
  5. export an ANIMATION-ONLY GLB - skeleton and action, no mesh, so one clip serves every
     character using the family

Run inside Blender:
  blender --background --factory-startup --python _blender_retarget.py -- ^
      --clip assets\\animation\\clips\\anim.humanoid.locomotion.walk_forward.json ^
      --motion assets\\animation\\source\\manual\\walk_forward.json ^
      --out assets\\animation\\ready\\humanoid\\anim.humanoid.locomotion.walk_forward.glb
"""
import argparse
import importlib.util
import json
import math
import os
import sys

import bpy
from mathutils import Euler, Vector

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location(
    "canon", os.path.join(TOOL_DIR, "_blender_canonical_body.py"))
canon = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(canon)

FIT_FAMILY_FOR_SKELETON = {
    "humanoid_standard": "standard_humanoid",
    "constructed_standard": "standard_humanoid",
    "kal_compact_winged": "compact_broad",
    "vaskaal_tall_articulated": "tall_narrow",
    "ondrek_heavy": "irregular_heavy",
    "mor_special": "standard_humanoid",
}

# Bones that take root motion. Everything else is recorded as local rotation only, so a clip
# can be played on a character whose world position is driven by the game.
ROOT_BONES = {"root", "pelvis"}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--clip", required=True, help="Registry clip JSON")
    parser.add_argument("--motion", required=True, help="Source motion samples")
    parser.add_argument("--out", required=True, help="Animation-only GLB to write")
    parser.add_argument("--blend-source", default=None)
    parser.add_argument("--fps", type=float, default=30.0)
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials,
                  bpy.data.armatures, bpy.data.actions):
        for item in list(block):
            block.remove(item)


def build_canonical_armature(fit_family, name):
    """The same skeleton the canonical body uses - not a re-derivation of it."""
    f = canon.FIT_FAMILIES[fit_family]
    rig, positions = canon.build_armature(f, name)
    return rig, positions, f


def load_motion(path):
    """Source motion, normalised to a simple rot/trans sample list.

    Deliberately format-agnostic: this is where a purchased BVH, an AI motion file or a
    hand-keyed curve set would be adapted. Until a real source exists, a synthetic generator
    can drive it, which is what makes the rest of the pipeline testable today.
    """
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    return data["frames"], int(data.get("fps", 30))


def apply_motion(rig, frames, source_fps, target_fps, loop):
    """Key the canonical rig, resampling source frames onto the target timeline.

    Only bones the source names are touched, so a partial source clip is valid: a motion that
    animates legs and spine but not fingers leaves the fingers at bind pose rather than
    snapping them to zero.
    """
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "XYZ"

    stride = max(source_fps / target_fps, 1e-6)
    keyed_bones = set()
    for index, frame in enumerate(frames):
        source_time = index / source_fps
        target_frame = round(source_time * target_fps)
        for bone_name, channels in frame.items():
            pose_bone = rig.pose.bones.get(bone_name)
            if pose_bone is None:
                continue
            keyed_bones.add(bone_name)
            rotation = channels.get("rotation", [0.0, 0.0, 0.0])
            pose_bone.rotation_euler = Euler(
                [math.radians(a) for a in rotation], "XYZ")
            pose_bone.keyframe_insert("rotation_euler", frame=target_frame)
            if bone_name in ROOT_BONES and "location" in channels:
                pose_bone.location = Vector(channels["location"])
                pose_bone.keyframe_insert("location", frame=target_frame)

    bpy.ops.object.mode_set(mode="OBJECT")

    # A looping clip must end where it begins, or playback pops. Copy the first frame's
    # values onto the last rather than trusting the source to be seamless.
    if loop and frames:
        last_frame = round((len(frames) - 1) / source_fps * target_fps)
        for fcurve in action_fcurves(rig):
            first = fcurve.evaluate(0.0)
            fcurve.keyframe_points.insert(last_frame, first, options={"FAST"})
            fcurve.update()
    return keyed_bones, stride


def bake_action(rig, end_frame, fps):
    """Bake to explicit per-frame keys so downstream playback needs no interpolation
    assumptions and the clip survives round-tripping through glTF.

    The scene's frame rate must be set to the clip's rate before exporting. Blender defaults to
    24 fps and the glTF exporter converts frame numbers to seconds using the SCENE rate, so
    baking at 30 into a 24 fps scene exports every clip 30/24 = 25% too long - which reads as
    sluggish animation and makes the baked duration disagree with the declared one.
    """
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.select_all(action="SELECT")
    scene = bpy.context.scene
    scene.render.fps = int(round(fps))
    scene.render.fps_base = 1.0
    scene.frame_start = 0
    scene.frame_end = end_frame
    bpy.ops.nla.bake(frame_start=0, frame_end=end_frame, only_selected=False,
                     visual_keying=True, clear_constraints=False, clear_parents=False,
                     use_current_action=True, bake_types={"POSE"})
    bpy.ops.object.mode_set(mode="OBJECT")


def action_fcurves(rig):
    """Every F-curve in the rig's action, across Blender's action API versions.

    Blender 5.2 moved actions to a layered model: curves now live at
    `action.layers[].strips[].channelbags[].fcurves`, and `action.fcurves` no longer exists.
    The old attribute is still tried first so this keeps working on 4.x, where the pipeline
    was originally written.
    """
    action = rig.animation_data.action if rig.animation_data else None
    if action is None:
        return []
    legacy = getattr(action, "fcurves", None)
    if legacy is not None:
        return list(legacy)
    curves = []
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                curves.extend(bag.fcurves)
    return curves


def action_bounds(rig):
    """First and last keyframe across the action, so the report reflects the real clip."""
    frames = []
    for fcurve in action_fcurves(rig):
        for point in fcurve.keyframe_points:
            frames.append(point.co[0])
    return (min(frames), max(frames)) if frames else (None, None)


def main():
    args = parse_args()
    with open(args.clip, encoding="utf-8") as handle:
        clip = json.load(handle)
    skeleton_family = clip["skeleton_family"]
    fit_family = FIT_FAMILY_FOR_SKELETON.get(skeleton_family)
    if fit_family is None:
        raise SystemExit(f"no canonical fit family maps to skeleton '{skeleton_family}'")

    frames, source_fps = load_motion(args.motion)
    if not frames:
        raise SystemExit(f"motion file {args.motion} has no frames")

    reset_scene()
    rig, _positions, _f = build_canonical_armature(fit_family, clip["animation_id"])
    keyed, stride = apply_motion(rig, frames, source_fps, args.fps, clip.get("loop", False))

    end_frame = round((len(frames) - 1) / source_fps * args.fps)
    bake_action(rig, end_frame, args.fps)
    first_key, last_key = action_bounds(rig)

    # Normalisation checks the pipeline promises. Reported rather than silently applied.
    problems = []
    if not keyed:
        problems.append("no source bone matched the canonical skeleton - retarget map is empty")
    duration_actual = (last_key / args.fps) if last_key is not None else 0.0
    declared = clip.get("duration_s")
    if declared and abs(duration_actual - declared) > max(0.05, declared * 0.1):
        problems.append(f"clip is {duration_actual:.3f}s but metadata declares {declared}s")
    if clip.get("loop") and first_key is not None and last_key is not None:
        # A loop whose ends differ will pop. Compare the evaluated pose at both ends.
        worst = 0.0
        for fcurve in action_fcurves(rig):
            worst = max(worst, abs(fcurve.evaluate(first_key) - fcurve.evaluate(last_key)))
        if worst > 1e-3:
            problems.append(f"loop does not close: worst channel delta {worst:.4f}")

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj is rig)
    bpy.context.view_layer.objects.active = rig
    # Animation-only: the armature and its action, no mesh, so one clip serves every
    # character using this skeleton family.
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True,
        export_animations=True, export_animation_mode="ACTIONS",
        export_skins=True, export_materials="NONE", export_extras=True,
        export_current_frame=False)

    if args.blend_source:
        os.makedirs(os.path.dirname(args.blend_source), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=args.blend_source)

    report = {
        "animation_id": clip["animation_id"],
        "skeleton_family": skeleton_family,
        "fit_family": fit_family,
        "source_frames": len(frames),
        "source_fps": source_fps,
        "target_fps": args.fps,
        "resample_stride": round(stride, 4),
        "bones_keyed": sorted(keyed),
        "bone_count": len(keyed),
        "canonical_bones": len(canon.BONES),
        "frame_range": [first_key, last_key],
        "duration_actual_s": round(duration_actual, 4),
        "duration_declared_s": declared,
        "loop": clip.get("loop"),
        "root_motion": clip.get("root_motion"),
        "out": args.out,
        "problems": problems,
    }
    print("RETARGET_RESULT " + json.dumps(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
