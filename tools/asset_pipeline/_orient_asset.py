"""Measure the lean baked into a static LOD0 and estimate the rigid rotation that levels it.

Pixal3D reconstructs every mesh in its concept camera's frame (image up = +Y, camera at +Z), so an
elevated or lowered concept view arrives as a rigid pitch, and a three-quarter view leaves a box
yawed about vertical. Nothing downstream levels a mesh. This tool measures that tilt on the glTF
vertex data, classifies which layer carries it, and writes the correction the Blender step applies.

Pure Python + numpy; reads GLBs only and writes only under assets/_staging/orient/<id>/.

Frames: everything here is the glTF export frame (X right, Y up, Z forward = toward the concept
camera). A correction is a 3x3 rotation R with v_new = R @ v_old, followed by a translation that puts
min Y at 0 and the X/Z bounding-box centre at 0.

Estimators, by kind (the primary is iterated to a fixed point: re-measured on the corrected mesh
until the correction stops changing; the secondary is measured once, independently):
  rigid  primary: dominant face-normal frame (area-weighted Manhattan frame, multi-start Procrustes),
         giving up AND footprint yaw; four 90-degree yaw candidates are recorded for a visual front
         check. secondary: the stable pose the body settles into on flat ground (local minimum of
         centroid height over the support plane, seeded at the frame's up; loose debris excluded).
  round  as rigid, but only up is corrected (a round footprint has no yaw to align).
  tree   primary: trunk axis only, from the connected trunk component tracked slice by slice up the
         bare-trunk run (area-weighted surface samples, robust line through the centroids).
         secondary: the ground-contact plane under the trunk when it is well conditioned, else a
         core-centroid line fit. Yaw is left as reconstructed.
  stick  primary: principal axis of the surface; secondary: the end-cap face normals.
A correction is refused (flagged, not applied) when the two estimators disagree by more than 8 deg,
when the level tilt (primary up vs +Y, correction.level_tilt_deg) exceeds 45 deg, or for weapons.
The 45-deg gate covers the tilt only. The total rotation (correction.total_rotation_deg) adds the
free 90-deg yaw choice on top of the tilt; it is recorded, not gated, and may exceed 45 deg.

verify exits non-zero when any triangle corner's normal in the staged GLB deviates more than 1 deg
from R applied to the ready LOD0's normal at that corner, or when that comparison cannot be made.

Usage:
  python _orient_asset.py estimate --asset container_chest_iron_banded [--kind rigid]
  python _orient_asset.py choose --asset container_chest_iron_banded --candidate 1 --reason "lock plate faces +Z" [--up secondary --up-reason ...]
  python _orient_asset.py verify --asset container_chest_iron_banded
"""
import argparse
import hashlib
import json
import math
import os
import struct
import sys

import numpy as np

ASSETS = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "assets"))
STAGING = os.path.join(ASSETS, "_staging", "orient")
UP = np.array([0.0, 1.0, 0.0])
MAX_DISAGREE_DEG = 8.0
MAX_CORRECTION_DEG = 45.0          # gates level_tilt_deg only, never total_rotation_deg
MAX_CORNER_NORMAL_ERROR_DEG = 1.0  # verify: staged corner normal vs R @ source normal
MAX_CORNER_POSITION_ERROR_M = 1e-4  # verify: corner correspondence must hold before normals are compared

# Kind per asset where the id alone does not say it. Everything else: flora_* -> tree, else rigid.
KINDS = {
    "resource_ash_haft": "stick",
    "container_barrel_oak": "round",
}


# ----------------------------------------------------------------------------------------- glTF io
def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, js, binary = 12, None, b""
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == 0x4E4F534A:
            js = json.loads(data[offset:offset + length].decode("utf-8"))
        elif kind == 0x004E4942:
            binary = data[offset:offset + length]
        offset += length
    return js, binary


