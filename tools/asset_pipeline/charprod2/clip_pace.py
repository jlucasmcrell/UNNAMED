"""The pace an in-place locomotion clip was authored at: in a clip with no root travel the planted foot slides backward at exactly the
speed the body would travel, so the pace is the lowest foot's horizontal speed while it is on the ground (within 2 cm of the lowest
point either foot reaches). Also the cycle time and the step cadence.

    blender -b --python clip_pace.py -- CLIP.glb [CLIP.glb ...] [--feet foot.L,foot.R]
"""
import json
import statistics
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
feet = argv[argv.index("--feet") + 1].split(",") if "--feet" in argv else ["foot.L", "foot.R"]
out = {}
for path in [a for a in argv if a.endswith(".glb")]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    if not (arm.animation_data and arm.animation_data.action) and bpy.data.actions:
        arm.animation_data_create()
        arm.animation_data.action = bpy.data.actions[0]
    act = arm.animation_data.action
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fps = bpy.context.scene.render.fps
    names = [n for n in feet if n in arm.pose.bones] or [b.name for b in arm.pose.bones if b.name.lower().startswith("foot")][:2]
    track = {n: [] for n in names}
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        for n in names:
            track[n].append((arm.matrix_world @ arm.pose.bones[n].head).copy())
    low = min(p.z for n in names for p in track[n])
    speeds, contacts = [], 0
    for n in names:
        ps = track[n]
        for i in range(len(ps) - 1):
            if ps[i].z < low + 0.02 and ps[i + 1].z < low + 0.02:
                d = ps[i + 1] - ps[i]
                speeds.append((d.x ** 2 + d.y ** 2) ** 0.5 * fps)
                contacts += 1
    frames = f1 - f0 + 1
    out[path.replace("\\", "/").split("/")[-1]] = {
        "pace_mps": round(statistics.median(speeds), 3) if speeds else None, "contact_frames": contacts,
        "cycle_s": round((frames - 1) / fps, 3), "feet": names}
print("CLIP_PACE " + json.dumps(out))
