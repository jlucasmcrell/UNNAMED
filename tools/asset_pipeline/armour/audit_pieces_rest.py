"""Rest-pose (or one clip frame) piece-level intersection breakdown between pieces on different bones.
blender -b --factory-startup -P audit_pieces_rest.py -- <build.blend> <build_report.json> [clip.glb@frame]
"""
import bpy, sys, json
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=argv[0])
rep = json.load(open(argv[1]))
pnames = list(rep["piece_tris"].keys())
body = next(o for o in bpy.data.objects if o.type == "MESH")
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
if len(argv) > 2:
    clip, fr = argv[2].rsplit("@", 1)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=clip)
    new = [o for o in bpy.data.objects if o not in before]
    act = next(o for o in new if o.type == "ARMATURE").animation_data.action
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data_create()
    arm.animation_data.action = act
    arm.animation_data.action_slot = act.slots[0]
    f = float(fr)
    bpy.context.scene.frame_set(int(f), subframe=f - int(f))
me = body.data
names = [m.name for m in me.materials]
pid = [0] * len(me.vertices)
me.attributes["piece_id"].data.foreach_get("value", pid)
me.calc_loop_triangles()
dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg)
em = ev.to_mesh()
co = [ev.matrix_world @ v.co for v in em.vertices]
ev.to_mesh_clear()
by_piece = {}
for lt in me.loop_triangles:
    kind = names[lt.material_index]
    if kind in ("k_void", "k_cloth"):
        continue
    by_piece.setdefault(pid[lt.vertices[0]], []).append(tuple(lt.vertices))
trees = {p: BVHTree.FromPolygons(co, t, all_triangles=True) for p, t in by_piece.items()}
keys = sorted(trees)
res = []
for i, a in enumerate(keys):
    for b in keys[i + 1:]:
        if rep["piece_bone"][pnames[a]] == rep["piece_bone"][pnames[b]]:
            continue
        n = len(trees[a].overlap(trees[b]))
        if n:
            res.append((n, pnames[a], pnames[b]))
res.sort(reverse=True)
for n, a, b in res:
    print("PAIR %5d  %-22s %-22s" % (n, a, b))
print("TOTAL", sum(r[0] for r in res))
