"""Procedural build of container_chest_iron_banded: an oak chest bound in iron, with a lid that opens (hero prop).

The image-to-3D reconstruction in assets/ready is squashed and smeared (levelled it is 0.72 x 0.39 x 0.41 m with
blurred sides), so the chest is built here from real member sizes in headless Blender 5.2, baked to PBR textures and
exported as a GLB. The whole build is deterministic from --seed.

Design follows the concept (assets/concepts/container_chest_iron_banded.png): a flat-lidded chest of dark oak boards,
0.80 x 0.50 m and about 0.57 m tall. The body is three boards high on every side, the lowest cut away between bracket
feet at the corners, over a three-board floor; the lid is a frame of four rails under four top boards. Iron: a band
round the body at the lid seam and one low down, angle irons up the body corners, folded caps over the four foot
corners and the four lid corners, a strap over each lid end, domed rivets throughout and a big stud on each strap.
On the front a hasp hangs from a plate on the lid, over the seam band, onto a lock plate with a staple through the
hasp slot; at the back two three-knuckle hinges carry the lid.

The body is hollow (the lid opens onto it). The lid is a separate child node named 'lid' whose origin lies on the hinge
axis at the back top edge, so the game can swing it open: in glTF the axis is the node's local X, and a negative
rotation about X lifts the front.

Every member is a closed solid (outward winding, chamfered edges), unwrapped in its own frame at one texel density.
The wood shader reads each vertex's position in its member's frame (x along the grain) and grows rings round a
seeded pith line, so figure runs along each board and end grain shows rings. Shaders (dark oak, blackened wrought
iron) are baked with Cycles into one 2048 atlas: base colour (sRGB), tangent normal (OpenGL) and ORM, embedded in
the GLB. The lid is baked lifted clear of the body, so the inside of the chest is baked open and lit.

Frames: built in Blender Z-up with the width on X and the front on -Y. The glTF export (+Y up) turns that into front
+Z, centred on X/Z, lowest point y = 0.

Usage:
  blender --background --factory-startup --python _procgen_chest.py -- [--out-dir DIR] [--seed N] [--res 2048]
          [--samples 24] [--device auto|cpu] [--no-bake] [--work-dir DIR]

Writes <out-dir>/container_chest_iron_banded.glb and container_chest_iron_banded_provenance.json. Textures and a
.blend go to --work-dir (default: a temp folder), not to the staging folder. The last line is "RESULT {json}".
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

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ASSET_ID = "container_chest_iron_banded"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
CONCEPT = os.path.join(REPO, "assets", "concepts", ASSET_ID + ".png")
DEFAULT_OUT = os.path.join(REPO, "assets", "_staging", "procedural", ASSET_ID)
TAU = 2.0 * math.pi
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

WOOD, IRON = 0, 1
KINDS = ("wood", "iron")
BOX, LID = 0, 1
GROUPS = ("box", "lid")
LIFT = 1.5          # the lid is baked this far above the body so neither shades the other
TRI_BUDGET = 20000

PARAMS = {
    # wood carcass (outer faces of the boards)
    "width": 0.788, "depth": 0.488, "board_t": 0.022, "body_h": 0.400, "rows": [0.0, 0.100, 0.250, 0.400],
    "board_gap": 0.0008, "foot_w": 0.085, "foot_clear": 0.035, "floor_z": 0.040,
    "seam_gap": 0.0015, "lid_rail_h": 0.139, "lid_boards": 4,
    "chamfer": 0.0015, "lid_edge_chamfer": 0.006, "end_chamfer": 0.0012,
    # iron
    "band_t": 0.004, "seam_band": (0.352, 0.392), "low_band": (0.058, 0.092),
    "angle_t": 0.004, "angle_leg": 0.045, "angle_clip": 0.005, "angle_z": (0.095, 0.3513),
    "cap_t": 0.007, "cap_leg": 0.075, "cap_clip": 0.009, "foot_cap_h": 0.110, "lid_cap_h": 0.075,
    "strap_t": 0.004, "strap_w": 0.045, "strap_inset": 0.005, "iron_chamfer": 0.0008,
    "rivet_r": 0.0055, "rivet_h": 0.0035, "stud_r": 0.011, "stud_h": 0.006,
    "hinge_x": 0.23, "hinge_r": 0.0075, "hinge_leaf_w": 0.045, "hinge_leaf_t": 0.006,
    # texturing
    "texel_target_px_per_m": 512,
}


# --------------------------------------------------------------------------------------------------------------------
# Geometry kernel: closed solids with member-frame coordinates
# --------------------------------------------------------------------------------------------------------------------

class Builder:
    """Vertices, faces, per-corner UVs in member-frame metres, per-vertex member-frame positions (for the grain) and
    per-part attributes. Each part is closed and never shares vertices with another."""

    def __init__(self, rng):
        self.rng = rng
        self.verts, self.lpos, self.vpart = [], [], []
        self.faces, self.fuv, self.fpart, self.fend = [], [], [], []
        self.parts = []
        self.M = Matrix.Identity(4)

    def part(self, name, group, mat, M, wood=None):
        r = self.rng
        self.M = M
        p = {"name": name, "group": group, "mat": mat, "rand": r.random(), "v0": len(self.verts),
             "f0": len(self.faces)}
        if wood is None:
            p["pith"] = (0.0, 0.0, p["rand"])
            p["slope"] = (0.0, 0.0, 0.004)
            p["world_grain"] = True
        else:
            hy, hz = wood
            if r.random() < 0.7:          # flat-sawn: pith off a broad face, cathedral figure on it
                py0 = r.uniform(-0.8, 0.8) * hy
                pz0 = r.choice((-1, 1)) * r.uniform(0.14, 0.34)
            else:                         # rift/quarter: pith off an edge, straight grain
                py0 = r.choice((-1, 1)) * r.uniform(0.09, 0.25)
                pz0 = r.uniform(-1.5, 1.5) * hz
            p["pith"] = (py0, pz0, p["rand"])
            p["slope"] = (r.gauss(0, 0.012), r.gauss(0, 0.012), r.uniform(0.0022, 0.0036))
            p["world_grain"] = False
        self.parts.append(p)
        return p

    def v(self, local):
        local = Vector(local)
        w = self.M @ local
        self.verts.append(w)
        self.lpos.append(tuple(w) if self.parts[-1]["world_grain"] else tuple(local))
        self.vpart.append(len(self.parts) - 1)
        return len(self.verts) - 1

    def f(self, idx, uv, end=0.0):
        self.fend.append(end)
        self.faces.append(tuple(idx))
        self.fuv.append(tuple((float(a), float(c)) for a, c in uv))
        self.fpart.append(len(self.parts) - 1)

    def close(self):
        """Turn the last part outward if its volume came out negative (a mirrored frame or a CW profile)."""
        p = self.parts[-1]
        if signed_volume(self, p["f0"], len(self.faces)) < 0:
            for fi in range(p["f0"], len(self.faces)):
                self.faces[fi] = tuple(reversed(self.faces[fi]))
                self.fuv[fi] = tuple(reversed(self.fuv[fi]))


def signed_volume(b, f0, f1):
    vol = 0.0
    for fi in range(f0, f1):
        idx = b.faces[fi]
        p0 = b.verts[idx[0]]
        for k in range(1, len(idx) - 1):
            vol += p0.dot(b.verts[idx[k]].cross(b.verts[idx[k + 1]])) / 6.0
    return vol


def frame(origin, ex, ey):
    """4x4 from an origin and two orthonormal axes (ez = ex x ey, right-handed)."""
    ex, ey = Vector(ex).normalized(), Vector(ey).normalized()
    ez = ex.cross(ey)
    M = Matrix.Identity(4)
    for i in range(3):
        M[i][0], M[i][1], M[i][2], M[i][3] = ex[i], ey[i], ez[i], origin[i]
    return M


def ring_uv_lengths(ring):
    acc = [0.0]
    n = len(ring)
    for j in range(n):
        acc.append(acc[-1] + (ring[(j + 1) % n] - ring[j]).length)
    return acc


def loft(b, rings, us, cap_axes=None, closed=False, end_caps=False):
    """Quads between consecutive rings (each ring CCW about the loft direction), flat n-gon caps. UVs: u along the
    loft, v round the ring (one island), caps projected on cap_axes."""
    n, m = len(rings[0]), len(rings)
    idx = [[b.v(p) for p in ring] for ring in rings]
    vc = [ring_uv_lengths(r) for r in rings]
    for i in range(m if closed else m - 1):
        i1 = (i + 1) % m
        u0, u1 = us[i], us[i + 1]
        for j in range(n):
            j1 = (j + 1) % n
            b.f((idx[i][j], idx[i][j1], idx[i1][j1], idx[i1][j]),
                [(u0, vc[i][j]), (u0, vc[i][j + 1]), (u1, vc[i1][j + 1]), (u1, vc[i1][j])])
    if not closed:
        a, c = cap_axes
        order = list(reversed(range(n)))
        e = 1.0 if end_caps else 0.0
        b.f([idx[0][j] for j in order], [(rings[0][j].dot(a), rings[0][j].dot(c)) for j in order], e)
        b.f([idx[-1][j] for j in range(n)], [(rings[-1][j].dot(a), rings[-1][j].dot(c)) for j in range(n)], e)


def rect_prof(hy, hz, cs):
    """Chamfered rectangle in (y, z), CCW seen from +x. cs = chamfers at (+y+z, -y+z, -y-z, +y-z)."""
    cpp, cmp_, cmm, cpm = cs
    return [(hy, -hz + cpm), (hy, hz - cpp), (hy - cpp, hz), (-hy + cmp_, hz), (-hy, hz - cmp_), (-hy, -hz + cmm),
            (-hy + cmm, -hz), (hy - cpm, -hz)]


def rect_inset(hy, hz, cs, e):
    """rect_prof shrunk by e on every side with the same vertex count; each chamfer shrinks by (2 - sqrt 2) e (a true
    offset) and stops at 0.3 mm, so a narrow chamfer never turns inside out."""
    k = (2.0 - math.sqrt(2.0)) * e
    return rect_prof(hy - e, hz - e, [max(c - k, 0.0003) for c in cs])


def beam(b, name, group, M, L, hy, hz, cs, ce, wood=True, mat=WOOD):
    """Board or bar along local x (centred), section 2hy (local y) x 2hz (local z), every edge chamfered."""
    b.part(name, group, mat, M, wood=(hy, hz) if wood else None)
    full = rect_prof(hy, hz, cs)
    ins = rect_inset(hy, hz, cs, ce)
    xs = [(-L / 2, ins), (-L / 2 + ce, full), (L / 2 - ce, full), (L / 2, ins)]
    rings = [[Vector((x, y, z)) for y, z in prof] for x, prof in xs]
    loft(b, rings, [x for x, _ in xs], cap_axes=(Y, Z), end_caps=wood)
    b.close()


def poly_area(poly):
    return 0.5 * sum(poly[i - 1][0] * poly[i][1] - poly[i][0] * poly[i - 1][1] for i in range(len(poly)))


def ccw(poly):
    return list(poly) if poly_area(poly) > 0 else list(reversed(poly))


def offset_poly(poly, e):
    """Offset a CCW polygon inward by e (outward for e < 0): each vertex where its two offset edge lines meet."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(poly[i - 1]), Vector(poly[i]), Vector(poly[(i + 1) % n])
        d1, d2 = (p1 - p0).normalized(), (p2 - p1).normalized()
        n1, n2 = Vector((-d1.y, d1.x)), Vector((-d2.y, d2.x))
        a, c = p1 + n1 * e, p1 + n2 * e
        den = d1.x * d2.y - d1.y * d2.x
        if abs(den) < 1e-9:
            out.append((a.x, a.y))
            continue
        w = c - a
        t = (w.x * d2.y - w.y * d2.x) / den
        q = a + d1 * t
        out.append((q.x, q.y))
    return out


