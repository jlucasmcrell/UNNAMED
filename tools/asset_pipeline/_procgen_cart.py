"""Procedural build of prop_cart_damaged_merchant: the ruined merchant cart on the Charwood trail (hero prop).

Image-to-3D cannot make this prop (it melts thin, open assemblies), so it is built here from real member sizes in
headless Blender 5.2, baked to PBR textures and exported as a GLB. The whole build is deterministic from --seed.

Design follows the approved concept (assets/concepts/prop_cart_damaged_merchant.png): a four-wheeled plank-bed cart,
bed 1.80 x 0.96 m, three-board sides and ends, corner posts and side stakes capped and strapped in iron, spoked wooden
wheels with iron tyres, weathered grey oak, rusted iron, a rope hanging from the front cross-beam and cloth rolls in the
bed. Damage by design: the near front wheel has collapsed (its felloes broke, the iron tyre sprang off and lies on the
ground with the broken pieces), so the fore axle rolls and the bed tilts ~10 deg toward the player and the shafts; the
near side has a broken-out bay of boards (one piece on the ground), two floor planks are splintered at the front, both
shafts are snapped and one loose plank lies in front. The concept shows no cover, so there is none.

Frames: built in Blender Z-up with the cart's length on X (shafts toward -X) and the player side on -Y. The glTF export
(+Y up) turns that into front +Z, long side on X, centred on X/Z, lowest point at y = 0. SOCK_item is an empty on the bed
floor where the bow lies: its local +Y is the bed-floor normal, local +X runs along the bed.

Every member is a closed swept solid (outward winding, chamfered edges), unwrapped in its own frame: U along the
member (the grain), V around its section, in metres. A second UV set carries the same unwrap with a per-member offset
and drives the procedural shaders, so the grain follows each member; the first UV set is the same islands packed into
two 2048 atlases at one texel density. The shaders (weathered oak, fresh breaks, rusted iron, hemp rope, oilcloth) are
baked with Cycles to base colour (sRGB), tangent normal (OpenGL) and ORM, and embedded in the GLB.

Usage:
  blender --background --factory-startup --python _procgen_cart.py -- [--out-dir DIR] [--seed N] [--res 2048]
          [--samples 32] [--device auto|cpu] [--no-bake] [--work-dir DIR]

Writes <out-dir>/prop_cart_damaged_merchant.glb and prop_cart_damaged_merchant_provenance.json. Textures and a .blend
go to --work-dir (default: a temp folder), not to the staging folder.
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import random
import sys
import tempfile
import time
from contextlib import contextmanager

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ASSET_ID = "prop_cart_damaged_merchant"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
CONCEPT = os.path.join(REPO, "assets", "concepts", ASSET_ID + ".png")
DEFAULT_OUT = os.path.join(REPO, "assets", "_staging", "procedural", ASSET_ID)
TAU = 2.0 * math.pi
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

WOOD, IRON, BREAK, ROPE, CLOTH = range(5)
KINDS = ("wood", "iron", "break", "rope", "cloth")
BODY, GEAR = 0, 1
ATLASES = ("body", "gear")

# World blocker cart_wreck (content/regions/ashen_hollow.yaml): 3.0 x 2.0 m, 1.4 m tall. Fitting.cs rejects a model
# that leaves more than 0.5 m of blocker per side or stops more than 0.6 m below its top.
BLOCKER = {"x": 3.0, "z": 2.0, "height": 1.4, "side_margin": 0.5, "height_shortfall": 0.6}
# The bow pickup sits at world (157, 162); the blocker centre is (157, 162.2), so the bow is 0.2 m toward glTF -Z.
BOW_OFFSET_GLTF_Z = -0.2
# Where the bow lies, in the bed frame (x along the bed from its centre, y across, on the floor): on the centre line of
# the far-side planks so a 1.2-1.4 m bow clears both end boards and the cloth rolls on the near side.
BOW_BED_XY = (-0.12, 0.17)

PARAMS = {
    # wheels
    "wheel_r": 0.36, "tyre_t": 0.010, "tyre_w": 0.074, "felloe_d": 0.064, "felloe_w": 0.070,
    "spokes": 10, "track": 1.34, "axle_x": 0.48, "axle_sec": 0.09, "bolster_h": 0.07, "rear_wedge_min": 0.03,
    "near_hub_z": 0.19,            # the collapsed wheel's hub, propped on spoke stubs over a broken felloe
    # bed
    "bed_len": 1.80, "floor_w": 0.96, "sill_h": 0.10, "sill_w": 0.09, "floor_t": 0.028, "plank_w": 0.155,
    "plank_gap": 0.006, "board_h": 0.15, "board_t": 0.025, "board_gap": 0.012, "post": 0.065, "stake": 0.06,
    "stake_x": 0.28, "transom_half_span": 0.575,
    # iron
    "strap_t": 0.005, "strap_w": 0.055, "cap_w": 0.07, "band_t": 0.005,
    # shafts and overall
    "shaft_h": 0.07, "shaft_w": 0.06, "overall_x": 2.90,
    # texturing
    "texel_target_px_per_m": 512,
}


# --------------------------------------------------------------------------------------------------------------------
# Geometry kernel: closed swept solids with member-frame UVs
# --------------------------------------------------------------------------------------------------------------------

class Builder:
    """Accumulates vertices, faces, per-corner member-frame UVs (metres), materials and per-member attributes."""

    def __init__(self, rng):
        self.rng = rng
        self.verts, self.faces, self.fuv, self.fmat, self.fpart = [], [], [], [], []
        self.parts = []
        self.M = Matrix.Identity(4)
        self.atlas = BODY
        self.group = "bed"

    @contextmanager
    def xf(self, M):
        old = self.M
        self.M = old @ M
        try:
            yield
        finally:
            self.M = old

    @contextmanager
    def using(self, atlas=None, group=None):
        old = (self.atlas, self.group)
        if atlas is not None:
            self.atlas = atlas
        if group is not None:
            self.group = group
        try:
            yield
        finally:
            self.atlas, self.group = old

    def part(self, name, mud=0.0, weather=None):
        r = self.rng
        self.parts.append({
            "name": name, "group": self.group, "atlas": self.atlas, "rand": r.random(),
            "weather": r.random() if weather is None else weather, "mud": mud,
            "off": (r.uniform(0.0, 40.0), r.uniform(0.0, 40.0)), "v0": len(self.verts), "f0": len(self.faces)})
        return len(self.parts) - 1

    def v(self, p):
        self.verts.append(self.M @ Vector(p))
        return len(self.verts) - 1

    def f(self, idx, uv, mat):
        self.faces.append(tuple(idx))
        self.fuv.append(tuple((float(a), float(c)) for a, c in uv))
        self.fmat.append(mat)
        self.fpart.append(len(self.parts) - 1)

    def mark(self):
        return len(self.verts), len(self.faces), len(self.parts)

    def rollback(self, mk):
        nv, nf, np_ = mk
        del self.verts[nv:], self.faces[nf:], self.fuv[nf:], self.fmat[nf:], self.fpart[nf:], self.parts[np_:]

    def span_min_z(self, mk):
        return min(v.z for v in self.verts[mk[0]:])

    def lift(self, mk, dz):
        for v in self.verts[mk[0]:]:
            v.z += dz


def rect_section(h, w, c=0.0, nt=1, ns=1):
    """Chamfered rectangle, CCW seen from +T: s along N (height h), t along B (width w)."""
    hs, ht = h / 2.0, w / 2.0
    c = min(c, 0.45 * hs, 0.45 * ht)

    def edge(a, b, n):
        return [(a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n) for k in range(n)]

    if c > 1e-6:
        a1, a2, a3, a4 = (hs, -ht + c), (hs, ht - c), (hs - c, ht), (-hs + c, ht)
        a5, a6, a7, a8 = (-hs, ht - c), (-hs, -ht + c), (-hs + c, -ht), (hs - c, -ht)
        return (edge(a1, a2, nt) + edge(a2, a3, 1) + edge(a3, a4, ns) + edge(a4, a5, 1) + edge(a5, a6, nt)
                + edge(a6, a7, 1) + edge(a7, a8, ns) + edge(a8, a1, 1))
    c1, c2, c3, c4 = (hs, -ht), (hs, ht), (-hs, ht), (-hs, -ht)
    return edge(c1, c2, nt) + edge(c2, c3, ns) + edge(c3, c4, nt) + edge(c4, c1, ns)


def circle_section(r, n, phase=0.0):
    return [(r * math.cos(phase + TAU * k / n), r * math.sin(phase + TAU * k / n)) for k in range(n)]


def normal_to(T, up):
    """N perpendicular to T, as close to `up` as possible, and B = T x N (so T, N, B is right-handed)."""
    up = Vector(up)
    N = up - T * up.dot(T)
    if N.length < 1e-8:
        alt = X if abs(T.x) < 0.9 else Y
        N = alt - T * alt.dot(T)
    N.normalize()
    return N, T.cross(N)


def splinter_profile(rng, n, jag):
    """Per-ring-vertex protrusion of a broken end: short fibres with a few long splinters."""
    out, prev = [], 0.0
    for _ in range(n):
        d = jag * (0.08 + 0.45 * rng.random() ** 2)
        if rng.random() < 0.28:
            d += jag * (0.45 + 0.55 * rng.random())
        d = 0.65 * d + 0.35 * prev
        out.append(d)
        prev = d
    return out


def sweep(b, sec, frames, mat, caps=("flat", "flat"), jags=(None, None), closed=False, cap_mat=BREAK, rec=0.0):
    """Sweep a CCW section through frames (P, T, N, B, ks, kt, u). Sides are quads (outward), ends are flat n-gons
    or, for a broken end, a fan to a recessed centre with the ring pushed out into splinters."""
    n, m = len(sec), len(frames)
    rings = []
    for i, (P, T, N, B, ks, kt, u) in enumerate(frames):
        ring = []
        for j, (s, t) in enumerate(sec):
            d = 0.0
            if not closed and i == 0 and jags[0] is not None:
                d = -jags[0][j]
            if not closed and i == m - 1 and jags[1] is not None:
                d = jags[1][j]
            ring.append((P + N * (s * ks) + B * (t * kt) + T * d, u + abs(d) * (1 if i else -1)))
        rings.append(ring)
    idx = [[b.v(p) for p, _ in ring] for ring in rings]
    vcoord = []
    for ring in rings:
        acc = [0.0]
        for j in range(n):
            acc.append(acc[-1] + (ring[(j + 1) % n][0] - ring[j][0]).length)
        vcoord.append(acc)
    total_u = frames[-1][6] + (frames[0][0] - frames[-1][0]).length if closed else 0.0
    for i in range(m if closed else m - 1):
        i1 = (i + 1) % m
        wrap = total_u if (closed and i1 == 0) else 0.0
        for j in range(n):
            j1 = (j + 1) % n
            uv = [(rings[i][j][1], vcoord[i][j]), (rings[i][j1][1], vcoord[i][j + 1]),
                  (rings[i1][j1][1] + wrap, vcoord[i1][j + 1]), (rings[i1][j][1] + wrap, vcoord[i1][j])]
            b.f((idx[i][j], idx[i][j1], idx[i1][j1], idx[i1][j]), uv, mat)
    if closed:
        return
    for end in (0, 1):
        ring = idx[0] if end == 0 else idx[-1]
        P, T, N, B, ks, kt, u = frames[0] if end == 0 else frames[-1]
        planar = [(s * ks, t * kt) for s, t in sec]
        if caps[end] != "broken":
            order = list(range(n)) if end == 1 else list(reversed(range(n)))
            b.f([ring[j] for j in order], [planar[j] for j in order], mat)
            continue
        hs = max(abs(s) for s, _ in sec) * ks
        ht = max(abs(t) for _, t in sec) * kt
        cs, ct = b.rng.uniform(-0.25, 0.25) * hs, b.rng.uniform(-0.25, 0.25) * ht
        depth = rec if rec else 0.3 * max(jags[end])
        centre = P + N * cs + B * ct + T * (depth if end == 0 else -depth)
        ci = b.v(centre)
        for j in range(n):
            j1 = (j + 1) % n
            if end == 1:
                b.f((ci, ring[j], ring[j1]), [(cs, ct), planar[j], planar[j1]], cap_mat)
            else:
                b.f((ci, ring[j1], ring[j]), [(cs, ct), planar[j1], planar[j]], cap_mat)


def member(b, name, p0, p1, up, h, w, mat=WOOD, c=0.004, ends=("flat", "flat"), taper=(1.0, 1.0), bow=0.0,
           nseg=1, nt=1, ns=1, jag=0.05, mud=0.0, weather=None):
    """Straight (optionally bowed and tapered) chamfered beam from p0 to p1; h along `up`, w across."""
    p0, p1 = Vector(p0), Vector(p1)
    D = p1 - p0
    L = D.length
    T0 = D / L
    N0, _ = normal_to(T0, up)
    brk = [e == "broken" for e in ends]
    if any(brk):
        nt = max(nt, int(w / 0.02) + 1)
        ns = max(ns, int(h / 0.02) + 1)
    cc = min(c, 0.45 * h / 2, 0.45 * w / 2, 0.3 * L)
    sec = rect_section(h, w, cc, nt, ns)
    stations = []
    if not brk[0] and cc > 0:
        stations.append((0.0, True))
    stations.append((cc if (not brk[0] and cc > 0) else 0.0, False))
    for k in range(1, nseg):
        stations.append((L * k / nseg, False))
    stations.append((L - cc if (not brk[1] and cc > 0) else L, False))
    if not brk[1] and cc > 0:
        stations.append((L, True))
    frames = []
    for u, inset in stations:
        x = u / L
        P = p0 + T0 * u + N0 * (bow * 4.0 * x * (1.0 - x))
        T = (T0 + N0 * (bow * 4.0 * (1.0 - 2.0 * x) / L)).normalized()
        N, B = normal_to(T, N0)
        ks = 1.0 + (taper[0] - 1.0) * x
        kt = 1.0 + (taper[1] - 1.0) * x
        if inset:
            ks *= (h - 2 * cc) / h
            kt *= (w - 2 * cc) / w
        frames.append((P, T, N, B, ks, kt, u))
    jags = [splinter_profile(b.rng, len(sec), jag) if brk[e] else None for e in (0, 1)]
    b.part(name, mud=mud, weather=weather)
    sweep(b, sec, frames, mat, caps=ends, jags=jags)


def lathe(b, name, C, A, profile, n=16, mat=WOOD, up=None, mud=0.0, phase=0.0):
    """Solid of revolution about axis A from point C; profile is [(u along A, radius)]."""
    A = Vector(A).normalized()
    C = Vector(C)
    N, B = normal_to(A, up if up is not None else (Z if abs(A.z) < 0.9 else X))
    frames = [(C + A * u, A, N, B, r, r, u) for u, r in profile]
    b.part(name, mud=mud)
    sweep(b, circle_section(1.0, n, phase), frames, mat)


def ring(b, name, C, E1, E2, radius, h=None, w=None, c=0.0, circ=None, a0=0.0, a1=TAU, n=48, mat=IRON,
         ends=("flat", "flat"), jag=0.03, mud=0.0, nt=1, ns=1):
    """Sweep along a circle (or arc) of `radius` about C in the plane E1-E2; section radial h x axial w, or round."""
    C, E1, E2 = Vector(C), Vector(E1), Vector(E2)
    closed = abs((a1 - a0) - TAU) < 1e-6
    sec = circle_section(circ, 8) if circ else rect_section(h, w, c, nt, ns)
    cc = 0.0 if circ else min(c, 0.45 * h / 2, 0.45 * w / 2)
    brk = [e == "broken" for e in ends]
    if not closed and any(brk) and not circ:
        sec = rect_section(h, w, cc, max(nt, int(w / 0.02) + 1), max(ns, int(h / 0.02) + 1))
    stations = []
    if closed:
        stations = [(a0 + TAU * k / n, False) for k in range(n)]
    else:
        da = cc / radius
        if not brk[0] and da > 0:
            stations.append((a0, True))
        start = a0 + (da if not brk[0] else 0.0)
        stop = a1 - (da if not brk[1] else 0.0)
        segs = max(2, int(round(n * (a1 - a0) / TAU)))
        stations += [(start + (stop - start) * k / segs, False) for k in range(segs + 1)]
        if not brk[1] and da > 0:
            stations.append((a1, True))
    frames = []
    for th, inset in stations:
        radial = E1 * math.cos(th) + E2 * math.sin(th)
        T = -E1 * math.sin(th) + E2 * math.cos(th)
        N = radial
        B = T.cross(N)
        ks = kt = 1.0
        if inset:
            ks, kt = (h - 2 * cc) / h, (w - 2 * cc) / w
        frames.append((C + radial * radius, T, N, B, ks, kt, radius * (th - a0)))
    jags = [splinter_profile(b.rng, len(sec), jag) if (brk[e] and not closed) else None for e in (0, 1)]
    b.part(name, mud=mud)
    sweep(b, sec, frames, mat, caps=ends, jags=jags, closed=closed)


def poly_sweep(b, name, pts, bend_axis, h, w, mat=IRON, closed=False, mud=0.0):
    """Flat bar (thickness h, width w along bend_axis) bent along a polyline with mitred corners: bands and caps."""
    pts = [Vector(p) for p in pts]
    Bax = Vector(bend_axis).normalized()
    m = len(pts)
    frames, u = [], 0.0
    for i in range(m):
        if closed:
            d0 = (pts[i] - pts[i - 1]).normalized()
            d1 = (pts[(i + 1) % m] - pts[i]).normalized()
        else:
            d0 = (pts[i] - pts[i - 1]).normalized() if i > 0 else (pts[1] - pts[0]).normalized()
            d1 = (pts[i + 1] - pts[i]).normalized() if i < m - 1 else d0
        T = (d0 + d1).normalized()
        N = Bax.cross(T).normalized()
        if i > 0:
            u += (pts[i] - pts[i - 1]).length
        frames.append((pts[i], T, N, Bax, 1.0 / max(0.3, T.dot(d0)), 1.0, u))
    b.part(name, mud=mud)
    sweep(b, rect_section(h, w, 0.0), frames, mat, closed=closed)


def catmull(points, step=0.02):
    pts = [Vector(p) for p in points]
    ext = [pts[0] + (pts[0] - pts[1])] + pts + [pts[-1] + (pts[-1] - pts[-2])]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        k = max(2, int((p2 - p1).length / step))
        for s in range(k):
            t = s / k
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return out


def tube(b, name, pts, r, n=8, mat=ROPE, mud=0.0, closed=False):
    """Round tube along a point path, parallel-transported frames (rope)."""
    pts = [Vector(p) for p in pts]
    m = len(pts)
    frames, u = [], 0.0
    N = None
    for i in range(m):
        if closed:
            d = pts[(i + 1) % m] - pts[i - 1]
        else:
            d = pts[min(i + 1, m - 1)] - pts[max(i - 1, 0)]
        T = d.normalized()
        if N is None:
            N, _ = normal_to(T, Z if abs(T.z) < 0.9 else X)
        N = (N - T * N.dot(T)).normalized()
        B = T.cross(N)
        if i > 0:
            u += (pts[i] - pts[i - 1]).length
        frames.append((pts[i], T, N, B, 1.0, 1.0, u))
    b.part(name, mud=mud)
    sweep(b, circle_section(r, n), frames, mat, closed=closed)


def box8(b, name, bottom, top, mat=WOOD, grain_axis=1):
    """Hexahedron from 4 bottom and 4 top corners (each ordered x0y0, x1y0, x1y1, x0y1): the rear bolster wedge."""
    b.part(name)
    vb = [b.v(p) for p in bottom]
    vt = [b.v(p) for p in top]
    P = [Vector(p) for p in bottom] + [Vector(p) for p in top]

    def uvs(ids, axes):
        return [(P[i][axes[0]], P[i][axes[1]]) for i in ids]
    g = grain_axis
    other = 0 if g == 1 else 1
    # bottom, top, y0, y1, x0, x1 (all outward with bottom/top corners listed x0y0, x1y0, x1y1, x0y1)
    quads = [((0, 3, 2, 1), (g, other)), ((4, 5, 6, 7), (g, other)), ((0, 1, 5, 4), (0, 2)),
             ((3, 7, 6, 2), (0, 2)), ((0, 4, 7, 3), (1, 2)), ((1, 2, 6, 5), (1, 2))]
    allv = vb + vt
    for q, axes in quads:
        b.f([allv[i] for i in q], uvs(q, axes), mat)


def rivet(b, p, axis, r=0.009, h=0.006):
    lathe(b, "rivet", Vector(p) - Vector(axis).normalized() * 0.0008, axis,
          [(0.0, r), (h * 0.45, r * 0.86), (h * 0.8, r * 0.5), (h, r * 0.12)], n=8, mat=IRON)


def band_around(b, name, centre, axis, half_a, half_b, dir_a, t=0.005, w=0.03):
    """Iron band around a rectangular member: axis = member axis, half sizes along dir_a and axis x dir_a."""
    centre, axis, dir_a = Vector(centre), Vector(axis).normalized(), Vector(dir_a).normalized()
    dir_b = axis.cross(dir_a)
    a, c = half_a + t / 2, half_b + t / 2
    pts = [centre + dir_a * a + dir_b * c, centre - dir_a * a + dir_b * c,
           centre - dir_a * a - dir_b * c, centre + dir_a * a - dir_b * c]
    poly_sweep(b, name, pts, axis, t, w, closed=True)


# --------------------------------------------------------------------------------------------------------------------
# Cart assembly
# --------------------------------------------------------------------------------------------------------------------

def hub_profile():
    return [(0.0, 0.056), (0.008, 0.064), (0.03, 0.074), (0.10, 0.088), (0.17, 0.082), (0.225, 0.070),
            (0.232, 0.064), (0.24, 0.056)]


def hub_r(u):
    prof = hub_profile()
    for (u0, r0), (u1, r1) in zip(prof, prof[1:]):
        if u0 <= u <= u1:
            return r0 + (r1 - r0) * (u - u0) / (u1 - u0)
    return prof[-1][1]


def wheel_basis(A):
    A = Vector(A).normalized()
    E2 = (Z - A * Z.dot(A)).normalized()
    return A, E2.cross(A), E2


def nave(b, name, C, A, E1, E2, mud):
    start = C - A * 0.10
    lathe(b, name + "_nave", start, A, hub_profile(), n=16, mat=WOOD, up=E2, mud=mud)
    for u0 in (0.022, 0.212):
        ring(b, name + "_nave_band", start + A * u0, E1, E2, hub_r(u0) + 0.003, h=0.006, w=0.018, c=0.0012, n=24)
    lathe(b, name + "_axle_cap", start + A * 0.236, A,
          [(0.0, 0.046), (0.004, 0.05), (0.018, 0.05), (0.026, 0.04), (0.032, 0.022)], n=12, mat=IRON, up=E2)


def spoke(b, name, C, d, A, r_in, r_out, ends=("flat", "flat"), mud=0.4):
    member(b, name, C + d * r_in, C + d * r_out, A, 0.050, 0.038, c=0.010, taper=(0.8, 0.8), ends=ends,
           jag=0.035, mud=mud)


def wheel(b, name, C, A, P, theta0, mud=0.85):
    A, E1, E2 = wheel_basis(A)
    R, tt = P["wheel_r"], P["tyre_t"]
    r_fo = R - tt
    r_fi = r_fo - P["felloe_d"]
    nave(b, name, C, A, E1, E2, mud)
    for k in range(P["spokes"]):
        th = theta0 + TAU * k / P["spokes"]
        spoke(b, f"{name}_spoke", C, E1 * math.cos(th) + E2 * math.sin(th), A, 0.07, r_fi + 0.018)
    for i in range(5):
        a0 = theta0 + TAU * (2 * i + 1) / 20.0
        ring(b, f"{name}_felloe", C, E1, E2, r_fo - P["felloe_d"] / 2, h=P["felloe_d"], w=P["felloe_w"], c=0.005,
             a0=a0, a1=a0 + TAU / 5, n=60, mat=WOOD, mud=mud)
    ring(b, f"{name}_tyre", C, E1, E2, R - tt / 2, h=tt, w=P["tyre_w"], c=0.002, n=60, mat=IRON, mud=mud)


def broken_wheel(b, name, C, A, P, theta0, keep, stub_floor):
    """The collapsed wheel: nave on the axle arm, the upper felloes on four spokes, the rest snapped to stubs."""
    A, E1, E2 = wheel_basis(A)
    R, tt, fd = P["wheel_r"], P["tyre_t"], P["felloe_d"]
    r_fo = R - tt
    r_fi = r_fo - fd
    nave(b, name, C, A, E1, E2, 0.85)
    lo, hi = keep
    stub_len = [0.08, 0.15, 0, 0, 0, 0, 0.06, 0.05, 0.03, 0.05]
    for k in range(P["spokes"]):
        th = theta0 + TAU * k / P["spokes"]
        d = E1 * math.cos(th) + E2 * math.sin(th)
        deg = math.degrees(th) % 360.0
        if lo + 6 < deg < hi - 6:
            spoke(b, f"{name}_spoke", C, d, A, 0.07, r_fi + 0.018)
            continue
        length = stub_len[k]
        if d.z < -1e-3:  # a downward stub stops above the broken felloe it rests on
            length = min(length, (C.z - stub_floor - 0.03) / -d.z - 0.088 - 0.035)
        if length < 0.012:
            continue                 # snapped flush under the nave
        spoke(b, f"{name}_spoke_stub", C, d, A, 0.07, 0.088 + length, ends=("flat", "broken"), mud=0.6)
    arcs = []
    for i in range(5):
        a0 = math.degrees(theta0) + 360.0 * (2 * i + 1) / 20.0
        a1 = a0 + 72.0
        s0, s1 = max(a0, lo), min(a1, hi)
        if s1 - s0 > 5:
            arcs.append((s0, s1, "broken" if s0 > a0 + 1e-6 else "flat", "broken" if s1 < a1 - 1e-6 else "flat"))
    for s0, s1, e0, e1 in arcs:
        ring(b, f"{name}_felloe", C, E1, E2, r_fo - fd / 2, h=fd, w=P["felloe_w"], c=0.005, a0=math.radians(s0),
             a1=math.radians(s1), n=60, mat=WOOD, ends=(e0, e1), jag=0.04, mud=0.6)


def solve_front_axle(P):
    """Roll of the fore axle (about X) with the far wheel on the ground and the near hub at near_hub_z."""
    R, tw, half, target = P["wheel_r"], P["tyre_w"], P["track"] / 2, P["near_hub_z"]

    def f(beta):
        s, c = math.sin(beta), math.cos(beta)
        zc = target + half * s
        return (zc + half * s) - (R * c + tw / 2 * s)

    lo, hi = 0.0, 0.6
    for _ in range(80):
        mid = (lo + hi) / 2
        if f(lo) * f(mid) <= 0:
            hi = mid
        else:
            lo = mid
    beta = (lo + hi) / 2
    zc = target + half * math.sin(beta)
    return beta, zc


def bed_matrix(P, M_fa, beta, phi):
    ax = P["axle_x"]
    K = M_fa @ Vector((0, 0, P["axle_sec"] / 2 + P["bolster_h"]))
    return (Matrix.Translation(K) @ Matrix.Rotation(beta, 4, "X") @ Matrix.Rotation(phi, 4, "Y")
            @ Matrix.Translation(Vector((ax, 0, 0))))


def build_bed(b, P, rng):
    """Bed in its own frame: origin at the underside centre, length on X (front -X), near side -Y."""
    hl, hw = P["bed_len"] / 2, P["floor_w"] / 2
    sh, sw, ft = P["sill_h"], P["sill_w"], P["floor_t"]
    bh, bt, bg = P["board_h"], P["board_t"], P["board_gap"]
    z_f1 = sh + ft
    z_top = z_f1 + 3 * bh + 2 * bg
    y_in = hw - bt
    ps, st, sx_ = P["post"], P["stake"], P["stake_x"]
    tsp = P["transom_half_span"]
    stt, stw = P["strap_t"], P["strap_w"]
    info = {"z_floor_top": z_f1, "z_top": z_top}

    # under-frame (baked into the gear atlas, which balances the two atlases' area)
    under = b.using(atlas=GEAR)
    under.__enter__()
    for sy in (-1, 1):
        member(b, "sill", (-(hl - sw), sy * (hw - sw / 2), sh / 2), (hl - sw, sy * (hw - sw / 2), sh / 2), Z, sh, sw,
               c=0.006)
    for sx in (-1, 1):
        member(b, "transom", (sx * (hl - sw / 2), -tsp, sh / 2), (sx * (hl - sw / 2), tsp, sh / 2), Z, sh, sw, c=0.006)
    for x in (-0.30, 0.30):
        member(b, "bearer", (x, -(hw - sw), sh / 2 + 0.01), (x, hw - sw, sh / 2 + 0.01), Z, 0.08, 0.07)
    under.__exit__(None, None, None)

    # floor: six planks, two splintered short at the front
    pw, pg = P["plank_w"], P["plank_gap"]
    front_break = {1: -0.70, 4: -0.78}
    for k in range(6):
        y = -hw + pw / 2 + k * (pw + pg) + rng.uniform(-0.002, 0.002)
        z = sh + ft / 2 + rng.uniform(-0.0012, 0.0012)
        x0 = front_break.get(k, -hl)
        member(b, "floor_plank", (x0, y, z), (hl, y, z), Z, ft, pw, c=0.004,
               ends=("broken" if k in front_break else "flat", "flat"), bow=rng.uniform(-0.003, 0.001),
               nseg=3, jag=0.07)

    # side boards; the near side (-Y) has a broken-out bay between the rear stake and the rear post
    near_bay = {1: [(-hl, 0.36, "flat", "broken"), (0.80, hl, "broken", "flat")],
                2: [(-hl, 0.55, "flat", "broken"), (0.63, hl, "broken", "flat")]}
    for sy in (-1, 1):
        for k in range(3):
            zc = z_f1 + bh / 2 + k * (bh + bg) + rng.uniform(-0.002, 0.002)
            yc = sy * (hw - bt / 2)
            pieces = near_bay.get(k, [(-hl, hl, "flat", "flat")]) if sy < 0 else [(-hl, hl, "flat", "flat")]
            for x0, x1, e0, e1 in pieces:
                member(b, "side_board", (x0, yc, zc), (x1, yc, zc), Z, bh, bt, c=0.004, ends=(e0, e1), nseg=2,
                       jag=0.09, bow=rng.uniform(-0.003, 0.003))
    for sx in (-1, 1):
        for k in range(3):
            zc = z_f1 + bh / 2 + k * (bh + bg) + rng.uniform(-0.002, 0.002)
            xc = sx * (hl - bt / 2)
            member(b, "end_board", (xc, -y_in, zc), (xc, y_in, zc), Z, bh, bt, c=0.004)

    # corner posts and stakes
    for sx in (-1, 1):
        for sy in (-1, 1):
            xc, yc = sx * (hl - ps / 2), sy * (hw + ps / 2)
            member(b, "corner_post", (xc, yc, sh), (xc, yc, z_top + 0.05), X, ps, ps, c=0.007)
            for zb in (0.17, z_top - 0.035):
                band_around(b, "post_band", (xc, yc, zb), Z, ps / 2, ps / 2, X, t=P["band_t"], w=0.032)
    for sx in (-1, 1):
        for sy in (-1, 1):
            xc, yc = sx * sx_, sy * (hw + st / 2)
            member(b, "stake", (xc, yc, 0.0), (xc, yc, z_top), X, st, st, c=0.006)
            ys = sy * (hw + st + stt / 2)
            member(b, "stake_strap", (xc, ys, 0.004), (xc, ys, z_top - 0.004), (0, sy, 0), stt, stw, c=0.0012,
                   mat=IRON)
            yi, yo, zc = sy * (y_in - stt / 2), sy * (hw + st + stt + stt / 2), z_top + stt / 2
            poly_sweep(b, "stake_cap", [(xc, yi, z_top - 0.085), (xc, yi, zc), (xc, yo, zc), (xc, yo, z_top - 0.11)],
                       X, stt, P["cap_w"])
            for zr in (0.06, 0.29):
                rivet(b, (xc, sy * (hw + st + stt), zr), (0, sy, 0))
            rivet(b, (xc, sy * (y_in - stt), z_top - 0.045), (0, -sy, 0))
            rivet(b, (xc, sy * (hw + st + 2 * stt), z_top - 0.06), (0, sy, 0))
    # transom ends banded where they stand proud of the sides
    for sx in (-1, 1):
        for sy in (-1, 1):
            if sx < 0 and sy < 0:
                continue             # the rope is looped here instead
            band_around(b, "transom_band", (sx * (hl - sw / 2), sy * (tsp - 0.0135), sh / 2), Y, sw / 2, sh / 2, X,
                        t=P["band_t"], w=0.022)
    # end-board straps with caps and rivets
    for sx in (-1, 1):
        for yy in (-0.26, 0.26):
            xs = sx * (hl + stt / 2)
            member(b, "end_strap", (xs, yy, 0.012), (xs, yy, z_top - 0.004), (sx, 0, 0), stt, stw, c=0.0012,
                   mat=IRON)
            xi, xo, zc = sx * (hl - bt - stt / 2), sx * (hl + stt + stt / 2), z_top + stt / 2
            poly_sweep(b, "end_cap", [(xi, yy, z_top - 0.08), (xi, yy, zc), (xo, yy, zc), (xo, yy, z_top - 0.10)],
                       Y, stt, P["cap_w"])
            for zr in (0.05, 0.22, 0.41):
                rivet(b, (sx * (hl + stt), yy, zr), (sx, 0, 0))
    # a ring on the tail board
    member(b, "ring_plate", (hl + 0.002, -0.03, 0.43), (hl + 0.002, 0.03, 0.43), X, 0.004, 0.045, c=0.001, mat=IRON)
    tilt = Matrix.Rotation(math.radians(-14), 3, "Y")
    ring(b, "tail_ring", (hl + 0.013, 0, 0.395), Y, tilt @ Z, 0.034, circ=0.0055, n=20)

    # cargo: two oilcloth rolls on wooden cores, slid down against the near side, tied with rope
    cargo = b.using(atlas=GEAR)
    cargo.__enter__()
    for (cx, cy, rr, ln, yaw) in ((0.42, -0.355, 0.075, 0.72, 0.0), (0.50, -0.15, 0.068, 0.60, 8.0)):
        D = Matrix.Rotation(math.radians(yaw), 3, "Z") @ X
        c0 = Vector((cx, cy, z_f1 + rr)) - D * (ln / 2)
        lathe(b, "cloth_roll", c0, D, [(0.0, rr * 0.78), (0.012, rr * 0.95), (0.035, rr), (ln - 0.035, rr),
                                       (ln - 0.012, rr * 0.95), (ln, rr * 0.78)], n=18, mat=CLOTH)
        lathe(b, "roll_core", c0 - D * 0.035, D, [(0.0, 0.016), (0.004, 0.02), (ln + 0.066, 0.02),
                                                  (ln + 0.07, 0.016)], n=10, mat=WOOD)
        _, e1, e2 = wheel_basis(D) if abs(D.z) < 0.9 else (None, X, Y)
        for ut in (0.16, ln - 0.16):
            ring(b, "roll_tie", c0 + D * ut, e1, e2, rr + 0.004, circ=0.0055, n=24, mat=ROPE)
    cargo.__exit__(None, None, None)
    return info


def build_cart(b, P, rng):
    R, ax, half, asec = P["wheel_r"], P["axle_x"], P["track"] / 2, P["axle_sec"]
    beta, zc = solve_front_axle(P)
    M_fa = Matrix.Translation(Vector((-ax, 0, zc))) @ Matrix.Rotation(beta, 4, "X")
    rear_top = R + asec / 2
    Q = Vector((ax, -P["floor_w"] / 2, 0.0))
    target = rear_top + P["rear_wedge_min"]
    lo, hi = -0.4, 0.4
    for _ in range(80):
        mid = (lo + hi) / 2
        f_lo = (bed_matrix(P, M_fa, beta, lo) @ Q).z - target
        f_mid = (bed_matrix(P, M_fa, beta, mid) @ Q).z - target
        if f_lo * f_mid <= 0:
            hi = mid
        else:
            lo = mid
    phi = (lo + hi) / 2
    M_bed = bed_matrix(P, M_fa, beta, phi)
    bed_up = (M_bed.to_3x3() @ Z).normalized()
    info = {"fore_axle_roll_deg": math.degrees(beta), "bed_pitch_deg": math.degrees(phi),
            "bed_tilt_deg": math.degrees(math.acos(max(-1.0, min(1.0, bed_up.dot(Z))))), "M_bed": M_bed,
            "M_fa": M_fa}

    with b.using(atlas=BODY, group="bed"), b.xf(M_bed):
        info.update(build_bed(b, P, rng))

    with b.using(atlas=GEAR, group="running_gear"):
        # rear axle, level on both rear wheels; wedge bolster up to the tilted bed
        member(b, "axle_tree", (ax, -(half - 0.095), R), (ax, half - 0.095, R), Z, asec, asec, c=0.007)
        x0, x1, y0, y1 = ax - 0.05, ax + 0.05, -0.50, 0.50
        bottom = [(x0, y0, rear_top), (x1, y0, rear_top), (x1, y1, rear_top), (x0, y1, rear_top)]
        top = [tuple(M_bed @ Vector((x, y, 0.0))) for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1))]
        box8(b, "rear_bolster", bottom, top)
        # fore axle, rolled; bolster on it carries the bed at the king pin
        with b.xf(M_fa):
            member(b, "axle_tree", (0, -(half - 0.095), 0), (0, half - 0.095, 0), Z, asec, asec, c=0.007)
            member(b, "fore_bolster", (0, -0.50, asec / 2 + P["bolster_h"] / 2), (0, 0.50, asec / 2 + P["bolster_h"] / 2),
                   Z, P["bolster_h"], 0.10, c=0.006)
            for sy in (-1, 1):
                band_around(b, "axle_band", (0, sy * (half - 0.13), 0), Y, asec / 2, asec / 2, X, w=0.028)
        band = [(ax, sy * (half - 0.13), R) for sy in (-1, 1)]
        for p in band:
            band_around(b, "axle_band", p, Y, asec / 2, asec / 2, X, w=0.028)
        # reach pole from the fore bolster to behind the rear axle
        member(b, "reach", M_fa @ Vector((0.06, 0, asec / 2 + 0.035)), (ax + 0.24, 0, rear_top + 0.035), Z, 0.07,
               0.075, c=0.006)

    # wheels
    theta = [rng.uniform(0, TAU / 10) for _ in range(3)]
    with b.using(atlas=GEAR, group="wheel_rear_near"):
        wheel(b, "wheel_rn", Vector((ax, -half, R)), -Y, P, theta[0])
    with b.using(atlas=GEAR, group="wheel_rear_far"):
        wheel(b, "wheel_rf", Vector((ax, half, R)), Y, P, theta[1])
    rot = M_fa.to_3x3()
    with b.using(atlas=GEAR, group="wheel_fore_far"):
        wheel(b, "wheel_ff", M_fa @ Vector((0, half, 0)), rot @ Y, P, theta[2])
    C_near = M_fa @ Vector((0, -half, 0))
    felloe_top = 0.0 + P["felloe_w"]          # a broken felloe lies flat on the ground under the hub
    with b.using(atlas=GEAR, group="wheel_fore_near"):
        broken_wheel(b, "wheel_fn", C_near, rot @ -Y, P, 0.0, (54.0, 188.0), felloe_top)
    info["near_hub"] = C_near

    build_debris(b, P, rng, info, C_near)
    build_shafts(b, P, rng, info, M_fa)
    build_rope(b, P, info, M_bed)
    return info


def ground(b, mk, target=0.0):
    b.lift(mk, target - b.span_min_z(mk))


def build_debris(b, P, rng, info, C_near):
    R, tt, fd, fw = P["wheel_r"], P["tyre_t"], P["felloe_d"], P["felloe_w"]
    r_mid = R - tt - fd / 2
    hub = Vector((C_near.x, C_near.y, 0))
    # the iron tyre, sprung off, lying on the ground around the collapsed wheel
    with b.using(atlas=GEAR, group="tyre_on_ground"):
        mk = b.mark()
        tilt = Matrix.Rotation(math.radians(2.0), 3, "X")
        centre = Vector((hub.x + 0.04, hub.y - 0.03, 0.0))
        ring(b, "loose_tyre", centre, tilt @ X, tilt @ Y, R - tt / 2, h=tt, w=P["tyre_w"], c=0.002, n=60, mat=IRON,
             mud=0.8)
        ground(b, mk)
        info["loose_tyre_centre"] = centre
    # broken felloes: one flat under the hub (the axle arm rests on it), one beyond the wheel
    with b.using(atlas=GEAR, group="felloe_under_hub"):
        mk = b.mark()
        d = Vector((0.25, 0.97, 0)).normalized()
        c = hub + d * r_mid
        ang = math.atan2(-d.y, -d.x)
        ring(b, "loose_felloe", c, X, Y, r_mid, h=fd, w=fw, c=0.005, a0=ang - math.radians(34),
             a1=ang + math.radians(36), n=60, mat=WOOD, ends=("flat", "broken"), jag=0.05, mud=0.9)
        ground(b, mk)
    with b.using(atlas=GEAR, group="felloe_loose"):
        mk = b.mark()
        c = Vector((0.12, -0.88 + r_mid, 0))
        ring(b, "loose_felloe", c, X, Y, r_mid, h=fd, w=fw, c=0.005, a0=math.radians(-90 - 30),
             a1=math.radians(-90 + 26), n=60, mat=WOOD, ends=("broken", "flat"), jag=0.05, mud=0.9)
        ground(b, mk)
    with b.using(atlas=GEAR, group="spoke_loose"):
        mk = b.mark()
        member(b, "loose_spoke", (hub.x - 0.14, hub.y - 0.25, 0), (hub.x + 0.10, hub.y - 0.29, 0), Z, 0.034,
               0.044, c=0.009, ends=("broken", "broken"), jag=0.04, mud=0.9)
        ground(b, mk)
    # the board that fell out of the near side's broken bay, and a splintered floor plank in front
    with b.using(atlas=BODY, group="board_on_ground"):
        mk = b.mark()
        member(b, "loose_board", (0.40, -0.87, 0), (0.80, -0.83, 0), Z, P["board_t"], P["board_h"], c=0.004,
               ends=("broken", "broken"), jag=0.08, mud=0.7, nseg=2)
        ground(b, mk)
    with b.using(atlas=BODY, group="plank_on_ground"):
        mk = b.mark()
        member(b, "loose_plank", (-1.34, 0.02, 0), (-0.98, 0.10, 0), Z, P["floor_t"], P["plank_w"], c=0.004,
               ends=("broken", "broken"), jag=0.07, mud=0.7, nseg=2)
        ground(b, mk)


def shaft_part(b, name, p0, p1, h0, w0, h1, w1, ends, bow=0.0):
    member(b, name, p0, p1, Z, h0, w0, c=0.009, ends=ends, taper=(h1 / h0, w1 / w0), bow=bow, nseg=4, jag=0.08,
           mud=0.3)


def build_shafts(b, P, rng, info, M_fa):
    """Two shafts hinged at the fore axle, dropped to the ground; both snapped."""
    front_x = max(v.x for v in b.verts) - P["overall_x"] + 0.09   # splinters stand ~8 cm proud of the tip
    h0, w0 = P["shaft_h"], P["shaft_w"]
    h1, w1 = 0.05, 0.045
    with b.using(atlas=GEAR, group="shaft_far"):
        S = M_fa @ Vector((-0.04, 0.40, 0.0))
        tip = Vector((front_x, 0.33, 0.03))
        mk = b.mark()
        for _ in range(3):
            b.rollback(mk)
            shaft_part(b, "shaft", S, tip, h0, w0, h1 * 1.05, w1 * 1.05, ("flat", "broken"), bow=0.035)
            dz = b.span_min_z(mk)
            if abs(dz) < 1e-4:
                break
            tip.z -= dz
        info["shaft_far_tip"] = tip
        L = (tip - S).length
        D = (tip - S).normalized()
        N0, _ = normal_to(D, Z)

        def centre(u):
            x = u / L
            return S + D * u + N0 * (0.035 * 4.0 * x * (1.0 - x)), h0 + (h1 * 1.05 - h0) * x, w0 + (w1 * 1.05 - w0) * x
        c_, hh, ww = centre(0.10)
        band_around(b, "shaft_band", c_, D, hh / 2, ww / 2, N0, w=0.035)
        c_, hh, _ = centre(L - 0.30)
        ring(b, "tug_ring", c_ + N0 * (hh / 2 + 0.016), D, N0, 0.022, circ=0.005, n=16)
    with b.using(atlas=GEAR, group="shaft_near_stub"):
        S = M_fa @ Vector((-0.04, -0.40, 0.0))
        yaw = Vector((-1.0, -0.08, 0.0)).normalized()
        L = 0.78
        end = S + yaw * math.sqrt(max(0.01, L * L - S.z * S.z))
        end.z = 0.02
        mk = b.mark()
        for _ in range(3):
            b.rollback(mk)
            shaft_part(b, "shaft_stub", S, end, h0, w0, h0 * 0.93, w0 * 0.93, ("flat", "broken"))
            dz = b.span_min_z(mk)
            if abs(dz) < 1e-4:
                break
            end.z -= dz
        D = (end - S).normalized()
        band_around(b, "shaft_band", S + D * 0.10, D, h0 / 2 * 0.99, w0 / 2 * 0.99, normal_to(D, Z)[0], w=0.035)
        info["shaft_near_break"] = end
    with b.using(atlas=GEAR, group="shaft_near_piece"):
        brk = info["shaft_near_break"]
        p0 = Vector((brk.x - 0.06, brk.y - 0.10, 0.0))
        p1 = Vector((front_x + 0.08, brk.y - 0.24, 0.0))
        mk = b.mark()
        member(b, "shaft_piece", p0, p1, Z, h0 * 0.9, w0 * 0.9, c=0.009, ends=("broken", "flat"),
               taper=(h1 / (h0 * 0.9), w1 / (w0 * 0.9)), nseg=3, jag=0.1, mud=0.6)
        ground(b, mk)


def build_rope(b, P, info, M_bed):
    """A rope looped round the near end of the front cross-beam, hanging to the ground and lying in a curl."""
    hl, sw, sh, tsp = P["bed_len"] / 2, P["sill_w"], P["sill_h"], P["transom_half_span"]
    xc, yl = -(hl - sw / 2), -(tsp - 0.014)
    r = 0.0085
    a, c = sw / 2 + r, sh / 2 + r
    loop = []
    for k in range(24):
        th = TAU * k / 24
        # rounded rectangle round the beam section (x, z), in the bed frame
        px = max(-a, min(a, (a + 0.012) * math.cos(th)))
        pz = max(-c, min(c, (c + 0.012) * math.sin(th)))
        loop.append(M_bed @ Vector((xc + px, yl, sh / 2 + pz)))
    with b.using(atlas=GEAR, group="rope"):
        tube(b, "rope_loop", loop, r, n=8, closed=True, mud=0.2)
        start = M_bed @ Vector((xc - 0.01, yl - 0.004, sh / 2 - c))
        s = start
        ctrl = [s, s + Vector((-0.01, -0.01, -0.35 * s.z)), Vector((s.x - 0.04, s.y - 0.025, s.z * 0.3)),
                Vector((s.x - 0.09, s.y - 0.05, r + 0.01)), Vector((s.x - 0.20, s.y - 0.09, r)),
                Vector((s.x - 0.34, s.y - 0.05, r)), Vector((s.x - 0.47, s.y - 0.14, r)),
                Vector((s.x - 0.50, s.y - 0.27, r))]
        path = catmull(ctrl, 0.018)
        for p in path:
            p.z = max(r, p.z)
        tube(b, "rope", path, r, n=8, mud=0.5)


# --------------------------------------------------------------------------------------------------------------------
# Mesh, validation, UVs
# --------------------------------------------------------------------------------------------------------------------

def signed_volume(b, f0, f1):
    vol = 0.0
    for fi in range(f0, f1):
        idx = b.faces[fi]
        p0 = b.verts[idx[0]]
        for k in range(1, len(idx) - 1):
            vol += p0.dot(b.verts[idx[k]].cross(b.verts[idx[k + 1]])) / 6.0
    return vol


def validate_builder(b):
    """Per part: closed (every edge used twice, in opposite directions) and positive volume; no bow-tie quads."""
    report = {"parts": len(b.parts), "open_edges": 0, "misoriented_edges": 0, "negative_volume_parts": [],
              "bowtie_quads": 0, "degenerate_faces": 0}
    bounds = [p["f0"] for p in b.parts] + [len(b.faces)]
    for pi in range(len(b.parts)):
        f0, f1 = bounds[pi], bounds[pi + 1]
        directed = {}
        for fi in range(f0, f1):
            idx = b.faces[fi]
            for k in range(len(idx)):
                e = (idx[k], idx[(k + 1) % len(idx)])
                directed[e] = directed.get(e, 0) + 1
        for (a, c), cnt in directed.items():
            if cnt > 1:
                report["misoriented_edges"] += 1
            if (c, a) not in directed:
                report["open_edges"] += 1
        if signed_volume(b, f0, f1) <= 0:
            report["negative_volume_parts"].append(b.parts[pi]["name"])
    for idx in b.faces:
        P = [b.verts[i] for i in idx]
        n = Vector((0, 0, 0))
        for k in range(len(P)):
            n += P[k].cross(P[(k + 1) % len(P)])
        if n.length < 1e-10:
            report["degenerate_faces"] += 1
            continue
        if len(P) == 4:
            ok = all(((P[a] - P[o]).cross(P[c] - P[o])).dot(n) > 0
                     for o, a, c in ((0, 1, 2), (0, 2, 3), (0, 1, 3), (1, 2, 3)))
            if not ok:
                report["bowtie_quads"] += 1
    return report


def clash_report(b):
    """Triangle overlaps between groups that should only touch at intended joints (a review aid, not a gate)."""
    groups = {}
    bounds = [p["f0"] for p in b.parts] + [len(b.faces)]
    for pi, p in enumerate(b.parts):
        groups.setdefault(p["group"], []).extend(range(bounds[pi], bounds[pi + 1]))
    trees = {}
    for g, fl in groups.items():
        polys = [b.faces[i] for i in fl]
        used = sorted({i for f in polys for i in f})
        remap = {o: n for n, o in enumerate(used)}
        trees[g] = BVHTree.FromPolygons([b.verts[i] for i in used], [[remap[i] for i in f] for f in polys])
    out = {}
    names = sorted(trees)
    for i, a in enumerate(names):
        for c in names[i + 1:]:
            hits = trees[a].overlap(trees[c])
            if hits:
                out[f"{a}|{c}"] = len(hits)
    return out


def make_object(b):
    me = bpy.data.meshes.new(ASSET_ID)
    me.from_pydata([tuple(v) for v in b.verts], [], [list(f) for f in b.faces])
    me.update()
    atlas = me.uv_layers.new(name="UVMap")
    grain = me.uv_layers.new(name="grain")
    flat = np.array([c for f in b.fuv for uv in f for c in uv], dtype=np.float32)
    atlas.data.foreach_set("uv", flat)
    offs = []
    for fi, f in enumerate(b.fuv):
        ou, ov = b.parts[b.fpart[fi]]["off"]
        offs.extend([ou, ov] * len(f))
    grain.data.foreach_set("uv", flat + np.array(offs, dtype=np.float32))
    mats = [b.parts[b.fpart[i]]["atlas"] * 5 + b.fmat[i] for i in range(len(b.faces))]
    me.polygons.foreach_set("material_index", mats)
    for key, name in (("rand", "prand"), ("weather", "pweath"), ("mud", "pmud")):
        attr = me.attributes.new(name, "FLOAT", "FACE")
        attr.data.foreach_set("value", [float(b.parts[b.fpart[i]][key]) for i in range(len(b.faces))])
    me.uv_layers.active = atlas
    obj = bpy.data.objects.new(ASSET_ID, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def select_only(obj):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish_topology(obj):
    """Triangulate (beauty), mark sharp by angle, then face-area weighted custom normals so chamfers catch light."""
    me = obj.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(50.0))
    select_only(obj)
    mod = obj.modifiers.new("weighted", "WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.weight = 50
    mod.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=mod.name)


def uv_area_3d_area(me, polys):
    uv = me.uv_layers["UVMap"].data
    a2 = a3 = 0.0
    for p in polys:
        ls = list(p.loop_indices)
        u0 = uv[ls[0]].uv
        v0 = me.vertices[me.loops[ls[0]].vertex_index].co
        for k in range(1, len(ls) - 1):
            u1, u2 = uv[ls[k]].uv, uv[ls[k + 1]].uv
            a2 += abs((u1 - u0).cross(u2 - u0)) / 2
            v1 = me.vertices[me.loops[ls[k]].vertex_index].co
            v2 = me.vertices[me.loops[ls[k + 1]].vertex_index].co
            a3 += (v1 - v0).cross(v2 - v0).length / 2
    return a2, a3


def pack_atlases(obj, res):
    """Pack each atlas's islands (member-frame metres) into 0-1, then scale both to the same texel density."""
    me = obj.data
    scene = bpy.context.scene
    scene.tool_settings.use_uv_select_sync = True
    select_only(obj)
    me.uv_layers.active = me.uv_layers["UVMap"]
    stats = {}
    for atlas in (BODY, GEAR):
        mi = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get("material_index", mi)
        psel = (mi // 5) == atlas
        vsel = np.zeros(len(me.vertices), dtype=bool)
        lv = np.empty(len(me.loops), dtype=np.int32)
        me.loops.foreach_get("vertex_index", lv)
        ls = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get("loop_start", ls)
        lt = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get("loop_total", lt)
        vsel[lv[np.repeat(psel, lt)]] = True
        me.vertices.foreach_set("select", vsel)
        ev = np.empty(len(me.edges) * 2, dtype=np.int32)
        me.edges.foreach_get("vertices", ev)
        me.edges.foreach_set("select", vsel[ev.reshape(-1, 2)].all(axis=1))
        me.polygons.foreach_set("select", psel)
        me.update()
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=True, rotate_method="CARDINAL", scale=True,
                                margin_method="FRACTION", margin=6.0 / res, shape_method="CONCAVE")
        bpy.ops.object.mode_set(mode="OBJECT")
        polys = [p for p in me.polygons if p.material_index // 5 == atlas]
        a2, a3 = uv_area_3d_area(me, polys)
        stats[atlas] = {"uv_area": a2, "area_m2": a3, "px_per_m": math.sqrt(a2 / a3) * res}
    target = min(s["px_per_m"] for s in stats.values())
    uv = me.uv_layers["UVMap"].data
    for atlas in (BODY, GEAR):
        k = target / stats[atlas]["px_per_m"]
        if k < 0.999:
            for p in me.polygons:
                if p.material_index // 5 == atlas:
                    for li in p.loop_indices:
                        uv[li].uv = uv[li].uv * k
        stats[atlas]["px_per_m_final"] = target
    return stats


def uv_degenerate(me):
    uv = me.uv_layers["UVMap"].data
    bad = 0
    for p in me.polygons:
        ls = list(p.loop_indices)
        for k in range(1, len(ls) - 1):
            a, c, d = uv[ls[0]].uv, uv[ls[k]].uv, uv[ls[k + 1]].uv
            if abs((c - a).cross(d - a)) < 1e-11:
                bad += 1
    return bad


# --------------------------------------------------------------------------------------------------------------------
# Procedural shaders (baked)
# --------------------------------------------------------------------------------------------------------------------

def lin(r, g, b_):
    def f(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return (f(r), f(g), f(b_))


class Val:
    __slots__ = ("g", "s", "k")

    def __init__(self, g, s, k):
        self.g, self.s, self.k = g, s, k

    def __add__(self, o): return self.g.op("ADD", self, o)
    def __radd__(self, o): return self.g.op("ADD", o, self)
    def __sub__(self, o): return self.g.op("SUBTRACT", self, o)
    def __rsub__(self, o): return self.g.op("SUBTRACT", o, self)
    def __mul__(self, o): return self.g.op("MULTIPLY", self, o)
    def __rmul__(self, o): return self.g.op("MULTIPLY", o, self)
    def __truediv__(self, o): return self.g.op("DIVIDE", self, o)
    def __rtruediv__(self, o): return self.g.op("DIVIDE", o, self)
    def __neg__(self): return self.g.op("MULTIPLY", self, -1.0)


def kind(x):
    if isinstance(x, Val):
        return x.k
    return "v" if isinstance(x, (tuple, list)) else "f"


class Graph:
    def __init__(self, tree):
        self.t = tree

    def node(self, typ, **kw):
        n = self.t.nodes.new(typ)
        for k, v in kw.items():
            setattr(n, k, v)
        return n

    def put(self, sock, x):
        if isinstance(x, Val):
            self.t.links.new(x.s, sock)
            return
        if isinstance(x, (tuple, list)):
            x = tuple(float(c) for c in x)
            if len(sock.default_value) == 4 and len(x) == 3:
                x = x + (1.0,)
            sock.default_value = x
            return
        try:
            sock.default_value = float(x)
        except TypeError:
            n = len(sock.default_value)
            sock.default_value = (float(x),) * 3 + ((1.0,) if n == 4 else ())

    def op(self, name, a, c):
        if kind(a) == "f" and kind(c) == "f":
            n = self.node("ShaderNodeMath", operation=name)
            self.put(n.inputs[0], a)
            self.put(n.inputs[1], c)
            return Val(self, n.outputs[0], "f")
        n = self.node("ShaderNodeVectorMath", operation=name)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], c)
        return Val(self, n.outputs[0], "v")

    def m(self, name, a, c=0.0, clamp=False):
        n = self.node("ShaderNodeMath", operation=name, use_clamp=clamp)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], c)
        return Val(self, n.outputs[0], "f")

    def sin(self, a): return self.m("SINE", a)
    def abs(self, a): return self.m("ABSOLUTE", a)
    def max(self, a, c): return self.m("MAXIMUM", a, c)
    def min(self, a, c): return self.m("MINIMUM", a, c)
    def pow(self, a, c): return self.m("POWER", self.m("MAXIMUM", a, 0.0), c)
    def sat(self, a): return self.m("ADD", a, 0.0, clamp=True)
    def fract(self, a): return self.m("FRACT", a)

    def smooth(self, e0, e1, x):
        t = self.sat((x - e0) * (1.0 / (e1 - e0)))
        return t * t * (3.0 - 2.0 * t)

    def mix(self, a, c, t):
        if isinstance(a, tuple) and isinstance(c, tuple):
            return a + tuple(ci - ai for ai, ci in zip(a, c)) * t
        return a + (c - a) * t

    def vec(self, x, y, z):
        n = self.node("ShaderNodeCombineXYZ")
        for i, c in enumerate((x, y, z)):
            self.put(n.inputs[i], c)
        return Val(self, n.outputs[0], "v")

    def sep(self, v):
        n = self.node("ShaderNodeSeparateXYZ")
        self.put(n.inputs[0], v)
        return tuple(Val(self, n.outputs[i], "f") for i in range(3))

    def dot(self, a, c):
        n = self.node("ShaderNodeVectorMath", operation="DOT_PRODUCT")
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], c)
        return Val(self, n.outputs[1], "f")

    def noise(self, v, w, detail=2.0, rough=0.5):
        n = self.node("ShaderNodeTexNoise", noise_dimensions="4D", noise_type="FBM", normalize=True)
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["W"], w)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Detail"], detail)
        self.put(n.inputs["Roughness"], rough)
        return Val(self, n.outputs[0], "f")

    def voronoi(self, v, scale=1.0, jitter=1.0):
        n = self.node("ShaderNodeTexVoronoi", voronoi_dimensions="3D", feature="F1")
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["Scale"], scale)
        self.put(n.inputs["Randomness"], jitter)
        return Val(self, n.outputs["Distance"], "f")

    def ramp(self, t, stops):
        n = self.node("ShaderNodeValToRGB")
        els = n.color_ramp.elements
        while len(els) < len(stops):
            els.new(0.5)
        for e, (pos, col) in zip(els, stops):
            e.position = pos
            e.color = tuple(col) + (1.0,)
        self.put(n.inputs[0], t)
        return Val(self, n.outputs[0], "v")

    def attr(self, name):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Factor"], "f")


