"""Bake the ten Phase-1 world materials from procedural Blender shaders that tile by construction.

The maps these replace were photographs of material samples on a grey backdrop, roll-tiled: most of
every texture was backdrop, and no crop of a photograph repeats. These are rebuilt as procedural PBR
shaders in which every input is a periodic function of UV, so the baked maps repeat exactly:

  * organic detail is 4D noise sampled on a flat torus, (cos 2pi u, sin 2pi u, cos 2pi v, sin 2pi v)
    scaled per axis, so f(u + 1, v) == f(u, v + 1) == f(u, v);
  * structure (boards, courses, blocks, slates, stones, tussocks) comes from integer cell lattices
    whose per-cell random values are hashed from the cell index modulo the cell count, including a
    2D Voronoi whose feature points are hashed the same way.

Base colour, height, roughness and metallic are baked through emission; the normal is baked from a
Bump node driven by the same height, in tangent space with +Y up (OpenGL, as Godot expects). Bakes
run at twice the output resolution and are box-filtered down. Occlusion is a cavity term of the baked
height, blurred with a periodic (FFT) Gaussian so it tiles too.

Writes, per material, into --out/<id>/: <id>_basecolor.png (sRGB), <id>_normal.png, <id>_orm.png
(occlusion R, roughness G, metallic B), <id>_ao.png, <id>_roughness.png, <id>_material.json (the
same record schema as assets/materials) and MAT_<id>.tres. Proof renders and metrics go to
--out/_proof/. Nothing under assets/materials is read for writing or touched.

Usage (Blender 5.2):
  blender --background --factory-startup --python _blender_bake_world_materials.py -- [options]
    --material ID [ID ...]   default: all ten
    --stage bake|render|all  default: all
    --res 2048 --ss 2 --samples 4
    --out DIR                default: assets/_staging/materials
"""
import argparse
import json
import math
import os
import random
import struct
import sys
import time
import zlib

import bpy
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(REPO, "assets")
OUT_DEFAULT = os.path.join(ASSETS, "_staging", "materials")
TAU = 2.0 * math.pi
HEIGHT_ENC = 5.0   # height is emitted as h * 5 + 0.5, so +-0.1 m fits the 0..1 range


# --------------------------------------------------------------------------------------------
# A small expression layer over shader nodes, so a material reads as maths instead of wiring.
# --------------------------------------------------------------------------------------------

def lin(rgb):
    """sRGB 0-255 to linear, for colour constants."""
    out = []
    for x in rgb:
        x = x / 255.0
        out.append(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4)
    return tuple(out)


def kind(x):
    if isinstance(x, S):
        return x.k
    if isinstance(x, (tuple, list)):
        return "v"
    return "f"


def sid(sockets, identifier):
    for socket in sockets:
        if socket.identifier == identifier:
            return socket
    raise KeyError(identifier)


class S:
    """A node output socket with a kind: 'f' float or 'v' vector/colour."""
    __slots__ = ("g", "s", "k")

    def __init__(self, g, socket, k):
        self.g, self.s, self.k = g, socket, k

    def __add__(self, o): return self.g.op2("ADD", self, o)
    def __radd__(self, o): return self.g.op2("ADD", o, self)
    def __sub__(self, o): return self.g.op2("SUBTRACT", self, o)
    def __rsub__(self, o): return self.g.op2("SUBTRACT", o, self)
    def __mul__(self, o): return self.g.op2("MULTIPLY", self, o)
    def __rmul__(self, o): return self.g.op2("MULTIPLY", o, self)
    def __truediv__(self, o): return self.g.op2("DIVIDE", self, o)
    def __rtruediv__(self, o): return self.g.op2("DIVIDE", o, self)
    def __neg__(self): return self.g.op2("MULTIPLY", self, -1.0)


class Cells:
    pass


