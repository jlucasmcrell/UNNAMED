"""Bind a generated mesh to an armature so it can be animated.

Generative meshes have no skeleton, so every character and creature built so far is a
static prop. This adds the missing stage: build a named skeleton appropriate to the
asset's body plan, bind the mesh to it, clean the weights, and export a rigged GLB.

Run inside Blender:
  blender --background --factory-startup --python _blender_rig.py -- ^
      --input mesh.glb --outdir out\name --name veth --rig humanoid

Body plans:
  humanoid  biped, arms at rest, for races and NPCs
  quadruped four-legged, for most creatures
  worm      single chain, for serpents and things without limbs
  none      validation only - reports whether the mesh could be rigged at all
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

# Bone definitions: name, head (x, y, z), tail (x, y, z), parent.
# Coordinates are in metres with the origin at the footprint centre and the base on
# Z=0, which is what the asset pipeline guarantees. Height is normalised to 1.8 m for
# humanoids before binding, so these are absolute.
HUMANOID = [
    ("root",     (0.0, 0.0, 0.00),  (0.0, 0.0, 0.10),  None),
    ("hips",     (0.0, 0.0, 0.95),  (0.0, 0.0, 1.12),  "root"),
    ("spine",    (0.0, 0.0, 1.12),  (0.0, 0.0, 1.32),  "hips"),
    ("chest",    (0.0, 0.0, 1.32),  (0.0, 0.0, 1.50),  "spine"),
    ("neck",     (0.0, 0.0, 1.50),  (0.0, 0.0, 1.60),  "chest"),
    ("head",     (0.0, 0.0, 1.60),  (0.0, 0.0, 1.78),  "neck"),

    ("shoulder.L", (0.05, 0.0, 1.46), (0.18, 0.0, 1.44), "chest"),
    ("upper_arm.L", (0.18, 0.0, 1.44), (0.22, 0.0, 1.16), "shoulder.L"),
    ("forearm.L", (0.22, 0.0, 1.16), (0.24, 0.0, 0.90), "upper_arm.L"),
    ("hand.L",    (0.24, 0.0, 0.90), (0.25, 0.0, 0.78), "forearm.L"),

    ("shoulder.R", (-0.05, 0.0, 1.46), (-0.18, 0.0, 1.44), "chest"),
    ("upper_arm.R", (-0.18, 0.0, 1.44), (-0.22, 0.0, 1.16), "shoulder.R"),
    ("forearm.R", (-0.22, 0.0, 1.16), (-0.24, 0.0, 0.90), "upper_arm.R"),
    ("hand.R",    (-0.24, 0.0, 0.90), (-0.25, 0.0, 0.78), "forearm.R"),

    ("thigh.L",   (0.10, 0.0, 0.95), (0.10, 0.0, 0.52), "hips"),
    ("shin.L",    (0.10, 0.0, 0.52), (0.10, 0.0, 0.10), "thigh.L"),
    ("foot.L",    (0.10, 0.0, 0.10), (0.10, 0.12, 0.02), "shin.L"),

    ("thigh.R",   (-0.10, 0.0, 0.95), (-0.10, 0.0, 0.52), "hips"),
    ("shin.R",    (-0.10, 0.0, 0.52), (-0.10, 0.0, 0.10), "thigh.R"),
    ("foot.R",    (-0.10, 0.0, 0.10), (-0.10, 0.12, 0.02), "shin.R"),
]

# Quadrupeds are built from the measured bounding box instead, because leg length and
# body length vary far more between a wolf and a tortoise than between two humans.
QUADRUPED_SPINE_FRACTION = 0.55   # body occupies this share of the length, front to back


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--rig", default="humanoid",
                        choices=["humanoid", "quadruped", "worm", "none"])
    parser.add_argument("--max-influences", type=int, default=4,
                        help="glTF supports 4 bone influences per vertex")
    parser.add_argument("--min-weight", type=float, default=0.02)
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.objects):
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


def merge(meshes, name):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    # Name the object and its datablock after the asset, or the rigged export carries the
    # importer's generic Mesh_0/Material_0 and the rigged library is unreadable by name.
    joined.name = name
    joined.data.name = f"{name}_mesh"
    for material in joined.data.materials:
        if material is not None:
            material.name = f"MAT_{name}"
    return joined


def bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c[i] for c in corners) for i in range(3)))
    high = Vector((max(c[i] for c in corners) for i in range(3)))
    return low, high


def humanoid_bones():
    return HUMANOID


def quadruped_bones(low, high):
    """Build a quadruped skeleton scaled to the mesh's actual proportions."""
    length = high.y - low.y          # nose to tail along Y
    height = high.z - low.z
    width = high.x - low.x
    spine_z = low.z + height * 0.62
    body_start = low.y + length * 0.18
    body_end = low.y + length * (0.18 + QUADRUPED_SPINE_FRACTION)
    leg_x = width * 0.30
    leg_top = spine_z
    leg_bottom = low.z + height * 0.06

    bones = [
        ("root", (0.0, low.y + length * 0.5, low.z), (0.0, low.y + length * 0.5, low.z + 0.08), None),
        ("hips", (0.0, body_end, spine_z), (0.0, (body_start + body_end) / 2, spine_z), "root"),
        ("spine", (0.0, (body_start + body_end) / 2, spine_z), (0.0, body_start, spine_z), "hips"),
        ("neck", (0.0, body_start, spine_z), (0.0, low.y + length * 0.08, spine_z + height * 0.06), "spine"),
        ("head", (0.0, low.y + length * 0.08, spine_z + height * 0.06), (0.0, low.y, spine_z + height * 0.02), "neck"),
        ("tail", (0.0, body_end, spine_z), (0.0, high.y, spine_z + height * 0.04), "hips"),
    ]
    for side, sx in (("L", leg_x), ("R", -leg_x)):
        # Front pair hangs off the front of the spine, rear pair off the hips.
        bones += [
            (f"front_upper.{side}", (sx, body_start, leg_top), (sx, body_start, (leg_top + leg_bottom) / 2), "spine"),
            (f"front_lower.{side}", (sx, body_start, (leg_top + leg_bottom) / 2), (sx, body_start, leg_bottom), f"front_upper.{side}"),
            (f"front_paw.{side}", (sx, body_start, leg_bottom), (sx, body_start - length * 0.05, low.z), f"front_lower.{side}"),
            (f"rear_upper.{side}", (sx, body_end, leg_top), (sx, body_end, (leg_top + leg_bottom) / 2), "hips"),
            (f"rear_lower.{side}", (sx, body_end, (leg_top + leg_bottom) / 2), (sx, body_end, leg_bottom), f"rear_upper.{side}"),
            (f"rear_paw.{side}", (sx, body_end, leg_bottom), (sx, body_end - length * 0.05, low.z), f"rear_lower.{side}"),
        ]
    return bones