def accessor(js, binary, index):
    acc = js["accessors"][index]
    view = js["bufferViews"][acc["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
    dtype = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[acc["componentType"]]
    count = acc["count"]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    item = np.dtype(dtype).itemsize * comps
    stride = view.get("byteStride", 0)
    if stride and stride != item:
        raw = np.frombuffer(binary, np.uint8, stride * (count - 1) + item, start)
        rows = np.lib.stride_tricks.as_strided(raw, (count, item), (stride, 1))
        return np.frombuffer(rows.copy().tobytes(), dtype).reshape(count, comps)
    return np.frombuffer(binary, dtype, count * comps, start).reshape(count, comps)


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], float).reshape(4, 4).T
    x, y, z, w = node.get("rotation", [0, 0, 0, 1])
    rot = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                    [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                    [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = rot * np.array(node.get("scale", [1, 1, 1]), float)
    m[:3, 3] = node.get("translation", [0, 0, 0])
    return m


def load_mesh(path):
    """World-space triangles of every mesh node, plus a record of the node hierarchy."""
    js, binary = read_glb(path)
    nodes = js.get("nodes", [])
    parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}

    def world(i):
        m = node_matrix(nodes[i])
        while i in parent:
            i = parent[i]
            m = node_matrix(nodes[i]) @ m
        return m

    verts, faces, normals, uvs, node_info, offset = [], [], [], [], [], 0
    for i, node in enumerate(nodes):
        g = world(i)
        node_info.append({"index": i, "name": node.get("name"), "mesh": node.get("mesh"),
                          "parent": parent.get(i), "children": node.get("children", []),
                          "world_is_identity": bool(np.allclose(g, np.eye(4), atol=1e-6)),
                          "local_trs": {k: node[k] for k in ("translation", "rotation", "scale", "matrix") if k in node}})
        if "mesh" not in node:
            continue
        for prim in js["meshes"][node["mesh"]]["primitives"]:
            v = accessor(js, binary, prim["attributes"]["POSITION"]).astype(float)
            v = (g @ np.c_[v, np.ones(len(v))].T).T[:, :3]
            if "NORMAL" in prim["attributes"]:
                nrm = accessor(js, binary, prim["attributes"]["NORMAL"]).astype(float) @ g[:3, :3].T
                normals.append(nrm)
            if "TEXCOORD_0" in prim["attributes"]:
                uvs.append(accessor(js, binary, prim["attributes"]["TEXCOORD_0"]).astype(float))
            if "indices" in prim:
                f = accessor(js, binary, prim["indices"]).astype(np.int64).reshape(-1, 3)
            else:
                f = np.arange(len(v)).reshape(-1, 3)
            verts.append(v)
            faces.append(f + offset)
            offset += len(v)
    V = np.vstack(verts)
    F = np.vstack(faces)
    N = np.vstack(normals) if normals else None
    UV = np.vstack(uvs) if uvs else None
    return {"js": js, "binary": binary, "V": V, "F": F, "N": N, "UV": UV, "nodes": node_info,
            "scene_roots": js["scenes"][js.get("scene", 0)]["nodes"]}


# ----------------------------------------------------------------------------------- geometry kit
def unit(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def angle_deg(a, b):
    return float(np.degrees(np.arccos(np.clip(unit(a) @ unit(b), -1.0, 1.0))))


def face_data(V, F):
    cross = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    area = np.linalg.norm(cross, axis=1) / 2
    ok = area > 0
    n = np.zeros_like(cross)
    n[ok] = cross[ok] / (2 * area[ok, None])
    return n, area


def surface_samples(V, F, count=300000, seed=7):
    _, area = face_data(V, F)
    rng = np.random.default_rng(seed)
    tri = rng.choice(len(F), count, p=area / area.sum())
    r1, r2 = rng.random(count), rng.random(count)
    s = np.sqrt(r1)
    a, b, c = V[F[tri, 0]], V[F[tri, 1]], V[F[tri, 2]]
    return (1 - s)[:, None] * a + (s * (1 - r2))[:, None] * b + (s * r2)[:, None] * c


def rot_about(axis, deg):
    axis = unit(axis)
    k = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    t = math.radians(deg)
    return np.eye(3) + math.sin(t) * k + (1 - math.cos(t)) * k @ k


def minimal_rotation(src, dst):
    """Smallest rotation taking direction src onto dst (no twist about dst)."""
    src, dst = unit(src), unit(dst)
    axis = np.cross(src, dst)
    s = np.linalg.norm(axis)
    if s < 1e-12:
        return np.eye(3)
    return rot_about(axis, math.degrees(math.atan2(s, src @ dst)))


def rotation_angle(R):
    return float(np.degrees(np.arccos(np.clip((np.trace(R) - 1) / 2, -1.0, 1.0))))


def rotation_axis(R):
    w = np.array([R[2, 1] - R[1, 2], R[0, 2] - R[2, 0], R[1, 0] - R[0, 1]])
    n = np.linalg.norm(w)
    return (w / n).round(5).tolist() if n > 1e-9 else [0.0, 1.0, 0.0]


def quaternion_xyzw(R):
    t = np.trace(R)
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        q = [(R[2, 1] - R[1, 2]) / s, (R[0, 2] - R[2, 0]) / s, (R[1, 0] - R[0, 1]) / s, 0.25 * s]
    else:
        i = int(np.argmax(np.diag(R)))
        j, k = (i + 1) % 3, (i + 2) % 3
        s = math.sqrt(1.0 + R[i, i] - R[j, j] - R[k, k]) * 2
        q = [0.0, 0.0, 0.0, (R[k, j] - R[j, k]) / s]
        q[i] = 0.25 * s
        q[j] = (R[j, i] + R[i, j]) / s
        q[k] = (R[k, i] + R[i, k]) / s
    return [round(float(x), 7) for x in q]


def describe_up(d):
    """Tilt of an object-up direction: total, and split into depth (about X, + = top toward the
    concept camera at +Z) and lateral (about Z, + = top toward +X)."""
    d = unit(d)
    if d[1] < 0:
        d = -d
    return {"off_vertical_deg": round(angle_deg(d, UP), 2),
            "depth_tilt_deg": round(math.degrees(math.atan2(d[2], d[1])), 2),
            "lateral_tilt_deg": round(math.degrees(math.atan2(d[0], d[1])), 2),
            "dir": d.round(4).tolist()}


def bbox(V):
    lo, hi = V.min(0), V.max(0)
    return {"min": lo.round(4).tolist(), "max": hi.round(4).tolist(), "size_xyz": (hi - lo).round(4).tolist()}


# ------------------------------------------------------------------------------ rigid estimators
def manhattan_frame(n, area, starts=None):
    """Rotation whose axes best explain the area-weighted face normals (each normal is pulled to its
    nearest signed axis). Multi-start; returns the frame (columns = axis directions in the world),
    the area fraction within 8 deg of an axis, and the per-axis area fractions."""
    total = area.sum()
    if starts is None:
        starts = [rot_about(UP, yaw) @ rot_about([1, 0, 0], pitch)
                  for yaw in (0, 22.5, 45, 67.5) for pitch in (-30, -15, 0, 15, 30)]
    best = None
    for R in starts:
        for cone in (30, 20, 12, 8, 8, 8):
            c = n @ R                      # normal in frame coordinates
            k = np.argmax(np.abs(c), 1)
            mag = np.abs(c[np.arange(len(c)), k])
            sel = mag > math.cos(math.radians(cone))
            if area[sel].sum() == 0:
                break
            a = np.zeros_like(c)
            a[np.arange(len(c)), k] = np.sign(c[np.arange(len(c)), k])
            M = (n[sel] * area[sel, None]).T @ a[sel]
            U, _, Vt = np.linalg.svd(M)
            D = np.diag([1, 1, np.linalg.det(U @ Vt)])
            R = U @ D @ Vt
        c = n @ R
        k = np.argmax(np.abs(c), 1)
        mag = np.abs(c[np.arange(len(c)), k])
        inl = mag > math.cos(math.radians(8))
        score = float(area[inl].sum() / total)
        if best is None or score > best[1]:
            per_axis = [round(float(area[inl & (k == j)].sum() / total), 4) for j in range(3)]
            best = (R, score, per_axis)
    return best


def volume_centroid(V, F):
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    vol = np.einsum("ij,ij->i", a, np.cross(b, c)) / 6.0
    total = vol.sum()
    _, area = face_data(V, F)
    surf = ((a + b + c) / 3 * area[:, None]).sum(0) / area.sum()
    if abs(total) < 1e-12:
        return surf, "surface_area_centroid", 0.0
    cen = ((a + b + c) / 4 * vol[:, None]).sum(0) / total
    lo, hi = V.min(0), V.max(0)
    if np.any(cen < lo) or np.any(cen > hi):
        return surf, "surface_area_centroid", float(total)
    return cen, "volume_centroid", float(total)


def extreme_points(V, count=6000):
    i = np.arange(count) + 0.5
    phi = np.arccos(1 - 2 * i / count)
    theta = math.pi * (1 + 5 ** 0.5) * i
    D = np.c_[np.cos(theta) * np.sin(phi), np.cos(phi), np.sin(theta) * np.sin(phi)]
    keep = set()
    for s in range(0, count, 400):
        keep.update(np.argmax(V @ D[s:s + 400].T, 0).tolist())
        keep.update(np.argmin(V @ D[s:s + 400].T, 0).tolist())
    return V[sorted(keep)]


def components(V, F, weld_tol=1e-4):
    """Connected components of the triangles after welding vertices by position (the LOD0 is split
    along UV seams, so unwelded index connectivity is meaningless). Returns a label per face."""
    q = np.round(V / weld_tol).astype(np.int64)
    _, weld = np.unique(q, axis=0, return_inverse=True)
    Fw = weld.ravel()[F]
    lab = np.arange(Fw.max() + 1)
    while True:
        m = lab[Fw].min(1)
        new = lab.copy()
        for k in range(3):
            np.minimum.at(new, Fw[:, k], m)
        new = new[new]
        if np.array_equal(new, lab):
            break
        lab = new
    return lab[Fw[:, 0]]


def min_distance(A, B, chunk=1500):
    best = np.inf
    for s in range(0, len(A), chunk):
        best = min(best, float(((A[s:s + chunk, None, :] - B[None, :, :]) ** 2).sum(2).min()))
    return best ** 0.5


def body_faces(V, F, gap_frac=0.08):
    """Faces of the physical body: the largest component plus every component within gap_frac of
    the object's size from what is already attached. Reconstructions leave legs, bands and wheels as
    separate shells (measured gaps: bench legs 0.3%, chest parts 0.5%, cart wheels 3.6-4.1% of size);
    a piece further away (the trestle table's stray stick sits 31% away) is loose debris and cannot
    support the body."""
    flab = components(V, F)
    _, area = face_data(V, F)
    ids, inv = np.unique(flab, return_inverse=True)
    frac = np.bincount(inv, area) / area.sum()
    size = float(np.ptp(V, 0).max())
    attached = {int(np.argmax(frac))}
    pts = {j: V[np.unique(F[inv == j].ravel())] for j in range(len(ids))}
    thin = lambda P, n: P[::max(1, len(P) // n)]
    changed = True
    while changed:
        changed = False
        body = thin(np.vstack([pts[j] for j in attached]), 12000)
        for j in range(len(ids)):
            if j in attached:
                continue
            if min_distance(thin(pts[j], 600), body) <= gap_frac * size:
                attached.add(j)
                changed = True
    keep = np.isin(inv, sorted(attached))
    debris = []
    body = thin(np.vstack([pts[j] for j in attached]), 12000)
    for j in range(len(ids)):
        if j not in attached:
            P = pts[j]
            debris.append({"area_frac": round(float(frac[j]), 4),
                           "gap_m": round(min_distance(thin(P, 600), body), 3),
                           "bbox_min": P.min(0).round(3).tolist(), "bbox_max": P.max(0).round(3).tolist()})
    return keep, {"components": int(len(ids)), "attached": len(attached), "gap_tolerance_m": round(gap_frac * size, 4),
                  "debris": debris}


def rest_pose(V, F, seed=UP):
    """Where the object settles on flat ground: descend the centroid height above the support
    plane, U(n) = n.c - min_i n.p_i, starting from up = seed. Local minima of U are the stable
    resting faces of the convex hull, so this reads the contact geometry (feet, base rim, wheels)
    independently of the face-normal frame that supplies the seed."""
    c, how, vol = volume_centroid(V, F)
    V = V[np.unique(F.ravel())]
    P = extreme_points(V)
    seed = unit(seed)
    e1 = unit(np.cross(seed, [0.0, 0.0, 1.0]) if abs(seed[2]) < 0.9 else np.cross(seed, [1.0, 0.0, 0.0]))
    e2 = np.cross(e1, seed)

    def n_of(tx, tz):
        return unit(seed + math.tan(math.radians(tx)) * e1 + math.tan(math.radians(tz)) * e2)

    def U(tx, tz):
        nn = n_of(tx, tz)
        return float(nn @ c - (P @ nn).min())

    tx = tz = 0.0
    u0 = u = U(tx, tz)
    step = 4.0
    while step > 0.01:
        moved = False
        for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            cand = U(tx + dx * step, tz + dz * step)
            if cand < u - 1e-9:
                tx, tz, u, moved = tx + dx * step, tz + dz * step, cand, True
                break
        if not moved:
            step /= 2
    nn = n_of(tx, tz)
    h = V @ nn
    contact = V[h < h.min() + 0.002 * (h.max() - h.min())]
    return {"up": nn, "seed": describe_up(seed)["dir"], "centroid": c.round(4).tolist(), "centroid_method": how,
            "centroid_height_at_seed_m": round(u0, 4), "centroid_height_rest_m": round(float(nn @ c - h.min()), 4),
            "moved_from_seed_deg": round(angle_deg(nn, seed), 2), "contact_vertices": int(len(contact))}


def dominant_normal(n, area, seed, cone=35):
    d = unit(seed)
    frac = 0.0
    for c in (cone, 20, 12, 12, 12):
        sel = n @ d > math.cos(math.radians(c))
        if area[sel].sum() == 0:
            return None, 0.0
        d = unit((n[sel] * area[sel, None]).sum(0))
        frac = float(area[sel].sum() / area.sum())
    return d, frac


# ------------------------------------------------------------------------------- tree estimators
def components_2d(cells):
    """Connected components (8-neighbour) of a set of integer grid cells; pure Python flood fill."""
    todo = set(cells)
    comps = []
    while todo:
        seed = todo.pop()
        stack, comp = [seed], [seed]
        while stack:
            i, j = stack.pop()
            for di in (-1, 0, 1):
                for dj in (-1, 0, 1):
                    q = (i + di, j + dj)
                    if q in todo:
                        todo.remove(q)
                        stack.append(q)
                        comp.append(q)
        comps.append(comp)
    return comps


def trunk_components(P, band=(0.03, 0.70), slices=34, cell_frac=0.011):
    """Per horizontal slice, the connected surface component that continues the trunk (largest at
    the base, then nearest the previous centroid). Returns centroids and per-slice widths."""
    lo, hi = P.min(0), P.max(0)
    H = hi[1] - lo[1]
    cell = cell_frac * H
    edges = np.linspace(lo[1] + band[0] * H, lo[1] + band[1] * H, slices + 1)
    prev, rows = None, []
    for y0, y1 in zip(edges[:-1], edges[1:]):
        s = P[(P[:, 1] >= y0) & (P[:, 1] < y1)]
        if len(s) < 30:
            continue
        ij = np.floor((s[:, [0, 2]] - lo[[0, 2]]) / cell).astype(int)
        keys = [tuple(x) for x in np.unique(ij, axis=0)]
        lookup = {}
        for comp_id, comp in enumerate(components_2d(keys)):
            for key in comp:
                lookup[key] = comp_id
        labels = np.array([lookup[tuple(x)] for x in ij])
        best, best_score = None, None
        for comp_id in np.unique(labels):
            pts = s[labels == comp_id]
            if len(pts) < 30:
                continue
            cxz = pts[:, [0, 2]].mean(0)
            score = -len(pts) if prev is None else float(np.linalg.norm(cxz - prev))
            if best_score is None or score < best_score:
                best, best_score = (cxz, pts), score
        if best is None:
            continue
        prev = best[0]
        rows.append({"y": float((y0 + y1) / 2), "h_frac": float((y0 + y1) / 2 - lo[1]) / H,
                     "cx": float(best[0][0]), "cz": float(best[0][1]),
                     "width": float(max(np.ptp(best[1][:, 0]), np.ptp(best[1][:, 2]))), "points": int(len(best[1]))})
    return rows


def bare_trunk(rows, start=0.06, jump=2.2):
    """The bare-trunk run: from `start` of the height up to the first slice whose tracked component
    is wider than `jump` x the running median width (canopy or first major fork), or whose centre
    steps sideways by more than the running median width (tracking lost)."""
    run = []
    for r in rows:
        if r["h_frac"] < start:
            continue
        if len(run) >= 4:
            med = float(np.median([q["width"] for q in run]))
            step = math.hypot(r["cx"] - run[-1]["cx"], r["cz"] - run[-1]["cz"])
            if r["width"] > jump * med or step > med:
                break
        run.append(r)
    return run


def robust_line(C, iters=10):
    """Line through points, iteratively reweighted (Huber, k = 1.5 x median residual)."""
    C = np.asarray(C, float)
    w = np.ones(len(C))
    for _ in range(iters):
        mu = (C * w[:, None]).sum(0) / w.sum()
        X = (C - mu) * np.sqrt(w)[:, None]
        d = np.linalg.svd(X)[2][0]
        d = d * np.sign(d[1])
        r = np.linalg.norm(np.cross(C - mu, d), axis=1)
        k = 1.5 * max(np.median(r), 1e-6)
        w = np.where(r <= k, 1.0, k / r)
    return d, float(r.max())


def trunk_axis_components(P):
    rows = trunk_components(P)
    run = bare_trunk(rows)
    if len(run) < 4:
        return None
    d, resid = robust_line([[r["cx"], r["y"], r["cz"]] for r in run])
    return {"up": d, "slices": len(run), "max_centroid_residual_m": round(resid, 3),
            "band_h_frac": [round(run[0]["h_frac"], 3), round(run[-1]["h_frac"], 3)],
            "slice_rows": [{k: round(v, 3) if isinstance(v, float) else v for k, v in r.items()} for r in rows]}


def base_plane(P, trunk_base_xz, rel_radius=0.2, rel_band=0.08):
    """Ground-contact plane under the trunk: lowest sample per footprint cell within rel_radius x H
    of the trunk base and the lowest rel_band x H, least squares. Needs a flared base or ground patch."""
    lo, hi = P.min(0), P.max(0)
    H = hi[1] - lo[1]
    near = P[(np.hypot(P[:, 0] - trunk_base_xz[0], P[:, 2] - trunk_base_xz[1]) < rel_radius * H)
             & (P[:, 1] < lo[1] + rel_band * H)]
    if len(near) < 50:
        return None
    cell = 0.012 * H
    ij = np.floor((near[:, [0, 2]] - near[:, [0, 2]].min(0)) / cell).astype(int)
    order = np.lexsort((near[:, 1], ij[:, 1], ij[:, 0]))
    ij, near = ij[order], near[order]
    first = np.r_[True, np.any(ij[1:] != ij[:-1], axis=1)]
    C = near[first]
    A = np.c_[C[:, 0], C[:, 2], np.ones(len(C))]
    coef = np.linalg.lstsq(A, C[:, 1], rcond=None)[0]
    rms = float(np.sqrt(np.mean((A @ coef - C[:, 1]) ** 2)))
    return {"up": unit([-coef[0], 1.0, -coef[1]]), "cells": int(len(C)), "rms_m": round(rms, 4),
            "rms_frac_of_height": round(rms / H, 4)}


def trunk_axis_core(P, band=(0.04, 0.25), slices=16):
    """Fallback check: running core centroid (points within 2x the slice's 20th-percentile radius
    of the previous centre), straight-line fit of centre vs height."""
    lo, hi = P.min(0), P.max(0)
    H = hi[1] - lo[1]
    edges = np.linspace(lo[1] + band[0] * H, lo[1] + band[1] * H, slices + 1)
    c, rows = None, []
    for y0, y1 in zip(edges[:-1], edges[1:]):
        s = P[(P[:, 1] >= y0) & (P[:, 1] < y1)]
        if len(s) < 30:
            continue
        if c is None:
            c = np.median(s[:, [0, 2]], 0)
        d = np.linalg.norm(s[:, [0, 2]] - c, axis=1)
        core = s[d < max(np.percentile(d, 20) * 2.0, 0.02 * H)]
        if len(core) >= 15:
            c = core[:, [0, 2]].mean(0)
        rows.append([c[0], (y0 + y1) / 2, c[1]])
    if len(rows) < 4:
        return None
    R = np.array(rows)
    X = np.c_[R[:, 1], np.ones(len(R))]
    sx = np.linalg.lstsq(X, R[:, 0], rcond=None)[0][0]
    sz = np.linalg.lstsq(X, R[:, 2], rcond=None)[0][0]
    return {"up": unit([sx, 1.0, sz]), "slices": len(rows), "band": list(band)}


# ------------------------------------------------------------------------------ stick estimators
def stick_axis(P, n, area):
    mu = P.mean(0)
    w, vec = np.linalg.eigh(np.cov((P - mu).T))
    axis = vec[:, -1] * np.sign(vec[1, -1])
    ratio = float(w[-1] / w[-2])
    caps = []
    for sgn in (1, -1):
        d, frac = dominant_normal(n, area, sgn * axis, cone=25)
        if d is not None:
            caps.append((sgn * d, frac))
    cap_up = unit(sum(d * f for d, f in caps)) if caps else None
    return {"up": axis, "eig_ratio": round(ratio, 1)}, (
        {"up": cap_up, "cap_area_frac": [round(f, 4) for _, f in caps]} if cap_up is not None else None)


# -------------------------------------------------------------------------------- orchestration
def kind_of(asset_id, meta):
    if asset_id in KINDS:
        return KINDS[asset_id]
    if asset_id.startswith("flora_") or meta.get("category") == "flora":
        return "tree"
    return "rigid"


def primary_estimate(V, F, kind):
    """The primary estimator alone, on the mesh as given."""
    n, area = face_data(V, F)
    if kind in ("rigid", "round"):
        R, score, per_axis = manhattan_frame(n, area)
        k = int(np.argmax(np.abs(R[1, :])))
        return {"method": "face_normal_frame", "up": R[:, k] * np.sign(R[1, k]), "frame": R, "up_axis_index": k,
                "frame_support_area_frac_8deg": round(score, 4), "per_axis_support": per_axis}
    if kind == "tree":
        p = trunk_axis_components(surface_samples(V, F))
        if p:
            p["method"] = "trunk_slice_components_bare_run"
        return p
    if kind == "stick":
        p = stick_axis(surface_samples(V, F), n, area)[0]
        p["method"] = "principal_axis"
        return p
    raise ValueError(kind)


def fixed_point(V, F, kind, iters=10, tol=0.05):
    """Re-measure on the corrected mesh until the correction stops changing. Height-banded
    estimators (the trunk run) are not rotation-equivariant, so a one-shot estimate on a tilted
    mesh can overshoot; the fixed point is the orientation in which the estimator reads level.
    Returns (R, last primary measured in the corrected frame, step sizes in deg)."""
    R = np.eye(3)
    steps = []
    p = None
    for _ in range(iters):
        p = primary_estimate(V @ R.T, F, kind)
        if p is None:
            return None, None, steps
        if kind == "rigid":
            step = box_candidates(p["frame"], p["up_axis_index"])[0]["R"]
        else:
            step = minimal_rotation(p["up"], UP)
        R = step @ R
        steps.append(round(rotation_angle(step), 3))
        if steps[-1] < tol:
            break
    if kind != "rigid":
        R = minimal_rotation(R.T @ UP, UP)     # up only: drop any twist the composition accumulated
    return R, p, steps


def estimate_up(mesh, kind):
    """Returns (primary, secondary, extra) where each estimator is a dict with an 'up' vector in the
    mesh's own frame. The primary is iterated to its fixed point (see fixed_point); the secondary is
    measured once, independently, on the mesh as given."""
    V, F = mesh["V"], mesh["F"]
    n, area = face_data(V, F)
    extra = {}
    R_fp, p_last, steps = fixed_point(V, F, kind)
    if R_fp is None:
        return None, None, extra
    primary = {k: v for k, v in p_last.items() if k not in ("up", "frame", "slice_rows")}
    primary.update({"up": R_fp.T @ UP, "R_fixed_point": R_fp, "fixed_point_steps_deg": steps,
                    "one_shot_up": describe_up(primary_estimate(V, F, kind)["up"]) if len(steps) > 1 else None})
    if kind == "tree":
        primary["slice_rows_levelled_frame"] = p_last.get("slice_rows")
    if kind in ("rigid", "round"):
        up = primary["up"]
        keep, pieces = body_faces(V, F)
        extra["pieces"] = pieces
        Fb = F[keep]
        rp = rest_pose(V, Fb, seed=up)
        secondary = {"method": "rest_pose_on_flat_ground_seeded_at_frame_up (body only, loose debris excluded)", **rp}
        drop = rest_pose(V, Fb, seed=UP)
        extra["rest_pose_dropped_as_is"] = describe_up(drop["up"]) | {"note": "settled pose if set down in its current orientation"}
        top, ftop = dominant_normal(n, area, primary["up"])
        bot, fbot = dominant_normal(n, area, -primary["up"])
        extra["top_faces"] = describe_up(top) | {"area_frac": round(ftop, 4)} if top is not None else None
        extra["bottom_faces"] = describe_up(-bot) | {"area_frac": round(fbot, 4)} if bot is not None else None
    elif kind == "tree":
        P = surface_samples(V, F)
        rows = trunk_components(P)
        secondary = None
        if rows:
            base = rows[0]
            bp = base_plane(P, (base["cx"], base["cz"]))
            if bp and bp["cells"] >= 60 and bp["rms_frac_of_height"] <= 0.012:
                secondary = {"method": "ground_contact_plane", **bp}
            else:
                extra["ground_contact_plane_rejected"] = ({k: v for k, v in bp.items() if k != "up"} | {"up": describe_up(bp["up"])}) if bp else None
            core = trunk_axis_core(P)
            if core:
                if secondary is None:
                    secondary = {"method": "trunk_core_centroid_line", **core}
                else:
                    extra["trunk_core_centroid_line"] = describe_up(core["up"])
            lo = P.min(0)
            H = P.max(0)[1] - lo[1]
            b = np.array([base["cx"], base["y"], base["cz"]])
            extra["base_to_canopy_centroid"] = describe_up(P[P[:, 1] > lo[1] + 0.5 * H].mean(0) - b)
            extra["base_to_mesh_centroid"] = describe_up(P.mean(0) - b)
    elif kind == "stick":
        P = surface_samples(V, F)
        _, secondary = stick_axis(P, n, area)
        if secondary:
            secondary["method"] = "end_cap_normals"
    else:
        raise ValueError(kind)
    return primary, secondary, extra


def box_candidates(frame, k_up):
    """The four proper rotations that take the measured frame to the world axes with its up on +Y,
    ordered by yaw: candidate 0 is the smallest total rotation, 1..3 add 90/180/270 deg about Y."""
    up = frame[:, k_up] * np.sign(frame[1, k_up])
    others = [frame[:, j] for j in range(3) if j != k_up]
    options = []
    for target in (np.array([1.0, 0, 0]), np.array([-1.0, 0, 0]), np.array([0, 0, 1.0]), np.array([0, 0, -1.0])):
        tb = np.cross(target, UP)
        src = np.c_[others[0], up, others[1]]
        for sgn in (1, -1):
            dst = np.c_[target, UP, sgn * tb]
            R = dst @ src.T
            if np.linalg.det(R) > 0:
                options.append(R)
                break
    options.sort(key=rotation_angle)
    base = options[0]
    cands = []
    for q in range(4):
        R = rot_about(UP, 90 * q) @ base
        # which measured frame direction now faces +Z (the game's forward)
        front_src = R.T @ np.array([0, 0, 1.0])
        cands.append({"index": q, "R": R, "rotation_deg": round(rotation_angle(R), 2),
                      "yaw_after_level_deg": 90 * q,
                      "front_source_dir": front_src.round(4).tolist(),
                      "front_source_azimuth_deg": round(math.degrees(math.atan2(front_src[0], front_src[2])), 1)})
    return cands


def ready_path(asset_id):
    return os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")


def to_local(p):
    """Metas record W:\\UNNAMED\\... (the mapped share); this machine sees the same tree at G:."""
    if not p:
        return p
    for prefix in ("W:\\UNNAMED\\", "W:/UNNAMED/"):
        if p.startswith(prefix):
            return os.path.join(os.path.dirname(ASSETS), p[len(prefix):])
    return p


def layer_checks(asset_id, meta, kind, primary_up):
    """Node transforms / hierarchy of the ready file, and the same primary estimator on the raw
    reconstruction and any archived intermediate, so the layer that introduced the tilt is measured."""
    out = {}
    layers = {}
    raw = to_local(meta.get("raw_3d_path") or meta.get("source"))
    src = to_local(meta.get("source"))
    for tag, p in (("raw_reconstruction", raw), ("meta_source", src),
                   ("archive_rescale", os.path.join(ASSETS, "archive", "rescale", asset_id, f"{asset_id}.glb"))):
        if not p or os.path.normcase(os.path.abspath(p)) == os.path.normcase(ready_path(asset_id)):
            continue
        if tag == "meta_source" and raw and os.path.normcase(p) == os.path.normcase(raw):
            continue
        if not os.path.exists(p):
            layers[tag] = {"path": p, "exists": False}
            continue
        m = load_mesh(p)
        prim, _, _ = estimate_up(m, kind)
        up = prim["up"] if prim else None
        layers[tag] = {"path": p, "exists": True,
                       "generator": m["js"]["asset"].get("generator"),
                       "mesh_nodes_world_identity": all(nd["world_is_identity"] for nd in m["nodes"] if nd["mesh"] is not None),
                       "bbox": bbox(m["V"]),
                       "primary_up": describe_up(up) if up is not None else None,
                       "delta_vs_ready_deg": round(angle_deg(up, primary_up), 2) if up is not None else None}
    out["earlier_layers"] = layers
    return out


def estimate(args):
    asset_id = args.asset
    path = ready_path(asset_id)
    meta_path = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_meta.json")
    meta = json.load(open(meta_path, encoding="utf-8")) if os.path.exists(meta_path) else {}
    kind = args.kind or kind_of(asset_id, meta)
    mesh = load_mesh(path)
    V = mesh["V"]
    out_path = os.path.join(STAGING, asset_id, f"{asset_id}_orientation.json")
    old_rec = json.load(open(out_path, encoding="utf-8")) if os.path.exists(out_path) else {}
    primary, secondary, extra = estimate_up(mesh, kind)

    rec = {"asset_id": asset_id, "source_glb": path, "source_sha256": hashlib.sha256(open(path, "rb").read()).hexdigest(),
           "kind": kind, "category": meta.get("category"), "frame": "glTF export frame: X right, Y up, Z forward (toward concept camera)",
           "nodes": mesh["nodes"], "scene_roots": mesh["scene_roots"],
           "mesh_nodes_world_identity": all(nd["world_is_identity"] for nd in mesh["nodes"] if nd["mesh"] is not None),
           "bbox_before": bbox(V), "vertices": int(len(V)), "triangles": int(len(mesh["F"]))}

    flags = []
    if asset_id.startswith("weapon") or meta.get("category") == "weapon":
        flags.append("weapon: not corrected by policy")
    if primary is None or secondary is None:
        flags.append("an estimator failed to produce an axis")
    est = {}
    for tag, e in (("primary", primary), ("secondary", secondary)):
        if e is None:
            est[tag] = None
            continue
        clean = {k: v for k, v in e.items() if k not in ("up", "frame", "R_fixed_point")}
        est[tag] = {**clean, "up": describe_up(e["up"])}
    rec["estimators"] = est
    rec["diagnostics"] = {k: v for k, v in extra.items() if v is not None}

    disagreement = angle_deg(primary["up"], secondary["up"]) if primary and secondary else None
    rec["estimator_disagreement_deg"] = round(disagreement, 2) if disagreement is not None else None

    if primary is not None:
        up = unit(primary["up"])
        if kind == "rigid":
            base = primary["R_fixed_point"]
            cands = []
            for q in range(4):
                Rq = rot_about(UP, 90 * q) @ base
                f = Rq.T @ np.array([0, 0, 1.0])
                cands.append({"index": q, "R": Rq, "rotation_deg": round(rotation_angle(Rq), 2), "yaw_after_level_deg": 90 * q,
                              "front_source_dir": f.round(4).tolist(),
                              "front_source_azimuth_deg": round(math.degrees(math.atan2(f[0], f[2])), 1)})
            rec["candidates"] = [{k: (v.round(7).tolist() if isinstance(v, np.ndarray) else v) for k, v in c.items()}
                                 for c in cands]
            prev = old_rec.get("yaw_choice") or {}
            if prev.get("how", "").startswith("visual") and prev.get("candidate") is not None:
                chosen = prev["candidate"]
                rec["yaw_choice"] = prev
            else:
                chosen = 0
                rec["yaw_choice"] = {"candidate": chosen, "how": "default: smallest total rotation; pending visual front check"}
            R = with_up_source(rec, cands[chosen]["R"], rec["yaw_choice"].get("up_source", "primary"))
        else:
            R = minimal_rotation(up, UP)
            rec["yaw_choice"] = {"candidate": None, "how": f"{kind}: minimal rotation, yaw left as reconstructed"}
        level = minimal_rotation(up, UP)
        tilt = angle_deg(up, UP)
        if tilt > MAX_CORRECTION_DEG:
            flags.append(f"level tilt {tilt:.1f} deg > {MAX_CORRECTION_DEG} (gate covers the tilt only)")
        if disagreement is not None and disagreement > MAX_DISAGREE_DEG:
            flags.append(f"estimators disagree by {disagreement:.1f} deg > {MAX_DISAGREE_DEG}")
        support = primary.get("frame_support_area_frac_8deg")
        if disagreement is None:
            conf = 0.0
        else:
            conf = max(0.0, 1.0 - disagreement / MAX_DISAGREE_DEG)
            if support is not None:
                conf *= min(1.0, support / 0.35)
        rec["correction"] = {
            "measured_up": describe_up(up),
            "level_tilt_deg": round(tilt, 2),
            "R": R.round(7).tolist(),
            "quaternion_xyzw": quaternion_xyzw(R),
            "total_rotation_deg": round(rotation_angle(R), 2),
            "total_rotation_axis": rotation_axis(R),
            "yaw_after_level_deg": round(math.degrees(math.atan2((R @ level.T)[0, 2], (R @ level.T)[0, 0])), 2),
            "applied_up": describe_up(R.T @ UP),
            "gates": {"level_tilt_deg_max": MAX_CORRECTION_DEG, "estimator_disagreement_deg_max": MAX_DISAGREE_DEG,
                      "tilt_gate_covers": "level_tilt_deg only (primary up vs +Y)",
                      "total_rotation_deg": "recorded, not gated: tilt plus the free 90-deg yaw choice"},
            "confidence": round(conf, 3),
            "confidence_label": "refused" if flags else ("high" if conf >= 0.6 else "medium" if conf >= 0.25 else "low"),
        }
        # size effect, measured on the vertices (translation does not change extents)
        Vn = V @ R.T
        rec["bbox_after_estimate"] = bbox(Vn - np.r_[0.0, Vn[:, 1].min(), 0.0])
        rec["layers"] = layer_checks(asset_id, meta, kind, up)
    rec["flags"] = flags
    rec["status"] = "refused" if flags else "estimated"

    outdir = os.path.join(STAGING, asset_id)
    os.makedirs(outdir, exist_ok=True)
    out = os.path.join(outdir, f"{asset_id}_orientation.json")
    old = json.load(open(out, encoding="utf-8")) if os.path.exists(out) else {}
    for keep in ("judgement", "classification"):
        if keep in old:
            rec[keep] = old[keep]
    json.dump(rec, open(out, "w", encoding="utf-8"), indent=1)
    c = rec.get("correction", {})
    print(f"{asset_id} [{kind}] tilt {c.get('level_tilt_deg')} deg  rot {c.get('total_rotation_deg')}  "
          f"disagree {rec['estimator_disagreement_deg']}  conf {c.get('confidence')} {c.get('confidence_label')}  "
          f"bbox {rec['bbox_before']['size_xyz']} -> {rec.get('bbox_after_estimate', {}).get('size_xyz')}  flags {flags}")
    return rec


def with_up_source(rec, R, up_source):
    """Candidate rotations level the face-normal frame. With up_source 'secondary' the up comes from
    the rest pose instead (the body sits on its supports) while the frame's yaw alignment is kept:
    R' = minimal_rotation(R @ rest_up -> +Y) @ R."""
    if up_source != "secondary":
        return R
    rest_up = np.array(rec["estimators"]["secondary"]["up"]["dir"])
    return minimal_rotation(R @ rest_up, UP) @ R


def choose(args):
    out = os.path.join(STAGING, args.asset, f"{args.asset}_orientation.json")
    rec = json.load(open(out, encoding="utf-8"))
    cand = rec["candidates"][args.candidate]
    R = with_up_source(rec, np.array(cand["R"]), args.up)
    level = minimal_rotation(R.T @ UP, UP)
    rec["correction"].update({
        "R": R.round(7).tolist(), "quaternion_xyzw": quaternion_xyzw(R),
        "total_rotation_deg": round(rotation_angle(R), 2), "total_rotation_axis": rotation_axis(R),
        "yaw_after_level_deg": round(math.degrees(math.atan2((R @ level.T)[0, 2], (R @ level.T)[0, 0])), 2)})
    rec["yaw_choice"] = {"candidate": args.candidate, "how": "visual: rendered all four candidates from +Z",
                         "reason": args.reason, "front_source_azimuth_deg": cand["front_source_azimuth_deg"],
                         "up_source": args.up, "up_reason": args.up_reason}
    rec["correction"]["applied_up"] = describe_up(R.T @ UP)
    json.dump(rec, open(out, "w", encoding="utf-8"), indent=1)
    print(f"{args.asset}: candidate {args.candidate} chosen ({args.reason})")


def verify(args):
    """Check the staged GLB against the ready LOD0: same topology and UVs, positions and normals equal
    to R applied to the originals (plus the grounding translation), images byte-identical, material
    unchanged, mesh node identity, grounded and centred, residual tilt re-measured."""
    asset_id = args.asset
    outdir = os.path.join(STAGING, asset_id)
    out = os.path.join(outdir, f"{asset_id}_orientation.json")
    rec = json.load(open(out, encoding="utf-8"))
    R = np.array(rec["correction"]["R"])
    a = load_mesh(ready_path(asset_id))
    b = load_mesh(os.path.join(outdir, f"{asset_id}.glb"))
    res = {}
    res["vertices"] = [len(a["V"]), len(b["V"])]
    res["triangles"] = [len(a["F"]), len(b["F"])]
    Va = a["V"] @ R.T
    t = np.array([-(Va[:, 0].min() + Va[:, 0].max()) / 2, -Va[:, 1].min(), -(Va[:, 2].min() + Va[:, 2].max()) / 2])
    Va = Va + t

    # Compare per triangle corner (the round trip keeps triangle order; checked by the position error).
    # Normal gate: every corner, no exemptions. A source normal of (0,1,0) (a placeholder an earlier
    # Blender export wrote) is rotated like any other, so the levelled file is the ready file rotated.
    failures = []
    if len(a["F"]) != len(b["F"]):
        failures.append(f"triangle count changed {len(a['F'])} -> {len(b['F'])}: corner normals cannot be compared")
    else:
        ca, cb = a["F"].ravel(), b["F"].ravel()
        res["max_corner_position_error_m"] = float(np.abs(Va[ca] - b["V"][cb]).max())
        res["max_corner_uv_error"] = float(np.abs(a["UV"][ca] - b["UV"][cb]).max())
        if res["max_corner_position_error_m"] > MAX_CORNER_POSITION_ERROR_M:
            failures.append(f"corner positions differ by up to {res['max_corner_position_error_m']:.3g} m "
                            f"(> {MAX_CORNER_POSITION_ERROR_M}): corner correspondence broken, normals not comparable")
        elif a["N"] is None and b["N"] is not None:
            failures.append("source has no NORMAL but the staged file does")
        elif a["N"] is not None and b["N"] is None:
            failures.append("staged file lost the NORMAL attribute")
        elif a["N"] is not None:
            cosang = np.clip(np.einsum("ij,ij->i", unit_rows(a["N"][ca] @ R.T), unit_rows(b["N"][cb])), -1, 1)
            err = np.degrees(np.arccos(cosang))
            worst = int(np.argmax(err))
            over = err > MAX_CORNER_NORMAL_ERROR_DEG
            res["max_corner_normal_error_deg"] = round(float(err.max()), 4)
            res["worst_corner"] = {"corner": worst, "source_vertex": int(ca[worst]), "staged_vertex": int(cb[worst]),
                                   "source_normal": a["N"][ca[worst]].round(5).tolist(),
                                   "expected_R_source": unit_rows(a["N"][ca[worst]][None] @ R.T)[0].round(5).tolist(),
                                   "staged_normal": b["N"][cb[worst]].round(5).tolist()}
            res["corners_normal_error_over_1deg"] = int(over.sum())
            res["source_vertices_over_1deg"] = sorted(set(ca[over].tolist()))[:50]
            res["source_corners_with_normal_0_1_0"] = int(np.all(a["N"][ca] == [0.0, 1.0, 0.0], 1).sum())
            if over.any():
                failures.append(f"{int(over.sum())} corners have a normal more than {MAX_CORNER_NORMAL_ERROR_DEG} deg from "
                                f"R @ source normal (max {err.max():.2f} deg at source vertex {int(ca[worst])})")
    res["normal_gate"] = {"max_corner_normal_error_deg": MAX_CORNER_NORMAL_ERROR_DEG, "applies_to": "every triangle corner",
                          "passed": not failures, "failures": failures}
    res["translation_applied"] = t.round(5).tolist()
    res["bbox_after"] = bbox(b["V"])
    lo, hi = b["V"].min(0), b["V"].max(0)
    res["grounded_min_y"] = round(float(lo[1]), 6)
    res["centre_xz"] = [round(float((lo[0] + hi[0]) / 2), 6), round(float((lo[2] + hi[2]) / 2), 6)]
    res["mesh_nodes_world_identity"] = all(nd["world_is_identity"] for nd in b["nodes"] if nd["mesh"] is not None)

    def imgs(m):
        js, bn = m["js"], m["binary"]
        out_ = []
        for im in js.get("images", []):
            v = js["bufferViews"][im["bufferView"]]
            out_.append(hashlib.sha256(bn[v.get("byteOffset", 0):v.get("byteOffset", 0) + v["byteLength"]]).hexdigest())
        return sorted(out_)
    res["images_identical"] = imgs(a) == imgs(b)
    res["image_count"] = [len(a["js"].get("images", [])), len(b["js"].get("images", []))]
    strip = lambda js: json.dumps({k: js.get(k) for k in ("materials", "samplers")}, sort_keys=True)
    res["materials_samplers_identical"] = strip(a["js"]) == strip(b["js"])
    res["textures"] = [len(a["js"].get("textures", [])), len(b["js"].get("textures", []))]
    res["socket_nodes"] = [{"name": n["name"], **n["local_trs"]} for n in b["nodes"] if (n["name"] or "").startswith("SOCK_")]
    # Residuals: one-shot primary and the independent secondary, re-measured on the staged file.
    prim = primary_estimate(b["V"], b["F"], rec["kind"])
    _, sec, _ = estimate_up(b, rec["kind"])
    res["residual_tilt_primary_deg"] = round(angle_deg(prim["up"], UP), 2) if prim else None
    res["residual_tilt_secondary_deg"] = round(angle_deg(sec["up"], UP), 2) if sec else None
    if rec["kind"] == "rigid" and prim is not None:
        F = prim["frame"]
        res["residual_frame_axes_vs_world_deg"] = [round(min(angle_deg(F[:, j], e) for e in
                                                            (np.eye(3)[i] * s for i in range(3) for s in (1, -1))), 2)
                                                   for j in range(3)]
    rec["verification"] = res
    json.dump(rec, open(out, "w", encoding="utf-8"), indent=1)
    print(json.dumps(res, indent=1))
    if failures:
        print(f"VERIFY FAIL {asset_id}: " + "; ".join(failures), file=sys.stderr)
        sys.exit(1)
    print(f"VERIFY PASS {asset_id}: max corner normal error {res.get('max_corner_normal_error_deg')} deg")


def unit_rows(X):
    return X / np.maximum(np.linalg.norm(X, axis=1, keepdims=True), 1e-12)


def main():
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    e = sub.add_parser("estimate")
    e.add_argument("--asset", required=True)
    e.add_argument("--kind", choices=["rigid", "round", "tree", "stick"])
    c = sub.add_parser("choose")
    c.add_argument("--asset", required=True)
    c.add_argument("--candidate", type=int, required=True)
    c.add_argument("--reason", required=True)
    c.add_argument("--up", choices=["primary", "secondary"], default="primary",
                   help="level by the face-normal frame (primary) or by the rest pose (secondary), keeping the frame's yaw")
    c.add_argument("--up-reason", default=None)
    v = sub.add_parser("verify")
    v.add_argument("--asset", required=True)
    args = p.parse_args()
    {"estimate": estimate, "choose": choose, "verify": verify}[args.cmd](args)


if __name__ == "__main__":
    main()
