"""Parametric material recipes for baked props: one implementation per material family, parameters for the rest.

Unifies the per-generator shaders: sh_pine (_procgen_crate.py: 3D rings round a drifting pith line, knots, saw
marks), sh_oak (_procgen_chest.py: ring-porous pores, rays, dark finish), sh_wood/sh_break/sh_rope/sh_cloth
(_procgen_cart.py: silvered weathering, checks, lichen, splinters, laid rope, canvas) and the iron shaders of
chest/crate/cart and the world baker's cast_iron (_blender_bake_world_materials.py). Iron is corrected for PBR:
bare metal is metallic 1 with a metal-range base colour (sRGB ~150-180); mill/hammer scale and rust are dielectric
(metallic 0). The world baker's cast_iron gives its dark scale metallic 0.6-1.0 with base sRGB 40-68, and the prop
shaders gave blackened iron metallic ~0.8 at sRGB 34-78: a metal that dark reflects less than any real metal.

Every recipe is f(g, c, **params) -> {'base': linear rgb, 'rough', 'metal', 'height' (metres, for the bump)} over a
shadergraph.Graph g and a surface_context c. Colour parameters are sRGB 0-255 triples.

API
    wood(g, c, species='oak'|'pine'|'ash'|'elm'|'beech', age=0.35, paint=None, paint_cover=0.7, grain_scale=1.0,
         finish='raw'|'oiled'|'dark', end_grain=True)      age 0 new-sawn .. 1 silver-grey, checked, lichened
    iron(g, c, kind='cast'|'forged'|'wrought', rust=0.35)
    rope(g, c, colour=(150, 128, 92), pitch=0.024, strands=3, dirt=0.3)          hemp; needs a tube/sweep part
    leather(g, c, tan=0.5, wear=0.3)
    canvas(g, c, colour=(150, 138, 112), weave=180, dirt=0.3)                     cloth; weave in member x, y
    stone(g, c, kind='granite'|'limestone'|'rubble'|'slate', weathering=0.4, lichen=0.3)
    plaster(g, c, tint=(178, 168, 148), dirt=0.3, cracks=0.3)
    fresh_break(g, c, species='pine')                                             splintered or freshly broken wood
    RECIPES {name: fn};  SPECIES, IRON_KINDS, STONE_KINDS: the parameter tables
    Material(recipe, name=None, **params)   a slot's material: .build(g, c), .flat_colour/.flat_metal (--no-bake)
"""
import math

from .shadergraph import lin

TAU = 2.0 * math.pi

# Early/late wood colours (sRGB), ring width (m), latewood band, and how much of each feature the species shows.
SPECIES = {
    "pine": {"early": (222, 186, 134), "late": (168, 106, 50), "ring": 0.0050, "late_band": (0.58, 0.86),
             "knots": 1.0, "pores": 0.0, "rays": 0.0, "figure": 0.9},
    "oak": {"early": (140, 104, 70), "late": (86, 60, 40), "ring": 0.0030, "late_band": (0.45, 0.80),
            "knots": 0.25, "pores": 1.0, "rays": 1.0, "figure": 0.65},
    "ash": {"early": (208, 182, 142), "late": (158, 126, 88), "ring": 0.0036, "late_band": (0.50, 0.85),
            "knots": 0.2, "pores": 0.8, "rays": 0.2, "figure": 0.7},
    "elm": {"early": (164, 118, 80), "late": (106, 70, 46), "ring": 0.0032, "late_band": (0.50, 0.85),
            "knots": 0.35, "pores": 0.7, "rays": 0.1, "figure": 0.7},
    "beech": {"early": (198, 154, 112), "late": (172, 124, 88), "ring": 0.0040, "late_band": (0.55, 0.9),
              "knots": 0.1, "pores": 0.0, "rays": 0.6, "figure": 0.5},
}
FINISH = {"raw": (1.0, 0.0), "oiled": (0.78, -0.12), "dark": (0.42, -0.06)}   # colour factor, roughness shift

