"""A small expression layer over Cycles shader nodes, so a material reads as maths instead of wiring.

Extracted from _blender_bake_world_materials.py (the most complete copy: MapRange smoothstep, Mix nodes, constant
folding, periodic torus noise, periodic Voronoi cells) and reconciled with the Val/Graph/Ctx wrappers of
_procgen_cart.py, _procgen_chest.py and _procgen_crate.py (4D object-space noise, Voronoi with cell colour, geometry
attributes, the bake surface context).

API
    lin(r, g, b) or lin((r, g, b)) -> Col, a linear rgb tuple from sRGB 0-255 that scales by numbers
    Val                         a node output socket with a kind, 'f' float or 'v' vector/colour; + - * / and unary -
    Graph(tree)                 builds into a node tree
      plumbing   node(type, **props), put(socket, value), m(op, a, b, c, clamp), vm(op, a, b, c, scale, out)
      scalar     floor fract mod abs min max sqrt pow sin cos atan2 lt gt sat
      ranges     smooth(e0, e1, x) (e0 > e1 falls), lstep(e0, e1, x), mix(a, b, t) float or vector
      vectors    vec(x, y, z), sep(v), length(v), dot(a, b), normalize(v), muladd(a, b, c)
      colour     ramp(t, stops, interp='LINEAR', space='auto')  stops (pos, colour); colour sRGB 0-255 or linear
      noise      noise(v, w=0, detail, rough, dims='4D'), n3(v, ...), voronoi(v, scale, jitter, dims, feature)
                 -> (distance, cell colour), white(v), hash(kx, ky, seed)
      geometry   attr(name) float, attrv(name) vector, geometry() -> (position, normal), ao(distance), bevel_normal(r)
      periodic   (tileable UV materials, as the world baker uses) U, V, base, torus(U, V), tor, n4, z4, warp,
                 cells(U, V, N, M, T, ...) -> Cells, rotate(cells, angle), straws(T, N, seed, chance)
    Ctx, surface_context(g, ao_distance=0.35, bevel_radius=0.004) -> Ctx
      the per-texel context every material recipe reads: pos, nrm, pz (bake-lift corrected), nz, ao, edge, cvx
      (exposed convex edges), low (0 at the ground, 1 from 0.3 m up), and the member attributes the mesh builder
      writes (meshbuild.ATTRIBUTES): mpos/lx/ly/lz member-frame position (x along the grain), py0/pz0/ringf pith line
      and ring-width factor, sy/sz/kden pith drift and knot density, rand, pend (end grain), wear.
"""
import math
import random

TAU = 2.0 * math.pi

# FBM 4D noise standard deviation by detail, measured on 1024 px bakes (normalised FBM, rough 0.5).
NOISE_SIGMA = {0: 0.121, 1: 0.090, 2: 0.079, 3: 0.075, 4: 0.072, 5: 0.071, 6: 0.070, 7: 0.070, 8: 0.070}


class Col(tuple):
    """A constant colour that scales by a plain number (a tuple would repeat); anything else defers to Val."""

    def __mul__(self, k):
        if isinstance(k, (int, float)) and not isinstance(k, bool):
            return Col(c * k for c in self)
        return NotImplemented

    __rmul__ = __mul__


def lin(*rgb):
    """sRGB 0-255 to linear, for colour constants: lin(r, g, b) or lin((r, g, b))."""
    if len(rgb) == 1:
        rgb = tuple(rgb[0])
    out = []
    for x in rgb:
        x = x / 255.0
        out.append(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4)
    return Col(out)


def kind(x):
    if isinstance(x, Val):
        return x.k
    if isinstance(x, (tuple, list)):
        return "v"
    return "f"


def sid(sockets, identifier):
    for socket in sockets:
        if socket.identifier == identifier:
            return socket
    raise KeyError(identifier)


