"""Dimension-driven building generator: walls, openings, framing, gables, roof, floor, chimney.

Buildings are modular parametric assemblies built from exact dimensions, never image-to-3D. This
generator takes a building's outer size, wall thickness and height, its door (wall, position,
width, height), roof pitch and overhangs, materials and style, and emits every member as its own
closed convex solid:

  * continuous wall runs split at each opening, with a lintel over every opening and, in a door
    gap, a frame (two jambs and a head) filling the gap around a 1.03 m leaf. The game hangs
    prop_iron_banded_oak_door in that gap, so the model carries no leaf;
  * style "timber_frame": stone plinth, sill beams, posts, mid rails, braces, wall plates and tie
    beams with recessed plaster infill; style "stone": ashlar courses 0.30 m tall laid to the
    texture's own bed joints, bonded alternately at the corners, with a timber wall plate;
  * gable infill (plaster or vertical boards) with rakers, king post and struts; a wedge wall plate
    on the eaves walls, so the roof sits on the wall with no daylight between;
  * a ridge beam, purlins and rafters, and two roof slopes mitred at the ridge with eave and gable
    overhangs, a ridge cap and barge boards; a floor; a chimney where the building has a hearth.

Frame and orientation. Every model is authored in the game's world frame, translated so its origin
is the footprint centre at ground level: glTF +X = world +X (east), +Y up, +Z = world +Z (the
region's z, north). A building is therefore placed at its world centre with NO rotation. The
lodge's door is on its +X side (east); the forge's door is on its -X side (west). Blender authors
Z-up, so a frame point (x, y, z) is written to Blender as (x, -z, y) and the Y-up export maps it
back.

Walls coincide with the world blockers: a wall's outer and inner faces are the blocker's faces
(members recessed by at most 0.03 m, never proud), so collision matches what is drawn. The build
checks this by sampling both on a 5 cm grid and writes the result into the provenance JSON.

Each building is one GLB: an empty root named after the asset, a mesh node 'structure', a mesh
node 'roof' (slopes, ridge, rafters, purlins, barge boards - hide it when the player is inside) and,
for the forge, a mesh node 'chimney' (hood and stack). Materials are slots named
MAT_<asset>_<surface> with the baked world materials embedded, resampled periodically to ~512 px
per metre. UVs are projected per face in each member's own frame at the material's real tile size:
walls on a shared world frame so courses and plaster run on across pieces, timbers along their
grain and centred on one board of the hewn-oak texture.

Gates: every mesh passes _blender_build_kit.geometry_gate (no bow-tie, inward-facing, zero-area or
UV-degenerate triangles) before export, and the exported GLB passes glb_gate.

Usage (Blender 5.2):
  blender --background --factory-startup --python _procgen_building.py -- --stage bake-materials
  blender --background --factory-startup --python _procgen_building.py -- \
      [--asset longhouse forge_shed building_fence_panel building_well] [--out-root DIR]

Writes assets/_staging/buildings/<id>/<id>.glb and <id>_provenance.json; shared resampled textures
go to assets/_staging/buildings/_textures/, variant materials to assets/_staging/buildings/_materials/.
Refuses any output under assets/ready.
"""
import argparse
import hashlib
import json
import math
import os
import random
import re
import sys
import time

import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(HERE)
import _blender_build_kit as kit  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ASSETS = os.path.join(REPO, "assets")
STAGING = os.path.join(ASSETS, "_staging", "buildings")
BAKED = os.path.join(ASSETS, "_staging", "materials")
VARIANTS = os.path.join(STAGING, "_materials")
TEXTURES = os.path.join(STAGING, "_textures")
REGION = os.path.join(REPO, "content", "regions", "ashen_hollow.yaml")
PX_PER_M = 512
MODULAR_INTERFACE_VERSION = "1.0"

# Ashlar bed joints: the shader lays 8 courses per 2.4 m tile with rv = V*8 + 0.41, so a joint sits
# at V = (k - 0.41)/8. Offsetting v by 0.59/8 puts joints at y = 0, 0.3, 0.6 ... in metres.
ASHLAR_COURSE_M = 0.30
ASHLAR_V0 = 0.59 / 8.0
# Hewn oak: 6 faces per 2.4 m tile, U*6 + 0.37 -> face k is centred at U = (k + 0.13) / 6.
HEWN_FACES = 6
# Slate: 12 courses per tile, rv = V*12 + 0.43; start the eave just above a course tail.
SLATE_V0 = (0.06 - 0.43) / 12.0

SURFACES = {
    "timber": "material_hewn_oak_timber",
    "boards": "material_oak_plank_floor",
    "plaster": "material_plaster_lath_wall",
    "stone": "material_limestone_ashlar",
    "rubble": "material_rubble_stone_wall",
    "roof": "material_slate_roof_scale",
    "iron": "material_forged_iron",
}


# --------------------------------------------------------------------------------------------
# World truth
# --------------------------------------------------------------------------------------------

def read_region(path):
    """The blocker and door rows of a region YAML (flow-mapping lines; no PyYAML in Blender)."""
    boxes, circles, doors = {}, {}, {}
    pattern = re.compile(r"\{\s*(?:id|key):\s*([\w.]+),.*?(box_m|circle_m):\s*\[([^\]]+)\]"
                         r".*?height_m:\s*([\d.]+)")
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            match = pattern.search(line)
            if not match:
                continue
            name, shape, numbers, height = match.groups()
            values = [float(v) for v in numbers.split(",")]
            row = {"values": values, "height": float(height)}
            if name.startswith("door."):
                doors[name] = row
            elif shape == "box_m":
                boxes[name] = row
            else:
                circles[name] = row
    return boxes, circles, doors


# --------------------------------------------------------------------------------------------
# Geometry: closed convex solids in the frame (x east, y up, z north)
# --------------------------------------------------------------------------------------------

HEXA_FACES = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
UP = Vector((0.0, 1.0, 0.0))
AX = Vector((1.0, 0.0, 0.0))
AZ = Vector((0.0, 0.0, 1.0))


def newell(points):
    n = Vector((0.0, 0.0, 0.0))
    for i, p in enumerate(points):
        q = points[(i + 1) % len(points)]
        n.x += (p.y - q.y) * (p.z + q.z)
        n.y += (p.z - q.z) * (p.x + q.x)
        n.z += (p.x - q.x) * (p.y + q.y)
    return n


class UV:
    """How a solid's faces are projected: tile size, origin, grain (texture +v) and fallback."""

    def __init__(self, tile, grain=UP, alt=AX, origin=(0.0, 0.0, 0.0), u0=0.0, v0=0.0,
                 board=None):
        self.tile, self.grain, self.alt = tile, Vector(grain).normalized(), Vector(alt).normalized()
        self.origin, self.u0, self.v0, self.board = Vector(origin), u0, v0, board

    def face(self, points, normal):
        g = self.grain - normal * self.grain.dot(normal)
        if g.length < 0.35:
            g = self.alt - normal * self.alt.dot(normal)
        if g.length < 0.35:
            g = normal.orthogonal()
        g.normalize()
        u_dir = g.cross(normal)
        pu = [(p - self.origin).dot(u_dir) for p in points]
        pv = [(p - self.origin).dot(g) for p in points]
        if self.board is not None:
            centre = sum(pu) / len(pu)
            us = [(x - centre) / self.tile + self.board for x in pu]
        else:
            us = [x / self.tile + self.u0 for x in pu]
        return [(u, v / self.tile + self.v0) for u, v in zip(us, pv)]


class Solid:
    def __init__(self, verts, faces, surface, uv, tag):
        self.verts = [Vector(v) for v in verts]
        self.faces = [tuple(f) for f in faces]
        volume = 0.0
        for f in self.faces:
            a = self.verts[f[0]]
            for i in range(1, len(f) - 1):
                volume += a.dot(self.verts[f[i]].cross(self.verts[f[i + 1]]))
        if volume < 0.0:
            self.faces = [tuple(reversed(f)) for f in self.faces]
        self.volume = abs(volume) / 6.0
        self.surface, self.uv, self.tag = surface, uv, tag
        self.planes = []
        for f in self.faces:
            n = newell([self.verts[i] for i in f]).normalized()
            self.planes.append((n, n.dot(self.verts[f[0]])))
        self.lo = Vector([min(v[i] for v in self.verts) for i in range(3)])
        self.hi = Vector([max(v[i] for v in self.verts) for i in range(3)])


