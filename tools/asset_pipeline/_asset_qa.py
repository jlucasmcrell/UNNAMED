"""Objective quality signals for Otherreach GLB assets. Flags are review triggers, not auto-reject gates.

Python + numpy (scipy's KD-tree for piece gaps): GLBs are read directly, no Blender. Every measurement is taken in
the file's own glTF frame (+Y up, +Z model forward, metres), the frame the game draws, with node transforms applied
(skinned primitives are posed by joint world x inverseBindMatrix, as glTF requires, and ignore their own node
transform).

Measured per GLB
  materials    material / texture / image counts, primitives without a material, primitives without TEXCOORD_0,
               material or texture indices out of range.
  topology     on geometry welded by POSITION (quantised to WELD_QUANTUM_M). LOD0 meshes are split into UV-chart
               islands, so index-level open edges are UV seams, not holes; welding measures the surface itself.
               Unwelded and welded vert/tri, welded boundary-edge share, non-manifold edges, duplicate directed edges
               (two faces on one edge with the same winding), connected components and the largest one's triangle
               share, degenerate-triangle share, detached pieces (area share, and each piece's distance from the
               main body; pieces resting on the ground beside a grounded body are not detached).
  orientation  up tilt from the dominant flat faces (area-weighted face-normal clusters), principal axes of the
               area-weighted surface, footprint yaw from the minimum-area rectangle of the XZ hull, and the plane
               through the lowest points per footprint cell (the tilt_check.py method). Flags on every LOD0, rigged
               assets' LOD0 included; the rigged GLB gets the yaw and diagonal flags only (ORIENT_CHECKS).
  placement    min Y against 0 (grounding); longest side of the tightest oriented box against meta target_size_m.
  lods         each _lodN file against LOD0 and the meta's requested budget.
  skin         deform joints far from every vertex (vs mesh height), joints above the mesh top, deform joints that
               dominate no vertex.
               LIMITATION: the rig-fit test only asks whether a joint is near the surface or enclosed by it. A joint
               that sits inside the WRONG body part passes: npc_ondrek_stonecarver's upper_arm, forearm and hand
               joints lie inside the torso about 0.4 m inboard of the arms and are not flagged. Catching that needs
               the joint compared with the vertices it drives (weights), which is not measured here.

Thresholds and the reason for each are in THRESHOLDS; flags carry the measured value and the threshold.

Usage
  python _asset_qa.py --all [--workers 8]             whole library -> assets/_staging/qa/assets/<id>.json + report
  python _asset_qa.py --ids container_barrel_oak ...  selected assets, same outputs
  python _asset_qa.py --glb <file.glb> [--kind lod0|lod|rigged] [--target 0.85]   one file, JSON to stdout
"""
import argparse
import json
import math
import os
import struct
import sys
import time
from concurrent.futures import ProcessPoolExecutor, as_completed

import numpy as np
from scipy.spatial import cKDTree

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.environ.get("UNNAMED_ASSETS") or os.path.normpath(os.path.join(HERE, "..", "..", "assets"))
QA_VERSION = "1.1"

WELD_QUANTUM_M = 1e-5
LOD_SUFFIXES = ("_lod1", "_lod2", "_lod3")

# Each threshold: value, and why it sits there (calibrated on the named cases; see _asset_qa_report.py).
THRESHOLDS = {
    "lod_budget_factor": (1.25, "LOD triangles may overshoot the requested budget by 25%; decimation lands near but "
                                "not exactly on its target (the task's stated tolerance)."),
    "lod_bounds_drift": (0.05, "Largest shift of any LOD AABB face vs LOD0, as a share of LOD0's longest AABB side. "
                               "A LOD that moves or shrinks its silhouette by 5% pops when it swaps in."),
    "boundary_share": (0.05, "Welded open edges / all edges. Open edges are fine on their own: the good barrel "
                             "is 1.0% and the good cauldron 1.7%, while the failed winch is 0.34%. 5% only catches "
                             "sheet geometry and cracked meshes (the kit pieces at 62.5%, every LOD at 18-72%)."),
    "nonmanifold_share": (0.05, "Welded edges shared by 3+ faces / all edges (mostly 4-face pinches where two "
                                "sheets touch). Not separating: good barrel 2.4% = failed winch 2.4%; failed shaft "
                                "4.3%. Set above the library's 90th percentile (4.1%) as a gross-defect trigger."),
    "winding_conflict_share": (0.002, "2-face edges whose faces use the edge in the same direction / all edges: "
                                      "true inverted normals. Good LOD0s 0-0.10% (cauldron), failed shaft 0.25%."),
    "fold_share": (0.20, "Length share of consistently wound 2-face edges whose faces turn back more than 120 deg: "
                         "the thin-shell / crumple signature. Good barrel 13.7%, billet 0%; failed winch 27.0%, "
                         "shaft 32.1%. The library is bimodal with a trough at 8-20% (15 of 500 assets)."),
    "detached_area_share": (0.05, "Area share of pieces >= 0.5% of the surface not linked to the largest piece "
                                  "through gaps <= max(1 cm, 1.5% of the diagonal). The weak cart's floating slabs "
                                  "are 10% at 8 cm; double-layer shells (billet) and barrel hoops sit 1-8 mm apart; "
                                  "the winch's members sit 0.5-1.4 cm apart, 0.5% of its diagonal. Pieces resting on "
                                  "the ground beside a grounded main body (the balance scales' loose weights) are "
                                  "placed, not detached, and are not counted."),
    "floating_piece_gaps": (2.0, "A detached piece of at least 0.5% of the area whose linked group is this many "
                                 "link gaps or more from the main body floats free, whatever the total share. "
                                 "npc_ondrek_stonecarver's rocks sit 6.6-6.8 gaps (0.29-0.30 m) behind it; the "
                                 "smallest clear floater, magic_ward_stone_small's rod, 2.16. Parts at 1.4-1.9 gaps "
                                 "nearly touch (paddle on a vat rim 1.39, wheel off its axle 1.58, hams' pole 1.65), "
                                 "and the area share decides those."),
    "largest_component_area_share": (0.40, "Area share of the biggest welded piece. Not the task's 'dominant piece' "
                                           "test: the good billet, waystone and cauldron are double-layer shells "
                                           "split 50/50 into nested pieces 1-8 mm apart, and the failed winch is "
                                           "0.79. Only a mesh with no piece holding 40% is flagged."),
    "tiny_component_tri_share": (0.02, "Share of triangles in pieces of 8 or fewer triangles: shards. Good LOD0s "
                                       "are 0-0.02%; cracked LODs reach 16-61%."),
    "degenerate_share": (0.01, "Zero-area or collinear triangles / all triangles (after welding). Every current "
                               "LOD0 measures 0; anything at 1% is an export fault."),
    "tilt_deg": (8.0, "Up tilt from the dominant flat faces. Authored and level kit pieces measure under ~2 deg; "
                      "Pixal3D camera-pitch leans are 10-35 deg. Every LOD0 (rigged assets' LOD0 included); "
                      "not the skinned rigged GLB, whose flat faces are posed limbs."),
    "flat_min_share": (0.02, "A flat-face cluster must hold at least 2% of the surface area to define up; below "
                             "that the object has no dominant flat face and tilt is reported as not measurable."),
    "flat_min_core": (0.35, "Share of a cluster's area within 4 deg of its mean normal. Measured flat faces sit at "
                            "0.41-1.0 (anvil top 0.41, chest lid 0.79, kit walls 1.0); curved bands on the barrel, "
                            "the ash haft and the bench's splayed legs sit at 0.13-0.30. Only side faces used to "
                            "derive up from a box frame must pass it."),
    "diagonal_axis_deg": (10.0, "An elongated asset (principal length ratio >= diagonal_min_elongation) whose "
                                "major axis is more than 10 deg off every world axis lies diagonally."),
    "diagonal_min_elongation": (2.0, "sqrt(l1/l2) of the area-weighted surface: at 2x the long axis is well defined."),
    "yaw_deg": (10.0, "Footprint rectangle rotated more than 10 deg off the X/Z axes; Pixal3D yaws rectangular "
                      "props 39-56 deg. Only when the footprint is rectangular or elongated."),
    "yaw_min_fill": (0.85, "Hull area / min-area-rectangle area at or above 0.85 counts as a rectangular footprint "
                           "(a disc is 0.785, so round props never trigger)."),
    "yaw_min_aspect": (1.5, "A footprint 1.5x longer than wide has a defined yaw even when not rectangular."),
    "base_plane_tilt_deg": (15.0, "Plane through the lowest point per footprint cell (tilt_check.py). It correlates "
                                  "only 0.33 with the flat-face tilt, halves the chest's lean and reads 40-77 deg on "
                                  "diagonal weapons, so it flags only compact assets with no flat-face up (trees, "
                                  "rocks, organic props), at 15 deg."),
    "ground_tol_m": (0.005, "min Y may differ from 0 by 5 mm (or ground_tol_frac of the height, whichever is "
                            "larger) before the asset floats or sinks."),
    "ground_tol_frac": (0.01, "1% of the height; decimated LODs move the lowest vertex slightly."),
    "scale_ratio_hi": (1.15, "Longest side of the tightest oriented box / target_size_m. The pipeline scales the "
                             "tilted AABB, so a diagonal asset is up to 1.41x its target."),
    "scale_ratio_lo": (0.85, "Same ratio, low side: under-sized against the declared target."),
    "joint_gap_frac": (0.10, "Nearest-vertex distance of a non-root skin joint / mesh height, AND at least one of "
                             "26 rays from the joint escapes the mesh. npc_kal_smith's head is 0.231 off with 26/26 "
                             "rays escaping; npc_veth_magistrate's worst is 0.044. Ondrek spines sit 0.14-0.15 off "
                             "inside a hollow torso with 0 rays escaping, so distance alone would misfire."),
    "joint_above_frac": (0.02, "A non-root joint more than 2% of the height above the mesh top (kal_smith's head is "
                               "0.231 above; normal heads sit 0.11 below the top)."),
    "zero_dominance_max": (2, "Non-root skin joints that are the strongest influence on no vertex, weightless "
                              "joints included. Healthy humanoids have 0-2 (16 of 26 have 0); kal_smith has 6 and "
                              "the yawed quadrupeds mostly 3-11."),
}


