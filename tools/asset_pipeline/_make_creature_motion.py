"""Procedural motion for the three creature rig plans, in the retargeter's source format.

The player clips are authored against the canonical humanoid skeleton. Enemies are not: they are
rigged to their own armatures by `_blender_rig.py`, with 18 bones for a quadruped, 20 for a
humanoid creature and 5 for a worm. So the creature clips are generated against THOSE bone names
and applied directly to those rigs, rather than retargeted through the canonical body.

Five clips per enemy, which is what sprint section 9 asks for: idle, locomotion, attack, hit
reaction, death. Like the player's motion these are programmer animation - the brief wants
"reliable gameplay-capable motion through the complete pipeline", not final animation - but each
one is functional: a walk that reads as a gait, an attack with a windup and a hit window, a death
that collapses.

Usage:
    python _make_creature_motion.py --plan quadruped --kind walk --out .../wolf_walk.json
"""
import argparse
import json
import math
import os
import sys

QUADRUPED_LEGS = [
    ("front_upper.L", "front_lower.L", "front_paw.L"),
    ("front_upper.R", "front_lower.R", "front_paw.R"),
    ("rear_upper.L", "rear_lower.L", "rear_paw.L"),
    ("rear_upper.R", "rear_lower.R", "rear_paw.R"),
]

HUMANOID_LIMBS = [
    ("thigh.L", "shin.L", "foot.L"),
    ("thigh.R", "shin.R", "foot.R"),
]


def _rot(x=0.0, y=0.0, z=0.0):
    return {"rotation": [x, y, z]}


