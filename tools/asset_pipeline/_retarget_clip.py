"""Retarget one external clip onto a fitted 20-bone rig and register it: the animation retarget factory.

    SOURCE PACK CLIP -> BONE MAP (data) -> WORLD-SPACE ORIENTATION MATCH -> OUR RIG -> anim.<id>.glb + record

Method, per frame (glTF frame, +Y up, metres; the maths is numpy and runs without Blender):

  * The source is sampled from its own GLB with exact glTF semantics, never through Blender's importer,
    so its rest pose is the file's node rest (the T-pose) and nothing is re-guessed.
  * Root motion is taken off the source root first: every source bone is re-expressed as if the root had
    stayed at rest ("body frame"). The root's own travel is scaled and put back on the target `root`
    (--root keep) or stripped and only recorded (--root inplace).
  * Each mapped target bone takes its source bone's WORLD orientation through a constant offset
    calibrated in a matched rest:  R_target(t) = Q . R_drive(t) . R_drive(rest)^-1 . Q^-1 . R_matched.
    Q turns the source's facing onto the target's. R_matched is the target's own rest orientation, except
    for bones marked "align" (arms, legs): there the target bone is first swung so it points where the
    source bone points at the source rest - that is what absorbs the T-pose (source) to A-pose (target)
    difference, so an arm held horizontally in the source is horizontal on the target and not 45 degrees
    lower. Torso, neck, head and feet are not swung: their rests already agree, and the target neck is
    authored leaning forward, which a swing would undo.
  * An unmapped source bone folds into its nearest mapped parent. In a chain (three source spine bones
    onto two, a rigid wrist segment) the target bone is driven by the DEEPEST source bone before the next
    mapped one, so the folded bone's rotation lands on the target parent. Branches beyond the map
    (fingers, toes, weapon slots, IK helpers) are dropped with the report naming where each one folded.
  * A target bone with no source either follows its parent at its rest angle (the KayKit neck) or is
    derived: "derive": {"toward": <source joint>, "pivot": "midline"} swings the bone so it points from a
    fixed point on the source parent's midline to that joint (KayKit has no clavicle but moves the upper
    arm joint, so the target shoulder protracts and shrugs with it).
  * Positions are the target's own bone lengths (forward kinematics). Only the hips translate: the
    midpoint of the two thigh joints follows the source's, scaled by the leg-length ratio (--scale legs:
    thigh + shin of the target over the source's).
  * Feet (--feet ik, the default): each ankle is put on the source ankle's travel from rest, scaled the
    same way, by a two-bone solve that keeps the FK knee's hinge plane (thigh swing plus twist), so a
    planted source foot stays planted on a body whose thigh : shin ratio differs. A goal beyond the leg's
    reach first lowers the hips; what is lowered and anything still out of reach is recorded.

Blender does only the rig and the export: it imports the target GLB, keys pose bones with
basis = rest_local^-1 . wanted_local (glTF node locals; exact for a Blender-authored rig imported with the
BLENDER bone heuristic), removes the mesh and exports an animation-only GLB on the target's own rest,
which is what ArtLibrary.Retarget carries onto other bodies by bone name. The export is then read back
and every key compared with what was asked for; a mismatch fails the run.

The record is written only if `_animation_registry.validate_clip` passes. Provenance comes from the
pack's provenance.json beside the archive (pack, author, license, url) plus the archive's and the source
file's SHA-256, the clip name, the map and its SHA-256.

  blender --background --factory-startup --python _retarget_clip.py -- ^
      --source F:\\...\\KayKit_Character_Animations_Free_1.1.zip ^
      --member KayKit_Character_Animations_1.1/Animations/gltf/Rig_Medium/Rig_Medium_CombatRanged.glb ^
      --clip Ranged_Bow_Draw --map retarget_maps\\kaykit_rig_medium.json ^
      --target assets\\rigged\\player_veth_wanderer\\player_veth_wanderer_rigged.glb ^
      --id player.veth_wanderer.ext_bow_draw --category combat --clip-type transition ^
      --hand-left grip_primary --event windup_start:0
"""
import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
import zipfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _blender_rig_fit_humanoid20 as fit  # noqa: E402  (numpy GLB reader; bpy optional)
import _animation_registry as registry  # noqa: E402

DEFAULT_ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", "assets"))
EXPORT_ROT_TOLERANCE_DEG = 0.05
EXPORT_POS_TOLERANCE_M = 0.0005
ROOT_MOTION_MIN_M = 0.01          # less total root travel than this is "no root motion"


# ------------------------------------------------------------------------------------------------
# small rotation algebra (3x3 matrices; quaternions as x, y, z, w like glTF)

