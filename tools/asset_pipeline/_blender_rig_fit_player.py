"""Fit the canonical 52-bone skeleton to a player body AS IT STANDS, weight it, then re-pose it to the contract.

Why this exists. `_blender_bind_canonical.py` drops the canonical skeleton at its fixed landmarks and copies weights
from the canonical tube body. On `race2_veth_bindpose` the mesh's arms hang ~60 degrees below horizontal against the
contract's 45-degree A-pose, so the hand bones end 0.16 m outside the hands and the fingers 0.2 m out; arm clips bend
the arms about points off the body. This tool does the opposite of forcing the mesh onto a template:

  1. measure the mesh (geodesic level sets along each limb, perpendicular cross-sections, fingertip extrema, exact
     horizontal contours of the trunk and legs) and place every joint of the 52-bone contract inside it (definitions
     on each `fit_*` function). The generated bodies are double-walled shells (an outer skin and an inner wall ~5 mm
     under it), so the walls are told apart first and everything is measured on the outer skin;
  2. weight it with Blender's bone-heat weights, computed on a welded copy of the outer skin (UV-seam duplicates
     merged); cap the two bones heat over-extends - the upper arm to the arm and a band over the shoulder cap, the
     thigh to below the femoral heads (`refine_weights`) - and smooth where the caps acted; then carry the weights to
     the original vertices: the skin by vertex identity, the inner wall through the wall (so it cannot poke through),
     separate shells (boots) from the nearest skin vertex; limb weights are faded out across the midline, where heat
     reaches between touching thighs;
  3. rotate each arm chain (upper arm, forearm, hand; the fingers ride the hand) until every arm bone points exactly
     along the contracted 45-degree A-pose, deform the mesh by that pose (linear blend skinning, the maths the engine
     uses) and take the deformed mesh and the posed skeleton as the new rest. Vertices without arm weight - the
     torso, legs and head - are bit-identical to LOD0;
  4. fade the baked tangent-space normal map toward flat where the re-pose moved skin against its bake-time
     neighbourhood or stretched it (`repose_attenuation`, `fade_normal_map`). No tangents are written: the
     engine's glTF loader generates them (MikkTSpace) on the re-posed rest mesh.

The result keeps all 52 bone names, their parents and order, and the five SOCK_* attachments (SOCK_hand_* re-seated
in the palms with a grip frame), the 1.80 m height and the ground contact. Every joint keeps the contract skeleton's
rest frame (orientation); only the joint positions are this body's. The humanoid clips carry the contract skeleton's
bone offsets in every key, so on a body of other proportions they need the engine's rest-relative retarget (the
working-tree `ArtLibrary.Retarget` with a source rest); `render` emulates both that and the committed absolute one.

A body that is not facing the contract's forward is refused (a rigid yaw would change the asset: the owner's call).

Everything is written under assets/_staging/rigs/<id>/; nothing in ready/, rigged/ or animation/ is touched.

Usage (Blender 5.2, factory startup):
  blender --background --factory-startup --python _blender_rig_fit_player.py -- fit --asset-id race2_veth_bindpose
  blender --background --factory-startup --python _blender_rig_fit_player.py -- render --asset-id race2_veth_bindpose
  (the refined-weight rig is staged with --out-dir assets/_staging/rigs/race2_veth_bindpose/v2 on both commands,
   the normal-map fade with --out-dir .../v3)
"""
import argparse
import heapq
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.append(TOOL_DIR)

from _blender_canonical_body import (  # noqa: E402
    ATTACHMENTS, BONES, FIT_FAMILIES, SKELETON_VERSION, bone_positions, skeleton_bones,
)

REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.path.join(REPO, "assets")
STAGING = os.path.join(ASSETS, "_staging", "rigs")
FINGER_ORDER = ["index", "middle", "ring", "pinky"]
SIDES = (("l", 1.0), ("r", -1.0))


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("fit", "render"))
    parser.add_argument("--asset-id", required=True)
    parser.add_argument("--fit-family", default="standard_humanoid", choices=sorted(FIT_FAMILIES))
    parser.add_argument("--input", default=None, help="Mesh GLB (default: assets/ready/<id>/<id>.glb)")
    parser.add_argument("--out-dir", default=None, help="Default: assets/_staging/rigs/<id>")
    parser.add_argument("--clips", nargs="*", default=None, help="render: clip ids (default: the verification set)")
    parser.add_argument("--retarget", nargs="*", default=["relative", "absolute"],
                        choices=("relative", "absolute"), help="render: engine retarget modes to emulate")
    parser.add_argument("--size", type=int, default=1024, help="render: pixels")
    return parser.parse_args(argv)


# --------------------------------------------------------------------------------------------------------------------
# Surface analysis (numpy only: Blender ships no scipy)
# --------------------------------------------------------------------------------------------------------------------

def components(n, edges):
    """Connected-component label per vertex, by min-label propagation with pointer jumping."""
    label = np.arange(n)
    while True:
        low = np.minimum(label[edges[:, 0]], label[edges[:, 1]])
        new = label.copy()
        np.minimum.at(new, edges[:, 0], low)
        np.minimum.at(new, edges[:, 1], low)
        new = new[new]
        if np.array_equal(new, label):
            break
        label = new
    return np.unique(label, return_inverse=True)[1].ravel()


class Surface:
    """The mesh welded at its UV seams, with edge adjacency for geodesic distances.

    A generated mesh is split along every UV seam, so an unwelded graph stops at each seam and a geodesic front never
    crosses it. Welding at 1e-5 m merges only exact duplicates, so it changes no shape.
    """

    def __init__(self, positions, triangles):
        key = np.round(positions / 1e-5).astype(np.int64)
        _unique, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
        self.to_welded = inverse.ravel()
        self.V = positions[first]
        tri = self.to_welded[triangles]
        keep = (tri[:, 0] != tri[:, 1]) & (tri[:, 1] != tri[:, 2]) & (tri[:, 0] != tri[:, 2])
        self.F = tri[keep]
        edges = np.unique(np.sort(np.concatenate(
            [self.F[:, [0, 1]], self.F[:, [1, 2]], self.F[:, [2, 0]]]), axis=1), axis=0)
        self.edges = edges
        n = len(self.V)
        both = np.concatenate([edges, edges[:, ::-1]])
        both = both[np.argsort(both[:, 0], kind="stable")]
        self.ptr = np.searchsorted(both[:, 0], np.arange(n + 1)).tolist()
        self.nbr = both[:, 1].tolist()
        self.len = np.linalg.norm(self.V[both[:, 0]] - self.V[both[:, 1]], axis=1).tolist()
        self.comp = components(n, edges)
        sizes = np.bincount(self.comp)
        self.main = self.comp == int(np.argmax(sizes))
        self.height = float(self.V[:, 2].max())
        self.ground = float(self.V[:, 2].min())

    def geodesic(self, sources, allowed=None):
        n = len(self.V)
        dist = [math.inf] * n
        allow = None if allowed is None else allowed.tolist()
        heap = []
        for s in np.atleast_1d(sources):
            dist[int(s)] = 0.0
            heap.append((0.0, int(s)))
        heapq.heapify(heap)
        ptr, nbr, length = self.ptr, self.nbr, self.len
        while heap:
            du, u = heapq.heappop(heap)
            if du > dist[u]:
                continue
            for k in range(ptr[u], ptr[u + 1]):
                v = nbr[k]
                if allow is not None and not allow[v]:
                    continue
                nd = du + length[k]
                if nd < dist[v]:
                    dist[v] = nd
                    heapq.heappush(heap, (nd, v))
        return np.array(dist)

    def neighbours(self, u):
        return self.nbr[self.ptr[u]:self.ptr[u + 1]]

    def nearest_distance(self, points):
        points = np.atleast_2d(points)
        return np.array([float(np.sqrt(((self.V - p) ** 2).sum(1)).min()) for p in points])


def unit(v):
    v = np.asarray(v, dtype=float)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def rotation_between(a, b):
    """Smallest rotation (3x3) taking direction a onto direction b."""
    a, b = unit(a), unit(b)
    axis = np.cross(a, b)
    s, c = np.linalg.norm(axis), float(np.dot(a, b))
    if s < 1e-12:
        if c > 0:
            return np.eye(3)
        other = unit(np.cross(a, [1.0, 0.0, 0.0] if abs(a[0]) < 0.9 else [0.0, 1.0, 0.0]))
        return axis_angle(other, math.pi)
    return axis_angle(axis / s, math.atan2(s, c))


def axis_angle(axis, angle):
    x, y, z = unit(axis)
    c, s, t = math.cos(angle), math.sin(angle), 1 - math.cos(angle)
    return np.array([[t * x * x + c, t * x * y - s * z, t * x * z + s * y],
                     [t * x * y + s * z, t * y * y + c, t * y * z - s * x],
                     [t * x * z - s * y, t * y * z + s * x, t * z * z + c]])


def angle_deg(a, b):
    return math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(unit(a), unit(b)))))))