class Model:
    """Solids grouped into named nodes, each node one mesh with one slot per surface."""

    def __init__(self, asset_id, surfaces):
        self.asset_id = asset_id
        self.surfaces = surfaces              # surface -> material id
        self.nodes = {}
        self.rng = random.Random(asset_id)

    def add(self, node, verts, faces, surface, uv, tag="detail"):
        if len(verts) < 4:
            raise ValueError("a solid needs at least 4 vertices")
        solid = Solid(verts, faces, surface, uv, tag)
        if solid.volume < 1e-9:
            raise ValueError(f"zero-volume solid in {node} ({surface}, {tag})")
        self.nodes.setdefault(node, []).append(solid)
        return solid

    # -- primitives ------------------------------------------------------------------------

    def box(self, node, lo, hi, surface, uv, tag="detail"):
        lo, hi = Vector(lo), Vector(hi)
        if min(hi - lo) <= 1e-6:
            raise ValueError(f"empty box {tuple(lo)}..{tuple(hi)} in {node}/{tag}")
        corners = [Vector((hi.x if i & 1 else lo.x, hi.y if i & 2 else lo.y,
                           hi.z if i & 4 else lo.z)) for i in range(8)]
        return self.add(node, corners, HEXA_FACES, surface, uv, tag)

    def hexa(self, node, corners, surface, uv, tag="detail"):
        return self.add(node, corners, HEXA_FACES, surface, uv, tag)

    def loft(self, node, bottom, top, surface, uv, tag="detail"):
        """Convex frustum between two convex polygons with matching vertex order."""
        n = len(bottom)
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces += [(i, (i + 1) % n, n + (i + 1) % n, n + i) for i in range(n)]
        return self.add(node, list(bottom) + list(top), faces, surface, uv, tag)

    def prism(self, node, polygon, extrude, surface, uv, tag="detail"):
        extrude = Vector(extrude)
        return self.loft(node, [Vector(p) for p in polygon],
                         [Vector(p) + extrude for p in polygon], surface, uv, tag)

    def beam(self, node, p0, p1, side, width, depth, surface, uv=None, tag="timber", extend=0.0):
        """Oriented box on the centreline p0->p1; `side` fixes the width direction."""
        p0, p1 = Vector(p0), Vector(p1)
        d = (p1 - p0).normalized()
        p0, p1 = p0 - d * extend, p1 + d * extend
        s = Vector(side) - d * Vector(side).dot(d)
        s.normalize()
        w = d.cross(s)
        corners = []
        for i in range(8):
            base = p1 if i & 1 else p0
            corners.append(base + s * (width / 2 if i & 2 else -width / 2)
                           + w * (depth / 2 if i & 4 else -depth / 2))
        return self.hexa(node, corners, surface, uv or self.timber_uv(d, s), tag)

    def cylinder(self, node, base, axis, radius, length, sides, surface, uv, tag="detail",
                 radius_top=None, phase=0.0):
        axis = Vector(axis).normalized()
        a = axis.orthogonal().normalized()
        b = axis.cross(a)
        rt = radius if radius_top is None else radius_top
        ring = [a * math.cos(phase + 2 * math.pi * i / sides)
                + b * math.sin(phase + 2 * math.pi * i / sides) for i in range(sides)]
        base = Vector(base)
        return self.loft(node, [base + r * radius for r in ring],
                         [base + axis * length + r * rt for r in ring], surface, uv, tag)

    def annulus(self, node, centre, r_in, r_out, y0, y1, segments, surface, uv_fn, tag,
                phase=0.0, gap=0.0):
        """A ring of convex sectors; `gap` (radians) leaves a joint between sectors."""
        solids = []
        for i in range(segments):
            a0 = phase + 2 * math.pi * i / segments + gap / 2
            a1 = phase + 2 * math.pi * (i + 1) / segments - gap / 2
            pts = []
            for k in range(8):
                ang = a1 if k & 1 else a0
                r = r_out if k & 2 else r_in
                y = y1 if k & 4 else y0
                pts.append(Vector((centre[0] + r * math.cos(ang), y,
                                   centre[1] + r * math.sin(ang))))
            solids.append(self.hexa(node, pts, surface, uv_fn(i, (a0 + a1) / 2), tag))
        return solids

    # -- UV presets ----------------------------------------------------------------------------

    def tile(self, surface):
        return MATERIAL_TILES[self.surfaces[surface]]

    def wall_uv(self, surface, run_axis):
        v0 = ASHLAR_V0 if self.surfaces[surface] == SURFACES["stone"] else 0.0
        return UV(self.tile(surface), grain=UP, alt=run_axis, v0=v0)

    def timber_uv(self, along, side=None):
        face = self.rng.randrange(HEWN_FACES)
        return UV(self.tile("timber"), grain=along, alt=side if side is not None else UP,
                  origin=(0, 0, 0), v0=self.rng.random(),
                  board=(face + 0.13) / HEWN_FACES)


MATERIAL_TILES = {}


def material_record(material_id):
    for root in (VARIANTS, BAKED):
        path = os.path.join(root, material_id, f"{material_id}_material.json")
        if os.path.exists(path):
            with open(path, encoding="utf-8") as handle:
                return os.path.dirname(path), json.load(handle)
    raise SystemExit(f"material {material_id} has no baked record; run --stage bake-materials")


# --------------------------------------------------------------------------------------------
# Walls
# --------------------------------------------------------------------------------------------

class Wall:
    """One wall run. run axis 'x' (north/south walls) or 'z' (east/west); c0..c1 across it."""

    def __init__(self, name, axis, r_full, r_inner, c0, c1, outer_sign, gable=False):
        self.name, self.axis = name, axis
        self.r_full, self.r_inner = r_full, r_inner
        self.c0, self.c1, self.outer_sign, self.gable = c0, c1, outer_sign, gable
        self.openings = []

    @property
    def run_vec(self):
        return AX if self.axis == "x" else AZ

    @property
    def across_vec(self):
        return AZ if self.axis == "x" else AX

    def lo_hi(self, s0, s1, y0, y1, inset=0.0):
        c0, c1 = self.c0 + inset, self.c1 - inset
        if self.axis == "x":
            return (s0, y0, c0), (s1, y1, c1)
        return (c0, y0, s0), (c1, y1, s1)

    def point(self, s, y, c):
        return Vector((s, y, c)) if self.axis == "x" else Vector((c, y, s))


def split(span, holes):
    """Subtract sorted intervals `holes` from span=(a, b)."""
    a, b = span
    out = []
    for h0, h1 in sorted(holes):
        if h1 <= a or h0 >= b:
            continue
        if h0 > a + 1e-6:
            out.append((a, min(h0, b)))
        a = max(a, h1)
    if b > a + 1e-6:
        out.append((a, b))
    return out


def make_walls(spec):
    X, Z, t = spec["size_x"] / 2, spec["size_z"] / 2, spec["t"]
    walls = {
        "south": Wall("south", "x", (-X, X), (-X + t, X - t), -Z, -Z + t, -1),
        "north": Wall("north", "x", (-X, X), (-X + t, X - t), Z - t, Z, +1),
        "west": Wall("west", "z", (-Z, Z), (-Z + t, Z - t), -X, -X + t, -1, gable=True),
        "east": Wall("east", "z", (-Z, Z), (-Z + t, Z - t), X - t, X, +1, gable=True),
    }
    for opening in spec["openings"]:
        wall = walls[opening["wall"]]
        half = opening["width"] / 2
        wall.openings.append(dict(opening, s0=opening["centre"] - half,
                                  s1=opening["centre"] + half))
    return walls


def door_frame(model, wall, o, surface="timber"):
    """Jambs and head filling a door gap around the leaf's clear opening; set back 0.03 m."""
    clear_w, clear_h = o["clear"]
    jamb = (o["width"] - clear_w) / 2
    inset = 0.03
    side = wall.across_vec
    lo, hi = wall.lo_hi(o["s0"], o["s0"] + jamb, 0.0, o["y1"], inset)
    model.box("structure", lo, hi, surface, model.timber_uv(UP, side), "door_frame")
    lo, hi = wall.lo_hi(o["s1"] - jamb, o["s1"], 0.0, o["y1"], inset)
    model.box("structure", lo, hi, surface, model.timber_uv(UP, side), "door_frame")
    lo, hi = wall.lo_hi(o["s0"] + jamb, o["s1"] - jamb, clear_h, o["y1"], inset)
    model.box("structure", lo, hi, surface, model.timber_uv(wall.run_vec, side), "door_frame")
    # Threshold board across the full gap, flush with the floor.
    lo, hi = wall.lo_hi(o["s0"] + jamb, o["s1"] - jamb, 0.0, 0.04, inset)
    model.box("structure", lo, hi, surface, model.timber_uv(wall.run_vec, side), "threshold")


def shutter(model, wall, o):
    """Closed plank shutters in a window, 0.10 m in from the outer face: nothing to see through,
    so the solid blocker behind a window is what the eye expects."""
    t = wall.c1 - wall.c0
    outer_inset, thick = 0.10, 0.05
    if wall.outer_sign > 0:
        c0, c1 = wall.c1 - outer_inset - thick, wall.c1 - outer_inset
    else:
        c0, c1 = wall.c0 + outer_inset, wall.c0 + outer_inset + thick
    assert thick + outer_inset < t
    if wall.axis == "x":
        lo, hi = (o["s0"], o["y0"], c0), (o["s1"], o["y1"], c1)
    else:
        lo, hi = (c0, o["y0"], o["s0"]), (c1, o["y1"], o["s1"])
    model.box("structure", lo, hi, "boards", UV(model.tile("boards"), grain=UP,
                                                 alt=wall.run_vec, u0=model.rng.random()),
              "shutter")


