"""Blender: graft a high-detail head onto a character body - the high-detail source for the face and hair.

The full-body reconstruction spends ~200 voxels on the whole head; its face is soft and lumpy and its hair a solid
shell. The same concept, cropped to the head and reconstructed on its own (same seed and tier), gets the whole voxel
grid: a formed face and hair as separate strand clumps. Here that head replaces the body's:
  * its inner shell is removed (as clean_mesh does for the body);
  * it is placed by the two cameras (both reconstructions are pixel-aligned to one concept): their rotation, the scale
    their pixel footprints imply at the neck's depth, and a translation fitted by nearest-point iterations to the body
    just above the seam (--align seam). The body's face - soft, its depths wrong - is not used to place it; a
    similarity to it (--align face, every MediaPipe landmark ray-cast onto both) lands the shoulders centimetres off;
  * the seam is at the neck (--cut-below-chin under the chin landmark): the head part keeps only the piece holding its
    face (its bust's vest and shoulders are separate shells), and the body loses what is connected to its old face
    above the chin and inside a cylinder a little wider than the new neck down to the seam - its crew collar and
    shoulders, which the reconstruction fused into the neck, stay whole instead of being chopped into a ledge;
  * the two rings are bridged and the bridge relaxed into a collar;
  * the head's vertices take the skin weights of the body's original head surface nearest them;
  * the head keeps its own UVs and maps as a second material ("head_src") so the transfer bake reads each part's maps,
    and its own camera, carried into the grafted world (--head-camera-out), so its projection and eyes register on
    its own geometry. The body's material is "body_src".

    blender --background --factory-startup --python head_graft.py -- --body clean.glb --head head_raw.glb
        --camera camera.json --head-camera head_camera.json --landmarks landmarks.json --crop head_crop.json
        --out grafted.glb --head-camera-out head_camera_world.json [--report r.json]
"""
import argparse
import json
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
from bl_common import boundary_loops, remove_interior  # noqa: E402


def args():
    ap = argparse.ArgumentParser()
    for k in ("body", "head", "camera", "head_camera", "landmarks", "crop", "out", "head_camera_out"):
        ap.add_argument("--" + k.replace("_", "-"), required=True)
    ap.add_argument("--cut-below-chin", type=float, default=0.035, help="the seam plane under the chin landmark")
    ap.add_argument("--collar-gap", type=float, default=0.015, help="the body's opening is this much wider than the new neck")
    ap.add_argument("--relax", type=int, default=6, help="smoothing passes over the bridge")
    ap.add_argument("--min-fragment", type=int, default=60, help="loose pieces near the head smaller than this are dropped")
    ap.add_argument("--align", choices=("seam", "face"), default="seam",
                    help="seam: the cameras' scale, translation fitted at the seam; face: a similarity to the body's face landmarks")
    ap.add_argument("--head-group", default="head")
    ap.add_argument("--neck-group", default="neck")
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def to_blender(p):
    return Vector((p[0], -p[2], p[1]))


class Camera:
    def __init__(self, path):
        c = json.load(open(path))
        self.R, self.t, self.f, self.c = np.array(c["R"]), np.array(c["t"]), c["f"], np.array(c["c"])
        self.centre = -self.R.T @ self.t

    def ray(self, px, M=Matrix.Identity(4)):
        """Origin and direction in Blender space of the ray through px; M maps the camera's world into Blender's."""
        d = self.R.T @ np.array([(px[0] - self.c[0]) / self.f, (px[1] - self.c[1]) / self.f, 1.0])
        o = M @ to_blender(self.centre)
        e = M @ to_blender(self.centre + d)
        return o, (e - o).normalized()


