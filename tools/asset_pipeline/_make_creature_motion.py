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

The quadruped and humanoid creature clips (idle, walk, run, attack, hit, death) are written as
schema 2: what the body does, not which way each bone's local axis turns. A bone's local axes
depend on its roll, which differs from rig to rig; the old per-bone Euler angles therefore bent
knees and elbows backward and swung the humanoid attack behind the body on the fitted rigs, and
their hips-local "death" rotation yawed the quadrupeds instead of dropping them. Schema 2 says:

  body   fwd/up/side offsets (in the rig's mean leg length) and pitch/yaw/roll in the creature's
         own axes: pitch + nose down, yaw + toward its left, roll + right side down; forward is
         glTF +Z and up +Y on every rig. "pivot": "contacts" turns the body about the ground
         between its feet (a fall) instead of about the hips.
  bones  the same pitch/yaw/roll for spine, neck, head, tail... (.R bones mirrored).
  limbs  "foot": [out, up, fwd] ground contact relative to where the stance plants that foot
         (again in leg lengths; up 0 = the foot's lowest vertex on y = 0), "planted", "toe"
         (the foot or paw folding, + toes down/back), or forward kinematics: "swing" (+ toward the
         front), "raise" (+ out/up), "flex" [middle, end] (+ folds, never hyperextends); "ik"
         blends the two.
  ground "feet" (planted feet on the ground), "lie" (a body lying on it) or "free".

`_blender_anim_creature.py` resolves this against the rig's rest geometry and skinned mesh: bend
planes from each chain's rest shape, feet by IK, a fitted stance, and the ground rule after every
frame. The NPC kinds (talk, handover, work_*, sit) and the worm are still schema 1.

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

# The game plays a creature attack by holding the clip at the simulation's phase, not the clip's
# clock (SkinnedCreature.Pose): the Windup shows 0-45 % of the clip, Active 45-70 % and Recovery
# 70-100 %. So the beats come from that map: coil during the windup and hold it, strike at the
# start of Active so the blow is fully extended while Active is on screen (its first held frame is
# at about 50 % of the clip), hold it through Active, recover in Recovery.
HOLD_WINDUP_END, HOLD_ACTIVE_END = 0.45, 0.70
ATTACK_S = 0.9
WINDUP_END = 0.40 * ATTACK_S
COMMIT = HOLD_WINDUP_END * ATTACK_S
STRIKE_END = 0.51 * ATTACK_S
RECOVER = HOLD_ACTIVE_END * ATTACK_S

# The game moves the body and plays walk at speed / 1.6 and run at speed / 5 (SkinnedCreature.Pose,
# clamped to 0.6-1.6 and 0.7-1.5): at 1x a walk clip stands for 1.6 m/s and a run for 5 m/s, so a
# planted foot has to travel backward at exactly that speed or it slides in the game.
GAME_GAIT_SPEED = {"walk": 1.6, "run": 5.0}
# duty: share of the cycle a foot is planted. sweep: how far a planted foot travels under the body,
# in leg lengths. The baker (`_blender_anim_creature.py`) turns speed x clip length into a whole
# number of cycles whose stance travel is closest to sweep x the rig's leg length, which sets the
# stride and the cadence for that rig.
GAITS = {
    ("quadruped", "walk"): {"duty": 0.55, "sweep": 0.90, "lift": 0.10},      # a walking trot
    ("quadruped", "run"): {"duty": 0.30, "sweep": 1.10, "lift": 0.16},       # a flying trot
    ("humanoid", "walk"): {"duty": 0.62, "sweep": 0.85, "lift": 0.10},
    ("humanoid", "run"): {"duty": 0.40, "sweep": 1.05, "lift": 0.18},
    ("arthropod", "walk"): {"duty": 0.55, "sweep": 0.55, "lift": 0.12},
    ("arthropod", "run"): {"duty": 0.45, "sweep": 0.65, "lift": 0.16},
}
GAIT_SAMPLES = 161          # one cycle, phase 0-1; the baker resamples it onto the clip's keys


def gait_block(plan, kind):
    """The source's "gait" header: the baker sizes the stride from it (feet "fwd" are in stance
    travels: +0.5 where a foot lands, -0.5 where it lifts)."""
    g = GAITS[(plan, kind)]
    return {"speed_m_s": GAME_GAIT_SPEED[kind], "duty": g["duty"], "sweep": g["sweep"],
            "fwd_unit": "stance_travel"}


def _rot(x=0.0, y=0.0, z=0.0):
    return {"rotation": [x, y, z]}


# ------------------------------------------------------------------------------------------------
# Schema 2

QUADRUPED_RIG = {
    "body": "hips", "ground_bone": "root", "trunk": ["hips", "spine"],
    "limbs": {
        # Each chain bends the way its rest shape already bends (the fitted rigs' forelegs fold
        # back at the elbow, their hind legs back at the mid joint); a straight chain falls back
        # to the anatomy named after the bar.
        "front.L": {"bones": list(QUADRUPED_LEGS[0]), "side": "L", "bend": "rest|back", "contact": True},
        "front.R": {"bones": list(QUADRUPED_LEGS[1]), "side": "R", "bend": "rest|back", "contact": True},
        "rear.L": {"bones": list(QUADRUPED_LEGS[2]), "side": "L", "bend": "rest|forward", "contact": True},
        "rear.R": {"bones": list(QUADRUPED_LEGS[3]), "side": "R", "bend": "rest|forward", "contact": True},
    },
    # Standing: tilt the body nose-down (turning the neck back up) and/or drop it until every paw
    # reaches the ground with the legs at most 97% straight.
    "stance": {"tilt": "pitch", "counter": ["neck"], "extension": 0.97, "clearance": 0.01},
}

HUMANOID_RIG = {
    "body": "hips", "ground_bone": "root", "trunk": ["hips", "spine", "chest", "shoulder.L", "shoulder.R"],
    "limbs": {
        # Knees fold forward and elbows back, whatever the rest pose's few millimetres say.
        "leg.L": {"bones": list(HUMANOID_LIMBS[0]), "side": "L", "bend": "forward", "contact": True},
        "leg.R": {"bones": list(HUMANOID_LIMBS[1]), "side": "R", "bend": "forward", "contact": True},
        "arm.L": {"bones": ["upper_arm.L", "forearm.L", "hand.L"], "side": "L", "bend": "back"},
        "arm.R": {"bones": ["upper_arm.R", "forearm.R", "hand.R"], "side": "R", "bend": "back"},
    },
    # Standing: drop the body until both feet reach the ground with the knees at most 98.5%
    # straight; a leg that is shorter than the other (a fitted mesh stepping or with a raised foot)
    # is met by hiking the pelvis (rolled, the spine turned back upright) before bending the knees
    "stance": {"tilt": "roll", "counter": ["spine"], "tilt_range": [-8.0, 8.0], "tilt_cost": 0.002,
               "extension": 0.985, "clearance": 0.01},
}


def smooth(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3.0 - 2.0 * x)


def attack_beats(t, duration):
    """Windup, strike and recovery weights; mix() blends rest -> windup -> strike -> rest."""
    w = smooth(t / WINDUP_END)
    s = smooth((t - COMMIT) / (STRIKE_END - COMMIT))
    r = smooth((t - RECOVER) / (duration - RECOVER))

    def mix(windup, strike, rest=0.0):
        return (rest + (windup - rest) * w + (strike - windup) * s) * (1.0 - r) + rest * r
    return w, s, r, mix


def step(phase, duty, stride, lift, toe_swing, toe_push=0.0):
    """One foot through a gait cycle (phase 0-1): planted from +stride/2 to -stride/2 for the first
    `duty` of the cycle, then carried forward on an arc. Returns a limb intention."""
    phase %= 1.0
    if phase < duty:
        u = phase / duty
        toe = toe_push * smooth((u - 0.7) / 0.3)          # heel rising before the foot leaves
        return {"foot": [0.0, 0.0, stride * (0.5 - u)], "planted": True, "toe": toe}
    u = (phase - duty) / (1.0 - duty)
    fwd = stride * (-0.5 + (0.5 - 0.5 * math.cos(math.pi * u)))
    up = lift * math.sin(math.pi * u)
    toe = toe_push * (1.0 - u) ** 2 + toe_swing * math.sin(math.pi * u)
    return {"foot": [0.0, up, fwd], "planted": up <= 1e-9, "toe": toe}


def arc(fraction, reach, height):
    """A foot carried from 0 to `reach` forward over a `height` arc (fractions 0-1)."""
    return {"foot": [0.0, height * math.sin(math.pi * fraction), reach * fraction],
            "planted": fraction <= 0.0 or fraction >= 1.0}


def quadruped_intent(kind, t, duration, phase):
    legs = list(QUADRUPED_RIG["limbs"])
    body, bones, limbs, ground = {}, {}, {}, "feet"
    if kind == "idle":
        breath = math.sin(2 * math.pi * phase)             # 4.0 s
        look = math.sin(4 * math.pi * phase)               # 2.0 s: every period divides the loop
        body = {"up": 0.006 * breath, "side": 0.008 * math.sin(2 * math.pi * phase + 1.0),
                "pitch": 0.5 * breath}
        bones = {"spine": {"pitch": -0.8 * breath}, "neck": {"pitch": -1.5 * breath},
                 "head": {"yaw": 7.0 * look, "pitch": 2.0 * math.sin(2 * math.pi * phase + 0.5)},
                 "tail": {"yaw": 9.0 * look, "pitch": 3.0 * breath}}
        limbs = {n: {"foot": [0.0, 0.0, 0.0]} for n in legs}
    elif kind in ("walk", "run"):
        walk = kind == "walk"
        g = GAITS[("quadruped", kind)]
        duty, lift, toe = g["duty"], g["lift"], (40.0 if walk else 55.0)
        # a trot: the diagonal pairs move together; one cycle, feet in stance travels (gait_block)
        offsets = {"front.L": 0.0, "rear.R": 0.0, "front.R": 0.5, "rear.L": 0.5}
        limbs = {n: step(phase + offsets[n], duty, 1.0, lift, toe) for n in legs}
        # high at mid-stance in the walk, at mid-flight in the run
        c = math.cos(4 * math.pi * (phase - (duty / 2 if walk else (duty + 0.5) / 2)))
        body = {"up": (0.008 if walk else 0.035) * c, "pitch": (0.0 if walk else 2.0) + 1.2 * c,
                "roll": 1.5 * math.sin(2 * math.pi * phase), "yaw": 1.5 * math.sin(2 * math.pi * phase)}
        bones = {"spine": {"yaw": -3.0 * math.sin(2 * math.pi * phase), "pitch": -0.8 * c},
                 "neck": {"pitch": -1.5 * c - (0.0 if walk else 4.0)},
                 "head": {"pitch": 1.0 * c, "yaw": -1.5 * math.sin(2 * math.pi * phase)},
                 "tail": {"yaw": 12.0 * math.sin(2 * math.pi * phase + 0.8),
                          "pitch": 4.0 if walk else 10.0}}
    elif kind == "attack":
        w, s, r, mix = attack_beats(t, duration)
        # crouch back with the head drawn up, then lunge: the forefeet leave the ground while the
        # front rears in an arch, land a stride ahead for the hold with the head driven forward and
        # down, then step back; the hind feet stay planted and the legs take the stretch. The body
        # comes down as the forefeet land, so the ground rule never has to drop it in one frame.
        arch = math.sin(math.pi * s) * (1.0 - r)
        body = {"fwd": mix(-0.10, 0.34), "up": mix(-0.05, -0.08) + 0.04 * arch,
                "pitch": mix(5.0, 3.0) - 10.0 * arch}
        bones = {"spine": {"pitch": mix(3.0, 1.0) - 3.0 * arch}, "neck": {"pitch": mix(-14.0, 12.0)},
                 "head": {"pitch": mix(-8.0, 8.0) - 6.0 * arch}, "tail": {"pitch": mix(-8.0, 16.0)}}
        reach = 0.32
        if t < COMMIT:
            front = {"foot": [0.0, 0.0, 0.0]}
        elif t < STRIKE_END:
            front = arc(s, reach, 0.14)
        elif t < RECOVER:
            front = {"foot": [0.0, 0.0, reach]}
        else:
            back = arc(r, -reach, 0.08)
            front = {"foot": [0.0, back["foot"][1], reach + back["foot"][2]], "planted": back["planted"]}
        limbs = {"front.L": dict(front), "front.R": dict(front),
                 "rear.L": {"foot": [0.0, 0.0, 0.0]}, "rear.R": {"foot": [0.0, 0.0, 0.0]}}
    elif kind == "hit":
        k = math.sin(math.pi * min(t / duration, 1.0))
        # struck from the front: the head and forequarters snap up and back, feet stay put
        body = {"fwd": -0.07 * k, "pitch": -6.0 * k, "roll": 3.0 * k}
        bones = {"spine": {"pitch": -2.0 * k}, "neck": {"pitch": -14.0 * k},
                 "head": {"pitch": 10.0 * k, "yaw": 6.0 * k}, "tail": {"pitch": -12.0 * k}}
        limbs = {n: {"foot": [0.0, 0.0, 0.0]} for n in legs}
    elif kind == "death":
        # The legs buckle with the feet still planted and the body sinks, then it rolls onto its
        # right side and goes limp. The motion carries everything past the floor, the way a dead
        # weight falls: the body sinks further than the floor allows, the uppermost (left) legs,
        # the neck and the tail sag toward the ground below them. The ground rule stops each at
        # the floor - the trunk by lifting the body only as far as the trunk needs, a leg, the neck
        # or the tail by turning it up about its own root - so the corpse ends lying on its side.
        buckle = smooth(t / 0.45)
        roll = smooth((t - 0.35) / 0.75)
        settle = smooth((t - 0.95) / 0.6)
        free = smooth((t - 0.30) / 0.45)
        body = {"up": -0.35 * buckle - 0.6 * roll, "fwd": 0.04 * buckle,
                "pitch": 7.0 * buckle - 5.0 * roll, "roll": 84.0 * roll}
        bones = {"spine": {"pitch": 4.0 * buckle, "yaw": -8.0 * roll},
                 "neck": {"pitch": 18.0 * buckle + 8.0 * roll, "yaw": -25.0 * settle},
                 "head": {"pitch": 10.0 * buckle - 6.0 * settle, "yaw": -10.0 * settle},
                 # the tail points back, so a turn toward the left swings its tip right: down
                 "tail": {"pitch": -14.0 * buckle, "yaw": 30.0 * roll}}
        limbs = {}
        for n in legs:
            front = n.startswith("front")
            upper = n.endswith(".L")          # the side left on top
            limbs[n] = {"foot": [0.0, 0.0, 0.0], "ik": 1.0 - free,
                        "swing": (12.0 if front else -8.0) * roll,
                        "raise": (-35.0 if upper else 10.0) * settle,
                        "flex": [(55.0 - 30.0 * settle) * buckle, (40.0 - 15.0 * settle) * buckle]}
        ground = "lie"
    else:
        raise ValueError(f"no quadruped intention for {kind}")
    return {"body": body, "bones": bones, "limbs": limbs, "ground": ground}


def humanoid_intent(kind, t, duration, phase):
    body, bones, limbs, ground = {}, {}, {}, "feet"
    legs = ("leg.L", "leg.R")
    if kind == "idle":
        breath = math.sin(2 * math.pi * phase)             # 4.0 s
        look = math.sin(4 * math.pi * phase + 0.3)         # 2.0 s
        body = {"up": 0.004 * breath, "side": 0.010 * math.sin(2 * math.pi * phase),
                "roll": -1.0 * math.sin(2 * math.pi * phase)}
        bones = {"spine": {"pitch": 1.0 + 0.6 * breath}, "chest": {"pitch": -1.2 * breath},
                 "neck": {"pitch": 0.8 * breath}, "head": {"yaw": 6.0 * look, "pitch": -1.0 * breath}}
        for n in ("arm.L", "arm.R"):
            limbs[n] = {"swing": 3.0 + 2.0 * breath, "raise": 6.0 + 1.0 * breath,
                        "flex": [14.0 + 3.0 * breath, 4.0]}
        limbs.update({n: {"foot": [0.0, 0.0, 0.0]} for n in legs})
    elif kind in ("walk", "run"):
        walk = kind == "walk"
        # the run keeps a short flight (duty just under half) so it reads as running without the
        # body hanging in the air
        g = GAITS[("humanoid", kind)]
        duty, lift = g["duty"], g["lift"]
        toe_swing, toe_push = (-8.0, 22.0) if walk else (-6.0, 30.0)
        limbs = {"leg.L": step(phase, duty, 1.0, lift, toe_swing, toe_push),
                 "leg.R": step(phase + 0.5, duty, 1.0, lift, toe_swing, toe_push)}
        swing = 20.0 if walk else 36.0
        c = math.cos(2 * math.pi * phase)                   # + when the left foot is forward
        limbs["arm.L"] = {"swing": -swing * c, "raise": 8.0 if walk else 12.0,
                          "flex": [18.0 + 8.0 * max(0.0, -c) if walk else 75.0, 5.0]}
        limbs["arm.R"] = {"swing": swing * c, "raise": 8.0 if walk else 12.0,
                          "flex": [18.0 + 8.0 * max(0.0, c) if walk else 75.0, 5.0]}
        mid = duty / 2 if walk else (duty + 0.5) / 2         # walk: high at mid-stance; run: mid-flight
        bob = math.cos(4 * math.pi * (phase - mid))
        lean = 3.0 if walk else 10.0
        body = {"up": (0.012 if walk else 0.012) * bob, "yaw": -(5.0 if walk else 8.0) * c,
                "roll": 2.5 * math.cos(2 * math.pi * (phase - duty / 2))}
        bones = {"spine": {"pitch": lean, "yaw": (3.0 if walk else 5.0) * c},
                 "chest": {"yaw": (4.0 if walk else 6.0) * c},
                 "neck": {"pitch": -0.5 * lean}, "head": {"pitch": -0.4 * lean, "yaw": -1.5 * c}}
    elif kind == "attack":
        w, s, r, mix = attack_beats(t, duration)
        # an overhand blow with the right arm: raised and drawn back with the right shoulder
        # turned away, then driven down and forward (+Z) with the body lunging onto planted feet
        body = {"fwd": mix(-0.04, 0.12), "up": mix(-0.02, -0.05), "yaw": mix(-10.0, 12.0)}
        bones = {"spine": {"pitch": mix(-8.0, 14.0), "yaw": mix(-10.0, 8.0)},
                 "chest": {"pitch": mix(-6.0, 10.0), "yaw": mix(-15.0, 14.0)},
                 "neck": {"pitch": mix(6.0, -10.0)}, "head": {"pitch": mix(4.0, -8.0)}}
        # the windup stops well short of straight overhead: past ~110 deg the shoulder drags the
        # back of the husk's distance-weighted skin with it and opens the armour's armpit triangle
        limbs = {"arm.R": {"swing": mix(112.0, 50.0), "raise": mix(15.0, 8.0),
                           "flex": [mix(100.0, 10.0), mix(10.0, 0.0)]},
                 "arm.L": {"swing": mix(30.0, -25.0), "raise": mix(25.0, 18.0),
                           "flex": [mix(45.0, 30.0), 0.0]},
                 "leg.L": {"foot": [0.0, 0.0, 0.0]}, "leg.R": {"foot": [0.0, 0.0, 0.0]}}
    elif kind == "hit":
        k = math.sin(math.pi * min(t / duration, 1.0))
        # struck from the front: the chest and head snap back, the arms fly out
        body = {"fwd": -0.05 * k, "roll": 3.0 * k}
        bones = {"spine": {"pitch": -8.0 * k}, "chest": {"pitch": -8.0 * k},
                 "neck": {"pitch": 4.0 * k}, "head": {"pitch": -10.0 * k, "yaw": 8.0 * k}}
        limbs = {n: {"swing": -18.0 * k, "raise": 15.0 * k, "flex": [30.0 * k, 10.0 * k]}
                 for n in ("arm.L", "arm.R")}
        limbs.update({n: {"foot": [0.0, 0.0, 0.0]} for n in legs})
    elif kind == "death":
        # the knees buckle on planted feet, then the body topples forward about the ground between
        # the feet, gathering speed like a fall, lands face down and settles; the head turns aside
        buckle = smooth(t / 0.35)
        u = min(max((t - 0.25) / 0.85, 0.0), 1.0)
        fall = 90.0 * u * u                     # no rebound: a body that lands stays down
        free = smooth((t - 0.35) / 0.45)
        limp = smooth((t - 0.45) / 0.6)
        body = {"pitch": fall + 6.0 * buckle * (1.0 - u), "pivot": "contacts", "up": -0.10 * buckle}
        bones = {"spine": {"pitch": 12.0 * buckle * (1.0 - u) + 4.0 * u},
                 "chest": {"pitch": 6.0 * buckle * (1.0 - u)},
                 "neck": {"pitch": 10.0 * buckle - 25.0 * u},
                 "head": {"pitch": -10.0 * u, "yaw": 55.0 * smooth((t - 0.6) / 0.5)}}
        # Face down, the creature's forward is the floor. Once down, the legs and arms sag toward
        # it ("swing" forward) and the ground rule stops each on the floor by turning it up about
        # its hip or shoulder, so nothing is left propped or raised; the feet point, lying on
        # their tops.
        down = smooth((t - 1.00) / 0.45)
        for n in legs:
            limbs[n] = {"foot": [0.0, 0.0, 0.0], "ik": 1.0 - free, "follow": limp, "toe": 70.0 * limp,
                        "swing": 2.0 * u + 6.0 * down,
                        "flex": [6.0 * u + 20.0 * buckle * (1.0 - u) - 3.0 * down, 70.0 * limp]}
        # The arms reach forward to break the fall (the floor stops them, so they bend back
        # rather than holding the body up), then slide out to the sides and lie flat.
        catch = smooth((t - 0.40) / 0.45) * (1.0 - smooth((t - 1.10) / 0.40))
        flat = smooth((t - 1.10) / 0.40)
        for n in ("arm.L", "arm.R"):
            limbs[n] = {"swing": 5.0 + 40.0 * catch + 20.0 * flat, "raise": 8.0 + 22.0 * catch + 30.0 * flat,
                        "flex": [10.0 + 15.0 * catch - 6.0 * flat, 0.0]}
        ground = "lie"
    else:
        raise ValueError(f"no humanoid intention for {kind}")
    return {"body": body, "bones": bones, "limbs": limbs, "ground": ground}


INTENTIONS = {"quadruped": (QUADRUPED_RIG, quadruped_intent), "humanoid": (HUMANOID_RIG, humanoid_intent)}
CREATURE_KINDS = ("idle", "walk", "run", "attack", "hit", "death")
LOOPING = ("idle", "walk", "run")
DENSE = ("death",)                       # keyed at four times the frame rate (gaits: expand_gait)


def intentions(plan, kind, duration, fps):
    """Schema 2 clip: frames exactly duration / (frames - 1) apart, so the last key lands on the
    declared length and a loop's last frame equals its first (the baker stretches its frame rate
    to match, so the key rate need not be the nominal fps)."""
    rig, author = INTENTIONS[plan]
    if (plan, kind) in GAITS:
        # one gait cycle, sampled finely by phase; the baker fits it to the rig and the clip
        frames = []
        for index in range(GAIT_SAMPLES):
            phase = index / (GAIT_SAMPLES - 1)
            frame = author(kind, phase * duration, duration, phase)
            frame["phase"] = round(phase, 6)
            frames.append(frame)
        return {"schema_version": "2", "kind": kind, "plan": plan, "fps": fps, "duration_s": duration,
                "synthetic": True, "loop": True, "rig": rig, "gait": gait_block(plan, kind),
                "frames": frames}
    # a death ends in a fall onto the floor, fastest at the moment it lands, and its parts are
    # turned up off the floor frame by frame: keys four times as dense keep the feet, the body and
    # the limbs on the floor between keys as well as on them
    count = int(round(duration * fps * (4 if kind in DENSE else 1))) + 1
    frames = []
    for index in range(count):
        t = duration * index / (count - 1)
        frame = author(kind, t, duration, index / (count - 1))
        frame["t"] = round(t, 6)
        frames.append(frame)
    return {"schema_version": "2", "kind": kind, "plan": plan, "fps": fps, "duration_s": duration,
            "synthetic": True, "loop": kind in LOOPING, "rig": rig, "frames": frames}


def humanoid(kind, frames, fps, size):
    """The NPC kinds on the 20-bone rig (schema 1). The creature kinds are humanoid_intent()."""
    out = []
    period = (frames - 1) / fps if frames > 1 else 1.0
    for index in range(frames):
        t = index / fps
        phase = 2 * math.pi * t / period
        pose = {}
        if kind == "talk":
            # A conversational gesture: the right hand lifts and falls while the torso turns a little
            # toward the listener. Looping, because an NPC holds a conversation for an arbitrary time
            # and the clip has to survive being played continuously.
            beat = math.sin(phase)
            gesture = math.sin(phase * 2.0 + 0.6)
            pose = {
                "spine": _rot(beat * 2.0, 0, beat * 3.0),
                "chest": _rot(beat * 1.5, beat * 5.0, 0),
                "neck": _rot(-beat * 2.0, -beat * 4.0, 0),
                "head": _rot(beat * 3.0, -beat * 6.0, 0),
                "hips": {"rotation": [0, 0, beat * 1.5], "location": [0, beat * 0.004, 0]},
                # Right hand up and open, left hand resting low. Asymmetric on purpose: a symmetric
                # gesture reads as a mannequin rather than a person mid-sentence.
                "upper_arm.R": _rot(-52.0 + gesture * 14.0, 0, -18.0),
                "forearm.R": _rot(64.0 + gesture * 20.0),
                "hand.R": _rot(gesture * 12.0),
                "upper_arm.L": _rot(6.0 + beat * 2.0, 0, 8.0),
                "forearm.L": _rot(22.0 + beat * 3.0),
            }
        elif kind == "handover":
            # Offer an object with the right hand and withdraw. One-shot: it has a beginning, a moment
            # where the object changes hands, and a settled end, which is why it declares an event.
            duration = 1.35
            if t < 0.42:
                k = (t / 0.42) ** 0.8
            elif t < 0.62:
                k = 1.0
            else:
                k = max(0.0, 1.0 - (t - 0.62) / max(duration - 0.62, 1e-6))
            pose = {
                "spine": _rot(6.0 * k, 0, -5.0 * k),
                "chest": _rot(4.0 * k, -10.0 * k, 0),
                "neck": _rot(-4.0 * k, 8.0 * k, 0),
                "head": _rot(6.0 * k, 10.0 * k, 0),
                "hips": {"rotation": [0, 0, -3.0 * k], "location": [0, 0, 0]},
                "upper_arm.R": _rot(-64.0 * k, 0, -12.0 * k),
                "forearm.R": _rot(30.0 + 26.0 * k),
                "hand.R": _rot(-8.0 * k),
                "upper_arm.L": _rot(8.0 * k, 0, 6.0 * k),
                "forearm.L": _rot(26.0 * k),
                "thigh.L": _rot(-4.0 * k), "thigh.R": _rot(4.0 * k),
            }
        elif kind == "work_forge":
            # Striking down at an anvil. Both arms drive, the torso folds into the blow, and the hips
            # absorb it - the same shape as the combat attack but two-handed and shallower, because
            # this is labour rather than a swing.
            strike = 2 * math.pi * t / period
            drive = math.sin(strike)
            fold = max(0.0, math.sin(strike))
            pose = {
                "spine": _rot(10.0 + fold * 6.0),
                "chest": _rot(8.0 + fold * 5.0),
                "neck": _rot(-6.0 - fold * 3.0),
                "head": _rot(-4.0 - fold * 4.0),
                "hips": {"rotation": [4.0 + fold * 3.0, 0, 0],
                         "location": [0, -0.02 * fold * size, 0]},
                "upper_arm.R": _rot(-40.0 + drive * 46.0, 0, -10.0),
                "forearm.R": _rot(40.0 + drive * 24.0),
                "upper_arm.L": _rot(-30.0 + drive * 38.0, 0, 10.0),
                "forearm.L": _rot(44.0 + drive * 20.0),
                "thigh.L": _rot(-8.0), "thigh.R": _rot(8.0),
                "shin.L": _rot(-12.0), "shin.R": _rot(-6.0),
            }
        elif kind == "work_table":
            # Leaning over a survey table: weight forward, both hands out in front and low, small
            # working movements. The lean is constant so the pose reads at any frame.
            busy = math.sin(phase)
            pose = {
                "spine": _rot(20.0 + busy * 2.0),
                "chest": _rot(14.0 + busy * 2.0, busy * 4.0, 0),
                "neck": _rot(-16.0),
                "head": _rot(-14.0, busy * 5.0, 0),
                "hips": {"rotation": [8.0, 0, 0], "location": [0, -0.02 * size, 0]},
                "upper_arm.R": _rot(-34.0 + busy * 5.0, 0, -16.0),
                "forearm.R": _rot(58.0 + busy * 7.0),
                "hand.R": _rot(busy * 10.0),
                "upper_arm.L": _rot(-30.0 - busy * 5.0, 0, 16.0),
                "forearm.L": _rot(54.0 - busy * 7.0),
                "thigh.L": _rot(-14.0), "thigh.R": _rot(-12.0),
                "shin.L": _rot(-8.0), "shin.R": _rot(-10.0),
            }
        elif kind == "sit":
            # Seated: hips lowered to the seat height declared in the clip metadata, thighs forward and
            # level, shins down. A relaxed idle on top of that, so it loops.
            breath = math.sin(phase)
            pose = {
                "hips": {"rotation": [0, 0, 0], "location": [0, -0.45 * size, 0]},
                "thigh.L": _rot(78.0), "thigh.R": _rot(78.0),
                "shin.L": _rot(-80.0), "shin.R": _rot(-80.0),
                "foot.L": _rot(6.0), "foot.R": _rot(6.0),
                "spine": _rot(4.0 + breath * 1.0),
                "chest": _rot(2.0 + breath * 1.4),
                "neck": _rot(-2.0 - breath * 1.0),
                "head": _rot(breath * 2.0, breath * 3.0, 0),
                "upper_arm.L": _rot(14.0 + breath * 2.0, 0, 4.0),
                "upper_arm.R": _rot(14.0 + breath * 2.0, 0, -4.0),
                "forearm.L": _rot(46.0 + breath), "forearm.R": _rot(46.0 + breath),
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


# Schema 1 generators. The quadruped plan has none left: all of its kinds are schema 2.
GENERATORS = {"quadruped": None, "humanoid": humanoid, "worm": worm}

# Seconds per clip. Locomotion clips loop, so their length is the gait period.
DURATIONS = {
    "idle": 4.0,
    "walk": 1.2,
    "run": 0.8,
    "attack": 0.9,
    "hit": 0.55,
    "death": 1.9,
    # NPC social and work motions. The looping ones are long enough to survive being played
    # continuously without an obvious repeat; `handover` is one-shot and short because it has a
    # beginning and an end.
    "talk": 3.2,
    "handover": 1.35,
    "work_forge": 1.6,
    "work_table": 3.0,
    "sit": 4.0,
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

    seconds = DURATIONS[args.kind]
    if args.plan in INTENTIONS and args.kind in CREATURE_KINDS:
        data = intentions(args.plan, args.kind, seconds, args.fps)
        rig = data["rig"]
        data["bones_animated"] = sorted({b for f in data["frames"] for b in f["bones"]}
                                        | {b for limb in rig["limbs"].values() for b in limb["bones"]}
                                        | {rig["body"], rig["ground_bone"]})
    elif GENERATORS.get(args.plan) is None:
        raise SystemExit(f"the {args.plan} plan has no {args.kind} motion")
    else:
        size = SIZE_HINT.get(args.asset_id, 1.0)
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
    print(f"  {args.plan:<10} {args.kind:<7} {len(data['frames']):>3} frames  "
          f"{len(data['bones_animated']):>2} bones -> {os.path.basename(args.out)} "
          f"(schema {data['schema_version']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