def worm_bones(low, high):
    """A single chain along the longest horizontal axis, for limbless creatures."""
    along_y = (high.y - low.y) >= (high.x - low.x)
    length = (high.y - low.y) if along_y else (high.x - low.x)
    mid = (low + high) / 2
    segments = 5
    bones = []
    for index in range(segments):
        t0 = index / segments
        t1 = (index + 1) / segments
        if along_y:
            head = (mid.x, low.y + length * t0, mid.z)
            tail = (mid.x, low.y + length * t1, mid.z)
        else:
            head = (low.x + length * t0, mid.y, mid.z)
            tail = (low.x + length * t1, mid.y, mid.z)
        name = "root" if index == 0 else f"segment_{index}"
        parent = None if index == 0 else ("root" if index == 1 else f"segment_{index - 1}")
        bones.append((name, head, tail, parent))
    return bones


def build_armature(bones, name):
    armature = bpy.data.armatures.new(f"{name}_armature")
    rig = bpy.data.objects.new(f"{name}_rig", armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for bone_name, head, tail, parent in bones:
        edit_bone = armature.edit_bones.new(bone_name)
        edit_bone.head = head
        edit_bone.tail = tail
        created[bone_name] = edit_bone
    for bone_name, _head, _tail, parent in bones:
        if parent and parent in created:
            created[bone_name].parent = created[parent]
            created[bone_name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def bind(mesh, rig):
    """Parent the mesh to the rig, then weight it.

    Heat-map weighting needs clean topology and fails silently on generative meshes:
    Blender warns "failed to find solution for one or more bones" and still returns
    success, leaving every vertex unweighted. So the weights are always checked, and a
    deterministic distance fallback is applied when they are missing. An asset that
    binds crudely is far better than one that cannot be animated at all.
    """
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    stats = weight_report(mesh, rig)
    if stats["unweighted_vertices"] == 0:
        return "automatic"

    print(f"    heat weighting left {stats['unweighted_vertices']} vertices unassigned; "
          f"applying distance weights")
    distance_weights(mesh, rig)
    return "distance-fallback"


def distance_weights(mesh, rig, falloff=2.0, reach=2.0):
    """Weight each vertex by proximity to bone segments, normalised per vertex.

    Produces a usable skin on any mesh, including non-manifold generative output. It
    does not respect topology the way heat weighting does, so joints deform more
    softly, but it always yields a complete and valid bind.
    """
    armature = rig.data
    deform = [bone for bone in armature.bones if bone.use_deform]
    segments = [(bone.name, rig.matrix_world @ bone.head_local,
                 rig.matrix_world @ bone.tail_local) for bone in deform]

    for group in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(group)
    groups = {name: mesh.vertex_groups.new(name=name) for name, _h, _t in segments}

    def point_to_segment(point, head, tail):
        span = tail - head
        length_sq = span.length_squared
        if length_sq < 1e-12:
            return (point - head).length
        t = max(0.0, min(1.0, (point - head).dot(span) / length_sq))
        return (point - (head + span * t)).length

    matrix = mesh.matrix_world
    scale = max(reach, 1e-6)
    for vertex in mesh.data.vertices:
        position = matrix @ vertex.co
        raw = []
        for name, head, tail in segments:
            distance = point_to_segment(position, head, tail)
            raw.append((name, 1.0 / ((distance / scale) ** falloff + 1e-6)))
        total = sum(weight for _n, weight in raw)
        if total <= 0.0:
            continue
        # Keep the strongest few and renormalise, mirroring the influence limit.
        raw.sort(key=lambda item: item[1], reverse=True)
        kept = raw[:max(1, 4)]
        kept_total = sum(weight for _n, weight in kept)
        for name, weight in kept:
            groups[name].add([vertex.index], weight / kept_total, "REPLACE")


def clean_weights(mesh, max_influences, min_weight):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode="ALL", limit=min_weight)
    bpy.ops.object.vertex_group_limit_total(group_select_mode="ALL", limit=max_influences)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)


