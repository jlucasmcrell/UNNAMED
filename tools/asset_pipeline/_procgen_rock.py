"""rock_outcrop archetype template: weathered rock built to its blocker's dimensions (docs/WAVE_0_MODULAR_ASSET_STANDARD.md
section 17; assets/manifests/asset_production.json).

One template, one parameter file per asset: assets/manifests/procgen/<asset id>.json holds the size (from the blocker the
game draws it over), the shape family, the seed and the material recipe. Everything shared (shader graph, material
recipes, UV packing, baking, validation, export, provenance, noise, the run itself) comes from procgen_lib.

Families (params "family"):
    boulder     one rock solid: a rounded box cut by facet planes (joints, chip scars), with strata ledges, open
                splits, dents and noise, stood on a flat bed at z = 0 and scaled to size_m exactly (rim rocks, the
                beam supports, the iron seam rock)
    cluster     small solids placed one by one - ore lumps, chips, scar flakes (the iron seam's node overlays)
    fractured   a standing mass broken along fracture planes into pieces that rest on the plane below them, some set
                out of register (the Foldscar's heart); fracture faces take material slot 1
    mass        a jointed, bedded rock mass built to a box blocker, sides held within a band of the box faces, an
                irregular top; tiled (a seamless baked tile on world-scale UVs) with per-vertex colour (den walls)
    masonry     masonry_block mode: bonded dressed-stone courses on a mortar core with a cope; as a hearth, a glowing
                coal bed, iron rim, tuyere and quench bosh (prop_forge_hearth)

Shape (Blender Z-up, -Y front = glTF +Z = world north; +Y = world south): a solid is a radial graph about an interior
point, so every ray from it leaves once. Its grid is a box lattice (top and four sides) at about grid_m spacing,
mapped to rays through the box's proportions; the open bottom is a fan on its bed (the ground, or for a broken piece
the fracture it rests on). Planar breaks are snapped and relaxed so a grid does not alias them into teeth.

Material recipes here extend the library's (rock_face over materials.stone; masonry over rock_face; iron_ore, fold_face,
coals, water, and the periodic rock_tile) and a JSON slot may also name a library recipe (iron, plaster, ...).

    blender --background --factory-startup --python tools/asset_pipeline/_procgen_rock.py -- --asset <id> [runner args]
"""
import json
import math
import os
import sys

import numpy as np

TOOLS = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TOOLS)
from procgen_lib import noise as nz  # noqa: E402
from procgen_lib import primitives as pr  # noqa: E402
from procgen_lib.materials import Material, stone  # noqa: E402
from procgen_lib.runner import run_template  # noqa: E402
from procgen_lib.shadergraph import lin  # noqa: E402

PARAM_DIR = os.path.join(os.path.dirname(os.path.dirname(TOOLS)), "assets", "manifests", "procgen")
BIG = 1.0e3
Z = np.array([0.0, 0.0, 1.0])


# ------------------------------------------------------------------------------------------------ materials

# Mineral grain per rock kind (sRGB): the common grain, a second grain, the dark specks, the pale specks, and the
# shares of the last three. The library's stone() grain (2-3 mm) only reads as pixel noise at rock texel densities.
GRAIN = {
    "granite": [(166, 162, 156), (176, 160, 150), (44, 43, 43), (196, 196, 192), 0.3, 0.12, 0.1],
    "limestone": [(170, 162, 144), (156, 150, 134), (112, 106, 94), (188, 182, 168), 0.35, 0.05, 0.08],
    "greywacke": [(104, 102, 98), (92, 90, 88), (58, 56, 55), (136, 134, 128), 0.35, 0.1, 0.06],
}
LICHEN = [(170, 170, 160), (150, 152, 128), (168, 150, 96)]


def zn(g, v, w, detail=3.0):
    """Object-space fBm rescaled to about zero mean and unit deviation (the normalised noise deviates ~0.075)."""
    return (g.noise(v, w, detail=detail) - 0.5) * 13.0


def rock_face(g, c, kind="granite", weathering=0.5, lichen=0.3, tint=(1.0, 1.0, 1.0), bump_m=0.004, cracks=0.5,
              crack_cell_m=1.2, grain_mm=9.0, grain_mix=0.7, strata_m=0.0, strata_dip=(0.0, 0.0, 1.0), veins=0.0,
              rust=0.0, rust_dir=(0.0, 1.0, 0.0), rust_at=(0.0, 0.0, 0.5), rust_radius_m=1.2, rust_vein=None,
              fresh=0.0):
    """Weathered rock face over the library's stone(): mineral grain, mottling, a dark weathering crust, lichen
    crusts, relief the grid is too coarse to carry, a partly open crack network, bedding lines, pale veins, rain
    streaks and (for an ore seam) an iron vein bleeding rust down the face. rust_vein: {"normal", "width_m"} of the
    vein plane through rust_at. fresh 0..1 strips the weathering (a new break)."""
    s = stone(g, c, kind="limestone" if kind == "limestone" else "granite", weathering=weathering, lichen=0.0)
    p = c.pos
    G = GRAIN[kind]
    warp = g.noise(p * 0.9, 21.0, detail=2.0, color=True) - (0.5, 0.5, 0.5)
    t_big, t_mid, t_fine = zn(g, p * 0.8, 31.0), zn(g, p * 6.0, 32.0), zn(g, p * 30.0, 33.0, 2.0)
    gd, gc = g.voronoi(p * (1000.0 / grain_mm))
    gr, gg, _ = g.sep(gc)
    mineral = g.mix(lin(G[0]), lin(G[1]), g.lt(gg, G[4]))
    mineral = g.mix(mineral, lin(G[2]), g.lt(gr, G[5]))
    mineral = g.mix(mineral, lin(G[3]), g.gt(gr, 1.0 - G[6]))
    col = g.mix(s["base"], mineral, grain_mix) * tuple(tint)
    col = col * g.sat(1.0 + t_big * 0.1 + t_mid * 0.06 + t_fine * 0.03)
    # weathering: a dark crust in patches, greyed and dirtied low down, paler spalls on exposed arrises
    aged = weathering * (1.0 - fresh)
    crust = g.smooth(0.2, 1.4, t_big + t_mid * 0.5)
    col = g.mix(col, lin(96, 92, 84) * g.sat(0.85 + t_mid * 0.1), crust * 0.6 * aged)
    col = g.mix(col, lin(98, 88, 72), (1.0 - c.low) * 0.35 * aged)
    spall = g.smooth(0.8, 1.6, zn(g, p * 2.2, 34.0)) * c.cvx
    col = g.mix(col, col * 1.2, g.sat(spall + fresh * 0.4))
    # lichen: ragged crusts of mixed sizes, clustered where the weather reaches
    rag = g.noise(p * 16.0, 36.0, detail=3.0, color=True) - (0.5, 0.5, 0.5)
    ld, lc = g.voronoi(p * 5.0 + warp * 1.1 + rag * 0.5)
    lr, lg, lb = g.sep(lc)
    size = 0.1 + lg * lg * 0.38
    spot = ((1.0 - g.smooth(size * 0.75, size, ld)) * g.lt(lr, lichen * 0.8) * g.smooth(-0.4, 0.8, zn(g, p * 0.8, 35.0))
            * g.smooth(-0.3, 0.5, c.nz) * g.smooth(0.3, 0.9, c.ao) * (1.0 - fresh))
    lcol = g.mix(g.mix(lin(LICHEN[0]), lin(LICHEN[1]), g.gt(lb, 0.55)), lin(LICHEN[2]), g.gt(lb, 0.88))
    col = g.mix(col, lcol * g.sat(0.9 + t_fine * 0.05), spot * 0.85)
    # cracks: a few long joints (cell edges of a warped Voronoi, open only in places) and hairlines
    e, _ = g.voronoi(p * (1.0 / crack_cell_m) + warp * 0.5, feature="DISTANCE_TO_EDGE")
    open_ = g.smooth(0.3, 1.0, zn(g, p * 0.6, 25.0)) * cracks
    crack = (1.0 - g.smooth(0.0, 0.016, e)) * open_
    lip = (1.0 - g.smooth(0.016, 0.05, e)) * open_ * (1.0 - crack)
    e2, _ = g.voronoi(p * (3.0 / crack_cell_m) + warp * 1.2, feature="DISTANCE_TO_EDGE")
    hair = (1.0 - g.smooth(0.0, 0.02, e2)) * g.smooth(0.8, 1.4, zn(g, p * 1.7, 26.0)) * cracks
    col = g.mix(col, col * 0.18, crack * 0.9)
    col = g.mix(col, col * 0.55, hair * 0.55)
    col = g.mix(col, col * 0.8, lip * 0.4)
    col = col * (0.62 + 0.38 * c.ao)
    rough = s["rough"] + crack * 0.08 + spot * 0.05 - fresh * 0.06 + t_fine * 0.015
    height = (bump_m * (t_big * 0.25 + t_mid * 0.35 + t_fine * 0.12) + (1.0 - g.smooth(0.0, 0.35, gd)) * 0.0002
              - crack * 0.008 - hair * 0.0012 - lip * 0.0015 + spot * 0.0004 - spall * 0.002)
    if strata_m > 0:
        sd = g.dot(p, tuple(strata_dip)) * (1.0 / strata_m) + t_big * 0.03
        f = g.fract(sd)
        br, _, _ = g.sep(g.white(g.vec(g.floor(sd), 3.0, 7.0)))
        col = col * (0.86 + br * 0.24)
        line = (1.0 - g.smooth(0.0, 0.04, f)) + g.smooth(0.96, 1.0, f)
        col = g.mix(col, col * 0.45, line * 0.7)
        height = height - line * 0.004 + (br - 0.5) * 0.002
    if veins > 0:
        ev, _ = g.voronoi(p * 2.6 + warp * 1.4, feature="DISTANCE_TO_EDGE")
        vein = (1.0 - g.smooth(0.0, 0.02, ev)) * g.smooth(0.0, 0.8, zn(g, p * 1.1, 28.0)) * veins
        col = g.mix(col, lin(196, 194, 188), vein * 0.75)
        height = height + vein * 0.0005
    # rain streaks: long in z, darker where water runs off the ledges
    streak = g.smooth(0.5, 1.5, zn(g, g.vec(c.lx * 5.0, c.ly * 5.0, c.pz * 0.6), 29.0))
    col = g.mix(col, col * 0.7, streak * aged * (1.0 - g.smooth(0.3, 0.8, c.nz)) * 0.8)
    if rust > 0:
        rel = p - tuple(rust_at)
        rd = g.length(rel) * (1.0 / rust_radius_m)
        face = g.smooth(-0.1, 0.55, g.dot(c.nrm, tuple(rust_dir)))
        run = g.smooth(-0.3, 1.2, zn(g, g.vec(c.lx * 9.0, c.ly * 9.0, c.pz * 0.7), 30.0, 4.0))
        halo = (1.0 - g.smooth(0.3, 1.0, rd)) * face
        core = 0.0
        if rust_vein:
            vd = g.abs(g.dot(rel, tuple(rust_vein["normal"])) + zn(g, p * 1.5, 38.0) * 0.012)
            w = rust_vein["width_m"] * g.sat(0.8 + zn(g, p * 4.0, 39.0) * 0.3)
            core = ((1.0 - g.smooth(w * 0.3, w * 0.5, vd)) * (1.0 - g.smooth(0.6, 1.0, rd))
                    * g.smooth(-0.9, 0.2, zn(g, p * 2.5, 37.0)))
            halo = halo * (0.35 + 0.65 * (1.0 - g.smooth(w * 0.5, w * 4.0, vd)))
        stain = g.sat(halo * (0.25 + 0.75 * run)) * rust
        rust_col = g.mix(lin(98, 58, 38), lin(138, 84, 50), g.sat(0.5 + t_mid * 0.3))
        rust_col = g.mix(rust_col, lin(66, 40, 30), crack)
        col = g.mix(col, rust_col * (0.6 + 0.4 * c.ao), stain * 0.8)
        ore = g.mix(lin(46, 40, 38), lin(78, 50, 40), g.sat(0.4 + t_fine * 0.25))
        col = g.mix(col, ore, core * rust)
        rough = rough + stain * 0.06 - core * 0.2
        height = height + stain * t_fine * 0.0003 + core * 0.004 + core * t_fine * 0.0008
    return {"base": col, "rough": rough, "metal": 0.0, "height": height}


