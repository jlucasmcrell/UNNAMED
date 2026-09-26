"""Where an attack clip's blow happens: the sword hand's speed over the clip, and the fractions where the fast swing starts and ends
(speed above half its peak) - the marks presentation maps the simulation's windup / active / recovery onto.

    blender -b --python clip_marks.py -- CLIP.glb [CLIP.glb ...] [--bone hand.R]
"""
import json
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
bone = argv[argv.index("--bone") + 1] if "--bone" in argv else "hand.R"
clips = [a for a in argv if a.endswith(".glb")]
out = {}
for path in clips:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    act = arm.animation_data.action if arm.animation_data else None
    if act is None and bpy.data.actions:
        act = bpy.data.actions[0]
        arm.animation_data_create()
        arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fps = bpy.context.scene.render.fps
    pos = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        pos.append((arm.matrix_world @ arm.pose.bones[bone].head).copy())
    speed = [(pos[i + 1] - pos[i]).length * fps for i in range(len(pos) - 1)]
    peak = max(speed)
    fast = [i for i, s in enumerate(speed) if s >= 0.5 * peak]
    n = len(speed)
    out[path.split("/")[-1].split("\\")[-1]] = {"frames": n + 1, "fps": fps, "peak_mps": round(peak, 2),
                                                "swing": [round(fast[0] / n, 3), round((fast[-1] + 1) / n, 3)],
                                                "profile": [round(s, 1) for s in speed[:: max(1, n // 24)]]}
print("CLIP_MARKS " + json.dumps(out))
