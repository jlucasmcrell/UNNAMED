"""Stage-6 test: does topology repair improve a failed Pixal3D compound prop?

Works on a COPY of ready/<id>/<id>.glb and writes only under assets/_staging/topology_repair/<id>/.
The ready, rigged, animation, manifests and concepts trees are read, never written.

Repair applied (the forensic audit's proposal):
  1. merge-by-distance weld of coincident vertices (--weld metres; the sweep in the report shows
     where the weld stops being coincident-only and starts collapsing real edges)
  2. delete faces that exactly duplicate another face (same vertices, same winding); opposite-winding
     duplicates are kept and counted, because deleting one side would open a hole under culling
  3. bmesh recalc_face_normals (consistent winding, oriented outward)
  4. vertex normals recomputed from the repaired geometry (custom normals dropped, shaded smooth)
Non-manifold edges and open boundaries are located and reported, never filled or split.

Measured before and after, in Blender and on the GLB files themselves: vert/tri, index-level and
position-welded boundary-edge share, non-manifold edges, winding conflicts, components, duplicate faces,
and faces whose winding opposes the stored NORMAL. Renders use identical cameras and lighting for
before and after, textured (material culling as authored) and clay (backface culling on, as in Godot).

Run headless:
  blender --background --factory-startup --python _topology_repair_test.py -- \
      --asset prop_quarry_winch [--asset prop_cart_damaged_merchant] [--weld 1e-5] [--res 1024]
Weld-only variant (steps 1-2, stored normals kept, no recalc), written to <id>__weldonly/:
      ... -- --asset prop_quarry_winch --no-recalc --keep-normals --tag weldonly
"""
import argparse
import json
import math
import os
import shutil
import struct
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(REPO, "assets")
STAGING = os.path.join(ASSETS, "_staging", "topology_repair")
SWEEP = (1e-6, 1e-5, 1e-4, 1e-3, 3e-3)
# (label, azimuth from the concept-camera side toward +X, elevation), degrees. View 0 is the
# Pixal3D reconstruction camera, so it should reproduce the concept image's framing.
VIEWS = (("v0_concept_cam", 0, 0), ("v1_front_right", 55, 20), ("v2_back_left", 215, 25),
         ("v3_high_left", 300, 60))


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--asset", action="append", required=True)
    p.add_argument("--weld", type=float, default=1e-5, help="merge-by-distance radius in metres")
    p.add_argument("--res", type=int, default=1024)
    p.add_argument("--samples", type=int, default=32)
    p.add_argument("--no-render", action="store_true")
    p.add_argument("--out", default=STAGING)
    p.add_argument("--no-recalc", action="store_true", help="skip recalc_face_normals (weld-only variant)")
    p.add_argument("--keep-normals", action="store_true", help="keep the stored glTF normals")
    p.add_argument("--tag", default="", help="suffix for the per-asset output folder")
    return p.parse_args(argv)


def guarded_out(path):
    root = os.path.normcase(os.path.abspath(STAGING))
    full = os.path.normcase(os.path.abspath(path))
    if not (full == root or full.startswith(root + os.sep)):
        raise SystemExit(f"refusing to write outside {STAGING}: {path}")
    os.makedirs(path, exist_ok=True)
    return path


# ---------------------------------------------------------------- GLB file reader (no Blender)
_DT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
_NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}


