"""Reusable prop archetypes on procgen_lib (Phase B, B8: a shared builder layer for the prop backlog's recurring
forms, ahead of the reuse gate's per-asset templates -- see assets/manifests/asset_production.json). Where
sample_banded_box.py and _procgen_soft_goods.py are each one archetype's template, this module is the layer
*under* several templates: one parameterised builder per shared form, so a new prop instance (or a whole new
template) writes only the parameters that differ, not the geometry.

Every builder takes (b, P, rng, slots, group=...) -- P is that builder's own parameter dict, slots a
{role: material slot index} map (e.g. {"wood": 0, "iron": 1}), rng the Builder's own np.random.Generator (already
seeded by run_template) -- adds closed solids to b through primitives/meshbuild exactly as a template would, and
returns a small dict a template's build(b, P, rng) can splice straight into run_template's spec: "nodes" (groups
split into child objects, e.g. a chest lid), "sockets", "bake_lift", "no_clash" and "dims" (approximate x, y, z
extent, for a sanity check against the brief's real-world sizes). It never returns "materials": a builder is
material-agnostic, the calling template supplies Material instances for its own slots.

Fittings (straps, rivets, hinges, a hasp, a ring handle) are the deliberately small, shared vocabulary applied
*onto* other builders' geometry -- plank_box's lid hinges and hasp, wheel_axle's hub rivets, pipe_conduit's flange
bolts and tech_housing's corner fasteners all call the same few functions. _board_row is the other cross-cutting
helper: jittered-width boards laid side by side, used by plank_box (walls, floor, lid) and bench_table (top).

Build-space convention throughout, matching runner.py: Blender Z-up, front toward -Y, centred on X/Y by
run_template's own shift -- a builder need not centre or ground itself.

API
    timber_frame(b, P, rng, slots, group="frame") -> dict
        posts at a footprint's corners, a beam over each edge, optional knee braces; P: footprint_m or size_m,
        height_m, post_m (w, d), beam_h_m, beam_w_m, brace, brace_drop_m
    plank_box(b, P, rng, slots, group="body") -> dict
        chamfered-plank walls and floor over (length_m, depth_m, body_h_m); optional lid on battens (bands,
        hinge_fitting at the back, hasp_fitting at the front, rope or ring handles) -- a crate is P["lid"]["present"]
        False, a chest True
    wheel_axle(b, P, rng, slots, group="wheel") -> dict
        lathed hub, radiating spokes, a felloe rim (pr.ring), an iron tire band, a stub axle; hub axis local X (the
        wheel stands upright once run_template grounds it)
    bench_table(b, P, rng, slots, group="body") -> dict
        legs continued above a timber_frame stretcher rail up to seat/top height, boards across the top
        (_board_row); P: length_m, depth_m, top_h_m, leg_m, stretcher_z_frac, top_boards
    posts_and_rails(b, P, rng, slots, group="fence") -> dict
        a fence run: a post at each point of P["posts_m"] (a polyline), 1+ horizontal rails between consecutive posts
    rope_chain(b, name, pts, rng, kind="rope"|"chain", r_m, group, slot, n=8) -> None
        a sagging rope (round tube, pts pre-sagged by the caller or pr.catmull'd) or a chain of alternating-plane
        pr.ring links, along a point polyline
    pipe_conduit(b, P, rng, slots, group="pipe") -> dict
        a round tube along P["path_m"], a flange at each open end (bolted rim), an iron collar at each interior
        bend, and an optional inline P["valve"] (a lathed body, a stem, a small handwheel)
    tech_housing(b, P, rng, slots, group="body") -> dict
        a chamfered cased box (pr.prism); a trim seam loop standing in for a recessed access panel, a stack of
        raised louvre vent slats, corner rivets -- an unfamiliar manufactured case, not a glowing sci-fi panel
    banded_ring(b, P, rng, slots, group="body") -> dict        a torus (pr.ring) with a few perpendicular bands
    split_lobe(b, P, rng, slots, group="body") -> dict         two parallel bulbous lathed lobes across a gap
    stepped_plinth(b, P, rng, slots, group="body") -> dict     stacked chamfered prisms of shrinking footprint
    strap_fitting(b, name, pts, axis, t, w, c, group, slot, closed=False)     pr.poly_sweep, named for callers
    rivet_row(b, prefix, pts, normal, group, slot, r=0.0055)                  a pr.rivet at each point
    hinge_fitting(b, name, at, axis, length, r, group, slot)                  a small iron knuckle barrel
    hasp_fitting(b, name, at, normal, across, group, slot, w=0.03, h=0.05, t=0.006)   a strap plate + staple ring
    ring_handle_fitting(b, name, at, E1, E2, radius, wire_r, group, slot)     pr.ring(circ=wire_r), named for callers
"""
import math

