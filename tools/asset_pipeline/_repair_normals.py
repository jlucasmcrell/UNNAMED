"""Find and repair wrong NORMAL data in LOD0 GLBs, touching only the corners that are wrong.

Plain Python + numpy on the GLB bytes (no Blender, no re-export). Nothing but normals changes: every
triangle keeps its corner positions, UVs and material, and the embedded images keep their bytes.

What counts as wrong. Positions are welded (quantised to 1e-6 of the model's diagonal); for every
triangle corner three reference directions describe the local surface:
  face    the corner's own face normal (from winding; what back-face culling shows)
  group   the corner-angle-weighted normal of the faces around the welded vertex whose normal is within
          --feature-angle of the corner's face (the smooth group the corner belongs to)
  vertex  the area-weighted normal of every face around the welded vertex, used only where those faces
          do not cancel (its length is over --cancel-ratio of their summed area). Double-sided sheets
          cancel it: the generated LOD0s store exactly this cancelled vector on thin sheets, where it
          points up to 180 deg away from the face it shades (prop_canvas_haversack: 26% of the area)
A corner is BAD when its stored normal is more than --threshold degrees from every usable reference.
Flat shading (stored = face), smooth shading (stored = vertex or group) and hard creases all pass;
normals that point along the surface, into it, or in an unrelated direction do not.

What a bad corner gets.
  1. When one axis permutation (any of the 48 signed 3x3 permutations) explains the file - the bad
     share falls by at least ROTATION_GAIN and to under a third - the positions were re-oriented
     without the normals, and every vertex whose permuted normal passes takes it: that is the normal
     that was authored (prop_iron_banded_oak_door: x,y,z stored as z,x,y).
  2. Otherwise it gets its group normal (area-weighted mean of the group normals of the vertex's bad
     corners that lie in one smooth group): smooth within the feature angle, sharp across creases.
     A vertex whose corners are all bad and in one group is rewritten in place. A vertex that also has
     good corners, or whose bad corners lie in several groups (a crease or the two sides of a sheet
     through one vertex), keeps its stored normal for its good corners and gets one new vertex per
     group of bad corners (all other attributes copied), unless --no-split.
A file needing no new vertex is rewritten in place of its NORMAL bytes only (JSON and every other byte
identical). With new vertices, the vertex accessors grow, indices are rewritten (uint32 if needed) and
buffer views are repacked in order; the JSON otherwise stays equal and the tool checks per-corner
POSITION/TEXCOORD, materials and image bytes against the source after writing.

--floor: repair leaves an asset whose bad-area share is under this (default --min-share, 0.10, the
'affected' line) byte-identical. container_barrel_oak has 2.9% flagged (cancelled vertex normals on its
rim and hoop sheets) and is left alone; recomputing every normal of it would move them a median 20 deg
(90th percentile 144 deg) from what it has.

Tangents: no LOD0 in the library carries a TANGENT attribute, so a loader generates them from the
normals and UVs (glTF 2.0: MikkTSpace when tangents are absent) and they follow the repair.

Usage (system Python):
  python _repair_normals.py survey [--ids a,b | --all] [--out <json>]
  python _repair_normals.py repair --ids a,b | --affected [--min-share 0.10] [--staging-root <dir>]
  python _repair_normals.py check <file.glb>...      (measure any GLB, e.g. a staged result)
Options: --threshold 60 --feature-angle 60 --cancel-ratio 0.2 --floor <share> --no-split --no-permutation
--permutation-scope all|bad --ready-root <assets/ready>. repair writes <staging-root>/<id>/<id>.glb and
<id>_normals_report.json plus a run summary; it refuses to write under ready/.
"""
import argparse
import datetime
import hashlib
import itertools
import json
import os
import struct
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "assets"))
READY = os.path.join(ASSETS, "ready")
STAGING = os.path.join(ASSETS, "_staging", "normals")

THRESHOLD_DEG = 60.0
FEATURE_DEG = 60.0  # generated LOD0s are smooth-shaded; 60 matches them better than 45 and keeps 90 deg creases
AFFECTED_SHARE = 0.10
CANCEL_RATIO = 0.2  # vertex reference only where faces do not cancel (see analyse)
ROTATION_GAIN = 0.10  # a permutation must cut the bad-area share by at least this much (absolute)

COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def signed_permutations():
    out = []
    for p in itertools.permutations(range(3)):
        for s in itertools.product((1.0, -1.0), repeat=3):
            m = np.zeros((3, 3))
            for i in range(3):
                m[i, p[i]] = s[i]
            out.append(m)
    return out  # out[0] is the identity


PERMS = signed_permutations()


# ----------------------------------------------------------------------------- GLB io

def read_glb(path):
    data = open(path, "rb").read()
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67 or version != 2:
        raise ValueError(f"{path}: not a glTF 2.0 binary")
    chunks, off = [], 12
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunks.append((ctype, off + 8, clen))
        off += 8 + clen
    js_chunk = next(c for c in chunks if c[0] == 0x4E4F534A)
    bin_chunk = next((c for c in chunks if c[0] == 0x004E4942), None)
    js = json.loads(data[js_chunk[1]:js_chunk[1] + js_chunk[2]])
    return data, js, bin_chunk


def accessor(data, js, bin_chunk, index):
    a = js["accessors"][index]
    if "sparse" in a or "bufferView" not in a:
        raise ValueError(f"accessor {index}: sparse or bufferView-less accessors are not handled")
    bv = js["bufferViews"][a["bufferView"]]
    if bv.get("buffer", 0) != 0 or "uri" in js["buffers"][bv.get("buffer", 0)]:
        raise ValueError(f"accessor {index}: data outside the GLB BIN chunk")
    dtype = np.dtype(COMPONENT[a["componentType"]])
    width = WIDTH[a["type"]]
    stride = bv.get("byteStride", 0)
    if stride and stride != dtype.itemsize * width:
        raise ValueError(f"accessor {index}: interleaved buffer views are not handled")
    start = bin_chunk[1] + bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    arr = np.frombuffer(data, dtype, a["count"] * width, start)
    return (arr.reshape(a["count"], width) if width > 1 else arr), start


def mesh_groups(data, js, bin_chunk):
    """One group per distinct (POSITION, NORMAL) accessor pair; primitives sharing them are merged."""
    groups = {}
    for mi, mesh in enumerate(js.get("meshes", [])):
        for pi, prim in enumerate(mesh["primitives"]):
            att = prim["attributes"]
            if prim.get("mode", 4) != 4 or "NORMAL" not in att or "POSITION" not in att:
                continue
            key = (att["POSITION"], att["NORMAL"])
            groups.setdefault(key, []).append((mi, pi, prim))
    out = []
    for (pa, na), prims in groups.items():
        P, _ = accessor(data, js, bin_chunk, pa)
        N, n_off = accessor(data, js, bin_chunk, na)
        na_ = js["accessors"][na]
        if na_["componentType"] != 5126 or na_.get("normalized"):
            raise ValueError(f"NORMAL accessor {na} is not float32")
        tris = []
        for _, _, prim in prims:
            if "indices" in prim:
                idx, _ = accessor(data, js, bin_chunk, prim["indices"])
                tris.append(idx.astype(np.int64).reshape(-1, 3))
            else:
                tris.append(np.arange(len(P), dtype=np.int64).reshape(-1, 3))
        out.append({"prims": [f"mesh{m}.prim{p}" for m, p, _ in prims], "prim_ids": [(m, p) for m, p, _ in prims],
                    "ntris": [len(t) for t in tris], "normal_accessor": na,
                    "normal_offset": n_off, "P": P.astype(np.float64), "N": N.astype(np.float64),
                    "T": np.concatenate(tris)})
    return out


# ----------------------------------------------------------------------------- analysis

def unit(v):
    n = np.linalg.norm(v, axis=-1, keepdims=True)
    return v / np.maximum(n, 1e-30), n[..., 0]


