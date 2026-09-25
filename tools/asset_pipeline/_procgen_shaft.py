"""Procedural build of prop_blocked_shaft: the blocked mine entrance at Blackvein Cut (hero landmark).

Image-to-3D could not make this prop: it melted the thin timber members and the open portal into one
crumpled sheet (145.6 m2 of surface inside a 30.8 m2 envelope, 2.6 m instead of the world's 6 x 5 x 4 m).
This builds it from real dimensions instead, as a hybrid:

  * TIMBER, IRON, ROPE, SLABS AND SPOIL are generated here. Every member is a closed solid built in
    its own frame (a swept log, an extruded board or slab), UV-unwrapped in that frame (U around the
    member, V along it, so grain runs along every member), packed at one texel density into a single
    atlas, and textured by baking procedural shaders to base colour / normal / ORM. The shaders read
    member-local coordinates, so grain, checks, knots and end-grain rings follow each member whatever
    its angle. Ambient occlusion is baked against the whole scene, rocks included.
  * ROCK is procedural too: every stone is its own fractured block (a sphere pressed onto a seeded set of
    fracture planes, arrises worn round, surface pitted with noise, collapse-decimated to a budget), placed
    as a pile at authored positions and sizes and cut flat at the ground. The pile is unwrapped at one
    texel density into its own atlas and baked from a procedural stone shader (grey granite, cracks,
    flecks, iron stain, lichen, ground dust, the shaft mouth in shadow) with AO against the whole scene.

World fit: the blocker rock_blocked_shaft is 6.0 x 5.0 m and 4.0 m tall (content/regions/ashen_hollow.yaml).
Fitting.cs rejects a model that leaves more than 0.5 m of blocker per side or stops more than 0.6 m below
its top, so the pile fills 6.3 x 5.6 m and rises to 4.0 m. The opening faces +Z, 2.0 m wide between the
posts and 2.4 m to the underside of the cap log.

Frames: authored in glTF terms (x right, y up, z front); Blender's Z-up frame is only a transport detail
(G() converts). Export is GLB, +Y up, front +Z, centred on X/Z, lowest point y = 0.

Usage (Blender 5.2, headless):
  blender --background --factory-startup --python tools/asset_pipeline/_procgen_shaft.py -- ^
      [--out assets/_staging/procedural/prop_blocked_shaft] [--seed 20260924] [--atlas 2048] [--preview]

--preview skips UV packing and baking and uses flat colours (layout iteration only). Writes
<out>/prop_blocked_shaft.glb and <out>/prop_blocked_shaft_provenance.json. Last stdout line:
"PROCGEN_RESULT <provenance path>".
"""
import argparse
import datetime
import hashlib
import json
import math
import os
import random
import struct
import sys
import tempfile
import time

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector, noise
from mathutils.bvhtree import BVHTree
from mathutils.geometry import delaunay_2d_cdt

ASSET_ID = "prop_blocked_shaft"
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TOOL_DIR))
ASSETS = os.path.join(REPO, "assets")
CONCEPT = os.path.join(ASSETS, "concepts", f"{ASSET_ID}.png")
DEFAULT_OUT = os.path.join(ASSETS, "_staging", "procedural", ASSET_ID)
BLOCKER = {"id": "rock_blocked_shaft", "width_x_m": 6.0, "depth_z_m": 5.0, "height_m": 4.0,
           "source": "content/regions/ashen_hollow.yaml (x73-79, z16-21)"}
FIT_SIDE_MARGIN = 0.5      # Fitting.cs SideMargin
FIT_HEIGHT_SHORTFALL = 0.6  # Fitting.cs HeightShortfall

# Texel density of each surface kind relative to the wood (the atlas density is solved for, see pack_atlas).
KIND_DENSITY = {"wood": 1.0, "iron": 1.0, "rope": 1.0, "dirt": 0.45, "hidden": 0.06}  # hidden: faces on the ground
MATERIAL_KINDS = ("wood", "iron", "rope", "dirt")
PREVIEW_COLOURS = {"wood": (0.30, 0.26, 0.21), "iron": (0.05, 0.045, 0.04), "rope": (0.16, 0.12, 0.07),
                   "dirt": (0.22, 0.18, 0.14)}
# Stone generator (unit radius, before placement scales it): fracture planes at these offsets, a sphere cap
# where no plane bounds it, Laplacian wear of the arrises, then low (lumps) and mid (pits) noise along the normal.
ROCK_GEN = {"planes": (11, 16), "pebble_planes": (6, 8), "plane_offset": (0.55, 0.92), "cap": 1.15,
            "wear_iters": 1, "wear": 0.4, "lump": 0.05, "lump_freq": 1.3, "pit": 0.018, "pit_freq": 4.0,
            "skin": 0.03, "skin_freq": 3.0}  # skin: metres of relief put back after the flat clamps, per metre

# ----------------------------------------------------------------------------------------- layout
# Posts: squared (hewn, with wane at the corners) set 2.0 m apart, leaning back into the face.
POST = {"half_side": 0.125, "log_radius": 0.155, "base_x": 1.17, "base_z": 2.32, "length": 3.55,
        "lean_back_deg": (11.5, 12.5), "lean_side_deg": (-1.5, -1.0)}  # (left, right); side + = toward +x
CAP = {"radius": 0.155, "x0": -2.60, "x1": 2.50, "underside_y": 2.40, "sag_right_m": 0.06, "notch_m": 0.035}
LINTEL = {"radius": 0.11, "x0": -1.38, "x1": 1.38, "y": (2.20, 2.15), "z": (1.22, 1.16)}
STUBS = [  # tie logs running back into the rock under the cap ends (the concept's short log at the joint)
    {"name": "stub_left", "radius": 0.10, "x": -1.47, "y": (2.31, 2.33), "z": (1.10, 2.42)},  # bears on the cap
    {"name": "stub_right", "radius": 0.095, "x": 1.46, "y": (2.29, 2.27), "z": (1.10, 2.30)},
]
PROPS = [  # the two crossed pit props jammed across the mouth
    {"name": "prop_long", "radius": 0.115, "top": (-0.92, 2.14, 1.42), "foot": (1.72, 2.72)},
    {"name": "prop_short", "radius": 0.105, "top": (0.92, 1.96, 1.34), "foot": (-0.98, 2.30)},
]
PEGS = {"count": 8, "height": (0.45, 2.25), "length": (0.05, 0.12), "iron_share": 0.35, "on_cap": 3}
ROPE = {"prop": "prop_long", "at_from_foot_m": (0.42, 0.36, 0.30), "radius": 0.010}
PLANK = {"width": 0.24, "thickness": 0.035, "chamfer": 0.004}
BOARDS = [  # nailed across the post fronts: (name, centre height y, left end x, right end x, jag at (l, r))
    {"name": "board_high", "y": 1.74, "x0": -1.45, "x1": 1.40, "jag": (False, True), "width": 0.27,
     "hang_deg": -4.0},
    {"name": "board_low_right", "y": 1.06, "x0": 0.25, "x1": 1.42, "jag": (True, False), "width": 0.23,
     "hang_deg": 31.0},
]
LOOSE_BOARDS = [  # broken boards in the rubble: centre (x, y, z), length, width, yaw, pitch, roll (deg)
    {"name": "loose_board_a", "c": (-0.42, 0.72, 1.80), "len": 1.15, "w": 0.21, "rot": (18, -38, 12)},
    {"name": "loose_board_b", "c": (0.40, 1.30, 1.45), "len": 0.85, "w": 0.19, "rot": (-25, 24, -8)},
    {"name": "loose_board_c", "c": (-0.95, 0.02, 2.72), "len": 0.95, "w": 0.22, "rot": (62, 3, 4)},
]
SLABS = [  # broken stone slabs, cut from the rock like the rest: centre (x, z), size (len, width, thick), yaw, lean
    {"name": "S1", "c": (0.52, 2.08), "size": (1.00, 0.72, 0.11), "yaw": -8, "lean": 62},
    {"name": "S2", "c": (-0.15, 2.72), "size": (0.85, 0.52, 0.09), "yaw": 14, "lean": 5},
    {"name": "S3", "c": (1.05, 2.86), "size": (0.55, 0.36, 0.075), "yaw": -35, "lean": 11},
    {"name": "S4", "c": (-0.62, 2.28), "size": (0.70, 0.45, 0.08), "yaw": 30, "lean": 38},
]
SPILL = {"cx": 0.0, "cz": 2.25, "ax": 2.15, "az_front": 0.80, "az_back": 0.90, "hmax": 0.17, "edge": 0.008}

# Rock pile. size = (x, up, front) metres of the placed rock, pos = (x, bottom y, z); a bottom below 0
# sinks the rock and it is cut flat at the ground. variant picks the decimation budget. flat squares a
# rock off into a quarried block: "u.86" clamps everything above 86% of its height onto a plane (u/d up
# and down, f/b front and back, l/r left and right). variant = the stone's triangle budget.
ROCK_VARIANTS = {"big": 1200, "core": 520, "med": 800, "small": 500, "pebble": 44}
ROCKS = [
    # left face: two quarried blocks stacked at the front, two behind
    ("L1", "big", (2.05, 1.70, 1.95), (-2.12, -0.20, 1.12), "u.86 f.84 l.88 d.97"),
    ("L2", "big", (1.95, 1.60, 1.85), (-2.08, 1.00, 0.82), "u.84 f.82 d.86 l.9"),  # flat bottom lands on L1
    ("L3", "big", (2.10, 2.10, 2.05), (-2.05, -0.30, -0.95), "l.88 b.88 u.9"),
    ("L4", "big", (2.15, 1.65, 2.00), (-1.80, 1.50, -0.85), "u.84 l.88 d.9"),
    # right face
    ("R1", "big", (2.00, 1.65, 1.95), (2.14, -0.20, 1.15), "u.86 f.84 r.88 d.97"),
    ("R2", "big", (1.90, 1.55, 1.85), (2.06, 0.98, 0.82), "u.84 f.82 d.86 r.9"),
    ("R3", "big", (2.10, 2.15, 2.05), (2.05, -0.30, -0.95), "r.88 b.88 u.9"),
    ("R4", "big", (2.10, 1.65, 2.00), (1.80, 1.52, -0.88), "u.84 r.88 d.9"),
    # centre: core behind the mouth, the block over the portal, the crown behind
    ("C1", "core", (2.60, 2.80, 2.30), (0.00, -0.30, -1.02), ""),  # set back: the shaft void behind the rubble
    ("C2", "big", (2.60, 1.20, 1.60), (0.00, 2.28, 0.50), "f.84 u.86 d.9"),
    ("C3", "big", (2.90, 1.35, 2.20), (0.05, 2.32, -0.72), "u.86 b.9"),
    ("B1", "big", (2.20, 2.20, 1.50), (-0.90, -0.30, -1.32), "b.9"),
    ("B2", "big", (2.20, 2.10, 1.45), (1.00, -0.30, -1.36), "b.9"),
    # the row of stones lying on the cap log
    ("T1", "med", (0.95, 0.60, 0.85), (-1.05, 2.56, 1.50), "d.8 f.8 u.84 l.85"),
    ("T2", "med", (1.00, 0.55, 0.85), (-0.15, 2.60, 1.46), "d.8 u.82 f.84 r.86"),
    ("T3", "med", (0.90, 0.62, 0.80), (0.70, 2.55, 1.46), "d.8 f.8 u.84 l.86"),
    ("T4", "med", (0.85, 0.66, 0.85), (1.50, 2.48, 1.40), "d.8 u.84 r.85"),
    # rubble choking the mouth: high at the back, low across the threshold, pulled back off the entrance
    # plane so the shaft behind (C1) reads as a real recess with depth, not a painted-dark backdrop
    ("M1", "med", (0.80, 0.55, 0.80), (-0.52, 0.50, 0.85), "u.82 f.85"),
    ("M2", "med", (0.90, 0.80, 0.80), (0.38, 0.80, 0.70), "f.8"),
    ("M3", "med", (0.75, 0.80, 0.75), (0.62, 0.50, 1.10), ""),
    ("M4", "med", (0.80, 0.80, 0.80), (-0.70, 0.20, 1.20), "f.82"),
    ("M5", "med", (0.90, 0.75, 0.80), (0.05, 0.35, 1.15), "u.8"),
    ("M6", "small", (0.70, 0.45, 0.70), (-0.30, -0.15, 1.45), "u.8"),
    ("M7", "small", (0.75, 0.50, 0.70), (0.55, -0.15, 1.40), "r.8 u.85"),
    ("M8", "small", (0.55, 0.38, 0.55), (-0.85, -0.15, 1.60), ""),
    ("M9", "small", (0.50, 0.34, 0.50), (0.95, -0.15, 1.65), "u.8"),
    ("M10", "small", (0.45, 0.26, 0.45), (0.20, -0.10, 1.80), ""),
    # fallen stones in front of the face
    ("F1", "med", (1.00, 0.72, 0.85), (-1.80, -0.15, 2.32), "u.82 f.85"),
    ("F2", "small", (0.75, 0.42, 0.60), (-0.35, -0.10, 2.92), "u.8"),
    ("F3", "med", (1.05, 0.78, 0.90), (2.30, -0.20, 2.20), "u.82 l.85"),
    ("F4", "small", (0.40, 0.30, 0.40), (1.60, -0.10, 2.78), ""),
    ("F5", "small", (0.55, 0.42, 0.50), (-2.45, -0.10, 2.20), "u.85"),
    ("F6", "small", (0.30, 0.22, 0.30), (0.80, -0.05, 3.02), ""),
    ("F7", "small", (0.60, 0.52, 0.55), (2.80, -0.10, 1.95), "u.85"),
]
FLAT_DIRS = {"u": (0, 1, 0), "d": (0, -1, 0), "f": (0, 0, 1), "b": (0, 0, -1), "l": (-1, 0, 0), "r": (1, 0, 0)}
PEBBLES = {"count": 24, "x": (-2.0, 2.2), "z": (2.05, 3.10), "size": (0.05, 0.14)}