def timber_frame_walls(model, spec, walls):
    t, h = spec["t"], spec["h"]
    plinth_h, sill_h, plate_h, pw = 0.30, 0.20, 0.20, 0.22
    sill_top, plate_bot = plinth_h + sill_h, h - plate_h
    rail = (1.35, 1.50)
    X, Z = spec["size_x"] / 2, spec["size_z"] / 2

    # Corner posts, t x t, from plinth to plate.
    for sx in (-1, 1):
        for sz in (-1, 1):
            x0, x1 = sorted((sx * X, sx * (X - t)))
            z0, z1 = sorted((sz * Z, sz * (Z - t)))
            model.box("structure", (x0, plinth_h, z0), (x1, plate_bot, z1), "timber",
                      model.timber_uv(UP, AX), "wall")

    for wall in walls.values():
        doors = [o for o in wall.openings if o["kind"] == "door"]
        run_uv = model.wall_uv("stone", wall.run_vec)
        full = wall.r_full if wall.axis == "x" else wall.r_inner
        # Plinth course (stone), sill beams, plates.
        for a, b in split(full, [(o["s0"], o["s1"]) for o in doors]):
            model.box("structure", *wall.lo_hi(a, b, 0.0, plinth_h), "stone", run_uv, "wall")
        for a, b in split(wall.r_inner, [(o["s0"], o["s1"]) for o in doors]):
            model.box("structure", *wall.lo_hi(a, b, plinth_h, sill_top), "timber",
                      model.timber_uv(wall.run_vec, UP), "wall")
        model.box("structure", *wall.lo_hi(*full, plate_bot, h), "timber",
                  model.timber_uv(wall.run_vec, UP), "wall")

        # Posts: flanking every opening, then intermediates so no bay exceeds max_bay.
        posts = []
        for o in wall.openings:
            posts += [(o["s0"] - pw, o["s0"]), (o["s1"], o["s1"] + pw)]
        r0, r1 = wall.r_inner
        taken = sorted(posts + [(o["s0"], o["s1"]) for o in wall.openings])
        for a, b in split((r0, r1), taken):
            bays = max(1, math.ceil((b - a) / spec.get("max_bay", 2.0)))
            for i in range(1, bays):
                c = a + (b - a) * i / bays
                posts.append((c - pw / 2, c + pw / 2))
        posts.sort()
        for a, b in posts:
            model.box("structure", *wall.lo_hi(a, b, sill_top, plate_bot), "timber",
                      model.timber_uv(UP, wall.run_vec), "wall")

        # Bays between posts: openings get their sill/lintel and infill, free bays plaster.
        edges = [(r0, r0)] + posts + [(r1, r1)]
        free = []
        for (_, a), (b, _) in zip(edges[:-1], edges[1:]):
            if b - a <= 1e-6:
                continue
            opening = next((o for o in wall.openings if abs(o["s0"] - a) < 1e-6
                            and abs(o["s1"] - b) < 1e-6), None)
            if opening is None:
                free.append((a, b))
                continue
            y0, y1 = opening["y0"], opening["y1"]
            lintel_top = y1 + 0.22
            model.box("structure", *wall.lo_hi(a, b, y1, lintel_top), "timber",
                      model.timber_uv(wall.run_vec, UP), "lintel")
            if plate_bot - lintel_top > 0.02:
                model.box("structure", *wall.lo_hi(a, b, lintel_top, plate_bot, 0.03),
                          "plaster", model.wall_uv("plaster", wall.run_vec), "wall")
            if opening["kind"] == "door":
                door_frame(model, wall, opening)
            else:
                model.box("structure", *wall.lo_hi(a, b, y0 - 0.15, y0), "timber",
                          model.timber_uv(wall.run_vec, UP), "window_sill")
                if y0 - 0.15 - sill_top > 0.02:
                    model.box("structure", *wall.lo_hi(a, b, sill_top, y0 - 0.15, 0.03),
                              "plaster", model.wall_uv("plaster", wall.run_vec), "wall")
                shutter(model, wall, opening)
        for index, (a, b) in enumerate(free):
            model.box("structure", *wall.lo_hi(a, b, sill_top, plate_bot, 0.03), "plaster",
                      model.wall_uv("plaster", wall.run_vec), "wall")
            end = (index == 0 and abs(a - r0) < 1e-6) or (index == len(free) - 1
                                                          and abs(b - r1) < 1e-6)
            if end and b - a >= 0.9:
                # A tension brace from the corner post's head down to the sill.
                corner = a if abs(a - r0) < 1e-6 else b
                away = b if corner == a else a
                reach = min(b - a - 0.12, 1.5)
                foot = corner + math.copysign(reach, away - corner)
                cm = (wall.c0 + wall.c1) / 2
                model.beam("structure", wall.point(corner, plate_bot, cm),
                           wall.point(foot, sill_top, cm), wall.across_vec.cross(Vector(
                               wall.point(foot, sill_top, cm) - wall.point(corner, plate_bot, cm))
                               .normalized()), 0.16, t - 0.03, "timber", tag="wall", extend=0.12)
            else:
                model.box("structure", *wall.lo_hi(a, b, *rail), "timber",
                          model.timber_uv(wall.run_vec, UP), "wall")

    # Tie beams across the hall at plate level.
    for x in spec.get("tie_beams", []):
        model.box("structure", (x - 0.11, plate_bot, -Z + t), (x + 0.11, h, Z - t), "timber",
                  model.timber_uv(AZ, UP), "tie_beam")


def stone_walls(model, spec, walls):
    h, t = spec["h"], spec["t"]
    courses = round(h / ASHLAR_COURSE_M)
    if abs(courses * ASHLAR_COURSE_M - h) > 1e-6:
        raise SystemExit(f"stone wall height {h} is not a whole number of {ASHLAR_COURSE_M} m courses")
    lintel_bearing = 0.30
    for wall in walls.values():
        for o in wall.openings:
            for y in (o["y0"], o["y1"]):
                if abs(y / ASHLAR_COURSE_M - round(y / ASHLAR_COURSE_M)) > 1e-6:
                    raise SystemExit(f"{wall.name} opening edge {y} is off the course lines")
        for k in range(courses):
            y0, y1 = k * ASHLAR_COURSE_M, (k + 1) * ASHLAR_COURSE_M
            # Corners bond alternately: even courses run the north/south walls through.
            through = (wall.axis == "x") == (k % 2 == 0)
            span = wall.r_full if through else wall.r_inner
            holes = []
            for o in wall.openings:
                if y0 < o["y1"] - 1e-6 and y1 > o["y0"] + 1e-6:
                    holes.append((o["s0"], o["s1"]))
                elif abs(y0 - o["y1"]) < 1e-6:
                    holes.append((o["s0"] - lintel_bearing, o["s1"] + lintel_bearing))
            inset = (0.0, 0.004, 0.008)[model.rng.randrange(3)]
            for a, b in split(span, holes):
                model.box("structure", *wall.lo_hi(a, b, y0, y1, inset), "stone",
                          model.wall_uv("stone", wall.run_vec), "wall")
        for o in wall.openings:
            model.box("structure", *wall.lo_hi(o["s0"] - lintel_bearing, o["s1"] + lintel_bearing,
                                               o["y1"], o["y1"] + ASHLAR_COURSE_M),
                      "timber", model.timber_uv(wall.run_vec, UP), "lintel")
            if o["kind"] == "door":
                door_frame(model, wall, o)
            else:
                shutter(model, wall, o)
        # Timber wall plate on the masonry.
        full = wall.r_full if wall.axis == "x" else wall.r_inner
        model.box("structure", *wall.lo_hi(*full, h, h + 0.20), "timber",
                  model.timber_uv(wall.run_vec, UP), "plate")


# --------------------------------------------------------------------------------------------
# Gables and roof
# --------------------------------------------------------------------------------------------

