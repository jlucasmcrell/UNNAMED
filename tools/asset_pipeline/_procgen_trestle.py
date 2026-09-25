"""Procedural trestle table: prop_long_trestle_table (Sel's survey table), built from real dimensions.

Image-to-3D melts this thin, open assembly (the Pixal3D reconstruction in assets/ready is yawed ~45 deg,
pitched 15 deg and shrunk to fit), so it is built here in Blender from member sizes instead, after
assets/concepts/prop_long_trestle_table.png: seven 40 mm pine boards on two A-trestles, each trestle two
splayed legs under a head rail with a low stretcher between them, and an iron angle cleat at each rail
end holding the boards. The concept shows no papers on the top, so there are none.

Every member is a closed chamfered prism in its own frame (X along the grain). UVs are projected per side
in that frame, so grain runs along each member at one texel density, and the textures are evaluated in
numpy from a 3D pine function in the same frame (growth rings round a pith line, knots, end checks). The
wood is then aged: grime and dust, mud at the feet, a handled front edge with knife gouges and nicks, a
scorch, ink stains and cup rings. Occlusion is baked with Cycles. Everything is seeded: the same
arguments rebuild the same file.

Output (glTF): metres, +Y up, front (+Z) is the long side with the gouged edge, centred on X/Z, lowest
point at y = 0. One mesh, one material, 1024 px base colour (sRGB) / normal (OpenGL) / ORM.

Usage:
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_trestle.py -- ^
      [--out <dir>] [--seed 1807] [--tex 1024] [--ss 2] [--ao-samples 64]
Writes <out>/prop_long_trestle_table.glb and <out>/prop_long_trestle_table_provenance.json
(default <out>: assets/_staging/procedural/prop_long_trestle_table). The last stdout line is
"PROCGEN_RESULT <provenance path>".
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import sys
import tempfile
import time

import bmesh
import bpy
import numpy as np


ASSET_ID = "prop_long_trestle_table"
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONCEPT = "assets/concepts/prop_long_trestle_table.png"

# Blender frame while building: X = table length, -Y = front (glTF +Z), Z = up.
PARAMS = {
    "length_m": 1.80,
    "depth_m": 0.92,
    "top_height_m": 0.88,
    "board_thickness_m": 0.04,
    "board_count": 7,
    "board_gap_m": 0.0005,
    "board_end_stagger_m": 0.002,
    "trestle_inset_m": 0.22,          # table end to trestle centre line
    "rail": {"length_m": 0.82, "thickness_m": 0.09, "height_m": 0.10},
    "leg": {"width_m": 0.10, "thickness_m": 0.09, "foot_centre_m": 0.375, "top_centre_m": 0.27,
            "tenon_into_rail_m": 0.005},
    "stretcher": {"centre_height_m": 0.21, "height_m": 0.09, "thickness_m": 0.06, "tenon_into_leg_m": 0.015},
    "cleat": {"width_m": 0.05, "plate_m": 0.005, "drop_m": 0.055, "reach_m": 0.035},
    "chamfer_m": {"top_outer": 0.007, "top_inner": 0.0015, "board_under": 0.0015, "board_end": 0.005,
                  "leg": 0.005, "foot": 0.004, "rail_low": 0.006, "rail_high": 0.0015, "rail_end": 0.004,
                  "stretcher": 0.004, "iron": 0.001},
    "texel_target_px_per_m": 512,
    "island_margin_px": 3,
}

# Side label -> (outward normal, u, v) in the member frame, all right-handed (u x v = n) so the tangent
# frame the engine derives from the UVs matches the one the normal map is written in.
SIDES = {
    "py": ((0, 1, 0), (1, 0, 0), (0, 0, -1)),
    "pz": ((0, 0, 1), (1, 0, 0), (0, 1, 0)),
    "my": ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
    "mz": ((0, 0, -1), (1, 0, 0), (0, -1, 0)),
    "x0": ((-1, 0, 0), (0, 1, 0), (0, 0, -1)),
    "x1": ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
}


def log(msg):
    print(f"[procgen_trestle] {msg}", flush=True)


def srgb_to_lin(c):
    c = np.asarray(c, np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------------------------------------- noise
def _hash3(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 1440662683 + seed * 2654435761) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h = h ^ (h >> 16)
    return h.astype(np.float64) * (1.0 / 4294967295.0)


def vnoise(p, seed):
    """Value noise in [-1, 1] at points p (N, 3), quintic interpolation."""
    fl = np.floor(p)
    f = p - fl
    i = fl.astype(np.int64)
    u = f * f * f * (f * (f * 6 - 15) + 10)
    out = np.zeros(len(p))
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                out += wx * wy * wz * _hash3(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out * 2 - 1


def fbm(p, seed, octaves=3, gain=0.5):
    total, amp, norm = np.zeros(len(p)), 1.0, 0.0
    for o in range(octaves):
        total += amp * vnoise(p * (2.0 ** o) + o * 17.13, seed + o * 101)
        norm += amp
        amp *= gain
    return total / norm


# ---------------------------------------------------------------------------------------------- members
def chamfer_rect(hy, hz, c_pp, c_mp, c_mm, c_pm, mid_z=False):
    """Chamfered rectangle profile, CCW seen from +X. Each entry is (y, z, label of the edge leaving it).
    Chamfer strips belong to the adjacent Z side, so a board's worn top edges carry the top texture."""
    pts = [(hy, -hz + c_pm, "py"), (hy, hz - c_pp, "pz"), (hy - c_pp, hz, "pz")]
    if mid_z:
        pts.append((0.0, hz, "pz"))
    pts += [(-hy + c_mp, hz, "pz"), (-hy, hz - c_mp, "my"), (-hy, -hz + c_mm, "mz"), (-hy + c_mm, -hz, "mz")]
    if mid_z:
        pts.append((0.0, -hz, "mz"))
    pts += [(hy - c_pm, -hz, "mz")]
    prof = Profile(pts)
    prof.args = (hy, hz, c_pp, c_mp, c_mm, c_pm, mid_z)
    return prof


class Profile(list):
    """A chamfer_rect profile that remembers how it was made, so its end inset can be rebuilt."""
    args = None


def inset_profile(prof, e):
    """The profile shrunk by e on every side for an end chamfer band, with the same vertices in the same
    order. A true polygon offset would turn chamfers narrower than 0.59 e inside out (a bow-tie), so each
    chamfer shrinks by the offset amount and stops at 0.3 mm."""
    hy, hz, c_pp, c_mp, c_mm, c_pm, mid_z = prof.args
    k = 2 * math.tan(math.pi / 8) / math.sqrt(2) * e
    c = [max(v - k, 0.0003) for v in (c_pp, c_mp, c_mm, c_pm)]
    return [(y, z) for y, z, _ in chamfer_rect(hy - e, hz - e, *c, mid_z=mid_z)]


class Member:
    def __init__(self, name, kind, material, rot, origin, profile, plane0, plane1, e0, e1, rings,
                 scales, deform=None):
        self.name, self.kind, self.material = name, kind, material
        self.rot = np.array(rot, np.float64)          # columns: member X, Y, Z in the build frame
        self.origin = np.array(origin, np.float64)
        self.profile = profile
        self.plane0, self.plane1 = plane0, plane1     # end planes x = a + sy*y + sz*z
        self.e0, self.e1 = e0, e1                     # end chamfers
        self.rings = rings                            # extra ring positions (0..1) along the member
        self.scales = scales                          # texel scale per side label
        self.deform = deform
        self.wood = None

    def to_world(self, p):
        return p @ self.rot.T + self.origin


def plane_x(plane, y, z):
    a, sy, sz = plane
    return a + sy * y + sz * z


