"""soft_goods archetype template (docs/WAVE_0_MODULAR_ASSET_STANDARD.md section 17; assets/manifests/
asset_production.json archetype "soft_goods"): sewn bags built from panel patterns -- body (+ integral gusset),
flap, patch pocket, strap (shoulder sling or belt loop), carry handle, drawstring neck, buckle closures and seam
welts -- covering sacks, haversacks, pouches, bedrolls and bundles from one template plus per-asset parameters.

Geometry, not simulation: the body is a vertical loft of rounded-rectangle rings (panel pattern: front + back +
side gusset read as one sewn tube) whose half-width/half-depth follow a deterministic profile (neck taper, belly
bulge, foot flare) and then a seeded per-vertex "inflate/settle" deformer -- radial jitter from
procgen_lib.noise.fbm3, weighted toward the belly and floor, faded out at the cinched neck -- so the sack reads as
an unevenly filled, gravity-settled fabric bag rather than a rigid box. This is the alternative the standard
allows to a baked Blender cloth sim: fully deterministic from the seed, no physics bake/cache to go stale.

Panel orientation convention used throughout (Blender Z-up, front toward -Y, matching procgen_lib.runner):
elements flush against the FRONT or BACK face (flap, pocket, buckle tabs) sweep with bend_axis = X (tangent to
those faces); elements flush against a SIDE face (seam welts, a shoulder strap, a belt loop) use bend_axis = Y.

Per-asset parameters live in assets/manifests/procgen/<asset_id>.json ({"concept": ..., "parameters": {...}}, a
deep override of PARAMS below); the instance itself supplies only what differs from the archetype defaults, which
is what keeps a second instance's file small (the cost rule, section 17).

    blender --background --factory-startup --python tools/asset_pipeline/_procgen_soft_goods.py -- \
        --asset prop_canvas_haversack [--review --review-size 1536] [--tokens N --wall-minutes M]
"""
import copy
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from procgen_lib import primitives as pr  # noqa: E402
from procgen_lib import noise as ns  # noqa: E402
from procgen_lib import export  # noqa: E402
from procgen_lib.materials import Material  # noqa: E402
from procgen_lib.runner import run_template  # noqa: E402

X, Y, Z = np.eye(3)
BODY, TRIM, HARDWARE = 0, 1, 2
DEFAULT_ASSET = "prop_canvas_haversack"

DEFAULT_CLOSURE = {
    "z_frac": 0.86, "y_face": None, "buckle_w_m": 0.020, "buckle_h_m": 0.015, "buckle_t_m": 0.006,
    "buckle_c_m": 0.0025, "frame_t_m": 0.0045, "tab_w_m": 0.018, "tab_thick_m": 0.004, "tab_c_m": 0.0015,
    "tab_top_z_m": None, "rivet_r_m": 0.004,
}

PARAMS = {
    "seed": 2609,
    "body": {
        "width_m": 0.28, "depth_m": 0.20, "height_m": 0.34, "rings": 16, "corner_r_m": 0.020,
        "belly_t": 0.55, "bulge": 0.10, "neck_taper": 0.35, "neck_start": 0.62, "base_flare": 0.06,
        "settle_amp_m": 0.006, "settle_octaves": 3, "seam_w_m": 0.0035, "seam_welts": True,
    },
    "flap": {
        "present": False, "coverage": 0.42, "width_frac": 0.92, "thickness_m": 0.004, "edge_c_m": 0.0015,
        "rise_m": 0.012, "droop_m": 0.006, "standoff_m": 0.011,
    },
    "pocket": {
        "present": False, "width_frac": 0.68, "height_frac": 0.34, "y_frac": 0.10, "standoff_m": 0.028,
        "corner_r_m": 0.010, "edge_c_m": 0.003, "flap_present": True, "flap_coverage": 0.55,
        "flap_thickness_m": 0.0035, "flap_edge_c_m": 0.0012, "flap_droop_m": 0.004, "rivet_r_m": 0.0035,
    },
    "closures": [],
    "strap": {
        "type": None, "side": -1.0, "upper_t": 0.92, "lower_t": 0.10, "attach_gap_m": 0.011, "sag_m": 0.05,
        "width_m": 0.028, "thickness_m": 0.004, "edge_c_m": 0.0015, "rivet_r_m": 0.0045,
        "z_frac": 0.55, "loop_h_m": 0.05, "standoff_m": 0.022, "loop_thickness_m": 0.004, "loop_width_m": 0.032,
        "loop_edge_c_m": 0.0015,
    },
    "handle": {
        "present": False, "z_frac": 1.0, "span_m": 0.09, "rise_m": 0.05, "y_frac": 0.22, "r_m": 0.006,
        "rivet_r_m": 0.004,
    },
    "drawstring": {
        "present": False, "t_frac": 0.94, "r_m": 0.0032, "gather_amp_m": 0.004, "tail_len_m": 0.05,
    },
    "materials": {
        "body": {"recipe": "canvas", "colour": [150, 138, 112], "weave": 180, "dirt": 0.35},
        "trim": {"recipe": "leather", "tan": 0.55, "wear": 0.35},
        "hardware": {"recipe": "iron", "kind": "forged", "rust": 0.10},
    },
}


