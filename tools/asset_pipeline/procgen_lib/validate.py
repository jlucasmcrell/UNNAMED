"""Geometry validation: on the Builder before any mesh exists, on the Blender mesh, and on the exported GLB.

validate_builder unifies validate_builder/validate_parts of _procgen_cart.py, _procgen_chest.py and
_procgen_crate.py (closed, outward, bow-tie, degenerate), made weld-aware and extended with non-manifold edges;
uv_face_checks and mesh_checks come from the crate/chest zero-area-UV checks; clash_report from cart/chest.
geometry_gate and glb_gate were moved here from _blender_build_kit.py, which now imports them, so the building kit
and the procedural library share one copy.

API
    validate_builder(b, weld_tol=1e-6) -> report     per part: open (weld-aware), misoriented and non-manifold
        edges, negative-volume (inward) parts, bow-tie polygons, degenerate faces, non-planar quads > 1 deg;
        report['ok'] and report['failed'] (the keys that are non-zero; non-planar quads are reported, not failed)
    uv_face_checks(b, loop_uv, res) -> report          zero-area UV faces and the smallest UV face in px^2
    mesh_checks(me) -> (area3d, area_uv) per triangle of a triangulated mesh
    clash_report(b, pairs=None) -> {'a|b': overlapping triangle pairs} between part groups (BVH; Blender only)
    geometry_gate(obj) -> report                        bow-tie / flipped / inward / degenerate / UV-degenerate
    glb_gate(path) -> report                            winding vs stored normals, degenerate, UV-degenerate
    require_clean(obj)                                  geometry_gate or SystemExit
"""
import json
import math

import numpy as np


# ------------------------------------------------------------------------------------------------ builder level

def _newell(P):
    return np.sum(np.cross(P, np.roll(P, -1, axis=0)), axis=0) / 2.0


def validate_builder(b, weld_tol=1e-6):
    """Checks every part as a closed solid. Weld-aware: vertices of one part closer than weld_tol count as one, so
    a primitive that duplicates a seam vertex is judged by its geometry, not its indexing."""
    V = np.asarray(b.verts, np.float64)
    report = {"parts": len(b.parts), "faces": len(b.faces), "open_edges": 0, "misoriented_edges": 0,
              "nonmanifold_edges": 0, "negative_volume_parts": [], "bowtie_polygons": 0, "degenerate_faces": 0,
              "nonplanar_quads_over_1deg": 0, "examples": []}
    for pi, part in enumerate(b.parts):
        fl = list(b.part_faces(pi))
        if not fl:
            report["examples"].append(f"part {part.name} has no faces")
            report["open_edges"] += 1
            continue
        used = sorted({i for fi in fl for i in b.faces[fi]})
        keys = {}
        weld = {}
        for i in used:
            k = tuple(np.round(V[i] / weld_tol).astype(np.int64))
            weld[i] = keys.setdefault(k, i)
        directed, undirected = {}, {}
        vol = 0.0
        for fi in fl:
            idx = [weld[i] for i in b.faces[fi]]
            for k in range(len(idx)):
                e = (idx[k], idx[(k + 1) % len(idx)])
                if e[0] == e[1]:
                    continue
                directed[e] = directed.get(e, 0) + 1
                u = (min(e), max(e))
                undirected[u] = undirected.get(u, 0) + 1
            p0 = V[idx[0]]
            for k in range(1, len(idx) - 1):
                vol += p0.dot(np.cross(V[idx[k]], V[idx[k + 1]])) / 6.0
        for (a, c), cnt in directed.items():
            if cnt > 1:
                report["misoriented_edges"] += 1
            if (c, a) not in directed:
                report["open_edges"] += 1
                if len(report["examples"]) < 10:
                    report["examples"].append(f"open edge in {part.name} at {np.round(V[a], 4).tolist()}")
        report["nonmanifold_edges"] += sum(1 for cnt in undirected.values() if cnt > 2)
        if vol <= 0:
            report["negative_volume_parts"].append(part.name)
    for f in b.faces:
        P = V[list(f)]
        nrm = _newell(P)
        ln = np.linalg.norm(nrm)
        if ln < 1e-10:
            report["degenerate_faces"] += 1
            continue
        nrm = nrm / ln
        if len(P) > 3:
            prv, nxt = np.roll(P, 1, axis=0), np.roll(P, -1, axis=0)
            turns = np.cross(P - prv, nxt - P) @ nrm
            span = np.linalg.norm(P - prv, axis=1) * np.linalg.norm(nxt - P, axis=1)
            if np.any(turns < -1e-6 * np.maximum(span, 1e-12)):
                report["bowtie_polygons"] += 1
        if len(P) == 4:
            n1 = np.cross(P[1] - P[0], P[2] - P[0])
            n2 = np.cross(P[2] - P[0], P[3] - P[0])
            l1, l2 = np.linalg.norm(n1), np.linalg.norm(n2)
            if l1 > 1e-12 and l2 > 1e-12 and math.acos(np.clip(n1.dot(n2) / (l1 * l2), -1, 1)) > math.radians(1.0):
                report["nonplanar_quads_over_1deg"] += 1
    fail_keys = ("open_edges", "misoriented_edges", "nonmanifold_edges", "negative_volume_parts", "bowtie_polygons",
                 "degenerate_faces")
    report["failed"] = [k for k in fail_keys if report[k]]
    report["ok"] = not report["failed"]
    return report


