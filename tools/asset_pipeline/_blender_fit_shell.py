"""Derive a canonical wearable fit shell from a fit-family body reference.

This is Method C of the armour bakeoff. Armour fit must not come from a generated
mannequin, because the generator's body is arbitrary and unrepeatable: the same prompt gives
a different torso each run, so armour fitted to it fits nothing consistently.

Instead the canonical body is deterministic - a declared parametric form per fit family - and
an armour shell is derived from it by:

  1. selecting the body region the piece covers
  2. duplicating that surface
  3. offsetting it outward by the layer offset for its construction family
  4. solidifying it into a shell with a declared thickness
  5. cutting the boundary cleanly along region edges

Every step is deterministic, so the same definition always yields the same shell. Style,
exterior form and decoration can then come from generated geometry laid over this base.

Run inside Blender:
  blender --background --factory-startup --python _blender_fit_shell.py -- ^
      --fit-family standard_humanoid --region chest --layer armour \\
      --out ready\\armour_chest_plate_base_a\\armour_chest_plate_base_a.glb
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

# Body proportions per fit family, in metres, in the export frame (Y up, base at 0).
# These are declared constants, not generated values: that is the whole point. A fit family
# is a specification, and armour is authored against the specification.
FIT_FAMILIES = {
    "standard_humanoid": {
        "height": 1.80, "chest_depth": 0.22, "chest_width": 0.34,
        "waist_width": 0.28, "hip_width": 0.32, "neck_width": 0.13,
        "shoulder_width": 0.44, "neck_z": 1.50, "chest_z": 1.32,
        "waist_z": 1.10, "hip_z": 0.95, "shoulder_z": 1.45, "gorget_z": 1.58, "gorget_top_z": 1.70,
    },
    "compact_broad": {
        "height": 1.30, "chest_depth": 0.30, "chest_width": 0.46,
        "waist_width": 0.42, "hip_width": 0.44, "neck_width": 0.20,
        "shoulder_width": 0.58, "neck_z": 1.08, "chest_z": 0.95,
        "waist_z": 0.79, "hip_z": 0.68, "shoulder_z": 1.04, "gorget_z": 1.12, "gorget_top_z": 1.22,
    },
    "tall_narrow": {
        "height": 2.35, "chest_depth": 0.17, "chest_width": 0.26,
        "waist_width": 0.21, "hip_width": 0.24, "neck_width": 0.10,
        "shoulder_width": 0.34, "neck_z": 1.96, "chest_z": 1.72,
        "waist_z": 1.44, "hip_z": 1.24, "shoulder_z": 1.89, "gorget_z": 2.06, "gorget_top_z": 2.22,
    },
    "irregular_heavy": {
        "height": 2.60, "chest_depth": 0.44, "chest_width": 0.66,
        "waist_width": 0.60, "hip_width": 0.64, "neck_width": 0.26,
        "shoulder_width": 0.82, "neck_z": 2.16, "chest_z": 1.90,
        "waist_z": 1.58, "hip_z": 1.36, "shoulder_z": 2.08, "gorget_z": 2.26, "gorget_top_z": 2.42,
    },
}

# Layer offsets outward from the body surface, per construction family, in metres.
LAYER_OFFSETS = {
    "underlayer": 0.008,
    "cloth": 0.004,
    "gambeson": 0.012,
    "leather": 0.014,
    "mail": 0.020,
    "scale": 0.024,
    "lamellar": 0.026,
    "brigandine": 0.024,
    "plate": 0.028,
    "splint": 0.026,
    "coat_of_plates": 0.026,
    "exotic": 0.030,
}

# Which vertical band of the body a region covers, given as named landmarks of the fit
# family plus an angular range around the body (0 degrees is front).
REGIONS = {
    "chest":    {"z_min": "waist_z", "z_max": "shoulder_z", "angle": (-72, 72)},
    "back":     {"z_min": "waist_z", "z_max": "shoulder_z", "angle": (108, 252)},
    "abdomen":  {"z_min": "hip_z", "z_max": "waist_z", "angle": (-80, 80)},
    "gorget":   {"z_min": "gorget_z", "z_max": "gorget_top_z", "angle": (-150, 150)},
    "shoulder": {"z_min": "shoulder_z", "z_max": "neck_z", "angle": (18, 80)},
    "hip":      {"z_min": "hip_z", "z_max": "waist_z", "angle": (-104, 104)},
    "full_torso": {"z_min": "hip_z", "z_max": "shoulder_z", "angle": (-180, 180)},
}


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--fit-family", required=True, choices=sorted(FIT_FAMILIES))
    parser.add_argument("--region", required=True, choices=sorted(REGIONS))
    parser.add_argument("--layer", default="plate", choices=sorted(LAYER_OFFSETS))
    parser.add_argument("--out", required=True)
    parser.add_argument("--name", default=None)
    parser.add_argument("--thickness", type=float, default=0.003,
                        help="Shell thickness in metres")
    parser.add_argument("--segments", type=int, default=48)
    parser.add_argument("--report", default=None)
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def body_section_radius(family, z_fraction):
    """Radius profile of the canonical body at a height, as (half_width, half_depth).

    Interpolates between the declared landmark widths. Deliberately simple: this is a fit
    reference, not an anatomical model, and simplicity is what makes it repeatable.
    """
    landmarks = [
        (family["hip_z"], family["hip_width"] / 2, family["chest_depth"] / 2 * 0.95),
        (family["waist_z"], family["waist_width"] / 2, family["chest_depth"] / 2 * 0.85),
        (family["chest_z"], family["chest_width"] / 2, family["chest_depth"] / 2),
        (family["shoulder_z"], family["shoulder_width"] / 2, family["chest_depth"] / 2),
        # The gorget band encloses the neck, so it must NOT interpolate from the shoulder
        # width - blending shoulder to neck made a 0.36 m wide collar for a 0.13 m neck.
        # Two landmarks close together and both near the neck give a proper tapering collar.
        (family["gorget_z"], family["neck_width"] * 0.75, family["neck_width"] * 0.85),
        (family["gorget_top_z"], family["neck_width"] * 0.58, family["neck_width"] * 0.66),
    ]
    landmarks.sort(key=lambda item: item[0])
    z = z_fraction
    if z <= landmarks[0][0]:
        return landmarks[0][1], landmarks[0][2]
    if z >= landmarks[-1][0]:
        return landmarks[-1][1], landmarks[-1][2]
    for (z0, w0, d0), (z1, w1, d1) in zip(landmarks, landmarks[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / max(z1 - z0, 1e-9)
            return w0 + (w1 - w0) * t, d0 + (d1 - d0) * t
    return landmarks[-1][1], landmarks[-1][2]


def build_shell(family, region, offset, thickness, segments):
    """Build the region's surface as a solidified shell, offset outward from the body."""
    spec = REGIONS[region]
    z_low = family[spec["z_min"]]
    z_high = family[spec["z_max"]]
    if region == "shoulder":
        z_high = z_low + 0.06
    angle_start, angle_end = spec["angle"]

    rows = 14
    cols = max(int(segments * (angle_end - angle_start) / 360.0), 8)

    mesh = bpy.data.meshes.new("shell")
    obj = bpy.data.objects.new("shell", mesh)
    bpy.context.collection.objects.link(obj)

    verts = []
    faces = []
    for row in range(rows + 1):
        t = row / rows
        z = z_low + (z_high - z_low) * t
        half_w, half_d = body_section_radius(family, z)
        for col in range(cols + 1):
            u = col / cols
            angle = math.radians(angle_start + (angle_end - angle_start) * u)
            # Elliptical section: angle 0 is front, and the shell follows the body with the
            # layer offset applied radially outward.
            #
            # Blender is Z-up; the exporter converts to Y-up. A body height therefore goes
            # into Blender's Y here, NOT its Z - writing it into Z produced a flat horizontal
            # band that exported as a 9 cm tall "chest plate".
            x = (half_w + offset) * math.sin(angle)
            depth = (half_d + offset) * math.cos(angle)
            verts.append((x, depth, z))
    for row in range(rows):
        for col in range(cols):
            a = row * (cols + 1) + col
            b = a + 1
            c = a + (cols + 1)
            d = c + 1
            faces.append((a, b, d, c))

    mesh.from_pydata(verts, [], faces)
    mesh.update()

    # Solidify outward so the shell has thickness and closed edges - this is what makes it a
    # wearable plate rather than a single-sided surface.
    solidify = obj.modifiers.new("Solidify", "SOLIDIFY")
    solidify.thickness = thickness
    solidify.offset = 1.0
    solidify.use_even_offset = True

    bevel = obj.modifiers.new("Bevel", "BEVEL")
    bevel.width = 0.0015
    bevel.segments = 2

    bpy.context.view_layer.objects.active = obj
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c[i] for c in corners) for i in range(3)))
    high = Vector((max(c[i] for c in corners) for i in range(3)))
    return low, high