def deep_merge(base, override):
    """Recursive dict override, except a material spec ({"recipe": ..., <its own params>}) replaces wholesale --
    each recipe has its own parameter names, so merging a leather override onto a canvas default would leave
    stray canvas-only kwargs (colour, weave, dirt) that leather() does not accept."""
    out = copy.deepcopy(base)
    for k, v in override.items():
        if isinstance(v, dict) and isinstance(out.get(k), dict) and "recipe" not in v:
            out[k] = deep_merge(out[k], v)
        else:
            out[k] = copy.deepcopy(v)
    return out


def clearance_eps(BP, k=0.6):
    """Minimum stand-off beyond the smooth half_extents() profile for anything meant to just clear the body: the
    body mesh itself carries seeded settle jitter on top of that smooth profile (build_body), so a flush-touch
    part needs more than a nominal gap or it can dip inside a jittered bulge and fail the clash gate. Kept small
    (k tunes it per caller): too little risks a clash, too much visibly separates the part from the body (the
    review render's island-connectivity check)."""
    return 0.0012 + BP.get("settle_amp_m", 0.0) * k


def half_extents(t, BP):
    """(half_width, half_depth) of the body's rounded-rect cross-section at normalised height t in [0, 1]:
    a smooth belly bulge, a tapered/cinched neck near the top and a flared foot where it settles on the ground."""
    bump = BP["bulge"] * math.exp(-6.0 * (t - BP["belly_t"]) ** 2)
    neck = BP["neck_taper"] * ns.smoothstep(BP["neck_start"], 1.0, t)
    flare = BP["base_flare"] * (1.0 - ns.smoothstep(0.0, 0.10, t))
    scale = max(0.15, 1.0 + bump - neck + flare)
    return BP["width_m"] / 2 * scale, BP["depth_m"] / 2 * scale


def build_body(b, P, rng):
    BP = P["body"]
    n = max(6, int(BP["rings"]))
    ts = np.linspace(0.0, 1.0, n)
    zs = ts * BP["height_m"]
    npts = 8  # chamfer_rect always returns 8 points
    coords = np.array([[ts[i] * 7.3, (k / npts) * 5.1, 0.0] for i in range(n) for k in range(npts)])
    jitter = ns.fbm3(coords, P["seed"] + 101, octaves=BP["settle_octaves"]).reshape(n, npts)
    frame = pr.frame_from((0.0, 0.0, 0.0), Z, Y)
    b.part("body", frame, group="body", slot=BODY, texel=1.0)
    rings = []
    for i, t in enumerate(ts):
        hw, hd = half_extents(t, BP)
        cr = min(BP["corner_r_m"], 0.4 * hw, 0.4 * hd)
        w = ns.smoothstep(0.0, 0.12, t) * (1.0 - 0.4 * ns.smoothstep(0.8, 1.0, t))
        amp = BP["settle_amp_m"] * w
        ring = []
        for k, (py, pz) in enumerate(pr.chamfer_rect(hw, hd, cr)):
            r = math.hypot(py, pz) + 1e-9
            j = float(jitter[i, k]) * amp
            ring.append((float(zs[i]), py + (py / r) * j, pz + (pz / r) * j))
        rings.append(ring)
    pr.loft(b, rings, [float(z) for z in zs], cap_axes=(Y, Z), end_caps=False)
    b.close()