def uv_face_checks(b, loop_uv, res, uv_min_ratio=1e-6):
    """Zero-area UV faces (against their 3D area) on the packed UVs, and the smallest UV face in px^2."""
    V = np.asarray(b.verts, np.float64)
    uv = np.asarray(loop_uv, np.float64)
    off, bad, smallest = 0, 0, float("inf")
    for f in b.faces:
        P = V[list(f)]
        U = uv[off:off + len(f)]
        off += len(f)
        a3 = np.linalg.norm(_newell(P))
        a2 = abs(0.5 * np.sum(U[:, 0] * np.roll(U[:, 1], -1) - np.roll(U[:, 0], -1) * U[:, 1]))
        smallest = min(smallest, a2 * res * res)
        if a3 > 1e-10 and (not np.isfinite(a2) or a2 <= uv_min_ratio * a3):
            bad += 1
    return {"uv_zero_area_faces": bad, "uv_smallest_face_px2": round(smallest, 4)}


def mesh_checks(me):
    """Triangulated mesh: per-triangle 3D area and UV area (UVMap)."""
    n = len(me.polygons)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    ls = np.empty(n, np.int64)
    me.polygons.foreach_get("loop_start", ls)
    lt = np.empty(n, np.int64)
    me.polygons.foreach_get("loop_total", lt)
    assert (lt == 3).all(), "mesh_checks needs a triangulated mesh"
    idx = ls[:, None] + np.arange(3)[None, :]
    P = co[lv[idx]]
    a3 = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    U = uv[idx]
    e1, e2 = U[:, 1] - U[:, 0], U[:, 2] - U[:, 0]
    a2 = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2
    return a3, a2


def clash_report(b, pairs=None):
    """Overlapping triangle pairs between part groups that should only touch (e.g. a lid and its body). pairs: list
    of (group_a, group_b); default every pair of groups."""
    from mathutils.bvhtree import BVHTree
    trees = {}
    for g in b.groups:
        fl = b.group_faces(g)
        used = sorted({i for fi in fl for i in b.faces[fi]})
        remap = {o: k for k, o in enumerate(used)}
        trees[g] = BVHTree.FromPolygons([tuple(b.verts[i]) for i in used], [[remap[i] for i in b.faces[fi]] for fi in fl])
    if pairs is None:
        pairs = [(a, c) for i, a in enumerate(b.groups) for c in b.groups[i + 1:]]
    return {f"{a}|{c}": len(trees[a].overlap(trees[c])) for a, c in pairs if a in trees and c in trees}


# ------------------------------------------------------------------------------------------------ mesh / GLB gates
# Moved verbatim from _blender_build_kit.py (2026-09-25); the kit imports these.