IRON_KINDS = {   # oxide scale colour, bare metal colour (metal-range), scale cover, hammer dimples, slag fibre, sand skin
    "cast": {"scale": (46, 45, 44), "bare": (152, 151, 148), "cover": 0.92, "hammer": 0.0, "fibre": 0.0, "sand": 1.0},
    "forged": {"scale": (38, 37, 38), "bare": (172, 169, 164), "cover": 0.82, "hammer": 1.0, "fibre": 0.0, "sand": 0.0},
    "wrought": {"scale": (48, 46, 45), "bare": (176, 172, 166), "cover": 0.74, "hammer": 0.4, "fibre": 1.0, "sand": 0.0},
}

STONE_KINDS = {
    "granite": {"tones": [(0.0, (118, 112, 108)), (0.5, (150, 142, 136)), (1.0, (176, 164, 156))],
                "speck": (38, 36, 36), "speck_share": 0.22, "grain": 420.0, "rough": 0.72},
    "limestone": {"tones": [(0.0, (158, 150, 130)), (0.5, (176, 166, 144)), (1.0, (188, 180, 160))],
                  "speck": (120, 112, 96), "speck_share": 0.05, "grain": 160.0, "rough": 0.86},
    "rubble": {"tones": [(0.0, (84, 80, 74)), (0.35, (108, 104, 96)), (0.7, (122, 108, 90)), (1.0, (132, 128, 120))],
               "speck": (52, 50, 48), "speck_share": 0.08, "grain": 220.0, "rough": 0.84},
    "slate": {"tones": [(0.0, (56, 60, 68)), (0.5, (70, 76, 86)), (1.0, (78, 80, 84))],
              "speck": (44, 46, 50), "speck_share": 0.03, "grain": 300.0, "rough": 0.64},
}


def _near_ground(c):
    return 1.0 - c.low


