"""Procedural build of prop_quarry_winch: a timber quarry winch (crab) from real member sizes.

Image-to-3D melted this prop (splayed members, fused rope), so it is authored here from dimensions:
two A-frame trestles (legs, sills, cross rails, diagonal braces), long sills and rails tying them,
a log drum with iron hoops and iron-rimmed flanges, a helix of hemp rope wound flange to flange with
a length running off over the front sill to an iron hook on the ground, iron bearing straps and two
iron crank handles (drum axle and pinion shaft) on the model's right side (-X), grips at ~1.0 m.

Frame: glTF, metres, +Y up, front +Z, drum axis X, centred on X/Z, lowest point y = 0.

Texturing is a trim-sheet atlas (1024 x 1024, 512 px/m, one material). The atlas is five horizontal
bands: weathered side-grain timber, end-grain tiles, laid hemp rope, turned grip wood and iron. The
side-grain, rope, grip and iron bands are periodic along U, so every member is unwrapped in its own
frame (grain along the member's length) at exactly 512 px/m with a seeded offset and the sampler's
REPEAT wrap carries long members. Iron is resampled from the seamless world material
assets/_staging/materials/material_cast_iron_surface when it is present, else generated here.
Everything is seeded, so the same seed rebuilds the same file.

Usage:
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_winch.py -- ^
      [--out assets/_staging/procedural/prop_quarry_winch] [--seed 4127] [--no-world-iron]

Writes <out>/prop_quarry_winch.glb, <out>/prop_quarry_winch_provenance.json and the atlas PNGs in
<out>/textures/. The last stdout line is "PROCGEN_RESULT <provenance path>".
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ASSET_ID = "prop_quarry_winch"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.path.join(REPO, "assets")
DEFAULT_OUT = os.path.join(ASSETS, "_staging", "procedural", ASSET_ID)
CONCEPT = os.path.join(ASSETS, "concepts", ASSET_ID + ".png")
WORLD_IRON = os.path.join(ASSETS, "_staging", "materials", "material_cast_iron_surface")

TEX = 1024            # atlas size in px (mid-tier prop)
PPM = 512.0           # texel density, px per metre
# name: (first row, rows); rows count up from the bottom of the image (Blender v = 0)
BANDS = {"wood": (0, 312), "end": (312, 96), "rope": (408, 88), "grip": (496, 88), "iron": (584, 440)}
MARGIN = 4            # px kept clear at band edges so mips do not bleed across bands
ISLAND_PAD = 2         # extra px kept clear inside an already-margined island, belt-and-braces
END_TILE = 96
END_TILES = 10
END_TILE_U0 = 32

P = {
    "axle_height": 1.07,
    "trestle_x": 0.59,                 # centre plane of each trestle's legs
    "leg": {"section": 0.14, "foot_z": 0.40, "top_z": 0.14, "top_y": 1.30},
    "trestle_sill": {"section": 0.13, "half_len": 0.62},
    "long_sill": {"width": 0.12, "height": 0.11, "z": 0.51, "half_len": 0.80},
    "long_rail": {"width": 0.11, "height": 0.13, "y": 0.62},
    "cross_rail": {"width": 0.11, "height": 0.13, "y": 0.70, "half_len": 0.46},
    "brace": {"width": 0.10, "thickness": 0.05},
    "timber_chamfer": 0.012,
    "iron_chamfer": 0.0015,
    "strap": {"thickness": 0.012, "band_thickness": 0.006, "band_width": 0.035},
    "drum": {"log_radius": 0.15, "log_half": 0.515, "flange_inner_x": 0.42, "flange_thickness": 0.02,
             "flange_radius": 0.41, "rim_inner": 0.405, "rim_outer": 0.43, "rim_half_width": 0.016,
             "hoop_width": 0.03, "hoop_thickness": 0.008},
    "rope": {"radius": 0.025, "helix_radius": 0.272, "turns": 16, "sides": 8, "segments_per_turn": 28,
             "lay_m": 2.0 / 12.0, "free_step": 0.025},
    "axle_radius": 0.03,
    "pinion": {"height": 0.83, "radius": 0.022},
    "crank_upper": {"arm": 0.28, "dir_deg": 200.0, "arm_x": 0.245, "grip_len": 0.27},
    "crank_lower": {"arm": 0.20, "dir_deg": 25.0, "arm_x": 0.18, "grip_len": 0.27},
    "grip_radius": 0.034,
}


# ----------------------------------------------------------------------------------------------
# small helpers

def g2b(v):
    """glTF frame (x, y up, z front) -> Blender frame (x, -z, y). A proper rotation: winding survives."""
    return (v[0], -v[2], v[1])


def basis_from(ex, hint):
    ex = Vector(ex).normalized()
    ey = Vector(hint) - ex * Vector(hint).dot(ex)
    if ey.length < 1e-6:
        ey = Vector((0, 1, 0)) - ex * ex.y
        if ey.length < 1e-6:
            ey = Vector((1, 0, 0)) - ex * ex.x
    ey.normalize()
    ez = ex.cross(ey)
    return ex, ey, ez


def frame_matrix(origin, ex, ey, ez):
    m = Matrix.Identity(4)
    for r in range(3):
        m[r][0], m[r][1], m[r][2], m[r][3] = ex[r], ey[r], ez[r], origin[r]
    return m


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ----------------------------------------------------------------------------------------------
# textures (numpy). Arrays are [row, col] with row 0 at the bottom (Blender pixel order), so +row is +V.

class Noise:
    """Value noise with quintic interpolation; periodic in u, optionally in v. Coords are 0..1."""

    def __init__(self, rng, cells_v, cells_u, wrap_v=False):
        self.cv, self.cu, self.wrap_v = int(cells_v), int(cells_u), wrap_v
        rows = self.cv if wrap_v else self.cv + 1
        self.lat = rng.random_sample((rows, self.cu)).astype(np.float64)

    def __call__(self, v, u):
        fu = np.mod(u, 1.0) * self.cu
        fv = (np.mod(v, 1.0) if self.wrap_v else np.clip(v, 0.0, 1.0)) * self.cv
        iu = np.floor(fu).astype(np.int64)
        iv = np.floor(fv).astype(np.int64)
        tu, tv = fu - iu, fv - iv
        iu0, iu1 = iu % self.cu, (iu + 1) % self.cu
        if self.wrap_v:
            iv0, iv1 = iv % self.cv, (iv + 1) % self.cv
        else:
            iv0, iv1 = np.clip(iv, 0, self.cv), np.clip(iv + 1, 0, self.cv)
        su = tu * tu * tu * (tu * (tu * 6 - 15) + 10)
        sv = tv * tv * tv * (tv * (tv * 6 - 15) + 10)
        a, b = self.lat[iv0, iu0], self.lat[iv0, iu1]
        c, d = self.lat[iv1, iu0], self.lat[iv1, iu1]
        top = a + (b - a) * su
        bot = c + (d - c) * su
        return top + (bot - top) * sv


def fbm(rng, v, u, cells_v, cells_u, octaves, gain=0.5, wrap_v=False):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * Noise(rng, cells_v * 2 ** o, cells_u * 2 ** o, wrap_v)(v, u)
        norm += amp
        amp *= gain
    return total / norm


def blur(a, radius, wrap_v=False):
    """Three box passes, wrapping in u (and v when asked)."""
    r = int(max(1, radius))
    out = a
    for _ in range(3):
        pad = np.pad(out, ((0, 0), (r, r)), mode="wrap")
        c = np.cumsum(np.pad(pad, ((0, 0), (1, 0))), axis=1)
        out = (c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)
        pad = np.pad(out, ((r, r), (0, 0)), mode="wrap" if wrap_v else "edge")
        c = np.cumsum(np.pad(pad, ((1, 0), (0, 0))), axis=0)
        out = (c[2 * r + 1:, :] - c[:-2 * r - 1, :]) / (2 * r + 1)
    return out


def height_to_normal(h, wrap_v=False):
    """Height in metres -> OpenGL tangent-space normal (+G = +V), encoded 0..1."""
    px = 1.0 / PPM
    hu = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / (2 * px)
    if wrap_v:
        hv = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / (2 * px)
    else:
        hp = np.pad(h, ((1, 1), (0, 0)), mode="edge")
        hv = (hp[2:] - hp[:-2]) / (2 * px)
    n = np.stack([-hu, -hv, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def cavity_ao(h, radius_px, depth_m, strength, wrap_v=False):
    cav = np.clip((blur(h, radius_px, wrap_v) - h) / depth_m, 0.0, 1.0)
    return np.clip(1.0 - strength * cav, 0.25, 1.0)


def band_grid(rows, cols=TEX):
    r = np.arange(rows, dtype=np.float64)[:, None] + 0.5
    c = np.arange(cols, dtype=np.float64)[None, :] + 0.5
    return np.broadcast_to(r, (rows, cols)), np.broadcast_to(c, (rows, cols))


def lerp3(col, target, t):
    t = np.asarray(t)[..., None]
    return col * (1.0 - t) + np.asarray(target) * t


def tex_wood(rng):
    """Weathered, silvered side-grain timber; grain along U; periodic in U."""
    H = BANDS["wood"][1]
    rr, cc = band_grid(H)
    v, u = rr / H, cc / TEX
    # knots first: they deflect the grain around them
    knot_core = np.zeros((H, TEX))
    knot_ring = np.zeros((H, TEX))
    deflect = np.zeros((H, TEX))
    for _ in range(6):
        uk, vk = rng.uniform(0, TEX), rng.uniform(20, H - 20)
        rk = rng.uniform(3.5, 7.0)
        du = np.mod(cc - uk + TEX / 2, TEX) - TEX / 2
        dv = rr - vk
        d = np.sqrt((du / (rk * 1.8)) ** 2 + (dv / rk) ** 2)
        knot_core = np.maximum(knot_core, smoothstep(1.0, 0.55, d))
        knot_ring = np.maximum(knot_ring, np.exp(-((d - 1.15) / 0.22) ** 2))
        deflect += rk * 1.4 * np.exp(-(d ** 2) / 3.0) * np.sign(dv)
    warp = (fbm(rng, v, u, 3, 5, 3) - 0.5) * 4.0 + (fbm(rng, v, u, 8, 17, 2) - 0.5) * 1.5 + 0.45 * deflect
    vw = (rr + warp) / H
    g1 = fbm(rng, vw, u, 110, 3, 2)
    g0 = fbm(rng, vw, u, 200, 5, 1)
    g2 = fbm(rng, vw, u, 28, 2, 3)
    g3 = fbm(rng, v, u, 3, 6, 3)
    ridge = (1.0 - np.abs(2.0 * g1 - 1.0)) ** 4
    fleck = smoothstep(0.66, 0.8, fbm(rng, vw, u, 70, 90, 2)) * smoothstep(0.45, 0.7, g2)
    # checks (drying cracks) running with the grain
    crack = np.zeros((H, TEX))
    for _ in range(26):
        v0, u0 = rng.uniform(8, H - 8), rng.uniform(0, TEX)
        length = rng.uniform(30, 280)
        width = rng.uniform(0.55, 1.3)
        amp, lam, ph = rng.uniform(1.0, 4.0), rng.uniform(60, 220), rng.uniform(0, 2 * math.pi)
        depth = rng.uniform(0.5, 1.0)
        n = int(length) + 1
        uu = np.arange(n, dtype=np.float64)
        cols = (np.arange(n) + int(u0)) % TEX
        t = uu / length
        taper = np.sin(np.pi * np.clip(t, 0, 1)) ** 0.6
        vc = v0 + amp * np.sin(2 * math.pi * uu / lam + ph)
        r0, r1 = int(max(0, v0 - amp - 6)), int(min(H, v0 + amp + 7))
        rows = np.arange(r0, r1, dtype=np.float64)[:, None] + 0.5
        m = np.exp(-((rows - vc[None, :]) / (width * (0.4 + taper[None, :]))) ** 2) * taper[None, :] * depth
        crack[r0:r1, cols] = np.maximum(crack[r0:r1, cols], m)
    rust = smoothstep(0.7, 0.8, fbm(rng, v, u, 8, 30, 3)) * smoothstep(0.6, 0.75, fbm(rng, v, u, 3, 9, 2))
    brown = smoothstep(0.58, 0.85, fbm(rng, v, u, 4, 7, 3))
    grime = fbm(rng, v, u, 10, 30, 3)

    # grain terms carry extra amplitude (1.8x) so the normal map reads as brushed/weathered timber,
    # not a smooth brushed-metal sheen, under direct sun
    grain_strength = 1.8
    height = (grain_strength * (0.0005 * (g1 - 0.5) + 0.0003 * (g0 - 0.5) + 0.0003 * (g2 - 0.5) + 0.0005 * (g3 - 0.5)
              + 0.00035 * ridge + 0.0002 * fleck) - 0.0035 * crack - 0.0006 * knot_core + 0.0002 * knot_ring)
    tone = np.clip(0.5 + 0.8 * (g1 - 0.5) + 0.25 * (g0 - 0.5) + 0.8 * (g2 - 0.5) + 0.4 * (g3 - 0.5), 0.0, 1.0)
    col = lerp3(np.full((H, TEX, 3), [0.13, 0.125, 0.118]), [0.62, 0.615, 0.59], tone ** 1.05)
    col = col + 0.12 * ridge[..., None]
    col = lerp3(col, [0.74, 0.74, 0.72], 0.5 * fleck)
    col = lerp3(col, [0.33, 0.265, 0.2], 0.35 * brown)
    col = col * (0.9 + 0.15 * grime[..., None])
    col = lerp3(col, [0.10, 0.085, 0.07], 0.8 * knot_core)
    col = lerp3(col, [0.2, 0.18, 0.16], 0.2 * knot_ring)
    col = lerp3(col, [0.05, 0.045, 0.04], 0.9 * crack)
    col = lerp3(col, [0.3, 0.19, 0.11], 0.3 * rust)
    # raised floor/baseline: weathered timber never goes as glossy as the old 0.5 floor allowed,
    # which read as a brushed-metal sheen in direct sun
    rough = np.clip(0.78 + 0.14 * (1.0 - tone) + 0.16 * crack + 0.05 * rust - 0.04 * fleck, 0.7, 0.98)
    ao = cavity_ao(height, 4, 0.0015, 0.55)
    return np.clip(col, 0, 1), height_to_normal(height), rough, ao


def tex_end(rng):
    """End-grain tiles: growth rings around an off-centre pith, radial checks."""
    H = BANDS["end"][1]
    col = np.zeros((H, TEX, 3))
    height = np.zeros((H, TEX))
    rough = np.full((H, TEX), 0.9)
    rr, cc = band_grid(H)
    base_noise = fbm(rng, rr / H, cc / TEX, 4, 40, 3)
    col[:] = lerp3(np.full((H, TEX, 3), [0.2, 0.18, 0.16]), [0.34, 0.31, 0.27], base_noise)
    for k in range(END_TILES):
        c0 = END_TILE_U0 + k * END_TILE
        sl = np.s_[:, c0:c0 + END_TILE]
        y = rr[sl] - END_TILE / 2
        x = cc[sl] - c0 - END_TILE / 2
        px, py = rng.uniform(-75, 75), rng.uniform(-75, 75)
        dx, dy = x - px, y - py
        r_m = np.sqrt(dx * dx + dy * dy) / PPM
        ang = np.arctan2(dy, dx)
        # ring spacing kept at >= 4 px (8-12 mm): real 3-6 mm rings alias into moire at 512 px/m
        spacing = rng.uniform(0.008, 0.012)
        wob = Noise(rng, 6, 12, wrap_v=True)((ang / (2 * np.pi)) % 1.0, r_m * 4.0)
        phase = r_m / spacing + 1.2 * wob
        ring = np.exp(-(((phase - np.floor(phase)) - 0.5) / 0.16) ** 2)
        checks = np.zeros_like(r_m)
        for _ in range(int(rng.randint(2, 5))):
            a0 = rng.uniform(-np.pi, np.pi)
            da = np.abs(np.mod(ang - a0 + np.pi, 2 * np.pi) - np.pi) * np.maximum(r_m, 1e-4) * PPM
            reach = rng.uniform(0.03, 0.09)
            checks = np.maximum(checks, np.exp(-(da / 0.9) ** 2) * smoothstep(reach, reach * 0.4, r_m))
        tone = rng.uniform(0.85, 1.1) * (0.85 + 0.3 * Noise(rng, 8, 60)(rr[sl] / H, cc[sl] / TEX))
        tcol = np.full(r_m.shape + (3,), [0.31, 0.28, 0.245]) * tone[..., None]
        tcol = lerp3(tcol, [0.17, 0.15, 0.125], 0.75 * ring)
        tcol = lerp3(tcol, [0.07, 0.06, 0.05], 0.9 * checks)
        col[sl] = tcol
        height[sl] = -0.0003 * ring - 0.003 * checks
        rough[sl] = 0.86 + 0.1 * checks
    ao = cavity_ao(height, 3, 0.0015, 0.5)
    return np.clip(col, 0, 1), height_to_normal(height), rough, ao


def tex_rope(rng):
    """Three-strand laid hemp. U along the rope (lay divides the 2 m period), V around it (80 px)."""
    H = BANDS["rope"][1]
    circ_px = 80.0
    rr, cc = band_grid(H)
    c = (rr - MARGIN) / circ_px                  # fraction of the circumference, periodic
    um = cc / PPM
    lay = P["rope"]["lay_m"]
    t = 3.0 * (c + um / lay)
    f = t - np.floor(t)
    prof = np.sqrt(np.clip(1.0 - (2.0 * f - 1.0) ** 2, 0.0, 1.0))
    yarn = np.sin(2 * np.pi * 12.0 * (3.0 * c - 0.75 * um / lay))
    cw = np.mod(c, 1.0)
    fuzz = Noise(rng, 20, 420, wrap_v=True)(cw, cc / TEX)
    dirt = fbm(rng, cw, cc / TEX, 2, 26, 3, wrap_v=True)
    stain = smoothstep(0.6, 0.8, fbm(rng, cw, cc / TEX, 2, 9, 2, wrap_v=True))
    hair = smoothstep(0.82, 0.95, Noise(rng, 40, 700, wrap_v=True)(cw, cc / TEX)) * prof
    height = 0.0042 * prof ** 0.7 + 0.00035 * yarn * prof + 0.0003 * (fuzz - 0.5)
    base = np.array([0.41, 0.37, 0.28])
    col = base * (0.25 + 0.75 * prof[..., None] ** 0.8) + 0.04 * (yarn * prof)[..., None] + 0.06 * (fuzz - 0.5)[..., None]
    col = col * (0.84 + 0.3 * dirt[..., None])
    col = lerp3(col, [0.64, 0.61, 0.5], 0.5 * hair)
    col = lerp3(col, [0.22, 0.2, 0.15], 0.55 * stain)
    rough = np.clip(0.86 + 0.08 * (1 - prof) + 0.03 * fuzz, 0.7, 0.98)
    ao = np.clip(0.5 + 0.5 * prof, 0.3, 1.0)
    return np.clip(col, 0, 1), height_to_normal(height), rough, ao


def tex_grip(rng):
    """Turned, hand-polished grip wood; grain along U, V periodic around the grip."""
    H = BANDS["grip"][1]
    circ_px = 2 * math.pi * P["grip_radius"] * PPM
    rr, cc = band_grid(H)
    cw = np.mod((rr - MARGIN) / circ_px, 1.0)
    u = cc / TEX
    g1 = fbm(rng, cw, u, 36, 3, 2, wrap_v=True)
    g2 = fbm(rng, cw, u, 8, 6, 3, wrap_v=True)
    grime = fbm(rng, cw, u, 3, 30, 3, wrap_v=True)
    streak = (1.0 - np.abs(2.0 * g1 - 1.0)) ** 5
    tone = np.clip(0.5 + 1.1 * (g1 - 0.5) + 0.9 * (g2 - 0.5), 0, 1)
    col = lerp3(np.full((H, TEX, 3), [0.25, 0.15, 0.09]), [0.56, 0.39, 0.25], tone)
    col = lerp3(col, [0.16, 0.1, 0.06], 0.6 * streak)
    col = lerp3(col, [0.17, 0.12, 0.08], 0.6 * smoothstep(0.5, 0.8, grime))
    height = 0.0002 * (g1 - 0.5) + 0.0002 * (g2 - 0.5)
    rough = np.clip(0.48 + 0.15 * grime, 0.35, 0.75)
    ao = np.ones((H, TEX))
    return np.clip(col, 0, 1), height_to_normal(height, wrap_v=False), rough, ao


def load_image_array(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)[..., :3].astype(np.float64)


def srgb_to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def tex_iron(rng, use_world):
    """Iron band. The world cast-iron material (1 m tile at 2048 px) resampled to 512 px/m and tiled
    twice across the 2 m band when it exists; a procedural fallback otherwise."""
    H = BANDS["iron"][1]
    files = {k: os.path.join(WORLD_IRON, f"material_cast_iron_surface_{k}.png") for k in ("basecolor", "normal", "orm")}
    if use_world and all(os.path.isfile(p) for p in files.values()):
        base = load_image_array(files["basecolor"])
        nrm = load_image_array(files["normal"])
        orm = load_image_array(files["orm"])
        src = base.shape[0]
        f = int(round(src / PPM))              # 1 m tile -> 512 px
        def down(a):
            s = a.shape[0] // f
            return a[: s * f, : s * f].reshape(s, f, s, f, -1).mean(axis=(1, 3))
        base_t = linear_to_srgb(down(srgb_to_linear(base)))
        n = nrm * 2.0 - 1.0
        n_t = down(n)
        n_t /= np.linalg.norm(n_t, axis=-1, keepdims=True)
        orm_t = down(orm)
        reps = TEX // base_t.shape[1]
        tile = lambda a: np.tile(a, (1, reps, 1))[:H]
        col = tile(base_t)
        normal = tile(n_t * 0.5 + 0.5)
        ormb = tile(orm_t)
        return np.clip(col, 0, 1), normal, np.clip(ormb[..., 1], 0.05, 1), np.clip(ormb[..., 0], 0, 1), ormb[..., 2], "world:material_cast_iron_surface"
    rr, cc = band_grid(H)
    v, u = rr / H, cc / TEX
    n1 = fbm(rng, v, u, 12, 28, 4)
    rust = smoothstep(0.62, 0.75, fbm(rng, v, u, 8, 20, 3))
    pits = smoothstep(0.78, 0.9, Noise(rng, 110, 256)(v, u))
    height = 0.0004 * (n1 - 0.5) - 0.0006 * pits + 0.0003 * rust
    col = lerp3(np.full((H, TEX, 3), [0.17, 0.165, 0.165]), [0.30, 0.29, 0.28], n1)
    col = lerp3(col, [0.33, 0.17, 0.08], 0.8 * rust)
    rough = np.clip(0.55 + 0.25 * rust + 0.1 * n1, 0.3, 0.95)
    metal = np.clip(0.85 - 0.8 * rust, 0, 1)
    ao = cavity_ao(height, 3, 0.0008, 0.5)
    return np.clip(col, 0, 1), height_to_normal(height), rough, ao, metal, "procedural"


def build_atlas(seed, use_world_iron, tex_dir):
    rng = np.random.RandomState(seed + 101)
    base = np.zeros((TEX, TEX, 3))
    normal = np.zeros((TEX, TEX, 3))
    orm = np.zeros((TEX, TEX, 3))
    for name, fn in (("wood", tex_wood), ("end", tex_end), ("rope", tex_rope), ("grip", tex_grip)):
        r0, rows = BANDS[name]
        c, n, rgh, ao = fn(rng)
        base[r0:r0 + rows], normal[r0:r0 + rows] = c, n
        orm[r0:r0 + rows] = np.stack([ao, rgh, np.zeros_like(rgh)], axis=-1)
    r0, rows = BANDS["iron"]
    c, n, rgh, ao, metal, iron_source = tex_iron(rng, use_world_iron)
    base[r0:r0 + rows], normal[r0:r0 + rows] = c, n
    orm[r0:r0 + rows] = np.stack([ao, rgh, metal], axis=-1)

    os.makedirs(tex_dir, exist_ok=True)
    images = {}
    for key, arr, space in (("basecolor", base, "sRGB"), ("normal", normal, "Non-Color"), ("orm", orm, "Non-Color")):
        name = f"{ASSET_ID}_{key}"
        img = bpy.data.images.new(name, TEX, TEX, alpha=False, float_buffer=False)
        img.colorspace_settings.name = space
        rgba = np.ones((TEX, TEX, 4), dtype=np.float32)
        rgba[..., :3] = np.round(np.clip(arr, 0, 1) * 255.0) / 255.0
        img.pixels.foreach_set(rgba.ravel())
        path = os.path.join(tex_dir, name + ".png")
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        images[key] = (img, path)
    return images, iron_source


# ----------------------------------------------------------------------------------------------
# geometry primitives (local frames) -> bmesh

def reindex(bm):
    bm.verts.index_update()
    bm.faces.index_update()
    bm.edges.index_update()
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    return bm


def bm_from(verts, faces):
    bm = bmesh.new()
    bv = [bm.verts.new(v) for v in verts]
    for f in faces:
        bm.faces.new([bv[i] for i in f])
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return reindex(bm)


def bevel(bm, offset):
    if offset > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=offset, offset_type="OFFSET", segments=1,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return reindex(bm)


def hexa(corners):
    """Closed hexahedron; corners 0-3 around one end, 4-7 the matching corners at the other."""
    faces = [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]
    return bm_from(corners, faces)


def box_local(sx, sy, sz, centre=(0, 0, 0)):
    cx, cy, cz = centre
    xs, ys, zs = (cx - sx / 2, cx + sx / 2), (cy - sy / 2, cy + sy / 2), (cz - sz / 2, cz + sz / 2)
    corners = [(xs[0], ys[0], zs[0]), (xs[0], ys[1], zs[0]), (xs[0], ys[1], zs[1]), (xs[0], ys[0], zs[1]),
               (xs[1], ys[0], zs[0]), (xs[1], ys[1], zs[0]), (xs[1], ys[1], zs[1]), (xs[1], ys[0], zs[1])]
    return hexa(corners)


def lathe(profile, sides, closed=False, theta0=0.0):
    """Solid of revolution about local +X. profile = [(x, r)], r == 0 only at the ends (poles).
    closed=True joins the last profile point back to the first (a ring / annulus)."""
    verts, rings = [], []
    for x, r in profile:
        if r <= 1e-9:
            rings.append([len(verts)])
            verts.append((x, 0.0, 0.0))
        else:
            ring = []
            for j in range(sides):
                th = theta0 + 2 * math.pi * j / sides
                ring.append(len(verts))
                verts.append((x, r * math.cos(th), r * math.sin(th)))
            rings.append(ring)
    pairs = list(zip(rings[:-1], rings[1:]))
    if closed:
        pairs.append((rings[-1], rings[0]))
    faces = []
    for a, b in pairs:
        if len(a) == 1:
            faces += [(a[0], b[(j + 1) % sides], b[j]) for j in range(sides)]
        elif len(b) == 1:
            faces += [(a[j], a[(j + 1) % sides], b[0]) for j in range(sides)]
        else:
            faces += [(a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]) for j in range(sides)]
    return bm_from(verts, faces)


def rect_ring(length, w, h, t):
    """Iron band round a w x h member: a closed rectangular ring along local X (0..length)."""
    verts = []
    for x in (0.0, length):
        for a, b in ((w / 2 + t, h / 2 + t), (w / 2 - 0.001, h / 2 - 0.001)):
            verts += [(x, -a, -b), (x, a, -b), (x, a, b), (x, -a, b)]
    # index: end e (0/1) * 8 + outer(0)/inner(4) + corner
    def i(e, inner, k):
        return e * 8 + inner * 4 + (k % 4)
    faces = []
    for k in range(4):
        faces.append((i(0, 0, k), i(0, 0, k + 1), i(1, 0, k + 1), i(1, 0, k)))      # outer
        faces.append((i(0, 1, k + 1), i(0, 1, k), i(1, 1, k), i(1, 1, k + 1)))      # inner
        faces.append((i(0, 0, k + 1), i(0, 0, k), i(0, 1, k), i(0, 1, k + 1)))      # end 0
        faces.append((i(1, 0, k), i(1, 0, k + 1), i(1, 1, k + 1), i(1, 1, k)))      # end 1
    return bm_from(verts, faces)


def parallel_frames(points):
    tangents = []
    for i in range(len(points)):
        a = points[max(0, i - 1)]
        b = points[min(len(points) - 1, i + 1)]
        tangents.append((b - a).normalized())
    t0 = tangents[0]
    ref = Vector((0, 1, 0)) if abs(t0.y) < 0.9 else Vector((1, 0, 0))
    n = (ref - t0 * ref.dot(t0)).normalized()
    normals = [n]
    for i in range(1, len(points)):
        t_prev, t = tangents[i - 1], tangents[i]
        axis = t_prev.cross(t)
        if axis.length > 1e-9:
            ang = t_prev.angle(t)
            n = (Matrix.Rotation(ang, 3, axis.normalized()) @ n)
        n = (n - t * n.dot(t)).normalized()
        normals.append(n)
    return tangents, normals


def sweep(points, radii, sides):
    """Closed tube along a polyline (world coords given directly), end caps as fans.
    Returns bm plus per-vertex (s along, j around) for UVs."""
    tangents, normals = parallel_frames(points)
    verts, info = [], []
    s = 0.0
    for i, p in enumerate(points):
        if i:
            s += (p - points[i - 1]).length
        t, n = tangents[i], normals[i]
        b = t.cross(n)
        for j in range(sides):
            th = 2 * math.pi * j / sides
            verts.append(tuple(p + (n * math.cos(th) + b * math.sin(th)) * radii[i]))
            info.append((s, j))
    rings = len(points)
    faces = []
    for i in range(rings - 1):
        for j in range(sides):
            a, b_ = i * sides + j, i * sides + (j + 1) % sides
            faces.append((a, b_, b_ + sides, a + sides))
    c0 = len(verts)
    verts.append(tuple(points[0]))
    info.append((0.0, -1))
    c1 = len(verts)
    verts.append(tuple(points[-1]))
    info.append((s, -2))
    faces += [(c0, (j + 1) % sides, j) for j in range(sides)]
    last = (rings - 1) * sides
    faces += [(c1, last + j, last + (j + 1) % sides) for j in range(sides)]
    bm = bm_from(verts, faces)
    return bm, info, s


# ----------------------------------------------------------------------------------------------
# UV mappers (atlas coordinates). U runs 1/TEX per px and wraps (REPEAT); V is placed inside a band.

def band_v(band, px):
    return (BANDS[band][0] + px) / TEX


def section_outline(a, b, c):
    """Chamfered 2a x 2b section (chamfer c), CCW from the middle of the chamfer at corner (a, -b).
    Returns points, cumulative arc lengths and the arc position of each corner's chamfer middle."""
    pts = [(a - c / 2, -b + c / 2), (a, -b + c), (a, b - c), (a - c, b), (-a + c, b), (-a, b - c),
           (-a, -b + c), (-a + c, -b), (a - c, -b), (a - c / 2, -b + c / 2)]
    cum = [0.0]
    for p, q in zip(pts[:-1], pts[1:]):
        cum.append(cum[-1] + math.hypot(q[0] - p[0], q[1] - p[1]))
    corners = [0.0] + [(cum[i] + cum[i + 1]) / 2 for i in (2, 4, 6)]
    return pts, cum, corners