def geometry_gate(obj, uv_min_ratio=1e-6, area_min=1e-9):
    """Measure the triangles the exporter will write, and report every defect that renders wrong.

    Checks, on the mesh's own loop triangulation (the one the glTF exporter uses):
      * bow-tie polygons: any corner that turns against the polygon's Newell normal, or a
        triangle whose normal opposes the polygon it came from;
      * inward-facing triangles, per connected shell: in a convex shell a triangle must face away
        from the shell's centroid; a non-convex closed shell is tested by ray parity (a ray leaving
        the front face must cross the shell an even number of times);
      * degenerate triangles: zero 3D area, or a UV area that is zero or tiny against the 3D area
        (a face the texture cannot cover).
    Returns the report; `report["ok"]` is False when any count is non-zero.
    """
    from mathutils.bvhtree import BVHTree

    mesh = obj.data
    mesh.calc_loop_triangles()
    co = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
    tris = np.array([t.vertices[:] for t in mesh.loop_triangles], dtype=np.int64).reshape(-1, 3)
    tri_loops = np.array([t.loops[:] for t in mesh.loop_triangles], dtype=np.int64).reshape(-1, 3)
    tri_poly = np.array([t.polygon_index for t in mesh.loop_triangles], dtype=np.int64)
    report = {"object": obj.name, "triangles": int(len(tris)), "bowtie_polygons": 0,
              "flipped_triangles": 0, "inward_triangles": 0, "degenerate_triangles": 0,
              "uv_degenerate_triangles": 0, "shells": 0, "open_shells": 0, "examples": []}
    if not len(tris):
        report["ok"] = False
        report["examples"].append("no triangles")
        return report

    a, b, c = co[tris[:, 0]], co[tris[:, 1]], co[tris[:, 2]]
    cross = np.cross(b - a, c - a)
    area = 0.5 * np.linalg.norm(cross, axis=1)
    tri_n = cross / np.maximum(2.0 * area, 1e-30)[:, None]

    # Polygon Newell normals and per-corner turn test.
    poly_n = np.zeros((len(mesh.polygons), 3))
    for p in mesh.polygons:
        idx = list(p.vertices)
        pts = co[idx]
        nxt = np.roll(pts, -1, axis=0)
        newell = np.array([np.sum((pts[:, 1] - nxt[:, 1]) * (pts[:, 2] + nxt[:, 2])),
                           np.sum((pts[:, 2] - nxt[:, 2]) * (pts[:, 0] + nxt[:, 0])),
                           np.sum((pts[:, 0] - nxt[:, 0]) * (pts[:, 1] + nxt[:, 1]))])
        length = np.linalg.norm(newell)
        poly_n[p.index] = newell / length if length > 1e-12 else 0.0
        if len(idx) > 3:
            prv = np.roll(pts, 1, axis=0)
            turns = np.cross(pts - prv, nxt - pts) @ poly_n[p.index]
            span = np.linalg.norm(pts - prv, axis=1) * np.linalg.norm(nxt - pts, axis=1)
            if length <= 1e-12 or np.any(turns < -1e-6 * np.maximum(span, 1e-12)):
                report["bowtie_polygons"] += 1
                if len(report["examples"]) < 8:
                    report["examples"].append(f"bow-tie polygon {p.index}")
    flipped = np.einsum("ij,ij->i", tri_n, poly_n[tri_poly]) < 0.0
    flipped &= area > area_min
    report["flipped_triangles"] = int(flipped.sum())

    degenerate = area <= area_min
    report["degenerate_triangles"] = int(degenerate.sum())
    if mesh.uv_layers.active is None:
        report["uv_degenerate_triangles"] = int(len(tris))
        report["examples"].append("no UV layer")
    else:
        uv = np.array([d.uv[:] for d in mesh.uv_layers.active.data], dtype=np.float64)
        ua, ub, uc = uv[tri_loops[:, 0]], uv[tri_loops[:, 1]], uv[tri_loops[:, 2]]
        uv_area = 0.5 * np.abs((ub[:, 0] - ua[:, 0]) * (uc[:, 1] - ua[:, 1])
                               - (ub[:, 1] - ua[:, 1]) * (uc[:, 0] - ua[:, 0]))
        bad_uv = ~np.isfinite(uv_area) | (uv_area <= uv_min_ratio * np.maximum(area, area_min))
        bad_uv &= ~degenerate
        report["uv_degenerate_triangles"] = int(bad_uv.sum())

    # Connected shells (vertex connectivity through triangles). Run this on the authored mesh,
    # where each primitive is its own shell; an imported GLB splits flat faces apart, which is what
    # glb_gate() below is for.
    tris_idx = tris
    parent = np.arange(len(co))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for t in tris:
        r0, r1, r2 = find(t[0]), find(t[1]), find(t[2])
        parent[r1] = r0
        parent[find(r2)] = r0
    roots = np.array([find(t[0]) for t in tris])
    edge_count = {}
    for t in tris:
        for i, j in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            key = (i, j) if i < j else (j, i)
            edge_count[key] = edge_count.get(key, 0) + 1
    inward = np.zeros(len(tris), dtype=bool)
    for root in np.unique(roots):
        sel = np.nonzero(roots == root)[0]
        report["shells"] += 1
        closed = all(edge_count[(min(i, j), max(i, j))] == 2
                     for t in tris[sel] for i, j in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])))
        if not closed:
            report["open_shells"] += 1
        live = sel[~degenerate[sel]]
        if not len(live):
            continue
        verts = np.unique(tris_idx[live].ravel())
        centre = co[verts].mean(axis=0)
        cen = (a[live] + b[live] + c[live]) / 3.0
        # Convex when no shell vertex lies in front of any of its triangle planes.
        plane_d = np.einsum("ij,ij->i", tri_n[live], cen)
        heights = co[verts] @ tri_n[live].T - plane_d[None, :]
        extent = np.ptp(co[verts], axis=0).max()
        if heights.max() <= 1e-5 * max(extent, 1e-3):
            inward[live] = np.einsum("ij,ij->i", tri_n[live], cen - centre) <= 0.0
            continue
        if not closed:
            continue
        # Non-convex closed shell: ray parity, majority of three slightly skewed rays.
        shell_tris = tris_idx[live]
        bvh = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(t) for t in shell_tris])
        skews = (np.array([0.013, 0.007, 0.0]), np.array([-0.011, 0.0, 0.017]),
                 np.array([0.0, -0.019, 0.005]))
        for k, tri_index in enumerate(live):
            votes = 0
            for skew in skews:
                direction = tri_n[tri_index] + skew
                direction /= np.linalg.norm(direction)
                origin = cen[k] + tri_n[tri_index] * 1e-5
                hits = 0
                for _ in range(64):
                    hit = bvh.ray_cast(origin.tolist(), direction.tolist())
                    if hit[0] is None:
                        break
                    hits += 1
                    origin = np.array(hit[0]) + direction * 1e-5
                votes += hits % 2
            inward[tri_index] = votes >= 2
    report["inward_triangles"] = int(inward.sum())
    for name, mask in (("inward", inward), ("flipped", flipped), ("degenerate", degenerate)):
        for index in np.nonzero(mask)[0][:3]:
            if len(report["examples"]) < 12:
                report["examples"].append(f"{name} triangle {int(index)} of polygon "
                                          f"{int(tri_poly[index])} at "
                                          f"{np.round(cen_all(a, b, c, index), 3).tolist()}")
    report["ok"] = not any(report[k] for k in ("bowtie_polygons", "flipped_triangles",
                                                "inward_triangles", "degenerate_triangles",
                                                "uv_degenerate_triangles"))
    return report


