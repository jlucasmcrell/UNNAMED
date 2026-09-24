"""Generate synthetic motion data, in the source-motion format the retargeter consumes.

Purpose is **pipeline proof, not production animation**. Locomotion here is deliberately simple
and readable: it drives the legs and counter-swings the arms so the retarget, bake, loop-check,
export and Godot playback stages can all be exercised end to end. A real motion source (purchased
library, mocap or AI motion) arrives in this same shape once adapted, so replacing it later is a
data swap, not a rewrite.

The sprint brief is explicit that the goal is "reliable gameplay-capable motion through the
complete Blender to GLB to Godot pipeline" and not final animation, so artefacts that read as
programmer motion are expected and acceptable here.

Usage:
    python _make_motion.py --kind walk   --out assets\\animation\\source\\manual\\walk.json
    python _make_motion.py --kind run    --out ...\\run.json
    python _make_motion.py --kind sprint --out ...\\sprint.json
    python _make_motion.py --kind idle   --out ...\\idle.json
"""
import argparse
import json
import math
import sys

# Canonical bone names the generator animates. A partial set on purpose: the retargeter leaves
# unnamed bones at bind pose rather than snapping them to zero.
WALK_BONES = ["thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r",
              "upperarm_l", "lowerarm_l", "upperarm_r", "lowerarm_r",
              "spine_01", "spine_02", "chest", "pelvis"]

# Per-gait parameters. Stride and lean are what actually distinguish the three speeds at this
# fidelity: a prototype needs the player to read as walking, running or sprinting, which is a
# silhouette and cadence problem before it is a muscle problem.
#
# Periods are chosen so the frame count is exact at 30 fps: a loop of N intervals spans N/fps
# seconds, so 1.0, 0.7 and 0.6 all land on whole frames and the declared duration in the clip
# metadata matches the baked animation rather than approximating it.
GAITS = {
    "walk":   {"stride": 28.0, "period": 1.00, "bob": 0.020, "lean": 0.0,  "arm": 0.70, "knee": 45.0},
    "run":    {"stride": 42.0, "period": 0.70, "bob": 0.035, "lean": 8.0,  "arm": 1.00, "knee": 62.0},
    "sprint": {"stride": 52.0, "period": 0.60, "bob": 0.045, "lean": 14.0, "arm": 1.20, "knee": 74.0},
}


def locomotion_cycle(frames, fps, gait):
    """A two-step cycle. Legs swing in antiphase; arms counter-swing.

    Rotations are degrees about the bone's local X, the usual forward-swing axis in this
    skeleton's orientation. The root bob is real root motion, so it can be kept or stripped
    independently of the pose.
    """
    p = GAITS[gait]
    out = []
    for index in range(frames):
        t = index / fps
        phase = 2 * math.pi * (t / p["period"])
        swing = math.sin(phase) * p["stride"]
        knee_l = max(0.0, math.sin(phase - 0.9)) * p["knee"]
        knee_r = max(0.0, math.sin(phase + math.pi - 0.9)) * p["knee"]
        bob = math.sin(2 * phase) * p["bob"]
        lean = p["lean"]
        out.append({
            "pelvis": {"rotation": [lean * 0.25, 0.0, math.sin(phase) * 4.0],
                       "location": [0.0, bob, 0.0]},
            "spine_01": {"rotation": [lean * 0.30, 0.0, -math.sin(phase) * 3.0]},
            "spine_02": {"rotation": [lean * 0.35, 0.0, -math.sin(phase) * 2.0]},
            "chest": {"rotation": [lean * 0.40, math.sin(phase) * 5.0, 0.0]},
            "thigh_l": {"rotation": [swing, 0.0, 0.0]},
            "thigh_r": {"rotation": [-swing, 0.0, 0.0]},
            "calf_l": {"rotation": [-knee_l, 0.0, 0.0]},
            "calf_r": {"rotation": [-knee_r, 0.0, 0.0]},
            "foot_l": {"rotation": [knee_l * 0.35, 0.0, 0.0]},
            "foot_r": {"rotation": [knee_r * 0.35, 0.0, 0.0]},
            "upperarm_l": {"rotation": [-swing * p["arm"], 0.0, 0.0]},
            "upperarm_r": {"rotation": [swing * p["arm"], 0.0, 0.0]},
            "lowerarm_l": {"rotation": [20.0 + swing * 0.2, 0.0, 0.0]},
            "lowerarm_r": {"rotation": [20.0 - swing * 0.2, 0.0, 0.0]},
        })
    return out