class Ctx:
    pass


def context(g):
    c = Ctx()
    uvn = g.node("ShaderNodeUVMap", uv_map="grain")
    c.u, c.v, _ = g.sep(Val(g, uvn.outputs["UV"], "v"))
    c.rand, c.weather, c.pmud = g.attr("prand"), g.attr("pweath"), g.attr("pmud")
    geo = g.node("ShaderNodeNewGeometry")
    c.pos = Val(g, geo.outputs["Position"], "v")
    c.nrm = Val(g, geo.outputs["Normal"], "v")
    _, _, c.pz = g.sep(c.pos)
    _, _, c.nz = g.sep(c.nrm)
    ao = g.node("ShaderNodeAmbientOcclusion", samples=16, only_local=False, inside=False)
    g.put(ao.inputs["Distance"], 0.35)
    c.ao = Val(g, ao.outputs["AO"], "f")
    bev = g.node("ShaderNodeBevel", samples=8)
    g.put(bev.inputs["Radius"], 0.007)
    c.edge = g.smooth(0.992, 0.93, g.dot(Val(g, bev.outputs["Normal"], "v"), c.nrm))
    c.cvx = c.edge * g.smooth(0.55, 0.9, c.ao)     # convex (exposed) edges only
    splash = g.noise(c.pos * 9.0, 3.1, detail=5.0, rough=0.6)
    caked = g.smooth(0.50 - c.pmud * 0.22, 0.62 - c.pmud * 0.2, splash)
    c.mud = g.sat(g.max(g.smooth(0.26, 0.02, c.pz) * g.smooth(0.40, 0.60, splash),
                        c.pmud * caked * g.smooth(0.95, 0.2, c.pz)))
    c.low = g.smooth(0.03, 0.75, c.pz)             # grime gathers toward the ground
    return c


