"""One clean locomotion cycle out of a CMU mocap trial (the rancidmilk FBX conversion), as a GLB the retarget factory can read.

CMU trials are long takes with the travel baked into the hip. This finds the gait period from the legs' and arms' rotations (the
lag at which the pose best repeats), takes the steadiest single cycle in the middle of the take, and moves the straight-line travel
off the hip onto the armature object (the retarget map's source root, "cmu_root") so --root inplace removes the travel while the
hips keep their bob, sway and turn. Exported: the armature and its one action, named "Cycle".

    blender -b --python cmu_cycle.py -- --fbx 35_01.fbx --out walk_35_01.glb [--min 0.5] [--max 1.6]
"""
import json
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
fbx = argv[argv.index("--fbx") + 1]
out = argv[argv.index("--out") + 1]
lag_min = float(argv[argv.index("--min") + 1]) if "--min" in argv else 0.5
lag_max = float(argv[argv.index("--max") + 1]) if "--max" in argv else 1.6

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, automatic_bone_orientation=False)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for o in list(bpy.data.objects):
    if o is not arm:
        bpy.data.objects.remove(o)
act = arm.animation_data.action
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base
f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
feature_bones = [b for b in ("lThigh", "rThigh", "lShin", "rShin", "lShldr", "rShldr", "lForeArm", "rForeArm", "abdomen") if b in arm.pose.bones]

hip = arm.pose.bones["hip"]
frames = list(range(f0, f1 + 1))
feats, hip_world = [], []
for f in frames:
    scene.frame_set(f)
    v = []
    for b in feature_bones:
        q = arm.pose.bones[b].matrix_basis.to_quaternion()
        if q.w < 0:
            q.negate()
        v += [q.w, q.x, q.y, q.z]
    feats.append(v)
    hip_world.append((arm.matrix_world @ hip.matrix).copy())
feats = np.array(feats)
n = len(frames)

# The gait period: the lag at which the pose repeats best over the take's middle.
lo, hi = max(2, int(lag_min * fps)), min(n // 2, int(lag_max * fps))
mid0, mid1 = n // 6, n - n // 6
best_lag, best_d = None, 1e9
for lag in range(lo, hi + 1):
    idx = np.arange(mid0, max(mid0 + 1, mid1 - lag))
    if len(idx) == 0:
        continue
    d = float(np.mean(np.linalg.norm(feats[idx] - feats[idx + lag], axis=1)))
    if d < best_d:
        best_lag, best_d = lag, d
if best_lag is None:
    raise SystemExit("take too short for a cycle")
# The steadiest cycle: the start whose pose (and pose change) best matches one period later.
vel = np.vstack([feats[1:] - feats[:-1], np.zeros((1, feats.shape[1]))])
cands = [s for s in range(n // 8, n - best_lag - n // 8)] or list(range(0, n - best_lag))
score = [np.linalg.norm(feats[s] - feats[s + best_lag]) + 2 * np.linalg.norm(vel[s] - vel[s + best_lag]) for s in cands]
s0 = cands[int(np.argmin(score))]
s1 = s0 + best_lag

# Every bone's pose over the cycle and one period before it (the take runs on smoothly into the cycle's start from there).
bases = {}
for f in range(frames[max(0, s0 - best_lag)], frames[s1] + 1):
    scene.frame_set(f)
    bases[f] = {b.name: b.matrix_basis.copy() for b in arm.pose.bones}
tail = max(2, best_lag // 4)

# The travel: a straight line through the hip's ground positions at the cycle's two ends, carried by the armature object.
p0, p1 = hip_world[s0 - 0].translation.copy(), hip_world[s1].translation.copy()
p0.z = p1.z = 0
arm.name = "cmu_root"
arm.animation_data.action = act
for i, f in enumerate(range(frames[s0], frames[s1] + 1)):
    t = i / best_lag
    travel = p0.lerp(p1, t)
    arm.location = travel
    arm.keyframe_insert("location", frame=f)
    scene.frame_set(f)
    world = Matrix.Translation(-travel) @ hip_world[s0 + i]
    w = (i - (best_lag - tail)) / tail
    if w > 0 and s0 + i - best_lag >= 0:
        # The hip's own seam: toward where it was one period earlier, relative to the travel line extended back.
        before = Matrix.Translation(-p0.lerp(p1, t - 1)) @ hip_world[s0 + i - best_lag]
        qa, qc = world.to_quaternion(), before.to_quaternion()
        if qa.dot(qc) < 0:
            qc.negate()
        world = Matrix.LocRotScale(world.to_translation().lerp(before.to_translation(), min(1.0, w)), qa.slerp(qc, min(1.0, w)),
                                   world.to_scale())
    hip.matrix = world
    hip.keyframe_insert("location", frame=f)
    hip.keyframe_insert("rotation_quaternion" if hip.rotation_mode == "QUATERNION" else "rotation_euler", frame=f)
    # The loop seam: over the cycle's last quarter every bone eases toward its pose one period earlier, which the take carries on
    # into the cycle's first frame - the last frame then is the first, and the loop never pops.
    w = (i - (best_lag - tail)) / tail
    earlier = frames[s0] + i - best_lag
    if w > 0 and earlier in bases:
        for b in arm.pose.bones:
            if b is hip:
                continue
            a, c = bases[f][b.name], bases[earlier][b.name]
            qa, qc = a.to_quaternion(), c.to_quaternion()
            if qa.dot(qc) < 0:
                qc.negate()
            q = qa.slerp(qc, min(1.0, w))
            b.matrix_basis = Matrix.LocRotScale(a.to_translation().lerp(c.to_translation(), min(1.0, w)), q, a.to_scale())
            b.keyframe_insert("location", frame=f)
            b.keyframe_insert("rotation_quaternion" if b.rotation_mode == "QUATERNION" else "rotation_euler", frame=f)
scene.frame_start, scene.frame_end = frames[s0], frames[s1]
act.name = "Cycle"
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=True, export_frame_range=True,
                          export_anim_single_armature=True, export_force_sampling=True, export_optimize_animation_size=False,
                          export_skins=True, export_def_bones=False)
speed = (p1 - p0).length / (best_lag / fps)
json.dump({"fbx": fbx, "fps": fps, "period_frames": best_lag, "period_s": round(best_lag / fps, 3), "start_frame": frames[s0],
           "match_error": round(float(min(score)), 4), "raw_speed_units_per_s": round(speed, 3),
           "hip_height_units": round(float(np.median([m.translation.z for m in hip_world])), 3)}, open(out + ".json", "w"), indent=1)
print("CMU_CYCLE", open(out + ".json").read().replace("\n", " "))