def iron_ore(g, c, rust=0.6, glint=0.5):
    """Iron ore lumps: dark hematite to red-brown, streaky ochre limonite, small metallic crystal glints, cracks."""
    p = c.pos
    warp = g.noise(p * 3.0, 40.0, detail=2.0, color=True) - (0.5, 0.5, 0.5)
    t_mid, t_fine = zn(g, p * 9.0, 41.0), zn(g, p * 40.0, 42.0, 2.0)
    col = g.mix(lin(62, 50, 46), lin(106, 54, 38), g.smooth(-0.8, 0.8, zn(g, p * 3.0 + warp, 43.0)))
    col = col * g.sat(0.9 + t_mid * 0.1)
    ochre = g.smooth(0.0, 1.2, zn(g, g.vec(c.lx * 14.0, c.ly * 14.0, c.pz * 2.5), 44.0)) * rust
    col = g.mix(col, g.mix(lin(140, 84, 46), lin(112, 60, 34), g.sat(0.5 + t_fine * 0.3)), ochre * 0.75)
    gd, gc = g.voronoi(p * 160.0)
    gr, _, _ = g.sep(gc)
    spark = g.lt(gr, 0.08 * glint) * (1.0 - ochre) * (1.0 - g.smooth(0.25, 0.5, gd))
    col = g.mix(col, lin(118, 114, 112), spark)
    e, _ = g.voronoi(p * 7.0 + warp * 0.6, feature="DISTANCE_TO_EDGE")
    crack = (1.0 - g.smooth(0.0, 0.025, e)) * g.smooth(0.0, 1.0, zn(g, p * 2.0, 45.0))
    col = g.mix(col, col * 0.3, crack * 0.8)
    col = col * (0.55 + 0.45 * c.ao)
    rough = 0.74 - spark * 0.42 + ochre * 0.12 + crack * 0.05
    height = t_mid * 0.0018 + t_fine * 0.0005 + (1.0 - g.smooth(0.0, 0.4, gd)) * 0.0003 - crack * 0.003
    return {"base": col, "rough": rough, "metal": spark, "height": height}


def fold_face(g, c, tint=(64, 60, 70), rough=0.32, veins=0.6, origin=(0.0, 0.0, 1.5)):
    """The Foldscar's fracture faces: fine-grained and too smooth for a break, darker than the weathered skin with
    the faintest violet-grey cast, faint hackle ripples fanning from one origin, and the skin's pale veins running
    straight through (same vein field as rock_face, so they line up across the break). No emission."""
    p = c.pos
    t_big, t_mid, t_fine = zn(g, p * 1.2, 51.0), zn(g, p * 8.0, 52.0), zn(g, p * 60.0, 53.0, 2.0)
    col = lin(tint) * g.sat(1.0 + t_big * 0.07 + t_mid * 0.035 + t_fine * 0.02)
    ripple = g.sin(g.length(p - tuple(origin)) * 55.0 + t_big * 1.5) * g.smooth(-0.5, 1.0, t_mid)
    warp = g.noise(p * 0.9, 21.0, detail=2.0, color=True) - (0.5, 0.5, 0.5)
    ev, _ = g.voronoi(p * 2.6 + warp * 1.4, feature="DISTANCE_TO_EDGE")
    vein = (1.0 - g.smooth(0.0, 0.02, ev)) * g.smooth(0.0, 0.8, zn(g, p * 1.1, 28.0)) * veins
    col = g.mix(col, lin(176, 174, 172), vein * 0.6)
    col = col * (0.7 + 0.3 * c.ao)
    return {"base": col, "rough": rough + t_fine * 0.02 + ripple * 0.015 + vein * 0.2, "metal": 0.0,
            "height": ripple * 0.00015 + t_mid * 0.0002 + t_big * 0.0006}


