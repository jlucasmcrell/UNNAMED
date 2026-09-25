"""Procedural narrow-gauge quarry rail track: prop_quarry_rail_track (3.0 m straight) and _end (buffer stop).

Image-to-3D could not make this prop (Pixal3D rebuilt the concept's receding run as a slab pitched 40 deg),
so it is assembled here from real member sizes after assets/concepts/prop_quarry_rail_track.png:

  * two flat-bottomed iron rails, 72 mm tall (64 mm foot, 34 mm head, 8 mm web), 600 mm gauge between the
    inner faces of the heads, on 6.5 mm iron base plates, each held by two dog spikes;
  * five rough timber sleepers, about 1.00 x 0.15 x 0.10 m, at 600 mm centres, half sunk in the ballast,
    each hewn a little differently (size, yaw, drooping ends, weathering, splits, rust bleed at the spikes);
  * a low gravel ballast strip (crest 78 mm) with a ragged spill edge and loose broken stone on it;
  * half fishplates with bolts at both rail ends, so two modules butted end to end read as one fished joint.

The straight module is exactly 3.0 m along Z and repeats: its ends are the same cross-section (ballast
outline and height are periodic in Z), sleepers sit 0.3 m in from each end, and the ballast texture repeats
twice per module. The end-stop variant has the same joint at its front (+Z) end, one rail end bent slightly
upward (it overran), and a timber buffer beam on two posts with raking struts, dug into a gravel heap,
facing +Z along the track.

Every member is a closed chamfered prism in its own frame; side UVs unroll the cross-section (U along the
member, V round it), so grain runs along each member at one texel density. Textures are evaluated in numpy
from seeded noise in each island's own metres: one 1024 atlas (wood, iron, stone) per module, with the rail
sides in a horizontally periodic band so both rails fit at full density. The ballast has its own 1024
crushed-stone texture (periodic Voronoi layers, supersampled 2x), tiled at 1.5 m so it meets itself at every
module joint; the staged material_loose_gravel was tried first and dropped (warm rounded river gravel).

Output (glTF): metres, +Y up, the track runs along Z (the end stop's buffer faces +Z), centred on X/Z, lowest
point at y = 0. One mesh per module, two materials (atlas + ballast).

Usage:
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_railtrack.py -- ^
      [--variant both|straight|end] [--out <dir>] [--seed 2609] [--tex 1024] [--density 512] [--preview]
Writes <out>/prop_quarry_rail_track.glb, <out>/prop_quarry_rail_track_end.glb and a _provenance.json for
each (default <out>: assets/_staging/procedural/prop_quarry_rail_track). --preview also renders a run of
modules from the concept's viewpoint and a close-up into <out>/renders/. The last stdout line is
"PROCGEN_RESULT <provenance path>[, <provenance path>]".
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import struct
import sys
import tempfile
import time
import zlib

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(REPO, "assets")
ASSET_ID = "prop_quarry_rail_track"
END_ID = ASSET_ID + "_end"
OUT_DEFAULT = os.path.join(ASSETS, "_staging", "procedural", ASSET_ID)
CONCEPT = os.path.join(ASSETS, "concepts", ASSET_ID + ".png")
SCRIPT_REL = "tools/asset_pipeline/_procgen_railtrack.py"

# All sizes in metres. Build frame is Blender's: Z up, the track along Y; the glTF exporter turns that into
# +Y up with game +Z = Blender -Y, so the end stop's buffer (at Blender +Y, facing -Y) faces game +Z.
PARAMS = {
    "module_length": 3.0,
    "gauge": 0.600,                   # between the inner faces of the rail heads
    "rail_centre_x": 0.317,           # gauge / 2 + head / 2
    "rail": {"height": 0.072, "foot": 0.064, "head": 0.034, "web": 0.008,
             "class": "flat-bottomed, about 15 kg/m (30 lb/yd class light rail)"},
    "sleeper": {"length": 1.00, "width": 0.15, "depth": 0.10, "pitch": 0.60, "top_z": 0.130,
                "positions_straight": [-1.2, -0.6, 0.0, 0.6, 1.2], "positions_end": [-1.2, -0.6, 0.0, 0.6]},
    "base_plate": {"across": 0.130, "along": 0.100, "thick": 0.0065},
    "dog_spike": {"shank": 0.0135, "head_lip": 0.011},
    "fishplate": {"half_length": 0.20, "thick": 0.011, "bolts_from_joint": [0.055, 0.145]},
    "ballast": {"crest_z": 0.078, "crest_half": 0.50, "max_half": 0.70, "lump": 0.011, "tile_m": 1.5, "ss": 2},
    "rocks": {"count_straight": 960, "count_end": 860, "r_min": 0.017, "r_max": 0.066},
    "end_stop": {"rail_end_y": 0.92, "bent_rail": "left (game -X)", "bend_rise": 0.032, "bend_length": 0.26,
                 "beam": [1.00, 0.16, 0.16], "beam_bottom_z": 0.24, "beam_front_y": 0.94,
                 "posts": [0.14, 0.14, 0.47], "post_x": 0.32, "strut_section": 0.09, "heap_peak_z": 0.21},
}
PC = PARAMS
SEAT_Z = PC["sleeper"]["top_z"] + PC["base_plate"]["thick"]          # rail base
HALF = PC["module_length"] / 2.0


def log(msg):
    print(f"[railtrack] {msg}", flush=True)


# ============================================================================================ noise
_RT = np.random.default_rng(0x5EED5).random(1 << 16)


def _hash(ix, iy, seed):
    with np.errstate(over="ignore"):
        h = (ix * np.int64(73856093)) ^ (iy * np.int64(19349663)) ^ np.int64(seed * 83492791 + 1013904223)
        h = h ^ (h >> np.int64(15))
        h = h * np.int64(2246822519)
        h = h ^ (h >> np.int64(13))
    return _RT[h & 0xFFFF]


def vnoise(x, y, seed, px=None, py=None):
    """Quintic gradient (Perlin) noise on an integer lattice, about [-0.7, 0.7]; periodic in x with px
    cells and in y with py cells when given. Gradient rather than value noise: value noise's contours
    come out blocky and axis-aligned once thresholded."""
    x = np.asarray(x, np.float64)
    y = np.asarray(y, np.float64)
    xf, yf = np.floor(x), np.floor(y)
    tx, ty = x - xf, y - yf
    ix, iy = xf.astype(np.int64), yf.astype(np.int64)
    ix1, iy1 = ix + 1, iy + 1
    if px:
        ix, ix1 = np.mod(ix, px), np.mod(ix1, px)
    if py:
        iy, iy1 = np.mod(iy, py), np.mod(iy1, py)

    def grad(gx, gy, dx, dy):
        a = _hash(gx, gy, seed) * (2.0 * math.pi)
        return np.cos(a) * (tx - dx) + np.sin(a) * (ty - dy)

    sx = tx * tx * tx * (tx * (tx * 6 - 15) + 10)
    sy = ty * ty * ty * (ty * (ty * 6 - 15) + 10)
    a, b = grad(ix, iy, 0, 0), grad(ix1, iy, 1, 0)
    c, d = grad(ix, iy1, 0, 1), grad(ix1, iy1, 1, 1)
    ab = a + (b - a) * sx
    cd = c + (d - c) * sx
    return ab + (cd - ab) * sy


def fbm(x, y, seed, octaves=4, gain=0.5, px=None, py=None):
    """fBm of gradient noise, centred on 0.5."""
    tot, amp, norm, f = 0.0, 1.0, 0.0, 1
    for o in range(octaves):
        tot = tot + amp * vnoise(np.asarray(x) * f, np.asarray(y) * f, seed + 131 * o,
                                 px * f if px else None, py * f if py else None)
        norm += amp
        amp *= gain
        f *= 2
    return 0.5 + tot / norm


_STD = {}


def nz(x, y, seed, octaves=4, px=None, py=None):
    """fbm rescaled to about zero mean and unit standard deviation."""
    if octaves not in _STD:
        g = np.random.default_rng(7).random((2, 40000)) * 400.0
        _STD[octaves] = float(np.std(fbm(g[0], g[1], 99, octaves)))
    return (fbm(x, y, seed, octaves, px=px, py=py) - 0.5) / _STD[octaves]


def sstep(e0, e1, x):
    t = np.clip((np.asarray(x, np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def periodic_1d(y, seed, terms=6, period=3.0, k0=1):
    """Smooth random function of y with period `period` (sum of integer harmonics k0..terms)."""
    rng = np.random.default_rng(seed)
    out = np.zeros_like(np.asarray(y, np.float64))
    for k in range(k0, terms + 1):
        a = rng.normal() / k ** 1.1
        ph = rng.uniform(0, 2 * math.pi)
        out = out + a * np.sin(2 * math.pi * k * np.asarray(y) / period + ph)
    return out / 1.2


# ============================================================================================ geometry
class Island:
    def __init__(self, key, kind, w, h, params):
        self.key, self.kind, self.w, self.h, self.params = key, kind, w, h, params
        self.band = params.get("band", False)
        self.x0 = self.y0 = 0
        self.wpx = self.hpx = 0


class Build:
    """Geometry of one module: parts (one closed solid or a set of them) plus the atlas islands."""

    def __init__(self):
        self.parts = []
        self.islands = {}

    def island(self, key, kind, w, h, **params):
        if key in self.islands:
            isl = self.islands[key]
            isl.w, isl.h = max(isl.w, w), max(isl.h, h)
        else:
            self.islands[key] = Island(key, kind, w, h, params)
        return key

    def part(self, name, material, smooth_deg):
        p = Part(name, material, smooth_deg)
        self.parts.append(p)
        return p


class Part:
    def __init__(self, name, material, smooth_deg):
        self.name, self.material, self.smooth = name, material, smooth_deg
        self.v, self.f, self.uv, self.isl = [], [], [], []
        self.solids = 0

    def add(self, p):
        self.v.append((float(p[0]), float(p[1]), float(p[2])))
        return len(self.v) - 1

    def face(self, idx, uvs, island):
        self.f.append(tuple(idx))
        self.uv.append([(float(a), float(b)) for a, b in uvs])
        self.isl.append(island)


def unit(v):
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def frame_from(aw, up):
    """(au, av) with au x av = aw, av as close to `up` as possible."""
    aw = unit(aw)
    au = unit(np.cross(up, aw))
    av = np.cross(aw, au)
    return au, av


def outward_normals(prof):
    n = len(prof)
    out = []
    for i in range(n):
        p0, p1, p2 = np.array(prof[i - 1]), np.array(prof[i]), np.array(prof[(i + 1) % n])
        e0, e1 = p1 - p0, p2 - p1
        n0 = np.array([e0[1], -e0[0]]) / np.linalg.norm(e0)
        n1 = np.array([e1[1], -e1[0]]) / np.linalg.norm(e1)
        m = n0 + n1
        m = m / np.linalg.norm(m)
        out.append((m, max(float(np.dot(m, n0)), 0.3)))
    return out


def inset_polygon(prof, d):
    if d <= 0:
        return list(prof)
    return [tuple(np.array(p) - m * d / c) for p, (m, c) in zip(prof, outward_normals(prof))]


def polygon_area(prof):
    a = 0.0
    for i in range(len(prof)):
        x0, y0 = prof[i - 1]
        x1, y1 = prof[i]
        a += x0 * y1 - x1 * y0
    return a / 2.0


def face_tess(part, idx, uvs, island, positions):
    """Emit an n-gon as simple triangles (tessellate_polygon handles a concave/near-collinear outline
    correctly); Blender's own n-gon-to-triangle split at export time picked a bad diagonal for a few
    end-cap outlines here and left a sliver triangle with an all-but-zero-area UV footprint."""
    pts = [Vector(positions[i]) for i in idx]
    for tri in tessellate_polygon([pts]):
        part.face([idx[i] for i in tri], [uvs[i] for i in tri], island)


def prism(part, prof, length, origin, au, av, side_isl, cap0_isl, cap1_isl, inset0=0.0, inset1=0.0,
          stations=(), deform=None, vscale=None, u_offset=0.0):
    """Closed prism: CCW profile (in au/av) swept along aw = au x av from `origin` for `length`.

    Ends are chamfered by inset0/inset1 (an inset ring at the very end). Side UV: u = (length - w) +
    u_offset along the member, v = arclength round the profile (edge i scaled by vscale[i]); caps: profile
    coordinates. Both are laid out so the UV frame is not mirrored against the outward normal. Returns
    (side island dims, cap dims).
    """
    assert polygon_area(prof) > 0, "profile must be counter-clockwise"
    au, av = unit(au), unit(av)
    aw = np.cross(au, av)
    origin = np.asarray(origin, np.float64)
    n = len(prof)
    s = [0.0]
    for i in range(n):
        seg = math.dist(prof[i], prof[(i + 1) % n]) * (vscale[i] if vscale else 1.0)
        s.append(s[-1] + seg)
    rings = []
    if inset0 > 0:
        rings.append((0.0, inset_polygon(prof, inset0)))
    rings.append((inset0, prof))
    for w in stations:
        if inset0 + 1e-6 < w < length - inset1 - 1e-6:
            rings.append((w, prof))
    rings.append((length - inset1, prof))
    if inset1 > 0:
        rings.append((length, inset_polygon(prof, inset1)))
    idx = []
    for ri, (w, pr) in enumerate(rings):
        row = []
        for i, (px, py) in enumerate(pr):
            pos = origin + px * au + py * av + w * aw
            if deform is not None:
                pos = deform(pos, i, prof[i][0], prof[i][1], w, ri, len(rings))
            row.append(part.add(pos))
        idx.append(row)
    for k in range(len(rings) - 1):
        w0, w1 = rings[k][0], rings[k + 1][0]
        for i in range(n):
            j = (i + 1) % n
            a, b, c, d = idx[k][i], idx[k][j], idx[k + 1][j], idx[k + 1][i]
            u0, u1 = length - w0 + u_offset, length - w1 + u_offset
            # a side quad can go needle-thin in v at a pinned "seat" deform zone; tessellate it instead
            # of handing Blender a possibly near-degenerate quad to split on its own diagonal
            face_tess(part, [a, b, c, d], [(u0, s[i]), (u0, s[i + 1]), (u1, s[i + 1]), (u1, s[i])], side_isl,
                      part.v)
    xs = [p[0] for p in prof]
    ys = [p[1] for p in prof]
    minx, maxx, miny = min(xs), max(xs), min(ys)
    first, last = rings[0][1], rings[-1][1]
    cap0_idx = list(reversed(idx[0]))
    face_tess(part, cap0_idx, [(maxx - p[0], p[1] - miny) for p in reversed(first)], cap0_isl, part.v)
    cap1_idx = list(idx[-1])
    face_tess(part, cap1_idx, [(p[0] - minx, p[1] - miny) for p in last], cap1_isl, part.v)
    part.solids += 1
    return (length + abs(u_offset), s[-1]), (maxx - minx, max(ys) - miny)


def rounded_rect(w, h, r, seg=2, x0=None, y0=0.0):
    """CCW rounded rectangle, bottom-centre start; x centred unless x0 given."""
    cx = 0.0 if x0 is None else x0 + w / 2
    pts = [(cx, y0)]
    corners = [((cx + w / 2 - r, y0 + r), -90), ((cx + w / 2 - r, y0 + h - r), 0),
               ((cx - w / 2 + r, y0 + h - r), 90), ((cx - w / 2 + r, y0 + r), 180)]
    for (ccx, ccy), a0 in corners:
        for k in range(seg + 1):
            a = math.radians(a0 + 90.0 * k / seg)
            pts.append((ccx + r * math.cos(a), ccy + r * math.sin(a)))
    return pts


# ---------------------------------------------------------------------------------- ballast
_SCALE = {}


def _hw_raw(y, side):
    """Spill edge: a few long lobes plus a ragged fringe at 10-35 cm."""
    seed = 4100 + (0 if side < 0 else 1)
    return periodic_1d(y, seed, terms=6) + 1.3 * periodic_1d(y, seed + 7, terms=18, k0=8)


def _hw_scale(side):
    """Spill amplitude per side, set so both sides reach just past max_half where both variants agree
    (y <= 0.9); halfwidth() clips there, so the model's X bounds are exactly +-max_half (centred on X)."""
    if side not in _SCALE:
        ys = np.linspace(-HALF, 0.9, 2401)
        base = PC["ballast"]["max_half"] - 0.075
        _SCALE[side] = (PC["ballast"]["max_half"] + 0.006 - base) / (0.045 * float(_hw_raw(ys, side).max()))
    return _SCALE[side]


