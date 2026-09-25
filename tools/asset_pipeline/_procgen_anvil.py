"""Procedural build of prop_blacksmith_anvil_stump: a London-pattern anvil on a banded, sawn stump.

Image-to-3D melts this kind of object (the Pixal3D anvil's face and base are ~10 deg apart), so it
is authored here from real dimensions. Run headless:

  blender --background --factory-startup --python _procgen_anvil.py -- [--out-dir DIR] [--seed N]
          [--atlas 2048] [--quick]

Method
  * Anvil and stump are signed distance fields in metres. The anvil is the intersection of a side
    and an end silhouette (drawn with Bezier flanks, feet, arches), unioned with the face slab, the
    table and a tapered curved horn, minus the hardy and pritchel holes and a few seeded edge chips.
    The stump is a perturbed cylinder: root flares, seeded radial checks, a rounded rim, and its
    radius pinned to a circle under the iron band. Each field is meshed with surface nets (closed,
    outward, quads never bow-tie), projected onto the field, and decimated to budget.
  * Band, lap plate, rivets and the four hold-down dogs are exact parametric solids.
  * UVs are laid in each member's own frame at one texel density: the stump side is cylindrical
    (grain along V), the end grain planar, the band runs along its circumference, the anvil is
    angle-grouped. One island pack fills the atlas.
  * Materials are evaluated per texel in numpy (object-space procedural iron, polished steel,
    bark, end grain, wrought band iron). Normals combine the field's own gradient (the "high poly")
    with a procedural height, written in the MikkTSpace frame the exporter uses (OpenGL, +Y).
    Occlusion comes from a Cycles bake of the whole prop, so the anvil shades the stump top.
  * Output: one GLB with one material (base colour sRGB, normal, ORM), Y-up, front +Z, centred
    on X/Z, lowest point at y=0, plus SOCK_interact_anvil at the striking face.

Everything is seeded; the same seed and parameters rebuild the same asset.
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import struct
import sys
import time
import zlib

import numpy as np

import bmesh
import bpy
from mathutils import Matrix, Vector

ASSET_ID = "prop_blacksmith_anvil_stump"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
CONCEPT = os.path.join(REPO, "assets", "concepts", f"{ASSET_ID}.png")

# All lengths in metres. Anvil frame: +x toward the heel, horn toward -x, z up, origin at the
# centre of the base. The prop is built in Blender's frame (Z up, front = -Y) and exported Y-up.
PARAMS = {
    "seed": 20260924,
    "face_height": 0.80,          # anvil face above the floor
    # anvil (a ~75 kg London pattern: 0.75 m horn tip to heel, 0.285 m tall, 105 mm face)
    "anvil_height": 0.300,
    "face_width": 0.112,
    "face_thickness": 0.082,      # depth of the heel overhang
    "x_heel": 0.340,
    "x_step": -0.070,
    "x_table": -0.155,
    "table_drop": 0.022,
    "table_thickness": 0.060,
    "horn_tip_x": -0.400,
    "horn_root_radius": 0.055,
    "horn_tip_radius": 0.006,
    "base_half_length": 0.165,
    "base_half_width": 0.125,
    "foot_height": 0.040,
    "hardy_hole": 0.0254,         # 1 in square
    "hardy_x": 0.285,
    "pritchel_diameter": 0.016,   # 5/8 in round
    "pritchel_x": 0.232,
    "anvil_offset_x": 0.020,      # anvil base centre relative to the stump axis
    "anvil_sink": 0.002,          # feet bedded into the end grain
    # stump
    "stump_radius": 0.250,        # 0.50 m sawn trunk
    "stump_taper": 0.03,
    "root_count": 5,
    "crack_count": 6,
    "rim_round": 0.012,
    # band
    "band_z": 0.250,
    "band_width": 0.055,
    "band_thickness": 0.007,
    "rivet_count": 7,
    "rivet_radius": 0.0110,
    "rivet_height": 0.0060,
    # mesh
    "anvil_grid": 0.0025,
    "stump_grid": 0.0035,
    "anvil_tris": 16000,
    "stump_tris": 13500,
    "sharp_angle_anvil": 45.0,
    "sharp_angle_stump": 60.0,
    "sharp_angle_iron": 50.0,
    # texture
    "atlas": 2048,
    "pad_px": 12,
}

LOG_T0 = time.time()


def log(*parts):
    print(f"[procgen_anvil {time.time() - LOG_T0:7.1f}s]", *parts, flush=True)


# --------------------------------------------------------------------------------------------
# numpy noise (hash-based, seeded, deterministic)
# --------------------------------------------------------------------------------------------

_GRAD = np.array([[1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0], [1, 0, 1], [-1, 0, 1],
                  [1, 0, -1], [-1, 0, -1], [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1],
                  [1, 1, 0], [-1, 1, 0], [0, -1, 1], [0, -1, -1]], np.float64)
_ROT = np.array(Matrix.Rotation(0.61, 3, Vector((0.36, 0.48, 0.8)).normalized()), np.float64)


def _hash(ix, iy, iz, seed):
    h = (ix * 73856093 + iy * 19349663 + iz * 83492791 + seed * 2654435761) & 0xFFFFFFFF
    h = h.astype(np.uint32)
    h ^= h >> np.uint32(16)
    h *= np.uint32(0x7FEB352D)
    h ^= h >> np.uint32(15)
    h *= np.uint32(0x846CA68B)
    h ^= h >> np.uint32(16)
    return h


def perlin3(p, seed):
    """Gradient noise, roughly [-1, 1]. p is (N, 3)."""
    i0 = np.floor(p)
    f = p - i0
    i0 = i0.astype(np.int64)
    u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0)
    out = np.zeros(len(p))
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1.0 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1.0 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1.0 - u[:, 2]
                g = _GRAD[_hash(i0[:, 0] + dx, i0[:, 1] + dy, i0[:, 2] + dz, seed) & np.uint32(15)]
                n = g[:, 0] * (f[:, 0] - dx) + g[:, 1] * (f[:, 1] - dy) + g[:, 2] * (f[:, 2] - dz)
                out += wx * wy * wz * n
    return out


def fbm3(p, seed, octaves=4, lacunarity=2.03, gain=0.5):
    total = np.zeros(len(p))
    amp, norm = 1.0, 0.0
    q = np.asarray(p, np.float64)
    for o in range(octaves):
        total += amp * perlin3(q, seed + 101 * o)
        norm += amp
        amp *= gain
        q = (q @ _ROT) * lacunarity
    return total / norm


def voronoi3(p, seed):
    """F1, F2 (cell units) and a [0,1) random per nearest cell."""
    c = np.floor(p).astype(np.int64)
    f = p - c
    f1 = np.full(len(p), 9.0)
    f2 = np.full(len(p), 9.0)
    cid = np.zeros(len(p), np.uint32)
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            for oz in (-1, 0, 1):
                h = _hash(c[:, 0] + ox, c[:, 1] + oy, c[:, 2] + oz, seed)
                jx = (h & np.uint32(1023)) / 1023.0
                jy = ((h >> np.uint32(10)) & np.uint32(1023)) / 1023.0
                jz = ((h >> np.uint32(20)) & np.uint32(1023)) / 1023.0
                d = (ox + jx - f[:, 0]) ** 2 + (oy + jy - f[:, 1]) ** 2 + (oz + jz - f[:, 2]) ** 2
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d))
                cid = np.where(closer, h, cid)
                f1 = np.where(closer, d, f1)
    rnd = (_hash(cid.astype(np.int64), 7, 11, seed + 3) & np.uint32(0xFFFF)) / 65536.0
    return np.sqrt(f1), np.sqrt(f2), rnd


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    """a + (b - a) t, where a/b may be colours ((3,) or (N, 3)) and t is per point (N,)."""
    a = np.asarray(a, np.float64)
    b = np.asarray(b, np.float64)
    t = np.asarray(t, np.float64)
    if t.ndim == 1 and max(a.ndim, b.ndim) >= 1 and (a.shape[-1:] == (3,) or b.shape[-1:] == (3,)) \
            and not (a.ndim == 1 and a.shape[0] == t.shape[0] and b.ndim == 0):
        t = t[:, None]
    return a + (b - a) * t


# --------------------------------------------------------------------------------------------
# SDF primitives
# --------------------------------------------------------------------------------------------

def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def smax(a, b, k):
    return -smin(-a, -b, k)


def round_box(x, y, z, centre, half, r):
    qx = np.abs(x - centre[0]) - (half[0] - r)
    qy = np.abs(y - centre[1]) - (half[1] - r)
    qz = np.abs(z - centre[2]) - (half[2] - r)
    outside = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2 + np.maximum(qz, 0) ** 2)
    inside = np.minimum(np.maximum(qx, np.maximum(qy, qz)), 0.0)
    return outside + inside - r


def polygon_sdf(qx, qy, poly):
    """Exact signed distance to a closed polygon (negative inside)."""
    d = (qx - poly[0, 0]) ** 2 + (qy - poly[0, 1]) ** 2
    s = np.ones_like(qx)
    j = len(poly) - 1
    for i in range(len(poly)):
        ex, ey = poly[j, 0] - poly[i, 0], poly[j, 1] - poly[i, 1]
        wx, wy = qx - poly[i, 0], qy - poly[i, 1]
        t = np.clip((wx * ex + wy * ey) / (ex * ex + ey * ey), 0.0, 1.0)
        bx, by = wx - ex * t, wy - ey * t
        d = np.minimum(d, bx * bx + by * by)
        c1 = qy >= poly[i, 1]
        c2 = qy < poly[j, 1]
        c3 = ex * wy > ey * wx
        flip = (c1 & c2 & c3) | (~c1 & ~c2 & ~c3)
        s = np.where(flip, -s, s)
        j = i
    return s * np.sqrt(d)


def bezier(p0, p1, p2, p3, n):
    t = np.linspace(0.0, 1.0, n)[:, None]
    p0, p1, p2, p3 = (np.asarray(v, np.float64) for v in (p0, p1, p2, p3))
    return (1 - t) ** 3 * p0 + 3 * (1 - t) ** 2 * t * p1 + 3 * (1 - t) * t ** 2 * p2 + t ** 3 * p3


class Field2D:
    """A polygon's SDF sampled on a fine grid and read back bilinearly (cheap at any point)."""

    def __init__(self, poly, lo, hi, step):
        self.lo = np.asarray(lo, np.float64)
        self.step = step
        nx = int(math.ceil((hi[0] - lo[0]) / step)) + 1
        ny = int(math.ceil((hi[1] - lo[1]) / step)) + 1
        gx = lo[0] + step * np.arange(nx)
        gy = lo[1] + step * np.arange(ny)
        X, Y = np.meshgrid(gx, gy, indexing="ij")
        self.grid = polygon_sdf(X.ravel(), Y.ravel(), poly).reshape(nx, ny)
        self.shape = (nx, ny)

    def __call__(self, a, b):
        A, B = np.broadcast_arrays(np.asarray(a, np.float64), np.asarray(b, np.float64))
        shape = A.shape
        fa = (A.ravel() - self.lo[0]) / self.step
        fb = (B.ravel() - self.lo[1]) / self.step
        ca = np.clip(fa, 0, self.shape[0] - 1.000001)
        cb = np.clip(fb, 0, self.shape[1] - 1.000001)
        ia = np.floor(ca).astype(np.int64)
        ib = np.floor(cb).astype(np.int64)
        ta, tb = ca - ia, cb - ib
        g = self.grid
        v = (g[ia, ib] * (1 - ta) * (1 - tb) + g[ia + 1, ib] * ta * (1 - tb)
             + g[ia, ib + 1] * (1 - ta) * tb + g[ia + 1, ib + 1] * ta * tb)
        # outside the table: add the clamped-away distance (a safe over-estimate)
        v += np.hypot((fa - ca) * self.step, (fb - cb) * self.step)
        return v.reshape(shape)


# --------------------------------------------------------------------------------------------
# the anvil
# --------------------------------------------------------------------------------------------