def rock_tile(g, c, kind="greywacke", tint=(1.0, 1.0, 1.0), tile_m=4.0, grain_mm=8.0, grain_mix=0.6, cracks=0.6,
              lichen=0.3, bump_m=0.006, layers_m=0.0):
    """Seamless weathered rock face for large tiled surfaces: periodic in U, V (the Graph's torus noise and periodic
    cells), so one tile_m square repeats without a seam. Mineral grain, mottling, a dark crust, lichen crusts, a
    partly open crack network and (layers_m > 0) faint bedding laminae along V. The mass's per-vertex colours carry
    the large-scale variation the repeat cannot."""
    G = GRAIN[kind]
    T = tile_m
    big, mid, fine = g.z4(2.0, seed=1, detail=3.0), g.z4(8.0, seed=2, detail=3.0), g.z4(40.0, seed=3, detail=2.0)
    n = max(8, int(round(T * 1000.0 / grain_mm)))
    gc = g.cells(g.U, g.V, n, n, T, jx=0.9, seed=5, edge=False)
    mineral = g.mix(lin(G[0]), lin(G[1]), g.lt(gc.h2, G[4]))
    mineral = g.mix(mineral, lin(G[2]), g.lt(gc.h1, G[5]))
    mineral = g.mix(mineral, lin(G[3]), g.gt(gc.h1, 1.0 - G[6]))
    # little broad contrast here: anything a few metres across would mark the repeat (the vertex colours carry it)
    base = g.mix(lin(G[0]) * 0.95, lin(G[1]), g.smooth(-1.5, 1.5, big))
    col = g.mix(base, mineral, grain_mix) * tuple(tint) * g.sat(1.0 + big * 0.035 + mid * 0.07 + fine * 0.03)
    crust = g.smooth(0.5, 1.6, mid + g.z4(5.0, seed=13, detail=3.0) * 0.6)
    col = g.mix(col, lin(96, 92, 84) * g.sat(0.85 + mid * 0.1), crust * 0.22)
    wu, wv = g.warp(0.015, 3.0, seed=6)
    cr = g.cells(wu, wv, 5, 5, T, jx=1.0, seed=7, edge=True)
    open_ = g.smooth(0.3, 1.0, g.z4(1.5, seed=8, detail=3.0)) * cracks
    crack = (1.0 - g.smooth(0.0, 0.012, cr.edge)) * open_
    lip = (1.0 - g.smooth(0.012, 0.04, cr.edge)) * open_ * (1.0 - crack)
    hc = g.cells(wu, wv, 16, 16, T, jx=1.0, seed=9, edge=True)
    hair = (1.0 - g.smooth(0.0, 0.006, hc.edge)) * g.smooth(0.8, 1.4, g.z4(3.0, seed=10, detail=3.0)) * cracks
    lc = g.cells(g.U, g.V, 14, 14, T, jx=1.0, seed=11, edge=False)
    rad = 0.035 + lc.h2 * lc.h2 * 0.1
    spot = (1.0 - g.smooth(rad * 0.75, rad, lc.f1)) * g.lt(lc.h1, lichen * 0.7) * g.smooth(-0.4, 0.8, g.z4(2.0, seed=12))
    lcol = g.mix(lin(LICHEN[0]), lin(LICHEN[1]), g.gt(lc.h3, 0.55))
    col = g.mix(col, lcol * g.sat(0.9 + fine * 0.05), spot * 0.8)
    lam = 0.0
    if layers_m > 0:
        lam = g.sin(g.V * (2.0 * math.pi * max(1, round(T / layers_m))) + big * 0.6) * g.smooth(-0.5, 1.0, mid)
        col = col * (1.0 + lam * 0.04)
    col = g.mix(col, col * 0.18, crack * 0.9)
    col = g.mix(col, col * 0.55, hair * 0.5)
    col = g.mix(col, col * 0.8, lip * 0.4)
    rough = 0.84 + fine * 0.02 + crack * 0.08 + spot * 0.05
    height = (bump_m * (big * 0.3 + mid * 0.4 + fine * 0.15) - crack * 0.01 - hair * 0.0012 - lip * 0.002
              + spot * 0.0004 + lam * 0.0006)
    return {"base": col, "rough": rough, "metal": 0.0, "height": height}


def masonry(g, c, soot_at=(0.0, 0.0, 0.9), soot_radius_m=0.6, soot=0.8, **rock):
    """Dressed stone blocks: rock_face with the weathering turned down, blackened with soot round the fire."""
    r = rock_face(g, c, **rock)
    d = g.length(c.pos - tuple(soot_at)) * (1.0 / soot_radius_m)
    s = (1.0 - g.smooth(0.25, 1.0, d)) * soot * (0.55 + 0.45 * g.smooth(-0.2, 0.8, c.nz))
    r["base"] = g.mix(r["base"], lin(30, 27, 25) * g.sat(0.9 + zn(g, c.pos * 9.0, 60.0) * 0.12), s * 0.85)
    r["rough"] = r["rough"] + s * 0.05
    return r


def coals(g, c, centre=(0.0, 0.0, 0.8), radius_m=0.3, heat=1.0, glow=(255, 92, 22)):
    """A bed of charcoal and coke: black lumps greying to ash on top and at the edges, glowing from the gaps between
    them (where the baked occlusion is deep) and hottest in the middle. 'emit' is the glow (emissive map)."""
    p = c.pos
    t_mid, t_fine = zn(g, p * 20.0, 61.0), zn(g, p * 80.0, 62.0, 2.0)
    rel = p - tuple(centre)
    rx, ry, _ = g.sep(rel)
    r = g.sqrt(rx * rx + ry * ry) * (1.0 / radius_m)
    core = 1.0 - g.smooth(0.15, 1.0, r)
    crevice = g.smooth(0.9, 0.45, c.ao)
    hot = g.sat(core * (0.25 + 0.75 * crevice) + core * core * 0.35 * g.smooth(0.3, 1.2, t_mid)) * heat
    ash = g.smooth(0.3, 1.4, t_mid) * (1.0 - core * 0.8) * g.smooth(0.2, 0.8, c.nz)
    base = g.mix(lin(24, 21, 19), lin(56, 52, 48), g.smooth(-0.5, 1.5, t_fine) * 0.5)
    base = g.mix(base, lin(126, 122, 116), ash * 0.75)
    base = g.mix(base, lin(118, 46, 14), hot * 0.8)
    return {"base": base, "rough": 0.86 - hot * 0.1 + ash * 0.05, "metal": 0.0,
            "height": t_mid * 0.0012 + t_fine * 0.0004, "emit": lin(glow) * (hot * hot)}


def water(g, c, tint=(22, 27, 29), rough=0.06):
    """Still quench water: dark, near-mirror smooth, the faintest ripple and a film of scale at the edges."""
    p = c.pos
    edge = g.smooth(0.9, 0.5, c.ao)
    col = g.mix(lin(tint), lin(52, 46, 40), edge * 0.5)
    return {"base": col, "rough": rough + edge * 0.3, "metal": 0.0, "height": zn(g, p * 14.0, 71.0) * 0.0002}


RECIPES = {"rock_face": rock_face, "iron_ore": iron_ore, "fold_face": fold_face, "rock_tile": rock_tile,
           "masonry": masonry, "coals": coals, "water": water}


def material(spec):
    """A slot's material from its JSON: a template recipe above, or a library recipe by name (iron, plaster, ...)."""
    spec = dict(spec)
    name = spec.pop("recipe")
    return Material(RECIPES.get(name, name), name=spec.pop("name", None), **spec)


# ------------------------------------------------------------------------------------------------ geometry

def smin(a, b, k):
    """Polynomial smooth minimum of distances (k = blend width in metres; k <= 0 is the hard minimum)."""
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b + (a - b) * h - k * h * (1.0 - h)


def unit(v):
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v, axis=-1, keepdims=True)


def lattice(nx, ny, nz_):
    """Box lattice without its bottom face: points in [-1, 1]^3, outward quads with (box face, a, b) grid keys, the
    per-face grid sizes, and the bottom ring counter-clockwise seen from above."""
    keys = [(i, j, k) for k in range(nz_ + 1) for j in range(ny + 1) for i in range(nx + 1)
            if i in (0, nx) or j in (0, ny) or k == nz_]
    ix = {key: n for n, key in enumerate(keys)}
    quads, where = [], []
    for j in range(ny):
        for i in range(nx):
            quads.append((ix[i, j, nz_], ix[i + 1, j, nz_], ix[i + 1, j + 1, nz_], ix[i, j + 1, nz_]))
            where.append((0, i, j))
    for k in range(nz_):
        for j in range(ny):
            quads.append((ix[nx, j, k], ix[nx, j + 1, k], ix[nx, j + 1, k + 1], ix[nx, j, k + 1]))
            where.append((1, j, k))
            quads.append((ix[0, j, k], ix[0, j, k + 1], ix[0, j + 1, k + 1], ix[0, j + 1, k]))
            where.append((2, j, k))
        for i in range(nx):
            quads.append((ix[i, ny, k], ix[i, ny, k + 1], ix[i + 1, ny, k + 1], ix[i + 1, ny, k]))
            where.append((3, i, k))
            quads.append((ix[i, 0, k], ix[i + 1, 0, k], ix[i + 1, 0, k + 1], ix[i, 0, k + 1]))
            where.append((4, i, k))
    ring = ([(i, 0) for i in range(nx)] + [(nx, j) for j in range(ny)] + [(i, ny) for i in range(nx, 0, -1)]
            + [(0, j) for j in range(ny, 0, -1)])
    L = np.array(keys, np.float64) / [nx, ny, nz_] * 2.0 - 1.0
    for q, (f, _, _) in zip(quads[:1] + quads[-4:], where[:1] + where[-4:]):
        nrm = np.cross(L[q[1]] - L[q[0]], L[q[2]] - L[q[0]])
        assert nrm.dot(L[list(q)].mean(axis=0)) > 0, "lattice quad wound inward"
    dims = {0: (nx, ny), 1: (ny, nz_), 2: (ny, nz_), 3: (nx, nz_), 4: (nx, nz_)}
    return L, quads, where, dims, [ix[i, j, 0] for i, j in ring]


