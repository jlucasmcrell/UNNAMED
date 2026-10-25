"""The Stopped Gate's keepers (docs/FIELD_RUINS.md): the gate tender and the Gate Warden, built on the golem's rig.

The golem (creature_golem_stan, ShyrAssets, CC BY 4.0) gives the skeleton and the six clips; its body is thrown away. Each bone gets
a mechanism of Firstfold iron instead: shingled plates over a core of works (pistons, axles, and gears at the joints), with violet
Otherfire burning in the seams between plates, in the helm's slit and in the core at the open back (the weak point). Every part is
weighted wholly to one bone, and the skeleton is scaled to the keeper's height, so the golem's clips drive it unchanged
(ArtLibrary.Retarget matches by bone name and scales root travel by height). The materials named otherfire_* are what the game flares
as the keeper winds up a blow (SkinnedCreature). The iron's maps are drawn here, so nothing else is read.

  blender --background --factory-startup --python tools/field_ruins/build_keepers.py -- --out D:/Otherreach_Depot/field_ruins_4k
"""
import math
import os
import sys

import bmesh
import bpy
import mathutils
import numpy as np

GOLEM = r"C:\Otherreach_Recovery\asset_work\creatures\ready_new\creature_golem_stan\creature_golem_stan.glb"
GOLEM_HEIGHT = 4.0
TEXTURE = 2048
VIOLET = (0.62, 0.22, 1.0)
KEEPERS = {
    # id: (height m, girth of the plates about each bone, spires on the back)
    "creature_gate_tender": (3.0, 0.8, 1),
    "creature_gate_warden": (8.0, 1.0, 3),
}
rng = np.random.default_rng(11)


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# --- the maps -----------------------------------------------------------------------------------------------------------------------

