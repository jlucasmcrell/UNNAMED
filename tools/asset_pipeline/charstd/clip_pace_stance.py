"""The pace an in-place locomotion clip shows on its rig: while a foot is flat on the ground it slides backward at the speed the body
would travel. A foot is flat when its ankle is within 3 cm of that ankle's lowest in the clip and its toe within 2 cm of the toe's
lowest; the pace is the median along-travel speed (the clip's forward axis only) over both feet's flat frames. A captured stance
also rolls - the ankle swings forward over the heel as the foot lands and over the toe as the heel lifts - so the ankle's speed over
the whole contact, or its speed across the ground (sideways wander included), both read slow or fast against the ground the flat foot
actually holds. A rig without toe bones falls back to the ankle alone. Also the cycle length and the flat share.

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
    bpy.context.scene.render.fps = 60     # the clips' bake rate: at Blender's default 24 every key is not sampled
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    if not (arm.animation_data and arm.animation_data.action) and bpy.data.actions:
        arm.animation_data_create()
        arm.animation_data.action = bpy.data.actions[0]
    act = arm.animation_data.action
    scene = bpy.context.scene
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    fps = scene.render.fps / scene.render.fps_base
    toes = all(t in arm.pose.bones for t in ("toe.L", "toe.R"))
    track = {b: [] for b in ("foot.L", "foot.R") + (("toe.L", "toe.R") if toes else ())}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        for b in track:
            track[b].append((arm.matrix_world @ arm.pose.bones[b].head).copy())
    speeds, stance = [], 0
    for side in "LR":
        pts = track["foot." + side]
        toe = track.get("toe." + side)
        low, toe_low = min(p.z for p in pts), min(p.z for p in toe) if toe else 0
        for i, (a, c) in enumerate(zip(pts, pts[1:])):
            flat = a.z - low < 0.03 and c.z - low < 0.03 and (not toe or (toe[i].z - toe_low < 0.02 and toe[i + 1].z - toe_low < 0.02))
            if flat:
                speeds.append(abs(c.y - a.y) * fps)     # glTF +Z forward imports as Blender -Y: the along-travel axis
                stance += 1
    out[path.split("\\")[-1].split("/")[-1]] = {"pace_mps": round(statistics.median(speeds), 3) if speeds else 0.0,
                                                "stance_frames": stance, "frames": f1 - f0 + 1, "seconds": round((f1 - f0) / fps, 3)}
print("CLIP_PACE_STANCE", json.dumps(out))