def build_prism(m):
    """Vertices (member frame), faces (vertex index lists, outward winding) and a side label per face."""
    prof = [(y, z) for y, z, _ in m.profile]
    labels = [lab for _, _, lab in m.profile]
    n = len(prof)
    rings = []
    if m.e0 > 0:
        rings.append(("cap0", [(plane_x(m.plane0, y, z), y, z) for y, z in inset_profile(m.profile, m.e0)]))
    for t in [0.0] + list(m.rings) + [1.0]:
        ring = []
        for y, z in prof:
            xs = plane_x(m.plane0, y, z) + m.e0
            xe = plane_x(m.plane1, y, z) - m.e1
            ring.append((xs + t * (xe - xs), y, z))
        rings.append(("body", ring))
    if m.e1 > 0:
        rings.append(("cap1", [(plane_x(m.plane1, y, z), y, z) for y, z in inset_profile(m.profile, m.e1)]))
    verts = [v for _, ring in rings for v in ring]
    faces, face_labels = [], []
    for j in range(len(rings) - 1):
        for k in range(n):
            k1 = (k + 1) % n
            faces.append([j * n + k, j * n + k1, (j + 1) * n + k1, (j + 1) * n + k])
            if rings[j][0] == "cap0":
                face_labels.append("x0")
            elif rings[j + 1][0] == "cap1":
                face_labels.append("x1")
            else:
                face_labels.append(labels[k])
    last = len(rings) - 1
    # End caps; a profile with mid points is split along the chord between them, so no triangle can be
    # made of three collinear points (a sliver that flips when the board cups).
    if n == 10:
        halves = [[0, 1, 2, 3, 8, 9], [3, 4, 5, 6, 7, 8]]
    else:
        halves = [list(range(n))]
    for h in halves:
        faces.append([k for k in reversed(h)])
        face_labels.append("x0")
        faces.append([last * n + k for k in h])
        face_labels.append("x1")
    return np.array(verts, np.float64), faces, face_labels


def rot_about_z90():
    # member X -> +Y, member Y -> -X, member Z -> +Z
    return np.array([[0, -1, 0], [1, 0, 0], [0, 0, 1]], np.float64)


def make_members(rng):
    P = PARAMS
    L, D, H, T = P["length_m"], P["depth_m"], P["top_height_m"], P["board_thickness_m"]
    ch = P["chamfer_m"]
    members = []

    # Boards: widths vary like real stock, the outer two carry the worn table edge.
    n = P["board_count"]
    w = rng.uniform(0.85, 1.15, n)
    w = w / w.sum() * (D - (n - 1) * P["board_gap_m"])
    y = -D / 2
    ident = np.eye(3)
    for i in range(n):
        yc = y + w[i] / 2
        y += w[i] + P["board_gap_m"]
        hy = w[i] / 2
        front, back = i == 0, i == n - 1
        # Blender -Y is the front; the member Y axis is the build Y axis for boards.
        c_pp = ch["top_outer"] if back else ch["top_inner"]
        c_mp = ch["top_outer"] if front else ch["top_inner"]
        prof = chamfer_rect(hy, T / 2, c_pp, c_mp, ch["board_under"], ch["board_under"], mid_z=True)
        s0 = -L / 2 + rng.uniform(0, P["board_end_stagger_m"])
        s1 = L / 2 - rng.uniform(0, P["board_end_stagger_m"])
        if i == 3:
            s0, s1 = -L / 2, L / 2   # one board spans the full length, so the table is exactly L long
        bow = rng.uniform(0.6, 1.4) * 0.001
        cup = rng.choice([-1, 1]) * rng.uniform(0.3, 0.9) * 0.001
        twist = rng.normal(0, 0.0006)
        lift = rng.uniform(-0.0008, 0.0008)   # boards never sit dead flush
        xt = L / 2 - P["trestle_inset_m"]

        def deform(p, bow=bow, cup=cup, twist=twist, hy=hy, xt=xt, lift=lift):
            x, yy = p[:, 0], p[:, 1]
            dz = bow * ((x / xt) ** 2 - 1) + cup * (yy / hy) ** 2 + twist * (x / (L / 2)) * (yy / hy) + lift
            q = p.copy()
            q[:, 2] += dz
            return q

        scales = {"pz": 1.0, "mz": 0.3, "x0": 1.0, "x1": 1.0,
                  "py": 1.0 if back else 0.1, "my": 1.0 if front else 0.1}
        members.append(Member(f"board_{i}", "board", "wood", ident, (0, yc, H - T / 2), prof,
                              (s0, 0, 0), (s1, 0, 0), ch["board_end"], ch["board_end"],
                              [k / 12 for k in range(1, 12)], scales, deform))

    rail, leg, st, cl = P["rail"], P["leg"], P["stretcher"], P["cleat"]
    rail_top = H - T
    rail_bottom = rail_top - rail["height_m"]
    Rz = rot_about_z90()
    for side in (-1, 1):
        xt = side * (L / 2 - P["trestle_inset_m"])
        tag = "L" if side < 0 else "R"
        # Head rail across the depth, under the boards.
        prof = chamfer_rect(rail["thickness_m"] / 2, rail["height_m"] / 2,
                            ch["rail_high"], ch["rail_high"], ch["rail_low"], ch["rail_low"])
        hl = rail["length_m"] / 2
        members.append(Member(f"rail_{tag}", "rail", "wood", Rz, (xt, 0, rail_bottom + rail["height_m"] / 2), prof,
                              (-hl, 0, 0), (hl, 0, 0), ch["rail_end"], ch["rail_end"], [0.25, 0.5, 0.75],
                              {"py": 1.0, "my": 1.0, "pz": 0.1, "mz": 0.5, "x0": 1.0, "x1": 1.0}))
        # Splayed legs: in the trestle plane, foot further out than the top; cut level top and bottom.
        leg_top = rail_bottom + leg["tenon_into_rail_m"]
        for s in (-1, 1):
            foot = np.array([xt, s * leg["foot_centre_m"], 0.0])
            top = np.array([xt, s * leg["top_centre_m"], leg_top])
            ax = (top - foot) / np.linalg.norm(top - foot)
            az = np.array([1.0, 0.0, 0.0])
            ay = np.cross(az, ax)
            rot = np.stack([ax, ay, az], axis=1)
            prof = chamfer_rect(leg["width_m"] / 2, leg["thickness_m"] / 2, ch["leg"], ch["leg"], ch["leg"], ch["leg"])
            # member x of the level plane world z = h: x*ax_z + y*ay_z = h
            p0 = (0.0, -ay[2] / ax[2], 0.0)
            p1 = (leg_top / ax[2], -ay[2] / ax[2], 0.0)
            bow = rng.normal(0, 0.0012)
            length = leg_top / ax[2]

            def deform(p, bow=bow, length=length):
                t = np.clip(p[:, 0] / length, 0, 1)
                q = p.copy()
                q[:, 2] += bow * np.sin(math.pi * t)
                return q

            members.append(Member(f"leg_{tag}{'F' if s < 0 else 'B'}", "leg", "wood", rot, foot, prof, p0, p1,
                                  ch["foot"], 0.0, [0.2, 0.4, 0.6, 0.8],
                                  {"py": 1.0, "my": 1.0, "pz": 1.0, "mz": 1.0, "x0": 0.25, "x1": 0.1}, deform))
        # Stretcher between the legs, its tenons buried in them.
        zc = st["centre_height_m"]
        yc_leg = leg["foot_centre_m"] + (leg["top_centre_m"] - leg["foot_centre_m"]) * zc / leg_top
        half_w = (leg["width_m"] / 2) / math.cos(math.atan2(leg["foot_centre_m"] - leg["top_centre_m"], leg_top))
        hl = yc_leg - half_w + st["tenon_into_leg_m"]
        prof = chamfer_rect(st["thickness_m"] / 2, st["height_m"] / 2, ch["stretcher"], ch["stretcher"],
                            ch["stretcher"], ch["stretcher"])
        members.append(Member(f"stretcher_{tag}", "stretcher", "wood", Rz, (xt, 0, zc), prof,
                              (-hl, 0, 0), (hl, 0, 0), 0.0, 0.0, [0.33, 0.66],
                              {"py": 1.0, "my": 1.0, "pz": 1.0, "mz": 0.5, "x0": 0.1, "x1": 0.1}))
        # Iron angle cleats at the rail ends: a plate down the rail end and a plate under the boards.
        c = ch["iron"]
        for s in (-1, 1):
            ye = s * rail["length_m"] / 2
            hw, pt = cl["width_m"] / 2, cl["plate_m"] / 2
            # vertical plate: member X up the rail end, Y along build X, Z outwards (build Y * s)
            rot_v = np.stack([np.array([0, 0, 1.0]), np.array([s * 1.0, 0, 0]), np.array([0, s * 1.0, 0])], axis=1)
            prof = chamfer_rect(hw, pt, c, c, c, c)
            members.append(Member(f"cleat_{tag}{'F' if s < 0 else 'B'}_v", "cleat", "iron", rot_v,
                                  (xt, ye + s * pt, rail_top), prof, (-cl["drop_m"], 0, 0), (0.0, 0, 0), c, c, [],
                                  {k: 1.0 for k in SIDES}))
            # horizontal plate under the boards, reaching out from the rail end
            rot_h = np.stack([np.array([0, s * 1.0, 0]), np.array([-s * 1.0, 0, 0]), np.array([0, 0, 1.0])], axis=1)
            members.append(Member(f"cleat_{tag}{'F' if s < 0 else 'B'}_h", "cleat", "iron", rot_h,
                                  (xt, ye, rail_top - pt), prof, (0.0, 0, 0), (cl["reach_m"], 0, 0), c, c, [],
                                  {k: 1.0 for k in SIDES}))
    for m in members:
        assert abs(np.linalg.det(m.rot) - 1) < 1e-9, m.name
    return members