def G(x, up, front):
    """glTF frame (x right, y up, z front) -> Blender frame (x right, -y front, z up)."""
    return Vector((x, -front, up))


# ------------------------------------------------------------------------------------------ args
def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", default=DEFAULT_OUT)
    p.add_argument("--seed", type=int, default=20260924)
    p.add_argument("--atlas", type=int, default=2048)
    p.add_argument("--target-density", type=float, default=512.0, help="px/m ceiling for wood")
    p.add_argument("--pad", type=int, default=6, help="px of padding around each UV island")
    p.add_argument("--bake-samples", type=int, default=16)
    p.add_argument("--ao-samples", type=int, default=160)
    p.add_argument("--ao-distance", type=float, default=0.45)
    p.add_argument("--rock-ao-distance", type=float, default=0.9, help="AO reach for the stone atlas (m)")
    p.add_argument("--preview", action="store_true")
    p.add_argument("--save-blend", default="")
    return p.parse_args(argv)


# ------------------------------------------------------------------------------ member builders
ISLANDS = []  # global UV island registry: {"id", "kind", "member"}


class Member:
    """One closed solid of the timber atlas under construction (a bmesh with UV, island and shader layers)."""

    def __init__(self, name, kind, mid):
        self.name, self.kind, self.mid = name, kind, mid
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.isl = self.bm.faces.layers.int.new("island")
        self.endc = self.bm.faces.layers.float.new("endcap")
        self.mloc = self.bm.verts.layers.float_vector.new("mloc")
        self.midl = self.bm.verts.layers.float.new("mid")

    def vert(self, co, mloc):
        v = self.bm.verts.new(co)
        v[self.mloc] = mloc
        v[self.midl] = self.mid
        return v

    def face(self, verts, uvs, island, endcap=0.0):
        f = self.bm.faces.new(verts)
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        f[self.isl] = island
        f[self.endc] = endcap
        return f

    def island(self, kind=None):
        ISLANDS.append({"id": len(ISLANDS), "kind": kind or self.kind, "member": self.name})
        return len(ISLANDS) - 1

    def finish(self, materials):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(obj)
        me.materials.append(materials[self.kind])
        obj["kind"] = self.kind
        return obj


def rmf_frames(centres, seam_hint):
    """Rotation-minimising frames along a polyline; N starts at seam_hint projected off the tangent."""
    n = len(centres)
    tangents = []
    for i in range(n):
        if i == 0:
            t = centres[1] - centres[0]
        elif i == n - 1:
            t = centres[-1] - centres[-2]
        else:
            t = (centres[i + 1] - centres[i]).normalized() + (centres[i] - centres[i - 1]).normalized()
        tangents.append(t.normalized())
    nv = seam_hint - tangents[0] * seam_hint.dot(tangents[0])
    if nv.length < 1e-6:
        nv = tangents[0].orthogonal()
    normals = [nv.normalized()]
    for i in range(1, n):
        q = tangents[i - 1].rotation_difference(tangents[i])
        v = q @ normals[-1]
        normals.append((v - tangents[i] * v.dot(tangents[i])).normalized())
    binormals = [tangents[i].cross(normals[i]) for i in range(n)]
    return tangents, normals, binormals


def profile_point(theta, shape, q):
    c, s = math.cos(theta), math.sin(theta)
    if shape == "square":  # hewn square of half-side q (as a fraction of the log radius), wane at the corners
        rr = min(1.0, q / max(abs(c), abs(s)))
        return c * rr, s * rr
    return c, s


def log_member(name, kind, p0, p1, r0, r1, sides, materials, mid, *, shape="round", square_q=0.8,
               bow=0.0, bow_dir=None, seam_hint=None, chamfer=0.012, cap0="chamfer", cap1="chamfer",
               seg_len=0.32, wobble=0.035, split_len=1.55, taper_noise=0.035):
    """A swept log (or hewn post) from p0 to p1: closed, UV'd around x along, end grain on the caps.

    cap: 'chamfer' (bevelled end), 'flat' (square end), 'ground' (cut horizontally at z = 0 so it
    stands on the ground; p0 must then be the lower end and on the ground).
    """
    m = Member(name, kind, mid)
    axis = p1 - p0
    length = axis.length
    axis.normalize()
    seam_hint = seam_hint or Vector((0.0, 1.0, 0.0))
    ch = min(chamfer, 0.3 * min(r0, r1))
    stations = []  # (s, radius scale)
    if cap0 == "chamfer":
        stations += [(0.0, (r0 - ch) / r0), (ch, 1.0)]
    else:
        stations += [(0.0, 1.0)]
    n_int = max(1, int(round(length / seg_len)))
    for i in range(1, n_int):
        s = length * i / n_int
        if stations[-1][0] + 0.05 < s < length - ch - 0.05:
            stations.append((s, 1.0))
    if cap1 == "chamfer":
        stations += [(length - ch, 1.0), (length, (r1 - ch) / r1)]
    else:
        stations += [(length, 1.0)]

    seed_off = mid * 97.0
    centres, radii = [], []
    for s, k in stations:
        t = s / length
        c = p0 + axis * s
        if bow and bow_dir is not None:
            c = c + bow_dir * (bow * math.sin(math.pi * t))
        # three scalar lookups: noise.noise_vector is not repeatable between Blender sessions, noise.noise is
        w = Vector([noise.noise(Vector((s * 0.9, seed_off, k))) for k in (1.7, 11.3, 23.9)])
        w -= axis * w.dot(axis)
        c = c + w * 0.006
        r = (r0 + (r1 - r0) * t) * (1.0 + taper_noise * noise.noise(Vector((s * 1.1, seed_off, 3.3))))
        centres.append(c)
        radii.append(r * k)
    tangents, normals, binormals = rmf_frames(centres, seam_hint)

    rings, ring_uv_u, ring_v = [], [], []
    for i, (s, _k) in enumerate(stations):
        c, T, N, B, r = centres[i], tangents[i], normals[i], binormals[i], radii[i]
        ring, pos_list, along_list = [], [], []
        for j in range(sides):
            th = 2.0 * math.pi * j / sides
            a, b = profile_point(th, shape, square_q)
            jit = 1.0 + wobble * noise.noise(Vector((s * 3.1, th * 1.3, seed_off + 5.0)))
            a, b = a * r * jit, b * r * jit
            pos = c + N * a + B * b
            along = s
            if (i == 0 and cap0 == "ground") or (i == len(stations) - 1 and cap1 == "ground"):
                if abs(T.z) > 1e-3:
                    tt = -pos.z / T.z
                    pos = pos + T * tt
                    along = s + tt
            pos_list.append(pos)
            along_list.append(along)
            ring.append(m.vert(pos, (along, a, b)))
        # arc length around the ring (seam at j = 0, which faces seam_hint)
        arc = [0.0]
        for j in range(1, sides + 1):
            arc.append(arc[-1] + (pos_list[j % sides] - pos_list[j - 1]).length)
        rings.append(ring)
        ring_uv_u.append(arc)
        ring_v.append(along_list)

    n_chunks = max(1, int(math.ceil(length / split_len)))
    chunk_islands = [m.island() for _ in range(n_chunks)]
    for i in range(len(rings) - 1):
        s_mid = 0.5 * (stations[i][0] + stations[i + 1][0])
        isl = chunk_islands[min(n_chunks - 1, int(s_mid / (length / n_chunks)))]
        for j in range(sides):
            j1 = (j + 1) % sides
            verts = [rings[i][j], rings[i][j1], rings[i + 1][j1], rings[i + 1][j]]
            uvs = [(ring_uv_u[i][j], ring_v[i][j]), (ring_uv_u[i][j + 1], ring_v[i][j1]),
                   (ring_uv_u[i + 1][j + 1], ring_v[i + 1][j1]), (ring_uv_u[i + 1][j], ring_v[i + 1][j])]
            m.face(verts, uvs, isl)
    # caps: planar in the ring frame, end grain
    for idx, rev in ((0, True), (len(rings) - 1, False)):
        ring = rings[idx]
        order = list(reversed(ring)) if rev else list(ring)
        uvs = [(v[m.mloc][1], v[m.mloc][2]) for v in order]
        on_ground = (cap0 if rev else cap1) == "ground"
        f = m.face(order, uvs, m.island("hidden" if on_ground else None), endcap=1.0)
        bmesh.ops.triangulate(m.bm, faces=[f], quad_method="BEAUTY", ngon_method="BEAUTY")
    obj = m.finish(materials)
    obj["length_m"] = length
    return obj


def ring_member(name, kind, centre, axis, loop_radius, tube_radius, materials, mid, seg=14, sides=6, tilt=None):
    """A closed torus (a rope wrap around a log)."""
    m = Member(name, kind, mid)
    axis = axis.normalized()
    u0 = axis.orthogonal().normalized()
    v0 = axis.cross(u0)
    rings, arcs = [], []
    circumference = 2 * math.pi * loop_radius
    for k in range(seg):
        ph = 2 * math.pi * k / seg
        radial = u0 * math.cos(ph) + v0 * math.sin(ph)
        tangent = axis.cross(radial).normalized()
        c = centre + radial * loop_radius + axis * (tilt * math.sin(ph) if tilt else 0.0)
        N, B = radial, tangent.cross(radial)
        ring, pos = [], []
        for j in range(sides):
            th = 2 * math.pi * j / sides
            p = c + N * (math.cos(th) * tube_radius) + B * (math.sin(th) * tube_radius)
            pos.append(p)
            ring.append(m.vert(p, (circumference * k / seg, math.cos(th) * tube_radius, math.sin(th) * tube_radius)))
        arc = [0.0]
        for j in range(1, sides + 1):
            arc.append(arc[-1] + (pos[j % sides] - pos[j - 1]).length)
        rings.append(ring)
        arcs.append(arc)
    isl = m.island()
    for k in range(seg):
        k1 = (k + 1) % seg
        v_a = circumference * k / seg
        v_b = circumference * (k + 1) / seg
        for j in range(sides):
            j1 = (j + 1) % sides
            m.face([rings[k][j], rings[k][j1], rings[k1][j1], rings[k1][j]],
                   [(arcs[k][j], v_a), (arcs[k][j + 1], v_a), (arcs[k1][j + 1], v_b), (arcs[k1][j], v_b)], isl)
    return m.finish(materials)


def resample_outline(outline, max_edge):
    out = []
    n = len(outline)
    for i in range(n):
        a, b = Vector(outline[i]), Vector(outline[(i + 1) % n])
        steps = max(1, int(math.ceil((b - a).length / max_edge)))
        for k in range(steps):
            out.append(a.lerp(b, k / steps))
    return out


def inset_outline(pts, d):
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2 = pts[i - 1], pts[i], pts[(i + 1) % n]
        e1, e2 = (p1 - p0).normalized(), (p2 - p1).normalized()
        n1, n2 = Vector((-e1.y, e1.x)), Vector((-e2.y, e2.x))  # inward (left) normals of a CCW outline
        mdir = n1 + n2
        if mdir.length < 1e-6:
            mdir = n1
        mdir.normalize()
        off = d / max(0.35, mdir.dot(n1))
        off = min(off, 0.45 * min((p1 - p0).length, (p2 - p1).length))
        out.append(p1 + mdir * off)
    return out


def ear_clip(pts, eps=1e-10):
    """Triangulate a simple CCW 2D polygon; flat (collinear) corners are never clipped as ears, so every
    outline vertex ends up in a real triangle. Returns CCW index triples."""
    idx = list(range(len(pts)))
    tris = []

    def cross(a, b, c):
        return (b - a).cross(c - a)

    def inside(p, a, b, c):
        return cross(a, b, p) > eps and cross(b, c, p) > eps and cross(c, a, p) > eps

    while len(idx) > 3:
        n = len(idx)
        best = None
        for t in range(n):
            a, b, c = idx[t - 1], idx[t], idx[(t + 1) % n]
            area = cross(pts[a], pts[b], pts[c])
            if area <= eps:
                continue
            if any(inside(pts[q], pts[a], pts[b], pts[c]) for q in idx if q not in (a, b, c)):
                continue
            quality = area / ((pts[b] - pts[a]).length + (pts[c] - pts[b]).length + (pts[a] - pts[c]).length) ** 2
            if best is None or quality > best[0]:  # the fattest ear, so no slivers where the outline is resampled
                best = (quality, t)
        best = None if best is None else best[1]
        if best is None:  # numerically stuck: clip the largest convex corner
            best = max(range(n), key=lambda t: cross(pts[idx[t - 1]], pts[idx[t]], pts[idx[(t + 1) % n]]))
        tris.append((idx[best - 1], idx[best], idx[(best + 1) % n]))
        idx.pop(best)
    tris.append(tuple(idx))
    return tris


def cap_triangles(pts):
    """Constrained Delaunay triangulation of a simple CCW outline (well-shaped triangles even along the
    collinear resample points); ear clipping if the CDT had to add a vertex. CCW index triples."""
    n = len(pts)
    verts, _, faces, orig, _, _ = delaunay_2d_cdt([Vector((p.x, p.y)) for p in pts],
                                                   [(i, (i + 1) % n) for i in range(n)], [list(range(n))], 1, 1e-9)
    if len(verts) != n or any(len(o) != 1 for o in orig):
        return ear_clip(pts)
    tris = []
    for f in faces:
        i, j, k = (orig[v][0] for v in f)
        if (pts[j] - pts[i]).cross(pts[k] - pts[i]) < 0:
            j, k = k, j
        tris.append((i, j, k))
    return tris