def outline_s(y, z, pts, cum):
    """Arc position of the closest point on the outline (side-face vertices lie on it exactly)."""
    best_d, best_s = None, 0.0
    for i in range(len(pts) - 1):
        (py, pz), (qy, qz) = pts[i], pts[i + 1]
        dy, dz = qy - py, qz - pz
        l2 = dy * dy + dz * dz
        t = min(1.0, max(0.0, ((y - py) * dy + (z - pz) * dz) / l2))
        ey, ez = py + t * dy - y, pz + t * dz - z
        d = ey * ey + ez * ez
        if best_d is None or d < best_d:
            best_d, best_s = d, cum[i] + t * math.sqrt(l2)
    return best_s


def plane_axes(n, hint):
    """In-plane axes of a face: hint projected into the plane, then n x that."""
    a1 = hint - n * n.dot(hint)
    if a1.length < 1e-6:
        a1 = n.orthogonal()
    a1.normalize()
    return a1, n.cross(a1)


class Mapper:
    def __init__(self, seed):
        self.rng = np.random.RandomState(seed + 202)

    def wood_member(self, bm, w, h, seam_corner, chamfer):
        """Side faces: one strip unrolled round the chamfered section, U along the member.
        End faces (and end chamfers): each unfolded in its own plane onto an end-grain tile."""
        a, b = w / 2, h / 2
        pts, cum, corners = section_outline(a, b, chamfer)
        per = cum[-1]
        seam_s = corners[seam_corner]
        rows = BANDS["wood"][1] - 2 * MARGIN
        span = per * PPM
        v_off = self.rng.uniform(0, max(0.0, rows - span))
        u_off = self.rng.uniform(0, TEX)
        su = -1.0 if self.rng.random_sample() < 0.5 else 1.0
        flip_v = self.rng.random_sample() < 0.5
        tiles = [int(self.rng.randint(0, END_TILES)), int(self.rng.randint(0, END_TILES))]
        out = {}
        for f in bm.faces:
            n = f.normal
            if abs(n.x) >= 0.5:
                k = tiles[0 if n.x < 0 else 1]
                cu = END_TILE_U0 + k * END_TILE + END_TILE / 2
                a1, a2 = plane_axes(n, Vector((0.0, 1.0 if n.x > 0 else -1.0, 0.0)))
                xc = sum(l.vert.co.x for l in f.loops) / len(f.loops)
                ctr = Vector((xc, 0.0, 0.0))
                out[f.index] = [((cu + (l.vert.co - ctr).dot(a1) * PPM) / TEX,
                                 band_v("end", END_TILE / 2 + (l.vert.co - ctr).dot(a2) * PPM)) for l in f.loops]
                continue
            ss = [(outline_s(l.vert.co.y, l.vert.co.z, pts, cum) - seam_s) % per for l in f.loops]
            if max(ss) - min(ss) > per / 2:
                ss = [x + per if x < per / 2 else x for x in ss]
            uvs = []
            for l, sv in zip(f.loops, ss):
                sv = per - sv if flip_v else sv
                uvs.append(((u_off + su * l.vert.co.x * PPM) / TEX, band_v("wood", MARGIN + v_off + sv * PPM)))
            out[f.index] = uvs
        return out

    def _cyl(self, f, band, u_off, v_off, islands, arc_along_u, circ_px=None):
        """Cylindrical unwrap about local X for one face. circ_px: map the angle (not the arc)
        onto that many px, for bands whose pattern is periodic round a fixed circumference."""
        cos_ = [(l.vert.co.x, math.atan2(l.vert.co.z, l.vert.co.y), math.hypot(l.vert.co.y, l.vert.co.z)) for l in f.loops]
        ths = [c[1] for c, l in zip(cos_, f.loops) if c[2] > 1e-7]
        if not ths:
            ths = [0.0]
        if islands == 1:
            if max(ths) - min(ths) > math.pi:
                ths_fix = lambda t: t + 2 * math.pi if t < 0 else t
            else:
                ths_fix = lambda t: t
            mean_th = sum(ths_fix(t) for t in ths) / len(ths)
            angs = [ths_fix(c[1]) if c[2] > 1e-7 else mean_th for c in cos_]
            angs = [x + math.pi for x in angs]
        else:
            width = 2 * math.pi / islands
            # unwrap this face's own raw angles first (as the single-island path does), so a face
            # that straddles the atan2 +-pi seam keeps one contiguous angle run instead of half its
            # verts jumping to the far side of the island and squeezing the whole band into one facet
            if max(ths) - min(ths) > math.pi:
                ths_fix = lambda t: t + 2 * math.pi if t < 0 else t
            else:
                ths_fix = lambda t: t
            mean_th = sum(ths_fix(t) for t in ths) / len(ths)
            centre = mean_th % (2 * math.pi)
            isl = int(centre // width) % islands
            start = isl * width
            raw = [ths_fix(c[1]) if c[2] > 1e-7 else mean_th for c in cos_]
            rel = mean_th - start
            shift = 0.0
            while rel + shift > math.pi:
                shift -= 2 * math.pi
            while rel + shift < -math.pi:
                shift += 2 * math.pi
            angs = [t - start + shift for t in raw]
        uvs = []
        for c, ang in zip(cos_, angs):
            arc = ang * max(c[2], 1e-7)
            if circ_px is not None:
                uvs.append(((u_off + c[0] * PPM) / TEX, band_v(band, MARGIN + v_off + ang / (2 * math.pi) * circ_px)))
            elif arc_along_u:
                uvs.append(((u_off + arc * PPM) / TEX, band_v(band, MARGIN + v_off + (c[0]) * PPM)))
            else:
                uvs.append(((u_off + c[0] * PPM) / TEX, band_v(band, MARGIN + v_off + arc * PPM)))
        return uvs

    def lathe_part(self, bm, band, r_max, islands=1, cap="planar", circ_px=None):
        """UVs for a solid of revolution about local X."""
        rows = BANDS[band][1] - 2 * MARGIN
        circ = 2 * math.pi * r_max
        arc_along_u = (circ / islands) * PPM > rows and circ_px is None
        u_off = self.rng.uniform(0, TEX)
        if circ_px is not None:
            v_off = 0.0
        elif arc_along_u:
            xs = [v.co.x for v in bm.verts]
            span = (max(xs) - min(xs)) * PPM + 2 * ISLAND_PAD
            v_off = self.rng.uniform(0, max(0.0, rows - span)) - min(xs) * PPM + ISLAND_PAD
        else:
            span = (circ / islands) * PPM + 2 * ISLAND_PAD
            v_off = self.rng.uniform(0, max(0.0, rows - span)) + ISLAND_PAD
        tile = int(self.rng.randint(0, END_TILES))
        cap_scale = 1.0
        if cap == "end" and 2 * r_max * PPM > END_TILE - 8:
            cap_scale = (END_TILE - 8) / (2 * r_max * PPM)   # log ends only; they face the legs
        # planar cap: scale the disc (not clamp its edge) so it fits inside the band with padding on
        # both sides; clamping collapsed distinct verts to the same row and produced zero-area faces
        planar_avail = rows - 2 * ISLAND_PAD
        planar_scale = 1.0
        if cap != "end" and 2 * r_max * PPM > planar_avail > 0:
            planar_scale = planar_avail / (2 * r_max * PPM)
        cap_r_px = r_max * PPM * planar_scale
        cap_v = self.rng.uniform(cap_r_px, max(cap_r_px, rows - 2 * ISLAND_PAD - cap_r_px)) + ISLAND_PAD
        out = {}
        cap_limit = 0.9 if cap == "end" else 0.7     # log end chamfers stay on the side strip
        for f in bm.faces:
            if abs(f.normal.x) > cap_limit:
                if cap == "end":
                    cu = END_TILE_U0 + tile * END_TILE + END_TILE / 2
                    out[f.index] = [((cu + l.vert.co.y * PPM * cap_scale) / TEX,
                                     band_v("end", END_TILE / 2 + l.vert.co.z * PPM * cap_scale)) for l in f.loops]
                else:
                    out[f.index] = [((u_off + l.vert.co.y * PPM * planar_scale) / TEX,
                                     band_v(band, MARGIN + cap_v + l.vert.co.z * PPM * planar_scale))
                                    for l in f.loops]
                continue
            out[f.index] = self._cyl(f, band, u_off, v_off, islands, arc_along_u, circ_px)
        return out

    def planar_part(self, bm, band):
        """Box projection in the part frame; U takes the longer in-plane axis of the part."""
        rows = BANDS[band][1] - 2 * MARGIN
        co = np.array([tuple(v.co) for v in bm.verts])
        lo, hi = co.min(axis=0), co.max(axis=0)
        ext = hi - lo
        u_off = self.rng.uniform(0, TEX)
        v_offs = {}
        out = {}
        for f in bm.faces:
            k = int(np.argmax(np.abs(np.array(f.normal))))
            i, j = [a for a in range(3) if a != k]
            if ext[j] > ext[i]:
                i, j = j, i
            if abs(f.normal[k]) < 0.999:
                # chamfer or slanted face: unfold it in its own plane, as its own island
                e = Vector((0.0, 0.0, 0.0))
                e[i] = 1.0
                a1, a2 = plane_axes(f.normal, e)
                c1 = [l.vert.co.dot(a1) for l in f.loops]
                c2 = [l.vert.co.dot(a2) for l in f.loops]
                vo = self.rng.uniform(0, max(0.0, rows - (max(c2) - min(c2)) * PPM))
                out[f.index] = [((u_off + (x1 - min(c1)) * PPM) / TEX, band_v(band, MARGIN + vo + (x2 - min(c2)) * PPM))
                                for x1, x2 in zip(c1, c2)]
                continue
            if (k, i, j) not in v_offs:
                v_offs[(k, i, j)] = self.rng.uniform(0, max(0.0, rows - ext[j] * PPM))
            vo = v_offs[(k, i, j)]
            sgn = 1.0 if f.normal[k] >= 0 else -1.0
            out[f.index] = [((u_off + sgn * (l.vert.co[i] - lo[i]) * PPM) / TEX,
                             band_v(band, MARGIN + vo + (l.vert.co[j] - lo[j]) * PPM)) for l in f.loops]
        return out

    def tube(self, bm, info, band, circ_px):
        """Swept tube: U = distance along it, V = angle round it over circ_px (periodic)."""
        sides = max(j for _, j in info) + 1
        u_off = self.rng.uniform(0, TEX)
        rows = BANDS[band][1] - 2 * MARGIN
        v0 = MARGIN + max(0.0, (rows - circ_px) / 2)
        cap_c = MARGIN + rows / 2
        out = {}
        for f in bm.faces:
            idx = [l.vert.index for l in f.loops]
            if any(info[i][1] < 0 for i in idx):
                centre = bm.verts[next(i for i in idx if info[i][1] < 0)].co
                n = f.normal
                ax1 = n.orthogonal().normalized()
                ax2 = n.cross(ax1)
                su = info[idx[0]][0]
                out[f.index] = [((u_off + su * PPM + (l.vert.co - centre).dot(ax1) * PPM) / TEX,
                                 band_v(band, cap_c + (l.vert.co - centre).dot(ax2) * PPM)) for l in f.loops]
                continue
            js = [info[i][1] for i in idx]
            if 0 in js and (sides - 1) in js:
                js = [sides if j == 0 else j for j in js]
            out[f.index] = [((u_off + info[i][0] * PPM) / TEX, band_v(band, v0 + j / sides * circ_px))
                            for i, j in zip(idx, js)]
        return out


# ----------------------------------------------------------------------------------------------
# assembly

class Assembly:
    def __init__(self, seed):
        self.verts, self.faces, self.uvs, self.face_part = [], [], [], []
        self.parts = []
        self.map = Mapper(seed)
        self.rng = np.random.RandomState(seed + 303)

    def add(self, name, kind, bm, matrix, uv):
        # closed-solid and outward-winding check in the local frame
        open_edges = sum(1 for e in bm.edges if len(e.link_faces) != 2)
        vol = 0.0
        for f in bm.faces:
            co = [l.vert.co for l in f.loops]
            for k in range(1, len(co) - 1):
                vol += co[0].dot(co[k].cross(co[k + 1])) / 6.0
        base = len(self.verts)
        for v in bm.verts:
            self.verts.append((matrix @ v.co.to_4d()).to_3d())
        pidx = len(self.parts)
        tris = 0
        for f in bm.faces:
            self.faces.append(tuple(base + l.vert.index for l in f.loops))
            self.uvs.append(uv[f.index])
            self.face_part.append(pidx)
            tris += len(f.loops) - 2
        det = matrix.to_3x3().determinant()
        self.parts.append({"name": name, "kind": kind, "tris": tris, "open_edges": open_edges,
                           "signed_volume_m3": round(vol * det, 9)})
        bm.free()

    # ---- timber
    def timber(self, name, p0, p1, w, h, wdir, cut0=None, cut1=None, chamfer=None):
        p0, p1 = Vector(p0), Vector(p1)
        ex, ey, ez = basis_from(p1 - p0, wdir)
        L = (p1 - p0).length
        corners_yz = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
        corners = []
        for end, cut in ((0, cut0), (1, cut1)):
            for y, z in corners_yz:
                x = 0.0 if end == 0 else L
                if cut is not None:
                    pt, nrm = Vector(cut[0]), Vector(cut[1])
                    o = p0 + ey * y + ez * z
                    x = (pt - o).dot(nrm) / ex.dot(nrm)
                corners.append((x, y, z))
        ch = P["timber_chamfer"] if chamfer is None else chamfer
        bm = bevel(hexa(corners), ch)
        # hide the strip seam on the corner facing down/back
        best, seam = None, 0
        for k, (y, z) in enumerate(((w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2), (-w / 2, -h / 2))):
            d = (ey * y + ez * z).normalized()
            score = d.dot(Vector((0.0, 0.6, 0.8)))
            if best is None or score < best:
                best, seam = score, k
        uv = self.map.wood_member(bm, w, h, seam, ch)
        self.add(name, "timber", bm, frame_matrix(p0, ex, ey, ez), uv)

    # ---- iron
    def iron_box(self, name, centre, size, ex, ey, chamfer=None):
        ex, ey, ez = basis_from(ex, ey)
        bm = bevel(box_local(*size), P["iron_chamfer"] if chamfer is None else chamfer)
        uv = self.map.planar_part(bm, "iron")
        self.add(name, "iron", bm, frame_matrix(Vector(centre), ex, ey, ez), uv)

    def band(self, name, start, axis, length, w, h, wdir):
        ex, ey, ez = basis_from(axis, wdir)
        t = P["strap"]["band_thickness"]
        bm = bevel(rect_ring(length, w, h, t), P["iron_chamfer"])
        uv = self.map.planar_part(bm, "iron")
        self.add(name, "iron", bm, frame_matrix(Vector(start), ex, ey, ez), uv)

    def iron_lathe(self, name, origin, axis, profile, sides, closed=False, band="iron", hint=(0, 1, 0),
                   islands=1, cap="planar", circ_px=None):
        ex, ey, ez = basis_from(axis, hint)
        bm = lathe(profile, sides, closed)
        r_max = max(r for _, r in profile)
        uv = self.map.lathe_part(bm, band, r_max, islands, cap, circ_px)
        kind = {"iron": "iron", "wood": "timber", "grip": "grip"}[band]
        self.add(name, kind, bm, frame_matrix(Vector(origin), ex, ey, ez), uv)

    def bolt(self, name, pos, normal, washer=True, rivet=False):
        n = Vector(normal).normalized()
        pos = Vector(pos)
        if rivet:
            prof = [(-0.002, 0.0), (-0.002, 0.009), (0.002, 0.009), (0.005, 0.0065), (0.0065, 0.0)]
            self.iron_lathe(name, pos, n, prof, 8)
            return
        base = pos
        if washer:
            self.iron_box(name + "_washer", pos + n * 0.001, (0.006, 0.05, 0.05), n, (0, 1, 0) if abs(n.y) < 0.9 else (1, 0, 0), chamfer=0.001)
            base = pos + n * 0.004
        prof = [(-0.002, 0.0), (-0.002, 0.017), (0.003, 0.017), (0.008, 0.013), (0.0115, 0.0065), (0.0125, 0.0)]
        self.iron_lathe(name, base, n, prof, 8)


def build_geometry(seed):
    A = Assembly(seed)
    Xt = P["trestle_x"]
    L = P["leg"]
    sec = L["section"]
    hy = P["axle_height"]

    def leg_z(y):
        return L["foot_z"] - (L["foot_z"] - L["top_z"]) * (y / L["top_y"])

    lean = math.atan2(L["foot_z"] - L["top_z"], L["top_y"])
    half_leg_z = sec / 2 / math.cos(lean)          # half-thickness of a leg measured along z
    ground = ((0, 0, 0), (0, -1, 0))

    for sx in (-1, 1):
        x = sx * Xt
        side = "L" if sx < 0 else "R"
        # legs: foot cut level with the ground, top square to the leg
        for sz in (-1, 1):
            foot = Vector((x, 0.0, sz * L["foot_z"]))
            top = Vector((x, L["top_y"], sz * L["top_z"]))
            axis = (top - foot).normalized()
            A.timber(f"leg_{side}{'F' if sz > 0 else 'B'}", foot - axis * 0.06, top, sec, sec, (1, 0, 0), cut0=ground)
        # trestle sill (along z) against the outer face of the legs
        ts = P["trestle_sill"]
        sxc = x + sx * (sec / 2 - 0.005 + ts["section"] / 2)
        A.timber(f"trestle_sill_{side}", (sxc, ts["section"] / 2, -ts["half_len"]), (sxc, ts["section"] / 2, ts["half_len"]),
                 ts["section"], ts["section"], (1, 0, 0))
        # cross rail (along z) at mid height on the outer face
        cr = P["cross_rail"]
        cxc = x + sx * (sec / 2 - 0.005 + cr["width"] / 2)
        A.timber(f"cross_rail_{side}", (cxc, cr["y"], -cr["half_len"]), (cxc, cr["y"], cr["half_len"]),
                 cr["width"], cr["height"], (1, 0, 0))
        # diagonal brace on the outer face: sill (front) up to the cross rail (back), ends let in
        br = P["brace"]
        bxc = x + sx * (sec / 2 + br["thickness"] / 2)
        b0 = Vector((bxc, 0.10, 0.30))
        b1 = Vector((bxc, cr["y"] - 0.04, -0.27))
        d = (b1 - b0).normalized()
        A.timber(f"brace_{side}", b0 - d * 0.1, b1 + d * 0.1, br["thickness"], br["width"], (1, 0, 0),
                 cut0=((0, 0.10, 0), (0, -1, 0)), cut1=((0, cr["y"] - 0.04, 0), (0, 1, 0)))
        # bands round the cross rail ends and the trestle sill ends
        bw = P["strap"]["band_width"]
        for sz in (-1, 1):
            z0 = sz * (cr["half_len"] - 0.055)
            A.band(f"band_cross_{side}{sz}", (cxc, cr["y"], z0), (0, 0, sz), bw, cr["width"], cr["height"], (1, 0, 0))
            z0 = sz * (ts["half_len"] - 0.045)
            A.band(f"band_tsill_{side}{sz}", (sxc, ts["section"] / 2, z0), (0, 0, sz), bw * 0.8, ts["section"], ts["section"], (1, 0, 0))
        # bolts: leg to trestle sill, leg to cross rail, brace across the back leg
        for sz in (-1, 1):
            A.bolt(f"bolt_tsill_{side}{sz}", (sxc + sx * ts["section"] / 2, 0.066, sz * leg_z(0.066)), (sx, 0, 0))
            A.bolt(f"bolt_cross_{side}{sz}", (cxc + sx * cr["width"] / 2, cr["y"], sz * leg_z(cr["y"])), (sx, 0, 0))
        yb = 0.47
        tb = (yb - b0.y) / (b1.y - b0.y)
        A.bolt(f"bolt_brace_{side}", (bxc + sx * br["thickness"] / 2, yb, b0.z + (b1.z - b0.z) * tb), (sx, 0, 0), washer=False)

        # bearing strap across the leg tops (both sides)
        st = P["strap"]["thickness"]
        zs = leg_z(hy) + half_leg_z + 0.02
        stx = x + sx * (sec / 2 + st / 2)
        A.iron_box(f"strap_upper_{side}", (stx, hy, 0.0), (st, 0.12, 2 * zs), (1, 0, 0), (0, 1, 0))
        for sz in (-1, 1):
            for yy in (hy - 0.03, hy + 0.03):
                A.bolt(f"bolt_strap_{side}{sz}{yy:.2f}", (stx + sx * st / 2, yy, sz * leg_z(yy)), (sx, 0, 0), washer=False)

    # tie beams along X: long sills on the ground and rails at mid height, front and back
    ls = P["long_sill"]
    lr = P["long_rail"]
    for sz in (-1, 1):
        tag = "F" if sz > 0 else "B"
        A.timber(f"long_sill_{tag}", (-ls["half_len"], ls["height"] / 2, sz * ls["z"]), (ls["half_len"], ls["height"] / 2, sz * ls["z"]),
                 ls["width"], ls["height"], (0, 0, 1))
        rz = sz * (leg_z(lr["y"]) + half_leg_z + lr["width"] / 2 - 0.004)
        A.timber(f"long_rail_{tag}", (-(Xt + 0.06), lr["y"], rz), (Xt + 0.06, lr["y"], rz), lr["width"], lr["height"], (0, 0, 1))
        for sx in (-1, 1):
            A.band(f"band_rail_{tag}{sx}", (sx * (Xt + 0.02), lr["y"], rz), (sx, 0, 0), P["strap"]["band_width"], lr["width"], lr["height"], (0, 0, 1))
            A.bolt(f"bolt_rail_{tag}{sx}", (sx * (Xt - 0.035), lr["y"], rz + sz * lr["width"] / 2), (0, 0, sz))
            A.bolt(f"bolt_lsill_{tag}{sx}", (sx * Xt, 0.055, sz * (ls["z"] + ls["width"] / 2)), (0, 0, sz))
    # back diagonal brace, lying on the inclined back face of the legs
    br = P["brace"]
    zb0 = -(leg_z(0.08) + half_leg_z + br["thickness"] / 2)
    zb1 = -(leg_z(lr["y"]) + half_leg_z + br["thickness"] / 2)
    b0 = Vector((-(Xt - 0.05), 0.08, zb0))
    b1 = Vector((Xt - 0.05, lr["y"], zb1))
    d = (b1 - b0).normalized()
    plane_n = Vector((0.0, math.sin(lean), -math.cos(lean)))          # back-face normal (outward, -z)
    A.timber("brace_back", b0 - d * 0.12, b1 + d * 0.12, br["width"], br["thickness"], d.cross(plane_n),
             cut0=((0, 0.08, 0), (0, -1, 0)), cut1=((0, lr["y"], 0), (0, 1, 0)))
    tb = 0.5
    A.bolt("bolt_brace_back", tuple(b0 + (b1 - b0) * tb + plane_n * br["thickness"] / 2), tuple(plane_n), washer=False)

    # ---- drum: log with iron hoops, iron-rimmed flanges, iron axle
    D = P["drum"]
    lh = D["log_half"]
    c = 0.012
    A.iron_lathe("log", (0, hy, 0), (1, 0, 0),
                 [(-lh, 0.0), (-lh, D["log_radius"] - c), (-lh + c, D["log_radius"]), (lh - c, D["log_radius"]),
                  (lh, D["log_radius"] - c), (lh, 0.0)], 16, band="wood", islands=2, cap="end")
    for sx in (-1, 1):
        x0 = sx * (lh - 0.02 - D["hoop_width"] / 2)
        r0, r1 = D["log_radius"] - 0.002, D["log_radius"] + D["hoop_thickness"]
        hw = D["hoop_width"] / 2
        A.iron_lathe(f"hoop_{sx}", (x0, hy, 0), (1, 0, 0),
                     [(-hw, r0), (-hw, r1 - 0.0015), (-hw + 0.0015, r1), (hw - 0.0015, r1), (hw, r1 - 0.0015), (hw, r0)], 16, closed=True)
        xf = sx * (D["flange_inner_x"] + D["flange_thickness"] / 2)
        ft = D["flange_thickness"] / 2
        A.iron_lathe(f"flange_{sx}", (xf, hy, 0), (1, 0, 0),
                     [(-ft, D["log_radius"] - 0.003), (-ft, D["flange_radius"]), (ft, D["flange_radius"]), (ft, D["log_radius"] - 0.003)], 48, closed=True)
        rw = D["rim_half_width"]
        ri, ro = D["rim_inner"], D["rim_outer"]
        A.iron_lathe(f"rim_{sx}", (xf, hy, 0), (1, 0, 0),
                     [(-rw, ri), (-rw, ro - 0.003), (-rw + 0.003, ro), (rw - 0.003, ro), (rw, ro - 0.003), (rw, ri)], 48, closed=True)
        for face in (-1, 1):
            xs = xf + face * ft
            for k in range(12):
                th = 2 * math.pi * (k + 0.5 * (face > 0)) / 12
                p = (xs, hy + 0.37 * math.cos(th), 0.37 * math.sin(th))
                A.bolt(f"rivet_{sx}{face}{k}", p, (face, 0, 0), rivet=True)
        for k in range(6):
            th = 2 * math.pi * k / 6 + 0.3
            A.bolt(f"flange_bolt_{sx}{k}", (xf + sx * ft, hy + 0.195 * math.cos(th), 0.195 * math.sin(th)), (sx, 0, 0), washer=False)

    st = P["strap"]["thickness"]
    ar = P["axle_radius"]
    cu, cl = P["crank_upper"], P["crank_lower"]
    x_axle_l = -(Xt + cu["arm_x"] + 0.035)
    x_axle_r = Xt + sec / 2 + st + 0.045
    A.iron_lathe("axle", (0, hy, 0), (1, 0, 0),
                 [(x_axle_l, 0.0), (x_axle_l, ar - 0.003), (x_axle_l + 0.003, ar), (x_axle_r - 0.003, ar), (x_axle_r, ar - 0.003), (x_axle_r, 0.0)], 12)
    # bearing bosses outside the straps
    A.iron_lathe("boss_axle_R", (Xt + sec / 2 + st, hy, 0), (1, 0, 0),
                 [(0.0, 0.0), (0.0, 0.05), (0.022, 0.05), (0.03, 0.042), (0.03, 0.0)], 16)

    # ---- crank side (-X): hanger plate, lower strap, pinion shaft, two cranks
    x_face = -(Xt + sec / 2)
    pin_y = P["pinion"]["height"]
    zs = leg_z(pin_y) + half_leg_z + 0.02
    A.iron_box("strap_lower_L", (x_face - st / 2, pin_y, 0.0), (st, 0.10, 2 * zs), (1, 0, 0), (0, 1, 0))
    for sz in (-1, 1):
        for yy in (pin_y - 0.025, pin_y + 0.025):
            A.bolt(f"bolt_lstrap_{sz}{yy:.3f}", (x_face - st, yy, sz * leg_z(yy)), (-1, 0, 0), washer=False)
    hang_x = x_face - st - st / 2
    y_lo, y_hi = pin_y - 0.055, hy + 0.055
    A.iron_box("hanger_L", (hang_x, (y_lo + y_hi) / 2, 0.0), (st, y_hi - y_lo, 0.13), (1, 0, 0), (0, 1, 0))
    for yy in (y_hi - 0.02, y_lo + 0.02):
        for sz in (-1, 1):
            A.bolt(f"bolt_hanger_{yy:.3f}{sz}", (hang_x - st / 2, yy, sz * 0.042), (-1, 0, 0), washer=False)
    x_out = hang_x - st / 2
    A.iron_lathe("boss_axle_L", (x_out, hy, 0), (-1, 0, 0),
                 [(0.0, 0.0), (0.0, 0.05), (0.022, 0.05), (0.03, 0.042), (0.03, 0.0)], 16)
    A.iron_lathe("boss_pinion", (x_out, pin_y, 0), (-1, 0, 0),
                 [(0.0, 0.0), (0.0, 0.04), (0.02, 0.04), (0.026, 0.034), (0.026, 0.0)], 16)
    pr = P["pinion"]["radius"]
    x_pin_in = -(Xt - 0.03)
    x_pin_out = -(Xt + cl["arm_x"] + 0.03)
    A.iron_lathe("pinion_shaft", (0, pin_y, 0), (1, 0, 0),
                 [(x_pin_out, 0.0), (x_pin_out, pr - 0.002), (x_pin_out + 0.002, pr), (x_pin_in - 0.002, pr), (x_pin_in, pr - 0.002), (x_pin_in, 0.0)], 12)

    gr = P["grip_radius"]

    def crank(tag, centre_y, spec, x_arm):
        ang = math.radians(spec["dir_deg"])
        dz, dy = math.cos(ang), math.sin(ang)
        arm = spec["arm"]
        hub = Vector((x_arm, centre_y, 0.0))
        pin = Vector((x_arm, centre_y + dy * arm, dz * arm))
        t = 0.016
        along = (pin - hub).normalized()
        A.iron_box(f"crank_arm_{tag}", (hub + pin) / 2, (arm, t, 0.045), along, (1, 0, 0), chamfer=0.002)
        A.iron_lathe(f"crank_hub_{tag}", hub + Vector((t / 2, 0, 0)), (-1, 0, 0),
                     [(0.0, 0.0), (0.0, 0.036), (t + 0.006, 0.036), (t + 0.012, 0.03), (t + 0.012, 0.0)], 16)
        A.iron_lathe(f"crank_eye_{tag}", pin + Vector((t / 2, 0, 0)), (-1, 0, 0),
                     [(0.0, 0.0), (0.0, 0.028), (t + 0.004, 0.028), (t + 0.008, 0.022), (t + 0.008, 0.0)], 16)
        x0 = x_arm - t / 2 - 0.008
        gl = spec["grip_len"]
        A.iron_lathe(f"grip_pin_{tag}", (x0, pin.y, pin.z), (-1, 0, 0),
                     [(0.0, 0.0), (0.0, 0.011), (gl + 0.02, 0.011), (gl + 0.02, 0.0)], 10)
        prof = [(0.004, 0.0), (0.004, gr * 0.8), (0.012, gr * 0.95), (0.05, gr), (0.1, gr * 1.04),
                (gl - 0.02, gr * 0.98), (gl - 0.004, gr * 0.85), (gl, 0.0)]
        A.iron_lathe(f"grip_{tag}", (x0, pin.y, pin.z), (-1, 0, 0), prof, 12, band="grip",
                     circ_px=2 * math.pi * gr * PPM)
        A.iron_lathe(f"grip_cap_{tag}", (x0 - gl - 0.001, pin.y, pin.z), (-1, 0, 0),
                     [(0.0, 0.0), (0.0, 0.016), (0.004, 0.016), (0.008, 0.011), (0.009, 0.0)], 10)
        A.iron_box(f"nut_{tag}", (x_arm - t / 2 - 0.009, centre_y, 0.0), (0.018, 0.045, 0.045), (1, 0, 0), (0, 1, 0), chamfer=0.002)

    crank("upper", hy, cu, -(Xt + cu["arm_x"]))
    crank("lower", pin_y, cl, -(Xt + cl["arm_x"]))

    # ---- rope: helix flange to flange, then off the underside, over the front sill, to a hook
    R = P["rope"]
    rr, hr = R["radius"], R["helix_radius"]
    x_a = -(D["flange_inner_x"] - rr)
    x_b = D["flange_inner_x"] - rr
    Q = Vector((ls["z"] - ls["width"] / 2 - 0.0177, ls["height"] + 0.0177))     # (z, y) rope centre at the sill's rear edge
    A_, B_ = Q.x, hy - Q.y
    alpha = math.atan2(B_, A_)
    phi_end = math.asin(hr / math.hypot(A_, B_)) - alpha                        # A sin(phi) + B cos(phi) = R
    phi_end %= 2 * math.pi
    turns = R["turns"]
    phi0 = phi_end - 2 * math.pi * turns
    n_seg = int(turns * R["segments_per_turn"])
    pts = []
    lead = 0.8
    # dead-end anchor clear of the crank-side flange: past its outer face by the rope's own radius
    # plus a small gap, so the tube never enters the flange's solid thickness
    x_anchor = -(D["flange_inner_x"] + D["flange_thickness"] + rr + 0.003)
    for k in range(4):                                                           # lead-in from the anchor
        t = k / 4.0
        ph = phi0 - lead * (1 - t)
        xx = x_anchor + (x_a - x_anchor) * t
        pts.append(Vector((xx, hy - hr * math.cos(ph), hr * math.sin(ph))))
    for k in range(n_seg + 1):
        ph = phi0 + (phi_end - phi0) * k / n_seg
        xx = x_a + (x_b - x_a) * k / n_seg
        pts.append(Vector((xx, hy - hr * math.cos(ph), hr * math.sin(ph))))
    p_leave = pts[-1]
    x_drop = 0.33
    q1 = Vector((x_drop + 0.01, Q.y, Q.x))
    span = (q1 - p_leave).length
    n_free = max(2, int(span / R["free_step"]))
    for k in range(1, n_free):
        pts.append(p_leave + (q1 - p_leave) * (k / n_free))
    zf = ls["z"] + ls["width"] / 2
    ctrl = [q1,
            Vector((x_drop + 0.005, ls["height"] + rr, ls["z"])),
            Vector((x_drop, ls["height"] + 0.0177, zf + 0.0177)),
            Vector((x_drop - 0.004, 0.07, zf + rr + 0.001)),
            Vector((x_drop - 0.012, 0.032, zf + rr + 0.012)),
            Vector((x_drop - 0.05, rr, zf + rr + 0.03)),
            Vector((x_drop - 0.15, rr, zf + rr + 0.045)),
            Vector((x_drop - 0.30, rr, zf + rr + 0.04)),
            Vector((x_drop - 0.45, rr, zf + rr + 0.04)),
            Vector((x_drop - 0.56, rr, zf + rr + 0.042))]
    # Catmull-Rom through the control points, resampled
    dense = []
    ext = [ctrl[0] * 2 - ctrl[1]] + ctrl + [ctrl[-1] * 2 - ctrl[-2]]
    for i in range(1, len(ext) - 2):
        p0_, p1_, p2_, p3_ = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for s in range(8):
            t = s / 8.0
            dense.append(0.5 * ((2 * p1_) + (-p0_ + p2_) * t + (2 * p0_ - 5 * p1_ + 4 * p2_ - p3_) * t * t
                                + (-p0_ + 3 * p1_ - 3 * p2_ + p3_) * t * t * t))
    dense.append(ctrl[-1])
    acc = 0.0
    last = pts[-1]
    for p in dense:
        acc += (p - last).length
        last = p
        if acc >= R["free_step"] * 0.999 or p is dense[-1]:
            pts.append(p.copy())
            acc = 0.0
    for p in pts:                                   # nothing may dip below the ground
        if p.y < rr:
            p.y = rr
    radii = [rr] * len(pts)
    bm, info, length = sweep(pts, radii, R["sides"])
    uv = A.map.tube(bm, info, "rope", 80.0)
    A.add("rope", "rope", bm, Matrix.Identity(4), uv)
    rope_len = length

    # hook at the free end, lying on the ground: eye ring, shank and a tapering bend toward the sill
    end = pts[-1]
    rod, eye_r, eye_rod = 0.009, 0.02, 0.0075
    eye_c = Vector((end.x - 0.012, eye_rod + 0.0005, end.z))
    ring = [(eye_rod * math.sin(2 * math.pi * k / 8), eye_r + eye_rod * math.cos(2 * math.pi * k / 8)) for k in range(8)]
    A.iron_lathe("hook_eye", eye_c, (0, 1, 0), ring, 16, closed=True, hint=(1, 0, 0))
    hk = [Vector((eye_c.x - eye_r - eye_rod * 0.5, rod, end.z))]
    for k in range(1, 4):
        hk.append(Vector((hk[0].x - 0.02 * k, rod, end.z)))
    bend = 0.026
    cz = end.z - bend
    cxh = hk[-1].x
    for k in range(1, 14):
        a = math.pi * 1.1 * k / 13
        hk.append(Vector((cxh - bend * math.sin(a), rod, cz + bend * math.cos(a))))
    radii = [rod * (1.0 if i < len(hk) - 7 else max(0.35, 1.0 - 0.1 * (i - (len(hk) - 7)))) for i in range(len(hk))]
    bm, info, _ = sweep(hk, radii, 8)
    uv = A.map.tube(bm, info, "iron", 2 * math.pi * rod * PPM)
    A.add("hook", "iron", bm, Matrix.Identity(4), uv)
    return A, rope_len


# ----------------------------------------------------------------------------------------------
# mesh, material, checks, export

def make_object(A):
    mesh = bpy.data.meshes.new(ASSET_ID)
    verts = [g2b(v) for v in A.verts]
    mesh.from_pydata(verts, [], A.faces)
    uvl = mesh.uv_layers.new(name="UVMap")
    flat = []
    for poly_uv in A.uvs:
        for u, v in poly_uv:
            flat += (u, v)
    uvl.data.foreach_set("uv", flat)
    mesh.polygons.foreach_set("use_smooth", [True] * len(mesh.polygons))
    mesh.set_sharp_from_angle(angle=math.radians(48.0))
    obj = bpy.data.objects.new(ASSET_ID, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mod = obj.modifiers.new("weighted", "WEIGHTED_NORMAL")
    mod.mode = "FACE_AREA"
    mod.weight = 50
    mod.keep_sharp = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def make_material(obj, images):
    mat = bpy.data.materials.new("MAT_" + ASSET_ID)
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (600, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = images["basecolor"][0]
    tb.location = (-400, 300)
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = images["normal"][0]
    tn.location = (-400, 0)
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.location = (-100, 0)
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    to = nt.nodes.new("ShaderNodeTexImage")
    to.image = images["orm"][0]
    to.location = (-400, -300)
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    sep.location = (-100, -300)
    nt.links.new(to.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    # the exporter reads occlusion from its own "glTF Material Output" group; build it with the
    # add-on's helper so the importer (used by verify_glb) finds every socket it expects
    from io_scene_gltf2.blender.com.material_helpers import create_settings_group, get_gltf_node_name
    group = bpy.data.node_groups.get(get_gltf_node_name()) or create_settings_group(get_gltf_node_name())
    gnode = nt.nodes.new("ShaderNodeGroup")
    gnode.node_tree = group
    gnode.location = (300, -350)
    nt.links.new(sep.outputs["Red"], gnode.inputs["Occlusion"])
    mat.use_backface_culling = True   # every part is a closed outward solid: export single-sided
    obj.data.materials.append(mat)
    return mat


def mesh_checks(obj, A):
    """Quad sanity (no bow-ties), degenerate faces, UV area and texel density per kind."""
    me = obj.data
    V = np.array([tuple(v.co) for v in me.vertices])
    uv = np.zeros((len(me.loops), 2))
    me.uv_layers[0].data.foreach_get("uv", uv.ravel())
    bowtie = nonplanar = degenerate = uv_zero = 0
    density = {}
    for poly in me.polygons:
        idx = list(poly.vertices)
        pts = V[idx]
        n = np.array(poly.normal)
        area = poly.area
        if area < 1e-10:
            degenerate += 1
            continue
        if len(idx) == 4:
            for k in range(4):
                a, b, c = pts[k - 1], pts[k], pts[(k + 1) % 4]
                if np.dot(np.cross(b - a, c - b), n) <= 0:
                    bowtie += 1
                    break
            d = np.abs(np.dot(pts - pts.mean(axis=0), n)).max()
            if d > 1e-4 * max(1.0, math.sqrt(area)) and d > 2e-5:
                nonplanar += 1
        luv = uv[list(poly.loop_indices)] * TEX
        uva = 0.0
        for k in range(1, len(luv) - 1):
            e1, e2 = luv[k] - luv[0], luv[k + 1] - luv[0]
            uva += abs(e1[0] * e2[1] - e1[1] * e2[0]) / 2
        if uva < 1e-6:
            uv_zero += 1
            continue
        kind = A.parts[A.face_part[poly.index]]["kind"]
        density.setdefault(kind, []).append((math.sqrt(uva / area), area))
    dens = {}
    for kind, vals in density.items():
        arr = np.array(vals)
        w = arr[:, 1] / arr[:, 1].sum()
        dens[kind] = {"area_weighted_mean_px_per_m": round(float((arr[:, 0] * w).sum()), 1),
                      "p5": round(float(np.percentile(arr[:, 0], 5)), 1),
                      "p50": round(float(np.percentile(arr[:, 0], 50)), 1),
                      "p95": round(float(np.percentile(arr[:, 0], 95)), 1)}
    return {"bowtie_quads": bowtie, "nonplanar_quads": nonplanar, "degenerate_faces": degenerate,
            "uv_zero_area_faces": uv_zero, "texel_density": dens}


def export_glb(obj, path):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_normals=True, export_texcoords=True, export_tangents=True,
                              export_materials="EXPORT", export_image_format="AUTO")


def verify_glb(path):
    """Re-import the GLB and measure it in the glTF frame."""
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.import_scene.gltf(filepath=path)
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    tris = 0
    lo = np.full(3, np.inf)
    hi = np.full(3, -np.inf)
    inward = 0
    mats, imgs = set(), {}
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        tris += len(me.loop_triangles)
        co = np.array([tuple(o.matrix_world @ v.co) for v in me.vertices])
        g = np.stack([co[:, 0], co[:, 2], -co[:, 1]], axis=1)       # Blender -> glTF frame
        lo, hi = np.minimum(lo, g.min(axis=0)), np.maximum(hi, g.max(axis=0))
        cn = np.zeros((len(me.loops), 3))
        me.corner_normals.foreach_get("vector", cn.ravel())
        for lt in me.loop_triangles:
            fn = np.array(lt.normal)
            vn = cn[list(lt.loops)].mean(axis=0)
            if np.dot(fn, vn) < 0:
                inward += 1
        for m in me.materials:
            mats.add(m.name)
            if m.use_nodes:
                for n in m.node_tree.nodes:
                    if n.type == "TEX_IMAGE" and n.image:
                        imgs[n.image.name] = list(n.image.size)
    dims = hi - lo
    centre = (hi + lo) / 2
    return {"triangles": tris, "min": [round(float(x), 5) for x in lo], "max": [round(float(x), 5) for x in hi],
            "dimensions_m": [round(float(x), 4) for x in dims], "centre_xz": [round(float(centre[0]), 5), round(float(centre[2]), 5)],
            "triangles_against_normals": inward, "materials": sorted(mats), "images": imgs, "mesh_objects": len(objs)}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--seed", type=int, default=4127)
    ap.add_argument("--no-world-iron", action="store_true", help="generate the iron band instead of using the world material")
    args = ap.parse_args(argv)
    t0 = time.time()
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    images, iron_source = build_atlas(args.seed, not args.no_world_iron, os.path.join(out, "textures"))
    A, rope_len = build_geometry(args.seed)

    # centre on X/Z and put the lowest point on y = 0
    co = np.array([tuple(v) for v in A.verts])
    lo, hi = co.min(axis=0), co.max(axis=0)
    shift = Vector((-(lo[0] + hi[0]) / 2, -lo[1], -(lo[2] + hi[2]) / 2))
    A.verts = [v + shift for v in A.verts]

    obj = make_object(A)
    make_material(obj, images)
    checks = mesh_checks(obj, A)
    bad_parts = [p for p in A.parts if p["open_edges"] or p["signed_volume_m3"] <= 0]

    glb = os.path.join(out, ASSET_ID + ".glb")
    export_glb(obj, glb)
    measured = verify_glb(glb)

    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_winch.py",
        "command": "blender --background --factory-startup --python tools/asset_pipeline/_procgen_winch.py -- "
                   f"--seed {args.seed}" + (" --no-world-iron" if args.no_world_iron else ""),
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "concept": {"path": "assets/concepts/prop_quarry_winch.png",
                    "sha256": sha256(CONCEPT) if os.path.isfile(CONCEPT) else None},
        "replaces": "assets/ready/prop_quarry_winch/prop_quarry_winch.glb (Pixal3D reconstruction; not modified)",
        "frame": "glTF +Y up, front +Z, drum axis X, centred on X/Z, lowest point y = 0; cranks on the model's right (-X)",
        "shift_applied_m": [round(shift.x, 5), round(shift.y, 5), round(shift.z, 5)],
        "parameters": P,
        "materials": {
            "material": "MAT_" + ASSET_ID,
            "atlas_px": TEX, "texel_density_px_per_m": PPM,
            "bands_rows_from_bottom": BANDS,
            "maps": {k: os.path.relpath(v[1], out).replace("\\", "/") for k, v in images.items()},
            "normal_convention": "tangent space, OpenGL (+G = +V)",
            "orm": "R occlusion (texture cavity only, no per-mesh AO bake), G roughness, B metallic",
            "iron_source": iron_source,
            "iron_source_sha256": {k: sha256(os.path.join(WORLD_IRON, f"material_cast_iron_surface_{k}.png"))
                                   for k in ("basecolor", "normal", "orm")} if iron_source.startswith("world") else None,
            "timber_rope_grip_endgrain": "procedural numpy textures from the seed (periodic in U)",
        },
        "dimensions_m": {"x": measured["dimensions_m"][0], "y": measured["dimensions_m"][1], "z": measured["dimensions_m"][2]},
        "triangles": measured["triangles"],
        "rope_length_m": round(rope_len, 3),
        "checks": {"parts": len(A.parts), "parts_not_closed_or_inward": bad_parts, **checks, "glb": measured},
        "build_seconds": round(time.time() - t0, 1),
    }
    ppath = os.path.join(out, ASSET_ID + "_provenance.json")
    with open(ppath, "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    print(json.dumps({k: prov[k] for k in ("dimensions_m", "triangles")}), flush=True)
    print(json.dumps(prov["checks"], indent=1)[:4000], flush=True)
    print("PROCGEN_RESULT " + ppath, flush=True)


if __name__ == "__main__":
    main()