def qmat(q):
    x, y, z, w = np.asarray(q, float) / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def mat_quat(m):
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        q = [(m[2, 1] - m[1, 2]) / s, (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s, 0.25 * s]
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        q = [0.25 * s, (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s, (m[2, 1] - m[1, 2]) / s]
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        q = [(m[0, 1] + m[1, 0]) / s, 0.25 * s, (m[1, 2] + m[2, 1]) / s, (m[0, 2] - m[2, 0]) / s]
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        q = [(m[0, 2] + m[2, 0]) / s, (m[1, 2] + m[2, 1]) / s, 0.25 * s, (m[1, 0] - m[0, 1]) / s]
    q = np.array(q)
    return q / np.linalg.norm(q)


def ortho(m):
    """Nearest rotation (drops the 1e-7 scale noise Blender leaves in rests)."""
    u, _s, vt = np.linalg.svd(m)
    r = u @ vt
    if np.linalg.det(r) < 0:
        u[:, -1] *= -1
        r = u @ vt
    return r


def axis_angle(axis, angle):
    x, y, z = axis / np.linalg.norm(axis)
    c, s, C = math.cos(angle), math.sin(angle), 1 - math.cos(angle)
    return np.array([[c + x * x * C, x * y * C - z * s, x * z * C + y * s],
                     [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
                     [z * x * C - y * s, z * y * C + x * s, c + z * z * C]])


def swing(a, b):
    """Smallest rotation taking direction a onto direction b."""
    a = np.asarray(a, float) / np.linalg.norm(a)
    b = np.asarray(b, float) / np.linalg.norm(b)
    v = np.cross(a, b)
    s, c = np.linalg.norm(v), float(np.dot(a, b))
    if s < 1e-10:
        if c > 0:
            return np.eye(3)
        other = np.array([1.0, 0, 0]) if abs(a[0]) < 0.9 else np.array([0, 0, 1.0])
        return axis_angle(np.cross(a, other), math.pi)
    return axis_angle(v / s, math.atan2(s, c))


def angle_deg(a, b):
    a = np.asarray(a, float) / np.linalg.norm(a)
    b = np.asarray(b, float) / np.linalg.norm(b)
    return math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(a, b))))))


def rot_angle_deg(r):
    return math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(r) - 1) / 2))))


def trs(t, r, s):
    m = np.eye(4)
    m[:3, :3] = qmat(r) * np.asarray(s, float)
    m[:3, 3] = t
    return m


def compose(r, p):
    m = np.eye(4)
    m[:3, :3] = r
    m[:3, 3] = p
    return m


def slerp(q0, q1, a):
    q0, q1 = np.asarray(q0, float), np.asarray(q1, float)
    d = float(np.dot(q0, q1))
    if d < 0:
        q1, d = -q1, -d
    if d > 0.9995:
        q = q0 + a * (q1 - q0)
        return q / np.linalg.norm(q)
    th = math.acos(d)
    return (math.sin((1 - a) * th) * q0 + math.sin(a * th) * q1) / math.sin(th)


# ------------------------------------------------------------------------------------------------
# a glTF scene with exact sampling and forward kinematics

class Scene:
    def __init__(self, path):
        self.path = path
        self.doc, self.blob = fit.read_glb(path)
        nodes = self.doc["nodes"]
        self.names = [n.get("name", f"node_{i}") for i, n in enumerate(nodes)]
        self.index = {}
        for i, name in enumerate(self.names):
            self.index.setdefault(name, i)
        self.parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        self.rest = []
        for n in nodes:
            if "matrix" in n:
                raise ValueError(f"{path}: node '{n.get('name')}' uses a matrix; TRS nodes only")
            self.rest.append((np.array(n.get("translation", [0, 0, 0]), float),
                              np.array(n.get("rotation", [0, 0, 0, 1]), float),
                              np.array(n.get("scale", [1, 1, 1]), float)))

        def depth(i):
            d = 0
            while i in self.parent:
                i, d = self.parent[i], d + 1
            return d
        self.order = sorted(range(len(nodes)), key=depth)
        skins = self.doc.get("skins", [])
        self.joints = [self.names[j] for j in skins[0]["joints"]] if skins else []

    def clip_names(self):
        return [a.get("name") for a in self.doc.get("animations", [])]

    def tracks(self, clip):
        anims = [a for a in self.doc.get("animations", []) if a.get("name") == clip]
        if not anims:
            raise SystemExit(f"{os.path.basename(self.path)} has no clip '{clip}'; it has: "
                             f"{', '.join(self.clip_names())}")
        anim, out = anims[0], {}
        for ch in anim["channels"]:
            s = anim["samplers"][ch["sampler"]]
            t = fit.accessor(self.doc, self.blob, s["input"]).astype(np.float64).ravel()
            v = fit.accessor(self.doc, self.blob, s["output"]).astype(np.float64).reshape(-1,
                  {"rotation": 4, "translation": 3, "scale": 3}.get(ch["target"]["path"], 1))
            interp = s.get("interpolation", "LINEAR")
            if interp == "CUBICSPLINE":        # in-tangent, value, out-tangent per key: keep values
                v, interp = v[1::3], "LINEAR"
            out[(ch["target"]["node"], ch["target"]["path"])] = (t, v, interp)
        return out

    @staticmethod
    def duration(tracks):
        return max(float(t[-1]) for t, _v, _i in tracks.values())

    @staticmethod
    def key_rate(tracks):
        t = max((t for t, _v, _i in tracks.values()), key=len)
        return 1.0 / float(np.median(np.diff(t))) if len(t) > 1 else 30.0

    @staticmethod
    def sample(track, time, path):
        t, v, interp = track
        if time <= t[0]:
            return v[0]
        if time >= t[-1]:
            return v[-1]
        k = int(np.searchsorted(t, time, side="right") - 1)
        if interp == "STEP":
            return v[k]
        a = (time - t[k]) / max(t[k + 1] - t[k], 1e-12)
        if path == "rotation":
            return slerp(v[k], v[k + 1], a)
        return (1 - a) * v[k] + a * v[k + 1]

    def local(self, tracks=None, time=0.0):
        out = []
        for i, (t, r, s) in enumerate(self.rest):
            if tracks:
                if (i, "translation") in tracks:
                    t = self.sample(tracks[(i, "translation")], time, "translation")
                if (i, "rotation") in tracks:
                    r = self.sample(tracks[(i, "rotation")], time, "rotation")
                if (i, "scale") in tracks:
                    s = self.sample(tracks[(i, "scale")], time, "scale")
            out.append(trs(t, r, s))
        return out

    def world(self, local):
        w = [None] * len(local)
        for i in self.order:
            p = self.parent.get(i)
            w[i] = local[i] if p is None else w[p] @ local[i]
        return w

    def pos(self, worlds, name):
        return worlds[self.index[name]][:3, 3].copy()

    def rot(self, worlds, name):
        return ortho(worlds[self.index[name]][:3, :3])


