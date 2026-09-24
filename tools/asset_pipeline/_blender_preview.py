"""Render turnaround previews of a GLB so an asset can actually be looked at.

Nothing in the pipeline ever showed what a finished mesh looks like; verification only
proves the file is structurally valid. That means a mesh can pass every check while being
the wrong shape, and there is no way to compare two quality settings visually.

Writes a single contact sheet per asset with four views, so one file answers "is this
right?" without opening a modelling tool.

Run inside Blender:
  blender --background --factory-startup --python _blender_preview.py -- ^
      --input ready\\name\\name.glb --out review\\name.jpg
"""
import argparse
import math
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--out", required=True, help="Output directory for view images")
    parser.add_argument("--name", default=None)
    parser.add_argument("--size", type=int, default=512)
    parser.add_argument("--views", type=int, default=4)
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def import_glb(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"no mesh in {path}")
    return meshes


def bounds(objects):
    bpy.context.view_layer.update()
    corners = []
    for obj in objects:
        corners += [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c[i] for c in corners) for i in range(3)))
    high = Vector((max(c[i] for c in corners) for i in range(3)))
    return low, high


def setup_world():
    world = bpy.data.worlds.new("W")
    bpy.context.scene.world = world
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs[0].default_value = (0.05, 0.055, 0.065, 1.0)
    background.inputs[1].default_value = 1.0


def add_lights(center, radius):
    """Three-point setup scaled to the subject so small and large assets both read.

    Energies are per-light because a key/fill/rim rig needs a ratio, not one flat value. The
    previous version gave all three 800 * radius^2, which is roughly six times the irradiance that
    lands a 0.5-albedo surface at mid grey - so pale assets rendered pure white and could not be
    judged. A weathered timber wall and a limestone wall looked identical.

    Power is in watts; a light of power P at distance d gives roughly P / (4 * pi * d^2) W/m^2, and
    a surface reads as mid grey at about 3 W/m^2. With the lamps at 3 * radius, P = 355 * radius^2
    is correct for a single lamp, so the three sum to that rather than each hitting it.
    """
    specs = [("key", 45, -60, 180.0), ("fill", 25, 60, 70.0), ("rim", 80, 170, 100.0)]
    for name, elevation, azimuth, power in specs:
        data = bpy.data.lights.new(name, type="AREA")
        data.energy = power * (radius ** 2)
        data.size = radius * 2
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        rad = math.radians(elevation)
        azi = math.radians(azimuth)
        obj.location = center + Vector((
            radius * 3 * math.cos(rad) * math.sin(azi),
            -radius * 3 * math.cos(rad) * math.cos(azi),
            radius * 3 * math.sin(rad)))


def render_view(camera, center, radius, azimuth, out_path, size):
    rad = math.radians(azimuth)
    camera.location = center + Vector((
        radius * 3.1 * math.sin(rad),
        -radius * 3.1 * math.cos(rad),
        radius * 1.5))
    direction = center - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    name = args.name or os.path.splitext(os.path.basename(args.input))[0]
    os.makedirs(args.out, exist_ok=True)

    reset_scene()
    objects = import_glb(args.input)
    low, high = bounds(objects)
    center = (low + high) / 2
    radius = max((high - low).length / 2, 0.05)

    setup_world()
    add_lights(center, radius)

    camera_data = bpy.data.cameras.new("Cam")
    camera_data.lens = 60
    camera = bpy.data.objects.new("Cam", camera_data)
    bpy.context.collection.objects.link(camera)
    bpy.context.scene.camera = camera

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 90

    written = []
    for index in range(args.views):
        azimuth = int(360 * index / args.views)
        path = os.path.join(args.out, f"{name}_view{index}.jpg")
        render_view(camera, center, radius, azimuth, path, args.size)
        written.append(path)

    print(f"PREVIEW_RESULT {name} views={len(written)} dir={args.out}")


if __name__ == "__main__":
    main()
