"""Parametric closed-solid primitives on a meshbuild.Builder, each unwrapped in its own member frame.

Extracted and unified from _procgen_chest.py (loft, chamfered rect profile and true inset, beam, prism with hole,
lathe, rivet, band_loop), _procgen_cart.py (sweep with broken/splintered ends, member from p0 to p1, lathe, ring,
poly_sweep, catmull, tube, rivet, band_around) and _procgen_crate.py (nail heads with tilt).

Conventions: every section/profile is CCW about the direction it is swept along, so faces wind outward; the
Builder's close() turns a part that still comes out inside-out. UV islands are in metres of the member frame:
sides are one strip per part (u along the member, v round the section, right-handed so the tangent frame the engine
derives matches the baked normal), each end cap its own island. End caps of wood carry end=True (end grain).

API (points and axes are 3-sequences; frames are 4x4 numpy matrices from frame_from/meshbuild.frame_matrix)
    frame_from(origin, x_axis, z_hint) -> 4x4        right-handed member frame, x along the member
    normal_to(T, up) -> (N, B)
    rect_section(h, w, c=0, nt=1, ns=1), chamfer_rect(hy, hz, cs), rect_inset(hy, hz, cs, e), circle_section(r, n)
    offset_poly(poly, e), ccw(poly), poly_area(poly)
    loft(b, rings, us, closed=False, cap_axes=(Y, Z), end_caps=False, cap_texel=None)
    beam(b, name, x0, x1, hy, hz, frame, cs=0.0015, ce=0.0012, group, slot, grain=True, cap_texel=None, wear=0)
    member(b, name, p0, p1, up, h, w, ...) -> a beam from p0 to p1, h along `up`
    plank(b, name, centre, along, normal, length, width, thickness, ...) -> a board, grain along `along`
    prism(b, name, frame, poly, t, c, group, slot, hole=None)     polygon extruded along local z, chamfered
    lathe(b, name, C, A, profile, n=12, group, slot, phase=0)     profile [(u along A, radius > 0)]
    sweep(b, name, sec, frames, group, slot, closed=False, caps=('flat','flat'), jags=(None, None), cap_slot=None)
    tube(b, name, pts, r, n=8, ...), catmull(points, step=0.02)  round tube on parallel-transported frames
    poly_sweep(b, name, pts, bend_axis, h, w, c=0, closed=False, ...)  flat bar bent along a polyline (mitred)
    timber(b, name, p0, p1, up, h, w, c, ends, taper, bow, nseg, jag, ..., cap_slot)  bowed/tapered/broken beam
    band_loop(b, name, centre, hx, hy, z0, z1, t, c=0.0008, ...)  iron band round a box (half sizes hx, hy)
    band_around(b, name, centre, axis, half_a, half_b, dir_a, t, w, ...)  band round a rectangular member
    ring(b, name, C, E1, E2, radius, h, w, c=0, a0=0, a1=TAU, n=48, ...)   sweep along a circle or arc
    rivet(b, p, normal, r, h, n=8, sink=0.0008, ...), nail(b, p, normal, rng, r, h, ...)
    splinter_profile(rng, n, jag)
"""
import math

import numpy as np

TAU = 2.0 * math.pi
X, Y, Z = np.eye(3)


def _v(p):
    return np.asarray(p, np.float64)


def _unit(v):
    v = _v(v)
    return v / np.linalg.norm(v)


def normal_to(T, up):
    """N perpendicular to T, as close to `up` as possible, and B = T x N (T, N, B right-handed)."""
    T, up = _unit(T), _v(up)
    N = up - T * up.dot(T)
    if np.linalg.norm(N) < 1e-8:
        alt = X if abs(T[0]) < 0.9 else Y
        N = alt - T * alt.dot(T)
    N = N / np.linalg.norm(N)
    return N, np.cross(T, N)


def frame_from(origin, x_axis, z_hint):
    """4x4 member frame: x along x_axis, z as close to z_hint as possible, y = z x x."""
    x = _unit(x_axis)
    z, _ = normal_to(x, z_hint)
    y = np.cross(z, x)
    M = np.eye(4)
    M[:3, 0], M[:3, 1], M[:3, 2], M[:3, 3] = x, y, z, _v(origin)
    return M


