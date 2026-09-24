"""Render an NPC animation clip on an actual NPC rig, so the poses can be looked at.

`_render_anim_preview.py` renders against the canonical fit body (`--fit-family standard_humanoid`),
whose bones are named `upperarm_l`, `calf_l`, `pelvis`. The NPC clips animate the simplified rig's
bones - `upper_arm.L`, `shin.L`, `hips` - so the retargeter finds nothing to drive and every clip comes
back as an A-pose. All four social clips rendered identically, which looks like four broken clips and is
actually one wrong body.

That is the same name mismatch `docs/SKELETON_CONTRACT_RECONCILIATION.md` records between the 52-bone
canonical skeleton and the 20-bone NPC rig, showing up as a rendering fault. Reviewing a 20-bone clip
needs a 20-bone body, which is what this does: it reuses `_blender_anim_creature`'s own
`import_rigged` and `apply_motion`, so the motion is applied by the same code that exports the clips.

Usage:
    blender --background --factory-startup --python _render_npc_preview.py -- \
        --rigged <npc_rigged.glb> --motion <motion.json> --out <dir> --frames 0 24 48
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


def load_creature_tool():
    """Reuse the exporter's own import and motion application, so preview matches what is exported."""
    path = os.path.join(TOOL_DIR, "_blender_anim_creature.py")
    spec = importlib.util.spec_from_file_location("_anim_creature", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--rigged", required=True)
    parser.add_argument("--motion", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--frames", type=int, nargs="+", required=True)
    parser.add_argument("--size", type=int, default=560)
    parser.add_argument("--azimuth", type=float, default=34.0)
    return parser.parse_args(argv)


def main():
    args = parse_args()
    creature = load_creature_tool()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    rig = creature.import_rigged(args.rigged)

    with open(args.motion, encoding="utf-8") as handle:
        motion = json.load(handle)
    rig, keyed, missing = None, [], []
    rig = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"][0]
    keyed, missing = creature.apply_motion(rig, motion["frames"],
                                           int(motion.get("fps", 30)))
    print(f"  NPC_PREVIEW keyed={len(keyed)} missing={len(missing)}"
          + (f" MISSING={missing}" if missing else ""))

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        for vertex in mesh.vertices:
            point = evaluated.matrix_world @ vertex.co
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
        evaluated.to_mesh_clear()
    centre = (low + high) / 2.0
    radius = max((high - low).length / 2.0, 0.2)

    for index, (azimuth_deg, scale) in enumerate(((34, 0.55), (206, 0.22), (150, 0.23))):
        data = bpy.data.lights.new(f"L{index}", type="AREA")
        data.energy = 355.0 * scale * (radius ** 2)
        data.size = radius * 0.35
        light = bpy.data.objects.new(f"L{index}", data)
        bpy.context.collection.objects.link(light)
        azimuth = math.radians(azimuth_deg)
        light.location = centre + Vector((
            radius * 3 * math.cos(math.radians(35)) * math.sin(azimuth),
            radius * 3 * math.sin(math.radians(35)),
            -radius * 3 * math.cos(math.radians(35)) * math.cos(azimuth)))
        light.rotation_euler = (centre - light.location).to_track_quat("-Z", "Y").to_euler()

    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.06, 1.0)
    bpy.context.scene.world = world

    camera_data = bpy.data.cameras.new("Cam")
    camera_data.lens = 60
    camera = bpy.data.objects.new("Cam", camera_data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    azimuth = math.radians(args.azimuth)
    camera.location = centre + Vector((
        radius * 2.7 * math.sin(azimuth), radius * 0.35, radius * 2.7 * math.cos(azimuth)))
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()

    scene = bpy.context.scene
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "PNG"
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.fps = int(motion.get("fps", 30))

    os.makedirs(args.out, exist_ok=True)
    for frame in args.frames:
        scene.frame_set(frame)
        path = os.path.join(args.out, f"f{frame:03d}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"  NPC_PREVIEW_VIEW {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