# ---------------------------------------------------------------------------------------------- wood
EARLY = srgb_to_lin((210, 166, 104))
LATE = srgb_to_lin((150, 88, 38))
KNOT = srgb_to_lin((120, 70, 34))
KNOT_RIM = srgb_to_lin((58, 36, 20))


def wood_params(rng, m, extents):
    (x0, x1), hy, hz = extents
    if m.kind == "board":
        # a mixed stack: about half flat-sawn (pith under or over the board: broad cathedral figure on the
        # top), half rift-sawn (pith off to one side: tighter, straighter grain)
        if rng.random() < 0.5:
            ang = rng.choice([-1, 1]) * math.pi / 2 + rng.normal(0, 0.25)
        else:
            ang = rng.choice([0.0, math.pi]) + rng.choice([-1, 1]) * rng.uniform(0.35, 0.8)
        dist = rng.uniform(0.09, 0.25)
        py0, pz0 = dist * math.cos(ang), dist * math.sin(ang)
    else:
        ang, dist = rng.uniform(0, 2 * math.pi), rng.uniform(0.04, 0.14)
        py0, pz0 = dist * math.cos(ang), dist * math.sin(ang)
    k = {
        "py0": py0, "pz0": pz0, "sy": rng.normal(0, 0.012), "sz": rng.normal(0, 0.012),
        "ring": rng.uniform(0.0032, 0.0055), "warp": rng.uniform(0.002, 0.0045),
        "seed": int(rng.integers(1, 2 ** 30)),
        "tint": rng.uniform(0.82, 1.06) * np.array([1, rng.uniform(0.94, 1.04), rng.uniform(0.86, 1.08)]),
        "knots": [], "checks": [],
    }
    count = {"board": rng.integers(3, 7), "leg": rng.integers(1, 3), "rail": rng.integers(0, 2),
             "stretcher": 1}.get(m.kind, 0)
    for _ in range(int(count)):
        xk = rng.uniform(x0 + 0.08 * (x1 - x0), x1 - 0.08 * (x1 - x0))
        # aim the branch at the visible faces
        if m.kind == "board":
            # branch runs from the pith out through the top face, where a knot shows
            phi = math.atan2(hz - pz0, rng.uniform(-0.7, 0.7) * hy - py0)
        else:
            phi = rng.uniform(0, 2 * math.pi)
        tilt = rng.uniform(-0.35, 0.35)
        if m.kind == "board" and math.sin(phi) < math.sin(math.radians(40)):
            continue   # a branch leaving at a shallow angle cuts as a long spike knot, which reads as a slash
        d = np.array([math.tan(tilt), math.cos(phi), math.sin(phi)])
        k["knots"].append({"x": xk, "dir": d / np.linalg.norm(d), "r": rng.uniform(0.007, 0.016),
                           "swirl": rng.uniform(0.8, 1.8) * rng.choice([-1, 1]), "dead": bool(rng.random() < 0.35)})
    for _ in range(int(rng.integers(0, 3))):
        k["checks"].append({"phi": rng.uniform(0, 2 * math.pi), "end": int(rng.integers(0, 2)),
                            "len": rng.uniform(0.03, 0.14), "w": rng.uniform(0.0005, 0.0011)})
    k["x_range"] = (x0, x1)
    return k


def wood_fields(m, P):
    """Pine at member-frame points P (N, 3): linear colour, height (m), roughness, latewood mask."""
    k = m.wood
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    s = k["seed"]
    wander = fbm(np.c_[x * 0.9, np.full_like(x, 3.1), np.full_like(x, 7.7)], s + 1, 2)
    py = k["py0"] + k["sy"] * x + 0.006 * wander
    pz = k["pz0"] + k["sz"] * x + 0.006 * fbm(np.c_[x * 0.9, np.full_like(x, 11.3), np.full_like(x, 1.9)], s + 2, 2)
    dy, dz = y - py, z - pz
    r = np.sqrt(dy * dy + dz * dz)
    r = r + k["warp"] * fbm(P * np.array([1.6, 14.0, 14.0]), s + 3, 3)
    phase = r / k["ring"] + 0.12 * fbm(P * np.array([4.0, 60.0, 60.0]), s + 4, 2)
    knot_core = np.zeros(len(P))
    knot_q = np.full(len(P), 9.0)
    knot_rim = np.zeros(len(P))
    for kn in k["knots"]:
        o = np.array([kn["x"], k["py0"] + k["sy"] * kn["x"], k["pz0"] + k["sz"] * kn["x"]])
        w = P - o
        along = w @ kn["dir"]
        perp = w - along[:, None] * kn["dir"][None, :]
        q = np.linalg.norm(perp, axis=1)
        rad = kn["r"] * np.clip(0.35 + along / 0.08, 0.35, 1.0)
        ok = along > 0
        infl = np.exp(-(q / (rad * 2.6)) ** 2) * ok
        phase = phase + kn["swirl"] * infl
        core = smoothstep(rad * 1.02, rad * 0.86, q) * ok
        rim = np.exp(-((q - rad) / (0.0012 + 0.08 * rad)) ** 2) * ok * (1.0 if kn["dead"] else 0.45)
        knot_core = np.maximum(knot_core, core)
        knot_rim = np.maximum(knot_rim, rim)
        knot_q = np.where(core > 0, np.minimum(knot_q, q / rad), knot_q)
    f = phase - np.floor(phase)
    # latewood: a narrow dark band that fades in and ends sharply; ring strength varies year to year
    lw = smoothstep(0.60, 0.84, f) * (1 - smoothstep(0.94, 0.995, f))
    year = np.floor(phase)
    strength = 0.55 + 0.45 * _hash3(year.astype(np.int64), np.zeros(len(P), np.int64), np.zeros(len(P), np.int64), s)
    lw = lw * strength
    col = EARLY[None, :] * (1 - lw[:, None]) + LATE[None, :] * lw[:, None]
    streak = fbm(P * np.array([0.7, 38.0, 38.0]), s + 5, 3)
    fibre = fbm(P * np.array([5.0, 420.0, 420.0]), s + 6, 2)
    fine = fbm(P * np.array([2.0, 1500.0, 1500.0]), s + 7, 1)
    col = col * (1 + 0.16 * streak[:, None] + 0.08 * fibre[:, None] + 0.05 * fine[:, None])
    col = col * k["tint"][None, :]
    kc = KNOT[None, :] * (0.85 + 0.25 * np.cos(np.clip(knot_q, 0, 1) * 14.0))[:, None]
    col = col * (1 - knot_core[:, None]) + kc * knot_core[:, None]
    col = col * (1 - 0.55 * knot_rim[:, None]) + KNOT_RIM[None, :] * 0.55 * knot_rim[:, None]
    # end checks: radial splits from the pith, running a few cm in from an end
    crack = np.zeros(len(P))
    for ck in k["checks"]:
        c, sn = math.cos(ck["phi"]), math.sin(ck["phi"])
        radial = dy * c + dz * sn
        dist = np.abs(-dy * sn + dz * c)
        from_end = (x - k["x_range"][0]) if ck["end"] == 0 else (k["x_range"][1] - x)
        taper = smoothstep(ck["len"], 0.0, from_end) * (radial > 0.01)
        crack = np.maximum(crack, np.exp(-(dist / (ck["w"] * (0.3 + taper))) ** 2) * taper)
    col = col * (1 - 0.8 * crack[:, None])
    height = 0.00022 * lw + 0.00006 * fibre + 0.00003 * fine + 0.00008 * knot_core - 0.0012 * crack
    rough = 0.66 - 0.06 * lw + 0.04 * fibre + 0.15 * crack
    return col, height, rough, lw, crack