# ------------------------------------------------------------------------------------------------ sections

def chamfer_rect(hy, hz, cs):
    """Chamfered rectangle in (y, z), CCW seen from +x. cs = chamfer or (+y+z, -y+z, -y-z, +y-z)."""
    cpp, cmp_, cmm, cpm = (cs,) * 4 if np.isscalar(cs) else cs
    return [(hy, -hz + cpm), (hy, hz - cpp), (hy - cpp, hz), (-hy + cmp_, hz), (-hy, hz - cmp_), (-hy, -hz + cmm),
            (-hy + cmm, -hz), (hy - cpm, -hz)]


def rect_inset(hy, hz, cs, e):
    """chamfer_rect shrunk by e on every side, same vertex count; each chamfer shrinks by (2 - sqrt 2) e (a true
    offset) and stops at 0.3 mm, so a narrow chamfer never turns inside out."""
    cs = (cs,) * 4 if np.isscalar(cs) else cs
    k = (2.0 - math.sqrt(2.0)) * e
    return chamfer_rect(hy - e, hz - e, [max(c - k, 0.0003) for c in cs])


def rect_section(h, w, c=0.0, nt=1, ns=1):
    """Chamfered rectangle for sweeps, CCW seen from +T: s along N (height h), t along B (width w); nt/ns subdivide
    the sides (broken ends need vertices to splinter)."""
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


def poly_area(poly):
    return 0.5 * sum(poly[i - 1][0] * poly[i][1] - poly[i][0] * poly[i - 1][1] for i in range(len(poly)))


def ccw(poly):
    return list(poly) if poly_area(poly) > 0 else list(reversed(poly))


