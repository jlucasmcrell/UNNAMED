"""Author the Otherreach modular building kit parametrically.

Why these are built rather than generated: a modular kit only works if every piece agrees on its
interface to the millimetre. A wall must be exactly 3.0 m so three of them span 9.0 m; a door frame
must be exactly 2.20 m tall so the 2.05 m door leaf and a 1.80 m Veth both fit under it. A
single-image reconstructor cannot guarantee any of that - the door asset came back as a 1.39 m
thick mass and needed a separate fitting pass to become usable at all. Architectural elements are
the one category where parametric authoring is not just easier but required.

Dimensions are real. The canonical Veth is 1.80 m tall with 0.42 m shoulders, so the door frame
opening is 2.20 m and every interior clearance exceeds the body.

Pieces are exported at a stable origin: X and Y are centred on the footprint and Z sits on the
ground, matching every other asset in the library, so two pieces butt together by placing one at
the other's edge rather than by offsetting for a pivot.

Usage:
    blender --background --factory-startup --python _blender_build_kit.py -- \
        --asset-id building_wall_timber --out <dir>
    python _build_kit.py --apply
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

# Every piece, with its exact dimensions in metres and the material it wears. Tile sizes come from
# the PBR materials already produced, so a wall and a floor repeat their textures consistently.
PIECES = {
    "building_wall_timber": {
        "note": "Timber-framed wall panel with plaster infill. 3.0 m module.",
        "material": "material_plaster_lath_wall",
        "lod_faces": "2000,700,200",
    },
    "building_wall_stone": {
        "note": "Rubble stone wall panel. 3.0 m module, thicker than the timber wall.",
        "material": "material_rubble_stone_wall",
        "lod_faces": "2000,700,200",
    },
    "building_roof_panel": {
        "note": "Slated roof panel, 3.0 m run by 2.0 m slope.",
        "material": "material_slate_roof_scale",
        "lod_faces": "2000,700,200",
    },
    "building_door_frame": {
        "note": "Door frame with a 1.10 x 2.20 m opening; takes the 2.05 m door leaf.",
        "material": "material_oak_plank_floor",
        "lod_faces": "1500,500,150",
    },
    "building_window_frame": {
        "note": "Window frame, 0.90 x 0.90 m opening, with a central mullion.",
        "material": "material_oak_plank_floor",
        "lod_faces": "1000,400,120",
    },
    "building_floor_planks": {
        "note": "Plank floor tile, 3.0 x 3.0 m.",
        "material": "material_oak_plank_floor",
        "lod_faces": "2000,700,200",
    },
    "building_step": {
        "note": "Single stair tread, 1.20 m wide, 0.30 m going, 0.18 m rise.",
        "material": "material_limestone_ashlar",
        "lod_faces": "600,250,80",
    },
    "building_post": {
        "note": "Square structural post, 0.15 m, 2.60 m tall.",
        "material": "material_oak_plank_floor",
        "lod_faces": "400,150,50",
    },
    "building_beam": {
        "note": "Structural beam, 3.0 m span, 0.15 x 0.15 m section.",
        "material": "material_oak_plank_floor",
        "lod_faces": "400,150,50",
    },
    "building_fence_panel": {
        "note": "Palisade fence panel, 2.40 m run, 1.10 m tall.",
        "material": "material_oak_plank_floor",
        "lod_faces": "1200,400,120",
    },
    "building_ruin_wall": {
        "note": "Broken stone wall fragment with a jagged top edge. Ruin kit.",
        "material": "material_rubble_stone_wall",
        "lod_faces": "1500,500,150",
    },
    "building_well": {
        "note": "Village well: stone ring, two posts and a peaked roof. ~2.2 m tall.",
        "material": "material_rubble_stone_wall",
        "lod_faces": "2500,900,250",
    },
    "building_road_segment": {
        "note": "Packed dirt road segment, 4.0 x 4.0 m, slightly cambered.",
        "material": "material_packed_dirt_ground",
        "lod_faces": "800,300,100",
    },
}


# Texel density per piece, taken from the PBR material each one wears so the kit repeats its
# textures at a consistent real-world rate.
TILE_SIZE = {
    "material_plaster_lath_wall": 2.5,
    "material_rubble_stone_wall": 3.0,
    "material_limestone_ashlar": 2.5,
    "material_slate_roof_scale": 2.0,
    "material_carved_timber": 2.0,
    "material_oak_plank_floor": 2.0,
    "material_packed_dirt_ground": 4.0,
}

# The modular standard says a component under 4k triangles gets no LOD chain, because reducing a
# 36-face wall three times costs more than it saves and produces three identical files.
LOD_MIN_FACES = 4000

# Collision: a building module gets a box proxy and nothing else.
COLLISION_POLICY = "box"


def add_box(verts, faces, centre, size, jitter=0.0, seed=0):
    """Axis-aligned box, optionally with its top corners jittered for a broken look."""
    cx, cy, cz = centre
    sx, sy, sz = (s / 2.0 for s in size)
    base = len(verts)
    corners = []
    rng = seed
    for dz in (-1, 1):
        for dy in (-1, 1):
            for dx in (-1, 1):
                jx = jy = jz = 0.0
                if jitter and dz > 0:
                    rng = (rng * 1103515245 + 12345) % 2147483648
                    jx = ((rng / 2147483648.0) - 0.5) * jitter
                    rng = (rng * 1103515245 + 12345) % 2147483648
                    jy = ((rng / 2147483648.0) - 0.5) * jitter
                    rng = (rng * 1103515245 + 12345) % 2147483648
                    jz = ((rng / 2147483648.0) - 0.5) * jitter
                corners.append((cx + dx * sx + jx, cy + dy * sy + jy, cz + dz * sz + jz))
    verts.extend(corners)
    # Winding matches the rest of the pipeline: outward-facing, single-sided.
    quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    for a, b, c, d in quads:
        faces.append((base + a, base + b, base + d, base + c))


def apply_box_uvs(mesh, tile_size):
    """Planar-project each face at a fixed texel density.

    Two reasons this is generated rather than unwrapped with Smart Project. First, a box assembly
    has no UVs at all until something makes them, and a kit piece that references a PBR material it
    cannot map is not textured no matter how good the material is. Second, projecting every face at
    1/tile_size gives every piece in the kit the SAME texel density, so a wall and a floor repeat
    their textures at the same real-world rate and seams between pieces line up. Smart Project
    would give each piece its own arbitrary scale and the kit would not match itself.
    """
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for polygon in mesh.polygons:
        normal = polygon.normal
        # Drop the axis the face most faces along, then project the remaining two.
        axis = max(range(3), key=lambda i: abs(normal[i]))
        u_axis, v_axis = [i for i in range(3) if i != axis]
        # A wall's horizontal run and a floor's span should repeat at the same rate, so the
        # horizontal axes divide by the tile size and vertical detail uses the same scale.
        for loop_index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv_layer.data[loop_index].uv = (
                vertex[u_axis] / tile_size,
                vertex[v_axis] / tile_size,
            )


def build_pbr_material(material, material_id, asset_root):
    """Wire the generated PBR maps into a Principled BSDF so the GLB is self-contained.

    A material NAME alone is not a material: the pack verifier rejects an asset with no embedded
    textures, and rightly, because a kit piece that references a texture living somewhere else is
    not usable on its own. The UVs are already scaled by the material's tile size, so one UV unit
    is one tile and no extra mapping node is needed.

    The node layout matters. Blender's glTF exporter only recognises metallic/roughness when the
    packed ORM image is split and routed to those specific sockets; a different arrangement exports
    the base colour and silently drops the rest.
    """
    directory = os.path.join(asset_root, "materials", material_id)
    base = os.path.join(directory, f"{material_id}_basecolor.png")
    normal = os.path.join(directory, f"{material_id}_normal.png")
    orm = os.path.join(directory, f"{material_id}_orm.png")
    if not (os.path.exists(base) and os.path.exists(normal) and os.path.exists(orm)):
        return False

    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)

    output = nodes.new("ShaderNodeOutputMaterial")
    output.location = (600, 0)
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    principled.location = (300, 0)
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])

    albedo = nodes.new("ShaderNodeTexImage")
    albedo.image = bpy.data.images.load(base)
    albedo.location = (-400, 250)
    links.new(albedo.outputs["Color"], principled.inputs["Base Color"])

    normal_tex = nodes.new("ShaderNodeTexImage")
    normal_tex.image = bpy.data.images.load(normal)
    normal_tex.image.colorspace_settings.name = "Non-Color"
    normal_tex.location = (-400, -50)
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.location = (-120, -50)
    links.new(normal_tex.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], principled.inputs["Normal"])

    orm_tex = nodes.new("ShaderNodeTexImage")
    orm_tex.image = bpy.data.images.load(orm)
    orm_tex.image.colorspace_settings.name = "Non-Color"
    orm_tex.location = (-400, -350)
    separate = nodes.new("ShaderNodeSeparateColor")
    separate.location = (-120, -350)
    links.new(orm_tex.outputs["Color"], separate.inputs["Color"])
    # glTF packs occlusion in R, roughness in G, metallic in B.
    links.new(separate.outputs["Green"], principled.inputs["Roughness"])
    links.new(separate.outputs["Blue"], principled.inputs["Metallic"])

    material["tile_size_m"] = TILE_SIZE.get(material_id, 2.0)
    return True


def mesh_from(verts, faces, name, material_name, tile_size, asset_root=None,
              material_id=None):
    mesh = bpy.data.meshes.new(f"{name}_mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.validate()
    mesh.update()
    apply_box_uvs(mesh, tile_size)

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)

    material = bpy.data.materials.new(material_name)
    material.use_backface_culling = True
    mesh.materials.append(material)
    if asset_root and material_id:
        build_pbr_material(material, material_id, asset_root)
    return obj


def build(asset_id):
    """Return (verts, faces, material_name) for one kit piece, at exact real dimensions."""
    v, f = [], []
    material = PIECES[asset_id]["material"]

    if asset_id == "building_wall_timber":
        # 3.0 m module, 2.60 m tall, 0.18 m thick. Four studs, two rails, infill panel.
        length, height, thick = 3.0, 2.60, 0.18
        stud = 0.14
        add_box(v, f, (0, 0, 0.09), (length, thick, 0.18))                 # sill
        add_box(v, f, (0, 0, height - 0.09), (length, thick, 0.18))        # head
        for x in (-length / 2 + stud / 2, 0.0, length / 2 - stud / 2):
            add_box(v, f, (x, 0, height / 2), (stud, thick, height - 0.36))  # studs
        # Infill sits slightly proud so it reads as daub over the frame.
        add_box(v, f, (0, 0, height / 2), (length - 2 * stud, thick * 0.7, height - 0.36))

    elif asset_id == "building_wall_stone":
        length, height, thick = 3.0, 2.60, 0.35
        rows = 7
        row_h = height / rows
        for row in range(rows):
            blocks = 5 if row % 2 == 0 else 4
            bw = length / blocks
            offset = 0.0 if row % 2 == 0 else bw / 2
            for col in range(blocks):
                x = -length / 2 + offset + col * bw + bw / 2
                # Staggered courses overhang the module unless the end block is pulled back, and
                # a 3.36 m wall does not butt against a 3.00 m one.
                half = bw * 0.97 / 2
                x = max(-length / 2 + half, min(length / 2 - half, x))
                add_box(v, f, (x, 0, row * row_h + row_h / 2),
                        (bw * 0.97, thick, row_h * 0.96), jitter=0.012, seed=row * 13 + col)

    elif asset_id == "building_roof_panel":
        # Exactly 3.0 m along the eave by 2.0 m up the slope. Courses step down in Z to lap over one
        # another, which is what sheds water; sizing each course with an overhang instead made the
        # panel 2.10 m and broke the module.
        run, slope, thick = 3.0, 2.0, 0.12
        courses = 7
        course_h = slope / courses
        lap = 0.03
        for index in range(courses):
            y = -slope / 2 + index * course_h + course_h / 2
            add_box(v, f, (0, y, thick / 2 + index * lap), (run, course_h, thick))

    elif asset_id == "building_door_frame":
        # Opening 1.10 x 2.20 m so the 2.05 m door leaf and a 1.80 m Veth both clear it.
        opening_w, opening_h, jamb, deep = 1.10, 2.20, 0.12, 0.20
        outer_w = opening_w + 2 * jamb
        add_box(v, f, (-(opening_w / 2 + jamb / 2), 0, opening_h / 2 + jamb),
                (jamb, deep, opening_h + 2 * jamb))
        add_box(v, f, ((opening_w / 2 + jamb / 2), 0, opening_h / 2 + jamb),
                (jamb, deep, opening_h + 2 * jamb))
        add_box(v, f, (0, 0, opening_h + jamb / 2), (outer_w, deep, jamb))

    elif asset_id == "building_window_frame":
        side, member, deep = 0.90, 0.10, 0.16
        outer = side + 2 * member
        for sx in (-1, 1):
            add_box(v, f, (sx * (side / 2 + member / 2), 0, side / 2 + member),
                    (member, deep, side + 2 * member))
        for sz in (0, 1):
            add_box(v, f, (0, 0, member / 2 + sz * (side + member)), (outer, deep, member))
        add_box(v, f, (0, 0, side / 2 + member), (member * 0.7, deep, side))   # mullion

    elif asset_id == "building_floor_planks":
        width, planks = 3.0, 12
        pw = width / planks
        for index in range(planks):
            x = -width / 2 + index * pw + pw / 2
            add_box(v, f, (x, 0, 0.025), (pw * 0.96, width, 0.05))

    elif asset_id == "building_step":
        add_box(v, f, (0, 0, 0.09), (1.20, 0.30, 0.18))
        add_box(v, f, (0, -0.165, 0.185), (1.20, 0.05, 0.02))   # nosing

    elif asset_id == "building_post":
        add_box(v, f, (0, 0, 1.30), (0.15, 0.15, 2.60))

    elif asset_id == "building_beam":
        add_box(v, f, (0, 0, 0.075), (3.0, 0.15, 0.15))

    elif asset_id == "building_fence_panel":
        run, height, pale = 2.40, 1.10, 0.09
        count = 18
        spacing = run / count
        for index in range(count):
            x = -run / 2 + index * spacing + spacing / 2
            add_box(v, f, (x, 0, height / 2 - 0.05), (pale * 0.75, 0.035, height - 0.10))
        for z in (0.30, 0.85):
            add_box(v, f, (0, -0.035, z), (run, 0.05, 0.07))
        for x in (-run / 2 + 0.075, run / 2 - 0.075):
            add_box(v, f, (x, 0, height / 2), (0.15, 0.15, height))

    elif asset_id == "building_ruin_wall":
        length, height, thick = 3.0, 1.60, 0.35
        rows = 5
        row_h = height / rows
        for row in range(rows):
            # Higher courses are progressively more broken away, giving a ruined silhouette. The
            # jagged silhouette comes from the shrinking span, not from jittering the depth, which
            # only pushed the wall past its declared thickness.
            shrink = row / rows * length * 0.55
            span = max(length - shrink, 0.6)
            blocks = max(int(span / 0.42), 2)
            bw = span / blocks
            for col in range(blocks):
                x = -span / 2 + col * bw + bw / 2
                add_box(v, f, (x, 0, row * row_h + row_h / 2),
                        (bw * 0.94, thick * 0.94, row_h * 0.92), jitter=0.02, seed=row * 7 + col)

    elif asset_id == "building_well":
        radius, wall_h, thick = 0.60, 0.85, 0.22
        segments = 14
        for index in range(segments):
            angle = 2 * math.pi * index / segments
            x = math.cos(angle) * (radius - thick / 2)
            y = math.sin(angle) * (radius - thick / 2)
            add_box(v, f, (x, y, wall_h / 2), (thick, thick, wall_h), jitter=0.02, seed=index)
        for sx in (-1, 1):
            add_box(v, f, (sx * (radius - 0.05), 0, wall_h + 0.85), (0.10, 0.10, 1.70))
        # Peaked roof over the well head.
        add_box(v, f, (0, -0.45, wall_h + 1.80), (radius * 2.2, 0.90, 0.07))
        add_box(v, f, (0, 0.45, wall_h + 1.80), (radius * 2.2, 0.90, 0.07))
        add_box(v, f, (0, 0, wall_h + 1.92), (radius * 2.4, 0.12, 0.10))

    elif asset_id == "building_road_segment":
        size, camber = 4.0, 0.06
        add_box(v, f, (0, 0, 0.02), (size, size, 0.04))
        # A shallow crown so the road is not a flat plane underfoot.
        add_box(v, f, (0, 0, 0.05), (size * 0.72, size * 0.72, 0.02 + camber))

    else:
        raise SystemExit(f"unknown kit piece {asset_id}")

    return v, f, material


def export_glb(objects, path):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True,
        export_apply=True, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=True)


def decimate(obj, face_cap):
    if face_cap and len(obj.data.polygons) > face_cap:
        for other in bpy.context.scene.objects:
            other.select_set(False)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new("LODDecimate", "DECIMATE")
        modifier.decimate_type = "COLLAPSE"
        modifier.ratio = max(face_cap / len(obj.data.polygons), 0.001)
        bpy.ops.object.modifier_apply(modifier=modifier.name)


def main():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--asset-id", required=True, choices=sorted(PIECES))
    parser.add_argument("--outdir", required=True)
    parser.add_argument("--assets-root", default=r"W:\UNNAMED\assets")
    args = parser.parse_args(argv)

    reset_scene()
    verts, faces, material = build(args.asset_id)
    obj = mesh_from(verts, faces, args.asset_id, f"MAT_{args.asset_id}",
                    TILE_SIZE.get(material, 2.0), args.assets_root, material)
    # X and Y centred on the footprint, base on the ground, so two pieces butt together by placing
    # one at the other's edge.
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lows = Vector((min(c[i] for c in corners) for i in range(3)))
    highs = Vector((max(c[i] for c in corners) for i in range(3)))
    obj.location = (-(lows.x + highs.x) / 2.0, -(lows.y + highs.y) / 2.0, -lows.z)
    for other in bpy.context.scene.objects:
        other.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    os.makedirs(args.outdir, exist_ok=True)
    dims = [round(highs[i] - lows[i], 4) for i in range(3)]
    base_path = os.path.join(args.outdir, f"{args.asset_id}.glb")
    export_glb([obj], base_path)

    base_faces = len(obj.data.polygons)

    # LOD chain only where the piece is big enough to justify one.
    lod_faces = [int(x) for x in PIECES[args.asset_id]["lod_faces"].split(",")]
    lod_policy = "none" if base_faces < LOD_MIN_FACES else "chain"
    lods = []
    if lod_policy == "chain":
        for index, cap in enumerate(lod_faces, start=1):
            copy = obj.copy()
            copy.data = obj.data.copy()
            name = f"{args.asset_id}_lod{index}"
            copy.name = name
            copy.data.name = f"{name}_mesh"
            bpy.context.collection.objects.link(copy)
            decimate(copy, cap)
            export_glb([copy], os.path.join(args.outdir, f"{name}.glb"))
            lods.append({"name": name, "faces": len(copy.data.polygons)})
            bpy.data.objects.remove(copy, do_unlink=True)

    # Building modules get a box proxy and nothing else.
    box_verts, box_faces = [], []
    add_box(box_verts, box_faces, (0.0, 0.0, dims[2] / 2.0),
            (dims[0], dims[1], dims[2]))
    box = mesh_from(box_verts, box_faces, f"{args.asset_id}_collision_box",
                    f"MAT_{args.asset_id}", TILE_SIZE.get(material, 2.0))
    export_glb([box], os.path.join(args.outdir, f"{args.asset_id}_collision_box.glb"))

    meta = {
        "asset_id": args.asset_id,
        "category": "building",
        "target_size_m": round(max(dims), 4),
        "transform": {"dimensions": dims, "rescaled_by": "parametric_kit"},
        "material": material,
        "tile_size_m": TILE_SIZE.get(material, 2.0),
        "uv": "planar box projection at the material's tile size, so every piece shares texel density",
        "lod_policy": lod_policy,
        "collision_policy": COLLISION_POLICY,
        "collision": {"box_dimensions": dims, "mode": "box"},
        "note": PIECES[args.asset_id]["note"],
        "authored": "parametric; exact dimensions are an interface, not an approximation",
    }
    with open(os.path.join(args.outdir, f"{args.asset_id}_meta.json"), "w",
              encoding="utf-8") as handle:
        json.dump(meta, handle, indent=2)
    bpy.data.objects.remove(box, do_unlink=True)

    print("KIT_RESULT " + json.dumps({
        "asset_id": args.asset_id,
        "dimensions_m": dims,
        "faces": base_faces,
        "vertices": len(obj.data.vertices),
        "material": material,
        "tile_size_m": TILE_SIZE.get(material, 2.0),
        "lod_policy": lod_policy,
        "lods": lods,
        "has_uvs": len(obj.data.uv_layers) > 0,
        "note": PIECES[args.asset_id]["note"],
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