def idle_cycle(frames, fps):
    """Breathing and a slow weight shift, so an idle player is not a statue.

    Deliberately slow and small: idle is the clip most likely to be seen for a long time, and a
    loop seam is most obvious when nothing much is moving.
    """
    period = 4.0
    out = []
    for index in range(frames):
        t = index / fps
        breath = math.sin(2 * math.pi * t / period)
        sway = math.sin(2 * math.pi * t / period - 0.6)
        out.append({
            "pelvis": {"rotation": [0.0, 0.0, sway * 1.2], "location": [0.0, breath * 0.006, 0.0]},
            "spine_01": {"rotation": [breath * 1.0, 0.0, -sway * 0.8]},
            "spine_02": {"rotation": [breath * 1.2, 0.0, -sway * 0.6]},
            "chest": {"rotation": [breath * 1.6, sway * 1.0, 0.0]},
            "neck": {"rotation": [-breath * 0.8, sway * 1.4, 0.0]},
            "upperarm_l": {"rotation": [breath * 2.0, 0.0, 0.0]},
            "upperarm_r": {"rotation": [breath * 2.0, 0.0, 0.0]},
            "lowerarm_l": {"rotation": [8.0 + breath * 1.5, 0.0, 0.0]},
            "lowerarm_r": {"rotation": [8.0 + breath * 1.5, 0.0, 0.0]},
            "thigh_l": {"rotation": [breath * 0.6, 0.0, 0.0]},
            "thigh_r": {"rotation": [-breath * 0.6, 0.0, 0.0]},
            "calf_l": {"rotation": [-breath * 0.8, 0.0, 0.0]},
            "calf_r": {"rotation": [-breath * 0.8, 0.0, 0.0]},
        })
    return out


def thrust_strike(frames, fps, duration=0.82):
    """Wind up, commit, recover. Times line up with the registry events so the clip's declared
    attack_commit and hit window can be checked against the motion rather than assumed."""
    commit, hit_start, hit_end, recovery = 0.21, 0.29, 0.47, 0.52
    out = []
    for index in range(frames):
        t = index / fps
        if t < commit:
            # wind up: draw back
            k = t / commit
            arm = -35.0 * k
            torso = -8.0 * k
        elif t < hit_start:
            # commit: drive forward fast
            k = (t - commit) / max(hit_start - commit, 1e-6)
            arm = -35.0 + 105.0 * k
            torso = -8.0 + 16.0 * k
        elif t < hit_end:
            arm, torso = 70.0, 8.0
        else:
            k = min((t - recovery) / max(duration - recovery, 1e-6), 1.0)
            arm = 70.0 * (1.0 - k)
            torso = 8.0 * (1.0 - k)
        out.append({
            "pelvis": {"rotation": [0.0, torso * 0.3, 0.0], "location": [0.0, 0.0, 0.0]},
            "spine_01": {"rotation": [0.0, torso * 0.4, 0.0]},
            "spine_02": {"rotation": [0.0, torso * 0.5, 0.0]},
            "chest": {"rotation": [0.0, torso * 0.6, 0.0]},
            "upperarm_r": {"rotation": [arm, 0.0, 0.0]},
            "lowerarm_r": {"rotation": [max(0.0, 60.0 - arm * 0.6), 0.0, 0.0]},
            "upperarm_l": {"rotation": [-arm * 0.35, 0.0, 0.0]},
            "lowerarm_l": {"rotation": [45.0, 0.0, 0.0]},
            "thigh_l": {"rotation": [-12.0, 0.0, 0.0]},
            "thigh_r": {"rotation": [12.0, 0.0, 0.0]},
            "calf_l": {"rotation": [-18.0, 0.0, 0.0]},
            "calf_r": {"rotation": [-8.0, 0.0, 0.0]},
        })
    return out