def offset_poly(poly, e):
    """Offset a CCW polygon inward by e (outward for e < 0): each vertex where its two offset edge lines meet."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = _v(poly[i - 1]), _v(poly[i]), _v(poly[(i + 1) % n])
        d1, d2 = _unit(p1 - p0), _unit(p2 - p1)
        n1, n2 = np.array([-d1[1], d1[0]]), np.array([-d2[1], d2[0]])
        a, c = p1 + n1 * e, p1 + n2 * e
        den = d1[0] * d2[1] - d1[1] * d2[0]
        if abs(den) < 1e-9:
            out.append((a[0], a[1]))
            continue
        w = c - a
        t = (w[0] * d2[1] - w[1] * d2[0]) / den
        q = a + d1 * t
        out.append((q[0], q[1]))
    return out


# ------------------------------------------------------------------------------------------------ loft

def _arc(ring):
    acc = [0.0]
    for j in range(len(ring)):
        acc.append(acc[-1] + float(np.linalg.norm(ring[(j + 1) % len(ring)] - ring[j])))
    return acc


def loft(b, rings, us, closed=False, cap_axes=(Y, Z), end_caps=False, cap_texel=None):
    """Quads between consecutive rings (member-frame points, each ring CCW about the loft direction) and flat n-gon
    caps. Island 'side': (u along the loft, -arc round the ring); caps 'cap0'/'cap1' projected on cap_axes (a, c),
    which must satisfy a x c = the loft direction."""
    rings = [[_v(p) for p in r] for r in rings]
    n, m = len(rings[0]), len(rings)
    idx = [[b.v(p) for p in ring] for ring in rings]
    vc = [_arc(r) for r in rings]
    total = us[-1] + float(np.linalg.norm(rings[0][0] - rings[-1][0])) if closed else None
    for i in range(m if closed else m - 1):
        i1 = (i + 1) % m
        u0, u1 = us[i], (total if (closed and i1 == 0) else us[i1])
        for j in range(n):
            j1 = (j + 1) % n
            b.f((idx[i][j], idx[i][j1], idx[i1][j1], idx[i1][j]),
                [(u0, -vc[i][j]), (u0, -vc[i][j + 1]), (u1, -vc[i1][j + 1]), (u1, -vc[i1][j])], island="side")
    if closed:
        return
    a, c = _v(cap_axes[0]), _v(cap_axes[1])
    order = list(reversed(range(n)))
    b.f([idx[0][j] for j in order], [(-rings[0][j].dot(a), rings[0][j].dot(c)) for j in order],
        island="cap0", end=end_caps, texel=cap_texel)
    b.f([idx[-1][j] for j in range(n)], [(rings[-1][j].dot(a), rings[-1][j].dot(c)) for j in range(n)],
        island="cap1", end=end_caps, texel=cap_texel)


def beam(b, name, x0, x1, hy, hz, frame, cs=0.0015, ce=0.0012, group="body", slot=0, grain=True, cap_texel=None,
         wear=0.0, texel=1.0):
    """Board or bar along member x from x0 to x1, section 2hy (y) x 2hz (z), every long edge chamfered by cs and
    both ends by ce. grain=True gives it a wood pith line (see meshbuild.wood_grain) and end-grain caps."""
    b.part(name, frame, group=group, slot=slot, grain=(hy, hz) if grain else None, wear=wear, texel=texel)
    full = chamfer_rect(hy, hz, cs)
    if ce > 0:
        ins = rect_inset(hy, hz, cs, ce)
        xs = [(x0, ins), (x0 + ce, full), (x1 - ce, full), (x1, ins)]
    else:
        xs = [(x0, full), (x1, full)]
    loft(b, [[(x, y, z) for y, z in prof] for x, prof in xs], [x for x, _ in xs], cap_axes=(Y, Z),
         end_caps=grain, cap_texel=cap_texel)
    b.close()
    return b.current


def member(b, name, p0, p1, up, h, w, **kw):
    """A beam from p0 to p1; section h along `up`, w across."""
    p0, p1 = _v(p0), _v(p1)
    L = float(np.linalg.norm(p1 - p0))
    return beam(b, name, 0.0, L, w / 2, h / 2, frame_from(p0, p1 - p0, up), **kw)


def plank(b, name, centre, along, normal, length, width, thickness, **kw):
    """A board centred at `centre`, grain along `along`, its broad face toward `normal`."""
    return beam(b, name, -length / 2, length / 2, width / 2, thickness / 2, frame_from(centre, along, normal), **kw)


def prism(b, name, frame, poly, t, c, group="body", slot=0, hole=None, grain=None, texel=1.0, wear=0.0):
    """A polygon in local (x, y) extruded along local z from -t/2 to t/2, its perimeter chamfered by c on both faces.
    `hole` (same vertex count) cuts a through-hole whose caps join the outer ring quad by quad."""
    poly = ccw(poly)
    b.part(name, frame, group=group, slot=slot, grain=grain, texel=texel, wear=wear)
    zs = [(-t / 2, c), (-t / 2 + c, 0.0), (t / 2 - c, 0.0), (t / 2, c)] if c > 0 else [(-t / 2, 0.0), (t / 2, 0.0)]
    rings = [[(x, y, z) for x, y in offset_poly(poly, e)] for z, e in zs]
    us = [z for z, _ in zs]
    if hole is None:
        loft(b, rings, us, cap_axes=(X, Y))
        b.close()
        return b.current
    hole = ccw(hole)
    n = len(poly)
    assert len(hole) == n and c > 0
    hrings = [[_v((x, y, z)) for x, y in offset_poly(hole, -e)] for z, e in zs]
    rings = [[_v(p) for p in r] for r in rings]
    oi = [[b.v(p) for p in r] for r in rings]
    hi = [[b.v(p) for p in r] for r in hrings]
    ovc, hvc = [_arc(r) for r in rings], [_arc(r) for r in hrings]
    for i in range(3):
        for j in range(n):
            j1 = (j + 1) % n
            b.f((oi[i][j], oi[i][j1], oi[i + 1][j1], oi[i + 1][j]),
                [(us[i], -ovc[i][j]), (us[i], -ovc[i][j + 1]), (us[i + 1], -ovc[i + 1][j + 1]),
                 (us[i + 1], -ovc[i + 1][j])], island="side")
            b.f((hi[i][j1], hi[i][j], hi[i + 1][j], hi[i + 1][j1]),
                [(us[i], hvc[i][j + 1]), (us[i], hvc[i][j]), (us[i + 1], hvc[i + 1][j]), (us[i + 1], hvc[i + 1][j + 1])],
                island="hole")
    for j in range(n):
        j1 = (j + 1) % n
        for ro, rh, po, ph, bottom in ((oi[0], hi[0], rings[0], hrings[0], True), (oi[3], hi[3], rings[3], hrings[3], False)):
            q = (ro[j1], ro[j], rh[j], rh[j1]) if bottom else (ro[j], ro[j1], rh[j1], rh[j])
            pts = {ro[j]: po[j], ro[j1]: po[j1], rh[j]: ph[j], rh[j1]: ph[j1]}
            b.f(q, [((-1 if bottom else 1) * pts[k][0], pts[k][1]) for k in q], island="cap0" if bottom else "cap1")
    b.close()
    return b.current


def lathe(b, name, C, A, profile, n=12, group="body", slot=0, phase=0.0, texel=1.0, up=None, wear=0.0):
    """Solid of revolution about axis A from C; profile [(u along A, radius > 0)], flat caps at both ends."""
    assert all(r > 0 for _, r in profile), "lathe radii must be positive (a zero radius makes degenerate faces)"
    A = _unit(A)
    ref = up if up is not None else (Z if abs(A[2]) < 0.9 else X)
    b.part(name, frame_from(C, A, ref), group=group, slot=slot, texel=texel, wear=wear)
    rings = [[(u, r * math.cos(phase + TAU * k / n), r * math.sin(phase + TAU * k / n)) for k in range(n)]
             for u, r in profile]
    loft(b, rings, [u for u, _ in profile], cap_axes=(Y, Z))
    b.close()
    return b.current


# ------------------------------------------------------------------------------------------------ sweeps

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


def sweep(b, name, sec, frames, group="body", slot=0, closed=False, caps=("flat", "flat"), jags=(None, None),
          cap_slot=None, rec=0.0, texel=1.0, grain=None, wear=0.0):
    """Sweep a CCW section (s along N, t along B) through frames (P, T, N, B, ks, kt, u). Sides are one island
    (u along the path); flat ends are n-gon caps; a 'broken' end is a fan to a recessed centre with the ring pushed
    out into splinters, on cap_slot (a fresh-break material). Member coordinates are (u, s, t)."""
    b.part(name, None, group=group, slot=slot, texel=texel, grain=grain, wear=wear)
    n, m = len(sec), len(frames)
    rings = []
    for i, (P, T, N, B, ks, kt, u) in enumerate(frames):
        P, T, N, B = _v(P), _v(T), _v(N), _v(B)
        ring = []
        for j, (s, t) in enumerate(sec):
            d = 0.0
            if not closed and i == 0 and jags[0] is not None:
                d = -jags[0][j]
            if not closed and i == m - 1 and jags[1] is not None:
                d = jags[1][j]
            ring.append((P + N * (s * ks) + B * (t * kt) + T * d, (u + d, s * ks, t * kt)))
        rings.append(ring)
    idx = [[b.vw(p, mp) for p, mp in ring] for ring in rings]
    vcoord = [_arc([p for p, _ in ring]) for ring in rings]
    total_u = frames[-1][6] + float(np.linalg.norm(_v(frames[0][0]) - _v(frames[-1][0]))) if closed else 0.0
    for i in range(m if closed else m - 1):
        i1 = (i + 1) % m
        wrap = total_u if (closed and i1 == 0) else 0.0
        for j in range(n):
            j1 = (j + 1) % n
            uv = [(rings[i][j][1][0], -vcoord[i][j]), (rings[i][j1][1][0], -vcoord[i][j + 1]),
                  (rings[i1][j1][1][0] + wrap, -vcoord[i1][j + 1]), (rings[i1][j][1][0] + wrap, -vcoord[i1][j])]
            b.f((idx[i][j], idx[i][j1], idx[i1][j1], idx[i1][j]), uv, island="side")
    if not closed:
        for end in (0, 1):
            ring = idx[0] if end == 0 else idx[-1]
            P, T, N, B, ks, kt, u = frames[0] if end == 0 else frames[-1]
            planar = [((-1 if end == 0 else 1) * s * ks, t * kt) for s, t in sec]
            if caps[end] != "broken":
                order = list(range(n)) if end == 1 else list(reversed(range(n)))
                b.f([ring[j] for j in order], [planar[j] for j in order], island=f"cap{end}", end=grain is not None)
                continue
            hs = max(abs(s) for s, _ in sec) * ks
            ht = max(abs(t) for _, t in sec) * kt
            cs, ct = b.rng.uniform(-0.25, 0.25) * hs, b.rng.uniform(-0.25, 0.25) * ht
            depth = rec if rec else 0.3 * max(jags[end])
            sgn = 1.0 if end == 0 else -1.0
            centre = _v(P) + _v(N) * cs + _v(B) * ct + _v(T) * depth * sgn
            ci = b.vw(centre, (u + depth * sgn, cs, ct))
            cuv = ((-1 if end == 0 else 1) * cs, ct)
            for j in range(n):
                j1 = (j + 1) % n
                tri = (ci, ring[j], ring[j1]) if end == 1 else (ci, ring[j1], ring[j])
                uvs = (cuv, planar[j], planar[j1]) if end == 1 else (cuv, planar[j1], planar[j])
                b.f(tri, uvs, island=f"break{end}", slot=cap_slot if cap_slot is not None else slot, end=True)
    b.close()
    return b.current


def catmull(points, step=0.02):
    pts = [_v(p) for p in points]
    ext = [pts[0] + (pts[0] - pts[1])] + pts + [pts[-1] + (pts[-1] - pts[-2])]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        k = max(2, int(np.linalg.norm(p2 - p1) / step))
        for s in range(k):
            t = s / k
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return out


def tube(b, name, pts, r, n=8, group="body", slot=0, closed=False, texel=1.0, wear=0.0):
    """Round tube along a point path on parallel-transported frames (rope, wire, pipe)."""
    pts = [_v(p) for p in pts]
    m = len(pts)
    frames, u, N = [], 0.0, None
    for i in range(m):
        d = pts[(i + 1) % m] - pts[i - 1] if closed else pts[min(i + 1, m - 1)] - pts[max(i - 1, 0)]
        T = _unit(d)
        if N is None:
            N, _ = normal_to(T, Z if abs(T[2]) < 0.9 else X)
        N = _unit(N - T * N.dot(T))
        if i > 0:
            u += float(np.linalg.norm(pts[i] - pts[i - 1]))
        frames.append((pts[i], T, N, np.cross(T, N), 1.0, 1.0, u))
    return sweep(b, name, circle_section(r, n), frames, group=group, slot=slot, closed=closed, texel=texel, wear=wear)


def poly_sweep(b, name, pts, bend_axis, h, w, c=0.0, closed=False, group="body", slot=0, texel=1.0, wear=0.0):
    """Flat bar (thickness h, width w along bend_axis) bent along a polyline with mitred corners: bands, straps."""
    pts = [_v(p) for p in pts]
    Bax = _unit(bend_axis)
    m = len(pts)
    frames, u = [], 0.0
    for i in range(m):
        if closed:
            d0, d1 = _unit(pts[i] - pts[i - 1]), _unit(pts[(i + 1) % m] - pts[i])
        else:
            d0 = _unit(pts[i] - pts[i - 1]) if i > 0 else _unit(pts[1] - pts[0])
            d1 = _unit(pts[i + 1] - pts[i]) if i < m - 1 else d0
        T = _unit(d0 + d1)
        N = _unit(np.cross(Bax, T))
        if i > 0:
            u += float(np.linalg.norm(pts[i] - pts[i - 1]))
        frames.append((pts[i], T, N, Bax, 1.0 / max(0.3, T.dot(d0)), 1.0, u))
    return sweep(b, name, rect_section(h, w, c), frames, group=group, slot=slot, closed=closed, texel=texel,
                 wear=wear)


def timber(b, name, p0, p1, up, h, w, c=0.004, ends=("flat", "flat"), taper=(1.0, 1.0), bow=0.0, nseg=1,
           jag=0.05, group="body", slot=0, cap_slot=None, grain=True, texel=1.0, wear=0.0):
    """Swept chamfered beam from p0 to p1 (h along `up`, w across) that may bow (sag `bow` m at mid-length along
    `up`), taper (end section = taper x start) and end 'broken' (splintered, on cap_slot). From _procgen_cart.py's
    member(); member() above is the plain straight beam. Member coordinates are (u, s along up, t across)."""
    p0, p1 = _v(p0), _v(p1)
    L = float(np.linalg.norm(p1 - p0))
    T0 = (p1 - p0) / L
    N0, _ = normal_to(T0, up)
    brk = [e == "broken" for e in ends]
    nt, ns = (int(w / 0.02) + 1, int(h / 0.02) + 1) if any(brk) else (1, 1)
    cc = min(c, 0.45 * h / 2, 0.45 * w / 2, 0.3 * L)
    sec = rect_section(h, w, cc, nt, ns)
    stations = [(0.0, True)] if (not brk[0] and cc > 0) else []
    stations.append((cc if (not brk[0] and cc > 0) else 0.0, False))
    stations += [(L * k / nseg, False) for k in range(1, nseg)]
    stations.append((L - cc if (not brk[1] and cc > 0) else L, False))
    if not brk[1] and cc > 0:
        stations.append((L, True))
    frames = []
    for u, inset in stations:
        x = u / L
        P = p0 + T0 * u + N0 * (bow * 4.0 * x * (1.0 - x))
        T = _unit(T0 + N0 * (bow * 4.0 * (1.0 - 2.0 * x) / L))
        N, B = normal_to(T, N0)
        ks, kt = 1.0 + (taper[0] - 1.0) * x, 1.0 + (taper[1] - 1.0) * x
        if inset:
            ks, kt = ks * (h - 2 * cc) / h, kt * (w - 2 * cc) / w
        frames.append((P, T, N, B, ks, kt, u))
    jags = [splinter_profile(b.rng, len(sec), jag) if brk[e] else None for e in (0, 1)]
    return sweep(b, name, sec, frames, group=group, slot=slot, caps=ends, jags=jags, cap_slot=cap_slot,
                 texel=texel, grain=(h / 2, w / 2) if grain else None, wear=wear)


def band_loop(b, name, centre, hx, hy, z0, z1, t, c=0.0008, group="body", slot=0, texel=1.0, wear=0.0):
    """Flat band of thickness t round a box of half sizes hx, hy (its inner face on the box), from z0 to z1 above
    `centre`, mitred at the four corners."""
    cx, cy, cz = _v(centre)
    ox, oy, zc = hx + t / 2, hy + t / 2, cz + (z0 + z1) / 2
    path = [(cx + ox, cy - oy, zc), (cx + ox, cy + oy, zc), (cx - ox, cy + oy, zc), (cx - ox, cy - oy, zc)]
    return poly_sweep(b, name, path, Z, t, z1 - z0, c=c, closed=True, group=group, slot=slot, texel=texel,
                      wear=wear)


def band_around(b, name, centre, axis, half_a, half_b, dir_a, t=0.005, w=0.03, c=0.0, group="body", slot=0,
                texel=1.0):
    """Band of thickness t and width w round a rectangular member: axis = member axis, half sizes along dir_a and
    axis x dir_a."""
    centre, axis, dir_a = _v(centre), _unit(axis), _unit(dir_a)
    dir_b = np.cross(axis, dir_a)
    a, cc = half_a + t / 2, half_b + t / 2
    pts = [centre + dir_a * a + dir_b * cc, centre - dir_a * a + dir_b * cc,
           centre - dir_a * a - dir_b * cc, centre + dir_a * a - dir_b * cc]
    return poly_sweep(b, name, pts, axis, t, w, c=c, closed=True, group=group, slot=slot, texel=texel)


def ring(b, name, C, E1, E2, radius, h=None, w=None, c=0.0, circ=None, a0=0.0, a1=TAU, n=48,
         ends=("flat", "flat"), jag=0.03, group="body", slot=0, cap_slot=None, texel=1.0, grain=None, wear=0.0):
    """Sweep along a circle (or arc a0..a1) of `radius` about C in the plane E1-E2; section radial h x axial w,
    or round of radius circ. grain (h/2, w/2) gives a wooden ring (felloe) its pith line and end grain."""
    C, E1, E2 = _v(C), _unit(E1), _unit(E2)
    closed = abs((a1 - a0) - TAU) < 1e-6
    sec = circle_section(circ, 8) if circ else rect_section(h, w, c)
    cc = 0.0 if circ else min(c, 0.45 * h / 2, 0.45 * w / 2)
    brk = [e == "broken" for e in ends]
    if not closed and any(brk) and not circ:
        sec = rect_section(h, w, cc, int(w / 0.02) + 1, int(h / 0.02) + 1)
    stations = []
    if closed:
        stations = [(a0 + TAU * k / n, False) for k in range(n)]
    else:
        da = cc / radius
        if not brk[0] and da > 0:
            stations.append((a0, True))
        start, stop = a0 + (da if not brk[0] else 0.0), a1 - (da if not brk[1] else 0.0)
        segs = max(2, int(round(n * (a1 - a0) / TAU)))
        stations += [(start + (stop - start) * k / segs, False) for k in range(segs + 1)]
        if not brk[1] and da > 0:
            stations.append((a1, True))
    frames = []
    for th, inset in stations:
        radial = E1 * math.cos(th) + E2 * math.sin(th)
        T = -E1 * math.sin(th) + E2 * math.cos(th)
        ks = kt = 1.0
        if inset:
            ks, kt = (h - 2 * cc) / h, (w - 2 * cc) / w
        frames.append((C + radial * radius, T, radial, np.cross(T, radial), ks, kt, radius * (th - a0)))
    jags = [splinter_profile(b.rng, len(sec), jag) if (brk[e] and not closed) else None for e in (0, 1)]
    return sweep(b, name, sec, frames, group=group, slot=slot, closed=closed, caps=ends, jags=jags,
                 cap_slot=cap_slot, texel=texel, grain=grain, wear=wear)


# ------------------------------------------------------------------------------------------------ fixings

def rivet(b, p, normal, r=0.0055, h=0.0035, n=8, sink=0.0008, group="body", slot=0, name="rivet", texel=2.0):
    """Domed rivet head on a surface at p (normal out of the surface), its underside sunk `sink` into it."""
    nrm = _unit(normal)
    return lathe(b, name, _v(p) - nrm * sink, nrm,
                 [(0.0, r), (sink + h * 0.45, r * 0.86), (sink + h * 0.8, r * 0.52), (sink + h, r * 0.14)],
                 n=n, group=group, slot=slot, texel=texel)


def nail(b, p, normal, rng, r=0.0065, h=0.0028, sink=0.0006, seg=6, tilt_deg=2.5, group="body", slot=0,
         name="nail", texel=2.0):
    """Hand-forged nail head: a low faceted dome, driven a little off square, size and sink jittered by rng."""
    nrm = _unit(normal)
    helper = X if abs(nrm[0]) < 0.9 else Y
    a = _unit(np.cross(nrm, helper))
    bb = np.cross(nrm, a)
    tilt = rng.normal(0, math.radians(tilt_deg), 2)
    axis = _unit(nrm + a * math.tan(tilt[0]) + bb * math.tan(tilt[1]))
    r *= rng.uniform(0.9, 1.1)
    h *= rng.uniform(0.75, 1.15)
    sink += rng.uniform(0.0, 0.0006)
    return lathe(b, name, _v(p) - axis * sink, axis, [(0.0, r), (sink + h * 0.45, r * 0.82), (sink + h, r * 0.36)],
                 n=seg, group=group, slot=slot, phase=rng.uniform(0, TAU), texel=texel)