def analyse(P, N, T, feature_deg=FEATURE_DEG, cancel_ratio=None):
    """Per-corner reference directions (face, group, vertex) and weights for one vertex/index set."""
    cancel_ratio = CANCEL_RATIO if cancel_ratio is None else cancel_ratio
    tri = P[T]
    fn, fa2 = unit(np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0]))
    area = fa2 / 2
    # corner angles
    e1 = tri[:, [1, 2, 0]] - tri
    e2 = tri[:, [2, 0, 1]] - tri
    cosang = (e1 * e2).sum(2) / np.maximum(np.linalg.norm(e1, axis=2) * np.linalg.norm(e2, axis=2), 1e-30)
    cang = np.arccos(np.clip(cosang, -1, 1))  # (F,3)
    valid = area > 0
    # weld by position
    diag = float(np.linalg.norm(P.max(0) - P.min(0))) if len(P) else 1.0
    q = np.round(P / max(diag * 1e-6, 1e-12)).astype(np.int64)
    _, wid = np.unique(q, axis=0, return_inverse=True)
    wid = wid.reshape(-1)
    C = T.reshape(-1)                      # corner -> glTF vertex
    cf = np.repeat(np.arange(len(T)), 3)   # corner -> face
    cw = wid[C]                            # corner -> welded vertex
    nc = len(C)
    # all (corner i, corner j) pairs that share a welded vertex
    order = np.argsort(cw, kind="stable")
    ws = cw[order]
    starts = np.flatnonzero(np.r_[True, ws[1:] != ws[:-1]])
    sizes = np.diff(np.r_[starts, nc])
    k = np.repeat(sizes, sizes)
    total = int((sizes.astype(np.int64) ** 2).sum())
    grp = np.zeros((nc, 3))
    allv = np.zeros((nc, 3))
    cos_feat = np.cos(np.radians(feature_deg))
    wa = (cang.reshape(-1) * valid[cf])    # corner-angle weight of each corner's face
    aa = (area[cf] * valid[cf])            # area weight
    chunk = 20_000_000
    pos = 0
    while pos < nc:
        # process sorted corners [pos, end) so that the pair count stays bounded
        cum = np.cumsum(k[pos:])
        end = pos + max(1, int(np.searchsorted(cum, chunk, side="right")))
        kk = k[pos:end]
        I = np.repeat(np.arange(pos, end), kk)
        off = np.arange(len(I)) - np.repeat(np.cumsum(kk) - kk, kk)
        gstart = np.repeat(starts, sizes)[pos:end]
        J = np.repeat(gstart, kk) + off
        ci, cj = order[I], order[J]
        fi, fj = cf[ci], cf[cj]
        within = (fn[fi] * fn[fj]).sum(1) >= cos_feat
        np.add.at(grp, ci[within], fn[fj[within]] * wa[cj[within], None])
        np.add.at(allv, ci, fn[fj] * aa[cj, None])
        pos = end
    grp_u, grp_len = unit(grp)
    all_u, all_len = unit(allv)
    # generated LOD0s store exactly this vector even where double-sided sheets nearly cancel it; there it
    # points anywhere (haversack: up to 180 deg from the visible face), so it is no reference
    wsum = np.bincount(cw, weights=aa)[cw]
    all_ok = all_len > cancel_ratio * np.maximum(wsum, 1e-30)
    return {"fn": fn, "area": area, "valid": valid, "C": C, "cf": cf, "grp": grp_u, "grp_ok": grp_len > 0,
            "all": all_u, "all_ok": all_ok, "corner_area": (area / 3)[cf], "pairs": total,
            "welded": int(wid.max() + 1) if len(wid) else 0}


def corner_bad(A, Nc, thr_deg):
    """Bool per corner: stored/candidate normal Nc (per corner) is beyond thr of face, group and vertex."""
    c = np.cos(np.radians(thr_deg))
    n, ln = unit(Nc)
    ok = (n * A["fn"][A["cf"]]).sum(1) >= c
    ok |= A["grp_ok"] & ((n * A["grp"]).sum(1) >= c)
    ok |= A["all_ok"] & ((n * A["all"]).sum(1) >= c)
    ok |= ~A["valid"][A["cf"]]            # degenerate faces say nothing
    return ~ok | (ln < 1e-6)


def bad_share(A, bad):
    w = A["corner_area"]
    return float(w[bad].sum() / max(w.sum(), 1e-30))


def vertex_bad(A, bad, nv):
    """A glTF vertex counts as bad when more than half of its corner area is bad."""
    w = A["corner_area"]
    tot = np.bincount(A["C"], weights=w, minlength=nv)
    b = np.bincount(A["C"], weights=w * bad, minlength=nv)
    return (b > 0.5 * tot) & (tot > 0), b, tot