def slab_member(name, kind, outline, thick, ch, origin, A, B, materials, mid, *, max_edge=0.35,
                split_len=1.5, top_jitter=0.0, end_grain_axis=True):
    """An extruded outline (board or stone slab): closed, chamfered, grain along A."""
    m = Member(name, kind, mid)
    A, B = A.normalized(), B.normalized()
    C = A.cross(B).normalized()
    outer = resample_outline([Vector(p) for p in outline], max_edge)
    area2 = sum(outer[i - 1].x * outer[i].y - outer[i].x * outer[i - 1].y for i in range(len(outer)))
    if area2 < 0:
        outer.reverse()
    inner = inset_outline(outer, ch)
    n = len(outer)
    levels = [(inner, -thick / 2), (outer, -thick / 2 + ch), (outer, thick / 2 - ch), (inner, thick / 2)]
    rings = []
    for li, (pts, c) in enumerate(levels):
        ring = []
        for j, p in enumerate(pts):
            cc = c
            if top_jitter and li >= 2:
                cc += top_jitter * noise.noise(Vector((p.x * 3.0, p.y * 3.0, mid * 50)))
            co = origin + A * p.x + B * p.y + C * cc
            ring.append(m.vert(co, (p.x, p.y, cc)))
        rings.append(ring)
    # side strip: u along the outline, v up the edge (true lengths, chamfers included)
    per = [0.0]
    for j in range(1, n + 1):
        per.append(per[-1] + (outer[j % n] - outer[j - 1]).length)
    v_lv = [0.0, ch * 1.4142, ch * 1.4142 + (thick - 2 * ch), 2 * ch * 1.4142 + (thick - 2 * ch)]
    n_chunks = max(1, int(math.ceil(per[-1] / split_len)))
    chunk_isl = [m.island() for _ in range(n_chunks)]
    for j in range(n):
        j1 = (j + 1) % n
        u_mid = 0.5 * (per[j] + per[j + 1])
        isl = chunk_isl[min(n_chunks - 1, int(u_mid / (per[-1] / n_chunks)))]
        e = (outer[j1] - outer[j]).normalized()
        endc = 1.0 if (end_grain_axis and abs(e.x) < 0.55) else 0.0
        for k in range(3):
            verts = [rings[k][j], rings[k][j1], rings[k + 1][j1], rings[k + 1][j]]
            uvs = [(per[j], v_lv[k]), (per[j + 1], v_lv[k]), (per[j + 1], v_lv[k + 1]), (per[j], v_lv[k + 1])]
            m.face(verts, uvs, isl, endcap=endc)
    # caps: ear-clipped here in the outline plane (Blender's ngon triangulation bridged a splinter notch
    # with a flipped triangle on some broken ends); CCW triangles for the top, reversed for the bottom
    top_isl, bot_isl = m.island(), m.island()
    for i, j, k in cap_triangles(inner):
        m.face([rings[3][i], rings[3][j], rings[3][k]], [(inner[q].x, inner[q].y) for q in (i, j, k)], top_isl)
        m.face([rings[0][i], rings[0][k], rings[0][j]], [(-inner[q].x, inner[q].y) for q in (i, k, j)], bot_isl)
    return m.finish(materials)


def board_outline(length, width, rng, jag_left, jag_right):
    """Board outline in (along, across), CCW, with splintered ends where broken."""
    pts = [(0.0, -width / 2)]

    def jag_end(x_base, sign, jag):
        seq = []
        cols = 5
        for k in range(1, cols):
            b = -width / 2 + width * k / cols
            if jag:
                d = rng.uniform(-0.11, 0.035) if k % 2 else rng.uniform(-0.03, 0.07)
            else:
                d = 0.0
            seq.append((x_base + sign * d, b))
        return seq

    pts.append((length, -width / 2))
    pts += jag_end(length, 1.0, jag_right)
    pts.append((length, width / 2))
    pts.append((0.0, width / 2))
    pts += list(reversed(jag_end(0.0, -1.0, jag_left)))
    if jag_left:
        pts[0] = (pts[0][0] - rng.uniform(0.0, 0.04), pts[0][1])
    return pts


# ------------------------------------------------------------------------------- the dirt spill
def spill_height(x, z, rng_off):
    """Height (m) of the spoil spill at glTF (x, z); 0 outside."""
    dx = (x - SPILL["cx"]) / SPILL["ax"]
    dzr = z - SPILL["cz"]
    dz = dzr / (SPILL["az_front"] if dzr >= 0 else SPILL["az_back"])
    th = math.atan2(dz, dx)
    edge = 1.0 + 0.16 * noise.noise(Vector((math.cos(th) * 2.0, math.sin(th) * 2.0, rng_off)))
    f = math.sqrt(dx * dx + dz * dz) / edge
    if f >= 1.0:
        return 0.0
    back_bias = 1.0 + 0.6 * max(0.0, -dz)  # piled higher toward the mouth
    h = SPILL["hmax"] * (1 - f * f) ** 1.4 * back_bias * (1.0 + 0.25 * noise.noise(Vector((x * 1.7, z * 1.7, rng_off + 3))))
    return max(SPILL["edge"], h + SPILL["edge"])


def spill_member(materials, mid, rng_off):
    m = Member("spill", "dirt", mid)
    ring_f = [0.0, 0.14, 0.3, 0.46, 0.6, 0.72, 0.83, 0.92, 1.0]
    seg = 40
    grid = []
    for fi, f in enumerate(ring_f):
        row = []
        for k in range(seg if fi else 1):
            th = 2 * math.pi * k / seg
            edge = 1.0 + 0.16 * noise.noise(Vector((math.cos(th) * 2.0, math.sin(th) * 2.0, rng_off)))
            ff = f * edge * 0.999
            dx, dz = math.cos(th) * ff, math.sin(th) * ff
            x = SPILL["cx"] + dx * SPILL["ax"]
            z = SPILL["cz"] + dz * (SPILL["az_front"] if dz >= 0 else SPILL["az_back"])
            h = spill_height(x, z, rng_off) if fi < len(ring_f) - 1 else SPILL["edge"]
            p = G(x, h, z)
            row.append(m.vert(p, tuple(p)))
        grid.append(row)
    bottom = []
    for k in range(seg):
        top_v = grid[-1][k]
        p = Vector(top_v.co)
        p.z = 0.0
        bottom.append(m.vert(p, tuple(p)))
    top_isl = m.island()

    def uv_top(v):
        return (v.co.x, v.co.y)

    for k in range(seg):
        k1 = (k + 1) % seg
        c0 = grid[0][0]
        m.face([c0, grid[1][k], grid[1][k1]], [uv_top(c0), uv_top(grid[1][k]), uv_top(grid[1][k1])], top_isl)
    for fi in range(1, len(ring_f) - 1):
        for k in range(seg):
            k1 = (k + 1) % seg
            vs = [grid[fi][k], grid[fi + 1][k], grid[fi + 1][k1], grid[fi][k1]]
            m.face(vs, [uv_top(v) for v in vs], top_isl)
    side_isl = m.island()
    per = [0.0]
    for k in range(1, seg + 1):
        per.append(per[-1] + (grid[-1][k % seg].co - grid[-1][k - 1].co).length)
    for k in range(seg):
        k1 = (k + 1) % seg
        vs = [grid[-1][k], bottom[k], bottom[k1], grid[-1][k1]]
        m.face(vs, [(per[k], SPILL["edge"]), (per[k], 0.0), (per[k + 1], 0.0), (per[k + 1], SPILL["edge"])], side_isl)
    bot = m.face(list(reversed(bottom)), [(-v.co.x, v.co.y) for v in reversed(bottom)], m.island("hidden"))
    bmesh.ops.triangulate(m.bm, faces=[bot], quad_method="BEAUTY", ngon_method="BEAUTY")
    return m.finish(materials)


# ------------------------------------------------------------------------------------------ rock
def rock_mesh(name, rng, tris):
    """One fractured stone, centred, about unit radius; returns (mesh, bounding size).

    An icosphere is pressed onto a seeded set of fracture planes (each vertex pulled in along its ray to
    the nearest plane, a sphere cap where none bounds it), so the stone has flat faces and sharp arrises
    like split granite; Laplacian passes wear the arrises round, noise along the normal lumps and pits
    the faces, and the collapse decimator brings it to the triangle budget (flat faces go first)."""
    small = tris < 100
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2 if small else 4, radius=1.0)
    planes = []
    for _ in range(rng.randint(*ROCK_GEN["pebble_planes" if small else "planes"])):
        zc, th = rng.uniform(-1.0, 1.0), rng.uniform(0.0, 2 * math.pi)
        rr = math.sqrt(1.0 - zc * zc)
        planes.append((Vector((rr * math.cos(th), rr * math.sin(th), zc)), rng.uniform(*ROCK_GEN["plane_offset"])))
    for v in bm.verts:
        d = v.co.normalized()
        t = ROCK_GEN["cap"]
        for n, h in planes:
            c = d.dot(n)
            if c > 1e-6:
                t = min(t, h / c)
        v.co = d * t
    for _ in range(ROCK_GEN["wear_iters"]):
        bmesh.ops.smooth_vert(bm, verts=bm.verts[:], factor=ROCK_GEN["wear"],
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.normal_update()
    off = Vector((rng.uniform(-90, 90), rng.uniform(-90, 90), rng.uniform(-90, 90)))
    moves = []
    for v in bm.verts:
        lump = noise.noise(v.co * ROCK_GEN["lump_freq"] + off)
        pit = sum(noise.noise(v.co * ROCK_GEN["pit_freq"] * 2 ** o + off * (o + 2)) * 0.5 ** o for o in range(3))
        moves.append(v.normal * (ROCK_GEN["lump"] * lump + ROCK_GEN["pit"] * pit))
    for v, m in zip(bm.verts, moves):
        v.co += m
    outward(bm)
    me = bpy.data.meshes.new(f"_stone_{name}")
    bm.to_mesh(me)
    bm.free()
    out = decimated(me, tris)
    bpy.data.meshes.remove(me)
    bm = bmesh.new()
    bm.from_mesh(out)
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=-(lo + hi) / 2)
    outward(bm)
    bm.to_mesh(out)
    bm.free()
    return out, hi - lo


def decimated(me, tris):
    obj = bpy.data.objects.new("_dec", me)
    bpy.context.scene.collection.objects.link(obj)
    src_tris = sum(len(p.vertices) - 2 for p in me.polygons)
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, tris / src_tris)
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(obj.evaluated_get(dg))
    bpy.data.objects.remove(obj, do_unlink=True)
    return out


def flatten(bm, spec):
    """Square a rock off: vertices past a plane are clamped onto it (the original UVs ride along)."""
    for token in spec.split():
        d = FLAT_DIRS[token[0]]
        keep = float(token[1:])
        n = G(*d).normalized()
        proj = [v.co.dot(n) for v in bm.verts]
        lo, hi = min(proj), max(proj)
        cut = lo + keep * (hi - lo)
        for v, p in zip(bm.verts, proj):
            if p > cut:
                v.co -= n * (p - cut)


def place_rock(name, base_me, native, size, pos, rng, material, mirror=None, yaw=None, tilt=None, flat="",
               local_flat=""):
    me = base_me.copy()
    me.name = f"rock_{name}"
    sx, sup, sfr = size
    mirror = rng.random() < 0.5 if mirror is None else mirror
    yaw = rng.uniform(0, 360) if yaw is None else yaw
    tilt = (rng.uniform(-7, 7), rng.uniform(-7, 7)) if tilt is None else tilt
    S = Matrix.Diagonal(((-1 if mirror else 1) * sx / native.x, sfr / native.y, sup / native.z, 1.0))
    R = Euler((math.radians(tilt[0]), math.radians(tilt[1]), math.radians(yaw)), "XYZ").to_matrix().to_4x4()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.transform(bm, matrix=S, verts=bm.verts[:])
    if local_flat:  # squared off in the rock's own frame (a slab), before it is turned
        flatten(bm, local_flat)
    bmesh.ops.transform(bm, matrix=R, verts=bm.verts[:])
    if mirror:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)])
    hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    target = G(pos[0], pos[1], pos[2])
    shift = Vector((target.x - (lo.x + hi.x) / 2, target.y - (lo.y + hi.y) / 2, target.z - lo.z))
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=shift)
    if flat:
        flatten(bm, flat)
    # the clamps leave dead-flat planes: give every face back some relief, in metres so all stones match
    amp = min(ROCK_GEN["skin"], 0.06 * min(size))
    srng = random.Random(f"skin-{name}")
    off = Vector((srng.uniform(-90, 90), srng.uniform(-90, 90), srng.uniform(-90, 90)))
    bm.normal_update()
    moves = []
    for v in bm.verts:  # eased off on the arrises the clamps made, where a push along the normal folds a face over
        k = min((f.normal.dot(v.normal) for f in v.link_faces), default=1.0)
        ease = min(1.0, max(0.0, (k - 0.55) / 0.35))
        moves.append(v.normal * amp * ease * sum(noise.noise(v.co * ROCK_GEN["skin_freq"] * 2 ** o + off) * 0.5 ** o
                                                  for o in range(2)))
    for v, m in zip(bm.verts, moves):
        v.co += m
    cut = clip_to_ground(bm)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])  # slivers left by the clamps and the cut
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3], quad_method="BEAUTY",
                          ngon_method="BEAUTY")
    drop_slivers(bm)
    outward(bm)
    bm.to_mesh(me)
    bm.free()
    rid = me.attributes.new("rid", "FLOAT", "POINT")  # per-stone tone/pattern offset for the stone shader
    rid.data.foreach_set("value", [random.Random(f"rid-{name}").random()] * len(me.vertices))
    obj = bpy.data.objects.new(f"rock_{name}", me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.clear()
    me.materials.append(material)
    obj["kind"] = "rock"
    return obj, {"name": name, "size": list(size), "pos": list(pos), "yaw": round(yaw, 2),
                 "tilt": [round(t, 2) for t in tilt], "mirror": mirror, "flat": flat, "local_flat": local_flat,
                 "ground_cut": cut}


def drop_slivers(bm, min_area=1e-8):
    """Collapse the shortest edge of every zero-area triangle (the clamps and the ground cut leave a few)."""
    for _ in range(16):
        bad = [f for f in bm.faces if f.calc_area() < min_area]
        if not bad:
            return
        edges = {min(f.edges, key=lambda e: e.calc_length()) for f in bad}
        bmesh.ops.collapse(bm, edges=list(edges), uvs=False)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])