def read_glb_mesh(path):
    blob = open(path, "rb").read()
    off, js, bin_ = 12, None, b""
    while off < len(blob) - 8:
        ln, kind = struct.unpack("<II", blob[off:off + 8])
        chunk = blob[off + 8:off + 8 + ln]
        if kind == 0x4E4F534A:
            js = json.loads(chunk)
        elif kind == 0x004E4942:
            bin_ = chunk
        off += 8 + ln + (-ln) % 4

    def acc(i):
        a = js["accessors"][i]
        v = js["bufferViews"][a["bufferView"]]
        n = _NC[a["type"]]
        return np.frombuffer(bin_, _DT[a["componentType"]], a["count"] * n,
                             v.get("byteOffset", 0) + a.get("byteOffset", 0)).reshape(a["count"], n)

    Ps, Ns, Ts, base = [], [], [], 0
    for m in js["meshes"]:
        for pr in m["primitives"]:
            P = acc(pr["attributes"]["POSITION"]).astype(np.float64)
            N = acc(pr["attributes"]["NORMAL"]).astype(np.float64) if "NORMAL" in pr["attributes"] else None
            T = acc(pr["indices"]).reshape(-1, 3).astype(np.int64) + base
            Ps.append(P); Ns.append(N); Ts.append(T); base += len(P)
    P = np.vstack(Ps)
    N = np.vstack(Ns) if all(n is not None for n in Ns) else None
    T = np.vstack(Ts)
    return P, T, (N[T] if N is not None else None)


# ---------------------------------------------------------------- topology metrics
def components(T, nv):
    lab = np.arange(nv)
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]]])
    while True:
        m = np.minimum(lab[e[:, 0]], lab[e[:, 1]])
        new = lab.copy()
        np.minimum.at(new, e[:, 0], m)
        np.minimum.at(new, e[:, 1], m)
        new = new[new]
        if np.array_equal(new, lab):
            break
        lab = new
    return lab


def edge_stats(T, split=False):
    e = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
    es = np.sort(e, axis=1)
    ue, inv, cnt = np.unique(es, axis=0, return_inverse=True, return_counts=True)
    de, dcnt = np.unique(e, axis=0, return_counts=True)
    if not split:
        return ue, inv.reshape(-1), cnt, int((dcnt > 1).sum())
    # a repeated directed edge on a 2-face edge is a true orientation conflict; on a >2-face edge
    # some repeats are unavoidable, so the two are counted apart
    rep = de[dcnt > 1]
    und = {tuple(x): c for x, c in zip(ue.tolist(), cnt.tolist())}
    on_manifold = sum(1 for a, b in rep.tolist() if und[(min(a, b), max(a, b))] == 2)
    return ue, inv.reshape(-1), cnt, int((dcnt > 1).sum()), on_manifold


def weld_index(P, q):
    _, inv = np.unique(np.round(P / q).astype(np.int64), axis=0, return_inverse=True)
    return inv.reshape(-1)