def waves(size, count, lo, hi, direction=None):
    """A tiling field of summed cosines. With a direction the wave vectors crowd round it, so the field streaks across it (grain)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size
    out = np.zeros((size, size), np.float32)
    for _ in range(count):
        m = int(rng.integers(lo, hi))
        if direction:
            a, b = direction[0] * m + int(rng.integers(-1, 2)), direction[1] * m + int(rng.integers(-1, 2))
        else:
            a, b = (int(v) for v in rng.integers(-hi, hi + 1, 2))
        out += np.cos(2 * np.pi * (a * x + b * y) + rng.uniform(0, 2 * np.pi)).astype(np.float32) / math.sqrt(max(m, 1))
    return (out - out.min()) / (out.max() - out.min())


def image(name, rgb, colour):
    img = bpy.data.images.new(name, rgb.shape[1], rgb.shape[0], alpha=False)
    img.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    rgba = np.concatenate([rgb, np.ones(rgb.shape[:2] + (1,), np.float32)], axis=2)
    img.pixels.foreach_set(rgba.ravel())
    img.pack()
    return img


def iron_maps():
    """Firstfold iron (WORLD_MATERIALS.md): dark, cool, its grain running across the light instead of with it; pitted and worn."""
    grain = waves(TEXTURE, 48, 6, 90, direction=(1, -2))
    mottle = waves(TEXTURE, 14, 1, 5)
    pits = rng.random((TEXTURE, TEXTURE), dtype=np.float32)
    worn = (pits > 0.992).astype(np.float32) * mottle
    tone = 0.34 + 0.07 * grain + 0.06 * mottle + 0.16 * worn
    iron = tone[..., None] * np.array([0.92, 0.95, 1.05], np.float32)
    # The works are the same iron kept moving: smoother, a little brighter, a faint warmth where it has run hot.
    works = (0.42 + 0.05 * grain + 0.06 * mottle)[..., None] * np.array([1.05, 0.98, 0.94], np.float32)
    height = grain * 0.6 + mottle * 0.4 - worn
    dx, dy = np.roll(height, -1, 1) - np.roll(height, 1, 1), np.roll(height, -1, 0) - np.roll(height, 1, 0)
    n = np.stack([-dx * 6, -dy * 6, np.ones_like(dx)], axis=2)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    normal = n * 0.5 + 0.5
    zero = np.zeros_like(grain)
    orm = lambda rough, metal: np.stack([np.ones_like(grain), rough, zero + metal], axis=2)
    return {
        "iron": (image("keeper_iron_basecolor", iron, True), image("keeper_iron_orm", orm(0.52 + 0.2 * mottle - 0.12 * grain + 0.2 * worn, 0.6), False)),
        "works": (image("keeper_works_basecolor", works, True), image("keeper_works_orm", orm(0.32 + 0.12 * mottle, 0.8), False)),
        "normal": image("keeper_iron_normal", normal, False),
    }


def materials(maps):
    out = {}
    for key in ("iron", "works"):
        mat = bpy.data.materials.new("keeper_" + key)
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes["Principled BSDF"]
        colour, orm = (nodes.new("ShaderNodeTexImage") for _ in range(2))
        colour.image, orm.image = maps[key]
        split = nodes.new("ShaderNodeSeparateColor")
        normal = nodes.new("ShaderNodeTexImage")
        normal.image = maps["normal"]
        bump = nodes.new("ShaderNodeNormalMap")
        links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
        links.new(orm.outputs["Color"], split.inputs["Color"])
        links.new(split.outputs["Green"], bsdf.inputs["Roughness"])
        links.new(split.outputs["Blue"], bsdf.inputs["Metallic"])
        links.new(normal.outputs["Color"], bump.inputs["Color"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
        out[key] = mat
    # Otherfire is violet and lights only itself: an emissive surface, never a light (no light rides a keeper).
    for key, strength in (("seam", 1.6), ("core", 3.0)):
        mat = bpy.data.materials.new("otherfire_" + key)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*VIOLET, 1)
        bsdf.inputs["Emission Color"].default_value = (*VIOLET, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
        bsdf.inputs["Roughness"].default_value = 0.3
        out[key] = mat
    return out


# --- the shapes (the golem's 4 m frame: Z up, facing -Y, so the back is +Y) -----------------------------------------------------------

def bevel(bm, width):
    bmesh.ops.bevel(bm, geom=bm.edges[:] + bm.verts[:], offset=width, segments=2, profile=0.5, affect="EDGES", clamp_overlap=True)


def chip(bm, amount):
    """Edge damage: knocks a few of the bevelled corners in, unevenly."""
    bm.normal_update()
    for v in bm.verts:
        if rng.random() < 0.18:
            v.co -= v.normal * float(rng.uniform(0.3, 1.0)) * amount


def box(size, at, taper=(1.0, 1.0), bev=0.03, damage=0.02):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = mathutils.Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        if v.co.z > 0:
            v.co.x *= taper[0]
            v.co.y *= taper[1]
    bevel(bm, bev)
    chip(bm, damage)
    bmesh.ops.translate(bm, vec=mathutils.Vector(at), verts=bm.verts)
    return bm


def shell(centre, r, thick, a0, a1, z0, z1, flare=0.0, segs=12, bev=0.02, damage=0.015):
    """A curved plate round a vertical axis through centre, from angle a0 to a1 (degrees; 0 is +X, -90 the front), wider at z1 by flare."""
    bm = bmesh.new()
    rows = []
    for i in range(segs + 1):
        t = math.radians(a0 + (a1 - a0) * i / segs)
        ring = []
        for z, rr in ((z0, r), (z1, r * (1 + flare))):
            for d in (0, thick):
                ring.append(bm.verts.new((centre[0] + (rr + d) * math.cos(t), centre[1] + (rr + d) * math.sin(t), z)))
        rows.append(ring)   # inner low, outer low, inner high, outer high
    for a, b in zip(rows, rows[1:]):
        for q in ((0, 2), (1, 3), (0, 1), (2, 3)):
            bm.faces.new((a[q[0]], b[q[0]], b[q[1]], a[q[1]]))
    for ring in (rows[0], rows[-1]):
        bm.faces.new((ring[0], ring[1], ring[3], ring[2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bevel(bm, bev)
    chip(bm, damage)
    return bm


def cylinder(r, length, at, axis="Z", segs=16):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=length)
    turn(bm, axis)
    bmesh.ops.translate(bm, vec=mathutils.Vector(at), verts=bm.verts)
    return bm


def gear(r, teeth, thick, at, axis="X"):
    """A toothed wheel: the works showing at a joint."""
    bm = bmesh.new()
    root, tip = r * 0.84, r
    profile = []
    for i in range(teeth):
        a = 2 * math.pi * i / teeth
        step = 2 * math.pi / teeth
        for frac, rr in ((0.0, root), (0.18, tip), (0.5, tip), (0.68, root)):
            profile.append((rr * math.cos(a + frac * step), rr * math.sin(a + frac * step)))
    front = [bm.verts.new((x, y, thick / 2)) for x, y in profile]
    back = [bm.verts.new((x, y, -thick / 2)) for x, y in profile]
    bm.faces.new(front)
    bm.faces.new(back)
    for i in range(len(profile)):
        j = (i + 1) % len(profile)
        bm.faces.new((front[i], front[j], back[j], back[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=r * 0.3, radius2=r * 0.3, depth=thick * 1.8)
    turn(bm, axis)
    bmesh.ops.translate(bm, vec=mathutils.Vector(at), verts=bm.verts)
    return bm


def spire(base, height, width, lean):
    """One of the broken spires on a keeper's back: the Needle, small."""
    bm = box((width, width * 0.7, height), (0, 0, height / 2), taper=(0.18, 0.25), bev=0.015, damage=0.02)
    bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=mathutils.Matrix.Rotation(math.radians(lean), 3, "X"), verts=bm.verts)
    bmesh.ops.translate(bm, vec=mathutils.Vector(base), verts=bm.verts)
    return bm