def roof_and_gables(model, spec, walls):
    X, Z, t = spec["size_x"] / 2, spec["size_z"] / 2, spec["t"]
    roof = spec["roof"]
    he = spec["eave_h"]
    tp = math.tan(math.radians(roof["pitch"]))
    cp = math.cos(math.radians(roof["pitch"]))
    eo, go, T = roof["eave"], roof["gable"], roof["thick"]
    Tv = T / cp
    over = 0.02                                   # members reach this far into the roof slab

    def yu(z):
        return he + (Z - abs(z)) * tp

    apex = yu(0.0)
    gable_surface = spec["gable_surface"]

    # Eaves wedge plates: fill from the plate top to the roof's underside, between the gables.
    for sign in (-1, 1):
        zo, zi = sign * Z, sign * (Z - t)
        poly = [Vector((-X + t, he, zo)), Vector((-X + t, he + over, zo)),
                Vector((-X + t, yu(zi) + over, zi)), Vector((-X + t, he, zi))]
        model.prism("structure", poly, (2 * (X - t), 0, 0), "timber",
                    model.timber_uv(AX, UP), "wall_plate_wedge")

    # Gables: infill pentagon, rakers mitred at the ridge, king post and two struts.
    for name in ("west", "east"):
        wall = walls[name]
        x0, x1 = wall.c0, wall.c1
        poly = [Vector((x0 + 0.03, he, -Z)), Vector((x0 + 0.03, he, Z)),
                Vector((x0 + 0.03, he + over, Z)), Vector((x0 + 0.03, apex + over, 0.0)),
                Vector((x0 + 0.03, he + over, -Z))]
        model.prism("structure", poly, (x1 - x0 - 0.06, 0, 0), gable_surface,
                    UV(model.tile(gable_surface), grain=UP, alt=AZ, u0=model.rng.random()),
                    "gable")
        rake_depth = 0.24 / cp
        for sign in (-1, 1):
            corners = []
            for i in range(8):
                z = sign * Z if i & 1 else 0.0
                top = yu(z) + over
                y = top if i & 2 else top - rake_depth
                x = (x1 - 0.01) if i & 4 else (x0 + 0.01)
                corners.append(Vector((x, y, z)))
            slope = Vector((0.0, -tp, sign)).normalized()
            model.hexa("structure", corners, "timber", model.timber_uv(slope, AX), "raker")
        model.box("structure", (x0 + 0.015, he, -0.11), (x1 - 0.015, apex - rake_depth + 0.05, 0.11),
                  "timber", model.timber_uv(UP, AZ), "king_post")
        for sign in (-1, 1):
            zc = sign * Z * 0.5
            model.box("structure", (x0 + 0.015, he, zc - 0.08),
                      (x1 - 0.015, yu(zc) - rake_depth + 0.05, zc + 0.08),
                      "timber", model.timber_uv(UP, AZ), "strut")

    # Roof slopes, mitred at z = 0, with eave and gable overhangs.
    xa, xb = -X - go, X + go
    ze = Z + eo
    for sign in (-1, 1):
        corners = []
        for i in range(8):
            x = xb if i & 1 else xa
            z = sign * ze if i & 2 else 0.0
            y = yu(z) + (Tv if i & 4 else 0.0)
            corners.append(Vector((x, y, z)))
        upslope = Vector((0.0, tp, -sign)).normalized()
        eave_top = Vector((xa, yu(sign * ze) + Tv, sign * ze))
        model.hexa("roof", corners, "roof",
                   UV(model.tile("roof"), grain=upslope, alt=AX, origin=eave_top,
                      u0=model.rng.random(), v0=SLATE_V0), "roof_slope")

    # Ridge cap along the apex, and the ridge beam beneath it.
    cap = 0.17
    poly = [Vector((xa - 0.05, yu(cap) + Tv - 0.015, -cap)),
            Vector((xa - 0.05, yu(cap) + Tv - 0.015, cap)),
            Vector((xa - 0.05, apex + Tv + 0.07, 0.0))]
    model.prism("roof", poly, (xb - xa + 0.10, 0, 0), "timber", model.timber_uv(AX, UP),
                "ridge_cap")
    rb = 0.13
    poly = [Vector((xa - 0.10, apex - 0.34, -rb)), Vector((xa - 0.10, apex - 0.34, rb)),
            Vector((xa - 0.10, yu(rb) + over, rb)), Vector((xa - 0.10, apex + over, 0.0)),
            Vector((xa - 0.10, yu(rb) + over, -rb))]
    model.prism("roof", poly, (xb - xa + 0.20, 0, 0), "timber", model.timber_uv(AX, UP),
                "ridge_beam")

    chim = spec.get("chimney")
    # Purlins at mid-slope, and rafters every ~0.9 m between the gables with tails at the eaves.
    for sign in (-1, 1):
        zp = sign * Z * 0.6
        hw = 0.10
        z_in, z_out = zp - sign * hw, zp + sign * hw
        poly = [Vector((xa - 0.05, yu(z_in) - 0.22, z_in)), Vector((xa - 0.05, yu(z_out) - 0.22, z_out)),
                Vector((xa - 0.05, yu(z_out) + over, z_out)), Vector((xa - 0.05, yu(z_in) + over, z_in))]
        model.prism("roof", poly, (xb - xa + 0.10, 0, 0), "timber", model.timber_uv(AX, UP), "purlin")
    count = max(2, round((2 * (X - t) - 0.4) / 0.9))
    depth = 0.16 / cp
    for i in range(count + 1):
        x = -X + t + 0.2 + (2 * (X - t) - 0.4) * i / count
        for sign in (-1, 1):
            if chim and sign * chim["z"] > 0 and abs(x - chim["x"]) < chim["size"] / 2 + 0.12:
                continue
            corners = []
            for k in range(8):
                xx = x + (0.06 if k & 1 else -0.06)
                z = sign * ze if k & 2 else 0.0
                top = yu(z) + over
                y = top if k & 4 else top - depth
                corners.append(Vector((xx, y, z)))
            model.hexa("roof", corners, "timber",
                       model.timber_uv(Vector((0.0, -tp, sign)).normalized(), AX), "rafter")

    # Barge boards cover the slab ends at both gables.
    for xs in (-1, 1):
        x0 = xb if xs > 0 else xa - 0.04
        for sign in (-1, 1):
            corners = []
            for k in range(8):
                x = x0 + (0.04 if k & 1 else 0.0)
                z = sign * ze if k & 2 else 0.0
                y = (yu(z) + Tv + 0.03) if k & 4 else (yu(z) - 0.20)
                corners.append(Vector((x, y, z)))
            model.hexa("roof", corners, "timber",
                       model.timber_uv(Vector((0.0, -tp, sign)).normalized(), AX), "barge_board")
    return {"eave_h": he, "ridge_underside_h": apex, "ridge_top_h": apex + Tv + 0.07,
            "pitch_deg": roof["pitch"], "roof_thickness": T}


def chimney(model, spec, roof_info):
    c = spec["chimney"]
    x, z, s = c["x"], c["z"], c["size"]
    hb, hood_w = c["hood_bottom"], c["hood_width"]
    top = roof_info["ridge_top_h"] + c["above_ridge"]
    hood_top = hb + 0.6
    sq = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    model.loft("chimney", [Vector((x + a * hood_w / 2, hb, z + b * hood_w / 2)) for a, b in sq],
               [Vector((x + a * s / 2, hood_top, z + b * s / 2)) for a, b in sq], "iron",
               UV(model.tile("iron"), grain=UP, alt=AX), "hood")
    model.box("chimney", (x - s / 2, hood_top, z - s / 2), (x + s / 2, top, z + s / 2), "rubble",
              UV(model.tile("rubble"), grain=UP, alt=AX), "stack")
    model.box("chimney", (x - s / 2 - 0.06, top, z - s / 2 - 0.06),
              (x + s / 2 + 0.06, top + 0.15, z + s / 2 + 0.06), "stone",
              UV(model.tile("stone"), grain=UP, alt=AX, v0=ASHLAR_V0), "cap")
    return {"stack_top_h": top + 0.15, "hood_bottom_h": hb}


def floor(model, spec):
    X, Z, t = spec["size_x"] / 2, spec["size_z"] / 2, spec["t"]
    surface = spec["floor_surface"]
    # Floor sits on the ground (0.00..0.03): nothing below grade, so the model's base is its footprint.
    model.box("structure", (-X + t, 0.0, -Z + t), (X - t, 0.03, Z - t), surface,
              UV(model.tile(surface), grain=AX, alt=AZ,
                 v0=ASHLAR_V0 if SURFACES[surface] == SURFACES["stone"] else 0.0), "floor")


# --------------------------------------------------------------------------------------------
# Buildings
# --------------------------------------------------------------------------------------------