def umeyama(src, dst):
    ms, md = src.mean(0), dst.mean(0)
    s, d = src - ms, dst - md
    U, S, Vt = np.linalg.svd(d.T @ s / len(src))
    D = np.eye(3)
    if np.linalg.det(U) * np.linalg.det(Vt) < 0:
        D[2, 2] = -1
    Rm = U @ D @ Vt
    scale = np.trace(np.diag(S) @ D) / (s ** 2).sum(1).mean()
    return scale, Rm, md - scale * Rm @ ms


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.body)
    body = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not body]:
        bpy.data.objects.remove(o)
    arm = body.find_armature()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=a.head)
    head = [o for o in set(bpy.data.objects) - before if o.type == "MESH"][0]
    for o in [o for o in set(bpy.data.objects) - before if o is not head]:
        bpy.data.objects.remove(o)
    report = {}

    # The head in its own world space, welded, its inner shell removed.
    hm = head.matrix_world.copy()
    hb = bmesh.new()
    hb.from_mesh(head.data)
    bmesh.ops.remove_doubles(hb, verts=hb.verts[:], dist=1e-7)
    hb.transform(hm)
    deleted, area, _ = remove_interior(hb)
    report["head_interior_deleted"] = deleted

    # Landmark correspondences.
    marks = json.load(open(a.landmarks))
    crop = json.load(open(a.crop))
    cam, hcam = Camera(a.camera), Camera(a.head_camera)
    bb = bmesh.new()
    bb.from_mesh(body.data)
    # Welded: the GLB splits vertices at every UV seam, which would cut the neck ring into pieces.
    bmesh.ops.remove_doubles(bb, verts=bb.verts[:], dist=1e-6)
    bb.transform(body.matrix_world)
    bb.normal_update()
    bvh_body = BVHTree.FromBMesh(bb)
    hb.normal_update()
    bvh_head = BVHTree.FromBMesh(hb)
    src, dst = [], []
    for px in marks["all"]:
        o, d = cam.ray(px)
        hit_b = bvh_body.ray_cast(o, d, 10.0)
        hpx = ((px[0] - crop["x0"]) * crop["out"] / crop["size"], (px[1] - crop["y0"]) * crop["out"] / crop["size"])
        o2, d2 = hcam.ray(hpx)
        hit_h = bvh_head.ray_cast(o2, d2, 10.0)
        if hit_b[0] is None or hit_h[0] is None:
            continue
        src.append(np.array(hit_h[0]))
        dst.append(np.array(hit_b[0]))
    src, dst = np.array(src), np.array(dst)
    # The rotation is the two cameras' (both reconstructions are pixel-aligned to one concept seen from one direction);
    # a free fit would tilt the head to match the body face's wrong depths. Scale and translation by least squares.
    A = np.array([[1.0, 0, 0], [0, 0, -1], [0, 1, 0]])  # glTF -> Blender axes
    Rm = A @ (cam.R.T @ hcam.R) @ A.T

    def fit(sel):
        xs, xd = (Rm @ src[sel].T).T, dst[sel]
        ms, md = xs.mean(0), xd.mean(0)
        k = ((xs - ms) * (xd - md)).sum() / ((xs - ms) ** 2).sum()
        return k, md - k * ms

    keep = np.ones(len(src), bool)
    for _ in range(2):  # the second pass drops the worst tenth (silhouette landmarks hit different depths)
        scale, tr = fit(keep)
        res = np.linalg.norm(scale * (Rm @ src.T).T + tr - dst, axis=1)
        keep = res <= np.percentile(res, 90)
    free_scale, free_R, _ = umeyama(src[keep], dst[keep])
    report.update(landmark_pairs=int(len(src)), scale=float(scale), rms_m=float(np.sqrt((res[keep] ** 2).mean())),
                  camera_rotation_deg=float(np.degrees(np.arccos(np.clip((np.trace(Rm) - 1) / 2, -1, 1)))),
                  free_fit_rotation_deg=float(np.degrees(np.arccos(np.clip((np.trace(free_R @ Rm.T) - 1) / 2, -1, 1)))))
    if a.align == "seam":
        # The scale the two cameras imply (the head part's lateral footprint in the concept is the body's: both are
        # pixel-aligned to it), taken at the depths where the parts meet (below the chin); the translation fitted by
        # nearest-point iterations on the head part's band just above the seam. The body's face - soft, lumpy, its
        # depths wrong - takes no part: the landmark fit above uses it and lands the shoulders centimetres off.
        A_inv = A.T
        chin_y = marks["all"][152][1]
        zb, zh = [], []
        for gy in np.linspace(chin_y, crop["y0"] + crop["size"], 40):
            for gx in np.linspace(crop["x0"], crop["x0"] + crop["size"], 40):
                o, d = cam.ray((gx, gy))
                h1 = bvh_body.ray_cast(o, d, 10.0)
                o2, d2 = hcam.ray(((gx - crop["x0"]) * crop["out"] / crop["size"], (gy - crop["y0"]) * crop["out"] / crop["size"]))
                h2 = bvh_head.ray_cast(o2, d2, 10.0)
                if h1[0] is not None and h2[0] is not None:
                    zb.append((cam.R @ (A_inv @ np.array(h1[0])) + cam.t)[2])
                    zh.append((hcam.R @ (A_inv @ np.array(h2[0])) + hcam.t)[2])
        scale = float(np.median(zb) * crop["size"] * hcam.f / (cam.f * crop["out"] * np.median(zh)))
        o, d = cam.ray(marks["all"][152])
        z_band = bvh_body.ray_cast(o, d, 10.0)[0].z - a.cut_below_chin
        pts = [scale * (Rm @ np.array(v.co)) + tr for v in hb.verts]
        band = np.array([p for p in pts if z_band < p[2] < z_band + 0.06])
        for _ in range(8):
            near = np.array([np.array(bvh_body.find_nearest(Vector(p))[0]) for p in band])
            step = (near - band).mean(0)
            band = band + step
            tr = tr + step
        report.update(align="seam", camera_scale=scale, seam_band_points=int(len(band)),
                      seam_residual_m=float(np.linalg.norm(near - band, axis=1).mean()))
    Rm = Rm * scale
    M = Matrix.Translation(Vector(tr)) @ Matrix([list(r) + [0] for r in Rm] + [[0, 0, 0, 1]])
    hb.transform(M)
    hb.normal_update()
    report["transform"] = [list(r) for r in M]
    # The head crop's camera carried into the grafted world (glTF axes): with M = T + s.Rr (Blender axes), a head-world
    # point x maps to M.x, so the camera seeing the grafted head as the head crop saw it is R' = R_h.Rr^T,
    # t' = s.t_h - R'.T (a pinhole projection is unchanged by the uniform scale s). The head part is projected and
    # its eyes cut through this camera, onto the head crop, so its texture registers on its own geometry.
    Mn = np.array(M)
    s_uni = float(np.cbrt(np.linalg.det(Mn[:3, :3])))
    Rr_gl = A.T @ (Mn[:3, :3] / s_uni) @ A
    T_gl = A.T @ Mn[:3, 3]
    R2 = hcam.R @ Rr_gl.T
    t2 = s_uni * hcam.t - R2 @ T_gl
    hc = json.load(open(a.head_camera))
    hc.update(R=R2.tolist(), t=t2.tolist(), note="the head crop's camera in the grafted character's world (head_graft.py)")
    json.dump(hc, open(a.head_camera_out, "w"), indent=1)

    # The cut plane: under the chin landmark (MediaPipe 152), on the body.
    o, d = cam.ray(marks["all"][152])
    chin = bvh_body.ray_cast(o, d, 10.0)[0]
    neck_base = arm.matrix_world @ arm.data.bones[a.neck_group].head_local
    z_cut = chin.z - a.cut_below_chin
    report.update(z_chin=chin.z, z_cut=z_cut)

    # The head part is cut under the plane and keeps only the piece holding the face: its neck and head (the bust's
    # vest and shoulders are separate shells from its neck and fall away).
    bmesh.ops.delete(hb, geom=[fc for fc in hb.faces if fc.calc_center_median().z < z_cut], context="FACES")
    hb.faces.ensure_lookup_table()
    bvh_cut = BVHTree.FromBMesh(hb)
    o, d = cam.ray(marks["all"][1])
    seed_h = hb.faces[bvh_cut.ray_cast(o, d, 10.0)[2]]
    keep, stack = set(), [seed_h]
    while stack:
        x = stack.pop()
        if x not in keep:
            keep.add(x)
            stack.extend(l.face for e in x.edges for l in e.link_loops if l.face not in keep)
    bmesh.ops.delete(hb, geom=[fc for fc in hb.faces if fc not in keep], context="FACES")
    bmesh.ops.delete(hb, geom=[v for v in hb.verts if not v.link_faces], context="VERTS")
    hb.verts.ensure_lookup_table()
    report["head_faces"] = len(hb.faces)
    ring = [e for e in hb.edges if len(e.link_faces) == 1 and all(abs(v.co.z - z_cut) < 0.02 for v in e.verts)]
    ring = max(boundary_loops(hb, ring), key=len)
    ring_pts = [v.co for e in ring for v in e.verts]
    axis = Vector((sum(p.x for p in ring_pts) / len(ring_pts), sum(p.y for p in ring_pts) / len(ring_pts), 0))
    r_neck = sum((Vector((p.x, p.y, 0)) - axis).length for p in ring_pts) / len(ring_pts)
    report.update(neck_ring_edges=len(ring), neck_radius_m=r_neck)

    # Skin weights for the head: those of the body's original surface nearest each head vertex (inverse-distance
    # blend of the nearest face's corners), four influences.
    groups = {g.index: g.name for g in body.vertex_groups}
    deform = bb.verts.layers.deform.active
    bb.faces.ensure_lookup_table()
    head_weights = []
    for v in hb.verts:
        loc, _, fi, _ = bvh_body.find_nearest(v.co)
        fc = bb.faces[fi]
        w = [1.0 / ((loc - x.co).length + 1e-5) for x in fc.verts]
        total = sum(w)
        acc = {}
        for x, wi in zip(fc.verts, w):
            for gi, gw in x[deform].items():
                acc[groups[gi]] = acc.get(groups[gi], 0.0) + gw * wi / total
        top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
        total = sum(x for _, x in top) or 1.0
        head_weights.append({k: x / total for k, x in top})

    # The body loses what is connected to its old face (flood fill from the face the nose-tip landmark hits) above the
    # chin, and inside a cylinder a little wider than the new neck down to the seam. Its collar and shoulders outside
    # the cylinder stay whole (the reconstruction fused the crew collar into the neck: a plane through it would leave
    # a chopped ledge).
    o, d = cam.ray(marks["all"][1])
    seed = bb.faces[bvh_body.ray_cast(o, d, 10.0)[2]]
    r_cyl = r_neck + a.collar_gap

    def goes(fc):
        c = fc.calc_center_median()
        return c.z > chin.z - 0.005 or (c.z > z_cut and (Vector((c.x, c.y, 0)) - axis).length < r_cyl)

    doomed, stack = set(), [seed]
    while stack:
        fc = stack.pop()
        if fc in doomed or not goes(fc):
            continue
        doomed.add(fc)
        stack.extend(l.face for e in fc.edges for l in e.link_loops if l.face not in doomed)
    # What of the old head's surface lies inside the new head (an old chin poking out below the plane, outside the
    # cylinder) goes too: a face above the seam behind the new head's surface.
    bvh_new = BVHTree.FromBMesh(hb)
    inside = 0
    for fc in bb.faces:
        if fc in doomed:
            continue
        c = fc.calc_center_median()
        if c.z <= z_cut:
            continue
        loc, nrm, _, dist = bvh_new.find_nearest(c, 0.06)
        if loc is not None and (c - loc).dot(nrm) < 0:
            doomed.add(fc)
            inside += 1
    report["body_faces_inside_new_head"] = inside
    doomed = list(doomed)
    report["body_faces_removed"] = len(doomed)
    bmesh.ops.delete(bb, geom=doomed, context="FACES")
    bmesh.ops.delete(bb, geom=[v for v in bb.verts if not v.link_faces], context="VERTS")

    # Both back into their objects (world space; the body sits at identity), the head's weights as vertex groups.
    assert body.matrix_world == Matrix.Identity(4), "expected the body at identity"
    bb.to_mesh(body.data)
    bb.free()
    head.matrix_world = Matrix.Identity(4)
    hb.to_mesh(head.data)
    hb.free()
    for name in groups.values():
        head.vertex_groups.new(name=name)
    for i, w in enumerate(head_weights):
        for name, x in w.items():
            head.vertex_groups[name].add([i], x, "REPLACE")
    body.data.materials.clear()
    body.data.materials.append(bpy.data.materials.new("body_src"))
    mh = head.data.materials[0] if head.data.materials else bpy.data.materials.new("head_src")
    mh.name = "head_src"
    head.data.materials.clear()
    head.data.materials.append(mh)
    body.data.uv_layers[0].name = "UVMap"
    head.data.uv_layers[0].name = "UVMap"
    n_body_verts = len(body.data.vertices)
    bpy.ops.object.select_all(action="DESELECT")
    head.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    me = body.data

    # Bridge the two neck loops.
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    near = [e for e in bm.edges if len(e.link_faces) == 1 and all(z_cut - 0.02 < v.co.z < chin.z + 0.02 for v in e.verts)
            and all((Vector((v.co.x, v.co.y, 0)) - axis).length < r_cyl + 0.04 for v in e.verts)]
    loops = boundary_loops(bm, near)
    body_loops = [l for l in loops if all(v.index < n_body_verts for e in l for v in e.verts)]
    head_loops = [l for l in loops if all(v.index >= n_body_verts for e in l for v in e.verts)]
    report["neck_loops"] = {"body": [len(l) for l in body_loops], "head": [len(l) for l in head_loops]}
    if body_loops and head_loops:
        bl, hl = max(body_loops, key=len), max(head_loops, key=len)
        made = bmesh.ops.bridge_loops(bm, edges=bl + hl)
        report["bridge_faces"] = len(made["faces"])
        tri = bmesh.ops.triangulate(bm, faces=made["faces"])["faces"]
        # The bridge's winding follows whichever loop bridge_loops started from: face every bridge triangle away from
        # the neck's axis (inward faces render black and take no projection).
        flipped = 0
        for fc in tri:
            fc.normal_update()
            c = fc.calc_center_median()
            if fc.normal.dot(Vector((c.x - axis.x, c.y - axis.y, 0))) < 0:
                fc.normal_flip()
                flipped += 1
        report["bridge_faces_flipped"] = flipped
        # The two rings differ in length (the parts' tessellations differ): relax the bridge and its neighbours so
        # the fan of long triangles becomes a smooth collar.
        ring_verts = {v for fc in tri for v in fc.verts}
        ring_verts |= {n for v in list(ring_verts) for e in v.link_edges for n in e.verts}
        for _ in range(a.relax):
            bmesh.ops.smooth_vert(bm, verts=list(ring_verts), factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
        # Any other loop left at the seam (a sliver the cut left open) is closed.
        rest = [e for l in body_loops + head_loops if l is not bl and l is not hl for e in l if e.is_valid and len(e.link_faces) == 1]
        if rest:
            report["seam_holes_filled"] = len(bmesh.ops.holes_fill(bm, edges=rest, sides=len(rest))["faces"])
    else:
        report["bridge_faces"] = 0
    # Fragments the cuts left floating near the head (a hair tip, a sliver of the old collar) are dropped.
    bm.faces.ensure_lookup_table()
    seen, dropped = set(), 0
    for fc in list(bm.faces):
        if fc in seen or not fc.is_valid:
            continue
        stack, comp = [fc], set()
        while stack:
            x = stack.pop()
            if x not in comp:
                comp.add(x)
                stack.extend(l.face for e in x.edges for l in e.link_loops if l.face not in comp)
        seen |= comp
        if len(comp) < a.min_fragment and max(f.calc_center_median().z for f in comp) > z_cut - 0.15:
            bmesh.ops.delete(bm, geom=list(comp), context="FACES")
            dropped += len(comp)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    report["fragment_faces_dropped"] = dropped
    for fc in bm.faces:
        fc.smooth = True
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    report["faces"] = len(me.polygons)
    report["materials"] = [m.name for m in me.materials]
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1, default=float)
    print("HEAD_GRAFT_RESULT " + json.dumps(report, default=float))

main()