def T(name):
    return THRESHOLDS[name][0]


# ----------------------------------------------------------------------------------------------------------- GLB I/O
_DTYPE = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}


def read_glb(path):
    with open(path, "rb") as handle:
        blob = handle.read()
    if len(blob) < 20 or blob[:4] != b"glTF":
        raise ValueError("not a GLB")
    doc, binary = None, b""
    offset = 12
    while offset + 8 <= len(blob):
        length, kind = struct.unpack_from("<II", blob, offset)
        chunk = blob[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            doc = json.loads(chunk.decode("utf-8"))
        elif kind == 0x004E4942 and not binary:
            binary = chunk
        offset += 8 + length + ((-length) % 4)
    if doc is None:
        raise ValueError("no JSON chunk")
    return doc, binary


def _view_array(doc, binary, view_index, byte_offset, dtype, ncomp, count):
    view = doc["bufferViews"][view_index]
    if view.get("buffer", 0) != 0 or "uri" in doc["buffers"][view.get("buffer", 0)]:
        raise ValueError("external buffers are not supported")
    start = view.get("byteOffset", 0) + byte_offset
    item = dtype.itemsize * ncomp
    stride = view.get("byteStride") or item
    if count == 0:
        return np.zeros((0, ncomp), dtype)
    if stride == item:
        return np.frombuffer(binary, dtype, count * ncomp, start).reshape(count, ncomp)
    raw = np.frombuffer(binary, np.uint8, stride * (count - 1) + item, start)
    rows = np.lib.stride_tricks.as_strided(raw, (count, item), (stride, 1))
    return np.ascontiguousarray(rows).view(dtype).reshape(count, ncomp)


def accessor(doc, binary, index):
    a = doc["accessors"][index]
    dtype = np.dtype(_DTYPE[a["componentType"]]).newbyteorder("<")
    ncomp = _NCOMP[a["type"]]
    count = a["count"]
    if "bufferView" in a:
        out = _view_array(doc, binary, a["bufferView"], a.get("byteOffset", 0), dtype, ncomp, count)
    else:
        out = np.zeros((count, ncomp), dtype)
    if "sparse" in a:
        sp = a["sparse"]
        idt = np.dtype(_DTYPE[sp["indices"]["componentType"]]).newbyteorder("<")
        idx = _view_array(doc, binary, sp["indices"]["bufferView"], sp["indices"].get("byteOffset", 0), idt, 1,
                          sp["count"]).reshape(-1).astype(np.int64)
        vals = _view_array(doc, binary, sp["values"]["bufferView"], sp["values"].get("byteOffset", 0), dtype, ncomp,
                           sp["count"])
        out = out.copy()
        out[idx] = vals
    if a.get("normalized") and dtype.kind in "iu":
        info = np.iinfo(dtype)
        out = out.astype(np.float64) / info.max
        if dtype.kind == "i":
            out = np.maximum(out, -1.0)
    return out


def node_matrix(node):
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    m = np.eye(4)
    x, y, z, w = node.get("rotation", [0.0, 0.0, 0.0, 1.0])
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m[:3, :3] = r * np.array(node.get("scale", [1.0, 1.0, 1.0]))
    m[:3, 3] = node.get("translation", [0.0, 0.0, 0.0])
    return m


def world_matrices(doc):
    nodes = doc.get("nodes", [])
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            parent[c] = i
    local = [node_matrix(n) for n in nodes]
    world = [None] * len(nodes)
    for i in range(len(nodes)):
        chain, j = [], i
        while j is not None and world[j] is None:
            chain.append(j)
            j = parent.get(j)
        m = np.eye(4) if j is None else world[j]
        for k in reversed(chain):
            m = m @ local[k]
            world[k] = m
    return world, parent


def scene_node_order(doc, parent):
    nodes = doc.get("nodes", [])
    scenes = doc.get("scenes")
    if scenes:
        roots = list(scenes[doc.get("scene", 0)].get("nodes", []))
    else:
        roots = [i for i in range(len(nodes)) if i not in parent]
    order, stack = [], list(reversed(roots))
    while stack:
        i = stack.pop()
        order.append(i)
        stack.extend(reversed(nodes[i].get("children", [])))
    return order


def primitive_triangles(doc, binary, prim, nverts):
    mode = prim.get("mode", 4)
    if "indices" in prim:
        idx = accessor(doc, binary, prim["indices"]).reshape(-1).astype(np.int64)
    else:
        idx = np.arange(nverts, dtype=np.int64)
    if mode == 4:
        return idx[:len(idx) // 3 * 3].reshape(-1, 3)
    if mode == 5 and len(idx) >= 3:
        tri = np.stack([idx[:-2], idx[1:-1], idx[2:]], 1)
        odd = np.arange(len(tri)) % 2 == 1
        tri[odd] = tri[odd][:, [1, 0, 2]]
        return tri
    if mode == 6 and len(idx) >= 3:
        return np.stack([np.full(len(idx) - 2, idx[0]), idx[1:-1], idx[2:]], 1)
    return np.zeros((0, 3), np.int64)


def _skin(doc, binary, node, prim, world, positions):
    """Bind-pose skinned positions plus per-vertex joint indices / weights."""
    skin = doc["skins"][node["skin"]]
    joints = skin["joints"]
    if "inverseBindMatrices" in skin:
        ibm = accessor(doc, binary, skin["inverseBindMatrices"]).astype(np.float64).reshape(-1, 4, 4).transpose(0, 2, 1)
    else:
        ibm = np.tile(np.eye(4), (len(joints), 1, 1))
    jm = np.array([world[j] for j in joints]) @ ibm
    attrs = prim["attributes"]
    js, ws = [], []
    k = 0
    while f"JOINTS_{k}" in attrs and f"WEIGHTS_{k}" in attrs:
        js.append(accessor(doc, binary, attrs[f"JOINTS_{k}"]).astype(np.int64))
        w = accessor(doc, binary, attrs[f"WEIGHTS_{k}"])
        ws.append(w.astype(np.float64))
        k += 1
    J = np.concatenate(js, 1)
    W = np.concatenate(ws, 1)
    out = np.zeros_like(positions)
    wsum = np.zeros(len(positions))
    for s in range(J.shape[1]):
        m = W[:, s] > 0
        if not m.any():
            continue
        M = jm[J[m, s]]
        v = np.einsum("nij,nj->ni", M[:, :3, :3], positions[m]) + M[:, :3, 3]
        out[m] += W[m, s, None] * v
        wsum[m] += W[m, s]
    ok = wsum > 0
    out[ok] /= wsum[ok, None]
    out[~ok] = positions[~ok]
    return out, J, W, node["skin"]


def load_geometry(path):
    """World-space positions, triangles, per-primitive material facts and skin data for everything the scene draws."""
    doc, binary = read_glb(path)
    world, parent = world_matrices(doc)
    order = scene_node_order(doc, parent)
    nodes = doc.get("nodes", [])
    pos, tris, prims = [], [], []
    skin = {"J": [], "W": [], "skin": None, "offsets": []}
    offset = 0
    for ni in order:
        node = nodes[ni]
        if "mesh" not in node:
            continue
        for pi, prim in enumerate(doc["meshes"][node["mesh"]].get("primitives", [])):
            attrs = prim.get("attributes", {})
            prims.append({"node": ni, "mesh": node["mesh"], "primitive": pi, "material": prim.get("material"),
                          "attributes": sorted(attrs), "mode": prim.get("mode", 4)})
            if "POSITION" not in attrs:
                continue
            P = accessor(doc, binary, attrs["POSITION"]).astype(np.float64)
            tri = primitive_triangles(doc, binary, prim, len(P))
            if "skin" in node and "JOINTS_0" in attrs and "WEIGHTS_0" in attrs:
                P, J, W, si = _skin(doc, binary, node, prim, world, P)
                skin["J"].append(J)
                skin["W"].append(W)
                skin["offsets"].append((offset, len(P)))
                skin["skin"] = si if skin["skin"] is None else skin["skin"]
            else:
                M = world[ni]
                P = P @ M[:3, :3].T + M[:3, 3]
            pos.append(P)
            tris.append(tri + offset)
            offset += len(P)
    P = np.concatenate(pos) if pos else np.zeros((0, 3))
    F = np.concatenate(tris) if tris else np.zeros((0, 3), np.int64)
    return doc, P, F, prims, skin, world


# ------------------------------------------------------------------------------------------------------- materials
_TEX_KEYS = ("baseColorTexture", "metallicRoughnessTexture", "normalTexture", "occlusionTexture", "emissiveTexture")


def material_facts(doc, prims):
    n_mat = len(doc.get("materials", []))
    n_tex = len(doc.get("textures", []))
    n_img = len(doc.get("images", []))
    tex_refs_bad = 0
    for m in doc.get("materials", []):
        refs = [m.get(k) for k in _TEX_KEYS] + [m.get("pbrMetallicRoughness", {}).get(k) for k in _TEX_KEYS]
        for r in refs:
            if isinstance(r, dict) and not (0 <= r.get("index", -1) < n_tex):
                tex_refs_bad += 1
    for t in doc.get("textures", []):
        if "source" in t and not (0 <= t["source"] < n_img):
            tex_refs_bad += 1
    return {
        "materials": n_mat, "textures": n_tex, "images": n_img, "primitives": len(prims),
        "primitives_without_material": sum(1 for p in prims if p["material"] is None),
        "primitives_without_texcoord0": sum(1 for p in prims if "TEXCOORD_0" not in p["attributes"]),
        "material_index_out_of_range": sum(1 for p in prims if p["material"] is not None
                                           and not (0 <= p["material"] < n_mat)),
        "texture_refs_out_of_range": tex_refs_bad,
        "non_triangle_primitives": sum(1 for p in prims if p["mode"] not in (4, 5, 6)),
    }


# -------------------------------------------------------------------------------------------------------- topology
def weld(P, quantum=WELD_QUANTUM_M):
    q = np.round(P / quantum).astype(np.int64)
    uniq, inverse = np.unique(q, axis=0, return_inverse=True)
    return uniq.astype(np.float64) * quantum, inverse.reshape(-1)


def _components(nv, u, v):
    """Union-find by root hooking + pointer jumping; returns a root label per vertex."""
    L = np.arange(nv)
    while True:
        lu, lv = L[u], L[v]
        diff = lu != lv
        if not diff.any():
            return L
        lo = np.minimum(lu[diff], lv[diff])
        hi = np.maximum(lu[diff], lv[diff])
        np.minimum.at(L, hi, lo)
        while True:
            nxt = L[L]
            if np.array_equal(nxt, L):
                break
            L = nxt


def _edge_counts(F, nv):
    a, b, c = F[:, 0], F[:, 1], F[:, 2]
    src = np.concatenate([a, b, c])
    dst = np.concatenate([b, c, a])
    directed = src * nv + dst
    undirected = np.minimum(src, dst) * nv + np.maximum(src, dst)
    _, dcount = np.unique(directed, return_counts=True)
    ukeys, ucount = np.unique(undirected, return_counts=True)
    return dcount, ukeys, ucount


def _manifold_pairs(F, nv):
    """Face pairs across edges used by exactly two faces, and whether the two faces use the edge in the same
    direction (a real winding conflict). Edges used by 4 faces (two sheets pinched together) are excluded: each
    direction appears twice there even when every face is wound consistently."""
    nt = len(F)
    a, b, c = F[:, 0], F[:, 1], F[:, 2]
    src = np.concatenate([a, b, c])
    dst = np.concatenate([b, c, a])
    fid = np.tile(np.arange(nt), 3)
    _, uinv, ucount = np.unique(np.minimum(src, dst) * nv + np.maximum(src, dst), return_inverse=True,
                                return_counts=True)
    order = np.argsort(uinv, kind="stable")
    idx = order[ucount[uinv[order]] == 2].reshape(-1, 2)
    return fid[idx[:, 0]], fid[idx[:, 1]], src[idx[:, 0]] == src[idx[:, 1]], src[idx[:, 0]], dst[idx[:, 0]]


def _surface_points(V, F, tri_piece, spacing, cap=1_500_000):
    """Vertices plus barycentric-grid samples on every triangle longer than `spacing`, each with its piece index.
    Low-poly pieces (kit bricks, posts) touch face to face while their corners sit far apart, so gaps are measured
    between surface points no more than `spacing` apart, not between vertices. Spacing grows if the samples would
    exceed `cap` points."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    longest = np.sqrt(np.maximum(np.maximum(((b - a) ** 2).sum(1), ((c - b) ** 2).sum(1)), ((a - c) ** 2).sum(1)))
    vert_piece = np.full(len(V), -1, np.int64)
    vert_piece[F.reshape(-1)] = np.repeat(tri_piece, 3)
    keep = vert_piece >= 0
    pts, labs = [V[keep]], [vert_piece[keep]]
    n = np.ceil(longest / spacing).astype(np.int64)
    extra = float((((n + 1) * (n + 2)) // 2)[n > 1].sum())
    if extra > cap:
        n = np.ceil(longest / (spacing * math.sqrt(extra / cap))).astype(np.int64)
    for m in np.unique(n[n > 1]):
        i, j = np.meshgrid(np.arange(m + 1), np.arange(m + 1), indexing="ij")
        ok = i + j <= m
        wi, wj = (i[ok] / m)[None, :, None], (j[ok] / m)[None, :, None]
        rows = np.where(n == m)[0]
        step = max(1, (1 << 20) // int(ok.sum()))  # <= ~1M samples (24 MB) per block
        for s in range(0, len(rows), step):
            sel = rows[s:s + step]
            pa = a[sel][:, None, :]
            pts.append((pa + wi * (b[sel] - a[sel])[:, None, :] + wj * (c[sel] - a[sel])[:, None, :]).reshape(-1, 3))
            labs.append(np.repeat(tri_piece[sel], ok.sum()))
    return np.concatenate(pts), np.concatenate(labs)


def _tree(points):
    # unbalanced trees build and search surface samples (flat, coplanar clusters) far faster than median splits
    return cKDTree(points, balanced_tree=False, compact_nodes=False)


def detached_parts(V, F, tri_label, area, min_share=0.005, gap_frac=0.015, gap_min_m=0.01):
    """Welded pieces are linked when they come within max(gap_min_m, gap_frac x bounding diagonal) of each other;
    pieces holding >= min_share of the area that are not linked (directly or through other such links) to the
    main body, the linked group holding the most area, are detached. Nested double layers and touching parts (hoops
    on a barrel) sit millimetres apart; a slab floating above a cart bed sits centimetres away. Only pieces >=
    min_share are link sources, so a chain through a smaller piece is not followed. Distances are between surface
    points at most gap/2 apart (_surface_points), so low-poly pieces that touch face to face link even when their
    corners are far apart.
    Ground: a group of linked pieces whose lowest point is within one gap of y = 0 (the ground the game stands the
    asset on) rests on the ground. When the main body rests on the ground too, other resting groups count as placed
    beside it (loose weights next to a balance), not detached; they are listed under grounded_parts.
    Each detached piece carries the exact distance from its linked group to the main body, in metres and in gaps
    (a beam lying on a roof that floats 6 cm above its posts is as far away as the roof, not farther)."""
    diag = float(np.linalg.norm(V.max(0) - V.min(0)))
    gap = max(gap_min_m, gap_frac * diag)
    labs, inv = np.unique(tri_label, return_inverse=True)
    share = np.bincount(inv, weights=area) / max(area.sum(), 1e-30)
    if len(labs) < 2:
        return {"gap_m": round(gap, 4), "parts": [], "detached_area_share": 0.0, "grounded_parts": [],
                "grounded_area_share": 0.0, "max_dist_gaps": 0.0}
    S, s_label = _surface_points(V, F, inv, gap / 2)
    by_label = np.argsort(s_label, kind="stable")
    S, s_label = S[by_label], s_label[by_label]
    start = np.searchsorted(s_label, np.arange(len(labs) + 1))
    box_lo = np.minimum.reduceat(S, start[:-1], axis=0)
    box_hi = np.maximum.reduceat(S, start[:-1], axis=0)

    def points(ids):
        return np.concatenate([S[start[j]:start[j + 1]] for j in ids])
    order = np.argsort(share)[::-1]
    sig = [int(k) for k in order if share[k] >= min_share]
    parent = list(range(len(labs)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for k in sig[1:]:
        cand = np.where(np.all(box_lo <= box_hi[k] + gap, axis=1) & np.all(box_hi >= box_lo[k] - gap, axis=1))[0]
        cand = [int(j) for j in cand if find(int(j)) != find(k)]
        if not cand:
            continue
        d, _ = _tree(points([k])).query(points(cand), distance_upper_bound=gap)
        hit = np.repeat(cand, np.diff(start)[cand])[d <= gap]
        for j in np.unique(hit):
            if find(k) != find(int(j)):
                parent[find(k)] = find(int(j))
    root = np.array([find(k) for k in range(len(labs))])
    main = int(np.bincount(root, weights=share, minlength=len(labs)).argmax())
    piece_min_y = box_lo[:, 1]
    group_min_y = np.full(len(labs), np.inf)
    np.minimum.at(group_min_y, root, piece_min_y)
    grounded_group = np.abs(group_min_y) <= gap
    main_tree = None
    group_dist = {}
    parts, grounded = [], []
    for k in sig:
        if root[k] == main:
            continue
        entry = {"area_share": round(float(share[k]), 4), "size_m": np.round(box_hi[k] - box_lo[k], 3).tolist(),
                 "centre_m": np.round((box_hi[k] + box_lo[k]) / 2, 3).tolist(),
                 "min_y_m": round(float(piece_min_y[k]), 4)}
        if grounded_group[main] and grounded_group[root[k]]:
            grounded.append(entry)
            continue
        if root[k] not in group_dist:
            if main_tree is None:
                main_tree = _tree(points(np.where(root == main)[0]))
            Qg = points(np.where(root == root[k])[0])
            bound = float(main_tree.query(Qg, eps=0.5)[0].min())  # >= the true minimum, at most 1.5x it
            group_dist[root[k]] = float(main_tree.query(Qg, distance_upper_bound=bound * (1 + 1e-9) + 1e-12)[0]
                                        .min())
        d = group_dist[root[k]]
        entry["dist_to_main_m"] = round(d, 4)
        entry["dist_gaps"] = round(d / gap, 2)
        parts.append(entry)
    return {"gap_m": round(gap, 4), "parts": parts,
            "detached_area_share": round(sum(p["area_share"] for p in parts), 4),
            "max_dist_gaps": max((p["dist_gaps"] for p in parts), default=0.0),
            "grounded_parts": grounded, "grounded_area_share": round(sum(p["area_share"] for p in grounded), 4)}


def topology(P, F):
    n_tri = len(F)
    out = {"unwelded_vertices": int(len(P)), "triangles": int(n_tri),
           "unwelded_vert_per_tri": round(len(P) / max(n_tri, 1), 4)}
    if n_tri == 0:
        out["empty"] = True
        return out, None
    _, _, ucount_raw = _edge_counts(F, len(P))
    out["unwelded_boundary_share"] = round(float((ucount_raw == 1).sum()) / max(len(ucount_raw), 1), 5)

    V, inv = weld(P)
    W = inv[F]
    e1 = V[W[:, 1]] - V[W[:, 0]]
    e2 = V[W[:, 2]] - V[W[:, 0]]
    cross = np.cross(e1, e2)
    cn = np.linalg.norm(cross, axis=1)
    e3 = V[W[:, 2]] - V[W[:, 1]]
    longest2 = np.maximum(np.maximum((e1 * e1).sum(1), (e2 * e2).sum(1)), (e3 * e3).sum(1))
    collapsed = (W[:, 0] == W[:, 1]) | (W[:, 1] == W[:, 2]) | (W[:, 0] == W[:, 2])
    degenerate = collapsed | (cn <= 1e-6 * longest2)
    good = W[~degenerate]
    out.update({"welded_vertices": int(len(V)), "welded_vert_per_tri": round(len(V) / n_tri, 4),
                "degenerate_triangles": int(degenerate.sum()),
                "degenerate_share": round(float(degenerate.mean()), 5)})
    if len(good) == 0:
        out["empty"] = True
        return out, None
    dcount, ukeys, ucount = _edge_counts(good, len(V))
    n_edges = len(ucount)
    out.update({
        "edges": int(n_edges),
        "boundary_edges": int((ucount == 1).sum()),
        "boundary_share": round(float((ucount == 1).sum()) / n_edges, 5),
        "nonmanifold_edges": int((ucount > 2).sum()),
        "nonmanifold_share": round(float((ucount > 2).sum()) / n_edges, 5),
        "duplicate_directed_edges": int((dcount > 1).sum()),
        "dup_directed_share": round(float((dcount > 1).sum()) / n_edges, 5),
    })
    gcross = cross[~degenerate]
    normals = gcross / np.linalg.norm(gcross, axis=1)[:, None]
    f0, f1, same_dir, es, ed = _manifold_pairs(good, len(V))
    consistent = ~same_dir
    elen = np.linalg.norm(V[es] - V[ed], axis=1)
    cos_d = (normals[f0] * normals[f1]).sum(1)
    clen = max(float(elen[consistent].sum()), 1e-30)
    out.update({
        "winding_conflict_edges": int(same_dir.sum()),
        "winding_conflict_share": round(float(same_dir.sum()) / n_edges, 5),
        # length-weighted share of consistently wound 2-face edges whose faces turn more than 60 / 120 deg:
        # thin two-sided sheets meet at knife edges (~180 deg), crumpled surfaces zig-zag
        "crease_share": round(float(elen[consistent & (cos_d < 0.5)].sum()) / clen, 5),
        "fold_share": round(float(elen[consistent & (cos_d < -0.5)].sum()) / clen, 5),
    })
    eu = ukeys // len(V)
    ev = ukeys % len(V)
    labels = _components(len(V), eu, ev)
    tri_label = labels[good[:, 0]]
    _, sizes = np.unique(tri_label, return_counts=True)
    area = 0.5 * cn[~degenerate]
    _, lab_inv = np.unique(tri_label, return_inverse=True)
    comp_area = np.bincount(lab_inv, weights=area)
    sizes_sorted = np.sort(sizes)[::-1]
    out.update({
        "components": int(len(sizes)),
        "largest_component_share": round(float(sizes_sorted[0]) / len(good), 5),
        "largest_component_area_share": round(float(comp_area.max() / max(comp_area.sum(), 1e-30)), 5),
        "tiny_component_tri_share": round(float(sizes[sizes <= 8].sum()) / len(good), 5),
        "detached": detached_parts(V, good, tri_label, area),
    })
    return out, (V, good, gcross, area)


# ----------------------------------------------------------------------------------------------------- orientation
UP = np.array([0.0, 1.0, 0.0])


def _angle(a, b):
    return float(np.degrees(np.arccos(np.clip(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)), -1, 1))))


def _cube_bins(n, res=24):
    a = np.abs(n)
    ax = a.argmax(1)
    rows = np.arange(len(n))
    major = a[rows, ax]
    neg = n[rows, ax] < 0
    other = np.array([[1, 2], [0, 2], [0, 1]])[ax]
    u = n[rows, other[:, 0]] / major
    v = n[rows, other[:, 1]] / major
    iu = np.clip(((u + 1) * 0.5 * res).astype(np.int64), 0, res - 1)
    iv = np.clip(((v + 1) * 0.5 * res).astype(np.int64), 0, res - 1)
    return (ax * 2 + neg) * res * res + iu * res + iv


def flat_clusters(normals, area, max_clusters=8, cone_deg=12.0, remove_deg=25.0, stop_share=0.005):
    """Area-weighted face-normal clusters: seed at the densest cube-map bin, mean-shift inside a cone, remove, repeat.
    `share` is the cluster's area inside the cone / total area; `core` is the part of that within 4 deg (flat faces
    concentrate there, curved bands spread across the cone)."""
    total = area.sum()
    remaining = np.ones(len(normals), bool)
    bins = _cube_bins(normals)
    cos_cone, cos_core, cos_remove = (np.cos(np.radians(x)) for x in (cone_deg, 4.0, remove_deg))
    clusters = []
    for _ in range(max_clusters):
        hist = np.bincount(bins[remaining], weights=area[remaining], minlength=6 * 24 * 24)
        best = int(hist.argmax())
        if hist[best] < stop_share * total:
            break
        sel = remaining & (bins == best)
        d = (normals[sel] * area[sel, None]).sum(0)
        d /= np.linalg.norm(d)
        for cone in (20.0, cone_deg, cone_deg, cone_deg, cone_deg):
            m = remaining & (normals @ d > np.cos(np.radians(cone)))
            s = (normals[m] * area[m, None]).sum(0)
            if np.linalg.norm(s) == 0:
                break
            d = s / np.linalg.norm(s)
        dots = normals @ d
        m = remaining & (dots > cos_cone)
        share = float(area[m].sum() / total)
        core = float(area[remaining & (dots > cos_core)].sum() / max(area[m].sum(), 1e-30))
        clusters.append({"dir": d, "share": share, "core": core})
        remaining &= ~(dots > cos_remove)
        if not remaining.any():
            break
    return clusters


def up_from_flats(clusters, min_share, min_core):
    """Best-fit up from flat-face clusters. Direct evidence: clusters within 50 deg of +Y / -Y (tops and bottoms).
    Derived evidence: the cross product of two roughly orthogonal side axes (a box frame), built only from clusters
    that are genuinely flat (core >= min_core; curved bands on barrels, rods and splayed legs are not), with
    antiparallel faces merged into one axis. Candidates agreeing within 10 deg are grouped; a group's support is the
    area share of the distinct clusters behind it, and the best-supported group wins."""
    flats = [(i, c) for i, c in enumerate(clusters) if c["share"] >= min_share]
    cands = []  # (direction, source cluster ids, kind)
    for i, c in flats:
        d = c["dir"]
        if _angle(d, UP) <= 50:
            cands.append((d, {i}, "top"))
        elif _angle(d, -UP) <= 50:
            cands.append((-d, {i}, "bottom"))
    axes = []
    for i, c in flats:
        if not 50 < _angle(c["dir"], UP) < 130 or c["core"] < min_core:
            continue
        for ax in axes:
            dp = float(np.dot(c["dir"], ax["dir"]))
            if abs(dp) >= np.cos(np.radians(10)):
                s = ax["dir"] * ax["w"] + np.sign(dp) * c["dir"] * c["share"]
                ax["dir"] = s / np.linalg.norm(s)
                ax["w"] += c["share"]
                ax["ids"].add(i)
                break
        else:
            axes.append({"dir": c["dir"], "w": c["share"], "ids": {i}})
    for i in range(len(axes)):
        for j in range(i + 1, len(axes)):
            a, b = axes[i]["dir"], axes[j]["dir"]
            if abs(np.dot(a, b)) < np.sin(np.radians(15)):
                c = np.cross(a, b)
                c /= np.linalg.norm(c)
                if c[1] < 0:
                    c = -c
                if _angle(c, UP) <= 50:
                    cands.append((c, axes[i]["ids"] | axes[j]["ids"], "side-frame"))
    if not cands:
        return None
    share = {i: c["share"] for i, c in flats}
    groups = []
    for d, ids, kind in cands:
        for g in groups:
            if _angle(d, g["dir"]) <= 10:
                g["members"].append((d, ids, kind))
                s = sum(m[0] * sum(share[k] for k in m[1]) for m in g["members"])
                g["dir"] = s / np.linalg.norm(s)
                g["ids"] |= ids
                break
        else:
            groups.append({"dir": d, "ids": set(ids), "members": [(d, ids, kind)]})
    for g in groups:
        g["support"] = sum(share[k] for k in g["ids"])
    g = max(groups, key=lambda x: x["support"])
    return {"up": g["dir"], "support_share": round(g["support"], 4),
            "sources": sorted({m[2] for m in g["members"]}), "candidates": len(cands), "groups": len(groups)}


def _describe_up(d):
    d = d / np.linalg.norm(d)
    return {"tilt_deg": round(_angle(d, UP), 2),
            "pitch_toward_pos_z_deg": round(float(np.degrees(np.arctan2(d[2], d[1]))), 2),
            "roll_toward_pos_x_deg": round(float(np.degrees(np.arctan2(d[0], d[1]))), 2),
            "up_vector": np.round(d, 4).tolist()}


def surface_moments(V, F):
    """Area-weighted mean and covariance of the surface (exact second moment of uniform triangles)."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    area = 0.5 * np.linalg.norm(np.cross(b - a, c - a), axis=1)
    A = area.sum()
    s = a + b + c
    mean = (area[:, None] * s).sum(0) / (3 * A)
    second = (np.einsum("n,ni,nj->ij", area, a, a) + np.einsum("n,ni,nj->ij", area, b, b)
              + np.einsum("n,ni,nj->ij", area, c, c) + np.einsum("n,ni,nj->ij", area, s, s)) / (12 * A)
    return mean, second - np.outer(mean, mean)


def _hull2d(pts):
    """Monotone-chain convex hull of a small point set (counter-clockwise)."""
    pts = np.unique(pts, axis=0)
    if len(pts) < 3:
        return pts
    pts = pts[np.lexsort((pts[:, 1], pts[:, 0]))]

    def half(points):
        h = []
        for p in points:
            while len(h) >= 2 and ((h[-1][0] - h[-2][0]) * (p[1] - h[-2][1])
                                   - (h[-1][1] - h[-2][1]) * (p[0] - h[-2][0])) <= 0:
                h.pop()
            h.append(p)
        return h
    lower, upper = half(pts), half(pts[::-1])
    return np.array(lower[:-1] + upper[:-1])


def min_area_rect(xy):
    """Minimum-area enclosing rectangle of 2-D points via the hull of per-direction extreme points (0.25 deg sweep)
    and rotating calipers over the hull edges. Angle is the long side's direction from +x, in [0, 180)."""
    th = np.radians(np.arange(0, 360, 0.25))
    dirs = np.stack([np.cos(th), np.sin(th)], 1)
    # 64 directions per block: points x 1440 directions at once reached ~0.5 GB per worker
    ext = np.unique(np.concatenate([np.argmax(xy @ dirs[s:s + 64].T, axis=0) for s in range(0, len(dirs), 64)]))
    hull = _hull2d(xy[ext])
    if len(hull) < 3:
        return None
    edges = np.roll(hull, -1, axis=0) - hull
    ang = np.arctan2(edges[:, 1], edges[:, 0]) % (np.pi / 2)
    ang = np.unique(np.round(ang, 9))
    best = None
    for t in ang:
        u = np.array([np.cos(t), np.sin(t)])
        v = np.array([-u[1], u[0]])
        pu, pv = hull @ u, hull @ v
        w, h = pu.max() - pu.min(), pv.max() - pv.min()
        if best is None or w * h < best[0]:
            best = (w * h, t, w, h)
    area, t, w, h = best
    long_angle = math.degrees(t) if w >= h else math.degrees(t) + 90.0
    x, y = hull[:, 0], hull[:, 1]
    hull_area = 0.5 * abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1)))
    return {"long_m": float(max(w, h)), "short_m": float(min(w, h)), "angle_deg": long_angle % 180.0,
            "fill": float(hull_area / area) if area > 0 else 0.0}