def building_spec(asset_id, boxes, doors):
    """Derive the generator parameters from the world blockers, so the model IS the blockers."""
    if asset_id == "longhouse":
        prefix, door_key = "longhouse_", "door.longhouse"
    else:
        prefix, door_key = "forge_", "door.forge_shed"
    walls = {k: v for k, v in boxes.items() if k.startswith(prefix)}
    x0 = min(v["values"][0] for v in walls.values())
    z0 = min(v["values"][1] for v in walls.values())
    x1 = max(v["values"][2] for v in walls.values())
    z1 = max(v["values"][3] for v in walls.values())
    heights = {v["height"] for v in walls.values()}
    if len(heights) != 1:
        raise SystemExit(f"{asset_id}: walls disagree on height {heights}")
    thick = {round(min(v["values"][2] - v["values"][0], v["values"][3] - v["values"][1]), 4)
             for v in walls.values()}
    if len(thick) != 1:
        raise SystemExit(f"{asset_id}: walls disagree on thickness {thick}")
    t, h = thick.pop(), heights.pop()
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    dx0, dz0, dx1, dz1 = doors[door_key]["values"]
    if abs(dx1 - x1) < 1e-6 and abs(dx0 - (x1 - t)) < 1e-6:
        wall, centre = "east", (dz0 + dz1) / 2 - cz
        width = dz1 - dz0
    elif abs(dx0 - x0) < 1e-6 and abs(dx1 - (x0 + t)) < 1e-6:
        wall, centre = "west", (dz0 + dz1) / 2 - cz
        width = dz1 - dz0
    elif abs(dz0 - z0) < 1e-6:
        wall, centre = "south", (dx0 + dx1) / 2 - cx
        width = dx1 - dx0
    else:
        wall, centre = "north", (dx0 + dx1) / 2 - cx
        width = dx1 - dx0
    door_h = doors[door_key]["height"]
    spec = {
        "asset_id": asset_id,
        "world_centre": [cx, cz],
        "world_box": [x0, z0, x1, z1],
        "size_x": x1 - x0, "size_z": z1 - z0, "t": t, "h": h,
        "blockers": {k: v for k, v in walls.items()},
        "door_box": {"key": door_key, **doors[door_key]},
        "openings": [{"kind": "door", "wall": wall, "centre": centre, "width": width,
                      "y0": 0.0, "y1": door_h, "clear": (1.05, 2.10), "leaf": (1.033, 2.05)}],
    }
    if asset_id == "longhouse":
        spec.update(style="timber_frame", eave_h=spec["h"], floor_surface="boards",
                    gable_surface="plaster", tie_beams=[-4.0, 0.0, 4.0], max_bay=2.0,
                    roof={"pitch": 40.0, "eave": 0.45, "gable": 0.35, "thick": 0.12})
        for wall_name in ("north", "south"):
            for c in (-3.6, 3.6):
                spec["openings"].append({"kind": "window", "wall": wall_name, "centre": c,
                                         "width": 0.9, "y0": 1.15, "y1": 2.0})
        spec["surfaces"] = {k: SURFACES[k] for k in ("timber", "plaster", "stone", "boards",
                                                    "roof")}
    else:
        spec.update(style="stone", eave_h=spec["h"] + 0.20, floor_surface="stone",
                    gable_surface="boards",
                    roof={"pitch": 38.0, "eave": 0.45, "gable": 0.35, "thick": 0.12})
        spec["openings"].append({"kind": "window", "wall": "south", "centre": -1.5,
                                 "width": 0.9, "y0": 1.2, "y1": 2.1})
        hearth = (60.5, 143.5)
        spec["hearth_world"] = list(hearth)
        spec["anvil_world"] = [57.5, 140.5]
        spec["chimney"] = {"x": hearth[0] - cx, "z": hearth[1] - cz, "size": 0.9,
                           "hood_bottom": 2.1, "hood_width": 1.5, "above_ridge": 0.55}
        spec["surfaces"] = {k: SURFACES[k] for k in ("timber", "stone", "boards", "roof",
                                                    "rubble", "iron")}
    return spec


def build_building(spec):
    model = Model(spec["asset_id"], spec["surfaces"])
    walls = make_walls(spec)
    if spec["style"] == "timber_frame":
        timber_frame_walls(model, spec, walls)
    else:
        stone_walls(model, spec, walls)
    info = roof_and_gables(model, spec, walls)
    if spec.get("chimney"):
        info.update(chimney(model, spec, info))
    floor(model, spec)
    return model, info


def build_fence(box):
    x0, z0, x1, z1 = box["values"]
    L, D, H = x1 - x0, z1 - z0, box["height"]
    model = Model("building_fence_panel", {"timber": SURFACES["timber"]})
    posts = 5
    pw = 0.16
    for i in range(posts):
        x = -L / 2 + pw / 2 + (L - pw) * i / (posts - 1)
        model.box("structure", (x - pw / 2, 0.0, -pw / 2), (x + pw / 2, H - 0.08, pw / 2),
                  "timber", model.timber_uv(UP, AX), "post")
        base = [Vector((x - pw / 2, H - 0.08, -pw / 2)), Vector((x + pw / 2, H - 0.08, -pw / 2)),
                Vector((x + pw / 2, H - 0.08, pw / 2)), Vector((x - pw / 2, H - 0.08, pw / 2))]
        tip = Vector((x, H, 0.0))
        model.add("structure", base + [tip], [(3, 2, 1, 0), (0, 1, 4), (1, 2, 4), (2, 3, 4),
                                               (3, 0, 4)], "timber", model.timber_uv(UP, AX),
                  "post_cap")
    for y in (0.30, 0.64, 0.98):
        model.box("structure", (-L / 2 + 0.03, y - 0.055, -0.035), (L / 2 - 0.03, y + 0.055, 0.035),
                  "timber", model.timber_uv(AX, UP), "rail")
    assert D >= pw
    return model, {"run_m": L, "depth_m": D, "height_m": H, "posts": posts, "rails": 3}


def build_well(circle):
    cxw, czw, radius = circle["values"]
    wall_h = circle["height"]
    model = Model("building_well", {k: SURFACES[k] for k in ("stone", "rubble", "timber",
                                                             "roof", "iron")})
    r_in = 0.65
    course = ASHLAR_COURSE_M
    segments = 18      # chord sag 1.5 cm, so the half-block bond offset barely shows

    def ring_uv(i, angle):
        tangent = Vector((-math.sin(angle), 0.0, math.cos(angle)))
        return UV(model.tile("stone"), grain=UP, alt=tangent, u0=model.rng.random(), v0=ASHLAR_V0)

    for k in range(3):
        model.annulus("structure", (0.0, 0.0), r_in, radius, k * course, (k + 1) * course,
                      segments, "stone", ring_uv, "ring", phase=(k % 2) * math.pi / segments,
                      gap=0.0)
    model.annulus("structure", (0.0, 0.0), r_in - 0.03, radius, 3 * course, wall_h, segments,
                  "stone", ring_uv, "coping", phase=math.pi / segments / 2)
    model.cylinder("structure", (0.0, 0.0, 0.0), UP, r_in + 0.005, 0.03, 16, "rubble",
                   UV(model.tile("rubble"), grain=AX, alt=AZ), "shaft_floor")

    # Frame: two posts seated in the ring, a head beam, and a small slated roof.
    px, pw = r_in + (radius - r_in) / 2, 0.16
    post_top = 2.25
    for s in (-1, 1):
        model.box("structure", (s * px - pw / 2, 0.6, -pw / 2), (s * px + pw / 2, post_top, pw / 2),
                  "timber", model.timber_uv(UP, AX), "post")
    head_top = post_top + 0.16
    model.box("structure", (-1.02, post_top, -0.08), (1.02, head_top, 0.08), "timber",
              model.timber_uv(AX, UP), "head_beam")
    tp = math.tan(math.radians(40.0))
    span, thick, xr = 0.85, 0.05, 1.1

    def yu(z):
        return head_top - (abs(z) - 0.0) * tp

    for sign in (-1, 1):
        corners = []
        for i in range(8):
            x = xr if i & 1 else -xr
            z = sign * span if i & 2 else 0.0
            corners.append(Vector((x, yu(z) + (thick / math.cos(math.radians(40))
                                               if i & 4 else 0.0), z)))
        model.hexa("roof", corners, "roof",
                   UV(model.tile("roof"), grain=Vector((0.0, tp, -sign)).normalized(), alt=AX,
                      origin=corners[2], u0=model.rng.random(), v0=SLATE_V0), "roof_slope")
    top = head_top + thick / math.cos(math.radians(40))
    model.prism("roof", [Vector((-xr - 0.03, yu(0.1) + thick / math.cos(math.radians(40)) - 0.01, -0.1)),
                         Vector((-xr - 0.03, yu(0.1) + thick / math.cos(math.radians(40)) - 0.01, 0.1)),
                         Vector((-xr - 0.03, top + 0.05, 0.0))],
                (2 * xr + 0.06, 0, 0), "timber", model.timber_uv(AX, UP), "ridge_cap")

    # Windlass: axle between the posts, rope drum, iron crank outside the east post.
    axle_y = 1.55
    crank_x = px + pw / 2 + 0.2
    model.cylinder("structure", (-px + pw / 2, axle_y, 0.0), AX, 0.06, crank_x - (-px + pw / 2), 10,
                   "timber", model.timber_uv(AX, UP), "axle")
    model.cylinder("structure", (-0.25, axle_y, 0.0), AX, 0.10, 0.50, 12, "timber",
                   model.timber_uv(AX, UP), "rope_drum", phase=0.2)
    model.box("structure", (crank_x - 0.02, axle_y - 0.28, -0.025), (crank_x + 0.02, axle_y + 0.03, 0.025),
              "iron", UV(model.tile("iron"), grain=UP, alt=AX), "crank_arm")
    model.cylinder("structure", (crank_x, axle_y - 0.26, 0.0), AX, 0.02, 0.18, 8, "iron",
                   UV(model.tile("iron"), grain=AX, alt=UP), "crank_handle")

    # Rope and bucket hanging over the shaft.
    bucket_bottom, bucket_h = 1.02, 0.30
    model.cylinder("structure", (0.0, bucket_bottom + bucket_h + 0.06, 0.0), UP, 0.012,
                   axle_y - 0.10 - (bucket_bottom + bucket_h + 0.06), 6, "timber",
                   model.timber_uv(UP, AX), "rope")
    model.cylinder("structure", (0.0, bucket_bottom, 0.0), UP, 0.13, bucket_h, 12, "timber",
                   model.timber_uv(UP, AX), "bucket", radius_top=0.16)
    for y in (bucket_bottom + 0.05, bucket_bottom + 0.22):
        r0 = 0.13 + 0.03 * (y - bucket_bottom) / bucket_h
        model.cylinder("structure", (0.0, y, 0.0), UP, r0 + 0.008, 0.03, 12, "iron",
                       UV(model.tile("iron"), grain=UP, alt=AX), "hoop",
                       radius_top=r0 + 0.008 + 0.003)
    for s in (-1, 1):
        model.box("structure", (s * 0.165 - 0.008, bucket_bottom + bucket_h - 0.04, -0.01),
                  (s * 0.165 + 0.008, bucket_bottom + bucket_h + 0.07, 0.01), "iron",
                  UV(model.tile("iron"), grain=UP, alt=AX), "bail")
    model.box("structure", (-0.173, bucket_bottom + bucket_h + 0.05, -0.01),
              (0.173, bucket_bottom + bucket_h + 0.07, 0.01), "iron",
              UV(model.tile("iron"), grain=AX, alt=UP), "bail")
    return model, {"ring_outer_diameter_m": 2 * radius, "ring_inner_diameter_m": 2 * r_in,
                   "ring_height_m": wall_h, "frame_top_m": top + 0.05, "world_centre": [cxw, czw]}