def sh_wood(g, c):
    u, v, r, w = c.u, c.v, c.rand, c.weather
    wob = g.noise(g.vec(u * 0.9, v * 5.0, r * 17.0), 1.0, detail=3.0)
    wob2 = g.noise(g.vec(u * 4.0, v * 18.0, r * 13.0), 2.0, detail=2.0)
    ring_ = g.sin((v * 95.0 + wob * 8.0 + wob2 * 1.5) * TAU) * 0.5 + 0.5
    late = g.smooth(0.60, 0.95, ring_)
    # long fibres: the finest octave must stay long along the grain or the fibres read as dashes
    fibre = g.noise(g.vec(u * 1.2, v * 260.0, r * 29.0), 3.0, detail=2.0)
    streak = g.noise(g.vec(u * 0.4, v * 26.0, r * 31.0), 4.0, detail=3.0)
    dark = g.smooth(0.54, 0.74, g.noise(g.vec(u * 0.3, v * 12.0, r * 47.0), 9.0, detail=3.0))
    blotch = g.noise(g.vec(u * 1.2, v * 1.2, r * 37.0), 5.0, detail=4.0)
    cn = g.noise(g.vec(u * 0.8, v * 42.0, r * 41.0), 6.0, detail=2.0)
    cmask = g.smooth(0.46, 0.64, g.noise(g.vec(u * 0.7, v * 2.0, r * 43.0), 7.0, detail=2.0))
    crack = (1.0 - g.smooth(0.0, 0.024, g.abs(cn - 0.5))) * cmask
    knot = ((1.0 - g.smooth(0.025, 0.07, g.voronoi(g.vec(u * 2.5, v * 8.0, r * 50.0))))
            * g.smooth(0.62, 0.7, g.noise(g.vec(u * 0.9, v * 0.9, r * 61.0), 10.0, detail=2.0)))
    tone = g.ramp(r, [(0.0, lin(64, 58, 51)), (0.35, lin(86, 79, 70)), (0.7, lin(76, 66, 55)),
                      (1.0, lin(100, 93, 84))])
    base = tone * ((1.0 - late * 0.5) * (0.74 + fibre * 0.52) * (0.58 + streak * 0.84) * (0.80 + blotch * 0.40))
    base = base * (1.0 - dark * 0.45)
    expose = g.sat(c.nz * 0.5 + 0.5) * g.sat(w * 0.7 + 0.2) * c.ao
    base = g.mix(base, lin(124, 120, 112), expose * 0.30 * (1.0 - dark))
    base = g.mix(base, lin(38, 31, 26), knot * 0.85)
    base = base * (0.62 + 0.38 * c.low) * (0.22 + 0.78 * c.ao)
    base = g.mix(base, lin(30, 26, 22), crack * 0.9)
    base = g.mix(base, lin(142, 130, 112), c.cvx * 0.45)
    lich = (1.0 - g.smooth(0.05, 0.13, g.voronoi(c.pos * 13.0))) * g.smooth(0.58, 0.72, g.noise(c.pos * 3.0, 8.0))
    lich = lich * g.smooth(0.3, 0.8, c.nz) * g.smooth(0.3, 0.7, w)
    base = g.mix(base, lin(112, 116, 94), lich * 0.5)
    base = g.mix(base, lin(52, 43, 34), c.mud)
    rough = 0.80 + fibre * 0.07 - c.cvx * 0.10 + c.mud * 0.1 + crack * 0.05 - expose * 0.04
    height = (wob * 0.0010 - late * 0.0008 + fibre * 0.0004 + streak * 0.0005 - crack * 0.0024
              - c.cvx * 0.0005 + lich * 0.0002 - knot * 0.0008 + c.mud * 0.0006)
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def sh_break(g, c):
    u, v, r = c.u, c.v, c.rand
    fib = g.noise(g.vec(u * 90.0, v * 90.0, r * 7.0), 1.0, detail=4.0)
    grain = g.noise(g.vec(u * 14.0, v * 160.0, r * 9.0), 2.0, detail=3.0)
    base = g.mix(lin(150, 116, 82), lin(116, 92, 68), fib)
    base = base * (0.8 + grain * 0.4)
    base = g.mix(base, lin(118, 110, 98), 0.38)
    base = base * (0.45 + 0.55 * c.ao)
    base = g.mix(base, lin(66, 54, 41), c.mud * 0.8)
    return {"base": base, "rough": 0.86 + fib * 0.06, "metal": 0.0, "height": fib * 0.0012 + grain * 0.0006}


