"""Procedural build of prop_wooden_crate: a nailed pine shipping crate with a loose lid (hero prop).

The image-to-3D reconstruction in assets/ready is crumpled and tilted (top and base slope 21 and 18 deg) and its texture
carries smeared lettering, so the crate is built here from real member sizes in headless Blender 5.2, after
assets/concepts/prop_wooden_crate.png: four square corner posts standing on the ground, each wall five horizontal
boards between the posts with a framed edge (top and bottom rails) and an X-brace nailed over them, a floor of boards
across two skids, and domed iron nail heads at every joint. The concept is an open crate; the lid asked for by the
brief is built in the same idiom (plank top, framed edge, X-brace, two locating cleats underneath). The concept's
"Merchant" lettering is left out.

Frames: built in Blender Z-up with the crate's length on X and the front (glTF +Z) on -Y; the lid's hinge edge is the
top back edge (+Y). glTF export (+Y up): front +Z, long side on X, centred on X/Z, lowest point y = 0. Nodes:
prop_wooden_crate (the body) with two children, 'lid' (its origin on the top back edge, so a rotation about its local
X opens it) and SOCK_interact_take (library socket convention: 0.85 of the height, +Y out of the top).

Every member is a closed chamfered prism (or a lathed nail head) in its own frame, X along the grain. UVs are projected
per side in that frame (grain runs along each member at one texel density) and packed into one 2048 atlas; faces that
are never seen are packed smaller. A point attribute carries each member-frame position to the shaders, so the pine
is a true 3D wood (growth rings round an off-member pith line, knots where branches leave it, latewood, fibre, saw
marks) and end grain shows rings. The shaders (pine, iron nail heads) are baked with Cycles to base colour (sRGB),
tangent normal (OpenGL) and ORM, and embedded in the GLB. The lid is lifted clear during the bake, so the inside of
the crate carries open-box occlusion. The whole build is deterministic from --seed.

Usage:
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_crate.py -- ^
      [--out-dir DIR] [--seed N] [--res 2048] [--samples 32] [--device auto|cpu] [--no-bake] [--work-dir DIR]
Writes <out-dir>/prop_wooden_crate.glb and prop_wooden_crate_provenance.json (default out-dir:
assets/_staging/procedural/prop_wooden_crate). Textures and a .blend go to --work-dir (default: a temp folder).
The last stdout line is "PROCGEN_RESULT <provenance path>".
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
from mathutils import Matrix, Vector

ASSET_ID = "prop_wooden_crate"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
CONCEPT_REL = "assets/concepts/prop_wooden_crate.png"
DEFAULT_OUT = os.path.join(REPO, "assets", "_staging", "procedural", ASSET_ID)
TAU = 2.0 * math.pi

WOOD, IRON = 0, 1
KINDS = ("wood", "iron")
TRI_BUDGET = 12000

PARAMS = {
    "length_m": 1.20, "depth_m": 0.95, "height_m": 0.95,
    "post_m": 0.07,
    "foot_clearance_m": 0.04,        # walls start 40 mm up; the posts and the skids stand on the ground
    "wall": {"board_t_m": 0.020, "board_count": 5, "board_gap_m": 0.002,
             "board_inset_m": 0.035,  # board outer face behind the post face; the frame lies in front of it
             "frame_t_m": 0.022, "rail_w_m": 0.09, "brace_w_m": 0.08, "board_into_post_m": 0.002},
    "floor": {"t_m": 0.022, "count": 6, "gap_m": 0.003},
    "skid": {"w_m": 0.07, "y_m": 0.28},
    "lid": {"plank_t_m": 0.022, "count": 5, "gap_m": 0.002, "rail_w_m": 0.09, "rail_t_m": 0.022,
            "brace_w_m": 0.08, "cleat_w_m": 0.05, "cleat_h_m": 0.022, "cleat_clear_m": 0.005, "cleat_half_len_m": 0.40},
    "nail": {"head_r_m": 0.0065, "head_h_m": 0.0028, "sink_m": 0.0006, "segments": 6},
    "chamfer_m": {"post": 0.005, "post_top": 0.003, "post_foot": 0.002, "board_out": 0.002, "board_in": 0.0015,
                  "frame_out": 0.003, "frame_in": 0.001, "lid_edge": 0.004, "lid_top": 0.002, "lid_end": 0.003,
                  "skid": 0.004, "cleat": 0.003},
    "texel_target_px_per_m": 512,
    "island_margin_px": 3,
    "texel_scale": {"hidden": 0.1, "gap_edge": 0.15, "nail": 2.0},
    "bake_ao_distance_m": 0.35,
    "bake_lid_lift_m": 1.5,
}

# Side label -> (outward normal, u, v) in the member frame, right-handed (u x v = n), so the tangent frame the engine
# derives from the UVs matches the one the normal map is baked in.
SIDES = {
    "py": ((0, 1, 0), (1, 0, 0), (0, 0, -1)),
    "pz": ((0, 0, 1), (1, 0, 0), (0, 1, 0)),
    "my": ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
    "mz": ((0, 0, -1), (1, 0, 0), (0, -1, 0)),
    "x0": ((-1, 0, 0), (0, 1, 0), (0, 0, -1)),
    "x1": ((1, 0, 0), (0, 1, 0), (0, 0, 1)),
}
EX, EY, EZ = np.eye(3)


def log(msg):
    print(f"[procgen_crate] {msg}", flush=True)


# --------------------------------------------------------------------------------------------------------------------
# Members: closed chamfered prisms and lathed nail heads, each in its own frame
# --------------------------------------------------------------------------------------------------------------------

def chamfer_rect(hy, hz, c_pp, c_mp, c_mm, c_pm):
    """Chamfered rectangle, CCW seen from +X: [(y, z, label of the edge leaving it)]. Chamfer strips belong to the
    adjacent Z side (so a board's eased edges carry its face texture)."""
    return [(hy, -hz + c_pm, "py"), (hy, hz - c_pp, "pz"), (hy - c_pp, hz, "pz"), (-hy + c_mp, hz, "pz"),
            (-hy, hz - c_mp, "my"), (-hy, -hz + c_mm, "mz"), (-hy + c_mm, -hz, "mz"), (hy - c_pm, -hz, "mz")]


def inset_profile(hy, hz, cs, e):
    """The profile shrunk by e on every side for an end chamfer band, same vertex order; each corner chamfer shrinks
    by the offset amount and stops at 0.3 mm, so a narrow chamfer cannot turn inside out."""
    k = 2 * math.tan(math.pi / 8) / math.sqrt(2) * e
    c = [max(v - k, 0.0003) for v in cs]
    return [(y, z) for y, z, _ in chamfer_rect(hy - e, hz - e, *c)]


def plane_x(plane, y, z):
    a, sy, sz = plane
    return a + sy * y + sz * z


def frame(xm, zm):
    """Rotation whose columns are member X, Y, Z (right-handed) from X and Z."""
    xm = np.asarray(xm, np.float64)
    zm = np.asarray(zm, np.float64)
    xm = xm / np.linalg.norm(xm)
    zm = zm - xm * zm.dot(xm)
    zm = zm / np.linalg.norm(zm)
    ym = np.cross(zm, xm)
    rot = np.stack([xm, ym, zm], axis=1)
    assert abs(np.linalg.det(rot) - 1) < 1e-9
    return rot


def plane_in_member(rot, origin, point, normal):
    """A world plane (point, normal) as x = a + sy*y + sz*z in the member frame."""
    n = np.asarray(normal, np.float64)
    nx, ny, nz = n @ rot[:, 0], n @ rot[:, 1], n @ rot[:, 2]
    d = n @ (np.asarray(point, np.float64) - origin)
    assert abs(nx) > 1e-6, "cut plane parallel to the member"
    return (d / nx, -ny / nx, -nz / nx)


class Part:
    def __init__(self, name, kind, group, material, rot, origin):
        self.name, self.kind, self.group, self.material = name, kind, group, material
        self.rot = np.asarray(rot, np.float64)
        self.origin = np.asarray(origin, np.float64)
        self.local = None       # (N, 3) member-frame vertices
        self.faces = None       # vertex index lists, outward winding
        self.labels = None      # side label per face
        self.scales = {}        # texel scale per side label
        self.wood = None        # shader parameters
        self.hy = self.hz = 0.0

    def to_world(self, p):
        return np.asarray(p, np.float64) @ self.rot.T + self.origin


def prism(name, kind, group, rot, origin, plane0, plane1, hy, hz, cs, e=(0.0, 0.0), scales=None):
    """Chamfered prism between two end planes (member frame), end chamfers e0/e1 (0 = a plain butt cut)."""
    p = Part(name, kind, group, WOOD, rot, origin)
    p.hy, p.hz = hy, hz
    prof = chamfer_rect(hy, hz, *cs)
    pts = [(y, z) for y, z, _ in prof]
    labels = [lab for _, _, lab in prof]
    n = len(pts)
    e0, e1 = e
    rings = []
    if e0 > 0:
        rings.append(("cap0", [(plane_x(plane0, y, z), y, z) for y, z in inset_profile(hy, hz, cs, e0)]))
    for t in (0.0, 1.0):
        ring = []
        for y, z in pts:
            xs = plane_x(plane0, y, z) + e0
            xe = plane_x(plane1, y, z) - e1
            assert xe - xs > 1e-3, f"{name}: end planes cross"
            ring.append((xs + t * (xe - xs), y, z))
        rings.append(("body", ring))
    if e1 > 0:
        rings.append(("cap1", [(plane_x(plane1, y, z), y, z) for y, z in inset_profile(hy, hz, cs, e1)]))
    verts = [v for _, ring in rings for v in ring]
    faces, flabels = [], []
    for j in range(len(rings) - 1):
        for k in range(n):
            k1 = (k + 1) % n
            faces.append([j * n + k, j * n + k1, (j + 1) * n + k1, (j + 1) * n + k])
            if rings[j][0] == "cap0":
                flabels.append("x0")
            elif rings[j + 1][0] == "cap1":
                flabels.append("x1")
            else:
                flabels.append(labels[k])
    last = len(rings) - 1
    faces.append(list(reversed(range(n))))
    flabels.append("x0")
    faces.append([last * n + k for k in range(n)])
    flabels.append("x1")
    p.local = np.array(verts, np.float64)
    p.faces, p.labels = faces, flabels
    p.plane0, p.plane1 = plane0, plane1
    p.scales = dict(scales or {})
    return p


def straight(name, kind, group, rot, origin, x0, x1, hy, hz, cs, e=(0.0, 0.0), scales=None):
    return prism(name, kind, group, rot, origin, (x0, 0.0, 0.0), (x1, 0.0, 0.0), hy, hz, cs, e, scales)


def nail(name, group, point, normal, rng):
    """Domed iron nail head, its flat underside sunk a little into the wood; member Z out along the nail."""
    NP = PARAMS["nail"]
    n = np.asarray(normal, np.float64)
    n = n / np.linalg.norm(n)
    tilt = rng.normal(0, math.radians(2.5), 2)
    helper = EX if abs(n[0]) < 0.9 else EY
    a = np.cross(n, helper)
    a /= np.linalg.norm(a)
    b = np.cross(n, a)
    axis = n + a * math.tan(tilt[0]) + b * math.tan(tilt[1])
    axis /= np.linalg.norm(axis)
    xm = a - axis * a.dot(axis)
    xm /= np.linalg.norm(xm)
    rot = np.stack([xm, np.cross(axis, xm), axis], axis=1)
    p = Part(name, "nail", group, IRON, rot, point)
    r = NP["head_r_m"] * rng.uniform(0.9, 1.1)
    h = NP["head_h_m"] * rng.uniform(0.75, 1.15)
    sink = NP["sink_m"] + rng.uniform(0.0, 0.0006)
    seg = NP["segments"]
    phase = rng.uniform(0, TAU)
    stations = [(-sink, r), (h * 0.45, r * 0.82), (h, r * 0.36)]
    verts = []
    for z, rad in stations:
        for j in range(seg):
            th = phase + TAU * j / seg
            verts.append((rad * math.cos(th), rad * math.sin(th), z))
    faces, labels = [], []
    for i in range(len(stations) - 1):
        for j in range(seg):
            j1 = (j + 1) % seg
            faces.append([i * seg + j, i * seg + j1, (i + 1) * seg + j1, (i + 1) * seg + j])
            labels.append("pz")
    last = len(stations) - 1
    faces.append([last * seg + j for j in range(seg)])
    labels.append("pz")
    faces.append(list(reversed(range(seg))))
    labels.append("mz")
    p.local = np.array(verts, np.float64)
    p.faces, p.labels = faces, labels
    ts = PARAMS["texel_scale"]
    p.scales = {"pz": ts["nail"], "mz": ts["hidden"]}
    return p


# --------------------------------------------------------------------------------------------------------------------
# Crate assembly
# --------------------------------------------------------------------------------------------------------------------

def split_widths(rng, total, count, gap):
    w = rng.uniform(0.88, 1.12, count)
    return w / w.sum() * (total - (count - 1) * gap)


def xbrace(parts, nails, rng, tag, group, kind, O, e_s, e_t, n, S, t_lo, t_hi, w, thick, cs, scales, flip):
    """X-brace inside the rectangle s in [-S, S], t in [t_lo, t_hi] of a layer centred on plane O (normal n): one
    continuous diagonal and one in two halves butted against it; the ends are cut square to t (against the rails),
    held 4 mm clear of the posts."""
    O, e_s, e_t, n = (np.asarray(v, np.float64) for v in (O, e_s, e_t, n))
    tc = 0.5 * (t_lo + t_hi)
    C = O + e_t * tc
    delta = 0.0
    for _ in range(12):
        th = math.atan2(t_hi - t_lo, 2 * (S - delta))
        delta = (w / 2) / math.sin(th) + 0.004
    sg = -1.0 if flip else 1.0
    d1 = math.cos(th) * e_s + sg * math.sin(th) * e_t
    d2 = math.cos(th) * e_s - sg * math.sin(th) * e_t
    hy, hz = w / 2, thick / 2

    def cut(rot, t):
        return plane_in_member(rot, C, O + e_t * t, e_t)

    def nail_pair(p, plane, inward):
        for yy in (-0.22 * w, 0.22 * w):
            x = plane_x(plane, yy, hz) + inward * rng.uniform(0.032, 0.042)
            nails.append(nail(f"nail_{p.name}", group, p.to_world([x, yy + rng.normal(0, 0.002), hz]), p.rot[:, 2], rng))

    r1 = frame(d1, n)
    t0, t1 = (t_lo, t_hi) if sg > 0 else (t_hi, t_lo)
    full = prism(f"{tag}_brace", kind, group, r1, C, cut(r1, t0), cut(r1, t1), hy, hz, cs, scales=scales)
    parts.append(full)
    nail_pair(full, full.plane0, 1)
    nail_pair(full, full.plane1, -1)
    nails.append(nail(f"nail_{full.name}", group, full.to_world([rng.normal(0, 0.002), rng.normal(0, 0.002), hz]),
                      full.rot[:, 2], rng))
    r2 = frame(d2, n)
    s0, s1 = (t_hi, t_lo) if sg > 0 else (t_lo, t_hi)
    p1 = np.cross(n, d1)
    side = 1.0 if np.dot(-d2, p1) > 0 else -1.0
    near = plane_in_member(r2, C, C + p1 * side * (w / 2), p1)
    far = plane_in_member(r2, C, C - p1 * side * (w / 2), p1)
    half_a = prism(f"{tag}_brace_a", kind, group, r2, C, cut(r2, s0), near, hy, hz, cs, scales=scales)
    half_b = prism(f"{tag}_brace_b", kind, group, r2, C, far, cut(r2, s1), hy, hz, cs, scales=scales)
    parts += [half_a, half_b]
    nail_pair(half_a, half_a.plane0, 1)
    nail_pair(half_b, half_b.plane1, -1)
    for p, plane, inward in ((half_a, half_a.plane1, -1), (half_b, half_b.plane0, 1)):
        x = plane_x(plane, 0.0, hz) + inward * rng.uniform(0.03, 0.04)
        nails.append(nail(f"nail_{p.name}", group, p.to_world([x, rng.normal(0, 0.003), hz]), p.rot[:, 2], rng))
    return math.degrees(th)


def rail_nails(nails, rng, p, S, group):
    """Two staggered nails at each end of a frame rail and one every ~0.3 m between."""
    hz, hy = p.hz, p.hy
    for sx in (-1, 1):
        nails.append(nail(f"nail_{p.name}", group, p.to_world([sx * (S - rng.uniform(0.024, 0.03)), 0.5 * hy, hz]),
                          p.rot[:, 2], rng))
        nails.append(nail(f"nail_{p.name}", group, p.to_world([sx * (S - rng.uniform(0.042, 0.05)), -0.5 * hy, hz]),
                          p.rot[:, 2], rng))
    k = max(1, int(round(2 * S / 0.3)) - 1)
    for i in range(k):
        x = -S + 2 * S * (i + 1) / (k + 1) + rng.normal(0, 0.01)
        nails.append(nail(f"nail_{p.name}", group, p.to_world([x, rng.normal(0, 0.006), hz]), p.rot[:, 2], rng))


def build_crate(rng):
    P = PARAMS
    L, D, H = P["length_m"], P["depth_m"], P["height_m"]
    ps, fc = P["post_m"], P["foot_clearance_m"]
    W, F, K, LD, ch, ts = P["wall"], P["floor"], P["skid"], P["lid"], P["chamfer_m"], P["texel_scale"]
    hid, gap_edge = ts["hidden"], ts["gap_edge"]
    z_b = H - LD["plank_t_m"] - LD["rail_t_m"]
    parts, nails = [], []
    info = {"body_top_z": z_b}
    rw, ft, bt, bw = W["rail_w_m"], W["frame_t_m"], W["board_t_m"], W["brace_w_m"]

    # corner posts: member X up, Y along world X, Z along world Y
    for sx in (-1, 1):
        for sy in (-1, 1):
            cx, cy = sx * (L / 2 - ps / 2), sy * (D / 2 - ps / 2)
            rot = np.stack([EZ, EX, EY], axis=1)
            c = ch["post"]
            p = straight(f"post_{'LR'[sx > 0]}{'FB'[sy > 0]}", "post", "body", rot, (cx, cy, 0.0), 0.0, z_b, ps / 2,
                         ps / 2, (c, c, c, c), e=(ch["post_foot"], ch["post_top"]),
                         scales={"x0": hid, "x1": 0.5})
            parts.append(p)
            for zc in (fc + rw / 2, z_b - rw / 2):
                for nrm, pos in ((sx * EX, (sx * L / 2, cy, zc)), (sy * EY, (cx, sy * D / 2, zc))):
                    jit = rng.normal(0, 0.003, 3) * (1 - np.abs(nrm))
                    nails.append(nail(f"nail_{p.name}", "body", np.array(pos) + jit, nrm, rng))

    # walls: boards between the posts, a framed edge and an X-brace nailed over them
    walls = (("front", -EY, D / 2, L / 2 - ps), ("back", EY, D / 2, L / 2 - ps),
             ("left", -EX, L / 2, D / 2 - ps), ("right", EX, L / 2, D / 2 - ps))
    flips = rng.random(4) < 0.5
    info["brace_angles_deg"] = {}
    for wi, (tag, n, d_out, S) in enumerate(walls):
        e_s = np.cross(EZ, n)
        d_board = d_out - W["board_inset_m"] - bt / 2
        d_frame = d_out - W["board_inset_m"] + ft / 2
        rot = frame(e_s, n)
        widths = split_widths(rng, z_b - fc, W["board_count"], W["board_gap_m"])
        z = fc
        for bi, wdt in enumerate(widths):
            zc = z + wdt / 2
            z += wdt + W["board_gap_m"]
            reach = S + W["board_into_post_m"]
            parts.append(straight(f"{tag}_board_{bi}", "board", "body", rot, n * d_board + EZ * zc, -reach, reach,
                                  wdt / 2, bt / 2, (ch["board_out"], ch["board_out"], ch["board_in"], ch["board_in"]),
                                  scales={"py": gap_edge, "my": gap_edge, "x0": hid, "x1": hid}))
        cs_frame = (ch["frame_out"], ch["frame_out"], ch["frame_in"], ch["frame_in"])
        frame_scales = {"mz": hid, "x0": hid, "x1": hid}
        for rtag, zc in (("rail_top", z_b - rw / 2), ("rail_bottom", fc + rw / 2)):
            p = straight(f"{tag}_{rtag}", "rail", "body", rot, n * d_frame + EZ * zc, -S, S, rw / 2, ft / 2, cs_frame,
                         scales=frame_scales)
            parts.append(p)
            rail_nails(nails, rng, p, S, "body")
        info["brace_angles_deg"][tag] = round(xbrace(
            parts, nails, rng, tag, "body", "brace", n * d_frame, e_s, EZ, n, S, fc + rw, z_b - rw, bw, ft, cs_frame,
            frame_scales, bool(flips[wi])), 2)

    # floor boards across the crate on two skids along it
    x_in = L / 2 - W["board_inset_m"] - bt
    y_in = D / 2 - W["board_inset_m"] - bt
    rz = np.stack([EY, -EX, EZ], axis=1)          # member X along world Y
    widths = split_widths(rng, 2 * x_in, F["count"], F["gap_m"])
    x = -x_in
    zf = fc + F["t_m"] / 2
    for bi, wdt in enumerate(widths):
        xc = x + wdt / 2
        x += wdt + F["gap_m"]
        p = straight(f"floor_board_{bi}", "floor", "body", rz, (xc, 0.0, zf), -y_in, y_in, wdt / 2, F["t_m"] / 2,
                     (ch["board_out"], ch["board_out"], ch["board_in"], ch["board_in"]),
                     scales={"py": gap_edge, "my": gap_edge, "mz": gap_edge, "x0": hid, "x1": hid})
        parts.append(p)
        for sy in (-1, 1):
            for yy in (-0.3, 0.3):
                q = [sy * K["y_m"] + rng.normal(0, 0.006), yy * wdt / 2 + rng.normal(0, 0.004), F["t_m"] / 2]
                nails.append(nail(f"nail_{p.name}", "body", p.to_world(q), EZ, rng))
    skid_x = L / 2 - W["board_inset_m"]
    for sy in (-1, 1):
        c = ch["skid"]
        parts.append(straight(f"skid_{'FB'[sy > 0]}", "skid", "body", np.eye(3), (0.0, sy * K["y_m"], fc / 2), -skid_x,
                              skid_x, K["w_m"] / 2, fc / 2, (0.001, 0.001, c, c), e=(ch["lid_end"], ch["lid_end"]),
                              scales={"pz": hid, "mz": hid, "py": 0.5, "my": 0.5}))

    # lid: planks along the crate, framed edge and X-brace on top, locating cleats underneath
    pt, lrw, lrt = LD["plank_t_m"], LD["rail_w_m"], LD["rail_t_m"]
    widths = split_widths(rng, D, LD["count"], LD["gap_m"])
    y = -D / 2
    for bi, wdt in enumerate(widths):
        yc = y + wdt / 2
        y += wdt + LD["gap_m"]
        front, back = bi == 0, bi == LD["count"] - 1
        cs = (ch["lid_edge"] if back else ch["lid_top"], ch["lid_edge"] if front else ch["lid_top"],
              ch["board_in"], ch["board_in"])
        parts.append(straight(f"lid_plank_{bi}", "lid_plank", "lid", np.eye(3), (0.0, yc, z_b + pt / 2), -L / 2, L / 2,
                              wdt / 2, pt / 2, cs, e=(ch["lid_end"], ch["lid_end"]),
                              scales={"py": 1.0 if back else gap_edge, "my": 1.0 if front else gap_edge}))
    z_r = z_b + pt + lrt / 2
    lcs = (ch["lid_edge"], ch["lid_edge"], ch["frame_in"], ch["frame_in"])
    for sy in (-1, 1):
        p = straight(f"lid_rail_{'FB'[sy > 0]}", "lid_rail", "lid", np.eye(3), (0.0, sy * (D / 2 - lrw / 2), z_r),
                     -L / 2, L / 2, lrw / 2, lrt / 2, lcs, e=(ch["lid_end"], ch["lid_end"]), scales={"mz": hid})
        parts.append(p)
        rail_nails(nails, rng, p, L / 2, "lid")
    y_span = D / 2 - lrw
    for sx in (-1, 1):
        p = straight(f"lid_rail_{'LR'[sx > 0]}", "lid_rail", "lid", rz, (sx * (L / 2 - lrw / 2), 0.0, z_r), -y_span,
                     y_span, lrw / 2, lrt / 2, lcs, scales={"mz": hid, "x0": hid, "x1": hid})
        parts.append(p)
        y = -D / 2
        for wdt in widths:
            yc = y + wdt / 2
            y += wdt + LD["gap_m"]
            if abs(yc) < y_span - 0.03:
                nails.append(nail(f"nail_{p.name}", "lid", p.to_world([yc + rng.normal(0, 0.006),
                                                                      rng.normal(0, 0.008), lrt / 2]), EZ, rng))
    info["brace_angles_deg"]["lid"] = round(xbrace(
        parts, nails, rng, "lid", "lid", "lid_brace", (0.0, 0.0, z_r), EX, EY, EZ, L / 2 - lrw, -y_span, y_span,
        LD["brace_w_m"], lrt, lcs, {"mz": hid, "x0": hid, "x1": hid}, bool(rng.random() < 0.5)), 2)
    cw, chh = LD["cleat_w_m"], LD["cleat_h_m"]
    xc = x_in - LD["cleat_clear_m"] - cw / 2
    c = ch["cleat"]
    for sx in (-1, 1):
        parts.append(straight(f"lid_cleat_{'LR'[sx > 0]}", "cleat", "lid", rz, (sx * xc, 0.0, z_b - chh / 2),
                              -LD["cleat_half_len_m"], LD["cleat_half_len_m"], cw / 2, chh / 2, (0.001, 0.001, c, c),
                              e=(c, c), scales={"pz": hid}))
    info["lid_pivot"] = np.array([0.0, D / 2, z_b])
    info["interior_clearances_m"] = {
        "cleat_to_short_wall": round(x_in - (xc + cw / 2), 4),
        "cleat_to_post_y": round((D / 2 - ps) - LD["cleat_half_len_m"], 4),
        "cleat_to_long_wall": round(y_in - LD["cleat_half_len_m"], 4)}
    return parts, nails, info


# --------------------------------------------------------------------------------------------------------------------
# Wood parameters (per member, drive the baked shader)
# --------------------------------------------------------------------------------------------------------------------

def wood_params(rng, p):
    if p.material != WOOD:
        return {"rand": float(rng.random()), "pith": (0.0, 0.0, 0.005), "pdir": (1.0, 0.0, 0.0),
                "pslope": (0.0, 0.0, 0.0)}
    if p.kind == "post":
        ang, dist = rng.uniform(0, TAU), rng.uniform(0.02, 0.09)
    else:
        # a mixed stack: flat-sawn (pith over or under the board face: cathedral figure) or rift-sawn (pith off to
        # one edge: tighter, straighter lines)
        if rng.random() < 0.65:
            ang = rng.choice([-1, 1]) * math.pi / 2 + rng.normal(0, 0.25)
            dist = rng.uniform(0.05, 0.16)
        else:
            ang = rng.choice([0.0, math.pi]) + rng.choice([-1, 1]) * rng.uniform(0.3, 0.8)
            dist = rng.uniform(0.07, 0.2) if p.hy > 0.035 else rng.uniform(0.05, 0.14)
    py0, pz0 = dist * math.cos(ang), dist * math.sin(ang)
    psi = math.atan2(-pz0, -py0)
    kden = rng.uniform(0.3, 0.6) if p.kind in ("board", "floor", "lid_plank") else rng.uniform(0.15, 0.4)
    # the log's axis is never quite along the board: the pith line drifts through it, which draws the arches
    slope = rng.normal(0, 0.018, 2)
    return {"rand": float(rng.random()), "pith": (py0, pz0, rng.uniform(0.0035, 0.0065)),
            "pdir": (math.cos(psi), math.sin(psi), kden), "pslope": (float(slope[0]), float(slope[1]), 0.0)}


# --------------------------------------------------------------------------------------------------------------------
# Validation
# --------------------------------------------------------------------------------------------------------------------

def validate_parts(verts, faces, face_part, parts):
    """Per part: closed (every edge used once each way), positive volume; no bow-tie quads; no degenerate faces."""
    report = {"parts": len(parts), "open_edges": 0, "misoriented_edges": 0, "negative_volume_parts": [],
              "bowtie_quads": 0, "degenerate_faces": 0}
    by_part = {}
    for fi, pi in enumerate(face_part):
        by_part.setdefault(pi, []).append(fi)
    for pi, fl in by_part.items():
        directed = {}
        vol = 0.0
        for fi in fl:
            f = faces[fi]
            for k in range(len(f)):
                e = (f[k], f[(k + 1) % len(f)])
                directed[e] = directed.get(e, 0) + 1
            p0 = verts[f[0]]
            for k in range(1, len(f) - 1):
                vol += np.dot(p0, np.cross(verts[f[k]], verts[f[k + 1]])) / 6.0
        for (a, c), cnt in directed.items():
            if cnt > 1:
                report["misoriented_edges"] += 1
            if (c, a) not in directed:
                report["open_edges"] += 1
        if vol <= 0:
            report["negative_volume_parts"].append(parts[pi].name)
    for f in faces:
        P = verts[f]
        nrm = np.zeros(3)
        for k in range(len(P)):
            nrm += np.cross(P[k], P[(k + 1) % len(P)])
        if np.linalg.norm(nrm) < 2e-10:
            report["degenerate_faces"] += 1
            continue
        if len(P) == 4:
            ok = all(np.dot(np.cross(P[a] - P[o], P[c] - P[o]), nrm) > 0
                     for o, a, c in ((0, 1, 2), (0, 2, 3), (0, 1, 3), (1, 2, 3)))
            if not ok:
                report["bowtie_quads"] += 1
    return report


def mesh_checks(me):
    """Triangulated mesh: 3D-degenerate triangles and zero-area UV triangles (plus the smallest UV triangle in px^2)."""
    n = len(me.polygons)
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    ls = np.empty(n, np.int64)
    me.polygons.foreach_get("loop_start", ls)
    lt = np.empty(n, np.int64)
    me.polygons.foreach_get("loop_total", lt)
    assert (lt == 3).all()
    idx = ls[:, None] + np.arange(3)[None, :]
    P = co[lv[idx]]
    a3 = np.linalg.norm(np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0]), axis=1) / 2
    U = uv[idx]
    e1, e2 = U[:, 1] - U[:, 0], U[:, 2] - U[:, 0]
    a2 = np.abs(e1[:, 0] * e2[:, 1] - e1[:, 1] * e2[:, 0]) / 2
    return a3, a2


