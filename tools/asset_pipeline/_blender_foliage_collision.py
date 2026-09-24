"""Rebuild collision for foliage from the part the player actually collides with.

Every foliage asset currently carries collision built from its whole bounding volume, which is
wrong in a way that breaks gameplay rather than merely costing memory: a 14 m oak has a
12.67 x 14.00 x 13.74 m convex hull, so the player is stopped several metres from the trunk by a
block of empty air. Ground cover has collision it should not have at all.

The policy, following the collision rule in the modular asset standard ("collision is for things
the player or physics touches as world geometry"):

  tree         trunk only   - the canopy is not solid, the trunk is
  bush         lower mass   - a bramble blocks, but only where it is dense
  ground       none         - a fern or a patch of heather is walked through

Trunk and lower-mass proxies are convex hulls of the vertices BELOW a height threshold, so they
taper with the trunk instead of boxing the whole plant. Ground cover has its collision files
removed, and the decision is recorded in the asset's metadata so the pack verifier can honour it
rather than demanding files that should not exist.

Usage:
    python _blender_foliage_collision.py --input <base.glb> --outdir <dir> --asset-id <id> \
        --mode trunk --height-fraction 0.25
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from _blender_cleanup import reset_scene  # noqa: E402


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--asset-id", required=True)
    parser.add_argument("--mode", required=True, choices=("trunk", "lower", "none"))
    parser.add_argument("--height-fraction", type=float, default=0.25,
                        help="Fraction of total height treated as the collision-bearing part")
    parser.add_argument("--hull-faces", type=int, default=64)
    parser.add_argument("--core-radius", type=float, default=2.0,
                        help="Multiple of the median radius kept as the trunk core")
    return parser.parse_args(argv)


def import_glb(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError(f"no mesh in {path}")
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def export_glb(objects, path):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True,
        export_apply=True, export_yup=True, export_normals=True,
        export_materials="NONE", export_texcoords=False)


def lower_vertices(obj, height_fraction):
    """World-space coordinates of vertices in the bottom slice of the object.

    Blender is Z-up, so "lower" is a Z threshold. Grows the slice if the requested band is too
    sparse to describe anything: a birch tree has only six vertices in its lowest 6%, and falling
    back to the whole mesh there produced a collider the size of the entire canopy.
    """
    matrix = obj.matrix_world
    coords = [matrix @ v.co for v in obj.data.vertices]
    if not coords:
        return [], height_fraction
    zs = [c.z for c in coords]
    low, high = min(zs), max(zs)
    fraction = height_fraction
    chosen = []
    while fraction <= 0.6:
        cutoff = low + (high - low) * fraction
        chosen = [c for c in coords if c.z <= cutoff]
        if len(chosen) >= 24:
            break
        fraction *= 1.6
    return chosen, fraction


def trim_to_core(points, radius_factor=2.0, floor_m=0.06):
    """Keep the vertices clustered around the vertical axis, discarding what hangs far from it.

    A willow's branches droop to the ground, so a plain bounding box of its lowest slice is 8.2 m
    wide and would stop the player metres from the trunk. Keeping everything within a multiple of
    the MEDIAN distance from the slice's centroid adapts to however wide that particular trunk is.

    A fixed percentile band was tried first and failed on the birch: its lowest slice is sparse, so
    a 20th-to-80th band collapsed the trunk to an 8 cm twig. Median radius has no such failure mode
    because it is measured from the geometry rather than from the vertex count.
    """
    if len(points) < 8:
        return points
    xs = sorted(p.x for p in points)
    ys = sorted(p.y for p in points)
    centre_x = xs[len(xs) // 2]
    centre_y = ys[len(ys) // 2]
    distances = sorted(math.hypot(p.x - centre_x, p.y - centre_y) for p in points)
    median = distances[len(distances) // 2]
    limit = max(median * radius_factor, floor_m)
    core = [p for p in points if math.hypot(p.x - centre_x, p.y - centre_y) <= limit]
    return core if len(core) >= 12 else points


def hull_object(points, name, face_cap):
    mesh = bpy.data.meshes.new(f"{name}_mesh")
    bm = bmesh.new()
    for point in points:
        bm.verts.new(point)
    bm.verts.ensure_lookup_table()
    result = bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)
    # convex_hull leaves interior geometry behind; drop it or the proxy is not a hull.
    interior = [g for g in result.get("geom_interior", []) if isinstance(g, bmesh.types.BMVert)]
    if interior:
        bmesh.ops.delete(bm, geom=interior, context="VERTS")
    unused = [v for v in bm.verts if not v.link_faces]
    if unused:
        bmesh.ops.delete(bm, geom=unused, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    if face_cap and len(mesh.polygons) > face_cap:
        for other in bpy.context.scene.objects:
            other.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new("Decimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(face_cap / len(mesh.polygons), 0.001)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def world_bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lows = Vector((min(c[i] for c in corners) for i in range(3)))
    highs = Vector((max(c[i] for c in corners) for i in range(3)))
    return lows, highs


def main():
    args = parse_args()
    reset_scene()
    source = import_glb(args.input)

    if args.mode == "none":
        # Nothing to emit. The caller removes any existing collision files and records the policy.
        print("FOLIAGE_COLLISION " + json.dumps({
            "asset_id": args.asset_id, "mode": "none", "files": [],
            "note": "ground cover has no collision"}))
        return 0

    points, used_fraction = lower_vertices(source, args.height_fraction)
    if args.mode == "trunk":
        points = trim_to_core(points, args.core_radius)
    if len(points) < 8:
        # Should be unreachable after the slice growth and trimming. If it ever fires, the proxy
        # is the whole plant, so say so rather than quietly shipping a canopy-sized collider.
        print("  WARNING: too few vertices for a hull; falling back to the whole mesh")
        points = [source.matrix_world @ v.co for v in source.data.vertices]

    # Box proxy: the axis-aligned bounds of the collision-bearing slice.
    lows = Vector((min(p[i] for p in points) for i in range(3)))
    highs = Vector((max(p[i] for p in points) for i in range(3)))
    centre = (lows + highs) / 2.0
    size = highs - lows

    box_mesh = bpy.data.meshes.new(f"{args.asset_id}_collision_box_mesh")
    box = bpy.data.objects.new(f"{args.asset_id}_collision_box", box_mesh)
    bpy.context.collection.objects.link(box)
    corners = [(centre.x + sx * size.x / 2, centre.y + sy * size.y / 2, centre.z + sz * size.z / 2)
               for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6),
             (0, 2, 6, 4), (1, 5, 7, 3)]
    box_mesh.from_pydata(corners, [], faces)
    box_mesh.update()

    hull = hull_object(points, f"{args.asset_id}_collision_hull", args.hull_faces)

    os.makedirs(args.outdir, exist_ok=True)
    hull_path = os.path.join(args.outdir, f"{args.asset_id}_collision_hull.glb")
    box_path = os.path.join(args.outdir, f"{args.asset_id}_collision_box.glb")
    box.name = f"{args.asset_id}_collision_box"
    box.data.name = f"{args.asset_id}_collision_box_mesh"
    export_glb([box], box_path)
    export_glb([hull], hull_path)

    print("FOLIAGE_COLLISION " + json.dumps({
        "asset_id": args.asset_id,
        "mode": args.mode,
        "height_fraction": args.height_fraction,
        "height_fraction_used": round(used_fraction, 4),
        "source_vertices": len(source.data.vertices),
        "collision_vertices": len(points),
        "hull_faces": len(hull.data.polygons),
        "box_dimensions_m": [round(v, 4) for v in size],
        "box_centre_m": [round(v, 4) for v in centre],
        "files": [os.path.basename(hull_path), os.path.basename(box_path)],
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
