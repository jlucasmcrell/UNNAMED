"""Rebuild an asset's _lod1/_lod2/_lod3 from its existing LOD0: textured, crack-free, in budget.

LOD0 (<id>.glb) is only read, never written. Run headless:
  "<blender>" --background --factory-startup --python _rebuild_lods.py -- \
      --input G:/UNNAMED/assets/ready/<id>/<id>.glb --outdir G:/UNNAMED/assets/_staging/lods/<id> --render

Budgets come from the meta's lods.<id>_lodN.requested_faces (default 8000/2500/600); textures are
capped per level by --tex-sizes (default 1024/512/256). Each level is built by every route in the
chain (--route, default C/B/R/T), each candidate is exported, measured, rendered next to LOD0 and
gated, and the passing candidate with the lowest render score is kept (--select best).

Routes:
  A  Blender COLLAPSE on the unwelded import, UVs and material slots kept, LOD0's images
     downscaled. The import is split into UV-chart islands that decimate apart (cracks, shards);
     kept for comparison only.
  C  Seam-aware quadric simplifier (below) that keeps LOD0's UVs, normals and materials per
     corner; LOD0's images downscaled. Exact texture, but chart corners cannot move, so it stops
     early on fragmented atlases.
  B  The same simplifier with no seams, Smart UV Project + repack, then base colour, ORM and a
     tangent normal map baked from LOD0 (Cycles, selected-to-active through a cage).
     --b-decimator blender instead welds by position and uses Blender's COLLAPSE (stalls).
  R  Voxel remesh of LOD0 (surface pushed out half a voxel so thin sheets survive), specks and
     cavities dropped, Blender COLLAPSE to budget, then baked like B. Always watertight.
  T  R with twice the push-out; wins on foliage made of thin sheets.
  I  Billboard impostor (12 triangles, cut-out alpha): tried for the last level of foliage (meta
     category flora) when no other route passes (--impostor auto; always/off also exist) and kept
     only if it passes the gates itself.

Why an in-script simplifier: welded by position, the Pixal3D LOD0s are heavily non-manifold
(barrel: 5,540 of 14,320 vertices, 1,303 edges shared by four faces) and Blender's COLLAPSE stops
far above budget on them (barrel 34,074 of 39,247 faces welded by distance). Vertices are merged
only across properly paired edges, and collapses follow meshoptimizer's vertex kinds (manifold,
border, seam, locked) with link-condition and flip checks. Only numpy is available.

Before B and C, enclosed inner layers may be dropped (many LOD0s are thin-walled hollows whose
inner skin faces a sealed cavity). A face is dropped only when it lies against an outer surface,
every one of 64 hemisphere rays is stopped by a face turned toward the viewer, 160 orthographic
face-ID views at 1024 px plus perspective views from inside the bounding box never see it, and it
sits in a cluster of at least 16 such faces. The drop is then abandoned (every face kept) when it
opens more edge than the crack gate allows or the pruned LOD0 renders differently from LOD0.
When LOD0's NORMAL data is far from its own surface (--normals auto), baked LODs take LOD0's
normals and normal texels instead of re-baking them, so they shade like LOD0.

Gates per candidate: triangles within 1.1x budget and below the level above; material, UVs and
embedded textures; open-edge length <= 1.25 x LOD0's full open-edge length + 0.5 x diagonal; no new
shards (welded components of <= 8 triangles that LOD0 does not have); bounds; and, from 8 EEVEE
views at 512 px with back faces culled (front, back, left, right, top, low 3/4 and two high 3/4),
silhouette match, colour error, see-through holes inside LOD0's silhouette and the largest solid
patch of wrong colour (an inner layer showing through, a bad bake).

Writes <id>_lod1..3.glb, <id>_lod_report.json and <id>_lod_holes.jpg (the 8 gate views per level,
LOD0 on top, holes the gate counted in red); --render adds <id>_lod_compare.jpg (LOD0..LOD3 side by
side, three views, one camera). --render-only measures and renders an existing LOD set.
"""
import argparse
import hashlib
import json
import math
import os
import struct
import sys
import time

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

DEFAULT_BUDGETS = (8000, 2500, 600)
MANIFOLD, BORDER, SEAM, LOCKED = 0, 1, 2, 3
# LOD0 normals are averaged over this fraction of the LOD's median edge length (baked routes)
NORMAL_RADIUS = 0.25
# a shard is a welded component of at most this many triangles
SHARD_TRIS = 8
# an 8x8 block of the gate render whose mean colour error exceeds this counts as a blotch
BLOTCH_ERROR = 0.1


# ----------------------------------------------------------------------------- arguments

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--input", required=True, help="LOD0 GLB (read only)")
    p.add_argument("--outdir", required=True)
    p.add_argument("--id", default=None, help="asset id (default: input file stem)")
    p.add_argument("--meta", default=None, help="meta.json with lods.requested_faces (default: next to input)")
    p.add_argument("--budgets", default=None, help="override, e.g. 8000,2500,600")
    p.add_argument("--route", default="C/B/R/T",
                   help="A|B|C|R|T, a chain such as C/B/R/T (candidates per level, see --select), "
                        "or one of those per level, e.g. C/B,C/B/R,B/R/T")
    p.add_argument("--tex-sizes", default="1024,512,256", help="max texture edge per level")
    p.add_argument("--image-format", default="AUTO", choices=["AUTO", "JPEG", "WEBP"])
    p.add_argument("--image-quality", type=int, default=90)
    p.add_argument("--impostor", default="auto", choices=["auto", "always", "off"],
                   help="route I for the last level: auto = foliage (meta category flora) when no other "
                        "route passes; always = every asset; off = never")
    p.add_argument("--select", default="best", choices=["best", "first"],
                   help="per level: build every route of the chain and keep the best render score "
                        "among those passing the gates (best), or stop at the first that passes (first)")
    p.add_argument("--b-decimator", default="qem", choices=["qem", "blender"])
    p.add_argument("--r-thicken", type=float, default=1.0, help="route R sheet thickening, in voxels")
    p.add_argument("--r-voxel-scale", type=float, default=1.0, help="route R voxel size multiplier")
    p.add_argument("--keep-hidden", action="store_true", help="do not drop enclosed inner layers")
    p.add_argument("--hidden-rays", type=int, default=64, help="hemisphere rays per face in the enclosure test")
    p.add_argument("--hidden-wall", type=float, default=0.01,
                   help="an inner layer lies within this fraction of the diagonal of an outer surface")
    p.add_argument("--hidden-views", type=int, default=160,
                   help="orthographic face-ID views over the whole sphere that must never see a dropped face")
    p.add_argument("--hidden-px", type=int, default=1024, help="face-ID view resolution")
    p.add_argument("--hidden-air-points", type=int, default=16,
                   help="camera points inside the bounding box (6 perspective views each)")
    p.add_argument("--hidden-min-cluster", type=int, default=16,
                   help="clusters of fewer droppable faces are kept")
    p.add_argument("--prune-change-max", type=float, default=0.05,
                   help="dropping hidden faces is abandoned when the pruned LOD0 changes more than this "
                        "%% of LOD0's rendered pixels in any probe view")
    p.add_argument("--normal-radius", type=float, default=NORMAL_RADIUS,
                   help="LOD0 normals are averaged over this fraction of the LOD's median edge length")
    p.add_argument("--normals", default="auto", choices=["auto", "lod0", "geometry"],
                   help="baked routes: geometry = LOD's own normals + baked tangent map; lod0 = LOD0's "
                        "normals transferred + LOD0's normal texels copied; auto = lod0 when more than "
                        "10%% of LOD0's area has shading normals over 60 degrees from its surface")
    p.add_argument("--lod0-normal-map", default="copy", choices=["bake", "copy"],
                   help="with LOD0 normals: bake = LOD0's final shading normal baked into the LOD's own "
                        "tangent space; copy = LOD0's tangent-space texels copied as they are (wrong where "
                        "a UV island is oriented differently from LOD0's)")
    p.add_argument("--no-probe", action="store_true", help="skip the in-session comparison renders")
    p.add_argument("--probe-size", type=int, default=512, help="gate render edge in pixels (8 views)")
    p.add_argument("--gate-silhouette-match", default="0.98,0.96,0.90",
                   help="minimum silhouette match vs LOD0 in every view: share of pixels where the two "
                        "silhouettes agree, ignoring 2 px along either outline (not IoU); one value or one per level")
    p.add_argument("--gate-colour", default="0.02,0.03,0.05",
                   help="maximum pooled render colour error (mean over views); one value or one per level")
    p.add_argument("--gate-blotch-patch", default="2,3,5",
                   help="maximum size of the largest 4-connected patch of 8x8 render blocks whose colour "
                        "error exceeds %s, %%%% of LOD0's covered blocks, in any view; one value or one per level"
                        % BLOTCH_ERROR)
    p.add_argument("--gate-holes", default="0.02,0.02,0.02",
                   help="maximum see-through hole pixels inside LOD0's silhouette, %% of LOD0's pixels, "
                        "in any view; one value or one per level")
    p.add_argument("--bake-samples", type=int, default=8)
    p.add_argument("--device", default="GPU", choices=["GPU", "CPU"])
    p.add_argument("--render", action="store_true", help="write <id>_lod_compare.jpg")
    p.add_argument("--render-only", action="store_true",
                   help="do not rebuild; measure/render <id>_lod1..3.glb found in --lod-dir")
    p.add_argument("--lod-dir", default=None, help="folder holding the LOD set for --render-only")
    p.add_argument("--panel", type=int, default=768, help="compare image panel edge in pixels")
    p.add_argument("--compare-name", default=None, help="compare image file name override")
    p.add_argument("--title", default="", help="text added to every render label")
    return p.parse_args(argv)


def per_level(text, n, cast=str):
    vals = [cast(v.strip()) for v in str(text).split(",") if v.strip()]
    if len(vals) == 1:
        vals = vals * n
    if len(vals) != n:
        raise SystemExit(f"expected 1 or {n} values, got {text!r}")
    return vals


def read_budgets(meta_path, asset_id):
    """requested_faces per level from meta 'lods'; defaults when missing or empty."""
    if not meta_path or not os.path.exists(meta_path):
        return list(DEFAULT_BUDGETS), "default (no meta)"
    with open(meta_path, encoding="utf-8") as handle:
        meta = json.load(handle)
    lods = meta.get("lods") or {}
    out = []
    for i, default in enumerate(DEFAULT_BUDGETS, start=1):
        entry = lods.get(f"{asset_id}_lod{i}") if isinstance(lods, dict) else None
        req = entry.get("requested_faces") if isinstance(entry, dict) else None
        out.append(int(req) if req else default)
    return out, "meta" if lods else "default (meta lods empty)"


# ----------------------------------------------------------------------------- scene / import

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    bpy.ops.import_scene.gltf(filepath=path, merge_vertices=False, disable_bone_shape=True)
    return [o for o in bpy.context.scene.objects if o.type == "MESH"]


def joined_mesh(meshes, name):
    """One object in world space, holding every mesh of the file."""
    objs = []
    for o in meshes:
        me = o.data.copy()
        me.transform(o.matrix_world)
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        objs.append(ob)
    for o in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
    if len(objs) > 1:
        for o in bpy.context.scene.objects:
            o.select_set(False)
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.object.join()
    src = objs[0]
    src.name = name
    bm = bmesh.new()
    bm.from_mesh(src.data)
    if any(len(f.verts) != 3 for f in bm.faces):
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) != 3])
        bm.to_mesh(src.data)
    bm.free()
    return src


def mesh_arrays(obj):
    me = obj.data
    nv, nl, nf = len(me.vertices), len(me.loops), len(me.polygons)
    P = np.empty(nv * 3, np.float32)
    me.vertices.foreach_get("co", P)
    CV = np.empty(nl, np.int64)
    me.loops.foreach_get("vertex_index", CV)
    ls = np.empty(nf, np.int64)
    me.polygons.foreach_get("loop_start", ls)
    if not np.array_equal(ls, np.arange(nf) * 3):
        order = (ls[:, None] + np.arange(3)[None, :]).reshape(-1)
    else:
        order = np.arange(nl)
    UV = np.zeros(nl * 2, np.float32)
    if me.uv_layers:
        me.uv_layers.active.data.foreach_get("uv", UV)
    N = np.empty(nl * 3, np.float32)
    me.corner_normals.foreach_get("vector", N)
    FM = np.empty(nf, np.int64)
    me.polygons.foreach_get("material_index", FM)
    return {
        "P": P.reshape(-1, 3).astype(np.float64),
        "T": CV[order].reshape(-1, 3),
        "UV": UV.reshape(-1, 2)[order],
        "N": N.reshape(-1, 3)[order],
        "FM": FM,
    }


# ----------------------------------------------------------------------------- topology

def pair_fins(P, T, he, ph, g, cnt, fwd):
    """Pair the faces around an edge shared by 4, 6 or 8 faces (two solids touching along a line).

    Faces are sorted by angle around the edge; a pair bounds one solid when both normals point
    out of the angular sector between them. Groups that do not alternate are left open.
    Returns rows (half-edge a, half-edge b) to stitch.
    """
    out = []
    multi = np.flatnonzero(np.isin(cnt[g], (4, 6, 8)))
    if len(multi) == 0:
        return np.zeros((0, 2), np.int64)
    multi = multi[np.argsort(g[multi], kind="stable")]
    bounds = np.flatnonzero(np.r_[True, g[multi][1:] != g[multi][:-1], True])
    for s, e in zip(bounds[:-1], bounds[1:]):
        hs = multi[s:e]
        if fwd[hs].sum() * 2 != len(hs):
            continue
        h0 = hs[0]
        a, b = (he[h0, 0], he[h0, 1]) if ph[h0, 0] < ph[h0, 1] else (he[h0, 1], he[h0, 0])
        d = P[b] - P[a]
        dl = np.linalg.norm(d)
        if dl <= 0:
            continue
        d /= dl
        f = hs // 3
        k = hs % 3
        opp = T[f, (k + 2) % 3]
        w = P[opp] - P[a]
        w -= (w @ d)[:, None] * d
        ref = w[0] / max(np.linalg.norm(w[0]), 1e-30)
        ref2 = np.cross(d, ref)
        theta = np.arctan2(w @ ref2, w @ ref)
        tri = P[T[f]]
        n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
        sgn = np.sign((n * np.cross(d, w)).sum(1))
        o = np.argsort(theta)
        hs, sgn = hs[o], sgn[o]
        if (sgn == 0).any() or (sgn[1:] == sgn[:-1]).any():
            continue
        m = len(hs)
        idx = [(i, i + 1) for i in range(0, m, 2)] if sgn[0] < 0 else \
            [(i, (i + 1) % m) for i in range(1, m, 2)]
        if any(fwd[hs[i]] == fwd[hs[j]] for i, j in idx):
            continue
        out.extend((hs[i], hs[j]) for i, j in idx)
    return np.array(out, np.int64).reshape(-1, 2)


def stitch_ids(P, T, tol=1e-6):
    """Merge vertices only across edges shared by exactly two oppositely wound faces.

    Welding every coincident vertex (remove_doubles) turns LOD0 into a non-manifold mesh; stitching
    only proper edge pairs rebuilds the manifold surface and leaves fins/pinches open. Returns a
    compact stitched id per source vertex and the stitched positions.
    """
    nv = len(P)
    q = np.round(P / tol).astype(np.int64)
    _, pid = np.unique(q, axis=0, return_inverse=True)
    pid = pid.reshape(-1)
    he = np.stack([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]], 1).reshape(-1, 2)
    ph = pid[he]
    key = np.sort(ph, 1)
    fwd = ph[:, 0] < ph[:, 1]
    _, g, cnt = np.unique(key, axis=0, return_inverse=True, return_counts=True)
    g = g.reshape(-1)
    idx2 = np.flatnonzero(cnt[g] == 2)
    o2 = idx2[np.argsort(g[idx2], kind="stable")]
    A, B = o2[0::2], o2[1::2]
    ok = fwd[A] != fwd[B]
    A, B = A[ok], B[ok]
    fins = pair_fins(P, T, he, ph, g, cnt, fwd)
    if len(fins):
        A = np.concatenate([A, fins[:, 0]])
        B = np.concatenate([B, fins[:, 1]])
    parent = np.arange(nv)
    pairs = np.concatenate([np.stack([he[A, 0], he[B, 1]], 1),
                            np.stack([he[A, 1], he[B, 0]], 1)])
    # union-find by repeated min-label propagation with pointer jumping (vectorised)
    a, b = pairs[:, 0], pairs[:, 1]
    for _ in range(1000):
        la, lb = parent[a], parent[b]
        m = np.minimum(la, lb)
        changed = (la != m) | (lb != m)
        if not changed.any():
            break
        np.minimum.at(parent, la, m)
        np.minimum.at(parent, lb, m)
        for _ in range(64):
            nxt = parent[parent]
            if np.array_equal(nxt, parent):
                break
            parent = nxt
    roots, sid = np.unique(parent, return_inverse=True)
    PS = np.zeros((len(roots), 3))
    PS[sid] = P
    stats = {"source_vertices": nv, "stitched_vertices": int(len(roots)),
             "edge_groups": {str(k): int(v) for k, v in zip(*np.unique(cnt, return_counts=True))},
             "stitched_pairs": int(ok.sum()), "same_direction_pairs": int((~ok).sum()),
             "fin_pairs": int(len(fins))}
    return sid.reshape(-1), PS, stats


