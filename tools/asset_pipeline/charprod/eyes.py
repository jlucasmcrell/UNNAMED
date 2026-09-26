"""Blender: real eyes for a reconstructed character - the lid openings cut where the concept's eyelids are, eyeballs set
behind them, the sockets closed.

The reconstruction has no eyes: the eye region is one smooth surface with the eyes painted on (dark blobs in its own
texture). Here, per eye:
  * the mesh around the eye is refined (the reconstruction's ~4 mm triangles cannot hold a 28 x 10 mm opening);
  * the faces inside the concept's lid contour (MediaPipe landmarks, landmarks.py) are removed and the opening's rim is
    snapped onto that contour as the concept camera sees it;
  * an eyeball (--eye-radius, 12 mm) is placed behind the opening: the camera ray through the concept's iris centre
    hits the surface; the ball's front sits --recess behind that point, gazing back along the ray (the concept's
    eyes look at its camera);
  * the rim is extruded onto the eyeball (a lid wall), so no gap shows between lid and ball.
The eyeballs go to their own GLB (--eyes-out, UV = the gaze-axis orthographic projection that eye_texture.py paints),
the mesh with its openings to --out.

    blender --background --factory-startup --python eyes.py -- --input mesh.glb --camera camera.json
        --landmarks landmarks.json --out mesh_eyes.glb --eyes-out eyeballs.glb [--report r.json]
"""
import argparse
import json
import math
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--camera", required=True)
    ap.add_argument("--landmarks", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--eyes-out", required=True)
    ap.add_argument("--eye-radius", type=float, default=0.012)
    ap.add_argument("--recess", type=float, default=0.0008, help="the ball's front behind the surface at the iris")
    ap.add_argument("--refine-radius", type=float, default=0.024)
    ap.add_argument("--refine", type=int, default=2)
    ap.add_argument("--head-group", default="head")
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def to_blender(p):
    return Vector((p[0], -p[2], p[1]))


def to_gltf(v):
    return np.array([v.x, v.z, -v.y])


def inside(poly, x, y):
    hit = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi:
            hit = not hit
        j = i
    return hit


def nearest_on_polyline(poly, p):
    best, bd = None, 1e18
    for i in range(len(poly)):
        a, b = np.array(poly[i]), np.array(poly[(i + 1) % len(poly)])
        ab = b - a
        t = np.clip(np.dot(p - a, ab) / max(np.dot(ab, ab), 1e-12), 0, 1)
        q = a + t * ab
        d = np.sum((p - q) ** 2)
        if d < bd:
            best, bd = q, d
    return best


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    for o in [o for o in bpy.data.objects if o.type == "MESH" and o is not obj]:
        bpy.data.objects.remove(o)
    arm = obj.find_armature()
    me = obj.data
    mw = obj.matrix_world
    assert mw == Matrix.Identity(4), "expected the skinned mesh at identity"
    cam = json.load(open(a.camera))
    R, t, f, c = np.array(cam["R"]), np.array(cam["t"]), cam["f"], np.array(cam["c"])
    C = to_blender(-R.T @ t)
    marks = json.load(open(a.landmarks))

    def ray(px):
        d = R.T @ np.array([(px[0] - c[0]) / f, (px[1] - c[1]) / f, 1.0])
        return to_blender(d / np.linalg.norm(d))

    def project(v):
        q = R @ to_gltf(v) + t
        return np.array([f * q[0] / q[2] + c[0], f * q[1] / q[2] + c[1]]), q[2]

    def unproject(px, z):
        q = np.array([(px[0] - c[0]) / f * z, (px[1] - c[1]) / f * z, z])
        return to_blender(R.T @ (q - t))

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bm.normal_update()
    report = {"eyes": {}}
    eyes = {}
    for side, e in marks["eyes"].items():
        bvh = BVHTree.FromBMesh(bm)
        d = ray(e["iris_centre"])
        hit = bvh.ray_cast(C, d, 10.0)
        assert hit[0] is not None, f"eye {side}: the iris ray misses the mesh"
        H = hit[0]
        E = H + d * (a.eye_radius + a.recess)
        eyes[side] = (E, -d)
        # Refine around the eye.
        for _ in range(a.refine):
            near = [fc for fc in bm.faces if (fc.calc_center_median() - H).length < a.refine_radius]
            edges = list({ed for fc in near for ed in fc.edges})
            bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True)
            bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.normal_update()
        bvh = BVHTree.FromBMesh(bm)
        contour = e["contour"]
        doomed = []
        for fc in bm.faces:
            p = fc.calc_center_median()
            if (p - H).length > a.refine_radius or fc.normal.dot((C - p).normalized()) < 0.1:
                continue
            px, _ = project(p)
            if not inside(contour, px[0], px[1]):
                continue
            v = (p - C)
            h = bvh.ray_cast(C, v.normalized(), v.length + 1e-3)
            if h[2] == fc.index or (h[0] is not None and h[3] >= v.length - 2e-3):
                doomed.append(fc)
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        # The opening's rim: boundary edges near this eye; its vertices snapped onto the contour.
        rim = [ed for ed in bm.edges if len(ed.link_faces) == 1 and (ed.verts[0].co - H).length < a.refine_radius]
        rim_verts = {v for ed in rim for v in ed.verts}
        for v in rim_verts:
            px, z = project(v.co)
            v.co = unproject(nearest_on_polyline(contour, px), z)
        # The lid wall: the rim extruded onto the eyeball, just inside its surface.
        ext = bmesh.ops.extrude_edge_only(bm, edges=rim)
        for v in [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]:
            v.co = E + (v.co - E).normalized() * (a.eye_radius - 0.0004)
        report["eyes"][side] = {"hit": list(H), "centre": list(E), "removed_faces": len(doomed), "rim_edges": len(rim)}
    # No whole-mesh normal recalculation: on an open reconstruction it flips whole regions inward. The lid wall takes
    # its winding from the rim it was extruded from.
    bm.normal_update()
    for fc in bm.faces:
        fc.smooth = True
    bm.to_mesh(me)
    bm.free()
    report["interocular_m"] = round((eyes["L"][0] - eyes["R"][0]).length, 4)
    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True)

    # The eyeballs: UV spheres around the gaze axis, UV = orthographic projection along the gaze (front hemisphere).
    for o in list(bpy.data.objects):
        if o.type == "MESH":
            bpy.data.objects.remove(o)
    for side, (E, gaze) in eyes.items():
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=24, radius=a.eye_radius)
        rot = Vector((0, 0, 1)).rotation_difference(gaze).to_matrix().to_4x4()
        uvl = bm.loops.layers.uv.new("UVMap")
        for fc in bm.faces:
            for l in fc.loops:
                n = l.vert.co.normalized()
                # front hemisphere onto the disk; the back folds to the rim (sclera)
                k = 0.5 if n.z >= 0 else 0.5 * (2 - 1e-3)
                s = math.sqrt(max(n.x * n.x + n.y * n.y, 1e-12))
                rr = s if n.z >= 0 else 1.0
                l[uvl].uv = (0.5 + 0.5 * rr * n.x / s * (1 if s > 1e-6 else 0), 0.5 + 0.5 * rr * n.y / s * (1 if s > 1e-6 else 0))
        bm.transform(Matrix.Translation(E) @ rot)
        emesh = bpy.data.meshes.new(f"eye.{side}")
        bm.to_mesh(emesh)
        bm.free()
        eo = bpy.data.objects.new(f"eye.{side}", emesh)
        bpy.context.scene.collection.objects.link(eo)
        for p in emesh.polygons:
            p.use_smooth = True
        eo["gaze"] = list(gaze)
        eo["centre"] = list(E)
    for o in bpy.data.objects:
        o.select_set(o.type == "MESH")
    bpy.ops.export_scene.gltf(filepath=a.eyes_out, export_format="GLB", use_selection=True, export_extras=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("EYES_RESULT " + json.dumps(report))


main()
