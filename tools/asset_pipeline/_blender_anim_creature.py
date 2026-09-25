"""Apply procedural motion to a creature's own rig and export an animation-only GLB.

`_blender_retarget.py` drives the canonical humanoid skeleton. Enemies are not on that skeleton:
each is rigged to its own armature (18 bones for a quadruped, 20 for a humanoid creature, 5 for a
worm), so their clips are applied directly to those bones by name instead.

The mesh is deleted before export, so the result is animation-only and can be loaded alongside the
rigged body without duplicating its geometry - the same contract the player clips follow.

Two motion formats are accepted:

  schema 1  per-bone Euler rotations (degrees) in each bone's own local frame, keyed as given. The
            shared NPC set and the worm still use it. Signs mean different things on rigs with
            different bone rolls, which is why the creature clips moved off it.

  schema 2  intentions, resolved here against this rig's rest geometry and its skinned mesh:
            - the body and the named bones turn about the creature's own axes (pitch + nose down,
              yaw + toward its left, roll + right side down; .R bones mirrored), so +Z forward and
              +Y up mean the same thing on every rig;
            - limbs are two-segment chains. Each chain's bend plane and fold direction come from
              its rest geometry (the knee's offset from the root-to-end line) or, where the motion
              says so ("forward", "back", "up"), from the anatomy, and a fold is always toward that
              side, so knees and elbows cannot bend backward; a chain marked "fk": "hang" (the
              person plan's arms) is posed from hanging straight down, not from its rest, so an
              A-pose bind hangs its arms (Limb.fk_hang); a body "upright" (0-1) takes the bind's
              own trunk lean out, so the body's pitch and roll are measured from vertical;
            - feet are placed by two-bone IK on ground contacts: the lowest vertex of the foot's own
              skin lands exactly on the target height, so a planted foot stands on y = 0;
            - a stance is fitted once per rig from the rest pose: the smallest body drop (and, for
              quadrupeds, nose-down tilt) that lets every planted foot reach the ground, and the
              smallest lift that keeps the body clear of it;
            - after each frame is posed the root is dropped or lifted, measured on the skinned mesh
              the same way the game skins it. A foot the IK holds is corrected on its own target
              (its skin, not its bone, stands on the ground) and never moves the root. "feet" drops
              the root until every planted foot can reach the ground and lifts it out of the floor
              if anything else is below it; "lie" (a death) only keeps things out of the floor:
              the root is lifted for the trunk alone, never for a limb, and a limb, neck or tail
              driven below y = 0 is turned up about its own root instead, so a fall never pops
              back up; "free" only lifts it out of the floor. A planted foot's skin is held where
              it touched down (IK target nudged, capped), so it does not slide;
            - a gait source ("gait": walk/run) is one cycle: the clip gets the whole number of
              cycles whose stance travel at the game's gait speed best fits the rig's leg length
              (and the sprawled legs' reach), so planted feet travel exactly with the ground;
            - chains listed as keep_clear (the spider's fangs) fold up just enough to stay above it;
            - a schema 2 clip's keys are spaced so the last lands on its declared length exactly
              (the exporter's frame rate is stretched to fit, e.g. the 0.55 s hit).

Usage:
    blender --background --factory-startup --python _blender_anim_creature.py -- \
        --rigged <creature_rigged.glb> --motion <motion.json> --out <clip.glb> \
        --fps 30 --loop
"""
import argparse
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from _blender_cleanup import reset_scene  # noqa: E402


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--rigged", required=True)
    parser.add_argument("--motion", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--fps", type=float, default=30.0)
    parser.add_argument("--loop", action="store_true")
    parser.add_argument("--report", default=None,
                        help="schema 2: write the per-frame solve (stance, ground, reach) to this JSON")
    return parser.parse_args(argv)


def import_rigged(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    bpy.ops.import_scene.gltf(filepath=path)
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError(f"no armature in {path}")
    return armatures[0]


def apply_motion(rig, frames, fps):
    """Schema 1: key every named bone per frame, in degrees converted to radians."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")

    missing = set()
    keyed = set()
    for index, frame in enumerate(frames):
        bpy.context.scene.frame_set(index)
        for bone_name, transform in frame.items():
            bone = rig.pose.bones.get(bone_name)
            if bone is None:
                missing.add(bone_name)
                continue
            bone.rotation_mode = "XYZ"
            rotation = transform.get("rotation")
            if rotation:
                bone.rotation_euler = [math.radians(value) for value in rotation]
            location = transform.get("location")
            if location:
                # Bone-local translation; only the root and hips use it, for a vertical bob.
                bone.location = location
            bone.keyframe_insert(data_path="rotation_euler", frame=index)
            if location:
                bone.keyframe_insert(data_path="location", frame=index)
            keyed.add(bone_name)
    bpy.ops.object.mode_set(mode="OBJECT")
    return keyed, missing


# ------------------------------------------------------------------------------------------------
# Schema 2: intentions resolved against the rig
#
# Everything below works in Blender armature space, which the glTF importer sets up as glTF with
# (x, y, z) -> (x, -z, y): the creature's forward (glTF +Z) is -Y here, up (glTF +Y) is +Z, and its
# left (glTF +X) is +X. The armature object of an imported rig is the identity.

FWD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))
LEFT = Vector((1.0, 0.0, 0.0))
IDENTITY = Matrix.Identity(3)
IK_REACH = 0.9995           # a chain never straightens past this (a dead-straight knee has no plane)
GROUND_TOL = 0.0005         # metres: the ground rule stops when it moves the root less than this
CONTACT_HOLD_CAP = 0.06     # leg lengths: most a planted foot's IK target moves to hold its skin still


def rot(axis, degrees):
    if not degrees:
        return IDENTITY.copy()
    return Matrix.Rotation(math.radians(degrees), 3, axis)


def side_of(name):
    return -1.0 if name.endswith(".R") else 1.0


def creature_rotation(spec, side=1.0):
    """pitch + nose down (about the left axis), yaw + toward the left, roll + right side down."""
    if not spec:
        return IDENTITY.copy()
    return (rot(UP, side * spec.get("yaw", 0.0)) @ rot(LEFT, spec.get("pitch", 0.0))
            @ rot(FWD, side * spec.get("roll", 0.0)))


def frame_of(u, n):
    """Orthonormal frame with columns (u, n orthogonalised against u, u x n)."""
    u = u.normalized()
    n = (n - n.dot(u) * u).normalized()
    return Matrix((u, n, u.cross(n))).transposed()


def blend(a, b, w):
    """Rotation matrices a -> b by weight w (0 = a, 1 = b)."""
    if w <= 0.0:
        return a.copy()
    if w >= 1.0:
        return b.copy()
    return a.to_quaternion().slerp(b.to_quaternion(), w).to_matrix()


class Body:
    """The rig's rest skeleton and its skin, as numbers: enough to pose, skin and measure."""

    def __init__(self, rig, mesh):
        bones = list(rig.data.bones)

        def depth(bone):
            return 0 if bone.parent is None else 1 + depth(bone.parent)

        ordered = sorted(bones, key=depth)          # stable: parents before children
        self.order = [b.name for b in ordered]
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.R0 = {b.name: b.matrix_local.to_3x3() for b in bones}
        self.H0 = {b.name: b.head_local.copy() for b in bones}
        self.T0 = {b.name: b.tail_local.copy() for b in bones}
        index = {name: i for i, name in enumerate(self.order)}

        to_arm = rig.matrix_world.inverted() @ mesh.matrix_world
        count = len(mesh.data.vertices)
        co = np.empty(count * 3)
        mesh.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        m = np.array(to_arm)
        self.V = co @ m[:3, :3].T + m[:3, 3]

        names = {g.index: g.name for g in mesh.vertex_groups}
        J = np.zeros((count, 4), dtype=np.int64)
        W = np.zeros((count, 4))
        for v in mesh.data.vertices:
            entries = sorted(((g.weight, index[names[g.group]]) for g in v.groups
                              if g.weight > 0 and names.get(g.group) in index), reverse=True)[:4]
            total = sum(w for w, _ in entries)
            for k, (w, b) in enumerate(entries):
                J[v.index, k] = b
                W[v.index, k] = w / total
        self.J, self.W = J, W
        self.dominant = np.array([self.order[j] for j in J[:, 0]], dtype=object)
        self.dominant_weight = W[:, 0]
        self.height = float(self.V[:, 2].max() - self.V[:, 2].min())

    def rows_of(self, bones, min_weight=0.5):
        want = set(bones)
        return np.array([i for i, (b, w) in enumerate(zip(self.dominant, self.dominant_weight))
                         if b in want and w >= min_weight], dtype=np.int64)

    def skin(self, D, P, rows=None):
        """Linear blend skinning, as glTF and the game do it (inverse bind = inverse rest)."""
        nb = len(self.order)
        A = np.empty((nb, 3, 3))
        t = np.empty((nb, 3))
        for i, name in enumerate(self.order):
            d = D[name]
            A[i] = np.array(d)
            t[i] = np.array(P[name] - d @ self.H0[name])
        V, J, W = (self.V, self.J, self.W) if rows is None else (self.V[rows], self.J[rows], self.W[rows])
        return np.einsum("nk,nkij,nj->ni", W, A[J], V) + np.einsum("nk,nki->ni", W, t[J])


class Limb:
    """A two-segment chain (upper, lower) with an end bone: a leg with its foot, an arm with its
    hand, an arthropod leg with its tarsus. The chain's bend plane and fold direction are measured
    from its rest geometry, or taken from the anatomy when the motion names a direction."""

    def __init__(self, body, name, spec):
        self.name = name
        self.bones = list(spec["bones"])
        upper, lower, end = self.bones
        self.upper, self.lower, self.end = upper, lower, end
        self.parent = body.parent[upper]
        self.side = spec.get("side")
        self.side = (1.0 if self.side == "L" else -1.0) if self.side else side_of(upper)
        self.contact = bool(spec.get("contact", False))
        self.carry = spec.get("end", "lock") == "carry"
        self.reach = spec.get("reach", "lower")

        J0, A0, B0 = body.H0[upper], body.H0[lower], body.H0[end]
        self.J0, self.A0, self.B0 = J0, A0, B0
        self.rows = body.rows_of([end])
        if len(self.rows) == 0:
            self.rows = np.array([i for i, b in enumerate(body.dominant) if b == end], dtype=np.int64)
        self.end_rest = body.V[self.rows] - np.array(B0) if len(self.rows) else np.zeros((1, 3))
        if len(self.rows):
            ends = body.V[self.rows]
            low = ends[:, 2].min()
            near = ends[ends[:, 2] <= low + 0.015]
            c = near.mean(axis=0)
            self.c0 = Vector((c[0], c[1], low))
        else:
            self.c0 = body.T0[end].copy()
        # The sole: the end bone's own skin plus the lower bone's skin near the ground (within a
        # fifth of the chain's length of its lowest rest point: a heel, an ankle or toes weighted to
        # the shin). A foot stands on the floor when the lowest of these does.
        chain_rows = np.nonzero(np.isin(body.dominant, [lower, end]))[0]
        band = max(0.03, 0.2 * ((A0 - J0).length + (B0 - A0).length))
        near = chain_rows[body.V[chain_rows, 2] <= body.V[chain_rows, 2].min() + band] if len(chain_rows) else chain_rows
        self.sole = np.union1d(self.rows, near).astype(np.int64)
        # carry: the end rides rigidly on the lower bone, so the chain's end point is the contact
        self.B = self.c0.copy() if self.carry else B0.copy()
        self.L1 = (A0 - J0).length
        self.L2 = (self.B - A0).length
        self.Ltot = self.L1 + self.L2
        self.h0 = (self.B - J0).normalized()
        offset = (A0 - J0) - (A0 - J0).dot(self.h0) * self.h0
        self.offset = offset
        choice, _, fallback = spec.get("bend", "rest|forward").partition("|")
        self.bend_choice, self.bend_fallback = choice, fallback or choice
        self.rest_offset = offset.length
        self.rest_offset_forward = offset.dot(FWD)
        if choice == "rest" and offset.length >= 0.02 * self.Ltot:
            pole, source = offset.normalized(), "rest"
        else:
            pole, source = self.anatomy_pole(self.bend_fallback), f"anatomy:{self.bend_fallback}"
        self.u0 = (A0 - J0).normalized()
        self.v0 = (self.B - A0).normalized()
        self.set_pole(pole, source)
        swing = self.h0.cross(FWD)
        self.s0 = swing.normalized() if swing.length > 1e-6 else LEFT.copy()
        # raise: a hanging limb swings out to its side (abduction); a sprawled one, which points
        # more sideways, forward or back than down, lifts toward up, whichever way it points
        lift = self.h0.cross(UP)
        self.r0 = lift.normalized() if lift.length > 0.5 else FWD * self.side
        self.rest_extension = (self.B - J0).length / self.Ltot
        # "fk": "hang" (the person plan's arms): the FK angles are measured from the upper bone
        # hanging straight down, whatever the bind pose, so an A-pose bind hangs its arms
        self.fk_from = spec.get("fk", "rest")
        self.q_hang = self.u0.rotation_difference(-UP).to_matrix() if self.fk_from == "hang" else None

    AXES = {"forward": FWD, "back": -FWD, "up": UP, "down": -UP}

    def anatomy_pole(self, name):
        direction = self.AXES[name]
        return (direction - direction.dot(self.h0) * self.h0).normalized()

    def set_pole(self, pole, source):
        self.p0, self.bend_source = pole, source
        # +angle about f0 folds the chain: the middle joint moves toward the pole
        self.f0 = pole.cross(self.h0).normalized()
        # How far the rest pose is already folded at the middle joint (degrees, + toward the fold
        # side). Where the anatomy sets the side, a flex is measured from straight, so a rest pose
        # that is bent the wrong way (a fitted elbow a few degrees past straight) is not replayed.
        self.rest_fold = 0.0
        if source.startswith("anatomy"):
            self.rest_fold = math.degrees(math.atan2(self.u0.cross(self.v0).dot(self.f0), self.u0.dot(self.v0)))

    # --- FK ------------------------------------------------------------------------------------
    def fk(self, spec):
        """swing: + moves the end toward the creature's forward; raise: + moves it outward (a
        hanging limb) or up (a sprawled one, see r0); twist about the upper bone; flex: folds at the
        middle and end joints, + toward the fold side only."""
        flex = list(spec.get("flex", [0.0, 0.0])) + [0.0, 0.0]
        if self.q_hang is not None:
            return self.fk_hang(spec, flex)
        qu = (rot(self.s0, spec.get("swing", 0.0)) @ rot(self.r0, spec.get("raise", 0.0))
              @ rot(self.u0, self.side * spec.get("twist", 0.0)))
        return qu, rot(self.f0, flex[0] - self.rest_fold), rot(self.f0, flex[1])

    def fk_hang(self, spec, flex):
        """FK from hanging straight down beside the body (fk "hang"): the upper bone is first turned
        from its rest to straight down, then twist + turns it in about itself, raise + swings it out
        to its side, swing + forward, and yaw + across toward the other side. The fold is the
        chain's own (an elbow folds the forearm forward from hanging; the end folds the same way,
        tilting a hanging hand's thumb up); bend + folds the end toward its palm (a hanging hand's
        palm faces the thigh: bent back, it faces forward with the arm held out), and pronate + turns
        it about the lower bone, the thumb (which points forward on a hanging hand) turning in."""
        s = self.side
        qu = (rot(UP, -s * spec.get("yaw", 0.0)) @ rot(-LEFT, spec.get("swing", 0.0))
              @ rot(FWD * s, spec.get("raise", 0.0)) @ rot(-UP, s * spec.get("twist", 0.0)) @ self.q_hang)
        qe = (rot(self.v0, s * spec.get("pronate", 0.0)) @ rot(self.f0, flex[1])
              @ rot(FWD, -s * spec.get("bend", 0.0)))
        return qu, rot(self.f0, flex[0] - self.rest_fold), qe

    # --- IK ------------------------------------------------------------------------------------
    def chain(self, Pu, target, pole):
        d = target - Pu
        dist = d.length
        reach = self.Ltot * IK_REACH
        low = abs(self.L1 - self.L2) * 1.001 + 1e-5
        dc = min(max(dist, low), reach)
        h = d / dist if dist > 1e-9 else self.h0.copy()
        p = pole - pole.dot(h) * h
        if p.length < 1e-6:
            p = self.p0 - self.p0.dot(h) * h
        p.normalize()
        ca = max(-1.0, min(1.0, (self.L1 ** 2 + dc ** 2 - self.L2 ** 2) / (2 * self.L1 * dc)))
        sa = math.sqrt(max(0.0, 1.0 - ca * ca))
        K = Pu + self.L1 * (ca * h + sa * p)
        E = Pu + dc * h
        f1 = p.cross(h).normalized()
        Du = frame_of(K - Pu, f1) @ frame_of(self.u0, self.f0).transposed()
        Dl = frame_of(E - K, f1) @ frame_of(self.v0, self.f0).transposed()
        return Du, Dl, dist / self.Ltot

    def pull_in(self, Pu, target, extension):
        """Move a target horizontally toward the point under the chain's root until it is reachable."""
        reach = self.Ltot * extension
        if (target - Pu).length <= reach:
            return target
        under = Vector((Pu.x, Pu.y, target.z))
        lo, hi = 0.0, 1.0
        for _ in range(30):
            mid = 0.5 * (lo + hi)
            if (target.lerp(under, mid) - Pu).length > reach:
                lo = mid
            else:
                hi = mid
        return target.lerp(under, hi)

    def end_low(self, De):
        """Lowest point of the end bone's own skin, relative to its head, for orientation De."""
        if not len(self.rows):
            return 0.0
        return float((self.end_rest @ np.array(De).T)[:, 2].min())


class Solver:
    def __init__(self, body, motion):
        self.body = body
        spec = motion["rig"]
        self.spec = spec
        self.body_bone = spec["body"]
        self.ground_bone = spec.get("ground_bone", "root")
        self.limbs = {name: Limb(body, name, s) for name, s in spec["limbs"].items()}
        self.pair_bends()
        self.by_upper = {l.upper: l for l in self.limbs.values()}
        contacts = [l for l in self.limbs.values() if l.contact]
        self.scale = sum(l.Ltot for l in contacts) / len(contacts) if contacts else body.height * 0.5
        self.keep_clear = [list(c) for c in spec.get("keep_clear", [])]
        clear_bones = {b for c in self.keep_clear for b in c}
        self.clear_rows = {tuple(c): body.rows_of(c, 0.0) for c in self.keep_clear}
        limb_low = {b for l in contacts for b in (l.lower, l.end)}
        self.body_rows = np.array([i for i, b in enumerate(body.dominant)
                                   if b not in limb_low and b not in clear_bones], dtype=np.int64)
        excluded = np.zeros(len(body.V), dtype=bool)
        for rows in self.clear_rows.values():
            excluded[rows] = True
        self.ground_rows = np.nonzero(~excluded)[0]
        # What a lying body rests on: the trunk ("trunk" bones; without it, everything that is not a
        # limb). The limbs and the other chains off the trunk (neck and head, tail) are parts that
        # the floor turns up about their own root instead.
        limb_bones = {b for l in self.limbs.values() for b in l.bones}
        trunk = spec.get("trunk")
        if trunk:
            trunk = set(trunk) | {self.body_bone}
            self.core_rows = np.nonzero(np.isin(body.dominant, list(trunk)) & ~excluded)[0]
        else:
            in_limb = np.isin(body.dominant, list(limb_bones))
            self.core_rows = np.nonzero(~excluded & ~in_limb)[0]
        children = {}
        for name, par in body.parent.items():
            children.setdefault(par, []).append(name)

        def below(name):
            out = [name]
            for c in children.get(name, []):
                if c not in limb_bones and c not in clear_bones:
                    out += below(c)
            return out
        self.parts = {}                 # base bone -> (rows, rest root)
        for l in self.limbs.values():
            self.parts[l.upper] = self._part_rows(l.bones, l.J0)
        if trunk:
            for name, par in body.parent.items():
                if (par in trunk and name not in trunk and name not in limb_bones
                        and name not in clear_bones and name != self.ground_bone):
                    self.parts[name] = self._part_rows(below(name), body.H0[name])
        # the skin next to a part's root joint moves with the trunk and rests on the floor with it
        far = np.zeros(len(body.V), dtype=bool)
        for rows, _ in self.parts.values():
            far[rows] = True
        self.core_rows = np.nonzero(~excluded & ~far)[0] if trunk else self.core_rows
        stance = spec.get("stance", {})
        self.extension = stance.get("extension", 0.985)
        self.clearance = stance.get("clearance", 0.01)
        self.tilt = stance.get("tilt")              # "pitch" (nose down) or "roll" (right hip down)
        self.tilt_axis = {"pitch": LEFT, "roll": FWD}.get(self.tilt)
        self.tilt_range = stance.get("tilt_range", [-4.0, 16.0] if self.tilt == "pitch" else [-8.0, 8.0])
        self.tilt_cost = stance.get("tilt_cost", 0.005)   # metres of drop one degree of tilt is worth
        self.counter = stance.get("counter", [])
        self.neutral = {l.name: Vector((l.c0.x, l.c0.y, 0.0)) for l in contacts}
        self.tilt_deg, self.drop = 0.0, 0.0
        self.goals, self._goal = {}, None
        self.locks = {}
        self.given_way = set()
        self.last_lift = {}
        self.pivot_contacts = (sum((v for v in self.neutral.values()), Vector())
                               / max(len(self.neutral), 1)) if self.neutral else Vector()
        # How far the bind's trunk leans, forward and to the right (deg): the head joint against the
        # body bone. A frame's body "upright" (0-1) takes that much of it out, so its pitch and roll are
        # measured from vertical rather than from a bind that stoops or leans.
        top = next((b for b in ("head", "neck", "chest") if b in body.H0), None)
        trunk = (body.H0[top] - body.H0[self.body_bone]) if top else UP.copy()
        self.rest_lean = (math.degrees(math.atan2(trunk.dot(FWD), trunk.dot(UP))),
                          math.degrees(math.atan2(-trunk.dot(LEFT), trunk.dot(UP))))

    def _part_rows(self, bones, root):
        """A part's vertices away from its root joint (those next to it move with the trunk)."""
        rows = self.body.rows_of(bones, 0.0)
        if not len(rows):
            return rows, root
        dist = np.linalg.norm(self.body.V[rows] - np.array(root), axis=1)
        return rows[dist >= 0.3 * dist.max()], root

    def pair_bends(self):
        """A left and right chain bend the same way. "rest|<anatomy>" reads the side from the pair's
        rest offsets along the anatomy's axis (forward-back or up-down), averaged over the pair: a
        fitted pair whose rest shapes are nearly straight, or crooked sideways on one side, would
        otherwise fold one knee forward and the other back or out. A chain keeps its own rest
        plane only when that plane lies mostly along the axis and agrees with the pair."""
        done = set()
        for name, limb in self.limbs.items():
            if limb.bend_choice != "rest" or name in done:
                continue
            twin = name[:-1] + {"L": "R", "R": "L"}.get(name[-1], name[-1]) if name[-2:] in (".L", ".R") else None
            pair = [limb] + ([self.limbs[twin]] if twin in self.limbs and twin != name else [])
            done.update(l.name for l in pair)
            axis = Limb.AXES[limb.bend_fallback]
            axis = axis if limb.bend_fallback in ("forward", "up") else -axis      # + along forward / up
            along = sum(l.offset.dot(axis) for l in pair) / len(pair)
            if abs(along) < 0.02 * sum(l.Ltot for l in pair) / len(pair):
                for l in pair:
                    l.set_pole(l.anatomy_pole(l.bend_fallback), f"anatomy:{l.bend_fallback}")
                continue
            sign = 1.0 if along > 0 else -1.0
            for l in pair:
                own = l.offset.dot(axis) * sign
                if l.offset.length > 1e-9 and own >= 0.5 * l.offset.length and own >= 0.01 * l.Ltot:
                    l.set_pole(l.offset.normalized(), "rest")
                else:
                    pole = sign * axis
                    l.set_pole((pole - pole.dot(l.h0) * l.h0).normalized(), "rest:pair")

    # --- one pose ------------------------------------------------------------------------------
    def targets(self, frame):
        out = {}
        s = self.scale
        for name, lspec in frame.get("limbs", {}).items():
            limb = self.limbs[name]
            if "foot" not in lspec or name not in self.neutral:
                continue
            side, up, fwd = lspec["foot"]
            out[name] = self.neutral[name] + (side * s * limb.side) * LEFT + (up * s) * UP + (fwd * s) * FWD
        return out

    def pose(self, frame, shift=0.0, targets=None, extension=None, extra=None, lifts=None):
        """extra: {bone: rotation in rest axes} applied before the bone's authored rotation.
        lifts: {limb: rotation in armature axes} turning the solved chain about its root joint."""
        body, s = self.body, self.scale
        targets = self.targets(frame) if targets is None else targets
        Q = {}
        for name, spec in frame.get("bones", {}).items():
            if name in body.H0:
                Q[name] = creature_rotation(spec, side_of(name))
        for name, q in (extra or {}).items():
            Q[name] = q @ Q.get(name, IDENTITY)
        if self.tilt_axis is not None and self.tilt_deg:
            for name in self.counter:
                Q[name] = rot(self.tilt_axis, -self.tilt_deg) @ Q.get(name, IDENTITY)
        bspec = frame.get("body", {})
        R_auth = creature_rotation(bspec)
        if bspec.get("upright"):
            u = bspec["upright"]
            R_auth = R_auth @ rot(FWD, -u * self.rest_lean[1]) @ rot(LEFT, -u * self.rest_lean[0])
        R_tilt = rot(self.tilt_axis, self.tilt_deg) if self.tilt_axis is not None else IDENTITY
        base = (body.H0[self.body_bone] - self.drop * UP + bspec.get("fwd", 0.0) * s * FWD
                + bspec.get("up", 0.0) * s * UP + bspec.get("side", 0.0) * s * LEFT)
        pivot = bspec.get("pivot", "body")
        body_head = self.pivot_contacts + R_auth @ (base - self.pivot_contacts) if pivot == "contacts" else base

        D, P, info = {}, {}, {}
        done = set()
        for name in body.order:
            if name in done:
                continue
            par = body.parent[name]
            Dp = D[par] if par else IDENTITY
            if par:
                Pexp = P[par] + Dp @ (body.H0[name] - body.H0[par])
            else:
                Pexp = body.H0[name].copy()
            if name == self.ground_bone:
                D[name] = Dp @ Q.get(name, IDENTITY)
                P[name] = Pexp + shift * UP
                continue
            if name == self.body_bone:
                D[name] = Dp @ R_auth @ R_tilt
                P[name] = body_head + shift * UP
                continue
            limb = self.by_upper.get(name)
            if limb is None:
                D[name] = Dp @ Q.get(name, IDENTITY)
                if lifts and name in lifts:
                    D[name] = lifts[name] @ D[name]
                P[name] = Pexp
                continue
            lspec = frame.get("limbs", {}).get(limb.name, {})
            Du, Dl, De, ext = self.solve_limb(limb, lspec, Dp, Pexp, targets.get(limb.name), extension)
            self.goals[limb.name] = (Pexp, self._goal)
            if lifts and limb.upper in lifts:
                E = lifts[limb.upper]
                Du, Dl, De = E @ Du, E @ Dl, E @ De
            D[limb.upper], P[limb.upper] = Du, Pexp
            D[limb.lower] = Dl
            P[limb.lower] = Pexp + Du @ (limb.A0 - limb.J0)
            D[limb.end] = De
            P[limb.end] = P[limb.lower] + Dl @ (limb.B0 - limb.A0)
            info[limb.name] = ext
            done.update((limb.upper, limb.lower, limb.end))
        return D, P, info

    def solve_limb(self, limb, lspec, Dp, Pu, target, extension):
        if limb.q_hang is not None and "reach" in lspec:
            return self.reach_limb(limb, lspec, Dp, Pu)
        fk_u, fk_l, fk_e = limb.fk(lspec)
        w = lspec.get("ik", 1.0 if target is not None else 0.0)
        toe = lspec.get("toe", 0.0)
        follow = lspec.get("follow", 1.0 if limb.carry else 0.0)
        Du_fk = Dp @ fk_u
        Dl_fk = Du_fk @ fk_l
        De_fk = Dl_fk @ fk_e
        self._goal = None
        if target is None or w <= 0.0:
            return Du_fk, Dl_fk, De_fk, None
        pole = Dp @ limb.p0
        if limb.reach == "pull_in":
            target = limb.pull_in(Pu, target, extension or self.extension)
        world_end = rot(Dp @ limb.f0, toe)
        ext = None
        if limb.carry:
            # the chain end is the contact point itself; nudge it until the tarsus' lowest vertex
            # sits on the target height
            goal = target.copy()
            for _ in range(3):
                Du, Dl, ext = limb.chain(Pu, goal, pole)
                De = Dl @ rot(limb.f0, toe)
                head = Pu + Du @ (limb.A0 - limb.J0) + Dl @ (limb.B0 - limb.A0)
                low = head.z + limb.end_low(De)
                if abs(low - target.z) < 1e-4:
                    break
                goal.z += target.z - low
            self._goal = goal
        else:
            De = world_end
            for _ in range(2 if follow > 0.0 else 1):
                anchor = De @ (limb.c0 - limb.B0)
                ankle = Vector((target.x - anchor.x, target.y - anchor.y, target.z - limb.end_low(De)))
                Du, Dl, ext = limb.chain(Pu, ankle, pole)
                self._goal = ankle
                De = blend(world_end, Dl @ rot(limb.f0, toe), follow)
        if w >= 1.0:
            return Du, Dl, De, ext
        # IK to FK blend, bone by bone in each bone's parent frame
        qu = blend(fk_u, Dp.inverted() @ Du, w)
        ql = blend(fk_l, Du.inverted() @ Dl, w)
        qe = blend(fk_e, Dl.inverted() @ De, w)
        Du = Dp @ qu
        Dl = Du @ ql
        return Du, Dl, Dl @ qe, ext

    def reach_limb(self, limb, lspec, Dp, Pu):
        """A fk "hang" chain placed by IK: its end (a wrist) at "reach" [out, up, fwd] from its root
        (a shoulder), in chain lengths along the parent's posed axes (out + away from the midline),
        the middle joint (an elbow) toward "pole" [out, up, fwd] (default down and back: a hanging
        arm's elbow); the end bone's wrist fold and pronation as in FK. Where the pole leaves the
        chain as FK would hang it, the hand's thumb points the same way, so a weapon sits alike."""
        s = limb.side
        r = lspec["reach"]
        target = Pu + (Dp @ ((r[0] * s) * LEFT + r[1] * UP + r[2] * FWD)) * limb.Ltot
        pv = lspec.get("pole", [0.0, -1.0, -0.5])
        pole = Dp @ ((pv[0] * s) * LEFT + pv[1] * UP + pv[2] * FWD)
        Du, Dl, ext = limb.chain(Pu, target, pole)
        _, _, qe = limb.fk_hang(lspec, list(lspec.get("flex", [0.0, 0.0])) + [0.0, 0.0])
        self._goal = None
        return Du, Dl, Dl @ qe, ext

    # --- the ground ------------------------------------------------------------------------------
    def planted(self, frame):
        out = []
        for name, lspec in frame.get("limbs", {}).items():
            if name not in self.neutral or "foot" not in lspec or lspec.get("ik", 1.0) < 0.999:
                continue
            if lspec.get("planted", lspec["foot"][1] <= 1e-6):
                out.append(self.limbs[name])
        return out

    def settle(self, frame):
        """Pose, skin, and move the root until the frame's ground rule holds.

        IK already puts each foot's own lowest vertex on its target. What skinning adds (vertices
        near the ankle that also follow the shin) is measured and taken off that foot's target, so
        the skinned foot, not the bone, stands on the ground. The root moves only for the rule:
        down when a planted foot cannot reach the ground from where the body is, up when anything
        else is below it, or onto the ground for a body lying there."""
        mode = frame.get("ground", "feet")
        if mode == "lie":
            return self.settle_lying(frame)
        planted = self.planted(frame)
        targets = self.targets(frame)
        placed = [n for n, l in frame.get("limbs", {}).items()
                  if n in targets and l.get("ik", 1.0) >= 0.999 and len(self.limbs[n].sole)]
        correction = {n: Vector() for n in placed}
        shift = 0.0
        extra = {}
        for _ in range(24):
            aimed = {n: (t + correction[n] if n in correction else t) for n, t in targets.items()}
            D, P, info = self.pose(frame, shift, aimed, extra=extra)
            X = self.body.skin(D, P)
            moved = False
            short = {}
            held = np.zeros(len(X), dtype=bool)       # feet the IK holds on their targets
            for n in placed:
                limb = self.limbs[n]
                miss = float(X[limb.sole, 2].min()) - targets[n].z
                reach = info.get(n)
                folded = (abs(limb.L1 - limb.L2) * 1.001 + 1e-5) / limb.Ltot
                if reach is not None and (reach > IK_REACH or reach < folded):
                    short[n] = miss            # straight or fully folded: the IK cannot hold it
                    if limb in planted:        # the root comes down to it; it cannot hold the root up
                        held[limb.sole] = True
                    continue
                held[limb.sole] = True
                if abs(miss) > GROUND_TOL * 0.5:
                    correction[n].z -= miss
                    moved = True
                if limb in planted and self.hold_contact(limb, X, targets[n], correction[n]):
                    moved = True
            # A held foot is corrected on its own target, so it never moves the root: the root
            # answers only to the rest of the body and to feet the IK cannot hold.
            rest = self.ground_rows[~held[self.ground_rows]]
            rest_low = float(X[rest, 2].min()) if len(rest) else float("inf")
            if mode == "lie":
                # lying: the lowest part of the body on the floor, unless held feet already are
                delta = -min(rest_low, 0.0) if held.any() else -rest_low
            else:
                delta = 0.0
                gaps = [short[l.name] for l in planted if l.name in short]
                # a planted foot out of reach: drop the root until its target is in reach (the
                # vertical gap alone misses a foot that is short because it is far ahead or behind)
                for l in planted:
                    root, goal = self.goals.get(l.name, (None, None))
                    if l.name in short and goal is not None:
                        d = goal - root
                        reach = l.Ltot * (IK_REACH - 0.002)
                        h2 = d.x * d.x + d.y * d.y
                        if h2 < reach * reach:
                            gaps.append(-d.z - math.sqrt(reach * reach - h2))
                if mode == "feet" and gaps and max(gaps) > 0.0:
                    delta = -max(gaps)
                if rest_low + delta < 0.0:
                    delta = -rest_low
            if abs(delta) < GROUND_TOL and not moved:
                break
            shift += delta
        extra, raised = self.clear_chains(frame, shift, aimed, extra)
        D, P, info = self.pose(frame, shift, aimed, extra=extra)
        X = self.body.skin(D, P)
        self.update_locks(planted, X, targets)
        report = {"shift": round(shift, 5), "lowest": round(float(X[:, 2].min()), 5),
                  "planted": {l.name: round(float(X[l.sole, 2].min()), 5) for l in planted if len(l.sole)},
                  "extension": {k: round(v, 4) for k, v in info.items() if v is not None},
                  "raised_deg": raised}
        return D, P, report

    def hold_contact(self, limb, X, target, correction):
        """A planted foot's skin must not slide: the vertex it stands on (locked when it touched
        down, see update_locks) keeps its place relative to the target, which moves exactly with
        the ground. Skin weighted partly to the shin follows the shin's sweep and would drift off
        the bone's track, so the drift is measured on the skin and taken off the IK target."""
        lock = self.locks.get(limb.name)
        if lock is None:
            return False
        v, ox, oy = lock
        dx = float(X[v, 0]) - (target.x + ox)
        dy = float(X[v, 1]) - (target.y + oy)
        if dx * dx + dy * dy <= (GROUND_TOL * 0.5) ** 2:
            return False
        cx, cy = correction.x - dx, correction.y - dy
        # Ordinary shin-weighted skin needs a few centimetres. A foot whose skin barely follows
        # its bone (weights shared with the other foot or the root, an asset defect) would drag
        # the leg out of reach, so the correction is capped and the rest is left to show.
        cap = CONTACT_HOLD_CAP * limb.Ltot
        size = math.hypot(cx, cy)
        if size > cap:
            cx, cy = cx * cap / size, cy * cap / size
        if abs(cx - correction.x) + abs(cy - correction.y) <= GROUND_TOL * 0.5:
            return False
        correction.x, correction.y = cx, cy
        return True

    def update_locks(self, planted, X, targets):
        """Lock each planted foot to the vertex it stands on; a foot rolling off that vertex (a
        heel rising) moves the lock to its new lowest vertex, where it stands. A lifted foot lets
        go."""
        names = {l.name for l in planted if len(l.sole)}
        for n in list(self.locks):
            if n not in names:
                del self.locks[n]
        for l in planted:
            if l.name not in names:
                continue
            rows = l.sole
            k = int(rows[int(np.argmin(X[rows, 2]))])
            lock = self.locks.get(l.name)
            if lock is not None and float(X[lock[0], 2]) <= float(X[k, 2]) + 0.005:
                continue
            t = targets[l.name]
            self.locks[l.name] = (k, float(X[k, 0]) - t.x, float(X[k, 1]) - t.y)

    def settle_lying(self, frame):
        """A body falling to the floor and lying on it (a death). The ground rule only keeps things
        out of the floor; the motion itself brings the body down. The root is lifted only when the
        core (everything that is not a limb) would go below y = 0 and it is never lifted for a
        limb, so a leg or arm driven into the floor cannot push the falling body back up; that
        limb is turned up about its root joint instead, by the least angle that puts it on the
        floor. Feet the IK still holds are corrected on their own targets, as when standing."""
        planted = self.planted(frame)
        targets = self.targets(frame)
        placed = [l.name for l in planted if len(l.sole)]
        correction = {n: Vector() for n in placed}
        shift = 0.0
        for _ in range(24):
            aimed = {n: (t + correction[n] if n in correction else t) for n, t in targets.items()}
            D, P, info = self.pose(frame, shift, aimed)
            X = self.body.skin(D, P)
            moved = False
            for n in placed:
                miss = float(X[self.limbs[n].sole, 2].min()) - targets[n].z
                if abs(miss) > GROUND_TOL * 0.5:
                    correction[n].z -= miss
                    moved = True
                if self.hold_contact(self.limbs[n], X, targets[n], correction[n]):
                    moved = True
            core_low = float(X[self.core_rows, 2].min()) if len(self.core_rows) else 0.0
            new_shift = max(0.0, shift - core_low)
            if abs(new_shift - shift) < GROUND_TOL * 0.5 and not moved:
                break
            shift = new_shift
        # a held leg that folds into the floor anywhere but its sole (a knee or wrist buckling onto
        # it) gives way: it is turned up with the other parts instead of standing on its target
        # (once given way it stays so: a leg that flips between standing and giving way would jump)
        held = set()
        for n in placed:
            limb = self.limbs[n]
            rows = np.setdiff1d(self.parts[limb.upper][0], limb.sole)
            if n in self.given_way or (len(rows) and float(X[rows, 2].min()) < -GROUND_TOL):
                self.given_way.add(n)
            else:
                held.add(n)
        lifts, lifted = self.floor_limbs(frame, shift, aimed, held)
        extra, raised = self.clear_chains(frame, shift, aimed, {}, lifts)
        D, P, info = self.pose(frame, shift, aimed, extra=extra, lifts=lifts)
        X = self.body.skin(D, P)
        self.update_locks([self.limbs[n] for n in held], X, targets)
        report = {"shift": round(shift, 5), "lowest": round(float(X[:, 2].min()), 5),
                  "core_lowest": round(float(X[self.core_rows, 2].min()), 5) if len(self.core_rows) else None,
                  "planted": {l.name: round(float(X[l.sole, 2].min()), 5) for l in planted if len(l.sole)},
                  "extension": {k: round(v, 4) for k, v in info.items() if v is not None},
                  "limb_floor_deg": lifted, "raised_deg": raised}
        return D, P, report

    def floor_limbs(self, frame, shift, targets, held):
        """Turn each part (a limb, the neck and head, the tail) that reaches below the floor up
        about its root joint, by the least angle that brings it back to y = 0, or as far up as that
        turn can bring it. Parts are independent chains; feet the IK holds are left to it."""
        lifts, lifted = {}, {}
        D, P, _ = self.pose(frame, shift, targets)
        held_bones = {self.limbs[n].upper for n in held}
        for base, (rows, _) in self.parts.items():
            if base in held_bones or not len(rows):
                continue
            X = self.body.skin(D, P, rows)
            k = int(np.argmin(X[:, 2]))
            if X[k, 2] >= -GROUND_TOL:
                self.last_lift.pop(base, None)
                continue
            r = Vector(X[k]) - P[base]
            par = self.body.parent[base]
            Dp = D[par] if par else IDENTITY
            # Candidate turns: the one that lifts the lowest point fastest, and the part's own
            # swing (forward/back) and splay (in/out) both ways; last frame's turn is preferred
            # while it is nearly as small, so the part does not flick from one to another.
            axes = [(Dp @ LEFT).normalized(), -(Dp @ LEFT).normalized(),
                    (Dp @ FWD).normalized(), -(Dp @ FWD).normalized()]
            steep = r.cross(UP)
            if steep.length > 1e-6:
                axes.insert(0, steep.normalized())
            last = self.last_lift.get(base)
            if last is not None:
                axes.insert(0, last)

            def low(axis, angle):
                trial = dict(lifts)
                trial[base] = rot(axis, angle)
                Dt, Pt, _ = self.pose(frame, shift, targets, lifts=trial)
                return float(self.body.skin(Dt, Pt, rows)[:, 2].min())

            def least(axis):
                prev, best = 0.0, (low(axis, 0.0), 0.0)
                for step in range(10, 160, 10):
                    value = low(axis, float(step))
                    if value >= -GROUND_TOL:
                        lo, hi = prev, float(step)
                        for _ in range(10):
                            mid = 0.5 * (lo + hi)
                            if low(axis, mid) >= -GROUND_TOL:
                                hi = mid
                            else:
                                lo = mid
                        return hi, True, value
                    if value > best[0]:
                        best = (value, float(step))
                    prev = float(step)
                return best[1], False, best[0]

            kept = least(last) if last is not None else None
            if kept is not None and kept[1]:
                angle, axis = kept[0], last          # keep turning the same way while it works
            else:
                options = [(least(a), a) for a in axes]
                cleared = [(o[0], a) for o, a in options if o[1]]
                if cleared:
                    angle, axis = min(cleared, key=lambda x: x[0])
                else:
                    (angle, _, _), axis = max(options, key=lambda o: o[0][2])
            self.last_lift[base] = axis
            if angle > 0.0:
                lifts[base] = rot(axis, angle)
                lifted[base] = round(angle, 2)
        return lifts, lifted

    def clear_chains(self, frame, shift, targets, extra, lifts=None):
        """Fold each keep_clear chain up about its root, by the least angle that keeps it clear."""
        extra = dict(extra)
        raised = {}
        for chain in self.keep_clear:
            rows = self.clear_rows[tuple(chain)]
            if not len(rows):
                continue
            base = chain[0]
            direction = self.body.T0[chain[-1]] - self.body.H0[base]
            axis = direction.cross(UP)
            axis = axis.normalized() if axis.length > 1e-6 else LEFT.copy()

            def low(angle):
                trial = dict(extra)
                trial[base] = rot(axis, angle)
                D, P, _ = self.pose(frame, shift, targets, extra=trial, lifts=lifts)
                return float(self.body.skin(D, P, rows)[:, 2].min())

            if low(0.0) >= self.clearance:
                continue
            lo, hi = 0.0, 150.0
            for _ in range(16):
                mid = 0.5 * (lo + hi)
                if low(mid) >= self.clearance:
                    hi = mid
                else:
                    lo = mid
            extra[base] = rot(axis, hi)
            raised[base] = round(hi, 2)
        return extra, raised

    # --- the stance ------------------------------------------------------------------------------
    def fit_stance(self):
        """The rig's standing pose: every planted foot on the ground, the body clear of it."""
        contacts = [l for l in self.limbs.values() if l.contact]
        lowerers = [l for l in contacts if l.reach == "lower"]
        neutral_frame = {"limbs": {l.name: {"foot": [0.0, 0.0, 0.0], "planted": True} for l in contacts},
                         "ground": "feet"}
        s = self.scale

        def evaluate(tilt, drop):
            self.tilt_deg, self.drop = tilt, drop
            D, P, info = self.pose(neutral_frame, 0.0, extension=self.extension)
            worst = max((info[l.name] for l in lowerers if info.get(l.name) is not None), default=0.0)
            low = float(self.body.skin(D, P, self.body_rows)[:, 2].min()) if len(self.body_rows) else 1.0
            return worst, low

        tilts = [0.0]
        if self.tilt_axis is not None:
            lo_t, hi_t = self.tilt_range
            tilts = [lo_t + 0.5 * i for i in range(int(round((hi_t - lo_t) / 0.5)) + 1)]
        best = None
        for tilt in tilts:
            lo, hi = -0.5 * s, 0.8 * s
            if evaluate(tilt, hi)[0] > self.extension:
                continue
            if evaluate(tilt, lo)[0] <= self.extension:
                h_reach = lo
            else:
                for _ in range(26):
                    mid = 0.5 * (lo + hi)
                    if evaluate(tilt, mid)[0] <= self.extension:
                        hi = mid
                    else:
                        lo = mid
                h_reach = hi
            lo, hi = -0.8 * s, 0.8 * s
            if evaluate(tilt, lo)[1] < self.clearance:
                continue
            for _ in range(26):
                mid = 0.5 * (lo + hi)
                if evaluate(tilt, mid)[1] >= self.clearance:
                    lo = mid
                else:
                    hi = mid
            h_clear = lo
            if h_reach > h_clear + 1e-4:
                continue
            drop = min(max(0.0, h_reach), h_clear)
            cost = abs(drop) + self.tilt_cost * abs(tilt)
            if best is None or cost < best[0] - 1e-9:
                best = (cost, tilt, drop, h_reach, h_clear)
        if best is None:
            raise RuntimeError("no stance puts every planted foot on the ground with the body clear of it")
        _, self.tilt_deg, self.drop, h_reach, h_clear = best
        # pulled-in feet stay where the stance put them
        D, P, info = self.pose(neutral_frame, 0.0, extension=self.extension)
        for l in contacts:
            if l.reach == "pull_in":
                Pu = P[l.upper]
                self.neutral[l.name] = l.pull_in(Pu, self.neutral[l.name], self.extension)
        self.pivot_contacts = (sum((v for v in self.neutral.values()), Vector())
                               / max(len(self.neutral), 1))
        return {"tilt": self.tilt, "tilt_deg": round(self.tilt_deg, 3), "drop_m": round(self.drop, 4),
                "drop_needed_for_reach_m": round(h_reach, 4), "drop_allowed_by_clearance_m": round(h_clear, 4),
                "extension": self.extension, "clearance_m": self.clearance, "scale_m": round(s, 4),
                "pulled_in": {l.name: round((self.neutral[l.name] - Vector((l.c0.x, l.c0.y, 0))).length, 4)
                              for l in contacts if l.reach == "pull_in"}}

    def gait_chords(self):
        """For each sprawled contact (reach "pull_in") at the fitted stance: the stretch along the
        body's forward axis, through where its tip stands, that the leg reaches at the stance
        extension. A planted tip must stay inside it for the whole stance, or it would be pulled
        in and slide."""
        contacts = [l for l in self.limbs.values() if l.contact and l.reach == "pull_in"]
        frame = {"limbs": {l.name: {"foot": [0.0, 0.0, 0.0], "planted": True}
                           for l in self.limbs.values() if l.contact}, "ground": "feet"}
        D, P, _ = self.pose(frame, 0.0, extension=self.extension)
        out = {}
        for l in contacts:
            Pu, N = P[l.upper], self.neutral[l.name]
            reach = l.Ltot * self.extension
            rho2 = reach * reach - (Pu.z - N.z) ** 2 - (N.x - Pu.x) ** 2
            half = math.sqrt(rho2) if rho2 > 0.0 else 0.0
            out[l.name] = (2.0 * half, Pu.y, half)
        return out

    def center_gait(self, chords, travel):
        """Slide each sprawled contact's neutral along the forward axis just enough that its stance
        (travel metres, centred on the neutral) lies inside its reachable stretch."""
        moved = {}
        for name, (_, centre, half) in chords.items():
            N = self.neutral[name]
            lo, hi = centre - half + 0.5 * travel, centre + half - 0.5 * travel
            y = min(max(N.y, lo), hi) if lo <= hi else centre
            moved[name] = {"shift_m": round(N.y - y, 4), "short_m": round(max(0.0, travel - 2.0 * half), 4)}
            self.neutral[name] = Vector((N.x, y, N.z))
        return moved

    def describe(self):
        return {name: {"bones": l.bones, "bend": l.bend_source, "rest_extension": round(l.rest_extension, 4),
                       "rest_fold_deg": round(l.rest_fold, 2),
                       "rest_knee_offset_m": round(l.rest_offset, 4),
                       "rest_knee_offset_forward_m": round(l.rest_offset_forward, 4),
                       "lengths_m": [round(l.L1, 4), round(l.L2, 4)]}
                for name, l in self.limbs.items()}


def interpolate(a, b, u):
    """A frame between two frames: numbers and lists of numbers blend, anything else is taken
    from the nearer one."""
    if isinstance(a, dict) and isinstance(b, dict):
        return {k: (interpolate(a[k], b[k], u) if k in a and k in b else (a.get(k) if k in a else b.get(k)))
                for k in list(a) + [k for k in b if k not in a]}
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        return [interpolate(x, y, u) for x, y in zip(a, b)]
    if isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool)             and not isinstance(b, bool):
        return a + (b - a) * u
    return a if u < 0.5 else b


def expand_gait(motion, scale, fps, longest=None, keys_per_cycle=32):
    """A gait source is one cycle sampled by phase, with the feet's forward travel in stance
    travels. The clip must carry the game's speed for its gait (speed_m_s at 1x): a planted foot
    travels speed x duty x period under the body. Choose the whole number of cycles in the clip
    whose stance travel is closest to sweep x this rig's leg length and that every sprawled leg
    can reach (`longest`, see Solver.gait_chords), then key the clip: frames resampled at each
    key's phase, the feet's forward travel turned into leg lengths."""
    gait = motion["gait"]
    duration = motion["duration_s"]
    travel = gait["speed_m_s"] * duration * gait["duty"]          # stance travel for one cycle
    want = gait["sweep"] * scale
    options = [n for n in range(1, 25) if longest is None or travel / n <= longest] or [24]
    cycles = min(options, key=lambda n: abs(math.log(travel / n / want)))
    stance = travel / cycles                                      # metres per stance
    k = stance / scale                                            # stance travels -> leg lengths
    source = motion["frames"]
    phases = [f["phase"] for f in source]
    count = max(int(round(duration * fps * 2)), keys_per_cycle * cycles) + 1
    frames = []
    for i in range(count):
        t = duration * i / (count - 1)
        phase = (cycles * t / duration) % 1.0
        j = min(max(0, int(np.searchsorted(phases, phase, side="right")) - 1), len(source) - 2)
        u = (phase - phases[j]) / max(phases[j + 1] - phases[j], 1e-12)
        frame = interpolate(source[j], source[j + 1], min(max(u, 0.0), 1.0))
        for limb in frame.get("limbs", {}).values():
            if "foot" in limb:
                limb["foot"] = [limb["foot"][0], limb["foot"][1], limb["foot"][2] * k]
        frame["t"] = round(t, 6)
        frame["phase"] = round(phase, 6)
        frames.append(frame)
    expanded = dict(motion)
    expanded["frames"] = frames
    info = {"speed_m_s": gait["speed_m_s"], "duty": gait["duty"], "sweep_wanted": gait["sweep"],
            "leg_m": round(scale, 4), "cycles": cycles, "period_s": round(duration / cycles, 4),
            "stride_m": round(gait["speed_m_s"] * duration / cycles, 4), "stance_travel_m": round(stance, 4),
            "sweep": round(k, 3), "keys": count}
    return expanded, info


def basis_of(body, D, P):
    """Pose-bone basis (location, quaternion) per bone from armature-space rotations and heads."""
    out = {}
    for name in body.order:
        par = body.parent[name]
        R0 = body.R0[name]
        if par:
            Dp = D[par]
            Pexp = P[par] + Dp @ (body.H0[name] - body.H0[par])
        else:
            Dp = IDENTITY
            Pexp = body.H0[name]
        local = R0.inverted() @ Dp.inverted() @ D[name] @ R0
        loc = R0.inverted() @ Dp.inverted() @ (P[name] - Pexp)
        out[name] = (loc, local.to_quaternion())
    return out


def apply_intentions(rig, body, motion, solver=None, stance=None):
    if solver is None:
        solver = Solver(body, motion)
        stance = solver.fit_stance()
    frames = motion["frames"]
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    previous = {}
    per_frame = []
    solver.locks, solver.given_way, solver.last_lift = {}, set(), {}
    if motion.get("loop"):
        # a loop's first frame follows its last: solve it through once so the feet's contact locks
        # are what they will be when the clip comes round, then key it
        for frame in frames:
            solver.settle(dict(frame))
    for index, frame in enumerate(frames):
        frame = dict(frame)
        D, P, report = solver.settle(frame)
        report["t"] = frame.get("t")
        report["ground"] = frame.get("ground", "feet")
        per_frame.append(report)
        for name, (loc, quat) in basis_of(body, D, P).items():
            pb = rig.pose.bones[name]
            if name in previous and previous[name].dot(quat) < 0.0:
                quat = -quat
            previous[name] = quat
            pb.rotation_quaternion = quat
            pb.location = loc
            pb.keyframe_insert(data_path="rotation_quaternion", frame=index)
            pb.keyframe_insert(data_path="location", frame=index)
    bpy.ops.object.mode_set(mode="OBJECT")
    return set(body.order), set(), {"stance": stance, "limbs": solver.describe(), "frames": per_frame}


def main():
    args = parse_args()
    with open(args.motion, encoding="utf-8") as handle:
        motion = json.load(handle)
    frames = motion["frames"]
    schema = str(motion.get("schema_version", "1"))

    reset_scene()
    rig = import_rigged(args.rigged)
    body = None
    gait_info = None
    if schema == "2":
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH" and o.vertex_groups]
        if not meshes:
            raise RuntimeError(f"schema 2 motion needs the skinned mesh in {args.rigged}")
        body = Body(rig, meshes[0])
        solver = Solver(body, motion)
        stance = solver.fit_stance()
        if motion.get("gait"):
            chords = solver.gait_chords()
            longest = min((c[0] for c in chords.values()), default=None)
            motion, gait_info = expand_gait(motion, solver.scale, args.fps, longest)
            gait_info["neutral_moves"] = solver.center_gait(chords, gait_info["stance_travel_m"])
            frames = motion["frames"]

    # Remove the mesh so the export is animation-only and does not duplicate the creature's
    # geometry in every clip.
    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    scene = bpy.context.scene
    # The glTF exporter converts frame numbers to seconds using the SCENE rate. Leaving it at
    # Blender's 24 fps default exported the player clips 25% too long; the same trap applies here.
    scene.render.fps = int(round(args.fps))
    scene.render.fps_base = 1.0
    duration = motion.get("duration_s")
    if schema == "2" and duration and len(frames) > 1:
        # The exporter's seconds are frame / (fps * fps_base). A length that is not a whole number
        # of 1/30 s frames (the 0.55 s hit) is kept exact by stretching the frame rate slightly.
        scene.render.fps_base = (len(frames) - 1) / (duration * scene.render.fps)
    scene.frame_start = 0
    scene.frame_end = max(len(frames) - 1, 0)

    solved = None
    if schema == "2":
        keyed, missing, solved = apply_intentions(rig, body, motion, solver, stance)
        if gait_info:
            solved["gait"] = gait_info
    else:
        keyed, missing = apply_motion(rig, frames, args.fps)

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    for obj in bpy.context.scene.objects:
        obj.select_set(obj is rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(
        filepath=args.out, export_format="GLB", use_selection=True,
        export_apply=False, export_yup=True, export_normals=False,
        export_materials="NONE", export_texcoords=False,
        export_skins=True, export_extras=False)

    rate = scene.render.fps * scene.render.fps_base
    result = {
        "rigged": os.path.basename(args.rigged),
        "kind": motion["kind"],
        "plan": motion["plan"],
        "out": args.out,
        "frames": len(frames),
        "bones_keyed": sorted(keyed),
        "bones_missing": sorted(missing),
        "duration_s": round((len(frames) - 1) / rate, 4),
        "fps": args.fps,
    }
    if solved is not None:
        result["schema"] = 2
        result["stance"] = solved["stance"]
        result["limbs"] = solved["limbs"]
        if gait_info:
            result["gait"] = gait_info
        if args.report:
            os.makedirs(os.path.dirname(os.path.abspath(args.report)), exist_ok=True)
            with open(args.report, "w", encoding="utf-8") as handle:
                json.dump(solved, handle, indent=1)
            result["solve_report"] = args.report
    print("CREATURE_ANIM_RESULT " + json.dumps(result))
    return 0


if __name__ == "__main__":
    sys.exit(main())