def turn(bm, axis):
    if axis == "X":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=mathutils.Matrix.Rotation(math.pi / 2, 3, "Y"), verts=bm.verts)
    elif axis == "Y":
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=mathutils.Matrix.Rotation(math.pi / 2, 3, "X"), verts=bm.verts)


# --- the mechanism ------------------------------------------------------------------------------------------------------------------

def side(s):
    """One arm and one leg; s is +1 for the left (+X), -1 for the right. Angles face outward on either side."""
    o = 0 if s > 0 else 180
    ax, lx = 1.33 * s, 0.44 * s
    m = lambda a: (o + a) if s > 0 else (o - a)
    out = lambda a0, a1: sorted((m(a0), m(a1)))
    return [
        # Shoulder: a gear where the arm hangs from the yoke, and a pauldron of three shingled plates with fire between them.
        ("UpperArm", "works", gear(0.3, 14, 0.12, (0.93 * s, 0.0, 3.71))),
        ("UpperArm", "works", cylinder(0.15, 0.7, (ax, 0.0, 3.4))),
        ("UpperArm", "iron", shell((ax, 0), 0.4, 0.09, *out(-120, 120), 3.62, 4.0, flare=-0.15)),
        ("UpperArm", "iron", box((0.62, 0.72, 0.16), (ax + 0.04 * s, 0.0, 4.04), taper=(0.7, 0.8), bev=0.05, damage=0.03)),
        ("UpperArm", "seam", shell((ax, 0), 0.41, 0.05, *out(-110, 110), 3.55, 3.63, segs=10, bev=0.01, damage=0)),
        ("UpperArm", "iron", shell((ax, 0), 0.38, 0.08, *out(-115, 115), 3.36, 3.6, flare=0.08)),
        ("UpperArm", "seam", shell((ax, 0), 0.37, 0.05, *out(-105, 105), 3.3, 3.37, segs=10, bev=0.01, damage=0)),
        ("UpperArm", "iron", shell((ax, 0), 0.33, 0.08, *out(-110, 110), 3.12, 3.34, flare=0.1)),
        # Elbow: the bend runs on a gear.
        ("UpperArm", "works", gear(0.22, 12, 0.1, (ax + 0.2 * s, 0.0, 3.05))),
        ("UpperArm", "works", gear(0.22, 12, 0.1, (ax - 0.2 * s, 0.0, 3.05))),
        # Forearm: the gauntlet that sweeps, banded, pistons down its back.
        ("LowerArm", "iron", box((0.62, 0.62, 0.9), (ax, 0.0, 2.5), taper=(0.78, 0.8), bev=0.05, damage=0.03)),
        ("LowerArm", "iron", shell((ax, 0), 0.4, 0.07, 0, 360, 2.22, 2.42, segs=20)),
        ("LowerArm", "seam", shell((ax, 0), 0.38, 0.04, 0, 360, 2.42, 2.47, segs=20, bev=0.01, damage=0)),
        ("LowerArm", "iron", shell((ax, 0), 0.36, 0.07, 0, 360, 2.47, 2.66, segs=20)),
        ("LowerArm", "works", cylinder(0.05, 0.85, (ax + 0.12 * s, 0.34, 2.55))),
        ("LowerArm", "works", cylinder(0.05, 0.85, (ax - 0.12 * s, 0.34, 2.55))),
        ("Hand", "works", cylinder(0.18, 0.4, (ax, 0.0, 1.98))),
        # Fist: a maul of iron, knuckles plated, fire in the knuckle gap.
        ("Hand", "iron", box((0.92, 0.92, 0.72), (ax, 0.0, 1.5), taper=(1.08, 1.08), bev=0.07, damage=0.04)),
        ("Hand", "seam", box((0.86, 0.06, 0.06), (ax, -0.47, 1.62), bev=0.01, damage=0)),
        ("Hand", "iron", box((0.95, 0.16, 0.26), (ax, -0.5, 1.4), bev=0.04, damage=0.03)),
        ("Hand", "iron", box((0.95, 0.16, 0.22), (ax, -0.48, 1.8), taper=(0.92, 0.8), bev=0.04, damage=0.03)),
        # Hip gear, then the thigh: a piston with a plate on its front and outer side.
        ("Leg", "works", gear(0.22, 12, 0.1, (lx + 0.17 * s, 0.0, 1.98))),
        ("Leg", "works", cylinder(0.14, 1.0, (lx, 0.0, 1.45))),
        ("Leg", "iron", shell((lx, 0.02), 0.25, 0.09, *out(-170, 30), 1.12, 1.88, flare=0.12)),
        ("Leg", "works", gear(0.18, 10, 0.09, (lx + 0.2 * s, 0.0, 1.0))),
        ("Leg", "works", gear(0.18, 10, 0.09, (lx - 0.2 * s, 0.0, 1.0))),
        # Shin: a greave with a knee cop and a fire seam under it.
        ("LowerLeg", "works", cylinder(0.1, 0.72, (lx, 0.02, 0.6))),
        ("LowerLeg", "iron", shell((lx, 0.02), 0.18, 0.08, *out(-200, 20), 0.3, 0.88, flare=0.25)),
        ("LowerLeg", "seam", shell((lx, 0.02), 0.2, 0.04, *out(-160, -20), 0.88, 0.93, segs=8, bev=0.01, damage=0)),
        ("LowerLeg", "iron", box((0.32, 0.18, 0.24), (lx, -0.22, 0.98), taper=(0.7, 0.9), bev=0.04)),
        # Foot: a wedge sabaton.
        ("Feet", "iron", box((0.52, 0.66, 0.26), (lx, -0.04, 0.13), taper=(0.9, 0.6), bev=0.04, damage=0.03)),
        ("Feet", "iron", box((0.48, 0.22, 0.14), (lx, -0.36, 0.07), taper=(0.85, 0.6), bev=0.03)),
    ]


