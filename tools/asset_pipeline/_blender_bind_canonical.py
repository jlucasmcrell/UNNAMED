"""Bind a generated character mesh to the canonical fit-family skeleton.

The gap this closes: `_blender_rig.py` builds its own ad-hoc armature (a 20-bone humanoid), while
`_blender_canonical_body.py` builds the contracted reference body and its 52-bone skeleton. A
player character has to end up on the SECOND one, because that is what armour fit, the animation
clips and the weapon grip sockets are all authored against. Nothing bound a mesh to the canonical
skeleton, so this does.

Scale is semantic, per the pipeline rule both `CANONICAL_BODY_AND_SKELETON.md` and the animation
document state: a character is scaled so its HEIGHT matches the fit family, never by longest axis.
A 1.80 m Veth stays 1.80 m whatever its arm span happens to be.

The helper-weight purge is repeated here rather than only in the canonical body builder, because
automatic weighting gave a pole target real weight on `tall_narrow` once already. An IK helper
that deforms the mesh is a silent, hard-to-find bug.

Usage:
    blender --background --python _blender_bind_canonical.py -- \
        --input mesh.glb --asset-id race2_veth_representative \
        --fit-family standard_humanoid --out out.glb
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from _blender_canonical_body import (  # noqa: E402
    ATTACHMENTS, BONES, FINGERS, FIT_FAMILIES, IK_BONES, SKELETON_VERSION, THUMB,
    build_armature, build_body, reset_scene, skeleton_bones,
)

FINGER_NAMES = {f"{finger}_0{n}" for finger, _y, _f in FINGERS + [THUMB] for n in (1, 2)}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="Mesh GLB to bind")
    parser.add_argument("--asset-id", required=True)
    parser.add_argument("--fit-family", required=True, choices=sorted(FIT_FAMILIES))
    parser.add_argument("--out", required=True)
    parser.add_argument("--report", default=None, help="Skeleton contract JSON to write")
    parser.add_argument("--height-override", type=float, default=None,
                        help="Metres. Defaults to the fit family's contracted height.")
    parser.add_argument("--segments", type=int, default=16,
                        help="Radial resolution of the canonical weight-source body")
    return parser.parse_args(argv)


def import_mesh(path):
    """Import a GLB and return only the meshes it brought in.

    Taking every mesh in the scene also picks up the canonical weight-source body, and the join
    then fuses the character into the reference figure. Snapshot the scene and diff it.
    """
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and o not in before]
    if not meshes:
        raise RuntimeError(f"no new mesh imported from {path}")
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def world_bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lows = Vector((min(c[i] for c in corners) for i in range(3)))
    highs = Vector((max(c[i] for c in corners) for i in range(3)))
    return lows, highs


def fit_to_canonical(obj, target_height):
    """Scale by HEIGHT (Blender Z) and seat the feet on Z=0 at the footprint centre.

    Height is the semantic dimension for a character. Longest-axis scaling is what produced a
    library where every creature is exactly 1.8 m and a tree is 0.5 m.
    """
    lows, highs = world_bounds(obj)
    dims = highs - lows
    if dims.z <= 0:
        raise RuntimeError("imported mesh has zero height")
    scale = target_height / dims.z
    obj.scale = (obj.scale[0] * scale, obj.scale[1] * scale, obj.scale[2] * scale)
    bpy.context.view_layer.update()

    lows, highs = world_bounds(obj)
    centre_x = (lows.x + highs.x) / 2.0
    centre_y = (lows.y + highs.y) / 2.0
    # Blender is Z-up: the ground plane is Z=0 and forward is -Y.
    obj.location = (obj.location.x - centre_x,
                    obj.location.y - centre_y,
                    obj.location.z - lows.z)
    bpy.context.view_layer.update()

    for other in bpy.context.scene.objects:
        other.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    lows, highs = world_bounds(obj)
    return [round(v, 4) for v in (highs - lows)], round(scale, 6)


def skin_source_body(rig, name, segments):
    """Build the canonical body and skin it, to act as the weight source.

    Automatic bone-heat weighting fails on a generated character: the canonical armature is in the
    contracted A-pose with the arms 45 degrees out, while a generated mesh stands with its arms at
    its sides, so the arm bones sit outside the geometry and Blender assigns nothing at all. It
    still reports success, which is how a bind ends up with zero weighted bones.

    The canonical body exists precisely to solve this. It is in the correct bind pose and skins
    correctly, so its weights are transferred to the generated mesh by nearest surface point.
    """
    bpy.ops.object.mode_set(mode="OBJECT")
    body = build_body(FIT_FAMILIES[name], f"{name}_weightsource", segments)
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    for other in bpy.context.scene.objects:
        other.select_set(False)
    return body


def transfer_weights(source, target):
    """Copy vertex weights from the canonical body to the mesh by nearest source vertex.

    Done explicitly with a KD-tree rather than through the data-transfer operator so the mapping
    is inspectable and its failures are visible: an operator that silently transfers nothing looks
    exactly like a successful bind.
    """
    from mathutils import kdtree

    for group in list(target.vertex_groups):
        target.vertex_groups.remove(group)

    # Every group the source has, so the target ends up with the same vocabulary.
    groups = {}
    for group in source.vertex_groups:
        groups[group.name] = target.vertex_groups.new(name=group.name)

    source_matrix = source.matrix_world
    tree = kdtree.KDTree(len(source.data.vertices))
    source_weights = []
    for index, vertex in enumerate(source.data.vertices):
        tree.insert(source_matrix @ vertex.co, index)
        source_weights.append([(m.group, m.weight) for m in vertex.groups if m.weight > 1e-5])
    tree.balance()

    target_matrix = target.matrix_world
    assigned = 0
    for vertex in target.data.vertices:
        _co, nearest, _distance = tree.find(target_matrix @ vertex.co)
        for group_index, weight in source_weights[nearest]:
            name = source.vertex_groups[group_index].name
            groups[name].add([vertex.index], weight, "REPLACE")
        if source_weights[nearest]:
            assigned += 1
    return assigned


def attach_to_armature(mesh, rig):
    """Parent the mesh to the rig with an Armature modifier, without re-weighting it."""
    mesh.parent = rig
    mesh.matrix_parent_inverse = rig.matrix_world.inverted()
    modifier = mesh.modifiers.new("Armature", "ARMATURE")
    modifier.object = rig
    modifier.use_vertex_groups = True


def purge_helper_weights(obj):
    helpers = {name for name, _parent in IK_BONES}
    removed = []
    for group in list(obj.vertex_groups):
        if group.name in helpers:
            obj.vertex_groups.remove(group)
            removed.append(group.name)
    return removed


def main():
    args = parse_args()
    f = FIT_FAMILIES[args.fit_family]
    target_height = args.height_override or f["height"]

    reset_scene()
    rig, positions = build_armature(f, args.asset_id)

    # The canonical body is the weight source. It is in the contracted bind pose and skins
    # correctly, which a generated mesh in a natural stance does not.
    source = skin_source_body(rig, args.fit_family, args.segments)

    mesh = import_mesh(args.input)
    dims, scale = fit_to_canonical(mesh, target_height)

    mesh.name = args.asset_id
    mesh.data.name = f"{args.asset_id}_mesh"
    for index, material in enumerate(mesh.data.materials):
        if material is not None:
            material.name = f"MAT_{args.asset_id}" if index == 0 else f"MAT_{args.asset_id}_{index}"

    assigned = transfer_weights(source, mesh)
    attach_to_armature(mesh, rig)
    bound = assigned > 0

    purged = purge_helper_weights(mesh)
    bpy.data.objects.remove(source, do_unlink=True)

    # Report which bones actually received weight, so a bind that silently misses the fingers or
    # the head is visible here rather than in the engine.
    weighted = set()
    for vertex in mesh.data.vertices:
        for membership in vertex.groups:
            if membership.weight > 1e-5:
                weighted.add(mesh.vertex_groups[membership.group].name)
    bones = skeleton_bones()
    deform = {b for b, _p, d in bones if d}
    unweighted_deform = sorted(deform - weighted)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    sockets = [bpy.data.objects[s] for s, _p in ATTACHMENTS if s in bpy.data.objects]
    keep = {mesh, rig, *sockets}
    for other in bpy.context.scene.objects:
        other.select_set(other in keep)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=True,
        export_skins=True, export_extras=True)

    def export_frame(p):
        x, y, z = p
        return [round(x, 5), round(z, 5), round(-y, 5)]

    core_names = {b for b, _p in BONES}
    contract = {
        "skeleton_version": SKELETON_VERSION,
        "asset_id": args.asset_id,
        "fit_family": args.fit_family,
        "units": "metres",
        "export_frame": "Y-up, base on ground plane, footprint centred",
        "forward_axis": "-Z",
        "up_axis": "+Y",
        "reference_pose": "A-pose, arms 45 degrees down",
        "height_m": round(target_height, 4),
        "measured_dimensions_m": dims,
        "bone_count": len(bones),
        "core_bone_count": len(core_names),
        "deform_bone_count": len(deform),
        "bones": [
            {"name": b, "parent": p, "head": export_frame(positions[b]), "deform": d,
             "role": ("core" if b in core_names
                      else "finger" if b.rsplit("_", 1)[0] in FINGER_NAMES
                      else "ik")}
            for b, p, d in bones
        ],
        "attachments": [
            {"name": s, "parent_bone": p, "position": export_frame(positions[s]),
             "role": "equipment_attach"}
            for s, p in ATTACHMENTS
        ],
        "landmarks_m": {k: v for k, v in f.items()},
        "skinned": bound,
    }
    report_path = args.report or os.path.splitext(args.out)[0] + "_skeleton.json"
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(contract, handle, indent=2)

    print("BIND_RESULT " + json.dumps({
        "asset_id": args.asset_id,
        "fit_family": args.fit_family,
        "skeleton_version": SKELETON_VERSION,
        "out": args.out,
        "height_m": round(target_height, 4),
        "dimensions_m": dims,
        "scale_applied": scale,
        "bound": bound,
        "bones": len(bones),
        "weighted_bones": len(weighted),
        "unweighted_deform_bones": unweighted_deform,
        "helper_groups_purged": purged,
        "vertices": len(mesh.data.vertices),
        "faces": len(mesh.data.polygons),
        "skeleton": report_path,
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