# ---------------------------------------------------------------------------------------------- ageing
def make_decals(rng, members):
    P = PARAMS
    L, D, H = P["length_m"], P["depth_m"], P["top_height_m"]
    d = {"gouges": [], "scratches": [], "nicks": [], "ink": [], "cups": [], "stains": [], "pegs": [], "nails": []}
    # heavy knife gouges along the front (glTF +Z, build -Y) edge
    for _ in range(22):
        d["gouges"].append({"x": rng.uniform(-0.82, 0.82), "y": -D / 2 + rng.uniform(0.015, 0.12),
                            "ang": math.pi / 2 + rng.normal(0, 0.55), "len": rng.uniform(0.03, 0.09),
                            "w": rng.uniform(0.003, 0.006), "depth": rng.uniform(0.001, 0.0022)})
    for _ in range(70):
        d["scratches"].append({"x": rng.uniform(-0.85, 0.85), "y": rng.uniform(-D / 2 + 0.02, D / 2 - 0.02),
                               "ang": rng.uniform(0, math.pi), "len": rng.uniform(0.02, 0.12),
                               "w": rng.uniform(0.0008, 0.0014), "depth": rng.uniform(0.00008, 0.0002),
                               "light": bool(rng.random() < 0.6)})
    # dents and chips on the outer top edges, heavier on the front, and on the feet
    for _ in range(26):
        d["nicks"].append({"p": np.array([rng.uniform(-0.88, 0.88), -D / 2, H]), "r": rng.uniform(0.003, 0.009),
                           "depth": rng.uniform(0.0008, 0.0025)})
    for _ in range(8):
        d["nicks"].append({"p": np.array([rng.uniform(-0.88, 0.88), D / 2, H]), "r": rng.uniform(0.003, 0.007),
                           "depth": rng.uniform(0.0006, 0.0016)})
    for sx in (-1, 1):
        for _ in range(4):
            d["nicks"].append({"p": np.array([sx * L / 2, rng.uniform(-D / 2, D / 2), H]),
                               "r": rng.uniform(0.003, 0.008), "depth": rng.uniform(0.0008, 0.002)})
    for m in members:
        if m.kind == "leg":
            for _ in range(3):
                q = np.array([rng.uniform(0, 0.05), rng.choice([-1, 1]) * PARAMS["leg"]["width_m"] / 2,
                              rng.choice([-1, 1]) * PARAMS["leg"]["thickness_m"] / 2])
                d["nicks"].append({"p": m.to_world(q[None, :])[0], "r": rng.uniform(0.004, 0.01),
                                   "depth": rng.uniform(0.001, 0.003)})
    # ink: a survey table's working stains, towards the back half
    for cx, cy in ((-0.24, 0.16), (0.12, 0.27), (-0.5, -0.02)):
        blob = {"x": cx + rng.normal(0, 0.03), "y": cy + rng.normal(0, 0.03), "r": rng.uniform(0.014, 0.028),
                "seed": int(rng.integers(1, 2 ** 30)), "dots": []}
        for _ in range(int(rng.integers(1, 5))):
            a, rr = rng.uniform(0, 2 * math.pi), rng.uniform(0.02, 0.06)
            blob["dots"].append((blob["x"] + rr * math.cos(a), blob["y"] + rr * math.sin(a), rng.uniform(0.001, 0.003)))
        d["ink"].append(blob)
    for cx, cy in ((0.3, -0.18), (-0.66, 0.2), (0.62, 0.3)):
        d["cups"].append({"x": cx, "y": cy, "r": rng.uniform(0.034, 0.042), "a0": rng.uniform(0, 2 * math.pi),
                          "arc": rng.uniform(3.6, 6.2), "seed": int(rng.integers(1, 2 ** 30))})
    for cx, cy in ((-0.05, -0.05), (0.52, -0.3), (-0.62, -0.28), (0.2, 0.12), (-0.35, 0.33)):
        d["stains"].append({"x": cx, "y": cy, "r": rng.uniform(0.07, 0.12), "seed": int(rng.integers(1, 2 ** 30))})
    d["scorch"] = {"x": 0.5, "y": 0.1, "r": 0.068, "w": 0.006, "gap": rng.uniform(0, 2 * math.pi), "seed": int(rng.integers(1, 2 ** 30))}
    # oak pegs through the joints: stretcher tenons (through each leg) and leg tenons (through each rail)
    leg, st, rail = PARAMS["leg"], PARAMS["stretcher"], PARAMS["rail"]
    leg_top = H - PARAMS["board_thickness_m"] - rail["height_m"] + leg["tenon_into_rail_m"]
    for m in members:
        if m.kind == "leg":
            zc = st["centre_height_m"]
            s = 1 if m.origin[1] > 0 else -1
            yc = s * (leg["foot_centre_m"] + (leg["top_centre_m"] - leg["foot_centre_m"]) * zc / leg_top)
            d["pegs"].append({"p": np.array([m.origin[0], yc, zc]), "r": 0.007})
        if m.kind == "rail":
            for s in (-1, 1):
                d["pegs"].append({"p": np.array([m.origin[0], s * leg["top_centre_m"], m.origin[2] - 0.012]), "r": 0.007})
    for m in members:
        if m.kind == "cleat":
            vertical = m.name.endswith("_v")
            # nail heads on the face that shows: out from the rail end, or down under the boards
            face = "pz" if vertical else "mz"
            pz = PARAMS["cleat"]["plate_m"] / 2 * (1 if vertical else -1)
            for sx in (-1, 1):
                q = np.array([-0.03 if vertical else 0.022, sx * 0.013, pz])
                d["nails"].append({"p": m.to_world(q[None, :])[0], "r": 0.0038, "member": m.name, "face": face})
    return d