def outward(bm):
    """Consistent winding, then make sure it faces out (a sliver cut from a pebble can fool recalc)."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if bm.calc_volume(signed=True) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])


def clip_to_ground(bm):
    """Cut a closed mesh at z = 0, drop what is below and cap the hole (UVs of the cap: a small planar patch)."""
    if min(v.co.z for v in bm.verts) >= -1e-6:
        return False
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=(0, 0, 0), plane_no=(0, 0, 1), clear_inner=True)
    for v in bm.verts:
        if abs(v.co.z) < 1e-5:
            v.co.z = 0.0
    boundary = [e for e in bm.edges if e.is_boundary]
    faces = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)["faces"]
    if faces:
        res = bmesh.ops.triangulate(bm, faces=faces, quad_method="BEAUTY", ngon_method="BEAUTY")
        uv = bm.loops.layers.uv.active
        if uv is not None:
            for f in res["faces"]:
                for loop in f.loops:
                    loop[uv].uv = (0.5 + loop.vert.co.x * 0.02, 0.5 + loop.vert.co.y * 0.02)
    return True


# ------------------------------------------------------------------------------ shader building
class NB:
    def __init__(self, mat):
        mat.use_nodes = True
        self.nt = mat.node_tree
        for n in list(self.nt.nodes):
            self.nt.nodes.remove(n)

    def n(self, t, **kw):
        node = self.nt.nodes.new(t)
        for k, v in kw.items():
            setattr(node, k, v)
        return node

    def set(self, sock, v):
        if isinstance(v, bpy.types.NodeSocket):
            self.nt.links.new(v, sock)
        elif isinstance(v, (tuple, list)) and len(v) == 3 and sock.type == "RGBA":
            sock.default_value = (v[0], v[1], v[2], 1.0)
        else:
            sock.default_value = v

    @staticmethod
    def sock(sockets, ident):
        return next(s for s in sockets if s.identifier == ident)

    def math(self, op, a, b=0.0, clamp=False):
        m = self.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        self.set(m.inputs[0], a)
        self.set(m.inputs[1], b)
        return m.outputs[0]

    def mul(self, a, b):
        return self.math("MULTIPLY", a, b)

    def add(self, a, b):
        return self.math("ADD", a, b)

    def attr(self, name, out="Fac"):
        return self.n("ShaderNodeAttribute", attribute_name=name, attribute_type="GEOMETRY").outputs[out]

    def sep(self, v):
        s = self.n("ShaderNodeSeparateXYZ")
        self.set(s.inputs[0], v)
        return s.outputs[0], s.outputs[1], s.outputs[2]

    def comb(self, x, y, z):
        c = self.n("ShaderNodeCombineXYZ")
        for i, v in enumerate((x, y, z)):
            self.set(c.inputs[i], v)
        return c.outputs[0]

    def vscale(self, v, s):
        vm = self.n("ShaderNodeVectorMath", operation="SCALE")
        self.set(vm.inputs[0], v)
        self.set(vm.inputs["Scale"], s)
        return vm.outputs[0]

    def vadd(self, a, b):
        vm = self.n("ShaderNodeVectorMath", operation="ADD")
        self.set(vm.inputs[0], a)
        self.set(vm.inputs[1], b)
        return vm.outputs[0]

    def noise(self, vec, scale=1.0, detail=4.0, rough=0.5, distortion=0.0):
        t = self.n("ShaderNodeTexNoise")
        t.noise_dimensions = "3D"
        self.set(t.inputs["Vector"], vec)
        self.set(t.inputs["Scale"], scale)
        self.set(t.inputs["Detail"], detail)
        self.set(t.inputs["Roughness"], rough)
        self.set(t.inputs["Distortion"], distortion)
        return t.outputs["Fac"]

    def voronoi(self, vec, scale=1.0, feature="F1", rand=1.0):
        t = self.n("ShaderNodeTexVoronoi", feature=feature)
        t.voronoi_dimensions = "3D"
        self.set(t.inputs["Vector"], vec)
        self.set(t.inputs["Scale"], scale)
        self.set(t.inputs["Randomness"], rand)
        return t

    def smooth(self, x, a, b):
        mr = self.n("ShaderNodeMapRange", interpolation_type="SMOOTHSTEP", clamp=True)
        self.set(mr.inputs["Value"], x)
        self.set(mr.inputs["From Min"], a)
        self.set(mr.inputs["From Max"], b)
        return mr.outputs["Result"]

    def mix(self, fac, a, b):
        m = self.n("ShaderNodeMix", data_type="RGBA", clamp_factor=True)
        self.set(self.sock(m.inputs, "Factor_Float"), fac)
        self.set(self.sock(m.inputs, "A_Color"), a)
        self.set(self.sock(m.inputs, "B_Color"), b)
        return self.sock(m.outputs, "Result_Color")

    def mixf(self, fac, a, b):
        m = self.n("ShaderNodeMix", data_type="FLOAT", clamp_factor=True)
        self.set(self.sock(m.inputs, "Factor_Float"), fac)
        self.set(self.sock(m.inputs, "A_Float"), a)
        self.set(self.sock(m.inputs, "B_Float"), b)
        return self.sock(m.outputs, "Result_Float")

    def cmul(self, col, f):
        m = self.n("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY", clamp_factor=True)
        self.set(self.sock(m.inputs, "Factor_Float"), 1.0)
        self.set(self.sock(m.inputs, "A_Color"), col)
        cc = self.comb(f, f, f) if isinstance(f, bpy.types.NodeSocket) else (f, f, f)
        self.set(self.sock(m.inputs, "B_Color"), cc)
        return self.sock(m.outputs, "Result_Color")

    def ramp(self, fac, stops):
        r = self.n("ShaderNodeValToRGB")
        self.set(r.inputs["Fac"], fac)
        els = r.color_ramp.elements
        while len(els) < len(stops):
            els.new(0.5)
        for el, (pos, col) in zip(els, stops):
            el.position = pos
            el.color = (col[0], col[1], col[2], 1.0)
        return r.outputs["Color"]

    def finish(self, albedo, rough, metal, height, bump_strength=0.7, bump_dist=0.004, bevel=0.0):
        geo = self.n("ShaderNodeNewGeometry")
        base_normal = geo.outputs["Normal"]
        if bevel:
            bv = self.n("ShaderNodeBevel", samples=8)
            self.set(bv.inputs["Radius"], bevel)
            base_normal = bv.outputs["Normal"]
        bump = self.n("ShaderNodeBump")
        self.set(bump.inputs["Strength"], bump_strength)
        self.set(bump.inputs["Distance"], bump_dist)
        self.set(bump.inputs["Height"], height)
        self.set(bump.inputs["Normal"], base_normal)
        bsdf = self.n("ShaderNodeBsdfPrincipled")
        self.set(bsdf.inputs["Base Color"], albedo)
        self.set(bsdf.inputs["Roughness"], rough)
        self.set(bsdf.inputs["Metallic"], metal)
        self.set(bsdf.inputs["Normal"], bump.outputs["Normal"])
        out = self.n("ShaderNodeOutputMaterial")
        self.set(out.inputs["Surface"], bsdf.outputs["BSDF"])
        return {"albedo": albedo, "rough": rough, "metal": metal, "bsdf": bsdf, "out": out}


def height_above_ground(g):
    geo = g.n("ShaderNodeNewGeometry")
    return g.sep(geo.outputs["Position"])[2], geo


def wood_shader(mat):
    g = NB(mat)
    ml = g.attr("mloc", "Vector")
    mid = g.attr("mid")
    endc = g.attr("endcap")
    along, cy, cz = g.sep(ml)
    off = g.mul(mid, 37.0)
    a_off = g.add(along, off)
    geo = g.n("ShaderNodeNewGeometry")
    upness = g.math("MAXIMUM", g.sep(geo.outputs["Normal"])[2], 0.0)
    # fibre: fine streaks running along the member (about 1 cm across, metres along)
    fib = g.noise(g.comb(g.mul(a_off, 0.35), g.mul(cy, 105.0), g.mul(cz, 105.0)), 1.0, 12.0, 0.72, 0.15)
    fib_s = g.smooth(fib, 0.36, 0.66)
    streak = g.noise(g.comb(g.mul(a_off, 0.7), g.mul(cy, 26.0), g.mul(cz, 26.0)), 1.0, 6.0, 0.6, 0.3)
    # weather checks: long open cracks with a few wide ones
    vc = g.voronoi(g.comb(g.mul(a_off, 0.09), g.mul(cy, 11.0), g.mul(cz, 11.0)), 1.0, "DISTANCE_TO_EDGE")
    cw = g.add(0.012, g.mul(g.noise(g.comb(g.mul(a_off, 1.5), off, 3.0), 1.0, 2.0), 0.03))
    crack_raw = g.math("SUBTRACT", 1.0, g.smooth(vc.outputs["Distance"], 0.0, cw))
    cmask = g.smooth(g.noise(g.vadd(g.vscale(ml, 1.6), g.comb(off, 0.0, 0.0)), 1.0, 2.0), 0.38, 0.5)
    crack = g.mul(crack_raw, cmask)
    # knots
    kv = g.voronoi(g.comb(g.mul(a_off, 1.3), g.mul(cy, 1.3), g.mul(cz, 1.3)), 1.0, "F1")
    ksel = g.smooth(g.sep(kv.outputs["Color"])[0], 0.70, 0.76)
    kdist = kv.outputs["Distance"]
    knot = g.mul(ksel, g.math("SUBTRACT", 1.0, g.smooth(kdist, 0.03, 0.055)))
    knot_ring = g.mul(ksel, g.mul(g.smooth(kdist, 0.03, 0.05), g.math("SUBTRACT", 1.0, g.smooth(kdist, 0.06, 0.1))))
    # large zones: brown heartwood showing through, bleached silver where the weather hits
    big = g.noise(g.comb(g.mul(a_off, 0.5), g.mul(cy, 4.0), g.mul(cz, 4.0)), 1.0, 4.0, 0.55)
    tone = g.add(g.mul(fib_s, 0.55), g.mul(streak, 0.45))
    grey = g.ramp(tone, [(0.22, (0.050, 0.039, 0.028)), (0.42, (0.150, 0.120, 0.086)),
                         (0.60, (0.265, 0.220, 0.165)), (0.78, (0.375, 0.330, 0.270))])
    brown = g.ramp(tone, [(0.22, (0.055, 0.030, 0.015)), (0.45, (0.190, 0.110, 0.052)),
                          (0.70, (0.360, 0.200, 0.088))])
    brown_mask = g.mul(g.smooth(big, 0.40, 0.57), g.math("SUBTRACT", 0.92, g.mul(upness, 0.5)))
    side = g.mix(brown_mask, grey, brown)
    side = g.mix(g.mul(g.smooth(upness, 0.3, 0.9), 0.3), side, (0.30, 0.29, 0.27))
    side = g.mix(g.mul(crack, 0.95), side, (0.018, 0.015, 0.012))
    side = g.mix(g.mul(knot, 0.9), side, (0.040, 0.028, 0.018))
    side = g.mix(g.mul(knot_ring, 0.5), side, (0.17, 0.11, 0.065))
    side = g.cmul(side, g.add(0.80, g.mul(mid, 0.36)))
    # end grain: rings, radial checks, weathered toward grey
    r = g.math("SQRT", g.add(g.mul(cy, cy), g.mul(cz, cz)))
    ring_n = g.noise(g.vscale(ml, 7.0), 1.0, 3.0)
    rings = g.math("SINE", g.add(g.mul(r, 190.0), g.mul(ring_n, 5.0)))
    rings01 = g.add(g.mul(rings, 0.5), 0.5)
    endcol = g.ramp(rings01, [(0.0, (0.13, 0.090, 0.055)), (1.0, (0.33, 0.25, 0.16))])
    ang = g.math("ARCTAN2", cz, cy)
    rv = g.voronoi(g.comb(g.mul(ang, 1.6), g.mul(r, 2.0), off), 1.0, "DISTANCE_TO_EDGE")
    rcrack = g.mul(g.math("SUBTRACT", 1.0, g.smooth(rv.outputs["Distance"], 0.0, 0.05)), g.smooth(r, 0.02, 0.05))
    endcol = g.mix(g.mul(rcrack, 0.9), endcol, (0.02, 0.017, 0.014))
    endcol = g.mix(0.3, endcol, (0.20, 0.19, 0.17))
    albedo = g.mix(endc, side, endcol)
    # dirt splashed up from the ground
    hz = g.sep(geo.outputs["Position"])[2]
    dn = g.noise(g.vadd(g.vscale(ml, 4.0), g.comb(off, 0, 0)), 1.0, 4.0, 0.6)
    dirt = g.mul(g.math("SUBTRACT", 1.0, g.smooth(hz, 0.02, 0.8)), g.smooth(dn, 0.25, 0.6))
    albedo = g.mix(g.mul(dirt, 0.8), albedo, (0.095, 0.072, 0.050))
    rough = g.add(0.76, g.add(g.mul(fib_s, 0.12), g.mul(crack, 0.1)))
    height_side = g.add(g.add(g.mul(fib_s, 0.55), g.mul(streak, 0.35)), g.add(g.mul(crack, -1.6), g.mul(knot_ring, 0.3)))
    height_end = g.add(g.mul(rings01, 0.25), g.mul(rcrack, -1.0))
    height = g.mixf(endc, height_side, height_end)
    return g.finish(albedo, rough, 0.0, height, bump_strength=1.0, bump_dist=0.005, bevel=0.008)


def iron_shader(mat):
    g = NB(mat)
    geo = g.n("ShaderNodeNewGeometry")
    p = geo.outputs["Position"]
    n1 = g.noise(g.vscale(p, 40.0), 1.0, 6.0, 0.6)
    n2 = g.noise(g.vscale(p, 160.0), 1.0, 3.0, 0.5)
    rust = g.smooth(g.add(g.mul(n1, 0.8), g.mul(n2, 0.2)), 0.42, 0.62)
    albedo = g.mix(rust, (0.040, 0.037, 0.034), g.ramp(n2, [(0.3, (0.11, 0.045, 0.018)), (0.7, (0.26, 0.11, 0.035))]))
    rough = g.mixf(rust, 0.55, 0.88)
    metal = g.mixf(rust, 0.75, 0.05)
    height = g.add(g.mul(rust, 0.4), g.mul(n2, 0.3))
    return g.finish(albedo, rough, metal, height, bump_strength=0.5, bump_dist=0.001)


def rope_shader(mat):
    g = NB(mat)
    ml = g.attr("mloc", "Vector")
    along, cy, cz = g.sep(ml)
    ang = g.math("ARCTAN2", cz, cy)
    twist = g.math("SINE", g.add(g.mul(along, 260.0), g.mul(ang, 3.0)))
    fib = g.noise(g.comb(g.mul(along, 30.0), g.mul(cy, 300.0), g.mul(cz, 300.0)), 1.0, 4.0)
    t01 = g.add(g.mul(twist, 0.35), g.add(g.mul(fib, 0.5), 0.25))
    albedo = g.ramp(t01, [(0.2, (0.055, 0.042, 0.028)), (0.8, (0.19, 0.145, 0.09))])
    return g.finish(albedo, 0.93, 0.0, t01, bump_strength=0.8, bump_dist=0.002)


def dirt_shader(mat):
    g = NB(mat)
    ml = g.attr("mloc", "Vector")  # world position (Blender frame)
    x, y, z = g.sep(ml)
    mott = g.noise(g.vscale(ml, 2.2), 1.0, 5.0, 0.6)
    fine = g.noise(g.vscale(ml, 30.0), 1.0, 4.0, 0.55)
    albedo = g.ramp(g.add(g.mul(mott, 0.6), g.mul(fine, 0.4)),
                    [(0.3, (0.13, 0.11, 0.085)), (0.52, (0.25, 0.215, 0.17)), (0.7, (0.38, 0.345, 0.29))])
    gv = g.voronoi(g.vscale(ml, 38.0), 1.0, "F1")
    grit = g.mul(g.math("SUBTRACT", 1.0, g.smooth(gv.outputs["Distance"], 0.18, 0.32)),
                 g.smooth(g.sep(gv.outputs["Color"])[0], 0.45, 0.5))
    albedo = g.mix(g.mul(grit, 0.8), albedo, g.ramp(g.sep(gv.outputs["Color"])[2],
                                                    [(0.0, (0.18, 0.175, 0.165)), (1.0, (0.42, 0.41, 0.39))]))
    # damp patch pooled at the threshold (glTF z 1.85..2.45 is Blender y -2.45..-1.85)
    dx = g.smooth(g.math("ABSOLUTE", x), 0.55, 1.05)
    dz = g.smooth(g.math("ABSOLUTE", g.add(y, 2.15)), 0.18, 0.42)
    damp = g.mul(g.math("SUBTRACT", 1.0, g.math("MAXIMUM", dx, dz)), g.smooth(mott, 0.35, 0.55))
    albedo = g.mix(g.mul(damp, 0.85), albedo, (0.035, 0.030, 0.025))
    rough = g.mixf(damp, g.add(0.86, g.mul(fine, 0.1)), 0.28)
    height = g.add(g.mul(grit, 0.8), g.add(g.mul(fine, 0.3), g.mul(mott, 0.4)))
    return g.finish(albedo, rough, 0.0, height, bump_strength=0.9, bump_dist=0.008)


def stone_shader(mat):
    """Weathered grey granite for the pile, read in world space (stone has no grain direction)."""
    g = NB(mat)
    geo = g.n("ShaderNodeNewGeometry")
    p = geo.outputs["Position"]
    x, y, z = g.sep(p)
    rid = g.attr("rid")
    po = g.vadd(p, g.comb(g.mul(rid, 41.0), g.mul(rid, 17.0), g.mul(rid, 7.0)))  # each stone its own patterns
    upness = g.math("MAXIMUM", g.sep(geo.outputs["Normal"])[2], 0.0)
    broad = g.noise(g.vscale(po, 0.8), 1.0, 5.0, 0.55)
    midn = g.noise(g.vscale(po, 5.0), 1.0, 6.0, 0.6)
    tone = g.add(g.mul(broad, 0.55), g.mul(midn, 0.45))
    albedo = g.ramp(tone, [(0.28, (0.040, 0.039, 0.037)), (0.44, (0.088, 0.086, 0.082)),
                           (0.60, (0.150, 0.146, 0.138)), (0.78, (0.235, 0.229, 0.216))])
    # chipped, hammered faces: small conchoidal facets (about 12 cm) with dark seams between them
    chipv = g.voronoi(g.vscale(warp_src := g.vadd(po, g.vscale(g.comb(g.noise(g.vscale(po, 2.0), 1.0, 2.0), g.noise(g.vscale(po, 2.1), 1.0, 2.0), 0.5), 0.12)), 8.0), 1.0, "F1")
    chip = g.smooth(chipv.outputs["Distance"], 0.0, 0.6)  # height rises toward the cell rim: concave scallops
    chipe = g.voronoi(g.vscale(warp_src, 8.0), 1.0, "DISTANCE_TO_EDGE")
    seam = g.math("SUBTRACT", 1.0, g.smooth(chipe.outputs["Distance"], 0.0, 0.06))
    albedo = g.mix(g.mul(seam, 0.45), albedo, (0.030, 0.029, 0.028))
    albedo = g.cmul(albedo, g.add(1.12, g.mul(chip, -0.3)))
    # darker weathering rind in broad patches
    rind = g.smooth(g.noise(g.vscale(po, 1.7), 1.0, 4.0, 0.6), 0.48, 0.66)
    albedo = g.mix(g.mul(rind, 0.5), albedo, (0.045, 0.044, 0.042))
    # coarse crystals: pale feldspar and dark mica flecks (about 1.5 cm)
    fv = g.voronoi(g.vscale(po, 70.0), 1.0, "F1")
    fc = g.sep(fv.outputs["Color"])[0]
    albedo = g.mix(g.mul(g.smooth(fc, 0.89, 0.94), 0.25), albedo, (0.36, 0.35, 0.33))
    albedo = g.mix(g.mul(g.math("SUBTRACT", 1.0, g.smooth(fc, 0.08, 0.14)), 0.55), albedo, (0.030, 0.029, 0.028))
    # fractures: irregular dark lines on some of the cell edges
    warp = g.vadd(po, g.vscale(g.comb(g.noise(g.vscale(po, 3.0), 1.0, 3.0), g.noise(g.vscale(po, 3.1), 1.0, 3.0), 0.5), 0.25))
    cv = g.voronoi(g.vscale(warp, 1.7), 1.0, "DISTANCE_TO_EDGE")
    crack = g.mul(g.math("SUBTRACT", 1.0, g.smooth(cv.outputs["Distance"], 0.0, 0.013)),
                  g.smooth(g.noise(g.vscale(po, 1.1), 1.0, 2.0), 0.46, 0.56))
    albedo = g.mix(g.mul(crack, 0.9), albedo, (0.016, 0.015, 0.014))
    # weathering pocks, clustered
    pv = g.voronoi(g.vscale(po, 22.0), 1.0, "F1")
    pock = g.mul(g.math("SUBTRACT", 1.0, g.smooth(pv.outputs["Distance"], 0.10, 0.22)),
                 g.mul(g.smooth(g.sep(pv.outputs["Color"])[1], 0.55, 0.6), g.smooth(g.noise(g.vscale(po, 2.3), 1.0, 3.0), 0.45, 0.6)))
    albedo = g.mix(g.mul(pock, 0.7), albedo, (0.035, 0.034, 0.032))
    # rusty stain (an iron vein country rock), sparse
    stain_n = g.noise(g.comb(g.mul(x, 1.4), g.mul(y, 1.4), g.mul(z, 0.5)), 1.0, 4.0, 0.6)
    stain = g.mul(g.smooth(stain_n, 0.60, 0.70), 0.45)
    albedo = g.mix(stain, albedo, (0.17, 0.095, 0.050))
    # pale lichen and grey dust on the upward faces
    lich = g.mul(g.smooth(g.noise(g.vscale(po, 3.5), 1.0, 5.0, 0.6), 0.64, 0.70), g.smooth(upness, 0.35, 0.8))
    albedo = g.mix(g.mul(lich, 0.55), albedo, (0.26, 0.26, 0.19))
    albedo = g.mix(g.mul(g.smooth(upness, 0.55, 0.95), 0.28), albedo, (0.27, 0.25, 0.22))
    # earth splashed up the foot of every stone
    dn = g.noise(g.vscale(po, 6.0), 1.0, 4.0, 0.6)
    dust = g.mul(g.math("SUBTRACT", 1.0, g.smooth(z, 0.04, 0.55)), g.smooth(dn, 0.28, 0.6))
    albedo = g.mix(g.mul(dust, 0.8), albedo, (0.13, 0.105, 0.078))
    albedo = g.cmul(albedo, g.add(0.80, g.mul(rid, 0.40)))
    # the shaft behind the portal is in shadow: stones inside the mouth read dark (Blender y = -glTF z)
    inside = g.mul(g.mul(g.math("SUBTRACT", 1.0, g.smooth(g.math("ABSOLUTE", x), 0.9, 1.3)),
                         g.smooth(y, -2.1, -1.5)),
                   g.mul(g.smooth(z, 0.1, 0.55), g.math("SUBTRACT", 1.0, g.smooth(z, 2.25, 2.6))))
    albedo = g.cmul(albedo, g.math("SUBTRACT", 1.0, g.mul(inside, 0.75)))
    rough = g.add(0.80, g.add(g.mul(midn, 0.12), g.mul(crack, 0.06)))
    height = g.add(g.add(g.mul(midn, 0.5), g.mul(chip, 0.7)), g.add(g.mul(crack, -1.2), g.mul(pock, -0.8)))
    return g.finish(albedo, rough, 0.0, height, bump_strength=1.0, bump_dist=0.02)


SHADERS = {"wood": wood_shader, "iron": iron_shader, "rope": rope_shader, "dirt": dirt_shader, "stone": stone_shader}


def gltf_output_group():
    name = "glTF Material Output"
    ng = bpy.data.node_groups.get(name)
    if ng is None:
        ng = bpy.data.node_groups.new(name, "ShaderNodeTree")
        ng.interface.new_socket(name="Occlusion", in_out="INPUT", socket_type="NodeSocketFloat")
        ng.interface.new_socket(name="Thickness", in_out="INPUT", socket_type="NodeSocketFloat")
    return ng


def pbr_material(name, base, normal, orm, extension="EXTEND", vertex_colour=None):
    """Principled material wired the way Blender's glTF exporter maps to base/normal/ORM (+ occlusion).

    With vertex_colour, the base colour texture is multiplied by that colour attribute, which the
    exporter writes as COLOR_0 (glTF multiplies base colour by COLOR_0)."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = True  # every part is a closed outward solid: export single-sided
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    tb = nt.nodes.new("ShaderNodeTexImage")
    tb.image = base
    tb.extension = extension
    if vertex_colour:
        vc = nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = vertex_colour
        mul = nt.nodes.new("ShaderNodeMix")
        mul.data_type, mul.blend_type = "RGBA", "MULTIPLY"
        NB.sock(mul.inputs, "Factor_Float").default_value = 1.0
        nt.links.new(tb.outputs["Color"], NB.sock(mul.inputs, "A_Color"))
        nt.links.new(vc.outputs["Color"], NB.sock(mul.inputs, "B_Color"))
        nt.links.new(NB.sock(mul.outputs, "Result_Color"), bsdf.inputs["Base Color"])
    else:
        nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = normal
    tn.image.colorspace_settings.name = "Non-Color"
    tn.extension = extension
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    to = nt.nodes.new("ShaderNodeTexImage")
    to.image = orm
    to.image.colorspace_settings.name = "Non-Color"
    to.extension = extension
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(to.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    grp = nt.nodes.new("ShaderNodeGroup")
    grp.node_tree = gltf_output_group()
    nt.links.new(sep.outputs["Red"], grp.inputs["Occlusion"])
    return mat


# --------------------------------------------------------------------------------- build scene
def build_timber(materials, rng):
    objs = []
    mids = iter(rng.random() for _ in range(1000))
    back = Vector((0.0, 1.0, 0.0))  # Blender +Y = glTF -Z: seams face the rock
    posts = {}
    for side, sx in (("left", -1.0), ("right", 1.0)):
        i = 0 if side == "left" else 1
        lb, ls = math.radians(POST["lean_back_deg"][i]), math.radians(POST["lean_side_deg"][i])
        base = G(sx * POST["base_x"], 0.0, POST["base_z"])
        d = G(math.tan(ls), 1.0, -math.tan(lb)).normalized()
        top = base + d * POST["length"]
        posts[side] = (base, d)
        objs.append(log_member(f"post_{side}", "wood", base, top, POST["log_radius"], POST["log_radius"] * 0.95,
                               24, materials, next(mids), shape="square",
                               square_q=POST["half_side"] / POST["log_radius"], seam_hint=back,
                               cap0="ground", cap1="chamfer", chamfer=0.015, bow=0.02, bow_dir=G(1, 0, 0)))

    def post_axis_at(side, y):
        base, d = posts[side]
        return base + d * (y / d.z)

    # cap log resting in notches on the post fronts
    cy_l = CAP["underside_y"] + CAP["radius"]
    cy_r = cy_l - CAP["sag_right_m"]
    front_l = -post_axis_at("left", cy_l).y + POST["half_side"] + CAP["radius"] - CAP["notch_m"]
    front_r = -post_axis_at("right", cy_r).y + POST["half_side"] + CAP["radius"] - CAP["notch_m"]
    xl, xr = post_axis_at("left", cy_l).x, post_axis_at("right", cy_r).x

    def cap_at(x):
        t = (x - xl) / (xr - xl)
        return G(x, cy_l + (cy_r - cy_l) * t, front_l + (front_r - front_l) * t)

    cap0, cap1 = cap_at(CAP["x0"]), cap_at(CAP["x1"])
    objs.append(log_member("cap_log", "wood", cap0, cap1, CAP["radius"], CAP["radius"] * 0.93, 16, materials,
                           next(mids), seam_hint=back, bow=-0.025, bow_dir=G(0, 1, 0)))
    # iron spikes through the cap into each post
    for side, x in (("left", xl), ("right", xr)):
        c = cap_at(x)
        for k, dy in enumerate((-0.055, 0.06)):
            head = c + G(0.02 * (k - 0.5), dy, 0) + G(0, 0, CAP["radius"] - 0.012)
            objs.append(log_member(f"spike_cap_{side}_{k}", "iron", head - G(0, 0, 0.05), head + G(0, 0, 0.018),
                                   0.016, 0.016, 4, materials, next(mids), cap0="flat", cap1="chamfer",
                                   chamfer=0.003, wobble=0.0, taper_noise=0.0, seam_hint=G(0, 1, 0)))
    # inner set lintel, ends buried in the rock
    objs.append(log_member("lintel", "wood", G(LINTEL["x0"], LINTEL["y"][0], LINTEL["z"][0]),
                           G(LINTEL["x1"], LINTEL["y"][1], LINTEL["z"][1]), LINTEL["radius"], LINTEL["radius"] * 0.92,
                           12, materials, next(mids), seam_hint=back, bow=-0.03, bow_dir=G(0, 1, 0)))
    for st in STUBS:
        objs.append(log_member(st["name"], "wood", G(st["x"], st["y"][0], st["z"][0]), G(st["x"], st["y"][1], st["z"][1]),
                               st["radius"], st["radius"] * 0.95, 12, materials, next(mids), seam_hint=G(0, -1, 0)))
    # crossed pit props: feet on the ground, the short one pushed back so they touch at the crossing
    prop_axes = {}
    for pr in PROPS:
        top = G(*pr["top"])
        foot_xz = G(pr["foot"][0], 0.0, pr["foot"][1])
        axis = (top - foot_xz).normalized()
        foot = foot_xz + Vector((0, 0, pr["radius"] * math.sqrt(max(0.0, 1 - axis.z ** 2)) + 0.0005))
        prop_axes[pr["name"]] = [foot, top, pr["radius"]]
    (fa, ta, ra), (fb, tb, rb) = prop_axes["prop_long"], prop_axes["prop_short"]
    da, db = ta - fa, tb - fb
    nrm = da.cross(db).normalized()
    if nrm.y < 0:
        nrm = -nrm  # toward the back (Blender +Y)
    dist = (fb - fa).dot(nrm)
    push = (ra + rb - 0.012) - dist
    prop_axes["prop_short"][0] = fb + nrm * push
    prop_axes["prop_short"][1] = tb + nrm * push
    for pr in PROPS:
        foot, top, r = prop_axes[pr["name"]]
        objs.append(log_member(pr["name"], "wood", foot, top, r, r * 0.9, 12, materials, next(mids),
                               seam_hint=back, bow=0.015, bow_dir=G(0, 1, 0), cap0="chamfer", cap1="chamfer"))
    foot, top, r = prop_axes[ROPE["prop"]]
    ax = (top - foot).normalized()
    for k, s in enumerate(ROPE["at_from_foot_m"]):
        centre = foot + ax * s
        objs.append(ring_member(f"rope_{k}", "rope", centre, ax, r * 0.97 + ROPE["radius"] * 0.6, ROPE["radius"],
                                materials, next(mids), seg=14, sides=6, tilt=0.012 * (k - 1)))
    # boards nailed across the post fronts
    for bd in BOARDS:
        y = bd["y"]
        pl = post_axis_at("left", y) + G(0, 0, POST["half_side"])
        pr_ = post_axis_at("right", y) + G(0, 0, POST["half_side"])
        A = (pr_ - pl).normalized()
        up = ((posts["left"][1] + posts["right"][1]) / 2)
        B = (up - A * up.dot(A)).normalized()
        C = A.cross(B).normalized()
        t = PLANK["thickness"]
        x_span = bd["x1"] - bd["x0"]
        origin = pl + A * (bd["x0"] - pl.x) / max(1e-6, A.x) + C * (t / 2 + 0.001)
        outline = board_outline(x_span, bd["width"], rng, bd["jag"][0], bd["jag"][1])
        nails_at = [x for x in (pl.x, pr_.x) if bd["x0"] + 0.05 < x < bd["x1"] - 0.05]
        if "hang_deg" in bd:  # turned in its own plane about the nail at the right post
            pivot_a = pr_.x - bd["x0"]
            ang = math.radians(bd["hang_deg"])
            rot = Matrix.Rotation(ang, 3, C)
            pivot = origin + A * pivot_a
            A2, B2 = rot @ A, rot @ B
            origin = pivot - A2 * pivot_a
            A, B = A2, B2
            if abs(bd["hang_deg"]) > 10:  # hanging loose: only the pivot nail holds it
                nails_at = [pr_.x]
        objs.append(slab_member(bd["name"], "wood", outline, t, PLANK["chamfer"], origin, A, B, materials, next(mids),
                                max_edge=0.4))
        for k, x in enumerate(nails_at):
            a = x - bd["x0"]
            for j, b in enumerate((-0.065, 0.07)):
                p = origin + A * a + B * b + C * (t / 2)
                objs.append(log_member(f"nail_{bd['name']}_{k}_{j}", "iron", p - C * 0.012, p + C * 0.005, 0.008,
                                       0.008, 8, materials, next(mids), cap0="flat", cap1="chamfer", chamfer=0.002,
                                       wobble=0.0, taper_noise=0.0, seam_hint=B))
    # pegs and old spikes left in the posts and cap (the concept's posts bristle with them)
    for k in range(PEGS["count"]):
        side = ("left", "right")[k % 2]
        y = rng.uniform(*PEGS["height"])
        axis_pt = post_axis_at(side, y)
        across = rng.uniform(-0.07, 0.07)
        iron = rng.random() < PEGS["iron_share"]
        d = G(rng.uniform(-0.35, 0.35), rng.uniform(-0.15, 0.35), 1.0).normalized()
        root = axis_pt + G(across, 0.0, POST["half_side"] - 0.035)
        length = rng.uniform(*PEGS["length"]) + 0.035
        r = 0.007 if iron else rng.uniform(0.012, 0.017)
        objs.append(log_member(f"peg_{side}_{k}", "iron" if iron else "wood", root, root + d * length, r, r * 0.8,
                               4 if iron else 6, materials, next(mids), cap0="flat", cap1="flat" if iron else "chamfer",
                               chamfer=0.003, wobble=0.0, taper_noise=0.0, seam_hint=G(0, 1, 0), seg_len=1.0))
    for k in range(PEGS["on_cap"]):
        x = rng.uniform(CAP["x0"] + 0.4, CAP["x1"] - 0.4)
        c = cap_at(x)
        d = G(rng.uniform(-0.3, 0.3), 1.0, rng.uniform(-0.1, 0.4)).normalized()
        root = c + G(0, CAP["radius"] - 0.03, 0)
        objs.append(log_member(f"peg_cap_{k}", "wood", root, root + d * rng.uniform(0.09, 0.14), 0.016, 0.013, 6,
                               materials, next(mids), cap0="flat", cap1="chamfer", chamfer=0.003, wobble=0.0,
                               taper_noise=0.0, seam_hint=G(0, 0, -1), seg_len=1.0))
    # loose broken boards in the rubble
    for lb in LOOSE_BOARDS:
        yaw, pitch, roll = (math.radians(a) for a in lb["rot"])
        R = Euler((roll, pitch, yaw), "XYZ").to_matrix()
        A, B = R @ Vector((1, 0, 0)), R @ Vector((0, 1, 0))
        c = G(*lb["c"])
        outline = board_outline(lb["len"], lb["w"], rng, True, True)
        origin = c - A * (lb["len"] / 2)
        ob = slab_member(lb["name"], "wood", outline, PLANK["thickness"] - 0.004, PLANK["chamfer"], origin, A, B,
                         materials, next(mids), max_edge=0.4)
        lift_to_ground(ob)
        objs.append(ob)
    spill_off = rng.uniform(0, 100)
    objs.append(spill_member(materials, next(mids), spill_off))
    return objs, prop_axes, spill_off


def lift_to_ground(obj, sink=0.0):
    """Translate so the lowest vertex sits at z = 0 (minus sink): no timber member may go below ground."""
    zmin = min(v.co.z for v in obj.data.vertices)
    obj.data.transform(Matrix.Translation((0, 0, -zmin - sink)))


def build_rocks(rng, material, rock_rng_seed, spill_off):
    def stone(name, var):  # each stone's shape has its own stream, so editing one never reshapes the others
        return rock_mesh(name, random.Random(f"{rock_rng_seed}-{name}"), ROCK_VARIANTS[var])

    objs, records = [], []
    for name, var, size, pos, flat in ROCKS:
        yaw = rng.uniform(0, 360)
        tilt = (rng.uniform(-6, 6), rng.uniform(-6, 6))
        mirror = rng.random() < 0.5
        me, native = stone(name, var)
        o, rec = place_rock(name, me, native, size, pos, rng, material, mirror=mirror, yaw=yaw, tilt=tilt,
                            flat=flat)
        bpy.data.meshes.remove(me)
        rec["variant"] = var
        objs.append(o)
        records.append(rec)
    for sl in SLABS:  # a small stone scaled thin and clamped flat both sides: a broken slab
        L, W, T = sl["size"]
        me, native = stone(sl["name"], "small")
        o, rec = place_rock(sl["name"], me, native, (L, 2.5 * T, W), (sl["c"][0], 0.0, sl["c"][1]),
                            rng, material, mirror=rng.random() < 0.5, yaw=sl["yaw"], tilt=(sl["lean"], 0.0),
                            local_flat="u.7 d.7")
        bpy.data.meshes.remove(me)
        rec["variant"] = "slab"
        objs.append(o)
        records.append(rec)
    prng = random.Random(rock_rng_seed + 7)
    for k in range(PEBBLES["count"]):
        for _ in range(100):  # only where the spill is, so every pebble rests on something
            x = prng.uniform(*PEBBLES["x"])
            z = prng.uniform(*PEBBLES["z"])
            if spill_height(x, z, spill_off) > SPILL["edge"] * 2.5:
                break
        s = prng.uniform(*PEBBLES["size"])
        size = (s * prng.uniform(0.8, 1.3), s * prng.uniform(0.5, 0.8), s * prng.uniform(0.8, 1.2))
        pos = (x, -size[1] * 0.3, z)  # re-seated on the spill once it exists (seat_pebbles)
        me, native = stone(f"P{k:02d}", "pebble")
        o, rec = place_rock(f"P{k:02d}", me, native, size, pos, prng, material)
        bpy.data.meshes.remove(me)
        rec["variant"] = "pebble"
        objs.append(o)
        records.append(rec)
    return objs, records


def seat_on_spill(rock_objs, spill_obj):
    """Drop every pebble so it sinks 30% of its height into the spill (or the ground) below it."""
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(spill_obj, dg)
    for o in rock_objs:
        if not o.name.startswith("rock_P"):
            continue
        vs = o.data.vertices
        lo = min(v.co.z for v in vs)
        hi = max(v.co.z for v in vs)
        cx = sum(v.co.x for v in vs) / len(vs)
        cy = sum(v.co.y for v in vs) / len(vs)
        hit = bvh.ray_cast(Vector((cx, cy, 5.0)), Vector((0, 0, -1)))
        ground = hit[0].z if hit[0] is not None else 0.0
        target_lo = ground - 0.3 * (hi - lo)
        o.data.transform(Matrix.Translation((0, 0, target_lo - lo)))
        bm = bmesh.new()
        bm.from_mesh(o.data)
        clip_to_ground(bm)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges[:])
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3], quad_method="BEAUTY",
                              ngon_method="BEAUTY")
        drop_slivers(bm)
        outward(bm)
        bm.to_mesh(o.data)
        bm.free()