def build_wedges(sid, arrays, keep_attributes=True):
    """A wedge is one corner identity: stitched vertex plus material, UV and normal.

    With keep_attributes=False (bake route) the wedge is the stitched vertex alone, so the
    simplifier sees no seams at all.
    """
    T, FM = arrays["T"], arrays["FM"]
    cs = sid[T].reshape(-1)
    if keep_attributes:
        fm = np.repeat(FM, 3)
        uvb = arrays["UV"].view(np.int32).astype(np.int64)
        nq = np.round(arrays["N"] * 1e4).astype(np.int64)
        K = np.column_stack([cs, fm, uvb, nq])
    else:
        K = cs[:, None]
    _, first, inv = np.unique(K, axis=0, return_index=True, return_inverse=True)
    inv = inv.reshape(-1)
    return {
        "TW": inv.reshape(-1, 3),
        "wr": cs[first],
        "wuv": arrays["UV"][first],
        "wn": arrays["N"][first],
    }


def locked_by_fan(TS, ns):
    """Stitched vertices whose faces do not form one fan (pinches, non-manifold edges)."""
    me = bpy.data.meshes.new("_fan_probe")
    used = np.unique(TS)
    remap = np.full(ns, -1)
    remap[used] = np.arange(len(used))
    me.from_pydata(np.zeros((len(used), 3)).tolist(), [], remap[TS].tolist())
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    bad = np.zeros(ns, bool)
    flags = np.array([not v.is_manifold for v in bm.verts], bool)
    bad[used] = flags[: len(used)]
    bm.free()
    bpy.data.meshes.remove(me)
    return bad


# ----------------------------------------------------------------------------- quadric simplifier

def plane_quadrics(n, d, w):
    """10-coefficient quadrics w * (n,d)(n,d)^T for rows of unit normals n and offsets d."""
    a, b, c = n[:, 0], n[:, 1], n[:, 2]
    return w[:, None] * np.stack([a * a, a * b, a * c, a * d, b * b, b * c, b * d, c * c, c * d, d * d], 1)


def quadric_eval(Q, p):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    a2, ab, ac, ad, b2, bc, bd, c2, cd, d2 = Q.T
    return (a2 * x * x + 2 * ab * x * y + 2 * ac * x * z + 2 * ad * x + b2 * y * y
            + 2 * bc * y * z + 2 * bd * y + c2 * z * z + 2 * cd * z + d2)


class Topo:
    """Edge/vertex classification of the current triangle set (all vectorised)."""

    def __init__(self, TW, wr, ns, locked):
        TS = wr[TW]
        self.TS = TS
        nt = len(TS)
        ha = TS.reshape(-1)
        hb = TS[:, [1, 2, 0]].reshape(-1)
        wa = TW.reshape(-1)
        wb = TW[:, [1, 2, 0]].reshape(-1)
        hf = np.repeat(np.arange(nt), 3)
        key = np.minimum(ha, hb).astype(np.int64) * ns + np.maximum(ha, hb)
        order = np.argsort(key, kind="stable")
        ks = key[order]
        ek, estart, ecount = np.unique(ks, return_index=True, return_counts=True)
        h1 = order[estart]
        h2 = order[np.minimum(estart + 1, len(order) - 1)]
        border = ecount == 1
        opp = (ecount == 2) & (ha[h1] == hb[h2])
        nm = ~(border | opp)
        seam = opp & ((wa[h1] != wb[h2]) | (wb[h1] != wa[h2]))
        eu, ev = ek // ns, ek % ns

        def vcount(mask):
            return np.bincount(np.concatenate([eu[mask], ev[mask]]), minlength=ns)

        nb, nnm, nse = vcount(border), vcount(nm), vcount(seam)
        nedge = vcount(np.ones(len(ek), bool))
        nface = np.bincount(ha, minlength=ns)
        uw = np.unique(wa)
        nw = np.bincount(wr[uw], minlength=ns)
        kind = np.full(ns, LOCKED, np.int8)
        ok = (nface > 0) & (nnm == 0) & ~locked
        closed = (nb == 0) & (nedge == nface)
        opened = (nb == 2) & (nedge == nface + 1)
        kind[ok & closed & (nw == 1) & (nse == 0)] = MANIFOLD
        kind[ok & opened & (nw == 1) & (nse == 0)] = BORDER
        kind[ok & closed & (nw == 2) & (nse == 2)] = SEAM
        # neighbour CSR
        src = np.concatenate([eu, ev])
        dst = np.concatenate([ev, eu])
        o = np.argsort(src, kind="stable")
        self.nbr = dst[o]
        self.nptr = np.zeros(ns + 1, np.int64)
        self.nptr[1:] = np.cumsum(np.bincount(src, minlength=ns))
        # vertex -> faces CSR
        o = np.argsort(ha, kind="stable")
        self.vfaces = hf[o]
        self.fptr = np.zeros(ns + 1, np.int64)
        self.fptr[1:] = np.cumsum(nface)
        self.ek, self.ecount, self.eu, self.ev = ek, ecount, eu, ev
        self.border, self.nm, self.seam = border, nm, seam
        self.f1, self.f2 = hf[h1], hf[h2]
        self.kind = kind
        self.ns = ns


def expand(ptr, rows_vertex):
    """For each row r with vertex v, yield (row index repeated, flat index into CSR data)."""
    deg = ptr[rows_vertex + 1] - ptr[rows_vertex]
    rep = np.repeat(np.arange(len(rows_vertex)), deg)
    start = np.repeat(ptr[rows_vertex], deg)
    offs = np.arange(len(rep)) - np.repeat(np.cumsum(deg) - deg, deg)
    return rep, start + offs


def wedge_at(TS, TW, f, x):
    col = np.argmax(TS[f] == x[:, None], axis=1)
    return TW[f, col]


def simplify(TW, FM, wr, PS, targets, border_weight=10.0, seam_weight=1.0, flip_cos=0.05,
             max_frac=0.12):
    """Seam-aware half-edge-collapse QEM, batched into passes of independent collapses.

    Vertex kinds follow meshoptimizer: manifold vertices collapse anywhere, border vertices only
    along their border, seam vertices only along their seam; complex vertices never move. Every
    collapse keeps the target vertex's position and corner attributes, so UVs stay valid.
    Returns {target: (TW, FM)} snapshots and statistics.
    """
    ns = len(PS)
    TW = TW.copy()
    FM = FM.copy()
    locked = locked_by_fan(wr[TW], ns)
    topo = Topo(TW, wr, ns, locked)
    # initial quadrics: area-weighted face planes plus border and seam constraint planes
    TS = topo.TS
    p0, p1, p2 = PS[TS[:, 0]], PS[TS[:, 1]], PS[TS[:, 2]]
    fn = np.cross(p1 - p0, p2 - p0)
    area2 = np.linalg.norm(fn, axis=1)
    nn = fn / np.maximum(area2, 1e-30)[:, None]
    Q = np.zeros((ns, 10))
    fq = plane_quadrics(nn, -(nn * p0).sum(1), area2 * 0.5)
    for k in range(3):
        np.add.at(Q, TS[:, k], fq)
    for mask, weight in ((topo.border, border_weight), (topo.seam, seam_weight)):
        idx = np.flatnonzero(mask)
        if len(idx) == 0:
            continue
        pa, pb = PS[topo.eu[idx]], PS[topo.ev[idx]]
        e = pb - pa
        m = np.cross(e, nn[topo.f1[idx]])
        ml = np.linalg.norm(m, axis=1)
        good = ml > 1e-30
        m = m[good] / ml[good][:, None]
        eq = plane_quadrics(m, -(m * pa[good]).sum(1), weight * (e[good] ** 2).sum(1))
        np.add.at(Q, topo.eu[idx][good], eq)
        np.add.at(Q, topo.ev[idx][good], eq)
    mean_area = float(np.mean(area2 * 0.5)) if len(area2) else 0.0
    stats = {"locked_vertices": int(locked.sum()),
             "initial_kinds": {k: int((topo.kind == v).sum()) for k, v in
                               (("manifold", MANIFOLD), ("border", BORDER), ("seam", SEAM), ("locked", LOCKED))},
             "passes": 0, "stalled_at": None}
    snaps = {}
    targets = sorted(targets, reverse=True)
    ti = 0
    first = True
    while ti < len(targets):
        tgt = targets[ti]
        if len(TW) <= tgt:
            snaps[tgt] = (TW.copy(), FM.copy())
            ti += 1
            continue
        if not first:
            topo = Topo(TW, wr, ns, locked)
        first = False
        TS = topo.TS
        valid_e = np.flatnonzero(~topo.nm)
        cu = np.concatenate([topo.eu[valid_e], topo.ev[valid_e]])
        cv = np.concatenate([topo.ev[valid_e], topo.eu[valid_e]])
        ce = np.concatenate([valid_e, valid_e])
        ku, kv = topo.kind[cu], topo.kind[cv]
        eb, es = topo.border[ce], topo.seam[ce]
        ok = (ku == MANIFOLD) & ~eb & ~es
        ok |= (ku == BORDER) & eb & ((kv == BORDER) | (kv == LOCKED))
        ok |= (ku == SEAM) & es & ((kv == SEAM) | (kv == LOCKED))
        cu, cv, ce = cu[ok], cv[ok], ce[ok]
        if len(cu) == 0:
            stats["stalled_at"] = int(len(TW))
            break
        two = topo.ecount[ce] == 2
        f1 = topo.f1[ce]
        f2 = np.where(two, topo.f2[ce], f1)
        wu1, wv1 = wedge_at(TS, TW, f1, cu), wedge_at(TS, TW, f1, cv)
        wu2, wv2 = wedge_at(TS, TW, f2, cu), wedge_at(TS, TW, f2, cv)
        kuc = topo.kind[cu]
        good = np.ones(len(cu), bool)
        good &= ~((kuc == MANIFOLD) & (wv1 != wv2))
        good &= ~((kuc == SEAM) & ((wu1 == wu2) | (wv1 == wv2)))
        # link condition: shared neighbours must be exactly the faces' opposite vertices
        rep, flat = expand(topo.nptr, cu)
        w = topo.nbr[flat]
        vv = cv[rep]
        k2 = np.minimum(vv, w).astype(np.int64) * ns + np.maximum(vv, w)
        pos = np.minimum(np.searchsorted(topo.ek, k2), len(topo.ek) - 1)
        hit = (topo.ek[pos] == k2) & (w != vv)
        common = np.bincount(rep[hit], minlength=len(cu))
        good &= common == topo.ecount[ce]
        # flip check on the faces that survive around u
        rep, flat = expand(topo.fptr, cu)
        f = topo.vfaces[flat]
        tri = TS[f]
        survive = ~(tri == cv[rep][:, None]).any(1)
        pts = PS[tri]
        n_old = np.cross(pts[:, 1] - pts[:, 0], pts[:, 2] - pts[:, 0])
        at_u = tri == cu[rep][:, None]
        pts_new = np.where(at_u[:, :, None], PS[cv[rep]][:, None, :], pts)
        n_new = np.cross(pts_new[:, 1] - pts_new[:, 0], pts_new[:, 2] - pts_new[:, 0])
        lo = np.linalg.norm(n_old, axis=1)
        ln = np.linalg.norm(n_new, axis=1)
        dot = (n_old * n_new).sum(1)
        degenerate_old = lo <= 1e-12 * max(mean_area, 1e-30)
        bad = survive & ~degenerate_old & (dot <= flip_cos * lo * ln)
        flip_ok = np.bincount(rep[bad], minlength=len(cu)) == 0
        stats["last_pass"] = {
            "tris": int(len(TW)),
            "kinds": {k: int((topo.kind[np.unique(TS)] == v).sum()) for k, v in
                      (("manifold", MANIFOLD), ("border", BORDER), ("seam", SEAM), ("locked", LOCKED))},
            "candidates": int(len(cu)), "after_wedge_and_link": int(good.sum()),
            "after_flip": int((good & flip_ok).sum())}
        good &= flip_ok
        idx = np.flatnonzero(good)
        if len(idx) == 0:
            stats["stalled_at"] = int(len(TW))
            break
        e = PS[cu[idx]] - PS[cv[idx]]
        cost = quadric_eval(Q[cu[idx]] + Q[cv[idx]], PS[cv[idx]]) + 1e-3 * mean_area * (e * e).sum(1)
        order = idx[np.argsort(cost, kind="stable")]
        needed = len(TW) - tgt
        cap = max(16, int(max_frac * len(np.unique(TS))))
        touched = np.zeros(ns, bool)
        nbr, nptr = topo.nbr, topo.nptr
        cu_l, cv_l, rem_l = cu.tolist(), cv.tolist(), topo.ecount[ce].tolist()
        acc = []
        removed = 0
        for i in order.tolist():
            u, v = cu_l[i], cv_l[i]
            if touched[u] or touched[v]:
                continue
            acc.append(i)
            touched[u] = touched[v] = True
            touched[nbr[nptr[u]:nptr[u + 1]]] = True
            removed += rem_l[i]
            if removed >= needed or len(acc) >= cap:
                break
        acc = np.array(acc, np.int64)
        wmap = np.arange(int(TW.max()) + 1)
        wmap[wu1[acc]] = wv1[acc]
        wmap[wu2[acc]] = wv2[acc]
        np.add.at(Q, cv[acc], Q[cu[acc]])
        TW = wmap[TW]
        S = wr[TW]
        keep = (S[:, 0] != S[:, 1]) & (S[:, 1] != S[:, 2]) & (S[:, 0] != S[:, 2])
        TW, FM = TW[keep], FM[keep]
        # a collapse may close a tetrahedron-like pocket into two coincident faces; drop both
        S = np.sort(wr[TW], 1)
        _, inv, cnt = np.unique(S, axis=0, return_inverse=True, return_counts=True)
        dup = cnt[inv.reshape(-1)] > 1
        if dup.any():
            TW, FM = TW[~dup], FM[~dup]
        stats["passes"] += 1
    for t in targets:
        if t not in snaps:
            snaps[t] = (TW.copy(), FM.copy())
    return snaps, stats


# ----------------------------------------------------------------------------- mesh / materials

def build_object(name, TW, FM, wedges, PS, materials, with_uv=True, custom_normals=True):
    wr = wedges["wr"]
    S = wr[TW]
    used = np.unique(S)
    remap = np.full(len(PS), -1)
    remap[used] = np.arange(len(used))
    me = bpy.data.meshes.new(f"{name}_mesh")
    me.from_pydata(PS[used].tolist(), [], remap[S].tolist())
    if len(me.polygons) != len(TW):
        raise RuntimeError(f"{name}: mesh build dropped faces ({len(me.polygons)} of {len(TW)})")
    if with_uv:
        uvl = me.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", wedges["wuv"][TW].reshape(-1).astype(np.float32))
    for m in materials:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", FM.astype(np.int32))
    me.shade_smooth()
    if custom_normals:
        me.normals_split_custom_set(wedges["wn"][TW].reshape(-1, 3).tolist())
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def downscale_image(img, size, name, role):
    """Box-filtered copy no larger than size (sRGB-aware for colour, renormalised for normals)."""
    w, h = img.size
    if w == 0 or h == 0:
        return img
    f = 1
    while max(w, h) // f > size:
        f *= 2
    if f == 1:
        return img
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4).astype(np.float64)
    nw, nh = w // f, h // f
    px = px[: nh * f, : nw * f]
    colour = img.colorspace_settings.name.lower().startswith("srgb")
    if colour:
        px[..., :3] = srgb_to_linear(px[..., :3])
    out = px.reshape(nh, f, nw, f, 4).mean(axis=(1, 3))
    if colour:
        out[..., :3] = linear_to_srgb(out[..., :3])
    if role == "normal":
        v = out[..., :3] * 2 - 1
        v /= np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-8)
        out[..., :3] = v * 0.5 + 0.5
    new = bpy.data.images.new(name, nw, nh, alpha=img.depth in (32, 128))
    new.colorspace_settings.name = img.colorspace_settings.name
    new.pixels.foreach_set(out.astype(np.float32).reshape(-1))
    new.alpha_mode = img.alpha_mode
    new.update()
    return new


def image_roles(mat):
    roles = {}
    if not mat or not mat.use_nodes:
        return roles
    for link in mat.node_tree.links:
        if link.from_node.type != "TEX_IMAGE" or not link.from_node.image:
            continue
        to = link.to_node
        if to.type == "NORMAL_MAP":
            roles[link.from_node.image.name] = "normal"
        elif to.type == "BSDF_PRINCIPLED" and link.to_socket.name == "Base Color":
            roles.setdefault(link.from_node.image.name, "basecolor")
        else:
            roles.setdefault(link.from_node.image.name, "orm")
    return roles


