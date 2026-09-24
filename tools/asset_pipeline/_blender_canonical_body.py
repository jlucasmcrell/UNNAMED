"""Build the canonical fit-family body and its armature.

This is step 2 of the animation pipeline order: freeze character scale and body-reference
conventions. Armour fit, rigging and retargeting all depend on it, so it must be a
**declared specification** rather than a generated mesh - the same reasoning that produced
Method C in the armour bakeoff.

Exports, for one fit family:
  <family>_body.glb        the reference body, skinned to the canonical skeleton
  <family>_skeleton.json   bone positions, parentage, reference pose, units

Conventions, all deliberately matching the existing Wave-0 asset standard so armour authored
against it lines up without conversion:

  units        metres
  export       Y-up, base on the ground plane, footprint centred
  reference    A-pose (arms 45 degrees down), the bind pose
  forward      -Z in the export frame
  up           +Y in the export frame

Run inside Blender:
  blender --background --factory-startup --python _blender_canonical_body.py -- ^
      --fit-family standard_humanoid --out assets\\rigs\\humanoid_standard\\body.glb
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

# Proportions per fit family, in metres. These live here rather than being measured from
# generated art because a generated body differs every run, and armour fitted to one body
# fits nothing else.
FIT_FAMILIES = {
    "standard_humanoid": {
        "height": 1.80,
        "head": 0.235, "neck": 0.075, "shoulder_w": 0.42, "chest_w": 0.34,
        "chest_d": 0.22, "waist_w": 0.28, "hip_w": 0.32,
        "upperarm": 0.295, "lowerarm": 0.255, "hand": 0.19,
        "thigh": 0.435, "calf": 0.415, "foot": 0.255,
        "shoulder_z": 1.45, "chest_z": 1.32, "waist_z": 1.10,
        "hip_z": 0.95, "knee_z": 0.50, "ankle_z": 0.09,
        "gorget_z": 1.58, "gorget_top_z": 1.70, "neck_z": 1.50,
    },
    "compact_broad": {
        "height": 1.30,
        "head": 0.215, "neck": 0.070, "shoulder_w": 0.58, "chest_w": 0.46,
        "chest_d": 0.30, "waist_w": 0.42, "hip_w": 0.44,
        "upperarm": 0.215, "lowerarm": 0.185, "hand": 0.165,
        "thigh": 0.310, "calf": 0.295, "foot": 0.200,
        "shoulder_z": 1.04, "chest_z": 0.95, "waist_z": 0.79,
        "hip_z": 0.68, "knee_z": 0.36, "ankle_z": 0.08,
        "gorget_z": 1.12, "gorget_top_z": 1.22, "neck_z": 1.08,
    },
    "tall_narrow": {
        "height": 2.35,
        "head": 0.260, "neck": 0.095, "shoulder_w": 0.34, "chest_w": 0.26,
        "chest_d": 0.17, "waist_w": 0.21, "hip_w": 0.24,
        "upperarm": 0.395, "lowerarm": 0.345, "hand": 0.235,
        "thigh": 0.580, "calf": 0.555, "foot": 0.300,
        "shoulder_z": 1.89, "chest_z": 1.72, "waist_z": 1.44,
        "hip_z": 1.24, "knee_z": 0.65, "ankle_z": 0.10,
        "gorget_z": 2.06, "gorget_top_z": 2.22, "neck_z": 1.96,
    },
    "irregular_heavy": {
        "height": 2.60,
        "head": 0.300, "neck": 0.140, "shoulder_w": 0.82, "chest_w": 0.66,
        "chest_d": 0.44, "waist_w": 0.60, "hip_w": 0.64,
        "upperarm": 0.400, "lowerarm": 0.350, "hand": 0.280,
        "thigh": 0.620, "calf": 0.600, "foot": 0.360,
        "shoulder_z": 2.08, "chest_z": 1.90, "waist_z": 1.58,
        "hip_z": 1.36, "knee_z": 0.72, "ankle_z": 0.11,
        "gorget_z": 2.26, "gorget_top_z": 2.42, "neck_z": 2.16,
    },
}

# The canonical humanoid bone list. Names are lower-case with _l/_r suffixes, matching the
# armour region vocabulary in the modular asset standard so a bone and a coverage region are
# spelled the same way.
BONES = [
    ("root", None),
    ("pelvis", "root"),
    ("spine_01", "pelvis"),
    ("spine_02", "spine_01"),
    ("spine_03", "spine_02"),
    ("chest", "spine_03"),
    ("neck", "chest"),
    ("head", "neck"),
    ("clavicle_l", "chest"), ("upperarm_l", "clavicle_l"),
    ("lowerarm_l", "upperarm_l"), ("hand_l", "lowerarm_l"),
    ("clavicle_r", "chest"), ("upperarm_r", "clavicle_r"),
    ("lowerarm_r", "upperarm_r"), ("hand_r", "lowerarm_r"),
    ("thigh_l", "pelvis"), ("calf_l", "thigh_l"),
    ("foot_l", "calf_l"), ("toe_l", "foot_l"),
    ("thigh_r", "pelvis"), ("calf_r", "thigh_r"),
    ("foot_r", "calf_r"), ("toe_r", "foot_r"),
]

# Two-segment fingers, so a hand can close on a grip. `ANIMATION_RIGGING_AND_RETARGETING_
# PIPELINE.md` requires "finger bones ... weapon attachment points; hand IK targets; foot IK
# targets" but names none of them, and `CANONICAL_BODY_AND_SKELETON.md` lists all of those as
# still missing. The names below are this pipeline's frozen choice; they follow the document's
# stated convention (lower-case, `_l`/`_r` side suffix, zero-padded numeric suffix).
#
# (finger, lateral offset in metres, total finger length as a fraction of hand length)
FINGERS = [
    ("index", -0.022, 0.92),
    ("middle", -0.007, 1.00),
    ("ring", 0.008, 0.93),
    ("pinky", 0.023, 0.78),
]
THUMB = ("thumb", 0.030, 0.62)

# Non-deforming helper bones. IK targets sit exactly on the joint they drive, and pole targets
# are offset so an IK solver has a plane to resolve against. They must not take skin weights, or
# automatic weighting would pull geometry toward a point that never moves with the mesh.
IK_BONES = [
    ("IK_hand_l", "root"), ("IK_elbow_l", "root"),
    ("IK_foot_l", "root"), ("IK_knee_l", "root"),
    ("IK_hand_r", "root"), ("IK_elbow_r", "root"),
    ("IK_foot_r", "root"), ("IK_knee_r", "root"),
]

# Equipment attachment points, exported as named empties rather than bones because that is what
# `WAVE_0_MODULAR_ASSET_STANDARD.md` specifies for `SOCK_*` interfaces. A held weapon's
# `SOCK_grip_primary` is mated to `SOCK_hand_*`; the rest are where stowed gear rides.
ATTACHMENTS = [
    ("SOCK_hand_l", "hand_l"),
    ("SOCK_hand_r", "hand_r"),
    ("SOCK_attach_back", "chest"),
    ("SOCK_attach_hip_l", "pelvis"),
    ("SOCK_attach_hip_r", "pelvis"),
]

SKELETON_VERSION = "humanoid_standard_v2"


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--fit-family", required=True, choices=sorted(FIT_FAMILIES))
    parser.add_argument("--out", required=True, help="Body GLB to write")
    parser.add_argument("--blend-source", default=None)
    parser.add_argument("--report", default=None, help="Skeleton JSON to write")
    parser.add_argument("--segments", type=int, default=16,
                        help="Radial resolution of the body sections")
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials, bpy.data.armatures):
        for item in list(block):
            block.remove(item)


def bone_positions(f):
    """Canonical joint positions, in Blender (Z-up) coordinates.

    A-pose: arms angled 45 degrees down from horizontal. The reference pose is part of the
    contract, so it is computed here rather than left to whoever binds a mesh later.
    """
    hip_z = f["hip_z"]
    arm_angle = math.radians(45.0)
    # Shoulder joints sit a shoulder-width apart, slightly below the shoulder landmark.
    shoulder_x = f["shoulder_w"] / 2.0 * 0.82
    arm_dx = math.cos(arm_angle)
    arm_dz = -math.sin(arm_angle)
    upper_len, lower_len, hand_len = f["upperarm"], f["lowerarm"], f["hand"]

    pos = {
        "root": (0.0, 0.0, 0.0),
        "pelvis": (0.0, 0.0, hip_z),
        "spine_01": (0.0, 0.0, (hip_z + f["waist_z"]) / 2.0),
        "spine_02": (0.0, 0.0, f["waist_z"]),
        "spine_03": (0.0, 0.0, (f["waist_z"] + f["chest_z"]) / 2.0),
        "chest": (0.0, 0.0, f["chest_z"]),
        "neck": (0.0, 0.0, f["neck_z"]),
        "head": (0.0, 0.0, f["neck_z"] + f["neck"] + f["head"] * 0.5),
    }
    for side, sign in (("l", 1.0), ("r", -1.0)):
        sx = shoulder_x * sign
        sz = f["shoulder_z"]
        pos[f"clavicle_{side}"] = (sx * 0.35, 0.0, sz)
        elbow = (sx + arm_dx * upper_len * sign, 0.0, sz + arm_dz * upper_len)
        wrist = (elbow[0] + arm_dx * lower_len * sign, 0.0, elbow[2] + arm_dz * lower_len)
        tip = (wrist[0] + arm_dx * hand_len * sign, 0.0, wrist[2] + arm_dz * hand_len)
        pos[f"upperarm_{side}"] = (sx, 0.0, sz)
        pos[f"lowerarm_{side}"] = elbow
        pos[f"hand_{side}"] = wrist
        pos[f"__handtip_{side}"] = tip

        # Fingers run on from a palm point along the same direction as the hand. They spread
        # laterally in Y, which is the axis perpendicular to the arm plane, so the spread is
        # identical for both hands and only X mirrors.
        hand_dir = (arm_dx * sign, 0.0, arm_dz)
        palm = (wrist[0] + hand_dir[0] * hand_len * 0.55,
                wrist[1],
                wrist[2] + hand_dir[2] * hand_len * 0.55)
        pos[f"__palm_{side}"] = palm

        def along(origin, distance, extra=(0.0, 0.0, 0.0)):
            return (origin[0] + hand_dir[0] * distance + extra[0],
                    origin[1] + extra[1],
                    origin[2] + hand_dir[2] * distance + extra[2])

        for finger, y_offset, fraction in FINGERS:
            length = hand_len * fraction
            base = (palm[0], palm[1] + y_offset, palm[2])
            middle = along(base, length * 0.5)
            pos[f"{finger}_01_{side}"] = base
            pos[f"{finger}_02_{side}"] = middle
            pos[f"__tip_{finger}_{side}"] = along(middle, length * 0.5)

        thumb, thumb_y, thumb_fraction = THUMB
        thumb_len = hand_len * thumb_fraction
        thumb_base = along((wrist[0], wrist[1] + thumb_y, wrist[2]), hand_len * 0.28)
        thumb_mid = along(thumb_base, thumb_len * 0.5, extra=(0.0, thumb_len * 0.30, 0.0))
        pos[f"{thumb}_01_{side}"] = thumb_base
        pos[f"{thumb}_02_{side}"] = thumb_mid
        pos[f"__tip_{thumb}_{side}"] = along(thumb_mid, thumb_len * 0.5,
                                             extra=(0.0, thumb_len * 0.30, 0.0))

        # IK targets sit on the joint they drive; pole targets sit off to the side so a solver
        # has a plane. Toes point toward -Y in this rig, so forward is -Y: knees pole forward,
        # elbows pole backward.
        pos[f"IK_hand_{side}"] = wrist
        pos[f"IK_elbow_{side}"] = (elbow[0] + 0.05 * sign, 0.12, elbow[2] - 0.05)

        hx = f["hip_w"] / 2.0 * 0.55 * sign
        pos[f"thigh_{side}"] = (hx, 0.0, hip_z)
        pos[f"calf_{side}"] = (hx, 0.0, f["knee_z"])
        pos[f"foot_{side}"] = (hx, 0.0, f["ankle_z"])
        pos[f"toe_{side}"] = (hx, -f["foot"] * 0.6, f["ankle_z"] * 0.35)
        pos[f"IK_foot_{side}"] = (hx, 0.0, f["ankle_z"])
        pos[f"IK_knee_{side}"] = (hx, -0.12, f["knee_z"])

        pos[f"SOCK_hand_{side}"] = palm
        pos[f"SOCK_attach_hip_{side}"] = (f["hip_w"] / 2.0 * 0.95 * sign, 0.07, hip_z + 0.04)

    pos["SOCK_attach_back"] = (0.0, 0.17, f["chest_z"] + 0.10)
    return pos


def skeleton_bones():
    """Every bone in the character skeleton, as (name, parent, deform).

    The 24 core bones are unchanged and stay first, so anything that reads the previous skeleton
    keeps working: the armour fit boundary, the retarget profiles and the existing clips all key
    off those names. Fingers deform; IK and pole targets do not.
    """
    bones = [(name, parent, True) for name, parent in BONES]
    for side in ("l", "r"):
        for finger, _y, _f in FINGERS + [THUMB]:
            bones.append((f"{finger}_01_{side}", f"hand_{side}", True))
            bones.append((f"{finger}_02_{side}", f"{finger}_01_{side}", True))
    bones.extend([(name, parent, False) for name, parent in IK_BONES])
    return bones


def build_armature(f, name):
    positions = bone_positions(f)
    bones = skeleton_bones()
    arm_data = bpy.data.armatures.new(f"RIG_{name}")
    rig = bpy.data.objects.new(f"RIG_{name}", arm_data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")

    parents = {b: p for b, p, _d in bones}
    created = {}
    for bone_name, parent, deform in bones:
        head = positions[bone_name]
        # A bone's tail is its child's head where one exists, otherwise the recorded tip.
        child = next((b for b, p, _d in bones if p == bone_name), None)
        if child:
            tail = positions[child]
        elif bone_name.startswith(("index_02", "middle_02", "ring_02", "pinky_02", "thumb_02")):
            finger = bone_name.split("_")[0]
            tail = positions[f"__tip_{finger}_{bone_name[-1]}"]
        elif bone_name.startswith("hand_"):
            tail = positions[f"__handtip_{bone_name[-1]}"]
        else:
            tail = (head[0], head[1], head[2] + 0.05)
        edit_bone = arm_data.edit_bones.new(bone_name)
        edit_bone.head = Vector(head)
        edit_bone.tail = Vector(tail)
        edit_bone.use_deform = deform
        if parent:
            edit_bone.parent = created[parent]
            edit_bone.use_connect = False
        created[bone_name] = edit_bone

    bpy.ops.object.mode_set(mode="OBJECT")

    # Equipment attachment points as named empties, per the modular asset standard's `SOCK_*`
    # convention. They are parented to the bone they ride so they follow the body.
    #
    # A bone-parented object's local location is expressed in the bone's own rotated frame, so
    # `target - bone_tail` is only correct for unrotated bones and lands the socket in the wrong
    # place on the arms and chest. Assign the world matrix after parenting instead and let
    # Blender derive the local offset.
    bpy.context.view_layer.update()
    for socket_name, bone_parent in ATTACHMENTS:
        target = Vector(positions[socket_name])
        empty = bpy.data.objects.new(socket_name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.04
        bpy.context.collection.objects.link(empty)
        empty.parent = rig
        empty.parent_type = "BONE"
        empty.parent_bone = bone_parent
        bpy.context.view_layer.update()
        empty.matrix_world = Matrix.Translation(target)
        empty["socket_role"] = "equipment_attach"
        empty["socket_bone"] = bone_parent

    return rig, positions


def body_section(f, z):
    """Half-width and half-depth of the reference body at a height."""
    landmarks = [
        (f["ankle_z"], f["hip_w"] / 2 * 0.42, f["chest_d"] / 2 * 0.45),
        (f["knee_z"], f["hip_w"] / 2 * 0.52, f["chest_d"] / 2 * 0.55),
        (f["hip_z"], f["hip_w"] / 2, f["chest_d"] / 2 * 0.95),
        (f["waist_z"], f["waist_w"] / 2, f["chest_d"] / 2 * 0.85),
        (f["chest_z"], f["chest_w"] / 2, f["chest_d"] / 2),
        (f["shoulder_z"], f["shoulder_w"] / 2, f["chest_d"] / 2),
        (f["gorget_z"], f["neck"] * 0.75, f["neck"] * 0.85),
        (f["gorget_top_z"], f["neck"] * 0.58, f["neck"] * 0.66),
        (f["neck_z"] + f["neck"], f["neck"] / 2, f["neck"] / 2 * 1.05),
    ]
    landmarks.sort(key=lambda item: item[0])
    if z <= landmarks[0][0]:
        return landmarks[0][1], landmarks[0][2]
    if z >= landmarks[-1][0]:
        return landmarks[-1][1], landmarks[-1][2]
    for (z0, w0, d0), (z1, w1, d1) in zip(landmarks, landmarks[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / max(z1 - z0, 1e-9)
            return w0 + (w1 - w0) * t, d0 + (d1 - d0) * t
    return landmarks[-1][1], landmarks[-1][2]


def add_tube(verts, faces, path, radii, segments):
    """Append a closed tube following `path`, with per-point radii.

    Used for limbs, which the torso loft does not produce. Each ring is oriented along the
    local segment direction so an angled arm does not become a distorted prism.
    """
    rings = []
    for index, (point, radius) in enumerate(zip(path, radii)):
        if index == 0:
            direction = Vector(path[1]) - Vector(path[0])
        elif index == len(path) - 1:
            direction = Vector(path[-1]) - Vector(path[-2])
        else:
            direction = Vector(path[index + 1]) - Vector(path[index - 1])
        direction.normalize()
        # Any axis not parallel to the segment will do as a starting reference.
        reference = Vector((0.0, 1.0, 0.0))
        if abs(direction.dot(reference)) > 0.9:
            reference = Vector((0.0, 0.0, 1.0))
        side = direction.cross(reference).normalized()
        up = side.cross(direction).normalized()
        centre = Vector(point)
        ring = []
        for col in range(segments):
            angle = 2 * math.pi * col / segments
            offset = side * (radius * math.sin(angle)) + up * (radius * math.cos(angle))
            ring.append(centre + offset)
        rings.append(ring)

    base = len(verts)
    for ring in rings:
        for point in ring:
            verts.append((point.x, point.y, point.z))
    for row in range(len(rings) - 1):
        for col in range(segments):
            a = base + row * segments + col
            b = base + row * segments + (col + 1) % segments
            c = base + (row + 1) * segments + col
            d = base + (row + 1) * segments + (col + 1) % segments
            faces.append((a, b, d, c))


def build_body(f, name, segments):
    """A lofted torso plus tube limbs, sized from the declared landmarks.

    Deliberately plain: this is a fit reference, not character art. Its value is that it is
    the same every run, so armour authored against it fits every character using the family.
    Limbs matter even so - armour covers arms and legs, and a torso-only reference cannot
    support the deformation checks the modular standard requires.
    """
    verts, faces = [], []
    rows = 24
    # Torso spans hip to neck; the head is a separate cap so the trunk does not balloon.
    z_low, z_high = f["hip_z"] * 0.55, f["neck_z"] + f["neck"]
    for row in range(rows + 1):
        z = z_low + (z_high - z_low) * row / rows
        half_w, half_d = body_section(f, z)
        for col in range(segments):
            angle = 2 * math.pi * col / segments
            verts.append((half_w * math.sin(angle), half_d * math.cos(angle), z))
    for row in range(rows):
        for col in range(segments):
            a = row * segments + col
            b = row * segments + (col + 1) % segments
            c = (row + 1) * segments + col
            d = (row + 1) * segments + (col + 1) % segments
            faces.append((a, b, d, c))

    positions = bone_positions(f)
    arm_radius = f["upperarm"] * 0.085
    leg_radius = f["thigh"] * 0.095

    for side in ("l", "r"):
        # Arm: shoulder -> elbow -> wrist, following the A-pose the skeleton declares.
        shoulder = positions[f"upperarm_{side}"]
        elbow = positions[f"lowerarm_{side}"]
        wrist = positions[f"hand_{side}"]
        mid_upper = tuple((a + b) / 2 for a, b in zip(shoulder, elbow))
        mid_lower = tuple((a + b) / 2 for a, b in zip(elbow, wrist))
        add_tube(verts, faces, [shoulder, mid_upper, elbow, mid_lower, wrist],
                 [arm_radius * 1.15, arm_radius, arm_radius * 0.88, arm_radius * 0.78,
                  arm_radius * 0.62], max(segments // 2, 8))

        # Hand and fingers. A stub hand leaves the finger bones unweighted on the reference
        # body, and weight transfer can only propagate weights the source already has, so a
        # character bound from this body ends up with a rigid hand. A weapon grip needs the
        # digits to articulate.
        palm = positions[f"__palm_{side}"]
        add_tube(verts, faces, [wrist, palm],
                 [arm_radius * 0.62, arm_radius * 0.56], max(segments // 2, 8))
        finger_radius = arm_radius * 0.17
        for finger, _offset, _fraction in FINGERS + [THUMB]:
            add_tube(verts, faces,
                     [positions[f"{finger}_01_{side}"],
                      positions[f"{finger}_02_{side}"],
                      positions[f"__tip_{finger}_{side}"]],
                     [finger_radius * 1.15, finger_radius * 0.92, finger_radius * 0.68], 6)

        # Leg: hip -> knee -> ankle.
        hip = positions[f"thigh_{side}"]
        knee = positions[f"calf_{side}"]
        ankle = positions[f"foot_{side}"]
        mid_thigh = tuple((a + b) / 2 for a, b in zip(hip, knee))
        mid_calf = tuple((a + b) / 2 for a, b in zip(knee, ankle))
        add_tube(verts, faces, [hip, mid_thigh, knee, mid_calf, ankle],
                 [leg_radius * 1.2, leg_radius * 0.95, leg_radius * 0.82,
                  leg_radius * 0.68, leg_radius * 0.5], max(segments // 2, 8))

    # Head: a simple ovoid above the neck so silhouette and helmet fit have something to sit on.
    head_base = positions["head"]
    head_centre = (head_base[0], head_base[1], head_base[2] + f["head"] * 0.12)
    head_rows = 8
    for row in range(head_rows + 1):
        t = row / head_rows
        z = head_centre[2] - f["head"] * 0.5 + f["head"] * t
        # Narrow at both ends so it reads as a head rather than a cylinder.
        shrink = math.sin(math.pi * max(min(t, 1.0), 0.0)) ** 0.45
        half_w = f["head"] * 0.34 * shrink
        half_d = f["head"] * 0.40 * shrink
        for col in range(segments):
            angle = 2 * math.pi * col / segments
            verts.append((half_w * math.sin(angle), half_d * math.cos(angle), z))
    head_base_index = len(verts) - (head_rows + 1) * segments
    for row in range(head_rows):
        for col in range(segments):
            a = head_base_index + row * segments + col
            b = head_base_index + row * segments + (col + 1) % segments
            c = head_base_index + (row + 1) * segments + col
            d = head_base_index + (row + 1) * segments + (col + 1) % segments
            faces.append((a, b, d, c))

    mesh = bpy.data.meshes.new(f"{name}_mesh")
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    mesh.from_pydata(verts, [], faces)
    mesh.update()

    # Smooth so the reference reads as a body rather than a prism stack.
    for poly in mesh.polygons:
        poly.use_smooth = True
    subsurf = obj.modifiers.new("Smooth", "SUBSURF")
    subsurf.levels = 1
    subsurf.render_levels = 1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=subsurf.name)

    material = bpy.data.materials.new(f"MAT_{name}")
    material.use_nodes = True
    material.use_backface_culling = True
    obj.data.materials.append(material)
    return obj


def main():
    args = parse_args()
    f = FIT_FAMILIES[args.fit_family]
    name = f"{args.fit_family}_body"

    reset_scene()
    rig, positions = build_armature(f, name)
    body = build_body(f, name, args.segments)

    # Skin the body to the skeleton with automatic weights, so the reference can be posed -
    # which is what makes it usable for the deformation and clipping checks the armour
    # standard requires.
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
        weighted = True
    except RuntimeError:
        weighted = False

    # Enforce the invariant rather than trust the auto-weighter. A pole target sits outside the
    # limb it steers, close enough that bone-heat weighting gave `IK_elbow_l` real weight on
    # tall_narrow. A helper with weight deforms the mesh, which is exactly what it must not do.
    helper_names = {name for name, _parent in IK_BONES}
    purged = []
    parent_mesh = bpy.data.objects.get(name)
    if parent_mesh:
        for group in list(parent_mesh.vertex_groups):
            if group.name in helper_names:
                parent_mesh.vertex_groups.remove(group)
                purged.append(group.name)

    obj = bpy.data.objects[name]
    obj.name = name
    obj.data.name = f"{name}_mesh"

    sockets = [bpy.data.objects[s] for s, _p in ATTACHMENTS if s in bpy.data.objects]

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    keep = {obj, rig, *sockets}
    for other in bpy.context.scene.objects:
        other.select_set(other in keep)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=False,
        export_skins=True, export_extras=True)

    if args.blend_source:
        os.makedirs(os.path.dirname(args.blend_source), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=args.blend_source)

    def export_frame(p):
        x, y, z = p
        return [round(x, 5), round(z, 5), round(-y, 5)]

    bones = skeleton_bones()
    core_names = {b for b, _p in BONES}
    skeleton = {
        "skeleton_version": SKELETON_VERSION,
        "fit_family": args.fit_family,
        "units": "metres",
        "export_frame": "Y-up, base on ground plane, footprint centred",
        "forward_axis": "-Z",
        "up_axis": "+Y",
        "reference_pose": "A-pose, arms 45 degrees down",
        "height_m": f["height"],
        "bone_count": len(bones),
        "core_bone_count": len(BONES),
        "deform_bone_count": sum(1 for _b, _p, d in bones if d),
        "bones": [
            {
                "name": b,
                "parent": p,
                "head": export_frame(positions[b]),
                "deform": d,
                "role": ("core" if b in core_names
                         else "finger" if any(b.startswith(f"{n}_") for n, _y, _f in FINGERS + [THUMB])
                         else "ik"),
            }
            for b, p, d in bones
        ],
        "attachments": [
            {"name": s, "parent_bone": p, "position": export_frame(positions[s]),
             "role": "equipment_attach"}
            for s, p in ATTACHMENTS
        ],
        "landmarks_m": {k: v for k, v in f.items()},
        "skinned": weighted,
    }
    report_path = args.report or os.path.splitext(args.out)[0] + "_skeleton.json"
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(skeleton, handle, indent=2)

    print("BODY_RESULT " + json.dumps({
        "fit_family": args.fit_family,
        "skeleton_version": SKELETON_VERSION,
        "out": args.out,
        "bones": len(bones),
        "deform_bones": skeleton["deform_bone_count"],
        "attachments": len(ATTACHMENTS),
        "vertices": len(obj.data.vertices),
        "faces": len(obj.data.polygons),
        "skinned": weighted,
        "skeleton": report_path,
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