def sh_iron(g, c):
    u, v, r = c.u, c.v, c.rand
    p = c.pos * 1.0
    n1 = g.noise(g.vec(u * 5.0, v * 5.0, r * 11.0), 1.0, detail=4.0)
    n2 = g.noise(p * 38.0, 2.0 + r, detail=4.0)
    n3 = g.noise(p * 140.0, 3.0, detail=2.0)
    pits = 1.0 - g.smooth(0.0, 0.1, g.voronoi(p * 90.0))
    rust = g.smooth(0.56, 0.70, n1 * 0.6 + n2 * 0.35 + (1.0 - c.ao) * 0.3 + c.mud * 0.15)
    base = g.mix(lin(58, 58, 59), lin(78, 77, 75), n3)
    base = g.mix(base, g.mix(lin(74, 50, 36), lin(100, 66, 44), n2), rust)
    base = g.mix(base, lin(30, 27, 24), pits * 0.5 * rust)
    worn = c.cvx * (1.0 - rust * 0.7)
    base = g.mix(base, lin(138, 136, 132), worn * 0.7)
    base = base * (0.45 + 0.55 * c.ao) * (0.7 + 0.3 * c.low)
    base = g.mix(base, lin(52, 43, 34), c.mud * 0.85)
    metal = g.max(g.mix(0.75, 0.05, rust), worn * 0.95) * (1.0 - c.mud * 0.85)
    rough = g.mix(g.mix(g.mix(0.55, 0.9, rust), 0.35, worn) + n3 * 0.05, 0.92, c.mud)
    height = (n1 * 0.0003 + rust * (n2 * 0.0004 + 0.0002) - pits * rust * 0.0004 + n3 * 0.00006
              + c.mud * 0.0008)
    return {"base": base, "rough": rough, "metal": metal, "height": height}


