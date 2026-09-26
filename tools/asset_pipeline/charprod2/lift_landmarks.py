"""The reference's face landmarks as 3D points on the source character's own mesh (charprod2), for fitting MPFB's face targets to real
geometry rather than to a photograph's inferred depth: the source registered to the MPFB body (hybrid_fit.py's torso shift and head
fit), and each MediaPipe landmark of the source's render (solve_face_fit.py landmarks --reference) cast through the face fit's
orthographic camera onto its skin and eyes.

    blender -b --python lift_landmarks.py -- --source source.glb --fit fitted_fit.json --dir <facefit dir> [--skin-material MAT_player_skin]

Writes <dir>/source_points.json: {"points": [[x, y, z] | null, ...]} in the landmarks' order (MediaPipe's 478).
"""
import json
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
arg = lambda k, d=None: argv[argv.index(k) + 1] if k in argv else d  # noqa: E731
folder = os.path.abspath(arg("--dir"))
skin_mat = arg("--skin-material", "MAT_player_skin")
fit = json.load(open(arg("--fit")))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(arg("--source")))
src = next(o for o in bpy.data.objects if o.type == "MESH" and o.data.materials and "LOD" not in o.name)
world = src.matrix_world.copy()
for m in list(src.modifiers):
    src.modifiers.remove(m)
src.data.transform(world)
src.data.transform(Matrix.Translation(Vector(fit["torso_shift_m"])))
src.data.transform(Matrix(fit["head_T"]))
face = {i for i, m in enumerate(src.data.materials) if m and (m.name.startswith(skin_mat) or m.name.lower().startswith("eye"))}
bvh = BVHTree.FromPolygons([v.co for v in src.data.vertices], [tuple(p.vertices) for p in src.data.polygons if p.material_index in face])
cam = json.load(open(os.path.join(folder, "probe.json")))["camera"]
cx, cy, cz = cam["center"]
landmarks = json.load(open(os.path.join(folder, "lm_reference.json")))["landmarks"]
pts = []
for px, py, _ in landmarks:
    x = cx + (px / cam["res"] - 0.5) * cam["scale"]
    z = cz + (0.5 - py / cam["res"]) * cam["scale"]
    hit = bvh.ray_cast(Vector((x, cy - 1.0, z)), Vector((0, 1, 0)), 3.0)
    pts.append(list(hit[0]) if hit[0] is not None else None)
json.dump({"points": pts}, open(os.path.join(folder, "source_points.json"), "w"))
print("LIFT_LANDMARKS", sum(p is not None for p in pts), "of", len(pts))