def grid_counts(size, budget_tris, grid_m=None):
    """Lattice resolution for a solid of this size: the finest even spacing whose triangles fit the budget."""
    L, W, H = size
    e = grid_m or 0.05
    while True:
        nx, ny, nz_ = (max(4, int(round(s / e))) for s in (L, W, H))
        tris = 2 * (nx * ny + 2 * nz_ * (nx + ny)) + 2 * (nx + ny)
        if tris <= budget_tris or e > 2.0:
            return nx, ny, nz_
        e *= 1.03


def facet_planes(rng, spec, base_radius):
    """Joint planes that cut caps off the base solid, set by set (major joints, then chip scars): (normal, offset
    from the centre, blend, tag). A set's azimuths are spread evenly with jitter so its faces go round the rock."""
    out = []
    for F in spec.get("facets", []):
        count = int(F.get("count", 0))
        az0 = rng.uniform(0.0, 2.0 * math.pi)
        for m in range(count):
            az = az0 + 2.0 * math.pi * (m + rng.uniform(-0.35, 0.35)) / max(count, 1)
            el = math.radians(rng.uniform(*F.get("elevation_deg", (-10.0, 55.0))))
            n = np.array([math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el)])
            r0 = float(base_radius(n[None, :])[0])
            out.append((n, r0 * (1.0 - rng.uniform(*F.get("depth", (0.05, 0.2)))), F.get("blend_m", 0.05),
                        F.get("tag", "facet")))
    for pl in spec.get("planes", []):
        out.append((unit(pl["n"]), pl["offset_m"], pl.get("blend_m", 0.04), pl.get("tag", "plane")))
    return out


def mass_shape(size, centre_z, roundness, taper, sink):
    """Inside test F(p) < 1 of a tapered superellipsoid standing on z = 0 (the whole mass a piece is broken from)."""
    a, b, ht, hb = size[0] / 2.0, size[1] / 2.0, size[2] - centre_z, centre_z * sink

    def F(p):
        x, y, z = p[..., 0], p[..., 1], p[..., 2] - centre_z
        k = 1.0 - taper * np.clip(z / ht, 0.0, 1.0)
        h = np.where(z >= 0.0, ht, hb)
        return (np.abs(x / (a * k)) ** roundness + np.abs(y / (b * k)) ** roundness
                + np.abs(z / h) ** roundness) ** (1.0 / roundness)
    return F


def ray_radius(F, O, D, t_max):
    """Distance along each unit ray D from the interior point O to the surface F = 1 (bisection; F is convex)."""
    lo, hi = np.zeros(len(D)), np.full(len(D), t_max)
    for _ in range(40):
        mid = (lo + hi) / 2.0
        inside = F(O + D * mid[:, None]) < 1.0
        lo, hi = np.where(inside, mid, lo), np.where(inside, hi, mid)
    return lo


def plane_hits(D, planes, r, tag):
    for n, off, blend, t in planes:
        nd = D @ n
        tp = np.where(nd > 1e-6, off / np.maximum(nd, 1e-6), BIG)
        tag = np.where(tp < r - 0.01, t, tag)
        r = smin(r, tp, blend)
    return r, tag