import numpy as np

from . import primitives as pr

TAU = 2.0 * math.pi
X, Y, Z = np.eye(3)


def _unit(v):
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def _widths(rng, total, count, gap):
    w = rng.uniform(0.88, 1.12, count)
    return w / w.sum() * (total - (count - 1) * gap)


def _board_row(b, prefix, rng, count, total, gap, length, thickness, origin, along, normal, across, group, slot,
              texel=1.0):
    """count boards of jittered width summing to `total` (minus gaps), laid side by side along `across` from
    `origin`, each running `length` along `along` and `thickness` deep along `normal`."""
    origin = np.asarray(origin, np.float64)
    y = -total / 2
    for i, w in enumerate(_widths(rng, total, count, gap)):
        centre = origin + across * (y + w / 2)
        pr.plank(b, f"{prefix}{i}", centre, along, normal, length, w, thickness, group=group, slot=slot, texel=texel)
        y += w + gap


# ------------------------------------------------------------------------------------------------ fittings

def strap_fitting(b, name, pts, axis, t, w, c, group, slot, closed=False):
    """A mitred flat iron strap along a polyline of points (band_loop/band_around are the box/member-shaped special
    cases of this in primitives)."""
    return pr.poly_sweep(b, name, pts, axis, t, w, c=c, closed=closed, group=group, slot=slot)


def rivet_row(b, prefix, pts, normal, group, slot, r=0.0055):
    """A domed rivet at each point (a fixed or per-point normal)."""
    normals = normal if isinstance(normal, list) else [normal] * len(pts)
    for i, (p, n) in enumerate(zip(pts, normals)):
        pr.rivet(b, p, n, r=r, group=group, slot=slot, name=f"{prefix}{i}")


def hinge_fitting(b, name, at, axis, length, r, group, slot):
    """A strap hinge's knuckle: a small barrel along `axis`, centred at `at`."""
    axis = _unit(axis)
    C = np.asarray(at, np.float64) - axis * length / 2
    pr.lathe(b, name, C, axis, [(0.0, r * 0.7), (length * 0.15, r), (length * 0.85, r), (length, r * 0.7)],
             n=10, group=group, slot=slot)


def hasp_fitting(b, name, at, normal, across, group, slot, w=0.03, h=0.05, t=0.006):
    """A hinged hasp over a seam: a chamfered strap plate standing off the surface, with a small staple ring for a
    padlock (or a pin) to pass through."""
    normal, across = _unit(normal), _unit(across)
    at = np.asarray(at, np.float64)
    pts = [at + normal * (t * 2.2), at, at - normal * (h * 0.55)]
    pr.poly_sweep(b, f"{name}_plate", pts, across, t, w, c=t * 0.3, group=group, slot=slot)
    ring_handle_fitting(b, f"{name}_staple", at - normal * (h * 0.55) - normal * (w * 0.4), across,
                        np.cross(across, normal), w * 0.4, t * 0.55, group, slot)


def ring_handle_fitting(b, name, at, E1, E2, radius, wire_r, group, slot):
    """A round pull ring (drawer, chest, hasp staple) in the plane E1-E2."""
    return pr.ring(b, name, at, E1, E2, radius, circ=wire_r, group=group, slot=slot)


