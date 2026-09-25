"""Re-apply an asset's declared size after levelling: uniform scale about the ground origin.

The cleanup pass scales so the bounding box's longest side equals the declared size. On a
mesh reconstructed tilted, that box is the tilted one, so once levelled the asset is too long
(resource_ash_haft: 2.217 m for a declared 1.9 m). This rescales the levelled GLB uniformly so
its extent along one glTF axis equals the declared size; its base stays on the ground and its
footprint centre stays put. Materials, UVs and textures pass through unchanged. Run headless:
  blender --background --factory-startup --python _blender_set_length.py -- \
      --input <levelled.glb> --output <out.glb> --axis y --length 1.9
Prints SET_LENGTH_RESULT {scale, before, after} (glTF axes, metres).
"""
import argparse
import json
import sys

import bpy
from mathutils import Vector

GLTF_TO_BLENDER = {"x": 0, "y": 2, "z": 1}   # glTF Y-up is Blender Z-up; glTF Z is Blender -Y


def extent(objects):
    corners = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    return (Vector([min(c[i] for c in corners) for i in range(3)]),
            Vector([max(c[i] for c in corners) for i in range(3)]))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--axis", choices=["x", "y", "z"], default="y")
    parser.add_argument("--length", type=float, required=True)
    args = parser.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.input)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    roots = [o for o in bpy.context.scene.objects if o.parent is None]
    lows, highs = extent(meshes)
    axis = GLTF_TO_BLENDER[args.axis]
    scale = args.length / (highs[axis] - lows[axis])
    pivot = Vector(((lows.x + highs.x) / 2, (lows.y + highs.y) / 2, lows.z))
    for root in roots:
        root.matrix_world = (__import__("mathutils").Matrix.Translation(pivot)
                             @ __import__("mathutils").Matrix.Scale(scale, 4)
                             @ __import__("mathutils").Matrix.Translation(-pivot)
                             @ root.matrix_world)
    bpy.context.view_layer.update()
    after_lows, after_highs = extent(meshes)
    for o in bpy.context.scene.objects:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=args.output, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_normals=True, export_materials="EXPORT", export_texcoords=True)
    size = lambda lo, hi: [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)]
    print("SET_LENGTH_RESULT " + json.dumps({"scale": round(scale, 6), "before_xyz": size(lows, highs),
                                             "after_xyz": size(after_lows, after_highs),
                                             "ground": round(after_lows.z, 5)}))


if __name__ == "__main__":
    main()