# --------------------------------------------------------------------------------------------
# Collision agreement: drawn solids against the world blockers
# --------------------------------------------------------------------------------------------

def inside_solids(points, solids):
    hit = np.zeros(len(points), dtype=bool)
    for solid in solids:
        lo, hi = np.array(solid.lo[:]), np.array(solid.hi[:])
        box = np.all((points >= lo - 1e-9) & (points <= hi + 1e-9), axis=1) & ~hit
        if not box.any():
            continue
        cand = points[box]
        ok = np.ones(len(cand), dtype=bool)
        for n, d in solid.planes:
            ok &= cand @ np.array(n[:]) <= d + 1e-9
        idx = np.nonzero(box)[0][ok]
        hit[idx] = True
    return hit


def collision_agreement(model, spec, heights=(0.15, 1.0, 1.7), step=0.05, band=0.035):
    """Sample a grid over the footprint: blocked (world) vs drawn (structure solids), per height.
    Points within `band` of a blocker face are skipped: members may be recessed that far."""
    cx, cz = spec["world_centre"]
    x0, z0, x1, z1 = spec["world_box"]
    xs = np.arange(x0 - 0.5 + step / 2, x1 + 0.5, step)
    zs = np.arange(z0 - 0.5 + step / 2, z1 + 0.5, step)
    gx, gz = np.meshgrid(xs, zs, indexing="ij")
    gx, gz = gx.ravel(), gz.ravel()
    blockers = list(spec["blockers"].values())
    door = spec.get("door_box")
    body = [s for s in model.nodes["structure"] if s.tag not in ("floor", "threshold")]
    walls = make_walls(spec)
    result = {}
    for y in heights:
        blocked = np.zeros(len(gx), dtype=bool)
        near = np.zeros(len(gx), dtype=bool)
        for b in blockers:
            bx0, bz0, bx1, bz1 = b["values"]
            if y >= b["height"]:
                continue
            inside = (gx > bx0) & (gx < bx1) & (gz > bz0) & (gz < bz1)
            blocked |= inside
            edge = ((gx > bx0 - band) & (gx < bx1 + band) & (gz > bz0 - band) & (gz < bz1 + band)
                    & ~((gx > bx0 + band) & (gx < bx1 - band) & (gz > bz0 + band) & (gz < bz1 - band)))
            near |= edge
        in_door = np.zeros(len(gx), dtype=bool)
        if door:
            dx0, dz0, dx1, dz1 = door["values"]
            in_door = (gx > dx0) & (gx < dx1) & (gz > dz0) & (gz < dz1)
            dedge = ((gx > dx0 - band) & (gx < dx1 + band) & (gz > dz0 - band) & (gz < dz1 + band)
                     & ~((gx > dx0 + band) & (gx < dx1 - band) & (gz > dz0 + band) & (gz < dz1 - band)))
            near |= dedge
        # Window reveals: open to the eye in front of and behind the closed shutter, solid to
        # collision. Counted apart, since a blocker there is correct (nobody climbs through).
        in_window = np.zeros(len(gx), dtype=bool)
        for wall, o in ((w, o) for w in walls.values() for o in w.openings):
            if o["kind"] != "window" or not (o["y0"] < y < o["y1"]):
                continue
            lo, hi = wall.lo_hi(o["s0"], o["s1"], 0.0, 1.0)
            in_window |= ((gx - cx > lo[0]) & (gx - cx < hi[0]) & (gz - cz > lo[2])
                          & (gz - cz < hi[2]))
        pts = np.stack([gx - cx, np.full(len(gx), y), gz - cz], axis=1)
        drawn = inside_solids(pts, body)
        check = ~near & ~in_door & ~in_window
        result[f"y_{y:.2f}"] = {
            "samples": int(check.sum()),
            "window_reveal_open_samples": int((in_window & ~near & ~drawn).sum()),
            "blocked_not_drawn": int((blocked & ~drawn & check).sum()),
            "drawn_not_blocked": int((drawn & ~blocked & check).sum()),
            "door_box_samples": int((in_door & ~near).sum()),
            "door_box_drawn_frame": int((in_door & ~near & drawn).sum()),
        }
    ok = all(r["blocked_not_drawn"] == 0 and r["drawn_not_blocked"] == 0 for r in result.values())
    frame_share = {k: round(r["door_box_drawn_frame"] / max(1, r["door_box_samples"]), 3)
                   for k, r in result.items()}
    return {"ok": ok, "grid_step_m": step, "face_band_m": band, "levels": result,
            "door_box_share_drawn_as_frame": frame_share}


def circle_agreement(model, circle, heights=(0.15, 0.6), step=0.02, band=0.035):
    cx, cz, r = circle["values"]
    xs = np.arange(-r - 0.3, r + 0.3, step) + step / 2
    gx, gz = np.meshgrid(xs, xs, indexing="ij")
    gx, gz = gx.ravel(), gz.ravel()
    rr = np.hypot(gx, gz)
    body = [s for s in model.nodes["structure"] if s.tag in ("ring", "coping", "shaft_floor")]
    out = {}
    for y in heights:
        drawn = inside_solids(np.stack([gx, np.full(len(gx), y), gz], axis=1), body)
        outside = rr > r + band
        out[f"y_{y:.2f}"] = {"drawn_outside_circle": int((drawn & outside).sum()),
                             "ring_solid_share_inside": round(float(drawn[rr < r - band].mean()), 3)}
    return {"ok": all(v["drawn_outside_circle"] == 0 for v in out.values()), "levels": out}


def box_agreement(model, box, heights=(0.3, 0.64, 0.98)):
    x0, z0, x1, z1 = box["values"]
    L, D = x1 - x0, z1 - z0
    lo = np.min([np.array(s.lo[:]) for s in model.nodes["structure"]], axis=0)
    hi = np.max([np.array(s.hi[:]) for s in model.nodes["structure"]], axis=0)
    inside = bool(lo[0] >= -L / 2 - 1e-6 and hi[0] <= L / 2 + 1e-6 and lo[2] >= -D / 2 - 1e-6
                  and hi[2] <= D / 2 + 1e-6 and hi[1] <= box["height"] + 1e-6 and lo[1] >= -1e-6)
    return {"ok": inside, "drawn_bounds_m": [lo.round(4).tolist(), hi.round(4).tolist()],
            "blocker_local_m": [[-L / 2, 0.0, -D / 2], [L / 2, box["height"], D / 2]]}


# --------------------------------------------------------------------------------------------
# Textures and materials
# --------------------------------------------------------------------------------------------

def load_pixels(path):
    image = bpy.data.images.load(path, check_existing=False)
    try:
        image.colorspace_settings.name = "Non-Color"
        w, h = image.size
        px = np.empty(w * h * image.channels, dtype=np.float32)
        image.pixels.foreach_get(px)
        return px.reshape(h, w, image.channels)[:, :, :3].copy()      # rows bottom-up
    finally:
        bpy.data.images.remove(image)