# ------------------------------------------------------------------------------------------------ timber frame

def timber_frame(b, P, rng, slots, group="frame"):
    wood = slots["wood"]
    footprint = P.get("footprint_m")
    if footprint is None:
        Lx, Ly = P.get("size_m", (1.0, 1.0))
        footprint = [(-Lx / 2, -Ly / 2), (Lx / 2, -Ly / 2), (Lx / 2, Ly / 2), (-Lx / 2, Ly / 2)]
    H = P.get("height_m", 1.0)
    pw, pd = P.get("post_m", (0.09, 0.09))
    bh, bw = P.get("beam_h_m", 0.08), P.get("beam_w_m", 0.07)
    n = len(footprint)
    for i, (x, y) in enumerate(footprint):
        pr.member(b, f"post_{i}", (x, y, 0.0), (x, y, H), X, pd, pw, group=group, slot=wood)
    for i in range(n):
        x0, y0 = footprint[i]
        x1, y1 = footprint[(i + 1) % n]
        pr.member(b, f"beam_{i}", (x0, y0, H), (x1, y1, H), Z, bh, bw, group=group, slot=wood)
    if P.get("brace", True):
        drop = P.get("brace_drop_m", 0.35)
        bs = bw * 0.75
        for i in range(n):
            x0, y0 = footprint[i]
            x1, y1 = footprint[(i + 1) % n]
            low = (x0, y0, H - drop)
            mid = ((x0 + x1) / 2, (y0 + y1) / 2, H - bh * 0.3)
            pr.member(b, f"brace_{i}", low, mid, Z, bs, bs, group=group, slot=wood)
    Lx = max(p[0] for p in footprint) - min(p[0] for p in footprint)
    Ly = max(p[1] for p in footprint) - min(p[1] for p in footprint)
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (Lx, Ly, H), "top_z": H,
            "footprint": footprint}


# ------------------------------------------------------------------------------------------------ plank box / crate / chest

