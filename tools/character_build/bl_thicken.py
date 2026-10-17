"""WP17 outfit v8: give a draped single-sheet garment its material's thickness - seams welded, Solidify inward (the draped surface stays the
outside), with the open edges (hem, armholes, neckline) closed by a rim - and write it back as the same bl_garment.py source.

blender -b --factory-startup --python bl_thicken.py -- --source <drape .glb> --out <.glb> [--mm 3]
"""
import argparse
import sys

import bmesh
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
ap = argparse.ArgumentParser()
ap.add_argument("--source", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--mm", type=float, default=3.0)
ap.add_argument("--weld-mm", type=float, default=4.0)
a = ap.parse_args(argv)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=a.source)
g = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = g
g.select_set(True)
# the drape exports its panels' seams as coincident vertex pairs: welded first, or the thickness splits open along every seam
bm = bmesh.new()
bm.from_mesh(g.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=a.weld_mm / 1000)
bm.to_mesh(g.data)
bm.free()
sol = g.modifiers.new("thickness", "SOLIDIFY")
sol.thickness, sol.offset, sol.use_rim, sol.use_quality_normals = a.mm / 1000, -1.0, True, True
bpy.ops.object.modifier_apply(modifier=sol.name)
bpy.ops.export_scene.gltf(filepath=a.out, use_selection=True, export_format="GLB", export_animations=False)
print("THICK", len(g.data.vertices), "vertices", sum(len(p.vertices) - 2 for p in g.data.polygons), "tris")
