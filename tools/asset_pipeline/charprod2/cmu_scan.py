"""Scan CMU mocap trials (the rancidmilk FBX conversion) for straight steady locomotion: per trial, the hips' horizontal speed over
its middle, how straight the path is, and the duration - to find a walk, a run and a sprint at the game's own speeds.

    blender -b --python cmu_scan.py -- --dir <fbx dir> --out scan.json [--glob "16_*.fbx"]
"""
import glob
import json
import os
import statistics
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
folder = argv[argv.index("--dir") + 1]
out = argv[argv.index("--out") + 1]
pattern = argv[argv.index("--glob") + 1] if "--glob" in argv else "*.fbx"
results = {}
for path in sorted(glob.glob(os.path.join(folder, pattern))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    except Exception as e:
        results[os.path.basename(path)] = {"error": str(e)[:200]}
        continue
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm is None or not arm.animation_data or not arm.animation_data.action:
        results[os.path.basename(path)] = {"error": "no armature/action"}
        continue
    hip = next((b for b in arm.pose.bones if b.parent is None), arm.pose.bones[0])
    act = arm.animation_data.action
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    step = max(1, (f1 - f0) // 400)
    pts = []
    for f in range(f0, f1 + 1, step):
        bpy.context.scene.frame_set(f)
        p = arm.matrix_world @ hip.head
        pts.append((p.x, p.y, p.z))
    n = len(pts)
    a, b = n // 5, n - n // 5
    mid = pts[a:b]
    if len(mid) < 3:
        continue
    seg = [((mid[i + 1][0] - mid[i][0]) ** 2 + (mid[i + 1][1] - mid[i][1]) ** 2) ** 0.5 for i in range(len(mid) - 1)]
    length = sum(seg)
    net = ((mid[-1][0] - mid[0][0]) ** 2 + (mid[-1][1] - mid[0][1]) ** 2) ** 0.5
    dt = step / fps
    speeds = [s / dt for s in seg]
    results[os.path.basename(path)] = {
        "seconds": round((f1 - f0) / fps, 2), "fps": fps, "mid_speed": round(length / (dt * len(seg)), 2),
        "speed_sd": round(statistics.pstdev(speeds), 2), "straightness": round(net / length, 3) if length > 0 else 0,
        "hip_height": round(statistics.median(p[2] for p in pts), 3), "bones": len(arm.pose.bones), "root": hip.name}
json.dump(results, open(out, "w"), indent=1)
print("CMU_SCAN", len(results))