def base_plane_tilt(P, band=0.25, grid=8):
    """tilt_check.py: least-squares plane through each footprint cell's lowest point in the bottom 25% of height."""
    lo, hi = P.min(0), P.max(0)
    size = np.maximum(hi - lo, 1e-9)
    base = P[P[:, 1] < lo[1] + band * size[1]]
    if len(base) < 4:
        return None
    i = np.minimum(grid - 1, ((base[:, 0] - lo[0]) / size[0] * grid).astype(np.int64))
    k = np.minimum(grid - 1, ((base[:, 2] - lo[2]) / size[2] * grid).astype(np.int64))
    cell = i * grid + k
    order = np.lexsort((base[:, 1], cell))
    first = order[np.r_[True, cell[order][1:] != cell[order][:-1]]]
    if len(first) < 4:
        return None
    ci, ck = cell[first] // grid, cell[first] % grid
    C = np.c_[lo[0] + (ci + 0.5) * size[0] / grid, lo[2] + (ck + 0.5) * size[2] / grid, base[first, 1]]
    A = np.c_[C[:, 0], C[:, 1], np.ones(len(C))]
    (a, c, _), *_ = np.linalg.lstsq(A, C[:, 2], rcond=None)
    return round(float(np.degrees(np.arctan(np.hypot(a, c)))), 2)