def wood(g, c, species="oak", age=0.35, paint=None, paint_cover=0.7, grain_scale=1.0, finish="raw", end_grain=True):
    S = SPECIES[species]
    fin_col, fin_rough = FINISH[finish]
    x, y, z, r0 = c.lx, c.ly, c.lz, c.rand
    A = g.sat(c.wear * 0.5 + age)
    ring = c.ringf * (S["ring"] * grain_scale)
    # the log's axis is never quite along the board: the pith line drifts and wanders through it
    wy = g.noise(g.vec(x * 0.7, r0 * 31.0, 1.7), 0.3, detail=2.0) - 0.5
    wz = g.noise(g.vec(x * 0.7, r0 * 37.0, 5.3), 0.6, detail=2.0) - 0.5
    dy = y - c.py0 - c.sy * x - wy * 0.02
    dz = z - c.pz0 - c.sz * x - wz * 0.02
    rr = g.sqrt(dy * dy + dz * dz)
    pl = g.sqrt(c.py0 * c.py0 + c.pz0 * c.pz0) + 1e-4
    cs, sn = -c.py0 / pl, -c.pz0 / pl
    th = g.atan2(dz * cs - dy * sn, dy * cs + dz * sn)        # angle round the pith, 0 across the member (no seam)
    # knots: one candidate per (along the grain, round the pith) cell; only some cells carry a branch
    kd_s, kcol = g.voronoi(g.vec(x + r0 * 37.0, th * 0.2, 0.0), scale=4.0, dims="2D")
    kd = kd_s * 0.25
    kc_r, kc_g, kc_b = g.sep(kcol)
    kr = g.max((0.007 + 0.009 * kc_g) * g.smooth(0.015, 0.08, rr), 0.001)
    kden = c.kden * S["knots"]
    act = 1.0 - g.smooth(kden - 0.02, kden + 0.02, kc_r)
    core = act * (1.0 - g.smooth(kr * 0.8, kr, kd))
    swirl = act * (1.0 - g.smooth(kr * 0.5, kr * 3.2, kd))
    rim = act * (1.0 - g.smooth(0.0, 0.0016, g.abs(kd - kr))) * g.smooth(0.4, 0.6, kc_b)   # dead knots ring dark
    phase = rr / ring + g.noise(g.vec(x * 3.0, y * 60.0, z * 60.0), 1.3, detail=2.0) * 0.3 + swirl * 1.6
    f = g.fract(phase)
    lb0, lb1 = S["late_band"]
    lw = g.smooth(lb0, lb1, f) * (1.0 - g.smooth(0.94, 0.995, f))
    lw = lw * (0.5 + 0.5 * g.noise(g.vec(g.floor(phase) * 0.71, r0 * 13.0, 0.5), 2.0, detail=0.0))
    early = 1.0 - g.smooth(0.0, 0.3, f)
    fibre = g.noise(g.vec(x * 3.0, y * 420.0, z * 420.0), 3.0, detail=2.0)
    streak = g.noise(g.vec(x * 0.5, y * 45.0, z * 45.0), 4.0, detail=3.0)
    pores = early * g.smooth(0.52, 0.68, g.noise(g.vec(x * 5.0, y * 700.0, z * 700.0 + r0 * 3.0), 5.0, detail=1.0))
    pores = pores * S["pores"]
    rays = g.smooth(0.70, 0.78, g.noise(g.vec(x * 30.0, y * 260.0, z * 260.0 + r0 * 41.0), 7.0, detail=1.0))
    rays = rays * (S["rays"] * (1.0 - lw))
    tint = g.ramp(r0, [(0.0, (0.94, 0.92, 0.85)), (0.3, (0.98, 0.94, 0.88)), (0.6, (0.92, 0.87, 0.8)),
                       (1.0, (1.0, 0.94, 0.9))], space="linear")
    base = g.mix(lin(S["early"]), lin(S["late"]), lw * S["figure"])
    base = base * tint * (0.88 + streak * 0.24) * (0.93 + fibre * 0.14)
    base = g.mix(base, lin(S["late"]) * 0.55, pores * 0.45)
    base = g.mix(base, lin(S["early"]) * 1.1, rays * 0.2)
    kc = g.mix(lin(S["late"]) * 0.9, lin(S["late"]) * 0.6, g.sin(kd / kr * 13.0) * 0.5 + 0.5)
    base = g.mix(base, kc, core)
    base = g.mix(base, lin(60, 36, 20), rim * 0.7)
    endg = c.pend if end_grain else 0.0
    base = base * (1.0 - endg * 0.3) * (1.0 - (1.0 - g.smooth(0.0, 0.08, f)) * endg * 0.25)
    base = base * fin_col
    # age: greying where the weather reaches, handling grime, dirt low down, occlusion grime, worn edges, dents,
    # checks along the grain and lichen on the oldest wood
    near = _near_ground(c)
    blot = g.noise(c.pos * 2.5, 5.0, detail=4.0)
    base = base * (1.0 - (0.10 + 0.22 * A) * g.smooth(0.4, 0.8, blot))
    sky = g.sat(c.nz * 0.5 + 0.5)
    grey = (0.05 + 0.5 * A * A) * (0.55 + 0.45 * sky) * (0.4 + 0.6 * c.ao)
    base = g.mix(base, lin(150, 146, 136) * (0.75 + 0.25 * fin_col), grey)
    dirt = near * (0.6 + 0.4 * g.noise(c.pos * 8.0, 6.0, detail=3.0))
    base = g.mix(base, lin(92, 78, 60), dirt * (0.2 + 0.35 * A))
    k = 0.6 - 0.25 * A
    base = base * (k + (1.0 - k) * c.ao)
    base = g.mix(base, lin(S["early"]) * (0.8 + 0.2 * fin_col), c.cvx * 0.3)
    dv, dcol = g.voronoi(c.pos, scale=60.0)
    dr, _, _ = g.sep(dcol)
    dent = (1.0 - g.smooth(0.0, 0.22, dv)) * (1.0 - g.smooth(0.10, 0.12, dr)) * (0.4 + 0.6 * c.edge)
    base = base * (1.0 - dent * 0.12)
    cn = g.noise(g.vec(x * 0.8, th * 6.0 + r0 * 41.0, 0.3), 6.0, detail=2.0)
    cmask = g.smooth(0.46, 0.64, g.noise(g.vec(x * 0.7, th * 1.5, r0 * 43.0), 7.0, detail=2.0))
    crack = (1.0 - g.smooth(0.0, 0.024, g.abs(cn - 0.5))) * cmask * g.smooth(0.4, 0.8, A) * (1.0 - endg)
    base = g.mix(base, lin(30, 26, 22), crack * 0.9)
    lich = ((1.0 - g.smooth(0.05, 0.13, g.voronoi(c.pos * 13.0)[0])) * g.smooth(0.58, 0.72, g.noise(c.pos * 3.0, 8.0))
            * g.smooth(0.3, 0.8, c.nz) * g.smooth(0.65, 0.95, A))
    base = g.mix(base, lin(112, 116, 94), lich * 0.5)
    saw = g.sin((x * 80.0 + g.noise(g.vec(x * 2.0, y * 3.0, z * 3.0), 7.0) * 3.0) * TAU) * (1.0 - endg) * (1.0 - A)
    rough = (0.70 - lw * 0.06 + fibre * 0.06 + endg * 0.12 + near * 0.1 - c.cvx * 0.1 + core * 0.04 + A * 0.08
             + crack * 0.05 + pores * 0.06 + fin_rough)
    height = (lw * (0.00016 + 0.0004 * A) + fibre * 0.00005 + streak * 0.00005 + core * 0.0001 - rim * 0.00025
              + saw * 0.00005 - dent * 0.0004 + blot * 0.00005 - pores * 0.00018 - crack * 0.0024 + lich * 0.0002)
    if paint is not None:
        pn = g.noise(c.pos * 6.0, 9.0, detail=4.0)
        keep = g.smooth(1.0 - paint_cover - 0.06, 1.0 - paint_cover + 0.06, pn) * (1.0 - c.cvx * 0.85)
        keep = keep * (1.0 - endg)
        paint_col = lin(paint) * (0.92 + fibre * 0.08) * (0.6 + 0.4 * c.ao)
        base = g.mix(base, g.mix(paint_col, lin(150, 146, 136), grey * 0.6), keep)
        rough = g.mix(rough, 0.55 + A * 0.2, keep)
        height = height + keep * 0.00015
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def iron(g, c, kind="wrought", rust=0.35):
    K = IRON_KINDS[kind]
    p, r = c.pos, c.rand
    n1 = g.noise(p * 16.0, 1.0 + r, detail=4.0)
    n2 = g.noise(p * 55.0, 2.0, detail=4.0)
    n3 = g.noise(p * 240.0, 3.0, detail=2.0)
    dimple, _ = g.voronoi(p * 60.0)
    pits = 1.0 - g.smooth(0.0, 0.12, g.voronoi(p * 150.0)[0])
    slag = g.smooth(0.55, 0.75, g.noise(g.vec(c.lx * 4.0, c.ly * 300.0, c.lz * 300.0), 4.0, detail=3.0)) * K["fibre"]
    sand = g.noise(p * 600.0, 5.0, detail=2.0) * K["sand"]
    near = _near_ground(c)
    mott = g.smooth(0.42, 0.62, n1 * 0.7 + n2 * 0.3)
    t = 0.78 - 0.36 * rust
    rustm = g.smooth(t - 0.08, t + 0.08, n2 * 0.45 + n1 * 0.25 + (1.0 - c.ao) * 0.45 + near * 0.12)
    worn = g.max(c.cvx * (0.75 + 0.25 * n3), g.smooth(0.70, 0.84, n1 * 0.6 + n2 * 0.4) * (1.0 - K["cover"]))
    bare = g.sat(worn * (1.0 - rustm) * (1.0 - slag * 0.5))
    scale = g.mix(lin(K["scale"]) * 0.8, lin(K["scale"]) * 1.2, mott * 0.75 + n3 * 0.15)
    scale = g.mix(scale, lin(32, 31, 30), slag * 0.6)
    rust_col = g.mix(lin(70, 44, 30), lin(112, 66, 38), n3)
    base = g.mix(scale, rust_col, rustm * 0.9)
    base = g.mix(base, lin(26, 23, 21), pits * 0.5 * rustm)
    base = base * (0.45 + 0.55 * c.ao)                  # dielectric scale and rust carry their grime
    bare_col = lin(K["bare"]) * (0.92 + n3 * 0.16) * (0.8 + 0.2 * c.ao)
    base = g.mix(base, bare_col, bare)
    rough = g.mix(g.mix(0.66 + n3 * 0.08 + (1.0 - mott) * 0.04 + sand * 0.08, 0.90, rustm), 0.34 + n3 * 0.08, bare)
    height = (dimple * 0.00022 * K["hammer"] + n2 * 0.00025 + rustm * (n3 * 0.0003 + 0.00012)
              - pits * rustm * 0.00035 + n3 * 0.00004 + slag * 0.00008 + sand * 0.00006)
    return {"base": base, "rough": rough, "metal": bare, "height": height}