def topo_metrics(P, T, CN, q):
    """CN: per-triangle stored corner normals (T,3,3) or None."""
    out = {"verts": int(len(P)), "tris": int(len(T))}
    out["verts_per_tri"] = round(len(P) / max(len(T), 1), 3)
    ue, _, cnt, _ = edge_stats(T)
    out["index_level"] = {"edges": int(len(ue)), "boundary_edges": int((cnt == 1).sum()),
                          "boundary_share_pct": round(100.0 * (cnt == 1).sum() / len(ue), 2)}
    lab = components(T, len(P))
    out["index_level"]["components"] = int(len(np.unique(lab[np.unique(T)])))

    inv = weld_index(P, q)
    Tw = inv[T]
    keep = (Tw[:, 0] != Tw[:, 1]) & (Tw[:, 1] != Tw[:, 2]) & (Tw[:, 0] != Tw[:, 2])
    Tw = Tw[keep]
    ue, einv, cnt, conflicts, conflicts_manifold = edge_stats(Tw, split=True)
    Pw = np.zeros((inv.max() + 1, 3))
    Pw[inv] = P
    elen = np.linalg.norm(Pw[ue[:, 0]] - Pw[ue[:, 1]], axis=1)
    valence = np.bincount(Tw.ravel())
    labw = components(Tw, inv.max() + 1)
    comp_of_tri = labw[Tw[:, 0]]
    sizes = np.bincount(np.unique(comp_of_tri, return_inverse=True)[1].reshape(-1))
    # duplicates: same vertex set; same winding if the canonical cyclic rotation matches
    srt = np.sort(Tw, axis=1)
    _, dinv, dcnt = np.unique(srt, axis=0, return_inverse=True, return_counts=True)
    dinv = dinv.reshape(-1)
    rot = np.argmin(Tw, axis=1)
    canon = np.stack([Tw[np.arange(len(Tw)), (rot + k) % 3] for k in range(3)], axis=1)
    same = opp = 0
    for g in np.nonzero(dcnt > 1)[0]:
        idx = np.nonzero(dinv == g)[0]
        c0 = canon[idx[0]]
        for j in idx[1:]:
            if np.array_equal(canon[j], c0):
                same += 1
            else:
                opp += 1
    welded = {"weld_q_m": q, "unique_positions": int(inv.max() + 1), "degenerate_after_weld": int((~keep).sum()),
              "verts_per_tri": round((inv.max() + 1) / max(len(Tw), 1), 3),
              "edges": int(len(ue)), "boundary_edges": int((cnt == 1).sum()),
              "boundary_share_pct": round(100.0 * (cnt == 1).sum() / len(ue), 3),
              "nonmanifold_edges_gt2": int((cnt > 2).sum()),
              "winding_conflicts_repeated_directed_edges": conflicts,
              "winding_conflicts_on_2face_edges": conflicts_manifold,
              "edge_len_m_median_p99_max": [round(float(x), 4) for x in (np.median(elen), np.percentile(elen, 99), elen.max())],
              "edges_longer_than_10cm": int((elen > 0.10).sum()),
              "tris_per_vertex_mean_max": [round(float(valence[valence > 0].mean()), 2), int(valence.max())],
              "components": int(len(sizes)), "largest_component_tris": int(sizes.max()),
              "component_tris_sorted": sorted((int(s) for s in sizes), reverse=True)[:20],
              "duplicate_faces_same_winding": same, "duplicate_faces_opposite_winding": opp}
    out["welded"] = welded
    if CN is not None:
        Pt = P[T]
        fn = np.cross(Pt[:, 1] - Pt[:, 0], Pt[:, 2] - Pt[:, 0])
        d = (fn * CN.mean(1)).sum(1)
        out["faces_winding_opposes_stored_normal"] = int((d < 0).sum())
        out["faces_winding_opposes_stored_normal_pct"] = round(100.0 * (d < 0).sum() / len(T), 2)
    return out


def sweep(P, T):
    rows = []
    for q in SWEEP:
        inv = weld_index(P, q)
        Tw = inv[T]
        deg = int(((Tw[:, 0] == Tw[:, 1]) | (Tw[:, 1] == Tw[:, 2]) | (Tw[:, 0] == Tw[:, 2])).sum())
        rows.append({"q_m": q, "unique_positions": int(inv.max() + 1), "tris_collapsed": deg})
    return rows


def boundary_and_nonmanifold_regions(P, T, q):
    """Locate open boundary loops and non-manifold edge clusters in the welded mesh."""
    inv = weld_index(P, q)
    Pw = np.zeros((inv.max() + 1, 3))
    Pw[inv] = P
    Tw = inv[T]
    Tw = Tw[(Tw[:, 0] != Tw[:, 1]) & (Tw[:, 1] != Tw[:, 2]) & (Tw[:, 0] != Tw[:, 2])]
    ue, _, cnt, _ = edge_stats(Tw)
    zmin, zmax = P[:, 2].min(), P[:, 2].max()

    def clusters(edges):
        if len(edges) == 0:
            return []
        verts, local = np.unique(edges, return_inverse=True)
        local = local.reshape(-1, 2)
        lab = components(np.c_[local, local[:, 1]], len(verts))
        out = []
        for c in np.unique(lab):
            sel = np.nonzero(lab[local[:, 0]] == c)[0]
            vv = Pw[verts[np.unique(local[sel])]]
            length = float(np.linalg.norm(Pw[edges[sel, 0]] - Pw[edges[sel, 1]], axis=1).sum())
            ctr = vv.mean(0)
            out.append({"edges": int(len(sel)), "length_m": round(length, 3),
                        "centre_xyz_blender": [round(float(x), 3) for x in ctr],
                        "height_frac": round(float((ctr[2] - zmin) / max(zmax - zmin, 1e-9)), 2),
                        "extent_m": [round(float(x), 3) for x in (vv.max(0) - vv.min(0))]})
        return sorted(out, key=lambda r: -r["edges"])

    b = clusters(ue[cnt == 1])
    nm = clusters(ue[cnt > 2])
    return {"boundary_regions": len(b), "boundary_top": b[:12],
            "nonmanifold_clusters": len(nm), "nonmanifold_top": nm[:12],
            "nonmanifold_cluster_sizes": sorted((r["edges"] for r in nm), reverse=True)[:40]}