# ------------------------------------------------------------------------------------------------
# the retarget

class Retarget:
    def __init__(self, source, target, bmap, scale_mode="legs"):
        self.src, self.tgt, self.map = source, target, bmap
        self.S0 = source.world(source.local())
        self.T0 = target.world(target.local())
        self.bones = [n for n in (target.names[i] for i in target.order) if n in target.joints]
        missing = [b for b in self.bones if b not in bmap["bones"]]
        if missing:
            raise SystemExit(f"map declares nothing for target bone(s) {missing} (use \"source\": null)")
        self.chain = bmap.get("target_chain", {})
        self.entry = {b: bmap["bones"][b] for b in self.bones}
        for b, e in self.entry.items():
            for key in ("source", "drive", "aim"):
                if e.get(key) and e[key] not in source.index:
                    raise SystemExit(f"map: {b}.{key} '{e[key]}' is not a node of the source")
            if e.get("derive") and e["derive"]["toward"] not in source.index:
                raise SystemExit(f"map: {b}.derive.toward '{e['derive']['toward']}' is not a node")
        self.root_src = bmap.get("source_root")
        self.tparent = {}
        for b in self.bones:
            p = target.parent.get(target.index[b])
            self.tparent[b] = target.names[p] if p is not None and target.names[p] in target.joints else None

        # facing: the source's left-right axis turned onto the target's, about +Y
        ls = self._pos0(bmap["source_lateral"][0]) - self._pos0(bmap["source_lateral"][1])
        lt = (target.pos(self.T0, bmap["target_lateral"][0]) - target.pos(self.T0, bmap["target_lateral"][1]))
        yaw = math.degrees(math.atan2(lt[0], lt[2]) - math.atan2(ls[0], ls[2]))
        self.yaw_measured_deg = (yaw + 180) % 360 - 180
        # rigs are authored facing an axis; a fitted rig's slightly uneven hips must not turn the motion
        snapped = round(self.yaw_measured_deg / 90.0) * 90.0
        self.yaw_deg = snapped if abs(snapped - self.yaw_measured_deg) < 10 else self.yaw_measured_deg
        self.Q = axis_angle(np.array([0, 1.0, 0]), math.radians(self.yaw_deg))
        self.lateral_src = ls / np.linalg.norm(ls)

        # scale: leg length (thigh + shin) of the target over the source's
        def leg(scene, worlds, names):
            p = [scene.pos(worlds, n) for n in names]
            return np.linalg.norm(p[1] - p[0]) + np.linalg.norm(p[2] - p[1])
        tl = np.mean([leg(target, self.T0, c) for c in bmap["target_legs"]])
        sl = np.mean([leg(source, self.S0, [self.entry[b]["source"] for b in c]) for c in bmap["target_legs"]])
        hip_t = target.pos(self.T0, "hips")[1]
        hip_s = self._pos0(self.entry["hips"]["source"])[1]
        self.scale_legs, self.scale_hips = tl / sl, hip_t / hip_s
        if scale_mode == "legs":
            self.k = self.scale_legs
        elif scale_mode == "hips":
            self.k = self.scale_hips
        else:
            self.k = float(scale_mode)
        self.scale_mode = scale_mode

        # drive bones (a chain's folded bones land on the parent) and alignment offsets
        self.drive, self.offset, self.aligned, self.derived, self.follow = {}, {}, [], [], []
        for b in self.bones:
            e = self.entry[b]
            if not e.get("source"):
                if e.get("derive"):
                    self.derived.append(b)
                    self._prepare_derived(b)
                else:
                    self.follow.append(b)
                continue
            d = e.get("drive") or self._auto_drive(b)
            self.drive[b] = d
            R_rest = target.rot(self.T0, b)
            if e.get("align"):
                dt = self._target_dir(b)
                ds = self.Q @ self._source_dir(b)
                R_match = swing(dt, ds) @ R_rest
                self.aligned.append({"bone": b, "rest_swing_deg": round(angle_deg(dt, ds), 2)})
            else:
                R_match = R_rest
            self.offset[b] = (self.Q @ source.rot(self.S0, d)).T @ R_match

        # hips anchor: the midpoint of the thigh joints
        legs = bmap["target_legs"]
        self.mid_t0 = np.mean([target.pos(self.T0, c[0]) for c in legs], axis=0)
        self.mid_src = [self.entry[c[0]]["source"] for c in legs]
        self.mid_s0 = np.mean([self._pos0(n) for n in self.mid_src], axis=0)
        self.leg_span = {c[2]: np.linalg.norm(target.pos(self.T0, c[1]) - target.pos(self.T0, c[0]))
                         + np.linalg.norm(target.pos(self.T0, c[2]) - target.pos(self.T0, c[1])) for c in legs}

        # what every source joint became
        used = {e.get("source") for e in self.entry.values()} | set(self.drive.values())
        owner = {}
        for b, e in self.entry.items():
            if e.get("source"):
                owner[e["source"]] = b
        for b, d in self.drive.items():
            owner[d] = b
        self.folded = {}
        for name in source.joints or source.names:
            if name in used or name == self.root_src:
                continue
            i = source.index[name]
            while i in source.parent and source.names[i] not in owner:
                i = source.parent[i]
            self.folded[name] = owner.get(source.names[i], "dropped")

    # -- rest helpers
    def _pos0(self, name):
        return self.src.pos(self.S0, name)

    def _next_mapped(self, b):
        c = self.chain.get(b)
        while c is not None and not self.entry[c].get("source"):
            c = self.chain.get(c)
        return c

    def _auto_drive(self, b):
        s = self.entry[b]["source"]
        c = self._next_mapped(b)
        if c is None:
            return s
        i = self.src.parent.get(self.src.index[self.entry[c]["source"]])
        # the deepest source bone before the next mapped one, if it lies below this bone's source
        j = i
        while j is not None:
            if self.src.names[j] == s:
                return self.src.names[i]
            j = self.src.parent.get(j)
        return s

    def _target_dir(self, b):
        c = self.chain.get(b)
        if c:
            return self.tgt.pos(self.T0, c) - self.tgt.pos(self.T0, b)
        return self.tgt.rot(self.T0, b) @ np.array([0, 1.0, 0])

    def _source_dir(self, b):
        e = self.entry[b]
        aim = e.get("aim")
        if not aim:
            c = self._next_mapped(b)
            aim = self.entry[c]["source"] if c else None
        if aim:
            return self._pos0(aim) - self._pos0(e["source"])
        return self.src.rot(self.S0, self.drive[b]) @ np.array([0, 1.0, 0])

    def _parent_drive(self, b):
        p = self.tparent[b]
        while p is not None and p not in self.drive:
            p = self.tparent[p]
        return self.drive.get(p)

    def _prepare_derived(self, b):
        spec = self.entry[b]["derive"]
        par = self._parent_drive(b)
        if par is None:
            raise SystemExit(f"map: derived bone {b} has no mapped parent to hang from")
        W = self.S0[self.src.index[par]]
        c_local = np.linalg.inv(W) @ np.r_[self._pos0(spec["toward"]), 1.0]
        pivot = c_local[:3].copy()
        if spec.get("pivot", "midline") == "midline":
            lat = W[:3, :3].T @ self.lateral_src
            lat /= np.linalg.norm(lat)
            pivot -= np.dot(pivot, lat) * lat
        spec["_parent"], spec["_pivot"] = par, np.r_[pivot, 1.0]

    # -- per frame
    def source_body(self, tracks, time):
        """Source worlds with the root's motion removed, and that motion (rotation, translation)."""
        W = self.src.world(self.src.local(tracks, time))
        if not self.root_src:
            return W, np.eye(3), np.zeros(3)
        r = self.src.index[self.root_src]
        R0, Rt = self.S0[r], W[r]
        undo = R0 @ np.linalg.inv(Rt)
        body = [undo @ m for m in W]
        dR = ortho(Rt[:3, :3]) @ ortho(R0[:3, :3]).T
        dp = Rt[:3, 3] - R0[:3, 3]
        return body, dR, dp

    def _ankle_goal(self, body, foot):
        s_ankle = self.entry[foot]["source"]
        return self.tgt.pos(self.T0, foot) + self.k * (
            self.Q @ (self.src.pos(body, s_ankle) - self._pos0(s_ankle)))

    def _two_bone(self, world, thigh, shin, foot, goal):
        """Put the ankle on `goal` by turning thigh and shin in the plane of the FK knee; the foot
        keeps its world orientation. Returns how far short an out-of-reach goal stays (m)."""
        tgt, T0 = self.tgt, self.T0
        a = np.linalg.norm(tgt.pos(T0, shin) - tgt.pos(T0, thigh))
        b = np.linalg.norm(tgt.pos(T0, foot) - tgt.pos(T0, shin))
        P, knee_fk, ankle_fk = world[thigh][:3, 3], world[shin][:3, 3], world[foot][:3, 3]
        d = goal - P
        dist = float(np.linalg.norm(d))
        u = d / dist
        reach = min(max(dist, abs(a - b) + 1e-6), (a + b) * (1 - 1e-6))
        x = (a * a - b * b + reach * reach) / (2 * reach)
        h = math.sqrt(max(a * a - x * x, 0.0))
        pole = knee_fk - P
        pole = pole - np.dot(pole, u) * u
        if np.linalg.norm(pole) < 1e-9:           # a straight FK leg: bend the knee forward
            pole = self.Q @ np.array([0, 0, 1.0])
            pole = pole - np.dot(pole, u) * u
        knee = P + x * u + h * pole / np.linalg.norm(pole)
        end = P + reach * u
        # carry the FK knee's hinge plane onto the solved one (thigh swing plus twist), so the knee stays a
        # hinge in the thigh's own frame instead of bending sideways when the goal leaves the FK plane
        n_fk = np.cross(knee_fk - P, ankle_fk - knee_fk)
        n_new = np.cross(knee - P, end - knee)
        if np.linalg.norm(n_fk) > 1e-6 and np.linalg.norm(n_new) > 1e-6:
            def frame_of(axis, normal):
                ax = axis / np.linalg.norm(axis)
                n = normal - np.dot(normal, ax) * ax
                n /= np.linalg.norm(n)
                return np.column_stack([ax, n, np.cross(ax, n)])
            turn = frame_of(knee - P, n_new) @ frame_of(knee_fk - P, n_fk).T
        else:
            turn = swing(knee_fk - P, knee - P)
        shin_turn = swing(turn @ (ankle_fk - knee_fk), end - knee)
        world[thigh] = compose(turn @ world[thigh][:3, :3], P)
        world[shin] = compose(shin_turn @ turn @ world[shin][:3, :3], knee)
        world[foot] = compose(world[foot][:3, :3], end)
        return max(dist - (a + b), 0.0)

    def frame(self, tracks, time, root_mode="keep", feet="ik"):
        """Target joint locals (glTF node matrices, by name) at `time`, plus the root travel."""
        B, dR, dp = self.source_body(tracks, time)
        src, tgt = self.src, self.tgt
        world, goals, hips_drop = {}, {}, 0.0
        for b in self.bones:
            p = self.tparent[b]
            Lr = trs(*tgt.rest[tgt.index[b]])
            if b in self.drive:
                R = self.Q @ ortho(B[src.index[self.drive[b]]][:3, :3]) @ self.offset[b]
            elif b in self.derived:
                spec = self.entry[b]["derive"]
                Wp = B[src.index[spec["_parent"]]]
                Wp0 = self.S0[src.index[spec["_parent"]]]
                D = ortho(Wp[:3, :3]) @ ortho(Wp0[:3, :3]).T
                v0 = self._pos0(spec["toward"]) - (Wp0 @ spec["_pivot"])[:3]
                v = src.pos(B, spec["toward"]) - (Wp @ spec["_pivot"])[:3]
                R_virtual = swing(D @ v0, v) @ D
                R = self.Q @ R_virtual @ self.Q.T @ tgt.rot(self.T0, b)
            else:
                R = ortho(world[p][:3, :3] @ ortho(Lr[:3, :3])) if p else tgt.rot(self.T0, b)
            if p is None:                       # the root: at rest in the body frame
                P = tgt.pos(self.T0, b)
            elif b == "hips":
                mid_s = np.mean([src.pos(B, n) for n in self.mid_src], axis=0)
                mid_t = self.mid_t0 + self.k * (self.Q @ (mid_s - self.mid_s0))
                R0 = tgt.rot(self.T0, b)
                P = mid_t - R @ R0.T @ (self.mid_t0 - tgt.pos(self.T0, b))
                if feet == "ik":
                    # a stance wider than this body's legs can span: lower the hips until both ankle goals
                    # are in reach, rather than let a foot hang short of its plant
                    drop = 0.0
                    for thigh, shin, foot in self.map["target_legs"]:
                        goals[foot] = self._ankle_goal(B, foot)
                        hip_joint = P + R @ R0.T @ (tgt.pos(self.T0, thigh) - tgt.pos(self.T0, b))
                        d = goals[foot] - hip_joint
                        span = self.leg_span[foot] * 0.999
                        h = float(np.linalg.norm(d[[0, 2]]))
                        if np.linalg.norm(d) > span and h < span:
                            drop = max(drop, -d[1] - math.sqrt(span * span - h * h))
                    P = P - np.array([0, max(drop, 0.0), 0])
                    hips_drop = max(drop, 0.0)
            else:
                P = (world[p] @ np.r_[Lr[:3, 3], 1.0])[:3]
            world[b] = compose(R, P)

        # feet: the ankle follows the source ankle's scaled travel, so a planted source foot stays planted
        # on a body whose thigh : shin ratio differs (FK alone slides it)
        shortfall = {"hips_drop": hips_drop}
        if feet == "ik":
            for thigh, shin, foot in self.map["target_legs"]:
                shortfall[foot] = self._two_bone(world, thigh, shin, foot, goals[foot])

        travel = self.k * (self.Q @ dp)
        turn = self.Q @ dR @ self.Q.T
        locals_ = {}
        for b in self.bones:
            p = self.tparent[b]
            if p is None:
                parent_world = self.T0[tgt.parent[tgt.index[b]]] if tgt.index[b] in tgt.parent else np.eye(4)
                W = world[b]
                if root_mode == "keep":
                    W = compose(turn @ W[:3, :3], W[:3, 3] + travel)
                locals_[b] = np.linalg.inv(parent_world) @ W
            else:
                locals_[b] = np.linalg.inv(world[p]) @ world[b]
        return locals_, travel, turn, shortfall

    def run(self, tracks, fps, root_mode="keep", feet="ik"):
        duration = Scene.duration(tracks)
        n = int(round(duration * fps)) + 1
        times = [min(i / fps, duration) for i in range(n)]
        frames, travel, short, drop = [], [], [], []
        for t in times:
            locals_, tr, _turn, facts = self.frame(tracks, t, root_mode, feet)
            frames.append(locals_)
            travel.append(tr)
            drop.append(facts.pop("hips_drop"))
            short.append(max(facts.values(), default=0.0))
        self.ik_facts = {"feet": feet, "ik_out_of_reach_frames": int(sum(s > 1e-4 for s in short)),
                         "ik_max_shortfall_m": round(float(max(short, default=0.0)), 4),
                         "ik_hips_lowered_frames": int(sum(d > 1e-4 for d in drop)),
                         "ik_hips_lowered_max_m": round(float(max(drop, default=0.0)), 4)}
        return times, frames, np.array(travel)