class Graph:
    def __init__(self, tree):
        self.tree = tree
        self.count = 0
        tc = self.node("ShaderNodeTexCoord")
        offset = self.node("ShaderNodeCombineXYZ")
        offset.name = "UV_OFFSET"
        uv = self.vm("ADD", S(self, tc.outputs["UV"], "v"), S(self, offset.outputs[0], "v"))
        self.U, self.V, _ = self.sep(uv)
        self.base = self.torus(self.U, self.V)

    # ---- plumbing
    def node(self, kind_name, **props):
        n = self.tree.nodes.new(kind_name)
        for key, value in props.items():
            setattr(n, key, value)
        n.location = ((self.count % 60) * 180.0, -(self.count // 60) * 280.0)
        self.count += 1
        return n

    def put(self, socket, value):
        if isinstance(value, S):
            self.tree.links.new(value.s, socket)
        elif socket.type in ("VECTOR", "RGBA"):
            if not isinstance(value, (tuple, list)):
                value = (value, value, value)
            size = len(socket.default_value)
            values = [float(x) for x in value][:size]
            values += [1.0] * (size - len(values))
            socket.default_value = values
        else:
            socket.default_value = float(value)

    def m(self, op, a, b=0.0, c=0.0, clamp=False):
        n = self.node("ShaderNodeMath", operation=op, use_clamp=clamp)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], b)
        self.put(n.inputs[2], c)
        return S(self, n.outputs[0], "f")

    def vm(self, op, a, b=(0.0, 0.0, 0.0), c=(0.0, 0.0, 0.0), scale=1.0, out=0):
        n = self.node("ShaderNodeVectorMath", operation=op)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], b)
        self.put(n.inputs[2], c)
        self.put(n.inputs[3], scale)
        return S(self, n.outputs[out], "v" if out == 0 else "f")

    def op2(self, op, a, b):
        ka, kb = kind(a), kind(b)
        if ka == "f" and kb == "f":
            if not isinstance(a, S) and not isinstance(b, S):
                return {"ADD": a + b, "SUBTRACT": a - b, "MULTIPLY": a * b,
                        "DIVIDE": a / b}[op]
            return self.m(op, a, b)
        if op == "MULTIPLY" and "f" in (ka, kb):
            vec, sc = (a, b) if ka == "v" else (b, a)
            return self.vm("SCALE", vec, scale=sc)
        if op == "DIVIDE" and kb == "f":
            return self.vm("SCALE", a, scale=(1.0 / b) if not isinstance(b, S)
                           else self.m("DIVIDE", 1.0, b))
        return self.vm(op, a, b)

    # ---- scalar helpers
    def floor(self, a): return self.m("FLOOR", a) if kind(a) == "f" else self.vm("FLOOR", a)
    def fract(self, a): return self.m("FRACT", a) if kind(a) == "f" else self.vm("FRACTION", a)
    def mod(self, a, b): return self.m("FLOORED_MODULO", a, b)
    def abs(self, a): return self.m("ABSOLUTE", a)
    def min(self, a, b): return self.m("MINIMUM", a, b)
    def max(self, a, b): return self.m("MAXIMUM", a, b)
    def sqrt(self, a): return self.m("SQRT", self.m("MAXIMUM", a, 0.0))
    def pow(self, a, b): return self.m("POWER", self.m("MAXIMUM", a, 0.0), b)
    def sin(self, a): return self.m("SINE", a)
    def cos(self, a): return self.m("COSINE", a)
    def lt(self, a, b): return self.m("LESS_THAN", a, b)
    def gt(self, a, b): return self.m("GREATER_THAN", a, b)
    def sat(self, a): return self.m("ADD", a, 0.0, clamp=True)

    def _range(self, interp, e0, e1, x):
        n = self.node("ShaderNodeMapRange", data_type="FLOAT", interpolation_type=interp, clamp=True)
        self.put(n.inputs[0], x)
        self.put(n.inputs[1], e0)
        self.put(n.inputs[2], e1)
        self.put(n.inputs[3], 0.0)
        self.put(n.inputs[4], 1.0)
        return S(self, n.outputs[0], "f")

    def smooth(self, e0, e1, x): return self._range("SMOOTHSTEP", e0, e1, x)
    def lin(self, e0, e1, x): return self._range("LINEAR", e0, e1, x)

    def mix(self, a, b, t):
        if kind(a) == "f" and kind(b) == "f":
            n = self.node("ShaderNodeMix", data_type="FLOAT", clamp_factor=True)
            self.put(sid(n.inputs, "Factor_Float"), t)
            self.put(sid(n.inputs, "A_Float"), a)
            self.put(sid(n.inputs, "B_Float"), b)
            return S(self, sid(n.outputs, "Result_Float"), "f")
        n = self.node("ShaderNodeMix", data_type="VECTOR", factor_mode="UNIFORM", clamp_factor=True)
        self.put(sid(n.inputs, "Factor_Float"), t)
        self.put(sid(n.inputs, "A_Vector"), a)
        self.put(sid(n.inputs, "B_Vector"), b)
        return S(self, sid(n.outputs, "Result_Vector"), "v")

    # ---- vector helpers
    def vec(self, x, y, z=0.0):
        n = self.node("ShaderNodeCombineXYZ")
        self.put(n.inputs[0], x)
        self.put(n.inputs[1], y)
        self.put(n.inputs[2], z)
        return S(self, n.outputs[0], "v")

    def sep(self, v):
        n = self.node("ShaderNodeSeparateXYZ")
        self.put(n.inputs[0], v)
        return tuple(S(self, n.outputs[i], "f") for i in range(3))

    def length(self, v): return self.vm("LENGTH", v, out=1)
    def dot(self, a, b): return self.vm("DOT_PRODUCT", a, b, out=1)
    def normalize(self, v): return self.vm("NORMALIZE", v)
    def muladd(self, a, b, c): return self.vm("MULTIPLY_ADD", a, b, c)

    def ramp(self, t, stops, interp="LINEAR"):
        """Colour ramp over t; stops are (position, sRGB 0-255)."""
        n = self.node("ShaderNodeValToRGB")
        cr = n.color_ramp
        cr.interpolation = interp
        stops = sorted(stops)
        cr.elements[0].position = stops[0][0]
        cr.elements[0].color = lin(stops[0][1]) + (1.0,)
        cr.elements[1].position = stops[-1][0]
        cr.elements[1].color = lin(stops[-1][1]) + (1.0,)
        for position, colour in stops[1:-1]:
            e = cr.elements.new(position)
            e.color = lin(colour) + (1.0,)
        self.put(n.inputs[0], t)
        return S(self, n.outputs[0], "v")

    def white(self, v):
        n = self.node("ShaderNodeTexWhiteNoise", noise_dimensions="3D")
        self.put(n.inputs["Vector"], v)
        return S(self, n.outputs["Color"], "v")

    def hash(self, kx, ky, seed):
        """Three independent uniform values for integer cell keys (keys must already be modded)."""
        return self.sep(self.white(self.vec(kx, ky, float(seed))))

    # ---- periodic noise on the flat torus
    def torus(self, U, V):
        cu, su = self.cos(U * TAU), self.sin(U * TAU)
        cv, sv = self.cos(V * TAU), self.sin(V * TAU)
        return (self.vec(cu, su, cv), sv)

    def tor(self, fu, fv=None, seed=0, base=None, off=None):
        """4D sample point: fu, fv are noise lattice periods per tile along u and v."""
        fv = fu if fv is None else fv
        b3, bw = base or self.base
        rng = random.Random(seed * 7919 + 17)
        o = [rng.uniform(-300.0, 300.0) for _ in range(4)]
        p = self.muladd(b3, (fu / TAU, fu / TAU, fv / TAU), tuple(o[:3]))
        w = self.m("MULTIPLY_ADD", bw, fv / TAU, o[3])
        if off is not None:
            p = p + off
        return p, w

    def n4(self, fu, fv=None, seed=0, detail=2.0, rough=0.5, lac=2.0, dist=0.0, base=None,
           off=None, color=False):
        """Periodic fBm noise, normalised to about [0, 1] (mean 0.5)."""
        p, w = self.tor(fu, fv, seed, base, off)
        n = self.node("ShaderNodeTexNoise", noise_dimensions="4D", noise_type="FBM", normalize=True)
        self.put(n.inputs["Vector"], p)
        self.put(n.inputs["W"], w)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Detail"], detail)
        self.put(n.inputs["Roughness"], rough)
        self.put(n.inputs["Lacunarity"], lac)
        self.put(n.inputs["Distortion"], dist)
        return S(self, n.outputs[1], "v") if color else S(self, n.outputs[0], "f")

    def z4(self, fu, fv=None, seed=0, detail=2.0, **kw):
        """Periodic noise rescaled to roughly zero mean, unit standard deviation."""
        sigma = NOISE_SIGMA.get(int(round(detail)), 0.075)
        return (self.n4(fu, fv, seed, detail, **kw) - 0.5) * (1.0 / sigma)

    def warp(self, amount, fu, seed, detail=3.0):
        """Displace (U, V) by periodic noise; the result is still periodic in U and V."""
        a = self.z4(fu, seed=seed, detail=detail)
        b = self.z4(fu, seed=seed + 1, detail=detail)
        return self.U + a * amount, self.V + b * amount

    # ---- periodic 2D Voronoi on an N x M jittered lattice
    def cells(self, U, V, N, M, T, jx=1.0, jy=None, seed=0, edge=True):
        """F1 (m), vector to the nearest point (m), its 3 hash values, and distance to its edge (m).

        The lattice point of cell (i, j) is hashed from (i mod N, j mod M), so the diagram repeats
        exactly once per tile. Distances are measured in metres, so an N x M lattice over a square
        tile gives cells of T/N by T/M.
        """
        jy = jx if jy is None else jy
        sx, sy = T / N, T / M
        grid = self.vec(U * float(N), V * float(M), 0.0)
        cell0 = self.vm("FLOOR", grid)
        candidates = []
        for dj in (-1.0, 0.0, 1.0):
            for di in (-1.0, 0.0, 1.0):
                c = cell0 + (di, dj, 0.0)
                key = self.vm("MODULO", c + (8.0 * N, 8.0 * M, 0.0), (float(N), float(M), 1.0e9))
                h = self.white(key + (1.0, 1.0, float(seed)))
                point = self.muladd(h, (jx, jy, 0.0), c + (0.5 - 0.5 * jx, 0.5 - 0.5 * jy, 0.0))
                delta = (point - grid) * (sx, sy, 0.0)
                candidates.append((self.length(delta), delta, h))
        best, bdelta, bhash = candidates[0]
        for dist, delta, h in candidates[1:]:
            t = self.lt(dist, best)
            best = self.min(best, dist)
            bdelta = self.mix(bdelta, delta, t)
            bhash = self.mix(bhash, h, t)
        out = Cells()
        out.f1, out.delta, out.hash = best, bdelta, bhash
        out.dx, out.dy, _ = self.sep(bdelta)
        out.h1, out.h2, out.h3 = self.sep(bhash)
        if edge:
            e = None
            for dist, delta, h in candidates:
                mid = (bdelta + delta) * 0.5
                normal = delta - bdelta
                d = self.dot(mid, self.normalize(normal)) + self.lt(self.length(normal), 1.0e-7) * 10.0
                e = d if e is None else self.min(e, d)
            out.edge = e
        return out


    def n3(self, p, detail=2.0, rough=0.5):
        """Plain 3D noise of an arbitrary (already periodic) vector."""
        n = self.node("ShaderNodeTexNoise", noise_dimensions="3D", noise_type="FBM", normalize=True)
        self.put(n.inputs["Vector"], p)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Detail"], detail)
        self.put(n.inputs["Roughness"], rough)
        return S(self, n.outputs[0], "f")

    def rotate(self, cells, angle):
        """Cell-local (along, across) coordinates in metres at a heading."""
        ca, sa = self.cos(angle), self.sin(angle)
        return cells.dx * ca + cells.dy * sa, cells.dy * ca - cells.dx * sa

    def straws(self, T, N, seed, chance, width=0.0006):
        """Short straight stalks lying at random headings, one per lattice cell at most."""
        c = self.cells(self.U, self.V, N, N, T, jx=0.9, seed=seed, edge=False)
        along, across = self.rotate(c, c.h1 * math.pi)
        half = c.h2 * 0.022 + 0.012
        return ((1.0 - self.smooth(width, width * 2.0, self.abs(across)))
                * (1.0 - self.smooth(half * 0.85, half, self.abs(along)))
                * self.lt(c.h3, chance))


# FBM 4D noise standard deviation by detail, measured on 1024 px bakes (normalised FBM, rough 0.5).
NOISE_SIGMA = {0: 0.121, 1: 0.090, 2: 0.079, 3: 0.075, 4: 0.072, 5: 0.071, 6: 0.070, 7: 0.070,
               8: 0.070}


# --------------------------------------------------------------------------------------------
# Materials. Each returns base colour (linear), height (m), roughness and metallic.
# --------------------------------------------------------------------------------------------

MATERIALS = {}


def material(asset_id, cls, tile_m, ao):
    """tile_m: metres one repeat covers; ao: strength of the height-cavity occlusion."""
    def register(fn):
        MATERIALS[asset_id] = {"fn": fn, "class": cls, "tile_m": tile_m, "ao": ao}
        return fn
    return register