def prism(b, name, group, M, poly, t, c, mat=IRON, wood=None, hole=None):
    """A polygon in local (x, y) extruded along local z from -t/2 to t/2, its perimeter chamfered by c on both faces.
    `hole` (same vertex count, CCW) cuts a through-hole whose caps join the outer ring quad by quad."""
    poly = ccw(poly)
    b.part(name, group, mat, M, wood=wood)
    zs = [(-t / 2, c), (-t / 2 + c, 0.0), (t / 2 - c, 0.0), (t / 2, c)]
    rings = [[Vector((x, y, z)) for x, y in offset_poly(poly, e)] for z, e in zs]
    us = [z for z, _ in zs]
    if hole is None:
        loft(b, rings, us, cap_axes=(X, Y))
        b.close()
        return
    hole = ccw(hole)
    n = len(poly)
    assert len(hole) == n
    hrings = [[Vector((x, y, z)) for x, y in offset_poly(hole, -e)] for z, e in zs]
    oi = [[b.v(p) for p in r] for r in rings]
    hi = [[b.v(p) for p in r] for r in hrings]
    ovc = [ring_uv_lengths(r) for r in rings]
    hvc = [ring_uv_lengths(r) for r in hrings]
    for i in range(3):
        for j in range(n):
            j1 = (j + 1) % n
            b.f((oi[i][j], oi[i][j1], oi[i + 1][j1], oi[i + 1][j]),
                [(us[i], ovc[i][j]), (us[i], ovc[i][j + 1]), (us[i + 1], ovc[i + 1][j + 1]), (us[i + 1], ovc[i + 1][j])])
            b.f((hi[i][j1], hi[i][j], hi[i + 1][j], hi[i + 1][j1]),
                [(us[i], hvc[i][j + 1] + 1.0), (us[i], hvc[i][j] + 1.0), (us[i + 1], hvc[i + 1][j] + 1.0),
                 (us[i + 1], hvc[i + 1][j + 1] + 1.0)])
    for j in range(n):
        j1 = (j + 1) % n
        for ring_o, ring_h, pts_o, pts_h, bottom in ((oi[0], hi[0], rings[0], hrings[0], True),
                                                     (oi[3], hi[3], rings[3], hrings[3], False)):
            q = (ring_o[j1], ring_o[j], ring_h[j], ring_h[j1]) if bottom else (ring_o[j], ring_o[j1], ring_h[j1], ring_h[j])
            pts = {ring_o[j]: pts_o[j], ring_o[j1]: pts_o[j1], ring_h[j]: pts_h[j], ring_h[j1]: pts_h[j1]}
            b.f(q, [(pts[k].x, pts[k].y) for k in q])
    b.close()


def lathe(b, name, group, C, A, profile, n=8, mat=IRON):
    """Solid of revolution about axis A from C; profile [(u along A, radius)]."""
    A = Vector(A).normalized()
    ref = X if abs(A.x) < 0.9 else Y
    ey = (ref - A * ref.dot(A)).normalized()
    b.part(name, group, mat, frame(Vector(C), A, ey))
    rings = [[Vector((u, r * math.cos(TAU * k / n), r * math.sin(TAU * k / n))) for k in range(n)] for u, r in profile]
    loft(b, rings, [u for u, _ in profile], cap_axes=(Y, Z))
    b.close()


def rivet(b, group, p, normal, r=None, h=None, n=8):
    r = r or PARAMS["rivet_r"]
    h = h or PARAMS["rivet_h"]
    lathe(b, "rivet", group, Vector(p) - Vector(normal).normalized() * 0.0008, normal,
          [(0.0, r), (0.0008 + h * 0.45, r * 0.86), (0.0008 + h * 0.8, r * 0.52), (0.0008 + h, r * 0.14)], n=n)