# ------------------------------------------------------------------------------------------------
# inputs, provenance, record

def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def open_source(source, member, workdir):
    """(local path of the source GLB, provenance facts). A zip member is extracted to `workdir`."""
    facts = {"archive": None, "archive_sha256": None}
    if source.lower().endswith(".zip"):
        if not member:
            raise SystemExit("--member is required when --source is a zip")
        facts["archive"] = os.path.basename(source)
        facts["archive_sha256"] = sha256_file(source)
        with zipfile.ZipFile(source) as zf:
            data = zf.read(member)
        path = os.path.join(workdir, os.path.basename(member))
        with open(path, "wb") as handle:
            handle.write(data)
        facts["file"] = member
        facts["file_sha256"] = hashlib.sha256(data).hexdigest()
    else:
        path = source
        facts["file"] = os.path.basename(source)
        facts["file_sha256"] = sha256_file(source)
    return path, facts


def load_provenance(source, explicit):
    path = explicit or os.path.join(os.path.dirname(os.path.abspath(source)), "provenance.json")
    if not os.path.exists(path):
        raise SystemExit(f"no provenance.json at {path}; give --provenance")
    with open(path, encoding="utf-8") as handle:
        return json.load(handle), path


def family_dir(animation_id):
    """The folder ArtLibrary.Clip looks in: the id's first segment, creatures pluralised."""
    first = animation_id.split(".")[1]
    return {"creature": "creatures"}.get(first, first)