def downscaled_materials(materials, size, lod_name):
    out, cache = [], {}
    for k, mat in enumerate(materials):
        mc = mat.copy()
        mc.name = f"MAT_{lod_name}" if k == 0 else f"MAT_{lod_name}_{k}"
        roles = image_roles(mat)
        for node in mc.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image:
                src = node.image
                if src.name not in cache:
                    role = roles.get(src.name, "orm")
                    cache[src.name] = downscale_image(src, size, f"{lod_name}_{role}_{len(cache)}", role)
                node.image = cache[src.name]
        out.append(mc)
    return out


# ----------------------------------------------------------------------------- remesh route

def simple_arrays(obj):
    me = obj.data
    P = np.empty(len(me.vertices) * 3, np.float64)
    me.vertices.foreach_get("co", P)
    T = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", T)
    return P.reshape(-1, 3), T.reshape(-1, 3)


def vertex_labels(T, nv):
    lab = np.arange(nv)
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]]])
    a, b = e[:, 0], e[:, 1]
    for _ in range(10000):
        la, lb = lab[a], lab[b]
        m = np.minimum(la, lb)
        if np.array_equal(la, m) and np.array_equal(lb, m):
            break
        np.minimum.at(lab, la, m)
        np.minimum.at(lab, lb, m)
        lab = lab[lab]
    return lab


def replace_mesh(obj, P, T):
    me = bpy.data.meshes.new(obj.data.name)
    me.from_pydata(P.tolist(), [], T.tolist())
    me.shade_smooth()
    old = obj.data
    obj.data = me
    bpy.data.meshes.remove(old)


def remesh_lod(obj, target, area, diag, thicken=1.0, voxel_scale=1.0, prune_share=0.001):
    """Voxel remesh (watertight, fuses touching parts), prune specks, then decimate to target.

    Sheets thinner than a voxel vanish in a voxel remesh (leaf cards, cloth), so every vertex is
    first pushed out along its smooth normal by thicken x voxel / 2. A thin closed sheet then gains
    thicken x voxel of thickness; a solid grows by half a voxel.
    """
    voxel = min(max(math.sqrt(area / (target * 12.0)), diag / 400.0), diag / 80.0) * voxel_scale
    bpy.context.view_layer.objects.active = obj
    if thicken > 0:
        me = obj.data
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        nrm = np.empty(len(me.vertices) * 3, np.float32)
        me.vertex_normals.foreach_get("vector", nrm)
        co = co.reshape(-1, 3) + nrm.reshape(-1, 3) * (voxel * thicken / 2)
        me.vertices.foreach_set("co", co.reshape(-1))
        me.update()
    mod = obj.modifiers.new("Remesh", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = voxel
    mod.adaptivity = 0.0
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    P, T = simple_arrays(obj)
    info = {"voxel_m": round(voxel, 5), "thicken_m": round(voxel * thicken, 5), "remeshed_tris": int(len(T))}
    lab = vertex_labels(T, len(P))
    fa = np.linalg.norm(np.cross(P[T[:, 1]] - P[T[:, 0]], P[T[:, 2]] - P[T[:, 0]]), axis=1) / 2
    comp = lab[T[:, 0]]
    ids, inv = np.unique(comp, return_inverse=True)
    carea = np.bincount(inv, weights=fa)
    # signed volume per component: closed cavities face inward and come out negative
    sv = np.einsum("ij,ij->i", P[T[:, 0]], np.cross(P[T[:, 1]], P[T[:, 2]])) / 6
    cvol = np.bincount(inv, weights=sv)
    keep = (carea[inv] >= prune_share * fa.sum()) & (cvol[inv] > 0)
    if not keep.any():
        keep = inv == int(np.argmax(carea))
    info["cavities_removed"] = int((cvol < 0).sum())
    info["components"] = int(len(ids))
    info["pruned_components"] = int((carea < prune_share * fa.sum()).sum())
    if not keep.all():
        used = np.unique(T[keep])
        remap = np.full(len(P), -1)
        remap[used] = np.arange(len(used))
        replace_mesh(obj, P[used], remap[T[keep]])
    cur = len(obj.data.polygons)
    if target < cur:
        mod = obj.modifiers.new("LODDecimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = max(target / cur, 0.0001)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    info["collapse_tris"] = len(obj.data.polygons)
    if len(obj.data.polygons) > 1.05 * target:
        # Blender's COLLAPSE fell short; finish with the in-script simplifier
        P, T = simple_arrays(obj)
        snaps, st = simplify(T, np.zeros(len(T), np.int64), np.arange(len(P)), P, [target])
        TW, _ = snaps[target]
        used = np.unique(TW)
        remap = np.full(len(P), -1)
        remap[used] = np.arange(len(used))
        replace_mesh(obj, P[used], remap[TW])
        info["qem_finish"] = {"stalled_at": st.get("stalled_at"), "passes": st["passes"]}
    obj.data.shade_smooth()
    return info


# ----------------------------------------------------------------------------- bake route

def setup_cycles(device):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    used = "CPU"
    if device == "GPU":
        try:
            prefs = bpy.context.preferences.addons["cycles"].preferences
            for backend in ("OPTIX", "CUDA", "HIP", "ONEAPI"):
                try:
                    prefs.compute_device_type = backend
                except TypeError:
                    continue
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == backend]
                if gpus:
                    for d in prefs.devices:
                        d.use = d.type == backend
                    scene.cycles.device = "GPU"
                    used = f"GPU:{backend}:{gpus[0].name}"
                    break
        except Exception as exc:  # noqa: BLE001 - fall back to CPU and record why
            used = f"CPU (GPU setup failed: {exc})"
    return used


def gltf_output_group():
    from io_scene_gltf2.blender.com.material_helpers import create_settings_group, get_gltf_node_name
    name = get_gltf_node_name()
    return bpy.data.node_groups.get(name) or create_settings_group(name)


def baked_material(lod_name, size):
    mat = bpy.data.materials.new(f"MAT_{lod_name}")
    mat.use_nodes = True
    mat.use_backface_culling = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    imgs = {}
    for role, cs in (("basecolor", "sRGB"), ("orm", "Non-Color"), ("normal", "Non-Color")):
        img = bpy.data.images.new(f"{lod_name}_{role}", size, size, alpha=False)
        img.colorspace_settings.name = cs
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        node.extension = "EXTEND"
        imgs[role] = (img, node)
    nt.links.new(imgs["basecolor"][1].outputs["Color"], bsdf.inputs["Base Color"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(imgs["orm"][1].outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    grp = nt.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    nt.links.new(sep.outputs["Red"], grp.inputs["Occlusion"])
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(imgs["normal"][1].outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return mat, imgs


def _source_socket(nt, node, name):
    sock = node.inputs.get(name)
    if sock is None:
        return None, None
    if sock.is_linked:
        return sock.links[0].from_socket, None
    return None, sock.default_value


def rewire_source(materials, mode):
    """Point each LOD0 material's output at an Emission of what one bake pass needs.

    mode: mask (white), basecolor, normalcopy (LOD0's tangent normal texels) or orm (occlusion,
    roughness, metallic packed as glTF expects).
    """
    undo = []
    for mat in materials:
        nt = mat.node_tree
        out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None) \
            or next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if out is None or bsdf is None:
            continue
        prev = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].is_linked else None
        em = nt.nodes.new("ShaderNodeEmission")
        added = [em]
        if mode == "mask":
            em.inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
        elif mode == "normalcopy":
            nmap = next((n for n in nt.nodes if n.type == "NORMAL_MAP"), None)
            col = nmap.inputs["Color"] if nmap else None
            if col is not None and col.is_linked:
                nt.links.new(col.links[0].from_socket, em.inputs["Color"])
            else:
                em.inputs["Color"].default_value = (0.5, 0.5, 1.0, 1.0)
        elif mode == "basecolor":
            s, dv = _source_socket(nt, bsdf, "Base Color")
            if s is not None:
                nt.links.new(s, em.inputs["Color"])
            else:
                em.inputs["Color"].default_value = dv
        else:
            comb = nt.nodes.new("ShaderNodeCombineColor")
            added.append(comb)
            grp = next((n for n in nt.nodes if n.type == "GROUP" and n.node_tree
                        and n.node_tree.name.lower() in ("gltf material output", "gltf settings")), None)
            occ = grp.inputs.get("Occlusion") if grp else None
            if occ is not None and occ.is_linked:
                nt.links.new(occ.links[0].from_socket, comb.inputs["Red"])
            else:
                comb.inputs["Red"].default_value = 1.0
            for ch, name in (("Green", "Roughness"), ("Blue", "Metallic")):
                s, dv = _source_socket(nt, bsdf, name)
                if s is not None:
                    nt.links.new(s, comb.inputs[ch])
                else:
                    comb.inputs[ch].default_value = float(dv)
            nt.links.new(comb.outputs["Color"], em.inputs["Color"])
        em.inputs["Strength"].default_value = 1.0
        nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
        undo.append((nt, out, prev, added))
    return undo


def restore_source(undo):
    for nt, out, prev, added in undo:
        for n in added:
            nt.nodes.remove(n)
        if prev is not None:
            nt.links.new(prev, out.inputs["Surface"])


def smart_uv(obj, size):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.001,
                             area_weight=0.0, correct_aspect=True, scale_to_bounds=False)
    # Smart UV's own packing leaves most of the square empty on these meshes (cart LOD1: 3,311
    # islands, 3.9% coverage); a bounding-box repack with a 4-texel margin reaches 30-67%.
    bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=True, margin_method="FRACTION",
                            margin=4.0 / size, shape_method="AABB")
    bpy.ops.object.mode_set(mode="OBJECT")
    return fill_uv_square(obj.data)


def fill_uv_square(me):
    """Stretch the packed layout along each axis to span the whole texture.

    Long thin assets pack into a strip (spear LOD1 reached only u = 0.733, a quarter of every texture
    unused). Stretching makes texels slightly non-square but only ever adds texels.
    """
    uv = np.empty(len(me.loops) * 2, np.float32)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2).astype(np.float64)
    lo, hi = uv.min(0), uv.max(0)
    margin = max(float(min(lo.min(), 1.0 - hi.max())), 0.0)
    scale = (1.0 - 2 * margin) / np.maximum(hi - lo, 1e-9)
    if (scale < 1.01).all():
        return [1.0, 1.0]
    scale = np.maximum(scale, 1.0)
    uv = margin + (uv - lo) * scale
    me.uv_layers.active.data.foreach_set("uv", uv.astype(np.float32).reshape(-1))
    me.update()
    return [round(float(v), 3) for v in scale]


def make_cage(lod, extrusion):
    """Copy of the LOD pushed out along its geometric vertex normals.

    Bake rays run from the cage to the LOD, so their direction does not depend on the custom
    (transferred) normals the LOD is shaded with.
    """
    me = lod.data.copy()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    nrm = np.empty(len(me.vertices) * 3, np.float32)
    me.vertex_normals.foreach_get("vector", nrm)
    me.vertices.foreach_set("co", co + nrm * extrusion)
    me.update()
    cage = bpy.data.objects.new(lod.name + "_cage", me)
    bpy.context.collection.objects.link(cage)
    cage.hide_render = True
    return cage


def transfer_normals(lod, P, T, N, tree):
    """Give the LOD the shading normals LOD0 has at the nearest surface point.

    Several LOD0s carry NORMAL data far from their geometry (door: median 90 degrees off); a baked
    tangent-space map cannot express that, so without this the LOD would shade differently.
    """
    me = lod.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    out = np.zeros_like(co)
    # average over the patch of LOD0 each LOD vertex stands for; a point sample of noisy normals
    # shows every large LOD3 triangle as a separate facet
    ev = np.array([e.vertices[:] for e in me.edges])
    radius = 0.0
    if len(ev):
        radius = NORMAL_RADIUS * float(np.median(np.linalg.norm(co[ev[:, 0]] - co[ev[:, 1]], axis=1)))
    tri = P[T]
    farea = np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1)
    fmean = N.reshape(-1, 3, 3).mean(1)
    for i, c in enumerate(co):
        near = tree.find_nearest_range(Vector(c), radius) if radius > 0 else []
        if len(near) >= 3:
            idx = np.array([h[2] for h in near])
            dist = np.array([h[3] for h in near])
            w = farea[idx] * np.maximum(1.0 - dist / radius, 0.05)
            out[i] = (fmean[idx] * w[:, None]).sum(0)
            continue
        loc, _, f, _ = tree.find_nearest(Vector(c))
        if f is None:
            continue
        a, b, d = P[T[f, 0]], P[T[f, 1]], P[T[f, 2]]
        v0, v1, v2 = b - a, d - a, np.array(loc) - a
        d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
        d20, d21 = v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-30:
            w = np.array([1 / 3, 1 / 3, 1 / 3])
        else:
            y = (d11 * d20 - d01 * d21) / den
            z = (d00 * d21 - d01 * d20) / den
            w = np.clip(np.array([1 - y - z, y, z]), 0, 1)
        out[i] = w @ N[3 * f:3 * f + 3]
    ln = np.linalg.norm(out, axis=1)
    geo = np.empty(len(me.vertices) * 3, np.float32)
    me.vertex_normals.foreach_get("vector", geo)
    geo = geo.reshape(-1, 3)
    bad = ln < 1e-6
    out[~bad] /= ln[~bad][:, None]
    out[bad] = geo[bad]
    me.normals_split_custom_set_from_vertices(out.tolist())
    me.update()
    dots = (out * geo).sum(1)
    return {"median_deg_from_geometry": round(float(np.degrees(np.arccos(np.clip(np.median(dots), -1, 1)))), 1),
            "share_over_90deg": round(float((dots < 0).mean()), 3)}


