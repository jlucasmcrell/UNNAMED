"""The pace an in-place locomotion clip shows on its rig, per foot: while a foot is planted (within 3 cm of that foot's own lowest
height in the clip) it slides backward at the speed the body would travel. Median over both feet's stance frames; also the cycle
length and the stance share. More robust than one threshold for both feet when a run or sprint lands on the forefoot.

    blender -b --python clip_pace_stance.py -- CLIP.glb [CLIP.glb ...]
"""
import json
import statistics
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
out = {}
for path in [a for a in argv if a.endswith(".glb")]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    if not (arm.animation_data and arm.animation_data.action) and bpy.data.actions:
        arm.animation_data_create()
        arm.animation_data.action = bpy.data.actions[0]
    act = arm.animation_data.action
    scene = bpy.context.scene
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fps = scene.render.fps / scene.render.fps_base
    track = {"foot.L": [], "foot.R": []}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for b in track:
            track[b].append((arm.matrix_world @ arm.pose.bones[b].head).copy())
    speeds, stance = [], 0
    for b, pts in track.items():
        low = min(p.z for p in pts)
        for a, c in zip(pts, pts[1:]):
            if a.z - low < 0.03 and c.z - low < 0.03:
                speeds.append(((c.x - a.x) ** 2 + (c.y - a.y) ** 2) ** 0.5 * fps)
                stance += 1
    out[path.split("\\")[-1].split("/")[-1]] = {"pace_mps": round(statistics.median(speeds), 3) if speeds else 0.0,
                                                "stance_frames": stance, "frames": f1 - f0 + 1, "seconds": round((f1 - f0) / fps, 3)}
print("CLIP_PACE_STANCE", json.dumps(out))
