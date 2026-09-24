"""Report what Blender's glTF importer actually does to a kit piece's transform.

The assembly instantiator places pieces by setting an object transform, which is only correct if it
knows whether the importer leaves the geometry in glTF axes (with the Y-up conversion carried on the
object) or bakes the conversion into the geometry (leaving the object identity). Guessing wrong
either double-applies the conversion or drops it, and both produce a scrambled building.

This answers the question by importing one piece and printing its rotation and bounds rather than
reasoning about it.

Run inside Blender:
    blender --background --factory-startup --python _probe_gltf_import.py -- <piece.glb>
"""
import sys

import bpy
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=path)

for obj in bpy.data.objects:
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    if obj.type == "MESH":
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
    dims = high - low
    print(f"PROBE {obj.name!r} type={obj.type} "
          f"rot_euler=({obj.rotation_euler.x:.4f},{obj.rotation_euler.y:.4f},{obj.rotation_euler.z:.4f}) "
          f"rot_mode={obj.rotation_mode} "
          f"loc=({obj.location.x:.3f},{obj.location.y:.3f},{obj.location.z:.3f}) "
          f"world_dims=({dims.x:.3f},{dims.y:.3f},{dims.z:.3f})")
