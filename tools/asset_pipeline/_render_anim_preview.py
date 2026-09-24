"""Render preview frames of a source motion on the canonical body, so a clip can be looked at.

A clip can pass every structural check - animation-only, closed loop, correct duration, real bone
targets - and still be obviously wrong: a collapse that sinks through the floor, an arm that folds
backwards, a crouch that is a squat with the feet in the air. None of those are visible in a JSON
report, and all of them are visible in one render.

This poses the canonical body with the *same* motion code the exporter uses (`apply_motion` from
_blender_retarget.py), so what is rendered is what is exported, then renders the frames asked for
from a fixed camera under neutral light.

Run inside Blender:
  blender --background --factory-startup --python _render_anim_preview.py -- ^
      --motion assets\\animation\\source\\manual\\death.json ^
      --out assets\\review\\anim_preview\\death --frames 0 15 31 60
"""
import argparse
import importlib.util
import json
import math
import os
import sys

import bpy
from mathutils import Vector

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))


def load_tool(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(TOOL_DIR, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


canon = load_tool("canon", "_blender_canonical_body.py")
retarget = load_tool("retarget", "_blender_retarget.py")


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--motion", required=True)
    parser.add_argument("--out", required=True, help="Output directory")
    parser.add_argument("--frames", type=int, nargs="+", required=True)
    parser.add_argument("--fit-family", default="standard_humanoid")
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--azimuth", type=float, default=38.0,
                        help="Camera rotation about the vertical axis, degrees")
    parser.add_argument("--width", type=int, default=480)
    parser.add_argument("--height", type=int, default=620)
    parser.add_argument("--aim-height", type=float, default=0.95,
                        help="Camera aim point above the ground, metres")
    return parser.parse_args(argv)


def build_stage(azimuth, aim_height):
    """A camera on a 3/4 view and a three-point-ish neutral rig. Fixed, so two clips compare."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"

    world = bpy.data.worlds.new("preview_world")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.48, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    scene.world = world

    # A ground plane at Z=0 so the feet can be judged against something, with a checker-free matte
    # so nothing in the image is mistaken for character detail.
    bpy.ops.mesh.primitive_plane_add(size=12.0, location=(0.0, 0.0, 0.0))
    ground = bpy.context.active_object
    ground.name = "preview_ground"
    material = bpy.data.materials.new("preview_ground")
    material.use_nodes = True
    material.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.30, 0.31, 0.30, 1.0)
    ground.data.materials.append(material)

    radius = 4.6
    angle = math.radians(azimuth)
    camera_data = bpy.data.cameras.new("preview_camera")
    camera_data.lens = 62.0
    camera = bpy.data.objects.new("preview_camera", camera_data)
    camera.location = (math.sin(angle) * radius, math.cos(angle) * radius, 1.45)
    bpy.context.collection.objects.link(camera)

    aim = bpy.data.objects.new("preview_aim", None)
    aim.location = (0.0, 0.0, aim_height)
    bpy.context.collection.objects.link(aim)
    constraint = camera.constraints.new(type="TRACK_TO")
    constraint.target = aim
    constraint.track_axis = "TRACK_NEGATIVE_Z"
    constraint.up_axis = "UP_Y"
    scene.camera = camera

    for name, offset, energy in (("key", (2.6, 3.0, 4.2), 900.0),
                                 ("fill", (-3.4, 1.4, 2.0), 260.0),
                                 ("rim", (-1.2, -3.4, 3.2), 420.0)):
        light_data = bpy.data.lights.new(f"preview_{name}", type="AREA")
        light_data.energy = energy
        light_data.size = 3.0
        light = bpy.data.objects.new(f"preview_{name}", light_data)
        light.location = offset
        direction = aim.location - Vector(offset)
        light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        bpy.context.collection.objects.link(light)

    scene.render.resolution_x = bpy.context.scene.render.resolution_x
    return scene


def main():
    args = parse_args()
    with open(args.motion, encoding="utf-8") as handle:
        motion_data = __import__("json").load(handle)

    fit = canon.FIT_FAMILIES[args.fit_family]
    canon.reset_scene()
    name = f"{args.fit_family}_preview"
    rig, positions = canon.build_armature(fit, name)
    body = canon.build_body(fit, name, 16)

    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    scene = build_stage(args.azimuth, fit["total_height"] if "total_height" in fit else 1.8)
    scene.render.resolution_x = args.width
    scene.render.resolution_y = args.height
    scene.render.fps = args.fps
    scene.frame_start = 0
    scene.frame_end = max(args.frames)

    frames = motion_data["frames"]
    retarget.apply_motion(rig, frames, int(motion_data.get("fps", args.fps)), args.fps, False)

    os.makedirs(args.out, exist_ok=True)
    written = []
    for frame in args.frames:
        scene.frame_set(frame)
        path = os.path.join(args.out, f"f{frame:03d}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        written.append(path)

    print("PREVIEW_RESULT " + __import__("json").dumps({
        "motion": args.motion,
        "frames": args.frames,
        "outputs": written,
        "fit_family": args.fit_family,
        "source_frames": len(frames),
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