def periodic_weights(n_in, n_out):
    """Area-weighted resampling taps that wrap, so a tiling texture still tiles: for each output
    sample, the input indices it overlaps and their weights (each row sums to 1)."""
    scale = n_in / n_out
    taps = int(math.ceil(scale)) + 1
    index = np.zeros((n_out, taps), dtype=np.int64)
    weight = np.zeros((n_out, taps), dtype=np.float64)
    for i in range(n_out):
        a, b = i * scale, (i + 1) * scale
        j, k = math.floor(a), 0
        while j < b:
            overlap = min(b, j + 1) - max(a, j)
            if overlap > 0:
                index[i, k], weight[i, k] = j % n_in, overlap
                k += 1
            j += 1
        weight[i] /= weight[i].sum()
    return index, weight


def resample_axis(a, axis, taps):
    index, weight = taps
    out = 0.0
    for k in range(index.shape[1]):
        w = weight[:, k].reshape([-1 if d == axis else 1 for d in range(a.ndim)])
        out = out + np.take(a, index[:, k], axis=axis) * w
    return out


def prepare_textures(material_id, directory, record):
    import _blender_bake_world_materials as bw
    tile = float(record["tile_size_m"])
    size = int(round(PX_PER_M * tile / 4.0)) * 4
    out_dir = os.path.join(TEXTURES, material_id)
    os.makedirs(out_dir, exist_ok=True)
    maps = {}
    for key in ("basecolor", "normal", "orm"):
        src = os.path.join(directory, record["maps"][key])
        dst = os.path.join(out_dir, f"{material_id}_{key}_{size}.png")
        maps[key] = dst
        if os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src):
            continue
        a = load_pixels(src).astype(np.float64)
        taps = periodic_weights(a.shape[0], size)
        out = resample_axis(resample_axis(a, 0, taps), 1, taps)
        if key == "normal":
            n = out * 2.0 - 1.0
            n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-8)
            out = n * 0.5 + 0.5
        bw.write_png(dst, np.ascontiguousarray(bw.to_u8(out[::-1])))   # PNG rows top-down
    return {"size_px": size, "px_per_m": round(size / tile, 1), "tile_size_m": tile,
            "files": {k: os.path.relpath(v, REPO).replace("\\", "/") for k, v in maps.items()},
            "source": os.path.relpath(directory, REPO).replace("\\", "/")}


def gltf_output_group():
    # The addon's own constructor: the importer reuses a group by this name and expects all of its
    # sockets (a hand-made Occlusion-only group makes the next GLB import fail).
    from io_scene_gltf2.blender.com.material_helpers import create_settings_group, get_gltf_node_name
    group = bpy.data.node_groups.get(get_gltf_node_name())
    if group is None:
        group = create_settings_group(get_gltf_node_name())
    return group


def make_material(name, maps):
    material = bpy.data.materials.new(name)
    material.use_backface_culling = True
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    for node in list(nodes):
        nodes.remove(node)
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    base = nodes.new("ShaderNodeTexImage")
    base.image = bpy.data.images.load(os.path.join(REPO, maps["basecolor"]))
    links.new(base.outputs["Color"], bsdf.inputs["Base Color"])
    normal = nodes.new("ShaderNodeTexImage")
    normal.image = bpy.data.images.load(os.path.join(REPO, maps["normal"]))
    normal.image.colorspace_settings.name = "Non-Color"
    nmap = nodes.new("ShaderNodeNormalMap")
    links.new(normal.outputs["Color"], nmap.inputs["Color"])
    links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    orm = nodes.new("ShaderNodeTexImage")
    orm.image = bpy.data.images.load(os.path.join(REPO, maps["orm"]))
    orm.image.colorspace_settings.name = "Non-Color"
    sep = nodes.new("ShaderNodeSeparateColor")
    links.new(orm.outputs["Color"], sep.inputs["Color"])
    links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
    links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    group = nodes.new("ShaderNodeGroup")
    group.node_tree = gltf_output_group()
    links.new(sep.outputs["Red"], group.inputs["Occlusion"])
    return material


# --------------------------------------------------------------------------------------------
# Blender output
# --------------------------------------------------------------------------------------------

def to_blender(v):
    return (v.x, -v.z, v.y)


def realise(model, textures):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    root = bpy.data.objects.new(model.asset_id, None)
    bpy.context.collection.objects.link(root)
    materials = {}
    for surface, material_id in model.surfaces.items():
        materials[surface] = make_material(f"MAT_{model.asset_id}_{surface}",
                                           textures[material_id]["files"])
    objects = {}
    for node, solids in model.nodes.items():
        verts, faces, uvs, slots = [], [], [], []
        used = [s for s in model.surfaces if any(x.surface == s for x in solids)]
        for solid in solids:
            base = len(verts)
            verts += [to_blender(v) for v in solid.verts]
            for f in solid.faces:
                pts = [solid.verts[i] for i in f]
                n = newell(pts).normalized()
                faces.append(tuple(base + i for i in f))
                uvs.append(solid.uv.face(pts, n))
                slots.append(used.index(solid.surface))
        mesh = bpy.data.meshes.new(f"{model.asset_id}_{node}_mesh")
        mesh.from_pydata(verts, [], faces)
        mesh.validate()
        layer = mesh.uv_layers.new(name="UVMap")
        loop = 0
        for polygon, face_uv in zip(mesh.polygons, uvs):
            assert polygon.loop_total == len(face_uv)
            for k in range(polygon.loop_total):
                layer.data[polygon.loop_start + k].uv = face_uv[k]
            polygon.material_index = slots[polygon.index]
            loop += polygon.loop_total
        for surface in used:
            mesh.materials.append(materials[surface])
        mesh.update()
        obj = bpy.data.objects.new(node, mesh)
        bpy.context.collection.objects.link(obj)
        obj.parent = root
        objects[node] = obj
    bpy.context.view_layer.update()
    return root, objects


def export(root, objects, path):
    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    root.select_set(True)
    for obj in objects.values():
        obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True,
                              export_apply=True, export_yup=True, export_normals=True,
                              export_materials="EXPORT", export_texcoords=True,
                              export_cameras=False, export_lights=False)