def bake_onto(src, lod, lod_name, size, samples, extrusion, ray, cage=None, normal_mode="bake"):
    mat, imgs = baked_material(lod_name, size)
    lod.data.materials.clear()
    lod.data.materials.append(mat)
    scene = bpy.context.scene
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    bk = scene.render.bake
    bk.use_selected_to_active = True
    bk.use_cage = cage is not None
    bk.cage_object = cage
    bk.cage_extrusion = 0.0 if cage is not None else extrusion
    bk.max_ray_distance = ray
    bk.margin = max(2, size // 64)
    bk.margin_type = "EXTEND"
    bk.use_clear = True
    bk.target = "IMAGE_TEXTURES"
    for o in scene.objects:
        o.select_set(False)
    src.select_set(True)
    lod.select_set(True)
    bpy.context.view_layer.objects.active = lod
    src_mats = [m for m in src.data.materials if m]
    # hit mask: white emission from LOD0; texels whose ray found nothing stay black
    mask = bpy.data.images.new(f"{lod_name}_mask", size, size, alpha=False, float_buffer=True)
    mask.colorspace_settings.name = "Non-Color"
    mask_node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    mask_node.image = mask
    timings = {}
    for role in ("mask", "basecolor", "orm", "normal"):
        mat.node_tree.nodes.active = mask_node if role == "mask" else imgs[role][1]
        t0 = time.time()
        if role == "normal" and normal_mode == "bake":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
        else:
            undo = rewire_source(src_mats, "normalcopy" if role == "normal" else role)
            # the mask gets no margin, so texels outside UV islands also count as unfilled
            bk.margin = 0 if role == "mask" else max(2, size // 64)
            try:
                bpy.ops.object.bake(type="EMIT")
            finally:
                restore_source(undo)
        timings[role] = round(time.time() - t0, 2)
    t0 = time.time()
    timings["unhit_texel_share"] = fill_misses([imgs[r][0] for r in ("basecolor", "orm", "normal")], mask,
                                               imgs["normal"][0], max_iter=max(8, size // 32))
    timings["fill"] = round(time.time() - t0, 2)
    mat.node_tree.nodes.remove(mask_node)
    bpy.data.images.remove(mask)
    return timings


def fill_misses(images, mask, normal_img, max_iter=32):
    """Replace texels no bake ray reached with the average of their filled neighbours.

    A miss leaves black base colour, zero roughness (reads as chrome) and a zero normal. Texels
    outside the UV islands are grown the same way (a margin); whatever is left after max_iter
    rings takes the mean. Returns the share of texels that were not hit.
    """
    w, h = mask.size
    m = np.empty(w * h * 4, np.float32)
    mask.pixels.foreach_get(m)
    valid = m.reshape(h, w, 4)[..., 0] > 0.5
    share = float(1.0 - valid.mean())
    if valid.all() or not valid.any():
        return round(share, 5)
    stacks = []
    for img in images:
        px = np.empty(w * h * 4, np.float32)
        img.pixels.foreach_get(px)
        stacks.append(px.reshape(h, w, 4))
    X = np.concatenate(stacks, axis=2)
    X[~valid] = 0
    for _ in range(max_iter):
        if valid.all():
            break
        v = valid.astype(np.float32)
        acc = np.zeros_like(X)
        cnt = np.zeros((h, w), np.float32)
        acc[1:] += X[:-1] * v[:-1, :, None]
        cnt[1:] += v[:-1]
        acc[:-1] += X[1:] * v[1:, :, None]
        cnt[:-1] += v[1:]
        acc[:, 1:] += X[:, :-1] * v[:, :-1, None]
        cnt[:, 1:] += v[:, :-1]
        acc[:, :-1] += X[:, 1:] * v[:, 1:, None]
        cnt[:, :-1] += v[:, 1:]
        grow = ~valid & (cnt > 0)
        X[grow] = acc[grow] / cnt[grow][:, None]
        valid |= grow
    if not valid.all():
        X[~valid] = X[valid].mean(0)
    for k, img in enumerate(images):
        px = X[..., 4 * k:4 * k + 4].copy()
        if img is normal_img:
            v3 = px[..., :3] * 2 - 1
            v3 /= np.maximum(np.linalg.norm(v3, axis=2, keepdims=True), 1e-6)
            px[..., :3] = v3 * 0.5 + 0.5
        px[..., 3] = 1.0
        img.pixels.foreach_set(px.reshape(-1))
        img.update()
    return round(share, 5)


# ----------------------------------------------------------------------------- hidden faces

def hemisphere_dirs(k=64, z_min=0.035):
    """k unit directions spread over the +z hemisphere (Fibonacci), down to z_min (2 degrees)."""
    i = np.arange(k) + 0.5
    z = z_min + (1 - z_min) * (1 - i / k)
    r = np.sqrt(1 - z * z)
    phi = i * math.pi * (3 - math.sqrt(5))
    return np.stack([r * np.cos(phi), r * np.sin(phi), z], 1)


def sphere_dirs(k):
    """k unit directions spread over the whole sphere (Fibonacci), from straight up to straight down."""
    i = np.arange(k) + 0.5
    z = 1 - 2 * i / k
    r = np.sqrt(np.maximum(1 - z * z, 0.0))
    phi = i * math.pi * (3 - math.sqrt(5))
    return np.stack([r * np.cos(phi), r * np.sin(phi), z], 1)


def blocked_for_viewer(tree, o, d, far, eps):
    """True when a viewer far out along d cannot see point o.

    A face turned toward that viewer (normal along d) hides o. A face turned away is culled in the
    game (back faces are not drawn), so the ray passes through it. Unsure means visible.
    """
    for _ in range(64):
        loc, hn, _, _ = tree.ray_cast(o, d, far)
        if loc is None:
            return False
        if hn.dot(d) > 0:
            return True
        o = loc + d * eps
    return False


def face_frames(P, T):
    tri = P[T]
    c = tri.mean(1)
    n = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    ln = np.linalg.norm(n, axis=1)
    ok = ln > 0
    n[ok] /= ln[ok][:, None]
    helper = np.where(np.abs(n[:, 2:3]) < 0.9, [[0.0, 0.0, 1.0]], [[1.0, 0.0, 0.0]])
    u = np.cross(n, helper)
    u /= np.maximum(np.linalg.norm(u, axis=1), 1e-30)[:, None]
    v = np.cross(n, u)
    return c, n, u, v, ok, ln / 2


def inner_shell_candidates(P, T, diag, k=64, wall_rel=0.01):
    """Faces that are an enclosed inner layer of LOD0 (candidates for removal; everything else stays).

    Many Pixal3D LOD0s are thin-walled hollows: an outer skin, and a few mm behind it an inner skin
    facing into a sealed cavity (door: 70% of its area; hound 47%; median wall 0.2-0.3% of the
    diagonal). A face qualifies only when both hold:
      wall      straight behind it (or straight in front of it) within wall_rel x diagonal, the ray
                leaves through a surface met from behind: it lies against an outer surface;
      enclosed  every one of k rays over its front hemisphere, down to 2 degrees from grazing, is
                stopped by a face turned toward a viewer beyond it (blocked_for_viewer).
    The ray test alone does not decide removal: dense_seen_faces() must also never see the face.
    """
    tree = bvh_from(P, T)
    c, n, u, v, ok, _ = face_frames(P, T)
    H = hemisphere_dirs(k)
    eps = 1e-4 * diag
    far = 4.0 * diag
    wall_max = wall_rel * diag
    wall = np.zeros(len(T), bool)
    cand = np.zeros(len(T), bool)
    for f in np.flatnonzero(ok).tolist():
        nv = Vector(n[f])
        loc, hn, _, _ = tree.ray_cast(Vector(c[f] - n[f] * eps), -nv, wall_max)
        backed = loc is not None and hn.dot(-nv) > 0
        if not backed:
            loc, hn, _, _ = tree.ray_cast(Vector(c[f] + n[f] * eps), nv, wall_max)
            backed = loc is not None and hn.dot(nv) > 0
        if not backed:
            continue
        wall[f] = True
        o = Vector(c[f] + n[f] * eps)
        dirs = H[:, :1] * u[f] + H[:, 1:2] * v[f] + H[:, 2:3] * n[f]
        if all(blocked_for_viewer(tree, o, Vector(d), far, eps) for d in dirs):
            cand[f] = True
    return cand, {"wall_backed": int(wall.sum()), "wall_backed_and_enclosed": int(cand.sum()),
                  "rays_per_face": k, "wall_max_m": round(wall_max, 5)}


def air_viewpoints(P, T, diag, grid=6, max_points=16):
    """Camera positions inside LOD0's bounding box: in open air and reachable from outside.

    A grid point counts when it is at least 2% of the diagonal from any surface, most of the first
    surfaces around it face it (it is not inside material), and at least one of 26 rays escapes (it
    is not a sealed cavity). A farthest-point subset of up to max_points is returned; these give the
    views from inside concave parts (a ship's hull, under a cart) that outside cameras never get.
    """
    tree = bvh_from(P, T)
    lo, hi = P.min(0), P.max(0)
    dirs = [Vector(d) for d in sphere_dirs(26)]
    pts = []
    steps = (np.arange(grid) + 0.5) / grid
    for gx in steps:
        for gy in steps:
            for gz in steps:
                p = lo + (hi - lo) * np.array([gx, gy, gz])
                pv = Vector(p)
                near = tree.find_nearest(pv)
                if near[0] is not None and near[3] < 0.02 * diag:
                    continue
                esc = hits = back = 0
                for d in dirs:
                    loc, hn, _, _ = tree.ray_cast(pv, d, 4 * diag)
                    if loc is None:
                        esc += 1
                        continue
                    hits += 1
                    back += hn.dot(d) > 0
                if esc and back * 2 <= hits:
                    pts.append(p)
    if len(pts) <= max_points:
        return pts
    pts = np.array(pts)
    centre = (lo + hi) / 2
    chosen = [int(np.argmin(np.linalg.norm(pts - centre, axis=1)))]
    dmin = np.linalg.norm(pts - pts[chosen[0]], axis=1)
    while len(chosen) < max_points:
        j = int(np.argmax(dmin))
        chosen.append(j)
        dmin = np.minimum(dmin, np.linalg.norm(pts - pts[j], axis=1))
    return [pts[j] for j in chosen]


def look_rotation(fwd):
    """Camera rotation looking along fwd (Blender cameras look down local -Z, local +Y is up)."""
    from mathutils import Matrix
    fwd = np.asarray(fwd, float) / np.linalg.norm(fwd)
    hint = np.array([0.0, 0.0, 1.0]) if abs(fwd[2]) < 0.99 else np.array([0.0, 1.0, 0.0])
    right = np.cross(fwd, hint)
    right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    return Matrix((right.tolist(), up.tolist(), (-fwd).tolist())).transposed().to_euler(), right, up


def dense_seen_faces(src, nfaces, lo, hi, tmpdir, viewpoints, n_dirs=160, size=1024, cube=512):
    """Faces that show up in face-ID renders (back faces culled) from n_dirs orthographic directions
    over the whole sphere (including from below) at size px, plus a 90-degree cube of perspective
    views at cube px from every air viewpoint (inside concave parts)."""
    scene = bpy.context.scene
    hidden = {o: o.hide_render for o in scene.objects}
    me = src.data.copy()
    obj = bpy.data.objects.new("_faceid", me)
    scene.collection.objects.link(obj)
    ids = np.arange(nfaces) + 1
    attr = me.color_attributes.new("fid", "FLOAT_COLOR", "FACE")
    cols = np.stack([(ids & 255) / 255.0, ((ids >> 8) & 255) / 255.0, ((ids >> 16) & 255) / 255.0,
                     np.ones(nfaces)], 1).astype(np.float32)
    attr.data.foreach_set("color", cols.reshape(-1))
    mat = bpy.data.materials.new("_faceid")
    mat.use_nodes = True
    mat.use_backface_culling = True
    nt = mat.node_tree
    nt.nodes.clear()
    at = nt.nodes.new("ShaderNodeAttribute")
    at.attribute_name = "fid"
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(at.outputs["Color"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    me.materials.clear()
    me.materials.append(mat)
    for o in scene.objects:
        o.hide_render = o is not obj
    cam = bpy.data.objects.new("_faceid_cam", bpy.data.cameras.new("_faceid_cam"))
    scene.collection.objects.link(cam)
    centre = (lo + hi) / 2
    diag = float(np.linalg.norm(hi - lo))
    prev = (scene.render.engine, scene.camera, scene.view_settings.view_transform, scene.render.film_transparent,
            scene.render.resolution_x, scene.render.resolution_y, scene.render.filter_size,
            scene.render.image_settings.file_format, scene.render.image_settings.color_depth,
            scene.render.image_settings.color_mode, scene.render.filepath)
    scene.render.engine = engine_eevee()
    scene.camera = cam
    scene.view_settings.view_transform = "Raw"
    scene.render.film_transparent = True
    scene.render.resolution_percentage = 100
    scene.render.filter_size = 0.0
    try:
        samples = scene.eevee.taa_render_samples
        scene.eevee.taa_render_samples = 1
    except AttributeError:
        samples = None
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_depth = "32"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = os.path.join(tmpdir, "_faceid.exr")
    seen = np.zeros(nfaces, bool)
    views = [("ORTHO", centre + d * diag * 1.5, -d, size) for d in sphere_dirs(n_dirs)]
    for p in viewpoints:
        for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
            views.append(("PERSP", np.asarray(p, float), np.array(d, float), cube))
    stats = {"ortho_views": n_dirs, "ortho_px": size, "air_viewpoints": len(viewpoints), "cube_px": cube,
             "renders": len(views)}
    try:
        for kind, loc, fwd, px_size in views:
            cam.data.type = kind
            if kind == "ORTHO":
                cam.data.ortho_scale = diag * 1.02
                cam.data.clip_start = diag * 0.01
                cam.data.clip_end = diag * 3
            else:
                cam.data.sensor_fit = "AUTO"
                cam.data.angle = math.pi / 2
                cam.data.clip_start = diag * 1e-3
                cam.data.clip_end = diag * 4
            scene.render.resolution_x = scene.render.resolution_y = px_size
            cam.location = Vector(np.asarray(loc, float).tolist())
            cam.rotation_euler = look_rotation(fwd)[0]
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(scene.render.filepath)
            px = np.empty(px_size * px_size * 4, np.float32)
            img.pixels.foreach_get(px)
            bpy.data.images.remove(img)
            px = px.reshape(-1, 4)
            px = px[px[:, 3] > 0.99]
            rgb = np.round(px[:, :3] * 255).astype(np.int64)
            fid = rgb[:, 0] + (rgb[:, 1] << 8) + (rgb[:, 2] << 16) - 1
            fid = fid[(fid >= 0) & (fid < nfaces)]
            seen[fid] = True
    finally:
        bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.meshes.remove(me)
        bpy.data.materials.remove(mat)
        for o, h in hidden.items():
            o.hide_render = h
        (scene.render.engine, scene.camera, scene.view_settings.view_transform, scene.render.film_transparent,
         scene.render.resolution_x, scene.render.resolution_y, scene.render.filter_size,
         scene.render.image_settings.file_format, scene.render.image_settings.color_depth,
         scene.render.image_settings.color_mode, scene.render.filepath) = prev
        if samples is not None:
            scene.eevee.taa_render_samples = samples
        try:
            os.remove(os.path.join(tmpdir, "_faceid.exr"))
        except OSError:
            pass
    return seen, stats


def label_components(n, a, b):
    """Connected-component labels of n nodes joined by edges (a[i], b[i]) (min-label propagation)."""
    lab = np.arange(n)
    for _ in range(10000):
        la, lb = lab[a], lab[b]
        m = np.minimum(la, lb)
        if np.array_equal(la, m) and np.array_equal(lb, m):
            break
        np.minimum.at(lab, la, m)
        np.minimum.at(lab, lb, m)
        lab = lab[lab]
    return lab


def face_clusters(P, T, mask, tol=1e-6):
    """Connected clusters (faces sharing a welded edge) among the faces in mask; -1 elsewhere."""
    idx = np.flatnonzero(mask)
    out = np.full(len(T), -1)
    if not len(idx):
        return out
    q = np.round(P / tol).astype(np.int64)
    _, pid = np.unique(q, axis=0, return_inverse=True)
    W = pid.reshape(-1)[T[idx]]
    e = np.sort(np.concatenate([W[:, [0, 1]], W[:, [1, 2]], W[:, [2, 0]]]), axis=1)
    key = e[:, 0] * (int(W.max()) + 1) + e[:, 1]
    f = np.tile(np.arange(len(idx)), 3)
    o = np.argsort(key, kind="stable")
    ks, fs = key[o], f[o]
    same = ks[1:] == ks[:-1]
    lab = label_components(len(idx), fs[:-1][same], fs[1:][same])
    out[idx] = np.unique(lab, return_inverse=True)[1].reshape(-1)
    return out


def hidden_faces(src, arrays, diag, tmpdir, args, cache_key, max_open_edge=None):
    """Faces of LOD0 to drop before routes B and C: enclosed inner layers never seen in dense renders.

    Removed = inner_shell_candidates & not seen by dense_seen_faces, in clusters of at least
    args.hidden_min_cluster faces (smaller clusters are kept). The masks are cached in tmpdir per
    LOD0 hash and settings, so a rerun after a crash does not repeat the renders. When dropping every
    candidate would open more than twice max_open_edge metres of edges the renders are skipped: the
    drop will be refused anyway (the inner layer is joined to the outer surface).
    """
    P, T = arrays["P"], arrays["T"]
    settings = f"{args.hidden_rays}_{args.hidden_wall}_{args.hidden_views}_{args.hidden_px}_{args.hidden_air_points}"
    cache = os.path.join(tmpdir, f"_hidden_{cache_key[:12]}_{hashlib.sha1(settings.encode()).hexdigest()[:8]}.npz")
    stats = {}
    if os.path.exists(cache):
        z = np.load(cache)
        if len(z["cand"]) == len(T):
            stats = json.loads(str(z["stats"]))
            stats["from_cache"] = os.path.basename(cache)
            return z["removed"], stats
    t0 = time.time()
    cand, st = inner_shell_candidates(P, T, diag, k=args.hidden_rays, wall_rel=args.hidden_wall)
    stats.update(st)
    stats["ray_seconds"] = round(time.time() - t0, 2)
    seen = np.zeros(len(T), bool)
    opened = weld_topology(P, T[~cand])["welded_boundary_length_m"] if cand.any() else 0.0
    stats["candidates_open_edge_m"] = opened
    if cand.any() and max_open_edge is not None and opened > 2 * max_open_edge:
        stats["renders_skipped"] = f"dropping all candidates opens {opened} m of edges > 2 x {round(max_open_edge, 3)} m"
    elif cand.any():
        t0 = time.time()
        points = air_viewpoints(P, T, diag, max_points=args.hidden_air_points)
        seen, st = dense_seen_faces(src, len(T), P.min(0), P.max(0), tmpdir, points,
                                    n_dirs=args.hidden_views, size=args.hidden_px)
        stats.update(st)
        stats["render_seconds"] = round(time.time() - t0, 2)
    removed = cand & ~seen
    stats["candidates_seen_in_renders"] = int((cand & seen).sum())
    cl = face_clusters(P, T, removed)
    if removed.any():
        sizes = np.bincount(cl[removed])
        small = sizes < args.hidden_min_cluster
        stats["small_clusters_kept"] = int(small.sum())
        stats["small_cluster_faces_kept"] = int(sizes[small].sum())
        removed &= ~np.isin(cl, np.flatnonzero(small))
    stats["removed"] = int(removed.sum())
    np.savez_compressed(cache, cand=cand, removed=removed, stats=json.dumps(stats))
    return removed, stats


def subset_faces(arrays, keep):
    corners = np.repeat(keep, 3)
    out = dict(arrays)
    out["T"] = arrays["T"][keep]
    out["UV"] = arrays["UV"][corners]
    out["N"] = arrays["N"][corners]
    out["FM"] = arrays["FM"][keep]
    return out


# ----------------------------------------------------------------------------- render probe

class RenderProbe:
    """In-session EEVEE renders of a GLB from fixed cameras, back faces culled, for the render gate.

    Imports the file into the current scene, hides everything else from render, renders the views
    and removes what the import created, so it can run between bakes.
    """

    # (name, azimuth, elevation) in degrees; azimuth 0 looks at the model's front (glTF +Z)
    VIEWS = (("front", 0.0, 10.0), ("back", 180.0, 10.0), ("left", 90.0, 10.0), ("right", 270.0, 10.0),
             ("top", 0.0, 90.0), ("low_3q", 40.0, -25.0), ("high_3q_front", -35.0, 18.0),
             ("high_3q_back", 145.0, 32.0))

    def __init__(self, lo_gltf, hi_gltf, size, tmpdir):
        self.lo = np.array([lo_gltf[0], -hi_gltf[2], lo_gltf[1]])
        self.hi = np.array([hi_gltf[0], -lo_gltf[2], hi_gltf[1]])
        self.size = size
        self.tmp = os.path.join(tmpdir, "_probe_view.png")

    def _camera(self, az, el):
        centre = (self.lo + self.hi) / 2
        radius = float(np.linalg.norm(self.hi - self.lo)) / 2
        a, e = math.radians(az), math.radians(el)
        view = np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)])
        rot, right, up = look_rotation(-view)
        corners = np.array([[x, y, z] for x in (self.lo[0], self.hi[0]) for y in (self.lo[1], self.hi[1])
                            for z in (self.lo[2], self.hi[2])]) - centre
        t = math.tan(math.atan(18.0 / 50.0)) * 0.92
        dist = max(max(max(abs(c @ right), abs(c @ up)) / t + c @ view for c in corners), radius * 0.5)
        return centre, view, dist, rot

    def render(self, path):
        scene = bpy.context.scene
        before_objs = set(bpy.data.objects)
        before = {k: set(getattr(bpy.data, k)) for k in ("meshes", "materials", "images", "cameras", "lights",
                                                          "worlds", "node_groups")}
        hidden = {o: o.hide_render for o in scene.objects}
        for o in scene.objects:
            o.hide_render = True
        prev = (scene.render.engine, scene.camera, scene.world, scene.render.film_transparent,
                scene.render.resolution_x, scene.render.resolution_y, scene.render.filepath)
        import_glb(path)
        new_objs = set(bpy.data.objects) - before_objs
        for o in new_objs:
            o.hide_render = False
        for m in set(bpy.data.materials) - before["materials"]:
            m.use_backface_culling = True
        cam = bpy.data.objects.new("_probe_cam", bpy.data.cameras.new("_probe_cam"))
        scene.collection.objects.link(cam)
        cam.data.lens = 50.0
        cam.data.sensor_fit = "HORIZONTAL"
        cam.data.sensor_width = 36.0
        sun = bpy.data.objects.new("_probe_sun", bpy.data.lights.new("_probe_sun", "SUN"))
        scene.collection.objects.link(sun)
        sun.data.energy = 3.0
        world = bpy.data.worlds.new("_probe_world")
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.58, 1)
        scene.world = world
        scene.camera = cam
        scene.render.engine = engine_eevee()
        scene.render.film_transparent = True
        scene.render.resolution_x = scene.render.resolution_y = self.size
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = "PNG"
        scene.render.image_settings.color_mode = "RGBA"
        scene.view_settings.view_transform = "Standard"
        scene.render.filepath = self.tmp
        try:
            samples = scene.eevee.taa_render_samples
            scene.eevee.taa_render_samples = 16
        except AttributeError:
            samples = None
        out = []
        try:
            for _, az, el in self.VIEWS:
                centre, view, dist, rot = self._camera(az, el)
                cam.data.clip_start = dist * 0.01
                cam.data.clip_end = dist * 10
                cam.location = Vector((centre + view * dist).tolist())
                cam.rotation_euler = rot
                sun.rotation_euler = (math.radians(50), 0, math.radians(az - 30))
                bpy.ops.render.render(write_still=True)
                img = bpy.data.images.load(self.tmp)
                px = np.empty(self.size * self.size * 4, np.float32)
                img.pixels.foreach_get(px)
                bpy.data.images.remove(img)
                out.append(px.reshape(self.size, self.size, 4))
        finally:
            if samples is not None:
                scene.eevee.taa_render_samples = samples
            for o in new_objs | {cam, sun}:
                bpy.data.objects.remove(o, do_unlink=True)
            for k, old in before.items():
                coll = getattr(bpy.data, k)
                for item in set(coll) - old:
                    if item.users == 0 or k in ("cameras", "lights", "worlds"):
                        coll.remove(item)
            for o, h in hidden.items():
                o.hide_render = h
            (scene.render.engine, scene.camera, scene.world, scene.render.film_transparent,
             scene.render.resolution_x, scene.render.resolution_y, scene.render.filepath) = prev
        return out


def dilate(mask, steps):
    out = mask.copy()
    for _ in range(steps):
        grown = out.copy()
        grown[1:] |= out[:-1]
        grown[:-1] |= out[1:]
        grown[:, 1:] |= out[:, :-1]
        grown[:, :-1] |= out[:, 1:]
        out = grown
    return out


def hole_mask(ref_view, view):
    """See-through holes: pixels LOD0 covers and the LOD leaves transparent, in patches that do not
    touch LOD0's own background. A hole in a deck or a crack across a board counts; an outline, a
    sail edge or a thin rope that moves or gets thinner does not (silhouette match covers those)."""
    ma, mb = ref_view[..., 3] > 0.5, view[..., 3] > 0.5
    diff = ma & ~mb
    if not diff.any():
        return diff
    h, w = diff.shape
    idx = np.arange(h * w).reshape(h, w)
    hor = diff[:, :-1] & diff[:, 1:]
    ver = diff[:-1, :] & diff[1:, :]
    lab = label_components(h * w, np.concatenate([idx[:, :-1][hor], idx[:-1, :][ver]]),
                           np.concatenate([idx[:, 1:][hor], idx[1:, :][ver]])).reshape(h, w)
    edge = np.zeros((h, w), bool)
    edge[0] = edge[-1] = True
    edge[:, 0] = edge[:, -1] = True
    open_ = diff & (dilate(~ma, 1) | edge)
    return diff & ~np.isin(lab, np.unique(lab[open_]))


def render_delta(ref_views, views, tolerance_px=2):
    """Silhouette agreement, colour error of 8x8-pooled renders and see-through holes, per view.

    silhouette_match ignores disagreement within tolerance_px of the other outline, so an edge-on
    thin board that is a pixel thicker does not count; holes, shards and lost parts still do.
    silhouette_iou is reported only. hole_share is hole_mask() as % of LOD0's covered pixels.
    blotch_share is the % of covered 8x8 blocks whose colour error exceeds BLOTCH_ERROR: patches
    of wrong colour that the mean colour error dilutes.
    """
    ious, matches, errs, holes, blotch, patch = [], [], [], [], [], []
    for a, b in zip(ref_views, views):
        ma, mb = a[..., 3] > 0.5, b[..., 3] > 0.5
        union = (ma | mb).sum()
        ious.append(float((ma & mb).sum() / union) if union else 1.0)
        off = (ma & ~dilate(mb, tolerance_px)).sum() + (mb & ~dilate(ma, tolerance_px)).sum()
        matches.append(float(1.0 - off / union) if union else 1.0)
        holes.append(float(hole_mask(a, b).sum() / max(ma.sum(), 1) * 100))
        grey = np.array([0.5, 0.5, 0.5])
        ca = a[..., :3] * a[..., 3:4] + grey * (1 - a[..., 3:4])
        cb = b[..., :3] * b[..., 3:4] + grey * (1 - b[..., 3:4])
        h = a.shape[0] // 8 * 8
        pool = lambda c: c[:h, :h].reshape(h // 8, 8, h // 8, 8, 3).mean(axis=(1, 3))
        pa, pb = pool(ca), pool(cb)
        cover = (ma | mb)[:h, :h].reshape(h // 8, 8, h // 8, 8).mean(axis=(1, 3)) > 0
        berr = np.abs(pa - pb).mean(axis=2)
        block = berr[cover]
        errs.append(float(block.mean()) if cover.any() else 0.0)
        blotch.append(float((block > BLOTCH_ERROR).mean() * 100) if cover.any() else 0.0)
        patch.append(largest_patch(cover & (berr > BLOTCH_ERROR)) / max(int(cover.sum()), 1) * 100)
    return {"views": [v[0] for v in RenderProbe.VIEWS][:len(matches)],
            "silhouette_match_min": round(min(matches), 4), "silhouette_match_views": [round(v, 4) for v in matches],
            "silhouette_iou_min": round(min(ious), 4), "silhouette_iou_views": [round(v, 4) for v in ious],
            "colour_error_mean": round(float(np.mean(errs)), 4), "colour_error_views": [round(v, 4) for v in errs],
            "hole_share_max_pct": round(max(holes), 4), "hole_share_views_pct": [round(v, 4) for v in holes],
            "blotch_share_max_pct": round(max(blotch), 3), "blotch_share_views_pct": [round(v, 3) for v in blotch],
            "blotch_patch_max_pct": round(max(patch), 3), "blotch_patch_views_pct": [round(v, 3) for v in patch]}


def largest_patch(mask):
    """Blocks in the largest 4-connected patch of mask (a solid wrong-coloured area, as opposed to
    scattered blocks of lost detail)."""
    if not mask.any():
        return 0
    h, w = mask.shape
    idx = np.arange(h * w).reshape(h, w)
    hor = mask[:, :-1] & mask[:, 1:]
    ver = mask[:-1, :] & mask[1:, :]
    lab = label_components(h * w, np.concatenate([idx[:, :-1][hor], idx[:-1, :][ver]]),
                           np.concatenate([idx[:, 1:][hor], idx[1:, :][ver]])).reshape(h, w)
    return int(np.bincount(lab[mask]).max())


def pixel_change_pct(ref_view, view, tol=0.1):
    """Pixels whose coverage flips or whose colour moves by more than tol, % of LOD0's pixels."""
    ma, mb = ref_view[..., 3] > 0.5, view[..., 3] > 0.5
    moved = ma & mb & (np.abs(ref_view[..., :3] - view[..., :3]).max(axis=2) > tol)
    return float(((ma ^ mb) | moved).sum() / max(ma.sum(), 1) * 100)


def holes_sheet(rows, out_path, cell=256):
    """What the render gate saw: one row per LOD (LOD0 first), one column per probe view, on pink,
    with the see-through holes the gate counted painted red."""
    rows = [r for r in rows if r[0]]
    nv = len(rows[0][0])
    canvas = np.zeros((len(rows) * cell, nv * cell, 4), np.float32)
    bg = np.array([0.93, 0.72, 0.86, 1.0], np.float32)
    for r, (views, ref) in enumerate(rows):
        for c, v in enumerate(views):
            f = v.shape[0] // cell
            img = v[: cell * f, : cell * f].reshape(cell, f, cell, f, 4).mean(axis=(1, 3))
            comp = img * img[..., 3:4] + bg * (1 - img[..., 3:4])
            if ref is not None:
                h = hole_mask(ref[c], v)[: cell * f, : cell * f].reshape(cell, f, cell, f).any(axis=(1, 3))
                comp[h] = (1.0, 0.0, 0.0, 1.0)
            comp[..., 3] = 1.0
            y0 = (len(rows) - 1 - r) * cell
            canvas[y0:y0 + cell, c * cell:(c + 1) * cell] = comp
    out = bpy.data.images.new("holes_sheet", nv * cell, len(rows) * cell, alpha=False)
    out.pixels.foreach_set(canvas.reshape(-1))
    sc = bpy.context.scene
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    out.save_render(out_path, scene=sc)
    bpy.data.images.remove(out)
    return out_path


# ----------------------------------------------------------------------------- deviation

def bvh_from(P, T):
    return BVHTree.FromPolygons([tuple(p) for p in P], [tuple(t) for t in T], all_triangles=True)


def one_sided(points, tree, cap=40000):
    if len(points) > cap:
        points = points[np.linspace(0, len(points) - 1, cap).astype(np.int64)]
    d = np.empty(len(points))
    for i, p in enumerate(points):
        hit = tree.find_nearest(Vector(p))
        d[i] = hit[3] if hit[0] is not None else np.inf
    return d


def deviation(PA, TA, TA_samples, PB, TB, diag):
    """Two-sided vertex/centroid-to-surface distances in % of LOD0's bounding diagonal.

    LOD0 is sampled only on TA_samples (its visible faces when hidden faces were dropped).
    """
    ta, tb = bvh_from(PA, TA), bvh_from(PB, TB)
    samples = lambda P, T: np.concatenate([P[np.unique(T)], P[T].mean(1)])
    a_to_b = one_sided(samples(PA, TA_samples), tb) / diag * 100
    b_to_a = one_sided(samples(PB, TB), ta) / diag * 100
    r = lambda d: [round(float(v), 3) for v in np.percentile(d, [50, 95, 99, 100])]
    return {"lod0_to_lod_pct_p50_p95_p99_max": r(a_to_b), "lod_to_lod0_pct_p50_p95_p99_max": r(b_to_a)}


# ----------------------------------------------------------------------------- GLB measurement

def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    magic, _, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:
        raise ValueError(f"not a GLB: {path}")
    off, js, binchunk = 12, None, b""
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == 0x4E4F534A:
            js = json.loads(chunk)
        elif ctype == 0x004E4942:
            binchunk = chunk
    return js, binchunk


def accessor(js, blob, idx):
    a = js["accessors"][idx]
    bv = js["bufferViews"][a["bufferView"]]
    comps = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    dt = np.dtype({5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}[a["componentType"]])
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride") or comps * dt.itemsize
    if stride == comps * dt.itemsize:
        return np.frombuffer(blob, dt, a["count"] * comps, start).reshape(a["count"], comps)
    raw = np.frombuffer(blob, np.uint8, (a["count"] - 1) * stride + comps * dt.itemsize, start)
    rows = [raw[i * stride:i * stride + comps * dt.itemsize].view(dt) for i in range(a["count"])]
    return np.stack(rows)


def image_dims(b):
    if b[:8] == b"\x89PNG\r\n\x1a\n":
        return struct.unpack(">II", b[16:24]), "png"
    if b[:2] == b"\xff\xd8":
        i = 2
        while i < len(b) - 9:
            if b[i] != 0xFF:
                i += 1
                continue
            marker = b[i + 1]
            if marker in (0xC0, 0xC1, 0xC2):
                h, w = struct.unpack(">HH", b[i + 5:i + 9])
                return (w, h), "jpeg"
            i += 2 + struct.unpack(">H", b[i + 2:i + 4])[0]
    if b[:4] == b"RIFF" and b[8:12] == b"WEBP":
        return (0, 0), "webp"
    return (0, 0), "unknown"


def node_matrices(js):
    mats = {}

    def local(n):
        if "matrix" in n:
            return np.array(n["matrix"], float).reshape(4, 4).T
        m = np.eye(4)
        if "scale" in n:
            m = np.diag(list(n["scale"]) + [1.0]) @ m
        if "rotation" in n:
            x, y, z, w = n["rotation"]
            r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                          [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                          [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
            rm = np.eye(4)
            rm[:3, :3] = r
            m = rm @ m
        if "translation" in n:
            t = np.eye(4)
            t[:3, 3] = n["translation"]
            m = t @ m
        return m

    def walk(i, parent):
        m = parent @ local(js["nodes"][i])
        mats[i] = m
        for c in js["nodes"][i].get("children", []):
            walk(c, m)

    for scene in js.get("scenes", []):
        for i in scene.get("nodes", []):
            walk(i, np.eye(4))
    return mats


def glb_geometry(path):
    js, blob = read_glb(path)
    Ps, Ts, prims, off = [], [], [], 0
    for ni, m in node_matrices(js).items():
        node = js["nodes"][ni]
        if "mesh" not in node:
            continue
        for prim in js["meshes"][node["mesh"]]["primitives"]:
            P = accessor(js, blob, prim["attributes"]["POSITION"]).astype(np.float64)
            P = (np.c_[P, np.ones(len(P))] @ m.T)[:, :3]
            I = accessor(js, blob, prim["indices"]).reshape(-1).astype(np.int64) if "indices" in prim \
                else np.arange(len(P))
            Ps.append(P)
            Ts.append(I.reshape(-1, 3) + off)
            off += len(P)
            prims.append(prim)
    return js, blob, np.concatenate(Ps), np.concatenate(Ts), prims


def weld_topology(P, T, tol=1e-5, parts=False):
    """Open edges, components and shards of the mesh welded by position (tol in metres)."""
    q = np.round(P / tol).astype(np.int64)
    _, inv = np.unique(q, axis=0, return_inverse=True)
    W = inv.reshape(-1)[T]
    rows = np.flatnonzero((W[:, 0] != W[:, 1]) & (W[:, 1] != W[:, 2]) & (W[:, 0] != W[:, 2]))
    W = W[rows]
    n = int(inv.max()) + 1 if len(inv) else 0
    e = np.sort(np.concatenate([W[:, [0, 1]], W[:, [1, 2]], W[:, [2, 0]]]), axis=1)
    key = e[:, 0] * n + e[:, 1]
    uk, cnt = np.unique(key, return_counts=True)
    ea, eb = uk // n, uk % n
    lab = np.arange(n)
    for _ in range(10000):
        la, lb = lab[ea], lab[eb]
        m = np.minimum(la, lb)
        if np.array_equal(la, m) and np.array_equal(lb, m):
            break
        np.minimum.at(lab, la, m)
        np.minimum.at(lab, lb, m)
        lab = lab[lab]
    comp = lab[W[:, 0]]
    _, cinv, sizes = np.unique(comp, return_inverse=True, return_counts=True)
    cinv = cinv.reshape(-1)
    Pw = np.zeros((n, 3))
    Pw[inv.reshape(-1)] = P
    b = cnt == 1
    fa = np.linalg.norm(np.cross(Pw[W[:, 1]] - Pw[W[:, 0]], Pw[W[:, 2]] - Pw[W[:, 0]]), axis=1) / 2
    carea = np.bincount(cinv, weights=fa, minlength=len(sizes))
    shard = sizes <= SHARD_TRIS
    out = {
        "welded_boundary_length_m": round(float(np.linalg.norm(Pw[ea[b]] - Pw[eb[b]], axis=1).sum()), 4),
        "welded_boundary_edge_share": round(float((cnt == 1).sum()) / max(len(uk), 1), 4),
        "welded_nonmanifold_edge_share": round(float((cnt > 2).sum()) / max(len(uk), 1), 4),
        "welded_components": int(len(sizes)),
        "tris_in_components_le8_share": round(float(sizes[shard].sum()) / max(len(W), 1), 4),
        "shard_components": int(shard.sum()),
        "shard_area_share": round(float(carea[shard].sum() / max(fa.sum(), 1e-30)), 6),
    }
    if not parts:
        return out
    return out, {"Pw": Pw, "W": W, "cinv": cinv, "sizes": sizes, "carea": carea, "fa": fa, "rows": rows}


def fragments(parts, ref_parts, diag, samples=24):
    """Pieces of the LOD that broke off LOD0.

    Every welded LOD component is matched to the LOD0 component under most of its vertices (up to
    `samples` vertices, nearest LOD0 surface). When several LOD components match the same LOD0
    component, all but the largest (by area) broke off it; so does a component lying farther than
    1% of the diagonal from LOD0. A broken-off component of <= SHARD_TRIS triangles is a new shard.
    A small loose piece of LOD0 that simplification shrank to a shard matches only itself and is
    not new. Returns per-component masks (new shard, detached) and counts/area shares.
    """
    sizes, cinv, W, Pw, fa, carea = (parts[k] for k in ("sizes", "cinv", "W", "Pw", "fa", "carea"))
    nc = len(sizes)
    out = {"new_shard": np.zeros(nc, bool), "detached": np.zeros(nc, bool)}
    if "tree" not in ref_parts:
        ref_parts["tree"] = bvh_from(ref_parts["Pw"], ref_parts["W"])
    tree = ref_parts["tree"]
    match = np.full(nc, -1)
    far = np.zeros(nc, bool)
    order = np.argsort(cinv, kind="stable")
    bounds = np.searchsorted(cinv[order], np.arange(nc + 1))
    for c in range(nc):
        tris = order[bounds[c]:bounds[c + 1]]
        verts = np.unique(W[tris])
        if len(verts) > samples:
            verts = verts[np.linspace(0, len(verts) - 1, samples).astype(np.int64)]
        votes, dists = [], []
        for v in verts.tolist():
            loc, _, f, d = tree.find_nearest(Vector(Pw[v]))
            if loc is not None:
                votes.append(int(ref_parts["cinv"][f]))
                dists.append(d)
        if not votes:
            far[c] = True
            continue
        match[c] = np.bincount(votes).argmax()
        far[c] = float(np.median(dists)) > 0.01 * diag
    detached = far.copy()
    for k in np.unique(match[match >= 0]).tolist():
        comps = np.flatnonzero(match == k)
        if len(comps) > 1:
            biggest = comps[np.argmax(carea[comps])]
            detached[comps[comps != biggest]] = True
    out["detached"] = detached
    out["new_shard"] = detached & (sizes <= SHARD_TRIS)
    tot = max(fa.sum(), 1e-30)
    out["new_shard_components"] = int(out["new_shard"].sum())
    out["new_shard_area_share"] = round(float(carea[out["new_shard"]].sum() / tot), 6)
    out["detached_components"] = int(detached.sum())
    out["detached_area_share"] = round(float(carea[detached].sum() / tot), 6)
    return out


def new_shard_faces(P, T, ref_parts, diag):
    """Faces of T (rows) that belong to new shards, for dropping them before a LOD is exported."""
    topo, parts = weld_topology(P, T, parts=True)
    fr = fragments(parts, ref_parts, diag)
    drop = np.zeros(len(T), bool)
    if fr["new_shard_components"]:
        drop[parts["rows"]] = fr["new_shard"][parts["cinv"]]
    return drop, {"dropped_new_shards": fr["new_shard_components"],
                  "dropped_new_shard_area_share": fr["new_shard_area_share"]}


def measure_glb(path, ref=None):
    js, blob, P, T, prims = glb_geometry(path)
    mats = js.get("materials", [])
    texs = js.get("textures", [])
    imgs = js.get("images", [])
    image_info = []
    for im in imgs:
        if "bufferView" in im:
            bv = js["bufferViews"][im["bufferView"]]
            b = blob[bv.get("byteOffset", 0): bv.get("byteOffset", 0) + bv["byteLength"]]
            dims, fmt = image_dims(b)
            image_info.append({"embedded": True, "dims": list(dims), "format": fmt, "bytes": len(b)})
        else:
            image_info.append({"embedded": False, "uri": im.get("uri")})

    def tex(ref_obj):
        if not ref_obj:
            return None
        src = texs[ref_obj["index"]].get("source")
        return image_info[src] if src is not None and src < len(image_info) else None

    prim_rows = []
    for prim in prims:
        attrs = prim["attributes"]
        row = {"attributes": sorted(attrs), "material": prim.get("material")}
        if "TEXCOORD_0" in attrs:
            uv = accessor(js, blob, attrs["TEXCOORD_0"]).astype(np.float64)
            row["uv_finite"] = bool(np.isfinite(uv).all())
            row["uv_range"] = [round(float(uv.min()), 4), round(float(uv.max()), 4)]
            if "indices" in prim:
                t = uv[accessor(js, blob, prim["indices"]).reshape(-1, 3).astype(np.int64)]
                row["uv_area"] = round(float(0.5 * np.abs(
                    (t[:, 1, 0] - t[:, 0, 0]) * (t[:, 2, 1] - t[:, 0, 1])
                    - (t[:, 2, 0] - t[:, 0, 0]) * (t[:, 1, 1] - t[:, 0, 1])).sum()), 4)
        if prim.get("material") is not None:
            mat = mats[prim["material"]]
            pbr = mat.get("pbrMetallicRoughness", {})
            row["material_name"] = mat.get("name")
            row["alpha_mode"] = mat.get("alphaMode", "OPAQUE")
            row["basecolor"] = tex(pbr.get("baseColorTexture"))
            row["metallic_roughness"] = tex(pbr.get("metallicRoughnessTexture"))
            row["normal"] = tex(mat.get("normalTexture"))
            row["occlusion"] = tex(mat.get("occlusionTexture"))
        prim_rows.append(row)
    lo, hi = P.min(0), P.max(0)
    out = {
        "file_bytes": os.path.getsize(path),
        "triangles": int(len(T)),
        "vertices": int(len(P)),
        "primitives": prim_rows,
        "images": image_info,
        "image_bytes": int(sum(i.get("bytes", 0) for i in image_info)),
        "bounds_min": [round(float(v), 4) for v in lo],
        "bounds_max": [round(float(v), 4) for v in hi],
    }
    topo, parts = weld_topology(P, T, parts=True)
    out.update(topo)
    if ref is not None:
        diag = float(np.linalg.norm(ref["hi"] - ref["lo"]))
        out["bounds_delta_pct_of_diag"] = round(float(max(np.abs(lo - ref["lo"]).max(),
                                                         np.abs(hi - ref["hi"]).max())) / diag * 100, 3)
        out.update(deviation(ref["P"], ref["T"], ref.get("Tvis", ref["T"]), P, T, diag))
        if "parts" not in ref:
            ref["parts"] = weld_topology(ref["P"], ref["T"], parts=True)[1]
        fr = fragments(parts, ref["parts"], diag)
        for k in ("new_shard_components", "new_shard_area_share", "detached_components", "detached_area_share"):
            out[k] = fr[k]
    return out, (P, T, lo, hi)


def open_edge_allowance(lod0):
    """Most open-edge length a LOD may have: LOD0's FULL welded open-edge length x 1.25 plus half the
    bounding diagonal. A decimated border stays about as long; islands pulling apart, or faces
    dropped as hidden, add open edges."""
    diag = float(np.linalg.norm(np.array(lod0["bounds_max"]) - np.array(lod0["bounds_min"])))
    return 1.25 * lod0["welded_boundary_length_m"] + 0.5 * diag


def gate(lod, lod0, target, prev_tris, limits=None, impostor=False):
    """Pass/fail checks; each failure is named. An impostor is made of separate quads by design, so
    the open-edge and shard checks do not apply to it (the render checks do)."""
    fails = []
    rd = lod.get("render")
    if rd and limits:
        if rd["silhouette_match_min"] < limits["silhouette_match_min"]:
            fails.append(f"silhouette match {rd['silhouette_match_min']} < {limits['silhouette_match_min']}")
        if rd["colour_error_mean"] > limits["colour_error_max"]:
            fails.append(f"render colour error {rd['colour_error_mean']} > {limits['colour_error_max']}")
        if rd.get("blotch_patch_max_pct", 0) > limits["blotch_patch_max_pct"]:
            fails.append(f"wrong-colour patch {rd['blotch_patch_max_pct']}% of LOD0's blocks > "
                         f"{limits['blotch_patch_max_pct']}%")
        if rd["hole_share_max_pct"] > limits["hole_share_max_pct"]:
            fails.append(f"see-through holes {rd['hole_share_max_pct']}% of LOD0's silhouette > "
                         f"{limits['hole_share_max_pct']}%")
    if lod["triangles"] > 1.1 * target:
        fails.append(f"triangles {lod['triangles']} > 1.1 x target {target}")
    if prev_tris is not None and lod["triangles"] >= prev_tris:
        fails.append(f"triangles {lod['triangles']} not below previous level {prev_tris}")
    want = [k for k in ("basecolor", "normal", "metallic_roughness", "occlusion")
            if any(p.get(k) for p in lod0["primitives"])]
    for i, p in enumerate(lod["primitives"]):
        if p.get("material") is None:
            fails.append(f"primitive {i} has no material")
            continue
        if "TEXCOORD_0" not in p["attributes"] or not p.get("uv_finite", False):
            fails.append(f"primitive {i} lacks finite TEXCOORD_0")
        for k in want:
            t = p.get(k)
            if not t or not t.get("embedded"):
                fails.append(f"primitive {i} lacks embedded {k} texture")
    lim = open_edge_allowance(lod0)
    if not impostor and lod["welded_boundary_length_m"] > lim:
        fails.append(f"open-edge length {lod['welded_boundary_length_m']} m > {round(lim, 3)} m "
                     f"(LOD0 {lod0['welded_boundary_length_m']} m)")
    if not impostor and lod.get("new_shard_components", 0) > 0:
        fails.append(f"{lod['new_shard_components']} new shards (components of <= {SHARD_TRIS} triangles "
                     f"broken off LOD0), {round(lod['new_shard_area_share'] * 100, 4)}% of area")
    if lod.get("bounds_delta_pct_of_diag", 0) > 2.0:
        fails.append(f"bounds moved {lod['bounds_delta_pct_of_diag']}% of diagonal")
    return fails


# ----------------------------------------------------------------------------- impostor route

# (normal the quad faces, up) in Blender world space: two vertical cross planes and one horizontal
# plane through the centre, each as two back-to-back quads (the game culls back faces)
IMPOSTOR_SIDES = (((0, -1, 0), (0, 0, 1)), ((0, 1, 0), (0, 0, 1)), ((1, 0, 0), (0, 0, 1)),
                  ((-1, 0, 0), (0, 0, 1)), ((0, 0, 1), (0, 1, 0)), ((0, 0, -1), (0, 1, 0)))


def _unpremultiply_fill(rgba, iters=16):
    """EXR renders are premultiplied: divide by alpha, then grow colour into the transparent texels
    so mipmaps and filtering do not pull a dark fringe into the cut-out edge."""
    a = rgba[..., 3]
    rgb = np.where(a[..., None] > 1e-4, rgba[..., :3] / np.maximum(a[..., None], 1e-4), 0.0)
    valid = a > 0.5
    for _ in range(iters):
        if valid.all() or not valid.any():
            break
        v = valid.astype(np.float32)
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape, np.float32)
        acc[1:] += rgb[:-1] * v[:-1, :, None]
        cnt[1:] += v[:-1]
        acc[:-1] += rgb[1:] * v[1:, :, None]
        cnt[:-1] += v[1:]
        acc[:, 1:] += rgb[:, :-1] * v[:, :-1, None]
        cnt[:, 1:] += v[:, :-1]
        acc[:, :-1] += rgb[:, 1:] * v[:, 1:, None]
        cnt[:, :-1] += v[:, 1:]
        grow = ~valid & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        valid |= grow
    if valid.any() and not valid.all():
        rgb[~valid] = rgb[valid].mean(0)
    return rgb, a


def impostor_lod(src, materials, name, size, tmpdir):
    """Route I (LOD3 of foliage only): a billboard impostor of 12 triangles.

    Two vertical cross planes and one horizontal plane through LOD0's centre, each two back-to-back
    quads. Every quad shows an orthographic EEVEE render of LOD0 taken along its normal (back faces
    culled): base colour with a cut-out alpha (glTF alphaMode MASK, cutoff 0.5), occlusion/roughness/
    metallic, and the view-space shading normal as its tangent-space normal map. The atlas is twice
    the level's texture edge (3 x 2 cells).
    """
    scene = bpy.context.scene
    P = np.array([v.co[:] for v in src.data.vertices])
    lo, hi = P.min(0), P.max(0)
    centre = (lo + hi) / 2
    diag = float(np.linalg.norm(hi - lo))
    atlas = 2 * size
    cols, rows = 3, 2
    cw, ch = atlas // cols, atlas // rows
    pad = 2
    tex = {r: np.zeros((atlas, atlas, 4), np.float32) for r in ("basecolor", "orm", "normal")}
    tex["normal"][..., :3] = (0.5, 0.5, 1.0)
    tex["orm"][..., :3] = (1.0, 1.0, 0.0)
    for r in tex:
        tex[r][..., 3] = 1.0
    tex["basecolor"][..., 3] = 0.0
    hidden = {o: o.hide_render for o in scene.objects}
    for o in scene.objects:
        o.hide_render = o is not src
    culling = {m: m.use_backface_culling for m in materials}
    for m in materials:
        m.use_backface_culling = True
    nmat = bpy.data.materials.new("_impostor_normal")
    nmat.use_nodes = True
    nmat.use_backface_culling = True
    nt = nmat.node_tree
    nt.nodes.clear()
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    vt = nt.nodes.new("ShaderNodeVectorTransform")
    vt.vector_type, vt.convert_from, vt.convert_to = "NORMAL", "WORLD", "CAMERA"
    mul = nt.nodes.new("ShaderNodeVectorMath")
    mul.operation = "MULTIPLY_ADD"
    # shader camera space has X right, Y up and +Z pointing away from the viewer (measured), so a
    # tangent-space normal facing the viewer is (x, y, -z)
    mul.inputs[1].default_value = (0.5, 0.5, -0.5)
    mul.inputs[2].default_value = (0.5, 0.5, 0.5)
    em = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(geo.outputs["Normal"], vt.inputs["Vector"])
    nt.links.new(vt.outputs["Vector"], mul.inputs[0])
    nt.links.new(mul.outputs["Vector"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    cam = bpy.data.objects.new("_impostor_cam", bpy.data.cameras.new("_impostor_cam"))
    scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.sensor_fit = "AUTO"
    vl = bpy.context.view_layer
    prev = (scene.render.engine, scene.camera, scene.view_settings.view_transform, scene.render.film_transparent,
            scene.render.resolution_x, scene.render.resolution_y, scene.render.filter_size,
            scene.render.image_settings.file_format, scene.render.image_settings.color_depth,
            scene.render.image_settings.color_mode, scene.render.filepath, vl.material_override)
    scene.render.engine = engine_eevee()
    scene.camera = cam
    scene.view_settings.view_transform = "Raw"
    scene.render.film_transparent = True
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "OPEN_EXR"
    scene.render.image_settings.color_depth = "32"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = os.path.join(tmpdir, "_impostor.exr")
    try:
        samples = scene.eevee.taa_render_samples
        scene.eevee.taa_render_samples = 16
    except AttributeError:
        samples = None
    verts, faces, uvs = [], [], []
    try:
        for k, (d, up_hint) in enumerate(IMPOSTOR_SIDES):
            d = np.array(d, float)
            _, right, up = look_rotation(-d)
            if abs(np.dot(up, up_hint)) < 0.5:
                raise RuntimeError("impostor camera frame")
            rel = P - centre
            r0, r1 = float((rel @ right).min()), float((rel @ right).max())
            u0, u1 = float((rel @ up).min()), float((rel @ up).max())
            w, h = max(r1 - r0, 1e-6), max(u1 - u0, 1e-6)
            s = max(w / (cw - 2 * pad), h / (ch - 2 * pad))
            rx, ry = max(int(math.ceil(w / s)), 1), max(int(math.ceil(h / s)), 1)
            rm, um = (r0 + r1) / 2, (u0 + u1) / 2
            W, H = rx * s, ry * s
            cam.data.ortho_scale = s * max(rx, ry)
            cam.data.clip_start = diag * 0.01
            cam.data.clip_end = diag * 4
            cam.location = Vector((centre + d * diag * 2 + right * rm + up * um).tolist())
            cam.rotation_euler = look_rotation(-d)[0]
            scene.render.resolution_x, scene.render.resolution_y = rx, ry
            cx, cy = (k % cols) * cw + pad, (k // cols) * ch + pad
            passes = {}
            for role in ("basecolor", "orm", "normal"):
                undo = []
                if role == "normal":
                    vl.material_override = nmat
                else:
                    undo = rewire_source(materials, role)
                try:
                    bpy.ops.render.render(write_still=True)
                finally:
                    vl.material_override = None
                    restore_source(undo)
                img = bpy.data.images.load(scene.render.filepath)
                px = np.empty(rx * ry * 4, np.float32)
                img.pixels.foreach_get(px)
                bpy.data.images.remove(img)
                passes[role] = px.reshape(ry, rx, 4)
            alpha = passes["basecolor"][..., 3]
            for role in ("basecolor", "orm", "normal"):
                rgb, _ = _unpremultiply_fill(passes[role])
                if role == "basecolor":
                    rgb = linear_to_srgb(rgb)
                elif role == "normal":
                    v = rgb * 2 - 1
                    v /= np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-6)
                    rgb = v * 0.5 + 0.5
                tex[role][cy:cy + ry, cx:cx + rx, :3] = rgb
            tex["basecolor"][cy:cy + ry, cx:cx + rx, 3] = alpha
            # quad: counter-clockwise seen from +d, spanning exactly what the render covers
            base = len(verts)
            for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                verts.append((centre + right * (rm + a * W / 2) + up * (um + b * H / 2)).tolist())
                uvs.append(((cx + (a + 1) / 2 * rx) / atlas, (cy + (b + 1) / 2 * ry) / atlas))
            faces += [(base, base + 1, base + 2), (base, base + 2, base + 3)]
    finally:
        bpy.data.objects.remove(cam, do_unlink=True)
        bpy.data.materials.remove(nmat)
        for m, c in culling.items():
            m.use_backface_culling = c
        for o, hdn in hidden.items():
            o.hide_render = hdn
        (scene.render.engine, scene.camera, scene.view_settings.view_transform, scene.render.film_transparent,
         scene.render.resolution_x, scene.render.resolution_y, scene.render.filter_size,
         scene.render.image_settings.file_format, scene.render.image_settings.color_depth,
         scene.render.image_settings.color_mode, scene.render.filepath, vl.material_override) = prev
        if samples is not None:
            scene.eevee.taa_render_samples = samples
        try:
            os.remove(os.path.join(tmpdir, "_impostor.exr"))
        except OSError:
            pass
    me = bpy.data.meshes.new(f"{name}_mesh")
    me.from_pydata(verts, [], faces)
    uvl = me.uv_layers.new(name="UVMap")
    loop_uv = np.array([uvs[v] for f in faces for v in f], np.float32)
    uvl.data.foreach_set("uv", loop_uv.reshape(-1))
    me.update()
    lod = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(lod)
    mat, imgs = baked_material(name, atlas)
    bc = bpy.data.images.new(f"{name}_basecolor_a", atlas, atlas, alpha=True)
    bc.colorspace_settings.name = "sRGB"
    bc.alpha_mode = "STRAIGHT"
    bpy.data.images.remove(imgs["basecolor"][0])
    imgs["basecolor"] = (bc, imgs["basecolor"][1])
    imgs["basecolor"][1].image = bc
    for role, (img, _) in imgs.items():
        img.pixels.foreach_set(tex[role].reshape(-1))
        img.update()
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    clip = nt.nodes.new("ShaderNodeMath")
    clip.operation = "ROUND"  # the glTF exporter reads Alpha -> Round as alphaMode MASK, cutoff 0.5
    nt.links.new(imgs["basecolor"][1].outputs["Alpha"], clip.inputs[0])
    nt.links.new(clip.outputs["Value"], bsdf.inputs["Alpha"])
    me.materials.append(mat)
    return lod, {"impostor": {"sides": len(IMPOSTOR_SIDES), "atlas_px": atlas, "cell_px": [cw, ch],
                              "alpha_mode": "MASK 0.5"}}


# ----------------------------------------------------------------------------- render

def engine_eevee():
    items = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    return "BLENDER_EEVEE" if "BLENDER_EEVEE" in items else "BLENDER_EEVEE_NEXT"


def render_panels(entries, ref_lo, ref_hi, out_path, panel, title=""):
    """Render every GLB with one fixed camera per view (derived from LOD0 bounds) and tile them.

    Rows are views (front three-quarter from glTF +Z, back three-quarter, low three-quarter from
    below), columns are entries. The background is a flat pink so holes and cracks read clearly.
    """
    # bounds arrive in glTF space (Y up); the imported scene is Blender space (x, -z, y)
    b_lo = np.array([ref_lo[0], -ref_hi[2], ref_lo[1]])
    b_hi = np.array([ref_hi[0], -ref_lo[2], ref_hi[1]])
    centre = (b_lo + b_hi) / 2
    radius = float(np.linalg.norm(b_hi - b_lo)) / 2
    views = [(-35.0, 18.0), (145.0, 32.0), (40.0, -25.0)]
    lens = 50.0
    half_fov = math.atan(18.0 / lens)
    dist = radius / math.sin(half_fov) * 1.04
    cols, rows = len(entries), len(views)
    canvas = np.zeros((rows * panel, cols * panel, 4), np.float32)
    bg = np.array([0.93, 0.72, 0.86, 1.0], np.float32)
    tmp = out_path + ".panel.png"
    for c, (path, label) in enumerate(entries):
        for r, (az, el) in enumerate(views):
            reset_scene()
            import_glb(path)
            sc = bpy.context.scene
            cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
            sc.collection.objects.link(cam)
            cam.data.lens = lens
            cam.data.sensor_fit = "HORIZONTAL"
            cam.data.sensor_width = 36.0
            cam.data.clip_start = dist * 0.01
            cam.data.clip_end = dist * 10
            # Blender -Y is glTF +Z (model forward); azimuth 0 looks at the front.
            a, e = math.radians(az), math.radians(el)
            view = np.array([math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)])
            # fit LOD0's eight bounding-box corners into the frame (same distance for every LOD)
            fwd = -view
            right = np.cross(fwd, [0.0, 0.0, 1.0])
            right /= np.linalg.norm(right)
            up = np.cross(right, fwd)
            corners = np.array([[x, y, z] for x in (b_lo[0], b_hi[0]) for y in (b_lo[1], b_hi[1])
                                for z in (b_lo[2], b_hi[2])]) - centre
            t = math.tan(half_fov) * 0.92
            need = max(max(abs(c @ right), abs(c @ up)) / t - c @ fwd for c in corners)
            dist = max(need, radius * 0.5)
            cam.data.clip_start = dist * 0.01
            cam.data.clip_end = dist * 10
            off = Vector((view * dist).tolist())
            cam.location = Vector(centre.tolist()) + off
            cam.rotation_euler = (Vector(centre.tolist()) - cam.location).to_track_quat("-Z", "Y").to_euler()
            sc.camera = cam
            sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
            sc.collection.objects.link(sun)
            sun.data.energy = 3.0
            sun.rotation_euler = (math.radians(50), 0, math.radians(az - 30))
            world = bpy.data.worlds.new("w")
            world.use_nodes = True
            world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.55, 0.58, 1)
            world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
            sc.world = world
            sc.render.engine = engine_eevee()
            sc.render.film_transparent = True
            sc.render.resolution_x = sc.render.resolution_y = panel
            sc.render.resolution_percentage = 100
            sc.view_settings.view_transform = "Standard"
            try:
                sc.eevee.taa_render_samples = 16
            except AttributeError:
                pass
            for m in bpy.data.materials:
                m.use_backface_culling = True
            txt = bpy.data.curves.new("label", "FONT")
            txt.body = f"{label}{('  ' + title) if title else ''}"
            txt.size = 0.028
            tob = bpy.data.objects.new("label", txt)
            sc.collection.objects.link(tob)
            tob.parent = cam
            zt = cam.data.clip_start * 4
            txt.size = 0.028 * zt
            tob.location = (-0.35 * zt, 0.325 * zt, -zt)
            tm = bpy.data.materials.new("label")
            tm.use_nodes = True
            tm.node_tree.nodes.clear()
            em = tm.node_tree.nodes.new("ShaderNodeEmission")
            em.inputs["Color"].default_value = (0.02, 0.02, 0.02, 1)
            mo = tm.node_tree.nodes.new("ShaderNodeOutputMaterial")
            tm.node_tree.links.new(em.outputs[0], mo.inputs["Surface"])
            txt.materials.append(tm)
            sc.render.image_settings.file_format = "PNG"
            sc.render.image_settings.color_mode = "RGBA"
            sc.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(tmp)
            px = np.empty(panel * panel * 4, np.float32)
            img.pixels.foreach_get(px)
            px = px.reshape(panel, panel, 4)
            alpha = px[..., 3:4]
            comp = px * alpha + bg * (1 - alpha)
            comp[..., 3] = 1.0
            y0 = (rows - 1 - r) * panel
            canvas[y0:y0 + panel, c * panel:(c + 1) * panel] = comp
            bpy.data.images.remove(img)
    try:
        os.remove(tmp)
    except OSError:
        pass
    out = bpy.data.images.new("compare", cols * panel, rows * panel, alpha=False)
    out.pixels.foreach_set(canvas.reshape(-1))
    out.filepath_raw = out_path
    out.file_format = "JPEG"
    sc = bpy.context.scene
    sc.render.image_settings.file_format = "JPEG"
    sc.render.image_settings.quality = 90
    out.save_render(out_path, scene=sc)
    return out_path


# ----------------------------------------------------------------------------- main

def export_lod(obj, path, image_format, quality):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=True,
        export_yup=True, export_normals=True, export_texcoords=True, export_tangents=False,
        export_materials="EXPORT", export_image_format=image_format, export_image_quality=quality,
        export_draco_mesh_compression_enable=False, export_extras=False,
        export_animations=False, export_skins=False, export_morph=False)


def subset_object(name, arrays, keep, materials):
    """LOD0's faces in keep as they are (positions, UVs, normals, materials), as a new object."""
    sub = subset_faces(arrays, keep)
    corners = np.arange(len(sub["T"]) * 3).reshape(-1, 3)
    wedges = {"wr": sub["T"].reshape(-1), "wuv": sub["UV"], "wn": sub["N"]}
    return build_object(name, corners, sub["FM"], wedges, sub["P"], materials)


def export_subset(name, arrays, keep, materials, path, image_format, quality):
    """Export LOD0's faces in keep as they are."""
    ob = subset_object(name, arrays, keep, materials)
    export_lod(ob, path, image_format, quality)
    bpy.data.objects.remove(ob, do_unlink=True)


def main():
    global NORMAL_RADIUS
    args = parse_args()
    NORMAL_RADIUS = args.normal_radius
    t_start = time.time()
    src_path = os.path.abspath(args.input)
    asset_id = args.id or os.path.splitext(os.path.basename(src_path))[0]
    outdir = os.path.abspath(args.outdir)
    os.makedirs(outdir, exist_ok=True)
    if os.path.normcase(os.path.dirname(src_path)) == os.path.normcase(outdir) and not args.render_only:
        raise SystemExit("refusing to write LODs next to the LOD0 they are built from; use a staging dir")
    meta_path = args.meta or os.path.join(os.path.dirname(src_path), f"{asset_id}_meta.json")
    if args.budgets:
        budgets, budget_src = [int(v) for v in args.budgets.split(",")], "cli"
    else:
        budgets, budget_src = read_budgets(meta_path, asset_id)
    n = len(budgets)
    category = None
    if os.path.exists(meta_path):
        with open(meta_path, encoding="utf-8") as handle:
            category = json.load(handle).get("category")
    routes = per_level(args.route.upper(), n)
    cascades = [r.split("/") for r in routes]
    for c in cascades:
        if not c or any(r not in ("A", "B", "C", "R", "T", "I") for r in c):
            raise SystemExit(f"bad route {args.route!r}: use A, B, C, R, T, I or a chain like C/B/R/T")
    tex_sizes = per_level(args.tex_sizes, n, int)
    gate_match = per_level(args.gate_silhouette_match, n, float)
    gate_colour = per_level(args.gate_colour, n, float)
    gate_holes = per_level(args.gate_holes, n, float)
    gate_patch = per_level(args.gate_blotch_patch, n, float)
    limits = {"silhouette_match_min": gate_match, "colour_error_max": gate_colour, "hole_share_max_pct": gate_holes,
              "blotch_patch_max_pct": gate_patch}
    level_limits = lambda i: {k: v[i - 1] for k, v in limits.items()}
    logs = []

    def log(msg):
        logs.append(msg)
        print("[rebuild_lods] " + msg, flush=True)

    lod0_m, (P0, T0, lo0, hi0) = measure_glb(src_path)
    diag = float(np.linalg.norm(hi0 - lo0))
    ref = {"P": P0, "T": T0, "lo": lo0, "hi": hi0}
    with open(src_path, "rb") as handle:
        src_sha1 = hashlib.sha1(handle.read()).hexdigest()
    limits_report = dict(limits, open_edge_length_max_m=round(open_edge_allowance(lod0_m), 4),
                         new_shard_components_max=0, render_views=[v[0] for v in RenderProbe.VIEWS],
                         render_px=args.probe_size)
    report = {"asset_id": asset_id, "input": src_path, "input_sha1": src_sha1, "outdir": outdir, "budgets": budgets,
              "budget_source": budget_src, "category": category, "routes": routes, "tex_sizes": tex_sizes,
              "image_format": args.image_format, "blender": bpy.app.version_string,
              "gate_limits": limits_report, "lod0": lod0_m, "lods": {}}
    files = {}
    probe = None if args.no_probe else RenderProbe(lo0, hi0, args.probe_size, outdir)
    ref_views = []
    probe_views = {}

    def probe_metrics(path):
        if probe is None:
            return None
        if not ref_views:
            ref_views.extend(probe.render(src_path))
        views = probe.render(path)
        probe_views[path] = views
        return render_delta(ref_views, views)

    reset_scene()
    if args.render_only:
        lod_dir = os.path.abspath(args.lod_dir or outdir)
        prev = lod0_m["triangles"]
        for i in range(1, n + 1):
            path = os.path.join(lod_dir, f"{asset_id}_lod{i}.glb")
            if not os.path.exists(path):
                continue
            m, _ = measure_glb(path, ref)
            m["render"] = probe_metrics(path)
            m["gate_failures"] = gate(m, lod0_m, budgets[i - 1], prev, level_limits(i),
                                      impostor=any(p.get("alpha_mode") == "MASK" for p in m["primitives"]))
            prev = m["triangles"]
            report["lods"][f"lod{i}"] = m
            files[i] = path
            if path in probe_views:
                probe_views[i] = probe_views.pop(path)
        report["mode"] = "render_only"
        report["lod_dir"] = lod_dir
    else:
        meshes = import_glb(src_path)
        if not meshes:
            raise SystemExit(f"no mesh in {src_path}")
        src = joined_mesh(meshes, f"{asset_id}_lod0src")
        materials = [m for m in src.data.materials if m]
        arrays = mesh_arrays(src)
        n_src = len(arrays["T"])
        report["source_faces"] = n_src
        need_c = any("C" in c for c in cascades)
        need_b = any("B" in c for c in cascades) and args.b_decimator == "qem"
        need_r = any(("R" in c) or ("T" in c) for c in cascades)

        # enclosed inner layers are dropped before simplifying (routes B and C), only when that is
        # provably safe; any doubt keeps every face
        arrays_v = arrays
        # what baked routes (B, R, T) bake from and take shading normals from: LOD0 without its
        # enclosed inner layers even when those stay in the mesh being simplified, so a bake ray or a
        # normal lookup cannot land on an inner skin (door: light and grey blotches when it did)
        arrays_bake, bake_src = arrays, src
        if (need_b or need_c) and not args.keep_hidden and probe is None:
            report["hidden"] = {"accepted": False, "reason": "--no-probe: the drop cannot be checked, every face kept"}
        elif (need_b or need_c) and not args.keep_hidden:
            t0 = time.time()
            removed, hstats = hidden_faces(src, arrays, diag, outdir, args, src_sha1,
                                           max_open_edge=open_edge_allowance(lod0_m))
            hstats["accepted"] = False
            keep = ~removed
            if not removed.any():
                hstats["reason"] = "no enclosed inner layer found"
            else:
                # the pruned LOD0 must open no more edge than the crack gate allows and must render
                # exactly like LOD0 (a dropped face that can be seen shows as a changed pixel)
                hstats["pruned_open_edge_m"] = weld_topology(arrays["P"], arrays["T"][keep])["welded_boundary_length_m"]
                hstats["open_edge_allowance_m"] = round(open_edge_allowance(lod0_m), 4)
                if hstats["pruned_open_edge_m"] > hstats["open_edge_allowance_m"]:
                    hstats["reason"] = (f"dropping them opens {hstats['pruned_open_edge_m']} m of edges, more than "
                                        f"the {hstats['open_edge_allowance_m']} m the crack gate allows; every face kept")
                else:
                    check = os.path.join(outdir, f"{asset_id}_pruned_check.glb")
                    export_subset(f"{asset_id}_pruned", arrays, keep, downscaled_materials(materials, 1 << 16, "pruned"),
                                  check, args.image_format, args.image_quality)
                    if not ref_views:
                        ref_views.extend(probe.render(src_path))
                    change = [pixel_change_pct(a, b) for a, b in zip(ref_views, probe.render(check))]
                    os.remove(check)
                    hstats["pruned_pixel_change_pct_views"] = [round(v, 4) for v in change]
                    if max(change) > args.prune_change_max:
                        hstats["reason"] = (f"the pruned LOD0 changes {round(max(change), 4)}% of LOD0's pixels "
                                            f"(> {args.prune_change_max}%); every face kept")
                    else:
                        hstats["accepted"] = True
                        hstats["reason"] = "enclosed, never seen, renders like LOD0"
            hstats["seconds"] = round(time.time() - t0, 2)
            report["hidden"] = hstats
            log(f"hidden faces: {int(removed.sum())} of {len(removed)} droppable; "
                f"{'dropped' if hstats['accepted'] else 'kept'} ({hstats['reason']})")
            if removed.any():
                arrays_bake = subset_faces(arrays, keep)
                bake_src = subset_object(f"{asset_id}_bakesrc", arrays, keep, materials)
                hstats["bake_source"] = ("LOD0 without the droppable faces" +
                                         (" (ray test only, renders skipped)" if hstats.get("renders_skipped") else ""))
                # the same faces in glTF order, so LOD0->LOD deviation samples only what can be seen
                cb = arrays["P"][arrays["T"]].mean(1)
                cg = P0[T0].mean(1)
                if len(cg) == len(cb) and np.allclose(np.stack([cb[:, 0], cb[:, 2], -cb[:, 1]], 1), cg, atol=1e-5):
                    ref["Tvis"] = T0[keep]
                    report["deviation_samples"] = "LOD0 without its enclosed inner layers"
                else:
                    report["deviation_samples"] = "all LOD0 faces (face order differs from import)"
            if hstats["accepted"]:
                arrays_v = arrays_bake
        # cap each level 2% below the level above (and below LOD0's visible faces) so the chain
        # descends even when a budget exceeds what is there (weaponcomp_haft_short_a: 2188/2187/600)
        targets, prev = [], min(n_src, len(arrays_v["T"]))
        for b in budgets:
            t = min(int(b), int(prev * 0.98))
            targets.append(max(t, 4))
            prev = targets[-1]
        report["targets"] = targets
        snaps_c = snaps_b = wedges_c = wedges_b = wedges_r = PS_v = PS_f = None
        if need_b or need_c:
            t0 = time.time()
            sid_v, PS_v, sstats = stitch_ids(arrays_v["P"], arrays_v["T"])
            sstats["seconds"] = round(time.time() - t0, 2)
            report["stitch"] = sstats
            log(f"stitched {len(arrays_v['T'])} faces: {sstats['stitched_vertices']} vertices")
        if need_c:
            t0 = time.time()
            wedges_c = build_wedges(sid_v, arrays_v, keep_attributes=True)
            lvl = [targets[i] for i in range(n) if "C" in cascades[i]]
            snaps_c, st = simplify(wedges_c["TW"], arrays_v["FM"], wedges_c["wr"], PS_v, lvl)
            st["seconds"] = round(time.time() - t0, 2)
            report["simplify_C"] = st
        if need_b:
            t0 = time.time()
            wedges_b = build_wedges(sid_v, arrays_v, keep_attributes=False)
            lvl = [targets[i] for i in range(n) if "B" in cascades[i]]
            snaps_b, st = simplify(wedges_b["TW"], arrays_v["FM"], wedges_b["wr"], PS_v, lvl)
            st["seconds"] = round(time.time() - t0, 2)
            report["simplify_B"] = st
        if need_r:
            sid_f, PS_f, _ = stitch_ids(arrays["P"], arrays["T"])
            wedges_r = build_wedges(sid_f, arrays, keep_attributes=False)
        tri = arrays["P"][arrays["T"]]
        area = float(np.linalg.norm(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]), axis=1).sum() / 2)
        device_used = None
        normal_tree = []
        # how far LOD0's NORMAL data sits from its own surface decides how baked LODs are shaded
        vis = arrays_bake
        tv = vis["P"][vis["T"]]
        fnrm = np.cross(tv[:, 1] - tv[:, 0], tv[:, 2] - tv[:, 0])
        farea = np.linalg.norm(fnrm, axis=1)
        fnrm /= np.maximum(farea, 1e-30)[:, None]
        cn = vis["N"].reshape(-1, 3, 3).mean(1)
        cn /= np.maximum(np.linalg.norm(cn, axis=1), 1e-30)[:, None]
        off60 = float(farea[(fnrm * cn).sum(1) < 0.5].sum() / max(farea.sum(), 1e-30))
        report["lod0_normals_over_60deg_area_share"] = round(off60, 4)
        normals_mode = args.normals if args.normals != "auto" else ("lod0" if off60 > 0.1 else "geometry")
        report["baked_normals_mode"] = normals_mode

        ref_parts_b = []

        def drop_shards(P, T, info):
            """New shards (pieces of <= SHARD_TRIS triangles broken off LOD0) are dropped from every
            candidate before UVs, bake and export; the report counts them."""
            if not ref_parts_b:
                ref_parts_b.append(weld_topology(arrays["P"], arrays["T"], parts=True)[1])
            drop, st = new_shard_faces(P, T, ref_parts_b[0], diag)
            info.update(st)
            return ~drop

        def reach(route, target):
            """Triangles a precomputed simplification delivers for this target (None if not precomputed)."""
            snaps = snaps_c if route == "C" else snaps_b if (route == "B" and need_b) else None
            return len(snaps[target][0]) if snaps is not None else None

        def build(route, name, target, size, info):
            nonlocal device_used
            if route == "I":
                lod, st = impostor_lod(src, materials, name, size, outdir)
                info.update(st)
                return lod
            if route == "A":
                lod = src.copy()
                lod.data = src.data.copy()
                bpy.context.collection.objects.link(lod)
                cur = len(lod.data.polygons)
                if target < cur:
                    mod = lod.modifiers.new("LODDecimate", "DECIMATE")
                    mod.decimate_type = "COLLAPSE"
                    mod.ratio = max(target / cur, 0.0001)
                    bpy.context.view_layer.objects.active = lod
                    bpy.ops.object.modifier_apply(modifier=mod.name)
                mats = downscaled_materials(materials, size, name)
                for k, m in enumerate(mats):
                    lod.data.materials[k] = m
                return lod
            if route == "C":
                TW, FM = snaps_c[target]
                keep = drop_shards(PS_v, wedges_c["wr"][TW], info)
                return build_object(name, TW[keep], FM[keep], wedges_c, PS_v,
                                    downscaled_materials(materials, size, name))
            if route in ("R", "T"):
                lod = build_object(name, wedges_r["TW"], arrays["FM"], wedges_r, PS_f, [], with_uv=False,
                                   custom_normals=False)
                thicken = args.r_thicken * (2.0 if route == "T" else 1.0)
                info["remesh"] = remesh_lod(lod, target, area, diag, thicken=thicken,
                                            voxel_scale=args.r_voxel_scale)
                Pr, Tr = simple_arrays(lod)
                keep = drop_shards(Pr, Tr, info)
                if not keep.all():
                    used = np.unique(Tr[keep])
                    remap = np.full(len(Pr), -1)
                    remap[used] = np.arange(len(used))
                    replace_mesh(lod, Pr[used], remap[Tr[keep]])
            elif need_b:
                TW, FM = snaps_b[target]
                keep = drop_shards(PS_v, wedges_b["wr"][TW], info)
                lod = build_object(name, TW[keep], FM[keep], wedges_b, PS_v, [], with_uv=False, custom_normals=False)
            else:
                # route B exactly as first specified: weld by position, Blender COLLAPSE without UVs
                lod = src.copy()
                lod.data = src.data.copy()
                bpy.context.collection.objects.link(lod)
                bm = bmesh.new()
                bm.from_mesh(lod.data)
                bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
                bm.to_mesh(lod.data)
                bm.free()
                while lod.data.uv_layers:
                    lod.data.uv_layers.remove(lod.data.uv_layers[0])
                lod.data.materials.clear()
                cur = len(lod.data.polygons)
                mod = lod.modifiers.new("LODDecimate", "DECIMATE")
                mod.decimate_type = "COLLAPSE"
                mod.ratio = max(target / cur, 0.0001)
                bpy.context.view_layer.objects.active = lod
                bpy.ops.object.modifier_apply(modifier=mod.name)
                lod.data.shade_smooth()
            if device_used is None:
                device_used = setup_cycles(args.device)
                report["bake_device"] = device_used
            bpy.context.scene.render.engine = "CYCLES"
            info["uv_fill_stretch"] = smart_uv(lod, size)
            # cage from the measured separation between this LOD and LOD0
            Pl, Tl = simple_arrays(lod)
            dv = deviation(arrays["P"], arrays["T"], arrays_bake["T"], Pl, Tl, diag)
            sep = max(dv["lod_to_lod0_pct_p50_p95_p99_max"][2], dv["lod0_to_lod_pct_p50_p95_p99_max"][2])
            extrusion = min(max(sep * 1.5, 0.3), 5.0) / 100 * diag
            info["bake_extrusion_m"] = round(extrusion, 5)
            cage = make_cage(lod, extrusion)
            normal_mode = "bake"
            if normals_mode == "lod0":
                # LOD0's shading normals disagree with its surface: carry them over as vertex
                # normals and copy LOD0's tangent-space texels (a bake would re-express them
                # against the LOD's geometry and change the look)
                if not normal_tree:
                    normal_tree.append(bvh_from(arrays_bake["P"], arrays_bake["T"]))
                info["normals"] = transfer_normals(lod, arrays_bake["P"], arrays_bake["T"], arrays_bake["N"],
                                                   normal_tree[0])
                normal_mode = args.lod0_normal_map
            info["normal_map"] = normal_mode
            try:
                try:
                    info["bake"] = bake_onto(bake_src, lod, name, size, args.bake_samples, extrusion,
                                             extrusion * 2.5, cage=cage, normal_mode=normal_mode)
                except RuntimeError as exc:
                    # the GPU is shared with other jobs; a CUDA context or memory failure bakes on the CPU
                    if bpy.context.scene.cycles.device != "GPU" or not any(
                            k in str(exc) for k in ("CUDA", "OptiX", "OPTIX", "GPU", "memory")):
                        raise
                    log(f"GPU bake failed ({str(exc).strip()[:120]}); baking on the CPU from here on")
                    bpy.context.scene.cycles.device = "CPU"
                    report["bake_device"] = f"CPU (GPU failed: {str(exc).strip()[:120]})"
                    info["bake_device_fallback"] = "CPU"
                    info["bake"] = bake_onto(bake_src, lod, name, size, args.bake_samples, extrusion,
                                             extrusion * 2.5, cage=cage, normal_mode=normal_mode)
            finally:
                bpy.data.objects.remove(cage, do_unlink=True)
            return lod

        prev_tris = lod0_m["triangles"]
        for i in range(1, n + 1):
            cascade, target, size = cascades[i - 1], targets[i - 1], tex_sizes[i - 1]
            name = f"{asset_id}_lod{i}"
            path = os.path.join(outdir, f"{name}.glb")
            candidates, skipped = [], []
            level_routes = list(cascade)
            impostor_ok = i == n and (args.impostor == "always" or (args.impostor == "auto" and category == "flora"))
            if impostor_ok and args.impostor == "always" and "I" not in level_routes:
                level_routes.append("I")

            def add_impostor(k):
                """Foliage: the impostor joins the last level only when no other route passed."""
                if (impostor_ok and k == len(level_routes) - 1 and "I" not in level_routes
                        and not any(not c["gate_failures"] for c in candidates)):
                    level_routes.append("I")

            for k, route in enumerate(level_routes):
                last = k == len(level_routes) - 1
                r = reach(route, target)
                if r is not None and r > 1.1 * target and not (last and not candidates):
                    skipped.append({"route": route, "skipped": f"simplifier reached {r} tris"})
                    add_impostor(k)
                    continue
                t0 = time.time()
                info = {"route": route, "target": target, "requested": budgets[i - 1], "tex_size": size}
                lod = build(route, name, target, size, info)
                lod.name = name
                lod.data.name = f"{name}_mesh"
                info["build_faces"] = len(lod.data.polygons)
                cand_path = os.path.join(outdir, f"{name}.{route}.candidate.glb")
                # the impostor's cut-out alpha needs a format with alpha
                export_lod(lod, cand_path, "AUTO" if route == "I" and args.image_format == "JPEG" else args.image_format,
                           args.image_quality)
                info["seconds"] = round(time.time() - t0, 2)
                bpy.data.objects.remove(lod, do_unlink=True)
                m, _ = measure_glb(cand_path, ref)
                info.update(m)
                t1 = time.time()
                info["render"] = probe_metrics(cand_path)
                info["probe_seconds"] = round(time.time() - t1, 2)
                info["gate_failures"] = gate(m | {"render": info["render"]}, lod0_m, target, prev_tris, level_limits(i),
                                             impostor=route == "I")
                rd = info["render"] or {}
                info["score"] = (round(rd["colour_error_mean"] + 0.5 * (1.0 - rd["silhouette_match_min"]), 5)
                                 if rd else float(k))
                info["_path"] = cand_path
                info["_views"] = probe_views.pop(cand_path, None)
                log(f"lod{i} route {route}: {m['triangles']} tris (target {target}), "
                    f"open edges {m['welded_boundary_length_m']} m, shards dropped {info.get('dropped_new_shards')}, "
                    f"detached {m.get('detached_components')}, holes {rd.get('hole_share_max_pct')}, "
                    f"match {rd.get('silhouette_match_min')}, colour {rd.get('colour_error_mean')}, "
                    f"score {info['score']}, {m['file_bytes'] / 1e6:.2f} MB, {info['seconds']} s, "
                    f"fails={info['gate_failures']}")
                candidates.append(info)
                if args.select == "first" and not info["gate_failures"]:
                    break
                add_impostor(k)
            # lowest render score among candidates that pass every gate, else among all (an impostor
            # only when it passes: failing, it is a different kind of object, not a lesser LOD)
            pool = ([c for c in candidates if not c["gate_failures"]]
                    or [c for c in candidates if c["route"] != "I"] or candidates)
            chosen = min(pool, key=lambda c: c["score"])
            for c in candidates:
                cp = c.pop("_path")
                views = c.pop("_views")
                if c is chosen:
                    os.replace(cp, path)
                    probe_views[i] = views
                else:
                    os.remove(cp)
            chosen["cascade"] = "/".join(level_routes)
            chosen["selection"] = args.select
            chosen["rejected"] = skipped + [
                {"route": c["route"], "triangles": c["triangles"], "score": c["score"], "render": c["render"],
                 "gate_failures": c["gate_failures"], "file_bytes": c["file_bytes"], "seconds": c["seconds"]}
                for c in candidates if c is not chosen]
            prev_tris = chosen["triangles"]
            report["lods"][f"lod{i}"] = chosen
            files[i] = path
            log(f"lod{i}: chose route {chosen['route']} (score {chosen['score']})")

    report["passed"] = all(not v["gate_failures"] for v in report["lods"].values()) and len(report["lods"]) == n
    if args.render:
        entries = [(src_path, f"LOD0  {lod0_m['triangles']} tris")]
        for i in sorted(files):
            m = report["lods"][f"lod{i}"]
            route = m.get("route", "existing")
            tex = max((im.get("dims", [0])[0] for im in m["images"]), default=0)
            entries.append((files[i], f"LOD{i}  {route}  {m['triangles']} tris  tex {tex}"))
        name = args.compare_name or f"{asset_id}_lod_compare.jpg"
        t0 = time.time()
        report["compare_image"] = render_panels(entries, lo0, hi0, os.path.join(outdir, name), args.panel,
                                                args.title)
        report["render_seconds"] = round(time.time() - t0, 2)
    if ref_views:
        rows = [(ref_views, None)] + [(probe_views.get(i), ref_views) for i in sorted(files)]
        report["holes_image"] = holes_sheet(rows, os.path.join(outdir, f"{asset_id}_lod_holes.jpg"))
    try:
        os.remove(os.path.join(outdir, "_probe_view.png"))
    except OSError:
        pass
    report["seconds_total"] = round(time.time() - t_start, 2)
    report["log"] = logs
    rep_name = f"{asset_id}_lod_report.json" if not args.render_only else f"{asset_id}_lod_report_existing.json"
    with open(os.path.join(outdir, rep_name), "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print("REBUILD_LODS_RESULT " + json.dumps({"asset_id": asset_id, "passed": report["passed"],
                                               "report": os.path.join(outdir, rep_name)}), flush=True)


if __name__ == "__main__":
    main()