def quadruped(kind, frames, fps, size):
    """A diagonal-pair gait: front-left moves with rear-right, which is how a trot reads.

    Amplitudes scale with the creature's size so a wolf and a bear do not move identically.
    """
    stride = 26.0 * size
    out = []
    # Phase must complete exactly one cycle across the clip, or the loop does not close.
    period = (frames - 1) / fps if frames > 1 else 1.0
    for index in range(frames):
        t = index / fps
        phase = 2 * math.pi * t / period
        pose = {}
        if kind == "idle":
            # Every period here must divide the clip's duration or the loop does not close: a 3 s
            # tail sway inside a 4 s clip ends mid-cycle and leaves a visible seam. The clip is
            # 4.0 s, so the periods are 4.0 (breath), 2.0 (sway) and 2.0 (tail).
            breath = math.sin(phase)
            sway = math.sin(2 * math.pi * t / 2.0)
            pose = {
                "spine": _rot(breath * 1.2),
                "neck": _rot(-breath * 1.5),
                "head": _rot(breath * 1.0, 0.0, sway * 5.0),
                "tail": _rot(0.0, 0.0, sway * 9.0),
                "hips": {"rotation": [0, 0, 0], "location": [0, breath * 0.008, 0]},
            }
            for upper, lower, paw in QUADRUPED_LEGS:
                pose[upper] = _rot(breath * 0.8)
                pose[lower] = _rot(-breath * 1.0)
        elif kind in ("walk", "run"):
            speed = 1.0 if kind == "walk" else 1.8
            amp = stride * (1.0 if kind == "walk" else 1.5)
            for leg_index, (upper, lower, paw) in enumerate(QUADRUPED_LEGS):
                # Diagonal pairs share a phase: 0 and 3 together, 1 and 2 together.
                offset = 0.0 if leg_index in (0, 3) else math.pi
                swing = math.sin(phase - offset) * amp
                lift = max(0.0, math.sin(phase - offset - 0.9)) * amp * 1.3
                pose[upper] = _rot(swing)
                pose[lower] = _rot(-lift)
                pose[paw] = _rot(lift * 0.4)
            pose["hips"] = {"rotation": [math.sin(phase) * 3.0, 0, 0],
                            "location": [0, math.sin(2 * phase) * 0.012 * size, 0]}
            pose["spine"] = _rot(math.sin(phase) * 2.0)
            pose["neck"] = _rot(-math.sin(phase) * 3.0)
            pose["tail"] = _rot(0, 0, math.sin(phase * 0.5) * 12.0)
        elif kind == "attack":
            # Windup, bite, recover. The hit window lines up with the registry events.
            duration = 0.9
            if t < 0.25:
                k = t / 0.25
                reach, jaw = -18.0 * k, -10.0 * k
            elif t < 0.45:
                k = (t - 0.25) / 0.20
                reach, jaw = -18.0 + 58.0 * k, -10.0 + 30.0 * k
            elif t < 0.62:
                reach, jaw = 40.0, 20.0
            else:
                k = min((t - 0.62) / max(duration - 0.62, 1e-6), 1.0)
                reach, jaw = 40.0 * (1 - k), 20.0 * (1 - k)
            pose["neck"] = _rot(reach * 0.5)
            pose["head"] = _rot(reach * 0.5 + jaw * 0.3)
            pose["spine"] = _rot(reach * 0.25)
            pose["hips"] = {"rotation": [reach * 0.2, 0, 0], "location": [0, 0, 0]}
            for upper, lower, paw in QUADRUPED_LEGS[:2]:
                pose[upper] = _rot(-reach * 0.35)
                pose[lower] = _rot(reach * 0.25)
            for upper, lower, paw in QUADRUPED_LEGS[2:]:
                pose[upper] = _rot(reach * 0.30)
                pose[lower] = _rot(-reach * 0.20)
        elif kind == "hit":
            duration = 0.55
            k = math.sin(math.pi * min(t / duration, 1.0))
            pose["spine"] = _rot(-14.0 * k)
            pose["neck"] = _rot(20.0 * k)
            pose["head"] = _rot(-12.0 * k, 0, 8.0 * k)
            pose["hips"] = {"rotation": [-8.0 * k, 0, 0], "location": [0, -0.03 * k * size, 0]}
            for upper, lower, paw in QUADRUPED_LEGS:
                pose[upper] = _rot(-6.0 * k)
                pose[lower] = _rot(-10.0 * k)
        elif kind == "death":
            duration = 1.8
            k = min(t / duration, 1.0)
            ease = 1.0 - (1.0 - k) ** 2
            pose["hips"] = {"rotation": [0, 0, 42.0 * ease],
                            "location": [0, -0.10 * ease * size, 0]}
            pose["spine"] = _rot(8.0 * ease, 0, 18.0 * ease)
            pose["neck"] = _rot(-26.0 * ease)
            pose["head"] = _rot(30.0 * ease, 0, -14.0 * ease)
            pose["tail"] = _rot(0, 0, -22.0 * ease)
            for index, (upper, lower, paw) in enumerate(QUADRUPED_LEGS):
                fold = 34.0 * ease if index < 2 else -24.0 * ease
                pose[upper] = _rot(fold)
                pose[lower] = _rot(-abs(fold) * 1.1)
        out.append(pose)
    return out