def plank_box(b, P, rng, slots, group="body"):
    wood, iron = slots["wood"], slots.get("iron", slots["wood"])
    rope = slots.get("rope", iron)
    L, D, H, t = P["length_m"], P["depth_m"], P["body_h_m"], P.get("board_t_m", 0.018)
    rows, gap = P.get("rows", 3), P.get("gap_m", 0.0012)
    z = 0.0
    heights = _widths(rng, H, rows, gap)
    for i, h in enumerate(heights):
        zc = z + h / 2
        z += h + gap
        pr.plank(b, f"wall_F{i}", (0, -(D / 2 - t / 2), zc), X, -Y, L, h, t, group=group, slot=wood)
        pr.plank(b, f"wall_B{i}", (0, D / 2 - t / 2, zc), X, Y, L, h, t, group=group, slot=wood)
        pr.plank(b, f"wall_L{i}", (-(L / 2 - t / 2), 0, zc), Y, -X, D - 2 * t - 0.0006, h, t, group=group, slot=wood)
        pr.plank(b, f"wall_R{i}", (L / 2 - t / 2, 0, zc), Y, X, D - 2 * t - 0.0006, h, t, group=group, slot=wood)
    floor_z = P.get("floor_z_m", min(0.02, H * 0.08))
    _board_row(b, "floor_", rng, P.get("floor_boards", 3), D - 2 * t - 0.0006, gap, L - 2 * t - 0.0006, t,
              (0, 0, floor_z + t / 2), X, Z, Y, group, wood)
    bands = P.get("bands")
    if bands:
        bt, rr = bands.get("t_m", 0.003), bands.get("rivet_r_m", 0.0055)
        for k, (z0, z1) in enumerate(bands.get("z_m", [])):
            pr.band_loop(b, f"band_{k}", (0, 0, 0), L / 2, D / 2, z0, z1, bt, group=group, slot=iron)
            if bands.get("rivets", True):
                zc = (z0 + z1) / 2
                for s in (-1, 1):
                    pts = [(u, s * (D / 2 + bt), zc) for u in (-0.17 * L, 0.0, 0.17 * L)]
                    rivet_row(b, f"band_{k}_rv_fb{s}_", pts, [(0, s, 0)] * 3, group, iron, r=rr)
                    pts = [(s * (L / 2 + bt), u, zc) for u in (-0.17 * D, 0.0, 0.17 * D)]
                    rivet_row(b, f"band_{k}_rv_lr{s}_", pts, [(s, 0, 0)] * 3, group, iron, r=rr)
    lid = P.get("lid", {})
    out = {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (L, D, H)}
    if lid.get("present"):
        lt, lb = lid.get("t_m", 0.02), lid.get("boards", 3)
        bw_, bh_ = lid.get("batten_w_m", 0.04), lid.get("batten_h_m", 0.018)
        zl = H + lid.get("gap_m", 0.0015)
        _board_row(b, "lid_", rng, lb, D, lid.get("board_gap_m", gap), L, lt, (0, 0, zl + lt / 2), X, Z, Y,
                  "lid", wood)
        for s in (-1, 1):
            xb = s * (L / 2 - t - 0.006 - bw_ / 2)
            pr.plank(b, f"batten_{'LR'[s > 0]}", (xb, 0, zl - bh_ / 2), Y, s * X, D - 2 * t - 0.012, bh_, bw_,
                     group="lid", slot=wood)
        hinge_count = lid.get("hinges", 2)
        hinge_r = 0.009
        for i in range(hinge_count):
            xh = (i + 0.5) / hinge_count * L - L / 2
            hinge_fitting(b, f"hinge_{i}", (xh, D / 2 + hinge_r + 0.0006, zl + lt / 2), X,
                         L / (hinge_count * 3.2), hinge_r, "lid", iron)
            pr.rivet(b, (xh - 0.02, D / 2 - t * 0.3, zl + lt / 2), Z, r=0.004, group=group, slot=iron,
                    name=f"hinge_{i}_rv")
        if lid.get("hasp", True):
            # mid-wall, clearly below the lid seam (H - 0.015 read as "on the lid" at a chest's scale, too close
            # to the rim) so it cannot clash the lid boards above it and reads as a body-mounted catch
            hasp_fitting(b, "hasp", (0.0, -(D / 2 + t * 0.5), H * 0.68), -Y, X, group, iron)
        handle = P.get("handle", {})
        if handle.get("type") == "rope":
            hs, zh = handle.get("span_m", 0.13) / 2, handle.get("z_m", 0.6 * H)
            for s in (-1, 1):
                xw = s * (L / 2 - 0.006)
                pts = [(xw, -hs, zh), (s * (L / 2 + 0.02), -hs * 0.8, zh - 0.02),
                       (s * (L / 2 + 0.03), 0, zh - 0.045), (s * (L / 2 + 0.02), hs * 0.8, zh - 0.02), (xw, hs, zh)]
                rope_chain(b, f"rope_{'LR'[s > 0]}", pts, rng, kind="rope", r_m=handle.get("r_m", 0.007),
                          group="body", slot=rope)
        elif handle.get("type") == "ring":
            zh = handle.get("z_m", 0.6 * H)
            for s in (-1, 1):
                ring_handle_fitting(b, f"handle_{'LR'[s > 0]}", (s * (L / 2 + 0.002), 0, zh), Y, Z,
                                    handle.get("r_m", 0.045), 0.006, group, iron)
        out.update({"nodes": {"lid": {"group": "lid", "pivot": (0.0, D / 2, H)}}, "bake_lift": {"lid": 1.0},
                    "no_clash": [("body", "lid")], "dims": (L, D, H + lt)})
    return out


# ------------------------------------------------------------------------------------------------ wheel + axle

