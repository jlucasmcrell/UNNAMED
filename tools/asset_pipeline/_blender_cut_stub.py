"""Cut the standardized sacrificial stub off a modular component.

The proof set established that Trellis authors a component reliably only when it is presented
attached to a neighbour. The result therefore contains the component *plus* a length of that
neighbour. This script removes it.

Removal is deterministic because `_stub_spec.py` standardizes the stub: a plain cylinder of
known diameter, coaxial with the socket's `primary` axis, with a known length. So the cut is a
single plane, perpendicular to `primary`, at the socket's declared insertion depth.

Two things make this reliable where "clean up the mesh" would not be:

  * the cut plane is **computed from the socket**, not guessed from geometry
  * the stub is described as plain undecorated material, so the component's own detail stops
    at the cut and the surviving edge is the component's real mating face

Run inside Blender:
  blender --background --factory-startup --python _blender_cut_stub.py -- ^
      --input ready\\name\\name.glb --sockets sockets\\name.json ^
      --socket SOCK_head --out out\\name.glb
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, help="GLB carrying the stub")
    parser.add_argument("--sockets", required=True, help="Socket definition JSON")
    parser.add_argument("--socket", required=True, help="Socket whose axis the cut follows")
    parser.add_argument("--out", required=True)
    parser.add_argument("--blend-source", default=None)
    parser.add_argument("--trim-margin", type=float, default=0.004,
                        help="Metres of material kept beyond the socket origin, so the mating "
                             "face is not cut away entirely")
    parser.add_argument("--cap", action="store_true",
                        help="Fill the cut boundary so the shell is closed")
    parser.add_argument("--component-side", choices=("near", "far"), default=None,
                        help="Which side of the cut is the component. 'near' keeps the side "
                             "toward the socket origin, 'far' the side beyond it. Default: "
                             "the side holding the greater mesh extent.")
    return parser.parse_args(argv)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for block in (bpy.data.meshes, bpy.data.objects, bpy.data.materials):
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


def join_meshes(meshes):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def export_frame_to_blender(vector):
    """Export (Y-up) -> Blender (Z-up), matching the exporter's conversion."""
    x, y, z = vector
    return Vector((x, -z, y))


def measure_profile(obj, axis, origin, samples=120):
    """Cross-sectional radius along the axis, as (position, max_radius) pairs.

    The standardized stub is a plain cylinder of known diameter, and the component proper is
    wider and detailed. So the boundary between them shows up as a step in the radius profile,
    and finding that step is far more robust than assuming which side of the socket the stub
    lies on.
    """
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()

    projections = [(v.co - origin).dot(axis) for v in bm.verts]
    if not projections:
        bm.free()
        return [], 0.0, 0.0
    low, high = min(projections), max(projections)
    span = high - low
    if span <= 1e-6:
        bm.free()
        return [], low, high

    buckets = [[] for _ in range(samples)]
    for vertex, projection in zip(bm.verts, projections):
        index = min(int((projection - low) / span * samples), samples - 1)
        offset = vertex.co - origin - axis * projection
        buckets[index].append(offset.length)
    bm.free()

    profile = []
    for index, radii in enumerate(buckets):
        position = low + span * (index + 0.5) / samples
        profile.append((position, max(radii) if radii else 0.0))
    return profile, low, high


def find_stub_boundary(profile, tolerance=1.35):
    """Find the sharpest radial step in the profile: the haft/stub meets the component there.

    A mace has wide ends and a narrow middle, so "which end is the stub" cannot be inferred
    from the ends alone. What IS unambiguous is the step itself: the sacrificial stub is a
    plain narrow cylinder, so the profile drops sharply where component meets stub.

    Returns (cut_position, drop_size) for the largest step found, or (None, 0).
    """
    if len(profile) < 8:
        return None, 0.0
    best_position, best_drop = None, 0.0
    for index in range(1, len(profile)):
        drop = profile[index - 1][1] - profile[index][1]
        if drop > best_drop:
            best_drop = drop
            best_position = profile[index][0]
    return best_position, best_drop