def humanoid(kind, frames, fps, size):
    """A two-legged creature. Same gait shape as the player, scaled by size."""
    stride = 24.0 * size
    out = []
    period = (frames - 1) / fps if frames > 1 else 1.0
    for index in range(frames):
        t = index / fps
        phase = 2 * math.pi * t / period
        pose = {}
        if kind == "idle":
            breath = math.sin(phase)
            pose = {
                "spine": _rot(breath * 1.0),
                "chest": _rot(breath * 1.4),
                "neck": _rot(-breath * 1.0),
                "hips": {"rotation": [0, 0, 0], "location": [0, breath * 0.006, 0]},
                "upper_arm.L": _rot(breath * 2.0), "upper_arm.R": _rot(breath * 2.0),
                "forearm.L": _rot(8.0 + breath), "forearm.R": _rot(8.0 + breath),
            }
        elif kind in ("walk", "run"):
            amp = stride * (1.0 if kind == "walk" else 1.5)
            swing = math.sin(phase) * amp
            knee_l = max(0.0, math.sin(phase - 0.9)) * amp * 1.7
            knee_r = max(0.0, math.sin(phase + math.pi - 0.9)) * amp * 1.7
            pose = {
                "thigh.L": _rot(swing), "thigh.R": _rot(-swing),
                "shin.L": _rot(-knee_l), "shin.R": _rot(-knee_r),
                "foot.L": _rot(knee_l * 0.35), "foot.R": _rot(knee_r * 0.35),
                "spine": _rot(2.0),
                "chest": _rot(0, math.sin(phase) * 5.0, 0),
                "upper_arm.L": _rot(-swing * 0.8), "upper_arm.R": _rot(swing * 0.8),
                "forearm.L": _rot(18.0), "forearm.R": _rot(18.0),
                "hips": {"rotation": [0, 0, math.sin(phase) * 4.0],
                         "location": [0, math.sin(2 * phase) * 0.012 * size, 0]},
            }
        elif kind == "attack":
            duration = 0.85
            if t < 0.28:
                k = t / 0.28
                arm, torso = -34.0 * k, -9.0 * k
            elif t < 0.46:
                k = (t - 0.28) / 0.18
                arm, torso = -34.0 + 100.0 * k, -9.0 + 18.0 * k
            elif t < 0.62:
                arm, torso = 66.0, 9.0
            else:
                k = min((t - 0.62) / max(duration - 0.62, 1e-6), 1.0)
                arm, torso = 66.0 * (1 - k), 9.0 * (1 - k)
            pose = {
                "upper_arm.R": _rot(arm), "forearm.R": _rot(max(0.0, 55.0 - arm * 0.5)),
                "upper_arm.L": _rot(-arm * 0.3), "forearm.L": _rot(40.0),
                "spine": _rot(torso * 0.4), "chest": _rot(torso * 0.6, 0, 0),
                "hips": {"rotation": [torso * 0.3, 0, 0], "location": [0, 0, 0]},
                "thigh.L": _rot(-10.0), "thigh.R": _rot(10.0),
                "shin.L": _rot(-16.0), "shin.R": _rot(-8.0),
            }
        elif kind == "hit":
            duration = 0.55
            k = math.sin(math.pi * min(t / duration, 1.0))
            pose = {
                "spine": _rot(-12.0 * k), "chest": _rot(-10.0 * k),
                "neck": _rot(16.0 * k), "head": _rot(-10.0 * k, 0, 6.0 * k),
                "hips": {"rotation": [-6.0 * k, 0, 0], "location": [0, -0.02 * k, 0]},
                "upper_arm.L": _rot(-12.0 * k), "upper_arm.R": _rot(-12.0 * k),
            }
        elif kind == "death":
            duration = 1.9
            k = min(t / duration, 1.0)
            ease = 1.0 - (1.0 - k) ** 2
            pose = {
                "hips": {"rotation": [68.0 * ease, 0, 18.0 * ease],
                         "location": [0, -0.35 * ease * size, 0]},
                "spine": _rot(16.0 * ease, 0, -8.0 * ease),
                "chest": _rot(12.0 * ease), "neck": _rot(-18.0 * ease),
                "head": _rot(22.0 * ease, 0, 10.0 * ease),
                "thigh.L": _rot(52.0 * ease), "thigh.R": _rot(46.0 * ease),
                "shin.L": _rot(-70.0 * ease), "shin.R": _rot(-62.0 * ease),
                "upper_arm.L": _rot(-30.0 * ease), "upper_arm.R": _rot(-26.0 * ease),
            }
        out.append(pose)
    return out