def band_loop(b, name, group, hx, hy, z0, z1, t, c):
    """Flat iron band round a hx x hy (half sizes) rectangle, mitred at the corners, section chamfered."""
    b.part(name, group, IRON, Matrix.Identity(4))
    w = z1 - z0
    zc = (z0 + z1) / 2
    ox, oy = hx + t / 2, hy + t / 2
    path = [Vector((ox, -oy, zc)), Vector((ox, oy, zc)), Vector((-ox, oy, zc)), Vector((-ox, -oy, zc))]
    sec = rect_prof(t / 2, w / 2, (c, c, c, c))   # (s across the band thickness, t up the band)
    rings, us, u = [], [], 0.0
    m = len(path)
    for i in range(m):
        d0 = (path[i] - path[i - 1]).normalized()
        d1 = (path[(i + 1) % m] - path[i]).normalized()
        T = (d0 + d1).normalized()
        N = Z.cross(T).normalized()
        ks = 1.0 / max(0.3, T.dot(d0))
        rings.append([path[i] + N * (s * ks) + Z * tt for s, tt in sec])
        if i:
            u += (path[i] - path[i - 1]).length
        us.append(u)
    us.append(u + (path[0] - path[-1]).length)
    loft(b, rings, us, closed=True)
    b.close()


# --------------------------------------------------------------------------------------------------------------------
# Chest
# --------------------------------------------------------------------------------------------------------------------

def corner_map(sx, sy, W, D):
    """Canonical corner frame -> world XY: the wood lies at x < 0 and y > 0 from the corner."""
    cx, cy = sx * W / 2, sy * D / 2

    def f(pts):
        return [(cx + sx * x, cy - sy * y) for x, y in pts]
    return f


def angle_poly(a, t, k):
    """L section round a corner: legs a along each face, thickness t, the outer corner clipped by k."""
    return [(-a, -t), (t - k, -t), (t, -t + k), (t, a), (0.0, a), (0.0, 0.0), (-a, 0.0)]


def skirt_poly(L, h, foot, clear, n_arc=6):
    """Lowest side board: bracket feet at both ends, the span between cut up to `clear` with a cove at each foot."""
    pts = [(-L / 2, 0.0), (-L / 2 + foot, 0.0)]
    for k in range(1, n_arc):
        a = (math.pi / 2) * k / n_arc
        pts.append((-L / 2 + foot + clear * math.sin(a), clear - clear * math.cos(a)))
    pts.append((-L / 2 + foot + clear, clear))
    pts.append((L / 2 - foot - clear, clear))
    for k in range(n_arc - 1, 0, -1):
        a = (math.pi / 2) * k / n_arc
        pts.append((L / 2 - foot - clear * math.sin(a), clear - clear * math.cos(a)))
    pts += [(L / 2 - foot, 0.0), (L / 2, 0.0), (L / 2, h), (-L / 2, h)]
    return pts