# --------------------------------------------------------------------------------------------------------------------
# Mesh, UVs, normals
# --------------------------------------------------------------------------------------------------------------------

def select_only(obj):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def skyline_pack(sizes, W, H, gap=1):
    """Skyline packing of (w, h) rects in the given order, each placed (upright or turned 90 deg) where its top ends
    lowest. Returns [(x, y, turned)] or None when they do not fit."""
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
        islands[i]["d"] = dens * islands[i]["scale"]
        used += w * h
    return {"px_per_m": dens, "atlas_use": round(used / float(tex * tex), 4), "pack_order": name,
            "islands": n, "margin_px": M}


def island_st_to_px(isl, s, t):
    """Island (s, t) in metres -> atlas pixels. A turned island is rotated +90 deg (still right-handed)."""
    M = PARAMS["island_margin_px"]
    x0, y0 = isl["px"]
    if isl["turned"]:
        tmax = isl["tmin"] + isl["ext"][1]
        return x0 + M + (tmax - t) * isl["d"], y0 + M + (s - isl["smin"]) * isl["d"]
    return x0 + M + (s - isl["smin"]) * isl["d"], y0 + M + (t - isl["tmin"]) * isl["d"]


def make_object(parts, shift, tex):
    verts, faces, face_part, face_scale, face_island = [], [], [], [], []
    mpos = []
    islands, index = [], {}
    off = 0
    for pi, p in enumerate(parts):
        world = p.to_world(p.local) + shift
        verts.append(world)
        mpos.append(p.local)
        for lab in sorted(set(p.labels)):
            _, u, v = (np.array(a, np.float64) for a in SIDES[lab])
            pts = np.concatenate([p.local[f] for f, lb in zip(p.faces, p.labels) if lb == lab])
            s, t = pts @ u, pts @ v
            index[(pi, lab)] = len(islands)
            islands.append({"key": f"{pi:04d}:{lab}", "label": lab, "scale": p.scales.get(lab, 1.0),
                            "smin": float(s.min()), "tmin": float(t.min()),
                            "ext": (float(s.max() - s.min()), float(t.max() - t.min()))})
        for f, lab in zip(p.faces, p.labels):
            faces.append([off + i for i in f])
            face_part.append(pi)
            face_scale.append(p.scales.get(lab, 1.0))
            face_island.append(index[(pi, lab)])
        off += len(p.local)
    pack = layout_islands(islands, tex)
    loop_uv = []
    for pi, p in enumerate(parts):
        for f, lab in zip(p.faces, p.labels):
            isl = islands[index[(pi, lab)]]
            _, u, v = (np.array(a, np.float64) for a in SIDES[lab])
            lv = p.local[f]
            px, py = island_st_to_px(isl, lv @ u, lv @ v)
            loop_uv.append(np.c_[px, py] / tex)
    verts = np.concatenate(verts)
    mpos = np.concatenate(mpos)
    me = bpy.data.meshes.new(ASSET_ID)
    me.from_pydata(verts.tolist(), [], faces)
    if me.validate():
        raise SystemExit("mesh.validate() changed the generated mesh")
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", np.concatenate(loop_uv).astype(np.float32).ravel())
    me.polygons.foreach_set("material_index", [parts[pi].material for pi in face_part])
    a = me.attributes.new("mpos", "FLOAT_VECTOR", "POINT")
    a.data.foreach_set("vector", mpos.astype(np.float32).ravel())
    for name, fn in (("prand", lambda p: p.wood["rand"]), ("plid", lambda p: 1.0 if p.group == "lid" else 0.0)):
        a = me.attributes.new(name, "FLOAT", "FACE")
        a.data.foreach_set("value", [float(fn(parts[pi])) for pi in face_part])
    a = me.attributes.new("pscale", "FLOAT", "FACE")
    a.data.foreach_set("value", [float(s) for s in face_scale])
    pend = []
    for p in parts:
        for lab in p.labels:
            pend.append(1.0 if (lab in ("x0", "x1") and p.material == WOOD) else 0.0)
    a = me.attributes.new("pend", "FLOAT", "FACE")
    a.data.foreach_set("value", pend)
    for name in ("pith", "pdir", "pslope"):
        a = me.attributes.new(name, "FLOAT_VECTOR", "FACE")
        a.data.foreach_set("vector", np.array([parts[pi].wood[name] for pi in face_part], np.float32).ravel())
    me.uv_layers.active = uvl
    obj = bpy.data.objects.new(ASSET_ID, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj, verts, faces, face_part, pack


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


def face_array(me, name):
    out = np.empty(len(me.polygons), np.float32)
    me.attributes[name].data.foreach_get("value", out)
    return out


def uv_stats(obj, res, pack):
    """Measured texel density of the full-scale faces (UV area over 3D area; chamfer strips projected onto their face
    read a little lower) and the share of the atlas the UV triangles cover."""
    me = obj.data
    a3, a2 = mesh_checks(me)
    full = face_array(me, "pscale") == 1.0
    return {**pack, "px_per_m_measured": round(math.sqrt(a2[full].sum() / a3[full].sum()) * res, 1),
            "uv_fill": round(float(a2.sum()), 4), "surface_m2": round(float(a3.sum()), 3)}


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

    def sin(self, a): return self.m("SINE", a)
    def abs(self, a): return self.m("ABSOLUTE", a)
    def max(self, a, c): return self.m("MAXIMUM", a, c)
    def min(self, a, c): return self.m("MINIMUM", a, c)
    def pow(self, a, c): return self.m("POWER", self.m("MAXIMUM", a, 0.0), c)
    def sat(self, a): return self.m("ADD", a, 0.0, clamp=True)
    def fract(self, a): return self.m("FRACT", a)
    def floor(self, a): return self.m("FLOOR", a)
    def sqrt(self, a): return self.m("SQRT", self.m("MAXIMUM", a, 0.0))
    def atan2(self, y, x): return self.m("ARCTAN2", y, x)

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

    def voronoi(self, v, scale=1.0, jitter=1.0, dims="3D"):
        """F1 distance (in the scaled space) and the cell's random colour."""
        n = self.node("ShaderNodeTexVoronoi", voronoi_dimensions=dims, feature="F1")
        self.put(n.inputs["Vector"], v)
        self.put(n.inputs["Scale"], scale)
        self.put(n.inputs["Randomness"], jitter)
        return Val(self, n.outputs["Distance"], "f"), Val(self, n.outputs["Color"], "v")

    def ramp(self, t, stops):
        n = self.node("ShaderNodeValToRGB")
        els = n.color_ramp.elements
        while len(els) < len(stops):
            els.new(0.5)
        for e, (pos, col) in zip(els, stops):
            e.position = pos
            e.color = tuple(col) + (1.0,)
        self.put(n.inputs[0], t)
        return Val(self, n.outputs[0], "v")

    def attr(self, name, vector=False):
        n = self.node("ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=name)
        return Val(self, n.outputs["Vector" if vector else "Factor"], "v" if vector else "f")


class Ctx:
    pass


def context(g):
    c = Ctx()
    c.mpos = g.attr("mpos", vector=True)
    c.pith = g.attr("pith", vector=True)
    c.pdir = g.attr("pdir", vector=True)
    c.pslope = g.attr("pslope", vector=True)
    c.rand = g.attr("prand")
    c.pend = g.attr("pend")
    geo = g.node("ShaderNodeNewGeometry")
    c.pos = Val(g, geo.outputs["Position"], "v")
    c.nrm = Val(g, geo.outputs["Normal"], "v")
    _, _, c.pz = g.sep(c.pos)
    _, _, c.nz = g.sep(c.nrm)
    ao = g.node("ShaderNodeAmbientOcclusion", samples=16, only_local=False, inside=False)
    g.put(ao.inputs["Distance"], PARAMS["bake_ao_distance_m"])
    c.ao = Val(g, ao.outputs["AO"], "f")
    bev = g.node("ShaderNodeBevel", samples=8)
    g.put(bev.inputs["Radius"], 0.004)
    c.edge = g.smooth(0.992, 0.93, g.dot(Val(g, bev.outputs["Normal"], "v"), c.nrm))
    c.cvx = c.edge * g.smooth(0.55, 0.9, c.ao)     # convex (exposed) edges only
    return c


def sh_pine(g, c):
    """Pine in 3D member space: rings round a wandering pith line off the member, knots where branches leave it."""
    x, y, z = g.sep(c.mpos)
    py0, pz0, ring = g.sep(c.pith)
    cs, sn, kden = g.sep(c.pdir)
    sy, sz, _ = g.sep(c.pslope)
    r0 = c.rand
    wy = g.noise(g.vec(x * 0.7, r0 * 31.0, 1.7), 0.3, detail=2.0) - 0.5
    wz = g.noise(g.vec(x * 0.7, r0 * 37.0, 5.3), 0.6, detail=2.0) - 0.5
    dy = y - py0 - sy * x - wy * 0.02
    dz = z - pz0 - sz * x - wz * 0.02
    a = dy * cs + dz * sn
    b = dz * cs - dy * sn
    rr = g.sqrt(dy * dy + dz * dz)
    th = g.atan2(b, a)                               # angle round the pith, ~0 across the member (no seam)
    # knots: one candidate per cell in (along the grain, round the pith); only some cells carry a branch
    # 2D cells: a slice through 3D cells would rarely pass near a feature point, and knots would all but vanish
    kd_s, kcol = g.voronoi(g.vec(x + r0 * 37.0, th * 0.2, 0.0), scale=4.0, dims="2D")
    kd = kd_s * (1.0 / 4.0)
    kc_r, kc_g, kc_b = g.sep(kcol)
    kr = g.max((0.007 + 0.009 * kc_g) * g.smooth(0.015, 0.08, rr), 0.001)
    act = 1.0 - g.smooth(kden - 0.02, kden + 0.02, kc_r)
    core = act * (1.0 - g.smooth(kr * 0.8, kr, kd))
    swirl = act * (1.0 - g.smooth(kr * 0.5, kr * 3.2, kd))
    rim = act * (1.0 - g.smooth(0.0, 0.0016, g.abs(kd - kr))) * g.smooth(0.4, 0.6, kc_b)   # dead knots ring dark
    phase = rr / ring + g.noise(g.vec(x * 3.0, y * 60.0, z * 60.0), 1.3, detail=2.0) * 0.3 + swirl * 1.6
    f = g.fract(phase)
    lw = g.smooth(0.58, 0.86, f) * (1.0 - g.smooth(0.94, 0.995, f))
    strength = 0.5 + 0.5 * g.noise(g.vec(g.floor(phase) * 0.71, r0 * 13.0, 0.5), 2.0, detail=0.0)
    lw = lw * strength
    fibre = g.noise(g.vec(x * 3.0, y * 420.0, z * 420.0), 3.0, detail=2.0)
    streak = g.noise(g.vec(x * 0.5, y * 45.0, z * 45.0), 4.0, detail=3.0)
    tint = g.ramp(r0, [(0.0, (0.94, 0.92, 0.85)), (0.3, (0.98, 0.94, 0.88)), (0.6, (0.92, 0.87, 0.8)),
                       (1.0, (1.0, 0.94, 0.9))])
    base = g.mix(lin(222, 186, 134), lin(168, 106, 50), lw * 0.9)
    base = base * tint * (0.88 + streak * 0.24) * (0.93 + fibre * 0.14)
    kc = g.mix(lin(150, 88, 40), lin(104, 58, 28), g.sin(kd / kr * 13.0) * 0.5 + 0.5)
    base = g.mix(base, kc, core)
    base = g.mix(base, lin(60, 36, 20), rim * 0.7)
    base = base * (1.0 - c.pend * 0.3)
    # age: a little grey on what faces the sky, blotchy handling grime, dirt low down, occlusion grime, worn edges
    blot = g.noise(c.pos * 2.5, 5.0, detail=4.0)
    base = base * (1.0 - 0.16 * g.smooth(0.45, 0.8, blot))
    weather = g.sat(c.nz * 0.5 + 0.5) * 0.10 + 0.06
    base = g.mix(base, lin(160, 150, 132), weather)
    low = g.smooth(0.32, 0.0, c.pz) * (0.6 + 0.4 * g.noise(c.pos * 8.0, 6.0, detail=3.0))
    base = g.mix(base, lin(92, 78, 60), low * 0.45)
    base = base * (0.6 + 0.4 * c.ao)
    base = g.mix(base, lin(226, 198, 150), c.cvx * 0.3)
    dv, dcol = g.voronoi(c.pos, scale=60.0)
    dr, _, _ = g.sep(dcol)
    dent = (1.0 - g.smooth(0.0, 0.22, dv)) * (1.0 - g.smooth(0.10, 0.12, dr)) * (0.4 + 0.6 * c.edge)
    base = base * (1.0 - dent * 0.12)
    saw = g.sin((x * 80.0 + g.noise(g.vec(x * 2.0, y * 3.0, z * 3.0), 7.0) * 3.0) * TAU) * (1.0 - c.pend)
    rough = 0.70 - lw * 0.06 + fibre * 0.06 + c.pend * 0.12 + low * 0.1 - c.cvx * 0.1 + core * 0.04
    height = (lw * 0.00016 + fibre * 0.00005 + streak * 0.00005 + core * 0.0001 - rim * 0.00025 + saw * 0.00005
              - dent * 0.0004 + blot * 0.00005)
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def sh_iron(g, c):
    """Wrought nail heads: dark iron, hammered, rust in patches and where the air cannot reach."""
    p = c.pos
    n1 = g.noise(p * 60.0, 1.0 + c.rand * 5.0, detail=4.0)
    n2 = g.noise(p * 300.0, 2.0, detail=2.0)
    ham = g.noise(p * 140.0, 3.0, detail=1.0)
    rust = g.smooth(0.52, 0.68, n1 + (1.0 - c.ao) * 0.25)
    base = g.mix(lin(78, 77, 78), lin(108, 106, 102), n2)
    base = g.mix(base, lin(98, 60, 36), rust * 0.8)
    base = g.mix(base, lin(150, 148, 142), c.cvx * 0.5 * (1.0 - rust))
    base = base * (0.6 + 0.4 * c.ao)
    metal = g.mix(0.85, 0.1, rust)
    rough = g.mix(0.45, 0.85, rust) + n2 * 0.06
    height = n2 * 0.00004 + rust * 0.00008 + ham * 0.00008
    return {"base": base, "rough": rough, "metal": metal, "height": height}


SHADERS = {WOOD: sh_pine, IRON: sh_iron}


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
    g.put(e_orm.inputs["Color"], g.vec(g.pow(c.ao, 1.15), g.sat(res["rough"]), g.sat(res["metal"])))
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


def run_bake(kind_, device_used):
    scene = bpy.context.scene

    def go():
        if kind_ == "normal":
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y",
                                normal_b="POS_Z", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
        else:
            bpy.ops.object.bake(type="EMIT", margin=16, margin_type="EXTEND", use_clear=True,
                                target="IMAGE_TEXTURES", uv_layer="UVMap")
    for attempt in range(3):
        try:
            go()
            return
        except RuntimeError as err:
            if scene.cycles.device == "GPU":
                log(f"GPU bake failed ({str(err)[:120]}); retrying on the CPU")
                set_device(scene, "cpu")
                device_used.add("CPU")
                continue
            if attempt == 2:
                raise
            log(f"CPU bake failed ({str(err)[:120]}); retrying")
            time.sleep(5)


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


def bake_textures(obj, res, samples, device, work, lid_verts):
    scene = bpy.context.scene
    dev = set_device(scene, device)
    device_used = {scene.cycles.device if dev == "CPU" else dev}
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.seed = 0
    scene.render.bake.use_selected_to_active = False
    # a ground plane so the occlusion includes the ground under the crate; the lid lifted clear so the inside of the
    # crate is baked as an open box (both removed before export)
    bpy.ops.mesh.primitive_plane_add(size=8.0, location=(0, 0, 0))
    plane = bpy.context.active_object
    plane.name = "_ao_ground"
    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lifted = co.copy()
    lifted[lid_verts, 2] += PARAMS["bake_lid_lift_m"]
    me.vertices.foreach_set("co", lifted.ravel())
    me.update()
    image = bpy.data.images.new("bake_atlas", res, res, alpha=False, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    mats, shaders = [], []
    for k in (WOOD, IRON):
        mat, sh = bake_material(f"bake_{KINDS[k]}", k, image)
        mats.append(mat)
        shaders.append(sh)
    keep = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", keep)
    me.materials.clear()                 # clearing the slots resets every face's index, so restore it
    for mat in mats:
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", keep)
    me.update()
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
        log(f"baked {pass_} in {timings[pass_]} s")
    bpy.data.objects.remove(plane)
    me.vertices.foreach_set("co", co.ravel())
    me.update()
    col, orm, nrm = out["colour"], out["orm"], out["normal"]
    mask = (col.sum(axis=2) > 1e-6) | (orm.sum(axis=2) > 1e-6)   # texels the emit bakes wrote (islands + margin)
    for arr, fill in ((col, None), (orm, None), (nrm, (0.5, 0.5, 1.0))):
        arr[~mask] = fill if fill is not None else arr[mask].mean(axis=0)
    stem = os.path.join(work, ASSET_ID)
    paths = {"basecolor": stem + "_basecolor.png", "orm": stem + "_orm.png", "normal": stem + "_normal.png"}
    save_png(to_srgb(col), res, paths["basecolor"], "sRGB")
    save_png(orm, res, paths["orm"], "Non-Color")
    save_png(nrm, res, paths["normal"], "Non-Color")
    stats = {"coverage": round(float(mask.mean()), 4),
             "normal_mean_z_decoded": round(float((nrm[mask][:, 2] * 2 - 1).mean()), 4),
             "normal_min_z_decoded": round(float((nrm[mask][:, 2] * 2 - 1).min()), 4)}
    return paths, timings, sorted(device_used), stats


def gltf_output_group():
    ng = bpy.data.node_groups.get("glTF Material Output")
    if ng is None:
        ng = bpy.data.node_groups.new("glTF Material Output", "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.nodes.new("NodeGroupInput")
    return ng


def final_material(obj, paths):
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
    me = obj.data
    me.materials.clear()
    me.materials.append(mat)
    me.polygons.foreach_set("material_index", np.zeros(len(me.polygons), np.int32))
    me.update()


def flat_material(obj):
    """--no-bake: plain colours per shader kind, for fast geometry review."""
    me = obj.data
    idx = np.empty(len(me.polygons), dtype=np.int32)
    me.polygons.foreach_get("material_index", idx)
    me.materials.clear()
    for k, col in ((WOOD, (0.55, 0.40, 0.22)), (IRON, (0.05, 0.05, 0.05))):
        mat = bpy.data.materials.new(f"MAT_flat_{KINDS[k]}")
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = col + (1.0,)
        bsdf.inputs["Roughness"].default_value = 0.8
        bsdf.inputs["Metallic"].default_value = 0.7 if k == IRON else 0.0
        mat.use_backface_culling = True
        me.materials.append(mat)
    me.polygons.foreach_set("material_index", idx)
    me.update()


def separate_lid(obj, pivot):
    """Split the lid faces into their own object 'lid', origin on the hinge edge, parented to the body."""
    me = obj.data
    plid = face_array(me, "plid") > 0.5
    lt = np.empty(len(me.polygons), np.int64)
    me.polygons.foreach_get("loop_total", lt)
    lv = np.empty(len(me.loops), np.int64)
    me.loops.foreach_get("vertex_index", lv)
    vsel = np.zeros(len(me.vertices), bool)
    vsel[lv[np.repeat(plid, lt)]] = True
    ev = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ev)
    me.vertices.foreach_set("select", vsel)
    me.edges.foreach_set("select", vsel[ev.reshape(-1, 2)].all(axis=1))
    me.polygons.foreach_set("select", plid)
    me.update()
    select_only(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = [o for o in bpy.context.scene.objects if o is not obj and o.type == "MESH"]
    assert len(new) == 1, new
    lid = new[0]
    lid.name = "lid"
    lid.data.name = "lid"
    lid.data.transform(Matrix.Translation(-Vector(pivot)))
    lid.location = Vector(pivot)
    lid.parent = obj
    lid.matrix_parent_inverse = Matrix.Identity(4)
    assert (face_array(obj.data, "plid") < 0.5).all() and (face_array(lid.data, "plid") > 0.5).all()
    return lid


def add_socket(obj, dims_z):
    """SOCK_interact_take per _author_interaction_sockets.py (0.85 of the height, centred, +Y up / -Z secondary)."""
    s = bpy.data.objects.new("SOCK_interact_take", None)
    s.empty_display_type = "ARROWS"
    s.empty_display_size = 0.15
    s.location = (0.0, 0.0, 0.85 * dims_z)
    bpy.context.scene.collection.objects.link(s)
    s.parent = obj
    s.matrix_parent_inverse = Matrix.Identity(4)
    return s


# --------------------------------------------------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------------------------------------------------

def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out-dir", default=DEFAULT_OUT)
    ap.add_argument("--work-dir", default=os.path.join(tempfile.gettempdir(), ASSET_ID + "_procgen"))
    ap.add_argument("--seed", type=int, default=2609)
    ap.add_argument("--res", type=int, default=2048)
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--device", default="auto", choices=("auto", "cpu"))
    ap.add_argument("--no-bake", action="store_true")
    return ap.parse_args(argv)


def gltf_vec(v):
    """Blender Z-up to glTF Y-up (x, z, -y)."""
    return [round(float(v[0]), 5), round(float(v[2]), 5), round(float(-v[1]), 5)]


def main():
    args = parse_args()
    args.out_dir = os.path.abspath(args.out_dir)
    args.work_dir = os.path.abspath(args.work_dir)
    norm = os.path.normcase(args.out_dir)
    for forbidden in ("ready", "rigged", "animation"):
        if os.path.normcase(os.path.join(REPO, "assets", forbidden)) in norm:
            raise SystemExit(f"refusing to write into assets/{forbidden}")
    t_start = time.time()
    os.makedirs(args.out_dir, exist_ok=True)
    os.makedirs(args.work_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    rng = np.random.default_rng(args.seed)
    parts, nails, info = build_crate(rng)
    parts = parts + nails
    for i, p in enumerate(parts):
        p.wood = wood_params(np.random.default_rng([args.seed, i]), p)

    # centre on X/Y (glTF X/Z) with the lowest point at z = 0
    allw = np.concatenate([p.to_world(p.local) for p in parts])
    lo, hi = allw.min(axis=0), allw.max(axis=0)
    shift = np.array([-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2]])
    pivot = info["lid_pivot"] + shift
    t_pack = time.time()
    obj, verts, faces, face_part, pack = make_object(parts, shift, args.res)
    log(f"packed {pack['islands']} islands at {pack['px_per_m']} px/m in {time.time() - t_pack:.1f} s")
    topo = validate_parts(verts, faces, face_part, parts)
    log(f"TOPOLOGY {json.dumps(topo)}")
    lo, hi = verts.min(axis=0), verts.max(axis=0)
    dims = hi - lo
    lid_verts = np.unique(np.concatenate([np.array(f) for f, pi in zip(faces, face_part) if parts[pi].group == "lid"]))

    finish_topology(obj)
    tris = len(obj.data.polygons)
    uvs = uv_stats(obj, args.res, pack)
    a3, a2 = mesh_checks(obj.data)
    geo_checks = {
        "degenerate_triangles": int((a3 < 1e-10).sum()),
        "uv_zero_area_triangles": int((a2 < 1e-12).sum()),
        "uv_smallest_triangle_px2": round(float(a2.min() * args.res * args.res), 4),
    }
    log(f"{len(parts)} parts ({len(nails)} nails), {tris} tris, uv {json.dumps(uvs)}, {json.dumps(geo_checks)}")
    failed = (topo["open_edges"] or topo["misoriented_edges"] or topo["negative_volume_parts"] or topo["bowtie_quads"]
              or topo["degenerate_faces"] or geo_checks["degenerate_triangles"] or geo_checks["uv_zero_area_triangles"])
    if failed:
        raise SystemExit(f"validation failed: {topo} {geo_checks}")
    if tris > TRI_BUDGET:
        raise SystemExit(f"{tris} triangles over the hero budget {TRI_BUDGET}")

    textures, timings, devices, bake_stats = {}, {}, [], {}
    if args.no_bake:
        flat_material(obj)
    else:
        paths, timings, devices, bake_stats = bake_textures(obj, args.res, args.samples, args.device, args.work_dir,
                                                            lid_verts)
        final_material(obj, paths)
        textures = {k: os.path.basename(v) for k, v in paths.items()}
    lid = separate_lid(obj, pivot)
    for me in (obj.data, lid.data):
        for name in ("mpos", "prand", "plid", "pscale", "pend", "pith", "pdir", "pslope"):
            me.attributes.remove(me.attributes[name])
    sock = add_socket(obj, float(dims[2]))

    glb = os.path.join(args.out_dir, ASSET_ID + ".glb")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(args.work_dir, ASSET_ID + ".blend"))
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_materials="EXPORT", export_yup=True,
                              export_apply=True, export_normals=True, export_texcoords=True, export_tangents=False,
                              export_image_format="AUTO", use_selection=False, export_extras=False)

    concept = os.path.join(REPO, CONCEPT_REL)
    concept_sha = hashlib.sha256(open(concept, "rb").read()).hexdigest() if os.path.exists(concept) else None
    script_sha = hashlib.sha256(open(os.path.abspath(__file__), "rb").read()).hexdigest()
    glb_sha = hashlib.sha256(open(glb, "rb").read()).hexdigest()
    counts = {}
    for p in parts:
        counts[p.kind] = counts.get(p.kind, 0) + 1
    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_crate.py",
        "script_sha256": script_sha,
        "command": "blender --background --factory-startup --python tools/asset_pipeline/_procgen_crate.py -- "
                   f"--seed {args.seed} --res {args.res} --samples {args.samples}" + (" --no-bake" if args.no_bake else ""),
        "blender": bpy.app.version_string,
        "seed": args.seed,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "concept": CONCEPT_REL,
        "concept_sha256": concept_sha,
        "parameters": PARAMS,
        "design_notes": [
            "Replaces the image-to-3D reconstruction (crumpled and warped, top and base sloping 21 and 18 deg, "
            "lettering smeared in its texture).",
            "Every wall carries an X-brace inside a framed edge, per the brief ('an X-brace on each side panel'); the "
            "concept shows the brace on the short ends and keeps the long face plain for its lettering.",
            "The concept's 'Merchant' lettering is left out (no text on props; it was the smeared part of the "
            "reconstruction).",
            "The concept is an open crate; the lid asked for by the brief is built in the same idiom: plank top, framed "
            "edge, X-brace, two locating cleats underneath that sit inside the walls.",
            "Pine per the concept, lightly aged for the setting: greyed on top, occlusion grime, dirt near the ground, "
            "worn edges, dents.",
            "Nail heads stand ~3 mm proud of the faces they are driven into, so the overall size exceeds the "
            "1.20 x 0.95 x 0.95 m timber box by that much.",
        ],
        "members": counts,
        "frame": "glTF +Y up, front +Z (opposite the lid hinge), long side on X, centred on X/Z, lowest point y = 0",
        "nodes": {
            ASSET_ID: "the body (posts, walls, floor, skids and their nails)",
            "lid": {"parent": ASSET_ID, "origin_gltf": gltf_vec(pivot),
                    "note": "origin on the top back edge of the body (hinge line along X); rotate about local X "
                            "(negative angle in glTF opens it away from the front)"},
            "SOCK_interact_take": {"parent": ASSET_ID, "position_gltf": gltf_vec((0.0, 0.0, 0.85 * dims[2])),
                                   "primary": "+Y", "secondary": "-Z"},
        },
        "dimensions_m": {"x": round(float(dims[0]), 4), "y_height": round(float(dims[2]), 4),
                         "z": round(float(dims[1]), 4),
                         "timber_box": [PARAMS["length_m"], PARAMS["height_m"], PARAMS["depth_m"]],
                         "min_gltf": gltf_vec((lo[0], hi[1], lo[2])), "max_gltf": gltf_vec((hi[0], lo[1], hi[2]))},
        "layout": {"body_top_y_m": round(float(info["body_top_z"]), 4),
                   "brace_angles_deg": info["brace_angles_deg"],
                   "interior_clearances_m": info["interior_clearances_m"]},
        "world_structure": {
            "fitting_rule": "src/Presentation/Art/Fitting.cs: <= 0.5 m of blocker per side, top <= 0.6 m below it",
            "note": "no structure in src/ or content/ binds prop_wooden_crate (art_bindings.json lists it only "
                    "under its reconstruction's tilt), so there is no blocker to fit against yet; any blocker "
                    f"between {dims[0]:.2f}-{dims[0] + 1.0:.2f} x {dims[1]:.2f}-{dims[1] + 1.0:.2f} m and up to "
                    f"{dims[2] + 0.6:.2f} m tall would accept it"},
        "triangles": tris,
        "triangle_budget": TRI_BUDGET,
        "checks": {"topology": topo, **geo_checks,
                   "gates": "0 open/misoriented edges, 0 negative-volume parts, 0 bow-tie quads, 0 degenerate faces "
                            "or triangles, 0 zero-area UV triangles, triangles <= budget (the script stops otherwise)"},
        "materials": {
            "source": "own procedural PBR shaders (3D pine with rings, knots and saw marks; wrought-iron nail heads), "
                      "baked with Cycles; the staging world materials were not used",
            "material": f"MAT_{ASSET_ID}", "single_sided": True, "atlas": args.res,
            "uv": uvs,
            "texel_scale_notes": "px_per_m is for visible faces; never-seen faces (butt ends, faces against other "
                                 "members, undersides on the ground) at 0.1, board edges in 2-3 mm gaps at 0.15, nail "
                                 "heads at 2.0 (a 13 mm head is ~13 px)",
            "maps": "base colour (sRGB), normal (tangent, OpenGL +Y), ORM (R occlusion, G roughness, B metallic)",
            "textures": textures,
            "bake": ({"samples": args.samples, "devices": devices, "seconds": timings,
                      "ao_distance_m": PARAMS["bake_ao_distance_m"], "lid_lifted_m": PARAMS["bake_lid_lift_m"],
                      **bake_stats} if not args.no_bake else None),
        },
        "outputs": [ASSET_ID + ".glb", ASSET_ID + "_provenance.json"],
        "glb_sha256": glb_sha,
        "build_seconds": round(time.time() - t_start, 1),
    }
    prov_path = os.path.join(args.out_dir, ASSET_ID + "_provenance.json")
    with open(prov_path, "w", encoding="utf-8") as fh:
        json.dump(prov, fh, indent=2)
    log(f"wrote {glb} ({tris} tris, dims {prov['dimensions_m']})")
    print(f"PROCGEN_RESULT {prov_path}", flush=True)


if __name__ == "__main__":
    main()