def rope(g, c, colour=(150, 128, 92), pitch=0.024, strands=3, dirt=0.3):
    u, s, t = c.lx, c.ly, c.lz
    th = g.atan2(t, s)
    phase = u * (1.0 / pitch) + th * (strands / TAU)
    strand = g.sin(phase * TAU) * 0.5 + 0.5
    groove = 1.0 - g.smooth(0.0, 0.3, strand)
    fibre = g.noise(g.vec(u * 60.0 + th * 3.0, th * 40.0, c.rand * 7.0), 1.0, detail=3.0)
    fuzz = g.noise(c.pos * 400.0, 2.0, detail=2.0)
    col = lin(colour)
    base = g.mix(col * 0.6, col * 1.08, strand * 0.7 + fibre * 0.3) * (0.9 + fuzz * 0.2)
    base = base * (1.0 - groove * 0.35) * (0.4 + 0.6 * c.ao)
    base = g.mix(base, lin(66, 54, 41), _near_ground(c) * dirt)
    return {"base": base, "rough": 0.9 - fibre * 0.04, "metal": 0.0,
            "height": strand * 0.0014 + fibre * 0.0002 + fuzz * 0.0001}


def leather(g, c, tan=0.5, wear=0.3):
    p = c.pos
    col = g.mix(lin(178, 132, 86), lin(70, 44, 28), tan)
    pebble, _ = g.voronoi(p * 700.0)
    crease = 1.0 - g.smooth(0.0, 0.03, g.abs(g.noise(p * 18.0, 1.0, detail=3.0) - 0.5))
    blot = g.noise(p * 5.0, 2.0, detail=4.0)
    scuff = g.smooth(0.62, 0.8, g.noise(p * 40.0, 3.0, detail=3.0)) * wear
    base = col * (0.85 + blot * 0.3) * (0.92 + pebble * 0.12)
    base = base * (1.0 - crease * 0.3)
    base = g.mix(base, col * 0.55, c.cvx * 0.5)                # burnished, darkened edges
    base = g.mix(base, g.mix(col, lin(196, 172, 140), 0.4), scuff * 0.6)
    base = base * (0.5 + 0.5 * c.ao)
    base = g.mix(base, lin(72, 60, 46), _near_ground(c) * 0.3 * wear)
    rough = 0.58 + pebble * 0.08 + scuff * 0.2 - c.cvx * 0.18 + blot * 0.04
    height = pebble * 0.00012 - crease * 0.0003 - scuff * 0.00005
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def canvas(g, c, colour=(150, 138, 112), weave=180.0, dirt=0.3):
    u, v = c.lx, c.ly
    warp = g.sin(u * (TAU * weave)) * 0.5 + 0.5
    weft = g.sin(v * (TAU * weave)) * 0.5 + 0.5
    cloth = g.mix(warp, weft, g.gt(g.sin((u + v) * (TAU * weave * 0.5)), 0.0))
    folds = g.noise(c.pos * 6.0, 1.0, detail=3.0)
    blot = g.noise(c.pos * 3.0, 2.0, detail=4.0)
    stain = g.smooth(0.58, 0.78, g.noise(c.pos * 7.0, 3.0, detail=4.0))
    col = lin(colour)
    base = g.mix(col * 0.82, col * 1.08, blot) * (0.9 + cloth * 0.14)
    base = g.mix(base, col * 0.6, stain * 0.5 * (0.5 + dirt))
    base = g.mix(base, lin(196, 188, 170), g.smooth(0.4, 0.95, c.nz) * 0.15)   # sun-bleached on top
    base = g.mix(base, lin(80, 68, 52), _near_ground(c) * dirt)
    base = base * (0.45 + 0.55 * c.ao)
    return {"base": base, "rough": 0.84 + cloth * 0.06, "metal": 0.0,
            "height": cloth * 0.00018 + folds * 0.0006 + blot * 0.0002}


