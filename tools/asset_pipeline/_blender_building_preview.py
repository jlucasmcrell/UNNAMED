"""Render an assembled building from a framed three-quarter view, with the framing numbers printed.

`_blender_preview.py` frames from `obj.matrix_world @ obj.bound_box`. That is correct when the objects
carry their node transforms, and silently wrong when an importer has baked those transforms into the
vertex data and left every object at identity - in the second case the union of local boxes describes
one piece, not the building, and both the camera centre and the radius come out wrong.

Rather than assume which case this is, this script measures both and prints them:

  * the naive union of `obj.bound_box` in world space, and
  * the union of the *evaluated mesh* vertices in world space, which is the truth whatever the
    importer did.

It then frames the camera on the true bounds, so the picture is usable regardless. It also prints the
per-axis split of where the geometry sits, which is what tells a wall from a roof.

Usage:
    blender --background --factory-startup --python _blender_building_preview.py -- \
        --input <file.glb> --out <dir> --name <name> [--views 3] [--size 900]
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--views", type=int, default=3)
    parser.add_argument("--size", type=int, default=900)
    parser.add_argument("--elevation", type=float, default=22.0,
                        help="Camera elevation above the horizon, in degrees")
    return parser.parse_args(argv)


def naive_bounds(objects):
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
    return low, high


def true_bounds(objects):
    """Union of evaluated vertices in world space - independent of what the importer did."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        matrix = evaluated.matrix_world
        for vertex in mesh.vertices:
            point = matrix @ vertex.co
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
        evaluated.to_mesh_clear()
    return low, high


def main():
    args = parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.input)

    objects = [o for o in bpy.data.objects if o.type == "MESH"]
    if not objects:
        raise SystemExit("no mesh objects imported")

    nlow, nhigh = naive_bounds(objects)
    tlow, thigh = true_bounds(objects)
    nsize, tsize = nhigh - nlow, thigh - tlow

    print(f"  BUILDING_BOUNDS {args.name}")
    print(f"     objects          : {len(objects)}")
    print(f"     naive  min/max   : {[round(v,3) for v in nlow]} {[round(v,3) for v in nhigh]}"
          f"  size {[round(v,3) for v in nsize]}")
    print(f"     true   min/max   : {[round(v,3) for v in tlow]} {[round(v,3) for v in thigh]}"
          f"  size {[round(v,3) for v in tsize]}")
    print(f"     naive == true    : {all(abs(nsize[i]-tsize[i]) < 1e-4 for i in range(3))}")

    # Where the mass sits vertically, which separates roof from walls without knowing the asset.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    top_third = wall_band = 0
    roof_cut = tlow[1] + tsize[1] * 0.66
    for obj in objects:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        matrix = evaluated.matrix_world
        local_top = max((matrix @ v.co).y for v in mesh.vertices)
        local_low = min((matrix @ v.co).y for v in mesh.vertices)
        if local_low >= roof_cut:
            top_third += 1
        elif local_top <= roof_cut:
            wall_band += 1
        evaluated.to_mesh_clear()
    print(f"     pieces above 66% : {top_third}   wholly below : {wall_band}")

    centre = (tlow + thigh) / 2.0
    radius = max(tsize.length / 2.0, 0.05)

    # Neutral three-point light, scaled to the subject so exposure is comparable across assets.
    #
    # The powers must sum to 1.0, not exceed it. `_blender_preview.py` calibrated 355 * radius^2 as the
    # irradiance that renders a surface at mid grey, and the first version of this file used scales of
    # 1.0, 0.45 and 0.7 - summing to 2.15x - so every building it produced was lit two stops hot and
    # read as washed-out white regardless of its materials. That is a property of the renderer, not of
    # the assets, and it would have sent me looking for a texture bug that was not there.
    for index, (azimuth_deg, power_scale) in enumerate(((35, 0.52), (215, 0.21), (140, 0.27))):
        data = bpy.data.lights.new(f"L{index}", type="AREA")
        data.energy = 355.0 * power_scale * (radius ** 2)
        # A small source, not `radius * 2`. At radius*2 on a 7 m building the three "lamps" are 14 m
        # wide softboxes, and any surface with a specular lobe returns a highlight larger than the
        # surface - the roof's shingle courses came back pure white while their base colour measured a
        # weathered mid-grey 112. The roughness values are the materials' own and should not be
        # changed to flatter a preview, so the light is made physically smaller instead.
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
    camera_data.lens = 50
    camera = bpy.data.objects.new("Cam", camera_data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    # Standard, not the AgX default. AgX is a filmic transform: it desaturates and lifts mid-tones,
    # which is right for a photograph and wrong for judging whether a material's albedo is correct.
    # A weathered slate roof at mean luminance 112 was rendering as near-white under it.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 92
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.film_transparent = False

    os.makedirs(args.out, exist_ok=True)
    distance = radius * 2.9
    elevation = math.radians(args.elevation)
    for view in range(args.views):
        azimuth = math.radians(view * (360.0 / args.views) + 35.0)
        camera.location = centre + Vector((
            distance * math.cos(elevation) * math.sin(azimuth),
            distance * math.sin(elevation),
            distance * math.cos(elevation) * math.cos(azimuth)))
        camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
        path = os.path.join(args.out, f"{args.name}_view{view}.jpg")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"  BUILDING_VIEW {path}")

    print(f"  BUILDING_RESULT {args.name} views={args.views} dir={args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