def _frame_extents(V, R):
    proj = V @ R.T
    return proj.max(0) - proj.min(0)


def orientation(V, F, cross, area):
    """Orientation signals on welded geometry (V vertices, F non-degenerate triangles)."""
    normals = cross / np.linalg.norm(cross, axis=1)[:, None]
    clusters = flat_clusters(normals, area)
    up = up_from_flats(clusters, T("flat_min_share"), T("flat_min_core"))
    out = {"flat_clusters": [{"dir": np.round(c["dir"], 4).tolist(), "share": round(c["share"], 4),
                              "core": round(c["core"], 3), "deg_from_up": round(_angle(c["dir"], UP), 1)}
                             for c in clusters]}
    if up:
        out["up"] = _describe_up(up["up"]) | {k: v for k, v in up.items() if k != "up"}
    else:
        out["up"] = None

    mean, cov = surface_moments(V, F)
    evals, evecs = np.linalg.eigh(cov)
    evals, evecs = evals[::-1], evecs[:, ::-1]
    axes = []
    for k in range(3):
        e = evecs[:, k]
        nearest = int(np.argmax(np.abs(e)))
        axes.append({"vector": np.round(e, 4).tolist(), "sigma_m": round(float(math.sqrt(max(evals[k], 0))), 4),
                     "nearest_world_axis": "XYZ"[nearest],
                     "deg_off_nearest_world_axis": round(_angle(np.abs(e), np.eye(3)[nearest]), 2),
                     "deg_from_vertical": round(min(_angle(e, UP), _angle(-e, UP)), 2)})
    elong = math.sqrt(evals[0] / max(evals[1], 1e-30))
    out["principal_axes"] = axes
    out["elongation"] = round(elong, 3)
    out["flatness"] = round(math.sqrt(evals[1] / max(evals[2], 1e-30)), 3)

    rect = min_area_rect(V[:, [0, 2]])
    if rect:
        a = rect["angle_deg"] % 90.0
        rect["deg_off_axis"] = round(min(a, 90.0 - a), 2)
        rect["aspect"] = round(rect["long_m"] / max(rect["short_m"], 1e-9), 3)
        rect = {k: (round(v, 4) if isinstance(v, float) else v) for k, v in rect.items()}
    out["footprint"] = rect
    out["base_plane_tilt_deg"] = base_plane_tilt(V)

    # tightest oriented box among: world axes, principal axes, and the levelled frame (measured up + footprint
    # rectangle in the plane normal to it)
    frames = {"world": np.eye(3), "principal": evecs.T.copy()}
    if up:
        u = up["up"] / np.linalg.norm(up["up"])
        t = np.cross(u, [0.0, 0.0, 1.0])
        if np.linalg.norm(t) < 1e-6:
            t = np.cross(u, [1.0, 0.0, 0.0])
        t /= np.linalg.norm(t)
        s = np.cross(t, u)
        r2 = min_area_rect(np.c_[V @ t, V @ s])
        if r2:
            th = math.radians(r2["angle_deg"])
            a1 = math.cos(th) * t + math.sin(th) * s
            frames["levelled"] = np.stack([a1, u, np.cross(a1, u)])
    best = None
    for name, R in frames.items():
        ext = _frame_extents(V, R)
        vol = float(np.prod(np.maximum(ext, 1e-9)))
        if best is None or vol < best[0]:
            best = (vol, name, ext)
    out["oriented_box"] = {"frame": best[1], "dims_m": np.round(np.sort(best[2])[::-1], 4).tolist()}
    return out