def normalise_to_region(obj, name):
    """Centre the footprint and seat the base, using the existing pipeline convention."""
    obj.name = name
    obj.data.name = f"{name}_mesh"
    low, high = bounds(obj)
    centre = (low + high) / 2
    obj.location = (obj.location.x - centre.x, obj.location.y - centre.y, obj.location.z - low.z)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    return bounds(obj)


def main():
    args = parse_args()
    family = FIT_FAMILIES[args.fit_family]
    offset = LAYER_OFFSETS[args.layer]
    name = args.name or f"armour_{args.region}_{args.layer}_a"

    reset_scene()
    obj = build_shell(family, args.region, offset, args.thickness, args.segments)
    low, high = normalise_to_region(obj, name)

    material = bpy.data.materials.new(f"MAT_{name}")
    material.use_nodes = True
    material.use_backface_culling = True

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=True)

    report = {
        "name": name,
        "method": "C_canonical_fit_shell",
        "fit_family": args.fit_family,
        "region": args.region,
        "layer": args.layer,
        "layer_offset_m": offset,
        "thickness_m": args.thickness,
        "vertices": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "dimensions_m": [round(high.x - low.x, 4), round(high.z - low.z, 4),
                         round(high.y - low.y, 4)],
        "out": args.out,
    }
    if args.report:
        with open(args.report, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2)
    print("FITSHELL_RESULT " + json.dumps(report))


if __name__ == "__main__":
    main()