def root_events(travel, times):
    dist = np.linalg.norm(travel - travel[0], axis=1)
    total = float(dist.max())
    if total < ROOT_MOTION_MIN_M:
        return []
    moving = [i for i in range(1, len(times)) if np.linalg.norm(travel[i] - travel[i - 1]) > 1e-4]
    return [{"id": "root_motion_start", "time": round(times[max(moving[0] - 1, 0)], 4)},
            {"id": "root_motion_end", "time": round(times[moving[-1]], 4)}]


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--source", required=True, help="source GLB, or the pack's zip with --member")
    p.add_argument("--member", help="path of the GLB inside the zip")
    p.add_argument("--clip", required=True, help="clip name inside the source GLB")
    p.add_argument("--map", required=True, help="retarget_maps/<source skeleton>.json")
    p.add_argument("--target", required=True, help="the rigged GLB whose skeleton receives the motion")
    p.add_argument("--id", required=True, help="animation id, e.g. player.veth_wanderer.ext_bow_draw")
    p.add_argument("--root", choices=("keep", "inplace"), default="keep")
    p.add_argument("--scale", default="legs", help="legs | hips | <number>")
    p.add_argument("--feet", choices=("ik", "fk"), default="ik",
                   help="ik: ankles follow the source ankles' scaled travel (no slide); fk: rotations only")
    p.add_argument("--fps", type=float, default=60.0,
                   help="bake rate (default 60, the game frame rate and the authored player clips' rate)")
    p.add_argument("--category", default="locomotion")
    p.add_argument("--clip-type", default="transition")
    p.add_argument("--loop", action="store_true")
    p.add_argument("--hand-left", default="open")
    p.add_argument("--hand-right", default="open")
    p.add_argument("--event", action="append", default=[], help="id:seconds (repeatable)")
    p.add_argument("--tag", action="append", default=[])
    p.add_argument("--skeleton-family", default="creature_humanoid")
    p.add_argument("--notes", default="")
    p.add_argument("--provenance", help="default: provenance.json beside --source")
    p.add_argument("--assets", default=DEFAULT_ASSETS)
    return p.parse_args(argv)