def sha256(path):
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def run_asset(asset_id, out_root, boxes, circles, doors):
    if asset_id in ("longhouse", "forge_shed"):
        spec = building_spec(asset_id, boxes, doors)
        model, info = build_building(spec)
        agreement = collision_agreement(model, spec)
        world = {"blockers": spec["blockers"], "door": spec["door_box"],
                 "centre_xz": spec["world_centre"], "box_xz": spec["world_box"]}
        facing = ({"longhouse": "door on the model's +X side (world east)",
                   "forge_shed": "door on the model's -X side (world west)"}[asset_id])
        params = {k: spec[k] for k in ("size_x", "size_z", "t", "h", "style", "eave_h", "roof",
                                       "openings", "floor_surface", "gable_surface")}
        for key in ("chimney", "tie_beams", "hearth_world", "anvil_world"):
            if key in spec:
                params[key] = spec[key]
        centre = spec["world_centre"]
    elif asset_id == "building_fence_panel":
        box = boxes["fence_smithy"]
        model, info = build_fence(box)
        agreement = box_agreement(model, box)
        x0, z0, x1, z1 = box["values"]
        centre = [(x0 + x1) / 2, (z0 + z1) / 2]
        world = {"blocker": {"fence_smithy": box}, "centre_xz": centre}
        facing = "run along the model's X axis (world east-west); symmetric"
        params = info
    elif asset_id == "building_well":
        circle = circles["rock_well"]
        model, info = build_well(circle)
        agreement = circle_agreement(model, circle)
        centre = circle["values"][:2]
        world = {"blocker": {"rock_well": circle}, "centre_xz": centre}
        facing = "windlass axle along the model's X axis, crank on +X; ring is round"
        params = info
    else:
        raise SystemExit(f"unknown asset {asset_id}")

    textures = {}
    for material_id in sorted(set(model.surfaces.values())):
        directory, record = material_record(material_id)
        textures[material_id] = prepare_textures(material_id, directory, record)

    root, objects = realise(model, textures)
    gates = {}
    for node, obj in objects.items():
        report = kit.geometry_gate(obj)
        gates[node] = {k: v for k, v in report.items() if k != "object"}
        if not report["ok"]:
            raise SystemExit(f"geometry gate failed for {asset_id}/{node}: {report}")

    out_dir = os.path.join(out_root, asset_id)
    os.makedirs(out_dir, exist_ok=True)
    glb = os.path.join(out_dir, f"{asset_id}.glb")
    export(root, objects, glb)
    glb_report = kit.glb_gate(glb)
    if not glb_report["ok"]:
        raise SystemExit(f"GLB gate failed for {glb}: {glb_report}")

    triangles = {}
    low = np.full(3, 1e9)
    high = np.full(3, -1e9)
    for node, obj in objects.items():
        obj.data.calc_loop_triangles()
        triangles[node] = len(obj.data.loop_triangles)
        co = np.array([v.co[:] for v in obj.data.vertices])
        low, high = np.minimum(low, co.min(0)), np.maximum(high, co.max(0))
    total = sum(triangles.values())
    if total > 60000:
        raise SystemExit(f"{asset_id}: {total} triangles exceeds the 60k budget")
    # Blender (x, y, z) -> glTF (x, z, -y)
    bounds = {"min": [round(low[0], 4), round(low[2], 4), round(-high[1], 4)],
              "max": [round(high[0], 4), round(high[2], 4), round(-low[1], 4)]}
    tags = {}
    for node, solids in model.nodes.items():
        for s in solids:
            tags[f"{node}/{s.tag}"] = tags.get(f"{node}/{s.tag}", 0) + 1

    provenance = {
        "asset_id": asset_id,
        "category": "building",
        "modular_interface_version": MODULAR_INTERFACE_VERSION,
        "status": "export_ready",
        "godot_validated": False,
        "concept_source": {"longhouse": "assets/concepts/building_lodge.png",
                           "forge_shed": "assets/concepts/building_smithy.png"}.get(asset_id),
        "generation_model": "parametric (no image-to-3D)",
        "generator": {"script": "tools/asset_pipeline/_procgen_building.py",
                      "script_sha256": sha256(os.path.abspath(__file__)),
                      "gate_script_sha256": sha256(os.path.join(HERE, "_blender_build_kit.py")),
                      "blender": bpy.app.version_string},
        "generation_date": time.strftime("%Y-%m-%d %H:%M:%S"),
        "glb_path": os.path.relpath(glb, REPO).replace("\\", "/"),
        "glb_sha256": sha256(glb),
        "glb_bytes": os.path.getsize(glb),
        "world_truth": {"source": "content/regions/ashen_hollow.yaml", **world},
        "frame": {"origin": "footprint centre at ground level",
                  "axes": "glTF +X = world +X (east), +Y up, +Z = world +Z (north)",
                  "placement": f"position ({centre[0]}, 0, {centre[1]}), rotation none",
                  "facing": facing},
        "parameters": params,
        "measured": info,
        "bounds_m": bounds,
        "dimensions_m": [round(bounds["max"][i] - bounds["min"][i], 4) for i in range(3)],
        "nodes": {node: {"triangles": triangles[node], "solids": len(model.nodes[node]),
                         "materials": [f"MAT_{asset_id}_{s}" for s in model.surfaces
                                       if any(x.surface == s for x in model.nodes[node])]}
                  for node in objects},
        "members": tags,
        "triangles": total,
        "triangle_budget": 60000,
        "materials": {f"MAT_{asset_id}_{s}": {"material": m, **textures[m]}
                      for s, m in model.surfaces.items()},
        "texture_resolution": "per material, resampled periodically to ~512 px/m",
        "uv": ("per-face projection in each member's frame at the material's real tile size; "
               "walls share the building frame so courses and plaster continue across pieces"),
        "gates": {"geometry": gates, "glb": {k: v for k, v in glb_report.items() if k != "glb"}},
        "collision_agreement": agreement,
        "collision_status": "world blockers (content/regions/ashen_hollow.yaml); no proxy shipped",
        "lod_status": "none (building LODs to be generated from this assembly)",
        "rigged": False,
        "license_notes": "First-party parametric asset; textures baked from first-party procedural shaders.",
    }
    with open(os.path.join(out_dir, f"{asset_id}_provenance.json"), "w", encoding="utf-8") as fh:
        json.dump(provenance, fh, indent=2)
        fh.write("\n")
    print("PROCGEN_RESULT " + json.dumps({
        "asset_id": asset_id, "glb": glb, "triangles": triangles, "total": total,
        "dimensions_m": provenance["dimensions_m"], "collision_ok": agreement["ok"],
        "glb_gate_ok": glb_report["ok"], "bytes": provenance["glb_bytes"]}))
    return provenance


# --------------------------------------------------------------------------------------------
# Variant materials, baked by the world-material shaders
# --------------------------------------------------------------------------------------------

def register_variants():
    import _blender_bake_world_materials as bw
    tau = bw.TAU

    @bw.material("material_hewn_oak_timber", "timber", 2.4, ao=0.6)
    def hewn_oak(g, T):
        """Adzed structural oak: six faces a tile, grain the full length, no butt joints or nails
        (the plank floor's joints and nails read wrong on a post or beam)."""
        faces = HEWN_FACES
        width = T / faces
        cu = g.U * float(faces) + 0.37
        fx = g.fract(cu)
        key = g.mod(g.floor(cu), float(faces)) + 1.0
        b1, b2, b3 = g.hash(key, 1.0, 180)
        ex = g.min(fx, 1.0 - fx) * width
        seam = 1.0 - g.smooth(0.0015, 0.003, ex)
        arris = g.smooth(0.0, 0.012, ex)
        off = g.vec(b1 * 97.0, b2 * 89.0, b3 * 71.0)
        wob = g.z4(3.0, 1.2, seed=182, detail=3, off=off)
        wob2 = g.z4(12.0, 3.0, seed=183, detail=2, off=off)
        lines = fx * 70.0 + b1 * 40.0 + wob * 1.6 + wob2 * 0.4
        ring = g.sin(lines * tau) * 0.5 + 0.5
        late = g.smooth(0.78, 0.99, ring)
        fibre = g.z4(900.0, 25.0, seed=186, detail=2, off=off)
        check_z = g.z4(40.0, 2.0, seed=187, detail=2, off=off)
        check = (g.smooth(0.94, 0.985, g.sin((fx * 5.0 + wob * 0.8) * tau) * 0.5 + 0.5)
                 * g.smooth(0.3, 1.2, check_z))
        adze = g.z4(18.0, 6.0, seed=188, detail=3, off=off)
        weather = g.smooth(0.2, 1.6, g.z4(2.0, 0.6, seed=189, detail=4))
        dents = g.z4(40, seed=190, detail=3)
        height = (adze * 0.0012 + arris * 0.002 - seam * 0.003 + late * 0.0002
                  + fibre * 0.00005 + dents * 0.0002 - check * 0.002)
        tone = g.ramp(g.sat(b2 * 0.8 + b3 * 0.2), [(0.0, (70, 54, 40)), (0.5, (86, 68, 50)),
                                                   (1.0, (100, 82, 62))])
        wood = g.mix(tone, bw.lin((92, 86, 78)), weather * 0.45)
        wood = wood * (1.0 - late * 0.28) * (1.0 + fibre * 0.05)
        wood = wood * (1.0 - check * 0.6)
        wood = g.mix(wood, bw.lin((30, 25, 20)), seam)
        rough = 0.72 + weather * 0.08 - late * 0.03 + check * 0.1
        return {"base": wood, "height": height, "rough": rough, "metal": 0.0}

    @bw.material("material_forged_iron", "metal", 1.0, ao=0.4)
    def forged_iron(g, T):
        """The cast-iron shader with its metallic corrected: iron and its scale are metal (1.0),
        only rust is dielectric (0.0). The source authors 0.6 over bare scale, which no PBR
        surface is."""
        out = bw.cast_iron(g, T)
        out["metal"] = 1.0 - g.smooth(0.82, 0.89, out["rough"])
        return out

    return bw


def bake_variants():
    bw = register_variants()
    args = argparse.Namespace(res=2048, ss=2, samples=4)
    proof = os.path.join(VARIANTS, "_proof")
    os.makedirs(proof, exist_ok=True)
    for material_id in ("material_hewn_oak_timber", "material_forged_iron"):
        record = bw.bake_material(material_id, args, VARIANTS, proof)
        record["generator"]["variant_of"] = {
            "material_hewn_oak_timber": "material_oak_plank_floor shader, restructured",
            "material_forged_iron": "material_cast_iron_surface shader, metallic corrected"}[material_id]
        record["generator"]["registered_by"] = "tools/asset_pipeline/_procgen_building.py"
        path = os.path.join(VARIANTS, material_id, f"{material_id}_material.json")
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(record, fh, indent=2)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--stage", choices=("bake-materials", "build"), default="build")
    parser.add_argument("--asset", nargs="*", default=["longhouse", "forge_shed",
                                                       "building_fence_panel", "building_well"])
    parser.add_argument("--out-root", default=STAGING)
    args = parser.parse_args(argv)
    out_root = os.path.abspath(args.out_root)
    ready = os.path.normcase(os.path.join(ASSETS, "ready"))
    if os.path.normcase(out_root) == ready or os.path.normcase(out_root).startswith(ready + os.sep):
        print("  refusing to write under assets/ready")
        return 1
    if args.stage == "bake-materials":
        bake_variants()
        return 0
    for material_id in set(SURFACES.values()):
        MATERIAL_TILES[material_id] = float(material_record(material_id)[1]["tile_size_m"])
    boxes, circles, doors = read_region(REGION)
    for asset_id in args.asset:
        run_asset(asset_id, out_root, boxes, circles, doors)
    return 0


if __name__ == "__main__":
    code = main()
    sys.stdout.flush()
    if code:
        sys.exit(code)