def build_seams(b, P, rng):
    BP = P["body"]
    if not BP.get("seam_welts", True):
        return
    n = 10
    for side in (-1, 1):
        pts = []
        for t in np.linspace(0.02, 0.97, n):
            hw, _ = half_extents(t, BP)
            pts.append((side * (hw + 0.0008), 0.0, t * BP["height_m"]))
        pr.poly_sweep(b, f"seam_{'L' if side < 0 else 'R'}", pts, Y, BP["seam_w_m"], BP["seam_w_m"] * 1.6,
                      c=BP["seam_w_m"] * 0.3, group="seam", slot=TRIM)


def build_flap(b, P, rng):
    FP, BP = P["flap"], P["body"]
    if not FP.get("present"):
        return None
    H = BP["height_m"]
    hw_n, hd_n = half_extents(1.0, BP)
    t_tip = 1.0 - FP["coverage"]
    standoff = FP["standoff_m"]
    # hug the front profile point by point (not a chord between two samples) so the drape clears the belly bulge
    front_ts = np.linspace(1.0, t_tip, 6)
    front_pts = [(0.0, -half_extents(t, BP)[1] - standoff, t * H) for t in front_ts]
    front_pts[0] = (0.0, front_pts[0][1], H + 0.006)      # clear of the top cap plane at z = H
    front_pts[-1] = (0.0, front_pts[-1][1], front_pts[-1][2] - FP["droop_m"])
    pts = [(0.0, hd_n + standoff, H + 0.003), (0.0, hd_n * 0.15, H + FP["rise_m"])] + front_pts
    w = 2 * hw_n * FP["width_frac"]
    pr.poly_sweep(b, "flap", pts, X, FP["thickness_m"], w, c=FP["edge_c_m"], group="flap", slot=TRIM)
    return {"tip_y": front_pts[-1][1], "tip_z": front_pts[-1][2], "hw": hw_n}


def build_pocket(b, P, rng):
    PP, BP = P["pocket"], P["body"]
    if not PP.get("present"):
        return
    z0 = PP["y_frac"] * BP["height_m"]
    h = PP["height_frac"] * BP["height_m"]
    z1 = z0 + h
    H = BP["height_m"]
    hw, _ = half_extents((z0 + 0.5 * h) / H, BP)
    # the panel is flat, so it must clear the body's widest bulge across the whole height it spans, not just the
    # mid-height sample, plus a small fixed clearance (enough to clear the settle jitter on a front-facing panel,
    # but under the review render's ~3.5 mm island-connectivity tolerance so the pocket doesn't read as floating)
    hd = max(half_extents(t, BP)[1] for t in np.linspace(z0 / H, z1 / H, 5)) + 0.002
    w = PP["width_frac"] * 2 * hw
    standoff = PP["standoff_m"]
    frame = pr.frame_from((0.0, -hd - standoff / 2, (z0 + z1) / 2), X, -Y)
    poly = pr.chamfer_rect(w / 2, h / 2, PP["corner_r_m"])
    pr.prism(b, "pocket", frame, poly, standoff, PP["edge_c_m"], group="pocket", slot=BODY)
    if PP.get("flap_present"):
        fh = h * PP["flap_coverage"]
        pts = [(0.0, -hd - standoff * 0.2, z1 - 0.002), (0.0, -hd - standoff - PP["flap_droop_m"], z1 - fh)]
        pr.poly_sweep(b, "pocket_flap", pts, X, PP["flap_thickness_m"], w * 0.94, c=PP["flap_edge_c_m"],
                      group="pocket_flap", slot=TRIM)
        for s in (-1, 1):
            pr.rivet(b, (s * w * 0.32, -hd - standoff - PP["flap_droop_m"] * 0.6, z1 - fh + 0.004), (0, -1, 0),
                    r=PP["rivet_r_m"], group="pocket_flap", slot=HARDWARE)