def export_with_blender(target_path, bones, rest_locals, frames, fps, out_path, action_name):
    import bpy
    from mathutils import Matrix, Quaternion

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=target_path, bone_heuristic="BLENDER", disable_bone_shape=True)
    arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if len(arms) != 1:
        raise SystemExit(f"{target_path}: expected one armature, found {len(arms)}")
    rig = arms[0]
    for obj in list(bpy.context.scene.objects):
        if obj is not rig:
            bpy.data.objects.remove(obj, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)

    scene = bpy.context.scene
    scene.render.fps = int(round(fps))
    scene.render.fps_base = int(round(fps)) / fps
    scene.frame_start, scene.frame_end = 0, len(frames) - 1
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    previous = {}
    for f, locals_ in enumerate(frames):
        for name in bones:
            pb = rig.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            basis = np.linalg.inv(rest_locals[name]) @ locals_[name]
            q = mat_quat(ortho(basis[:3, :3]))                 # x, y, z, w
            q = Quaternion((q[3], q[0], q[1], q[2]))
            if name in previous and previous[name].dot(q) < 0:
                q.negate()
            previous[name] = q.copy()
            pb.location = Matrix(basis.tolist()).to_translation()
            pb.rotation_quaternion = q
            pb.scale = (1.0, 1.0, 1.0)
            for path in ("location", "rotation_quaternion", "scale"):
                pb.keyframe_insert(path, frame=f)
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.animation_data.action.name = action_name
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj is rig)
    bpy.ops.export_scene.gltf(
        filepath=out_path, export_format="GLB", use_selection=True, export_apply=False,
        export_yup=True, export_normals=False, export_materials="NONE", export_texcoords=False,
        export_skins=True, export_extras=False, export_animations=True,
        export_animation_mode="ACTIVE_ACTIONS", export_force_sampling=True)