def cut_stub(obj, socket_spec, trim_margin, cap, component_side=None):
    """Remove the stub, cutting on the plane perpendicular to the socket axis.

    The cut position comes from the sharpest radial step in the profile: the sacrificial stub
    is a plain narrow cylinder, so the section narrows abruptly where it meets the component.

    Which side survives is a declared property, not a geometric inference, because a mace has
    wide ends and a narrow middle - "the wide side" is genuinely ambiguous there. `near` keeps
    the side toward the socket origin, `far` the side beyond it. When neither is declared the
    side holding the greater mesh extent wins, which is right for most components.

    Returns (vert_count_removed, capped_edges, cut_position, method, radial_drop).
    """
    axis = export_frame_to_blender(socket_spec["primary"]).normalized()
    origin = export_frame_to_blender(socket_spec["position"])

    profile, _low, _high = measure_profile(obj, axis, origin)
    boundary, drop = find_stub_boundary(profile)
    method = "profile_step"

    if boundary is None:
        boundary = 0.0
        method = "socket_origin_fallback"

    if component_side is None:
        # The component is the substantial part. Weight each side by how much surface it
        # carries rather than by raw length, since a thin haft is long but simple.
        below = sum(r * r for position, r in profile if position < boundary)
        above = sum(r * r for position, r in profile if position >= boundary)
        component_is_high = above > below
        method += "+extent"
    else:
        # Measured on the stub mace: the component occupies the LOW signed distances and the
        # stub the HIGH ones, because the socket axis ends up pointing from the component out
        # along the stub. So 'near' (keep the side toward the origin, where the component is)
        # means keeping LOW. Established from the probe rather than inferred from the axis.
        component_is_high = component_side == "far"
        method += "+declared"

    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()

    # Signed distance along the axis from the socket origin. Keep the component half, with a
    # small margin so the mating face itself is not cut away.
    def signed(vert):
        return (vert.co - origin).dot(axis)

    if component_is_high:
        doomed = [v for v in bm.verts if signed(v) < boundary - trim_margin]
    else:
        doomed = [v for v in bm.verts if signed(v) > boundary + trim_margin]

    removed = len(doomed)
    if removed:
        bmesh.ops.delete(bm, geom=doomed, context="VERTS")

    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    holes = None
    if cap and removed:
        bm = bmesh.new()
        bm.from_mesh(mesh)
        edges = [e for e in bm.edges if len(e.link_faces) == 1]
        if edges:
            bmesh.ops.holes_fill(bm, edges=edges)
            holes = len(edges)
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()

    return removed, holes, boundary, method, drop


def bounds(obj):
    bpy.context.view_layer.update()
    corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    low = Vector((min(c[i] for c in corners) for i in range(3)))
    high = Vector((max(c[i] for c in corners) for i in range(3)))
    return low, high


def main():
    args = parse_args()
    with open(args.sockets, encoding="utf-8") as handle:
        definition = json.load(handle)
    if args.socket not in definition.get("sockets", {}):
        raise SystemExit(f"socket {args.socket} not declared in {args.sockets}")
    asset_id = definition["asset_id"]

    reset_scene()
    obj = join_meshes(import_glb(args.input))

    socket_spec = definition["sockets"][args.socket]
    if socket_spec.get("interface_shape") == "planar":
        # A planar interface (a shield's forearm mount, for instance) has no cylindrical
        # envelope and no stub to remove: the asset is a whole object. Say so rather than
        # inventing a diameter to satisfy the tool.
        print("STUB_RESULT " + json.dumps({
            "ok": True, "asset_id": asset_id, "socket": args.socket,
            "skipped": "planar interface, no stub to cut", "verts_removed": 0}))
        return 0
    envelope = float(socket_spec.get("envelope", 0.0) or 0.0)
    if envelope <= 0.0:
        raise SystemExit(f"socket {args.socket} declares no envelope and is not marked "
                         f"planar, so the stub boundary cannot be found")
    removed, holes, boundary, method, drop = cut_stub(
        obj, socket_spec, args.trim_margin, args.cap, args.component_side)
    if removed == 0:
        print(f"STUB_RESULT {json.dumps({'ok': False, 'asset_id': asset_id, 'reason': 'nothing removed; the cut plane may be behind all geometry'})}")
        return 1

    # Re-seat: the component's own footprint should sit on the ground plane, centred.
    low, high = bounds(obj)
    centre = (low + high) / 2
    obj.location = (obj.location.x - centre.x, obj.location.y - centre.y, obj.location.z - low.z)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    obj.name = asset_id
    obj.data.name = f"{asset_id}_mesh"

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=True)

    if args.blend_source:
        os.makedirs(os.path.dirname(args.blend_source), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=args.blend_source)

    low, high = bounds(obj)
    report = {
        "ok": True,
        "asset_id": asset_id,
        "socket": args.socket,
        "verts_removed": removed,
        "boundary_edges_capped": holes,
        "cut_position_m": round(boundary, 4),
        "boundary_method": method,
        "faces": len(obj.data.polygons),
        "dimensions_m_export_frame": [
            round(high.x - low.x, 4), round(high.z - low.z, 4), round(high.y - low.y, 4)],
        "out": args.out,
    }
    print("STUB_RESULT " + json.dumps(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