@material("material_packed_dirt_ground", "ground", 4.0, ao=0.55)
def packed_dirt(g, T):
    big = g.z4(2, seed=1, detail=2)
    mid = g.z4(12, seed=2, detail=5, rough=0.55)
    fine = g.z4(150, seed=3, detail=3)
    grit = g.z4(900, seed=4, detail=1)
    dry = g.smooth(0.0, 1.0, g.z4(4, seed=6, detail=3))
    # compacted clods, and a trodden crust that cracks where it has dried out
    clods = g.cells(g.U, g.V, 60, 60, T, jx=0.9, seed=13)
    lump = g.pow(g.smooth(0.0, 0.02, clods.edge), 0.7) * (clods.h1 * 0.004 + 0.002)
    uw, vw = g.warp(0.004, 40, seed=5)
    crust = g.cells(uw, vw, 18, 18, T, jx=0.85, seed=11)
    crack = (1.0 - g.smooth(0.001, 0.0045, crust.edge)) * dry
    # small stones pressed into the surface
    peb = g.cells(g.U, g.V, 90, 90, T, jx=0.8, seed=12, edge=False)
    pdx, pdy, _ = g.sep(g.normalize(peb.delta))
    # an angular, uneven outline, sunk into the soil so only a low cap shows
    r0 = (peb.h2 * 0.011 + 0.005) * (g.n3(g.vec(pdx * 0.7, pdy * 0.7, peb.h1 * 40.0), 2.0) * 1.6 + 0.2)
    present = g.lt(peb.h3, 0.14)
    dome = g.pow(g.sat(1.0 - (peb.f1 / r0) * (peb.f1 / r0)), 0.5) * present
    stone = g.gt(dome, 0.02)
    height = (big * 0.006 + mid * 0.003 + lump + fine * 0.0008 + grit * 0.0003 - crack * 0.005
              + dome * r0 * 0.3)

    soil = g.ramp(g.sat(mid * 0.08 + fine * 0.1 + big * 0.03 + (clods.h2 - 0.5) * 0.3 + 0.5),
                  [(0.0, (62, 49, 38)), (0.5, (88, 71, 54)), (1.0, (116, 96, 74))])
    soil = g.mix(soil, lin((120, 106, 88)), dry * 0.15)
    soil = soil * (1.0 - crack * 0.55) * (1.0 + grit * 0.035) * (0.9 + g.sat(lump * 250.0) * 0.1)
    speck = g.smooth(2.0, 2.6, g.z4(420, seed=7, detail=1))
    soil = g.mix(soil, lin((40, 33, 28)), speck * 0.8)
    pebble = g.ramp(peb.h1, [(0.0, (70, 64, 56)), (0.5, (98, 90, 78)), (1.0, (124, 114, 96))])
    pebble = pebble * (0.8 + g.pow(dome, 0.5) * 0.2)
    base = g.mix(soil, pebble, stone)
    rough = g.mix(0.86 + dry * 0.08 + fine * 0.01, 0.74, stone)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


def radial_blades(g, c, radius, K, L, seed):
    """Blades radiating from a tussock centre: 3D noise over (direction * K, radius * L)."""
    r = c.f1
    d = g.normalize(c.delta)
    dx, dy, _ = g.sep(d)
    # a ragged outline rather than a disc
    radius = radius * (g.n3(g.vec(dx * 1.5, dy * 1.5, c.h1 * 30.0 + seed), detail=2.0) * 1.6 + 0.2)
    streak = g.n3(g.vec(dx * K, dy * K, r * L + c.h1 * 50.0 + seed), detail=2.0)
    # blades thin out toward and past the rim
    threshold = 0.46 + g.sat(r / radius) * 0.1
    return g.smooth(threshold, threshold + 0.06, streak), g.sat(1.0 - r / radius)


@material("material_marsh_grass_turf", "ground", 3.0, ao=0.85)
def marsh_grass(g, T):
    uw, vw = g.warp(0.01, 6, seed=20)
    soil_z = g.z4(10, seed=21, detail=5)
    moss_z = g.z4(6, seed=22, detail=4)
    tus = g.cells(uw, vw, 9, 9, T, jx=0.85, seed=23)
    tuft = g.cells(g.U, g.V, 23, 23, T, jx=0.9, seed=24)
    # fade each clump out before its lattice cell ends, so no clump is clipped to a polygon
    tus_fade = g.smooth(0.0, 0.05, tus.edge)
    tuft_fade = g.smooth(0.0, 0.02, tuft.edge)

    tus_r = tus.h2 * 0.06 + 0.1
    tus_blade, tus_t = radial_blades(g, tus, tus_r * 1.35, 30.0, 11.0, 3.0)
    tus_blade = tus_blade * tus_fade
    tus_t = tus_t * tus_fade
    tus_body = g.smooth(0.0, 0.3, tus_t)
    tus_h = g.smooth(0.0, 1.0, tus_t) * (tus.h3 * 0.03 + 0.04) + tus_blade * 0.006 - 0.002

    tuft_r = tuft.h2 * 0.02 + 0.035
    tuft_blade, tuft_t = radial_blades(g, tuft, tuft_r * 1.4, 16.0, 20.0, 7.0)
    tuft_blade = tuft_blade * tuft_fade
    tuft_t = tuft_t * tuft_fade
    tuft_body = g.smooth(0.0, 0.3, tuft_t)
    tuft_h = g.smooth(0.0, 1.0, tuft_t) * 0.018 + tuft_blade * 0.004 - 0.004

    soil_h = soil_z * 0.004 - 0.008
    straw = g.straws(T, 40, 25, 0.55) * (1.0 - tus_body) * (1.0 - tuft_body)
    height = g.max(g.max(soil_h + straw * 0.002, tuft_h), tus_h)
    # blades that fall outside a tussock's body still lie over the soil
    on_tus = g.max(g.gt(tus_h, g.max(soil_h, tuft_h)) * tus_body, tus_blade * (1.0 - tus_body))
    on_tuft = g.max(g.gt(tuft_h, soil_h) * tuft_body, tuft_blade * (1.0 - tuft_body)) * (1.0 - on_tus)

    soil = g.ramp(g.sat(soil_z * 0.2 + 0.5), [(0.0, (32, 27, 22)), (1.0, (58, 47, 35))])
    moss = g.smooth(0.6, 1.4, moss_z)
    soil = g.mix(soil, g.ramp(g.sat(moss_z * 0.3 + 0.3), [(0.0, (42, 50, 30)), (1.0, (62, 70, 42))]),
                 moss * 0.8)
    soil = g.mix(soil, lin((132, 118, 84)), straw)
    blade_ramp = [(0.0, (38, 46, 24)), (0.35, (74, 84, 38)), (0.65, (108, 106, 56)),
                  (1.0, (146, 130, 82))]
    dead = g.lt(tus.h3, 0.3) * 0.35
    thatch = lin((46, 44, 28))
    tus_col = g.ramp(g.sat((1.0 - tus_t) * 0.6 + tus.h2 * 0.25 + dead + 0.1), blade_ramp)
    tus_col = g.mix(thatch, tus_col, tus_blade)
    tuft_col = g.ramp(g.sat((1.0 - tuft_t) * 0.5 + tuft.h1 * 0.3 + 0.15), blade_ramp)
    tuft_col = g.mix(thatch, tuft_col, tuft_blade)
    base = g.mix(soil, tuft_col, on_tuft)
    base = g.mix(base, tus_col, on_tus)
    rough = g.mix(g.mix(0.55 + soil_z * 0.03 + moss * 0.25, 0.75, straw), 0.8,
                  g.max(on_tus, on_tuft))
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_loose_gravel", "ground", 2.0, ao=0.85)
def loose_gravel(g, T):
    def layer(N, seed, lift, gap):
        c = g.cells(g.U, g.V, N, N, T, jx=0.95, seed=seed)
        size = T / N
        e = c.edge - (c.h2 + 1.0) * gap
        body = g.smooth(0.0, 0.001, e)
        profile = g.pow(g.smooth(0.0, size * 0.3, e), 0.5)
        tilt = (c.dx * (c.h1 - 0.5) + c.dy * (c.h3 - 0.5)) * 0.3
        facet = g.z4(N * 2.3, seed=seed + 1, detail=2) * (size * 0.03)
        h = lift + body * (profile * size * (c.h1 * 0.15 + 0.3) + tilt + facet) - (1.0 - body) * 0.01
        tone = g.fract(c.h1 * 7.31 + c.h3 * 3.17)
        colour = g.ramp(tone, [(0.0, (74, 73, 72)), (0.15, (102, 100, 96)), (0.45, (124, 120, 111)),
                               (0.7, (140, 129, 108)), (0.85, (116, 98, 80)), (1.0, (148, 144, 134))])
        colour = colour * (0.62 + profile * 0.38) * (1.0 + g.z4(N * 14.0, seed=seed + 2, detail=1) * 0.06)
        return h, colour, c.h2

    ha, ca, ra = layer(44, 31, 0.0, 0.003)
    hb, cb, rb = layer(80, 33, -0.006, 0.002)
    dirt_z = g.z4(30, seed=35, detail=4)
    hd = dirt_z * 0.002 + g.z4(600, seed=36, detail=2) * 0.0004 - 0.01
    b_top = g.gt(hb, hd)
    a_top = g.gt(ha, g.max(hb, hd))
    height = g.max(g.max(ha, hb), hd)
    dirt = g.ramp(g.sat(dirt_z * 0.2 + 0.5), [(0.0, (62, 55, 46)), (1.0, (92, 81, 66))])
    base = g.mix(g.mix(dirt, cb, b_top), ca, a_top)
    rough = g.mix(g.mix(0.95, rb * 0.15 + 0.72, b_top), ra * 0.15 + 0.7, a_top)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_churned_wet_mud", "ground", 3.0, ao=0.6)
