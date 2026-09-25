"""Standard review render: four comparable views of one GLB, a 2x2 contact sheet and a JSON record.

Every asset goes through the same camera, light, ground and scale furniture, so two sheets can be
laid side by side and compared. The views are fixed in the glTF frame the game uses (+Y up, model
forward +Z; Blender's importer turns that into Z up, forward -Y):

  front   orthographic, camera on the +Z side looking at the model's +Z face, 8 deg above level
  right   orthographic, camera on -X: the model's own right side when it faces +Z, 8 deg above level
  back    orthographic, camera on the -Z side, 8 deg above level
  34high  perspective 50 mm, front-right (between +Z and -X), 35 deg above level

The three orthographic views share one ortho scale, so a metre is the same number of pixels in all
three (px_per_m in the JSON). The ground plane sits at world height 0 (glTF y = 0), not at the
model's lowest point, so a floating or sinking model shows against it: a yellow floor line runs
through the footprint at the depth of the lowest point (a floating model shows a gap above it), and
any surface below ground is painted orange and seen through the slightly transparent floor. A
banded scale post (10 bands) stands beside the model with an up arrow on top, and a small axis
triad lies at its foot: green = +Y up, blue = +Z forward, red = +X. The 3/4 view has its own post,
scaled to the model's height, and frames all of it. The three-point light rig turns
with the camera, so every view is lit the same way relative to the viewer. Colour management is
Standard, not AgX, so albedo reads as albedo (an 18% grey card facing the front camera renders at
linear 0.18).

Framing uses the evaluated world-space vertices (armatures in rest pose), not object boxes. The
glTF importer's 1 m bone-shape icosphere is disabled, and anything in a glTF_not_exported
collection is ignored.

Optional --passes (comma list, or "all"):
  normals   front faces blue, back faces red (lit, so the shape still reads)
  wire      clay shading with a 1 px triangle wireframe
  detached  mesh welded by position; islands not in contact with the main body shown magenta
The island analysis behind "detached" always runs, so the JSON always has the counts. Each pass
sheet carries its own legend. The JSON also records how the model stands: "lean" (tilt of the
dominant flat faces, the _asset_qa estimator and threshold) and "contact" (share of the footprint
within 1 cm of the ground, and the hull of that contact as a share of the footprint's hull), and per
view whether the scale post is fully in frame.

Usage:
  blender --background --factory-startup --python _review_render.py -- ^
      --input <file.glb> --out <dir> [--name <id>] [--size 1024] [--passes normals,wire,detached]

Writes <out>/<id>_front|right|back|34high.jpg (JPEG q95, full size), <id>_sheet.jpg, per pass
<id>_<pass>_<view>.jpg and <id>_<pass>_sheet.jpg, and <id>_review.json. The last stdout line is
"REVIEW_RESULT <json path>". --offset-z moves the model up (or down) before rendering; it exists to
prove the gate shows floating and sinking, and is recorded in the JSON.
"""
import argparse
import hashlib
import json
import math
import os
import sys
import tempfile
import time

import bpy
import bmesh
import blf
import imbuf
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

T_START = time.time()

# name, azimuth (deg, 0 = camera on glTF +Z), elevation (deg), orthographic, sheet label
VIEWS = (
    ("front", 0.0, 8.0, True, "FRONT  +Z face  ortho, 8 deg up"),
    ("right", 270.0, 8.0, True, "RIGHT  model's right (-X)  ortho, 8 deg up"),
    ("back", 180.0, 8.0, True, "BACK  -Z face  ortho, 8 deg up"),
    ("34high", 315.0, 35.0, False, "3/4 HIGH  front-right  50 mm, 35 deg up"),
)
PASSES = ("normals", "wire", "detached")

# Key / fill / rim, azimuth relative to the camera, small sources (0.35R) so specular highlights
# stay smaller than the surfaces that return them. Total power is measured, not assumed: with the
# 355 * R^2 of _blender_preview.py an 18% grey card facing the front camera rendered at linear 0.257
# and an 80% card clipped, so the total is scaled by 0.70 to put the 18% card at about 0.18.
LIGHTS = (("key", -60.0, 45.0, 0.52), ("fill", 60.0, 25.0, 0.21), ("rim", 170.0, 70.0, 0.27))
LIGHT_TOTAL = 250.0
LIGHT_SIZE = 0.35

BACKGROUND = 0.05            # linear, achromatic
GROUND_ALBEDO = (0.10, 0.13)
GROUND_SEE_THROUGH = 0.45
CLAY_ALBEDO = 0.42
ORTHO_MARGIN = 1.10          # ortho_scale = margin x the largest projected extent of the three views
PERSP_FILL = 0.92            # 3/4 view: content half-extent <= 92% of the half-frame
LENS_MM = 50.0
SENSOR_MM = 36.0
NICE = (0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 50.0, 100.0)
# Contact tolerance, as a fraction of the model's largest dimension. Measured on the Pixal3D test
# set: after welding, the pieces of one reconstruction sit 0.0000-0.0039 x maxdim apart (spear head
# and shaft ~5 mm on 2.2 m, barrel hoops up to 3.3 mm on 0.85 m), so 0.008 leaves 2x headroom and
# still flags a chip floating a centimetre off a 1 m prop.
DETACH_TOL_FRACTION = 0.008
SAMPLE_CAP = 3_000_000       # edge points for the contact test; the spacing grows past this
# Support: the share of the footprint (cells a vertical ray hits) whose lowest surface is within
# CONTACT_TOL_M of the ground at y = 0, on a grid of CONTACT_GRID cells across the longer side.
CONTACT_TOL_M = 0.01
CONTACT_GRID = 200

ESTIMATOR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_asset_qa.py")
qa = None  # _asset_qa, loaded read-only in main(): the flat-face lean estimator and its threshold


def load_estimator(path):
    """Import _asset_qa from `path` without writing anything next to it.

    Blender's Python has no scipy. The functions used here do not need it, so while the module loads,
    any scipy import resolves to a stand-in whose attributes raise only if called.
    """
    import importlib.abc
    import importlib.util
    import types
    sys.dont_write_bytecode = True

    def unavailable(*_args, **_kwargs):
        raise RuntimeError("scipy is not available in Blender's Python")

    class ScipyStandIn(importlib.abc.MetaPathFinder, importlib.abc.Loader):
        def find_spec(self, name, _path, _target=None):
            if name == "scipy" or name.startswith("scipy."):
                return importlib.util.spec_from_loader(name, self, is_package=True)
            return None

        def create_module(self, spec):
            module = types.ModuleType(spec.name)
            module.__path__ = []
            module.__getattr__ = lambda _attr: unavailable
            return module

        def exec_module(self, module):
            pass

    stand_in = None
    if importlib.util.find_spec("scipy") is None:
        stand_in = ScipyStandIn()
        sys.meta_path.insert(0, stand_in)
    try:
        spec = importlib.util.spec_from_file_location("_asset_qa", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    finally:
        if stand_in:
            sys.meta_path.remove(stand_in)
            for name in [n for n in sys.modules if n == "scipy" or n.startswith("scipy.")]:
                del sys.modules[name]
    return module


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--out", required=True, help="output directory")
    parser.add_argument("--name", default=None, help="asset id used in file names (default: file stem)")
    parser.add_argument("--size", type=int, default=1024)
    parser.add_argument("--passes", default="", help="comma list of normals,wire,detached or 'all'")
    parser.add_argument("--samples", type=int, default=64)
    parser.add_argument("--estimator", default=ESTIMATOR,
                        help="_asset_qa.py to take the lean estimator from (the batch passes a snapshot)")
    parser.add_argument("--offset-z", type=float, default=0.0,
                        help="self-test only: move the model up (+) or down (-) by this many metres")
    args = parser.parse_args(argv)
    raw = [p.strip() for p in args.passes.split(",") if p.strip()]
    args.passes = list(PASSES) if raw == ["all"] else raw
    bad = [p for p in args.passes if p not in PASSES]
    if bad:
        parser.error(f"unknown pass(es) {bad}; choose from {PASSES}")
    return args


# --------------------------------------------------------------------------------------- import
def import_asset(path, offset_z):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path, disable_bone_shape=True)
    scene = bpy.context.scene
    ignored = set()
    for coll in bpy.data.collections:
        if coll.name.startswith("glTF_not_exported"):
            ignored.update(coll.all_objects)
    for obj in ignored:
        obj.hide_render = True
    for obj in scene.objects:
        if obj.type in ("LIGHT", "CAMERA"):
            obj.hide_render = True
    armatures = [o for o in scene.objects if o.type == "ARMATURE" and o not in ignored]
    for arm in armatures:
        arm.data.pose_position = "REST"
    meshes = [o for o in scene.objects if o.type == "MESH" and o not in ignored]
    if not meshes:
        raise SystemExit(f"no mesh objects in {path}")
    if offset_z:
        for obj in scene.objects:
            if obj.parent is None and obj not in ignored:
                obj.location.z += offset_z
    bpy.context.view_layer.update()
    return meshes, armatures, len(ignored)