def sh_rope(g, c):
    u, v, r = c.u, c.v, c.rand
    phase = u * (1.0 / 0.024) + v * (3.0 / 0.0535)
    strand = g.sin(phase * TAU) * 0.5 + 0.5
    fuzz = g.noise(g.vec(u * 200.0, v * 200.0, r), 1.0, detail=3.0)
    base = g.mix(lin(72, 62, 48), lin(118, 103, 80), strand * 0.7 + fuzz * 0.3)
    base = g.mix(base, lin(108, 101, 90), 0.3)
    base = base * (0.4 + 0.6 * c.ao)
    base = g.mix(base, lin(66, 54, 41), c.mud)
    return {"base": base, "rough": 0.9, "metal": 0.0, "height": strand * 0.0014 + fuzz * 0.0003}


def sh_cloth(g, c):
    u, v, r = c.u, c.v, c.rand
    weave = (g.sin(u * TAU * 260.0) * g.sin(v * TAU * 260.0)) * 0.5 + 0.5
    folds = g.noise(g.vec(u * 2.5, v * 6.0, r * 5.0), 1.0, detail=3.0)
    blot = g.noise(g.vec(u * 1.5, v * 3.0, r * 3.0), 2.0, detail=4.0)
    base = g.mix(lin(50, 52, 44), lin(70, 68, 56), blot)
    base = base * (0.85 + folds * 0.3)
    stain = g.smooth(0.55, 0.75, g.noise(g.vec(u * 4.0, v * 8.0, r * 9.0), 3.0, detail=4.0))
    base = g.mix(base, lin(44, 40, 33), stain * 0.6)
    base = g.mix(base, lin(120, 114, 100), g.smooth(0.4, 0.95, c.nz) * 0.18)
    base = base * (0.45 + 0.55 * c.ao)
    return {"base": base, "rough": 0.84 + weave * 0.05, "metal": 0.0,
            "height": folds * 0.004 + weave * 0.00018 + blot * 0.001}