def unwrap_rocks(obj, hidden_scale=0.04, buried_reach=0.3, rays=16):
    """UVs of the pile: smart projection keeps every island at its true size (one texel density across
    the stones). Faces nobody can see - cut flat on the ground, or buried (every ray over the normal
    hemisphere hits the pile, the timber or the ground within buried_reach, e.g. inside a neighbour) -
    are projected apart and shrunk to almost nothing, then everything is packed into the unit square."""
    me = obj.data
    dg = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, dg) for o in bpy.context.scene.objects if o.type == "MESH"]
    rng = random.Random(3)
    dirs = []
    for _ in range(rays):
        u1, u2 = rng.random(), rng.random()
        r, th = math.sqrt(u1), 2 * math.pi * u2
        dirs.append((r * math.cos(th), r * math.sin(th), math.sqrt(max(0.0, 1 - u1))))
    buried = set()
    for poly in me.polygons:
        n = poly.normal
        c = poly.center + n * 0.003
        t = n.orthogonal().normalized()
        b = n.cross(t)
        for dx, dy, dz in dirs:
            d = t * dx + b * dy + n * dz
            if d.z < -1e-4 and -c.z / d.z < buried_reach:
                continue
            if not any(tr.ray_cast(c, d, buried_reach)[0] is not None for tr in trees):
                break
        else:
            buried.add(poly.index)
    if "UVMap" not in me.uv_layers:
        me.uv_layers.new(name="UVMap")
    for o in bpy.context.scene.objects:
        o.select_set(o == obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bm = bmesh.from_edit_mesh(me)
    bm.faces.ensure_lookup_table()
    bm.faces.index_update()

    def on_ground(f):
        return f.index in buried or (f.normal.z < -0.95 and max(v.co.z for v in f.verts) < 1e-3)

    hidden_idx = set()
    for want_hidden in (False, True):
        for f in bm.faces:
            f.select = on_ground(f) == want_hidden
            if want_hidden and f.select:
                hidden_idx.add(f.index)
        bmesh.update_edit_mesh(me)
        bpy.ops.uv.smart_project(angle_limit=math.radians(60.0), island_margin=0.0, area_weight=0.0,
                                 correct_aspect=True, scale_to_bounds=False)
        bm = bmesh.from_edit_mesh(me)
        bm.faces.ensure_lookup_table()
    uvl = bm.loops.layers.uv.active
    for i in hidden_idx:
        for loop in bm.faces[i].loops:
            loop[uvl].uv *= hidden_scale
    for f in bm.faces:
        f.select = True
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.002)
    bpy.ops.object.mode_set(mode="OBJECT")
    area_3d = sum(p.area for i, p in enumerate(me.polygons) if i not in hidden_idx)
    return {"method": "smart_project 60 deg + pack_islands", "islands_hidden_scale": hidden_scale,
            "visible_area_m2": round(area_3d, 2), "hidden_faces": len(hidden_idx), "buried_faces": len(buried),
            "buried_reach_m": buried_reach}