def stone(g, c, kind="granite", weathering=0.4, lichen=0.3):
    K = STONE_KINDS[kind]
    q = c.mpos + g.vec(c.rand * 37.0, c.rand * 53.0, c.rand * 71.0)
    big = g.noise(q * 3.0, 1.0, detail=4.0)
    mid = g.noise(q * 18.0, 2.0, detail=4.0)
    fine = g.noise(q * 120.0, 3.0, detail=2.0)
    grain_d, grain_c = g.voronoi(q * K["grain"])
    gr, gg, _ = g.sep(grain_c)
    tone = g.sat(big * 0.5 + c.rand * 0.4 + mid * 0.3 - 0.1)
    base = g.ramp(tone, K["tones"])
    base = base * (0.9 + gg * 0.2)
    speck = g.lt(gr, K["speck_share"])
    base = g.mix(base, lin(K["speck"]), speck * 0.8)
    if kind == "slate":
        lam = g.sin((c.lz * 900.0 + big * 6.0) * TAU) * 0.5 + 0.5
        base = base * (0.94 + lam * 0.08)
    else:
        lam = 0.0
    pits = (1.0 - g.smooth(0.0, 0.25, grain_d)) * g.lt(gg, 0.08) * (1.0 if kind == "limestone" else 0.3)
    base = g.mix(base, base * 0.55, pits)
    base = g.mix(base, base * 1.18, c.cvx * 0.5)                          # chipped, fresher arrises
    streaks = g.smooth(0.55, 0.8, g.noise(g.vec(c.lx * 20.0, c.ly * 20.0, c.pz * 2.0), 4.0, detail=3.0))
    base = g.mix(base, base * 0.7, streaks * weathering * 0.5)
    lich_m = (g.smooth(0.62, 0.72, g.noise(q * 6.0, 5.0, detail=4.0)) * g.smooth(0.2, 0.8, c.nz) * lichen)
    base = g.mix(base, g.mix(lin(132, 134, 116), lin(150, 140, 96), fine), lich_m * 0.7)
    moss = g.smooth(0.55, 0.7, g.noise(q * 4.0, 6.0, detail=4.0)) * _near_ground(c) * weathering
    base = g.mix(base, lin(52, 60, 34), moss * 0.7)
    base = base * (0.55 + 0.45 * c.ao)
    rough = K["rough"] + fine * 0.06 - c.cvx * 0.06 + lich_m * 0.08 + moss * 0.1
    height = big * 0.0012 + mid * 0.0006 + fine * 0.0002 - pits * 0.0006 + lam * 0.0003 + lich_m * 0.0002
    return {"base": base, "rough": rough, "metal": 0.0, "height": height}


