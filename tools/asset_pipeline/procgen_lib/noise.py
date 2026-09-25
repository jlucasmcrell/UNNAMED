"""Seeded, deterministic numpy noise (no Blender dependency), for geometry jitter and numpy-painted maps.

Extracted from _procgen_anvil.py (hash, perlin3, fbm3, voronoi3), _procgen_trestle.py (value noise) and the
shared smoothstep/lerp helpers that anvil, railtrack, trestle and winch each carried.

API (p is an (N, 3) float array; every function is pure and deterministic in its seed):
    hash3(ix, iy, iz, seed) -> uint32          integer lattice hash
    perlin3(p, seed) -> (N,)                   gradient noise, about [-1, 1]
    fbm3(p, seed, octaves=4, lacunarity=2.03, gain=0.5) -> (N,)   rotated-octave fBm of perlin3, about [-1, 1]
    vnoise3(p, seed) -> (N,)                   value noise, [-1, 1]
    vfbm3(p, seed, octaves=3, gain=0.5) -> (N,)
    voronoi3(p, seed) -> (f1, f2, rnd)         F1/F2 in cell units and a [0, 1) random per nearest cell
    smoothstep(e0, e1, x), lerp(a, b, t)       numpy helpers (e0 > e1 gives the falling step)
"""
import numpy as np

_GRAD = np.array([[1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0], [1, 0, 1], [-1, 0, 1],
                  [1, 0, -1], [-1, 0, -1], [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1],
                  [1, 1, 0], [-1, 1, 0], [0, -1, 1], [0, -1, -1]], np.float64)


def _rotation(angle, axis):
    """Rodrigues rotation matrix (the octave decorrelation rotation anvil built with mathutils)."""
    axis = np.asarray(axis, np.float64)
    axis = axis / np.linalg.norm(axis)
    x, y, z = axis
    c, s, t = np.cos(angle), np.sin(angle), 1.0 - np.cos(angle)
    return np.array([[t * x * x + c, t * x * y - s * z, t * x * z + s * y],
                     [t * x * y + s * z, t * y * y + c, t * y * z - s * x],
                     [t * x * z - s * y, t * y * z + s * x, t * z * z + c]])


_ROT = _rotation(0.61, (0.36, 0.48, 0.8))   # == Matrix.Rotation(0.61, 3, axis), applied as q @ _ROT like anvil


def hash3(ix, iy, iz, seed):
    h = (np.asarray(ix, np.int64) * 73856093 + np.asarray(iy, np.int64) * 19349663
         + np.asarray(iz, np.int64) * 83492791 + int(seed) * 2654435761) & 0xFFFFFFFF
    h = h.astype(np.uint32)
    h ^= h >> np.uint32(16)
    h *= np.uint32(0x7FEB352D)
    h ^= h >> np.uint32(15)
    h *= np.uint32(0x846CA68B)
    h ^= h >> np.uint32(16)
    return h


def _unit(h):
    return h.astype(np.float64) * (1.0 / 4294967295.0)


def _fade(f):
    return f * f * f * (f * (f * 6.0 - 15.0) + 10.0)


def perlin3(p, seed):
    """Gradient noise, roughly [-1, 1]."""
    p = np.asarray(p, np.float64)
    i0 = np.floor(p)
    f = p - i0
    i0 = i0.astype(np.int64)
    u = _fade(f)
    out = np.zeros(len(p))
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1.0 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1.0 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1.0 - u[:, 2]
                g = _GRAD[hash3(i0[:, 0] + dx, i0[:, 1] + dy, i0[:, 2] + dz, seed) & np.uint32(15)]
                out += wx * wy * wz * (g[:, 0] * (f[:, 0] - dx) + g[:, 1] * (f[:, 1] - dy) + g[:, 2] * (f[:, 2] - dz))
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


def vnoise3(p, seed):
    """Value noise in [-1, 1], quintic interpolation."""
    p = np.asarray(p, np.float64)
    fl = np.floor(p)
    f = p - fl
    i = fl.astype(np.int64)
    u = _fade(f)
    out = np.zeros(len(p))
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                out += wx * wy * wz * _unit(hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed))
    return out * 2 - 1


def vfbm3(p, seed, octaves=3, gain=0.5):
    p = np.asarray(p, np.float64)
    total, amp, norm = np.zeros(len(p)), 1.0, 0.0
    for o in range(octaves):
        total += amp * vnoise3(p * (2.0 ** o) + o * 17.13, seed + o * 101)
        norm += amp
        amp *= gain
    return total / norm


def voronoi3(p, seed):
    """F1, F2 (cell units) and a [0, 1) random per nearest cell."""
    p = np.asarray(p, np.float64)
    c = np.floor(p).astype(np.int64)
    f = p - c
    f1 = np.full(len(p), 9.0)
    f2 = np.full(len(p), 9.0)
    cid = np.zeros(len(p), np.uint32)
    for ox in (-1, 0, 1):
        for oy in (-1, 0, 1):
            for oz in (-1, 0, 1):
                h = hash3(c[:, 0] + ox, c[:, 1] + oy, c[:, 2] + oz, seed)
                jx = (h & np.uint32(1023)) / 1023.0
                jy = ((h >> np.uint32(10)) & np.uint32(1023)) / 1023.0
                jz = ((h >> np.uint32(20)) & np.uint32(1023)) / 1023.0
                d = (ox + jx - f[:, 0]) ** 2 + (oy + jy - f[:, 1]) ** 2 + (oz + jz - f[:, 2]) ** 2
                closer = d < f1
                f2 = np.where(closer, f1, np.minimum(f2, d))
                cid = np.where(closer, h, cid)
                f1 = np.where(closer, d, f1)
    rnd = (hash3(cid.astype(np.int64), 7, 11, seed + 3) & np.uint32(0xFFFF)) / 65536.0
    return np.sqrt(f1), np.sqrt(f2), rnd


def smoothstep(e0, e1, x):
    t = np.clip((np.asarray(x, np.float64) - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    """a + (b - a) t; a/b may be colours ((3,) or (N, 3)) with t per point (N,)."""
    a, b, t = (np.asarray(v, np.float64) for v in (a, b, t))
    if t.ndim == 1 and (a.shape[-1:] == (3,) or b.shape[-1:] == (3,)):
        t = t[:, None]
    return a + (b - a) * t