def build_chest(b, P):
    rng = b.rng
    W, D, t = P["width"], P["depth"], P["board_t"]
    ch, ce, g = P["chamfer"], P["end_chamfer"], P["board_gap"] / 2
    rows = P["rows"]
    info = {}

    # ---- body walls: three boards a side, front and back full width, the ends between them
    for side, (sy, name) in enumerate(((-1, "front"), (1, "back"))):
        yc = sy * (D / 2 - t / 2)
        for r in range(3):
            z0, z1 = rows[r] + (g if r else 0.0), rows[r + 1] - (g if r < 2 else 0.0)
            proud = rng.uniform(-0.0004, 0.0004)
            M = frame(Vector((0.0, yc + sy * proud, (z0 + z1) / 2)), X, Z)     # local z = -Y (out of the front)
            if r == 0:
                poly = [(x, y - (z1 - z0) / 2) for x, y in skirt_poly(W, z1 - z0, P["foot_w"], P["foot_clear"])]
                prism(b, f"{name}_board_0", BOX, M, poly, t, ch, mat=WOOD, wood=((z1 - z0) / 2, t / 2))
            else:
                beam(b, f"{name}_board_{r}", BOX, M, W, (z1 - z0) / 2, t / 2, (ch,) * 4, ce)
    Le = D - 2 * t - 0.0006
    for sx, name in ((-1, "left"), (1, "right")):
        xc = sx * (W / 2 - t / 2)
        for r in range(3):
            z0, z1 = rows[r] + (g if r else 0.0), rows[r + 1] - (g if r < 2 else 0.0)
            proud = rng.uniform(-0.0004, 0.0004)
            M = frame(Vector((xc + sx * proud, 0.0, (z0 + z1) / 2)), Y, Z)     # local z = +X
            if r == 0:
                poly = [(x, y - (z1 - z0) / 2) for x, y in
                        skirt_poly(Le, z1 - z0, P["foot_w"] - t, P["foot_clear"])]
                prism(b, f"{name}_board_0", BOX, M, poly, t, ch, mat=WOOD, wood=((z1 - z0) / 2, t / 2))
            else:
                beam(b, f"{name}_board_{r}", BOX, M, Le, (z1 - z0) / 2, t / 2, (ch,) * 4, ce)
    # ---- floor: three boards across the inside, on the skirt
    Lf, Df = W - 2 * t - 0.0006, D - 2 * t - 0.0006
    ws = [rng.uniform(0.85, 1.15) for _ in range(3)]
    ws = [w / sum(ws) * (Df - 2 * P["board_gap"]) for w in ws]
    y = -Df / 2
    for i, w in enumerate(ws):
        M = frame(Vector((0.0, y + w / 2, P["floor_z"] + t / 2)), X, Y)
        beam(b, f"floor_{i}", BOX, M, Lf, w / 2, t / 2, (ch,) * 4, ce)
        y += w + P["board_gap"]

    # ---- lid: four rails under four top boards
    z0 = P["body_h"] + P["seam_gap"]
    zr = z0 + P["lid_rail_h"]
    ztop = zr + t
    info.update(lid_bottom=z0, lid_top=ztop)
    for sy, name in ((-1, "front"), (1, "back")):
        M = frame(Vector((0.0, sy * (D / 2 - t / 2), (z0 + zr) / 2)), X, Z)
        beam(b, f"lid_{name}_rail", LID, M, W, (zr - z0) / 2, t / 2, (ch,) * 4, ce)
    for sx, name in ((-1, "left"), (1, "right")):
        M = frame(Vector((sx * (W / 2 - t / 2), 0.0, (z0 + zr) / 2)), Y, Z)
        beam(b, f"lid_{name}_rail", LID, M, Le, (zr - z0) / 2, t / 2, (ch,) * 4, ce)
    n = P["lid_boards"]
    ws = [rng.uniform(0.9, 1.1) for _ in range(n)]
    ws = [w / sum(ws) * (D - (n - 1) * P["board_gap"]) for w in ws]
    y = -D / 2
    big = P["lid_edge_chamfer"]
    for i, w in enumerate(ws):
        cs = [ch, ch, ch, ch]
        if i == 0:
            cs[1] = big           # the front board's front top edge, worn round
        if i == n - 1:
            cs[0] = big
        lift = rng.uniform(-0.0003, 0.0003)
        M = frame(Vector((0.0, y + w / 2, zr + t / 2 + lift)), X, Y)
        beam(b, f"lid_top_{i}", LID, M, W, w / 2, t / 2, cs, ce)
        y += w + P["board_gap"]

    # ---- iron on the body
    ic, bt = P["iron_chamfer"], P["band_t"]
    for name, (za, zb) in (("seam_band", P["seam_band"]), ("low_band", P["low_band"])):
        band_loop(b, name, BOX, W / 2, D / 2, za, zb, bt, ic)
    corners = [(sx, sy) for sy in (-1, 1) for sx in (-1, 1)]
    a0, a1 = P["angle_z"]
    for sx, sy in corners:
        cm = corner_map(sx, sy, W, D)
        tag = ("L" if sx < 0 else "R") + ("F" if sy < 0 else "B")
        M = Matrix.Translation(Vector((0, 0, (a0 + a1) / 2)))
        prism(b, f"corner_angle_{tag}", BOX, M, cm(angle_poly(P["angle_leg"], P["angle_t"], P["angle_clip"])),
              a1 - a0, ic)
        M = Matrix.Translation(Vector((0, 0, P["foot_cap_h"] / 2)))
        prism(b, f"foot_cap_{tag}", BOX, M, cm(angle_poly(P["cap_leg"], P["cap_t"], P["cap_clip"])),
              P["foot_cap_h"], ic)
    # ---- iron on the lid: corner caps (a folded L under a clipped top plate) and a strap over each end
    ct, cl = P["cap_t"], P["cap_leg"]
    for sx, sy in corners:
        cm = corner_map(sx, sy, W, D)
        tag = ("L" if sx < 0 else "R") + ("F" if sy < 0 else "B")
        M = Matrix.Translation(Vector((0, 0, ztop - P["lid_cap_h"] / 2)))
        prism(b, f"lid_cap_{tag}", LID, M, cm(angle_poly(cl, ct, P["cap_clip"])), P["lid_cap_h"], ic)
        top = [(-cl, -ct), (ct - P["cap_clip"], -ct), (ct, -ct + P["cap_clip"]), (ct, cl), (-cl, cl)]
        M = Matrix.Translation(Vector((0, 0, ztop + ct / 2)))
        prism(b, f"lid_cap_top_{tag}", LID, M, cm(top), ct, ic)
    st, sw = P["strap_t"], P["strap_w"]
    zb = z0 + 0.006
    k = 0.003
    for sx in (-1, 1):
        xs = sx * (W / 2 - P["strap_inset"] - sw / 2)
        poly = [(-D / 2 - st, zb), (-D / 2, zb), (-D / 2, ztop), (D / 2, ztop), (D / 2, zb), (D / 2 + st, zb),
                (D / 2 + st, ztop + st - k), (D / 2 + st - k, ztop + st), (-D / 2 - st + k, ztop + st),
                (-D / 2 - st, ztop + st - k)]
        M = frame(Vector((xs, 0.0, 0.0)), Y, Z)          # local (x, y) = world (Y, Z), extruded along X
        prism(b, f"lid_strap_{'L' if sx < 0 else 'R'}", LID, M, poly, sw, ic)
        info.setdefault("strap_x", []).append(xs)

    # ---- hasp: plate and knuckle on the lid front, the hasp hanging over the seam band onto the lock plate
    yf = -D / 2
    M = frame(Vector((0.0, yf - 0.0025, 0.4785)), X, Z)
    prism(b, "hasp_plate", LID, M, [(-0.035, -0.0315), (0.035, -0.0315), (0.035, 0.0285), (0.032, 0.0315),
                                    (-0.032, 0.0315), (-0.035, 0.0285)], 0.005, ic)
    kz, ky = 0.4475, yf - 0.0085
    lathe(b, "hasp_knuckle", LID, (-0.021, ky, kz), X,
          [(0.0, 0.0055), (0.001, 0.0065), (0.041, 0.0065), (0.042, 0.0055)], n=10)
    hb, ht = 0.318, 0.445
    outer = [(-0.010, hb), (0.010, hb), (0.016, hb + 0.006), (0.016, ht - 0.002), (0.014, ht), (-0.014, ht),
             (-0.016, ht - 0.002), (-0.016, hb + 0.006)]
    s0, s1 = 0.327, 0.352
    slot = [(-0.003, s0), (0.003, s0), (0.0045, s0 + 0.0015), (0.0045, s1 - 0.0015), (0.003, s1), (-0.003, s1),
            (-0.0045, s1 - 0.0015), (-0.0045, s0 + 0.0015)]
    M = frame(Vector((0.0, yf - 0.008, 0.0)), X, Z)
    prism(b, "hasp", LID, M, outer, 0.005, ic, hole=slot)
    M = frame(Vector((0.0, yf - 0.0025, 0.318)), X, Z)
    prism(b, "lock_plate", BOX, M, [(-0.027, -0.036), (0.027, -0.036), (0.03, -0.033), (0.03, 0.036),
                                    (-0.03, 0.036), (-0.03, -0.033)], 0.005, ic)
    zc, h, w = (s0 + s1) / 2, 0.0095, 0.0035
    yo, yl = yf - 0.004, yf - 0.019
    staple = [(yo, zc - h), (yl + 0.002, zc - h), (yl, zc - h + 0.002), (yl, zc + h - 0.002), (yl + 0.002, zc + h),
              (yo, zc + h), (yo, zc + h - w), (yl + w, zc + h - w), (yl + w, zc - h + w), (yo, zc - h + w)]
    M = frame(Vector((0.0, 0.0, 0.0)), Y, Z)
    prism(b, "staple", BOX, M, staple, 0.006, 0.0006)

    # ---- hinges at the back: body leaf, lid leaf, three knuckles on one axis
    yb = D / 2
    hr, lw, lt = P["hinge_r"], P["hinge_leaf_w"], P["hinge_leaf_t"]
    axis_y, axis_z = yb + 0.0085, P["body_h"] + P["seam_gap"] / 2
    info["hinge"] = Vector((0.0, axis_y, axis_z))
    kl = lw / 3
    for hx in (-P["hinge_x"], P["hinge_x"]):
        lo, hi_ = -lw / 2, lw / 2
        m0, m1 = -kl / 2, kl / 2
        # body leaf (local x = world X, y = world Z, extruded along -Y): notched under the lid knuckles
        notch = axis_z - hr - 0.0012
        body_leaf = [(lo, 0.29), (hi_, 0.29), (hi_, notch), (m1 - 0.0005, notch), (m1 - 0.0005, P["body_h"]),
                     (m0 + 0.0005, P["body_h"]), (m0 + 0.0005, notch), (lo, notch)]
        M = frame(Vector((hx, yb + lt / 2, 0.0)), X, Z)
        prism(b, "hinge_leaf_body", BOX, M, body_leaf, lt, ic)
        top = z0 + 0.105
        up = axis_z + hr + 0.0012
        lid_leaf = [(lo, z0), (m0 - 0.0005, z0), (m0 - 0.0005, up), (m1 + 0.0005, up), (m1 + 0.0005, z0),
                    (hi_, z0), (hi_, top - 0.004), (hi_ - 0.004, top), (lo + 0.004, top), (lo, top - 0.004)]
        prism(b, "hinge_leaf_lid", LID, M, lid_leaf, lt, ic)
        prof = lambda L: [(0.0, hr - 0.001), (0.001, hr), (L - 0.001, hr), (L, hr - 0.001)]
        for grp, xa, xb in ((LID, lo, m0 - 0.0005), (BOX, m0 + 0.0005, m1 - 0.0005), (LID, m1 + 0.0005, hi_)):
            lathe(b, "hinge_knuckle", grp, (hx + xa, axis_y, axis_z), X, prof(xb - xa), n=10)

    # ---- rivets
    rv = []
    sb, lb = P["seam_band"], P["low_band"]
    for zb_, span, nx, ny, skip in (((sb[0] + sb[1]) / 2, 0.33, 11, 7, True), ((lb[0] + lb[1]) / 2, 0.27, 7, 4, False)):
        for x in np.linspace(-span, span, nx):
            if skip and abs(x) < 0.05:
                continue
            rv.append((BOX, (x, -D / 2 - bt, zb_), -Y))
            if not (skip and min(abs(x - P["hinge_x"]), abs(x + P["hinge_x"])) < 0.035):
                rv.append((BOX, (x, D / 2 + bt, zb_), Y))
        yspan = 0.18 if skip else 0.12
        for yy in np.linspace(-yspan, yspan, ny):
            rv.append((BOX, (-W / 2 - bt, yy, zb_), -X))
            rv.append((BOX, (W / 2 + bt, yy, zb_), X))
    for sx, sy in corners:
        for z in (0.16, 0.23, 0.30):
            rv.append((BOX, (sx * (W / 2 - P["angle_leg"] / 2), sy * (D / 2 + P["angle_t"]), z), Y * sy))
            rv.append((BOX, (sx * (W / 2 + P["angle_t"]), sy * (D / 2 - P["angle_leg"] / 2), z), X * sx))
        for z in (0.035, 0.085):
            rv.append((BOX, (sx * (W / 2 - 0.045), sy * (D / 2 + ct), z), Y * sy))
            rv.append((BOX, (sx * (W / 2 + ct), sy * (D / 2 - 0.045), z), X * sx))
        rv.append((LID, (sx * (W / 2 - 0.05), sy * (D / 2 + ct), ztop - 0.04), Y * sy))
        rv.append((LID, (sx * (W / 2 + ct), sy * (D / 2 - 0.05), ztop - 0.04), X * sx))
        rv.append((LID, (sx * (W / 2 - 0.042), sy * (D / 2 - 0.042), ztop + ct), Z))
    for xs in info["strap_x"]:
        for yy in (-0.13, -0.065, 0.065, 0.13):
            rv.append((LID, (xs, yy, ztop + st), Z))
        for sy in (-1, 1):
            rv.append((LID, (xs, sy * (D / 2 + st), z0 + 0.04), Y * sy))
    for x in (-0.025, 0.025):
        for z in (0.462, 0.498):
            rv.append((LID, (x, yf - 0.005, z), -Y))
        rv.append((BOX, (x, yf - 0.005, 0.293), -Y))
    for hx in (-P["hinge_x"], P["hinge_x"]):
        for dx in (-0.013, 0.013):
            rv.append((BOX, (hx + dx, yb + lt, 0.305), Y))
            rv.append((BOX, (hx + dx, yb + lt, 0.335), Y))
            rv.append((LID, (hx + dx, yb + lt, z0 + 0.045), Y))
            rv.append((LID, (hx + dx, yb + lt, z0 + 0.085), Y))
    for grp, p, nrm in rv:
        rivet(b, grp, p, nrm)
    for xs in info["strap_x"]:
        rivet(b, LID, (xs, 0.0, ztop + st), Z, r=P["stud_r"], h=P["stud_h"], n=12)
    info["rivets"] = len(rv) + len(info["strap_x"])
    return info