def plaster(g, c, tint=(178, 168, 148), dirt=0.3, cracks=0.3):
    p = c.pos
    und = g.noise(p * 3.0, 1.0, detail=3.0)
    trowel = g.noise(p * 16.0, 2.0, detail=5.0, rough=0.6)
    grain = g.noise(p * 600.0, 3.0, detail=2.0)
    edge_d, _ = g.voronoi(p * 5.0, feature="DISTANCE_TO_EDGE")
    crack = (1.0 - g.smooth(0.0, 0.012, edge_d)) * g.smooth(1.0 - cracks, 1.0, g.noise(p * 4.0, 4.0, detail=3.0))
    stain = g.smooth(0.55, 0.8, g.noise(p * 3.0, 5.0, detail=5.0))
    runs = g.smooth(0.6, 0.85, g.noise(g.vec(c.lx * 20.0, c.ly * 20.0, c.pz * 2.2), 6.0, detail=3.0))
    col = lin(tint)
    base = col * (0.9 + und * 0.1 + trowel * 0.08) * (1.0 + grain * 0.02)
    base = g.mix(base, col * 0.7, stain * 0.35 * (0.5 + dirt))
    base = g.mix(base, col * 0.75, runs * 0.3 * dirt)
    base = base * (1.0 - crack * 0.5)
    base = g.mix(base, lin(96, 84, 66), _near_ground(c) * dirt)
    base = base * (0.55 + 0.45 * c.ao)
    return {"base": base, "rough": 0.9 + grain * 0.02, "metal": 0.0,
            "height": und * 0.001 + trowel * 0.0004 + grain * 0.00005 - crack * 0.0012}