def worm(kind, frames, fps, size):
    """A travelling wave along the chain. The only rig whose motion is not a gait at all."""
    segments = [f"segment_{i}" for i in range(1, 5)]
    out = []
    period = (frames - 1) / fps if frames > 1 else 1.0
    for index in range(frames):
        t = index / fps
        phase = 2 * math.pi * t / period
        pose = {}
        if kind == "idle":
            for seg_index, name in enumerate(segments):
                pose[name] = _rot(0.0, 0.0, math.sin(phase - seg_index * 0.6) * 6.0)
            pose["root"] = {"rotation": [0, 0, 0], "location": [0, 0, 0]}
        elif kind in ("walk", "run"):
            # The wave travels head-ward, which is what makes a serpent read as moving.
            amp = 16.0 if kind == "walk" else 26.0
            for seg_index, name in enumerate(segments):
                pose[name] = _rot(0.0, math.sin(phase - seg_index * 0.8) * amp, 0.0)
            pose["root"] = {"rotation": [0, 0, math.sin(phase) * 5.0], "location": [0, 0, 0]}
        elif kind == "attack":
            duration = 0.7
            if t < 0.3:
                k = t / 0.3
                coil = -22.0 * k
            elif t < 0.45:
                k = (t - 0.3) / 0.15
                coil = -22.0 + 60.0 * k
            else:
                k = min((t - 0.45) / max(duration - 0.45, 1e-6), 1.0)
                coil = 38.0 * (1 - k)
            for seg_index, name in enumerate(segments):
                pose[name] = _rot(0.0, coil * (0.35 + 0.2 * seg_index), 0.0)
            pose["root"] = {"rotation": [0, 0, 0], "location": [0, 0, 0]}
        elif kind == "hit":
            duration = 0.5
            k = math.sin(math.pi * min(t / duration, 1.0))
            for seg_index, name in enumerate(segments):
                pose[name] = _rot(0.0, -12.0 * k * (1.0 - 0.15 * seg_index), 0.0)
            pose["root"] = {"rotation": [0, 0, 0], "location": [0, 0, 0]}
        elif kind == "death":
            duration = 1.6
            k = min(t / duration, 1.0)
            ease = 1.0 - (1.0 - k) ** 2
            for seg_index, name in enumerate(segments):
                pose[name] = _rot(0.0, 8.0 * ease * (0.4 + 0.2 * seg_index), 0.0)
            pose["root"] = {"rotation": [0, 0, 12.0 * ease], "location": [0, 0, 0]}
        out.append(pose)
    return out


GENERATORS = {"quadruped": quadruped, "humanoid": humanoid, "worm": worm}

# Seconds per clip. Locomotion clips loop, so their length is the gait period.
DURATIONS = {
    "idle": 4.0,
    "walk": 1.2,
    "run": 0.8,
    "attack": 0.9,
    "hit": 0.55,
    "death": 1.9,
}

# Body-size multipliers so a wolf and a bear do not move with identical amplitude.
SIZE_HINT = {"creature_frost_wolf": 0.85, "creature_highland_brown_bear": 1.2,
             "creature_bone_walker_husk": 0.95, "creature_animated_armour": 1.05,
             "creature_great_river_serpent": 1.0}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--plan", required=True, choices=sorted(GENERATORS))
    parser.add_argument("--kind", required=True, choices=sorted(DURATIONS))
    parser.add_argument("--out", required=True)
    parser.add_argument("--asset-id", default=None)
    parser.add_argument("--fps", type=int, default=30)
    args = parser.parse_args()

    size = SIZE_HINT.get(args.asset_id, 1.0)
    seconds = DURATIONS[args.kind]
    frames = int(round(seconds * args.fps)) + 1
    poses = GENERATORS[args.plan](args.kind, frames, args.fps, size)

    data = {
        "schema_version": "1",
        "kind": args.kind,
        "plan": args.plan,
        "fps": args.fps,
        "synthetic": True,
        "bones_animated": sorted({b for pose in poses for b in pose}),
        "frames": poses,
    }
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2)
    print(f"  {args.plan:<10} {args.kind:<7} {frames:>3} frames  "
          f"{len(data['bones_animated']):>2} bones -> {os.path.basename(args.out)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