# --------------------------------------------------------------------------------------------------------------------
# Validation, mesh, UVs
# --------------------------------------------------------------------------------------------------------------------

def validate_builder(b):
    """Per part: closed (every edge used once each way) and outward (positive volume); no bow-tie quads and no
    degenerate faces."""
    report = {"parts": len(b.parts), "open_edges": 0, "misoriented_edges": 0, "negative_volume_parts": [],
              "bowtie_quads": 0, "degenerate_faces": 0, "nonplanar_quads_over_1deg": 0}
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
        Pt = [b.verts[i] for i in idx]
        n = Vector((0, 0, 0))
        for k in range(len(Pt)):
            n += Pt[k].cross(Pt[(k + 1) % len(Pt)])
        if n.length < 1e-10:
            report["degenerate_faces"] += 1
            continue
        if len(Pt) == 4:
            ok = all(((Pt[a] - Pt[o]).cross(Pt[c] - Pt[o])).dot(n) > 0
                     for o, a, c in ((0, 1, 2), (0, 2, 3), (0, 1, 3), (1, 2, 3)))
            if not ok:
                report["bowtie_quads"] += 1
            n1 = (Pt[1] - Pt[0]).cross(Pt[2] - Pt[0])
            n2 = (Pt[2] - Pt[0]).cross(Pt[3] - Pt[0])
            if n1.length > 1e-12 and n2.length > 1e-12 and n1.angle(n2) > math.radians(1.0):
                report["nonplanar_quads_over_1deg"] += 1
    return report


def clash_report(b):
    """Triangle overlaps between the body and the lid (they should only meet at the hinge, never overlap)."""
    trees = {}
    for g in (BOX, LID):
        fl = [i for i in range(len(b.faces)) if b.parts[b.fpart[i]]["group"] == g]
        used = sorted({i for fi in fl for i in b.faces[fi]})
        remap = {o: k for k, o in enumerate(used)}
        trees[g] = BVHTree.FromPolygons([b.verts[i] for i in used], [[remap[i] for i in b.faces[fi]] for fi in fl])
    return len(trees[BOX].overlap(trees[LID]))


def make_object(b):
    me = bpy.data.meshes.new(ASSET_ID)
    me.from_pydata([tuple(v) for v in b.verts], [], [list(f) for f in b.faces])
    me.update()
    atlas = me.uv_layers.new(name="UVMap")
    atlas.data.foreach_set("uv", np.array([c for f in b.fuv for uv in f for c in uv], dtype=np.float32))
    parts = [b.parts[i] for i in b.fpart]
    me.polygons.foreach_set("material_index", [p["mat"] for p in parts])
    a = me.attributes.new("lpos", "FLOAT_VECTOR", "POINT")
    a.data.foreach_set("vector", np.array(b.lpos, dtype=np.float32).ravel())
    for name, key in (("pith", "pith"), ("pslope", "slope")):
        a = me.attributes.new(name, "FLOAT_VECTOR", "FACE")
        a.data.foreach_set("vector", np.array([p[key] for p in parts], dtype=np.float32).ravel())
    a = me.attributes.new("pgroup", "INT", "FACE")
    a.data.foreach_set("value", [p["group"] for p in parts])
    a = me.attributes.new("pend", "FLOAT", "FACE")
    a.data.foreach_set("value", [float(e) for e in b.fend])
    a = me.attributes.new("pzoff", "FLOAT", "FACE")
    a.data.foreach_set("value", [0.0] * len(parts))
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


def pack_atlas(obj, res):
    me = obj.data
    bpy.context.scene.tool_settings.use_uv_select_sync = True
    select_only(obj)
    me.uv_layers.active = me.uv_layers["UVMap"]
    me.vertices.foreach_set("select", np.ones(len(me.vertices), dtype=bool))
    me.edges.foreach_set("select", np.ones(len(me.edges), dtype=bool))
    me.polygons.foreach_set("select", np.ones(len(me.polygons), dtype=bool))
    me.update()
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.uv.pack_islands(udim_source="CLOSEST_UDIM", rotate=True, rotate_method="CARDINAL", scale=True,
                            margin_method="FRACTION", margin=6.0 / res, shape_method="CONCAVE")
    bpy.ops.object.mode_set(mode="OBJECT")
    uv = np.empty(len(me.loops) * 2, dtype=np.float64)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    t = lv.reshape(-1, 3)            # triangulated
    tu = uv.reshape(-1, 3, 2)
    e1, e2 = tu[:, 1] - tu[:, 0], tu[:, 2] - tu[:, 0]
    a2 = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2
    p = co[t]
    a3 = np.linalg.norm(np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0]), axis=1) / 2
    return {"uv_fill": float(a2.sum()), "area_m2": float(a3.sum()), "px_per_m": float(math.sqrt(a2.sum() / a3.sum()) * res),
            "uv_zero_area_tris": int((a2 < 1e-11).sum()), "degenerate_tris": int((a3 < 1e-12).sum()),
            "u_range": [float(uv[:, 0].min()), float(uv[:, 0].max())], "v_range": [float(uv[:, 1].min()), float(uv[:, 1].max())]}


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

    def abs(self, a): return self.m("ABSOLUTE", a)
    def max(self, a, c): return self.m("MAXIMUM", a, c)
    def min(self, a, c): return self.m("MINIMUM", a, c)
    def pow(self, a, c): return self.m("POWER", self.m("MAXIMUM", a, 0.0), c)
    def sqrt(self, a): return self.m("SQRT", self.m("MAXIMUM", a, 0.0))
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

    def attr(self, name):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Fac"] if "Fac" in n.outputs else n.outputs["Factor"], "f")

    def attrv(self, name):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Vector"], "v")


class Ctx:
    pass


