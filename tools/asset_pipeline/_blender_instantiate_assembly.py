"""Place a kit assembly's pieces and export it as one GLB.

**STATUS: WORKING.** The `forge_shed` (6 x 6 m, 28 pieces, 2772 triangles) and the `longhouse`
(9 x 6 m, 38 pieces, 2628 triangles) both assemble into correct buildings, measured against their
declared footprints and confirmed by rendering them.

Four separate bugs had to be fixed to get there, which is worth recording because each one produced
a shape that looked plausible in the numbers:

  1. Objects arrive from the glTF importer in QUATERNION rotation mode, so writing `rotation_euler`
     did nothing at all. Every rotation was silently dropped, including the roof pitch.
  2. The importer bakes the Y-up conversion into the geometry, so re-applying it double-converts.
  3. An assembly `[x, y, z]` position has `y` as height and maps to a Blender location `(x, -z, y)`.
  4. The generator placed the roof panel centres at `half_d + ROOF_SLOPE/2 - 0.30`, which is 3.7 m
     from the centre of a 6 m building. A 2 m panel pitched at 32 degrees spans only 1.70 m
     horizontally, so the whole roof sat *outside* the walls - covering z 2.85..4.55 against a wall
     line at 2.91 - floating clear with the ridge wide open, and making a 6 m building measure 9.4 m
     of depth. That was `_make_kit_assemblies.py`'s bug, not this file's, and it is why the roof
     position is now computed along the roof plane from eave to ridge.

Two false alarms also cost time and are recorded so they are not re-chased. The wall piece renders
as triangular shards, and stripping every texture left the shards in place, which normally proves bad
geometry. It is not: `building_wall_stone` is deliberately about thirty small jittered stone blocks,
so it renders busy by design. And the "comb" look of the assembled walls is that same rubble texture
seen through the preview's flat lighting - with the material stripped the geometry reads cleanly as
four walls, a gable and a door gap.

What remains is appearance, not structure: the kit pieces render near-white and washed out, so a
timber hall currently looks like a paper model. The kit builder names materials
(`material_rubble_stone_wall`, `material_limestone_ashlar`), so whether those are bound to the
exported pieces is the next thing to check.

**Nothing architectural should go through the image-to-3D reconstructor.** The smithy concept is a
good, readable drawing of an open-fronted workshop; the reconstructed mesh is a jumbled mass of
intersecting planes. Both are quarantined in `assets/_superseded/reconstruction_failed/`, and the
batch request marks those two ids `"build": false` with their assembly named.

`kit_assemblies.json` declares the prototype's buildings as *data*: the `forge_shed` (Kera's smithy,
a 6 x 6 m stone forge shed holding the anvil and bellows) and the `longhouse` (a 9 x 6 m hall with a
south door, the communal building). Those declarations have existed since the modular kit was
authored and **nothing has ever turned them into a mesh**, so the two most important buildings in
Ashen Hollow have no asset at all.

Building them from the kit rather than reconstructing them from a concept is not just easier, it is
the only route that produces something usable. `_blender_build_kit.py` explains why: a modular kit
only works if every piece agrees on its interface to the millimetre - a wall must be exactly 3.0 m
so three span 9.0 m, and a door frame exactly 2.20 m so a 1.80 m Veth fits under it. A single-image
reconstructor cannot hold that, and demonstrably does not: the smithy concept is a good, readable
drawing of an open-fronted workshop, and the reconstructed mesh is a jumbled mass of intersecting
planes. Architectural elements are the one category where parametric assembly is required.

Pieces are kept as separate nodes rather than joined, so the result stays editable and a later pass
can swap one wall without rebuilding the building.

Run inside Blender:
    blender --background --factory-startup --python _blender_instantiate_assembly.py -- \
        --assembly forge_shed --out W:\\UNNAMED\\assets\\ready\\forge_shed
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector

ASSETS = r"W:\UNNAMED\assets"
MANIFEST = os.path.join(ASSETS, "manifests", "kit_assemblies.json")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True)
    parser.add_argument("--out", required=True)
    return parser.parse_args(argv)


def main():
    args = parse_args()

    with open(MANIFEST, encoding="utf-8") as handle:
        assemblies = json.load(handle)["assemblies"]
    if args.assembly not in assemblies:
        raise SystemExit(f"unknown assembly '{args.assembly}'; "
                         f"known: {sorted(assemblies)}")
    assembly = assemblies[args.assembly]
    pieces = assembly["pieces"]

    bpy.ops.wm.read_factory_settings(use_empty=True)

    placed = 0
    missing = []
    for index, piece in enumerate(pieces):
        asset = piece["asset"]
        source = os.path.join(ASSETS, "ready", asset, f"{asset}.glb")
        if not os.path.exists(source):
            missing.append(asset)
            continue
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=source)
        imported = [o for o in bpy.data.objects if o not in before]
        roots = [o for o in imported if o.parent is None]

        # Three things about the importer, all confirmed by probing a piece rather than assumed,
        # and all of which had to be right before a building stopped being a pile:
        #
        #   * The importer BAKES the Y-up conversion into the geometry. Objects arrive with an
        #     identity transform and their world dimensions already in Blender axes, so the
        #     conversion must not be re-applied.
        #   * Objects arrive in QUATERNION rotation mode, so writing `rotation_euler` does nothing
        #     at all. That is what silently dropped every rotation, including the roof pitch, and
        #     left two roofs lying flat - which is why the first builds measured 6.92 x 9.4 x 3.0
        #     for a building declared as 6 x 6 with a 2.6 m wall.
        #   * An assembly position is `[x, y, z]` with `y` as height, in the same frame the pieces
        #     are authored in, so it maps to a Blender location `(x, -z, y)`.
        x, y, z = piece["position"]
        yaw = math.radians(piece.get("rotation_y_deg", 0.0))
        # The generator's roof pitch is negated here. `_make_kit_assemblies.py` authors it as
        # `sz * -32.0` intending a gable, and read in the glTF frame the panel is authored in - a
        # 3.0 x 0.3 x 2.0 slab, thin in Y - that value tilts the eave edge *up* and the ridge edge
        # *down*, which is a valley. A rotation about X maps (y, z) to (y cos - z sin, y sin +
        # z cos), so a point at local +z rises when sin(theta) is negative; with theta = -32 the
        # far edge rises. Negating it puts the ridge high and the eaves low.
        pitch = math.radians(-piece.get("rotation_x_deg", 0.0))
        for root in roots:
            root.rotation_mode = "XYZ"
            root.rotation_euler = (pitch, 0.0, yaw)
            root.location = Vector((x, -z, y))
        placed += 1

    if missing:
        unique = sorted(set(missing))
        print(f"ASSEMBLY_RESULT {{\"ok\": false, \"missing_pieces\": {json.dumps(unique)}}}")
        return 1

    # Collapse the duplicated materials the repeated imports left behind.
    #
    # Importing the same piece twenty times gives Blender `MAT_building_roof_panel`,
    # `MAT_building_roof_panel.001` and so on - twenty separate materials that are identical and
    # reference the same three textures. A first longhouse exported 38 materials for 38 pieces
    # against only 9 images. Since the whole value of a modular kit is that an engine can batch and
    # instance repeated pieces, a unique material per piece defeats the kit entirely: 38 materials
    # means 38 draw calls for one building, and a street of twenty buildings means 760.
    #
    # The suffix Blender appends is the only thing distinguishing them, so the base name is the
    # identity. Materials that differ for a real reason do not share a base name and are untouched.
    canonical = {}
    for material in bpy.data.materials:
        base = material.name.split(".")[0]
        canonical.setdefault(base, material)
    remapped = 0
    for obj in bpy.data.objects:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            target = canonical[slot.material.name.split(".")[0]]
            if slot.material is not target:
                slot.material = target
                remapped += 1
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)

    # Bounds from the evaluated geometry, so the reported size is the assembled size.
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
    dims = high - low

    os.makedirs(args.out, exist_ok=True)
    out_path = os.path.join(args.out, f"{args.assembly}.glb")
    bpy.ops.export_scene.gltf(filepath=out_path, export_format="GLB", use_selection=False,
                              export_apply=False)

    # Collision, as one box per solid piece.
    #
    # A single convex hull is the wrong proxy here and not merely a coarse one: Ashen Hollow's two
    # buildings are "enterable interiors" (bible section 26), and a hull around the whole assembly
    # seals the doorway that the building exists to have. Boxing each piece keeps every opening open,
    # because an opening is a gap *between* pieces.
    #
    # `building_door_frame` is skipped outright. It is the piece that defines the entrance, so
    # boxing it would wall up the door with the very geometry meant to frame it. The kit's own
    # convention is box collision (`building_wall_timber` declares mode "box" with dimensions
    # 3.0 x 0.18 x 2.6), so this matches it rather than inventing a second scheme.
    solid = [obj for obj in bpy.data.objects
             if obj.type == "MESH" and "door_frame" not in obj.name]
    boxes = []
    for obj in solid:
        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        low_c = Vector((min(c[i] for c in corners) for i in range(3)))
        high_c = Vector((max(c[i] for c in corners) for i in range(3)))
        centre = (low_c + high_c) / 2.0
        size = high_c - low_c
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=centre)
        cube = bpy.context.active_object
        cube.scale = (max(size.x, 0.01), max(size.y, 0.01), max(size.z, 0.01))
        boxes.append(cube)

    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj not in boxes:
            obj.hide_set(True)
    collision_path = os.path.join(args.out, f"{args.assembly}_collision_box.glb")
    bpy.ops.export_scene.gltf(filepath=collision_path, export_format="GLB",
                              use_selection=False, export_apply=True,
                              use_visible=True)
    for cube in boxes:
        bpy.data.objects.remove(cube, do_unlink=True)

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    triangles = 0
    for obj in meshes:
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)

    # Write the meta the rest of the pipeline expects, so an assembly is a first-class asset rather
    # than something only this tool understands. The backfill pass adds the derived provenance
    # fields afterwards; these are the ones only the builder knows.
    meta = {
        "asset_id": args.assembly,
        "name": args.assembly,
        "category": "building",
        "source": "kit_assemblies.json",
        "assembly_of": sorted({piece["asset"] for piece in pieces}),
        "pieces_placed": placed,
        "target_size_m": round(max(dims.x, dims.y, dims.z), 3),
        "height_m": round(dims.z, 3),
        "base": {"triangles": triangles, "meshes": len(meshes)},
        "transform": {"dimensions": [round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)]},
        "collision": {"mode": "box", "per_piece": True, "pieces": len(boxes),
                      "note": ("One box per solid piece, door frame excluded so the entrance stays"
                               " open. A convex hull would seal it.")},
        "collision_status": "present",
        "modular_interface_version": "1.0",
        "license_notes": "First-party generated asset, assembled from the parametric modular kit.",
    }
    with open(os.path.join(args.out, f"{args.assembly}_meta.json"), "w", encoding="utf-8") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")

    print("ASSEMBLY_RESULT " + json.dumps({
        "ok": True,
        "assembly": args.assembly,
        "pieces_placed": placed,
        "objects": len(bpy.data.objects),
        "meshes": len(meshes),
        "triangles": triangles,
        "footprint_m": [round(dims.x, 3), round(dims.y, 3)], "height_m": round(dims.z, 3),
        "out": out_path,
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