# --------------------------------------------------------------------------------- contact graph
def contact_report(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    trees = [BVHTree.FromObject(o, dg) for o in objs]
    boxes = []
    for o in objs:
        cs = [v.co for v in o.data.vertices]
        boxes.append((Vector([min(c[i] for c in cs) for i in range(3)]), Vector([max(c[i] for c in cs) for i in range(3)])))
    parent = list(range(len(objs)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(len(objs)):
        for j in range(i + 1, len(objs)):
            lo1, hi1 = boxes[i]
            lo2, hi2 = boxes[j]
            if any(lo1[k] > hi2[k] + 1e-3 or lo2[k] > hi1[k] + 1e-3 for k in range(3)):
                continue
            if trees[i].overlap(trees[j]):
                parent[find(i)] = find(j)
    groups = {}
    for i in range(len(objs)):
        groups.setdefault(find(i), []).append(objs[i].name)
    main = max(groups.values(), key=len)
    return [g for g in groups.values() if g is not main]


# ------------------------------------------------------------------------------------ UV packing
def island_rects(obj):
    me = obj.data
    nl = len(me.loops)
    uv = np.empty(nl * 2, np.float32)
    me.uv_layers["UVMap"].data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    isl = np.empty(len(me.polygons), np.int32)
    me.attributes["island"].data.foreach_get("value", isl)
    loop_face = np.empty(nl, np.int32)
    starts = np.empty(len(me.polygons), np.int32)
    totals = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get("loop_start", starts)
    me.polygons.foreach_get("loop_total", totals)
    loop_face = np.repeat(np.arange(len(me.polygons)), totals)
    loop_isl = isl[loop_face]
    rects = {}
    for i in np.unique(loop_isl):
        sel = uv[loop_isl == i]
        rects[int(i)] = (sel.min(0), sel.max(0))
    return uv, loop_isl, rects


def shelf_pack(items, atlas):
    """items: (id, w_px, h_px) incl. padding. Shelf packing, tallest first. Returns {id: (x, y)} or None."""
    items = sorted(items, key=lambda t: (-t[2], -t[1], t[0]))
    x = y = shelf = 0.0
    place = {}
    for iid, w, h in items:
        if w > atlas:
            return None
        if x + w > atlas:
            y += shelf
            x = shelf = 0.0
        if y + h > atlas:
            return None
        place[iid] = (x, y)
        x += w
        shelf = max(shelf, h)
    return place


def pack_atlas(obj, atlas, target_density, pad):
    uv, loop_isl, rects = island_rects(obj)
    info = {}
    for iid, (lo, hi) in rects.items():
        kind = ISLANDS[iid]["kind"]
        w, h = float(hi[0] - lo[0]), float(hi[1] - lo[1])
        info[iid] = (lo, w, h, KIND_DENSITY[kind], h > w)

    def attempt(d):
        items = []
        for iid, (lo, w, h, ks, rot) in info.items():
            pw, ph = (h, w) if rot else (w, h)
            items.append((iid, pw * d * ks + 2 * pad, ph * d * ks + 2 * pad))
        return shelf_pack(items, atlas)

    lo_d, hi_d = 20.0, target_density
    best = None
    if attempt(hi_d) is not None:
        lo_d = hi_d
    else:
        for _ in range(40):
            mid = 0.5 * (lo_d + hi_d)
            if attempt(mid) is not None:
                lo_d = mid
            else:
                hi_d = mid
    d = lo_d
    place = attempt(d)
    new_uv = uv.copy()
    for iid, (lo, w, h, ks, rot) in info.items():
        sel = loop_isl == iid
        local = uv[sel] - lo
        if rot:
            local = np.stack([local[:, 1], w - local[:, 0]], 1)
        px, py = place[iid]
        new_uv[sel] = (np.array([px + pad, py + pad]) + local * d * ks) / atlas
    obj.data.uv_layers["UVMap"].data.foreach_set("uv", new_uv.reshape(-1))
    used = sum(((h if rot else w) * d * ks) * ((w if rot else h) * d * ks) for (lo, w, h, ks, rot) in info.values())
    return {"density_px_per_m": {k: round(d * v, 1) for k, v in KIND_DENSITY.items()},
            "islands": len(info), "atlas_fill": round(used / atlas ** 2, 3)}


# ------------------------------------------------------------------------------------------ bake
def setup_cycles(samples):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    device = "CPU"
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for backend in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = backend
                prefs.get_devices()
                gpus = [d for d in prefs.devices if d.type == backend]
                if gpus:
                    for d in prefs.devices:
                        d.use = d.type == backend
                    scene.cycles.device = "GPU"
                    device = backend
                    break
            except TypeError:
                continue
    except KeyError:
        pass
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.seed = 0
    return device


def bake_atlas(timber, bake_mats, atlas, args, tmpdir, label="timber", ao_distance=None, ao_darken=0.45):
    scene = bpy.context.scene
    device = setup_cycles(args.bake_samples)
    if scene.world is None:
        scene.world = bpy.data.worlds.new("World")
    scene.world.light_settings.distance = args.ao_distance if ao_distance is None else ao_distance
    for o in scene.objects:
        o.select_set(False)
    timber.select_set(True)
    bpy.context.view_layer.objects.active = timber
    bake = scene.render.bake
    bake.margin = 8
    bake.margin_type = "EXTEND"
    bake.use_clear = True
    bake.target = "IMAGE_TEXTURES"
    images = {}

    def new_image(name, colour):
        img = bpy.data.images.new(name, atlas, atlas, alpha=False, float_buffer=False)
        img.colorspace_settings.name = colour
        return img

    def point_nodes(img):
        for mat in bake_mats.values():
            nt = mat.node_tree
            node = nt.nodes.get("BAKE_TARGET") or nt.nodes.new("ShaderNodeTexImage")
            node.name = "BAKE_TARGET"
            node.image = img
            nt.nodes.active = node

    timings = {}
    fallbacks = []

    def run_bake(kind):
        """Bake on the GPU; the card is shared with other jobs, so fall back to the CPU if it cannot."""
        try:
            bpy.ops.object.bake(type=kind)
        except RuntimeError as err:
            if scene.cycles.device != "GPU":
                raise
            fallbacks.append(f"{kind}: {err}".strip()[:200])
            scene.cycles.device = "CPU"
            bpy.ops.object.bake(type=kind)

    for channel, colour in (("albedo", "sRGB"), ("rough", "Non-Color"), ("metal", "Non-Color")):
        img = new_image(f"bake_{channel}", colour)
        point_nodes(img)
        for mat in bake_mats.values():
            nt, sockets = mat.node_tree, SOCKETS[mat.name]
            em = nt.nodes.get("BAKE_EMIT") or nt.nodes.new("ShaderNodeEmission")
            em.name = "BAKE_EMIT"
            src = sockets[channel]
            for link in list(em.inputs["Color"].links):
                nt.links.remove(link)
            if isinstance(src, bpy.types.NodeSocket):
                nt.links.new(src, em.inputs["Color"])
            else:
                v = src if isinstance(src, (tuple, list)) else (src, src, src)
                em.inputs["Color"].default_value = (v[0], v[1], v[2], 1.0)
            nt.links.new(em.outputs["Emission"], sockets["out"].inputs["Surface"])
        t0 = time.time()
        run_bake("EMIT")
        timings[channel] = round(time.time() - t0, 1)
        images[channel] = img
    for mat in bake_mats.values():
        s = SOCKETS[mat.name]
        mat.node_tree.links.new(s["bsdf"].outputs["BSDF"], s["out"].inputs["Surface"])
    img = new_image("bake_normal", "Non-Color")
    point_nodes(img)
    bake.normal_space = "TANGENT"
    bake.normal_r, bake.normal_g, bake.normal_b = "POS_X", "POS_Y", "POS_Z"
    scene.cycles.samples = max(8, args.bake_samples)
    t0 = time.time()
    run_bake("NORMAL")
    timings["normal"] = round(time.time() - t0, 1)
    images["normal"] = img
    img = new_image("bake_ao", "Non-Color")
    point_nodes(img)
    scene.cycles.samples = args.ao_samples
    t0 = time.time()
    run_bake("AO")
    timings["ao"] = round(time.time() - t0, 1)
    images["ao"] = img
    # compose: albedo darkened a little by AO, ORM = (AO, roughness, metallic)
    def px(img):
        a = np.empty(atlas * atlas * 4, np.float32)
        img.pixels.foreach_get(a)
        return a.reshape(atlas, atlas, 4)

    alb, rough, metal, ao, nrm = (px(images[k]) for k in ("albedo", "rough", "metal", "ao", "normal"))
    ao1 = np.clip(ao[..., 0], 0, 1)
    lin = np.where(alb[..., :3] <= 0.04045, alb[..., :3] / 12.92, ((alb[..., :3] + 0.055) / 1.055) ** 2.4)
    lin *= (1.0 - ao_darken * (1.0 - ao1))[..., None]
    srgb = np.where(lin <= 0.0031308, lin * 12.92, 1.055 * np.power(np.maximum(lin, 0), 1 / 2.4) - 0.055)
    out = {}
    for name, arr, colour in (("basecolor", np.dstack([srgb, np.ones((atlas, atlas))]), "sRGB"),
                              ("normal", nrm, "Non-Color"),
                              ("orm", np.dstack([ao1, rough[..., 0], metal[..., 0], np.ones((atlas, atlas))]), "Non-Color")):
        img = bpy.data.images.new(f"T_{ASSET_ID}_{label}_{name}", atlas, atlas, alpha=False)
        img.colorspace_settings.name = colour
        img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).reshape(-1))
        path = os.path.join(tmpdir, f"T_{ASSET_ID}_{label}_{name}.png")
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        bpy.data.images.remove(img)
        loaded = bpy.data.images.load(path)
        loaded.colorspace_settings.name = colour
        loaded.pack()
        out[name] = loaded
    stats = {"device": device, "gpu_fallbacks": fallbacks, "seconds": timings,
             "albedo_luma_mean_srgb": round(float((srgb[..., 0] * 0.2126 + srgb[..., 1] * 0.7152 + srgb[..., 2] * 0.0722).mean() * 255), 1),
             "ao_mean": round(float(ao1.mean()), 3)}
    return out, stats


# --------------------------------------------------------------------------------------- finish
def join(objs, name):
    target = objs[0]
    with bpy.context.temp_override(active_object=target, object=target, selected_objects=objs,
                                   selected_editable_objects=objs):
        bpy.ops.object.join()
    target.name = name
    target.data.name = name
    return target


def finalize_normals(obj, angle_deg):
    me = obj.data
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    me.set_sharp_from_angle(angle=math.radians(angle_deg))


def remove_attrs(me, names):
    for n in names:
        if n in me.attributes:
            me.attributes.remove(me.attributes[n])


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ------------------------------------------------------------------------------------ validation
def read_glb(path):
    data = open(path, "rb").read()
    _, _, length = struct.unpack_from("<III", data, 0)
    off, chunks = 12, []
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunks.append(data[off + 8: off + 8 + clen])
        off += 8 + clen
    gltf = json.loads(chunks[0])
    binc = chunks[1]
    comp = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
    width = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}

    def acc(i):
        a = gltf["accessors"][i]
        bv = gltf["bufferViews"][a["bufferView"]]
        dt = np.dtype(comp[a["componentType"]])
        n = a["count"] * width[a["type"]]
        start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = bv.get("byteStride")
        if stride and stride != dt.itemsize * width[a["type"]]:
            raw = np.frombuffer(binc, np.uint8, count=stride * a["count"], offset=start).reshape(a["count"], stride)
            return raw[:, : dt.itemsize * width[a["type"]]].copy().view(dt).reshape(a["count"], -1)
        return np.frombuffer(binc, dt, count=n, offset=start).reshape(a["count"], -1)

    return gltf, acc