def solid(spec, rng, size, centre_z, budget_tris, cut=(), piece=None):
    """One radial rock solid. Returns positions (n, 3), quads, grid keys, face grids, bottom ring, fan centre, the
    tag of the plane each vertex lies on ('' where the free surface is), and the lattice resolution.
    piece: a piece broken from a larger mass - {"origin": interior point, "F": the mass's inside test, "planes":
    [(n, offset from origin, blend, tag)], "bed": (outward normal, offset) of the plane it stands on, "bed_tag"}; the
    lattice then turns so its open bottom faces the bed, and rays run from the piece's origin."""
    if piece is None:
        L, W, H = size
        a, b = L / 2.0, W / 2.0
        ht, hb = H - centre_z, centre_z
        C = np.array([0.0, 0.0, centre_z])
        p_exp = spec.get("roundness", 3.0)
        taper = spec.get("taper", 0.0)
        hb_ext = hb * spec.get("sink", 2.2)

        def base_radius(D):
            dz = D[:, 2]
            k = 1.0 - taper * np.maximum(dz, 0.0)
            h = np.where(dz >= 0.0, ht, hb_ext)
            return 1.0 / ((np.abs(D[:, 0] / (a * k)) ** p_exp + np.abs(D[:, 1] / (b * k)) ** p_exp
                           + np.abs(dz / h) ** p_exp) ** (1.0 / p_exp))

        planes = facet_planes(rng, spec, base_radius) + list(cut)
        R = np.eye(3)
        bed_n, bed_o, bed_tag = np.array([0.0, 0.0, -1.0]), centre_z, "ground"
    else:
        C = np.asarray(piece["origin"], np.float64)
        planes = list(piece["planes"]) + list(cut)
        bed_n, bed_off = unit(piece["bed"][0]), piece["bed"][1]
        bed_o, bed_tag = bed_off - bed_n @ C, piece.get("bed_tag", "bed")
        zl = -bed_n
        xl = unit(np.cross([0.0, 1.0, 0.0], zl))
        R = np.stack([xl, np.cross(zl, xl), zl], axis=1)

        def base_radius(D):
            return ray_radius(piece["F"], C, D, 2.0 * max(size))

        probe = R @ np.array([[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1]], np.float64).T
        pr_, _ = plane_hits(probe.T, planes, base_radius(probe.T), np.full(5, "", dtype=object))
        a, b, ht, hb = (pr_[0] + pr_[1]) / 2.0, (pr_[2] + pr_[3]) / 2.0, pr_[4], bed_o
        size = (2.0 * a, 2.0 * b, ht + hb)
    nx, ny, nz_ = grid_counts(size, budget_tris, spec.get("grid_m"))
    Lq, quads, where, dims, ring = lattice(nx, ny, nz_)
    q = Lq.copy()
    q[:, 2] = np.where(q[:, 2] < 0.0, q[:, 2] * hb, q[:, 2] * ht)
    D = unit(q * [a, b, 1.0]) @ R.T
    r, tag = plane_hits(D, planes, base_radius(D), np.full(len(D), "", dtype=object))
    P0 = C + D * r[:, None]
    free = np.array([t == "" for t in tag])
    jointed = np.array([t in ("facet", "chip") for t in tag])
    delta = np.zeros(len(D))
    # strata: beds of random thickness along the dip normal; each bed stands in or out, with a groove at its base
    S = spec.get("strata")
    if S:
        dn = unit(S.get("dip_normal", [0, 0, 1]))
        s = (P0 - C) @ dn + nz.fbm3(P0 * 0.35, rng.integers(1 << 30), 3) * S.get("wander_m", 0.08)
        edges = np.cumsum(rng.uniform(*S["thickness_m"], 64)) - 8.0
        bed = np.searchsorted(edges, s)
        relief = rng.uniform(-1.0, 1.0, 66) * S.get("relief_m", 0.04)
        dist = np.minimum(np.abs(s - edges[np.clip(bed - 1, 0, 63)]), np.abs(edges[np.clip(bed, 0, 63)] - s))
        groove = np.clip(1.0 - dist / S.get("groove_w_m", 0.06), 0.0, 1.0) * S.get("groove_m", 0.035)
        side = np.clip(1.0 - np.abs(D[:, 2]) * 1.2, 0.0, 1.0)
        delta += (relief[bed] - groove) * side
    for sp in spec.get("splits", []):
        sn = unit(sp["normal"])
        at = np.asarray(sp["at"], np.float64) + C
        dist = (P0 - at) @ sn
        along = np.linalg.norm((P0 - at) - np.outer(dist, sn), axis=1)
        reach = np.clip(1.0 - along / sp["extent_m"], 0.0, 1.0) ** 0.5 * nz.smoothstep(0.0, 0.45, P0[:, 2])
        v = np.clip(1.0 - np.abs(dist) / sp["width_m"], 0.0, 1.0)
        delta -= v * v * (3.0 - 2.0 * v) * sp["depth_m"] * reach
    for dn in spec.get("dents", []):     # chip scars: spherical scoops about a point given from the centre
        at = np.asarray(dn["at"], np.float64) + C
        u = np.clip(1.0 - np.linalg.norm(P0 - at, axis=1) / dn["radius_m"], 0.0, 1.0)
        delta -= u * u * (3.0 - 2.0 * u) * dn["depth_m"]
    for cb in spec.get("cobbles", []):   # a heap of lumps (coal, rubble): rounded cells with creases between
        f1, f2, _ = nz.voronoi3(P0 / cb["cell_m"], int(rng.integers(1 << 30)))
        delta += (np.clip((f2 - f1) * 2.0, 0.0, 1.0) - 0.5) * cb["amp_m"]
    N_ = spec.get("noise", {})
    seed = int(rng.integers(1 << 30))
    delta += nz.fbm3(P0 * N_.get("freq", 0.6), seed, 4) * N_.get("amp_m", 0.08)
    delta += nz.fbm3(P0 * N_.get("fine_freq", 2.5), seed + 7, 3) * N_.get("fine_amp_m", 0.02)
    delta = np.where(free, delta, delta * np.where(jointed, spec.get("joint_noise", 0.45), spec.get("plane_noise", 0.25)))
    nd = D @ bed_n
    t_bed = np.where(nd > 1e-6, bed_o / np.maximum(nd, 1e-6), BIG)
    rf = np.minimum(np.maximum(r + delta, 0.05 * r), t_bed)
    for n, off, _, t in planes:          # breaks stay planar: nothing the noise moved may cross a fracture
        if t == "fracture":
            nd_ = D @ n
            rf = np.minimum(rf, np.where(nd_ > 1e-6, off / np.maximum(nd_, 1e-6), BIG))
    P = C + D * rf[:, None]
    breaks = [(n, off) for n, off, _, t in planes if t == "fracture"]
    breaks += [(bed_n, bed_o)] if bed_tag == "fracture" else []
    if breaks:
        # a grid only aliases a planar break: snap what lies just inside onto it, then relax the break's vertices
        # within its plane so its outline runs smooth instead of in steps
        snap = spec.get("fracture_snap", 0.6) * min(s / n_ for s, n_ in zip(size, (nx, ny, nz_)))
        Q = np.asarray(quads)
        e = np.concatenate([Q[:, [k, (k + 1) % 4]] for k in range(4)])
        e = np.concatenate([e, e[:, ::-1]])
        cnt = np.bincount(e[:, 0], minlength=len(P))[:, None]
        for n, off in breaks:
            d = (P - C) @ n - off
            P -= np.outer(np.where(d > -snap, d, 0.0), n)
            tag = np.where(d > -snap, "fracture", tag)
            on = np.abs((P - C) @ n - off) < 1e-7
            for _ in range(spec.get("fracture_relax", 4)):
                avg = np.zeros_like(P)
                np.add.at(avg, e[:, 0], P[e[:, 1]])
                avg /= np.maximum(cnt, 1)
                avg -= np.outer((avg - C) @ n - off, n)
                P[on] = avg[on]
    P -= np.outer(np.maximum((P - C) @ bed_n - bed_o, 0.0), bed_n)          # nothing below the bed
    P[ring] -= np.outer((P[ring] - C) @ bed_n - bed_o, bed_n)                # the ring lies on it
    tag = np.where(rf >= t_bed - 1e-9, bed_tag, tag)
    tag[ring] = bed_tag
    return {"P": P, "quads": quads, "where": where, "dims": dims, "ring": ring, "fan": C + bed_n * bed_o,
            "tag": tag, "bed_tag": bed_tag, "grid": (nx, ny, nz_)}


def fit_size(pieces, size):
    """Scale all pieces together so their bounds are exactly size (x, y about the centre, z up from 0)."""
    allp = np.concatenate([pc["P"] for pc in pieces])
    lo, hi = allp.min(axis=0), allp.max(axis=0)
    mid = (lo + hi) / 2.0
    k = np.asarray(size, np.float64) / (hi - lo)
    for pc in pieces:
        pc["P"][:, :2] = (pc["P"][:, :2] - mid[:2]) * k[:2]
        pc["P"][:, 2] = (pc["P"][:, 2] - lo[2]) * k[2]
        pc["fan"] = np.array([(pc["fan"][0] - mid[0]) * k[0], (pc["fan"][1] - mid[1]) * k[1], (pc["fan"][2] - lo[2]) * k[2]])
    return k


def chart_uvs(P, quads, where, dims, cos_max=0.62, extent_m=1.3):
    """Charts of each box face's grid, halved until every quad faces within acos(cos_max) of the chart's mean normal
    and the chart spans at most extent_m; each chart is projected orthographically on its mean normal, right-handed
    seen from outside. Returns per quad (chart key, corner UVs in metres, mean normal z)."""
    Q = np.asarray(quads)
    nrm = np.zeros((len(Q), 3))
    for k in range(4):
        nrm += np.cross(P[Q[:, k]], P[Q[:, (k + 1) % 4]])
    grid = {}
    for qi, (f, u, v) in enumerate(where):
        grid.setdefault(f, {})[(u, v)] = qi
    out = [None] * len(Q)

    def basis(n):
        e1 = np.cross(n, Z) if abs(n[2]) < 0.9 else np.cross(n, [1.0, 0.0, 0.0])
        e1 /= np.linalg.norm(e1)
        return e1, np.cross(n, e1)

    def visit(f, a0, a1, b0, b1):
        ids = [grid[f][(u, v)] for u in range(a0, a1) for v in range(b0, b1)]
        n = nrm[ids].sum(axis=0)
        n /= np.linalg.norm(n)
        un = nrm[ids] / np.maximum(np.linalg.norm(nrm[ids], axis=1, keepdims=True), 1e-12)
        e1, e2 = basis(n)
        pts = P[Q[ids].ravel()]
        span = max(np.ptp(pts @ e1), np.ptp(pts @ e2))
        single = a1 - a0 == 1 and b1 - b0 == 1
        if single or ((un @ n).min() >= cos_max and span <= extent_m):
            key = f"c{f}_{a0}_{b0}"
            for qi in ids:
                out[qi] = (key, [(float(P[i] @ e1), float(P[i] @ e2)) for i in Q[qi]], float(n[2]))
            return
        if a1 - a0 >= b1 - b0:
            m = (a0 + a1) // 2
            visit(f, a0, m, b0, b1)
            visit(f, m, a1, b0, b1)
        else:
            m = (b0 + b1) // 2
            visit(f, a0, a1, b0, m)
            visit(f, a0, a1, m, b1)

    for f, (A, B) in dims.items():
        visit(f, 0, A, 0, B)
    return out


def quad_faces(b, P, q, idx, uv4, **kw):
    """One grid quad as a face, or - when displacement folded it (a corner turning against its normal) - as two
    triangles along the diagonal that keeps both facing out."""
    Q4 = P[list(q)]
    n = sum(np.cross(Q4[k], Q4[(k + 1) % 4]) for k in range(4))
    turns = [np.cross(Q4[k] - Q4[k - 1], Q4[(k + 1) % 4] - Q4[k]) @ n for k in range(4)]
    if min(turns) > 1e-9 * np.dot(n, n):
        b.f([idx[i] for i in q], uv4, **kw)
        return
    split = ((0, 1, 3), (1, 2, 3)) if min(turns[1], turns[3]) < min(turns[0], turns[2]) else ((0, 1, 2), (0, 2, 3))
    for tri in split:
        b.f([idx[q[k]] for k in tri], [uv4[k] for k in tri], **kw)