def apply_surface(m, label, P, W, nW, col, height, rough, rng_seed, decals):
    """Age one island's samples: P member frame, W build-frame world position, nW world normal (3,)."""
    D = PARAMS["depth_m"]
    N = len(P)
    top = m.kind == "board" and label == "pz"
    end_grain = label in ("x0", "x1") and m.kind != "cleat"
    blot = fbm(W * 3.2, rng_seed + 11, 3)
    grime = np.clip(0.5 + 0.6 * blot, 0, 1)
    # general age: a little darker and greyer, blotchy
    lum = col @ np.array([0.2126, 0.7152, 0.0722])
    col = col * (1 - 0.24 * grime[:, None]) * 0.88
    col = col * 0.86 + lum[:, None] * 0.14 * 0.94
    if end_grain:
        col = col * 0.58
        rough = rough + 0.14
        height = height + 0.00015 * fbm(P * 900.0, rng_seed + 12, 2)
    if m.kind == "board" and label == "mz":
        col = col * 1.04    # underside: out of the light and hands, less patina
    if nW[2] > 0.9 and m.kind in ("stretcher", "leg"):
        dust = smoothstep(0.1, 0.7, 0.5 + 0.5 * fbm(W * 40.0, rng_seed + 13, 2))
        col = col * (1 - 0.25 * dust[:, None]) + srgb_to_lin((150, 140, 125))[None, :] * 0.25 * dust[:, None]
        rough = rough + 0.1 * dust
    # mud and floor grime at the feet
    zf = W[:, 2]
    edge = 0.07 + 0.035 * fbm(W * np.array([30.0, 30.0, 4.0]), rng_seed + 14, 3)
    mud = smoothstep(edge + 0.05, edge - 0.04, zf) * (0.75 + 0.25 * fbm(W * 90.0, rng_seed + 17, 2))
    splash = (fbm(W * 160.0, rng_seed + 15, 2) > 0.4) * smoothstep(0.3, 0.05, zf)
    mud = np.clip(np.maximum(mud, 0.5 * splash), 0, 1)
    # scuffs and floor grime darken the lower legs and stretchers well above the mud line
    low = smoothstep(0.4, 0.05, zf) * (0.6 + 0.4 * fbm(W * np.array([12.0, 12.0, 3.0]), rng_seed + 19, 2))
    col = col * (1 - 0.18 * low[:, None])
    rough = rough + 0.05 * low
    mud_col = srgb_to_lin((92, 80, 66))
    col = col * (1 - 0.7 * mud[:, None]) + mud_col[None, :] * 0.7 * mud[:, None]
    rough = rough + 0.2 * mud
    height = height + 0.0002 * mud * fbm(W * 300.0, rng_seed + 16, 2)
    if top:
        x, y = W[:, 0], W[:, 1]
        # handled patina: darker and smoother along the front, where hands and forearms rest
        front = smoothstep(-D / 2 + 0.22, -D / 2 + 0.01, y)
        use = np.clip(0.55 * front + 0.25 * smoothstep(0.7, 0.2, np.abs(x)), 0, 1)
        col = col * (1 - 0.18 * use[:, None])
        rough = rough - 0.14 * use
        # dirt packed into the joints between boards and worked into the board ends
        hy = max(abs(p[0]) for p in m.profile)
        x_lo, x_hi = m.wood["x_range"]
        seam = smoothstep(0.008, 0.0, hy - np.abs(P[:, 1])) * (0.6 + 0.4 * fbm(W * 25.0, rng_seed + 18, 2))
        ends = smoothstep(0.1, 0.0, np.minimum(P[:, 0] - x_lo, x_hi - P[:, 0]))
        col = col * (1 - (0.14 * seam + 0.16 * ends)[:, None])
        rough = rough + 0.06 * seam
        for st in decals["stains"]:
            dist = np.hypot(x - st["x"], y - st["y"])
            rr = st["r"] * (1 + 0.35 * fbm(np.c_[x * 18, y * 18, np.zeros(N)], st["seed"], 3))
            inside = smoothstep(rr, rr * 0.8, dist)
            rim = np.exp(-((dist - rr) / 0.004) ** 2) * (0.5 + 0.5 * fbm(np.c_[x * 60, y * 60, np.zeros(N)], st["seed"] + 1, 2))
            col = col * (1 - (0.08 * inside + 0.1 * rim)[:, None])
        for cup in decals["cups"]:
            dist = np.hypot(x - cup["x"], y - cup["y"])
            ang = np.mod(np.arctan2(y - cup["y"], x - cup["x"]) - cup["a0"], 2 * math.pi)
            arc = smoothstep(cup["arc"], cup["arc"] - 0.6, ang)
            ring = np.exp(-((dist - cup["r"]) / 0.0022) ** 2) * arc
            ring = ring * (0.6 + 0.4 * fbm(np.c_[x * 90, y * 90, np.zeros(N)], cup["seed"], 2))
            col = col * (1 - 0.35 * ring[:, None]) * (1 - 0.06 * (dist < cup["r"])[:, None])
        for ink in decals["ink"]:
            # soaked-in ink: a ragged translucent blot, darker where it dried at the rim, a few flecks
            dist = np.hypot(x - ink["x"], y - ink["y"])
            if dist.min() > ink["r"] * 3 + 0.1:
                continue
            xy = np.c_[x * 22, y * 22, np.zeros(N)]
            wick = 0.5 + 0.5 * fbm(np.c_[x * 6, y * 260, np.zeros(N)], ink["seed"] + 2, 2)   # along the grain
            dist = np.hypot((x - ink["x"]) * (0.75 + 0.25 * wick), y - ink["y"])
            rr = ink["r"] * (1 + 0.35 * fbm(xy, ink["seed"], 4))
            a = smoothstep(rr, rr * 0.55, dist) * (0.3 + 0.3 * wick)
            a = a + 0.12 * np.exp(-((dist - rr * 0.9) / 0.002) ** 2) * (dist < rr)
            for dx, dy, dr in ink["dots"]:
                a = np.maximum(a, 0.4 * smoothstep(dr, dr * 0.5, np.hypot(x - dx, y - dy)))
            a = np.clip(a, 0, 0.55)
            ink_col = srgb_to_lin((22, 24, 38))
            col = col * (1 - a[:, None]) + ink_col[None, :] * a[:, None]
            rough = rough * (1 - 0.3 * a) + 0.35 * 0.3 * a
        # a pot set down hot: a broken charred ring with a browned, cracked inside and a scorched halo
        sc = decals["scorch"]
        xy = np.c_[x * 20, y * 20, np.zeros(N)]
        dist = np.hypot(x - sc["x"], y - sc["y"]) * (1 + 0.05 * fbm(xy, sc["seed"], 3))
        r, w = sc["r"], sc["w"]
        ang = np.mod(np.arctan2(y - sc["y"], x - sc["x"]) - sc["gap"], 2 * math.pi)
        broken = 0.35 + 0.65 * smoothstep(0.0, 1.4, np.minimum(ang, 2 * math.pi - ang))   # the pot sat tilted
        ring = np.exp(-((dist - r) / (w * (1 + 0.6 * fbm(xy * 2, sc["seed"] + 4, 2)))) ** 2) * (0.65 + 0.55 * fbm(xy * 3, sc["seed"] + 3, 2)) * broken
        char = 0.7 * smoothstep(0.5, 0.9, ring)
        inside = smoothstep(r * 1.02, r * 0.6, dist)
        halo = smoothstep(r * 1.7, r * 0.95, dist)
        crackle = smoothstep(0.1, 0.0, np.abs(fbm(np.c_[x * 180, y * 110, np.zeros(N)], sc["seed"] + 1, 2)))
        ash = 0.5 + 0.5 * fbm(np.c_[x * 300, y * 300, np.zeros(N)], sc["seed"] + 2, 2)
        col = col * (1 - (0.3 * halo + 0.2 * inside)[:, None] * np.array([0.7, 0.84, 1.0])[None, :])
        scorch_col = srgb_to_lin((74, 47, 29))
        dark = np.clip(smoothstep(0.1, 0.5, ring) * 0.75 + 0.25 * inside * crackle, 0, 1)
        col = col * (1 - dark[:, None]) + scorch_col[None, :] * dark[:, None]
        char_col = srgb_to_lin((38, 30, 25)) * (1 + 0.5 * ash)[:, None] * (1 - 0.5 * crackle)[:, None]
        col = col * (1 - char[:, None]) + char_col * char[:, None]
        rough = rough + 0.2 * np.maximum(char, 0.5 * inside)
        height = height - 0.0005 * char - 0.0003 * char * crackle - 0.00015 * inside * crackle
        for g in decals["scratches"] + decals["gouges"]:
            c, s = math.cos(g["ang"]), math.sin(g["ang"])
            box = (np.abs(x - g["x"]) < g["len"] + 0.01) & (np.abs(y - g["y"]) < g["len"] + 0.01)
            if not box.any():
                continue
            idx = np.nonzero(box)[0]
            gx, gy = x[idx] - g["x"], y[idx] - g["y"]
            a, b = gx * c + gy * s, -gx * s + gy * c
            t = np.clip(a / g["len"], 0, 1)
            taper = np.sqrt(np.clip(np.sin(math.pi * t), 0, 1)) * (a > 0) * (a < g["len"])
            half = g["w"] / 2 * taper + 1e-5
            v = np.clip(1 - np.abs(b) / half, 0, 1) * taper
            height[idx] -= g["depth"] * v
            if "light" in g:
                shade = 1.12 if g["light"] else 0.85
            else:
                shade = 0.6
            col[idx] = col[idx] * (1 + (shade - 1) * np.clip(v * 1.6, 0, 1))[:, None]
            rough[idx] += 0.1 * v
    # dents and chips (3D, so they cross an edge onto both faces)
    for nk in decals["nicks"]:
        dist = np.linalg.norm(W - nk["p"][None, :], axis=1)
        v = np.exp(-(dist / nk["r"]) ** 2)
        if v.max() < 1e-3:
            continue
        height = height - nk["depth"] * v
        fresh = smoothstep(0.35, 0.8, v)
        col = col * (1 + 0.1 * fresh[:, None]) * (1 - 0.25 * smoothstep(0.1, 0.35, v) * (1 - fresh))[:, None]
    # pegs: 14 mm dowels through the joints, their end grain flush on the X faces
    if abs(nW[0]) > 0.9:
        for pg in decals["pegs"]:
            dist = np.hypot(W[:, 1] - pg["p"][1], W[:, 2] - pg["p"][2])
            near = np.abs(W[:, 0] - pg["p"][0]) < 0.08
            core = smoothstep(pg["r"], pg["r"] * 0.85, dist) * near
            rim = np.exp(-((dist - pg["r"]) / 0.0011) ** 2) * near
            peg_col = srgb_to_lin((110, 74, 42)) * (1 + 0.15 * np.cos(dist / pg["r"] * 9))[:, None]
            col = col * (1 - core[:, None]) + peg_col * core[:, None]
            col = col * (1 - 0.6 * rim[:, None])
            height = height - 0.0003 * rim + 0.0001 * core
            rough = rough + 0.1 * core
    return col, height, rough


