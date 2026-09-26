"""Blender: the production mesh from a rigged reconstruction - the enclosed inner shell removed, small holes filled, the
skin weights carried along.

The image-to-3D step meshes an unsigned distance field (the template's RemeshMesh "udf"), which wraps every surface
in a closed thin shell: an outer skin facing out and an inner one a voxel or two inside it, facing in. On the Phase-A
player 20,942 of 39,586 triangles (2.61 of 5.13 m2) are that inner skin - enclosed, never visible from outside - so
half the triangle budget and half the texture atlas were spent on nothing. Here a face is interior when no ray from it
(across its outward hemisphere) escapes the mesh; large connected sets of interior faces are deleted (a small set is a
crevice, kept), small holes left where the two skins met are filled. (--subdivide adds smooth subdivision on the head and hands; off by
default - subdividing a reconstruction's face only exposes its lumps: the high-detail head comes from head_graft.py.)

    blender --background --factory-startup --python clean_mesh.py -- --input rigged.glb --out clean.glb
        [--rays 64] [--min-interior 200] [--subdivide 0] [--subdivide-groups head,neck,hand.L,hand.R] [--report r.json]
"""
import argparse
import json
import os
import sys

import bmesh
import bpy
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(__file__))
from bl_common import remove_interior  # noqa: E402


def args():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--rays", type=int, default=64)
    ap.add_argument("--min-interior", type=int, default=200, help="smallest connected interior set deleted")
    ap.add_argument("--fill-edges", type=int, default=16, help="holes up to this many edges are filled")
    ap.add_argument("--subdivide", type=int, default=0)
    ap.add_argument("--subdivide-groups", default="head,neck,hand.L,hand.R")
    ap.add_argument("--report")
    return ap.parse_args(sys.argv[sys.argv.index("--") + 1:])


def main():
    a = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=a.input)
    obj = [o for o in bpy.data.objects if o.type == "MESH" and o.vertex_groups][0]
    me = obj.data
    report = {"input_faces": len(me.polygons)}
    groups = {g.index: g.name for g in obj.vertex_groups}

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    assert obj.matrix_world == Matrix.Identity(4), "expected the skinned mesh at identity"
    deleted, area, small = remove_interior(bm, rays=a.rays, min_interior=a.min_interior)
    report.update(deleted_faces=deleted, deleted_area_m2=round(area, 3), kept_crevice_faces=small)

    boundary = [e for e in bm.edges if len(e.link_faces) == 1]
    report["open_edges_after_delete"] = len(boundary)
    if boundary:
        filled = bmesh.ops.holes_fill(bm, edges=boundary, sides=a.fill_edges)
        report["holes_filled_faces"] = len(filled["faces"])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.normal_update()
    report["open_edges"] = sum(1 for e in bm.edges if len(e.link_faces) == 1)

    # More geometry where the camera gets close: smooth subdivision of the head and hands.
    deform = bm.verts.layers.deform.active
    wanted = {i for i, n in groups.items() if n in set(a.subdivide_groups.split(","))}
    for _ in range(a.subdivide):
        faces = [fc for fc in bm.faces
                 if sum(sum(w for gi, w in v[deform].items() if gi in wanted) for v in fc.verts) / len(fc.verts) > 0.5]
        edges = list({e for fc in faces for e in fc.edges})
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, smooth=0.6, use_grid_fill=True, use_smooth_even=True)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
    report["subdivided_faces"] = len(faces) if a.subdivide else 0
    for fc in bm.faces:
        fc.smooth = True
    bm.normal_update()
    # Weights: normalised to four influences (the subdivision interpolates them).
    for v in bm.verts:
        d = v[deform]
        top = sorted(d.items(), key=lambda kv: -kv[1])[:4]
        total = sum(w for _, w in top) or 1.0
        for gi in list(d.keys()):
            del d[gi]
        for gi, w in top:
            d[gi] = w / total
    bm.to_mesh(me)
    report["faces"] = len(me.polygons)
    report["vertices"] = len(me.vertices)
    bm.free()
    me.normals_domain  # noqa: B018 (touch so the mesh recomputes normals before export)

    bpy.ops.export_scene.gltf(filepath=a.out, export_format="GLB", use_selection=False, export_apply=False, export_skins=True)
    if a.report:
        json.dump(report, open(a.report, "w"), indent=1)
    print("CLEAN_MESH_RESULT " + json.dumps(report))


main()