def emit_solid(b, pc, name, slot_of, group="body", hidden_texel=0.1):
    """Add one solid to the Builder: charted quads (charts lying on the bed get a low texel scale) and the bed fan."""
    b.part(name, group=group, slot=0)
    P = pc["P"]
    idx = [b.v(p) for p in P]
    charts = chart_uvs(P, pc["quads"], pc["where"], pc["dims"])
    ground = pc["bed_tag"] == "ground"
    for q, (key, uv4, nzm) in zip(pc["quads"], charts):
        on_bed = ground and nzm < -0.9 and all(P[i][2] < 0.02 for i in q)
        quad_faces(b, P, q, idx, uv4, island=f"{name}_{key}", slot=slot_of(pc["tag"][list(q)]),
                   texel=hidden_texel if on_bed else None)
    ci = b.v(pc["fan"])
    ring = pc["ring"]
    fan_slot = slot_of(np.array([pc["bed_tag"]] * 3, dtype=object))
    for m in range(len(ring)):
        r0, r1 = ring[m], ring[(m + 1) % len(ring)]
        tri = (ci, idx[r1], idx[r0])
        b.f(tri, [(pc["fan"][0], -pc["fan"][1]), (P[r1][0], -P[r1][1]), (P[r0][0], -P[r0][1])],
            island=f"{name}_bed", slot=fan_slot, texel=hidden_texel if ground else None)
    b.close()


# ------------------------------------------------------------------------------------------------ families

def build_boulder(b, P, rng):
    S = P["shape"]
    size = P["size_m"]
    pc = solid(S, rng, size, S.get("centre_z_frac", 0.42) * size[2], P["tri_budget"] * 0.97)
    k = fit_size([pc], size)
    emit_solid(b, pc, "rock", lambda tags: 0)
    return {"materials": [material(P["material"])],
            "notes": [P.get("note", ""), f"lattice {pc['grid']}, fitted by {np.round(k, 3).tolist()}"],
            "info": {"blocker": P.get("blocker"), "family": "boulder"}}


def rot(yaw_deg=0.0, tilt_deg=0.0, roll_deg=0.0):
    """Rotation: roll about x, then tilt about y, then yaw about z (degrees)."""
    a, t, r = (math.radians(v) for v in (yaw_deg, tilt_deg, roll_deg))
    Rz = np.array([[math.cos(a), -math.sin(a), 0], [math.sin(a), math.cos(a), 0], [0, 0, 1]])
    Ry = np.array([[math.cos(t), 0, math.sin(t)], [0, 1, 0], [-math.sin(t), 0, math.cos(t)]])
    Rx = np.array([[1, 0, 0], [0, math.cos(r), -math.sin(r)], [0, math.sin(r), math.cos(r)]])
    return Rz @ Ry @ Rx


def place(pc, R, at):
    pc["P"] = pc["P"] @ R.T + at
    pc["fan"] = R @ pc["fan"] + at
    return pc


def build_cluster(b, P, rng):
    """Small solids placed one by one (ore lumps, chips, flakes): each a boulder-family solid of its own size and
    shape, turned (yaw, tilt, roll) and set at its point; nothing is rescaled."""
    base = P["shape"]
    budget = P["tri_budget"] * 0.95 / sum(max(l["size"]) ** 2 for l in P["lumps"])
    for i, L in enumerate(P["lumps"]):
        spec = {**base, **L.get("shape", {})}
        pc = solid(spec, rng, L["size"], spec.get("centre_z_frac", 0.45) * L["size"][2],
                   max(200.0, budget * max(L["size"]) ** 2))
        fit_size([pc], L["size"])
        place(pc, rot(L.get("yaw_deg", 0.0), L.get("tilt_deg", 0.0), L.get("roll_deg", 0.0)), np.asarray(L["at"]))
        emit_solid(b, pc, f"lump{i}", lambda tags, s=L.get("slot", 0): s)
    return {"materials": [material(m) for m in P["materials"]],
            "notes": [P.get("note", "")], "info": {"blocker": P.get("blocker"), "family": "cluster"}}


def axis_rot(axis, deg):
    k = unit(axis)
    K = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    a = math.radians(deg)
    return np.eye(3) + math.sin(a) * K + (1.0 - math.cos(a)) * K @ K


def build_fractured(b, P, rng):
    """A standing mass broken along fracture planes: every piece is cut from one shared shape (its joints and noise
    are the mass's, so the skin runs on across a break), rests on the plane below it, and may be set out of
    register - slid along its bed and turned about the bed's normal. Fracture faces take material slot 1."""
    S, size = P["shape"], P["size_m"]
    cz = S.get("centre_z_frac", 0.45) * size[2]
    F = mass_shape(size, cz, S.get("roundness", 3.0), S.get("taper", 0.0), S.get("sink", 2.2))
    M = np.array([0.0, 0.0, cz])
    facets = facet_planes(rng, S, lambda D: ray_radius(F, M, D, 2.0 * max(size)))
    fr = [(unit(f["n"]), float(unit(f["n"]) @ np.asarray(f["through"], np.float64))) for f in P["fractures"]]
    pieces = []
    for pd in P["pieces"]:
        O = np.asarray(pd["origin"], np.float64)
        planes = [(n, o - n @ (O - M), bl, t) for n, o, bl, t in facets]
        cuts = [(s * fr[i][0], s * fr[i][1]) for i, s in pd["cuts"]]
        bed = cuts[pd["bed"]] if pd.get("bed") is not None else (np.array([0.0, 0.0, -1.0]), 0.0)
        planes += [(n, o - n @ O, S.get("fracture_blend_m", 0.012), "fracture")
                   for k, (n, o) in enumerate(cuts) if k != pd.get("bed")]
        pc = solid(S, rng, size, cz, P["tri_budget"] * 0.95 * pd["budget_share"],
                   piece={"origin": O, "F": F, "planes": planes, "bed": bed,
                          "bed_tag": "ground" if pd.get("bed") is None else "fracture"})
        sh = pd.get("shift")
        if sh:
            n = bed[0]
            slide = np.asarray(sh.get("slide_m", (0, 0, 0)), np.float64)
            pivot = pc["fan"].copy()
            pc["P"] -= pivot
            pc["fan"] = pc["fan"] - pivot
            place(pc, axis_rot(n, sh.get("turn_deg", 0.0)), pivot + slide - n * (slide @ n))
        pieces.append(pc)
    k = fit_size(pieces, size)
    for i, pc in enumerate(pieces):
        emit_solid(b, pc, f"piece{i}", lambda tags: 1 if all(t == "fracture" for t in tags) else 0)
    return {"materials": [material(m) for m in P["materials"]],
            "notes": [P.get("note", ""), f"pieces {[pc['grid'] for pc in pieces]}, fitted by {np.round(k, 3).tolist()}"],
            "info": {"blocker": P.get("blocker"), "family": "fractured"}}


def perimeter_s(x, y, A, B, R):
    """Arc length round a rounded rectangle (inner half sizes A, B, corner radius R), counter-clockwise from (A + R, -B)."""
    cx, cy = np.clip(x, -A, A), np.clip(y, -B, B)
    th = np.mod(np.arctan2(y - cy, x - cx), 2.0 * math.pi)
    q = math.pi / 2.0
    s = np.select(
        [(x >= A) & (np.abs(y) <= B), (x > A) & (y > B), (np.abs(x) <= A) & (y >= B), (x < -A) & (y > B),
         (x <= -A) & (np.abs(y) <= B), (x < -A) & (y < -B), (np.abs(x) <= A) & (y <= -B)],
        [y + B, 2 * B + R * th, 2 * B + R * q + (A - x), 2 * B + 2 * A + R * q + R * (th - q),
         2 * B + 2 * A + 2 * R * q + (B - y), 4 * B + 2 * A + 2 * R * q + R * (th - 2 * q),
         4 * B + 2 * A + 3 * R * q + (x + A)],
        4 * B + 4 * A + 3 * R * q + R * (th - 3 * q))
    return s, 4 * A + 4 * B + 2 * math.pi * R