def verify_export(out_path, bones, times, frames):
    """Read the written GLB back and compare every key with what was asked for."""
    s = Scene(out_path)
    anim = s.doc["animations"][0]["name"]
    tracks = s.tracks(anim)
    worst_rot = worst_pos = 0.0
    for t, locals_ in zip(times, frames):
        got = s.local(tracks, t)
        for b in bones:
            g, w = got[s.index[b]], locals_[b]
            worst_rot = max(worst_rot, rot_angle_deg(ortho(g[:3, :3]).T @ ortho(w[:3, :3])))
            worst_pos = max(worst_pos, float(np.linalg.norm(g[:3, 3] - w[:3, 3])))
    meshes = len(s.doc.get("meshes", []))
    return {"max_rotation_error_deg": round(worst_rot, 5), "max_translation_error_m": round(worst_pos, 6),
            "duration_s": round(Scene.duration(tracks), 4), "meshes": meshes}


def main():
    args = parse_args()
    animation_id = args.id if args.id.startswith("anim.") else "anim." + args.id
    with open(args.map, encoding="utf-8") as handle:
        bmap = json.load(handle)
    provenance, provenance_path = load_provenance(args.source, args.provenance)
    workdir = tempfile.mkdtemp(prefix="retarget_")
    try:
        src_path, facts = open_source(args.source, args.member, workdir)
        if facts["archive_sha256"] and provenance.get("download", {}).get("archive_sha256") not in (
                None, facts["archive_sha256"]):
            raise SystemExit("archive SHA-256 differs from provenance.json: not the recorded download")
        source, target = Scene(src_path), Scene(args.target)
        tracks = source.tracks(args.clip)
        fps = args.fps or round(Scene.key_rate(tracks))
        rt = Retarget(source, target, bmap, args.scale)
        times, frames, travel = rt.run(tracks, fps, args.root, args.feet)

        rig_id = os.path.basename(args.target).replace("_rigged.glb", "").replace(".glb", "")
        out_glb = os.path.join(args.assets, "animation", "ready", family_dir(animation_id), animation_id + ".glb")
        out_json = os.path.join(args.assets, "animation", "clips", animation_id + ".json")
        rest_locals = {b: trs(*target.rest[target.index[b]]) for b in rt.bones}

        duration = round(times[-1], 4)
        seg = np.linalg.norm(np.diff(travel, axis=0), axis=1)
        net = travel[-1] - travel[0]
        root_motion = args.root == "keep" and float(np.linalg.norm(net)) >= ROOT_MOTION_MIN_M
        events = []
        for spec in args.event:
            eid, at = spec.split(":")
            events.append({"id": eid, "time": round(float(at), 4)})
        if root_motion:
            events += root_events(travel, times)
        events.sort(key=lambda e: e["time"])

        map_rel = os.path.relpath(os.path.abspath(args.map), HERE).replace("\\", "/")
        record = {
            "schema_version": registry.SCHEMA_VERSION,
            "animation_id": animation_id,
            "skeleton_family": args.skeleton_family,
            "category": args.category,
            "animation_family": animation_id.split(".")[1],
            "clip_type": args.clip_type,
            "loop": bool(args.loop),
            "root_motion": bool(root_motion),
            "duration_s": duration,
            "events": events,
            "hand_profile": {"left": args.hand_left, "right": args.hand_right},
            "tags": ["external", provenance.get("id", "unknown_pack"), rig_id] + args.tag,
            "source": {
                "kind": "external_retarget",
                "pack": provenance.get("pack_name"),
                "pack_id": provenance.get("id"),
                "author": provenance.get("author_provider"),
                "license": provenance.get("license"),
                "license_url": provenance.get("license_url"),
                "url": provenance.get("url"),
                "archive": facts["archive"],
                "archive_sha256": facts["archive_sha256"],
                "file": facts["file"],
                "file_sha256": facts["file_sha256"],
                "clip": args.clip,
                "clip_duration_s": round(Scene.duration(tracks), 4),
                "clip_key_rate_hz": round(Scene.key_rate(tracks), 3),
                "source_skeleton": bmap.get("source_skeleton"),
                "map": map_rel,
                "map_sha256": sha256_file(args.map),
                "tool": "_retarget_clip.py",
                "rig": rig_id,
                "shared": False,
            },
            "applies_to": [rig_id],
            "reference_pose": f"fitted A-pose bind of {rig_id} (20-bone humanoid); source rest is the "
                              f"pack's T-pose, absorbed by the arm/leg rest alignment",
            "retarget": {
                "method": "world-space orientation match per mapped bone; arms and legs rest-aligned "
                          "(T-pose to A-pose); hips anchored at the thigh midpoint; ankles on the "
                          "source ankles' scaled travel (two-bone IK) when feet=ik",
                "fps": fps,
                "scale": round(float(rt.k), 5),
                "scale_mode": args.scale,
                "scale_legs": round(float(rt.scale_legs), 5),
                "scale_hips": round(float(rt.scale_hips), 5),
                "facing_yaw_deg": round(rt.yaw_deg, 3),
                "facing_yaw_measured_deg": round(rt.yaw_measured_deg, 3),
                **rt.ik_facts,
                "root_mode": args.root,
                "root_travel_m": [round(float(x), 4) for x in net],
                "root_path_m": round(float(seg.sum()), 4),
                "root_speed_m_s": round(float(seg.sum()) / max(duration, 1e-6), 4),
                "aligned_bones": rt.aligned,
                "derived_bones": rt.derived,
                "following_bones": rt.follow,
                "drives": rt.drive,
                "folded_source_bones": rt.folded,
            },
            "notes": args.notes or (
                f"{args.clip} from {provenance.get('pack_name')}, retargeted onto {rig_id}. Fingers are not on "
                f"this rig: the hand holds its rest shape."
                + (f" The root rises {net[1]:.3f} m and travels {math.hypot(net[0], net[2]):.3f} m on this body: "
                   f"the source's travel x {rt.k:.4f} (leg ratio); --scale 1 keeps the source's metres."
                   if root_motion else "")),
        }
        problems = registry.validate_clip(record, out_json)
        if problems:
            raise SystemExit("record invalid:\n  " + "\n  ".join(problems))

        try:
            import bpy  # noqa: F401
        except ImportError:
            raise SystemExit("run inside Blender to export (the retarget maths ran: "
                             f"{len(frames)} frames, scale {rt.k:.4f})")
        export_with_blender(args.target, rt.bones, rest_locals, frames, fps, out_glb, animation_id)
        check = verify_export(out_glb, rt.bones, times, frames)
        record["retarget"]["export_check"] = check
        if (check["max_rotation_error_deg"] > EXPORT_ROT_TOLERANCE_DEG
                or check["max_translation_error_m"] > EXPORT_POS_TOLERANCE_M or check["meshes"]
                or abs(check["duration_s"] - duration) > 0.02):
            raise SystemExit(f"export does not match the retarget: {check}")
        os.makedirs(os.path.dirname(out_json), exist_ok=True)
        with open(out_json, "w", encoding="utf-8") as handle:
            json.dump(record, handle, indent=2)
            handle.write("\n")
        print("RETARGET_RESULT " + json.dumps({
            "animation_id": animation_id, "glb": out_glb, "record": out_json, "frames": len(frames),
            "fps": fps, "duration_s": duration, "scale": round(float(rt.k), 4),
            "root_travel_m": record["retarget"]["root_travel_m"], "export_check": check,
            "glb_sha256": sha256_file(out_glb), "provenance": provenance_path}))
        return 0
    finally:
        shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