def churned_mud(g, T):
    uw, vw = g.warp(0.02, 5, seed=40, detail=4)
    wb = g.torus(uw, vw)
    broad = g.z4(5, seed=41, detail=5, base=wb)
    churn = g.z4(16, seed=42, detail=4, base=wb)
    ridges = g.sat(1.0 - g.abs(churn) * 0.5)
    fine = g.z4(120, seed=43, detail=3, base=wb)

    def prints(N, seed, a, b, depth, chance):
        c = g.cells(uw, vw, N, N, T, jx=0.9, seed=seed, edge=False)
        along, across = g.rotate(c, c.h1 * TAU)
        d = g.sqrt(along * along * (1.0 / (a * a)) + across * across * (1.0 / (b * b)))
        on = g.lt(c.h2, chance)
        pit = (1.0 - g.smooth(0.55, 1.0, d)) * on
        rim = g.smooth(0.85, 1.05, d) * (1.0 - g.smooth(1.05, 1.5, d)) * on
        return rim * depth * 0.35 - pit * depth

    ground = (broad * 0.006 + ridges * 0.014 + fine * 0.0012
              + prints(10, 44, 0.07, 0.05, 0.022, 0.6) + prints(17, 45, 0.045, 0.04, 0.015, 0.5))
    level = -0.012
    water = 1.0 - g.smooth(level - 0.0015, level + 0.0005, ground)
    height = g.max(ground, level)
    straw = g.straws(T, 36, 46, 0.3) * (1.0 - water)
    height = height + straw * 0.0015

    dryness = g.sat((ground - level) * 16.0)
    mud = g.ramp(g.sat(dryness * 0.75 + fine * 0.06 + 0.05),
                 [(0.0, (52, 41, 31)), (0.45, (74, 58, 44)), (1.0, (104, 86, 66))])
    base = g.mix(mud, lin((44, 39, 33)), water)
    base = g.mix(base, lin((124, 106, 68)), straw)
    rough = g.mix(g.mix(0.3 + dryness * 0.38 + fine * 0.02, 0.05, water), 0.7, straw)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_rubble_stone_wall", "stone", 3.0, ao=0.9)
def rubble_wall(g, T):
    uw, vw = g.warp(0.006, 10, seed=50)
    st = g.cells(uw, vw, 7, 14, T, jx=0.85, jy=0.3, seed=51)
    mortar_half = g.sat(g.z4(25, seed=52, detail=2) * 0.25 + 0.5) * 0.01 + 0.008
    e = st.edge - mortar_half
    body = g.smooth(0.0, 0.004, e)
    pillow = g.pow(g.smooth(0.0, 0.06, e), 0.45)
    tilt = (st.dx * (st.h1 - 0.5) + st.dy * (st.h2 - 0.5)) * 0.12
    rock = g.z4(20, seed=53, detail=6, rough=0.55) * 0.004 + g.z4(140, seed=54, detail=3) * 0.0008
    h_stone = pillow * (st.h3 * 0.015 + 0.02) + tilt + rock + 0.012
    mortar_z = g.z4(60, seed=55, detail=3)
    height = g.mix(mortar_z * 0.0015 - 0.004, h_stone, body)

    tone = g.fract(st.h1 * 13.7 + st.h3 * 5.3)
    stone = g.ramp(tone, [(0.0, (76, 74, 70)), (0.3, (100, 98, 92)), (0.55, (118, 114, 104)),
                          (0.75, (114, 99, 82)), (0.9, (92, 87, 80)), (1.0, (128, 124, 116))])
    fleck = g.z4(1000, seed=56, detail=1)
    stone = stone * (1.0 + fleck * 0.045 + g.z4(80, seed=57, detail=3) * 0.05)
    stone = g.mix(stone, lin((40, 40, 38)), g.smooth(1.9, 2.3, fleck) * 0.5)
    lichen = (g.smooth(1.6, 2.3, g.z4(35, seed=58, detail=4))
              * g.sat(g.z4(300, seed=62, detail=2) * 0.4 + 0.5))
    stone = g.mix(stone, lin((132, 134, 118)), lichen * 0.6)
    dark_lichen = g.smooth(1.9, 2.3, g.z4(22, seed=59, detail=4))
    stone = g.mix(stone, lin((56, 57, 52)), dark_lichen * 0.6)
    stone = stone * (0.8 + pillow * 0.2)
    mortar = g.ramp(g.sat(mortar_z * 0.2 + 0.5), [(0.0, (72, 67, 60)), (1.0, (106, 100, 90))])
    base = g.mix(mortar, stone, body)
    moss_z = g.z4(7, seed=60, detail=4)
    moss = g.smooth(0.7, 1.4, moss_z) * (1.0 - g.smooth(-0.004, 0.015, e))
    base = g.mix(base, g.ramp(g.sat(moss_z * 0.3 + 0.2), [(0.0, (46, 52, 32)), (1.0, (64, 70, 42))]),
                 moss * 0.85)
    streak = g.sat(g.z4(30, 2, seed=61, detail=3) * 0.3 + 0.5)
    base = base * (streak * 0.2 + 0.8)
    rough = g.mix(0.93, 0.8 + rock * 0.4 + lichen * 0.08, body)
    rough = g.mix(rough, 0.96, moss)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_plaster_lath_wall", "plaster", 2.5, ao=0.6)
def plaster_lath(g, T):
    undul = g.z4(3, seed=60, detail=3)
    trowel = g.z4(16, seed=61, detail=5, rough=0.6)
    grain = g.z4(600, seed=62, detail=2)
    h_plaster = undul * 0.0025 + trowel * 0.0009 + grain * 0.00012
    uw, vw = g.warp(0.012, 25, seed=63)
    cr = g.cells(uw, vw, 6, 6, T, jx=0.9, seed=64)
    crack = (1.0 - g.smooth(0.0003, 0.0011, cr.edge)) * g.smooth(0.2, 0.9, g.z4(4, seed=65, detail=3))
    h_plaster = h_plaster - crack * 0.0015

    # where the plaster has fallen away and the laths show
    us, vs = g.warp(0.03, 6, seed=66, detail=4)
    spz = g.z4(3.5, seed=67, detail=5, rough=0.6, base=g.torus(us, vs))
    spall = g.smooth(1.95, 2.02, spz)
    rim = g.smooth(1.82, 1.95, spz) * (1.0 - spall)
    lv = g.V * 56.0 + 0.37
    fy = g.fract(lv)
    rkey = g.mod(g.floor(lv), 56.0) + 1.0
    r1, r2, r3 = g.hash(rkey, 3.0, 68)
    lo = r1 * 0.06 + 0.08
    hi = r2 * 0.08 + 0.74
    strip = g.smooth(lo, lo + 0.04, fy) * (1.0 - g.smooth(hi - 0.04, hi, fy))
    lath_grain = g.z4(12, 700, seed=69, detail=3)
    key = g.smooth(0.8, 1.4, g.z4(40, 8, seed=70, detail=2)) * (1.0 - strip)
    h_lath = -0.016 + (1.0 - strip) * -0.012 + lath_grain * 0.0003 * strip + key * 0.008
    height = g.mix(h_plaster + rim * 0.0006, h_lath, spall)

    plaster = g.ramp(g.sat(undul * 0.15 + trowel * 0.1 + 0.5),
                     [(0.0, (148, 139, 120)), (0.5, (170, 160, 140)), (1.0, (184, 176, 158))])
    stain = g.smooth(0.3, 1.5, g.z4(3, seed=71, detail=5))
    plaster = g.mix(plaster, lin((124, 112, 90)), stain * 0.35)
    runs = g.smooth(0.8, 2.0, g.z4(22, 2.2, seed=72, detail=3))
    plaster = g.mix(plaster, lin((116, 108, 94)), runs * 0.35)
    plaster = plaster * (g.sat(g.z4(8, seed=73, detail=4) * 0.2 + 0.5) * 0.15 + 0.85)
    plaster = plaster * (1.0 - crack * 0.5) * (1.0 + grain * 0.015)
    plaster = g.mix(plaster, lin((194, 188, 174)), rim * 0.6)
    wood = g.ramp(g.sat(r3 * 0.6 + lath_grain * 0.12 + 0.2), [(0.0, (74, 61, 46)), (1.0, (110, 92, 69))])
    lath = g.mix(lin((32, 28, 24)), wood, strip)
    lath = g.mix(lath, lin((146, 137, 118)), key)
    base = g.mix(plaster, lath, spall)
    rough = g.mix(0.9 + grain * 0.01 + stain * 0.03, g.mix(0.9, 0.82, strip), spall)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_oak_plank_floor", "timber", 2.4, ao=0.6)