def fresh_break(g, c, species="pine"):
    S = SPECIES[species]
    x, y, z, r = c.lx, c.ly, c.lz, c.rand
    fib = g.noise(g.vec(x * 90.0, y * 90.0, z * 90.0 + r * 7.0), 1.0, detail=4.0)
    grain = g.noise(g.vec(x * 14.0, y * 160.0, z * 160.0 + r * 9.0), 2.0, detail=3.0)
    base = g.mix(lin(S["early"]), lin(S["late"]), fib * 0.6) * (0.85 + grain * 0.3)
    base = g.mix(base, lin(S["early"]) * 1.1, 0.3)
    base = base * (0.45 + 0.55 * c.ao)
    return {"base": base, "rough": 0.86 + fib * 0.06, "metal": 0.0, "height": fib * 0.0012 + grain * 0.0006}


RECIPES = {"wood": wood, "iron": iron, "rope": rope, "leather": leather, "canvas": canvas, "stone": stone,
           "plaster": plaster, "fresh_break": fresh_break}

_FLAT = {"wood": ((0.40, 0.27, 0.16), 0.0), "iron": ((0.05, 0.05, 0.05), 0.0), "rope": ((0.28, 0.22, 0.14), 0.0),
         "leather": ((0.2, 0.11, 0.06), 0.0), "canvas": ((0.3, 0.26, 0.19), 0.0), "stone": ((0.25, 0.24, 0.22), 0.0),
         "plaster": ((0.42, 0.39, 0.33), 0.0), "fresh_break": ((0.6, 0.45, 0.28), 0.0)}


class Material:
    """One material slot: a recipe and its parameters."""

    def __init__(self, recipe, name=None, **params):
        self.recipe_name = recipe if isinstance(recipe, str) else recipe.__name__
        self.recipe = RECIPES[recipe] if isinstance(recipe, str) else recipe
        self.params = params
        self.name = name or self.recipe_name
        self.flat_colour, self.flat_metal = _FLAT.get(self.recipe_name, ((0.5, 0.5, 0.5), 0.0))

    def build(self, g, c):
        return self.recipe(g, c, **self.params)

    def describe(self):
        return {"recipe": self.recipe_name, "params": self.params}
