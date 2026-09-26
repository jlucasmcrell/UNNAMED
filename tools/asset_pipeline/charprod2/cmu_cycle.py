"""One clean locomotion cycle out of a CMU mocap trial (the rancidmilk FBX conversion), as a GLB the retarget factory can read.

CMU trials are long takes with the travel baked into the hip. This finds the gait period from the legs' and arms' rotations (the
lag at which the pose best repeats), takes the steadiest single cycle in the middle of the take, and takes the travel off the hip
along its averaged path (the hips keep their bob, sway and surge): baked in place over the rest hip, facing the rest facing, from
frame 0. Exported: the armature (named "cmu_root", the retarget map's source root, at rest) and its one action, named "Cycle".

    blender -b --python cmu_cycle.py -- --fbx 35_01.fbx --out walk_35_01.glb [--min 0.5] [--max 1.6]
"""
import json
import math
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


def blend(a, c, w):
    qa, qc = a.to_quaternion(), c.to_quaternion()
    if qa.dot(qc) < 0:
        qc.negate()
    return Matrix.LocRotScale(a.to_translation().lerp(c.to_translation(), w), qa.slerp(qc, w), a.to_scale())


# The travel: the hip's ground path averaged over one period (the gait's own sway and surge left in), and its heading. Each frame is
# re-expressed in that path's frame - the path point laid on the hip's rest (bind) ground point, the path heading turned onto the rest
# facing - so a take that curves or runs the other way round the room still gives an in-place cycle facing forward whose planted feet
# neither drift sideways nor slide. (A straight line between the cycle's ends left a curved take's feet slipping sideways, the retarget
# measures the hips from the rest pose so a cycle centred elsewhere drew the body off its origin, and a cycle keyed from its source
# frame held its first pose until then.) Baked onto a fresh action from frame 0.
ground = [Vector((m.translation.x, m.translation.y, 0)) for m in hip_world]
half = best_lag // 2


def centre(k):
    lo_, hi_ = max(0, k - half), min(n - 1, k + half)
    return sum(ground[lo_:hi_ + 1], Vector()) / (hi_ - lo_ + 1)


def heading(k):
    d = centre(min(n - 1, k + 2)) - centre(max(0, k - 2))
    return math.atan2(d.y, d.x)


rest = arm.matrix_world @ hip.bone.head_local
home = Vector((rest.x, rest.y, 0))
side = (arm.matrix_world @ arm.pose.bones["lThigh"].bone.head_local) - (arm.matrix_world @ arm.pose.bones["rThigh"].bone.head_local)
face = side.cross(Vector((0, 0, 1)))
facing = math.atan2(face.y, face.x)
print("CMU_REST", tuple(round(v, 2) for v in rest), "facing_deg", round(math.degrees(facing), 1))


def in_path(k):
    return Matrix.Translation(home) @ Matrix.Rotation(facing - heading(k), 4, "Z") @ Matrix.Translation(-centre(k)) @ hip_world[k]


to_arm = arm.matrix_world.inverted()
keys = []
for i in range(best_lag + 1):
    f = frames[s0 + i]
    world = in_path(s0 + i)
    basis = dict(bases[f])
    # The loop seam: over the cycle's last quarter every bone eases toward its pose one period earlier, which the take carries on into
    # the cycle's first frame - the last frame then is the first, and the loop never pops. The hip eases in the path's frame.
    w = min(1.0, (i - (best_lag - tail)) / tail)
    j = s0 + i - best_lag
    if w > 0 and j >= 0 and frames[j] in bases:
        world = blend(world, in_path(j), w)
        for name in basis:
            if name != hip.name:
                basis[name] = blend(basis[name], bases[frames[j]][name], w)
    keys.append((basis, to_arm @ world))
arm.animation_data.action = None
for b in arm.pose.bones:
    b.rotation_mode = "QUATERNION"
cycle = bpy.data.actions.new("Cycle")
last = {}
for i, (basis, hip_arm) in enumerate(keys):
    for b in arm.pose.bones:
        b.matrix_basis = basis[b.name]
    bpy.context.view_layer.update()
    hip.matrix = hip_arm
    bpy.context.view_layer.update()
    arm.animation_data.action = cycle
    for b in arm.pose.bones:
        if b is hip:
            b.matrix_basis = hip.matrix_basis.copy()
        q = b.rotation_quaternion.copy()
        if b.name in last and q.dot(last[b.name]) < 0:
            q.negate()
            b.rotation_quaternion = q
        last[b.name] = q
        b.keyframe_insert("location", frame=i)
        b.keyframe_insert("rotation_quaternion", frame=i)
    arm.animation_data.action = None
arm.animation_data.action = cycle
bpy.data.actions.remove(act)
arm.name = "cmu_root"
scene.frame_start, scene.frame_end = 0, best_lag
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=out, export_format="GLB", use_selection=True, export_animations=True, export_frame_range=True,
                          export_anim_single_armature=True, export_force_sampling=True, export_optimize_animation_size=False,
                          export_skins=True, export_def_bones=False)
speed = (centre(s1) - centre(s0)).length / (best_lag / fps)
json.dump({"fbx": fbx, "fps": fps, "period_frames": best_lag, "period_s": round(best_lag / fps, 3), "start_frame": frames[s0],
           "match_error": round(float(min(score)), 4), "raw_speed_units_per_s": round(speed, 3),
           "hip_height_units": round(float(np.median([m.translation.z for m in hip_world])), 3)}, open(out + ".json", "w"), indent=1)
print("CMU_CYCLE", open(out + ".json").read().replace("\n", " "))