# ------------------------------------------------------------------------------------------------------------ skin
def _ray_dirs(n=26):
    """Fibonacci-sphere directions."""
    k = np.arange(n) + 0.5
    phi = np.arccos(1 - 2 * k / n)
    theta = np.pi * (1 + 5 ** 0.5) * k
    return np.stack([np.cos(theta) * np.sin(phi), np.cos(phi), np.sin(theta) * np.sin(phi)], 1)


def escaping_rays(points, V, F, dirs=None):
    """Per point, how many of the rays cast from it leave without crossing any triangle (Moller-Trumbore). A point
    inside a hollow torso is enclosed from every direction even though the nearest vertex is far away; a joint
    floating beside or above the body has open sky in some direction."""
    dirs = _ray_dirs() if dirs is None else dirs
    v0 = V[F[:, 0]]
    e1 = V[F[:, 1]] - v0
    e2 = V[F[:, 2]] - v0
    out = np.zeros(len(points), np.int64)
    for i, o in enumerate(points):
        s = o - v0
        for d in dirs:
            p = np.cross(d, e2)
            det = (e1 * p).sum(1)
            ok = np.abs(det) > 1e-15
            inv = np.where(ok, 1.0 / np.where(ok, det, 1.0), 0.0)
            u = (s * p).sum(1) * inv
            q = np.cross(s, e1)
            v = (q @ d) * inv
            t = (e2 * q).sum(1) * inv
            if not (ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-9)).any():
                out[i] += 1
    return out


