"""Sample world bone heads over each clip: ankle heights, root travel, extreme angles.
blender -b --factory-startup -P probe_clip_motion.py -- <clip.glb> ...
"""
import bpy, sys, math

argv = sys.argv[sys.argv.index("--") + 1:]
for path in argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    act = arm.animation_data.action
    f0, f1 = act.frame_range
    sc = bpy.context.scene
    name = path.rsplit(".", 2)[-2]
    lows = {"L": [], "R": []}
    print("=== ", name, "frames", f0, f1)
    n = 8
    for i in range(n + 1):
        f = f0 + (f1 - f0) * i / n
        sc.frame_set(int(f), subframe=f - int(f))
        pb = arm.pose.bones
        mw = arm.matrix_world
        def w(b, tail=False):
            return mw @ (pb[b].tail if tail else pb[b].head)
        row = []
        for s in "LR":
            a = w("foot." + s)
            lows[s].append(a.z)
            row.append("ank%s z=%.3f y=%.3f" % (s, a.z, a.y))
        r = w("root"); h = w("hips"); hd = w("head")
        ch = pb["chest"].matrix.to_3x3() @ __import__("mathutils").Vector((0, 1, 0))
        print("  f%5.1f root=(%.2f,%.2f,%.2f) hips=(%.2f,%.2f,%.2f) head=(%.2f,%.2f,%.2f) %s" % (f, *r, *h, *hd, " ".join(row)))
    print("  min ankle L %.3f R %.3f" % (min(lows["L"]), min(lows["R"])))
