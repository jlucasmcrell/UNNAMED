"""A prepared size variant of a scanned or sculpted rock (Phase B demo): the source model uniformly scaled so its footprint covers a
target width (rocks have no semantic size; the source asset itself is never changed), exported as a NEW asset id with an LOD chain
(decimated 50 % / 20 % / 7 %) and a meta record naming its source and scale.

    blender -b --python prepare_scaled_variant.py -- --source <id> --id <new id> --width <m> [--min-height <m>] [--assets <root>]
    blender -b --python prepare_scaled_variant.py -- --file <depot .gltf/.glb> --id <new id> [--width 0]   (a depot model as it is)

With --width 0 the model keeps its authored size (a depot model prepared as it is: base centred on the origin, LODs, meta).
"""
import json
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
assets = arg("--assets", "G:/UNNAMED_PHASEB/assets")
src, new, file = arg("--source"), arg("--id"), arg("--file")
width, min_height = float(arg("--width", "0")), float(arg("--min-height", "0"))
out_dir = os.path.join(assets, "ready", new)
os.makedirs(out_dir, exist_ok=True)


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=file or os.path.join(assets, "ready", src, src + ".glb"))
    return [o for o in bpy.data.objects if o.type == "MESH"]


meshes = load()
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
size = hi - lo
# Blender Z is up here (glTF Y): the footprint is X and Y.
# The footprint's mean side meets the target: an irregular rock neither overhangs its footprint far on its long side nor leaves much
# invisible collision on its short one.
scale = max(width / max((size.x + size.y) / 2, 1e-3), min_height / max(size.z, 1e-3), 1.0) if width > 0 else 1.0
meta = {"id": new, "source": src or file, "scale": round(scale, 4), "authored_size_m": [round(v * scale, 3) for v in (size.x, size.y, size.z)],
        "how": "uniform scale of the source (a rock's size is a production choice), LODs by collapse decimation", "lods": {}}
for level, ratio in ((0, 1.0), (1, 0.5), (2, 0.2), (3, 0.07)):
    meshes = load()
    root = bpy.data.objects.new("root", None)
    bpy.context.scene.collection.objects.link(root)
    for o in meshes:
        if o.parent is None:
            o.parent = root
    root.scale = (scale, scale, scale)
    root.location = (-(lo.x + hi.x) / 2 * scale, -(lo.y + hi.y) / 2 * scale, -lo.z * scale)   # base centred on the origin
    bpy.context.view_layer.update()
    tris = 0
    for o in meshes:
        if ratio < 1.0:
            m = o.modifiers.new("lod", "DECIMATE")
            m.ratio = ratio
            m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        for mod in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
        tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    name = new + (f"_lod{level}" if level else "") + ".glb"
    bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, name), export_format="GLB", export_yup=True, export_apply=True)
    meta["lods"][name] = tris
json.dump(meta, open(os.path.join(out_dir, new + "_meta.json"), "w"), indent=1)
print("SCALED_VARIANT", json.dumps(meta))