def inward_by_parity(P, T):
    """Faces whose winding normal points inward, judged by ray parity: a ray leaving the face along
    its normal that crosses the surface an odd number of times started out facing inside. Noisy on an
    open or self-intersecting mesh, but geometry is identical before and after, so it compares."""
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromPolygons([tuple(p) for p in P.tolist()], T.tolist(), epsilon=0.0)
    Pt = P[T]
    n = np.cross(Pt[:, 1] - Pt[:, 0], Pt[:, 2] - Pt[:, 0])
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-30)
    c = Pt.mean(1)
    inward = np.zeros(len(T), dtype=bool)
    for i in range(len(T)):
        d = Vector(n[i])
        o = Vector(c[i]) + d * 1e-4
        hits = 0
        while hits < 64:
            loc, _nor, _idx, _dist = bvh.ray_cast(o, d)
            if loc is None:
                break
            hits += 1
            o = loc + d * 1e-4
        inward[i] = hits % 2 == 1
    return inward


# ---------------------------------------------------------------- Blender helpers
def blender_arrays(ob):
    me = ob.data
    mw = np.array(ob.matrix_world)
    P = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    lt = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_total", lt)
    if not (lt == 3).all():
        raise RuntimeError("expected an all-triangle mesh")
    ls = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_start", ls)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    T = lv[ls[:, None] + np.arange(3)]
    cn = np.empty(len(me.loops) * 3)
    me.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3) @ mw[:3, :3].T
    return P, T, cn[ls[:, None] + np.arange(3)]


def import_copy(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    obs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    if len(obs) != 1:
        raise RuntimeError(f"expected one mesh object in {path}, got {len(obs)}")
    return obs[0]


def repair(ob, weld, recalc=True, keep_normals=False):
    me = ob.data.copy()
    if keep_normals:
        cn = np.empty(len(me.loops) * 3)
        me.corner_normals.foreach_get("vector", cn)
        me.attributes.new("orig_n", "FLOAT_VECTOR", "CORNER").data.foreach_set("vector", cn)
    rep = bpy.data.objects.new(ob.name + "_repaired", me)
    bpy.context.scene.collection.objects.link(rep)
    rep.matrix_world = ob.matrix_world
    bm = bmesh.new()
    bm.from_mesh(me)
    log = {"weld_distance_m": weld, "verts_in": len(bm.verts), "faces_in": len(bm.faces)}
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=weld)
    log["verts_after_weld"] = len(bm.verts)
    log["faces_after_weld"] = len(bm.faces)
    bm.verts.index_update()
    seen, kill, opp = {}, [], 0
    for f in bm.faces:
        vs = [v.index for v in f.verts]
        k = tuple(sorted(vs))
        r = vs.index(min(vs))
        canon = tuple(vs[r:] + vs[:r])
        if k in seen:
            if seen[k] == canon:
                kill.append(f)
            else:
                opp += 1
        else:
            seen[k] = canon
    if kill:
        bmesh.ops.delete(bm, geom=kill, context="FACES_ONLY")
    log["duplicate_faces_same_winding_removed"] = len(kill)
    log["duplicate_faces_opposite_winding_kept"] = opp
    degen = [e for e in bm.edges if e.calc_length() < 1e-9]
    log["zero_length_edges"] = len(degen)
    bm.normal_update()
    bm.faces.index_update()
    before_n = [f.normal.copy() for f in bm.faces]
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.normal_update()
    flipped = [i for i, f in enumerate(bm.faces) if f.normal.dot(before_n[i]) < 0]
    log["faces_flipped_by_recalc"] = len(flipped)
    log["faces_out"] = len(bm.faces)
    bm.to_mesh(me)
    bm.free()
    if "custom_normal" in me.attributes:
        me.attributes.remove(me.attributes["custom_normal"])
    me.shade_smooth()
    for name in ("sharp_edge", "sharp_face"):
        if name in me.attributes:
            me.attributes.remove(me.attributes[name])
    if keep_normals:
        # the stored glTF normals are absolute directions; re-apply them to the welded corners
        cn = np.empty(len(me.loops) * 3)
        me.attributes["orig_n"].data.foreach_get("vector", cn)
        me.attributes.remove(me.attributes["orig_n"])
        me.normals_split_custom_set(cn.reshape(-1, 3).tolist())
    log["recalc_face_normals"] = recalc
    log["vertex_normals"] = "stored glTF normals kept" if keep_normals else "recomputed smooth"
    me.update()
    return rep, log, np.array(flipped, dtype=np.int64)


