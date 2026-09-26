"""Geometry helpers for the animated-armour v2 build (run inside Blender).

Every plate is a parametric patch fn(s, t) -> world point (rest pose), turned into a bmesh grid, oriented so its
normals face away from an inside reference, then given thickness (Solidify, inner shell -> void material) and,
where asked, a rolled rim (a tube swept along the plate's boundary).
"""
import math
import bmesh
import bpy
from mathutils import Vector

Z = Vector((0.0, 0.0, 1.0))


def smoothstep(e0, e1, x):
    t = min(max((x - e0) / (e1 - e0), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def cr(knots, x):
    """Catmull-Rom through (x, (values...)) knots, clamped at the ends."""
    xs = [k[0] for k in knots]
    if x <= xs[0]:
        return knots[0][1]
    if x >= xs[-1]:
        return knots[-1][1]
    i = max(j for j in range(len(xs) - 1) if xs[j] <= x)
    t = (x - xs[i]) / (xs[i + 1] - xs[i])
    p0 = knots[max(i - 1, 0)][1]
    p1 = knots[i][1]
    p2 = knots[i + 1][1]
    p3 = knots[min(i + 2, len(knots) - 1)][1]
    return tuple(0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t ** 3)
                 for a, b, c, d in zip(p0, p1, p2, p3))


def sect(theta, side_pos, front, back, p=2.0, side_neg=None):
    """Superellipse section; theta 0 = front (+y), +90deg = +x side. Returns (x, y)."""
    s, c = math.sin(theta), math.cos(theta)
    a = side_pos if s >= 0 or side_neg is None else side_neg
    x = a * math.copysign(abs(s) ** (2.0 / p), s)
    y = (front if c >= 0 else back) * math.copysign(abs(c) ** (2.0 / p), c)
    return x, y


class Frame:
    """origin + x*X + y*Y + z*A  (X lateral/outward, Y front, A along)."""

    def __init__(self, origin, X, Y, A):
        self.o, self.X, self.Y, self.A = origin.copy(), X.normalized(), Y.normalized(), A.normalized()

    def p(self, x, y, z):
        return self.o + self.X * x + self.Y * y + self.A * z


def limb_frame(head, tail, front_hint, outward_hint):
    A = (tail - head).normalized()
    Y = (front_hint - A * front_hint.dot(A)).normalized()
    X = Y.cross(A).normalized()
    if X.dot(outward_hint) < 0:
        X = -X
    return Frame(head, X, Y, A), (tail - head).length


# ---------------------------------------------------------------- patches

class Patch:
    def __init__(self, fn, ns, nt, closed_s=False):
        self.fn, self.ns, self.nt, self.closed = fn, ns, nt, closed_s
        self.sign = 1.0

    def raw_normal(self, s, t, eps=1e-3):
        if self.closed:
            ds = self.fn((s + eps) % 1.0, t) - self.fn((s - eps) % 1.0, t)
        else:
            ds = self.fn(min(s + eps, 1.0), t) - self.fn(max(s - eps, 0.0), t)
        dt = self.fn(s, min(t + eps, 1.0)) - self.fn(s, max(t - eps, 0.0))
        n = ds.cross(dt)
        return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))

    def normal_at(self, s, t):
        return self.raw_normal(s, t) * self.sign

    def calibrate(self, inside):
        """Set sign so normal_at points away from inside (point or closest-point function)."""
        score = 0.0
        for i in range(1, 8):
            for j in range(1, 8):
                s, t = i / 8, j / 8
                p = self.fn(s, t)
                ref = inside(p) if callable(inside) else inside
                score += self.raw_normal(s, t).dot(p - ref)
        self.sign = 1.0 if score >= 0 else -1.0
        return self


def build_patch(bm, patch, skip=None):
    """Grid of quads. skip(s_mid, t_mid) -> True leaves that face out (holes). Returns rows of BMVerts."""
    cols = patch.ns if patch.closed else patch.ns + 1
    rows = []
    for j in range(patch.nt + 1):
        t = j / patch.nt
        rows.append([bm.verts.new(patch.fn(i / patch.ns, t)) for i in range(cols)])
    for j in range(patch.nt):
        for i in range(patch.ns):
            i2 = (i + 1) % cols if patch.closed else i + 1
            if skip and skip((i + 0.5) / patch.ns, (j + 0.5) / patch.nt):
                continue
            bm.faces.new((rows[j][i], rows[j][i2], rows[j + 1][i2], rows[j + 1][i]))
    return rows