def build_mass(b, P, rng):
    """A rock mass built to a box blocker: a rounded box whose sides are jointed into blocks along bedding planes and
    vertical joints (each block set in or out and tilted a little, grooves at the joints), held within side_band_m
    of the box faces; an irregular top. Tiled: the sides map arc length round the perimeter by height (a whole
    number of tiles round, so no seam), the top maps x, y; per-vertex colours carry the large-scale variation."""
    S = P["shape"]
    tgt = np.asarray(P["size_m"], np.float64)
    a, bb, H = tgt[0] / 2.0, tgt[1] / 2.0, tgt[2] - S.get("top_rise_m", 0.2)
    R = S.get("corner_m", 0.8)
    nx, ny, nz_ = grid_counts((tgt[0], tgt[1], H), P["tri_budget"] * 0.97, S.get("grid_m"))
    Lq, quads, where, dims, ring = lattice(nx, ny, nz_)
    Pb = np.c_[Lq[:, 0] * a, Lq[:, 1] * bb, (Lq[:, 2] + 1.0) / 2.0 * H]
    Qc = np.clip(Pb, [-a + R, -bb + R, -1e9], [a - R, bb - R, H - R])
    nrm = unit(Pb - Qc)
    Pr = Qc + R * nrm
    side = np.clip((0.8 - nrm[:, 2]) / 0.4, 0.0, 1.0)
    s, perim = perimeter_s(Pr[:, 0], Pr[:, 1], a - R, bb - R, R)
    # blocks: bedding by height (wandering a little), vertical joints along the perimeter
    seed = int(rng.integers(1 << 30))
    zb = Pr[:, 2] + nz.fbm3(Pr * 0.2, seed + 11, 3) * S.get("bed_wander_m", 0.3)
    beds = np.cumsum(rng.uniform(*S["bed_m"], 40))
    bi = np.clip(np.searchsorted(beds, zb), 0, 40)
    band = S.get("side_band_m", 0.28)
    # each bed stands in or out by an amount that drifts along the wall; joints wander and vary in depth
    bed_off = rng.uniform(-0.75, -0.1, 41)[bi] * band
    drift = nz.fbm3(np.c_[s * S.get("ledge_drift_freq", 0.22), bi * 7.3, np.zeros(len(s))], seed + 12, 3)
    sj = s + nz.fbm3(Pr * 0.3, seed + 13, 3) * S.get("joint_wander_m", 0.8)
    joints = np.cumsum(rng.uniform(*S["joint_m"], 200))
    ji = np.clip(np.searchsorted(joints, sj), 0, 199)
    jdepth = rng.uniform(0.2, 1.0, 201)[ji]
    d_bed = np.minimum(np.abs(zb - np.r_[0.0, beds][bi]), np.abs(beds[np.clip(bi, 0, 39)] - zb))
    d_jnt = np.minimum(np.abs(sj - np.r_[0.0, joints][ji]), np.abs(joints[ji] - sj))
    groove = (np.clip(1.0 - d_bed / S.get("bed_groove_w_m", 0.12), 0.0, 1.0) * S.get("bed_groove_m", 0.1)
              + np.clip(1.0 - d_jnt / S.get("joint_groove_w_m", 0.2), 0.0, 1.0) * S.get("joint_groove_m", 0.12)
              * jdepth)
    d_side = (bed_off + drift * band * 0.9 - groove + nz.fbm3(Pr * 0.35, seed + 14, 4) * S.get("broad_noise_m", 0.1)
              + nz.fbm3(Pr * 1.1, seed, 3) * S.get("noise_m", 0.04))
    d_side = np.clip(d_side, -band, 0.0)
    # the irregular top is a height field over x, y that stretches each vertical column (so it cannot fold over
    # the rounded top edge); only the sides move along their normal
    T_ = S.get("top", {})
    xy0 = np.c_[Pr[:, 0], Pr[:, 1], np.zeros(len(Pr))]
    d_top = (nz.fbm3(xy0 * T_.get("freq", 0.14), seed + 1, 4) * T_.get("amp_m", 1.2) - T_.get("drop_m", 0.25)
             + nz.fbm3(xy0 * 0.5, seed + 2, 3) * T_.get("step_m", 0.15))
    d_top = np.clip(d_top, -T_.get("max_drop_m", 1.1), S.get("top_rise_m", 0.2))
    delta = side * d_side + (1.0 - side) * nz.fbm3(Pr * 1.1, seed + 3, 3) * S.get("noise_m", 0.04)
    Pd = Pr + nrm * delta[:, None]
    Pd[:, 2] *= (H + d_top) / H
    Pd[ring, 2] = 0.0
    pc = {"P": Pd, "fan": np.array([0.0, 0.0, 0.0])}
    k = fit_size([pc], tgt)
    Pd = pc["P"]
    # world-scale UVs (metres): sides by arc length (a whole number of tiles round) and height, top by x, y
    tile = P["tile"]["tile_m"]
    ku = (perim / max(1, round(perim / tile))) / tile
    su = s / ku
    b.part("mass", group="body", slot=0)
    idx = [b.v(p) for p in Pd]
    qn = np.array([nrm[list(q)].mean(axis=0) for q in quads])
    for q, n_ in zip(quads, qn):
        q = list(q)
        if n_[2] > 0.7:
            quad_faces(b, Pd, q, idx, [(Pd[i][0], Pd[i][1]) for i in q], island="top")
            continue
        u = su[q].copy()
        if u.max() - u.min() > perim / ku / 2.0:
            u[u < perim / ku / 2.0] += perim / ku
        quad_faces(b, Pd, q, idx, [(u[j], Pd[i][2]) for j, i in enumerate(q)], island="side")
    ci = b.v(pc["fan"])
    for m in range(len(ring)):
        r0, r1 = ring[m], ring[(m + 1) % len(ring)]
        b.f((ci, idx[r1], idx[r0]), [(0.0, 0.0), (Pd[r1][0], -Pd[r1][1]), (Pd[r0][0], -Pd[r0][1])], island="bed")
    b.close()
    # per-vertex colour: broad tone, darker in recesses and joints, damp at the foot, rain streaks, lichen on top
    V = Pd
    C_ = P.get("colour", {})
    tint = np.asarray(C_.get("tint", [1.0, 1.0, 1.0]))
    tone = 1.0 + nz.fbm3(V * 0.12, seed + 3, 3) * C_.get("tone", 0.35)
    rec = np.clip(1.0 + (delta - delta.mean()) * side * C_.get("recess", 1.2), 0.6, 1.1)
    damp = nz.smoothstep(0.9, 0.0, V[:, 2]) * C_.get("damp", 0.25)
    streak = np.clip(nz.fbm3(np.c_[V[:, 0] * 0.9, V[:, 1] * 0.9, V[:, 2] * 0.07], seed + 4, 3) * 2.2, 0.0, 1.0)
    moss = (1.0 - side) * np.clip(nz.fbm3(V * 0.18, seed + 5, 4) * 1.6 + 0.45, 0.0, 1.0) * C_.get("moss", 0.5)
    col = np.outer(tone * rec * (1.0 - damp) * (1.0 - streak * side * C_.get("streaks", 0.25)), tint)
    col = col * (1.0 - moss[:, None]) + moss[:, None] * np.array(C_.get("moss_tint", [0.78, 0.86, 0.66])) * tone[:, None]
    col = np.vstack([np.clip(col, 0.05, 1.0), [[0.5, 0.5, 0.5]]])
    tile_mat = material(P["tile"]["material"])
    return {"materials": [tile_mat], "tiled": {"tile_m": tile, "material": tile_mat}, "vertex_colours": col,
            "notes": [P.get("note", ""), f"lattice {(nx, ny, nz_)}, fitted by {np.round(k, 4).tolist()}, perimeter "
                      f"{perim:.2f} m = {round(perim / tile)} tiles"],
            "info": {"blocker": P.get("blocker"), "family": "mass", "box_m": P.get("box_m"),
                     "side_band_m": band, "perimeter_m": perim}}