# ---------------------------------------------------------------------------------------------
# Action clips: the general, combat and magic set the sprint brief section 6 lists.
#
# Each clip is a table of (t, pose) keys, where t is a fraction of the clip and poses are linearly
# interpolated between keys. A table rather than a chain of time comparisons, because these clips
# exist to carry a readable *shape* of motion through the pipeline, and a shape is easier to read
# and to correct as a list of poses. Because t is a fraction, changing a clip's duration rescales
# the whole action instead of breaking the relation between its phases and its declared events.
#
# Sign conventions, inherited from the locomotion and thrust generators that already work:
#   upperarm local X, positive = arm raises / swings forward
#   lowerarm local X, positive = elbow flexes  (negative would be hyperextension)
#   thigh    local X, positive = hip flexes forward
#   calf     local X, negative = knee flexes
#   spine and chest X, positive = torso bends forward; Y twists; Z side-bends
#   root     local X, positive = the whole body tips forward about the ground
#   pelvis location[1] = vertical metres, because the bone's own Y axis is world up
# ---------------------------------------------------------------------------------------------

def arm(side, upper, lower, twist=0.0, hand=0.0, clavicle=0.0):
    pose = {f"upperarm_{side}": {"rotation": [upper, twist, 0.0]},
            f"lowerarm_{side}": {"rotation": [lower, 0.0, 0.0]}}
    if hand:
        pose[f"hand_{side}"] = {"rotation": [hand, 0.0, 0.0]}
    if clavicle:
        pose[f"clavicle_{side}"] = {"rotation": [clavicle, 0.0, 0.0]}
    return pose


def leg(side, hip, knee, ankle=0.0):
    return {f"thigh_{side}": {"rotation": [hip, 0.0, 0.0]},
            f"calf_{side}": {"rotation": [-knee, 0.0, 0.0]},
            f"foot_{side}": {"rotation": [ankle, 0.0, 0.0]}}


def legs(hip, knee, ankle=0.0):
    pose = leg("l", hip, knee, ankle)
    pose.update(leg("r", hip, knee, ankle))
    return pose


def torso(bend, twist=0.0, side=0.0):
    """A torso bend distributed over the four spine segments rather than pinned on one joint.

    The weights sum to 1.0, so `bend` is the total forward angle and a caller can think in terms of
    "lean 30 degrees" without tracking how it is shared out.
    """
    return {"spine_01": {"rotation": [bend * 0.25, twist * 0.20, side * 0.30]},
            "spine_02": {"rotation": [bend * 0.35, twist * 0.30, side * 0.30]},
            "spine_03": {"rotation": [bend * 0.20, twist * 0.20, side * 0.20]},
            "chest": {"rotation": [bend * 0.20, twist * 0.30, side * 0.20]}}


def hips(drop=0.0, yaw=0.0):
    return {"pelvis": {"location": [0.0, -drop, 0.0], "rotation": [0.0, yaw, 0.0]}}


def head(pitch=0.0, yaw=0.0):
    return {"neck": {"rotation": [pitch * 0.6, yaw * 0.6, 0.0]},
            "head": {"rotation": [pitch * 0.4, yaw * 0.4, 0.0]}}


def merged(*parts):
    pose = {}
    for part in parts:
        pose.update(part)
    return pose


def interp_pose(a, b, k):
    """Linear blend between two poses. A channel present in only one pose blends from zero."""
    pose = {}
    for bone in set(a) | set(b):
        pa, pb = a.get(bone, {}), b.get(bone, {})
        channels = {}
        for channel in set(pa) | set(pb):
            va = pa.get(channel, [0.0, 0.0, 0.0])
            vb = pb.get(channel, [0.0, 0.0, 0.0])
            channels[channel] = [va[i] + (vb[i] - va[i]) * k for i in range(3)]
        pose[bone] = channels
    return pose


