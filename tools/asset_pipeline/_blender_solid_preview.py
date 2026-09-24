"""Render a GLB with flat solid shading, so geometry can be judged without materials in the way.

When a render looks wrong there are two possibilities and they need separating before either is fixed:
the geometry is arranged badly, or the surfaces are shaded badly. Materials hide the answer, because a
chaotic-looking surface can be a fine silhouette covered in a badly mapped texture.

Workbench shading draws every surface as flat clay with no textures and no image-based lighting, so
what is left is shape. If the asset reads as a building here, the geometry is sound and the fault is in
the materials; if it still reads as a tangle, no material work will save it.

Usage:
    blender --background --factory-startup --python _blender_solid_preview.py -- \
        --input <file.glb> --out <dir> --name <name> [--views 2] [--size 880]
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
    parser.add_argument("--views", type=int, default=2)
    parser.add_argument("--size", type=int, default=880)
    parser.add_argument("--elevation", type=float, default=24.0)
    return parser.parse_args(argv)


def true_bounds(objects):
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
    low, high = true_bounds(objects)
    size = high - low
    centre = (low + high) / 2.0
    radius = max(size.length / 2.0, 0.05)
    print(f"  SOLID_BOUNDS {args.name} size {[round(v,3) for v in size]}")

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.62, 0.60, 0.57)
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.cavity_type = "BOTH"
    scene.display.shading.curvature_ridge_factor = 1.0
    scene.display.shading.curvature_valley_factor = 1.0
    scene.render.resolution_x = args.size
    scene.render.resolution_y = args.size
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 92

    camera_data = bpy.data.cameras.new("Cam")
    camera_data.lens = 50
    camera = bpy.data.objects.new("Cam", camera_data)
    bpy.context.collection.objects.link(camera)
    scene.camera = camera

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
        path = os.path.join(args.out, f"{args.name}_solid{view}.jpg")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"  SOLID_VIEW {path}")
    print(f"  SOLID_RESULT {args.name} views={args.views}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