def validate_glb(path, atlas):
    gltf, acc = read_glb(path)
    report = {"nodes": len(gltf.get("nodes", [])), "materials": [m["name"] for m in gltf.get("materials", [])],
              "images": [(im.get("name"), im.get("mimeType")) for im in gltf.get("images", [])], "primitives": []}
    lo_all, hi_all = np.full(3, np.inf), np.full(3, -np.inf)
    total = 0
    for mesh in gltf["meshes"]:
        for prim in mesh["primitives"]:
            pos = acc(prim["attributes"]["POSITION"]).astype(np.float64)
            nrm = acc(prim["attributes"]["NORMAL"]).astype(np.float64)
            uv = acc(prim["attributes"]["TEXCOORD_0"]).astype(np.float64)
            idx = acc(prim["indices"]).astype(np.int64).reshape(-1, 3)
            total += len(idx)
            lo_all = np.minimum(lo_all, pos.min(0))
            hi_all = np.maximum(hi_all, pos.max(0))
            p0, p1, p2 = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
            fn = np.cross(p1 - p0, p2 - p0)
            area = 0.5 * np.linalg.norm(fn, axis=1)
            vn = nrm[idx].sum(1)
            inverted = int(((fn * vn).sum(1) < 0).sum())
            t0, t1, t2 = uv[idx[:, 0]], uv[idx[:, 1]], uv[idx[:, 2]]
            uva = 0.5 * np.abs((t1[:, 0] - t0[:, 0]) * (t2[:, 1] - t0[:, 1]) - (t2[:, 0] - t0[:, 0]) * (t1[:, 1] - t0[:, 1]))
            # weld by position -> components, closedness, orientation, signed volume
            key = np.round(pos / 1e-5).astype(np.int64)
            _, weld = np.unique(key, axis=0, return_inverse=True)
            weld = weld.reshape(-1)
            w = weld[idx]
            e = np.concatenate([w[:, [0, 1]], w[:, [1, 2]], w[:, [2, 0]]])
            tri_of_edge = np.tile(np.arange(len(w)), 3)
            nondeg = (w[:, 0] != w[:, 1]) & (w[:, 1] != w[:, 2]) & (w[:, 0] != w[:, 2])
            keep = np.tile(nondeg, 3)
            e, tri_of_edge = e[keep], tri_of_edge[keep]
            und = np.sort(e, 1)
            uk, inv, cnt = np.unique(und, axis=0, return_inverse=True, return_counts=True)
            inv = inv.reshape(-1)
            directed = np.unique(e, axis=0, return_counts=True)[1]
            boundary = int((cnt == 1).sum())
            nonmanifold = int((cnt > 2).sum())
            same_dir = int((directed > 1).sum())
            # triangle components through shared undirected edges
            parent = np.arange(len(w))

            def find(a):
                while parent[a] != a:
                    parent[a] = parent[parent[a]]
                    a = parent[a]
                return a

            order = np.argsort(inv, kind="stable")
            inv_sorted = inv[order]
            tris_sorted = tri_of_edge[order]
            for k in range(1, len(order)):
                if inv_sorted[k] == inv_sorted[k - 1]:
                    ra, rb = find(tris_sorted[k]), find(tris_sorted[k - 1])
                    if ra != rb:
                        parent[ra] = rb
            roots = np.array([find(t) for t in range(len(w))])
            vol = np.einsum("ij,ij->i", p0, np.cross(p1, p2)) / 6.0
            comps, comp_idx = np.unique(roots, return_inverse=True)
            comp_vol = np.bincount(comp_idx, weights=vol)
            mat = gltf["materials"][prim["material"]]["name"] if "material" in prim else None
            dens = np.sqrt(np.divide(uva * atlas * atlas, area, out=np.zeros_like(area), where=area > 1e-12))
            colour = None
            if "COLOR_0" in prim["attributes"]:
                c0 = acc(prim["attributes"]["COLOR_0"])
                c0 = c0.astype(np.float64) / (65535.0 if c0.dtype == np.uint16 else 255.0 if c0.dtype == np.uint8 else 1.0)
                colour = {"min": round(float(c0[:, :3].min()), 3), "mean": round(float(c0[:, :3].mean()), 3),
                          "max": round(float(c0[:, :3].max()), 3)}
            report["primitives"].append({
                "material": mat, "triangles": int(len(idx)), "vertices": int(len(pos)), "color_0": colour,
                "components": int(len(comps)), "components_negative_volume": int((comp_vol <= 0).sum()),
                "boundary_edges": boundary, "nonmanifold_edges": nonmanifold, "same_direction_edges": same_dir,
                "zero_area_tris": int((area < 1e-10).sum()), "uv_degenerate_tris": int((uva < 1e-12).sum()),
                "inverted_shading_tris": inverted,
                "texel_density_px_per_m_p10_p50_p90": [round(float(np.percentile(dens[area > 1e-8], q)), 1) for q in (10, 50, 90)],
            })
    report["triangles"] = total
    report["bounds_min"] = [round(float(v), 4) for v in lo_all]
    report["bounds_max"] = [round(float(v), 4) for v in hi_all]
    size = hi_all - lo_all
    report["size_xyz_m"] = [round(float(v), 4) for v in size]
    report["centre_xz"] = [round(float((lo_all[0] + hi_all[0]) / 2), 4), round(float((lo_all[2] + hi_all[2]) / 2), 4)]
    w, dpt, h = BLOCKER["width_x_m"], BLOCKER["depth_z_m"], BLOCKER["height_m"]
    report["fitting"] = {
        "blocker": [w, dpt, h],
        "turned": bool((size[0] >= size[2]) != (w >= dpt)),
        "empty_x_per_side": round(float((w - size[0]) / 2), 3), "empty_z_per_side": round(float((dpt - size[2]) / 2), 3),
        "height_shortfall": round(float(h - size[1]), 3),
        "passes": bool(w - size[0] <= 2 * FIT_SIDE_MARGIN + 0.005 and dpt - size[2] <= 2 * FIT_SIDE_MARGIN + 0.005
                       and h - size[1] <= FIT_HEIGHT_SHORTFALL + 0.005),
    }
    return report