def context(g):
    c = Ctx()
    c.lx, c.ly, c.lz = g.sep(g.attrv("lpos"))
    c.py0, c.pz0, c.rand = g.sep(g.attrv("pith"))
    c.sy, c.sz, c.ringw = g.sep(g.attrv("pslope"))
    c.pend = g.attr("pend")
    geo = g.node("ShaderNodeNewGeometry")
    c.pos = Val(g, geo.outputs["Position"], "v")
    c.nrm = Val(g, geo.outputs["Normal"], "v")
    _, _, pz = g.sep(c.pos)
    c.pz = pz - g.attr("pzoff")
    _, _, c.nz = g.sep(c.nrm)
    ao = g.node("ShaderNodeAmbientOcclusion", samples=16, only_local=False, inside=False)
    g.put(ao.inputs["Distance"], 0.3)
    c.ao = Val(g, ao.outputs["AO"], "f")
    bev = g.node("ShaderNodeBevel", samples=8)
    g.put(bev.inputs["Radius"], 0.004)
    c.edge = g.smooth(0.995, 0.94, g.dot(Val(g, bev.outputs["Normal"], "v"), c.nrm))
    c.cvx = c.edge * g.smooth(0.55, 0.9, c.ao)     # convex (exposed) edges only
    c.low = g.smooth(0.0, 0.30, c.pz)              # floor grime toward the ground
    return c


def sh_oak(g, c):
    """Dark oak in the member frame: rings round a pith line, earlywood pores, rays, streaks, then age."""
    x, y, z, r = c.lx, c.ly, c.lz, c.rand
    wob = g.noise(g.vec(x * 1.1, y * 7.0, z * 7.0 + r * 17.0), 1.0, detail=3.0)
    dy = y - (c.py0 + c.sy * x)
    dz = z - (c.pz0 + c.sz * x)
    rr = g.sqrt(dy * dy + dz * dz) + (wob - 0.5) * 0.009
    phase = rr / c.ringw + (g.noise(g.vec(x * 3.0, y * 45.0, z * 45.0 + r * 5.0), 2.0, detail=2.0) - 0.5) * 0.5
    f = g.fract(phase)
    late = g.smooth(0.45, 0.80, f) * (1.0 - g.smooth(0.94, 1.0, f))
    early = 1.0 - g.smooth(0.0, 0.3, f)
    pores = early * g.smooth(0.52, 0.68, g.noise(g.vec(x * 5.0, y * 700.0, z * 700.0 + r * 3.0), 3.0, detail=1.0))
    fibre = g.noise(g.vec(x * 1.5, y * 320.0, z * 320.0 + r * 29.0), 4.0, detail=2.0)
    streak = g.noise(g.vec(x * 0.35, y * 18.0, z * 18.0 + r * 31.0), 5.0, detail=3.0)
    blotch = g.noise(g.vec(x * 2.0, y * 2.0, z * 2.0 + r * 37.0), 6.0, detail=4.0)
    rays = g.smooth(0.70, 0.78, g.noise(g.vec(x * 30.0, y * 260.0, z * 260.0 + r * 41.0), 7.0, detail=1.0))
    tone = 0.8 + r * 0.4
    flame = g.smooth(0.52, 0.74, streak) * (1.0 - late)
    light = g.mix(lin(86, 57, 38), lin(136, 86, 46), flame)
    base = g.mix(light, lin(46, 31, 22), late * 0.68)
    base = g.mix(base, lin(40, 28, 20), pores * 0.45)
    base = g.mix(base, lin(130, 96, 66), rays * 0.18 * (1.0 - late))
    base = base * ((0.78 + fibre * 0.44) * (0.80 + blotch * 0.40) * tone)
    base = base * 0.38                                              # the dark oil-and-smoke finish
    endg = c.pend
    base = base * (1.0 - endg * 0.45)
    ringline = (1.0 - g.smooth(0.0, 0.08, f)) * endg
    base = base * (1.0 - ringline * 0.3)
    # age: grime in the lows and seams, handled edges lighter, dust on the lid, floor dirt at the feet
    base = base * (0.35 + 0.65 * c.ao)
    base = g.mix(base, lin(122, 96, 70), c.cvx * 0.35)
    dust = g.smooth(0.6, 0.95, c.nz) * g.smooth(0.45, 0.75, g.noise(c.pos * 9.0, 8.0, detail=3.0))
    base = g.mix(base, lin(92, 84, 74), dust * 0.18)
    base = base * (0.72 + 0.28 * c.low)
    dirt = (1.0 - c.low) * g.smooth(0.45, 0.65, g.noise(c.pos * 22.0, 9.0, detail=4.0))
    base = g.mix(base, lin(56, 47, 38), dirt * 0.5)
    rough = 0.62 + fibre * 0.08 + pores * 0.08 - c.cvx * 0.14 + dust * 0.12 + dirt * 0.12 + late * 0.04 + endg * 0.12
    height = (-late * 0.00022 - pores * 0.00018 + fibre * 0.00012 + streak * 0.00010 - rays * 0.00004
              + wob * 0.00030 - c.cvx * 0.00020)
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def sh_iron(g, c):
    """Blackened wrought iron: hammer dimples, a mottled grey scale, worn bright edges, rust in the lows."""
    p = c.pos
    r = c.rand
    n1 = g.noise(p * 16.0, 1.0 + r, detail=4.0)
    n2 = g.noise(p * 55.0, 2.0, detail=4.0)
    n3 = g.noise(p * 240.0, 3.0, detail=2.0)
    dimple = g.voronoi(p * 60.0)
    pits = 1.0 - g.smooth(0.0, 0.12, g.voronoi(p * 150.0))
    mott = g.smooth(0.42, 0.62, n1 * 0.7 + n2 * 0.3)
    base = g.mix(lin(34, 34, 36), lin(78, 78, 77), mott * 0.75 + n3 * 0.15)
    rust = g.smooth(0.60, 0.76, n2 * 0.45 + n1 * 0.25 + (1.0 - c.ao) * 0.45 + (1.0 - c.low) * 0.12)
    base = g.mix(base, g.mix(lin(70, 44, 30), lin(104, 62, 36), n3), rust * 0.85)
    base = g.mix(base, lin(26, 23, 21), pits * 0.5 * rust)
    worn = c.cvx * (1.0 - rust * 0.8)
    base = g.mix(base, lin(150, 148, 144), worn * 0.65)
    base = base * (0.45 + 0.55 * c.ao)
    metal = g.max(g.mix(0.78, 0.08, rust), worn * 0.95)
    rough = g.mix(g.mix(0.52, 0.86, rust), 0.32, worn) + n3 * 0.06 + (1.0 - mott) * 0.04
    height = (dimple * 0.00022 + n2 * 0.00025 + rust * (n3 * 0.0003 + 0.00012) - pits * rust * 0.00035
              + n3 * 0.00004)
    return {"base": base, "rough": rough, "metal": metal, "height": height}


SHADERS = {WOOD: sh_oak, IRON: sh_iron}


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
    g.put(e_orm.inputs["Color"], g.vec(g.pow(c.ao, 1.1), g.sat(res["rough"]), g.sat(res["metal"])))
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