class Anvil:
    def __init__(self, P, rng):
        self.P = P
        H = self.H = P["anvil_height"]
        self.table_top = H - P["table_drop"]
        L, W, fh = P["base_half_length"], P["base_half_width"], P["foot_height"]
        slab_bottom = H - P["face_thickness"]

        # side silhouette (x, z), counter-clockwise
        side = [(-L, 0.0)]
        arch = np.linspace(-1.0, 1.0, 25)
        side += [(0.095 * a, 0.028 * math.sqrt(max(0.0, 1 - a * a))) for a in arch]
        side += [(L, 0.0), (L, fh - 0.006), (L - 0.004, fh), (L - 0.022, fh)]
        side += [tuple(p) for p in bezier((L - 0.022, fh), (0.098, 0.080), (0.092, 0.170),
                                          (0.212, slab_bottom + 0.002), 28)[1:]]
        side += [(0.235, slab_bottom + 0.022), (0.235, H - 0.040), (-0.150, H - 0.040),
                 (-0.150, 0.200)]
        side += [tuple(p) for p in bezier((-0.150, 0.200), (-0.092, 0.172), (-0.098, 0.080),
                                          (-L + 0.022, fh), 28)[1:]]
        side += [(-L + 0.004, fh), (-L, fh - 0.006)]
        self.side_poly = np.array(side)

        # end silhouette (y, z), symmetric
        right = [(W, fh - 0.006), (W - 0.004, fh), (W - 0.016, fh)]
        right += [tuple(p) for p in bezier((W - 0.016, fh), (0.068, 0.060), (0.050, 0.125),
                                           (0.050, slab_bottom + 0.005), 28)[1:]]
        right += [(0.050, H - 0.040)]
        left = [(-y, z) for (y, z) in reversed(right)]  # top to bottom
        end = [(-W, 0.0)] + [(0.062 * a, 0.022 * math.sqrt(max(0.0, 1 - a * a))) for a in arch]
        end += [(W, 0.0)] + right + left
        self.end_poly = np.array(end)

        self.side = Field2D(self.side_poly, (-0.46, -0.03), (0.38, 0.31), 0.0005)
        self.end = Field2D(self.end_poly, (-0.17, -0.03), (0.17, 0.31), 0.0005)

        # horn: tapered tube along a cubic Bezier in the x-z plane
        r0, rt = P["horn_root_radius"], P["horn_tip_radius"]
        root = (-0.120, self.table_top - r0)
        tip = (P["horn_tip_x"], H - 0.083)
        curve = bezier(root, (-0.235, root[1] + 0.008), (-0.335, tip[1] + 0.016), tip, 800)
        t = np.linspace(0.0, 1.0, 800)
        radius = rt + (r0 - rt) * (1 - t) ** 0.85
        order = np.argsort(curve[:, 0])
        self.hx = curve[order, 0]
        self.hz = curve[order, 1]
        self.hr = radius[order]
        self.hdr = np.gradient(self.hr, self.hx)
        self.x_tip, self.x_root = float(self.hx[0]), float(self.hx[-1])

        # seeded edge chips on the face and heel (the concept's worn, knocked edges)
        fw = P["face_width"] / 2
        chips = [((P["x_step"] + 0.012, -fw - 0.001, H + 0.001), 0.0085)]  # the big one by the step
        for _ in range(7):
            side_sign = -1.0 if rng.random() < 0.6 else 1.0
            x = rng.uniform(P["x_step"] + 0.03, P["x_heel"] - 0.02)
            r = rng.uniform(0.0025, 0.0055)
            chips.append(((x, side_sign * (fw + 0.25 * r), H + 0.25 * r), r))
        chips.append(((P["x_heel"] + 0.002, fw * 0.6, H + 0.002), 0.005))
        chips.append(((P["x_table"] + 0.01, -fw + 0.002, self.table_top + 0.002), 0.004))
        self.chips = chips

    def horn(self, x, y, z):
        xc = np.clip(x, self.x_tip, self.x_root)
        zc = np.interp(xc, self.hx, self.hz)
        rr = np.interp(xc, self.hx, self.hr)
        dr = np.interp(xc, self.hx, self.hdr)
        rho = np.sqrt((x - xc) ** 2 + y ** 2 + (z - zc) ** 2)
        return (rho - rr) / np.sqrt(1.0 + dr * dr)

    def sdf(self, x, y, z):
        P, H = self.P, self.H
        body = smax(self.side(x, z), self.end(y, z), 0.006)
        fw = P["face_width"] / 2
        face = round_box(x, y, z, ((P["x_step"] + P["x_heel"]) / 2, 0.0, H - P["face_thickness"] / 2),
                         ((P["x_heel"] - P["x_step"]) / 2, fw, P["face_thickness"] / 2), 0.0028)
        tt = P["table_thickness"]
        table = round_box(x, y, z, ((P["x_table"] + P["x_step"] + 0.006) / 2, 0.0, self.table_top - tt / 2),
                          ((P["x_step"] + 0.006 - P["x_table"]) / 2, fw - 0.002, tt / 2), 0.0028)
        d = smin(body, face, 0.010)
        d = smin(d, table, 0.006)
        d = smin(d, self.horn(x, y, z), 0.014)
        # hardy (square) and pritchel (round) holes through the heel
        hh = P["hardy_hole"] / 2
        qx = np.abs(x - P["hardy_x"]) - (hh - 0.0015)
        qy = np.abs(y) - (hh - 0.0015)
        hardy = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - 0.0015
        pritchel = np.sqrt((x - P["pritchel_x"]) ** 2 + y ** 2) - P["pritchel_diameter"] / 2
        holes = np.maximum(np.minimum(hardy, pritchel), 0.14 - z)
        d = smax(d, -holes, 0.0018)
        for (cx, cy, cz), r in self.chips:
            d = smax(d, -(np.sqrt((x - cx) ** 2 + (y - cy) ** 2 + (z - cz) ** 2) - r), 0.0012)
        return d

    def bounds(self):
        return (self.x_tip - 0.01, -self.P["base_half_width"] - 0.01, -0.01), \
               (self.P["x_heel"] + 0.01, self.P["base_half_width"] + 0.01, self.H + 0.01)


# --------------------------------------------------------------------------------------------
# the stump
# --------------------------------------------------------------------------------------------

class Stump:
    def __init__(self, P, rng, grid):
        self.P = P
        self.R0 = P["stump_radius"]
        self.Hs = P["face_height"] - P["anvil_height"] + P["anvil_sink"]
        self.taper = P["stump_taper"]
        self.harm = [(m, rng.uniform(0.002, 0.006) * (2.0 if m == 2 else 1.0), rng.uniform(0, 2 * math.pi),
                      rng.uniform(-6, 6)) for m in range(2, 13)]
        self.burls = [(rng.uniform(0, 2 * math.pi), rng.uniform(0.10, 0.44), rng.uniform(0.010, 0.020),
                       rng.uniform(0.18, 0.32), rng.uniform(0.04, 0.08)) for _ in range(4)]
        n = P["root_count"]
        base = rng.uniform(0, 2 * math.pi)
        self.roots = [(base + 2 * math.pi * i / n + rng.uniform(-0.35, 0.35), rng.uniform(0.035, 0.068),
                       rng.uniform(0.22, 0.40), rng.uniform(0.065, 0.12)) for i in range(n)]
        self.flare0, self.flare_l = 0.018, 0.09
        zb, w = P["band_z"], P["band_width"]
        self.band_lo, self.band_hi = zb - w / 2, zb + w / 2
        self.Rb = self.R0 * (1 - self.taper * zb / self.Hs) + 0.002
        wmin = 0.8 * grid
        self.cracks = []
        angles = np.sort(rng.uniform(0, 2 * math.pi, P["crack_count"]))
        for a in angles:
            self.cracks.append(dict(theta=float(a), depth=rng.uniform(0.07, 0.16), z_end=rng.uniform(-0.12, 0.30),
                                    w0=rng.uniform(0.004, 0.0085), wmin=wmin, phase=rng.uniform(0, 6.28),
                                    wig=rng.uniform(0.015, 0.04), phase2=rng.uniform(0, 6.28),
                                    wig2=rng.uniform(0.006, 0.014), phase3=rng.uniform(0, 6.28)))
        self.chips = []
        for _ in range(6):
            a = rng.uniform(0, 2 * math.pi)
            r = rng.uniform(0.010, 0.020)
            rr = self.R0 * (1 - self.taper) + 0.35 * r
            self.chips.append(((rr * math.cos(a), rr * math.sin(a), self.Hs + 0.3 * r), r))

    def band_mask(self, z):
        lo, hi = self.band_lo, self.band_hi
        return smoothstep(lo - 0.012, lo - 0.002, z) * (1.0 - smoothstep(hi + 0.002, hi + 0.012, z))

    def radius(self, th, z):
        zc = np.maximum(z, 0.0)
        R = self.R0 * (1 - self.taper * zc / self.Hs)
        for m, a, ph, b in self.harm:
            R = R + a * np.cos(m * th + ph + b * zc)
        for th_i, A, s, l in self.roots:
            d = (th - th_i + math.pi) % (2 * math.pi) - math.pi
            R = R + A * np.exp(-(d / s) ** 2) * np.exp(-zc / l)
        R = R + self.flare0 * np.exp(-zc / self.flare_l)
        for th_b, zb_, A, sth, sz in self.burls:
            d = (th - th_b + math.pi) % (2 * math.pi) - math.pi
            R = R + A * np.exp(-(d / sth) ** 2 - ((zc - zb_) / sz) ** 2)
        m = self.band_mask(z)
        return R + (self.Rb - 0.0005 - R) * m

    def sdf(self, x, y, z):
        rho = np.sqrt(x * x + y * y)
        th = np.arctan2(y, x)
        e = 0.002
        R = self.radius(th, z)
        dz = (self.radius(th, z + e) - self.radius(th, z - e)) / (2 * e)
        dth = (self.radius(th + 0.01, z) - self.radius(th - 0.01, z)) / 0.02 / np.maximum(rho, 0.05)
        side = (rho - R) / np.sqrt(1.0 + dz * dz + dth * dth)
        d = smax(side, z - self.Hs, self.P["rim_round"])
        d = np.maximum(d, -z)
        for (cx, cy, cz), r in self.chips:
            d = smax(d, -(np.sqrt((x - cx) ** 2 + (y - cy) ** 2 + (z - cz) ** 2) - r), 0.003)
        for c in self.cracks:
            thc = c["theta"] + c["wig"] * np.sin(9.0 * z + c["phase"]) + c["wig2"] * np.sin(31.0 * z + c["phase2"])
            nx, ny = np.cos(thc), np.sin(thc)
            along = x * nx + y * ny
            perp = np.abs(-x * ny + y * nx)
            s = np.clip((z - c["z_end"]) / (self.Hs - c["z_end"]), 0.0, 1.0) ** 0.6
            depth = c["depth"] * s + 0.004
            rin = self.R0 * (1 - self.taper * np.maximum(z, 0) / self.Hs) - depth
            wf = np.clip((along - rin) / depth, 0.0, 1.0) ** 0.8
            breathe = 0.65 + 0.35 * np.sin(17.0 * z + c["phase3"])
            w = c["w0"] * wf * (0.25 + 0.75 * s) * breathe + c["wmin"]
            crack = np.maximum(np.maximum(perp - w, rin - along), 0.012 - depth)
            d = smax(d, -crack, 0.0035)
        return d

    def bounds(self):
        r = self.R0 + max(A for _, A, _, _ in self.roots) + self.flare0 + 0.02
        return (-r, -r, -0.01), (r, r, self.Hs + 0.01)


# --------------------------------------------------------------------------------------------
# surface nets
# --------------------------------------------------------------------------------------------