SHADERS = {WOOD: sh_wood, IRON: sh_iron, BREAK: sh_break, ROPE: sh_rope, CLOTH: sh_cloth}


def bake_material(name, k, image):
    mat = bpy.data.materials.new(name)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    g = Graph(tree)
    c = context(g)
    res = SHADERS[k](g, c)
    out = g.node("ShaderNodeOutputMaterial")
    e_col = g.node("ShaderNodeEmission")
    g.put(e_col.inputs["Color"], res["base"])
    g.put(e_col.inputs["Strength"], 1.0)
    e_orm = g.node("ShaderNodeEmission")
    g.put(e_orm.inputs["Color"], g.vec(g.pow(c.ao, 1.15), g.sat(res["rough"]), g.sat(res["metal"])))
    g.put(e_orm.inputs["Strength"], 1.0)
    bump = g.node("ShaderNodeBump")
    g.put(bump.inputs["Strength"], 1.0)
    g.put(bump.inputs["Distance"], 1.0)
    g.put(bump.inputs["Filter Width"], 1.0)
    g.put(bump.inputs["Height"], res["height"])
    diffuse = g.node("ShaderNodeBsdfDiffuse")
    tree.links.new(bump.outputs["Normal"], diffuse.inputs["Normal"])
    img = g.node("ShaderNodeTexImage")
    img.image = image
    tree.nodes.active = img
    return mat, {"out": out, "colour": e_col.outputs[0], "orm": e_orm.outputs[0], "normal": diffuse.outputs[0],
                 "tree": tree}