def export_selected(ob, path):
    bpy.ops.object.select_all(action="DESELECT")
    ob.hide_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_materials="EXPORT", export_normals=True, export_yup=True,
                              export_apply=False)


# ---------------------------------------------------------------- rendering
def make_mat(name, rgb, cull=True, attr=None):
    m = bpy.data.materials.new(name)
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.65
    if attr:
        a = m.node_tree.nodes.new("ShaderNodeAttribute")
        a.attribute_name = attr
        m.node_tree.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    m.use_backface_culling = cull
    return m


def setup_stage(res, samples):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.eevee.taa_render_samples = samples
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("stage_world")
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.62, 0.63, 0.65, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.45
    sc.world = world
    sun = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
    sun.data.energy = 2.6
    sun.data.angle = math.radians(4)
    sun.rotation_euler = (math.radians(40), 0, math.radians(-30))
    sc.collection.objects.link(sun)
    bpy.ops.mesh.primitive_plane_add(size=400, location=(0, 0, -0.002))
    ground = bpy.context.active_object
    ground.name = "ground"
    ground.data.materials.append(make_mat("ground", (0.5, 0.5, 0.52), cull=False))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 50
    cam.data.clip_start = 0.01
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def frame_views(ob):
    corners = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    ctr, r = (lo + hi) / 2, (hi - lo).length / 2
    d = r / math.sin(math.atan(18 / 50)) * 0.8
    out = []
    for label, az, el in VIEWS:
        a, e = math.radians(az), math.radians(el)
        pos = ctr + d * Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        out.append((label, pos, (ctr - pos).to_track_quat("-Z", "Y")))
    return out


def render_set(cam, views, show, hide, out_dir, prefix, material=None):
    for o in hide:
        o.hide_render = True
    show.hide_render = False
    saved = None
    if material is not None:
        saved = [s.material for s in show.material_slots]
        for s in show.material_slots:
            s.material = material
    files = []
    for label, pos, quat in views:
        cam.location = pos
        cam.rotation_mode = "QUATERNION"
        cam.rotation_quaternion = quat
        f = os.path.join(out_dir, f"{prefix}_{label}.png")
        bpy.context.scene.render.filepath = f
        bpy.ops.render.render(write_still=True)
        files.append(f)
    if saved is not None:
        for s, m in zip(show.material_slots, saved):
            s.material = m
    return files