def skin_signals(doc, P, skin, world, F=None):
    if not skin["J"]:
        return None
    sk = doc["skins"][skin["skin"]]
    joints = sk["joints"]
    names = [doc["nodes"][j].get("name", str(j)) for j in joints]
    K = len(joints)
    idx = np.concatenate([np.arange(o, o + n) for o, n in skin["offsets"]])
    J = np.concatenate(skin["J"])
    W = np.concatenate(skin["W"])
    weight_sum = np.zeros(K)
    for s in range(J.shape[1]):
        np.add.at(weight_sum, np.clip(J[:, s], 0, K - 1), W[:, s])
    dominant = J[np.arange(len(J)), W.argmax(1)]
    dom = np.bincount(dominant[W.max(1) > 0], minlength=K)
    deform = weight_sum > 1e-6
    jpos = np.array([world[j][:3, 3] for j in joints])
    Pv = P[idx]
    lo, hi = Pv.min(0), Pv.max(0)
    height = float(hi[1] - lo[1])
    dist = np.empty(K)
    for k0 in range(0, K, 16):
        d2 = ((Pv[None, :, :] - jpos[k0:k0 + 16, None, :]) ** 2).sum(-1)
        dist[k0:k0 + 16] = np.sqrt(d2.min(1))
    gap = dist / max(height, 1e-9)
    above = (jpos[:, 1] - hi[1]) / max(height, 1e-9)
    rows = []
    for k in range(K):
        rows.append({"name": names[k], "deform": bool(deform[k]), "pos": np.round(jpos[k], 4).tolist(),
                     "nearest_vertex_m": round(float(dist[k]), 4), "gap_frac": round(float(gap[k]), 4),
                     "above_top_frac": round(float(above[k]), 4), "dominated_vertices": int(dom[k]),
                     "weight_sum": round(float(weight_sum[k]), 3)})
    # Checked set: every skin joint except skeleton roots (parent outside the skin). glTF skin.joints is the deform
    # set the exporter bound; a root sits at the origin by design. Joints that ended up with no weight at all are
    # kept: a head bone that drives nothing is the defect, not an exemption.
    jset = set(joints)
    parent = {c: i for i, n in enumerate(doc["nodes"]) for c in n.get("children", [])}
    for k, j in enumerate(joints):
        rows[k]["root"] = parent.get(j) not in jset
    if F is not None and len(F):
        far = [k for k in range(K) if not rows[k]["root"] and gap[k] > T("joint_gap_frac")]
        esc = escaping_rays(jpos[far], P, F) if far else []
        for k, e in zip(far, esc):
            rows[k]["escaping_rays"] = int(e)
    c_rows = [r for r in rows if not r["root"]]
    for r in c_rows:
        # outside = far from every vertex AND not enclosed (rays not measured -> joint was within the gap threshold)
        r["outside"] = r.get("escaping_rays", 0) > 0
    worst_gap = max(c_rows, key=lambda r: r["gap_frac"]) if c_rows else None
    worst_above = max(c_rows, key=lambda r: r["above_top_frac"]) if c_rows else None
    return {
        "joints": K, "root_joints": [r["name"] for r in rows if r["root"]],
        "weighted_joints": int(deform.sum()), "skinned_vertices": int(len(idx)),
        "unweighted_vertices": int((W.sum(1) <= 0).sum()),
        "mesh_height_m": round(height, 4),
        "joint_top_over_mesh_top": round(float((max(r["pos"][1] for r in c_rows) - lo[1]) / max(height, 1e-9)), 4)
        if c_rows else None,
        "worst_gap": {"joint": worst_gap["name"], "gap_frac": worst_gap["gap_frac"],
                      "nearest_vertex_m": worst_gap["nearest_vertex_m"],
                      "escaping_rays": worst_gap.get("escaping_rays")} if worst_gap else None,
        "outside_joints": [r["name"] for r in c_rows if r["outside"]],
        "enclosed_far_joints": [r["name"] for r in c_rows if "escaping_rays" in r and not r["outside"]],
        "worst_above": {"joint": worst_above["name"], "above_top_frac": worst_above["above_top_frac"]}
        if worst_above else None,
        "zero_dominance_joints": [r["name"] for r in c_rows if r["dominated_vertices"] == 0],
        "weightless_joints": [r["name"] for r in c_rows if not r["deform"]],
        "joint_table": rows,
    }


# ------------------------------------------------------------------------------------------------------- analysis
def analyse_glb(path, kind="lod0", want_orientation=None):
    """Measure one GLB. kind: 'lod0' (drawn model), 'lod' (_lodN) or 'rigged'."""
    t0 = time.time()
    doc, P, F, prims, skin, world = load_geometry(path)
    out = {"file": os.path.abspath(path), "kind": kind, "bytes": os.path.getsize(path)}
    out["materials"] = material_facts(doc, prims)
    if len(P):
        lo, hi = P.min(0), P.max(0)
        out["bounds"] = {"min": np.round(lo, 5).tolist(), "max": np.round(hi, 5).tolist(),
                         "size": np.round(hi - lo, 5).tolist()}
    else:
        out["bounds"] = None
    topo, welded = topology(P, F)
    out["topology"] = topo
    if want_orientation is None:
        want_orientation = kind in ("lod0", "rigged")
    if want_orientation and welded is not None:
        V, G, cross, area = welded
        out["orientation"] = orientation(V, G, cross, area)
    if kind == "rigged":
        out["skin"] = skin_signals(doc, P, skin, world, F)
    out["seconds"] = round(time.time() - t0, 3)
    return out