def orient(bm, inside):
    """Flip the whole bmesh if most face normals point toward `inside(p)` (a point or closest-point function)."""
    bm.normal_update()
    score = 0.0
    for f in bm.faces:
        c = f.calc_center_median()
        ref = inside(c) if callable(inside) else inside
        score += f.normal.dot(c - ref) * f.calc_area()
    if score < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
        bm.normal_update()
    return score


def seg_closest(a, b):
    ab = b - a
    L2 = ab.length_squared

    def f(p):
        t = 0.0 if L2 == 0 else min(max((p - a).dot(ab) / L2, 0.0), 1.0)
        return a + ab * t
    return f


# ---------------------------------------------------------------- tubes, rivets, bosses

def sweep(bm, pts, normals, radius, sides=5, closed=False, cap=True):
    """Tube along pts; normals give the section orientation. Returns created faces."""
    n = len(pts)
    uvl = bm.loops.layers.uv.verify()
    pl = bm.faces.layers.int.get("preuv") or bm.faces.layers.int.new("preuv")
    rings = []
    for i, p in enumerate(pts):
        if closed:
            tan = pts[(i + 1) % n] - pts[(i - 1) % n]
        else:
            tan = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tan.normalize()
        nn = normals[i] - tan * normals[i].dot(tan)
        if nn.length < 1e-6:
            nn = tan.orthogonal()
        nn.normalize()
        bn = tan.cross(nn)
        r = radius[i] if isinstance(radius, (list, tuple)) else radius
        rings.append([bm.verts.new(p + (nn * math.cos(2 * math.pi * k / sides) + bn * math.sin(2 * math.pi * k / sides)) * r)
                      for k in range(sides)])
    faces = []
    cum = [0.0]
    for i in range(1, n):
        cum.append(cum[-1] + (pts[i] - pts[i - 1]).length)
    if closed:
        cum.append(cum[-1] + (pts[0] - pts[-1]).length)
    UL = 2.5
    segs = n if closed else n - 1
    for i in range(segs):
        a, b = rings[i], rings[(i + 1) % n]
        u0, u1 = min(cum[i] / UL, 1.0), min(cum[i + 1] / UL, 1.0)
        for k in range(sides):
            k2 = (k + 1) % sides
            f = bm.faces.new((a[k], b[k], b[k2], a[k2]))
            for l, uv in zip(f.loops, ((u0, k / sides), (u1, k / sides), (u1, (k + 1) / sides), (u0, (k + 1) / sides))):
                l[uvl].uv = uv
            f[pl] = 1
            faces.append(f)
    if not closed and cap:
        for ring in (list(reversed(rings[0])), rings[-1]):
            f = bm.faces.new(ring)
            for l in f.loops:
                l[uvl].uv = (0.0, 0.5)
            f[pl] = 1
            faces.append(f)
    return faces


def dome(bm, center, normal, radius, height, segs=8, rings=2):
    """Low dome (rivet / boss) sitting on a surface. Returns faces."""
    normal = normal.normalized()
    t1 = normal.orthogonal().normalized()
    t2 = normal.cross(t1)
    base = center - normal * (height * 0.35)
    # layers first: adding a layer invalidates face references made before it
    uvl = bm.loops.layers.uv.verify()
    pl = bm.faces.layers.int.get("preuv") or bm.faces.layers.int.new("preuv")
    verts_rings = []
    for r in range(rings):
        ang = (math.pi / 2) * r / rings
        rr = radius * math.cos(ang)
        hh = height * math.sin(ang)
        verts_rings.append([bm.verts.new(base + (t1 * math.cos(2 * math.pi * k / segs) + t2 * math.sin(2 * math.pi * k / segs)) * rr + normal * hh)
                            for k in range(segs)])
    apex = bm.verts.new(base + normal * height)
    faces = []
    for r in range(rings - 1):
        a, b = verts_rings[r], verts_rings[r + 1]
        for k in range(segs):
            k2 = (k + 1) % segs
            faces.append(bm.faces.new((a[k], a[k2], b[k2], b[k])))
    last = verts_rings[-1]
    for k in range(segs):
        faces.append(bm.faces.new((last[k], last[(k + 1) % segs], apex)))
    for f in faces:
        f[pl] = 2
        for l in f.loops:
            d = l.vert.co - base
            l[uvl].uv = (0.5 + 0.48 * d.dot(t1) / radius, 0.5 + 0.48 * d.dot(t2) / radius)
    return faces