def perpendicular_basis(axis):
    helper = np.array([0.0, 1.0, 0.0]) if abs(axis[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
    e1 = unit(np.cross(axis, helper))
    return e1, unit(np.cross(axis, e1))


def ring_profile(V, dist, step=0.01, limit=1.5):
    """Centroid and RMS radius of each geodesic level set: a limb's rings until it meets the body."""
    rows = []
    finite = np.isfinite(dist)
    s = 0.0
    top = min(limit, float(dist[finite].max()) if finite.any() else 0.0)
    while s < top:
        m = (dist >= s) & (dist < s + step)
        if m.sum() >= 4:
            p = V[m]
            c = p.mean(0)
            rows.append({"s": round(s, 4), "c": c, "r": float(np.sqrt(((p - c) ** 2).sum(1).mean())),
                         "n": int(m.sum())})
        s += step
    return rows


def tube_end(rows, start, growth=1.3, window=0.15):
    """First level set past `start` whose radius outgrows the recent tube by `growth`: where a limb meets the trunk."""
    for i, row in enumerate(rows):
        if row["s"] < start:
            continue
        recent = [q["r"] for q in rows[:i] if row["s"] - window <= q["s"] < row["s"]]
        if len(recent) >= 5 and row["r"] > growth * float(np.median(recent)):
            return row["s"]
    return rows[-1]["s"]


def section_profile(P, axis, origin, step=0.01, half=0.006):
    """Cross-sections of a limb perpendicular to `axis`: bounding-box centre (unbiased by uneven vertex density)
    and the two perpendicular extents."""
    e1, e2 = perpendicular_basis(axis)
    t = (P - origin) @ axis
    rows = []
    for tt in np.arange(t.min(), t.max() + 1e-9, step):
        m = np.abs(t - tt) < half
        if m.sum() < 4:
            continue
        q = P[m] - origin
        a1, a2 = q @ e1, q @ e2
        centre = origin + axis * tt + e1 * (a1.min() + a1.max()) / 2 + e2 * (a2.min() + a2.max()) / 2
        rows.append({"t": float(tt), "centre": centre, "w1": float(np.ptp(a1)), "w2": float(np.ptp(a2)),
                     "area": float(np.ptp(a1) * np.ptp(a2)), "n": int(m.sum())})
    return rows


def smooth(values, k=3):
    values = np.asarray(values, dtype=float)
    pad = k // 2
    padded = np.pad(values, pad, mode="edge")
    return np.convolve(padded, np.ones(k) / k, mode="valid")


def line_fit(points):
    points = np.asarray(points)
    c = points.mean(0)
    _u, _s, vt = np.linalg.svd(points - c)
    return c, vt[0]


# --------------------------------------------------------------------------------------------------------------------
# Wall structure. The generated bodies are thin-walled shells: an outer skin and an inner wall ~5 mm inside it, joined
# into one closed surface. From a point inside a limb a ray crosses the inner wall and then the outer skin, so "inside
# the body" is 2 crossings modulo 4, not an odd count; and bone-heat visibility from the outer skin is blocked by the
# inner wall. So the walls are told apart first, and every measurement and the heat solve use the outer skin only.
# --------------------------------------------------------------------------------------------------------------------

def bvh(V, F):
    from mathutils.bvhtree import BVHTree
    return BVHTree.FromPolygons([tuple(map(float, v)) for v in V], [tuple(map(int, f)) for f in F],
                                all_triangles=True)


def self_intersections(tree, faces):
    """Faces of `tree` (built from `faces`) that pass through a face they share no vertex with: a fold through the
    skin, or two parts pressed into each other."""
    pairs = [(a, b) for a, b in tree.overlap(tree) if not set(faces[a]) & set(faces[b])]
    return np.unique(np.array(pairs, dtype=np.int64).ravel()) if pairs else np.zeros(0, dtype=np.int64)


def crossings(tree, origin, direction, limit=64):
    count = 0
    o = Vector(tuple(map(float, origin)))
    d = Vector(tuple(map(float, direction))).normalized()
    for _ in range(limit):
        hit, _normal, _index, _dist = tree.ray_cast(o, d)
        if hit is None:
            break
        count += 1
        o = hit + d * 1e-5
    return count


def classify_walls(surf, normals):
    """True for inner-wall vertices: a ray along the vertex normal crosses the surface 2 (mod 4) times, i.e. it
    starts facing into a cavity. Voted over the normal and two jittered directions."""
    tree = bvh(surf.V, surf.F)
    rng = np.random.default_rng(7)
    inner = np.zeros(len(surf.V), dtype=bool)
    for i, (p, n) in enumerate(zip(surf.V, normals)):
        votes = 0
        for k in range(3):
            d = n if k == 0 else unit(n + rng.normal(0.0, 0.15, 3))
            votes += crossings(tree, p + n * 2e-4, d) % 4 == 2
        inner[i] = votes >= 2
    # A ray that grazes another limb's wall miscounts; the walls are large regions, so a majority filter over each
    # vertex's neighbourhood removes the isolated misreads.
    for _ in range(2):
        counts = np.zeros(len(inner))
        sizes = np.zeros(len(inner))
        e = surf.edges
        np.add.at(counts, e[:, 0], inner[e[:, 1]]); np.add.at(counts, e[:, 1], inner[e[:, 0]])
        np.add.at(sizes, e[:, 0], 1); np.add.at(sizes, e[:, 1], 1)
        inner = (counts + inner) / (sizes + 1) > 0.5
    return inner, tree


RAY_DIRECTIONS = ((0.577, 0.577, 0.577), (-0.6, 0.1, 0.79), (0.2, -0.95, 0.24), (0.9, -0.3, -0.3),
                  (-0.4, -0.5, -0.77), (-0.8, 0.55, -0.2), (0.1, 0.7, -0.7))


def inside_counts(tree, point):
    """Crossings from a point along seven spread directions."""
    return [crossings(tree, point, d) for d in RAY_DIRECTIONS]


def is_inside(counts):
    """Inside a double-walled body: every limb a ray passes whole adds 4 crossings, so a count that is not a multiple
    of 4 starts inside the skin (2: in the cavity; 1 or 3: in the wall of a thin digit). Majority of the rays (a ray
    that grazes where two fingers touch can miscount)."""
    return sum(c % 4 != 0 for c in counts) * 2 > len(counts)


# --------------------------------------------------------------------------------------------------------------------
# Landmark fit. Every joint of the 52-bone contract is placed by the contract's own definition, applied to landmarks
# measured on this mesh instead of the fit family's declared constants (see `_blender_canonical_body.bone_positions`).
# --------------------------------------------------------------------------------------------------------------------

def fit_arm(surf, sign):
    """Wrist, elbow and shoulder of one arm, measured along the arm as it hangs.

    The arm is found by geodesic distance from its lateral extremity (a fingertip): level sets of that distance are
    rings around the limb, and where a ring's radius outgrows the tube it has reached the trunk (the armpit). Cross
    sections perpendicular to the arm's axis then give the centreline and the thickness profile:
      wrist    - the thinnest section between the palm and mid-forearm;
      elbow    - the bend of the centreline (best two-segment fit wrist-elbow-shoulder); where the arm is straighter
                 than 8 degrees there is no bend to find and the contract's upper/forearm ratio places it instead;
      shoulder - on the upper arm's axis, extended up past the armpit, the point with the largest inscribed ball:
                 the centre of the shoulder's mass on the humerus line.
    """
    V, H = surf.V, surf.height
    allowed = surf.main & surf.outer
    idx = np.where(allowed & (V[:, 2] > 0.3 * H) & (V[:, 2] < 0.75 * H) & (sign * V[:, 0] > 0))[0]
    seed = int(idx[np.argmax(sign * V[idx, 0])])
    dist = surf.geodesic(seed, allowed=allowed)
    rows = ring_profile(V, dist)
    s_top = tube_end(rows, start=0.25)
    arm = allowed & (dist < s_top)
    tube = [r["c"] for r in rows if 0.3 * s_top <= r["s"] <= 0.95 * s_top]
    c0, axis = line_fit(tube)
    if np.dot(tube[-1] - tube[0], axis) < 0:
        axis = -axis
    prof = section_profile(V[arm], axis, c0)
    t = np.array([r["t"] for r in prof])
    area = smooth([r["area"] for r in prof])
    t_lo, t_hi = float(t.min()), float(t.max())
    window = (t >= t_lo + 0.12) & (t <= t_lo + 0.45 * (t_hi - t_lo))
    i_wrist = int(np.where(window)[0][np.argmin(area[window])])
    wrist = prof[i_wrist]["centre"]
    t_wrist = float(t[i_wrist])

    # The section keeps its tube size until it starts to swallow the trunk at the armpit.
    upper_rows = [r for r in prof if r["t"] > t_wrist + 0.1]
    base_area = float(np.median([r["area"] for r in upper_rows[: max(3, len(upper_rows) // 2)]]))
    t_armpit = next((r["t"] for r in upper_rows if r["area"] > 1.35 * base_area and r["t"] > t_wrist + 0.2),
                    upper_rows[-1]["t"])
    centre_line = [r for r in prof if t_wrist <= r["t"] <= t_armpit - 0.02]
    points = np.array([r["centre"] for r in centre_line])

    # Upper arm axis from the upper half of the clean centreline, extended past the armpit.
    half = len(points) // 2
    ua_c, ua_u = line_fit(points[half:])
    if np.dot(ua_u, axis) < 0:
        ua_u = -ua_u
    skin = V[allowed]
    start = points[-1]
    best = (-1.0, start)
    for lam in np.arange(0.0, 0.16, 0.0025):
        p = ua_c + ua_u * (np.dot(start - ua_c, ua_u) + lam)
        depth = float(np.sqrt(((skin - p) ** 2).sum(1)).min())
        if depth > best[0]:
            best = (depth, p)
    shoulder_depth, shoulder = best

    # Elbow: best two-segment fit, or the contract's proportion on a straight arm.
    chain = np.vstack([points, shoulder])
    arc = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(chain, axis=0), axis=1))])
    total = float(arc[-1])
    cand = []
    for i in range(len(points)):
        if not 0.3 <= arc[i] / total <= 0.7:
            continue
        e = points[i]
        res = 0.0
        for j, p in enumerate(points):
            a, b = (wrist, e) if j <= i else (e, shoulder)
            ab = b - a
            tt = np.clip(np.dot(p - a, ab) / max(float(np.dot(ab, ab)), 1e-12), 0, 1)
            res += float(np.sum((a + ab * tt - p) ** 2))
        cand.append((res, i))
    _res, i_elbow = min(cand)
    elbow = points[i_elbow]
    bend = angle_deg(elbow - shoulder, wrist - elbow)
    elbow_rule = "bend of the centreline"
    if bend < 8.0:
        f = FIT_FAMILIES["standard_humanoid"]
        from_wrist = total * f["lowerarm"] / (f["upperarm"] + f["lowerarm"])
        k = min(max(int(np.searchsorted(arc, from_wrist)), 1), len(points) - 1)
        elbow = points[k]
        elbow_rule = "contract upper/forearm ratio (arm straighter than 8 deg)"

    return {
        "seed": V[seed], "dist": dist, "arm_mask": arm, "axis": axis, "origin": c0, "profile": prof,
        "t_wrist": t_wrist, "t_armpit": t_armpit, "wrist": wrist, "elbow": elbow, "shoulder": shoulder,
        "shoulder_depth": shoulder_depth, "elbow_bend_deg": bend, "elbow_rule": elbow_rule,
        "wrist_section_m": [prof[i_wrist]["w1"], prof[i_wrist]["w2"]],
    }


def local_maxima(surf, values, candidates, rings=2):
    """Vertices whose value is not exceeded anywhere in their `rings`-ring neighbourhood."""
    out = []
    cand = set(int(i) for i in candidates)
    for u in cand:
        ring = {u}
        frontier = {u}
        for _ in range(rings):
            frontier = {w for x in frontier for w in surf.neighbours(x)} - ring
            ring |= frontier
        ring.discard(u)
        if all(values[u] >= values[w] for w in ring if np.isfinite(values[w])):
            out.append(u)
    return out


def fit_hand(surf, arm, sign):
    """The five digits of one hand, from geodesic extrema, and the palm.

    Geodesic distance from the wrist section, over the hand only, peaks at each fingertip. The shortest peak is the
    thumb (its tip only reaches the index finger's first joint); the other four are ordered across the hand from the
    thumb's side (index, middle, ring, pinky). Each digit is then walked back from its tip by geodesic level sets
    until its ring outgrows the digit (it has joined the palm or its neighbour): that ring is the digit's base.
    Per the contract's definitions: `<digit>_01` sits at the knuckle - the base moved one digit-diameter toward the
    wrist along the digit, which is where the knuckle lies under the web; `<digit>_02` sits halfway from the knuckle
    to the tip along the digit's centreline; the chain ends inside the tip.
    """
    V = surf.V
    allowed = surf.main & surf.outer
    t = (V - arm["origin"]) @ arm["axis"]
    hand = arm["arm_mask"] & (t < arm["t_wrist"] + 0.003)
    ring = np.where(arm["arm_mask"] & (np.abs(t - arm["t_wrist"]) < 0.006))[0]
    dw = surf.geodesic(ring, allowed=hand)
    reach = np.isfinite(dw)
    peak = float(dw[reach].max())
    tips = [u for u in local_maxima(surf, dw, np.where(reach & (dw > 0.45 * peak))[0])]
    # Merge peaks closer than a digit's width (one digit can carry two near-equal maxima on its cap).
    tips.sort(key=lambda u: -dw[u])
    merged = []
    for u in tips:
        if all(np.linalg.norm(V[u] - V[m]) > 0.012 for m in merged):
            merged.append(u)
    if len(merged) != 5:
        raise RuntimeError(f"hand {sign:+.0f}: found {len(merged)} digit tips, expected 5 - fit by hand")
    thumb = min(merged, key=lambda u: dw[u])
    fingers = [u for u in merged if u != thumb]
    c, across = line_fit(V[fingers])
    if np.dot(V[thumb] - c, across) < 0:
        across = -across
    fingers.sort(key=lambda u: -np.dot(V[u] - c, across))  # thumb side first: index, middle, ring, pinky

    digits = {}
    for name, tip in zip(["thumb"] + FINGER_ORDER, [thumb] + fingers):
        dt = surf.geodesic(tip, allowed=hand)
        rows = ring_profile(V, dt, step=0.004, limit=0.2)
        radii = [r["r"] for r in rows]
        tube_r = float(np.median(radii[1:6]))
        base_s = next((r["s"] for r in rows[4:] if r["r"] > 1.6 * tube_r), rows[-1]["s"])
        line = np.array([r["c"] for r in rows if 0.004 <= r["s"] < base_s])
        base = line[-1]
        direction = unit(line[0] - line[-1]) if len(line) > 1 else unit(V[tip] - base)
        knuckle = base - direction * 2.0 * tube_r  # a ring's RMS radius is the digit's radius
        tip_centre = line[0]
        # Halfway along the centreline from knuckle to tip.
        chain = np.vstack([tip_centre, line[1:], knuckle])[::-1]
        seg = np.linalg.norm(np.diff(chain, axis=0), axis=1)
        arc = np.concatenate([[0.0], np.cumsum(seg)])
        half = arc[-1] / 2.0
        k = int(np.searchsorted(arc, half))
        k = min(max(k, 1), len(chain) - 1)
        w = (half - arc[k - 1]) / max(seg[k - 1], 1e-9)
        middle = chain[k - 1] + (chain[k] - chain[k - 1]) * w
        digits[name] = {"tip_vertex": V[tip], "tip": tip_centre, "base": base, "knuckle": knuckle,
                        "middle": middle, "radius": tube_r, "length": float(arc[-1]),
                        "geodesic_from_wrist": float(dw[tip]), "knuckle_rule": "digit base"}

    # The four finger knuckles. A finger that touches its neighbour merges with it early, so its base reads too close
    # to the tip (under a fifth of the way back to the wrist), and one that leans on a neighbour pulls its base toward
    # it. Only the knuckle LEVEL is taken from the fingers that separated cleanly; the knuckles themselves are laid
    # evenly across the palm's own section at that level (thumb excluded), index on the thumb's side, in the middle of
    # the palm's thickness.
    wrist = arm["wrist"]
    up = unit(wrist - np.mean([digits[f]["tip"] for f in FINGER_ORDER], axis=0))
    for f in FINGER_ORDER:
        d = digits[f]
        d["base_fraction"] = float(np.dot(d["base"] - d["tip"], up) / max(np.dot(wrist - d["tip"], up), 1e-9))
    clean = [f for f in FINGER_ORDER if digits[f]["base_fraction"] >= 0.2]
    if not clean:
        raise RuntimeError(f"hand {sign:+.0f}: no finger separates from the palm - fit by hand")
    u_h = unit(np.mean([digits[f]["knuckle"] for f in clean], axis=0) - wrist)
    s_k = float(np.mean([np.dot(digits[f]["knuckle"] - wrist, u_h) for f in clean]))
    across = digits["index"]["tip"] - digits["pinky"]["tip"]
    across = unit(across - u_h * np.dot(across, u_h))
    n_h = np.cross(u_h, across)
    hv_all = V[hand]
    th = digits["thumb"]
    ab = th["tip"] - th["knuckle"]
    tt = np.clip((hv_all - th["knuckle"]) @ ab / float(ab @ ab), 0.0, 1.0)
    thumb_d = np.linalg.norm(hv_all - (th["knuckle"] + tt[:, None] * ab), axis=1)
    s_all = (hv_all - wrist) @ u_h
    sec = hv_all[(np.abs(s_all - s_k) < 0.006) & (thumb_d > 2.0 * th["radius"])]
    a, nn = (sec - wrist) @ across, (sec - wrist) @ n_h
    a_min, a_max, n_mid = float(a.min()), float(a.max()), float((nn.min() + nn.max()) / 2.0)
    for i, f in enumerate(FINGER_ORDER):
        d = digits[f]
        a_f = a_max - (i + 0.5) / 4.0 * (a_max - a_min)
        d["knuckle"] = wrist + u_h * s_k + across * a_f + n_h * n_mid
        d["length"] = float(np.linalg.norm(d["tip"] - d["knuckle"]))
        d["knuckle_rule"] = ("evenly across the palm section at the knuckle level of the clean fingers ("
                             + ", ".join(clean) + ")")

    # The middle joint: the centre of the digit's own cross-section halfway from knuckle to tip (a curled digit
    # bows away from the straight knuckle-tip line, so the line's midpoint can sit outside it). Each hand vertex
    # belongs to the digit whose knuckle-tip segment it is nearest.
    hv = V[hand]
    seg_d = []
    for name in DIGITS:
        a, b = digits[name]["knuckle"], digits[name]["tip"]
        ab = b - a
        tt = np.clip((hv - a) @ ab / float(ab @ ab), 0.0, 1.0)
        seg_d.append(np.linalg.norm(hv - (a + tt[:, None] * ab), axis=1))
    owner = np.argmin(np.array(seg_d), axis=0)
    for k, name in enumerate(DIGITS):
        d = digits[name]
        ax = unit(d["tip"] - d["knuckle"])
        mid = (d["knuckle"] + d["tip"]) / 2.0
        pts = hv[(owner == k) & (seg_d[k] < 0.03)]
        s = (pts - mid) @ ax
        slab = pts[np.abs(s) < 0.004]
        if len(slab) >= 4:
            e1, e2 = perpendicular_basis(ax)
            q = slab - mid
            a1, a2 = q @ e1, q @ e2
            d["middle"] = mid + e1 * (a1.min() + a1.max()) / 2 + e2 * (a2.min() + a2.max()) / 2

    # Palm: the hand between the wrist section and the knuckle line.
    knuckles = np.array([digits[f]["knuckle"] for f in FINGER_ORDER])
    k_mid = knuckles.mean(0)
    hand_axis = unit(k_mid - arm["wrist"])
    s = (V - arm["wrist"]) @ hand_axis
    reach_len = float(np.dot(k_mid - arm["wrist"], hand_axis))
    palm_mask = hand & (s > 0.15 * reach_len) & (s < 0.95 * reach_len)
    e1, e2 = perpendicular_basis(hand_axis)
    q = V[palm_mask] - arm["wrist"]
    a1, a2 = q @ e1, q @ e2
    centre_s = 0.6 * reach_len
    palm_centre = (arm["wrist"] + hand_axis * centre_s + e1 * (a1.min() + a1.max()) / 2
                   + e2 * (a2.min() + a2.max()) / 2)
    # Palmar side: the side the digits curl toward (the fingertips sit off the knuckle-wrist line on that side).
    tips_c = np.array([digits[f]["tip"] for f in FINGER_ORDER]).mean(0)
    across_k = unit(digits["index"]["knuckle"] - digits["pinky"]["knuckle"])
    normal = unit(np.cross(hand_axis, across_k))
    curl = float(np.dot(tips_c - k_mid, normal))
    thumb_side = float(np.dot(digits["thumb"]["tip"] - k_mid, normal))
    if curl + thumb_side < 0:
        normal = -normal
    return {"digits": digits, "palm_centre": palm_centre, "palm_normal": normal, "hand_mask": hand,
            "curl_m": curl, "thumb_offset_m": thumb_side, "digit_tips_found": len(merged)}


def plane_loops(surf, faces, z):
    """The mesh's exact contour at height z: edge/plane crossing points, grouped into loops by shared faces."""
    V = surf.V
    s = V[:, 2] - z
    s = np.where(s == 0.0, 1e-9, s)
    tri = faces
    e = np.stack([tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]]], axis=1)
    crossing = (s[e[..., 0]] * s[e[..., 1]]) < 0
    two = crossing.sum(1) == 2
    if not two.any():
        return []
    e2 = e[two][crossing[two]].reshape(-1, 2, 2)
    keys = np.sort(e2.reshape(-1, 2), axis=1)
    uniq, inverse = np.unique(keys, axis=0, return_inverse=True)
    inverse = inverse.ravel().reshape(-1, 2)
    label = components(len(uniq), inverse)
    a, b = V[uniq[:, 0]], V[uniq[:, 1]]
    sa, sb = s[uniq[:, 0]], s[uniq[:, 1]]
    pts = a + (b - a) * (sa / (sa - sb))[:, None]
    return [pts[label == k] for k in range(label.max() + 1)]


def section(p):
    """Bounding-box centre and extents of a slab's points in X and Y."""
    lo, hi = p[:, :2].min(0), p[:, :2].max(0)
    return (lo + hi) / 2.0, hi - lo


def facing(surf):
    """How far the body is turned about the vertical: the shoulder girdle's long axis (the principal axis of the
    body's section at 0.8 H) and the hip line (between the two legs' sections at 0.3 H), both in degrees from the
    X axis. The contract skeleton faces +Z (glTF), so both should be near zero."""
    V, H = surf.V, surf.height
    skin = surf.main & surf.outer
    faces = surf.F[skin[surf.F].all(1)]

    def fold(a):
        return (a + 90.0) % 180.0 - 90.0

    loops = [g for g in plane_loops(surf, faces, 0.8 * H) if g[:, 0].min() < 0.0 < g[:, 0].max()]
    shoulders = None
    if loops:
        g = max(loops, key=lambda g: np.ptp(g[:, 0]))
        q = g[:, :2] - g[:, :2].mean(0)
        _w, v = np.linalg.eigh(np.cov(q.T))
        shoulders = fold(math.degrees(math.atan2(v[1, -1], v[0, -1])))
    legs = [g for g in plane_loops(surf, faces, 0.3 * H) if len(g) >= 6]
    hips = None
    left = [g for g in legs if g[:, 0].mean() > 0.0]
    right = [g for g in legs if g[:, 0].mean() < 0.0]
    if left and right:
        a = section(max(left, key=len))[0]
        b = section(max(right, key=len))[0]
        hips = fold(math.degrees(math.atan2(a[1] - b[1], a[0] - b[0])))
    return {"shoulder_line_deg": shoulders, "hip_line_deg": hips}


def fit_body(surf, arm_masks):
    """Trunk, legs and head: horizontal sections of the outer skin with the arms removed.

    Definitions (the contract's landmarks, measured):
      crotch    - the lowest point of the body's midline (below it the legs are apart);
      hip_z     - crotch + 0.035 H: the femoral heads sit ~6 cm above the crotch on a 1.8 m body. The thigh heads are
                  the thigh axis (sections between knee and crotch) at that height, and the pelvis is their midpoint,
                  as in the contract (pelvis and thigh heads share one height);
      ankle_z   - 1 cm below the lowest leg section that is still leg-shaped (front-to-back extent within 1.25x the
                  shin's): the foot flares below it. The ankle joint is that section's centre;
      knee_z    - the contract's knee ratio between ankle and hip ((knee_z - ankle_z)/(hip_z - ankle_z) = 0.41/0.86):
                  a straight leg has no bend to measure. The knee joint is the leg section's centre there;
      toe       - the ball of the foot: 72% of the way from heel to toe tip, at 0.35 ankle_z (the contract's height);
      waist_z   - the narrowest trunk section between hip and chest; chest_z the deepest one (front to back) between
                  waist and armpit; spine_01/spine_03 halfway, as in the contract;
      neck_z    - the base of the neck: where the trunk narrows to 1.6x the neck's narrowest width;
      head      - the skull base at the jaw line: above the neck's narrowest section, where it has widened to 1.25x.
    """
    V, H = surf.V, surf.height
    skin = surf.main & surf.outer & ~arm_masks
    P = V[skin]
    faces = surf.F[skin[surf.F].all(1)]

    def trunk(z):
        loops = [g for g in plane_loops(surf, faces, z) if len(g) >= 6]
        spans = [g for g in loops if g[:, 0].min() < 0.0 < g[:, 0].max()]
        return max(spans, key=len) if spans else None

    def leg_loop(z, sign):
        # All contour pieces on that side: a loop can be cut where a stray vertex was dropped from the skin.
        loops = [g for g in plane_loops(surf, faces, z) if len(g) >= 3 and sign * g[:, 0].min() > 0.0
                 and abs(g[:, 0].mean()) < 0.3]
        return np.vstack(loops) if loops else None

    def on_midline(z):
        g = trunk(z)
        return g is not None and bool((np.abs(g[:, 0]) < 0.01).any())

    # The crotch: the lowest height at which the body's contour reaches the midline.
    z = 0.3 * H
    while not on_midline(z) and z < 0.7 * H:
        z += 0.0025
    crotch = float(z)
    hip_z = crotch + 0.035 * H
    # Just below the crotch the inner thighs may touch; the thigh axis is measured where the legs are apart.
    z = crotch
    while trunk(z) is not None and z > 0.3 * H:
        z -= 0.005
    thighs_apart_z = float(z)

    legs = {}
    for side, sign in SIDES:
        def leg(z):
            return leg_loop(z, sign)
        shin = [section(leg(z))[1][1] for z in np.arange(0.15 * H / 1.8, 0.25 * H / 1.8, 0.01) if leg(z) is not None]
        shin_depth = float(np.median(shin))
        z = 0.25 * H / 1.8
        while z > 0.03:
            g = leg(z - 0.01)
            if g is None or section(g)[1][1] > 1.25 * shin_depth:
                break
            z -= 0.01
        ankle_z = z - 0.01
        ankle_c, _ = section(leg(ankle_z + 0.01))
        f = FIT_FAMILIES["standard_humanoid"]
        knee_z = ankle_z + (hip_z - ankle_z) * (f["knee_z"] - f["ankle_z"]) / (f["hip_z"] - f["ankle_z"])
        knee_c, _ = section(leg(knee_z))
        thigh = np.array([[*section(leg(z))[0], z] for z in np.arange(knee_z + 0.05, thighs_apart_z - 0.01, 0.01)
                          if leg(z) is not None])
        tc, tu = line_fit(thigh)
        hip = tc + tu * (hip_z - tc[2]) / tu[2]
        foot = P[(P[:, 2] < ankle_z) & (sign * P[:, 0] > 0.02) & (np.abs(P[:, 0]) < 0.3)]
        heel_y, tip_y = float(foot[:, 1].max()), float(foot[:, 1].min())
        ball_y = heel_y + 0.72 * (tip_y - heel_y)
        ball = foot[np.abs(foot[:, 1] - ball_y) < 0.01]
        ball_x = float((ball[:, 0].min() + ball[:, 0].max()) / 2.0)
        legs[side] = {
            "thigh": np.array([hip[0], hip[1], hip_z]),
            "calf": np.array([knee_c[0], knee_c[1], knee_z]),
            "foot": np.array([ankle_c[0], ankle_c[1], ankle_z]),
            "toe": np.array([ball_x, ball_y, 0.35 * ankle_z]),
            "toe_tip": np.array([ball_x, tip_y + 0.01, 0.35 * ankle_z]),
            "foot_length_m": heel_y - tip_y, "shin_depth_m": shin_depth,
        }

    zs = np.arange(hip_z + 0.05, 0.75 * H, 0.01)
    widths = smooth([section(trunk(z))[1][0] for z in zs], 5)
    waist_z = float(zs[int(np.argmin(widths))])
    armpit_z = min(0.78 * H, 0.75 * H)
    zc = np.arange(waist_z + 0.05, armpit_z, 0.01)
    depths = smooth([section(trunk(z))[1][1] for z in zc], 3)
    chest_z = float(zc[int(np.argmax(depths))])

    zn = np.arange(0.8 * H, 0.92 * H, 0.005)
    nw = [section(trunk(z))[1][0] for z in zn]
    i_narrow = int(np.argmin(nw))
    neck_narrow_z, neck_w = float(zn[i_narrow]), float(nw[i_narrow])
    neck_z = next(float(z) for z, w in zip(zn[i_narrow::-1], nw[i_narrow::-1]) if w > 1.6 * neck_w)
    # The head joint sits where the neck meets the skull (the jaw line: the section has widened to 1.25x the neck's
    # narrowest). The fit family's mid-skull head joint would leave the whole face on the neck bone in the heat solve,
    # so every nod would shear the face.
    head_z = next(float(z) for z, w in zip(zn[i_narrow:], nw[i_narrow:]) if w > 1.25 * neck_w)

    def centre(z, column=None):
        g = trunk(z)
        if column is not None:
            g = g[np.abs(g[:, 0]) < column]
        c, _ = section(g)
        return np.array([0.0, c[1], z])

    pelvis = (legs["l"]["thigh"] + legs["r"]["thigh"]) / 2.0
    pelvis[0] = 0.0
    spine = {
        "pelvis": pelvis,
        "spine_01": centre((hip_z + waist_z) / 2.0),
        "spine_02": centre(waist_z),
        "spine_03": centre((waist_z + chest_z) / 2.0),
        "chest": centre(chest_z),
        "neck": centre(neck_z, column=0.75 * neck_w),
        # On the neck's axis (its narrowest section's centre), not the jaw line's centre, which the chin pulls forward.
        "head": np.array([0.0, section(trunk(neck_narrow_z))[0][1], head_z]),
    }
    hip_band = trunk(hip_z + 0.04)
    loops = [g for g in plane_loops(surf, faces, chest_z + 0.10) if np.abs(g[:, 0]).min() < 0.15]
    back_band = np.vstack([g[np.abs(g[:, 0]) < 0.15] for g in loops])
    return {
        "legs": legs, "spine": spine,
        "landmarks": {"crotch_z": crotch, "hip_z": hip_z, "waist_z": waist_z, "chest_z": chest_z,
                      "neck_z": neck_z, "neck_narrowest_z": neck_narrow_z, "neck_width_m": neck_w,
                      "head_z": head_z, "height": H,
                      "waist_w": float(np.ptp(trunk(waist_z)[:, 0])),
                      "chest_w": float(np.ptp(trunk(chest_z)[:, 0])),
                      "chest_d": float(np.ptp(trunk(chest_z)[:, 1])),
                      "hip_w": float(np.ptp(hip_band[:, 0])),
                      "hip_socket_band": {"half_width": float(np.abs(hip_band[:, 0]).max()),
                                          "centre_y": float(section(hip_band)[0][1])},
                      "back_surface_y": float(back_band[:, 1].max())},
    }


# --------------------------------------------------------------------------------------------------------------------
# Skeleton assembly
# --------------------------------------------------------------------------------------------------------------------

DIGITS = ["thumb"] + FINGER_ORDER


def assemble_joints(arms, hands, body):
    """Joint heads in the mesh's own pose, by the contract's bone names, plus the digit tips (bone tails)."""
    J = {"root": np.zeros(3)}
    J.update({k: np.array(v, dtype=float) for k, v in body["spine"].items()})
    tips = {}
    for side, _sign in SIDES:
        a, h, lg = arms[side], hands[side], body["legs"][side]
        sh = np.array(a["shoulder"], dtype=float)
        J[f"clavicle_{side}"] = np.array([0.35 * sh[0], sh[1], sh[2]])  # the contract's clavicle: 0.35 of the way out
        J[f"upperarm_{side}"] = sh
        J[f"lowerarm_{side}"] = np.array(a["elbow"], dtype=float)
        J[f"hand_{side}"] = np.array(a["wrist"], dtype=float)
        for name in DIGITS:
            d = h["digits"][name]
            J[f"{name}_01_{side}"] = np.array(d["knuckle"], dtype=float)
            J[f"{name}_02_{side}"] = np.array(d["middle"], dtype=float)
            tips[f"{name}_{side}"] = np.array(d["tip"], dtype=float)
        for b in ("thigh", "calf", "foot", "toe"):
            J[f"{b}_{side}"] = np.array(lg[b], dtype=float)
    return J, tips


def helper_joints(J):
    """IK and pole targets, with the contract's offsets from the joints they drive (`bone_positions`)."""
    out = {}
    for side, sign in SIDES:
        elbow = J[f"lowerarm_{side}"]
        knee = J[f"calf_{side}"]
        out[f"IK_hand_{side}"] = J[f"hand_{side}"].copy()
        out[f"IK_elbow_{side}"] = elbow + np.array([0.05 * sign, 0.12, -0.05])
        out[f"IK_foot_{side}"] = J[f"foot_{side}"].copy()
        out[f"IK_knee_{side}"] = knee + np.array([0.0, -0.12, 0.0])
    return out


def tail_of(name, J, tips, bones):
    """The contract's tail rule (`_blender_canonical_body.build_armature`): the first child's head, a digit's tip,
    else 5 cm straight up."""
    child = next((b for b, p, _d in bones if p == name), None)
    if child:
        tail = J[child]
    elif name.split("_")[0] in DIGITS and "_02_" in name:
        tail = tips[f"{name.split('_')[0]}_{name[-1]}"]
    else:
        tail = J[name] + np.array([0.0, 0.0, 0.05])
    if np.linalg.norm(tail - J[name]) < 1e-4:
        raise RuntimeError(f"bone {name} would have zero length")
    return tail


def canonical_directions(fam):
    """Each bone's rest direction on the contract skeleton (head to tail, by the contract's own tail rule)."""
    C = {k: np.array(v, dtype=float) for k, v in bone_positions(fam).items()}
    tips = {f"{d}_{s}": C[f"__tip_{d}_{s}"] for d in DIGITS for s, _sign in SIDES}
    bones = skeleton_bones()
    return {b: unit(tail_of(b, C, tips, bones) - C[b]) for b, _p, _d in bones}


def build_rig(name, J, tips, for_heat=False, heat_tails=None, directions=None):
    """The armature, every bone at roll 0 like the contract's.

    for_heat: bones point at their children in the mesh's own pose (heat weights follow the limbs as modelled);
    `heat_tails` lengthens chosen bones for the solve only (the head bone to the crown, the toes to the toe tips).
    directions: the exported rig's bones take the contract skeleton's rest directions, so every joint frame is the
    contract's own and a clip's rotation keys mean the same thing on this body; the heads stay where they were
    fitted. (Only the joint positions and frames reach the glTF; Blender tails are not exported.)"""
    bones = skeleton_bones()
    data = bpy.data.armatures.new(f"RIG_{name}")
    rig = bpy.data.objects.new(f"RIG_{name}", data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for bone_name, parent, deform in bones:
        eb = data.edit_bones.new(bone_name)
        eb.head = Vector(tuple(map(float, J[bone_name])))
        tail = (heat_tails or {}).get(bone_name) if for_heat else None
        if tail is None:
            tail = tail_of(bone_name, J, tips, bones)
            if directions is not None:
                length = max(float(np.linalg.norm(tail - J[bone_name])), 0.01)
                tail = J[bone_name] + directions[bone_name] * length
        eb.tail = Vector(tuple(map(float, tail)))
        eb.roll = 0.0
        # The root stands between the feet: it must not take skin in the heat solve, or the soles would stay behind
        # when the pelvis moves. It keeps the contract's deform flag in the exported rig.
        eb.use_deform = deform and not (for_heat and bone_name == "root")
        if parent:
            eb.parent = created[parent]
            eb.use_connect = False
        created[bone_name] = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


# --------------------------------------------------------------------------------------------------------------------
# Weights
# --------------------------------------------------------------------------------------------------------------------

def heat_weights(surf, rig, names, normals, refine=None):
    """Bone-heat weights on the welded outer skin, then carried to every other welded vertex.

    The proxy is the main body's outer skin only: the inner wall would block every bone from the skin's view (bone
    heat needs line of sight), and the boot shells are separate islands with no bone in view at all. An inner-wall
    vertex takes the weights of the skin point straight through the wall from it (a ray along its inward-facing
    normal, interpolated across the skin triangle it hits), so the wall moves exactly with the skin above it and
    cannot poke through; a boot vertex (or an inner one whose ray misses) takes its nearest skin vertex's weights.
    `refine(W, skin)` corrects the skin's weights (welded rows, `skin` marks the proxy) before they are carried.
    """
    from mathutils import kdtree
    from mathutils.bvhtree import BVHTree
    keep = surf.main & surf.outer
    faces = surf.F[keep[surf.F].all(1)]
    used = np.unique(faces)
    remap = -np.ones(len(surf.V), dtype=np.int64)
    remap[used] = np.arange(len(used))
    me = bpy.data.meshes.new("heat_proxy")
    me.from_pydata(surf.V[used].tolist(), [], remap[faces].tolist())
    me.update()
    proxy = bpy.data.objects.new("heat_proxy", me)
    bpy.context.collection.objects.link(proxy)
    for o in bpy.context.scene.objects:
        o.select_set(o in (proxy, rig))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    col = {n: i for i, n in enumerate(names)}
    Wp = np.zeros((len(used), len(names)))
    gname = {g.index: g.name for g in proxy.vertex_groups}
    for v in me.vertices:
        for g in v.groups:
            name = gname[g.group]
            if name in col and g.weight > 1e-6:
                Wp[v.index, col[name]] = g.weight
    unweighted = int((Wp.sum(1) <= 1e-6).sum())

    # Fill any vertex the solve left empty from its nearest weighted neighbour (reported; zero on a clean solve).
    ok = Wp.sum(1) > 1e-6
    tree = kdtree.KDTree(int(ok.sum()))
    ok_idx = np.where(ok)[0]
    for k, i in enumerate(ok_idx):
        tree.insert(Vector(tuple(surf.V[used[i]])), k)
    tree.balance()
    for i in np.where(~ok)[0]:
        _co, k, _d = tree.find(Vector(tuple(surf.V[used[i]])))
        Wp[i] = Wp[ok_idx[k]]

    refine_stats = None
    if refine is not None:
        W_skin = np.zeros((len(surf.V), len(names)))
        W_skin[used] = Wp
        in_skin = np.zeros(len(surf.V), dtype=bool)
        in_skin[used] = True
        W_skin, refine_stats = refine(W_skin, in_skin)
        Wp = W_skin[used]

    W = np.zeros((len(surf.V), len(names)))
    W[used] = Wp
    tree = kdtree.KDTree(len(used))
    for k, i in enumerate(used):
        tree.insert(Vector(tuple(surf.V[i])), k)
    tree.balance()
    skin_faces = remap[faces]
    skin = BVHTree.FromPolygons([tuple(map(float, v)) for v in surf.V[used]], skin_faces.tolist(), all_triangles=True)
    others = np.setdiff1d(np.arange(len(surf.V)), used)
    far, through = [], 0
    for i in others:
        p = surf.V[i]
        if surf.main[i]:
            d = Vector(tuple(map(float, -normals[i])))
            hit, _n, fi, dist = skin.ray_cast(Vector(tuple(map(float, p))) + d * 1e-5, d, 0.02)
            if hit is not None:
                a, b, c = (surf.V[used[j]] for j in skin_faces[fi])
                w = barycentric(np.array(hit), a, b, c)
                W[i] = w[0] * Wp[skin_faces[fi][0]] + w[1] * Wp[skin_faces[fi][1]] + w[2] * Wp[skin_faces[fi][2]]
                far.append(dist)
                through += 1
                continue
        _co, k, dist = tree.find(Vector(tuple(map(float, p))))
        W[i] = Wp[k]
        far.append(dist)
    bpy.data.objects.remove(proxy, do_unlink=True)
    stats = {"proxy_vertices": int(len(used)), "proxy_unweighted_after_heat": unweighted,
             "transferred_vertices": int(len(others)), "transferred_through_the_wall": through,
             "transferred_by_nearest_skin_vertex": int(len(others) - through),
             "transfer_distance_m": {"median": float(np.median(far)) if far else 0.0,
                                     "p99": float(np.percentile(far, 99)) if far else 0.0,
                                     "max": float(np.max(far)) if far else 0.0}}
    if refine_stats is not None:
        stats["refinement"] = refine_stats
    return W, stats


def barycentric(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
    d20, d21 = v2 @ v0, v2 @ v1
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-18:
        return np.array([1.0, 0.0, 0.0])
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    out = np.clip(np.array([1.0 - v - w, v, w]), 0.0, 1.0)
    return out / out.sum()


def side_filter(W, V, names, band=0.025):
    """Fade a limb's weight out across the body's midline: full on its own side, half on the midline, none beyond
    `band` on the other side; the rest renormalised.

    Bone heat reaches across a gap it can see through: where the inner thighs and the buttocks face each other, each
    thigh took up to half of the other thigh's weight, which tears the skin between the legs open when they part.
    A left bone has no business moving skin right of the midline."""
    x = V[:, 0]
    factor = np.ones_like(W)
    for k, n in enumerate(names):
        if n.endswith("_l"):
            factor[:, k] = np.clip((band + x) / (2.0 * band), 0.0, 1.0)
        elif n.endswith("_r"):
            factor[:, k] = np.clip((band - x) / (2.0 * band), 0.0, 1.0)
    out = W * factor
    total = out.sum(1, keepdims=True)
    keep = total[:, 0] > 1e-9
    out[keep] /= total[keep]
    out[~keep] = W[~keep]
    removed = float(np.abs(out - W).sum(1).max())
    return out, removed


def limit_weights(W, count=4, floor=0.01):
    """Keep each vertex's `count` largest influences above `floor`, renormalised (what the exporter writes)."""
    out = np.zeros_like(W)
    order = np.argsort(-W, axis=1)[:, :count]
    rows = np.arange(len(W))[:, None]
    top = W[rows, order]
    top = np.where(top >= floor, top, 0.0)
    total = top.sum(1, keepdims=True)
    top = np.where(total > 0, top / np.where(total > 0, total, 1.0), 0.0)
    out[rows, order] = top
    return out


# Where bone heat over-reaches (metres). Heat gives each skin point to the bones it sees nearest, and two bones reach
# well past their own limb: the upper arm, which in the mesh's pose hangs against the side of the chest, took 0.2-0.6
# of the chest wall under the armpit, so raising the arm dragged that skin up and folded it over the chest; and the
# thighs, whose heads sit far nearer the flank and buttock than the pelvis bone does (one short segment on the
# midline), took half of each buttock to 10 cm above the hip joint and a tenth to the waist, so every hip flexion
# swung the buttock out and pulled the belly. Each is capped across a measured boundary with a smooth band. The band
# widths were set by sweeping all 17 humanoid clips under the rest-relative retarget: narrower bands fold the skin
# at the band (linear blend skinning pulls a half-weighted point toward the joint), wider ones bend the limb itself.
ARM_UNDER_MID = 0.03   # upper arm, under the arm: half influence this far along the arm past its root ring,
ARM_UNDER_HALF = 0.08  # none this much less (5 cm onto the chest wall), full this much more (11 cm down the arm)
ARM_CAP_MID = -0.02    # over the shoulder cap: half influence 2 cm past the ring, so the deltoid follows the arm,
ARM_CAP_HALF = 0.12    # and none 14 cm past it, at the base of the neck
CAP_SIDE = 0.05        # half-width of the turn from under-arm to shoulder cap, about the plane of the arm's axis
HIP_ABOVE = 0.14       # thigh: no influence this far above the femoral heads' height (0.41 at it),
HIP_BELOW = 0.18       # full influence this far below it
SMOOTH_ITERATIONS = 4  # umbrella passes over the vertices the caps changed (and one ring round them)


def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def cap_bone(W, b, limit, fallback):
    """Cap bone b's weight at `limit` per vertex. The weight taken off goes to the vertex's other bones in proportion
    to what they hold (they are scaled up to fill the row; renormalising the whole row would hand it back to b), or to
    `fallback` (b's parent) where b held the vertex alone. Returns the weight moved per vertex."""
    new = np.minimum(W[:, b], limit)
    moved = W[:, b] - new
    rows = moved > 1e-9
    others = W[rows].copy()
    others[:, b] = 0.0
    total = others.sum(1)
    alone = total <= 1e-9
    others[~alone] *= ((1.0 - new[rows][~alone]) / total[~alone])[:, None]
    others[alone, fallback] = 1.0 - new[rows][alone]
    others[:, b] = new[rows]
    W[rows] = others
    return moved


def refine_weights(surf, skin, W, names, J, arms):
    """Cap the upper arms and thighs to their own limbs (see ARM_* and HIP_*), on the welded outer skin.

    Upper arm: the arm is the tube `fit_arm` found by geodesic rings from the fingertips up to its root ring, where
    the rings outgrow the tube at the armpit. A signed geodesic distance from that ring (+ along the arm, - onto the
    trunk) sets the cap, a smooth ramp centred ARM_UNDER_MID along the arm with half-width ARM_UNDER_HALF. Over the
    shoulder cap it is centred ARM_CAP_MID with half-width ARM_CAP_HALF instead, so the deltoid follows the arm and
    the fade ends at the neck. The cap side is the one above the plane that holds the upper arm's axis and the body's
    front-back axis (mesh pose); the chest wall under the arm lies below it.
    Thigh: none at HIP_ABOVE above the femoral heads' height, full at HIP_BELOW below it, about the plane through both
    heads square to the trunk, which the flexing thigh swings about; above it the flank, buttock and belly belong to
    the pelvis and spine.
    The taken weight goes to each vertex's other bones (the clavicle, chest and spine under the arm; the pelvis and
    spine above the hip), then SMOOTH_ITERATIONS umbrella passes smooth the changed region."""
    V = surf.V
    col = {n: i for i, n in enumerate(names)}
    e = surf.edges
    W = W.copy()
    moved = np.zeros(len(V))
    stats = {}
    for side, _sign in SIDES:
        shoulder = J[f"upperarm_{side}"]
        arm = arms[side]["arm_mask"] & skin
        cut = arm[e[:, 0]] != arm[e[:, 1]]
        ring = np.unique(np.where(arm[e[cut]], e[cut], -1))
        ring = ring[ring >= 0]
        ring = ring[np.linalg.norm(V[ring] - shoulder, axis=1) < 0.15]  # the arm's root, not a hand touching the hip
        inside = surf.geodesic(ring, allowed=arm)
        outside = surf.geodesic(ring, allowed=skin & ~arm)
        s = np.where(arm, inside, -outside)
        s = np.where(np.isfinite(s), s, np.where(arm, 1.0, -1.0))
        axis = unit(J[f"lowerarm_{side}"] - shoulder)
        up = unit(np.cross(axis, [0.0, 1.0, 0.0]))
        if up[2] < 0:
            up = -up
        cap_side = smoothstep(((V - shoulder) @ up + CAP_SIDE) / (2.0 * CAP_SIDE))
        mid = ARM_UNDER_MID + (ARM_CAP_MID - ARM_UNDER_MID) * cap_side
        half = ARM_UNDER_HALF + (ARM_CAP_HALF - ARM_UNDER_HALF) * cap_side
        limit = np.where(skin, smoothstep(0.5 + (s - mid) / (2.0 * half)), 1.0)
        m = cap_bone(W, col[f"upperarm_{side}"], limit, col[f"clavicle_{side}"])
        moved = np.maximum(moved, m)
        stats[f"upperarm_{side}"] = {"root_ring_vertices": int(len(ring)),
                                     "root_ring_z_m": [round(float(V[ring, 2].min()), 4),
                                                       round(float(V[ring, 2].max()), 4)],
                                     "vertices_capped": int((m > 1e-3).sum()),
                                     "max_weight_moved": round(float(m.max()), 4)}
        hip = float(J[f"thigh_{side}"][2])
        limit = np.where(skin, smoothstep((hip + HIP_ABOVE - V[:, 2]) / (HIP_ABOVE + HIP_BELOW)), 1.0)
        m = cap_bone(W, col[f"thigh_{side}"], limit, col["pelvis"])
        moved = np.maximum(moved, m)
        stats[f"thigh_{side}"] = {"hip_joint_z_m": round(hip, 4), "vertices_capped": int((m > 1e-3).sum()),
                                  "max_weight_moved": round(float(m.max()), 4)}
    # Smooth where the caps acted (plus one ring), holding every other vertex fixed.
    zone = skin & (moved > 1e-3)
    grow = zone.copy()
    np.logical_or.at(grow, e[:, 0], zone[e[:, 1]])
    np.logical_or.at(grow, e[:, 1], zone[e[:, 0]])
    zone = grow & skin
    ok = skin[e[:, 0]] & skin[e[:, 1]]
    a, b = e[ok, 0], e[ok, 1]
    degree = np.zeros(len(V))
    np.add.at(degree, a, 1.0)
    np.add.at(degree, b, 1.0)
    for _ in range(SMOOTH_ITERATIONS):
        acc = np.zeros_like(W)
        np.add.at(acc, a, W[b])
        np.add.at(acc, b, W[a])
        avg = acc / np.maximum(degree, 1.0)[:, None]
        W[zone] = 0.5 * W[zone] + 0.5 * avg[zone]
    W[zone] /= np.maximum(W[zone].sum(1, keepdims=True), 1e-12)
    stats["smoothed_vertices"] = int(zone.sum())
    stats["bands_m"] = {"arm_under_mid": ARM_UNDER_MID, "arm_under_half": ARM_UNDER_HALF, "arm_cap_mid": ARM_CAP_MID,
                        "arm_cap_half": ARM_CAP_HALF, "cap_side": CAP_SIDE, "hip_above": HIP_ABOVE,
                        "hip_below": HIP_BELOW, "smooth_iterations": SMOOTH_ITERATIONS}
    return W, stats


# --------------------------------------------------------------------------------------------------------------------
# The re-pose to the contracted A-pose
# --------------------------------------------------------------------------------------------------------------------

def signed_angle_about(a, b, axis):
    axis = unit(axis)
    a = unit(a - axis * np.dot(a, axis))
    b = unit(b - axis * np.dot(b, axis))
    return math.atan2(float(np.dot(np.cross(a, b), axis)), float(np.dot(a, b)))


def arm_pose(J, tips, C):
    """World-space rotations that lay each arm chain along the contract's A-pose.

    Per side, in chain order: the upper arm swings about the shoulder onto the contract's upper-arm direction; the
    forearm (already carried by that swing) swings about the elbow onto the contract's forearm direction, which
    straightens the elbow as the contract's reference pose does; the hand swings about the wrist onto the contract's
    wrist-to-index-knuckle direction and then turns about that axis until its knuckle line (pinky to index) lies
    along the contract's. The digits ride the hand unchanged. Every other bone keeps its rest.
    Returns per-bone rotation G and the posed joint positions.
    """
    G = {name: np.eye(3) for name in J}
    Pp = {name: np.array(p, dtype=float) for name, p in J.items()}
    Tp = {name: np.array(p, dtype=float) for name, p in tips.items()}
    report = {}
    for side, _sign in SIDES:
        u, l, h = f"upperarm_{side}", f"lowerarm_{side}", f"hand_{side}"
        i1, p1 = f"index_01_{side}", f"pinky_01_{side}"
        d_u, d_l = unit(C[l] - C[u]), unit(C[h] - C[l])
        d_h = unit(C[i1] - C[h])
        R_u = rotation_between(J[l] - J[u], d_u)
        G_u = R_u
        R_l = rotation_between(G_u @ (J[h] - J[l]), d_l)
        G_l = R_l @ G_u
        R_h = rotation_between(G_l @ (J[i1] - J[h]), d_h)
        G_h = R_h @ G_l
        twist = signed_angle_about(G_h @ (J[i1] - J[p1]), C[i1] - C[p1], d_h)
        G_h = axis_angle(d_h, twist) @ G_h
        Pp[l] = J[u] + G_u @ (J[l] - J[u])
        Pp[h] = Pp[l] + G_l @ (J[h] - J[l])
        G[u], G[l], G[h] = G_u, G_l, G_h
        for name in DIGITS:
            for k in ("01", "02"):
                b = f"{name}_{k}_{side}"
                Pp[b] = Pp[h] + G_h @ (J[b] - J[h])
                G[b] = G_h
            Tp[f"{name}_{side}"] = Pp[h] + G_h @ (tips[f"{name}_{side}"] - J[h])
        report[side] = {
            "upper_arm_below_horizontal_deg": {"mesh": math.degrees(math.asin(-unit(J[l] - J[u])[2])),
                                               "contract": math.degrees(math.asin(-d_u[2]))},
            "upper_arm_swing_deg": angle_deg(J[l] - J[u], d_u),
            "elbow_straightened_deg": angle_deg(G_u @ (J[h] - J[l]), d_l),
            "hand_swing_deg": angle_deg(G_l @ (J[i1] - J[h]), d_h),
            "hand_twist_deg": math.degrees(twist),
        }
    return G, Pp, Tp, report


def skin_transforms(G, J, Pp, names):
    """Per-bone 3x4 transform (rotation about the bone's rest head, carried to its posed head)."""
    R = np.stack([G[n] for n in names])
    t = np.stack([Pp[n] - G[n] @ J[n] for n in names])
    return R, t


def deform(P, N, W, R, t):
    """Linear blend skinning - the engine's maths - of positions and normals."""
    out = np.zeros_like(P)
    nrm = np.zeros_like(N)
    for b in np.where(W.sum(0) > 0)[0]:
        w = W[:, b:b + 1]
        m = w[:, 0] > 0
        if not m.any():
            continue
        out[m] += w[m] * (P[m] @ R[b].T + t[b])
        nrm[m] += w[m] * (N[m] @ R[b].T)
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-12)
    return out, nrm


# --------------------------------------------------------------------------------------------------------------------
# The baked normal map after the re-pose
# --------------------------------------------------------------------------------------------------------------------

# The tangent-space normal map was baked on the reconstruction as it stood, arms hanging against the chest. Where two
# body parts nearly touched, the bake's rays crossed the gap and wrote the neighbour's surface into the texture, and
# where the re-pose stretches the skin the baked detail no longer fits it. Hidden while the arm lies against the body,
# both show once the arm rises, as saw-toothed patches (the texture's own texels, magnified) on the side chest and the
# inner upper arm. So the map is faded toward flat wherever the re-pose moved a region against its bake-time
# neighbourhood or stretched it, by as much as it did.
NM_NEAR = 0.06       # bake-time neighbours this close count in full, fading to none at NM_FAR (metres): the reach of
NM_FAR = 0.14        # bake's contamination on this body (patches up to 7 cm from the arm on the chest, 11 on the arm)
NM_TURN_DEG = 10.0   # relative turn against a bake-time neighbour that flattens the map fully (smoothstep from 0)
NM_STRETCH = (0.03, 0.15)  # principal stretch |ln s| of a skin triangle: none below, flat above
NM_SMOOTH = 3        # umbrella passes over the welded mesh (raise-only) so the fade has no facet steps
NM_GUTTER = 6        # texels the fade is carried past each UV island's edge (bilinear and mip filtering reach)


def repose_attenuation(P, P_new, tri, W, R):
    """Per-vertex fade (0 keep the baked normal, 1 flat) from the re-pose (P -> P_new with bone rotations R).

    Motion: each vertex's own blended rotation (sum of w_b R_b) against that of every vertex within NM_FAR of it in
    the mesh's own pose, weighted 1 inside NM_NEAR and smoothly to 0 at NM_FAR; the largest weighted difference, as an
    angle, over NM_TURN_DEG. A region carried rigidly (a whole forearm) differs from none of its neighbours and keeps
    its map; skin that was near a limb that swung - across a contact gap or through a blend zone - loses it as far
    as the limb turned. Stretch: the larger |ln| principal stretch of the triangles round the vertex."""
    surf = Surface(P, tri)
    first = np.zeros(len(surf.V), dtype=np.int64)
    first[surf.to_welded[::-1]] = np.arange(len(P))[::-1]
    V, Vn = surf.V, P_new[first]
    A = np.einsum("nb,bij->nij", W[first], R).reshape(len(V), 9)
    order = np.argsort(V[:, 2], kind="stable")
    z = V[order, 2]
    turn = np.zeros(len(V))
    for s in range(0, len(V), 512):
        rows = order[s:s + 512]
        lo, hi = np.searchsorted(z, z[s:s + 512].min() - NM_FAR), np.searchsorted(z, z[s:s + 512].max() + NM_FAR)
        cols = order[lo:hi]
        d = np.linalg.norm(V[rows][:, None, :] - V[cols][None, :, :], axis=2)
        r, c = np.nonzero(d < NM_FAR)
        if not len(r):
            continue
        k = 1.0 - smoothstep((d[r, c] - NM_NEAR) / (NM_FAR - NM_NEAR))
        # |R_a - R_b|_F = 2 sqrt(2) sin(angle / 2) for two rotations; the blended matrices are read the same way.
        f = np.linalg.norm(A[rows[r]] - A[cols[c]], axis=1) / (2.0 * math.sqrt(2.0))
        ang = np.degrees(2.0 * np.arcsin(np.clip(f, 0.0, 1.0)))
        np.maximum.at(turn, rows[r], k * ang)
    motion = smoothstep(turn / NM_TURN_DEG)

    F = surf.F
    e1, e2 = V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]]
    f1, f2 = Vn[F[:, 1]] - Vn[F[:, 0]], Vn[F[:, 2]] - Vn[F[:, 0]]
    u = e1 / np.maximum(np.linalg.norm(e1, axis=1, keepdims=True), 1e-12)
    w = np.cross(e1, e2)
    v = np.cross(w / np.maximum(np.linalg.norm(w, axis=1, keepdims=True), 1e-12), u)
    rest2 = np.stack([np.stack([(e1 * u).sum(1), (e2 * u).sum(1)], 1),
                      np.stack([(e1 * v).sum(1), (e2 * v).sum(1)], 1)], 1)       # 2x2: rest edges in the plane
    ok = np.abs(np.linalg.det(rest2)) > 1e-14
    grad = np.zeros((len(F), 3, 2))
    grad[ok] = np.stack([f1, f2], 2)[ok] @ np.linalg.inv(rest2[ok])        # 3x2 deformation gradient
    sv = np.linalg.svd(grad[ok], compute_uv=False)
    tri_stretch = np.zeros(len(F))
    tri_stretch[ok] = np.abs(np.log(np.maximum(sv, 1e-6))).max(1)
    stretch = np.zeros(len(V))
    np.maximum.at(stretch, F.ravel(), np.repeat(tri_stretch, 3))
    by_stretch = smoothstep((stretch - NM_STRETCH[0]) / (NM_STRETCH[1] - NM_STRETCH[0]))

    a = np.maximum(motion, by_stretch)
    e = surf.edges
    degree = np.zeros(len(V))
    np.add.at(degree, e[:, 0], 1.0)
    np.add.at(degree, e[:, 1], 1.0)
    for _ in range(NM_SMOOTH):
        acc = np.zeros(len(V))
        np.add.at(acc, e[:, 0], a[e[:, 1]])
        np.add.at(acc, e[:, 1], a[e[:, 0]])
        a = np.maximum(a, acc / np.maximum(degree, 1.0))
    stats = {"max_relative_turn_deg": round(float(turn.max()), 2),
             "max_triangle_stretch_ln": round(float(tri_stretch.max()), 4),
             "vertices_faded_over_0.1": int((a[surf.to_welded] > 0.1).sum()),
             "vertices_faded_over_0.9": int((a[surf.to_welded] > 0.9).sum()),
             "vertices_faded_by_motion_over_0.5": int((motion[surf.to_welded] > 0.5).sum()),
             "vertices_faded_by_stretch_over_0.5": int((by_stretch[surf.to_welded] > 0.5).sum()),
             "constants": {"near_m": NM_NEAR, "far_m": NM_FAR, "turn_deg": NM_TURN_DEG,
                           "stretch_ln": list(NM_STRETCH), "smooth_passes": NM_SMOOTH, "gutter_texels": NM_GUTTER}}
    return a[surf.to_welded], stats