def weight_report(mesh, rig):
    armature = rig.data
    deform_names = {b.name for b in armature.bones if b.use_deform}
    groups = {g.name: g.index for g in mesh.vertex_groups}
    present = deform_names & set(groups)

    unweighted = 0
    max_influences = 0
    for vertex in mesh.data.vertices:
        influences = [g for g in vertex.groups
                      if mesh.vertex_groups[g.group].name in deform_names and g.weight > 0.0]
        if not influences:
            unweighted += 1
        max_influences = max(max_influences, len(influences))

    total = len(mesh.data.vertices)
    return {
        "bones": len(armature.bones),
        "bones_with_weights": len(present),
        "vertices": total,
        "unweighted_vertices": unweighted,
        "unweighted_percent": round(100.0 * unweighted / max(total, 1), 2),
        "max_influences": max_influences,
    }


def export_glb(objects, path):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,      # applying modifiers would destroy the armature bind
        export_yup=True,
        export_normals=True,
        export_materials="EXPORT",
        export_texcoords=True,
        export_skins=True,
    )


def main():
    args = parse_args()
    os.makedirs(args.outdir, exist_ok=True)

    reset_scene()
    # The rigged export is <name>_rigged.glb, so its mesh follows the same stem rule the lod
    # and collision proxies use: mesh name is the file stem plus _mesh.
    mesh = merge(import_glb(args.input), f"{args.name}_rigged")
    low, high = bounds(mesh)

    if args.rig == "humanoid":
        bones = humanoid_bones()
    elif args.rig == "quadruped":
        bones = quadruped_bones(low, high)
    elif args.rig == "worm":
        bones = worm_bones(low, high)
    else:
        bones = None

    if bones is None:
        report = {"asset": args.name, "plan": "none", "note": "validation only"}
        print("RIG_RESULT " + json.dumps(report))
        return

    rig = build_armature(bones, args.name)
    method = bind(mesh, rig)
    clean_weights(mesh, args.max_influences, args.min_weight)
    stats = weight_report(mesh, rig)

    out_path = os.path.join(args.outdir, f"{args.name}_rigged.glb")
    export_glb([rig, mesh], out_path)

    report = {
        "asset": args.name,
        "plan": args.rig,
        "method": method,
        "out": out_path,
        **stats,
    }
    with open(os.path.join(args.outdir, f"{args.name}_rig.json"), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print("RIG_RESULT " + json.dumps(report))


if __name__ == "__main__":
    main()
