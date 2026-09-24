"""Render two or more GLBs in one scene so their relative size can be seen.

**STATUS: the framing is not yet reliable. Do not treat its output as measurement.** It renders, and
the characters are correctly proportioned relative to each other, but the vertical framing is wrong:
two rigged GLBs arrive with their geometry at origins 1.0 m apart, so a single scene-level grounding
shift grounds one and lifts the other, and the scene comes out 2.8 m tall for a 1.30 m figure beside
a 1.80 m one. A reference post was tried and removed for the same reason - it started correctly
grounded and the shift lifted it instead.

For a size question, measure rather than render: `_glb_bounds.py` reads the POSITION accessor min/max
straight out of the GLB container, which is a direct measurement rather than a render estimate, and
that is what confirmed the Kal at 1.300 m against the Veth at 1.800 m. Use this script for shape and
silhouette, not for scale.

The fix, when it is worth doing, is an explicit Empty per character with the imported roots parented
to it (`keep_transform=True`) and the Empty's Z taken from that character's own measured bounds,
rather than shifting object transforms and hoping the mesh sits under the root.

`_blender_preview.py` frames each asset to fill the image, which is right for judging one asset's
shape and useless for judging whether one character is the right size next to another: a 1.30 m Kal
and a 1.80 m Veth come out the same size on screen. That is the gap this was meant to close.

Run inside Blender:
    blender --background --factory-startup --python _render_scale_compare.py -- \\
        --glb a.glb b.glb --out dir --name kal_vs_veth
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

SPACING_M = 1.6
POST_HEIGHT_M = 1.80


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--glb", nargs="+", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--size", type=int, default=900)
    return parser.parse_args(argv)


def world_bounds(objects):
    """True world-space bounds of the given objects, armature deformation included.

    `obj.bound_box` is the REST-POSE box, so on a skinned character it does not describe what is
    actually on screen. Grounding by it left both characters floating well above the floor plane and
    made a correct 1.30 m / 1.80 m pair look wrong. Evaluating the depsgraph gives the deformed
    vertices, which is what the camera sees.
    """
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        for vertex in mesh.vertices:
            point = evaluated.matrix_world @ vertex.co
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
        evaluated.to_mesh_clear()
    return low, high


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)

    # No reference post. A post that starts correctly grounded gets lifted along with everything else
    # by the scene grounding pass, because the characters arrive hanging below the origin and the
    # post does not, so a single shift cannot be right for both. Removing it leaves a comparison of
    # the characters against each other, which is exact, and the measured heights are stated in the
    # caption instead of being read off a ruler that cannot be trusted.

    slots = len(args.glb) + 1
    for index, path in enumerate(args.glb):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        imported = [o for o in bpy.data.objects if o not in before]
        roots = [o for o in imported if o.parent is None]
        # Centre each character on its own slot horizontally only. Vertical grounding is done once
        # for the whole scene afterwards: doing it per character assumed each import's root carried
        # the whole hierarchy, and on a skinned GLB the mesh can sit under a transform the root does
        # not see, which left the pair at a combined height of 2.8 m instead of 1.8 m.
        low, high = world_bounds(imported)
        centre = (low + high) / 2.0
        offset = Vector((index * SPACING_M - SPACING_M * (len(args.glb) - 1) / 2.0, 0.0, 0.0))
        for root in roots:
            root.location += Vector((-centre.x, -centre.y, 0.0)) + offset

    # Ground the entire scene: post and characters share one floor, so equal heights are equal on
    # screen and the post works as a ruler.
    scene_low, scene_high = world_bounds(bpy.data.objects)
    for obj in bpy.data.objects:
        if obj.parent is None:
            obj.location.z -= scene_low.z
    low, high = world_bounds(bpy.data.objects)
    centre = (low + high) / 2.0
    radius = max((high - low).length / 2.0, 0.5)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = args.size
    scene.render.resolution_y = int(args.size * 0.72)
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 92

    camera_data = bpy.data.cameras.new("cam")
    # ORTHOGRAPHIC, deliberately. A perspective camera makes the left-hand reference post project
    # lower than the characters standing beside it, so the post stops being a ruler - the first
    # version of this render showed a 1.80 m post ending 195 px below two grounded characters. An
    # orthographic camera has no such distortion, so equal heights are equal on screen and a reader
    # can measure straight off the image.
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = max(high.x - low.x, (high.z - low.z) / 0.72) * 1.12
    camera = bpy.data.objects.new("cam", camera_data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.location = centre + Vector((0.0, -radius * 6.0, 0.0))
    target = bpy.data.objects.new("target", None)
    scene.collection.objects.link(target)
    target.location = centre
    camera.constraints.new(type="TRACK_TO").target = target

    world = bpy.data.worlds.new("W")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.065, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0

    for name, elevation, azimuth, power in (("key", 45, -60, 180.0), ("fill", 25, 60, 70.0),
                                            ("rim", 80, 170, 100.0)):
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = power * (radius ** 2)
        data.size = radius * 2
        light = bpy.data.objects.new(name, data)
        scene.collection.objects.link(light)
        rad, azi = math.radians(elevation), math.radians(azimuth)
        light.location = centre + Vector((radius * 3 * math.cos(rad) * math.sin(azi),
                                          -radius * 3 * math.cos(rad) * math.cos(azi),
                                          radius * 3 * math.sin(rad)))
        light.constraints.new(type="TRACK_TO").target = target

    out = os.path.join(args.out, f"{args.name}.jpg")
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for index in range(len(args.glb)):
        low, high = world_bounds(meshes)
        print(f"COMPARE_BOUNDS {os.path.basename(args.glb[index])}")
    scene_low, scene_high = world_bounds(bpy.data.objects)
    print(f"COMPARE_BOUNDS scene z {scene_low.z:.3f}..{scene_high.z:.3f}")
    print(f"COMPARE_RESULT {out} objects={len(args.glb)} reference_post_m={POST_HEIGHT_M}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