def wheel_axle(b, P, rng, slots, group="wheel"):
    wood, iron = slots["wood"], slots.get("iron", slots["wood"])
    R = P.get("radius_m", 0.55)
    rim_t, rim_w = P.get("rim_t_m", 0.05), P.get("rim_w_m", 0.09)
    hub_r, hub_len = P.get("hub_r_m", 0.09), P.get("hub_len_m", 0.24)
    n_spokes = P.get("spoke_count", 10)
    spoke_w, spoke_t = P.get("spoke_w_m", 0.05), P.get("spoke_t_m", 0.045)
    axle_r, axle_len = P.get("axle_r_m", 0.035), P.get("axle_len_m", 0.9)
    tire = P.get("tire", {"present": True, "t_m": 0.014})
    pr.lathe(b, "hub", (-hub_len / 2, 0, 0), X,
            [(0.0, hub_r * 0.7), (hub_len * 0.1, hub_r), (hub_len * 0.9, hub_r), (hub_len, hub_r * 0.7)],
            n=12, group=group, slot=wood)
    rim_r = R - rim_t / 2
    for k in range(n_spokes):
        th = TAU * k / n_spokes
        d = math.cos(th) * Y + math.sin(th) * Z
        p0, p1 = hub_r * 0.96 * d, rim_r * d
        pr.member(b, f"spoke_{k}", p0, p1, X, spoke_t, spoke_w, group=group, slot=wood)
    pr.ring(b, "rim", (0, 0, 0), Y, Z, rim_r, h=rim_t, w=rim_w, c=rim_t * 0.15, group=group, slot=wood,
           grain=(rim_t / 2, rim_w / 2))
    if tire.get("present", True):
        tt = tire.get("t_m", 0.014)
        pr.ring(b, "tire", (0, 0, 0), Y, Z, R + tt / 2, h=tt, w=rim_w * 0.92, c=tt * 0.2, group=group, slot=iron)
    pr.lathe(b, "axle", (-axle_len / 2, 0, 0), X,
            [(0.0, axle_r * 0.8), (axle_len * 0.06, axle_r), (axle_len * 0.94, axle_r), (axle_len, axle_r * 0.8)],
            n=10, group="axle", slot=iron)
    # the axle is meant to pass through the hub bore (no boolean cut in this library): wheel|axle is not registered
    # as a no_clash pair, unlike plank_box's body|lid, which must not interpenetrate
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (2 * R, rim_w, axle_len)}


# ------------------------------------------------------------------------------------------------ bench / table

def bench_table(b, P, rng, slots, group="body"):
    wood = slots["wood"]
    L, D, top_h = P["length_m"], P.get("depth_m", 0.32), P["top_h_m"]
    lw, ld = P.get("leg_m", (0.045, 0.045))
    inset = P.get("leg_inset_m", 0.05)
    top_t = P.get("top_t_m", 0.03)
    stretch_z = P.get("stretcher_z_frac", 0.32) * top_h
    footprint = [(-(L / 2 - inset), -(D / 2 - inset)), (L / 2 - inset, -(D / 2 - inset)),
                (L / 2 - inset, D / 2 - inset), (-(L / 2 - inset), D / 2 - inset)]
    timber_frame(b, {"footprint_m": footprint, "height_m": stretch_z, "post_m": (lw, ld),
                     "beam_h_m": P.get("stretcher_h_m", 0.035), "beam_w_m": P.get("stretcher_w_m", 0.03),
                     "brace": False}, rng, slots, group=group)
    for i, (x, y) in enumerate(footprint):
        pr.member(b, f"leg_top_{i}", (x, y, stretch_z), (x, y, top_h - top_t), X, ld, lw, group=group, slot=wood)
    _board_row(b, "top_", rng, P.get("top_boards", 4), D, P.get("gap_m", 0.0012), L, top_t,
              (0, 0, top_h - top_t / 2), X, Z, Y, group, wood)
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (L, D, top_h)}


# ------------------------------------------------------------------------------------------------ posts and rails (fence)