def assess(P, N, T, thr, feat):
    A = analyse(P, N, T, feat)
    bad = corner_bad(A, N[A["C"]], thr)
    share = bad_share(A, bad)
    # angle of stored normal to the face normal, for the report
    n, _ = unit(N[A["C"]])
    ang = np.degrees(np.arccos(np.clip((n * A["fn"][A["cf"]]).sum(1), -1, 1)))
    w = A["corner_area"]
    med = float(np.median(ang[bad])) if bad.any() else None
    return A, bad, share, {"bad_area_share": round(share, 5), "bad_corners": int(bad.sum()),
                           "corners": int(len(bad)), "median_bad_angle_deg": None if med is None else round(med, 1),
                           "face_angle_over_thr_area_share": round(float(w[ang > thr].sum() / max(w.sum(), 1e-30)), 5)}


def best_permutation(A, N, thr):
    shares = []
    for M in PERMS:
        shares.append(bad_share(A, corner_bad(A, (N @ M.T)[A["C"]], thr)))
    i = int(np.argmin(shares))
    return i, shares


def permutation_explains(share, shares, i):
    return i != 0 and share - shares[i] >= ROTATION_GAIN and shares[i] < share / 3


def repair_group(g, thr, feat, allow_rotation=True, perm_scope="all", skip=False, split=True):
    """Plan the repair of one vertex set. Returns (normals, corner->vertex, appended-vertex sources, info, A).

    normals has one row per output vertex (the source vertices, then any appended split copies);
    appended vertex k copies every other attribute of source vertex src[k]."""
    P, N, T = g["P"], g["N"], g["T"]
    nv = len(P)
    A, bad, share, info = assess(P, N, T, thr, feat)
    C = A["C"]
    newN = N.copy()
    Cn = C.copy()
    src, extra = [], []
    info.update(vertices=int(nv), permutation=None, from_permutation=0, recomputed_in_place=0,
                split_vertices=0, added_vertices=0, left_bad_mixed=0)
    if skip or not bad.any():
        info.update(after_bad_area_share=info["bad_area_share"], after_bad_corners=info["bad_corners"])
        return newN, Cn, np.zeros(0, np.int64), info, A
    if allow_rotation:
        i, shares = best_permutation(A, N, thr)
        if permutation_explains(share, shares, i):
            M = PERMS[i]
            info["permutation"] = {"matrix": M.astype(int).tolist(), "bad_area_share_after": round(shares[i], 5)}
            Nr = N @ M.T
            rvb, _, _ = vertex_bad(A, corner_bad(A, Nr[C], thr), nv)
            vb, _, _ = vertex_bad(A, bad, nv)
            used = np.bincount(C, minlength=nv) > 0
            # the permutation is a property of the whole file, so vertices that pass as stored only by
            # coincidence (door: 2016 of them, a median 69 deg from their permuted normal) take it too
            use = (vb if perm_scope == "bad" else used) & ~rvb
            newN[use] = Nr[use]
            info["from_permutation"] = int(use.sum())
    bad2 = corner_bad(A, newN[C], thr)          # corners still wrong after any permutation
    cos_feat = np.cos(np.radians(feat))
    grp = np.where(A["grp_ok"][:, None], A["grp"], np.where(A["all_ok"][:, None], A["all"], A["fn"][A["cf"]]))
    w = np.maximum(A["corner_area"], 1e-30)
    vany = np.bincount(C, weights=bad2.astype(float), minlength=nv) > 0
    vgood = np.bincount(C, weights=(~bad2).astype(float), minlength=nv) > 0
    acc = np.zeros((nv, 3))
    np.add.at(acc, C[bad2], grp[bad2] * w[bad2, None])
    acc_u, _ = unit(acc)
    spread = bad2 & ((grp * acc_u[C]).sum(1) < cos_feat)
    vspread = np.bincount(C, weights=spread.astype(float), minlength=nv) > 0
    # every corner bad and one smooth group: the vertex takes the group normal in place
    simple = vany & ~vgood & ~vspread
    newN[simple] = acc_u[simple]
    info["recomputed_in_place"] = int(simple.sum())
    hard = np.flatnonzero(vany & ~simple)
    if len(hard) and not split:
        # no splitting: a vertex that is mostly bad gets its bad corners' mean group normal
        vb, _, _ = vertex_bad(A, bad2, nv)
        take = hard[vb[hard]]
        newN[take] = acc_u[take]
        info["recomputed_in_place"] += int(len(take))
        info["left_bad_mixed"] = int(len(hard) - len(take))
    elif len(hard):
        # a vertex whose corners disagree: good corners keep the vertex and its stored normal, each smooth
        # group of bad corners gets its own copy of the vertex (a new vertex) with its group normal
        order = np.argsort(C, kind="stable")
        Cs = C[order]
        lo = np.searchsorted(Cs, hard, "left")
        hi = np.searchsorted(Cs, hard, "right")
        for v, a, b in zip(hard, lo, hi):
            cs = order[a:b]
            badc = cs[bad2[cs]]
            badc = badc[np.argsort(-w[badc], kind="stable")]
            reuse = not vgood[v]
            left = badc
            k = 0
            while len(left):
                take = (grp[left] @ grp[left[0]]) >= cos_feat
                cl = left[take]
                left = left[~take]
                n = unit((grp[cl] * w[cl, None]).sum(0))[0]
                if k == 0 and reuse:
                    newN[v] = n
                else:
                    Cn[cl] = nv + len(src)
                    src.append(v)
                    extra.append(n)
                k += 1
        info["split_vertices"] = int(len(hard))
        info["added_vertices"] = int(len(src))
    src = np.array(src, np.int64)
    if len(src):
        newN = np.concatenate([newN, np.array(extra)])
    # corners with no usable reference at all (only degenerate faces around them) keep the stored normal
    zero = np.linalg.norm(newN, axis=1) < 0.5
    if zero.any():
        newN[zero] = N[np.r_[np.arange(nv), src][zero]]
    after = corner_bad(A, newN[Cn], thr)
    info.update(after_bad_area_share=round(bad_share(A, after), 5), after_bad_corners=int(after.sum()))
    return newN, Cn, src, info, A


