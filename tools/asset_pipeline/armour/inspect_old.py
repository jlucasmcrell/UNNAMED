"""Inspect the old animated-armour GLB: armature rest, mesh bounds, per-bone vertex bounds.
Run: blender -b --factory-startup -P inspect_old.py -- <glb> [<clip glb> ...]
"""
import bpy, sys, json
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
glb = argv[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)

out = {}
for ob in bpy.data.objects:
    print("OBJ", ob.name, ob.type, "parent=", ob.parent.name if ob.parent else None,
          "loc", tuple(round(v, 4) for v in ob.location), "rot", tuple(round(v, 4) for v in ob.rotation_euler),
          "scale", tuple(round(v, 4) for v in ob.scale))
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
print("ARM matrix_world", [list(map(lambda x: round(x, 4), r)) for r in arm.matrix_world])
for b in arm.data.bones:
    hw = arm.matrix_world @ b.head_local
    tw = arm.matrix_world @ b.tail_local
    print("BONE %-14s parent=%-12s head=(%.3f,%.3f,%.3f) tail=(%.3f,%.3f,%.3f) len=%.3f roll-ish z=%s" % (
        b.name, b.parent.name if b.parent else "-", *hw, *tw, b.length,
        tuple(round(v, 3) for v in (arm.matrix_world.to_3x3() @ b.matrix_local.to_3x3().col[2]))))

for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    me = ob.data
    mw = ob.matrix_world
    pts = [mw @ v.co for v in me.vertices]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print("MESH", ob.name, "verts", len(me.vertices), "tris", tris, "min", tuple(round(v, 3) for v in mn), "max", tuple(round(v, 3) for v in mx))
    print("  materials", [m.name if m else None for m in me.materials])
    for m in me.materials:
        if m and m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == "TEX_IMAGE" and n.image:
                    print("   tex", n.image.name, n.image.size[:])
    # per-bone dominant vertex bounds
    names = {g.index: g.name for g in ob.vertex_groups}
    per = {}
    for v, p in zip(me.vertices, pts):
        if not v.groups:
            continue
        g = max(v.groups, key=lambda g: g.weight)
        per.setdefault(names[g.group], []).append(p)
    for bn, ps in sorted(per.items()):
        mn = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
        mx = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
        print("  VG %-14s n=%6d min=(%.3f,%.3f,%.3f) max=(%.3f,%.3f,%.3f)" % (bn, len(ps), *mn, *mx))
    # loose parts count
    import bmesh
    bm = bmesh.new(); bm.from_mesh(me)
    seen = set(); comps = []
    for v in bm.verts:
        if v.index in seen: continue
        stack = [v]; seen.add(v.index); n = 0
        while stack:
            x = stack.pop(); n += 1
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index); stack.append(o)
        comps.append(n)
    comps.sort(reverse=True)
    print("  loose components", len(comps), "largest", comps[:10], "n<50:", sum(1 for c in comps if c < 50))

print("ACTIONS", [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
print("SCENE fps", bpy.context.scene.render.fps)