def end_taper(y, variant):
    if variant != "end":
        return np.ones_like(np.asarray(y, np.float64))
    return 1.0 - 0.34 * sstep(0.95, 1.5, y)


def halfwidth(y, side, variant):
    base = PC["ballast"]["max_half"] - 0.075
    hw = np.minimum(base + 0.045 * _hw_scale(side) * _hw_raw(y, side), PC["ballast"]["max_half"])
    return hw * end_taper(y, variant)


def ballast_height(x, y, variant):
    """Ballast surface height (m) at (x, y); zero at and beyond the spill edge. Periodic in y with the
    module length (the lump noise is periodic on its first axis), so both module ends match."""
    x = np.asarray(x, np.float64)
    y = np.asarray(y, np.float64)
    B = PC["ballast"]
    hw = np.where(x < 0, halfwidth(y, -1, variant), halfwidth(y, 1, variant))
    d = np.abs(x)
    a = B["crest_half"] + 0.03 * np.where(x < 0, periodic_1d(y, 4200, terms=5), periodic_1d(y, 4201, terms=5))
    a = a * end_taper(y, variant)
    t = np.clip((d - a) / np.maximum(hw - a, 1e-3), 0.0, 1.0)
    shoulder = np.where(d < a, 1.0, 0.5 * (1.0 + np.cos(np.pi * t)))
    cells = 21
    lump = nz(y * (cells / PC["module_length"]), x * 7.0 + 31.0, 4301, 3, px=cells)
    h = shoulder * (B["crest_z"] + B["lump"] * lump * (0.6 + 0.4 * t))
    cells2 = 54
    edge_rag = nz(y * (cells2 / PC["module_length"]), x * 18.0 + 90.0, 4302, 2, px=cells2)
    h = h + 0.006 * edge_rag * np.sin(np.pi * t) * (d >= a)
    if variant == "end":
        E = PC["end_stop"]
        heap = E["heap_peak_z"] - B["crest_z"] * 0.4
        gy = np.exp(-((y - 1.20) ** 2) / (2 * 0.16 ** 2))
        gx = np.where(d < 0.62, 0.5 * (1 + np.cos(np.pi * d / 0.62)), 0.0)
        h = h + heap * gy * gx * (1.0 + 0.06 * nz(x * 4.0 + 7.0, y * 4.0, 4303, 2))
        h = h * sstep(1.5, 1.27, y)
    h = np.where(d >= hw, 0.0, np.maximum(h, 0.0))
    return h


def build_ballast(b, variant):
    part = b.part("ballast", "ballast", 80.0)
    ny, nx = 60, 34
    ys = np.linspace(-HALF, HALF, ny + 1)
    ts = np.linspace(-1.0, 1.0, nx + 1)
    ts = np.sign(ts) * np.abs(ts) ** 0.85
    T = PC["ballast"]["tile_m"]
    grid = np.zeros((ny + 1, nx + 1), np.int64)
    pos = np.zeros((ny + 1, nx + 1, 3))
    for i, y in enumerate(ys):
        hl, hr = float(halfwidth(y, -1, variant)), float(halfwidth(y, 1, variant))
        xs = np.where(ts < 0, ts * hl, ts * hr)
        zs = ballast_height(xs, np.full_like(xs, y), variant)
        zs[0] = zs[-1] = 0.0
        if variant == "end" and i == ny:
            zs[:] = 0.0
        for j in range(nx + 1):
            pos[i, j] = (xs[j], y, zs[j])
            grid[i, j] = part.add(pos[i, j])

    def tuv(p):
        return (p[0] / T, p[1] / T)

    for i in range(ny):
        for j in range(nx):
            q = (grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j])
            part.face(q, [tuv(pos[i, j]), tuv(pos[i, j + 1]), tuv(pos[i + 1, j + 1]), tuv(pos[i + 1, j])], None)
    cap0 = True
    cap1 = not (variant == "end")
    if cap0:
        row = [grid[0, j] for j in range(nx, -1, -1)]
        face_tess(part, row, [(pos[0, j][0] / T, pos[0, j][2] / T) for j in range(nx, -1, -1)], None, part.v)
    if cap1:
        row = [grid[ny, j] for j in range(nx + 1)]
        face_tess(part, row, [(-pos[ny, j][0] / T, pos[ny, j][2] / T) for j in range(nx + 1)], None, part.v)
    loop = [grid[0, nx]]
    loop += [grid[0, 0]] if cap0 else [grid[0, j] for j in range(nx - 1, -1, -1)]
    loop += [grid[i, 0] for i in range(1, ny + 1)]
    loop += [grid[ny, nx]] if cap1 else [grid[ny, j] for j in range(1, nx + 1)]
    loop += [grid[i, nx] for i in range(ny - 1, 0, -1)]
    vv = part.v
    loop_uv = [(-vv[k][0] / T, vv[k][1] / T) for k in loop]
    # this perimeter (flat, z = 0) is a long, ragged, concave outline (the spill edge's noise folds it
    # locally); Blender's own n-gon fill can leave a gap in a concave polygon like this, so it is
    # tessellated here into simple triangles instead of handed over as one n-gon
    tris = tessellate_polygon([[Vector(vv[k]) for k in loop]])
    for tri in tris:
        part.face([loop[i] for i in tri], [loop_uv[i] for i in tri], None)
    part.solids += 1


# ---------------------------------------------------------------------------------- rails
def rail_profile():
    """Flat-bottomed rail section (x across, y up from the base), counter-clockwise from the base centre."""
    R = PC["rail"]
    H, fw, hw, ww = R["height"], R["foot"] / 2, R["head"] / 2, R["web"] / 2
    right = [(fw - 0.0015, 0.0), (fw, 0.0015), (fw, 0.0055), (fw - 0.001, 0.0068),
             (ww + 0.0045, 0.0115), (ww + 0.001, 0.0135), (ww, 0.017), (ww, H * 0.60),
             (ww + 0.0015, H * 0.66), (hw - 0.001, H * 0.70), (hw, H * 0.72), (hw, H - 0.0065),
             (hw - 0.001, H - 0.0025), (hw - 0.004, H - 0.0005), (hw - 0.0095, H)]
    left = [(-x, y) for x, y in reversed(right)]
    return [(0.0, 0.0)] + right + left


def rail_top_band(prof):
    """Arclength range of the running surface (the rounded top of the head)."""
    s = [0.0]
    for i in range(len(prof)):
        s.append(s[-1] + math.dist(prof[i], prof[(i + 1) % len(prof)]))
    top = [i for i, p in enumerate(prof) if p[1] >= PC["rail"]["height"] - 0.0006]
    return s[min(top)] - 0.002, s[max(top)] + 0.002, s