def sample_grid(sdf, lo, hi, h, chunk=40):
    lo = np.asarray(lo, np.float64) - 0.37 * h  # keep flat faces off grid planes
    n = np.ceil((np.asarray(hi) - lo) / h).astype(int) + 2
    xs = lo[0] + h * np.arange(n[0])
    ys = lo[1] + h * np.arange(n[1])
    zs = lo[2] + h * np.arange(n[2])
    F = np.empty(tuple(n), np.float32)
    for i in range(0, n[0], chunk):
        X = xs[i:i + chunk, None, None]
        v = sdf(X, ys[None, :, None], zs[None, None, :])
        F[i:i + chunk] = np.broadcast_to(v, (len(X), n[1], n[2]))
    return F, lo


class GridField:
    """Trilinear read-back of a sampled field (used for curvature and cavity in the texture pass)."""

    def __init__(self, F, lo, h):
        self.F, self.lo, self.h = F, np.asarray(lo), h

    def __call__(self, p):
        f = (p - self.lo) / self.h
        n = np.array(self.F.shape) - 1.000001
        f = np.clip(f, 0, n)
        i = np.floor(f).astype(np.int64)
        t = f - i
        F = self.F
        out = np.zeros(len(p))
        for dx in (0, 1):
            wx = t[:, 0] if dx else 1 - t[:, 0]
            for dy in (0, 1):
                wy = t[:, 1] if dy else 1 - t[:, 1]
                for dz in (0, 1):
                    wz = t[:, 2] if dz else 1 - t[:, 2]
                    out += wx * wy * wz * F[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
        return out


def _split_codes():
    """Cell corner codes whose inside or outside corners are not edge-connected (two sheets in one cell)."""
    bad = np.zeros(256, bool)
    for code in range(256):
        for want in (1, 0):
            members = [c for c in range(8) if ((code >> c) & 1) == want]
            if not members:
                continue
            seen, stack = {members[0]}, [members[0]]
            while stack:
                c = stack.pop()
                for bit in (1, 2, 4):
                    d = c ^ bit
                    if d in members and d not in seen:
                        seen.add(d)
                        stack.append(d)
            if len(seen) != len(members):
                bad[code] = True
    return bad


_SPLIT = _split_codes()


def ambiguity(F, where=False):
    """(split cells, checkerboard faces): the two cases where surface nets would glue two sheets.

    With where=True, returns the grid corners involved instead of the counts.
    """
    S = (F < 0).astype(np.uint8)
    code = np.zeros(tuple(n - 1 for n in F.shape), np.uint8)
    for c in range(8):
        dx, dy, dz = c & 1, (c >> 1) & 1, (c >> 2) & 1
        code |= S[dx:F.shape[0] - 1 + dx, dy:F.shape[1] - 1 + dy, dz:F.shape[2] - 1 + dz] << np.uint8(c)
    split = np.argwhere(_SPLIT[code])
    corners = [split + np.array([c & 1, (c >> 1) & 1, (c >> 2) & 1]) for c in range(8)]
    checker = 0
    for axis in range(3):
        a, b = [i for i in range(3) if i != axis]

        def corner(da, db):
            idx = [slice(None)] * 3
            idx[a] = slice(da, F.shape[a] - 1 + da)
            idx[b] = slice(db, F.shape[b] - 1 + db)
            return S[tuple(idx)]

        c00, c11, c01, c10 = corner(0, 0), corner(1, 1), corner(0, 1), corner(1, 0)
        faces = np.argwhere((c00 == c11) & (c01 == c10) & (c00 != c01))
        checker += len(faces)
        for da, db in ((0, 0), (1, 1), (0, 1), (1, 0)):
            q = faces.copy()
            q[:, a] += da
            q[:, b] += db
            corners.append(q)
    if where:
        return np.unique(np.concatenate(corners), axis=0) if corners else np.zeros((0, 3), int)
    return len(split), checker


def repair_ambiguity(F, rounds=30):
    """Re-average the samples around any ambiguous cell with their six neighbours until none is left.

    It only touches the few corners involved (a sub-voxel sliver or pinch), so the surface does not
    move visibly; the count of touched samples is reported.
    """
    touched = 0
    for _ in range(rounds):
        pts = ambiguity(F, where=True)
        if not len(pts):
            break
        pts = pts[(pts > 0).all(1) & (pts < np.array(F.shape) - 1).all(1)]
        if not len(pts):
            break
        i, j, k = pts.T
        avg = (F[i, j, k] + F[i - 1, j, k] + F[i + 1, j, k] + F[i, j - 1, k] + F[i, j + 1, k]
               + F[i, j, k - 1] + F[i, j, k + 1]) / 7.0
        F[i, j, k] = avg
        touched += len(pts)
    return touched, ambiguity(F)


def sample_clean(sdf, lo, hi, h, tries=4):
    """Sample the field, sliding the grid by sub-cell steps until no cell is ambiguous (deterministic);
    if none of the offsets is clean, repair the best one locally."""
    best = None
    for k in range(tries):
        F, origin = sample_grid(sdf, np.asarray(lo) + k * 0.173 * h, hi, h)
        amb = ambiguity(F)
        if best is None or sum(amb) < sum(best[2]):
            best = (F, origin, amb, k)
        if sum(amb) == 0:
            break
    F, origin, amb, k = best
    touched = 0
    if sum(amb):
        touched, amb = repair_ambiguity(F)
    return F, origin, amb, (k, touched)


def surface_nets(F, lo, h):
    nx, ny, nz = F.shape
    s = F < 0
    cnt = np.zeros((nx - 1, ny - 1, nz - 1), np.uint8)
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                cnt += s[dx:nx - 1 + dx, dy:ny - 1 + dy, dz:nz - 1 + dz]
    active = (cnt > 0) & (cnt < 8)
    ci, cj, ck = np.nonzero(active)
    index = np.full(active.shape, -1, np.int64)
    index[ci, cj, ck] = np.arange(len(ci))
    corners = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0), (0, 0, 1), (1, 0, 1), (0, 1, 1), (1, 1, 1)]
    acc = np.zeros((len(ci), 3))
    num = np.zeros(len(ci))
    for a in range(8):
        for b in range(a + 1, 8):
            oa, ob = np.array(corners[a]), np.array(corners[b])
            if np.abs(oa - ob).sum() != 1:
                continue
            fa = F[ci + oa[0], cj + oa[1], ck + oa[2]].astype(np.float64)
            fb = F[ci + ob[0], cj + ob[1], ck + ob[2]].astype(np.float64)
            m = (fa < 0) != (fb < 0)
            t = np.where(m, fa / np.where(m, fa - fb, 1.0), 0.0)
            acc[m] += oa + t[m, None] * (ob - oa)
            num[m] += 1
    verts = lo + h * (np.stack([ci, cj, ck], 1) + acc / num[:, None])
    quads = []
    e = s[:-1, 1:-1, 1:-1] != s[1:, 1:-1, 1:-1]
    i, j, k = np.nonzero(e)
    j, k = j + 1, k + 1
    q = np.stack([index[i, j - 1, k - 1], index[i, j, k - 1], index[i, j, k], index[i, j - 1, k]], 1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    e = s[1:-1, :-1, 1:-1] != s[1:-1, 1:, 1:-1]
    i, j, k = np.nonzero(e)
    i, k = i + 1, k + 1
    q = np.stack([index[i - 1, j, k - 1], index[i - 1, j, k], index[i, j, k], index[i, j, k - 1]], 1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    e = s[1:-1, 1:-1, :-1] != s[1:-1, 1:-1, 1:]
    i, j, k = np.nonzero(e)
    i, j = i + 1, j + 1
    q = np.stack([index[i - 1, j - 1, k], index[i, j - 1, k], index[i, j, k], index[i - 1, j, k]], 1)
    flip = ~s[i, j, k]
    q[flip] = q[flip][:, ::-1]
    quads.append(q)
    return verts, np.concatenate(quads)


def project(verts, sdf, h, iters=2):
    p = verts.copy()
    e = 0.25 * h
    for _ in range(iters):
        x, y, z = p[:, 0], p[:, 1], p[:, 2]
        f = sdf(x, y, z)
        g = np.stack([(sdf(x + e, y, z) - sdf(x - e, y, z)),
                      (sdf(x, y + e, z) - sdf(x, y - e, z)),
                      (sdf(x, y, z + e) - sdf(x, y, z - e))], 1) / (2 * e)
        g2 = (g * g).sum(1) + 1e-12
        p = p - (f / g2)[:, None] * g
        delta = p - verts
        n = np.linalg.norm(delta, axis=1)
        lim = 0.6 * h
        scale = np.where(n > lim, lim / np.maximum(n, 1e-12), 1.0)
        p = verts + delta * scale[:, None]
    return p


# --------------------------------------------------------------------------------------------
# Blender mesh helpers
# --------------------------------------------------------------------------------------------

def new_object(name, verts, faces):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    mesh.validate()
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def decimate(obj, target_tris):
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    mod = obj.modifiers.new("decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, target_tris / tris)
    mod.use_collapse_triangulate = True
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    obj.modifiers.clear()
    old = obj.data
    obj.data = mesh
    bpy.data.meshes.remove(old)
    return tris


def finish_part(obj, sharp_deg):
    """Triangulate, make winding consistent and outward, flag sharp edges by angle, smooth shade."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3],
                          quad_method="BEAUTY", ngon_method="BEAUTY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    thr = math.radians(sharp_deg)
    for e in bm.edges:
        e.smooth = not (len(e.link_faces) == 2 and e.calc_face_angle(0.0) > thr)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def solid_report(mesh):
    """Closedness, winding and orientation of every connected component."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    boundary = sum(1 for e in bm.edges if len(e.link_faces) == 1)
    nonmanifold = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    inconsistent = 0
    for e in bm.edges:
        if len(e.link_faces) == 2:
            a, b = e.link_faces
            la = [l for l in a.loops if l.edge == e][0]
            lb = [l for l in b.loops if l.edge == e][0]
            if la.vert == lb.vert:
                inconsistent += 1
    comp = {}
    seen = set()
    comps = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, members = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            members.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        vol = 0.0
        for g in members:
            v = [l.vert.co for l in g.loops]
            for i in range(1, len(v) - 1):
                vol += v[0].dot(v[i].cross(v[i + 1])) / 6.0
        comps.append(vol)
    zero_area = sum(1 for f in bm.faces if f.calc_area() < 1e-10)
    bm.free()
    return {"components": len(comps), "negative_volume_components": sum(1 for v in comps if v <= 0),
            "boundary_edges": boundary, "nonmanifold_edges": nonmanifold,
            "inconsistent_winding_edges": inconsistent, "zero_area_faces": zero_area,
            "volume_m3": float(sum(comps))}


def uv_area(mesh):
    if not mesh.uv_layers:
        return 0.0
    uv = np.empty(len(mesh.loops) * 2)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    total = 0.0
    for p in mesh.polygons:
        idx = list(p.loop_indices)
        a = uv[idx]
        total += 0.5 * abs(np.sum(a[:, 0] * np.roll(a[:, 1], -1) - np.roll(a[:, 0], -1) * a[:, 1]))
    return total


def mesh_faces(mesh):
    """Triangle vertex indices, face normals/centres and edge-adjacent face pairs as numpy arrays."""
    nl = len(mesh.loops)
    lv = np.empty(nl, np.int64)
    mesh.loops.foreach_get("vertex_index", lv)
    le = np.empty(nl, np.int64)
    mesh.loops.foreach_get("edge_index", le)
    tri = lv.reshape(-1, 3)  # parts are triangulated before UVs
    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    fn = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("normal", fn)
    fn = fn.reshape(-1, 3)
    fc = co[tri].mean(1)
    face_of_loop = np.repeat(np.arange(len(tri)), 3)
    order = np.argsort(le, kind="stable")
    se, sf = le[order], face_of_loop[order]
    same = se[1:] == se[:-1]
    pairs = np.stack([sf[:-1][same], sf[1:][same]], 1)
    return tri, co, fn, fc, pairs


def components(labels, pairs):
    lab = np.arange(len(labels))
    p = pairs[labels[pairs[:, 0]] == labels[pairs[:, 1]]]
    while True:
        m = np.minimum(lab[p[:, 0]], lab[p[:, 1]])
        new = lab.copy()
        np.minimum.at(new, p[:, 0], m)
        np.minimum.at(new, p[:, 1], m)
        new = new[new]
        if np.array_equal(new, lab):
            break
        lab = new
    return np.unique(lab, return_inverse=True)[1]


def smooth_labels(labels, pairs, min_size, rounds=6):
    """Fold small projection-class patches into their neighbours so islands stay whole."""
    labels = labels.copy()
    k = int(labels.max()) + 1
    for _ in range(rounds):
        comp = components(labels, pairs)
        small = np.bincount(comp)[comp] < min_size
        if not small.any():
            break
        a, b = pairs[:, 0], pairs[:, 1]
        diff = labels[a] != labels[b]
        m1, m2 = small[a] & diff, small[b] & diff
        vc = np.concatenate([comp[a[m1]], comp[b[m2]]])
        vl = np.concatenate([labels[b[m1]], labels[a[m2]]])
        if not len(vc):
            break
        votes = np.bincount(vc * k + vl, minlength=(comp.max() + 1) * k).reshape(-1, k)
        has = votes.sum(1) > 0
        best = votes.argmax(1)
        change = small & has[comp]
        labels[change] = best[comp[change]]
    return labels


# planar projection bases per axis class: (u axis, v axis) with u x v = the class normal
_AXIS_UV = {0: ((0, 1, 0), (0, 0, 1)), 1: ((0, -1, 0), (0, 0, 1)), 2: ((-1, 0, 0), (0, 0, 1)),
            3: ((1, 0, 0), (0, 0, 1)), 4: ((1, 0, 0), (0, 1, 0)), 5: ((1, 0, 0), (0, -1, 0))}


def axis_classes(fn):
    """0:+X 1:-X 2:+Y 3:-Y 4:+Z 5:-Z by dominant normal component."""
    ax = np.abs(fn).argmax(1)
    neg = fn[np.arange(len(fn)), ax] < 0
    return ax * 2 + neg


def set_uvs(mesh, loop_uv):
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    mesh.uv_layers.active.data.foreach_set("uv", loop_uv.ravel())


def planar_class_uv(obj, min_size):
    """Box projection in the part's own frame, with class patches merged so islands stay whole."""
    mesh = obj.data
    tri, co, fn, fc, pairs = mesh_faces(mesh)
    raw = axis_classes(fn)
    labels = smooth_labels(raw, pairs, min_size)
    axes = np.array([(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)], np.float64)
    invalid = (fn * axes[labels]).sum(1) < 0.3  # never project a face nearly edge-on
    labels[invalid] = raw[invalid]
    uv = np.zeros((len(tri), 3, 2))
    for c, (ua, va) in _AXIS_UV.items():
        m = labels == c
        p = co[tri[m]]
        uv[m, :, 0] = p @ np.array(ua, np.float64)
        uv[m, :, 1] = p @ np.array(va, np.float64)
    set_uvs(mesh, uv)
    return int(components(labels, pairs).max() + 1)


def stump_uv(obj, R0):
    """Cylindrical side (grain along V), planar end grain and base, radial crack walls."""
    mesh = obj.data
    tri, co, fn, fc, pairs = mesh_faces(mesh)
    rho = np.maximum(np.hypot(fc[:, 0], fc[:, 1]), 1e-6)
    tangent = np.stack([-fc[:, 1] / rho, fc[:, 0] / rho, 0 * rho], 1)
    labels = np.zeros(len(tri), np.int64)  # 0 side, 1 top, 2 bottom, 3 crack wall
    labels[np.abs((fn * tangent).sum(1)) > 0.8] = 3
    labels[fn[:, 2] > 0.7] = 1
    labels[fn[:, 2] < -0.7] = 2
    raw = labels.copy()
    labels = smooth_labels(labels, pairs, 24)
    radial = np.stack([fc[:, 0] / rho, fc[:, 1] / rho, 0 * rho], 1)
    fit = np.stack([(fn * radial).sum(1), fn[:, 2], -fn[:, 2], np.abs((fn * tangent).sum(1))], 1)
    invalid = fit[np.arange(len(labels)), labels] < 0.3
    labels[invalid] = raw[invalid]
    p = co[tri]
    uv = np.zeros((len(tri), 3, 2))
    m = labels == 1
    uv[m] = p[m][:, :, :2]
    m = labels == 2  # the underside stands on the floor and is never seen: a small island is enough
    uv[m, :, 0], uv[m, :, 1] = 0.15 * p[m][:, :, 0], -0.15 * p[m][:, :, 1]
    m = labels == 3
    uv[m, :, 0], uv[m, :, 1] = np.hypot(p[m][:, :, 0], p[m][:, :, 1]), p[m][:, :, 2]
    m = labels == 0
    th = np.arctan2(p[m][:, :, 1], p[m][:, :, 0])
    th = th + 2 * np.pi * np.round((th[:, :1] - th) / (2 * np.pi))
    uv[m, :, 0], uv[m, :, 1] = th * R0, p[m][:, :, 2]
    set_uvs(mesh, uv)
    return int(components(labels, pairs).max() + 1)


# ------------------------------------------------------------------ parametric iron parts

def mesh_from_bm(name, bm):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def ring_solid(name, radius_in, thickness, z0, width, th0, th1, segments, chamfer, closed):
    """A band (full ring or arc) with a chamfered rectangular section, UVs along the circumference."""
    c = chamfer
    sec = [(0.0, -width / 2 + c), (c, -width / 2), (thickness - c, -width / 2), (thickness, -width / 2 + c),
           (thickness, width / 2 - c), (thickness - c, width / 2), (c, width / 2), (0.0, width / 2 - c)]
    per = [0.0]
    for i in range(len(sec)):
        a, b = sec[i], sec[(i + 1) % len(sec)]
        per.append(per[-1] + math.hypot(b[0] - a[0], b[1] - a[1]))
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    n_ring = segments if closed else segments + 1
    rings = []
    for a in range(n_ring):
        th = th0 + (th1 - th0) * a / segments
        ring = []
        for (dr, dz) in sec:
            r = radius_in + dr
            ring.append(bm.verts.new((r * math.cos(th), r * math.sin(th), z0 + dz)))
        rings.append(ring)
    arc = (th1 - th0) * (radius_in + thickness / 2)
    ns = len(sec)
    for a in range(segments):
        r0, r1 = rings[a], rings[(a + 1) % n_ring]
        for b in range(ns):
            f = bm.faces.new((r0[b], r1[b], r1[(b + 1) % ns], r0[(b + 1) % ns]))
            u0, u1 = arc * a / segments, arc * (a + 1) / segments
            v0, v1 = per[b], per[b + 1]
            for loop, uvv in zip(f.loops, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))):
                loop[uvl].uv = uvv
    if not closed:
        for ring, off in ((rings[0], arc + 0.01), (rings[-1], arc + 0.03)):
            f = bm.faces.new(ring)
            for loop in f.loops:
                i = ring.index(loop.vert)
                loop[uvl].uv = (off + sec[i][0], sec[i][1] + width / 2)
    return mesh_from_bm(name, bm)


def rivet(name, centre, normal, radius, height, segments=12, rows=4, sink=0.0008):
    """A domed rivet head: a spherical cap whose sphere carries on 0.8 mm into the band, closed by a disk."""
    n = Vector(normal).normalized()
    e1 = Vector((0, 0, 1)).cross(n)
    if e1.length < 1e-6:
        e1 = Vector((1, 0, 0))
    e1.normalize()
    e2 = n.cross(e1)
    Rs = (radius * radius + height * height) / (2 * height)
    phi_low = math.acos((Rs - height - sink) / Rs)
    sphere_c = Vector(centre) - n * (Rs - height)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    rings = []
    for k in range(rows):
        phi = phi_low * (1 - k / rows)
        rings.append([bm.verts.new(sphere_c + (n * math.cos(phi) + (e1 * math.cos(2 * math.pi * s / segments)
                      + e2 * math.sin(2 * math.pi * s / segments)) * math.sin(phi)) * Rs) for s in range(segments)])
    apex = bm.verts.new(sphere_c + n * Rs)
    origin = Vector(centre)

    def uv_of(v, du=0.0):
        d = v.co - origin
        return (d.dot(e1) + du, d.dot(e2))

    for k in range(rows - 1):
        for s in range(segments):
            f = bm.faces.new((rings[k][s], rings[k][(s + 1) % segments], rings[k + 1][(s + 1) % segments], rings[k + 1][s]))
            for loop in f.loops:
                loop[uvl].uv = uv_of(loop.vert)
    for s in range(segments):
        f = bm.faces.new((rings[-1][s], rings[-1][(s + 1) % segments], apex))
        for loop in f.loops:
            loop[uvl].uv = uv_of(loop.vert)
    f = bm.faces.new(list(reversed(rings[0])))
    for loop in f.loops:
        loop[uvl].uv = uv_of(loop.vert, 0.03)
    return mesh_from_bm(name, bm)


def hold_down_dog(name, x, side, profile, width, frame):
    """An L-shaped wrought hold-down: over the anvil foot, bent down and driven into the stump."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    ax, az = frame
    vs = []
    for dx in (-width / 2, width / 2):
        vs.append([bm.verts.new((ax + x + dx, side * s, az + z)) for (s, z) in profile])
    n = len(profile)
    faces = [bm.faces.new(vs[0]), bm.faces.new(list(reversed(vs[1])))]
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((vs[0][i], vs[0][j], vs[1][j], vs[1][i])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=0.0008, segments=1, affect="EDGES",
                    profile=0.5, clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method="BEAUTY",
                          ngon_method="EAR_CLIP")
    for f in bm.faces:
        nrm = f.normal
        axis = max(range(3), key=lambda i: abs(nrm[i]))
        u_ax, v_ax = [i for i in range(3) if i != axis]
        for loop in f.loops:
            loop[uvl].uv = (loop.vert.co[u_ax], loop.vert.co[v_ax])
    return mesh_from_bm(name, bm)


def uv_islands(mesh):
    """Faces joined across edges whose two sides carry the same UVs (triangulated mesh)."""
    nl = len(mesh.loops)
    uv = np.empty(nl * 2)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    le = np.empty(nl, np.int64)
    mesh.loops.foreach_get("edge_index", le)
    order = np.argsort(le, kind="stable")
    same = le[order][1:] == le[order][:-1]
    l1, l2 = order[:-1][same], order[1:][same]
    nxt = lambda l: 3 * (l // 3) + (l % 3 + 1) % 3
    cont = (np.abs(uv[l1] - uv[nxt(l2)]).max(1) < 1e-7) & (np.abs(uv[nxt(l1)] - uv[l2]).max(1) < 1e-7)
    pairs = np.stack([l1[cont] // 3, l2[cont] // 3], 1)
    return components(np.zeros(nl // 3, np.int64), pairs), uv


def skyline(sizes, W, H):
    """Bottom-left skyline packing of integer rectangles, each tried upright and turned 90 deg."""
    order = sorted(range(len(sizes)), key=lambda i: (-max(sizes[i]), -min(sizes[i]), i))
    sky = [[0, 0, W]]
    place = [None] * len(sizes)
    for i in order:
        best = None
        for turn in (0, 1):
            w, h = sizes[i] if not turn else sizes[i][::-1]
            for j in range(len(sky)):
                x = sky[j][0]
                if x + w > W:
                    break
                y, k, rem = 0, j, w
                while rem > 0:
                    y = max(y, sky[k][1])
                    rem -= sky[k][2]
                    k += 1
                if y + h > H:
                    continue
                key = (y + h, y, x)
                if best is None or key < best[0]:
                    best = (key, x, y, w, h, turn)
        if best is None:
            return None
        _, x, y, w, h, turn = best
        place[i] = (x, y, turn)
        new = []
        for sx, sy, sw in sky:
            ex = sx + sw
            if ex <= x or sx >= x + w:
                new.append([sx, sy, sw])
                continue
            if sx < x:
                new.append([sx, sy, x - sx])
            if ex > x + w:
                new.append([x + w, sy, ex - x - w])
        new.append([x, y + h, w])
        new.sort()
        sky = [new[0]]
        for seg in new[1:]:
            if seg[1] == sky[-1][1] and seg[0] == sky[-1][0] + sky[-1][2]:
                sky[-1][2] += seg[2]
            else:
                sky.append(seg)
    return place


def pack_atlas(mesh, atlas, gap):
    """Pack the metre-scaled islands into one atlas at a single texel density (largest that fits)."""
    comp, uv = uv_islands(mesh)
    loop_island = np.repeat(comp, 3)
    n = int(comp.max()) + 1
    lo = np.full((n, 2), np.inf)
    hi = np.full((n, 2), -np.inf)
    np.minimum.at(lo, loop_island, uv)
    np.maximum.at(hi, loop_island, uv)
    ext = hi - lo
    placed, s_lo, s_hi = None, 50.0, atlas / max(1e-6, float(ext.max())) * 0.999
    for _ in range(28):
        s_mid = 0.5 * (s_lo + s_hi)
        sizes = [(int(math.ceil(e[0] * s_mid)) + gap, int(math.ceil(e[1] * s_mid)) + gap) for e in ext]
        res = skyline(sizes, atlas, atlas)
        if res is None:
            s_hi = s_mid
        else:
            s_lo, placed = s_mid, (res, s_mid)
    place, s = placed
    local = (uv - lo[loop_island]) * s
    out = np.empty_like(uv)
    for i, (x, y, turn) in enumerate(place):
        m = loop_island == i
        u, v = local[m, 0], local[m, 1]
        if turn:
            u, v = ext[i, 1] * s - v, u
        out[m, 0] = (x + gap / 2 + u) / atlas
        out[m, 1] = (y + gap / 2 + v) / atlas
    mesh.uv_layers.active.data.foreach_set("uv", out.ravel())
    fill = float((ext[:, 0] * ext[:, 1]).sum() * s * s / atlas / atlas)
    return s, n, fill


# --------------------------------------------------------------------------------------------
# texture evaluation
# --------------------------------------------------------------------------------------------

def mesh_arrays(mesh):
    mesh.calc_loop_triangles()
    mesh.calc_tangents(uvmap="UVMap")
    nl = len(mesh.loops)
    tl = np.empty(len(mesh.loop_triangles) * 3, np.int64)
    mesh.loop_triangles.foreach_get("loops", tl)
    tl = tl.reshape(-1, 3)
    tp = np.empty(len(mesh.loop_triangles), np.int64)
    mesh.loop_triangles.foreach_get("polygon_index", tp)
    lv = np.empty(nl, np.int64)
    mesh.loops.foreach_get("vertex_index", lv)
    co = np.empty(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    uv = np.empty(nl * 2)
    mesh.uv_layers.active.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    cn = np.empty(nl * 3)
    mesh.corner_normals.foreach_get("vector", cn)
    cn = cn.reshape(-1, 3)
    tg = np.empty(nl * 3)
    mesh.loops.foreach_get("tangent", tg)
    tg = tg.reshape(-1, 3)
    bs = np.empty(nl)
    mesh.loops.foreach_get("bitangent_sign", bs)
    return dict(tri_loops=tl, tri_poly=tp, loop_vert=lv, co=co, uv=uv, cn=cn, tg=tg, bs=bs)


def rasterize(uv_tri, size):
    tri_id = np.full((size, size), -1, np.int32)
    bary = np.zeros((size, size, 3), np.float32)
    P = uv_tri * size - 0.5
    eps = 1e-4
    for t in range(len(P)):
        a, b, c = P[t]
        x0 = max(int(math.ceil(min(a[0], b[0], c[0]) - 0.5)), 0)
        x1 = min(int(math.floor(max(a[0], b[0], c[0]) + 0.5)), size - 1)
        y0 = max(int(math.ceil(min(a[1], b[1], c[1]) - 0.5)), 0)
        y1 = min(int(math.floor(max(a[1], b[1], c[1]) + 0.5)), size - 1)
        if x1 < x0 or y1 < y0:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        v0x, v0y = b[0] - a[0], b[1] - a[1]
        v1x, v1y = c[0] - a[0], c[1] - a[1]
        den = v0x * v1y - v1x * v0y
        if abs(den) < 1e-12:
            continue
        v2x, v2y = X - a[0], Y - a[1]
        w1 = (v2x * v1y - v1x * v2y) / den
        w2 = (v0x * v2y - v2x * v0y) / den
        w0 = 1.0 - w1 - w2
        m = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
        if not m.any():
            continue
        tri_id[Y[m], X[m]] = t
        bary[Y[m], X[m], 0] = w0[m]
        bary[Y[m], X[m], 1] = w1[m]
        bary[Y[m], X[m], 2] = w2[m]
    return tri_id, bary


def normalize(v):
    return v / np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-12)


def convexity(field, p, r):
    """Mean field value on a small sphere minus the centre value: + on convex edges, - in crevices."""
    dirs = np.array([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]]
                    + [[sx, sy, sz] for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)], np.float64)
    dirs = normalize(dirs)
    base = field(p)
    acc = np.zeros(len(p))
    for d in dirs:
        acc += field(p + r * d)
    return acc / len(dirs) - base


def field_normal(field, p, e):
    g = np.stack([field(p + [e, 0, 0]) - field(p - [e, 0, 0]),
                  field(p + [0, e, 0]) - field(p - [0, e, 0]),
                  field(p + [0, 0, e]) - field(p - [0, 0, e])], 1)
    return normalize(g)


# ------------------------------------------------------------------ materials

def anvil_material(pl, n, conv, A, seed):
    """Forged iron body, polished working face. pl: anvil-local points; n: surface normal."""
    P = A.P
    H = A.H
    x, y, z = pl[:, 0], pl[:, 1], pl[:, 2]
    nz = n[:, 2]
    # masks
    top = smoothstep(0.86, 0.96, nz) * smoothstep(A.table_top - 0.012, A.table_top - 0.004, z)
    horn_top = smoothstep(0.30, 0.85, nz) * smoothstep(P["x_table"] + 0.01, P["x_table"] - 0.02, x)
    edge = smoothstep(0.00012, 0.0006, conv)
    crev = smoothstep(-0.0001, -0.0008, conv)
    wear_noise = fbm3(pl * 22.0, seed + 1, 3)
    polish = np.clip(np.maximum(top, 0.35 * horn_top * smoothstep(-0.2, 0.3, wear_noise)), 0, 1)
    face_edge = edge * smoothstep(A.table_top - 0.03, A.table_top - 0.006, z)
    polish = np.maximum(polish, 0.8 * face_edge)

    # body iron
    big = fbm3(pl * 14.0, seed + 2, 4)
    tint = fbm3(pl * 5.0, seed + 3, 3)
    body = lerp([0.026, 0.027, 0.030], [0.052, 0.051, 0.054], smoothstep(-0.5, 0.6, big))
    body = lerp(body, lerp([0.030, 0.034, 0.043], [0.046, 0.037, 0.031], smoothstep(-0.4, 0.4, tint)), 0.35)
    rough = 0.50 + 0.12 * big
    metal = np.full(len(pl), 0.35)
    # mill-scale mottling: patches of slightly different tone and sheen
    cm = smoothstep(-0.45, 0.45, fbm3(pl * 13.0 + 3.1, seed + 15, 4))
    body = body * (0.82 + 0.36 * cm)[:, None]
    rough = rough + 0.16 * (cm - 0.5)
    # forge-scale flecks (the concept's pale specks) and pits
    f1, _, cell = voronoi3(pl * 170.0, seed + 4)
    fleck = smoothstep(0.17, 0.08, f1) * (cell < 0.16) * (1 - polish)
    f1p, _, cellp = voronoi3(pl * 80.0 + 13.7, seed + 5)
    pit = smoothstep(0.16, 0.06, f1p) * (cellp < 0.20)
    body = lerp(body, [0.30, 0.295, 0.285], fleck)
    rough = lerp(rough, 0.78, fleck)
    metal = lerp(metal, 0.15, fleck)
    body = lerp(body, lerp([0.010, 0.010, 0.011], [0.060, 0.028, 0.012], smoothstep(0.3, 0.7, cellp * 5 % 1.0)), pit * 0.9)
    rough = lerp(rough, 0.8, pit)
    # rust and grime in crevices and low on the feet
    rust_amt = np.clip(crev * (0.55 + 0.45 * smoothstep(-0.2, 0.5, fbm3(pl * 30, seed + 6, 3)))
                       + 0.45 * smoothstep(0.07, 0.0, z) * smoothstep(-0.1, 0.5, fbm3(pl * 18, seed + 7, 3)), 0, 0.75)
    rust = lerp([0.052, 0.026, 0.013], [0.095, 0.045, 0.018], smoothstep(-0.4, 0.5, fbm3(pl * 60, seed + 8, 2)))
    body = lerp(body, rust, rust_amt)
    rough = lerp(rough, 0.86, rust_amt)
    metal = lerp(metal, 0.12, rust_amt)
    # ash dust on the foot ledges
    dust = smoothstep(0.75, 0.95, nz) * smoothstep(0.075, 0.045, z) * 0.55
    body = lerp(body, [0.085, 0.080, 0.075], dust)
    rough = lerp(rough, 0.92, dust)
    metal = lerp(metal, 0.0, dust)
    # worn bright edges
    wear = edge * (0.6 + 0.4 * smoothstep(-0.3, 0.3, wear_noise)) * (1 - rust_amt)
    body = lerp(body, [0.20, 0.20, 0.21], wear * 0.8)
    rough = lerp(rough, 0.32, wear)
    metal = lerp(metal, 0.95, wear)
    # weld line of the steel face plate along the slab sides
    seam = (smoothstep(0.0013, 0.0004, np.abs(z - (H - 0.017))) * smoothstep(0.5, 0.2, np.abs(nz))
            * (x > P["x_step"] + 0.004))
    body = lerp(body, [0.012, 0.012, 0.013], seam * 0.85)

    # polished steel face
    oxide = smoothstep(0.15, 0.65, fbm3(pl * 6.0, seed + 9, 4))
    scratch = np.abs(perlin3(pl * np.array([7.0, 520.0, 90.0]), seed + 10))
    scratch2 = np.abs(perlin3((pl @ np.array(Matrix.Rotation(0.7, 3, "Z"))) * np.array([9.0, 380.0, 80.0]), seed + 11))
    s_line = smoothstep(0.06, 0.0, scratch) + 0.6 * smoothstep(0.05, 0.0, scratch2)
    f1d, _, celld = voronoi3(pl * 55.0, seed + 12)
    ding = smoothstep(0.22, 0.10, f1d) * (celld < 0.18)
    steel = lerp([0.47, 0.47, 0.49], [0.30, 0.295, 0.30], oxide * 0.55)
    steel = lerp(steel, [0.60, 0.60, 0.62], np.clip(s_line, 0, 1) * 0.35)
    steel = lerp(steel, [0.12, 0.12, 0.125], ding * 0.6)
    s_rough = 0.20 + 0.20 * oxide + 0.08 * np.clip(s_line, 0, 1) + 0.15 * ding
    base = lerp(body, steel, polish)
    rough = lerp(rough, s_rough, polish)
    metal = lerp(metal, 1.0, polish)

    masks = dict(polish=polish, seam=seam, pit=pit, ding=ding, fleck=fleck)

    def height(q):
        f1h, _, _ = voronoi3(q * 30.0, seed + 13)
        hammer = -0.0006 * (f1h ** 2)
        fine = 0.00007 * fbm3(q * 400.0, seed + 14, 2)
        f1q, _, cq = voronoi3(q * 80.0 + 13.7, seed + 5)
        pits = -0.0005 * smoothstep(0.16, 0.06, f1q) * (cq < 0.20)
        f1s, _, cs = voronoi3(q * 55.0, seed + 12)
        dings = -0.00025 * smoothstep(0.22, 0.10, f1s) * (cs < 0.18)
        scr = np.abs(perlin3(q * np.array([7.0, 520.0, 90.0]), seed + 10))
        scr = -0.00003 * smoothstep(0.06, 0.0, scr)
        body_h = hammer + fine + pits
        face_h = 0.15 * hammer + dings + scr
        seam_h = -0.00025 * masks["seam"]
        return body_h * (1 - masks["polish"]) + face_h * masks["polish"] + seam_h

    return base, rough, metal, height


def stump_material(pl, n, conv, S, A_rect, seed):
    """Bark on the sides, weathered end grain on top, scale flakes and soot around the anvil."""
    x, y, z = pl[:, 0], pl[:, 1], pl[:, 2]
    nz = n[:, 2]
    top = smoothstep(0.55, 0.85, nz) * smoothstep(S.Hs - 0.03, S.Hs - 0.01, z)
    crev = smoothstep(-0.0002, -0.0022, conv)
    # --- bark: deep branching furrows, rough fibrous ridges broken into plates
    def bark_fields(q):
        warp = fbm3(q * np.array([7.0, 7.0, 1.6]), seed + 19, 3)
        warp2 = fbm3(q * np.array([25.0, 25.0, 5.0]), seed + 34, 2)
        n1 = perlin3(q * np.array([26.0, 26.0, 1.5]) + (0.9 * warp + 0.25 * warp2)[:, None], seed + 20)
        n2 = perlin3(q * np.array([58.0, 58.0, 3.5]) + (0.5 * warp2)[:, None], seed + 35)
        ridge = smoothstep(0.0, 0.38, np.abs(n1)) * (0.62 + 0.38 * smoothstep(0.0, 0.20, np.abs(n2)))
        brk = smoothstep(0.0, 0.12, np.abs(perlin3(q * np.array([14.0, 14.0, 9.0]), seed + 31)))
        plate = ridge * (0.5 + 0.5 * brk)
        fibre = perlin3(q * np.array([380.0, 380.0, 14.0]), seed + 36)
        tex = fbm3(q * np.array([70.0, 70.0, 22.0]), seed + 37, 3)
        return ridge, plate, fibre, tex

    ridge, plate, fibre, tex = bark_fields(pl)
    tone = fbm3(pl * np.array([12.0, 12.0, 3.0]), seed + 21, 4)
    wmix = fbm3(pl * np.array([9.0, 9.0, 3.0]), seed + 32, 4)
    top_col = lerp([0.105, 0.048, 0.020], [0.195, 0.090, 0.038], smoothstep(-0.5, 0.5, tone))
    top_col = lerp(top_col, [0.115, 0.098, 0.082], smoothstep(0.0, 0.55, wmix) * smoothstep(0.5, 0.9, plate) * 0.6)
    top_col = top_col * (1.0 + 0.16 * fibre + 0.18 * tex)[:, None]
    bark = lerp([0.016, 0.008, 0.005], [0.062, 0.027, 0.012], smoothstep(0.0, 0.35, plate))
    bark = lerp(bark, top_col, smoothstep(0.30, 0.80, plate))
    f1p, _, cp = voronoi3(pl * np.array([90.0, 90.0, 40.0]), seed + 38)
    bpit = smoothstep(0.22, 0.08, f1p) * (cp < 0.25) * smoothstep(0.4, 0.8, plate)
    bark = lerp(bark, [0.025, 0.012, 0.007], bpit * 0.8)
    # patches where the bark has fallen away: weathered sapwood with vertical grain
    patch = smoothstep(0.12, 0.26, fbm3(pl * np.array([5.0, 5.0, 1.7]), seed + 22, 4))
    grain = perlin3(pl * np.array([240.0, 240.0, 5.0]), seed + 23)
    wood = lerp([0.17, 0.075, 0.030], [0.30, 0.150, 0.062], smoothstep(-0.5, 0.5, grain))
    wood = lerp(wood, [0.14, 0.115, 0.090], smoothstep(0.1, 0.6, wmix) * 0.35)
    side = lerp(bark, wood, patch)
    side_rough = lerp(lerp(0.96, 0.82, plate) + 0.05 * tex, 0.80, patch)
    # soot and dust darkening toward the top of the side
    side = side * lerp(1.0, 0.72, smoothstep(0.25, S.Hs, z))[:, None]
    # --- end grain: uneven growth rings, fine drying checks, weathered and sooted
    px, py = 0.014, -0.009

    def ring_fields(q):
        rp_ = np.hypot(q[:, 0] - px, q[:, 1] - py)
        th_ = np.arctan2(q[:, 1] - py, q[:, 0] - px)
        rad = np.stack([rp_ * 25.0, np.zeros_like(rp_), np.zeros_like(rp_)], 1)
        growth = 2.2 * fbm3(rad, seed + 39, 3)  # good and bad years: uneven ring widths
        rd = rp_ + 0.005 * fbm3(q * 7.0, seed + 24, 3) + 0.0012 * perlin3(q * 45.0, seed + 25)
        phase = rd / 0.0047 + growth
        ring = phase % 1.0
        late = smoothstep(0.62, 0.86, ring) * smoothstep(1.0, 0.93, ring)
        chk = np.abs(perlin3(np.stack([np.cos(th_) * 38.0, np.sin(th_) * 38.0, rp_ * 5.0], 1), seed + 40))
        check = smoothstep(0.035, 0.0, chk) * smoothstep(0.06, 0.22, rp_)
        return rp_, late, check

    rp, late, check = ring_fields(pl)
    ew = lerp([0.170, 0.098, 0.052], [0.125, 0.064, 0.030], smoothstep(0.17, 0.10, rp))
    endg = lerp(ew, [0.075, 0.037, 0.017], late * 0.8)
    fib = fbm3(pl * np.array([160.0, 160.0, 160.0]), seed + 41, 2)
    endg = endg * (1.0 + 0.12 * fib)[:, None]
    grey = smoothstep(-0.2, 0.5, fbm3(pl * 6.0, seed + 26, 4))
    endg = lerp(endg, [0.100, 0.088, 0.076], grey * 0.55)
    endg = lerp(endg, [0.020, 0.012, 0.008], check * 0.85)
    # darkened, compressed wood around and under the anvil's feet
    ax0, ax1, ay0, ay1 = A_rect
    dx = np.maximum(np.maximum(ax0 - x, x - ax1), 0)
    dy = np.maximum(np.maximum(ay0 - y, y - ay1), 0)
    dist = np.hypot(dx, dy)
    near = smoothstep(0.09, 0.0, dist)
    endg = endg * lerp(1.0, 0.40, near * 0.8 + 0.3 * smoothstep(0.22, 0.04, dist))[:, None]
    saw = np.sin(2 * math.pi * (x * 0.8 + y * 0.6) / 0.011 + 3.0 * perlin3(pl * 10.0, seed + 27))
    endg = endg * (1.0 + 0.04 * saw)[:, None]
    f1s, _, cs = voronoi3(pl * 240.0, seed + 28)
    density = 0.05 + 0.35 * near
    flake = smoothstep(0.20, 0.10, f1s) * (cs < density)
    end_rough = 0.84 - 0.06 * late
    top_metal = 0.3 * flake
    endg = lerp(endg, [0.018, 0.017, 0.017], flake * 0.9)
    end_rough = lerp(end_rough, 0.55, flake)

    base = lerp(side, endg, top)
    rough = lerp(side_rough, end_rough, top)
    metal = top_metal * top
    # fissure interiors
    base = lerp(base, [0.030, 0.019, 0.012], crev * 0.7)
    rough = lerp(rough, 0.95, crev)

    masks = dict(top=top, patch=patch, near=near)

    def height(q):
        rq, plq, fq, tq = bark_fields(q)
        bark_h = 0.0080 * plq ** 0.9 + rq * (0.0020 * tq + 0.0009 * fq)             + 0.0015 * fbm3(q * np.array([10.0, 10.0, 3.0]), seed + 33, 3)
        g = perlin3(q * np.array([240.0, 240.0, 5.0]), seed + 23)
        wood_h = 0.0012 + 0.00018 * g
        pch = smoothstep(0.12, 0.26, fbm3(q * np.array([5.0, 5.0, 1.7]), seed + 22, 4))
        side_h = lerp(bark_h, wood_h, pch)
        rq = np.hypot(q[:, 0] - px, q[:, 1] - py) + 0.004 * fbm3(q * 8.0, seed + 24, 3) \
            + 0.0012 * perlin3(q * 45.0, seed + 25)
        rr = (rq / 0.0042) % 1.0
        ring_h = 0.00022 * smoothstep(0.55, 0.9, rr) * smoothstep(1.0, 0.92, rr)
        sw = np.sin(2 * math.pi * (q[:, 0] * 0.8 + q[:, 1] * 0.6) / 0.011 + 3.0 * perlin3(q * 10.0, seed + 27))
        top_h = ring_h + 0.00008 * sw + 0.00012 * fbm3(q * 90.0, seed + 30, 2)
        return lerp(side_h, top_h, masks["top"])

    return base, rough, metal, height


def iron_material(pl, n, kind, S, seed):
    """Wrought band, lap, rivets and dogs: dark, hammered, rusting."""
    rho = np.hypot(pl[:, 0], pl[:, 1])
    z = pl[:, 2]
    mott = fbm3(pl * 16.0, seed + 40, 4)
    base = lerp([0.026, 0.026, 0.027], [0.050, 0.048, 0.047], smoothstep(-0.5, 0.6, mott))
    rough = 0.55 + 0.1 * mott
    metal = np.full(len(pl), 0.45)
    rust_amt = np.clip(smoothstep(0.15, 0.55, fbm3(pl * 11.0, seed + 41, 4)) * 0.8, 0, 0.8)
    rust = lerp([0.050, 0.025, 0.012], [0.090, 0.042, 0.017], smoothstep(-0.4, 0.5, fbm3(pl * 70, seed + 42, 2)))
    base = lerp(base, rust, rust_amt)
    rough = lerp(rough, 0.85, rust_amt)
    metal = lerp(metal, 0.12, rust_amt)
    # worn edges of the band and the crowns of the rivets
    band_edge = smoothstep(S.P["band_width"] / 2 - 0.004, S.P["band_width"] / 2 - 0.0006,
                           np.abs(z - S.P["band_z"])) * (kind <= 3)
    radial = np.stack([pl[:, 0] / np.maximum(rho, 1e-6), pl[:, 1] / np.maximum(rho, 1e-6), 0 * rho], 1)
    crown = smoothstep(0.93, 0.99, (n * radial).sum(1)) * (kind == 4)
    wear = np.clip(band_edge * 0.7 + crown * 0.8, 0, 1) * (1 - rust_amt)
    base = lerp(base, [0.19, 0.19, 0.195], wear)
    rough = lerp(rough, 0.35, wear)
    metal = lerp(metal, 0.95, wear)

    def height(q):
        f1h, _, _ = voronoi3(q * 60.0, seed + 43)
        return -0.0003 * f1h ** 2 + 0.00006 * fbm3(q * 300.0, seed + 44, 2)

    return base, rough, metal, height


def srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, 12.92 * c, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def dilate(img, mask, iterations):
    img = img.copy()
    m = mask.copy()
    for _ in range(iterations):
        acc = np.zeros_like(img)
        cnt = np.zeros(m.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            mm = np.roll(np.roll(m, dy, 0), dx, 1)
            acc += np.roll(np.roll(img, dy, 0), dx, 1) * mm[..., None]
            cnt += mm
        grow = (~m) & (cnt > 0)
        img[grow] = acc[grow] / cnt[grow][:, None]
        m = m | grow
    # fill whatever is left with the mean so mips never pull in black
    if (~m).any():
        img[~m] = img[m].mean(0)
    return img


def write_png(path, rgb_bottom_up):
    """8-bit RGB PNG from a float [0,1] image whose first row is the bottom (Blender order)."""
    a = np.clip(np.round(rgb_bottom_up[::-1] * 255.0), 0, 255).astype(np.uint8)
    h, w, c = a.shape
    raw = b"".join(b"\x00" + a[r].tobytes() for r in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    color_type = {3: 2, 4: 6, 1: 0}[c]
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, color_type, 0, 0, 0)))
        fh.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        fh.write(chunk(b"IEND", b""))


def setup_cycles():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    device = "CPU"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for kind in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = kind
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == kind]
                if gpus:
                    for d in prefs.devices:
                        d.use = d.type == kind
                    device = kind
                    break
            except Exception:
                continue
    except Exception:
        pass
    scene.cycles.device = "GPU" if device != "CPU" else "CPU"
    return device


def bake_ao(obj, size, distance, samples):
    scene = bpy.context.scene
    img = bpy.data.images.new("ao_bake", size, size, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    mat = bpy.data.materials.new("ao_bake_mat")
    try:
        mat.use_nodes = True
    except Exception:
        pass
    nt = mat.node_tree
    for node in list(nt.nodes):
        nt.nodes.remove(node)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emit = nt.nodes.new("ShaderNodeEmission")
    ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
    ao.samples = 16
    ao.inputs["Distance"].default_value = distance
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(ao.outputs["AO"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    nt.nodes.active = tex
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    scene.cycles.samples = samples
    scene.render.bake.margin = 4
    scene.render.bake.use_clear = True
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type="EMIT", margin=4, use_clear=True)
    px = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(px)
    obj.data.materials.clear()
    bpy.data.materials.remove(mat)
    return px.reshape(size, size, 4)[..., 0].copy()


def final_material(name, paths):
    mat = bpy.data.materials.new(name)
    try:
        mat.use_nodes = True
    except Exception:
        pass
    nt = mat.node_tree
    for node in list(nt.nodes):
        nt.nodes.remove(node)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    base = nt.nodes.new("ShaderNodeTexImage")
    base.image = bpy.data.images.load(paths["basecolor"])
    base.image.colorspace_settings.name = "sRGB"
    nt.links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    nrm = nt.nodes.new("ShaderNodeTexImage")
    nrm.image = bpy.data.images.load(paths["normal"])
    nrm.image.colorspace_settings.name = "Non-Color"
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    orm = nt.nodes.new("ShaderNodeTexImage")
    orm.image = bpy.data.images.load(paths["orm"])
    orm.image.colorspace_settings.name = "Non-Color"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    group = bpy.data.node_groups.get("glTF Material Output")
    if group is None:
        group = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        group.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    gnode = nt.nodes.new("ShaderNodeGroup")
    gnode.node_tree = group
    nt.links.new(sep.outputs["Red"], gnode.inputs["Occlusion"])
    mat.use_backface_culling = True
    return mat


def verify_normal_map(obj, mat, size, cover, n_world, n_low):
    """Bake the object-space shading normal of the finished material (Blender decodes the tangent map
    with MikkTSpace, as glTF viewers do) and measure its angle to the normal that was meant."""
    img = bpy.data.images.new("normal_check", size, size, alpha=False, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.nodes.active = tex
    scene = bpy.context.scene
    scene.cycles.samples = 1
    bake = scene.render.bake
    bake.normal_space = "OBJECT"
    bake.normal_r, bake.normal_g, bake.normal_b = "POS_X", "POS_Y", "POS_Z"
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type="NORMAL", margin=2, use_clear=True)
    px = np.empty(size * size * 4, np.float32)
    img.pixels.foreach_get(px)
    nt.nodes.remove(tex)
    bpy.data.images.remove(img)
    got = normalize(px.reshape(size, size, 4)[..., :3][cover].astype(np.float64) * 2.0 - 1.0)
    ang = np.degrees(np.arccos(np.clip((got * n_world).sum(1), -1, 1)))
    base = np.degrees(np.arccos(np.clip((n_low * n_world).sum(1), -1, 1)))
    return {"angle_deg": [round(float(v), 2) for v in np.percentile(ang, [50, 95, 99])],
            "detail_vs_mesh_deg": [round(float(v), 2) for v in np.percentile(base, [50, 95, 99])],
            "note": "decoded map vs intended normal (median/p95/p99); detail_vs_mesh is how far the map bends the mesh normal"}


def add_socket(position_blender):
    """SOCK_interact_anvil as _blender_sockets.py authors it: primary +Y up, secondary -Z (export)."""
    empty = bpy.data.objects.new("SOCK_interact_anvil", None)
    empty.empty_display_type = "ARROWS"
    empty.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(empty)
    empty.location = position_blender
    primary = Vector((0, 1, 0))
    secondary = Vector((0, 0, -1))
    cross = primary.cross(secondary).normalized()
    secondary = cross.cross(primary).normalized()
    re = Matrix(((primary.x, secondary.x, cross.x), (primary.y, secondary.y, cross.y),
                 (primary.z, secondary.z, cross.z)))
    C = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))
    rb = C.inverted() @ re @ C
    empty.rotation_mode = "QUATERNION"
    empty.rotation_quaternion = rb.to_quaternion()
    empty["socket_role"] = "interact"
    empty["socket_family"] = ""
    empty["socket_depth"] = 0.02
    empty["socket_envelope"] = 0.0
    empty["socket_mate"] = "antiparallel"
    return empty


# --------------------------------------------------------------------------------------------
# build
# --------------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=os.path.join(REPO, "assets", "_staging", "procedural", ASSET_ID))
    ap.add_argument("--seed", type=int, default=PARAMS["seed"])
    ap.add_argument("--atlas", type=int, default=PARAMS["atlas"])
    ap.add_argument("--quick", action="store_true", help="coarse grids and a 512 atlas for iteration")
    ap.add_argument("--keep-blend", action="store_true", help="also save the build scene as .blend")
    return ap.parse_args(argv)


def main():
    args = parse_args()
    P = dict(PARAMS)
    P["seed"] = args.seed
    P["atlas"] = args.atlas
    if args.quick:
        P["anvil_grid"], P["stump_grid"], P["atlas"] = 0.004, 0.006, 512
    os.makedirs(args.out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rng = np.random.default_rng(P["seed"])
    seed = P["seed"] % 100000

    # ---------------- geometry
    anvil = Anvil(P, rng)
    stump = Stump(P, rng, P["stump_grid"])
    ax, az = P["anvil_offset_x"], stump.Hs - P["anvil_sink"]

    log("sampling anvil field")
    lo, hi = anvil.bounds()
    Fa, loa, amb_a, off_a = sample_clean(anvil.sdf, lo, hi, P["anvil_grid"])
    va, qa = surface_nets(Fa, loa, P["anvil_grid"])
    va = project(va, anvil.sdf, P["anvil_grid"])
    log(f"anvil surface nets: {len(va)} verts, {len(qa)} quads")
    log("sampling stump field")
    lo, hi = stump.bounds()
    Fs, los, amb_s, off_s = sample_clean(stump.sdf, lo, hi, P["stump_grid"])
    vs, qs = surface_nets(Fs, los, P["stump_grid"])
    vs = project(vs, stump.sdf, P["stump_grid"])
    log(f"stump surface nets: {len(vs)} verts, {len(qs)} quads; grid offsets {off_a}/{off_s}, ambiguous cells/faces left {amb_a}/{amb_s}")

    va_world = va + np.array([ax, 0.0, az])
    anvil_obj = new_object("anvil", va_world, qa)
    stump_obj = new_object("stump", vs, qs)
    high = {"anvil": solid_report(anvil_obj.data), "stump": solid_report(stump_obj.data)}
    log("high-res solids", json.dumps(high))
    decimate(anvil_obj, P["anvil_tris"])
    decimate(stump_obj, P["stump_tris"])
    finish_part(anvil_obj, P["sharp_angle_anvil"])
    finish_part(stump_obj, P["sharp_angle_stump"])

    # band, lap plate, rivets
    Rb = stump.Rb - 0.001
    t, w, zb = P["band_thickness"], P["band_width"], P["band_z"]
    band = ring_solid("band", Rb, t, zb, w, 0.0, 2 * math.pi, 96, 0.0016, True)
    lap_th = math.radians(-112.0)
    lap_half = 0.036 / (Rb + t)
    lap = ring_solid("lap", Rb + t - 0.0006, t * 0.85, zb, w, lap_th - lap_half, lap_th + lap_half, 10, 0.0014, False)
    rivets = []
    base_th = rng.uniform(0, 2 * math.pi)
    for i in range(P["rivet_count"]):
        th = base_th + 2 * math.pi * i / P["rivet_count"] + rng.uniform(-0.06, 0.06)
        if abs((th - lap_th + math.pi) % (2 * math.pi) - math.pi) < 0.25:
            th += 0.35
        r = Rb + t
        rivets.append(rivet(f"rivet{i}", (r * math.cos(th), r * math.sin(th), zb + rng.uniform(-0.002, 0.002)),
                            (math.cos(th), math.sin(th), 0.0), P["rivet_radius"], P["rivet_height"]))
    for k, off in enumerate((-0.6, 0.6)):
        th = lap_th + off * lap_half
        r = Rb + t - 0.0006 + t * 0.85
        rivets.append(rivet(f"laprivet{k}", (r * math.cos(th), r * math.sin(th), zb),
                            (math.cos(th), math.sin(th), 0.0), P["rivet_radius"] * 0.85, P["rivet_height"] * 0.85))
    # hold-down straps: over the foot ledge, down its end face, nailed flat to the end grain
    fh, top = P["foot_height"], P["anvil_sink"]
    W, st = P["base_half_width"], 0.006
    profile = [(W - 0.013, fh + 0.0003), (W + 0.0015, fh + 0.0003), (W + 0.0015, top + 0.0002),
               (W + 0.038, top + 0.0002), (W + 0.038, top + 0.0002 + st), (W + 0.0015 + st, top + 0.0002 + st),
               (W + 0.0015 + st, fh + 0.0003 + st), (W - 0.013, fh + 0.0003 + st)]
    dogs = []
    xs = P["base_half_length"] - 0.033
    for i, (dxs, side) in enumerate(((-xs, -1), (xs, -1), (-xs, 1), (xs, 1))):
        dogs.append(hold_down_dog(f"dog{i}", dxs, side, profile, 0.028, (ax, az)))
        rivets.append(rivet(f"nail{i}", (ax + dxs, side * (W + 0.026), az + top + 0.0002 + st), (0, 0, 1), 0.0065, 0.0035))
    iron_parts = [band, lap] + rivets + dogs
    for o in iron_parts:
        finish_part(o, P["sharp_angle_iron"])

    # UVs in each member's own frame, in metres (the stump's about its own axis, before recentring)
    islands = {"anvil": planar_class_uv(anvil_obj, 40), "stump": stump_uv(stump_obj, stump.R0),
               "dogs": sum(planar_class_uv(d, 6) for d in dogs)}
    log("uv islands by projection class", islands)

    # ---------------- recentre: bbox centred on X and Y(blender), lowest point at z=0
    all_objs = [anvil_obj, stump_obj] + iron_parts
    pts = np.concatenate([np.array([v.co[:] for v in o.data.vertices]) for o in all_objs])
    mn, mx = pts.min(0), pts.max(0)
    shift = np.array([-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mn[2]])
    for o in all_objs:
        o.data.transform(Matrix.Translation(Vector(shift)))
    stump_origin = shift.copy()
    anvil_origin = shift + np.array([ax, 0.0, az])
    log("recentre shift", shift.round(5).tolist())

    # ---------------- join (one mesh, one material) and pack one atlas
    bm = bmesh.new()
    parts = []
    for o, part in [(anvil_obj, 0), (stump_obj, 1), (band, 2), (lap, 3)] + [(r, 4) for r in rivets] + [(d, 5) for d in dogs]:
        start = len(bm.faces)
        bm.from_mesh(o.data)
        parts.append((part, start, len(bm.faces)))
    final_mesh = bpy.data.meshes.new(ASSET_ID)
    bm.to_mesh(final_mesh)
    bm.free()
    final = bpy.data.objects.new(ASSET_ID, final_mesh)
    bpy.context.scene.collection.objects.link(final)
    for o in all_objs:
        bpy.data.objects.remove(o)
    face_part = np.zeros(len(final_mesh.polygons), np.int32)
    for part, a, b in parts:
        face_part[a:b] = part

    scale_px, n_islands, fill = pack_atlas(final_mesh, P["atlas"], P["pad_px"])
    log(f"packed {n_islands} islands at {scale_px:.1f} px/m, atlas fill {fill:.1%}")

    report = {"high_res": high, "final": solid_report(final_mesh)}
    tris = sum(len(p.vertices) - 2 for p in final_mesh.polygons)
    log("final tris", tris, json.dumps(report["final"]))

    # texel density and degenerate UVs
    A = mesh_arrays(final_mesh)
    tl = A["tri_loops"]
    tri_part = face_part[A["tri_poly"]]
    p3 = A["co"][A["loop_vert"][tl]]
    uvt = A["uv"][tl]
    a3 = 0.5 * np.linalg.norm(np.cross(p3[:, 1] - p3[:, 0], p3[:, 2] - p3[:, 0]), axis=1)
    e1, e2 = uvt[:, 1] - uvt[:, 0], uvt[:, 2] - uvt[:, 0]
    auv = 0.5 * np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0])
    size = P["atlas"]
    density = {}
    fz = np.cross(p3[:, 1] - p3[:, 0], p3[:, 2] - p3[:, 0])
    underside = fz[:, 2] < -0.7 * np.linalg.norm(fz, axis=1)  # the stump base, deliberately small
    for part, name in enumerate(("anvil", "stump", "band", "lap", "rivets", "dogs")):
        m = (tri_part == part) & ~(underside & (tri_part == 1))
        density[name] = round(float(size * math.sqrt(auv[m].sum() / a3[m].sum())), 1)
    uv_degenerate = int((auv * size * size < 1e-6).sum())
    uv_fill = float(auv.sum())
    log("texel density px/m", density, "uv fill", round(uv_fill, 3), "uv-degenerate tris", uv_degenerate)

    # ---------------- texture evaluation
    log("rasterising atlas")
    tri_id, bary = rasterize(uvt, size)
    cover = tri_id >= 0
    ti = tri_id[cover]
    w = bary[cover].astype(np.float64)
    loops = tl[ti]
    pos = (A["co"][A["loop_vert"][loops]] * w[:, :, None]).sum(1)
    n_low = normalize((A["cn"][loops] * w[:, :, None]).sum(1))
    t_low = (A["tg"][loops] * w[:, :, None]).sum(1)
    t_low = normalize(t_low - n_low * (t_low * n_low).sum(1, keepdims=True))
    b_low = np.cross(n_low, t_low) * A["bs"][loops[:, 0]][:, None]
    part = tri_part[ti]
    log(f"{len(pos)} texels covered ({len(pos) / size / size:.1%})")

    basec = np.zeros((len(pos), 3))
    rough = np.zeros(len(pos))
    metal = np.zeros(len(pos))
    nmap = np.zeros((len(pos), 3))
    eps = 0.0004

    def shade(sel, mat_fn, n_hi, to_local):
        pl = to_local(pos[sel])
        b, r, m, hfn = mat_fn(pl, n_hi)
        basec[sel], rough[sel], metal[sel] = b, r, m
        tl_, bl_ = t_low[sel], b_low[sel]
        # re-orthogonalise the tangent frame on the detailed normal, then add the height slope
        t2 = normalize(tl_ - n_hi * (tl_ * n_hi).sum(1, keepdims=True))
        b2 = normalize(bl_ - n_hi * (bl_ * n_hi).sum(1, keepdims=True) - t2 * (bl_ * t2).sum(1, keepdims=True))
        h0 = hfn(pl)
        dt = (hfn(pl + eps * t2) - h0) / eps
        db = (hfn(pl + eps * b2) - h0) / eps
        n2 = normalize(n_hi - dt[:, None] * t2 - db[:, None] * b2)
        nmap[sel] = n2

    # anvil: the field is the "high poly"
    sel = part == 0
    fa = GridField(Fa, loa, P["anvil_grid"])
    pl_a = pos[sel] - anvil_origin
    exact_a = lambda p: anvil.sdf(p[:, 0], p[:, 1], p[:, 2])
    n_hi = field_normal(exact_a, pl_a, 0.0003)
    n_hi = np.where(((n_hi * n_low[sel]).sum(1) > 0.2)[:, None], n_hi, n_low[sel])
    conv_a = convexity(exact_a, pl_a, 0.0025)
    log("anvil convexity p5/p50/p95", np.percentile(conv_a, [5, 50, 95]).round(6).tolist())
    shade(sel, lambda pl, n: anvil_material(pl, n, conv_a, anvil, seed), n_hi, lambda p: p - anvil_origin)
    log("anvil shaded")

    sel = part == 1
    pl_s = pos[sel] - stump_origin
    exact_s = lambda p: stump.sdf(p[:, 0], p[:, 1], p[:, 2])
    fs = GridField(Fs, los, P["stump_grid"])
    n_hi = field_normal(exact_s, pl_s, 0.0004)
    n_hi = np.where(((n_hi * n_low[sel]).sum(1) > 0.3)[:, None], n_hi, n_low[sel])
    conv_s = convexity(fs, pl_s, 0.005)
    log("stump convexity p5/p50/p95", np.percentile(conv_s, [5, 50, 95]).round(6).tolist())
    L, W = P["base_half_length"], P["base_half_width"]
    rect = (ax - L, ax + L, -W, W)
    shade(sel, lambda pl, n: stump_material(pl, n, conv_s, stump, rect, seed), n_hi, lambda p: p - stump_origin)
    log("stump shaded")

    for kind in (2, 3, 4, 5):
        sel = part == kind
        if sel.any():
            shade(sel, lambda pl, n, k=kind: iron_material(pl, n, np.full(len(pl), k), stump, seed),
                  n_low[sel], lambda p: p - stump_origin)
    log("iron shaded")

    # tangent-space encode (OpenGL: +Y is the bitangent / +V direction)
    ts = np.stack([(nmap * t_low).sum(1), (nmap * b_low).sum(1), (nmap * n_low).sum(1)], 1)
    ts = normalize(ts)

    # ---------------- occlusion bake (the whole prop, so the anvil shades the stump top)
    device = setup_cycles()
    log("baking AO on", device)
    ao_img = bake_ao(final, size, 0.25, 8 if args.quick else 24)
    ao = ao_img[cover]
    cav = np.ones(len(pos))
    cav[part == 0] = 1.0 - 0.5 * smoothstep(-0.0001, -0.0010, conv_a)
    cav[part == 1] = 1.0 - 0.45 * smoothstep(-0.0003, -0.0030, conv_s)
    occl = np.clip(ao * cav, 0.0, 1.0)
    basec = basec * lerp(1.0, occl, 0.35)[:, None]

    # ---------------- images
    def image(values, channels):
        img = np.zeros((size, size, channels), np.float32)
        img[cover] = values.reshape(len(pos), channels)
        return dilate(img, cover, P["pad_px"] + 4)

    paths = {k: os.path.join(args.out_dir, f"{ASSET_ID}_{k}.png") for k in ("basecolor", "normal", "orm")}
    write_png(paths["basecolor"], image(srgb(basec), 3))
    write_png(paths["normal"], image(ts * 0.5 + 0.5, 3))
    orm = np.stack([occl, np.clip(rough, 0.04, 1.0), np.clip(metal, 0.0, 1.0)], 1)
    write_png(paths["orm"], image(orm, 3))
    log("textures written")

    # ---------------- final material, socket, export
    final_mesh.materials.clear()
    mat = final_material(f"MAT_{ASSET_ID}", paths)
    final_mesh.materials.append(mat)
    for p in final_mesh.polygons:
        p.material_index = 0
    normal_check = verify_normal_map(final, mat, size, cover, nmap, n_low)
    log("normal map decode check (deg): median %.2f p95 %.2f p99 %.2f" % tuple(normal_check["angle_deg"]))
    sock_pos = Vector((anvil_origin[0], anvil_origin[1], anvil_origin[2] + anvil.H))
    add_socket(sock_pos)
    glb = os.path.join(args.out_dir, f"{ASSET_ID}.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_yup=True, export_apply=True,
                              export_materials="EXPORT", export_image_format="AUTO", export_texcoords=True,
                              export_normals=True, export_tangents=True, export_extras=True, use_selection=False)
    if args.keep_blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.out_dir, f"{ASSET_ID}_build.blend"))
    log("exported", glb)

    # ---------------- provenance
    pts = np.array([v.co[:] for v in final_mesh.vertices])
    mn, mx = pts.min(0), pts.max(0)
    dims_blender = mx - mn
    dims = {"x": round(float(dims_blender[0]), 4), "y_up": round(float(dims_blender[2]), 4),
            "z_front": round(float(dims_blender[1]), 4)}
    anvil_mass = high["anvil"]["volume_m3"] * 7850.0
    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_anvil.py",
        "command": f'blender --background --factory-startup --python tools/asset_pipeline/_procgen_anvil.py -- --seed {P["seed"]} --atlas {P["atlas"]}',
        "blender": bpy.app.version_string,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "seed": P["seed"],
        "parameters": P,
        "concept": "assets/concepts/prop_blacksmith_anvil_stump.png",
        "concept_sha256": hashlib.sha256(open(CONCEPT, "rb").read()).hexdigest() if os.path.exists(CONCEPT) else None,
        "replaces": "assets/ready/prop_blacksmith_anvil_stump (Pixal3D reconstruction; face and base ~10 deg apart)",
        "frame": "glTF +Y up, front +Z (the smith's side; horn to -X, heel and hardy hole to +X), centred on X/Z, lowest point y=0",
        "dimensions_m": dims,
        "face_height_m": round(float(anvil_origin[2] + anvil.H), 4),
        "stump_diameter_m": round(2 * stump.R0, 3),
        "anvil_length_m": round(P["x_heel"] - anvil.x_tip + P["horn_tip_radius"], 3),
        "anvil_mass_kg_estimate": round(anvil_mass, 1),
        "triangles": tris,
        "triangles_by_part": {n: int((tri_part == i).sum()) for i, n in enumerate(("anvil", "stump", "band", "lap", "rivets", "dogs"))},
        "solids": report,
        "uv": {"texel_density_px_per_m": density, "uv_fill": round(uv_fill, 4), "uv_degenerate_tris": uv_degenerate,
               "note": "One atlas at one density (the stump's never-seen base is packed at 0.15x). About 2x the "
                       "512 px/m guide: at 512 px/m this hero prop would leave ~75% of the 2048 atlas empty, and "
                       "the world tileables it stands among run 853-2048 px/m."},
        "materials": {
            "name": f"MAT_{ASSET_ID}",
            "atlas": P["atlas"],
            "maps": {k: os.path.basename(v) for k, v in paths.items()},
            "encoding": "base colour sRGB; normal tangent-space OpenGL (+Y), MikkTSpace of the exported mesh; ORM = occlusion, roughness, metallic",
            "source": "procedural shaders evaluated per texel in numpy (object space), normals from the SDF gradient "
                      "plus procedural height, occlusion from a Cycles bake of the whole prop. The staged world "
                      "materials were not used: material_cast_iron_surface is a 1 m tileable without edge wear, "
                      "polish or rust placement, and there is no bark or end-grain material.",
        },
        "sockets": {"SOCK_interact_anvil": {"position_export": [round(float(sock_pos.x), 4), round(float(sock_pos.z), 4),
                                                                round(float(-sock_pos.y), 4)],
                                            "primary": [0, 1, 0], "secondary": [0, 0, -1], "note": "Striking face centre, above the waist."}},
        "normal_map_check": normal_check,
        "bake_device": device,
        "outputs": [os.path.basename(glb)] + [os.path.basename(v) for v in paths.values()],
    }
    with open(os.path.join(args.out_dir, f"{ASSET_ID}_provenance.json"), "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    log("PROCGEN_RESULT", json.dumps({"glb": glb, "triangles": tris, "dimensions_m": dims}))


if __name__ == "__main__":
    main()