def keyed_cycle(keys, frames):
    span = frames - 1
    out = []
    for index in range(frames):
        t = index / span if span else 0.0
        for position in range(len(keys) - 1):
            start, first = keys[position]
            end, second = keys[position + 1]
            if start <= t <= end:
                k = (t - start) / (end - start) if end > start else 0.0
                out.append(interp_pose(first, second, k))
                break
        else:
            out.append(interp_pose(keys[-1][1], keys[-1][1], 0.0))
    return out


# A bladed guard stance, seen from the side: right hand grips the sword at chest height, left hand
# supports, weight on the back foot. Every combat clip starts and finishes here so the set can be
# blended from one to another without a visible pop.
SWORD_GUARD = merged(
    torso(8.0, twist=-6.0), legs(0, 0),
    leg("l", 14.0, 22.0, 6.0), leg("r", -8.0, 16.0, 2.0),
    arm("r", 54.0, 58.0, hand=16.0), arm("l", 22.0, 70.0, hand=10.0))

POLEARM_GUARD = merged(
    torso(6.0, twist=8.0),
    leg("l", 16.0, 24.0, 6.0), leg("r", -10.0, 18.0, 2.0),
    arm("r", 34.0, 46.0, hand=14.0, clavicle=-4.0), arm("l", 46.0, 64.0, hand=12.0))

BOW_READY = merged(
    torso(2.0, twist=10.0),
    leg("l", 12.0, 18.0, 4.0), leg("r", -8.0, 14.0, 2.0),
    arm("l", 66.0, 12.0, hand=6.0, clavicle=-6.0), arm("r", 26.0, 78.0, hand=12.0))

