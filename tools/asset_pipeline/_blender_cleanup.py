"""Blender cleanup pass: normalize a raw generative GLB into Godot-ready assets.

Takes the GLB that ComfyUI exports and produces, with no manual steps:
  - a cleaned base mesh with origin at footprint centre, base on Z=0
  - LOD1/LOD2/LOD3 by progressive decimation
  - a convex-hull collision proxy and a box collision proxy
  - per-asset JSON metadata

Run headless:
  blender --background --factory-startup --python _blender_cleanup.py -- \
      --input raw.glb --outdir C:\\assets\\clean --name greatsword --target-size 1.2
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

# Godot 4 expects metres and Y-up; the glTF exporter handles the axis conversion,
# so this script only has to guarantee real-world scale and a sane origin.
UNIT_HINT_MAP = {
    "weapon": 1.2,
    "shield": 0.8,
    "tool": 0.6,
    "prop": 0.5,
    "icon": 0.3,
    "creature": 1.8,
    "character": 1.8,
    "building": 4.0,
}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--category", default="prop",
                        help=f"One of {sorted(UNIT_HINT_MAP)}; sets the default target size")
    parser.add_argument("--target-size", type=float, default=None,
                        help="Longest dimension in metres. Overrides --category")
    parser.add_argument("--lod-faces", default="8000,2500,600",
                        help="Comma-separated face budgets for LOD1..LODn, highest first")
    parser.add_argument("--hull-faces", type=int, default=64,
                        help="Target face count for the convex hull proxy")
    parser.add_argument("--keep-two-sided", action="store_true",
                        help="Keep double-sided materials instead of forcing single-sided")
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.objects):
        for item in list(block):
            block.remove(item)


def import_glb(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def merge_meshes(meshes):
    """Join every mesh into one object so the asset is a single draw unit."""
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    merged = bpy.context.view_layer.objects.active
    merged.name = "MESH"
    return merged


def world_bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lows = Vector((min(c[i] for c in corners) for i in range(3)))
    highs = Vector((max(c[i] for c in corners) for i in range(3)))
    return lows, highs


def normalize_transform(obj, target_size):
    """Scale so the longest axis equals target_size, centre X/Y, put base on Z=0."""
    lows, highs = world_bounds(obj)
    dims = highs - lows
    longest = max(dims.x, dims.y, dims.z)
    if longest <= 0:
        raise RuntimeError("imported mesh has zero extent")

    scale = target_size / longest
    obj.scale = (obj.scale[0] * scale, obj.scale[1] * scale, obj.scale[2] * scale)
    bpy.context.view_layer.update()

    lows, highs = world_bounds(obj)
    centre_x = (lows.x + highs.x) / 2.0
    centre_y = (lows.y + highs.y) / 2.0
    # Sit the footprint on the ground plane and centre it horizontally.
    obj.location = (
        obj.location.x - centre_x,
        obj.location.y - centre_y,
        obj.location.z - lows.z,
    )
    bpy.context.view_layer.update()

    lows, highs = world_bounds(obj)
    return {
        "dimensions": [round(v, 4) for v in (highs - lows)],
        "scale_applied": round(scale, 6),
        "min": [round(v, 4) for v in lows],
        "max": [round(v, 4) for v in highs],
    }


def apply_transforms(obj):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def force_single_sided():
    for material in bpy.data.materials:
        material.use_backface_culling = True
        if hasattr(material, "use_transparency_overlap"):
            pass


def export_glb(objects, path, draco=False, with_materials=True):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_normals=True,
        export_materials="EXPORT" if with_materials else "NONE",
        export_texcoords=with_materials,
        export_draco_mesh_compression_enable=draco,
    )


def strip_surface_data(obj):
    """Drop materials and UVs so derivative meshes do not re-embed the full
    texture set. Every LOD and proxy would otherwise carry ~14 MB of identical
    PNGs, which dwarfs the geometry it is meant to simplify."""
    obj.data.materials.clear()
    while obj.data.uv_layers:
        obj.data.uv_layers.remove(obj.data.uv_layers[0])


def make_lod(source, face_cap, name, outdir):
    """Duplicate the source and decimate down to face_cap.

    Face budgets cascade: each LOD is capped both by its own budget and by the
    previous LOD's result, so levels stay strictly ordered even when the source
    is already lighter than the requested budget.
    """
    lod = source.copy()
    lod.data = source.data.copy()
    lod.name = name
    bpy.context.collection.objects.link(lod)

    current_faces = len(source.data.polygons)
    target_faces = min(face_cap, current_faces)
    if target_faces >= current_faces:
        # Already at or below budget; emit as-is rather than up-sampling.
        modifier = None
        faces = current_faces
    else:
        modifier = lod.modifiers.new("LODDecimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(target_faces / current_faces, 0.001)
        bpy.context.view_layer.objects.active = lod
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        faces = len(lod.data.polygons)

    strip_surface_data(lod)
    export_glb([lod], os.path.join(outdir, f"{name}.glb"), with_materials=False)
    bpy.data.objects.remove(lod, do_unlink=True)
    return {"faces": faces, "requested_faces": target_faces,
            "ratio": round(target_faces / current_faces, 4) if modifier else 1.0}


def make_collision(source, hull_faces, name, outdir):
    """Convex hull for accurate collision, plus a box for cheap coarse blocking."""
    result = {}

    hull = source.copy()
    hull.data = source.data.copy()
    hull.name = f"{name}_convex"
    bpy.context.collection.objects.link(hull)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    hull.select_set(True)
    bpy.context.view_layer.objects.active = hull

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.convex_hull()
    bpy.ops.object.mode_set(mode="OBJECT")

    faces = len(hull.data.polygons)
    if hull_faces and faces > hull_faces * 4:
        modifier = hull.modifiers.new("HullDecimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = (hull_faces * 4) / faces
        bpy.context.view_layer.objects.active = hull
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        faces = len(hull.data.polygons)

    strip_surface_data(hull)
    export_glb([hull], os.path.join(outdir, f"{name}_collision_hull.glb"), with_materials=False)
    result["convex_hull_faces"] = faces
    bpy.data.objects.remove(hull, do_unlink=True)

    lows, highs = world_bounds(source)
    bpy.ops.mesh.primitive_cube_add(
        size=1.0,
        location=((lows.x + highs.x) / 2, (lows.y + highs.y) / 2, (lows.z + highs.z) / 2),
    )
    box = bpy.context.active_object
    box.name = f"{name}_collision_box"
    box.scale = (
        max(highs.x - lows.x, 1e-4),
        max(highs.y - lows.y, 1e-4),
        max(highs.z - lows.z, 1e-4),
    )
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    strip_surface_data(box)
    export_glb([box], os.path.join(outdir, f"{name}_collision_box.glb"), with_materials=False)
    result["box_dimensions"] = [round(v, 4) for v in (highs - lows)]
    bpy.data.objects.remove(box, do_unlink=True)

    return result


def mesh_stats(obj):
    mesh = obj.data
    return {
        "vertices": len(mesh.vertices),
        "faces": len(mesh.polygons),
        "triangles": sum(max(len(p.vertices) - 2, 0) for p in mesh.polygons),
        "uv_layers": len(mesh.uv_layers),
        "materials": [m.name for m in mesh.materials if m],
        "loose_verts_removed": True,
    }


def main():
    args = parse_args()
    os.makedirs(args.outdir, exist_ok=True)

    reset_scene()
    meshes = import_glb(args.input)
    if not meshes:
        raise RuntimeError(f"no mesh objects found in {args.input}")

    merged = merge_meshes(meshes)
    apply_transforms(merged)

    # Drop stray loose vertices and degenerate faces the generator can leave behind.
    bpy.context.view_layer.objects.active = merged
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.delete_loose()
    bpy.ops.mesh.dissolve_degenerate()
    bpy.ops.object.mode_set(mode="OBJECT")

    if not args.keep_two_sided:
        force_single_sided()

    target = args.target_size if args.target_size else UNIT_HINT_MAP.get(args.category, 0.5)
    transform = normalize_transform(merged, target)
    apply_transforms(merged)

    source_faces = len(merged.data.polygons)
    base_stats = mesh_stats(merged)
    export_glb([merged], os.path.join(args.outdir, f"{args.name}.glb"))

    budgets = [int(b) for b in args.lod_faces.split(",") if b.strip()]
    lods = {}
    previous_faces = source_faces
    for index, budget in enumerate(budgets, start=1):
        lod_name = f"{args.name}_lod{index}"
        cap = min(budget, previous_faces)
        lods[lod_name] = make_lod(merged, cap, lod_name, args.outdir)
        previous_faces = lods[lod_name]["faces"]

    # LODs must shrink monotonically or the chain is useless in engine.
    ordered = [entry["faces"] for entry in lods.values()]
    if ordered != sorted(ordered, reverse=True):
        raise RuntimeError(f"LOD face counts are not strictly descending: {ordered}")

    collision = make_collision(merged, args.hull_faces, args.name, args.outdir)

    report = {
        "source": args.input,
        "name": args.name,
        "category": args.category,
        "target_size_m": target,
        "transform": transform,
        "source_faces": source_faces,
        "base": base_stats,
        "lods": lods,
        "collision": collision,
        "outputs": sorted(os.listdir(args.outdir)),
    }
    report_path = os.path.join(args.outdir, f"{args.name}_meta.json")
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    print("CLEANUP_RESULT " + json.dumps({
        "name": args.name,
        "base_faces": base_stats["faces"],
        "base_tris": base_stats["triangles"],
        "dimensions": transform["dimensions"],
        "lods": lods,
        "collision": collision,
        "meta": report_path,
    }))


if __name__ == "__main__":
    main()