def build_closures(b, P, rng):
    BP = P["body"]
    for i, spec in enumerate(P.get("closures", [])):
        c = {**DEFAULT_CLOSURE, **spec}
        hw_full, _ = half_extents(1.0, BP)
        x = c["x_frac"] * hw_full
        zc = c["z_frac"] * BP["height_m"]
        _, hd = half_extents(c["z_frac"], BP)
        y_face = c["y_face"] if c["y_face"] is not None else -hd
        frame = pr.frame_from((x, y_face - c["buckle_t_m"] / 2, zc), X, -Y)
        outer = pr.chamfer_rect(c["buckle_w_m"] / 2, c["buckle_h_m"] / 2, c["buckle_c_m"])
        ft = c["frame_t_m"]
        hole = pr.chamfer_rect(max(c["buckle_w_m"] / 2 - ft, 0.0025), max(c["buckle_h_m"] / 2 - ft * 1.5, 0.0025),
                               max(c["buckle_c_m"] - ft * 0.6, 0.0004))
        pr.prism(b, f"buckle_{i}", frame, outer, c["buckle_t_m"], c["buckle_c_m"] * 0.6, group="closures",
                 slot=HARDWARE, hole=hole)
        pr.rivet(b, (x, y_face - 0.001, zc + c["buckle_h_m"] / 2 + 0.007), (0, -1, 0), r=c["rivet_r_m"],
                group="closures", slot=HARDWARE)
        # the tab threads through the buckle's hole: recessed behind the buckle's OUTER face (so the frame reads
        # in front of it, not the other way round) and long enough to show a short tail past the buckle's bottom
        z_tab_top = c["tab_top_z_m"] if c["tab_top_z_m"] is not None else zc + c["buckle_h_m"] / 2 + 0.016
        z_tab_bot = zc - c["buckle_h_m"] / 2 - 0.006
        y_tab = y_face - c["buckle_t_m"] * 0.35
        pts = [(x, y_tab, z_tab_top), (x, y_tab, z_tab_bot)]
        pr.poly_sweep(b, f"tab_{i}", pts, X, c["tab_thick_m"], c["tab_w_m"], c=c["tab_c_m"], group="closures",
                      slot=TRIM)
        pr.rivet(b, (x, y_tab - c["tab_thick_m"] / 2 - 0.0005, z_tab_top - 0.002), (0, -1, 0),
                r=c["rivet_r_m"] * 0.85, group="closures", slot=HARDWARE)


def build_strap(b, P, rng):
    SP, BP = P["strap"], P["body"]
    kind = SP.get("type")
    if kind == "shoulder":
        side = SP["side"]
        hw_top, _ = half_extents(SP["upper_t"], BP)
        hw_bot, _ = half_extents(SP["lower_t"], BP)
        z_top, z_bot = SP["upper_t"] * BP["height_m"], SP["lower_t"] * BP["height_m"]
        x_top = side * (hw_top + SP["attach_gap_m"])
        x_bot = side * (hw_bot + SP["attach_gap_m"])
        x_mid = side * (max(hw_top, hw_bot) + SP["attach_gap_m"] + SP["sag_m"])
        pts = [(x_top, 0.0, z_top), (x_mid, 0.0, (z_top + z_bot) * 0.5), (x_bot, 0.0, z_bot)]
        pr.poly_sweep(b, "strap_shoulder", pts, Y, SP["thickness_m"], SP["width_m"], c=SP["edge_c_m"],
                      group="strap", slot=TRIM)
        for p in (pts[0], pts[-1]):
            # its own group: a rivet is meant to sink into the surface it fastens (see primitives.rivet), which
            # would otherwise register as a clash against the body
            pr.rivet(b, p, (side, 0, 0), r=SP["rivet_r_m"], group="strap_hw", slot=HARDWARE)
    elif kind == "belt_loop":
        H = BP["height_m"]
        t = SP["z_frac"]
        loop_half_a = SP["loop_h_m"] / 2
        # the loop is a flat band, so it must clear the body's bulge across the whole height it spans, plus a
        # small clearance margin (a flush touch can register as a clash near the tolerance -- see build_pocket)
        span = np.linspace(max(0.0, (t * H - loop_half_a) / H), min(1.0, (t * H + loop_half_a) / H), 5)
        hd = max(half_extents(tt, BP)[1] for tt in span) + clearance_eps(BP, k=1.5)
        half_b = SP["standoff_m"] / 2.0
        cc = half_b + SP["loop_thickness_m"] / 2.0   # band_around's own half-extent along dir_b (band_around, half_b + t/2)
        centre = (0.0, hd + cc, t * H)                # so the near face (centre_y - cc) sits just clear of hd
        pr.band_around(b, "belt_loop", centre, X, loop_half_a, half_b, Z, t=SP["loop_thickness_m"],
                       w=SP["loop_width_m"], c=SP["loop_edge_c_m"], group="strap", slot=TRIM)