# kind -> (duration_s, loop, keys). Durations are chosen so the frame count at 30 fps is exact:
# a clip of N frames spans (N-1)/30 s, so every duration below lands on a whole frame.
ACTIONS = {
    "interact": (1.60, False, [
        (0.00, {}),
        (0.30, merged(torso(5.0), arm("r", 46.0, 52.0, twist=-8.0, hand=10.0))),
        (0.58, merged(torso(7.0), arm("r", 58.0, 34.0, twist=-5.0, hand=16.0))),
        (0.68, merged(torso(7.0), arm("r", 58.0, 34.0, twist=-5.0, hand=16.0))),
        (0.90, merged(torso(2.0), arm("r", 16.0, 26.0))),
        (1.00, {}),
    ]),
    "pickup": (1.80, False, [
        (0.00, {}),
        (0.34, merged(torso(24.0), legs(46.0, 78.0, 26.0), arm("r", 30.0, 46.0), hips(0.22))),
        (0.56, merged(torso(32.0), legs(58.0, 92.0, 32.0), arm("r", 16.0, 30.0, hand=20.0),
                      hips(0.30))),
        (0.66, merged(torso(32.0), legs(58.0, 92.0, 32.0), arm("r", 16.0, 30.0, hand=20.0),
                      hips(0.30))),
        (1.00, {}),
    ]),
    "hit_react": (0.55, False, [
        (0.00, merged(torso(-18.0), arm("l", -24.0, 34.0), arm("r", -20.0, 30.0),
                      head(-10.0), legs(6.0, 10.0), hips(0.02))),
        (0.32, merged(torso(-7.0), arm("l", -10.0, 18.0), arm("r", -8.0, 16.0), head(-4.0))),
        (1.00, {}),
    ]),
    # Root rotation carries the fall and the pelvis drop carries the buckle. They are not additive:
    # tipping the body already lowers the hips, so the drop is small where the tip is large.
    "death": (2.00, False, [
        (0.00, merged(torso(-12.0), arm("l", -28.0, 40.0), arm("r", -24.0, 36.0), hips(0.03))),
        (0.22, merged(torso(16.0), legs(20.0, 34.0, 10.0), arm("l", -34.0, 52.0),
                      arm("r", -30.0, 48.0), {"root": {"rotation": [8.0, 0.0, 0.0]}}, hips(0.10))),
        (0.52, merged(torso(30.0), legs(62.0, 96.0, 28.0), arm("l", -46.0, 66.0),
                      arm("r", -42.0, 62.0), {"root": {"rotation": [20.0, 0.0, 0.0]}}, hips(0.20))),
        (0.80, merged(torso(22.0), legs(48.0, 72.0, 22.0), arm("l", -62.0, 74.0),
                      arm("r", -58.0, 70.0), {"root": {"rotation": [52.0, 0.0, 0.0]}},
                      head(14.0), hips(0.08))),
        (1.00, merged(torso(14.0), legs(40.0, 60.0, 18.0), arm("l", -68.0, 78.0),
                      arm("r", -64.0, 74.0), {"root": {"rotation": [68.0, 0.0, 0.0]}},
                      head(18.0), hips(0.04))),
    ]),
    # Loop closes by construction: the first and last keys are the same pose, and the middle key is
    # a small sway, so a character holding guard is not a statue.
    "sword_ready": (1.60, True, [
        (0.00, SWORD_GUARD),
        (0.50, merged(torso(9.0, twist=-4.0), legs(0, 0),
                      leg("l", 15.0, 23.0, 6.0), leg("r", -7.0, 15.0, 2.0),
                      arm("r", 56.0, 60.0, hand=16.0), arm("l", 23.0, 71.0, hand=10.0))),
        (1.00, SWORD_GUARD),
    ]),
    "sword_attack": (0.90, False, [
        (0.00, SWORD_GUARD),
        (0.24, merged(torso(-10.0, twist=-26.0), leg("l", 10.0, 20.0, 4.0), leg("r", -14.0, 20.0, 2.0),
                      arm("r", 18.0, 78.0, hand=18.0), arm("l", 6.0, 84.0))),
        (0.34, merged(torso(12.0, twist=18.0), leg("l", 20.0, 26.0, 8.0), leg("r", 4.0, 18.0, 2.0),
                      arm("r", 74.0, 22.0, hand=4.0), arm("l", 34.0, 62.0))),
        (0.46, merged(torso(20.0, twist=34.0), leg("l", 26.0, 30.0, 10.0), leg("r", 10.0, 22.0, 4.0),
                      arm("r", 88.0, 40.0), arm("l", 46.0, 52.0))),
        (0.62, merged(torso(12.0, twist=18.0), leg("l", 20.0, 26.0, 8.0),
                      arm("r", 70.0, 56.0), arm("l", 34.0, 64.0))),
        (1.00, SWORD_GUARD),
    ]),
    "sword_block": (0.70, False, [
        (0.00, SWORD_GUARD),
        (0.15, merged(torso(-6.0, twist=-14.0, side=6.0), leg("l", 18.0, 28.0, 8.0),
                      leg("r", -12.0, 24.0, 4.0),
                      arm("r", 70.0, 84.0, hand=20.0), arm("l", 40.0, 90.0), head(-4.0))),
        (0.55, merged(torso(-6.0, twist=-14.0, side=6.0), leg("l", 18.0, 28.0, 8.0),
                      leg("r", -12.0, 24.0, 4.0),
                      arm("r", 70.0, 84.0, hand=20.0), arm("l", 40.0, 90.0), head(-4.0))),
        (1.00, SWORD_GUARD),
    ]),
    "polearm_ready": (1.60, True, [
        (0.00, POLEARM_GUARD),
        (0.50, merged(torso(7.0, twist=7.0),
                      leg("l", 17.0, 25.0, 6.0), leg("r", -9.0, 17.0, 2.0),
                      arm("r", 35.0, 47.0, hand=14.0, clavicle=-4.0), arm("l", 47.0, 65.0, hand=12.0))),
        (1.00, POLEARM_GUARD),
    ]),
    "bow_ready": (1.20, True, [
        (0.00, BOW_READY),
        (0.50, merged(torso(3.0, twist=9.0),
                      leg("l", 13.0, 19.0, 4.0), leg("r", -7.0, 13.0, 2.0),
                      arm("l", 67.0, 13.0, hand=6.0, clavicle=-6.0), arm("r", 27.0, 79.0, hand=12.0))),
        (1.00, BOW_READY),
    ]),
    "bow_draw_release": (1.10, False, [
        (0.00, BOW_READY),
        (0.30, merged(torso(4.0, twist=20.0),
                      leg("l", 14.0, 20.0, 4.0), leg("r", -10.0, 16.0, 2.0),
                      arm("l", 68.0, 12.0, hand=6.0, clavicle=-6.0), arm("r", 10.0, 96.0, hand=14.0))),
        (0.62, merged(torso(6.0, twist=26.0),
                      leg("l", 15.0, 21.0, 4.0), leg("r", -12.0, 18.0, 2.0),
                      arm("l", 70.0, 10.0, hand=6.0, clavicle=-8.0), arm("r", 4.0, 104.0, hand=16.0))),
        (0.70, merged(torso(2.0, twist=6.0),
                      leg("l", 13.0, 19.0, 4.0), leg("r", -8.0, 14.0, 2.0),
                      arm("l", 66.0, 12.0, hand=6.0, clavicle=-6.0), arm("r", 40.0, 24.0))),
        (1.00, BOW_READY),
    ]),
    "cast": (1.40, False, [
        (0.00, {}),
        (0.30, merged(torso(-6.0, twist=-12.0), leg("l", 10.0, 16.0, 4.0), leg("r", -6.0, 12.0, 2.0),
                      arm("r", -22.0, 72.0, hand=18.0), arm("l", -6.0, 40.0), head(-6.0))),
        (0.55, merged(torso(10.0, twist=-6.0), leg("l", 16.0, 22.0, 6.0), leg("r", -6.0, 14.0, 2.0),
                      arm("r", 72.0, 16.0, hand=-8.0), arm("l", 20.0, 54.0), head(4.0))),
        (0.72, merged(torso(10.0, twist=-6.0), leg("l", 16.0, 22.0, 6.0), leg("r", -6.0, 14.0, 2.0),
                      arm("r", 72.0, 16.0, hand=-8.0), arm("l", 30.0, 62.0), head(4.0))),
        (1.00, {}),
    ]),
    # Turning is a pose-only step: the pelvis yaws about its own axis and the feet swap weight, but
    # the root does not move, so the game still owns the character's facing.
    "turn_in_place": (0.70, False, [
        (0.00, {}),
        (0.25, merged(torso(4.0, twist=-16.0), hips(0.03, yaw=-18.0),
                      leg("l", 12.0, 30.0, 8.0), leg("r", -6.0, 14.0, 2.0))),
        (0.60, merged(torso(2.0, twist=-30.0), hips(0.02, yaw=-36.0),
                      leg("l", -8.0, 16.0, 2.0), leg("r", 14.0, 32.0, 8.0))),
        (1.00, {}),
    ]),
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--kind", required=True,
                        choices=("walk", "run", "sprint", "idle", "thrust") + tuple(ACTIONS))
    parser.add_argument("--out", required=True)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--seconds", type=float, default=None)
    args = parser.parse_args()

    if args.kind in GAITS:
        seconds = args.seconds or GAITS[args.kind]["period"]
        # One extra frame closes the loop: the last frame repeats the first pose.
        frames = locomotion_cycle(int(round(seconds * args.fps)) + 1, args.fps, args.kind)
    elif args.kind == "idle":
        seconds = args.seconds or 4.0
        frames = idle_cycle(int(round(seconds * args.fps)) + 1, args.fps)
    elif args.kind == "thrust":
        seconds = args.seconds or 0.82
        frames = thrust_strike(int(round(seconds * args.fps)) + 1, args.fps, seconds)
    else:
        duration, _loop, keys = ACTIONS[args.kind]
        seconds = args.seconds or duration
        frames = keyed_cycle(keys, int(round(seconds * args.fps)) + 1)

    data = {
        "schema_version": "1",
        "kind": args.kind,
        "fps": args.fps,
        "synthetic": True,
        "note": ("Generated for pipeline proof. Not production motion: it exists so the "
                 "retarget, bake, loop-check, export and Godot playback stages can be exercised "
                 "end to end before a real motion source is chosen."),
        "bones_animated": sorted({b for f in frames for b in f}),
        "frames": frames,
    }
    import os
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2)
    print(f"  wrote {len(frames)} frames at {args.fps} fps -> {args.out}")
    print(f"  animated bones: {len(data['bones_animated'])}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