def build_rails(b, variant, rng):
    part = b.part("rails", "atlas", 32.0)
    prof = rail_profile()
    x0 = PC["rail_centre_x"]
    t0, t1, s = rail_top_band(prof)
    b.island("rail_band", "rail", 0.0, s[-1], band=True, seed=int(rng.integers(1e6)), top=(t0, t1))
    E = PC["end_stop"]
    y_lo = -HALF
    y_hi = HALF if variant == "straight" else E["rail_end_y"]
    L = y_hi - y_lo
    offsets = {-1: 0.0, 1: 0.83}
    for side in (-1, 1):
        cap_hi = b.island(f"railcap_{side}_hi", "rust", 0.06, 0.07, seed=int(rng.integers(1e6)), cut=True)
        cap_lo = b.island(f"railcap_{side}_lo", "rust", 0.06, 0.07, seed=int(rng.integers(1e6)), cut=True)
        deform, stations = None, ()
        if variant == "end" and side == -1:
            y_bend0 = E["rail_end_y"] - E["bend_length"]

            def deform(pos, i, px, py, w, ri, nr, y_bend0=y_bend0):
                t = min(max((pos[1] - y_bend0) / E["bend_length"], 0.0), 1.0)
                return pos + np.array([0.0, 0.0, E["bend_rise"] * t * t])
            # w runs from y_hi towards y_lo
            stations = [y_hi - yy for yy in np.linspace(y_bend0, y_hi, 10)[:-1]]
        prism(part, prof, L, (side * x0, y_hi, SEAT_Z), (1, 0, 0), (0, 0, 1), "rail_band", cap_hi, cap_lo,
              inset0=0.0015, inset1=0.0015, stations=stations, deform=deform, u_offset=offsets[side])


# ---------------------------------------------------------------------------------- sleepers
def sleeper_profile(W, T, r_top, zb):
    rb = 0.013
    pts = [(0.0, 0.0), (W / 2 - rb, 0.0), (W / 2, rb), (W / 2, zb)]
    c = (W / 2 - r_top, T - r_top)
    for a in (0, 45, 90):
        pts.append((c[0] + r_top * math.cos(math.radians(a)), c[1] + r_top * math.sin(math.radians(a))))
    c = (-W / 2 + r_top, T - r_top)
    for a in (90, 135, 180):
        pts.append((c[0] + r_top * math.cos(math.radians(a)), c[1] + r_top * math.sin(math.radians(a))))
    pts += [(-W / 2, zb), (-W / 2, rb), (-W / 2 + rb, 0.0)]
    return pts


TONES = {
    # sRGB 0..1: light, dark, weather-grey mix
    "grey": ((0.52, 0.49, 0.44), (0.30, 0.27, 0.23), 0.50),
    "bleached": ((0.60, 0.57, 0.50), (0.37, 0.34, 0.29), 0.60),
    "brown": ((0.42, 0.33, 0.25), (0.22, 0.17, 0.12), 0.25),
    "dark": ((0.33, 0.27, 0.21), (0.16, 0.13, 0.10), 0.15),
    "rusty": ((0.47, 0.38, 0.30), (0.27, 0.20, 0.15), 0.30),
}


def build_sleepers(b, variant, rng, spikes_out):
    part = b.part("sleepers", "atlas", 40.0)
    S = PC["sleeper"]
    positions = S["positions_straight"] if variant == "straight" else S["positions_end"]
    tones = ["grey", "rusty", "bleached", "dark", "brown", "grey", "bleached"]
    order = rng.permutation(len(tones))
    xr = PC["rail_centre_x"]
    for k, y0 in enumerate(positions):
        seed = int(rng.integers(1e6))
        L = S["length"] + rng.uniform(-0.04, 0.04)
        W = S["width"] + rng.uniform(-0.011, 0.011)
        T = S["depth"] + rng.uniform(-0.009, 0.006)
        cx = rng.uniform(-0.02, 0.02)
        cy = y0 + rng.uniform(-0.018, 0.018)
        yaw = math.radians(rng.uniform(-1.6, 1.6))
        r_top = rng.uniform(0.012, 0.02)
        zb = T - 0.056
        prof = sleeper_profile(W, T, r_top, zb)
        n = len(prof)
        vscale = []
        for i in range(n):
            ya, yb_ = prof[i][1], prof[(i + 1) % n][1]
            vscale.append(1.0 if max(ya, yb_) > zb + 1e-6 else 0.25)
        aw = np.array([math.cos(yaw), math.sin(yaw), 0.0])
        au = np.array([-math.sin(yaw), math.cos(yaw), 0.0])
        av = np.array([0.0, 0.0, 1.0])
        centre = np.array([cx, cy, S["top_z"] - T])
        origin = centre - aw * (L / 2)
        # where each rail crosses this sleeper, in w (along) and p (across) coordinates
        seats = []
        for side in (-1, 1):
            xw = side * xr
            w = (xw - origin[0]) / aw[0]
            ycross = origin[1] + aw[1] * w
            seats.append((side, w, 0.0))
        w_seats = [w for _, w, _ in seats]
        droop = {-1: rng.uniform(0.0, 0.013), 1: rng.uniform(0.0, 0.013)}
        droop_start = max(abs(w - L / 2) for w in w_seats) + 0.075
        nrm = outward_normals(prof)
        s_arc = [0.0]
        for i in range(n):
            s_arc.append(s_arc[-1] + math.dist(prof[i], prof[(i + 1) % n]))
        endcut = {0: rng.uniform(-0.035, 0.035), 1: rng.uniform(-0.035, 0.035)}

        def deform(pos, i, px, py, w, ri, nr, seed=seed, L=L, T=T, nrm=nrm, s_arc=s_arc, au=au, av=av, aw=aw,
                   w_seats=w_seats, droop=droop, droop_start=droop_start, endcut=endcut):
            m = nrm[i][0]
            near_seat = min(abs(w - ws) for ws in w_seats) < 0.075 and py > T - 0.03
            amp = 0.0003 if near_seat else 1.0
            # the end chamfer ring takes its neighbour's displacement, so the chamfer cannot fold over
            wn = min(max(w, 0.011), L - 0.011)
            d = (0.0026 * float(nz(wn * 5.0, s_arc[i] * 5.0, seed, 3)) +
                 0.0009 * float(nz(wn * 22.0, s_arc[i] * 22.0, seed + 1, 2))) * amp
            if py < 0.02:
                d = d * 0.3
            pos = pos + d * (m[0] * au + m[1] * av)
            xl = w - L / 2
            side = 1 if xl > 0 else -1
            over = max(abs(xl) - droop_start, 0.0) / max(L / 2 - droop_start, 1e-3)
            pos = pos - np.array([0, 0, droop[side] * over * over])
            if ri <= 1 or ri >= nr - 2:
                e = 0 if ri <= 1 else 1
                # rough, slightly skew saw cut at each end
                cut = endcut[e] * (px / 0.15) + 0.004 * float(nz(py * 40.0, px * 40.0, seed + 2 + e, 2))
                pos = pos + aw * cut * (1 if e == 1 else -1) * 0.6
            return pos

        stations = sorted(set([float(x) for x in np.linspace(0, L, 13)[1:-1]] +
                              [w + dw for w in w_seats for dw in (-0.07, 0.07)]))
        tone = tones[order[k % len(tones)]]
        side_key = b.island(f"sleeper{k}_side", "wood", L, 0.0, seed=seed, tone=tone,
                            rust_wash=0.8 if tone == "rusty" else float(rng.uniform(0.15, 0.5)))
        c0 = b.island(f"sleeper{k}_end0", "endgrain", W, T, seed=seed + 11, tone=tone)
        c1 = b.island(f"sleeper{k}_end1", "endgrain", W, T, seed=seed + 12, tone=tone)
        (sw, sh), _ = prism(part, prof, L, origin, au, av, side_key, c0, c1, inset0=0.011, inset1=0.011,
                            stations=stations, deform=deform, vscale=vscale)
        isl = b.islands[side_key]
        isl.h = max(isl.h, sh)
        # texture annotations: v of the visible band, the top face and the seats
        sv = [0.0]
        for i in range(n):
            sv.append(sv[-1] + math.dist(prof[i], prof[(i + 1) % n]) * vscale[i])
        i_zb_r, i_top_r = 3, 6
        i_top_l, i_zb_l = 7, 10
        top_x_right = prof[i_top_r][0]
        isl.params.update(
            L=L, W=W, visible=(sv[i_zb_r], sv[i_zb_l]),
            top=(sv[i_top_r], sv[i_top_l]), buried_scale=0.25,
            seats=[(L - w, sv[i_top_r] + (top_x_right - 0.0)) for w in w_seats],
            spikes=[], seat_half=(0.05, 0.065), droop=droop)
        spikes_out.append(dict(k=k, origin=origin, aw=aw, au=au, L=L, top_x_right=top_x_right,
                               sv_top=sv[i_top_r], island=side_key, w_seats=w_seats, cy=cy, yaw=yaw))


def seat_y(sl, side):
    """y of the rail seat centre on sleeper `sl` for rail `side` (the sleeper centreline under the rail)."""
    w = sl["w_seats"][0 if side == -1 else 1]
    return float((sl["origin"] + sl["aw"] * w)[1])


def build_iron(b, variant, rng, sleepers):
    xr = PC["rail_centre_x"]
    BP = PC["base_plate"]
    plates = b.part("base_plates", "atlas", 35.0)
    spikes = b.part("spikes", "atlas", 35.0)
    ch = 0.0012
    pt = BP["thick"] + 0.0005                        # 0.5 mm let into the sleeper, top flush with the rail base
    plate_prof = [(0.0, 0.0), (BP["across"] / 2 - ch, 0.0), (BP["across"] / 2, ch), (BP["across"] / 2, pt - ch),
                  (BP["across"] / 2 - ch, pt), (-BP["across"] / 2 + ch, pt),
                  (-BP["across"] / 2, pt - ch), (-BP["across"] / 2, ch), (-BP["across"] / 2 + ch, 0.0)]
    zf = SEAT_Z + 0.0069                              # top of the rail foot's edge
    zb = PC["sleeper"]["top_z"] - 0.03
    spike_prof = [(-0.0135, zb), (-0.0005, zb), (-0.0005, zf), (0.0110, zf + 0.0006), (0.0122, zf + 0.0050),
                  (0.0062, zf + 0.0098), (-0.0150, zf + 0.0104), (-0.0166, zf + 0.0058), (-0.0138, zf + 0.0006)]
    nvar = 3
    for isl_k in range(nvar):
        b.island(f"plate_side{isl_k}", "rust", BP["along"], 0.0, seed=int(rng.integers(1e6)))
        b.island(f"plate_cap{isl_k}", "rust", BP["across"], BP["thick"], seed=int(rng.integers(1e6)))
        b.island(f"spike_side{isl_k}", "rust", 0.014, 0.0, seed=int(rng.integers(1e6)))
        b.island(f"spike_cap{isl_k}", "rust", 0.03, 0.06, seed=int(rng.integers(1e6)))
    count = 0
    missing = set()
    if variant == "straight":
        missing = {(1, -1, 1), (3, 1, -1)}          # (sleeper, rail side, spike side): pulled spikes
    for sl in sleepers:
        for side in (-1, 1):
            ys = seat_y(sl, side)
            v = count % nvar
            dims, cap = prism(plates, plate_prof, BP["along"], (side * xr, ys + BP["along"] / 2, PC["sleeper"]["top_z"] - 0.0005),
                              (1, 0, 0), (0, 0, 1), f"plate_side{v}", f"plate_cap{v}", f"plate_cap{v}",
                              inset0=0.0012, inset1=0.0012)
            b.islands[f"plate_side{v}"].h = max(b.islands[f"plate_side{v}"].h, dims[1])
            for sp_side in (-1, 1):                 # -1: gauge side, +1: field side
                if (sl["k"], side, sp_side) in missing:
                    continue
                lateral = side * sp_side            # +X or -X from the rail centre
                edge_x = side * xr + lateral * PC["rail"]["foot"] / 2
                au = np.array([-lateral, 0.0, 0.0])
                av = np.array([0.0, 0.0, 1.0])
                aw = np.cross(au, av)
                yc = ys + (0.026 if sp_side < 0 else -0.026) * side
                origin = np.array([edge_x, yc, 0.0]) - aw * 0.007
                v2 = (count + (sp_side > 0)) % nvar
                dims, _ = prism(spikes, spike_prof, 0.014, origin, au, av, f"spike_side{v2}", f"spike_cap{v2}",
                                f"spike_cap{v2}", inset0=0.0012, inset1=0.0012)
                b.islands[f"spike_side{v2}"].h = max(b.islands[f"spike_side{v2}"].h, dims[1])
                # rust bleed on the sleeper top around this spike (u along the sleeper, v across)
                isl = b.islands[sl["island"]]
                w_sp = (edge_x + lateral * 0.007 - sl["origin"][0]) / sl["aw"][0]
                y_on_centre = sl["origin"][1] + sl["aw"][1] * w_sp
                p_across = (yc - y_on_centre) * math.cos(sl["yaw"])
                isl.params["spikes"].append((sl["L"] - w_sp, sl["sv_top"] + (sl["top_x_right"] - p_across)))
            count += 1