def run_bake(kind_, device_used, tries=3):
    """Bake one pass; the GPU is shared with other jobs, so retry a failed GPU bake, then fall back to the CPU."""
    scene = bpy.context.scene

    def go():
        if kind_ == "normal":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
                                normal_b="POS_Z", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
        else:
            bpy.ops.object.bake(type="EMIT", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
    for attempt in range(tries):
        try:
            go()
            return
        except RuntimeError as err:
            if scene.cycles.device != "GPU":
                raise
            print(f"  GPU bake failed ({str(err)[:120]}), attempt {attempt + 1}/{tries}", flush=True)
            time.sleep(5 * (attempt + 1))
    print("  falling back to the CPU", flush=True)
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


def lid_vertex_mask(me):
    a = np.empty(len(me.polygons), dtype=np.int32)
    me.attributes["pgroup"].data.foreach_get("value", a)
    lt = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("loop_total", lt)
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    mask = np.zeros(len(me.vertices), dtype=bool)
    mask[lv[np.repeat(a == LID, lt)]] = True
    return mask, a


def shift_lid(me, dz):
    mask, fgroup = lid_vertex_mask(me)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    co[mask, 2] += dz
    me.vertices.foreach_set("co", co.ravel())
    zoff = np.where(fgroup == LID, dz, 0.0).astype(np.float32)
    me.attributes["pzoff"].data.foreach_set("value", zoff)
    me.update()


def bake_textures(obj, res, samples, device, work):
    scene = bpy.context.scene
    dev = set_device(scene, device)
    device_used = {scene.cycles.device if dev == "CPU" else dev}
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.seed = 0
    scene.render.bake.use_selected_to_active = False
    bpy.ops.mesh.primitive_plane_add(size=6.0, location=(0, 0, 0))   # ground for the occlusion; not exported
    plane = bpy.context.active_object
    plane.name = "_ao_ground"
    image = bpy.data.images.new("bake_atlas", res, res, alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    mats, shaders = [], []
    for k in (WOOD, IRON):
        mat, sh = bake_material(f"bake_{KINDS[k]}", k, image)
        mats.append(mat)
        shaders.append(sh)
    me = obj.data
    keep = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", keep)
    me.materials.clear()
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", keep)
    me.update()
    shift_lid(me, LIFT)
    select_only(obj)
    out, timings = {}, {}
    for pass_ in ("colour", "orm", "normal"):
        for sh in shaders:
            tree = sh["tree"]
            for link in list(sh["out"].inputs["Surface"].links):
                tree.links.remove(link)
            tree.links.new(sh[pass_], sh["out"].inputs["Surface"])
        t0 = time.time()
        run_bake(pass_, device_used)
        timings[pass_] = round(time.time() - t0, 1)
        px = np.empty(res * res * 4, dtype=np.float32)
        image.pixels.foreach_get(px)
        out[pass_] = px.reshape(res, res, 4)[:, :, :3].copy()
        print(f"  baked {pass_} in {timings[pass_]} s", flush=True)
    shift_lid(me, -LIFT)
    bpy.data.objects.remove(plane)
    col, orm, nrm = out["colour"], out["orm"], out["normal"]
    mask = (col.sum(axis=2) > 1e-6) | (orm.sum(axis=2) > 1e-6)
    for arr, fill in ((col, None), (orm, None), (nrm, (0.5, 0.5, 1.0))):
        arr[~mask] = fill if fill is not None else arr[mask].mean(axis=0)
    stem = os.path.join(work, ASSET_ID)
    paths = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
    save_png(to_srgb(col), res, paths["basecolor"], "sRGB")
    save_png(orm, res, paths["orm"], "Non-Color")
    save_png(nrm, res, paths["normal"], "Non-Color")
    return paths, timings, sorted(device_used), float(mask.mean())


def gltf_output_group():
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is None:
        ng = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.nodes.new("NodeGroupInput")
    return ng


def final_material(paths):
    mat = bpy.data.materials.new(f"MAT_{ASSET_ID}")
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
    tree.links.new(bsdf.outputs[0], out.inputs["Surface"])
    col = tree.nodes.new("ShaderNodeTexImage")
    col.image = bpy.data.images.load(paths["basecolor"])
    col.image.colorspace_settings.name = "sRGB"
    tree.links.new(col.outputs["Color"], bsdf.inputs["Base Color"])
    orm = tree.nodes.new("ShaderNodeTexImage")
    orm.image = bpy.data.images.load(paths["orm"])
    orm.image.colorspace_settings.name = "Non-Color"
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(orm.outputs["Color"], sep.inputs[0])
    tree.links.new(sep.outputs[1], bsdf.inputs["Roughness"])
    tree.links.new(sep.outputs[2], bsdf.inputs["Metallic"])
    grp = tree.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    tree.links.new(sep.outputs[0], grp.inputs["Occlusion"])
    nrm = tree.nodes.new("ShaderNodeTexImage")
    nrm.image = bpy.data.images.load(paths["normal"])
    nrm.image.colorspace_settings.name = "Non-Color"
    nmap = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    tree.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    mat.use_backface_culling = True     # every part is a closed solid: single-sided in glTF
    return mat


def flat_materials():
    mats = []
    for k, col in ((WOOD, (0.06, 0.035, 0.02)), (IRON, (0.035, 0.035, 0.037))):
        mat = bpy.data.materials.new(f"MAT_flat_{KINDS[k]}")
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = col + (1.0,)
        bsdf.inputs["Roughness"].default_value = 0.6
        bsdf.inputs["Metallic"].default_value = 0.8 if k == IRON else 0.0
        mat.use_backface_culling = True
        mats.append(mat)
    return mats


def corner_normals(me):
    cn = np.empty(len(me.loops) * 3, dtype=np.float64)
    me.corner_normals.foreach_get("vector", cn)
    return cn.reshape(-1, 3)


def split_lid(obj, hinge):
    """Move the lid's faces into their own object 'lid' with its origin on the hinge axis, parented to the body.
    Returns the largest change in any corner normal (degrees), which must be ~0."""
    me = obj.data
    before = corner_normals(me)
    grp = np.empty(len(me.polygons), dtype=np.int32)
    me.attributes["pgroup"].data.foreach_get("value", grp)
    ls = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_start", ls)
    lt = np.empty(len(me.polygons), dtype=np.int64)
    me.polygons.foreach_get("loop_total", lt)
    corner_grp = np.repeat(grp, lt)
    lid_me = me.copy()
    lid_me.name = ASSET_ID + "_lid"
    for mesh, drop in ((me, LID), (lid_me, BOX)):
        bm = bmesh.new()
        bm.from_mesh(mesh)
        layer = bm.faces.layers.int.get("pgroup")
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[layer] == drop], context="FACES")
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
    lid_me.transform(Matrix.Translation(-hinge))
    lid = bpy.data.objects.new("lid", lid_me)
    bpy.context.scene.collection.objects.link(lid)
    lid.parent = obj
    lid.location = hinge
    # compare normals: faces keep their order within each group through the deletes
    worst = 0.0
    for mesh, g in ((me, BOX), (lid_me, LID)):
        after = corner_normals(mesh)
        ref = before[corner_grp == g]
        if len(ref) != len(after):
            return lid, 180.0
        cosv = np.clip(np.einsum("ij,ij->i", ref, after), -1, 1)
        worst = max(worst, float(np.degrees(np.arccos(cosv)).max()))
    return lid, worst


# --------------------------------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    ap.add_argument("--work-dir", default=os.path.join(tempfile.gettempdir(), ASSET_ID + "_procgen"))
    ap.add_argument("--seed", type=int, default=2209)
    ap.add_argument("--res", type=int, default=2048)
    ap.add_argument("--samples", type=int, default=24)
    ap.add_argument("--device", default="auto", choices=("auto", "cpu"))
    ap.add_argument("--no-bake", action="store_true")
    return ap.parse_args(argv)


def gltf_vec(v):
    """Blender Z-up to glTF Y-up (x, z, -y)."""
    return [round(v[0], 5), round(v[2], 5), round(-v[1], 5)]