def glb_gate(path, uv_min_ratio=1e-6, area_min=1e-9):
    """Check an exported GLB as the engine will read it: every triangle's winding must agree with
    the normals stored on its corners (a disagreement is a triangle lit from behind, or culled
    where it should show), and no triangle may be zero-area or UV-degenerate.

    Imports into the current scene and removes what it imported."""
    import bpy

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    report = {"glb": path, "meshes": 0, "triangles": 0, "normal_disagree_triangles": 0,
              "degenerate_triangles": 0, "uv_degenerate_triangles": 0, "examples": []}
    for obj in imported:
        if obj.type != "MESH":
            continue
        mesh = obj.data
        report["meshes"] += 1
        mesh.calc_loop_triangles()
        co = np.array([v.co[:] for v in mesh.vertices], dtype=np.float64)
        tris = np.array([t.vertices[:] for t in mesh.loop_triangles], dtype=np.int64).reshape(-1, 3)
        loops = np.array([t.loops[:] for t in mesh.loop_triangles], dtype=np.int64).reshape(-1, 3)
        report["triangles"] += len(tris)
        if not len(tris):
            continue
        cross = np.cross(co[tris[:, 1]] - co[tris[:, 0]], co[tris[:, 2]] - co[tris[:, 0]])
        area = 0.5 * np.linalg.norm(cross, axis=1)
        corner_n = np.array([n.vector[:] for n in mesh.corner_normals], dtype=np.float64)
        stored = corner_n[loops].sum(axis=1)
        disagree = (np.einsum("ij,ij->i", cross, stored) <= 0.0) & (area > area_min)
        degenerate = area <= area_min
        report["normal_disagree_triangles"] += int(disagree.sum())
        report["degenerate_triangles"] += int(degenerate.sum())
        if mesh.uv_layers.active is None:
            report["uv_degenerate_triangles"] += len(tris)
        else:
            uv = np.array([d.uv[:] for d in mesh.uv_layers.active.data], dtype=np.float64)
            ua, ub, uc = uv[loops[:, 0]], uv[loops[:, 1]], uv[loops[:, 2]]
            uv_area = 0.5 * np.abs((ub[:, 0] - ua[:, 0]) * (uc[:, 1] - ua[:, 1])
                                   - (ub[:, 1] - ua[:, 1]) * (uc[:, 0] - ua[:, 0]))
            bad = (~np.isfinite(uv_area) | (uv_area <= uv_min_ratio * np.maximum(area, area_min)))
            report["uv_degenerate_triangles"] += int((bad & ~degenerate).sum())
        if disagree.any() and len(report["examples"]) < 6:
            report["examples"].append(f"{obj.name}: {int(disagree.sum())} triangles wound "
                                      "against their stored normals")
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    report["ok"] = report["triangles"] > 0 and not any(
        report[k] for k in ("normal_disagree_triangles", "degenerate_triangles",
                            "uv_degenerate_triangles"))
    return report


def cen_all(a, b, c, index):
    return (a[index] + b[index] + c[index]) / 3.0


def require_clean(obj):
    """Run geometry_gate and stop the build on any defect."""
    report = geometry_gate(obj)
    print("GEOMETRY_GATE " + json.dumps(report))
    if not report["ok"]:
        raise SystemExit(f"geometry gate failed for {obj.name}: {report}")
    return report