def see_through(cam, views, show, hide, ground, out_dir, prefix):
    """Share of the object's silhouette that turns see-through once back faces are culled, as the
    game's single-sided material does. Workbench, transparent film, ground hidden."""
    sc = bpy.context.scene
    engine, transparent = sc.render.engine, sc.render.film_transparent
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.film_transparent = True
    sc.display.shading.light = "FLAT"
    sc.display.shading.color_type = "SINGLE"
    ground.hide_render = True
    for o in hide:
        o.hide_render = True
    show.hide_render = False
    rows = []
    for label, pos, quat in views:
        cam.location = pos
        cam.rotation_mode = "QUATERNION"
        cam.rotation_quaternion = quat
        alpha = []
        for cull in (False, True):
            sc.display.shading.show_backface_culling = cull
            f = os.path.join(out_dir, f"{prefix}_mask_{'cull' if cull else 'nocull'}_{label}.png")
            sc.render.filepath = f
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(f)
            px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
            img.pixels.foreach_get(px)
            alpha.append(px[3::4] > 0.5)
            bpy.data.images.remove(img)
        sil = alpha[0].sum()
        holes = (alpha[0] & ~alpha[1]).sum()
        rows.append({"view": label, "silhouette_px": int(sil), "see_through_px": int(holes),
                     "see_through_pct": round(100.0 * holes / max(sil, 1), 2)})
    sc.render.engine, sc.render.film_transparent = engine, transparent
    sc.display.shading.show_backface_culling = False
    ground.hide_render = False
    return rows


def paint_faces(ob, colours, name="diag"):
    me = ob.data
    if name in me.attributes:
        me.attributes.remove(me.attributes[name])
    at = me.attributes.new(name, "FLOAT_COLOR", "CORNER")
    ls = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_start", ls)
    buf = np.zeros((len(me.loops), 4), dtype=np.float32)
    for k in range(3):
        buf[ls + k] = colours
    at.data.foreach_set("color", buf.ravel())


def face_flags(P, T, q):
    inv = weld_index(P, q)
    Tw = inv[T]
    e = np.sort(np.concatenate([Tw[:, [0, 1]], Tw[:, [1, 2]], Tw[:, [2, 0]]]), axis=1)
    _, einv, cnt = np.unique(e, axis=0, return_inverse=True, return_counts=True)
    c = cnt[einv.reshape(-1)].reshape(3, -1).T
    return (c > 2).any(1), (c == 1).any(1)