def iron_fields(m, label, P, W, nW, seed, decals):
    N = len(P)
    base = srgb_to_lin((58, 56, 54))
    rust_c = srgb_to_lin((118, 66, 36))
    rn = fbm(W * 90.0, seed + 21, 4)
    rust = smoothstep(-0.05, 0.35, rn + 0.25 * fbm(W * 400.0, seed + 22, 2))
    col = base[None, :] * (1 + 0.12 * fbm(W * 300.0, seed + 23, 2))[:, None]
    col = col * (1 - rust[:, None]) + rust_c[None, :] * rust[:, None] * (0.8 + 0.3 * fbm(W * 600.0, seed + 24, 2))[:, None]
    metal = 0.85 * (1 - rust) + 0.1 * rust
    rough = 0.5 + 0.1 * fbm(W * 200.0, seed + 25, 2) + 0.35 * rust
    height = 0.00012 * rust * fbm(W * 900.0, seed + 26, 2) - 0.00006 * np.abs(fbm(W * 1500.0, seed + 27, 1))
    for nl in decals["nails"]:
        if nl["member"] != m.name or label != nl["face"]:
            continue
        dist = np.linalg.norm(W - nl["p"][None, :], axis=1)
        dome = np.clip(1 - (dist / nl["r"]) ** 2, 0, 1)
        height = height + 0.0008 * np.sqrt(dome)
        col = col * (1 - 0.35 * (dome > 0))[:, None]
        rough = rough + 0.1 * (dome > 0)
    return col, height, rough, metal


# ---------------------------------------------------------------------------------------------- packing
def skyline_pack(sizes, W, H, gap=1):
    """Skyline packing of (w, h) rects in the given order, each placed (upright or turned 90 deg) where its
    top ends lowest. Returns [(x, y, turned)] or None when they do not fit."""
    sky = np.zeros(W, np.int64)
    out = []
    for w, h in sizes:
        best = None
        for turned, (rw, rh) in ((False, (w, h)), (True, (h, w))):
            if rw + 2 * gap > W or (turned and w == h):
                continue
            win = np.lib.stride_tricks.sliding_window_view(sky, rw + 2 * gap).max(axis=1)
            x = int(np.argmin(win + rh))
            cand = (int(win[x]) + rh, int(win[x]), x, turned, rw, rh)
            if best is None or cand[:3] < best[:3]:
                best = cand
        if best is None:
            return None
        top, y, x, turned, rw, rh = best
        if top + 2 * gap > H:
            return None
        sky[x:x + rw + 2 * gap] = y + rh + gap
        out.append((x + gap, y + gap, turned))
    return out


def layout_islands(islands, tex):
    """Densest texel density (px/m, up to the target) at which every island fits, trying a few orders."""
    M = PARAMS["island_margin_px"]
    n = len(islands)
    keys = {
        "height": lambda i: (-islands[i]["ext"][1] * islands[i]["scale"], -islands[i]["ext"][0] * islands[i]["scale"]),
        "long_side": lambda i: (-max(islands[i]["ext"]) * islands[i]["scale"], -min(islands[i]["ext"]) * islands[i]["scale"]),
        "area": lambda i: (-islands[i]["ext"][0] * islands[i]["ext"][1] * islands[i]["scale"] ** 2,),
    }

    def sizes_for(order, dens):
        return [(int(math.ceil(islands[i]["ext"][0] * dens * islands[i]["scale"])) + 2 * M,
                 int(math.ceil(islands[i]["ext"][1] * dens * islands[i]["scale"])) + 2 * M) for i in order]

    best = None
    for name, key in keys.items():
        order = sorted(range(n), key=lambda i: key(i) + (islands[i]["key"],))
        lo, hi = 100.0, float(PARAMS["texel_target_px_per_m"])
        if skyline_pack(sizes_for(order, hi), tex, tex) is not None:
            lo = hi
        while hi - lo >= 0.5:
            mid = (lo + hi) / 2
            if skyline_pack(sizes_for(order, mid), tex, tex) is not None:
                lo = mid
            else:
                hi = mid
        dens = math.floor(lo)
        if best is None or dens > best[0]:
            best = (dens, name, order)
    dens, name, order = best
    sizes = sizes_for(order, dens)
    placed = skyline_pack(sizes, tex, tex)
    assert placed is not None
    used = 0
    for (w, h), (x, y, turned), i in zip(sizes, placed, order):
        islands[i]["px"] = (x, y)
        islands[i]["turned"] = turned
        islands[i]["size_px"] = (h, w) if turned else (w, h)
        islands[i]["d"] = dens * islands[i]["scale"]
        used += w * h
    return dens, used / float(tex * tex), name


def island_st_to_px(isl, s, t):
    """Island (s, t) in metres -> atlas pixels. A turned island is rotated +90 deg (still right-handed)."""
    M = PARAMS["island_margin_px"]
    x0, y0 = isl["px"]
    if isl["turned"]:
        tmax = isl["tmin"] + isl["ext"][1]
        return x0 + M + (tmax - t) * isl["d"], y0 + M + (s - isl["smin"]) * isl["d"]
    return x0 + M + (s - isl["smin"]) * isl["d"], y0 + M + (t - isl["tmin"]) * isl["d"]


# ---------------------------------------------------------------------------------------------- build
def build_geometry(members):
    all_verts, all_faces, face_member, face_label = [], [], [], []
    loop_uv_src = []   # per face: list of (member idx, label, local vertex)
    islands = {}
    per_member = []
    offset = 0
    for mi, m in enumerate(members):
        v, faces, labels = build_prism(m)
        per_member.append((v, faces, labels))
        for f, lab in zip(faces, labels):
            key = (mi, lab)
            isl = islands.setdefault(key, {"key": f"{m.name}:{lab}", "member": mi, "label": lab, "pts": []})
            isl["pts"].extend(v[f])
    for (mi, lab), isl in islands.items():
        n, u, vv = (np.array(a, np.float64) for a in SIDES[lab])
        pts = np.array(isl["pts"])
        s, t = pts @ u, pts @ vv
        isl["smin"], isl["tmin"] = float(s.min()), float(t.min())
        isl["ext"] = (float(s.max() - s.min()), float(t.max() - t.min()))
        isl["plane"] = float((pts @ n).max()) if lab not in ("x0", "x1") else None
        isl["scale"] = members[mi].scales.get(lab, 1.0)
        del isl["pts"]
    island_list = [islands[k] for k in sorted(islands, key=lambda k: (k[0], k[1]))]
    index = {(isl["member"], isl["label"]): i for i, isl in enumerate(island_list)}
    for mi, (m, (v, faces, labels)) in enumerate(zip(members, per_member)):
        vd = m.deform(v) if m.deform else v
        world = m.to_world(vd)
        all_verts.append(world)
        for f, lab in zip(faces, labels):
            all_faces.append([offset + i for i in f])
            face_member.append(mi)
            face_label.append(lab)
            loop_uv_src.append((index[(mi, lab)], v[f]))
        offset += len(v)
    return np.concatenate(all_verts), all_faces, face_member, face_label, loop_uv_src, island_list