def gather_geometry(meshes):
    """World-space vertices, vertex normals and triangles of the evaluated meshes (armature deform included)."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    verts, normals, tris, offset = [], [], [], 0
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        count = len(mesh.vertices)
        if count:
            co = np.empty(count * 3, np.float32)
            mesh.vertices.foreach_get("co", co)
            matrix = np.array(evaluated.matrix_world, dtype=np.float64)
            co = co.reshape(-1, 3).astype(np.float64) @ matrix[:3, :3].T + matrix[:3, 3]
            nor = np.empty(count * 3, np.float32)
            mesh.vertex_normals.foreach_get("vector", nor)
            nor = nor.reshape(-1, 3).astype(np.float64) @ np.linalg.inv(matrix[:3, :3])
            nor /= np.maximum(np.linalg.norm(nor, axis=1, keepdims=True), 1e-12)
            normals.append(nor)
            mesh.calc_loop_triangles()
            tri = np.empty(len(mesh.loop_triangles) * 3, np.int32)
            mesh.loop_triangles.foreach_get("vertices", tri)
            verts.append(co)
            tris.append(tri.reshape(-1, 3).astype(np.int64) + offset)
            offset += count
        evaluated.to_mesh_clear()
    if not verts:
        raise SystemExit("meshes have no vertices")
    return np.concatenate(verts), np.concatenate(normals), np.concatenate(tris)


# ------------------------------------------------------------------------------ island analysis
def connected_components(count, edges):
    """Union-find by repeated hooking and pointer jumping; returns a root id per element."""
    parent = np.arange(count)
    if len(edges) == 0:
        return parent
    while True:
        a = parent[edges[:, 0]]
        b = parent[edges[:, 1]]
        differ = a != b
        if not differ.any():
            return parent
        low = np.minimum(a[differ], b[differ])
        high = np.maximum(a[differ], b[differ])
        np.minimum.at(parent, high, low)
        while True:
            jumped = parent[parent]
            if np.array_equal(jumped, parent):
                break
            parent = jumped


def surface_samples(welded_verts, welded_tris, tri_island, spacing):
    """Test points on every island's surface: its vertices plus points along each edge at most `spacing` apart.

    The closest approach of two triangles is always vertex-to-triangle or edge-to-edge, so testing
    these points against the other island's triangles finds every vertex-to-face contact exactly and
    every edge-to-edge contact to within spacing / 2. Returns (points, island per point, spacing used).
    """
    vert_island = np.full(len(welded_verts), -1, np.int64)
    vert_island[welded_tris.ravel()] = np.repeat(tri_island, 3)
    used = np.flatnonzero(vert_island >= 0)
    edges = np.sort(np.concatenate([welded_tris[:, [0, 1]], welded_tris[:, [1, 2]], welded_tris[:, [2, 0]]]), axis=1)
    edges, first = np.unique(edges, axis=0, return_index=True)
    edge_island = np.tile(tri_island, 3)[first]
    start = welded_verts[edges[:, 0]]
    step = welded_verts[edges[:, 1]] - start
    length = np.linalg.norm(step, axis=1)
    while True:
        inner = np.maximum(np.ceil(length / spacing).astype(np.int64) - 1, 0)
        if inner.sum() <= SAMPLE_CAP:
            break
        spacing *= 1.5
    owner = np.repeat(np.arange(len(edges)), inner)
    rank = np.arange(len(owner)) - np.repeat(np.cumsum(inner) - inner, inner) + 1
    points = start[owner] + step[owner] * (rank / (inner[owner] + 1))[:, None]
    return (np.concatenate([welded_verts[used], points]),
            np.concatenate([vert_island[used], edge_island[owner]]), spacing)


def first_hit(bvh, points, tol):
    for point in points.tolist():
        if bvh.find_nearest(point, tol)[0] is not None:
            return True
    return False


def island_analysis(verts, tris, maxdim):
    """Weld by position, split into islands, and find islands not in contact with the main body.

    The LOD0 meshes are split into thousands of UV-chart islands; welding coincident vertices first
    leaves only true geometric pieces. Islands are built from triangles only, so a loose vertex or a
    zero-area triangle never becomes an island. Two islands are in contact when their surfaces come
    within `tol`: their triangles intersect (BVH triangle overlap), or a vertex or edge point of one
    lies within `tol` of the other's triangles (BVH nearest, both directions). An island is attached
    when it is in contact with an attached island, starting from the largest island by area.
    Everything else is detached, including pieces that touch only each other.
    """
    weld_eps = max(maxdim, 1e-3) * 1e-6
    quantised = np.round(verts / weld_eps).astype(np.int64)
    _, first, inverse = np.unique(quantised, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.reshape(-1)
    welded_verts = verts[first]
    welded_tris = inverse[tris]
    a, b, c = (welded_verts[welded_tris[:, i]] for i in range(3))
    tri_area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    # Collapsed by the weld (repeated index) or collinear: no surface, so no island of its own.
    keep = tri_area > weld_eps * weld_eps
    welded_tris, tri_area = welded_tris[keep], tri_area[keep]

    edges = np.concatenate([welded_tris[:, [0, 1]], welded_tris[:, [1, 2]]])
    roots = connected_components(len(welded_verts), edges)
    _, tri_island = np.unique(roots[welded_tris[:, 0]], return_inverse=True)
    tri_island = tri_island.reshape(-1)
    island_count = int(tri_island.max()) + 1 if len(tri_island) else 0
    island_area = np.bincount(tri_island, weights=tri_area, minlength=island_count)
    main = int(np.argmax(island_area)) if island_count else 0

    tol = DETACH_TOL_FRACTION * maxdim
    spacing = tol / 2
    pairs = set()

    def add(p, q):
        pairs.add((min(p, q), max(p, q)))

    if island_count > 1:
        points, point_island, spacing = surface_samples(welded_verts, welded_tris, tri_island, spacing)
        every = welded_verts.tolist()
        main_faces = tri_island == main
        other_faces = np.flatnonzero(~main_faces)
        bvh_main = BVHTree.FromPolygons(every, welded_tris[main_faces].tolist(), all_triangles=True)
        bvh_other = BVHTree.FromPolygons(every, welded_tris[other_faces].tolist(), all_triangles=True)
        for i, _j in bvh_other.overlap(bvh_main):
            add(int(tri_island[other_faces[i]]), main)
        for i, j in bvh_other.overlap(bvh_other):
            p, q = int(tri_island[other_faces[i]]), int(tri_island[other_faces[j]])
            if p != q:
                add(p, q)

        tri_lo = np.minimum(np.minimum(a[keep], b[keep]), c[keep])
        tri_hi = np.maximum(np.maximum(a[keep], b[keep]), c[keep])
        box_lo = np.full((island_count, 3), np.inf)
        box_hi = np.full((island_count, 3), -np.inf)
        np.minimum.at(box_lo, tri_island, tri_lo)
        np.maximum.at(box_hi, tri_island, tri_hi)
        by_x = np.argsort(points[:, 0], kind="stable")
        xs = points[by_x, 0]

        def near_box(island):
            lo, hi = box_lo[island] - tol, box_hi[island] + tol
            span = by_x[np.searchsorted(xs, lo[0]):np.searchsorted(xs, hi[0], side="right")]
            inside = np.all((points[span] >= lo) & (points[span] <= hi), axis=1)
            return span[inside]

        face_order = np.argsort(tri_island, kind="stable")
        face_bounds = np.searchsorted(tri_island[face_order], np.arange(island_count + 1))
        near_main = near_box(main)
        near_main = near_main[np.argsort(point_island[near_main], kind="stable")]
        main_bounds = np.searchsorted(point_island[near_main], np.arange(island_count + 1))
        for island in range(island_count):
            if island == main:
                continue
            # Points of every other island (the main body included) near this island's triangles.
            faces = welded_tris[face_order[face_bounds[island]:face_bounds[island + 1]]]
            local, remap = np.unique(faces, return_inverse=True)
            bvh = BVHTree.FromPolygons(welded_verts[local].tolist(), remap.reshape(-1, 3).tolist(),
                                       all_triangles=True)
            near = near_box(island)
            near = near[point_island[near] != island]
            near = near[np.argsort(point_island[near], kind="stable")]
            owners, starts = np.unique(point_island[near], return_index=True)
            for owner, lo, hi in zip(owners.tolist(), starts, list(starts[1:]) + [len(near)]):
                if (min(owner, island), max(owner, island)) not in pairs and first_hit(bvh, points[near[lo:hi]], tol):
                    add(owner, island)
            # This island's own points near the main body's triangles.
            if (min(main, island), max(main, island)) not in pairs:
                mine = near_main[main_bounds[island]:main_bounds[island + 1]]
                if first_hit(bvh_main, points[mine], tol):
                    add(island, main)

    group = connected_components(island_count, np.array(sorted(pairs), dtype=np.int64).reshape(-1, 2))
    detached_island = group != group[main] if island_count else np.zeros(0, bool)
    detached_groups = len(set(group[detached_island].tolist()))
    total_area = float(island_area.sum()) or 1.0
    return {
        "weld_eps_m": weld_eps,
        "tolerance_m": tol,
        "contact_test": "triangle overlap + vertex/edge points vs triangles (BVH nearest), both directions",
        "edge_sample_spacing_m": spacing,
        "welded_vertices": int(len(welded_verts)),
        "loose_vertices_ignored": int(len(welded_verts) - len(np.unique(welded_tris))),
        "degenerate_triangles_ignored": int((~keep).sum()),
        "islands": island_count,
        "detached_islands": int(detached_island.sum()),
        "detached_groups": int(detached_groups),
        "detached_area_fraction": float(island_area[detached_island].sum() / total_area),
        "largest_detached_area_fraction": float(island_area[detached_island].max() / total_area)
        if detached_island.any() else 0.0,
    }, first, welded_tris, detached_island[tri_island] if island_count else np.zeros(0, bool)


# --------------------------------------------------------------------------------- lean/contact
def hull_area(mask, cell):
    """Area of the convex hull of the True cells of a (row = y, column = x) grid, cell corners included."""
    rows = np.flatnonzero(mask.any(axis=1))
    if not len(rows):
        return 0.0
    first = mask[rows].argmax(axis=1)
    last = mask.shape[1] - 1 - mask[rows][:, ::-1].argmax(axis=1)
    columns = np.concatenate([first, first + 1, last, last + 1] * 2)
    lines = np.concatenate([rows] * 4 + [rows + 1] * 4)
    hull = qa._hull2d(np.c_[columns, lines].astype(np.float64) * cell)
    if len(hull) < 3:
        return 0.0
    x, y = hull[:, 0], hull[:, 1]
    return float(0.5 * abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1))))


def lean_and_contact(welded_verts, welded_tris, skinned):
    """How a model stands: the tilt of its dominant flat faces and how much of its footprint is on the ground.

    Lean uses the _asset_qa estimator unchanged (area-weighted face-normal clusters, glTF frame).
    As in _asset_qa, the lean verdict is not applied to a skinned model (its flat faces are limbs).
    Contact takes the lowest surface over each cell of a grid across the footprint, from a vertical
    ray up through the cell centre and from vertex and edge points that fall in the cell (so thin
    walls and open tube ends count). The footprint is the cells the model covers; a cell is supported
    when that lowest surface is within CONTACT_TOL_M of the ground (y = 0). A level crate on a flat
    bottom supports most of its footprint; a prop pitched onto an edge or corner supports a sliver of
    it, inside a small hull.
    """
    gltf = np.c_[welded_verts[:, 0], welded_verts[:, 2], -welded_verts[:, 1]]
    a, b, c = (gltf[welded_tris[:, i]] for i in range(3))
    cross = np.cross(b - a, c - a)
    length = np.linalg.norm(cross, axis=1)
    ok = length > 0
    clusters = qa.flat_clusters(cross[ok] / length[ok, None], 0.5 * length[ok])
    up = qa.up_from_flats(clusters, qa.T("flat_min_share"), qa.T("flat_min_core"))
    threshold = qa.T("tilt_deg")
    lean = {"method": "_asset_qa flat-face up (area-weighted face-normal clusters)", "threshold_deg": threshold}
    if up:
        lean.update(qa._describe_up(up["up"]))
        lean.update({"flat_area_share": up["support_share"], "sources": up["sources"],
                     "verdict": "leaning" if lean["tilt_deg"] > threshold else "level"})
        if skinned:
            lean["verdict"] = "not applied (skinned)"
    else:
        lean.update({"tilt_deg": None, "verdict": "not measurable"})
    lean["base_plane_tilt_deg"] = qa.base_plane_tilt(gltf)

    used = welded_verts[np.unique(welded_tris)]
    lo, hi = used.min(axis=0), used.max(axis=0)
    cell = max(hi[0] - lo[0], hi[1] - lo[1], 1e-3) / CONTACT_GRID
    nx = max(1, int(math.ceil((hi[0] - lo[0]) / cell)))
    ny = max(1, int(math.ceil((hi[1] - lo[1]) / cell)))
    x0 = (lo[0] + hi[0]) / 2 - nx * cell / 2
    y0 = (lo[1] + hi[1]) / 2 - ny * cell / 2
    start = min(lo[2], 0.0) - 1.0
    reach = hi[2] - start + 1.0
    bvh = BVHTree.FromPolygons(welded_verts.tolist(), welded_tris.tolist(), all_triangles=True)
    height = np.full((ny, nx), np.nan)
    up_dir = (0.0, 0.0, 1.0)
    for row in range(ny):
        y = y0 + (row + 0.5) * cell
        for col in range(nx):
            hit = bvh.ray_cast((x0 + (col + 0.5) * cell, y, start), up_dir, reach)[0]
            if hit is not None:
                height[row, col] = hit[2]
    points, _island, _spacing = surface_samples(welded_verts, welded_tris, np.zeros(len(welded_tris), np.int64),
                                                cell / 2)
    cols = np.clip(((points[:, 0] - x0) / cell).astype(np.int64), 0, nx - 1)
    rows = np.clip(((points[:, 1] - y0) / cell).astype(np.int64), 0, ny - 1)
    lowest = np.full(ny * nx, np.inf)
    np.minimum.at(lowest, rows * nx + cols, points[:, 2])
    height = np.fmin(height, np.where(np.isinf(lowest), np.nan, lowest).reshape(ny, nx))
    footprint = ~np.isnan(height)
    supported = footprint & (np.nan_to_num(height, nan=np.inf) <= CONTACT_TOL_M)
    covered = int(footprint.sum())
    footprint_hull = hull_area(footprint, cell)
    contact = {
        "tolerance_m": CONTACT_TOL_M,
        "grid_cell_m": round(cell, 5),
        "footprint_area_m2": round(covered * cell * cell, 5),
        "supported_area_m2": round(int(supported.sum()) * cell * cell, 5),
        "support_share": round(int(supported.sum()) / covered, 4) if covered else 0.0,
        "contact_hull_share": round(hull_area(supported, cell) / footprint_hull, 4) if footprint_hull else 0.0,
    }
    return lean, contact


# ------------------------------------------------------------------------------------ materials
def new_material(name):
    mat = bpy.data.materials.new(name)
    nodes = mat.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    return mat, output


def emission_material(name, rgb):
    mat, output = new_material(name)
    emit = mat.node_tree.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = (*rgb, 1.0)
    mat.node_tree.links.new(emit.outputs[0], output.inputs["Surface"])
    return mat


def principled(mat, base, roughness):
    bsdf = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    if isinstance(base, (tuple, list)):
        bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.25
    return bsdf


def clay_material():
    mat, output = new_material("review_clay")
    bsdf = principled(mat, (CLAY_ALBEDO,) * 3, 0.6)
    mat.node_tree.links.new(bsdf.outputs[0], output.inputs["Surface"])
    return mat


def ground_material(cell):
    mat, output = new_material("review_ground")
    nt = mat.node_tree
    coords = nt.nodes.new("ShaderNodeTexCoord")
    checker = nt.nodes.new("ShaderNodeTexChecker")
    checker.inputs["Scale"].default_value = 1.0 / cell
    checker.inputs["Color1"].default_value = (GROUND_ALBEDO[0],) * 3 + (1.0,)
    checker.inputs["Color2"].default_value = (GROUND_ALBEDO[1],) * 3 + (1.0,)
    # Diffuse only: at the 8 deg views the floor is seen near grazing, where any specular lobe
    # (Fresnel) turns it into a bright mirror of the lamps and washes out the checker.
    bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
    # A little self-light so the floor far from the lamps settles near the background level
    # instead of going black behind dark models; shadows still darken the diffuse part.
    glow = nt.nodes.new("ShaderNodeEmission")
    glow.inputs["Strength"].default_value = 0.4
    add = nt.nodes.new("ShaderNodeAddShader")
    nt.links.new(coords.outputs["Object"], checker.inputs["Vector"])
    nt.links.new(checker.outputs["Color"], bsdf.inputs["Color"])
    nt.links.new(checker.outputs["Color"], glow.inputs["Color"])
    nt.links.new(bsdf.outputs[0], add.inputs[0])
    nt.links.new(glow.outputs[0], add.inputs[1])
    # Slightly see-through, so anything below ground level shows as a ghost under the floor;
    # a grounded model has nothing down there.
    clear = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    mix.inputs[0].default_value = GROUND_SEE_THROUGH
    nt.links.new(add.outputs[0], mix.inputs[1])
    nt.links.new(clear.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], output.inputs["Surface"])
    # Blended, not dithered: dithered transparency left visible grain over the whole floor.
    mat.surface_render_method = "BLENDED"
    return mat


def normals_material():
    mat, output = new_material("review_normals")
    nt = mat.node_tree
    geometry = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs[6].default_value = (0.08, 0.22, 0.85, 1.0)   # front face: blue
    mix.inputs[7].default_value = (0.85, 0.06, 0.05, 1.0)   # back face: red
    bsdf = nt.nodes.new("ShaderNodeBsdfDiffuse")
    nt.links.new(geometry.outputs["Backfacing"], mix.inputs[0])
    nt.links.new(mix.outputs[2], bsdf.inputs["Color"])
    nt.links.new(bsdf.outputs[0], output.inputs["Surface"])
    return mat


def wire_material():
    mat, output = new_material("review_wire")
    nt = mat.node_tree
    bsdf = principled(mat, (0.4, 0.4, 0.4), 0.7)
    wire = nt.nodes.new("ShaderNodeWireframe")
    wire.use_pixel_size = True
    wire.inputs[0].default_value = 1.0
    ink = nt.nodes.new("ShaderNodeEmission")
    ink.inputs["Color"].default_value = (0.004, 0.004, 0.004, 1.0)
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(wire.outputs[0], mix.inputs[0])
    nt.links.new(bsdf.outputs[0], mix.inputs[1])
    nt.links.new(ink.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], output.inputs["Surface"])
    return mat


def detached_material():
    mat, output = new_material("review_detached")
    nt = mat.node_tree
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_type = "GEOMETRY"
    attr.attribute_name = "review_detached"
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.inputs[6].default_value = (0.35, 0.35, 0.35, 1.0)
    mix.inputs[7].default_value = (1.0, 0.0, 0.85, 1.0)   # magenta: no hue shared with the orange below-floor tint
    bsdf = principled(mat, None, 0.6)
    nt.links.new(attr.outputs["Fac"], mix.inputs[0])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs[0], output.inputs["Surface"])
    return mat


def ensure_materials(meshes):
    """Meshes without materials (the LOD files) get a neutral clay; returns True if any texture."""
    clay = None
    textured = False
    for obj in meshes:
        if not obj.data.materials:
            clay = clay or clay_material()
            obj.data.materials.append(clay)
        for index, mat in enumerate(obj.data.materials):
            if mat is None:
                clay = clay or clay_material()
                obj.data.materials[index] = clay
            elif mat.node_tree and any(n.type == "TEX_IMAGE" and n.image for n in mat.node_tree.nodes):
                textured = True
    return textured


def tint_buried(meshes, depth):
    """Paint every surface more than `depth` below ground level orange (emission), in place.

    The floor is slightly see-through, so a sunk model shows an orange ghost under the floor line.
    Nothing changes on a model that is not below ground.
    """
    seen = set()
    for obj in meshes:
        for mat in obj.data.materials:
            if mat is None or mat.name in seen or mat.node_tree is None:
                continue
            seen.add(mat.name)
            nt = mat.node_tree
            output = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None)
            if output is None or not output.inputs["Surface"].links:
                continue
            source = output.inputs["Surface"].links[0].from_socket
            geometry = nt.nodes.new("ShaderNodeNewGeometry")
            split = nt.nodes.new("ShaderNodeSeparateXYZ")
            below = nt.nodes.new("ShaderNodeMath")
            below.operation = "LESS_THAN"
            below.inputs[1].default_value = -depth
            glow = nt.nodes.new("ShaderNodeEmission")
            glow.inputs["Color"].default_value = (1.0, 0.30, 0.0, 1.0)
            glow.inputs["Strength"].default_value = 2.0
            mix = nt.nodes.new("ShaderNodeMixShader")
            nt.links.new(geometry.outputs["Position"], split.inputs[0])
            nt.links.new(split.outputs["Z"], below.inputs[0])
            nt.links.new(below.outputs[0], mix.inputs[0])
            nt.links.new(source, mix.inputs[1])
            nt.links.new(glow.outputs[0], mix.inputs[2])
            nt.links.new(mix.outputs[0], output.inputs["Surface"])


def override_materials(meshes, material_for):
    for obj in meshes:
        for slot in obj.material_slots:
            data_mat = slot.material
            slot.link = "OBJECT"
            slot.material = material_for(data_mat)


def restore_materials(meshes):
    for obj in meshes:
        for slot in obj.material_slots:
            slot.link = "DATA"


# -------------------------------------------------------------------------------------- helpers
def link(obj, collection, shadow=False):
    collection.objects.link(obj)
    obj.visible_shadow = shadow
    return obj


def mesh_object(name, bm, material, collection, shadow=False):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(material)
    return link(bpy.data.objects.new(name, mesh), collection, shadow)


def box(name, low, high, material, collection):
    low, high = Vector(low), Vector(high)
    bm = bmesh.new()
    matrix = Matrix.Translation((low + high) / 2) @ Matrix.Diagonal((*(high - low), 1.0))
    bmesh.ops.create_cube(bm, size=1.0, matrix=matrix)
    return mesh_object(name, bm, material, collection)


def arrow(name, origin, direction, length, thickness, material, collection):
    """Square shaft plus cone head, from `origin` along `direction`."""
    direction = Vector(direction).normalized()
    rotation = Vector((0, 0, 1)).rotation_difference(direction).to_matrix().to_4x4()
    head = length * 0.28
    bm = bmesh.new()
    shaft = length - head
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation(Vector(origin))
                          @ rotation @ Matrix.Translation((0, 0, shaft / 2))
                          @ Matrix.Diagonal((thickness, thickness, shaft, 1.0)))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=thickness * 1.6, radius2=0.0,
                          depth=head, matrix=Matrix.Translation(Vector(origin)) @ rotation
                          @ Matrix.Translation((0, 0, shaft + head / 2)))
    return mesh_object(name, bm, material, collection)


def nice_at_most(value):
    candidates = [n for n in NICE if n <= value]
    return candidates[-1] if candidates else NICE[0]


# Which helper set each view shows. In each ortho view (A for front/back, B for right) the post and
# the yellow floor line stand at the depth of the model's lowest point, so that point sits exactly on
# the line when the model is grounded, above it when floating, below it when sunk; at 8 deg a floor
# line at any other depth would be drawn higher or lower than the floor under the contact. C sits at
# the (-X, +Y) corner, to the left of the model in the 3/4 view rather than between it and the camera.
VIEW_SET = {"front": "A", "back": "A", "right": "B", "34high": "C"}


def build_helpers(low, high, contact):
    """Scale posts with up arrows, axis triads and floor lines. Returns (sets, facts, collection)."""
    collection = bpy.data.collections.new("review_helpers")
    bpy.context.scene.collection.children.link(collection)
    dims = high - low
    maxdim = max(dims)
    footprint = max(dims.x, dims.y)
    post_height = nice_at_most(1.3 * maxdim)
    # The 3/4 view frames its whole post, so that post follows the model's height, not its largest
    # side: a 1.3 x maxdim post beside a flat or long model would shrink the model to a corner.
    post_34 = nice_at_most(max(dims.z, 0.25 * maxdim))
    heights = {"A": post_height, "B": post_height, "C": post_34}
    width = post_height / 30.0
    gap = 0.15 * max(footprint, 0.2 * maxdim)
    light = emission_material("review_band_light", (0.80, 0.80, 0.80))
    dark = emission_material("review_band_dark", (0.03, 0.03, 0.03))
    green = emission_material("review_axis_y_up", (0.05, 0.75, 0.10))
    blue = emission_material("review_axis_z_fwd", (0.10, 0.30, 1.00))
    red = emission_material("review_axis_x", (0.90, 0.08, 0.06))
    yellow = emission_material("review_floor_line", (1.00, 0.80, 0.05))
    line = width * 0.35
    # C stands off the (-X, +Y) corner of the box; its triad runs along the box edges, outside the
    # box, so a small gap is safe and keeps the model large in the 3/4 frame.
    c_gap = 0.4 * gap + post_34 / 20.0
    positions = {"A": (low.x - gap - width * 1.5, contact[1]),
                 "B": (contact[0], high.y + gap + width * 1.5),
                 "C": (low.x - c_gap, high.y + c_gap)}
    sets = {}
    for key, (px, py) in positions.items():
        height = heights[key]
        band, post_width = height / 10.0, height / 30.0
        arm, thick = 0.30 * height, post_width * 0.55
        tall, foot = [], []
        for index in range(10):
            tall.append(box(f"review_post{key}_{index}",
                            (px - post_width / 2, py - post_width / 2, index * band),
                            (px + post_width / 2, py + post_width / 2, (index + 1) * band),
                            dark if index % 2 == 0 else light, collection))
        tall.append(arrow(f"review_up{key}", (px, py, height), (0, 0, 1), height * 0.12,
                          post_width * 0.5, green, collection))
        # glTF +Z (model forward) is Blender -Y; glTF +X is Blender +X.
        foot.append(arrow(f"review_fwd{key}", (px, py, thick / 2), (0, -1, 0), arm, thick, blue, collection))
        foot.append(arrow(f"review_x{key}", (px, py, thick / 2), (1, 0, 0), arm, thick, red, collection))
        extra = []
        # Ground level at the depth of the lowest point, drawn right through the footprint: hidden
        # under a grounded model, visible in the gap under a floating one.
        if key == "A":
            extra.append(box("review_floorA", (px, py - line / 2, 0.0), (high.x + gap + width, py + line / 2, line / 2),
                             yellow, collection))
        elif key == "B":
            extra.append(box("review_floorB", (px - line / 2, low.y - gap - width, 0.0), (px + line / 2, py, line / 2),
                             yellow, collection))
        sets[key] = {"frame": tall + foot, "post": tall, "objects": tall + foot + extra,
                     "position_gltf": [round(px, 5), 0.0, round(-py, 5)]}
    facts = {"post_height_m": post_height, "post_band_m": post_height / 10.0, "post_width_m": width,
             "post_34_height_m": post_34, "post_34_band_m": post_34 / 10.0,
             "post_positions_gltf": {view: sets[key]["position_gltf"] for view, key in VIEW_SET.items()}}
    return sets, facts, collection


def show_set(sets, key):
    for name, helper_set in sets.items():
        for obj in helper_set["objects"]:
            obj.hide_render = name != key


def build_ground(centre, size, cell, collection):
    half = size / 2
    mesh = bpy.data.meshes.new("review_ground")
    mesh.from_pydata([(centre.x - half, centre.y - half, 0), (centre.x + half, centre.y - half, 0),
                      (centre.x + half, centre.y + half, 0), (centre.x - half, centre.y + half, 0)],
                     [], [(0, 1, 2, 3)])
    mesh.materials.append(ground_material(cell))
    obj = bpy.data.objects.new("review_ground", mesh)
    return link(obj, collection, shadow=False)


def helper_points(objects):
    """Helper vertices (helpers are built in world space with identity transforms)."""
    return np.array([tuple(v.co) for obj in objects for v in obj.data.vertices], dtype=np.float64)


# ------------------------------------------------------------------------------ camera + lights
def view_basis(azimuth, elevation):
    a, e = math.radians(azimuth), math.radians(elevation)
    to_camera = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    forward = -to_camera
    right = forward.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(forward)
    return to_camera, right, up


def camera_rotation(right, up, to_camera):
    return Matrix((right, up, to_camera)).transposed().to_euler()


def plan_views(view_points):
    """Camera placement per view. Ortho views share one scale; the 3/4 view is solved to fit.

    Each ortho view frames the model, its footprint on the ground and that view's post and triad.
    The 3/4 view frames the model, its footprint and its own post (scaled to the model's height)
    and triad, so the whole scale reference is always in that frame.
    """
    everything = np.concatenate(list(view_points.values()))
    lo, hi = everything.min(axis=0), everything.max(axis=0)
    reach = float(np.linalg.norm(hi - lo)) / 2
    plans, extent = {}, 0.0
    for name, azimuth, elevation, ortho, _label in VIEWS:
        subset = view_points[name]
        slo, shi = subset.min(axis=0), subset.max(axis=0)
        aim = Vector((slo + shi) / 2)
        rel = subset - np.array(aim)
        to_camera, right, up = view_basis(azimuth, elevation)
        x, y = rel @ np.array(right), rel @ np.array(up)
        if ortho:
            extent = max(extent, x.max() - x.min(), y.max() - y.min())
            centre = aim + right * float((x.max() + x.min()) / 2) + up * float((y.max() + y.min()) / 2)
            plans[name] = {"centre": centre, "basis": (to_camera, right, up), "ortho": True,
                           "azimuth": azimuth, "elevation": elevation}
            continue
        depth_axis = rel @ np.array(to_camera)
        half = SENSOR_MM / 2 / LENS_MM

        def spans(distance):
            depth = distance - depth_axis
            sx, sy = x / depth, y / depth
            return sx, sy, max(sx.max() - sx.min(), sy.max() - sy.min()) / 2

        near, far = float(depth_axis.max()) + reach * 0.05, reach * 50 + 1.0
        for _ in range(60):
            mid = (near + far) / 2
            if spans(mid)[2] <= half * PERSP_FILL:
                far = mid
            else:
                near = mid
        sx, sy, _ = spans(far)
        plans[name] = {"centre": aim, "basis": (to_camera, right, up), "ortho": False, "distance": far,
                       "shift": ((sx.max() + sx.min()) / 2 / (2 * half), (sy.max() + sy.min()) / 2 / (2 * half)),
                       "azimuth": azimuth, "elevation": elevation}
    ortho_scale = ORTHO_MARGIN * extent
    for plan in plans.values():
        if plan["ortho"]:
            plan["ortho_scale"] = ortho_scale
            plan["distance"] = reach * 4 + ortho_scale * 10
    return plans, ortho_scale, reach


def place_camera(camera, plan, reach, ground_size):
    data = camera.data
    to_camera, right, up = plan["basis"]
    camera.location = plan["centre"] + to_camera * plan["distance"]
    camera.rotation_euler = camera_rotation(right, up, to_camera)
    data.sensor_fit = "AUTO"
    data.sensor_width = SENSOR_MM
    if plan["ortho"]:
        data.type = "ORTHO"
        data.ortho_scale = plan["ortho_scale"]
        data.shift_x = data.shift_y = 0.0
    else:
        data.type = "PERSP"
        data.lens = LENS_MM
        data.shift_x, data.shift_y = plan["shift"]
    data.clip_start = plan["distance"] * 0.005
    data.clip_end = plan["distance"] + reach * 2 + ground_size * 2


def frame_check(scene, camera, post_points, model_points):
    """Is the view's whole scale post (with its arrow) inside the frame, and how much of it does the model span?"""
    bpy.context.view_layer.update()

    def project(points):
        return np.array([tuple(world_to_camera_view(scene, camera, Vector(p))) for p in points.tolist()])
    post, model = project(post_points), project(model_points)
    inside = bool(np.all((post[:, :2] >= 0.0) & (post[:, :2] <= 1.0)) and np.all(post[:, 2] > 0.0))
    return {"scale_reference_in_frame": inside,
            "model_frame_span": round(float(max(np.ptp(model[:, 0]), np.ptp(model[:, 1]))), 3)}


def make_lights(radius):
    lights = []
    for name, _az, _el, fraction in LIGHTS:
        data = bpy.data.lights.new(f"review_{name}", type="AREA")
        data.energy = LIGHT_TOTAL * fraction * radius ** 2
        data.size = radius * LIGHT_SIZE
        # EEVEE otherwise ends each lamp's influence where it drops below light_threshold, which
        # draws a hard edge across the floor.
        data.use_custom_distance = True
        data.cutoff_distance = radius * 1000.0
        obj = bpy.data.objects.new(f"review_{name}", data)
        bpy.context.scene.collection.objects.link(obj)
        lights.append(obj)
    return lights


def turn_lights(lights, centre, radius, view_azimuth):
    for obj, (_name, rel_azimuth, elevation, _fraction) in zip(lights, LIGHTS):
        a, e = math.radians(view_azimuth + rel_azimuth), math.radians(elevation)
        obj.location = centre + radius * 3 * Vector((math.cos(e) * math.sin(a),
                                                      -math.cos(e) * math.cos(a), math.sin(e)))
        obj.rotation_euler = (centre - obj.location).to_track_quat("-Z", "Y").to_euler()


# ---------------------------------------------------------------------------------- sheets/text
# Header ink (sRGB 0-255), matched to what each helper looks like in the renders.
INK = {"green": (70, 220, 90), "blue": (90, 150, 255), "red": (240, 75, 65), "yellow": (255, 215, 40),
       "orange": (255, 140, 20), "magenta": (255, 70, 225), "grey": (165, 165, 165)}
FONT_PATH = os.path.join(os.path.dirname(bpy.app.binary_path), f"{bpy.app.version[0]}.{bpy.app.version[1]}",
                         "datafiles", "fonts", "DejaVuSansMono.woff2")


def read_rgb(path):
    import OpenImageIO as oiio
    pixels = oiio.ImageBuf(path).get_pixels(oiio.UINT8)
    return pixels[..., :3].copy()


def write_jpeg(path, pixels):
    import OpenImageIO as oiio
    spec = oiio.ImageSpec(pixels.shape[1], pixels.shape[0], 3, oiio.UINT8)
    spec.attribute("Compression", "jpeg:95")
    out = oiio.ImageOutput.create(path)
    out.open(path, spec)
    out.write_image(np.ascontiguousarray(pixels))
    out.close()


def text_mask(width, height, items):
    """Rasterise [(x, y_from_top, px, text)] with blf into an alpha mask (OIIO has no FreeType here)."""
    font = blf.load(FONT_PATH) if os.path.exists(FONT_PATH) else 0
    buffer = imbuf.new((width, height))
    with blf.bind_imbuf(font, buffer):
        blf.color(font, 1.0, 1.0, 1.0, 1.0)
        for x, y, size, text, *_colour in items:
            blf.size(font, size)
            blf.position(font, x, height - y - size, 0)
            blf.draw_buffer(font, text)
    handle, path = tempfile.mkstemp(suffix=".png")
    os.close(handle)
    try:
        imbuf.write(buffer, filepath=path)
        import OpenImageIO as oiio
        mask = oiio.ImageBuf(path).get_pixels(oiio.UINT8)
    finally:
        os.remove(path)
    return mask[..., 3].astype(np.float32) / 255.0 if mask.shape[2] == 4 else mask[..., 0] / 255.0


def char_width(size):
    """Advance of one character of the (monospaced) sheet font at `size` px."""
    font = blf.load(FONT_PATH) if os.path.exists(FONT_PATH) else 0
    blf.size(font, size)
    return blf.dimensions(font, "M" * 40)[0] / 40.0


def make_sheet(paths, labels, header, out_path):
    """2x2 sheet under a header; a header line is a string, (text, rgb) or a list of (text, rgb) segments."""
    cells = [read_rgb(p) for p in paths]
    cell = cells[0].shape[0]
    scale = cell / 1024.0
    line = int(round(30 * scale))
    head = int(round(line * (len(header) + 0.8)))
    band = int(round(40 * scale))  # label strip above each image, so no label covers the render
    sheet = np.full((head + 2 * (band + cell), 2 * cell, 3), 24, np.uint8)
    items = []
    for index, (pixels, label) in enumerate(zip(cells, labels)):
        row, col = divmod(index, 2)
        top, left = head + row * (band + cell), col * cell
        sheet[top + band:top + band + cell, left:left + cell] = pixels
        sheet[top:top + band, left:left + cell] = 12
        items.append((left + int(12 * scale), top + int(8 * scale), int(round(24 * scale)), label))
    size = int(round(22 * scale))
    advance = char_width(size)
    for index, entry in enumerate(header):
        segments = [(entry, None)] if isinstance(entry, str) else entry if isinstance(entry, list) else [entry]
        x = int(14 * scale)
        for text, colour in segments:
            items.append((x, int(round(line * (0.4 + index))), size, text, colour))
            x += int(round(advance * len(text)))
    for colour in {item[4] if len(item) > 4 else None for item in items}:
        group = [item for item in items if (item[4] if len(item) > 4 else None) == colour]
        alpha = text_mask(sheet.shape[1], sheet.shape[0], group)[..., None]
        ink = np.array(colour or (235, 235, 235), np.float32)
        sheet = (sheet * (1.0 - alpha) + ink * alpha).astype(np.uint8)
    sheet[head - max(1, int(scale)):head, :] = 90
    write_jpeg(out_path, sheet)


# --------------------------------------------------------------------------------------- main
def render(scene, path):
    scene.render.filepath = path
    started = time.time()
    bpy.ops.render.render(write_still=True)
    return time.time() - started


def main():
    global qa
    args = parse_args()
    qa = load_estimator(os.path.abspath(args.estimator))
    name = args.name or os.path.splitext(os.path.basename(args.input))[0]
    out_dir = os.path.abspath(args.out)
    os.makedirs(out_dir, exist_ok=True)
    seconds = {}

    started = time.time()
    meshes, armatures, ignored = import_asset(os.path.abspath(args.input), args.offset_z)
    seconds["import"] = time.time() - started

    started = time.time()
    verts, normals, tris = gather_geometry(meshes)
    low, high = Vector(verts.min(axis=0)), Vector(verts.max(axis=0))
    dims = high - low
    maxdim = max(max(dims), 1e-3)
    centre = (low + high) / 2
    radius = max(dims.length / 2, 0.05)
    islands, welded_index, welded_tris, detached_faces = island_analysis(verts, tris, maxdim)
    lean, support = lean_and_contact(verts[welded_index], welded_tris, bool(armatures))
    seconds["analysis"] = time.time() - started

    started = time.time()
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = args.samples
    scene.render.resolution_x = scene.render.resolution_y = args.size
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.quality = 95
    world = bpy.data.worlds.new("review_world")
    world.node_tree.nodes["Background"].inputs[0].default_value = (BACKGROUND,) * 3 + (1.0,)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world

    textured = ensure_materials(meshes)
    ground_tol = max(0.002, 0.005 * dims.z)
    tint_buried(meshes, ground_tol)
    cell = 10.0 ** math.floor(math.log10(maxdim / 3.0))
    near_low = verts[:, 2] <= low.z + max(0.002, 0.01 * dims.z)
    contact = verts[near_low, :2].mean(axis=0)
    helper_sets, facts, helpers = build_helpers(low, high, contact)
    footprint = np.array([(low.x, low.y, 0.0), (high.x, low.y, 0.0), (low.x, high.y, 0.0), (high.x, high.y, 0.0)])
    view_points = {}
    # The 3/4 view frames the model's own shadow on the ground rather than its box corners, which
    # stick out well past a round model's silhouette on the diagonal.
    shadow = np.c_[verts[:, :2], np.zeros(len(verts))]
    for view_name, _az, _el, ortho, _label in VIEWS:
        view_points[view_name] = np.concatenate(
            [verts, helper_points(helper_sets[VIEW_SET[view_name]]["frame"]), footprint if ortho else shadow])
    plans, ortho_scale, reach = plan_views(view_points)
    # Big enough that its near edge never enters an 8 deg ortho frame; the far edge is the horizon.
    ground_size = 8.0 * ortho_scale
    build_ground(Vector((centre.x, centre.y, 0.0)), ground_size, cell, helpers)
    lights = make_lights(radius)
    camera = bpy.data.objects.new("review_camera", bpy.data.cameras.new("review_camera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    seconds["setup"] = time.time() - started

    extremes = np.concatenate([verts.argmin(axis=0), verts.argmax(axis=0)])
    model_sample = verts[np.union1d(np.linspace(0, len(verts) - 1, min(len(verts), 4000)).astype(np.int64),
                                    extremes)]

    def render_set(prefix):
        written = {}
        for view_name, azimuth, _el, _ortho, _label in VIEWS:
            place_camera(camera, plans[view_name], reach, ground_size)
            show_set(helper_sets, VIEW_SET[view_name])
            turn_lights(lights, centre, radius, azimuth)
            path = os.path.join(out_dir, f"{name}_{prefix}{view_name}.jpg")
            written[view_name] = {"file": os.path.basename(path), "seconds": round(render(scene, path), 3)}
            if not prefix:
                written[view_name].update(frame_check(
                    scene, camera, helper_points(helper_sets[VIEW_SET[view_name]]["post"]), model_sample))
        return written

    started = time.time()
    views = render_set("")
    seconds["views"] = time.time() - started

    pass_views = {}
    started = time.time()
    for pass_name in args.passes:
        if pass_name == "normals":
            mat = normals_material()
            override_materials(meshes, lambda _m: mat)
            pass_views[pass_name] = render_set("normals_")
            restore_materials(meshes)
        elif pass_name == "wire":
            mat = wire_material()
            override_materials(meshes, lambda _m: mat)
            pass_views[pass_name] = render_set("wire_")
            restore_materials(meshes)
        elif pass_name == "detached":
            welded_verts = verts[welded_index]
            mesh = bpy.data.meshes.new("review_welded")
            mesh.vertices.add(len(welded_verts))
            mesh.vertices.foreach_set("co", welded_verts.astype(np.float32).ravel())
            mesh.loops.add(len(welded_tris) * 3)
            mesh.loops.foreach_set("vertex_index", welded_tris.astype(np.int32).ravel())
            mesh.polygons.add(len(welded_tris))
            mesh.polygons.foreach_set("loop_start", np.arange(0, len(welded_tris) * 3, 3, dtype=np.int32))
            mesh.polygons.foreach_set("loop_total", np.full(len(welded_tris), 3, np.int32))
            mesh.update()
            attribute = mesh.attributes.new("review_detached", "FLOAT", "FACE")
            attribute.data.foreach_set("value", detached_faces.astype(np.float32))
            mesh.polygons.foreach_set("use_smooth", np.ones(len(welded_tris), bool))
            # The model's own vertex normals, so the welded copy shades like the original instead
            # of averaging across its creases.
            mesh.normals_split_custom_set_from_vertices(normals[welded_index].tolist())
            mesh.materials.append(detached_material())
            welded = bpy.data.objects.new("review_welded", mesh)
            scene.collection.objects.link(welded)
            for obj in meshes:
                obj.hide_render = True
            pass_views[pass_name] = render_set("detached_")
            for obj in meshes:
                obj.hide_render = False
            welded.hide_render = True
    seconds["passes"] = time.time() - started

    # glTF frame: x = x, y (up) = z, z (forward) = -y
    def to_gltf(v):
        return [round(float(v[0]), 5), round(float(v[2]), 5), round(float(-v[1]), 5)]
    low_gltf, high_gltf = to_gltf(low), to_gltf(high)
    gltf_min = [min(a, b) for a, b in zip(low_gltf, high_gltf)]
    gltf_max = [max(a, b) for a, b in zip(low_gltf, high_gltf)]
    grounding = "grounded" if abs(low.z) <= ground_tol else ("floating" if low.z > 0 else "sinking")
    px_per_m = args.size / ortho_scale

    started = time.time()
    header = [
        f"{name}   {os.path.basename(args.input)}   {'textured' if textured else 'UNTEXTURED'}   "
        f"{len(tris)} tris" + (f"   rest pose ({len(armatures)} armature)" if armatures else ""),
        (f"size x {dims.x:.3f}  y(up) {dims.z:.3f}  z(fwd) {dims.y:.3f} m   "
         f"low y {low.z:+.3f} m = {grounding.upper()}" + (f"   [offset-z {args.offset_z:+.3f} self-test]"
                                                           if args.offset_z else ""),
         None if grounding == "grounded" else (255, 150, 30)),
        (f"lean {lean['tilt_deg']:.1f} deg from flat faces = {lean['verdict'].upper()} (limit {lean['threshold_deg']:g})"
         if lean["tilt_deg"] is not None else "lean not measurable (no dominant flat face)")
        + f"   support {support['support_share'] * 100:.0f}% of footprint within {CONTACT_TOL_M * 100:g} cm of floor"
        f", contact hull {support['contact_hull_share'] * 100:.0f}%",
        f"ortho {px_per_m:.1f} px/m   post {facts['post_height_m']:g} m, bands {facts['post_band_m']:g} m"
        f" (3/4: {facts['post_34_height_m']:g} m, bands {facts['post_34_band_m']:g} m)   "
        f"checker {cell:g} m   detached {islands['detached_islands']} of {islands['islands']} islands",
    ]
    if lean["verdict"] == "leaning":
        header[2] = (header[2], INK["orange"])
    axes = [("green = +Y up", INK["green"]), ("   blue = +Z forward", INK["blue"]), ("   red = +X", INK["red"]),
            ("   yellow = floor under the lowest point", INK["yellow"])]
    pass_legends = {
        "normals": [("NORMALS pass:  ", None), ("blue = front face", INK["blue"]),
                    ("   red = back face (flipped normal or open shell)", INK["red"])],
        "wire": [("WIRE pass:  grey clay, black lines = triangle edges", None)],
        "detached": [("DETACHED pass:  ", None), ("grey = attached to the main body", INK["grey"]),
                     (f"   magenta = detached: {islands['detached_islands']} of {islands['islands']} islands, "
                      f"{islands['detached_area_fraction'] * 100:.2f}% of the area", INK["magenta"])],
    }
    labels = [label for *_rest, label in VIEWS]
    sheet_path = os.path.join(out_dir, f"{name}_sheet.jpg")
    make_sheet([os.path.join(out_dir, views[v]["file"]) for v, *_ in VIEWS], labels,
               header + [axes + [("   orange = below floor", INK["orange"])]], sheet_path)
    pass_sheets = {}
    for pass_name, written in pass_views.items():
        path = os.path.join(out_dir, f"{name}_{pass_name}_sheet.jpg")
        make_sheet([os.path.join(out_dir, written[v]["file"]) for v, *_ in VIEWS],
                   [f"{pass_name.upper()}  {label}" for label in labels],
                   header + [axes + [("   (below-floor tint not drawn)", INK["grey"])], pass_legends[pass_name]],
                   path)
        pass_sheets[pass_name] = os.path.basename(path)
    seconds["sheets"] = time.time() - started
    seconds["total_in_blender"] = time.time() - T_START

    with open(__file__, "rb") as handle:
        script_sha = hashlib.sha256(handle.read()).hexdigest()
    with open(args.estimator, "rb") as handle:
        estimator_sha = hashlib.sha256(handle.read()).hexdigest()
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from _review_render_batch import estimator_digest
    stat = os.stat(args.input)
    record = {
        "id": name,
        "input": os.path.abspath(args.input),
        "input_bytes": stat.st_size,
        "input_mtime": stat.st_mtime,
        "script_sha256": script_sha,
        "estimator_sha256": estimator_sha,
        "estimator_digest": estimator_digest(args.estimator),
        "blender_version": bpy.app.version_string,
        "settings": {"size": args.size, "passes": args.passes, "samples": args.samples,
                     "offset_z_m": args.offset_z},
        "frame": "glTF: +Y up, +Z forward (model front), +X = model's left",
        "bounds_gltf": {"min": gltf_min, "max": gltf_max},
        "dims_m": {"x": round(dims.x, 5), "y_up": round(dims.z, 5), "z_forward": round(dims.y, 5)},
        "low_z_m": round(low.z, 5),
        "top_z_m": round(high.z, 5),
        "grounding": grounding,
        "grounding_tolerance_m": round(ground_tol, 5),
        "footprint_centre_offset_m": {"x": round(centre.x, 5), "z": round(-centre.y, 5)},
        "lowest_point_xz_m": {"x": round(float(contact[0]), 5), "z": round(float(-contact[1]), 5)},
        "mesh_objects": len(meshes),
        "armatures": len(armatures),
        "ignored_not_exported_objects": ignored,
        "vertices": int(len(verts)),
        "triangles": int(len(tris)),
        "textured": textured,
        "ortho_scale_m": round(ortho_scale, 5),
        "px_per_m": round(px_per_m, 3),
        "checker_cell_m": cell,
        **{k: (round(v, 5) if isinstance(v, float) else v) for k, v in facts.items()},
        "islands": {k: (round(v, 6) if isinstance(v, float) else v) for k, v in islands.items()},
        "detached_island_count": islands["detached_islands"],
        "lean": lean,
        "contact": support,
        "views": {v: {**views[v], "azimuth_deg": plans[v]["azimuth"], "elevation_deg": plans[v]["elevation"],
                      "projection": "ortho" if plans[v]["ortho"] else f"persp {LENS_MM:g}mm"}
                  for v, *_ in VIEWS},
        "sheet": os.path.basename(sheet_path),
        "passes": pass_views,
        "pass_sheets": pass_sheets,
        "seconds": {k: round(v, 3) for k, v in seconds.items()},
    }
    json_path = os.path.join(out_dir, f"{name}_review.json")
    with open(json_path, "w", encoding="utf-8") as handle:
        json.dump(record, handle, indent=2)
    print(f"REVIEW_RESULT {json_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