def set_device(scene, device):
    scene.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    if device != "cpu":
        for dt in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = dt
                prefs.get_devices()
                if any(d.type == dt for d in prefs.devices):
                    for d in prefs.devices:
                        d.use = d.type == dt
                    scene.cycles.device = "GPU"
                    return dt
            except TypeError:
                continue
    prefs.compute_device_type = "NONE"
    scene.cycles.device = "CPU"
    return "CPU"


def run_bake(kind_, device_used):
    scene = bpy.context.scene

    def go():
        if kind_ == "normal":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
                                normal_b="POS_Z", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
        else:
            bpy.ops.object.bake(type="EMIT", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
    try:
        go()
    except RuntimeError as err:
        if scene.cycles.device != "GPU":
            raise
        print(f"  GPU bake failed ({str(err)[:120]}); retrying on the CPU", flush=True)
        set_device(scene, "cpu")
        device_used.add("CPU")
        go()


def to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def save_png(arr, res, path, colourspace):
    img = bpy.data.images.new(os.path.basename(path), res, res, alpha=False)
    img.colorspace_settings.name = colourspace
    rgba = np.ones((res, res, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(arr, 0.0, 1.0)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def bake_textures(obj, res, samples, device, work):
    scene = bpy.context.scene
    dev = set_device(scene, device)
    device_used = {scene.cycles.device if dev == "CPU" else dev}
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.seed = 0
    scene.render.bake.use_selected_to_active = False
    # a ground plane so the baked occlusion includes the ground under the cart; not exported
    bpy.ops.mesh.primitive_plane_add(size=12.0, location=(0, 0, 0))
    plane = bpy.context.active_object
    plane.name = "_ao_ground"
    images, mats, shaders = {}, [], []
    for atlas in (BODY, GEAR):
        im = bpy.data.images.new(f"bake_{ATLASES[atlas]}", res, res, alpha=False, float_buffer=True)
        im.colorspace_settings.name = "Non-Color"
        images[atlas] = im
    for atlas in (BODY, GEAR):
        for k in range(5):
            mat, sh = bake_material(f"bake_{ATLASES[atlas]}_{KINDS[k]}", k, images[atlas])
            mats.append(mat)
            shaders.append(sh)
    me = obj.data
    keep = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", keep)
    me.materials.clear()                 # clearing the slots resets every face's index, so restore it
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", keep)
    me.update()
    select_only(obj)
    out = {}
    timings = {}
    for pass_ in ("colour", "orm", "normal"):
        for sh in shaders:
            tree = sh["tree"]
            for link in list(sh["out"].inputs["Surface"].links):
                tree.links.remove(link)
            tree.links.new(sh[pass_], sh["out"].inputs["Surface"])
        t0 = time.time()
        run_bake(pass_, device_used)
        timings[pass_] = round(time.time() - t0, 1)
        for atlas in (BODY, GEAR):
            px = np.empty(res * res * 4, dtype=np.float32)
            images[atlas].pixels.foreach_get(px)
            out[(atlas, pass_)] = px.reshape(res, res, 4)[:, :, :3].copy()
        print(f"  baked {pass_} in {timings[pass_]} s", flush=True)
    bpy.data.objects.remove(plane)
    paths = {}
    for atlas in (BODY, GEAR):
        col, orm, nrm = out[(atlas, "colour")], out[(atlas, "orm")], out[(atlas, "normal")]
        mask = (col.sum(axis=2) > 1e-6) | (orm.sum(axis=2) > 1e-6)   # texels the emit bakes wrote (islands + margin)
        for arr, fill in ((col, None), (orm, None), (nrm, (0.5, 0.5, 1.0))):
            arr[~mask] = fill if fill is not None else arr[mask].mean(axis=0)
        stem = os.path.join(work, f"{ASSET_ID}_{ATLASES[atlas]}")
        paths[atlas] = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
        save_png(to_srgb(col), res, paths[atlas]["basecolor"], "sRGB")
        save_png(orm, res, paths[atlas]["orm"], "Non-Color")
        save_png(nrm, res, paths[atlas]["normal"], "Non-Color")
        paths[atlas]["coverage"] = float(mask.mean())
    return paths, timings, sorted(device_used)


def gltf_output_group():
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is None:
        ng = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.nodes.new("NodeGroupInput")
    return ng


def final_materials(obj, paths):
    mats = []
    for atlas in (BODY, GEAR):
        mat = bpy.data.materials.new(f"MAT_{ASSET_ID}_{ATLASES[atlas]}")
        tree = mat.node_tree
        for n in list(tree.nodes):
            tree.nodes.remove(n)
        out = tree.nodes.new("ShaderNodeOutputMaterial")
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
        tree.links.new(bsdf.outputs[0], out.inputs["Surface"])
        col = tree.nodes.new("ShaderNodeTexImage")
        col.image = bpy.data.images.load(paths[atlas]["basecolor"])
        col.image.colorspace_settings.name = "sRGB"
        tree.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
        orm = tree.nodes.new("ShaderNodeTexImage")
        orm.image = bpy.data.images.load(paths[atlas]["orm"])
        orm.image.colorspace_settings.name = "Non-Color"
        sep = tree.nodes.new("ShaderNodeSeparateColor")
        tree.links.new(orm.outputs["Color"], sep.inputs[0])
        tree.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
        tree.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
        grp = tree.nodes.new("ShaderNodeGroup")
        grp.node_tree = gltf_output_group()
        tree.links.new(sep.outputs[0], grp.inputs["Occlusion"])
        nrm = tree.nodes.new("ShaderNodeTexImage")
        nrm.image = bpy.data.images.load(paths[atlas]["normal"])
        nrm.image.colorspace_settings.name = "Non-Color"
        nmap = tree.nodes.new("ShaderNodeNormalMap")
        tree.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
        tree.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        mat.use_backface_culling = True     # every part is a closed solid: single-sided in glTF
        mats.append(mat)
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", (idx // 5).astype(np.int32))
    me.update()


def flat_materials(obj):
    """--no-bake: plain colours per shader kind, for fast geometry review."""
    cols = {WOOD: (0.12, 0.105, 0.09), IRON: (0.04, 0.04, 0.04), BREAK: (0.3, 0.2, 0.12), ROPE: (0.16, 0.13, 0.09),
            CLOTH: (0.06, 0.065, 0.05)}
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for k in range(5):
        mat = bpy.data.materials.new(f"MAT_flat_{KINDS[k]}")
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = cols[k] + (1.0,)
        bsdf.inputs["Roughness"].default_value = 0.8
        bsdf.inputs["Metallic"].default_value = 0.7 if k == IRON else 0.0
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", (idx % 5).astype(np.int32))
    me.update()


# --------------------------------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    ap.add_argument("--work-dir", default=os.path.join(tempfile.gettempdir(), ASSET_ID + "_procgen"))
    ap.add_argument("--seed", type=int, default=1307)
    ap.add_argument("--res", type=int, default=2048)
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--device", default="auto", choices=("auto", "cpu"))
    ap.add_argument("--no-bake", action="store_true")
    return ap.parse_args(argv)


def gltf_vec(v):
    """Blender Z-up to glTF Y-up (x, z, -y)."""
    return [round(v.x, 5), round(v.z, 5), round(-v.y, 5)]


def main():
    args = parse_args()
    args.out_dir = os.path.abspath(args.out_dir)
    args.work_dir = os.path.abspath(args.work_dir)
    t_start = time.time()
    os.makedirs(args.out_dir, exist_ok=True)
    os.makedirs(args.work_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rng = random.Random(args.seed)
    P = dict(PARAMS)
    b = Builder(rng)
    info = build_cart(b, P, rng)

    # centre on X/Y (glTF X/Z) with the lowest point at z = 0; check the long side against 3.0 m first
    xs = [v.x for v in b.verts]
    ys = [v.y for v in b.verts]
    zs = [v.z for v in b.verts]
    shift = Vector((-(min(xs) + max(xs)) / 2, -(min(ys) + max(ys)) / 2, -min(zs)))
    b.verts = [v + shift for v in b.verts]
    dims = Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))

    topo = validate_builder(b)
    clashes = clash_report(b)
    print("TOPOLOGY", json.dumps(topo))
    print("CLASHES", json.dumps(clashes))

    obj = make_object(b)
    finish_topology(obj)
    tris = len(obj.data.polygons)
    uv_stats = pack_atlases(obj, args.res)
    uv_bad = uv_degenerate(obj.data)

    # the bow's socket on the bed floor, basis from the bed frame
    M_bed = Matrix.Translation(shift) @ info["M_bed"]
    bed_x = (M_bed.to_3x3() @ X).normalized()
    bed_n = (M_bed.to_3x3() @ Z).normalized()
    sock_pos = M_bed @ Vector((BOW_BED_XY[0], BOW_BED_XY[1], info["z_floor_top"] + 0.003))
    world_bow = Vector((0.0, -BOW_OFFSET_GLTF_Z, sock_pos.z))   # the world pickup point (157, 162), model frame
    sock = bpy.data.objects.new("SOCK_item", None)
    sock.empty_display_type = "ARROWS"
    sock.empty_display_size = 0.15
    basis = Matrix((bed_x, bed_n.cross(bed_x), bed_n)).transposed()
    sock.matrix_world = Matrix.Translation(sock_pos) @ basis.to_4x4()
    bpy.context.scene.collection.objects.link(sock)
    sock.parent = obj

    textures = {}
    timings = {}
    devices = []
    if args.no_bake:
        flat_materials(obj)
    else:
        paths, timings, devices = bake_textures(obj, args.res, args.samples, args.device, args.work_dir)
        final_materials(obj, paths)
        textures = {ATLASES[a]: {k: os.path.basename(v) if isinstance(v, str) else v for k, v in paths[a].items()}
                    for a in paths}
    me = obj.data
    me.uv_layers.remove(me.uv_layers["grain"])
    for name in ("prand", "pweath", "pmud"):
        me.attributes.remove(me.attributes[name])

    glb = os.path.join(args.out_dir, ASSET_ID + ".glb")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.work_dir, ASSET_ID + ".blend"))
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True, export_tangents=False,
                              export_image_format="AUTO", use_selection=False)

    fits = {
        "x_between_2.0_and_3.0": 2.0 - 1e-3 <= dims.x <= 3.0 + 1e-3,
        "z_between_1.0_and_2.0": 1.0 - 1e-3 <= dims.y <= 2.0 + 1e-3,
        "height_at_least_0.8": dims.z >= 0.8,
        "height_within_1.4": dims.z <= 1.4,
    }
    concept_sha = hashlib.sha256(open(CONCEPT, "rb").read()).hexdigest() if os.path.exists(CONCEPT) else None
    script_sha = hashlib.sha256(open(os.path.abspath(__file__), "rb").read()).hexdigest()
    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_cart.py",
        "script_sha256": script_sha,
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "parameters": P,
        "concept": "assets/concepts/prop_cart_damaged_merchant.png",
        "concept_sha256": concept_sha,
        "design_notes": [
            "Four wheels because the concept shows four (the semantic rule says two-wheeled).",
            "Damage: near fore wheel collapsed (tyre sprung off, felloes and a spoke on the ground), fore axle rolls, "
            "bed tilts toward the player and the shafts; near side broken-out bay with the board on the ground; two "
            "floor planks splintered at the front; both shafts snapped; a loose plank in front; rope from the front "
            "cross-beam. No cover: the concept shows none.",
            "Concept tyres read like treaded rubber; built as iron tyres per the brief.",
        ],
        "frame": "glTF +Y up, front +Z (the tilted, damaged side), long side on X, centred on X/Z, lowest point y=0",
        "dimensions_m": {"x": round(dims.x, 4), "y_height": round(dims.z, 4), "z": round(dims.y, 4)},
        "blocker": BLOCKER,
        "fits_blocker": fits,
        "tilt": {k: round(info[k], 2) for k in ("bed_tilt_deg", "bed_pitch_deg", "fore_axle_roll_deg")},
        "triangles": tris,
        "triangle_budget": 40000,
        "topology_check": topo,
        "uv_degenerate_triangles": uv_bad,
        "clashes_between_groups": clashes,
        "materials": {
            "source": "own procedural PBR shaders (weathered oak, fresh break, rusted wrought iron, hemp rope, "
                      "oilcloth), baked with Cycles; the staging world materials were not used",
            "atlases": {ATLASES[a]: {"material": f"MAT_{ASSET_ID}_{ATLASES[a]}", "size": args.res,
                                     "px_per_m": round(uv_stats[a]["px_per_m_final"], 1),
                                     "surface_m2": round(uv_stats[a]["area_m2"], 3)} for a in (BODY, GEAR)},
            "maps": "base colour (sRGB), normal (tangent, OpenGL +Y), ORM (R occlusion, G roughness, B metallic)",
            "textures": textures,
            "bake": {"samples": args.samples, "devices": devices, "seconds": timings} if not args.no_bake else None,
        },
        "sockets": {"SOCK_item": {
            "position": gltf_vec(sock_pos), "primary_y": gltf_vec(bed_n), "along_x": gltf_vec(bed_x),
            "role": "item", "note": "where the hunting bow lies on the bed floor; +Y out of the floor, +X along the bed",
            "horizontal_offset_from_world_bow_point_m": round((sock_pos - world_bow).length, 3)}},
        "outputs": [ASSET_ID + ".glb", ASSET_ID + "_provenance.json"],
        "build_seconds": round(time.time() - t_start, 1),
    }
    with open(os.path.join(args.out_dir, ASSET_ID + "_provenance.json"), "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    print("RESULT", json.dumps({"glb": glb, "dims": prov["dimensions_m"], "tris": tris, "tilt": prov["tilt"],
                                "fits": fits, "px_per_m": {ATLASES[a]: prov["materials"]["atlases"][ATLASES[a]]["px_per_m"]
                                                           for a in (BODY, GEAR)}}))


if __name__ == "__main__":
    main()