def normal_map_image(material):
    """The image feeding the material's Normal Map node (as the glTF importer builds it), or None."""
    if material is None or material.node_tree is None:
        return None, None
    for node in material.node_tree.nodes:
        if node.type == "NORMAL_MAP":
            link = node.inputs["Color"].links[0] if node.inputs["Color"].links else None
            if link is not None and link.from_node.type == "TEX_IMAGE" and link.from_node.image is not None:
                return link.from_node, link.from_node.image
    return None, None


def fade_normal_map(obj, fade, out_path):
    """Rasterise the per-vertex fade into the normal map's UV space and blend its texels toward flat (0.5, 0.5, 1).

    Barycentric over every UV triangle (the largest fade where islands overlap), carried NM_GUTTER texels past the
    island edges; each texel's normal becomes normalize(lerp(n, +Z, fade)). The faded map is written to `out_path`
    and replaces the material's normal image for the export. Returns stats, or None when there is no normal map."""
    me = obj.data
    node, image = normal_map_image(me.materials[0] if me.materials else None)
    if image is None:
        return None
    w, h = image.size
    px = np.zeros(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)                                  # rows run bottom-up, as Blender's UV v does
    me.calc_loop_triangles()
    lt = np.zeros(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("loops", lt)
    lt = lt.reshape(-1, 3)
    lv = np.zeros(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    uv = np.zeros(len(me.loops) * 2)
    me.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2) * [w, h] - 0.5                     # texel centres at integers
    fl = fade[lv]
    amount = np.zeros((h, w))
    covered = np.zeros((h, w), dtype=bool)
    for t in lt[fl[lt].max(1) > 0.0]:
        q = uv[t]
        x0, y0 = np.floor(q.min(0)).astype(int)
        x1, y1 = np.ceil(q.max(0)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, w - 1), min(y1, h - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        g = np.stack([gx.ravel(), gy.ravel()], 1).astype(float)
        a, b, c = q
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((b[1] - c[1]) * (g[:, 0] - c[0]) + (c[0] - b[0]) * (g[:, 1] - c[1])) / den
        l1 = ((c[1] - a[1]) * (g[:, 0] - c[0]) + (a[0] - c[0]) * (g[:, 1] - c[1])) / den
        l2 = 1.0 - l0 - l1
        inside = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
        if not inside.any():
            continue
        val = l0 * fl[t[0]] + l1 * fl[t[1]] + l2 * fl[t[2]]
        yy, xx = gy.ravel()[inside], gx.ravel()[inside]
        np.maximum.at(amount, (yy, xx), val[inside])
        covered[yy, xx] = True
    # Carry the fade outward from the islands into the gutter texels only.
    for _ in range(NM_GUTTER):
        grow = amount.copy()
        pad = np.pad(amount, 1)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                grow = np.maximum(grow, pad[1 + dy:1 + dy + h, 1 + dx:1 + dx + w])
        amount = np.where(covered, amount, grow)
    n = px[..., :3].astype(np.float64) * 2.0 - 1.0
    n = n * (1.0 - amount[..., None]) + np.array([0.0, 0.0, 1.0]) * amount[..., None]
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-12)
    out = px.copy()
    changed = amount > 1e-4
    out[..., :3] = np.where(changed[..., None], (n * 0.5 + 0.5).astype(np.float32), px[..., :3])
    faded = bpy.data.images.new(os.path.splitext(os.path.basename(out_path))[0], w, h, alpha=False)
    faded.colorspace_settings.name = "Non-Color"
    faded.pixels.foreach_set(out.ravel())
    faded.filepath_raw = out_path
    faded.file_format = "PNG"
    faded.save()
    bpy.data.images.remove(faded)
    loaded = bpy.data.images.load(out_path, check_existing=False)
    loaded.colorspace_settings.name = "Non-Color"
    node.image = loaded
    return {"image": repo_path(out_path), "size": [w, h], "texels_changed": int(changed.sum()),
            "texels_over_0.5": int((amount > 0.5).sum()), "texels_flat": int((amount > 0.999).sum()),
            "source_image": image.name}


# --------------------------------------------------------------------------------------------------------------------
# fit mode
# --------------------------------------------------------------------------------------------------------------------

EXPORT_AXES = np.array([[1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, -1.0, 0.0]])  # Blender (Z up) -> glTF (Y up)


def export_frame(p):
    x, y, z = (float(v) for v in p)
    return [round(x, 5), round(z, 5), round(-y, 5)]


def import_mesh(path):
    import addon_utils
    addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.context.scene.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    if len(meshes) != 1:
        raise RuntimeError(f"expected one mesh in {path}, found {len(meshes)}")
    obj = meshes[0]
    if not np.allclose(np.array(obj.matrix_world), np.eye(4), atol=1e-7):
        raise RuntimeError("the LOD0 mesh carries a node transform; this tool expects vertices in place")
    return obj


def mesh_arrays(obj):
    me = obj.data
    n = len(me.vertices)
    P = np.zeros(n * 3)
    me.vertices.foreach_get("co", P)
    P = P.reshape(-1, 3)
    me.calc_loop_triangles()
    tri = np.zeros(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", tri)
    cn = np.zeros(len(me.loops) * 3)
    me.corner_normals.foreach_get("vector", cn)
    lv = np.zeros(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    N = np.zeros((n, 3))
    np.add.at(N, lv, cn.reshape(-1, 3))
    N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
    return P, tri.reshape(-1, 3), N


def add_sockets(rig, J, hands, G, body, Pp):
    """The five SOCK_* attachments, parented to their bones by world matrix (as the contract builder does)."""
    sockets = {}
    lm = body["landmarks"]
    for side, sign in SIDES:
        h = f"hand_{side}"
        palm = Pp[h] + G[h] @ (hands[side]["palm_centre"] - J[h])
        across = G[h] @ (J[f"index_01_{side}"] - J[f"pinky_01_{side}"])
        hand_dir = unit(Pp[f"index_01_{side}"] - Pp[h])
        x = unit(across - hand_dir * np.dot(across, hand_dir))
        normal = G[h] @ hands[side]["palm_normal"]
        y = unit(normal - x * np.dot(normal, x))
        z = np.cross(x, y)
        m = np.eye(4)
        # The glTF exporter converts an object's own axes with the scene (its local +Z becomes glTF +Y, its -Y glTF
        # +Z), so the empty is given the columns (x, -z, y) for the exported socket to carry X=x, Y=y, Z=z.
        m[:3, 0], m[:3, 1], m[:3, 2], m[:3, 3] = x, -z, y, palm
        sockets[f"SOCK_hand_{side}"] = (h, m)
        hb = lm["hip_socket_band"]
        hip = np.eye(4)
        hip[:3, 3] = [0.95 * hb["half_width"] * sign, hb["centre_y"] + 0.07, lm["hip_z"] + 0.04]
        sockets[f"SOCK_attach_hip_{side}"] = ("pelvis", hip)
    back = np.eye(4)
    back[:3, 3] = [0.0, lm["back_surface_y"] + 0.06, lm["chest_z"] + 0.10]
    sockets["SOCK_attach_back"] = ("chest", back)
    bpy.context.view_layer.update()
    objs = []
    for name, parent in ATTACHMENTS:
        bone, m = sockets[name]
        assert bone == parent
        empty = bpy.data.objects.new(name, None)
        empty.empty_display_type = "ARROWS"
        empty.empty_display_size = 0.04
        bpy.context.collection.objects.link(empty)
        empty.parent = rig
        empty.parent_type = "BONE"
        empty.parent_bone = parent
        bpy.context.view_layer.update()
        empty.matrix_world = Matrix([list(r) for r in m])
        empty["socket_role"] = "equipment_attach"
        empty["socket_bone"] = parent
        objs.append(empty)
    return sockets, objs


def repo_path(path):
    """Repository-relative where it can be (a scratch --out-dir on another drive stays absolute)."""
    try:
        return os.path.relpath(path, REPO)
    except ValueError:
        return os.path.abspath(path)


def rounded(v):
    return np.round(np.asarray(v, dtype=float), 4).tolist()


def main_fit(args):
    asset = args.asset_id
    src = args.input or os.path.join(ASSETS, "ready", asset, f"{asset}.glb")
    out_dir = os.path.abspath(args.out_dir or os.path.join(STAGING, asset))  # Blender resolves no path against the cwd
    fam = FIT_FAMILIES[args.fit_family]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj = import_mesh(src)
    P, tri, N = mesh_arrays(obj)
    height = float(P[:, 2].max() - P[:, 2].min())
    if abs(P[:, 2].min()) > 1e-4 or abs(height - fam["height"]) > 0.01:
        raise RuntimeError(f"mesh is not seated at the contract height: min z {P[:, 2].min():.4f}, height {height:.4f}")

    surf = Surface(P, tri)
    Nw = np.zeros_like(surf.V)
    np.add.at(Nw, surf.to_welded, N)
    Nw /= np.maximum(np.linalg.norm(Nw, axis=1, keepdims=True), 1e-12)
    inner, _tree = classify_walls(surf, Nw)
    surf.outer = ~inner

    # The fit assumes the body stands facing the contract's forward. A body built turned needs a rigid whole-mesh
    # yaw first, which changes the asset and is the owner's call: stop and say so rather than fit a turned skeleton.
    turn = facing(surf)
    if any(v is None or abs(v) > 8.0 for v in turn.values()):
        raise RuntimeError(f"the body is not facing the contract's forward (+Z): {turn}; a rigid yaw of the whole "
                           f"mesh is needed before it can be fitted - not applied here")

    arms = {side: fit_arm(surf, sign) for side, sign in SIDES}
    hands = {side: fit_hand(surf, arms[side], sign) for side, sign in SIDES}
    body = fit_body(surf, arms["l"]["arm_mask"] | arms["r"]["arm_mask"])
    J, tips = assemble_joints(arms, hands, body)
    J.update(helper_joints(J))

    # Weight in the mesh's own pose, where every bone lies inside the limb it drives.
    names = [b for b, _p, _d in skeleton_bones()]
    heat_tails = {"head": np.array([J["head"][0], J["head"][1], height - 0.03])}
    for side, _sign in SIDES:
        heat_tails[f"toe_{side}"] = body["legs"][side]["toe_tip"]
    heat_rig = build_rig(f"{asset}_heat", J, tips, for_heat=True, heat_tails=heat_tails)
    W_welded, heat_stats = heat_weights(surf, heat_rig, names, Nw,
                                        refine=lambda W, skin: refine_weights(surf, skin, W, names, J, arms))
    W_welded, heat_stats["side_filter_max_weight_removed"] = side_filter(W_welded, surf.V, names)
    W = limit_weights(W_welded[surf.to_welded])
    bpy.data.objects.remove(heat_rig, do_unlink=True)

    # Re-pose the arms to the contract and take the deformed mesh as the new rest.
    C = {k: np.array(v, dtype=float) for k, v in bone_positions(fam).items()}
    G, Pp, Tp, pose_report = arm_pose(J, tips, C)
    Pp.update(helper_joints(Pp))
    R, t = skin_transforms(G, J, Pp, names)
    P_new, N_new = deform(P, N, W, R, t)
    moved = np.linalg.norm(P_new - P, axis=1) > 1e-9
    P_new[~moved] = P[~moved]
    N_new[~moved] = N[~moved]

    me = obj.data
    me.vertices.foreach_set("co", P_new.ravel())
    me.update()
    me.normals_split_custom_set_from_vertices([tuple(n) for n in N_new])
    os.makedirs(out_dir, exist_ok=True)
    fade, nm_stats = repose_attenuation(P, P_new, tri, W, R)
    nm_stats["texture"] = fade_normal_map(obj, fade, os.path.join(out_dir, f"{asset}_normal.png"))

    rig = build_rig(asset, Pp, Tp, directions=canonical_directions(fam))
    obj.name = asset
    me.name = f"{asset}_mesh"
    for g in list(obj.vertex_groups):
        obj.vertex_groups.remove(g)
    deform_names = [b for b, _p, d in skeleton_bones() if d]
    groups = {n: obj.vertex_groups.new(name=n) for n in deform_names}
    col = {n: i for i, n in enumerate(names)}
    for n in deform_names:
        w = W[:, col[n]]
        for i in np.where(w > 0)[0]:
            groups[n].add([int(i)], float(w[i]), "REPLACE")
    obj.parent = rig
    obj.matrix_parent_inverse = rig.matrix_world.inverted()
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mod.use_vertex_groups = True
    sockets, socket_objs = add_sockets(rig, J, hands, G, body, Pp)

    out_glb = os.path.join(out_dir, f"{asset}_rigged.glb")
    keep = {obj, rig, *socket_objs}
    for o in bpy.context.scene.objects:
        o.select_set(o in keep)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(
        filepath=out_glb, export_format="GLB", use_selection=True,
        # No TANGENT: the exporter splits vertices where MikkTSpace handedness flips (10 here), which breaks the vertex
        # identity with LOD0; the engine's glTF loader generates MikkTSpace tangents from this re-posed rest mesh.
        export_apply=False, export_yup=True, export_normals=True,
        export_materials="EXPORT", export_texcoords=True,
        export_skins=True, export_extras=True, export_animations=False)

    # ---- reports -------------------------------------------------------------------------------------------------
    final_surf = Surface(P_new, tri)
    tree = bvh(final_surf.V, final_surf.F)
    body_bones = skeleton_bones()
    dominated = {n: int((W[:, col[n]] > 0.5).sum()) for n in names}
    joints = []
    for b, parent, d in body_bones:
        p = Pp[b]
        dist = float(final_surf.nearest_distance(p)[0])
        counts = inside_counts(tree, p)
        joints.append({"name": b, "parent": parent, "deform": d, "head": export_frame(p),
                       "nearest_vertex_m": round(dist, 4),
                       "inside_body": is_inside(counts),
                       "ray_crossings": counts,
                       "within_0.06H": dist <= 0.06 * height,
                       "dominated_vertices": dominated[b]})
    lm = body["landmarks"]
    dims = (P_new.max(0) - P_new.min(0)).round(4).tolist()
    core = {b for b, _p in BONES}
    contract = {
        "skeleton_version": SKELETON_VERSION,
        "asset_id": asset,
        "fit_family": args.fit_family,
        "units": "metres",
        "export_frame": "Y-up, base on ground plane, footprint centred",
        "forward_axis": "-Z",
        "up_axis": "+Y",
        "reference_pose": "A-pose, arms 45 degrees down",
        "height_m": round(height, 4),
        "measured_dimensions_m": dims,
        "bone_count": len(body_bones),
        "core_bone_count": len(core),
        "deform_bone_count": sum(1 for _b, _p, d in body_bones if d),
        "bones": [{"name": b, "parent": p, "head": export_frame(Pp[b]), "deform": d,
                   "role": "core" if b in core else "finger" if b.split("_")[0] in DIGITS else "ik"}
                  for b, p, d in body_bones],
        "attachments": [{"name": s, "parent_bone": p, "position": export_frame(sockets[s][1][:3, 3]),
                         "role": "equipment_attach"} for s, p in ATTACHMENTS],
        "landmarks_m": {
            "height": round(height, 4),
            "shoulder_z": round(float((Pp["upperarm_l"][2] + Pp["upperarm_r"][2]) / 2), 4),
            "chest_z": round(lm["chest_z"], 4), "waist_z": round(lm["waist_z"], 4), "hip_z": round(lm["hip_z"], 4),
            "knee_z": round(float(Pp["calf_l"][2]), 4), "ankle_z": round(float(Pp["foot_l"][2]), 4),
            "neck_z": round(lm["neck_z"], 4),
            "upperarm": round(float(np.linalg.norm(Pp["lowerarm_l"] - Pp["upperarm_l"])), 4),
            "lowerarm": round(float(np.linalg.norm(Pp["hand_l"] - Pp["lowerarm_l"])), 4),
            "thigh": round(float(np.linalg.norm(Pp["calf_l"] - Pp["thigh_l"])), 4),
            "calf": round(float(np.linalg.norm(Pp["foot_l"] - Pp["calf_l"])), 4),
            "chest_w": round(lm["chest_w"], 4), "chest_d": round(lm["chest_d"], 4),
            "waist_w": round(lm["waist_w"], 4), "hip_w": round(lm["hip_w"], 4),
        },
        "landmarks_source": "measured on the mesh by _blender_rig_fit_player.py (the fit family's declared "
                            "constants are in _blender_canonical_body.FIT_FAMILIES)",
        "grip_frame": {
            "sockets": ["SOCK_hand_l", "SOCK_hand_r"],
            "origin": "palm centre: the hand's cross-section centre 60% of the way from the wrist to the knuckle line",
            "+X": "primary - across the palm from the little-finger knuckle toward the index knuckle, square to the "
                  "hand: the way a gripped handle's head points out of a closed fist (HeldWeapon: SOCK_grip_primary +X)",
            "+Y": "secondary - out of the palm (the side the fingers close toward), square to +X",
            "+Z": "X cross Y",
        },
        "skinned": True,
    }
    skeleton_path = os.path.join(out_dir, f"{asset}_rigged_skeleton.json")
    with open(skeleton_path, "w", encoding="utf-8") as handle:
        json.dump(contract, handle, indent=2)

    report = {
        "asset_id": asset,
        "tool": "tools/asset_pipeline/_blender_rig_fit_player.py",
        "input": repo_path(src),
        "output": repo_path(out_glb),
        "method": "fitted to the mesh as it stands, bone-heat weighted on the welded outer skin, arm chains "
                  "re-posed to the contracted 45-degree A-pose and applied as rest",
        "mesh": {"vertices": int(len(P)), "welded_vertices": int(len(surf.V)), "triangles": int(len(tri)),
                 "components": int(surf.comp.max() + 1), "inner_wall_vertices": int(inner.sum()),
                 "note": "double-walled shell: an outer skin and an inner wall ~5 mm inside it"},
        "height_m": round(height, 4), "ground_min_z_m": round(float(P_new[:, 2].min()), 6),
        "facing_deg": {k: round(v, 2) for k, v in turn.items()},
        "landmarks": {k: (round(v, 4) if isinstance(v, float) else v) for k, v in lm.items()},
        "arms": {side: {"shoulder": rounded(arms[side]["shoulder"]), "elbow": rounded(arms[side]["elbow"]),
                        "wrist": rounded(arms[side]["wrist"]), "elbow_rule": arms[side]["elbow_rule"],
                        "elbow_bend_deg": round(arms[side]["elbow_bend_deg"], 2),
                        "shoulder_inscribed_radius_m": round(arms[side]["shoulder_depth"], 4),
                        "wrist_section_m": rounded(arms[side]["wrist_section_m"])} for side, _s in SIDES},
        "hands": {side: {"digits": {k: {"knuckle": rounded(d["knuckle"]), "middle": rounded(d["middle"]),
                                        "tip": rounded(d["tip"]), "length_m": round(d["length"], 4),
                                        "radius_m": round(d["radius"], 4), "knuckle_rule": d["knuckle_rule"]}
                                    for k, d in hands[side]["digits"].items()},
                         "palm_centre": rounded(hands[side]["palm_centre"]),
                         "palm_normal": rounded(hands[side]["palm_normal"])}
                  for side, _s in SIDES},
        "note_frames": "arms/hands/landmarks above are Blender frame (Z up, -Y forward) in the mesh's own pose; "
                       "joints below are the export frame (glTF Y up, +Z forward) after the re-pose",
        "repose": pose_report,
        "repose_rotations_export_frame": {
            b: np.round(EXPORT_AXES @ G[b] @ EXPORT_AXES.T, 8).tolist()
            for b in names if not np.allclose(G[b], np.eye(3))},
        "mesh_pose_joints_export_frame": {b: [round(float(v), 6) for v in (EXPORT_AXES @ J[b])] for b in names},
        "repose_moved_vertices": int(moved.sum()),
        "unmoved_vertices_bit_identical_to_lod0": int((~moved).sum()),
        "weights": {"method": "Blender bone heat (ARMATURE_AUTO) on the welded outer skin; upper arms capped to the "
                              "arm and a shoulder-cap band, thighs to below the femoral heads (refine_weights), the "
                              "changed region smoothed; inner wall takes the skin weights straight through the wall "
                              "(barycentric), separate shells (boots) the nearest skin vertex's; limb weights faded "
                              "out across the midline (+-2.5 cm); 4 influences, floor 0.01",
                    **heat_stats},
        "joints": joints,
        "dod": {
            "max_joint_distance_m": round(max(j["nearest_vertex_m"] for j in joints if j["deform"]), 4),
            "limit_m": round(0.06 * height, 4),
            "deform_joints_outside_limit": [j["name"] for j in joints if j["deform"] and not j["within_0.06H"]],
            "deform_joints_not_inside_body": [j["name"] for j in joints if j["deform"] and not j["inside_body"]],
            "limb_bones_without_dominated_vertices": [
                b for b in names if (b.startswith(("clavicle", "upperarm", "lowerarm", "hand", "thigh", "calf",
                                                   "foot", "toe", "neck", "head")) or b.split("_")[0] in DIGITS)
                and dominated[b] == 0],
        },
        "sockets": {s: {"bone": sockets[s][0], "position": export_frame(sockets[s][1][:3, 3])}
                    for s, _p in ATTACHMENTS},
        "normal_map": {"method": "baked tangent-space normal map faded toward flat by the re-pose's relative "
                                 "motion against each region's bake-time neighbourhood and by its stretch "
                                 "(repose_attenuation); no TANGENT written: the loader generates MikkTSpace "
                                 "tangents on the re-posed rest mesh (exporting them splits vertices and breaks "
                                 "the vertex identity with LOD0)",
                       **nm_stats},
    }
    report_path = os.path.join(out_dir, f"{asset}_rig_fit_report.json")
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
    print("FIT_RESULT " + json.dumps({"out": out_glb, "skeleton": skeleton_path, "report": report_path,
                                      "dod": report["dod"], "repose": pose_report, "weights": heat_stats}))
    return 0


# --------------------------------------------------------------------------------------------------------------------
# render mode: the engine's playback, emulated from the files, then drawn
# --------------------------------------------------------------------------------------------------------------------

VERIFY_CLIPS = [
    "humanoid.locomotion.idle", "humanoid.locomotion.walk_forward", "humanoid.sword.attack_01",
    "humanoid.bow.draw_release_01", "humanoid.polearm.thrust_01", "humanoid.magic.cast_01",
]
# Where an independent check of the first staged rig found the armpit fold (an arm at or above horizontal) and the
# hip ballooning (deep hip flexion); `render` draws both armpits and the hips there under a low sun with shadows,
# because a fold only shows as a jagged cast shadow.
CHECK_FRAMES = [
    ("humanoid.locomotion.sprint_forward", 0.167), ("humanoid.locomotion.sprint_forward", 0.100),
    ("humanoid.sword.attack_01", 0.400), ("humanoid.sword.block_01", 0.167), ("humanoid.general.pickup_01", 1.067),
    ("humanoid.locomotion.run_forward", 0.133),
]


def read_glb(path):
    import struct
    with open(path, "rb") as handle:
        data = handle.read()
    _magic, _version, length = struct.unpack_from("<III", data, 0)
    off, js, binary = 12, None, b""
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == 0x4E4F534A:
            js = json.loads(chunk)
        elif ctype == 0x004E4942:
            binary = chunk
    return js, binary


def accessor(js, binary, index):
    kinds = {5126: np.float32, 5123: np.uint16, 5121: np.uint8, 5125: np.uint32, 5122: np.int16, 5120: np.int8}
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
    a = js["accessors"][index]
    view = js["bufferViews"][a["bufferView"]]
    dtype = np.dtype(kinds[a["componentType"]])
    comps = width[a["type"]]
    off = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = view.get("byteStride", 0)
    item = dtype.itemsize * comps
    if stride and stride != item:
        raw = np.frombuffer(binary, np.uint8, stride * a["count"], off).reshape(a["count"], stride)[:, :item]
        out = np.frombuffer(raw.tobytes(), dtype).reshape(a["count"], comps)
    else:
        out = np.frombuffer(binary, dtype, a["count"] * comps, off).reshape(a["count"], comps)
    if a.get("normalized"):
        out = out.astype(np.float64) / np.iinfo(dtype).max
    return out


def q_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return np.array([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz])


def q_inv(q):
    return np.array([-q[0], -q[1], -q[2], q[3]]) / float(np.dot(q, q))


def q_mat(q):
    x, y, z, w = unit(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def q_slerp(a, b, t):
    a, b = unit(a), unit(b)
    d = float(np.dot(a, b))
    if d < 0:
        b, d = -b, -d
    if d > 0.9995:
        return unit(a + (b - a) * t)
    th = math.acos(d)
    return (a * math.sin((1 - t) * th) + b * math.sin(t * th)) / math.sin(th)


def trs(t, q, s):
    m = np.eye(4)
    m[:3, :3] = q_mat(q) * np.asarray(s)
    m[:3, 3] = t
    return m


def node_trs(node):
    return (np.array(node.get("translation", [0.0, 0.0, 0.0]), dtype=float),
            np.array(node.get("rotation", [0.0, 0.0, 0.0, 1.0]), dtype=float),
            np.array(node.get("scale", [1.0, 1.0, 1.0]), dtype=float))


class GltfRig:
    """A skinned GLB read the way the engine reads it: joint rests from the nodes, bind poses from the skin."""

    def __init__(self, path):
        js, b = read_glb(path)
        self.js = js
        nodes = js["nodes"]
        skin = js["skins"][0]
        self.joint_nodes = skin["joints"]
        self.names = [nodes[j]["name"] for j in self.joint_nodes]
        self.index = {n: i for i, n in enumerate(self.names)}
        node_parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        pos = {j: i for i, j in enumerate(self.joint_nodes)}
        self.parent = [pos.get(node_parent.get(j), -1) for j in self.joint_nodes]
        self.rest = [node_trs(nodes[j]) for j in self.joint_nodes]
        self.ibm = accessor(js, b, skin["inverseBindMatrices"]).astype(np.float64).reshape(-1, 4, 4).transpose(0, 2, 1)
        mesh_node = next(n for n in nodes if "mesh" in n and "skin" in n)
        prim = js["meshes"][mesh_node["mesh"]]["primitives"][0]
        at = prim["attributes"]
        self.P = accessor(js, b, at["POSITION"]).astype(np.float64)
        self.N = accessor(js, b, at["NORMAL"]).astype(np.float64)
        self.UV = accessor(js, b, at["TEXCOORD_0"]).astype(np.float64) if "TEXCOORD_0" in at else None
        self.JOINTS = accessor(js, b, at["JOINTS_0"]).astype(np.int64)
        w = accessor(js, b, at["WEIGHTS_0"]).astype(np.float64)
        self.WEIGHTS = w / np.maximum(w.sum(1, keepdims=True), 1e-12)
        self.tri = accessor(js, b, prim["indices"]).astype(np.int64).reshape(-1, 3)
        self.order = self.topological()
        self.sockets = {}
        for i, n in enumerate(nodes):
            if n.get("name", "").startswith("SOCK_") and node_parent.get(i) in pos:
                self.sockets[n["name"]] = (pos[node_parent[i]], trs(*node_trs(n)))
        rest_world = self.world([trs(*r) for r in self.rest])
        self.rest_world = rest_world

    def topological(self):
        order, seen = [], set()

        def visit(i):
            if i in seen:
                return
            if self.parent[i] >= 0:
                visit(self.parent[i])
            seen.add(i)
            order.append(i)
        for i in range(len(self.names)):
            visit(i)
        return order

    def world(self, local):
        out = [None] * len(local)
        for i in self.order:
            out[i] = local[i] if self.parent[i] < 0 else out[self.parent[i]] @ local[i]
        return np.array(out)

    def height_of(self, name="pelvis"):
        return float(self.rest_world[self.index[name]][1, 3])

    def skin(self, world):
        M = world @ self.ibm
        P = np.zeros_like(self.P)
        N = np.zeros_like(self.N)
        Ph = np.c_[self.P, np.ones(len(self.P))]
        for k in range(4):
            j = self.JOINTS[:, k]
            w = self.WEIGHTS[:, k:k + 1]
            m = M[j]
            P += w * np.einsum("nij,nj->ni", m[:, :3, :], Ph)
            N += w * np.einsum("nij,nj->ni", m[:, :3, :3], self.N)
        N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)
        return P, N


class GltfClip:
    def __init__(self, path):
        js, b = read_glb(path)
        nodes = js["nodes"]
        anim = js["animations"][0]
        self.channels = {}
        self.duration = 0.0
        for ch in anim["channels"]:
            sampler = anim["samplers"][ch["sampler"]]
            times = accessor(js, b, sampler["input"]).astype(np.float64).ravel()
            values = accessor(js, b, sampler["output"]).astype(np.float64)
            if sampler.get("interpolation", "LINEAR") == "CUBICSPLINE":
                values = values.reshape(len(times), 3, -1)[:, 1]
            name = nodes[ch["target"]["node"]]["name"]
            self.channels.setdefault(name, {})[ch["target"]["path"]] = (
                times, values, sampler.get("interpolation", "LINEAR"))
            self.duration = max(self.duration, float(times[-1]))
        self.rest = {n["name"]: node_trs(n) for n in nodes if "name" in n}
        skin = js["skins"][0]
        joint_names = [nodes[j]["name"] for j in skin["joints"]]
        node_parent = {c: i for i, n in enumerate(nodes) for c in n.get("children", [])}
        # The source skeleton's hip height, as the engine reads it (global rest of the pelvis).
        chain, j = [], nodes.index(next(n for n in nodes if n.get("name") == "pelvis"))
        while j is not None and nodes[j].get("name") in joint_names:
            chain.append(j)
            j = node_parent.get(j)
        m = np.eye(4)
        for j in reversed(chain):
            m = m @ trs(*node_trs(nodes[j]))
        self.hip_height = float(m[1, 3])

    def sample(self, name, path, t):
        times, values, interp = self.channels[name][path]
        if t <= times[0]:
            return values[0].copy()
        if t >= times[-1]:
            return values[-1].copy()
        k = int(np.searchsorted(times, t)) - 1
        if interp == "STEP":
            return values[k].copy()
        f = (t - times[k]) / (times[k + 1] - times[k])
        if path == "rotation":
            return q_slerp(values[k], values[k + 1], f)
        return values[k] + (values[k + 1] - values[k]) * f


def engine_pose(rig, clip, t, mode):
    """Local bone transforms as the engine plays `clip` on `rig` (ArtLibrary.Retarget + AnimationPlayer).

    absolute - the committed Retarget: every track's key is the bone's pose as authored (translations included).
    relative - the working-tree Retarget: keys carried relative to rest (target rest + motion away from the
               source's rest; rotations as to_rest * from_rest^-1 * key; root and pelvis travel scaled by hip height).
    """
    scale = rig.height_of() / clip.hip_height if clip.hip_height > 0.01 else 1.0
    local = []
    for i, name in enumerate(rig.names):
        rt, rq, rs = rig.rest[i]
        t_, q_, s_ = rt.copy(), rq.copy(), rs.copy()
        ch = clip.channels.get(name, {})
        src = clip.rest.get(name)
        if "translation" in ch:
            key = clip.sample(name, "translation", t)
            if mode == "absolute" or src is None:
                t_ = key
            else:
                travels = rig.parent[i] < 0 or name in ("hips", "pelvis")
                moved = key - src[0]
                t_ = rt + (moved * scale if travels else moved)
        if "rotation" in ch:
            key = clip.sample(name, "rotation", t)
            q_ = key if (mode == "absolute" or src is None) else q_mul(q_mul(rq, q_inv(src[1])), key)
        if "scale" in ch:
            s_ = clip.sample(name, "scale", t)
        local.append(trs(t_, q_, s_))
    return rig.world(local)


def to_blender(p):
    p = np.asarray(p)
    return np.stack([p[..., 0], -p[..., 2], p[..., 1]], axis=-1)


class Stage:
    """A lit stage with a ground plane at 0 and the rig's own material on a mesh we pose ourselves."""

    def __init__(self, rig_path, size):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        scene = bpy.context.scene
        scene.render.engine = "BLENDER_EEVEE"
        scene.render.resolution_x = size
        scene.render.resolution_y = size
        scene.render.image_settings.file_format = "PNG"
        scene.view_settings.view_transform = "Standard"
        world = bpy.data.worlds.new("stage")
        scene.world = world
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.42, 0.45, 0.48, 1.0)
        world.node_tree.nodes["Background"].inputs[1].default_value = 0.8
        import addon_utils
        addon_utils.enable("io_scene_gltf2", default_set=True, persistent=True)
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=rig_path)
        imported = [o for o in bpy.data.objects if o not in before]
        mats = [m for o in imported if o.type == "MESH" for m in o.data.materials if m]
        self.material = mats[0] if mats else None
        for o in imported:
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.ops.mesh.primitive_plane_add(size=14.0, location=(0.0, 0.0, 0.0))
        ground = bpy.context.active_object
        ground.name = "ground"
        gm = bpy.data.materials.new("ground")
        gm.use_nodes = True
        bsdf = gm.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.24, 0.25, 0.24, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
        ground.data.materials.append(gm)
        for name, loc, energy in (("key", (2.6, -3.0, 4.2), 380.0), ("fill", (-3.4, -1.4, 2.0), 110.0),
                                  ("rim", (-1.2, 3.4, 3.2), 220.0)):
            ld = bpy.data.lights.new(name, "AREA")
            ld.energy = energy
            ld.size = 3.0
            lo = bpy.data.objects.new(name, ld)
            lo.location = loc
            lo.rotation_euler = (Vector((0, 0, 1.0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
            scene.collection.objects.link(lo)
        cam_data = bpy.data.cameras.new("cam")
        self.camera = bpy.data.objects.new("cam", cam_data)
        scene.collection.objects.link(self.camera)
        scene.camera = self.camera
        self.scene = scene
        self.bodies = {}
        self.body = None
        self.markers = []
        self.plain = bpy.data.materials.new("reference_body")
        self.plain.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.62, 0.55, 0.45, 1.0)

    def set_body(self, P, N, tri, UV, name="body"):
        """(Re)build a posed body from glTF-frame arrays and show only it."""
        Pb, Nb = to_blender(P), to_blender(N)
        if name not in self.bodies:
            me = bpy.data.meshes.new(name)
            me.from_pydata(Pb.tolist(), [], tri.tolist())
            if UV is not None:
                uv = me.uv_layers.new(name="UVMap")
                loops = np.zeros(len(me.loops), dtype=np.int64)
                me.loops.foreach_get("vertex_index", loops)
                coords = np.c_[UV[loops, 0], 1.0 - UV[loops, 1]]
                uv.data.foreach_set("uv", coords.ravel())
            for poly in me.polygons:
                poly.use_smooth = True
            me.materials.append(self.material if (UV is not None and self.material is not None) else self.plain)
            obj = bpy.data.objects.new(name, me)
            self.scene.collection.objects.link(obj)
            self.bodies[name] = obj
        for other, obj in self.bodies.items():
            obj.hide_render = other != name
        self.body = self.bodies[name]
        me = self.body.data
        me.vertices.foreach_set("co", Pb.ravel())
        me.update()
        me.normals_split_custom_set_from_vertices([tuple(n) for n in Nb])

    def aim(self, target, azimuth, distance, lens=50.0, elevation=8.0):
        target = Vector(tuple(map(float, target)))
        az, el = math.radians(azimuth), math.radians(elevation)
        # Azimuth 0 looks at the character's front (the character faces -Y in Blender).
        offset = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * distance
        self.camera.location = target + offset
        self.camera.rotation_euler = (-offset).to_track_quat("-Z", "Y").to_euler()
        self.camera.data.lens = lens

    def render(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        return path

    def set_markers(self, groups):
        """Emissive spheres: [(points, rgb, radius), ...]. Joints red, sockets green in the x-ray views."""
        for m in self.markers:
            bpy.data.objects.remove(m, do_unlink=True)
        self.markers = []
        for k, (points, color, radius) in enumerate(groups):
            mat = bpy.data.materials.get(f"marker{k}") or bpy.data.materials.new(f"marker{k}")
            nodes = mat.node_tree.nodes
            nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
            nodes["Principled BSDF"].inputs["Emission Color"].default_value = (*color, 1.0)
            nodes["Principled BSDF"].inputs["Emission Strength"].default_value = 3.0
            for p in points:
                bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=tuple(map(float, p)), segments=12,
                                                     ring_count=6)
                o = bpy.context.active_object
                o.data.materials.append(mat)
                self.markers.append(o)

    def xray(self, on):
        if self.body is None:
            return
        if on:
            mat = bpy.data.materials.get("xray") or bpy.data.materials.new("xray")
            mat.use_nodes = True
            bsdf = mat.node_tree.nodes["Principled BSDF"]
            bsdf.inputs["Base Color"].default_value = (0.55, 0.6, 0.7, 1.0)
            bsdf.inputs["Alpha"].default_value = 0.12
            try:
                mat.surface_render_method = "BLENDED"
            except AttributeError:
                mat.blend_method = "BLEND"
            self._restore = self.body.data.materials[0]
            self.body.data.materials[0] = mat
        elif getattr(self, "_restore", None) is not None:
            self.body.data.materials[0] = self._restore
            self._restore = None


def contact_sheet(paths, cols, out_path, labels=None):
    """Tile rendered frames into one image (numpy over Blender's image loader)."""
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p, check_existing=False)
        w, h = im.size
        px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
        px = px[: h // 2 * 2, : w // 2 * 2].reshape(h // 2, 2, w // 2, 2, 4).mean(axis=(1, 3))  # half size
        imgs.append(px)
        bpy.data.images.remove(im)
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.ones((rows * h, cols * w, 4), dtype=np.float32)
    for k, px in enumerate(imgs):
        r, c = divmod(k, cols)
        # Blender image rows run bottom-up: the first row of tiles goes at the top.
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = px
    img = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    img.pixels.foreach_set(sheet.ravel())
    img.filepath_raw = out_path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    return out_path


def main_render(args):
    asset = args.asset_id
    out_dir = os.path.abspath(args.out_dir or os.path.join(STAGING, asset))  # Blender resolves no path against the cwd
    rig_path = os.path.join(out_dir, f"{asset}_rigged.glb")
    lod0_path = args.input or os.path.join(ASSETS, "ready", asset, f"{asset}.glb")
    render_dir = os.path.join(out_dir, "renders")
    # Renders are regenerated whole; only this tool's own staging folder is ever cleared.
    staging = os.path.normcase(os.path.abspath(STAGING))
    if os.path.isdir(render_dir):
        target = os.path.normcase(os.path.abspath(render_dir))
        if target.startswith(staging + os.sep) and os.path.basename(target) == "renders":
            import shutil
            shutil.rmtree(render_dir)
    rig = GltfRig(rig_path)
    stage = Stage(rig_path, args.size)
    metrics = {"asset_id": asset, "rig": repo_path(rig_path), "clips": {}}

    # ---- bind pose against LOD0 --------------------------------------------------------------------------------
    js, b = read_glb(lod0_path)
    lod_prim = js["meshes"][0]["primitives"][0]
    lod_P = accessor(js, b, lod_prim["attributes"]["POSITION"]).astype(np.float64)
    same_order = len(lod_P) == len(rig.P)
    moved = np.linalg.norm(rig.P - lod_P, axis=1) > 1e-6 if same_order else None
    P_rest, N_rest = rig.skin(rig.rest_world)
    bind_err = float(np.abs(P_rest - rig.P).max())
    metrics["bind"] = {
        "skin_at_rest_vs_file_max_m": bind_err,
        "vertices": len(rig.P), "lod0_vertices": len(lod_P),
        "vertices_moved_from_lod0": int(moved.sum()) if same_order else None,
        "unmoved_max_deviation_m": float(np.abs(rig.P[~moved] - lod_P[~moved]).max()) if same_order else None,
        "bones_dominating_moved_vertices": sorted({rig.names[j] for j in rig.JOINTS[moved][
            rig.WEIGHTS[moved] > 0.5]}) if same_order else None,
    }
    if not same_order:
        raise RuntimeError("the rigged mesh no longer has LOD0's vertex order; the bind comparison needs it")
    lod_N = accessor(js, b, lod_prim["attributes"]["NORMAL"]).astype(np.float64)
    bind_paths = []
    for label, P_, N_ in (("lod0", lod_P, lod_N), ("rig_rest", P_rest, N_rest)):
        stage.set_body(P_, N_, rig.tri, rig.UV)
        for view, az in (("front", 0.0), ("side", 90.0), ("three_quarter", 35.0)):
            stage.aim((0, 0, 0.95), az, 3.3, lens=50.0)
            bind_paths.append(stage.render(os.path.join(render_dir, "bind", f"{label}_{view}.png")))
    deform_idx = [i for i, n in enumerate(rig.names) if not n.startswith("IK_")]

    def marker_groups(world):
        socks = [to_blender((world[j] @ local)[:3, 3]) for j, local in rig.sockets.values()]
        return [(to_blender(world[deform_idx, :3, 3]), (1.0, 0.12, 0.05), 0.013), (socks, (0.1, 1.0, 0.2), 0.016)]

    stage.set_body(P_rest, N_rest, rig.tri, rig.UV)
    stage.xray(True)
    stage.set_markers(marker_groups(rig.rest_world))
    for view, az in (("front", 0.0), ("side", 90.0)):
        stage.aim((0, 0, 0.95), az, 3.3, lens=50.0)
        bind_paths.append(stage.render(os.path.join(render_dir, "bind", f"rig_rest_xray_{view}.png")))
    for side in ("l", "r"):
        wrist = to_blender(rig.rest_world[rig.index[f"hand_{side}"]][:3, 3])
        stage.aim(wrist, 0.0, 0.7, lens=60.0, elevation=5.0)
        bind_paths.append(stage.render(os.path.join(render_dir, "bind", f"rig_rest_xray_hand_{side}.png")))
    stage.set_markers([])
    stage.xray(False)

    # Undo the arm re-pose through the skin itself: if the mesh is unchanged apart from the pose, the rig driven back
    # to the mesh's own arm pose reproduces LOD0.
    report_path = os.path.join(out_dir, f"{asset}_rig_fit_report.json")
    if os.path.exists(report_path):
        with open(report_path, encoding="utf-8") as handle:
            fit = json.load(handle)
        rot, orig = fit["repose_rotations_export_frame"], fit["mesh_pose_joints_export_frame"]
        back = []
        for i, name in enumerate(rig.names):
            rw = rig.rest_world[i]
            m = np.eye(4)
            if name in rot:
                inv = np.array(rot[name]).T
                m[:3, :3] = inv
                m[:3, 3] = np.array(orig[name]) - inv @ rw[:3, 3]
            back.append(m @ rw)
        P_back, N_back = rig.skin(np.array(back))
        dev = np.linalg.norm(P_back - lod_P, axis=1)
        metrics["restore_to_lod0"] = {
            "what": "rig posed back to the mesh's own arm pose (inverse of the re-pose) vs LOD0 vertex positions",
            "max_m": round(float(dev.max()), 5), "p99_m": round(float(np.percentile(dev, 99)), 5),
            "p999_m": round(float(np.percentile(dev, 99.9)), 5),
            "vertices_over_1mm": int((dev > 0.001).sum()), "vertices_over_5mm": int((dev > 0.005).sum()),
            "note": "linear blend skinning is not exactly invertible where a vertex blends two re-posed bones; "
                    "vertices that took no arm weight are bit-identical to LOD0",
        }
        stage.set_body(P_back, N_back, rig.tri, rig.UV)
        for view, az in (("front", 0.0), ("side", 90.0), ("three_quarter", 35.0)):
            stage.aim((0, 0, 0.95), az, 3.3, lens=50.0)
            bind_paths.append(stage.render(os.path.join(render_dir, "bind", f"rig_posed_back_to_lod0_{view}.png")))
    contact_sheet(bind_paths, 3, os.path.join(render_dir, "bind_sheet.png"))

    # ---- clips --------------------------------------------------------------------------------------------------
    reference_path = os.path.join(ASSETS, "rigs", "humanoid_standard", "humanoid_standard_body.glb")
    reference = GltfRig(reference_path) if os.path.exists(reference_path) else None
    clips = args.clips or VERIFY_CLIPS
    hands = [rig.index["hand_l"], rig.index["hand_r"]]
    elbows = [rig.index["lowerarm_l"], rig.index["lowerarm_r"]]
    edges = np.unique(np.sort(np.concatenate([rig.tri[:, [0, 1]], rig.tri[:, [1, 2]], rig.tri[:, [2, 0]]]), 1),
                      axis=0)
    rest_len = np.linalg.norm(rig.P[edges[:, 0]] - rig.P[edges[:, 1]], axis=1)
    key = np.round(rig.P / 1e-5).astype(np.int64)
    _u, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.ravel()
    welded_tri = inverse[rig.tri]
    arm_bones = [(f"{a}_{s}", f"{b}_{s}") for s in ("l", "r")
                 for a, b in (("upperarm", "lowerarm"), ("lowerarm", "hand"), ("hand", "index_01"))]
    # What the committed (absolute) retarget does to this rig: every clip key carries the source skeleton's bone
    # offsets, so each bone is moved to where the canonical skeleton has it relative to its parent.
    first_clip = GltfClip(os.path.join(ASSETS, "animation", "ready", clips[0].split(".")[0], f"anim.{clips[0]}.glb"))
    mismatch = {}
    for i, name in enumerate(rig.names):
        if name in first_clip.rest and not name.startswith("IK_"):
            mismatch[name] = round(float(np.linalg.norm(rig.rest[i][0] - first_clip.rest[name][0])), 4)
    metrics["bone_offset_vs_clip_rest_m"] = dict(sorted(mismatch.items(), key=lambda kv: -kv[1]))
    for mode in args.retarget:
        for clip_id in clips:
            family = clip_id.split(".")[0]
            path = os.path.join(ASSETS, "animation", "ready", family, f"anim.{clip_id}.glb")
            clip = GltfClip(path)
            record = os.path.join(ASSETS, "animation", "clips", f"anim.{clip_id}.json")
            events = []
            if os.path.exists(record):
                events = [e["time"] for e in json.load(open(record, encoding="utf-8")).get("events", [])]
            cand = sorted({round(x, 3) for x in [0.0, *events, 0.5 * clip.duration, 0.75 * clip.duration]
                           if 0.0 <= x <= clip.duration})
            pick = np.unique(np.round(np.linspace(0, len(cand) - 1, min(4, len(cand)))).astype(int))
            times = [cand[i] for i in pick]
            shots, rows = [], []
            for t in times:
                world = engine_pose(rig, clip, t, mode)
                P_, N_ = rig.skin(world)
                ratio = np.linalg.norm(P_[edges[:, 0]] - P_[edges[:, 1]], axis=1) / np.maximum(rest_len, 1e-9)
                tree = bvh(to_blender(P_[first]), welded_tri)
                sock = {}
                for s in ("SOCK_hand_l", "SOCK_hand_r"):
                    j, local = rig.sockets[s]
                    sock[s] = is_inside(inside_counts(tree, to_blender((world[j] @ local)[:3, 3])))
                row = {"time_s": t, "edge_stretch_max": round(float(ratio.max()), 3),
                       "edge_stretch_p999": round(float(np.percentile(ratio, 99.9)), 3),
                       "edge_compress_min": round(float(ratio.min()), 3),
                       "lowest_point_m": round(float(P_[:, 1].min()), 4),
                       "hand_sockets_inside_hand": sock,
                       "elbows_inside_arm": {rig.names[j]: is_inside(inside_counts(
                           tree, to_blender(world[j][:3, 3]))) for j in elbows},
                       "wrists_inside_arm": {rig.names[j]: is_inside(inside_counts(
                           tree, to_blender(world[j][:3, 3]))) for j in hands}}
                if reference is not None:
                    ref_world = engine_pose(reference, clip, t, "absolute")
                    err = {}
                    for a, b in arm_bones:
                        mine = world[rig.index[b]][:3, 3] - world[rig.index[a]][:3, 3]
                        theirs = ref_world[reference.index[b]][:3, 3] - ref_world[reference.index[a]][:3, 3]
                        err[f"{a}"] = round(angle_deg(mine, theirs), 2)
                    row["arm_direction_vs_canonical_deg"] = err
                rows.append(row)
                stage.set_body(P_, N_, rig.tri, rig.UV)
                tag = f"{clip_id.split('.', 1)[1]}_t{int(round(t * 1000)):04d}ms"
                base = os.path.join(render_dir, mode, clip_id.split(".", 1)[1])
                for view, az in (("front", 25.0), ("side", 100.0)):
                    stage.aim((0, 0, 0.95), az, 3.3, lens=50.0)
                    shots.append(stage.render(os.path.join(base, f"{tag}_{view}.png")))
                for side, az in (("r", 35.0), ("l", -35.0)):
                    j, local = rig.sockets[f"SOCK_hand_{side}"]
                    stage.aim(to_blender((world[j] @ local)[:3, 3]), az, 0.75, lens=60.0, elevation=12.0)
                    shots.append(stage.render(os.path.join(base, f"{tag}_hand_{side}.png")))
                stage.xray(True)
                stage.set_markers(marker_groups(world))
                stage.aim((0, 0, 0.95), 25.0, 3.3, lens=50.0)
                shots.append(stage.render(os.path.join(base, f"{tag}_xray.png")))
                stage.set_markers([])
                stage.xray(False)
                if reference is not None:
                    rP, rN = reference.skin(engine_pose(reference, clip, t, "absolute"))
                    stage.set_body(rP, rN, reference.tri, None, name="canonical_reference")
                    stage.aim((0, 0, 0.95), 25.0, 3.3, lens=50.0)
                    shots.append(stage.render(os.path.join(base, f"{tag}_canonical_reference.png")))
            contact_sheet(shots, 6 if reference is not None else 5,
                          os.path.join(render_dir, f"sheet_{mode}_{clip_id.split('.', 1)[1]}.png"))
            metrics["clips"].setdefault(mode, {})[clip_id] = {"duration_s": round(clip.duration, 3), "frames": rows}

    # ---- close-ups at CHECK_FRAMES (rest-relative retarget), lit by a low sun and a fill that cast shadows --------
    lamps = [o for o in stage.scene.objects if o.type == "LIGHT"]
    for o in lamps:
        o.hide_render = True
    added = []
    for name, energy, rot in (("check_sun", 3.0, (50.0, 10.0, -30.0)), ("check_fill", 1.0, (60.0, 0.0, 150.0))):
        lamp = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        lamp.data.energy = energy
        lamp.rotation_euler = tuple(math.radians(v) for v in rot)
        stage.scene.collection.objects.link(lamp)
        added.append(lamp)
    clip_start = stage.camera.data.clip_start
    stage.camera.data.clip_start = 0.01
    close, close_rows = [], {}
    views = (("armpit_r", "upperarm_r", -80.0, 0.6), ("armpit_l", "upperarm_l", 80.0, 0.6),
             ("hip_r", "pelvis", -80.0, 0.8), ("hip_back", "pelvis", 160.0, 0.8))
    pelvis = rig.index["pelvis"]
    for clip_id, t in CHECK_FRAMES:
        clip = GltfClip(os.path.join(ASSETS, "animation", "ready", clip_id.split(".")[0], f"anim.{clip_id}.glb"))
        world = engine_pose(rig, clip, t, "relative")
        P_, N_ = rig.skin(world)
        close_rows[f"{clip_id}@{t}"] = {"self_intersecting_faces": int(len(self_intersections(
            bvh(to_blender(P_[first]), welded_tri), welded_tri)))}
        stage.set_body(P_, N_, rig.tri, rig.UV)
        # Views are taken about the body as it faces in this frame (a clip may turn the root).
        facing = to_blender(world[pelvis][:3, :3] @ rig.rest_world[pelvis][:3, :3].T @ np.array([0.0, 0.0, 1.0]))
        turn = math.degrees(math.atan2(facing[0], -facing[1]))
        tag = f"{clip_id.split('.', 1)[1]}_t{int(round(t * 1000)):04d}ms"
        for view, bone, az, dist in views:
            target = to_blender(world[rig.index[bone]][:3, 3]) + (np.array([0.0, 0.0, -0.08]) if "arm" in view else 0)
            stage.aim(target, az + turn, dist, lens=50.0, elevation=5.0)
            close.append(stage.render(os.path.join(render_dir, "checks", f"{tag}_{view}.png")))
    contact_sheet(close, len(views), os.path.join(render_dir, "sheet_checks.png"))
    for lamp in added:
        bpy.data.objects.remove(lamp, do_unlink=True)
    for o in lamps:
        o.hide_render = False
    stage.camera.data.clip_start = clip_start
    metrics["check_frames"] = close_rows

    # Every humanoid clip on disk, numbers only, 16 samples each.
    sweep = {}
    folder = os.path.join(ASSETS, "animation", "ready", "humanoid")
    for fname in sorted(os.listdir(folder)):
        if not fname.endswith(".glb"):
            continue
        clip = GltfClip(os.path.join(folder, fname))
        for mode in args.retarget:
            worst = {"edge_stretch_max": 0.0, "edge_stretch_p999": 0.0, "lowest_point_min_m": 9.0,
                     "hand_socket_outside_hand_samples": 0, "elbow_outside_arm_samples": 0,
                     "self_intersecting_faces_max": 0, "samples": 0}
            for t in np.linspace(0.0, clip.duration, 16):
                world = engine_pose(rig, clip, t, mode)
                P_, _N = rig.skin(world)
                ratio = np.linalg.norm(P_[edges[:, 0]] - P_[edges[:, 1]], axis=1) / np.maximum(rest_len, 1e-9)
                tree = bvh(to_blender(P_[first]), welded_tri)
                worst["edge_stretch_max"] = max(worst["edge_stretch_max"], round(float(ratio.max()), 3))
                worst["edge_stretch_p999"] = max(worst["edge_stretch_p999"], round(float(np.percentile(ratio, 99.9)), 3))
                worst["lowest_point_min_m"] = min(worst["lowest_point_min_m"], round(float(P_[:, 1].min()), 4))
                worst["self_intersecting_faces_max"] = max(worst["self_intersecting_faces_max"],
                                                           int(len(self_intersections(tree, welded_tri))))
                for s in ("SOCK_hand_l", "SOCK_hand_r"):
                    j, local = rig.sockets[s]
                    worst["hand_socket_outside_hand_samples"] += not is_inside(
                        inside_counts(tree, to_blender((world[j] @ local)[:3, 3])))
                for j in elbows:
                    worst["elbow_outside_arm_samples"] += not is_inside(inside_counts(tree, to_blender(world[j][:3, 3])))
                worst["samples"] += 1
            sweep.setdefault(mode, {})[fname[len("anim."):-len(".glb")]] = worst
    metrics["all_humanoid_clips"] = sweep
    metrics_path = os.path.join(out_dir, f"{asset}_clip_metrics.json")
    with open(metrics_path, "w", encoding="utf-8") as handle:
        json.dump(metrics, handle, indent=2)
    print("RENDER_RESULT " + json.dumps({"metrics": metrics_path, "renders": render_dir, "bind": metrics["bind"]}))
    return 0


def main():
    args = parse_args()
    if args.mode == "fit":
        return main_fit(args)
    return main_render(args)


if __name__ == "__main__":
    sys.exit(main())