def oak_planks(g, T):
    boards = 12
    bw = T / boards
    cu = g.U * float(boards) + 0.37      # keep board joints off the tile edge
    fx = g.fract(cu)
    ckey = g.mod(g.floor(cu), float(boards)) + 1.0
    c1, c2, c3 = g.hash(ckey, 1.0, 80)
    k = g.gt(c1, 0.45) + 1.0               # one or two boards per column per tile
    vy = g.V * k + c2
    fy = g.fract(vy)
    bkey = g.mod(g.floor(vy), k) + 1.0
    b1, b2, b3 = g.hash(ckey, bkey + 10.0, 81)
    blen = T / k
    ex = g.min(fx, 1.0 - fx) * bw
    ey = g.min(fy, 1.0 - fy) * blen
    edge = g.min(ex, ey)
    seam = g.max(1.0 - g.smooth(0.001, 0.002, ex), 1.0 - g.smooth(0.0006, 0.0014, ey))
    bevel = g.smooth(0.0, 0.004, edge)

    board_off = g.vec(b1 * 97.0, b2 * 89.0 + c3 * 13.0, b3 * 71.0)
    # flat-sawn grain: growth lines that run the board's length and drift slowly across it
    wob = g.z4(3.0, 1.2, seed=82, detail=3, off=board_off)
    wob2 = g.z4(12.0, 3.0, seed=83, detail=2, off=board_off)
    lines = fx * 90.0 + b1 * 40.0 + wob * 1.4 + wob2 * 0.35
    ring = g.sin(lines * TAU) * 0.5 + 0.5
    late = g.smooth(0.8, 0.99, ring)
    fibre = g.z4(900.0, 25.0, seed=86, detail=2, off=board_off)
    pore = g.smooth(1.2, 2.4, g.z4(1400.0, 40.0, seed=84, detail=1, off=board_off))
    worn = g.smooth(0.3, 1.5, g.z4(2.5, seed=85, detail=3))
    grime = 1.0 - g.smooth(0.0, 0.02, edge)
    dents = g.z4(40, seed=87, detail=3)

    bx = fx * bw
    nail_dx = g.min(g.abs(bx - 0.05), g.abs(bx - (bw - 0.05)))
    nail_dy = ey - 0.035
    nail = 1.0 - g.smooth(0.0035, 0.0048, g.sqrt(nail_dx * nail_dx + nail_dy * nail_dy))

    cup = (fx - 0.5) * (fx - 0.5) * 0.0024
    height = (b3 * 0.0008 + cup - (1.0 - bevel) * 0.0025 - seam * 0.004 + late * 0.00015
              + fibre * 0.00004 + dents * 0.00015 - pore * 0.0001 - nail * 0.0004)

    tone = g.ramp(g.sat(b2 * 0.8 + c3 * 0.2), [(0.0, (64, 46, 32)), (0.5, (88, 66, 46)),
                                               (1.0, (112, 88, 62))])
    wood = g.mix(tone, lin((96, 88, 78)), b1 * 0.35)
    wood = wood * (1.0 - late * 0.3) * (1.0 - pore * 0.2) * (1.0 + fibre * 0.05)
    wood = g.mix(wood, wood * 1.18, worn * 0.6)
    wood = wood * (1.0 - grime * 0.45)
    wood = g.mix(wood, lin((26, 22, 18)), seam)
    base = g.mix(wood, lin((44, 40, 36)), nail)
    rough = 0.66 + grime * 0.12 - worn * 0.1 + pore * 0.05
    rough = g.mix(g.mix(rough, 0.9, seam), 0.5, nail)
    return {"base": base, "height": height, "rough": rough, "metal": nail * 0.7}


@material("material_limestone_ashlar", "stone", 2.4, ao=0.7)
def limestone_ashlar(g, T):
    courses = 8
    ch = T / courses
    rv = g.V * float(courses) + 0.41     # keep bed joints off the tile edge
    fy = g.fract(rv)
    rkey = g.mod(g.floor(rv), float(courses)) + 1.0
    r1, r2, r3 = g.hash(rkey, 1.0, 90)
    n = g.floor(r1 * 2.999) + 3.0          # three to five blocks a course per tile
    x = g.U * n + r2
    fx = g.fract(x)
    bkey = g.mod(g.floor(x), n) + 1.0
    k1, k2, k3 = g.hash(rkey, bkey + 20.0, 91)
    bw = T / n
    chip = g.z4(50, seed=92, detail=3) * 0.0012 + g.z4(220, seed=93, detail=2) * 0.0004
    e = g.min(g.min(fx, 1.0 - fx) * bw, g.min(fy, 1.0 - fy) * ch) + chip
    face = g.smooth(0.004, 0.0055, e)
    arris = g.pow(g.smooth(0.004, 0.016, e), 0.6)
    tool_dir = g.gt(k3, 0.5)
    tool = g.sin(g.mix(g.U * 700.0 + g.V * 350.0, g.U * 700.0 - g.V * 350.0, tool_dir) * TAU)
    surface = g.z4(30, seed=94, detail=4)
    pits = g.cells(g.U, g.V, 160, 160, T, jx=0.9, seed=95, edge=False)
    pit = (1.0 - g.smooth(0.4, 1.0, pits.f1 / (pits.h2 * 0.0014 + 0.0008))) * g.lt(pits.h3, 0.1)
    tool = tool * g.smooth(0.3, 1.5, g.z4(9, seed=102, detail=3))   # tooling survives in patches
    h_face = (arris * 0.006 + (k1 - 0.5) * 0.001 + tool * 0.00006 + surface * 0.0005
              - pit * 0.0008)
    mortar_z = g.z4(90, seed=96, detail=3)
    height = g.mix(mortar_z * 0.0004 - 0.003, h_face, face)

    block = g.ramp(k1, [(0.0, (146, 138, 120)), (0.35, (160, 150, 128)), (0.65, (152, 146, 134)),
                        (1.0, (164, 152, 128))])
    block = block * (1.0 + surface * 0.04 + g.z4(200, seed=97, detail=2) * 0.025)
    block = g.mix(block, block * 1.08, 1.0 - arris)
    block = g.mix(block, lin((80, 76, 70)), pit * 0.6)
    lichen = g.smooth(1.6, 1.9, g.z4(14, seed=98, detail=4))
    block = g.mix(block, lin((170, 170, 152)), lichen * 0.6)
    spots = g.smooth(2.3, 2.6, g.z4(40, seed=99, detail=3))
    block = g.mix(block, lin((70, 70, 64)), spots * 0.6)
    mortar = g.ramp(g.sat(mortar_z * 0.2 + 0.5), [(0.0, (112, 106, 94)), (1.0, (140, 132, 118))])
    base = g.mix(mortar, block, face)
    runs = g.smooth(0.5, 2.0, g.z4(28, 2.0, seed=100, detail=3))
    base = g.mix(base, lin((92, 88, 80)), runs * 0.3)
    base = base * (g.sat(g.z4(3, seed=101, detail=4) * 0.25 + 0.5) * 0.2 + 0.8)
    rough = g.mix(0.92, 0.8 + surface * 0.02 + lichen * 0.08 + pit * 0.1, face)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_slate_roof_scale", "roof", 2.0, ao=0.8)
def slate_roof(g, T):
    rows = 12
    rv = g.V * float(rows) + 0.43        # keep the course tails off the tile edge
    fy = g.fract(rv)
    row = g.floor(rv)

    def course(index):
        rkey = g.mod(index, float(rows)) + 1.0
        r1, r2, _ = g.hash(rkey, 1.0, 100)
        n = g.floor(r1 * 2.999) + 8.0      # eight to ten slates a course per tile
        x = g.U * n + r2
        fx = g.fract(x)
        skey = g.mod(g.floor(x), n) + 1.0
        s1, s2, s3 = g.hash(rkey, skey + 30.0, 101)
        return fx, T / n, s1, s2, s3

    fxa, wa, a1, a2, a3 = course(row)
    fxb, wb, b1, b2, b3 = course(row - 1.0)
    edge_z = g.z4(90, 40, seed=102, detail=3)
    # the tail of the course above: dressed, slightly ragged, each slate a little askew
    tail = edge_z * 0.012 + (a3 - 0.5) * 0.04 + (fxa - 0.5) * (a2 - 0.5) * 0.08 + 0.05
    upper = g.smooth(tail - 0.004, tail + 0.004, fy)
    ha = (1.0 - (fy - tail) * 0.85) * 0.009 + a1 * 0.0012
    hb = (1.0 - (fy + 0.95) * 0.85) * 0.009 + b1 * 0.0012
    side_z = g.z4(60, 120, seed=103, detail=2) * 0.0005
    ja = 1.0 - g.smooth(0.0012, 0.0024, g.min(fxa, 1.0 - fxa) * wa + side_z)
    jb = 1.0 - g.smooth(0.0012, 0.0024, g.min(fxb, 1.0 - fxb) * wb + side_z)
    joint = g.mix(jb, ja, upper)
    lamination = g.z4(40, 6, seed=104, detail=3)
    flake = g.smooth(0.6, 1.4, g.z4(30, seed=105, detail=3)) * 0.6
    height = g.mix(hb, ha, upper) - joint * 0.004 + lamination * 0.0003 + flake * 0.0004

    tone = g.mix(g.fract(b1 * 7.3 + b3 * 3.1), g.fract(a1 * 7.3 + a3 * 3.1), upper)
    slate = g.ramp(tone, [(0.0, (54, 58, 66)), (0.3, (68, 74, 84)), (0.55, (76, 80, 84)),
                          (0.75, (74, 68, 76)), (1.0, (62, 68, 64))])
    slate = slate * (1.0 + lamination * 0.05 + flake * 0.06)
    bloom = g.smooth(0.8, 2.0, g.z4(6, seed=106, detail=4))
    slate = g.mix(slate, lin((102, 106, 108)), bloom * 0.35)
    li = g.cells(g.U, g.V, 36, 36, T, jx=0.9, seed=107, edge=False)
    lichen_r = li.h2 * 0.008 + 0.005
    lichen = ((1.0 - g.smooth(lichen_r * 0.5, lichen_r, li.f1)) * g.lt(li.h3, 0.035)
              * g.sat(g.z4(250, seed=109, detail=2) * 0.5 + 0.7))
    slate = g.mix(slate, g.mix(lin((122, 126, 108)), lin((132, 124, 94)), li.h1), lichen * 0.75)
    slate = g.mix(slate, lin((30, 32, 34)), joint)
    moss = g.smooth(0.6, 1.4, g.z4(10, seed=108, detail=4)) * g.max(joint, 1.0 - upper)
    base = g.mix(slate, lin((46, 54, 30)), moss * 0.8)
    height = height + lichen * 0.0005
    rough = 0.62 + lamination * 0.03 + bloom * 0.1
    rough = g.mix(g.mix(rough, 0.9, lichen), 0.95, moss * 0.8)
    return {"base": base, "height": height, "rough": rough, "metal": 0.0}