def readback(glb):
    """Import the written GLB into an empty scene and report its nodes, hierarchy and extents (glTF frame)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    nodes = {}
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    tris = 0
    for o in bpy.context.scene.objects:
        nodes[o.name] = {"type": o.type, "parent": o.parent.name if o.parent else None,
                         "location_gltf": gltf_vec(o.matrix_world.translation)}
        if o.type == "MESH":
            for v in o.data.vertices:
                w = Vector(gltf_vec(o.matrix_world @ v.co))
                lo = Vector(map(min, lo, w))
                hi = Vector(map(max, hi, w))
            o.data.calc_loop_triangles()
            tris += len(o.data.loop_triangles)
            nodes[o.name]["materials"] = [m.name for m in o.data.materials]
            nodes[o.name]["double_sided"] = [not m.use_backface_culling for m in o.data.materials]
    return {"nodes": nodes, "min_gltf": [round(c, 4) for c in lo], "max_gltf": [round(c, 4) for c in hi],
            "triangles": tris}


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
    info = build_chest(b, P)

    # centre on X/Y (glTF X/Z), lowest point at z = 0
    xs = [v.x for v in b.verts]
    ys = [v.y for v in b.verts]
    zs = [v.z for v in b.verts]
    shift = Vector((-(min(xs) + max(xs)) / 2, -(min(ys) + max(ys)) / 2, -min(zs)))
    b.verts = [v + shift for v in b.verts]
    for i in range(len(b.lpos)):
        if b.parts[b.vpart[i]]["world_grain"]:
            b.lpos[i] = tuple(Vector(b.lpos[i]) + shift)
    hinge = info["hinge"] + shift
    dims = Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))

    topo = validate_builder(b)
    clashes = clash_report(b)
    print("TOPOLOGY", json.dumps(topo), flush=True)
    print("BODY|LID CLASHES", clashes, flush=True)
    bad = topo["open_edges"] or topo["misoriented_edges"] or topo["negative_volume_parts"] or topo["bowtie_quads"] \
        or topo["degenerate_faces"]
    if bad:
        raise SystemExit("topology check failed: " + json.dumps(topo))

    obj = make_object(b)
    finish_topology(obj)
    tris = len(obj.data.polygons)
    tris_by_group = {GROUPS[g]: 0 for g in (BOX, LID)}
    grp = np.empty(len(obj.data.polygons), dtype=np.int32)
    obj.data.attributes["pgroup"].data.foreach_get("value", grp)
    for g in (BOX, LID):
        tris_by_group[GROUPS[g]] = int((grp == g).sum())
    uv = pack_atlas(obj, args.res)
    print("UV", json.dumps(uv), flush=True)
    if uv["uv_zero_area_tris"] or uv["degenerate_tris"]:
        raise SystemExit("uv/degenerate check failed: " + json.dumps(uv))
    if tris > TRI_BUDGET:
        raise SystemExit(f"{tris} triangles over the {TRI_BUDGET} budget")

    textures, timings, devices, coverage = {}, {}, [], None
    if args.no_bake:
        me = obj.data
        keep = np.empty(len(me.polygons), dtype=np.int32)
        me.polygons.foreach_get("material_index", keep)
        me.materials.clear()
        for m in flat_materials():
            me.materials.append(m)
        me.polygons.foreach_set("material_index", keep)
    else:
        paths, timings, devices, coverage = bake_textures(obj, args.res, args.samples, args.device, args.work_dir)
        mat = final_material(paths)
        me = obj.data
        me.materials.clear()
        me.materials.append(mat)
        me.polygons.foreach_set("material_index", np.zeros(len(me.polygons), dtype=np.int32))
        textures = {k: os.path.basename(v) for k, v in paths.items()}
    me = obj.data
    lid, normal_drift = split_lid(obj, hinge)
    print("NORMAL DRIFT (deg)", normal_drift, flush=True)
    if normal_drift > 0.25:          # float noise in the custom-normal encoding is ~0.05 deg
        raise SystemExit(f"splitting the lid changed corner normals by {normal_drift} deg")
    for mesh in (obj.data, lid.data):
        for name in ("lpos", "pith", "pslope", "pgroup", "pzoff", "pend"):
            if name in mesh.attributes:
                mesh.attributes.remove(mesh.attributes[name])

    glb = os.path.join(args.out_dir, ASSET_ID + ".glb")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.work_dir, ASSET_ID + ".blend"))
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True, export_tangents=False,
                              export_image_format="AUTO", use_selection=False, export_extras=False)
    glb_sha = hashlib.sha256(open(glb, "rb").read()).hexdigest()
    back = readback(glb)
    print("READBACK", json.dumps(back), flush=True)

    concept_sha = hashlib.sha256(open(CONCEPT, "rb").read()).hexdigest() if os.path.exists(CONCEPT) else None
    script_sha = hashlib.sha256(open(os.path.abspath(__file__), "rb").read()).hexdigest()
    ztop = info["lid_top"] + shift.z
    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_chest.py",
        "script_sha256": script_sha,
        "command": "blender --background --factory-startup --python tools/asset_pipeline/_procgen_chest.py -- "
                   f"--seed {args.seed} --res {args.res} --samples {args.samples}" + (" --no-bake" if args.no_bake else ""),
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "parameters": P,
        "concept": "assets/concepts/container_chest_iron_banded.png",
        "concept_sha256": concept_sha,
        "replaces": "assets/ready/container_chest_iron_banded (Pixal3D reconstruction; levelled 0.72 x 0.39 x 0.41 m, "
                    "squashed, sides smeared)",
        "design_notes": [
            "Flat lid, as the concept draws it (top boards level, the front and back top edges worn round); no dome.",
            "Body hollow (walls, floor) so the open lid shows an inside; the lid is a rail frame under four top boards.",
            "Hasp and lock plate on the front (+Z): hasp plate and knuckle on the lid, hasp over the seam band, "
            "staple through the hasp slot on the body lock plate. No padlock: the concept shows none.",
            "Two three-knuckle hinges at the back (the concept does not show the back); their knuckles alternate "
            "lid/body on the hinge axis and each leaf is notched round the other's knuckle.",
            "The concept's small latch on the right end at the seam is not modelled.",
            "Inside is baked with the lid lifted clear, so the chest reads lit when opened; the closed seam loses its "
            "baked crease (the seam band covers most of it).",
        ],
        "frame": "glTF +Y up, front +Z (hasp side), width on X, centred on X/Z, lowest point y=0",
        "nodes": {
            ASSET_ID: "root mesh: the body, its irons, the lock plate and staple, the middle hinge knuckles",
            "lid": "child mesh: rails, top boards, lid caps, end straps, hasp plate, knuckle and hasp, lid hinge leaves "
                   "and outer knuckles; origin on the hinge axis",
        },
        "lid_hinge": {
            "origin_gltf": gltf_vec(hinge), "axis_gltf": [1.0, 0.0, 0.0],
            "open": "rotate the 'lid' node about its local X by a negative angle (front rises); about -100 deg is "
                    "fully open, lid resting back",
            "note": "axis on the knuckle centres, 8.5 mm behind the back face and at the middle of the 1.5 mm seam",
        },
        "dimensions_m": {"x": round(dims.x, 4), "y_height": round(dims.z, 4), "z": round(dims.y, 4),
                         "wood_carcass": [P["width"], round(ztop, 4), P["depth"]]},
        "world_fit": "container sites have no world structure, so there is no Fitting.cs constraint; checked by "
                     "render beside a 1.8 m figure",
        "triangles": tris,
        "triangles_by_node": {ASSET_ID: tris_by_group["box"], "lid": tris_by_group["lid"]},
        "triangle_budget": TRI_BUDGET,
        "rivets_and_studs": info["rivets"],
        "checks": {
            "topology": topo,
            "uv_zero_area_tris": uv["uv_zero_area_tris"],
            "degenerate_tris": uv["degenerate_tris"],
            "body_lid_triangle_overlaps": clashes,
            "corner_normal_drift_on_split_deg": round(normal_drift, 6),
            "readback": back,
        },
        "materials": {
            "source": "own procedural PBR shaders (dark oak with rings round a seeded pith line per board, blackened "
                      "wrought iron), baked with Cycles; the staged world materials were not used",
            "material": f"MAT_{ASSET_ID}", "single_sided": True,
            "atlas": {"size": args.res, "px_per_m": round(uv["px_per_m"], 1), "surface_m2": round(uv["area_m2"], 3),
                      "uv_fill": round(uv["uv_fill"], 3), "baked_coverage": coverage},
            "texel_note": "one density for every island, packed to fill the atlas; above the 512 px/m guide because "
                          "at 512 px/m this hero prop would leave most of the 2048 atlas empty",
            "maps": "base colour (sRGB), normal (tangent, OpenGL +Y, MikkTSpace), ORM (R occlusion, G roughness, "
                    "B metallic), embedded",
            "textures": textures,
            "bake": {"samples": args.samples, "devices": devices, "seconds": timings} if not args.no_bake else None,
        },
        "glb_sha256": glb_sha,
        "outputs": [ASSET_ID + ".glb", ASSET_ID + "_provenance.json"],
        "build_seconds": round(time.time() - t_start, 1),
    }
    with open(os.path.join(args.out_dir, ASSET_ID + "_provenance.json"), "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    print("RESULT", json.dumps({"glb": glb, "dims": prov["dimensions_m"], "tris": tris,
                                "px_per_m": prov["materials"]["atlas"]["px_per_m"], "hinge": gltf_vec(hinge)}))


if __name__ == "__main__":
    main()
