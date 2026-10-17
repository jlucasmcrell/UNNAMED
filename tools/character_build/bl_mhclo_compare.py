"""WP17 comparison: MakeHuman-style proxy binding (the .mhclo method Humanizer and MPFB use) against bl_cards.refit's per-triangle
frame field, for carrying a garment from the Average body to a build. Written from the documented .mhclo method, not from Humanizer's
code (AGPL-3.0, so nothing of it is copied).

blender -b --factory-startup --python bl_mhclo_compare.py -- --source <garment glb on the fit body> --fit mh_veth_m
    --target mh_veth_m_very_heavy --assembled <work/assembled> --out <garment glb, on the target, unskinned>

.mhclo binding: each garment vertex is the closest point's triangle on the fit body (three vertices and barycentric weights) plus an
offset kept in world axes, scaled per axis by the body's extent ratio (MakeHuman's x/y/z_scale reference pairs). Carried to the target
by the same weights and the scaled offset. The frame field instead turns the offset with the triangle's own frame. The output then
goes through bl_garment.py with its fit set to the target (no further refit), so only the binding differs between the two routes.
"""
import argparse
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
for k in ("source", "fit", "target", "assembled", "out"):
    ap.add_argument("--" + k, required=True)
a = ap.parse_args(argv)


def body(asset):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(a.assembled, asset, f"{asset}_rigged.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    mesh = next(o for o in new if o.type == "MESH" and o.name.endswith("_body"))
    verts = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
    tris = [tuple(p.vertices) for p in mesh.data.polygons]
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return verts, tris


bpy.ops.wm.read_factory_settings(use_empty=True)
fit_v, tris = body(a.fit)
tgt_v, _ = body(a.target)
extent = lambda vs: Vector([max(v[i] for v in vs) - min(v[i] for v in vs) for i in range(3)])
scale = Vector([t / f for t, f in zip(extent(tgt_v), extent(fit_v))])
bvh = BVHTree.FromPolygons(fit_v, tris)
bpy.ops.import_scene.gltf(filepath=a.source)
garments = [o for o in bpy.data.objects if o.type == "MESH"]
for g in garments:
    mw, inv = g.matrix_world, g.matrix_world.inverted()
    for v in g.data.vertices:
        p = mw @ v.co
        hit, _, face, _ = bvh.find_nearest(p)
        i, j, k = tris[face]
        on_target = barycentric_transform(hit, fit_v[i], fit_v[j], fit_v[k], tgt_v[i], tgt_v[j], tgt_v[k])
        offset = p - hit
        v.co = inv @ (on_target + Vector([offset[n] * scale[n] for n in range(3)]))
bpy.ops.object.select_all(action="DESELECT")
for g in garments:
    g.select_set(True)
bpy.ops.export_scene.gltf(filepath=a.out, use_selection=True, export_format="GLB", export_skins=False, export_animations=False)
print("MHCLO", a.out, "axis scale", tuple(round(s, 3) for s in scale))
