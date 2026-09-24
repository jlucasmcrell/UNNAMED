"""Rescale a rigged (skinned) GLB in Blender, keeping the skin valid.

`_rescale_glb.py` refuses skinned meshes, and it is right to: a skinned GLB carries inverse bind
matrices expressed in the old scale, so rewriting POSITION data alone would leave every joint
displaced relative to the mesh and the character would tear apart the moment it animated. That
refusal is why the three creatures the scale audit flagged were left pinned at the 1.8 m category
default while their unrigged `ready/` meshes were corrected - which leaves the rigged copy, the one
the game actually renders, silently 38% too large.

Blender can do the operation correctly because it owns the whole skeleton/mesh relationship: scaling
the armature and the bound meshes together through a transform apply scales the bone rest positions
and the vertices by the same factor, and the glTF exporter then writes a fresh, self-consistent set
of inverse bind matrices. The scale is baked into the data, so the exported files keep unit node
transforms, which is what the rest of the pipeline expects.

Run inside Blender:
    blender --background --factory-startup --python _blender_rescale_rigged.py -- \
        --rigged <in.glb> --out <out.glb> --target-longest 1.3
"""
import argparse
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--rigged", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--target-longest", type=float, required=True)
    return parser.parse_args(argv)


def world_bounds():
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            for axis in range(3):
                low[axis] = min(low[axis], point[axis])
                high[axis] = max(high[axis], point[axis])
    return low, high


def main():
    args = parse_args()

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=args.rigged)

    low, high = world_bounds()
    dims = high - low
    longest = max(dims.x, dims.y, dims.z)
    if longest <= 0:
        print(f"RESCALE_RESULT {{\"ok\": false, \"error\": \"zero extent\"}}")
        return 1
    factor = args.target_longest / longest

    # Scale the roots only. Children inherit through the hierarchy, and transform_apply then bakes
    # the scale into bone rest positions and mesh vertices alike.
    roots = [o for o in bpy.data.objects if o.parent is None]
    for obj in roots:
        obj.scale = (factor, factor, factor)

    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = roots[0] if roots else None
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    low2, high2 = world_bounds()
    dims2 = high2 - low2
    longest2 = max(dims2.x, dims2.y, dims2.z)

    # Re-seat on the ground, the same convention the unrigged rescale uses, so the creature's feet
    # sit at y=0 rather than wherever the scale left them.
    if abs(low2.z) > 1e-6:
        for obj in bpy.data.objects:
            if obj.parent is None:
                obj.location.z -= low2.z

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=False,
        export_apply=False, export_skins=True)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    bones = sum(len(a.data.bones) for a in armatures)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    weighted = sum(1 for m in meshes for v in m.data.vertices if v.groups)
    print("RESCALE_RESULT " + (
        f'{{"ok": true, "factor": {factor:.5f}, "from_m": {longest:.4f}, '
        f'"to_m": {longest2:.4f}, "armatures": {len(armatures)}, "bones": {bones}, '
        f'"meshes": {len(meshes)}, "vertices_with_weights": {weighted}}}'))
    return 0


if __name__ == "__main__":
    sys.exit(main())