def posts_and_rails(b, P, rng, slots, group="fence"):
    wood = slots["wood"]
    pts = [np.asarray(p, np.float64) for p in P["posts_m"]]
    post_h, post_w = P.get("post_h_m", 1.1), P.get("post_w_m", 0.09)
    rail_h, rail_w = P.get("rail_h_m", 0.06), P.get("rail_w_m", 0.032)
    rails_z = P.get("rails_z_m", [0.35, 0.78])
    for i, p in enumerate(pts):
        pr.member(b, f"post_{i}", (p[0], p[1], 0.0), (p[0], p[1], post_h), X, post_w, post_w, group=group,
                 slot=wood)
    for i in range(len(pts) - 1):
        for j, z in enumerate(rails_z):
            p0, p1 = pts[i], pts[i + 1]
            pr.member(b, f"rail_{i}_{j}", (p0[0], p0[1], z), (p1[0], p1[1], z), Z, rail_h, rail_w, group=group,
                     slot=wood)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [],
            "dims": (max(xs) - min(xs), max(ys) - min(ys), post_h)}


# ------------------------------------------------------------------------------------------------ rope and chain

def rope_chain(b, name, pts, rng, kind="rope", r_m=0.008, link_r_m=None, group="rope", slot=0, n=8):
    """A sagging rope (round tube along `pts`, pr.catmull-smoothed) or a chain of alternating-plane pr.ring links,
    each spanning one segment of `pts`."""
    if kind == "rope":
        return pr.tube(b, name, pr.catmull(list(pts), step=0.012), r_m, n=n, group=group, slot=slot)
    pts = [np.asarray(p, np.float64) for p in pts]
    for i in range(len(pts) - 1):
        p0, p1 = pts[i], pts[i + 1]
        axis = _unit(p1 - p0)
        helper = Z if abs(axis[2]) < 0.9 else X
        perp1 = _unit(np.cross(axis, helper))
        perp2 = np.cross(axis, perp1)
        rl = link_r_m or (float(np.linalg.norm(p1 - p0)) / 2 * 0.92)
        pr.ring(b, f"{name}_{i}", (p0 + p1) / 2, axis, perp1 if i % 2 == 0 else perp2, rl, circ=r_m, group=group,
               slot=slot)


# ------------------------------------------------------------------------------------------------ pipe / conduit