def _flag(flags, code, file_key, value, threshold, message, severity="review"):
    flags.append({"code": code, "file": file_key, "severity": severity, "value": value, "threshold": threshold,
                  "message": message})


def _topology_flags(flags, key, topo):
    if not topo or topo.get("empty"):
        _flag(flags, "topology_empty", key, 0, None, "no drawable triangles", "fail")
        return
    det = topo.get("detached") or {}
    values = dict(topo, detached_area_share=det.get("detached_area_share"))
    for code, metric, thr, above in (("open_surface", "boundary_share", "boundary_share", True),
                                     ("non_manifold", "nonmanifold_share", "nonmanifold_share", True),
                                     ("winding_conflict", "winding_conflict_share", "winding_conflict_share", True),
                                     ("crumpled_surface", "fold_share", "fold_share", True),
                                     ("detached_parts", "detached_area_share", "detached_area_share", True),
                                     ("fragmented", "largest_component_area_share", "largest_component_area_share",
                                      False),
                                     ("shattered", "tiny_component_tri_share", "tiny_component_tri_share", True),
                                     ("degenerate_tris", "degenerate_share", "degenerate_share", True)):
        v = values.get(metric)
        if v is None:
            continue
        if (v > T(thr)) if above else (v < T(thr)):
            _flag(flags, code, key, v, T(thr), f"{metric} {v} {'>' if above else '<'} {T(thr)}")
    far = [p for p in det.get("parts", []) if p.get("dist_gaps", 0) >= T("floating_piece_gaps")]
    if far:
        w = max(far, key=lambda p: p["dist_gaps"])
        _flag(flags, "floating_piece", key, w["dist_gaps"], T("floating_piece_gaps"),
              f"{len(far)} piece(s) >= 0.5% of the area float >= {T('floating_piece_gaps')} gaps "
              f"({det['gap_m']} m each) from the main body: " +
              "; ".join(f"{p['area_share']:.2%} at {p['centre_m']}, {p['dist_to_main_m']} m away" for p in far))


# Orientation flags per file kind. Every LOD0 is a rigid mesh, rigged assets' LOD0 included; the skinned rigged GLB
# gets only the footprint yaw and major-axis checks (its flat faces and lowest points are posed limbs).
ORIENT_CHECKS = {"lod0": ("tilted", "diagonal", "yawed", "base_plane_tilt"), "rigged": ("diagonal", "yawed"),
                 "lod": ()}


def _placement_flags(flags, key, res, target, checks):
    b = res.get("bounds")
    if not b:
        return
    height = b["size"][1]
    tol = max(T("ground_tol_m"), T("ground_tol_frac") * height)
    miny = b["min"][1]
    if miny > tol:
        _flag(flags, "floating", key, round(miny, 4), round(tol, 4), f"min Y {miny:.4f} m above 0")
    elif miny < -tol:
        _flag(flags, "sunk", key, round(miny, 4), round(-tol, 4), f"min Y {miny:.4f} m below 0")
    o = res.get("orientation")
    if not o:
        return
    if target:
        longest = o["oriented_box"]["dims_m"][0]
        ratio = round(longest / target, 4)
        res.setdefault("placement", {})["scale_ratio"] = ratio
        res["placement"]["longest_extent_m"] = longest
        res["placement"]["target_size_m"] = target
        if ratio > T("scale_ratio_hi"):
            _flag(flags, "oversize", key, ratio, T("scale_ratio_hi"),
                  f"longest oriented extent {longest} m = {ratio}x target {target} m")
        elif ratio < T("scale_ratio_lo"):
            _flag(flags, "undersize", key, ratio, T("scale_ratio_lo"),
                  f"longest oriented extent {longest} m = {ratio}x target {target} m")
    up = o.get("up")
    if "tilted" in checks and up and up["tilt_deg"] > T("tilt_deg"):
        _flag(flags, "tilted", key, up["tilt_deg"], T("tilt_deg"),
              f"flat-face up is {up['tilt_deg']} deg off +Y (pitch toward +Z {up['pitch_toward_pos_z_deg']}, "
              f"roll toward +X {up['roll_toward_pos_x_deg']})")
    ax = o["principal_axes"][0]
    if "diagonal" in checks and o["elongation"] >= T("diagonal_min_elongation") \
            and ax["deg_off_nearest_world_axis"] > T("diagonal_axis_deg"):
        _flag(flags, "diagonal", key, ax["deg_off_nearest_world_axis"], T("diagonal_axis_deg"),
              f"major axis {ax['deg_off_nearest_world_axis']} deg off {ax['nearest_world_axis']} "
              f"(elongation {o['elongation']})")
    fp = o.get("footprint")
    if "yawed" in checks and fp and (fp["fill"] >= T("yaw_min_fill") or fp["aspect"] >= T("yaw_min_aspect")) \
            and fp["deg_off_axis"] > T("yaw_deg"):
        _flag(flags, "yawed", key, fp["deg_off_axis"], T("yaw_deg"),
              f"footprint rectangle {fp['deg_off_axis']} deg off X/Z (fill {fp['fill']}, aspect {fp['aspect']})")
    bp = o.get("base_plane_tilt_deg")
    if "base_plane_tilt" in checks and bp is not None and bp > T("base_plane_tilt_deg") and not up \
            and o["elongation"] < T("diagonal_min_elongation"):
        _flag(flags, "base_plane_tilt", key, bp, T("base_plane_tilt_deg"),
              f"lowest-points plane tilts {bp} deg (no flat-face up available)")


def _material_flags(flags, key, mat, lod0_mat=None, is_lod=False):
    sev = "fail"
    if mat["materials"] == 0:
        _flag(flags, "no_materials", key, 0, 1, "file has zero materials", sev)
    if is_lod and lod0_mat and lod0_mat["textures"] > 0 and mat["textures"] == 0:
        _flag(flags, "no_textures", key, 0, lod0_mat["textures"], "zero textures while LOD0 is textured", sev)
    if mat["primitives_without_material"]:
        _flag(flags, "unbound_primitive", key, mat["primitives_without_material"], 0,
              f"{mat['primitives_without_material']} primitive(s) without a material", sev)
    if mat["primitives_without_texcoord0"]:
        _flag(flags, "no_texcoord0", key, mat["primitives_without_texcoord0"], 0,
              f"{mat['primitives_without_texcoord0']} primitive(s) without TEXCOORD_0", sev)
    if mat["material_index_out_of_range"]:
        _flag(flags, "material_index_range", key, mat["material_index_out_of_range"], 0,
              "material index out of range", sev)
    if mat["texture_refs_out_of_range"]:
        _flag(flags, "texture_index_range", key, mat["texture_refs_out_of_range"], 0,
              "texture/image index out of range", sev)


def _lod_flags(flags, key, res, prev_tris, requested, lod0):
    tris = res["topology"]["triangles"]
    if requested:
        factor = round(tris / requested, 3)
        res["lod_budget"] = {"requested": requested, "triangles": tris, "factor": factor}
        if factor > T("lod_budget_factor"):
            _flag(flags, "lod_over_budget", key, factor, T("lod_budget_factor"),
                  f"{tris} tris = {factor}x requested {requested}", "fail")
    else:
        res["lod_budget"] = {"requested": None, "triangles": tris, "factor": None}
    if prev_tris is not None and tris >= prev_tris:
        _flag(flags, "lod_not_decreasing", key, tris, prev_tris, f"{tris} tris is not below the previous level's "
              f"{prev_tris}", "fail")
    if lod0 and lod0.get("bounds") and res.get("bounds"):
        b0, b = lod0["bounds"], res["bounds"]
        scale = max(b0["size"])
        drift = max(max(abs(x - y) for x, y in zip(b["min"], b0["min"])),
                    max(abs(x - y) for x, y in zip(b["max"], b0["max"]))) / max(scale, 1e-9)
        res["lod_bounds_drift"] = round(drift, 4)
        if drift > T("lod_bounds_drift"):
            _flag(flags, "lod_bounds_drift", key, round(drift, 4), T("lod_bounds_drift"),
                  f"AABB moved {drift:.1%} of LOD0's longest side", "fail")