# ------------------------------------------------------------------------------------------ main
def main():
    args = parse_args()
    t_start = time.time()
    os.makedirs(args.out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    stone_mat = bpy.data.materials.new("_bake_stone")
    if args.preview:
        stone_mat.use_nodes = True
        stone_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.3, 0.29, 0.27, 1)
    else:
        SOCKETS[stone_mat.name] = stone_shader(stone_mat)

    bake_mats = {}
    for kind in MATERIAL_KINDS:
        mat = bpy.data.materials.new(f"_bake_{kind}")
        if args.preview:
            mat.use_nodes = True
            mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*PREVIEW_COLOURS[kind], 1)
        else:
            SOCKETS[mat.name] = SHADERS[kind](mat)
        bake_mats[kind] = mat

    # separate streams, so a change to the timber never reshuffles the rock pile (and the other way round)
    timber_objs, prop_axes, spill_off = build_timber(bake_mats, random.Random(args.seed))
    rock_objs, rock_records = build_rocks(random.Random(args.seed + 1), stone_mat, args.seed, spill_off)
    spill = next(o for o in timber_objs if o.name == "spill")
    seat_on_spill(rock_objs, spill)

    all_objs = timber_objs + rock_objs
    detached = contact_report(all_objs)
    inverted = []
    for o in all_objs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        if bm.calc_volume(signed=True) <= 0:
            inverted.append(o.name)
        bm.free()
    part_tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in all_objs}

    timber = join(timber_objs, f"{ASSET_ID}_timber")
    pack = None
    bake_stats = None
    if not args.preview:
        pack = pack_atlas(timber, args.atlas, args.target_density, args.pad)
        tmpdir = tempfile.mkdtemp(prefix="procgen_shaft_")
        textures, bake_stats = bake_atlas(timber, bake_mats, args.atlas, args, tmpdir)
        timber_mat = pbr_material(f"MAT_{ASSET_ID}_timber", textures["basecolor"], textures["normal"], textures["orm"])
        timber.data.materials.clear()
        timber.data.materials.append(timber_mat)
        timber.data.polygons.foreach_set("material_index", [0] * len(timber.data.polygons))
    finalize_normals(timber, 40.0)
    rocks = join(rock_objs, f"{ASSET_ID}_rock")
    rock_uv = rock_bake = None
    if not args.preview:
        rock_uv = unwrap_rocks(rocks)
        bpy.ops.mesh.primitive_plane_add(size=30.0, location=(0.0, 0.0, 0.0))  # the ground, for the stones' AO
        ground = bpy.context.active_object
        rtex, rock_bake = bake_atlas(rocks, {"stone": stone_mat}, args.atlas, args, tmpdir, label="rock",
                                     ao_distance=args.rock_ao_distance, ao_darken=0.6)
        bpy.data.objects.remove(ground, do_unlink=True)
        rock_mat = pbr_material(f"MAT_{ASSET_ID}_rock", rtex["basecolor"], rtex["normal"], rtex["orm"])
        rocks.data.materials.clear()
        rocks.data.materials.append(rock_mat)
        rocks.data.polygons.foreach_set("material_index", [0] * len(rocks.data.polygons))
    finalize_normals(rocks, 55.0)
    final = join([timber, rocks], ASSET_ID)
    remove_attrs(final.data, ["mloc", "mid", "endcap", "island", "rid"])

    # centre on X/Z (glTF), lowest point at y = 0
    vs = final.data.vertices
    co = np.empty(len(vs) * 3, np.float32)
    vs.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lo, hi = co.min(0), co.max(0)
    final.data.transform(Matrix.Translation((-(lo[0] + hi[0]) / 2, -(lo[1] + hi[1]) / 2, -lo[2])))
    final.data.update()

    if args.save_blend:
        bpy.ops.wm.save_as_mainfile(filepath=args.save_blend, compress=True)
    glb = os.path.join(args.out, f"{ASSET_ID}.glb")
    for o in scene.objects:
        o.select_set(o == final)
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", use_selection=True, export_yup=True,
                              export_apply=True, export_materials="EXPORT", export_image_format="AUTO",
                              export_texcoords=True, export_normals=True, export_cameras=False, export_lights=False,
                              export_extras=False)
    report = validate_glb(glb, args.atlas)

    prov = {
        "asset_id": ASSET_ID,
        "method": "procedural",
        "script": "tools/asset_pipeline/_procgen_shaft.py",
        "command": "blender --background --factory-startup --python tools/asset_pipeline/_procgen_shaft.py -- "
                   + " ".join(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []),
        "blender": bpy.app.version_string,
        "date": datetime.datetime.now().isoformat(timespec="seconds"),
        "seed": args.seed,
        "preview": bool(args.preview),
        "concept_used": {"path": os.path.relpath(CONCEPT, REPO).replace("\\", "/"), "sha256": sha256(CONCEPT)},
        "world_blocker": BLOCKER,
        "frame": "glTF +Y up, front +Z, centred on X/Z, lowest point y = 0, metres",
        "dimensions_m": {"x": report["size_xyz_m"][0], "y": report["size_xyz_m"][1], "z": report["size_xyz_m"][2]},
        "opening_m": {"clear_width_between_posts_at_base": round(2 * (POST["base_x"] - POST["half_side"]), 3),
                      "clear_height_to_cap_underside": CAP["underside_y"]},
        "triangles": report["triangles"],
        "triangle_budget": 40000,
        "parts": {"timber_members": len(timber_objs), "rock_pieces": len(rock_objs), "triangles_per_part": part_tris},
        "detached_parts": detached,
        "non_positive_volume_parts": inverted,
        "materials": [
            {"name": f"MAT_{ASSET_ID}_timber", "kind": "baked procedural atlas (wood, iron, rope, spoil)",
             "maps": ["basecolor sRGB", "normal OpenGL tangent", "ORM (R occlusion, G roughness, B metallic)"],
             "resolution": args.atlas, "uv": pack, "bake": bake_stats},
            {"name": f"MAT_{ASSET_ID}_rock", "kind": "baked procedural stone atlas (granite, cracks, stain, lichen, dust)",
             "maps": ["basecolor sRGB", "normal OpenGL tangent", "ORM (R occlusion, G roughness, B metallic)"],
             "resolution": args.atlas, "uv": rock_uv, "bake": rock_bake, "ao_distance_m": args.rock_ao_distance},
        ],
        "parameters": {"post": POST, "cap": CAP, "lintel": LINTEL, "stubs": STUBS, "props": PROPS, "rope": ROPE,
                       "plank": PLANK, "pegs": PEGS, "boards": BOARDS, "loose_boards": LOOSE_BOARDS, "slabs": SLABS, "spill": SPILL,
                       "rock_variants_tris": ROCK_VARIANTS, "rock_generator": ROCK_GEN, "rocks": rock_records, "pebbles": PEBBLES,
                       "kind_density": KIND_DENSITY, "atlas": args.atlas, "pad_px": args.pad,
                       "bake_samples": args.bake_samples, "ao_samples": args.ao_samples, "ao_distance_m": args.ao_distance,
                       "normals_sharp_angle_deg": {"timber": 40.0, "rock": 55.0}},
        "validation": report,
        "seconds": round(time.time() - t_start, 1),
    }
    prov_path = os.path.join(args.out, f"{ASSET_ID}_provenance.json")
    with open(prov_path, "w", encoding="utf-8") as f:
        json.dump(prov, f, indent=2, default=lambda o: list(o) if hasattr(o, "__iter__") else str(o))
    print(json.dumps({"triangles": report["triangles"], "size": report["size_xyz_m"], "fitting": report["fitting"],
                      "detached": detached, "uv": pack}, indent=1))
    print("PROCGEN_RESULT", prov_path)


SOCKETS = {}  # bake material name -> its channel sockets (ID properties cannot hold sockets)


if __name__ == "__main__":
    main()