# ---------------------------------------------------------------- main
def run_asset(aid, a):
    src = os.path.join(ASSETS, "ready", aid, f"{aid}.glb")
    out = guarded_out(os.path.join(a.out, aid + (f"__{a.tag}" if a.tag else "")))
    copy = os.path.join(out, f"{aid}_source_copy.glb")
    shutil.copy2(src, copy)
    rep = {"asset": aid, "source": src, "copy": copy, "weld_m": a.weld}

    P0, T0, CN0 = read_glb_mesh(copy)
    rep["file_before"] = topo_metrics(P0, T0, CN0, a.weld)
    rep["weld_sweep"] = sweep(P0, T0)

    ob = import_copy(copy)
    P, T, CN = blender_arrays(ob)
    rep["blender_before"] = topo_metrics(P, T, CN, a.weld)
    rep["regions_before"] = boundary_and_nonmanifold_regions(P, T, a.weld)

    fixed, log, flipped = repair(ob, a.weld, recalc=not a.no_recalc, keep_normals=a.keep_normals)
    rep["repair_log"] = log
    P1, T1, CN1 = blender_arrays(fixed)
    rep["blender_after"] = topo_metrics(P1, T1, CN1, a.weld)
    rep["regions_after"] = boundary_and_nonmanifold_regions(P1, T1, a.weld)
    in0, in1 = inward_by_parity(P, T), inward_by_parity(P1, T1)
    rep["inward_facing_by_ray_parity"] = {
        "before": int(in0.sum()), "before_pct": round(100.0 * in0.mean(), 2),
        "after": int(in1.sum()), "after_pct": round(100.0 * in1.mean(), 2)}
    # vertex positions must be untouched: every repaired vertex sits exactly on an original one
    s0 = {tuple(np.round(p, 7)) for p in P}
    rep["repaired_vertices_not_on_original_positions"] = int(sum(tuple(np.round(p, 7)) not in s0 for p in P1))
    rep["original_positions_missing_after"] = len(s0 - {tuple(np.round(p, 7)) for p in P1})

    out_glb = os.path.join(out, f"{aid}_repaired.glb")
    export_selected(fixed, out_glb)
    P2, T2, CN2 = read_glb_mesh(out_glb)
    rep["file_after"] = topo_metrics(P2, T2, CN2, a.weld)
    rep["file_after_path"] = out_glb

    if not a.no_render:
        t0 = time.time()
        rdir = guarded_out(os.path.join(out, "renders"))
        cam = setup_stage(a.res, a.samples)
        views = frame_views(ob)
        clay = make_mat("clay", (0.42, 0.41, 0.4), cull=True)
        diag = make_mat("diag", (1, 1, 1), cull=False, attr="diag")
        files = {}
        files["before_tex"] = render_set(cam, views, ob, [fixed], rdir, "before_tex")
        files["after_tex"] = render_set(cam, views, fixed, [ob], rdir, "after_tex")
        files["before_clay"] = render_set(cam, views, ob, [fixed], rdir, "before_clay", clay)
        files["after_clay"] = render_set(cam, views, fixed, [ob], rdir, "after_clay", clay)
        # diagnostics: before = faces whose winding opposes their stored normal (red);
        # after = faces the repair flipped (red); both: non-manifold (orange), open boundary (blue)
        grey, red, orange, blue = (0.6, 0.6, 0.6, 1), (0.9, 0.08, 0.05, 1), (1.0, 0.55, 0.0, 1), (0.1, 0.3, 1.0, 1)
        Pt = P[T]
        opp = ((np.cross(Pt[:, 1] - Pt[:, 0], Pt[:, 2] - Pt[:, 0]) * CN.mean(1)).sum(1) < 0)
        nm, bd = face_flags(P, T, a.weld)
        col = np.tile(grey, (len(T), 1))
        col[opp] = red; col[nm] = orange; col[bd] = blue
        paint_faces(ob, col)
        files["before_diag"] = render_set(cam, views, ob, [fixed], rdir, "before_diag", diag)
        nm1, bd1 = face_flags(P1, T1, a.weld)
        col = np.tile(grey, (len(T1), 1))
        col[flipped] = red; col[nm1] = orange; col[bd1] = blue
        paint_faces(fixed, col)
        files["after_diag"] = render_set(cam, views, fixed, [ob], rdir, "after_diag", diag)
        ground = bpy.data.objects["ground"]
        rep["see_through_under_culling"] = {
            "before": see_through(cam, views, ob, [fixed], ground, rdir, "before"),
            "after": see_through(cam, views, fixed, [ob], ground, rdir, "after")}
        rep["renders"] = files
        rep["render_seconds"] = round(time.time() - t0, 1)
        rep["views_az_el_deg"] = [(v, az, el) for v, az, el in VIEWS]

    with open(os.path.join(out, f"{aid}_topology_report.json"), "w", encoding="utf-8") as fh:
        json.dump(rep, fh, indent=1)
    return rep


def main():
    a = parse_args()
    guarded_out(a.out)
    for aid in a.asset:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        rep = run_asset(aid, a)
        b, r = rep["blender_before"]["welded"], rep["blender_after"]["welded"]
        print(f"TOPO {aid}: tris {rep['blender_before']['tris']}->{rep['blender_after']['tris']} "
              f"verts {rep['blender_before']['verts']}->{rep['blender_after']['verts']} "
              f"welded boundary {b['boundary_share_pct']}%->{r['boundary_share_pct']}% "
              f"nonmanifold {b['nonmanifold_edges_gt2']}->{r['nonmanifold_edges_gt2']} "
              f"conflicts {b['winding_conflicts_repeated_directed_edges']}->"
              f"{r['winding_conflicts_repeated_directed_edges']} comps {b['components']}->{r['components']} "
              f"(on 2-face edges {b['winding_conflicts_on_2face_edges']}->{r['winding_conflicts_on_2face_edges']}) "
              f"flipped {rep['repair_log']['faces_flipped_by_recalc']} "
              f"inward-by-parity {rep['inward_facing_by_ray_parity']['before_pct']}%->"
              f"{rep['inward_facing_by_ray_parity']['after_pct']}%")
        for side in ("before", "after"):
            for row in rep.get("see_through_under_culling", {}).get(side, []):
                print(f"TOPO   see-through {side} {row['view']}: {row['see_through_pct']}%")


if __name__ == "__main__":
    main()