def build_joint(b, rng, joint_y, inward):
    """Half fishplates and bolts at a rail joint at y = joint_y; the plates run `inward` (+1/-1 in y)."""
    FP = PC["fishplate"]
    xr = PC["rail_centre_x"]
    part = b.part(f"fishplates_{'hi' if joint_y > 0 else 'lo'}", "atlas", 35.0)
    bolts = b.part(f"bolts_{'hi' if joint_y > 0 else 'lo'}", "atlas", 35.0)
    ww = PC["rail"]["web"] / 2
    prof = [(ww + 0.0002, 0.0150), (ww + 0.0085, 0.0138), (ww + 0.0112, 0.0165), (ww + 0.0112, 0.0435),
            (ww + 0.0088, 0.0468), (ww + 0.0003, 0.0472)]
    b.island("fish_side", "rust", FP["half_length"], 0.0, seed=4401)
    b.island("fish_cap", "rust", 0.012, 0.035, seed=4402)
    hexp = [(0.0105 * math.cos(math.radians(a)), 0.0105 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
    sq = rounded_rect(0.019, 0.019, 0.0022, seg=1, y0=-0.0095)
    stub = [(0.0055 * math.cos(math.radians(a)), 0.0055 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
    b.island("bolt_side", "rust", 0.012, 0.0, seed=4403)
    b.island("bolt_cap", "rust", 0.022, 0.022, seed=4404)
    for rail in (-1, 1):
        for lat in (-1, 1):                         # -1 gauge side, +1 field side (outward from the track)
            direction = rail * lat                  # +X or -X
            au = np.array([direction, 0.0, 0.0])
            av = np.array([0.0, 0.0, 1.0])
            aw = np.cross(au, av)                   # (0, -direction, 0)
            # choose origin so w runs from the joint inwards when aw points inwards, else from the inner end
            if aw[1] * inward > 0:
                origin, i0, i1 = (rail * xr, joint_y, SEAT_Z), 0.0, 0.0015
            else:
                origin, i0, i1 = (rail * xr, joint_y + inward * FP["half_length"], SEAT_Z), 0.0015, 0.0
            dims, _ = prism(part, prof, FP["half_length"], origin, au, av, "fish_side", "fish_cap", "fish_cap",
                            inset0=i0, inset1=i1)
            b.islands["fish_side"].h = max(b.islands["fish_side"].h, dims[1])
            for dy in FP["bolts_from_joint"]:
                y = joint_y + inward * dy
                face_x = rail * xr + direction * (ww + 0.0112)
                bw = np.array([direction, 0.0, 0.0])
                bu, bv = frame_from(bw, np.array([0.0, 0.0, 1.0]))
                c = np.array([face_x, y, SEAT_Z + 0.030]) - bw * 0.0005
                if lat > 0:
                    d1, _ = prism(bolts, hexp, 0.0070, c, bu, bv, "bolt_side", "bolt_cap", "bolt_cap", inset1=0.0018)
                else:
                    d1, _ = prism(bolts, sq, 0.0110, c, bu, bv, "bolt_side", "bolt_cap", "bolt_cap", inset1=0.0012)
                    d2, _ = prism(bolts, stub, 0.0185, c, bu, bv, "bolt_side", "bolt_cap", "bolt_cap", inset1=0.0016)
                    d1 = (max(d1[0], d2[0]), max(d1[1], d2[1]))
                b.islands["bolt_side"].w = max(b.islands["bolt_side"].w, d1[0])
                b.islands["bolt_side"].h = max(b.islands["bolt_side"].h, d1[1])


# ---------------------------------------------------------------------------------- end stop
def build_buffer(b, rng):
    E = PC["end_stop"]
    wood = b.part("buffer_timber", "atlas", 40.0)
    iron = b.part("buffer_iron", "atlas", 35.0)
    L, D, H = E["beam"]
    seed = int(rng.integers(1e6))
    prof = rounded_rect(D, H, 0.016, seg=2)
    nrm = outward_normals(prof)

    def rough(amp, seed):
        def deform(pos, i, px, py, w, ri, nr):
            m = nrm[i][0]
            d = amp * float(nz(w * 5.0, (px + py) * 9.0, seed, 3))
            return pos + d * np.array([0.0, m[0], m[1]])
        return deform

    origin = np.array([-L / 2, E["beam_front_y"] + D / 2, E["beam_bottom_z"]])
    key = b.island("beam_side", "wood", L, 0.0, seed=seed, tone="dark", beam=True)
    (sw, sh), _ = prism(wood, prof, L, origin, (0, 1, 0), (0, 0, 1), key,
                        b.island("beam_end0", "endgrain", D, H, seed=seed + 1, tone="dark"),
                        b.island("beam_end1", "endgrain", D, H, seed=seed + 2, tone="dark"),
                        inset0=0.012, inset1=0.012, stations=np.linspace(0, L, 9)[1:-1], deform=rough(0.0022, seed))
    isl = b.islands[key]
    isl.h = max(isl.h, sh)
    # the front face (-Y) of the beam: profile runs bottom-centre -> +Y side -> top -> -Y side, so the front
    # face is the last quarter of V; the buffer plates sit there
    isl.params.update(L=L, W=D, visible=(-1.0, 1e9), top=(0.0, 0.0), seats=[], spikes=[], droop={-1: 0, 1: 0})
    pw, ph, pt = 0.14, 0.12, 0.009
    bprof = rounded_rect(pw, ph, 0.004, seg=1, y0=0.0)
    b.island("bplate_side", "rust", pt, 0.0, seed=4501)
    b.island("bplate_cap", "rust", pw, ph, seed=4502, impact=True)
    b.island("bhead_side", "rust", 0.009, 0.0, seed=4503)
    b.island("bhead_cap", "rust", 0.024, 0.024, seed=4504)
    zc = E["beam_bottom_z"] + H / 2
    for side in (-1, 1):
        x = side * PC["rail_centre_x"]
        origin = np.array([x, E["beam_front_y"] + 0.0012, zc - ph / 2])
        dims, _ = prism(iron, bprof, pt, origin, (1, 0, 0), (0, 0, 1), "bplate_side", "bplate_cap", "bplate_cap",
                        inset1=0.0016)
        b.islands["bplate_side"].h = max(b.islands["bplate_side"].h, dims[1])
        for dx in (-0.045, 0.045):
            hp = rounded_rect(0.022, 0.022, 0.003, seg=1, y0=-0.011)
            o = np.array([x + dx, E["beam_front_y"] - pt + 0.0012 + 0.0008, zc])
            dims, _ = prism(iron, hp, 0.009, o, (1, 0, 0), (0, 0, 1), "bhead_side", "bhead_cap", "bhead_cap",
                            inset1=0.002)
            b.islands["bhead_side"].h = max(b.islands["bhead_side"].h, dims[1])
    # posts behind the beam, driven into the ground
    pw_, pd_, ph_ = E["posts"]
    for side in (-1, 1):
        s2 = int(rng.integers(1e6))
        pp = rounded_rect(pw_, pd_, 0.014, seg=2, y0=-pd_ / 2)
        nrm_p = outward_normals(pp)

        def pdeform(pos, i, px, py, w, ri, nr, s2=s2, nrm_p=nrm_p):
            m = nrm_p[i][0]
            d = 0.002 * float(nz(w * 6.0, (px - py) * 9.0, s2, 3))
            return pos + d * np.array([m[0], m[1], 0.0])

        origin = np.array([side * E["post_x"], E["beam_front_y"] + D + pd_ / 2, 0.0])
        key = b.island(f"post{side}_side", "wood", ph_, 0.0, seed=s2, tone="brown", post=True)
        (sw, sh), _ = prism(wood, pp, ph_, origin, (1, 0, 0), (0, 1, 0), key,
                            b.island(f"post{side}_end0", "endgrain", pw_, pd_, seed=s2 + 1, tone="dark"),
                            b.island(f"post{side}_end1", "endgrain", pw_, pd_, seed=s2 + 2, tone="dark"),
                            inset0=0.0, inset1=0.013, stations=np.linspace(0, ph_, 6)[1:-1], deform=pdeform)
        isl = b.islands[key]
        isl.h = max(isl.h, sh)
        isl.params.update(L=ph_, W=pw_, visible=(-1.0, 1e9), top=(0.0, 0.0), seats=[], spikes=[],
                          droop={-1: 0, 1: 0}, buried_u=(ph_ - 0.22, ph_))
        # raking strut from high on the post's back face down into the heap
        p0 = np.array([side * E["post_x"], 1.195, 0.43])
        p1 = np.array([side * E["post_x"], 1.435, 0.052])
        aw = unit(p1 - p0)
        au = np.array([1.0, 0.0, 0.0])
        av = np.cross(aw, au)
        sp = rounded_rect(E["strut_section"], E["strut_section"], 0.012, seg=2, y0=-E["strut_section"] / 2)
        s3 = int(rng.integers(1e6))
        key = b.island(f"strut{side}_side", "wood", float(np.linalg.norm(p1 - p0)), 0.0, seed=s3, tone="brown")
        (sw, sh), _ = prism(wood, sp, float(np.linalg.norm(p1 - p0)), p0, au, av, key,
                            b.island(f"strut{side}_end0", "endgrain", 0.09, 0.09, seed=s3 + 1, tone="brown"),
                            b.island(f"strut{side}_end1", "endgrain", 0.09, 0.09, seed=s3 + 2, tone="brown"),
                            inset0=0.008, inset1=0.008, stations=np.linspace(0, 0.45, 5)[1:-1])
        isl = b.islands[key]
        isl.h = max(isl.h, sh)
        isl.params.update(L=float(np.linalg.norm(p1 - p0)), W=0.09, visible=(-1.0, 1e9), top=(0.0, 0.0), seats=[],
                          spikes=[], droop={-1: 0, 1: 0})


# ---------------------------------------------------------------------------------- stones
def rock_mesh(rng, r):
    k = int(rng.integers(8, 11))
    pts = rng.normal(size=(k, 3))
    pts /= np.linalg.norm(pts, axis=1, keepdims=True)
    pts *= rng.uniform(0.86, 1.0, size=(k, 1))
    ax = np.array([r, r * rng.uniform(0.7, 0.95), r * rng.uniform(0.6, 0.85)])
    pts *= ax
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    for gone in res.get("geom_interior", []) + res.get("geom_unused", []):
        if isinstance(gone, bmesh.types.BMVert) and gone.is_valid:
            bm.verts.remove(gone)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.verts.index_update()
    verts = np.array([v.co[:] for v in bm.verts])
    faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    return verts, faces


def rot_matrix(rng, tilt_deg):
    yaw = rng.uniform(0, 2 * math.pi)
    t1, t2 = math.radians(rng.uniform(-tilt_deg, tilt_deg)), math.radians(rng.uniform(-tilt_deg, tilt_deg))
    cz, sz = math.cos(yaw), math.sin(yaw)
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, math.cos(t1), -math.sin(t1)], [0, math.sin(t1), math.cos(t1)]])
    Ry = np.array([[math.cos(t2), 0, math.sin(t2)], [0, 1, 0], [-math.sin(t2), 0, math.cos(t2)]])
    return Rz @ Rx @ Ry


def build_rocks(b, variant, rng, sleepers):
    part = b.part("stones", "atlas", 62.0)
    R = PC["rocks"]
    n_target = R["count_straight"] if variant == "straight" else R["count_end"]
    patch = 0.24
    tones = [("stone0", (0.58, 0.57, 0.54)), ("stone1", (0.46, 0.45, 0.43)), ("stone2", (0.34, 0.34, 0.33)),
             ("stone3", (0.50, 0.46, 0.40))]
    for key, tone in tones:
        b.island(key, "stone", patch, patch, seed=int(rng.integers(1e6)), tone=tone)
    xr = PC["rail_centre_x"]
    E = PC["end_stop"]

    def blocked(x, y, r):
        if abs(abs(x) - xr) < 0.052 + r * 0.8 and (variant == "straight" or y < E["rail_end_y"] + 0.02):
            return True
        for sl in sleepers:
            # sleeper footprint in its own frame
            d = np.array([x, y, 0.0]) - (sl["origin"] + sl["aw"] * sl["L"] / 2)
            along = abs(float(d @ sl["aw"]))
            across = abs(float(d @ sl["au"]))
            if along < sl["L"] / 2 + 0.012 + r and across < PC["sleeper"]["width"] / 2 + 0.012 + r * 0.8:
                return True
        if variant == "end":
            if y > 0.86 and y < 1.16 and abs(x) < 0.56:       # under/at the beam
                return True
            if y > 1.06 and abs(abs(x) - E["post_x"]) < 0.13 + r:  # posts and struts
                return True
        return False

    placed = []
    tries = 0
    while len(placed) < n_target and tries < n_target * 40:
        tries += 1
        u = rng.random()
        r = R["r_min"] + (R["r_max"] - R["r_min"]) * u ** 2.0
        y = rng.uniform(-HALF + r * 1.05 + 0.004, HALF - r * 1.05 - 0.004)
        side = -1 if rng.random() < 0.5 else 1
        hw = float(halfwidth(y, side, variant))
        x = side * rng.uniform(0.0, max(hw - r * 1.1, 0.0))
        # more stone in the cribs and on the shoulders, fewer at the very fringe
        edge = abs(x) / hw
        if edge > 0.93 and rng.random() < 0.6:
            continue
        if blocked(x, y, r):
            continue
        if any((x - px) ** 2 + (y - py) ** 2 < (0.7 * (r + pr)) ** 2 for px, py, pr in placed):
            continue
        placed.append((x, y, r))
    for (x, y, r) in placed:
        verts, faces = rock_mesh(rng, r)
        M = rot_matrix(rng, 15.0)
        verts = verts @ M.T
        h = float(ballast_height(np.array([x]), np.array([y]), variant)[0])
        zmin, zmax = float(verts[:, 2].min()), float(verts[:, 2].max())
        sink = rng.uniform(0.35, 0.6) * (zmax - zmin)
        z = h - sink - zmin
        z = max(z, 0.0008 - zmin)
        verts = verts + np.array([x, y, z])
        # keep strictly inside the module length and the ballast's +-max_half
        if verts[:, 1].max() > HALF - 0.001 or verts[:, 1].min() < -HALF + 0.001:
            continue
        if np.abs(verts[:, 0]).max() > PC["ballast"]["max_half"] - 0.002:
            continue
        key, _ = tones[int(rng.integers(len(tones)))]
        base = len(part.v)
        for p in verts:
            part.add(p)
        for f in faces:
            P = verts[f]
            # Newell's method over every edge, not just the first 3 verts: a convex-hull face can be
            # an n-gon whose leading vertices are near-collinear, which made cross(P[1]-P[0], P[2]-P[0])
            # near zero and gave the whole polygon a garbage (near-zero-area) UV basis
            n = np.zeros(3)
            for i in range(len(P)):
                n += np.cross(P[i], P[(i + 1) % len(P)])
            n = n / (np.linalg.norm(n) + 1e-12)
            # tangent from the face's own first edge, not a fixed world axis: a sliver hull facet (a
            # thin-but-real triangle, an unavoidable artifact of a near-coplanar cluster of hull points)
            # could have its whole extent sitting almost exactly along whichever world axis got picked,
            # collapsing that face to a zero-area UV footprint; an edge of the face itself is never
            # perpendicular to the face's own plane, so this basis cannot degenerate that way
            t = P[1] - P[0]
            t = t / (np.linalg.norm(t) + 1e-12)
            bb = np.cross(n, t)
            c = P.mean(axis=0)
            uv = [((p - c) @ t, (p - c) @ bb) for p in P]
            ext = max(max(abs(a), abs(b_)) for a, b_ in uv)
            room = max(patch / 2 - ext - 0.004, 0.0)
            ou, ov = patch / 2 + rng.uniform(-room, room), patch / 2 + rng.uniform(-room, room)
            part.face([base + i for i in f], [(a + ou, b_ + ov) for a, b_ in uv], key)
        part.solids += 1
    return len(placed)


def build_loose_spikes(b, variant, rng, sleepers, count):
    part = b.part("loose_spikes", "atlas", 35.0)
    prof = [(0.000, 0.0045), (0.012, 0.0), (0.115, 0.0), (0.117, -0.011), (0.127, -0.012), (0.128, 0.008),
            (0.126, 0.015), (0.012, 0.013), (0.000, 0.0085)]
    b.island("loose_side", "rust", 0.013, 0.0, seed=4601)
    b.island("loose_cap", "rust", 0.13, 0.03, seed=4602)
    done = 0
    tries = 0
    while done < count and tries < 400:
        tries += 1
        x = rng.uniform(-0.5, 0.5)
        y = rng.uniform(-1.3, 1.3 if variant == "straight" else 0.7)
        if abs(abs(x) - PC["rail_centre_x"]) < 0.12:
            continue
        clear = True
        for sl in sleepers:
            if abs(y - sl["cy"]) < 0.16:
                clear = False
        if not clear:
            continue
        phi = rng.uniform(0, 2 * math.pi)
        au = np.array([math.cos(phi), math.sin(phi), 0.0])
        av = np.array([-math.sin(phi), math.cos(phi), 0.0])
        c = np.array([x, y, 0.0])
        h = float(ballast_height(np.array([x]), np.array([y]), variant)[0])
        origin = c - au * 0.064 + np.array([0, 0, h - 0.004])
        dims, _ = prism(part, prof, 0.013, origin, au, av, "loose_side", "loose_cap", "loose_cap",
                        inset0=0.001, inset1=0.001)
        b.islands["loose_side"].h = max(b.islands["loose_side"].h, dims[1])
        done += 1


def build_module(variant, seed):
    rng = np.random.default_rng([seed, 1 if variant == "straight" else 2])
    b = Build()
    build_ballast(b, variant)
    sleepers = []
    build_sleepers(b, variant, rng, sleepers)
    build_rails(b, variant, rng)
    build_iron(b, variant, rng, sleepers)
    build_joint(b, rng, -HALF, +1)
    if variant == "straight":
        build_joint(b, rng, HALF, -1)
    else:
        build_buffer(b, rng)
    n_rocks = build_rocks(b, variant, rng, sleepers)
    build_loose_spikes(b, variant, rng, sleepers, 2)
    return b, {"sleepers": len(sleepers), "stones": n_rocks, "loose_spikes": 2}


# ============================================================================================ checks
def check_part(part):
    """Every edge used once in each direction (closed, consistently wound); positive volume per solid;
    quads planar-convex (no bow-ties)."""
    V = np.array(part.v)
    directed = {}
    for fi, f in enumerate(part.f):
        for i in range(len(f)):
            e = (f[i], f[(i + 1) % len(f)])
            directed[e] = directed.get(e, 0) + 1
    bad = 0
    for (a, b_), c in directed.items():
        if c != 1 or directed.get((b_, a), 0) != 1:
            bad += 1
    # connected components by shared vertices
    parent = list(range(len(V)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for f in part.f:
        for i in range(1, len(f)):
            ra, rb = find(f[0]), find(f[i])
            if ra != rb:
                parent[ra] = rb
    vol = {}
    for f in part.f:
        p0 = V[f[0]]
        for i in range(1, len(f) - 1):
            v = float(np.dot(p0, np.cross(V[f[i]], V[f[i + 1]]))) / 6.0
            r = find(f[0])
            vol[r] = vol.get(r, 0.0) + v
    inverted = sum(1 for v in vol.values() if v <= 0)
    bowtie = []
    for f in part.f:
        if len(f) == 4:
            a, b_, c, d = (V[i] for i in f)
            n1 = np.cross(b_ - a, c - a)
            n2 = np.cross(c - a, d - a)
            n3 = np.cross(b_ - a, d - a)
            n4 = np.cross(c - b_, d - b_)
            if np.dot(n1, n2) <= 0 or np.dot(n3, n4) <= 0:
                bowtie.append([[round(float(x), 4) for x in V[i]] for i in f])
    out = {"part": part.name, "faces": len(part.f), "solids": len(vol), "open_or_misrouted_edges": bad,
           "inverted_solids": inverted, "bowtie_quads": len(bowtie), "min_volume_m3": min(vol.values())}
    if bowtie:
        out["bowtie_examples"] = bowtie[:3]
    return out


# ============================================================================================ atlas
def pack(islands, S, D, pad):
    """Shelf pack at density D (px/m); the rail band spans the full width at the bottom. None if it overflows."""
    y = 0
    band = [i for i in islands if i.band]
    for isl in band:
        isl.wpx, isl.hpx = S, int(math.ceil(isl.h * D)) + 1
        isl.x0, isl.y0 = 0, y + pad
        y += isl.hpx + 2 * pad
    rest = [i for i in islands if not i.band]
    for isl in rest:
        isl.wpx, isl.hpx = int(math.ceil(isl.w * D)) + 1, int(math.ceil(isl.h * D)) + 1
        if isl.wpx + 2 * pad > S:
            return None
    rest.sort(key=lambda i: (-i.hpx, -i.wpx, i.key))
    shelves = []                                    # [y, height, x_cursor]
    for isl in rest:
        need_w = isl.wpx + 2 * pad
        need_h = isl.hpx + 2 * pad
        spot = None
        for sh in shelves:
            if sh[2] + need_w <= S and need_h <= sh[1]:
                spot = sh
                break
        if spot is None:
            if y + need_h > S:
                return None
            spot = [y, need_h, 0]
            shelves.append(spot)
            y += need_h
        isl.x0, isl.y0 = spot[2] + pad, spot[0] + pad
        spot[2] += need_w
    used = sum(i.wpx * i.hpx for i in islands) / float(S * S)
    return used


def blur(a, sigma, wrap_x=False, wrap_y=False):
    r = max(1, int(3 * sigma))
    k = np.exp(-(np.arange(-r, r + 1) ** 2) / (2 * sigma * sigma))
    k /= k.sum()
    out = np.pad(a, ((0, 0), (r, r)), mode="wrap" if wrap_x else "edge")
    out = sum(k[i] * out[:, i:i + a.shape[1]] for i in range(2 * r + 1))
    out2 = np.pad(out, ((r, r), (0, 0)), mode="wrap" if wrap_y else "edge")
    return sum(k[i] * out2[i:i + a.shape[0], :] for i in range(2 * r + 1))


def mixc(a, b, t):
    t = np.asarray(t)[..., None]
    return a + (np.asarray(b) - a) * t


def _cracks(U, V, p, rng, L, v_lo, v_hi, count):
    """Checks and splits along the grain: tapering dark lines with a little drift, some from the ends."""
    s = p["seed"]
    crack = np.zeros_like(U)
    for c in range(count):
        vc = rng.uniform(v_lo, v_hi)
        from_end = rng.random() < 0.55
        length = rng.uniform(0.07, 0.5) * (L if L < 0.8 else 1.0)
        width = rng.uniform(0.0010, 0.0036)
        at_start = rng.random() < 0.5
        if from_end:
            us, ue = (0.0, length) if at_start else (L - length, L)
        else:
            us = rng.uniform(0.0, max(L - length, 0.01))
            ue = us + length
        drift = 0.0035 * nz(U * 2.5, np.full_like(U, vc * 10.0 + c * 7.3), s + 40 + c, 2)
        t = np.clip((U - us) / max(ue - us, 1e-3), 0.0, 1.0)
        inside = (U > us) & (U < ue)
        if from_end:
            taper = (1.0 - t) ** 0.8 if at_start else t ** 0.8
        else:
            taper = np.sin(np.pi * t) ** 0.7
        w = width * taper
        d = np.abs(V - vc - drift)
        crack = np.maximum(crack, sstep(w + 0.0004, w * 0.4, d) * inside * (w > 1e-5))
    return crack


def paint_wood(U, V, p):
    s = p["seed"]
    rng = np.random.default_rng(s)
    light, dark, weather = TONES[p.get("tone", "grey")]
    light, dark = np.array(light), np.array(dark)
    L = p.get("L", 1.0)
    vis0, vis1 = p.get("visible", (-1.0, 1e9))
    t0, t1 = p.get("top", (0.0, 0.0))
    H = p.get("h", 0.5)
    wv = V + 0.010 * nz(U * 0.9, V * 3.0, s, 3) + 0.0015 * nz(U * 6.0, V * 25.0, s + 1, 2)
    phase = wv * 140.0 + 2.2 * nz(U * 1.1, wv * 5.0, s + 2, 3)
    ring = 0.5 + 0.5 * np.sin(2.0 * np.pi * phase)
    line = sstep(0.80, 0.97, ring)
    streak = nz(U * 1.2, wv * 40.0, s + 3, 4)
    fibre = nz(U * 22.0, wv * 380.0, s + 4, 2)
    v_lo = max(vis0, 0.004) if vis0 > 0 else 0.004
    v_hi = min(vis1, H - 0.004) if vis1 < 1e8 else H - 0.004
    crack = _cracks(U, V, p, rng, L, v_lo, v_hi, int(rng.integers(5, 10)))
    base = mixc(light, dark, np.clip(0.45 + 0.20 * streak + 0.05 * fibre, 0.0, 1.0))
    base = mixc(base, dark * 0.7, line * 0.65)
    checks = sstep(1.7, 2.4, nz(U * 9.0, wv * 260.0, s + 16, 2)) * sstep(-0.2, 0.8, nz(U * 2.0, V * 2.0, s + 17, 2))
    base = mixc(base, dark * 0.45, checks * 0.7)
    if t1 > t0:
        top = sstep(t0 - 0.015, t0 + 0.005, V) * sstep(t1 + 0.015, t1 - 0.005, V)
    else:
        top = np.zeros_like(V)
    silver = np.clip(weather + 0.18 * nz(U * 2.0, V * 2.0, s + 5, 3) + 0.25 * top, 0.0, 1.0)
    grey = np.array([0.54, 0.51, 0.46])
    base = mixc(base, grey * (0.93 + 0.07 * fibre[..., None]), silver * 0.38)
    if t1 > t0:
        base = base * (0.84 + 0.16 * top)[..., None]      # sun-bleached tops, darker weathered sides
    decay = sstep(1.3, 2.1, nz(U * 9.0, V * 9.0, s + 6, 4))
    base = mixc(base, np.array([0.12, 0.10, 0.08]), decay * 0.45)
    mott = sstep(0.3, 1.5, nz(U * 5.0, V * 5.0, s + 18, 3))
    base = mixc(base, dark * 0.6, mott * 0.45)
    pits = sstep(1.8, 2.5, nz(U * 110.0, V * 110.0, s + 19, 1))
    base = mixc(base, dark * 0.35, pits * 0.7)
    cluster = sstep(0.2, 1.2, nz(U * 3.0, V * 3.0, s + 7, 3)) * p.get("lichen", 1.0)
    spots = sstep(1.0, 1.6, nz(U * 70.0, V * 70.0, s + 8, 3))
    lich = cluster * spots
    base = mixc(base, np.array([0.62, 0.63, 0.57]), lich * 0.6)
    green = sstep(1.3, 2.2, nz(U * 6.0, V * 6.0, s + 9, 3)) * 0.5
    base = mixc(base, np.array([0.33, 0.35, 0.24]), green * 0.5)
    below = ((V < vis0) | (V > vis1)).astype(np.float64)
    near = np.maximum(sstep(0.03, 0.0, V - vis0) * (V >= vis0), sstep(0.03, 0.0, vis1 - V) * (V <= vis1))
    earth = np.array([0.27, 0.24, 0.20])
    dirt = np.clip(below * 0.85 + near * 0.6 * (0.75 + 0.25 * fibre), 0.0, 1.0)
    if "buried_u" in p:
        bu0 = p["buried_u"][0]
        dirt = np.maximum(dirt, sstep(bu0 - 0.06, bu0 + 0.02, U) * 0.75)
    base = mixc(base, earth, dirt)
    rust = np.zeros_like(U)
    for (us, vs) in p.get("spikes", []):
        du, dv = (U - us) / 0.06, (V - vs) / 0.02
        rust = np.maximum(rust, np.exp(-(du * du + dv * dv)) * (0.75 + 0.25 * nz(U * 50, V * 50, s + 13, 2)))
    seat_ao = np.ones_like(U)
    for (us, vs) in p.get("seats", []):
        vc = (t0 + t1) / 2.0
        du = np.maximum(np.abs(U - us) - 0.065, 0.0)
        dv = np.maximum(np.abs(V - vc) - 0.05, 0.0)
        dist = np.sqrt(du * du + dv * dv)
        inside = (np.abs(U - us) < 0.065) & (np.abs(V - vc) < 0.05)
        seat_ao = np.minimum(seat_ao, np.where(inside, 0.5, 1.0 - 0.5 * np.exp(-dist / 0.01)))
        bleed = np.exp(-(du / 0.05) ** 2 - (dv / 0.012) ** 2) * (1 - inside)
        rust = np.maximum(rust, 0.45 * bleed * sstep(0.2, 1.3, nz(U * 30, V * 30, s + 14, 3)))
    if p.get("rust_wash"):
        wash = sstep(0.0, 1.4, nz(U * 3.0, V * 3.0, s + 15, 3)) * (0.6 + 0.4 * sstep(-1.0, 1.0, streak))
        rust = np.maximum(rust, p["rust_wash"] * wash)
    rust = np.clip(rust, 0.0, 1.0)
    base = mixc(base, np.array([0.44, 0.22, 0.10]) * (0.85 + 0.15 * fibre[..., None]), rust * 0.8)
    base = mixc(base, np.array([0.06, 0.05, 0.04]), crack * 0.95)
    h = (0.0005 * sstep(0.55, 0.95, ring) + 0.0005 * streak + 0.00012 * fibre - 0.0012 * checks - 0.0008 * pits +
         0.0012 * nz(U * 4.0, V * 4.0, s + 11, 3) - 0.004 * crack - 0.0012 * decay + 0.0003 * lich)
    rough = np.clip(0.80 + 0.04 * streak + 0.12 * crack + 0.06 * lich - 0.03 * line - 0.1 * rust, 0.6, 1.0)
    ao = np.clip(1.0 - 0.6 * crack - 0.25 * decay, 0.0, 1.0) * seat_ao
    return base, h, rough, np.zeros_like(U), ao


def paint_endgrain(U, V, p):
    s = p["seed"]
    light, dark, weather = TONES[p.get("tone", "grey")]
    rng = np.random.default_rng(s)
    W, T = p.get("w", 0.15), p.get("h", 0.10)
    cx = W / 2 + rng.choice([-1, 1]) * rng.uniform(0.0, 0.07)
    cy = rng.uniform(-0.10, T * 0.4)
    dx, dy = U - cx, V - cy
    r = np.sqrt(dx * dx + dy * dy) + 0.002 * nz(U * 30, V * 30, s, 3)
    ang = np.arctan2(dy, dx)
    rings = 0.5 + 0.5 * np.sin(2 * np.pi * r / 0.0045)
    checks = np.zeros_like(U)
    for k in range(int(rng.integers(2, 5))):
        a0 = rng.uniform(-math.pi, math.pi)
        da = np.angle(np.exp(1j * (ang - a0)))
        wk = rng.uniform(0.001, 0.003)
        checks = np.maximum(checks, sstep(wk, 0.0, np.abs(da) * r) * sstep(0.0, 0.02, r))
    base = mixc(np.array(light) * 0.8, np.array(dark) * 0.85, 0.35 + 0.3 * rings)
    base = mixc(base, np.array([0.47, 0.45, 0.42]), weather * 0.5)
    rot = sstep(0.6, 1.6, nz(U * 14, V * 14, s + 3, 3))
    base = mixc(base, np.array([0.16, 0.13, 0.10]), rot * 0.55)
    base = mixc(base, np.array([0.05, 0.04, 0.03]), checks)
    h = (rings - 0.5) * 0.0004 - checks * 0.004 - rot * 0.0015 + 0.0004 * nz(U * 60, V * 60, s + 4, 2)
    rough = np.clip(0.88 + 0.06 * rot, 0, 1)
    ao = 1.0 - 0.6 * checks
    return base, h, rough, np.zeros_like(U), ao


def paint_rust(U, V, p, period=None):
    s = p["seed"]

    def pf(fu, fv, seed, octv):
        if period:
            cells = max(1, int(round(fu * period)))
            return nz(U * (cells / period), V * fv, seed, octv, px=cells)
        return nz(U * fu, V * fv, seed, octv)

    blot = pf(5.0, 2.5, s, 4)
    blot2 = pf(16.0, 9.0, s + 1, 3)
    streak = pf(90.0, 6.0, s + 2, 3)
    pits = sstep(1.6, 2.3, pf(260.0, 260.0, s + 3, 1))
    grain = pf(140.0, 140.0, s + 4, 2)
    crust = pf(330.0, 330.0, s + 6, 2)
    base = mixc(np.array([0.31, 0.19, 0.125]), np.array([0.17, 0.11, 0.08]), sstep(-0.9, 0.9, blot))
    base = mixc(base, np.array([0.44, 0.25, 0.13]), sstep(0.8, 2.2, blot2) * 0.4)
    base = mixc(base, np.array([0.14, 0.09, 0.07]), sstep(0.2, 2.2, streak) * 0.3)
    base = base * (0.86 + 0.08 * grain[..., None] + 0.06 * crust[..., None])
    base = mixc(base, np.array([0.09, 0.065, 0.05]), pits * 0.65)
    h = (0.0003 * blot2 + 0.00025 * sstep(0.2, 0.6, blot) + 0.00015 * grain + 0.00012 * crust
         - 0.0005 * pits)
    rough = np.clip(0.86 + 0.04 * blot2 + 0.06 * pits, 0.6, 1.0)
    metal = np.zeros_like(U)
    if "top" in p:
        t0, t1 = p["top"]
        top = sstep(t0 - 0.002, t0 + 0.002, V) * sstep(t1 + 0.002, t1 - 0.002, V)
        shine = sstep(-0.3, 1.0, pf(2.0, 30.0, s + 5, 3))
        worn = mixc(np.array([0.30, 0.21, 0.16]), np.array([0.40, 0.33, 0.28]), shine)
        base = mixc(base, worn * (0.93 + 0.05 * grain[..., None]), top * 0.8)
        h = h * (1.0 - 0.7 * top)
        rough = rough * (1.0 - 0.3 * top)
        metal = metal + 0.3 * top * shine
    if p.get("impact"):
        c = sstep(0.045, 0.0, np.sqrt((U - 0.07) ** 2 + (V - 0.06) ** 2))
        base = mixc(base, np.array([0.28, 0.24, 0.21]), c * 0.6)
        metal = np.maximum(metal, 0.3 * c)
        rough = rough * (1.0 - 0.25 * c)
    if p.get("cut"):
        base = base * 0.85
    return base, h, rough, metal, np.ones_like(U)


def paint_stone(U, V, p):
    s = p["seed"]
    tone = np.array(p["tone"])
    n1 = nz(U * 22.0, V * 22.0, s, 4)
    n2 = nz(U * 120.0, V * 120.0, s + 1, 2)
    speck = sstep(1.6, 2.2, nz(U * 500.0, V * 500.0, s + 2, 1))
    base = tone * (1.0 + 0.07 * n1[..., None]) * (1.0 + 0.04 * n2[..., None])
    base = mixc(base, tone * 0.55, speck * 0.5)
    stain = sstep(0.9, 1.9, nz(U * 10.0, V * 10.0, s + 3, 3))
    base = mixc(base, np.array([0.36, 0.29, 0.22]), stain * 0.35)
    h = n1 * 0.0005 + n2 * 0.00015
    rough = np.clip(0.84 + 0.03 * n1, 0.7, 0.95)
    return base, h, rough, np.zeros_like(U), np.ones_like(U)


# ---------------------------------------------------------------------------------- ballast texture
STONE_PALETTE = [(0.57, 0.56, 0.54), (0.45, 0.44, 0.42), (0.35, 0.35, 0.34), (0.50, 0.46, 0.41),
                 (0.64, 0.63, 0.60), (0.27, 0.27, 0.26), (0.42, 0.37, 0.31), (0.52, 0.52, 0.51)]


def voronoi(u, v, N, seed, jitter=0.9):
    """Periodic Voronoi on an N x N jittered lattice over the unit tile: F1, F2 (cell units), a cell hash
    in [0, 1) and the offset from the nearest feature point."""
    gx, gy = u * N, v * N
    cx, cy = np.floor(gx), np.floor(gy)
    f1 = np.full(u.shape, 9.0)
    f2 = np.full(u.shape, 9.0)
    cid = np.zeros(u.shape)
    ox = np.zeros(u.shape)
    oy = np.zeros(u.shape)
    for dj in (-1.0, 0.0, 1.0):
        for di in (-1.0, 0.0, 1.0):
            kx, ky = cx + di, cy + dj
            mx = np.mod(kx, N).astype(np.int64)
            my = np.mod(ky, N).astype(np.int64)
            px = kx + 0.5 + (_hash(mx, my, seed) - 0.5) * jitter
            py = ky + 0.5 + (_hash(mx, my, seed + 1) - 0.5) * jitter
            dxx, dyy = gx - px, gy - py
            d = np.sqrt(dxx * dxx + dyy * dyy)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            cid = np.where(closer, _hash(mx, my, seed + 2), cid)
            ox = np.where(closer, dxx, ox)
            oy = np.where(closer, dyy, oy)
            f1 = np.where(closer, d, f1)
    return f1, f2, cid, ox, oy


def crushed_stone(S, tile, seed):
    """Periodic crushed-stone ballast, S px per tile: three Voronoi layers of broken stone (large, medium,
    small) over dark fines. Each stone is a faceted lump (three random cutting planes over a low dome)
    that falls to the fines at its cell edge; a quarter of the large and medium cells are left empty so
    sizes mix. Layers are composited by height, so the highest stone at a texel gives its colour."""
    jj, ii = np.meshgrid(np.arange(S), np.arange(S))
    u, v = (jj + 0.5) / S, (ii + 0.5) / S
    pal = np.array(STONE_PALETTE)
    dust = np.array([0.11, 0.10, 0.09])
    h = np.zeros((S, S))
    col = np.broadcast_to(dust, (S, S, 3)) * (0.85 + 0.3 * fbm(u * 64, v * 64, seed + 40, 2, px=64, py=64))[..., None]
    for N, hmin, hmax, keep, sd in ((int(round(tile / 0.048)), 0.011, 0.024, 0.78, seed),
                                    (int(round(tile / 0.026)), 0.006, 0.012, 0.8, seed + 10),
                                    (int(round(tile / 0.013)), 0.0025, 0.005, 1.0, seed + 20)):
        f1, f2, cid, ox, oy = voronoi(u, v, N, sd)
        e = np.maximum((f2 - f1) * 0.5, 1e-4)
        rad = f1 + e
        r = f1 / rad
        key = (cid * 65521).astype(np.int64)
        present = _hash(key, np.int64(9), sd + 9) < keep
        hc = hmin + (hmax - hmin) * _hash(key, np.int64(1), sd + 3)
        cut = r * 0.8
        for k in range(3):
            a = _hash(key, np.int64(20 + k), sd + 11 + k) * 2 * math.pi
            off = 0.15 + 0.35 * _hash(key, np.int64(30 + k), sd + 21 + k)
            cut = np.maximum(cut, (ox * np.cos(a) + oy * np.sin(a)) / rad + off)
        tilt = ((_hash(key, np.int64(4), sd + 7) - 0.5) * ox + (_hash(key, np.int64(5), sd + 8) - 0.5) * oy) / rad
        lump = hc * np.clip(1.0 - np.clip(cut, 0.0, 1.0) ** 1.4 + 0.25 * tilt, 0.0, 1.3)
        lump = lump * sstep(0.0, 0.12, 1.0 - r) * present
        colour = pal[(_hash(key, np.int64(7), sd + 6) * len(pal)).astype(np.int64) % len(pal)]
        shade = 0.85 + 0.25 * _hash(key, np.int64(2), sd + 4)
        colour = colour * shade[..., None] * (0.82 + 0.18 * (1.0 - r ** 3))[..., None]
        win = lump > h
        h = np.where(win, lump, h)
        col = np.where(win[..., None], colour, col)
    speck = sstep(1.5, 2.3, nz(u * 256, v * 256, seed + 30, 1, px=256, py=256))
    col = col * (1.0 - 0.22 * speck[..., None])
    grime = nz(u * 6.0, v * 6.0, seed + 31, 3, px=6, py=6)
    col = col * (0.93 + 0.05 * grime[..., None])
    return col, h


def ballast_maps(S, work, seed=5150):
    tile = PC["ballast"]["tile_m"]
    ss = PC["ballast"]["ss"]
    col, h = crushed_stone(S * ss, tile, seed)
    if ss > 1:
        # box-filter the supersampled bake: stone outlines come out anti-aliased
        col = col.reshape(S, ss, S, ss, 3).mean(axis=(1, 3))
        h = h.reshape(S, ss, S, ss).mean(axis=(1, 3))
    texel = tile / S
    cav = np.clip((blur(h, 4.0, wrap_x=True, wrap_y=True) - h) / 0.005, 0.0, 1.0)
    ao = np.clip(1.0 - 0.7 * cav, 0.0, 1.0)
    gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * texel)
    gy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2 * texel)
    n = np.stack([-gx, -gy, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    rough = np.clip(0.86 + 0.08 * cav, 0.0, 1.0)
    col = col * (0.72 + 0.28 * ao[..., None])
    paths = {}
    for name, arr in (("basecolor", col), ("normal", n * 0.5 + 0.5),
                      ("orm", np.stack([ao, rough, np.zeros_like(h)], axis=-1))):
        path = os.path.join(work, f"ballast_{name}.png")
        write_png(path, to_u8(arr))
        paths[name] = path
    return {"paths": paths, "mean_srgb": [round(float(x) * 255, 1) for x in col.reshape(-1, 3).mean(axis=0)]}


def paint_atlas(islands, S, D, pad):
    base = np.zeros((S, S, 3))
    base[:] = (0.35, 0.28, 0.22)
    height = np.zeros((S, S))
    rough = np.full((S, S), 0.85)
    metal = np.zeros((S, S))
    ao = np.ones((S, S))
    nrm = np.zeros((S, S, 3))
    nrm[..., 2] = 1.0
    texel = 1.0 / D
    for isl in islands:
        if isl.band:
            j0, j1 = 0, S
        else:
            j0, j1 = isl.x0 - pad, isl.x0 + isl.wpx + pad
        i0, i1 = isl.y0 - pad, isl.y0 + isl.hpx + pad
        jj, ii = np.meshgrid(np.arange(j0, j1), np.arange(i0, i1))
        U = (jj + 0.5 - isl.x0) / D
        V = (ii + 0.5 - isl.y0) / D
        p = dict(isl.params)
        p.setdefault("w", isl.w)
        p.setdefault("h", isl.h)
        if isl.kind == "wood":
            b, h, r, m, a = paint_wood(U, V, p)
        elif isl.kind == "endgrain":
            b, h, r, m, a = paint_endgrain(U, V, p)
        elif isl.kind == "rail":
            b, h, r, m, a = paint_rust(U, V, p, period=S / D)
        elif isl.kind == "stone":
            b, h, r, m, a = paint_stone(U, V, p)
        else:
            b, h, r, m, a = paint_rust(U, V, p)
        # cavity occlusion from the island's own height
        cav = np.clip((blur(h, 3.0, wrap_x=isl.band) - h) / 0.0012, 0.0, 1.0)
        a = a * (1.0 - 0.45 * cav)
        gx = np.gradient(h, axis=1) / texel
        gy = np.gradient(h, axis=0) / texel
        if isl.band:
            gx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * texel)
        n = np.stack([-gx, -gy, np.ones_like(h)], axis=-1)
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        sl = (slice(i0, i1), slice(j0, j1))
        base[sl] = np.clip(b * (0.75 + 0.25 * a[..., None]), 0, 1)
        height[sl] = h
        rough[sl] = r
        metal[sl] = m
        ao[sl] = a
        nrm[sl] = n
    return base, nrm, rough, metal, ao


# ============================================================================================ images
def to_u8(a):
    return (np.clip(a, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def write_png(path, rgb_u8_bottom_up):
    a = rgb_u8_bottom_up[::-1]
    h, w = a.shape[:2]
    raw = np.empty((h, w * 3 + 1), np.uint8)
    raw[:, 0] = 0
    raw[:, 1:] = a.reshape(h, w * 3)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw.tobytes(), 6)) + chunk(b"IEND", b"")
    with open(path, "wb") as fh:
        fh.write(png)


def read_png_bottom_up(path):
    """8-bit RGB/RGBA PNG as float 0..1, rows bottom-up (Blender's image loader: stored values, no transform)."""
    img = bpy.data.images.load(path, check_existing=False)
    try:
        img.colorspace_settings.name = "Non-Color"
        w, h = img.size
        px = np.empty(w * h * img.channels, np.float32)
        img.pixels.foreach_get(px)
        return px.reshape(h, w, img.channels)[..., :3].astype(np.float64)
    finally:
        bpy.data.images.remove(img)


# ============================================================================================ blender
def gltf_output_group():
    name = "glTF Material Output"
    if name in bpy.data.node_groups:
        return bpy.data.node_groups[name]
    g = bpy.data.node_groups.new(name, "ShaderNodeTree")
    g.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    g.nodes.new("NodeGroupOutput")
    g.nodes.new("NodeGroupInput")
    return g


def load_image(path, colorspace):
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = colorspace
    return img


def make_material(name, paths):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    for node in list(nt.nodes):
        nt.nodes.remove(node)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = load_image(paths["basecolor"], "sRGB")
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = load_image(paths["normal"], "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    to = nt.nodes.new("ShaderNodeTexImage")
    to.image = load_image(paths["orm"], "Non-Color")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(to.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    grp = nt.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    nt.links.new(sep.outputs["Red"], grp.inputs["Occlusion"])
    return mat


def resolve_uv(part, b, S, D):
    uvs = []
    for fi, f in enumerate(part.f):
        key = part.isl[fi]
        if key is None:
            uvs.extend(part.uv[fi])
            continue
        isl = b.islands[key]
        for (u, v) in part.uv[fi]:
            uvs.append(((isl.x0 + u * D) / S, (isl.y0 + v * D) / S))
    return uvs


def make_objects(b, S, D, mats):
    objs = []
    for part in b.parts:
        me = bpy.data.meshes.new(part.name)
        me.from_pydata(part.v, [], [list(f) for f in part.f])
        uvl = me.uv_layers.new(name="UVMap")
        flat = np.array(resolve_uv(part, b, S, D), np.float64).ravel()
        assert len(flat) == 2 * len(me.loops), (part.name, len(flat), len(me.loops))
        uvl.data.foreach_set("uv", flat)
        me.materials.append(mats[part.material])
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(part.smooth))
        me.update()
        obj = bpy.data.objects.new(part.name, me)
        bpy.context.scene.collection.objects.link(obj)
        objs.append(obj)
    return objs


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if bpy.context.scene.world is None:
        bpy.context.scene.world = bpy.data.worlds.new("World")


def glb_summary(path):
    with open(path, "rb") as fh:
        data = fh.read()
    n = struct.unpack("<I", data[12:16])[0]
    js = json.loads(data[20:20 + n])
    lo = np.array([1e9] * 3)
    hi = -lo
    tris = 0
    for m in js["meshes"]:
        for prim in m["primitives"]:
            acc = js["accessors"][prim["attributes"]["POSITION"]]
            lo = np.minimum(lo, acc["min"])
            hi = np.maximum(hi, acc["max"])
            tris += js["accessors"][prim["indices"]]["count"] // 3
    mats = []
    for mt in js.get("materials", []):
        pbr = mt.get("pbrMetallicRoughness", {})
        mats.append({"name": mt.get("name"), "baseColorTexture": "baseColorTexture" in pbr,
                     "metallicRoughnessTexture": "metallicRoughnessTexture" in pbr,
                     "normalTexture": "normalTexture" in mt, "occlusionTexture": "occlusionTexture" in mt})
    return {"bytes": len(data), "min": [round(float(x), 5) for x in lo], "max": [round(float(x), 5) for x in hi],
            "triangles": int(tris), "materials": mats, "images": len(js.get("images", [])),
            "nodes": len(js.get("nodes", [])), "meshes": len(js["meshes"])}


def rel(path):
    try:
        return os.path.relpath(path, REPO).replace("\\", "/")
    except ValueError:
        return path


def build_variant(variant, args, work):
    t0 = time.time()
    reset()
    _SCALE.clear()
    asset = ASSET_ID if variant == "straight" else END_ID
    b, counts = build_module(variant, args.seed)
    checks = [check_part(p) for p in b.parts]
    bad = [c for c in checks if c["open_or_misrouted_edges"] or c["inverted_solids"] or c["bowtie_quads"]]
    for c in checks:
        log(f"  {c}")
    if bad:
        raise SystemExit(f"{asset}: open, inverted or bow-tie geometry: {bad}")
    S, pad = args.tex, 4
    D = float(args.density)
    isl_list = [b.islands[k] for k in sorted(b.islands)]
    while True:
        used = pack(isl_list, S, D, pad)
        if used is not None:
            break
        D *= 0.98
    D = round(D, 2)
    used = pack(isl_list, S, D, pad)
    log(f"{asset}: {len(isl_list)} islands packed at {D} px/m, atlas use {used:.1%}")
    base, nrm, rough, metal, ao = paint_atlas(isl_list, S, D, pad)
    paths = {}
    for name, arr in (("basecolor", base), ("normal", nrm * 0.5 + 0.5),
                      ("orm", np.stack([ao, rough, metal], axis=-1))):
        path = os.path.join(work, f"{asset}_{name}.png")
        write_png(path, to_u8(arr))
        paths[name] = path
    gravel = ballast_maps(S, work)
    mats = {"atlas": make_material(f"M_{ASSET_ID}_atlas", paths),
            "ballast": make_material(f"M_{ASSET_ID}_ballast", gravel["paths"])}
    objs = make_objects(b, S, D, mats)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = asset
    obj.data.name = asset
    me = obj.data
    V = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", V)
    V = V.reshape(-1, 3)
    lo, hi = V.min(axis=0), V.max(axis=0)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    os.makedirs(args.out, exist_ok=True)
    glb = os.path.join(args.out, f"{asset}.glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_normals=True, export_tangents=True,
                              export_materials="EXPORT", export_texcoords=True, export_image_format="AUTO",
                              export_extras=False)
    summ = glb_summary(glb)
    with open(glb, "rb") as fh:
        sha = hashlib.sha256(fh.read()).hexdigest()
    dims = {"x_width_m": round(float(hi[0] - lo[0]), 4), "y_height_m": round(float(hi[2] - lo[2]), 4),
            "z_length_m": round(float(hi[1] - lo[1]), 4),
            "min_gltf": [round(float(lo[0]), 5), round(float(lo[2]), 5), round(float(-hi[1]), 5)],
            "max_gltf": [round(float(hi[0]), 5), round(float(hi[2]), 5), round(float(-lo[1]), 5)]}
    prov = {
        "asset_id": asset,
        "method": "procedural",
        "script": SCRIPT_REL,
        "command": f"blender --background --factory-startup --python {SCRIPT_REL} -- --variant {variant} "
                   f"--seed {args.seed} --tex {args.tex} --density {args.density}",
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.date.today().isoformat(),
        "concept": "assets/concepts/prop_quarry_rail_track.png",
        "replaces": "assets/ready/prop_quarry_rail_track (Pixal3D reconstruction, pitched 40 deg; not modified)",
        "variant": variant,
        "parameters": PARAMS,
        "counts": counts,
        "dimensions": dims,
        "modular": {
            "module_length_m": PC["module_length"], "joint_ends_gltf_z": [HALF] if variant == "end" else [-HALF, HALF],
            "sleeper_pitch_m": PC["sleeper"]["pitch"], "sleeper_inset_from_joint_m": 0.3,
            "rail_centres_x_m": [-PC["rail_centre_x"], PC["rail_centre_x"]], "rail_top_y_m": round(SEAT_Z + PC["rail"]["height"], 4),
            "ballast": "outline, height and lumps are periodic in Z with the module length; ballast UV tile 1.5 m",
            "game_fit": "Fitting.cs 'tile' repeats a piece at max(size.X, size.Z) = 3.0 m along the run",
            "end_stop": "buffer at glTF z = -0.94..-1.10 facing +Z; joint at +Z end only" if variant == "end" else None,
        },
        "triangles": tris,
        "geometry_checks": checks,
        "materials": {
            "count": 2,
            "atlas": {"name": f"M_{ASSET_ID}_atlas", "size": S, "texel_density_px_per_m": D, "atlas_use": round(used, 3),
                      "maps": "base colour sRGB, normal tangent-space OpenGL (+Y up), ORM (R cavity/contact occlusion, "
                              "G roughness, B metallic)",
                      "notes": "sleeper undersides and buried lower sides at 0.25 of the density; small identical iron "
                               "parts (plates, spikes, bolts, fishplates) share three or fewer island variants; loose "
                               "stones share four stone patches; rail sides use a band periodic in U (one repeat per "
                               f"{round(S / D, 3)} m)",
                      "source": "procedural, numpy gradient noise evaluated per island in member metres (this script)"},
            "ballast": {"name": f"M_{ASSET_ID}_ballast", "size": S, "tile_m": PC["ballast"]["tile_m"],
                        "texel_density_px_per_m": round(S / PC["ballast"]["tile_m"], 1),
                        "source": "procedural in numpy (this script): three periodic Voronoi layers of angular broken "
                                  "stone over fines, periodic by construction; the staged material_loose_gravel was "
                                  "tried first and dropped (warm rounded river gravel, wrong for the concept's grey "
                                  "crushed quarry stone)",
                        "mean_srgb": gravel["mean_srgb"]},
        },
        "glb": {"path": rel(glb), "sha256": sha, **summ},
        "build_seconds": round(time.time() - t0, 1),
    }
    prov_path = os.path.join(args.out, f"{asset}_provenance.json")
    with open(prov_path, "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    log(f"wrote {glb}: {tris} tris, dims {dims}, glb {summ}")
    return prov_path, glb, paths


# ============================================================================================ preview
def preview(args, glbs):
    reset()
    scene = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    scene.view_settings.view_transform = "Standard"
    scene.render.resolution_x = scene.render.resolution_y = 1536
    world = scene.world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.8, 0.8, 0.8, 1.0)
    bg.inputs[1].default_value = 0.8

    def load(path):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=path)
        return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]

    straight = load(glbs["straight"])[0]
    end = load(glbs["end"])[0]
    straight.location = (0, 0, 0)
    for y in (3.0, 6.0, 9.0):
        dup = straight.copy()
        dup.location = (0, y, 0)
        scene.collection.objects.link(dup)
    end.location = (0, 12.0, 0)
    ground = bpy.data.meshes.new("ground")
    ground.from_pydata([(-30, -40, -0.001), (30, -40, -0.001), (30, 40, -0.001), (-30, 40, -0.001)], [], [[0, 1, 2, 3]])
    g = bpy.data.objects.new("ground", ground)
    scene.collection.objects.link(g)
    gm = bpy.data.materials.new("ground")
    gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.5, 0.5, 0.5, 1)
    gm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    ground.materials.append(gm)
    sun = bpy.data.lights.new("sun", "SUN")
    sun.energy = 2.2
    sun.angle = math.radians(3)
    so = bpy.data.objects.new("sun", sun)
    so.rotation_euler = (math.radians(40), math.radians(-18), math.radians(-35))
    scene.collection.objects.link(so)
    cam = bpy.data.cameras.new("cam")
    co = bpy.data.objects.new("cam", cam)
    scene.collection.objects.link(co)
    scene.camera = co
    os.makedirs(os.path.join(args.out, "renders"), exist_ok=True)
    shots = {
        "concept_view": ((-2.6, -2.7, 3.4), (0.6, 3.9, 0.0), 28),
        "closeup_joint": ((0.85, 0.75, 0.62), (0.12, 1.55, 0.08), 40),
        "closeup_endstop": ((1.05, 10.2, 0.95), (0.0, 12.95, 0.2), 38),
        "overhead": ((0.0, 3.0, 7.0), (0.0, 3.0001, 0.0), 50),
    }
    out = {}
    for name, (loc, tgt, lens) in shots.items():
        co.location = loc
        d = np.array(tgt) - np.array(loc)
        from mathutils import Vector
        co.rotation_euler = Vector(d).to_track_quat("-Z", "Y").to_euler()
        cam.lens = lens
        path = os.path.join(args.out, "renders", f"{ASSET_ID}_{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out[name] = path
    # concept beside the concept-view render
    concept = read_png_bottom_up(CONCEPT)
    mine = read_png_bottom_up(out["concept_view"])
    h = min(concept.shape[0], mine.shape[0])
    side = np.concatenate([concept[:h, :h], mine[:h, :h]], axis=1)
    cmp_path = os.path.join(args.out, "renders", f"{ASSET_ID}_concept_vs_build.png")
    write_png(cmp_path, to_u8(side))
    out["compare"] = cmp_path
    log(f"preview renders: {out}")
    return out


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--variant", default="both", choices=["both", "straight", "end"])
    ap.add_argument("--out", default=OUT_DEFAULT)
    ap.add_argument("--seed", type=int, default=2609)
    ap.add_argument("--tex", type=int, default=1024)
    ap.add_argument("--density", type=float, default=512.0)
    ap.add_argument("--preview", action="store_true")
    ap.add_argument("--keep-textures", default=None, help="copy the atlas PNGs into this folder")
    args = ap.parse_args(argv)
    work = tempfile.mkdtemp(prefix="procgen_railtrack_")
    variants = ["straight", "end"] if args.variant == "both" else [args.variant]
    provs, glbs = [], {}
    for v in variants:
        prov, glb, paths = build_variant(v, args, work)
        provs.append(prov)
        glbs[v] = glb
        if args.keep_textures:
            os.makedirs(args.keep_textures, exist_ok=True)
            import shutil
            for p in paths.values():
                shutil.copy(p, args.keep_textures)
    if args.preview and len(glbs) == 2:
        preview(args, glbs)
    print("PROCGEN_RESULT " + ", ".join(provs), flush=True)


if __name__ == "__main__":
    main()