def mechanism(spires):
    parts = [
        # Pelvis: a block of works, tassets hanging round its front and sides.
        ("Center", "works", box((0.9, 0.55, 0.42), (0, 0.02, 1.95), bev=0.05)),
        ("Center", "iron", shell((0, 0.05), 0.5, 0.07, -150, -95, 1.62, 2.12, flare=-0.12, segs=6)),
        ("Center", "iron", shell((0, 0.05), 0.5, 0.07, -85, -30, 1.62, 2.12, flare=-0.12, segs=6)),
        ("Center", "seam", shell((0, 0.05), 0.47, 0.04, -150, -30, 2.1, 2.16, segs=10, bev=0.01, damage=0)),
        # Chest: a spine of works, a breastplate over a belly plate with fire in the joint, flanks, and an open back.
        ("Body", "works", cylinder(0.24, 1.45, (0, 0.2, 2.95))),
        ("Body", "iron", shell((0, 0.12), 0.6, 0.11, -165, -15, 2.86, 3.72, flare=0.1)),
        ("Body", "seam", shell((0, 0.12), 0.58, 0.05, -160, -20, 2.8, 2.87, bev=0.01, damage=0)),
        ("Body", "iron", shell((0, 0.12), 0.52, 0.1, -155, -25, 2.24, 2.82, flare=0.12)),
        ("Body", "iron", shell((0, 0.12), 0.66, 0.09, -20, 55, 2.45, 3.58, flare=0.05, segs=8)),
        ("Body", "iron", shell((0, 0.12), 0.66, 0.09, 125, 200, 2.45, 3.58, flare=0.05, segs=8)),
        ("Body", "seam", shell((0, 0.12), 0.64, 0.05, -17, -13, 2.5, 3.55, segs=2, bev=0.008, damage=0)),
        ("Body", "seam", shell((0, 0.12), 0.64, 0.05, 193, 197, 2.5, 3.55, segs=2, bev=0.008, damage=0)),
        # The yoke the arms hang from, and the back's ribs framing the works round the core.
        ("Body", "iron", box((1.95, 0.85, 0.24), (0, 0.08, 3.74), taper=(0.92, 0.9), bev=0.06, damage=0.03)),
        ("Body", "iron", box((0.16, 0.2, 1.2), (0.55, 0.62, 3.0), bev=0.03)),
        ("Body", "iron", box((0.16, 0.2, 1.2), (-0.55, 0.62, 3.0), bev=0.03)),
        ("Body", "works", gear(0.34, 16, 0.1, (0.24, 0.6, 3.15), axis="Y")),
        ("Body", "works", gear(0.25, 12, 0.1, (-0.24, 0.62, 2.72), axis="Y")),
        ("Body", "works", gear(0.17, 10, 0.08, (-0.08, 0.66, 3.42), axis="Y")),
        ("Body", "core", cylinder(0.2, 0.3, (0.0, 0.52, 2.9), axis="Y", segs=24)),
        # Helm: sunk forward between the shoulders, a brow over one violet slit.
        ("Head", "works", cylinder(0.16, 0.3, (0, -0.25, 3.82))),
        ("Head", "iron", box((0.58, 0.66, 0.46), (0, -0.4, 4.0), taper=(0.75, 0.82), bev=0.05, damage=0.03)),
        ("Head", "seam", box((0.4, 0.05, 0.05), (0, -0.74, 4.0), bev=0.01, damage=0)),
        ("Head", "iron", box((0.66, 0.24, 0.12), (0, -0.66, 4.1), taper=(0.9, 0.7), bev=0.03)),
        ("Head", "iron", box((0.62, 0.3, 0.16), (0, -0.62, 3.82), taper=(1.0, 1.1), bev=0.03)),
    ]
    # The spires: the tallest in the middle, the outer two leaning out, all leaning back.
    for i in range(spires):
        x = (i - (spires - 1) / 2) * 0.48
        tall = 1.3 if x == 0 else 0.85
        spike = spire((x, 0.4, 3.78), tall, 0.24, -18)
        bmesh.ops.rotate(spike, cent=(x, 0.4, 3.78), matrix=mathutils.Matrix.Rotation(math.radians(-14 * x / 0.48), 3, "Y"), verts=spike.verts)
        parts.append(("Body", "iron", spike))
    for s in (1, -1):
        parts += [(bone + ("_L" if s > 0 else "_R"), mat, bm) for bone, mat, bm in side(s)]
    return parts