def pipe_conduit(b, P, rng, slots, group="pipe"):
    pipe, iron = slots["pipe"], slots.get("iron", slots["pipe"])
    pts = [np.asarray(p, np.float64) for p in P["path_m"]]
    r = P.get("radius_m", 0.045)
    pr.tube(b, "pipe", pr.catmull(pts, step=0.02), r, n=P.get("n", 10), group=group, slot=pipe)
    fl_r, fl_t = P.get("flange_r_m", r * 1.8), P.get("flange_t_m", 0.012)
    for end in (0, -1):
        p = pts[end]
        axis = _unit(pts[1] - pts[0]) if end == 0 else _unit(pts[-1] - pts[-2])
        sign = -1.0 if end == 0 else 1.0
        pr.lathe(b, f"flange_{end}", p - axis * fl_t * 0.5 * sign, axis,
                [(0.0, fl_r * 0.55), (fl_t * 0.3, fl_r), (fl_t * 0.7, fl_r), (fl_t, fl_r * 0.55)],
                n=14, group=group, slot=iron)
        e1, e2 = pr.normal_to(axis, Z)
        bolts = [p + axis * fl_t * 0.5 * sign + (math.cos(a) * e1 + math.sin(a) * e2) * fl_r * 0.82
                for a in np.linspace(0, TAU, 6, endpoint=False)]
        rivet_row(b, f"flange_{end}_bolt_", bolts, [axis * sign] * len(bolts), group, iron, r=fl_r * 0.09)
    for i in range(1, len(pts) - 1):
        d0, d1 = _unit(pts[i] - pts[i - 1]), _unit(pts[i + 1] - pts[i])
        axis = _unit(d0 + d1)
        pr.lathe(b, f"collar_{i}", pts[i] - axis * fl_t * 0.6, axis,
                [(0.0, r * 0.98), (fl_t * 0.5, r * 1.22), (fl_t * 1.1, r * 0.98)], n=12, group=group, slot=iron)
    out = {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (0, 0, 0)}
    valve = P.get("valve")
    if valve:
        t = valve.get("at_t", 0.5)
        idx = min(range(len(pts) - 1), key=lambda k: abs((k + 0.5) / (len(pts) - 1) - t))
        p0, p1 = pts[idx], pts[idx + 1]
        axis, centre = _unit(p1 - p0), (p0 + p1) / 2
        body_r, body_len = valve.get("body_r_m", r * 2.3), valve.get("body_len_m", r * 3.2)
        pr.lathe(b, "valve_body", centre - axis * body_len / 2, axis,
                [(0.0, r * 1.02), (body_len * 0.12, body_r), (body_len * 0.88, body_r), (body_len, r * 1.02)],
                n=14, group=group, slot=iron)
        up, _ = pr.normal_to(axis, Z)
        stem_h = valve.get("stem_h_m", body_r * 1.7)
        pr.member(b, "valve_stem", centre + up * body_r, centre + up * (body_r + stem_h), axis, r * 0.45,
                 r * 0.45, group=group, slot=iron)
        wc = centre + up * (body_r + stem_h)
        e1, e2 = pr.normal_to(up, axis)
        wheel_r = valve.get("wheel_r_m", body_r * 1.1)
        pr.ring(b, "valve_wheel", wc, e1, e2, wheel_r, circ=r * 0.4, group=group, slot=iron)
        for k in range(4):
            a = TAU * k / 4
            spoke_end = wc + (math.cos(a) * e1 + math.sin(a) * e2) * wheel_r * 0.9
            pr.member(b, f"valve_spoke_{k}", wc, spoke_end, up, r * 0.3, r * 0.3, group=group, slot=iron)
    return out


# ------------------------------------------------------------------------------------------------ technological housing / panel

def tech_housing(b, P, rng, slots, group="body"):
    case = slots["case"]
    trim = slots.get("trim", case)
    Lx, Ly, Lz = P.get("size_m", (0.4, 0.28, 0.32))
    chamfer = P.get("chamfer_m", 0.012)
    # the case's own frame is centred on its origin, so its extrusion spans z in [-Lz/2, Lz/2] (run_template grounds
    # the whole model afterwards); panel/vent/fastener z positions below are relative to that same centre, not 0
    frame = pr.frame_from((0.0, 0.0, 0.0), X, Z)
    pr.prism(b, "case", frame, pr.chamfer_rect(Lx / 2, Ly / 2, chamfer), Lz, c=chamfer * 0.6, group=group, slot=case)
    # a trim seam loop standing in for a recessed access panel (no boolean cut in this library: a proud, flush-set
    # frame reads as paneling under the bake's material contrast instead). poly_sweep's `h` lands in-plane (the
    # ribbon's visible width, perpendicular to the path) and `w` along bend_axis (here -Y: how far it stands off
    # the case face), so the ribbon width is the h argument and the standoff the w argument, not the reverse.
    panel = P.get("panel", {"present": True})
    if panel.get("present", True):
        pw, ph = panel.get("w_m", Lx * 0.42), panel.get("h_m", Lz * 0.40)
        pz = panel.get("z_m", 0.0)
        y = -Ly / 2
        pts = [(-pw / 2, y, pz - ph / 2), (-pw / 2, y, pz + ph / 2), (pw / 2, y, pz + ph / 2),
              (pw / 2, y, pz - ph / 2)]
        strap_fitting(b, "panel_seam", pts, -Y, 0.010, 0.0018, 0.0005, group, trim, closed=True)
    vents = P.get("vents", {"present": True, "count": 5})
    if vents.get("present", True):
        vn = vents.get("count", 5)
        vw, vh = vents.get("w_m", Lx * 0.3), vents.get("h_m", 0.014)
        vgap = vents.get("gap_m", 0.008)
        vz0 = vents.get("z0_m", -Lz * 0.10)
        vx = vents.get("x_m", Lx * 0.22)
        for i in range(vn):
            zc = vz0 + i * (vh + vgap)
            pr.plank(b, f"vent_{i}", (vx, -Ly / 2 - 0.006, zc), X, -Y, vw, vh, 0.01, group=group, slot=trim)
    fr = P.get("fastener_r_m", 0.006)
    if P.get("fasteners", True):
        cx, cz = Lx / 2 - 0.02, Lz / 2 - 0.02
        pts = [(sx * cx, -Ly / 2 - 0.0005, sz * cz) for sx in (-1, 1) for sz in (-1, 1)]
        rivet_row(b, "corner_", pts, [-Y] * 4, group, trim, r=fr)
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (Lx, Ly, Lz)}


