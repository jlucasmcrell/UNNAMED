"""Attach modular interface sockets to a normalised asset and export a production GLB.

This is the Wave 0 capability that does not exist yet. Trellis output has no sockets, every
exported mesh is called Mesh_0, and no Blender source is retained, so a socket change would
mean re-running generation. This script closes all three gaps:

  * authors each socket as a named Blender empty positioned and oriented by a full orthonormal
    basis, not just a longitudinal axis
  * renames the object, mesh datablock and material to the asset id
  * saves the canonical .blend source
  * exports the production GLB with sockets as named nodes

Socket definitions come from a JSON file, one per asset:

{
  "asset_id": "weaponcomp_mace_head_flanged_a",
  "modular_interface_version": "0.1",
  "nominal_size_m": [0.09, 0.11, 0.09],
  "sockets": {
    "SOCK_head": {
      "position":  [0.0, -0.055, 0.0],
      "primary":   [0.0, -1.0, 0.0],
      "secondary": [0.0, 0.0, 1.0],
      "roll": 0.0, "depth": 0.02, "envelope": 0.048,
      "family": "standard", "role": "head", "mate": "antiparallel"
    }
  }
}

Coordinates are authored in the **export** frame (Y-up, base at ground, footprint centred),
which is what an engine sees. The Blender conversion is handled here so the same numbers
mean the same thing in the socket file, the GLB and Godot.

Run inside Blender:
  blender --background --factory-startup --python _blender_sockets.py -- ^
      --input ready\\name\\name.glb --sockets sockets\\name.json ^
      --blend-source blender_src\\name.blend --out ready\\name\\name.glb
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="Normalised GLB to attach sockets to")
    parser.add_argument("--sockets", required=True, help="Socket definition JSON")
    parser.add_argument("--out", required=True, help="Production GLB to write")
    parser.add_argument("--blend-source", default=None,
                        help="Canonical .blend to save; required for lineage")
    parser.add_argument("--asset-id", default=None, help="Override the asset id")
    parser.add_argument("--resize-to-nominal", action="store_true",
                        help="Scale to nominal_size_m instead of trusting the input scale")
    # LOD and collision live here rather than in a second pass, because the modular chain and
    # the production cleanup chain were two half-pipelines: cleanup produced LODs and
    # collision but no sockets, and socket authoring produced sockets but no LODs, so neither
    # directory held a complete modular asset. One Blender session now emits the whole set.
    parser.add_argument("--lod-faces", default=None,
                        help="Comma-separated face budgets, e.g. 8000,2500,600. "
                             "LODs carry no textures, matching the existing library.")
    parser.add_argument("--collision-faces", type=int, default=None,
                        help="Target faces for the convex hull proxy")
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials, bpy.data.armatures):
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


def merge(meshes):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c[i] for c in corners) for i in range(3)))
    high = Vector((max(c[i] for c in corners) for i in range(3)))
    return low, high


def resize_to_nominal(obj, nominal):
    """Scale so the export-frame extent matches the declared nominal size exactly.

    Modular parts must be sized by specification, not by a category default: a mace head is
    0.11 m because it seats in a 0.11 m socket. Longest-axis normalisation is deliberately
    NOT used here.
    """
    low, high = bounds(obj)
    extent = high - low
    # nominal is given in export (Y-up) order; the imported GLB is already in Blender Z-up,
    # so map export (x, y, z) -> blender (x, -z, y) for the comparison.
    blender_nominal = Vector((nominal[0], nominal[2], nominal[1]))
    factors = []
    for axis in range(3):
        target = blender_nominal[axis]
        current = extent[axis]
        factors.append(target / current if current > 1e-6 else 1.0)
    # A single uniform factor preserves proportions and refuses to distort the part.
    scale = sum(factors) / 3.0
    obj.scale = (obj.scale[0] * scale, obj.scale[1] * scale, obj.scale[2] * scale)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Re-seat: base on the ground plane, footprint centred, as the base standard requires.
    low, high = bounds(obj)
    centre = (low + high) / 2
    obj.location = (obj.location.x - centre.x, obj.location.y - centre.y, obj.location.z - low.z)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return scale


def socket_matrix(spec):
    """Build an orthonormal basis from a socket definition.

    `primary` is the mating axis and `secondary` fixes roll. They are made orthogonal here so
    a slightly sloppy definition still yields a valid rotation rather than a sheared one.
    """
    primary = Vector(spec["primary"]).normalized()
    secondary = Vector(spec.get("secondary", [0.0, 0.0, 1.0]))
    if abs(primary.dot(secondary.normalized())) > 0.999:
        # Degenerate pair; pick any axis not parallel to primary.
        secondary = Vector((1.0, 0.0, 0.0))
        if abs(primary.dot(secondary)) > 0.9:
            secondary = Vector((0.0, 1.0, 0.0))
    cross = primary.cross(secondary).normalized()
    secondary = cross.cross(primary).normalized()
    if spec.get("roll"):
        angle = math.radians(float(spec["roll"]))
        rotation = Matrix.Rotation(angle, 3, primary)
        secondary = (rotation @ secondary).normalized()
        cross = primary.cross(secondary).normalized()
    # Columns are the basis vectors, so local +X -> primary, +Y -> secondary, +Z -> cross.
    return Matrix((
        (primary.x, secondary.x, cross.x),
        (primary.y, secondary.y, cross.y),
        (primary.z, secondary.z, cross.z),
    ))


def blender_position(export_position):
    """Convert an export-frame (Y-up) position to Blender (Z-up)."""
    x, y, z = export_position
    return Vector((x, -z, y))


def author_socket(name, spec):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "ARROWS"
    empty.empty_display_size = 0.02
    bpy.context.collection.objects.link(empty)
    empty.location = blender_position(spec["position"])
    # Build the basis in export frame, then express it in Blender frame.
    export_basis = socket_matrix(spec)
    blender_basis = export_to_blender_matrix(export_basis)
    empty.rotation_mode = "QUATERNION"
    empty.rotation_quaternion = blender_basis.to_quaternion()
    empty["socket_role"] = spec.get("role", "")
    empty["socket_family"] = spec.get("family", "")
    # A planar interface legitimately declares no envelope, so None must read as absent
    # rather than as a bad number. Blender stores it as 0.0, meaning "no diameter".
    empty["socket_depth"] = float(spec.get("depth", 0.02) or 0.0)
    empty["socket_envelope"] = float(spec.get("envelope", 0.0) or 0.0)
    empty["socket_mate"] = spec.get("mate", "antiparallel")
    return empty


def export_to_blender_matrix(export_basis):
    """Convert a rotation expressed in export axes into Blender axes.

    Export maps blender (x, y, z) -> export (x, z, -y), so the conversion matrix C has
    columns [(1,0,0), (0,0,1), (0,-1,0)] - note this is C, whose columns are the images of
    the Blender basis vectors, NOT the earlier transpose.

    A rotation R_e authored in the export frame becomes R_b = C^-1 @ R_e @ C in Blender.
    The order matters and is easy to get wrong: the first version of this function used
    C @ R_e @ C^-1, which left positions correct and silently INVERTED the basis, so every
    socket pointed backwards. `_verify_sockets.py` caught it because it checks the full
    basis rather than trusting the exported node to be right.
    """
    convert = Matrix((
        (1.0, 0.0, 0.0),
        (0.0, 0.0, 1.0),
        (0.0, -1.0, 0.0),
    ))
    return convert.inverted() @ export_basis @ convert


def rename_asset(obj, asset_id, definition):
    """Give the object, mesh and material stable asset-id names.

    The library currently exports Mesh_0 and Material_0 for every asset, which makes an
    imported scene unreadable and blocks any tooling that looks assets up by name.
    """
    obj.name = asset_id
    obj.data.name = f"{asset_id}_mesh"
    for index, slot in enumerate(obj.material_slots):
        material = slot.material
        if material is None:
            continue
        material.name = (f"MAT_{asset_id}" if index == 0
                         else f"MAT_{asset_id}_{index}")
        if material.use_nodes:
            for node in material.node_tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    node.name = f"BSDF_{asset_id}"
    return obj


def strip_surface_data(obj):
    """Drop materials and UVs before exporting a LOD.

    A decimated proxy keeps the silhouette and discards the surface detail that makes the
    textures large, so carrying a 2K PBR set on a 600-face mesh would be pure waste.
    """
    obj.data.materials.clear()
    while obj.data.uv_layers:
        obj.data.uv_layers.remove(obj.data.uv_layers[0])


def make_lod(source, face_cap, name, out_path, previous_faces=None):
    """Duplicate the source and decimate to face_cap, emitting a texture-free GLB.

    Budgets cascade: each LOD is capped by its own budget AND by the previous LOD's actual
    result. Without that, a cap the topology cannot reach (asking 600 faces of a mesh that
    can only collapse to 2,382) leaves two adjacent LODs at the same size, which defeats the
    point of having them.
    """
    lod = source.copy()
    lod.data = source.data.copy()
    lod.name = name
    lod.data.name = f"{name}_mesh"
    bpy.context.collection.objects.link(lod)

    current = len(source.data.polygons)
    target = min(face_cap, current)
    if previous_faces is not None:
        # Stay strictly below the previous level so the chain always descends.
        target = min(target, max(previous_faces - 1, int(previous_faces * 0.6)))
    target = max(target, 4)

    if target >= current:
        faces = current
        ratio = 1.0
    else:
        modifier = lod.modifiers.new("LODDecimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(target / current, 0.001)
        bpy.context.view_layer.objects.active = lod
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        faces = len(lod.data.polygons)
        ratio = round(target / current, 4)

    strip_surface_data(lod)
    export_glb([lod], out_path, name, with_materials=False)
    bpy.data.objects.remove(lod, do_unlink=True)
    return {"faces": faces, "requested_faces": target, "ratio": ratio}


def make_collision(source, hull_faces, name, out_dir):
    """Emit a real convex hull proxy and a cheap box proxy.

    The hull must be an actual hull, not a decimated copy: a decimated mesh keeps concavities,
    so it is a worse collision proxy than the full mesh and costs the same to test against.
    `bmesh.ops.convex_hull` produces the enclosing shape, then it is decimated to the budget.
    """
    result = {}

    hull = source.copy()
    hull.data = source.data.copy()
    hull.name = f"{name}_collision_hull"
    hull.data.name = f"{name}_collision_hull_mesh"
    bpy.context.collection.objects.link(hull)
    strip_surface_data(hull)

    mesh = hull.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)
    # convex_hull leaves the interior geometry behind; clear it so the proxy is hollow.
    interior = [v for v in bm.verts if not v.link_faces]
    if interior:
        bmesh.ops.delete(bm, geom=interior, context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    current = len(hull.data.polygons)
    if hull_faces and current > hull_faces:
        # A hull is convex by construction, so PLANAR dissolve collapses coplanar faces first
        # and cheaply, then COLLAPSE handles whatever is left. A single COLLAPSE pass barely
        # dents a hull because almost every face is already minimal.
        bpy.context.view_layer.objects.active = hull
        planar = hull.modifiers.new("HullPlanar", "DECIMATE")
        planar.decimate_type = "DISSOLVE"
        planar.angle_limit = 0.0873   # 5 degrees
        bpy.ops.object.modifier_apply(modifier=planar.name)
        if len(hull.data.polygons) > hull_faces:
            collapse = hull.modifiers.new("HullCollapse", "DECIMATE")
            collapse.decimate_type = "COLLAPSE"
            collapse.ratio = max(hull_faces / len(hull.data.polygons), 0.001)
            bpy.ops.object.modifier_apply(modifier=collapse.name)
    result["convex_hull_faces"] = len(hull.data.polygons)
    export_glb([hull], os.path.join(out_dir, f"{name}_collision_hull.glb"),
               f"{name}_collision_hull", with_materials=False)
    bpy.data.objects.remove(hull, do_unlink=True)

    low, high = bounds(source)
    box_mesh = bpy.data.meshes.new(f"{name}_collision_box_mesh")
    box = bpy.data.objects.new(f"{name}_collision_box", box_mesh)
    bpy.context.collection.objects.link(box)
    corners = [
        (low.x, low.y, low.z), (high.x, low.y, low.z),
        (high.x, high.y, low.z), (low.x, high.y, low.z),
        (low.x, low.y, high.z), (high.x, low.y, high.z),
        (high.x, high.y, high.z), (low.x, high.y, high.z),
    ]
    faces = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1),
             (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    box_mesh.from_pydata(corners, [], faces)
    box_mesh.update()
    result["box_dimensions"] = [round(high.x - low.x, 4), round(high.y - low.y, 4),
                                round(high.z - low.z, 4)]
    export_glb([box], os.path.join(out_dir, f"{name}_collision_box.glb"),
               f"{name}_collision_box", with_materials=False)
    bpy.data.objects.remove(box, do_unlink=True)

    return result


def export_glb(objects, path, asset_id, with_materials=True):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,
        export_yup=True,
        export_normals=True,
        export_materials="EXPORT" if with_materials else "NONE",
        export_texcoords=True,
        export_extras=True,   # socket metadata travels with the empty
    )


def main():
    args = parse_args()
    with open(args.sockets, encoding="utf-8") as handle:
        definition = json.load(handle)

    asset_id = args.asset_id or definition.get("asset_id")
    if not asset_id:
        raise SystemExit("socket file must declare asset_id")
    interface_version = definition.get("modular_interface_version")
    if not interface_version:
        raise SystemExit("socket file must declare modular_interface_version")

    reset_scene()
    obj = merge(import_glb(args.input))

    resize_scale = None
    if args.resize_to_nominal and definition.get("nominal_size_m"):
        resize_scale = resize_to_nominal(obj, definition["nominal_size_m"])

    rename_asset(obj, asset_id, definition)

    sockets = []
    for name, spec in definition.get("sockets", {}).items():
        sockets.append(author_socket(name, spec))

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    # Sockets are authored before LODs so the LOD copies inherit nothing extra; the base GLB
    # is what carries them.
    export_glb([obj] + sockets, args.out, asset_id)

    out_dir = os.path.dirname(args.out)
    lods = {}
    if args.lod_faces:
        previous = None
        for index, budget in enumerate(
                int(v) for v in args.lod_faces.split(",") if v.strip()):
            lod_name = f"{asset_id}_lod{index + 1}"
            entry = make_lod(obj, budget, lod_name,
                             os.path.join(out_dir, f"{lod_name}.glb"),
                             previous_faces=previous)
            lods[lod_name] = entry
            previous = entry["faces"]

    collision = None
    if args.collision_faces:
        collision = make_collision(obj, args.collision_faces, asset_id, out_dir)

    blend_path = args.blend_source
    if blend_path:
        os.makedirs(os.path.dirname(blend_path), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    low, high = bounds(obj)
    report = {
        "asset_id": asset_id,
        "modular_interface_version": interface_version,
        "sockets": sorted(definition.get("sockets", {}).keys()),
        "socket_count": len(sockets),
        "resize_scale": round(resize_scale, 6) if resize_scale else None,
        "out": args.out,
        "blend_source": blend_path,
        "dimensions_m_export_frame": [
            round(high.x - low.x, 4), round(high.z - low.z, 4), round(high.y - low.y, 4)
        ],
        "base": {
            "vertices": len(obj.data.vertices),
            "faces": len(obj.data.polygons),
            "triangles": len(obj.data.polygons),
            "materials": [m.name for m in obj.data.materials],
        },
        "lods": lods,
        "collision": collision,
    }
    report_path = os.path.splitext(args.out)[0] + "_sockets.json"
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)

    # The per-asset meta.json is mandatory, and `_verify_pack.py` treats a missing one as a
    # failure rather than a warning. The cleanup chain wrote it; this chain did not, so every
    # unified asset arrived without normalisation provenance and the pack failed verification.
    meta = {
        "source": args.input,
        "name": asset_id,
        "category": definition.get("category"),
        # A modular component's declared nominal size IS its specification, so the longest
        # measured axis is the target rather than a value scaled toward. Recording it here
        # keeps `_verify_pack.py`'s normalisation check meaningful: it compares the measured
        # longest axis against this, so a silently wrong scale still fails.
        "target_size_m": round(max(high.x - low.x, high.y - low.y, high.z - low.z), 4),
        "modular_interface_version": interface_version,
        "fit_family": definition.get("fit_family"),
        "socket_family": sorted({spec.get("family", "") for spec in
                                 definition.get("sockets", {}).values()}),
        "sockets": sorted(definition.get("sockets", {}).keys()),
        "transform": {
            "dimensions": [round(high.x - low.x, 4), round(high.y - low.y, 4),
                           round(high.z - low.z, 4)],
            "scale_applied": round(resize_scale, 6) if resize_scale else 1.0,
        },
        "source_faces": len(obj.data.polygons),
        "base": report["base"],
        "lods": lods,
        "collision": collision or {},
        "outputs": sorted(os.path.basename(p) for p in
                          [args.out] + [os.path.join(out_dir, f"{n}.glb") for n in lods]
                          + ([os.path.join(out_dir, f"{asset_id}_collision_hull.glb"),
                              os.path.join(out_dir, f"{asset_id}_collision_box.glb")]
                             if collision else [])),
    }
    meta_path = os.path.join(out_dir, f"{asset_id}_meta.json")
    with open(meta_path, "w", encoding="utf-8") as handle:
        json.dump(meta, handle, indent=2)

    print("SOCKET_RESULT " + json.dumps(report))


if __name__ == "__main__":
    main()