def check_closed(faces, face_member, n_members, verts):
    """Every member closed and consistently wound (each directed edge once, its reverse once) with
    positive signed volume (outward)."""
    report = []
    for mi in range(n_members):
        fs = [f for f, m in zip(faces, face_member) if m == mi]
        directed = {}
        vol = 0.0
        for f in fs:
            for a, b in zip(f, f[1:] + f[:1]):
                directed[(a, b)] = directed.get((a, b), 0) + 1
            p0 = verts[f[0]]
            for k in range(1, len(f) - 1):
                vol += np.dot(p0, np.cross(verts[f[k]], verts[f[k + 1]])) / 6.0
        bad = sum(1 for (a, b), c in directed.items() if c != 1 or directed.get((b, a), 0) != 1)
        report.append({"member": mi, "faces": len(fs), "bad_edges": bad, "volume_m3": vol})
    return report


def vertex_normals(verts, faces):
    """Face-area weighted normals, counting only the largest faces at each vertex, so flat faces stay flat
    and each chamfer strip turns smoothly from one face to the next (catches light like a worn edge)."""
    nv = len(verts)
    fa, fn = [], []
    for f in faces:
        p = verts[f]
        c = np.zeros(3)
        for k in range(1, len(f) - 1):
            c += np.cross(p[k] - p[0], p[k + 1] - p[0])
        a = np.linalg.norm(c) / 2
        fa.append(a)
        fn.append(c / (np.linalg.norm(c) + 1e-20))
    vmax = np.zeros(nv)
    for f, a in zip(faces, fa):
        for i in f:
            vmax[i] = max(vmax[i], a)
    acc = np.zeros((nv, 3))
    for f, a, n in zip(faces, fa, fn):
        for i in f:
            if a >= 0.5 * vmax[i]:
                acc[i] += a * n
    return acc / np.linalg.norm(acc, axis=1, keepdims=True)


def texture_islands(members, islands, tex, ss, decals, seed):
    size = tex * ss
    base = np.zeros((size, size, 3))
    base[:] = srgb_to_lin((150, 112, 72))
    rough = np.full((size, size), 0.7)
    metal = np.zeros((size, size))
    nrm = np.zeros((size, size, 3))
    nrm[..., 2] = 1.0
    M = PARAMS["island_margin_px"]
    for ii, isl in enumerate(islands):
        m = members[isl["member"]]
        lab = isl["label"]
        n, u, v = (np.array(a, np.float64) for a in SIDES[lab])
        x0, y0 = isl["px"]
        w, h = isl["size_px"]
        d = isl["d"]
        cx = (np.arange(w * ss) + 0.5) / ss - M
        cy = (np.arange(h * ss) + 0.5) / ss - M
        CX, CY = np.meshgrid(cx / d, cy / d)
        if isl["turned"]:
            S_, T_ = isl["smin"] + CY, isl["tmin"] + isl["ext"][1] - CX
        else:
            S_, T_ = isl["smin"] + CX, isl["tmin"] + CY
        P = S_.ravel()[:, None] * u[None, :] + T_.ravel()[:, None] * v[None, :]
        if lab in ("x0", "x1"):
            plane = m.plane0 if lab == "x0" else m.plane1
            P[:, 0] = plane_x(plane, P[:, 1], P[:, 2])
        else:
            P = P + isl["plane"] * n[None, :]
        W = m.to_world(P)
        nW = m.rot @ n
        if m.material == "wood":
            col, hgt, rgh, lw, crack = wood_fields(m, P)
            col, hgt, rgh = apply_surface(m, lab, P, W, nW, col, hgt, rgh, seed + ii * 7, decals)
            met = np.zeros(len(P))
        else:
            col, hgt, rgh, met = iron_fields(m, lab, P, W, nW, seed + ii * 7, decals)
        hg = hgt.reshape(h * ss, w * ss)
        step = 1.0 / (d * ss)
        dhdt, dhds = np.gradient(hg, step)
        nn = np.stack([-dhds, -dhdt, np.ones_like(hg)], axis=-1)
        nn /= np.linalg.norm(nn, axis=-1, keepdims=True)
        sl = (slice(y0 * ss, (y0 + h) * ss), slice(x0 * ss, (x0 + w) * ss))
        base[sl] = col.reshape(h * ss, w * ss, 3)
        rough[sl] = rgh.reshape(h * ss, w * ss)
        metal[sl] = met.reshape(h * ss, w * ss)
        nrm[sl] = nn

    def down(a):
        if ss == 1:
            return a
        sh = (tex, ss, tex, ss) + a.shape[2:]
        return a.reshape(sh).mean(axis=(1, 3))

    base, rough, metal, nrm = down(base), down(rough), down(metal), down(nrm)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    return base, np.clip(rough, 0.05, 1.0), np.clip(metal, 0, 1), nrm


def new_image(name, arr, tex, colorspace):
    img = bpy.data.images.new(name, tex, tex, alpha=False, float_buffer=False)
    img.colorspace_settings.name = colorspace
    rgba = np.ones((tex, tex, 4), np.float32)
    rgba[..., :3] = arr
    img.pixels.foreach_set(rgba.ravel())
    img.update()
    return img


def save_image(img, path):
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    img.filepath = path