def build(asset_id, height, girth, spires, mats, out):
    imported = []
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=GOLEM)
    imported = [o for o in bpy.data.objects if o not in before]
    rig = next(o for o in imported if o.type == "ARMATURE")
    for o in imported:
        if o.type == "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    objs = []
    for bone, mat, bm in mechanism(spires):
        # A slimmer keeper is the same mechanism drawn in about each bone's own vertical axis.
        axis = rig.matrix_world @ rig.data.bones[bone].head_local
        for v in bm.verts:
            v.co.x = axis.x + (v.co.x - axis.x) * girth
            v.co.y = axis.y + (v.co.y - axis.y) * girth
        mesh = bpy.data.meshes.new(bone)
        bm.to_mesh(mesh)
        bm.free()
        mesh.materials.append(mats[mat])
        ob = bpy.data.objects.new(bone, mesh)
        bpy.context.scene.collection.objects.link(ob)
        ob.vertex_groups.new(name=bone).add(range(len(mesh.vertices)), 1.0, "REPLACE")
        objs.append(ob)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    keeper = bpy.context.view_layer.objects.active
    keeper.name = keeper.data.name = asset_id
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=1.2)
    bpy.ops.object.mode_set(mode="OBJECT")
    keeper.parent = rig
    mod = keeper.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    # The keeper's height, baked into the skeleton's rest (the clips retarget relative to rest; travel scales with height).
    s = height / GOLEM_HEIGHT
    rig.scale = (s, s, s)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    keeper.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    rig.name = asset_id + "_rig"
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    folder = os.path.join(out, "rigged", asset_id)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, asset_id + "_rigged.glb")
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    keeper.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_animations=False,
                              export_image_format="JPEG", export_jpeg_quality=95, export_yup=True)
    dims = keeper.dimensions
    print(f"KEEPER {asset_id} {path} {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} m, {len(keeper.data.polygons)} faces")
    bpy.data.objects.remove(keeper, do_unlink=True)
    bpy.data.objects.remove(rig, do_unlink=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = argv[argv.index("--out") + 1] if "--out" in argv else r"D:\Otherreach_Depot\field_ruins_4k"
    fresh()
    mats = materials(iron_maps())
    for asset_id, (height, girth, spires) in KEEPERS.items():
        build(asset_id, height, girth, spires, mats, out)


main()
