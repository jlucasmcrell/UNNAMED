"""Play a clip on a rigged GLB with the game's semantics and measure how the skin deforms.

Game semantics (src/Presentation/Art/ArtLibrary.cs Retarget + SkinnedModel): every bone track of the clip
is applied by bone NAME, replacing that bone's local translation / rotation / scale. Keys are carried over
rest-relative: rotation' = (body_rest * clip_rest^-1) * key, translation' = body_rest + (key - clip_rest),
the root/hips translation delta scaled by body hips height / clip hips height. Scale keys pass through.
Skinning: joint world matrix x inverse bind matrix, weighted (glTF). A clip baked on the body's own rest
comes back unchanged.

Measured on every frame (glTF frame: +Y up, metres), on the welded surface (UV seams merged):
  min_y             lowest skinned vertex (negative = below the ground, positive = floating)
  stretch           every edge's length change against rest; max and counts over 5 / 10 cm
  compression       edges shortened below 50 % of rest (a fold or a collapse)
  folds             edges whose two faces turned back on each other: the faces' normals agreed at rest
                    (dot > 0.5) and oppose in the pose (dot < -0.5) - a crease or a torn, jagged mass
  self_intersection triangle pairs that cut through each other in the pose but not at rest (inside
                    Blender only: mathutils BVHTree; the generative surfaces already cross themselves in
                    places at rest, so only the pairs the motion creates count); "local" = the two
                    triangles lay within LOCAL_PAIR x height of each other at rest: the surface crumpling
                    onto itself (weights), as against a limb passing through the body (the motion)
Every frame's worst place is reported with the bones that dominate it, so a failure points at the weights
that cause it. --dump writes posed vertex positions for chosen frames, and per vertex which defect touches
it (1 fold, 2 new self-intersection), for _render_pose_views.py, which then renders exactly what was measured.

  blender --background --factory-startup --python _skin_deform_check.py -- \
      --rig <rigged.glb> --clip <clip.glb> [--clip ...] [--json out.json] [--dump out.npz --dump-frames 12 31]
  python _skin_deform_check.py ...        (the same, without the self-intersection measure)
"""
import argparse
import json
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _blender_rig_fit_humanoid20 as fit  # noqa: E402  (numpy GLB reader; bpy optional)

try:
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
except ImportError:
    BVHTree = None

STRETCH_EDGE = (0.05, 0.10)   # m: edges stretched beyond these are counted
COMPRESS_RATIO = 0.5          # an edge shorter than this share of its rest length is compressed
FOLD_REST_DOT, FOLD_POSE_DOT = 0.5, -0.5
LOCAL_PAIR = 0.05             # x height: intersecting triangles this close at rest are the surface crumpling


def qmul(a, b):
    ax, ay, az, aw = np.moveaxis(a, -1, 0)
    bx, by, bz, bw = np.moveaxis(b, -1, 0)
    return np.stack([aw * bx + ax * bw + ay * bz - az * by,
                     aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw,
                     aw * bw - ax * bx - ay * by - az * bz], -1)


def qinv(q):
    return q * np.array([-1, -1, -1, 1.0])


def qmat(q):
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def trs(t, r, s):
    m = np.eye(4)
    m[:3, :3] = qmat(np.asarray(r, float)) * np.asarray(s, float)
    m[:3, 3] = t
    return m


def node_trs(node):
    if "matrix" in node:
        raise ValueError("matrix nodes not handled")
    return (np.array(node.get("translation", [0, 0, 0]), float), np.array(node.get("rotation", [0, 0, 0, 1]), float),
            np.array(node.get("scale", [1, 1, 1]), float))