# ----------------------------------------------------------------------------- driver

def sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def measure_file(path, thr, feat):
    data, js, bc = read_glb(path)
    rows, tot_w, bad_w = [], 0.0, 0.0
    for g in mesh_groups(data, js, bc):
        A, bad, share, info = assess(g["P"], g["N"], g["T"], thr, feat)
        w = A["corner_area"].sum()
        tot_w += w
        bad_w += share * w
        info["prims"] = g["prims"]
        rows.append(info)
    return {"bad_area_share": round(bad_w / max(tot_w, 1e-30), 5), "groups": rows}


def relayout(data, js, bc, edits):
    """New GLB bytes where each edited vertex set has its appended vertices and new indices.

    Every buffer view keeps its place in the list and its bytes unless it holds an edited accessor;
    views are repacked in order with 4-byte alignment. The JSON changes only in accessor counts, index
    component types/min/max, buffer view offsets/lengths and the buffer length."""
    js2 = json.loads(json.dumps(js))
    view_bytes = {}
    uses = {}
    for a in js["accessors"]:
        if "bufferView" in a:
            uses[a["bufferView"]] = uses.get(a["bufferView"], 0) + 1
    for g, newN, Cn, src in edits:
        if not len(src) and np.array_equal(Cn, g["T"].reshape(-1)):
            # nothing appended: normals only, same count
            acc = js2["accessors"][g["normal_accessor"]]
            view_bytes[acc["bufferView"]] = newN.astype(np.float32).tobytes()
            continue
        prims = [js2["meshes"][m]["primitives"][p] for m, p in g["prim_ids"]]
        att = prims[0]["attributes"]
        if any(pr["attributes"] != att for pr in prims):
            raise ValueError("primitives sharing POSITION/NORMAL differ in other attributes; cannot split")
        for name, ai in att.items():
            acc = js2["accessors"][ai]
            if uses[acc["bufferView"]] != 1 or acc.get("byteOffset", 0):
                raise ValueError(f"accessor {ai} shares its buffer view; cannot split")
            arr, _ = accessor(data, js, bc, ai)
            new = newN.astype(np.float32) if name == "NORMAL" else np.concatenate([arr, arr[src]])
            acc["count"] = int(len(new))
            view_bytes[acc["bufferView"]] = np.ascontiguousarray(new).tobytes()
        nvert = len(newN)
        off = 0
        for pr, ntri in zip(prims, g["ntris"]):
            idx = Cn[off * 3:(off + ntri) * 3]
            off += ntri
            if "indices" not in pr:
                raise ValueError("non-indexed primitive; cannot split")
            ia = js2["accessors"][pr["indices"]]
            if uses[ia["bufferView"]] != 1 or ia.get("byteOffset", 0):
                raise ValueError("index accessor shares its buffer view; cannot split")
            ctype = ia["componentType"] if nvert - 1 <= np.iinfo(COMPONENT[ia["componentType"]]).max else 5125
            ia["componentType"] = ctype
            if "max" in ia:
                ia["max"] = [int(idx.max())]
            if "min" in ia:
                ia["min"] = [int(idx.min())]
            view_bytes[ia["bufferView"]] = idx.astype(COMPONENT[ctype]).tobytes()
    out = bytearray()
    for i, bv in enumerate(js2["bufferViews"]):
        if bv.get("buffer", 0) != 0:
            raise ValueError("more than one buffer")
        if i in view_bytes:
            b = view_bytes[i]
        else:
            o = bc[1] + js["bufferViews"][i].get("byteOffset", 0)
            b = data[o:o + js["bufferViews"][i]["byteLength"]]
        out += b"\0" * (-len(out) % 4)
        bv["byteOffset"] = len(out)
        bv["byteLength"] = len(b)
        out += b
    out += b"\0" * (-len(out) % 4)
    js2["buffers"][0]["byteLength"] = len(out)
    jb = json.dumps(js2, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    jb += b" " * (-len(jb) % 4)
    total = 12 + 8 + len(jb) + 8 + len(out)
    return (struct.pack("<III", 0x46546C67, 2, total) + struct.pack("<II", len(jb), 0x4E4F534A) + jb
            + struct.pack("<II", len(out), 0x004E4942) + bytes(out)), js2


def verify_relayout(data, js, bc, path):
    """Split output: same triangles with the same corner positions and UVs, same images and JSON elsewhere."""
    d2, j2, b2 = read_glb(path)
    for k in js:
        if k not in ("accessors", "bufferViews", "buffers"):
            assert js[k] == j2[k], f"JSON '{k}' changed"
    for im in js.get("images", []):
        if "bufferView" in im:
            v1, v2 = js["bufferViews"][im["bufferView"]], j2["bufferViews"][im["bufferView"]]
            o1, o2 = bc[1] + v1.get("byteOffset", 0), b2[1] + v2.get("byteOffset", 0)
            assert data[o1:o1 + v1["byteLength"]] == d2[o2:o2 + v2["byteLength"]], "image bytes changed"
    for mesh1, mesh2 in zip(js["meshes"], j2["meshes"]):
        for p1, p2 in zip(mesh1["primitives"], mesh2["primitives"]):
            assert p1.get("material") == p2.get("material"), "material changed"
            i1 = accessor(data, js, bc, p1["indices"])[0].astype(np.int64)
            i2 = accessor(d2, j2, b2, p2["indices"])[0].astype(np.int64)
            assert len(i1) == len(i2), "triangle count changed"
            for name in p1["attributes"]:
                if name == "NORMAL":
                    continue
                a1 = accessor(data, js, bc, p1["attributes"][name])[0]
                a2 = accessor(d2, j2, b2, p2["attributes"][name])[0]
                assert np.array_equal(a1[i1], a2[i2]), f"per-corner {name} changed"
    return True


def repair_file(src, dst, thr, feat, allow_rotation=True, perm_scope="all", floor=0.0, split=True):
    data, js, bc = read_glb(src)
    before = measure_file(src, thr, feat)["bad_area_share"]
    skip = before < floor
    groups, edits, tot_w, bad_w, after_w = [], [], 0.0, 0.0, 0.0
    for g in mesh_groups(data, js, bc):
        newN, Cn, srcv, info, A = repair_group(g, thr, feat, allow_rotation, perm_scope, skip, split)
        base = newN[:len(g["N"])]
        changed = np.any(base != g["N"], axis=1)
        edits.append((g, newN, Cn, srcv, changed))
        w = A["corner_area"].sum()
        tot_w += w
        bad_w += info["bad_area_share"] * w
        after_w += info["after_bad_area_share"] * w
        info["prims"] = g["prims"]
        info["changed_vertices"] = int(changed.sum())
        groups.append(info)
    splits = any(len(e[3]) for e in edits)
    if splits:
        out, _ = relayout(data, js, bc, [(g, newN, Cn, s) for g, newN, Cn, s, _ in edits])
    else:
        out = bytearray(data)
        for g, newN, Cn, s, changed in edits:
            if changed.any():
                o = g["normal_offset"]
                cur = np.frombuffer(bytes(out[o:o + len(g["N"]) * 12]), np.float32).reshape(-1, 3).copy()
                cur[changed] = newN[changed].astype(np.float32)
                out[o:o + len(g["N"]) * 12] = cur.tobytes()
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    tmp = dst + ".tmp"
    with open(tmp, "wb") as f:
        f.write(out)
    os.replace(tmp, dst)
    if splits:
        verify_relayout(data, js, bc, dst)
    else:
        # everything but the NORMAL bytes identical
        check = open(dst, "rb").read()
        assert len(check) == len(data)
        mask = np.ones(len(data), bool)
        for g, *_ in edits:
            mask[g["normal_offset"]:g["normal_offset"] + len(g["N"]) * 12] = False
        assert np.array_equal(np.frombuffer(data, np.uint8)[mask], np.frombuffer(check, np.uint8)[mask]), \
            "bytes outside the NORMAL accessors changed"
    re = measure_file(dst, thr, feat)
    return {"source": src, "source_sha1": sha1(src), "output": dst, "output_sha1": sha1(dst),
            "identical_to_source": sha1(src) == sha1(dst), "layout": "relayout (vertices split)" if splits else "in place",
            "left_alone_below_floor": bool(skip), "floor": floor,
            "bad_area_share_before": round(bad_w / max(tot_w, 1e-30), 5),
            "bad_area_share_after": round(after_w / max(tot_w, 1e-30), 5),
            "bad_area_share_after_remeasured": re["bad_area_share"],
            "changed_vertices": int(sum(x["changed_vertices"] for x in groups)),
            "added_vertices": int(sum(x["added_vertices"] for x in groups)),
            "vertices": int(sum(x["vertices"] for x in groups)), "groups": groups}


def ids_from(args):
    if args.ids:
        return [s.strip() for s in args.ids.split(",") if s.strip()]
    return sorted(d for d in os.listdir(args.ready_root)
                  if os.path.isfile(os.path.join(args.ready_root, d, d + ".glb")))


def main(argv=None):
    global CANCEL_RATIO
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("cmd", choices=["survey", "repair", "check"])
    ap.add_argument("files", nargs="*")
    ap.add_argument("--ids", default="")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--affected", action="store_true", help="repair: every asset over --min-share in a fresh survey")
    ap.add_argument("--min-share", type=float, default=AFFECTED_SHARE)
    ap.add_argument("--threshold", type=float, default=THRESHOLD_DEG)
    ap.add_argument("--feature-angle", type=float, default=FEATURE_DEG)
    ap.add_argument("--no-permutation", action="store_true", help="always recompute; never restore a permuted normal")
    ap.add_argument("--no-split", action="store_true",
                    help="never add vertices: a mixed or crease-spanning vertex gets one averaged normal (or is "
                         "left alone when mostly good); the file then keeps its exact layout")
    ap.add_argument("--permutation-scope", choices=["all", "bad"], default="all",
                    help="all: once a permutation is detected every vertex whose permuted normal passes takes it; "
                         "bad: only vertices that fail as stored")
    ap.add_argument("--floor", type=float, default=None,
                    help="repair: an asset whose bad-area share is under this is written unchanged "
                         "(default: --min-share, 0.10)")
    ap.add_argument("--cancel-ratio", type=float, default=CANCEL_RATIO,
                    help="the all-faces vertex normal counts as a reference only where its length is over this "
                         "share of the summed face weight (0 = always; double-sided sheets cancel it)")
    ap.add_argument("--ready-root", default=READY)
    ap.add_argument("--staging-root", default=STAGING)
    ap.add_argument("--out", default=None)
    args = ap.parse_args(argv)
    CANCEL_RATIO = args.cancel_ratio
    if args.floor is None:
        args.floor = args.min_share
    thr, feat = args.threshold, args.feature_angle
    params = {"threshold_deg": thr, "feature_angle_deg": feat, "rotation_gain": ROTATION_GAIN,
              "floor": args.floor, "permutation_scope": args.permutation_scope, "cancel_ratio": args.cancel_ratio,
              "split": not args.no_split,
              "permutation": not args.no_permutation,
              "tool": os.path.basename(__file__), "time": datetime.datetime.now().isoformat(timespec="seconds")}

    if args.cmd == "check":
        for f in args.files:
            r = measure_file(f, thr, feat)
            print(json.dumps({"file": f, "bad_area_share": r["bad_area_share"]}))
        return 0

    if args.cmd == "survey" or args.affected:
        ids = ids_from(args) if (args.ids or args.all or args.affected) else []
        rows = []
        for i, aid in enumerate(ids):
            path = os.path.join(args.ready_root, aid, aid + ".glb")
            try:
                data, js, bc = read_glb(path)
                tot_w = bad_w = 0.0
                perm = None
                med = []
                for g in mesh_groups(data, js, bc):
                    A, bad, share, info = assess(g["P"], g["N"], g["T"], thr, feat)
                    w = A["corner_area"].sum()
                    tot_w += w
                    bad_w += share * w
                    if info["median_bad_angle_deg"] is not None:
                        med.append(info["median_bad_angle_deg"])
                    if share >= args.min_share:
                        pi, shares = best_permutation(A, g["N"], thr)
                        if permutation_explains(share, shares, pi):
                            perm = {"matrix": PERMS[pi].astype(int).tolist(), "bad_area_share_after": round(shares[pi], 5)}
                row = {"id": aid, "bad_area_share": round(bad_w / max(tot_w, 1e-30), 5),
                       "median_bad_angle_deg": float(np.median(med)) if med else None, "permutation": perm}
            except Exception as e:  # noqa: BLE001 - one bad file must not stop the survey
                row = {"id": aid, "error": f"{type(e).__name__}: {e}"}
            rows.append(row)
            print(f"[{i + 1}/{len(ids)}] {aid} {row.get('bad_area_share', row.get('error'))}", flush=True)
        affected = sorted((r for r in rows if r.get("bad_area_share", 0) > args.min_share),
                          key=lambda r: -r["bad_area_share"])
        survey = {"params": params, "ready_root": args.ready_root, "assets": len(rows),
                  "affected_over": args.min_share, "affected_count": len(affected),
                  "affected": affected, "all": rows}
        if args.cmd == "survey":
            out = args.out or os.path.join(args.staging_root, "_survey.json")
            os.makedirs(os.path.dirname(out), exist_ok=True)
            json.dump(survey, open(out, "w"), indent=1)
            print(f"SURVEY {len(affected)} of {len(rows)} over {args.min_share:.0%} -> {out}")
            return 0
        ids = [r["id"] for r in affected]
    else:
        ids = ids_from(args)

    summary = []
    for aid in ids:
        src = os.path.join(args.ready_root, aid, aid + ".glb")
        dst = os.path.join(args.staging_root, aid, aid + ".glb")
        if os.path.normcase(os.path.abspath(dst)).startswith(os.path.normcase(os.path.abspath(args.ready_root)) + os.sep):
            raise SystemExit("refusing to write under ready/")
        rep = repair_file(src, dst, thr, feat, allow_rotation=not args.no_permutation, split=not args.no_split,
                          perm_scope=args.permutation_scope, floor=args.floor)
        rep["params"] = params
        json.dump(rep, open(os.path.join(os.path.dirname(dst), aid + "_normals_report.json"), "w"), indent=1)
        perm = next((g["permutation"] for g in rep["groups"] if g.get("permutation")), None)
        print(f"{aid}: bad {rep['bad_area_share_before']:.4f} -> {rep['bad_area_share_after_remeasured']:.4f}, "
              f"changed {rep['changed_vertices']}/{rep['vertices']} vertices"
              f"{', permutation ' + str(perm['matrix']) if perm else ''}", flush=True)
        summary.append({k: rep[k] for k in ("output", "bad_area_share_before", "bad_area_share_after_remeasured",
                                            "changed_vertices", "added_vertices", "vertices", "layout",
                                            "identical_to_source", "left_alone_below_floor")} | {"id": aid})
    out = args.out or os.path.join(args.staging_root, "_repair_%s.json" % datetime.datetime.now().strftime("%Y%m%d_%H%M%S"))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    json.dump({"params": params, "assets": summary}, open(out, "w"), indent=1)
    print(f"REPAIR {len(summary)} assets -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