class Val:
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
        self._uv = None

    # ---- plumbing
    def node(self, kind_name, **props):
        n = self.tree.nodes.new(kind_name)
        for key, value in props.items():
            setattr(n, key, value)
        n.location = ((self.count % 60) * 180.0, -(self.count // 60) * 280.0)
        self.count += 1
        return n

    def put(self, socket, value):
        if isinstance(value, Val):
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
        return Val(self, n.outputs[0], "f")

    def vm(self, op, a, b=(0.0, 0.0, 0.0), c=(0.0, 0.0, 0.0), scale=1.0, out=0):
        n = self.node("ShaderNodeVectorMath", operation=op)
        self.put(n.inputs[0], a)
        self.put(n.inputs[1], b)
        self.put(n.inputs[2], c)
        self.put(n.inputs[3], scale)
        return Val(self, n.outputs[out], "v" if out == 0 else "f")

    def op2(self, op, a, b):
        ka, kb = kind(a), kind(b)
        if not isinstance(a, Val) and not isinstance(b, Val):        # constants fold
            fa = tuple(a) if ka == "v" else (a,) * (3 if kb == "v" else 1)
            fb = tuple(b) if kb == "v" else (b,) * len(fa)
            fn = {"ADD": lambda x, y: x + y, "SUBTRACT": lambda x, y: x - y, "MULTIPLY": lambda x, y: x * y,
                  "DIVIDE": lambda x, y: x / y}[op]
            out = tuple(fn(x, y) for x, y in zip(fa, fb))
            return out[0] if len(out) == 1 else Col(out)
        if ka == "f" and kb == "f":
            return self.m(op, a, b)
        if op == "MULTIPLY" and "f" in (ka, kb):
            vec, sc = (a, b) if ka == "v" else (b, a)
            return self.vm("SCALE", vec, scale=sc)
        if op == "DIVIDE" and kb == "f":
            return self.vm("SCALE", a, scale=(1.0 / b) if not isinstance(b, Val) else self.m("DIVIDE", 1.0, b))
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
    def atan2(self, y, x): return self.m("ARCTAN2", y, x)
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
        return Val(self, n.outputs[0], "f")

    def smooth(self, e0, e1, x):
        """Smoothstep from e0 to e1; with constant edges e0 > e1 gives the falling step (1 below e1, 0 above e0)."""
        if not isinstance(e0, Val) and not isinstance(e1, Val) and e0 > e1:
            return 1.0 - self._range("SMOOTHSTEP", e1, e0, x)
        return self._range("SMOOTHSTEP", e0, e1, x)

    def lstep(self, e0, e1, x):
        if not isinstance(e0, Val) and not isinstance(e1, Val) and e0 > e1:
            return 1.0 - self._range("LINEAR", e1, e0, x)
        return self._range("LINEAR", e0, e1, x)

    def mix(self, a, b, t):
        """a + (b - a) t with t clamped to 0..1; floats or vectors/colours."""
        if kind(a) == "f" and kind(b) == "f":
            n = self.node("ShaderNodeMix", data_type="FLOAT", clamp_factor=True)
            self.put(sid(n.inputs, "Factor_Float"), t)
            self.put(sid(n.inputs, "A_Float"), a)
            self.put(sid(n.inputs, "B_Float"), b)
            return Val(self, sid(n.outputs, "Result_Float"), "f")
        n = self.node("ShaderNodeMix", data_type="VECTOR", factor_mode="UNIFORM", clamp_factor=True)
        self.put(sid(n.inputs, "Factor_Float"), t)
        self.put(sid(n.inputs, "A_Vector"), a)
        self.put(sid(n.inputs, "B_Vector"), b)
        return Val(self, sid(n.outputs, "Result_Vector"), "v")

    # ---- vector helpers
    def vec(self, x, y, z=0.0):
        n = self.node("ShaderNodeCombineXYZ")
        self.put(n.inputs[0], x)
        self.put(n.inputs[1], y)
        self.put(n.inputs[2], z)
        return Val(self, n.outputs[0], "v")

    def sep(self, v):
        n = self.node("ShaderNodeSeparateXYZ")
        self.put(n.inputs[0], v)
        return tuple(Val(self, n.outputs[i], "f") for i in range(3))

    def length(self, v): return self.vm("LENGTH", v, out=1)
    def dot(self, a, b): return self.vm("DOT_PRODUCT", a, b, out=1)
    def normalize(self, v): return self.vm("NORMALIZE", v)
    def muladd(self, a, b, c): return self.vm("MULTIPLY_ADD", a, b, c)

    def ramp(self, t, stops, interp="LINEAR", space="auto"):
        """Colour ramp over t; stops are (position, colour). space: 'srgb255' (0-255 sRGB, converted), 'linear'
        (0-1 floats as given) or 'auto' (sRGB 0-255 when any component exceeds 1)."""
        stops = sorted(stops, key=lambda s: s[0])
        if space == "auto":
            space = "srgb255" if any(c > 1.0 for _, col in stops for c in col) else "linear"
        conv = lin if space == "srgb255" else (lambda c: tuple(float(x) for x in c))
        n = self.node("ShaderNodeValToRGB")
        cr = n.color_ramp
        cr.interpolation = interp
        cr.elements[0].position = stops[0][0]
        cr.elements[0].color = conv(stops[0][1]) + (1.0,)
        cr.elements[1].position = stops[-1][0]
        cr.elements[1].color = conv(stops[-1][1]) + (1.0,)
        for position, colour in stops[1:-1]:
            e = cr.elements.new(position)
            e.color = conv(colour) + (1.0,)
        self.put(n.inputs[0], t)
        return Val(self, n.outputs[0], "v")

    # ---- noise
    def white(self, v):
        n = self.node("ShaderNodeTexWhiteNoise", noise_dimensions="3D")
        self.put(n.inputs["Vector"], v)
        return Val(self, n.outputs["Color"], "v")

    def hash(self, kx, ky, seed):
        """Three independent uniform values for integer cell keys (keys must already be modded)."""
        return self.sep(self.white(self.vec(kx, ky, float(seed))))

    def noise(self, v, w=0.0, detail=2.0, rough=0.5, dims="4D", lac=2.0, dist=0.0, color=False):
        """Object-space fBm noise, normalised to about [0, 1] (mean 0.5). v is already scaled."""
        n = self.node("ShaderNodeTexNoise", noise_dimensions=dims, noise_type="FBM", normalize=True)
        self.put(n.inputs["Vector"], v)
        if dims == "4D":
            self.put(n.inputs["W"], w)
        self.put(n.inputs["Scale"], 1.0)
        self.put(n.inputs["Detail"], detail)
        self.put(n.inputs["Roughness"], rough)
        self.put(n.inputs["Lacunarity"], lac)
        self.put(n.inputs["Distortion"], dist)
        return Val(self, n.outputs[1], "v") if color else Val(self, n.outputs[0], "f")

    def n3(self, p, detail=2.0, rough=0.5):
        return self.noise(p, detail=detail, rough=rough, dims="3D")

    def voronoi(self, v, scale=1.0, jitter=1.0, dims="3D", feature="F1"):
        """Distance (in the scaled space) and the cell's random colour."""
        n = self.node("ShaderNodeTexVoronoi", voronoi_dimensions=dims, feature=feature)
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["Scale"], scale)
        self.put(n.inputs["Randomness"], jitter)
        colour = n.outputs.get("Color")          # DISTANCE_TO_EDGE / N_SPHERE_RADIUS have no cell colour
        return Val(self, n.outputs["Distance"], "f"), (Val(self, colour, "v") if colour is not None else None)

    # ---- geometry
    def attr(self, name):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Fac"] if "Fac" in n.outputs else n.outputs["Factor"], "f")

    def attrv(self, name):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Vector"], "v")

    def geometry(self):
        geo = self.node("ShaderNodeNewGeometry")
        return Val(self, geo.outputs["Position"], "v"), Val(self, geo.outputs["Normal"], "v")

    def ao(self, distance, samples=16):
        n = self.node("ShaderNodeAmbientOcclusion", samples=samples, only_local=False, inside=False)
        self.put(n.inputs["Distance"], distance)
        return Val(self, n.outputs["AO"], "f")

    def bevel_normal(self, radius, samples=8):
        n = self.node("ShaderNodeBevel", samples=samples)
        self.put(n.inputs["Radius"], radius)
        return Val(self, n.outputs["Normal"], "v")

    # ---- periodic noise on the flat torus (tileable UV materials)
    def _ensure_uv(self):
        if self._uv is None:
            tc = self.node("ShaderNodeTexCoord")
            offset = self.node("ShaderNodeCombineXYZ")
            offset.name = "UV_OFFSET"
            uv = self.vm("ADD", Val(self, tc.outputs["UV"], "v"), Val(self, offset.outputs[0], "v"))
            u, v, _ = self.sep(uv)
            self._uv = (u, v, self.torus(u, v))
        return self._uv

    @property
    def U(self): return self._ensure_uv()[0]

    @property
    def V(self): return self._ensure_uv()[1]

    @property
    def base(self): return self._ensure_uv()[2]

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

    def n4(self, fu, fv=None, seed=0, detail=2.0, rough=0.5, lac=2.0, dist=0.0, base=None, off=None, color=False):
        """Periodic fBm noise, normalised to about [0, 1] (mean 0.5)."""
        p, w = self.tor(fu, fv, seed, base, off)
        return self.noise(p, w, detail, rough, "4D", lac, dist, color)

    def z4(self, fu, fv=None, seed=0, detail=2.0, **kw):
        """Periodic noise rescaled to roughly zero mean, unit standard deviation."""
        sigma = NOISE_SIGMA.get(int(round(detail)), 0.075)
        return (self.n4(fu, fv, seed, detail, **kw) - 0.5) * (1.0 / sigma)

    def warp(self, amount, fu, seed, detail=3.0):
        """Displace (U, V) by periodic noise; the result is still periodic in U and V."""
        a = self.z4(fu, seed=seed, detail=detail)
        b = self.z4(fu, seed=seed + 1, detail=detail)
        return self.U + a * amount, self.V + b * amount

    def cells(self, U, V, N, M, T, jx=1.0, jy=None, seed=0, edge=True):
        """Periodic 2D Voronoi on an N x M jittered lattice over a tile of T metres: F1 (m), vector to the nearest
        point (m), its 3 hash values, and distance to its edge (m). Point (i, j) is hashed from (i mod N, j mod M)."""
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


class Ctx:
    """Per-texel values a material recipe reads (see surface_context)."""


def surface_context(g, ao_distance=0.35, bevel_radius=0.004):
    """The bake context: world position/normal, occlusion, exposed edges, height above the ground, and the member
    attributes written by meshbuild.make_object (all optional on the mesh: a missing attribute reads 0)."""
    c = Ctx()
    c.pos, c.nrm = g.geometry()
    c.mpos = g.attrv("mpos")
    c.lx, c.ly, c.lz = g.sep(c.mpos)
    c.py0, c.pz0, c.ringf = g.sep(g.attrv("pgrain"))
    c.sy, c.sz, c.kden = g.sep(g.attrv("pslope"))
    c.rand = g.attr("prand")
    c.pend = g.attr("pend")
    c.wear = g.attr("pwear")
    _, _, pz = g.sep(c.pos)
    c.pz = pz - g.attr("pzoff")
    _, _, c.nz = g.sep(c.nrm)
    c.ao = g.ao(ao_distance)
    c.edge = g.smooth(0.992, 0.93, g.dot(g.bevel_normal(bevel_radius), c.nrm))
    c.cvx = c.edge * g.smooth(0.55, 0.9, c.ao)        # convex (exposed) edges only
    c.low = g.smooth(0.0, 0.30, c.pz)                  # 0 at the ground, 1 from 0.3 m up
    return c