@material("material_cast_iron_surface", "metal", 1.0, ao=0.4)
def cast_iron(g, T):
    und = g.z4(4, seed=110, detail=3)
    peel = g.z4(90, seed=111, detail=4)
    sand = g.z4(600, seed=112, detail=2)
    pits = g.cells(g.U, g.V, 120, 120, T, jx=0.9, seed=113, edge=False)
    pit = ((1.0 - g.smooth(0.5, 1.0, pits.f1 / (pits.h2 * 0.0012 + 0.0004)))
           * g.lt(pits.h3, 0.3))
    uw, vw = g.warp(0.02, 8, seed=114)
    wb = g.torus(uw, vw)
    rz = g.z4(7, seed=115, detail=6, rough=0.6, base=wb)
    rz2 = g.z4(40, seed=116, detail=4, base=wb)
    halo = 1.0 - g.smooth(1.0, 2.5, pits.f1 / (pits.h2 * 0.0012 + 0.0004))
    rust = g.smooth(1.5, 1.9, rz + rz2 * 0.3 + halo * g.lt(pits.h3, 0.3) * 0.8)
    crust = g.z4(260, seed=117, detail=3)
    worn = g.smooth(1.0, 2.0, und * 0.7 + g.z4(20, seed=119, detail=3) * 0.3) * (1.0 - rust)
    height = (und * 0.0006 + peel * 0.00015 + sand * 0.00004 - pit * 0.0005
              + rust * (crust * 0.0001 + 0.00015))

    scale = g.ramp(g.sat(g.z4(20, seed=118, detail=3) * 0.25 + 0.5),
                   [(0.0, (40, 40, 42)), (0.5, (54, 53, 54)), (1.0, (68, 65, 62))])
    scale = scale * (1.0 + sand * 0.04)
    rust_col = g.ramp(g.sat(crust * 0.25 + rz2 * 0.1 + 0.5),
                      [(0.0, (60, 38, 27)), (0.5, (82, 50, 33)), (1.0, (102, 64, 41))])
    base = g.mix(scale, lin((112, 110, 106)), worn * 0.35)
    base = g.mix(base, rust_col, rust)
    base = g.mix(base, lin((28, 26, 24)), pit * 0.7)
    metal = g.mix(g.mix(0.6, 1.0, worn), 0.0, rust)
    rough = g.mix(g.mix(0.58 + sand * 0.03, 0.32, worn), 0.9, rust)
    rough = g.mix(rough, 0.8, pit)
    return {"base": base, "height": height, "rough": rough, "metal": metal}


# --------------------------------------------------------------------------------------------
# Baking
# --------------------------------------------------------------------------------------------

FORCE_CPU = False


def use_cpu(scene):
    """CPU only, with no GPU compute type at all, so a full shared GPU cannot fail the run."""
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "NONE"
    scene.cycles.device = "CPU"
    return "CPU"


def set_gpu(scene):
    scene.render.engine = "CYCLES"
    if FORCE_CPU:
        return use_cpu(scene)
    prefs = bpy.context.preferences.addons["cycles"].preferences
    for device_type in ("OPTIX", "CUDA"):
        try:
            prefs.compute_device_type = device_type
            prefs.get_devices()
            usable = [d for d in prefs.devices if d.type == device_type]
            if usable:
                for d in prefs.devices:
                    d.use = d.type == device_type
                scene.cycles.device = "GPU"
                return device_type
        except TypeError:
            continue
    scene.cycles.device = "CPU"
    return "CPU"


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    device = set_gpu(scene)
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.use_denoising = False
    return scene, device


DEVICES_USED = set()


def with_fallback(action):
    """Run a bake or render; if the shared GPU has no memory to give, redo it on the CPU.

    The same shader runs either way; only speed differs. The device is recorded. If the CUDA
    context itself cannot be created, the in-process retry can fail as well; the process then
    exits non-zero and the material can be rerun with --device cpu.
    """
    scene = bpy.context.scene
    try:
        result = action()
        DEVICES_USED.add(scene.cycles.device)
        return result
    except RuntimeError as error:
        text = str(error)
        if scene.cycles.device != "GPU" or not any(
                key in text for key in ("memory", "CUDA", "OptiX", "OPTIX", "device")):
            raise
        print(f"  GPU failed ({text.strip()[:120]}); retrying on the CPU", flush=True)
        use_cpu(scene)
        result = action()
        DEVICES_USED.add("CPU")
        return result


def build_material(asset_id):
    spec = MATERIALS[asset_id]
    mat = bpy.data.materials.new(asset_id)
    tree = mat.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    g = Graph(tree)
    result = spec["fn"](g, spec["tile_m"])
    out = g.node("ShaderNodeOutputMaterial")
    emit_colour = g.node("ShaderNodeEmission")
    g.put(emit_colour.inputs["Color"], result["base"])
    g.put(emit_colour.inputs["Strength"], 1.0)
    metal = result.get("metal", 0.0)
    emit_data = g.node("ShaderNodeEmission")
    g.put(emit_data.inputs["Color"],
          g.vec(result["height"] * HEIGHT_ENC + 0.5, result["rough"], metal))
    g.put(emit_data.inputs["Strength"], 1.0)
    bump = g.node("ShaderNodeBump")
    g.put(bump.inputs["Strength"], 1.0)
    g.put(bump.inputs["Distance"], 1.0)
    g.put(bump.inputs["Filter Width"], 1.0)
    g.put(bump.inputs["Height"], result["height"])
    diffuse = g.node("ShaderNodeBsdfDiffuse")
    tree.links.new(bump.outputs["Normal"], diffuse.inputs["Normal"])
    image_node = g.node("ShaderNodeTexImage")
    tree.nodes.active = image_node
    shaders = {"colour": emit_colour.outputs[0], "data": emit_data.outputs[0],
               "normal": diffuse.outputs[0]}
    return mat, tree, out, shaders, image_node, g.count