def build_masonry(b, P, rng):
    """masonry_block mode: dressed-stone courses, bonded at the corners (odd and even courses swap which rows run
    through), on a mortar core, with capstones round openings in the top. As a hearth: a fire pit holding a lumpy
    coal bed (glowing, material slot 3) and lumps, an iron rim lining its mouth, a tuyere through the wall on the
    bellows side, and a quench bosh of still water behind a stone partition.
    Slots: 0 stone, 1 mortar, 2 iron, 3 coal, 4 water."""
    M = P["masonry"]
    X, Y = M["footprint_m"]
    t, gap, ch = M["wall_m"], M.get("joint_m", 0.008), M.get("chamfer_m", 0.012)
    STONE, MORTAR, IRON, COAL, WATER = range(5)
    Xa, Za = np.array([1.0, 0.0, 0.0]), np.array([0.0, 0.0, 1.0])

    def block(name, c, size, slot=STONE, jitter=True, axis=0):
        """A chamfered block centred at c: size (along the axis, across, height); axis 0 = x, 1 = y; hand-laid
        blocks sit a little off true."""
        yaw = (math.pi / 2 if axis else 0.0) + (math.radians(rng.normal(0.0, 0.5)) if jitter else 0.0)
        c = np.asarray(c, np.float64) + (np.r_[rng.normal(0.0, 0.002, 2), 0.0] if jitter else 0.0)
        pr.beam(b, name, -size[0] / 2, size[0] / 2, size[1] / 2, size[2] / 2,
                pr.frame_from(c, [math.cos(yaw), math.sin(yaw), 0.0], Za), cs=ch, ce=ch, grain=False, slot=slot)

    def row(name, a0, a1, fixed, axis, zc, h, depth):
        """A course's row of blocks from a0 to a1 along x (axis 0) or y (axis 1), random lengths, mortar gaps."""
        n = max(1, int(round((a1 - a0) / rng.uniform(*M.get("block_len_m", (0.3, 0.42))))))
        w = rng.uniform(0.8, 1.2, n)
        pos = a0
        for i, ln in enumerate(w / w.sum() * (a1 - a0 - (n - 1) * gap)):
            c = (pos + ln / 2, fixed, zc) if axis == 0 else (fixed, pos + ln / 2, zc)
            block(f"{name}{i}", c, (ln, depth, h - gap), axis=axis)
            pos += ln + gap

    z = 0.0
    for k, h in enumerate(M["courses_m"]):
        zc = z + h / 2
        ex, ey = (X / 2, Y / 2 - t - gap) if k % 2 == 0 else (X / 2 - t - gap, Y / 2)
        for s in (1, -1):
            row(f"c{k}_fb{s}", -ex, ex, s * (Y / 2 - t / 2), 0, zc, h, t)
            row(f"c{k}_lr{s}", -ey, ey, s * (X / 2 - t / 2), 1, zc, h, t)
        z += h
    top = z
    bed_z, fire, bosh = M["bed_z_m"], M["fire_x_m"], M["bosh_x_m"]
    oy = Y / 2 - t
    pr.beam(b, "core", -X / 2 + 0.05, X / 2 - 0.05, Y / 2 - 0.05, bed_z / 2, pr.frame_from((0, 0, bed_z / 2), Xa, Za),
            cs=0.004, ce=0.004, grain=False, slot=MORTAR)
    for s in (1, -1):        # mortar backing inside the walls above the core, so no joint shows the fire through
        block(f"back_fb{s}", (0.0, s * (Y / 2 - t / 2), (bed_z + top) / 2 - 0.01),
              (X - 0.1, t * 0.5, top - bed_z + 0.02), slot=MORTAR, jitter=False)
        block(f"back_lr{s}", (s * (X / 2 - t / 2), 0.0, (bed_z + top) / 2 - 0.01),
              (Y - 0.1, t * 0.5, top - bed_z + 0.02), slot=MORTAR, jitter=False, axis=1)
    block("partition", ((bosh[1] + fire[0]) / 2, 0.0, (bed_z + top) / 2),
          (fire[0] - bosh[1] - 2 * gap, 2 * oy - 2 * gap, top - bed_z - gap), jitter=False)
    # capstones: a projecting cope round the openings
    cap, over = M["cap_m"], M.get("cap_over_m", 0.02)
    zc = top + cap / 2
    for s in (1, -1):
        block(f"cap_fb{s}", (0.0, s * (oy + (Y / 2 + over - oy) / 2 + gap / 2), zc),
              (X + 2 * over, Y / 2 + over - oy - gap, cap))
    for name, x0, x1 in (("cap_w", -X / 2 - over, bosh[0]), ("cap_mid", bosh[1], fire[0]), ("cap_e", fire[1], X / 2 + over)):
        block(name, ((x0 + x1) / 2, 0.0, zc), (x1 - x0 - 2 * gap, 2 * oy - 2 * gap, cap))
    # the bosh: still water behind the partition
    wz = M["water_z_m"]
    pr.beam(b, "water", bosh[0] + 0.004, bosh[1] - 0.004, oy - 0.004, (wz - bed_z) / 2,
            pr.frame_from((0.0, 0.0, (wz + bed_z) / 2), Xa, Za), cs=0.002, ce=0.002, grain=False, slot=WATER)
    # the iron rim lining the fire pit's mouth, standing a little proud of the cope
    rim_h, rim_t = M.get("rim_m", (0.055, 0.012))
    rz = top + cap + 0.015 - rim_h / 2
    ox, oyy = rim_t / 2, oy - rim_t / 2 + 0.002
    pr.poly_sweep(b, "rim", [(fire[0] + ox, -oyy, rz), (fire[1] - ox, -oyy, rz), (fire[1] - ox, oyy, rz),
                             (fire[0] + ox, oyy, rz)], Za, rim_t, rim_h, c=0.002, closed=True, slot=IRON)
    # tuyere: the bellows' pipe through the east wall into the bottom of the fire
    tz = bed_z + M.get("tuyere_up_m", 0.07)
    pr.tube(b, "tuyere", [(X / 2 + 0.004, 0.0, tz), (fire[1] - 0.03, 0.0, tz)], M.get("tuyere_r_m", 0.03), n=10,
            slot=IRON)
    pr.lathe(b, "tuyere_flange", (X / 2 - 0.01, 0.0, tz), Xa, [(0.0, 0.058), (0.012, 0.058), (0.02, 0.036)], n=12,
             slot=IRON)
    # the fire: a lumpy coal bed filling the pit, and loose lumps on it
    C_ = P["coal"]
    mound = {**C_["mound_shape"]}
    size = ((fire[1] - fire[0]) - 0.02, 2 * oy - 0.02, C_["mound_h_m"])
    pc = solid(mound, rng, size, mound.get("centre_z_frac", 0.3) * size[2], C_["mound_tris"])
    fit_size([pc], size)
    place(pc, np.eye(3), np.array([(fire[0] + fire[1]) / 2, 0.0, bed_z]))
    emit_solid(b, pc, "coalbed", lambda tags: COAL)
    for i in range(C_["lumps"]):
        s_ = rng.uniform(*C_["lump_m"])
        sz = (s_, s_ * rng.uniform(0.7, 1.0), s_ * rng.uniform(0.55, 0.8))
        spec = {**C_["lump_shape"]}
        pl = solid(spec, rng, sz, 0.45 * sz[2], 180)
        fit_size([pl], sz)
        u, v = rng.uniform(-0.38, 0.38, 2)
        at = np.array([(fire[0] + fire[1]) / 2 + u * (fire[1] - fire[0]), v * 2 * oy, 0.0])
        at[2] = bed_z + C_["mound_h_m"] * (1.0 - min(1.0, (u * u + v * v) * 3.0)) * 0.85
        place(pl, rot(rng.uniform(0, 360), rng.normal(0, 12), rng.normal(0, 12)), at)
        emit_solid(b, pl, f"coal{i}", lambda tags: COAL)
    return {"materials": [material(m) for m in P["materials"]], "emissive_strength": P.get("emissive_strength", 1.0),
            "notes": [P.get("note", "")], "info": {"blocker": P.get("blocker"), "family": "masonry", "top_m": top + cap}}


FAMILIES = {"boulder": build_boulder, "cluster": build_cluster, "fractured": build_fractured, "mass": build_mass,
            "masonry": build_masonry}


def build(b, P, rng):
    return FAMILIES[P["family"]](b, P, rng)


def load_instance(argv):
    """--asset <id> picks assets/manifests/procgen/<id>.json; the rest of argv goes to the runner."""
    if "--asset" not in argv:
        raise SystemExit("usage: ... _procgen_rock.py -- --asset <asset id> [runner options]")
    i = argv.index("--asset")
    asset_id, rest = argv[i + 1], argv[:i] + argv[i + 2:]
    path = os.path.join(PARAM_DIR, asset_id + ".json")
    with open(path, encoding="utf-8") as handle:
        params = json.load(handle)
    return asset_id, params, path, rest


if __name__ == "__main__":
    ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ASSET, PARAMS, PATH, REST = load_instance(ARGV)
    run_template(build, PARAMS, ASSET, archetype=PARAMS.get("archetype", "rock_outcrop"), concept=PARAMS.get("concept"),
                 tri_budget=PARAMS["tri_budget"], res=PARAMS.get("res", 2048), px_per_m=PARAMS.get("px_per_m", 512),
                 ao_distance=PARAMS.get("ao_distance_m", 0.5), instance_files=[PATH], argv=REST)
