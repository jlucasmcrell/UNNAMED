"""Interpenetration audit: for sampled frames of each clip, count intersecting triangle pairs between surfaces
riding on DIFFERENT bones (BVH overlap), split into plate-vs-plate (steel/rim/brass/leather) and cloth-involved.
Runs on the build .blend (it keeps the per-kind materials); inner void shells are ignored.
blender -b --factory-startup -P audit_intersections.py -- <build.blend> <clips_dir> <out.json> [samples_per_clip]
"""
import bpy, sys, json, os
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, CLIPS, OUT = argv[0], argv[1], argv[2]
N = int(argv[3]) if len(argv) > 3 else 12
bpy.ops.wm.open_mainfile(filepath=SRC)
body = next(o for o in bpy.data.objects if o.type == "MESH")
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
me = body.data
names = [m.name for m in me.materials]
gname = {g.index: g.name for g in body.vertex_groups}
vbone = [gname[v.groups[0].group] for v in me.vertices]
me.calc_loop_triangles()
tris_by_bone = {}
for lt in me.loop_triangles:
    kind = names[lt.material_index]
    if kind == "k_void":
        continue
    b = vbone[lt.vertices[0]]
    tris_by_bone.setdefault(b, []).append((tuple(lt.vertices), kind != "k_cloth"))
bones = sorted(tris_by_bone)


def audit():
    dg = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(dg)
    em = ev.to_mesh()
    co = [ev.matrix_world @ v.co for v in em.vertices]
    ev.to_mesh_clear()
    trees = {b: BVHTree.FromPolygons(co, [t for t, _ in tris_by_bone[b]], all_triangles=True) for b in bones}
    plate, cloth, pairs = 0, 0, {}
    for i, a in enumerate(bones):
        for b in bones[i + 1:]:
            hits = trees[a].overlap(trees[b])
            if not hits:
                continue
            p = sum(1 for x, y in hits if tris_by_bone[a][x][1] and tris_by_bone[b][y][1])
            plate += p
            cloth += len(hits) - p
            if p:
                pairs[f"{a}|{b}"] = p
    return plate, cloth, pairs


report = {}
arm.animation_data_create()
arm.animation_data.action = None
bpy.context.scene.frame_set(0)
for pb in arm.pose.bones:
    pb.matrix_basis.identity()
report["rest"] = dict(zip(("plate_pairs", "cloth_pairs", "by_bones"), audit()))
print("REST", report["rest"]["plate_pairs"], report["rest"]["cloth_pairs"], report["rest"]["by_bones"])
for clip in ("idle", "walk", "run", "attack", "hit", "death"):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(CLIPS, f"anim.creature.animated_armour.{clip}.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    carm = next(o for o in new if o.type == "ARMATURE")
    act = carm.animation_data.action
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data.action = act
    if act.slots:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    rows = []
    for k in range(N + 1):
        f = f0 + (f1 - f0) * k / N
        bpy.context.scene.frame_set(int(f), subframe=f - int(f))
        p, c, pairs = audit()
        rows.append({"frame": round(f, 2), "plate_pairs": p, "cloth_pairs": c, "by_bones": pairs})
    worst = max(rows, key=lambda r: r["plate_pairs"])
    report[clip] = {"frames": rows, "worst_frame": worst["frame"], "worst_plate_pairs": worst["plate_pairs"]}
    print("CLIP", clip, "plate pairs per frame", [r["plate_pairs"] for r in rows], "worst", worst["frame"], worst["by_bones"])
with open(OUT, "w") as h:
    json.dump(report, h, indent=1)
print("WROTE", OUT)