# ---------------------------------------------------------------- objects

MATS = {}


def mat(name, color):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    m.use_nodes = True
    m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1.0)
    MATS[name] = m
    return m


def to_object(name, bm, bone, material, void, thickness=0.0, rims=None, extra=None, edge=None):
    """bm (surface, outward normals) -> object with thickness; rims: list of (pts, normals, closed) sweeps added after.
    extra: list of (bm_builder(bm) -> faces, material) pieces added without thickness (rivets, bosses)."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    me.materials.append(material)
    me.materials.append(void)
    me.materials.append(edge or material)
    for p in me.polygons:
        p.material_index = 0
    if thickness > 0:
        mod = ob.modifiers.new("solid", "SOLIDIFY")
        mod.thickness = thickness
        mod.offset = -1.0
        mod.use_even_offset = True
        mod.use_rim = True
        mod.material_offset = 1
        mod.material_offset_rim = 2
        dg = bpy.context.evaluated_depsgraph_get()
        new = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        ob.modifiers.clear()
        old = ob.data
        ob.data = new
        bpy.data.meshes.remove(old)
        new.name = name
    ob["bone"] = bone
    return ob


def add_parts(ob, builders):
    """Append extra geometry (built by functions taking a bmesh) to object with per-builder material."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for build, material in builders:
        if material.name not in [m.name for m in me.materials]:
            me.materials.append(material)
        idx = [m.name for m in me.materials].index(material.name)
        faces = build(bm)
        for f in faces:
            f.material_index = idx
    bm.to_mesh(me)
    bm.free()


def rim_specs(patch, which, thickness, extra_r=0.0035, sides=5):
    """Sweep specs (pts, normals, closed, r, sides) for the patch boundary: which in {'loop','t0','t1','s0','s1'}."""
    specs = []
    cols = patch.ns if patch.closed else patch.ns + 1
    nt = patch.nt

    def pt_n(i, j):
        s, t = i / patch.ns, j / nt
        return patch.fn(s, t), patch.normal_at(s, t)
    seqs = []
    if patch.closed:
        if "t0" in which or "loop" in which:
            seqs.append(([(i, 0) for i in range(cols)], True))
        if "t1" in which or "loop" in which:
            seqs.append(([(i, nt) for i in range(cols)], True))
    else:
        if "loop" in which:
            idx = [(i, 0) for i in range(cols)] + [(cols - 1, j) for j in range(1, nt + 1)] + \
                  [(i, nt) for i in range(cols - 2, -1, -1)] + [(0, j) for j in range(nt - 1, 0, -1)]
            seqs.append((idx, True))
        else:
            if "t0" in which:
                seqs.append(([(i, 0) for i in range(cols)], False))
            if "t1" in which:
                seqs.append(([(i, nt) for i in range(cols)], False))
            if "s0" in which:
                seqs.append(([(0, j) for j in range(nt + 1)], False))
            if "s1" in which:
                seqs.append(([(cols - 1, j) for j in range(nt + 1)], False))
    for idx, closed in seqs:
        pts, nrm = [], []
        for i, j in idx:
            p, n = pt_n(i, j)
            pts.append(p - n * thickness * 0.5)
            nrm.append(n)
        specs.append((pts, nrm, closed, thickness * 0.5 + extra_r, sides))
    return specs


def rim_builder(specs, orient_fn=None):
    def build(bm):
        faces = []
        for pts, nrm, closed, r, sides in specs:
            faces += sweep(bm, pts, nrm, r, sides=sides, closed=closed)
        return faces
    return build


def rivet_builder(patch, placements, radius=0.0065, height=0.0045, segs=6, sign=1.0):
    """placements: list of (s, t). Rivets sit on the outer surface."""
    def build(bm):
        faces = []
        for s, t in placements:
            p = patch.fn(s, t)
            n = patch.normal_at(s, t) * sign
            faces += dome(bm, p, n, radius, height, segs=segs, rings=1)
        return faces
    return build


def boss_builder(items):
    """items: list of (point, normal, radius, height)."""
    def build(bm):
        faces = []
        for p, n, r, h in items:
            faces += dome(bm, p, n, r, h, segs=10, rings=3)
        return faces
    return build