def _skin_flags(flags, key, sk):
    if not sk:
        _flag(flags, "no_skin", key, 0, None, "rigged file has no skinned primitive", "fail")
        return
    out_rows = [r for r in sk["joint_table"] if r.get("outside")]
    if out_rows:
        w = max(out_rows, key=lambda r: r["gap_frac"])
        _flag(flags, "joint_outside_mesh", key, w["gap_frac"], T("joint_gap_frac"),
              f"{len(out_rows)} joint(s) farther than {T('joint_gap_frac')} of the height from every vertex and "
              f"not enclosed by the mesh, worst {w['name']} {w['nearest_vertex_m']} m ({w['gap_frac']} of height, "
              f"{w['escaping_rays']}/26 rays escape): {', '.join(r['name'] for r in out_rows)}")
    wa = sk["worst_above"]
    if wa and wa["above_top_frac"] > T("joint_above_frac"):
        bad = [r["name"] for r in sk["joint_table"] if not r["root"] and r["above_top_frac"] > T("joint_above_frac")]
        _flag(flags, "joint_above_mesh", key, wa["above_top_frac"], T("joint_above_frac"),
              f"{len(bad)} joint(s) above the mesh top, worst {wa['joint']} {wa['above_top_frac']} of the height: "
              f"{', '.join(bad)}")
    z = sk["zero_dominance_joints"]
    if len(z) > T("zero_dominance_max"):
        _flag(flags, "joints_dominate_nothing", key, len(z), T("zero_dominance_max"),
              f"{len(z)} joints are the strongest influence on no vertex: {', '.join(z)}")
    if sk["unweighted_vertices"]:
        _flag(flags, "unweighted_vertices", key, sk["unweighted_vertices"], 0,
              f"{sk['unweighted_vertices']} skinned vertices have no weight")


def _read_meta(asset_id, assets):
    p = os.path.join(assets, "ready", asset_id, f"{asset_id}_meta.json")
    if not os.path.exists(p):
        return None
    with open(p, encoding="utf-8") as h:
        return json.load(h)


def assess_asset(asset_id, assets=ASSETS):
    """All files of one asset id: ready LOD0, its _lodN files, and rigged/<id>/<id>_rigged.glb if present."""
    meta = _read_meta(asset_id, assets)
    target = (meta or {}).get("target_size_m")
    rigged_meta = bool((meta or {}).get("rigged"))
    ready_dir = os.path.join(assets, "ready", asset_id)
    rig_path = os.path.join(assets, "rigged", asset_id, f"{asset_id}_rigged.glb")
    result = {"asset_id": asset_id, "qa_version": QA_VERSION, "category": (meta or {}).get("category"),
              "meta_rigged": rigged_meta, "target_size_m": target, "files": {}, "flags": [], "notes": []}
    flags = result["flags"]
    if meta is None:
        result["notes"].append("no ready meta.json")
    lod0 = None
    lod0_path = os.path.join(ready_dir, f"{asset_id}.glb")
    if os.path.exists(lod0_path):
        try:
            lod0 = analyse_glb(lod0_path, "lod0")
            result["files"]["lod0"] = lod0
            _material_flags(flags, "lod0", lod0["materials"])
            _topology_flags(flags, "lod0", lod0["topology"])
            _placement_flags(flags, "lod0", lod0, target, ORIENT_CHECKS["lod0"])
        except Exception as exc:  # measured failure is a finding, not a crash
            result["files"]["lod0"] = {"file": lod0_path, "error": repr(exc)}
            _flag(flags, "unreadable", "lod0", None, None, repr(exc), "fail")
    elif os.path.isdir(ready_dir):
        _flag(flags, "lod0_missing", "lod0", None, None, "ready/<id>/<id>.glb missing", "fail")

    requested = {}
    for k, v in ((meta or {}).get("lods") or {}).items():
        if isinstance(v, dict):
            requested[k] = v.get("requested_faces")
    prev = lod0["topology"]["triangles"] if lod0 and "topology" in lod0 else None
    for suffix in LOD_SUFFIXES:
        p = os.path.join(ready_dir, f"{asset_id}{suffix}.glb")
        key = suffix.lstrip("_")
        if not os.path.exists(p):
            continue
        try:
            res = analyse_glb(p, "lod")
        except Exception as exc:
            result["files"][key] = {"file": p, "error": repr(exc)}
            _flag(flags, "unreadable", key, None, None, repr(exc), "fail")
            continue
        result["files"][key] = res
        _material_flags(flags, key, res["materials"], lod0["materials"] if lod0 else None, is_lod=True)
        _topology_flags(flags, key, res["topology"])
        _lod_flags(flags, key, res, prev, requested.get(f"{asset_id}{suffix}"), lod0)
        if not requested.get(f"{asset_id}{suffix}"):
            result["notes"].append(f"{key}: meta has no requested_faces; budget check skipped")
        prev = res["topology"]["triangles"]

    if os.path.exists(rig_path):
        try:
            res = analyse_glb(rig_path, "rigged")
            result["files"]["rigged"] = res
            _material_flags(flags, "rigged", res["materials"])
            _topology_flags(flags, "rigged", res["topology"])
            _placement_flags(flags, "rigged", res, target, ORIENT_CHECKS["rigged"])
            _skin_flags(flags, "rigged", res.get("skin"))
        except Exception as exc:
            result["files"]["rigged"] = {"file": rig_path, "error": repr(exc)}
            _flag(flags, "unreadable", "rigged", None, None, repr(exc), "fail")
    elif rigged_meta:
        result["notes"].append("meta says rigged but rigged/<id>/<id>_rigged.glb is missing")

    result["flag_codes"] = sorted({f"{f['file']}:{f['code']}" for f in flags})
    result["flag_count"] = len(flags)
    return result


def library_ids(assets=ASSETS):
    ids = set()
    for sub in ("ready", "rigged"):
        root = os.path.join(assets, sub)
        if os.path.isdir(root):
            ids.update(d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d)))
    return sorted(ids)


def _json_default(o):
    if isinstance(o, np.generic):
        return o.item()
    if isinstance(o, np.ndarray):
        return o.tolist()
    raise TypeError(type(o))


def _run_one(args):
    asset_id, assets, out_dir = args
    try:
        res = assess_asset(asset_id, assets)
    except Exception as exc:
        res = {"asset_id": asset_id, "error": repr(exc), "flags": [
            {"code": "qa_crash", "file": "-", "severity": "fail", "value": None, "threshold": None,
             "message": repr(exc)}], "flag_codes": ["-:qa_crash"], "flag_count": 1}
    with open(os.path.join(out_dir, f"{asset_id}.json"), "w", encoding="utf-8") as h:
        json.dump(res, h, indent=1, default=_json_default)
        h.write("\n")
    return asset_id, res.get("flag_count", 0), res.get("error")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--ids", nargs="*")
    ap.add_argument("--glb")
    ap.add_argument("--kind", default="lod0", choices=("lod0", "lod", "rigged"))
    ap.add_argument("--target", type=float, default=None)
    ap.add_argument("--assets", default=ASSETS)
    ap.add_argument("--out", default=None, help="staging dir (default <assets>/_staging/qa)")
    ap.add_argument("--workers", type=int, default=max(1, min(8, (os.cpu_count() or 2) // 2)))
    ap.add_argument("--no-report", action="store_true")
    a = ap.parse_args(argv)

    if a.glb:
        res = analyse_glb(a.glb, a.kind)
        flags = []
        _material_flags(flags, a.kind, res["materials"])
        _topology_flags(flags, a.kind, res["topology"])
        _placement_flags(flags, a.kind, res, a.target, ORIENT_CHECKS[a.kind])
        if a.kind == "rigged":
            _skin_flags(flags, a.kind, res.get("skin"))
        res["flags"] = flags
        json.dump(res, sys.stdout, indent=1, default=_json_default)
        print()
        return 0

    ids = library_ids(a.assets) if a.all else (a.ids or [])
    if not ids:
        ap.error("give --all, --ids or --glb")
    out = a.out or os.path.join(a.assets, "_staging", "qa")
    per_asset = os.path.join(out, "assets")
    os.makedirs(per_asset, exist_ok=True)
    t0 = time.time()
    jobs = [(i, a.assets, per_asset) for i in ids]
    done = 0
    if a.workers > 1 and len(jobs) > 1:
        with ProcessPoolExecutor(max_workers=a.workers) as pool:
            futures = [pool.submit(_run_one, j) for j in jobs]
            for fut in as_completed(futures):
                asset_id, n, err = fut.result()
                done += 1
                if err or done % 50 == 0 or done == len(jobs):
                    print(f"  [{done}/{len(jobs)}] {asset_id} flags={n}{' ERROR ' + err if err else ''}"
                          f"  {time.time() - t0:.0f}s", flush=True)
    else:
        for j in jobs:
            asset_id, n, err = _run_one(j)
            done += 1
            print(f"  [{done}/{len(jobs)}] {asset_id} flags={n}{' ERROR ' + err if err else ''}", flush=True)
    print(f"  wrote {len(jobs)} asset JSON files to {per_asset} in {time.time() - t0:.0f}s")
    if not a.no_report:
        import _asset_qa_report
        return _asset_qa_report.main(["--out", out])
    return 0


if __name__ == "__main__":
    sys.exit(main())