def build_handle(b, P, rng):
    HP, BP = P["handle"], P["body"]
    if not HP.get("present"):
        return
    z = HP["z_frac"] * BP["height_m"]
    hw, hd = half_extents(min(HP["z_frac"], 1.0), BP)
    span, rise = HP["span_m"], HP["rise_m"]
    y0 = hd * HP["y_frac"]
    pts = pr.catmull([(-span / 2, y0, z), (-span * 0.28, y0 * 0.4, z + rise), (0.0, y0 * 0.1, z + rise * 1.15),
                      (span * 0.28, y0 * 0.4, z + rise), (span / 2, y0, z)], step=0.01)
    pr.tube(b, "handle", pts, HP["r_m"], n=8, group="handle", slot=TRIM)
    for s in (-1, 1):
        pr.rivet(b, (s * span / 2, y0, z), (0, 1, 0), r=HP["rivet_r_m"], group="handle", slot=HARDWARE)


def build_drawstring(b, P, rng):
    DP, BP = P["drawstring"], P["body"]
    if not DP.get("present"):
        return
    t = DP["t_frac"]
    hw, hd = half_extents(t, BP)
    z = t * BP["height_m"]
    n = 14
    angles = np.linspace(0.0, 2 * math.pi, n, endpoint=False)
    coords = np.stack([np.cos(angles), np.sin(angles), np.full(n, P["seed"] * 0.001)], axis=1) * 3.0
    jig = ns.fbm3(coords, P["seed"] + 205, octaves=2) * DP["gather_amp_m"]
    pts = [((hw * 0.94 + jig[k]) * math.cos(a), (hd * 0.94 + jig[k]) * math.sin(a), z + abs(jig[k]) * 0.4)
           for k, a in enumerate(angles)]
    pr.tube(b, "drawstring", pts, DP["r_m"], n=6, group="drawstring", slot=TRIM, closed=True)
    for s in (-1, 1):
        tail = pr.catmull([(s * hw * 0.15, -hd * 0.9, z), (s * hw * 0.22, -hd * 1.05, z - DP["tail_len_m"] * 0.6),
                           (s * hw * 0.18, -hd * 0.95, z - DP["tail_len_m"])], step=0.01)
        pr.tube(b, f"drawstring_tail_{'L' if s < 0 else 'R'}", tail, DP["r_m"] * 0.85, n=6, group="drawstring",
               slot=TRIM)


def mat_from(spec):
    spec = dict(spec)
    recipe = spec.pop("recipe")
    return Material(recipe, **spec)


def build(b, P, rng):
    build_body(b, P, rng)
    build_seams(b, P, rng)
    build_pocket(b, P, rng)
    build_flap(b, P, rng)
    build_closures(b, P, rng)
    build_strap(b, P, rng)
    build_handle(b, P, rng)
    build_drawstring(b, P, rng)
    BP = P["body"]
    take_z = 0.85 * BP["height_m"]
    M = P["materials"]
    return {
        "materials": [mat_from(M["body"]), mat_from(M["trim"]), mat_from(M["hardware"])],
        "sockets": [{"name": "SOCK_interact_take", "at": (0.0, 0.0, take_z)}],
        "no_clash": [("body", "flap"), ("body", "pocket"), ("body", "strap")],
        "notes": [
            "soft_goods archetype: panel-pattern sewn bag (body/gusset as one lofted tube, flap, pocket, strap, "
            "buckle closures, drawstring, seam welts) on a seeded inflate/settle deformer -- radial jitter from "
            "procgen_lib.noise.fbm3, weighted toward the belly and floor -- in place of a baked Blender cloth sim.",
            "hardware uses the library's iron() recipe (no brass/bronze recipe exists yet); low-rust forged iron "
            "approximates worn steel buckle fittings rather than a golden brass tone.",
        ],
        "frame": "glTF +Y up, front +Z (build front -Y), centred on X/Z, lowest point y = 0",
    }


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    asset_id, rest, it = DEFAULT_ASSET, [], iter(argv)
    for tok in it:
        if tok == "--asset":
            asset_id = next(it)
        else:
            rest.append(tok)
    inst_path = os.path.join(export.REPO, "assets", "manifests", "procgen", f"{asset_id}.json")
    with open(inst_path, encoding="utf-8") as handle:
        inst = json.load(handle)
    P = deep_merge(PARAMS, inst.get("parameters", {}))
    concept = inst.get("concept", f"assets/concepts/{asset_id}.png")
    run_template(build, P, asset_id, archetype="soft_goods", concept=concept, tri_budget=12000, argv=rest,
                 instance_files=[inst_path])
