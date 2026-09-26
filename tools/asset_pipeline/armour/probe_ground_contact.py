"""Lowest mesh point per clip (25 samples each): does the rigged model sink below the floor?
blender -b --factory-startup -P probe_ground_contact.py -- <rigged.glb> <clips_dir>
"""
import bpy, sys, os
argv = sys.argv[sys.argv.index("--") + 1:]
model, clips = argv[0], argv[1]
for clip in ("idle", "walk", "run", "attack", "hit", "death"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=model)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH" and not o.name.startswith("Icosphere"))
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(clips, f"anim.creature.animated_armour.{clip}.glb"))
    new = [o for o in bpy.data.objects if o not in before]
    act = next(o for o in new if o.type == "ARMATURE").animation_data.action
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    arm.animation_data_create(); arm.animation_data.action = act; arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    lows = []
    for k in range(25):
        f = f0 + (f1 - f0) * k / 24
        bpy.context.scene.frame_set(int(f), subframe=f - int(f))
        dg = bpy.context.evaluated_depsgraph_get()
        ev = body.evaluated_get(dg)
        me = ev.to_mesh()
        lows.append((min((ev.matrix_world @ v.co).z for v in me.vertices), round(f, 1)))
        ev.to_mesh_clear()
    print("GROUND", clip, "min z %.3f at f%s" % min(lows), "max-of-mins %.3f" % max(l[0] for l in lows))