# ------------------------------------------------------------------------------------------------ ancient / alien artifact components

def banded_ring(b, P, rng, slots, group="body"):
    """A torus with a few thin perpendicular bands clamped round the tube at intervals."""
    body = slots["body"]
    trim = slots.get("trim", body)
    R, r = P.get("radius_m", 0.18), P.get("tube_r_m", 0.03)
    E1, E2 = X, Z
    pr.ring(b, "ring", (0, 0, 0), E1, E2, R, circ=r, group=group, slot=body)
    axis = np.cross(E1, E2)
    n = P.get("band_count", 3)
    spread = P.get("band_spread", 0.55)
    phase = P.get("band_phase", 0.4)
    for i in range(n):
        th = phase + (i - (n - 1) / 2) * (TAU * spread / max(n, 1))
        N = E1 * math.cos(th) + E2 * math.sin(th)
        pt = R * N
        pr.ring(b, f"band_{i}", pt, N, axis, r * 1.4, circ=r * 0.32, group=group, slot=trim)
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (2 * (R + r), 2 * (R + r), 2 * r * 1.7)}


def split_lobe(b, P, rng, slots, group="body"):
    """Two parallel bulbous lathed lobes side by side across a narrow gap: a non-obvious twin-pod silhouette."""
    body = slots["body"]
    L, Rm, gap = P.get("length_m", 0.30), P.get("radius_m", 0.11), P.get("gap_m", 0.02)
    profile = [(0.0, 0.05 * Rm), (0.16 * L, 0.85 * Rm), (0.5 * L, Rm), (0.84 * L, 0.72 * Rm), (L, 0.06 * Rm)]
    for s in (-1, 1):
        pr.lathe(b, f"lobe_{'L' if s < 0 else 'R'}", (0.0, s * (Rm + gap / 2), 0.0), X, profile, n=14,
                group=group, slot=body)
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (L, 2 * Rm * 2 + gap, 2 * Rm)}


def stepped_plinth(b, P, rng, slots, group="body"):
    """A pedestal of stacked chamfered prisms, each step's footprint shrinking from the one below."""
    body = slots["body"]
    steps = P.get("steps", 3)
    base_w, base_d = P.get("base_w_m", 0.5), P.get("base_d_m", 0.5)
    step_h, shrink, chamfer = P.get("step_h_m", 0.08), P.get("shrink", 0.82), P.get("chamfer_m", 0.01)
    z = 0.0
    for i in range(steps):
        w, d = base_w * shrink ** i, base_d * shrink ** i
        frame = pr.frame_from((0.0, 0.0, z + step_h / 2), X, Z)
        pr.prism(b, f"step_{i}", frame, pr.chamfer_rect(w / 2, d / 2, chamfer), step_h, c=chamfer * 0.6,
                 group=group, slot=body)
        z += step_h
    return {"nodes": {}, "sockets": [], "bake_lift": {}, "no_clash": [], "dims": (base_w, base_d, steps * step_h)}