def bake(obj, tree, out, shader, image_node, res, samples, kind_name, uv_offset=(0.0, 0.0)):
    scene = bpy.context.scene
    scene.cycles.samples = samples
    for link in list(out.inputs["Surface"].links):
        tree.links.remove(link)
    tree.links.new(shader, out.inputs["Surface"])
    tree.nodes["UV_OFFSET"].inputs[0].default_value = uv_offset[0]
    tree.nodes["UV_OFFSET"].inputs[1].default_value = uv_offset[1]
    image = bpy.data.images.new(f"bake_{kind_name}_{res}", res, res, alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    image_node.image = image
    tree.nodes.active = image_node
    started = time.time()
    if kind_name == "normal":
        with_fallback(lambda: bpy.ops.object.bake(
            type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
            normal_b="POS_Z", margin=0, use_clear=True, target="IMAGE_TEXTURES"))
    else:
        with_fallback(lambda: bpy.ops.object.bake(type="EMIT", margin=0, use_clear=True,
                                                  target="IMAGE_TEXTURES"))
    pixels = np.empty(res * res * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    # Blender stores rows bottom-up; flip so row 0 is the top of the PNG (v = 1).
    array = pixels.reshape(res, res, 4)[::-1, :, :3].copy()
    return array, time.time() - started


def make_plane(tile):
    bpy.ops.mesh.primitive_plane_add(size=tile, location=(0.0, 0.0, 0.0))
    obj = bpy.context.active_object
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    return obj


# --------------------------------------------------------------------------------------------
# Image maths (numpy only; everything wraps, so derived maps tile as well)
# --------------------------------------------------------------------------------------------

def downsample(array, factor):
    if factor == 1:
        return array
    h, w = array.shape[:2]
    shape = (h // factor, factor, w // factor, factor) + array.shape[2:]
    return array.reshape(shape).mean(axis=(1, 3))


def to_srgb(linear):
    linear = np.clip(linear, 0.0, 1.0)
    return np.where(linear <= 0.0031308, linear * 12.92,
                    1.055 * np.power(linear, 1.0 / 2.4) - 0.055)


def blur_periodic(array, sigma_px):
    h, w = array.shape
    ky = np.fft.fftfreq(h)[:, None]
    kx = np.fft.rfftfreq(w)[None, :]
    kernel = np.exp(-2.0 * (math.pi * sigma_px) ** 2 * (kx ** 2 + ky ** 2))
    return np.fft.irfft2(np.fft.rfft2(array) * kernel, s=array.shape)


def normal_from_height(height, texel_m):
    """Reference normal from the baked height, OpenGL (+Y up the image), for cross-checking."""
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) / (2.0 * texel_m)
    gy_up = (np.roll(height, 1, axis=0) - np.roll(height, -1, axis=0)) / (2.0 * texel_m)
    n = np.stack([-gx, -gy_up, np.ones_like(height)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def occlusion_from_height(height, texel_m, strength):
    """Cavity occlusion: how far a texel sits below its neighbourhood, at two scales."""
    small = blur_periodic(height, 0.004 / texel_m) - height
    large = blur_periodic(height, 0.03 / texel_m) - height
    cavity = np.clip(small / 0.004, 0.0, 1.0) * 0.6 + np.clip(large / 0.015, 0.0, 1.0) * 0.4
    return np.clip(1.0 - strength * cavity, 0.2, 1.0)


def to_u8(array):
    return (np.clip(array, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def write_png(path, array_u8):
    """Minimal PNG writer (no metadata): L or RGB, 8 bit."""
    if array_u8.ndim == 2:
        colour_type, channels = 0, 1
    else:
        colour_type, channels = 2, array_u8.shape[2]
        assert channels == 3
    h, w = array_u8.shape[:2]
    rows = array_u8.reshape(h, w * channels)
    raw = np.empty((h, w * channels + 1), dtype=np.uint8)
    raw[:, 0] = 0
    raw[:, 1:] = rows

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, colour_type, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw.tobytes(), 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as handle:
        handle.write(png)


def seam_metrics(array):
    """Wrap-edge jump against the interior neighbour jumps, both axes.

    ratio: mean |first - last| column (row) over the mean |adjacent| difference inside the image;
    rank: the fraction of interior adjacent-column (row) mean differences smaller than the edge's.
    A texture that repeats cleanly has ratio near 1 and a rank well inside the distribution; a
    cut photograph's edge is the largest jump in the image (rank 1.0).
    """
    a = array.astype(np.float64)
    if a.ndim == 3:
        a = a.mean(axis=-1)
    col = np.abs(np.diff(a, axis=1)).mean(axis=0)   # per interior column pair
    row = np.abs(np.diff(a, axis=0)).mean(axis=1)   # per interior row pair
    edge_h = float(np.abs(a[:, 0] - a[:, -1]).mean())
    edge_v = float(np.abs(a[0, :] - a[-1, :]).mean())
    return {"ratio_h": round(edge_h / float(col.mean()), 4),
            "ratio_v": round(edge_v / float(row.mean()), 4),
            "rank_h": round(float(np.mean(col < edge_h)), 4),
            "rank_v": round(float(np.mean(row < edge_v)), 4)}


# --------------------------------------------------------------------------------------------
# Stages
# --------------------------------------------------------------------------------------------

def load_png_array(path):
    """Read a PNG through Blender (no PIL here) as stored 0..1 values, rows top-down."""
    image = bpy.data.images.load(path, check_existing=False)
    try:
        w, h = image.size
        pixels = np.empty(w * h * image.channels, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        return pixels.reshape(h, w, image.channels)[::-1, :, :3].copy()
    finally:
        bpy.data.images.remove(image)


def godot_material(asset_id, spec):
    base = f"res://assets/materials/{asset_id}"
    return f"""[gd_resource type="StandardMaterial3D" load_steps=4 format=3]

; {asset_id} - {spec['class']}, one repeat covers {spec['tile_m']} m.
; Generated by _blender_bake_world_materials.py; do not hand-edit.
; Procedural and periodic by construction; the normal map is baked at physical height, so it is
; applied at scale 1.

[ext_resource type="Texture2D" path="{base}/{asset_id}_basecolor.png" id="1_albedo"]
[ext_resource type="Texture2D" path="{base}/{asset_id}_normal.png" id="2_normal"]
[ext_resource type="Texture2D" path="{base}/{asset_id}_orm.png" id="3_orm"]

[resource]
albedo_texture = ExtResource("1_albedo")
normal_enabled = true
normal_texture = ExtResource("2_normal")
normal_scale = 1.0
orm_texture = ExtResource("3_orm")
roughness = 1.0
metallic = 1.0
metallic_specular = 0.5
uv1_scale = Vector3(1, 1, 1)
texture_filter = 3
"""


def lighting_gradient(srgb_u8):
    lum = (srgb_u8[..., 0] * 0.2126 + srgb_u8[..., 1] * 0.7152 + srgb_u8[..., 2] * 0.0722) / 255.0
    return float(np.std(blur_periodic(lum, lum.shape[0] * 0.12)))


def bake_material(asset_id, args, out_root, proof):
    spec = MATERIALS[asset_id]
    tile = spec["tile_m"]
    res, ss = args.res, args.ss
    big = res * ss
    scene, device = reset_scene()
    DEVICES_USED.clear()
    obj = make_plane(tile)
    mat, tree, out, shaders, image_node, count = build_material(asset_id)
    obj.data.materials.append(mat)
    timing = {}
    colour, timing["colour"] = bake(obj, tree, out, shaders["colour"], image_node, big,
                                    args.samples, "colour")
    data, timing["data"] = bake(obj, tree, out, shaders["data"], image_node, big,
                                args.samples, "data")
    normal, timing["normal"] = bake(obj, tree, out, shaders["normal"], image_node, big,
                                    args.samples, "normal")

    # Periodicity of the shader itself: evaluate it half a tile along both axes and compare with
    # the unshifted bake rolled by half. One sample, so both sample exactly the pixel centres.
    check = 1024
    shift = {}
    for kind_name in ("colour", "data"):
        a, _ = bake(obj, tree, out, shaders[kind_name], image_node, check, 1, kind_name)
        b, _ = bake(obj, tree, out, shaders[kind_name], image_node, check, 1, kind_name,
                    uv_offset=(0.5, 0.5))
        diff = np.abs(b - np.roll(a, (check // 2, check // 2), axis=(0, 1)))
        shift[kind_name] = {"max_abs": float(diff.max()),
                            "p99_99": float(np.percentile(diff, 99.99)),
                            "fraction_over_half_8bit_step": float((diff > 0.5 / 255.0).mean())}

    colour = downsample(colour, ss)
    height = downsample((data[..., 0] - 0.5) / HEIGHT_ENC, ss)
    rough = np.clip(downsample(data[..., 1], ss), 0.0, 1.0)
    metal = np.clip(downsample(data[..., 2], ss), 0.0, 1.0)
    n = downsample(normal * 2.0 - 1.0, ss)
    n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-8)
    del data, normal

    texel = tile / res
    ao = occlusion_from_height(height, texel, spec["ao"])
    base_u8 = to_u8(to_srgb(colour))
    normal_u8 = to_u8(n * 0.5 + 0.5)
    orm_u8 = to_u8(np.stack([ao, rough, metal], axis=-1))

    # Cross-check: the baked normal against one differentiated from the baked height.
    ref = normal_from_height(height, texel)
    angle = np.degrees(np.arccos(np.clip((n * ref).sum(-1), -1.0, 1.0)))
    green_corr = float(np.corrcoef(n[..., 1].ravel(), ref[..., 1].ravel())[0, 1])

    directory = os.path.join(out_root, asset_id)
    os.makedirs(directory, exist_ok=True)
    names = {"basecolor": f"{asset_id}_basecolor.png", "normal": f"{asset_id}_normal.png",
             "ao": f"{asset_id}_ao.png", "roughness": f"{asset_id}_roughness.png",
             "orm": f"{asset_id}_orm.png"}
    write_png(os.path.join(directory, names["basecolor"]), base_u8)
    write_png(os.path.join(directory, names["normal"]), normal_u8)
    write_png(os.path.join(directory, names["ao"]), np.ascontiguousarray(orm_u8[..., 0]))
    write_png(os.path.join(directory, names["roughness"]), np.ascontiguousarray(orm_u8[..., 1]))
    write_png(os.path.join(directory, names["orm"]), orm_u8)

    seams = {"basecolor": seam_metrics(base_u8), "normal": seam_metrics(normal_u8),
             "orm": seam_metrics(orm_u8), "height": seam_metrics(height)}
    concept = os.path.join(ASSETS, "concepts", f"{asset_id}.png")
    control = seam_metrics(load_png_array(concept)) if os.path.exists(concept) else None
    lum = base_u8.astype(np.float64) @ np.array([0.2126, 0.7152, 0.0722])
    stats = {
        "basecolor_mean_srgb": [round(float(x), 1) for x in base_u8.reshape(-1, 3).mean(0)],
        "basecolor_luma_mean": round(float(lum.mean()), 1),
        "basecolor_luma_p5_p95": [round(float(x), 1) for x in np.percentile(lum, [5, 95])],
        "height_m_min_max": [round(float(height.min()), 4), round(float(height.max()), 4)],
        "roughness_mean": round(float(rough.mean()), 3),
        "metallic_mean": round(float(metal.mean()), 3),
        "ao_mean": round(float(ao.mean()), 3),
        "normal_vs_height_angle_deg_median_p95": [round(float(np.median(angle)), 3),
                                                  round(float(np.percentile(angle, 95)), 3)],
        "normal_green_vs_height_correlation": round(green_corr, 5),
    }
    record = {
        "asset_id": asset_id,
        "material_class": spec["class"],
        "tile_size_m": tile,
        "resolution": res,
        "maps": names,
        "pbr": {"roughness_base": round(float(rough.mean()), 2),
                "metallic": round(float(metal.mean()), 2),
                "normal_strength": 1.0},
        "source_concept": f"assets/concepts/{asset_id}.png",
        "tileable": True,
        "delit": True,
        "delight": {"method": "not needed: base colour is baked from an emission pass, so no "
                              "lighting is present to remove",
                    "residual_gradient": round(lighting_gradient(base_u8), 5)},
        "generator": {"script": "tools/asset_pipeline/_blender_bake_world_materials.py",
                      "blender": bpy.app.version_string,
                      "method": "procedural shader, periodic by construction (4D noise on a flat "
                                "torus; cell lattices hashed modulo the cell count)",
                      "bake_resolution": big, "supersample": ss, "samples": args.samples,
                      "shader_nodes": count,
                      "cycles_device": f"{device}" if DEVICES_USED == {"GPU"}
                      else "+".join(sorted(DEVICES_USED)),
                      "normal": "tangent space, OpenGL (+Y up the image), physical height, "
                                "apply at normal scale 1.0"},
        "seam": {"maps": seams, "half_tile_shift_rebake": shift,
                 "concept_photo_control": control},
        "stats": stats,
        "note": ("Occlusion (R), roughness (G) and metallic (B) are packed as glTF ORM. Roughness "
                 "and metallic are authored in the shader; occlusion is a periodic cavity term of "
                 "the baked height. The ao and roughness maps repeat the ORM channels."),
    }
    with open(os.path.join(directory, f"{asset_id}_material.json"), "w", encoding="utf-8") as fh:
        json.dump(record, fh, indent=2)
    with open(os.path.join(directory, f"MAT_{asset_id}.tres"), "w", encoding="utf-8") as fh:
        fh.write(godot_material(asset_id, spec))

    # Quick numpy check images: the maps side by side, and a 3x3 raking-lit tiling.
    light = np.array([-0.45, 0.55, 0.70])
    light /= np.linalg.norm(light)
    shade = colour * (0.18 + 0.95 * np.clip((n * light).sum(-1), 0.0, 1.0))[..., None]
    lit = to_u8(to_srgb(shade))
    step = max(1, (3 * res) // 1536)
    write_png(os.path.join(proof, f"{asset_id}_quicklit_3x3.png"),
              np.ascontiguousarray(np.tile(lit, (3, 3, 1))[::step, ::step]))
    small = max(1, res // 512)
    write_png(os.path.join(proof, f"{asset_id}_maps.png"), np.ascontiguousarray(np.concatenate(
        [base_u8[::small, ::small], normal_u8[::small, ::small], orm_u8[::small, ::small],
         lit[::small, ::small]], axis=1)))

    print(f"  {asset_id:<30} {'+'.join(sorted(DEVICES_USED))} nodes={count} bake "
          f"{timing['colour']:.1f}/{timing['data']:.1f}/{timing['normal']:.1f}s "
          f"seam ratio base {seams['basecolor']['ratio_h']:.3f}/{seams['basecolor']['ratio_v']:.3f} "
          f"normal {seams['normal']['ratio_h']:.3f}/{seams['normal']['ratio_v']:.3f} "
          f"shift max {max(v['max_abs'] for v in shift.values()):.2e} "
          f"luma {stats['basecolor_luma_mean']} rough {stats['roughness_mean']}", flush=True)
    return record


def look_at(obj, target):
    from mathutils import Vector
    direction = Vector(target) - obj.location
    obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def sun(scene, elevation_deg, azimuth_deg, strength):
    from mathutils import Vector
    data = bpy.data.lights.new("sun", "SUN")
    data.energy = strength
    data.angle = math.radians(0.6)
    obj = bpy.data.objects.new("sun", data)
    scene.collection.objects.link(obj)
    e, a = math.radians(elevation_deg), math.radians(azimuth_deg)
    toward = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    obj.rotation_euler = (-toward).to_track_quat("-Z", "Y").to_euler()
    return obj


def render_material(asset_id, out_root, proof):
    """Render the staged maps (not the shader) on a 3 x 3 tiled plane: top-down, a close view on
    a tile corner where four repeats meet, and a grazing view under raking light."""
    spec = MATERIALS[asset_id]
    tile = spec["tile_m"]
    directory = os.path.join(out_root, asset_id)
    scene, device = reset_scene()
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.file_format = "PNG"
    world = bpy.data.worlds.new("world")
    scene.world = world
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.42, 0.46, 0.55, 1.0)
    background.inputs["Strength"].default_value = 0.45

    bpy.ops.mesh.primitive_plane_add(size=3.0 * tile)
    plane = bpy.context.active_object
    for loop in plane.data.uv_layers.active.data:
        loop.uv = (loop.uv[0] * 3.0, loop.uv[1] * 3.0)
    mat = bpy.data.materials.new("preview")
    tree = mat.node_tree
    principled = tree.nodes.get("Principled BSDF")

    def texture(name, colourspace):
        node = tree.nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(directory, name), check_existing=False)
        node.image.colorspace_settings.name = colourspace
        node.extension = "REPEAT"
        node.interpolation = "Linear"
        return node

    base = texture(f"{asset_id}_basecolor.png", "sRGB")
    nmap_tex = texture(f"{asset_id}_normal.png", "Non-Color")
    orm = texture(f"{asset_id}_orm.png", "Non-Color")
    split = tree.nodes.new("ShaderNodeSeparateColor")
    nmap = tree.nodes.new("ShaderNodeNormalMap")
    nmap.convention = "OPENGL"
    nmap.inputs["Strength"].default_value = 1.0
    tree.links.new(base.outputs["Color"], principled.inputs["Base Color"])
    tree.links.new(orm.outputs["Color"], split.inputs[0])
    tree.links.new(split.outputs[1], principled.inputs["Roughness"])
    tree.links.new(split.outputs[2], principled.inputs["Metallic"])
    tree.links.new(nmap_tex.outputs["Color"], nmap.inputs["Color"])
    tree.links.new(nmap.outputs["Normal"], principled.inputs["Normal"])
    plane.data.materials.append(mat)

    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    outputs = []

    light = sun(scene, 38.0, 125.0, 3.2)
    cam_data.type = "ORTHO"
    for label, centre, extent, size in (("top", (0.0, 0.0), 3.0 * tile, 1536),
                                        ("corner", (0.5 * tile, 0.5 * tile), 0.4 * tile, 1024)):
        cam_data.ortho_scale = extent
        cam.location = (centre[0], centre[1], 5.0 * tile)
        cam.rotation_euler = (0.0, 0.0, 0.0)
        scene.render.resolution_x = scene.render.resolution_y = size
        path = os.path.join(proof, f"{asset_id}_{label}.png")
        scene.render.filepath = path
        with_fallback(lambda: bpy.ops.render.render(write_still=True))
        outputs.append(path)

    bpy.data.objects.remove(light)
    sun(scene, 11.0, 20.0, 4.0)
    cam_data.type = "PERSP"
    cam_data.lens = 30.0
    cam_data.clip_start = 0.01
    cam.location = (0.35 * tile, -1.62 * tile, 0.2 * tile)
    look_at(cam, (0.0, 0.2 * tile, 0.0))
    scene.render.resolution_x, scene.render.resolution_y = 1600, 1024
    path = os.path.join(proof, f"{asset_id}_grazing.png")
    scene.render.filepath = path
    with_fallback(lambda: bpy.ops.render.render(write_still=True))
    outputs.append(path)
    print(f"  rendered {asset_id}: " + ", ".join(os.path.basename(p) for p in outputs), flush=True)
    return outputs


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--material", nargs="*", default=None)
    parser.add_argument("--stage", choices=("bake", "render", "all"), default="all")
    parser.add_argument("--res", type=int, default=2048)
    parser.add_argument("--ss", type=int, default=2)
    parser.add_argument("--samples", type=int, default=4)
    parser.add_argument("--out", default=OUT_DEFAULT)
    parser.add_argument("--device", choices=("auto", "cpu"), default="auto",
                        help="auto: OptiX/CUDA if available; cpu: never touch the GPU")
    args = parser.parse_args(argv)
    global FORCE_CPU
    FORCE_CPU = args.device == "cpu"
    ids = args.material or sorted(MATERIALS)
    unknown = [i for i in ids if i not in MATERIALS]
    if unknown:
        print(f"  unknown material(s): {unknown}")
        return 1
    out_root = os.path.abspath(args.out)
    protected = os.path.normcase(os.path.join(ASSETS, "materials"))
    if os.path.normcase(out_root) == protected or os.path.normcase(out_root).startswith(protected + os.sep):
        print("  refusing to write into assets/materials; the photographs stay untouched")
        return 1
    proof = os.path.join(out_root, "_proof")
    os.makedirs(proof, exist_ok=True)
    summary_path = os.path.join(proof, "bake_summary.json")
    summary = {}
    if os.path.exists(summary_path):
        with open(summary_path, encoding="utf-8") as fh:
            summary = json.load(fh)
    if args.stage in ("bake", "all"):
        for asset_id in ids:
            record = bake_material(asset_id, args, out_root, proof)
            summary[asset_id] = {"tile_size_m": record["tile_size_m"], "seam": record["seam"],
                                 "stats": record["stats"], "pbr": record["pbr"]}
            with open(summary_path, "w", encoding="utf-8") as fh:
                json.dump(summary, fh, indent=2)
    if args.stage in ("render", "all"):
        for asset_id in ids:
            render_material(asset_id, out_root, proof)
    return 0


if __name__ == "__main__":
    code = main()
    sys.stdout.flush()
    if code:
        sys.exit(code)