def gltf_output_group():
    name = "glTF Material Output"
    if name in bpy.data.node_groups:
        return bpy.data.node_groups[name]
    g = bpy.data.node_groups.new(name, "ShaderNodeTree")
    g.interface.new_socket("Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
    g.nodes.new("NodeGroupOutput")
    g.nodes.new("NodeGroupInput")
    return g


def build_material(img_base, img_nrm, img_orm):
    mat = bpy.data.materials.new(f"M_{ASSET_ID}")
    mat.use_nodes = True
    mat.use_backface_culling = True   # closed solids: exported single-sided
    nt = mat.node_tree
    for node in list(nt.nodes):
        nt.nodes.remove(node)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = img_base
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = img_nrm
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    to = nt.nodes.new("ShaderNodeTexImage")
    to.image = img_orm
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(to.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    grp = nt.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    nt.links.new(sep.outputs["Red"], grp.inputs["Occlusion"])
    return mat


def bake_ao(obj, tex, samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.seed = 0
    scene.world.light_settings.distance = 0.35
    img = bpy.data.images.new("ao_bake", tex, tex, alpha=False, float_buffer=True, is_data=True)
    mat = bpy.data.materials.new("M_bake")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    mat.node_tree.nodes.active = node
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type="AO", margin=8, margin_type="EXTEND", use_clear=True)
    px = np.empty(tex * tex * 4, np.float32)
    img.pixels.foreach_get(px)
    obj.data.materials.clear()
    bpy.data.materials.remove(mat)
    return px.reshape(tex, tex, 4)[..., 0].astype(np.float64)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(REPO, "assets", "_staging", "procedural", ASSET_ID))
    ap.add_argument("--seed", type=int, default=1807)
    ap.add_argument("--tex", type=int, default=1024)
    ap.add_argument("--ss", type=int, default=2)
    ap.add_argument("--ao-samples", type=int, default=64)
    ap.add_argument("--dump-textures", default=None, help="also write the three texture PNGs to this folder")
    args = ap.parse_args(argv)
    t0 = time.time()
    os.makedirs(args.out, exist_ok=True)

    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    if bpy.context.scene.world is None:
        bpy.context.scene.world = bpy.data.worlds.new("World")

    rng = np.random.default_rng(args.seed)
    members = make_members(rng)
    verts, faces, face_member, face_label, loop_uv_src, islands = build_geometry(members)
    for mi, m in enumerate(members):
        pts = build_prism(m)[0]
        m.wood = wood_params(np.random.default_rng([args.seed, mi]), m,
                             ((float(pts[:, 0].min()), float(pts[:, 0].max())),
                              float(np.abs(pts[:, 1]).max()), float(np.abs(pts[:, 2]).max())))
    # centre on X/Y, lowest point on the ground
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    verts = verts + shift
    for m in members:
        m.origin = m.origin + shift
    closed = check_closed(faces, face_member, len(members), verts)
    bad = [c for c in closed if c["bad_edges"] or c["volume_m3"] <= 0]
    if bad:
        raise SystemExit(f"open or inverted members: {bad}")

    dens, used, pack_order = layout_islands(islands, args.tex)
    log(f"{len(members)} members, {len(faces)} faces, {len(islands)} islands, {dens} px/m, "
        f"atlas use {used:.1%} ({pack_order} order)")

    # mesh
    mesh = bpy.data.meshes.new(ASSET_ID)
    mesh.from_pydata(verts.tolist(), [], faces)
    if mesh.validate():
        raise SystemExit("mesh.validate() changed the generated mesh")
    mesh.update()
    uvl = mesh.uv_layers.new(name="UVMap")
    uv = []
    for ii, lv in loop_uv_src:
        isl = islands[ii]
        _, u, v = (np.array(a, np.float64) for a in SIDES[isl["label"]])
        px, py = island_st_to_px(isl, lv @ u, lv @ v)
        uv.append(np.c_[px, py] / args.tex)
    uvl.data.foreach_set("uv", np.concatenate(uv).ravel())
    vn = vertex_normals(verts, faces)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(mesh)
    bm.free()
    mesh.shade_smooth()
    # a face more than 60 deg off its vertex's normal (an unchamfered end cut) keeps its own, flat normal
    loop_v = np.empty(len(mesh.loops), np.int64)
    mesh.loops.foreach_get("vertex_index", loop_v)
    starts = np.empty(len(mesh.polygons), np.int64)
    counts = np.empty(len(mesh.polygons), np.int64)
    mesh.polygons.foreach_get("loop_start", starts)
    mesh.polygons.foreach_get("loop_total", counts)
    fnorm = np.empty(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("normal", fnorm)
    loop_f = np.empty(len(mesh.loops), np.int64)
    for i, (s0, c0) in enumerate(zip(starts, counts)):
        loop_f[s0:s0 + c0] = i
    lf = fnorm.reshape(-1, 3)[loop_f]
    lv = vn[loop_v]
    flat = np.einsum("ij,ij->i", lf, lv) < 0.5
    mesh.normals_split_custom_set(np.where(flat[:, None], lf, lv).tolist())
    obj = bpy.data.objects.new(ASSET_ID, mesh)
    bpy.context.scene.collection.objects.link(obj)

    ao = bake_ao(obj, args.tex, args.ao_samples)
    log(f"ao baked ({time.time() - t0:.0f}s)")

    decals = make_decals(np.random.default_rng([args.seed, 999]), members)
    base, rough, metal, nrm = texture_islands(members, islands, args.tex, args.ss, decals, args.seed)
    # dirt settles where the air cannot reach: joints, seams, under the rails
    occl = np.clip(ao, 0, 1)
    dirt = (1 - occl)[..., None]
    base = base * (1 - 0.55 * dirt) + base * srgb_to_lin((120, 104, 88))[None, None, :] * 0.55 * dirt
    rough = np.clip(rough + 0.08 * dirt[..., 0], 0.05, 1)
    log(f"textures ({time.time() - t0:.0f}s)")

    work = tempfile.mkdtemp(prefix="procgen_trestle_")
    img_base = new_image(f"{ASSET_ID}_basecolor", lin_to_srgb(base), args.tex, "sRGB")
    img_nrm = new_image(f"{ASSET_ID}_normal", nrm * 0.5 + 0.5, args.tex, "Non-Color")
    orm = np.stack([np.clip(0.25 + 0.75 * occl, 0, 1), rough, metal], axis=-1)
    img_orm = new_image(f"{ASSET_ID}_orm", orm, args.tex, "Non-Color")
    for img in (img_base, img_nrm, img_orm):
        save_image(img, os.path.join(work, img.name + ".png"))
        if args.dump_textures:
            os.makedirs(args.dump_textures, exist_ok=True)
            with open(os.path.join(work, img.name + ".png"), "rb") as src:
                data = src.read()
            with open(os.path.join(args.dump_textures, img.name + ".png"), "wb") as dst:
                dst.write(data)
    mesh.materials.clear()
    mesh.materials.append(build_material(img_base, img_nrm, img_orm))

    glb = os.path.join(args.out, f"{ASSET_ID}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_apply=True,
                              export_yup=True, export_normals=True, export_tangents=True,
                              export_materials="EXPORT", export_texcoords=True, export_image_format="AUTO",
                              export_extras=False)

    # measurements for the provenance record (glTF axes: x, y up, z front)
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    dims = {"x_length_m": round(float(hi[0] - lo[0]), 4), "y_height_m": round(float(hi[2] - lo[2]), 4),
            "z_depth_m": round(float(hi[1] - lo[1]), 4),
            "min_gltf": [round(float(lo[0]), 4), round(float(lo[2]), 4), round(float(-hi[1]), 4)],
            "max_gltf": [round(float(hi[0]), 4), round(float(hi[2]), 4), round(float(-lo[1]), 4)]}
    tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
    with open(glb, "rb") as fh:
        sha = hashlib.sha256(fh.read()).hexdigest()
    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_trestle.py",
        "command": f'blender --background --factory-startup --python tools/asset_pipeline/_procgen_trestle.py -- '
                   f'--seed {args.seed} --tex {args.tex} --ss {args.ss} --ao-samples {args.ao_samples}',
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.date.today().isoformat(),
        "concept": CONCEPT,
        "parameters": PARAMS,
        "members": [{"name": m.name, "kind": m.kind, "material": m.material} for m in members],
        "dimensions": dims,
        "world_blocker": {"id": "survey_table", "size_m": [1.8, 1.8, 0.9],
                          "side_gap_m": [round((1.8 - dims["x_length_m"]) / 2, 3), round((1.8 - dims["z_depth_m"]) / 2, 3)],
                          "height_shortfall_m": round(0.9 - dims["y_height_m"], 3),
                          "fits_fitting_cs": bool((1.8 - dims["x_length_m"]) / 2 <= 0.505 and (1.8 - dims["z_depth_m"]) / 2 <= 0.505
                                                  and 0.9 - dims["y_height_m"] <= 0.605),
                          "rule": "src/Presentation/Art/Fitting.cs: <= 0.5 m empty blocker per side, top <= 0.6 m below blocker top"},
        "concept_deviations": [
            "1.8 x 0.92 m to the survey_table blocker; the concept table reads longer (about 3:1)",
            "40 mm boards per the plank standard (2-4 cm); the concept's top reads as an ~80 mm slab",
            "7 plank boards butted tight (0.5 mm joints, 1.5 mm eased edges); the concept shows a glued-strip top",
            "no papers or maps: the concept shows none",
            "each trestle's stretcher is the low cross rail between its legs, as drawn; no long stretcher between trestles",
            "ageing (knife gouges on the front edge, pot-ring scorch, ink and cup stains, grime, mud at the feet) "
            "from the request prompt and the build brief; the concept itself is clean",
        ],
        "triangles": tris,
        "materials": {
            "count": 1,
            "name": f"M_{ASSET_ID}",
            "textures": {"base_color": f"{args.tex} px, sRGB", "normal": f"{args.tex} px, tangent space OpenGL (+Y up)",
                         "orm": f"{args.tex} px, R occlusion (Cycles AO bake, 0.35 m), G roughness, B metallic"},
            "texel_density_px_per_m": dens,
            "texel_scale_notes": "density above is for visible faces; hidden faces (board edges between boards, "
                                 "tenons, rail tops, leg tops) 0.1 of it, board undersides 0.3, rail and stretcher "
                                 "undersides 0.5, leg feet 0.25",
            "atlas_use": round(used, 3),
            "atlas_pack_order": pack_order,
            "supersample": args.ss,
            "sources": "procedural in numpy (3D pine: rings round a pith line, knots, checks; ageing decals) and "
                       "Cycles AO; the staged world materials were not used (oak plank floor is dark oak with "
                       "plank seams baked in, wrong for per-member pine grain)",
        },
        "glb_sha256": sha,
        "build_seconds": round(time.time() - t0, 1),
        "closed_members": len(closed),
    }
    prov_path = os.path.join(args.out, f"{ASSET_ID}_provenance.json")
    with open(prov_path, "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    log(f"wrote {glb} ({tris} tris, {dims})")
    print(f"PROCGEN_RESULT {prov_path}", flush=True)


if __name__ == "__main__":
    main()