class Rig:
    def __init__(self, path):
        self.doc, self.blob = fit.read_glb(path)
        doc = self.doc
        skin = doc["skins"][0]
        self.joints = skin["joints"]
        self.ibm = fit.accessor(doc, self.blob, skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
        self.names = [doc["nodes"][j]["name"] for j in self.joints]
        self.parent = {}
        for i, n in enumerate(doc["nodes"]):
            for c in n.get("children", []):
                self.parent[c] = i
        self.rest = {i: node_trs(n) for i, n in enumerate(doc["nodes"])}
        mesh = fit.glb_mesh(path, skinned=True)       # skinned positions as authored (glTF axes)
        self.V = mesh["verts_gltf"]
        self.J = mesh["joints"]
        self.W = mesh["weights"] / np.maximum(mesh["weights"].sum(1, keepdims=True), 1e-12)
        self.T = mesh["tris"]
        # welded surface (position-quantised) so UV seams neither count edges twice nor part folds
        q = np.round(self.V / 1e-5).astype(np.int64)
        _u, first, inv = np.unique(q, axis=0, return_index=True, return_inverse=True)
        inv = inv.ravel()
        self.first = first                  # welded vertex -> one source vertex
        self.weld = inv                     # source vertex -> welded vertex
        wt = inv[self.T]
        keep = (wt[:, 0] != wt[:, 1]) & (wt[:, 1] != wt[:, 2]) & (wt[:, 2] != wt[:, 0])
        self.WT = wt[keep]                  # welded triangles
        self.WT_src = np.nonzero(keep)[0]
        e = np.concatenate([self.WT[:, [0, 1]], self.WT[:, [1, 2]], self.WT[:, [2, 0]]])
        f = np.concatenate([np.arange(len(self.WT))] * 3)
        e = np.sort(e, axis=1)
        order = np.lexsort((e[:, 1], e[:, 0]))
        e, f = e[order], f[order]
        self.E, start = np.unique(e, axis=0, return_index=True)
        # manifold edges (exactly two faces) for the fold measure
        counts = np.diff(np.append(start, len(e)))
        two = counts == 2
        self.fold_edges = self.E[two]
        self.fold_faces = np.stack([f[start[two]], f[start[two] + 1]], 1)
        self.hips = self.names.index("hips") if "hips" in self.names else None

    def world(self, local):
        cache = {}

        def w(i):
            if i in cache:
                return cache[i]
            m = local.get(i)
            if m is None:
                m = trs(*self.rest[i])
            p = self.parent.get(i)
            cache[i] = m if p is None else w(p) @ m
            return cache[i]
        return {j: w(j) for j in self.joints}

    def rest_local(self):
        return {j: trs(*self.rest[j]) for j in self.joints}

    def hips_height(self):
        if self.hips is None:
            return 0.0
        wm = self.world(self.rest_local())
        return float(wm[self.joints[self.hips]][1, 3])

    def skin(self, local):
        wm = self.world(local)
        mats = np.stack([wm[j] @ self.ibm[k] for k, j in enumerate(self.joints)])
        vh = np.c_[self.V, np.ones(len(self.V))]
        out = np.zeros((len(self.V), 3))
        for c in range(self.J.shape[1]):
            m = mats[self.J[:, c]]
            out += self.W[:, c:c + 1] * np.einsum("nij,nj->ni", m, vh)[:, :3]
        return out

    def dominant(self, v):
        return self.names[int(self.J[v][np.argmax(self.W[v])])]


class Clip:
    def __init__(self, path):
        self.doc, self.blob = fit.read_glb(path)
        doc = self.doc
        anim = doc["animations"][0]
        self.tracks = {}
        self.t_end = 0.0
        for ch in anim["channels"]:
            s = anim["samplers"][ch["sampler"]]
            t = fit.accessor(doc, self.blob, s["input"]).astype(np.float64).ravel()
            v = fit.accessor(doc, self.blob, s["output"]).astype(np.float64)
            name = doc["nodes"][ch["target"]["node"]]["name"]
            self.tracks[(name, ch["target"]["path"])] = (t, v.reshape(len(t), -1), s.get("interpolation", "LINEAR"))
            self.t_end = max(self.t_end, float(t[-1]))
        self.rest = {n["name"]: node_trs(n) for n in doc["nodes"] if "name" in n}
        parent = {}
        for i, n in enumerate(doc["nodes"]):
            for c in n.get("children", []):
                parent[c] = i
        idx = {n.get("name"): i for i, n in enumerate(doc["nodes"])}

        def w(i):
            m = trs(*node_trs(doc["nodes"][i]))
            p = parent.get(i)
            return m if p is None else w(p) @ m
        self.hips_y = float(w(idx["hips"])[1, 3]) if "hips" in idx else 0.0

    def sample(self, key, time):
        t, v, interp = self.tracks[key]
        if time <= t[0]:
            return v[0]
        if time >= t[-1]:
            return v[-1]
        k = int(np.searchsorted(t, time) - 1)
        if interp == "STEP":
            return v[k]
        a = (time - t[k]) / max(t[k + 1] - t[k], 1e-12)
        if key[1] == "rotation":
            q0, q1 = v[k], v[k + 1]
            if np.dot(q0, q1) < 0:
                q1 = -q1
            q = (1 - a) * q0 + a * q1
            return q / np.linalg.norm(q)
        return (1 - a) * v[k] + a * v[k + 1]


def pose(rig, clip, time, relative=True):
    """Local matrices of the rig's joints at `time`, game semantics."""
    scale = 1.0
    if relative and clip.hips_y > 0.01:
        scale = rig.hips_height() / clip.hips_y
    local = {}
    for j in rig.joints:
        name = rig.doc["nodes"][j]["name"]
        t, r, s = (x.copy() for x in rig.rest[j])
        src = clip.rest.get(name)
        travels = rig.parent.get(j) not in rig.joints or name in ("hips", "pelvis")
        if (name, "translation") in clip.tracks:
            key = clip.sample((name, "translation"), time)
            if relative and src is not None:
                moved = key - src[0]
                t = rig.rest[j][0] + (moved * scale if travels else moved)
            else:
                t = key
        if (name, "rotation") in clip.tracks:
            key = clip.sample((name, "rotation"), time)
            if relative and src is not None:
                r = qmul(qmul(rig.rest[j][1], qinv(src[1])), key)
                r /= np.linalg.norm(r)
            else:
                r = key
        if (name, "scale") in clip.tracks:
            s = clip.sample((name, "scale"), time)
        local[j] = trs(t, r, s)
    return local


def face_normals(P, tris):
    n = np.cross(P[tris[:, 1]] - P[tris[:, 0]], P[tris[:, 2]] - P[tris[:, 0]])
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


def intersecting_pairs(P, tris):
    """Pairs of triangles (sharing at most one vertex) that cut each other: mathutils BVHTree."""
    if BVHTree is None:
        return None
    tree = BVHTree.FromPolygons([Vector(p) for p in P], tris.tolist(), all_triangles=True)
    return {(min(a, b), max(a, b)) for a, b in tree.overlap(tree)}


def measure(rig, clip, fps=30.0, relative=True, dump_frames=(), intersections=True, focus=None):
    rest_full = rig.skin(rig.rest_local())
    rest = rest_full[rig.first]
    near = None
    if focus:       # (joint name, radius): defects whose rest place lies within radius of that joint
        wm = rig.world(rig.rest_local())
        centre = wm[rig.joints[rig.names.index(focus[0])]][:3, 3]
        near = np.linalg.norm(rest - centre, axis=1) <= focus[1]
    E = rig.E
    rest_len = np.linalg.norm(rest[E[:, 0]] - rest[E[:, 1]], axis=1)
    rest_n = face_normals(rest, rig.WT)
    fe, ff = rig.fold_edges, rig.fold_faces
    rest_dot = (rest_n[ff[:, 0]] * rest_n[ff[:, 1]]).sum(1)
    rest_pairs = intersecting_pairs(rest, rig.WT) if intersections else None
    rest_centroid = rest[rig.WT].mean(1)
    height = float(rest[:, 1].max() - rest[:, 1].min())
    frames = int(round(clip.t_end * fps)) + 1
    rows, dumps = [], {}
    worst = {"stretch_m": 0.0}
    for f in range(frames):
        local = pose(rig, clip, f / fps, relative)
        P_full = rig.skin(local)
        P = P_full[rig.first]
        if f in dump_frames:
            dumps[f] = P_full.astype(np.float32)
        d = np.linalg.norm(P[E[:, 0]] - P[E[:, 1]], axis=1)
        stretch = d - rest_len
        ratio = d / np.maximum(rest_len, 1e-9)
        k = int(np.argmax(stretch))
        n = face_normals(P, rig.WT)
        dot = (n[ff[:, 0]] * n[ff[:, 1]]).sum(1)
        folded = (rest_dot > FOLD_REST_DOT) & (dot < FOLD_POSE_DOT)
        if near is not None:
            focus_fold = int((folded & near[fe[:, 0]]).sum())
        row = {"frame": f, "min_y": round(float(P[:, 1].min()), 4),
               "max_edge_stretch_m": round(float(stretch[k]), 4),
               "edges_over_5cm": int((stretch > STRETCH_EDGE[0]).sum()),
               "edges_over_10cm": int((stretch > STRETCH_EDGE[1]).sum()),
               "edges_compressed_50pct": int(((ratio < COMPRESS_RATIO) & (rest_len > 0.004)).sum()),
               "folded_edges": int(folded.sum())}
        where = []
        if folded.any():
            where += list(fe[folded].ravel())
        if intersections and rest_pairs is not None:
            pairs = intersecting_pairs(P, rig.WT) - rest_pairs
            row["new_self_intersections"] = len(pairs)
            # local: the two triangles lay within LOCAL_PAIR x height of each other at rest - the surface
            # crumpling onto itself (a weights defect), not a limb passing through the body (the motion)
            local = [(a, b) for a, b in pairs
                     if np.linalg.norm(rest_centroid[a] - rest_centroid[b]) <= LOCAL_PAIR * height]
            row["local_self_intersections"] = len(local)
            if near is not None:
                row["focus_local_self_intersections"] = sum(
                    1 for a, b in local if near[rig.WT[a, 0]] or near[rig.WT[b, 0]])
            if near is not None:
                row["focus_new_self_intersections"] = sum(
                    1 for a, b in pairs if near[rig.WT[a, 0]] or near[rig.WT[b, 0]])
            if pairs:
                tri_ids = np.array(sorted({t for p in pairs for t in p}))
                where += list(rig.WT[tri_ids].ravel())
        if near is not None:
            row["focus_folded_edges"] = focus_fold
            sel = near[E[:, 0]]
            row["focus_max_edge_stretch_m"] = round(float(stretch[sel].max()), 4) if sel.any() else 0.0
        if f in dump_frames:
            mask = np.zeros(len(rig.first), dtype=np.uint8)
            if folded.any():
                mask[fe[folded].ravel()] |= 1
            if intersections and rest_pairs is not None and row.get("new_self_intersections"):
                mask[rig.WT[tri_ids].ravel()] |= 2
            dumps[f"defects|{f}"] = mask[rig.weld]
        if where:
            w = np.unique(np.array(where))
            src = rig.first[w]
            bones = {}
            for v in src:
                b = rig.dominant(v)
                bones[b] = bones.get(b, 0) + 1
            row["defect_bones"] = dict(sorted(bones.items(), key=lambda kv: -kv[1])[:4])
            row["defect_rest_box_m"] = [[round(float(x), 3) for x in rest[w].min(0)],
                                        [round(float(x), 3) for x in rest[w].max(0)]]
        rows.append(row)
        if stretch[k] > worst["stretch_m"]:
            a, b = E[k]
            worst = {"stretch_m": round(float(stretch[k]), 4), "frame": f,
                     "rest_length_m": round(float(rest_len[k]), 4),
                     "at_rest_m": [round(float(x), 3) for x in rest[a]],
                     "bones": [rig.dominant(rig.first[a]), rig.dominant(rig.first[b])]}
    ys = [r["min_y"] for r in rows]
    out = {"frames": frames, "relative": relative,
           "min_y_range_m": [min(ys), max(ys)],
           "rest_min_y_m": round(float(rest[:, 1].min()), 4),
           "frames_below_ground_2cm": sum(1 for y in ys if y < -0.02),
           "frames_floating_2cm": sum(1 for y in ys if y > 0.02),
           "max_edge_stretch_m": max(r["max_edge_stretch_m"] for r in rows),
           "max_edges_over_5cm": max(r["edges_over_5cm"] for r in rows),
           "max_edges_over_10cm": max(r["edges_over_10cm"] for r in rows),
           "max_edges_compressed_50pct": max(r["edges_compressed_50pct"] for r in rows),
           "max_folded_edges": max(r["folded_edges"] for r in rows),
           "worst_fold_frame": max(rows, key=lambda r: r["folded_edges"])["frame"],
           "worst_edge": worst, "per_frame": rows}
    if near is not None:
        out["focus"] = {"joint": focus[0], "radius_m": focus[1],
                        "max_folded_edges": max(r["focus_folded_edges"] for r in rows),
                        "max_edge_stretch_m": max(r["focus_max_edge_stretch_m"] for r in rows)}
        if intersections and rest_pairs is not None:
            out["focus"]["max_new_self_intersections"] = max(r["focus_new_self_intersections"] for r in rows)
            out["focus"]["max_local_self_intersections"] = max(r["focus_local_self_intersections"] for r in rows)
            out["focus"]["worst_local_frame"] = max(rows, key=lambda r: r["focus_local_self_intersections"])["frame"]
            out["focus"]["worst_frame"] = max(rows, key=lambda r: r["focus_new_self_intersections"])["frame"]
    if intersections and rest_pairs is not None:
        out["rest_self_intersections"] = len(rest_pairs)
        out["max_new_self_intersections"] = max(r["new_self_intersections"] for r in rows)
        out["max_local_self_intersections"] = max(r["local_self_intersections"] for r in rows)
        out["worst_local_frame"] = max(rows, key=lambda r: r["local_self_intersections"])["frame"]
        out["worst_intersection_frame"] = max(rows, key=lambda r: r["new_self_intersections"])["frame"]
    return out, dumps


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument("--rig", required=True)
    parser.add_argument("--clip", action="append", required=True)
    parser.add_argument("--absolute", action="store_true", help="apply keys as-is (no rest-relative carry)")
    parser.add_argument("--no-intersections", action="store_true")
    parser.add_argument("--json")
    parser.add_argument("--dump", help="npz of posed vertices (glTF axes, source vertex order) per clip/frame")
    parser.add_argument("--dump-frames", type=int, nargs="*", default=[])
    parser.add_argument("--focus", nargs=2, metavar=("JOINT", "RADIUS_M"),
                        help="also count the defects whose rest place is within RADIUS_M of JOINT (e.g. "
                             "upper_arm.R 0.3: the right shoulder)")
    return parser.parse_args(argv)


def main():
    args = parse_args()
    rig = Rig(args.rig)
    rest = rig.skin(rig.rest_local())
    report = {"rig": args.rig, "bind_skin_vs_mesh_max_m": round(float(np.abs(rest - rig.V).max()), 7),
              "rig_hips_height_m": round(rig.hips_height(), 4), "welded_edges": int(len(rig.E)),
              "self_intersection_measured": BVHTree is not None and not args.no_intersections,
              "clips": {}}
    dumps = {}
    for c in args.clip:
        clip = Clip(c)
        m, d = measure(rig, clip, relative=not args.absolute, dump_frames=set(args.dump_frames),
                       intersections=not args.no_intersections,
                       focus=(args.focus[0], float(args.focus[1])) if args.focus else None)
        m["clip_hips_height_m"] = round(clip.hips_y, 4)
        stem = os.path.splitext(os.path.basename(c))[0]
        report["clips"][os.path.basename(c)] = m
        for f, P in d.items():
            if isinstance(f, str):                      # "defects|<frame>"
                dumps[f"defects|{stem}|{f.split('|')[1]}"] = P
            else:
                dumps[f"{stem}|{f}"] = P
        print(f"{os.path.basename(c)}: frames {m['frames']} min_y {m['min_y_range_m']} "
              f"below2cm {m['frames_below_ground_2cm']} float2cm {m['frames_floating_2cm']} "
              f"stretch {m['max_edge_stretch_m']} (>5cm {m['max_edges_over_5cm']}, >10cm {m['max_edges_over_10cm']}) "
              f"compressed {m['max_edges_compressed_50pct']} folds {m['max_folded_edges']} (f{m['worst_fold_frame']}) "
              f"new-isect {m.get('max_new_self_intersections')} (f{m.get('worst_intersection_frame')}) "
              f"local {m.get('max_local_self_intersections')} (f{m.get('worst_local_frame')}) "
              f"worst {m['worst_edge']}" + (f" FOCUS {m['focus']}" if "focus" in m else ""))
    if args.dump:
        np.savez_compressed(args.dump, rest=rest.astype(np.float32), **dumps)
    if args.json:
        with open(args.json, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=1)
    print(f"SKIN_CHECK_DONE bind skin vs mesh {report['bind_skin_vs_mesh_max_m']} m; hips {report['rig_hips_height_m']} m")
    return 0


if __name__ == "__main__":
    main()
